using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Scripts;

public partial class ServerMain : Node
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Net.Http.HttpClient _api = new() { Timeout = TimeSpan.FromSeconds(10) };
    private ServerConfiguration _config = null!;
    private TcpListener? _healthListener;
    private bool _registered;
    private bool _stopping;
    private NetworkBridge _bridge = null!;
    private readonly Dictionary<int, ConnectedPlayer> _players = [];
    private readonly Dictionary<Guid, SuspendedPlayer> _suspended = [];
    private readonly HashSet<Guid> _usedTickets = [];
    private uint _serverTick;
    private double _snapshotAccumulator;
    private long _worldSnapshotVersion;
    private string _terrainJson = "{}";
    private string _environmentJson = "{}";
    private double _worldSaveAccumulator;

    public override async void _Ready()
    {
        try
        {
            _config = ServerConfiguration.FromEnvironment();
            _bridge = GetNode<NetworkBridge>("/root/NetworkBridge");
            _api.DefaultRequestHeaders.Add("X-LifeVerse-Service-Key", _config.ServiceKey);
            _healthListener = new TcpListener(IPAddress.Loopback, _config.HealthPort);
            _healthListener.Start();
            _ = ServeHealthAsync(_shutdown.Token);
            await RegisterAsync(_shutdown.Token);
            await LoadWorldSnapshotAsync(_shutdown.Token);
            var peer = new ENetMultiplayerPeer();
            var enetError = peer.CreateServer(_config.GamePort, _config.MaxPlayers, 3);
            if (enetError != Error.Ok) throw new InvalidOperationException($"ENet failed to listen: {enetError}");
            Multiplayer.MultiplayerPeer = peer;
            Multiplayer.PeerConnected += OnPeerConnected;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;
            _ = HeartbeatLoopAsync(_shutdown.Token);
            Log("server_started", new { _config.InstanceId, _config.IslandId, _config.GamePort, _config.HealthPort, enet = "listening", _config.ProtocolVersion });
        }
        catch (Exception exception)
        {
            Log("server_start_failed", new { error = exception.Message });
            GetTree().Quit(1);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _serverTick++;
        var dt = Math.Min((float)delta, 1f / 20f);
        foreach (var player in _players.Values.Where(x => x.Authenticated))
        {
            player.Motion = AuthoritativeMovement.Step(player.State, player.LatestInput, dt);
            player.LastProcessedInput = player.LatestInput.Sequence;
            player.LatestInput = player.LatestInput with
            {
                Flags = player.LatestInput.Flags & (InputFlags.Run | InputFlags.Descend)
            };
        }
        _snapshotAccumulator += delta;
        _worldSaveAccumulator += delta;
        if (_worldSaveAccumulator >= 60) { _worldSaveAccumulator = 0; _ = SaveWorldSnapshotAsync(_shutdown.Token); }
        if (_snapshotAccumulator >= 1d / _config.SnapshotRate)
        {
            _snapshotAccumulator = 0;
            BroadcastSnapshots();
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var peer in _players.Values.Where(x => !x.Authenticated && now - x.ConnectedAt > TimeSpan.FromSeconds(5)).Select(x => x.PeerId).ToArray())
            Multiplayer.MultiplayerPeer.DisconnectPeer(peer);
        foreach (var avatar in _suspended.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray()) _suspended.Remove(avatar);
    }

    private void OnPeerConnected(long peerId)
    {
        _players[(int)peerId] = new ConnectedPlayer((int)peerId);
        Log("peer_connected", new { peerId });
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (!_players.Remove((int)peerId, out var player)) return;
        if (player.Authenticated)
        {
            _suspended[player.AvatarId] = new SuspendedPlayer(player, DateTimeOffset.UtcNow.AddSeconds(10));
            foreach (var other in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(other.PeerId, NetworkBridge.MethodName.DespawnPlayer, player.PeerId, true);
        }
        Log("peer_disconnected", new { peerId, player.AvatarId });
    }

    public async void Authenticate(int peerId, string token, int protocolVersion)
    {
        if (!_players.TryGetValue(peerId, out var player) || player.Authenticated || token.Length > 4096) return;
        if (protocolVersion != _config.ProtocolVersion)
        {
            _bridge.RpcId(peerId, NetworkBridge.MethodName.ProtocolRejected, _config.ProtocolVersion);
            Multiplayer.MultiplayerPeer.DisconnectPeer(peerId); return;
        }
        try
        {
            using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/entry-tokens/validate",
                new { token, islandId = _config.IslandId }, _shutdown.Token);
            if (!response.IsSuccessStatusCode) { Reject(peerId, "invalid_ticket"); return; }
            var admission = await response.Content.ReadFromJsonAsync<AdmissionResponse>(cancellationToken: _shutdown.Token);
            if (admission is null || admission.ProtocolVersion != _config.ProtocolVersion || !_usedTickets.Add(admission.TokenId)) { Reject(peerId, "ticket_reused_or_invalid"); return; }
            if (_suspended.Remove(admission.AvatarId, out var suspended)) player.State = suspended.Player.State;
            else
            {
                var slot = _players.Values.Count(x => x.Authenticated);
                player.State.X = slot % 8 * 2f; player.State.Z = slot / 8 * 2f;
            }
            player.Authenticated = true; player.UserId = admission.UserId; player.AvatarId = admission.AvatarId;
            player.SessionId = admission.SessionId; player.DisplayName = admission.DisplayName; player.AppearanceJson = admission.AppearanceJson;
            foreach (var existing in _players.Values.Where(x => x.Authenticated && x.PeerId != peerId))
            {
                SendSpawn(peerId, existing);
                SendSpawn(existing.PeerId, player);
            }
            SendSpawn(peerId, player);
            _bridge.RpcId(peerId, NetworkBridge.MethodName.AuthResult, true, "ok", (long)_serverTick);
            Log("peer_authenticated", new { peerId, admission.UserId, admission.AvatarId });
        }
        catch (Exception exception) { Log("admission_failed", new { peerId, error = exception.Message }); Reject(peerId, "admission_unavailable"); }
    }

    public void SubmitInput(int peerId, long sequence, float x, float z, int flags)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated || sequence <= player.LatestInput.Sequence || sequence > uint.MaxValue) return;
        if (!float.IsFinite(x) || !float.IsFinite(z)) return;
        var allowed = InputFlags.Run | InputFlags.Jump | InputFlags.Fly | InputFlags.Descend | InputFlags.Sit | InputFlags.Wave | InputFlags.Dance;
        player.LatestInput = new MovementInput((uint)sequence, Math.Clamp(x, -1f, 1f), Math.Clamp(z, -1f, 1f), (InputFlags)flags & allowed);
    }

    public void SendChat(int peerId, string message)
    {
        if (!_players.TryGetValue(peerId, out var sender) || !sender.Authenticated) return;
        var now = DateTimeOffset.UtcNow;
        while (sender.ChatTimes.Count > 0 && now - sender.ChatTimes.Peek() > TimeSpan.FromSeconds(10)) sender.ChatTimes.Dequeue();
        message = new string(message.Trim().Where(c => !char.IsControl(c)).Take(NetworkProtocol.MaxChatCharacters).ToArray());
        if (message.Length == 0 || sender.ChatTimes.Count >= 5 || ContainsBlockedTerm(message)) return;
        sender.ChatTimes.Enqueue(now);
        foreach (var target in _players.Values.Where(x => x.Authenticated && DistanceSquared(sender.State, x.State) <= 30f * 30f))
            _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.ReceiveChat, peerId, sender.DisplayName, message, now.ToUnixTimeMilliseconds());
    }

    public async void SubmitBuildOperation(int peerId, string operationJson)
    {
        if (!_players.TryGetValue(peerId, out var actor) || !actor.Authenticated || operationJson.Length > 100_000) return;
        var now = DateTimeOffset.UtcNow; while (actor.BuildTimes.Count > 0 && now - actor.BuildTimes.Peek() > TimeSpan.FromSeconds(1)) actor.BuildTimes.Dequeue(); if (actor.BuildTimes.Count >= 20) { _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationRejected, "rate_limit"); return; }
        BuildCommand? command; try { command = JsonSerializer.Deserialize<BuildCommand>(operationJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); } catch { command = null; }
        if (command is null || command.Operation is not ("create" or "update" or "delete") || (command.Object is not null && !ValidBuildCommand(command.Object))) { _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationRejected, "invalid_operation"); return; }
        actor.BuildTimes.Enqueue(now);
        try
        {
            using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/build-operations", new { userId = actor.UserId, operation = command.Operation, objectId = command.ObjectId, @object = command.Object, expectedRevision = command.ExpectedRevision }, _shutdown.Token);
            if (!response.IsSuccessStatusCode) { _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationRejected, response.StatusCode == System.Net.HttpStatusCode.Conflict ? "revision_conflict" : $"server_rejected_{(int)response.StatusCode}"); return; }
            var accepted = await response.Content.ReadAsStringAsync(_shutdown.Token);
            foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.BuildOperationApplied, accepted);
        }
        catch (Exception exception) { Log("build_operation_failed", new { peerId, error = exception.Message }); _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationRejected, "service_unavailable"); }
    }

    private void BroadcastSnapshots()
    {
        foreach (var target in _players.Values.Where(x => x.Authenticated))
        {
            var visible = _players.Values.Where(x => x.Authenticated && DistanceSquared(target.State, x.State) <= _config.InterestRadius * _config.InterestRadius)
                .Select(x => new PlayerSnapshot(x.PeerId, x.State.X, x.State.Y, x.State.Z, x.State.VelocityX, x.State.VelocityY, x.State.VelocityZ,
                    MathF.Atan2(x.State.VelocityX, -x.State.VelocityZ), x.Motion, x.LastProcessedInput)).ToArray();
            _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.Snapshot, SnapshotCodec.Encode(_serverTick, visible));
        }
    }

    private void SendSpawn(int targetPeer, ConnectedPlayer player) => _bridge.RpcId(targetPeer, NetworkBridge.MethodName.SpawnPlayer,
        player.PeerId, player.AvatarId.ToString(), player.DisplayName, player.AppearanceJson, player.State.X, player.State.Y, player.State.Z, (int)player.Motion);
    private void Reject(int peerId, string code) { _bridge.RpcId(peerId, NetworkBridge.MethodName.AuthResult, false, code, (long)_serverTick); Multiplayer.MultiplayerPeer.DisconnectPeer(peerId); }
    private static float DistanceSquared(AuthoritativePlayerState a, AuthoritativePlayerState b) => (a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z);
    private static bool ContainsBlockedTerm(string message) => (System.Environment.GetEnvironmentVariable("LIFEVERSE_CHAT_BLOCKED_TERMS") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static bool ValidBuildCommand(BuildObjectCommand command) { var values = new[] { command.PositionX, command.PositionY, command.PositionZ, command.RotationX, command.RotationY, command.RotationZ, command.ScaleX, command.ScaleY, command.ScaleZ }; return new[] { "cube", "sphere", "cylinder", "cone", "plane", "prism", "tube", "ring", "pyramid" }.Contains(command.PrimitiveType, StringComparer.OrdinalIgnoreCase) && values.All(float.IsFinite) && new[] { command.ScaleX, command.ScaleY, command.ScaleZ }.All(x => x is >= .01f and <= 64f) && (command.GeometryJson?.Length ?? 0) <= 16384 && (command.MaterialJson?.Length ?? 0) <= 32768; }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) _ = StopAsync();
    }

    private async Task RegisterAsync(CancellationToken ct)
    {
        var payload = new { instanceId = _config.InstanceId, islandId = _config.IslandId, hostname = _config.Hostname, port = _config.GamePort };
        using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/register", payload, ct);
        response.EnsureSuccessStatusCode();
        _registered = true;
        Log("server_registered", new { _config.InstanceId });
    }

    private async Task LoadWorldSnapshotAsync(CancellationToken ct)
    {
        using var response = await _api.GetAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/island-snapshot", ct);
        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<WorldSnapshotResponse>(cancellationToken: ct) ?? throw new InvalidOperationException("Island snapshot response was empty.");
        _worldSnapshotVersion = snapshot.Version; _terrainJson = snapshot.TerrainJson; _environmentJson = snapshot.EnvironmentJson;
        Log("world_snapshot_loaded", new { snapshot.Version, snapshot.IntegrityHash });
    }

    private async Task SaveWorldSnapshotAsync(CancellationToken ct)
    {
        try
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{_config.IslandId}:{_worldSnapshotVersion}:{_terrainJson}:{_environmentJson}")));
            using var response = await _api.PutAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/island-snapshot", new { expectedVersion = _worldSnapshotVersion, terrainJson = _terrainJson, environmentJson = _environmentJson, integrityHash = hash }, ct);
            if (response.IsSuccessStatusCode) { var result = await response.Content.ReadFromJsonAsync<SnapshotWriteResponse>(cancellationToken: ct); if (result is not null) _worldSnapshotVersion = result.Version; Log("world_snapshot_saved", new { _worldSnapshotVersion }); }
            else if ((int)response.StatusCode == 409) Log("world_snapshot_conflict", new { status = (int)response.StatusCode });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { Log("world_snapshot_save_failed", new { error = exception.Message }); }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                using var response = await _api.PutAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/heartbeat", new { status = "healthy" }, ct);
                if (!response.IsSuccessStatusCode) Log("heartbeat_failed", new { status = (int)response.StatusCode });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { Log("heartbeat_loop_failed", new { error = exception.Message }); }
    }

    private async Task ServeHealthAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _healthListener is not null)
            {
                using var client = await _healthListener.AcceptTcpClientAsync(ct);
                await using var stream = client.GetStream();
                var body = JsonSerializer.Serialize(new { status = _registered ? "healthy" : "starting", instanceId = _config.InstanceId });
                var bytes = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, ct);
                await stream.WriteAsync(bytes, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { Log("health_listener_failed", new { error = exception.Message }); }
    }

    private async Task StopAsync()
    {
        if (_stopping) return;
        _stopping = true;
        _shutdown.Cancel();
        await SaveWorldSnapshotAsync(CancellationToken.None);
        Multiplayer.MultiplayerPeer?.Close();
        _healthListener?.Stop();
        if (_registered)
        {
            try { using var response = await _api.DeleteAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}"); }
            catch (Exception exception) { Log("server_deregister_failed", new { error = exception.Message }); }
        }
        Log("server_stopped", new { _config.InstanceId });
        _api.Dispose();
        GetTree().Quit();
    }

    private static void Log(string eventName, object properties) => GD.Print(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, @event = eventName, properties }));

    private sealed class ConnectedPlayer(int peerId)
    {
        public int PeerId { get; } = peerId;
        public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;
        public bool Authenticated { get; set; }
        public Guid UserId { get; set; }
        public Guid AvatarId { get; set; }
        public Guid SessionId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string AppearanceJson { get; set; } = "{}";
        public AuthoritativePlayerState State { get; set; } = new();
        public MovementInput LatestInput { get; set; }
        public uint LastProcessedInput { get; set; }
        public NetworkMotionState Motion { get; set; }
        public Queue<DateTimeOffset> ChatTimes { get; } = new();
        public Queue<DateTimeOffset> BuildTimes { get; } = new();
    }
    private sealed record SuspendedPlayer(ConnectedPlayer Player, DateTimeOffset ExpiresAt);
    private sealed record AdmissionResponse(Guid UserId, Guid AvatarId, Guid IslandId, Guid SessionId, Guid TokenId,
        int ProtocolVersion, DateTimeOffset ExpiresAt, string DisplayName, string AppearanceJson);
    private sealed record WorldSnapshotResponse(Guid IslandId, long Version, string TerrainJson, string EnvironmentJson, string IntegrityHash);
    private sealed record SnapshotWriteResponse(long Version);
    private sealed record BuildCommand(string Operation, Guid? ObjectId, BuildObjectCommand? Object, int ExpectedRevision);
    private sealed record BuildObjectCommand(string PrimitiveType, float PositionX, float PositionY, float PositionZ, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ, string? MaterialJson, string? PhysicsJson, Guid? GroupId, int ExpectedRevision = 0, string? GeometryJson = null);
}
