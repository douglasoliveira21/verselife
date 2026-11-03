using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Vehicles;

namespace LifeVerse.Client.Autoloads;

public partial class NetworkManager : Node
{
    [Signal] public delegate void ConnectionStateChangedEventHandler(string state, string detail);
    [Signal] public delegate void ChatReceivedEventHandler(string line);
    [Signal] public delegate void BuildOperationReceivedEventHandler(string operationJson);
    [Signal] public delegate void BuildOperationRejectedEventHandler(string code);
    [Signal] public delegate void TerrainPermissionChangedEventHandler(bool canEdit);
    [Signal] public delegate void TerrainEditRejectedEventHandler(string code);
    [Signal] public delegate void VehicleMessageEventHandler(string text);
    public VehicleClient Vehicles { get; private set; } = null!;
    public int OwnPeerId => Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 0;
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
    private int _startAttempts;
    private const int MaxStartAttempts = 18;
    private bool _intentionalDisconnect;
    public bool IsAuthenticated { get; private set; }
    public Guid CurrentIslandId => _islandId;
    public bool IsConnectedToIsland => Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

    public override void _Ready()
    {
        _bridge = GetNode<NetworkBridge>("/root/NetworkBridge");
        Vehicles = new VehicleClient { Name = "Vehicles" }; AddChild(Vehicles);
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

    public IEnumerable<Vector3> RemotePositions => _remote.Values.Select(remote => remote.Node.GlobalPosition);

    public Error JoinIsland(Guid islandId, bool retry = false)
    {
        if (islandId == Guid.Empty) return Error.InvalidParameter;
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (!session.IsAuthenticated || _http.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return Error.Unauthorized;
        _islandId = islandId; _intentionalDisconnect = false; if (!retry) _startAttempts = 0;
        EmitSignal(SignalName.ConnectionStateChanged, "requesting_ticket", "Solicitando entrada na ilha…");
        return _http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/worlds/islands/{islandId}/entry-token",
            [$"Authorization: Bearer {session.AccessToken}"], Godot.HttpClient.Method.Post);
    }

    /// The API answers 503 with state "starting" while the supervisor boots the island's server; keep asking.
    /// "running" with a 503 means the island registered but its first heartbeat has not made it healthy yet.
    private static bool IslandIsStarting(byte[] body)
    {
        try { using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); return doc.RootElement.TryGetProperty("state", out var state) && state.GetString() is "starting" or "running"; }
        catch (JsonException) { return false; }
    }

