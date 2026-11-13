using System.Globalization;
using System.Text.Json;
using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>
/// The second layer of the Lua API: the same object API with camelCase names, plus <c>chat</c>, <c>timer</c>, <c>storage</c>, <c>debug</c>, <c>region</c>, <c>script</c>, <c>permissions</c> and <c>dialog</c>.
/// Everything a script asks for goes through here: arguments are checked, rates are limited, and what reaches beyond the object itself needs a grant of a person.
/// </summary>
public sealed partial class ScriptHost
{
    private const int MaxListenChannels = 8, MaxBroadcastTargets = 50, MaxPendingDialogs = 3;
    private const double MaxMoveSpeed = 50, MaxTurnSpeed = 720;
    private readonly Dictionary<Guid, (Guid Script, Guid Avatar, double Expires)> _dialogs = [];
    private readonly Dictionary<Guid, Dictionary<Guid, double>> _touchers = [];

    private void InstallExtendedApi(Instance instance)
    {
        var script = instance.Sandbox.Script;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return body(args); });
        Table Namespace(string name) { var table = new Table(script); script.Globals[name] = table; return table; }
        ScriptObjectState Me() => world.GetObject(instance.Definition.ObjectId) ?? throw new ScriptRuntimeException("the object no longer exists");
        ObjectChange Change() => instance.Pending ?? throw new ScriptRuntimeException("objects can only be changed while handling an event");

        // ---- object: the same calls with camelCase names, and what the first API did not have ----
        var obj = script.Globals.Get("object").Table;
        void Alias(string camel, string snake) => obj[camel] = obj.Get(snake);
        foreach (var (camel, snake) in new[] { ("getId", "get_id"), ("getName", "get_name"), ("getPosition", "get_position"), ("getRotation", "get_rotation"), ("getScale", "get_scale"), ("sendMessage", "send_message"), ("setText", "set_text") }) Alias(camel, snake);
        obj["setPosition"] = Fn(args => { Change().Position = Limit(Vector(args, 0)); instance.Motion = null; return DynValue.Nil; });
        obj["setRotation"] = Fn(args => { Change().Rotation = Vector(args, 0); return DynValue.Nil; });
        obj["setScale"] = Fn(args => { var v = Vector(args, 0); Change().Scale = new Vec3(Math.Clamp(v.X, .01f, 64f), Math.Clamp(v.Y, .01f, 64f), Math.Clamp(v.Z, .01f, 64f)); return DynValue.Nil; });
        obj["setName"] = Fn(args => { var name = Plain(args, 0, "name"); if (name.Length > 63) throw new ScriptRuntimeException("names have at most 63 characters"); Change().Name = name; return DynValue.Nil; });
        obj["getOwner"] = Fn(_ => DynValue.NewString(Me().OwnerUserId.ToString()));
        obj["getRegion"] = Fn(_ => world.GetRegion() is { } region ? DynValue.NewString(region.Id.ToString()) : DynValue.Nil);
        obj["setVisible"] = Fn(args => { Change().Material["transparency"] = args.Count > 0 && args[0].CastToBool() ? 0f : 1f; return DynValue.Nil; });
        obj["setAlpha"] = Fn(args => { Change().Material["transparency"] = 1f - Math.Clamp(Number(args, 0), 0f, 1f); return DynValue.Nil; });
        obj["setColor"] = Fn(args => { Change().Material["color"] = args.Count == 1 ? ColorOf(args[0]) : Rgb(Number(args, 0), Number(args, 1), Number(args, 2)); return DynValue.Nil; });
        obj["setGlow"] = Fn(args => { var glow = Math.Clamp(Number(args, 0), 0f, 1f); Change().Material["emission"] = Rgb(glow, glow, glow); return DynValue.Nil; });
        obj["setFullbright"] = Fn(args => { Change().Material["emission"] = args.Count > 0 && args[0].CastToBool() ? "#FFFFFF" : "#000000"; return DynValue.Nil; });
        obj["moveTo"] = Fn(args => { var target = Limit(Vector(args, 0, 3)); var speed = Math.Clamp(Number(args, 3, 1), .1f, (float)MaxMoveSpeed); Motion(instance).Position = target; Motion(instance).Speed = speed; Motion(instance).Last = world.Now; Motion(instance).CurrentPosition = Me().Position; return DynValue.Nil; });
        obj["rotateTo"] = Fn(args => { var target = Vector(args, 0, 3); var speed = Math.Clamp(Number(args, 3, 90), 1f, (float)MaxTurnSpeed); var motion = Motion(instance); motion.Rotation = target; motion.TurnSpeed = speed; motion.Last = world.Now; motion.CurrentRotation = Me().Rotation; return DynValue.Nil; });

        // linksets: link 1 is the root, the others follow in a stable order
        obj["getRoot"] = Fn(_ => DynValue.NewString((world.GetLinkset(instance.Definition.ObjectId) is { Count: > 0 } set ? set[0].Id : instance.Definition.ObjectId).ToString()));
        obj["getChildren"] = Fn(_ => Strings(script, world.GetLinkset(instance.Definition.ObjectId).Skip(1).Select(o => o.Id.ToString())));
        obj["getLinkCount"] = Fn(_ => DynValue.NewNumber(Math.Max(1, world.GetLinkset(instance.Definition.ObjectId).Count)));
        obj["getLink"] = Fn(args =>
        {
            var set = world.GetLinkset(instance.Definition.ObjectId); var index = (int)Number(args, 0);
            if (index < 1 || index > set.Count) return DynValue.Nil;
            var part = set[index - 1]; var table = new Table(script); table["id"] = part.Id.ToString(); table["name"] = part.Name; table["position"] = FromVec(script, part.Position); table["rotation"] = FromVec(script, part.Rotation); table["scale"] = FromVec(script, part.Scale);
            return DynValue.NewTable(table);
        });
        obj["setLinkPosition"] = Fn(args => { Link(args).Position = Limit(Vector(args, 1)); return DynValue.Nil; });
        obj["setLinkRotation"] = Fn(args => { Link(args).Rotation = Vector(args, 1); return DynValue.Nil; });
        ObjectChange Link(CallbackArguments args)
        {
            var set = world.GetLinkset(instance.Definition.ObjectId); var index = (int)Number(args, 0);
            if (index < 1 || index > set.Count) throw new ScriptRuntimeException($"this linkset has {set.Count} part(s)");
            var part = set[index - 1];
            if (part.OwnerUserId != instance.Definition.OwnerUserId) throw new ScriptRuntimeException("link parts of other people cannot be changed");
            _ = Change(); // only while handling an event
            if (part.Id == instance.Definition.ObjectId) return instance.Pending!;
            if (!instance.PendingLinks.TryGetValue(part.Id, out var change)) instance.PendingLinks[part.Id] = change = new ObjectChange();
            return change;
        }
        DynValue LinkMessage(CallbackArguments args) => DynValue.NewBoolean(SendLinkMessage(instance, Plain(args, 0, "message")));
        obj["sendLinkMessage"] = Fn(LinkMessage);
        obj["broadcastMessage"] = Fn(args => DynValue.NewNumber(Broadcast(instance, Plain(args, 0, "message"))));

        // ---- script ----
        var scriptApi = Namespace("script");
        scriptApi["sendLinkMessage"] = Fn(LinkMessage);
        scriptApi["getId"] = Fn(_ => DynValue.NewString(instance.Definition.Id.ToString()));
        scriptApi["getName"] = Fn(_ => DynValue.NewString(instance.Definition.Name));

        // ---- chat ----
        var chat = Namespace("chat");
        chat["say"] = Fn(args => { Speak(instance, ChatKind.Say, Plain(args, 0, "message")); return DynValue.Nil; });
        chat["whisper"] = Fn(args => { Speak(instance, ChatKind.Whisper, Plain(args, 0, "message")); return DynValue.Nil; });
        chat["shout"] = Fn(args => { Speak(instance, ChatKind.Shout, Plain(args, 0, "message")); return DynValue.Nil; });
        chat["sendChannel"] = Fn(args => DynValue.NewNumber(SendChannel(instance, Channel(args, 0), Plain(args, 1, "message"))));
        chat["listen"] = Fn(args =>
        {
            var channel = Channel(args, 0);
            if (instance.Listening.Count >= MaxListenChannels && !instance.Listening.Contains(channel)) throw new ScriptRuntimeException($"a script listens to at most {MaxListenChannels} channels");
            instance.Listening.Add(channel); return DynValue.Nil;
        });
        chat["stopListen"] = Fn(args => { instance.Listening.Remove(Channel(args, 0)); return DynValue.Nil; });
        chat["sendPrivateMessage"] = Fn(args => DynValue.NewBoolean(TellAvatar(instance, AvatarId(args, 0), Plain(args, 1, "message"))));

        // ---- timers that call a function (or fire on_interval / on_delay_complete with a name) ----
        var timers = Namespace("timer");
        DynValue StartTimer(CallbackArguments args, bool repeat)
        {
            var seconds = args.Count > 0 ? args[0].CastToNumber() : null;
            if (seconds is null || double.IsNaN(seconds.Value) || seconds < _options.MinTimerSeconds || seconds > 86_400) throw new ScriptRuntimeException($"timer must be between {_options.MinTimerSeconds} s and 24 h");
            if (instance.Timers.Count >= _options.MaxTimersPerScript) throw new ScriptRuntimeException($"at most {_options.MaxTimersPerScript} timers per script");
            var what = args.Count > 1 ? args[1] : DynValue.Nil;
            if (what.Type is not (DataType.Function or DataType.String)) throw new ScriptRuntimeException("timers need a function to call (or a name, which fires on_interval / on_delay_complete)");
            var timer = new Timer { Id = instance.NextTimerId++, Interval = seconds.Value, Repeat = repeat, Due = world.Now + seconds.Value, Callback = what.Type == DataType.Function ? what : null, Name = what.Type == DataType.String ? (what.String.Length > 64 ? what.String[..64] : what.String) : null };
            instance.Timers.Add(timer); return DynValue.NewNumber(timer.Id);
        }
        timers["after"] = Fn(args => StartTimer(args, false));
        timers["every"] = Fn(args => StartTimer(args, true));
        Timer? TimerOf(CallbackArguments args) { var id = (int)Number(args, 0); return instance.Timers.FirstOrDefault(t => t.Id == id); }
        timers["cancel"] = Fn(args => { var timer = TimerOf(args); if (timer is null) return DynValue.False; instance.Timers.Remove(timer); return DynValue.True; });
        timers["pause"] = Fn(args => { var timer = TimerOf(args); if (timer is null || timer.Paused) return DynValue.False; timer.Paused = true; timer.PausedRemaining = Math.Max(0, timer.Due - world.Now); return DynValue.True; });
        timers["resume"] = Fn(args => { var timer = TimerOf(args); if (timer is null || !timer.Paused) return DynValue.False; timer.Paused = false; timer.Due = world.Now + timer.PausedRemaining; return DynValue.True; });
        timers["getRemaining"] = Fn(args => TimerOf(args) is { } timer ? DynValue.NewNumber(timer.Paused ? timer.PausedRemaining : Math.Max(0, timer.Due - world.Now)) : DynValue.Nil);

        // ---- storage: what a script keeps across restarts (numbers, text, booleans and small tables) ----
        var storage = Namespace("storage");
        storage["set"] = Fn(args => StateSet(instance, StorageKey(args), args.Count > 1 ? args[1] : DynValue.Nil));
        storage["get"] = Fn(args => StateGet(instance, StorageKey(args)));
        storage["delete"] = Fn(args => StateSet(instance, StorageKey(args), DynValue.Nil));
        storage["keys"] = Fn(_ => Strings(script, instance.State.Keys.OrderBy(k => k, StringComparer.Ordinal)));

        // ---- debug and print: the lines go to the console of the script ----
        var debug = Namespace("debug");
        void Write(ScriptLogLevel level, CallbackArguments args)
        {
            var now = world.Now; while (instance.Chats.Count > 0 && now - instance.Chats.Peek() > 1) instance.Chats.Dequeue();
            if (instance.Chats.Count >= 50) return; instance.Chats.Enqueue(now); // a script cannot flood its own console
            AddLog(instance, level, instance.CurrentEvent ?? "load", string.Join(" ", args.GetArray().Select(a => a.ToPrintString())));
        }
        debug["log"] = Fn(args => { Write(ScriptLogLevel.Info, args); return DynValue.Nil; });
        debug["warn"] = Fn(args => { Write(ScriptLogLevel.Warning, args); return DynValue.Nil; });
        debug["error"] = Fn(args => { Write(ScriptLogLevel.Error, args); return DynValue.Nil; });
        script.Globals["print"] = Fn(args => { Write(ScriptLogLevel.Info, args); return DynValue.Nil; });

        // ---- region ----
        var region = Namespace("region");
        region["getName"] = Fn(_ => world.GetRegion() is { } info ? DynValue.NewString(info.Name) : DynValue.Nil);
        region["getId"] = Fn(_ => world.GetRegion() is { } info ? DynValue.NewString(info.Id.ToString()) : DynValue.Nil);
        region["getAvatarCount"] = Fn(_ => world.GetRegion() is { } info ? DynValue.NewNumber(info.AvatarCount) : DynValue.Nil);

        // ---- permissions and dialogs: a script cannot act on a person without being allowed by that person ----
        var permissions = Namespace("permissions");
        permissions["request"] = Fn(args => DynValue.NewBoolean(RequestPermission(instance, AvatarId(args, 0), Plain(args, 1, "permission").ToUpperInvariant())));
        permissions["has"] = Fn(args => DynValue.NewBoolean(_permissions.Has(instance.Definition.Id, AvatarId(args, 0), Plain(args, 1, "permission").ToUpperInvariant())));
        permissions["revoke"] = Fn(args => DynValue.NewBoolean(_permissions.Revoke(instance.Definition.Id, AvatarId(args, 0), Plain(args, 1, "permission").ToUpperInvariant())));
        var dialog = Namespace("dialog");
        dialog["show"] = Fn(args => ShowDialog(instance, AvatarId(args, 0), args.Count > 1 ? args[1] : DynValue.Nil));
        dialog["cancel"] = Fn(args => DynValue.NewBoolean(Guid.TryParse(args.Count > 0 ? args[0].CastToString() : null, out var id) && _dialogs.TryGetValue(id, out var open) && open.Script == instance.Definition.Id && _dialogs.Remove(id)));

        // audio: what the object carries (see object.play_sound)
        var audio = Namespace("audio");
        audio["play"] = Fn(args => PlaySound(instance, args));
        audio["stop"] = Fn(_ => { world.StopSound(instance.Definition); return DynValue.Nil; });
        audio["playAtPosition"] = Fn(args =>
        {
            var name = Plain(args, 0, "sound name"); var at = Limit(new Vec3(Number(args, 1), Number(args, 2), Number(args, 3))); var volume = Math.Clamp(Number(args, 4, 1f), 0f, 1f);
            var now = world.Now; while (instance.Sounds.Count > 0 && now - instance.Sounds.Peek() > 10) instance.Sounds.Dequeue(); if (instance.Sounds.Count >= 5) throw new ScriptRuntimeException("audio.playAtPosition: at most 5 calls every 10 seconds"); instance.Sounds.Enqueue(now);
            return DynValue.NewBoolean(world.PlaySoundAt(instance.Definition, name.Length > 120 ? name[..120] : name, volume, args.Count > 5 && args[5].CastToBool(), at));
        });
    }

    // ---- helpers of the API ----------------------------------------------------------------------------------

    private static string Plain(CallbackArguments args, int index, string what)
    {
        var text = args.Count > index ? args[index].CastToString() : null;
        if (string.IsNullOrWhiteSpace(text)) throw new ScriptRuntimeException($"expected {what}");
        return text.Replace('\n', ' ').Replace('\r', ' ');
    }

    private static float Number(CallbackArguments args, int index, float fallback = 0)
    {
        if (args.Count <= index || args[index].Type == DataType.Nil) return fallback;
        var value = args[index].CastToNumber() ?? throw new ScriptRuntimeException("expected a number");
        if (!double.IsFinite(value)) throw new ScriptRuntimeException("numbers must be finite");
        return (float)value;
    }

    /// <summary>A vector as <c>(x, y, z)</c> starting at the argument, or as one table <c>{x=, y=, z=}</c>.</summary>
    private static Vec3 Vector(CallbackArguments args, int first, int _ = 0)
    {
        if (args.Count > first && args[first].Type == DataType.Table) return ToVec(args, first);
        return new Vec3(Number(args, first), Number(args, first + 1), Number(args, first + 2));
    }

    private Vec3 Limit(Vec3 v) => Clamp(v);

    private static int Channel(CallbackArguments args, int index)
    {
        var channel = (int)Number(args, index);
        if (channel == 0) throw new ScriptRuntimeException("channel 0 is the open chat; use another number (negative numbers are private to your own objects)");
        return channel;
    }

    private static Guid AvatarId(CallbackArguments args, int index) => Guid.TryParse(args.Count > index ? args[index].CastToString() : null, out var id) ? id : throw new ScriptRuntimeException("expected an avatar id");

    private static string StorageKey(CallbackArguments args) => Key(args);

    private static string Rgb(float r, float g, float b) => $"#{(int)Math.Round(Math.Clamp(r, 0, 1) * 255):X2}{(int)Math.Round(Math.Clamp(g, 0, 1) * 255):X2}{(int)Math.Round(Math.Clamp(b, 0, 1) * 255):X2}";
    private static string ColorOf(DynValue value) => ValidColor(value);

    private static DynValue Strings(Script script, IEnumerable<string> values)
    {
        var table = new Table(script); var index = 1; foreach (var value in values.Take(200)) table[index++] = DynValue.NewString(value); return DynValue.NewTable(table);
    }

    private static MotionState Motion(Instance instance) => instance.Motion ??= new MotionState();

    // ---- chat --------------------------------------------------------------------------------------------------

    private void Speak(Instance instance, ChatKind kind, string text)
    {
        var now = world.Now;
        while (instance.Says.Count > 0 && now - instance.Says.Peek() > 10) instance.Says.Dequeue();
        if (instance.Says.Count >= 5) throw new ScriptRuntimeException("chat: at most 5 messages every 10 seconds");
        instance.Says.Enqueue(now); instance.Metrics.MessagesSent++;
        world.Chat(instance.Definition, kind, text.Length > 256 ? text[..256] : text);
    }

    /// <summary>Public channels (positive) reach scripts within 100 m; private channels (negative) only scripts of the same owner. Returns how many listeners got it.</summary>
    private int SendChannel(Instance instance, int channel, string text)
    {
        var now = world.Now;
        while (instance.Chats.Count > 0 && now - instance.Chats.Peek() > 10) instance.Chats.Dequeue();
        if (instance.Chats.Count >= 10) throw new ScriptRuntimeException("sendChannel: at most 10 messages every 10 seconds");
        instance.Chats.Enqueue(now); instance.Metrics.MessagesSent++;
        var me = world.GetObject(instance.Definition.ObjectId); var reached = 0;
        foreach (var listener in _instances.Values.Where(i => i != instance && Live(i) && i.Listening.Contains(channel)).ToList())
        {
            if (channel < 0 && listener.Definition.OwnerUserId != instance.Definition.OwnerUserId) continue;
            if (channel > 0 && me is not null && world.GetObject(listener.Definition.ObjectId) is { } there && Distance(me.Position, there.Position) > 100) continue;
            Enqueue(new ScriptEvent(listener.Definition.ObjectId, "chat", new Dictionary<string, object?> { ["channel"] = channel, ["avatar_name"] = me?.Name ?? "Object", ["sender"] = me?.Name ?? "Object", ["sender_id"] = instance.Definition.ObjectId, ["is_object"] = true, ["message"] = text.Length > 256 ? text[..256] : text }, listener.Definition.Id));
            reached++;
        }
        return reached;
    }

    private static float Distance(Vec3 a, Vec3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    private bool SendLinkMessage(Instance instance, string text)
    {
        var now = world.Now;
        while (instance.LinkMessages.Count > 0 && now - instance.LinkMessages.Peek() > 10) instance.LinkMessages.Dequeue();
        if (instance.LinkMessages.Count >= 20) throw new ScriptRuntimeException("link messages: at most 20 every 10 seconds");
        instance.LinkMessages.Enqueue(now); instance.Metrics.MessagesSent++;
        var parts = world.GetLinkset(instance.Definition.ObjectId).Where(p => p.OwnerUserId == instance.Definition.OwnerUserId).Select(p => p.Id).ToHashSet(); parts.Add(instance.Definition.ObjectId);
        var any = false;
        foreach (var other in _instances.Values.Where(i => i != instance && parts.Contains(i.Definition.ObjectId) && Live(i)).ToList())
        { Enqueue(new ScriptEvent(other.Definition.ObjectId, "link_message", new Dictionary<string, object?> { ["message"] = text.Length > 256 ? text[..256] : text, ["from"] = instance.Definition.ObjectId, ["from_script"] = instance.Definition.Id }, other.Definition.Id)); any = true; }
        return any;
    }

    /// <summary>A message to the other objects of the same owner that have scripts (at most 50), at most three times every ten seconds.</summary>
    private int Broadcast(Instance instance, string text)
    {
        var now = world.Now;
        while (instance.Directs.Count > 0 && now - instance.Directs.Peek() > 10) instance.Directs.Dequeue();
        if (instance.Directs.Count >= 3) throw new ScriptRuntimeException("broadcastMessage: at most 3 times every 10 seconds");
        instance.Directs.Enqueue(now); instance.Metrics.MessagesSent++;
        var sent = 0;
        foreach (var target in _instances.Values.Where(i => i.Definition.ObjectId != instance.Definition.ObjectId && i.Definition.OwnerUserId == instance.Definition.OwnerUserId && Live(i)).Select(i => i.Definition.ObjectId).Distinct().Take(MaxBroadcastTargets).ToList())
        { Enqueue(new ScriptEvent(target, "message", new Dictionary<string, object?> { ["message"] = text.Length > 256 ? text[..256] : text, ["from"] = instance.Definition.ObjectId })); sent++; }
        return sent;
    }

    /// <summary>A private line to one avatar: allowed to those who allowed it (SEND_MESSAGES) or touched the object in the last five minutes.</summary>
    private bool TellAvatar(Instance instance, Guid avatar, string text)
    {
        var now = world.Now;
        while (instance.Directs.Count > 0 && now - instance.Directs.Peek() > 10) instance.Directs.Dequeue();
        if (instance.Directs.Count >= 5) throw new ScriptRuntimeException("private messages: at most 5 every 10 seconds");
        var touched = _touchers.TryGetValue(instance.Definition.Id, out var seen) && seen.TryGetValue(avatar, out var when) && now - when < 300;
        if (!touched && !_permissions.Has(instance.Definition.Id, avatar, ScriptPermission.SendMessages)) return false;
        instance.Directs.Enqueue(now); instance.Metrics.MessagesSent++;
        return world.TellAvatar(instance.Definition, avatar, text.Length > 256 ? text[..256] : text);
    }

    /// <summary>Remembers who touched the object (so the script may answer them in private for a few minutes).</summary>
    private void NoteToucher(Instance instance, IReadOnlyDictionary<string, object?>? data)
    {
        if (data is null || !data.TryGetValue("avatar_id", out var raw) || raw is not Guid avatar) return;
        if (!_touchers.TryGetValue(instance.Definition.Id, out var seen)) _touchers[instance.Definition.Id] = seen = [];
        seen[avatar] = world.Now; if (seen.Count > 100) foreach (var old in seen.Where(p => world.Now - p.Value > 300).Select(p => p.Key).ToList()) seen.Remove(old);
    }

    // ---- motion toward a target (moveTo / rotateTo) --------------------------------------------------------------

    private void StepMotion(Instance instance, double now)
    {
        if (instance.Motion is not { } motion || now - motion.Last < .25) return;
        var dt = (float)(now - motion.Last); motion.Last = now; var change = new ObjectChange();
        if (motion.Position is { } target && motion.CurrentPosition is { } from)
        {
            var distance = Distance(from, target); var step = motion.Speed * dt;
            motion.CurrentPosition = distance <= step ? target : new Vec3(from.X + (target.X - from.X) / distance * step, from.Y + (target.Y - from.Y) / distance * step, from.Z + (target.Z - from.Z) / distance * step);
            change.Position = motion.CurrentPosition; if (distance <= step) motion.Position = null;
        }
        if (motion.Rotation is { } turn && motion.CurrentRotation is { } angle)
        {
            float Toward(float a, float b) { var delta = ((b - a + 540f) % 360f) - 180f; var step = motion.TurnSpeed * dt; return Math.Abs(delta) <= step ? b : a + Math.Sign(delta) * step; }
            var next = new Vec3(Toward(angle.X, turn.X), Toward(angle.Y, turn.Y), Toward(angle.Z, turn.Z)); motion.CurrentRotation = next; change.Rotation = next;
            if (next == turn) motion.Rotation = null;
        }
        if (!change.IsEmpty) world.ApplyChange(instance.Definition, change);
        if (motion.Position is null && motion.Rotation is null) instance.Motion = null;
    }

    // ---- permissions and dialogs ----------------------------------------------------------------------------------

    private bool RequestPermission(Instance instance, Guid avatar, string permission)
    {
        if (!ScriptPermission.OfAvatars.Contains(permission, StringComparer.Ordinal)) throw new ScriptRuntimeException($"'{permission}' is not asked of avatars (valid: {string.Join(", ", ScriptPermission.OfAvatars)})");
        var id = instance.Definition.Id;
        if (_permissions.Has(id, avatar, permission)) { Enqueue(new ScriptEvent(instance.Definition.ObjectId, "permission_granted", new Dictionary<string, object?> { ["avatar_id"] = avatar, ["permission"] = permission }, id)); return false; }
        if (!_permissions.MayAsk(id, avatar, permission, world.Now)) return false;
        var me = world.GetObject(instance.Definition.ObjectId);
        RaiseRequest(new ScriptRequest(Guid.NewGuid(), id, instance.Definition.ObjectId, me?.Name ?? "Object", avatar, "permission", "Permissão", $"“{me?.Name ?? "Um objeto"}” quer {ScriptPermission.Describe(permission)}.", ["Permitir", "Negar"], permission));
        return true;
    }

    /// <summary>What an avatar answered to a permission request: a grant lasts until it is revoked, a denial is remembered only as the event.</summary>
    public void ResolvePermission(Guid scriptId, Guid avatar, string permission, bool granted)
    {
        if (Find(scriptId) is not { } instance || !ScriptPermission.OfAvatars.Contains(permission, StringComparer.Ordinal)) return;
        if (granted) _permissions.Grant(scriptId, avatar, permission); else _permissions.Revoke(scriptId, avatar, permission);
        Enqueue(new ScriptEvent(instance.Definition.ObjectId, granted ? "permission_granted" : "permission_denied", new Dictionary<string, object?> { ["avatar_id"] = avatar, ["permission"] = permission }, scriptId));
    }

    private DynValue ShowDialog(Instance instance, Guid avatar, DynValue spec)
    {
        if (spec.Type != DataType.Table) throw new ScriptRuntimeException("dialog.show expects a table {title=, message=, buttons={...}}");
        var now = world.Now; foreach (var stale in _dialogs.Where(d => d.Value.Expires < now).Select(d => d.Key).ToList()) _dialogs.Remove(stale);
        if (_dialogs.Count(d => d.Value.Script == instance.Definition.Id) >= MaxPendingDialogs) throw new ScriptRuntimeException($"at most {MaxPendingDialogs} open dialogs per script");
        while (instance.Chats.Count > 0 && now - instance.Chats.Peek() > 10) instance.Chats.Dequeue();
        if (instance.Chats.Count >= 10) throw new ScriptRuntimeException("dialogs: at most 10 every 10 seconds"); instance.Chats.Enqueue(now);
        string Field(string name, string fallback, int max) { var v = spec.Table.Get(name); var text = v.Type == DataType.Nil ? fallback : v.CastToString() ?? fallback; return text.Length > max ? text[..max] : text; }
        var buttons = new List<string>(); if (spec.Table.Get("buttons") is { Type: DataType.Table } list) foreach (var pair in list.Table.Pairs.Take(12)) if (pair.Value.CastToString() is { Length: > 0 } label) buttons.Add(label.Length > 40 ? label[..40] : label);
        var withInput = spec.Table.Get("input").CastToBool(); if (buttons.Count == 0 && !withInput) buttons.Add("OK");
        var timeout = Math.Clamp(spec.Table.Get("timeout").CastToNumber() ?? 60, 5, 300);
        var me = world.GetObject(instance.Definition.ObjectId); var id = Guid.NewGuid();
        _dialogs[id] = (instance.Definition.Id, avatar, now + timeout);
        RaiseRequest(new ScriptRequest(id, instance.Definition.Id, instance.Definition.ObjectId, me?.Name ?? "Object", avatar, "dialog", Field("title", me?.Name ?? "Objeto", 60), Field("message", "", 400), buttons, null, withInput, timeout));
        return DynValue.NewString(id.ToString());
    }

    /// <summary>The answer of the avatar to a dialog. False when there is no such open dialog for that avatar (a made-up or expired id, or somebody else's dialog): nothing happens then.</summary>
    public bool RespondDialog(Guid requestId, Guid avatar, string response, string? input = null)
    {
        if (!_dialogs.TryGetValue(requestId, out var open) || open.Avatar != avatar || open.Expires < world.Now) return false;
        _dialogs.Remove(requestId);
        if (Find(open.Script) is not { } instance) return false;
        Enqueue(new ScriptEvent(instance.Definition.ObjectId, "dialog_response", new Dictionary<string, object?> { ["avatar_id"] = avatar, ["response"] = response.Length > 120 ? response[..120] : response, ["input"] = input is null ? null : input.Length > 200 ? input[..200] : input, ["request_id"] = requestId }, open.Script));
        return true;
    }
}

