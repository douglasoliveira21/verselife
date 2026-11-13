using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

public sealed partial class ScriptHost
{
    public const int MaxHudElements = 12, MaxHudText = 120;
    private static readonly Regex HudId = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

    private sealed class Hud
    {
        public bool Visible, Dirty, ClosedByPlayer; public string Title = ""; public float X = .8f, Y = .25f; public int Width = 260;
        public List<HudElement> Elements = []; public double LastSent = double.NegativeInfinity; public Queue<double> Actions = new();
    }

    private readonly Dictionary<(Guid Script, Guid Avatar), Hud> _huds = [];

    private void InstallHudApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var hud = new Table(script); script.Globals["hud"] = hud;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return body(args); });
        Hud Of(CallbackArguments args, bool create = true)
        {
            var avatar = AvatarId(args, 0);
            if (!_permissions.Has(instance.Definition.Id, avatar, ScriptPermission.ShowHud)) throw new ScriptRuntimeException("this avatar has not allowed SHOW_HUD: ask with permissions.request(avatarId, 'SHOW_HUD') and wait for on_permission_granted");
            if (_huds.TryGetValue((instance.Definition.Id, avatar), out var found)) return found;
            if (!create) return new Hud();
            if (_huds.Count(h => h.Key.Script == instance.Definition.Id) >= 20) throw new ScriptRuntimeException("a script keeps at most 20 HUDs");
            return _huds[(instance.Definition.Id, avatar)] = new Hud();
        }
        static string Short(string text) => text.Length > MaxHudText ? text[..MaxHudText] : text;
        static string Id(CallbackArguments args, int index) { var id = Plain(args, index, "element id"); return HudId.IsMatch(id) ? id : throw new ScriptRuntimeException("element ids are 1 to 32 letters, numbers, _ or -"); }
        static void Put(Hud h, HudElement element)
        {
            var at = h.Elements.FindIndex(e => e.Id == element.Id);
            if (at >= 0) h.Elements[at] = element; else if (h.Elements.Count >= MaxHudElements) throw new ScriptRuntimeException($"a HUD has at most {MaxHudElements} elements"); else h.Elements.Add(element);
            h.Dirty = true;
        }
        hud["show"] = Fn(args => { var h = Of(args); h.Visible = true; h.ClosedByPlayer = false; h.Dirty = true; return DynValue.Nil; });
        hud["hide"] = Fn(args => { var h = Of(args, create: false); h.Visible = false; h.Dirty = true; return DynValue.Nil; });
        hud["setTitle"] = Fn(args => { var h = Of(args); h.Title = Short(Plain(args, 1, "title")); h.Dirty = true; return DynValue.Nil; });
        hud["setPosition"] = Fn(args => { var h = Of(args); h.X = Math.Clamp(Number(args, 1), .05f, .95f); h.Y = Math.Clamp(Number(args, 2), .05f, .95f); h.Dirty = true; return DynValue.Nil; });
        hud["setSize"] = Fn(args => { var h = Of(args); h.Width = (int)Math.Clamp(Number(args, 1), 160f, 420f); h.Dirty = true; return DynValue.Nil; });
        hud["setLabel"] = Fn(args => { var h = Of(args); var color = args.Count > 3 && args[3].Type != DataType.Nil ? ColorOf(args[3]) : ""; Put(h, new HudElement(Id(args, 1), "label", Short(Plain(args, 2, "text")), color, 0, 0, "")); return DynValue.Nil; });
        hud["setButton"] = Fn(args => { var h = Of(args); Put(h, new HudElement(Id(args, 1), "button", Short(Plain(args, 2, "button text")), "", 0, 0, "")); return DynValue.Nil; });
        hud["setProgress"] = Fn(args => { var h = Of(args); var max = Math.Max(1, Number(args, 3, 100)); var value = Math.Clamp(Number(args, 2), 0, max); Put(h, new HudElement(Id(args, 1), "progress", args.Count > 4 ? Short(Plain(args, 4, "text")) : "", "", value, max, "")); return DynValue.Nil; });
        hud["setInput"] = Fn(args => { var h = Of(args); Put(h, new HudElement(Id(args, 1), "input", "", "", 0, 0, args.Count > 2 ? Short(Plain(args, 2, "placeholder")) : "")); return DynValue.Nil; });
        hud["remove"] = Fn(args => { var h = Of(args); var id = Id(args, 1); if (h.Elements.RemoveAll(e => e.Id == id) > 0) h.Dirty = true; return DynValue.Nil; });
        hud["clear"] = Fn(args => { var h = Of(args); h.Elements.Clear(); h.Dirty = true; return DynValue.Nil; });
    }

    /// <summary>Sends the HUDs that changed (at most four times a second each) and takes down those whose script stopped or whose avatar took the permission back.</summary>
    private void FlushHuds(double now)
    {
        foreach (var (key, hud) in _huds.ToList())
        {
            var instance = Find(key.Script);
            if (instance is null || !Live(instance) || !_permissions.Has(key.Script, key.Avatar, ScriptPermission.ShowHud)) { TakeDown(key, hud, instance?.Definition); continue; }
            if (!hud.Dirty || now - hud.LastSent < .25) continue;
            hud.Dirty = false; hud.LastSent = now;
            world.ShowHud(instance.Definition, key.Avatar, new HudSnapshot(hud.Visible, hud.Title, hud.X, hud.Y, hud.Width, [.. hud.Elements]));
        }
    }

    private void TakeDown((Guid Script, Guid Avatar) key, Hud hud, ScriptDefinition? definition)
    {
        _huds.Remove(key);
        if (hud.Visible && definition is not null) world.ShowHud(definition, key.Avatar, new HudSnapshot(false, "", 0, 0, 0, []));
    }

    /// <summary>Takes down every HUD of a script that is going away (the avatars' screens must not keep a dead script's panel).</summary>
    private void CloseHuds(Instance instance)
    {
        foreach (var key in _huds.Keys.Where(k => k.Script == instance.Definition.Id).ToList()) TakeDown(key, _huds[key], instance.Definition);
    }

    /// <summary>The player pressed a button, sent a text or closed the HUD. Accepted only for a HUD that script really shows that avatar, for an element of the right kind, a few times a second.</summary>
    public bool HudAction(Guid scriptId, Guid avatar, string action, string element, string? text)
    {
        if (!_huds.TryGetValue((scriptId, avatar), out var hud) || !hud.Visible || Find(scriptId) is not { } instance || !Live(instance)) return false;
        var now = world.Now; while (hud.Actions.Count > 0 && now - hud.Actions.Peek() > 1) hud.Actions.Dequeue(); if (hud.Actions.Count >= 10) return false; hud.Actions.Enqueue(now);
        var who = new Dictionary<string, object?> { ["id"] = avatar, ["name"] = AvatarName(avatar) };
        if (action == "close") { hud.Visible = false; hud.ClosedByPlayer = true; Enqueue(new ScriptEvent(instance.Definition.ObjectId, "hud_closed", new Dictionary<string, object?> { ["avatar_id"] = avatar, ["avatar"] = who }, scriptId)); return true; }
        var found = hud.Elements.FirstOrDefault(e => e.Id == element); if (found is null) return false;
        if (action == "click" && found.Type == "button") { Enqueue(new ScriptEvent(instance.Definition.ObjectId, "hud_click", new Dictionary<string, object?> { ["avatar_id"] = avatar, ["avatar"] = who, ["element"] = element }, scriptId)); return true; }
        if (action == "input" && found.Type == "input") { Enqueue(new ScriptEvent(instance.Definition.ObjectId, "hud_input", new Dictionary<string, object?> { ["avatar_id"] = avatar, ["avatar"] = who, ["element"] = element, ["text"] = (text ?? "").Length > 200 ? text![..200] : text ?? "" }, scriptId)); return true; }
        return false;
    }

    public bool HasHud(Guid scriptId, Guid avatar) => _huds.TryGetValue((scriptId, avatar), out var hud) && hud.Visible;

    /// <summary>The HUDs that should be on the screen of an avatar (for when they arrive or come back).</summary>
    public IReadOnlyList<(ScriptDefinition Script, HudSnapshot Hud)> HudsOf(Guid avatar) =>
        [.. _huds.Where(h => h.Key.Avatar == avatar && h.Value.Visible && Find(h.Key.Script) is not null).Select(h => (Find(h.Key.Script)!.Definition, new HudSnapshot(true, h.Value.Title, h.Value.X, h.Value.Y, h.Value.Width, [.. h.Value.Elements])))];
}
