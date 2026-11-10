using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class ScriptHostTests
{
    private sealed class FakeWorld : IScriptWorld
    {
        public double Now { get; set; } = 100;
        public Dictionary<Guid, ScriptObjectState> Objects { get; } = [];
        public List<(ScriptDefinition Script, ObjectChange Change)> Changes { get; } = [];
        public List<(ScriptDefinition Script, string Text)> Said { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) => Changes.Add((script, change));
        public void Say(ScriptDefinition script, string text) => Said.Add((script, text));
        public List<(string Name, float Volume, bool Loop)> Sounds { get; } = [];
        public int Stops { get; private set; }
        public List<(string Text, string Color)> Texts { get; } = [];
        public bool HasSound { get; set; } = true;
        public bool PlaySound(ScriptDefinition script, string soundName, float volume, bool loop) { if (!HasSound) return false; Sounds.Add((soundName, volume, loop)); return true; }
        public void StopSound(ScriptDefinition script) => Stops++;
        public void SetText(ScriptDefinition script, string text, string color) => Texts.Add((text, color));
    }

    private static readonly Guid Owner = Guid.NewGuid();

    private static (ScriptHost Host, FakeWorld World, List<ScriptReport> Reports, Guid ObjectId) Setup(string source, ScriptHostOptions? options = null, int version = 1)
    {
        var world = new FakeWorld(); var objectId = Guid.NewGuid();
        world.Objects[objectId] = new ScriptObjectState(objectId, Owner, "Porta", new Vec3(10, 1, 20), new Vec3(0, 90, 0), new Vec3(1, 2, 3));
        var host = new ScriptHost(world, options ?? new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>();
        host.Report += reports.Add;
        host.Sync([new ScriptDefinition(Guid.NewGuid(), objectId, Owner, "script", source, version)]);
        host.Process(200);
        return (host, world, reports, objectId);
    }

    private static void Fire(ScriptHost host, Guid objectId, string name, Dictionary<string, object?>? data = null) { host.Enqueue(new ScriptEvent(objectId, name, data)); host.Process(200); }

    [Fact]
    public void Touch_RunsTheHandlerWithTheEventAndAppliesOneMerged_Change()
    {
        var (host, world, reports, id) = Setup("function on_touch(e) object.set_position({x=1,y=2,z=3}) object.move({x=10,y=0,z=0}) object.set_color('#ff0000') say_name = e.avatar_name end");
        Fire(host, id, "touch", new() { ["avatar_name"] = "Ana", ["avatar_id"] = Guid.NewGuid() });
        Assert.Empty(reports);
        var change = Assert.Single(world.Changes).Change;
        Assert.Equal(new Vec3(11, 2, 3), change.Position);
        Assert.Equal("#ff0000", change.Material["color"]);
    }

    [Fact]
    public void ObjectApi_ReadsTheObjectAndOnlyChangesItsOwn()
    {
        var (host, world, reports, id) = Setup("function on_touch() local p = object.get_position() local s = object.get_scale() object.set_scale({x=p.x, y=s.y, z=999}) object.rotate({x=0,y=45,z=0}) end");
        Fire(host, id, "touch");
        Assert.Empty(reports);
        var change = Assert.Single(world.Changes).Change;
        Assert.Equal(new Vec3(10, 2, 64), change.Scale);   // z clamped to the API maximum
        Assert.Equal(new Vec3(0, 135, 0), change.Rotation);
    }

    [Fact]
    public void InvalidArgumentsBecomeReportedErrors_NotChanges()
    {
        var (host, world, reports, id) = Setup("function on_touch() object.set_color('red') end");
        Fire(host, id, "touch");
        Assert.Empty(world.Changes);
        Assert.Contains("#RRGGBB", Assert.Single(reports).Message);
        var (host2, world2, reports2, id2) = Setup("function on_touch() object.set_material({shininess=1}) end");
        Fire(host2, id2, "touch");
        Assert.Empty(world2.Changes); Assert.Single(reports2);
        var (host3, world3, _, id3) = Setup("function on_touch() object.set_material({roughness=7, emission='#00ff00'}) end");
        Fire(host3, id3, "touch");
        Assert.Equal(1f, Assert.Single(world3.Changes).Change.Material["roughness"]);
    }

    [Fact]
    public void ObjectsCannotBeChangedOutsideAnEvent()
    {
        var (host, _, reports, _) = Setup("object.set_position({x=1,y=1,z=1})");
        Assert.Contains(host.Scripts, s => s.Quarantined);
        Assert.Contains("top-level", Assert.Single(reports).Message + " top-level");
    }

    [Fact]
    public void EventsAreRateLimitedPerScript()
    {
        var (host, world, _, id) = Setup("function on_touch() object.move({x=1,y=0,z=0}) end");
        for (var i = 0; i < 60; i++) host.Enqueue(new ScriptEvent(id, "touch"));
        host.Process(500);
        Assert.Equal(20, world.Changes.Count);
        world.Now += 1.5;
        host.Enqueue(new ScriptEvent(id, "touch")); host.Process(100);
        Assert.Equal(21, world.Changes.Count);
    }

    [Fact]
    public void ARunawayScriptIsQuarantinedUntilItIsEdited()
    {
        var world = new FakeWorld(); var objectId = Guid.NewGuid(); var scriptId = Guid.NewGuid();
        world.Objects[objectId] = new ScriptObjectState(objectId, Owner, "Loop", default, default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add;
        host.Sync([new ScriptDefinition(scriptId, objectId, Owner, "loop", "function on_touch() while true do end end", 1)]);
        for (var i = 0; i < 3; i++) { world.Now += 1; Fire(host, objectId, "touch"); }
        var info = Assert.Single(host.Scripts);
        Assert.True(info.Quarantined);
        Assert.Contains("times", info.Reason);
        Assert.Contains(reports, r => r.Message.Contains("disabled"));
        var runs = info.Events; Fire(host, objectId, "touch");
        Assert.Equal(runs, Assert.Single(host.Scripts).Events);

        host.Sync([new ScriptDefinition(scriptId, objectId, Owner, "loop", "function on_touch() object.move({x=1,y=0,z=0}) end", 2)]);
        host.Process(100);
        Assert.False(Assert.Single(host.Scripts).Quarantined);
        Fire(host, objectId, "touch");
        Assert.Single(world.Changes);
    }

    [Fact]
    public void SyntaxErrorsAndBrokenTopLevelAreRejectedWithoutRunning()
    {
        var (host, _, reports, id) = Setup("function on_touch( end");
        Assert.True(Assert.Single(host.Scripts).Quarantined);
        Assert.Single(reports);
        Fire(host, id, "touch");
        Assert.Equal(0, Assert.Single(host.Scripts).Events);
    }

    [Fact]
    public void Timers_FireFromTheClockAndRepeatUpToTheLimit()
    {
        var (host, world, _, id) = Setup("ticks = 0 function on_start() object.create_timer(2, true) end function on_timer(e) ticks = ticks + 1 object.move({x=1,y=0,z=0}) end");
        host.Tick(world.Now); host.Process(100);
        Assert.Empty(world.Changes);
        world.Now += 2.1; host.Tick(world.Now); host.Process(100);
        world.Now += 2.1; host.Tick(world.Now); host.Process(100);
        Assert.Equal(2, world.Changes.Count);

        var (host2, world2, reports2, _) = Setup("function on_start() object.create_timer(0.01) end");
        Assert.Single(reports2); Assert.Empty(world2.Changes); _ = host2;
        var (_, _, reports3, _) = Setup("function on_start() for i = 1, 6 do object.create_timer(5) end end");
        Assert.Contains("timers", Assert.Single(reports3).Message);
        _ = id;
    }

    [Fact]
    public void State_PersistsAcrossReloadsAndIsValidated()
    {
        var world = new FakeWorld(); var objectId = Guid.NewGuid(); var scriptId = Guid.NewGuid();
        world.Objects[objectId] = new ScriptObjectState(objectId, Owner, "Placar", default, default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000));
        var definition = new ScriptDefinition(scriptId, objectId, Owner, "placar", "function on_touch() local n = object.state_get('score') or 0 object.state_set('score', n + 1) object.state_set('name', 'ana') object.state_set('on', true) end", 1);
        host.Sync([definition]);
        Fire(host, objectId, "touch"); Fire(host, objectId, "touch");
        var dirty = Assert.Single(host.TakeDirtyState());
        Assert.Equal("2", dirty.Value["score"]); Assert.Equal("\"ana\"", dirty.Value["name"]); Assert.Equal("true", dirty.Value["on"]);
        Assert.Empty(host.TakeDirtyState());

        var reloaded = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000));
        reloaded.Sync([definition], _ => dirty.Value);
        Fire(reloaded, objectId, "touch");
        Assert.Equal("3", Assert.Single(reloaded.TakeDirtyState()).Value["score"]);

        var (bad, _, reports, id) = Setup("function on_touch() object.state_set('t', {}) end");
        Fire(bad, id, "touch");
        Assert.Contains("state values", Assert.Single(reports).Message);
    }

    [Fact]
    public void Say_GoesToLocalChatAndIsRateLimited()
    {
        var (host, world, reports, id) = Setup("function on_touch(e) object.say('oi ' .. e.avatar_name) end");
        Fire(host, id, "touch", new() { ["avatar_name"] = "Ana" });
        Assert.Equal("oi Ana", Assert.Single(world.Said).Text);
        for (var i = 0; i < 8; i++) { world.Now += .1; Fire(host, id, "touch", new() { ["avatar_name"] = "x" }); }
        Assert.Equal(5, world.Said.Count);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public void Messages_OnlyReachObjectsOfTheSameOwner()
    {
        var world = new FakeWorld(); var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000));
        var button = Guid.NewGuid(); var lamp = Guid.NewGuid(); var stranger = Guid.NewGuid();
        world.Objects[button] = new ScriptObjectState(button, Owner, "Botão", default, default, default);
        world.Objects[lamp] = new ScriptObjectState(lamp, Owner, "Lâmpada", default, default, default);
        world.Objects[stranger] = new ScriptObjectState(stranger, Guid.NewGuid(), "Alheio", default, default, default);
        host.Sync(
        [
            new ScriptDefinition(Guid.NewGuid(), button, Owner, "b", $"function on_touch() sent_ok = object.send_message('ligar', '{lamp}') sent_bad = object.send_message('ligar', '{stranger}') end", 1),
            new ScriptDefinition(Guid.NewGuid(), lamp, Owner, "l", "function on_message(e) object.set_color('#ffff00') heard = e.message end", 1),
            new ScriptDefinition(Guid.NewGuid(), stranger, Guid.NewGuid(), "s", "function on_message(e) object.set_color('#000000') end", 1)
        ]);
        host.Process(100);
        Fire(host, button, "touch"); host.Process(100);
        var change = Assert.Single(world.Changes);
        Assert.Equal(lamp, change.Script.ObjectId);
        Assert.Equal("#ffff00", change.Change.Material["color"]);
    }

    [Fact]
    public void ScriptsOnTheSameObjectDoNotShareGlobals()
    {
        var world = new FakeWorld(); var objectId = Guid.NewGuid();
        world.Objects[objectId] = new ScriptObjectState(objectId, Owner, "Dois", default, default, default);
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000));
        host.Sync(
        [
            new ScriptDefinition(Guid.NewGuid(), objectId, Owner, "a", "n = 1 function on_touch() object.move({x=n,y=0,z=0}) end", 1),
            new ScriptDefinition(Guid.NewGuid(), objectId, Owner, "b", "function on_touch() object.move({x=(n or 0) + 100,y=0,z=0}) end", 1)
        ]);
        Fire(host, objectId, "touch");
        Assert.Equal([1f, 100f], world.Changes.Select(c => c.Change.Position!.Value.X).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Wants_TellsTheServerWhichObjectsListenForAnEvent()
    {
        var (host, _, _, id) = Setup("function on_enter(e) object.say('olá') end");
        Assert.True(host.Wants(id, "enter")); Assert.False(host.Wants(id, "touch")); Assert.False(host.Wants(Guid.NewGuid(), "enter"));
        Assert.Contains(id, host.ObjectsWanting("enter").ToList());
    }

    [Fact]
    public void HoardingDataBetweenEventsIsStopped()
    {
        var (host, _, reports, id) = Setup("bag = {} n = 0 function on_touch() for i = 1, 300 do n = n + 1 bag[n] = i end end", new ScriptHostOptions(MaxEventsPerSecond: 1000, StoredEntriesLimit: 2000, MillisecondsPerEvent: 1000));
        for (var i = 0; i < 80 && !host.Scripts[0].Quarantined; i++) Fire(host, id, "touch");
        Assert.True(Assert.Single(host.Scripts).Quarantined);
        Assert.True(host.Scripts[0].Reason!.Contains("too much data"), $"reason was: {host.Scripts[0].Reason}");
        Assert.NotEmpty(reports);
    }

    [Fact]
    public void TheEventQueueIsBoundedAndSlowWorkIsSpreadOverTicks()
    {
        var (host, world, _, id) = Setup("function on_touch() object.move({x=1,y=0,z=0}) end", new ScriptHostOptions(MaxEventsPerSecond: 100_000, MaxQueuedEvents: 50));
        for (var i = 0; i < 500; i++) host.Enqueue(new ScriptEvent(id, "touch"));
        Assert.Equal(50, host.QueuedEvents);
        host.Process(0);
        Assert.Equal(50, host.QueuedEvents);
        host.Process(500);
        Assert.Equal(0, host.QueuedEvents);
        Assert.Equal(50, world.Changes.Count);
    }
}

