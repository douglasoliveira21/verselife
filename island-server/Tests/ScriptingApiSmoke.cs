using System.Net;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.IslandServer.Scripts;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Tests;

/// <summary>
/// The second layer of the Lua API on the island, against a pretend API: speaking at a range, a linkset (changing another part of it), a dialog and a permission request shown to the right avatar (and answers from the wrong one
/// refused), what is reported to the API (state, the console, the permissions), what is loaded back (paused scripts, saved permissions) and the shutdown event.
/// </summary>
public partial class ScriptingApiSmoke : Node
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
            var owner = Guid.NewGuid(); var root = Guid.NewGuid(); var child = Guid.NewGuid(); var scriptRoot = Guid.NewGuid(); var scriptChild = Guid.NewGuid(); var scriptPaused = Guid.NewGuid(); var island = Guid.NewGuid(); var clockObj = Guid.NewGuid(); var scriptClock = Guid.NewGuid();
            string Obj(Guid id, string name, float x, Guid? parent = null) => JsonSerializer.Serialize(new { id, ownerUserId = owner, creatorUserId = owner, groupId = (Guid?)null, parentObjectId = parent, name, primitiveType = "cube", positionX = x, positionY = 1, positionZ = 20, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 2, scaleY = 3, scaleZ = .2, materialJson = "{}", geometryJson = "{}", physicsJson = "{}", contentJson = "[]", revision = 1 });
            string Script(Guid id, Guid objectId, string name, string source, bool paused = false) => JsonSerializer.Serialize(new { id, objectId, ownerUserId = owner, name, source, version = 1, instructionBudget = 10000, paused });
            var rootSource = "function on_touch(e) chat.whisper('psiu') chat.shout('ei') object.setLinkPosition(2, 5, 1, 20) storage.set('toques', (storage.get('toques') or 0) + 1) print('tocado por ' .. e.avatar.name) "
                + "dialog.show(e.avatar_id, {title = 'Porta', message = 'Abrir?', buttons = {'Sim', 'Não'}}) permissions.request(e.avatar_id, 'CONTROL_ANIMATION') end "
                + "function on_dialog_response(e) chat.say('resposta ' .. e.response) end function on_permission_granted(e) chat.say('permitido ' .. e.permission) end function on_region_shutdown() chat.say('desligando') end function on_link_message(e) chat.say('link ' .. e.message) end";
            var childSource = "function on_link_message(e) chat.say('filho ouviu ' .. e.message) end function on_collision_enter(e) chat.say('bateu ' .. e.entity.type .. ' ' .. e.entity.name) end function on_land_collision(e) chat.say('pousou ' .. e.speed) end function on_collision_exit(e) chat.say('saiu ' .. e.entity.name) end function on_touch_end(e) chat.say(\"soltou \" .. e.avatar.name) end function on_double_click(e) chat.say(\"duplo \" .. e.avatar.name) end function on_velocity_changed(e) if e.speed < 10 then object.setName(\"corre \" .. math.floor(e.speed)) end end function on_physics_update(e) if e.speed >= 10 then object.setPosition(e.position.x, 7.5, e.position.z) end end function on_trigger_enter(e) object.setName(\"dentro \" .. e.avatar.name) end function on_trigger_exit(e) object.setName(\"fora \" .. e.avatar.name) end";
            var ana = Guid.NewGuid(); var bruno = Guid.NewGuid();
            var accepted = JsonSerializer.Serialize(new { operation = "update", objectId = child, revision = 2 });
            var posts = new List<string>();
            var routes = new Dictionary<string, string>
            {
                ["island-content"] = JsonSerializer.Serialize(new { islandId = island, islandName = "Praça Central", objects = new[] { JsonDocument.Parse(Obj(root, "Casa", 10)).RootElement, JsonDocument.Parse(Obj(child, "Janela", 11, root)).RootElement, JsonDocument.Parse(Obj(clockObj, "Relógio", 10)).RootElement },
                    scripts = new[] { JsonDocument.Parse(Script(scriptRoot, root, "casa", rootSource)).RootElement, JsonDocument.Parse(Script(scriptChild, child, "janela", childSource)).RootElement, JsonDocument.Parse(Script(scriptPaused, root, "pausado", "function on_touch() chat.say('NAO') end", paused: true)).RootElement, JsonDocument.Parse(Script(scriptClock, clockObj, "relogio", "function on_touch(e) id = http.request({url = 'https://api.exemplo.com/clima?chave=segredo', method = 'GET'}) physics.setDynamic(true) physics.applyImpulse(0, 5, 0) economy.requestPayment(e.avatar_id, 100, 'Entrada') permissions.request(e.avatar_id, 'SHOW_HUD') permissions.request(e.avatar_id, 'CONTROL_ANIMATION') particles.create({preset = 'fire', rate = 10}) light.set({radius = 6}) end function on_permission_granted(e) if e.permission == 'SHOW_HUD' then hud.show(e.avatar_id) hud.setTitle(e.avatar_id, 'Loja') hud.setButton(e.avatar_id, 'ativar', 'Ativar') else avatar.playAnimation(e.avatar_id, 'wave', 3) end end function on_hud_click(e) chat.say('hud clique ' .. e.element) end function on_animation_finished(e) chat.say('animacao ' .. e.name .. ' ' .. e.reason) end function on_payment(e) chat.say('pago ' .. e.amount .. ' por ' .. e.avatar.name) end function on_payment_failed(e) chat.say('falhou ' .. e.reason) end function on_item_added(e) chat.say('item ' .. e.item.name .. ' ' .. e.item.type) end function on_http_response(e) chat.say('clima ' .. e.status .. ' ' .. e.body .. ' ' .. tostring(e.request_id == id)) end")).RootElement },
                    state = new Dictionary<string, object>(), grants = new[] { new { scriptId = scriptRoot, subjectId = bruno, permission = "SEND_MESSAGES" }, new { scriptId = scriptClock, subjectId = Guid.Empty, permission = "HTTP_REQUESTS" }, new { scriptId = scriptClock, subjectId = Guid.Empty, permission = "CONTROL_PHYSICS" } } }),
                ["build-operations"] = accepted, ["script-payment"] = "{\"status\":\"completed\",\"transactionId\":\"" + Guid.NewGuid() + "\",\"amount\":100}", ["script-http"] = "{\"status\":200,\"body\":\"{\\\"temp\\\":21}\",\"contentType\":\"application/json\"}", ["script-state"] = "{\"saved\":0}", ["script-runtime"] = "{\"runtime\":1}"
            };
            using var http = new System.Net.Http.HttpClient(new FakeApi(routes, posts));
            var config = new ServerConfiguration(Guid.NewGuid(), island, "http://api.test", new string('k', 40), "localhost", 7777, 7070, 20, 20, 100f, 4);
            var said = new List<(string Name, string Text, float Range)>(); var sent = new List<(Guid Avatar, JsonElement Json)>();
            var told = new List<string>(); var broadcasts = new List<string>(); var scripting = new IslandScripting(http, config, broadcasts.Add, (name, _, text, range) => said.Add((name, text, range)), (_, text) => told.Add(text), (_, _) => { });
            var gestures = new List<string>(); scripting.AnimationPlayer = (id, name, seconds) => { gestures.Add($"{id}:{name}:{seconds}"); return true; };
            var pushed = new List<string>(); scripting.PhysicsAction = (id, action, v) => { pushed.Add($"{id}:{action}:{v.Y}"); return true; };
            scripting.SendToAvatar = (avatar, json) => sent.Add((avatar, JsonDocument.Parse(json).RootElement.Clone()));
            await scripting.RefreshAsync(CancellationToken.None);
            var anaUser = Guid.NewGuid(); var anaPresence = new AvatarPresence(2, ana, anaUser, "Ana", 10, 1, 18); var brunoPresence = new AvatarPresence(3, bruno, Guid.NewGuid(), "Bruno", 11, 1, 19);
            async Task Run(int rounds = 6) { for (var i = 0; i < rounds; i++) { scripting.Update([anaPresence, brunoPresence]); await Task.Delay(40); } }

            Require(scripting.Scripts.Count == 4 && scripting.GetLinkset(child).Count == 2 && scripting.GetLinkset(child)[0].Id == root && scripting.GetRegion()!.Name == "Praça Central", "the linkset and the region come from the island content");
            Require(scripting.Scripts.Single(s => s.ScriptId == scriptPaused) is { } paused && scripting.StateOf(scriptPaused) == ScriptLifecycle.Paused, "a script the owner paused starts paused");
            await Run();

            scripting.Touch(anaPresence, root); await Run(10); await Task.Delay(300); await Run(5);
            Require(said.Any(s => s.Text == "psiu" && s.Range == 10f) && said.Any(s => s.Text == "ei" && s.Range == 100f), $"whisper and shout reach their ranges ({string.Join("|", said.Select(s => s.Text + s.Range))})");
            Require(!said.Any(s => s.Text == "NAO"), "a paused script hears nothing");
            var linked = posts.FirstOrDefault(p => p.Contains("build-operations") && p.Contains(child.ToString()) && p.Contains("\"positionX\":5")); Require(linked is not null, "setLinkPosition posted a change for the other part of the linkset");
            Require(sent.Count == 2 && sent.All(s => s.Avatar == ana), $"the dialog and the permission request went to the avatar that touched, nobody else (sent {sent.Count}, said [{string.Join("|", said.Select(s => s.Text))}], told [{string.Join(" // ", told)}] kinds [{string.Join(",", sent.Select(s => s.Json.GetProperty("kind").GetString()))}])");
            var dialog = sent.Single(s => s.Json.GetProperty("kind").GetString() == "dialog").Json; var permission = sent.Single(s => s.Json.GetProperty("kind").GetString() == "permission").Json;
            Require(dialog.GetProperty("operation").GetString() == "script_dialog" && dialog.GetProperty("buttons").GetArrayLength() == 2 && dialog.GetProperty("objectName").GetString() == "Casa", "the dialog carries its buttons and the object that asks");

            // somebody else cannot answer Ana's dialog or grant her permission; a made-up answer does nothing
            scripting.OnDialogResponse(brunoPresence, JsonSerializer.Serialize(new { id = dialog.GetProperty("id").GetGuid(), response = "Sim" })); scripting.OnDialogResponse(brunoPresence, JsonSerializer.Serialize(new { id = permission.GetProperty("id").GetGuid(), response = "Permitir" }));
            scripting.OnDialogResponse(anaPresence, "{\"id\":\"" + Guid.NewGuid() + "\",\"response\":\"Sim\"}"); scripting.OnDialogResponse(anaPresence, "not json"); await Run();
            Require(!said.Any(s => s.Text.StartsWith("resposta", StringComparison.Ordinal) || s.Text.StartsWith("permitido", StringComparison.Ordinal)), "answers from the wrong avatar or with made-up ids are refused");
            scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { id = dialog.GetProperty("id").GetGuid(), response = "Sim" })); scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { id = permission.GetProperty("id").GetGuid(), response = "Permitir" })); await Run();
            Require(said.Any(s => s.Text == "resposta Sim") && said.Any(s => s.Text == "permitido CONTROL_ANIMATION"), "the right avatar answers and the script hears it");
            Require(scripting.Scripts.Any() && !said.Any(s => s.Text.StartsWith("link ", StringComparison.Ordinal)), "no stray link messages");

            // what is reported to the API
            await scripting.FlushStateAsync(CancellationToken.None);
            var report = posts.Last(p => p.Contains("script-runtime")); var body = JsonDocument.Parse(report[(report.IndexOf('{'))..]).RootElement;
            var mine = body.GetProperty("runtime").EnumerateArray().Single(r => r.GetProperty("scriptId").GetGuid() == scriptRoot);
            Require(mine.GetProperty("state").GetString() == "Waiting" && mine.GetProperty("events").GetInt64() >= 2 && mine.GetProperty("cpuMilliseconds").GetDouble() > 0, "the report carries state and counters");
            Require(body.GetProperty("logs").EnumerateArray().Any(l => l.GetProperty("text").GetString() == "tocado por Ana" && l.GetProperty("level").GetString() == "info" && l.GetProperty("event").GetString() == "touch"), "the console lines are reported");
            Require(body.GetProperty("grants").EnumerateArray().Any(g => g.GetProperty("subjectId").GetGuid() == ana && g.GetProperty("permission").GetString() == "CONTROL_ANIMATION") && body.GetProperty("grants").EnumerateArray().Any(g => g.GetProperty("subjectId").GetGuid() == bruno), "granted and loaded permissions are reported");
            var reports = posts.Count(p => p.Contains("script-runtime")); await scripting.FlushStateAsync(CancellationToken.None); var again = JsonDocument.Parse(posts.Last(p => p.Contains("script-runtime"))[(posts.Last(p => p.Contains("script-runtime")).IndexOf('{'))..]).RootElement;
            Require(posts.Count(p => p.Contains("script-runtime")) == reports + 1 && !again.GetProperty("logs").EnumerateArray().Any(l => l.GetProperty("text").GetString() == "tocado por Ana"), "a console line is reported once");

            // a script the owner allowed to reach out: the island asks the API, the answer comes back as an event (and a script without the grant gets nothing)
            scripting.Touch(anaPresence, clockObj); await Run(8); await Task.Delay(300); await Run(8);
            var outgoing = posts.FirstOrDefault(p => p.Contains("script-http")); Require(outgoing is not null && outgoing.Contains(scriptClock.ToString()) && outgoing.Contains("https://api.exemplo.com/clima"), $"the request went to the API (posts: {string.Join(" // ", posts.Select(p => p[..Math.Min(60, p.Length)]))})");
            Require(said.Any(s => s.Text == "clima 200 {\"temp\":21} true"), $"the answer reached the script as on_http_response (said {string.Join("|", said.Select(s => s.Text))})");

            Require(pushed.Contains($"{clockObj}:impulse:5"), $"physics.applyImpulse reached the simulation (pushed {string.Join(",", pushed)})");
            Require(posts.Any(p => p.Contains("build-operations") && p.Contains(clockObj.ToString()) && p.Contains("physical")), "physics.setDynamic changed the physics settings through the build operation");

            // paying: the player sees the request, presses Pagar, the API moves the money and only then does the script hear on_payment
            var ask = sent.Last(s => s.Json.GetProperty("kind").GetString() == "payment").Json; Require(ask.GetProperty("buttons").GetArrayLength() == 2 && ask.GetProperty("message").GetString()!.Contains("L$ 100"), "the player is asked for the payment");
            Require(!said.Any(s => s.Text.StartsWith("pago", StringComparison.Ordinal)), "asking does not charge");
            scripting.OnDialogResponse(brunoPresence, JsonSerializer.Serialize(new { id = ask.GetProperty("id").GetGuid(), response = "Pagar" })); await Run(4); Require(!posts.Any(p => p.Contains("script-payment")), "somebody else cannot pay for the player");
            scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { id = ask.GetProperty("id").GetGuid(), response = "Pagar" })); await Run(8); await Task.Delay(200); await Run(8);
            var charge = posts.FirstOrDefault(p => p.Contains("script-payment")); Require(charge is not null && charge.Contains(anaUser.ToString()) && charge.Contains("\"amount\":100") && charge.Contains(scriptClock.ToString()), $"the API was asked to charge the player who agreed (post {charge})");
            Require(said.Any(s => s.Text == "pago 100 por Ana"), $"the script heard on_payment after the confirmation (said {string.Join("|", said.Select(s => s.Text))})");

            // the Conteúdo of an object changes (the island reads its content again): the scripts of that object hear it
            routes["island-content"] = routes["island-content"].Replace("\"contentJson\":\"[]\"", "\"contentJson\":\"[{\\\"name\\\":\\\"Campainha\\\",\\\"category\\\":\\\"sounds\\\"}]\""); await scripting.RefreshAsync(CancellationToken.None); await Run(8); await Task.Delay(300); await Run(8);
            Require(said.Any(s => s.Text == "item Campainha sounds"), $"a new item in the Conteúdo reached on_item_added (said {string.Join("|", said.Select(s => s.Text))})");

            // permissions asked of the avatar unlock the HUD and the animation; the HUD is sent to that avatar only and its clicks go back to the script
            var hudAsk = sent.Last(s => s.Json.GetProperty("kind").GetString() == "permission" && s.Json.GetProperty("message").GetString()!.Contains("HUD")).Json; var animAsk = sent.Last(s => s.Json.GetProperty("kind").GetString() == "permission" && s.Json.GetProperty("message").GetString()!.Contains("anima")).Json;
            scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { id = hudAsk.GetProperty("id").GetGuid(), response = "Permitir" })); scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { id = animAsk.GetProperty("id").GetGuid(), response = "Permitir" })); await Run(8); await Task.Delay(300); await Run(8);
            var screen = sent.LastOrDefault(s => s.Json.GetProperty("operation").GetString() == "script_hud"); Require(screen.Json.ValueKind == JsonValueKind.Object && screen.Avatar == ana && screen.Json.GetProperty("title").GetString() == "Loja" && screen.Json.GetProperty("elements")[0].GetProperty("type").GetString() == "button" && screen.Json.GetProperty("objectName").GetString() == "Relógio", "the HUD went to the avatar who allowed it");
            Require(gestures.Single().EndsWith(":wave:3", StringComparison.Ordinal), "the gesture was played after the avatar allowed it");
            var hudClick = JsonSerializer.Serialize(new { hud = true, script = scriptClock, action = "click", element = "ativar" }); scripting.OnDialogResponse(brunoPresence, hudClick); await Run(4); Require(!said.Any(s => s.Text.StartsWith("hud clique", StringComparison.Ordinal)), "somebody else cannot click Ana's HUD");
            scripting.OnDialogResponse(anaPresence, hudClick); scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { hud = true, script = scriptClock, action = "click", element = "inventado" })); await Run(8); await Task.Delay(300); await Run(8); Require(said.Count(s => s.Text == "hud clique ativar") == 1, "a click on the HUD reached the script, a made-up element did not");
            scripting.AnimationEnded(ana, "wave", "finished"); await Run(8); await Task.Delay(300); await Run(8); Require(said.Any(s => s.Text == "animacao wave finished"), "the script heard the gesture end");

            // particles and a light the script put on its object: announced to everybody, remembered for who arrives later, taken away with the script
            Require(broadcasts.Count(b => b.Contains("\"operation\":\"effect\"") && b.Contains("\"on\":true")) == 2 && broadcasts.Any(b => b.Contains("\"kind\":\"particles\"") && b.Contains("\"preset\":\"fire\"")) && broadcasts.Any(b => b.Contains("\"kind\":\"light\"") && b.Contains("\"radius\":6")), $"the emitter and the light were announced to everybody (told [{string.Join(" // ", told)}]; ops [{string.Join(" // ", broadcasts.Select(b => b.Length > 90 ? b[..90] : b))}])");
            Require(scripting.EffectOps().Count() == 2, "somebody who arrives later is sent the effects that are on");

            // the physics simulation reports contacts of a body: hits, landings and leaving, never resting contact, and not too many
            scripting.OnContact(child, root, true, .1f); scripting.OnContact(child, root, true, 4f); scripting.OnContact(child, null, true, 5f); scripting.OnContact(child, root, false, 0f); await Run(8); await Task.Delay(300); await Run(8);
            Require(said.Any(s => s.Text == "bateu object Casa") && said.Any(s => s.Text == "bateu ground Terreno") && said.Any(s => s.Text == "pousou 5") && said.Any(s => s.Text == "saiu Casa") && said.Count(s => s.Text == "bateu object Casa") == 1, $"collisions reached the script (told [{string.Join(" // ", told)}] scripts {string.Join(",", scripting.Scripts.Select(s => s.Name + ":" + s.Quarantined + ":" + s.Reason))})");


            // the owner took back SHOW_HUD from the editor: the island drops it when it reads its content again
            async Task<JsonElement> LastReport() { await scripting.FlushStateAsync(CancellationToken.None); var r = posts.Last(p => p.Contains("script-runtime")); return JsonDocument.Parse(r[r.IndexOf((char)123)..]).RootElement; }
            var before = await LastReport(); Require(before.GetProperty("grants").EnumerateArray().Any(g => g.GetProperty("permission").GetString() == "SHOW_HUD" && g.GetProperty("subjectId").GetGuid() == ana), "the island reports the permission the avatar gave");
            routes["island-content"] = routes["island-content"].Replace("\"state\":{}", "\"state\":{},\"revoked\":[{\"scriptId\":\"" + scriptClock + "\",\"subjectId\":\"" + ana + "\",\"permission\":\"SHOW_HUD\"}]"); await scripting.RefreshAsync(CancellationToken.None); await Run(4);
            var after = await LastReport(); Require(!after.GetProperty("grants").EnumerateArray().Any(g => g.GetProperty("permission").GetString() == "SHOW_HUD" && g.GetProperty("subjectId").GetGuid() == ana), "a permission the owner took back is gone from the island");
            // letting go of the button, and two touches in a row (the window script may chat only five times in ten seconds: let it pass)
            await Task.Delay(10500);
            said.Clear();
            scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { touch_end = true, objectId = child })); await Run(6);
            Require(!said.Any(s => s.Text.StartsWith("soltou", StringComparison.Ordinal)), "letting go without having touched is no event");
            scripting.Touch(anaPresence, child); scripting.OnDialogResponse(brunoPresence, JsonSerializer.Serialize(new { touch_end = true, objectId = child })); await Run(6);
            Require(!said.Any(s => s.Text.StartsWith("soltou", StringComparison.Ordinal)), "somebody else letting go does not end Ana's touch");
            scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { touch_end = true, objectId = child })); scripting.OnDialogResponse(anaPresence, JsonSerializer.Serialize(new { touch_end = true, objectId = child })); await Run(8); await Task.Delay(300); await Run(8);
            Require(said.Count(s => s.Text == "soltou Ana") == 1 && !said.Any(s => s.Text.StartsWith("duplo", StringComparison.Ordinal)), $"on_touch_end arrives once, and one touch is no double click ([{string.Join("|", said.Select(s => s.Text))}])");
            scripting.Touch(anaPresence, child); scripting.Touch(anaPresence, child); await Run(8); await Task.Delay(300); await Run(8);
            Require(said.Count(s => s.Text == "duplo Ana") == 1, $"two quick touches are one double click ([{string.Join("|", said.Select(s => s.Text))}] told [{string.Join(" // ", told)}])");


            // simulated bodies: on_physics_update while it moves (and once when it stops) and on_velocity_changed when the velocity changes enough
            var speedNow = new LifeVerse.Scripting.Vec3(0, 0, 0); scripting.VelocityOf = id => id == child ? speedNow : null;
            await Run(8); posts.Clear();
            speedNow = new LifeVerse.Scripting.Vec3(3, 0, 4); await Run(8); await Task.Delay(300); await Run(8);
            Require(posts.Any(p => p.Contains("build-operations") && p.Contains("corre 5")), $"on_velocity_changed told the speed ({string.Join(" // ", posts.Select(p => p.Length > 520 ? p[200..520] : p))})");
            Require(!posts.Any(p => p.Contains("7.5")), "a slow body does not trigger the fast branch"); posts.Clear(); await scripting.RefreshAsync(CancellationToken.None); await Run(4); speedNow = new LifeVerse.Scripting.Vec3(12, 0, 0); for (var t = 0; t < 20 && !posts.Any(p => p.Contains("7.5")); t++) { await scripting.RefreshAsync(CancellationToken.None); await Run(3); await Task.Delay(100); }      // the pretend API answers every change as if the object were gone, so it is read again
            Require(posts.Any(p => p.Contains("build-operations") && p.Contains("7.5")), $"on_physics_update ran while the body moved (posts {string.Join(" // ", posts.Select(p => p.Length > 560 ? p[250..560] : p))}, told [{string.Join(" // ", told)}] said {string.Join("|", said.Select(s => s.Text))})");
            scripting.VelocityOf = null; await Run(4);

            // trigger volumes: the box of the object (2 x 3 x 0.2 at 11, 1, 20), not a circle around it
            Require(IslandScripting.InsideVolume(new(11, 1, 20), new(0, 0, 0), new(2, 3, .2f), 11, 1, 20) && !IslandScripting.InsideVolume(new(11, 1, 20), new(0, 0, 0), new(2, 3, .2f), 11, 1, 22), "the volume is the box of the object");
            Require(IslandScripting.InsideVolume(new(0, 0, 0), new(0, 90, 0), new(10, 1, 1), 0, 0, 4) && !IslandScripting.InsideVolume(new(0, 0, 0), new(0, 90, 0), new(10, 1, 1), 4, 0, 0), "the volume turns with the object");
            var guest = new AvatarPresence(2, ana, anaUser, "Ana", 11, 1, 20);
            await scripting.RefreshAsync(CancellationToken.None); posts.Clear();
            for (var i = 0; i < 12; i++) { scripting.Update([guest]); await Task.Delay(60); } await Task.Delay(300); for (var i = 0; i < 8; i++) { scripting.Update([guest]); await Task.Delay(60); }
            Require(posts.Any(p => p.Contains("build-operations") && p.Contains("dentro Ana")), "stepping into the volume was announced");
            await scripting.RefreshAsync(CancellationToken.None); posts.Clear();
            for (var i = 0; i < 12; i++) { scripting.Update([anaPresence]); await Task.Delay(60); } await Task.Delay(300); for (var i = 0; i < 8; i++) { scripting.Update([anaPresence]); await Task.Delay(60); }
            Require(posts.Any(p => p.Contains("build-operations") && p.Contains("fora Ana")), "stepping out of the volume was announced");

            // giving: the object carries an item, the avatar allows GIVE_ITEMS, the API makes the copy and the script hears about it
            var giver = Guid.NewGuid(); var giverScript = Guid.NewGuid(); var gift = Guid.NewGuid();
            var giverSource = "function on_touch(e) permissions.request(e.avatar_id, 'GIVE_ITEMS') end function on_permission_granted(e) inventory.giveItem(e.avatar_id, 'Brinde') end function on_item_given(e) object.setName('deu ' .. e.item) economy.setObjectPrice(150) end function on_item_give_failed(e) object.setName('falhou ' .. e.reason) end function on_purchase(e) object.setName('vendido ' .. e.price .. ' ' .. e.sale_type) end";
            routes["script-give-item"] = "{\"status\":\"completed\",\"name\":\"Brinde\"}";
            routes["island-content"] = routes["island-content"].Replace("\"objects\":[", "\"objects\":[" + Obj(giver, "Doador", 30).Replace("\"contentJson\":\"[]\"", "\"contentJson\":" + JsonSerializer.Serialize(JsonSerializer.Serialize(new[] { new { assetId = gift, name = "Brinde", category = "objects" } }))) + ",").Replace("\"scripts\":[", "\"scripts\":[" + Script(giverScript, giver, "doador", giverSource) + ",");
            await scripting.RefreshAsync(CancellationToken.None); var donor = new AvatarPresence(2, ana, anaUser, "Ana", 30, 1, 20);
            for (var i = 0; i < 6; i++) { scripting.Update([donor]); await Task.Delay(40); }
            sent.Clear(); posts.Clear(); scripting.Touch(donor, giver); for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(40); }
            var giveAsk = sent.LastOrDefault(s => s.Json.GetProperty("kind").GetString() == "permission"); Require(giveAsk.Json.ValueKind == JsonValueKind.Object && giveAsk.Json.GetProperty("message").GetString()!.Contains("entregar itens"), "the avatar is asked to allow GIVE_ITEMS");
            scripting.OnDialogResponse(donor, JsonSerializer.Serialize(new { id = giveAsk.Json.GetProperty("id").GetGuid(), response = "Permitir" })); for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(60); } await Task.Delay(300); for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(60); }
            Require(posts.Any(p => p.Contains("script-give-item") && p.Contains(gift.ToString()) && p.Contains(anaUser.ToString())), "the island asked the API to deliver the item to the avatar");
            Require(posts.Any(p => p.Contains("build-operations") && p.Contains("deu Brinde")), "the script heard that the delivery was done");
            Require(posts.Any(p => p.Contains("build-operations") && p.Contains("\"operation\":\"properties\"") && p.Contains("\"salePrice\":150") && p.Contains(giver.ToString())), "economy.setObjectPrice asked the API to change the price of the object");
            // a sale that came back through the island reaches the scripts of the object that was sold, and does not make the island forget it
            await scripting.RefreshAsync(CancellationToken.None); posts.Clear();
            scripting.OnObjectApplied(JsonSerializer.Serialize(new { operation = "bought", objectId = giver, saleType = "copy", price = 250, buyerUserId = anaUser, sellerUserId = owner, delivered = Array.Empty<string>(), objects = Array.Empty<object>() }));
            Require(scripting.GetObject(giver) is not null, "a sale does not remove the object from the island");
            for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(60); } await Task.Delay(300); for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(60); }
            Require(posts.Any(p => p.Contains("build-operations") && p.Contains("vendido 250 copy")), "on_purchase told the price and the kind of sale");
            posts.Clear(); scripting.OnObjectApplied(JsonSerializer.Serialize(new { operation = "bought", objectId = giver, buyerUserId = anaUser, replay = true, objects = Array.Empty<object>() })); for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(60); }
            Require(!posts.Any(p => p.Contains("vendido")), "a replayed purchase is not a new sale");

            // non-player characters: made by a script whose owner allowed it, shown through the world of NPCs, walking and speaking
            var guard = Guid.NewGuid(); var guardScript = Guid.NewGuid(); var spawnedNpcs = new List<int>(); var goneNpcs = new List<int>();
            scripting.Npcs = new LifeVerse.Shared.Networking.NpcWorld((_, _) => 1f, () => 0f, () => 200f) { Spawned = n => spawnedNpcs.Add(n.PeerId), Despawned = goneNpcs.Add };
            var guardSource = "function on_touch(e) local g = npc.create('Guarda', 31, 20) npc.moveTo(g, 35, 20, 4) end function on_npc_arrived(e) npc.say(e.npc, 'cheguei') end";
            routes["island-content"] = routes["island-content"].Replace("\"objects\":[", "\"objects\":[" + Obj(guard, "Guarita", 30) + ",").Replace("\"scripts\":[", "\"scripts\":[" + Script(guardScript, guard, "guarita", guardSource) + ",");
            await scripting.RefreshAsync(CancellationToken.None); for (var i = 0; i < 6; i++) { scripting.Update([donor]); await Task.Delay(40); }
            sent.Clear(); said.Clear(); scripting.Touch(donor, guard); for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(60); } await Task.Delay(300); for (var i = 0; i < 4; i++) { scripting.Update([donor]); await Task.Delay(60); }
            Require(spawnedNpcs.Count == 0, "without the owner's permission no NPC is made");
            routes["island-content"] = routes["island-content"].Replace("\"grants\":[", "\"grants\":[{\"scriptId\":\"" + guardScript + "\",\"subjectId\":\"" + Guid.Empty + "\",\"permission\":\"CREATE_NPCS\"},");
            await scripting.RefreshAsync(CancellationToken.None); for (var i = 0; i < 6; i++) { scripting.Update([donor]); await Task.Delay(40); }
            scripting.Touch(donor, guard); await Task.Delay(1100); scripting.Touch(donor, guard); for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(60); } await Task.Delay(300); for (var i = 0; i < 4; i++) { scripting.Update([donor]); await Task.Delay(60); }
            Require(spawnedNpcs.Count >= 1 && scripting.Npcs.All.Count == spawnedNpcs.Count, $"the script made an NPC (spawned {spawnedNpcs.Count})");
            for (var i = 0; i < 6; i++) { scripting.Npcs.Tick(scripting.Now + i, 1f); } for (var i = 0; i < 8; i++) { scripting.Update([donor]); await Task.Delay(60); } await Task.Delay(300); for (var i = 0; i < 6; i++) { scripting.Update([donor]); await Task.Delay(60); }
            Require(scripting.Npcs.All.All(n => n.X == 35f) && said.Any(s => s.Name == "Guarda" && s.Text == "cheguei"), $"the NPC walked to where it was sent and spoke under its own name (said {string.Join("|", said.Select(s => s.Name + ":" + s.Text))})");
            // shutdown
            said.Clear(); await scripting.ShutdownAsync(); await Run(2); Require(said.Any(s => s.Text == "desligando") && posts.Any(p => p.Contains("script-state") || p.Contains("script-runtime")), "scripts hear the shutdown and the state is saved");

            GD.Print("event=scripting_api_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=scripting_api_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
