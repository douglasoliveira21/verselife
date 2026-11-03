using System.Diagnostics;
using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>Hard limits applied to every script run. Exceeding one stops the script; it never reaches the host.</summary>
public sealed record LuaLimits(
    int InstructionsPerEvent = 20_000,
    int SliceInstructions = 16,
    double MillisecondsPerEvent = 12,
    long AllocatedBytesPerEvent = 24_000_000,
    int MaxSourceBytes = 64 * 1024,
    int MaxStringResult = 64 * 1024,
    int MaxPatternSubject = 256,
    int MaxPatternQuantifiers = 3,
    int MaxUnpack = 256,
    int MaxSortElements = 2_000);

public enum LuaStatus { Completed, NoHandler, Error, InstructionLimit, TimeLimit, MemoryLimit, Rejected }

public sealed record LuaRunResult(LuaStatus Status, string? Error, long Instructions, double Milliseconds, long AllocatedBytes)
{
    public bool Ok => Status is LuaStatus.Completed or LuaStatus.NoHandler;
    /// <summary>True when the script itself is at fault in a way that should count toward quarantine.</summary>
    public bool Violation => Status is LuaStatus.InstructionLimit or LuaStatus.TimeLimit or LuaStatus.MemoryLimit;
}

/// <summary>
/// One Lua state with only the safe parts of the standard library. Code runs inside a coroutine that is forced to
/// yield every few instructions, so the host can enforce instruction, time and allocation budgets between slices.
/// The few library functions that can do unbounded work in a single step (string.rep, pattern matching, unpack,
/// sort comparators) are wrapped or replaced so they cannot. Nothing in the state can reach files, processes, the
/// network or other scripts.
/// </summary>
public sealed class LuaSandbox
{
    private static readonly string[] RemovedGlobals = ["load", "loadsafe", "loadstring", "dofile", "loadfile", "require", "os", "io", "debug", "collectgarbage", "package", "module", "print", "_MOONSHARP"];

    // Merge sort in Lua: unlike the built-in table.sort it runs its comparator inside the budgeted VM.
    private const string SortPrelude = @"
local function merge_sort(a, n, less)
  local b = {}
  local width = 1
  while width < n do
    local i = 1
    while i <= n do
      local mid = i + width
      local hi = i + 2 * width
      if mid > n + 1 then mid = n + 1 end
      if hi > n + 1 then hi = n + 1 end
      local l, r, k = i, mid, i
      while l < mid and r < hi do
        if less(a[r], a[l]) then b[k] = a[r]; r = r + 1 else b[k] = a[l]; l = l + 1 end
        k = k + 1
      end
      while l < mid do b[k] = a[l]; l = l + 1; k = k + 1 end
      while r < hi do b[k] = a[r]; r = r + 1; k = k + 1 end
      i = i + 2 * width
    end
    a, b = b, a
    width = width * 2
  end
  return a
end
function table.sort(t, comp)
  local n = #t
  if n < 2 then return end
  if n > __sort_limit then error('table.sort: more than ' .. __sort_limit .. ' elements') end
  local less = comp or function(x, y) return x < y end
  local copy = {}
  for i = 1, n do copy[i] = t[i] end
  local sorted = merge_sort(copy, n, less)
  for i = 1, n do t[i] = sorted[i] end
end";

    private static bool _warmed;

    /// <summary>
    /// Runs a small representative script once so the interpreter's first-use costs (JIT, caches) are paid up front;
    /// otherwise the first real events of a freshly started server could look slow enough to count as violations.
    /// </summary>
    public static void WarmUp()
    {
        if (_warmed) return;
        _warmed = true;
        var sandbox = new LuaSandbox(new LuaLimits(MillisecondsPerEvent: 10_000));
        sandbox.Load("function warm() local t = {} for i = 1, 300 do t[i] = tostring(i) .. 'x' end table.sort(t) local s = string.format('%d|%s', 5, string.upper('ok')) local f = string.find(s, '5') local b = {} for k, v in pairs(t) do b[#b + 1] = v end return f end");
        for (var i = 0; i < 3; i++) sandbox.Call("warm");
    }

    private readonly LuaLimits _limits;

    public Script Script { get; }
    public LuaLimits Limits => _limits;

    public LuaSandbox(LuaLimits? limits = null, Action<string>? log = null)
    {
        _limits = limits ?? new LuaLimits();
        Script = new Script(CoreModules.Preset_HardSandbox);
        foreach (var name in RemovedGlobals) Script.Globals[name] = DynValue.Nil;
        Script.Globals["log"] = DynValue.NewCallback((_, args) =>
        {
            var text = string.Join(" ", args.GetArray().Select(arg => arg.ToPrintString()));
            log?.Invoke(text.Length > 200 ? text[..200] : text);
            return DynValue.Nil;
        });
        Harden();
    }

