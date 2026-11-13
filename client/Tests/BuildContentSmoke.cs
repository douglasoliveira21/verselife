using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 8: the CONTEÚDO tab keeps items and scripts of the object in folders, creates and validates scripts, resets them, and respects who may modify the object.</summary>
public partial class BuildContentSmoke : Node3D
{
    private static string StateJson(Guid id, int revision, string content = "[]", int ownerRights = 15, string operation = "update") =>
        JsonSerializer.Serialize(new { operation, objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name = "Caixa", description = "", primitiveType = "cube", positionX = 0, positionY = .5f, positionZ = 0, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = "{}", geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = "{}", contentJson = content, ownerRights, revision, cost = 2 } });

    private static JsonElement Last(List<string> commands) => JsonDocument.Parse(commands[^1]).RootElement.Clone();
    private static JsonArray ContentOf(List<string> commands) => (JsonNode.Parse(Last(commands).GetProperty("object").GetProperty("contentJson").GetString()!) as JsonArray)!;

    public override async void _Ready()
    {
        try
        {
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.StartsWith("{\"operation\":\"lock\"") && !json.StartsWith("{\"operation\":\"unlock\"") && !json.StartsWith("{\"operation\":\"live\"")) commands.Add(json); return true; };
            await Frames(3); editor.SetActive(true);
            var tab = editor.Window.Content; var id = Guid.NewGuid();

            // the server, in memory: scripts of the object
            var scripts = new List<(Guid Id, string Name, string Source, int Version)>(); var calls = new List<string>();
            editor.ContentManager.UseTransportForTesting((method, path, body) =>
            {
                calls.Add(method + " " + path);
                if (method == "GET" && path.EndsWith("/scripts")) return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(scripts.Select(s => new { id = s.Id, name = s.Name, version = s.Version, status = "active" }))));
                if (method == "GET" && path.EndsWith("/api/v1/scripting/templates")) return Task.FromResult<(long, string)>((200, "[]"));
                if (method == "GET" && (path.EndsWith("/runtime") || path.EndsWith("/api/v1/scripting/reference"))) return Task.FromResult<(long, string)>((404, "{}"));   // the island has not reported yet; no reference in this pretend server
                if (method == "POST" && path.EndsWith("/scripts")) { var made = (Guid.NewGuid(), JsonDocument.Parse(body!).RootElement.GetProperty("name").GetString()!, "function on_touch(event) end", 1); scripts.Add(made); return Task.FromResult<(long, string)>((201, JsonSerializer.Serialize(new { id = made.Item1 }))); }
                if (method == "GET" && path.EndsWith("/executions")) return Task.FromResult<(long, string)>((200, "[]"));
                if (method == "GET") { var s = scripts.First(x => path.EndsWith(x.Id.ToString())); return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { id = s.Id, name = s.Name, source = s.Source, version = s.Version, status = "active" }))); }
                if (method == "PUT") { var i = scripts.FindIndex(x => path.EndsWith(x.Id.ToString())); var doc = JsonDocument.Parse(body!).RootElement; scripts[i] = (scripts[i].Id, doc.GetProperty("name").GetString()!, doc.GetProperty("source").GetString()!, scripts[i].Version + 1); return Task.FromResult<(long, string)>((200, "{}")); }
                if (method == "DELETE") { scripts.RemoveAll(x => path.EndsWith(x.Id.ToString())); return Task.FromResult<(long, string)>((204, "")); }
                if (path.EndsWith("/validate")) { var bad = JsonDocument.Parse(body!).RootElement.GetProperty("source").GetString()!.Contains("+++"); return Task.FromResult<(long, string)>((200, bad ? "{\"valid\":false,\"errors\":[{\"line\":2,\"message\":\"unexpected symbol\"}]}" : "{\"valid\":true,\"errors\":[]}")); }
                if (path.EndsWith("/reset")) { for (var i = 0; i < scripts.Count; i++) scripts[i] = (scripts[i].Id, scripts[i].Name, scripts[i].Source, scripts[i].Version + 1); return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { reset = scripts.Count }))); }
                return Task.FromResult<(long, string)>((404, "{}"));
            });

            editor.Synchronizer.OnOperation(StateJson(id, 1, operation: "create")); editor.Selection.Set([id]); editor.Window.SelectTabForTesting(4); await Frames(3);
            Require(tab.CountForTesting == "0 itens", "an empty object has no items");

            // --- NOVO SCRIPT ----------------------------------------------------------------------------------------------
            await tab.NewScriptForTesting(); await Frames(2);
            Require(scripts.Count == 1, "NOVO SCRIPT creates a script on the server");
            Require(tab.FoldersForTesting.SequenceEqual(["Scripts"]) && tab.ItemsOfForTesting("Scripts").Count == 1, "the script is listed in the Scripts folder");
            Require(tab.EditorForTesting.IsOpen && tab.EditorForTesting.ScriptIdForTesting == scripts[0].Id, "and opens in the editor");
            tab.EditorForTesting.SourceForTesting = "function on_touch(event)\n  +++\nend";
            Require(!await tab.EditorForTesting.ValidateAsync() && tab.EditorForTesting.InfoForTesting.Contains("linha 2"), "Validar shows the line of the error");
            tab.EditorForTesting.SourceForTesting = "function on_touch(event) end";
            Require(await tab.EditorForTesting.ValidateAsync(), "a valid script passes");
            tab.EditorForTesting.NameForTesting = "Porta"; await tab.EditorForTesting.SaveAsync(); await tab.RefreshForTesting();
            Require(scripts[0].Name == "Porta" && scripts[0].Version == 2, "saving writes name and source");

            // --- items in folders -----------------------------------------------------------------------------------------
            var sound = Guid.NewGuid(); var picture = Guid.NewGuid();
            tab.FillPickerForTesting("{\"items\":[{\"assetId\":\"" + sound + "\",\"name\":\"Campainha\",\"category\":\"sounds\"},{\"assetId\":\"" + picture + "\",\"name\":\"Foto\",\"category\":\"textures\"},{\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Roupa\",\"category\":\"hud\"}]}");
            Require(tab.PickerCountForTesting == 2, "the picker only offers what an object can carry");
            commands.Clear(); tab.DropForTesting(sound, "Campainha", "sounds");
            Require(ContentOf(commands).Count == 1 && ContentOf(commands)[0]!["assetId"]!.GetValue<string>() == sound.ToString(), "inserting sends a reference to the item (only the id, never the data)");
            editor.Synchronizer.OnOperation(StateJson(id, 2, Last(commands).GetProperty("object").GetProperty("contentJson").GetString()!)); await Frames(2);
            Require(tab.ItemsOfForTesting("Sons").Count == 1 && tab.CountForTesting == "2 itens", "the sound goes into the Sons folder");
            tab.DropForTesting(sound, "Campainha", "sounds");
            Require(commands.Count == 1 || !ContentOf(commands).Skip(1).Any(), "the same item is not added twice");

            tab.SelectForTesting(tab.Key(false, sound)); commands.Clear(); tab.RenameForTesting("Sino");
            Require(ContentOf(commands)[0]!["name"]!.GetValue<string>() == "Sino", "renaming changes the stored name");
            commands.Clear(); tab.MoveForTesting("Notecards");
            Require(ContentOf(commands)[0]!["folder"]!.GetValue<string>() == "Notecards", "moving a item keeps the folder in the entry");
            editor.Synchronizer.OnOperation(StateJson(id, 3, Last(commands).GetProperty("object").GetProperty("contentJson").GetString()!)); await Frames(2);
            Require(tab.ItemsOfForTesting("Notecards").Count == 1 && tab.ItemsOfForTesting("Sons").Count == 0, "and the tree shows it there");
            commands.Clear(); tab.RemoveForTesting();
            Require(ContentOf(commands).Count == 0, "Remover takes it out of the object");
            editor.Synchronizer.OnOperation(StateJson(id, 4)); await Frames(2);

            // --- reset and refresh --------------------------------------------------------------------------------------
            tab.SelectForTesting(tab.Key(true, scripts[0].Id)); var before = scripts[0].Version; var source = scripts[0].Source;
            await tab.ResetForTesting();
            Require(scripts[0].Version == before + 1 && scripts[0].Source == source && calls.Any(c => c.StartsWith("POST") && c.EndsWith("/scripts/reset")), "RESETAR SCRIPTS restarts them and keeps the source");
            tab.EditorForTesting.SourceForTesting = "-- mudança local"; await tab.RefreshForTesting();
            Require(tab.EditorForTesting.SourceForTesting == "-- mudança local" && tab.ItemsOfForTesting("Scripts").Count == 1, "ATUALIZAR neither duplicates nor discards what is open in the editor");
            await tab.RemoveScriptForTesting(); await Frames(2);
            Require(scripts.Count == 0 && !tab.EditorForTesting.IsOpen, "removing the script deletes it and closes its editor");

            // --- permissions ---------------------------------------------------------------------------------------------
            var locked = Guid.NewGuid(); editor.Synchronizer.OnOperation(StateJson(locked, 1, ownerRights: 5, operation: "create")); editor.Selection.Set([locked]); await Frames(2);
            commands.Clear(); tab.DropForTesting(sound, "Campainha", "sounds");
            Require(commands.Count == 0, "an object that cannot be modified accepts no new item");
            Require(tab.InfoForTesting.Contains("não pode ser modificado"), "and says why");
            tab.ShowPermissionsForTesting(); Require(tab.InfoForTesting.Contains("leitura"), "PERMISSÕES explains the object is read-only");
            editor.Selection.Set([id, locked]); await Frames(2);
            Require(tab.CountForTesting == "0 itens" && tab.InfoForTesting.Contains("um só objeto"), "with two objects selected there is no content to show");

            GD.Print("event=build_content_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_content_smoke result=failed error={exception.Message} at {exception.StackTrace?.Split((char)10).FirstOrDefault(l => l.Contains("LifeVerse"))}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
