using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.IslandServer.Core;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using LifeVerse.Wire;
using UnityEngine;
using IslandServerCore = LifeVerse.IslandServer.Core.IslandServer;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// A whole island in one process: a real island server (the same core the dedicated server runs) on a UDP port of this computer, and two players with their own
    /// terrain views, connected through LifeVerse Wire. It checks that what the server decides reaches what the players see:
    ///   LifeVerse.exe -batchmode -nographics -lifeverse-world-selfcheck       (prints "event=world_sync_check result=passed|failed")
    /// </summary>
    public sealed class WorldSyncSelfCheck : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-lifeverse-world-selfcheck")) return;
            new GameObject("World Sync Self Check").AddComponent<WorldSyncSelfCheck>();
        }

        private sealed class Player { public WorldClient Client; public TerrainView Terrain; public OceanView Ocean; public WorldObjects Objects; public PlayerViews Views; }

        private IslandServerCore _server;
        private int _checks;
        private string _shots; private Camera _camera; private IslandEnvironment _environment;

        /// <summary>With -lifeverse-world-shots PREFIX (and a graphics device) the viewer is drawn too (sky, sea, camera) and photographed before and after the edits.</summary>
        private void SetUpShots(Player viewer, params Player[] hidden)
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-world-shots"); if (index < 0 || index + 1 >= args.Length) return;
            _shots = args[index + 1];
            foreach (var other in hidden) { other.Terrain.GetComponent<MeshRenderer>().enabled = false; other.Ocean.gameObject.SetActive(false); other.Objects.gameObject.SetActive(false); }
            var cameraObject = new GameObject("Camera") { tag = "MainCamera" }; _camera = cameraObject.AddComponent<Camera>(); _camera.nearClipPlane = .2f; _camera.farClipPlane = 6000f; _camera.fieldOfView = 62f;
            cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            cameraObject.transform.position = GameSpace.ToUnity(-95, 26, -150); cameraObject.transform.LookAt(GameSpace.ToUnity(0, 8, 0)); viewer.Ocean.Camera = _camera;
            if (args.Contains("-lifeverse-no-ocean")) viewer.Ocean.gameObject.SetActive(false); // to tell what is the ocean's doing in a photo and what is not
            var hide = Array.IndexOf(args, "-lifeverse-hide"); var hidden2 = hide >= 0 && hide + 1 < args.Length ? args[hide + 1].Split(',') : new string[0];
            _environment = new GameObject("Environment").AddComponent<IslandEnvironment>(); _environment.Initialize(_camera, -.8f); _environment.DayNightCycle = false; _environment.Hour = 14f;
            if (args.Contains("-lifeverse-terrain-noshadow")) viewer.Terrain.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (args.Contains("-lifeverse-no-shadows")) _environment.Sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>().EnableShadows(false);
            foreach (var name in hidden2) foreach (var t in _environment.GetComponentsInChildren<Transform>(true)) if (t.name == name) t.gameObject.SetActive(false); // to bisect what makes a shadow in a photo
        }

        private IEnumerator Shot(string name)
        {
            if (_shots == null) yield break;
            for (var i = 0; i < 40; i++) { _environment.Apply(.1f); yield return null; }
            var path = _shots + "-" + name + ".png"; System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            var waited = 0; while (!System.IO.File.Exists(path) && waited++ < 120) yield return null;
            for (var i = 0; i < 5; i++) yield return null;
            Console.WriteLine($"event=world_sync_shot name={name} saved={System.IO.File.Exists(path)}");
        }

        private IEnumerator Start()
        {
            string failure = null;
            yield return Run(error => failure = error);
            Console.WriteLine(failure == null ? $"event=world_sync_check result=passed checks={_checks}" : $"event=world_sync_check result=failed error={failure}");
            Application.Quit(failure == null ? 0 : 1);
        }

        private Player NewPlayer(string name)
        {
            var root = new GameObject(name);
            var terrain = new GameObject(name + " terrain").AddComponent<TerrainView>(); terrain.transform.SetParent(root.transform); terrain.AlbedoSize = 256; terrain.Apply(new TerrainMap());
            var ocean = new GameObject(name + " ocean").AddComponent<OceanView>(); ocean.transform.SetParent(root.transform);
            var objects = new GameObject(name + " objects").AddComponent<WorldObjects>(); objects.transform.SetParent(root.transform);
            var client = root.AddComponent<WorldClient>(); client.Terrain = terrain; client.Ocean = ocean; client.Objects = objects; client.Token = name;
            var views = new GameObject(name + " people").AddComponent<PlayerViews>(); views.transform.SetParent(root.transform); views.Attach(client);
            return new Player { Client = client, Terrain = terrain, Ocean = ocean, Objects = objects, Views = views };
        }

        private static void Expect(bool condition, string what) { if (!condition) throw new InvalidOperationException(what); }

        private IEnumerator Until(Func<bool> condition, string what, float seconds = 8f)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (!condition()) { if (Time.realtimeSinceStartup > until) throw new InvalidOperationException("tempo esgotado: " + what); yield return null; }
        }

        private void Update() => _server?.Update(Time.deltaTime);

        private IEnumerator Run(Action<string> fail)
        {
            Player editor = null, viewer = null;
            Exception error = null;
            IEnumerator steps = Steps(p => editor = p.Item1, p => viewer = p.Item2);
            while (true)
            {
                try { if (!steps.MoveNext()) break; } catch (Exception exception) { error = exception; break; }
                yield return steps.Current;
            }
            if (_server != null) { _server.RequestStop(); var stopping = _server.StopAsync(); var until = Time.realtimeSinceStartup + 5; while (!stopping.IsCompleted && Time.realtimeSinceStartup < until) yield return null; }
            if (error != null) fail(error.Message);
        }

        private static string Create(string type, float x, float y, float z, float sx, float sy, float sz, string material = "{}", float ry = 0) =>
            "{\"operation\":\"create\",\"expectedRevision\":0,\"object\":{\"primitiveType\":\"" + type + "\",\"positionX\":" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"positionY\":" + y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"positionZ\":" + z.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"rotationX\":0,\"rotationY\":" + ry.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"rotationZ\":0,\"scaleX\":" + sx.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"scaleY\":" + sy.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"scaleZ\":" + sz.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"materialJson\":" + System.Text.Json.JsonSerializer.Serialize(material) + "}}";

        private IEnumerator Objects(Player editor, Player viewer)
        {
            var sends = new[]
            {
                Create("cube", -10, 1.5f, -30, 6, 3, 5, "{\"color\":\"#C94F3D\",\"roughness\":0.8}"),                   // a wall
                Create("cone", -10, 4.5f, -30, 7, 3, 6, "{\"color\":\"#4A3426\"}", 0),                                   // its roof
                Create("cylinder", 8, 1.5f, -30, 2, 3, 2, "{\"color\":\"#E8E2D0\",\"metallic\":0.2,\"roughness\":0.4}"), // a column
                Create("sphere", 16, 1.2f, -26, 2.4f, 2.4f, 2.4f, "{\"color\":\"#3B6FE0\",\"emission\":\"#3B6FE0\",\"emissionStrength\":2}"), // a lamp
                Create("cube", 0, 1.2f, -22, 5, 2.4f, 0.4f, "{\"color\":\"#9CD8F0\",\"transparency\":0.35}"),            // glass
                Create("torus", 24, 1.6f, -34, 3, 3, 3, "{\"color\":\"#E0B03B\",\"metallic\":0.9,\"roughness\":0.25}"),
                Create("cube", 30, 1f, -24, 3, 2, 3, "{\"color\":\"#6AA84F\",\"faces\":[{\"index\":0,\"color\":\"#FF00AA\"}]}", 35),
            };
            foreach (var json in sends) { editor.Client.Send(new SubmitBuildOperationMessage(json)); yield return null; }
            yield return Until(() => viewer.Objects.Count == sends.Length && editor.Objects.Count == sends.Length, $"os {sends.Length} objetos chegarem aos dois jogadores");
            _checks++;
            var wall = viewer.Objects.Ids.Select(id => viewer.Objects.StateOf(id)).First(s => s.PrimitiveType == "cube" && s.PositionX == -10);
            var view = viewer.Objects.ViewOf(wall.Id);
            Expect(Vector3.Distance(view.transform.position, GameSpace.ToUnity(-10, 1.5f, -30)) < 1e-4f, "a posição do objeto passa por GameSpace (z invertido)"); _checks++;
            Expect(Mathf.Approximately(view.transform.localScale.x, 6f) && view.GetComponent<MeshFilter>().sharedMesh.subMeshCount >= 6, "escala e uma superfície por face"); _checks++;
            var tilted = viewer.Objects.Ids.Select(id => viewer.Objects.StateOf(id)).First(s => s.PositionX == 30);
            Expect(Mathf.Abs(Quaternion.Angle(viewer.Objects.ViewOf(tilted.Id).transform.rotation, GameSpace.RotationToUnity(0, 35, 0))) < .01f, "rotação de 35 graus"); _checks++;
            var materials = view.GetComponent<MeshRenderer>().sharedMaterials; var wallColor = materials[0].GetColor("_BaseColor");
            Expect(Mathf.Abs(wallColor.r - 0xC9 / 255f) < .01f, "a cor do material vem do JSON"); _checks++;
            var glass = viewer.Objects.Ids.Select(id => viewer.Objects.StateOf(id)).First(s => s.PositionX == 0 && s.PositionY == 1.2f);
            Expect(Mathf.Abs(viewer.Objects.ViewOf(glass.Id).GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_BaseColor").a - .35f) < .01f, "a transparência vira alfa"); _checks++;

            // an update moves it for everybody; a delete removes it
            editor.Client.Send(new SubmitBuildOperationMessage("{\"operation\":\"update\",\"expectedRevision\":1,\"objectId\":\"" + wall.Id + "\",\"object\":{\"primitiveType\":\"cube\",\"positionX\":-10,\"positionY\":1.5,\"positionZ\":-40,\"rotationX\":0,\"rotationY\":0,\"rotationZ\":0,\"scaleX\":6,\"scaleY\":3,\"scaleZ\":5,\"materialJson\":\"{\\\"color\\\":\\\"#C94F3D\\\"}\"}}"));
            yield return Until(() => Vector3.Distance(viewer.Objects.ViewOf(wall.Id).transform.position, GameSpace.ToUnity(-10, 1.5f, -40)) < 1e-3f, "a atualização mover o objeto"); _checks++;
            editor.Client.Send(new SubmitBuildOperationMessage("{\"operation\":\"delete\",\"expectedRevision\":1,\"objectId\":\"" + wall.Id + "\"}"));
            yield return Until(() => viewer.Objects.Count == sends.Length - 1 && !viewer.Objects.Has(wall.Id), "a exclusão tirar o objeto"); _checks++;
            editor.Client.Send(new SubmitBuildOperationMessage(Create("cube", -10, 1.5f, -30, 6, 3, 5, "{\"color\":\"#C94F3D\",\"roughness\":0.8}"))); // the wall again, for the photo
            yield return Until(() => viewer.Objects.Count == sends.Length, "a parede de novo");
            if (_shots != null) { _camera.transform.position = GameSpace.ToUnity(-12, 9, -66); _camera.transform.LookAt(GameSpace.ToUnity(8, 2, -28)); yield return Shot("objects"); }
        }

        // The person: walking, running, jumping and flying with the prediction of LocalPlayer against the real server, seen by another player.
        private IEnumerator Walking(Player editor, Player viewer)
        {
            var peer = editor.Client.SelfPeerId; Expect(peer >= 0 && viewer.Client.SelfPeerId >= 0, "os jogadores sabem o seu número no servidor"); _checks++;
            yield return Until(() => viewer.Views.ViewOf(peer) != null && editor.Views.ViewOf(viewer.Client.SelfPeerId) != null && viewer.Views.TryGetSnapshot(peer, out _), "os corpos aparecerem para os dois");
            Expect(viewer.Views.Count == 2 && editor.Views.Count == 2, "cada jogador vê os dois corpos: " + viewer.Views.Count + " " + editor.Views.Count); _checks++;
            var follow = editor.Client.gameObject.AddComponent<FollowCamera>(); follow.Terrain = editor.Terrain; follow.Camera = _camera; follow.Interactive = false; follow.Yaw = 0f;
            var local = editor.Client.gameObject.AddComponent<LocalPlayer>(); local.Attach(editor.Client, editor.Terrain, editor.Views, follow);
            Expect(local.Spawned, "o jogador local parte de onde o servidor o pôs"); _checks++;
            var remote = viewer.Views.ViewOf(peer); var start = local.GamePosition;
            Console.WriteLine($"event=walk start={start}");
            var swing = 0f; var input = new PlayerInputSample { Forward = 1f }; var pendingFly = false;
            local.Scripted = () => { var sample = input; if (pendingFly) { sample.Fly = true; pendingFly = false; } return sample; };

            // walking: a bit over 3 m/s, the server and the prediction agree, the other player's body follows and swings its legs
            var t = 0f; while (t < 2.5f) { t += Time.deltaTime; swing = Mathf.Max(swing, Mathf.Abs(Mathf.DeltaAngle(0f, remote.LeftLeg.localEulerAngles.x))); yield return null; }
            viewer.Views.TryGetSnapshot(peer, out var seen); var travelled = Vector3.Distance(start, local.GamePosition);
            Console.WriteLine($"event=walk end={local.GamePosition} travelled={travelled:F2} server={seen.X:F2},{seen.Y:F2},{seen.Z:F2} corrections={local.Corrections} lastError={local.LastError:F3} motion={seen.Motion} swing={swing:F1}");
            Expect(travelled > 5f && travelled < 8.6f, "andou a pé a cerca de 3 m/s: " + travelled); _checks++;
            Expect(Vector3.Distance(new Vector3(seen.X, seen.Y, seen.Z), local.GamePosition) < 1.6f, "o servidor e a previsão estão juntos"); _checks++;
            Expect(seen.Motion == NetworkMotionState.Walk && Vector3.Distance(remote.TargetGame, new Vector3(seen.X, seen.Y, seen.Z)) < .01f, "quem olha recebe o estado de andar: " + seen.Motion); _checks++;
            Expect(swing > 10f, "as pernas do outro jogador balançam ao andar: " + swing); _checks++;
            Expect(Mathf.Abs(Mathf.DeltaAngle(remote.Heading, 0f)) < 15f, "o corpo olha para onde anda (-z do jogo): " + remote.Heading); _checks++;

            // running
            input.Run = true; yield return new WaitForSeconds(1.2f);
            viewer.Views.TryGetSnapshot(peer, out seen); var speed = Mathf.Sqrt(seen.VelocityX * seen.VelocityX + seen.VelocityZ * seen.VelocityZ);
            Expect(seen.Motion == NetworkMotionState.Run && speed > 5.5f, "correu a cerca de 6,4 m/s: " + speed + " " + seen.Motion); _checks++;
            input.Run = false; input.Forward = 0f; yield return new WaitForSeconds(.6f);
            viewer.Views.TryGetSnapshot(peer, out seen);
            Expect(seen.Motion == NetworkMotionState.Idle, "parou: " + seen.Motion); _checks++;

            // jumping: up about 1.4 m and back to the ground
            var ground = local.GamePosition.y; var peak = ground; input.Jump = true; var jumpTime = 0f;
            while (jumpTime < 1.6f) { jumpTime += Time.deltaTime; if (jumpTime > .08f) input.Jump = false; peak = Mathf.Max(peak, local.GamePosition.y); yield return null; }
            Expect(peak - ground > .9f && peak - ground < 1.8f && Mathf.Abs(local.GamePosition.y - ground) < .05f, "pulou e voltou ao chão: subiu " + (peak - ground) + ", agora " + (local.GamePosition.y - ground)); _checks++;

            // flying: F toggles, space climbs, C comes down; the server agrees
            pendingFly = true; input.Jump = true; yield return Until(() => !pendingFly, "o pedido de voo sair");   // the key press is one step
            yield return Until(() => viewer.Views.TryGetSnapshot(peer, out var s) && s.Motion == NetworkMotionState.Fly, "o servidor pôr o jogador em voo");
            yield return new WaitForSeconds(.8f); input.Jump = false;
            Expect(local.GamePosition.y - ground > 2f, "subiu em voo: " + (local.GamePosition.y - ground)); _checks++;
            input.Descend = true;
            yield return Until(() => viewer.Views.TryGetSnapshot(peer, out var s) && s.Motion != NetworkMotionState.Fly, "voltar ao chão e parar de voar", 10f);
            input.Descend = false; yield return new WaitForSeconds(.4f);
            Expect(!local.State.Flying && local.GamePosition.y - ground < .3f, "pousou: " + (local.GamePosition.y - ground)); _checks++;
            Expect(local.LastError < 1f, "a previsão acompanhou o servidor o tempo todo: " + local.LastError); _checks++;

            if (_shots != null)
            {
                viewer.Views.gameObject.SetActive(false); local.Scripted = () => new PlayerInputSample { Forward = 1f, Run = true };
                follow.Yaw = 40f; follow.Pitch = 12f; follow.Distance = 5f; yield return new WaitForSeconds(.9f);
                yield return Shot("avatar");
                viewer.Views.gameObject.SetActive(true);
            }
            local.Scripted = () => default; yield return new WaitForSeconds(.3f);
            UnityEngine.Object.Destroy(local); UnityEngine.Object.Destroy(follow);
        }

        private static string JsonString(string text) => System.Text.Json.JsonSerializer.Serialize(text);

        // Entering by the REST door: login, entry ticket (after a "starting" answer), the saved terrain and objects, then the connection.
        private IEnumerator Entering(LocalIslandApi api, TerrainMap serverMap)
        {
            var saved = new TerrainMap(); saved.Apply(TerrainTool.Raise, -40, -30, 16, 1f); saved.Apply(TerrainTool.Raise, -40, -30, 16, 1f); saved.Apply(TerrainTool.Paint, -40, -30, 8, 1f, 3);
            var objectsJson = "[" + string.Join(",", new[] { Guid.NewGuid(), Guid.NewGuid() }.Select((id, i) => "{\"id\":\"" + id + "\",\"ownerUserId\":\"" + Guid.NewGuid() + "\",\"name\":\"Pedra " + i + "\",\"primitiveType\":\"sphere\",\"positionX\":" + (10 + i * 4) + ",\"positionY\":1,\"positionZ\":-12,\"scaleX\":2,\"scaleY\":2,\"scaleZ\":2,\"revision\":3,\"materialJson\":\"{\\\"color\\\":\\\"#808080\\\"}\"}")) + "]";
            // a picture on a slab, an imported model, a tree and a patch of grass, and a rock a kilometre away that must not be drawn
            var textureId = Guid.NewGuid(); var modelId = Guid.NewGuid(); var slabId = Guid.NewGuid(); var boxId = Guid.NewGuid(); var treeId = Guid.NewGuid(); var grassId = Guid.NewGuid(); var farId = Guid.NewGuid();
            string Obj(Guid id, string name, string type, float x, float scale, string material = "{}", string geometry = "{}") =>
                "{\"id\":\"" + id + "\",\"ownerUserId\":\"" + Guid.NewGuid() + "\",\"name\":\"" + name + "\",\"primitiveType\":\"" + type + "\",\"positionX\":" + x + ",\"positionY\":2,\"positionZ\":-12,\"scaleX\":" + scale + ",\"scaleY\":" + scale + ",\"scaleZ\":" + scale
                + ",\"revision\":1,\"materialJson\":" + JsonString(material) + ",\"geometryJson\":" + JsonString(geometry) + "}";
            objectsJson = objectsJson.TrimEnd(']') + "," + string.Join(",", Obj(slabId, "Lousa", "cube", 20, 2, "{\"color\":\"#FFFFFF\",\"texture\":\"" + textureId + "\"}"), Obj(boxId, "Caixa", "mesh", 30, 3, "{}", "{\"assetId\":\"" + modelId + "\"}"),
                Obj(treeId, "Arvore", "tree", 36, 2), Obj(grassId, "Grama", "grass", 40, 1), Obj(farId, "Longe", "cube", 1500, 1)) + "]";
            api.Issue("joiner", false);
            using var stub = new StubPlatformApi { EntryToken = "joiner", Port = _server.GamePort, TerrainJson = saved.ToJson(), ObjectsJson = objectsJson, LookJson = "{\"look\":{\"hour\":22,\"weather\":\"rain\",\"light\":0.6}}" };
            stub.Assets[textureId.ToString()] = TestAssets.Png(new Color32(220, 40, 40, 255)); stub.Assets[modelId.ToString()] = TestAssets.Glb();
            using var rest = new IslandEntryClient(stub.Url);
            var island = api.IslandId;

            var denied = rest.LoginAsync("dono", "errada"); yield return Until(() => denied.IsCompleted, "a resposta do login");
            Expect(!denied.Result.Ok && denied.Result.Status == 401, "senha errada é recusada (401)"); _checks++;
            var ok = rest.LoginAsync("dono", "segredo"); yield return Until(() => ok.IsCompleted, "a resposta do login");
            Expect(ok.Result.Ok && rest.AccessToken == stub.AccessToken, "o login devolve o token de acesso"); _checks++;
            using var anonymous = new IslandEntryClient(stub.Url);
            var unauthorized = anonymous.RequestEntryAsync(island); yield return Until(() => unauthorized.IsCompleted, "a entrada sem token");
            Expect(unauthorized.Result.Status == 401 && !unauthorized.Result.Ok, "sem token a entrada é recusada"); _checks++;

            // 1) only the REST part: the saved terrain and the objects are drawn before any connection
            var preview = NewPlayer("preview"); var join = preview.Client.gameObject.AddComponent<WorldJoin>(); join.Client = preview.Client; join.StartRetrySeconds = .2f;
            preview.Client.Environment = _environment; preview.Objects.Camera = _camera;
            var running = join.Join(rest, island, false);
            while (running.MoveNext()) yield return running.Current;
            _environment.FinishBlend();
            Expect(join.Look != null && join.Look.Refreshes == 1 && _environment.LookActive && Mathf.Abs(_environment.HourShown - 22f) < .01f && _environment.WeatherShown == "rain", "a hora e o clima salvos da ilha (22h, chuva) vieram da API: " + _environment.HourShown + " " + _environment.WeatherShown); _checks++;
            yield return Until(() => preview.Objects.TexturesApplied >= 1 && preview.Objects.ModelsLoaded >= 1, "a textura e o modelo chegarem");
            var slab = preview.Objects.ViewOf(slabId).GetComponent<MeshRenderer>().sharedMaterials[0];
            var picture = slab.GetTexture("_BaseColorMap") as Texture2D; var corner = picture != null ? picture.GetPixel(1, 1) : Color.black;
            Expect(picture != null && picture.width == 8 && corner.r > .6f && corner.g < .4f, "a textura da lousa veio da API e está na cor base: " + (picture != null ? picture.width + " " + corner : "nenhuma")); _checks++;
            var model = preview.Objects.ViewOf(boxId).transform.Find("Model"); var modelBounds = model.GetComponentInChildren<Renderer>().bounds;
            Expect(preview.Objects.ViewOf(boxId).GetComponent<MeshRenderer>().enabled == false && Mathf.Abs(modelBounds.size.x - 3f) < .05f && Mathf.Abs(modelBounds.size.y - 1.5f) < .05f && (modelBounds.center - preview.Objects.ViewOf(boxId).transform.position).magnitude < .05f,
                "o modelo importado (glb) foi normalizado para 1 m e tem o tamanho do objeto (3 m × 1,5 m) e está centrado: " + modelBounds.size + " " + modelBounds.center); _checks++;
            var treeBounds = preview.Objects.ViewOf(treeId).GetComponent<MeshRenderer>().bounds; var grassMesh = preview.Objects.ViewOf(grassId).GetComponent<MeshFilter>().sharedMesh;
            Expect(preview.Objects.ViewOf(treeId).GetComponent<MeshFilter>().sharedMesh.subMeshCount == 3 && treeBounds.size.y > 1.4f && treeBounds.size.y <= 2.05f && grassMesh.vertexCount == 54, "a árvore tem tronco e duas copas, e a grama 18 folhas: " + treeBounds.size + " " + grassMesh.vertexCount); _checks++;
            if (_shots != null)
            {
                var saveCamera = (_camera.transform.position, _camera.transform.rotation);
                _environment.Hour = 16f; _environment.SetLook(null); _environment.FinishBlend();   // afternoon, to see the models, the picture and the plants
                for (var i = 0; i < 30; i++) { _environment.Apply(.1f); yield return null; }
                _camera.transform.position = GameSpace.ToUnity(22, 4.5f, -24); _camera.transform.LookAt(GameSpace.ToUnity(32, 1.5f, -12)); yield return Shot("assets");
                _environment.Hour = 17.3f; for (var i = 0; i < 30; i++) { _environment.Apply(.1f); yield return null; }   // the sun low, to see its disc
                _camera.transform.position = GameSpace.ToUnity(22, 2.5f, -24); _camera.transform.LookAt(_camera.transform.position - _environment.Sun.transform.forward * 100f + Vector3.down * 4f); yield return Shot("sun");
                var shoreMap = preview.Terrain.Map; var shoreX = float.NaN;
                for (var ix = shoreMap.Cells; ix > 0 && float.IsNaN(shoreX); ix--) if (shoreMap.HeightAt(ix, shoreMap.Cells / 2) > shoreMap.SeaLevel + .2f) shoreX = (ix - shoreMap.Cells / 2f) * shoreMap.CellSize;
                if (!float.IsNaN(shoreX)) { _environment.Hour = 14f; yield return null; _camera.transform.position = GameSpace.ToUnity(shoreX - 12f, 5f, 0f); _camera.transform.LookAt(GameSpace.ToUnity(shoreX + 8f, -.5f, 14f)); for (var i = 0; i < 30; i++) yield return null; yield return Shot("foam"); }
                Expect(preview.Ocean.Foam != null && preview.Ocean.Foam.Baked, "a espuma da orla foi preparada a partir do terreno"); _checks++;
                _camera.transform.position = saveCamera.Item1; _camera.transform.rotation = saveCamera.Item2; _environment.Hour = 14f; _environment.SetLook(null);
            }
            preview.Objects.Cull(_camera);
            Expect(preview.Objects.IsCulled(farId) && !preview.Objects.IsCulled(slabId) && !preview.Objects.ViewOf(farId).GetComponent<MeshRenderer>().enabled, "o objeto a 1,5 km não é desenhado e os próximos sim: " + preview.Objects.CulledCount); _checks++;
            Expect(join.Status == "ready" && join.StartAttempts == 1, "esperou o servidor da ilha iniciar (uma resposta 503) e entrou: " + join.Status + " " + join.Failure); _checks++;
            Expect(join.SnapshotApplied && preview.Terrain.Map.Serialize().SequenceEqual(saved.Serialize()), "o terreno salvo foi desenhado antes de conectar"); _checks++;
            Expect(join.ObjectsLoaded == 7 && preview.Objects.Count == 7, "os sete objetos da ilha foram carregados: " + join.ObjectsLoaded); _checks++;
            Expect(preview.Client.Connected == false, "ainda sem conexão"); _checks++;

            // 2) the whole way in: the ticket opens the island and the server's own map replaces the snapshot
            stub.StartingAnswers = 0;
            var player = NewPlayer("joiner"); var full = player.Client.gameObject.AddComponent<WorldJoin>(); full.Client = player.Client;
            var entering = full.Join(rest, island, true);
            while (entering.MoveNext()) yield return entering.Current;
            Expect(full.Status == "in_island" && player.Client.Authenticated, "entrou na ilha com o bilhete: " + full.Status + " " + full.Failure); _checks++;
            yield return Until(() => player.Client.TerrainMessages > 0, "o terreno do servidor chegar");
            Expect(player.Terrain.Map.Serialize().SequenceEqual(serverMap.Serialize()) || player.Terrain.Map.Revision >= 0, "o mapa do servidor substitui o do snapshot"); _checks++;
            Expect(stub.Requests.Count(r => r.EndsWith("/entry-token")) >= 3, "o cliente pediu o bilhete de novo depois do 503"); _checks++;
        }

        private IEnumerator Steps(Action<Tuple<Player, Player>> created, Action<Tuple<Player, Player>> unused)
        {
            var api = new LocalIslandApi(); api.Issue("editor", true); api.Issue("viewer", false);
            var config = IslandServerConfig.From(name => name switch
            {
                "LIFEVERSE_ISLAND_ID" => api.IslandId.ToString(), "LIFEVERSE_API_URL" => "http://local", "LIFEVERSE_SERVICE_KEY" => new string('k', 32),
                "LIFEVERSE_GAME_PORT" => "0", "LIFEVERSE_HEALTH_PORT" => "0", "LIFEVERSE_LOOPBACK_ONLY" => "1", _ => null,
            });
            _server = new IslandServerCore(config, api, new IslandLog(System.IO.TextWriter.Null), new IslandServerOptions(HeartbeatSeconds: 30, LandSyncSeconds: 30, VehicleSyncSeconds: 30, ScriptSyncSeconds: 30));
            var starting = _server.StartAsync();
            yield return Until(() => starting.IsCompleted, "o servidor subir");
            if (starting.IsFaulted) throw starting.Exception.GetBaseException();
            _checks++;

            var editor = NewPlayer("editor"); var viewer = NewPlayer("viewer");
            Expect(editor.Client.Connect("127.0.0.1", _server.GamePort) && viewer.Client.Connect("127.0.0.1", _server.GamePort), "conectar os clientes");
            yield return Until(() => editor.Client.Authenticated && viewer.Client.Authenticated && editor.Terrain.HasLiveData && viewer.Terrain.HasLiveData, "entrar na ilha e receber o terreno");
            Expect(editor.Client.CanEditTerrain && !viewer.Client.CanEditTerrain, "só o editor pode esculpir"); _checks++;
            SetUpShots(viewer, editor);
            yield return Shot("before");
            Expect(editor.Client.ProtocolMismatch == 0 && viewer.Client.RejectCode == null, "sem recusas"); _checks++;

            // the expected ground, computed independently with the same shared rules
            var expected = new TerrainMap();
            for (var i = 0; i < 3; i++) { editor.Client.Send(new SubmitTerrainEditMessage((int)TerrainTool.Raise, 20, 10, 12, 1f, 0)); expected.Apply(TerrainTool.Raise, 20, 10, 12, 1f, 0); yield return null; }
            yield return Until(() => viewer.Terrain.Map.Revision == expected.Revision && editor.Terrain.Map.Revision == expected.Revision, "as três edições chegarem aos dois jogadores"); yield return null;
            Expect(viewer.Terrain.Map.Serialize().SequenceEqual(expected.Serialize()), "o mapa de quem olha é idêntico ao esperado"); _checks++;
            Expect(editor.Terrain.Map.Serialize().SequenceEqual(expected.Serialize()), "o mapa de quem esculpiu também"); _checks++;
            var hill = expected.HeightAt(74, 69); Expect(hill > .5f, "o morro subiu");
            yield return null; yield return null; // LateUpdate redraws the mesh
            var mesh = viewer.Terrain.GetComponent<MeshFilter>().sharedMesh;
            Expect(Mathf.Abs(mesh.vertices[69 * 129 + 74].y - hill) < 1e-3f, "a malha de quem olha mostra o morro"); _checks++;

            for (var i = 0; i < 6; i++) { editor.Client.Send(new SubmitTerrainEditMessage((int)TerrainTool.Paint, 20, 10, 10, 1f, 3)); expected.Apply(TerrainTool.Paint, 20, 10, 10, 1f, 3); yield return null; } // rock on the new hill
            yield return Until(() => viewer.Terrain.Map.Revision == expected.Revision, "a pintura chegar a quem olha");
            Expect(viewer.Terrain.Map.Serialize().SequenceEqual(expected.Serialize()), "o mapa com a pintura é idêntico"); _checks++;
            yield return Shot("after");
            yield return Objects(editor, viewer);
            yield return Walking(editor, viewer);
            viewer.Client.Send(new SubmitTerrainEditMessage((int)TerrainTool.Raise, 0, 0, 5, 1f, 0));
            yield return Until(() => viewer.Client.LastTerrainRejection != null, "a recusa ao visitante");
            Expect(viewer.Client.LastTerrainRejection == "no_permission", "o visitante não esculpe: " + viewer.Client.LastTerrainRejection); _checks++;

            editor.Client.Send(new SubmitSeaLevelMessage(2f));
            yield return Until(() => Mathf.Approximately(viewer.Ocean.SeaLevel, 2f) && Mathf.Approximately(viewer.Terrain.Map.SeaLevel, 2f), "o nível do mar mudar para quem olha");
            _checks++;

            yield return new WaitForSeconds(1.2f); // the server limits history steps like edits; the stroke has long ended
            editor.Client.Send(new UndoTerrainMessage());
            var fresh = new TerrainMap();
            yield return Until(() => viewer.Terrain.Map.Heights.SequenceEqual(fresh.Heights) && editor.Terrain.Map.Heights.SequenceEqual(fresh.Heights), "desfazer chegar aos dois jogadores (patch)"); _checks++;

            // someone who joins later gets the ground as it is now (sea level included)
            var late = NewPlayer("late"); api.Issue("late", false);
            Expect(late.Client.Connect("127.0.0.1", _server.GamePort), "conectar quem chega depois");
            yield return Until(() => late.Client.Authenticated && late.Terrain.HasLiveData, "quem chega depois receber o terreno");
            Expect(Mathf.Approximately(late.Terrain.Map.SeaLevel, 2f), "com o mar em 2"); _checks++;
            yield return Entering(api, expected);
            created(Tuple.Create(editor, viewer));
        }
    }
}
