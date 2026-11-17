using System.Text;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Autoloads;

/// <summary>
/// The login of the person. The access token the API gives lasts a few minutes (15); this keeps it alive: shortly before it ends the refresh token is traded for a new pair (POST /auth/refresh), one request at a time
/// (the API changes the refresh token on every use and cancels the whole session if an old one comes back, so two refreshes at once would log the person out). If the API says the session is over, the person is signed out;
/// if the network fails, the token is kept and the refresh tried again a little later.
/// </summary>
public partial class SessionManager : Node
{
    [Signal] public delegate void SessionChangedEventHandler(bool authenticated);
    /// <summary>The API ended the session (the refresh was refused): the person has to sign in again. Not raised by a sign-out the person asked for.</summary>
    [Signal] public delegate void SessionExpiredEventHandler();
    /// <summary>The tokens were replaced by a refresh (no change of who is signed in).</summary>
    [Signal] public delegate void TokensRefreshedEventHandler();

    /// <summary>How long before the end of the access token the refresh starts, and how long to wait to try again after a network failure.</summary>
    public const double RefreshLeadSeconds = 90, RetrySeconds = 15;

    public string AccessToken { get; private set; } = string.Empty;
    public string RefreshToken { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    /// <summary>Account id read from the access token subject; used only to recognise our own replicated actions.</summary>
    public Guid UserId { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);
    /// <summary>When the access token ends (from its own "exp"); MinValue when it cannot be read.</summary>
    public DateTimeOffset AccessExpiresAt { get; private set; } = DateTimeOffset.MinValue;
    public bool Refreshing => _refreshing;

    /// <summary>The clock (tests move it).</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;
    /// <summary>Tests answer the refresh without a network: refresh token → (status, body).</summary>
    public Func<string, (long Status, string Body)>? RefreshTransport { get; set; }

    private bool _refreshing;
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private HttpRequest? _http;

    public void Set(string accessToken, string refreshToken, string username)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        Username = username;
        UserId = ParseSubject(accessToken);
        AccessExpiresAt = ParseExpiry(accessToken);
        _refreshing = false; _notBefore = DateTimeOffset.MinValue;
        EmitSignal(SignalName.SessionChanged, true);
    }

    public void Clear()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        Username = string.Empty;
        UserId = Guid.Empty;
        AccessExpiresAt = DateTimeOffset.MinValue;
        _refreshing = false;
        EmitSignal(SignalName.SessionChanged, false);
    }

    public override void _Process(double delta) => Tick();

    /// <summary>Starts the refresh when the token is about to end (called every frame; cheap).</summary>
    public void Tick()
    {
        if (!IsAuthenticated || _refreshing || string.IsNullOrEmpty(RefreshToken) || AccessExpiresAt == DateTimeOffset.MinValue) return;
        var now = Now(); if (now < _notBefore || AccessExpiresAt - now > TimeSpan.FromSeconds(RefreshLeadSeconds)) return;
        StartRefresh();
    }

    private void StartRefresh()
    {
        _refreshing = true; var sent = RefreshToken;
        if (RefreshTransport is not null) { var (status, body) = RefreshTransport(sent); Finish(sent, status, body); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); if (app is null) { _refreshing = false; return; }
        _http ??= new HttpRequest { Timeout = 20 }; if (_http.GetParent() is null) AddChild(_http);
        void Done(long result, long code, string[] headers, byte[] bytes) { _http!.RequestCompleted -= Done; Finish(sent, result == (long)HttpRequest.Result.Success ? code : 0, Encoding.UTF8.GetString(bytes)); }
        _http.RequestCompleted += Done;
        if (_http.Request(app.ApiBaseUrl + "/api/v1/auth/refresh", ["Content-Type: application/json"], Godot.HttpClient.Method.Post, JsonSerializer.Serialize(new { refreshToken = sent })) != Error.Ok)
        { _http.RequestCompleted -= Done; Finish(sent, 0, string.Empty); }
    }

    /// <summary>The answer of the API to the refresh that was sent with <paramref name="sent"/>.</summary>
    private void Finish(string sent, long status, string body)
    {
        _refreshing = false;
        if (!IsAuthenticated || sent != RefreshToken) return;                                   // signed out or in again meanwhile: this answer is for an old login
        if (status is >= 200 and < 300)
        {
            try
            {
                using var document = JsonDocument.Parse(body); var root = document.RootElement;
                var access = root.GetProperty("accessToken").GetString() ?? string.Empty; var refresh = root.GetProperty("refreshToken").GetString() ?? string.Empty;
                if (access.Length > 0 && refresh.Length > 0)
                {
                    AccessToken = access; RefreshToken = refresh; UserId = ParseSubject(access); AccessExpiresAt = ParseExpiry(access); _notBefore = DateTimeOffset.MinValue;
                    EmitSignal(SignalName.TokensRefreshed); return;
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
            _notBefore = Now().AddSeconds(RetrySeconds); return;                                 // an answer that cannot be read: try again, do not sign out
        }
        if (status is 400 or 401 or 403) { GD.PushWarning("event=session_expired reason=refresh_refused"); Clear(); EmitSignal(SignalName.SessionExpired); return; }      // the API says this session is over
        _notBefore = Now().AddSeconds(RetrySeconds);                                              // no network or a server error: keep the login and try again
    }

    private static JsonElement? Payload(string jwt, out JsonDocument? document)
    {
        document = null;
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            document = JsonDocument.Parse(Convert.FromBase64String(payload)); return document.RootElement;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return null; }
    }

    private static Guid ParseSubject(string jwt)
    {
        var root = Payload(jwt, out var document);
        using (document) return root is { } r && r.TryGetProperty("sub", out var subject) && Guid.TryParse(subject.GetString(), out var id) ? id : Guid.Empty;
    }

    private static DateTimeOffset ParseExpiry(string jwt)
    {
        var root = Payload(jwt, out var document);
        using (document) return root is { } r && r.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue;
    }
}
