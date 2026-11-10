using Godot;

namespace LifeVerse.Client.Scripts;

/// The scripts of an object in the Conteúdo tab. A script is a file of Lua that lives inside the object and runs on it in the island server: "Novo script…" creates one
/// (from a model) and opens it in the editor, the list shows the object's scripts next to the other things it carries, and Abrir, Remover and Renomear work on them too.
/// Anything from the inventory can also be dropped on the list.
public partial class BuildEditor
{
    private ScriptApi? _scriptApi;
    private ScriptEditorWindow? _scriptEditor;
    private Func<string, string, string?, Task<(long, string)>>? _scriptTransport;
    private readonly List<ScriptSummary> _scripts = [];
    private readonly List<PanelContainer> _scriptRowNodes = [];
    private Guid _scriptSelected, _scriptsFor;
    private ConfirmationDialog _confirmScript = null!;

    private ScriptApi ScriptsApi => _scriptApi ??= new ScriptApi(_scriptTransport ?? ScriptApi.HttpTransport(this), () => _islandId);

    private void InitScripting()
    {
        _scriptEditor = new ScriptEditorWindow { Name = "ScriptEditorWindow" }; AddChild(_scriptEditor); _scriptEditor.Initialize(ScriptsApi);
        _scriptEditor.Saved += () => _ = LoadScriptsAsync();
        _confirmScript = new ConfirmationDialog { Title = "Remover script", OkButtonText = "Remover", DialogText = "Remover este script do objeto? Ele não volta." }; AddChild(_confirmScript);
        _confirmScript.Confirmed += () => _ = DeleteSelectedScriptAsync();
    }

