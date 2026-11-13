using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The HUD a script shows on the screen of an avatar who allowed it: one throttled picture of what the script set, clicks and texts accepted only for what is really there, closing, and taking it down when the permission or the script goes.</summary>
public sealed class LuaHudTests
{
    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100;
        public Dictionary<Guid, ScriptObjectState> Objects { get; } = [];
        public List<string> Said { get; } = [];
        public List<(Guid Avatar, HudSnapshot Hud)> Huds { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
        public string? AvatarName(Guid avatarId) => "Ana";
        public void ShowHud(ScriptDefinition script, Guid avatarId, HudSnapshot hud) => Huds.Add((avatarId, hud));
    }

    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class Rig
    {
        public World World { get; } = new(); public ScriptHost Host { get; } public List<ScriptReport> Reports { get; } = [];
        public Guid Object { get; } = Guid.NewGuid(); public Guid Script { get; } = Guid.NewGuid();
        public Rig(string source)
        {
            World.Objects[Object] = new ScriptObjectState(Object, Owner, "Painel", default, default, new Vec3(1, 1, 1));
            Host = new ScriptHost(World, new ScriptHostOptions(MillisecondsPerEvent: 2000)); Host.Report += Reports.Add;
            Host.Sync([new ScriptDefinition(Script, Object, Owner, "painel", source, 1)]); Host.Process(500);
        }
        public void Touch(Guid avatar, string? what = null) { Host.Enqueue(new ScriptEvent(Object, "touch", new Dictionary<string, object?> { ["avatar_id"] = avatar, ["what"] = what })); Host.Process(500); }
        public void Advance(double seconds) { World.Now += seconds; Host.Tick(World.Now); Host.Process(500); }
        public void Allow(Guid avatar) => Host.ResolvePermission(Script, avatar, ScriptPermission.ShowHud, true);
    }

    private const string Panel = "function on_touch(e) hud.show(e.avatar_id) hud.setTitle(e.avatar_id, 'Loja') hud.setLabel(e.avatar_id, 'status', 'Vida: 100', '#3BF45F') hud.setButton(e.avatar_id, 'ativar', 'Ativar') hud.setProgress(e.avatar_id, 'energia', 70, 100, 'Energia') hud.setInput(e.avatar_id, 'nome', 'Seu nome') end "
        + "function on_hud_click(e) chat.say('clique ' .. e.element .. ' ' .. e.avatar.name) end function on_hud_input(e) chat.say('texto ' .. e.element .. ' ' .. e.text) end function on_hud_closed(e) chat.say('fechou ' .. e.avatar.name) end";

    [Fact]
    public void NeedsThePermissionOfTheAvatar_AndIsSentAsOneThrottledPicture()
    {
        var rig = new Rig(Panel); var ana = Guid.NewGuid();
        rig.Touch(ana); Assert.Contains("SHOW_HUD", Assert.Single(rig.Reports).Message); rig.Advance(1); Assert.Empty(rig.World.Huds);                                              // nothing on a screen that did not agree
        rig.Allow(ana); rig.Touch(ana); Assert.Empty(rig.World.Huds);                                                                                                                  // built now, sent on the next tick
        rig.Advance(.3); var (avatar, hud) = Assert.Single(rig.World.Huds); Assert.Equal(ana, avatar); Assert.True(hud.Visible); Assert.Equal(("Loja", 260), (hud.Title, hud.Width));
        Assert.Equal(["label:status:Vida: 100:#3BF45F", "button:ativar:Ativar:", "progress:energia:Energia:", "input:nome::"], hud.Elements.Select(e => $"{e.Type}:{e.Id}:{(e.Type == "input" ? "" : e.Text)}:{e.Color}"));
        Assert.Equal((70d, 100d, "Seu nome"), (hud.Elements[2].Value, hud.Elements[2].Max, hud.Elements[3].Placeholder));
        rig.Advance(1); Assert.Single(rig.World.Huds);                                                                                                                                  // nothing changed: nothing sent again
        rig.Touch(ana); rig.World.Now += .01; rig.Touch(ana); rig.Advance(.3); Assert.Equal(2, rig.World.Huds.Count);                                                                   // changes within a moment: one picture
    }

