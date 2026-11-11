using System.Net.Http.Json;
using Godot;
using LifeVerse.Shared.Platform;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>The API side of the heartbeat: tells the API who is connected and how the server is doing, and carries out what an administrator asked (remove a person, tell people something).</summary>
public partial class ServerMain
{
    private readonly ulong _startedAtMs = Time.GetTicksMsec();

    private ServerReport BuildReport() => new(
        [.. _players.Values.Where(p => p.Authenticated && p.UserId != Guid.Empty).Select(p => new PlayerReport(p.UserId, p.DisplayName, p.ConnectedAt.ToUnixTimeMilliseconds(), p.State.X, p.State.Y, p.State.Z))],
        Engine.GetFramesPerSecond(), Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0, Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024.0 * 1024.0), (long)((Time.GetTicksMsec() - _startedAtMs) / 1000), Engine.PhysicsTicksPerSecond);

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        using var response = await _api.PutAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/heartbeat", new { status = "healthy", players = _players.Values.Count(x => x.Authenticated), report = BuildReport() }, ServerChannelJson.Options, ct);
        if (!response.IsSuccessStatusCode) { Log("heartbeat_failed", new { status = (int)response.StatusCode }); return; }
        if (response.StatusCode != System.Net.HttpStatusCode.OK) return; // 204: nothing was asked
        var commands = ServerChannelJson.ParseCommands(await response.Content.ReadAsStringAsync(ct));
        if (commands.Count > 0) ApplyCommands(commands);
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
                    var peer = target.PeerId;
                    SendNotice(peer, string.IsNullOrWhiteSpace(command.Text) ? "Você foi removido da ilha por um administrador." : $"Você foi removido da ilha: {command.Text}");
                    GetTree().CreateTimer(.6).Timeout += () => { if (_players.ContainsKey(peer)) Multiplayer.MultiplayerPeer.DisconnectPeer(peer); }; // after the notice has had time to arrive
                    Log("player_kicked", new { peer, command.UserId });
                }
                else if (command.Kind == ServerCommandKinds.Shutdown)
                {
                    if (_stopping) continue;
                    foreach (var target in _players.Values.Where(x => x.Authenticated)) SendNotice(target.PeerId, "A ilha está reiniciando agora. Entre de novo em um instante.");
                    Log("restart_requested", new { command.Text });
                    GetTree().CreateTimer(1.5).Timeout += () => _ = StopAsync(); // after the notice has had time to arrive; StopAsync saves the island and its people, then the API starts it again
                }
                else if (command.Kind == ServerCommandKinds.Notice)
                {
                    foreach (var target in _players.Values.Where(x => x.Authenticated && (command.UserId is null || x.UserId == command.UserId))) SendNotice(target.PeerId, command.Text);
                    Log("notice_sent", new { command.UserId });
                }
            }
            catch (Exception exception) { Log("command_failed", new { command.Kind, error = exception.Message }); }
        }
    }

    private void SendNotice(int peerId, string text) => _bridge.RpcId(peerId, NetworkBridge.MethodName.ReceiveChat, 0, "Aviso", text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
