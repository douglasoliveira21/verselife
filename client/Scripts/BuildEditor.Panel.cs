using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// The build window (client/Assets/ui design): a toolbar with the tools and actions, and the tabs Geral, Objeto and Textura
/// with the values of the selected object. Nothing here talks to the server directly; every change goes through
/// CommitUpdates / CreateFrom, which send revisioned operations the island server validates.
public partial class BuildEditor
{
    private static readonly (string Id, string Label)[] Primitives =
    [
        ("cube", "Caixa"), ("sphere", "Esfera"), ("cylinder", "Cilindro"), ("prism", "Prisma"), ("torus", "Toro"), ("tube", "Tubo"),
        ("ring", "Anel"), ("pyramid", "Pirâmide"), ("cone", "Cone"), ("hemisphere", "Semiesfera"), ("plane", "Plano")
    ];

    private FloatingWindow _window = null!;
    private OptionButton _primitive = null!;
    private LineEdit _snap = null!, _name = null!;
    private TextEdit _description = null!;
    private readonly LineEdit[] _position = new LineEdit[3], _rotationFields = new LineEdit[3], _size = new LineEdit[3];
    private LineEdit _hollow = null!, _taperX = null!, _taperY = null!, _twist = null!;
    private ColorPickerButton _colorPicker = null!, _emissionPicker = null!;
    private CheckBox _glow = null!, _ghost = null!;
    private HSlider _roughness = null!, _metallic = null!, _opacity = null!;
    private Label _status = null!, _selectionLabel = null!;
    private Button _toolSelect = null!, _toolMove = null!, _toolRotate = null!, _toolScale = null!, _toolFace = null!;
    private CheckBox _gridSnap = null!, _showGrid = null!;
    private LineEdit _rotSnap = null!;
    private Label _faceLabel = null!;
    private bool _filling;

