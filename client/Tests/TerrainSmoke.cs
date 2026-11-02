using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Tests;

/// <summary>Runs the world with an edited heightmap. Set LIFEVERSE_CAPTURE_PATH (windowed run) to save screenshots.</summary>
public partial class TerrainSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>();
            AddChild(world);
            var terrain = world.GetNode<IslandTerrain>("IslandTerrain");
            var player = world.GetNode<PlayerController>("Player");
            await Frames(30);
            Require(Mathf.Abs(terrain.Map.SampleHeight(0, 0)) < .001f, "new terrain is not flat");
            Require(player.IsOnFloor() && Mathf.Abs(player.GlobalPosition.Y) < .1f, $"player not standing on flat terrain: {player.GlobalPosition}");
            await Capture("flat");
            // Open the main windows the way a player would, to inspect the interface.
            PressButton(world, "Mover"); PressButton(world, "Gestos");
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control joinWindow) joinWindow.Visible = false;
            await Frames(10);
            await Capture("ui");
            PressButton(world, "Inventário"); PressButton(world, "Construir"); PressButton(world, "Pessoas");
            await Frames(10);
            await Capture("ui2");
            PressButton(world, "Inventário"); PressButton(world, "Pessoas");
            PressButton(world, "Mover"); PressButton(world, "Gestos");

            // Raising the ground under a standing avatar must lift it, not bury it.
            player.GlobalPosition = new Vector3(0, 0, 8);
            player.Velocity = Vector3.Zero;
            await Frames(30);
            var burialRevision = terrain.Map.Revision;
            for (var i = 0; i < 10; i++) { burialRevision++; terrain.ApplyEdit(TerrainTool.Raise, 0, 8, 6, .4f, 0, burialRevision); }
            await Frames(60);
            var raised = terrain.Map.SampleHeight(0, 8);
            Require(raised > 3f, $"raise did not take effect: {raised}");
            Require(player.GlobalPosition.Y > raised - .3f, $"avatar buried in raised terrain: y={player.GlobalPosition.Y} ground={raised}");
            Require(player.IsOnFloor(), "avatar is not standing on the raised terrain");
            // Walk-in shore, then open water: the avatar must float at the surface instead of sinking or standing.
            player.GlobalPosition = new Vector3(0, 0, -96);
            player.Velocity = Vector3.Zero;
            await Frames(40);
            await Capture("shore");
            player.GlobalPosition = new Vector3(0, 0, -122);
            player.Velocity = Vector3.Zero;
            await Frames(150);
            var floatY = TerrainMap.SeaLevel - LifeVerse.Shared.Networking.AuthoritativeMovement.SwimFloatDepth;
            Require(!player.IsOnFloor() && Mathf.Abs(player.GlobalPosition.Y - floatY) < .6f, $"player is not floating at the surface: y={player.GlobalPosition.Y} expected~{floatY}");
            GD.Print($"event=ocean_debug camera_y={GetViewport().GetCamera3D().GlobalPosition.Y:F2} underwater={world.GetNode<OceanSurface>("OceanSurface").IsUnderwater}");
            await Capture("ocean");
            Input.ActionPress("descend"); await Frames(120); Input.ActionRelease("descend");
            Require(player.GlobalPosition.Y < floatY - 2f, $"player did not dive: y={player.GlobalPosition.Y}");
            Input.ActionPress("jump"); await Frames(180); Input.ActionRelease("jump");
            Require(player.GlobalPosition.Y > floatY - .3f && player.GlobalPosition.Y < floatY + .6f, $"player did not surface: y={player.GlobalPosition.Y}");
            player.GlobalPosition = new Vector3(0, 1, 0);
            player.Velocity = Vector3.Zero;
            await Frames(40);

            // A hill and a painted dirt patch, applied the way server-accepted edits are replayed.
            var revision = terrain.Map.Revision;
            for (var i = 0; i < 12; i++) { revision++; terrain.ApplyEdit(TerrainTool.Raise, 14, -6, 9, .4f, 0, revision); }
            for (var i = 0; i < 6; i++) { revision++; terrain.ApplyEdit(TerrainTool.Paint, 14, -6, 12, 1f, 1, revision); }
            for (var i = 0; i < 6; i++) { revision++; terrain.ApplyEdit(TerrainTool.Paint, 22, 4, 6, 1f, 3, revision); }
            for (var i = 0; i < 6; i++) { revision++; terrain.ApplyEdit(TerrainTool.Paint, -14, 10, 7, 1f, 2, revision); }
            await Frames(5);
            var hill = terrain.Map.SampleHeight(14, -6);
            Require(hill > 3f, $"hill too low: {hill}");

            player.GlobalPosition = new Vector3(14, hill + 3f, -6);
            player.Velocity = Vector3.Zero;
            await Frames(90);
            Require(player.IsOnFloor(), "player did not land on the raised terrain");
            Require(Mathf.Abs(player.GlobalPosition.Y - hill) < .35f, $"player not resting on the hill: y={player.GlobalPosition.Y} hill={hill}");
            player.GlobalPosition = new Vector3(0, 1, 6);
            player.Velocity = Vector3.Zero;
            await Frames(30);
            await Capture("edited");

            GD.Print($"event=terrain_smoke result=passed hill={hill:F2}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=terrain_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void PressButton(Node root, string text)
    {
        foreach (var node in root.FindChildren("*", "Button", true, false))
            if (node is Button button && button.Text == text) { button.EmitSignal(BaseButton.SignalName.Pressed); return; }
        throw new InvalidOperationException($"button not found: {text}");
    }

    private async Task Capture(string name)
    {
        var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        for (var i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var error = GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png");
        GD.Print($"event=terrain_capture name={name} result={error}");
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
