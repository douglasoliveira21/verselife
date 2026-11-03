using System.Globalization;
using Godot;

namespace LifeVerse.Client.Scripts;

/// "Importar modelo 3D": pick a GLB or glTF file, see a spinning preview with its real size, triangles, materials and textures,
/// choose how big it will be when placed, and only then upload it. Models over the limits cannot be confirmed, and the reason
/// is said in plain words. Nothing is sent to the server until the person presses Importar.
public partial class ModelImportDialog : Node
{
    private const string PrefsPath = "user://build_prefs.cfg";
    private FloatingWindow _window = null!;
    private FileDialog _files = null!;
    private SubViewport _viewport = null!;
    private Node3D _pivot = null!;
    private Label _info = null!, _problem = null!;
    private LineEdit _size = null!;
    private Button _confirm = null!;
    private string _path = string.Empty;
    private ModelReport? _report;

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
        _window = new FloatingWindow("Importar modelo 3D", icon: "cube") { Visible = false, Name = "ModelImport" };
        _window.Place(.5f, .5f, -330, -300, 330, 300);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 12);

        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); content.AddChild(row);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(280, 280) };
        frame.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(0, 0, 0, .35f), UiStyle.Border, 14, 1, 0, 0));
        var container = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(280, 280) };
        _viewport = new SubViewport { Size = new Vector2I(280, 280), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _pivot = new Node3D(); _viewport.AddChild(_pivot);
        _viewport.AddChild(new Camera3D { Position = new Vector3(0, .35f, 1.7f), Rotation = new Vector3(Mathf.DegToRad(-12), 0, 0), Current = true });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, 35, 0), LightEnergy = 1.2f });
        _viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.75f, .8f, .9f), AmbientLightEnergy = .8f, BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.12f, .16f, .2f) } });
        container.AddChild(_viewport); frame.AddChild(container); row.AddChild(frame);

        _info = new Label { Text = "Escolha um arquivo GLB ou glTF.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _info.AddThemeFontSizeOverride("font_size", 17); row.AddChild(_info);

        content.AddChild(new Label { Text = "Tamanho ao colocar (metros do lado maior)" });
        _size = new LineEdit { Text = PlacementSize.ToString("0.##", CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(0, 40) };
        content.AddChild(_size);
        _problem = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 44) };
        _problem.AddThemeColorOverride("font_color", UiStyle.Orange); _problem.AddThemeFontSizeOverride("font_size", 16);
        content.AddChild(_problem);

        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8); content.AddChild(buttons);
        buttons.AddChild(Button("Escolher arquivo…", UiStyle.ButtonKind.Secondary, () => _files.PopupCentered(new Vector2I(800, 500))));
        _confirm = Button("Importar", UiStyle.ButtonKind.Primary, Confirm); _confirm.Disabled = true; buttons.AddChild(_confirm);
        buttons.AddChild(Button("Cancelar", UiStyle.ButtonKind.Secondary, Close));

        _files = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.glb,*.gltf ; Modelos 3D"], Title = "Importar modelo 3D" };
        _files.FileSelected += Load;
        AddChild(_files);
    }

    private static Button Button(string text, UiStyle.ButtonKind kind, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.Pressed += action;
        return button;
    }

    public void AttachTo(Node root) => root.AddChild(_window);

    public void Open() { _window.Visible = true; _window.MoveToFront(); _files.PopupCentered(new Vector2I(800, 500)); }
    public void Close() { _window.Visible = false; Clear(); }

    private void Clear()
    {
        foreach (var child in _pivot.GetChildren()) { _pivot.RemoveChild(child); child.QueueFree(); }
        _report = null; _path = string.Empty; _confirm.Disabled = true;
    }

    public override void _Process(double delta) { if (_window.Visible && _pivot.GetChildCount() > 0) _pivot.RotateY((float)delta * .6f); }

    /// Reads and inspects the file, then shows what is in it. Public so tests can load bytes without a file dialog.
    public void Load(string path) => Show(path, Godot.FileAccess.GetFileAsBytes(path));

    public void Show(string path, byte[] bytes)
    {
        Clear();
        _path = path;
        var report = _report = ModelInspector.Inspect(bytes);
        var name = System.IO.Path.GetFileName(path);
        if (report.Scene is { } scene)
        {
            var bounds = ModelCache.Bounds(scene) ?? new Aabb(Vector3.Zero, Vector3.One);
            var longest = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));
            var wrapper = new Node3D();
            scene.Scale = Vector3.One * (1f / longest); scene.Position = -(bounds.Position + bounds.Size / 2f) / longest;
            wrapper.AddChild(scene); _pivot.AddChild(wrapper);
        }
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        _info.Text = report.Scene is null ? name : $"{name}\n\nTamanho original: {report.Size.X.ToString("0.##", culture)} × {report.Size.Y.ToString("0.##", culture)} × {report.Size.Z.ToString("0.##", culture)} m\nTriângulos: {report.Triangles.ToString("N0", culture)}\nMateriais: {report.Materials}\nTexturas: {report.Textures}\nPartes: {report.Parts}\nAnimações: {report.Animations} (ainda não tocam no mundo)\nArquivo: {bytes.Length / 1024:N0} KB";
        _problem.Text = report.Error ?? string.Empty;
        _confirm.Disabled = !report.Valid;
    }

    private void Confirm()
    {
        if (_report is not { Valid: true } || _path.Length == 0) return;
        if (!float.TryParse(_size.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size is < .1f or > 64f) { _problem.Text = "O tamanho deve ficar entre 0,1 e 64 metros."; return; }
        SavePlacementSize(size);
        var path = _path;
        Close();
        Confirmed?.Invoke(path);
    }

    public void ShowForTesting(string path, byte[] bytes) { _window.Visible = true; _window.MoveToFront(); Show(path, bytes); }
    public void ConfirmForTesting(string size) { _size.Text = size; Confirm(); }
    public bool CanConfirmForTesting => !_confirm.Disabled;
    public string InfoForTesting => _info.Text;
    public string ProblemForTesting => _problem.Text;
}
