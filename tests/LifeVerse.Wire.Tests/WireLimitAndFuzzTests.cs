using System.Diagnostics;
using LifeVerse.Wire;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.Wire.Tests;

/// <summary>O limite de envio por jogador e o fuzz por mutação do leitor do protocolo (R12).</summary>
public sealed class WireLimitAndFuzzTests(ITestOutputHelper output)
{
    private static readonly WireRateLimit Small = new(MessagesPerSecond: 10, BurstMessages: 20, BytesPerSecond: 1000, BurstBytes: 2000, DropsBeforeDisconnect: 5, WindowSeconds: 10);

    [Fact]
    public void ABurstIsAcceptedThenTheRestIsDroppedAndTheBucketRefills()
    {
        var budget = new WireBudget(Small, 0);
        for (var i = 0; i < 20; i++) Assert.Equal(WireVerdict.Accept, budget.Take(10, 0));
        Assert.Equal(WireVerdict.Drop, budget.Take(10, 0));
        Assert.Equal(WireVerdict.Accept, budget.Take(10, .15));      // 0,15 s devolve uma mensagem e meia
        Assert.Equal(WireVerdict.Drop, budget.Take(10, .15));
        for (var i = 0; i < 20; i++) Assert.Equal(WireVerdict.Accept, budget.Take(10, 100));   // o balde nunca passa da rajada
        Assert.Equal(WireVerdict.Drop, budget.Take(10, 100));
        Assert.Equal(3, budget.Dropped);
    }

    [Fact]
    public void WhoKeepsFloodingIsCutAndNothingElseIsAccepted()
    {
        var budget = new WireBudget(Small, 0);
        for (var i = 0; i < 20; i++) budget.Take(1, 0);
        for (var i = 0; i < 5; i++) Assert.Equal(WireVerdict.Drop, budget.Take(1, 0));
        Assert.Equal(WireVerdict.Disconnect, budget.Take(1, 0));
        Assert.Equal(WireVerdict.Drop, budget.Take(1, 1000));        // depois de cortado, nem com o balde cheio
    }

    [Fact]
    public void DropsAreForgivenInALaterWindow()
    {
        var budget = new WireBudget(Small, 0);
        for (var round = 0; round < 6; round++)
        {
            var now = round * 11.0;
            for (var i = 0; i < 20; i++) Assert.Equal(WireVerdict.Accept, budget.Take(1, now));
            for (var i = 0; i < 5; i++) Assert.Equal(WireVerdict.Drop, budget.Take(1, now));
        }
    }

    [Fact]
    public void BytesAreCountedToo()
    {
        var budget = new WireBudget(Small, 0);
        Assert.Equal(WireVerdict.Accept, budget.Take(1500, 0));
        Assert.Equal(WireVerdict.Drop, budget.Take(600, 0));
        Assert.Equal(WireVerdict.Accept, budget.Take(500, 0));
        Assert.Equal(WireVerdict.Accept, budget.Take(600, .7));      // 0,7 s devolvem 700 bytes
    }

    [Fact]
    public void TheDefaultsLeaveRoomForTheBusiestHonestPlayer()
    {
        // 60 entradas, 50 pacotes de voz, 12 operações de construção de 4 KB e o resto, por 60 segundos, mais a rajada de uma travada de 5 segundos
        var budget = new WireBudget(new WireRateLimit(), 0); var refused = 0;
        for (var second = 0; second < 60; second++)
        {
            for (var step = 0; step < 130; step++) if (budget.Take(step % 11 == 0 ? 4096 : 200, second + step / 130.0) != WireVerdict.Accept) refused++;
            if (second == 30) for (var i = 0; i < 650; i++) if (budget.Take(200, second + .999) != WireVerdict.Accept) refused++;
        }
        Assert.Equal(0, refused);
        var terrain = new WireBudget(new WireRateLimit(), 0); Assert.Equal(WireVerdict.Accept, terrain.Take(WireLimits.TerrainImport, 0));
    }

    [Fact]
    public void AFloodingPlayerIsDisconnectedOverTheNetworkAndOthersKeepPlaying()
    {
        using var server = new WireServer(4, 10, new WireRateLimit(MessagesPerSecond: 50, BurstMessages: 100, DropsBeforeDisconnect: 50));
        Assert.True(server.Start(0, loopbackOnly: true));
        var rejected = new List<(int Peer, string Reason)>(); var received = new Dictionary<int, int>();
        server.PeerRejected += (peer, reason) => rejected.Add((peer, reason));
        server.MessageReceived += (peer, message) => { if (message is AuthenticateMessage) server.MarkAuthenticated(peer); else received[peer] = received.GetValueOrDefault(peer) + 1; };
        using var calm = new WireClient(); using var loud = new WireClient(); var cut = false; loud.Disconnected += _ => cut = true;
        calm.Connect("127.0.0.1", server.Port); loud.Connect("127.0.0.1", server.Port);
        void Pump(Func<bool> done, string what) { var watch = Stopwatch.StartNew(); while (!done()) { Assert.True(watch.ElapsedMilliseconds < 8000, what); server.Poll(); calm.Poll(); loud.Poll(); Thread.Sleep(1); } }
        Pump(() => calm.IsConnected && loud.IsConnected, "conexão");
        calm.Send(new AuthenticateMessage("a", WireProtocol.Version)); loud.Send(new AuthenticateMessage("b", WireProtocol.Version));
        Pump(() => server.PeerCount == 2, "autenticação");
        for (var i = 0; i < 2000; i++) loud.Send(new SendChatMessage("x" + i));
        Pump(() => cut, "o inundador ser desconectado");
        for (var i = 0; i < 20; i++) calm.Send(new SendChatMessage("calmo " + i));
        Pump(() => received.Values.Any(v => v == 20), "o outro jogador continuar");
        Assert.Single(rejected); Assert.Equal("rate_limit", rejected[0].Reason);
        Assert.True(received.Values.Max() < 200, "só a rajada passa: " + received.Values.Max()); Assert.True(server.Throttled >= 50); Assert.True(calm.IsConnected);
    }

