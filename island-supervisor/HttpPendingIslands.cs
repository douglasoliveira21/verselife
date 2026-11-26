using System.Net.Http.Json;
using System.Text.Json;

namespace LifeVerse.IslandSupervisor;

/// <summary>Reads the islands waiting for a server from the API, authenticated with the platform service key: only the islands of this supervisor's engine ("godot" or "unity").</summary>
public sealed class HttpPendingIslands(HttpClient http, string apiBaseUrl, string engine = "godot") : IPendingIslands
{
    public async Task<IReadOnlyList<PendingIsland>> GetAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync($"{apiBaseUrl.TrimEnd('/')}/api/v1/platform/supervisor/pending?engine={Uri.EscapeDataString(engine)}", ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.GetProperty("islands").EnumerateArray()
            .Select(island => new PendingIsland(island.GetProperty("id").GetGuid(), island.GetProperty("name").GetString() ?? "island")).ToList();
    }
}
