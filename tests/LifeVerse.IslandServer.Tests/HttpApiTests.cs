using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using LifeVerse.IslandServer.Core;
using LifeVerse.Wire;

namespace LifeVerse.IslandServer.Tests;

/// <summary>Uma API HTTP de verdade (HttpListener) com as rotas /api/v1/platform/* do backend, para exercitar o HttpIslandApi byte a byte.</summary>
public sealed class StubPlatformApi : IDisposable
{
    public sealed record Call(string Method, string Path, string? ServiceKey, string Body);

    private readonly HttpListener _listener = new();
    private readonly Task _loop;
    public readonly ConcurrentQueue<Call> Calls = new();
    public readonly Guid IslandId = Guid.NewGuid();
    public readonly Guid InstanceId = Guid.NewGuid();
    public string Url { get; }

    public StubPlatformApi()
    {
        var port = new Random().Next(20000, 40000);
        while (true)
        {
            try { Url = $"http://127.0.0.1:{port}/"; _listener.Prefixes.Add(Url); _listener.Start(); break; }
            catch (HttpListenerException) { _listener.Prefixes.Clear(); port++; }
        }
        _loop = Task.Run(Serve);
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); } catch { return; }
            _ = Task.Run(() => Handle(context));
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var body = new StreamReader(request.InputStream, Encoding.UTF8).ReadToEnd();
        var path = request.Url!.AbsolutePath;
        Calls.Enqueue(new Call(request.HttpMethod, path, request.Headers["X-LifeVerse-Service-Key"], body));
        (int Status, string Json) answer = (request.HttpMethod, path) switch
        {
            ("POST", "/api/v1/platform/servers/register") => (200, "{}"),
            ("POST", "/api/v1/platform/entry-tokens/validate") => Validate(body),
            ("PUT", var p) when p.EndsWith("/heartbeat") => (204, ""),
            ("PUT", var p) when p.EndsWith("/player-locations") => (200, "{\"kept\":1,\"release\":[]}"),
            ("DELETE", _) => (204, ""),
            _ => (404, "{}"),
        };
        if (request.HttpMethod == "GET" && path.EndsWith("/island-snapshot"))
            answer = (200, JsonSerializer.Serialize(new { islandId = IslandId, version = 7, terrainJson = "{}", environmentJson = "{}", integrityHash = "abc" }));
        context.Response.StatusCode = answer.Status;
        var bytes = Encoding.UTF8.GetBytes(answer.Json);
        context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes); context.Response.Close();
    }

    private (int, string) Validate(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.GetProperty("token").GetString() != "bilhete-http") return (401, "{}");
        // o formato do AdmissionResponse da API: camelCase
        return (200, JsonSerializer.Serialize(new
        {
            userId = Guid.NewGuid(), avatarId = Guid.NewGuid(), islandId = IslandId, sessionId = Guid.NewGuid(), tokenId = Guid.NewGuid(), protocolVersion = 11,
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(1), displayName = "Via HTTP", appearanceJson = "{}", canEditTerrain = true, spawnX = 4.0, spawnZ = 6.0, islandSize = 256,
        }));
    }

    public void Dispose() { _listener.Stop(); _listener.Close(); }
}

