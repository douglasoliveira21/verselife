using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Vehicles;

namespace LifeVerse.IslandServer.Scripts;

public partial class ServerMain : Node
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Net.Http.HttpClient _api = new() { Timeout = TimeSpan.FromSeconds(10) };
    private ServerConfiguration _config = null!;
    private TcpListener? _healthListener;
    private bool _registered;
    private bool _ready;
    private bool _stopping;
    private NetworkBridge _bridge = null!;
    private readonly Dictionary<int, ConnectedPlayer> _players = [];
    private readonly Dictionary<Guid, SuspendedPlayer> _suspended = [];
    private readonly HashSet<Guid> _usedTickets = [];
    private uint _serverTick;
    private double _snapshotAccumulator;
    private long _worldSnapshotVersion;
    private string _terrainJson = "{}";
    private TerrainMap _terrain = new();
    private bool _terrainDirty;
    private string _environmentJson = "{}";
    private double _worldSaveAccumulator;
    private IslandScripting? _scripting;
    // When set (by the supervisor), an island nobody is on for this long saves itself and exits to free its port.
    private readonly int _idleShutdownMinutes = int.TryParse(System.Environment.GetEnvironmentVariable("LIFEVERSE_IDLE_SHUTDOWN_MINUTES"), out var idle) ? Math.Clamp(idle, 0, 24 * 60) : 0;
    private DateTimeOffset? _emptySince;

    public override async void _Ready()
    {
        try
        {
            _config = ServerConfiguration.FromEnvironment();
            _bridge = GetNode<NetworkBridge>("/root/NetworkBridge");
            _api.DefaultRequestHeaders.Add("X-LifeVerse-Service-Key", _config.ServiceKey);
            _healthListener = new TcpListener(IPAddress.Any, _config.HealthPort);
            _healthListener.Start();
            _ = ServeHealthAsync(_shutdown.Token);
            await RegisterAsync(_shutdown.Token);
            await LoadWorldSnapshotAsync(_shutdown.Token);
            await StartScriptingAsync(_shutdown.Token);
            _ = VehicleSyncLoopAsync(_shutdown.Token);
            var peer = new ENetMultiplayerPeer();
            var enetError = peer.CreateServer(_config.GamePort, _config.MaxPlayers, 3);
            if (enetError != Error.Ok) throw new InvalidOperationException($"ENet failed to listen: {enetError}");
            Multiplayer.MultiplayerPeer = peer;
            Multiplayer.PeerConnected += OnPeerConnected;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;
            _ = HeartbeatLoopAsync(_shutdown.Token);
            _ready = true;
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
        _scripting?.Update(_players.Values.Where(x => x.Authenticated).Select(PresenceOf).ToList());
        CheckIdle();
        var dt = Math.Min((float)delta, 1f / 20f);
        TickVehicles(dt);
        foreach (var player in _players.Values.Where(x => x.Authenticated))

        {
            if (PinDriver(player)) continue;
            player.Motion = AuthoritativeMovement.Step(player.State, player.LatestInput, dt, _terrain.SampleHeight, _terrain.SeaLevel, _terrain.HalfExtent + TerrainMap.OceanMargin);
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

    private void CheckIdle()
    {
        if (_idleShutdownMinutes <= 0 || !_ready || _stopping) return;
        var now = DateTimeOffset.UtcNow;
        if (_players.Values.Any(x => x.Authenticated) || _suspended.Count > 0) { _emptySince = null; return; }
        _emptySince ??= now;
        if (now - _emptySince < TimeSpan.FromMinutes(_idleShutdownMinutes)) return;
        Log("idle_shutdown", new { minutes = _idleShutdownMinutes });
        _ = StopAsync();
    }

    private void OnPeerConnected(long peerId)
    {
        _players[(int)peerId] = new ConnectedPlayer((int)peerId);
        Log("peer_connected", new { peerId });
    }

    private void OnPeerDisconnected(long peerId)
    {
        _fleet.Release((int)peerId);
        _voice.Forget((int)peerId);
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
                player.State.Y = _terrain.SampleHeight(player.State.X, player.State.Z) + AuthoritativeMovement.GroundOffset;
            }
            player.CanEditTerrain = admission.CanEditTerrain;
            player.Authenticated = true; player.UserId = admission.UserId; player.AvatarId = admission.AvatarId;
            player.SessionId = admission.SessionId; player.DisplayName = admission.DisplayName; player.AppearanceJson = admission.AppearanceJson;
            foreach (var existing in _players.Values.Where(x => x.Authenticated && x.PeerId != peerId))
            {
                SendSpawn(peerId, existing);
                SendSpawn(existing.PeerId, player);
            }
            SendSpawn(peerId, player);
            _bridge.RpcId(peerId, NetworkBridge.MethodName.AuthResult, true, "ok", (long)_serverTick);
            SendTerrain(player);
            SendAllVehicles(peerId);
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

    private static AvatarPresence PresenceOf(ConnectedPlayer player) => new(player.PeerId, player.AvatarId, player.UserId, player.DisplayName, player.State.X, player.State.Y, player.State.Z);

    /// Loads this island's objects and Lua scripts. A failure here must not keep the island from starting.
    private async Task StartScriptingAsync(CancellationToken ct)
    {
        _scripting = new IslandScripting(_api, _config,
            accepted => { foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.BuildOperationApplied, accepted); },
            (name, position, text) =>
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var target in _players.Values.Where(x => x.Authenticated && MathF.Abs(x.State.X - position.X) <= 30f && MathF.Abs(x.State.Z - position.Z) <= 30f))
                    _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.ReceiveChat, 0, name, text, now);
            },
            (userId, text) =>
            {
                foreach (var target in _players.Values.Where(x => x.Authenticated && x.UserId == userId))
                    _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.ReceiveChat, 0, "Script", text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            },
            Log);
        try { await _scripting.RefreshAsync(ct); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { Log("scripts_load_failed", new { error = exception.Message }); }
        _ = ScriptSyncLoopAsync(ct);
    }

    private async Task ScriptSyncLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                ticks++;
                try
                {
                    await _scripting!.RefreshAsync(ct);
                    if (ticks % 2 == 0) await _scripting.FlushStateAsync(ct);
                }
                catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException) { Log("scripts_sync_failed", new { error = exception.Message }); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public void TouchObject(int peerId, string objectId)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated || !Guid.TryParse(objectId, out var id)) return;
        _scripting?.Touch(PresenceOf(player), id);
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
        _scripting?.OnChat(PresenceOf(sender), message);
    }

    /// Shared checks for every terrain action: authenticated, allowed to edit terrain, within the per-second budget.
    private bool TryBeginTerrainAction(int peerId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ConnectedPlayer? actor)
    {
        actor = null;
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return false;
        if (!player.CanEditTerrain) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "no_permission"); return false; }
        var now = DateTimeOffset.UtcNow;
        while (player.TerrainTimes.Count > 0 && now - player.TerrainTimes.Peek() > TimeSpan.FromSeconds(1)) player.TerrainTimes.Dequeue();
        if (player.TerrainTimes.Count >= NetworkProtocol.MaxTerrainEditsPerSecond) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "rate_limit"); return false; }
        player.TerrainTimes.Enqueue(now);
        actor = player;
        return true;
    }

    public void SubmitTerrainEdit(int peerId, int tool, float x, float z, float radius, float strength, int layer)
    {
        if (!TryBeginTerrainAction(peerId, out var actor)) return;
        if (!Enum.IsDefined((TerrainTool)tool) || layer is < 0 or >= TerrainMap.LayerCount || !float.IsFinite(x) || !float.IsFinite(z)
            || Math.Abs(x) > _terrain.HalfExtent + TerrainMap.MaxBrushRadius || Math.Abs(z) > _terrain.HalfExtent + TerrainMap.MaxBrushRadius)
        { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "invalid_edit"); return; }
        // The accepted values are the clamped ones, so every client replays exactly what the server applied.
        radius = Math.Clamp(radius, 1f, TerrainMap.MaxBrushRadius); strength = Math.Clamp(strength, 0f, 1f);
        var before = _terrain.Capture(x, z, radius);
        if (!_terrain.Apply((TerrainTool)tool, x, z, radius, strength, layer)) return;
        _terrainDirty = true;
        RememberStroke(actor, before);
        foreach (var target in _players.Values.Where(p => p.Authenticated))
            _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.TerrainEdited, tool, x, z, radius, strength, layer, _terrain.Revision);
    }

    /// Edits made within a short time by the same player form one stroke, so a single undo reverts a whole drag.
    private static void RememberStroke(ConnectedPlayer actor, TerrainPatch before)
    {
        var now = DateTimeOffset.UtcNow;
        if (actor.UndoStack.Count > 0 && now - actor.LastStrokeAt < TimeSpan.FromMilliseconds(700) && actor.RedoStack.Count == 0) actor.UndoStack[^1].MergeKeepingOldest(before);
        else { actor.UndoStack.Add(before); actor.RedoStack.Clear(); if (actor.UndoStack.Count > 50) actor.UndoStack.RemoveAt(0); }
        actor.LastStrokeAt = now;
    }

    public void SubmitSeaLevel(int peerId, float level)
    {
        if (!TryBeginTerrainAction(peerId, out _)) return;
        if (!_terrain.SetSeaLevel(level)) return;
        _terrainDirty = true;
        foreach (var target in _players.Values.Where(p => p.Authenticated))
            _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.SeaLevelChanged, _terrain.SeaLevel, _terrain.Revision);
    }

    public void UndoTerrain(int peerId) => StepHistory(peerId, undo: true);
    public void RedoTerrain(int peerId) => StepHistory(peerId, undo: false);

    private void StepHistory(int peerId, bool undo)
    {
        if (!TryBeginTerrainAction(peerId, out var actor)) return;
        var from = undo ? actor.UndoStack : actor.RedoStack;
        var to = undo ? actor.RedoStack : actor.UndoStack;
        if (from.Count == 0) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, undo ? "nothing_to_undo" : "nothing_to_redo"); return; }
        var patch = from[^1]; from.RemoveAt(from.Count - 1);
        to.Add(_terrain.Restore(patch));
        _terrainDirty = true;
        actor.LastStrokeAt = DateTimeOffset.MinValue;
        BroadcastPatch(patch);
    }

    /// Sends the vertices that changed; very large patches (such as undoing an import) go out as a full map instead.
    private void BroadcastPatch(TerrainPatch patch)
    {
        foreach (var target in _players.Values.Where(p => p.Authenticated))
        {
            if (patch.Count > 4000) SendTerrain(target);
            else _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.TerrainPatched, patch.Serialize(), _terrain.Revision);
        }
    }

    public void SubmitTerrainImport(int peerId, byte[] heights)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        if (heights.Length > NetworkProtocol.MaxTerrainImportBytes || DateTimeOffset.UtcNow - player.LastTerrainImport < TimeSpan.FromSeconds(5)) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "rate_limit"); return; }
        if (!TryBeginTerrainAction(peerId, out var actor)) return;
        actor.LastTerrainImport = DateTimeOffset.UtcNow;
        var undo = _terrain.ImportHeights16(heights);
        if (undo is null) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "invalid_heightmap"); return; }
        if (undo.Count == 0) return;
        _terrainDirty = true;
        actor.UndoStack.Add(undo); actor.RedoStack.Clear(); if (actor.UndoStack.Count > 20) actor.UndoStack.RemoveAt(0);
        foreach (var target in _players.Values.Where(p => p.Authenticated)) SendTerrain(target);
    }

    public void RequestTerrain(int peerId)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        var now = DateTimeOffset.UtcNow;
        if (now - player.LastTerrainRequest < TimeSpan.FromSeconds(2)) return;
        player.LastTerrainRequest = now;
        SendTerrain(player);
    }

    private void SendTerrain(ConnectedPlayer player) => _bridge.RpcId(player.PeerId, NetworkBridge.MethodName.TerrainFull, _terrain.Serialize(), player.CanEditTerrain);


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
            _scripting?.OnObjectApplied(accepted);
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
                    YawOf(x), x.Motion, x.LastProcessedInput)).ToArray();
            _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.Snapshot, SnapshotCodec.Encode(_serverTick, visible));
        }
        BroadcastVehicles();
    }

    private void SendSpawn(int targetPeer, ConnectedPlayer player) => _bridge.RpcId(targetPeer, NetworkBridge.MethodName.SpawnPlayer,
        player.PeerId, player.AvatarId.ToString(), player.DisplayName, player.AppearanceJson, player.State.X, player.State.Y, player.State.Z, (int)player.Motion);
    private void Reject(int peerId, string code) { _bridge.RpcId(peerId, NetworkBridge.MethodName.AuthResult, false, code, (long)_serverTick); Multiplayer.MultiplayerPeer.DisconnectPeer(peerId); }
    private static float DistanceSquared(AuthoritativePlayerState a, AuthoritativePlayerState b) => (a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z);
    private static bool ContainsBlockedTerm(string message) => (System.Environment.GetEnvironmentVariable("LIFEVERSE_CHAT_BLOCKED_TERMS") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static bool ValidBuildCommand(BuildObjectCommand command) { var values = new[] { command.PositionX, command.PositionY, command.PositionZ, command.RotationX, command.RotationY, command.RotationZ, command.ScaleX, command.ScaleY, command.ScaleZ }; return new[] { "cube", "sphere", "cylinder", "cone", "plane", "prism", "tube", "ring", "pyramid", "mesh" }.Contains(command.PrimitiveType, StringComparer.OrdinalIgnoreCase) && values.All(float.IsFinite) && new[] { command.ScaleX, command.ScaleY, command.ScaleZ }.All(x => x is >= .01f and <= 64f) && (command.GeometryJson?.Length ?? 0) <= 16384 && (command.MaterialJson?.Length ?? 0) <= 32768; }

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
        _terrain = TerrainMap.FromJson(_terrainJson);
        Log("world_snapshot_loaded", new { snapshot.Version, snapshot.IntegrityHash });
    }

    private async Task SaveWorldSnapshotAsync(CancellationToken ct)
    {
        try
        {
            if (_terrainDirty) { _terrainJson = _terrain.ToJson(); _terrainDirty = false; }
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
                var ready = _registered && _ready;
                var body = JsonSerializer.Serialize(new { status = ready ? "healthy" : "starting", ready, instanceId = _config.InstanceId });
                var bytes = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {(ready ? "200 OK" : "503 Service Unavailable")}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
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
        try { if (_scripting is not null) await _scripting.FlushStateAsync(CancellationToken.None); } catch (Exception exception) { Log("script_state_flush_failed", new { error = exception.Message }); }
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
        public Queue<DateTimeOffset> TerrainTimes { get; } = new();
        public bool CanEditTerrain { get; set; }
        public DateTimeOffset LastTerrainRequest { get; set; }
        public DateTimeOffset LastTerrainImport { get; set; }
        public DateTimeOffset LastStrokeAt { get; set; }
        public List<TerrainPatch> UndoStack { get; } = [];
        public List<TerrainPatch> RedoStack { get; } = [];
    }
    private sealed record SuspendedPlayer(ConnectedPlayer Player, DateTimeOffset ExpiresAt);
    private sealed record AdmissionResponse(Guid UserId, Guid AvatarId, Guid IslandId, Guid SessionId, Guid TokenId,
        int ProtocolVersion, DateTimeOffset ExpiresAt, string DisplayName, string AppearanceJson, bool CanEditTerrain = false);
    private sealed record WorldSnapshotResponse(Guid IslandId, long Version, string TerrainJson, string EnvironmentJson, string IntegrityHash);
    private sealed record SnapshotWriteResponse(long Version);
    private sealed record BuildCommand(string Operation, Guid? ObjectId, BuildObjectCommand? Object, int ExpectedRevision);
    private sealed record BuildObjectCommand(string PrimitiveType, float PositionX, float PositionY, float PositionZ, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ, string? MaterialJson, string? PhysicsJson, Guid? GroupId, int ExpectedRevision = 0, string? GeometryJson = null, string? Name = null, string? Description = null, Guid? ParentObjectId = null, string? PermissionsJson = null, string? ContentJson = null);
}
