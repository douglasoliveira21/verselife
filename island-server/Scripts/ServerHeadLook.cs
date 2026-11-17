using Godot;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Scripts;

/// A player looks up or down through their own eyes (first person). The island passes the angle on to the players who can see them (nothing else is kept): never more than a few times a second, and
/// only a plain angle (the camera is not a thing the network carries). What the others do with it is only to lean the head.
public partial class ServerMain
{
    private readonly Dictionary<int, DateTimeOffset> _lastHeadLook = [];

    public void RelayHeadLook(int peerId, float pitch)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        var now = DateTimeOffset.UtcNow;
        if (!HeadLook.MayRelay(_lastHeadLook.TryGetValue(peerId, out var last) ? last : null, now)) return;
        if (HeadLook.Check(pitch) is { } problem) { Log("head_look_refused", new { peerId, reason = problem }); return; }
        _lastHeadLook[peerId] = now;
        var drawn = HeadLook.Drawn(pitch);
        foreach (var target in _players.Values.Where(x => x.Authenticated && x.PeerId != peerId && HeadLook.InRange(new Vector3(x.State.X - player.State.X, x.State.Y - player.State.Y, x.State.Z - player.State.Z).Length(), _config.InterestRadius)))
            _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.HeadLookChanged, peerId, drawn);
    }

    private void ForgetHeadLook(int peerId) => _lastHeadLook.Remove(peerId);
}
