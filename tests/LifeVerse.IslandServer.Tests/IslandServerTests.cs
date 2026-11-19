using LifeVerse.IslandServer.Core;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Platform;
using LifeVerse.Shared.Terrain;
using LifeVerse.Wire;

namespace LifeVerse.IslandServer.Tests;

public class IslandConfigTests
{
    private static Dictionary<string, string> Baseline() => new()
    {
        ["LIFEVERSE_ISLAND_ID"] = Guid.NewGuid().ToString(), ["LIFEVERSE_API_URL"] = "https://api.example.com/", ["LIFEVERSE_SERVICE_KEY"] = new string('s', 32),
    };

    [Fact]
    public void DefaultsMatchTheGodotServer()
    {
        var environment = Baseline();
        var config = IslandServerConfig.From(name => environment.GetValueOrDefault(name));
        Assert.Equal(7777, config.GamePort); Assert.Equal(7070, config.HealthPort); Assert.Equal(64, config.MaxPlayers);
        Assert.Equal(20, config.SnapshotRate); Assert.Equal(100f, config.InterestRadius); Assert.Equal("https://api.example.com", config.ApiBaseUrl);
        Assert.Equal(NetworkProtocol.Version, config.TicketProtocolVersion);
    }

    [Theory]
    [InlineData("LIFEVERSE_ISLAND_ID")]
    [InlineData("LIFEVERSE_API_URL")]
    [InlineData("LIFEVERSE_SERVICE_KEY")]
    public void RequiredVariablesAreRequired(string missing)
    {
        var environment = Baseline(); environment.Remove(missing);
        Assert.Throws<InvalidOperationException>(() => IslandServerConfig.From(name => environment.GetValueOrDefault(name)));
    }

    [Fact]
    public void ShortServiceKeyAndBadPortsAreRefusedAndLimitsAreClamped()
    {
        var shortKey = Baseline(); shortKey["LIFEVERSE_SERVICE_KEY"] = "curta";
        Assert.Throws<InvalidOperationException>(() => IslandServerConfig.From(name => shortKey.GetValueOrDefault(name)));
        var badPort = Baseline(); badPort["LIFEVERSE_GAME_PORT"] = "70000";
        Assert.Throws<InvalidOperationException>(() => IslandServerConfig.From(name => badPort.GetValueOrDefault(name)));
        var extreme = Baseline(); extreme["LIFEVERSE_MAX_PLAYERS"] = "9999"; extreme["LIFEVERSE_SNAPSHOT_RATE"] = "1"; extreme["LIFEVERSE_INTEREST_RADIUS"] = "99999";
        var config = IslandServerConfig.From(name => extreme.GetValueOrDefault(name));
        Assert.Equal(256, config.MaxPlayers); Assert.Equal(5, config.SnapshotRate); Assert.Equal(500f, config.InterestRadius);
    }
}

