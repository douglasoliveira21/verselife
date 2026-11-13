using System.Text;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;

namespace LifeVerse.Scripting;

public sealed record DebugVariable(string Name, string Type, string Value);

/// <summary>Where a script stopped: the line, why, what the function in progress holds, what the script keeps in its own globals and how deep the calls are.</summary>
public sealed record DebugSnapshot(int Line, string Reason, string Handler, IReadOnlyList<DebugVariable> Locals, IReadOnlyList<DebugVariable> Globals, int Depth);

public enum DebugCommand { Continue, StepIn, StepOver, StepOut }

/// <summary>
/// A debugger that never blocks the island: when the script reaches a line with a breakpoint (or the next line, when stepping), it takes a picture of the variables and makes the interpreter yield after the next instruction, the same way
/// the budget slices do. The sandbox then hands control back to the island, which goes on with everything else, and the script waits (its events are dropped meanwhile, like a paused script) until somebody continues or steps.
/// Only scripts the owner put in debug mode have it; the interpreter is slower in that mode. Lines are counted from 1.
/// </summary>
public sealed class LuaDebugger : IDebugger
{
    public const int MaxBreakpoints = 20, MaxVariables = 40, MaxValueLength = 160, MaxTableEntries = 6;

    private enum Phase { Running, Capturing }
    private readonly Script _script;
    private DebugService? _service; private SourceCode? _source;
    private readonly HashSet<int> _breakpoints = [];
    private HashSet<string> _baseline = [];
    private Phase _phase; private int _lastLine = -1; private string _reason = "breakpoint";
    private DebugCommand _command = DebugCommand.Continue; private int _commandDepth, _stepOrigin;
    private readonly List<DebugVariable> _locals = []; private int _depth;
    private readonly int _sliceInstructions;

    public LuaDebugger(Script script, int sliceInstructions) { _script = script; _sliceInstructions = sliceInstructions; }

    /// <summary>The coroutine the events run in (the sandbox tells, and tells again when it makes a new one).</summary>
    public Coroutine? Coroutine { get; set; }
    /// <summary>True from the moment the script stops at a line until somebody resumes it.</summary>
    public bool PausedNow { get; private set; }
    public DebugSnapshot? Snapshot { get; private set; }
    /// <summary>The function that was running when the script stopped (set by the sandbox).</summary>
    public string Handler { get; set; } = "";
    public IReadOnlyCollection<int> Breakpoints => _breakpoints;

    /// <summary>The names the script had before its own code ran: what is not in it is the script's own data.</summary>
    public void MarkBaseline() => _baseline = [.. _script.Globals.Keys.Select(k => k.ToPrintString())];

    /// <summary>Replaces the breakpoints (at most twenty lines, from 1).</summary>
    public void SetBreakpoints(IEnumerable<int> lines)
    {
        _breakpoints.Clear(); foreach (var line in lines.Where(l => l >= 1 && l <= 100_000).Distinct().Take(MaxBreakpoints)) _breakpoints.Add(line);
        _service?.ResetBreakPoints(_source!, new HashSet<int>(_breakpoints));
    }

    /// <summary>Lets the script go on: until the next breakpoint, to the next line, to the next line of the same or a higher function, or until the function that is running returns.</summary>
    public void Resume(DebugCommand command)
    {
        _commandDepth = Snapshot?.Depth ?? _depth; if (Snapshot is not null) { _lastLine = Snapshot.Line; _stepOrigin = Snapshot.Line; } _command = command; PausedNow = false; Snapshot = null;
        if (Coroutine is not null) Coroutine.AutoYieldCounter = _sliceInstructions;
    }

    /// <summary>The script is let go for good (debugging ended): no more stops.</summary>
    public void Detach() { _breakpoints.Clear(); if (_source is not null) _service?.ResetBreakPoints(_source, []); _command = DebugCommand.Continue; }      // a script that is stopped stays so until it is resumed

    // ---- what the interpreter asks ------------------------------------------------------------------------------------------------

