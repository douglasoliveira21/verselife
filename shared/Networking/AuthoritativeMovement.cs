namespace LifeVerse.Shared.Networking;

public sealed class AuthoritativePlayerState
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public bool Flying { get; set; }
    public bool Seated { get; set; }
    public bool CanFly { get; init; } = true;
    public float GroundY { get; set; }
    public bool Grounded => Y <= GroundY + .001f;
}

public static class AuthoritativeMovement
{
    public const float WalkSpeed = 3.2f;
    public const float RunSpeed = 6.4f;
    public const float FlySpeed = 7.5f;
    public const float Acceleration = 40f;
    public const float Deceleration = 60f;
    public const float JumpVelocity = 5.2f;
    // The avatar origin is at its feet, so it rests exactly on the terrain surface; StepDown is the largest
    // drop the avatar sticks to when walking downhill.
    public const float GroundOffset = 0f;
    public const float StepDown = .35f;

    public static NetworkMotionState Step(AuthoritativePlayerState state, MovementInput input, float delta, Func<float, float, float>? terrainHeight = null)
    {
        delta = Math.Clamp(delta, 0.001f, 0.1f);
        state.GroundY = terrainHeight is null ? GroundOffset : terrainHeight(state.X, state.Z) + GroundOffset;
        var wasGrounded = state.Grounded;
        var jumped = false;
        if (input.Flags.HasFlag(InputFlags.Sit)) state.Seated = !state.Seated;
        if (state.Seated) { state.VelocityX = 0; state.VelocityY = 0; state.VelocityZ = 0; return NetworkMotionState.Sit; }
        var length = MathF.Sqrt(input.X * input.X + input.Z * input.Z);
        var x = length > 1f ? input.X / length : input.X;
        var z = length > 1f ? input.Z / length : input.Z;
        if (input.Flags.HasFlag(InputFlags.Fly) && state.CanFly) state.Flying = !state.Flying;
        var speed = input.Flags.HasFlag(InputFlags.Run) ? RunSpeed : WalkSpeed;
        if (state.Flying) speed = FlySpeed;
        state.VelocityX = SmoothHorizontal(state.VelocityX, x * speed, delta);
        state.VelocityZ = SmoothHorizontal(state.VelocityZ, z * speed, delta);
        if (state.Flying)
            state.VelocityY = input.Flags.HasFlag(InputFlags.Jump) ? FlySpeed : input.Flags.HasFlag(InputFlags.Descend) ? -FlySpeed : 0f;
        else
        {
            if (input.Flags.HasFlag(InputFlags.Jump) && state.Grounded) { state.VelocityY = JumpVelocity; jumped = true; }
            state.VelocityY -= 9.8f * delta;
        }
        state.X += state.VelocityX * delta;
        state.Y += state.VelocityY * delta;
        state.Z += state.VelocityZ * delta;
        var ground = terrainHeight is null ? GroundOffset : terrainHeight(state.X, state.Z) + GroundOffset;
        var followsGround = !state.Flying && wasGrounded && !jumped && state.Y - ground <= StepDown;
        state.GroundY = ground;
        if (state.Y <= ground || followsGround) { state.Y = ground; state.VelocityY = Math.Max(0f, state.VelocityY); if (state.Flying && input.Flags.HasFlag(InputFlags.Descend)) state.Flying = false; }
        if (input.Flags.HasFlag(InputFlags.Wave)) return NetworkMotionState.Wave;
        if (input.Flags.HasFlag(InputFlags.Dance)) return NetworkMotionState.Dance;
        if (state.Flying) return NetworkMotionState.Fly;
        if (!state.Grounded) return state.VelocityY > 0 ? NetworkMotionState.Jump : NetworkMotionState.Fall;
        if (length < .05f) return NetworkMotionState.Idle;
        return input.Flags.HasFlag(InputFlags.Run) ? NetworkMotionState.Run : NetworkMotionState.Walk;
    }

    private static float MoveToward(float from, float to, float amount) => from < to ? Math.Min(from + amount, to) : Math.Max(from - amount, to);

    private static float SmoothHorizontal(float current, float target, float delta)
    {
        var accelerating = MathF.Abs(target) > MathF.Abs(current) || MathF.Sign(target) != MathF.Sign(current);
        return MoveToward(current, target, (accelerating ? Acceleration : Deceleration) * delta);
    }
}
