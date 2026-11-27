using System.Diagnostics;
using LifeVerse.Shared.Networking;
using LifeVerse.Wire;
using LifeVerse.Wire.Tests;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.IslandServer.Tests;

/// <summary>
/// O spike S2: 64 jogadores numa internet ruim (atraso nos dois sentidos, variação e perda), contra o servidor de verdade. Mede quantos snapshots chegam e em quanto tempo
/// um comando volta confirmado pelo servidor. <c>LIFEVERSE_LAG_SECONDS</c> alonga a medição.
/// </summary>
public sealed class LatencyTests(ITestOutputHelper output)
{
    private sealed class Walker
    {
        public required TestClient Client;
        public int Read; public bool Sent; public int PeerId = -1;
        public readonly Dictionary<uint, double> SentAt = new(); public uint Acked; public readonly List<double> AckMs = new();
        public int Snapshots; public double LastSnapshot, LongestGap; public int Chats; public SnapshotOrder Order;
    }

    [Theory]
    [InlineData(60, 90, 5, 330)]      // volta de uns 150 ms com 5% de perda em cada sentido
    [InlineData(120, 180, 10, 520)]   // volta de uns 300 ms com 10% de perda: uma conexão péssima
    public async Task SixtyFourPlayersPlayThroughABadNetwork(int minDelay, int maxDelay, int loss, int medianLimit)
    {
        var seconds = double.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_LAG_SECONDS"), out var s) ? s : 12;
        using var island = new IslandHarness();
        await island.StartAsync();
        using var proxy = new LossyUdpProxy(island.Server.GamePort, loss, maxDelay, seed: 64, minDelayMs: minDelay);
        var walkers = new List<Walker>(); var clock = Stopwatch.StartNew();
        for (var i = 0; i < 64; i++)
        {
            var admission = island.Api.Issue("andarilho" + i, i % 8 * 2, i / 8 * 2);
            var client = new TestClient { Admission = admission }; island.Clients.Add(client);
            Assert.True(client.Wire.Connect("127.0.0.1", proxy.Port)); walkers.Add(new Walker { Client = client });
        }

        void Pump()
        {
            var now = clock.Elapsed.TotalMilliseconds;
            foreach (var w in walkers)
            {
                w.Client.Wire.Poll();
                if (!w.Sent && w.Client.Wire.IsConnected) { w.Client.Wire.Send(new AuthenticateMessage(island.Api.TokenOf(w.Client.Admission!), WireProtocol.Version)); w.Sent = true; }
                List<IWireMessage> fresh; lock (w.Client.Received) { fresh = w.Client.Received.GetRange(w.Read, w.Client.Received.Count - w.Read); w.Read = w.Client.Received.Count; }
                foreach (var message in fresh)
                {
                    if (message is SpawnPlayerMessage spawn && spawn.AvatarId == w.Client.Admission!.AvatarId.ToString()) w.PeerId = spawn.PeerId;
                    else if (message is ReceiveChatMessage) w.Chats++;
                    else if (message is SnapshotMessage snapshot && SnapshotCodec.TryDecode(snapshot.Payload, out var decoded) && w.Order.Accept(decoded.ServerTick))   // como o cliente do jogo
                        foreach (var player in decoded.Players)
                        {
                            if (player.PeerId != w.PeerId) continue;   // o próprio jogador vai em um dos pacotes de cada snapshot
                            w.Snapshots++; if (w.LastSnapshot > 0) w.LongestGap = Math.Max(w.LongestGap, now - w.LastSnapshot); w.LastSnapshot = now;
                            if (player.LastProcessedInput > w.Acked && w.SentAt.TryGetValue(player.LastProcessedInput, out var at)) { w.Acked = player.LastProcessedInput; w.AckMs.Add(now - at); }
                        }
                }
            }
        }

        while (walkers.Any(w => w.PeerId < 0) && clock.Elapsed.TotalSeconds < 30) { Pump(); await Task.Delay(2); }
        Assert.Equal(64, walkers.Count(w => w.PeerId >= 0));

        foreach (var w in walkers) { w.Snapshots = 0; w.LastSnapshot = 0; w.LongestGap = 0; }
        var start = clock.Elapsed.TotalSeconds; var lastInput = 0.0; uint sequence = 0;
        walkers[0].Client.Wire.Send(new SendChatMessage("todos devem ler"));
        while (clock.Elapsed.TotalSeconds - start < seconds)
        {
            var now = clock.Elapsed.TotalSeconds;
            if (now - lastInput >= 1.0 / 30)
            {
                lastInput = now; sequence++;
                for (var i = 0; i < walkers.Count; i++) { walkers[i].SentAt[sequence] = clock.Elapsed.TotalMilliseconds; walkers[i].Client.Wire.Send(new SubmitInputMessage(sequence, (float)Math.Cos(now * .5 + i), (float)Math.Sin(now * .5 + i), 0)); }
            }
            Pump(); await Task.Delay(1);
        }
        for (var until = clock.Elapsed.TotalSeconds + 2; clock.Elapsed.TotalSeconds < until;) { Pump(); await Task.Delay(2); }

        var acks = walkers.SelectMany(w => w.AckMs).OrderBy(v => v).ToList();
        Assert.True(acks.Count > 1000, "confirmações medidas: " + acks.Count);
        double At(double share) => acks[Math.Min(acks.Count - 1, (int)(acks.Count * share))];
        var rate = walkers.Average(w => w.Snapshots / (seconds + 2)); var worstRate = walkers.Min(w => w.Snapshots / (seconds + 2)); var gap = walkers.Max(w => w.LongestGap);
        var stats = island.Server.Stats();
        output.WriteLine($"atraso {minDelay}-{maxDelay} ms por sentido, perda {loss}%: confirmação mediana {At(.5):F0} ms, p95 {At(.95):F0} ms, p99 {At(.99):F0} ms; snapshots/s por jogador {rate:F1} (pior {worstRate:F1}); maior buraco {gap:F0} ms; descartados {proxy.Dropped}; tick médio {stats.TickAverageMs:F2} ms, p99 {stats.TickP99Ms:F2} ms, {stats.TickRate:F1} Hz");
        Assert.All(walkers, w => Assert.True(w.Client.Disconnects.Count == 0, "desconectado: " + string.Join(",", w.Client.Disconnects)));
        Assert.True(walkers.All(w => w.Chats >= 1), "a mensagem confiável chega a todos, mesmo com perda; faltou em " + walkers.Count(w => w.Chats == 0));
        Assert.True(At(.5) < medianLimit, "mediana " + At(.5)); Assert.True(At(.5) >= minDelay * 2 - 20, "a mediana não pode ser menor que a ida e volta: " + At(.5));
        Assert.True(worstRate > 14 * (100 - loss) / 100.0, "quem menos recebeu: " + worstRate);   // são 20 por segundo, menos o que se perde Assert.True(gap < 2000, "ninguém fica dois segundos sem notícias: " + gap);
        Assert.Equal(0, stats.TickFailures); Assert.DoesNotContain("rate_limit", island.Logs.ToString());
    }
}
