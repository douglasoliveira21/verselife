using System.Text.Json;
using LifeVerse.IslandServer.Core;
using LifeVerse.Wire;
using Xunit;

namespace LifeVerse.IslandServer.Tests;

/// <summary>Um jogador hostil: quem inunda o servidor é desconectado, e mensagens válidas com valores absurdos não derrubam nem um passo da simulação (R12).</summary>
public sealed class AbuseTests
{
    private static IslandServerOptions Options(WireRateLimit? limit) => new(HeartbeatSeconds: .3, LocationSeconds: .5, DisconnectDelaySeconds: .15, VehicleSyncSeconds: .3, LandSyncSeconds: .3, ScriptSyncSeconds: .4, RateLimit: limit);

    [Fact]
    public async Task AFloodingPlayerIsDisconnectedAndTheIslandGoesOn()
    {
        using var island = new IslandHarness(options: Options(new WireRateLimit(MessagesPerSecond: 100, BurstMessages: 200, DropsBeforeDisconnect: 100)));
        await island.StartAsync();
        var calm = await island.JoinAsync("calma"); var loud = await island.JoinAsync("barulho", 3);
        for (var i = 0; i < 3000; i++) loud.Wire.Send(new TouchObjectMessage("nada-" + i));
        Assert.True(await island.PumpAsync(() => loud.Disconnects.Count > 0), "quem inunda é desconectado");
        Assert.Contains("rate_limit", island.Logs.ToString());
        calm.Wire.Send(new SendChatMessage("ainda estou aqui"));
        Assert.True(await island.PumpAsync(() => calm.All<ReceiveChatMessage>().Any(m => m.Message == "ainda estou aqui")), "o outro jogador continua");
        Assert.True(await island.PumpAsync(() => island.Server.PlayerCount == 1)); Assert.Equal(0, island.Server.Stats().TickFailures);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("[1,2]")]
    [InlineData("\"hud\"")]
    [InlineData("{\"touch_end\":true,\"objectId\":7}")]
    [InlineData("{\"id\":{}}")]
    [InlineData("{\"debug\":true,\"script\":3}")]
    [InlineData("{\"media_finished\":true,\"key\":1,\"playId\":2}")]
    [InlineData("{\"sound_finished\":true,\"objectId\":false}")]
    public async Task AScriptReplyOfTheWrongShapeDoesNothing(string json)
    {
        using var island = new IslandHarness();
        await island.StartAsync();
        var player = await island.JoinAsync("estranho");
        player.Wire.Send(new ScriptDialogResponseMessage(json)); player.Wire.Send(new SendChatMessage("depois"));
        Assert.True(await island.PumpAsync(() => player.All<ReceiveChatMessage>().Any(m => m.Message == "depois")));
        Assert.Equal(0, island.Server.Stats().TickFailures); Assert.DoesNotContain("message_failed", island.Logs.ToString());
    }

    [Fact]
    public async Task ValidMessagesWithAbsurdValuesNeverBreakAStep()
    {
        var open = new WireRateLimit(MessagesPerSecond: 1e9, BurstMessages: 1e9, BytesPerSecond: 1e12, BurstBytes: 1e12);
        using var island = new IslandHarness(options: Options(open));
        await island.StartAsync();
        var seed = int.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_FUZZ_SEED"), out var given) ? given : 20261127;
        var count = int.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_FUZZ_MESSAGES"), out var wanted) ? wanted : 6000;
        var random = new Random(seed);
        var hostile = await island.JoinAsync("hostil", canEdit: true); var watcher = await island.JoinAsync("olheiro", 2);
        for (var i = 0; i < count; i++)
        {
            if (hostile.Disconnects.Count > 0) { hostile = await island.JoinAsync("hostil" + i, canEdit: true); }
            hostile.Wire.Send(Random(random, hostile.PeerId, watcher.PeerId));
            if (i % 50 == 49) await island.PumpForAsync(10);
        }
        await island.PumpForAsync(500);
        Assert.True(island.Server.Stats().TickFailures == 0, "um passo falhou (semente " + seed + "): " + Failures(island.Logs.ToString()));
        Assert.True(!island.Logs.ToString().Contains("message_failed"), "uma mensagem quebrou o tratamento dela (semente " + seed + "): " + Failures(island.Logs.ToString()));
        // e a ilha continua de pé para quem chega
        var late = await island.JoinAsync("depois", 4);
        Assert.True(late.Authenticated);
        Assert.True(await island.PumpAsync(() => late.Position(late.PeerId) is not null), "quem chega depois recebe o mundo");
    }

