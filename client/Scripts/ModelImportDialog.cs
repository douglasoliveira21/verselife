using System.Globalization;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// "Importar modelo 3D" (client/Assets/ui/construir): pick a GLB or glTF file, see a turning preview with its real size, triangles,
/// materials and textures, choose how big it will be when placed, and only then upload it. Models over the limits cannot be confirmed,
/// and the reason is said in a banner in plain words. Nothing is sent to the server until the person presses Importar.
public partial class ModelImportDialog : Node
{
    private const string PrefsPath = "user://build_prefs.cfg";
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private FloatingWindow _window = null!;
    private FileDialog _files = null!;
    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Node3D _pivot = null!;
    private Label _fileName = null!, _sizeValue = null!, _trianglesValue = null!, _materialsValue = null!, _texturesValue = null!, _partsValue = null!, _animationsValue = null!, _problem = null!;
    private PanelContainer _banner = null!;
    private readonly LineEdit[] _dims = new LineEdit[3];
    private Button _confirm = null!, _spinButton = null!;
    private string _path = string.Empty;
    private ModelReport? _report;
    private Vector3 _original = Vector3.Zero;
    private float _placement = 2f;
    private float _zoom = 1f;
    private string _info = "Escolha um arquivo GLB ou glTF.";

    /// Raised with the file path once the person confirmed a valid model.
    public event Action<string>? Confirmed;
    public bool Visible => _window.Visible;
    public ModelReport? Report => _report;

    /// How long the longest side of a model is when it is first placed in the world, in metres.
    public static float PlacementSize
    {
        get
        {
            try { var config = new ConfigFile(); if (config.Load(PrefsPath) == Error.Ok) return Mathf.Clamp((float)(double)config.GetValue("models", "placement_size", 2.0), .1f, 64f); }
            catch (Exception) { /* a damaged preferences file means the default */ }
            return 2f;
        }
    }

    public static void SetPlacementSizeForTesting(float size) => SavePlacementSize(size);

    private static void SavePlacementSize(float size)
    {
        var config = new ConfigFile(); config.Load(PrefsPath);
        config.SetValue("models", "placement_size", (double)size); config.Save(PrefsPath);
    }

    public override void _Ready()
    {
        _placement = PlacementSize;
        _window = new FloatingWindow("Importar modelo 3D", icon: "cube") { Visible = false, Name = "ModelImport" };
        _window.Place(.5f, .5f, -650, -290, 650, 290);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 12);

        var main = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; main.AddThemeConstantOverride("separation", 14); content.AddChild(main);

