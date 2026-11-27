namespace LifeVerse.Wire;

/// <summary>
/// Quanto um jogador pode enviar ao servidor. O jogo manda no máximo umas 130 mensagens por segundo (60 entradas, 50 pacotes de voz, a fila de construção e o resto);
/// os padrões deixam mais que o dobro disso, e uma folga para a rajada que chega depois de uma travada de rede. Quem passa do limite tem as mensagens a mais
/// descartadas; quem insiste (mais de <see cref="DropsBeforeDisconnect"/> descartes em <see cref="WindowSeconds"/>) é desconectado ("rate_limit").
/// </summary>
public sealed record WireRateLimit(
    double MessagesPerSecond = 300, double BurstMessages = 900,
    double BytesPerSecond = 1_500_000, double BurstBytes = 4_000_000,
    int DropsBeforeDisconnect = 300, double WindowSeconds = 10);

/// <summary>O que fazer com um quadro recebido de um jogador.</summary>
public enum WireVerdict { Accept, Drop, Disconnect }

/// <summary>A conta de um jogador: dois baldes (mensagens e bytes) que se enchem com o tempo. Não é segura para várias threads: o servidor a usa só na thread do Poll.</summary>
public sealed class WireBudget
{
    private readonly WireRateLimit _limit;
    private double _messages, _bytes, _last, _windowStart;
    private int _drops;
    private bool _cut;

    public WireBudget(WireRateLimit limit, double now)
    {
        _limit = limit; _messages = limit.BurstMessages; _bytes = limit.BurstBytes; _last = _windowStart = now;
    }

    /// <summary>Quantos quadros foram descartados desde o começo.</summary>
    public long Dropped { get; private set; }

    /// <summary>Um quadro de <paramref name="size"/> bytes chegou no instante <paramref name="now"/> (segundos, só cresce). Depois de um <see cref="WireVerdict.Disconnect"/> nada mais é aceito.</summary>
    public WireVerdict Take(int size, double now)
    {
        if (_cut) return WireVerdict.Drop;
        var passed = Math.Max(0, now - _last); _last = Math.Max(_last, now);
        _messages = Math.Min(_limit.BurstMessages, _messages + passed * _limit.MessagesPerSecond);
        _bytes = Math.Min(_limit.BurstBytes, _bytes + passed * _limit.BytesPerSecond);
        if (_messages >= 1 && _bytes >= size) { _messages -= 1; _bytes -= size; return WireVerdict.Accept; }
        if (now - _windowStart > _limit.WindowSeconds) { _windowStart = now; _drops = 0; }
        Dropped++;
        if (++_drops <= _limit.DropsBeforeDisconnect) return WireVerdict.Drop;
        _cut = true; return WireVerdict.Disconnect;
    }
}