public sealed partial class ScriptHost
{
    private const int MaxTableJson = 2048, MaxTableEntries = 64, MaxTableDepth = 3;

    /// <summary>A small table (numbers, text, booleans and tables of those, three levels deep, 64 entries, 2 KiB) as JSON: what <c>storage.set</c> keeps.</summary>
    private static string TableJson(Table table)
    {
        var entries = 0;
        object? Convert(DynValue value, int depth)
        {
            if (++entries > MaxTableEntries) throw new ScriptRuntimeException($"stored tables have at most {MaxTableEntries} entries");
            switch (value.Type)
            {
                case DataType.Number when double.IsFinite(value.Number): return value.Number;
                case DataType.String when value.String.Length <= 512: return value.String;
                case DataType.Boolean: return value.Boolean;
                case DataType.Table:
                    if (depth >= MaxTableDepth) throw new ScriptRuntimeException($"stored tables are at most {MaxTableDepth} levels deep");
                    var pairs = value.Table.Pairs.ToList();
                    if (pairs.Count > 0 && pairs.Select(p => p.Key).All(k => k.Type == DataType.Number) && pairs.Select(p => (int)p.Key.Number).OrderBy(n => n).SequenceEqual(Enumerable.Range(1, pairs.Count)))
                        return pairs.OrderBy(p => p.Key.Number).Select(p => Convert(p.Value, depth + 1)).ToList();
                    var map = new Dictionary<string, object?>();
                    foreach (var pair in pairs)
                    {
                        if (pair.Key.Type is not (DataType.String or DataType.Number)) throw new ScriptRuntimeException("stored tables use text or number keys");
                        map[pair.Key.CastToString()] = Convert(pair.Value, depth + 1);
                    }
                    return map;
                default: throw new ScriptRuntimeException("state values inside tables are numbers, text, booleans and tables of those");
            }
        }
        var json = JsonSerializer.Serialize(Convert(DynValue.NewTable(table), 0));
        if (json.Length > MaxTableJson) throw new ScriptRuntimeException($"a stored table is at most {MaxTableJson} characters long once saved");
        return json;
    }

