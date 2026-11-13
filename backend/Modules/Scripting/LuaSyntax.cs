namespace LifeVerse.Api.Modules.Scripting;

/// <summary>A syntax error in a script: the line where the parser stopped and what it said.</summary>
public sealed record LuaSyntaxError(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"linha {Line}: {Message}" : Message;
}

/// <summary>Checks that a script is valid Lua (and uses nothing the sandbox lacks) without running any of it. The check itself lives in <see cref="LifeVerse.Scripting.ScriptCompiler"/>, the same one the editor and the island server use.</summary>
public static class LuaSyntax
{
    /// <summary>Null when the source is valid; otherwise the first error with its line.</summary>
    public static LuaSyntaxError? Check(string source) => LifeVerse.Scripting.ScriptCompiler.Compile(source).Diagnostics.FirstOrDefault(d => d.Severity == "error") is { } error ? new LuaSyntaxError(error.Line, error.Message) : null;
}
