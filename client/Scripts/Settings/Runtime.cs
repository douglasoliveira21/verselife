using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>Monitors, resolutions and window modes the device really offers, and how to switch to them.</summary>
public static class DisplayOptions
{
    private static readonly (int W, int H)[] Standard = [(1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1080), (2560, 1440), (3440, 1440), (3840, 2160)];
    public static readonly IReadOnlyList<SettingChoice> Modes = [new("window", "Janela"), new("fullscreen", "Tela cheia"), new("exclusive", "Tela cheia exclusiva")];

    public static string AspectOf(int width, int height)
    {
        var ratio = width / (double)height;
        foreach (var (name, value) in new[] { ("16:9", 16 / 9.0), ("16:10", 1.6), ("21:9", 2.38), ("4:3", 4 / 3.0), ("32:9", 32 / 9.0), ("3:2", 1.5) })
            if (Math.Abs(ratio - value) < .02) return name;
        return "";
    }
    private static int Gcd(int a, int b) => b == 0 ? Math.Max(1, a) : Gcd(b, a % b);

    public static int CurrentScreen => Math.Clamp(DisplayServer.WindowGetCurrentScreen(), 0, Math.Max(0, DisplayServer.GetScreenCount() - 1));

    public static IReadOnlyList<SettingChoice> Monitors()
    {
        var list = new List<SettingChoice>();
        for (var i = 0; i < Math.Max(1, DisplayServer.GetScreenCount()); i++) { var size = DisplayServer.ScreenGetSize(i); list.Add(new(i.ToString(), $"Monitor {i + 1} ({size.X} × {size.Y})")); }
        return list;
    }

    public static IReadOnlyList<SettingChoice> Resolutions(int screen)
    {
        var limit = DisplayServer.ScreenGetSize(Math.Clamp(screen, 0, Math.Max(0, DisplayServer.GetScreenCount() - 1)));
        var sizes = Standard.Where(s => s.W <= limit.X && s.H <= limit.Y).ToList();
        if (limit.X > 0 && limit.Y > 0 && !sizes.Contains((limit.X, limit.Y))) sizes.Add((limit.X, limit.Y));
        var current = DisplayServer.WindowGetSize(); if (!sizes.Contains((current.X, current.Y)) && current.X <= limit.X && current.Y <= limit.Y) sizes.Add((current.X, current.Y));
        return [.. sizes.OrderBy(s => s.W * s.H).Select(s => new SettingChoice($"{s.W}x{s.H}", AspectOf(s.W, s.H) is { Length: > 0 } aspect ? $"{s.W} × {s.H} ({aspect})" : $"{s.W} × {s.H}"))];
    }

    public static int RefreshRate(int screen) => (int)Math.Round(DisplayServer.ScreenGetRefreshRate(Math.Clamp(screen, 0, Math.Max(0, DisplayServer.GetScreenCount() - 1))));

    public static string CurrentMode() => DisplayServer.WindowGetMode() switch { DisplayServer.WindowMode.ExclusiveFullscreen => "exclusive", DisplayServer.WindowMode.Fullscreen => "fullscreen", _ => "window" };
    public static string CurrentResolution() { var s = DisplayServer.WindowGetSize(); return $"{s.X}x{s.Y}"; }

    /// <summary>The id to show in a dropdown for a stored value: "auto" means what the device is doing now.</summary>
    public static string Shown(string setting, string stored) => stored != "auto" ? stored : setting switch { "display.monitor" => CurrentScreen.ToString(), "display.resolution" => CurrentResolution(), _ => CurrentMode() };

    /// <summary>The height of the title bar and the width of the side borders the system draws around the game window (a sensible guess while the system has not said).</summary>
    public static (int Title, int Border) Frame()
    {
        var around = DisplayServer.WindowGetSizeWithDecorations() - DisplayServer.WindowGetSize(); if (around.X <= 0 || around.Y <= 0) return (31, 8);
        var border = around.X / 2; return (Math.Max(0, around.Y - border), border);
    }

    /// <summary>Puts a windowed game on the screen with its title bar showing (minimize, maximize and close), however large it is: the size is cut to the room the desktop leaves, and the bar is never above the top of the screen.</summary>
    public static void PlaceWindowed(int screen, int wantedWidth, int wantedHeight)
    {
        var usable = DisplayServer.ScreenGetUsableRect(screen); var (title, border) = Frame();
        var w = Math.Clamp(wantedWidth, 640, Math.Max(640, usable.Size.X - border * 2)); var h = Math.Clamp(wantedHeight, 360, Math.Max(360, usable.Size.Y - title - border));
        DisplayServer.WindowSetSize(new Vector2I(w, h));
        DisplayServer.WindowSetPosition(new Vector2I(usable.Position.X + (usable.Size.X - w) / 2, usable.Position.Y + title + Math.Max(0, (usable.Size.Y - title - border - h) / 2)));
    }

