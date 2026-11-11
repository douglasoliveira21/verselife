using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The CONTEÚDO tab: what the object carries, in a tree of folders (Scripts, Objetos, Sons, Animações, Imagens, Texturas, Materiais, Notecards, Outros). NOVO SCRIPT creates a Lua script in the
/// object and opens it in the editor (with Validar and Ctrl+S); PERMISSÕES shows what the selected item allows; ATUALIZAR CONTEÚDO reads the scripts again from the server without
/// touching what is open in the editor; RESETAR SCRIPTS restarts the scripts from their saved source. Items come from the inventory (button or drag and drop) as references.
/// </summary>
public partial class BuildContentTab : VBoxContainer
{
    private BuildSelectionManager _selection = null!;
    private BuildContentManager _content = null!;
    private Tree _tree = null!;
    private Label _info = null!, _count = null!;
    private LineEdit _name = null!;
    private OptionButton _folder = null!;
    private ScriptEditorWindow _editor = null!;
    private ConfirmationDialog _confirmRemove = null!, _confirmReset = null!, _picker = null!;
    private ItemList _pickerList = null!;
    private HttpRequest? _http;
    private readonly List<(Guid Asset, string Name, string Category)> _pickerItems = [];
    private readonly List<ScriptSummary> _scripts = [];
    private Guid _scriptsFor;
    private string _pickedKey = string.Empty;

    public event Action<string>? Said;

    private InventoryPanel? Inventory => GetTree()?.Root.FindChild("InventoryPanel", true, false) as InventoryPanel ?? GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel");

    // ---- test access -------------------------------------------------------------------------------------------------------

    public ScriptEditorWindow EditorForTesting => _editor;
    public string InfoForTesting => _info.Text;
    public string CountForTesting => _count.Text;
    public IReadOnlyList<string> FoldersForTesting => [.. Children().Select(i => i.GetText(0))];
    public IReadOnlyList<string> ItemsOfForTesting(string folder) => [.. Children().Where(i => i.GetText(0) == folder).SelectMany(i => i.GetChildren()).Select(i => i.GetText(0))];
    public int PickerCountForTesting => _pickerItems.Count;
    public void SelectForTesting(string key) => SelectKey(key);
    public string SelectedKeyForTesting => _pickedKey;
    public void FillPickerForTesting(string json) => FillPicker(json);
    public void RenameForTesting(string name) { _name.Text = name; Rename(); }
    public void MoveForTesting(string folder) { for (var i = 0; i < _folder.ItemCount; i++) if (_folder.GetItemText(i) == folder) _folder.Select(i); MoveSelected(); }
    public Task NewScriptForTesting() => NewScriptAsync();
    public Task RefreshForTesting() => RefreshAsync();
    public Task ResetForTesting() => ResetAsync();
    public void ShowPermissionsForTesting() => ShowPermissions();
    public void RemoveForTesting() => RemoveNow();
    public void DropForTesting(Guid asset, string name, string category) => Said?.Invoke(_content.Add(asset, name, category) ?? $"\"{name}\" guardado no objeto.");
    public Task RemoveScriptForTesting() => RemoveScriptAsync();

    public string Key(bool script, Guid id) => (script ? "script:" : "item:") + id;

    private IEnumerable<TreeItem> Children() { var root = _tree.GetRoot(); if (root is null) yield break; for (var item = root.GetFirstChild(); item is not null; item = item.GetNext()) yield return item; }

    public void Initialize(BuildSelectionManager selection, BuildContentManager content, Node editorHost)
    {
        _selection = selection; _content = content;
        _editor = new ScriptEditorWindow { Name = "ScriptEditorWindow" }; editorHost.AddChild(_editor); _editor.Initialize(content.Api); content.ApiChanged += _editor.UseApi; _editor.Saved += () => _ = RefreshAsync();
    }