    private static Table JsonTable(Script script, JsonElement element)
    {
        var table = new Table(script);
        DynValue Value(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Number => DynValue.NewNumber(e.GetDouble()), JsonValueKind.String => DynValue.NewString(e.GetString()), JsonValueKind.True => DynValue.True, JsonValueKind.False => DynValue.False,
            JsonValueKind.Object or JsonValueKind.Array => DynValue.NewTable(JsonTable(script, e)), _ => DynValue.Nil
        };
        if (element.ValueKind == JsonValueKind.Array) { var i = 1; foreach (var item in element.EnumerateArray()) table[i++] = Value(item); }
        else foreach (var property in element.EnumerateObject()) table[property.Name] = Value(property.Value);
        return table;
    }
}

public sealed partial class ScriptHost
{
    private const int MaxPendingHttp = 3;
    private readonly Dictionary<Guid, (Guid Script, double Started)> _http = [];

    private void InstallHttpApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var http = new Table(script); script.Globals["http"] = http;
        http["request"] = DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return StartHttp(instance, args.Count > 0 ? args[0] : DynValue.Nil); });
    }

    private DynValue StartHttp(Instance instance, DynValue spec)
    {
        if (!_permissions.Has(instance.Definition.Id, Guid.Empty, ScriptPermission.HttpRequests)) throw new ScriptRuntimeException("http.request needs the HTTP_REQUESTS permission: the owner of the object gives it in the editor (tab Permissões)");
        if (spec.Type != DataType.Table) throw new ScriptRuntimeException("http.request expects a table {url=, method=, headers=, body=}");
        var now = world.Now; foreach (var stale in _http.Where(h => now - h.Value.Started > 30).Select(h => h.Key).ToList()) _http.Remove(stale);
        if (_http.Count(h => h.Value.Script == instance.Definition.Id) >= MaxPendingHttp) throw new ScriptRuntimeException($"at most {MaxPendingHttp} requests at the same time per script");
        while (instance.Directs.Count > 0 && now - instance.Directs.Peek() > 60) instance.Directs.Dequeue();
        if (instance.Directs.Count >= 10) throw new ScriptRuntimeException("http.request: at most 10 requests a minute"); instance.Directs.Enqueue(now);
        string Field(string name, int max) { var v = spec.Table.Get(name); var text = v.Type == DataType.Nil ? "" : v.CastToString() ?? ""; return text.Length > max ? throw new ScriptRuntimeException($"{name} is too long") : text; }
        var url = Field("url", 1000); if (url.Length == 0) throw new ScriptRuntimeException("http.request needs a url");
        var method = Field("method", 8).ToUpperInvariant(); if (method.Length == 0) method = "GET";
        var headers = new Dictionary<string, string>(); if (spec.Table.Get("headers") is { Type: DataType.Table } list) foreach (var pair in list.Table.Pairs.Take(9)) { var name = pair.Key.CastToString() ?? ""; headers[name] = pair.Value.CastToString() ?? ""; }
        var body = spec.Table.Get("body").Type == DataType.Nil ? null : Field("body", 8192);
        var id = Guid.NewGuid(); _http[id] = (instance.Definition.Id, now);
        if (!world.StartHttp(instance.Definition, id, new HttpSpec(url, method, headers, body))) CompleteHttp(instance.Definition.Id, id, 0, "", "http_unavailable");
        return DynValue.NewString(id.ToString());
    }

    /// <summary>The answer of a request a script started (or why there is none). The script hears <c>on_http_response</c>; an answer nobody asked for, or twice, does nothing.</summary>
    public void CompleteHttp(Guid scriptId, Guid requestId, int status, string body, string? error)
    {
        if (!_http.TryGetValue(requestId, out var open) || open.Script != scriptId || Find(scriptId) is not { } instance) return;
        _http.Remove(requestId);
        Enqueue(new ScriptEvent(instance.Definition.ObjectId, "http_response", new Dictionary<string, object?> { ["request_id"] = requestId, ["status"] = status, ["body"] = body, ["error"] = error, ["ok"] = error is null && status is >= 200 and < 300 }, scriptId));
    }
}

