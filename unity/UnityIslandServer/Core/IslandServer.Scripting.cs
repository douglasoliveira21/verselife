using System.Text.Json;
using LifeVerse.IslandServer.Scripts;
using LifeVerse.Shared.Networking;
using LifeVerse.Wire;

namespace LifeVerse.IslandServer.Core;

/// <summary>
/// Object scripts. The Lua host and its glue to the island (IslandScripting, in Scripting/) load
/// the island's objects and scripts from the API, turn touches, chat, walking into objects and timers into script events, and apply what scripts ask for
/// through the same build-operation path players use. This file wires that glue to the Wire server: players, NPCs, gestures, vehicles and region changes.
/// Object physics comes through <see cref="IIslandPhysics"/> when the host provides one; without it the scripts that ask for impulse, velocity or contacts get "false"/null, as on an island without bodies.
/// </summary>
public sealed partial class IslandServer
{
    private IslandScripting? _scripting;
    private readonly Dictionary<int, (NetworkMotionState State, string Name, double Until)> _scriptGestures = new();

    private static AvatarPresence PresenceOf(Player player) => new(player.PeerId, player.AvatarId, player.UserId, player.DisplayName, player.State.X, player.State.Y, player.State.Z);

    private void ToAll(string json) { var message = new BuildOperationAppliedMessage(json); foreach (var target in _players.Values.Where(x => x.Authenticated)) _wire.Send(target.PeerId, message); }

    /// <summary>Loads this island's objects and Lua scripts. A failure here must not keep the island from starting.</summary>
    private async Task StartScriptingAsync(CancellationToken ct)
    {
        var scripting = new IslandScripting(_api.Http, ServerConfiguration.From(_config),
            accepted => ToAll(accepted),
            (name, position, text, range) =>
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var target in _players.Values.Where(x => x.Authenticated && MathF.Abs(x.State.X - position.X) <= range && MathF.Abs(x.State.Z - position.Z) <= range))
                    _wire.Send(target.PeerId, new ReceiveChatMessage(0, name, text, now));
            },
            (userId, text) =>
            {
                foreach (var target in _players.Values.Where(x => x.Authenticated && x.UserId == userId))
                    _wire.Send(target.PeerId, new ReceiveChatMessage(0, "Script", text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            },
            (name, properties) => _log.Write(name, properties));
        scripting.SendToAvatar = (avatarId, json) => { foreach (var target in _players.Values.Where(x => x.Authenticated && x.AvatarId == avatarId)) _wire.Send(target.PeerId, new BuildOperationAppliedMessage(json)); };
        scripting.Fleet = _fleet; scripting.SeaLevelOf = () => _terrain.SeaLevel;
        scripting.Npcs = new NpcWorld((x, z) => _terrain.SampleHeight(x, z), () => _terrain.SeaLevel, () => _terrain.HalfExtent, (x, z) => scripting.Obstacles.Blocked(x, _terrain.SampleHeight(x, z), z))
        {
            Spawned = npc => { foreach (var target in _players.Values.Where(x => x.Authenticated)) SendSpawnNpc(target.PeerId, npc); },
            Despawned = id => { foreach (var target in _players.Values.Where(x => x.Authenticated)) _wire.Send(target.PeerId, new DespawnPlayerMessage(id, false)); },
        };
        scripting.AnimationPlayer = PlayScriptGesture; scripting.AnimationStopper = StopScriptGesture;
        if (_physics is { } physics)
        {
            physics.Contacted = scripting.OnContact;
            scripting.PhysicsAction = (id, action, value) => physics.Apply(id, action, value);
            scripting.VelocityOf = id => physics.VelocityOf(id);
        }
        _scripting = scripting;
        try { await scripting.RefreshAsync(ct).ConfigureAwait(false); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) { _log.Write("scripts_load_failed", new { error = exception.Message }); }
        _scriptTask = ScriptSyncLoopAsync();
    }

    private async Task ScriptSyncLoopAsync()
    {
        var ticks = 0;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.ScriptSyncSeconds), _cts.Token).ConfigureAwait(false);
                ticks++;
                try
                {
                    var flush = ticks % 2 == 0;   // inside the step: the re-read changes the walls and the scripts the simulation is using
                    await OnStepAsync(async () => { await _scripting!.RefreshAsync(_cts.Token); if (flush) await _scripting!.FlushStateAsync(_cts.Token); }).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException) { _log.Write("scripts_sync_failed", new { error = exception.Message }); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void SendSpawnNpc(int targetPeer, NpcState npc) => _wire.Send(targetPeer, new SpawnPlayerMessage(npc.PeerId, Guid.Empty.ToString(), npc.Name, JsonSerializer.Serialize(new { DisplayName = npc.Name }), npc.X, npc.Y, npc.Z, (int)npc.Motion));

    private void SendScriptState(int peerId)
    {
        if (_scripting is null) return;
        if (_scripting.Npcs is { } npcs) foreach (var npc in npcs.All) SendSpawnNpc(peerId, npc);
        foreach (var text in _scripting.TextOps()) _wire.Send(peerId, new BuildOperationAppliedMessage(text)); // the floating texts that scripts put on objects
        foreach (var effect in _scripting.EffectOps()) _wire.Send(peerId, new BuildOperationAppliedMessage(effect)); // the particles and lights scripts keep going
        foreach (var stream in _scripting.MediaOps()) _wire.Send(peerId, new BuildOperationAppliedMessage(stream)); // the sound from the internet scripts keep playing
    }

    private void OnTouch(Player player, string objectId)
    {
        if (Guid.TryParse(objectId, out var id)) _scripting?.Touch(PresenceOf(player), id);
    }

    private void OnScriptDialog(Player player, string json)
    {
        if (json.Length <= 2048) _scripting?.OnDialogResponse(PresenceOf(player), json);
    }

    // ---- gestures that scripts make avatars play (wave and dance: the two the network protocol carries) ----

    private bool PlayScriptGesture(Guid avatarId, string name, double seconds)
    {
        var state = name switch { "wave" => NetworkMotionState.Wave, "dance" => NetworkMotionState.Dance, _ => (NetworkMotionState?)null };
        if (state is null || _players.Values.FirstOrDefault(p => p.Authenticated && p.AvatarId == avatarId) is not { } player) return false;
        if (_scriptGestures.TryGetValue(player.PeerId, out var running)) _scripting?.AnimationEnded(avatarId, running.Name, "replaced");
        _scriptGestures[player.PeerId] = (state.Value, name, _time + Math.Clamp(seconds, 1, 15));
        return true;
    }

    private void StopScriptGesture(Guid avatarId)
    {
        if (_players.Values.FirstOrDefault(p => p.Authenticated && p.AvatarId == avatarId) is not { } player || !_scriptGestures.Remove(player.PeerId, out var running)) return;
        _scripting?.AnimationEnded(avatarId, running.Name, "stopped");
    }

    private void ApplyScriptGesture(Player player)
    {
        if (!_scriptGestures.TryGetValue(player.PeerId, out var gesture)) return;
        if (player.Motion != NetworkMotionState.Idle && player.Motion != gesture.State) { _scriptGestures.Remove(player.PeerId); _scripting?.AnimationEnded(player.AvatarId, gesture.Name, "interrupted"); return; }
        if (_time >= gesture.Until) { _scriptGestures.Remove(player.PeerId); _scripting?.AnimationEnded(player.AvatarId, gesture.Name, "finished"); return; }
        player.Motion = gesture.State;
    }
}
