using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

public enum BuildTool { Select, Create, Move, Rotate, Scale, Face }

/// <summary>
/// The window of the new editor: the toolbar, the numbers about the selection and the island, the creation screen and the status line. It shows state and raises
/// events; what happens is decided by <see cref="BuildEditorController"/>. Every button here does something; tools that do not exist yet are not on it.
/// </summary>
public partial class BuildEditorWindow : Node
{
    private FloatingWindow _window = null!;
    private readonly Dictionary<BuildTool, Button> _toolButtons = [];
    private Button _camera = null!, _save = null!, _take = null!;
    private LineEdit _packageName = null!;
    private Button _return = null!, _history = null!, _duplicate = null!, _delete = null!, _undo = null!, _redo = null!, _link = null!, _unlink = null!;
    private Tree _tree = null!;
    private Control _tabArea = null!;
    private UiTabs _tabs = null!;
    public BuildGeneralTab General { get; private set; } = null!;
    public BuildObjectTab Object { get; private set; } = null!;
    public BuildResourcesTab Resources { get; private set; } = null!;
    public BuildTextureTab Texture { get; private set; } = null!;
    public BuildContentTab Content { get; private set; } = null!;
    public void RefreshPages() { General.Refresh(); Object.Refresh(); Resources.Refresh(); Texture.Refresh(); Content.Refresh(); }
    private Control[] _pages = [];
    public void SelectTabForTesting(int index) { _tabs.Select(index); }
    public int SelectedTabForTesting => _tabs.Selected;
    public bool TabsVisibleForTesting => _tabArea.Visible;
    public IReadOnlyList<string> TabNamesForTesting => [.. Enumerable.Range(0, _tabs.Count).Select(i => _tabs.TabAt(i).Text.Trim())];
    private OptionButton _space = null!;
    private CheckBox _snap = null!;
    private SpinBox _moveStep = null!, _rotateStep = null!, _scaleStep = null!;
    private Label _selectedLabel = null!, _impactLabel = null!, _parcelLabel = null!, _status = null!;
    private BuildCreationPanel _creation = null!;

    public event Action<BuildTool>? ToolRequested;
    public event Action<string>? PrimitivePicked;
    public event Action? DuplicateRequested, DeleteRequested, CloseRequested, ImportRequested, UndoRequested, RedoRequested, OptionsChanged, LinkRequested, UnlinkRequested;
    public event Action<Guid>? HierarchyPicked;
    public event Action? ReturnRequested, HistoryRequested, CameraRequested, TerrainRequested;
    public event Action<string, bool>? SavePackageRequested;
    public event Action<string>? PresetRequested;
    public void SetCameraPressed(bool on) => _camera.SetPressedNoSignal(on);
    public void PressSaveForTesting(bool take) => (take ? _take : _save).EmitSignal(BaseButton.SignalName.Pressed);
    public void SetPackageNameForTesting(string name) => _packageName.Text = name;
    public void SetCollabEnabled(int selected) { _return.Disabled = selected == 0; _history.Disabled = selected != 1; _save.Disabled = selected == 0; _take.Disabled = selected == 0; }
    public bool LinkEnabledForTesting => !_link.Disabled;
    public bool UnlinkEnabledForTesting => !_unlink.Disabled;
    public void PressLinkForTesting() => _link.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressUnlinkForTesting() => _unlink.EmitSignal(BaseButton.SignalName.Pressed);
    public int HierarchyRowsForTesting => _tree.GetRoot()?.GetChildCount() ?? 0;
    public void PickHierarchyForTesting(int row) { var item = _tree.GetRoot().GetChild(row); HierarchyPicked?.Invoke(Guid.Parse(item.GetMetadata(0).AsString())); }