public sealed partial class ScriptHost
{
    private const double MaxLinearSpeed = 50, MaxAngularSpeed = 20, MaxImpulsePerMass = 200;

    private void InstallPhysicsApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var physics = new Table(script); script.Globals["physics"] = physics;
        void Require()
        {
            if (!_permissions.Has(instance.Definition.Id, Guid.Empty, ScriptPermission.ControlPhysics)) throw new ScriptRuntimeException("physics needs the CONTROL_PHYSICS permission: the owner of the object gives it in the editor (tab Permissões)");
        }
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; Require(); return body(args); });
        PhysicsChange Pending() { _ = instance.Pending ?? throw new ScriptRuntimeException("physics settings can only be changed while handling an event"); return instance.Pending.Physics ??= new PhysicsChange(); }
        void Merge(Func<PhysicsChange, PhysicsChange> change) { Pending(); instance.Pending!.Physics = change(instance.Pending.Physics!); }
        bool Flag(CallbackArguments args) => args.Count > 0 && args[0].CastToBool();
        physics["setEnabled"] = Fn(args => { var on = Flag(args); Merge(p => p with { Collision = on }); return DynValue.Nil; });
        physics["setCollisionEnabled"] = physics["setEnabled"];
        physics["setPhantom"] = Fn(args => { var phantom = Flag(args); Merge(p => p with { Collision = !phantom }); return DynValue.Nil; });
        physics["setDynamic"] = Fn(args => { var on = Flag(args); Merge(p => p with { Physical = on }); return DynValue.Nil; });
        physics["setMass"] = Fn(args => { var mass = Number(args, 0); if (mass is < .01f or > 10_000f) throw new ScriptRuntimeException("mass goes from 0.01 to 10000"); Merge(p => p with { Mass = mass }); return DynValue.Nil; });
        physics["setGravityScale"] = Fn(args => DynValue.NewBoolean(Push(instance, "gravity", new Vec3(Math.Clamp(Number(args, 0), -5f, 5f), 0, 0))));
        physics["applyImpulse"] = Fn(args => DynValue.NewBoolean(Push(instance, "impulse", Limit3(Vector(args, 0), (float)MaxImpulsePerMass * 50))));
        physics["applyForce"] = Fn(args => { var force = Vector(args, 0); return DynValue.NewBoolean(Push(instance, "impulse", Limit3(new Vec3(force.X * .1f, force.Y * .1f, force.Z * .1f), (float)MaxImpulsePerMass * 50))); });   // a force acts for 0.1 s
        physics["setVelocity"] = Fn(args => DynValue.NewBoolean(Push(instance, "velocity", Limit3(Vector(args, 0), (float)MaxLinearSpeed))));
        physics["setAngularVelocity"] = Fn(args => DynValue.NewBoolean(Push(instance, "angular", Limit3(Vector(args, 0), (float)MaxAngularSpeed))));
        physics["getVelocity"] = Fn(_ => world.GetVelocity(instance.Definition.ObjectId) is { } v ? FromVec(script, v) : DynValue.Nil);
    }

    private static Vec3 Limit3(Vec3 v, float most)
    {
        var length = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); if (length <= most || length == 0) return v;
        var scale = most / length; return new Vec3(v.X * scale, v.Y * scale, v.Z * scale);
    }

    private bool Push(Instance instance, string action, Vec3 value)
    {
        var now = world.Now; var queue = instance.Pushes; while (queue.Count > 0 && now - queue.Peek() > 1) queue.Dequeue();
        if (queue.Count >= 10) throw new ScriptRuntimeException("physics: at most 10 pushes per second"); queue.Enqueue(now);
        return world.ApplyPhysics(instance.Definition, action, value);
    }
}

