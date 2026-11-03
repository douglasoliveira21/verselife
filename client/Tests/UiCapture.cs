using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// Not a pass/fail test: opens the world scene with sample windows and saves a picture of the interface, to compare it with the
/// design references in client/Assets/ui. Needs a window and LIFEVERSE_CAPTURE_PATH.
public partial class UiCapture : Node
{
    public override async void _Ready()
    {
        try
        {
            if (OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") == "login")
            {
                AddChild(GD.Load<PackedScene>("res://Scenes/Login.tscn").Instantiate<Control>());
                for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GetViewport().GetTexture().GetImage().SavePng($"{OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH")}-ui.png");
                GetTree().Quit();
                return;
            }
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>();
            AddChild(world);
            for (var i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ui = world.GetNode<ViewerUI>("ViewerUI");
            ui.PreviewForTesting(OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") is { Length: > 0 } view ? view : "people");
            for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng($"{OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH")}-ui.png");
            GD.Print("event=ui_capture result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=ui_capture result=failed error={exception.Message}"); GetTree().Quit(1); }
    }
}
