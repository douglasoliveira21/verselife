using Godot;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.IslandServer.Scripts;

/// A player is dragging an object worn on the body to a new place. The API keeps what is saved; this is only the live preview the others see while it is dragged: it is passed on only for an
/// attachment the player really wears (it is in the appearance the server holds for them), at most a few times a second, and only with numbers the API would accept. Nothing is kept: the saved
/// placement reaches everybody as a normal appearance refresh when the player presses save, and a cancel sends the saved placement back through this same way.
public partial class ServerMain
{
    private readonly Dictionary<int, DateTimeOffset> _lastAttachmentLive = [];
    private readonly Dictionary<int, (string Json, HashSet<Guid> Ids)> _wornCache = [];

    private HashSet<Guid> WornBy(int peerId, string appearanceJson)
    {
        if (_wornCache.TryGetValue(peerId, out var cached) && ReferenceEquals(cached.Json, appearanceJson)) return cached.Ids;
        var ids = AttachmentLive.WornIds(appearanceJson); _wornCache[peerId] = (appearanceJson, ids); return ids;
    }

    public void RelayAttachmentLive(int peerId, string id, float[] values)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        var now = DateTimeOffset.UtcNow;
        if (!AttachmentLive.MayRelay(_lastAttachmentLive.TryGetValue(peerId, out var last) ? last : null, now)) return;
        if (!Guid.TryParse(id, out var attachment) || !WornBy(peerId, player.AppearanceJson).Contains(attachment)) { Log("attachment_live_refused", new { peerId, reason = "not_worn" }); return; }
        if (AttachmentLive.Check(values) is { } problem) { Log("attachment_live_refused", new { peerId, reason = problem }); return; }
        _lastAttachmentLive[peerId] = now;
        foreach (var target in _players.Values.Where(x => x.Authenticated && x.PeerId != peerId)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.AttachmentLiveChanged, peerId, id, values);
    }

    private void ForgetAttachmentState(int peerId) { _lastAttachmentLive.Remove(peerId); _wornCache.Remove(peerId); }
}