public sealed partial class ScriptHost
{
    public const long MaxPayment = 100_000;
    private readonly Dictionary<Guid, (Guid Script, Guid Avatar, long Amount, double Expires, bool Paying)> _payments = [];

    private void InstallEconomyApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var economy = new Table(script); script.Globals["economy"] = economy;
        economy["requestPayment"] = DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return RequestPayment(instance, AvatarId(args, 0), (long)Math.Floor(Number(args, 1)), args.Count > 2 && args[2].Type != DataType.Nil ? Plain(args, 2, "reason") : ""); });
    }

    /// <summary>Asks an avatar for money for the owner of the object. The avatar sees who asks and how much and must press Pagar: a script cannot take anything. The money goes to whoever owns the object, which the script cannot choose.</summary>
    private DynValue RequestPayment(Instance instance, Guid avatar, long amount, string reason)
    {
        if (amount < 1 || amount > MaxPayment) throw new ScriptRuntimeException($"a payment is from L$ 1 to L$ {MaxPayment:N0}");
        var now = world.Now; foreach (var stale in _payments.Where(p => p.Value.Expires < now && !p.Value.Paying).Select(p => p.Key).ToList()) _payments.Remove(stale);
        if (_payments.Count(p => p.Value.Script == instance.Definition.Id) >= 5) throw new ScriptRuntimeException("at most 5 payments waiting per script");
        while (instance.Pushes.Count > 0 && now - instance.Pushes.Peek() > 60) instance.Pushes.Dequeue();
        if (instance.Pushes.Count >= 10) throw new ScriptRuntimeException("requestPayment: at most 10 requests a minute"); 
        if (!_permissions.MayAsk(instance.Definition.Id, avatar, "PAYMENT", now, 10)) throw new ScriptRuntimeException("the same person was asked for a payment a moment ago; wait 10 seconds");
        instance.Pushes.Enqueue(now);
        var me = world.GetObject(instance.Definition.ObjectId); var id = Guid.NewGuid(); _payments[id] = (instance.Definition.Id, avatar, amount, now + 60, false);
        RaiseRequest(new ScriptRequest(id, instance.Definition.Id, instance.Definition.ObjectId, me?.Name ?? "Object", avatar, "payment", "Pagamento", $"“{me?.Name ?? "Um objeto"}” pede L$ {amount:N0}{(reason.Length > 0 ? $" — {(reason.Length > 120 ? reason[..120] : reason)}" : "")}. O valor vai para o dono do objeto.", ["Pagar", "Recusar"], null, false, 60));
        return DynValue.NewString(id.ToString());
    }

    /// <summary>The avatar answered the payment request. Only the person asked, once, in time; "Pagar" starts the payment, anything else is a refusal.</summary>
    public bool RespondPayment(Guid requestId, Guid avatar, string response)
    {
        if (!_payments.TryGetValue(requestId, out var open) || open.Avatar != avatar || open.Expires < world.Now || open.Paying || Find(open.Script) is not { } instance) return false;
        if (response != "Pagar") { _payments.Remove(requestId); Enqueue(new ScriptEvent(instance.Definition.ObjectId, "payment_failed", new Dictionary<string, object?> { ["request_id"] = requestId, ["avatar_id"] = avatar, ["amount"] = open.Amount, ["reason"] = "declined" }, open.Script)); return true; }
        _payments[requestId] = open with { Paying = true, Expires = world.Now + 30 };
        if (!world.StartPayment(instance.Definition, requestId, avatar, open.Amount)) CompletePayment(open.Script, requestId, null, "unavailable");
        return true;
    }

    /// <summary>The result of a payment as the economy confirmed it: the script hears <c>on_payment</c> only for money that really moved, <c>on_payment_failed</c> otherwise. Repeats and made-up ids do nothing.</summary>
    public void CompletePayment(Guid scriptId, Guid requestId, Guid? transactionId, string? error)
    {
        if (!_payments.TryGetValue(requestId, out var open) || open.Script != scriptId || !open.Paying || Find(scriptId) is not { } instance) return;
        _payments.Remove(requestId);
        var data = new Dictionary<string, object?> { ["request_id"] = requestId, ["avatar_id"] = open.Avatar, ["amount"] = open.Amount, ["avatar"] = new Dictionary<string, object?> { ["id"] = open.Avatar, ["name"] = AvatarName(open.Avatar) } };
        if (error is null) { data["transaction_id"] = transactionId; Enqueue(new ScriptEvent(instance.Definition.ObjectId, "payment", data, scriptId)); }
        else { data["reason"] = error; Enqueue(new ScriptEvent(instance.Definition.ObjectId, "payment_failed", data, scriptId)); }
    }

    private string AvatarName(Guid avatar) => world.AvatarName(avatar) ?? "";
}

