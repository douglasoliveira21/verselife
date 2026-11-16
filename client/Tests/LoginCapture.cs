using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Windowed run that opens the login screen, presses "Entrar" busy and saves screenshots of the animation to LIFEVERSE_CAPTURE_PATH.</summary>
public partial class LoginCapture : Node
{
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), Godot.Timer.SignalName.Timeout);
    private void Save(string name) { var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH"); if (!string.IsNullOrWhiteSpace(path)) GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png"); GD.Print("event=login_capture name=" + name); }

    public override async void _Ready()
    {
        try
        {
            var screen = GD.Load<PackedScene>("res://Scenes/Login.tscn").Instantiate<Control>(); AddChild(screen); await Wait(1.2); Save("parado");
            ((LoginScreen)screen).Controls.OpenSettings(); await Wait(.8); Save("config"); ((LoginScreen)screen).Controls.SettingsPanel!.Visible = false;
            ((LoginScreen)screen).Panel.SetBusy(true); await Wait(.25); Save("a"); await Wait(.17); Save("b"); await Wait(.4); Save("c");
            GD.Print("event=login_capture result=passed"); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError("event=login_capture result=failed " + e.Message); GetTree().Quit(1); }
    }
}
