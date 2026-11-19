using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LifeVerse.Shared.Networking;
using LifeVerse.Wire;
using Xunit;

namespace LifeVerse.Wire.Tests;

/// <summary>Um servidor e clientes de verdade, em UDP no próprio computador: admissão, ordem, tamanhos grandes, quadros inválidos e uma rede que perde pacotes.</summary>
public sealed class WireNetworkTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public void Dispose() { foreach (var item in _disposables) item.Dispose(); }

    private sealed class Peer
    {
        public WireClient Client = new();
        public List<IWireMessage> Received = [];
        public List<WireDisconnectReason> Disconnects = [];
        public bool Connected;
    }

    private (WireServer Server, List<(int Peer, IWireMessage Message)> Inbox, List<(int Peer, string Reason)> Rejected) NewServer(int maxPeers = 8, double authSeconds = 10)
    {
        var server = new WireServer(maxPeers, authSeconds); _disposables.Add(server);
        Assert.True(server.Start(0, loopbackOnly: true));
        var inbox = new List<(int, IWireMessage)>(); var rejected = new List<(int, string)>();
        server.MessageReceived += (peer, message) => inbox.Add((peer, message));
        server.PeerRejected += (peer, reason) => rejected.Add((peer, reason));
        return (server, inbox, rejected);
    }

    private Peer NewClient(int port)
    {
        var peer = new Peer(); _disposables.Add(peer.Client);
        peer.Client.Connected += () => peer.Connected = true;
        peer.Client.Disconnected += reason => { peer.Connected = false; peer.Disconnects.Add(reason); };
        peer.Client.MessageReceived += peer.Received.Add;
        Assert.True(peer.Client.Connect("127.0.0.1", port));
        return peer;
    }

    private static void Pump(WireServer server, IEnumerable<Peer> clients, Func<bool> done, int timeoutMs = 8000, string? what = null)
    {
        var list = clients.ToList(); var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.ElapsedMilliseconds < timeoutMs, "tempo esgotado esperando: " + what);
            server.Poll(); foreach (var client in list) client.Client.Poll();
            Thread.Sleep(1);
        }
    }

    /// <summary>Conecta, autentica e devolve o id do jogador no servidor.</summary>
    private int Login(WireServer server, List<(int Peer, IWireMessage Message)> inbox, Peer client)
    {
        var before = inbox.Count;
        Pump(server, [client], () => client.Connected, what: "conexão");
        Assert.True(client.Client.Send(new AuthenticateMessage("ticket", WireProtocol.Version)));
        Pump(server, [client], () => inbox.Count > before, what: "Authenticate no servidor");
        var (peerId, message) = inbox[^1];
        Assert.IsType<AuthenticateMessage>(message);
        server.MarkAuthenticated(peerId);
        Assert.True(server.Send(peerId, new AuthResultMessage(true, "ok", 7)));
        var expected = client.Received.Count + 1;
        Pump(server, [client], () => client.Received.Count >= expected, what: "AuthResult no cliente");
        return peerId;
    }

    [Fact]
    public void AClientConnectsAuthenticatesAndGetsTheAnswer()
    {
        var (server, inbox, rejected) = NewServer(); var client = NewClient(server.Port);
        var peerId = Login(server, inbox, client);
        var result = Assert.IsType<AuthResultMessage>(client.Received[0]);
        Assert.True(result.Success); Assert.Equal(7u, result.ServerTick);
        Assert.True(server.IsAuthenticated(peerId)); Assert.Empty(rejected);
    }

    [Fact]
    public void AnUnauthenticatedPlayerThatSendsAnythingElseIsDisconnected()
    {
        var (server, _, rejected) = NewServer(); var client = NewClient(server.Port);
        Pump(server, [client], () => client.Connected, what: "conexão");
        client.Client.Send(new SendChatMessage("oi sem autenticar"));
        Pump(server, [client], () => rejected.Count > 0 && client.Disconnects.Count > 0, what: "recusa");
        Assert.Equal("not_authenticated", rejected[0].Reason);
    }

    [Fact]
    public void ASilentPlayerIsDisconnectedAfterTheAuthenticationTimeout()
    {
        var (server, _, rejected) = NewServer(authSeconds: 0.4); var client = NewClient(server.Port);
        Pump(server, [client], () => rejected.Count > 0 && client.Disconnects.Count > 0, what: "prazo de autenticação");
        Assert.Equal("auth_timeout", rejected[0].Reason);
    }

    [Fact]
    public void AMalformedFrameDisconnectsThePlayer()
    {
        var (server, inbox, rejected) = NewServer(); var client = NewClient(server.Port);
        Login(server, inbox, client);
        var raw = new LiteNetLib.EventBasedNetListener(); var manager = new LiteNetLib.NetManager(raw) { ChannelsCount = WireProtocol.ChannelCount }; _disposables.Add(new Stopper(manager));
        manager.Start(); manager.Connect("127.0.0.1", server.Port, WireProtocol.ConnectionKey);
        var connected = false; raw.PeerConnectedEvent += peer => { connected = true; peer.Send([0x02, 2, 0xC3, 0x28], 0, LiteNetLib.DeliveryMethod.ReliableOrdered); };
        // o jogador bruto envia um quadro com UTF-8 inválido logo depois de conectar (antes de se autenticar): o servidor o recusa
        var watch = Stopwatch.StartNew();
        while (rejected.Count == 0 && watch.ElapsedMilliseconds < 8000) { manager.PollEvents(); server.Poll(); client.Client.Poll(); Thread.Sleep(1); }
        Assert.True(connected); Assert.Single(rejected); Assert.Contains(rejected[0].Reason, new[] { "malformed", "not_authenticated" });
    }

    [Fact]
    public void ReliableMessagesArriveInOrder()
    {
        var (server, inbox, _) = NewServer(); var client = NewClient(server.Port); Login(server, inbox, client);
        var start = inbox.Count;
        for (var i = 0; i < 500; i++) client.Client.Send(new SendChatMessage("m" + i));
        Pump(server, [client], () => inbox.Count - start >= 500, what: "500 mensagens");
        Assert.Equal(Enumerable.Range(0, 500).Select(i => "m" + i), inbox.Skip(start).Select(x => ((SendChatMessage)x.Message).Message));
    }

    [Fact]
    public void SequencedInputsNeverGoBackwards()
    {
        var (server, inbox, _) = NewServer(); var client = NewClient(server.Port); Login(server, inbox, client);
        var start = inbox.Count;
        for (uint i = 1; i <= 300; i++) { client.Client.Send(new SubmitInputMessage(i, 1f, 0f, 0)); if (i % 20 == 0) { server.Poll(); client.Client.Poll(); } }
        Pump(server, [client], () => inbox.Skip(start).Any(x => x.Message is SubmitInputMessage { Sequence: 300 }), what: "último input");
        var sequences = inbox.Skip(start).Select(x => ((SubmitInputMessage)x.Message).Sequence).ToList();
        Assert.Equal(sequences.OrderBy(s => s), sequences);
    }

    [Fact]
    public void AHugeReliableMessageArrivesIntact()
    {
        var (server, inbox, _) = NewServer(); var client = NewClient(server.Port); var peerId = Login(server, inbox, client);
        var terrain = WireCodecTests.Bytes(900_000, 42);
        Assert.True(server.Send(peerId, new TerrainFullMessage(terrain, true)));
        Pump(server, [client], () => client.Received.OfType<TerrainFullMessage>().Any(), what: "terreno completo");
        var received = client.Received.OfType<TerrainFullMessage>().Single();
        Assert.True(received.CanEdit); Assert.Equal(terrain, received.Data);
    }

    [Fact]
    public void AGodotSnapshotPayloadCrossesTheNetworkAndDecodes()
    {
        var (server, inbox, _) = NewServer(); var client = NewClient(server.Port); var peerId = Login(server, inbox, client);
        var payload = SnapshotCodec.Encode(77, [new PlayerSnapshot(peerId, 1f, 2f, 3f, 0f, 0f, 0f, .5f, NetworkMotionState.Walk, 5)]);
        server.Send(peerId, new SnapshotMessage(payload));
        Pump(server, [client], () => client.Received.OfType<SnapshotMessage>().Any(), what: "snapshot");
        Assert.True(SnapshotCodec.TryDecode(client.Received.OfType<SnapshotMessage>().First().Payload, out var snapshot));
        Assert.Equal(77u, snapshot.ServerTick); Assert.Equal(NetworkMotionState.Walk, snapshot.Players.Single().Motion);
    }

    [Fact]
    public void BroadcastReachesTheOthersButNotTheSenderOrTheUnauthenticated()
    {
        var (server, inbox, _) = NewServer(); var a = NewClient(server.Port); var b = NewClient(server.Port); var c = NewClient(server.Port);
        var idA = Login(server, inbox, a); Login(server, inbox, b);
        Pump(server, [a, b, c], () => c.Connected, what: "terceiro conectado");           // c conectou e não se autenticou
        server.Broadcast(new ReceiveChatMessage(idA, "A", "oi", 1), exceptPeerId: idA);
        Pump(server, [a, b, c], () => b.Received.OfType<ReceiveChatMessage>().Any(), what: "broadcast em B");
        for (var i = 0; i < 50; i++) { server.Poll(); a.Client.Poll(); b.Client.Poll(); c.Client.Poll(); Thread.Sleep(2); }
        Assert.Empty(a.Received.OfType<ReceiveChatMessage>()); Assert.Empty(c.Received.OfType<ReceiveChatMessage>());
    }

    [Fact]
    public void ServerRefusesMorePlayersThanItsLimit()
    {
        var (server, inbox, _) = NewServer(maxPeers: 1); var first = NewClient(server.Port); Login(server, inbox, first);
        var second = NewClient(server.Port);
        var watch = Stopwatch.StartNew();
        while (second.Disconnects.Count == 0 && watch.ElapsedMilliseconds < 8000) { server.Poll(); first.Client.Poll(); second.Client.Poll(); Thread.Sleep(2); }
        Assert.False(second.Connected); Assert.NotEmpty(second.Disconnects); Assert.Equal(1, server.PeerCount);
    }

    [Fact]
    public void ReliableMessagesSurviveANetworkThatLosesAndDelaysPackets()
    {
        var (server, inbox, _) = NewServer();
        var proxy = new LossyUdpProxy(server.Port, lossPercent: 12, maxDelayMs: 35, seed: 7); _disposables.Add(proxy);
        var client = NewClient(proxy.Port);
        Login(server, inbox, client);
        var start = inbox.Count;
        for (var i = 0; i < 200; i++) client.Client.Send(new SendChatMessage("p" + i));
        for (uint i = 1; i <= 200; i++) client.Client.Send(new SubmitInputMessage(i, 0f, 0f, 0));
        Pump(server, [client], () => inbox.Skip(start).Count(x => x.Message is SendChatMessage) >= 200, timeoutMs: 30_000, what: "200 mensagens confiáveis com perda");
        Assert.Equal(Enumerable.Range(0, 200).Select(i => "p" + i), inbox.Skip(start).Select(x => x.Message).OfType<SendChatMessage>().Select(m => m.Message));
        var inputs = inbox.Skip(start).Select(x => x.Message).OfType<SubmitInputMessage>().Select(m => m.Sequence).ToList();
        Assert.Equal(inputs.OrderBy(s => s), inputs);                    // o que chegou das entradas não confiáveis chegou em ordem
        Assert.True(proxy.Dropped > 0, "o proxy precisa ter descartado datagramas para o teste valer");
    }

    private sealed class Stopper(LiteNetLib.NetManager manager) : IDisposable { public void Dispose() => manager.Stop(); }
}

