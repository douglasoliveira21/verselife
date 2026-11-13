using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>Where a script is in its life. Moves only along <see cref="ScriptLifecycleRules.Allowed"/>; anything else is refused.</summary>
public enum ScriptLifecycle { Created, Loaded, Validated, Running, Waiting, Paused, Suspended, Stopped, Faulted, Destroyed }

public static class ScriptLifecycleRules
{
    private static readonly Dictionary<ScriptLifecycle, ScriptLifecycle[]> Moves = new()
    {
        [ScriptLifecycle.Created] = [ScriptLifecycle.Loaded, ScriptLifecycle.Faulted, ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Loaded] = [ScriptLifecycle.Validated, ScriptLifecycle.Faulted, ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Validated] = [ScriptLifecycle.Waiting, ScriptLifecycle.Running, ScriptLifecycle.Faulted, ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Waiting] = [ScriptLifecycle.Running, ScriptLifecycle.Paused, ScriptLifecycle.Suspended, ScriptLifecycle.Stopped, ScriptLifecycle.Faulted, ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Running] = [ScriptLifecycle.Waiting, ScriptLifecycle.Paused, ScriptLifecycle.Suspended, ScriptLifecycle.Stopped, ScriptLifecycle.Faulted, ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Paused] = [ScriptLifecycle.Waiting, ScriptLifecycle.Stopped, ScriptLifecycle.Suspended, ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Suspended] = [ScriptLifecycle.Stopped, ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Stopped] = [ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Faulted] = [ScriptLifecycle.Stopped, ScriptLifecycle.Destroyed],
        [ScriptLifecycle.Destroyed] = []
    };

    public static bool Allowed(ScriptLifecycle from, ScriptLifecycle to) => from == to || Moves[from].Contains(to);

    /// <summary>The name shown to the player.</summary>
    public static string Describe(ScriptLifecycle state) => state switch
    {
        ScriptLifecycle.Created => "criado", ScriptLifecycle.Loaded => "carregado", ScriptLifecycle.Validated => "validado", ScriptLifecycle.Running => "executando", ScriptLifecycle.Waiting => "aguardando evento",
        ScriptLifecycle.Paused => "pausado", ScriptLifecycle.Suspended => "suspenso por limite", ScriptLifecycle.Stopped => "parado", ScriptLifecycle.Faulted => "com erro", _ => "destruído"
    };
}

/// <summary>Counters of one script, kept while it runs (reset when it is reloaded after an edit).</summary>
public sealed class ScriptMetricsData
{
    public long Events, EventsDropped, Errors, Violations, Suspensions, ApiCalls, Instructions, AllocatedBytes, MessagesSent, TimersFired;
    public double CpuMilliseconds, WorstEventMilliseconds;
    public string? LastError; public int LastErrorLine; public string? LastErrorEvent; public double LastErrorAt;
}

/// <summary>What the editor and the administrator see about a script: its state and its use of the island.</summary>
public sealed record ScriptMetrics(Guid ScriptId, Guid ObjectId, Guid OwnerUserId, string Name, ScriptLifecycle State, string StateText, long Events, long EventsDropped, long Errors, long Violations, long Suspensions,
    long ApiCalls, long Instructions, long AllocatedBytes, long MessagesSent, double CpuMilliseconds, double WorstEventMilliseconds, int ActiveTimers, int StateKeys, string? LastError, int LastErrorLine, string? SuspendedReason);

public enum ScriptLogLevel { Info, Warning, Error }

/// <summary>One line of the script console: what was written (or what went wrong), where and when.</summary>
public sealed record ScriptLogEntry(double At, ScriptLogLevel Level, Guid ScriptId, Guid ObjectId, string ScriptName, string Event, string Text, string? StackTrace = null, double Milliseconds = 0);

/// <summary>A problem found in a script, with where it is.</summary>
public sealed record ScriptDiagnostic(string Severity, int Line, string Message);

public enum ChatKind { Say, Whisper, Shout }

/// <summary>What a script may ask about the island it runs on.</summary>
public sealed record RegionInfo(Guid Id, string Name, int AvatarCount);

/// <summary>The kinds of things a script may do that reach beyond its own object. Each one is granted by a person (the owner of the object, or the avatar affected) and can be taken back at any time.</summary>
public static class ScriptPermission
{
    public const string ControlAnimation = "CONTROL_ANIMATION", ShowHud = "SHOW_HUD", RequestPayment = "REQUEST_PAYMENT", GiveItems = "GIVE_ITEMS", SendMessages = "SEND_MESSAGES", ControlMedia = "CONTROL_MEDIA",
        ControlPhysics = "CONTROL_PHYSICS", ControlAvatar = "CONTROL_AVATAR", HttpRequests = "HTTP_REQUESTS", CreateNpcs = "CREATE_NPCS", RegionInfo = "REGION_INFO", ModifyObject = "MODIFY_OBJECT", ControlVehicles = "CONTROL_VEHICLES";