        // the preview and its four buttons
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        frame.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(.02f, .08f, .11f, .78f), new Color(UiStyle.Accent, .5f), 14, 1, 6, 6));
        var frameBody = new VBoxContainer(); frameBody.AddThemeConstantOverride("separation", 6); frame.AddChild(frameBody);
        var container = new SubViewportContainer { Stretch = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _viewport = new SubViewport { Size = new Vector2I(400, 340), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, Msaa3D = Viewport.Msaa.Msaa4X };
        _pivot = new Node3D(); _viewport.AddChild(_pivot);
        _camera = new Camera3D { Fov = 40, KeepAspect = Camera3D.KeepAspectEnum.Width, Current = true };
        _viewport.AddChild(_camera); PlaceCamera();
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, 35, 0), LightEnergy = 1.2f });
        _viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.75f, .8f, .9f), AmbientLightEnergy = .8f, BackgroundMode = Godot.Environment.BGMode.ClearColor } });
        container.AddChild(_viewport); frameBody.AddChild(container);
        container.GuiInput += input => { if (input is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion) _pivot.RotateY(motion.Relative.X * .012f); };
        var pill = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter }; pill.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(0, 0, 0, .55f), new Color(UiStyle.Border, .5f), 22, 1, 10, 4));
        var pillRow = new HBoxContainer(); pillRow.AddThemeConstantOverride("separation", 6); pill.AddChild(pillRow);
        _spinButton = PillButton("reset", "Gira sozinho (ligado) ou deixa parado; arraste a imagem para girar à mão", () => { _spinButton.ButtonPressed = !_spinButton.ButtonPressed; }, toggle: true); _spinButton.ButtonPressed = true; pillRow.AddChild(_spinButton);
        pillRow.AddChild(PillButton("move-all", "Volta ao enquadramento original", () => { _zoom = 1f; PlaceCamera(); }));
        pillRow.AddChild(PillButton("zoom-in", "Aproxima", () => { _zoom = Mathf.Clamp(_zoom * .8f, .3f, 3f); PlaceCamera(); }));
        pillRow.AddChild(PillButton("zoom-out", "Afasta", () => { _zoom = Mathf.Clamp(_zoom * 1.25f, .3f, 3f); PlaceCamera(); }));
        frameBody.AddChild(pill);
        main.AddChild(frame);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 12); main.AddChild(right);
        var cards = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cards.AddThemeConstantOverride("separation", 12); right.AddChild(cards);

        // what is in the file
        var info = Card("Informações do modelo", "info", out var infoBody, out _, 6); info.SizeFlagsStretchRatio = 1.15f;
        _fileName = Muted("Nenhum arquivo escolhido", 17); _fileName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _fileName.AutowrapMode = TextServer.AutowrapMode.Off; infoBody.AddChild(_fileName);
        infoBody.AddChild(InfoRow("cube", "Tamanho original:", out _sizeValue));
        infoBody.AddChild(InfoRow("triangle", "Triângulos:", out _trianglesValue));
        infoBody.AddChild(InfoRow("layers", "Materiais:", out _materialsValue));
        infoBody.AddChild(InfoRow("image", "Texturas:", out _texturesValue));
        infoBody.AddChild(InfoRow("grid", "Partes (meshes):", out _partsValue));
        infoBody.AddChild(InfoRow("run", "Animações:", out _animationsValue));
        cards.AddChild(info);

        // how big it will be
        var sizeCard = Card("Tamanho ao colocar", "cube", out var sizeBody, out _, 8); sizeCard.SizeFlagsStretchRatio = .85f;
        var sizeRow = new HBoxContainer(); sizeRow.AddThemeConstantOverride("separation", 10); sizeBody.AddChild(sizeRow);
        var fields = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; fields.AddThemeConstantOverride("separation", 10); sizeRow.AddChild(fields);
        var names = new[] { "Largura (X)", "Altura (Y)", "Profundidade (Z)" };
        for (var i = 0; i < 3; i++) fields.AddChild(DimRow(i, names[i]));
        var link = new Button { Icon = UiStyle.Icon("link", UiStyle.AccentText, 28), TooltipText = "As proporções do modelo são sempre mantidas: mudar um lado muda os outros", Disabled = false, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(52, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, ExpandIcon = false };
        UiStyle.StyleButton(link, UiStyle.ButtonKind.ToolSelected, 6, 6, 12); link.AddThemeConstantOverride("icon_max_width", 28); link.CustomMinimumSize = new Vector2(52, 96); sizeRow.AddChild(link);
        sizeBody.AddChild(Muted("Mantém as proporções do modelo.", 16));
        cards.AddChild(sizeCard);

        _banner = new PanelContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _banner.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.OrangeFill, UiStyle.Orange, 12, 2, 16, 10));
        var bannerRow = new HBoxContainer(); bannerRow.AddThemeConstantOverride("separation", 12); _banner.AddChild(bannerRow);
        bannerRow.AddChild(UiStyle.IconRect("alert", UiStyle.Orange, 30));
        _problem = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        _problem.AddThemeFontOverride("font", UiStyle.Bold); _problem.AddThemeFontSizeOverride("font_size", 21); bannerRow.AddChild(_problem);
        right.AddChild(_banner);

        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 12); right.AddChild(buttons);
        buttons.AddChild(Act("folder", "Escolher arquivo…", UiStyle.ButtonKind.Secondary, () => _files.PopupCentered(new Vector2I(800, 500)), 1.4f));
        _confirm = Act("download", "Importar", UiStyle.ButtonKind.Primary, Confirm, 1f); _confirm.Disabled = true; buttons.AddChild(_confirm);
        buttons.AddChild(OrangeButtonFilled(Act("close", "Cancelar", UiStyle.ButtonKind.Secondary, Close, .9f)));

        _files = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.glb,*.gltf ; Modelos 3D"], Title = "Importar modelo 3D" };
        _files.FileSelected += Load;
        AddChild(_files);
        ShowDimensions();
    }

    private static Label Muted(string text, int size) { var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center }; label.AddThemeColorOverride("font_color", UiStyle.TextMuted); label.AddThemeFontSizeOverride("font_size", size); return label; }

    /// A card with a title row and a thin line, like the ones of the build window.
    private static PanelContainer Card(string title, string icon, out VBoxContainer body, out HBoxContainer header, int gap)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(.07f, .12f, .15f, .66f), new Color(UiStyle.BorderSoft, .75f), 14, 1, 16, 12));
        body = new VBoxContainer(); body.AddThemeConstantOverride("separation", gap); card.AddChild(body);
        header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 10);
        header.AddChild(UiStyle.IconRect(icon, UiStyle.Text, 26));
        var heading = new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center }; heading.AddThemeFontOverride("font", UiStyle.Bold); heading.AddThemeFontSizeOverride("font_size", 22); header.AddChild(heading);
        body.AddChild(header);
        body.AddChild(new ColorRect { CustomMinimumSize = new Vector2(0, 1), Color = new Color(UiStyle.Divider, .75f), MouseFilter = Control.MouseFilterEnum.Ignore });
        return card;
    }

    private static HBoxContainer InfoRow(string icon, string label, out Label value)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        row.AddChild(UiStyle.IconRect(icon, UiStyle.Text, 26));
        var name = new Label { Text = label, VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; name.AddThemeFontOverride("font", UiStyle.Medium); name.AddThemeFontSizeOverride("font_size", 20); row.AddChild(name);
        value = new Label { Text = "—", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; value.AddThemeFontOverride("font", UiStyle.Medium); value.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(value);
        return row;
    }

    private HBoxContainer DimRow(int index, string label)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        var name = new Label { Text = label, VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; name.AddThemeFontSizeOverride("font_size", 20); name.AddThemeFontOverride("font", UiStyle.Medium); row.AddChild(name);
        var edit = _dims[index] = new LineEdit { Text = "—", Editable = false, CustomMinimumSize = new Vector2(110, 44), Alignment = HorizontalAlignment.Left, MaxLength = 10 };
        edit.AddThemeFontSizeOverride("font_size", 20);
        edit.TextSubmitted += _ => DimensionTyped(index); edit.FocusExited += () => DimensionTyped(index);
        row.AddChild(edit);
        var arrows = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; arrows.AddThemeConstantOverride("separation", 0);
        foreach (var (icon, sign) in new[] { ("caret-up", 1), ("caret-down", -1) })
        {
            var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 16), Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 22), ExpandIcon = false };
            var none = new StyleBoxEmpty(); var hover = UiStyle.Box(new Color(1, 1, 1, .12f), new Color(0, 0, 0, 0), 5, 0, 0, 0);
            button.AddThemeStyleboxOverride("normal", none); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", hover); button.AddThemeStyleboxOverride("focus", none);
            var direction = sign; button.Pressed += () => { if (_original[index] > 1e-4f) SetDimension(index, _original[index] * (_placement / Longest()) + direction * .1f); };
            arrows.AddChild(button);
        }
        row.AddChild(arrows);
        var unit = new Label { Text = "m", VerticalAlignment = VerticalAlignment.Center }; unit.AddThemeFontSizeOverride("font_size", 20); row.AddChild(unit);
        return row;
    }

    private static Button PillButton(string icon, string tip, Action action, bool toggle = false)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 28), TooltipText = tip, ToggleMode = toggle, Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(52, 44), ExpandIcon = false };
        var hover = UiStyle.Box(new Color(1, 1, 1, .12f), new Color(0, 0, 0, 0), 12, 0, 0, 0); var none = new StyleBoxEmpty();
        button.AddThemeStyleboxOverride("normal", none); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", none); button.AddThemeStyleboxOverride("hover_pressed", hover); button.AddThemeStyleboxOverride("focus", none);
        button.AddThemeConstantOverride("icon_max_width", 28);
        button.Pressed += action;
        return button;
    }

    private static Button Act(string icon, string text, UiStyle.ButtonKind kind, Action action, float weight)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 26), CustomMinimumSize = new Vector2(0, 54), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = weight, FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        UiStyle.StyleButton(button, kind, 12, 6); button.AddThemeFontSizeOverride("font_size", 21); button.AddThemeFontOverride("font", UiStyle.Bold); button.AddThemeConstantOverride("icon_max_width", 28);
        button.Pressed += action;
        return button;
    }

    private static Button OrangeButtonFilled(Button button)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            button.AddThemeStyleboxOverride(state, UiStyle.Box(state == "hover" ? UiStyle.OrangeFillHover : UiStyle.OrangeFill, UiStyle.Orange, 12, 2, 12, 6));
        return button;
    }

    public void AttachTo(Node root) => root.AddChild(_window);

    public void Open() { _window.Visible = true; _window.MoveToFront(); _files.PopupCentered(new Vector2I(800, 500)); }
    public void Close() { _window.Visible = false; Clear(); }

    private void PlaceCamera() => _camera.Transform = new Transform3D(Basis.Identity, new Vector3(1.9f, 1.35f, 2.9f) * _zoom).LookingAt(new Vector3(0, -.02f, 0), Vector3.Up);

    private void Clear()
    {
        foreach (var child in _pivot.GetChildren()) { _pivot.RemoveChild(child); child.QueueFree(); }
        _report = null; _path = string.Empty; _confirm.Disabled = true; _original = Vector3.Zero; _info = "Escolha um arquivo GLB ou glTF.";
        _fileName.Text = "Nenhum arquivo escolhido"; _problem.Text = string.Empty; _banner.Visible = false;
        foreach (var value in new[] { _sizeValue, _trianglesValue, _materialsValue, _texturesValue, _partsValue, _animationsValue }) { value.Text = "—"; value.RemoveThemeColorOverride("font_color"); }
        ShowDimensions();
    }

    public override void _Process(double delta) { if (_window.Visible && _spinButton.ButtonPressed && _pivot.GetChildCount() > 0) _pivot.RotateY((float)delta * .6f); }

    /// Reads and inspects the file, then shows what is in it. Public so tests can load bytes without a file dialog.
    public void Load(string path) => Show(path, Godot.FileAccess.GetFileAsBytes(path));

    private static string Metres(float value) => value.ToString("0.00", Pt);
    private float Longest() => Mathf.Max(.0001f, Mathf.Max(_original.X, Mathf.Max(_original.Y, _original.Z)));

    public void Show(string path, byte[] bytes)
    {
        Clear();
        _placement = PlacementSize;
        _path = path;
        var report = _report = ModelInspector.Inspect(bytes);
        var name = System.IO.Path.GetFileName(path);
        _fileName.Text = name;
        if (report.Scene is { } scene)
        {
            var bounds = ModelCache.Bounds(scene) ?? new Aabb(Vector3.Zero, Vector3.One);
            var longest = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));
            var wrapper = new Node3D();
            scene.Scale = Vector3.One * (1.3f / longest); scene.Position = -(bounds.Position + bounds.Size / 2f) * (1.3f / longest);
            wrapper.AddChild(scene); _pivot.AddChild(wrapper);
            _original = report.Size;
            _sizeValue.Text = $"{Metres(report.Size.X)} m × {Metres(report.Size.Y)} m × {Metres(report.Size.Z)} m";
            _trianglesValue.Text = report.Triangles.ToString("N0", Pt);
            _trianglesValue.AddThemeColorOverride("font_color", report.Triangles > ModelLimits.MaxTriangles ? UiStyle.DangerBright : UiStyle.SuccessBright);
            _materialsValue.Text = report.Materials.ToString(Pt); _texturesValue.Text = report.Textures.ToString(Pt); _partsValue.Text = report.Parts.ToString(Pt);
            _animationsValue.Text = report.Animations > 0 ? $"{report.Animations} (ainda não tocam no mundo)" : "0";
            _info = $"{name}\n\nTamanho original: {report.Size.X.ToString("0.##", Pt)} × {report.Size.Y.ToString("0.##", Pt)} × {report.Size.Z.ToString("0.##", Pt)} m\nTriângulos: {report.Triangles.ToString("N0", Pt)}\nMateriais: {report.Materials}\nTexturas: {report.Textures}\nPartes: {report.Parts}\nAnimações: {report.Animations} (ainda não tocam no mundo)\nArquivo: {bytes.Length / 1024:N0} KB";
        }
        else _info = name;
        ShowDimensions();
        SetProblem(report.Error ?? string.Empty);
        _confirm.Disabled = !report.Valid;
    }

    private void SetProblem(string text) { _problem.Text = text; _banner.Visible = text.Length > 0; }

    /// The three sides the model will have when placed: its own proportions, scaled so the longest side is the chosen size.
    private void ShowDimensions()
    {
        for (var i = 0; i < 3; i++)
        {
            var known = _original[i] > 1e-4f;
            _dims[i].Editable = known;
            _dims[i].Text = known ? Metres(_original[i] * (_placement / Longest())) : "—";
        }
    }

    private void DimensionTyped(int index)
    {
        if (_original[index] <= 1e-4f || !float.TryParse(_dims[index].Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value)) { ShowDimensions(); return; }
        SetDimension(index, value);
    }

    private void SetDimension(int index, float metres)
    {
        _placement = metres / _original[index] * Longest();
        _problem.Text = string.Empty; if (_report?.Error is null) _banner.Visible = false;
        ShowDimensions();
    }

    private void Confirm() => ConfirmWith(_placement);

    private void ConfirmWith(float size)
    {
        if (_report is not { Valid: true } || _path.Length == 0) return;
        if (!float.IsFinite(size) || size is < .1f or > 64f) { SetProblem("O tamanho deve ficar entre 0,1 e 64 metros."); return; }
        SavePlacementSize(size);
        var path = _path;
        Close();
        Confirmed?.Invoke(path);
    }

    /// Screenshots: shows the refusal of a model over the limits.
    public void ShowRefusalForTesting(string text, int triangles) { _trianglesValue.Text = triangles.ToString("N0", Pt); _trianglesValue.AddThemeColorOverride("font_color", UiStyle.DangerBright); SetProblem(text); _confirm.Disabled = true; }
    public void ShowForTesting(string path, byte[] bytes) { _window.Visible = true; _window.MoveToFront(); Show(path, bytes); }
    public void ConfirmForTesting(string size) => ConfirmWith(float.TryParse(size.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : float.NaN);
    public bool CanConfirmForTesting => !_confirm.Disabled;
    public string InfoForTesting => _info;
    public string ProblemForTesting => _problem.Text;
}
