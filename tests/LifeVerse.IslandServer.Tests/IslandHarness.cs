using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using LifeVerse.IslandServer.Core;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Platform;
using LifeVerse.Wire;

namespace LifeVerse.IslandServer.Tests;

/// <summary>A API de mentira: guarda o que o servidor pediu e responde com bilhetes combinados no teste.</summary>
/// <summary>O HTTP cru da API de mentira: serve island-content aos scripts e guarda o que eles gravam.</summary>
public sealed class ScriptHandler(FakeApi api) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/island-content") && api.ContentJson is { } content) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(content) });
        if (path.EndsWith("/island-content")) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        if (path.EndsWith("/script-state")) { api.ScriptStateWrites.Enqueue(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()); return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent)); }
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}

public sealed class FakeApi : IIslandApi
{
    public volatile string? ContentJson;
    public readonly ConcurrentQueue<string> ScriptStateWrites = new();
    private System.Net.Http.HttpClient? _http;
    public System.Net.Http.HttpClient Http => _http ??= new System.Net.Http.HttpClient(new ScriptHandler(this)) { BaseAddress = new Uri("http://fake/") };

    public static string Content(Guid objectId, string name, float x, float z, string source, Guid? owner = null, string physics = "{}") => System.Text.Json.JsonSerializer.Serialize(new
    {
        objects = new[] { new { id = objectId, ownerUserId = owner ?? Guid.NewGuid(), name, primitiveType = "cube", physicsJson = physics, positionX = x, positionY = 0f, positionZ = z, scaleX = 1f, scaleY = 1f, scaleZ = 1f, revision = 1 } },
        scripts = new[] { new { id = Guid.NewGuid(), objectId, ownerUserId = owner ?? Guid.NewGuid(), name = "teste", source, version = 1, instructionBudget = 100000, paused = false } },
        state = new { }, grants = Array.Empty<object>(), islandName = "Teste",
    });

    public readonly ConcurrentDictionary<string, Admission> Tickets = new();
    public readonly ConcurrentQueue<IReadOnlyList<ServerCommand>> PendingCommands = new();
    public readonly ConcurrentQueue<PlayerLocation> SavedLocations = new();
    public readonly ConcurrentQueue<List<ReleasedEntry>> ReleaseNext = new();
    public readonly Guid IslandId = Guid.NewGuid();
    public volatile bool Registered, Deregistered, FailValidation;
    public int Heartbeats, Validations, SavedWorlds, ArrivalConfirmations;
    public int ArrivalStatus = 204;
    public string TerrainJson = "{}";
    public TimeSpan ValidationDelay = TimeSpan.Zero;

    public Admission Issue(string name, float spawnX = 0, float spawnZ = 0, int protocol = 11, Guid? avatar = null, float? resumeX = null, float? resumeZ = null, Guid? transfer = null, bool canEdit = false, string appearance = "{}")
    {
        var token = Guid.NewGuid().ToString("N");
        var admission = new Admission(Guid.NewGuid(), avatar ?? Guid.NewGuid(), IslandId, Guid.NewGuid(), Guid.NewGuid(), protocol, DateTimeOffset.UtcNow.AddMinutes(5),
            name, appearance, canEdit, spawnX, spawnZ, 256, resumeX, 0, resumeZ, transfer) { };
        Tickets[token] = admission;
        Tokens[admission.UserId] = token;
        return admission;
    }

    public readonly ConcurrentDictionary<Guid, string> Tokens = new();
    public string TokenOf(Admission admission) => Tokens[admission.UserId];

    public Task RegisterAsync(IslandServerConfig config, CancellationToken ct) { Registered = true; return Task.CompletedTask; }

    public async Task<Admission?> ValidateTicketAsync(string token, Guid islandId, CancellationToken ct)
    {
        Interlocked.Increment(ref Validations);
        if (ValidationDelay > TimeSpan.Zero) await Task.Delay(ValidationDelay, ct);
        if (FailValidation) throw new HttpRequestException("API unavailable");
        return Tickets.TryGetValue(token, out var admission) ? admission : null;
    }

    public Task<IslandWorldSnapshot> LoadWorldAsync(CancellationToken ct) => Task.FromResult(new IslandWorldSnapshot(IslandId, 1, TerrainJson, "{}", string.Empty));

    public Task<SaveWorldResult> SaveWorldAsync(long expectedVersion, string terrainJson, string environmentJson, string integrityHash, CancellationToken ct)
    { Interlocked.Increment(ref SavedWorlds); return Task.FromResult(new SaveWorldResult(true, false, expectedVersion + 1)); }

