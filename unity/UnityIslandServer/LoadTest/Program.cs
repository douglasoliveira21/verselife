using System.Diagnostics;
using LifeVerse.Shared.Networking;
using LifeVerse.Wire;

// Uso: LoadTest --port 17777 [--host 127.0.0.1] [--clients 32] [--seconds 20] [--token-prefix unity-]
// Cada cliente manda o bilhete "<prefixo><n>", anda em círculos e mede o que recebe. Saída: um resumo e código 0 só se todos entraram e receberam snapshots.
static string Arg(string[] args, string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
var host = Arg(args, "--host", "127.0.0.1"); var port = int.Parse(Arg(args, "--port", "7777"));
var count = int.Parse(Arg(args, "--clients", "32")); var seconds = double.Parse(Arg(args, "--seconds", "20"), System.Globalization.CultureInfo.InvariantCulture);
var prefix = Arg(args, "--token-prefix", "unity-");

var clients = new List<Bot>();
for (var i = 0; i < count; i++) clients.Add(new Bot(i, $"{prefix}{i}"));
foreach (var bot in clients) bot.Init();
foreach (var bot in clients) if (!bot.Client.Connect(host, port)) { Console.WriteLine($"falha ao iniciar o cliente {bot.Index}"); return 2; }

var watch = Stopwatch.StartNew(); var lastInput = 0.0; uint sequence = 0;
while (watch.Elapsed.TotalSeconds < seconds)
{
    foreach (var bot in clients) { bot.Client.Poll(); bot.TryAuthenticate(); }
    if (watch.Elapsed.TotalSeconds - lastInput >= 1.0 / 30)
    {
        lastInput = watch.Elapsed.TotalSeconds; sequence++;
        var angle = watch.Elapsed.TotalSeconds * .5;
        foreach (var bot in clients.Where(b => b.Authenticated)) bot.Client.Send(new SubmitInputMessage(sequence, (float)Math.Cos(angle + bot.Index), (float)Math.Sin(angle + bot.Index), 0));
    }
    Thread.Sleep(1);
}

var admitted = clients.Count(b => b.Authenticated);
var withSnapshots = clients.Count(b => b.Snapshots > 0);
var rate = clients.Where(b => b.Authenticated).Select(b => b.Snapshots / Math.Max(1, b.SecondsAuthenticated)).DefaultIfEmpty(0).Average();
var maxSeen = clients.Select(b => b.MaxPlayersInSnapshot).DefaultIfEmpty(0).Max();
var rtt = clients.Where(b => b.Client.IsConnected).Select(b => b.Client.RoundTripMilliseconds).DefaultIfEmpty(0).Average();
Console.WriteLine($"clientes={count} admitidos={admitted} com_snapshots={withSnapshots} snapshots_por_s_por_cliente={rate:F1} jogadores_no_snapshot_max={maxSeen} rtt_ms={rtt:F1} desconectados={clients.Count(b => b.Disconnected)} recusas={string.Join(',', clients.Select(b => b.RejectCode).Where(c => c != null).Distinct())}");
foreach (var bot in clients) bot.Client.Dispose();
return admitted == count && withSnapshots == count ? 0 : 1;

sealed class Bot(int index, string token)
{
    public readonly int Index = index;
    public readonly WireClient Client = new();
    private bool _sent; private readonly Stopwatch _since = new();
    public bool Authenticated, Disconnected; public string? RejectCode;
    public long Snapshots; public int MaxPlayersInSnapshot; private uint _lastTick; private int _chunk;
    public double SecondsAuthenticated => _since.IsRunning ? _since.Elapsed.TotalSeconds : 0;

    public void Init() { Client.Disconnected += _ => Disconnected = true; Client.MessageReceived += OnMessage; }

    public void TryAuthenticate()
    {
        if (_sent || !Client.IsConnected) return;
        Client.Send(new AuthenticateMessage(token, WireProtocol.Version)); _sent = true;
    }

    private void OnMessage(IWireMessage message)
    {
        switch (message)
        {
            case AuthResultMessage result: Authenticated = result.Success; if (!result.Success) RejectCode = result.Code; else _since.Start(); break;
            case SnapshotMessage snapshot when SnapshotCodec.TryDecode(snapshot.Payload, out var decoded):
                Snapshots++;
                // um snapshot grande chega em vários pacotes do mesmo tick: soma os jogadores desse tick
                _chunk = decoded.ServerTick == _lastTick ? _chunk + decoded.Players.Count : decoded.Players.Count; _lastTick = decoded.ServerTick;
                MaxPlayersInSnapshot = Math.Max(MaxPlayersInSnapshot, _chunk);
                break;
        }
    }
}
