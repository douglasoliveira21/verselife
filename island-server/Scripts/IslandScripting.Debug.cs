using System.Text.Json;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>The debugger of the scripts, driven from the editor of the OWNER through the connection the player already has: start (with breakpoints), change the breakpoints, continue, step in, step over, step out, stop, and ask where it is. When a script stops at a line the island sends the picture of its variables to its owner. The island itself never waits: a stopped script is just not given events.</summary>
public sealed partial class IslandScripting
{
    private readonly Dictionary<Guid, Queue<double>> _debugCommands = [];
    public const int MaxDebugCommandsPerSecond = 10;

    private void WireDebug()
    {
        _host.DebugStopped += (definition, eventName, snapshot) => SendDebug(definition.OwnerUserId, DebugMessage(definition.Id, "stopped", eventName, snapshot));
        _host.DebugEnded += (definition, reason) => SendDebug(definition.OwnerUserId, JsonSerializer.Serialize(new { operation = "script_debug", script = definition.Id, @event = "ended", reason }));
    }

    private void SendDebug(Guid ownerUserId, string json)
    {
        if (SendToAvatar is null || _avatars.FirstOrDefault(a => a.UserId == ownerUserId) is not { } owner) return;
        SendToAvatar(owner.AvatarId, json);
    }

    private string DebugMessage(Guid script, string kind, string? handler, DebugSnapshot? snapshot) => JsonSerializer.Serialize(new
    {
        operation = "script_debug", script, @event = kind, handler, breakpoints = _host.DebugBreakpointsOf(script), debugging = _host.IsDebugging(script), stopped = snapshot is not null,
        line = snapshot?.Line ?? 0, reason = snapshot?.Reason, depth = snapshot?.Depth ?? 0,
        locals = snapshot?.Locals.Select(v => new { name = v.Name, type = v.Type, value = v.Value }) ?? [], globals = snapshot?.Globals.Select(v => new { name = v.Name, type = v.Type, value = v.Value }) ?? []
    });

    /// <summary>A command of the editor. Believed only from the owner of the script, a few a second.</summary>
    private bool TryDebugCommand(AvatarPresence who, JsonElement root)
    {
        if (!root.TryGetProperty("debug", out var flag) || flag.ValueKind != JsonValueKind.True) return false;
        if (!root.TryGetProperty("script", out var s) || !Guid.TryParse(s.GetString(), out var script) || _host.DefinitionOf(script) is not { } definition || definition.OwnerUserId != who.UserId) return true;
        var now = Now; var times = _debugCommands.TryGetValue(who.AvatarId, out var q) ? q : _debugCommands[who.AvatarId] = new Queue<double>();
        while (times.Count > 0 && now - times.Peek() > 1) times.Dequeue(); if (times.Count >= MaxDebugCommandsPerSecond) return true; times.Enqueue(now);
        var lines = root.TryGetProperty("breakpoints", out var b) && b.ValueKind == JsonValueKind.Array ? b.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _)).Select(e => e.GetInt32()).Take(LuaDebugger.MaxBreakpoints).ToList() : [];
        switch (root.TryGetProperty("action", out var a) ? a.GetString() : null)
        {
            case "start": if (!_host.DebugStart(script, lines)) { SendDebug(who.UserId, JsonSerializer.Serialize(new { operation = "script_debug", script, @event = "refused", reason = "no_room" })); return true; } SendDebug(who.UserId, DebugMessage(script, "started", null, null)); break;
            case "breakpoints": if (_host.DebugSetBreakpoints(script, lines)) SendDebug(who.UserId, DebugMessage(script, "breakpoints", null, _host.DebugSnapshotOf(script))); break;
            case "continue": _host.DebugResume(script, DebugCommand.Continue); AfterResume(who, script); break;
            case "stepin": _host.DebugResume(script, DebugCommand.StepIn); AfterResume(who, script); break;
            case "stepover": _host.DebugResume(script, DebugCommand.StepOver); AfterResume(who, script); break;
            case "stepout": _host.DebugResume(script, DebugCommand.StepOut); AfterResume(who, script); break;
            case "stop": _host.DebugStop(script); break;
            case "state": SendDebug(who.UserId, DebugMessage(script, "state", null, _host.DebugSnapshotOf(script))); break;
        }
        return true;
    }

    /// <summary>After a resume the script either stopped again (the host already told) or ran to the end of its handler: the editor learns that it is running.</summary>
    private void AfterResume(AvatarPresence who, Guid script) { if (_host.DebugSnapshotOf(script) is null) SendDebug(who.UserId, DebugMessage(script, "running", null, null)); }
}