    public static readonly string[] All = [ModifyObject, ControlPhysics, ControlAnimation, ControlAvatar, ShowHud, RequestPayment, GiveItems, SendMessages, HttpRequests, CreateNpcs, ControlMedia, RegionInfo, ControlVehicles];

    /// <summary>Asked of the avatar that is affected (the script may not act on a person without being allowed by that person).</summary>
    public static readonly string[] OfAvatars = [ControlAnimation, ControlAvatar, RequestPayment, ShowHud, GiveItems, SendMessages];

    public static string Describe(string permission) => permission switch
    {
        ControlAnimation => "controlar as animações do seu avatar", ControlAvatar => "controlar o seu avatar", ShowHud => "mostrar um HUD na sua tela", RequestPayment => "pedir pagamentos em seu nome",
        GiveItems => "entregar itens a você", SendMessages => "enviar mensagens privadas a você", ControlMedia => "controlar a mídia do objeto", ControlPhysics => "controlar a física do objeto",
        HttpRequests => "acessar endereços externos", CreateNpcs => "criar NPCs", RegionInfo => "ler informações da região", ControlVehicles => "dirigir os veículos do dono", _ => "modificar o objeto"
    };

    /// <summary>Given by the owner of the object to the script (in the editor), never asked of avatars: what reaches beyond the object itself but not a person.</summary>
    public static readonly string[] OfOwner = [ControlPhysics, ControlMedia, HttpRequests, CreateNpcs, ControlVehicles];

    public static bool IsKnown(string permission) => All.Contains(permission, StringComparer.Ordinal);
}

/// <summary>
/// Who allowed which script to do what. The script cannot grant itself anything: grants come from people (through the dialog the island shows them) or from the owner (in the editor), are checked on every call
/// and can be revoked at any time, with effect on the next call. Grants made by an avatar are per script and per avatar; those of the owner are per script.
/// </summary>
public sealed class ScriptPermissions
{
    private readonly HashSet<(Guid Script, Guid Subject, string Permission)> _grants = [];
    private readonly Dictionary<(Guid Script, Guid Subject, string Permission), double> _asked = [];

    /// <summary><see cref="Guid.Empty"/> as the subject means the owner of the object (permissions over the object itself).</summary>
    public bool Has(Guid script, Guid subject, string permission) => _grants.Contains((script, subject, permission));
    public void Grant(Guid script, Guid subject, string permission) { if (ScriptPermission.IsKnown(permission)) _grants.Add((script, subject, permission)); }
    public bool Revoke(Guid script, Guid subject, string permission) => _grants.Remove((script, subject, permission));
    public int RevokeAll(Guid script) => _grants.RemoveWhere(g => g.Script == script);
    public int RevokeSubject(Guid subject) => _grants.RemoveWhere(g => g.Subject == subject);
    /// <summary>Makes the owner-level grants (subject empty) exactly those given: what the owner set in the editor is the only source of them.</summary>
    public void ReplaceOwnerGrants(IEnumerable<(Guid Script, string Permission)> given)
    {
        var wanted = given.Where(g => ScriptPermission.OfOwner.Contains(g.Permission, StringComparer.Ordinal)).ToHashSet();
        _grants.RemoveWhere(g => g.Subject == Guid.Empty && !wanted.Contains((g.Script, g.Permission)));
        foreach (var (script, permission) in wanted) _grants.Add((script, Guid.Empty, permission));
    }
    public IReadOnlyList<(Guid Subject, string Permission)> Of(Guid script) => [.. _grants.Where(g => g.Script == script).Select(g => (g.Subject, g.Permission))];
    /// <summary>The grants that were given, for persistence.</summary>
    public IReadOnlyList<(Guid Script, Guid Subject, string Permission)> All() => [.. _grants];

    /// <summary>False when the same request was made too recently (people are not asked over and over).</summary>
    public bool MayAsk(Guid script, Guid subject, string permission, double now, double minimumSeconds = 30)
    {
        var key = (script, subject, permission);
        if (_asked.TryGetValue(key, out var last) && now - last < minimumSeconds) return false;
        _asked[key] = now; return true;
    }
}
