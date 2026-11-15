using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Settings;

namespace LifeVerse.Client.Scripts.Settings;

public enum SyncResult { Ok, Conflict, Offline, Unauthorized, Invalid }

/// <summary>What the account holds: the revision and the values (already normalised by the store's own definitions when they are applied).</summary>
public sealed record AccountSnapshot(SyncResult Result, int Revision, IReadOnlyDictionary<string, object> Values, string Message = "");

/// <summary>
/// Settings that follow the account (see <see cref="AccountPreferenceRules"/>): fetch, send what changed, and tell the person when another device got there first.
/// Only the ids on the shared list leave this computer; graphics, window, devices and performance limits never do. Nothing is stored here but the revision and whether sync is on.
/// </summary>
public sealed class AccountSync
{
    public delegate void Transport(string method, string path, string? body, Action<int, string> done);

    private readonly SettingsStore _store;
    private readonly Transport _transport;
    private readonly Func<Guid> _user;
    private readonly string _statePath;
    private Guid _stateUser; private bool _on;
    public int Revision { get; private set; }
    public string Status { get; private set; } = "";
    public event Action? Changed;

    public AccountSync(SettingsStore store, string statePath, Transport transport, Func<Guid> user)
    {
        _store = store; _statePath = statePath; _transport = transport; _user = user; Load();
    }

    public bool SignedIn => _user() != Guid.Empty;
    /// <summary>Sync is on for the person who is signed in now (turning it on is per account, so another person on this computer starts with it off).</summary>
    public bool Enabled => _on && SignedIn && _stateUser == _user();

    /// <summary>The ids that follow the account and exist, as available settings, in this client.</summary>
    public static bool IsSynced(string id) => AccountPreferenceRules.IsSynced(id);
    public IEnumerable<string> SyncedIds => _store.Definitions.Where(d => d.IsAvailable && d.IsValueSetting && IsSynced(d.Id)).Select(d => d.Id);

