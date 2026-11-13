using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// What the selected object carries: the items kept as references in its content JSON (sounds, textures, animations, objects, notecards…) and its Lua scripts, which live on the
/// server. Items are changed with ordinary updates the server checks (ownership, up to 64); scripts go through the scripting API. Folders are a view over the item kind, and an entry
/// can be moved to another folder (kept in the entry, so it survives a restart).
/// </summary>
public partial class BuildContentManager : Node
{
    public sealed record Entry(Guid Asset, string Name, string Category, string Folder);

    public const int MaxEntries = 64;
    public static readonly string[] Folders = ["Scripts", "Objetos", "Sons", "Animações", "Imagens", "Texturas", "Materiais", "Notecards", "Outros"];

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;
    private ScriptApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;

    public ScriptApi Api => _api ??= new ScriptApi(_transport ?? ScriptApi.HttpTransport(this), () => _sync.IslandId);
    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection) { _sync = sync; _selection = selection; }
    public event Action<ScriptApi>? ApiChanged;
    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new ScriptApi(transport, () => _sync.IslandId); ApiChanged?.Invoke(_api); }

    /// <summary>The only selected object, or null (contents are edited one object at a time).</summary>
    public BuildObjectView? Target => _selection.Count == 1 ? _selection.Views().FirstOrDefault() : null;

    public static string FolderOfCategory(string category) => category switch
    {
        "scripts" => "Scripts", "objects" or "models" or "packages" or "clothing" or "accessories" => "Objetos", "sounds" => "Sons", "animations" => "Animações",
        "textures" => "Texturas", "notecards" => "Notecards", _ => "Outros"
    };

    public static string CategoryLabel(string category) => category switch
    {
        "scripts" => "Script", "textures" => "Textura", "sounds" => "Som", "animations" => "Animação", "models" => "Modelo 3D", "packages" => "Construção",
        "clothing" => "Roupa", "accessories" => "Acessório", "objects" => "Objeto", "notecards" => "Anotação", _ => category
    };

    public static List<Entry> Read(BuildObjectState state)
    {
        var list = new List<Entry>();
        try
        {
            foreach (var item in (JsonNode.Parse(string.IsNullOrWhiteSpace(state.ContentJson) ? "[]" : state.ContentJson) as JsonArray ?? []).OfType<JsonObject>())
            {
                if (!Guid.TryParse(item["assetId"]?.GetValue<string>(), out var asset)) continue;
                var category = item["category"]?.GetValue<string>() ?? "";
                var folder = item["folder"]?.GetValue<string>(); if (folder is null || !Folders.Contains(folder)) folder = FolderOfCategory(category);
                list.Add(new Entry(asset, item["name"]?.GetValue<string>() ?? "Item", category, folder));
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { }
        return list;
    }

    // ---- items -------------------------------------------------------------------------------------------------------------

    /// <summary>Applies a change to the content list of the selected object and sends it. Returns the problem, or null when it was sent.</summary>
    private string? Edit(Func<JsonArray, string?> change)
    {
        if (Target is not { } view) return "Selecione um só objeto para editar o conteúdo.";
        if (!BuildPermissionManager.CanEditContents(view.State)) return "Este objeto não pode ser modificado: o conteúdo dele não muda.";
        var after = BuildPersistenceManager.Clone(view.State);
        JsonArray array; try { array = JsonNode.Parse(string.IsNullOrWhiteSpace(after.ContentJson) ? "[]" : after.ContentJson) as JsonArray ?? []; } catch (JsonException) { array = []; }
        if (change(array) is { } problem) return problem;
        after.ContentJson = array.ToJsonString();
        return _sync.Update(after) ? null : "Sem conexão com a ilha.";
    }

    public string? Add(Guid asset, string name, string category, bool inInventory = true)
    {
        if (Target is { } view && BuildPermissionManager.CannotInsert(view.State, inInventory) is { } refused) return refused;
        if (!BuildContentManager.Folders.Contains(FolderOfCategory(category))) return "Esse tipo de item não cabe num objeto.";
        return Edit(array =>
        {
            if (array.Count >= MaxEntries) return $"Um objeto guarda até {MaxEntries} itens.";
            if (array.OfType<JsonObject>().Any(x => x["assetId"]?.GetValue<string>() == asset.ToString())) return "Esse item já está no objeto.";
            array.Add(new JsonObject { ["assetId"] = asset.ToString(), ["name"] = name.Length > 120 ? name[..120] : name, ["category"] = category }); // only a reference: the rights stay with the item
            return null;
        });
    }

    private static JsonObject? Find(JsonArray array, Guid asset) => array.OfType<JsonObject>().FirstOrDefault(x => x["assetId"]?.GetValue<string>() == asset.ToString());

    public string? Remove(Guid asset) => Edit(array => Find(array, asset) is { } entry ? Drop(array, entry) : "Esse item não está no objeto.");
    private static string? Drop(JsonArray array, JsonObject entry) { array.Remove(entry); return null; }

    public string? Rename(Guid asset, string name)
    {
        name = name.Trim(); if (name.Length is 0 or > 120) return "O nome deve ter de 1 a 120 letras.";
        return Edit(array => { if (Find(array, asset) is not { } entry) return "Esse item não está no objeto."; entry["name"] = name; return null; });
    }

    /// <summary>Moves an entry to another folder of the tree.</summary>
    public string? Move(Guid asset, string folder)
    {
        if (!Folders.Contains(folder)) return "Pasta desconhecida.";
        return Edit(array => { if (Find(array, asset) is not { } entry) return "Esse item não está no objeto."; entry["folder"] = folder; return null; });
    }

    // ---- scripts -----------------------------------------------------------------------------------------------------------

    public async Task<List<ScriptSummary>?> ScriptsAsync() => Target is { } view ? await Api.ListAsync(view.State.Id) : [];

    /// <summary>NOVO SCRIPT: creates a script from the first model in the selected object. Returns the id (or the reason it failed).</summary>
    public async Task<ScriptResult> NewScriptAsync(int existing)
    {
        if (Target is not { } view) return new ScriptResult(false, "Selecione um só objeto para criar um script nele.");
        if (!BuildPermissionManager.CanEditContents(view.State)) return new ScriptResult(false, "Este objeto não pode ser modificado: não recebe scripts.");
        var templates = await Api.TemplatesAsync(); var template = templates.FirstOrDefault(t => t.Source.Length > 0) ?? ScriptApi.DefaultTemplate;
        return await Api.CreateAsync(view.State.Id, $"Novo script {existing + 1}", template.Source, template.EventName);
    }

    public async Task<ScriptResult> RenameScriptAsync(Guid script, string name)
    {
        name = name.Trim(); if (name.Length is 0 or > 120) return new ScriptResult(false, "O nome deve ter de 1 a 120 letras.");
        if (Target is not { } view) return new ScriptResult(false, "Selecione um só objeto.");
        var detail = await Api.GetAsync(view.State.Id, script); if (detail is null) return new ScriptResult(false, "Não foi possível ler o script.");
        var result = await Api.SaveAsync(view.State.Id, script, name, detail.Source); return result.Ok ? new ScriptResult(true, "Script renomeado.", script) : result;
    }

    /// <summary>DUPLICAR: a new script in the same object with the code of the chosen one (the state it kept is not copied). The copy is named after the original.</summary>
    public async Task<ScriptResult> DuplicateScriptAsync(Guid script)
    {
        if (Target is not { } view) return new ScriptResult(false, "Selecione um só objeto.");
        if (!BuildPermissionManager.CanEditContents(view.State)) return new ScriptResult(false, "Este objeto não pode ser modificado: não recebe scripts.");
        var detail = await Api.GetAsync(view.State.Id, script); if (detail is null) return new ScriptResult(false, "Não foi possível ler o script.");
        var name = detail.Name.Length > 110 ? detail.Name[..110] : detail.Name;
        return await Api.CreateAsync(view.State.Id, name + " (cópia)", detail.Source, "on_start");
    }

    public async Task<ScriptResult> DeleteScriptAsync(Guid script) => Target is { } view ? await Api.DeleteAsync(view.State.Id, script) : new ScriptResult(false, "Selecione um só objeto.");

    /// <summary>RESETAR SCRIPTS: restarts every script of the object (the server ends the old runs and starts clean ones from the saved source).</summary>
    public async Task<ScriptResult> ResetScriptsAsync() => Target is { } view ? await Api.ResetAsync(view.State.Id) : new ScriptResult(false, "Selecione um só objeto.");
}
