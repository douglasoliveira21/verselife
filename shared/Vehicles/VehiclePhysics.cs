namespace LifeVerse.Shared.Vehicles;

public sealed class VehicleState
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    /// Radians. Zero faces -Z; the forward direction is (-sin yaw, -cos yaw), turning right lowers the yaw.
    public float Yaw { get; set; }
    /// Signed speed along the forward direction, metres per second (negative while reversing).
    public float Speed { get; set; }
    public float Steer { get; set; }
    public float Pitch { get; set; }
}

/// Throttle and steer are -1..1 (steer +1 is right). The server clamps whatever a client sends.
public readonly record struct VehicleInput(float Throttle, float Steer, bool Brake);

/// A deliberately simple arcade car: bicycle-model steering, limited acceleration, drag, braking, slopes
/// that slow you down, and water or the edge of the world that stop you. The server runs it; clients only watch.
public static class VehiclePhysics
{
    public const float WheelBase = 2.6f;
    public const float MaxSteerAngle = .55f;
    public const float SteerRate = 2.8f;
    public const float Acceleration = 7f;
    public const float BrakeDeceleration = 14f;
    public const float RollingResistance = 1.2f;
    public const float Drag = .25f;
    public const float ReverseSpeedFactor = .35f;
    public const float SlopeGravity = 5.5f;
    public const float DeepWater = .9f;
    public const float ProbeDistance = 1.3f;

    public static void Step(VehicleState state, VehicleInput input, float maxSpeed, float delta, Func<float, float, float>? terrainHeight = null,
        float seaLevel = float.NegativeInfinity, float worldLimit = float.PositiveInfinity)
    {
        delta = Math.Clamp(delta, .001f, .1f);
        var throttle = Sanitize(input.Throttle); var steer = Sanitize(input.Steer);
        var forwardX = -MathF.Sin(state.Yaw); var forwardZ = -MathF.Cos(state.Yaw);
        float Ground(float x, float z) => terrainHeight is null ? 0f : terrainHeight(x, z);

        var speed = state.Speed;
        if (input.Brake) speed = MoveToward(speed, 0f, BrakeDeceleration * delta);
        else if (throttle > 0f) speed = speed < -.1f ? MoveToward(speed, 0f, BrakeDeceleration * delta) : speed + Acceleration * throttle * MathF.Max(0f, 1f - speed / maxSpeed) * delta;
        else if (throttle < 0f) speed = speed > .1f ? MoveToward(speed, 0f, BrakeDeceleration * delta) : speed + Acceleration * throttle * MathF.Max(0f, 1f - speed / (-maxSpeed * ReverseSpeedFactor)) * delta;
        else speed = MoveToward(speed, 0f, (RollingResistance + Drag * MathF.Abs(speed)) * delta);

        var front = Ground(state.X + forwardX * ProbeDistance, state.Z + forwardZ * ProbeDistance);
        var back = Ground(state.X - forwardX * ProbeDistance, state.Z - forwardZ * ProbeDistance);
        var slope = (front - back) / (2f * ProbeDistance);
        speed -= slope * SlopeGravity * delta;
        speed = Math.Clamp(speed, -maxSpeed * ReverseSpeedFactor, maxSpeed);

        var steerLimit = MaxSteerAngle / (1f + MathF.Abs(speed) / 14f);
        state.Steer = MoveToward(state.Steer, steer * steerLimit, SteerRate * delta);
        state.Yaw = Wrap(state.Yaw - speed / WheelBase * MathF.Tan(state.Steer) * delta);

        forwardX = -MathF.Sin(state.Yaw); forwardZ = -MathF.Cos(state.Yaw);
        var nextX = state.X + forwardX * speed * delta; var nextZ = state.Z + forwardZ * speed * delta;
        var blocked = false;
        if (MathF.Abs(nextX) > worldLimit) { nextX = MathF.Sign(nextX) * worldLimit; blocked = true; }
        if (MathF.Abs(nextZ) > worldLimit) { nextZ = MathF.Sign(nextZ) * worldLimit; blocked = true; }
        if (seaLevel - Ground(nextX, nextZ) > DeepWater) { nextX = state.X; nextZ = state.Z; blocked = true; }
        if (blocked) speed = 0f;
        state.Speed = speed; state.X = nextX; state.Z = nextZ;
        state.Y = Ground(state.X, state.Z);
        var frontNow = Ground(state.X - MathF.Sin(state.Yaw) * ProbeDistance, state.Z - MathF.Cos(state.Yaw) * ProbeDistance);
        var backNow = Ground(state.X + MathF.Sin(state.Yaw) * ProbeDistance, state.Z + MathF.Cos(state.Yaw) * ProbeDistance);
        state.Pitch = MathF.Atan2(frontNow - backNow, 2f * ProbeDistance);
    }

    private static float Sanitize(float value) => float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f;
    private static float MoveToward(float from, float to, float amount) => from < to ? MathF.Min(from + amount, to) : MathF.Max(from - amount, to);
    private static float Wrap(float angle) { while (angle > MathF.PI) angle -= 2f * MathF.PI; while (angle < -MathF.PI) angle += 2f * MathF.PI; return angle; }
}
