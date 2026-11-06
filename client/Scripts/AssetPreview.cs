using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Audio;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>The bytes of an asset for a preview: kept in memory and on disk by content hash (checked against the hash, so a damaged copy is fetched again), otherwise downloaded.</summary>
public static class AssetBytes
{
    public const long MaxBytes = 40L * 1024 * 1024;
    /// <summary>Tests: answers a download without a network (asset id, hash); null means it failed.</summary>
    public static Func<Guid, string, Task<byte[]?>>? Provider { get; set; }
    private static readonly Dictionary<string, byte[]> Memory = [];
    private static readonly Queue<string> Recent = new();

    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool ValidKey(string hash) => hash.Length == 64 && hash.All(Uri.IsHexDigit);

    public static async Task<byte[]?> GetAsync(Node host, Guid assetId, string hash, long size)
    {
        hash = (hash ?? string.Empty).ToLowerInvariant(); var key = ValidKey(hash) ? hash : assetId.ToString("N");
        if (Memory.TryGetValue(key, out var known)) return known;
        var path = $"user://asset_cache/{key}.bin";
        if (ValidKey(hash) && Godot.FileAccess.FileExists(path)) { var stored = Godot.FileAccess.GetFileAsBytes(path); if (Hex(stored) == hash) return Remember(key, stored); }
        if (size > MaxBytes) return null;
        byte[]? bytes;
        if (Provider is not null) bytes = await Provider(assetId, hash);
        else
        {
            var session = host.GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = host.GetNodeOrNull<AppManager>("/root/AppManager");
            if (session is null || app is null || !session.IsAuthenticated) return null;
            var http = new HttpRequest { Timeout = 60, BodySizeLimit = (int)MaxBytes }; host.AddChild(http);
            try
            {
                if (http.Request($"{app.ApiBaseUrl}/api/v1/assets/{assetId}/download", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) return null;
                var answer = await host.ToSignal(http, HttpRequest.SignalName.RequestCompleted);
                bytes = (long)answer[1] is >= 200 and < 300 ? (byte[])answer[3] : null;
            }
            finally { http.QueueFree(); }
        }
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxBytes) return null;
        if (ValidKey(hash))
        {
            if (Hex(bytes) != hash) return null; // not what the inventory says it is
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://asset_cache"));
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write); file?.StoreBuffer(bytes);
        }
        return Remember(key, bytes);
    }

    private static byte[] Remember(string key, byte[] bytes)
    {
        if (Memory.TryAdd(key, bytes)) { Recent.Enqueue(key); while (Recent.Count > 6) Memory.Remove(Recent.Dequeue()); }
        return bytes;
    }

    public static void ForgetAllForTesting() { Memory.Clear(); Recent.Clear(); }
    public static int RememberedForTesting => Memory.Count;
}

public enum PreviewKind { None, Model, Texture, Animation, Audio, Package }