public sealed partial class ScriptHost
{
    private void InstallInventoryApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var inventory = new Table(script); script.Globals["inventory"] = inventory;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return body(args); });
        IReadOnlyList<ScriptItem> Items() => world.GetContents(instance.Definition.ObjectId);
        DynValue Describe(ScriptItem item) { var table = new Table(script); table["name"] = item.Name; table["type"] = item.Type; if (item.AssetId is { } asset) table["id"] = asset.ToString(); return DynValue.NewTable(table); }
        ScriptItem? Find(CallbackArguments args) { var name = Plain(args, 0, "item name"); return Items().FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)); }
        inventory["list"] = Fn(_ => { var table = new Table(script); var index = 1; foreach (var item in Items().Take(200)) table[index++] = Describe(item); return DynValue.NewTable(table); });
        inventory["getItem"] = Fn(args => Find(args) is { } item ? Describe(item) : DynValue.Nil);
        inventory["hasItem"] = Fn(args => DynValue.NewBoolean(Find(args) is not null));
        inventory["getItemType"] = Fn(args => Find(args) is { } item ? DynValue.NewString(item.Type) : DynValue.Nil);
        inventory["count"] = Fn(_ => DynValue.NewNumber(Items().Count));
        // giving: a copy of an item of the object to an avatar who allowed it. Starts the delivery; the result comes as on_item_given or on_item_give_failed.
        inventory["giveItem"] = Fn(args =>
        {
            var who = AvatarId(args, 0);
            if (!_permissions.Has(instance.Definition.Id, who, ScriptPermission.GiveItems)) throw new ScriptRuntimeException("this avatar has not allowed GIVE_ITEMS: ask with permissions.request(avatarId, 'GIVE_ITEMS') and wait for on_permission_granted");
            var wanted = Plain(args, 1, "item name"); var item = Items().FirstOrDefault(i => string.Equals(i.Name, wanted, StringComparison.OrdinalIgnoreCase)) ?? throw new ScriptRuntimeException("the object does not carry an item with that name (see inventory.list)");
            if (item.AssetId is not { } asset) throw new ScriptRuntimeException("that item cannot be given");
            if (item.Type is "scripts" or "notecards") throw new ScriptRuntimeException($"items of type '{item.Type}' cannot be given by scripts");
            var now = world.Now; while (instance.Gives.Count > 0 && now - instance.Gives.Peek() > 60) instance.Gives.Dequeue();
            if (instance.Gives.Count >= MaxGivesPerMinute) throw new ScriptRuntimeException($"inventory.giveItem: at most {MaxGivesPerMinute} a minute"); instance.Gives.Enqueue(now);
            return DynValue.NewBoolean(world.StartGiveItem(instance.Definition, who, asset, item.Name));
        });
    }

    public const int MaxGivesPerMinute = 6;

    /// <summary>The result of a delivery as the API reported it: <c>on_item_given</c> when the avatar now has the item, <c>on_item_give_failed</c> (with the reason) otherwise.</summary>
    public void CompleteGiveItem(Guid scriptId, Guid avatarId, string itemName, string? error)
    {
        if (Find(scriptId) is not { } instance) return;
        var data = new Dictionary<string, object?> { ["avatar_id"] = avatarId, ["item"] = itemName, ["avatar"] = new Dictionary<string, object?> { ["id"] = avatarId, ["name"] = AvatarName(avatarId) } };
        if (error is null) Enqueue(new ScriptEvent(instance.Definition.ObjectId, "item_given", data, scriptId));
        else { data["reason"] = error; Enqueue(new ScriptEvent(instance.Definition.ObjectId, "item_give_failed", data, scriptId)); }
    }

    /// <summary>The Conteúdo of the object changed: its scripts hear <c>on_inventory_changed</c> and, for each item, <c>on_item_added</c> or <c>on_item_removed</c> (at most ten items per change).</summary>
    public void ContentsChanged(Guid objectId, IReadOnlyList<ScriptItem> added, IReadOnlyList<ScriptItem> removed)
    {
        if (added.Count == 0 && removed.Count == 0) return;
        Enqueue(new ScriptEvent(objectId, "inventory_changed", new Dictionary<string, object?> { ["added"] = added.Count, ["removed"] = removed.Count }));
        foreach (var item in added.Take(10)) Enqueue(new ScriptEvent(objectId, "item_added", new Dictionary<string, object?> { ["item"] = new Dictionary<string, object?> { ["name"] = item.Name, ["type"] = item.Type, ["id"] = item.AssetId?.ToString() } }));
        foreach (var item in removed.Take(10)) Enqueue(new ScriptEvent(objectId, "item_removed", new Dictionary<string, object?> { ["item"] = new Dictionary<string, object?> { ["name"] = item.Name, ["type"] = item.Type, ["id"] = item.AssetId?.ToString() } }));
    }
}

