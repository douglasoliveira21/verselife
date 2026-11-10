using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// The window itself and its Objeto and Alinhar tabs, laid out like the references in client/Assets/ui/construir: tabs along the top with
/// icons, a slim bar of everyday actions, then the page. Every control writes to the same fields as before; only the look and the places changed.
public partial class BuildEditor
{
    private UiTabs _tabs = null!;
    private UiTabs _advancedTabs = null!;
    private Action _applyBarRefresh = () => { };
    private bool _advancedHierarchyOpen;
    private readonly List<ObjectPreview> _previews = [];
    private Label _selectedCount = null!, _primsLabel = null!;
    private readonly float[] _lastSize = [1, 1, 1];
    private Button _sizeLock = null!;
    private static readonly int[] RotationSteps = [1, 5, 15, 45, 90];

    private void BuildPanel()
    {
        _window = new FloatingWindow("Construir", icon: "hammer", minimizable: true, expandable: true) { Name = "BuildTools", Visible = false };
        _window.Place(0, 0, 18, 56, 740, 1500);
        _window.Scale = SmallBuildScale; // compact by default so it does not cover the world
        _window.CloseRequested += () => SetActive(false);
        _window.ExpandToggled += SetBuildWindowExpanded;
        var content = _window.Content;
        content.AddThemeConstantOverride("separation", 10);
        Control[] pages = [];
        Control[] advancedPages = [];

        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 22 };
        foreach (var (text, icon) in new[] { ("Geral", "list"), ("Objeto", "cube"), ("Recursos", "bulb"), ("Textura", "image"), ("Conteúdo", "folder") }) _tabs.AddTab(text, icon);
        content.AddChild(_tabs);