    /// <summary>At the start (and after a change of mode): a windowed game whose title bar ended up off the screen is moved down and, if needed, made smaller.</summary>
    public static void KeepTitleBarVisible()
    {
        if (DisplayServer.GetName() == "headless" || DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
        var screen = DisplayServer.WindowGetCurrentScreen(); var usable = DisplayServer.ScreenGetUsableRect(screen); var (title, border) = Frame(); var size = DisplayServer.WindowGetSize(); var position = DisplayServer.WindowGetPosition();
        var tooBig = size.X > usable.Size.X - border * 2 || size.Y > usable.Size.Y - title - border; var barHidden = position.Y - title < usable.Position.Y;
        if (tooBig || barHidden) PlaceWindowed(screen, size.X, size.Y);
        GD.Print($"event=window_checked usable={usable} title={title} border={border} size={size} before={position} after={DisplayServer.WindowGetPosition()}/{DisplayServer.WindowGetSize()} moved={tooBig || barHidden}");
    }

    /// <summary>Puts the window where the settings say. "auto" leaves that part as it is. Returns false when nothing was asked for.</summary>
    public static bool Apply(string monitor, string resolution, string mode)
    {
        if (monitor == "auto" && resolution == "auto" && mode == "auto") return false;
        var screen = monitor != "auto" && int.TryParse(monitor, out var m) && m >= 0 && m < DisplayServer.GetScreenCount() ? m : CurrentScreen;
        var wantedMode = mode == "auto" ? CurrentMode() : mode;
        if (monitor != "auto" && screen != DisplayServer.WindowGetCurrentScreen()) { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); DisplayServer.WindowSetCurrentScreen(screen); }
        switch (wantedMode)
        {
            case "fullscreen": DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen); break;
            case "exclusive": DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen); break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                var parts = resolution == "auto" ? [] : resolution.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                {
                    PlaceWindowed(screen, w, h);
                }
                else KeepTitleBarVisible();
                break;
        }
        return true;
    }
}

/// <summary>The 3D resolution scale (a lower one is upscaled with FSR) and the controller that lowers it while the frame rate is under the goal.</summary>
public static class RenderScale
{
    public const float Min = .5f, Max = 1.5f, DynamicFloor = .6f, Step = .05f;
    public static bool Dynamic { get; set; }
    public static float Base { get; private set; } = 1f;
    public static float Current { get; private set; } = 1f;
    private static double _timer;

    public static void SetBase(float scale, Viewport viewport) { Base = Math.Clamp(scale, Min, Max); Current = Base; Apply(viewport, Current); }

    public static void Apply(Viewport viewport, float scale)
    {
        scale = Math.Clamp(scale, Min, Max);
        viewport.Scaling3DMode = scale < .999f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = scale;
    }

    /// <summary>The scale after looking at the frame rate: down a step when it is well under the goal, up a step when it is back at it, never outside the limits.</summary>
    public static float Next(float current, float fps, float target, float min, float max)
    {
        if (fps < target * .9f) return Math.Max(min, current - Step);
        if (fps > target * .98f) return Math.Min(max, current + Step);
        return current;
    }

    /// <summary>The frame rate the game tries to hold: its own limit, or the monitor's rate when that is lower.</summary>
    public static float TargetFps()
    {
        var limit = Engine.MaxFps > 0 ? Engine.MaxFps : 1000f;
        var refresh = DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled ? DisplayOptions.RefreshRate(DisplayOptions.CurrentScreen) : 0;
        return refresh > 0 ? Math.Min(limit, refresh) : Math.Min(limit, 144f);
    }

    public static void Tick(double delta, Viewport viewport)
    {
        if (!Dynamic) return;
        _timer += delta; if (_timer < 1.0) return; _timer = 0;
        var next = Next(Current, (float)Engine.GetFramesPerSecond(), TargetFps(), Math.Min(DynamicFloor, Base), Base);
        if (!Mathf.IsEqualApprox(next, Current)) { Current = next; Apply(viewport, Current); }
    }
}

