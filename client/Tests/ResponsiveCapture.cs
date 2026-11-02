using Godot;

namespace LifeVerse.Client.Tests;

/// <summary>Manual visual QA harness. Set LIFEVERSE_CAPTURE_PATH before running.</summary>
public partial class ResponsiveCapture : Node
{
    public override async void _Ready()
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(800, 600),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        AddChild(viewport);
        var creator = GD.Load<PackedScene>("res://Scenes/CharacterCreator.tscn").Instantiate<Control>();
        viewport.AddChild(creator);
        for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(path)) path = "user://lifeverse-800x600.png";
        var error = viewport.GetTexture().GetImage().SavePng(path);
        GD.Print($"event=responsive_capture size=800x600 result={error} path={path}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
