using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LifeVerse.IslandServer.Core;
using LifeVerse.Shared.Platform;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// An island API that lives in memory: accepts the tickets it was given, serves an empty world and remembers nothing. It lets an island server run inside the Unity
    /// process (the self-checks, a local test island) without the real API.
    /// </summary>
    public sealed class LocalIslandApi : IIslandApi
    {
        private sealed class NotFound : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private readonly Dictionary<string, Admission> _tickets = new Dictionary<string, Admission>();
        private readonly Guid _island = Guid.NewGuid();
        private HttpClient _http;
        public Guid IslandId => _island;
        public string TerrainJson = "{}";
        public HttpClient Http => _http ??= new HttpClient(new NotFound()) { BaseAddress = new Uri("http://local/") };

        /// <summary>Accepts the token as a ticket for a player with that name (and the right to sculpt the island when <paramref name="canEdit"/>).</summary>
        public Admission Issue(string token, bool canEdit, float spawnX = 0, float spawnZ = 0, string appearanceJson = "{}", Guid? userId = null)
        {
            var admission = new Admission(userId ?? Guid.NewGuid(), Guid.NewGuid(), _island, Guid.NewGuid(), Guid.NewGuid(), 11, DateTimeOffset.UtcNow.AddHours(1), token, appearanceJson, canEdit, spawnX, spawnZ, 256, null, null, null, null);
            lock (_tickets) _tickets[token] = admission;
            return admission;
        }

        public Task RegisterAsync(IslandServerConfig config, CancellationToken ct) => Task.CompletedTask;
        public Task<Admission> ValidateTicketAsync(string token, Guid islandId, CancellationToken ct)
        {
            lock (_tickets)
            {
                if (!_tickets.TryGetValue(token, out var admission)) return Task.FromResult<Admission>(null);
                _tickets[token] = admission with { TokenId = Guid.NewGuid() }; // a ticket is for one entry; the same name can come back
                return Task.FromResult(admission with { TokenId = Guid.NewGuid() });
            }
        }
        public Task<IslandWorldSnapshot> LoadWorldAsync(CancellationToken ct) => Task.FromResult(new IslandWorldSnapshot(_island, 1, TerrainJson, "{}", ""));
        public Task<SaveWorldResult> SaveWorldAsync(long expectedVersion, string terrainJson, string environmentJson, string integrityHash, CancellationToken ct) => Task.FromResult(new SaveWorldResult(true, false, expectedVersion + 1));
        public Task<IReadOnlyList<ServerCommand>> HeartbeatAsync(int players, ServerReport report, CancellationToken ct) => Task.FromResult<IReadOnlyList<ServerCommand>>(Array.Empty<ServerCommand>());
        public Task<LocationAnswer> SaveLocationsAsync(IReadOnlyList<PlayerLocation> players, CancellationToken ct) => Task.FromResult(new LocationAnswer(players.Count, null));
        public Task<int> ConfirmArrivalAsync(Guid transferId, CancellationToken ct) => Task.FromResult(204);
        /// <summary>The vehicles this island has (the tests put one near the spawn point).</summary>
        public readonly List<VehicleRow> Vehicles = new List<VehicleRow>();
        public Task<IReadOnlyList<VehicleRow>> GetVehiclesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<VehicleRow>>(Vehicles.ToArray());
                /// <summary>Accepts create, update and delete the way the real API answers them: the object as the island will see it ("objectState"), or the id of what went away.</summary>
        public Task<(int Status, string Body)> BuildOperationAsync(string json, CancellationToken ct)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json); var root = document.RootElement; var operation = root.GetProperty("operation").GetString();
            if (operation == "delete") return Task.FromResult((200, "{\"operation\":\"delete\",\"objectId\":\"" + root.GetProperty("objectId").GetGuid() + "\"}"));
            if (operation != "create" && operation != "update") return Task.FromResult((404, "{}"));
            var id = operation == "create" ? Guid.NewGuid() : root.GetProperty("objectId").GetGuid(); var fields = new Dictionary<string, object>();
            foreach (var property in root.GetProperty("object").EnumerateObject()) fields[property.Name] = property.Value.ValueKind == System.Text.Json.JsonValueKind.Number ? (object)property.Value.GetDouble() : property.Value.ValueKind == System.Text.Json.JsonValueKind.String ? property.Value.GetString() : null;
            fields["id"] = id; fields["ownerUserId"] = root.GetProperty("userId").GetGuid(); fields["creatorUserId"] = root.GetProperty("userId").GetGuid(); fields["revision"] = root.TryGetProperty("expectedRevision", out var seen) && seen.ValueKind == System.Text.Json.JsonValueKind.Number ? seen.GetInt32() + 1 : 1;
            return Task.FromResult((200, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["operation"] = operation, ["objectId"] = id, ["objectState"] = fields })));
        }
        public Task<string> LandSyncAsync(long cursor, CancellationToken ct) => Task.FromResult<string>(null);
        public Task<(int Status, string Body)> GetAppearanceAsync(Guid avatarId, CancellationToken ct) => Task.FromResult((404, "{}"));
        public Task DeregisterAsync(CancellationToken ct) => Task.CompletedTask;
        public void Dispose() { _http?.Dispose(); }
    }
}
