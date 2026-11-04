using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>One uploaded body model as the server lists it.</summary>
public sealed record ModelEntry(Guid Id, string Name, int Triangles, int Joints, bool Active);

/// <summary>
/// The "Corpo personalizado" section of the character editor: import a GLB, see why it was refused or what to watch out for, choose which model
/// the avatar wears (or go back to the standard body), delete models, and save the reference model that shows the bones the game expects.
/// </summary>
public partial class CharacterCreator
{
    private (Guid? Id, string? Hash, float Scale, string Bones) _custom = (null, null, 1f, "{}");
    private readonly List<ModelEntry> _modelEntries = [];
    private ItemList _models = null!;
    private Label _modelMessage = null!;
    private FileDialog? _openDialog, _saveDialog;

    public IReadOnlyList<ModelEntry> Models => _modelEntries;

    private void BuildModelsManager(VBoxContainer fields)
    {
        fields.AddChild(new Label { Text = "Corpo personalizado (GLB)" });
        var hint = new Label { Text = "Importe um humanoide com esqueleto em um único arquivo .glb. O servidor confere os ossos, os pesos e as proporções antes de aceitar.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) };
        hint.AddThemeFontSizeOverride("font_size", 14); fields.AddChild(hint);
        var top = new HBoxContainer(); fields.AddChild(top);
        Button Add(HBoxContainer row, string text, System.Action pressed) { var b = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill }; b.Pressed += pressed; row.AddChild(b); return b; }
        Add(top, "Importar modelo…", PickModel); Add(top, "Salvar modelo de referência…", PickReferenceSave);
        _models = new ItemList { Name = "Models", CustomMinimumSize = new Vector2(0, 100), SizeFlagsHorizontal = SizeFlags.ExpandFill }; fields.AddChild(_models);
        var row = new HBoxContainer(); fields.AddChild(row);
        Add(row, "Usar este corpo", UseModel); Add(row, "Corpo padrão", () => UseModelId(null, "Voltou ao corpo padrão.")); Add(row, "Excluir", DeleteModel);
        _modelMessage = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) }; fields.AddChild(_modelMessage);
    }

    private ModelEntry? SelectedModel() { var picked = _models.GetSelectedItems(); return picked.Length == 0 || picked[0] >= _modelEntries.Count ? null : _modelEntries[picked[0]]; }

    public void LoadModels() => _manager.Call(Godot.HttpClient.Method.Get, "/models", null, (status, body) => { if (status is >= 200 and < 300) ReadModels(body); });

    private void ReadModels(string body)
    {
        _modelEntries.Clear(); _models.Clear();
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!Guid.TryParse(item.TryGetProperty("id", out var i) ? i.GetString() : null, out var id)) continue;
                var entry = new ModelEntry(id, item.TryGetProperty("name", out var n) ? n.GetString() ?? "Modelo" : "Modelo", item.TryGetProperty("triangles", out var t) ? t.GetInt32() : 0, item.TryGetProperty("joints", out var j) ? j.GetInt32() : 0, item.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True);
                _modelEntries.Add(entry);
                _models.AddItem($"{(entry.Active ? "● " : string.Empty)}{entry.Name} · {entry.Triangles:N0} triângulos · {entry.Joints} ossos");
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { _modelEntries.Clear(); _models.Clear(); }
    }

    private void PickModel()
    {
        _openDialog ??= MakeDialog(FileDialog.FileModeEnum.OpenFile, "Importar corpo (.glb)", path => { try { Import(System.IO.Path.GetFileName(path), System.IO.File.ReadAllBytes(path)); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { _modelMessage.Text = "Não foi possível ler esse arquivo."; } });
        _openDialog.PopupCentered(new Vector2I(900, 560));
    }

    private void PickReferenceSave()
    {
        _saveDialog ??= MakeDialog(FileDialog.FileModeEnum.SaveFile, "Salvar modelo de referência", path => { try { System.IO.File.WriteAllBytes(path, HumanoidTemplate.Build()); _modelMessage.Text = "Modelo de referência salvo. Ele mostra os ossos, a pose em T e as proporções esperadas."; } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { _modelMessage.Text = "Não foi possível salvar nesse lugar."; } });
        _saveDialog.CurrentFile = "lifeverse-humanoide.glb"; _saveDialog.PopupCentered(new Vector2I(900, 560));
    }

    private FileDialog MakeDialog(FileDialog.FileModeEnum mode, string title, System.Action<string> picked)
    {
        var dialog = new FileDialog { FileMode = mode, Access = FileDialog.AccessEnum.Filesystem, Title = title, UseNativeDialog = false };
        dialog.AddFilter("*.glb", "Modelo 3D (GLB)"); dialog.FileSelected += path => picked(path); AddChild(dialog); return dialog;
    }

    /// <summary>Sends a GLB to the server and shows what came back: the new model, or the reasons it was refused.</summary>
    public void Import(string fileName, byte[] bytes)
    {
        if (!fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) { _modelMessage.Text = "Use um arquivo .glb com tudo dentro (ossos, malha e texturas)."; return; }
        _modelMessage.Text = "Enviando e conferindo o modelo…";
        _manager.Upload("/models", fileName, bytes, new Dictionary<string, string> { ["name"] = System.IO.Path.GetFileNameWithoutExtension(fileName) }, (status, body) =>
        {
            if (status is >= 200 and < 300) { _modelMessage.Text = Describe("Modelo aceito.", body); LoadModels(); return; }
            _modelMessage.Text = status == 422 ? Describe("O modelo foi recusado:", body) : ModelMessage(status, body);
        });
    }

    private void UseModel()
    {
        if (SelectedModel() is not { } model) { _modelMessage.Text = "Escolha um modelo na lista."; return; }
        UseModelId(model.Id, $"Agora você usa o corpo “{model.Name}”.");
    }

    private void UseModelId(Guid? id, string done)
    {
        _manager.Call(id is null ? Godot.HttpClient.Method.Delete : Godot.HttpClient.Method.Post, id is null ? "/models/use" : $"/models/{id}/use", id is null ? null : "{}", (status, body) =>
        {
            if (status is < 200 or >= 300) { _modelMessage.Text = ModelMessage(status, body); return; }
            _manager.Call(Godot.HttpClient.Method.Get, string.Empty, null, (s, text) =>
            {
                if (s is >= 200 and < 300 && AvatarManager.ParseAppearance(text) is { } value) { _manager.SetCurrent(value); _lastSaved = value.Copy(); _custom = (value.CustomModelId, value.CustomModelHash, value.CustomModelScale, value.CustomModelBonesJson); RefreshPreview(); }
                _modelMessage.Text = done; LoadModels();
            });
        });
    }

    private void DeleteModel()
    {
        if (SelectedModel() is not { } model) { _modelMessage.Text = "Escolha um modelo na lista."; return; }
        _manager.Call(Godot.HttpClient.Method.Delete, $"/models/{model.Id}", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { _modelMessage.Text = ModelMessage(status, body); return; }
            if (_custom.Id == model.Id) { _custom = (null, null, 1f, "{}"); RefreshPreview(); }
            _modelMessage.Text = $"Modelo “{model.Name}” excluído."; LoadModels();
        });
    }

    /// <summary>The checks of the server explained in plain words: first the problems, then what to watch out for.</summary>
    private static string Describe(string title, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement; var report = root.TryGetProperty("report", out var r) ? r : root;
            var lines = new List<string> { title };
            if (report.ValueKind == JsonValueKind.Object && report.TryGetProperty("issues", out var issues))
                foreach (var issue in issues.EnumerateArray())
                {
                    var code = issue.TryGetProperty("code", out var c) ? c.GetString() ?? string.Empty : string.Empty; var error = issue.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.True;
                    lines.Add((error ? "✖ " : "• ") + ModelIssueText(code, issue.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty));
                }
            if (report.ValueKind == JsonValueKind.Object && report.TryGetProperty("triangles", out var tri) && report.TryGetProperty("height", out var h) && report.TryGetProperty("joints", out var jo)) lines.Add($"{tri.GetInt32():N0} triângulos, {jo.GetInt32()} ossos, {h.GetSingle():0.##} de altura.");
            return string.Join("\n", lines);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { return title; }
    }

    private static string ModelIssueText(string code, string message) => code switch
    {
        "missing_bones" => "Faltam ossos do humanoide. " + message,
        "bad_hierarchy" => "Os ossos não estão na ordem de um humanoide. " + message,
        "not_upright" => "O esqueleto não está em pé no eixo Y (cabeça acima do quadril, acima dos pés).",
        "bad_proportions" => "As proporções não parecem as de uma pessoa. " + message,
        "unskinned_mesh" => "Uma malha ligada ao esqueleto não tem pesos de osso.",
        "bad_weights" => "Os pesos dos ossos não somam 1. Normalize os pesos antes de exportar.",
        "bad_joint_index" => "Há pesos apontando para ossos que não existem.",
        "too_many_triangles" or "too_many_vertices" => "O modelo é pesado demais para um avatar. " + message,
        "too_many_joints" or "too_many_materials" or "too_many_textures" => message,
        "external_reference" => "O modelo aponta para outros arquivos. Exporte em um único .glb com tudo embutido.",
        "unsupported_extension" => message,
        "no_skin" or "no_skinned_mesh" or "multiple_skins" => message,
        "animations_ignored" => "As animações do arquivo são ignoradas; o avatar usa as do jogo.",
        "rigid_mesh" => "Uma malha não está ligada ao esqueleto e não vai acompanhar o corpo.",
        "extra_influences" => "Alguns vértices usam mais de 4 ossos; só os 4 mais fortes valem.",
        "no_chest" => "Não há osso de peito; a coluna faz o papel dele.",
        "odd_size" => "As unidades do modelo parecem não ser metros; ele será escalado para a altura padrão.",
        "asymmetric" => "O lado esquerdo e o direito do esqueleto não são espelhados.",
        "bad_glb" or "bad_json" or "bad_version" or "empty" => message,
        _ => message
    };

    private static string ModelMessage(int status, string body)
    {
        var error = string.Empty;
        try { using var document = JsonDocument.Parse(body); error = document.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty; } catch (JsonException) { }
        return error switch
        {
            "too_many_models" => "O limite é de 5 modelos. Exclua algum antes.",
            "too_big" => $"O arquivo passa de {AvatarModelValidator.MaxBytes / 1024 / 1024} MB.",
            "bad_file" => "Use um arquivo .glb.",
            "not_found" => "Modelo não encontrado.",
            _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível concluir (HTTP {status})."
        };
    }

    // ---- hooks for tests ------------------------------------------------------------------------------------------------------------

    public string ModelMessageForTesting => _modelMessage.Text;
    public void SelectModelForTesting(int index) { _models.Select(index); }
    public void UseModelForTesting() => UseModel();
    public void StandardBodyForTesting() => UseModelId(null, "Voltou ao corpo padrão.");
    public void DeleteModelForTesting() => DeleteModel();
    public Guid? CustomIdForTesting => _custom.Id;
}
