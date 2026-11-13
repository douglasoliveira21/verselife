namespace LifeVerse.Api.Data;

/// <summary>How a script is doing on its island: what the island server last reported (its state and its use of the island). One row per script, replaced at every report.</summary>
public sealed class ScriptRuntimeStatus
{
    public Guid ScriptId { get; set; }
    public required string State { get; set; }
    public required string StateText { get; set; }
    public long Events { get; set; }
    public long EventsDropped { get; set; }
    public long Errors { get; set; }
    public long Violations { get; set; }
    public long Suspensions { get; set; }
    public long ApiCalls { get; set; }
    public long Instructions { get; set; }
    public long AllocatedBytes { get; set; }
    public long MessagesSent { get; set; }
    public double CpuMilliseconds { get; set; }
    public double WorstEventMilliseconds { get; set; }
    public int ActiveTimers { get; set; }
    public int StateKeys { get; set; }
    public string LastError { get; set; } = string.Empty;
    public int LastErrorLine { get; set; }
    public string SuspendedReason { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One line of the console of a script (what it printed, and its errors with where they happened). Only the most recent lines of each script are kept.</summary>
public sealed class ScriptLogLine
{
    public Guid Id { get; set; }
    public Guid ScriptId { get; set; }
    public required string Level { get; set; }
    public required string EventName { get; set; }
    public required string Text { get; set; }
    public string StackTrace { get; set; } = string.Empty;
    public double Milliseconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>What a person allowed a script to do. The subject is the avatar that gave the permission (or the empty id for the owner's permissions over the object itself). Survives restarts of the island.</summary>
public sealed class ScriptGrant
{
    public Guid ScriptId { get; set; }
    public Guid SubjectId { get; set; }
    public required string Permission { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}
