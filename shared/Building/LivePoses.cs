namespace LifeVerse.Shared.Building;

/// <summary>Where one object is while somebody drags it (not saved: the drag is only shown to the others until it is let go).</summary>
public sealed record LivePose(Guid Id, float PositionX, float PositionY, float PositionZ, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ);

/// <summary>
/// The rules for showing a drag to other players as it happens. Only the person who holds the edit lease of an object may move it live, the numbers must be real and in range,
/// and a message carries a handful of objects at most. Nothing here changes the object: the saved pose is still the ordinary, server-checked update at the end of the drag.
/// </summary>
public static class LivePoses
{
    public const int MaxObjects = 64;
    public const float MaxCoordinate = 100_000f, MinScale = .005f, MaxScale = 128f;

    public static bool Accept(IReadOnlyList<LivePose>? poses, EditLocks locks, Guid user, DateTimeOffset now)
    {
        if (poses is null || poses.Count == 0 || poses.Count > MaxObjects || poses.Select(p => p.Id).Distinct().Count() != poses.Count) return false;
        foreach (var pose in poses)
        {
            if (locks.HolderOf(pose.Id, now) != user) return false;
            float[] positions = [pose.PositionX, pose.PositionY, pose.PositionZ]; float[] angles = [pose.RotationX, pose.RotationY, pose.RotationZ]; float[] scales = [pose.ScaleX, pose.ScaleY, pose.ScaleZ];
            if (positions.Any(v => !float.IsFinite(v) || Math.Abs(v) > MaxCoordinate) || angles.Any(v => !float.IsFinite(v) || Math.Abs(v) > 100_000f) || scales.Any(v => !float.IsFinite(v) || v < MinScale || v > MaxScale)) return false;
        }
        return true;
    }
}