    public void UseScriptTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _scriptTransport = transport; _scriptApi = new ScriptApi(transport, () => _islandId); _scriptEditor?.UseApi(_scriptApi); }
    public ScriptEditorWindow ScriptEditorForTesting => _scriptEditor!;
    public IReadOnlyList<ScriptSummary> ScriptsForTesting => _scripts;
    public Task LoadScriptsForTesting() => LoadScriptsAsync();
    public Task NewScriptForTesting() => NewScriptAsync();
    public void SelectScriptForTesting(Guid id) => SelectScript(id);
    public Task DeleteSelectedScriptForTesting() => DeleteSelectedScriptAsync();
    public Task RenameSelectedScriptForTesting(string name) => RenameSelectedScriptAsync(name);
    public Task OpenSelectedScriptForTesting() => OpenSelectedScriptAsync();
    public string ContentCountTextForTesting => _contentCount.Text;
    public void DropInventoryItemForTesting(Guid asset, string name, string category) => AddContent(asset, name, category);

    /// Reads the scripts of the selected object and lists them under its other contents.
    private async Task LoadScriptsAsync()
    {
        if (_contentRows is null) return;
        foreach (var node in _scriptRowNodes) { _contentRows.RemoveChild(node); node.QueueFree(); }
        _scriptRowNodes.Clear(); _scripts.Clear();
        if (_selection.Count != 1) { _scriptSelected = Guid.Empty; return; }
        var objectId = _selection[0].State.Id; _scriptsFor = objectId;
        var list = await ScriptsApi.ListAsync(objectId);
        if (list is null || _scriptsFor != objectId || _selection.Count != 1 || _selection[0].State.Id != objectId) return; // the selection moved on while the answer came
        foreach (var node in _scriptRowNodes) { _contentRows.RemoveChild(node); node.QueueFree(); }
        _scriptRowNodes.Clear(); _scripts.Clear(); _scripts.AddRange(list);
        foreach (var script in list) AddScriptRow(script);
        var items = Entries().Count; var total = items + list.Count;
        _contentCount.Text = total == 1 ? "1 item" : $"{total} itens";
        if (_scriptSelected != Guid.Empty && !list.Any(s => s.Id == _scriptSelected)) _scriptSelected = Guid.Empty;
        if (total > 0) _contentInfo.Text = $"{items} item(ns) guardado(s) e {list.Count} script(s) rodando neste objeto.";
    }

    private void AddScriptRow(ScriptSummary script)
    {
        var row = PanelWith(ContentRowBox(script.Id == _scriptSelected)); row.MouseFilter = Control.MouseFilterEnum.Stop;
        var line = Row(8); row.AddChild(line);
        line.AddChild(UiStyle.IconRect("code", UiStyle.AccentText, 24));
        var title = Txt(script.Name, 19, true); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; line.AddChild(title);
        var kind = Txt(script.Status == "active" ? "Script" : "Script (parado)", 18); kind.CustomMinimumSize = new Vector2(190, 0); line.AddChild(kind);
        var version = Txt($"v{script.Version}", 18, true, UiStyle.AccentText); version.CustomMinimumSize = new Vector2(80, 0); line.AddChild(version);
        var id = script.Id; row.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) { SelectScript(id); if (click.DoubleClick) _ = OpenSelectedScriptAsync(); } };
        _scriptRowNodes.Add(row); _contentRows.AddChild(row);
    }

    private void SelectScript(Guid id)
    {
        _scriptSelected = id; _contentSelected = -1;
        for (var i = 0; i < _contentRowNodes.Count; i++) _contentRowNodes[i].AddThemeStyleboxOverride("panel", ContentRowBox(false));
        for (var i = 0; i < _scriptRowNodes.Count && i < _scripts.Count; i++) _scriptRowNodes[i].AddThemeStyleboxOverride("panel", ContentRowBox(_scripts[i].Id == id));
        if (_scripts.FirstOrDefault(s => s.Id == id) is { } script) _contentName.Text = script.Name;
    }

    /// "Novo script…": a script from the first model, created in the object and opened in the editor.
    private async Task NewScriptAsync()
    {
        if (_selection.Count != 1) { _status.Text = "Selecione um só objeto para criar um script nele."; return; }
        var view = _selection[0]; var templates = await ScriptsApi.TemplatesAsync(); var template = templates.FirstOrDefault(t => t.Source.Length > 0) ?? ScriptApi.DefaultTemplate;
        var name = $"Novo script {_scripts.Count + 1}";
        var result = await ScriptsApi.CreateAsync(view.State.Id, name, template.Source, template.EventName);
        if (!result.Ok) { _status.Text = result.Message; return; }
        _status.Text = "Script criado: edite e salve."; await LoadScriptsAsync(); SelectScript(result.Id);
        if (_scriptEditor is not null) await _scriptEditor.OpenAsync(view.State.Id, result.Id);
    }

    private async Task OpenSelectedScriptAsync()
    {
        if (_scriptSelected == Guid.Empty || _selection.Count != 1 || _scriptEditor is null) { _status.Text = "Escolha um script da lista."; return; }
        await _scriptEditor.OpenAsync(_selection[0].State.Id, _scriptSelected);
    }

    private async Task DeleteSelectedScriptAsync()
    {
        if (_scriptSelected == Guid.Empty || _selection.Count != 1) return;
        var result = await ScriptsApi.DeleteAsync(_selection[0].State.Id, _scriptSelected); _status.Text = result.Message;
        if (result.Ok) { if (_scriptEditor is { IsOpen: true } editor && editor.ScriptIdForTesting == _scriptSelected) editor.Close(); _scriptSelected = Guid.Empty; await LoadScriptsAsync(); }
    }

    private async Task RenameSelectedScriptAsync(string name)
    {
        name = name.Trim(); if (name.Length is 0 or > 120) { _status.Text = "O nome deve ter de 1 a 120 letras."; return; }
        if (_scriptSelected == Guid.Empty || _selection.Count != 1) return;
        var objectId = _selection[0].State.Id; var detail = await ScriptsApi.GetAsync(objectId, _scriptSelected);
        if (detail is null) { _status.Text = "Não foi possível ler o script."; return; }
        var result = await ScriptsApi.SaveAsync(objectId, _scriptSelected, name, detail.Source); _status.Text = result.Ok ? "Script renomeado." : result.Message;
        if (result.Ok) await LoadScriptsAsync();
    }

    /// Remover: the selected script (after asking) or the selected thing the object carries.
    private void RemoveSelectedContent() { if (_scriptSelected != Guid.Empty) _confirmScript.PopupCentered(); else RemoveContent(); }

    /// Anything from the inventory can be dropped on the Conteúdo list: it goes into the object like "Inserir do inventário…".
    private void EnableContentDrop(Control area)
    {
        area.SetDragForwarding(default, Callable.From<Vector2, Variant, bool>((_, data) => DropItem(data) is not null), Callable.From<Vector2, Variant>((_, data) => { if (DropItem(data) is { } item) AddContent(item.Asset, item.Name, item.Category); }));
    }

    private (Guid Asset, string Name, string Category)? DropItem(Variant data)
    {
        if (_selection.Count != 1 || data.VariantType != Variant.Type.Dictionary) return null;
        var dictionary = data.AsGodotDictionary();
        if (!dictionary.TryGetValue("type", out var type) || type.AsString() != "inventory_item" || !dictionary.TryGetValue("itemId", out var id) || !Guid.TryParse(id.AsString(), out var itemId)) return null;
        return InventoryOf()?.DescribeItem(itemId);
    }
}
