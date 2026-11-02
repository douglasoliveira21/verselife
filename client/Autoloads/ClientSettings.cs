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
    public override void _Ready() => Load();
    public void Apply(int width, int height, bool fullscreen, bool vsync, int fps, float mouse, float uiScale, float volume)
    {
        DisplayServer.WindowSetSize(new Vector2I(Math.Max(640, width), Math.Max(360, height)));
        DisplayServer.WindowSetMode(fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = Math.Clamp(fps, 30, 360); FpsLimit = Engine.MaxFps; MouseSensitivity = Math.Clamp(mouse, .0005f, .02f); UiScale = Math.Clamp(uiScale, .75f, 2f); MasterVolume = Math.Clamp(volume, 0f, 1f); VSync = vsync; Save();
    }
    private void Load() { var cfg = new ConfigFile(); if (cfg.Load(Path) != Error.Ok) return; FpsLimit = (int)cfg.GetValue("graphics", "fps", FpsLimit); MouseSensitivity = (float)cfg.GetValue("controls", "mouse", MouseSensitivity); UiScale = (float)cfg.GetValue("ui", "scale", UiScale); MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume); VSync = (bool)cfg.GetValue("graphics", "vsync", VSync); Engine.MaxFps = FpsLimit; }
    private void Save() { var cfg = new ConfigFile(); cfg.SetValue("graphics", "fps", FpsLimit); cfg.SetValue("graphics", "vsync", VSync); cfg.SetValue("controls", "mouse", MouseSensitivity); cfg.SetValue("ui", "scale", UiScale); cfg.SetValue("audio", "master", MasterVolume); cfg.Save(Path); }
}
