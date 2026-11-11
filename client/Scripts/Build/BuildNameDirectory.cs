using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Build;

/// <summary>The names of people for the editor (creator, owner, previous owner): looked up once from the profile the API shows to everybody, then remembered. You are always "Você".</summary>
public partial class BuildNameDirectory : Node
{
    private readonly Dictionary<Guid, string> _names = [];
    private readonly Dictionary<Guid, List<Action<string>>> _waiting = [];
    private readonly Queue<Guid> _queue = new();
    private HttpRequest? _http;
    private Guid _current;
    private SessionManager? _session;

    /// <summary>Tests answer for the lookup: the display name of the person, or null.</summary>
    public Func<Guid, string?>? LookupOverride { get; set; }

    public override void _Ready()
    {
        _session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        _http = new HttpRequest { Timeout = 10 }; AddChild(_http); _http.RequestCompleted += OnCompleted;
    }

    /// <summary>The name now if it is known; otherwise a placeholder while it is fetched, and <paramref name="resolved"/> runs with the real name.</summary>
    public string NameOf(Guid id, Action<string>? resolved = null)
    {
        if (id == Guid.Empty) return "—";
        if (_session is not null && id == _session.UserId) return "Você";
        if (_names.TryGetValue(id, out var known)) return known;
        if (resolved is not null) { if (!_waiting.TryGetValue(id, out var list)) _waiting[id] = list = []; list.Add(resolved); }
        if (LookupOverride is not null) { var name = LookupOverride(id) ?? id.ToString()[..8]; _names[id] = name; Flush(id, name); return name; }
        if (!_queue.Contains(id) && _current != id) { _queue.Enqueue(id); Pump(); }
        return "…";
    }

    private void Pump()
    {
        if (_http is null || _current != Guid.Empty || _queue.Count == 0 || _session is not { IsAuthenticated: true }) return;
        _current = _queue.Dequeue();
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); if (app is null) { Fail(); return; }
        if (_http.Request($"{app.ApiBaseUrl}/api/v1/users/{_current}/profile", [$"Authorization: Bearer {_session.AccessToken}"]) != Error.Ok) Fail();
    }

    private void Fail() { var id = _current; _current = Guid.Empty; var name = id.ToString()[..8]; _names[id] = name; Flush(id, name); Pump(); }

    private void OnCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        var id = _current; _current = Guid.Empty; var name = id.ToString()[..8];
        if (responseCode is >= 200 and < 300)
            try { using var document = JsonDocument.Parse(Encoding.UTF8.GetString(body)); if (document.RootElement.TryGetProperty("displayName", out var shown) && shown.GetString() is { Length: > 0 } text) name = text; }
            catch (JsonException) { }
        _names[id] = name; Flush(id, name); Pump();
    }

    private void Flush(Guid id, string name) { if (_waiting.Remove(id, out var list)) foreach (var callback in list) callback(name); }
}
