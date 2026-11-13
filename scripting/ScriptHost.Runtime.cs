using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

public sealed partial class ScriptHost
{
    /// <summary>What every script of the island may use together: the host stops handing events to the scripts of a person who is using more than their share.</summary>
    public const int MaxScriptsPerObject = 8, MaxScriptsPerOwner = 200;
    private const double OwnerWindowSeconds = 10, OwnerCpuMillisecondsPerWindow = 600;

    private readonly ScriptPermissions _permissions = new();
    private readonly Dictionary<Guid, (double Start, double Cpu)> _ownerCpu = [];
    private long _dropped;

    public ScriptPermissions Permissions => _permissions;
    /// <summary>Events thrown away because the queue was full.</summary>
    public long DroppedEvents => _dropped;
    /// <summary>Raised when a script asks something of a person (permission, dialog): the island shows it and answers through <see cref="Answer"/>.</summary>
    public event Action<ScriptRequest>? Request;

    // ---- keeping the loaded scripts in step with the definitions ---------------------------------------------

    /// <summary>Makes the loaded scripts match the definitions: new ones start, changed versions reload (and start again), missing ones are destroyed (they get <c>on_destroy</c> first).</summary>
    public void Sync(IEnumerable<ScriptDefinition> definitions, Func<Guid, IReadOnlyDictionary<string, string>?>? savedState = null)
    {
        var wanted = definitions.ToDictionary(d => d.Id);
        foreach (var id in _instances.Keys.Where(id => !wanted.ContainsKey(id)).ToList()) Destroy(_instances[id]);
        var perObject = new Dictionary<Guid, int>(); var perOwner = new Dictionary<Guid, int>();
        foreach (var definition in wanted.Values.OrderBy(d => d.Id))
        {
            var overObject = perObject.GetValueOrDefault(definition.ObjectId) >= MaxScriptsPerObject; var overOwner = perOwner.GetValueOrDefault(definition.OwnerUserId) >= MaxScriptsPerOwner;
            if (overObject || overOwner)
            {
                if (_instances.TryGetValue(definition.Id, out var kept)) Destroy(kept);
                Report?.Invoke(new ScriptReport(definition, "load", LuaStatus.Rejected, overObject ? $"an object runs at most {MaxScriptsPerObject} scripts" : $"a person runs at most {MaxScriptsPerOwner} scripts on an island"));
                continue;
            }
            perObject[definition.ObjectId] = perObject.GetValueOrDefault(definition.ObjectId) + 1; perOwner[definition.OwnerUserId] = perOwner.GetValueOrDefault(definition.OwnerUserId) + 1;
            if (_instances.TryGetValue(definition.Id, out var existing) && existing.Definition.Version == definition.Version && existing.Definition.Source == definition.Source && existing.Definition.ObjectId == definition.ObjectId)
            {
                if (definition.Paused != existing.Definition.Paused) { if (definition.Paused) Pause(definition.Id); else if (existing.Enabled) Resume(definition.Id); existing.Definition = definition; }
                continue;
            }
            if (existing is not null) { _permissions.Revoke(definition.Id, Guid.Empty, ScriptPermission.ModifyObject); CloseHuds(existing); CloseEffects(existing); CloseNpcs(existing); CloseVehicles(existing); CloseMedia(existing); }
            var instance = Load(definition, savedState?.Invoke(definition.Id));
            _instances[definition.Id] = instance;
            if (!instance.Quarantined) _queue.Enqueue((definition.ObjectId, "start", null, definition.Id));
            if (definition.Paused) Pause(definition.Id);
        }
    }

    public void Enqueue(ScriptEvent scriptEvent)
    {
        while (_queue.Count >= _options.MaxQueuedEvents) { _queue.Dequeue(); _dropped++; }
        _queue.Enqueue((scriptEvent.ObjectId, scriptEvent.Name, scriptEvent.Data, scriptEvent.OnlyScript));
    }