public sealed partial class ScriptHost
{
    public static readonly string[] Gestures = ["wave", "dance"];
    private readonly Dictionary<Guid, (Guid Script, string Name)> _gestures = [];

    private void InstallAvatarApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var avatar = new Table(script); script.Globals["avatar"] = avatar;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return body(args); });
        void Allowed(Guid who)
        {
            if (!_permissions.Has(instance.Definition.Id, who, ScriptPermission.ControlAnimation)) throw new ScriptRuntimeException("this avatar has not allowed CONTROL_ANIMATION: ask with permissions.request(avatarId, 'CONTROL_ANIMATION') and wait for on_permission_granted");
        }
        avatar["playAnimation"] = Fn(args =>
        {
            var who = AvatarId(args, 0); Allowed(who); var name = Plain(args, 1, "animation name").ToLowerInvariant(); var seconds = Number(args, 2, 4);
            if (!Gestures.Contains(name)) throw new ScriptRuntimeException($"unknown animation '{name}' (available: {string.Join(", ", Gestures)})");
            var now = world.Now; while (instance.Pushes.Count > 0 && now - instance.Pushes.Peek() > 10) instance.Pushes.Dequeue();
            if (instance.Pushes.Count >= 10) throw new ScriptRuntimeException("avatar.playAnimation: at most 10 every 10 seconds"); instance.Pushes.Enqueue(now);
            var ok = world.PlayAnimation(instance.Definition, who, name, Math.Clamp(seconds, 1, 15)); if (ok) _gestures[who] = (instance.Definition.Id, name); return DynValue.NewBoolean(ok);
        });
        avatar["stopAnimation"] = Fn(args => { var who = AvatarId(args, 0); Allowed(who); world.StopAnimation(instance.Definition, who); return DynValue.Nil; });
    }

    /// <summary>A gesture a script started ended. The script that started it hears <c>on_animation_finished</c> with the reason (finished, interrupted, stopped, replaced).</summary>
    public void AnimationEnded(Guid avatarId, string name, string reason)
    {
        if (!_gestures.TryGetValue(avatarId, out var open) || open.Name != name || Find(open.Script) is not { } instance) return;
        _gestures.Remove(avatarId);
        Enqueue(new ScriptEvent(instance.Definition.ObjectId, "animation_finished", new Dictionary<string, object?> { ["avatar_id"] = avatarId, ["name"] = name, ["reason"] = reason, ["avatar"] = new Dictionary<string, object?> { ["id"] = avatarId, ["name"] = AvatarName(avatarId) } }, open.Script));
    }
}