/// <summary>
/// "Pré-visualizar": a window that shows what an inventory item really is before it is used. A 3D model turns in a small stage with its real size, triangles and
/// materials; a texture can be zoomed and moved over a chequered background; an animation plays on a bare skeleton, with a scrubber, speed and the list of its
/// clips; a sound plays with a position bar, volume, loop and (for WAV files) its waveform. Nothing is placed in the world and nothing is changed.
/// </summary>
public partial class AssetPreview : Node
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private FloatingWindow _window = null!;
    private Label _title = null!, _info = null!, _problem = null!;
    private VBoxContainer _stage = null!;
    private int _token;
    private double _clock;
    private PreviewKind _kind;
    private Func<double, bool>? _tick;

    public bool Visible => _window.Visible;
    public PreviewKind Kind => _kind;
    public string InfoForTesting => _info.Text;
    public string ProblemForTesting => _problem.Text;
    public string TitleForTesting => _title.Text;

    public static PreviewKind KindOf(string category, string mime)
    {
        mime = (mime ?? string.Empty).ToLowerInvariant(); category = (category ?? string.Empty).ToLowerInvariant();
        if (category == "packages") return PreviewKind.Package;
        if (category == "animations") return PreviewKind.Animation;
        if (mime.StartsWith("model/", StringComparison.Ordinal) || category == "models") return PreviewKind.Model;
        if (mime.StartsWith("image/", StringComparison.Ordinal) || category == "textures") return PreviewKind.Texture;
        if (mime.StartsWith("audio/", StringComparison.Ordinal) || category == "sounds") return PreviewKind.Audio;
        return PreviewKind.None;
    }

    public override void _Ready()
    {
        _window = new FloatingWindow("Pré-visualizar", icon: "image") { Visible = false, Name = "AssetPreviewWindow" };
        _window.Place(.5f, .5f, -330, -300, 330, 300);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 10);
        _title = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; _title.AddThemeFontSizeOverride("font_size", 22); _title.AddThemeFontOverride("font", UiStyle.Bold); content.AddChild(_title);
        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _info.AddThemeColorOverride("font_color", UiStyle.TextMuted); _info.AddThemeFontSizeOverride("font_size", 16); content.AddChild(_info);
        _stage = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _stage.AddThemeConstantOverride("separation", 8); content.AddChild(_stage);
        _problem = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _problem.AddThemeColorOverride("font_color", UiStyle.Orange); _problem.AddThemeFontSizeOverride("font_size", 16); content.AddChild(_problem);
        AddChild(_window); _window.CloseRequested += Close;
    }

    public void Close() { _token++; Clear(); _window.Visible = false; }

    private void Clear()
    {
        _tick = null; _clips = null; _figure = null; _clip = null; _animationBytes = null; _scrub = null; _clipInfo = null; _picture = null; _pictureHost = null; _modelViewport = null; _modelPivot = null; _modelCamera = null; _player = null; _position = null; _wave = null; _time_label = null;
        foreach (var child in _stage.GetChildren()) { _stage.RemoveChild(child); child.QueueFree(); }
        _problem.Text = string.Empty;
    }

    public override void _Process(double delta)
    {
        if (_tick is null) return;
        if (!_tick(delta)) _tick = null;
    }

    /// <summary>Opens the preview for an item. Returns what kind of preview it is (None when this kind of item has none).</summary>
    public async Task<PreviewKind> OpenAsync(Guid assetId, string name, string category, string mime, string hash, long size, int version = 1)
    {
        var token = ++_token; var kind = KindOf(category, mime); Clear(); _kind = kind;
        _title.Text = name; _info.Text = $"{category} · {FormatSize(size)} · v{version}";
        _window.Visible = true; _window.MoveToFront();
        if (kind == PreviewKind.None) { _problem.Text = "Esse tipo de item não tem pré-visualização."; return kind; }
        if (kind == PreviewKind.Package) { _problem.Text = "Para uma construção, abra o conteúdo pelo inventário."; return kind; }
        if (size > AssetBytes.MaxBytes) { _problem.Text = $"Grande demais para pré-visualizar ({FormatSize(size)}; o limite é {FormatSize(AssetBytes.MaxBytes)})."; return kind; }
        _problem.Text = "Carregando…";
        var bytes = await AssetBytes.GetAsync(this, assetId, hash, size);
        if (token != _token) return kind; // another item was opened meanwhile
        if (bytes is null) { _problem.Text = "Não foi possível carregar o arquivo (sem conexão, sem permissão ou o arquivo está diferente do registrado)."; return kind; }
        _problem.Text = string.Empty;
        switch (kind)
        {
            case PreviewKind.Model: ShowModel(bytes); break;
            case PreviewKind.Texture: ShowTexture(bytes, mime); break;
            case PreviewKind.Animation: ShowAnimation(bytes); break;
            case PreviewKind.Audio: ShowAudio(bytes, mime); break;
        }
        return kind;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0", Pt) + " MB";
        return (bytes / 1024d).ToString("0", Pt) + " KB";
    }

    private static Button Tool(string text, string tip, Action action) { var button = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) }; button.Pressed += action; return button; }
    private static HBoxContainer Row() { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); return row; }

    // ---- 3D models ----------------------------------------------------------------------------------------------------------------

    private SubViewport? _modelViewport; private Node3D? _modelPivot; private Camera3D? _modelCamera; private float _yaw = .6f, _pitch = -.25f, _distance = 3f, _fit = 3f; private bool _spin = true;
    public Node3D? ModelForTesting => _modelPivot;
    public float ModelDistanceForTesting => _distance;
    public bool SpinningForTesting => _spin;

    private void ShowModel(byte[] bytes)
    {
        var report = ModelInspector.Inspect(bytes);
        if (!report.Valid || report.Scene is null) { _problem.Text = $"Modelo inválido: {report.Error}"; return; }
        _info.Text += $"\nTamanho {report.Size.X.ToString("0.##", Pt)} × {report.Size.Y.ToString("0.##", Pt)} × {report.Size.Z.ToString("0.##", Pt)} m · {report.Triangles:N0} triângulos · {report.Materials} materiais · {report.Textures} texturas · {report.Parts} peça(s){(report.Animations > 0 ? $" · {report.Animations} animação(ões)" : string.Empty)}".Replace(",", ".");
        var container = new SubViewportContainer { Stretch = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 330), MouseFilter = Control.MouseFilterEnum.Stop };
        _modelViewport = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible };
        container.AddChild(_modelViewport);
        _modelViewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -35, 0), LightEnergy = 1.25f });
        _modelViewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.85f, .88f, .95f), AmbientLightEnergy = .75f } });
        _modelCamera = new Camera3D { Fov = 35, Current = true }; _modelViewport.AddChild(_modelCamera);
        _modelPivot = new Node3D(); _modelViewport.AddChild(_modelPivot); _modelPivot.AddChild(report.Scene);
        var bounds = ModelCache.Bounds(report.Scene); var center = bounds is { } b ? b.GetCenter() : Vector3.Zero; var radius = Mathf.Max(report.Size.Length() / 2f, .05f);
        report.Scene.Position = -center; _fit = _distance = radius / Mathf.Tan(Mathf.DegToRad(_modelCamera.Fov) / 2f) * 1.15f + radius * .2f; _spin = true;
        ApplyOrbit();
        container.GuiInput += input =>
        {
            if (input is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion) { _spin = false; _yaw -= motion.Relative.X * .01f; _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * .01f, -1.4f, 1.4f); ApplyOrbit(); }
            else if (input is InputEventMouseButton { Pressed: true } wheel && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) { Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? .88f : 1.14f); }
        };
        _stage.AddChild(container);
        var controls = Row(); _stage.AddChild(controls);
        controls.AddChild(Tool("Girar sozinho", "Liga ou desliga o giro automático", () => _spin = !_spin));
        controls.AddChild(Tool("Reenquadrar", "Volta a câmera para mostrar o modelo inteiro", () => { _distance = _fit; _yaw = .6f; _pitch = -.25f; _spin = true; ApplyOrbit(); }));
        controls.AddChild(Tool("Aproximar", "Chega mais perto", () => Zoom(.8f))); controls.AddChild(Tool("Afastar", "Sai para longe", () => Zoom(1.25f)));
        _tick = delta => { if (_spin && IsInstanceValid(_modelPivot)) { _yaw += (float)delta * .6f; ApplyOrbit(); } return IsInstanceValid(_modelPivot); };
    }

    private void Zoom(float factor) { _distance = Mathf.Clamp(_distance * factor, _fit * .15f, _fit * 6f); ApplyOrbit(); }

    private void ApplyOrbit()
    {
        if (_modelCamera is null || !IsInstanceValid(_modelCamera)) return;
        var position = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), -Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance;
        _modelCamera.Transform = new Transform3D(Basis.Identity, position).LookingAt(Vector3.Zero, Vector3.Up);
    }

    public void ZoomForTesting(float factor) => Zoom(factor);
    public void ToggleSpinForTesting() => _spin = !_spin;

    // ---- textures -----------------------------------------------------------------------------------------------------------------

    private TextureRect? _picture; private Control? _pictureHost; private float _pictureZoom = 1f; private Vector2 _pictureOffset;
    public float TextureZoomForTesting => _pictureZoom;
    public Vector2I TextureSizeForTesting => _picture?.Texture is { } t ? new Vector2I(t.GetWidth(), t.GetHeight()) : Vector2I.Zero;

    private void ShowTexture(byte[] bytes, string mime)
    {
        var image = new Image();
        var error = mime.ToLowerInvariant() switch { "image/png" => image.LoadPngFromBuffer(bytes), "image/jpeg" => image.LoadJpgFromBuffer(bytes), "image/webp" => image.LoadWebpFromBuffer(bytes), _ => Error.FileUnrecognized };
        if (error != Error.Ok || image.IsEmpty()) { _problem.Text = "Não foi possível ler a imagem."; return; }
        _info.Text += $"\n{image.GetWidth()} × {image.GetHeight()} pixels · {(image.DetectAlpha() != Image.AlphaMode.None ? "com transparência" : "opaca")}";
        var texture = ImageTexture.CreateFromImage(image);
        _pictureHost = new Control { ClipContents = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 330), MouseFilter = Control.MouseFilterEnum.Stop };
        _pictureHost.AddChild(new Checker { MouseFilter = Control.MouseFilterEnum.Ignore });
        _picture = new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        _pictureHost.AddChild(_picture); _stage.AddChild(_pictureHost);
        _pictureHost.Resized += FitPicture; FitPicture();
        _pictureHost.GuiInput += input =>
        {
            if (input is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion) { _pictureOffset += motion.Relative; PlacePicture(); }
            else if (input is InputEventMouseButton { Pressed: true } wheel && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) ZoomPicture(wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1f / 1.15f, wheel.Position);
        };
        var controls = Row(); _stage.AddChild(controls);
        controls.AddChild(Tool("Ajustar à janela", "Mostra a imagem inteira", FitPicture)); controls.AddChild(Tool("100%", "Um pixel da imagem por pixel da tela", ActualSize));
        controls.AddChild(Tool("Aproximar", "Zoom +", () => ZoomPicture(1.4f, null))); controls.AddChild(Tool("Afastar", "Zoom −", () => ZoomPicture(1f / 1.4f, null)));
    }

    /// <summary>A chequered background, so transparent parts of a texture can be seen as transparent.</summary>
    private sealed partial class Checker : Control
    {
        public Checker() { SetAnchorsPreset(LayoutPreset.FullRect); }
        public override void _Draw()
        {
            const float cell = 16; var a = new Color(.22f, .24f, .27f); var b = new Color(.30f, .32f, .36f);
            for (var y = 0; y * cell < Size.Y; y++) for (var x = 0; x * cell < Size.X; x++) DrawRect(new Rect2(x * cell, y * cell, cell, cell), (x + y) % 2 == 0 ? a : b);
        }
    }

    private void FitPicture()
    {
        if (_picture?.Texture is not { } t || _pictureHost is null || _pictureHost.Size.X < 1) return;
        _pictureZoom = Mathf.Min(_pictureHost.Size.X / t.GetWidth(), _pictureHost.Size.Y / t.GetHeight()); _pictureOffset = Vector2.Zero; PlacePicture();
    }

    private void ActualSize() { _pictureZoom = 1f; _pictureOffset = Vector2.Zero; PlacePicture(); }

    private void ZoomPicture(float factor, Vector2? around)
    {
        if (_picture?.Texture is null || _pictureHost is null) return;
        var before = _pictureZoom; _pictureZoom = Mathf.Clamp(_pictureZoom * factor, .02f, 32f);
        var anchor = around ?? _pictureHost.Size / 2f; _pictureOffset = anchor - _pictureHost.Size / 2f - (anchor - _pictureHost.Size / 2f - _pictureOffset) * (_pictureZoom / before); PlacePicture();
    }

    private void PlacePicture()
    {
        if (_picture?.Texture is not { } t || _pictureHost is null) return;
        var size = new Vector2(t.GetWidth(), t.GetHeight()) * _pictureZoom; _picture.Size = size; _picture.Position = _pictureHost.Size / 2f - size / 2f + _pictureOffset;
    }

    public void FitForTesting() => FitPicture();
    public void ActualSizeForTesting() => ActualSize();
    public void ZoomPictureForTesting(float factor) => ZoomPicture(factor, null);

    // ---- animations ---------------------------------------------------------------------------------------------------------------

    private StickView? _figure; private SkeletonClip? _clip; private byte[]? _animationBytes; private HSlider? _scrub; private Label? _clipInfo; private bool _playing = true, _loopClip = true; private float _speed = 1f, _time; private bool _scrubbing;
    private OptionButton? _clips;
    public string[] ClipNamesForTesting => _clips is null ? [] : Enumerable.Range(0, _clips.ItemCount).Select(_clips.GetItemText).ToArray();
    public float ClipTimeForTesting => _time;
    public bool PlayingForTesting => _playing;
    public string? PoseSignatureForTesting => _figure?.Signature;

    private void ShowAnimation(byte[] bytes)
    {
        var report = AnimationValidator.Validate(bytes);
        var names = SkeletonClip.Names(bytes);
        if (names is null || names.Count == 0 || !report.Clips.Any()) { _problem.Text = "Esse arquivo não tem uma animação que o jogo reconheça." + (report.Errors.FirstOrDefault() is { } first ? $" ({first.Message})" : string.Empty); return; }
        _animationBytes = bytes;
        _figure = new StickView { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 300), MouseFilter = Control.MouseFilterEnum.Stop }; _stage.AddChild(_figure);
        var pick = Row(); _stage.AddChild(pick);
        _clips = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None }; foreach (var n in names) _clips.AddItem(n);
        pick.AddChild(new Label { Text = "Animação" }); pick.AddChild(_clips); _clips.ItemSelected += index => SelectClip((int)index);
        _clipInfo = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _clipInfo.AddThemeColorOverride("font_color", UiStyle.TextMuted); _clipInfo.AddThemeFontSizeOverride("font_size", 15); _stage.AddChild(_clipInfo);
        _scrub = new HSlider { MinValue = 0, MaxValue = 1, Step = .001, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _stage.AddChild(_scrub);
        _scrub.DragStarted += () => _scrubbing = true; _scrub.DragEnded += _ => _scrubbing = false; _scrub.ValueChanged += value => { if (_scrubbing && _clip is not null) { _time = (float)value * _clip.Duration; _playing = false; Draw(); } };
        var controls = Row(); _stage.AddChild(controls);
        var play = Tool("Pausar", "Toca ou pausa", () => { }); play.Pressed += () => { _playing = !_playing; play.Text = _playing ? "Pausar" : "Tocar"; }; controls.AddChild(play);
        controls.AddChild(Tool("Do início", "Volta ao começo", () => { _time = 0; Draw(); }));
        var loop = new CheckBox { Text = "Repetir", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; loop.Toggled += on => _loopClip = on; controls.AddChild(loop);
        var speed = new OptionButton { FocusMode = Control.FocusModeEnum.None }; foreach (var s in new[] { "0,25×", "0,5×", "1×", "1,5×", "2×" }) speed.AddItem(s); speed.Select(2); speed.ItemSelected += i => _speed = new[] { .25f, .5f, 1f, 1.5f, 2f }[i]; controls.AddChild(speed);
        _playing = true; _loopClip = true; _speed = 1f; SelectClip(0);
        _tick = delta => { AdvanceClip((float)delta); return IsInstanceValid(_figure); };
    }

    private void SelectClip(int index)
    {
        if (_animationBytes is null || _clips is null) return;
        _clip = SkeletonClip.Read(_animationBytes, _clips.GetItemText(index)); _time = 0;
        if (_clip is null) { _problem.Text = "Não foi possível ler essa animação."; return; }
        var info = AnimationValidator.Validate(_animationBytes).Clips.FirstOrDefault(c => c.Name == _clips.GetItemText(index));
        _clipInfo!.Text = info is null ? string.Empty : $"{info.Duration.ToString("0.##", Pt)} s · {info.Bones} ossos · {info.Keys} chaves{(info.RootMotion ? " · move o corpo todo" : string.Empty)}{(info.Loopable ? " · emenda no início (boa para repetir)" : " · não emenda no início")}";
        Draw();
    }

    private void AdvanceClip(float delta)
    {
        if (_clip is null || _clip.Duration <= 0) return;
        if (_playing && !_scrubbing)
        {
            _time += delta * _speed;
            if (_time >= _clip.Duration) { if (_loopClip) _time %= _clip.Duration; else { _time = _clip.Duration; _playing = false; } }
        }
        if (_scrub is not null && !_scrubbing) _scrub.SetValueNoSignal(_time / _clip.Duration);
        Draw();
    }

    private void Draw() { if (_clip is not null && _figure is not null) _figure.Show(StickFigure.Pose(_clip, _time, _loopClip)); }

    public void AdvanceForTesting(float seconds) => AdvanceClip(seconds);
    public void SelectClipForTesting(int index) { _clips?.Select(index); SelectClip(index); }
    public void SetPlayingForTesting(bool playing) => _playing = playing;
    public void SetSpeedForTesting(float speed) => _speed = speed;

    /// <summary>The bare skeleton, drawn from the front (drag to turn it).</summary>
    private sealed partial class StickView : Control
    {
        private Dictionary<string, System.Numerics.Vector3>? _pose; private float _yaw;
        public string Signature => _pose is null ? string.Empty : string.Join(";", _pose.OrderBy(p => p.Key).Select(p => $"{p.Value.X:0.000},{p.Value.Y:0.000},{p.Value.Z:0.000}"));
        public StickView() { GuiInput += input => { if (input is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion) { _yaw += motion.Relative.X * .012f; QueueRedraw(); } }; }
        public void Show(Dictionary<string, System.Numerics.Vector3> pose) { _pose = pose; QueueRedraw(); }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(.03f, .08f, .11f, .85f));
            if (_pose is null) return;
            var scale = Mathf.Min(Size.Y / 2.1f, Size.X / 2.4f); var floor = new Vector2(Size.X / 2f, Size.Y - 18f);
            Vector2 Screen(System.Numerics.Vector3 p) { var x = p.X * Mathf.Cos(_yaw) + p.Z * Mathf.Sin(_yaw); return floor + new Vector2(x, -p.Y) * scale; }
            DrawLine(floor + new Vector2(-Size.X * .4f, 0), floor + new Vector2(Size.X * .4f, 0), new Color(UiStyle.Accent, .5f), 2f);
            foreach (var (from, to) in StickFigure.Lines()) if (_pose.TryGetValue(from, out var a) && _pose.TryGetValue(to, out var b)) DrawLine(Screen(a), Screen(b), UiStyle.Accent, 5f, true);
            foreach (var (bone, p) in _pose) DrawCircle(Screen(p), bone == "head" ? 14f : 5f, bone == "head" ? Colors.White : new Color(.9f, .98f, 1f));
        }
    }

    // ---- sounds -------------------------------------------------------------------------------------------------------------------

    private AudioStreamPlayer? _player; private HSlider? _position; private Label? _time_label; private Wave? _wave; private bool _seeking; private double _length; private bool _loopSound;
    public double SoundLengthForTesting => _length;
    public bool SoundPlayingForTesting => _player?.Playing == true;
    public AudioStream? StreamForTesting => _player?.Stream;
    public int WavePeaksForTesting => _wave?.PeakCount ?? 0;

    private void ShowAudio(byte[] bytes, string mime)
    {
        AudioStream? stream = null; WavData? wav = null;
        if (bytes.Length > 4 && bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S') stream = AudioStreamOggVorbis.LoadFromBuffer(bytes);
        else if (bytes.Length > 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F')
        {
            wav = WavReader.Read(bytes);
            if (wav is not null) { var (data, stereo) = WavReader.ToPcm16(wav); stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = wav.SampleRate, Stereo = stereo, Data = data }; }
            else { _problem.Text = "Esse WAV usa um formato que o jogo não toca (só PCM de 8, 16, 24 ou 32 bits)."; return; }
        }
        else if (mime.Contains("mpeg", StringComparison.OrdinalIgnoreCase)) stream = new AudioStreamMP3 { Data = bytes };
        if (stream is null) { _problem.Text = "Não foi possível ler o áudio."; return; }
        _length = stream.GetLength();
        if (_length > 0) _info.Text += $"\nDuração {FormatTime(_length)}" + (wav is not null ? $" · {wav.SampleRate:N0} Hz · {(wav.Channels == 1 ? "mono" : wav.Channels == 2 ? "estéreo" : $"{wav.Channels} canais")} · {wav.BitsPerSample} bits".Replace(",", ".") : string.Empty);
        _player = new AudioStreamPlayer { Stream = stream, VolumeDb = -6f }; _stage.AddChild(_player);
        if (wav is not null) { _wave = new Wave(WavReader.Peaks(wav, 480)) { CustomMinimumSize = new Vector2(0, 150), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _stage.AddChild(_wave); }
        else { var note = new Label { Text = "Arquivo OGG: a forma de onda só aparece para arquivos WAV.", HorizontalAlignment = HorizontalAlignment.Center }; note.AddThemeColorOverride("font_color", UiStyle.TextMuted); _stage.AddChild(note); }
        _position = new HSlider { MinValue = 0, MaxValue = Math.Max(_length, .01), Step = .01, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _stage.AddChild(_position);
        _position.DragStarted += () => _seeking = true; _position.DragEnded += _ => { _seeking = false; _player!.Seek((float)_position.Value); };
        _time_label = new Label { HorizontalAlignment = HorizontalAlignment.Center }; _stage.AddChild(_time_label);
        var controls = Row(); _stage.AddChild(controls);
        var play = Tool("Tocar", "Toca ou pausa", () => { }); play.Pressed += () => { if (_player!.Playing) { _player.StreamPaused = !_player.StreamPaused; } else { _player.Play(); _player.StreamPaused = false; } play.Text = _player.Playing && !_player.StreamPaused ? "Pausar" : "Tocar"; }; controls.AddChild(play);
        controls.AddChild(Tool("Parar", "Para e volta ao começo", () => { _player!.Stop(); _position!.SetValueNoSignal(0); play.Text = "Tocar"; }));
        var loop = new CheckBox { Text = "Repetir", FocusMode = Control.FocusModeEnum.None }; loop.Toggled += on => _loopSound = on; controls.AddChild(loop);
        controls.AddChild(new Label { Text = "Volume" }); var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = .01, Value = .5, CustomMinimumSize = new Vector2(120, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; volume.ValueChanged += v => _player!.VolumeDb = v <= .001 ? -80f : Mathf.LinearToDb((float)v); controls.AddChild(volume);
        _player.Finished += () => { if (_loopSound && IsInstanceValid(_player)) _player.Play(); else { play.Text = "Tocar"; } };
        _tick = _ =>
        {
            if (!IsInstanceValid(_player)) return false;
            var at = _player.Playing ? _player.GetPlaybackPosition() : 0; if (!_seeking && _position is not null) _position.SetValueNoSignal(at);
            _time_label!.Text = $"{FormatTime(_seeking && _position is not null ? _position.Value : at)} / {FormatTime(_length)}"; _wave?.Mark(_length > 0 ? at / _length : 0);
            return true;
        };
        _time_label.Text = $"{FormatTime(0)} / {FormatTime(_length)}";
    }

    private static string FormatTime(double seconds) { var s = Math.Max(0, seconds); return $"{(int)(s / 60)}:{s % 60:00.0}".Replace(",", "."); }

    public void PlaySoundForTesting() { _player?.Play(); }
    public void StopSoundForTesting() { _player?.Stop(); }

    /// <summary>The outline of a sound with a line where it is playing.</summary>
    private sealed partial class Wave : Control
    {
        private readonly float[] _peaks; private double _mark;
        public int PeakCount => _peaks.Length;
        public Wave(float[] peaks) { _peaks = peaks; MouseFilter = MouseFilterEnum.Ignore; }
        public void Mark(double fraction) { if (Math.Abs(fraction - _mark) > .0005) { _mark = fraction; QueueRedraw(); } }
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(.03f, .08f, .11f, .85f));
            var middle = Size.Y / 2f; var width = Size.X / _peaks.Length;
            for (var i = 0; i < _peaks.Length; i++) { var h = Mathf.Max(1f, _peaks[i] * (middle - 4f)); DrawRect(new Rect2(i * width, middle - h, Mathf.Max(1f, width - .5f), h * 2f), i / (float)_peaks.Length <= _mark ? UiStyle.Accent : new Color(UiStyle.Accent, .45f)); }
            DrawLine(new Vector2((float)_mark * Size.X, 0), new Vector2((float)_mark * Size.X, Size.Y), Colors.White, 2f);
        }
    }
}

public partial class AssetPreview
{
    /// <summary>Tests: answers the request for the contents of a construction without a network: item id -> (status, body).</summary>
    public static Func<Guid, (int Status, string Body)>? ContentsProvider { get; set; }
    private ItemList? _partsList;
    public int PartsForTesting => _partsList?.ItemCount ?? 0;
    public string PartTextForTesting(int row) => _partsList is not null && row < _partsList.ItemCount ? _partsList.GetItemText(row) : string.Empty;

    /// <summary>Opens a saved construction: what its parts are, how they hang together, and the things it is made of with which of them you have.</summary>
    public async Task<PreviewKind> OpenPackageAsync(Guid itemId, string name, int version = 1)
    {
        var token = ++_token; Clear(); _kind = PreviewKind.Package; _partsList = null; _title.Text = name; _info.Text = $"construção · v{version}"; _window.Visible = true; _window.MoveToFront(); _problem.Text = "Carregando…";
        (int Status, string Body) answer;
        if (ContentsProvider is not null) answer = ContentsProvider(itemId);
        else
        {
            var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
            if (session is null || app is null || !session.IsAuthenticated) { _problem.Text = "Entre na sua conta para ver o conteúdo."; return PreviewKind.Package; }
            var http = new HttpRequest { Timeout = 20 }; AddChild(http);
            try
            {
                if (http.Request($"{app.ApiBaseUrl}/api/v1/inventory/items/{itemId}/contents", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { _problem.Text = "Não foi possível pedir o conteúdo."; return PreviewKind.Package; }
                var reply = await ToSignal(http, HttpRequest.SignalName.RequestCompleted); answer = ((long)reply[0] == (long)HttpRequest.Result.Success ? (int)(long)reply[1] : 0, ((byte[])reply[3]).GetStringFromUtf8());
            }
            finally { http.QueueFree(); }
        }
        if (token != _token) return PreviewKind.Package;
        if (answer.Status is < 200 or >= 300) { _problem.Text = answer.Status == 404 ? "Essa construção não está mais no seu inventário." : answer.Status == 400 ? "Esse item não é uma construção." : "Não foi possível ler o conteúdo."; return PreviewKind.Package; }
        _problem.Text = string.Empty; ShowPackage(answer.Body); return PreviewKind.Package;
    }

    private void ShowPackage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            int N(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : 0;
            _info.Text = $"construção · {N(root, "objects")} peça(s) · {N(root, "linksets")} conjunto(s) · {N(root, "withScripts")} com scripts · {N(root, "withAnimations")} com animações";
            var parts = root.GetProperty("parts").EnumerateArray().ToList(); var depth = new int[parts.Count];
            for (var i = 0; i < parts.Count; i++) { var hops = 0; var at = parts[i].TryGetProperty("parent", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : -1; while (at >= 0 && at < parts.Count && hops++ < 16) at = parts[at].TryGetProperty("parent", out var up) && up.ValueKind == JsonValueKind.Number ? up.GetInt32() : -1; depth[i] = hops; }
            _partsList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 170) }; _partsList.AddThemeFontSizeOverride("font_size", 15); _stage.AddChild(_partsList);
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i]; string S(string n) => part.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                var marks = new List<string>(); if (N(part, "scripts") > 0) marks.Add($"{N(part, "scripts")} script(s)"); if (N(part, "animations") > 0) marks.Add($"{N(part, "animations")} animação(ões)"); if (N(part, "otherContent") > 0) marks.Add($"{N(part, "otherContent")} item(ns) dentro"); if (part.TryGetProperty("hasPivot", out var pivot) && pivot.ValueKind == JsonValueKind.True) marks.Add("pivô próprio");
                _partsList.AddItem($"{new string(' ', depth[i] * 3)}{(depth[i] > 0 ? "└ " : string.Empty)}{S("name")} ({S("shape")}){(marks.Count > 0 ? " · " + string.Join(", ", marks) : string.Empty)}");
            }
            var assets = root.GetProperty("assets").EnumerateArray().ToList();
            if (assets.Count > 0)
            {
                var missing = N(root, "missing"); var label = new Label { Text = missing == 0 ? "Você tem tudo de que ela é feita:" : $"Faltam {missing} peça(s) que você ainda não tem (a construção pode não funcionar completa):", AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", 15); label.AddThemeColorOverride("font_color", missing == 0 ? UiStyle.SuccessBright : UiStyle.Orange); _stage.AddChild(label);
                var list = new ItemList { CustomMinimumSize = new Vector2(0, 110), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; list.AddThemeFontSizeOverride("font_size", 15); _stage.AddChild(list);
                foreach (var asset in assets) { string S(string n) => asset.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty; list.AddItem($"{(asset.TryGetProperty("held", out var held) && held.ValueKind == JsonValueKind.True ? "✓" : "✗")} {S("name")} · {S("role") switch { "model" => "modelo", "texture" => "textura", _ => "conteúdo" }}"); }
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _problem.Text = "O conteúdo recebido não pôde ser lido."; }
    }
}