    private Instance Load(ScriptDefinition definition, IReadOnlyDictionary<string, string>? saved)
    {
        var limits = new LuaLimits(InstructionsPerEvent: Math.Clamp(definition.InstructionBudget, 1_000, 100_000), MillisecondsPerEvent: _options.MillisecondsPerEvent);
        Instance? self = null;
        _debug.TryGetValue(definition.Id, out var debugging);
        var sandbox = new LuaSandbox(limits, text => { Log?.Invoke($"[{definition.Name}] {text}"); if (self is not null) AddLog(self, ScriptLogLevel.Info, "log", text); }, debug: debugging is not null);
        var instance = new Instance(definition, sandbox); self = instance; instance.Lifecycle = ScriptLifecycle.Created;
        if (saved is not null) foreach (var (key, value) in saved.Take(_options.MaxStateKeys)) instance.State[key] = value;
        InstallApi(instance); instance.Lifecycle = ScriptLifecycle.Loaded;
        var result = sandbox.Load(definition.Source);
        if (!result.Ok)
        {
            var reason = result.Status == LuaStatus.Rejected ? $"does not load: {result.Error}" : $"top-level code failed: {result.Error ?? result.Status.ToString()}";
            Quarantine(instance, reason, ScriptLifecycle.Faulted); RecordError(instance, "load", result.Error ?? reason, result); Notify(instance, "load", result); return instance;
        }
        instance.Lifecycle = ScriptLifecycle.Validated; if (debugging is not null) sandbox.Debugger?.SetBreakpoints(debugging.Breakpoints);
        foreach (var name in Handlers.Values.SelectMany(x => x)) if (sandbox.HasHandler(name)) instance.HandlerNames.Add(name);
        instance.Lifecycle = ScriptLifecycle.Waiting;
        return instance;
    }

    // ---- the clock and the queue -----------------------------------------------------------------------------

    private static bool Live(Instance i) => !i.Quarantined && i.Enabled && i.Lifecycle is ScriptLifecycle.Waiting or ScriptLifecycle.Running && !(i.Sandbox.Debugger?.PausedNow ?? false);

    /// <summary>Starts due timers and moves objects that are travelling. Call regularly with the world clock.</summary>
    public void Tick(double now)
    {
        if (_debug.Count > 0) ExpireDebugPauses(now);
        foreach (var instance in _instances.Values)
        {
            if (!Live(instance)) continue;
            foreach (var timer in instance.Timers.ToList())
            {
                if (timer.Paused || timer.Due > now) continue;
                timer.Count++; instance.Metrics.TimersFired++;
                if (timer.Callback is not null) Enqueue(new ScriptEvent(instance.Definition.ObjectId, "timer_fn", new Dictionary<string, object?> { ["timer_id"] = timer.Id, ["count"] = timer.Count }, instance.Definition.Id));
                else if (timer.Name is not null) Enqueue(new ScriptEvent(instance.Definition.ObjectId, timer.Repeat ? "interval" : "delay_complete", new Dictionary<string, object?> { ["name"] = timer.Name, ["timer_id"] = timer.Id, ["count"] = timer.Count }, instance.Definition.Id));
                else Enqueue(new ScriptEvent(instance.Definition.ObjectId, "timer", new Dictionary<string, object?> { ["timer_id"] = timer.Id, ["count"] = timer.Count }, instance.Definition.Id));
                if (timer.Repeat) timer.Due = now + timer.Interval; else { if (timer.Callback is not null) instance.OneShotCallbacks[timer.Id] = timer.Callback; instance.Timers.Remove(timer); }
            }
            StepMotion(instance, now);
        }
        FlushHuds(now);
    }

    /// <summary>Runs queued events until the time budget is spent; returns how many ran.</summary>
    public int Process(double budgetMilliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ran = 0;
        while (_queue.Count > 0 && watch.Elapsed.TotalMilliseconds < budgetMilliseconds)
        {
            var (objectId, name, data, only) = _queue.Dequeue();
            var internalCallback = name == "timer_fn";
            if (!internalCallback && !Handlers.ContainsKey(name)) continue;
            foreach (var instance in _instances.Values.Where(i => i.Definition.ObjectId == objectId && (only is null || i.Definition.Id == only)).ToList())
            {
                if (!Live(instance)) { if (instance.Quarantined || instance.Lifecycle is ScriptLifecycle.Paused or ScriptLifecycle.Stopped) instance.Metrics.EventsDropped++; continue; }
                if (name == "chat" && !HearsChannel(instance, data)) continue;
                if (internalCallback) { if (data is not null && data.TryGetValue("timer_id", out var raw) && raw is int id && TimerCallback(instance, id) is { } callback) { if (!AllowEvent(instance) || OwnerOverBudget(instance)) continue; RunFunction(instance, "timer", callback, data); instance.OneShotCallbacks.Remove(id); ran++; } continue; }
                var handler = Handlers[name].FirstOrDefault(instance.HandlerNames.Contains);
                if (handler is null) continue;
                if (!AllowEvent(instance) || OwnerOverBudget(instance)) continue;
                if (name == "touch") NoteToucher(instance, data);
                Run(instance, name, handler, data); ran++;
            }
        }
        return ran;
    }

