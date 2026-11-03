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
    public bool Swimming { get; set; }
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
    // Ocean: the avatar swims once the water over its feet is chest deep and floats with its head just above the surface.
    public const float WadeDepth = 1.1f;
    public const float SwimFloatDepth = 1.2f;
    public const float SwimSurfaceMargin = .35f;
    public const float SwimSpeed = 3.5f;
    public const float SwimRunSpeed = 5f;
    public const float SwimVerticalSpeed = 2.5f;

    public static NetworkMotionState Step(AuthoritativePlayerState state, MovementInput input, float delta, Func<float, float, float>? terrainHeight = null,
        float seaLevel = float.NegativeInfinity, float worldLimit = float.PositiveInfinity, ObstacleField? obstacles = null)
    {
        delta = Math.Clamp(delta, 0.001f, 0.1f);
        // the ground is the terrain or, where something solid can be stood on, that thing's top surface
        float GroundAt(float gx, float gz, float feetY)
        {
            var terrain = terrainHeight is null ? GroundOffset : terrainHeight(gx, gz) + GroundOffset;
            if (obstacles is null) return terrain;
            var support = obstacles.SupportHeight(gx, feetY, gz);
            return float.IsNegativeInfinity(support) ? terrain : MathF.Max(terrain, support);
        }
        state.GroundY = GroundAt(state.X, state.Z, state.Y);
        var wasGrounded = state.Grounded;
        var jumped = false;
        if (input.Flags.HasFlag(InputFlags.Sit)) state.Seated = !state.Seated;
        if (state.Seated) { state.VelocityX = 0; state.VelocityY = 0; state.VelocityZ = 0; return NetworkMotionState.Sit; }
        var length = MathF.Sqrt(input.X * input.X + input.Z * input.Z);
        var x = length > 1f ? input.X / length : input.X;
        var z = length > 1f ? input.Z / length : input.Z;
        if (input.Flags.HasFlag(InputFlags.Fly) && state.CanFly) state.Flying = !state.Flying;
        var running = input.Flags.HasFlag(InputFlags.Run);
        state.Swimming = !state.Flying && seaLevel - state.GroundY >= WadeDepth;
        var floatY = seaLevel - SwimFloatDepth;
        var speed = state.Swimming ? (running ? SwimRunSpeed : SwimSpeed) : running ? RunSpeed : WalkSpeed;
        if (state.Flying) speed = FlySpeed;
        state.VelocityX = SmoothHorizontal(state.VelocityX, x * speed, delta);
        state.VelocityZ = SmoothHorizontal(state.VelocityZ, z * speed, delta);
        if (state.Flying)
            state.VelocityY = input.Flags.HasFlag(InputFlags.Jump) ? FlySpeed : input.Flags.HasFlag(InputFlags.Descend) ? -FlySpeed : 0f;
        else if (state.Swimming)
        {
            var target = input.Flags.HasFlag(InputFlags.Jump) ? SwimVerticalSpeed : input.Flags.HasFlag(InputFlags.Descend) ? -SwimVerticalSpeed : Math.Clamp((floatY - state.Y) * 3f, -2f, 2f);
            state.VelocityY = MoveToward(state.VelocityY, target, 14f * delta);
        }
        else
        {
            if (input.Flags.HasFlag(InputFlags.Jump) && state.Grounded) { state.VelocityY = JumpVelocity; jumped = true; }
            state.VelocityY -= 9.8f * delta;
        }
        var previousY = state.Y;
        state.Y += state.VelocityY * delta;
        // horizontal movement goes in small pieces so a fast avatar can never jump over a thin wall between two checks
        var moveX = state.VelocityX * delta; var moveZ = state.VelocityZ * delta;
        var pieces = obstacles is null ? 1 : Math.Clamp((int)MathF.Ceiling(MathF.Max(MathF.Abs(moveX), MathF.Abs(moveZ)) / .15f), 1, 8);
        for (var piece = 0; piece < pieces; piece++) { state.X += moveX / pieces; state.Z += moveZ / pieces; obstacles?.Resolve(state); }
        if (MathF.Abs(state.X) > worldLimit) { state.X = MathF.Sign(state.X) * worldLimit; state.VelocityX = 0; }
        if (MathF.Abs(state.Z) > worldLimit) { state.Z = MathF.Sign(state.Z) * worldLimit; state.VelocityZ = 0; }
        if (state.Swimming) state.Y = MathF.Min(state.Y, floatY + SwimSurfaceMargin);
        var ground = GroundAt(state.X, state.Z, MathF.Max(previousY, state.Y));
        var followsGround = !state.Flying && wasGrounded && !jumped && state.VelocityY <= 0f && state.Y - ground <= StepDown;
        state.GroundY = ground;
        if (state.Y <= ground || followsGround) { state.Y = ground; state.VelocityY = Math.Max(0f, state.VelocityY); if (state.Flying && input.Flags.HasFlag(InputFlags.Descend)) state.Flying = false; }
        if (input.Flags.HasFlag(InputFlags.Wave)) return NetworkMotionState.Wave;
        if (input.Flags.HasFlag(InputFlags.Dance)) return NetworkMotionState.Dance;
        if (state.Flying) return NetworkMotionState.Fly;
        if (state.Swimming) return NetworkMotionState.Swim;
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
