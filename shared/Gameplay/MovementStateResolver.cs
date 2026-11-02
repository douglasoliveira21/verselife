namespace LifeVerse.Shared.Gameplay;

public enum AvatarMotionState
{
    Idle, Walk, Run, Jump, Fall, Landing, FlyIdle, FlyMove, Sit, StandUp, Swim, Dance, Wave
}

public readonly record struct MovementSnapshot(
    bool IsSeated,
    bool IsSwimming,
    bool IsFlying,
    bool IsGrounded,
    bool IsRising,
    bool IsLanding,
    bool IsMoving,
    bool IsRunning);

public static class MovementStateResolver
{
    public static AvatarMotionState Resolve(MovementSnapshot value)
    {
        if (value.IsSeated) return AvatarMotionState.Sit;
        if (value.IsLanding) return AvatarMotionState.Landing;
        if (value.IsSwimming) return AvatarMotionState.Swim;
        if (value.IsFlying) return value.IsMoving ? AvatarMotionState.FlyMove : AvatarMotionState.FlyIdle;
        if (!value.IsGrounded) return value.IsRising ? AvatarMotionState.Jump : AvatarMotionState.Fall;
        if (!value.IsMoving) return AvatarMotionState.Idle;
        return value.IsRunning ? AvatarMotionState.Run : AvatarMotionState.Walk;
    }
}
