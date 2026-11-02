using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;

namespace LifeVerse.Client.Autoloads;

public partial class NetworkManager : Node
{
    [Signal] public delegate void ConnectionStateChangedEventHandler(string state, string detail);
    [Signal] public delegate void ChatReceivedEventHandler(string line);
    [Signal] public delegate void BuildOperationReceivedEventHandler(string operationJson);
    [Signal] public delegate void BuildOperationRejectedEventHandler(string code);
    private readonly Dictionary<int, RemoteAvatar> _remote = [];
    private HttpRequest _http = null!;
    private HttpRequest _snapshotHttp = null!;
    private NetworkBridge _bridge = null!;
    private PlayerController? _localPlayer;
    private Guid _islandId;
    private string _entryToken = string.Empty;
    private string _host = string.Empty;
    private int _port;
    private int _reconnectAttempt;
    private bool _intentionalDisconnect;
    public bool IsAuthenticated { get; private set; }
    public Guid CurrentIslandId => _islandId;
    public bool IsConnectedToIsland => Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

    public override void _Ready()
    {
        _bridge = GetNode<NetworkBridge>("/root/NetworkBridge");
        _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnEntryResponse;
        _snapshotHttp = new HttpRequest(); AddChild(_snapshotHttp); _snapshotHttp.RequestCompleted += OnSnapshotResponse;
        Multiplayer.ConnectedToServer += OnConnected;
        Multiplayer.ConnectionFailed += () => HandleConnectionLost("connection_failed");
        Multiplayer.ServerDisconnected += () => HandleConnectionLost("server_disconnected");
    }

    public override void _Process(double delta)
    {
        var weight = 1f - MathF.Exp(-12f * (float)delta);
        foreach (var remote in _remote.Values)
        {
            remote.Node.Position = remote.Node.Position.Lerp(remote.TargetPosition, weight);
            remote.Node.Rotation = new Vector3(0, Mathf.LerpAngle(remote.Node.Rotation.Y, remote.TargetYaw, weight), 0);
        }
    }

    public Error JoinIsland(Guid islandId)
    {
        if (islandId == Guid.Empty) return Error.InvalidParameter;
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (!session.IsAuthenticated || _http.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return Error.Unauthorized;
        _islandId = islandId; _intentionalDisconnect = false;
        EmitSignal(SignalName.ConnectionStateChanged, "requesting_ticket", "Solicitando entrada na ilha…");
        return _http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/worlds/islands/{islandId}/entry-token",
            [$"Authorization: Bearer {session.AccessToken}"], Godot.HttpClient.Method.Post);
    }

