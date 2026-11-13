using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The second layer of the Lua API (camelCase object calls, chat, timers with callbacks, storage, debug, links, permissions, dialogs), the life cycle of a script (stop, restart, pause, enable, destroy) and its metrics.</summary>
public sealed class LuaApiTests
{
    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100;
        public Dictionary<Guid, ScriptObjectState> Objects { get; } = [];
        public List<(ScriptDefinition Script, ObjectChange Change)> Changes { get; } = [];
        public List<(ChatKind Kind, string Text)> Spoken { get; } = [];
        public List<(Guid Avatar, string Text)> Told { get; } = [];
        public RegionInfo? Region { get; set; } = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Praça Central", 3);
        public List<Guid> Linkset { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) => Changes.Add((script, change));
        public void Say(ScriptDefinition script, string text) => Spoken.Add((ChatKind.Say, text));
        public void Chat(ScriptDefinition script, ChatKind kind, string text) => Spoken.Add((kind, text));
        public bool TellAvatar(ScriptDefinition script, Guid avatarId, string text) { Told.Add((avatarId, text)); return true; }
        public RegionInfo? GetRegion() => Region;
        public List<(Guid Request, HttpSpec Spec)> Http { get; } = []; public bool HttpWorks { get; set; } = true;
        public bool StartHttp(ScriptDefinition script, Guid requestId, HttpSpec spec) { Http.Add((requestId, spec)); return HttpWorks; }
        public List<(string Action, Vec3 Value)> Pushes { get; } = []; public Vec3? Velocity { get; set; } = new Vec3(1, 2, 3); public bool Simulated { get; set; } = true;
        public bool ApplyPhysics(ScriptDefinition script, string action, Vec3 value) { Pushes.Add((action, value)); return Simulated; }
        public Vec3? GetVelocity(Guid objectId) => Velocity;
        public List<(Guid Request, Guid Avatar, long Amount)> Payments { get; } = []; public bool PaymentsWork { get; set; } = true;
        public bool StartPayment(ScriptDefinition script, Guid requestId, Guid avatarId, long amount) { Payments.Add((requestId, avatarId, amount)); return PaymentsWork; }
        public string? AvatarName(Guid avatarId) => "Ana";
        public List<ScriptItem> Contents { get; } = [];
        public List<(Guid Avatar, string Name, double Seconds)> Played { get; } = []; public List<Guid> Stopped { get; } = [];
        public bool PlayAnimation(ScriptDefinition script, Guid avatarId, string name, double seconds) { Played.Add((avatarId, name, seconds)); return true; }
        public void StopAnimation(ScriptDefinition script, Guid avatarId) => Stopped.Add(avatarId);
        public IReadOnlyList<ScriptItem> GetContents(Guid objectId) => Contents;
        public IReadOnlyList<ScriptObjectState> GetLinkset(Guid objectId) => Linkset.Count > 0 && Linkset.Contains(objectId) ? [.. Linkset.Select(i => Objects[i])] : Objects.TryGetValue(objectId, out var one) ? [one] : [];
    }

    private static readonly Guid Owner = Guid.NewGuid(), Other = Guid.NewGuid();

    private sealed class Rig
    {
        public World World { get; } = new(); public ScriptHost Host { get; }
        public List<ScriptReport> Reports { get; } = []; public List<ScriptRequest> Requests { get; } = [];
        public Rig(ScriptHostOptions? options = null) { Host = new ScriptHost(World, options ?? new ScriptHostOptions(MillisecondsPerEvent: 2000)); Host.Report += Reports.Add; Host.Request += Requests.Add; }
        public (Guid Object, Guid Script) Add(string source, Guid? owner = null, string name = "Porta", Vec3? position = null, Guid? objectId = null, bool sync = true)
        {
            var id = objectId ?? Guid.NewGuid(); var script = Guid.NewGuid();
            if (!World.Objects.ContainsKey(id)) World.Objects[id] = new ScriptObjectState(id, owner ?? Owner, name, position ?? new Vec3(10, 1, 20), new Vec3(0, 90, 0), new Vec3(1, 2, 3));
            Definitions.Add(new ScriptDefinition(script, id, owner ?? Owner, name + "-script", source, 1));
            if (sync) Sync(); return (id, script);
        }
        public List<ScriptDefinition> Definitions { get; } = [];
        public void Sync() { Host.Sync(Definitions); Host.Process(500); }
        public void Fire(Guid obj, string name, Dictionary<string, object?>? data = null, Guid? only = null) { Host.Enqueue(new ScriptEvent(obj, name, data, only)); Host.Process(500); }
        public void Advance(double seconds) { World.Now += seconds; Host.Tick(World.Now); Host.Process(500); }
    }

    // ---- camelCase object API ---------------------------------------------------------------------------------------

    [Fact]
    public void ObjectApi_CamelCase_ChangesTheObjectInOneMergedChange()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch(e) object.setPosition(1, 2, 3) object.setRotation(0, 45, 0) object.setScale(2, 2, 2) object.setColor(1, 0, 0) object.setAlpha(0.25) object.setGlow(0.5) object.setName('Porta nova') local p = object.getPosition() seen = p.x end");
        rig.Fire(obj, "touch", new() { ["avatar_id"] = Guid.NewGuid() });
        Assert.Empty(rig.Reports);
        var change = Assert.Single(rig.World.Changes).Change;
        Assert.Equal(new Vec3(1, 2, 3), change.Position); Assert.Equal(new Vec3(0, 45, 0), change.Rotation); Assert.Equal(new Vec3(2, 2, 2), change.Scale); Assert.Equal("Porta nova", change.Name);
        Assert.Equal("#FF0000", change.Material["color"]); Assert.Equal(.75f, (float)change.Material["transparency"! ]!, 3); Assert.Equal("#808080", change.Material["emission"]);
    }

    [Fact]
    public void ObjectApi_StillRefusesBadInput()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch() object.setPosition(0/0, 0, 0) end"); rig.Fire(obj, "touch");
        Assert.Contains("finite", Assert.Single(rig.Reports).Message); Assert.Empty(rig.World.Changes);
        var (obj2, _) = rig.Add("function on_touch() object.setName(string.rep('x', 64)) end", sync: true); rig.Fire(obj2, "touch");
        Assert.Contains("63", rig.Reports.Last().Message);
    }

    [Fact]
    public void MoveTo_TravelsInStepsAtTheGivenSpeedAndStops()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_start() object.moveTo(14, 1, 20, 2) end");
        rig.Advance(.3); rig.Advance(.3); rig.Advance(.3);
        Assert.NotEmpty(rig.World.Changes);
        var xs = rig.World.Changes.Select(c => c.Change.Position!.Value.X).ToList();
        Assert.True(xs.First() > 10 && xs.First() < 11, $"first step {xs.First()}");           // 2 m/s over about 0.3 s
        Assert.True(xs.SequenceEqual(xs.OrderBy(x => x)));
        for (var i = 0; i < 12; i++) rig.Advance(.3);
        Assert.Equal(14f, rig.World.Changes.Last().Change.Position!.Value.X); var count = rig.World.Changes.Count; rig.Advance(.5); Assert.Equal(count, rig.World.Changes.Count);   // arrived: nothing more
        _ = obj;
    }

    [Fact]
    public void RotateTo_TakesTheShortWayAround()
    {
        var rig = new Rig(); rig.Add("function on_start() object.rotateTo(0, 10, 0, 90) end");   // from 90 degrees to 10: backwards, 80 degrees
        for (var i = 0; i < 6; i++) rig.Advance(.3);
        Assert.Equal(10f, rig.World.Changes.Last().Change.Rotation!.Value.Y); Assert.True(rig.World.Changes.First().Change.Rotation!.Value.Y < 90);
    }

    // ---- timers --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Timers_CallTheFunctionOnceOrEveryTime_AndCanBePausedAndCancelled()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("count = 0 function on_start() once = timer.after(2, function() object.setName('depois') end) loop = timer.every(1, function() count = count + 1 chat.say('tic ' .. count) end) end");
        rig.Advance(1.1); rig.Advance(1.1);
        Assert.Contains("tic 1", rig.World.Spoken.Select(s => s.Text)); Assert.Contains("tic 2", rig.World.Spoken.Select(s => s.Text));
        Assert.Contains(rig.World.Changes, c => c.Change.Name == "depois");                      // the one-shot ran once its time came
        Assert.Equal(1, rig.Host.MetricsOf(script)!.ActiveTimers);                                 // only the repeating one is left

        var (obj2, script2) = rig.Add("function on_touch() timer.pause(loop) paused = timer.getRemaining(loop) end function on_start() loop = timer.every(10, function() chat.say('x') end) end");
        rig.Advance(4); rig.Fire(obj2, "touch"); rig.Advance(20); Assert.DoesNotContain("x", rig.World.Spoken.Select(s => s.Text));   // paused: no tick even after a long time
        _ = obj; _ = script2;
    }

    [Fact]
    public void Timers_CancelPauseResumeAndRemaining()
    {
        var rig = new Rig();
        var (obj, _) = rig.Add("function on_start() a = timer.every(5, function() chat.say('a') end) b = timer.after(5, function() chat.say('b') end) end function on_touch(e) timer.cancel(b) timer.pause(a) chat.say('left ' .. string.format('%.0f', timer.getRemaining(a))) end function on_message() timer.resume(a) end");
        rig.Advance(2); rig.Fire(obj, "touch"); Assert.Contains("left 3", rig.World.Spoken.Select(s => s.Text));
        rig.Advance(10); Assert.DoesNotContain("a", rig.World.Spoken.Select(s => s.Text)); Assert.DoesNotContain("b", rig.World.Spoken.Select(s => s.Text));
        rig.Fire(obj, "message"); rig.Advance(3.1); Assert.Contains("a", rig.World.Spoken.Select(s => s.Text));   // resumed with what was left (3 s)
    }

    [Fact]
    public void NamedTimers_FireOnIntervalAndOnDelayComplete()
    {
        var rig = new Rig(); rig.Add("function on_start() timer.after(1, 'pronto') timer.every(2, 'ritmo') end function on_delay_complete(e) chat.say('delay ' .. e.name) end function on_interval(e) chat.say('interval ' .. e.name .. e.count) end");
        rig.Advance(1.1); rig.Advance(1); Assert.Contains("delay pronto", rig.World.Spoken.Select(s => s.Text)); Assert.Contains("interval ritmo1", rig.World.Spoken.Select(s => s.Text));
    }

    [Fact]
    public void Timers_AreLimitedAndNeedAFunction()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch() for i = 1, 40 do timer.every(5, function() end) end end"); rig.Fire(obj, "touch"); Assert.Contains("timers", Assert.Single(rig.Reports).Message);
        var (obj2, _) = rig.Add("function on_touch() timer.after(1, 42) end"); rig.Fire(obj2, "touch"); Assert.Contains("function", rig.Reports.Last().Message);
        var (obj3, _) = rig.Add("function on_touch() timer.after(0.001, function() end) end"); rig.Fire(obj3, "touch"); Assert.Contains("between", rig.Reports.Last().Message);
    }

    // ---- chat --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Chat_SayWhisperShoutAndRateLimit()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch() chat.say('a') chat.whisper('b') chat.shout('c') end");
        rig.Fire(obj, "touch"); Assert.Equal([(ChatKind.Say, "a"), (ChatKind.Whisper, "b"), (ChatKind.Shout, "c")], rig.World.Spoken);
        var (spam, _) = rig.Add("function on_touch() for i = 1, 20 do chat.say('x') end end"); rig.Fire(spam, "touch");
        Assert.Contains("at most 5", Assert.Single(rig.Reports).Message); Assert.Equal(3 + 5, rig.World.Spoken.Count);
    }

    [Fact]
    public void Channels_ReachOnlyListeners_AndPrivateOnesStayWithTheOwner()
    {
        var rig = new Rig();
        var (sender, _) = rig.Add("function on_touch() n = chat.sendChannel(100, 'alarme') m = chat.sendChannel(-5, 'segredo') chat.say(n .. ':' .. m) end", name: "Botão");
        var (mine, _) = rig.Add("function on_start() chat.listen(100) chat.listen(-5) end function on_chat(e) chat.say('mine ' .. e.channel .. ' ' .. e.message .. ' de ' .. e.sender) end", name: "Sirene");
        var (theirs, _) = rig.Add("function on_start() chat.listen(100) chat.listen(-5) end function on_chat(e) chat.say('theirs ' .. e.channel) end", owner: Other, name: "Alheio");
        var (deaf, _) = rig.Add("function on_chat(e) chat.say('deaf') end", name: "Surdo");
        rig.Fire(sender, "touch"); rig.Host.Process(500);
        var said = rig.World.Spoken.Select(s => s.Text).ToList();
        Assert.Contains("mine 100 alarme de Botão", said); Assert.Contains("mine -5 segredo de Botão", said);
        Assert.Contains("theirs 100", said); Assert.DoesNotContain("theirs -5", said);                // a private channel never leaves its owner
        Assert.DoesNotContain("deaf", said); Assert.Contains("2:1", said);
        _ = mine; _ = theirs; _ = deaf;
    }

    [Fact]
    public void Channels_AreRangeLimitedAndRefuseZero()
    {
        var rig = new Rig(); var (near, _) = rig.Add("function on_touch() chat.sendChannel(7, 'oi') end", position: new Vec3(0, 0, 0));
        var (far, _) = rig.Add("function on_start() chat.listen(7) end function on_chat() chat.say('longe') end", position: new Vec3(500, 0, 0), name: "Longe");
        rig.Fire(near, "touch"); Assert.DoesNotContain("longe", rig.World.Spoken.Select(s => s.Text));  // beyond 100 m
        var (zero, _) = rig.Add("function on_touch() chat.listen(0) end"); rig.Fire(zero, "touch"); Assert.Contains("open chat", rig.Reports.Last().Message);
        _ = far;
    }

    [Fact]
    public void PrivateMessages_NeedATouchOrAGrant()
    {
        var rig = new Rig(); var ana = Guid.NewGuid(); var bruno = Guid.NewGuid();
        var (obj, script) = rig.Add("function on_touch(e) ok = chat.sendPrivateMessage(e.avatar_id, 'olá') end function on_message(e) other = chat.sendPrivateMessage('" + bruno + "', 'oi') end");
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); Assert.Equal([(ana, "olá")], rig.World.Told);                      // she touched it
        rig.Fire(obj, "message"); Assert.Single(rig.World.Told);                                                                       // he did not, and has not allowed it
        rig.Host.ResolvePermission(script, bruno, ScriptPermission.SendMessages, true); rig.Fire(obj, "message"); Assert.Equal(2, rig.World.Told.Count);
        rig.World.Now += 400; rig.Fire(obj, "touch", new() { ["avatar_id"] = Guid.NewGuid() });
        Assert.True(rig.Host.Permissions.Revoke(script, bruno, ScriptPermission.SendMessages)); rig.Fire(obj, "message"); Assert.Equal(3, rig.World.Told.Count);   // revoked: effective at once (the third line was the answer to the new person who touched it)
    }

    // ---- storage, debug, region ------------------------------------------------------------------------------------------

    [Fact]
    public void Storage_KeepsTablesAcrossAReload_AndRefusesWhatCannotBeKept()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_touch() storage.set('porta', {aberta = true, voltas = 3, nomes = {'a', 'b'}}) storage.set('n', 5) end function on_message() local t = storage.get('porta') chat.say(tostring(t.aberta) .. t.voltas .. t.nomes[2] .. #storage.keys()) storage.delete('n') end");
        rig.Fire(obj, "touch"); var saved = rig.Host.TakeDirtyState()[script];
        var again = new Rig(); again.Definitions.Add(rig.Definitions[0]); again.World.Objects[obj] = rig.World.Objects[obj]; again.Host.Sync(again.Definitions, _ => saved);
        again.Fire(obj, "message"); Assert.Contains("true3b2", again.World.Spoken.Select(s => s.Text));
        Assert.DoesNotContain("n", again.Host.TakeDirtyState()[script].Keys);
        var (bad, _) = rig.Add("function on_touch() storage.set('x', {f = function() end}) end"); rig.Fire(bad, "touch"); Assert.Contains("state values", rig.Reports.Last().Message);
        var (big, _) = rig.Add("function on_touch() local t = {} for i = 1, 100 do t[i] = i end storage.set('x', t) end"); rig.Fire(big, "touch"); Assert.Contains("entries", rig.Reports.Last().Message);
        var (deep, _) = rig.Add("function on_touch() storage.set('x', {{{{1}}}}) end"); rig.Fire(deep, "touch"); Assert.Contains("levels", rig.Reports.Last().Message);
    }

    [Fact]
    public void Debug_PrintLogsAndErrors_GoToTheConsoleWithTheirContext()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_touch() print('olá', 3) debug.log('info') debug.warn('cuidado') debug.error('ruim') end");
        rig.Fire(obj, "touch");
        var logs = rig.Host.LogsOf(script);
        Assert.Equal([(ScriptLogLevel.Info, "olá 3"), (ScriptLogLevel.Info, "info"), (ScriptLogLevel.Warning, "cuidado"), (ScriptLogLevel.Error, "ruim")], logs.Select(l => (l.Level, l.Text)));
        Assert.All(logs, l => { Assert.Equal("touch", l.Event); Assert.Equal(obj, l.ObjectId); Assert.Equal(script, l.ScriptId); });
        Assert.Null(typeof(ScriptHost).GetMethod("x"));
        var (flood, fs) = rig.Add("function on_touch() for i = 1, 500 do print(i) end end"); rig.Fire(flood, "touch"); Assert.True(rig.Host.LogsOf(fs, 200).Count <= 50);
    }

    [Fact]
    public void DebugNamespace_HasOnlyLogWarnAndError()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch() local n = 0 for k, v in pairs(debug) do n = n + 1 end chat.say('debug=' .. n .. tostring(debug.getinfo) .. tostring(debug.sethook) .. tostring(debug.traceback)) end");
        rig.Fire(obj, "touch"); Assert.Equal("debug=3nilnilnil", rig.World.Spoken.Single().Text);
    }

    [Fact]
    public void Errors_AreRecordedWithTheLine_AndTheScriptHearsOnScriptError()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_touch()\n  local x = nil\n  return x.campo\nend\nfunction on_script_error(e) chat.say('erro em ' .. e.event .. ' linha ' .. e.line) end");
        rig.Fire(obj, "touch");
        var metrics = rig.Host.MetricsOf(script)!; Assert.Equal(1, metrics.Errors); Assert.Equal(3, metrics.LastErrorLine); Assert.NotNull(metrics.LastError);
        Assert.Contains("erro em touch linha 3", rig.World.Spoken.Select(s => s.Text));
        var entry = rig.Host.LogsOf(script).Single(l => l.Level == ScriptLogLevel.Error); Assert.Contains("touch", entry.Event); Assert.Contains("on_touch".Length > 0 ? "touch" : "", entry.StackTrace); Assert.True(entry.Milliseconds >= 0);
        Assert.Equal(ScriptLifecycle.Waiting, metrics.State);                                                                      // one error does not stop a script
    }

    [Fact]
    public void Region_AnswersFromTheWorld()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch() chat.say(region.getName() .. ' ' .. region.getAvatarCount() .. ' ' .. region.getId() .. ' ' .. object.getRegion()) end");
        rig.Fire(obj, "touch"); Assert.Equal("Praça Central 3 11111111-1111-1111-1111-111111111111 11111111-1111-1111-1111-111111111111", rig.World.Spoken.Single().Text);
    }

    // ---- links -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Linkset_ListsPartsChangesThemAndTalksBetweenThem()
    {
        var rig = new Rig(); var rootId = Guid.NewGuid(); var childId = Guid.NewGuid(); var foreign = Guid.NewGuid();
        rig.World.Objects[rootId] = new ScriptObjectState(rootId, Owner, "Casa", new Vec3(0, 0, 0), default, new Vec3(1, 1, 1));
        rig.World.Objects[childId] = new ScriptObjectState(childId, Owner, "Janela", new Vec3(1, 0, 0), default, new Vec3(1, 1, 1), rootId);
        rig.World.Objects[foreign] = new ScriptObjectState(foreign, Other, "Alheio", new Vec3(2, 0, 0), default, new Vec3(1, 1, 1), rootId);
        rig.World.Linkset.AddRange([rootId, childId, foreign]);
        rig.Add("function on_touch() chat.say(object.getLinkCount() .. ' ' .. object.getRoot() .. ' ' .. object.getLink(2).name .. ' ' .. #object.getChildren()) object.setLinkPosition(2, 5, 0, 0) object.sendLinkMessage('abrir') end", objectId: rootId, name: "Casa");
        rig.Add("function on_link_message(e) chat.say('janela ouviu ' .. e.message) end", objectId: childId, name: "Janela");
        rig.Fire(rootId, "touch");
        Assert.Contains($"3 {rootId} Janela 2", rig.World.Spoken.Select(s => s.Text)); Assert.Contains("janela ouviu abrir", rig.World.Spoken.Select(s => s.Text));
        var moved = Assert.Single(rig.World.Changes); Assert.Equal(childId, moved.Change.TargetId); Assert.Equal(new Vec3(5, 0, 0), moved.Change.Position);

        rig.Add("function on_touch() object.setLinkPosition(3, 1, 1, 1) end", objectId: rootId, name: "Casa2"); rig.Fire(rootId, "touch");
        Assert.Contains("other people", rig.Reports.Last().Message);                          // a part of somebody else is not ours to move
    }

    [Fact]
    public void BroadcastMessage_ReachesOnlyOtherObjectsOfTheSameOwner()
    {
        var rig = new Rig(); var (a, _) = rig.Add("function on_touch() n = object.broadcastMessage('alarme') chat.say('enviado a ' .. n) end", name: "A");
        rig.Add("function on_message(e) chat.say('B ouviu ' .. e.message) end", name: "B"); rig.Add("function on_message(e) chat.say('C ouviu') end", owner: Other, name: "C");
        rig.Fire(a, "touch"); rig.Host.Process(500); var said = rig.World.Spoken.Select(s => s.Text).ToList();
        Assert.Contains("B ouviu alarme", said); Assert.DoesNotContain("C ouviu", said); Assert.Contains("enviado a 1", said);
    }

    // ---- life cycle ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LifeCycle_ReloadsRunStartAndDestroy()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_start() chat.say('start') end function on_destroy() chat.say('destroy') end function on_reset() chat.say('reset') end");
        Assert.Equal(ScriptLifecycle.Waiting, rig.Host.StateOf(script)); Assert.Equal("aguardando evento", rig.Host.MetricsOf(script)!.StateText); Assert.Equal(["start"], rig.World.Spoken.Select(s => s.Text));
        Assert.True(rig.Host.Restart(script)); rig.Host.Process(500); Assert.Equal(["start", "reset", "start"], rig.World.Spoken.Select(s => s.Text));
        rig.Definitions.Clear(); rig.Host.Sync([]); Assert.Contains("destroy", rig.World.Spoken.Select(s => s.Text)); Assert.Null(rig.Host.StateOf(script));      // gone, and heard on_destroy first
        _ = obj;
    }

    [Fact]
    public void Stop_EndsEventsAndTimers_AndRestartBringsItBack_KeepingStorage()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_start() timer.every(1, function() chat.say('tic') end) storage.set('v', 7) end function on_touch() chat.say('toque ' .. tostring(storage.get('v'))) end");
        rig.Advance(1.1); var ticks = rig.World.Spoken.Count(s => s.Text == "tic"); Assert.Equal(1, ticks);
        Assert.True(rig.Host.Stop(script)); Assert.Equal(ScriptLifecycle.Stopped, rig.Host.StateOf(script));
        rig.Advance(5); rig.Fire(obj, "touch"); Assert.Equal(ticks, rig.World.Spoken.Count(s => s.Text == "tic")); Assert.DoesNotContain("toque 7", rig.World.Spoken.Select(s => s.Text)); Assert.True(rig.Host.MetricsOf(script)!.EventsDropped > 0);
        Assert.False(rig.Host.Stop(script));                                                                                                      // already stopped
        Assert.True(rig.Host.Restart(script)); rig.Host.Process(500); rig.Fire(obj, "touch"); Assert.Contains("toque 7", rig.World.Spoken.Select(s => s.Text)); Assert.Equal(ScriptLifecycle.Waiting, rig.Host.StateOf(script));
    }

    [Fact]
    public void Pause_FreezesTimersAndDropsEvents_ResumeContinues()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_start() timer.after(10, function() chat.say('fim') end) end function on_touch() chat.say('toque') end");
        rig.Advance(4); Assert.True(rig.Host.Pause(script)); Assert.Equal(ScriptLifecycle.Paused, rig.Host.StateOf(script));
        rig.Advance(100); rig.Fire(obj, "touch"); Assert.Empty(rig.World.Spoken);
        Assert.True(rig.Host.Resume(script)); rig.Advance(5); Assert.Empty(rig.World.Spoken); rig.Advance(1.5); Assert.Equal(["fim"], rig.World.Spoken.Select(s => s.Text));   // 6 s were left
        Assert.False(rig.Host.Resume(script));
    }

    [Fact]
    public void Disable_RunsOnDisableThenSilencesTheScript_EnableRunsOnEnable()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_disable() chat.say('desligando') end function on_enable() chat.say('ligado') end function on_touch() chat.say('toque') end");
        Assert.True(rig.Host.SetEnabled(script, false)); rig.Fire(obj, "touch"); Assert.Equal(["desligando"], rig.World.Spoken.Select(s => s.Text));
        Assert.True(rig.Host.SetEnabled(script, true)); rig.Host.Process(500); rig.Fire(obj, "touch"); Assert.Equal(["desligando", "ligado", "toque"], rig.World.Spoken.Select(s => s.Text));
    }

    [Fact]
    public void OwnerChange_RevokesGrantsAndTellsTheScripts()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_owner_changed(e) chat.say('novo dono ' .. e.owner_id) end");
        rig.Host.Permissions.Grant(script, Guid.Empty, ScriptPermission.ControlPhysics); Assert.True(rig.Host.Permissions.Has(script, Guid.Empty, ScriptPermission.ControlPhysics));
        rig.Host.OwnerChanged(obj, Other); rig.Host.Process(500);
        Assert.False(rig.Host.Permissions.Has(script, Guid.Empty, ScriptPermission.ControlPhysics)); Assert.Contains($"novo dono {Other}", rig.World.Spoken.Select(s => s.Text));
    }

    [Fact]
    public void Region_StartAndShutdownEventsReachTheScripts()
    {
        var rig = new Rig(); rig.Add("function on_region_start() chat.say('ilha de pé') end function on_region_shutdown() chat.say('ilha caindo') storage.set('ultima', 1) end");
        rig.Host.RegionStarted(); rig.Host.Process(500); Assert.Contains("ilha de pé", rig.World.Spoken.Select(s => s.Text));
        rig.Host.RegionShutdown(); Assert.Contains("ilha caindo", rig.World.Spoken.Select(s => s.Text)); Assert.NotEmpty(rig.Host.TakeDirtyState());        // the state is ready to be saved
    }

    // ---- permissions and dialogs ----------------------------------------------------------------------------------------------

    [Fact]
    public void Permissions_AreAskedOfThePerson_GrantedByThemAndRevocableAtOnce()
    {
        var rig = new Rig(); var ana = Guid.NewGuid();
        var (obj, script) = rig.Add("function on_touch(e) asked = permissions.request(e.avatar_id, 'CONTROL_ANIMATION') end function on_permission_granted(e) chat.say('sim ' .. e.permission .. ' ' .. tostring(permissions.has(e.avatar_id, e.permission))) end function on_permission_denied(e) chat.say('não ' .. e.permission) end");
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana });
        var request = Assert.Single(rig.Requests); Assert.Equal("permission", request.Kind); Assert.Equal(ana, request.AvatarId); Assert.Equal(ScriptPermission.ControlAnimation, request.Permission); Assert.Contains("animações", request.Message); Assert.Equal(["Permitir", "Negar"], request.Buttons);
        Assert.False(rig.Host.Permissions.Has(script, ana, ScriptPermission.ControlAnimation));                       // the script cannot give itself anything
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); Assert.Single(rig.Requests);                           // not asked again within 30 seconds
        rig.Host.ResolvePermission(script, ana, ScriptPermission.ControlAnimation, true); rig.Host.Process(500); Assert.Contains("sim CONTROL_ANIMATION true", rig.World.Spoken.Select(s => s.Text));
        rig.World.Now += 60; rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); Assert.Single(rig.Requests); Assert.Equal(2, rig.World.Spoken.Count(s => s.Text.StartsWith("sim", StringComparison.Ordinal)));   // already allowed: the answer is immediate, nobody is asked
        rig.Host.ResolvePermission(script, ana, ScriptPermission.ControlAnimation, false); rig.Host.Process(500); Assert.Contains("não CONTROL_ANIMATION", rig.World.Spoken.Select(s => s.Text)); Assert.False(rig.Host.Permissions.Has(script, ana, ScriptPermission.ControlAnimation));
    }

    [Fact]
    public void Permissions_RefuseUnknownKindsAndThoseNotAskedOfAvatars()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch(e) permissions.request(e.avatar_id, 'ROOT') end"); rig.Fire(obj, "touch", new() { ["avatar_id"] = Guid.NewGuid() });
        Assert.Contains("not asked of avatars", Assert.Single(rig.Reports).Message); Assert.Empty(rig.Requests);
        var permissions = new ScriptPermissions(); permissions.Grant(Guid.NewGuid(), Guid.NewGuid(), "ROOT"); Assert.Empty(permissions.All());
    }

    [Fact]
    public void Dialogs_AreShownToOneAvatar_AndOnlyThatAvatarCanAnswer()
    {
        var rig = new Rig(); var ana = Guid.NewGuid(); var bruno = Guid.NewGuid();
        var (obj, script) = rig.Add("function on_touch(e) id = dialog.show(e.avatar_id, {title = 'Porta', message = 'Escolha', buttons = {'Abrir', 'Fechar'}}) end function on_dialog_response(e) chat.say(e.avatar_id == '" + ana + "' and ('resposta ' .. e.response) or 'impostor') end");
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana });
        var request = Assert.Single(rig.Requests); Assert.Equal("dialog", request.Kind); Assert.Equal("Porta", request.Title); Assert.Equal(["Abrir", "Fechar"], request.Buttons); Assert.Equal(ana, request.AvatarId);
        Assert.False(rig.Host.RespondDialog(request.RequestId, bruno, "Abrir"));                                            // somebody else's dialog
        Assert.False(rig.Host.RespondDialog(Guid.NewGuid(), ana, "Abrir"));                                                 // made-up id
        Assert.True(rig.Host.RespondDialog(request.RequestId, ana, "Abrir")); rig.Host.Process(500); Assert.Equal(["resposta Abrir"], rig.World.Spoken.Select(s => s.Text));
        Assert.False(rig.Host.RespondDialog(request.RequestId, ana, "Abrir"));                                              // an answer counts once
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); var second = rig.Requests.Last(); rig.World.Now += 400; Assert.False(rig.Host.RespondDialog(second.RequestId, ana, "Fechar"));   // timed out
        _ = script;
    }

    [Fact]
    public void Dialogs_AreLimitedPerScript()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch(e) dialog.show(e.avatar_id, {message = 'x'}) end");
        for (var i = 0; i < 3; i++) rig.Fire(obj, "touch", new() { ["avatar_id"] = Guid.NewGuid() }); Assert.Equal(3, rig.Requests.Count);
        rig.Fire(obj, "touch", new() { ["avatar_id"] = Guid.NewGuid() }); Assert.Contains("at most 3 open dialogs", rig.Reports.Last().Message);
    }

    // ---- limits, suspension and fairness ---------------------------------------------------------------------------------------------

    [Fact]
    public void RunawayScripts_AreSuspendedWithTheReason_AndOthersKeepRunning()
    {
        var rig = new Rig(new ScriptHostOptions(MillisecondsPerEvent: 50, MaxEventsPerSecond: 100));
        var (loop, loopScript) = rig.Add("function on_touch() while true do end end", name: "Laço");
        var (recursion, recursionScript) = rig.Add("function f(n) return 1 + f(n + 1) end function on_touch() f(1) end", name: "Recursão");
        var (memory, memoryScript) = rig.Add("function on_touch() local t = {} for i = 1, 1e9 do t[i] = {i} end end", name: "Memória");
        var (fine, fineScript) = rig.Add("function on_touch() chat.say('vivo') end", name: "Bom");
        for (var i = 0; i < 4; i++) foreach (var obj in new[] { loop, recursion, memory, fine }) rig.Fire(obj, "touch");
        foreach (var script in new[] { loopScript, recursionScript, memoryScript }) { var m = rig.Host.MetricsOf(script)!; Assert.Equal(ScriptLifecycle.Suspended, m.State); Assert.NotNull(m.SuspendedReason); Assert.True(m.Violations >= 3); Assert.Equal(1, m.Suspensions); Assert.Contains(rig.Host.LogsOf(script), l => l.Text.StartsWith("suspended", StringComparison.Ordinal)); }
        Assert.Equal(4, rig.World.Spoken.Count(s => s.Text == "vivo")); Assert.Equal(ScriptLifecycle.Waiting, rig.Host.StateOf(fineScript));
        Assert.True(rig.Host.Restart(loopScript)); Assert.Equal(ScriptLifecycle.Waiting, rig.Host.StateOf(loopScript));        // an owner can bring a suspended script back
    }

    [Fact]
    public void EventSpam_IsDroppedAndCounted()
    {
        var rig = new Rig(new ScriptHostOptions(MillisecondsPerEvent: 2000, MaxEventsPerSecond: 10)); var (obj, script) = rig.Add("function on_touch() hits = (hits or 0) + 1 end");
        for (var i = 0; i < 100; i++) rig.Host.Enqueue(new ScriptEvent(obj, "touch")); rig.Host.Process(1000);
        var m = rig.Host.MetricsOf(script)!; Assert.Equal(10, m.Events); Assert.True(m.EventsDropped >= 90 - 0);
    }

    [Fact]
    public void Metrics_CountEventsCpuInstructionsAndApiCalls_PerScriptAndPerOwner()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_touch() for i = 1, 100 do local x = i * 2 end chat.say('a') object.getName() end");
        for (var i = 0; i < 3; i++) rig.Fire(obj, "touch");
        var m = rig.Host.MetricsOf(script)!; Assert.Equal(3, m.Events); Assert.True(m.Instructions > 0); Assert.True(m.CpuMilliseconds > 0 && m.WorstEventMilliseconds > 0); Assert.True(m.ApiCalls >= 6); Assert.True(m.MessagesSent == 3);
        rig.Add("function on_touch() end", owner: Other);
        var usage = rig.Host.UsageByOwner(); Assert.Equal(2, usage.Count); Assert.Equal(Owner, usage[0].Owner); Assert.Equal(1, usage[0].Scripts); Assert.Equal(3, usage[0].Events);
        Assert.Equal(2, rig.Host.AllMetrics().Count);
    }

    [Fact]
    public void AnObjectRunsAtMostEightScripts()
    {
        var rig = new Rig(); var obj = Guid.NewGuid(); for (var i = 0; i < 10; i++) rig.Add("function on_touch() chat.say('s') end", objectId: obj, name: "Objeto" + i, sync: false);
        rig.Sync(); Assert.Equal(ScriptHost.MaxScriptsPerObject, rig.Host.AllMetrics().Count); Assert.Equal(2, rig.Reports.Count(r => r.Message.Contains("at most 8", StringComparison.Ordinal)));
        rig.Fire(obj, "touch"); Assert.Equal(8, rig.World.Spoken.Count);                                                       // several scripts on one object all run
    }

    [Fact]
    public void LifeCycle_OnlyAllowedMoves()
    {
        Assert.True(ScriptLifecycleRules.Allowed(ScriptLifecycle.Created, ScriptLifecycle.Loaded)); Assert.True(ScriptLifecycleRules.Allowed(ScriptLifecycle.Waiting, ScriptLifecycle.Running));
        Assert.False(ScriptLifecycleRules.Allowed(ScriptLifecycle.Destroyed, ScriptLifecycle.Running)); Assert.False(ScriptLifecycleRules.Allowed(ScriptLifecycle.Stopped, ScriptLifecycle.Running)); Assert.False(ScriptLifecycleRules.Allowed(ScriptLifecycle.Created, ScriptLifecycle.Running));
        Assert.Equal(10, Enum.GetValues<ScriptLifecycle>().Length);
        Assert.All(Enum.GetValues<ScriptLifecycle>(), s => Assert.False(string.IsNullOrEmpty(ScriptLifecycleRules.Describe(s))));
    }

    // ---- the sandbox still holds ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("os.execute('ls')")]
    [InlineData("io.open('/etc/passwd')")]
    [InlineData("require('socket')")]
    [InlineData("package.loadlib('x', 'y')")]
    [InlineData("load('return 1')()")]
    [InlineData("dofile('x')")]
    [InlineData("debug.getinfo(1)")]
    [InlineData("debug.sethook()")]
    [InlineData("collectgarbage()")]
    [InlineData("setmetatable({}, {})")]
    [InlineData("string.dump(print)")]
    [InlineData("local c = coroutine.create(function() end)")]
    public void DangerousFunctions_DoNotExistForScripts(string code)
    {
        var rig = new Rig(); var (obj, _) = rig.Add($"function on_touch() {code} end"); rig.Fire(obj, "touch");
        var report = Assert.Single(rig.Reports); Assert.Equal(LuaStatus.Error, report.Status);
    }

    [Fact]
    public void Scripts_CannotReachTheHostThroughTheirTables()
    {
        var rig = new Rig(); var (obj, _) = rig.Add("function on_touch() local t = getmetatable(object) local c = chat.say chat.say(type(t) .. type(c) .. tostring(_G.os) .. tostring(_G.io) .. tostring(_G.package) .. tostring(_G.system)) end");
        rig.Fire(obj, "touch"); Assert.DoesNotContain("userdata", rig.World.Spoken.SingleOrDefault().Text ?? "");
    }

    [Fact]
    public void OwnerGrants_AreReplacedAsAWhole_AndLeaveAvatarGrantsAlone()
    {
        var rig = new Rig(); var (_, script) = rig.Add("function on_start() end"); var ana = Guid.NewGuid();
        rig.Host.Permissions.Grant(script, ana, ScriptPermission.ShowHud); rig.Host.Permissions.Grant(script, Guid.Empty, ScriptPermission.HttpRequests);
        rig.Host.Permissions.ReplaceOwnerGrants([(script, ScriptPermission.ControlPhysics), (script, ScriptPermission.ShowHud)]);   // SHOW_HUD is asked of avatars, so it cannot be an owner grant
        Assert.True(rig.Host.Permissions.Has(script, Guid.Empty, ScriptPermission.ControlPhysics)); Assert.False(rig.Host.Permissions.Has(script, Guid.Empty, ScriptPermission.HttpRequests)); Assert.False(rig.Host.Permissions.Has(script, Guid.Empty, ScriptPermission.ShowHud));
        Assert.True(rig.Host.Permissions.Has(script, ana, ScriptPermission.ShowHud));
    }

    // ---- http ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Http_NeedsThePermissionOfTheOwner_AndAnswersThroughAnEvent()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_touch() id = http.request({url = 'https://api.exemplo.com/x?k=1', method = 'post', headers = {['X-Key'] = 'v'}, body = '{}'}) end function on_http_response(e) chat.say(e.request_id == id and ('resposta ' .. e.status .. ' ' .. e.body .. ' ' .. tostring(e.ok)) or 'outra') end");
        rig.Fire(obj, "touch"); Assert.Contains("HTTP_REQUESTS", Assert.Single(rig.Reports).Message); Assert.Empty(rig.World.Http);                                      // nothing leaves without the permission
        rig.Host.Permissions.Grant(script, Guid.NewGuid(), ScriptPermission.HttpRequests); rig.Fire(obj, "touch"); Assert.Empty(rig.World.Http);                       // a grant of an avatar is not the owner's
        rig.Host.Permissions.Grant(script, Guid.Empty, ScriptPermission.HttpRequests); rig.Fire(obj, "touch");
        var (request, spec) = Assert.Single(rig.World.Http); Assert.Equal(("https://api.exemplo.com/x?k=1", "POST", "{}", "v"), (spec.Url, spec.Method, spec.Body, spec.Headers["X-Key"]));
        rig.Host.CompleteHttp(script, request, 200, "{\"ok\":1}", null); rig.Host.Process(500); Assert.Contains("resposta 200 {\"ok\":1} true", rig.World.Spoken.Select(s => s.Text));
        rig.Host.CompleteHttp(script, request, 200, "again", null); rig.Host.Process(500); Assert.Single(rig.World.Spoken);                                          // an answer counts once
        rig.Host.CompleteHttp(script, Guid.NewGuid(), 200, "made up", null); rig.Host.CompleteHttp(Guid.NewGuid(), request, 200, "wrong script", null); rig.Host.Process(500); Assert.Single(rig.World.Spoken);
    }

    [Fact]
    public void Http_FailuresComeBackAsTheSameEvent_AndRequestsAreLimited()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_touch() http.request({url = 'https://api.exemplo.com/'}) end function on_http_response(e) chat.say('erro ' .. tostring(e.error) .. ' ' .. tostring(e.ok)) end");
        rig.Host.Permissions.Grant(script, Guid.Empty, ScriptPermission.HttpRequests); rig.World.HttpWorks = false;
        rig.Fire(obj, "touch"); rig.Host.Process(500); Assert.Contains("erro http_unavailable false", rig.World.Spoken.Select(s => s.Text));                                 // a world that cannot do it says so
        rig.World.HttpWorks = true; rig.Fire(obj, "touch"); rig.Fire(obj, "touch"); rig.Fire(obj, "touch"); Assert.Equal(4, rig.World.Http.Count);
        rig.Fire(obj, "touch"); Assert.Contains("at most 3 requests at the same time", rig.Reports.Last().Message); Assert.Equal(4, rig.World.Http.Count);
        foreach (var (request, _) in rig.World.Http.ToList()) rig.Host.CompleteHttp(script, request, 500, "boom", null); rig.Host.Process(500);
        for (var i = 0; i < 10; i++) { rig.World.Now += 1; rig.Fire(obj, "touch"); foreach (var (request, _) in rig.World.Http.ToList()) rig.Host.CompleteHttp(script, request, 200, "", null); rig.Host.Process(500); }
        Assert.Contains("10 requests a minute", rig.Reports.Last().Message);
        var (bad, badScript) = rig.Add("function on_touch() http.request(42) http.request({}) end"); rig.Host.Permissions.Grant(badScript, Guid.Empty, ScriptPermission.HttpRequests); rig.Fire(bad, "touch"); Assert.Contains("expects a table", rig.Reports.Last().Message);
    }

    // ---- physics ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Physics_NeedsThePermissionOfTheOwner_ThenChangesSettingsAndPushesTheBody()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_touch() physics.setDynamic(true) physics.setMass(12) physics.setPhantom(false) physics.setGravityScale(0.5) ok1 = physics.applyImpulse(0, 5, 0) ok2 = physics.applyForce(0, 100, 0) physics.setVelocity(0, 3, 0) physics.setAngularVelocity(0, 1, 0) local v = physics.getVelocity() chat.say(v.x .. v.y .. v.z .. tostring(ok1)) end");
        rig.Fire(obj, "touch"); Assert.Contains("CONTROL_PHYSICS", Assert.Single(rig.Reports).Message); Assert.Empty(rig.World.Changes); Assert.Empty(rig.World.Pushes);
        rig.Host.Permissions.Grant(script, Guid.NewGuid(), ScriptPermission.ControlPhysics); rig.Fire(obj, "touch"); Assert.Empty(rig.World.Pushes);   // an avatar's grant is not the owner's
        rig.Host.Permissions.Grant(script, Guid.Empty, ScriptPermission.ControlPhysics); rig.Fire(obj, "touch");
        var change = Assert.Single(rig.World.Changes).Change.Physics!; Assert.Equal((true, 12f, true), (change.Physical, change.Mass, change.Collision));
        Assert.Equal(["gravity", "impulse", "impulse", "velocity", "angular"], rig.World.Pushes.Select(p => p.Action).ToList()); Assert.Equal(new Vec3(0, 10, 0), rig.World.Pushes[2].Value);   // a force of 100 for 0.1 s is an impulse of 10
        Assert.Contains("123true", rig.World.Spoken.Select(s => s.Text));
    }

    [Fact]
    public void Physics_RefusesWhatIsOutOfRange_AndPushesAreLimited_AndNonSimulatedBodiesSayNo()
    {
        var rig = new Rig(); var (obj, script) = rig.Add("function on_touch(e) if e.mass then physics.setMass(e.mass) end if e.fast then physics.setVelocity(1000, 0, 0) end for i = 1, (e.times or 1) do physics.applyImpulse(1, 0, 0) end r = physics.applyImpulse(1, 0, 0) chat.say(tostring(r)) end");
        rig.Host.Permissions.Grant(script, Guid.Empty, ScriptPermission.ControlPhysics);
        rig.Fire(obj, "touch", new() { ["mass"] = 0.0001 }); Assert.Contains("mass goes from", rig.Reports.Last().Message);
        rig.Fire(obj, "touch", new() { ["fast"] = true }); Assert.Equal(50f, rig.World.Pushes.Last(p => p.Action == "velocity").Value.X, 3);                      // 1000 m/s is clamped to 50
        rig.World.Simulated = false; rig.World.Now += 5; rig.Fire(obj, "touch"); Assert.Equal("false", rig.World.Spoken.Last().Text);                               // not a simulated body: false
        rig.World.Simulated = true; rig.World.Now += 5; rig.Fire(obj, "touch", new() { ["times"] = 20 }); Assert.Contains("at most 10 pushes", rig.Reports.Last().Message);
        rig.World.Velocity = null; rig.World.Now += 5; var (obj2, s2) = rig.Add("function on_touch() chat.say(tostring(physics.getVelocity())) end"); rig.Host.Permissions.Grant(s2, Guid.Empty, ScriptPermission.ControlPhysics); rig.Fire(obj2, "touch"); Assert.Equal("nil", rig.World.Spoken.Last().Text);
    }

    // ---- economy ---------------------------------------------------------------------------------------------------------------

    private const string Shop = "function on_touch(e) id = economy.requestPayment(e.avatar_id, 100, 'Entrada') end function on_payment(e) chat.say('pago ' .. e.amount .. ' por ' .. e.avatar.name .. ' ' .. tostring(e.transaction_id ~= nil)) end function on_payment_failed(e) chat.say('falhou ' .. e.reason) end";

    [Fact]
    public void Payment_IsAskedOfTheAvatar_PaidOnlyWhenTheyAgree_AndTheScriptHearsOnlyWhatTheEconomyConfirmed()
    {
        var rig = new Rig(); var ana = Guid.NewGuid(); var bruno = Guid.NewGuid(); var (obj, script) = rig.Add(Shop);
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana });
        var request = Assert.Single(rig.Requests); Assert.Equal(("payment", ana, 2), (request.Kind, request.AvatarId, request.Buttons.Count)); Assert.Equal(["Pagar", "Recusar"], request.Buttons); Assert.Contains("L$ 100", request.Message); Assert.Contains("dono do objeto", request.Message); Assert.Contains("Entrada", request.Message);
        Assert.Empty(rig.World.Payments);                                                                                    // nothing is charged by asking
        Assert.False(rig.Host.RespondPayment(request.RequestId, bruno, "Pagar")); Assert.False(rig.Host.RespondPayment(Guid.NewGuid(), ana, "Pagar")); Assert.Empty(rig.World.Payments);   // somebody else's request, a made-up one
        Assert.True(rig.Host.RespondPayment(request.RequestId, ana, "Pagar")); var (_, payer, amount) = Assert.Single(rig.World.Payments); Assert.Equal((ana, 100L), (payer, amount));
        Assert.False(rig.Host.RespondPayment(request.RequestId, ana, "Pagar")); Assert.Single(rig.World.Payments);       // pressing twice charges once
        rig.Host.Process(500); Assert.Empty(rig.World.Spoken);                                                               // the script has heard nothing yet: the money has not been confirmed
        rig.Host.CompletePayment(script, Guid.NewGuid(), Guid.NewGuid(), null); rig.Host.CompletePayment(Guid.NewGuid(), request.RequestId, Guid.NewGuid(), null); rig.Host.Process(500); Assert.Empty(rig.World.Spoken);   // made-up confirmations
        rig.Host.CompletePayment(script, request.RequestId, Guid.NewGuid(), null); rig.Host.Process(500); Assert.Equal(["pago 100 por Ana true"], rig.World.Spoken.Select(s => s.Text));
        rig.Host.CompletePayment(script, request.RequestId, Guid.NewGuid(), null); rig.Host.Process(500); Assert.Single(rig.World.Spoken);                       // a confirmation counts once
    }

    [Fact]
    public void Payment_DeclinedFailedOrUnavailable_TellsTheScriptWhy()
    {
        var rig = new Rig(); var ana = Guid.NewGuid(); var (obj, script) = rig.Add(Shop);
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); Assert.True(rig.Host.RespondPayment(rig.Requests[0].RequestId, ana, "Recusar")); rig.Host.Process(500); Assert.Equal(["falhou declined"], rig.World.Spoken.Select(s => s.Text)); Assert.Empty(rig.World.Payments);
        rig.World.Now += 20; rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); rig.Host.RespondPayment(rig.Requests[1].RequestId, ana, "Pagar"); rig.Host.CompletePayment(script, rig.Requests[1].RequestId, null, "insufficient_balance"); rig.Host.Process(500); Assert.Equal("falhou insufficient_balance", rig.World.Spoken.Last().Text);
        rig.World.PaymentsWork = false; rig.World.Now += 20; rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); rig.Host.RespondPayment(rig.Requests[2].RequestId, ana, "Pagar"); rig.Host.Process(500); Assert.Equal("falhou unavailable", rig.World.Spoken.Last().Text);
        rig.World.Now += 20; rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); rig.World.Now += 100; Assert.False(rig.Host.RespondPayment(rig.Requests[3].RequestId, ana, "Pagar"));      // too late
    }

    [Fact]
    public void Payment_IsLimited()
    {
        var rig = new Rig(); var ana = Guid.NewGuid(); var (obj, _) = rig.Add(Shop);
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); Assert.Contains("a moment ago", rig.Reports.Last().Message); Assert.Single(rig.Requests);              // the same person is not nagged
        foreach (var source in new[] { "economy.requestPayment(e.avatar_id, 0)", "economy.requestPayment(e.avatar_id, -5)", "economy.requestPayment(e.avatar_id, 100001)" })
        { var (bad, _) = rig.Add($"function on_touch(e) {source} end"); rig.Fire(bad, "touch", new() { ["avatar_id"] = Guid.NewGuid() }); Assert.Contains("a payment is from", rig.Reports.Last().Message); }
        var (many, _) = rig.Add("function on_touch(e) economy.requestPayment(e.avatar_id, 5) end"); for (var i = 0; i < 6; i++) rig.Fire(many, "touch", new() { ["avatar_id"] = Guid.NewGuid() }); Assert.Contains("at most 5 payments waiting", rig.Reports.Last().Message);
    }

    // ---- inventory of the object -----------------------------------------------------------------------------------------------

    [Fact]
    public void Inventory_ReadsWhatTheObjectCarries_AndHearsWhenItChanges()
    {
        var rig = new Rig(); var sound = Guid.NewGuid(); rig.World.Contents.AddRange([new ScriptItem("Campainha", "sounds", sound), new ScriptItem("Nota", "notes", null)]);
        var (obj, _) = rig.Add("function on_touch() local l = inventory.list() chat.say(inventory.count() .. ' ' .. l[1].name .. ' ' .. l[1].type .. ' ' .. tostring(l[2].id) .. ' ' .. tostring(inventory.hasItem('campainha')) .. tostring(inventory.hasItem('Chave')) .. ' ' .. inventory.getItemType('Nota') .. ' ' .. tostring(inventory.getItem('Nada')) .. ' ' .. inventory.getItem('Campainha').id) end function on_inventory_changed(e) chat.say('mudou +' .. e.added .. ' -' .. e.removed) end function on_item_added(e) chat.say('entrou ' .. e.item.name .. ' ' .. e.item.type) end function on_item_removed(e) chat.say('saiu ' .. e.item.name) end");
        rig.Fire(obj, "touch"); Assert.Equal($"2 Campainha sounds nil truefalse notes nil {sound}", rig.World.Spoken.Single().Text);
        rig.Host.ContentsChanged(obj, [new ScriptItem("Chave", "notes", null)], [new ScriptItem("Nota", "notes", null)]); rig.Host.Process(500);
        Assert.Equal(["mudou +1 -1", "entrou Chave notes", "saiu Nota"], rig.World.Spoken.Skip(1).Select(s => s.Text)); rig.Host.ContentsChanged(obj, [], []); rig.Host.Process(500); Assert.Equal(4, rig.World.Spoken.Count);   // nothing changed, nothing heard
    }

    // ---- avatar animations -----------------------------------------------------------------------------------------------------

    [Fact]
    public void AvatarAnimation_NeedsThePermissionOfThatAvatar_AndTheScriptHearsWhenItEnds()
    {
        var rig = new Rig(); var ana = Guid.NewGuid(); var bruno = Guid.NewGuid();
        var (obj, script) = rig.Add("function on_touch(e) ok = avatar.playAnimation(e.avatar_id, e.what or 'dance', 99) end function on_message(e) avatar.stopAnimation('" + ana + "') end function on_animation_finished(e) chat.say('fim ' .. e.name .. ' ' .. e.reason .. ' ' .. e.avatar.name) end");
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); Assert.Contains("CONTROL_ANIMATION", Assert.Single(rig.Reports).Message); Assert.Empty(rig.World.Played);                      // nobody dances by surprise
        rig.Host.ResolvePermission(script, bruno, ScriptPermission.ControlAnimation, true); rig.Fire(obj, "touch", new() { ["avatar_id"] = ana }); Assert.Empty(rig.World.Played);   // Bruno's permission is not Ana's
        rig.Host.ResolvePermission(script, ana, ScriptPermission.ControlAnimation, true); rig.Fire(obj, "touch", new() { ["avatar_id"] = ana });
        Assert.Equal((ana, "dance", 15d), Assert.Single(rig.World.Played));                                                                                                          // 99 seconds is cut to 15
        rig.Host.AnimationEnded(ana, "dance", "finished"); rig.Host.Process(500); Assert.Equal(["fim dance finished Ana"], rig.World.Spoken.Select(s => s.Text));
        rig.Host.AnimationEnded(ana, "dance", "finished"); rig.Host.AnimationEnded(bruno, "dance", "finished"); rig.Host.Process(500); Assert.Single(rig.World.Spoken);                    // a finish counts once, and only for what this script started
        rig.Fire(obj, "message"); Assert.Equal([ana], rig.World.Stopped);
        rig.Host.Permissions.Revoke(script, ana, ScriptPermission.ControlAnimation); rig.Fire(obj, "message"); Assert.Single(rig.World.Stopped);                                       // revoked: at once
    }

    [Fact]
    public void AvatarAnimation_OnlyKnownGestures_AndRateLimited()
    {
        var rig = new Rig(); var ana = Guid.NewGuid(); var (obj, script) = rig.Add("function on_touch(e) avatar.playAnimation(e.avatar_id, e.what, 2) end"); rig.Host.ResolvePermission(script, ana, ScriptPermission.ControlAnimation, true);
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana, ["what"] = "fly_away" }); Assert.Contains("unknown animation", rig.Reports.Last().Message); Assert.Empty(rig.World.Played);
        rig.Fire(obj, "touch", new() { ["avatar_id"] = ana, ["what"] = "WAVE" }); Assert.Equal("wave", rig.World.Played.Single().Name);
        for (var i = 0; i < 10; i++) rig.Fire(obj, "touch", new() { ["avatar_id"] = ana, ["what"] = "wave" }); rig.World.Now += 6; rig.Fire(obj, "touch", new() { ["avatar_id"] = ana, ["what"] = "wave" }); Assert.Contains("at most 10", rig.Reports.Last().Message); Assert.Equal(10, rig.World.Played.Count);
    }
}
