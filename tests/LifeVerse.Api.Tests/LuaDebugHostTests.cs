using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The debugger as the host sees it: a script in debug mode stops at its breakpoints without freezing the island, what it asked for is applied only when it ends, it hears nothing while stopped, and debugging ends on request, on a timeout or when it runs out of room.</summary>
public sealed class LuaDebugHostTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    // line 1 function on_touch / 2 local a = 5 / 3 object.setPosition(a, 0, 0) / 4 local b = a + 1 / 5 chat.say('fim ' .. b) / 6 end
    private const string Source = "function on_touch(e)\n  local a = 5\n  object.setPosition(a, 0, 0)\n  local b = a + 1\n  chat.say('fim ' .. b)\nend\nfunction on_start() chat.say('ligou') end\n";

    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public List<string> Said { get; } = []; public List<float> Moved { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { if (change.Position is { } p) Moved.Add(p.X); }
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
    }

    private static (ScriptHost Host, World World, Guid Object, Guid Script, List<(string Event, DebugSnapshot Snapshot)> Stops, List<ScriptReport> Reports) Rig(string source = Source)
    {
        var world = new World(); var obj = Guid.NewGuid(); world.Objects[obj] = new ScriptObjectState(obj, Owner, "Porta", new Vec3(0, 0, 0), default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var stops = new List<(string, DebugSnapshot)>(); var reports = new List<ScriptReport>(); host.DebugStopped += (_, e, s) => stops.Add((e, s)); host.Report += reports.Add;
        var script = Guid.NewGuid(); host.Sync([new ScriptDefinition(script, obj, Owner, "porta", source, 1)]); host.Process(500); return (host, world, obj, script, stops, reports);
    }

    private static void Touch(ScriptHost host, Guid obj) { host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(500); }

    [Fact]
    public void ItStopsAtTheBreakpoint_NothingIsAppliedYet_ThenFinishesWhenResumed()
    {
        var (host, world, obj, script, stops, reports) = Rig(); world.Said.Clear(); Assert.True(host.DebugStart(script, [4])); host.Process(500);          // loaded again in debug mode: on_start ran again
        Assert.Equal(["ligou"], world.Said); world.Said.Clear();
        Touch(host, obj); var (eventName, stop) = Assert.Single(stops); Assert.Equal(("touch", 4, "breakpoint"), (eventName, stop.Line, stop.Reason)); Assert.Contains(stop.Locals, v => v is { Name: "a", Value: "5" });
        Assert.Empty(world.Moved); Assert.Empty(world.Said);                                                                         // what the script asked for waits for the end of the handler
        Assert.True(host.DebugResume(script, DebugCommand.Continue)); Assert.Equal([5f], world.Moved); Assert.Equal(["fim 6"], world.Said); Assert.Empty(reports);
        Assert.False(host.DebugResume(script, DebugCommand.Continue));                                                               // nothing is stopped any more
    }

    [Fact]
    public void WhileStoppedTheScriptHearsNothing_AndTheIslandKeepsGoing()
    {
        var (host, world, obj, script, stops, _) = Rig(); host.DebugStart(script, [3]); host.Process(500); world.Said.Clear();
        Touch(host, obj); Assert.Single(stops); Touch(host, obj); Touch(host, obj); Assert.Single(stops);                           // two more touches while stopped are dropped, not queued behind it
        host.Tick(world.Now + 1); host.Process(500); Assert.Empty(world.Said);
        host.DebugResume(script, DebugCommand.Continue); Assert.Equal(["fim 6"], world.Said);                                       // only the first touch ever ran
        Touch(host, obj); Assert.Equal(2, stops.Count);                                                                              // the next one stops again
    }

    [Fact]
    public void SteppingStopsOnTheNextLines_AndTheBreakpointsCanChangeWhileDebugging()
    {
        var (host, world, obj, script, stops, _) = Rig(); host.DebugStart(script, [2]); host.Process(500); Touch(host, obj);
        host.DebugResume(script, DebugCommand.StepOver); Assert.Equal([2, 3], stops.Select(s => s.Snapshot.Line)); Assert.Equal("step", stops[1].Snapshot.Reason);
        Assert.True(host.DebugSetBreakpoints(script, [5])); Assert.Equal([5], host.DebugBreakpointsOf(script));
        host.DebugResume(script, DebugCommand.Continue); Assert.Equal(5, stops[2].Snapshot.Line); Assert.Contains(stops[2].Snapshot.Locals, v => v is { Name: "b", Value: "6" });
        host.DebugResume(script, DebugCommand.Continue); Assert.Equal(["fim 6"], world.Said.Where(s => s != "ligou"));
    }

    [Fact]
    public void LeavingDebugModeFinishesTheStoppedHandler_AndTheScriptRunsNormallyAgain()
    {
        var (host, world, obj, script, stops, _) = Rig(); var ended = new List<string>(); host.DebugEnded += (_, why) => ended.Add(why);
        host.DebugStart(script, [3]); host.Process(500); world.Said.Clear(); Touch(host, obj); Assert.Single(stops);
        Assert.True(host.DebugStop(script)); Assert.Equal(["stopped"], ended); Assert.False(host.IsDebugging(script)); Assert.Equal(["fim 6"], world.Said.Where(s => s != "ligou")); Assert.Equal([5f], world.Moved);   // the handler was let finish
        host.Process(500); world.Said.Clear(); Touch(host, obj); Assert.Single(stops); Assert.Equal(["fim 6"], world.Said);          // no more stops
        Assert.False(host.DebugStop(script));
    }

    [Fact]
    public void AScriptStoppedForTooLongIsLetGo()
    {
        var (host, world, obj, script, stops, _) = Rig(); var ended = new List<string>(); host.DebugEnded += (_, why) => ended.Add(why);
        host.DebugStart(script, [3]); host.Process(500); Touch(host, obj); Assert.Single(stops);
        host.Tick(world.Now + 60); Assert.Empty(ended); host.Tick(world.Now + ScriptHost.MaxDebugPauseSeconds + 1);
        Assert.Equal(["timeout"], ended); Assert.False(host.IsDebugging(script)); Assert.Equal([5f], world.Moved);
    }

    [Fact]
    public void OnlyAFewScriptsAreDebuggedAtOnce_AndOnlyRealOnes()
    {
        var world = new World(); var host = new ScriptHost(world, new ScriptHostOptions()); var definitions = new List<ScriptDefinition>();
        for (var i = 0; i < ScriptHost.MaxDebugSessions + 1; i++) { var obj = Guid.NewGuid(); world.Objects[obj] = new ScriptObjectState(obj, Owner, "o" + i, new Vec3(0, 0, 0), default, new Vec3(1, 1, 1)); definitions.Add(new ScriptDefinition(Guid.NewGuid(), obj, Owner, "s" + i, Source, 1)); }
        host.Sync(definitions); foreach (var d in definitions.Take(ScriptHost.MaxDebugSessions)) Assert.True(host.DebugStart(d.Id, [3]));
        Assert.False(host.DebugStart(definitions[^1].Id, [3])); Assert.True(host.DebugStart(definitions[0].Id, [2, 3]));          // no room for a fourth; one already in debugging may change
        Assert.False(host.DebugStart(Guid.NewGuid(), [1])); Assert.False(host.DebugSetBreakpoints(definitions[^1].Id, [1]));
    }

    [Fact]
    public void ABreakpointInsideAnotherHandlerAndAnErrorAfterResumingAreReported()
    {
        var (host, world, obj, script, stops, reports) = Rig("function on_touch(e)\n  local a = 1\n  local b = nil + a\nend\n"); host.DebugStart(script, [2]); host.Process(500);
        Touch(host, obj); Assert.Single(stops); host.DebugResume(script, DebugCommand.Continue);
        Assert.Equal(LuaStatus.Error, Assert.Single(reports).Status); Assert.Contains("(3,", Assert.Single(reports).Message);
    }
}