    public BuildSpace Space => _space.Selected == 1 ? BuildSpace.Local : BuildSpace.World;
    public bool SnapEnabled => _snap.ButtonPressed;
    public float MoveStep => (float)_moveStep.Value;
    public float RotateStepDegrees => (float)_rotateStep.Value;
    public float ScaleStep => (float)_scaleStep.Value;
    public bool UndoEnabledForTesting => !_undo.Disabled;
    public bool RedoEnabledForTesting => !_redo.Disabled;
    public void PressUndoForTesting() => _undo.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressRedoForTesting() => _redo.EmitSignal(BaseButton.SignalName.Pressed);
    public void SetOptionsForTesting(bool local, bool snap, float move, float rotate, float scale) { _space.Select(local ? 1 : 0); _snap.ButtonPressed = snap; _moveStep.Value = move; _rotateStep.Value = rotate; _scaleStep.Value = scale; OptionsChanged?.Invoke(); }

    public bool Visible { get => _window.Visible; set => _window.Visible = value; }
    public BuildCreationPanel Creation => _creation;
    public string SelectedTextForTesting => _selectedLabel.Text;
    public string ImpactTextForTesting => _impactLabel.Text;
    public string ParcelTextForTesting => _parcelLabel.Text;
    public string StatusForTesting => _status.Text;
    public FloatingWindow FrameForTesting => _window;

    public bool DuplicateEnabledForTesting => !_duplicate.Disabled;
    public bool DeleteEnabledForTesting => !_delete.Disabled;
    public bool ToolPressedForTesting(BuildTool tool) => _toolButtons[tool].ButtonPressed;
    public void PressToolForTesting(BuildTool tool) => _toolButtons[tool].EmitSignal(BaseButton.SignalName.Pressed);
    public void PressDuplicateForTesting() => _duplicate.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressDeleteForTesting() => _delete.EmitSignal(BaseButton.SignalName.Pressed);