/// <summary>Um intermediário UDP que descarta uma porcentagem dos datagramas e atrasa os demais (de 0 até o máximo), nos dois sentidos.</summary>
internal sealed class LossyUdpProxy : IDisposable
{
    private readonly UdpClient _front = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly IPEndPoint _server;
    private readonly ConcurrentDictionary<IPEndPoint, UdpClient> _upstreams = new();
    private readonly Random _random; private readonly int _loss, _maxDelay;
    private readonly CancellationTokenSource _stop = new();
    private int _dropped;

    public LossyUdpProxy(int serverPort, int lossPercent, int maxDelayMs, int seed)
    {
        _server = new IPEndPoint(IPAddress.Loopback, serverPort); _loss = lossPercent; _maxDelay = maxDelayMs; _random = new Random(seed);
        Task.Run(FrontLoop);
    }

    public int Port => ((IPEndPoint)_front.Client.LocalEndPoint!).Port;
    public int Dropped => Volatile.Read(ref _dropped);

    private bool Drop() { lock (_random) { return _random.Next(100) < _loss; } }
    private int Delay() { lock (_random) { return _random.Next(0, _maxDelay + 1); } }

    private async Task Forward(UdpClient from, byte[] data, IPEndPoint to)
    {
        if (Drop()) { Interlocked.Increment(ref _dropped); return; }
        var delay = Delay(); if (delay > 0) await Task.Delay(delay);
        try { await from.SendAsync(data, data.Length, to); } catch (ObjectDisposedException) { } catch (SocketException) { }
    }

    private async Task FrontLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await _front.ReceiveAsync(_stop.Token); } catch (OperationCanceledException) { return; } catch (ObjectDisposedException) { return; } catch (SocketException) { continue; }
            var client = packet.RemoteEndPoint;
            var upstream = _upstreams.GetOrAdd(client, endpoint => { var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); _ = Task.Run(() => BackLoop(socket, endpoint)); return socket; });
            _ = Forward(upstream, packet.Buffer, _server);
        }
    }

    private async Task BackLoop(UdpClient upstream, IPEndPoint client)
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await upstream.ReceiveAsync(_stop.Token); } catch (OperationCanceledException) { return; } catch (ObjectDisposedException) { return; } catch (SocketException) { continue; }
            _ = Forward(_front, packet.Buffer, client);
        }
    }

    public void Dispose() { _stop.Cancel(); _front.Dispose(); foreach (var socket in _upstreams.Values) socket.Dispose(); }
}
