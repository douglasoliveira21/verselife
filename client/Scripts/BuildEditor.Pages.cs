using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// The window itself and its Objeto and Alinhar tabs, laid out like the references in client/Assets/ui/construir: tabs along the top with
/// icons, a slim bar of everyday actions, then the page. Every control writes to the same fields as before; only the look and the places changed.
public partial class BuildEditor
{
    private UiTabs _tabs = null!;
    private readonly List<ObjectPreview> _previews = [];
    private Label _selectedCount = null!, _primsLabel = null!;
    private readonly float[] _lastSize = [1, 1, 1];
    private Button _sizeLock = null!;
    private static readonly int[] RotationSteps = [1, 5, 15, 45, 90];

    private void BuildPanel()
    {
        _window = new FloatingWindow("Editor de Construção", icon: "hammer", minimizable: true) { Name = "BuildTools", Visible = false };
        _window.Place(0, 0, 18, 56, 1418, 1280);
        _window.Scale = new Vector2(.55f, .55f); // drawn smaller so it does not cover the world; the layout itself is unchanged
        _window.CloseRequested += () => SetActive(false);
        var content = _window.Content;
        content.AddThemeConstantOverride("separation", 10);

        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 22 };
        foreach (var (text, icon) in new[] { ("Geral", "list"), ("Objeto", "cube"), ("Textura", "image"), ("Recursos", "bulb"), ("Conteúdo", "folder"), ("Alinhar", "move"), ("Hierarquia", "layers"), ("Transformar", "rotate") }) _tabs.AddTab(text, icon);
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
            ("focus", "Focar", "Aponta a câmera para a seleção (Home)", FocusSelection), ("cube", "Criar", "Cria a forma escolhida em Geral: depois clique onde quer colocá-la", CreateObject),
            ("undo", "", "Desfazer (Ctrl+Z)", Undo), ("redo", "", "Refazer (Ctrl+Y)", Redo), ("help", "Tutorial", "Mostra o passo a passo de construção", RestartTutorial)
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
        var pages = new[] { Scroll(stack, BuildGeneral()), Scroll(stack, BuildObjectTab()), Scroll(stack, BuildTextureTab()), Scroll(stack, BuildResourcesTab()), Scroll(stack, BuildContentTab()), Scroll(stack, BuildAlignTab()), Scroll(stack, BuildHierarchyTab()), Scroll(stack, BuildTransformTab()) };
        _tabs.TabChanged += index => { for (var i = 0; i < pages.Length; i++) pages[i].Visible = i == index; RefreshPreviews(); };

        var footer = Row(10); content.AddChild(footer);
        var infoIcon = UiStyle.IconRect("info", UiStyle.TextMuted, 22); infoIcon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; footer.AddChild(infoIcon);
        _status = new Label { Text = "Modo de construção ativo · Alterações são salvas automaticamente no servidor.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 0) };
        _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); _status.AddThemeFontSizeOverride("font_size", 18);
        footer.AddChild(_status);
        AddChild(_window);
        _tabs.Select(1);
        UpdateToolButtons();
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

