using Godot;

namespace LifeVerse.Client.Scripts;

public partial class PlayerController : CharacterBody3D
{
    [Export] public float Speed { get; set; } = 6f;
    [Export] public float JumpVelocity { get; set; } = 5f;

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;
        if (!IsOnFloor()) velocity.Y -= 9.8f * (float)delta;
        if (Input.IsActionJustPressed("jump") && IsOnFloor()) velocity.Y = JumpVelocity;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var direction = (Transform.Basis * new Vector3(input.X, 0, input.Y)).Normalized();
        velocity.X = direction.X * Speed;
        velocity.Z = direction.Z * Speed;
        Velocity = velocity;
        MoveAndSlide();
    }
}
