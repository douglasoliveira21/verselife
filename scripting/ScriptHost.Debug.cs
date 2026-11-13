namespace LifeVerse.Scripting;

/// <summary>What the owner asked of the debugger for a script: the lines to stop at.</summary>
public sealed class DebugSettings
{
    public List<int> Breakpoints { get; } = [];
}

public sealed partial class ScriptHost
{
    public const int MaxDebugSessions = 3;
    public const double MaxDebugPauseSeconds = 120;
    private readonly Dictionary<Guid, DebugSettings> _debug = [];

    /// <summary>A script stopped at a line (the function that was running, and what it holds). The island tells its owner.</summary>
    public event Action<ScriptDefinition, string, DebugSnapshot>? DebugStopped;
    /// <summary>A script left debugging (the owner ended it, or it waited too long).</summary>
    public event Action<ScriptDefinition, string>? DebugEnded;

    public bool IsDebugging(Guid scriptId) => _debug.ContainsKey(scriptId);
    public DebugSnapshot? DebugSnapshotOf(Guid scriptId) => Find(scriptId)?.Sandbox.Debugger is { PausedNow: true } d ? d.Snapshot : null;
    public IReadOnlyList<int> DebugBreakpointsOf(Guid scriptId) => _debug.TryGetValue(scriptId, out var s) ? s.Breakpoints : [];

    /// <summary>Puts a script in debug mode with these breakpoints: it is loaded again (on_reset and on_start run) with the debugger on. At most three scripts at once; false when there is no such script or no room.</summary>
    public bool DebugStart(Guid scriptId, IEnumerable<int> breakpoints)
    {
        if (Find(scriptId) is null || (!_debug.ContainsKey(scriptId) && _debug.Count >= MaxDebugSessions)) return false;
        var settings = _debug.TryGetValue(scriptId, out var existing) ? existing : _debug[scriptId] = new DebugSettings(); SetBreakpointList(settings, breakpoints);
        return Restart(scriptId);
    }

    /// <summary>Changes the breakpoints of a script that is being debugged, without loading it again.</summary>
    public bool DebugSetBreakpoints(Guid scriptId, IEnumerable<int> breakpoints)
    {
        if (!_debug.TryGetValue(scriptId, out var settings) || Find(scriptId) is not { } instance) return false;
        SetBreakpointList(settings, breakpoints); instance.Sandbox.Debugger?.SetBreakpoints(settings.Breakpoints); return true;
    }

    private static void SetBreakpointList(DebugSettings settings, IEnumerable<int> lines)
    {
        settings.Breakpoints.Clear(); settings.Breakpoints.AddRange(lines.Where(l => l >= 1 && l <= 100_000).Distinct().Take(LuaDebugger.MaxBreakpoints).Order());
    }

    /// <summary>Lets a stopped script go on (continue, one line, one line without entering calls, or out of the function). Its own limits start again from here. It may stop again at the next line to look at.</summary>
    public bool DebugResume(Guid scriptId, DebugCommand command)
    {
        if (Find(scriptId) is not { } instance || instance.Sandbox.Debugger is not { PausedNow: true } || instance.DebugEvent is not { } eventName) return false;
        var result = instance.Sandbox.ContinueDebug(command); FinishRun(instance, eventName, result); return true;
    }

    /// <summary>Leaves debug mode: a stopped script finishes what it was doing and the script is loaded again normally.</summary>
    public bool DebugStop(Guid scriptId, string reason = "stopped")
    {
        if (!_debug.Remove(scriptId)) return false;
        if (Find(scriptId) is { } instance)
        {
            if (instance.Sandbox.Debugger is { PausedNow: true } debugger && instance.DebugEvent is { } eventName) { debugger.Detach(); var result = instance.Sandbox.ContinueDebug(DebugCommand.Continue); FinishRun(instance, eventName, result); }
            Restart(scriptId); DebugEnded?.Invoke(instance.Definition, reason);
        }
        return true;
    }

    /// <summary>A script that stays stopped for two minutes is let go: debugging ends (nobody should find an object frozen).</summary>
    private void ExpireDebugPauses(double now)
    {
        foreach (var instance in _instances.Values.Where(i => i.Sandbox.Debugger is { PausedNow: true } && now - i.DebugPausedAt > MaxDebugPauseSeconds).ToList()) DebugStop(instance.Definition.Id, "timeout");
    }
}

public sealed partial class ScriptHost
{
    /// <summary>The definition of a script (who owns it, which object it is on), or null.</summary>
    public ScriptDefinition? DefinitionOf(Guid scriptId) => Find(scriptId)?.Definition;
}
