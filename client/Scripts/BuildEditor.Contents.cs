using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// The Conteúdo tab: the things an object carries — scripts, textures, sounds, animations, other objects — as references to
/// items in the owner's inventory, kept in the object's content JSON. The object holds references only, never the data itself,
/// and the server checks that the person adding an item really has it.
public partial class BuildEditor
{
    private LineEdit _contentName = null!;
    private Label _contentInfo = null!;
    private ItemList _picker = null!;
    private AcceptDialog _pickerDialog = null!;
    private HttpRequest? _pickerHttp;
    private readonly List<(Guid Asset, string Name, string Category)> _pickerItems = [];

    private List<JsonObject> Entries()
    {
        if (_selection.Count != 1) return [];
        try { return (JsonNode.Parse(string.IsNullOrWhiteSpace(_selection[0].State.ContentJson) ? "[]" : _selection[0].State.ContentJson) as JsonArray ?? []).OfType<JsonObject>().ToList(); }
        catch (JsonException) { return []; }
    }

    private void FillContent()
    {
        if (_contentRows is null) return;
        foreach (var node in _contentRowNodes) { _contentRows.RemoveChild(node); node.QueueFree(); }
        _contentRowNodes.Clear(); _contentTexts.Clear(); _contentSelected = -1;
        if (_selection.Count != 1) { _contentCount.Text = "0 itens"; _contentInfo.Text = _selection.Count == 0 ? "Selecione um objeto." : "Selecione um só objeto para ver o conteúdo."; return; }
        var entries = Entries();
        for (var i = 0; i < entries.Count; i++)
        {
            var name = entries[i]["name"]?.GetValue<string>() ?? "Item"; var category = entries[i]["category"]?.GetValue<string>() ?? "";
            var asset = Guid.TryParse(entries[i]["assetId"]?.GetValue<string>(), out var parsed) ? parsed : Guid.Empty; var rights = InventoryOf()?.OwnerRightsOfAsset(asset) is { } mask ? LifeVerse.Shared.Items.ItemRights.Letters(mask) : "—";
            _contentTexts.Add($"{name}  ·  {CategoryLabel(category)}"); AddContentRow(i, name, category, asset, rights);
        }
        _contentCount.Text = entries.Count == 1 ? "1 item" : $"{entries.Count} itens";
        _ = LoadScriptsAsync();
        _contentInfo.Text = entries.Count == 0 ? "Este objeto está vazio." : $"{entries.Count} item(ns) de até 64.";
    }

    private static string CategoryLabel(string category) => category switch
    {
        "scripts" => "Script", "textures" => "Textura", "sounds" => "Som", "animations" => "Animação", "models" => "Modelo 3D", "packages" => "Construção",
        "clothing" => "Roupa", "accessories" => "Acessório", "objects" => "Objeto", "notecards" => "Anotação", _ => category
    };

    private bool EditContent(Func<JsonArray, bool> change, string done)
    {
        if (_selection.Count != 1) { _status.Text = "Selecione um só objeto para editar o conteúdo."; return false; }
        var after = FromView(_selection[0]);
        JsonArray array;
        try { array = JsonNode.Parse(string.IsNullOrWhiteSpace(after.ContentJson) ? "[]" : after.ContentJson) as JsonArray ?? []; } catch (JsonException) { array = []; }
        if (!change(array)) return false;
        after.ContentJson = array.ToJsonString();
        CommitUpdates([after]);
        _status.Text = done;
        return true;
    }

    private void AddContent(Guid asset, string name, string category)
    {
        EditContent(array =>
        {
            if (array.Count >= 64) { _status.Text = "Um objeto guarda até 64 itens."; return false; }
            if (array.OfType<JsonObject>().Any(x => x["assetId"]?.GetValue<string>() == asset.ToString())) { _status.Text = "Esse item já está no objeto."; return false; }
            array.Add(new JsonObject { ["assetId"] = asset.ToString(), ["name"] = name.Length > 120 ? name[..120] : name, ["category"] = category });
            return true;
        }, $"\"{name}\" guardado no objeto.");
    }

