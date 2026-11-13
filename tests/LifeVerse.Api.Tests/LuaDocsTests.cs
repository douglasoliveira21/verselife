using System.Text;
using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>
/// The manual of the Lua API (<c>docs/LUA_API.md</c>) is written from the catalog and compared with the file on every run, so it cannot drift from the code. After changing the API, run the tests once with
/// <c>LIFEVERSE_UPDATE_DOCS=1</c> to rewrite it.
/// </summary>
public sealed class LuaDocsTests
{
    public static string Build()
    {
        var text = new StringBuilder();
        text.AppendLine("# API Lua do LifeVerse — referência").AppendLine();
        text.AppendLine("> Gerado automaticamente a partir do catálogo do código (`scripting/ScriptCatalog.cs`); não edite à mão. Cada função listada aqui é verificada por teste: ela existe no ambiente dos scripts.").AppendLine();
        text.AppendLine("Os eventos chamam funções globais com o nome do evento e recebem **uma tabela `event`** com os campos de cada evento (não parâmetros soltos). O que não aparece aqui ainda não existe: veja *Pendências* em `docs/LUA_SCRIPTING.md`.").AppendLine();
        text.AppendLine("## Eventos").AppendLine();
        text.AppendLine("| Evento | Quando | Campos de `event` |").AppendLine("|---|---|---|");
        foreach (var e in ScriptCatalog.Events) text.AppendLine($"| `{e.Name}` | {e.When} | {e.Fields} |");
        text.AppendLine();
        foreach (var category in ScriptCatalog.Functions.Select(f => f.Category).Distinct())
        {
            text.AppendLine($"## {category}").AppendLine();
            foreach (var f in ScriptCatalog.Functions.Where(f => f.Category == category))
            {
                text.AppendLine($"### `{f.Signature}`").AppendLine();
                text.AppendLine(f.Description).AppendLine();
                text.AppendLine($"- **Retorna:** {f.Returns}").AppendLine($"- **Permissão:** {f.Permission}").AppendLine($"- **Limites:** {f.Limits}").AppendLine($"- **Erros possíveis:** {f.Errors}").AppendLine();
                text.AppendLine("```lua").AppendLine(f.Example).AppendLine("```").AppendLine();
            }
        }
        text.AppendLine("## Permissões").AppendLine();
        text.AppendLine("| Permissão | O que permite | Pedida ao avatar |").AppendLine("|---|---|---|");
        foreach (var permission in ScriptPermission.All) text.AppendLine($"| `{permission}` | O script pode {ScriptPermission.Describe(permission)} | {(ScriptPermission.OfAvatars.Contains(permission) ? "sim (`permissions.request`)" : "não")} |");
        text.AppendLine().AppendLine("## Estados de um script").AppendLine();
        text.AppendLine("| Estado | Significa |").AppendLine("|---|---|");
        foreach (var state in Enum.GetValues<ScriptLifecycle>()) text.AppendLine($"| `{state}` | {ScriptLifecycleRules.Describe(state)} |");
        return text.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void TheManualMatchesTheCatalog()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/LUA_API.md")); var built = Build();
        if (Environment.GetEnvironmentVariable("LIFEVERSE_UPDATE_DOCS") == "1") { File.WriteAllText(path, built, new UTF8Encoding(false)); return; }
        Assert.True(File.Exists(path), "docs/LUA_API.md is missing: run the tests once with LIFEVERSE_UPDATE_DOCS=1");
        Assert.True(File.ReadAllText(path).Replace("\r\n", "\n") == built, "docs/LUA_API.md is out of date: run the tests once with LIFEVERSE_UPDATE_DOCS=1");
    }
}