public class HttpApiTests
{
    [Fact]
    public async Task TheServerTalksToARealHttpApiWithTheServiceKey()
    {
        using var stub = new StubPlatformApi();
        var key = new string('k', 40);
        var environment = new Dictionary<string, string>
        {
            ["LIFEVERSE_INSTANCE_ID"] = stub.InstanceId.ToString(), ["LIFEVERSE_ISLAND_ID"] = stub.IslandId.ToString(), ["LIFEVERSE_API_URL"] = stub.Url,
            ["LIFEVERSE_SERVICE_KEY"] = key, ["LIFEVERSE_GAME_PORT"] = "0", ["LIFEVERSE_HEALTH_PORT"] = "0", ["LIFEVERSE_LOOPBACK_ONLY"] = "1",
        };
        var config = IslandServerConfig.From(name => environment.GetValueOrDefault(name));
        using var server = new Core.IslandServer(config, new HttpIslandApi(config), new IslandLog(new StringWriter()),
            new IslandServerOptions(HeartbeatSeconds: .3, LocationSeconds: .4, DisconnectDelaySeconds: .1));
        await server.StartAsync();
        var run = server.RunAsync();

        using var client = new WireClient(); var received = new List<IWireMessage>();
        client.MessageReceived += message => { lock (received) received.Add(message); };
        Assert.True(client.Connect("127.0.0.1", server.GamePort));
        async Task Pump(Func<bool> done) { for (var i = 0; i < 6000 && !done(); i++) { client.Poll(); await Task.Delay(2); } }
        await Pump(() => client.IsConnected);
        client.Send(new AuthenticateMessage("bilhete-http", WireProtocol.Version));
        await Pump(() => { lock (received) return received.OfType<AuthResultMessage>().Any(); });
        AuthResultMessage result; lock (received) result = received.OfType<AuthResultMessage>().First();
        Assert.True(result.Success, result.Code);
        SpawnPlayerMessage spawn; lock (received) spawn = received.OfType<SpawnPlayerMessage>().First();
        Assert.Equal("Via HTTP", spawn.DisplayName); Assert.Equal(4f, spawn.X); Assert.Equal(6f, spawn.Z);

        await Pump(() => stub.Calls.Any(c => c.Path.EndsWith("/heartbeat"))); // um heartbeat (espera o fato, não um tempo fixo)
        client.Disconnect();
        await Pump(() => stub.Calls.Any(c => c.Method == "PUT" && c.Path.EndsWith("/player-locations")));
        await server.StopAsync(); await run;

        var calls = stub.Calls.ToList();
        Assert.All(calls, call => Assert.Equal(key, call.ServiceKey));
        var register = calls.First(c => c.Path == "/api/v1/platform/servers/register");
        using (var body = JsonDocument.Parse(register.Body))
        {
            Assert.Equal(stub.InstanceId, body.RootElement.GetProperty("instanceId").GetGuid());
            Assert.Equal(stub.IslandId, body.RootElement.GetProperty("islandId").GetGuid());
            Assert.True(body.RootElement.TryGetProperty("hostname", out _)); Assert.True(body.RootElement.TryGetProperty("port", out _));
        }
        Assert.Contains(calls, c => c.Method == "GET" && c.Path == $"/api/v1/platform/servers/{stub.InstanceId}/island-snapshot");
        Assert.Contains(calls, c => c.Method == "PUT" && c.Path == $"/api/v1/platform/servers/{stub.InstanceId}/heartbeat" && c.Body.Contains("\"players\":") && c.Body.Contains("\"report\""));
        var locations = calls.First(c => c.Path.EndsWith("/player-locations"));
        Assert.Contains("\"userId\"", locations.Body);
        Assert.Contains(calls, c => c.Method == "DELETE" && c.Path == $"/api/v1/platform/servers/{stub.InstanceId}");
        var validate = calls.First(c => c.Path.EndsWith("/entry-tokens/validate"));
        Assert.Contains("\"islandId\"", validate.Body);
    }

    [Fact]
    public async Task ARefusedTicketIsAnInvalidTicketAndAnUnreachableApiFailsTheStart()
    {
        using var stub = new StubPlatformApi();
        var environment = new Dictionary<string, string>
        {
            ["LIFEVERSE_ISLAND_ID"] = stub.IslandId.ToString(), ["LIFEVERSE_API_URL"] = stub.Url, ["LIFEVERSE_SERVICE_KEY"] = new string('k', 40),
            ["LIFEVERSE_GAME_PORT"] = "0", ["LIFEVERSE_HEALTH_PORT"] = "0", ["LIFEVERSE_LOOPBACK_ONLY"] = "1",
        };
        var config = IslandServerConfig.From(name => environment.GetValueOrDefault(name));
        using var api = new HttpIslandApi(config);
        Assert.Null(await api.ValidateTicketAsync("errado", stub.IslandId, CancellationToken.None));

        var unreachable = environment.ToDictionary(x => x.Key, x => x.Value); unreachable["LIFEVERSE_API_URL"] = "http://127.0.0.1:9/";
        var badConfig = IslandServerConfig.From(name => unreachable.GetValueOrDefault(name));
        using var server = new Core.IslandServer(badConfig, new HttpIslandApi(badConfig), new IslandLog(new StringWriter()));
        await Assert.ThrowsAnyAsync<Exception>(() => server.StartAsync());
        Assert.False(server.Ready);
    }
}
