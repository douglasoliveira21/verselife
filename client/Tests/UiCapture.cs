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
            if (OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") == "import")
            {
                var sample = new Node3D(); sample.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .2f, BottomRadius = .5f, Height = 1.6f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#C98B5A") } });
                var document = new GltfDocument(); var state = new GltfState(); document.AppendFromScene(sample, state);
                world.GetNode<InventoryPanel>("InventoryPanel").ImportDialogForTesting.ShowForTesting("vaso.glb", document.GenerateBuffer(state));
            }
            ui.PreviewForTesting(OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") is { Length: > 0 } view ? view : "people");
            for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng($"{OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH")}-ui.png");
            GD.Print("event=ui_capture result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=ui_capture result=failed error={exception.Message}"); GetTree().Quit(1); }
    }
}
