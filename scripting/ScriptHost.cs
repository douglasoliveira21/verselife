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
    /// <summary>The object this change is for when it is not the script's own (another part of its linkset).</summary>
    public Guid? TargetId { get; set; }
    public string? Name { get; set; }
    /// <summary>Settings of the physics of the object (a null field is left as it is).</summary>
    public PhysicsChange? Physics { get; set; }
    public bool IsEmpty => Position is null && Rotation is null && Scale is null && Material.Count == 0 && Name is null && Physics is null;
}

public sealed record ScriptObjectState(Guid Id, Guid OwnerUserId, string Name, Vec3 Position, Vec3 Rotation, Vec3 Scale, Guid? ParentId = null);
public sealed record PhysicsChange(bool? Collision = null, bool? Physical = null, float? Mass = null);
public sealed record ScriptItem(string Name, string Type, Guid? AssetId);
public sealed record HudElement(string Id, string Type, string Text, string Color, double Value, double Max, string Placeholder);
public sealed record HudSnapshot(bool Visible, string Title, float X, float Y, int Width, IReadOnlyList<HudElement> Elements);
public sealed record HttpSpec(string Url, string Method, IReadOnlyDictionary<string, string> Headers, string? Body);
public sealed record ScriptDefinition(Guid Id, Guid ObjectId, Guid OwnerUserId, string Name, string Source, int Version, int InstructionBudget = 10_000, bool Paused = false);
public sealed record ScriptEvent(Guid ObjectId, string Name, IReadOnlyDictionary<string, object?>? Data = null, Guid? OnlyScript = null);
public sealed record ScriptReport(ScriptDefinition Script, string EventName, LuaStatus Status, string Message);

/// <summary>What the host needs from the game server. Scripts only ever act on their own object through this.</summary>
public interface IScriptWorld
{
    double Now { get; }
    ScriptObjectState? GetObject(Guid id);
    void ApplyChange(ScriptDefinition script, ObjectChange change);
    void Say(ScriptDefinition script, string text);
    /// <summary>Plays a sound that is in the object (by name) for the people near it. False when the object has no such sound.</summary>
    bool PlaySound(ScriptDefinition script, string soundName, float volume, bool loop) => false;
    void StopSound(ScriptDefinition script) { }
    /// <summary>Floating text above the object (empty text removes it).</summary>
    void SetText(ScriptDefinition script, string text, string color) { }
    /// <summary>Speaks in the local chat with the range of the kind (say, whisper, shout).</summary>
    void Chat(ScriptDefinition script, ChatKind kind, string text) => Say(script, text);
    /// <summary>A private line to one avatar on the island; false when that avatar is not here or too far to be told.</summary>
    bool TellAvatar(ScriptDefinition script, Guid avatarId, string text) => false;
    RegionInfo? GetRegion() => null;
    /// <summary>Starts a request to the outside on behalf of a script (the island asks the API, which checks and makes it). False when this world cannot; the answer comes later through <see cref="ScriptHost.CompleteHttp"/>.</summary>
    bool StartHttp(ScriptDefinition script, Guid requestId, HttpSpec spec) => false;
    /// <summary>Starts the payment of an avatar who agreed to pay the owner of the object (the island asks the API, which moves the money). False when this world cannot; the result comes through <see cref="ScriptHost.CompletePayment"/>.</summary>
    bool StartPayment(ScriptDefinition script, Guid requestId, Guid avatarId, long amount) => false;
    /// <summary>Starts the delivery of a copy of an item of the object to an avatar who allowed it (the island asks the API, which checks the rights); the result comes through <see cref="ScriptHost.CompleteGiveItem"/>. False when this world cannot.</summary>
    bool StartGiveItem(ScriptDefinition script, Guid avatarId, Guid assetId, string itemName) => false;
    /// <summary>Pushes the body of the object the physics simulation runs (action: impulse, velocity, angular, gravity). False when the object is not a simulated body.</summary>
    bool ApplyPhysics(ScriptDefinition script, string action, Vec3 value) => false;
    /// <summary>The name of an avatar on the island, or null.</summary>
    string? AvatarName(Guid avatarId) => null;
    /// <summary>Plays a gesture (wave, dance) on an avatar for some seconds. False when it cannot (unknown gesture, avatar not here).</summary>
    bool PlayAnimation(ScriptDefinition script, Guid avatarId, string name, double seconds) => false;
    void StopAnimation(ScriptDefinition script, Guid avatarId) { }
    /// <summary>Shows, updates or hides the HUD of a script on the screen of an avatar (the whole picture each time; a hidden one is removed).</summary>
    void ShowHud(ScriptDefinition script, Guid avatarId, HudSnapshot hud) { }
    /// <summary>Starts, changes or removes an emitter of particles or a light on the object of a script (everybody on the island sees it).</summary>
    void SetEffect(ScriptDefinition script, EffectSpec effect) { }
    /// <summary>The parcel that covers a point of the island, or null where the land has no parcel.</summary>
    ParcelData? GetParcelAt(double x, double z) => null;
    /// <summary>Plays a sound that is in the object at a chosen point of the island. False when the object has no such sound.</summary>
    bool PlaySoundAt(ScriptDefinition script, string soundName, float volume, bool loop, Vec3 position) => false;
    /// <summary>What the object carries in its Conteúdo (sounds, textures, notes...), read only.</summary>
    IReadOnlyList<ScriptItem> GetContents(Guid objectId) => [];
    /// <summary>The velocity of the simulated body of the object, or null when it is not simulated.</summary>
    Vec3? GetVelocity(Guid objectId) => null;
    /// <summary>Every object of the linkset of the object, root first, then the others in a stable order. Empty when the object is unknown.</summary>
    IReadOnlyList<ScriptObjectState> GetLinkset(Guid objectId) => GetObject(objectId) is { } one ? [one] : [];
}