    /// <summary>Loads source and runs its top level (which defines the handlers) under the same budgets.</summary>
    public LuaRunResult Load(string source)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(source) > _limits.MaxSourceBytes) return new(LuaStatus.Rejected, "Script exceeds the source size limit.", 0, 0, 0);
        DynValue chunk;
        try { chunk = Script.LoadString(source, null, "script"); }
        catch (InterpreterException exception) { return new(LuaStatus.Rejected, exception.DecoratedMessage ?? exception.Message, 0, 0, 0); }
        return Run(chunk, []);
    }

    public bool HasHandler(string name) => Script.Globals.Get(name).Type == DataType.Function;

    public LuaRunResult Call(string handler, params DynValue[] arguments)
    {
        var function = Script.Globals.Get(handler);
        return function.Type == DataType.Function ? Run(function, arguments) : new(LuaStatus.NoHandler, null, 0, 0, 0);
    }

    public Table NewTable() => new(Script);

    private LuaRunResult Run(DynValue function, DynValue[] arguments)
    {
        var watch = Stopwatch.StartNew();
        long baseline = 0;
        long instructions = 0;
        LuaRunResult Result(LuaStatus status, string? error = null) => new(status, error, instructions, watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - baseline);
        try
        {
            var thread = Script.CreateCoroutine(function);
            thread.Coroutine.AutoYieldCounter = _limits.SliceInstructions;
            thread.Coroutine.Resume(arguments);
            // The interpreter's own start-up cost is not the script's: budget what is allocated after the first slice.
            baseline = GC.GetAllocatedBytesForCurrentThread();
            while (thread.Coroutine.State == CoroutineState.ForceSuspended)
            {
                instructions += _limits.SliceInstructions;
                if (instructions > _limits.InstructionsPerEvent) return Result(LuaStatus.InstructionLimit, "Instruction budget exceeded.");
                if (watch.Elapsed.TotalMilliseconds > _limits.MillisecondsPerEvent) return Result(LuaStatus.TimeLimit, "Time budget exceeded.");
                if (GC.GetAllocatedBytesForCurrentThread() - baseline > _limits.AllocatedBytesPerEvent) return Result(LuaStatus.MemoryLimit, "Memory budget exceeded.");
                thread.Coroutine.Resume();
            }
            // A single library call cannot be interrupted, so overruns are caught afterwards and count as violations.
            return watch.Elapsed.TotalMilliseconds > _limits.MillisecondsPerEvent * 4 ? Result(LuaStatus.TimeLimit, "Time budget exceeded.") : Result(LuaStatus.Completed);
        }
        catch (InterpreterException exception) { return Result(LuaStatus.Error, exception.DecoratedMessage ?? exception.Message); }
    }

    private void Harden()
    {
        var strings = Script.Globals.Get("string").Table;
        var tables = Script.Globals.Get("table").Table;
        Wrap(strings, "rep", args =>
        {
            var length = (long)(args[0].CastToString()?.Length ?? 0);
            var count = args.Count > 1 ? args[1].CastToNumber() ?? 0 : 0;
            if (count > 0 && length * count > _limits.MaxStringResult) throw new ScriptRuntimeException($"string.rep: result larger than {_limits.MaxStringResult} bytes");
        });
        foreach (var name in new[] { "find", "match", "gmatch", "gsub" })
        {
            var captured = name;
            Wrap(strings, captured, args => ValidatePattern(captured, args));
        }
        Wrap(strings, "gsub", args =>
        {
            if (args.Count > 2 && args[2].Type is DataType.Function or DataType.ClrFunction) throw new ScriptRuntimeException("string.gsub: function replacements are not allowed");
            if (args.Count > 2 && args[2].Type == DataType.String && (long)(args[0].CastToString()?.Length ?? 0) * Math.Max(1, args[2].String.Length) > _limits.MaxStringResult * 8L) throw new ScriptRuntimeException("string.gsub: result too large");
        });
        Wrap(tables, "unpack", ValidateUnpack);
        if (Script.Globals.Get("unpack").Type != DataType.Nil) Wrap(Script.Globals, "unpack", ValidateUnpack);
        strings["dump"] = DynValue.Nil;
        Script.Globals["__sort_limit"] = DynValue.NewNumber(_limits.MaxSortElements);
        Script.DoString(SortPrelude);
    }

    private void ValidatePattern(string name, CallbackArguments args)
    {
        if (args.Count < 2) return;
        var subject = args[0].CastToString() ?? string.Empty; var pattern = args[1].CastToString() ?? string.Empty;
        var plain = name == "find" && args.Count > 3 && args[3].CastToBool();
        if (plain) return;
        if (subject.Length > _limits.MaxPatternSubject) throw new ScriptRuntimeException($"string.{name}: subject longer than {_limits.MaxPatternSubject} characters");
        var quantifiers = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '%') { i++; continue; }
            if (pattern[i] is '*' or '+' or '-' or '?') quantifiers++;
        }
        if (quantifiers > _limits.MaxPatternQuantifiers) throw new ScriptRuntimeException($"string.{name}: pattern has more than {_limits.MaxPatternQuantifiers} repetitions");
    }

    private void ValidateUnpack(CallbackArguments args)
    {
        var from = args.Count > 1 ? args[1].CastToNumber() ?? 1 : 1;
        double? to = args.Count > 2 ? args[2].CastToNumber() : args.Count > 0 && args[0].Type == DataType.Table ? args[0].Table.Length : 0;
        if (to is { } end && end - from + 1 > _limits.MaxUnpack) throw new ScriptRuntimeException($"unpack: more than {_limits.MaxUnpack} values");
    }

    /// <summary>Replaces a library function with one that validates its arguments, then calls the original.</summary>
    private static void Wrap(Table owner, string name, Action<CallbackArguments> validate)
    {
        var original = owner.Get(name);
        if (original.Type == DataType.Nil) return;
        owner[name] = DynValue.NewCallback((context, args) =>
        {
            validate(args);
            return context.GetScript().Call(original, args.GetArray());
        });
    }
}