    private static DynValue? TimerCallback(Instance instance, int id) => instance.Timers.FirstOrDefault(t => t.Id == id)?.Callback ?? instance.OneShotCallbacks.GetValueOrDefault(id);

    private static bool HearsChannel(Instance instance, IReadOnlyDictionary<string, object?>? data)
    {
        var channel = data is not null && data.TryGetValue("channel", out var raw) && raw is int c ? c : 0;
        return channel == 0 || instance.Listening.Contains(channel);
    }

    private bool AllowEvent(Instance instance)
    {
        var now = world.Now;
        while (instance.Recent.Count > 0 && now - instance.Recent.Peek() > 1) instance.Recent.Dequeue();
        if (instance.Recent.Count >= _options.MaxEventsPerSecond) { instance.Metrics.EventsDropped++; return false; }
        instance.Recent.Enqueue(now);
        return true;
    }

    /// <summary>A person's scripts together may use only so much CPU in a window; past it their events wait for the next window (they are not run late, they are dropped and counted).</summary>
    private bool OwnerOverBudget(Instance instance)
    {
        var now = world.Now; var owner = instance.Definition.OwnerUserId;
        if (!_ownerCpu.TryGetValue(owner, out var window) || now - window.Start >= OwnerWindowSeconds) { _ownerCpu[owner] = (now, 0); return false; }
        if (window.Cpu <= OwnerCpuMillisecondsPerWindow) return false;
        instance.Metrics.EventsDropped++; return true;
    }

    // ---- running a handler -----------------------------------------------------------------------------------

    private void Run(Instance instance, string eventName, string handler, IReadOnlyDictionary<string, object?>? data) => RunFunction(instance, eventName, instance.Sandbox.Script.Globals.Get(handler), data);

    private void RunFunction(Instance instance, string eventName, DynValue function, IReadOnlyDictionary<string, object?>? data)
    {
        instance.Pending = new ObjectChange(); instance.PendingLinks = []; instance.Lifecycle = ScriptLifecycle.Running; instance.CurrentEvent = eventName;
        var argument = DynValue.NewTable(ToTable(instance.Sandbox.Script, data));
        var result = instance.Sandbox.CallFunction(eventName, function, argument);
        FinishRun(instance, eventName, result);
    }

    /// <summary>What follows a handler (or the part of one that ran): metrics, the changes it asked for, limits and errors. A handler stopped by the debugger waits for the rest.</summary>
    private void FinishRun(Instance instance, string eventName, LuaRunResult result)
    {
        if (result.Status == LuaStatus.Paused) { instance.DebugEvent = eventName; instance.DebugPausedAt = world.Now; instance.Metrics.CpuMilliseconds += result.Milliseconds; if (instance.Sandbox.Debugger?.Snapshot is { } stop) DebugStopped?.Invoke(instance.Definition, eventName, stop); return; }      // stopped at a line: nothing is applied until it ends
        instance.DebugEvent = null; instance.Events++; instance.CurrentEvent = null;
        var metrics = instance.Metrics; metrics.Events++; metrics.CpuMilliseconds += result.Milliseconds; metrics.Instructions += result.Instructions; metrics.AllocatedBytes += result.AllocatedBytes; metrics.WorstEventMilliseconds = Math.Max(metrics.WorstEventMilliseconds, result.Milliseconds);
        if (_ownerCpu.TryGetValue(instance.Definition.OwnerUserId, out var window)) _ownerCpu[instance.Definition.OwnerUserId] = (window.Start, window.Cpu + result.Milliseconds);
        if (instance.Pending is { IsEmpty: false } change) world.ApplyChange(instance.Definition, change);
        foreach (var (target, linked) in instance.PendingLinks) { linked.TargetId = target; world.ApplyChange(instance.Definition, linked); }
        instance.Pending = null; instance.PendingLinks = [];
        if (instance.Lifecycle == ScriptLifecycle.Running) instance.Lifecycle = ScriptLifecycle.Waiting;
        if (result.Violation)
        {
            var now = world.Now; metrics.Violations++;
            instance.Violations.Enqueue(now);
            while (instance.Violations.Count > 0 && now - instance.Violations.Peek() > _options.ViolationWindowSeconds) instance.Violations.Dequeue();
            if (instance.Violations.Count >= _options.ViolationsBeforeQuarantine) Quarantine(instance, $"{result.Status} {instance.Violations.Count} times in {_options.ViolationWindowSeconds:0}s");
        }
        else if (instance.Events % 50 == 0 && StoredEntries(instance.Sandbox.Script) > _options.StoredEntriesLimit) Quarantine(instance, "stores too much data");
        if (!result.Ok || result.Violation) RecordError(instance, eventName, result.Error ?? result.Status.ToString(), result);
        if (!result.Ok || result.Violation || instance.Quarantined) Notify(instance, eventName, result);
        if ((!result.Ok || result.Violation) && eventName != "script_error" && !instance.Quarantined && instance.HandlerNames.Contains("on_script_error"))
            Enqueue(new ScriptEvent(instance.Definition.ObjectId, "script_error", new Dictionary<string, object?> { ["event"] = eventName, ["message"] = result.Error ?? result.Status.ToString(), ["line"] = instance.Metrics.LastErrorLine, ["status"] = result.Status.ToString() }, instance.Definition.Id));
    }

