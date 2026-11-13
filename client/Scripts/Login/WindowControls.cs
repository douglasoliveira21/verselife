using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>The three buttons at the top right: configurações, minimizar and fechar. Settings use the same <see cref="ClientSettings"/> the game uses; the choice is saved by it.</summary>
public partial class WindowControls : HBoxContainer
{
    public static readonly (string Text, Vector2I Size)[] Resolutions = [("1280 × 720", new(1280, 720)), ("1366 × 768", new(1366, 768)), ("1600 × 900", new(1600, 900)), ("1920 × 1080", new(1920, 1080)), ("2560 × 1440", new(2560, 1440)), ("3840 × 2160", new(3840, 2160))];

    public Button Settings { get; private set; } = null!;
    public Button Minimize { get; private set; } = null!;
    public Button Close { get; private set; } = null!;
    public Button Sound { get; private set; } = null!;
    /// <summary>The theme the speaker button mutes.</summary>
    public LoginMusic? Music { get; set; }
    public ConfirmationDialog? Dialog { get; private set; }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        Settings = Round("settings", "Configurações", OpenSettings); Minimize = Round("minimize", "Minimizar", () => DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized)); Close = Round("close", "Fechar o jogo", () => GetTree().Quit());
        Sound = Round(LoginMusic.Muted ? "mic-off" : "volume", LoginMusic.Muted ? "Ligar a música" : "Silenciar a música", ToggleSound); AddChild(Sound); AddChild(Settings); AddChild(Minimize); AddChild(Close);
    }

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

    /// <summary>Window or full screen, resolution and volume.</summary>
    public void OpenSettings()
    {
        if (Dialog is not null && IsInstanceValid(Dialog)) { Dialog.QueueFree(); }
        var settings = GetNodeOrNull<ClientSettings>("/root/ClientSettings"); if (settings is null) return;
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 12);
        var full = new CheckBox { Text = " Tela cheia", ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen, Name = "Fullscreen" }; content.AddChild(full);
        content.AddChild(new Label { Text = "Resolução da janela" }); var size = new OptionButton { Name = "Resolution", CustomMinimumSize = new Vector2(280, 36) }; var current = DisplayServer.WindowGetSize(); var chosen = 0;
        for (var i = 0; i < Resolutions.Length; i++) { size.AddItem(Resolutions[i].Text); if (Resolutions[i].Size == current) chosen = i; } size.Select(chosen); content.AddChild(size);
        content.AddChild(new Label { Text = "Volume geral" }); var volume = new HSlider { Name = "Volume", MinValue = 0, MaxValue = 1, Step = .01, Value = settings.MasterVolume, CustomMinimumSize = new Vector2(280, 28) }; content.AddChild(volume);
        var dialog = new ConfirmationDialog { Title = "Configurações", OkButtonText = "Aplicar", CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() }; dialog.AddChild(content); AddChild(dialog); Dialog = dialog;
        dialog.Confirmed += () => { var pick = Resolutions[Math.Max(0, size.Selected)].Size; settings.Apply(pick.X, pick.Y, full.ButtonPressed, settings.VSync, settings.FpsLimit, settings.MouseSensitivity, settings.UiScale, (float)volume.Value); LoginMusic.ApplyBusVolume((float)volume.Value); dialog.QueueFree(); };
        dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCentered(new Vector2I(380, 280));
    }
}
