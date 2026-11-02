namespace LifeVerse.Shared.Networking;

public sealed class AuthoritativePlayerState
{
    public float X { get; set; }
    public float Y { get; set; } = 1f;
    public float Z { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public bool Flying { get; set; }
    public bool CanFly { get; init; } = true;
    public bool Grounded => Y <= 1.001f;
}

public static class AuthoritativeMovement
{
    public const float WalkSpeed = 3.2f;
    public const float RunSpeed = 6.4f;
    public const float FlySpeed = 7.5f;
    public const float Acceleration = 28f;
    public const float JumpVelocity = 5.2f;

    public static NetworkMotionState Step(AuthoritativePlayerState state, MovementInput input, float delta)
    {
        delta = Math.Clamp(delta, 0.001f, 0.1f);
        var length = MathF.Sqrt(input.X * input.X + input.Z * input.Z);
        var x = length > 1f ? input.X / length : input.X;
        var z = length > 1f ? input.Z / length : input.Z;
        if (input.Flags.HasFlag(InputFlags.Fly) && state.CanFly) state.Flying = !state.Flying;
        var speed = input.Flags.HasFlag(InputFlags.Run) ? RunSpeed : WalkSpeed;
        if (state.Flying) speed = FlySpeed;
        state.VelocityX = MoveToward(state.VelocityX, x * speed, Acceleration * delta);
        state.VelocityZ = MoveToward(state.VelocityZ, z * speed, Acceleration * delta);
        if (state.Flying)
            state.VelocityY = input.Flags.HasFlag(InputFlags.Jump) ? FlySpeed : input.Flags.HasFlag(InputFlags.Descend) ? -FlySpeed : 0f;
        else
        {
            if (input.Flags.HasFlag(InputFlags.Jump) && state.Grounded) state.VelocityY = JumpVelocity;
            state.VelocityY -= 9.8f * delta;
        }
        state.X += state.VelocityX * delta;
        state.Y += state.VelocityY * delta;
        state.Z += state.VelocityZ * delta;
        if (state.Y <= 1f) { state.Y = 1f; state.VelocityY = Math.Max(0f, state.VelocityY); if (state.Flying && input.Flags.HasFlag(InputFlags.Descend)) state.Flying = false; }
        if (input.Flags.HasFlag(InputFlags.Sit)) return NetworkMotionState.Sit;
        if (input.Flags.HasFlag(InputFlags.Wave)) return NetworkMotionState.Wave;
        if (input.Flags.HasFlag(InputFlags.Dance)) return NetworkMotionState.Dance;
        if (state.Flying) return NetworkMotionState.Fly;
        if (!state.Grounded) return state.VelocityY > 0 ? NetworkMotionState.Jump : NetworkMotionState.Fall;
        if (length < .05f) return NetworkMotionState.Idle;
        return input.Flags.HasFlag(InputFlags.Run) ? NetworkMotionState.Run : NetworkMotionState.Walk;
    }

    private static float MoveToward(float from, float to, float amount) => from < to ? Math.Min(from + amount, to) : Math.Max(from - amount, to);
}
