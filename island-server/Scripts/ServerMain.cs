using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Land;
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
    private readonly EditLocks _locks = new();
    private DateTimeOffset _lastLockSweep = DateTimeOffset.UtcNow;
    private readonly Dictionary<Guid, SuspendedPlayer> _suspended = [];
    private readonly HashSet<Guid> _usedTickets = [];
    private uint _serverTick;
    private double _snapshotAccumulator;
    private long _worldSnapshotVersion;
    private string _terrainJson = "{}";
    private TerrainMap _terrain = new();
    private LandAccessMap _access = LandAccessMap.Empty;
    private LandTerrainRights _rights = LandTerrainRights.Empty;
    private long _landCursor;
    private readonly Dictionary<Guid, DateTimeOffset> _lastPushLog = [];
    private bool _terrainDirty;
    private string _environmentJson = "{}";
    private double _worldSaveAccumulator;
    private IslandScripting? _scripting;
    private ServerPhysics? _physics;
    private int _physicsObjectsVersion = -1;
    private double _physicsCheck;
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
            _ = LandSyncLoopAsync(_shutdown.Token);
            _ready = true;
            Log("server_started", new { _config.InstanceId, _config.IslandId, _config.GamePort, _config.HealthPort, enet = "listening", _config.ProtocolVersion });
        }
        catch (Exception exception)
        {
            Log("server_start_failed", new { error = exception.Message });
            GetTree().Quit(1);
        }
    }


    /// The simulation of physical objects: reads the objects again when they changed, keeps the ground in step with the terrain and lets walkers push what they touch.
    private void TickPhysics(double delta)
    {
        if (_physics is null || _scripting is null) return;
        _physicsCheck += delta;
        if (_physicsCheck >= .5)
        {
            _physicsCheck = 0;
            _physics.SetTerrain(_terrain);
            if (_scripting.ObjectsVersion != _physicsObjectsVersion) { _physicsObjectsVersion = _scripting.ObjectsVersion; _physics.Sync(_scripting.Bodies()); }
        }
        if (_physics.BodyCount == 0) return;
        foreach (var player in _players.Values.Where(x => x.Authenticated && !x.State.Seated))
            _physics.Push(new Godot.Vector3(player.State.X, player.State.Y, player.State.Z), new Godot.Vector3(player.State.VelocityX, player.State.VelocityY, player.State.VelocityZ));
    }
    public override void _PhysicsProcess(double delta)
    {
        _serverTick++;
        _scripting?.Update(_players.Values.Where(x => x.Authenticated).Select(PresenceOf).ToList());
        TickPhysics(delta);
        CheckIdle();
        SweepLocks();
        var dt = Math.Min((float)delta, 1f / 20f);
        TickVehicles(dt);
        foreach (var player in _players.Values.Where(x => x.Authenticated))

        {
            if (PinDriver(player)) continue;
            player.Motion = AuthoritativeMovement.Step(player.State, player.LatestInput, dt, _terrain.SampleHeight, _terrain.SeaLevel, _terrain.HalfExtent + TerrainMap.OceanMargin, _scripting?.Obstacles);
            if (_access.HasZones) KeepOutOfClosedLand(player);
            player.LastProcessedInput = player.LatestInput.Sequence;
            player.LatestInput = player.LatestInput with
            {
                Flags = player.LatestInput.Flags & (InputFlags.Run | InputFlags.Descend)
            };
        }
        _snapshotAccumulator += delta;
        _worldSaveAccumulator += delta;
        TickLocations(delta);
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
        _lastTeleport.Remove((int)peerId); _lastAppearance.Remove((int)peerId);
        if (!_players.Remove((int)peerId, out var player)) return;
        if (player.Authenticated)
        {
            _ = SaveLocationsAsync([player], CancellationToken.None); // so signing in again brings them back to this spot
            BroadcastUnlock(_locks.ReleaseAll(player.UserId), player.UserId);
            if (!player.Released) _suspended[player.AvatarId] = new SuspendedPlayer(player, DateTimeOffset.UtcNow.AddSeconds(10)); // someone released by a trip does not come back on their own
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
            else PlaceArriving(player, admission); // where they were last time on this island, or around its spawn point
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
            SendLocks(peerId);
            if (_scripting is not null) foreach (var text in _scripting.TextOps()) _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationApplied, text); // the floating texts that scripts put on objects
            if (_physics is not null) foreach (var pose in _physics.PoseMessages()) _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationApplied, pose); // where the bodies of the simulation are now
            Log("peer_authenticated", new { peerId, admission.UserId, admission.AvatarId });
            if (admission.TransferId is { } trip) _ = ConfirmArrivalAsync(peerId, trip, _shutdown.Token); // a person who travelled: the API completes the trip only when this island says they are here
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

    /// Reliable and idempotent because flight is a state, not a per-frame input.
    /// A repeated packet leaves the same result instead of toggling twice.
    public void SetFlight(int peerId, bool enabled)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated || !player.State.CanFly || player.State.Seated) return;
        player.State.Flying = enabled;
        if (!enabled) return;
        player.State.Swimming = false;
        player.State.VelocityY = Math.Max(player.State.VelocityY, 2f);
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
        _physics = new ServerPhysics { Name = "ServerPhysics" }; AddChild(_physics);
        _physics.Initialize(message => { foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.BuildOperationApplied, message); });
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
        // island editors may sculpt anywhere; people who own, rent or build on parcels may sculpt inside them (see SubmitTerrainEdit)
        if (!player.CanEditTerrain && !_rights.HasAny(player.UserId)) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "no_permission"); return false; }
        var now = DateTimeOffset.UtcNow;
        while (player.TerrainTimes.Count > 0 && now - player.TerrainTimes.Peek() > TimeSpan.FromSeconds(1)) player.TerrainTimes.Dequeue();
        if (player.TerrainTimes.Count >= NetworkProtocol.MaxTerrainEditsPerSecond) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "rate_limit"); return false; }
        player.TerrainTimes.Enqueue(now);
        actor = player;
        return true;
    }

    public void SubmitTerrainEdit(int peerId, int tool, float x, float z, float radius, float strength, int packedLayer)
    {
        if (!TryBeginTerrainAction(peerId, out var actor)) return;
        // the layer number and the brush options (shape, edge, height to level to) arrive together in one integer
        if (!BrushCodec.TryUnpack(packedLayer, out var layer, out var brush)) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "invalid_edit"); return; }
        if (!Enum.IsDefined((TerrainTool)tool) || !float.IsFinite(x) || !float.IsFinite(z)
            || Math.Abs(x) > _terrain.HalfExtent + TerrainMap.MaxBrushRadius || Math.Abs(z) > _terrain.HalfExtent + TerrainMap.MaxBrushRadius)
        { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "invalid_edit"); return; }
        // The accepted values are the clamped ones, so every client replays exactly what the server applied.
        radius = Math.Clamp(radius, 1f, TerrainMap.MaxBrushRadius); strength = Math.Clamp(strength, 0f, 1f);
        // people without island-wide rights change only the ground their parcels own, and nothing that touches a neighbour's boundary
        var mask = actor.CanEditTerrain ? null : _rights.MaskFor(actor.UserId, _terrain);
        var before = _terrain.Capture(x, z, radius, brush.Square, mask);
        if (!_terrain.Apply((TerrainTool)tool, x, z, radius, strength, layer, brush, mask)) return;
        _terrainDirty = true;
        RememberStroke(actor, before);
        if (mask is null && brush.IsDefault && (TerrainTool)tool <= TerrainTool.Noise)
        {
            // the plain case: everyone replays the same edit
            foreach (var target in _players.Values.Where(p => p.Authenticated))
                _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.TerrainEdited, tool, x, z, radius, strength, layer, _terrain.Revision);
        }
        else BroadcastPatch(_terrain.Capture(x, z, radius, brush.Square, mask)); // masked or new kinds of edit: everyone gets the vertices that actually changed
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
        if (!TryBeginTerrainAction(peerId, out var seaActor)) return;
        if (!seaActor.CanEditTerrain) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "no_permission"); return; } // the sea belongs to island editors
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
        if (!actor.CanEditTerrain) { _bridge.RpcId(peerId, NetworkBridge.MethodName.TerrainEditRejected, "no_permission"); return; } // importing a whole map is for island editors
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
        if (command is null || command.Operation is not ("create" or "update" or "delete" or "return" or "rez" or "structure" or "protect" or "properties" or "lock" or "unlock") || (command.Object is not null && !ValidBuildCommand(command.Object)) || (command.ObjectIds?.Length ?? 0) > 500) { _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationRejected, "invalid_operation"); return; }
        actor.BuildTimes.Enqueue(now);
        if (command.Operation is "lock" or "unlock") { HandleLock(actor, command, now); return; }

        // someone else is editing it: the island refuses before the API is even asked
        var targets = command.Operation is "create" or "rez" ? [] : command.Operation == "structure" ? (command.Links?.Select(l => l.ObjectId).ToArray() ?? []) : (command.ObjectIds ?? (command.ObjectId is { } one ? [one] : []));
        if (_locks.FirstHeldByOther(targets, actor.UserId, now) is { } held) { _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationRejected, $"locked:{NameOf(held.Holder)}"); return; }
        try
        {
            using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/build-operations", new { userId = actor.UserId, operation = command.Operation, objectId = command.ObjectId, @object = command.Object, expectedRevision = command.ExpectedRevision, objectIds = command.ObjectIds, packageItemId = command.PackageItemId, links = command.Links, locked = command.Locked, properties = command.Properties, originX = command.OriginX, originY = command.OriginY, originZ = command.OriginZ }, _shutdown.Token);
            var accepted = await response.Content.ReadAsStringAsync(_shutdown.Token);
            if (!response.IsSuccessStatusCode) { _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationRejected, ErrorCodeOf(accepted) ?? (response.StatusCode == System.Net.HttpStatusCode.Conflict ? "revision_conflict" : $"server_rejected_{(int)response.StatusCode}")); return; }
            if (command.Operation == "return") { AnnounceReturned(accepted); return; }
            _scripting?.OnObjectApplied(accepted);
            foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.BuildOperationApplied, accepted);
            if (command.Operation == "delete") BroadcastUnlock(_locks.Release(targets, actor.UserId), actor.UserId);
            else if (targets.Length > 0 && _locks.HolderOf(targets[0], now) == actor.UserId) _locks.TryAcquire(targets, actor.UserId, now, out _); // working on it keeps the lease alive
        }
        catch (Exception exception) { Log("build_operation_failed", new { peerId, error = exception.Message }); _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationRejected, "service_unavailable"); }
    }

    /// <summary>The short "code" the API puts in a refusal, so the player gets a precise message instead of a status number.</summary>
    private static string? ErrorCodeOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && code.GetString() is { Length: > 0 and <= 40 } text && text.All(c => char.IsLetterOrDigit(c) || c == '_') ? text : null;
        }
        catch (JsonException) { return null; }
    }

    private string NameOf(Guid userId) => _players.Values.FirstOrDefault(x => x.Authenticated && x.UserId == userId)?.DisplayName is { Length: > 0 } name ? name : "outro jogador";

    private void Broadcast(object message) { var json = JsonSerializer.Serialize(message); foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.BuildOperationApplied, json); }

    private void HandleLock(ConnectedPlayer actor, BuildCommand command, DateTimeOffset now)
    {
        var ids = (command.ObjectIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) return;
        if (command.Operation == "unlock") { BroadcastUnlock(_locks.Release(ids, actor.UserId), actor.UserId); return; }
        if (!_locks.TryAcquire(ids, actor.UserId, now, out var holder)) { _bridge.RpcId(actor.PeerId, NetworkBridge.MethodName.BuildOperationRejected, holder == actor.UserId ? "lock_limit" : $"locked:{NameOf(holder)}"); return; }
        Broadcast(new { operation = "lock", objectIds = ids, userId = actor.UserId, userName = actor.DisplayName, expiresAtMs = (now + EditLocks.Lease).ToUnixTimeMilliseconds() });
    }

    private void BroadcastUnlock(IReadOnlyList<Guid> freed, Guid userId)
    {
        if (freed.Count > 0) Broadcast(new { operation = "unlock", objectIds = freed, userId });
    }

    private void SweepLocks()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastLockSweep < TimeSpan.FromSeconds(1)) return;
        _lastLockSweep = now;
        foreach (var byUser in _locks.Expire(now).GroupBy(x => x.User)) BroadcastUnlock(byUser.Select(x => x.Object).ToList(), byUser.Key);
    }

    /// <summary>A newcomer learns which objects are being edited right now.</summary>
    private void SendLocks(int peerId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var byUser in _locks.Snapshot(now).GroupBy(x => x.User))
            _bridge.RpcId(peerId, NetworkBridge.MethodName.BuildOperationApplied, JsonSerializer.Serialize(new { operation = "lock", objectIds = byUser.Select(x => x.Object).ToArray(), userId = byUser.Key, userName = NameOf(byUser.Key), expiresAtMs = byUser.Max(x => x.Expires).ToUnixTimeMilliseconds() }));
    }

    /// <summary>The API returned objects to their owners' inventories: everyone sees them leave, as an ordinary delete.</summary>
    private void AnnounceReturned(string accepted)
    {
        try
        {
            using var document = JsonDocument.Parse(accepted);
            if (document.RootElement.TryGetProperty("healed", out var healed) && healed.ValueKind == JsonValueKind.Array && healed.GetArrayLength() > 0)
            {
                var message = JsonSerializer.Serialize(new { operation = "structure", objects = healed }); // what hung under the returned objects was re-hung: everybody sees the new tree
                _scripting?.OnObjectApplied(message);
                foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.BuildOperationApplied, message);
            }
            if (!document.RootElement.TryGetProperty("objectIds", out var ids)) return;
            foreach (var id in ids.EnumerateArray())
            {
                if (!Guid.TryParse(id.GetString(), out var objectId)) continue;
                var message = JsonSerializer.Serialize(new { operation = "delete", objectId, returned = true });
                _scripting?.OnObjectApplied(message);
                foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.BuildOperationApplied, message);
                foreach (var holder in _locks.Snapshot(DateTimeOffset.UtcNow).Where(x => x.Object == objectId)) BroadcastUnlock(_locks.Release([objectId], holder.User), holder.User);
            }
        }
        catch (JsonException) { }
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
    private static bool ValidBuildCommand(BuildObjectCommand command) { var values = new[] { command.PositionX, command.PositionY, command.PositionZ, command.RotationX, command.RotationY, command.RotationZ, command.ScaleX, command.ScaleY, command.ScaleZ }; return PrimitiveCatalog.IsKnown(command.PrimitiveType) && values.All(float.IsFinite) && new[] { command.PivotX, command.PivotY, command.PivotZ }.All(p => p is null || (float.IsFinite(p.Value) && Math.Abs(p.Value) <= 64f)) && new[] { command.ScaleX, command.ScaleY, command.ScaleZ }.All(x => x is >= .01f and <= 64f) && (command.GeometryJson?.Length ?? 0) <= 16384 && (command.MaterialJson?.Length ?? 0) <= 32768; }

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

    /// <summary>
    /// A player standing where they are not allowed (a closed parcel, a ban) is moved to the nearest allowed spot, whatever got them there:
    /// walking, flying, a teleport, a reconnection or a ban that arrived while they were inside. The client is never asked.
    /// </summary>
    private void KeepOutOfClosedLand(ConnectedPlayer player)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var state = player.State;
        if (!_access.Denies(player.UserId, state.X, state.Z, nowMs)) return;
        if (_access.NearestAllowed(player.UserId, state.X, state.Z, nowMs) is not { } spot) return;
        state.X = (float)spot.X; state.Z = (float)spot.Z; state.Y = _terrain.SampleHeight(state.X, state.Z) + AuthoritativeMovement.GroundOffset;
        state.VelocityX = state.VelocityY = state.VelocityZ = 0; state.Flying = false; state.Seated = false;
        var now = DateTimeOffset.UtcNow;
        if (!_lastPushLog.TryGetValue(player.UserId, out var last) || now - last > TimeSpan.FromSeconds(10)) { _lastPushLog[player.UserId] = now; Log("land_access_pushed", new { player.UserId, x = state.X, z = state.Z }); }
    }

    /// <summary>Every few seconds: who is kept out of which parcel, and what the land system did to objects behind our back (returned, handed over), told to everyone as ordinary build messages.</summary>
    private async Task LandSyncLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try { do { await SyncLandAsync(ct); } while (await timer.WaitForNextTickAsync(ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { Log("land_sync_loop_failed", new { error = exception.Message }); }
    }

    private async Task SyncLandAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _api.GetAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/land-sync?since={_landCursor}", ct);
            if (!response.IsSuccessStatusCode) return;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            if (root.TryGetProperty("access", out var access)) _access = LandAccessMap.Parse(access);
            if (root.TryGetProperty("terrain", out var terrain)) _rights = LandTerrainRights.Parse(terrain);
            if (root.TryGetProperty("teleport", out var teleport)) _teleports = LandTeleport.Parse(teleport);
            if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
                foreach (var item in events.EnumerateArray())
                {
                    var message = item.GetRawText();
                    _scripting?.OnObjectApplied(message);
                    foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.BuildOperationApplied, message);
                }
            if (root.TryGetProperty("cursor", out var cursor) && cursor.TryGetInt64(out var value)) _landCursor = value;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Log("land_sync_failed", new { error = exception.Message }); }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                using var response = await _api.PutAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/heartbeat", new { status = "healthy", players = _players.Values.Count(x => x.Authenticated) }, ct);
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
        await SaveLocationsAsync([.. _players.Values.Where(x => x.Authenticated)], CancellationToken.None);
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
        public bool Released { get; set; }
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
        int ProtocolVersion, DateTimeOffset ExpiresAt, string DisplayName, string AppearanceJson, bool CanEditTerrain = false,
        float SpawnX = 0, float SpawnZ = 0, int IslandSize = 256, float? ResumeX = null, float? ResumeY = null, float? ResumeZ = null, Guid? TransferId = null);
    private sealed record WorldSnapshotResponse(Guid IslandId, long Version, string TerrainJson, string EnvironmentJson, string IntegrityHash);
    private sealed record SnapshotWriteResponse(long Version);
    private sealed record StructureLink(Guid ObjectId, Guid? GroupId, Guid? ParentId, int ExpectedRevision);
    private sealed record BuildCommand(string Operation, Guid? ObjectId, BuildObjectCommand? Object, int ExpectedRevision, Guid[]? ObjectIds = null, Guid? PackageItemId = null, float OriginX = 0, float OriginY = 0, float OriginZ = 0, StructureLink[]? Links = null, bool? Locked = null, JsonElement? Properties = null);
    private sealed record BuildObjectCommand(string PrimitiveType, float PositionX, float PositionY, float PositionZ, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ, string? MaterialJson, string? PhysicsJson, Guid? GroupId, int ExpectedRevision = 0, string? GeometryJson = null, string? Name = null, string? Description = null, Guid? ParentObjectId = null, string? PermissionsJson = null, string? ContentJson = null, float? PivotX = null, float? PivotY = null, float? PivotZ = null);
}
