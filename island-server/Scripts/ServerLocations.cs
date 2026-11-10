using System.Net.Http.Json;
using Godot;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>
/// Where each player stands is told to the API every so often and when somebody leaves, so signing in again brings them back to the same island and
/// the same spot (the API keeps only the last place). A failure to tell is only logged: playing never waits for it.
/// </summary>
public partial class ServerMain
{
    private const double LocationSaveSeconds = 20;
    private double _locationAccumulator;

    private void TickLocations(double delta)
    {
        _locationAccumulator += delta;
        if (_locationAccumulator < LocationSaveSeconds) return;
        _locationAccumulator = 0;
        var players = _players.Values.Where(x => x.Authenticated).ToList();
        if (players.Count > 0) _ = SaveLocationsAsync(players, _shutdown.Token);
    }

    private async Task SaveLocationsAsync(IReadOnlyCollection<ConnectedPlayer> players, CancellationToken ct)
    {
        try
        {
            var entries = players.Where(p => p.UserId != Guid.Empty).Select(p => new { userId = p.UserId, x = p.State.X, y = p.State.Y, z = p.State.Z }).ToArray();
            if (entries.Length == 0) return;
            using var response = await _api.PutAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/player-locations", new { players = entries }, ct);
            if (!response.IsSuccessStatusCode) { Log("player_locations_refused", new { status = (int)response.StatusCode }); return; }
            ReleaseTravellers(await response.Content.ReadFromJsonAsync<LocationAnswer>(cancellationToken: ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { Log("player_locations_failed", new { error = exception.Message }); }
    }

    /// <summary>Where a player arriving now stands: the spot they left from on this island, or around the island's spawn point.</summary>
    private void PlaceArriving(ConnectedPlayer player, AdmissionResponse admission)
    {
        var slot = _players.Values.Count(x => x.Authenticated);
        (float X, float Z)? resume = admission.ResumeX is { } rx && admission.ResumeZ is { } rz ? (rx, rz) : null;
        var spot = SpawnPlanner.Choose(admission.SpawnX, admission.SpawnZ, slot, resume, admission.IslandSize);
        var ground = _terrain.SampleHeight(spot.X, spot.Z) + AuthoritativeMovement.GroundOffset;
        player.State.X = spot.X; player.State.Z = spot.Z;
        player.State.Y = resume is not null && Math.Abs(spot.X - resume.Value.X) < .001f ? SpawnPlanner.Height(ground, admission.ResumeY) : ground;
    }
}

public partial class ServerMain
{
    private sealed record ReleasedEntry(Guid UserId, long ArrivedAtUnixMs);
    private sealed record LocationAnswer(int Kept, List<ReleasedEntry>? Release);

    /// <summary>
    /// People whose trip to another island was confirmed there. They leave this island, unless they came back here after that moment (a later connection is a new visit).
    /// </summary>
    private void ReleaseTravellers(LocationAnswer? answer)
    {
        if (answer?.Release is not { Count: > 0 } released) return;
        foreach (var entry in released)
            foreach (var player in _players.Values.Where(p => p.Authenticated && p.UserId == entry.UserId && LifeVerse.Shared.Loading.TravellerRelease.ShouldDrop(p.ConnectedAt, entry.ArrivedAtUnixMs)).ToList())
            {
                player.Released = true;
                Log("traveller_released", new { player.PeerId, player.UserId });
                Multiplayer.MultiplayerPeer.DisconnectPeer(player.PeerId);
            }
    }

    /// <summary>Tells the API the traveller is here. A trip the API no longer accepts (cancelled, expired) sends the person away again, so nobody is left on two islands.</summary>
    private async Task ConfirmArrivalAsync(int peerId, Guid transferId, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _api.PostAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/transfers/{transferId}/arrived", null, ct);
                if (response.IsSuccessStatusCode) { Log("traveller_arrival_confirmed", new { peerId, transferId }); return; }
                if ((int)response.StatusCode is 404 or 409)
                {
                    Log("traveller_arrival_refused", new { peerId, transferId, status = (int)response.StatusCode });
                    if (_players.TryGetValue(peerId, out var player)) { player.Released = true; Multiplayer.MultiplayerPeer.DisconnectPeer(peerId); }
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception exception) { Log("traveller_arrival_failed", new { peerId, transferId, attempt, error = exception.Message }); }
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
        }
    }
}