/// <summary>The numbers drawn over the game when the person asks for them (Indicadores na tela). Only readings the engine really gives; the CPU and GPU load in percent is not one of them.</summary>
public partial class ScreenMetricsOverlay : CanvasLayer
{
    private Label _label = null!;
    private double _timer;
    private readonly SettingsStore _store;
    private bool _measuring;
    public string TextForTesting => _label.Text;

    public ScreenMetricsOverlay(SettingsStore store) { _store = store; Layer = 30; Name = "ScreenMetrics"; }

    public override void _Ready()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(14, 92) };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(0, 0, 0, .55f), new Color(0, 0, 0, 0), 8, 0, 8, 5));
        _label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore }; _label.AddThemeFontSizeOverride("font_size", 15); _label.AddThemeColorOverride("font_color", UiStyle.Text);
        panel.AddChild(_label); AddChild(panel); Visible = false; _panel = panel;
        _store.ValueApplied += id => { if (id.StartsWith("metrics.", StringComparison.Ordinal)) Refresh(); };
        Refresh();
    }

    private PanelContainer _panel = null!;

    public override void _Process(double delta) { _timer += delta; if (_timer < .25) return; _timer = 0; Refresh(); }

    private void Refresh()
    {
        bool fps = _store.Get<bool>("metrics.fps"), memory = _store.Get<bool>("metrics.memory"), frame = _store.Get<bool>("metrics.frame_time");
        var want = fps || memory || frame; _panel.Visible = want; Visible = want;
        var root = GetViewport().GetViewportRid();
        if (frame != _measuring) { RenderStats.Request("overlay", frame); _measuring = frame; }
        if (!want) return;
        var lines = new List<string>();
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        if (fps) lines.Add($"{Engine.GetFramesPerSecond():0} FPS");
        if (frame) lines.Add(string.Format(pt, "Quadro: CPU {0:0.0} ms · GPU {1:0.0} ms", RenderingServer.ViewportGetMeasuredRenderTimeCpu(root) + RenderingServer.GetFrameSetupTimeCpu(), RenderingServer.ViewportGetMeasuredRenderTimeGpu(root)));
        if (memory) lines.Add(string.Format(pt, "RAM {0:0} MB · VRAM {1:0} MB", OS.GetStaticMemoryUsage() / 1048576.0, RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0));
        _label.Text = string.Join("\n", lines);
    }
}

/// <summary>Hides the objects farther than the chosen distance (Configurações › Mundo e avatares). Only what it hid itself is shown again; the collision of an object does not depend on being drawn.</summary>
public static class ObjectCuller
{
    /// <summary>Meters; 0 or less means no limit.</summary>
    public static float Range { get; set; }
    private static readonly HashSet<Node3D> Hidden = [];
    private static double _timer;
    public static int HiddenCount => Hidden.Count;

    public static void Tick(double delta, SceneTree tree)
    {
        _timer += delta; if (_timer < .5) return; _timer = 0;
        Update(tree.Root.GetCamera3D()?.GlobalPosition, tree.GetNodesInGroup("lifeverse_build_objects"));
    }

    /// <summary>One pass over the objects (the group of every placed object): far ones are hidden, near ones come back with a margin so none flickers at the edge.</summary>
    public static void Update(Vector3? eye, IEnumerable<Node> objects)
    {
        Hidden.RemoveWhere(n => !GodotObject.IsInstanceValid(n));
        if (eye is null) return;
        foreach (var node in objects)
        {
            if (node is not Node3D view || !GodotObject.IsInstanceValid(view)) continue;
            var distance = view.GlobalPosition.DistanceTo(eye.Value);
            if (Range <= 0f || distance <= Range * .95f) { if (Hidden.Remove(view)) view.Visible = true; }
            else if (distance > Range && view.Visible) { view.Visible = false; Hidden.Add(view); }
        }
        if (Range <= 0f) foreach (var view in Hidden.ToList()) { view.Visible = true; Hidden.Remove(view); }
    }
}

/// <summary>Lowers the quality by itself when the frame rate stays far under the goal for a while (Configurações › Otimização). Off by default; never touches a Personalizada preset.</summary>
public static class AutoQuality
{
    public const double WindowSeconds = 5, CooldownSeconds = 60;
    public const int BadWindowsNeeded = 3;
    public static bool Enabled { get; set; }
    public static event Action<string>? Lowered;
    private static double _timer, _cooldown; private static int _bad; private static double _sum; private static int _samples;