    private void RemoveContent()
    {
        var index = _contentSelected;
        if (index < 0) { _status.Text = "Escolha um item da lista para remover."; return; }
        EditContent(array => { if (index >= array.Count) return false; array.RemoveAt(index); return true; }, "Item removido do objeto.");
    }

    private void RenameContent()
    {
        var name = _contentName.Text.Trim();
        if (_contentSelected < 0) { _status.Text = "Escolha um item da lista para renomear."; return; }
        if (name.Length is 0 or > 120) { _status.Text = "O nome deve ter de 1 a 120 letras."; return; }
        var index = _contentSelected;
        EditContent(array => { if (index >= array.Count || array[index] is not JsonObject entry) return false; entry["name"] = name; return true; }, "Item renomeado.");
    }

    // ---- picking from the inventory --------------------------------------------------------------------------

    private void OpenPicker()
    {
        if (_selection.Count != 1) { _status.Text = "Selecione um só objeto primeiro."; return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated) { _status.Text = "Entre na sua conta para ver o inventário."; return; }
        _picker.Clear(); _pickerItems.Clear();
        _pickerDialog.PopupCentered();
        if (_pickerHttp is null) { _pickerHttp = new HttpRequest { Timeout = 20 }; AddChild(_pickerHttp); _pickerHttp.RequestCompleted += (_, code, _, body) => { if (code is >= 200 and < 300) FillPicker(body.GetStringFromUtf8()); else _status.Text = $"Não foi possível abrir o inventário (HTTP {code})."; }; }
        if (_pickerHttp.GetHttpClientStatus() == Godot.HttpClient.Status.Disconnected) _pickerHttp.Request($"{app.ApiBaseUrl}/api/v1/inventory/items?page=1&pageSize=100", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    /// Lists the inventory answer ({ items: [...] }) in the picker; only kinds an object can hold are shown.
    public void FillPicker(string json)
    {
        _picker.Clear(); _pickerItems.Clear();
        var allowed = new[] { "objects", "textures", "sounds", "animations", "clothing", "accessories", "models", "packages", "scripts", "notecards" };
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("items", out var items)) return;
            foreach (var item in items.EnumerateArray())
            {
                var category = item.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                if (!allowed.Contains(category) || !item.TryGetProperty("assetId", out var a) || !Guid.TryParse(a.GetString(), out var asset)) continue;
                if (item.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True) continue;
                var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "Item" : "Item";
                _pickerItems.Add((asset, name, category)); _picker.AddItem($"{name}  ·  {CategoryLabel(category)}");
            }
        }
        catch (JsonException) { _status.Text = "Resposta de inventário inválida."; }
    }

    public int ContentCountForTesting => Entries().Count;
    public int ContentRowsForTesting => _contentRowNodes.Count;
    public int PickerCountForTesting => _pickerItems.Count;
    public void AddContentForTesting(Guid asset, string name, string category) => AddContent(asset, name, category);
    public void RenameContentForTesting(int index, string name) { SelectContent(index); _contentName.Text = name; RenameContent(); }
    public void RemoveContentForTesting(int index) { SelectContent(index); RemoveContent(); }
    public string ContentTextForTesting(int index) => _contentTexts[index];
}

public partial class BuildEditor
{
    private InventoryPanel? InventoryOf() => GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel");

    /// Open, properties, copy or transfer of a thing the object carries: they are done on the inventory item it refers to, with the same rules and windows.
    private void RunContentAction(Guid asset, string action)
    {
        if (InventoryOf() is not { } inventory) { _status.Text = "O inventário não está disponível aqui."; return; }
        var problem = inventory.RunOnAsset(asset, action);
        _status.Text = problem ?? action switch { "open" => "Item aberto.", "properties" => "Propriedades do item.", "copy" => "Cópia pedida ao inventário.", _ => "Escolha para quem transferir." };
        LastContentActionForTesting = problem ?? "ok:" + action;
    }

    public string LastContentActionForTesting { get; private set; } = string.Empty;
    public void RunContentActionForTesting(int index, string action) { var entries = Entries(); if (index < entries.Count && Guid.TryParse(entries[index]["assetId"]?.GetValue<string>(), out var asset)) RunContentAction(asset, action); }
}