    public DebuggerCaps GetDebuggerCaps() => DebuggerCaps.CanDebugSourceCode | DebuggerCaps.CanDebugByteCode;
    public void SetDebugService(DebugService debugService) => _service = debugService;
    public void SetSourceCode(SourceCode sourceCode) { if (sourceCode.Name == "script") { _source = sourceCode; _service?.ResetBreakPoints(sourceCode, new HashSet<int>(_breakpoints)); } }
    public void SetByteCode(string[] byteCode) { }
    public bool IsPauseRequested() => !PausedNow && (_command != DebugCommand.Continue || _breakpoints.Count > 0);          // asked at every instruction: the breakpoints are checked here, not by the interpreter, so a loop stops every time
    public bool SignalRuntimeException(ScriptRuntimeException ex) => false;
    public void SignalExecutionEnded() { }
    public List<DynamicExpression> GetWatchItems() => [];
    public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }

    public void Update(WatchType watchType, IEnumerable<WatchItem> items)
    {
        if (watchType == WatchType.CallStack) _depth = items.Count();
        else if (watchType == WatchType.Locals) { _locals.Clear(); foreach (var item in items.Take(MaxVariables)) if (item.Name is { Length: > 0 } name && !name.StartsWith('(')) _locals.Add(Describe(name, item.Value)); }
    }

    public DebuggerAction GetAction(int ip, SourceRef sourceref)
    {
        if (PausedNow) return Run;
        if (_phase == Phase.Capturing)
        {
            _phase = Phase.Running;                                           // the variables of the Refresh we asked for have arrived
            if (_command != DebugCommand.Continue && _lastLine == _stepOrigin && _depth <= _commandDepth) return Run;          // back on the line we started from (a call returned to it): not a new line
            if (_command == DebugCommand.StepOver && _depth > _commandDepth) return Run;          // still inside a call: not yet
            if (_command == DebugCommand.StepOut && _depth >= _commandDepth) return Run;          // the function has not returned yet
            Pause(); return Run;
        }
        if (sourceref is null || sourceref.IsClrLocation || (_source is not null && sourceref.SourceIdx != _source.SourceID)) return Run;          // only the lines of the script itself (not the interpreter-side loop that serves its events)
        var line = sourceref.FromLine;
        if (line == _lastLine) return Run;
        _lastLine = line;
        var breakpoint = _breakpoints.Contains(line);
        if (breakpoint || _command != DebugCommand.Continue) { _reason = breakpoint ? "breakpoint" : "step"; _phase = Phase.Capturing; return new DebuggerAction { Action = DebuggerAction.ActionType.Refresh }; }
        return Run;
    }

    private static DebuggerAction Run => new() { Action = DebuggerAction.ActionType.Run };

    private void Pause()
    {
        PausedNow = true; _command = DebugCommand.Continue;
        Snapshot = new DebugSnapshot(_lastLine, _reason, Handler, [.. _locals], OwnGlobals(), _depth);
        if (Coroutine is not null) Coroutine.AutoYieldCounter = 1;           // yield after the next instruction: the island gets control back
    }

    private List<DebugVariable> OwnGlobals()
    {
        var list = new List<DebugVariable>();
        foreach (var pair in _script.Globals.Pairs)
        {
            var name = pair.Key.ToPrintString(); if (_baseline.Contains(name) || name.StartsWith("__", StringComparison.Ordinal) || pair.Value.Type is DataType.Function or DataType.ClrFunction) continue;
            list.Add(Describe(name, pair.Value)); if (list.Count >= MaxVariables) break;
        }
        return list;
    }

    /// <summary>A value as a short text: strings quoted and cut, tables with their first few entries, functions by name only.</summary>
    public static DebugVariable Describe(string name, DynValue? value)
    {
        value ??= DynValue.Nil;
        string text = value.Type switch
        {
            DataType.Nil or DataType.Void => "nil",
            DataType.Boolean => value.Boolean ? "true" : "false",
            DataType.Number => value.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            DataType.String => "\"" + value.String.Replace("\n", "\\n") + "\"",
            DataType.Table => DescribeTable(value.Table),
            DataType.Function or DataType.ClrFunction => "function",
            _ => value.Type.ToString().ToLowerInvariant()
        };
        if (text.Length > MaxValueLength) text = text[..MaxValueLength] + "…";
        return new DebugVariable(name, value.Type.ToLuaTypeString(), text);
    }

    private static string DescribeTable(Table table)
    {
        var builder = new StringBuilder("{"); var shown = 0; var total = 0;
        foreach (var pair in table.Pairs)
        {
            total++; if (shown >= MaxTableEntries) continue;
            var key = pair.Key.Type == DataType.String ? pair.Key.String : "[" + pair.Key.ToPrintString() + "]";
            var inner = pair.Value.Type switch { DataType.Table => "{…}", DataType.Function or DataType.ClrFunction => "function", DataType.String => "\"" + pair.Value.String + "\"", _ => pair.Value.ToPrintString() };
            if (shown++ > 0) builder.Append(", "); builder.Append(key).Append('=').Append(inner.Length > 40 ? inner[..40] + "…" : inner);
        }
        if (total > shown) builder.Append($", … +{total - shown}");
        return builder.Append('}').ToString();
    }
}
