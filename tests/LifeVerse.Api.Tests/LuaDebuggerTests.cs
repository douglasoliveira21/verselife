using LifeVerse.Scripting;
using MoonSharp.Interpreter;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The debugger of the sandbox: stops at breakpoints without blocking, shows the variables, steps in, over and out, and does not loosen any budget.</summary>
public sealed class LuaDebuggerTests
{
    // line numbers matter: they are what the breakpoints name
    private const string Source =
        "function triple(n)\n" +                // 1
        "  local t = n * 3\n" +                 // 2
        "  return t\n" +                        // 3
        "end\n" +                               // 4
        "function on_touch(e)\n" +              // 5
        "  local a = 10\n" +                    // 6
        "  local b = triple(a)\n" +             // 7
        "  total = b + 1\n" +                   // 8
        "  local c = total * 2\n" +             // 9
        "  done = c\n" +                        // 10
        "end\n";                                // 11

    private static LuaSandbox Rig(string source = Source, params int[] breakpoints)
    {
        var sandbox = new LuaSandbox(debug: true); Assert.True(sandbox.Load(source).Ok); sandbox.Debugger!.SetBreakpoints(breakpoints); return sandbox;
    }

    private static double Global(LuaSandbox s, string name) => s.Script.Globals.Get(name).Type == DataType.Number ? s.Script.Globals.Get(name).Number : double.NaN;

    [Fact]
    public void ItStopsAtABreakpointWithTheVariables_ThenGoesOn()
    {
        var sandbox = Rig(breakpoints: 8); var result = sandbox.Call("on_touch", DynValue.NewTable(sandbox.NewTable()));
        Assert.Equal(LuaStatus.Paused, result.Status); var snapshot = sandbox.Debugger!.Snapshot!;
        Assert.Equal((8, "breakpoint"), (snapshot.Line, snapshot.Reason));
        Assert.Contains(snapshot.Locals, v => v is { Name: "a", Value: "10" }); Assert.Contains(snapshot.Locals, v => v is { Name: "b", Value: "30" });
        Assert.True(double.IsNaN(Global(sandbox, "total")));                                                                     // line 8 has not run yet
        var done = sandbox.ContinueDebug(DebugCommand.Continue); Assert.True(done.Ok); Assert.Equal((31d, 62d), (Global(sandbox, "total"), Global(sandbox, "done")));
    }

    [Fact]
    public void StepInGoesToTheNextLineEvenInsideACall_StepOverDoesNot_StepOutLeavesTheFunction()
    {
        var into = Rig(breakpoints: 7); into.Call("on_touch", DynValue.NewTable(into.NewTable()));
        into.ContinueDebug(DebugCommand.StepIn); if (into.Debugger!.Snapshot!.Line == 1) into.ContinueDebug(DebugCommand.StepIn);          // the header of the function comes first
        Assert.Equal(2, into.Debugger!.Snapshot!.Line); Assert.Contains(into.Debugger.Snapshot.Locals, v => v.Name == "n" && v.Value == "10");   // inside triple
        var over = Rig(breakpoints: 7); over.Call("on_touch", DynValue.NewTable(over.NewTable()));
        over.ContinueDebug(DebugCommand.StepOver); Assert.Equal(8, over.Debugger!.Snapshot!.Line); Assert.Contains(over.Debugger.Snapshot.Locals, v => v.Name == "b" && v.Value == "30");   // the call ran
        var outside = Rig(breakpoints: 2); outside.Call("on_touch", DynValue.NewTable(outside.NewTable()));
        outside.ContinueDebug(DebugCommand.StepOut); Assert.True(outside.Debugger!.Snapshot!.Line is 7 or 8, $"left the function and stopped at {outside.Debugger.Snapshot.Line}");
        Assert.True(outside.ContinueDebug(DebugCommand.Continue).Ok);
    }