    private static readonly Regex ErrorLine = new(@"\((?<line>\d+),\d+(?:-\d+)?\)", RegexOptions.Compiled);

    private void RecordError(Instance instance, string eventName, string error, LuaRunResult result)
    {
        var metrics = instance.Metrics; metrics.Errors++; metrics.LastError = error; metrics.LastErrorEvent = eventName; metrics.LastErrorAt = world.Now;
        metrics.LastErrorLine = ErrorLine.Match(error) is { Success: true } match ? int.Parse(match.Groups["line"].Value) : 0;
        AddLog(instance, ScriptLogLevel.Error, eventName, error, stack: $"{instance.Definition.Name}:{(metrics.LastErrorLine > 0 ? metrics.LastErrorLine.ToString() : "?")} in {eventName} ({result.Status}, {result.Instructions} instructions)", milliseconds: result.Milliseconds);
    }

    private void Quarantine(Instance instance, string reason, ScriptLifecycle state = ScriptLifecycle.Suspended)
    {
        instance.Quarantined = true; instance.Reason = reason; CloseHuds(instance); CloseEffects(instance); CloseNpcs(instance); CloseVehicles(instance); CloseMedia(instance); instance.Timers.Clear(); instance.Motion = null; SetLifecycle(instance, state);
        if (state == ScriptLifecycle.Suspended) instance.Metrics.Suspensions++;
        AddLog(instance, ScriptLogLevel.Error, "limit", state == ScriptLifecycle.Suspended ? $"suspended: {reason}" : reason);
        Log?.Invoke($"[{instance.Definition.Name}] quarantined: {reason}");
    }

    private void Notify(Instance instance, string eventName, LuaRunResult result)
    {
        var now = world.Now;
        if (now - instance.LastReport < _options.ReportIntervalSeconds && !instance.Quarantined) return;
        instance.LastReport = now;
        var message = instance.Quarantined ? $"script disabled ({instance.Reason}). Edit and save it to enable it again." : $"{eventName}: {result.Error ?? result.Status.ToString()}";
        Report?.Invoke(new ScriptReport(instance.Definition, eventName, result.Status, message));
    }

    private void SetLifecycle(Instance instance, ScriptLifecycle state)
    {
        if (!ScriptLifecycleRules.Allowed(instance.Lifecycle, state)) return;
        instance.Lifecycle = state;
    }

    // ---- control: stop, start again, pause, enable ------------------------------------------------------------

    private Instance? Find(Guid scriptId) => _instances.GetValueOrDefault(scriptId);

    /// <summary>Stops a script: no more events, no timers. <see cref="Restart"/> starts it again from the saved source.</summary>
    public bool Stop(Guid scriptId)
    {
        if (Find(scriptId) is not { } instance || instance.Lifecycle == ScriptLifecycle.Stopped) return false;
        CloseHuds(instance); CloseEffects(instance); CloseNpcs(instance); CloseVehicles(instance); CloseMedia(instance); instance.Timers.Clear(); instance.Motion = null; instance.Listening.Clear(); instance.Lifecycle = ScriptLifecycle.Stopped; instance.Quarantined = false; instance.Reason = "stopped";
        AddLog(instance, ScriptLogLevel.Info, "control", "stopped"); return true;
    }

