using System.Net.Http;
using System.Text;
using System.Text.Json;
using LifeVerse.Shared.Platform;

namespace LifeVerse.IslandServer.Core;

public sealed record Admission(Guid UserId, Guid AvatarId, Guid IslandId, Guid SessionId, Guid TokenId,
    int ProtocolVersion, DateTimeOffset ExpiresAt, string DisplayName, string AppearanceJson, bool CanEditTerrain = false,
    float SpawnX = 0, float SpawnZ = 0, int IslandSize = 256, float? ResumeX = null, float? ResumeY = null, float? ResumeZ = null, Guid? TransferId = null);

public sealed record IslandWorldSnapshot(Guid IslandId, long Version, string TerrainJson, string EnvironmentJson, string IntegrityHash);

public sealed record SaveWorldResult(bool Saved, bool Conflict, long Version);

public sealed record ReleasedEntry(Guid UserId, long ArrivedAtUnixMs);

public sealed record LocationAnswer(int Kept, List<ReleasedEntry>? Release);

public sealed record VehicleRow(Guid Id, string Name, string VehicleType, float PositionX, float PositionZ, float RotationY, float MaxSpeed, Guid OwnerUserId = default);

public sealed record VehicleFeed(List<VehicleRow>? Vehicles);

public sealed record PlayerLocation(Guid UserId, float X, float Y, float Z);

/// <summary>Tudo o que o servidor de ilha pede à API. Uma interface para que os testes usem uma API de mentira sem rede.</summary>
public interface IIslandApi : IDisposable
{
    Task RegisterAsync(IslandServerConfig config, CancellationToken ct);
    /// <summary>Valida o bilhete de entrada. Null quando a API o recusa.</summary>
    Task<Admission?> ValidateTicketAsync(string token, Guid islandId, CancellationToken ct);
    Task<IslandWorldSnapshot> LoadWorldAsync(CancellationToken ct);
    Task<SaveWorldResult> SaveWorldAsync(long expectedVersion, string terrainJson, string environmentJson, string integrityHash, CancellationToken ct);
    Task<IReadOnlyList<ServerCommand>> HeartbeatAsync(int players, ServerReport report, CancellationToken ct);
    Task<LocationAnswer?> SaveLocationsAsync(IReadOnlyList<PlayerLocation> players, CancellationToken ct);
    /// <summary>O estado HTTP da confirmação de chegada de quem viajou (404 e 409: a viagem não vale mais).</summary>
    Task<int> ConfirmArrivalAsync(Guid transferId, CancellationToken ct);
    Task<IReadOnlyList<VehicleRow>> GetVehiclesAsync(CancellationToken ct);
    Task DeregisterAsync(CancellationToken ct);
}

/// <summary>A API real, pelas rotas /api/v1/platform/* e o cabeçalho X-LifeVerse-Service-Key (as mesmas do servidor Godot).</summary>
public sealed class HttpIslandApi : IIslandApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IslandServerConfig _config;

    public HttpIslandApi(IslandServerConfig config)
    {
        _config = config;
        _http.DefaultRequestHeaders.Add("X-LifeVerse-Service-Key", config.ServiceKey);
    }

    private string Server => $"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}";
    private static StringContent Body(object value) => new(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json");

    public async Task RegisterAsync(IslandServerConfig config, CancellationToken ct)
    {
        using var response = await _http.PostAsync($"{config.ApiBaseUrl}/api/v1/platform/servers/register",
            Body(new { instanceId = config.InstanceId, islandId = config.IslandId, hostname = config.Hostname, port = config.GamePort }), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Admission?> ValidateTicketAsync(string token, Guid islandId, CancellationToken ct)
    {
        using var response = await _http.PostAsync($"{_config.ApiBaseUrl}/api/v1/platform/entry-tokens/validate", Body(new { token, islandId }), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<Admission>(await response.Content.ReadAsStringAsync().ConfigureAwait(false), Json);
    }

    public async Task<IslandWorldSnapshot> LoadWorldAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync($"{Server}/island-snapshot", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<IslandWorldSnapshot>(await response.Content.ReadAsStringAsync().ConfigureAwait(false), Json) ?? throw new InvalidOperationException("Island snapshot response was empty.");
    }

    public async Task<SaveWorldResult> SaveWorldAsync(long expectedVersion, string terrainJson, string environmentJson, string integrityHash, CancellationToken ct)
    {
        using var response = await _http.PutAsync($"{Server}/island-snapshot", Body(new { expectedVersion, terrainJson, environmentJson, integrityHash }), ct).ConfigureAwait(false);
        if ((int)response.StatusCode == 409) return new SaveWorldResult(false, true, expectedVersion);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return new SaveWorldResult(true, false, document.RootElement.TryGetProperty("version", out var version) ? version.GetInt64() : expectedVersion);
    }

    public async Task<IReadOnlyList<ServerCommand>> HeartbeatAsync(int players, ServerReport report, CancellationToken ct)
    {
        using var response = await _http.PutAsync($"{Server}/heartbeat", new StringContent(JsonSerializer.Serialize(new { status = "healthy", players, report }, ServerChannelJson.Options), Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != System.Net.HttpStatusCode.OK) return Array.Empty<ServerCommand>();
        return ServerChannelJson.ParseCommands(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    public async Task<LocationAnswer?> SaveLocationsAsync(IReadOnlyList<PlayerLocation> players, CancellationToken ct)
    {
        using var response = await _http.PutAsync($"{Server}/player-locations", Body(new { players = players.Select(p => new { userId = p.UserId, x = p.X, y = p.Y, z = p.Z }).ToArray() }), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<LocationAnswer>(await response.Content.ReadAsStringAsync().ConfigureAwait(false), Json);
    }

    public async Task<int> ConfirmArrivalAsync(Guid transferId, CancellationToken ct)
    {
        using var response = await _http.PostAsync($"{Server}/transfers/{transferId}/arrived", new StringContent(string.Empty), ct).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    public async Task<IReadOnlyList<VehicleRow>> GetVehiclesAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync($"{Server}/vehicles", ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return Array.Empty<VehicleRow>();
        return JsonSerializer.Deserialize<VehicleFeed>(await response.Content.ReadAsStringAsync().ConfigureAwait(false), Json)?.Vehicles ?? new List<VehicleRow>();
    }

    public async Task DeregisterAsync(CancellationToken ct)
    {
        using var response = await _http.DeleteAsync(Server, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}
