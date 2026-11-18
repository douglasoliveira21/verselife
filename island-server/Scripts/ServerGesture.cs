using Godot;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Scripts;

/// A player applauds, points, greets, laughs or sits on the floor. Only the number of the gesture comes in (the island knows which ones exist and for how long they play);
/// it is passed on to the players who can see the one who made it, never more than a few times a second.
public partial class ServerMain
{
    private readonly Dictionary<int, DateTimeOffset> _lastGesture = [];

    public void RelayGesture(int peerId, int gestureId)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        var now = DateTimeOffset.UtcNow;
        if (!ExtraGestures.MayRelay(_lastGesture.TryGetValue(peerId, out var last) ? last : null, now)) return;
        if (!ExtraGestures.IsValid(gestureId)) { Log("gesture_refused", new { peerId, gestureId }); return; }
        _lastGesture[peerId] = now;
        foreach (var target in _players.Values.Where(x => x.Authenticated && x.PeerId != peerId && new Vector3(x.State.X - player.State.X, x.State.Y - player.State.Y, x.State.Z - player.State.Z).Length() <= _config.InterestRadius))
            _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.GestureChanged, peerId, gestureId);
    }

    private void ForgetGesture(int peerId) => _lastGesture.Remove(peerId);
}