    private static string Failures(string logs) => string.Join("\n", logs.Split('\n').Where(l => l.Contains("tick_failed") || l.Contains("message_failed")).Take(3));

    private static IWireMessage Random(Random r, int self, int other)
    {
        float F() => r.Next(8) switch { 0 => 0f, 1 => float.MaxValue, 2 => float.MinValue, 3 => -1e9f, 4 => float.Epsilon, 5 => (float)(r.NextDouble() * 2 - 1), 6 => (float)(r.NextDouble() * 2000 - 1000), _ => r.Next(-300, 300) };
        int I() => r.Next(6) switch { 0 => int.MaxValue, 1 => int.MinValue, 2 => -1, 3 => 0, 4 => self, _ => r.Next(-5, 40) };
        string Word() => r.Next(9) switch
        {
            0 => "", 1 => new string('a', r.Next(1, 60)), 2 => "{}", 3 => "[", 4 => "null", 5 => Guid.NewGuid().ToString(), 6 => "\u0000\u0001 ✓ <b>", 7 => other.ToString(), _ => "-1",
        };
        string Json()
        {
            var ops = new[] { "create", "update", "delete", "lock", "unlock", "link", "duplicate", "", "script_debug", "script_response" };
            object? Value(int depth) => r.Next(depth > 2 ? 5 : 7) switch { 0 => null, 1 => r.Next(2) == 0, 2 => F(), 3 => Word(), 4 => long.MaxValue, 5 => new[] { Value(depth + 1), Value(depth + 1) }, _ => new Dictionary<string, object?> { [Word()] = Value(depth + 1) } };
            if (r.Next(4) == 0) return r.Next(3) switch { 0 => new string('[', r.Next(1, 3000)), 1 => Word(), _ => JsonSerializer.Serialize(Value(0)) };
            var fields = new Dictionary<string, object?> { ["operation"] = ops[r.Next(ops.Length)] };
            foreach (var key in new[] { "object", "objectId", "objects", "expectedRevision", "ids", "script", "hud", "action", "buttons", "text" }) if (r.Next(3) == 0) fields[key] = Value(0);
            return JsonSerializer.Serialize(fields);
        }
        string Cut(string text, int max) => text.Length > max ? text.Substring(0, max) : text;
        byte[] Bytes(int max) { var data = new byte[r.Next(0, max)]; r.NextBytes(data); return data; }
        return r.Next(21) switch
        {
            0 => new SendChatMessage(Cut(Word(), 200)),
            1 => new SetFlightMessage(r.Next(2) == 0),
            2 => new SubmitBuildOperationMessage(Cut(Json(), 60_000)),
            3 => new SubmitTerrainEditMessage(I(), F(), F(), F(), F(), I()),
            4 => new SubmitTerrainImportMessage(Bytes(r.Next(20) == 0 ? 290_000 : 400)),
            5 => new SubmitSeaLevelMessage(F()),
            6 => new UndoTerrainMessage(),
            7 => new RedoTerrainMessage(),
            8 => new RequestTerrainMessage(),
            9 => new RefreshAppearanceMessage(),
            10 => new AttachmentLiveMessage(Cut(Word(), 60), Enumerable.Range(0, r.Next(0, 16)).Select(_ => F()).ToArray()),
            11 => new RequestTeleportMessage(Cut(Word(), 60), F(), F()),
            12 => new TouchObjectMessage(Cut(Word(), 60)),
            13 => new ScriptDialogResponseMessage(Cut(Json(), 16_000)),
            14 => new EnterVehicleMessage(I()),
            15 => new ExitVehicleMessage(),
            16 => new GestureMessage(I()),
            17 => new SubmitInputMessage((uint)r.Next(), F(), F(), (byte)r.Next(256)),
            18 => new SubmitVehicleInputMessage(F(), F(), r.Next(2) == 0),
            19 => new HeadLookMessage(F()),
            _ => new SubmitVoiceMessage((uint)r.Next(), Bytes(400)),
        };
    }
}
