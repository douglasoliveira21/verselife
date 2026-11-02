using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

public partial class MovementSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>();
            AddChild(world);
            var player = world.GetNode<PlayerController>("Player");
            await Frames(45);

            var walkStart = player.GlobalPosition;
            Input.ActionPress("move_forward");
            await Frames(24);
            Input.ActionRelease("move_forward");
            var walkDistance = HorizontalDistance(walkStart, player.GlobalPosition);
            Require(walkDistance > .5f, $"walk distance too small: {walkDistance}");

            var runStart = player.GlobalPosition;
            Input.ActionPress("move_forward"); Input.ActionPress("run");
            await Frames(24);
            Input.ActionRelease("move_forward"); Input.ActionRelease("run");
            var runDistance = HorizontalDistance(runStart, player.GlobalPosition);
            Require(runDistance > walkDistance * 1.45f, $"run did not exceed walk: walk={walkDistance} run={runDistance}");

            player.GlobalPosition = new Vector3(-5.35f, .8f, -5);
            player.Velocity = Vector3.Zero;
            await Frames(20);
            var slopeStart = player.GlobalPosition;
            Input.ActionPress("move_left"); await Frames(55); Input.ActionRelease("move_left");
            Require(player.GlobalPosition.X < slopeStart.X - .5f && player.GlobalPosition.Y > slopeStart.Y + .15f,
                $"slope ascent failed: from={slopeStart} to={player.GlobalPosition}");

            player.GlobalPosition = new Vector3(0, .05f, 1);
            player.Velocity = Vector3.Zero;
            await Frames(10);
            var groundY = player.GlobalPosition.Y;
            Input.ActionPress("jump"); await Frames(2); Input.ActionRelease("jump");
            var maxY = player.GlobalPosition.Y;
            for (var i = 0; i < 90; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); maxY = Mathf.Max(maxY, player.GlobalPosition.Y); }
            Require(maxY > groundY + .7f, $"jump height too small: {maxY - groundY}");
            Require(player.IsOnFloor(), "player did not land after jump");

            player.GlobalPosition = new Vector3(0, .05f, -4.5f);
            player.Velocity = Vector3.Zero;
            await Frames(10);
            Input.ActionPress("move_forward"); await Frames(90); Input.ActionRelease("move_forward");
            Require(player.GlobalPosition.Z > -7.65f, $"player crossed wall: z={player.GlobalPosition.Z}");

            player._UnhandledInput(Action("fly_toggle"));
            var flyStart = player.GlobalPosition.Y;
            Input.ActionPress("jump"); await Frames(30); Input.ActionRelease("jump");
            Require(player.GlobalPosition.Y > flyStart + 1f, "flight ascent failed");
            Input.ActionPress("descend"); await Frames(90); Input.ActionRelease("descend");
            Require(player.IsOnFloor(), "flight landing failed");

            player.GlobalPosition = new Vector3(12, .5f, 8);
            player.Velocity = Vector3.Zero;
            player.SetSwimming(true);
            var swimStart = player.GlobalPosition.Y;
            Input.ActionPress("jump"); await Frames(20); Input.ActionRelease("jump");
            Require(player.GlobalPosition.Y > swimStart + .5f, "swim ascent failed");
            player.SetSwimming(false);

            var seat = world.GetNode<InteractableSeat>("Seat");
            player.Sit(seat); await Frames(2);
            Require(player.GlobalPosition.DistanceTo(seat.SeatTransform.Origin) < .05f, "seat alignment failed");
            player._UnhandledInput(Action("interact")); await Frames(2);
            Require(player.GlobalPosition.DistanceTo(seat.SeatTransform.Origin) > .5f, "stand-up failed");

            var rig = player.GetNode<AvatarRig>("AvatarRig");
            player._UnhandledInput(Action("first_person"));
            Require(!rig.Visible, "first-person mode did not hide local avatar");
            player._UnhandledInput(Action("first_person"));
            Require(rig.Visible, "third-person mode did not restore local avatar");
            player._UnhandledInput(Action("cursor_toggle"));
            Require(Input.MouseMode == Input.MouseModeEnum.Visible, "cursor release failed");
            player._UnhandledInput(Action("cursor_toggle"));

            GD.Print($"event=movement_smoke result=passed walk={walkDistance:F2} run={runDistance:F2} jump={maxY - groundY:F2}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=movement_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static InputEventAction Action(string name) => new() { Action = name, Pressed = true };
    private static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.X, a.Z).DistanceTo(new Vector2(b.X, b.Z));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