    [Fact]
    public void SteppingWalksThroughTheLinesAndEnds()
    {
        var sandbox = Rig(breakpoints: 6); sandbox.Call("on_touch", DynValue.NewTable(sandbox.NewTable())); var lines = new List<int> { sandbox.Debugger!.Snapshot!.Line };
        for (var i = 0; i < 30; i++) { var r = sandbox.ContinueDebug(DebugCommand.StepOver); if (r.Status != LuaStatus.Paused) { Assert.True(r.Ok); break; } lines.Add(sandbox.Debugger.Snapshot!.Line); }
        Assert.Equal([6, 7, 8, 9, 10, 11], lines); Assert.Equal(62d, Global(sandbox, "done"));
    }

    [Fact]
    public void TheScriptsOwnGlobalsAreShown_WithoutTheApiAndTheLibraries()
    {
        var sandbox = Rig("counter = 7\nname = 'porta'\nlist = {1, 2, 3}\nfunction on_touch(e)\n  local x = 1\n  counter = counter + x\nend\n", 6);
        sandbox.Call("on_touch", DynValue.NewTable(sandbox.NewTable())); var globals = sandbox.Debugger!.Snapshot!.Globals;
        Assert.Contains(globals, v => v is { Name: "counter", Value: "7", Type: "number" }); Assert.Contains(globals, v => v is { Name: "name", Value: "\"porta\"" }); Assert.Contains(globals, v => v.Name == "list" && v.Value.StartsWith("{[1]=1", StringComparison.Ordinal));
        Assert.DoesNotContain(globals, v => v.Name is "string" or "table" or "math" or "on_touch");
    }

    [Fact]
    public void ABreakpointInALoopStopsEveryTime_AndTheBudgetStillHolds()
    {
        var sandbox = Rig("function on_touch(e)\n  for i = 1, 3 do\n    sum = (sum or 0) + i\n  end\nend\n", 3);
        var r = sandbox.Call("on_touch", DynValue.NewTable(sandbox.NewTable())); var hits = 0;
        while (r.Status == LuaStatus.Paused) { hits++; r = sandbox.ContinueDebug(DebugCommand.Continue); }
        Assert.Equal(3, hits); Assert.True(r.Ok); Assert.Equal(6d, Global(sandbox, "sum"));
        var forever = new LuaSandbox(new LuaLimits(MillisecondsPerEvent: 60_000), debug: true); Assert.True(forever.Load("function on_touch(e)\n  while true do x = 1 end\nend\n").Ok); Assert.Equal(LuaStatus.InstructionLimit, forever.Call("on_touch", DynValue.NewTable(forever.NewTable())).Status);   // debugging does not loosen the limit
    }

    [Fact]
    public void AnErrorAfterResumingIsAnError_AndWithoutDebugNothingChanges()
    {
        var sandbox = Rig("function on_touch(e)\n  local a = 1\n  local b = nil + 1\nend\n", 2); sandbox.Call("on_touch", DynValue.NewTable(sandbox.NewTable()));
        var failed = sandbox.ContinueDebug(DebugCommand.Continue); Assert.Equal(LuaStatus.Error, failed.Status);
        var plain = new LuaSandbox(); plain.Load(Source); Assert.Null(plain.Debugger); Assert.True(plain.Call("on_touch", DynValue.NewTable(plain.NewTable())).Ok); Assert.Equal(62d, Global(plain, "done"));
        Assert.Equal(LuaStatus.NoHandler, plain.ContinueDebug(DebugCommand.Continue).Status);                                  // nothing to continue
    }

    [Fact]
    public void DetachingLetsTheScriptRunToTheEnd_AndValuesAreShortTexts()
    {
        var sandbox = Rig(breakpoints: 8); sandbox.Call("on_touch", DynValue.NewTable(sandbox.NewTable())); sandbox.Debugger!.Detach(); Assert.True(sandbox.ContinueDebug(DebugCommand.Continue).Ok); Assert.Equal(62d, Global(sandbox, "done"));
        var longText = LuaDebugger.Describe("s", DynValue.NewString(new string((char)120, 500))).Value; Assert.Equal(LuaDebugger.MaxValueLength + 1, longText.Length); Assert.EndsWith("…", longText);
        Assert.Equal("nil", LuaDebugger.Describe("n", DynValue.Nil).Value); Assert.Equal("function", LuaDebugger.Describe("f", DynValue.NewCallback((_, _) => DynValue.Nil)).Value);
    }
}