    /// <summary>The level below this one (never under Muito baixa), or null when there is none.</summary>
    public static GraphicsQuality.Level? Below(GraphicsQuality.Level level) => level switch
    {
        GraphicsQuality.Level.Ultra => GraphicsQuality.Level.High, GraphicsQuality.Level.High => GraphicsQuality.Level.Medium, GraphicsQuality.Level.Medium => GraphicsQuality.Level.Low,
        GraphicsQuality.Level.Low => GraphicsQuality.Level.VeryLow, _ => null
    };

    /// <summary>True when the average frame rate of a window is bad enough to count against the preset.</summary>
    public static bool IsBad(double averageFps, double target) => target > 0 && averageFps < target * .7;

    public static void Tick(double delta, SettingsStore store)
    {
        if (!Enabled) { _bad = 0; return; }
        _cooldown = Math.Max(0, _cooldown - delta); _timer += delta;
        if (_timer >= 1) { _timer = 0; _sum += Engine.GetFramesPerSecond(); _samples++; }
        if (_samples < WindowSeconds) return;
        var average = _sum / _samples; _sum = 0; _samples = 0;
        _bad = IsBad(average, RenderScale.TargetFps()) ? _bad + 1 : 0;
        if (_bad < BadWindowsNeeded || _cooldown > 0) return;
        _bad = 0;
        if ((string)store.Value("graphics.preset") == "custom") return;
        var current = GraphicsQuality.Resolve(GraphicsQuality.Parse((string)store.Value("graphics.preset")));
        if (Below(current) is not { } lower) return;
        _cooldown = CooldownSeconds;
        store.SetAndApply("graphics.preset", GraphicsQuality.Keys[(int)lower]);
        SettingsAppliers.ApplyQuality(store);
        Lowered?.Invoke($"O jogo estava abaixo da meta de quadros: a qualidade gráfica passou para {GraphicsQuality.Labels[(int)lower]}.");
    }
}

/// <summary>The numbers of the Estatísticas tab: only what the engine reports. Nothing is estimated.</summary>
public static class RenderStats
{
    public sealed record Snapshot(double Fps, double AverageFps, double FrameMs, double ProcessMs, double PhysicsMs, double CpuRenderMs, double GpuRenderMs, double RamMb, double VramMb, double TextureMemoryMb, double BufferMemoryMb,
        long Objects, long Triangles, long DrawCalls, long Nodes, long Resources, long OrphanNodes, int AvatarsDrawn, int AvatarsTotal, string Adapter, string Renderer);

    private static readonly HashSet<string> Measuring = [];
    /// <summary>Who needs the GPU render time measured (it costs a little, so it only runs while somebody looks at it).</summary>
    public static void Request(string owner, bool wanted)
    {
        var before = Measuring.Count > 0; if (wanted) Measuring.Add(owner); else Measuring.Remove(owner);
        if ((Measuring.Count > 0) != before && Engine.GetMainLoop() is SceneTree tree) RenderingServer.ViewportSetMeasureRenderTime(tree.Root.GetViewportRid(), Measuring.Count > 0);
    }

    private static readonly Queue<double> Recent = new();
    private static double _timer;

    public static void Track(double delta)
    {
        _timer += delta; if (_timer < .25) return; _timer = 0;
        Recent.Enqueue(Engine.GetFramesPerSecond()); while (Recent.Count > 20) Recent.Dequeue();
    }

    public static double Average() => Recent.Count == 0 ? Engine.GetFramesPerSecond() : Recent.Average();

    public static Snapshot Read(Viewport viewport, NetworkManager? network)
    {
        var rid = viewport.GetViewportRid(); const double mb = 1048576.0;
        var counts = network?.DetailCounts; var total = (counts?.Values.Sum() ?? 0) + 1;      // the others in the island and you
        var drawn = (counts?.Where(p => p.Key != AvatarDetail.Hidden).Sum(p => p.Value) ?? 0) + 1;
        return new Snapshot(Engine.GetFramesPerSecond(), Average(), 1000.0 / Math.Max(1.0, Engine.GetFramesPerSecond()), Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0, Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0,
            RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid) + RenderingServer.GetFrameSetupTimeCpu(), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
            OS.GetStaticMemoryUsage() / mb, RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / mb, RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / mb, RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) / mb,
            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame), (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame), (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
            (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), (long)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount), (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount),
            drawn, total, RenderingServer.GetVideoAdapterName(), $"{RenderingServer.GetCurrentRenderingMethod()} · {RenderingServer.GetCurrentRenderingDriverName()}");
    }
}
