using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Networking;

namespace LifeVerse.Client.Scripts;

/// Keeps one WebSocket open to the API while signed in and raises `Notified` with a ready-to-show line for each
/// live event (private messages, friend requests, group invites). Reconnects with a growing pause.
public partial class NotificationClient : Node
{
    [Signal] public delegate void NotifiedEventHandler(string text);
    private WebSocketPeer? _socket;
    private double _wait;
    private double _backoff = 2;

    public override void _Process(double delta)
    {
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (session is null || !session.IsAuthenticated) { Disconnect(); return; }
        if (_socket is null)
        {
            _wait -= delta;
            if (_wait <= 0) Connect(session);
            return;
        }
        _socket.Poll();
        switch (_socket.GetReadyState())
        {
            case WebSocketPeer.State.Open:
                _backoff = 2;
                while (_socket.GetAvailablePacketCount() > 0)
                    if (NotificationFormatter.Describe(_socket.GetPacket().GetStringFromUtf8()) is { } text) EmitSignal(SignalName.Notified, text);
                break;
            case WebSocketPeer.State.Closed:
                _socket = null; _wait = _backoff; _backoff = Math.Min(_backoff * 2, 60);
                break;
        }
    }

    private void Connect(SessionManager session)
    {
        var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (app is null) return;
        var url = app.ApiBaseUrl.Replace("https://", "wss://", StringComparison.Ordinal).Replace("http://", "ws://", StringComparison.Ordinal) + "/api/v1/notifications/ws";
        var socket = new WebSocketPeer { HandshakeHeaders = [$"Authorization: Bearer {session.AccessToken}"] };
        if (socket.ConnectToUrl(url) != Error.Ok) { _wait = _backoff; _backoff = Math.Min(_backoff * 2, 60); return; }
        _socket = socket;
    }

    private void Disconnect()
    {
        if (_socket is null) return;
        _socket.Close(); _socket = null; _wait = 0; _backoff = 2;
    }

    public override void _ExitTree() => Disconnect();
}