    private void Load()
    {
        if (!Godot.FileAccess.FileExists(_statePath)) return;
        try
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(_statePath));
            var root = document.RootElement;
            _on = root.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
            Revision = root.TryGetProperty("revision", out var r) && r.TryGetInt32(out var n) ? Math.Max(0, n) : 0;
            _stateUser = root.TryGetProperty("user", out var u) && Guid.TryParse(u.GetString(), out var id) ? id : Guid.Empty;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { _on = false; Revision = 0; }
    }

    private void Save()
    {
        using var file = Godot.FileAccess.Open(_statePath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(new { enabled = _on, revision = Revision, user = _stateUser.ToString() }));
    }

    private void Set(bool on, int revision, string status)
    {
        _on = on; Revision = revision; _stateUser = _user(); Status = status; Save(); Changed?.Invoke();
    }

    public void TurnOff() => Set(false, Revision, "Local: as configurações valem só neste dispositivo.");

    // ---- talking to the server -----------------------------------------------------------------------------------------------

    private static SyncResult Classify(int status) => status switch { 200 => SyncResult.Ok, 409 => SyncResult.Conflict, 401 or 403 => SyncResult.Unauthorized, 400 => SyncResult.Invalid, _ => SyncResult.Offline };

    private static string Describe(SyncResult result) => result switch
    {
        SyncResult.Unauthorized => "Entre na conta para sincronizar.",
        SyncResult.Offline => "Sem conexão com o servidor; as alterações ficam só neste dispositivo por enquanto.",
        SyncResult.Invalid => "O servidor recusou algumas configurações.",
        SyncResult.Conflict => "A conta foi alterada em outro dispositivo.",
        _ => ""
    };

    /// <summary>Reads what the account holds. Nothing is applied here.</summary>
    public void Fetch(Action<AccountSnapshot> done)
    {
        if (!SignedIn) { done(new AccountSnapshot(SyncResult.Unauthorized, 0, new Dictionary<string, object>(), Describe(SyncResult.Unauthorized))); return; }
        _transport("GET", "/preferences", null, (status, text) => done(Parse(status, text)));
    }

    private static AccountSnapshot Parse(int status, string text)
    {
        var result = Classify(status); var values = new Dictionary<string, object>(); var revision = 0;
        if (result is SyncResult.Ok or SyncResult.Conflict)
        {
            try
            {
                using var document = JsonDocument.Parse(text); var root = document.RootElement;
                if (root.TryGetProperty("revision", out var r) && r.TryGetInt32(out var n)) revision = n;
                if (root.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object)
                    foreach (var property in v.EnumerateObject())
                        if (AccountPreferenceRules.Find(property.Name) is { } rule && AccountPreferenceRules.CheckValue(rule, property.Value) is { Error: null, Value: { } value }) values[property.Name] = value;
            }
            catch (JsonException) { result = SyncResult.Offline; }
        }
        return new AccountSnapshot(result, revision, values, Describe(result));
    }

    /// <summary>Applies what the account holds to this device (values the store validates once more) and starts following it.</summary>
    public int UseAccount(AccountSnapshot snapshot)
    {
        var count = 0;
        foreach (var (id, value) in snapshot.Values)
        {
            if (_store.Definition(id) is not { IsAvailable: true, IsValueSetting: true }) continue;
            _store.SetAndApply(id, value); count++;
        }
        Set(true, snapshot.Revision, $"Conta: {count} preferência(s) vieram da conta.");
        return count;
    }

    /// <summary>Sends these ids (only the ones on the shared list) with the revision this device last saw.</summary>
    public void Push(IEnumerable<string> ids, Action<AccountSnapshot> done)
    {
        var values = new Dictionary<string, object>();
        foreach (var id in ids.Distinct()) if (IsSynced(id) && _store.Definition(id) is { IsAvailable: true, IsValueSetting: true }) values[id] = _store.Value(id);
        if (values.Count == 0) { done(new AccountSnapshot(SyncResult.Ok, Revision, values)); return; }
        if (!SignedIn) { done(new AccountSnapshot(SyncResult.Unauthorized, Revision, values, Describe(SyncResult.Unauthorized))); return; }
        var body = JsonSerializer.Serialize(new { baseRevision = Revision, values });
        _transport("PUT", "/preferences", body, (status, text) =>
        {
            var answer = Parse(status, text);
            if (answer.Result == SyncResult.Ok) Set(true, answer.Revision, $"Conta: sincronizado (revisão {answer.Revision}).");
            else { Status = answer.Message.Length > 0 ? "Conta: " + answer.Message : Status; Changed?.Invoke(); }
            done(answer);
        });
    }

    /// <summary>Sends every synced setting (turning sync on with this device's values, or replacing the account's).</summary>
    public void PushAll(int baseRevision, Action<AccountSnapshot> done) { Revision = baseRevision; Push(SyncedIds, done); }

    /// <summary>Called after the store applied changes: sends the synced ones when sync is on. Another device's change comes back as a conflict.</summary>
    public void PushApplied(IEnumerable<string> changed, Action<AccountSnapshot> done)
    {
        if (!Enabled) { done(new AccountSnapshot(SyncResult.Ok, Revision, new Dictionary<string, object>())); return; }
        Push(changed, done);
    }

    /// <summary>At login or when the world loads: fetches and applies, if sync is on and nothing here was sent yet.</summary>
    public void PullIfEnabled(Action<AccountSnapshot>? done = null)
    {
        if (!Enabled) return;
        Fetch(snapshot =>
        {
            if (snapshot.Result == SyncResult.Ok && snapshot.Revision != Revision) UseAccount(snapshot);
            else if (snapshot.Result != SyncResult.Ok) { Status = "Conta: " + snapshot.Message; Changed?.Invoke(); }
            done?.Invoke(snapshot);
        });
    }

    // ---- the game's own connection ----------------------------------------------------------------------------------------------

    /// <summary>Requests through the game's HTTP stack, with the session's token. The token is read for each request and never kept.</summary>
    public static void GameTransport(string method, string path, string? body, Action<int, string> done)
    {
        var root = (Engine.GetMainLoop() as SceneTree)?.Root;
        var session = root?.GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = root?.GetNodeOrNull<AppManager>("/root/AppManager");
        if (root is null || session is null || app is null || !session.IsAuthenticated) { done(401, ""); return; }
        var http = new HttpRequest { Timeout = 15 }; root.AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, Encoding.UTF8.GetString(bytes)); };
        var verb = method switch { "PUT" => Godot.HttpClient.Method.Put, "DELETE" => Godot.HttpClient.Method.Delete, _ => Godot.HttpClient.Method.Get };
        if (http.Request(app.ApiBaseUrl + "/api/v1/users/me" + path, ["Content-Type: application/json", $"Authorization: Bearer {session.AccessToken}"], verb, body ?? "") != Error.Ok) { http.QueueFree(); done(0, ""); }
    }
}

/// <summary>The one sync of this game session, tied to the store of <see cref="SettingsService"/>.</summary>
public static class AccountSyncService
{
    private static AccountSync? _sync;
    public static AccountSync Instance => _sync ??= new AccountSync(SettingsService.Store, SettingsService.Root + "_conta.json", AccountSync.GameTransport,
        () => (Engine.GetMainLoop() as SceneTree)?.Root.GetNodeOrNull<SessionManager>("/root/SessionManager")?.UserId ?? Guid.Empty);
}