        // everyday actions, always at hand whatever the tab
        var quick = Row(8); content.AddChild(quick);
        _toolFace = ToolToggle(" Face", "Selecionar uma face para colori-la (5)", () => FaceTool());
        _toolFace.Icon = UiStyle.Icon("cube", UiStyle.Text, 24); _toolFace.AddThemeConstantOverride("icon_max_width", 24); _toolFace.AddThemeFontSizeOverride("font_size", 19); _toolFace.ExpandIcon = false; _toolFace.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; _toolFace.CustomMinimumSize = new Vector2(112, 44);
        quick.AddChild(_toolFace);
        _cameraButton = ToolToggle(" Câmera de edição", "Gira em volta da seleção: botão direito orbita, botão do meio desloca, roda dá zoom (End)", ToggleEditCamera);
        _cameraButton.Icon = UiStyle.Icon("camera", UiStyle.Text, 24); _cameraButton.AddThemeConstantOverride("icon_max_width", 24); _cameraButton.AddThemeFontSizeOverride("font_size", 19); _cameraButton.ExpandIcon = false; _cameraButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; _cameraButton.CustomMinimumSize = new Vector2(220, 44);
        quick.AddChild(_cameraButton);
        foreach (var (icon, text, tip, action) in new (string, string, string, Action)[]
        {
            ("undo", "", "Desfazer (Ctrl+Z)", Undo), ("redo", "", "Refazer (Ctrl+Y)", Redo), ("layers", "Avançado", "Alinhar, conjuntos vinculados, pivôs e duplicação múltipla", () => ShowEditPages(true)),
            ("help", "Tutorial", "Mostra o passo a passo de construção", RestartTutorial)
        })
        {
            var button = IconButton(icon, text, tip, action, UiStyle.ButtonKind.Tool, 44);
            button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; if (text.Length == 0) { button.Text = string.Empty; button.CustomMinimumSize = new Vector2(52, 44); }
            quick.AddChild(button);
        }
        _selectionLabel = new Label { Text = "Nada selecionado", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _selectionLabel.AddThemeFontSizeOverride("font_size", 19); quick.AddChild(_selectionLabel);
        _saveLabel = new Label { Text = "✓ Salvo", TooltipText = "Mostra se o servidor já confirmou as suas alterações", VerticalAlignment = VerticalAlignment.Center };
        _saveLabel.AddThemeFontOverride("font", UiStyle.Bold); _saveLabel.AddThemeFontSizeOverride("font_size", 20); quick.AddChild(_saveLabel);
        BuildTutorialCard(content);

        var stack = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 640) };
        content.AddChild(stack);
        pages = [Scroll(stack, BuildGeneral()), Scroll(stack, BuildObjectTab()), Scroll(stack, BuildResourcesTab()), Scroll(stack, BuildTextureTab()), Scroll(stack, BuildContentTab())];
        advancedPages = [Scroll(stack, BuildAlignTab()), Scroll(stack, BuildHierarchyTab()), Scroll(stack, BuildTransformTab())];
        foreach (var page in advancedPages) page.Visible = false;
        _advancedTabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 21, Visible = false };
        foreach (var (text, icon) in new[] { ("Alinhar", "move"), ("Conjuntos", "layers"), ("Transformar", "rotate") }) _advancedTabs.AddTab(text, icon);
        content.AddChild(_advancedTabs);
        content.MoveChild(_advancedTabs, 2);
        _advancedTabs.TabChanged += index =>
        {
            _advancedHierarchyOpen = index == 1;
            for (var i = 0; i < advancedPages.Length; i++) advancedPages[i].Visible = i == index;
            RefreshPreviews();
            if (_advancedHierarchyOpen) RefreshHierarchy();
        };
        _tabs.TabChanged += index => { for (var i = 0; i < pages.Length; i++) pages[i].Visible = i == index; RefreshPreviews(); };

        void ShowEditPages(bool advanced)
        {
            _tabs.Visible = !advanced; _advancedTabs.Visible = advanced;
            for (var i = 0; i < pages.Length; i++) pages[i].Visible = !advanced && i == _tabs.Selected;
            for (var i = 0; i < advancedPages.Length; i++) advancedPages[i].Visible = advanced && i == _advancedTabs.Selected;
            _advancedHierarchyOpen = advanced && _advancedTabs.Selected == 1; _applyBarRefresh();
            if (_advancedHierarchyOpen) RefreshHierarchy();
            RefreshPreviews();
        }

        // The classic build flow starts with a tool, then exposes the five editing tabs.
        // Keep LifeVerse-specific tools available below instead of crowding the main tabs.
        _stack = stack; _showEditPages = ShowEditPages;
        var modes = BuildModeBar(); var modePanels = BuildModePanels();
        content.AddChild(modes); content.MoveChild(modes, 0); content.AddChild(modePanels); content.MoveChild(modePanels, 1); content.MoveChild(_advancedTabs, 3);

        // one Restaurar / Aplicar bar for the tabs that send their fields to the server on request (Geral, Objeto, Recursos); Textura and Conteúdo apply as you go
        var applyBar = Row(10);
        var restoreAll = OrangeButton("undo", "Restaurar", "Volta os campos ao que o servidor confirmou", () => { FillFields(); _status.Text = "Valores restaurados."; }, 44); restoreAll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; applyBar.AddChild(restoreAll);
        var applyAll = IconButton("check", "Aplicar", "Envia nome, posição, forma, estados, luz e colisão da seleção ao servidor", () => { if (_tabs.Selected == 2) ApplyLight(); ApplySelected(); }, UiStyle.ButtonKind.Primary, 44); applyAll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; applyBar.AddChild(applyAll);
        content.AddChild(applyBar);
        void ShowApplyBar() => applyBar.Visible = _tabs.Visible && _tabs.Selected <= 2 && _stack.Visible;
        _tabs.TabChanged += _ => ShowApplyBar(); _applyBarRefresh = ShowApplyBar;

        var footer = Row(10); content.AddChild(footer);
        var infoIcon = UiStyle.IconRect("info", UiStyle.TextMuted, 22); infoIcon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; footer.AddChild(infoIcon);
        _status = new Label { Text = "Modo de construção ativo · Alterações são salvas automaticamente no servidor.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 0) };
        _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); _status.AddThemeFontSizeOverride("font_size", 18);
        footer.AddChild(_status);
        AddChild(_window);
        RestoreBuildWindowLayout();
        _tabs.Select(1);
        UpdateToolButtons();
        ShowModeUi();
    }

    /// The terrain editor already has its own authoritative workflow and undo history.
    /// Opening it here deliberately hands over the mouse instead of leaving two editors active.
    private void OpenTerrainTools()
    {
        var terrain = GetParent()?.GetNodeOrNull<TerrainEditor>("TerrainEditor");
        if (terrain is null)
        {
            _status.Text = "As ferramentas de terreno não estão disponíveis nesta ilha.";
            return;
        }
        SetActive(false);
        terrain.SetActive(true);
    }

    private ObjectPreview NewPreview(int minHeight = 280)
    {
        var preview = new ObjectPreview(minHeight); _previews.Add(preview);
        return preview;
    }

    /// Redraws the pictures of the tabs that can be seen (the others are redrawn when their tab opens).
    private void RefreshPreviews()
    {
        var state = _selection.Count > 0 ? _selection[0].State : null;
        foreach (var preview in _previews) if (preview.IsVisibleInTree() || !preview.HasObject) preview.Show(state);
    }

    public void ShowTabForTesting(int index) => _tabs.Select(index);

    // ---- tool buttons of the Objeto tab ----------------------------------------------------------------------

    private Button ListTool(string icon, string text, string tip, Action action)
    {
        var button = ToolToggle(" " + text, tip, action);
        button.Icon = UiStyle.Icon(icon, UiStyle.Text, 26); button.ExpandIcon = false; button.Alignment = HorizontalAlignment.Left; button.AddThemeConstantOverride("icon_max_width", 28);
        button.CustomMinimumSize = new Vector2(0, 52); button.AddThemeFontSizeOverride("font_size", 21);
        return button;
    }

    private static Button ListAction(string icon, string text, string tip, Action action)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 26), TooltipText = tip, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 52), ExpandIcon = false, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 12, 6, 12); button.AddThemeFontSizeOverride("font_size", 21); button.AddThemeFontOverride("font", UiStyle.Medium); button.AddThemeConstantOverride("icon_max_width", 28);
        button.Pressed += action;
        return button;
    }

    // ---- the Objeto tab ----------------------------------------------------------------------------------------

    private float RotationStep() => RotationSteps[Mathf.Clamp(_rotSnap.Selected, 0, RotationSteps.Length - 1)];

    /// "Posição", "Rotação" or "Tamanho": title and three axis fields, with a square button on the right.
    private PanelContainer TransformCard(string title, string icon, LineEdit[] fields, Func<float> step, Action<int> committed, Button trailing)
    {
        var card = Card(null, null, out var body, out _, 6, false, 14, 8);
        var heading = Row(8); heading.AddChild(UiStyle.IconRect(icon, UiStyle.Text, 24)); heading.AddChild(Txt(title, 21, true)); body.AddChild(heading);
        var row = Row(10); body.AddChild(row);
        for (var i = 0; i < 3; i++) row.AddChild(AxisField(fields, i, step, committed));
        row.AddChild(trailing);
        return card;
    }

    /// With the link pressed, changing one side of the size changes the other two by the same ratio.
    private void SizeCommitted(int index)
    {
        if (_sizeLock.ButtonPressed && _lastSize[index] > 1e-4f)
        {
            var ratio = NumOr(_size[index], _lastSize[index]) / _lastSize[index];
            for (var j = 0; j < 3; j++) if (j != index) _size[j].Text = Disp(Mathf.Clamp(_lastSize[j] * ratio, .01f, 64f));
        }
        ApplySelected();
    }

    // ---- the Alinhar tab ---------------------------------------------------------------------------------------

    private Control BuildAlignTab()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12);
        var top = Row(12); top.SizeFlagsVertical = Control.SizeFlags.ExpandFill; page.AddChild(top);

        var guide = Card(null, null, out var guideBody, out _, 0, false, 8, 8); guide.CustomMinimumSize = new Vector2(430, 0); guide.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        guideBody.AddChild(new AxesGlyph { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 330) });
        top.AddChild(guide);

        var panel = Card("Alinhar Objetos", "move", out var body, out _, 12, false);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; top.AddChild(panel);
        body.AddChild(Txt("Selecione dois ou mais objetos (Shift soma). Alinhar usa o contorno de cada objeto.", 18, false, UiStyle.TextMuted));
        body.AddChild(Rule());
        body.AddChild(AlignRow(AlignAxis.X, 0, "Eixo X", "(Horizontal)", ["align-left", "align-center-x", "align-right"], ["Esquerda", "Centro", "Direita"]));
        body.AddChild(Rule());
        body.AddChild(AlignRow(AlignAxis.Y, 1, "Eixo Y", "(Vertical)", ["align-bottom", "align-middle", "align-top"], ["Base", "Meio", "Topo"]));
        body.AddChild(Rule());
        body.AddChild(AlignRow(AlignAxis.Z, 2, "Eixo Z", "(Profundidade)", ["depth-front", "depth-center", "depth-back"], ["Frente", "Centro", "Trás"]));
        body.AddChild(Rule());
        var ground = Row(12); body.AddChild(ground);
        ground.AddChild(UiStyle.IconRect("ground", UiStyle.Text, 28)); ground.AddChild(Txt("Ajustar ao solo", 21, true));
        var groundNote = Txt("Apoia o ponto mais baixo da seleção no chão logo abaixo.", 18, false, UiStyle.TextMuted); groundNote.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; groundNote.AutowrapMode = TextServer.AutowrapMode.WordSmart; ground.AddChild(groundNote);
        var groundButton = IconButton("check", "Ajustar", "Apoia o ponto mais baixo da seleção no chão logo abaixo", RestSelectionOnGround, UiStyle.ButtonKind.Primary, 46); groundButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; groundButton.CustomMinimumSize = new Vector2(180, 46); ground.AddChild(groundButton);

        var bottom = Row(12); page.AddChild(bottom);
        var copy = Card("Copiar Transformação", "layers", out var copyBody, out _, 12);
        var copyRow = Row(10); copyBody.AddChild(copyRow);
        copyRow.AddChild(IconButton("pin", "Copiar posição", "Guarda a posição do primeiro objeto", () => CopyTransform(0), UiStyle.ButtonKind.Secondary, 50));
        copyRow.AddChild(IconButton("rotate", "Copiar rotação", "Guarda a rotação do primeiro objeto", () => CopyTransform(1), UiStyle.ButtonKind.Secondary, 50));
        copyRow.AddChild(IconButton("cube", "Copiar tamanho", "Guarda o tamanho do primeiro objeto", () => CopyTransform(2), UiStyle.ButtonKind.Secondary, 50));
        bottom.AddChild(copy);
        var paste = Card("Colar na Seleção", "paste", out var pasteBody, out _, 10, false); paste.CustomMinimumSize = new Vector2(520, 0); paste.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        pasteBody.AddChild(Txt("Aplica os valores copiados nos objetos selecionados.", 18, false, UiStyle.TextMuted));
        var pasteButton = OrangeButton("paste", "Colar na seleção", "Aplica o que foi copiado", PasteTransform, 50); pasteBody.AddChild(pasteButton);
        bottom.AddChild(paste);
        page.AddChild(BuildAdvancedAlignCard());
        return page;
    }

    private Control AlignRow(AlignAxis axis, int index, string title, string subtitle, string[] icons, string[] names)
    {
        var row = Row(12);
        var color = AxisColor(index);
        var chip = new PanelContainer { CustomMinimumSize = new Vector2(56, 56), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        chip.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(color, .16f), new Color(color, .95f), 10, 2, 0, 0));
        var letter = Txt("XYZ"[index].ToString(), 28, true, color); letter.HorizontalAlignment = HorizontalAlignment.Center; chip.AddChild(letter); row.AddChild(chip);
        var texts = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; texts.AddThemeConstantOverride("separation", 0);
        texts.AddChild(Txt(title, 21, true)); texts.AddChild(Txt(subtitle, 17, false, UiStyle.TextMuted)); row.AddChild(texts);
        row.AddChild(new VSeparator());
        for (var i = 0; i < 3; i++)
        {
            var edge = (AlignEdge)i; var captured = axis;
            var button = new Button { Text = names[i], Icon = UiStyle.Icon(icons[i], UiStyle.Text, 34), VerticalIconAlignment = VerticalAlignment.Top, IconAlignment = HorizontalAlignment.Center, ExpandIcon = false, CustomMinimumSize = new Vector2(0, 74), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, TooltipText = $"Alinha a seleção pelo lado: {names[i].ToLowerInvariant()}" };
            UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 8, 6, 12); button.AddThemeFontSizeOverride("font_size", 19); button.AddThemeConstantOverride("h_separation", 0); button.AddThemeConstantOverride("icon_max_width", 34);
            button.Pressed += () => AlignSelection(captured, edge);
            row.AddChild(button);
        }
        row.AddChild(new VSeparator());
        var distribute = new Button { Text = " Distribuir", Icon = UiStyle.Icon("distribute", UiStyle.Text, 30), ExpandIcon = false, CustomMinimumSize = new Vector2(190, 74), FocusMode = Control.FocusModeEnum.None, TooltipText = "Deixa os espaços iguais entre três ou mais objetos" };
        UiStyle.StyleButton(distribute, UiStyle.ButtonKind.Tool, 12, 6, 12); distribute.AddThemeFontSizeOverride("font_size", 19); distribute.AddThemeConstantOverride("icon_max_width", 30);
        var capturedAxis = axis; distribute.Pressed += () => DistributeSelection(capturedAxis);
        row.AddChild(distribute);
        return row;
    }
}