    /// <summary>Loads the script again from its source (what it kept with <c>storage</c> is kept): <c>on_reset</c> and then <c>on_start</c>. Also the way to start a stopped or suspended script.</summary>
    public bool Restart(Guid scriptId)
    {
        if (Find(scriptId) is not { } old) return false;
        CloseNpcs(old); CloseVehicles(old); CloseMedia(old); var definition = old.Definition; var state = new Dictionary<string, string>(old.State);
        var instance = Load(definition, state); instance.Metrics.Suspensions = old.Metrics.Suspensions; _instances[definition.Id] = instance;
        if (instance.Quarantined) return true;
        _queue.Enqueue((definition.ObjectId, "reset", null, definition.Id)); _queue.Enqueue((definition.ObjectId, "start", null, definition.Id));
        AddLog(instance, ScriptLogLevel.Info, "control", "restarted"); return true;
    }

    /// <summary>Pauses a script: it receives nothing and its timers stand still until <see cref="Resume"/>.</summary>
    public bool Pause(Guid scriptId)
    {
        if (Find(scriptId) is not { } instance || instance.Lifecycle is not (ScriptLifecycle.Waiting or ScriptLifecycle.Running)) return false;
        var now = world.Now; foreach (var timer in instance.Timers) if (!timer.Paused) { timer.Paused = true; timer.PausedRemaining = Math.Max(0, timer.Due - now); }
        instance.Lifecycle = ScriptLifecycle.Paused; return true;
    }

    public bool Resume(Guid scriptId)
    {
        if (Find(scriptId) is not { } instance || instance.Lifecycle != ScriptLifecycle.Paused) return false;
        var now = world.Now; foreach (var timer in instance.Timers) if (timer.Paused) { timer.Paused = false; timer.Due = now + timer.PausedRemaining; }
        instance.Lifecycle = ScriptLifecycle.Waiting; return true;
    }

    /// <summary>Disables a script (its <c>on_disable</c> runs first) or enables it again (then <c>on_enable</c> runs). A disabled script receives nothing.</summary>
    public bool SetEnabled(Guid scriptId, bool enabled)
    {
        if (Find(scriptId) is not { } instance || instance.Quarantined || instance.Lifecycle is ScriptLifecycle.Stopped or ScriptLifecycle.Destroyed) return false;
        if (instance.Enabled == enabled) return true;
        if (enabled)
        {
            instance.Enabled = true; if (instance.Lifecycle == ScriptLifecycle.Paused) Resume(scriptId);
            if (instance.HandlerNames.Contains("on_enable")) RunNow(instance, "enable", "on_enable", null); return true;
        }
        if (instance.HandlerNames.Contains("on_disable")) RunNow(instance, "disable", "on_disable", null);
        instance.Enabled = false; Pause(scriptId); return true;
    }

    /// <summary>The object changed owner: the grants the previous owner gave the scripts end, and the scripts hear <c>on_owner_changed</c>.</summary>
    public void OwnerChanged(Guid objectId, Guid newOwner)
    {
        foreach (var instance in _instances.Values.Where(i => i.Definition.ObjectId == objectId).ToList())
        {
            _permissions.RevokeAll(instance.Definition.Id);
            Enqueue(new ScriptEvent(objectId, "owner_changed", new Dictionary<string, object?> { ["owner_id"] = newOwner }, instance.Definition.Id));
        }
    }

    /// <summary>The island started (or started again): every script hears <c>on_region_start</c>.</summary>
    public void RegionStarted() { foreach (var instance in _instances.Values.Where(Live)) _queue.Enqueue((instance.Definition.ObjectId, "region_restart", null, instance.Definition.Id)); }

    /// <summary>The island is going down: every live script gets <c>on_region_shutdown</c> now, within its budget, and its state is left ready to be saved.</summary>
    public void RegionShutdown() { foreach (var instance in _instances.Values.Where(Live).ToList()) if (instance.HandlerNames.Contains("on_region_shutdown")) RunNow(instance, "region_shutdown", "on_region_shutdown", null); }