public sealed record ScriptHostOptions(int MaxEventsPerSecond = 20, int MaxQueuedEvents = 400, int MaxTimersPerScript = 16, double MinTimerSeconds = .25, int MaxStateKeys = 32, int MaxStateValueLength = 1024, int ViolationsBeforeQuarantine = 3, double ViolationWindowSeconds = 60, double ReportIntervalSeconds = 5, int StoredEntriesLimit = 20_000, double MaxCoordinate = 5000, double MillisecondsPerEvent = 12);

public sealed record ScriptInfo(Guid ScriptId, Guid ObjectId, string Name, int Version, bool Quarantined, string? Reason, int Events, int Violations);

/// <summary>
/// Runs the Lua scripts attached to objects: loads them into isolated sandboxes, turns world happenings into
/// events (touch, enter, leave, chat, timer, message, start), runs handlers under hard limits, and quarantines
/// scripts that repeatedly break them. Single threaded: the caller drives <see cref="Tick"/> and <see cref="Process"/>.
/// </summary>
public sealed partial class ScriptHost(IScriptWorld world, ScriptHostOptions? options = null, bool warm = true)
{
    private static readonly Dictionary<string, string[]> Handlers = new()
    {
        ["start"] = ["on_start"], ["touch"] = ["on_touch", "on_touch_start", "on_click"], ["touch_end"] = ["on_touch_end"], ["physics_update"] = ["on_physics_update"], ["trigger_enter"] = ["on_trigger_enter"], ["trigger_exit"] = ["on_trigger_exit"], ["velocity_changed"] = ["on_velocity_changed"], ["double_click"] = ["on_double_click"], ["enter"] = ["on_enter", "on_avatar_enter"], ["leave"] = ["on_leave", "on_avatar_leave"],
        ["timer"] = ["on_timer"], ["chat"] = ["on_chat"], ["message"] = ["on_message", "on_object_message"], ["region_restart"] = ["on_region_restart", "on_region_start"],
        ["reset"] = ["on_reset"], ["destroy"] = ["on_destroy"], ["enable"] = ["on_enable"], ["disable"] = ["on_disable"], ["owner_changed"] = ["on_owner_changed"], ["region_shutdown"] = ["on_region_shutdown"],
        ["interval"] = ["on_interval"], ["delay_complete"] = ["on_delay_complete"], ["link_message"] = ["on_link_message"], ["script_error"] = ["on_script_error"], ["dialog_response"] = ["on_dialog_response"],
        ["http_response"] = ["on_http_response"], ["payment"] = ["on_payment"], ["purchase"] = ["on_purchase"], ["item_given"] = ["on_item_given"], ["item_give_failed"] = ["on_item_give_failed"], ["parcel_enter"] = ["on_parcel_enter"], ["parcel_leave"] = ["on_parcel_leave"], ["collision_enter"] = ["on_collision_enter", "on_collision"], ["collision_exit"] = ["on_collision_exit"], ["land_collision"] = ["on_land_collision"], ["hud_click"] = ["on_hud_click"], ["hud_input"] = ["on_hud_input"], ["hud_closed"] = ["on_hud_closed"], ["animation_finished"] = ["on_animation_finished"], ["inventory_changed"] = ["on_inventory_changed"], ["item_added"] = ["on_item_added"], ["item_removed"] = ["on_item_removed"], ["payment_failed"] = ["on_payment_failed"], ["permission_granted"] = ["on_permission_granted"], ["permission_denied"] = ["on_permission_denied"]
    };
    /// <summary>The names of every function the host calls when something happens (what a script can define).</summary>
    public static IReadOnlyCollection<string> KnownHandlers { get; } = Handlers.Values.SelectMany(x => x).ToHashSet();

    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.Compiled);

    private sealed class MotionState { public Vec3? Position, Rotation, CurrentPosition, CurrentRotation; public float Speed = 1, TurnSpeed = 90; public double Last; }

    private sealed class Timer { public int Id; public double Interval; public bool Repeat; public double Due; public int Count; public DynValue? Callback; public string? Name; public bool Paused; public double PausedRemaining; }

    private sealed class Instance(ScriptDefinition definition, LuaSandbox sandbox)
    {
        public ScriptDefinition Definition { get; set; } = definition;
        public LuaSandbox Sandbox { get; } = sandbox;
        public bool Quarantined { get; set; }
        public string? Reason { get; set; }
        public Queue<double> Recent { get; } = new();
        public Queue<double> Violations { get; } = new();
        public Queue<double> Says { get; } = new();
        public Queue<double> Sounds { get; } = new();
        public Queue<double> Texts { get; } = new();
        public List<Timer> Timers { get; } = [];
        public Dictionary<string, string> State { get; } = [];
        public bool StateDirty { get; set; }
        public ObjectChange? Pending { get; set; }
        public int Events { get; set; }
        public int NextTimerId { get; set; } = 1;
        public double LastReport { get; set; } = double.NegativeInfinity;
        public HashSet<string> HandlerNames { get; } = [];
        public ScriptMetricsData Metrics { get; } = new();
        public ScriptLifecycle Lifecycle { get; set; } = ScriptLifecycle.Running;
        public bool Enabled { get; set; } = true;
        public HashSet<int> Listening { get; } = [];
        public Dictionary<Guid, ObjectChange> PendingLinks { get; set; } = [];
        public Queue<double> Chats { get; } = new();
        public Queue<double> LinkMessages { get; } = new();
        public Queue<double> Directs { get; } = new();
        public Queue<double> Pushes { get; } = new();
        public Queue<double> Gives { get; } = new();
        public int NextEffectId { get; set; } = 1;
        public List<ScriptLogEntry> Logs { get; } = [];
        public bool InHandler { get; set; }
        public string? CurrentEvent { get; set; }
        public Dictionary<int, DynValue> OneShotCallbacks { get; } = [];
        public MotionState? Motion { get; set; }
        public int Depth { get; set; }
    }

    private readonly ScriptHostOptions _options = Prepare(options, warm);
    private readonly Dictionary<Guid, Instance> _instances = [];
    private readonly Queue<(Guid Object, string Name, IReadOnlyDictionary<string, object?>? Data, Guid? Only)> _queue = new();

    private static ScriptHostOptions Prepare(ScriptHostOptions? options, bool warm) { LuaSandbox.WarmUp(); if (warm) ScriptWarmUp.Run(); return options ?? new ScriptHostOptions(); }

    public event Action<ScriptReport>? Report;
    public event Action<string>? Log;
    public int QueuedEvents => _queue.Count;

    public IReadOnlyList<ScriptInfo> Scripts => _instances.Values.Select(i => new ScriptInfo(i.Definition.Id, i.Definition.ObjectId, i.Definition.Name, i.Definition.Version, i.Quarantined, i.Reason, i.Events, i.Violations.Count)).ToList();

    /// <summary>Makes the loaded scripts match the definitions: new ones start, changed versions reload, missing ones are dropped.</summary>
    /// <summary>True when any live script of the object handles the event; lets the server skip work nobody listens to.</summary>
    public bool Wants(Guid objectId, string eventName) => Handlers.TryGetValue(eventName, out var names) && _instances.Values.Any(i => i.Definition.ObjectId == objectId && !i.Quarantined && names.Any(i.HandlerNames.Contains));

    public IEnumerable<Guid> ObjectsWanting(string eventName) => _instances.Values.Where(i => !i.Quarantined && Handlers.TryGetValue(eventName, out var names) && names.Any(i.HandlerNames.Contains)).Select(i => i.Definition.ObjectId).Distinct();


    /// <summary>Script state that changed since the last call, for persistence.</summary>
    public IReadOnlyDictionary<Guid, Dictionary<string, string>> TakeDirtyState()
    {
        var result = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var instance in _instances.Values.Where(i => i.StateDirty)) { result[instance.Definition.Id] = new Dictionary<string, string>(instance.State); instance.StateDirty = false; }
        return result;
    }

    // ---- loading and running ---------------------------------------------------------------------------------


    // ---- the object API --------------------------------------------------------------------------------------

    private void InstallApi(Instance instance)
    {
        var script = instance.Sandbox.Script;
        var objectApi = new Table(script);
        void Add(string name, Func<CallbackArguments, DynValue> body) => objectApi[name] = DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return body(args); });
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
        Add("play_sound", args => PlaySound(instance, args));
        Add("stop_sound", _ => { world.StopSound(instance.Definition); return DynValue.Nil; });
        Add("set_text", args => SetText(instance, args));
        Add("teleport_authorized", _ => DynValue.False);   // reserved: needs the world manager
        script.Globals["object"] = objectApi;

        var lv = new Table(script) { ["object"] = objectApi };
        lv["time"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(world.Now));
        script.Globals["lv"] = lv;
        InstallExtendedApi(instance); InstallHttpApi(instance); InstallPhysicsApi(instance); InstallEconomyApi(instance); InstallInventoryApi(instance); InstallAvatarApi(instance); InstallHudApi(instance); InstallEffectsApi(instance); InstallLandApi(instance);
    }

    private void Say(Instance instance, string text)
    {
        var now = world.Now;
        while (instance.Says.Count > 0 && now - instance.Says.Peek() > 10) instance.Says.Dequeue();
        if (instance.Says.Count >= 5) throw new ScriptRuntimeException("say: at most 5 messages every 10 seconds");
        instance.Says.Enqueue(now);
        world.Say(instance.Definition, text.Length > 256 ? text[..256] : text);
    }

    private void Limit(Queue<double> times, int most, string what)
    {
        var now = world.Now;
        while (times.Count > 0 && now - times.Peek() > 10) times.Dequeue();
        if (times.Count >= most) throw new ScriptRuntimeException($"{what}: at most {most} calls every 10 seconds");
        times.Enqueue(now);
    }

    private DynValue PlaySound(Instance instance, CallbackArguments args)
    {
        var name = Text(args, 0); Limit(instance.Sounds, 5, "play_sound");
        var volume = args.Count > 1 && args[1].CastToNumber() is { } v && !double.IsNaN(v) ? (float)Math.Clamp(v, 0, 1) : 1f;
        var loop = args.Count > 2 && args[2].CastToBool();
        return DynValue.NewBoolean(world.PlaySound(instance.Definition, name.Length > 120 ? name[..120] : name, volume, loop));
    }

    private DynValue SetText(Instance instance, CallbackArguments args)
    {
        var text = args.Count == 0 || args[0].Type == DataType.Nil || args[0].Type == DataType.String && args[0].String.Length == 0 ? string.Empty : Text(args, 0); Limit(instance.Texts, 5, "set_text"); // an empty text takes the label away
        var color = args.Count > 1 && args[1].Type == DataType.String ? args[1].String : "#FFFFFF";
        if (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)) throw new ScriptRuntimeException("set_text: color must be like #RRGGBB");
        world.SetText(instance.Definition, text.Length > 120 ? text[..120] : text, color);
        return DynValue.Nil;
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
            return document.RootElement.ValueKind switch { JsonValueKind.Number => DynValue.NewNumber(document.RootElement.GetDouble()), JsonValueKind.String => DynValue.NewString(document.RootElement.GetString()), JsonValueKind.True => DynValue.True, JsonValueKind.False => DynValue.False, JsonValueKind.Object or JsonValueKind.Array => DynValue.NewTable(JsonTable(instance.Sandbox.Script, document.RootElement)), _ => DynValue.Nil };
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
            DataType.Table => TableJson(value.Table),
            _ => throw new ScriptRuntimeException($"state values must be numbers, booleans, strings up to {_options.MaxStateValueLength} characters, or small tables of those")
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
