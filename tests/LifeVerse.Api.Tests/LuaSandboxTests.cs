using LifeVerse.Scripting;
using MoonSharp.Interpreter;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class LuaSandboxTests
{
    /// <summary>Runs a script on a background thread; a null result means the sandbox failed to stop it in time.</summary>
    private static (LuaRunResult? Result, LuaSandbox Sandbox, double Milliseconds) Run(string source, string handler = "run", LuaLimits? limits = null)
    {
        var sandbox = new LuaSandbox(limits);
        LuaRunResult? result = null;
        var started = DateTime.UtcNow;
        var thread = new Thread(() => { var load = sandbox.Load(source); result = load.Ok ? sandbox.Call(handler) : load; }) { IsBackground = true };
        thread.Start();
        var finished = thread.Join(TimeSpan.FromSeconds(8));
        return (finished ? result : null, sandbox, (DateTime.UtcNow - started).TotalMilliseconds);
    }

    [Fact]
    public void Handler_RunsAndCanChangeGlobals()
    {
        var (result, sandbox, _) = Run("count = 0\nfunction run() count = count + 1 end");
        Assert.Equal(LuaStatus.Completed, result!.Status);
        Assert.Equal(1d, sandbox.Script.Globals.Get("count").Number);
        sandbox.Call("run"); sandbox.Call("run");
        Assert.Equal(3d, sandbox.Script.Globals.Get("count").Number);
    }

    [Fact]
    public void Handler_ReceivesArgumentsAndMissingHandlersAreIgnored()
    {
        var sandbox = new LuaSandbox();
        Assert.True(sandbox.Load("function on_touch(event) seen = event.name .. ':' .. event.n end").Ok);
        var args = sandbox.NewTable(); args["name"] = "ana"; args["n"] = 3;
        Assert.Equal(LuaStatus.Completed, sandbox.Call("on_touch", DynValue.NewTable(args)).Status);
        Assert.Equal("ana:3", sandbox.Script.Globals.Get("seen").String);
        Assert.Equal(LuaStatus.NoHandler, sandbox.Call("on_missing").Status);
        Assert.True(sandbox.HasHandler("on_touch") && !sandbox.HasHandler("on_missing"));
    }

    [Fact]
    public void ErrorsAreReportedWithoutThrowing()
    {
        Assert.Equal(LuaStatus.Error, Run("function run() error('boom') end").Result!.Status);
        Assert.Contains("boom", Run("function run() error('boom') end").Result!.Error);
        Assert.Equal(LuaStatus.Error, Run("function run() local x = nil; return x.field end").Result!.Status);
        Assert.Equal(LuaStatus.Rejected, Run("function run( end").Result!.Status);
        Assert.Equal(LuaStatus.Rejected, Run(new string('-', 70_000)).Result!.Status);
    }

    [Theory]
    [InlineData("os")] [InlineData("io")] [InlineData("debug")] [InlineData("load")] [InlineData("loadstring")] [InlineData("dofile")]
    [InlineData("loadfile")] [InlineData("require")] [InlineData("package")] [InlineData("collectgarbage")] [InlineData("print")]
    [InlineData("setmetatable")] [InlineData("getmetatable")] [InlineData("rawset")] [InlineData("coroutine")] [InlineData("pcall")]
    [InlineData("_G.os")] [InlineData("_G['i'..'o']")] [InlineData("_G['lo'..'ad']")] [InlineData("string.dump")]
    public void DangerousGlobalsDoNotExist(string expression)
    {
        var (result, sandbox, _) = Run($"function run() found = type({expression}) end");
        Assert.True(result!.Ok || result.Status == LuaStatus.Error);
        if (result.Ok) Assert.Equal("nil", sandbox.Script.Globals.Get("found").String);
    }

    [Fact]
    public void ReachingForOtherScriptsOrTheHostIsImpossible()
    {
        var a = new LuaSandbox(); var b = new LuaSandbox();
        a.Load("secret = 42"); b.Load("function run() leaked = secret end");
        b.Call("run");
        Assert.Equal(DataType.Nil, b.Script.Globals.Get("leaked").Type);
        Assert.Equal(DataType.Nil, b.Script.Globals.Get("secret").Type);
    }

    [Theory]
    [InlineData("while true do end")]
    [InlineData("repeat until false")]
    [InlineData("for i = 1, 1e12 do end")]
    [InlineData("local t = {} for i = 1, 1e9 do t[i] = i end")]
    [InlineData("local function f() return 1 + f() end f()")]
    [InlineData("local t = {5, 4, 3} table.sort(t, function(a, b) while true do end end)")]
    public void RunawayCodeIsStoppedQuickly(string body)
    {
        var (result, _, milliseconds) = Run($"function run() {body} end");
        Assert.NotNull(result);
        Assert.False(result!.Ok, $"{body} was allowed to finish: {result.Status}");
        Assert.True(milliseconds < 1500, $"took {milliseconds:0} ms");
    }

    [Fact]
    public void InstructionBudgetTimeBudgetAndMemoryBudgetReportTheirOwnStatus()
    {
        Assert.Equal(LuaStatus.InstructionLimit, Run("function run() while true do end end", limits: new LuaLimits(MillisecondsPerEvent: 5000)).Result!.Status);
        Assert.Equal(LuaStatus.MemoryLimit, Run("function run() local s = 'x' for i = 1, 60 do s = s .. s end end", limits: new LuaLimits(MillisecondsPerEvent: 5000)).Result!.Status);
        Assert.True(Run("function run() while true do end end", limits: new LuaLimits(InstructionsPerEvent: int.MaxValue, MillisecondsPerEvent: 20)).Result!.Violation);
    }

    [Theory]
    [InlineData("local s = string.rep('x', 1000000000)")]
    [InlineData("local s = ('x'):rep(2000000000)")]
    [InlineData("local s = string.rep('abc', 100000000)")]
    [InlineData("local s = string.format('%999999999d', 1)")]
    [InlineData("local t = unpack({}, 1, 1000000000)")]
    [InlineData("local t = table.unpack({}, 1, 1000000000)")]
    [InlineData("string.gsub('abc', '.', function(c) while true do end end)")]
    [InlineData("string.find(('a'):rep(200), ('a*'):rep(10) .. 'b')")]
    [InlineData("string.match(('a'):rep(1000), 'a')")]
    [InlineData("local s = string.gsub(('x'):rep(60000), '.', ('y'):rep(60000))")]
    public void SingleCallBombsAreRefusedBeforeTheyRun(string body)
    {
        var (result, _, milliseconds) = Run($"function run() {body} end");
        Assert.NotNull(result);
        Assert.Equal(LuaStatus.Error, result!.Status);
        Assert.True(milliseconds < 1000, $"took {milliseconds:0} ms");
        Assert.True(result.AllocatedBytes < 8_000_000, $"allocated {result.AllocatedBytes}");
    }

    [Fact]
    public void NormalStringAndTableWorkStillWorks()
    {
        var (result, sandbox, _) = Run(@"
function run()
  local t = {}
  for i = 1, 200 do t[i] = (i * 7919) % 211 end
  table.sort(t)
  local ok = true
  for i = 2, #t do if t[i - 1] > t[i] then ok = false end end
  table.sort(t, function(a, b) return a > b end)
  for i = 2, #t do if t[i - 1] < t[i] then ok = false end end
  sorted = ok
  text = string.format('%05.1f|%s', 3.14159, string.upper('lua')) .. string.rep('ab', 3)
  found = string.find('hello world', 'o w')
  word = string.match('say hello now', '(%a+) now')
  swapped = string.gsub('a-b-c', '-', '+')
  top = math.max(unpack({3, 9, 4}))
end", limits: new LuaLimits(InstructionsPerEvent: 400_000, MillisecondsPerEvent: 200));
        Assert.Equal(LuaStatus.Completed, result!.Status);
        var g = sandbox.Script.Globals;
        Assert.True(g.Get("sorted").Boolean);
        Assert.Equal("003.1|LUAababab", g.Get("text").String);
        Assert.Equal(5d, g.Get("found").Number);
        Assert.Equal("hello", g.Get("word").String);
        Assert.Equal("a+b+c", g.Get("swapped").String);
        Assert.Equal(9d, g.Get("top").Number);
    }

    [Fact]
    public void ALegitimateButBusyHandlerFitsTheBudget()
    {
        var (result, sandbox, _) = Run("function run() local sum = 0 for i = 1, 1500 do sum = sum + i end total = sum end", limits: new LuaLimits(MillisecondsPerEvent: 1000));
        Assert.Equal(LuaStatus.Completed, result!.Status);
        Assert.Equal(1125750d, sandbox.Script.Globals.Get("total").Number);
    }

    [Fact]
    public void AFailedRunDoesNotPoisonLaterRuns()
    {
        var sandbox = new LuaSandbox();
        sandbox.Load("function spin() while true do end end function ok() good = true end");
        Assert.True(sandbox.Call("spin").Violation);
        Assert.Equal(LuaStatus.Completed, sandbox.Call("ok").Status);
        Assert.True(sandbox.Script.Globals.Get("good").Boolean);
    }
}
