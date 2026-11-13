using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Scripting;

public sealed record ScriptHttpRequest(string Url, string? Method, Dictionary<string, string>? Headers, string? Body);
public sealed record ScriptHttpCall(Guid ScriptId, string Url, string? Method, Dictionary<string, string>? Headers, string? Body);
public sealed record ScriptHttpResult(int Status, string Body, string ContentType, bool Truncated, string? Error = null, int Milliseconds = 0);

/// <summary>
/// The only way a script reaches the outside world, and it is not the script that does the reaching: the island asks the API, which checks, connects and answers. Only https to public addresses on the usual ports; the address that is
/// actually connected to is checked at connection time (so a name that points to a private address, or that changes its answer between the check and the connection, gets nowhere); redirects are followed by hand (at most three)
/// and each one is checked again; headers come from a short list; the request and the answer are small; and nothing leaves a trace of the secrets (the console line has no query string and no credentials).
/// </summary>
public sealed class ScriptHttpClient(Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null, HttpMessageHandler? handler = null)
{
    public const int MaxRequestBytes = 8 * 1024, MaxResponseBytes = 32 * 1024, MaxRedirects = 3, TimeoutSeconds = 8, MaxHeaders = 8;
    public static readonly int[] Ports = [443, 8443];
    private static readonly string[] Methods = ["GET", "POST"];
    private static readonly HashSet<string> AllowedHeaders = new(StringComparer.OrdinalIgnoreCase) { "Accept", "Accept-Language", "Content-Type", "Authorization", "User-Agent" };
    private static readonly Regex CustomHeader = new("^X-[A-Za-z0-9-]{1,40}$", RegexOptions.Compiled);
    private static readonly string[] ContentTypes = ["application/json", "text/plain", "text/html", "text/csv", "application/xml", "text/xml", "application/x-www-form-urlencoded"];

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve = resolve ?? (async (host, ct) => await Dns.GetHostAddressesAsync(host, ct));
    private HttpMessageHandler? _handler = handler;

    /// <summary>True for addresses on the public internet; false for loopback, private, link-local (including the cloud metadata address), shared, multicast, reserved and every IPv6 form of those.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None)) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) || (bytes[0] == 198 && bytes[1] is 18 or 19) || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) || bytes[0] >= 224);
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) || (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B) || (bytes[0] == 0x20 && bytes[1] == 0x02));
        return false;
    }

    /// <summary>Why a request must not be made, or null when it may.</summary>
    public static string? Refuse(string? url, string? method, Dictionary<string, string>? headers, string? body)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 1000 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "bad_url";
        if (uri.Scheme != Uri.UriSchemeHttps) return "only_https";
        if (uri.UserInfo.Length > 0) return "no_credentials_in_url";
        if (!Ports.Contains(uri.Port)) return "port_not_allowed";
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) && !IsPublic(literal)) return "private_address";
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !uri.Host.Contains('.') && uri.HostNameType == UriHostNameType.Dns) return "private_address";
        if (!Methods.Contains((method ?? "GET").ToUpperInvariant())) return "bad_method";
        if (body is not null && Encoding.UTF8.GetByteCount(body) > MaxRequestBytes) return "body_too_big";
        if (headers is not null)
        {
            if (headers.Count > MaxHeaders) return "too_many_headers";
            foreach (var (name, value) in headers)
            {
                if (!AllowedHeaders.Contains(name) && !CustomHeader.IsMatch(name)) return "header_not_allowed";
                if (value.Length > 500 || value.Any(c => c is '\r' or '\n')) return "bad_header_value";
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && !ContentTypes.Any(t => value.StartsWith(t, StringComparison.OrdinalIgnoreCase))) return "content_type_not_allowed";
            }
        }
        return null;
    }

    private HttpMessageHandler Handler() => _handler ??= new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(4), PooledConnectionLifetime = TimeSpan.FromSeconds(30),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await _resolve(context.DnsEndPoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a))) throw new HttpRequestException("private_address");   // every answer must be public: no mixing in a private one
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(addresses[0], context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    };

    public async Task<ScriptHttpResult> SendAsync(ScriptHttpRequest request, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        if (Refuse(request.Url, request.Method, request.Headers, request.Body) is { } refused) return new(0, "", "", false, refused);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        using var client = new HttpClient(Handler(), disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        var url = request.Url; var method = (request.Method ?? "GET").ToUpperInvariant(); var body = request.Body;
        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var message = new HttpRequestMessage(new HttpMethod(method), url);
                message.Headers.TryAddWithoutValidation("User-Agent", "LifeVerse-Script/1");
                foreach (var (name, value) in request.Headers ?? []) if (!name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) message.Headers.TryAddWithoutValidation(name, value);
                if (method == "POST") message.Content = new StringContent(body ?? "", Encoding.UTF8, request.Headers?.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value?.Split(';')[0] ?? "application/json");
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
                {
                    var next = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(url), location).ToString();
                    if (Refuse(next, "GET", null, null) is { } blocked) return new(0, "", "", false, "redirect_" + blocked, (int)watch.ElapsedMilliseconds);
                    url = next; if ((int)response.StatusCode is 301 or 302 or 303) { method = "GET"; body = null; }
                    continue;
                }
                var type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (type.Length > 0 && !ContentTypes.Contains(type, StringComparer.OrdinalIgnoreCase) && !type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)) return new((int)response.StatusCode, "", type, false, "content_type_not_allowed", (int)watch.ElapsedMilliseconds);
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                var buffer = new byte[MaxResponseBytes + 1]; var total = 0;
                while (total < buffer.Length) { var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), timeout.Token); if (read == 0) break; total += read; }
                var truncated = total > MaxResponseBytes; return new((int)response.StatusCode, Encoding.UTF8.GetString(buffer, 0, Math.Min(total, MaxResponseBytes)), type, truncated, null, (int)watch.ElapsedMilliseconds);
            }
            return new(0, "", "", false, "too_many_redirects", (int)watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(0, "", "", false, "timeout", (int)watch.ElapsedMilliseconds); }
        catch (HttpRequestException exception) { return new(0, "", "", false, exception.Message == "private_address" || exception.InnerException?.Message == "private_address" ? "private_address" : "connection_failed", (int)watch.ElapsedMilliseconds); }
    }
}