    /// <summary>
    /// Fuzz por mutação a partir dos 44 quadros válidos: bits trocados, cortes, enxertos, pedaços repetidos, tamanhos adulterados. O leitor nunca lança, e tudo o que ele aceita
    /// se escreve de volta exatamente como chegou (há uma escrita só para cada mensagem). Dura poucos segundos nos testes; para uma sessão longa:
    /// <c>LIFEVERSE_FUZZ_SECONDS=3600 dotnet test tests/LifeVerse.Wire.Tests --filter MutatedFrames</c>.
    /// </summary>
    [Fact]
    public void MutatedFramesNeverThrowAndWhatIsAcceptedIsWrittenBackTheSame()
    {
        var seconds = double.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_FUZZ_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 3;
        var seed = int.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_FUZZ_SEED"), out var given) ? given : 20261127;
        var random = new Random(seed); var corpus = WireCodecTests.Samples().Select(WireCodec.Encode).ToList(); var watch = Stopwatch.StartNew();
        long tries = 0, accepted = 0;
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            var frame = Mutate(random, corpus);
            foreach (var direction in new[] { WireDirection.ClientToServer, WireDirection.ServerToClient })
            {
                WireDecodeResult result;
                try { result = WireCodec.Decode(frame, direction); }
                catch (Exception exception) { throw new Xunit.Sdk.XunitException($"lançou {exception.GetType().Name} (semente {seed}): {Convert.ToHexString(frame)}"); }
                tries++;
                if (!result.Ok) continue;
                accepted++;
                var back = WireCodec.Encode(result.Message!);
                Assert.True(back.AsSpan().SequenceEqual(frame), $"aceitou um quadro que se escreve diferente (semente {seed}): {Convert.ToHexString(frame)} → {Convert.ToHexString(back)}");
                if (corpus.Count < 4000 && random.Next(50) == 0) corpus.Add(frame);   // o que passou vira ponto de partida de novas mutações
            }
        }
        output.WriteLine($"{tries} leituras, {accepted} aceitas, {seconds} s, semente {seed}");
        Assert.True(accepted > 100 && tries > 10_000, $"o fuzz precisa exercitar os dois caminhos: {tries} {accepted}");
    }

    private static byte[] Mutate(Random random, List<byte[]> corpus)
    {
        var frame = corpus[random.Next(corpus.Count)].ToList();
        for (var change = random.Next(1, 5); change > 0; change--)
        {
            var at = frame.Count == 0 ? 0 : random.Next(frame.Count);
            switch (random.Next(8))
            {
                case 0: if (frame.Count > 0) frame[at] ^= (byte)(1 << random.Next(8)); break;
                case 1: if (frame.Count > 0) frame[at] = new byte[] { 0, 1, 0x7F, 0x80, 0xFF, 0xC3 }[random.Next(6)]; break;
                case 2: if (frame.Count > 1) frame.RemoveRange(at, random.Next(1, frame.Count - at + 1)); break;
                case 3: var extra = new byte[random.Next(1, 12)]; random.NextBytes(extra); frame.InsertRange(at, extra); break;
                case 4: if (frame.Count > 0) { var count = random.Next(1, Math.Min(64, frame.Count - at) + 1); frame.InsertRange(at, frame.GetRange(at, count)); } break;
                case 5: var other = corpus[random.Next(corpus.Count)]; if (other.Length > 1) { frame.RemoveRange(at, frame.Count - at); frame.AddRange(other.Skip(random.Next(other.Length))); } break;
                case 6: frame.InsertRange(at, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }); break;                 // um tamanho enorme
                default: if (frame.Count >= 5) { var bits = BitConverter.GetBytes(new[] { float.NaN, float.PositiveInfinity, float.MaxValue, -0f }[random.Next(4)]); var put = random.Next(1, frame.Count - 3); for (var i = 0; i < 4; i++) frame[put + i] = bits[i]; } break;
            }
            if (frame.Count > 100_000) frame.RemoveRange(100_000, frame.Count - 100_000);
        }
        return frame.ToArray();
    }
}
