using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace LifeVerse.Api.Modules.Social;

/// Live notifications: every signed-in client keeps one WebSocket open and the API pushes small JSON events
/// ({"type":"message","data":{...},"at":"..."}) to it. Events for offline users are simply dropped; the same
/// information is always available through the normal REST endpoints when the client reconnects.
public sealed class NotificationHub(TimeProvider clock, ILogger<NotificationHub> logger)
{
    public const int MaxConnectionsPerUser = 4;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, WebSocket>> _connections = new();

    public int ConnectionCount(Guid userId) => _connections.TryGetValue(userId, out var sockets) ? sockets.Count : 0;

    /// Returns false when the user already has too many connections open.
    public bool TryAdd(Guid userId, Guid connectionId, WebSocket socket)
    {
        var sockets = _connections.GetOrAdd(userId, _ => new());
        if (sockets.Count >= MaxConnectionsPerUser) return false;
        sockets[connectionId] = socket;
        return true;
    }

    public void Remove(Guid userId, Guid connectionId)
    {
        if (!_connections.TryGetValue(userId, out var sockets)) return;
        sockets.TryRemove(connectionId, out _);
        if (sockets.IsEmpty) _connections.TryRemove(userId, out _);
    }

    /// Sends an event to all of the user's open connections. Never throws: a dead socket is dropped.
    public async Task NotifyAsync(Guid userId, string type, object data, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(userId, out var sockets)) return;
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, data, at = clock.GetUtcNow() }, Json));
        foreach (var (id, socket) in sockets.ToArray())
        {
            try
            {
                if (socket.State != WebSocketState.Open) { Remove(userId, id); continue; }
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "Dropping a dead notification socket");
                Remove(userId, id);
            }
        }
    }

    /// Serves one connection until the client leaves. Incoming text is ignored apart from keeping the socket alive.
    public async Task RunAsync(Guid userId, WebSocket socket, CancellationToken ct)
    {
        var connectionId = Guid.NewGuid();
        if (!TryAdd(userId, connectionId, socket))
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Too many connections.", CancellationToken.None);
            return;
        }
        try
        {
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException) { }
        finally { Remove(userId, connectionId); }
    }
}

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/notifications/ws", async (HttpContext context, NotificationHub hub) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest(new { error = "This endpoint speaks WebSocket." });
            var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
            if (!Guid.TryParse(subject, out var userId)) return Results.Unauthorized();
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.RunAsync(userId, socket, context.RequestAborted);
            return Results.Empty;
        }).RequireAuthorization().WithTags("Notifications");
        return endpoints;
    }
}
