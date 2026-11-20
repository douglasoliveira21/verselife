using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LifeVerse.Shared.Loading;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Platform;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Vehicles;
using LifeVerse.Shared.Voice;
using LifeVerse.Wire;

namespace LifeVerse.IslandServer.Core;

/// <summary>Os tempos do servidor. Os padrões são os do servidor Godot; os testes os encurtam.</summary>
public sealed record IslandServerOptions(double HeartbeatSeconds = 15, double LocationSeconds = 20, double WorldSaveSeconds = 60,
    double SuspendSeconds = 10, double VehicleSyncSeconds = 15, double LandSyncSeconds = 10, double ScriptSyncSeconds = 15, double AuthenticationSeconds = 5, double DisconnectDelaySeconds = .25);

/// <summary>O que o servidor mede de si mesmo (vai para o health check, o heartbeat e os testes de carga).</summary>
public sealed record IslandServerStats(int Players, long Ticks, double TickAverageMs, double TickP99Ms, double TickMaxMs, double TickRate,
    long SnapshotsSent, long SnapshotBytes, long MessagesReceived, long TickFailures, double UptimeSeconds);

/// <summary>
/// O servidor de ilha sem engine: admissão pela API, simulação autoritativa (a mesma AuthoritativeMovement do shared), snapshots por interesse,
/// chat, voo, gestos, persistência (mundo e posições), registro, heartbeat, comandos do admin, saúde e métricas.
/// Tudo acontece em <see cref="Update"/>, na thread de quem o chama; as tarefas de rede só deixam recados na caixa de entrada.
/// </summary>
public sealed partial class IslandServer : IDisposable
{
    private sealed class Player(int peerId)
    {
        public int PeerId { get; } = peerId;
        public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;
        public bool Admitting, Authenticated, Released, CanEditTerrain;
        public Guid UserId, AvatarId, SessionId;
        public string DisplayName = string.Empty, AppearanceJson = "{}";
        public AuthoritativePlayerState State = new();
        public MovementInput LatestInput;
        public uint LastProcessedInput;
        public NetworkMotionState Motion;
        public readonly Queue<DateTimeOffset> ChatTimes = new();
        public DateTimeOffset? LastGesture, LastHeadLook;
        public readonly Queue<DateTimeOffset> TerrainTimes = new(), BuildTimes = new();
        public DateTimeOffset? LastPushLog, LastTeleport, LastAppearance, LastAttachmentLive;
        public bool LateAppearance;
        public string? WornJson; public HashSet<Guid> WornIds = new();
        public DateTimeOffset LastTerrainRequest, LastTerrainImport, LastStrokeAt;
        public readonly List<TerrainPatch> UndoStack = new(), RedoStack = new();
    }

    private sealed record Suspended(Player Player, double ExpiresAt);

    private readonly IslandServerConfig _config;
    private readonly IslandServerOptions _options;
    private readonly IIslandApi _api;
    private readonly IslandLog _log;
    private readonly WireServer _wire;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<Action> _inbox = new();
    private readonly List<(double At, Action Run)> _scheduled = new();
    private readonly Dictionary<int, Player> _players = new();
    private readonly Dictionary<Guid, Suspended> _suspended = new();
    private readonly HashSet<Guid> _usedTickets = new();
    private readonly List<PlayerSnapshot> _visible = new();
    private readonly Queue<double> _tickMs = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly string[] _blockedTerms;
    private readonly VehicleFleet _fleet = new();
    private readonly VoiceRelay _voice = new();
    private TerrainMap _terrain = new();
    private string _terrainJson = "{}", _environmentJson = "{}";
    private long _worldVersion;
    private bool _terrainDirty, _registered, _ready, _stopping, _stopped, _stopRequested;
    private uint _serverTick;
    private double _time, _snapshotAccumulator, _locationAccumulator, _worldSaveAccumulator;
    private double? _emptySince;
    private TcpListener? _health;
    private Task? _healthTask, _heartbeatTask, _stopTask, _vehicleTask, _landTask, _scriptTask;
    private int _playerCount;
    private readonly object _tickGate = new();
    private bool _wireClosed;
    private long _ticks, _snapshotsSent, _snapshotBytes, _messages, _tickFailures;
    private double _tickMaxMs, _tickWindowStart;
    private long _tickWindowCount;
    private double _achievedRate;

