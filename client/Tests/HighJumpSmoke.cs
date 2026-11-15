using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Networking;

namespace LifeVerse.Client.Tests;

/// <summary>Standing on something built that the server does not know (it believes the avatar is on the ground): reconciling must not pull the avatar down, and a jump from up there must lift it.</summary>
public partial class HighJumpSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var player = world.GetNode<PlayerController>("Player");
            await Frames(20);
            var body = new StaticBody3D { Position = new Vector3(0, 2.5f, 0) };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 5, 6) } }); world.AddChild(body);
            player.GlobalPosition = new Vector3(0, 5.2f, 0); player.Velocity = Vector3.Zero;
            await Frames(40);
            Require(player.IsOnFloor() && Mathf.Abs(player.GlobalPosition.Y - 5f) < .15f, $"the avatar stands on the box ({player.GlobalPosition})");
            for (var i = 0; i < 30; i++) { player.Reconcile(new Vector3(0, 0, 0), Vector3.Zero, 0, NetworkMotionState.Idle); await Frames(1); }
            Require(player.GlobalPosition.Y > 4.8f, $"the server's lower position does not pull the avatar off the box ({player.GlobalPosition.Y:0.00})");
            Input.ActionPress("jump"); await Frames(2); Input.ActionRelease("jump");
            var peak = player.GlobalPosition.Y;
            for (var i = 0; i < 60; i++) { player.Reconcile(new Vector3(0, 0, 0), Vector3.Zero, 0, NetworkMotionState.Idle); await Frames(1); peak = Mathf.Max(peak, player.GlobalPosition.Y); }
            Require(peak > 5f + 1.0f, $"the jump from the top lifts the avatar ({peak - 5f:0.00} m)");
            GD.Print("event=high_jump_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=high_jump_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
