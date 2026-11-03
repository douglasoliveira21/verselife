using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

public readonly record struct Vec3(float X, float Y, float Z);

/// <summary>Changes a script asked for on its own object; applied once, after its handler returns.</summary>
public sealed class ObjectChange
{
    public Vec3? Position { get; set; }
    public Vec3? Rotation { get; set; }
    public Vec3? Scale { get; set; }
    public Dictionary<string, object?> Material { get; } = [];
    public bool IsEmpty => Position is null && Rotation is null && Scale is null && Material.Count == 0;
}

public sealed record ScriptObjectState(Guid Id, Guid OwnerUserId, string Name, Vec3 Position, Vec3 Rotation, Vec3 Scale);
public sealed record ScriptDefinition(Guid Id, Guid ObjectId, Guid OwnerUserId, string Name, string Source, int Version, int InstructionBudget = 10_000);
public sealed record ScriptEvent(Guid ObjectId, string Name, IReadOnlyDictionary<string, object?>? Data = null, Guid? OnlyScript = null);
public sealed record ScriptReport(ScriptDefinition Script, string EventName, LuaStatus Status, string Message);

/// <summary>What the host needs from the game server. Scripts only ever act on their own object through this.</summary>
public interface IScriptWorld
{
    double Now { get; }
    ScriptObjectState? GetObject(Guid id);
    void ApplyChange(ScriptDefinition script, ObjectChange change);
    void Say(ScriptDefinition script, string text);
}

public sealed record ScriptHostOptions(int MaxEventsPerSecond = 20, int MaxQueuedEvents = 400, int MaxTimersPerScript = 4, double MinTimerSeconds = .25, int MaxStateKeys = 32, int MaxStateValueLength = 1024, int ViolationsBeforeQuarantine = 3, double ViolationWindowSeconds = 60, double ReportIntervalSeconds = 5, int StoredEntriesLimit = 20_000, double MaxCoordinate = 5000, double MillisecondsPerEvent = 12);

public sealed record ScriptInfo(Guid ScriptId, Guid ObjectId, string Name, int Version, bool Quarantined, string? Reason, int Events, int Violations);

