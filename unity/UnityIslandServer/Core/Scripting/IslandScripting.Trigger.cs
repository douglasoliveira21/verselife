using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>Trigger volumes: an object whose script handles on_trigger_enter / on_trigger_exit is a volume, and the avatars that step into the box of the object (its own size and heading, not a radius around it) are announced. Spheres and cylinders count as the box around them.</summary>
public sealed partial class IslandScripting
{
    public const float TriggerMargin = .3f, TriggerHeadroom = 2f;
    public const int MaxTriggerVolumes = 200;
    private readonly HashSet<(Guid Object, Guid Avatar)> _inTrigger = [];

    /// <summary>Whether a point stands inside the volume of an object (turned by its yaw).</summary>
    public static bool InsideVolume(Vec3 position, Vec3 rotation, Vec3 scale, float x, float y, float z)
    {
        var yaw = -rotation.Y * MathF.PI / 180f; var dx = x - position.X; var dz = z - position.Z;
        var localX = dx * MathF.Cos(yaw) + dz * MathF.Sin(yaw); var localZ = -dx * MathF.Sin(yaw) + dz * MathF.Cos(yaw);
        return MathF.Abs(localX) <= scale.X / 2 + TriggerMargin && MathF.Abs(localZ) <= scale.Z / 2 + TriggerMargin && y >= position.Y - scale.Y / 2 - TriggerMargin && y <= position.Y + scale.Y / 2 + TriggerHeadroom;
    }

    private void WatchTriggers(IReadOnlyList<AvatarPresence> avatars)
    {
        var volumes = _host.ObjectsWanting("trigger_enter").Concat(_host.ObjectsWanting("trigger_exit")).Distinct().Take(MaxTriggerVolumes).ToHashSet();
        foreach (var key in _inTrigger.Where(k => !volumes.Contains(k.Object) || avatars.All(a => a.AvatarId != k.Avatar)).ToList()) _inTrigger.Remove(key);
        foreach (var objectId in volumes)
        {
            if (!_objects.TryGetValue(objectId, out var volume)) continue;
            foreach (var avatar in avatars)
            {
                var inside = InsideVolume(volume.Position, volume.Rotation, volume.Scale, avatar.X, avatar.Y, avatar.Z);
                if (inside == _inTrigger.Contains((objectId, avatar.AvatarId))) continue;
                if (inside) _inTrigger.Add((objectId, avatar.AvatarId)); else _inTrigger.Remove((objectId, avatar.AvatarId));
                _host.Enqueue(new ScriptEvent(objectId, inside ? "trigger_enter" : "trigger_exit", Describe(avatar, Distance(avatar, volume))));
            }
        }
    }
}
