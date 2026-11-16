using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.Settings;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>
/// The buttons at the top right of the login: music, configurações, and, only in full screen, minimizar and fechar (in a window the system's own title bar does that). Configurações opens the same settings window the game uses.
/// </summary>
public partial class WindowControls : HBoxContainer
{
    public Button Settings { get; private set; } = null!;
    public Button Minimize { get; private set; } = null!;
    public Button Close { get; private set; } = null!;
    public Button Sound { get; private set; } = null!;
    /// <summary>The theme the speaker button mutes.</summary>
    public LoginMusic? Music { get; set; }
    /// <summary>The settings window (made on the first use).</summary>
    public SettingsWindow? SettingsPanel { get; private set; }

    /// <summary>Whether the game fills the screen: only then are minimize and close shown.</summary>
    public static bool IsFullScreen => DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        Settings = Round("settings", "Configurações", OpenSettings); Minimize = Round("minimize", "Minimizar", () => DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized)); Close = Round("close", "Fechar o jogo", () => GetTree().Quit());
        Sound = Round(LoginMusic.Muted ? "mic-off" : "volume", LoginMusic.Muted ? "Ligar a música" : "Silenciar a música", ToggleSound); AddChild(Sound); AddChild(Settings); AddChild(Minimize); AddChild(Close);
        Follow();
    }

    public override void _Process(double delta) => Follow();

    private void Follow() { var full = IsFullScreen; if (Minimize.Visible != full) Minimize.Visible = full; if (Close.Visible != full) Close.Visible = full; }

    private static Button Round(string icon, string tip, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 22), TooltipText = tip, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(46, 46), MouseDefaultCursorShape = CursorShape.PointingHand, IconAlignment = HorizontalAlignment.Center, ExpandIcon = false };
        button.AddThemeStyleboxOverride("normal", UiStyle.Box(new Color(.106f, .149f, .18f, .78f), new Color(UiStyle.BorderSoft, .8f), 23, 1, 0, 0)); button.AddThemeStyleboxOverride("hover", UiStyle.Box(new Color(.14f, .2f, .24f, .92f), UiStyle.Accent, 23, 1, 0, 0));
        button.AddThemeStyleboxOverride("pressed", UiStyle.Box(new Color(.05f, .08f, .1f, .95f), UiStyle.Accent, 23, 1, 0, 0)); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Pressed += action; return button;
    }

    private void ToggleSound()
    {
        var mute = !LoginMusic.Muted; Music?.SetMuted(mute); Sound.Icon = UiStyle.Icon(mute ? "mic-off" : "volume", UiStyle.Text, 22); Sound.TooltipText = mute ? "Ligar a música" : "Silenciar a música";
    }

    /// <summary>The settings window of the game, over the login screen.</summary>
    public void OpenSettings()
    {
        if (SettingsPanel is null || !IsInstanceValid(SettingsPanel))
        {
            var top = (Node)this; while (top is not LoginScreen && top.GetParent() is Control parent) top = parent; if (top is not Control host) return;
            SettingsPanel = new SettingsWindow(SettingsService.Store); host.AddChild(SettingsPanel);
        }
        SettingsPanel.Open();
    }
}