    public Task<IReadOnlyList<ServerCommand>> HeartbeatAsync(int players, ServerReport report, CancellationToken ct)
    {
        Interlocked.Increment(ref Heartbeats);
        return Task.FromResult(PendingCommands.TryDequeue(out var commands) ? commands : Array.Empty<ServerCommand>());
    }

    public Task<LocationAnswer?> SaveLocationsAsync(IReadOnlyList<PlayerLocation> players, CancellationToken ct)
    {
        foreach (var player in players) SavedLocations.Enqueue(player);
        return Task.FromResult<LocationAnswer?>(new LocationAnswer(players.Count, ReleaseNext.TryDequeue(out var release) ? release : null));
    }

    public Task<int> ConfirmArrivalAsync(Guid transferId, CancellationToken ct) { Interlocked.Increment(ref ArrivalConfirmations); return Task.FromResult(ArrivalStatus); }

    public readonly ConcurrentQueue<string> BuildRequests = new();
    public Func<string, (int Status, string Body)> BuildAnswer = json => (200, json);
    public Task<(int Status, string Body)> BuildOperationAsync(string json, CancellationToken ct) { BuildRequests.Enqueue(json); return Task.FromResult(BuildAnswer(json)); }

    public int AppearanceRequests;
    public volatile string AppearanceBody = "{\"appearanceJson\":\"{}\",\"displayName\":\"\"}";
    public Task<(int Status, string Body)> GetAppearanceAsync(Guid avatarId, CancellationToken ct) { Interlocked.Increment(ref AppearanceRequests); return Task.FromResult((200, AppearanceBody)); }

    public volatile string? LandJson;
    public Task<string?> LandSyncAsync(long cursor, CancellationToken ct) => Task.FromResult(LandJson);

    public readonly List<VehicleRow> Vehicles = new();
    public Task<IReadOnlyList<VehicleRow>> GetVehiclesAsync(CancellationToken ct) { lock (Vehicles) return Task.FromResult<IReadOnlyList<VehicleRow>>(Vehicles.ToList()); }

    public Task DeregisterAsync(CancellationToken ct) { Deregistered = true; return Task.CompletedTask; }

    public void Dispose() { }
}

/// <summary>Um jogador de mentira: um WireClient que guarda tudo o que recebe.</summary>
public sealed class TestClient : IDisposable
{
    public readonly WireClient Wire = new();
    public readonly List<IWireMessage> Received = new();
    public readonly List<WireDisconnectReason> Disconnects = new();
    public int PeerId = -1;
    public Admission? Admission;

    public TestClient() { Wire.MessageReceived += m => { lock (Received) Received.Add(m); }; Wire.Disconnected += reason => Disconnects.Add(reason); }

    public IReadOnlyList<T> All<T>() where T : IWireMessage { lock (Received) return Received.OfType<T>().ToList(); }
    public T? Last<T>() where T : class, IWireMessage { lock (Received) return Received.OfType<T>().LastOrDefault(); }
    public bool Authenticated => All<AuthResultMessage>().Any(x => x.Success);

    public PlayerSnapshot? Snapshot(int peerId)
    {
        foreach (var message in All<SnapshotMessage>().Reverse())
            if (SnapshotCodec.TryDecode(message.Payload, out var snapshot))
                foreach (var p in snapshot.Players) if (p.PeerId == peerId) return p;
        return null;
    }

    public (float X, float Y, float Z, uint Ack)? Position(int peerId) => Snapshot(peerId) is { } p ? (p.X, p.Y, p.Z, p.LastProcessedInput) : null;
    public void Dispose() => Wire.Dispose();
}

/// <summary>Sobe um servidor de ilha de verdade (UDP no loopback, porta livre) com a API de mentira.</summary>
public sealed class IslandHarness : IDisposable
{
    public readonly FakeApi Api = new();
    public readonly Core.IslandServer Server;
    public readonly StringWriter Logs = new();
    public readonly List<TestClient> Clients = new();
    private Task? _run;

