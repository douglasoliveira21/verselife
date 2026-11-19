using System.Text.Json;
using LifeVerse.Shared.Land;
using LifeVerse.Shared.Networking;
using LifeVerse.Wire;

namespace LifeVerse.IslandServer.Core;

/// <summary>
/// Land: every few seconds the API tells the island who may stand where (closed parcels, bans), who may sculpt which parcels, where teleports land
/// and what the parcels forbid, plus the events the land system produced behind our back (returned or handed-over objects), which go to everyone
/// as ordinary build messages. A player standing where they may not is moved to the nearest allowed spot, whatever got them there.
/// </summary>
public sealed partial class IslandServer
{
    private LandAccessMap _access = LandAccessMap.Empty;
    private LandTerrainRights _rights = LandTerrainRights.Empty;
    private LandTeleport _teleports = LandTeleport.Empty;
    private LandRules _landRules = LandRules.Empty;
    private long _landCursor;
    private static readonly TimeSpan TeleportCooldown = TimeSpan.FromSeconds(1);

    private async Task LandSyncLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var body = await _api.LandSyncAsync(_landCursor, _cts.Token).ConfigureAwait(false);
                    if (body is not null) _inbox.Enqueue(() => ApplyLandSync(body));
                }
                catch (Exception exception) when (exception is not OperationCanceledException) { _log.Write("land_sync_failed", new { error = exception.Message }); }
                await Task.Delay(TimeSpan.FromSeconds(_options.LandSyncSeconds), _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyLandSync(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("access", out var access)) _access = LandAccessMap.Parse(access);
            if (root.TryGetProperty("terrain", out var terrain)) _rights = LandTerrainRights.Parse(terrain);
            if (root.TryGetProperty("teleport", out var teleport)) _teleports = LandTeleport.Parse(teleport);
            if (root.TryGetProperty("rules", out var rules)) _landRules = LandRules.Parse(rules);
            if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
                foreach (var item in events.EnumerateArray())
                {
                    var message = new BuildOperationAppliedMessage(item.GetRawText());
                    foreach (var target in _players.Values.Where(x => x.Authenticated)) _wire.Send(target.PeerId, message);
                }
            if (root.TryGetProperty("cursor", out var cursor) && cursor.TryGetInt64(out var value)) _landCursor = value;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { _log.Write("land_sync_failed", new { error = exception.Message }); }
    }

    /// <summary>A player standing where they are not allowed (a closed parcel, a ban) is moved to the nearest allowed spot. The client is never asked.</summary>
    private void KeepOutOfClosedLand(Player player)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var state = player.State;
        if (!_access.Denies(player.UserId, state.X, state.Z, nowMs)) return;
        if (_access.NearestAllowed(player.UserId, state.X, state.Z, nowMs) is not { } spot) return;
        state.X = (float)spot.X; state.Z = (float)spot.Z; state.Y = _terrain.SampleHeight(state.X, state.Z) + AuthoritativeMovement.GroundOffset;
        state.VelocityX = state.VelocityY = state.VelocityZ = 0; state.Flying = false; state.Seated = false;
        var now = DateTimeOffset.UtcNow;
        if (!player.LastPushLog.HasValue || now - player.LastPushLog > TimeSpan.FromSeconds(10)) { player.LastPushLog = now; _log.Write("land_access_pushed", new { player.UserId, x = state.X, z = state.Z }); }
    }

    /// <summary>Teleporting inside the island: the parcel's own rules (free, forced to its arrival point, refused) and its access list are applied here; the client only gets the answer.</summary>
    private void OnTeleport(Player player, RequestTeleportMessage request)
    {
        var now = DateTimeOffset.UtcNow;
        if (player.LastTeleport.HasValue && now - player.LastTeleport < TeleportCooldown) { RefuseTeleport(player, "rate_limit"); return; }
        player.LastTeleport = now;
        if (_fleet.VehicleOf(player.PeerId) is not null) { RefuseTeleport(player, "in_vehicle"); return; }
        Guid? parcel = null;
        if (!string.IsNullOrEmpty(request.ParcelId))
        {
            if (!Guid.TryParse(request.ParcelId, out var id)) { RefuseTeleport(player, "invalid"); return; }
            parcel = id;
        }
        var nowMs = now.ToUnixTimeMilliseconds();
        var half = _terrain.HalfExtent;
        var outcome = _teleports.Resolve(parcel, request.X, request.Z, (px, pz) => _access.Denies(player.UserId, px, pz, nowMs));
        if (outcome.Ok && (Math.Abs(outcome.X) > half || Math.Abs(outcome.Z) > half)) outcome = new LandTeleport.Outcome(false, "invalid", 0, 0, 0, null);
        if (!outcome.Ok) { RefuseTeleport(player, outcome.Code); return; }
        var state = player.State;
        state.X = (float)outcome.X; state.Z = (float)outcome.Z;
        state.Y = _terrain.SampleHeight(state.X, state.Z) + AuthoritativeMovement.GroundOffset;
        state.VelocityX = state.VelocityY = state.VelocityZ = 0; state.Flying = false; state.Seated = false;
        _wire.Send(player.PeerId, new TeleportResultMessage(true, "ok", state.X, state.Z, (float)outcome.Yaw));
        _log.Write("teleport", new { player.UserId, parcel, x = state.X, z = state.Z });
    }

    private void RefuseTeleport(Player player, string code) => _wire.Send(player.PeerId, new TeleportResultMessage(false, code, 0f, 0f, 0f));
}
