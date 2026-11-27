using System.Diagnostics;
using LiteNetLib;

namespace LifeVerse.Wire;

public enum WireDisconnectReason { Closed, Timeout, Refused, Failed }

internal static class WireTransport
{
    public static DeliveryMethod ToLiteNetLib(WireDelivery delivery) => delivery switch
    {
        WireDelivery.Reliable => DeliveryMethod.ReliableOrdered,
        WireDelivery.Sequenced => DeliveryMethod.Sequenced,
        _ => DeliveryMethod.Unreliable,
    };

    public static WireDisconnectReason ToReason(DisconnectReason reason) => reason switch
    {
        DisconnectReason.DisconnectPeerCalled or DisconnectReason.RemoteConnectionClose => WireDisconnectReason.Closed,
        DisconnectReason.Timeout => WireDisconnectReason.Timeout,
        DisconnectReason.ConnectionRejected or DisconnectReason.InvalidProtocol => WireDisconnectReason.Refused,
        _ => WireDisconnectReason.Failed,
    };

    public static NetManager NewManager(EventBasedNetListener listener) => new(listener)
    {
        ChannelsCount = WireProtocol.ChannelCount,
        AutoRecycle = true,           // o quadro é decodificado e copiado dentro do evento; nada guarda o leitor
        DisconnectTimeout = 8000,
    };
}

/// <summary>
/// Servidor LifeVerse Wire. Chame <see cref="Poll"/> a cada tick do servidor. Um jogador só pode enviar <see cref="AuthenticateMessage"/> até ser marcado
/// com <see cref="MarkAuthenticated"/> (a aplicação confere o ticket e a versão); qualquer outra coisa, um quadro malformado ou o silêncio por mais de
/// o prazo de autenticação desconecta o jogador. Os eventos rodam na thread que chama Poll.
/// </summary>
public sealed class WireServer : IDisposable
{
    private sealed class PeerState(NetPeer peer) { public readonly NetPeer Peer = peer; public bool Authenticated, Cut; public long ConnectedAt = Stopwatch.GetTimestamp(); public WireBudget? Budget; }

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private readonly Dictionary<int, PeerState> _peers = new();
    private readonly int _maxPeers;
    private readonly double _authenticationSeconds;
    private readonly WireRateLimit? _rateLimit;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <param name="rateLimit">Quanto cada jogador pode enviar; nulo usa os padrões de <see cref="WireRateLimit"/>.</param>
    public WireServer(int maxPeers, double authenticationSeconds = WireProtocol.DefaultAuthenticationSeconds, WireRateLimit? rateLimit = null)
    {
        _maxPeers = maxPeers; _authenticationSeconds = authenticationSeconds; _rateLimit = rateLimit ?? new WireRateLimit();
        _manager = WireTransport.NewManager(_listener);
        _listener.ConnectionRequestEvent += request =>
        {
            if (_manager.ConnectedPeersCount >= _maxPeers) { request.Reject(); return; }
            request.AcceptIfKey(WireProtocol.ConnectionKey);
        };
        _listener.PeerConnectedEvent += peer => { _peers[PeerKey(peer)] = new PeerState(peer) { Budget = new WireBudget(_rateLimit, _clock.Elapsed.TotalSeconds) }; PeerConnected?.Invoke(PeerKey(peer)); };
        _listener.PeerDisconnectedEvent += (peer, info) => { _peers.Remove(PeerKey(peer)); PeerDisconnected?.Invoke(PeerKey(peer), WireTransport.ToReason(info.Reason)); };
        _listener.NetworkReceiveEvent += (peer, reader, _, _) => Receive(peer, reader.GetRemainingBytesSpan());
    }

    public event Action<int>? PeerConnected;
    public event Action<int, WireDisconnectReason>? PeerDisconnected;
    public event Action<int, IWireMessage>? MessageReceived;
    /// <summary>Um jogador foi desconectado por culpa dele: "empty", "unknown_type", "wrong_direction", "malformed", "trailing_bytes", "too_large", "not_authenticated", "auth_timeout" ou "rate_limit".</summary>
    public event Action<int, string>? PeerRejected;

    /// <summary>O número do jogador na rede: o id do LiteNetLib (que começa em 0) mais 1. O 0 fica livre para "ninguém / o sistema" (avisos, scripts), como o 0 do Godot.</summary>
    private static int PeerKey(NetPeer peer) => peer.Id + 1;

    public int Port => _manager.LocalPort;
    /// <summary>Quadros descartados por passarem do limite, de todos os jogadores, desde o começo.</summary>
    public long Throttled { get; private set; }
    public int PeerCount => _manager.ConnectedPeersCount;

    /// <summary>Abre a porta (0 escolhe uma livre). Com <paramref name="loopbackOnly"/> só aceita conexões do próprio computador (testes; evita o aviso do Firewall).</summary>
    public bool Start(int port, bool loopbackOnly = false) => loopbackOnly ? _manager.Start(System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback, port) : _manager.Start(port);

    public void Poll()
    {
        _manager.PollEvents();
        if (_authenticationSeconds <= 0) return;
        var now = Stopwatch.GetTimestamp();
        foreach (var (id, state) in _peers.ToList())
            if (!state.Authenticated && (now - state.ConnectedAt) / (double)Stopwatch.Frequency > _authenticationSeconds) Reject(id, "auth_timeout");
    }