    public IslandHarness(IIslandPhysics? physics = null, int maxPlayers = 64, float interestRadius = 100, IslandServerOptions? options = null, int ticketProtocol = 11, int idleMinutes = 0)
    {
        var environment = new Dictionary<string, string>
        {
            ["LIFEVERSE_ISLAND_ID"] = Api.IslandId.ToString(), ["LIFEVERSE_API_URL"] = "http://fake/", ["LIFEVERSE_SERVICE_KEY"] = new string('k', 32),
            ["LIFEVERSE_GAME_PORT"] = "0", ["LIFEVERSE_HEALTH_PORT"] = "0", ["LIFEVERSE_MAX_PLAYERS"] = maxPlayers.ToString(),
            ["LIFEVERSE_INTEREST_RADIUS"] = interestRadius.ToString(System.Globalization.CultureInfo.InvariantCulture), ["LIFEVERSE_LOOPBACK_ONLY"] = "1",
            ["LIFEVERSE_TICKET_PROTOCOL"] = ticketProtocol.ToString(), ["LIFEVERSE_IDLE_SHUTDOWN_MINUTES"] = idleMinutes.ToString(),
        };
        var config = IslandServerConfig.From(name => environment.GetValueOrDefault(name));
        Server = new Core.IslandServer(config, Api, new IslandLog(Logs), options ?? new IslandServerOptions(HeartbeatSeconds: .3, LocationSeconds: .5, DisconnectDelaySeconds: .15, VehicleSyncSeconds: .3, LandSyncSeconds: .3, ScriptSyncSeconds: .4));
        if (physics is not null) Server.Physics = physics;
    }

    public async Task StartAsync() { await Server.StartAsync(); _run = Server.RunAsync(); }

    public TestClient Connect()
    {
        var client = new TestClient();
        Assert.True(client.Wire.Connect("127.0.0.1", Server.GamePort));
        Clients.Add(client);
        return client;
    }

    /// <summary>Conecta, manda o bilhete e espera a resposta (AuthResult).</summary>
    public async Task<TestClient> JoinAsync(string name, float x = 0, float z = 0, Guid? avatar = null, bool canEdit = false, string appearance = "{}")
    {
        var admission = Api.Issue(name, x, z, avatar: avatar, canEdit: canEdit, appearance: appearance);
        var client = Connect(); client.Admission = admission;
        await PumpAsync(() => client.Wire.IsConnected);
        client.Wire.Send(new AuthenticateMessage(Api.TokenOf(admission), WireProtocol.Version));
        await PumpAsync(() => client.All<AuthResultMessage>().Count > 0 || client.Disconnects.Count > 0);
        if (client.Authenticated) client.PeerId = client.All<SpawnPlayerMessage>().First(s => s.AvatarId == admission.AvatarId.ToString()).PeerId;
        return client;
    }

    public async Task<bool> PumpAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            foreach (var client in Clients) client.Wire.Poll();
            if (condition()) return true;
            await Task.Delay(2);
        }
        return condition();
    }

    public Task PumpForAsync(int milliseconds) => PumpAsync(() => false, milliseconds);

    public async Task<string> HealthAsync()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", Server.HealthPort);
        var stream = tcp.GetStream();
        var buffer = new byte[4096]; var text = new StringBuilder(); int read;
        while ((read = await stream.ReadAsync(buffer)) > 0) text.Append(Encoding.UTF8.GetString(buffer, 0, read));
        return text.ToString();
    }

    public async Task StopAsync() { if (_run is not null) { await Server.StopAsync(); await _run; } }

    public void Dispose()
    {
        foreach (var client in Clients) client.Dispose();
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
        Server.Dispose();
    }
}

/// <summary>Uma física de mentira: guarda o que o servidor pediu e devolve as poses que o teste combinar.</summary>
public sealed class FakePhysics : IIslandPhysics
{
    public readonly List<IReadOnlyList<LifeVerse.IslandServer.Scripts.PhysicsBodyDefinition>> Syncs = new();
    public readonly List<BodyPose> Current = new();
    public int Steps, TerrainSets, Pushes;
    public int BodyCount => Current.Count;
    public int FixedCount => 0;
    public int MaxBodies => 200;
    public Action<Guid, Guid?, bool, float>? Contacted { get; set; }
    public void SetTerrain(LifeVerse.Shared.Terrain.TerrainMap terrain) => Interlocked.Increment(ref TerrainSets);
    public void Sync(IEnumerable<LifeVerse.IslandServer.Scripts.PhysicsBodyDefinition> definitions) { lock (Syncs) Syncs.Add(definitions.ToList()); }
    public void Step(float seconds) => Interlocked.Increment(ref Steps);
    public IReadOnlyList<BodyPose> Poses() { lock (Current) return Current.ToList(); }
    public void Push(float feetX, float feetY, float feetZ, float velocityX, float velocityZ) => Interlocked.Increment(ref Pushes);
    public bool Apply(Guid id, string action, LifeVerse.Scripting.Vec3 value) => action == "impulse";
    public LifeVerse.Scripting.Vec3? VelocityOf(Guid id) => null;
    public void Dispose() { }
}