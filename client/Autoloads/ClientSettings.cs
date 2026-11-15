using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class ClientSettings : Node
{
    private const string Path = "user://settings.cfg";
    public int FpsLimit { get; private set; } = 120;
    public float MouseSensitivity { get; private set; } = 0.003f;
    public float UiScale { get; private set; } = 1f;
    public float MasterVolume { get; private set; } = 1f;
    public bool VSync { get; private set; } = true;
    public override void _Ready()
    {
        Load();
        Callable.From(Scripts.Settings.SettingsService.Boot).CallDeferred();      // after the other autoloads exist (the avatar budget lives in NetworkManager)
        AddChild(new Scripts.Settings.ScreenMetricsOverlay(Scripts.Settings.SettingsService.Store));
    }
    public override void _Process(double delta)
    {
        if (GetTree() is not { } tree) return;
        Scripts.Settings.RenderScale.Tick(delta, tree.Root); Scripts.Settings.ObjectCuller.Tick(delta, tree); Scripts.Settings.RenderStats.Track(delta); Scripts.Settings.AutoQuality.Tick(delta, Scripts.Settings.SettingsService.Store);
    }

    /// <summary>With the window out of focus the frame rate can be limited (Configurações › Otimização); it goes back to the normal limit on return.</summary>
    public override void _Notification(int what)
    {
        var store = Scripts.Settings.SettingsService.Store;
        if (what == NotificationApplicationFocusOut && store.Get<bool>("audio.mute_background")) AudioServer.SetBusMute(0, true);
        else if (what == NotificationApplicationFocusIn) AudioServer.SetBusMute(0, store.Get<float>("audio.master") <= 0f);
        if (what == NotificationApplicationFocusOut && int.TryParse((string)store.Value("perf.background_fps"), out var background) && background > 0) Engine.MaxFps = background;
        else if (what == NotificationApplicationFocusIn) Engine.MaxFps = Math.Clamp(store.Get<int>("graphics.fps_limit"), 30, 360);
    }
    /// <summary>Graphics quality (Automático, Baixo, Médio, Alto, Ultra): applied at once and kept.</summary>
    public void SetQuality(Scripts.GraphicsQuality.Level level) { Scripts.GraphicsQuality.Choose(level); Save(); }
    public void Apply(int width, int height, bool fullscreen, bool vsync, int fps, float mouse, float uiScale, float volume)
    {
        DisplayServer.WindowSetSize(new Vector2I(Math.Max(640, width), Math.Max(360, height)));
        DisplayServer.WindowSetMode(fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = Math.Clamp(fps, 30, 360); FpsLimit = Engine.MaxFps; MouseSensitivity = Math.Clamp(mouse, .0005f, .02f); UiScale = Math.Clamp(uiScale, .75f, 2f); MasterVolume = Math.Clamp(volume, 0f, 1f); VSync = vsync; Save();
    }
    private void Load() { var cfg = new ConfigFile(); if (cfg.Load(Path) != Error.Ok) return; FpsLimit = (int)cfg.GetValue("graphics", "fps", FpsLimit); MouseSensitivity = (float)cfg.GetValue("controls", "mouse", MouseSensitivity); UiScale = (float)cfg.GetValue("ui", "scale", UiScale); MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume); VSync = (bool)cfg.GetValue("graphics", "vsync", VSync); Engine.MaxFps = FpsLimit; Scripts.GraphicsQuality.Chosen = Scripts.GraphicsQuality.Parse((string)cfg.GetValue("graphics", "quality", "auto")); }
    private void Save() { var cfg = new ConfigFile(); cfg.SetValue("graphics", "fps", FpsLimit); Scripts.Settings.SettingsService.Mirror("graphics.vsync", VSync); Scripts.Settings.SettingsService.Mirror("graphics.fps_limit", FpsLimit); Scripts.Settings.SettingsService.Mirror("graphics.preset", Scripts.GraphicsQuality.Keys[(int)Scripts.GraphicsQuality.Chosen]); cfg.SetValue("graphics", "vsync", VSync); cfg.SetValue("graphics", "quality", Scripts.GraphicsQuality.Keys[(int)Scripts.GraphicsQuality.Chosen]); cfg.SetValue("controls", "mouse", MouseSensitivity); cfg.SetValue("ui", "scale", UiScale); cfg.SetValue("audio", "master", MasterVolume); cfg.Save(Path); }
}
