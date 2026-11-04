using Godot;
using LifeVerse.Shared.Land;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Scripts;

/// Teleporting inside the island: the player asks for a parcel (or a spot) and this decides where they land. The parcel's own rules
/// (free, forced to its arrival point, or refused) and its access list are applied here; the client only gets the answer.
public partial class ServerMain
{
    private LandTeleport _teleports = LandTeleport.Empty;
    private readonly Dictionary<int, DateTimeOffset> _lastTeleport = [];
    private static readonly TimeSpan TeleportCooldown = TimeSpan.FromSeconds(1);

    public void RequestTeleport(int peerId, string parcelId, float x, float z)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        var now = DateTimeOffset.UtcNow;
        if (_lastTeleport.TryGetValue(peerId, out var last) && now - last < TeleportCooldown) { RefuseTeleport(peerId, "rate_limit"); return; }
        _lastTeleport[peerId] = now;
        if (_fleet.VehicleOf(peerId) is not null) { RefuseTeleport(peerId, "in_vehicle"); return; }
        Guid? parcel = null;
        if (!string.IsNullOrEmpty(parcelId))
        {
            if (!Guid.TryParse(parcelId, out var id)) { RefuseTeleport(peerId, "invalid"); return; }
            parcel = id;
        }
        var nowMs = now.ToUnixTimeMilliseconds();
        var half = _terrain.HalfExtent;
        var outcome = _teleports.Resolve(parcel, x, z, (px, pz) => _access.Denies(player.UserId, px, pz, nowMs));
        if (outcome.Ok && (Math.Abs(outcome.X) > half || Math.Abs(outcome.Z) > half)) outcome = new LandTeleport.Outcome(false, "invalid", 0, 0, 0, null);
        if (!outcome.Ok) { RefuseTeleport(peerId, outcome.Code); return; }

        var state = player.State;
        state.X = (float)outcome.X; state.Z = (float)outcome.Z;
        state.Y = _terrain.SampleHeight(state.X, state.Z) + AuthoritativeMovement.GroundOffset;
        state.VelocityX = state.VelocityY = state.VelocityZ = 0; state.Flying = false; state.Seated = false;
        _scripting?.Obstacles.Resolve(state); // not inside a wall
        state.Y = Math.Max(state.Y, _terrain.SampleHeight(state.X, state.Z) + AuthoritativeMovement.GroundOffset);
        _bridge.RpcId(peerId, NetworkBridge.MethodName.TeleportResult, true, "ok", state.X, state.Z, (float)outcome.Yaw);
        Log("teleport", new { player.UserId, parcel, x = state.X, z = state.Z });
    }

    private void RefuseTeleport(int peerId, string code) => _bridge.RpcId(peerId, NetworkBridge.MethodName.TeleportResult, false, code, 0f, 0f, 0f);
}
