using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace LifeVerse.Api.Modules.Scripting;

/// <summary>A syntax error in a script: the line where the parser stopped and what it said.</summary>
public sealed record LuaSyntaxError(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"linha {Line}: {Message}" : Message;
}

/// <summary>
/// Checks that a script is valid Lua without running any of it (the code is only compiled, never executed). The same parser the island server runs
/// the script with, so what is accepted here loads there.
/// </summary>
public static partial class LuaSyntax
{
    [GeneratedRegex(@"^[^:]*:\((?<line>\d+),\d+(?:-\d+)?\):\s*(?<msg>.*)$", RegexOptions.Singleline)]
    private static partial Regex Decorated();

    /// <summary>Null when the source is valid Lua; otherwise the first error with its line.</summary>
    public static LuaSyntaxError? Check(string source)
    {
        try
        {
            var script = new Script(CoreModules.None);
            script.LoadString(source, null, "script");
            return null;
        }
        catch (SyntaxErrorException exception)
        {
            var match = Decorated().Match(exception.DecoratedMessage ?? string.Empty);
            if (match.Success) return new LuaSyntaxError(int.Parse(match.Groups["line"].Value), match.Groups["msg"].Value.Trim());
            return new LuaSyntaxError(0, exception.Message);
        }
        catch (ScriptRuntimeException exception) { return new LuaSyntaxError(0, exception.Message); }
    }
}