    [Fact]
    public void ClicksAndInputsAreAcceptedOnlyForWhatIsReallyThere()
    {
        var rig = new Rig(Panel); var ana = Guid.NewGuid(); var bruno = Guid.NewGuid(); rig.Allow(ana);
        Assert.False(rig.Host.HudAction(rig.Script, ana, "click", "ativar", null));                                                                                                     // no HUD yet
        rig.Touch(ana); rig.Advance(.3);
        Assert.True(rig.Host.HudAction(rig.Script, ana, "click", "ativar", null)); rig.Host.Process(500); Assert.Contains("clique ativar Ana", rig.World.Said);
        Assert.False(rig.Host.HudAction(rig.Script, ana, "click", "status", null)); Assert.False(rig.Host.HudAction(rig.Script, ana, "click", "inventado", null)); Assert.False(rig.Host.HudAction(rig.Script, bruno, "click", "ativar", null));   // a label is not a button; a made-up element; somebody else's HUD
        Assert.False(rig.Host.HudAction(rig.Script, ana, "input", "ativar", "x")); Assert.True(rig.Host.HudAction(rig.Script, ana, "input", "nome", new string('a', 500))); rig.Host.Process(500); Assert.Contains("texto nome " + new string('a', 200), rig.World.Said);
        Assert.False(rig.Host.HudAction(rig.Script, ana, "explode", "ativar", null));
        for (var i = 0; i < 30; i++) rig.Host.HudAction(rig.Script, ana, "click", "ativar", null);
        rig.Host.Process(500); Assert.True(rig.World.Said.Count(s => s.StartsWith("clique", StringComparison.Ordinal)) <= 11);                                                          // a stuck finger does not flood the script
    }

    [Fact]
    public void ClosedByThePlayerItIsGone_AndTakenDownWhenThePermissionOrTheScriptGoes()
    {
        var rig = new Rig(Panel); var ana = Guid.NewGuid(); rig.Allow(ana);
        rig.Touch(ana); rig.Advance(.3); Assert.True(rig.Host.HasHud(rig.Script, ana)); Assert.Single(rig.Host.HudsOf(ana));
        Assert.True(rig.Host.HudAction(rig.Script, ana, "close", "", null)); rig.Host.Process(500); Assert.Contains("fechou Ana", rig.World.Said); Assert.False(rig.Host.HasHud(rig.Script, ana)); Assert.Empty(rig.Host.HudsOf(ana));
        Assert.False(rig.Host.HudAction(rig.Script, ana, "click", "ativar", null));                                                                                                     // a closed HUD takes no clicks
        rig.World.Now += 6; rig.Touch(ana); rig.Advance(.3); Assert.True(rig.Host.HasHud(rig.Script, ana));                                                                              // the script shows it again when it wants
        var before = rig.World.Huds.Count; rig.Host.Permissions.Revoke(rig.Script, ana, ScriptPermission.ShowHud); rig.Advance(.3);
        Assert.Equal(before + 1, rig.World.Huds.Count); Assert.False(rig.World.Huds.Last().Hud.Visible); Assert.False(rig.Host.HasHud(rig.Script, ana));                                // permission taken back: the panel leaves the screen
        rig.Allow(ana); rig.Touch(ana); rig.Advance(.3); Assert.True(rig.World.Huds.Last().Hud.Visible);
        rig.Host.Stop(rig.Script); Assert.False(rig.World.Huds.Last().Hud.Visible); Assert.Empty(rig.Host.HudsOf(ana));                                                                  // a stopped script leaves nothing behind
    }

    [Fact]
    public void LimitsAndValidation()
    {
        var rig = new Rig("function on_touch(e) local a = e.avatar_id hud.show(a) if e.what == 'many' then for i = 1, 13 do hud.setLabel(a, 'l' .. i, 'x') end end if e.what == 'id' then hud.setLabel(a, 'bad id!', 'x') end if e.what == 'size' then hud.clear(a) hud.setSize(a, 9999) hud.setPosition(a, -3, 7) hud.setLabel(a, 'long', string.rep('x', 200)) end end");
        var ana = Guid.NewGuid(); rig.Allow(ana);
        rig.Touch(ana, "many"); Assert.Contains("at most 12 elements", rig.Reports.Last().Message); rig.World.Now += 6;
        rig.Touch(ana, "id"); Assert.Contains("element ids", rig.Reports.Last().Message); rig.World.Now += 6;
        rig.Touch(ana, "size"); rig.Advance(.3); var hud = rig.World.Huds.Last().Hud; Assert.Equal((420, .95f, .05f), (hud.Width, hud.Y, hud.X)); Assert.Equal(120, hud.Elements.Single(e => e.Id == "long").Text.Length);
    }
}
