using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Windowed run that opens the loading screen and the busy login button and saves screenshots to LIFEVERSE_CAPTURE_PATH.</summary>
public partial class LoadingCapture : Node
{
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), Godot.Timer.SignalName.Timeout);
    private void Save(string name) { var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH"); if (!string.IsNullOrWhiteSpace(path)) GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png"); GD.Print("event=loading_capture name=" + name); }

    public override async void _Ready()
    {
        try
        {
            GetNode<Autoloads.SessionManager>("/root/SessionManager").Set("token", "refresh", "capture");
            var entry = new WorldEntryCoordinator { Name = "WorldEntryCoordinator" }; AddChild(entry); await Wait(.3);
            entry.BeginLocating(); await Wait(.5); Save("a"); await Wait(.35); Save("b");
            entry.StateForTesting("requesting_ticket", "x"); await Wait(.6); Save("servidor");
            GD.Print("event=loading_capture result=passed"); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError("event=loading_capture result=failed " + e.Message); GetTree().Quit(1); }
    }
}
