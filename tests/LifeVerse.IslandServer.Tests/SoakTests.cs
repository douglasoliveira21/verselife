using System.Diagnostics;
using LifeVerse.Shared.Networking;
using LifeVerse.Wire;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.IslandServer.Tests;

/// <summary>
/// A ilha ligada por muito tempo: gente andando, conversando, entrando e saindo sem parar. O que se procura é o que só o tempo mostra: memória que cresce, jogadores que
/// ficam presos depois de sair, passos que falham ou que vão ficando lentos. Sem <c>LIFEVERSE_SOAK_SECONDS</c> é uma volta curta; com ele, dura o que for pedido.
/// </summary>
public sealed class SoakTests(ITestOutputHelper output)
{
    private const int Steady = 40;

    private sealed class Walker { public required TestClient Client; public bool Sent; public uint Sequence; public bool In; }

    [Fact]
    public async Task PeopleComeAndGoForALongTimeAndNothingPilesUp()
    {
        var seconds = double.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_SOAK_SECONDS"), out var asked) ? asked : 20;
        using var island = new IslandHarness();
        island.Api.ContentJson = FakeApi.Content(Guid.NewGuid(), "relogio", 4, 4, "local n = 0\nfunction on_start() timer.every(1, function() n = n + 1 object.state_set('voltas', n) end) end");
        await island.StartAsync();
        var walkers = new List<Walker>(); var clock = Stopwatch.StartNew(); var joined = 0; var left = 0; var snapshots = 0L; var chats = 0L; var saves = 0; var random = new Random(7);

        Walker Arrive()
        {
            var admission = island.Api.Issue("visita" + joined, random.Next(-30, 30), random.Next(-30, 30));
            var client = new TestClient { Admission = admission }; Assert.True(client.Wire.Connect("127.0.0.1", island.Server.GamePort)); joined++;
            return new Walker { Client = client };
        }

        void Leave(Walker w)
        {
            island.Api.Tickets.TryRemove(island.Api.TokenOf(w.Client.Admission!), out _); island.Api.Tokens.TryRemove(w.Client.Admission!.UserId, out _);
            w.Client.Wire.Disconnect(); w.Client.Dispose(); left++;
        }

        void Pump()
        {
            foreach (var w in walkers)
            {
                w.Client.Wire.Poll();
                if (!w.Sent && w.Client.Wire.IsConnected) { w.Client.Wire.Send(new AuthenticateMessage(island.Api.TokenOf(w.Client.Admission!), WireProtocol.Version)); w.Sent = true; }
                lock (w.Client.Received)
                {
                    foreach (var message in w.Client.Received) { if (message is AuthResultMessage { Success: true }) w.In = true; else if (message is SnapshotMessage) snapshots++; else if (message is ReceiveChatMessage) chats++; }
                    w.Client.Received.Clear();   // senão a memória do próprio teste cresceria
                }
            }
            while (island.Api.SavedLocations.TryDequeue(out _)) { }
            while (island.Api.ScriptStateWrites.TryDequeue(out var write)) if (write.Contains("voltas")) saves++;
        }

        long Memory() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(false) - island.Logs.GetStringBuilder().Length * sizeof(char); }

        for (var i = 0; i < Steady; i++) walkers.Add(Arrive());
        var samples = new List<(double At, long Bytes, double TickMs)>(); double lastInput = 0, lastChurn = 0, lastChat = 0, lastSample = 0; var warm = Math.Min(60, seconds * .25);
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            var now = clock.Elapsed.TotalSeconds;
            if (now - lastInput >= .05)
            {
                lastInput = now;
                for (var i = 0; i < walkers.Count; i++) if (walkers[i].In) walkers[i].Client.Wire.Send(new SubmitInputMessage(++walkers[i].Sequence, (float)Math.Cos(now * .3 + i), (float)Math.Sin(now * .3 + i), 0));
            }
            if (now - lastChat >= .7 && walkers.Count(w => w.In) > 0) { lastChat = now; var talker = walkers.Where(w => w.In).ElementAt(random.Next(walkers.Count(w => w.In))); talker.Client.Wire.Send(new SendChatMessage("oi " + joined)); }
            if (now - lastChurn >= .5) { lastChurn = now; var gone = walkers[random.Next(walkers.Count)]; walkers.Remove(gone); Leave(gone); walkers.Add(Arrive()); }
            if (now >= warm && now - lastSample >= Math.Max(2, seconds / 120)) { lastSample = now; samples.Add((now, Memory(), island.Server.Stats().TickAverageMs)); }
            Pump(); await Task.Delay(2);
        }

        // quem ficou está dentro; quem saiu não sobrou
        for (var until = clock.Elapsed.TotalSeconds + 3; clock.Elapsed.TotalSeconds < until;) { Pump(); await Task.Delay(2); }
        var during = island.Server.Stats();
        Assert.Equal(Steady, walkers.Count(w => w.In)); Assert.Equal(Steady, during.Players);
        foreach (var w in walkers) Leave(w); walkers.Clear();
        for (var until = clock.Elapsed.TotalSeconds + 3; clock.Elapsed.TotalSeconds < until && island.Server.Stats().Players > 0;) await Task.Delay(50);

        var stats = island.Server.Stats();
        var quarter = Math.Max(1, samples.Count / 4); double Mb(IEnumerable<(double At, long Bytes, double TickMs)> part) => part.Average(x => x.Bytes) / 1048576.0;
        var early = Mb(samples.Take(quarter)); var late = Mb(samples.TakeLast(quarter));
        output.WriteLine($"{seconds:F0} s: {joined} entradas, {left} saídas, {snapshots} snapshots e {chats} conversas recebidos, {saves} gravações do script; passo médio {samples.First().TickMs:F2} -> {samples.Last().TickMs:F2} ms, p99 {during.TickP99Ms:F2} ms, máximo {during.TickMaxMs:F1} ms, {during.TickRate:F1}/s; memória {early:F1} -> {late:F1} MB ({samples.Count} amostras)");
        var broken = island.Logs.ToString().Split('\n').Where(line => line.Contains("tick_failed") || line.Contains("message_failed")).ToList();
        Assert.True(stats.TickFailures == 0 && broken.Count == 0, $"{stats.TickFailures} passo(s) com erro; os primeiros:\n" + string.Join("\n", broken.Take(4)));
        Assert.Equal(0, stats.Players); Assert.DoesNotContain("rate_limit", island.Logs.ToString());
        Assert.True(saves > 0, "o script da ilha rodou e gravou o estado");
        Assert.True(snapshots > seconds * Steady * 5, "quem está na ilha segue recebendo: " + snapshots);
        Assert.True(late < early * 1.25 + 8, $"a memória não pode crescer com o tempo: {early:F1} -> {late:F1} MB");
        await island.StopAsync();
    }
}