    public IslandServer(IslandServerConfig config, IIslandApi api, IslandLog? log = null, IslandServerOptions? options = null)
    {
        _config = config; _api = api; _log = log ?? new IslandLog(); _options = options ?? new IslandServerOptions();
        _blockedTerms = (Environment.GetEnvironmentVariable("LIFEVERSE_CHAT_BLOCKED_TERMS") ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        _wire = new WireServer(config.MaxPlayers, options?.AuthenticationSeconds ?? 5);
        _wire.PeerConnected += OnPeerConnected;
        _wire.PeerDisconnected += OnPeerDisconnected;
        _wire.MessageReceived += OnMessage;
        _wire.PeerRejected += (peer, reason) => _log.Write("peer_rejected", new { peer, reason });
    }

    public bool Ready => _ready && _registered && !_stopping;
    public bool StopRequested => _stopRequested;
    public int GamePort => _wire.Port;
    public int HealthPort => _health?.LocalEndpoint is IPEndPoint endpoint ? endpoint.Port : 0;
    public int PlayerCount => Volatile.Read(ref _playerCount);
    public IslandServerConfig Config => _config;

    public IslandServerStats Stats()
    {
        double[] window;
        lock (_tickMs) window = _tickMs.ToArray();
        Array.Sort(window);
        var average = window.Length == 0 ? 0 : window.Average();
        var p99 = window.Length == 0 ? 0 : window[Math.Min(window.Length - 1, (int)(window.Length * .99))];
        return new IslandServerStats(PlayerCount, Interlocked.Read(ref _ticks), average, p99, _tickMaxMs, _achievedRate, Interlocked.Read(ref _snapshotsSent),
            Interlocked.Read(ref _snapshotBytes), Interlocked.Read(ref _messages), Interlocked.Read(ref _tickFailures), _uptime.Elapsed.TotalSeconds);
    }

    // ---- life cycle ----

    /// <summary>Saúde primeiro (ainda "starting"), depois registro, mundo, porta de jogo e heartbeat: a mesma ordem do servidor Godot.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        try
        {
            _health = new TcpListener(_config.LoopbackOnly ? IPAddress.Loopback : IPAddress.Any, _config.HealthPort);
            _health.Start();
            _healthTask = ServeHealthAsync(_health);
            await _api.RegisterAsync(_config, ct).ConfigureAwait(false);
            _registered = true;
            _log.Write("server_registered", new { _config.InstanceId });
            var snapshot = await _api.LoadWorldAsync(ct).ConfigureAwait(false);
            _worldVersion = snapshot.Version; _terrainJson = snapshot.TerrainJson; _environmentJson = snapshot.EnvironmentJson;
            _terrain = TerrainMap.FromJson(_terrainJson);
            _log.Write("world_snapshot_loaded", new { snapshot.Version, snapshot.IntegrityHash });
            if (!_wire.Start(_config.GamePort, _config.LoopbackOnly)) throw new InvalidOperationException($"LiteNetLib failed to listen on {_config.GamePort}.");
            await StartScriptingAsync(ct).ConfigureAwait(false);
            _heartbeatTask = HeartbeatLoopAsync();
            _vehicleTask = VehicleSyncLoopAsync();
            _landTask = LandSyncLoopAsync();
            _ready = true;
            _log.Write("server_started", new { _config.InstanceId, _config.IslandId, gamePort = _wire.Port, healthPort = HealthPort, wire = WireProtocol.Version, ticket = _config.TicketProtocolVersion, engine = "wire" });
        }
        catch (Exception exception)
        {
            _log.Write("server_start_failed", new { error = exception.Message });
            _stopping = true; // a porta de saúde que já abriu não deve reclamar ao fechar
            try { _health?.Stop(); } catch (SocketException) { }
            throw;
        }
    }