    public override void _Ready()
    {
        _window = new FloatingWindow("Construir", icon: "hammer") { Name = "BuildWindow", Visible = false, Theme = ViewerTheme.Create() };
        _window.Place(0, 0, 24, 90, 484, 740); _window.MakeResizable(); AddChild(_window);
        _window.CloseRequested += () => CloseRequested?.Invoke();
        var content = _window.Content; content.AddThemeConstantOverride("separation", 10);

        var tools = new HBoxContainer(); tools.AddThemeConstantOverride("separation", 6); content.AddChild(tools);
        AddToolToggle(tools, BuildTool.Select, "pointer", "Selecionar", "Clique em um objeto para selecioná-lo; Shift soma, Ctrl pega uma peça de um conjunto (1)");
        AddToolToggle(tools, BuildTool.Create, "cube", "Criar", "Escolhe uma forma e a coloca no mundo (2)");
        AddToolToggle(tools, BuildTool.Move, "move", "Mover", "Setas vermelha, verde e azul movem em X, Y e Z; a esfera central move livremente (3)");
        AddToolToggle(tools, BuildTool.Rotate, "rotate", "Girar", "Anéis giram em X, Y e Z; o anel branco gira em volta da câmera (4)");
        AddToolToggle(tools, BuildTool.Scale, "expand", "Esticar", "Caixas esticam em X, Y e Z; a esfera central estica por igual (5)");
        AddToolToggle(tools, BuildTool.Face, "layers", "Face", "Clique em uma face para escolhê-la; Shift soma mais faces (6)");
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 6); content.AddChild(actions);
        _duplicate = ActionButton(actions, "copy-plus", "Duplicar", "Duplica a seleção ao lado dela (Ctrl+D)", () => DuplicateRequested?.Invoke());
        _delete = ActionButton(actions, "trash", "Excluir", "Exclui a seleção (Delete)", () => DeleteRequested?.Invoke());
        actions.AddChild(new VSeparator());
        _link = ActionButton(actions, "link", "Vincular", "Vincula os objetos selecionados: o primeiro escolhido vira a raiz (Ctrl+G)", () => LinkRequested?.Invoke());
        _unlink = ActionButton(actions, "unlink", "Soltar", "Desvincula os objetos selecionados; eles ficam onde estão (Ctrl+Shift+G)", () => UnlinkRequested?.Invoke());
        actions.AddChild(new VSeparator());
        _undo = ActionButton(actions, "undo", "Desfazer", "Desfaz a última ação (Ctrl+Z)", () => UndoRequested?.Invoke());
        _redo = ActionButton(actions, "redo", "Refazer", "Refaz a ação desfeita (Ctrl+Y)", () => RedoRequested?.Invoke());
        _undo.Disabled = true; _redo.Disabled = true; _link.Disabled = true; _unlink.Disabled = true;
        var together = new HBoxContainer(); together.AddThemeConstantOverride("separation", 6); content.AddChild(together);
        _return = ActionButton(together, "download", "Devolver", "Devolve os objetos selecionados ao inventário dos donos (só o dono do terreno ou um administrador)", () => ReturnRequested?.Invoke());
        _history = ActionButton(together, "clock", "Histórico", "Mostra o que já aconteceu com o objeto selecionado (só o dono, o dono do terreno ou um administrador)", () => HistoryRequested?.Invoke());
        _camera = ActionButton(together, "eye", "Câmera", "Câmera de edição: gira em volta da seleção (botão direito gira, do meio desloca, roda dá zoom, Home foca)", () => CameraRequested?.Invoke()); _camera.ToggleMode = true;
        ActionButton(together, "mountain", "Terreno", "Abre as ferramentas de terreno (o editor de construção sai do caminho)", () => TerrainRequested?.Invoke());
        var packageRow = new HBoxContainer(); packageRow.AddThemeConstantOverride("separation", 6); content.AddChild(packageRow);
        _packageName = new LineEdit { PlaceholderText = "Nome da construção", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 80 }; packageRow.AddChild(_packageName);
        _save = ActionButton(packageRow, "download", "Guardar", "Guarda a seleção como construção no seu inventário", () => SavePackageRequested?.Invoke(_packageName.Text, false));
        _take = ActionButton(packageRow, "box", "Retirar", "Guarda no inventário e tira do mundo (Ctrl+Z traz de volta)", () => SavePackageRequested?.Invoke(_packageName.Text, true));
        _save.Disabled = true; _take.Disabled = true;
        _return.Disabled = true; _history.Disabled = true;

        var options = new HBoxContainer(); options.AddThemeConstantOverride("separation", 8); content.AddChild(options);
        options.AddChild(SmallLabel("Eixos:"));
        _space = new OptionButton { FocusMode = Control.FocusModeEnum.None, TooltipText = "Mundo: as setas seguem o mundo. Objeto: seguem os lados do objeto" }; _space.AddItem("Mundo"); _space.AddItem("Objeto"); _space.ItemSelected += _ => OptionsChanged?.Invoke(); options.AddChild(_space);
        _snap = new CheckBox { Text = " Encaixar", FocusMode = Control.FocusModeEnum.None, TooltipText = "A posição, o giro e o tamanho caem em múltiplos dos passos ao lado" }; _snap.Toggled += _ => OptionsChanged?.Invoke(); options.AddChild(_snap);
        _moveStep = Step("Passo de posição, em metros", .5, .01, 10, .01); _rotateStep = Step("Passo de giro, em graus", 15, 1, 90, 1); _scaleStep = Step("Passo de tamanho, em metros", .1, .01, 5, .01);
        options.AddChild(SmallLabel("m")); options.AddChild(_moveStep); options.AddChild(SmallLabel("°")); options.AddChild(_rotateStep); options.AddChild(SmallLabel("tam")); options.AddChild(_scaleStep);

        var info = new VBoxContainer(); info.AddThemeConstantOverride("separation", 2); content.AddChild(info);
        _selectedLabel = InfoLabel("Objetos selecionados: 0", true); _impactLabel = InfoLabel("Impacto na ilha: 0", true); _parcelLabel = InfoLabel(string.Empty, false);
        info.AddChild(_selectedLabel); info.AddChild(_impactLabel); info.AddChild(_parcelLabel);
        content.AddChild(new HSeparator());