    private Control BuildObjectTab()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12);
        var main = Row(12); main.SizeFlagsVertical = Control.SizeFlags.ExpandFill; page.AddChild(main);

        // the tools column
        var tools = Card("Ferramentas", null, out var toolsBody, out _, 8); tools.CustomMinimumSize = new Vector2(270, 0); tools.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _toolSelect = ListTool("pointer", "Selecionar", "Selecionar objetos (1)", () => SelectTool());
        _toolMove = ListTool("move", "Mover", "Mover nos três eixos (2)", () => SetMode(GizmoMode.Move));
        _toolRotate = ListTool("rotate", "Rotacionar", "Girar com os anéis (3)", () => SetMode(GizmoMode.Rotate));
        _toolScale = ListTool("expand", "Redimensionar", "Redimensionar (4)", () => SetMode(GizmoMode.Scale));
        foreach (var button in new[] { _toolSelect, _toolMove, _toolRotate, _toolScale }) toolsBody.AddChild(button);
        toolsBody.AddChild(ListAction("copy-plus", "Duplicar", "Duplicar (Ctrl+D)", Duplicate));
        toolsBody.AddChild(ListAction("copy", "Copiar", "Copiar (Ctrl+C)", CopySelection));
        toolsBody.AddChild(ListAction("paste", "Colar", "Colar (Ctrl+V)", Paste));
        toolsBody.AddChild(ListAction("link", "Agrupar", "Junta os objetos selecionados em um conjunto (Ctrl+G)", GroupSelection));
        toolsBody.AddChild(ListAction("unlink", "Desagrupar", "Separa o conjunto (Ctrl+Shift+G)", Ungroup));
        toolsBody.AddChild(ListAction("trash", "Excluir", "Excluir (Delete)", DeleteSelection));
        main.AddChild(tools);

        var rest = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rest.AddThemeConstantOverride("separation", 12); main.AddChild(rest);

        // preview and the three transform cards
        var top = Row(12); top.SizeFlagsVertical = Control.SizeFlags.ExpandFill; rest.AddChild(top);
        var preview = NewPreview(300); preview.SizeFlagsStretchRatio = .8f; top.AddChild(preview);
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.2f }; right.AddThemeConstantOverride("separation", 10); top.AddChild(right);

        _snap = Field("0,5", 8); _snap.Text = Disp(.5); _snap.TooltipText = "Passo do movimento com as setas e da posição aplicada"; _snap.CustomMinimumSize = new Vector2(110, 44);
        right.AddChild(TransformCard("Posição", "pin", _position, () => GridStep(), _ => ApplySelected(), SquareIcon("copy", "Copia a posição do primeiro objeto (cole na aba Alinhar)", () => CopyTransform(0))));
        right.AddChild(TransformCard("Rotação", "rotate", _rotationFields, RotationStep, _ => ApplySelected(), SquareIcon("reset", "Zera a rotação", () => { foreach (var field in _rotationFields) field.Text = Disp(0); ApplySelected(); })));
        _sizeLock = SquareIcon("link", "Mantém as proporções ao mudar um lado", () => { }, toggle: true);
        right.AddChild(TransformCard("Tamanho", "cube", _size, () => GridStep(), SizeCommitted, _sizeLock));

        var stepCard = Card(null, null, out var stepBody, out _, 0, false, 16, 8);
        var stepRow = Row(12); stepBody.AddChild(stepRow);
        stepRow.AddChild(UiStyle.IconRect("move-all", UiStyle.Text, 26)); stepRow.AddChild(Txt("Passo de movimento", 20, true));
        stepRow.AddChild(_snap); stepRow.AddChild(Stepper(_snap, () => .05f, () => { }));
        var stepSlider = NewSlider(.05, 5, .05, .5); stepRow.AddChild(stepSlider);
        stepSlider.ValueChanged += value => { if (!_filling) _snap.Text = Disp(value); };
        _snap.TextChanged += _ => { var value = Mathf.Clamp(NumOr(_snap, .5f), .05f, 5f); if (!Mathf.IsEqualApprox((float)stepSlider.Value, value)) stepSlider.SetValueNoSignal(value); UpdateGrid(); };
        right.AddChild(stepCard);

        // grid and snapping, the ghost option and the shape
        var bottom = Row(12); rest.AddChild(bottom);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = .8f }; left.AddThemeConstantOverride("separation", 10); bottom.AddChild(left);
        var grid = Card("Grade e encaixe", "grid-dots", out var gridBody, out _, 10);
        var gridRow = Row(14); gridBody.AddChild(gridRow);
        var checks = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; checks.AddThemeConstantOverride("separation", 14); gridRow.AddChild(checks);
        _showGrid = new CheckBox { Text = " Exibir grade", ButtonPressed = false }; _showGrid.Toggled += _ => UpdateGrid(); _showGrid.AddThemeFontSizeOverride("font_size", 20); checks.AddChild(_showGrid);
        _gridSnap = new CheckBox { Text = " Encaixar na grade", TooltipText = "A posição final cai em múltiplos do passo de movimento" }; _gridSnap.AddThemeFontSizeOverride("font_size", 20); checks.AddChild(_gridSnap);
        gridRow.AddChild(new VSeparator());
        var turn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; turn.AddThemeConstantOverride("separation", 8); gridRow.AddChild(turn);
        var turnTitle = Row(8); turnTitle.AddChild(UiStyle.IconRect("rotate", UiStyle.Text, 24)); turnTitle.AddChild(Txt("Passo de giro", 20)); turn.AddChild(turnTitle);
        _rotSnap = new OptionButton { CustomMinimumSize = new Vector2(0, 44), FocusMode = Control.FocusModeEnum.None, TooltipText = "Quanto gira a cada passo ao segurar Ctrl" };
        foreach (var step in RotationSteps) _rotSnap.AddItem($"{step}°");
        _rotSnap.Select(1); turn.AddChild(_rotSnap);
        left.AddChild(grid);
        var ghostCard = Card(null, null, out var ghostBody, out _, 0, false, 16, 10);
        _ghost = new CheckBox { Text = " Fantasma (pode ser atravessado)", TooltipText = "Desativa a colisão sólida; o objeto continua visível" }; _ghost.AddThemeFontSizeOverride("font_size", 20);
        ghostBody.AddChild(_ghost);
        var physRow = Row(8); ghostBody.AddChild(physRow);
        physRow.AddChild(Txt("Física", 19, true)); _physMode = Pick("Estático", "Físico"); _physMode.TooltipText = "Físico marca um corpo que deve se mover por física (cair, ser empurrado). Ainda não é simulado no mundo: fica onde foi posto."; physRow.AddChild(_physMode);
        var shapeRow = Row(8); ghostBody.AddChild(shapeRow); shapeRow.AddChild(Txt("Colisão", 19, true)); _collisionShape = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Caixa é leve; casco segue a forma; malha triangular preserva portas e aberturas, mas é apenas estática." };
        _collisionShape.AddItem("Nenhuma"); _collisionShape.AddItem("Caixa"); _collisionShape.AddItem("Casco convexo"); _collisionShape.AddItem("Malha triangular (estática)"); _collisionShape.Select(1);
        _collisionShape.ItemSelected += index => { if (index == 3 && _physMode.Selected == 1) { _physMode.Select(0); _status.Text = "Malha triangular é estática; o modo foi alterado para Estático."; } };
        shapeRow.AddChild(_collisionShape);
        _showCollision = new CheckBox { Text = " Visualizar colisão", TooltipText = "Mostra em ciano a geometria que gera a colisão. A malha triangular acompanha as partes importadas." }; _showCollision.AddThemeFontSizeOverride("font_size", 18); _showCollision.Toggled += SetCollisionPreview; ghostBody.AddChild(_showCollision);
        var physNumbers = Row(8); ghostBody.AddChild(physNumbers);
        physNumbers.AddChild(Txt("Massa", 18)); _physMass = Field("1", 8); _physMass.Text = "1"; physNumbers.AddChild(_physMass);
        physNumbers.AddChild(Txt("Atrito", 18)); _physFriction = Field("0,5", 6); _physFriction.Text = "0,5"; physNumbers.AddChild(_physFriction);
        physNumbers.AddChild(Txt("Quique", 18)); _physBounce = Field("0", 6); _physBounce.Text = "0"; physNumbers.AddChild(_physBounce);
        left.AddChild(ghostCard);

        var shape = Card("Forma", "layers", out var shapeBody, out _, 12); shape.SizeFlagsStretchRatio = 1.2f;
        _hollow = NewSlider(0, .95, .01, 0); _taperX = NewSlider(-1, 1, .01, 0); _taperY = NewSlider(-1, 1, .01, 0); _twist = NewSlider(-180, 180, 1, 0);
        shapeBody.AddChild(SliderLine(null, "Oco", _hollow, 3, false, 130)); shapeBody.AddChild(SliderLine(null, "Afunilar X", _taperX, 3, false, 130));
        shapeBody.AddChild(SliderLine(null, "Afunilar Y", _taperY, 3, false, 130)); shapeBody.AddChild(SliderLine(null, "Torção", _twist, 3, false, 130));
        shapeBody.AddChild(Txt("Vale para caixa, prisma, cilindro, tubo, cone e pirâmide.", 17, false, UiStyle.TextMuted));
        bottom.AddChild(shape);

        // footer of the tab
        var footer = Card(null, null, out var footerBody, out _, 0, false, 16, 8); page.AddChild(footer);
        var footerRow = Row(14); footerBody.AddChild(footerRow);
        footerRow.AddChild(UiStyle.IconRect("info", UiStyle.Text, 26));
        footerRow.AddChild(Txt("Selecionado:", 20)); _selectedCount = Txt("nenhum", 20, true, UiStyle.AccentText); footerRow.AddChild(_selectedCount);
        footerRow.AddChild(new VSeparator()); footerRow.AddChild(UiStyle.IconRect("cube", UiStyle.Text, 26));
        _primsLabel = Txt("Prims: —", 20); footerRow.AddChild(_primsLabel);
        footerRow.AddChild(Filler());
        var restore = OrangeButton("undo", "Restaurar", "Volta os campos ao que o servidor confirmou", () => { FillFields(); _status.Text = "Valores restaurados."; }); restore.CustomMinimumSize = new Vector2(190, 48); restore.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; footerRow.AddChild(restore);
        var apply = IconButton("check", "Aplicar à seleção", "Envia posição, rotação, tamanho, forma e física ao servidor", ApplySelected, UiStyle.ButtonKind.Primary, 48); apply.CustomMinimumSize = new Vector2(280, 48); apply.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; footerRow.AddChild(apply);
        return page;
    }

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