/// <summary>
/// Runs the Lua scripts attached to objects: loads them into isolated sandboxes, turns world happenings into
/// events (touch, enter, leave, chat, timer, message, start), runs handlers under hard limits, and quarantines
/// scripts that repeatedly break them. Single threaded: the caller drives <see cref="Tick"/> and <see cref="Process"/>.
/// </summary>
public sealed class ScriptHost(IScriptWorld world, ScriptHostOptions? options = null)
{
    private static readonly Dictionary<string, string[]> Handlers = new()
    {
        ["start"] = ["on_start"], ["touch"] = ["on_touch", "on_touch_start"], ["enter"] = ["on_enter", "on_avatar_enter"], ["leave"] = ["on_leave", "on_avatar_leave"],
        ["timer"] = ["on_timer"], ["chat"] = ["on_chat"], ["message"] = ["on_message", "on_object_message"], ["region_restart"] = ["on_region_restart"]
    };
    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.Compiled);

    private sealed class Timer { public int Id; public double Interval; public bool Repeat; public double Due; public int Count; }

    private sealed class Instance(ScriptDefinition definition, LuaSandbox sandbox)
    {
        public ScriptDefinition Definition { get; } = definition;
        public LuaSandbox Sandbox { get; } = sandbox;
        public bool Quarantined { get; set; }
        public string? Reason { get; set; }
        public Queue<double> Recent { get; } = new();
        public Queue<double> Violations { get; } = new();
        public Queue<double> Says { get; } = new();
        public List<Timer> Timers { get; } = [];
        public Dictionary<string, string> State { get; } = [];
        public bool StateDirty { get; set; }
        public ObjectChange? Pending { get; set; }
        public int Events { get; set; }
        public int NextTimerId { get; set; } = 1;
        public double LastReport { get; set; } = double.NegativeInfinity;
        public HashSet<string> HandlerNames { get; } = [];
    }

    private readonly ScriptHostOptions _options = Prepare(options);
    private readonly Dictionary<Guid, Instance> _instances = [];
    private readonly Queue<(Guid Object, string Name, IReadOnlyDictionary<string, object?>? Data, Guid? Only)> _queue = new();

    private static ScriptHostOptions Prepare(ScriptHostOptions? options) { LuaSandbox.WarmUp(); return options ?? new ScriptHostOptions(); }

    public event Action<ScriptReport>? Report;
    public event Action<string>? Log;
    public int QueuedEvents => _queue.Count;

    public IReadOnlyList<ScriptInfo> Scripts => _instances.Values.Select(i => new ScriptInfo(i.Definition.Id, i.Definition.ObjectId, i.Definition.Name, i.Definition.Version, i.Quarantined, i.Reason, i.Events, i.Violations.Count)).ToList();

    /// <summary>Makes the loaded scripts match the definitions: new ones start, changed versions reload, missing ones are dropped.</summary>
    public void Sync(IEnumerable<ScriptDefinition> definitions, Func<Guid, IReadOnlyDictionary<string, string>?>? savedState = null)
    {
        var wanted = definitions.ToDictionary(d => d.Id);
        foreach (var id in _instances.Keys.Where(id => !wanted.ContainsKey(id)).ToList()) _instances.Remove(id);
        foreach (var definition in wanted.Values)
        {
            if (_instances.TryGetValue(definition.Id, out var existing) && existing.Definition.Version == definition.Version && existing.Definition.Source == definition.Source && existing.Definition.ObjectId == definition.ObjectId) continue;
            var instance = Load(definition, savedState?.Invoke(definition.Id));
            _instances[definition.Id] = instance;
            if (!instance.Quarantined) _queue.Enqueue((definition.ObjectId, "start", null, definition.Id));
        }
    }

    public void Enqueue(ScriptEvent scriptEvent)
    {
        while (_queue.Count >= _options.MaxQueuedEvents) _queue.Dequeue();
        _queue.Enqueue((scriptEvent.ObjectId, scriptEvent.Name, scriptEvent.Data, scriptEvent.OnlyScript));
    }

    /// <summary>True when any live script of the object handles the event; lets the server skip work nobody listens to.</summary>
    public bool Wants(Guid objectId, string eventName) => Handlers.TryGetValue(eventName, out var names) && _instances.Values.Any(i => i.Definition.ObjectId == objectId && !i.Quarantined && names.Any(i.HandlerNames.Contains));

    public IEnumerable<Guid> ObjectsWanting(string eventName) => _instances.Values.Where(i => !i.Quarantined && Handlers.TryGetValue(eventName, out var names) && names.Any(i.HandlerNames.Contains)).Select(i => i.Definition.ObjectId).Distinct();

    /// <summary>Starts due timers. Call regularly with the world clock.</summary>
    public void Tick(double now)
    {
        foreach (var instance in _instances.Values)
        {
            if (instance.Quarantined) continue;
            foreach (var timer in instance.Timers.ToList())
            {
                if (timer.Due > now) continue;
                timer.Count++;
                Enqueue(new ScriptEvent(instance.Definition.ObjectId, "timer", new Dictionary<string, object?> { ["timer_id"] = timer.Id, ["count"] = timer.Count }, instance.Definition.Id));
                if (timer.Repeat) timer.Due = now + timer.Interval; else instance.Timers.Remove(timer);
            }
        }
    }

    /// <summary>Runs queued events until the time budget is spent; returns how many ran.</summary>
    public int Process(double budgetMilliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ran = 0;
        while (_queue.Count > 0 && watch.Elapsed.TotalMilliseconds < budgetMilliseconds)
        {
            var (objectId, name, data, only) = _queue.Dequeue();
            if (!Handlers.TryGetValue(name, out var handlerNames)) continue;
            foreach (var instance in _instances.Values.Where(i => i.Definition.ObjectId == objectId && (only is null || i.Definition.Id == only)).ToList())
            {
                if (instance.Quarantined) continue;
                var handler = handlerNames.FirstOrDefault(instance.HandlerNames.Contains);
                if (handler is null || !AllowEvent(instance)) continue;
                Run(instance, name, handler, data); ran++;
            }
        }
        return ran;
    }

    /// <summary>Script state that changed since the last call, for persistence.</summary>
    public IReadOnlyDictionary<Guid, Dictionary<string, string>> TakeDirtyState()
    {
        var result = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var instance in _instances.Values.Where(i => i.StateDirty)) { result[instance.Definition.Id] = new Dictionary<string, string>(instance.State); instance.StateDirty = false; }
        return result;
    }

    // ---- loading and running ---------------------------------------------------------------------------------

    private Instance Load(ScriptDefinition definition, IReadOnlyDictionary<string, string>? saved)
    {
        var limits = new LuaLimits(InstructionsPerEvent: Math.Clamp(definition.InstructionBudget, 1_000, 100_000), MillisecondsPerEvent: _options.MillisecondsPerEvent);
        var sandbox = new LuaSandbox(limits, text => Log?.Invoke($"[{definition.Name}] {text}"));
        var instance = new Instance(definition, sandbox);
        if (saved is not null) foreach (var (key, value) in saved.Take(_options.MaxStateKeys)) instance.State[key] = value;
        InstallApi(instance);
        var result = sandbox.Load(definition.Source);
        if (!result.Ok) { Quarantine(instance, result.Status == LuaStatus.Rejected ? $"does not load: {result.Error}" : $"top-level code failed: {result.Error ?? result.Status.ToString()}"); Notify(instance, "load", result); return instance; }
        foreach (var name in Handlers.Values.SelectMany(x => x)) if (sandbox.HasHandler(name)) instance.HandlerNames.Add(name);
        return instance;
    }

    private bool AllowEvent(Instance instance)
    {
        var now = world.Now;
        while (instance.Recent.Count > 0 && now - instance.Recent.Peek() > 1) instance.Recent.Dequeue();
        if (instance.Recent.Count >= _options.MaxEventsPerSecond) return false;
        instance.Recent.Enqueue(now);
        return true;
    }

    private void Run(Instance instance, string eventName, string handler, IReadOnlyDictionary<string, object?>? data)
    {
        instance.Pending = new ObjectChange();
        var argument = DynValue.NewTable(ToTable(instance.Sandbox.Script, data));
        var result = instance.Sandbox.Call(handler, argument);
        instance.Events++;
        if (instance.Pending is { IsEmpty: false } change) world.ApplyChange(instance.Definition, change);
        instance.Pending = null;
        if (result.Violation)
        {
            var now = world.Now;
            instance.Violations.Enqueue(now);
            while (instance.Violations.Count > 0 && now - instance.Violations.Peek() > _options.ViolationWindowSeconds) instance.Violations.Dequeue();
            if (instance.Violations.Count >= _options.ViolationsBeforeQuarantine) Quarantine(instance, $"{result.Status} {instance.Violations.Count} times in {_options.ViolationWindowSeconds:0}s");
        }
        else if (instance.Events % 50 == 0 && StoredEntries(instance.Sandbox.Script) > _options.StoredEntriesLimit) Quarantine(instance, "stores too much data");
        if (!result.Ok || result.Violation || instance.Quarantined) Notify(instance, eventName, result);
    }

    private void Quarantine(Instance instance, string reason)
    {
        instance.Quarantined = true; instance.Reason = reason; instance.Timers.Clear();
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

    // ---- the object API --------------------------------------------------------------------------------------

    private void InstallApi(Instance instance)
    {
        var script = instance.Sandbox.Script;
        var objectApi = new Table(script);
        void Add(string name, Func<CallbackArguments, DynValue> body) => objectApi[name] = DynValue.NewCallback((_, args) => body(args));
        ScriptObjectState Me() => world.GetObject(instance.Definition.ObjectId) ?? throw new ScriptRuntimeException("the object no longer exists");
        ObjectChange Change() => instance.Pending ?? throw new ScriptRuntimeException("objects can only be changed while handling an event");

        Add("get_id", _ => DynValue.NewString(instance.Definition.ObjectId.ToString()));
        Add("get_name", _ => DynValue.NewString(Me().Name));
        Add("get_position", _ => FromVec(script, Me().Position));
        Add("get_rotation", _ => FromVec(script, Me().Rotation));
        Add("get_scale", _ => FromVec(script, Me().Scale));
        Add("set_position", args => { Change().Position = Clamp(ToVec(args, 0)); return DynValue.Nil; });
        Add("move", args => { var current = Change().Position ?? Me().Position; var delta = ToVec(args, 0); Change().Position = Clamp(new Vec3(current.X + delta.X, current.Y + delta.Y, current.Z + delta.Z)); return DynValue.Nil; });
        Add("set_rotation", args => { Change().Rotation = ToVec(args, 0); return DynValue.Nil; });
        Add("rotate", args => { var current = Change().Rotation ?? Me().Rotation; var delta = ToVec(args, 0); Change().Rotation = new Vec3(current.X + delta.X, current.Y + delta.Y, current.Z + delta.Z); return DynValue.Nil; });
        Add("set_scale", args => { var v = ToVec(args, 0); Change().Scale = new Vec3(Math.Clamp(v.X, .01f, 64f), Math.Clamp(v.Y, .01f, 64f), Math.Clamp(v.Z, .01f, 64f)); return DynValue.Nil; });
        Add("set_color", args => { Change().Material["color"] = ToColor(args, 0); return DynValue.Nil; });
        Add("set_material", args =>
        {
            if (args.Count == 0 || args[0].Type != DataType.Table) throw new ScriptRuntimeException("set_material expects a table");
            foreach (var pair in args[0].Table.Pairs)
            {
                var key = pair.Key.CastToString();
                switch (key)
                {
                    case "color": case "emission": Change().Material[key] = ValidColor(pair.Value); break;
                    case "transparency": case "roughness": case "metallic": Change().Material[key] = Math.Clamp((float)(pair.Value.CastToNumber() ?? throw new ScriptRuntimeException($"{key} must be a number")), 0f, 1f); break;
                    default: throw new ScriptRuntimeException($"unknown material property '{key}'");
                }
            }
            return DynValue.Nil;
        });
        Add("say", args => { Say(instance, Text(args, 0)); return DynValue.Nil; });
        Add("send_message", args => SendMessage(instance, args));
        Add("create_timer", args =>
        {
            var seconds = args.Count > 0 ? args[0].CastToNumber() : null;
            if (seconds is null || double.IsNaN(seconds.Value) || seconds < _options.MinTimerSeconds || seconds > 86_400) throw new ScriptRuntimeException($"timer must be between {_options.MinTimerSeconds} and 86400 seconds");
            if (instance.Timers.Count >= _options.MaxTimersPerScript) throw new ScriptRuntimeException($"at most {_options.MaxTimersPerScript} timers per script");
            var timer = new Timer { Id = instance.NextTimerId++, Interval = seconds.Value, Repeat = args.Count > 1 && args[1].CastToBool(), Due = world.Now + seconds.Value };
            instance.Timers.Add(timer);
            return DynValue.NewNumber(timer.Id);
        });
        Add("cancel_timers", _ => { instance.Timers.Clear(); return DynValue.Nil; });
        Add("state_get", args => StateGet(instance, Key(args)));
        Add("state_set", args => StateSet(instance, Key(args), args.Count > 1 ? args[1] : DynValue.Nil));
        Add("play_sound", _ => DynValue.False);            // no audio assets yet
        Add("teleport_authorized", _ => DynValue.False);   // reserved: needs the world manager
        script.Globals["object"] = objectApi;

        var lv = new Table(script) { ["object"] = objectApi };
        lv["time"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(world.Now));
        script.Globals["lv"] = lv;
    }

    private void Say(Instance instance, string text)
    {
        var now = world.Now;
        while (instance.Says.Count > 0 && now - instance.Says.Peek() > 10) instance.Says.Dequeue();
        if (instance.Says.Count >= 5) throw new ScriptRuntimeException("say: at most 5 messages every 10 seconds");
        instance.Says.Enqueue(now);
        world.Say(instance.Definition, text.Length > 256 ? text[..256] : text);
    }

    private DynValue SendMessage(Instance instance, CallbackArguments args)
    {
        var text = Text(args, 0);
        if (args.Count < 2 || args[1].Type == DataType.Nil) { Say(instance, text); return DynValue.True; }
        if (!Guid.TryParse(args[1].CastToString(), out var targetId)) throw new ScriptRuntimeException("send_message: invalid object id");
        var target = world.GetObject(targetId);
        // Messages stay between objects of the same owner, so nobody can poke other people's scripts.
        if (target is null || target.OwnerUserId != instance.Definition.OwnerUserId) return DynValue.False;
        Enqueue(new ScriptEvent(targetId, "message", new Dictionary<string, object?> { ["message"] = text.Length > 256 ? text[..256] : text, ["from"] = instance.Definition.ObjectId.ToString() }));
        return DynValue.True;
    }

    private DynValue StateGet(Instance instance, string key)
    {
        if (!instance.State.TryGetValue(key, out var json)) return DynValue.Nil;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch { JsonValueKind.Number => DynValue.NewNumber(document.RootElement.GetDouble()), JsonValueKind.String => DynValue.NewString(document.RootElement.GetString()), JsonValueKind.True => DynValue.True, JsonValueKind.False => DynValue.False, _ => DynValue.Nil };
        }
        catch (JsonException) { return DynValue.Nil; }
    }

    private DynValue StateSet(Instance instance, string key, DynValue value)
    {
        if (value.Type == DataType.Nil) { instance.StateDirty |= instance.State.Remove(key); return DynValue.Nil; }
        string json = value.Type switch
        {
            DataType.Number when double.IsFinite(value.Number) => value.Number.ToString("R", CultureInfo.InvariantCulture),
            DataType.String when value.String.Length <= _options.MaxStateValueLength => JsonSerializer.Serialize(value.String),
            DataType.Boolean => value.Boolean ? "true" : "false",
            _ => throw new ScriptRuntimeException($"state values must be numbers, booleans or strings up to {_options.MaxStateValueLength} characters")
        };
        if (!instance.State.ContainsKey(key) && instance.State.Count >= _options.MaxStateKeys) throw new ScriptRuntimeException($"at most {_options.MaxStateKeys} state keys per script");
        if (!instance.State.TryGetValue(key, out var old) || old != json) { instance.State[key] = json; instance.StateDirty = true; }
        return DynValue.Nil;
    }

    // ---- value conversion ------------------------------------------------------------------------------------

    private static string Key(CallbackArguments args)
    {
        var key = args.Count > 0 ? args[0].CastToString() : null;
        if (string.IsNullOrEmpty(key) || key.Length > 64) throw new ScriptRuntimeException("state keys must be 1 to 64 characters");
        return key;
    }

    private static string Text(CallbackArguments args, int index)
    {
        var text = args.Count > index ? args[index].CastToString() : null;
        if (string.IsNullOrWhiteSpace(text)) throw new ScriptRuntimeException("expected a text message");
        return text.Replace('\n', ' ').Replace('\r', ' ');
    }

    private static string ToColor(CallbackArguments args, int index) => ValidColor(args.Count > index ? args[index] : DynValue.Nil);

    private static string ValidColor(DynValue value)
    {
        var text = value.CastToString();
        if (text is null || !ColorPattern.IsMatch(text)) throw new ScriptRuntimeException("colors must look like #RRGGBB");
        return text;
    }

    private static Vec3 ToVec(CallbackArguments args, int index)
    {
        if (args.Count <= index || args[index].Type != DataType.Table) throw new ScriptRuntimeException("expected a table like {x=0, y=0, z=0}");
        var table = args[index].Table;
        float Axis(string name, int position) { var value = (table.Get(name).Type != DataType.Nil ? table.Get(name) : table.Get(position)).CastToNumber() ?? 0; if (!double.IsFinite(value)) throw new ScriptRuntimeException("coordinates must be finite numbers"); return (float)value; }
        return new Vec3(Axis("x", 1), Axis("y", 2), Axis("z", 3));
    }

    private Vec3 Clamp(Vec3 v)
    {
        var limit = (float)_options.MaxCoordinate;
        return new Vec3(Math.Clamp(v.X, -limit, limit), Math.Clamp(v.Y, -limit, limit), Math.Clamp(v.Z, -limit, limit));
    }

    private static DynValue FromVec(Script script, Vec3 v) { var table = new Table(script); table["x"] = v.X; table["y"] = v.Y; table["z"] = v.Z; return DynValue.NewTable(table); }

    private static Table ToTable(Script script, IReadOnlyDictionary<string, object?>? data)
    {
        var table = new Table(script);
        if (data is null) return table;
        foreach (var (key, value) in data)
            table[key] = value switch
            {
                null => DynValue.Nil, string s => DynValue.NewString(s), bool b => DynValue.NewBoolean(b), Guid g => DynValue.NewString(g.ToString()),
                int i => DynValue.NewNumber(i), long l => DynValue.NewNumber(l), float f => DynValue.NewNumber(f), double d => DynValue.NewNumber(d),
                Vec3 v => FromVec(script, v), IReadOnlyDictionary<string, object?> nested => DynValue.NewTable(ToTable(script, nested)), _ => DynValue.NewString(value.ToString())
            };
        return table;
    }

    /// <summary>Counts table entries reachable from the globals (stopping early), to catch scripts that hoard data between events.</summary>
    private static int StoredEntries(Script script)
    {
        var seen = new HashSet<Table>(); var count = 0;
        void Visit(Table table, int depth)
        {
            if (depth > 8 || count > 200_000 || !seen.Add(table)) return;
            foreach (var pair in table.Pairs)
            {
                count++;
                if (pair.Value.Type == DataType.Table) Visit(pair.Value.Table, depth + 1);
            }
        }
        Visit(script.Globals, 0);
        return count;
    }
}