    private async void WaitForIsland()
    {
        var island = _islandId;
        if (++_startAttempts > MaxStartAttempts) { EmitSignal(SignalName.ConnectionStateChanged, "failed", "A ilha não iniciou a tempo. Tente de novo em instantes ou avise o administrador."); return; }
        EmitSignal(SignalName.ConnectionStateChanged, "waiting_island", $"Iniciando a ilha… ({_startAttempts}/{MaxStartAttempts})");
        await ToSignal(GetTree().CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
        if (_intentionalDisconnect || _islandId != island) return;
        JoinIsland(island, retry: true);
    }

    private void OnEntryResponse(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode == 503 && IslandIsStarting(body)) { WaitForIsland(); return; }
        if (responseCode is < 200 or >= 300) { EmitSignal(SignalName.ConnectionStateChanged, "failed", EntryFailureMessage(responseCode)); return; }
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

    private static string EntryFailureMessage(long code) => code switch
    {
        503 => "Esta ilha está sem servidor ativo no momento (503). Só ilhas com um servidor de ilha em execução aceitam jogadores; tente outra ilha ou avise o administrador.",
        404 => "Ilha não encontrada (404). Confira o ID.",
        403 => "Você não tem permissão para entrar nesta ilha (403).",
        401 => "Sessão expirada (401). Entre novamente.",
        409 => "Crie o avatar antes de entrar em uma ilha (409).",
        _ => $"Entrada recusada pela API ({code})."
    };

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
    public bool CanEditTerrain { get; private set; }
    public void SubmitTerrainEdit(TerrainTool tool, Vector3 point, float radius, float strength, int layer)
    {
        if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.SubmitTerrainEdit, (int)tool, point.X, point.Z, radius, strength, layer);
    }
    public void OnTerrainFull(byte[] data, bool canEdit)
    {
        if (GetTree().CurrentScene?.GetNodeOrNull<IslandTerrain>("IslandTerrain") is { } terrain && !terrain.ApplyFull(data)) GD.PushWarning("event=terrain_full_invalid");
        CanEditTerrain = canEdit; EmitSignal(SignalName.TerrainPermissionChanged, canEdit);
    }
    public void OnTerrainEdited(int tool, float x, float z, float radius, float strength, int layer, long revision)
    {
        if (!Enum.IsDefined((TerrainTool)tool) || GetTree().CurrentScene?.GetNodeOrNull<IslandTerrain>("IslandTerrain") is not { } terrain) return;
        if (!terrain.ApplyEdit((TerrainTool)tool, x, z, radius, strength, layer, revision)) _bridge.RpcId(1, NetworkBridge.MethodName.RequestTerrain);
    }
    public void OnTerrainEditRejected(string code) => EmitSignal(SignalName.TerrainEditRejected, code);
    public void TouchObject(Guid objectId) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.TouchObject, objectId.ToString()); }
    public void SubmitSeaLevel(float level) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.SubmitSeaLevel, level); }
    public void UndoTerrain() { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.UndoTerrain); }
    public void RedoTerrain() { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.RedoTerrain); }
    public void SubmitTerrainImport(byte[] heights) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.SubmitTerrainImport, heights); }
    public void OnSeaLevelChanged(float level, long revision)
    {
        if (GetTree().CurrentScene?.GetNodeOrNull<IslandTerrain>("IslandTerrain") is not { } terrain) return;
        if (!terrain.ApplySeaLevel(level, revision)) _bridge.RpcId(1, NetworkBridge.MethodName.RequestTerrain);
    }
    public void OnTerrainPatched(byte[] patch, long revision)
    {
        if (GetTree().CurrentScene?.GetNodeOrNull<IslandTerrain>("IslandTerrain") is not { } terrain) return;
        if (!terrain.ApplyPatch(patch, revision)) _bridge.RpcId(1, NetworkBridge.MethodName.RequestTerrain);
    }
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
        IsAuthenticated = false; CanEditTerrain = false; EmitSignal(SignalName.TerrainPermissionChanged, false); foreach (var remote in _remote.Values) remote.Node.QueueFree(); _remote.Clear(); Vehicles.Clear();
        EmitSignal(SignalName.ConnectionStateChanged, "offline", reason);
        if (_intentionalDisconnect || _islandId == Guid.Empty || _reconnectAttempt >= 3) return;
        _reconnectAttempt++; await ToSignal(GetTree().CreateTimer(Math.Pow(2, _reconnectAttempt - 1)), SceneTreeTimer.SignalName.Timeout); JoinIsland(_islandId);
    }

    public void Disconnect() { _intentionalDisconnect = true; _islandId = Guid.Empty; IsAuthenticated = false; Vehicles.Clear(); ClosePeer(); }
    public void EnterVehicle(int vehicleId) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.EnterVehicle, vehicleId); }
    public void ExitVehicle() { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.ExitVehicle); }
    public void SendVehicleInput(float throttle, float steer, bool brake) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.SubmitVehicleInput, throttle, steer, brake); }
    public void OnVehicleSpawned(int vehicleId, string name, string type, Vector3 position, float yaw) => Vehicles.Spawn(vehicleId, name, type, position, yaw);
    public void OnVehicleSnapshots(byte[] payload) { if (VehicleCodec.TryDecode(payload, out _, out var vehicles)) Vehicles.Apply(vehicles); }
    public void OnVehicleRejected(string code) => EmitSignal(SignalName.VehicleMessage, code switch { "too_far" => "Chegue mais perto do veículo.", "occupied" => "Alguém já está dirigindo este veículo.", "already_driving" => "Você já está em um veículo.", _ => "Veículo indisponível." });
    private void ClosePeer() { if (Multiplayer.HasMultiplayerPeer()) Multiplayer.MultiplayerPeer.Close(); Multiplayer.MultiplayerPeer = null; }
    private static AvatarMotionState ToAvatarMotion(NetworkMotionState state) => state switch
    {
        NetworkMotionState.Walk => AvatarMotionState.Walk, NetworkMotionState.Run => AvatarMotionState.Run, NetworkMotionState.Jump => AvatarMotionState.Jump,
        NetworkMotionState.Fall => AvatarMotionState.Fall, NetworkMotionState.Fly => AvatarMotionState.FlyMove, NetworkMotionState.Sit => AvatarMotionState.Sit,
        NetworkMotionState.Wave => AvatarMotionState.Wave, NetworkMotionState.Dance => AvatarMotionState.Dance, NetworkMotionState.Swim => AvatarMotionState.Swim, _ => AvatarMotionState.Idle
    };
    private sealed class RemoteAvatar(Node3D node, AvatarRig rig, Vector3 target)
    {
        public Node3D Node { get; } = node; public AvatarRig Rig { get; } = rig;
        public Vector3 TargetPosition { get; set; } = target; public float TargetYaw { get; set; }
    }
}