    public bool IsAuthenticated(int peerId) => _peers.TryGetValue(peerId, out var state) && state.Authenticated;

    public void MarkAuthenticated(int peerId) { if (_peers.TryGetValue(peerId, out var state)) state.Authenticated = true; }

    public bool Send(int peerId, IWireMessage message)
    {
        if (!_peers.TryGetValue(peerId, out var state)) return false;
        var route = WireCodec.RouteOf(message);
        state.Peer.Send(WireCodec.Encode(message), route.Channel, WireTransport.ToLiteNetLib(route.Delivery));
        return true;
    }

    /// <summary>Envia a todos os jogadores autenticados (ou a todos), menos <paramref name="exceptPeerId"/>.</summary>
    public void Broadcast(IWireMessage message, int exceptPeerId = -1, bool authenticatedOnly = true)
    {
        var route = WireCodec.RouteOf(message); var frame = WireCodec.Encode(message); var method = WireTransport.ToLiteNetLib(route.Delivery);
        foreach (var (id, state) in _peers)
        {
            if (id == exceptPeerId || (authenticatedOnly && !state.Authenticated)) continue;
            state.Peer.Send(frame, route.Channel, method);
        }
    }

    public void Disconnect(int peerId) { if (_peers.TryGetValue(peerId, out var state)) state.Peer.Disconnect(); }

    private void Reject(int peerId, string reason)
    {
        if (_peers.TryGetValue(peerId, out var state)) { if (state.Cut) return; state.Cut = true; }   // o que ainda estiver na fila dele é ignorado, e o motivo é dito uma vez só
        PeerRejected?.Invoke(peerId, reason); Disconnect(peerId);
    }

    private void Receive(NetPeer peer, ReadOnlySpan<byte> frame)
    {
        if (!_peers.TryGetValue(PeerKey(peer), out var state) || state.Cut) return;
        // o limite vem antes de decodificar: quem inunda não custa nem a leitura
        var verdict = state.Budget!.Take(frame.Length, _clock.Elapsed.TotalSeconds);
        if (verdict != WireVerdict.Accept) { Throttled++; if (verdict == WireVerdict.Disconnect) Reject(PeerKey(peer), "rate_limit"); return; }
        var decoded = WireCodec.Decode(frame, WireDirection.ClientToServer);
        if (!decoded.Ok) { Reject(PeerKey(peer), decoded.Error!); return; }
        if (!state.Authenticated && decoded.Message is not AuthenticateMessage) { Reject(PeerKey(peer), "not_authenticated"); return; }
        MessageReceived?.Invoke(PeerKey(peer), decoded.Message!);
    }

    public void Dispose() => _manager.Stop();
}

/// <summary>Cliente LifeVerse Wire. Chame <see cref="Poll"/> a cada quadro do jogo.</summary>
public sealed class WireClient : IDisposable
{
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private NetPeer? _peer;

    public WireClient()
    {
        _manager = WireTransport.NewManager(_listener);
        _manager.EnableStatistics = true;   // para a perda de pacotes mostrada ao jogador
        _listener.PeerConnectedEvent += peer => { _peer = peer; Connected?.Invoke(); };
        _listener.PeerDisconnectedEvent += (_, info) => { _peer = null; Disconnected?.Invoke(WireTransport.ToReason(info.Reason)); };
        _listener.NetworkReceiveEvent += (peer, reader, _, _) => Receive(peer, reader.GetRemainingBytesSpan());
    }

    public event Action? Connected;
    public event Action<WireDisconnectReason>? Disconnected;
    public event Action<IWireMessage>? MessageReceived;
    /// <summary>O servidor mandou um quadro que não se entende; a conexão é encerrada.</summary>
    public event Action<string>? Malformed;

    public bool IsConnected => _peer?.ConnectionState == ConnectionState.Connected;
    public int RoundTripMilliseconds => _peer?.Ping * 2 ?? 0;
    /// <summary>De cada cem pacotes enviados nesta conexão, quantos se perderam (0 sem conexão).</summary>
    public long PacketLossPercent => _peer?.Statistics.PacketLossPercent ?? 0;

    public bool Connect(string host, int port)
    {
        // conectar a este mesmo computador não precisa abrir o socket para a rede toda
        var local = System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address);
        if (!_manager.IsRunning && !(local ? _manager.Start(System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback, 0) : _manager.Start())) return false;
        return _manager.Connect(host, port, WireProtocol.ConnectionKey) != null;
    }

    public void Poll() => _manager.PollEvents();

    public bool Send(IWireMessage message)
    {
        if (_peer == null || _peer.ConnectionState != ConnectionState.Connected) return false;
        var route = WireCodec.RouteOf(message);
        _peer.Send(WireCodec.Encode(message), route.Channel, WireTransport.ToLiteNetLib(route.Delivery));
        return true;
    }

    public void Disconnect() => _peer?.Disconnect();

    private void Receive(NetPeer peer, ReadOnlySpan<byte> frame)
    {
        var decoded = WireCodec.Decode(frame, WireDirection.ServerToClient);
        if (!decoded.Ok) { Malformed?.Invoke(decoded.Error!); peer.Disconnect(); return; }
        MessageReceived?.Invoke(decoded.Message!);
    }

    public void Dispose() => _manager.Stop();
}