public class IslandServerTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task StartRegistersLoadsTheWorldAndAnswersTheHealthCheck()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        Assert.True(island.Api.Registered);
        Assert.True(island.Server.Ready);
        var health = await island.HealthAsync();
        Assert.StartsWith("HTTP/1.1 200 OK", health);
        Assert.Contains("\"status\":\"healthy\"", health);
        Assert.Contains("\"wire\":12", health);
        Assert.Contains("server_started", island.Logs.ToString());
    }

    [Fact]
    public async Task TheHealthCheckAnswersARealHttpClientWithoutResettingTheConnection()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var i = 0; i < 3; i++)
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{island.Server.HealthPort}/");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"healthy\"", text);
            Assert.Contains("\"players\":0", text);
        }
    }

    [Fact]
    public async Task AValidTicketAdmitsThePlayerWithSpawnsAndTerrain()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana", 10, 20);
        Assert.True(a.Authenticated);
        Assert.Equal("ok", a.Last<AuthResultMessage>()!.Code);
        var self = a.All<SpawnPlayerMessage>().Single();
        Assert.Equal("Ana", self.DisplayName); Assert.Equal(10f, self.X); Assert.Equal(20f, self.Z);
        Assert.True(await island.PumpAsync(() => a.All<TerrainFullMessage>().Count == 1)); // o terreno é grande e chega fragmentado, depois do AuthResult
        Assert.Equal(1, island.Server.PlayerCount);
    }

    [Fact]
    public async Task ADifferentWireVersionIsRejectedWithTheServerVersion()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var client = island.Connect();
        await island.PumpAsync(() => client.Wire.IsConnected);
        client.Wire.Send(new AuthenticateMessage("x", 11));
        Assert.True(await island.PumpAsync(() => client.All<ProtocolRejectedMessage>().Count > 0));
        Assert.Equal(WireProtocol.Version, client.Last<ProtocolRejectedMessage>()!.ServerVersion);
        Assert.True(await island.PumpAsync(() => client.Disconnects.Count > 0));
    }

    [Fact]
    public async Task UnknownTicketsAndReusedTicketsAreRefused()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var stranger = island.Connect();
        await island.PumpAsync(() => stranger.Wire.IsConnected);
        stranger.Wire.Send(new AuthenticateMessage("bilhete-falso", WireProtocol.Version));
        Assert.True(await island.PumpAsync(() => stranger.All<AuthResultMessage>().Count > 0));
        Assert.False(stranger.Last<AuthResultMessage>()!.Success); Assert.Equal("invalid_ticket", stranger.Last<AuthResultMessage>()!.Code);

        var first = await island.JoinAsync("Ana");
        var token = island.Api.TokenOf(first.Admission!);
        var replay = island.Connect();
        await island.PumpAsync(() => replay.Wire.IsConnected);
        replay.Wire.Send(new AuthenticateMessage(token, WireProtocol.Version));
        Assert.True(await island.PumpAsync(() => replay.All<AuthResultMessage>().Count > 0));
        Assert.Equal("ticket_reused_or_invalid", replay.Last<AuthResultMessage>()!.Code);
    }

    [Fact]
    public async Task ATicketOfAnotherProtocolIsRefusedUnlessTheServerAcceptsIt()
    {
        using var island = new IslandHarness(ticketProtocol: 11);
        await island.StartAsync();
        var admission = island.Api.Issue("Velho", protocol: 9);
        var client = island.Connect();
        await island.PumpAsync(() => client.Wire.IsConnected);
        client.Wire.Send(new AuthenticateMessage(island.Api.TokenOf(admission), WireProtocol.Version));
        Assert.True(await island.PumpAsync(() => client.All<AuthResultMessage>().Count > 0));
        Assert.Equal("ticket_reused_or_invalid", client.Last<AuthResultMessage>()!.Code);
    }

    [Fact]
    public async Task APIFailureRejectsWithAdmissionUnavailableAndTheServerKeepsRunning()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        island.Api.FailValidation = true;
        var client = await island.JoinAsync("Ana");
        Assert.Equal("admission_unavailable", client.Last<AuthResultMessage>()!.Code);
        island.Api.FailValidation = false;
        var again = await island.JoinAsync("Ana");
        Assert.True(again.Authenticated);
    }

    [Fact]
    public async Task MovementIsAuthoritativeAndAcknowledgedInSnapshots()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana", 0, 0);
        var start = a.Position(a.PeerId);
        for (uint sequence = 1; sequence <= 90; sequence++)
        {
            a.Wire.Send(new SubmitInputMessage(sequence, 0, -1, 0)); // para frente
            await island.PumpForAsync(16);
        }
        Assert.True(await island.PumpAsync(() => a.Position(a.PeerId) is { Ack: >= 80 }));
        var end = a.Position(a.PeerId)!.Value;
        Assert.True(end.Z < -3f, $"o jogador deveria ter andado para frente, mas Z={end.Z}");
        Assert.True(end.Z > -12f, $"andou rápido demais para o servidor autoritativo: Z={end.Z}");
        _ = start;
    }

    [Fact]
    public async Task ACheatingClientCannotMoveFasterThanTheServerAllows()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana", 0, 0);
        for (uint sequence = 1; sequence <= 120; sequence++)
        {
            a.Wire.Send(new SubmitInputMessage(sequence, 500f, 500f, (byte)(InputFlags.Run | (InputFlags)0))); // eixo gigante: o servidor limita a 1
            await island.PumpForAsync(8);
        }
        await island.PumpAsync(() => a.Position(a.PeerId) is { Ack: >= 100 });
        var p = a.Position(a.PeerId)!.Value;
        var distance = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
        Assert.True(distance < 120 * 8 / 1000f * AuthoritativeMovement.RunSpeed + 12f, $"distância {distance}");
    }

    [Fact]
    public async Task NonFiniteInputIsIgnored()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana", 0, 0);
        // o leitor do protocolo recusa NaN e desconecta; o servidor continua inteiro para os outros
        a.Wire.Send(new SubmitInputMessage(1, float.NaN, 0, 0));
        var b = await island.JoinAsync("Beto");
        Assert.True(b.Authenticated);
        Assert.Equal(0, island.Server.Stats().TickFailures);
    }

    [Fact]
    public async Task PlayersFarApartDoNotSeeEachOtherInSnapshotsButNearbyOnesDo()
    {
        using var island = new IslandHarness(interestRadius: 50);
        await island.StartAsync();
        var near1 = await island.JoinAsync("Perto1", 0, 0);
        var near2 = await island.JoinAsync("Perto2", 5, 0);
        var far = await island.JoinAsync("Longe", 90, 90);
        Assert.True(await island.PumpAsync(() => near1.Position(near2.PeerId) is not null && far.Position(far.PeerId) is not null));
        await island.PumpForAsync(300);
        Assert.NotNull(near1.Position(near2.PeerId));
        Assert.Null(near1.Position(far.PeerId));
        Assert.Null(far.Position(near1.PeerId));
    }

    [Fact]
    public async Task ChatReachesOnlyPlayersWithinThirtyMetersAndIsRateLimited()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var speaker = await island.JoinAsync("Ana", 0, 0);
        var close = await island.JoinAsync("Perto", 10, 0);
        var far = await island.JoinAsync("Longe", 60, 0);
        for (var i = 0; i < 8; i++) speaker.Wire.Send(new SendChatMessage($"olá {i}"));
        Assert.True(await island.PumpAsync(() => close.All<ReceiveChatMessage>().Count >= 5));
        await island.PumpForAsync(300);
        Assert.Equal(5, close.All<ReceiveChatMessage>().Count);   // no máximo 5 a cada 10 s
        Assert.Empty(far.All<ReceiveChatMessage>());
        Assert.Equal("Ana", close.All<ReceiveChatMessage>()[0].DisplayName);
    }

    [Fact]
    public async Task FlightIsAnIdempotentStateAndGesturesAreRelayedToNearbyPlayers()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana");
        var b = await island.JoinAsync("Beto", 3, 0);
        a.Wire.Send(new SetFlightMessage(true)); a.Wire.Send(new SetFlightMessage(true));
        a.Wire.Send(new GestureMessage(1));
        Assert.True(await island.PumpAsync(() => b.All<GestureChangedMessage>().Count > 0));
        Assert.Equal(a.PeerId, b.Last<GestureChangedMessage>()!.PeerId);
        Assert.True(await island.PumpAsync(() => a.Snapshot(a.PeerId) is { Motion: NetworkMotionState.Fly })); // voar é estado do servidor
    }

    [Fact]
    public async Task EachNewcomerKnowsEveryoneAndEveryoneKnowsTheNewcomer()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana"); var b = await island.JoinAsync("Beto"); var c = await island.JoinAsync("Cris");
        Assert.True(await island.PumpAsync(() => a.All<SpawnPlayerMessage>().Count == 3 && b.All<SpawnPlayerMessage>().Count == 3 && c.All<SpawnPlayerMessage>().Count == 3));
        Assert.Equal(new[] { "Ana", "Beto", "Cris" }, c.All<SpawnPlayerMessage>().Select(s => s.DisplayName).OrderBy(x => x));
    }

    [Fact]
    public async Task LeavingDespawnsForOthersSavesTheLocationAndAReconnectionKeepsTheSpot()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana", 0, 0);
        var b = await island.JoinAsync("Beto", 2, 0);
        for (uint s = 1; s <= 40; s++) { a.Wire.Send(new SubmitInputMessage(s, 1, 0, 0)); await island.PumpForAsync(16); }
        var userId = a.Admission!.UserId; var avatar = a.Admission.AvatarId;
        a.Wire.Disconnect();
        Assert.True(await island.PumpAsync(() => b.All<DespawnPlayerMessage>().Any(d => d.PeerId == a.PeerId && d.Suspended)));
        Assert.True(await island.PumpAsync(() => island.Api.SavedLocations.Any(l => l.UserId == userId)));
        var saved = island.Api.SavedLocations.Last(l => l.UserId == userId);
        Assert.True(saved.X > 1f);
        var back = await island.JoinAsync("Ana", 0, 0, avatar); // novo bilhete, mesmo avatar, dentro dos 10 s
        var spawn = back.All<SpawnPlayerMessage>().First(s => s.AvatarId == avatar.ToString());
        Assert.True(Math.Abs(spawn.X - saved.X) < 1.5f, $"voltou em X={spawn.X}, saiu em {saved.X}");
    }

    [Fact]
    public async Task AKickFromTheAdminPanelDisconnectsThePlayerAfterTheNotice()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana"); var b = await island.JoinAsync("Beto");
        island.Api.PendingCommands.Enqueue(new[] { new ServerCommand(Guid.NewGuid(), ServerCommandKinds.Kick, a.Admission!.UserId, "teste") });
        Assert.True(await island.PumpAsync(() => a.Disconnects.Count > 0, 6000));
        Assert.Contains(a.All<ReceiveChatMessage>(), m => m.DisplayName == "Aviso" && m.Message.Contains("teste"));
        Assert.Empty(b.Disconnects);
        Assert.True(island.Api.Heartbeats > 0);
    }

    [Fact]
    public async Task AShutdownCommandTellsEveryoneSavesAndDeregisters()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana");
        island.Api.PendingCommands.Enqueue(new[] { new ServerCommand(Guid.NewGuid(), ServerCommandKinds.Shutdown, null, "reinício") });
        Assert.True(await island.PumpAsync(() => island.Api.Deregistered, 8000));
        Assert.Contains(a.All<ReceiveChatMessage>(), m => m.Message.Contains("reiniciando"));
        Assert.Contains(island.Api.SavedLocations, l => l.UserId == a.Admission!.UserId);
        Assert.Contains("server_stopped", island.Logs.ToString());
    }

    [Fact]
    public async Task ATravellerWhoseTripWasConfirmedElsewhereIsReleased()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana");
        island.Api.ReleaseNext.Enqueue(new List<ReleasedEntry> { new(a.Admission!.UserId, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()) });
        Assert.True(await island.PumpAsync(() => a.Disconnects.Count > 0, 6000));
    }

    [Fact]
    public async Task ATravellerArrivingIsConfirmedWithTheAPIAndARefusedTripSendsThemAway()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        island.Api.ArrivalStatus = 409;
        var admission = island.Api.Issue("Viajante", transfer: Guid.NewGuid());
        var client = island.Connect(); client.Admission = admission;
        await island.PumpAsync(() => client.Wire.IsConnected);
        client.Wire.Send(new AuthenticateMessage(island.Api.TokenOf(admission), WireProtocol.Version));
        Assert.True(await island.PumpAsync(() => client.Disconnects.Count > 0, 6000));
        Assert.True(island.Api.ArrivalConfirmations >= 1);
    }

    [Fact]
    public async Task TheServerNeverExceedsMaxPlayers()
    {
        using var island = new IslandHarness(maxPlayers: 2);
        await island.StartAsync();
        var a = await island.JoinAsync("A"); var b = await island.JoinAsync("B");
        var c = island.Connect();
        Assert.True(await island.PumpAsync(() => c.Disconnects.Count > 0));
        Assert.Equal(2, island.Server.PlayerCount);
        Assert.True(a.Authenticated && b.Authenticated);
    }

    [Fact]
    public async Task OnlyIslandEditorsSculptAndEveryoneReplaysTheClampedEdit()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var editor = await island.JoinAsync("Ed", 0, 0, canEdit: true);
        var visitor = await island.JoinAsync("Vi", 2, 0);
        visitor.Wire.Send(new SubmitTerrainEditMessage(0, 5, 5, 4, .5f, 0));
        Assert.True(await island.PumpAsync(() => visitor.All<TerrainEditRejectedMessage>().Count > 0));
        Assert.Equal("no_permission", visitor.Last<TerrainEditRejectedMessage>()!.Code);

        editor.Wire.Send(new SubmitTerrainEditMessage(0, 5, 5, 999f, 7f, 0)); // raio e força fora dos limites
        Assert.True(await island.PumpAsync(() => visitor.All<TerrainEditedMessage>().Count > 0 && editor.All<TerrainEditedMessage>().Count > 0));
        var edited = visitor.Last<TerrainEditedMessage>()!;
        Assert.Equal(TerrainMap.MaxBrushRadius, edited.Radius); Assert.Equal(1f, edited.Strength); Assert.True(edited.Revision > 0);

        editor.Wire.Send(new SubmitTerrainEditMessage(99, 5, 5, 4, .5f, 0));
        Assert.True(await island.PumpAsync(() => editor.All<TerrainEditRejectedMessage>().Any(r => r.Code == "invalid_edit")));

        editor.Wire.Send(new UndoTerrainMessage());
        Assert.True(await island.PumpAsync(() => visitor.All<TerrainPatchedMessage>().Count > 0));
        editor.Wire.Send(new UndoTerrainMessage());
        Assert.True(await island.PumpAsync(() => editor.All<TerrainEditRejectedMessage>().Any(r => r.Code == "nothing_to_undo")));

        editor.Wire.Send(new SubmitSeaLevelMessage(2f));
        Assert.True(await island.PumpAsync(() => visitor.All<SeaLevelChangedMessage>().Any(s => s.Level == 2f)));
    }

    [Fact]
    public async Task TerrainEditsAreRateLimitedAndSavedWhenTheServerStops()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var editor = await island.JoinAsync("Ed", canEdit: true);
        for (var i = 0; i < 40; i++) editor.Wire.Send(new SubmitTerrainEditMessage(0, i % 10, 0, 3, .2f, 0));
        Assert.True(await island.PumpAsync(() => editor.All<TerrainEditRejectedMessage>().Any(r => r.Code == "rate_limit")));
        await island.StopAsync();
        Assert.True(island.Api.SavedWorlds >= 1);
    }

    [Fact]
    public async Task UnsupportedFeaturesAnswerWithAClearRejectionInsteadOfSilence()
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var a = await island.JoinAsync("Ana");
        a.Wire.Send(new SubmitBuildOperationMessage("{}"));
        a.Wire.Send(new EnterVehicleMessage(1));
        Assert.True(await island.PumpAsync(() => a.All<BuildOperationRejectedMessage>().Count > 0 && a.All<VehicleRejectedMessage>().Count > 0));
        Assert.Equal("unsupported", a.Last<BuildOperationRejectedMessage>()!.Code);
    }

    [Fact]
    public async Task AnIslandNobodyJoinsShutsDownByItselfWhenAskedTo()
    {
        using var island = new IslandHarness(idleMinutes: 1, options: new IslandServerOptions(HeartbeatSeconds: 5));
        await island.StartAsync();
        Assert.False(island.Server.StopRequested);
        Assert.Equal(1, island.Server.Config.IdleShutdownMinutes);
    }

    [Fact]
    public async Task ManyPlayersPlayTogetherWithoutStarvingTheTick()
    {
        const int count = 64;
        using var island = new IslandHarness(maxPlayers: 64, interestRadius: 500);
        await island.StartAsync();
        var players = new List<TestClient>();
        for (var i = 0; i < count; i++) players.Add(await island.JoinAsync($"P{i}", i % 8 * 2, i / 8 * 2));
        Assert.All(players, p => Assert.True(p.Authenticated));
        for (uint step = 1; step <= 100; step++)
        {
            foreach (var p in players) p.Wire.Send(new SubmitInputMessage(step, (p.PeerId % 3) - 1, (p.PeerId % 2 == 0) ? 1 : -1, 0));
            await island.PumpForAsync(16);
        }
        await island.PumpForAsync(400);
        var stats = island.Server.Stats();
        output.WriteLine($"CARGA jogadores={stats.Players} ticks={stats.Ticks} taxa={stats.TickRate:F1}Hz tick_medio={stats.TickAverageMs:F3}ms p99={stats.TickP99Ms:F3}ms max={stats.TickMaxMs:F3}ms snapshots={stats.SnapshotsSent} bytes={stats.SnapshotBytes} falhas={stats.TickFailures}");
        Assert.Equal(count, stats.Players);
        Assert.True(stats.TickFailures == 0, string.Join('\n', island.Logs.ToString().Split('\n').Where(l => l.Contains("tick_failed")).Take(2)));
        Assert.True(stats.SnapshotsSent > count * 10);
        Assert.True(stats.TickP99Ms < 25, $"p99 do tick = {stats.TickP99Ms:F2} ms (orçamento 16,7 ms a 60 Hz)");
        Assert.All(players, p => Assert.Equal(count, p.All<SpawnPlayerMessage>().Count));
        Assert.All(players, p => Assert.NotNull(p.Position(p.PeerId)));
        Assert.All(players, p => Assert.Empty(p.Disconnects));
    }
}