    private void BuildPanel()
    {
        _window = new FloatingWindow("Construir", icon: "hammer", minimizable: true) { Name = "BuildTools", Visible = false };
        _window.Place(0, 0, 18, 56, 700, 1130);
        _window.CloseRequested += () => SetActive(false);
        var content = _window.Content;
        content.AddThemeConstantOverride("separation", 9);

        // tools: icon above the name
        var tools = new HBoxContainer(); tools.AddThemeConstantOverride("separation", 8); content.AddChild(tools);
        _toolSelect = StackedTool("pointer", "Selecionar", "Selecionar objetos (1)", () => SelectTool());
        _toolMove = StackedTool("move", "Mover", "Mover nos três eixos (2)", () => SetMode(GizmoMode.Move));
        _toolRotate = StackedTool("rotate", "Girar", "Girar com os anéis (3)", () => SetMode(GizmoMode.Rotate));
        _toolScale = StackedTool("expand", "Esticar", "Redimensionar (4)", () => SetMode(GizmoMode.Scale));
        _toolFace = StackedTool("cube", "Face", "Selecionar uma face para colori-la (5)", () => FaceTool());
        foreach (var button in new[] { _toolSelect, _toolMove, _toolRotate, _toolScale, _toolFace }) tools.AddChild(button);

        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 8); content.AddChild(actions);
        foreach (var (icon, tip, action) in new (string, string, Action)[]
        {
            ("cube", "Criar a forma escolhida em Geral: depois clique onde quer colocá-la", CreateObject), ("copy", "Copiar (Ctrl+C)", CopySelection), ("paste", "Colar (Ctrl+V)", Paste), ("copy-plus", "Duplicar (Ctrl+D)", Duplicate),
            ("trash", "Excluir (Delete)", DeleteSelection), ("undo", "Desfazer (Ctrl+Z)", Undo), ("redo", "Refazer (Ctrl+Y)", Redo)
        })
        {
            var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 24), TooltipText = tip, CustomMinimumSize = new Vector2(0, 46), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 8, 6); button.Pressed += action; actions.AddChild(button);
        }

        var cameraRow = new HBoxContainer(); cameraRow.AddThemeConstantOverride("separation", 8); content.AddChild(cameraRow);
        _cameraButton = ToolToggle(" Câmera de edição", "Gira em volta da seleção: botão direito orbita, botão do meio desloca, roda dá zoom (End)", ToggleEditCamera);
        _cameraButton.Icon = UiStyle.Icon("camera", UiStyle.Text, 24); _cameraButton.ExpandIcon = false; _cameraButton.CustomMinimumSize = new Vector2(0, 44); _cameraButton.AddThemeFontSizeOverride("font_size", 17);
        cameraRow.AddChild(_cameraButton);
        cameraRow.AddChild(IconButton("focus", "Focar", "Aponta a câmera para a seleção (Home)", FocusSelection, UiStyle.ButtonKind.Tool));
        cameraRow.AddChild(IconButton("help", "Tutorial", "Mostra o passo a passo de construção", RestartTutorial, UiStyle.ButtonKind.Tool));

        var groupRow = new HBoxContainer(); groupRow.AddThemeConstantOverride("separation", 10); content.AddChild(groupRow);
        groupRow.AddChild(IconButton("link", "Vincular", "Junta os objetos selecionados em um conjunto (Ctrl+G)", GroupSelection, height: 42));
        groupRow.AddChild(IconButton("unlink", "Desvincular", "Separa o conjunto (Ctrl+Shift+G)", Ungroup, height: 42));

        content.AddChild(new HSeparator());
        var selectionRow = new HBoxContainer(); selectionRow.AddThemeConstantOverride("separation", 8); content.AddChild(selectionRow);
        _selectionLabel = new Label { Text = "Nada selecionado", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _selectionLabel.AddThemeFontSizeOverride("font_size", 17);
        selectionRow.AddChild(_selectionLabel);
        _saveLabel = new Label { Text = "✓ Salvo", TooltipText = "Mostra se o servidor já confirmou as suas alterações" }; _saveLabel.AddThemeFontOverride("font", UiStyle.Bold); _saveLabel.AddThemeFontSizeOverride("font_size", 18); selectionRow.AddChild(_saveLabel);
        content.AddChild(new HSeparator());
        BuildTutorialCard(content);

        // tabs
        var tabs = new UiTabs { EqualWidth = true, Flat = true };
        tabs.AddTab("Geral"); tabs.AddTab("Objeto"); tabs.AddTab("Textura"); tabs.AddTab("Recursos"); tabs.AddTab("Conteúdo"); tabs.AddTab("Alinhar");
        content.AddChild(tabs);
        var stack = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 420) };
        content.AddChild(stack);
        var pages = new[] { Scroll(stack, BuildGeneral()), Scroll(stack, BuildObjectTab()), Scroll(stack, BuildTextureTab()), Scroll(stack, BuildResourcesTab()), Scroll(stack, BuildContentTab()), Scroll(stack, BuildAlignTab()) };
        tabs.TabChanged += index => { for (var i = 0; i < pages.Length; i++) pages[i].Visible = i == index; };
        pages[0].Visible = true;

        content.AddChild(new HSeparator());
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 10); content.AddChild(footer);
        var infoIcon = UiStyle.IconRect("info", UiStyle.TextMuted, 22); infoIcon.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin; footer.AddChild(infoIcon);
        _status = new Label { Text = "Modo de construção ativo · Alterações são salvas automaticamente no servidor.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 0) };
        _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); _status.AddThemeFontSizeOverride("font_size", 16);
        footer.AddChild(_status);
        AddChild(_window);
        UpdateToolButtons();
    }

    private Button ToolToggle(string text, string tip, Action action)
    {
        var button = new Button { Text = text, TooltipText = tip, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        const float mx = 8, my = 6;
        button.AddThemeStyleboxOverride("normal", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Normal, mx, my, 12));
        button.AddThemeStyleboxOverride("hover", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Hover, mx, my, 12));
        button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Normal, mx, my, 12));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Hover, mx, my, 12));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeFontOverride("font", UiStyle.Medium); button.AddThemeFontSizeOverride("font_size", 16);
        foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) button.AddThemeColorOverride(color, Colors.White);
        button.Pressed += action;
        return button;
    }

    private static Button TextButton(string text, string tip, Action action, UiStyle.ButtonKind kind = UiStyle.ButtonKind.Secondary)
    {
        var button = new Button { Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 42), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 16); button.Pressed += action;
        return button;
    }

    private void UpdateToolButtons()
    {
        if (_toolSelect is null) return;
        _toolSelect.SetPressedNoSignal(_selectOnly);
        _toolMove.SetPressedNoSignal(!_selectOnly && !_faceTool && _mode == GizmoMode.Move);
        _toolRotate.SetPressedNoSignal(!_selectOnly && !_faceTool && _mode == GizmoMode.Rotate);
        _toolScale.SetPressedNoSignal(!_selectOnly && !_faceTool && _mode == GizmoMode.Scale);
        _toolFace.SetPressedNoSignal(_faceTool);
    }

    private static Control Scroll(Control stack, Control content)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content); stack.AddChild(scroll);
        return scroll;
    }

    // ---- tabs ------------------------------------------------------------------------------------------------

    private Control BuildObjectTab()
    {
        var box = Section();
        box.AddChild(FieldLabel("Posição (metros)")); box.AddChild(Triple(_position, "X", "Y", "Z"));
        box.AddChild(FieldLabel("Rotação (graus)")); box.AddChild(Triple(_rotationFields, "X", "Y", "Z"));
        box.AddChild(FieldLabel("Tamanho (metros, 0,01 a 64)")); box.AddChild(Triple(_size, "X", "Y", "Z"));
        box.AddChild(FieldLabel("Passo de movimento (metros)"));
        _snap = Field("0.5", 8); _snap.Text = "0.5"; _snap.TooltipText = "Passo do movimento com as setas e da posição aplicada"; box.AddChild(_snap);
        box.AddChild(FieldLabel("Grade e encaixe"));
        _showGrid = new CheckBox { Text = " Mostrar grade sob a seleção" }; _showGrid.Toggled += _ => UpdateGrid(); box.AddChild(_showGrid);
        _gridSnap = new CheckBox { Text = " Ajustar à grade ao mover", TooltipText = "A posição final cai em múltiplos do passo de movimento" }; box.AddChild(_gridSnap);
        _snap.TextChanged += _ => UpdateGrid();
        box.AddChild(FieldLabel("Passo de giro (graus)"));
        _rotSnap = Field("5", 6); _rotSnap.Text = "5"; box.AddChild(_rotSnap);
        box.AddChild(FieldLabel("Forma (caixa, prisma, cilindro, tubo, cone e pirâmide)"));
        var shape = new GridContainer { Columns = 2 }; shape.AddThemeConstantOverride("h_separation", 8); shape.AddThemeConstantOverride("v_separation", 6);
        _hollow = Labeled(shape, "Oco (0 a 0,95)", "0"); _taperX = Labeled(shape, "Afunilar X (-1 a 1)", "0"); _taperY = Labeled(shape, "Afunilar Y (-1 a 1)", "0"); _twist = Labeled(shape, "Torção (-180 a 180)", "0");
        box.AddChild(shape);
        _ghost = new CheckBox { Text = " Fantasma (pode ser atravessado)", TooltipText = "Desativa a colisão sólida; o objeto continua visível" };
        box.AddChild(_ghost);
        box.AddChild(TextButton("Aplicar à seleção", "Envia posição, rotação, tamanho, forma e física ao servidor", ApplySelected, UiStyle.ButtonKind.Primary));
        return box;
    }

    private static VBoxContainer Section() { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); return box; }

    private static Label FieldLabel(string text)
    {
        var label = new Label { Text = text }; label.AddThemeFontOverride("font", UiStyle.Medium); label.AddThemeFontSizeOverride("font_size", 16);
        return label;
    }

    private static LineEdit Field(string placeholder, int maxLength) => new() { PlaceholderText = placeholder, MaxLength = maxLength, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    private HBoxContainer Triple(LineEdit[] fields, params string[] labels)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        for (var i = 0; i < 3; i++)
        {
            var axis = new Label { Text = labels[i], VerticalAlignment = VerticalAlignment.Center };
            axis.AddThemeColorOverride("font_color", i switch { 0 => new Color("#FF5A5A"), 1 => new Color("#5AE05A"), _ => new Color("#5A9BFF") }); axis.AddThemeFontOverride("font", UiStyle.Bold);
            row.AddChild(axis);
            fields[i] = Field("0", 12); fields[i].Text = "0"; fields[i].TextSubmitted += _ => ApplySelected(); row.AddChild(fields[i]);
        }
        return row;
    }

    private LineEdit Labeled(GridContainer grid, string label, string value)
    {
        var text = new Label { Text = label, VerticalAlignment = VerticalAlignment.Center }; text.AddThemeFontSizeOverride("font_size", 15); grid.AddChild(text);
        var field = Field(value, 8); field.Text = value; field.TextSubmitted += _ => ApplySelected(); grid.AddChild(field);
        return field;
    }

    private static HSlider Slider(VBoxContainer box, string label, float value)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        var text = new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) }; text.AddThemeFontSizeOverride("font_size", 16); row.AddChild(text);
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = .01, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28) }; row.AddChild(slider);
        box.AddChild(row);
        return slider;
    }

    // ---- reading and writing the fields ----------------------------------------------------------------------

    private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool TryNum(string text, out float value) => float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    private static float NumOr(LineEdit field, float fallback) => TryNum(field.Text, out var value) ? value : fallback;

    /// Shows the selected object's values in the window; with several objects only the count is shown.
    private void FillFields()
    {
        if (_selectionLabel is null) return;
        _filling = true;
        _selectionLabel.Text = _selection.Count switch
        {
            0 => "Nada selecionado",
            1 => $"Selecionado: {_selection[0].State.Name} · revisão {_selection[0].State.Revision}{(_selection[0].State.GroupId is null ? "" : " · vinculado")}",
            _ => $"{_selection.Count} objetos selecionados"
        };
        if (_selection.Count == 1)
        {
            var view = _selection[0]; var s = view.State;
            _name.Text = s.Name; _description.Text = s.Description;
            for (var i = 0; i < 3; i++)
            {
                _position[i].Text = Num(new[] { view.Position.X, view.Position.Y, view.Position.Z }[i]);
                _rotationFields[i].Text = Num(new[] { s.RotationX, s.RotationY, s.RotationZ }[i]);
                _size[i].Text = Num(new[] { view.Scale.X, view.Scale.Y, view.Scale.Z }[i]);
            }
            var geometry = ParseObject(s.GeometryJson); var material = ParseObject(s.MaterialJson); var physics = ParseObject(s.PhysicsJson);
            _hollow.Text = Num(Read(geometry, "hollow", 0)); _taperX.Text = Num(Read(geometry, "taperX", 0)); _taperY.Text = Num(Read(geometry, "taperY", 0)); _twist.Text = Num(Read(geometry, "twist", 0));
            if (material["color"]?.GetValue<string>() is { } color) _colorPicker.Color = Color.FromHtml(color);
            _roughness.Value = Read(material, "roughness", .7f); _metallic.Value = Read(material, "metallic", 0f); _opacity.Value = Read(material, "transparency", 1f);
            _glow.ButtonPressed = material["emission"] is not null;
            if (material["emission"]?.GetValue<string>() is { } emission) _emissionPicker.Color = Color.FromHtml(emission);
            _ghost.ButtonPressed = physics["collision"] is JsonValue flag && flag.TryGetValue<bool>(out var solid) && !solid;
            var parts = s.GroupId is { } group ? _objects.Values.Count(o => o.State.GroupId == group) : 1;
            _infoLeft.Text = $"Criador: {Who(s.CreatorUserId)}\nProprietário: {Who(s.OwnerUserId)}";
            _infoRight.Text = $"Identificador: {s.Id}\nPrimitiva: {s.Name}{(parts > 1 ? $" (composta por {parts} primitivas)" : $" ({PrimitiveLabel(s.PrimitiveType)})")}";
        }
        else { _infoLeft.Text = _selection.Count == 0 ? "Selecione um objeto para ver criador, proprietário e identificador." : $"{_selection.Count} objetos: posição, rotação e tamanho próprios não são editados em conjunto; forma, material e física valem para todos."; _infoRight.Text = string.Empty; }
        FillTextureFields();
        FillContent();
        _filling = false;
        RefreshGizmo();
    }

    private string Who(Guid id) => id == Guid.Empty ? "—" : id == _session.UserId ? "Você" : id.ToString()[..8];
    private string Short(Guid id) => id == Guid.Empty ? "—" : id == _session.UserId ? "você" : id.ToString()[..8];
    private static string PrimitiveLabel(string id) => Primitives.FirstOrDefault(x => x.Id == id).Label ?? id;

    private static JsonObject ParseObject(string json)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? []; }
        catch (JsonException) { return []; }
    }

    private static float Read(JsonObject node, string key, float fallback) => node[key] is JsonValue value && value.TryGetValue<double>(out var number) ? (float)number : fallback;

    private void ApplySelected()
    {
        if (_filling) return;
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto primeiro."; return; }
        var single = _selection.Count == 1;
        var afters = new List<BuildObjectState>();
        foreach (var view in _selection)
        {
            var after = FromView(view);
            if (single)
            {
                after.PositionX = NumOr(_position[0], after.PositionX); after.PositionY = NumOr(_position[1], after.PositionY); after.PositionZ = NumOr(_position[2], after.PositionZ);
                after.RotationX = NumOr(_rotationFields[0], after.RotationX); after.RotationY = NumOr(_rotationFields[1], after.RotationY); after.RotationZ = NumOr(_rotationFields[2], after.RotationZ);
                after.ScaleX = ClampScale(NumOr(_size[0], after.ScaleX)); after.ScaleY = ClampScale(NumOr(_size[1], after.ScaleY)); after.ScaleZ = ClampScale(NumOr(_size[2], after.ScaleZ));
                if (!string.IsNullOrWhiteSpace(_name.Text)) after.Name = _name.Text.Trim();
                after.Description = _description.Text.Trim();
            }
            after.GeometryJson = GeometryFromFields(after.GeometryJson);
            after.MaterialJson = MaterialFromFields(after.MaterialJson);
            after.PhysicsJson = PhysicsFromFields(after.PhysicsJson);
            afters.Add(after);
        }
        CommitUpdates(afters);
        _status.Text = "Alterações enviadas ao servidor.";
    }

    private string GeometryFromFields(string existing)
    {
        var node = ParseObject(existing);
        node["hollow"] = Mathf.Clamp(NumOr(_hollow, 0), 0f, .95f); node["taperX"] = Mathf.Clamp(NumOr(_taperX, 0), -1f, 1f);
        node["taperY"] = Mathf.Clamp(NumOr(_taperY, 0), -1f, 1f); node["twist"] = Mathf.Clamp(NumOr(_twist, 0), -180f, 180f);
        return node.ToJsonString();
    }

    private string MaterialFromFields(string existing)
    {
        var node = ParseObject(existing);
        node["color"] = "#" + _colorPicker.Color.ToHtml(false);
        node["roughness"] = Math.Round(_roughness.Value, 2); node["metallic"] = Math.Round(_metallic.Value, 2);
        if (_opacity.Value < .999) node["transparency"] = Math.Round(_opacity.Value, 2); else node.Remove("transparency");
        if (_glow.ButtonPressed) { node["emission"] = "#" + _emissionPicker.Color.ToHtml(false); node["emissionStrength"] = Math.Round(_glowStrength.Value * 8, 2); } else { node.Remove("emission"); node.Remove("emissionStrength"); }
        return node.ToJsonString();
    }

    private string PhysicsFromFields(string existing)
    {
        var node = ParseObject(existing);
        node["collision"] = !_ghost.ButtonPressed;
        return node.ToJsonString();
    }

    private void CopyProperties()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto para copiar as propriedades."; return; }
        var s = _selection[0].State;
        _propertyClipboard = JsonSerializer.Serialize(new { materialJson = s.MaterialJson, geometryJson = s.GeometryJson, physicsJson = s.PhysicsJson });
        _status.Text = "Propriedades copiadas.";
    }

    private void PasteProperties()
    {
        if (string.IsNullOrWhiteSpace(_propertyClipboard)) { _status.Text = "Nenhuma propriedade copiada."; return; }
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que receberão as propriedades."; return; }
        try
        {
            using var doc = JsonDocument.Parse(_propertyClipboard);
            var afters = new List<BuildObjectState>();
            foreach (var view in _selection)
            {
                var after = FromView(view);
                after.MaterialJson = doc.RootElement.GetProperty("materialJson").GetString() ?? after.MaterialJson;
                after.GeometryJson = doc.RootElement.GetProperty("geometryJson").GetString() ?? after.GeometryJson;
                after.PhysicsJson = doc.RootElement.GetProperty("physicsJson").GetString() ?? after.PhysicsJson;
                afters.Add(after);
            }
            CommitUpdates(afters);
            _status.Text = "Propriedades coladas.";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _status.Text = "Propriedades inválidas na área de transferência."; }
    }

    /// Test hook: fills the object tab as a person would and presses Aplicar.
    public void ApplyFieldsForTesting(Vector3 position, Vector3 size, string name, float hollow)
    {
        for (var i = 0; i < 3; i++) { _position[i].Text = Num(position[i]); _size[i].Text = Num(size[i]); }
        _name.Text = name; _hollow.Text = Num(hollow);
        ApplySelected();
    }
    public void SelectToolForTesting() => SelectTool();
    public void ApplyRotationForTesting(Vector3 degrees) { for (var i = 0; i < 3; i++) _rotationFields[i].Text = Num(degrees[i]); ApplySelected(); }
    public string PrimitiveIdsForTesting => string.Join(",", Primitives.Select(x => x.Id));
    public Control WindowForTesting => _window;

    // ---- grid, faces and alignment -------------------------------------------------------------------------

    private float GridStep() => Mathf.Clamp(NumOr(_snap, .5f), 0f, 100f);

    private void UpdateGrid()
    {
        if (_grid is null || _showGrid is null) return;
        if (_selection.Count == 0 || !_active) { _grid.ShowAround(Vector3.Zero, 0, 0, false); return; }
        var boxes = _selection.Select(WorldBounds).ToList();
        var low = boxes.Min(x => x.Min.Y); var center = boxes.Aggregate(System.Numerics.Vector3.Zero, (sum, x) => sum + x.Center) / boxes.Count;
        _grid.ShowAround(new Vector3(center.X, 0, center.Z), low, GridStep(), _showGrid.ButtonPressed);
    }

    private void FaceTool()
    {
        CancelDrag(); _faceTool = true; _selectOnly = false; DisarmPlacement(null);
        UpdateToolButtons(); RefreshGizmo();
        _status.Text = "Clique em uma face para selecioná-la; depois use a aba Textura.";
    }

    private void SelectFace(BuildObjectView view, int face)
    {
        ClearFace();
        if (!view.IsProcedural || face < 0) { _faceLabel.Text = "Esta forma não tem faces separadas; use caixa, prisma, cilindro, tubo, cone ou pirâmide."; return; }
        _selectedFace = face; _faceView = view; view.HighlightFace(face);
        _faceLabel.Text = $"Face selecionada: {face}"; FillTextureFields(); _status.Text = "Face selecionada. Escolha a cor na aba Textura e use Cor na face.";
    }

    private BuildObjectView? _faceView;

    private void ClearFace()
    {
        if (_faceView is not null && IsInstanceValid(_faceView)) _faceView.HighlightFace(-1);
        _faceView = null; _selectedFace = -1;
        if (_faceLabel is not null) { _faceLabel.Text = "Use a ferramenta Face (5) e clique em uma face de uma primitiva."; FillTextureFields(); }
    }

    private void PaintFace(bool clear)
    {
        if (_selectedFace < 0 || _faceView is null || !IsInstanceValid(_faceView)) { _status.Text = "Selecione uma face com a ferramenta Face (5)."; return; }
        var after = FromView(_faceView);
        var material = ParseObject(after.MaterialJson);
        var faces = material["faces"] as JsonArray ?? [];
        for (var i = faces.Count - 1; i >= 0; i--) if (faces[i] is JsonObject entry && entry["index"]?.GetValue<int>() == _selectedFace) faces.RemoveAt(i);
        if (!clear) faces.Add(new JsonObject { ["index"] = _selectedFace, ["color"] = "#" + _colorPicker.Color.ToHtml(false) });
        if (faces.Count > 0) material["faces"] = faces; else material.Remove("faces");
        after.MaterialJson = material.ToJsonString();
        var face = _selectedFace;
        CommitUpdates([after]);
        _status.Text = clear ? "Cor da face removida." : "Face colorida.";
        _selectedFace = face;
    }

    /// World-space box around an object's visible mesh.
    private static LifeVerse.Shared.Building.Bounds WorldBounds(BuildObjectView view)
    {
        var mesh = view.GetNodeOrNull<MeshInstance3D>("PrimitiveMesh");
        var local = mesh?.GetAabb() ?? new Aabb(-Vector3.One / 2, Vector3.One);
        var transform = mesh is not null && mesh.IsInsideTree() ? mesh.GlobalTransform : view.GlobalTransform;
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var corner = transform * local.GetEndpoint(i);
            min = min.Min(corner); max = max.Max(corner);
        }
        return new LifeVerse.Shared.Building.Bounds(new System.Numerics.Vector3(min.X, min.Y, min.Z), new System.Numerics.Vector3(max.X, max.Y, max.Z));
    }

    private Control BuildAlignTab()
    {
        var box = Section();
        var note = new Label { Text = "Selecione dois ou mais objetos (Shift soma). Alinhar usa o contorno de cada objeto.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        note.AddThemeColorOverride("font_color", UiStyle.TextMuted); note.AddThemeFontSizeOverride("font_size", 15); box.AddChild(note);
        foreach (var (axis, label, names) in new[] { (AlignAxis.X, "Eixo X", new[] { "Esquerda", "Centro", "Direita" }), (AlignAxis.Y, "Eixo Y (altura)", new[] { "Base", "Meio", "Topo" }), (AlignAxis.Z, "Eixo Z", new[] { "Frente", "Centro", "Trás" }) })
        {
            box.AddChild(FieldLabel(label));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); box.AddChild(row);
            for (var i = 0; i < 3; i++) { var edge = (AlignEdge)i; var captured = axis; row.AddChild(TextButton(names[i], $"Alinha a seleção pelo lado: {names[i].ToLowerInvariant()}", () => AlignSelection(captured, edge))); }
            row.AddChild(TextButton("Distribuir", "Deixa os espaços iguais entre três ou mais objetos", () => DistributeSelection(axis)));
        }
        box.AddChild(FieldLabel("Chão"));
        box.AddChild(TextButton("Ajustar ao solo", "Apoia o ponto mais baixo da seleção no chão logo abaixo", RestSelectionOnGround));
        box.AddChild(FieldLabel("Copiar entre objetos"));
        var copy = new HBoxContainer(); copy.AddThemeConstantOverride("separation", 6); box.AddChild(copy);
        copy.AddChild(TextButton("Copiar posição", "Guarda a posição do primeiro objeto", () => CopyTransform(0)));
        copy.AddChild(TextButton("Copiar rotação", "Guarda a rotação do primeiro objeto", () => CopyTransform(1)));
        copy.AddChild(TextButton("Copiar tamanho", "Guarda o tamanho do primeiro objeto", () => CopyTransform(2)));
        box.AddChild(TextButton("Colar na seleção", "Aplica o que foi copiado", PasteTransform));
        return box;
    }

    private void MoveSelection(float[] offsets, AlignAxis axis, string done)
    {
        if (offsets.All(x => MathF.Abs(x) < 1e-4f)) { _status.Text = "Nada a mover: a seleção já está assim."; return; }
        var afters = new List<BuildObjectState>();
        for (var i = 0; i < _selection.Count; i++)
        {
            var after = FromView(_selection[i]);
            switch (axis) { case AlignAxis.X: after.PositionX += offsets[i]; break; case AlignAxis.Y: after.PositionY += offsets[i]; break; default: after.PositionZ += offsets[i]; break; }
            afters.Add(after);
        }
        CommitUpdates(afters);
        _status.Text = done;
    }

    private void AlignSelection(AlignAxis axis, AlignEdge edge)
    {
        if (_selection.Count < 2) { _status.Text = "Selecione dois ou mais objetos para alinhar."; return; }
        MoveSelection(AlignMath.Align(_selection.Select(WorldBounds).ToList(), axis, edge), axis, "Objetos alinhados.");
    }

    private void DistributeSelection(AlignAxis axis)
    {
        if (_selection.Count < 3) { _status.Text = "Selecione três ou mais objetos para distribuir."; return; }
        MoveSelection(AlignMath.Distribute(_selection.Select(WorldBounds).ToList(), axis), axis, "Objetos distribuídos.");
    }

    private void RestSelectionOnGround()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto primeiro."; return; }
        var boxes = _selection.Select(WorldBounds).ToList();
        var center = boxes.Aggregate(System.Numerics.Vector3.Zero, (sum, x) => sum + x.Center) / boxes.Count;
        var low = boxes.Min(x => x.Min.Y);
        var from = new Vector3(center.X, low + .05f, center.Z);
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 500f, uint.MaxValue & ~BuildGizmo.PhysicsLayer);
        query.Exclude = new Godot.Collections.Array<Rid>(_selection.SelectMany(v => v.GetChildren().OfType<CollisionObject3D>()).Select(x => x.GetRid()));
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        var ground = hit.Count > 0 ? hit["position"].AsVector3().Y : 0f;
        MoveSelection(Enumerable.Repeat(AlignMath.RestOnGround(boxes, ground), _selection.Count).ToArray(), AlignAxis.Y, "Objetos apoiados no solo.");
    }

    private Vector3? _copiedPosition, _copiedRotation, _copiedSize;

    private void CopyTransform(int what)
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto para copiar."; return; }
        var view = _selection[0]; var s = view.State;
        switch (what)
        {
            case 0: _copiedPosition = view.Position; _status.Text = "Posição copiada."; break;
            case 1: _copiedRotation = new Vector3(s.RotationX, s.RotationY, s.RotationZ); _status.Text = "Rotação copiada."; break;
            default: _copiedSize = view.Scale; _status.Text = "Tamanho copiado."; break;
        }
    }

    private void PasteTransform()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que receberão os valores."; return; }
        if (_copiedPosition is null && _copiedRotation is null && _copiedSize is null) { _status.Text = "Nada foi copiado ainda."; return; }
        var afters = new List<BuildObjectState>();
        foreach (var view in _selection)
        {
            var after = FromView(view);
            if (_copiedPosition is { } p) { after.PositionX = p.X; after.PositionY = p.Y; after.PositionZ = p.Z; }
            if (_copiedRotation is { } r) { after.RotationX = r.X; after.RotationY = r.Y; after.RotationZ = r.Z; }
            if (_copiedSize is { } z) { after.ScaleX = ClampScale(z.X); after.ScaleY = ClampScale(z.Y); after.ScaleZ = ClampScale(z.Z); }
            afters.Add(after);
        }
        CommitUpdates(afters);
        _status.Text = "Valores colados.";
    }

    public void AlignForTesting(int axis, int edge) => AlignSelection((AlignAxis)axis, (AlignEdge)edge);
    public void DistributeForTesting(int axis) => DistributeSelection((AlignAxis)axis);
    public void RestOnGroundForTesting() => RestSelectionOnGround();
    public void SelectFaceForTesting(Guid id, int face) { if (_objects.TryGetValue(Resolve(id), out var view)) { SelectView(view, false); _faceTool = true; SelectFace(view, face); } }
    public void PaintFaceForTesting(bool clear) => PaintFace(clear);
    public bool GridVisibleForTesting => _grid.Visible;
    public void SetGridForTesting(bool show, bool snap) { _showGrid.ButtonPressed = show; _gridSnap.ButtonPressed = snap; UpdateGrid(); }
    public void SetPaintColorForTesting(Color color) => _colorPicker.Color = color;
}
