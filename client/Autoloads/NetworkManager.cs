using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Vehicles;

namespace LifeVerse.Client.Autoloads;

public partial class NetworkManager : Node
{
    [Signal] public delegate void ConnectionStateChangedEventHandler(string state, string detail);
    /// <summary>Real progress of what the world needs to be ready after connecting: key is snapshot, terrain, spawn or objects; done and total are units (total -1 when unknown).</summary>
    [Signal] public delegate void WorldProgressEventHandler(string key, long done, long total);
    [Signal] public delegate void ChatReceivedEventHandler(string line);
    [Signal] public delegate void BuildOperationReceivedEventHandler(string operationJson);
    [Signal] public delegate void BuildOperationRejectedEventHandler(string code);
    [Signal] public delegate void TerrainPermissionChangedEventHandler(bool canEdit);
    [Signal] public delegate void TerrainEditRejectedEventHandler(string code);
    [Signal] public delegate void VehicleMessageEventHandler(string text);
    /// Somebody's look was refreshed while connected (the peer and the name).
    [Signal] public delegate void AppearanceRefreshedEventHandler(int peerId, string displayName);
    /// The island answered a teleport request: where we landed and which way we face, or why not.
    [Signal] public delegate void TeleportResultEventHandler(bool ok, string code, float x, float z, float yaw);
    public VehicleClient Vehicles { get; private set; } = null!;
    public VoiceChat Voice { get; private set; } = null!;
    /// The other avatars on the island with their names and where they stand, for matching a right click to a person.
    public IEnumerable<(int Peer, string Name, Vector3 Position)> RemoteAvatars => _remote.Select(x => (x.Key, x.Value.Name, x.Value.Node.GlobalPosition)).ToList();
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
    /// <summary>The island being entered (the same as CurrentIslandId; the name says what it is while the entry is still going on) and the one the person was on before it.</summary>
    public Guid TargetIslandId => _islandId;
    public Guid PreviousIslandId { get; private set; }
    /// <summary>Where the person asked to arrive on the next island, when they chose a spot (so the nearest assets load first); null for the usual arrival point.</summary>
    public (float X, float Z)? ArrivalHint { get; set; }
    /// <summary>A trip between islands is going on: a lost connection must not start the automatic reconnection to the island being entered.</summary>
    public bool TransferInProgress { get; set; }
    private Guid _previousIslandId { get => PreviousIslandId; set => PreviousIslandId = value; }
    /// <summary>The entry did not go on and the connection to the previous island is still there: the person stays on it.</summary>
    public void RestorePreviousIsland() { if (PreviousIslandId != Guid.Empty) { _islandId = PreviousIslandId; PreviousIslandId = Guid.Empty; } }
    public bool IsConnectedToIsland => Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

    public override void _Ready()
    {
        _bridge = GetNode<NetworkBridge>("/root/NetworkBridge");
        Vehicles = new VehicleClient { Name = "Vehicles" }; AddChild(Vehicles);
        Voice = new VoiceChat { Name = "Voice" }; AddChild(Voice);
        _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnEntryResponse;
        _snapshotHttp = new HttpRequest(); AddChild(_snapshotHttp); _snapshotHttp.RequestCompleted += OnSnapshotResponse;
        Multiplayer.ConnectedToServer += OnConnected;
        Multiplayer.ConnectionFailed += () => HandleConnectionLost("connection_failed");
        Multiplayer.ServerDisconnected += () => HandleConnectionLost("server_disconnected");
    }

    private double _detailTimer;
    /// What the avatars of other players may cost the scene, and how far each level of detail reaches.
    public DetailBudget AvatarBudget { get; set; } = new();
    /// How many of the other avatars are drawn at each level right now (for the performance overlay and tests).
    public IReadOnlyDictionary<AvatarDetail, int> DetailCounts => _remote.Values.GroupBy(r => r.Rig.Detail).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Decides how much of every other avatar is drawn: nearest first, by distance, by what the camera sees, within a budget of triangles; then tells each avatar.</summary>
    public void UpdateDetail()
    {
        if (_remote.Count == 0) return;
        var camera = GetViewport().GetCamera3D(); var centre = camera?.GlobalPosition ?? _localPlayer?.GlobalPosition ?? Vector3.Zero;
        var candidates = _remote.Select(p =>
        {
            var position = p.Value.Node.GlobalPosition;
            var visible = camera is null || camera.IsPositionInFrustum(position + Vector3.Up) || position.DistanceTo(centre) < 4f;
            return new DetailCandidate(p.Key, position.DistanceTo(centre), visible, p.Value.Rig.FullCost, p.Value.Rig.ReducedCost, false, p.Value.Rig.Detail);
        }).ToList();
        var plan = AvatarDetailPlanner.Plan(candidates, AvatarBudget);
        foreach (var (peer, detail) in plan) if (_remote.TryGetValue(peer, out var remote)) remote.Rig.SetDetail(detail);
    }

