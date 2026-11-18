using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

public sealed record ScriptCompilation(bool Valid, IReadOnlyList<ScriptDiagnostic> Diagnostics, IReadOnlyList<string> Handlers);

/// <summary>
/// Checks a script without running any of it: the syntax (the same parser the island server runs it with, so what is accepted here loads there), the calls the sandbox does not have, and the mistakes that would only show up
/// when the event happens (an event or a function with a misspelled name). Errors stop the script from being saved as valid; warnings are shown beside the code.
/// </summary>
public static partial class ScriptCompiler
{
    [GeneratedRegex(@"^[^:]*:\((?<line>\d+),\d+(?:-\d+)?\):\s*(?<msg>.*)$", RegexOptions.Singleline)]
    private static partial Regex Decorated();
    [GeneratedRegex(@"\bfunction\s+(?<name>on_[A-Za-z0-9_]*)\s*\(")]
    private static partial Regex HandlerPattern();
    [GeneratedRegex(@"(?<![A-Za-z0-9_.:])(?<ns>[A-Za-z_][A-Za-z0-9_]*)\.(?<fn>[A-Za-z_][A-Za-z0-9_]*)\s*[\(\{'""]")]
    private static partial Regex CallPattern();
    [GeneratedRegex(@"(?<![A-Za-z0-9_.:])(?<name>os|io|package|coroutine|require|load|loadstring|loadfile|dofile|setmetatable|getmetatable|rawget|rawset|collectgarbage|string\.dump)\b\s*[\.\(]")]
    private static partial Regex BlockedPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:timer\.(?:every|after)|object\.(?:create_timer|createTimer))\s*\(\s*(?<n>\d+(?:\.\d+)?|\.\d+)")]
    private static partial Regex TimerLiteral();
    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*|\d+")]
    private static partial Regex Tokens();

    private static readonly HashSet<string> DebugAllowed = ["log", "warn", "error"];

    public static ScriptCompilation Compile(string source)
    {
        var diagnostics = new List<ScriptDiagnostic>();
        if (string.IsNullOrWhiteSpace(source)) return new(false, [new("error", 0, "O script está vazio.")], []);
        if (Encoding.UTF8.GetByteCount(source) > new LuaLimits().MaxSourceBytes) return new(false, [new("error", 0, "O script passa do tamanho máximo (64 KB).")], []);
        var syntax = SyntaxError(source);
        if (syntax is not null) return new(false, [new("error", syntax.Line, syntax.Message)], []);

        var clean = Strip(source); var lines = clean.Split('\n'); var handlers = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = i + 1;
            foreach (Match match in HandlerPattern().Matches(lines[i]))
            {
                var name = match.Groups["name"].Value; handlers.Add(name);
                if (!ScriptCatalog.EventNames.Contains(name)) diagnostics.Add(new("warning", line, $"“{name}” não é um evento conhecido; ele nunca será chamado.{Suggest(name, ScriptCatalog.EventNames)}"));
            }
            foreach (Match match in BlockedPattern().Matches(lines[i]))
                diagnostics.Add(new("error", line, $"“{match.Groups["name"].Value.TrimEnd()}” não existe no ambiente dos scripts: o servidor não permite arquivos, processos, rede, carregamento de código nem metatabelas."));
            foreach (Match match in CallPattern().Matches(lines[i]))
            {
                var ns = match.Groups["ns"].Value; var fn = match.Groups["fn"].Value;
                if (ns == "debug" && !DebugAllowed.Contains(fn)) { diagnostics.Add(new("error", line, $"debug.{fn} não existe: só debug.log, debug.warn e debug.error.")); continue; }
                if (ns is "object" or "chat" or "timer" or "storage" or "region" or "permissions" or "dialog" or "audio" or "script" or "http" or "physics" or "economy" or "inventory" or "avatar" or "hud" or "particles" or "light" or "parcel" or "npc" or "vehicle" or "media" or "animation" && ScriptCatalog.ByNamespace.TryGetValue(ns, out var known) && !known.Contains(fn) && !(ns == "object" && ScriptCatalog.LegacyObject.Contains(fn)))
                    diagnostics.Add(new("warning", line, $"{ns}.{fn} não existe na API.{Suggest(fn, known, ns + ".")}"));
            }
        }
        for (var i = 0; i < lines.Length; i++)
            foreach (Match match in TimerLiteral().Matches(lines[i]))
                if (double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && (seconds < 0.25 || seconds > 86_400))
                    diagnostics.Add(new("error", i + 1, "Um temporizador só aceita de 0,25 s a 24 h. Com esse valor a chamada falha e o script não repete."));
        diagnostics.AddRange(EndlessLoops(clean));
        if (handlers.Count == 0) diagnostics.Add(new("warning", 0, "O script não trata nenhum evento (por exemplo on_start ou on_touch); ele não vai fazer nada."));
        foreach (var duplicate in handlers.GroupBy(h => h).Where(g => g.Count() > 1)) diagnostics.Add(new("warning", 0, $"{duplicate.Key} está definido {duplicate.Count()} vezes; só a última vale."));
        return new(diagnostics.All(d => d.Severity != "error"), diagnostics, [.. handlers.Distinct()]);
    }

    private const string EndlessMessage = "Este laço nunca termina: o servidor o interrompe pelo limite de instruções e o script para. O servidor trata um evento por vez e não há espera dentro do script; para repetir uma ação use timer.every(segundos, função).";

    /// <summary>A <c>while true do</c> or <c>repeat … until false</c> with no <c>break</c> or <c>return</c> inside can only end by blowing the instruction limit. Looks at the code without comments and strings; a loop that can leave is never reported.</summary>
    private static List<ScriptDiagnostic> EndlessLoops(string clean)
    {
        var found = new List<ScriptDiagnostic>();
        var tokens = Tokens().Matches(clean).Select(m => (Value: m.Value, Index: m.Index)).ToList();
        int LineOf(int index) { var line = 1; for (var k = 0; k < index && k < clean.Length; k++) if (clean[k] == '\n') line++; return line; }
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Value == "while" && i + 2 < tokens.Count && (tokens[i + 1].Value is "true" or "1") && tokens[i + 2].Value == "do")
            {
                Scan(tokens, i + 3, out var leaves);
                if (!leaves) found.Add(new("error", LineOf(tokens[i].Index), EndlessMessage));
            }
            else if (tokens[i].Value == "repeat")
            {
                var close = Scan(tokens, i + 1, out var leaves);
                if (close >= 0 && tokens[close].Value == "until" && close + 1 < tokens.Count && (tokens[close + 1].Value is "false" or "nil") && !leaves) found.Add(new("error", LineOf(tokens[i].Index), EndlessMessage));
            }
        }
        return found;
    }

    /// <summary>From the first word inside a block to the word that closes it: where that is (-1 when it never closes) and whether a break or return was seen on the way.</summary>
    private static int Scan(List<(string Value, int Index)> tokens, int start, out bool leaves)
    {
        leaves = false; var depth = 1;
        for (var k = start; k < tokens.Count; k++)
        {
            switch (tokens[k].Value)
            {
                case "function" or "if" or "do" or "repeat": depth++; break;
                case "end" or "until": depth--; if (depth == 0) return k; break;
                case "break" or "return": leaves = true; break;
            }
        }
        return -1;
    }

    private static ScriptDiagnostic? SyntaxError(string source)
    {
        try { new Script(CoreModules.None).LoadString(source, null, "script"); return null; }
        catch (SyntaxErrorException exception)
        {
            var match = Decorated().Match(exception.DecoratedMessage ?? string.Empty);
            return match.Success ? new("error", int.Parse(match.Groups["line"].Value), match.Groups["msg"].Value.Trim()) : new("error", 0, exception.Message);
        }
        catch (ScriptRuntimeException exception) { return new("error", 0, exception.Message); }
    }

    /// <summary>The source without comments and the contents of strings (lines are kept so line numbers still match).</summary>
    public static string Strip(string source)
    {
        var result = new StringBuilder(source.Length); var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                var level = LongBracket(source, i + 2);
                if (level >= 0) { var end = source.IndexOf("]" + new string('=', level) + "]", i + 2, StringComparison.Ordinal); var stop = end < 0 ? source.Length : end + level + 2; for (var k = i; k < stop; k++) if (source[k] == '\n') result.Append('\n'); i = stop; continue; }
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '[' && LongBracket(source, i) is var open and >= 0)
            {
                var end = source.IndexOf("]" + new string('=', open) + "]", i + 2, StringComparison.Ordinal); var stop = end < 0 ? source.Length : end + open + 2;
                result.Append("\"\""); for (var k = i; k < stop; k++) if (source[k] == '\n') result.Append('\n'); i = stop; continue;
            }
            if (c is '"' or '\'')
            {
                result.Append("\"\""); i++;
                while (i < source.Length && source[i] != c && source[i] != '\n') { if (source[i] == '\\' && i + 1 < source.Length) i++; i++; }
                if (i < source.Length && source[i] == c) i++;
                continue;
            }
            result.Append(c); i++;
        }
        return result.ToString();
    }

    private static int LongBracket(string source, int at)
    {
        if (at >= source.Length || source[at] != '[') return -1;
        var level = 0; var i = at + 1; while (i < source.Length && source[i] == '=') { level++; i++; }
        return i < source.Length && source[i] == '[' ? level : -1;
    }

    private static string Suggest(string name, IEnumerable<string> candidates, string prefix = "")
    {
        var best = candidates.Select(c => (Name: c, Distance: Distance(name, c))).Where(x => x.Distance <= 2).OrderBy(x => x.Distance).ThenBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault();
        return best.Name is null ? string.Empty : $" Você quis dizer {prefix}{best.Name}?";
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i; for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++) for (var j = 1; j <= b.Length; j++) d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