public static partial class ScriptingEndpoints
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Queue<DateTimeOffset>> HttpWindows = new();
    internal static ScriptHttpClient Http { get; set; } = new();
    private const int HttpPerScriptPerMinute = 10, HttpPerIslandPerMinute = 120;

    /// <summary>A script's request to the outside, made on its behalf (island server only, with the service key). Allowed only to scripts whose owner gave them HTTP_REQUESTS; limited per script and per island; the answer is small; and a line without secrets goes into the console of the script.</summary>
    internal static async Task<IResult> ScriptHttpAsync(Guid instanceId, ScriptHttpCall call, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Platform.PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return Results.NotFound();
        var script = await db.ObjectScripts.AsNoTracking().Where(s => s.Id == call.ScriptId && db.BuildObjects.Any(o => o.Id == s.ObjectId && o.IslandId == server.IslandId && o.DeletedAt == null)).Select(s => new { s.Id }).SingleOrDefaultAsync(ct);
        if (script is null) return Results.NotFound();
        if (!await db.ScriptGrants.AsNoTracking().AnyAsync(g => g.ScriptId == call.ScriptId && g.SubjectId == Guid.Empty && g.Permission == "HTTP_REQUESTS", ct)) return Results.Json(new ScriptHttpResult(0, "", "", false, "not_permitted"), statusCode: StatusCodes.Status403Forbidden);
        var now = clock.GetUtcNow();
        if (!Allow(call.ScriptId, HttpPerScriptPerMinute, now) || !Allow(server.IslandId, HttpPerIslandPerMinute, now)) return Results.Json(new ScriptHttpResult(0, "", "", false, "rate_limited"), statusCode: StatusCodes.Status429TooManyRequests);
        var result = await Http.SendAsync(new ScriptHttpRequest(call.Url, call.Method, call.Headers, call.Body), ct);
        var shown = Uri.TryCreate(call.Url, UriKind.Absolute, out var parsed) ? $"{parsed.Scheme}://{parsed.Host}{parsed.AbsolutePath}" : "?";
        db.ScriptLogs.Add(new ScriptLogLine { Id = Guid.NewGuid(), ScriptId = call.ScriptId, Level = result.Error is null ? "info" : "warning", EventName = "http", Text = Clip($"{(call.Method ?? "GET").ToUpperInvariant()} {shown} → {(result.Error ?? result.Status.ToString())} ({result.Milliseconds} ms, {result.Body.Length} bytes)", 500), CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return Results.Ok(result);
    }

    private static bool Allow(Guid key, int most, DateTimeOffset now)
    {
        var queue = HttpWindows.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue) { while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(1)) queue.Dequeue(); if (queue.Count >= most) return false; queue.Enqueue(now); return true; }
    }
}
