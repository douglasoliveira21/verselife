namespace LifeVerse.Shared.Gameplay;

public enum AvatarMotionState
{
    Idle, Walk, Run, Jump, Fall, Landing, FlyIdle, FlyMove, Sit, StandUp, Swim, Dance, Wave, Applaud, Point, Greet, Laugh, SitFloor,
    /// <summary>Walking backwards (S or the back arrow): the body keeps facing forward. Only the player sees it on their own avatar; it is not a state the network carries or the editor can override.</summary>
    WalkBack,
    /// <summary>Hitting the ground flat after a fall that began when the flight stopped in the air: a long fall-and-lie animation, then the avatar gets up. Only on the player's own avatar, like WalkBack.</summary>
    FlatImpact,
    /// <summary>Treading water: in the water and not swimming anywhere. The network only says "swimming"; every client tells the two apart by how fast the avatar moves.</summary>
    SwimIdle
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

public static class BackwardsWalk
{
    /// <summary>Walking or running while pulling back (S or the back arrow) is walking backwards; every other state stays as it is.</summary>
    public static AvatarMotionState Apply(AvatarMotionState state, bool pullingBack) => pullingBack && state is AvatarMotionState.Walk or AvatarMotionState.Run ? AvatarMotionState.WalkBack : state;
}

public static class WaterStill
{
    /// <summary>Swimming without going anywhere is treading water; every other state stays as it is.</summary>
    public static AvatarMotionState Apply(AvatarMotionState state, bool moving) => state == AvatarMotionState.Swim && !moving ? AvatarMotionState.SwimIdle : state;
}
