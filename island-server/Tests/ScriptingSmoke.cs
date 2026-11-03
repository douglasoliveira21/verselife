using System.Net;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.IslandServer.Scripts;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Tests;

/// <summary>
/// Runs the island-side script integration against a fake API: scripts and objects are loaded from "island-content",
/// avatar touches and walking become events, and what the script asks for is posted as an authoritative update.
/// </summary>
public partial class ScriptingSmoke : Node
{
    private sealed class FakeApi(Dictionary<string, string> routes, List<string> posts) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.Method != HttpMethod.Get) posts.Add($"{request.Method} {path} {body}");
            var key = routes.Keys.FirstOrDefault(path.EndsWith);
            return key is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(routes[key], Encoding.UTF8, "application/json") };
        }
    }

    public override async void _Ready()
    {
        try
        {
            var owner = Guid.NewGuid(); var door = Guid.NewGuid(); var bell = Guid.NewGuid(); var scriptDoor = Guid.NewGuid(); var scriptBell = Guid.NewGuid();
            string Obj(Guid id, string name, float x, float z) => $"{{\"id\":\"{id}\",\"ownerUserId\":\"{owner}\",\"creatorUserId\":\"{owner}\",\"groupId\":null,\"parentObjectId\":null,\"name\":\"{name}\",\"primitiveType\":\"cube\",\"positionX\":{x},\"positionY\":1,\"positionZ\":{z},\"rotationX\":0,\"rotationY\":0,\"rotationZ\":0,\"scaleX\":2,\"scaleY\":3,\"scaleZ\":0.2,\"materialJson\":\"{{}}\",\"geometryJson\":\"{{}}\",\"physicsJson\":\"{{\\\"collision\\\":true}}\",\"revision\":1}}";
            var door1 = "function on_start() object.say('pronto') end function on_touch(e) object.set_position({x=10,y=1,z=22}) object.set_color('#00ff00') object.say('Ola ' .. e.avatar_name) end";
            var bell1 = "function on_enter(e) object.say('bem-vindo ' .. e.avatar_name) end";
            string Script(Guid id, Guid objectId, string name, string source) => $"{{\"id\":\"{id}\",\"objectId\":\"{objectId}\",\"ownerUserId\":\"{owner}\",\"name\":\"{name}\",\"source\":{JsonSerializer.Serialize(source)},\"version\":1,\"instructionBudget\":10000}}";
            var accepted = $"{{\"operation\":\"update\",\"objectState\":{Obj(door, "Porta", 10, 22).Replace("\"revision\":1", "\"revision\":2")},\"objectId\":\"{door}\",\"revision\":2}}";
            var posts = new List<string>();
            var routes = new Dictionary<string, string>
            {
                ["island-content"] = $"{{\"islandId\":\"{Guid.NewGuid()}\",\"objects\":[{Obj(door, "Porta", 10, 20)},{Obj(bell, "Campainha", -30, 5)}],\"scripts\":[{Script(scriptDoor, door, "porta", door1)},{Script(scriptBell, bell, "campainha", bell1)}],\"state\":{{}}}}",
                ["build-operations"] = accepted,
                ["script-state"] = "{\"saved\":0}"
            };
            using var http = new System.Net.Http.HttpClient(new FakeApi(routes, posts));
            var config = new ServerConfiguration(Guid.NewGuid(), Guid.NewGuid(), "http://api.test", new string('k', 40), "localhost", 7777, 7070, 20, 20, 100f, 4);
            var said = new List<string>(); var broadcasts = new List<string>(); var told = new List<string>();
            var scripting = new IslandScripting(http, config, broadcasts.Add, (name, _, text) => said.Add($"{name}: {text}"), (_, text) => told.Add(text), (_, _) => { });

            await scripting.RefreshAsync(CancellationToken.None);
            Require(scripting.Scripts.Count == 2 && scripting.Scripts.All(s => !s.Quarantined), "scripts did not load");
            Require(scripting.GetObject(door)?.Position.Z == 20, "objects did not load");

            var near = new AvatarPresence(2, Guid.NewGuid(), owner, "Ana", 10, 1, 18);
            for (var i = 0; i < 5; i++) { scripting.Update([near]); await Task.Delay(30); }
            Require(said.Contains("Porta: pronto"), $"on_start did not run: [{string.Join("|", said)}]");

            scripting.Touch(near, door);
            for (var i = 0; i < 5; i++) { scripting.Update([near]); await Task.Delay(30); }
            await Task.Delay(300); scripting.Update([near]);
            Require(said.Contains("Porta: Ola Ana"), $"on_touch did not run: [{string.Join("|", said)}]");
            var update = posts.FirstOrDefault(p => p.Contains("build-operations"));
            Require(update is not null, "the script change was not posted to the API");
            Require(update!.Contains("\"positionZ\":22") && update.Contains("#00ff00") && update.Contains("\"expectedRevision\":1") && update.Contains(owner.ToString()), $"unexpected update: {update}");
            Require(broadcasts.Count == 1 && scripting.GetObject(door)?.Position.Z == 22, "the accepted change was not applied and broadcast");

            var posted = posts.Count;
            scripting.Touch(new AvatarPresence(3, Guid.NewGuid(), owner, "Longe", 80, 1, 80), door);
            for (var i = 0; i < 3; i++) { scripting.Update([near]); await Task.Delay(30); }
            Require(posts.Count == posted && said.Count(s => s.StartsWith("Porta: Ola")) == 1, "a touch from far away must be ignored");

            var visitor = new AvatarPresence(4, Guid.NewGuid(), owner, "Bia", -30, 1, 5);
            for (var i = 0; i < 4; i++) { scripting.Update([near, visitor]); await Task.Delay(300); }
            Require(said.Contains("Campainha: bem-vindo Bia"), $"on_enter did not run: [{string.Join("|", said)}]");
            var enters = said.Count(s => s.StartsWith("Campainha:"));
            for (var i = 0; i < 4; i++) { scripting.Update([near, visitor]); await Task.Delay(300); }
            Require(said.Count(s => s.StartsWith("Campainha:")) == enters, "standing inside must not repeat the enter event");

            GD.Print("event=scripting_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=scripting_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