    // ---- building ----------------------------------------------------------------------------------------------------------

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var top = new GridContainer { Columns = 2 }; top.AddThemeConstantOverride("h_separation", 6); top.AddThemeConstantOverride("v_separation", 6); AddChild(top);
        Button Big(string icon, string text, string tip, Action action) { var b = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 20), TooltipText = tip, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 42), SizeFlagsHorizontal = SizeFlags.ExpandFill }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Secondary, 8, 6, 10); b.AddThemeFontSizeOverride("font_size", 16); b.Pressed += action; top.AddChild(b); return b; }
        Big("code", "NOVO SCRIPT", "Cria um script Lua neste objeto e abre o editor", () => _ = NewScriptAsync());
        Big("lock", "PERMISSÕES", "O que o item escolhido permite (copiar, modificar, transferir) e o que o objeto permite", ShowPermissions);
        Big("refresh", "ATUALIZAR CONTEÚDO", "Lê de novo os scripts do servidor; o que está aberto no editor continua como está", () => _ = RefreshAsync());
        Big("play", "RESETAR SCRIPTS", "Reinicia os scripts do objeto a partir do código salvo (o código nunca é apagado)", () => _confirmReset.PopupCentered());

        _count = new Label { Text = "0 itens" }; _count.AddThemeFontSizeOverride("font_size", 16); _count.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_count);
        _tree = new Tree { HideRoot = true, CustomMinimumSize = new Vector2(0, 190), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Single };
        _tree.AddThemeFontSizeOverride("font_size", 16); _tree.ItemSelected += () => { if (_tree.GetSelected() is { } item && item.GetMetadata(0).VariantType == Variant.Type.String) { _pickedKey = item.GetMetadata(0).AsString(); _name.Text = item.GetText(0).Split("  ·  ")[0]; } else _pickedKey = string.Empty; };
        _tree.ItemActivated += () => OpenSelected(); AddChild(_tree);
        _tree.SetDragForwarding(default, Callable.From<Vector2, Variant, bool>((_, data) => DropItem(data) is not null), Callable.From<Vector2, Variant>((_, data) => { if (DropItem(data) is { } item) Said?.Invoke(_content.Add(item.Asset, item.Name, item.Category) ?? $"\"{item.Name}\" guardado no objeto."); }));

        var row1 = new HBoxContainer(); row1.AddThemeConstantOverride("separation", 6); AddChild(row1);
        Small(row1, "Inserir do inventário…", "Escolhe um item do seu inventário (ou arraste-o para a lista)", OpenPicker); Small(row1, "Abrir", "Abre o script no editor ou o item no inventário", OpenSelected); Small(row1, "Remover", "Tira o item (ou apaga o script) do objeto", () => { if (_pickedKey.Length > 0) _confirmRemove.PopupCentered(); else Said?.Invoke("Escolha um item da lista para remover."); });
        _name = new LineEdit { PlaceholderText = "Novo nome", MaxLength = 120, SizeFlagsHorizontal = SizeFlags.ExpandFill }; AddChild(_name);
        var row2 = new HBoxContainer(); row2.AddThemeConstantOverride("separation", 6); AddChild(row2);
        Small(row2, "Renomear", "Muda o nome do item escolhido", Rename); Small(row2, "Copiar", "Pede uma cópia do item ao inventário (precisa do direito de copiar)", () => ItemAction("copy")); Small(row2, "Propriedades", "Abre as propriedades do item no inventário", () => ItemAction("properties"));
        var row3 = new HBoxContainer(); row3.AddThemeConstantOverride("separation", 6); AddChild(row3);
        _folder = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, TooltipText = "Pasta de destino" }; foreach (var folder in BuildContentManager.Folders) _folder.AddItem(folder); row3.AddChild(_folder);
        Small(row3, "Mover para a pasta", "Muda o item de pasta", MoveSelected);
        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, Text = "Selecione um objeto." }; _info.AddThemeFontSizeOverride("font_size", 16); _info.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_info);

        _confirmRemove = new ConfirmationDialog { Title = "Remover do objeto", OkButtonText = "Remover", DialogText = "Remover o item escolhido do objeto? Um script apagado não volta." }; AddChild(_confirmRemove); _confirmRemove.Confirmed += RemoveNow;
        _confirmReset = new ConfirmationDialog { Title = "Resetar scripts", OkButtonText = "Resetar", DialogText = "Reiniciar todos os scripts deste objeto? O código fica; o estado deles é zerado." }; AddChild(_confirmReset); _confirmReset.Confirmed += () => _ = ResetAsync();
        _picker = new ConfirmationDialog { Title = "Inserir do inventário", OkButtonText = "Inserir", MinSize = new Vector2I(520, 420) }; AddChild(_picker); _picker.Confirmed += InsertPicked;
        _pickerList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(480, 340) }; _picker.AddChild(_pickerList);
        Refresh();
    }

    private static void Small(HBoxContainer row, string text, string tip, Action action)
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = SizeFlags.ExpandFill }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Secondary, 8, 5, 10); b.AddThemeFontSizeOverride("font_size", 15); b.Pressed += action; row.AddChild(b);
    }

    // ---- showing -----------------------------------------------------------------------------------------------------------

    /// <summary>Redraws the tree from the object's content and the last scripts read; scripts are fetched again with ATUALIZAR.</summary>
    public void Refresh()
    {
        if (_tree is null || _content is null) return;
        var keep = _pickedKey; _tree.Clear(); var root = _tree.CreateItem(); _pickedKey = string.Empty;
        var view = _content.Target;
        if (view is null) { _count.Text = "0 itens"; _info.Text = _selection.Count == 0 ? "Selecione um objeto." : "Selecione um só objeto para ver o conteúdo."; _scripts.Clear(); _scriptsFor = Guid.Empty; return; }
        if (_scriptsFor != view.State.Id) { _scripts.Clear(); _scriptsFor = view.State.Id; _ = LoadScriptsAsync(view.State.Id); }
        var entries = BuildContentManager.Read(view.State); var folders = new Dictionary<string, TreeItem>();
        TreeItem FolderItem(string name) { if (!folders.TryGetValue(name, out var item)) { item = _tree.CreateItem(root); item.SetText(0, name); item.SetSelectable(0, false); folders[name] = item; } return item; }
        foreach (var name in BuildContentManager.Folders) if (name == "Scripts" ? _scripts.Count > 0 : entries.Any(e => e.Folder == name)) FolderItem(name);
        foreach (var script in _scripts) { var item = _tree.CreateItem(FolderItem("Scripts")); item.SetText(0, $"{script.Name}  ·  Script v{script.Version}{(script.Status == "active" ? "" : " (parado)")}"); item.SetMetadata(0, Key(true, script.Id)); item.SetIcon(0, UiStyle.Icon("code", UiStyle.AccentText, 18)); }
        foreach (var entry in entries) { var item = _tree.CreateItem(FolderItem(entry.Folder)); item.SetText(0, $"{entry.Name}  ·  {BuildContentManager.CategoryLabel(entry.Category)}"); item.SetMetadata(0, Key(false, entry.Asset)); }
        var total = entries.Count + _scripts.Count; _count.Text = total == 1 ? "1 item" : $"{total} itens";
        _info.Text = !BuildPermissionManager.CanEditContents(view.State) ? "Este objeto não pode ser modificado: o conteúdo só pode ser visto." : total == 0 ? "Este objeto está vazio. Crie um script ou insira um item." : $"{entries.Count} item(ns) guardado(s) e {_scripts.Count} script(s) neste objeto.";
        if (keep.Length > 0) SelectKey(keep);
    }

    private async Task LoadScriptsAsync(Guid objectId)
    {
        var list = await _content.ScriptsAsync();
        if (list is null || _content.Target?.State.Id != objectId) return; // the selection moved on while the answer came
        _scripts.Clear(); _scripts.AddRange(list); Refresh();
    }

    private void SelectKey(string key)
    {
        foreach (var folder in Children()) for (var item = folder.GetFirstChild(); item is not null; item = item.GetNext())
            if (item.GetMetadata(0).VariantType == Variant.Type.String && item.GetMetadata(0).AsString() == key) { item.Select(0); _pickedKey = key; _name.Text = item.GetText(0).Split("  ·  ")[0]; return; }
    }

    private (bool Script, Guid Id)? Picked() { var parts = _pickedKey.Split(':'); return parts.Length == 2 && Guid.TryParse(parts[1], out var id) ? (parts[0] == "script", id) : null; }

    // ---- actions -----------------------------------------------------------------------------------------------------------

    public async Task NewScriptAsync()
    {
        var count = _scripts.Count; var result = await _content.NewScriptAsync(count);
        if (!result.Ok) { Said?.Invoke(result.Message); _info.Text = result.Message; return; }
        Said?.Invoke("Script criado: edite, valide e salve."); await RefreshAsync(); SelectKey(Key(true, result.Id));
        if (_content.Target is { } view) await _editor.OpenAsync(view.State.Id, result.Id);
    }

    /// <summary>ATUALIZAR CONTEÚDO: the scripts are read again; the editor (and any change not saved in it) is left alone.</summary>
    public async Task RefreshAsync()
    {
        if (_content.Target is not { } view) { Refresh(); return; }
        var list = await _content.ScriptsAsync(); if (list is null) { _info.Text = "Não foi possível ler os scripts agora."; return; }
        if (_content.Target?.State.Id != view.State.Id) return;
        _scripts.Clear(); _scripts.AddRange(list); _scriptsFor = view.State.Id; Refresh();
        Said?.Invoke(_editor.IsOpen && _editor.DirtyForTesting ? "Conteúdo atualizado. O script aberto tem alterações não salvas: elas foram mantidas." : "Conteúdo atualizado.");
    }

    public async Task ResetAsync()
    {
        var result = await _content.ResetScriptsAsync(); _info.Text = result.Message; Said?.Invoke(result.Message);
        if (result.Ok) await RefreshAsync();
    }

    private void ShowPermissions()
    {
        if (_content.Target is not { } view) { _info.Text = "Selecione um só objeto."; return; }
        var objectText = BuildPermissionManager.CanEditContents(view.State) ? "O objeto pode ser modificado: você muda o conteúdo dele." : "O objeto não pode ser modificado: o conteúdo é só de leitura.";
        var text = objectText;
        if (Picked() is { } picked)
            text += picked.Script ? " O script roda com as permissões do objeto." : " Item: " + BuildPermissionManager.Describe(BuildPermissionManager.RightsOf(Inventory, picked.Id)) + " Dentro do objeto ele mantém essas mesmas permissões.";
        _info.Text = text; Said?.Invoke(text);
    }

    private void OpenSelected()
    {
        if (Picked() is not { } picked) { Said?.Invoke("Escolha um item da lista."); return; }
        if (picked.Script) { if (_content.Target is { } view) _ = _editor.OpenAsync(view.State.Id, picked.Id); } else ItemAction("open");
    }

    private void ItemAction(string action)
    {
        if (Picked() is not { Script: false } picked) { Said?.Invoke("Escolha um item (não um script) da lista."); return; }
        if (Inventory is not { } inventory) { Said?.Invoke("O inventário não está disponível aqui."); return; }
        if (action == "copy" && !BuildPermissionManager.CanCopy(BuildPermissionManager.RightsOf(inventory, picked.Id))) { Said?.Invoke("Você não pode copiar esse item: ele não tem o direito de copiar."); return; }
        Said?.Invoke(inventory.RunOnAsset(picked.Id, action) ?? action switch { "open" => "Item aberto.", "properties" => "Propriedades do item.", _ => "Cópia pedida ao inventário." });
    }

    private void Rename()
    {
        if (Picked() is not { } picked) { Said?.Invoke("Escolha um item da lista para renomear."); return; }
        if (picked.Script) { _ = RenameScriptAsync(picked.Id); return; }
        Said?.Invoke(_content.Rename(picked.Id, _name.Text) ?? "Item renomeado.");
    }

    private async Task RenameScriptAsync(Guid id) { var result = await _content.RenameScriptAsync(id, _name.Text); Said?.Invoke(result.Message); if (result.Ok) await RefreshAsync(); }

    private void MoveSelected()
    {
        if (Picked() is not { Script: false } picked) { Said?.Invoke("Escolha um item (os scripts ficam na pasta Scripts)."); return; }
        Said?.Invoke(_content.Move(picked.Id, _folder.GetItemText(_folder.Selected)) ?? "Item movido.");
    }

    private void RemoveNow()
    {
        if (Picked() is not { } picked) return;
        if (picked.Script) { _ = RemoveScriptAsync(); return; }
        Said?.Invoke(_content.Remove(picked.Id) ?? "Item removido do objeto.");
    }

    public async Task RemoveScriptAsync()
    {
        if (Picked() is not { Script: true } picked) return;
        var result = await _content.DeleteScriptAsync(picked.Id); Said?.Invoke(result.Message);
        if (result.Ok) { if (_editor.IsOpen && _editor.ScriptIdForTesting == picked.Id) _editor.Close(); await RefreshAsync(); }
    }

    // ---- the inventory -----------------------------------------------------------------------------------------------------

    private (Guid Asset, string Name, string Category)? DropItem(Variant data)
    {
        if (_content?.Target is null || data.VariantType != Variant.Type.Dictionary) return null;
        var dictionary = data.AsGodotDictionary();
        if (!dictionary.TryGetValue("type", out var type) || type.AsString() != "inventory_item" || !dictionary.TryGetValue("itemId", out var id) || !Guid.TryParse(id.AsString(), out var itemId)) return null;
        return Inventory?.DescribeItem(itemId);
    }

    private void OpenPicker()
    {
        if (_content.Target is not { } view) { Said?.Invoke("Selecione um só objeto primeiro."); return; }
        if (!BuildPermissionManager.CanEditContents(view.State)) { Said?.Invoke("Este objeto não pode ser modificado: o conteúdo dele não muda."); return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is not { IsAuthenticated: true } || app is null) { Said?.Invoke("Entre na sua conta para ver o inventário."); return; }
        _pickerList.Clear(); _pickerItems.Clear(); _picker.PopupCentered();
        if (_http is null) { _http = new HttpRequest { Timeout = 20 }; AddChild(_http); _http.RequestCompleted += (_, code, _, body) => { if (code is >= 200 and < 300) FillPicker(body.GetStringFromUtf8()); else Said?.Invoke($"Não foi possível carregar o inventário (HTTP {code})."); }; }
        if (_http.GetHttpClientStatus() == Godot.HttpClient.Status.Disconnected) _http.Request($"{app.ApiBaseUrl}/api/v1/inventory/items?page=1&pageSize=100", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private void FillPicker(string json)
    {
        _pickerList.Clear(); _pickerItems.Clear();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("items", out var items)) return;
            foreach (var item in items.EnumerateArray())
            {
                var category = item.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                if (!BuildContentManager.Folders.Contains(BuildContentManager.FolderOfCategory(category))) continue;
                if (!Array.Exists(["objects", "textures", "sounds", "animations", "clothing", "accessories", "models", "packages", "scripts", "notecards"], x => x == category)) continue;
                if (!item.TryGetProperty("assetId", out var a) || !Guid.TryParse(a.GetString(), out var asset)) continue;
                if (item.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True) continue;
                var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "Item" : "Item";
                _pickerItems.Add((asset, name, category)); _pickerList.AddItem($"{name}  ·  {BuildContentManager.CategoryLabel(category)}");
            }
        }
        catch (JsonException) { Said?.Invoke("Resposta de inventário inválida."); }
    }

    private void InsertPicked()
    {
        var chosen = _pickerList.GetSelectedItems(); if (chosen.Length != 1 || chosen[0] >= _pickerItems.Count) return;
        var (asset, name, category) = _pickerItems[chosen[0]];
        Said?.Invoke(_content.Add(asset, name, category) ?? $"\"{name}\" guardado no objeto.");
    }
}
