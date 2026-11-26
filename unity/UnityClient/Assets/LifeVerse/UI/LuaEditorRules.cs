using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The rules of the script editor that do not need the screen (the same as the Godot client's <c>ScriptEditorWindow</c> and <c>ScriptApi.Runtime</c>): completing code from the reference of the
    /// language, the lines of a text, the breakpoints, the commands of the debugger and what the island answers, how a script is doing on the island and the full check of a source.
    /// </summary>
    public static class LuaEditorRules
    {
        public static readonly string[] Namespaces = { "object", "chat", "timer", "storage", "debug", "region", "script", "permissions", "dialog", "audio", "lv" };
        public static readonly string[] Keywords = { "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while" };
        public static readonly string[] DefaultEvents = { "on_start", "on_touch", "on_enter", "on_leave", "on_timer", "on_chat", "on_message" };
        /// <summary>What the owner may let the script do over the object (and the words for it).</summary>
        public static readonly (string Name, string Text)[] OwnerPermissions = { ("CONTROL_PHYSICS", "Controlar a física do objeto (physics.*)"), ("HTTP_REQUESTS", "Acessar endereços externos (http.*)"), ("CONTROL_MEDIA", "Controlar a mídia do objeto"), ("CREATE_NPCS", "Criar NPCs (npc.*)"), ("CONTROL_VEHICLES", "Dirigir os seus veículos parados (vehicle.*)") };
        private static readonly Regex Member = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z0-9_]*)$"), Word = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)$"), Local = new Regex(@"\blocal\s+([A-Za-z_][A-Za-z0-9_]*)");

        /// <summary>The reference of the language: (namespace, name) of each function and the names of the events. Null pieces fall back to what the client knows.</summary>
        public sealed class Reference { public List<(string Namespace, string Name, string Signature)> Functions = new List<(string, string, string)>(); public List<string> Events = new List<string>(); }

        public static Reference ReadReference(string body)
        {
            var r = new Reference();
            try
            {
                using var d = JsonDocument.Parse(body); var root = d.RootElement;
                if (root.TryGetProperty("functions", out var f) && f.ValueKind == JsonValueKind.Array) foreach (var x in f.EnumerateArray()) r.Functions.Add((S(x, "namespace"), S(x, "name"), S(x, "signature")));
                if (root.TryGetProperty("events", out var e) && e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) if (S(x, "name").Length > 0) r.Events.Add(S(x, "name"));
            }
            catch (JsonException) { }
            return r;
        }

        /// <summary>What can complete the text just before the cursor: after "ns." the functions of that namespace; otherwise namespaces, events, keywords and the locals of the source. Shown text and what to insert.</summary>
        public static List<(string Shown, string Insert)> CompletionFor(string beforeCursor, Reference reference, string source)
        {
            var result = new List<(string, string)>(); beforeCursor ??= "";
            var member = Member.Match(beforeCursor);
            if (member.Success)
            {
                var ns = member.Groups[1].Value; var typed = member.Groups[2].Value;
                foreach (var name in (reference?.Functions ?? new List<(string, string, string)>()).Where(f => f.Item1 == ns).Select(f => f.Item2).Distinct().Where(n => n.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal)) result.Add((name, name + "("));
                return result;
            }
            var w = Word.Match(beforeCursor); var word = w.Success ? w.Groups[1].Value : ""; if (word.Length == 0) return result;
            foreach (var ns in Namespaces.Where(n => n.StartsWith(word, StringComparison.OrdinalIgnoreCase) && n != word)) result.Add((ns, ns));
            foreach (var name in (reference != null && reference.Events.Count > 0 ? (IEnumerable<string>)reference.Events : DefaultEvents).Where(n => n.StartsWith(word, StringComparison.OrdinalIgnoreCase) && n != word)) result.Add((name, name + "()"));
            foreach (var keyword in Keywords.Where(k => k.StartsWith(word, StringComparison.Ordinal) && k != word)) result.Add((keyword, keyword));
            foreach (Match local in Local.Matches(source ?? "")) { var name = local.Groups[1].Value; if (name.StartsWith(word, StringComparison.Ordinal) && name != word && result.All(r => r.Item1 != name)) result.Add((name, name)); }
            return result;
        }

        /// <summary>The word being typed just before the cursor (what a completion replaces).</summary>
        public static string Typed(string beforeCursor) { var m = Word.Match(beforeCursor ?? ""); return m.Success ? m.Groups[1].Value : ""; }

        /// <summary>The line (from 1) of a position of a text.</summary>
        public static int LineOf(string text, int index) { var line = 1; for (var i = 0; i < Math.Min(index, (text ?? "").Length); i++) if (text[i] == '\n') line++; return line; }
        public static int LineCount(string text) => string.IsNullOrEmpty(text) ? 1 : text.Count(c => c == '\n') + 1;
        /// <summary>Where a line starts and ends (end excludes the line break); (-1, -1) when there is no such line.</summary>
        public static (int Start, int End) LineRange(string text, int line)
        {
            text ??= ""; if (line < 1) return (-1, -1); var start = 0;
            for (var l = 1; l < line; l++) { var next = text.IndexOf('\n', start); if (next < 0) return (-1, -1); start = next + 1; }
            var end = text.IndexOf('\n', start); return (start, end < 0 ? text.Length : end);
        }

        /// <summary>The lines of the breakpoints typed as "3, 7 12": sorted, without repeats, only lines that exist.</summary>
        public static List<int> ParseLines(string text, int lineCount) => Regex.Matches(text ?? "", @"\d+").Cast<Match>().Select(m => int.TryParse(m.Value, out var n) ? n : 0).Where(n => n >= 1 && n <= lineCount).Distinct().OrderBy(n => n).ToList();
        public static List<int> Toggle(IEnumerable<int> lines, int line) { var list = lines.ToList(); if (!list.Remove(line)) list.Add(line); list.Sort(); return list; }
        public static string LinesText(IEnumerable<int> lines) => string.Join(", ", lines);

        /// <summary>A command of the debugger, as the island expects it through the connection: start, stop, breakpoints, continue, stepin, stepover, stepout.</summary>
        public static string DebugCommand(Guid script, string action, IEnumerable<int> breakpoints) => JsonSerializer.Serialize(new { debug = true, script, action, breakpoints = breakpoints.ToArray() });

        /// <summary>What the island said about the script being debugged, for this script; null for anything else.</summary>
        public sealed class DebugEvent { public string Kind = "", Reason = "", Handler = ""; public bool Debugging, Stopped; public int Line; public string Text = ""; }

        public static DebugEvent ReadDebug(string json, Guid script)
        {
            try
            {
                using var d = JsonDocument.Parse(json); var root = d.RootElement;
                if (S(root, "operation") != "script_debug" || !Guid.TryParse(S(root, "script"), out var id) || id != script) return null;
                var e = new DebugEvent { Kind = S(root, "event"), Reason = S(root, "reason"), Handler = S(root, "handler"), Debugging = root.TryGetProperty("debugging", out var dg) && dg.ValueKind == JsonValueKind.True, Stopped = root.TryGetProperty("stopped", out var st) && st.ValueKind == JsonValueKind.True };
                if (root.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number) e.Line = line.GetInt32();
                if (e.Stopped) e.Text = StoppedText(root);
                return e;
            }
            catch (JsonException) { return null; }
        }

        private static string StoppedText(JsonElement root)
        {
            var text = new StringBuilder("Parado na linha " + (root.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0) + " (" + (S(root, "reason") == "step" ? "passo" : "ponto de parada") + ") · evento " + S(root, "handler") + "\n\nVariáveis locais:\n");
            void List(string key)
            {
                var any = false;
                if (root.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array) foreach (var v in items.EnumerateArray()) { any = true; text.Append("  " + S(v, "name") + " = " + S(v, "value") + "  (" + S(v, "type") + ")\n"); }
                if (!any) text.Append("  (nenhuma)\n");
            }
            List("locals"); text.Append("\nO que o script guarda (variáveis globais dele):\n"); List("globals");
            return text.ToString();
        }

        /// <summary>How a script is doing on the island (<c>/runtime</c>): its state, use, console and the permissions it holds.</summary>
        public sealed class Runtime
        {
            public string Status = "", State = "", StateText = "", LastError = "", SuspendedReason = ""; public bool Reported; public int Version, LastErrorLine, ActiveTimers, StateKeys;
            public long Events, Errors, Violations, ApiCalls, Instructions, AllocatedBytes, MessagesSent; public double CpuMilliseconds, WorstEventMilliseconds;
            public List<(string Level, string Event, string Text, string Stack, string At)> Logs = new List<(string, string, string, string, string)>();
            public List<(string Subject, string Permission)> Grants = new List<(string, string)>();
        }

        public static Runtime ReadRuntime(string body)
        {
            try
            {
                using var d = JsonDocument.Parse(body); var root = d.RootElement; var r = new Runtime { Status = S(root, "status"), Version = (int)L(root, "version") };
                if (root.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.Object)
                {
                    r.Reported = true; r.State = S(rt, "state"); r.StateText = S(rt, "stateText"); r.Events = L(rt, "events"); r.Errors = L(rt, "errors"); r.Violations = L(rt, "violations"); r.ApiCalls = L(rt, "apiCalls"); r.Instructions = L(rt, "instructions");
                    r.AllocatedBytes = L(rt, "allocatedBytes"); r.MessagesSent = L(rt, "messagesSent"); r.CpuMilliseconds = D(rt, "cpuMilliseconds"); r.WorstEventMilliseconds = D(rt, "worstEventMilliseconds"); r.ActiveTimers = (int)L(rt, "activeTimers"); r.StateKeys = (int)L(rt, "stateKeys");
                    r.LastError = S(rt, "lastError"); r.LastErrorLine = (int)L(rt, "lastErrorLine"); r.SuspendedReason = S(rt, "suspendedReason");
                }
                if (root.TryGetProperty("logs", out var logs) && logs.ValueKind == JsonValueKind.Array) foreach (var x in logs.EnumerateArray()) r.Logs.Add((S(x, "level"), S(x, "eventName"), S(x, "text"), S(x, "stackTrace"), S(x, "createdAt")));
                if (root.TryGetProperty("grants", out var grants) && grants.ValueKind == JsonValueKind.Array) foreach (var x in grants.EnumerateArray()) r.Grants.Add((S(x, "subjectId"), S(x, "permission")));
                return r;
            }
            catch (JsonException) { return null; }
        }

        public static string FormatLog((string Level, string Event, string Text, string Stack, string At) line)
        {
            var at = line.At.Length >= 19 ? line.At.Replace('T', ' ').Substring(11, 8) : line.At; var level = line.Level == "error" ? "ERRO" : line.Level == "warning" ? "AVISO" : "info";
            return at + "  " + level.PadRight(5) + "  " + line.Event + ": " + line.Text + (line.Stack.Length > 0 ? "\n               ↳ " + line.Stack : "");
        }

        public static string Performance(Runtime r)
        {
            if (r == null) return "Não foi possível ler o desempenho agora.";
            if (!r.Reported) return "Sem dados ainda: a ilha informa o uso a cada 30 segundos.";
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            return "Estado: " + (r.StateText.Length > 0 ? r.StateText : r.State) + "\nEventos: " + r.Events.ToString("N0", pt) + "   erros: " + r.Errors + "   violações: " + r.Violations + "\nChamadas à API: " + r.ApiCalls.ToString("N0", pt) + "   instruções: " + r.Instructions.ToString("N0", pt)
                + "\nMemória: " + (r.AllocatedBytes / 1024d).ToString("N0", pt) + " KB   mensagens: " + r.MessagesSent + "\nCPU: " + r.CpuMilliseconds.ToString("0.0", pt) + " ms (pior evento " + r.WorstEventMilliseconds.ToString("0.0", pt) + " ms)   temporizadores: " + r.ActiveTimers + "   chaves guardadas: " + r.StateKeys
                + (r.LastError.Length > 0 ? "\nÚltimo erro" + (r.LastErrorLine > 0 ? " (linha " + r.LastErrorLine + ")" : "") + ": " + r.LastError : "") + (r.SuspendedReason.Length > 0 ? "\nSuspenso: " + r.SuspendedReason : "");
        }

        /// <summary>The full check of a source by the server (<c>/validate</c>): valid, the errors and warnings with their lines, and the events the script handles.</summary>
        public static (bool Valid, List<(string Severity, int Line, string Message)> Problems, List<string> Handlers) ReadCheck(string body)
        {
            var problems = new List<(string, int, string)>(); var handlers = new List<string>(); var valid = false;
            try
            {
                using var d = JsonDocument.Parse(body); var root = d.RootElement; valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
                foreach (var (name, severity) in new[] { ("errors", "error"), ("warnings", "warning") }) if (root.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array) foreach (var item in items.EnumerateArray()) problems.Add((severity, (int)L(item, "line"), S(item, "message")));
                if (root.TryGetProperty("handlers", out var h) && h.ValueKind == JsonValueKind.Array) foreach (var x in h.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) handlers.Add(x.GetString());
            }
            catch (JsonException) { }
            return (valid, problems, handlers);
        }

        private static string S(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        private static long L(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
        private static double D(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : 0;
    }
}
