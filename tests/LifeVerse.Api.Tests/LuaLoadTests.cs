using System.Diagnostics;
using LifeVerse.Scripting;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.Api.Tests;

/// <summary>
/// How many scripts one island can carry, measured (not guessed). Each size loads that many scripts (one per object, each with a timer, a state and a handler), runs a burst of touches and several seconds of timers,
/// and reports memory, load time, events per second and the time one server tick would spend. The numbers are written to <c>docs/LUA_LOAD_RESULTS.md</c> by the sizes that run; set <c>LIFEVERSE_LUA_LOAD=full</c> for 1000 and 5000.
/// </summary>
public sealed class LuaLoadTests(ITestOutputHelper output)
{
    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public long Changes, Said;
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) => Changes++;
        public void Say(ScriptDefinition script, string text) => Said++;
    }

    private const string Source = "local aberta = false\nfunction on_start() timer.every(1, function() object.rotate({x=0, y=2, z=0}) end) storage.set('n', 0) end\nfunction on_touch(event) aberta = not aberta storage.set('n', (storage.get('n') or 0) + 1) chat.say(aberta and 'aberta' or 'fechada') end";

    public sealed record Result(int Scripts, double LoadSeconds, double MegabytesPerScript, double TotalMegabytes, double BurstEventsPerSecond, double MillisecondsPerEvent, double TickMillisecondsWorst, double TickMillisecondsAverage, long Dropped, int Suspended);

    private Result Measure(int count)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var before = GC.GetTotalMemory(true);
        var world = new World(); var ids = new List<Guid>(); var definitions = new List<ScriptDefinition>(); var owner = Guid.NewGuid();
        for (var i = 0; i < count; i++) { var id = Guid.NewGuid(); ids.Add(id); world.Objects[id] = new ScriptObjectState(id, owner, "Obj" + i, new Vec3(i % 100, 1, i / 100), default, new Vec3(1, 1, 1)); definitions.Add(new ScriptDefinition(Guid.NewGuid(), id, Guid.NewGuid() /* one owner each: no shared budget */, "s" + i, Source, 1)); }
        var host = new ScriptHost(world, new ScriptHostOptions(MaxEventsPerSecond: 1_000, MaxQueuedEvents: 100_000));
        var load = Stopwatch.StartNew(); host.Sync(definitions); while (host.QueuedEvents > 0) host.Process(1_000); load.Stop();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var after = GC.GetTotalMemory(true);

        // a burst: every object touched twice (as if everybody clicked at once), processed in server-sized slices of 6 ms like the island does
        foreach (var id in ids) { host.Enqueue(new ScriptEvent(id, "touch")); host.Enqueue(new ScriptEvent(id, "touch")); }
        var burst = Stopwatch.StartNew(); var ran = 0; var slices = 0; var worstSlice = 0.0;
        while (host.QueuedEvents > 0) { var slice = Stopwatch.StartNew(); ran += host.Process(6); slice.Stop(); worstSlice = Math.Max(worstSlice, slice.Elapsed.TotalMilliseconds); slices++; }
        burst.Stop();

        // five seconds of timers: a tick every 50 ms, the timers of every script fire each second
        var tickTimes = new List<double>();
        for (var t = 0; t < 100; t++)
        {
            world.Now += .05; var tick = Stopwatch.StartNew(); host.Tick(world.Now); host.Process(6); tick.Stop(); tickTimes.Add(tick.Elapsed.TotalMilliseconds);
        }
        var metrics = host.AllMetrics();
        return new Result(count, load.Elapsed.TotalSeconds, Math.Max(0, after - before) / 1048576.0 / count, Math.Max(0, after - before) / 1048576.0, ran / Math.Max(burst.Elapsed.TotalSeconds, 1e-6), burst.Elapsed.TotalMilliseconds / Math.Max(ran, 1), tickTimes.Max(), tickTimes.Average(), host.DroppedEvents + metrics.Sum(m => m.EventsDropped), metrics.Count(m => m.State is ScriptLifecycle.Suspended or ScriptLifecycle.Faulted));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void ManyScripts_LoadRunAndStayHealthy(int count)
    {
        var result = Measure(count); Report(result);
        Assert.True(result.MegabytesPerScript < 0.8, $"{result.MegabytesPerScript:0.00} MB per script: MoonSharp stacks no longer shrunk?");   // 2.3 MB before the stacks were shrunk
        Assert.True(MoonSharpStacks.Available);
        Assert.True(result.Suspended <= count / 100, $"{result.Suspended} suspended");           // a stray GC pause may cost a script a violation, never a crowd
        Assert.True(result.TickMillisecondsWorst < 100, $"a tick took {result.TickMillisecondsWorst:0.0} ms");   // the server budgets 6 ms per tick for scripts; the rest is queued, not run late in one go
    }

    [Fact]
    public void FullSizes_WhenAsked()
    {
        if (Environment.GetEnvironmentVariable("LIFEVERSE_LUA_LOAD") != "full") { output.WriteLine("1000 and 5000 scripts: set LIFEVERSE_LUA_LOAD=full to measure them."); return; }
        foreach (var count in new[] { 1_000, 5_000 }) { var result = Measure(count); Report(result); Assert.True(result.MegabytesPerScript < 0.8); Assert.True(result.Suspended <= count / 50, $"{result.Suspended} suspended"); }
    }

    private void Report(Result r)
    {
        var line = $"{r.Scripts,5} scripts | load {r.LoadSeconds,6:0.00} s | {r.MegabytesPerScript,5:0.00} MB/script ({r.TotalMegabytes,7:0.0} MB) | burst {r.BurstEventsPerSecond,8:0} events/s ({r.MillisecondsPerEvent:0.000} ms/event) | tick worst {r.TickMillisecondsWorst,6:0.0} ms avg {r.TickMillisecondsAverage,5:0.0} ms | dropped {r.Dropped} | suspended {r.Suspended}";
        output.WriteLine(line); Console.WriteLine("LUA_LOAD " + line);
    }
}
