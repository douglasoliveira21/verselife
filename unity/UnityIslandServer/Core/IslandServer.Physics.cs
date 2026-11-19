using System.Text.Json;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Core;

/// <summary>Object physics, driven from the tick: keeps the bodies in step with the objects, shoves them with walkers, steps the simulation and tells the players the poses that changed.</summary>
public sealed partial class IslandServer
{
    private const int PosesPerMessage = 40;
    private const double PoseInterval = .1;
    private const float MovedEnough = .004f;

    private IIslandPhysics? _physics;
    private int _physicsObjectsVersion = -1;
    private double _physicsCheck, _poseTimer;
    private readonly Dictionary<Guid, BodyPose> _sentPoses = new();

    /// <summary>The physics of this island. Set it before <see cref="StartAsync"/>; with none, objects never move by themselves.</summary>
    public IIslandPhysics? Physics
    {
        get => _physics;
        set { _physics = value; _physicsObjectsVersion = -1; _sentPoses.Clear(); }
    }

    private void TickPhysics(float delta)
    {
        if (_physics is null || _scripting is null) return;
        _physicsCheck += delta;
        if (_physicsCheck >= .5)
        {
            _physicsCheck = 0;
            _physics.SetTerrain(_terrain);
            if (_scripting.ObjectsVersion != _physicsObjectsVersion) { _physicsObjectsVersion = _scripting.ObjectsVersion; _physics.Sync(_scripting.Bodies()); }
        }
        if (_physics.BodyCount == 0) return;
        foreach (var player in _players.Values)
            if (player.Authenticated && !player.State.Seated) _physics.Push(player.State.X, player.State.Y, player.State.Z, player.State.VelocityX, player.State.VelocityZ);
        _physics.Step(Math.Min(delta, 1f / 20f));
        _poseTimer += delta;
        if (_poseTimer < PoseInterval) return;
        _poseTimer = 0;
        var poses = _physics.Poses();
        var changed = new List<BodyPose>();
        foreach (var pose in poses)
        {
            if (_sentPoses.TryGetValue(pose.Id, out var sent))
            {
                if (pose.Asleep && sent.Asleep) continue;
                if (!pose.Asleep && !sent.Asleep && Moved(sent, pose) < MovedEnough && RotationDelta(sent, pose) < .01f) continue;
            }
            _sentPoses[pose.Id] = pose; changed.Add(pose);
        }
        if (_sentPoses.Count > poses.Count) { var alive = new HashSet<Guid>(poses.Select(p => p.Id)); foreach (var gone in _sentPoses.Keys.Where(id => !alive.Contains(id)).ToList()) _sentPoses.Remove(gone); }
        foreach (var message in PoseMessages(changed)) ToAll(message);
    }

    private static float Moved(BodyPose a, BodyPose b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>The angle between two orientations, in radians.</summary>
    private static float RotationDelta(BodyPose a, BodyPose b)
    {
        var dot = Math.Abs(a.QX * b.QX + a.QY * b.QY + a.QZ * b.QZ + a.QW * b.QW);
        return 2f * MathF.Acos(Math.Min(1f, dot));
    }

    private static float Round(float value) => MathF.Round(value, 3);

    private static IEnumerable<string> PoseMessages(IReadOnlyList<BodyPose> poses)
    {
        for (var start = 0; start < poses.Count; start += PosesPerMessage)
            yield return JsonSerializer.Serialize(new
            {
                operation = "physics",
                bodies = poses.Skip(start).Take(PosesPerMessage).Select(p => new { id = p.Id, p = new[] { Round(p.X), Round(p.Y), Round(p.Z) }, q = new[] { Round(p.QX), Round(p.QY), Round(p.QZ), Round(p.QW) }, asleep = p.Asleep }),
            });
    }

    /// <summary>The pose of every body, as messages for somebody who has just arrived.</summary>
    private void SendPhysicsState(int peerId)
    {
        if (_physics is null || _physics.BodyCount == 0) return;
        foreach (var message in PoseMessages(_physics.Poses())) _wire.Send(peerId, new LifeVerse.Wire.BuildOperationAppliedMessage(message));
    }
}