        _creation = new BuildCreationPanel { Name = "Creation" }; content.AddChild(_creation); _creation.PrimitivePicked += type => PrimitivePicked?.Invoke(type); _creation.PresetRequested += id => PresetRequested?.Invoke(id); _creation.ImportRequested += () => ImportRequested?.Invoke();
        _tree = new Tree { HideRoot = true, CustomMinimumSize = new Vector2(0, 110), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false, SelectMode = Tree.SelectModeEnum.Single, TooltipText = "Peças do conjunto vinculado: clique para editar só uma peça" };
        _tree.AddThemeFontSizeOverride("font_size", 16); _tree.ItemSelected += () => { if (_tree.GetSelected() is { } item) HierarchyPicked?.Invoke(Guid.Parse(item.GetMetadata(0).AsString())); }; content.AddChild(_tree);
        // the tabs of the selected object: GERAL now; the others arrive with their phases and are added only when they work
        _tabArea = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false }; _tabArea.AddThemeConstantOverride("separation", 8); content.AddChild(_tabArea);
        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 18 }; _tabs.AddTab("GERAL", "list"); _tabs.AddTab("OBJETO", "cube"); _tabs.AddTab("RECURSOS", "bulb"); _tabs.AddTab("TEXTURA", "image"); _tabs.AddTab("CONTEÚDO", "folder"); _tabArea.AddChild(_tabs);
        ScrollContainer Page() => new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 360) };
        var generalPage = Page(); General = new BuildGeneralTab { Name = "General" }; generalPage.AddChild(General); _tabArea.AddChild(generalPage);
        var objectPage = Page(); Object = new BuildObjectTab { Name = "Object" }; objectPage.AddChild(Object); objectPage.Visible = false; _tabArea.AddChild(objectPage);
        var resourcesPage = Page(); Resources = new BuildResourcesTab { Name = "Resources" }; resourcesPage.AddChild(Resources); resourcesPage.Visible = false; _tabArea.AddChild(resourcesPage);
        var texturePage = Page(); Texture = new BuildTextureTab { Name = "Texture" }; texturePage.AddChild(Texture); texturePage.Visible = false; _tabArea.AddChild(texturePage);
        var contentPage = Page(); Content = new BuildContentTab { Name = "Content" }; contentPage.AddChild(Content); contentPage.Visible = false; _tabArea.AddChild(contentPage);
        _tabs.TabChanged += index => { generalPage.Visible = index == 0; objectPage.Visible = index == 1; resourcesPage.Visible = index == 2; texturePage.Visible = index == 3; contentPage.Visible = index == 4; RefreshPages(); };
        _pages = [generalPage, objectPage, resourcesPage, texturePage, contentPage];

        _status = new Label { Text = "Escolha uma forma em Criar ou clique em um objeto para selecioná-lo.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44) };
        _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); _status.AddThemeFontSizeOverride("font_size", 16); content.AddChild(_status);
        // everything scrolls inside the window, so the window keeps a sensible size instead of growing to fit its longest page
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 260) };
        var inner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; inner.AddThemeConstantOverride("separation", 10);
        foreach (var child in content.GetChildren().ToList()) { content.RemoveChild(child); inner.AddChild(child); }
        scroll.AddChild(inner); content.AddChild(scroll);
        SetSelection(0, 0, string.Empty);
    }

    private void AddToolToggle(HBoxContainer row, BuildTool tool, string icon, string text, string tip)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 22), ToggleMode = true, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 10, 6, 14); button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Normal, 10, 6, 14));
        button.AddThemeFontSizeOverride("font_size", 17); button.Pressed += () => ToolRequested?.Invoke(tool); _toolButtons[tool] = button; row.AddChild(button);
    }

    private static Button ActionButton(HBoxContainer row, string icon, string text, string tip, Action action)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 22), TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44) };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 10, 6, 14); button.AddThemeFontSizeOverride("font_size", 18); button.Pressed += action; row.AddChild(button); return button;
    }

    private static Label SmallLabel(string text) { var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label; }

    private SpinBox Step(string tip, double value, double min, double max, double step)
    {
        var box = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, TooltipText = tip, CustomMinimumSize = new Vector2(78, 34), SelectAllOnFocus = true };
        box.GetLineEdit().AddThemeFontSizeOverride("font_size", 16); box.ValueChanged += _ => OptionsChanged?.Invoke(); return box;
    }

    private static Label InfoLabel(string text, bool strong)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", strong ? 19 : 16);
        if (strong) label.AddThemeFontOverride("font", UiStyle.Bold); else label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        return label;
    }

    // ---- what the controller tells the window --------------------------------------------------------------------

    private bool _hasSelection;
    public void SetTool(BuildTool tool) { foreach (var (key, button) in _toolButtons) button.SetPressedNoSignal(key == tool); _creation.Visible = tool == BuildTool.Create; _creationOn = tool == BuildTool.Create; ShowArea(); }
    private bool _creationOn;
    private void ShowArea() { _tabArea.Visible = !_creationOn && _hasSelection; if (_tabArea.Visible) RefreshPages(); }

    public void SetSelection(int count, long impact, string summary)
    {
        _selectedLabel.Text = $"Objetos selecionados: {count}"; _impactLabel.Text = $"Impacto na ilha: {impact.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))}";
        _duplicate.Disabled = count == 0; _delete.Disabled = count == 0; _link.Disabled = count < 2; _hasSelection = count > 0; ShowArea();
    }

    public void SetLimits(BuildNetworkSynchronizer.BuildLimits limits)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var text = $"Ilha: {limits.Objects.ToString("N0", pt)} de {limits.MaxObjects.ToString("N0", pt)} objetos · custo {limits.Cost.ToString("N0", pt)} de {limits.MaxCost.ToString("N0", pt)} (restam {Math.Max(0, limits.MaxCost - limits.Cost).ToString("N0", pt)})";
        if (limits.ParcelName is not null) text += $"\nTerreno \"{limits.ParcelName}\": {limits.ParcelObjects.ToString("N0", pt)} de {limits.ParcelMaxObjects.ToString("N0", pt)} objetos · custo {limits.ParcelCost.ToString("N0", pt)} de {limits.ParcelMaxCost.ToString("N0", pt)} (restam {Math.Max(0, limits.ParcelMaxCost - limits.ParcelCost).ToString("N0", pt)})";
        if (!limits.CanBuildHere) text += "\nVocê não pode construir neste ponto.";
        _parcelLabel.Text = text;
    }

    public void SetStatus(string text) => _status.Text = text;
    public void SetCanUnlink(bool can) => _unlink.Disabled = !can;

    /// <summary>The parts of the linked set the selection belongs to, as an indented tree (empty hides it).</summary>
    public void SetHierarchy(IReadOnlyList<(BuildObjectView View, int Depth)> rows, IReadOnlyCollection<Guid> selected)
    {
        _tree.Clear(); _tree.Visible = rows.Count > 0; if (rows.Count == 0) return;
        var root = _tree.CreateItem();
        foreach (var (view, depth) in rows)
        {
            var item = _tree.CreateItem(root); item.SetText(0, $"{new string (' ', depth * 3)}{(depth == 0 ? "▣ " : "└ ")}{view.State.Name}"); item.SetMetadata(0, view.State.Id.ToString());
            if (selected.Contains(view.State.Id)) item.SetCustomColor(0, UiStyle.AccentText);
        }
    }
    public void SetHistory(bool canUndo, bool canRedo) { _undo.Disabled = !canUndo; _redo.Disabled = !canRedo; }
    public void Focus() { _window.MoveToFront(); }
}
