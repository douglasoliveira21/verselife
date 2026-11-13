using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The Lua editor against a pretend server: what the island reported (state, console, performance, permissions), Validar with errors and warnings that take the cursor to their line, completion from the API, the documentation, and Reiniciar, Parar and Pausar.</summary>
public partial class ScriptEditorSmoke : Control
{
    public override async void _Ready()
    {
        try
        {
            var island = Guid.NewGuid(); var obj = Guid.NewGuid(); var script = Guid.NewGuid(); var calls = new List<string>(); var paused = false; var reported = true;
            var reference = JsonSerializer.Serialize(new
            {
                functions = new[] { new { category = "Chat", @namespace = "chat", name = "say", signature = "chat.say(mensagem)", description = "Fala no chat local.", returns = "nada", permission = "—", limits = "5 a cada 10 s", errors = "mensagem vazia", example = "chat.say('Olá!')" },
                    new { category = "Chat", @namespace = "chat", name = "shout", signature = "chat.shout(mensagem)", description = "Grita.", returns = "nada", permission = "—", limits = "100 m", errors = "—", example = "chat.shout('Alarme')" },
                    new { category = "Temporizadores", @namespace = "timer", name = "after", signature = "timer.after(segundos, funcao)", description = "Chama depois do tempo.", returns = "número", permission = "—", limits = "16", errors = "—", example = "timer.after(5, function() end)" } },
                events = new[] { new { name = "on_touch", when = "Um avatar clica.", fields = "avatar_id", example = "function on_touch(event) end" } }
            });
            Task<(long, string)> Server(string method, string path, string? body)
            {
                calls.Add($"{method} {path}");
                if (path.EndsWith("/api/v1/scripting/reference", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((200, reference));
                if (path.EndsWith("/api/v1/scripting/templates", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((200, "[]"));
                if (path.EndsWith("/runtime", StringComparison.Ordinal))
                    return Task.FromResult<(long, string)>(reported ? (200, JsonSerializer.Serialize(new
                    {
                        scriptId = script, status = paused ? "paused" : "active", version = 3,
                        runtime = new { state = "Waiting", stateText = "aguardando evento", events = 12, eventsDropped = 1, errors = 1, violations = 0, suspensions = 0, apiCalls = 40, instructions = 5200, allocatedBytes = 20480, messagesSent = 3, cpuMilliseconds = 4.5, worstEventMilliseconds = 1.2, activeTimers = 1, stateKeys = 2, lastError = "attempt to index nil", lastErrorLine = 7, suspendedReason = "" },
                        logs = new[] { new { level = "info", eventName = "touch", text = "tocado por Ana", stackTrace = "", milliseconds = 0.2, createdAt = "2026-11-13T12:00:01Z" }, new { level = "error", eventName = "touch", text = "attempt to index nil", stackTrace = "porta:7 in touch", milliseconds = 0.4, createdAt = "2026-11-13T12:00:02Z" } },
                        grants = new[] { new { subjectId = Guid.Empty, permission = "CONTROL_PHYSICS", grantedAt = "2026-11-13T11:00:00Z" }, new { subjectId = Guid.NewGuid(), permission = "CONTROL_ANIMATION", grantedAt = "2026-11-13T11:30:00Z" } }
                    })) : (200, JsonSerializer.Serialize(new { scriptId = script, status = "active", version = 1, runtime = (object?)null, logs = Array.Empty<object>(), grants = Array.Empty<object>() })));
                if (path.EndsWith("/executions", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((200, "[]"));
                if (path.EndsWith("/validate", StringComparison.Ordinal))
                {
                    var source = JsonDocument.Parse(body!).RootElement.GetProperty("source").GetString()!;
                    if (source.Contains("+++")) return Task.FromResult<(long, string)>((200, "{\"valid\":false,\"errors\":[{\"line\":3,\"message\":\"unexpected symbol\"}],\"warnings\":[],\"handlers\":[]}"));
                    if (source.Contains("sya")) return Task.FromResult<(long, string)>((200, "{\"valid\":true,\"errors\":[],\"warnings\":[{\"line\":2,\"message\":\"chat.sya não existe na API. Você quis dizer chat.say?\"}],\"handlers\":[\"on_touch\"]}"));
                    return Task.FromResult<(long, string)>((200, "{\"valid\":true,\"errors\":[],\"warnings\":[],\"handlers\":[\"on_touch\"]}"));
                }
                if (path.EndsWith("/pause", StringComparison.Ordinal)) { paused = true; return Task.FromResult<(long, string)>((200, "{\"status\":\"paused\"}")); }
                if (path.EndsWith("/resume", StringComparison.Ordinal)) { paused = false; return Task.FromResult<(long, string)>((200, "{\"status\":\"active\"}")); }
                if (method == "PUT" && path.Contains("/permissions/", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((200, "{}"));
                if (path.EndsWith("/stop", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((200, "{}"));
                if (path.EndsWith("/reset", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((200, "{\"reset\":1}"));
                if (method == "GET" && path.EndsWith(script.ToString(), StringComparison.Ordinal)) return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { id = script, name = "porta", source = "function on_touch(event)\n  chat.say('oi')\nend\n", version = 3, status = "active" })));
                return Task.FromResult<(long, string)>((404, "{}"));
            }

            var editor = new ScriptEditorWindow { Name = "Editor" }; AddChild(editor); editor.Initialize(new ScriptApi(Server, () => island)); await Frames(3);
            var screen = GetViewport().GetVisibleRect().Size;
            await editor.OpenAsync(obj, script); await Frames(4);

            // ---- what the island reported ----
            Require(editor.IsOpen && editor.StateForTesting == "● aguardando evento", $"the header shows the state of the script (got {editor.StateForTesting})");
            Require(editor.TabTitlesForTesting.SequenceEqual(["Console", "Problemas", "Desempenho", "Permissões"]), "four tabs under the code");
            Require(editor.ConsoleForTesting.Contains("12:00:01") && editor.ConsoleForTesting.Contains("info") && editor.ConsoleForTesting.Contains("touch: tocado por Ana") && editor.ConsoleForTesting.Contains("ERRO") && editor.ConsoleForTesting.Contains("porta:7 in touch"), $"the console shows what the script wrote and its errors with where they happened (got {editor.ConsoleForTesting})");
            Require(editor.PerformanceForTesting.Contains("Eventos tratados: 12") && editor.PerformanceForTesting.Contains("CPU: 4,5 ms") && editor.PerformanceForTesting.Contains("Último erro (linha 7): attempt to index nil") && editor.PerformanceForTesting.Contains("temporizadores ativos: 1"), $"performance (got {editor.PerformanceForTesting})");
            Require(editor.PermissionsForTesting.Contains("CONTROL_ANIMATION — dada pelo avatar") && !editor.PermissionsForTesting.Contains("CONTROL_PHYSICS") && editor.OwnerPermissionsForTesting["CONTROL_PHYSICS"] && !editor.OwnerPermissionsForTesting["HTTP_REQUESTS"], $"the permissions of the owner are boxes, those of avatars a list (got {editor.PermissionsForTesting})");
            calls.Clear(); editor.ToggleOwnerPermissionForTesting("HTTP_REQUESTS"); await Frames(6); Require(calls.Contains($"PUT /api/v1/worlds/{island}/objects/{obj}/scripts/{script}/permissions/HTTP_REQUESTS") && editor.InfoForTesting.Contains("HTTP_REQUESTS concedida"), $"a box gives the permission (calls {string.Join(",", calls)}, info {editor.InfoForTesting})");
            Require(editor.PauseTextForTesting == "Pausar" && new[] { "Salvar (Ctrl+S)", "Validar", "Reiniciar", "Parar", "Pausar", "Atualizar", "Documentação" }.All(editor.HasButtonForTesting), "every button of the toolbar is there");
            var size = editor.SizeForTesting; Require(size.X <= screen.X && size.Y <= screen.Y, $"the window fits the screen (size {size}, screen {screen})");

            // ---- Validar ----
            editor.SourceForTesting = "function on_touch(event)\n  chat.sya('oi')\nend"; Require(await editor.ValidateAsync(), "warnings do not make a script invalid");
            Require(editor.TabForTesting.StartsWith("Problemas (1)", StringComparison.Ordinal) && editor.ProblemsForTesting[0].Contains("Aviso (linha 2)") && editor.InfoForTesting.Contains("1 aviso") && editor.InfoForTesting.Contains("on_touch"), $"a warning is listed with its line (got {string.Join("|", editor.ProblemsForTesting)} / {editor.InfoForTesting})");
            editor.SourceForTesting = "function on_touch(event)\n  local a = 1\n  +++\nend"; Require(!await editor.ValidateAsync(), "an error makes it invalid");
            Require(editor.InfoForTesting == "Erro na linha 3: unexpected symbol" && editor.CaretLineForTesting == 3 && editor.ProblemsForTesting[0].StartsWith("Erro (linha 3)", StringComparison.Ordinal), $"the error is shown and the cursor goes to its line (line {editor.CaretLineForTesting}, info {editor.InfoForTesting})");
            editor.SourceForTesting = "function on_touch(event)\n  chat.say('ok')\nend"; Require(await editor.ValidateAsync() && editor.ProblemsForTesting.Count == 0 && editor.TabForTesting == "Console" || editor.ProblemsForTesting.Count == 0, "a clean script lists no problems");

            // ---- completion, from the API ----
            editor.SourceForTesting = "local velocidade = 3\n";
            var members = editor.CompletionFor("  chat."); Require(members.Select(m => m.Shown).SequenceEqual(["say", "shout"]) && members[0].Insert == "say(", $"after chat. only the functions of chat are offered (got {string.Join(",", members.Select(m => m.Shown))})");
            Require(editor.CompletionFor("chat.sh").Select(m => m.Shown).SequenceEqual(["shout"]), "typing narrows the list"); Require(editor.CompletionFor("timer.").Single().Insert == "after(", "timer has after");
            var words = editor.CompletionFor("ti").Select(m => m.Shown).ToList(); Require(words.Contains("timer"), "namespaces are offered"); Require(editor.CompletionFor("on_to").Any(m => m.Shown == "on_touch" && m.Insert == "on_touch()"), "events are offered");
            Require(editor.CompletionFor("vel").Any(m => m.Shown == "velocidade"), "names the script already declared are offered"); Require(editor.CompletionFor("fu").Any(m => m.Shown == "function"), "and keywords"); Require(editor.CompletionFor("").Count == 0 && editor.CompletionFor("nada.").Count == 0, "nothing for an unknown namespace");

            // ---- documentation ----
            editor.PressForTesting("Documentação"); await Frames(6); var docs = editor.DocsForTesting!; Require(docs.IsOpen && docs.EntriesForTesting.SequenceEqual(["chat.say(mensagem)", "chat.shout(mensagem)", "timer.after(segundos, funcao)", "on_touch"]), $"the documentation lists functions and events (got {string.Join("|", docs.EntriesForTesting)})");
            Require(docs.CategoriesForTesting.SequenceEqual(["Todas as categorias", "Chat", "Temporizadores", "Eventos"]), "categories"); docs.CategoryForTesting("Chat"); Require(docs.EntriesForTesting.Count == 2, "the category filters"); docs.CategoryForTesting("Todas as categorias");
            docs.SearchForTesting("grita"); Require(docs.EntriesForTesting.SequenceEqual(["chat.shout(mensagem)"]), "the search looks in the descriptions too"); docs.SelectForTesting(0);
            Require(docs.DetailForTesting.Contains("chat.shout(mensagem)") && docs.DetailForTesting.Contains("Retorna: nada") && docs.DetailForTesting.Contains("Limites: 100 m") && docs.DetailForTesting.Contains("Exemplo"), $"the detail has signature, return, limits and an example (got {docs.DetailForTesting})");
            editor.SourceForTesting = ""; docs.PressInsertForTesting(); Require(editor.SourceForTesting.Contains("chat.shout('Alarme')"), "the example goes into the script");
            docs.SearchForTesting("zzz"); Require(docs.EntriesForTesting.Count == 0 && docs.DetailForTesting.Contains("Nada encontrado"), "a search with no result says so"); var ds = docs.SizeForTesting; Require(ds.X <= screen.X && ds.Y <= screen.Y, "the documentation fits the screen");

            // ---- control ----
            calls.Clear(); editor.PressForTesting("Pausar"); await Frames(6); Require(calls.Any(c => c.EndsWith("/pause", StringComparison.Ordinal)) && editor.PauseTextForTesting == "Continuar" && editor.InfoForTesting == "Script pausado.", "Pausar pauses and the button becomes Continuar");
            editor.PressForTesting("Continuar"); await Frames(6); Require(calls.Any(c => c.EndsWith("/resume", StringComparison.Ordinal)) && editor.PauseTextForTesting == "Pausar", "Continuar resumes");
            editor.PressForTesting("Parar"); await Frames(6); Require(calls.Any(c => c.EndsWith("/stop", StringComparison.Ordinal)) && editor.InfoForTesting.Contains("parado"), "Parar stops");
            editor.PressForTesting("Reiniciar"); await Frames(6); Require(calls.Any(c => c.EndsWith("/reset", StringComparison.Ordinal)) && editor.InfoForTesting.Contains("reiniciado"), "Reiniciar restarts");

            // ---- the island has not reported yet ----
            reported = false; await editor.RefreshConsoleAsync(); Require(editor.StateForTesting == "● aguardando a ilha" && editor.PerformanceForTesting.Contains("ainda não informou") && editor.ConsoleForTesting.Contains("sem execuções"), $"before the first report the editor says so (got {editor.StateForTesting} / {editor.ConsoleForTesting})");

            GD.Print("event=script_editor_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=script_editor_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
