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
            if (!response.IsSuccessStatusCode) Log("player_locations_refused", new { status = (int)response.StatusCode });
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