    private void Destroy(Instance instance)
    {
        if (Live(instance) && instance.HandlerNames.Contains("on_destroy")) RunNow(instance, "destroy", "on_destroy", null);
        CloseHuds(instance); CloseEffects(instance); CloseNpcs(instance); CloseVehicles(instance); CloseMedia(instance); instance.Timers.Clear(); instance.Motion = null; instance.Lifecycle = ScriptLifecycle.Destroyed; _permissions.RevokeAll(instance.Definition.Id); _instances.Remove(instance.Definition.Id);
    }

    private void RunNow(Instance instance, string eventName, string handler, IReadOnlyDictionary<string, object?>? data)
    {
        var enabled = instance.Enabled; var state = instance.Lifecycle; instance.Enabled = true; if (state == ScriptLifecycle.Paused) instance.Lifecycle = ScriptLifecycle.Waiting;
        Run(instance, eventName, handler, data); instance.Enabled = enabled; if (state == ScriptLifecycle.Paused && instance.Lifecycle == ScriptLifecycle.Waiting) instance.Lifecycle = ScriptLifecycle.Paused;
    }

    // ---- what the editor and the administrator look at -------------------------------------------------------

    public ScriptMetrics? MetricsOf(Guid scriptId) => Find(scriptId) is { } i ? ToMetrics(i) : null;
    public IReadOnlyList<ScriptMetrics> AllMetrics() => [.. _instances.Values.Select(ToMetrics)];
    public ScriptLifecycle? StateOf(Guid scriptId) => Find(scriptId)?.Lifecycle;

    private static ScriptMetrics ToMetrics(Instance i)
    {
        var m = i.Metrics;
        return new ScriptMetrics(i.Definition.Id, i.Definition.ObjectId, i.Definition.OwnerUserId, i.Definition.Name, i.Lifecycle, ScriptLifecycleRules.Describe(i.Lifecycle), m.Events, m.EventsDropped, m.Errors, m.Violations, m.Suspensions, m.ApiCalls, m.Instructions,
            m.AllocatedBytes, m.MessagesSent, m.CpuMilliseconds, m.WorstEventMilliseconds, i.Timers.Count, i.State.Count, m.LastError, m.LastErrorLine, i.Quarantined ? i.Reason : null);
    }

    /// <summary>The use of the island by owner: scripts, CPU, events and errors (for the administrators to find who is spending the most).</summary>
    public IReadOnlyList<(Guid Owner, int Scripts, double CpuMilliseconds, long Events, long Errors, long Suspended)> UsageByOwner() =>
        [.. _instances.Values.GroupBy(i => i.Definition.OwnerUserId).Select(g => (g.Key, g.Count(), g.Sum(i => i.Metrics.CpuMilliseconds), g.Sum(i => i.Metrics.Events), g.Sum(i => i.Metrics.Errors), (long)g.Count(i => i.Quarantined))).OrderByDescending(x => x.Item3)];

    public IReadOnlyList<ScriptLogEntry> LogsOf(Guid scriptId, int take = 100) => Find(scriptId) is { } i ? [.. i.Logs.TakeLast(Math.Clamp(take, 1, 200))] : [];

    private void AddLog(Instance instance, ScriptLogLevel level, string eventName, string text, string? stack = null, double milliseconds = 0)
    {
        if (instance.Logs.Count >= 200) instance.Logs.RemoveAt(0);
        instance.Logs.Add(new ScriptLogEntry(world.Now, level, instance.Definition.Id, instance.Definition.ObjectId, instance.Definition.Name, eventName, text.Length > 400 ? text[..400] : text, stack, milliseconds));
    }

    /// <summary>The script that should receive what a person answered (a dialog, a permission): called by the island.</summary>
    public void Answer(Guid scriptId, string eventName, IReadOnlyDictionary<string, object?> data)
    {
        if (Find(scriptId) is { } instance) Enqueue(new ScriptEvent(instance.Definition.ObjectId, eventName, data, scriptId));
    }

    private void RaiseRequest(ScriptRequest request) => Request?.Invoke(request);
}

/// <summary>Something a script asked of a person: the island shows it and, when the person answers, tells the script with <see cref="ScriptHost.Answer"/>.</summary>
public sealed record ScriptRequest(Guid RequestId, Guid ScriptId, Guid ObjectId, string ObjectName, Guid AvatarId, string Kind, string Title, string Message, IReadOnlyList<string> Buttons, string? Permission = null, bool WithInput = false, double TimeoutSeconds = 60);