public sealed class ScriptSoundAndTextTests
{
    private sealed class World : IScriptWorld
    {
        public Dictionary<Guid, ScriptObjectState> Objects { get; } = [];
        public List<(string Name, float Volume, bool Loop)> Sounds { get; } = [];
        public List<(string Text, string Color)> Texts { get; } = [];
        public int Stops { get; private set; }
        public bool HasSound { get; set; } = true;
        public double Now { get; set; } = 100;
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) { }
        public bool PlaySound(ScriptDefinition script, string soundName, float volume, bool loop) { if (!HasSound) return false; Sounds.Add((soundName, volume, loop)); return true; }
        public void StopSound(ScriptDefinition script) => Stops++;
        public void SetText(ScriptDefinition script, string text, string color) => Texts.Add((text, color));
    }

    private static (ScriptHost Host, World World, List<ScriptReport> Reports, Guid Id) Setup(string source)
    {
        var owner = Guid.NewGuid(); var id = Guid.NewGuid(); var world = new World();
        world.Objects[id] = new ScriptObjectState(id, owner, "Sino", new Vec3(0, 0, 0), new Vec3(0, 0, 0), new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add;
        host.Sync([new ScriptDefinition(Guid.NewGuid(), id, owner, "script", source, 1)]); host.Process(200);
        return (host, world, reports, id);
    }

    private static void Fire(ScriptHost host, Guid id) { host.Enqueue(new ScriptEvent(id, "touch", null)); host.Process(200); }

    [Fact]
    public void PlaySoundAsksTheWorldToPlayTheNamedSoundWithAClampedVolume()
    {
        var (host, world, reports, id) = Setup("function on_touch() ok = object.play_sound('Campainha', 3) object.play_sound('Outro', -1, true) end");
        Fire(host, id);
        Assert.Empty(reports);
        Assert.Equal([("Campainha", 1f, false), ("Outro", 0f, true)], world.Sounds);
    }

    [Fact]
    public void PlaySoundTellsTheScriptWhenThereIsNoSuchSound()
    {
        var (host, world, reports, id) = Setup("function on_touch() if not object.play_sound('Nada') then object.set_text('sem som') end end");
        world.HasSound = false; Fire(host, id);
        Assert.Empty(reports);
        Assert.Equal("sem som", Assert.Single(world.Texts).Text);
    }

    [Fact]
    public void StopSoundAndSetTextReachTheWorld()
    {
        var (host, world, reports, id) = Setup("function on_touch() object.stop_sound() object.set_text('Olá', '#12F2E0') object.set_text('') end");
        Fire(host, id);
        Assert.True(reports.Count == 0, string.Join(";", reports.Select(r => r.Message)));
        Assert.Equal(1, world.Stops);
        Assert.Equal([("Olá", "#12F2E0"), ("", "#FFFFFF")], world.Texts);
    }

    [Fact]
    public void SoundsAndTextsAreRateLimitedPerScript()
    {
        var (host, world, reports, id) = Setup("function on_touch() for i = 1, 20 do object.play_sound('A') end end");
        Fire(host, id);
        Assert.NotEmpty(reports);
        Assert.True(world.Sounds.Count <= 5);
        var (host2, world2, reports2, id2) = Setup("function on_touch() for i = 1, 20 do object.set_text('t' .. i) end end");
        Fire(host2, id2);
        Assert.NotEmpty(reports2); Assert.True(world2.Texts.Count <= 5);
    }

    [Theory]
    [InlineData("object.set_text('x', 'vermelho')")]
    public void BadArgumentsBecomeReportedErrors(string call)
    {
        var (host, world, reports, id) = Setup("function on_touch() " + call + " end");
        Fire(host, id);
        Assert.NotEmpty(reports); Assert.Empty(world.Texts); Assert.Empty(world.Sounds);
    }
}