    /// <summary>O laço do servidor (para o console e os testes): um passo a cada 1/TickRate, até <paramref name="ct"/> ou até um pedido de parada; depois salva e encerra.</summary>
    public Task RunAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        var watch = Stopwatch.StartNew(); var step = 1.0 / _config.TickRate; var next = 0.0; var last = 0.0;
        while (!ct.IsCancellationRequested && !_stopRequested)
        {
            var now = watch.Elapsed.TotalSeconds;
            if (now < next) { if (next - now > .002) Thread.Sleep(1); else Thread.Yield(); continue; }
            var delta = now - last; last = now; next += step; if (now > next + 1) next = now;
            Update((float)delta);
        }
        await StopAsync().ConfigureAwait(false);
    });

    /// <summary>Um passo do servidor. O Unity o chama de FixedUpdate; o console, de RunAsync.</summary>
    public void Update(float delta)
    {
        if (_stopped) return;
        var started = Stopwatch.GetTimestamp();
        try { lock (_tickGate) { if (_wireClosed) return; Step(delta); } }
        catch (Exception exception) { Interlocked.Increment(ref _tickFailures); _log.Write("tick_failed", new { error = exception.Message, exception.StackTrace }); }
        var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        lock (_tickMs) { _tickMs.Enqueue(ms); if (_tickMs.Count > 600) _tickMs.Dequeue(); }
        if (ms > _tickMaxMs) _tickMaxMs = ms;
        _tickWindowCount++;
        if (_time - _tickWindowStart >= 1) { _achievedRate = _tickWindowCount / (_time - _tickWindowStart); _tickWindowStart = _time; _tickWindowCount = 0; }
        Interlocked.Increment(ref _ticks);
    }

    private void Step(float delta)
    {
        _time += delta; _serverTick++;
        _wire.Poll();
        while (_inbox.TryDequeue(out var work)) work();
        for (var i = _scheduled.Count - 1; i >= 0; i--)
            if (_scheduled[i].At <= _time) { var run = _scheduled[i].Run; _scheduled.RemoveAt(i); run(); }
        if (!_ready || _stopping) return;
        var dt = Math.Min(delta, 1f / 20f);
        SweepLocks();
        _scripting?.Update(_players.Values.Where(x => x.Authenticated).Select(PresenceOf).ToList());
        TickPhysics(delta);
        _fleet.Tick(dt, _terrain.SampleHeight, _terrain.SeaLevel, _terrain.HalfExtent + TerrainMap.OceanMargin, _scripting?.Now ?? 0, (car, by, arrived) => _scripting?.VehicleArrived(car, by, arrived));
        _scripting?.Npcs?.Tick(_scripting.Now, dt);
        foreach (var player in _players.Values)
        {
            if (!player.Authenticated) continue;
            if (PinDriver(player)) continue;
            player.Motion = AuthoritativeMovement.Step(player.State, player.LatestInput, dt, _terrain.SampleHeight, _terrain.SeaLevel, _terrain.HalfExtent + TerrainMap.OceanMargin, _scripting?.Obstacles);
            ApplyScriptGesture(player);
            if (_access.HasZones) KeepOutOfClosedLand(player);
            player.LastProcessedInput = player.LatestInput.Sequence;
            player.LatestInput = player.LatestInput with { Flags = player.LatestInput.Flags & (InputFlags.Run | InputFlags.Descend) };
        }
        _snapshotAccumulator += delta; _locationAccumulator += delta; _worldSaveAccumulator += delta;
        if (_snapshotAccumulator >= 1.0 / _config.SnapshotRate) { _snapshotAccumulator = 0; BroadcastSnapshots(); BroadcastVehicles(); }
        if (_locationAccumulator >= _options.LocationSeconds) { _locationAccumulator = 0; SaveLocations(_players.Values.Where(p => p.Authenticated).ToList()); }
        if (_worldSaveAccumulator >= _options.WorldSaveSeconds) { _worldSaveAccumulator = 0; if (_terrainDirty) _ = SaveWorldAsync(_cts.Token); }
        foreach (var avatar in _suspended.Where(x => x.Value.ExpiresAt <= _time).Select(x => x.Key).ToList()) _suspended.Remove(avatar);
        CheckIdle();
    }

    private void CheckIdle()
    {
        if (_config.IdleShutdownMinutes <= 0 || _stopping) return;
        if (_players.Values.Any(x => x.Authenticated || x.Admitting) || _suspended.Count > 0) { _emptySince = null; return; }
        _emptySince ??= _time;
        if (_time - _emptySince < _config.IdleShutdownMinutes * 60.0) return;
        _log.Write("idle_shutdown", new { minutes = _config.IdleShutdownMinutes });
        _stopRequested = true;
    }

    /// <summary>Salva o mundo e quem está jogando, fecha a porta e se retira da API. Só roda uma vez.</summary>
    public void RequestStop() => _stopRequested = true;

    public Task StopAsync() => _stopTask ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        _stopping = true; _stopRequested = true;
        _cts.Cancel();
        try { if (_scripting is not null) await _scripting.ShutdownAsync().ConfigureAwait(false); }
        catch (Exception exception) { _log.Write("script_state_flush_failed", new { error = exception.Message }); }
        try { if (_terrainDirty) await SaveWorldAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { _log.Write("world_snapshot_save_failed", new { error = exception.Message }); }
        await SaveLocationsAsync(_players.Values.Where(p => p.Authenticated).ToList(), CancellationToken.None).ConfigureAwait(false);
        // o último passo da simulação termina antes de fechar a porta: o LiteNetLib não aceita Poll e Stop ao mesmo tempo
        lock (_tickGate)
        {
            foreach (var peer in _players.Keys.ToList()) _wire.Disconnect(peer);
            _wire.Poll();
            _wire.Dispose();
            _wireClosed = true;
            _physics?.Dispose();
        }
        try { _health?.Stop(); } catch (SocketException) { }
        if (_registered)
        {
            try { await _api.DeregisterAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { _log.Write("server_deregister_failed", new { error = exception.Message }); }
        }
        _log.Write("server_stopped", new { _config.InstanceId });
        _stopped = true;
    }

    public void Dispose()
    {
        _stopping = true;
        _cts.Cancel();
        if (!_stopped) { lock (_tickGate) { if (!_wireClosed) { _wireClosed = true; try { _wire.Dispose(); } catch { } } } try { _health?.Stop(); } catch { } }
        _api.Dispose();
    }

    // ---- connections ----

    private void OnPeerConnected(int peerId)
    {
        _players[peerId] = new Player(peerId);
        _log.Write("peer_connected", new { peerId });
    }

    private void OnPeerDisconnected(int peerId, WireDisconnectReason reason)
    {
        _fleet.Release(peerId);
        _voice.Forget(peerId); _scriptGestures.Remove(peerId);
        if (!_players.Remove(peerId, out var player)) return;
        if (player.Authenticated)
        {
            Interlocked.Decrement(ref _playerCount);
            BroadcastUnlock(_locks.ReleaseAll(player.UserId), player.UserId);
            _ = SaveLocationsAsync(new[] { player }, CancellationToken.None);
            if (!player.Released) _suspended[player.AvatarId] = new Suspended(player, _time + _options.SuspendSeconds); // quem sai em viagem não volta sozinho
            foreach (var other in _players.Values.Where(x => x.Authenticated)) _wire.Send(other.PeerId, new DespawnPlayerMessage(peerId, true));
        }
        _log.Write("peer_disconnected", new { peerId, reason = reason.ToString(), player.AvatarId });
    }

    private void OnMessage(int peerId, IWireMessage message)
    {
        Interlocked.Increment(ref _messages);
        if (!_players.TryGetValue(peerId, out var player)) return;
        if (message is AuthenticateMessage authenticate) { OnAuthenticate(player, authenticate); return; }
        if (!player.Authenticated) return;
        switch (message)
        {
            case SubmitInputMessage input: OnInput(player, input); break;
            case SetFlightMessage flight: OnFlight(player, flight.Enabled); break;
            case SendChatMessage chat: OnChat(player, chat.Message); break;
            case TouchObjectMessage touch: OnTouch(player, touch.ObjectId); break;
            case ScriptDialogResponseMessage dialog: OnScriptDialog(player, dialog.Json); break;
            case RefreshAppearanceMessage: OnRefreshAppearance(player); break;
            case AttachmentLiveMessage live: OnAttachmentLive(player, live); break;
            case SubmitVoiceMessage voice: OnVoice(player, voice); break;
            case GestureMessage gesture: OnGesture(player, gesture.GestureId); break;
            case HeadLookMessage look: OnHeadLook(player, look.Pitch); break;
            case RequestTerrainMessage: OnRequestTerrain(player); break;
            case SubmitTerrainEditMessage edit: OnTerrainEdit(player, edit); break;
            case SubmitSeaLevelMessage sea: OnSeaLevel(player, sea.Level); break;
            case UndoTerrainMessage: StepHistory(player, true); break;
            case RedoTerrainMessage: StepHistory(player, false); break;
            case SubmitTerrainImportMessage import: OnTerrainImport(player, import.Heights); break;
            case SubmitBuildOperationMessage build: OnBuildOperation(player, build.OperationJson); break;
            case EnterVehicleMessage enter: OnEnterVehicle(player, enter.VehicleId); break;
            case ExitVehicleMessage: OnExitVehicle(player); break;
            case SubmitVehicleInputMessage drive: if (float.IsFinite(drive.Throttle) && float.IsFinite(drive.Steer)) _fleet.SetInput(peerId, drive.Throttle, drive.Steer, drive.Brake); break;
            case RequestTeleportMessage teleport: OnTeleport(player, teleport); break;
            default: break; // voz, anexos, aparência, toque e diálogos de scripts: ainda sem equivalente (ver MIGRATION_PROGRESS.md, Fase 6)
        }
    }

    // ---- admission ----

    private void OnAuthenticate(Player player, AuthenticateMessage message)
    {
        if (player.Authenticated || player.Admitting || _stopping) return;
        if (message.ProtocolVersion != WireProtocol.Version)
        {
            _wire.Send(player.PeerId, new ProtocolRejectedMessage(WireProtocol.Version));
            DisconnectSoon(player.PeerId); return;
        }
        player.Admitting = true;
        _ = AdmitAsync(player, message.Token);
    }

    private async Task AdmitAsync(Player player, string token)
    {
        try
        {
            var admission = await _api.ValidateTicketAsync(token, _config.IslandId, _cts.Token).ConfigureAwait(false);
            _inbox.Enqueue(() => CompleteAdmission(player, admission));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log.Write("admission_failed", new { player.PeerId, error = exception.Message });
            _inbox.Enqueue(() => Reject(player, "admission_unavailable"));
        }
        catch (OperationCanceledException) { }
    }

    private void CompleteAdmission(Player player, Admission? admission)
    {
        if (!_players.TryGetValue(player.PeerId, out var current) || !ReferenceEquals(current, player) || _stopping) return;
        if (admission is null) { Reject(player, "invalid_ticket"); return; }
        if (admission.ProtocolVersion != _config.TicketProtocolVersion || !_usedTickets.Add(admission.TokenId)) { Reject(player, "ticket_reused_or_invalid"); return; }
        if (_suspended.Remove(admission.AvatarId, out var suspended)) player.State = suspended.Player.State;
        else PlaceArriving(player, admission); // onde estava da última vez nesta ilha, ou ao redor do ponto de entrada
        player.CanEditTerrain = admission.CanEditTerrain;
        player.Admitting = false; player.Authenticated = true;
        player.UserId = admission.UserId; player.AvatarId = admission.AvatarId; player.SessionId = admission.SessionId;
        player.DisplayName = admission.DisplayName; player.AppearanceJson = admission.AppearanceJson;
        _wire.MarkAuthenticated(player.PeerId);
        Interlocked.Increment(ref _playerCount);
        foreach (var existing in _players.Values.Where(x => x.Authenticated && x.PeerId != player.PeerId))
        {
            SendSpawn(player.PeerId, existing);
            SendSpawn(existing.PeerId, player);
        }
        SendSpawn(player.PeerId, player);
        _wire.Send(player.PeerId, new AuthResultMessage(true, "ok", _serverTick));
        SendTerrain(player);
        foreach (var vehicle in _fleet.Vehicles) SendVehicle(player.PeerId, vehicle);
        SendLocks(player.PeerId);
        SendScriptState(player.PeerId);
        SendPhysicsState(player.PeerId);
        _log.Write("peer_authenticated", new { player.PeerId, admission.UserId, admission.AvatarId });
        if (admission.TransferId is { } trip) _ = ConfirmArrivalAsync(player.PeerId, trip); // quem viajou: a API só conclui a viagem quando esta ilha diz que a pessoa chegou
    }

    private void Reject(Player player, string code)
    {
        player.Admitting = false;
        _wire.Send(player.PeerId, new AuthResultMessage(false, code, _serverTick));
        DisconnectSoon(player.PeerId);
        _log.Write("admission_rejected", new { player.PeerId, code });
    }

    private void PlaceArriving(Player player, Admission admission)
    {
        var slot = _players.Values.Count(x => x.Authenticated);
        (float X, float Z)? resume = admission.ResumeX is { } rx && admission.ResumeZ is { } rz ? (rx, rz) : null;
        var spot = SpawnPlanner.Choose(admission.SpawnX, admission.SpawnZ, slot, resume, admission.IslandSize);
        var ground = _terrain.SampleHeight(spot.X, spot.Z) + AuthoritativeMovement.GroundOffset;
        player.State.X = spot.X; player.State.Z = spot.Z;
        player.State.Y = resume is not null && Math.Abs(spot.X - resume.Value.X) < .001f ? SpawnPlanner.Height(ground, admission.ResumeY) : ground;
    }

    private async Task ConfirmArrivalAsync(int peerId, Guid transferId)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var status = await _api.ConfirmArrivalAsync(transferId, _cts.Token).ConfigureAwait(false);
                if (status is >= 200 and < 300) { _log.Write("traveller_arrival_confirmed", new { peerId, transferId }); return; }
                if (status is 404 or 409)
                {
                    _log.Write("traveller_arrival_refused", new { peerId, transferId, status });
                    _inbox.Enqueue(() => { if (_players.TryGetValue(peerId, out var player)) { player.Released = true; _wire.Disconnect(peerId); } });
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception) { _log.Write("traveller_arrival_failed", new { peerId, transferId, attempt, error = exception.Message }); }
            try { await Task.Delay(1000, _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }
    }

    // ---- gameplay ----

    private const InputFlags AllowedFlags = InputFlags.Run | InputFlags.Jump | InputFlags.Fly | InputFlags.Descend | InputFlags.Sit | InputFlags.Wave | InputFlags.Dance | InputFlags.Typing;

    private static void OnInput(Player player, SubmitInputMessage input)
    {
        if (input.Sequence <= player.LatestInput.Sequence || !float.IsFinite(input.X) || !float.IsFinite(input.Z)) return;
        // the client steps 60 times a second and the island uses only the latest input at each of its ticks: a sit toggle or a jump sent in a step that the next input replaced before the tick would be lost, so they are carried over
        var carried = player.LatestInput.Flags & (InputFlags.Sit | InputFlags.Jump);
        player.LatestInput = new MovementInput(input.Sequence, Math.Clamp(input.X, -1f, 1f), Math.Clamp(input.Z, -1f, 1f), ((InputFlags)input.Flags & AllowedFlags) | carried);
    }

    /// <summary>Confiável e idempotente: voar é um estado, não uma entrada por quadro.</summary>
    private static void OnFlight(Player player, bool enabled)
    {
        if (!player.State.CanFly || player.State.Seated) return;
        player.State.Flying = enabled;
        if (!enabled) return;
        player.State.Swimming = false;
        player.State.VelocityY = Math.Max(player.State.VelocityY, 2f);
    }

    private void OnChat(Player sender, string text)
    {
        var now = DateTimeOffset.UtcNow;
        while (sender.ChatTimes.Count > 0 && now - sender.ChatTimes.Peek() > TimeSpan.FromSeconds(10)) sender.ChatTimes.Dequeue();
        var message = new string(text.Trim().Where(c => !char.IsControl(c)).Take(NetworkProtocol.MaxChatCharacters).ToArray());
        if (message.Length == 0 || sender.ChatTimes.Count >= 5 || _blockedTerms.Any(t => message.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) return;
        sender.ChatTimes.Enqueue(now);
        foreach (var target in _players.Values.Where(x => x.Authenticated && DistanceSquared(sender, x) <= 30f * 30f))
            _wire.Send(target.PeerId, new ReceiveChatMessage(sender.PeerId, sender.DisplayName, message, now.ToUnixTimeMilliseconds()));
        _scripting?.OnChat(PresenceOf(sender), message);
    }

    private void OnGesture(Player player, int gestureId)
    {
        var now = DateTimeOffset.UtcNow;
        if (!ExtraGestures.MayRelay(player.LastGesture, now)) return;
        if (!ExtraGestures.IsValid(gestureId)) { _log.Write("gesture_refused", new { player.PeerId, gestureId }); return; }
        player.LastGesture = now;
        foreach (var target in _players.Values.Where(x => x.Authenticated && x.PeerId != player.PeerId && DistanceSquared(player, x) <= _config.InterestRadius * _config.InterestRadius))
            _wire.Send(target.PeerId, new GestureChangedMessage(player.PeerId, gestureId));
    }

    /// <summary>Proximity voice: Opus packets go to the players inside hearing range and nowhere else. The server never decodes the audio; it only checks size and rate, and voice can be off on a parcel.</summary>
    private void OnVoice(Player speaker, SubmitVoiceMessage message)
    {
        if (!_voice.Allow(speaker.PeerId, _uptime.ElapsedMilliseconds, message.Data.Length)) return;
        if (!_landRules.VoiceAllowedAt(speaker.State.X, speaker.State.Z)) return;
        var positions = _players.Values.Where(x => x.Authenticated).Select(x => new VoicePeer(x.PeerId, x.State.X, x.State.Y, x.State.Z)).ToList();
        foreach (var hearer in VoiceRelay.Hearers(speaker.PeerId, positions))
            if (!_players.TryGetValue(hearer, out var listener) || _landRules.VoiceAllowedAt(listener.State.X, listener.State.Z))
                _wire.Send(hearer, new ReceiveVoiceMessage(speaker.PeerId, message.Sequence, message.Data));
    }

    private void OnHeadLook(Player player, float pitch)
    {
        var now = DateTimeOffset.UtcNow;
        if (!HeadLook.MayRelay(player.LastHeadLook, now)) return;
        if (HeadLook.Check(pitch) is { } problem) { _log.Write("head_look_refused", new { player.PeerId, reason = problem }); return; }
        player.LastHeadLook = now;
        var drawn = HeadLook.Drawn(pitch);
        foreach (var target in _players.Values.Where(x => x.Authenticated && x.PeerId != player.PeerId && HeadLook.InRange(MathF.Sqrt(DistanceSquared(player, x)), _config.InterestRadius)))
            _wire.Send(target.PeerId, new HeadLookChangedMessage(player.PeerId, drawn));
    }

    private void BroadcastSnapshots()
    {
        var radius2 = _config.InterestRadius * _config.InterestRadius;
        foreach (var target in _players.Values)
        {
            if (!target.Authenticated) continue;
            _visible.Clear();
            foreach (var other in _players.Values)
            {
                if (!other.Authenticated || DistanceSquared(target, other) > radius2) continue;
                var s = other.State;
                _visible.Add(new PlayerSnapshot(other.PeerId, s.X, s.Y, s.Z, s.VelocityX, s.VelocityY, s.VelocityZ, YawOf(other), other.Motion, other.LastProcessedInput));
            }
            // Um snapshot é um datagrama sem fragmentação (LiteNetLib: ~1020 bytes). Com muita gente por perto ele vai em vários pacotes do MESMO tick;
            // o próprio jogador vai sempre no primeiro, para a confirmação das suas entradas (LastProcessedInput) não depender de um pacote que se perca.
            if (_scripting?.Npcs is { } npcs) _visible.AddRange(npcs.Snapshots(target.State.X, target.State.Y, target.State.Z, _config.InterestRadius));
            var self = _visible.FindIndex(x => x.PeerId == target.PeerId);
            if (self > 0) { var me = _visible[self]; _visible.RemoveAt(self); _visible.Insert(0, me); }
            for (var offset = 0; offset < _visible.Count || offset == 0; offset += SnapshotPlayersPerPacket)
            {
                var payload = SnapshotCodec.Encode(_serverTick, _visible.GetRange(offset, Math.Min(SnapshotPlayersPerPacket, _visible.Count - offset)));
                _wire.Send(target.PeerId, new SnapshotMessage(payload));
                Interlocked.Increment(ref _snapshotsSent); Interlocked.Add(ref _snapshotBytes, payload.Length);
                if (_visible.Count == 0) break;
            }
        }
    }

    /// <summary>6 bytes de cabeçalho + 28 por veículo: 32 veículos = 902 bytes.</summary>
    public const int VehiclesPerPacket = 32;

    /// <summary>6 bytes de cabeçalho + 23 por jogador: 40 jogadores = 926 bytes, abaixo do limite de um pacote do LiteNetLib.</summary>
    public const int SnapshotPlayersPerPacket = 40;

    private void SendSpawn(int targetPeer, Player player) => _wire.Send(targetPeer, new SpawnPlayerMessage(player.PeerId, player.AvatarId.ToString(), player.DisplayName,
        player.AppearanceJson, player.State.X, player.State.Y, player.State.Z, (int)player.Motion));

    private static float DistanceSquared(Player a, Player b)
    {
        var dx = a.State.X - b.State.X; var dy = a.State.Y - b.State.Y; var dz = a.State.Z - b.State.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private void DisconnectSoon(int peerId) => _scheduled.Add((_time + _options.DisconnectDelaySeconds, () => _wire.Disconnect(peerId))); // a recusa precisa de um instante para chegar


    // ---- terrain (the same rules as the Godot server; only island editors may sculpt until land-sync is ported) ----

    private bool TryBeginTerrainAction(Player player)
    {
        // island editors may sculpt anywhere; people who own, rent or build on parcels may sculpt inside them (see OnTerrainEdit)
        if (!player.CanEditTerrain && !_rights.HasAny(player.UserId)) { _wire.Send(player.PeerId, new TerrainEditRejectedMessage("no_permission")); return false; }
        var now = DateTimeOffset.UtcNow;
        while (player.TerrainTimes.Count > 0 && now - player.TerrainTimes.Peek() > TimeSpan.FromSeconds(1)) player.TerrainTimes.Dequeue();
        if (player.TerrainTimes.Count >= NetworkProtocol.MaxTerrainEditsPerSecond) { _wire.Send(player.PeerId, new TerrainEditRejectedMessage("rate_limit")); return false; }
        player.TerrainTimes.Enqueue(now);
        return true;
    }

    private void SendTerrain(Player player) => _wire.Send(player.PeerId, new TerrainFullMessage(_terrain.Serialize(), player.CanEditTerrain));

    private void OnRequestTerrain(Player player)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - player.LastTerrainRequest < TimeSpan.FromSeconds(2)) return;
        player.LastTerrainRequest = now;
        SendTerrain(player);
    }

    private void OnTerrainEdit(Player actor, SubmitTerrainEditMessage edit)
    {
        if (!TryBeginTerrainAction(actor)) return;
        if (!BrushCodec.TryUnpack(edit.Layer, out var layer, out var brush)) { RejectTerrain(actor, "invalid_edit"); return; }
        var limit = _terrain.HalfExtent + TerrainMap.MaxBrushRadius;
        if (!Enum.IsDefined(typeof(TerrainTool), (byte)Math.Clamp(edit.Tool, 0, 255)) || edit.Tool is < 0 or > 255 || !float.IsFinite(edit.X) || !float.IsFinite(edit.Z) || Math.Abs(edit.X) > limit || Math.Abs(edit.Z) > limit)
        { RejectTerrain(actor, "invalid_edit"); return; }
        var radius = Math.Clamp(edit.Radius, 1f, TerrainMap.MaxBrushRadius); var strength = Math.Clamp(edit.Strength, 0f, 1f); // everyone replays the clamped values
        var tool = (TerrainTool)edit.Tool;
        // people without island-wide rights change only the ground their parcels own, and nothing that touches a neighbour's boundary
        var mask = actor.CanEditTerrain ? null : _rights.MaskFor(actor.UserId, _terrain);
        var before = _terrain.Capture(edit.X, edit.Z, radius, brush.Square, mask);
        if (!_terrain.Apply(tool, edit.X, edit.Z, radius, strength, layer, brush, mask)) return;
        _terrainDirty = true; _scripting?.RegionChanged("terrain");
        RememberStroke(actor, before);
        if (mask is null && brush.IsDefault && tool <= TerrainTool.Noise)
            foreach (var target in _players.Values.Where(p => p.Authenticated)) _wire.Send(target.PeerId, new TerrainEditedMessage(edit.Tool, edit.X, edit.Z, radius, strength, edit.Layer, _terrain.Revision));
        else BroadcastPatch(_terrain.Capture(edit.X, edit.Z, radius, brush.Square, mask));
    }

    private void RejectTerrain(Player player, string terrainCode) => _wire.Send(player.PeerId, new TerrainEditRejectedMessage(terrainCode));

    /// <summary>Edits within a short time by the same player form one stroke, so a single undo reverts a whole drag.</summary>
    private static void RememberStroke(Player actor, TerrainPatch before)
    {
        var now = DateTimeOffset.UtcNow;
        if (actor.UndoStack.Count > 0 && now - actor.LastStrokeAt < TimeSpan.FromMilliseconds(700) && actor.RedoStack.Count == 0) actor.UndoStack[actor.UndoStack.Count - 1].MergeKeepingOldest(before);
        else { actor.UndoStack.Add(before); actor.RedoStack.Clear(); if (actor.UndoStack.Count > 50) actor.UndoStack.RemoveAt(0); }
        actor.LastStrokeAt = now;
    }

    private void OnSeaLevel(Player actor, float level)
    {
        if (!TryBeginTerrainAction(actor)) return;
        if (!actor.CanEditTerrain) { RejectTerrain(actor, "no_permission"); return; } // the sea belongs to island editors
        if (!_terrain.SetSeaLevel(level)) return;
        _terrainDirty = true; _scripting?.RegionChanged("sea_level");
        foreach (var target in _players.Values.Where(p => p.Authenticated)) _wire.Send(target.PeerId, new SeaLevelChangedMessage(_terrain.SeaLevel, _terrain.Revision));
    }

    private void StepHistory(Player actor, bool undo)
    {
        if (!TryBeginTerrainAction(actor)) return;
        var from = undo ? actor.UndoStack : actor.RedoStack; var to = undo ? actor.RedoStack : actor.UndoStack;
        if (from.Count == 0) { RejectTerrain(actor, undo ? "nothing_to_undo" : "nothing_to_redo"); return; }
        var patch = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
        to.Add(_terrain.Restore(patch));
        _terrainDirty = true; _scripting?.RegionChanged("terrain"); actor.LastStrokeAt = DateTimeOffset.MinValue;
        BroadcastPatch(patch);
    }

    /// <summary>Sends the vertices that changed; very large patches (such as undoing an import) go out as a full map instead.</summary>
    private void BroadcastPatch(TerrainPatch patch)
    {
        foreach (var target in _players.Values.Where(p => p.Authenticated))
        {
            if (patch.Count > 4000) SendTerrain(target);
            else _wire.Send(target.PeerId, new TerrainPatchedMessage(patch.Serialize(), _terrain.Revision));
        }
    }

    private void OnTerrainImport(Player actor, byte[] heights)
    {
        if (heights.Length > NetworkProtocol.MaxTerrainImportBytes || DateTimeOffset.UtcNow - actor.LastTerrainImport < TimeSpan.FromSeconds(5)) { RejectTerrain(actor, "rate_limit"); return; }
        if (!TryBeginTerrainAction(actor)) return;
        if (!actor.CanEditTerrain) { RejectTerrain(actor, "no_permission"); return; } // importing a whole map is for island editors
        actor.LastTerrainImport = DateTimeOffset.UtcNow;
        var undo = _terrain.ImportHeights16(heights);
        if (undo is null) { RejectTerrain(actor, "invalid_heightmap"); return; }
        if (undo.Count == 0) return;
        _terrainDirty = true; _scripting?.RegionChanged("terrain");
        actor.UndoStack.Add(undo); actor.RedoStack.Clear(); if (actor.UndoStack.Count > 20) actor.UndoStack.RemoveAt(0);
        foreach (var target in _players.Values.Where(p => p.Authenticated)) SendTerrain(target);
    }

    // ---- vehicles (the fleet is simulated here; a driver is pinned to the seat; clients only see snapshots) ----

    private void SendVehicle(int peerId, FleetVehicle vehicle) => _wire.Send(peerId, new VehicleSpawnedMessage(vehicle.Id, vehicle.Name, vehicle.Type, vehicle.State.X, vehicle.State.Y, vehicle.State.Z, vehicle.State.Yaw));

    private bool PinDriver(Player player)
    {
        if (_fleet.VehicleOf(player.PeerId) is not { } car) return false;
        var forwardX = -MathF.Sin(car.State.Yaw); var forwardZ = -MathF.Cos(car.State.Yaw);
        player.State.X = car.State.X; player.State.Y = car.State.Y + VehicleFleet.DriverHeight; player.State.Z = car.State.Z;
        player.State.VelocityX = forwardX * car.State.Speed; player.State.VelocityY = 0; player.State.VelocityZ = forwardZ * car.State.Speed;
        player.State.Flying = false; player.State.Seated = true;
        player.Motion = NetworkMotionState.Sit; player.LastProcessedInput = player.LatestInput.Sequence;
        player.LatestInput = new MovementInput(player.LatestInput.Sequence, 0, 0, InputFlags.None);
        return true;
    }

    /// <summary>The yaw other players see: the car's heading for a driver, the walking direction for everyone else.</summary>
    private float YawOf(Player player) => _fleet.VehicleOf(player.PeerId) is { } car ? -car.State.Yaw : MathF.Atan2(player.State.VelocityX, -player.State.VelocityZ);

    private void BroadcastVehicles()
    {
        if (_fleet.Vehicles.Count == 0) return;
        var all = _fleet.Snapshot();
        for (var offset = 0; offset < all.Count; offset += VehiclesPerPacket)
        {
            var payload = VehicleCodec.Encode(_serverTick, all.Skip(offset).Take(VehiclesPerPacket).ToList());
            foreach (var target in _players.Values.Where(x => x.Authenticated)) _wire.Send(target.PeerId, new VehicleSnapshotsMessage(payload));
        }
    }

    private void OnEnterVehicle(Player player, int vehicleId)
    {
        var result = _fleet.Enter(player.PeerId, vehicleId, player.State.X, player.State.Y, player.State.Z);
        if (result == EnterResult.Ok) { _log.Write("vehicle_entered", new { player.PeerId, vehicleId }); if (_fleet.Get(vehicleId) is { } entered) _scripting?.VehicleSeat(entered, true, PresenceOf(player)); return; }
        _wire.Send(player.PeerId, new VehicleRejectedMessage(result switch { EnterResult.TooFar => "too_far", EnterResult.Occupied => "occupied", EnterResult.AlreadyDriving => "already_driving", _ => "no_vehicle" }));
    }

    private void OnExitVehicle(Player player)
    {
        var left = _fleet.VehicleOf(player.PeerId);
        if (!_fleet.Exit(player.PeerId, _terrain.SampleHeight, out var x, out var y, out var z)) return;
        if (left is not null) _scripting?.VehicleSeat(left, false, PresenceOf(player));
        player.State.X = x; player.State.Y = y + AuthoritativeMovement.GroundOffset; player.State.Z = z;
        player.State.VelocityX = player.State.VelocityY = player.State.VelocityZ = 0; player.State.Seated = false;
        _log.Write("vehicle_exited", new { player.PeerId });
    }

    /// <summary>Adds the vehicles the API knows and this island has not seen yet, and tells everyone connected. Right away and then every 15 s; a failure never stops the island.</summary>
    private async Task VehicleSyncLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var rows = await _api.GetVehiclesAsync(_cts.Token).ConfigureAwait(false);
                    if (rows.Count > 0) _inbox.Enqueue(() => AddVehicles(rows));
                }
                catch (Exception exception) when (exception is not OperationCanceledException) { _log.Write("vehicles_sync_failed", new { error = exception.Message }); }
                await Task.Delay(TimeSpan.FromSeconds(_options.VehicleSyncSeconds), _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void AddVehicles(IReadOnlyList<VehicleRow> rows)
    {
        foreach (var row in rows)
        {
            if (_fleet.Add(row.Id, row.Name, row.VehicleType, row.PositionX, row.PositionZ, row.RotationY, row.MaxSpeed, _terrain.SampleHeight, row.OwnerUserId) is not { } vehicle) continue;
            foreach (var target in _players.Values.Where(x => x.Authenticated)) SendVehicle(target.PeerId, vehicle);
            _log.Write("vehicle_added", new { vehicle.Id, vehicle.Name });
        }
    }
    // ---- persistence ----

    private async Task SaveWorldAsync(CancellationToken ct)
    {
        try
        {
            if (_terrainDirty) { _terrainJson = _terrain.ToJson(); _terrainDirty = false; }
            var text = $"{_config.IslandId}:{_worldVersion}:{_terrainJson}:{_environmentJson}";
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
            var result = await _api.SaveWorldAsync(_worldVersion, _terrainJson, _environmentJson, hash, ct).ConfigureAwait(false);
            if (result.Saved) { _worldVersion = result.Version; _log.Write("world_snapshot_saved", new { _worldVersion }); }
            else if (result.Conflict) _log.Write("world_snapshot_conflict", new { status = 409 });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { _log.Write("world_snapshot_save_failed", new { error = exception.Message }); }
    }

    private void SaveLocations(IReadOnlyList<Player> players) { if (players.Count > 0) _ = SaveLocationsAsync(players, _cts.Token); }

    private async Task SaveLocationsAsync(IReadOnlyList<Player> players, CancellationToken ct)
    {
        try
        {
            var entries = players.Where(p => p.UserId != Guid.Empty).Select(p => new PlayerLocation(p.UserId, p.State.X, p.State.Y, p.State.Z)).ToList();
            if (entries.Count == 0) return;
            var answer = await _api.SaveLocationsAsync(entries, ct).ConfigureAwait(false);
            if (answer is null) { _log.Write("player_locations_refused"); return; }
            if (answer.Release is { Count: > 0 } released) _inbox.Enqueue(() => ReleaseTravellers(released));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { _log.Write("player_locations_failed", new { error = exception.Message }); }
    }

    /// <summary>Quem teve a viagem confirmada em outra ilha sai desta, a menos que tenha voltado depois (uma conexão posterior é outra visita).</summary>
    private void ReleaseTravellers(List<ReleasedEntry> released)
    {
        foreach (var entry in released)
            foreach (var player in _players.Values.Where(p => p.Authenticated && p.UserId == entry.UserId && TravellerRelease.ShouldDrop(p.ConnectedAt, entry.ArrivedAtUnixMs)).ToList())
            {
                player.Released = true;
                _log.Write("traveller_released", new { player.PeerId, player.UserId });
                _wire.Disconnect(player.PeerId);
            }
    }

    // ---- API channel: heartbeat and admin commands ----

    private async Task HeartbeatLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatSeconds), _cts.Token).ConfigureAwait(false);
                try { await SendHeartbeatAsync().ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OperationCanceledException) { _log.Write("heartbeat_failed", new { error = exception.Message }); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SendHeartbeatAsync()
    {
        var stats = Stats();
        var report = new ServerReport(PlayerReports(), stats.TickRate, stats.TickAverageMs, GC.GetTotalMemory(false) / (1024.0 * 1024.0), (long)stats.UptimeSeconds, stats.TickRate, _physics?.BodyCount ?? 0, _physics?.FixedCount ?? 0, _physics?.MaxBodies ?? 0, 0);
        var commands = await _api.HeartbeatAsync(stats.Players, report, _cts.Token).ConfigureAwait(false);
        if (commands.Count > 0) _inbox.Enqueue(() => ApplyCommands(commands));
    }

    private PlayerReport[] PlayerReports()
    {
        // O heartbeat roda fora da thread da simulação: tira uma cópia das posições (leitura de campos float, sem bloqueio) e tolera uma coleção que muda.
        try { return _players.Values.Where(p => p.Authenticated && p.UserId != Guid.Empty).Select(p => new PlayerReport(p.UserId, p.DisplayName, p.ConnectedAt.ToUnixTimeMilliseconds(), p.State.X, p.State.Y, p.State.Z)).ToArray(); }
        catch (InvalidOperationException) { return Array.Empty<PlayerReport>(); }
    }

    private void ApplyCommands(IReadOnlyList<ServerCommand> commands)
    {
        foreach (var command in commands)
        {
            try
            {
                if (command.Kind == ServerCommandKinds.Kick)
                {
                    var target = _players.Values.FirstOrDefault(x => x.Authenticated && x.UserId == command.UserId);
                    if (target is null) continue;
                    SendNotice(target.PeerId, string.IsNullOrWhiteSpace(command.Text) ? "Você foi removido da ilha por um administrador." : $"Você foi removido da ilha: {command.Text}");
                    var peer = target.PeerId;
                    _scheduled.Add((_time + .6, () => { if (_players.ContainsKey(peer)) _wire.Disconnect(peer); }));
                    _log.Write("player_kicked", new { peer, command.UserId });
                }
                else if (command.Kind == ServerCommandKinds.Shutdown)
                {
                    if (_stopping) continue;
                    foreach (var target in _players.Values.Where(x => x.Authenticated)) SendNotice(target.PeerId, "A ilha está reiniciando agora. Entre de novo em um instante.");
                    _log.Write("restart_requested", new { command.Text });
                    _scheduled.Add((_time + 1.5, () => _stopRequested = true)); // StopAsync salva a ilha e as pessoas; depois a API sobe a ilha de novo
                }
                else if (command.Kind == ServerCommandKinds.Notice)
                {
                    foreach (var target in _players.Values.Where(x => x.Authenticated && (command.UserId is null || x.UserId == command.UserId))) SendNotice(target.PeerId, command.Text);
                    _log.Write("notice_sent", new { command.UserId });
                }
            }
            catch (Exception exception) { _log.Write("command_failed", new { command.Kind, error = exception.Message }); }
        }
    }

    private void SendNotice(int peerId, string text) => _wire.Send(peerId, new ReceiveChatMessage(0, "Aviso", text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    // ---- health ----

    private async Task ServeHealthAsync(TcpListener listener)
    {
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                client.ReceiveTimeout = client.SendTimeout = 2000;
                var stats = Stats();
                var ready = Ready;
                var body = JsonSerializer.Serialize(new
                {
                    status = ready ? "healthy" : "starting", ready, instanceId = _config.InstanceId, players = stats.Players, engine = "unity-wire",
                    wire = WireProtocol.Version, tickRate = Math.Round(stats.TickRate, 1), tickMs = Math.Round(stats.TickAverageMs, 3), tickP99Ms = Math.Round(stats.TickP99Ms, 3),
                    uptimeSeconds = (long)stats.UptimeSeconds,
                });
                var bytes = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {(ready ? "200 OK" : "503 Service Unavailable")}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                var stream = client.GetStream();
                // Lê o pedido HTTP antes de responder: fechar o socket com o pedido ainda por ler faz o TCP mandar um RST e o cliente perde a resposta.
                // Uma sonda que só abre a conexão e espera (sem mandar pedido) também recebe a resposta: depois de 300 ms sem nada, responde mesmo assim.
                var request = new byte[2048];
                try
                {
                    var pending = stream.ReadAsync(request, 0, request.Length);
                    if (await Task.WhenAny(pending, Task.Delay(300)).ConfigureAwait(false) == pending) await pending.ConfigureAwait(false);
                }
                catch (IOException) { }
                await stream.WriteAsync(headers, 0, headers.Length).ConfigureAwait(false);
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stopping) { }
        catch (Exception exception) { _log.Write("health_listener_failed", new { error = exception.Message }); }
    }
}
