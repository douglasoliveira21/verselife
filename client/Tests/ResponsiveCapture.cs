using Godot;

namespace LifeVerse.Client.Tests;

/// <summary>Manual visual QA harness. Set LIFEVERSE_CAPTURE_PATH before running.</summary>
public partial class ResponsiveCapture : Node
{
    public override async void _Ready()
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(int.TryParse(OS.GetEnvironment("CAPTURE_W"), out var cw) ? cw : 800, int.TryParse(OS.GetEnvironment("CAPTURE_H"), out var ch) ? ch : 600),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        AddChild(viewport);
        var creator = GD.Load<PackedScene>("res://Scenes/CharacterCreator.tscn").Instantiate<Control>();
        viewport.AddChild(creator);
        for (var i = 0; i < 40; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (int.TryParse(OS.GetEnvironment("CAPTURE_TAB"), out var tab) && creator.FindChild("Tabs", true, false) is LifeVerse.Client.Scripts.UiTabs tabs) { tabs.Select(tab); for (var i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(path)) path = "user://lifeverse-800x600.png";
        var error = viewport.GetTexture().GetImage().SavePng(path);
        GD.Print($"event=responsive_capture size=800x600 result={error} path={path}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
