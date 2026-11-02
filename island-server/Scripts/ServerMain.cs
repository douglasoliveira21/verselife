using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Godot;

namespace LifeVerse.IslandServer.Scripts;

public partial class ServerMain : Node
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Net.Http.HttpClient _api = new() { Timeout = TimeSpan.FromSeconds(10) };
    private ServerConfiguration _config = null!;
    private TcpListener? _healthListener;
    private bool _registered;
    private bool _stopping;

    public override async void _Ready()
    {
        try
        {
            _config = ServerConfiguration.FromEnvironment();
            _api.DefaultRequestHeaders.Add("X-LifeVerse-Service-Key", _config.ServiceKey);
            _healthListener = new TcpListener(IPAddress.Loopback, _config.HealthPort);
            _healthListener.Start();
            _ = ServeHealthAsync(_shutdown.Token);
            await RegisterAsync(_shutdown.Token);
            _ = HeartbeatLoopAsync(_shutdown.Token);
            // ENet is deliberately not opened in Phase 01. A future authenticated
            // admission layer must validate the temporary island token first.
            Log("server_started", new { _config.InstanceId, _config.IslandId, _config.GamePort, _config.HealthPort, enet = "prepared_not_listening" });
        }
        catch (Exception exception)
        {
            Log("server_start_failed", new { error = exception.Message });
            GetTree().Quit(1);
        }
    }

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
}