    public override void _Process(double delta)
    {
        _detailTimer += delta; if (_detailTimer >= .25) { _detailTimer = 0; UpdateDetail(); }
        var weight = 1f - MathF.Exp(-12f * (float)delta);
        foreach (var (peer, remote) in _remote)
        {
            remote.Node.Position = remote.Node.Position.Lerp(remote.TargetPosition, weight);
            remote.Node.Rotation = new Vector3(0, Mathf.LerpAngle(remote.Node.Rotation.Y, remote.TargetYaw, weight), 0);
            // Voice is a property of the island: while it is on for you, everyone shows the small dot. Speakers feed their own rings (VoicePlayback).
            if (Voice.PlaybackOf(peer) is null) remote.Rig.SetVoice(IsAuthenticated && Voice.Enabled, 0f, 0f);
        }
    }

    /// Tests: puts another player somewhere at once (the position the snapshots would have moved them to).
    public void PlaceRemoteForTesting(int peerId, Vector3 position) { if (_remote.TryGetValue(peerId, out var remote)) { remote.Node.GlobalPosition = position; remote.TargetPosition = position; } }

    public IEnumerable<Vector3> RemotePositions => _remote.Values.Select(remote => remote.Node.GlobalPosition);

    public Error JoinIsland(Guid islandId, bool retry = false)
    {
        if (islandId == Guid.Empty) return Error.InvalidParameter;
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (!session.IsAuthenticated || _http.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return Error.Unauthorized;
        if (!retry) { _previousIslandId = IsAuthenticated && _islandId != islandId ? _islandId : Guid.Empty; }
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

    public void ReportWorldProgress(string key, long done, long total) => EmitSignal(SignalName.WorldProgress, key, done, total);
    private bool _spawnReported;

    private void RequestIslandSnapshot()
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        _snapshotHttp.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/worlds/islands/{_islandId}/snapshot", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private void OnSnapshotResponse(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode is < 200 or >= 300) { ReportWorldProgress("snapshot", 0, -2); return; } // -2: the island snapshot is not available; the terrain still arrives from the server
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); var environment = doc.RootElement.GetProperty("environmentJson").GetString() ?? "{}";
            var terrain = GetTree().CurrentScene?.GetNodeOrNull<IslandTerrain>("IslandTerrain"); terrain?.ApplySnapshot(doc.RootElement.GetProperty("terrainJson").GetString() ?? "{}", environment);
            ReportWorldProgress("snapshot", 1, 1);
        }
        catch (Exception exception) { GD.PushWarning($"event=island_snapshot_invalid error={exception.GetType().Name}"); }
    }

    /// <summary>Connects to an island with the single-use ticket of a trip (already issued by the API for this destination), instead of asking for an entry ticket.</summary>
    public Error JoinWithTicket(Guid islandId, string token, string host, int port, int protocolVersion)
    {
        if (islandId == Guid.Empty || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) return Error.InvalidParameter;
        if (protocolVersion != NetworkProtocol.Version) { EmitSignal(SignalName.ConnectionStateChanged, "incompatible", "Versão do protocolo incompatível."); return Error.InvalidParameter; }
        if (islandId != _islandId && _previousIslandId == Guid.Empty && IsAuthenticated) _previousIslandId = _islandId;
        _islandId = islandId; _intentionalDisconnect = false; _startAttempts = 0; _entryToken = token; _host = host; _port = port;
        RequestIslandSnapshot(); ConnectPeer();
        return Error.Ok;
    }

    /// <summary>Leaving the island we are on for another: its other avatars, vehicles and permissions go away at once, and the rest of the game is told so it can clear what belongs to it.</summary>
    public void LeaveCurrentIslandForTesting() => LeaveCurrentIsland();
    public int RemoteAvatarCountForTesting => _remote.Count;
    public void SetAuthenticatedForTesting(bool on) => IsAuthenticated = on;
    public void SetCurrentIslandForTesting(Guid island, bool authenticated) { _islandId = island; IsAuthenticated = authenticated; }

    private void LeaveCurrentIsland()
    {
        IsAuthenticated = false; CanEditTerrain = false; foreach (var remote in _remote.Values) remote.Node.QueueFree(); _remote.Clear(); Vehicles.Clear();
        EmitSignal(SignalName.ConnectionStateChanged, "switching", "Mudando de ilha");
    }

    private void ConnectPeer()
    {
        if (Multiplayer.HasMultiplayerPeer() && IsAuthenticated) LeaveCurrentIsland();
        ClosePeer();
        _spawnReported = false;
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

    /// The avatar this computer controls, once it exists (the editing camera hides it so it does not stand in the way).
    public PlayerController? LocalPlayer => _localPlayer;
    public void RegisterLocalPlayer(PlayerController player) => _localPlayer = player;
    public void UnregisterLocalPlayer(PlayerController player) { if (_localPlayer == player) _localPlayer = null; }
    public void SendInput(uint sequence, Vector2 direction, InputFlags flags) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.SubmitInput, (long)sequence, direction.X, direction.Y, (int)flags); }
    public void SetFlight(bool enabled) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.SetFlight, enabled); }
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
        ReportWorldProgress("terrain", 1, 1);
    }
    public void OnTerrainEdited(int tool, float x, float z, float radius, float strength, int layer, long revision)
    {
        if (!Enum.IsDefined((TerrainTool)tool) || GetTree().CurrentScene?.GetNodeOrNull<IslandTerrain>("IslandTerrain") is not { } terrain) return;
        if (!terrain.ApplyEdit((TerrainTool)tool, x, z, radius, strength, layer, revision)) _bridge.RpcId(1, NetworkBridge.MethodName.RequestTerrain);
    }
    /// Asks the island to teleport us to a parcel (its arrival point decides) or, without a parcel, to a spot. The server decides and answers with TeleportResult.
    public void RequestTeleport(string parcelId, float x, float z) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.RequestTeleport, parcelId ?? string.Empty, x, z); }
    public void OnTeleportResult(bool ok, string code, float x, float z, float yaw)
    {
        if (ok) _localPlayer?.FaceYaw(Mathf.DegToRad(yaw));
        EmitSignal(SignalName.TeleportResult, ok, code, x, z, yaw);
    }
    public void OnTerrainEditRejected(string code) => EmitSignal(SignalName.TerrainEditRejected, code);
    public void TouchObject(Guid objectId) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.TouchObject, objectId.ToString()); }
    /// <summary>The answer of the player to a dialog or permission request a script showed (the island checks it).</summary>
    public void SendScriptDialogResponse(string json) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.ScriptDialogResponse, json); }
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
            if (state.PeerId == ownPeer && !_spawnReported) { _spawnReported = true; ReportWorldProgress("spawn", 1, 1); } // the first position the server confirms for us
            if (state.PeerId == ownPeer) { _localPlayer?.Reconcile(position, new Vector3(state.VelocityX, state.VelocityY, state.VelocityZ), state.LastProcessedInput, state.Motion); continue; }
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
        _remote[peerId] = new RemoteAvatar(node, rig, position) { Name = displayName };
    }

    /// Asks the island to look up again how we look (after changing clothes, body or animations while connected). The island asks the server of accounts; nothing we send is shown to others.
    public void RefreshAppearance() { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.RefreshAppearance); }

    /// Somebody (maybe us) looks different now: show the new look at once.
    public void OnAppearanceChanged(int peerId, string displayName, string appearanceJson)
    {
        AvatarAppearanceData? data; try { data = JsonSerializer.Deserialize<AvatarAppearanceData>(appearanceJson); } catch (JsonException) { return; }
        if (data is null) return;
        if (peerId == Multiplayer.GetUniqueId()) { _localPlayer?.ShowAppearance(data); EmitSignal(SignalName.AppearanceRefreshed, peerId, displayName); return; }
        if (_remote.TryGetValue(peerId, out var remote)) { remote.Rig.ApplyAppearance(data); remote.Name = displayName; EmitSignal(SignalName.AppearanceRefreshed, peerId, displayName); }
    }

    public void OnPlayerDespawned(int peerId, bool suspended) { Voice.Forget(peerId); if (_remote.Remove(peerId, out var remote)) remote.Node.QueueFree(); }
    public void OnChatReceived(int peerId, string displayName, string message, long unixMilliseconds)
    {
        var time = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToLocalTime().ToString("HH:mm");
        EmitSignal(SignalName.ChatReceived, $"[{time}] {displayName}: {message}");
    }
    public void OnProtocolRejected(int serverVersion) => EmitSignal(SignalName.ConnectionStateChanged, "incompatible", $"Servidor exige protocolo {serverVersion}.");

    private async void HandleConnectionLost(string reason)
    {
        IsAuthenticated = false; CanEditTerrain = false; EmitSignal(SignalName.TerrainPermissionChanged, false); foreach (var remote in _remote.Values) remote.Node.QueueFree(); _remote.Clear(); Vehicles.Clear(); Voice.Reset();
        EmitSignal(SignalName.ConnectionStateChanged, "offline", reason);
        if (_intentionalDisconnect || _islandId == Guid.Empty || _reconnectAttempt >= 3) return;
        if (TransferInProgress) return;
        _reconnectAttempt++; await ToSignal(GetTree().CreateTimer(Math.Pow(2, _reconnectAttempt - 1)), SceneTreeTimer.SignalName.Timeout); JoinIsland(_islandId);
    }

    public void Disconnect() { _intentionalDisconnect = true; _islandId = Guid.Empty; IsAuthenticated = false; Vehicles.Clear(); Voice.Reset(); ClosePeer(); }
    public void SendVoice(long sequence, byte[] data) { if (IsAuthenticated) _bridge.RpcId(1, NetworkBridge.MethodName.SubmitVoice, sequence, data); }
    public void OnVoiceReceived(int speakerPeer, long sequence, byte[] data) => Voice.Receive(speakerPeer, sequence, data);
    public Node3D? RemoteNode(int peerId) => _remote.TryGetValue(peerId, out var remote) ? remote.Node : null;
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
        public Node3D Node { get; } = node; public AvatarRig Rig { get; } = rig; public string Name { get; set; } = string.Empty;
        public Vector3 TargetPosition { get; set; } = target; public float TargetYaw { get; set; }
    }
}