    private void OnEntryResponse(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode is < 200 or >= 300) { EmitSignal(SignalName.ConnectionStateChanged, "failed", $"Entrada recusada pela API ({responseCode})."); return; }
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); var root = doc.RootElement;
            _entryToken = root.GetProperty("token").GetString() ?? string.Empty; _host = root.GetProperty("host").GetString() ?? string.Empty;
            _port = root.GetProperty("port").GetInt32();
            if (root.GetProperty("protocolVersion").GetInt32() != NetworkProtocol.Version) { EmitSignal(SignalName.ConnectionStateChanged, "incompatible", "Versão do protocolo incompatível."); return; }
            RequestIslandSnapshot();
            ConnectPeer();
        }
        catch (Exception exception) { GD.PushWarning($"event=entry_response_invalid error={exception.GetType().Name}"); EmitSignal(SignalName.ConnectionStateChanged, "failed", "Resposta de entrada inválida."); }
    }

    private void RequestIslandSnapshot()
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        _snapshotHttp.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/worlds/islands/{_islandId}/snapshot", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private void OnSnapshotResponse(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode is < 200 or >= 300) return;
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); var environment = doc.RootElement.GetProperty("environmentJson").GetString() ?? "{}";
            var terrain = GetTree().CurrentScene?.GetNodeOrNull<IslandTerrain>("IslandTerrain"); terrain?.ApplySnapshot(doc.RootElement.GetProperty("terrainJson").GetString() ?? "{}", environment);
        }
        catch (Exception exception) { GD.PushWarning($"event=island_snapshot_invalid error={exception.GetType().Name}"); }
    }

    private void ConnectPeer()
    {
        ClosePeer();
        var peer = new ENetMultiplayerPeer(); var error = peer.CreateClient(_host, _port, 3);
        if (error != Error.Ok) { EmitSignal(SignalName.ConnectionStateChanged, "failed", error.ToString()); return; }
        Multiplayer.MultiplayerPeer = peer;
        EmitSignal(SignalName.ConnectionStateChanged, "connecting", $"Conectando a {_host}:{_port}…");
    }

    private void OnConnected()
    {
        EmitSignal(SignalName.ConnectionStateChanged, "authenticating", "Validando credenciais temporárias…");
        _bridge.RpcId(1, NetworkBridge.MethodName.Authenticate, _entryToken, NetworkProtocol.Version); _entryToken = string.Empty;
    }

    public void OnAuthenticationResult(bool success, string code, long serverTick)
    {
        IsAuthenticated = success;
        if (success) { _reconnectAttempt = 0; EmitSignal(SignalName.ConnectionStateChanged, "online", $"Online · tick {serverTick}"); }
        else EmitSignal(SignalName.ConnectionStateChanged, "failed", $"Autenticação recusada: {code}");
    }

    public void RegisterLocalPlayer(PlayerController player) => _localPlayer = player;
    public void UnregisterLocalPlayer(PlayerController player) { if (_localPlayer == player) _localPlayer = null; }
    public void SendInput(uint sequence, Vector2 direction, InputFlags flags) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.SubmitInput, (long)sequence, direction.X, direction.Y, (int)flags); }
    public void SendChatMessage(string message) { if (IsAuthenticated && !string.IsNullOrWhiteSpace(message)) _bridge.RpcId(1, NetworkBridge.MethodName.SendChat, message[..Math.Min(message.Length, NetworkProtocol.MaxChatCharacters)]); }
    public void SubmitBuildOperation(string operationJson) { if (IsAuthenticated && operationJson.Length <= 100_000) _bridge.RpcId(1, NetworkBridge.MethodName.SubmitBuildOperation, operationJson); }
    public void OnBuildOperationApplied(string operationJson) => EmitSignal(SignalName.BuildOperationReceived, operationJson);
    public void OnBuildOperationRejected(string code) => EmitSignal(SignalName.BuildOperationRejected, code);

    public void OnSnapshot(byte[] payload)
    {
        if (!SnapshotCodec.TryDecode(payload, out var snapshot)) return;
        var ownPeer = Multiplayer.GetUniqueId();
        foreach (var state in snapshot.Players)
        {
            var position = new Vector3(state.X, state.Y, state.Z);
            if (state.PeerId == ownPeer) { _localPlayer?.Reconcile(position, new Vector3(state.VelocityX, state.VelocityY, state.VelocityZ), state.LastProcessedInput); continue; }
            if (_remote.TryGetValue(state.PeerId, out var remote))
            {
                remote.TargetPosition = position; remote.TargetYaw = state.Yaw; remote.Rig.SetMotionState(ToAvatarMotion(state.Motion));
            }
        }
    }

    public void OnPlayerSpawned(int peerId, string avatarId, string displayName, string appearanceJson, Vector3 position, int state)
    {
        if (peerId == Multiplayer.GetUniqueId() || _remote.ContainsKey(peerId) || GetTree().CurrentScene is not Node parent) return;
        var node = new Node3D { Name = $"RemotePlayer_{peerId}", Position = position };
        parent.AddChild(node);
        var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); node.AddChild(rig);
        try { rig.ApplyAppearance(JsonSerializer.Deserialize<AvatarAppearanceData>(appearanceJson) ?? new AvatarAppearanceData { DisplayName = displayName }); } catch { }
        rig.SetMotionState(ToAvatarMotion((NetworkMotionState)state), true);
        _remote[peerId] = new RemoteAvatar(node, rig, position);
    }

    public void OnPlayerDespawned(int peerId, bool suspended) { if (_remote.Remove(peerId, out var remote)) remote.Node.QueueFree(); }
    public void OnChatReceived(int peerId, string displayName, string message, long unixMilliseconds)
    {
        var time = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToLocalTime().ToString("HH:mm");
        EmitSignal(SignalName.ChatReceived, $"[{time}] {displayName}: {message}");
    }
    public void OnProtocolRejected(int serverVersion) => EmitSignal(SignalName.ConnectionStateChanged, "incompatible", $"Servidor exige protocolo {serverVersion}.");

    private async void HandleConnectionLost(string reason)
    {
        IsAuthenticated = false; foreach (var remote in _remote.Values) remote.Node.QueueFree(); _remote.Clear();
        EmitSignal(SignalName.ConnectionStateChanged, "offline", reason);
        if (_intentionalDisconnect || _islandId == Guid.Empty || _reconnectAttempt >= 3) return;
        _reconnectAttempt++; await ToSignal(GetTree().CreateTimer(Math.Pow(2, _reconnectAttempt - 1)), SceneTreeTimer.SignalName.Timeout); JoinIsland(_islandId);
    }

    public void Disconnect() { _intentionalDisconnect = true; _islandId = Guid.Empty; IsAuthenticated = false; ClosePeer(); }
    private void ClosePeer() { if (Multiplayer.HasMultiplayerPeer()) Multiplayer.MultiplayerPeer.Close(); Multiplayer.MultiplayerPeer = null; }
    private static AvatarMotionState ToAvatarMotion(NetworkMotionState state) => state switch
    {
        NetworkMotionState.Walk => AvatarMotionState.Walk, NetworkMotionState.Run => AvatarMotionState.Run, NetworkMotionState.Jump => AvatarMotionState.Jump,
        NetworkMotionState.Fall => AvatarMotionState.Fall, NetworkMotionState.Fly => AvatarMotionState.FlyMove, NetworkMotionState.Sit => AvatarMotionState.Sit,
        NetworkMotionState.Wave => AvatarMotionState.Wave, NetworkMotionState.Dance => AvatarMotionState.Dance, _ => AvatarMotionState.Idle
    };
    private sealed class RemoteAvatar(Node3D node, AvatarRig rig, Vector3 target)
    {
        public Node3D Node { get; } = node; public AvatarRig Rig { get; } = rig;
        public Vector3 TargetPosition { get; set; } = target; public float TargetYaw { get; set; }
    }
}
