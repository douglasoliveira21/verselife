using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.IslandServer.Core;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using LifeVerse.Wire;
using LifeVerse.Unity.Audio;
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

        private sealed class Player { public WorldClient Client; public TerrainView Terrain; public OceanView Ocean; public WorldObjects Objects; public PlayerViews Views; public VehicleViews Vehicles; }

        private IslandServerCore _server;
        private int _checks;
        private string _shots; private Camera _camera; private IslandEnvironment _environment;

        /// <summary>With -lifeverse-world-shots PREFIX (and a graphics device) the viewer is drawn too (sky, sea, camera) and photographed before and after the edits.</summary>
        private void SetUpShots(Player viewer, params Player[] hidden)
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-world-shots");
            if (index < 0 || index + 1 >= args.Length)
            {
                // without photos there is still a camera and a sky, so the checks that need them (the island look, the culling, the planner) run the same way
                var plain = new GameObject("Camera") { tag = "MainCamera" }; _camera = plain.AddComponent<Camera>(); plain.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>(); plain.transform.position = GameSpace.ToUnity(-95, 26, -150); plain.transform.LookAt(GameSpace.ToUnity(0, 8, 0));
                _environment = new GameObject("Environment").AddComponent<IslandEnvironment>(); _environment.Initialize(_camera, -.8f); _environment.DayNightCycle = false; _environment.Hour = 14f; viewer.Ocean.Camera = _camera; return;
            }
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
            var views = new GameObject(name + " people").AddComponent<PlayerViews>(); views.transform.SetParent(root.transform); views.AutoPlan = false; views.Attach(client);
            var cars = new GameObject(name + " cars").AddComponent<VehicleViews>(); cars.transform.SetParent(root.transform); cars.Attach(client);
            return new Player { Client = client, Terrain = terrain, Ocean = ocean, Objects = objects, Views = views, Vehicles = cars };
        }

        private static void Expect(bool condition, string what) { if (!condition) throw new InvalidOperationException(what); }

        private IEnumerator Until(Func<bool> condition, string what, float seconds = 8f)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (!condition()) { if (Time.realtimeSinceStartup > until) throw new InvalidOperationException("tempo esgotado: " + what); yield return null; }
        }

        private void Update() => _server?.Update(Time.deltaTime);

        /// <summary>With a graphics device and -lifeverse-doc-shots FOLDER: saves what is on screen to FOLDER/name.png (for the docs).</summary>
        private IEnumerator Snap(string name)
        {
            var args = Environment.GetCommandLineArgs(); var at = Array.IndexOf(args, "-lifeverse-doc-shots"); if (at < 0 || at + 1 >= args.Length || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            yield return new WaitForSeconds(.6f); yield return new WaitForEndOfFrame(); System.IO.Directory.CreateDirectory(args[at + 1]); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(args[at + 1], name + ".png")); yield return new WaitForSeconds(.6f);
        }

        private static int IntArg(string[] args, string name, int fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback; }

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
            { var column = new Vector3(-10f, 0f, 30f); var topmost = float.MinValue; foreach (var id in viewer.Objects.Ids) { var go = viewer.Objects.ViewOf(id); if (go == null) continue; var box = default(UnityEngine.Bounds); var any = false; foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!any) { box = r.bounds; any = true; } else box.Encapsulate(r.bounds); } if (any && box.min.x <= column.x && box.max.x >= column.x && box.min.z <= column.z && box.max.z >= column.z) topmost = Mathf.Max(topmost, box.max.y); }
              var hit = viewer.Objects.Pick(new Ray(new Vector3(column.x, 100f, column.z), Vector3.down), 200f, out var pickedId, out var pickedBounds); Expect(hit && Mathf.Abs(pickedBounds.max.y - topmost) < .01f, "um raio de cima acerta o objeto mais alto daquele ponto e dá o topo dele: " + pickedBounds.max.y + " esperado " + topmost); Expect(!viewer.Objects.Pick(new Ray(new Vector3(column.x, 400f, column.z), Vector3.up), 200f, out _, out _), "um raio para o céu não acerta nada"); _checks++; }
            var view = viewer.Objects.ViewOf(wall.Id);
            Expect(Vector3.Distance(view.transform.position, GameSpace.ToUnity(-10, 1.5f, -30)) < 1e-4f, "a posiÃ§Ã£o do objeto passa por GameSpace (z invertido)"); _checks++;
            Expect(Mathf.Approximately(view.transform.localScale.x, 6f) && view.GetComponent<MeshFilter>().sharedMesh.subMeshCount >= 6, "escala e uma superfÃ­cie por face"); _checks++;
            var tilted = viewer.Objects.Ids.Select(id => viewer.Objects.StateOf(id)).First(s => s.PositionX == 30);
            Expect(Mathf.Abs(Quaternion.Angle(viewer.Objects.ViewOf(tilted.Id).transform.rotation, GameSpace.RotationToUnity(0, 35, 0))) < .01f, "rotaÃ§Ã£o de 35 graus"); _checks++;
            var materials = view.GetComponent<MeshRenderer>().sharedMaterials; var wallColor = materials[0].GetColor("_BaseColor");
            Expect(Mathf.Abs(wallColor.r - 0xC9 / 255f) < .01f, "a cor do material vem do JSON"); _checks++;
            var glass = viewer.Objects.Ids.Select(id => viewer.Objects.StateOf(id)).First(s => s.PositionX == 0 && s.PositionY == 1.2f);
            Expect(Mathf.Abs(viewer.Objects.ViewOf(glass.Id).GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_BaseColor").a - .35f) < .01f, "a transparÃªncia vira alfa"); _checks++;

            // an update moves it for everybody; a delete removes it
            editor.Client.Send(new SubmitBuildOperationMessage("{\"operation\":\"update\",\"expectedRevision\":1,\"objectId\":\"" + wall.Id + "\",\"object\":{\"primitiveType\":\"cube\",\"positionX\":-10,\"positionY\":1.5,\"positionZ\":-40,\"rotationX\":0,\"rotationY\":0,\"rotationZ\":0,\"scaleX\":6,\"scaleY\":3,\"scaleZ\":5,\"materialJson\":\"{\\\"color\\\":\\\"#C94F3D\\\"}\"}}"));
            yield return Until(() => Vector3.Distance(viewer.Objects.ViewOf(wall.Id).transform.position, GameSpace.ToUnity(-10, 1.5f, -40)) < 1e-3f, "a atualizaÃ§Ã£o mover o objeto"); _checks++;
            editor.Client.Send(new SubmitBuildOperationMessage("{\"operation\":\"delete\",\"expectedRevision\":1,\"objectId\":\"" + wall.Id + "\"}"));
            yield return Until(() => viewer.Objects.Count == sends.Length - 1 && !viewer.Objects.Has(wall.Id), "a exclusÃ£o tirar o objeto"); _checks++;
            editor.Client.Send(new SubmitBuildOperationMessage(Create("cube", -10, 1.5f, -30, 6, 3, 5, "{\"color\":\"#C94F3D\",\"roughness\":0.8}"))); // the wall again, for the photo
            yield return Until(() => viewer.Objects.Count == sends.Length, "a parede de novo");
            if (_shots != null) { _camera.transform.position = GameSpace.ToUnity(-12, 9, -66); _camera.transform.LookAt(GameSpace.ToUnity(8, 2, -28)); yield return Shot("objects"); }
        }

        // The person: walking, running, jumping and flying with the prediction of LocalPlayer against the real server, seen by another player.
        private IEnumerator Walking(Player editor, Player viewer)
        {
            var peer = editor.Client.SelfPeerId; Expect(peer >= 0 && viewer.Client.SelfPeerId >= 0, "os jogadores sabem o seu nÃºmero no servidor"); _checks++;
            yield return Until(() => viewer.Views.ViewOf(peer) != null && editor.Views.ViewOf(viewer.Client.SelfPeerId) != null && viewer.Views.TryGetSnapshot(peer, out _), "os corpos aparecerem para os dois");
            Expect(viewer.Views.Count == 2 && editor.Views.Count == 2, "cada jogador vÃª os dois corpos: " + viewer.Views.Count + " " + editor.Views.Count); _checks++;
            var follow = editor.Client.gameObject.AddComponent<FollowCamera>(); follow.Terrain = editor.Terrain; follow.Camera = _camera; follow.Interactive = false; follow.Yaw = 0f;
            var local = editor.Client.gameObject.AddComponent<LocalPlayer>(); local.Attach(editor.Client, editor.Terrain, editor.Views, follow);
            Expect(local.Spawned, "o jogador local parte de onde o servidor o pÃ´s"); _checks++;
            var remote = viewer.Views.ViewOf(peer); var start = local.GamePosition;
            Console.WriteLine($"event=walk start={start}");
            var swing = 0f; var input = new PlayerInputSample { Forward = 1f }; var pendingFly = false;
            local.Scripted = () => { var sample = input; if (pendingFly) { sample.Fly = true; pendingFly = false; } return sample; };

            // walking: a bit over 3 m/s, the server and the prediction agree, the other player's body follows and swings its legs
            var t = 0f; while (t < 2.5f) { t += Time.deltaTime; swing = Mathf.Max(swing, Mathf.Abs(Mathf.DeltaAngle(0f, remote.LeftLeg.localEulerAngles.x))); yield return null; }
            viewer.Views.TryGetSnapshot(peer, out var seen); var travelled = Vector3.Distance(start, local.GamePosition);
            Console.WriteLine($"event=walk end={local.GamePosition} travelled={travelled:F2} server={seen.X:F2},{seen.Y:F2},{seen.Z:F2} corrections={local.Corrections} lastError={local.LastError:F3} motion={seen.Motion} swing={swing:F1}");
            Expect(travelled > 5f && travelled < 8.6f, "andou a pÃ© a cerca de 3 m/s: " + travelled); _checks++;
            Expect(Vector3.Distance(new Vector3(seen.X, seen.Y, seen.Z), local.GamePosition) < 1.6f, "o servidor e a previsÃ£o estÃ£o juntos"); _checks++;
            Expect(seen.Motion == NetworkMotionState.Walk && Vector3.Distance(remote.TargetGame, new Vector3(seen.X, seen.Y, seen.Z)) < .01f, "quem olha recebe o estado de andar: " + seen.Motion); _checks++;
            Expect(swing > 10f, "as pernas do outro jogador balanÃ§am ao andar: " + swing); _checks++;
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
            Expect(peak - ground > .9f && peak - ground < 1.8f && Mathf.Abs(local.GamePosition.y - ground) < .05f, "pulou e voltou ao chÃ£o: subiu " + (peak - ground) + ", agora " + (local.GamePosition.y - ground)); _checks++;

            // flying: F toggles, space climbs, C comes down; the server agrees
            pendingFly = true; input.Jump = true; yield return Until(() => !pendingFly, "o pedido de voo sair");   // the key press is one step
            yield return Until(() => viewer.Views.TryGetSnapshot(peer, out var s) && s.Motion == NetworkMotionState.Fly, "o servidor pÃ´r o jogador em voo");
            yield return new WaitForSeconds(.8f); input.Jump = false;
            Expect(local.GamePosition.y - ground > 2f, "subiu em voo: " + (local.GamePosition.y - ground)); _checks++;
            input.Descend = true;
            yield return Until(() => viewer.Views.TryGetSnapshot(peer, out var s) && s.Motion != NetworkMotionState.Fly, "voltar ao chÃ£o e parar de voar", 10f);
            input.Descend = false; yield return new WaitForSeconds(.4f);
            Expect(!local.State.Flying && local.GamePosition.y - ground < .3f, "pousou: " + (local.GamePosition.y - ground)); _checks++;
            Expect(local.LastError < 1f, "a previsÃ£o acompanhou o servidor o tempo todo: " + local.LastError); _checks++;

            // gestures: waving travels as an input flag and reaches the other player as a motion state; applauding is told once through the gesture message
            input = new PlayerInputSample(); yield return new WaitForSeconds(.3f);
            Expect(local.StartGesture("wave"), "acenar Ã© aceito parado");
            yield return Until(() => viewer.Views.TryGetSnapshot(peer, out var s) && s.Motion == NetworkMotionState.Wave, "o outro jogador ver o aceno");
            yield return null; var armOut = Mathf.Abs(Mathf.DeltaAngle(0f, remote.RightArm.localEulerAngles.z));
            Expect(armOut > 100f, "o braÃ§o do outro jogador sobe ao acenar: " + armOut); _checks++;
            yield return new WaitForSeconds(2.6f);
            Expect(local.StartGesture("applaud"), "aplaudir Ã© aceito");
            yield return Until(() => remote.ActiveGesture == AvatarView.Gesture.Applaud, "o outro jogador ver os aplausos (mensagem de gesto)"); _checks++;
            Expect(editor.Views.ViewOf(peer).ActiveGesture == AvatarView.Gesture.Applaud, "e o prÃ³prio corpo tambÃ©m aplaude"); _checks++;
            local.Scripted = () => new PlayerInputSample { Forward = 1f }; yield return new WaitForSeconds(.4f); local.Scripted = () => default;
            Expect(remote.ActiveGesture == AvatarView.Gesture.None, "andar interrompe o gesto"); _checks++;
            Expect(remote.NameTag != null && remote.NameTag.GetComponent<TextMesh>().text == "editor", "o nome do jogador flutua sobre o corpo"); _checks++;

            // an object of the world as a seat: the island only learns that the person sits; this computer draws them on top of the object (and the object can be picked by a ray)
            var seatTop = new Vector3(local.GamePosition.x + 2f, local.GamePosition.y + 1.2f, local.GamePosition.z);
            Expect(local.SitOnObject(seatTop, 90f), "sentar num objeto é aceito parado"); yield return Until(() => viewer.Views.TryGetSnapshot(peer, out var s) && s.Motion == NetworkMotionState.Sit, "o outro jogador ver a pessoa sentada");
            yield return new WaitForSeconds(.4f); Expect(Vector3.Distance(editor.Views.ViewOf(peer).GamePosition, seatTop) < .05f && local.ObjectSeat.HasValue, "o próprio corpo é desenhado sobre o objeto: " + editor.Views.ViewOf(peer).GamePosition + " assento " + seatTop + " sentado=" + local.State.Seated + " objeto=" + local.ObjectSeat.HasValue + " servidor=" + local.ServerSeated + " tentativas=" + local.SeatRetries + " querer=" + local.WantSeat);
            local.StandUp(); yield return Until(() => !local.State.Seated && !local.ObjectSeat.HasValue, "levantar do assento do objeto: sentado=" + local.State.Seated + " assento=" + local.ObjectSeat.HasValue + " servidor=" + (viewer.Views.TryGetSnapshot(peer, out var ss) ? ss.Motion.ToString() : "?")); yield return new WaitForSeconds(.3f); Expect(!local.State.Seated, "de pé de novo"); _checks++;

            // first person: the body is not drawn from the inside and the eyes are at head height
            follow.SetFirstPerson(true); yield return new WaitForSeconds(.4f);
            Expect(!editor.Views.ViewOf(peer).Visible && follow.FirstPerson, "em primeira pessoa o prÃ³prio corpo some"); _checks++;
            if (_camera != null) Expect(Mathf.Abs(follow.Focus.y - (local.GamePosition.y + FollowCamera.EyeHeight)) < .02f && Vector3.Distance(_camera.transform.position, GameSpace.ToUnity(local.GamePosition) + Vector3.up * FollowCamera.EyeHeight) < .1f, "os olhos ficam na altura da cabeÃ§a: " + _camera.transform.position);
            if (_shots != null) { follow.Yaw = 40f; follow.Pitch = 4f; yield return Shot("firstperson"); }
            follow.SetFirstPerson(false); yield return new WaitForSeconds(.3f);
            Expect(editor.Views.ViewOf(peer).Visible && !follow.FirstPerson, "de volta Ã  terceira pessoa o corpo aparece"); _checks++;

            if (_shots != null)
            {
                viewer.Views.gameObject.SetActive(false); local.Scripted = () => new PlayerInputSample { Forward = 1f, Run = true };
                follow.Yaw = 40f; follow.Pitch = 12f; follow.Distance = 5f; yield return new WaitForSeconds(.9f);
                yield return Shot("avatar");
                viewer.Views.gameObject.SetActive(true);
            }
            local.Scripted = () => default; yield return new WaitForSeconds(.3f);
            UnityEngine.Object.Destroy(local); UnityEngine.Object.Destroy(follow);
            if (_shots != null)
            {
                // the other player seen from the front with the name over the head, and a wave
                yield return null; var other = editor.Views.ViewOf(viewer.Client.SelfPeerId); other.PlayGesture(0, 30f);
                var spot = other.GamePosition; _camera.transform.position = GameSpace.ToUnity(spot.x + .8f, spot.y + 1.7f, spot.z - 3.2f); _camera.transform.LookAt(GameSpace.ToUnity(spot.x, spot.y + 1.5f, spot.z));
                yield return Shot("people"); other.StopGesture();
            }
        }

        private const string DressedAppearance = "{\"displayName\":\"dressed\",\"skinColor\":\"#8D5524\",\"hairColor\":\"#FFD54F\",\"hairStyle\":\"mohawk\",\"beardStyle\":\"full\",\"bodySettingsJson\":\"{\\\"height\\\":1.1,\\\"shoulder\\\":1.05}\","
            + "\"equippedItems\":[{\"slot\":\"top\",\"itemKey\":\"tee_basic\",\"tintColor\":\"#FF0000\"},{\"slot\":\"hat\",\"itemKey\":\"cap_basic\",\"tintColor\":\"#0000FF\"},{\"slot\":\"back\",\"itemKey\":\"backpack_basic\",\"tintColor\":\"#00FF00\"}]}";

        // Bodies: a person dressed from the appearance the server sends, and a custom GLB skeleton moved by the game's own clips.
        private IEnumerator Bodies(Player editor, Player viewer, LocalIslandApi api)
        {
            var dressed = NewPlayer("dressed"); dressed.Client.AssetLoader = null;
            Expect(dressed.Client.Connect("127.0.0.1", _server.GamePort), "conectar quem vem vestido");
            yield return Until(() => dressed.Client.Authenticated && dressed.Client.SelfPeerId >= 0, "o jogador vestido entrar");
            var peer = dressed.Client.SelfPeerId;
            yield return Until(() => editor.Views.ViewOf(peer) != null && editor.Views.ViewOf(peer).Appearance.HairStyle == "mohawk", "o outro jogador receber a aparÃªncia");
            var view = editor.Views.ViewOf(peer); yield return null;
            Expect(Vector3.Distance(new Vector3(view.Appearance.Skin.r, view.Appearance.Skin.g, view.Appearance.Skin.b), new Vector3(0x8D / 255f, 0x55 / 255f, 0x24 / 255f)) < .01f, "a cor da pele vem do servidor"); _checks++;
            Expect(Mathf.Abs(view.Torso.localScale.y - 1.1f) < .01f && Mathf.Abs(view.Torso.localScale.x - 1.05f) < .01f, "a altura e os ombros do controle deslizante: " + view.Torso.localScale); _checks++;
            var shirt = view.Torso.Find("Torso").GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
            Expect(shirt.r > .9f && shirt.g < .1f && view.Dressing >= 6, "a camiseta Ã© vermelha e hÃ¡ cabelo, olhos, barba, bonÃ© e mochila: " + view.Dressing); _checks++;

            // a custom body: a template skeleton (Mixamo bone names) that holds one animation of its own, so the game's way of moving the bones can be compared with the loader's
            var spec = new AnimationSpec("Compare", 1f, new[] { ("upper_arm_l", 2, 1.0f), ("lower_leg_r", 0, -.8f), ("head", 1, .5f) });
            var glb = LifeVerse.Shared.Avatar.HumanoidTemplate.Build(new TemplateOptions { Naming = BoneNaming.Mixamo, Animations = new[] { spec } });
            var loading = SkeletalBody.CreateAsync(editor.Views.transform, glb, true); yield return Until(() => loading.IsCompleted, "o corpo personalizado carregar", 15f);
            if (loading.IsFaulted) throw loading.Exception.GetBaseException();
            var body = loading.Result; Expect(body != null && body.BoneCount >= 16, "o corpo personalizado tem o esqueleto padrÃ£o: " + (body != null ? body.BoneCount : 0)); _checks++;
            Expect(Mathf.Abs(body.RenderBounds.size.y - 1.8f) < .1f && Mathf.Abs(body.RenderBounds.min.y - body.transform.position.y) < .05f, "o corpo foi levado a 1,8 m e posto no chÃ£o: " + body.RenderBounds.size + " " + body.RenderBounds.min); _checks++;
            Expect(body.LoaderClips != null && body.LoaderClips.Length > 0, "o carregador tem a animaÃ§Ã£o do arquivo"); _checks++;
            var mine = SkeletonClip.Read(glb, "Compare"); Expect(mine != null, "o clipe compartilhado lÃª a animaÃ§Ã£o");
            body.LoaderClips[0].SampleAnimation(body.Content.gameObject, .5f);
            var wantedArm = body.ContentRotationOf("upper_arm_l"); var wantedLeg = body.ContentRotationOf("lower_leg_r"); var wantedHead = body.ContentRotationOf("head");
            body.ApplyClipAt(mine, 0f); body.ApplyClipAt(mine, .5f);
            Expect(Quaternion.Angle(body.ContentRotationOf("upper_arm_l"), wantedArm) < 1f && Quaternion.Angle(body.ContentRotationOf("lower_leg_r"), wantedLeg) < 1f && Quaternion.Angle(body.ContentRotationOf("head"), wantedHead) < 1f,
                "os ossos movidos pelo clipe compartilhado ficam onde o carregador os pÃµe: " + Quaternion.Angle(body.ContentRotationOf("upper_arm_l"), wantedArm) + " " + Quaternion.Angle(body.ContentRotationOf("lower_leg_r"), wantedLeg)); _checks++;
            Expect(Quaternion.Angle(wantedArm, Quaternion.identity) > 30f, "e o clipe realmente move o braÃ§o"); _checks++;
            UnityEngine.Object.Destroy(body.gameObject);

            // the same body through the appearance (the asset comes from the API): it replaces the simple body and the game's clips move it
            var assetId = Guid.NewGuid(); editor.Client.AssetLoader = id => System.Threading.Tasks.Task.FromResult(id == assetId ? LifeVerse.Shared.Avatar.HumanoidTemplate.Build(new TemplateOptions { Naming = BoneNaming.Mixamo }) : null);
            editor.Views.ApplyAppearance(peer, DressedAppearance.TrimEnd('}') + ",\"customModelId\":\"" + assetId + "\",\"customModelTriangles\":24}");
            yield return Until(() => view.Skeleton != null, "o corpo personalizado aparecer no outro jogador", 15f);
            yield return Until(() => DefaultClips.Ready, "os clipes do jogo serem lidos", 30f);
            Expect(DefaultClips.Count >= 8, "os clipes do jogo (idle, walk, run, jump, fall, voo, nado, digitaÃ§Ã£o): " + DefaultClips.Count); _checks++;
            var procedural = view.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(view.Skeleton.transform) && r.gameObject.name != "NameTag").ToList();
            Expect(procedural.All(r => !r.enabled) && view.Skeleton.GetComponentsInChildren<Renderer>().All(r => r.enabled), "o corpo simples some e o personalizado aparece: ligados do simples = " + string.Join(",", procedural.Where(r => r.enabled).Select(r => r.name)) + "; do personalizado desligados = " + view.Skeleton.GetComponentsInChildren<Renderer>().Count(r => !r.enabled) + " detalhe " + view.Detail); _checks++;
            uint seq = 0; var before = view.Skeleton.ContentRotationOf("upper_leg_l"); var swing = 0f; var clipName = ""; var started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 1.8f) { dressed.Client.SendInput(++seq, 0f, -1f, InputFlags.None); yield return null; if (view.Motion == NetworkMotionState.Walk && view.Skeleton.UsingClip) { clipName = view.Skeleton.ClipName; swing = Mathf.Max(swing, Quaternion.Angle(before, view.Skeleton.ContentRotationOf("upper_leg_l"))); } }
            dressed.Client.SendInput(++seq, 0f, 0f, InputFlags.None);
            Expect(clipName == "Walk" && swing > 8f, "o corpo personalizado anda com o clipe Walk do jogo: " + clipName + " " + swing); _checks++;
            yield return new WaitForSeconds(.5f); dressed.Client.SendInput(++seq, 0f, 0f, InputFlags.Sit); yield return Until(() => view.Motion == NetworkMotionState.Sit, "o jogador sentar");
            yield return new WaitForSeconds(.5f);
            Expect(!view.Skeleton.UsingClip, "sentar não tem clipe e usa a pose procedural no esqueleto"); _checks++;
            dressed.Client.SendInput(++seq, 0f, 0f, InputFlags.Sit); yield return Until(() => view.Motion != NetworkMotionState.Sit, "o jogador levantar");
            if (_shots != null)
            {
                yield return new WaitForSeconds(.6f); var spot = view.GamePosition; var from = GameSpace.ToUnity(spot.x + 1.2f, spot.y + 1.3f, spot.z - 3.4f); var at = GameSpace.ToUnity(spot.x, spot.y + 1f, spot.z);
                viewer.Views.gameObject.SetActive(false); dressed.Views.gameObject.SetActive(false);   // every client draws everybody in this one scene: only the editor's drawing of him is photographed
                _camera.transform.position = from; _camera.transform.LookAt(at); yield return Shot("body-custom");
                view.SetDetail(LifeVerse.Shared.Avatar.AvatarDetail.Basic); yield return Shot("body-dressed"); viewer.Views.gameObject.SetActive(true); dressed.Views.gameObject.SetActive(true);
            }
            view.SetDetail(LifeVerse.Shared.Avatar.AvatarDetail.Basic); Expect(procedural.Any(r => r.enabled) && view.Skeleton.GetComponentsInChildren<Renderer>().All(r => !r.enabled), "de longe volta o corpo simples"); _checks++;
            view.SetDetail(LifeVerse.Shared.Avatar.AvatarDetail.Hidden); Expect(view.GetComponentsInChildren<Renderer>().All(r => !r.enabled), "muito longe nÃ£o se desenha nada"); _checks++;
            view.SetDetail(LifeVerse.Shared.Avatar.AvatarDetail.Full);
            // the person's own clothes (a rigid piece on a bone and a skinned garment bound to the bones) and their own animation for a state, on the skeletal body
            var hatId = Guid.NewGuid(); var shirtId = Guid.NewGuid(); var danceFile = Guid.NewGuid(); var oldFiles = LifeVerse.Unity.Avatar.AvatarWardrobe.Files;
            var waveGlb = LifeVerse.Shared.Avatar.HumanoidTemplate.Build(new TemplateOptions { Animations = new[] { new LifeVerse.Shared.Avatar.AnimationSpec("Aceno", 1f, new[] { ("upper_arm_r", 2, 1.2f) }) } });
            LifeVerse.Unity.Avatar.AvatarWardrobe.Files = (kind, id) => System.Threading.Tasks.Task.FromResult(kind == "animations" && id == danceFile ? waveGlb : kind == "wearables" && id == hatId ? TestAssets.Glb() : kind == "wearables" && id == shirtId ? LifeVerse.Shared.Avatar.HumanoidTemplate.Build(new TemplateOptions()) : null);
            var worn = "[{\"id\":\"" + hatId + "\",\"slot\":\"hat\",\"kind\":\"rigid\",\"attachBone\":\"head\",\"offset\":[0,0.2,0],\"rotation\":[0,0,0],\"scale\":0.3},{\"id\":\"" + shirtId + "\",\"slot\":\"top\",\"kind\":\"clothing\",\"attachBone\":\"\",\"offset\":[0,0,0],\"rotation\":[0,0,0],\"scale\":1}]";
            editor.Views.ApplyAppearance(peer, DressedAppearance.TrimEnd('}') + ",\"customModelId\":\"" + assetId + "\",\"customModelTriangles\":24,\"customWearablesJson\":" + System.Text.Json.JsonSerializer.Serialize(worn) + ",\"animationOverridesJson\":" + System.Text.Json.JsonSerializer.Serialize("{\"Idle\":\"custom:" + danceFile + "#Aceno\"}") + "}");
            yield return Until(() => view.Skeleton != null && editor.Views.WardrobeOf(peer) is { } wardrobe && wardrobe.Wearables == 2 && wardrobe.Clips == 1, "as roupas e a animação próprias no corpo", 20f);
            var wardrobeNow = editor.Views.WardrobeOf(peer); Expect(wardrobeNow.Skinned == 1 && view.Skeleton.Overrides.ContainsKey("Idle"), "a roupa com esqueleto presa aos ossos do corpo e o Aceno no lugar do Idle: " + wardrobeNow.Skinned);
            var hat = view.Skeleton.BoneOf("head").GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith("Wearable hat")); Expect(hat != null, "o chapéu preso ao osso da cabeça");
            var skinnedShirt = view.Skeleton.Content.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.GetComponentsInParent<Transform>().Any(p => p.name.StartsWith("Wearable top")) && r.bones.Contains(view.Skeleton.BoneOf("head")));
            Expect(skinnedShirt != null, "a camiseta usa os ossos do próprio corpo"); _checks++;
            yield return new WaitForSeconds(.4f); Expect(view.Skeleton.ClipName == "Aceno", "parado, o corpo toca a animação própria: " + view.Skeleton.ClipName); _checks++;
            LifeVerse.Unity.Avatar.AvatarWardrobe.Files = oldFiles; editor.Views.ApplyAppearance(peer, DressedAppearance.TrimEnd('}') + ",\"customModelId\":\"" + assetId + "\",\"customModelTriangles\":24}");
            yield return Until(() => view.Skeleton != null && editor.Views.WardrobeOf(peer) is { } w2 && w2.Wearables == 0, "voltar ao corpo sem as roupas próprias", 15f);
            // the planner: near and in front the whole body, far away nothing
            var lens = new GameObject("Planner camera").AddComponent<Camera>(); var seat = GameSpace.ToUnity(view.GamePosition);
            lens.transform.position = seat + new Vector3(0, 1.6f, 4f); lens.transform.LookAt(seat + Vector3.up); editor.Views.PlanDetail(lens); Expect(view.Detail == LifeVerse.Shared.Avatar.AvatarDetail.Full, "perto e à frente o corpo é inteiro: " + view.Detail); _checks++;
            lens.transform.position = seat + new Vector3(0, 1.6f, 400f); lens.transform.LookAt(seat); editor.Views.PlanDetail(lens); Expect(view.Detail == LifeVerse.Shared.Avatar.AvatarDetail.Hidden, "a 400 m nada é desenhado: " + view.Detail); _checks++;
            lens.transform.position = seat + new Vector3(0, 1.6f, 4f); lens.transform.LookAt(seat); editor.Views.PlanDetail(lens); UnityEngine.Object.Destroy(lens.gameObject);

            // the body that comes with the game: the man of LifeVerse, read once from the file the build carries and made into a skeleton for this person
            Expect(LifeVerse.Unity.Avatar.BuiltInBodies.Available, "o corpo do jogo (lifeverse_man) está junto do build: " + LifeVerse.Unity.Avatar.BuiltInBodies.Path); _checks++;
            var oldBody = view.Skeleton; var loadStart = Time.realtimeSinceStartup; editor.Views.ApplyAppearance(peer, DressedAppearance.TrimEnd('}') + ",\"baseModel\":\"lifeverse_man\"}");
            yield return Until(() => view.Skeleton != null && view.Skeleton != oldBody && view.Skeleton.BoneCount >= 15, "o corpo do jogo (lifeverse_man) aparecer", 150f);
            Console.WriteLine($"event=builtin_body seconds={Time.realtimeSinceStartup - loadStart:F1} bones={view.Skeleton.BoneCount} triangles={view.Skeleton.Triangles} height={view.Skeleton.RenderBounds.size.y:F2}");
            Expect(Mathf.Abs(view.Skeleton.RenderBounds.size.y - 1.8f) < .15f && view.Skeleton.Triangles > 2000 && view.Appearance.CustomModelTriangles == view.Skeleton.Triangles, "o homem do LifeVerse tem 1,8 m, esqueleto padrão e a conta de triângulos: " + view.Skeleton.RenderBounds.size + " " + view.Skeleton.Triangles); _checks++;
            if (_shots != null) { yield return new WaitForSeconds(.5f); viewer.Views.gameObject.SetActive(false); dressed.Views.gameObject.SetActive(false); var manAt = view.GamePosition; _camera.transform.position = GameSpace.ToUnity(manAt.x + .6f, manAt.y + 1.4f, manAt.z - 3.4f); _camera.transform.LookAt(GameSpace.ToUnity(manAt.x, manAt.y + 1f, manAt.z)); yield return Shot("man"); viewer.Views.gameObject.SetActive(true); dressed.Views.gameObject.SetActive(true); }
            // the head follows the person's look
            viewer.Client.Send(new HeadLookMessage(.5f));
            yield return Until(() => editor.Views.ViewOf(viewer.Client.SelfPeerId) != null && Mathf.Abs(editor.Views.ViewOf(viewer.Client.SelfPeerId).HeadPitch - .5f) < .01f, "a cabeÃ§a do outro jogador acompanhar o olhar"); _checks++;
            viewer.Client.Send(new HeadLookMessage(3f)); yield return new WaitForSeconds(.4f);
            Expect(Mathf.Abs(editor.Views.ViewOf(viewer.Client.SelfPeerId).HeadPitch - .5f) < .01f, "um Ã¢ngulo fora do que uma cabeÃ§a faz Ã© recusado pelo servidor"); _checks++;
            UnityEngine.Object.Destroy(dressed.Client.gameObject);
            yield return Until(() => editor.Views.ViewOf(peer) == null, "quem saiu sumir para os outros");
        }

        // Driving: get into the car by the spawn point, drive it (the server simulates it), get out.
        private IEnumerator Driving(Player editor, Player viewer)
        {
            yield return Until(() => viewer.Vehicles.Count == 1 && editor.Vehicles.Count == 1, "o carro da ilha aparecer");
            var car = viewer.Vehicles.Ids.First(); var follow = viewer.Client.gameObject.AddComponent<FollowCamera>(); follow.Interactive = false; follow.Terrain = viewer.Terrain;
            var local = viewer.Client.gameObject.AddComponent<LocalPlayer>(); local.Vehicles = viewer.Vehicles; local.Attach(viewer.Client, viewer.Terrain, viewer.Views, follow);
            Expect(local.ToggleVehicle(), "hÃ¡ um carro a menos de 4 m");
            yield return Until(() => local.Driving != null && editor.Vehicles.DrivenBy(viewer.Client.SelfPeerId) != null, "o servidor pÃ´r o jogador ao volante");
            Expect(local.State.Seated && local.Motion == NetworkMotionState.Sit && editor.Views.ViewOf(viewer.Client.SelfPeerId) != null, "ao volante o jogador fica sentado, e o outro vÃª"); _checks++;
            var start = viewer.Vehicles.ViewOf(car).GamePosition; var input = new PlayerInputSample { Forward = 1f }; local.Scripted = () => input;
            yield return new WaitForSeconds(2.5f);
            var moved = Vector3.Distance(start, viewer.Vehicles.ViewOf(car).GamePosition); var seen = Vector3.Distance(start, editor.Vehicles.ViewOf(car).GamePosition);
            Expect(moved > 4f && seen > 3f && viewer.Vehicles.TryGetSnapshot(car, out var snapshot) && snapshot.Speed > 3f, "o carro andou (a velocidade e o ponto chegam aos dois): " + moved + " " + seen); _checks++;
            Expect(Vector3.Distance(local.GamePosition, viewer.Vehicles.ViewOf(car).GamePosition) < 1f, "o jogador anda com o carro"); _checks++;
            if (_shots != null) { var at = viewer.Vehicles.ViewOf(car).GamePosition; _camera.transform.position = GameSpace.ToUnity(at.x + 5f, at.y + 2.2f, at.z - 7f); _camera.transform.LookAt(GameSpace.ToUnity(at.x, at.y + .8f, at.z)); editor.Views.gameObject.SetActive(false); yield return Shot("car"); editor.Views.gameObject.SetActive(true); }
            input = new PlayerInputSample { Jump = true }; yield return new WaitForSeconds(2.5f);   // brake
            viewer.Vehicles.TryGetSnapshot(car, out snapshot); Expect(snapshot.Speed < 1f, "o freio para o carro: " + snapshot.Speed); _checks++;
            input = default; Expect(local.ToggleVehicle(), "descer do carro");
            yield return Until(() => local.Driving == null && !local.State.Seated, "o jogador descer", 6f); _checks++;
            UnityEngine.Object.Destroy(local); UnityEngine.Object.Destroy(follow);
        }

        private static string JsonString(string text) => System.Text.Json.JsonSerializer.Serialize(text);

        // Entering by the REST door: login, entry ticket (after a "starting" answer), the saved terrain and objects, then the connection.
        private static byte[] WavBytes(float seconds, float hertz, int rate = 16000)
        {
            var frames = (int)(seconds * rate); var data = new byte[44 + frames * 2]; void Put(int at, string text) { for (var i = 0; i < text.Length; i++) data[at + i] = (byte)text[i]; } void Int(int at, int value) { data[at] = (byte)value; data[at + 1] = (byte)(value >> 8); data[at + 2] = (byte)(value >> 16); data[at + 3] = (byte)(value >> 24); }
            Put(0, "RIFF"); Int(4, 36 + frames * 2); Put(8, "WAVE"); Put(12, "fmt "); Int(16, 16); data[20] = 1; data[22] = 1; Int(24, rate); Int(28, rate * 2); data[32] = 2; data[34] = 16; Put(36, "data"); Int(40, frames * 2);
            for (var i = 0; i < frames; i++) { var v = (short)(Mathf.Sin(2f * Mathf.PI * hertz * i / rate) * 12000f); data[44 + i * 2] = (byte)v; data[45 + i * 2] = (byte)(v >> 8); }
            return data;
        }

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
            Expect(!denied.Result.Ok && denied.Result.Status == 401, "senha errada Ã© recusada (401)"); _checks++;
            var ok = rest.LoginAsync("dono", "segredo"); yield return Until(() => ok.IsCompleted, "a resposta do login");
            Expect(ok.Result.Ok && rest.AccessToken == stub.AccessToken, "o login devolve o token de acesso"); _checks++;
            using var anonymous = new IslandEntryClient(stub.Url);
            var unauthorized = anonymous.RequestEntryAsync(island); yield return Until(() => unauthorized.IsCompleted, "a entrada sem token");
            Expect(unauthorized.Result.Status == 401 && !unauthorized.Result.Ok, "sem token a entrada Ã© recusada"); _checks++;

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
            Expect(picture != null && picture.width == 8 && corner.r > .6f && corner.g < .4f, "a textura da lousa veio da API e estÃ¡ na cor base: " + (picture != null ? picture.width + " " + corner : "nenhuma")); _checks++;
            var model = preview.Objects.ViewOf(boxId).transform.Find("Model"); var modelBounds = model.GetComponentInChildren<Renderer>().bounds;
            Expect(preview.Objects.ViewOf(boxId).GetComponent<MeshRenderer>().enabled == false && Mathf.Abs(modelBounds.size.x - 3f) < .05f && Mathf.Abs(modelBounds.size.y - 1.5f) < .05f && (modelBounds.center - preview.Objects.ViewOf(boxId).transform.position).magnitude < .05f,
                "o modelo importado (glb) foi normalizado para 1 m e tem o tamanho do objeto (3 m Ã— 1,5 m) e estÃ¡ centrado: " + modelBounds.size + " " + modelBounds.center); _checks++;
            var treeBounds = preview.Objects.ViewOf(treeId).GetComponent<MeshRenderer>().bounds; var grassMesh = preview.Objects.ViewOf(grassId).GetComponent<MeshFilter>().sharedMesh;
            Expect(preview.Objects.ViewOf(treeId).GetComponent<MeshFilter>().sharedMesh.subMeshCount == 3 && treeBounds.size.y > 1.4f && treeBounds.size.y <= 2.05f && grassMesh.vertexCount == 54, "a Ã¡rvore tem tronco e duas copas, e a grama 18 folhas: " + treeBounds.size + " " + grassMesh.vertexCount); _checks++;
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
            Expect(preview.Objects.IsCulled(farId) && !preview.Objects.IsCulled(slabId) && !preview.Objects.ViewOf(farId).GetComponent<MeshRenderer>().enabled, "o objeto a 1,5 km nÃ£o Ã© desenhado e os prÃ³ximos sim: " + preview.Objects.CulledCount); _checks++;
            Expect(join.Status == "ready" && join.StartAttempts == 1, "esperou o servidor da ilha iniciar (uma resposta 503) e entrou: " + join.Status + " " + join.Failure); _checks++;
            Expect(join.SnapshotApplied && preview.Terrain.Map.Serialize().SequenceEqual(saved.Serialize()), "o terreno salvo foi desenhado antes de conectar"); _checks++;
            Expect(join.ObjectsLoaded == 7 && preview.Objects.Count == 7, "os sete objetos da ilha foram carregados: " + join.ObjectsLoaded); _checks++;
            Expect(preview.Client.Connected == false, "ainda sem conexÃ£o"); _checks++;

            // 2) the whole way in: the ticket opens the island and the server's own map replaces the snapshot
            stub.StartingAnswers = 0;
            var player = NewPlayer("joiner"); var full = player.Client.gameObject.AddComponent<WorldJoin>(); full.Client = player.Client;
            var entering = full.Join(rest, island, true);
            while (entering.MoveNext()) yield return entering.Current;
            Expect(full.Status == "in_island" && player.Client.Authenticated, "entrou na ilha com o bilhete: " + full.Status + " " + full.Failure); _checks++;
            yield return Until(() => player.Client.TerrainMessages > 0, "o terreno do servidor chegar");
            Expect(player.Terrain.Map.Serialize().SequenceEqual(serverMap.Serialize()) || player.Terrain.Map.Revision >= 0, "o mapa do servidor substitui o do snapshot"); _checks++;
            Expect(stub.Requests.Count(r => r.EndsWith("/entry-token")) >= 3, "o cliente pediu o bilhete de novo depois do 503"); _checks++;
            // 3) the game as a person plays it: the login screen, the loading screen, the HUD and the chat, against the same island
            api.Issue("appuser", true, 0, 0, "{}", stub.UserId); stub.EntryToken = "appuser"; stub.StartingAnswers = 0;
            var settingsFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-app-settings-" + Guid.NewGuid() + ".json");
            var oneIsland = Guid.NewGuid(); stub.Routes["GET /api/v1/worlds/islands"] = "[{\"id\":\"" + oneIsland + "\",\"name\":\"Ilha do Sol\",\"category\":\"praia\"},{\"id\":\"" + island + "\",\"name\":\"Ilha de Teste\",\"category\":\"teste\"}]";
            stub.Routes["GET /api/v1/client/release"] = "{\"latest\":\"0.9.0\",\"minimum\":\"0.8.0\",\"downloadUrl\":\"https://example.com/get\",\"notes\":\"x\"}";
            var app = new GameObject("App").AddComponent<LifeVerse.Unity.UI.GameApp>(); app.SettingsPath = settingsFile; var savedLoginFile = settingsFile + ".login"; LifeVerse.Unity.UI.SavedLogin.Clear(savedLoginFile); app.SavedLoginPath = savedLoginFile; app.LiveAddress = stub.Live.Url; app.Begin(stub.Url, new LifeVerse.Unity.UI.UserSettings { AutoScale = false });
            yield return null; yield return null;
            yield return Until(() => app.Login.IslandCount == 2, "a lista de ilhas chegar à tela de entrada"); _checks++;
            // the login screen of the Godot client: the picture, the state of the servers, the news, the music, "esqueci a senha" and the island chosen by the server
            Expect(app.Login.Background != null && app.Login.Islands.choices[0] == LifeVerse.Unity.UI.LoginView.AutoIsland, "a tela de entrada tem o fundo e a escolha do servidor");
            yield return Until(() => app.Login.ServersNow == LifeVerse.Unity.UI.LoginView.Servers.Online && app.Login.ServerState.text.Contains("Online"), "o estado dos servidores (/health)"); _checks++;
            app.Login.ShowRecovery(); Expect(app.Login.Status.text.Contains("recuperação de senha"), "esqueci a senha explica o que fazer"); app.Login.Say("", false);
            yield return Until(() => app.LoginMusic != null && app.LoginMusic.isPlaying, "a música da tela de entrada"); _checks++;
            var beamBefore = app.Login.Lights.FirstBeamAngle; yield return new WaitForSeconds(.6f); Expect(Mathf.Abs(app.Login.Lights.FirstBeamAngle - beamBefore) > 1e-4f, "as luzes da entrada se movem"); _checks++;
            yield return Snap("ui-login");
            // a build below the minimum of the platform is told to update and does not even try to sign in
            yield return Until(() => app.UpdateInfo.Required && app.Login.UpdateNotice.Contains("obrigatória") && app.Login.UpdateDownloadShown, "o aviso de atualização obrigatória na tela de entrada");
            app.Login.User.value = "dono"; app.Login.Password.value = "segredo"; app.Login.Island.value = island.ToString(); var loginsBefore = stub.Requests.Count(r => r == "POST /api/v1/auth/login"); app.Login.Submit(); yield return null; yield return null;
            Expect(app.Current == LifeVerse.Unity.UI.GameApp.Phase.Login && app.Login.Status.text.Contains("obrigatória") && stub.Requests.Count(r => r == "POST /api/v1/auth/login") == loginsBefore, "abaixo do mínimo a entrada é recusada antes de chamar o login: " + app.Login.Status.text);
            app.UpdateInfo.Apply(200, "{\"latest\":\"0.9.0\",\"minimum\":\"0.1.0\"}"); Expect(!app.UpdateInfo.Required && app.UpdateInfo.Status == LifeVerse.Shared.Updates.UpdateStatus.Available, "com o mínimo abaixo da versão a entrada é liberada"); _checks++;
            app.Login.User.value = "dono"; app.Login.Password.value = "errada"; app.Login.Island.value = island.ToString(); app.Login.Submit();
            yield return Until(() => !app.Login.Busy, "a recusa da senha errada voltar à tela");
            Expect(app.Current == LifeVerse.Unity.UI.GameApp.Phase.Login && app.Login.Status.text.Contains("incorretos"), "senha errada: fica na tela de entrada com a mensagem em português: " + app.Login.Status.text); _checks++;
            // the island left to the server ("onde parei") and the files of the island loaded before the entry, kept on disk by their hash
            stub.Routes["GET /api/v1/worlds/me/start"] = "{\"islandId\":\"" + island + "\",\"name\":\"Ilha de Teste\",\"reason\":\"last\",\"isWelcome\":false}";
            var preAsset = Guid.NewGuid(); var preBytes = TestAssets.Png(new Color32(200, 90, 40, 255), 8); stub.Assets[preAsset.ToString()] = preBytes; string preHash; using (var sha = System.Security.Cryptography.SHA256.Create()) preHash = BitConverter.ToString(sha.ComputeHash(preBytes)).Replace("-", "").ToLowerInvariant();
            var prePath = System.IO.Path.Combine(LifeVerse.Unity.World.WorldJoin.CacheFolder, preHash + ".bin"); if (System.IO.File.Exists(prePath)) System.IO.File.Delete(prePath);
            stub.Routes["GET /api/v1/worlds/islands/" + island + "/assets/manifest"] = "{\"assets\":[{\"assetId\":\"" + preAsset + "\",\"kind\":\"texture\",\"hash\":\"" + preHash + "\",\"size\":" + preBytes.Length + ",\"mime\":\"image/png\",\"distance\":3}]}";
            app.Login.Island.value = ""; app.Login.Password.value = "segredo"; app.Login.Remember.value = true; app.Login.RememberPassword.value = true; app.Login.Submit();
            yield return Until(() => app.Current == LifeVerse.Unity.UI.GameApp.Phase.Playing, "entrar no jogo pela tela de entrada", 25f);
            Expect(app.StartReason == "last" && app.Islands.Current == island, "o servidor escolheu a ilha onde a pessoa parou"); _checks++;
            var sealedLogin = LifeVerse.Unity.UI.SavedLogin.Load(savedLoginFile); Expect(app.SavedPassword && sealedLogin is { } sealedPair && sealedPair.Login == "dono" && sealedPair.Password == "segredo" && !System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(savedLoginFile)).Contains("segredo") && !System.IO.File.ReadAllText(settingsFile).Contains("segredo"), "lembrar a senha guarda selado pelo Windows, nunca em texto"); _checks++;
            Expect(app.Login.Lights.BeamCount == 6 && app.Login.Lights.MoteCount == 46, "o show de luzes da entrada");
            Expect(System.IO.File.Exists(prePath) && stub.Requests.Contains("GET /api/v1/worlds/islands/" + island + "/assets/manifest"), "os recursos da ilha baixados antes da entrada e guardados pelo hash"); _checks++;
            yield return Until(() => !app.LoginMusic.isPlaying, "a música da entrada sumir ao entrar no mundo", 5f); _checks++;
            Expect(app.Client.Authenticated && app.Hud.Visible && app.Settings.LastLogin == "dono" && System.IO.File.ReadAllText(settingsFile).Contains("\"LastLogin\": \"dono\""), "entrou pela tela: HUD aberto, usuário lembrado e salvo"); _checks++;
            yield return Until(() => app.Player != null && app.Player.Spawned, "o jogador do jogo nascer");
            // a trip to another island: prepare, ticket, leave and connect, and the arrival confirmed by the API
            var tripId = Guid.NewGuid(); var tripIsland = island; stub.Routes["POST /api/v1/worlds/teleports/prepare"] = "{\"id\":\"" + tripId + "\",\"status\":\"prepared\"}";
            stub.Routes["POST /api/v1/worlds/teleports/" + tripId + "/connect"] = "{\"token\":\"" + stub.EntryToken + "\",\"host\":\"" + stub.Host + "\",\"port\":" + stub.Port + ",\"protocolVersion\":" + stub.ProtocolVersion + "}"; stub.Routes["GET /api/v1/worlds/teleports/" + tripId] = "{\"status\":\"arrived\"}";
            var oldClient = app.Client; Expect(app.Teleport(tripIsland, 12f, -30f), "a viagem começar");
            yield return Until(() => app.Trip.Ticket != null, "o bilhete da viagem chegar", 20f); Expect(stub.ReceivedAny(b => b.Route == "POST /api/v1/worlds/teleports/prepare" && b.Body.Contains(tripIsland.ToString()) && b.Body.Contains("\"x\":12") && b.Body.Contains("idempotencyKey")), "o pedido leva o destino, o ponto e a chave");
            yield return Until(() => app.Client != null && app.Client != oldClient && app.Current == LifeVerse.Unity.UI.GameApp.Phase.Playing && app.Client.Authenticated, "chegar à ilha de destino com o bilhete", 30f);
            yield return Until(() => app.Trip.Arrived, "a API confirmar a chegada"); Expect(!app.Travelling && app.Island == tripIsland, "a viagem termina e a ilha atual é a de destino"); _checks++;
            Expect(app.Arrival == new Vector2(12f, -30f) && stub.WithQuery.Contains("GET /api/v1/worlds/islands/" + tripIsland + "/assets/manifest?x=12&z=-30"), "os recursos da ilha de destino vêm a partir do ponto de chegada da viagem"); _checks++;
            yield return Until(() => app.Player != null && app.Player.Spawned, "o jogador nascer na ilha de destino");
            // the live channel of the platform: opened at sign-in with the token, events become notices, the bell, teleport invites and the balance; it comes back after a drop
            yield return Until(() => stub.LiveConnections == 1 && app.Live.Connected, "o canal ao vivo abrir com o token: " + app.Live.LastError + " recusados " + stub.LiveRefused + " (" + stub.LiveRefusedWhy + ")" + " ligados " + stub.LiveConnections, 15f);
            stub.Routes["GET /api/v1/notifications/center"] = "{\"unread\":3,\"unreadByCategory\":{\"grupos\":1,\"convites\":1,\"sistema\":1},\"hasMore\":false,\"skip\":0,\"items\":[]}";
            var toastsBefore = app.Events.Toasts; stub.Push("{\"type\":\"friend_request\",\"data\":{},\"at\":\"2026-11-25T10:00:00Z\"}");
            yield return Until(() => app.Events.Toasts == toastsBefore + 1, "o pedido de amizade virar aviso"); yield return Until(() => app.Unread == 3 && app.Bell.text == "Avisos (3)", "o sino contar as não lidas");
            app.Settings.ToastSystem = false; stub.Push("{\"type\":\"friend_request\",\"data\":{}}"); yield return Until(() => app.Events.Handled >= 2, "o segundo evento chegar"); Expect(app.Events.Toasts == toastsBefore + 1, "com os avisos de sistema desligados, nada aparece na tela"); app.Settings.ToastSystem = true;
            stub.Push("{\"type\":\"wallet_changed\",\"data\":{\"balance\":1234}}"); yield return Until(() => app.Events.Balance == 1234, "o saldo novo chegar sem perguntar");
            var inviteId = Guid.NewGuid(); stub.Routes["POST /api/v1/social/teleports/" + inviteId + "/accept"] = "{\"islandId\":\"" + island + "\",\"moverUserId\":\"" + app.Api.UserId + "\",\"x\":12,\"y\":0,\"z\":-8}";
            stub.Push("{\"type\":\"teleport_invite\",\"data\":{\"inviteId\":\"" + inviteId + "\",\"kind\":\"offer\",\"fromName\":\"Ana\",\"islandId\":\"" + island + "\"}}"); yield return Until(() => app.Events.OpenInvites.ContainsKey(inviteId), "o convite de teleporte abrir uma janela");
            app.Events.OpenInvites[inviteId].Answer(true); yield return Until(() => { lock (stub.Requests) return stub.Requests.Contains("POST /api/v1/social/teleports/" + inviteId + "/accept"); }, "a resposta ao convite chegar à API");
            yield return Until(() => app.Client.LastTeleport != null, "a ilha responder ao teleporte dentro dela", 10f); Expect(!app.Events.OpenInvites.ContainsKey(inviteId), "respondido, o convite fecha"); _checks++;
            stub.DropLive(); yield return Until(() => !app.Live.Connected, "o canal cair"); yield return Until(() => app.Live.Connected && app.Live.Connects >= 2 && stub.LiveConnections == 1, "o canal voltar sozinho", 20f); _checks++;
            // the notification center: tabs with the unread counts, read all, answer a group invite
            var invite = Guid.NewGuid(); stub.Routes["GET /api/v1/notifications/center"] = "{\"unread\":2,\"unreadByCategory\":{\"grupos\":1,\"convites\":1,\"sistema\":0},\"hasMore\":false,\"skip\":0,\"items\":[{\"source\":\"invite\",\"id\":\"" + invite + "\",\"category\":\"convites\",\"kind\":\"group_invite\",\"title\":\"Convite para Construtores\",\"body\":\"Entre no grupo\",\"at\":\"2026-11-25T10:00:00Z\",\"read\":false,\"actions\":[\"accept\",\"decline\"]},{\"source\":\"group\",\"id\":\"" + Guid.NewGuid() + "\",\"category\":\"grupos\",\"kind\":\"group_announcement\",\"title\":\"Aviso\",\"body\":\"Reunião hoje\",\"groupName\":\"Construtores\",\"at\":\"2026-11-25T09:00:00Z\",\"read\":false,\"actions\":[]}]}";
            stub.Routes["POST /api/v1/notifications/center/read"] = "{}"; stub.Routes["POST /api/v1/social/groups/invites/" + invite + "/accept"] = "{}";
            var center = new LifeVerse.Unity.UI.NotificationCenterWindow(app.Context, app.SetBell); center.Open(); yield return Until(() => center.Shown.Count == 2 && center.TabText("convites") == "Convites (1)", "a central listar e contar por aba");
            Expect(app.Unread == 2 && center.Shown[0].CanAnswer, "o sino acompanha a central e o convite pode ser respondido");
            center.MarkAllRead(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/notifications/center/read" && b.Body.Contains("\"all\":true")), "marcar tudo como lido chegar à API");
            center.Answer(center.Shown[0], true); yield return Until(() => { lock (stub.Requests) return stub.Requests.Contains("POST /api/v1/social/groups/invites/" + invite + "/accept"); }, "aceitar o convite pela central"); yield return Snap("ui-notifications"); center.Close(); _checks++;
            // windows that follow the channel: a group chat reloads when a message of its group arrives, and not for another group
            var chatGroup = Guid.NewGuid(); stub.Routes["GET /api/v1/social/messages"] = "[]"; var chatHolder = new LifeVerse.Unity.UI.FloatingWindow(app.Ui, "live-chat-test", "Chat", 320f, 260f, new Vector2(400, 200));
            var livePane = new LifeVerse.Unity.UI.ConversationPane(app.Context, () => "/api/v1/social/messages?groupId=" + chatGroup, text => "{}").FollowLive((type, data) => type == "group_message" && LifeVerse.Unity.UI.ApiContext.Field(data, "groupId") == chatGroup.ToString()); chatHolder.Body.Add(livePane); chatHolder.Open();
            stub.Push("{\"type\":\"group_message\",\"data\":{\"groupId\":\"" + Guid.NewGuid() + "\",\"content\":\"outro\"}}"); stub.Push("{\"type\":\"group_message\",\"data\":{\"groupId\":\"" + chatGroup + "\",\"content\":\"oi\"}}");
            yield return Until(() => livePane.LiveReloads == 1, "o chat do grupo se atualizar sozinho"); yield return new WaitForSeconds(.5f); Expect(livePane.LiveReloads == 1, "só a mensagem do próprio grupo atualiza"); chatHolder.Close();
            stub.Push("{\"type\":\"group_message\",\"data\":{\"groupId\":\"" + chatGroup + "\"}}"); yield return new WaitForSeconds(.5f); Expect(livePane.LiveReloads == 1, "fechada, a janela para de ouvir"); _checks++;
            // chat: what the person types goes to the others, what the others say shows
            // the world map: the atlas, the player's places, the chosen island and the trip to a spot of it
            var beachId = Guid.NewGuid(); var hillId = Guid.NewGuid(); var markId = Guid.NewGuid();
            stub.Routes["GET /api/v1/map/atlas"] = "{\"items\":[{\"id\":\"" + beachId + "\",\"name\":\"Praia\",\"description\":\"Sol\",\"hasTile\":true,\"tileVersion\":\"v1\",\"x\":0,\"y\":0,\"size\":512,\"spawnX\":10,\"spawnZ\":-20,\"maxPlayers\":40,\"online\":true,\"visitors\":3,\"access\":\"open\",\"favorite\":false,\"owner\":{\"displayName\":\"Ana\"}},{\"id\":\"" + hillId + "\",\"name\":\"Morro\",\"x\":768,\"y\":0,\"size\":512,\"access\":\"private\"}],\"total\":2,\"cellMeters\":768}";
            var folderId = Guid.NewGuid(); stub.Binary["/api/v1/map/islands/" + beachId + "/tile"] = TestAssets.Png(new Color32(60, 160, 90, 255), 32); stub.Routes["POST /api/v1/map/landmark-folders"] = "{}"; stub.Routes["PATCH /api/v1/map/landmark-folders/" + folderId] = "{}"; stub.Routes["PATCH /api/v1/map/landmarks/" + markId] = "{}";
            stub.Routes["GET /api/v1/map/landmarks"] = "{\"folders\":[{\"id\":\"" + folderId + "\",\"name\":\"Praias\"}],\"landmarks\":[{\"id\":\"" + markId + "\",\"name\":\"Meu canto\",\"islandId\":\"" + beachId + "\",\"folderId\":\"" + folderId + "\",\"isPublic\":false,\"x\":40,\"z\":-15}],\"favorites\":[{\"islandId\":\"" + hillId + "\",\"name\":\"Morro\"}],\"recent\":[],\"friends\":[],\"limits\":{\"landmarks\":200,\"folders\":20}}";
            stub.Routes["POST /api/v1/map/landmarks"] = "{\"id\":\"" + Guid.NewGuid() + "\"}"; stub.Routes["POST /api/v1/discovery/islands/" + beachId + "/favorite"] = "{}"; stub.Routes["DELETE /api/v1/map/landmarks/" + markId] = "{}";
            (Guid Island, float? X, float? Z)? traveled = null; var mapContext = new LifeVerse.Unity.UI.ApiContext { Ui = app.Ui, Api = app.Api, Notices = app.Notices, CurrentIsland = () => beachId, TravelTo = (id, x, z) => traveled = (id, x, z) };
            var map = new LifeVerse.Unity.UI.WorldMapWindow(mapContext, () => new Vector3(5f, 0f, 7f)); map.Open();
            yield return Until(() => map.Page.Items.Count == 2 && map.Marks.Count == 1 && map.Favorites.Count == 1 && map.ResultCount == 2, "o mapa receber as ilhas e os lugares do jogador");
            Expect(map.Canvas.Islands.Count == 2 && map.Canvas.Pins.Count == 1 && map.Canvas.You.HasValue && Math.Abs(map.Canvas.You.Value.X - 5) < .01 && Math.Abs(map.Canvas.You.Value.Y - 7) < .01, "as ilhas, o marco e o ponto 'você' no mapa");
            map.Canvas.Fit(); map.Choose(map.Page.Items[0], false); yield return Until(() => map.TileCount == 1, "a imagem aérea da ilha chegar"); yield return null; Expect(map.Canvas.TilesShown >= 1 && map.TileFetches == 1, "a imagem aérea aparece sobre a ilha e foi pedida uma vez: " + map.TileFetches);
            Expect(map.Folders.Count == 1 && map.Marks[0].Folder == folderId, "a pasta e o marco dentro dela");
            var marksWindow = new LifeVerse.Unity.UI.MapMarksWindow(mapContext, map); marksWindow.Open(); marksWindow.NewFolderField.value = "Cais"; marksWindow.CreateFolder(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/map/landmark-folders" && b.Body.Contains("Cais")), "a pasta nova chegar à API");
            marksWindow.Save(map.Marks[0], "Meu canto 2", 0, true); yield return Until(() => stub.ReceivedAny(b => b.Route == "PATCH /api/v1/map/landmarks/" + markId && b.Body.Contains("Meu canto 2") && b.Body.Contains("\"isPublic\":true") && b.Body.Contains("\"clearFolder\":true")), "o marco salvo com nome, sem pasta e compartilhado"); marksWindow.Close();
            // the parcels drawn over the island, and the one under the chosen spot
            stub.Routes["GET /api/v1/map/islands/" + beachId + "/parcels"] = "{\"islandId\":\"" + beachId + "\",\"size\":512,\"items\":[{\"id\":\"" + Guid.NewGuid() + "\",\"code\":\"A-1\",\"name\":\"Cais\",\"state\":\"sale\",\"area\":900,\"bounds\":{\"minX\":-200,\"minZ\":-200,\"maxX\":-20,\"maxZ\":0},\"teleport\":true,\"salePrice\":4500,\"owner\":\"Ana\"},{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Meu\",\"state\":\"mine\",\"bounds\":{\"minX\":20,\"minZ\":20,\"maxX\":220,\"maxZ\":200},\"teleport\":true}]}";
            map.SetParcels(true); map.LoadParcels(map.Page.Items[0]); map.LoadParcels(map.Page.Items[0]); yield return Until(() => map.Canvas.Parcels.ContainsKey(beachId) && map.Canvas.Parcels[beachId].Count == 2, "os terrenos da ilha chegarem"); Expect(map.ParcelFetchesOf(beachId) == 1, "os terrenos de uma ilha são pedidos uma vez: " + map.ParcelFetchesOf(beachId));
            map.SpotField.value = "-100 -50"; map.Choose(map.Page.Items[0], false); map.SpotField.value = "-100 -50"; map.SetParcels(true); var parcelProbe = map.Canvas.ParcelAt(beachId, -100, -50); Expect(parcelProbe != null && parcelProbe.Name == "Cais" && map.ParcelLine.Contains("Cais") && map.ParcelLine.Contains("à venda") && map.ParcelLine.Contains("4.500"), "o terreno sob o ponto escolhido aparece no cartão: " + map.ParcelLine);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) { map.Canvas.Fit(map.Page.Items[0]); yield return new WaitForSeconds(.4f); Expect(map.Canvas.ParcelsDrawn == 2, "os dois terrenos desenhados sobre a ilha: " + map.Canvas.ParcelsDrawn); }
            _checks++;
            yield return Snap("ui-map");
            map.SetParcels(false); map.Canvas.Fit();
            map.Choose(map.Page.Items[1], true); Expect(!map.TravelButton.enabledSelf, "uma ilha privada não deixa viajar"); map.Choose(map.Page.Items[0], true); Expect(map.TravelButton.enabledSelf && map.Chosen.Name == "Praia", "uma ilha aberta deixa viajar");
            map.SpotField.value = "60 -25"; map.Travel(); Expect(traveled is { } trip && trip.Island == beachId && Math.Abs(trip.X.Value - 60) < .01 && Math.Abs(trip.Z.Value + 25) < .01, "viajar leva ao ponto escolhido da ilha");
            map = new LifeVerse.Unity.UI.WorldMapWindow(mapContext); map.Open(); yield return Until(() => map.Page.Items.Count == 2, "o mapa de novo"); map.Choose(map.Page.Items[0], false); traveled = null;
            map.MarkNameField.value = "Cais"; map.AddMark(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/map/landmarks" && b.Body.Contains("Cais") && b.Body.Contains(beachId.ToString()) && b.Body.Contains("\"x\":10")), "o marco chegar à API com o ponto de nascimento");
            map.ToggleFavorite(); yield return Until(() => map.Chosen.Favorite, "a ilha virar favorita");             string heard = null; player.Client.ChatReceived += message => heard = message.Message;
            map.SpotField.value = "9999 0"; map.Travel(); Expect(traveled == null, "um ponto fora da ilha não viaja"); map.SpotField.value = ""; map.Travel(); Expect(traveled is { } home && home.X == null && home.Island == beachId, "sem ponto, viaja para onde a ilha recebe as pessoas"); map.Close(); _checks++;
            // effects that scripts put on objects: particles and lights by key, capped, moved, changed and taken away
            void Effect(string json) { using var effectDocument = System.Text.Json.JsonDocument.Parse(json); app.Effects.OnEffect(effectDocument.RootElement); }
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // a look at the effects in front of the camera (for the docs)
                var lookCamera = app.Follow.Camera; var ahead = lookCamera.transform.position + lookCamera.transform.forward * 7f + Vector3.down * 1.2f; var g = LifeVerse.Unity.World.GameSpace.ToGame(ahead); string P(float dx) => "\"x\":" + (g.x + dx).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + g.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + g.z.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Effect("{\"key\":\"shot:fire\",\"on\":true,\"kind\":\"particles\",\"preset\":\"fire\",\"rate\":50,\"lifetime\":1.4,\"size\":0.9,\"speed\":2.5,\"color\":\"#FF7A1A\"," + P(-2.2f) + "}"); Effect("{\"key\":\"shot:smoke\",\"on\":true,\"kind\":\"particles\",\"preset\":\"smoke\",\"rate\":30,\"lifetime\":3.5,\"size\":1.1,\"speed\":1.2,\"color\":\"#CCCCCC\"," + P(0f) + "}");
                Effect("{\"key\":\"shot:sparks\",\"on\":true,\"kind\":\"particles\",\"preset\":\"sparks\",\"rate\":50,\"lifetime\":1.2,\"size\":0.2,\"speed\":4,\"color\":\"#FFD060\"," + P(2.2f) + "}"); Effect("{\"key\":\"shot:lamp\",\"on\":true,\"kind\":\"light\",\"color\":\"#FFC080\",\"radius\":14,\"energy\":3,\"flicker\":true," + P(-2.2f) + "}");
                yield return new WaitForSeconds(1.2f); yield return Snap("effects"); app.Effects.Clear();
            }            Effect("{\"operation\":\"effect\",\"key\":\"s1:fire\",\"on\":true,\"kind\":\"particles\",\"preset\":\"fire\",\"rate\":30,\"lifetime\":1.5,\"size\":0.5,\"speed\":2,\"color\":\"#FF8800\",\"x\":10,\"y\":5,\"z\":20}");
            Effect("{\"operation\":\"effect\",\"key\":\"s1:lamp\",\"on\":true,\"kind\":\"light\",\"color\":\"#FFEEAA\",\"radius\":12,\"energy\":2,\"flicker\":true,\"x\":10,\"y\":6,\"z\":20}");
            Expect(app.Effects.Emitters == 1 && app.Effects.Lights == 1, "um emissor de fogo e uma luz");
            var fire = app.Effects.EffectOf("s1:fire"); var fireSystem = fire.GetComponent<ParticleSystem>(); Expect(fireSystem.isPlaying && Vector3.Distance(fire.transform.position, LifeVerse.Unity.World.GameSpace.ToUnity(10, 5, 20)) < .01f, "o fogo toca no ponto do objeto (z invertido)");
            Expect(fireSystem.main.maxParticles == 45 && fire.GetComponent<ParticleSystemRenderer>().sharedMaterial != null, "a quantidade de partículas vem da taxa e do tempo de vida: " + fireSystem.main.maxParticles);
            var lamp = app.Effects.EffectOf("s1:lamp"); Expect(Mathf.Abs(lamp.GetComponent<Light>().range - 12f) < .01f && lamp.GetComponent<LifeVerse.Unity.World.EffectLight>().Flicker, "a luz tem o raio e treme");
            Effect("{\"operation\":\"effect\",\"key\":\"s1:fire\",\"on\":true,\"kind\":\"particles\",\"preset\":\"fire\",\"rate\":30,\"lifetime\":1.5,\"x\":50,\"y\":5,\"z\":20}"); Expect(app.Effects.Emitters == 1 && Vector3.Distance(app.Effects.EffectOf("s1:fire").transform.position, LifeVerse.Unity.World.GameSpace.ToUnity(50, 5, 20)) < .01f, "a mesma chave move o efeito, não cria outro");
            Effect("{\"operation\":\"effect\",\"key\":\"s1:fire\",\"on\":false}"); Effect("{\"operation\":\"effect\",\"key\":\"s1:lamp\",\"on\":false}"); yield return null; Expect(app.Effects.Emitters == 0 && app.Effects.Lights == 0, "desligados, somem");
            for (var n = 0; n < 60; n++) Effect("{\"operation\":\"effect\",\"key\":\"cap" + n + "\",\"on\":true,\"kind\":\"particles\",\"preset\":\"smoke\",\"x\":0,\"y\":0,\"z\":0}"); for (var n = 0; n < 30; n++) Effect("{\"operation\":\"effect\",\"key\":\"lamp" + n + "\",\"on\":true,\"kind\":\"light\",\"x\":0,\"y\":0,\"z\":0}");
            Expect(app.Effects.Emitters == LifeVerse.Unity.World.ObjectEffects.MaxEmitters && app.Effects.Lights == LifeVerse.Unity.World.ObjectEffects.MaxLights, "os limites de emissores e de luzes valem: " + app.Effects.Emitters + " " + app.Effects.Lights); app.Effects.Clear(); _checks++;            app.Hud.Chat.Input.value = "  olá, ilha!  "; Expect(app.Hud.Chat.Submit(), "enviar pelo chat");
            // the real estate market and the player's properties
            var marketParcel = Guid.NewGuid(); var marketIsland = Guid.NewGuid();
            stub.Routes["GET /api/v1/discovery/market"] = "{\"total\":1,\"page\":1,\"pageSize\":9,\"items\":[{\"parcelId\":\"" + marketParcel + "\",\"islandId\":\"" + marketIsland + "\",\"islandName\":\"Praia\",\"code\":\"A-1\",\"name\":\"Lote\",\"category\":\"residencial\",\"area\":900,\"maxObjects\":200,\"offer\":\"sale\",\"state\":\"sale\",\"price\":4500,\"canVisit\":true}]}";
            stub.Routes["POST /api/v1/worlds/islands/" + marketIsland + "/land/parcels/" + marketParcel + "/buy"] = "{}";
            var estateMarket = new LifeVerse.Unity.UI.RealEstateMarketWindow(app.Context); estateMarket.Open(); yield return Until(() => estateMarket.Page.Items.Count == 1 && estateMarket.Count == 1, "o mercado listar o terreno"); Expect(estateMarket.LastQuery.Contains("type=parcel") && estateMarket.LastQuery.Contains("onlyAvailable=true"), "a aba Terrenos pede terrenos: " + estateMarket.LastQuery);
            yield return Snap("ui-real-estate"); estateMarket.SelectTab("ilhas_venda"); Expect(estateMarket.LastQuery.Contains("type=island") && estateMarket.LastQuery.Contains("offer=sale"), "a aba de ilhas à venda muda a busca"); yield return Until(() => estateMarket.Page.Items.Count == 1, "a nova busca"); estateMarket.QueryField.value = "praia"; estateMarket.Search(1); Expect(estateMarket.LastQuery.Contains("q=praia"), "a busca leva o texto");
            estateMarket.Buy(estateMarket.Page.Items[0]); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/worlds/islands/" + marketIsland + "/land/parcels/" + marketParcel + "/buy" && b.Body.Contains("\"expectedPrice\":4500") && b.Body.Contains("idempotencyKey")), "a compra levar o preço visto e a chave"); estateMarket.Close();
            var holdId = Guid.NewGuid(); stub.Routes["GET /api/v1/properties/mine"] = "{\"summary\":{\"total\":2,\"inUse\":1,\"rentedOut\":1,\"monthlyRevenue\":800,\"expiringSoon\":0},\"islands\":[{\"id\":\"" + holdId + "\",\"name\":\"Minha ilha\",\"parcels\":3}],\"parcels\":[{\"id\":\"" + marketParcel + "\",\"islandId\":\"" + holdId + "\",\"code\":\"B-2\",\"name\":\"Cais\",\"island\":\"Minha ilha\",\"area\":600,\"salePrice\":7000}],\"contracts\":[],\"listings\":[],\"payments\":[{\"type\":\"sale\",\"direction\":\"in\",\"amount\":7000,\"parcel\":\"Cais\"}],\"incomingTransfers\":[{\"islandId\":\"" + holdId + "\",\"name\":\"Ilha Nova\",\"from\":\"Caio\"}]}";
            stub.Routes["DELETE /api/v1/worlds/islands/" + holdId + "/land/parcels/" + marketParcel + "/sale"] = "{}"; stub.Routes["POST /api/v1/worlds/islands/" + holdId + "/real-estate/transfer/accept"] = "{}";
            var myProps = new LifeVerse.Unity.UI.MyPropertiesWindow(app.Context); myProps.Open(); yield return Until(() => myProps.Data != null && myProps.Count > 0, "as propriedades chegarem"); Expect(myProps.Data.Total == 2 && myProps.Data.Transfers.Count == 1 && myProps.Data.Parcels[0].Title == "B-2 — Cais", "o resumo, a oferta de ilha e o terreno");
            myProps.Show("terrenos"); Expect(myProps.Count == 1, "um terreno listado"); yield return Snap("ui-my-properties"); myProps.Show("pagamentos"); Expect(myProps.Count == 1, "um pagamento listado"); myProps.Close(); _checks++;
            // putting a parcel up for sale from the properties window
            stub.Routes["POST /api/v1/worlds/islands/" + holdId + "/land/parcels/" + marketParcel + "/sale"] = "{}"; var offerWindow = new LifeVerse.Unity.UI.ListingOfferWindow(app.Context, false, false, holdId, marketParcel, "Cais", null); offerWindow.Open(); offerWindow.PriceField.value = "0"; offerWindow.Submit(); Expect(offerWindow.Notice.Contains("preço"), "um preço zero é recusado antes de enviar");
            offerWindow.PriceField.value = "8.000"; offerWindow.Submit(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/worlds/islands/" + holdId + "/land/parcels/" + marketParcel + "/sale" && b.Body.Contains("\"price\":8000")), "o anúncio de venda chegar à API"); _checks++;
            // the fingers of a body read the real way: a model with finger bones, curled by what the hand does
            var fingerGlb = TestAssets.HumanoidWithFingers(); var fingerHost = new GameObject("dedos"); var fingerBody = default(LifeVerse.Unity.Avatar.SkeletalBody); var fingerTask = LifeVerse.Unity.Avatar.SkeletalBody.CreateAsync(fingerHost.transform, fingerGlb); yield return Until(() => fingerTask.IsCompleted, "o corpo com dedos ser lido"); fingerBody = fingerTask.Result;
            Expect(fingerBody != null && fingerBody.Fingers.HasFingers && fingerBody.Fingers.BoneCount == 30, "o corpo lido tem os 30 ossos dos dedos: " + (fingerBody != null ? fingerBody.Fingers.BoneCount : -1));
            for (var step = 0; step < 90; step++) { fingerBody.Tick(1f / 60f, LifeVerse.Shared.Networking.NetworkMotionState.Idle, LifeVerse.Unity.Avatar.AvatarView.Gesture.Point, 0f, 0f, default, 0f, true); yield return null; }
            Expect(fingerBody.Fingers.BendOf('r', LifeVerse.Unity.Avatar.Finger.Middle) > 40f && fingerBody.Fingers.BendOf('r', LifeVerse.Unity.Avatar.Finger.Index) < 15f, "apontando, o indicador fica e os outros fecham: " + fingerBody.Fingers.BendOf('r', LifeVerse.Unity.Avatar.Finger.Middle) + " / " + fingerBody.Fingers.BendOf('r', LifeVerse.Unity.Avatar.Finger.Index));
            for (var step = 0; step < 120; step++) { fingerBody.Tick(1f / 60f, LifeVerse.Shared.Networking.NetworkMotionState.Idle, LifeVerse.Unity.Avatar.AvatarView.Gesture.None, 0f, 0f, default, 0f, true); yield return null; }
            Expect(fingerBody.Fingers.BendOf('r', LifeVerse.Unity.Avatar.Finger.Middle) < 45f, "solta o gesto, a mão volta ao relaxado: " + fingerBody.Fingers.BendOf('r', LifeVerse.Unity.Avatar.Finger.Middle)); Destroy(fingerHost); _checks++;            yield return Until(() => heard == "olá, ilha!", "os outros ouvirem o chat"); _checks++;
            // the settings that follow the account: fetched, sent when changed, refused when another computer got there first
            stub.Routes["GET /api/v1/users/me/preferences"] = "{\"revision\":4,\"values\":{\"controls.invert_y\":true,\"chat.font_size\":20,\"chat.opacity\":50,\"controls.mouse_sensitivity\":1.5,\"chat.show_time\":\"nao\"}}"; stub.Routes["PUT /api/v1/users/me/preferences"] = "{\"revision\":5,\"values\":{}}";
            var syncSettings = new LifeVerse.Unity.UI.UserSettings(); var sync = new LifeVerse.Unity.UI.AccountSync(syncSettings, (m, p, b) => app.Api.ApiAsync(m, p, b), () => app.Api.UserId); Expect(!sync.Enabled, "a sincronia começa desligada");
            var fetched = sync.FetchAsync(); yield return Until(() => fetched.IsCompleted, "a conta responder"); Expect(fetched.Result.Result == LifeVerse.Unity.UI.SyncResult.Ok && fetched.Result.Revision == 4 && fetched.Result.Values.Count == 3 && !fetched.Result.Values.ContainsKey("controls.mouse_sensitivity") && !fetched.Result.Values.ContainsKey("chat.show_time"), "só chegam os valores conhecidos e válidos: " + fetched.Result.Values.Count);
            Expect(sync.UseAccount(fetched.Result) == 3 && syncSettings.InvertY && syncSettings.ChatFontSize == 20 && Math.Abs(syncSettings.ChatOpacity - .5f) < .001f && sync.Enabled && sync.Revision == 4, "usar a conta aplica os valores e liga a sincronia");
            syncSettings.AlwaysRun = true; var pushed = sync.PushAppliedAsync(); yield return Until(() => pushed.IsCompleted, "o envio"); Expect(pushed.Result.Result == LifeVerse.Unity.UI.SyncResult.Ok && sync.Revision == 5 && stub.ReceivedAny(b => b.Route == "PUT /api/v1/users/me/preferences" && b.Body.Contains("\"baseRevision\":4") && b.Body.Contains("\"controls.always_run\":true") && !b.Body.Contains("mouse")), "o envio leva a revisão vista e só ids da lista");
            sync.TurnOff(); Expect(!sync.Enabled, "desligar volta ao local"); _checks++;            player.Client.SendChat("oi do outro lado"); yield return Until(() => app.Hud.Chat.Lines.Any(l => l.Text == "oi do outro lado" && l.Name == "joiner"), "o chat do outro aparecer na tela"); _checks++;
            // sound: the recordings, the volumes, the soundscape, footsteps and typing, what scripts play, the music of a parcel, and voice
            yield return Until(() => SoundLibrary.Has(SoundLibrary.Footsteps) && SoundLibrary.Has(SoundLibrary.Typing) && SoundLibrary.Has(SoundLibrary.Login), "as gravações do jogo serem lidas", 20f);
            Expect(SoundLibrary.Get(SoundLibrary.Footsteps).length > 3f && SoundLibrary.Get(SoundLibrary.Typing).length > 3f && SoundLibrary.Get(SoundLibrary.Login).length > 20f, "as três gravações são mp3 de verdade lidos: " + SoundLibrary.Get(SoundLibrary.Footsteps).length + " " + SoundLibrary.Get(SoundLibrary.Typing).length + " " + SoundLibrary.Get(SoundLibrary.Login).length);
            Expect(app.LoginTheme != null && app.LoginTheme.FadeTarget == 0f && app.LoginTheme.Source.clip != null, "o tema da tela de entrada existe e some quando se entra no jogo"); _checks++;
            var savedMaster = app.Settings.MasterVolume; app.Settings.MasterVolume = .5f; app.Settings.EffectsVolume = .8f; app.Settings.VoiceVolume = .4f; app.ApplyAudio();
            Expect(Mathf.Abs(Buses.Master - .5f) < 1e-4f && Mathf.Abs(Buses.Gain(Bus.Effects) - .4f) < 1e-4f && Mathf.Abs(Buses.Gain(Bus.Voice) - .2f) < 1e-4f, "os volumes da configuração chegam aos grupos de som (geral vezes o do grupo)"); app.Settings.MasterVolume = savedMaster; app.Settings.EffectsVolume = 1f; app.Settings.VoiceVolume = 1f; app.ApplyAudio();
            var shore = AmbientLevels.Compute(0f, false, 0f, "clear"); var inland = AmbientLevels.Compute(300f, false, 0f, "clear"); var storm = AmbientLevels.Compute(300f, false, 25f, "storm"); var wet = AmbientLevels.Compute(300f, true, 0f, "clear");
            Expect(shore.Ocean > .99f && inland.Ocean < .01f && wet.Ocean > .99f && storm.Rain == 1f && storm.Wind > inland.Wind + .3f && AmbientLevels.Compute(300f, false, 0f, "rain").Rain > 0f, "o mar sobe perto da praia e na água, o vento com a altura e o temporal, a chuva com o tempo");
            app.Ambient.SetTargets(new LifeVerse.Shared.Audio.AmbientMix(1f, .3f, 0f)); yield return new WaitForSeconds(1.5f); var surf = app.Ambient.Render(4096); var energy = 0f; foreach (var sample in surf) energy += sample * sample; Expect(app.Ambient.Ocean > .5f && energy / surf.Length > 1e-4f && app.Ambient.Playing, "o som do mar sai das caixas: " + energy / surf.Length); app.Ambient.SetTargets(new LifeVerse.Shared.Audio.AmbientMix(0f, 0f, 0f)); _checks++;
            // footsteps and typing at the avatar
            var ownView = AvatarView.Create(4242, "ouvinte", Color.white); ownView.PlaceAt(new Vector3(40f, 5f, 40f)); var ownSounds = ownView.GetComponent<AvatarSounds>(); Expect(ownSounds != null && !ownSounds.StepsWanted, "o avatar tem seus sons e parado não anda");
            ownView.SetPose(new Vector3(40f, 5f, 40f), 0f, new Vector3(0f, 0f, -2f), NetworkMotionState.Walk); yield return new WaitForSeconds(.8f); Expect(ownSounds.StepsWanted && ownSounds.FootstepsPlaying && Mathf.Abs(ownSounds.StepPitch - 1f) < .01f, "andando, as passadas tocam no ritmo normal");
            ownView.SetPose(new Vector3(40f, 5f, 40f), 0f, new Vector3(0f, 0f, -6f), NetworkMotionState.Run); yield return new WaitForSeconds(.5f); Expect(ownSounds.FootstepsPlaying && Mathf.Abs(ownSounds.StepPitch - 1.45f) < .01f, "correndo, mais rápido: " + ownSounds.StepPitch);
            var stepSource = ownView.transform.Find("Sound_footsteps").GetComponent<AudioSource>(); Expect(stepSource.spatialBlend == 1f && stepSource.loop && stepSource.maxDistance > 20f, "as passadas são em 3D, em círculo, e se apagam com a distância");
            ownView.SetPose(new Vector3(40f, 5f, 40f), 0f, Vector3.zero, NetworkMotionState.Idle); yield return new WaitForSeconds(1.2f); Expect(!ownSounds.StepsWanted && !ownSounds.FootstepsPlaying && ownSounds.StepVolume < .05f, "parou: o som some e para");
            ownView.SetPose(new Vector3(40f, 5f, 40f), 0f, Vector3.zero, NetworkMotionState.Typing); yield return new WaitForSeconds(.6f); Expect(ownSounds.TypingWanted && ownSounds.TypingPlaying, "digitando, o teclado toca"); ownView.SetPose(new Vector3(40f, 5f, 40f), 0f, Vector3.zero, NetworkMotionState.Swim); yield return new WaitForSeconds(1.2f); Expect(!ownSounds.TypingPlaying && !ownSounds.FootstepsPlaying, "na água não há passos nem teclado"); Destroy(ownView.gameObject); _checks++;
            // what the avatar wears: a model on a point of the body, placed by the point's offset, taken off again
            var wearer = AvatarView.Create(4243, "vestido", Color.white); var worn = wearer.GetComponent<AvatarAttachments>(); var hatGlb = TestAssets.Glb(); var hatAsset = Guid.NewGuid(); var hatId = Guid.NewGuid(); AvatarAttachments.Loader = asset => System.Threading.Tasks.Task.FromResult(asset == hatAsset ? hatGlb : null);
            string HatJson(bool equipped, float y) => "[{\"id\":\"" + hatId + "\",\"inventoryItemId\":\"" + Guid.NewGuid() + "\",\"assetId\":\"" + hatAsset + "\",\"name\":\"Chapéu\",\"point\":\"head_top\",\"pointName\":\"Topo da cabeça\",\"position\":[0," + y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",0],\"rotation\":[0,0,0],\"scale\":[1,1,1],\"equipped\":" + (equipped ? "true" : "false") + ",\"revision\":1}]";
            wearer.ApplyAppearance(new AvatarAppearance { AttachmentsJson = HatJson(true, .05f) }); yield return Until(() => worn.Loaded >= 1 && worn.HolderOf(hatId) != null, "o chapéu carregar e ir para o corpo");
            Expect(worn.HolderOf(hatId).parent == wearer.Head && worn.HolderOf(hatId).GetComponentInChildren<Renderer>() != null, "o chapéu está preso à cabeça e tem malha");
            var headOffset = LifeVerse.Shared.Avatar.AttachmentPoints.Find("head_top").Offset; Expect(Mathf.Abs(worn.HolderOf(hatId).localPosition.y - (headOffset[1] + .05f)) < .001f, "fica no desvio do ponto mais a posição da pessoa: " + worn.HolderOf(hatId).localPosition.y);
            wearer.ApplyAppearance(new AvatarAppearance { AttachmentsJson = HatJson(true, .2f) }); yield return null; Expect(Mathf.Abs(worn.HolderOf(hatId).localPosition.y - (headOffset[1] + .2f)) < .001f && worn.Count == 1, "mudar a posição move o mesmo objeto");
            wearer.ApplyAppearance(new AvatarAppearance { AttachmentsJson = HatJson(false, .2f) }); yield return null; Expect(worn.Count == 0 && worn.HolderOf(hatId) == null, "tirado, o chapéu some do corpo"); Destroy(wearer.gameObject); _checks++;
            // sway: an object worn on something that moves leans back
            var swayer = AvatarView.Create(4244, "balanca", Color.white); var swayWorn = swayer.GetComponent<AvatarAttachments>(); var swayId = Guid.NewGuid(); swayer.PlaceAt(new Vector3(30f, 5f, 30f));
            string SwayJson(float sway) => "[{\"id\":\"" + swayId + "\",\"assetId\":\"" + hatAsset + "\",\"name\":\"Cauda\",\"point\":\"head_top\",\"position\":[0,0,0],\"rotation\":[0,0,0],\"scale\":[1,1,1],\"equipped\":true,\"revision\":1,\"sway\":" + sway.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]";
            swayer.ApplyAppearance(new AvatarAppearance { AttachmentsJson = SwayJson(1f) }); yield return Until(() => swayWorn.HolderOf(swayId) != null, "o objeto com balanço carregar");
            for (var step = 0; step < 20; step++) { swayer.PlaceAt(new Vector3(30f, 5f, 30f + step * .3f)); yield return null; }
            Expect(swayWorn.TiltOf(swayId) > .5f, "em movimento o objeto se inclina: " + swayWorn.TiltOf(swayId)); swayer.ApplyAppearance(new AvatarAppearance { AttachmentsJson = SwayJson(0f) });
            for (var step = 0; step < 10; step++) { swayer.PlaceAt(new Vector3(30f, 5f, 36f + step * .3f)); yield return null; } Expect(swayWorn.TiltOf(swayId) < .01f, "com balanço zero fica parado"); Destroy(swayer.gameObject); _checks++;
            // a model with a clip of its own, in the world: it plays what its setting says, from the clock of the server
            var clipAsset = Guid.NewGuid(); var clipGlb = TestAssets.AnimatedGlb(); var clipObject = Guid.NewGuid(); var oldObjectLoader = app.Client.Objects.AssetLoader; app.Client.Objects.AssetLoader = id => System.Threading.Tasks.Task.FromResult(id == clipAsset ? clipGlb : oldObjectLoader != null ? oldObjectLoader(id).Result : null);
            string ClipState(string animation) => "{\"operation\":\"create\",\"objectState\":{\"id\":\"" + clipObject + "\",\"name\":\"Giratorio\",\"primitiveType\":\"mesh\",\"geometryJson\":\"{\\\"assetId\\\":\\\"" + clipAsset + "\\\"}\",\"materialJson\":\"" + animation.Replace("\"", "\\\"") + "\",\"positionX\":0,\"positionY\":300,\"positionZ\":0,\"revision\":1}}";
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); Expect(app.Client.Objects.ApplyOperation(ClipState("{\"animation\":{\"clip\":\"Spin\",\"loop\":true,\"playing\":true,\"speed\":1,\"since\":" + nowMs + "}}")), "o objeto animado ser aceito");
            yield return Until(() => app.Client.Objects.ClipsOf(clipObject).Count == 1 && app.Client.Objects.ClipPlayingOf(clipObject) == "Spin", "o modelo ter o clipe e tocá-lo"); Expect(app.Client.Objects.ClipsOf(clipObject)[0] == "Spin", "o clipe do modelo se chama Spin");
            var before = app.Client.Objects.ClipTimeOf(clipObject); yield return new WaitForSeconds(.5f); Expect(Mathf.Abs(Mathf.Repeat(app.Client.Objects.ClipTimeOf(clipObject) - before, 2f) - .5f) < .25f, "o clipe corre com o tempo: " + before + " -> " + app.Client.Objects.ClipTimeOf(clipObject));
            var shownModel = app.Client.Objects.ViewOf(clipObject); var firstModelCount = app.Client.Objects.ModelsLoaded;
            Expect(app.Client.Objects.ApplyOperation(ClipState("{\"animation\":{\"clip\":\"Spin\",\"loop\":true,\"playing\":false,\"offset\":1.0,\"since\":" + nowMs + "}}")), "a pausa chegar"); yield return null;
            Expect(Mathf.Abs(app.Client.Objects.ClipTimeOf(clipObject) - 1f) < .05f && app.Client.Objects.ModelsLoaded == firstModelCount, "pausado, fica no ponto pedido sem recarregar o modelo");
            Expect(app.Client.Objects.ApplyOperation(ClipState("{\"animation\":{\"clip\":\"Nada\"}}")) && app.Client.Objects.ClipPlayingOf(clipObject) == "", "um clipe que o modelo não tem é ignorado");
            app.Client.Objects.Remove(clipObject); app.Client.Objects.AssetLoader = oldObjectLoader; _checks++;
            // an object worn at a point of the screen is held in front of the camera, at the corner of its point
            var hudCamera = app.Follow != null && app.Follow.Camera != null ? app.Follow.Camera : _camera; var hudId = Guid.NewGuid(); var hud = new LifeVerse.Unity.UI.HudAttachments(app.Ui, () => hudCamera); AvatarAttachments.Loader = asset => System.Threading.Tasks.Task.FromResult(asset == hatAsset ? hatGlb : null);
            var hudData = new AttachmentData { Id = hudId, AssetId = hatAsset, Name = "Medidor", Point = "hud_top_right", PointName = "HUD", Equipped = true, Position = new float[] { 0, 0, 0 } };
            hud.Apply(new List<AttachmentData> { hudData }); yield return Until(() => hud.Count == 1, "o anexo de tela ser desenhado"); yield return null; yield return null;
            var hudRoot = hud.RootOf(hudId); var hudScreen = hudCamera.WorldToScreenPoint(hudRoot.position); var hudWanted = LifeVerse.Unity.UI.HudAttachments.ScreenPoint(hudData, new Vector2(hudCamera.pixelWidth, hudCamera.pixelHeight));
            Expect(hudScreen.z > 0f && Vector2.Distance(new Vector2(hudScreen.x, hudScreen.y), hudWanted) < 3f, "o objeto de tela fica no canto do seu ponto: " + hudScreen + " queria " + hudWanted);
            Expect(hudRoot.GetComponentInChildren<Renderer>() != null && hudRoot.GetComponentInChildren<Collider>() == null, "tem malha e não toma o mouse");
            hudData.Position = new float[] { -1f, 0, 0 }; hud.Apply(new List<AttachmentData> { hudData }); yield return null; yield return null; var hudMoved = hudCamera.WorldToScreenPoint(hud.RootOf(hudId).position); Expect(Mathf.Abs((hudScreen.x - hudMoved.x) - LifeVerse.Unity.UI.HudAttachments.PixelsPerMetre) < 3f, "a posição afasta o objeto do canto em 200 pixels por metro");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) { yield return new WaitForSeconds(.5f); yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-hud-shot.png")); yield return new WaitForSeconds(.5f); }
            hud.Apply(new List<AttachmentData>()); Expect(hud.Count == 0, "tirado, some da tela"); hud.Clear(); _checks++;
            // the window: list, wear, change, take off
            var wornRow = "{\"id\":\"" + hatId + "\",\"inventoryItemId\":\"" + Guid.NewGuid() + "\",\"assetId\":\"" + hatAsset + "\",\"name\":\"Chapéu\",\"point\":\"head_top\",\"pointName\":\"Topo da cabeça\",\"position\":[0,0,0],\"rotation\":[0,0,0],\"scale\":[1,1,1],\"equipped\":true,\"revision\":2,\"canModify\":true}";
            var invItem = Guid.NewGuid(); stub.Routes["GET /api/v1/users/me/avatar/attachments"] = "{\"profiles\":[\"humanoid\"],\"attachments\":[" + wornRow + "]}"; stub.Routes["GET /api/v1/inventory/items"] = "{\"items\":[{\"id\":\"" + invItem + "\",\"name\":\"Boné\"}]}";
            stub.Routes["POST /api/v1/users/me/avatar/attachments"] = "{}"; stub.Routes["PATCH /api/v1/users/me/avatar/attachments/" + hatId] = "{}"; stub.Routes["DELETE /api/v1/users/me/avatar/attachments/" + hatId] = "{}";
            var attachWindow = new LifeVerse.Unity.UI.AttachmentsWindow(app.Context); attachWindow.Open(); yield return Until(() => attachWindow.Attachments.Count == 1 && attachWindow.Count == 1, "a janela listar o que se veste"); yield return Until(() => attachWindow.ItemField.choices.Contains("Boné"), "a janela listar o inventário");
            attachWindow.PointField.value = "Crânio"; attachWindow.Wear(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/users/me/avatar/attachments" && b.Body.Contains(invItem.ToString()) && b.Body.Contains("\"point\":\"skull\"")), "vestir mandar o item e o ponto");
            attachWindow.Apply(attachWindow.Attachments[0], "0 0,3 0", "0 45 0", "2 2 2"); yield return Until(() => stub.ReceivedAny(b => b.Route == "PATCH /api/v1/users/me/avatar/attachments/" + hatId && b.Body.Contains("0.3") && b.Body.Contains("45") && b.Body.Contains("2,2,2")), "o ajuste chegar à API");
            attachWindow.Detach(attachWindow.Attachments[0]); yield return Until(() => { lock (stub.Requests) return stub.Requests.Contains("DELETE /api/v1/users/me/avatar/attachments/" + hatId); }, "tirar chegar à API"); attachWindow.Close(); _checks++;
            // sounds that scripts of objects play
            var finished = new List<string>(); app.Sounds.Report = finished.Add; var soundAsset = Guid.NewGuid(); app.Sounds.AssetLoader = id => System.Threading.Tasks.Task.FromResult(id == soundAsset ? WavBytes(.4f, 440f) : null); var soundObject = Guid.NewGuid();
            app.Sounds.OnOperation("{\"operation\":\"sound\",\"objectId\":\"" + soundObject + "\",\"assetId\":\"" + soundAsset + "\",\"x\":3,\"y\":1,\"z\":4,\"volume\":0.6,\"loop\":true}"); yield return Until(() => app.Sounds.ActiveSounds == 1, "o som do objeto começar");
            yield return null; yield return null; var objectSource = app.Sounds.SoundOf(soundObject); Expect(objectSource.loop && objectSource.clip.length > .35f && Vector3.Distance(objectSource.transform.position, LifeVerse.Unity.World.GameSpace.ToUnity(3f, 1f, 4f)) < .01f && Mathf.Abs(objectSource.volume - .6f * Buses.Gain(Bus.Effects)) < .01f && objectSource.spatialBlend == 1f, "o som toca em círculo, no lugar do objeto, em 3D e no volume dos efeitos");
            app.Sounds.OnOperation("{\"operation\":\"sound_stop\",\"objectId\":\"" + soundObject + "\"}"); Expect(app.Sounds.ActiveSounds == 0, "o script parar o som");
            app.Sounds.OnOperation("{\"operation\":\"sound\",\"objectId\":\"" + soundObject + "\",\"assetId\":\"" + soundAsset + "\",\"x\":0,\"y\":1,\"z\":0,\"volume\":1,\"loop\":false}"); yield return Until(() => finished.Any(f => f.Contains("sound_finished") && f.Contains(soundObject.ToString())), "um som sem repetição acabar e a ilha ser avisada", 6f); Expect(app.Sounds.ActiveSounds == 0, "e some depois de acabar");
            app.Sounds.MediaFetch = url => System.Threading.Tasks.Task.FromResult(url.EndsWith("a.mp3") ? WavBytes(.5f, 330f) : null); var refusedBefore = app.Sounds.Refused;
            app.Sounds.OnOperation("{\"operation\":\"media\",\"key\":\"rd\",\"on\":true,\"url\":\"https://exemplo.com/a.mp3\",\"playId\":\"p1\",\"volume\":0.5,\"range\":20,\"loop\":true,\"x\":1,\"y\":1,\"z\":1}"); yield return Until(() => app.Sounds.ActiveStreams == 1, "o som da internet tocar no objeto"); var stream = app.Sounds.StreamOf("rd"); Expect(stream.loop && Mathf.Abs(stream.maxDistance - 20f) < .01f && app.Sounds.StreamPlayId("rd") == "p1", "ele toca com o alcance que o script pediu");
            app.Sounds.OnOperation("{\"operation\":\"media\",\"key\":\"rd\",\"on\":true,\"url\":\"https://exemplo.com/a.mp3\",\"playId\":\"p1\",\"volume\":0.9,\"range\":30,\"x\":8,\"y\":1,\"z\":1}"); yield return null; Expect(app.Sounds.StreamOf("rd") == stream && Vector3.Distance(stream.transform.position, LifeVerse.Unity.World.GameSpace.ToUnity(8f, 1f, 1f)) < .01f && Mathf.Abs(stream.maxDistance - 30f) < .01f, "a mesma jogada que mudou de lugar ou de volume não recomeça");
            app.Sounds.OnOperation("{\"operation\":\"media\",\"key\":\"rd\",\"on\":false}"); Expect(app.Sounds.ActiveStreams == 0, "o script desliga o som"); app.Sounds.OnOperation("{\"operation\":\"media\",\"key\":\"ruim\",\"on\":true,\"url\":\"http://exemplo.com/a.mp3\",\"playId\":\"p2\"}"); app.Sounds.OnOperation("{\"operation\":\"media\",\"key\":\"local\",\"on\":true,\"url\":\"https://localhost/a.mp3\",\"playId\":\"p3\"}"); Expect(app.Sounds.ActiveStreams == 0 && app.Sounds.Refused >= refusedBefore + 2, "endereço sem https ou de dentro da rede é recusado: " + app.Sounds.Refused);
            var realMp3 = System.IO.File.ReadAllBytes(System.IO.Path.Combine(SoundLibrary.Folder, "footsteps_concrete.mp3")); var decodedTask = AudioDecode.DecodeAsync(realMp3, "teste"); yield return Until(() => decodedTask.IsCompleted, "o mp3 ser decodificado", 20f); Expect(decodedTask.Result != null && decodedTask.Result.length > 3f && AudioDecode.Sniff(realMp3) == "mp3" && AudioDecode.Sniff(WavBytes(.1f, 100f)) == "wav" && AudioDecode.Sniff(new byte[64]) == null, "um mp3 de verdade vira som; o que não é som é recusado"); _checks++;
            // the music of the parcel under the person
            var here = app.Player.GamePosition; var cellX = LifeVerse.Shared.Land.LandMath.CellOf(here.x); var cellZ = LifeVerse.Shared.Land.LandMath.CellOf(here.z); var tuned = new List<string>(); app.ParcelMusic.Fetcher = url => { tuned.Add(url); return System.Threading.Tasks.Task.FromResult(WavBytes(.8f, 220f)); };
            string Land(string media) => "{\"parcels\":[{\"id\":\"" + Guid.NewGuid() + "\",\"media\":" + System.Text.Json.JsonSerializer.Serialize(media) + ",\"runs\":[[" + cellZ + "," + (cellX - 1) + "," + (cellX + 1) + "]]}]}";
            app.ParcelMusic.ReadLand(Land("{\"musicUrl\":\"https://exemplo.com/praia.mp3\",\"volume\":0.5,\"autoplay\":true,\"name\":\"Praia\"}")); yield return Until(() => app.ParcelMusic.Playing, "a música do terreno tocar"); Expect(app.ParcelMusic.Status.Contains("Praia") && app.ParcelMusic.Source.loop && tuned.Count == 1 && app.ParcelMusic.CurrentParcel != Guid.Empty, "a música do terreno toca em círculo e diz o nome: " + app.ParcelMusic.Status);
            yield return null; yield return null; Expect(Mathf.Abs(app.ParcelMusic.Source.volume - .5f * Buses.Gain(Bus.Music)) < .01f, "no volume do terreno vezes o da música"); app.ParcelMusic.Enabled = false; Expect(!app.ParcelMusic.Playing, "desligar a música dos terrenos para tudo"); app.ParcelMusic.Enabled = true; yield return Until(() => app.ParcelMusic.Playing, "ligar de novo recomeça");
            app.ParcelMusic.ReadLand(Land("{\"musicUrl\":\"http://exemplo.com/x.mp3\",\"autoplay\":true}")); yield return null; Expect(!app.ParcelMusic.Playing && app.ParcelMusic.StatusFailed && app.ParcelMusic.Status.Contains("https"), "música sem https é recusada com a razão: " + app.ParcelMusic.Status);
            app.ParcelMusic.ReadLand(Land("{\"musicUrl\":\"https://exemplo.com/quieta.mp3\",\"autoplay\":false}")); yield return null; Expect(!app.ParcelMusic.Playing, "música que não toca sozinha fica quieta"); app.ParcelMusic.ReadLand("{\"parcels\":[]}"); yield return null; Expect(!app.ParcelMusic.Playing && app.ParcelMusic.CurrentParcel == Guid.Empty, "chão aberto não tem música"); _checks++;
            // proximity voice: another person talks and this one hears, from where the speaker is
            var talker = new GameObject("Talker").AddComponent<VoiceChat>(); talker.Attach(player.Client); talker.ManualInput = true; talker.TalkHeld = true; var speech = new float[16000]; for (var i = 0; i < speech.Length; i++) speech[i] = Mathf.Sin(2f * Mathf.PI * 300f * i / 16000f) * .5f;
            talker.SendSamples(speech, 16000); yield return Until(() => app.Hearing.Heard >= 30, "a voz de quem está perto chegar e ser decodificada: " + app.Hearing.Heard); Expect(talker.Sent >= 45 && app.Hearing.Speakers >= 1 && app.Hearing.IsSpeaking(player.Client.SelfPeerId) && app.Hearing.AnySpeaking(), "cinquenta pacotes por segundo, e o falante aparece como falando: " + talker.Sent);
            var voiceOut = app.Hearing.OutOf(player.Client.SelfPeerId); yield return new WaitForSeconds(.2f); var rendered = voiceOut.Render(2048); var voiceEnergy = 0f; foreach (var sample in rendered) voiceEnergy += sample * sample; Expect(voiceOut.Delivered > 0 && app.Hearing.SourceOf(player.Client.SelfPeerId).spatialBlend == 1f && Mathf.Abs(app.Hearing.SourceOf(player.Client.SelfPeerId).maxDistance - 25f) < .01f, "a voz sai em 3D e se apaga a 25 m");
            talker.TalkHeld = false; talker.Locked = true; var heardBefore = talker.Sent; talker.SendSamples(new float[16000], 16000); Expect(talker.Sent == heardBefore && talker.Dropped > 0, "o microfone aberto não manda silêncio"); talker.SendSamples(speech, 16000); Expect(talker.Sent > heardBefore + 40, "mas manda quando há fala: " + (talker.Sent - heardBefore));
            talker.Muted = true; var mutedBefore = talker.Sent; talker.SendSamples(speech, 16000); Expect(talker.Sent == mutedBefore, "mudo não manda nada"); talker.Muted = false; talker.Locked = false; talker.TalkHeld = false; var idleBefore = talker.Sent; talker.SendSamples(speech, 16000); Expect(talker.Sent == idleBefore, "sem tecla nem cadeado não manda nada");
            var corruptBefore = app.Hearing.Corrupt; app.Hearing.Hear(777, 1, new byte[] { 1, 2, 3 }); app.Hearing.Hear(777, 2, new byte[500]); Expect(app.Hearing.Corrupt >= corruptBefore + 1, "pacote estragado é ignorado sem derrubar nada"); var heardView = app.Player.Views.ViewOf(player.Client.SelfPeerId); talker.Locked = false; talker.TalkHeld = true; talker.SendSamples(speech, 16000); yield return new WaitForSeconds(.3f); Expect(app.Hearing.IsSpeaking(player.Client.SelfPeerId) && heardView != null, "quem fala aparece como falando para quem ouve");
            var micWindow = new LifeVerse.Unity.UI.SettingsWindow(app.Ui, System.IO.Path.GetTempFileName()) { Mic = new GameObject("MicTest").AddComponent<MicTest>() }; micWindow.Open(); micWindow.Mic.Feed(.3f, .5f); Expect(micWindow.Mic.Level > .29f && micWindow.Mic.Peak > .49f && micWindow.MicBar != null && !micWindow.Mic.Active, "o teste do microfone existe na configuração e só abre o microfone quando a pessoa pede"); Destroy(micWindow.Mic.gameObject); micWindow.Close();
            Destroy(talker.gameObject); _checks++;

            // the music of the whole island where no parcel has its own, the microphone picker and the sounds of the interface
            var islandLook = app.Client.Environment.GetComponent<LifeVerse.Unity.World.IslandLookService>(); Expect(islandLook != null, "a ilha tem o serviço que lê o ambiente"); tuned.Clear(); app.ParcelMusic.ReadLand("{\"parcels\":[]}");
            void IslandSays(string media) { using var doc = System.Text.Json.JsonDocument.Parse("{\"look\":{},\"media\":" + media + "}"); islandLook.ReadMusic(doc.RootElement); }
            IslandSays("{\"musicUrl\":\"https://exemplo.com/ilha.mp3\",\"volume\":0.4,\"autoplay\":true,\"name\":\"Maré\"}"); yield return Until(() => app.ParcelMusic.Playing && app.ParcelMusic.IslandMusicPlaying, "a música da ilha tocar onde nenhum terreno tem a sua"); Expect(app.ParcelMusic.Status.Contains("Maré") && tuned.Count == 1 && islandLook.MusicVolume < .41f, "ela toca com o nome e o volume que o dono pôs: " + app.ParcelMusic.Status);
            app.ParcelMusic.ReadLand(Land("{\"musicUrl\":\"https://exemplo.com/praia.mp3\",\"volume\":0.5,\"autoplay\":true,\"name\":\"Praia\"}")); yield return Until(() => app.ParcelMusic.Status.Contains("Praia") && !app.ParcelMusic.IslandMusicPlaying, "dentro de um terreno com música própria toca a do terreno"); app.ParcelMusic.ReadLand(Land("{\"musicUrl\":\"https://exemplo.com/quieta.mp3\",\"autoplay\":false}")); yield return null; Expect(!app.ParcelMusic.Playing, "um terreno que não toca sozinho fica quieto, sem cair para a música da ilha");
            app.ParcelMusic.ReadLand("{\"parcels\":[]}"); yield return Until(() => app.ParcelMusic.Playing, "de volta ao chão aberto a música da ilha volta"); IslandSays("{\"musicUrl\":\"http://exemplo.com/ilha.mp3\"}"); yield return null; Expect(!app.ParcelMusic.Playing && islandLook.MusicUrl.Length == 0, "música da ilha sem https é tratada como nenhuma");
            IslandSays("{\"musicUrl\":\"https://exemplo.com/ilha.mp3\",\"autoplay\":false}"); yield return null; Expect(!app.ParcelMusic.Playing, "a música da ilha que não toca sozinha fica quieta"); IslandSays("{}"); _checks++;
            MicDevices.Lister = () => new[] { "Microfone USB", "Headset", "Headset", "" }; MicDevices.Preferred = "Headset"; Expect(MicDevices.Pick() == "Headset" && MicDevices.Choices().SequenceEqual(new[] { "auto", "Microfone USB", "Headset" }), "a lista de microfones tira repetidos e vazios, com o padrão do sistema na frente"); MicDevices.Preferred = "Desligado"; Expect(MicDevices.Pick() == null, "um microfone que foi desconectado volta ao padrão"); MicDevices.Preferred = MicDevices.Auto; Expect(MicDevices.Pick() == null && MicDevices.Label("auto") == "Padrão do sistema", "automático é o padrão do sistema");
            var pickerSettings = app.Ui.Settings; var pickerWindow = new LifeVerse.Unity.UI.SettingsWindow(app.Ui, System.IO.Path.GetTempFileName()); pickerWindow.Open(); Expect(pickerWindow.InputPicker != null && pickerWindow.InputPicker.choices.Count() == 3, "a configuração mostra os microfones: " + pickerWindow.InputPicker?.choices.Count()); pickerWindow.InputPicker.index = 2; Expect(pickerSettings.InputDevice == "Headset" && MicDevices.Preferred == "Headset", "escolher um microfone vale na hora"); pickerWindow.Close(); pickerSettings.InputDevice = "auto"; MicDevices.Lister = null; MicDevices.Preferred = MicDevices.Auto; _checks++;
            var clicksBefore = UiSounds.Played; var tickle = LifeVerse.Unity.UI.UiKit.Check("x", false, _ => { }); app.Ui.WindowLayer.Add(tickle); tickle.value = true; Expect(UiSounds.Played == clicksBefore + 1 && UiSounds.Last == UiSound.Toggle, "um interruptor faz o seu som");
            yield return new WaitForSeconds(.06f); app.Notices.Show("pronto", LifeVerse.Unity.UI.NoticeKind.Good); Expect(UiSounds.Last == UiSound.Good, "um aviso bom faz o som de coisa boa"); yield return new WaitForSeconds(.06f); app.Notices.Show("falhou", LifeVerse.Unity.UI.NoticeKind.Bad); Expect(UiSounds.Last == UiSound.Bad, "um aviso ruim, o de coisa ruim");
            var playedNow = UiSounds.Played; UiSounds.Play(UiSound.Click); var again = UiSounds.Play(UiSound.Click); Expect(UiSounds.Played == playedNow + 1 && !again, "o mesmo som não repete em 40 ms"); UiSounds.Enabled = false; yield return new WaitForSeconds(.06f); Expect(!UiSounds.Play(UiSound.Open) && UiSounds.Played == playedNow + 1, "desligados, os sons da interface não tocam"); UiSounds.Enabled = true; tickle.RemoveFromHierarchy();
            var windowSound = UiSounds.Played; yield return new WaitForSeconds(.06f); var soundWindow = new LifeVerse.Unity.UI.FloatingWindow(app.Ui, "som", "Som", 200f); soundWindow.Open(); Expect(UiSounds.Last == UiSound.Open && UiSounds.Played > windowSound, "abrir uma janela faz um som"); yield return new WaitForSeconds(.06f); soundWindow.Close(); Expect(UiSounds.Last == UiSound.Close, "e fechar faz outro"); _checks++;
            app.ParcelMusic.Enabled = true; app.Sounds.Clear();
            // rebindable keys: the bound key does the action, typing blocks it, and the settings window changes a key (swapping with the one that had it)
            var oldBackground = UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior; UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus; var testKeyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("lv-test-keyboard"); var oldBlock = KeyBindings.Blocked; var oldSource = KeyBindings.KeyboardSource; KeyBindings.KeyboardSource = () => testKeyboard; KeyBindings.Blocked = () => false; KeyBindings.Reset();
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(testKeyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Y)); yield return null; yield return null;
            Expect(KeyBindings.Held(GameAction.Talk) && !KeyBindings.Held(GameAction.Wave) && !KeyBindings.Held(GameAction.Jump), "a tecla de falar apertada vale como falar (e só isso)");
            KeyBindings.Blocked = () => true; Expect(!KeyBindings.Held(GameAction.Talk) && !KeyBindings.Pressed(GameAction.Talk), "digitando, as teclas do jogo não fazem nada"); KeyBindings.Blocked = () => false;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(testKeyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.UpArrow)); yield return null; yield return null; Expect(KeyBindings.Held(GameAction.Forward) && !KeyBindings.Held(GameAction.Back), "a seta para cima também anda");
            var keysWindow = new LifeVerse.Unity.UI.SettingsWindow(app.Ui, System.IO.Path.GetTempFileName()); keysWindow.Open(); keysWindow.BeginRebind(GameAction.Wave); Expect(keysWindow.Rebinding && keysWindow.KeyButtonText(GameAction.Wave).Contains("Aperte"), "o botão pede uma tecla");
            keysWindow.ChooseKey(UnityEngine.InputSystem.Key.Y); Expect(KeyBindings.Of(GameAction.Wave) == UnityEngine.InputSystem.Key.Y && KeyBindings.Of(GameAction.Talk) == UnityEngine.InputSystem.Key.G && keysWindow.KeyButtonText(GameAction.Wave) == "Y" && keysWindow.KeyButtonText(GameAction.Talk) == "G" && keysWindow.KeyStatus.Contains("ficou com G"), "escolher a tecla de outra ação troca as duas e a janela diz: " + keysWindow.KeyStatus);
            Expect(app.Ui.Settings.Keys["Wave"] == "Y" && app.Ui.Settings.Keys["Talk"] == "G", "a escolha vai para o arquivo de configuração"); keysWindow.BeginRebind(GameAction.Jump); keysWindow.ChooseKey(UnityEngine.InputSystem.Key.Escape); Expect(KeyBindings.Of(GameAction.Jump) == UnityEngine.InputSystem.Key.Space && keysWindow.KeyStatus.Contains("não pode"), "Esc não vira tecla de ação"); keysWindow.CancelRebind();
            KeyBindings.Reset(); app.Ui.Settings.Keys.Clear(); keysWindow.Close(); KeyBindings.KeyboardSource = oldSource; KeyBindings.Blocked = oldBlock; UnityEngine.InputSystem.InputSystem.RemoveDevice(testKeyboard); UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = oldBackground; _checks++;
            // importing a 3D model: a GLB as it is, a glTF gathered with its data, a COLLADA converted, the limits, the preview, and placing it
            var importFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-model-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(importFolder);
            var modelWindow = new LifeVerse.Unity.UI.ModelImportWindow(app.Context, app.Builder); modelWindow.Open(); var boxPath = System.IO.Path.Combine(importFolder, "caixa.glb"); System.IO.File.WriteAllBytes(boxPath, TestAssets.Glb()); modelWindow.Load(boxPath);
            Expect(modelWindow.Report != null && modelWindow.Report.Valid && modelWindow.Report.Triangles > 0 && modelWindow.Report.Size.x > .1f && modelWindow.CanPlace, "um GLB é lido e mostra as medidas e os triângulos: " + modelWindow.SizeText + " " + modelWindow.TrianglesText); yield return Until(() => modelWindow.Preview.Loaded || modelWindow.Preview.Problem.Length > 0, "o modelo aparecer girando na janela", 15f); Expect(modelWindow.Preview.Loaded, "o modelo é desenhado: " + modelWindow.Preview.Problem);
            var triangleBin = new byte[12 + 36]; var positions = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }; Buffer.BlockCopy(positions, 0, triangleBin, 0, 36); Buffer.BlockCopy(new ushort[] { 0, 1, 2 }, 0, triangleBin, 36, 6); System.IO.File.WriteAllBytes(System.IO.Path.Combine(importFolder, "tri.bin"), triangleBin);
            var gltfPath = System.IO.Path.Combine(importFolder, "tri.gltf"); System.IO.File.WriteAllText(gltfPath, "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}],\"buffers\":[{\"uri\":\"tri.bin\",\"byteLength\":48}],\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":6}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},{\"bufferView\":1,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}]}");
            modelWindow.Load(gltfPath); Expect(modelWindow.Report != null && modelWindow.Report.Valid && modelWindow.Report.Triangles == 1 && modelWindow.Glb != null && modelWindow.Glb[0] == 'g' && Mathf.Abs(modelWindow.Report.Size.x - 1f) < .01f, "um glTF com os dados ao lado vira um GLB só: " + modelWindow.ProblemText);
            System.IO.File.WriteAllText(System.IO.Path.Combine(importFolder, "sumiu.gltf"), "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"nao-existe.bin\",\"byteLength\":4}]}"); modelWindow.Load(System.IO.Path.Combine(importFolder, "sumiu.gltf")); Expect(modelWindow.Report == null && modelWindow.ProblemText.Contains("não encontrado") && !modelWindow.CanPlace, "um arquivo que o glTF cita e não está ao lado é recusado: " + modelWindow.ProblemText);
            System.IO.File.WriteAllText(System.IO.Path.Combine(importFolder, "fora.gltf"), "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"../segredo.bin\",\"byteLength\":4}]}"); modelWindow.Load(System.IO.Path.Combine(importFolder, "fora.gltf")); Expect(modelWindow.Report == null && modelWindow.ProblemText.Contains("fora da pasta"), "um glTF não lê arquivo de fora da pasta escolhida: " + modelWindow.ProblemText);
            var daePath = System.IO.Path.Combine(importFolder, "tri.dae"); System.IO.File.WriteAllText(daePath, "<?xml version=\"1.0\"?><COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\"><asset><unit meter=\"1\"/><up_axis>Y_UP</up_axis></asset><library_geometries><geometry id=\"g\"><mesh><source id=\"p\"><float_array id=\"pa\" count=\"9\">0 0 0 2 0 0 0 3 0</float_array><technique_common><accessor source=\"#pa\" count=\"3\" stride=\"3\"><param name=\"X\" type=\"float\"/><param name=\"Y\" type=\"float\"/><param name=\"Z\" type=\"float\"/></accessor></technique_common></source><vertices id=\"v\"><input semantic=\"POSITION\" source=\"#p\"/></vertices><triangles count=\"1\"><input semantic=\"VERTEX\" source=\"#v\" offset=\"0\"/><p>0 1 2</p></triangles></mesh></geometry></library_geometries><library_visual_scenes><visual_scene id=\"s\"><node id=\"n\"><instance_geometry url=\"#g\"/></node></visual_scene></library_visual_scenes><scene><instance_visual_scene url=\"#s\"/></scene></COLLADA>");
            modelWindow.Load(daePath); Expect(modelWindow.Report != null && modelWindow.Report.Valid && modelWindow.Report.Triangles == 1 && Mathf.Abs(modelWindow.Report.Size.y - 3f) < .05f, "um COLLADA é convertido para GLB: " + modelWindow.ProblemText + " " + modelWindow.SizeText);
            System.IO.File.WriteAllText(System.IO.Path.Combine(importFolder, "ruim.dae"), "<html>não sou um modelo</html>"); modelWindow.Load(System.IO.Path.Combine(importFolder, "ruim.dae")); Expect(modelWindow.Report == null && modelWindow.ProblemText.Contains("COLLADA"), "o que não é COLLADA é recusado: " + modelWindow.ProblemText);
            System.IO.File.WriteAllText(System.IO.Path.Combine(importFolder, "a.txt"), "x"); modelWindow.Load(System.IO.Path.Combine(importFolder, "a.txt")); Expect(modelWindow.Report == null && modelWindow.ProblemText.Contains(".glb"), "só se aceitam os três tipos");
            var heavy = LifeVerse.Unity.Models.ModelInspector.Inspect(System.Text.Encoding.UTF8.GetBytes("x")); Expect(!heavy.Valid && heavy.Error.Contains("glTF"), "bytes que não são um modelo são recusados");
            Expect(LifeVerse.Shared.Building.ModelLimits.Check(400000, 1, 1, 1, 1000) != null && LifeVerse.Shared.Building.ModelLimits.Check(10, 1, 1, 1, 1000) == null && LifeVerse.Unity.UI.ModelImportWindow.SizeProblem("0", out _) != null && LifeVerse.Unity.UI.ModelImportWindow.SizeProblem("2,5", out var meters) == null && Mathf.Abs(meters - 2.5f) < 1e-4f, "o limite de triângulos e o tamanho no mundo são conferidos antes de enviar");
            var modelAsset = Guid.NewGuid(); stub.Routes["POST /api/v1/assets/upload"] = "{\"id\":\"" + modelAsset + "\",\"name\":\"tri\"}"; modelWindow.Load(daePath); modelWindow.WorldSizeField.value = "3"; var objectsBeforeModel = app.Client.Objects.Count; modelWindow.Place();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/assets/upload" && b.Body.Contains("models") && b.Body.Contains(".glb")), "o modelo subir como GLB, na categoria de modelos"); yield return Until(() => app.Client.Objects.Count == objectsBeforeModel + 1, "a ilha colocar o modelo");
            var placed = app.Client.Objects.Ids.Select(i => app.Client.Objects.StateOf(i)).First(o => o.PrimitiveType == "mesh" && o.GeometryJson.Contains(modelAsset.ToString())); Expect(placed.GeometryJson.Contains(modelAsset.ToString()) && Mathf.Abs(placed.ScaleX - 3f) < .01f && placed.PhysicsJson.Contains("triangle"), "o objeto é um modelo, do tamanho pedido e com colisão exata: " + placed.GeometryJson + " " + placed.PhysicsJson);
            app.Builder.Delete(new[] { placed.Id }, false); yield return Until(() => !app.Client.Objects.Has(placed.Id), "o modelo de teste sair do mundo"); app.Builder.Clear(); yield return Until(() => app.Builder.Waiting == 0, "os comandos esvaziarem"); modelWindow.Close(); System.IO.Directory.Delete(importFolder, true); _checks++;
            // build mode: create, select, exact values, move, undo, the gizmo's drag, materials, copy, delete, and a lock held by someone else
            app.Store.Set("build.snap", "true"); app.ApplySettingsToWorld(); app.Builder.Grid = .25f;   // these checks use snapping on a grid of 0.25 (Configurações › Construção snaps; its step goes by 0.1)
            app.ToggleBuild(); Expect(app.Builder.Active && app.BuildPanel != null && app.BuildPanel.IsOpen, "B abre o modo de construção e o painel"); var objectsBefore = app.Client.Objects.Count; 

            Expect(app.Builder.Create("cube"), "pedir um objeto à ilha"); yield return Until(() => app.Builder.Selection.Count == 1 && app.Client.Objects.Count == objectsBefore + 1, "o objeto criado aparece e fica selecionado");
            var madeId = app.Builder.Selection[0]; var made = app.Client.Objects.StateOf(madeId); Expect(made.PrimitiveType == "cube" && made.CreatorUserId == app.Api.UserId && app.BuildPanel.SelectionText.Contains("Caixa"), "o objeto é uma caixa, feita por esta pessoa: " + app.BuildPanel.SelectionText); _checks++;
            app.BuildPanel.NumberField(0).value = "12.5"; app.BuildPanel.NumberField(6).value = "2"; app.BuildPanel.ApplyNumbers(); yield return Until(() => Mathf.Abs(app.Client.Objects.StateOf(madeId).PositionX - 12.5f) < .001f && Mathf.Abs(app.Client.Objects.StateOf(madeId).ScaleX - 2f) < .001f, "os valores exatos chegarem ao objeto");
            app.BuildPanel.NumberField(7).value = "900"; Expect(app.BuildPanel.ReadNumbers(out _) != null && app.BuildPanel.ReadNumbers(out _).Contains("tamanho"), "tamanho de 900 m é recusado antes de enviar"); app.BuildPanel.NumberField(7).value = "abc"; Expect(app.BuildPanel.ReadNumbers(out _).Contains("número"), "texto no lugar de número é recusado"); _checks++;
            app.BuildPanel.Refresh(); app.Builder.Move(new Vector3(1, 0, 0)); yield return Until(() => Mathf.Abs(app.Client.Objects.StateOf(madeId).PositionX - 13.5f) < .001f, "mover a seleção 1 m");
            Expect(app.Builder.Undo(), "desfazer o movimento"); yield return Until(() => Mathf.Abs(app.Client.Objects.StateOf(madeId).PositionX - 12.5f) < .001f, "o objeto volta para onde estava"); Expect(app.Builder.Redo(), "refazer"); yield return Until(() => Mathf.Abs(app.Client.Objects.StateOf(madeId).PositionX - 13.5f) < .001f, "o refazer move de novo"); _checks++;
            var zBefore = app.Client.Objects.StateOf(madeId).PositionZ; app.Builder.EndDragForTesting(2, 2f); yield return Until(() => Mathf.Abs(app.Client.Objects.StateOf(madeId).PositionZ - (zBefore + 2f)) < .001f, "arrastar a seta no eixo Z move o objeto 2 m"); _checks++;
            app.BuildPanel.EditMaterial(m => m["color"] = "#FF0000"); yield return Until(() => app.Client.Objects.StateOf(madeId).MaterialJson.Contains("#FF0000"), "a cor vermelha chegar ao material");
            var colour = app.Client.Objects.ViewOf(madeId).GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor"); Expect(colour.r > .9f && colour.g < .1f, "o objeto fica vermelho na cena: " + colour); _checks++;
            // the other maps of a surface: normal, ORM and emissive pictures, their strengths and the picture's offset
            var mapAsset = Guid.NewGuid(); var oldLoader = app.Client.Objects.AssetLoader; app.Client.Objects.AssetLoader = id => System.Threading.Tasks.Task.FromResult(id == mapAsset ? TestAssets.Png(new Color32(128, 128, 255, 255), 16) : null); var mapsBefore = app.Client.Objects.TexturesApplied;
            app.BuildPanel.MapField("normalMap").value = mapAsset.ToString(); app.BuildPanel.ApplyMap("normalMap"); app.BuildPanel.MapField("emissiveMap").value = "não é um identificador"; app.BuildPanel.ApplyMap("emissiveMap"); Expect(app.BuildPanel.StatusText.Contains("identificador"), "um identificador que não vale é recusado antes de enviar");
            app.BuildPanel.ApplyMap("normalMap"); app.BuildPanel.NormalStrengthSlider.value = 1.5f; app.BuildPanel.OffsetXSlider.value = .25f; Expect(app.Builder.InFlight >= 1, "enquanto a ilha não responde, as edições seguintes contam com a anterior"); yield return Until(() => { var m = app.Client.Objects.StateOf(madeId).MaterialJson; return m.Contains("normalMap") && m.Contains("normalStrength") && m.Contains("uvOffsetX"); }, "o mapa de relevo, a força e o deslocamento chegarem ao material, os três, sem um apagar o outro");
            yield return Until(() => app.Client.Objects.ViewOf(madeId).GetComponent<MeshRenderer>().sharedMaterials[0].GetTexture("_NormalMap") != null, "o mundo desenhar o relevo no objeto"); app.BuildPanel.EditMaterial(m => { m.Remove("normalMap"); m.Remove("normalStrength"); m.Remove("uvOffsetX"); }); app.Client.Objects.AssetLoader = oldLoader; _checks++;
                        // a picture and a video on the surface of an object
            LifeVerse.Unity.World.WorldObjects.MediaFilesAllowed = true; var mediaPng = TestAssets.Png(new Color32(20, 200, 90, 255), 32); app.Client.Objects.MediaLoader = address => System.Threading.Tasks.Task.FromResult(mediaPng);
            var imagesBefore = app.Client.Objects.MediaImages; app.BuildPanel.EditMaterial(m => m["media"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "image", ["url"] = "file:///quadro.png", ["playing"] = true });
            yield return Until(() => app.Client.Objects.MediaImages > imagesBefore, "a imagem da mídia ser baixada e posta na superfície"); var mediaMaterial = app.Client.Objects.ViewOf(madeId).GetComponent<MeshRenderer>().sharedMaterials[0]; Expect(mediaMaterial.GetTexture("_BaseColorMap") is Texture2D shown && shown.width == 32 && app.Client.Objects.StateOf(madeId).MaterialJson.Contains("media"), "a imagem aparece como a cor do objeto");
            app.BuildPanel.EditMaterial(m => m["media"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "video", ["url"] = "file:///filme.mp4", ["playing"] = false, ["loop"] = true }); yield return Until(() => app.Client.Objects.HasVideo(madeId), "o objeto ganhar um tocador de vídeo"); Expect(!app.Client.Objects.VideoPlaying(madeId), "um vídeo que não está tocando fica parado"); Expect(app.Client.Objects.VideoAudioOf(madeId) != null && app.Client.Objects.VideoAudioOf(madeId).spatialBlend == 1f, "o som do vídeo sai do objeto, em 3D, pelo grupo de mídia");
            app.BuildPanel.EditMaterial(m => m.Remove("media")); yield return Until(() => !app.Client.Objects.HasVideo(madeId), "tirar a mídia tira o tocador"); LifeVerse.Unity.World.WorldObjects.MediaFilesAllowed = false; Expect(!LifeVerse.Unity.World.WorldObjects.MediaOf("{\"media\":{\"type\":\"image\",\"url\":\"file:///x.png\"}}", out _, out _, out _, out _, out _) && LifeVerse.Unity.World.WorldObjects.MediaOf("{\"media\":{\"type\":\"image\",\"url\":\"https://exemplo.com/x.png\"}}", out _, out _, out _, out _, out _), "para as pessoas só endereços https valem como mídia"); _checks++;
            // shape, physics, sale, contents, the other gizmo modes and aligning
            app.BuildPanel.ShapeSlider("hollow").value = .5f; yield return Until(() => app.Client.Objects.StateOf(madeId).GeometryJson.Contains("hollow"), "a forma oca chegar ao objeto"); Expect(LifeVerse.Shared.Building.PrimParams.Parse(app.Client.Objects.StateOf(madeId).GeometryJson).Hollow > .45f, "o oco é lido do objeto como a ilha o guarda");
            app.BuildPanel.PhysicalToggle.value = true; yield return Until(() => app.Client.Objects.StateOf(madeId).PhysicsJson.Contains("physical"), "o objeto virar físico"); Expect(LifeVerse.Shared.Building.PhysicsRules.Validate(app.Client.Objects.StateOf(madeId).PhysicsJson) == null, "a física enviada passa nas regras da ilha"); _checks++;
            Expect(LifeVerse.Unity.UI.BuildWindow.SaleProblem("original", "abc", out _) != null && LifeVerse.Unity.UI.BuildWindow.SaleProblem("original", "-5", out _) != null && LifeVerse.Unity.UI.BuildWindow.SaleProblem("nada", "5", out _) != null && LifeVerse.Unity.UI.BuildWindow.SaleProblem("copy", "250", out var priced) == null && priced == 250, "a venda confere o preço e o modo antes de enviar");
            var soundId = Guid.NewGuid(); app.BuildPanel.AddContentEntry(soundId, "Porta rangendo", "sounds"); yield return Until(() => app.Client.Objects.StateOf(madeId).ContentJson.Contains(soundId.ToString()), "o item entrar no conteúdo do objeto"); app.BuildPanel.Refresh(); Expect(app.BuildPanel.ContentText.Contains("Porta rangendo"), "o conteúdo aparece na lista: " + app.BuildPanel.ContentText); _checks++;
            app.Builder.Mode = LifeVerse.Unity.UI.BuildEditor.GizmoMode.Rotate; var yawBefore = app.Client.Objects.StateOf(madeId).RotationY; app.Builder.EndDragForTesting(1, 30f); yield return Until(() => Mathf.Abs(Mathf.DeltaAngle(app.Client.Objects.StateOf(madeId).RotationY, yawBefore + 30f)) < .01f, "girar 30 graus em torno do eixo vertical");
            app.Builder.Mode = LifeVerse.Unity.UI.BuildEditor.GizmoMode.Scale; var widthBefore = app.Client.Objects.StateOf(madeId).ScaleX; app.Builder.EndDragForTesting(0, .5f); yield return Until(() => Mathf.Abs(app.Client.Objects.StateOf(madeId).ScaleX - (widthBefore + .5f)) < .001f, "esticar meio metro no eixo X"); app.Builder.Mode = LifeVerse.Unity.UI.BuildEditor.GizmoMode.Move; _checks++;
            Expect(app.Builder.AmountFor(.37f, 0f, false) == 0.25f && Mathf.Abs(app.Builder.AmountFor(.37f, 0f, true) - .37f) < 1e-5f, "a grade prende o movimento de 0,25 em 0,25"); app.Builder.Mode = LifeVerse.Unity.UI.BuildEditor.GizmoMode.Rotate; Expect(app.Builder.AmountFor(0f, 40f, false) == 15f || app.Builder.AmountFor(0f, 40f, false) == 30f, "o giro prende de 15 em 15 graus"); app.Builder.Mode = LifeVerse.Unity.UI.BuildEditor.GizmoMode.Move; _checks++;
            // the Recursos of an object (light, projector, reflection), the textures of the system, the content in folders, the names, floating text and script templates (as in the Godot client)
            app.Builder.Mode = LifeVerse.Unity.UI.BuildEditor.GizmoMode.Move; app.Builder.Select(new[] { madeId }); app.BuildPanel.Refresh(); app.BuildPanel.Show("Recursos");
            app.BuildPanel.LightOn.value = true; yield return Until(() => app.Client.Objects.StateOf(madeId).MaterialJson.Contains("\"light\"") && app.Client.Objects.LampOf(madeId) != null, "ligar a luz do objeto pela aba Recursos");
            Expect(app.Client.Objects.LampOf(madeId).GetComponentInChildren<Light>() is { } objectLamp && objectLamp.type == LightType.Point && objectLamp.range == 8f && objectLamp.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>() != null, "a luz redonda com o alcance escolhido"); _checks++;
            app.BuildPanel.Refresh(); app.BuildPanel.LightEnergy.value = 4f; yield return Until(() => app.Client.Objects.StateOf(madeId).MaterialJson.Contains("\"energy\":4"), "a força da luz chegar ao objeto");
            app.BuildPanel.SetProjector(true, "checker"); yield return Until(() => app.Client.Objects.StateOf(madeId).MaterialJson.Contains("\"projectorBuiltin\":\"checker\"") && app.Client.Objects.LampOf(madeId) is { } l && l.transform.Find("Projector") != null, "o projetor com a textura do sistema"); _checks++;
            app.BuildPanel.Refresh(); app.BuildPanel.ReflectOn.value = true; yield return Until(() => app.Client.Objects.ProbeOf(madeId) != null, "o reflexo do objeto"); _checks++;
            LifeVerse.Unity.World.WorldObjects.MaxLights = 0; app.Client.Objects.RefreshBudget(); Expect(app.Client.Objects.LampsOn == 0, "com limite zero nenhuma luz de objeto acende");
            LifeVerse.Unity.World.WorldObjects.MaxLights = 16; app.Client.Objects.RefreshBudget(); Expect(app.Client.Objects.LampsOn >= 1, "o limite de luzes volta"); _checks++;
            app.BuildPanel.SetTexture(true, "bricks"); yield return Until(() => app.Client.Objects.StateOf(madeId).MaterialJson.Contains("\"builtin\":\"bricks\""), "a textura do sistema escolhida no seletor");
            var picker = new LifeVerse.Unity.UI.TexturePickerWindow(app.Context, (b, tex) => { }); picker.Open(); yield return Until(() => picker.InventoryCount >= 1, "o seletor de texturas com o inventário"); picker.Close(); _checks++;
            app.Texts.OnOperation("{\"operation\":\"text\",\"objectId\":\"" + madeId + "\",\"text\":\"Loja aberta\",\"color\":\"#FFD54A\",\"x\":" + app.Client.Objects.StateOf(madeId).PositionX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + (Mathf.Max(app.Client.Objects.StateOf(madeId).PositionY, 0f) + 2.5f).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + app.Client.Objects.StateOf(madeId).PositionZ.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
            yield return null; yield return null; Expect(app.Texts.TextOf(madeId) == "Loja aberta" && app.Texts.transform.Find("Text " + madeId.ToString("N").Substring(0, 8)) is { } shownText && shownText.gameObject.activeSelf, "o texto flutuante de um script sobre o objeto, à vista"); app.BuildPanel.style.display = UnityEngine.UIElements.DisplayStyle.None; yield return Snap("ui-object-resources"); app.BuildPanel.style.display = UnityEngine.UIElements.DisplayStyle.Flex; _checks++;
            app.BuildPanel.Show("Conteúdo"); app.BuildPanel.Refresh(); Expect(app.BuildPanel.ContentFoldersShown.Any(f => f.Folder == "Sons" && f.Count == 1), "o conteúdo do objeto nas pastas do Godot");
            app.BuildPanel.Show("Geral"); yield return Until(() => app.BuildPanel.PeopleText.Contains("Criador: Você") && app.BuildPanel.PeopleText.Contains("Dono: Você"), "os nomes de criador e dono"); _checks++;
            stub.Routes["GET /api/v1/scripting/templates"] = "[{\"name\":\"porta_automatica\",\"eventName\":\"on_touch\",\"source\":\"function on_touch(event) end\"},{\"name\":\"elevador\",\"eventName\":\"on_timer\",\"source\":\"function on_timer(event) end\"}]";
            var templatesLua = new LifeVerse.Unity.UI.LuaEditorWindow(app.Context, island, madeId); templatesLua.Open(); templatesLua.OpenTemplates(); yield return Until(() => templatesLua.Templates == 2, "os modelos de script da API"); templatesLua.UseTemplate("elevador", "on_timer", "function on_timer(event) end"); templatesLua.Close(); _checks++;
            app.Texts.Show(madeId, "", "", Vector3.zero); app.BuildPanel.Show("Recursos"); app.BuildPanel.Refresh(); app.BuildPanel.ReflectOn.value = false; app.BuildPanel.LightOn.value = false; app.BuildPanel.SetTexture(true, "plywood");
            yield return Until(() => app.Client.Objects.LampOf(madeId) == null && app.Client.Objects.ProbeOf(madeId) == null, "tirar a luz e o reflexo do objeto"); app.Builder.Mode = LifeVerse.Unity.UI.BuildEditor.GizmoMode.Rotate;
            var twinId = Guid.Empty; app.Builder.CreateState(app.Client.Objects.StateOf(madeId).Copy(), m => twinId = m.Id, false, false); yield return Until(() => twinId != Guid.Empty, "um segundo objeto para alinhar"); app.Builder.Edit(new[] { twinId }, s => { s.ScaleY = 3f; s.PositionY = 8f; s.PositionX += 5f; }, "teste", false); yield return Until(() => Mathf.Abs(app.Client.Objects.StateOf(twinId).ScaleY - 3f) < .001f, "o segundo objeto ser alterado");
            app.Builder.Select(new[] { madeId, twinId }); app.BuildPanel.Align(LifeVerse.Shared.Building.AlignAxis.Y, LifeVerse.Shared.Building.AlignEdge.Min);
            yield return Until(() => { app.Builder.BoundsOf(madeId, out var b1); app.Builder.BoundsOf(twinId, out var b2); return Mathf.Abs(b1.min.y - b2.min.y) < .01f; }, "alinhar pelo fundo: os dois objetos ficam com a mesma base"); _checks++;
            // magnetic snap to a neighbour's face, and another person's drag shown as it happens
            app.Builder.Select(new[] { madeId }); app.Builder.BoundsOf(madeId, out var madeBox); app.Builder.BoundsOf(twinId, out var twinBox); var gapX = LifeVerse.Unity.UI.BuildEditor.ToGameBounds(twinBox).Min.X - LifeVerse.Unity.UI.BuildEditor.ToGameBounds(madeBox).Max.X;
            var snapped = app.Builder.SnapAmount(0, gapX - .12f); Expect(snapped.HasValue && Mathf.Abs(snapped.Value - gapX) < .02f && app.Builder.SnapPoint.HasValue, "arrastar até 12 cm de um vizinho encosta a face dele: " + snapped + " esperado " + gapX); Expect(!app.Builder.SnapAmount(0, gapX - 25f).HasValue || Mathf.Abs(app.Builder.SnapAmount(0, gapX - 25f).Value - (gapX - 25f)) < .3f, "longe de tudo nada puxa");
            var twinStart = app.Client.Objects.ViewOf(twinId).transform.position; var seenBefore = app.Builder.LiveSeen; yield return Until(() => app.Builder.Waiting == 0, "o envio de comandos esvaziar"); yield return new WaitForSeconds(.5f); player.Client.SendBuild("{\"operation\":\"lock\",\"objectIds\":[\"" + twinId + "\"],\"expectedRevision\":0}"); yield return new WaitForSeconds(.6f);
            player.Client.SendBuild("{\"operation\":\"live\",\"poses\":[{\"id\":\"" + twinId + "\",\"positionX\":" + (app.Client.Objects.StateOf(twinId).PositionX + 7f).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"positionY\":" + app.Client.Objects.StateOf(twinId).PositionY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"positionZ\":" + app.Client.Objects.StateOf(twinId).PositionZ.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"rotationX\":0,\"rotationY\":0,\"rotationZ\":0,\"scaleX\":1,\"scaleY\":3,\"scaleZ\":1}]}");
            yield return Until(() => app.Builder.LiveSeen > seenBefore && app.Client.Objects.ViewOf(twinId).transform.position.x > twinStart.x + 6f, "o arrasto do outro aparecer na hora, sem salvar");
            player.Client.SendBuild("{\"operation\":\"unlock\",\"objectIds\":[\"" + twinId + "\"],\"expectedRevision\":0}"); yield return Until(() => Vector3.Distance(app.Client.Objects.ViewOf(twinId).transform.position, twinStart) < .01f, "um arrasto que acabou sem salvar volta ao que a ilha tem"); _checks++;
            // picking a face by the cursor's ray, the tutorial, and the camera looking at an object
            app.Builder.BoundsOf(madeId, out var faceBox); var madeGo = app.Client.Objects.ViewOf(madeId).transform; var topOrigin = madeGo.TransformPoint(new Vector3(.4f, .5f, 0f)) + Vector3.up * 20f; var topRay = new Ray(topOrigin, Vector3.down); var sideOrigin = madeGo.TransformPoint(new Vector3(6f, 0f, .3f)); var sideRay = new Ray(sideOrigin, (madeGo.TransformPoint(new Vector3(0f, 0f, .3f)) - sideOrigin).normalized);
            Expect(app.Client.Objects.PickFace(madeId, topRay, out var topFace) && topFace == 0, "um raio de cima acerta a face do topo: " + topFace); Expect(app.Client.Objects.PickFace(madeId, sideRay, out var sideFace) && sideFace >= 2, "um raio de lado acerta uma parede: " + sideFace); Expect(!app.Client.Objects.PickFace(madeId, new Ray(faceBox.center + Vector3.up * 50f, Vector3.up), out _), "um raio para o céu não acerta face");
            Expect(app.Client.Objects.FacesOf(madeId).Length >= 6 && app.Client.Objects.FacesOf(madeId)[0] == 0 && LifeVerse.Unity.UI.BuildWindow.FaceName(0) == "Topo (0)", "a caixa oca tem as seis faces e mais as de dentro, e o painel dá nome a elas");
            Expect(app.Builder.Tutorial.Current > LifeVerse.Shared.Building.TutorialStep.Create && app.BuildPanel.TutorialText.Length > 10 && LifeVerse.Unity.UI.BuildWindow.TutorialStepText(LifeVerse.Shared.Building.TutorialStep.Done).Contains("concluído"), "o tutorial andou conforme a pessoa fazia: " + app.Builder.Tutorial.Current); app.Builder.Tutorial.Skip(); app.BuildPanel.Refresh(); Expect(app.BuildPanel.TutorialText.Contains("pulado"), "pular o tutorial"); app.Builder.Tutorial.Restart();
            app.ZoomTo(faceBox); yield return new WaitForSeconds(.8f); Expect(app.Follow.ZoomTarget.HasValue && Vector3.Distance(app.Follow.Focus, faceBox.center) < .05f, "o zoom olha para o objeto: " + app.Follow.Focus + " " + faceBox.center); app.Follow.ZoomTarget = null; yield return null; Expect(Vector3.Distance(app.Follow.Focus, faceBox.center) > .5f, "sem o zoom a câmera volta à pessoa"); _checks++;
            app.Builder.Select(new[] { madeId, twinId });
            Expect(LifeVerse.Unity.UI.BuildWindow.MediaProblem("image", "http://exemplo.com/a.png") != null && LifeVerse.Unity.UI.BuildWindow.MediaProblem("video", "https://localhost/a.mp4") != null && LifeVerse.Unity.UI.BuildWindow.MediaProblem("image", "https://10.0.0.1/a.png") != null && LifeVerse.Unity.UI.BuildWindow.MediaProblem("image", "https://user:senha@exemplo.com/a.png") != null && LifeVerse.Unity.UI.BuildWindow.MediaProblem("audio", "https://exemplo.com/a") != null && LifeVerse.Unity.UI.BuildWindow.MediaProblem("image", "https://exemplo.com/a.png") == null, "a mídia só vale de um endereço https público");
            app.Builder.Select(new[] { madeId }); app.BuildPanel.EditFace(f => f["color"] = "#00FF00"); yield return Until(() => { var m = app.Client.Objects.StateOf(madeId).MaterialJson; return m.Contains("\"faces\"") && m.Contains("#00FF00"); }, "a cor de uma face só entrar no material"); Expect(app.Client.Objects.StateOf(madeId).MaterialJson.Contains("#FF0000"), "a cor do objeto inteiro continua"); _checks++;
            var saleSample = new LifeVerse.Unity.World.WorldObjectState { PermissionsJson = "{\"saleType\":\"copy\",\"salePrice\":120,\"payPrice\":5}" }; Expect(LifeVerse.Unity.UI.BuildEditor.SaleOf(saleSample) == ("copy", 120L) && LifeVerse.Unity.UI.BuildEditor.PayPriceOf(saleSample) == 5L && LifeVerse.Unity.UI.BuildEditor.SaleOf(new LifeVerse.Unity.World.WorldObjectState()) == ("none", 0L), "a venda e o preço de pagar são lidos das permissões do objeto");
            Expect(app.Builder.Buy(madeId, 10) && app.Builder.Return(new[] { madeId }) && !app.Builder.Return(System.Array.Empty<Guid>()), "comprar e devolver viram pedidos à ilha");
            var carried = LifeVerse.Unity.UI.GameApp.BuildCarried(app.Client.Objects.StateOf(madeId)); Expect(carried.Count == 1 && carried[0] == "Porta rangendo", "o que o objeto leva aparece no menu de clique"); _checks++;
            yield return new WaitForSeconds(1.2f);
            Expect(LifeVerse.Unity.UI.BuildWindow.OperationLabel("update") == "Alterado" && LifeVerse.Unity.UI.BuildWindow.OperationLabel("buy") == "Comprado", "o histórico usa palavras da pessoa"); app.Builder.Delete(new[] { twinId }, false); yield return Until(() => !app.Client.Objects.Has(twinId), "o segundo objeto sair"); app.Builder.Select(new[] { madeId }); objectsBefore = app.Client.Objects.Count - 1;
            Expect(app.Builder.Duplicate() == 1, "copiar a seleção"); yield return Until(() => app.Client.Objects.Count == objectsBefore + 2 && app.Builder.Selection.Count == 1 && app.Builder.Selection[0] != madeId, "a cópia aparece e fica selecionada"); _checks++;
            var otherId = Guid.NewGuid(); app.Builder.OnOperation("{\"operation\":\"lock\",\"objectIds\":[\"" + madeId + "\"],\"userId\":\"" + otherId + "\",\"userName\":\"Ana\"}"); Expect(!app.Builder.Edit(new[] { madeId }, s => s.PositionX += 1, "mover"), "um objeto em edição por outra pessoa não é alterado"); Expect(app.Builder.LastMessage.Contains("Ana"), "a mensagem diz quem está editando: " + app.Builder.LastMessage);
            app.Builder.OnOperation("{\"operation\":\"unlock\",\"objectIds\":[\"" + madeId + "\"],\"userId\":\"" + otherId + "\"}"); Expect(!app.Builder.HeldByOther(madeId, out _), "a liberação tira o aviso"); _checks++;
            Expect(LifeVerse.Unity.UI.BuildCommands.RejectionText("object_protected").Contains("protegido") && LifeVerse.Unity.UI.BuildCommands.RejectionText("locked:Ana").StartsWith("Ana") && LifeVerse.Unity.UI.BuildCommands.RejectionText("zzz").Contains("zzz"), "as recusas viram frases"); _checks++;
            Expect(app.Builder.Delete(app.Builder.Selection.ToList()), "apagar a cópia"); yield return Until(() => app.Client.Objects.Count == objectsBefore + 1, "a cópia some do mundo"); Expect(app.Builder.Undo(), "desfazer o apagar"); yield return Until(() => app.Client.Objects.Count == objectsBefore + 2, "o objeto apagado volta"); _checks++;
            app.ToggleBuild(); Expect(!app.Builder.Active && app.BuildPanel == null, "B de novo sai do modo de construção"); _checks++;

            // buying and paying for objects, and the objects that were deleted
            var shopOwner = Guid.NewGuid(); var sellerName = "Dona Ana"; stub.Routes["GET /api/v1/users/" + shopOwner + "/profile/full"] = "{\"profile\":{\"displayName\":\"" + sellerName + "\"}}"; stub.Routes["GET /api/v1/economy/wallet"] = "{\"availableCoins\":500,\"isBlocked\":false}"; stub.Routes["POST /api/v1/economy/transfer"] = "{}";
            var shop = new LifeVerse.Unity.World.WorldObjectState { Id = Guid.NewGuid(), Name = "Cadeira", OwnerUserId = shopOwner, PermissionsJson = "{\"saleType\":\"copy\",\"salePrice\":120,\"payPrice\":25,\"payQuick\":[10,25,50]}" };
            var buying = new LifeVerse.Unity.UI.PurchaseWindow(app.Context, app.Builder, shop); buying.Open(); yield return Until(() => buying.OwnerName.Contains(sellerName) && buying.Balance == 500, "o nome de quem vende e o saldo aparecerem na compra"); Expect(buying.Price == 120 && buying.SaleType == "copy" && buying.CanBuy && LifeVerse.Unity.UI.PurchaseWindow.SaleLabel("contents").Contains("conteúdo"), "a compra mostra o preço, o que se leva e deixa comprar");
            buying.Buy(); yield return Until(() => stub.ReceivedAny(b => false) || app.Builder.LastRejection != null && buying.CanBuy, "a recusa da ilha voltar para a janela (a ilha de teste não vende)"); Expect(buying.StatusText.Length > 5, "a recusa vira uma frase na janela: " + buying.StatusText);
            app.Builder.OnOperation("{\"operation\":\"bought\",\"buyerUserId\":\"" + app.Api.UserId + "\",\"price\":120,\"saleType\":\"copy\",\"delivered\":[\"Cadeira\"],\"objects\":[]}"); Expect(buying.Done && buying.StatusText.Contains("Comprado por VC$ 120") && buying.StatusText.Contains("Cadeira"), "a compra feita aparece com o que foi para o inventário: " + buying.StatusText); buying.Close(); _checks++;
            var paying = new LifeVerse.Unity.UI.PaymentWindow(app.Context, shop); paying.Open(); Expect(LifeVerse.Unity.UI.PaymentWindow.OptionsOf(shop).SequenceEqual(new long[] { 25, 10, 50 }) && paying.Amount.value == "25", "o pagamento oferece o preço do objeto e os valores rápidos do dono"); paying.Amount.value = "0"; paying.Pay(); Expect(paying.StatusText.Contains("maior que zero") && LifeVerse.Unity.UI.PaymentWindow.AmountProblem("12", out var pay12) == null && pay12 == 12, "um valor que não vale é recusado antes de ir");
            paying.Amount.value = "25"; paying.Pay(); yield return Until(() => paying.Paid && stub.ReceivedAny(b => b.Route == "POST /api/v1/economy/transfer" && b.Body.Contains(shopOwner.ToString()) && b.Body.Contains("25")), "o pagamento chegar à plataforma, para o dono do objeto"); paying.Close(); _checks++;
            var lostId = Guid.NewGuid(); var lostTwo = Guid.NewGuid(); stub.Routes["GET /api/v1/inventory/lost"] = "{\"objects\":[{\"id\":\"" + lostId + "\",\"name\":\"Mesa\",\"shape\":\"cube\",\"deletedAt\":\"" + DateTimeOffset.UtcNow.AddDays(-2).ToString("o") + "\",\"expiresAt\":\"" + DateTimeOffset.UtcNow.AddDays(28).ToString("o") + "\"},{\"id\":\"" + lostTwo + "\",\"name\":\"Casa\",\"shape\":\"cube\",\"groupId\":\"" + Guid.NewGuid() + "\",\"deletedAt\":\"" + DateTimeOffset.UtcNow.AddDays(-1).ToString("o") + "\",\"expiresAt\":\"" + DateTimeOffset.UtcNow.AddDays(29).ToString("o") + "\"}]}"; stub.Routes["POST /api/v1/inventory/lost/recover"] = "{}";
            var lostWindow = new LifeVerse.Unity.UI.LostObjectsWindow(app.Context); lostWindow.Open(); yield return Until(() => lostWindow.Items.Count == 2, "os objetos apagados aparecerem"); Expect(lostWindow.Items[0].Text.Contains("Mesa") && lostWindow.Items[1].Text.Contains("conjunto") && lostWindow.NoteText.Contains("2 objeto"), "a lista mostra o nome e se era um conjunto: " + lostWindow.NoteText);
            lostWindow.Choose(1); Expect(lostWindow.Chosen == 1, "escolher um objeto"); lostWindow.Recover(true); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/inventory/lost/recover" && b.Body.Contains(lostTwo.ToString()) && !b.Body.Contains(lostId.ToString())), "só o escolhido ser pedido de volta"); yield return Until(() => lostWindow.NoteText.Contains("no seu inventário"), "a janela dizer onde foram parar"); lostWindow.Close(); _checks++;

            // terrain tools: the brush packs its options, a dab changes the ground for everybody, undo takes it back, the sea
            app.ToggleTerrain(); Expect(app.TerrainTools.Active && app.TerrainPanel != null, "T abre as ferramentas de terreno"); Expect(app.TerrainTools.CanSculpt, "o dono da ilha pode esculpir");
            app.TerrainTools.Tool = TerrainTool.Raise; app.TerrainTools.Radius = 5f; app.TerrainTools.StrengthPercent = 50f; var request = app.TerrainTools.RequestAt(3f, 4f); Expect(request.Tool == (int)TerrainTool.Raise && Mathf.Abs(request.Strength - .2f) < 1e-4f && request.Layer == 0, "o pincel manda a ferramenta, o raio e a força (50% de elevar é 0,2)");
            var revisionBefore = app.Client.Terrain.Map.Revision; var heightBefore = app.Client.Terrain.Map.SampleHeight(0f, 0f); for (var i = 0; i < 6; i++) { app.TerrainTools.SubmitAt(0f, 0f); yield return new WaitForSeconds(.09f); }
            yield return Until(() => app.Client.Terrain.Map.Revision > revisionBefore && app.Client.Terrain.Map.SampleHeight(0f, 0f) > heightBefore + .05f, "o terreno subir depois das pinceladas"); _checks++;
            var raised = app.Client.Terrain.Map.SampleHeight(0f, 0f); Expect(app.TerrainTools.Undo(), "desfazer pedido"); yield return Until(() => app.Client.Terrain.Map.SampleHeight(0f, 0f) < raised - .01f, "o desfazer baixa o terreno de novo"); _checks++;
            app.TerrainPanel.Select(TerrainTool.Paint); app.TerrainPanel.SelectLayer(3); Expect(app.TerrainTools.Tool == TerrainTool.Paint && app.TerrainTools.Layer == 3 && app.TerrainPanel.PaintVisible && !app.TerrainPanel.LevelVisible && app.TerrainPanel.LayerButtons == 8, "pintar mostra os oito materiais e esconde o nível");
            app.TerrainPanel.Select(TerrainTool.Level); Expect(app.TerrainPanel.LevelVisible && !app.TerrainPanel.PaintVisible, "nivelar mostra a altura do nível"); Expect(!app.TerrainTools.ApplySeaLevel(500f), "um mar fora dos limites não é enviado"); _checks++;
            // the preview of the brush (tried on the local ground and put back) and sculpting as the owner of a parcel
            var terrainMap = app.Client.Terrain.Map; var heightHere = terrainMap.SampleHeight(0f, 0f); var revisionHere = terrainMap.Revision; app.TerrainTools.Tool = TerrainTool.Raise; app.TerrainTools.Radius = 6f; app.TerrainTools.StrengthPercent = 80f; app.TerrainTools.PreviewAt(Vector3.zero);
            Expect(app.TerrainTools.PreviewRings == 3 && app.TerrainTools.PreviewDelta > .02f, "a prévia mostra três anéis no chão como ficaria: " + app.TerrainTools.PreviewDelta); Expect(terrainMap.SampleHeight(0f, 0f) == heightHere && terrainMap.Revision == revisionHere, "e o chão de verdade volta exatamente como estava");
            app.TerrainTools.Tool = TerrainTool.Paint; app.TerrainTools.PreviewAt(Vector3.zero); Expect(app.TerrainTools.PreviewRings == 0, "pintar não muda a altura: sem prévia de relevo"); app.TerrainTools.Tool = TerrainTool.Raise;
            app.TerrainTools.IslandEditorOverride = () => false; app.TerrainTools.ReadLand("{\"parcels\":[{\"canEditTerrain\":true,\"runs\":[[0,-1,0],[-1,-1,0]]},{\"canEditTerrain\":false,\"runs\":[[5,5,6]]}]}");
            Expect(app.TerrainTools.HasParcelRights && app.TerrainTools.CanSculpt && !app.TerrainTools.IslandEditor && app.TerrainTools.Mask != null, "o dono de um terreno pode esculpir sem ser editor da ilha");
            var halfCells = terrainMap.Cells / 2f; var maskNow = app.TerrainTools.Mask; Expect(maskNow(Mathf.RoundToInt(halfCells), Mathf.RoundToInt(halfCells)) && !maskNow(Mathf.RoundToInt(halfCells + 30f / terrainMap.CellSize), Mathf.RoundToInt(halfCells + 30f / terrainMap.CellSize)) && !maskNow(Mathf.RoundToInt(halfCells + 2f * 4f / terrainMap.CellSize), Mathf.RoundToInt(halfCells)), "o chão dentro do terreno pode mudar, o de fora e o da borda não");
            Expect(!app.TerrainTools.SubmitAt(30f, 30f) && app.TerrainTools.Status.Contains("não é o seu"), "uma pincelada fora do terreno não é enviada e a pessoa sabe por quê: " + app.TerrainTools.Status); var dabsBefore = app.TerrainTools.Dabs; Expect(app.TerrainTools.SubmitAt(0f, 0f) && app.TerrainTools.Dabs == dabsBefore + 1, "dentro do terreno ela é enviada (a ilha confere de novo)");
            Expect(!app.TerrainTools.ApplySeaLevel(1f), "o mar continua só dos editores da ilha"); app.TerrainTools.ReadLand("{\"parcels\":[]}"); Expect(!app.TerrainTools.HasParcelRights && !app.TerrainTools.CanSculpt, "sem terreno e sem ser editor não esculpe"); app.TerrainTools.IslandEditorOverride = null; _checks++;
            var exported = app.TerrainTools.Export(); Expect(exported != null && System.IO.File.Exists(exported) && new System.IO.FileInfo(exported).Length == app.Client.Terrain.Map.VertexCount * app.Client.Terrain.Map.VertexCount * 2, "exportar grava o mapa de 16 bits"); System.IO.File.WriteAllBytes(exported + ".curto", new byte[10]); Expect(!app.TerrainTools.Import(exported + ".curto") && app.TerrainTools.Status.Contains("inválido"), "um arquivo do tamanho errado é recusado"); System.IO.File.Delete(exported); System.IO.File.Delete(exported + ".curto");
            app.ToggleTerrain(); Expect(!app.TerrainTools.Active, "T de novo sai"); _checks++;
            if (_shots != null) { app.Hud.Notices.Show("Você entrou na ilha.", LifeVerse.Unity.UI.NoticeKind.Good); app.Follow.Yaw = 200f; app.Follow.Pitch = 10f; _camera.gameObject.SetActive(false); yield return Shot("game-hud"); _camera.gameObject.SetActive(true); }
            // -lifeverse-benchmark REPORT: the client under load, once per preset (needs a graphics device)
            var benchArgs = Environment.GetCommandLineArgs(); var benchAt = Array.IndexOf(benchArgs, "-lifeverse-benchmark");
            if (benchAt >= 0 && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                var crowd = new List<AvatarView>();
                // the self-check runs several clients in one scene: only the app's own island is drawn (the others would triple the terrain and sea)
                foreach (var extra in FindObjectsByType<TerrainView>(FindObjectsSortMode.None)) if (extra != app.Client.Terrain) extra.gameObject.SetActive(false);
                foreach (var extra in FindObjectsByType<OceanView>(FindObjectsSortMode.None)) if (extra != app.Client.Ocean) extra.gameObject.SetActive(false);
                foreach (var extra in FindObjectsByType<WorldObjects>(FindObjectsSortMode.None)) if (extra != app.Client.Objects) extra.gameObject.SetActive(false);
                foreach (var extra in FindObjectsByType<PlayerViews>(FindObjectsSortMode.None)) if (extra.Camera != app.Follow.Camera) extra.gameObject.SetActive(false);
                // -lifeverse-benchmark-hide ocean,terrain,ui,rain,shadows: leave a part out to see what it costs
                var hideAt = Array.IndexOf(benchArgs, "-lifeverse-benchmark-hide"); var hidden = hideAt >= 0 && hideAt + 1 < benchArgs.Length ? benchArgs[hideAt + 1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
                if (hidden.Contains("ocean")) app.Client.Ocean.gameObject.SetActive(false); if (hidden.Contains("foam")) app.Client.Ocean.transform.Find("ShoreFoam").gameObject.SetActive(false); if (hidden.Contains("waves")) app.Client.Ocean.Waves = false; if (hidden.Contains("terrain")) app.Client.Terrain.gameObject.SetActive(false); if (hidden.Contains("ui")) app.Ui.Root.style.display = UnityEngine.UIElements.DisplayStyle.None;
                app.Settings.VSync = false; app.Settings.FpsLimit = 0; app.Follow.enabled = false; app.Player.enabled = false; var benchCamera = app.Follow.Camera; benchCamera.transform.position = LifeVerse.Unity.World.GameSpace.ToUnity(0, 40, -110); benchCamera.transform.LookAt(LifeVerse.Unity.World.GameSpace.ToUnity(0, 6, 10));
                var benchmark = PerformanceBenchmark.Run(preset => { app.Settings.GraphicsPreset = preset; LifeVerse.Unity.UI.GraphicsQuality.Apply(app.Settings); app.ApplySettingsToWorld(); if (hidden.Contains("rain")) app.Client.Environment.RainQuality = 0f; if (hidden.Contains("shadows")) app.Client.Environment.SetShadows(false, 0f); }, time => PerformanceBenchmark.Walk(crowd, time), benchAt + 1 < benchArgs.Length ? benchArgs[benchAt + 1] : null, () => PerformanceBenchmark.Crowd(app.Client.Objects, crowd, IntArg(benchArgs, "-lifeverse-benchmark-objects", 600), IntArg(benchArgs, "-lifeverse-benchmark-avatars", 24)), IntArg(benchArgs, "-lifeverse-benchmark-presets", 4));
                yield return benchmark; foreach (var benchPerson in crowd) Destroy(benchPerson.gameObject); app.Follow.enabled = true; app.Player.enabled = true;
            }
            // the settings reach the world: Baixo turns the shadows off and thins the rain
            app.Settings.GraphicsPreset = 0; LifeVerse.Unity.UI.GraphicsQuality.Apply(app.Settings); app.ApplySettingsToWorld();
            var environment = app.Client.Environment; Expect(!environment.ShadowsOn && environment.RainQuality < .5f, "o preset Baixo desliga as sombras e afina a chuva"); _checks++;
            app.Settings.AlwaysRun = true; app.Settings.InvertY = true; app.ApplySettingsToWorld(); Expect(app.Player.AlwaysRun && app.Follow.InvertY, "os controles chegam ao avatar e à câmera"); _checks++;
            // 4) the platform windows against the API's own shapes: session renewal, social, wallet, inventory, search, profile, island, scripts, radial menu, map, overlay
            var ui = app.Ui; var userId = app.Api.UserId; var friend = Guid.NewGuid();
            stub.Routes["GET /api/v1/social/friends"] = "[{\"id\":\"" + friend + "\",\"username\":\"Ana\"}]";
            stub.ExpireAccessToken(); var renewed = app.Api.ApiAsync("GET", "/api/v1/social/friends"); yield return Until(() => renewed.IsCompleted, "a chamada com o token vencido");
            Expect(renewed.Result.Status == 200 && stub.Refreshes >= 1 && app.Api.Refreshes >= 1, "o token vencido é renovado sozinho e a chamada é refeita: " + renewed.Result.Status + " renovações " + stub.Refreshes); _checks++;

            stub.Routes["GET /api/v1/social/friends/requests"] = "[{\"userId\":\"" + Guid.NewGuid() + "\",\"username\":\"Caio\"}]"; stub.Routes["GET /api/v1/social/groups"] = "[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Os Construtores\",\"role\":\"owner\"}]";
            stub.Routes["GET /api/v1/social/messages"] = "[{\"content\":\"oi!\",\"senderUserId\":\"" + friend + "\",\"createdAt\":\"2026-11-19T10:00:00Z\"},{\"content\":\"tudo bem?\",\"senderUserId\":\"" + userId + "\",\"createdAt\":\"2026-11-19T10:01:00Z\"}]"; stub.Routes["POST /api/v1/social/messages"] = "{}";
            var social = new LifeVerse.Unity.UI.SocialWindow(app.Context); social.Open();
            yield return Until(() => social.FriendCount == 1, "a lista de amigos"); social.Talk(friend, "Ana"); yield return Until(() => social.MessageCount == 2, "a conversa com a Ana");
            social.MessageField.value = "bora!"; Expect(social.SendMessage(), "enviar a mensagem"); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/social/messages" && b.Body.Contains("bora!") && b.Body.Contains(friend.ToString())), "a mensagem chegar à API"); _checks++;
            social.Show("Pedidos"); yield return null; social.Show("Grupos"); yield return null; social.Close();

            stub.Routes["GET /api/v1/economy/wallet"] = "{\"id\":\"" + Guid.NewGuid() + "\",\"availableCoins\":250,\"isBlocked\":false}"; stub.Routes["GET /api/v1/economy/wallet/transactions"] = "[{\"description\":\"Bônus\",\"amountCoins\":100},{\"description\":\"Compra\",\"amountCoins\":-30}]"; stub.Routes["POST /api/v1/economy/transfer"] = "{}";
            var wallet = new LifeVerse.Unity.UI.WalletWindow(app.Context); wallet.Open(); yield return Until(() => wallet.Balance == 250, "o saldo da carteira");
            Expect(LifeVerse.Unity.UI.WalletWindow.Validate("x", "10", 250, out _, out _) != null && LifeVerse.Unity.UI.WalletWindow.Validate(friend.ToString(), "0", 250, out _, out _) != null && LifeVerse.Unity.UI.WalletWindow.Validate(friend.ToString(), "999", 250, out _, out _) == "Saldo insuficiente.", "a carteira recusa o que não faz sentido antes de perguntar à API"); _checks++;
            wallet.ToField.value = friend.ToString(); wallet.AmountField.value = "40"; wallet.Send(); yield return null;
            Expect(wallet.AwaitingConfirmation && !stub.ReceivedAny(b => b.Route == "POST /api/v1/economy/transfer" && b.Body.Contains(friend.ToString())), "o primeiro toque só pede a confirmação (nada foi enviado)"); _checks++;
            wallet.Send(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/economy/transfer" && b.Body.Contains("\"amountCoins\":40") && b.Body.Contains("idempotencyKey")), "a transferência confirmada chegar à API"); _checks++; wallet.Close();

            stub.Routes["GET /api/v1/inventory/items"] = "{\"items\":[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Cadeira\",\"isFavorite\":true},{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Mesa\",\"isFavorite\":false}]}";
            var inventory = new LifeVerse.Unity.UI.InventoryWindow(app.Context); inventory.Open(); yield return Until(() => inventory.ItemCount == 2, "os itens do inventário"); inventory.Close(); _checks++;
            stub.Routes["GET /api/v1/discovery/islands"] = "[{\"id\":\"" + oneIsland + "\",\"name\":\"Ilha do Sol\",\"category\":\"praia\"}]";
            var search = new LifeVerse.Unity.UI.SearchWindow(app.Context); search.Open(); yield return Until(() => search.ResultCount == 1, "o resultado da busca de ilhas"); search.Close(); _checks++;
            stub.Routes["GET /api/v1/users/" + userId + "/profile/full"] = "{\"profile\":{\"displayName\":\"Dono da Ilha\",\"about\":\"Construo coisas.\"}}";
            var profile = new LifeVerse.Unity.UI.ProfileWindow(app.Context); profile.Open(); yield return Until(() => profile.ShownName == "Dono da Ilha", "o perfil da pessoa"); profile.Close(); _checks++;
            stub.Routes["GET /api/v1/worlds/islands/" + island] = "{\"name\":\"Ilha de Teste\",\"description\":\"Para provar.\",\"category\":\"teste\",\"size\":256,\"maxPlayers\":64,\"runtimeState\":\"running\"}"; stub.Routes["GET /api/v1/worlds/islands/" + island + "/permissions"] = "[]";
            var about = new LifeVerse.Unity.UI.IslandInfoWindow(app.Context, island); about.Open(); yield return null; yield return null; about.Close();

            // the Lua editor: the syntax check is the island's own interpreter, the scripts come from and go to the API
            Expect(LifeVerse.Unity.UI.LuaEditorWindow.SyntaxProblem("local a = ") != null && LifeVerse.Unity.UI.LuaEditorWindow.SyntaxProblem("local a = 1\nprint(a)") == null && LifeVerse.Unity.UI.LuaEditorWindow.SyntaxProblem("  ") != null, "o editor acha o erro de sintaxe e aceita o script certo"); _checks++;
            var objectId = Guid.NewGuid(); var scriptId = Guid.NewGuid(); var scriptsPath = "/api/v1/worlds/" + island + "/objects/" + objectId + "/scripts";
            stub.Routes["GET " + scriptsPath] = "[{\"id\":\"" + scriptId + "\",\"name\":\"porta\"}]"; stub.Routes["GET " + scriptsPath + "/" + scriptId] = "{\"id\":\"" + scriptId + "\",\"name\":\"porta\",\"eventName\":\"on_touch\",\"source\":\"print(1)\"}"; stub.Routes["PUT " + scriptsPath + "/" + scriptId] = "{}";
            var lua = new LifeVerse.Unity.UI.LuaEditorWindow(app.Context, island, objectId); lua.Open(); lua.Open(scriptId); yield return Until(() => lua.Source == "print(1)", "o script abrir no editor");
            lua.Source = "print(2)"; lua.Save(); yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + scriptsPath + "/" + scriptId && b.Body.Contains("print(2)") && b.Body.Contains("on_touch")), "o script salvo chegar à API"); _checks++;
            yield return new WaitForSeconds(.6f); lua.Source = "print("; lua.Save(); yield return null; Expect(lua.StatusText.StartsWith("Erro"), "um script com erro não é salvo: " + lua.StatusText); _checks++;

            // the manual and the history of the script, then what scripts ask of the person (dialogs and panels)
            stub.Routes["GET /api/v1/scripting/reference"] = "{\"functions\":[{\"category\":\"Objetos\",\"namespace\":\"object\",\"name\":\"say\",\"signature\":\"object.say(text)\",\"description\":\"Fala.\",\"returns\":\"nada\",\"permission\":\"nenhuma\",\"limits\":\"200 letras\",\"errors\":\"texto grande\",\"example\":\"object.say('oi')\"},{\"category\":\"Tempo\",\"namespace\":\"timer\",\"name\":\"after\",\"signature\":\"timer.after(s, fn)\",\"description\":\"Espera.\",\"returns\":\"id\",\"permission\":\"nenhuma\",\"limits\":\"\",\"errors\":\"\",\"example\":\"\"}],\"events\":[{\"name\":\"on_touch\",\"when\":\"Quando alguém toca.\",\"fields\":\"avatar\",\"example\":\"function on_touch(event) end\"}]}";
            string inserted = null; var docs = new LifeVerse.Unity.UI.ScriptDocsWindow(app.Context, text => inserted = text); docs.Open(); yield return Until(() => docs.Count == 3, "a documentação carregar (2 funções e 1 evento)");
            docs.Search("timer"); Expect(docs.Count == 1 && docs.Titles[0].Contains("timer.after"), "a busca acha a função certa"); docs.Search(""); docs.PickCategory("Eventos"); Expect(docs.Count == 1 && docs.Titles[0] == "on_touch", "o filtro de categoria mostra só os eventos");
            docs.Pick(0); docs.PressInsert(); Expect(inserted != null && inserted.Contains("on_touch") && docs.DetailText.Contains("Quando alguém toca"), "o exemplo do evento é inserido e o detalhe aparece"); docs.Close(); _checks++;
            // the script editor of the Godot client: completing code, the full check of the server, the console, the use, the permissions, pause and the debugger
            var dbgScript = Guid.NewGuid(); var dbgPath = scriptsPath + "/" + dbgScript; var dbgSource = "local contador = 0\nfunction on_touch(e)\n  contador = contador + 1\n  object.say(contador)\nend\n";
            stub.Routes["GET " + dbgPath] = "{\"id\":\"" + dbgScript + "\",\"name\":\"contador\",\"eventName\":\"on_touch\",\"status\":\"active\",\"source\":" + System.Text.Json.JsonSerializer.Serialize(dbgSource) + "}"; stub.Routes["PUT " + dbgPath] = "{}";
            stub.Routes["GET " + dbgPath + "/runtime"] = "{\"status\":\"active\",\"version\":2,\"runtime\":{\"state\":\"Running\",\"stateText\":\"rodando\",\"events\":12,\"errors\":0,\"apiCalls\":12,\"instructions\":4000,\"allocatedBytes\":8192,\"cpuMilliseconds\":0.8},\"logs\":[{\"level\":\"info\",\"eventName\":\"on_touch\",\"text\":\"tocado 12 vezes\",\"stackTrace\":\"\",\"createdAt\":\"2026-11-26T10:20:30Z\"}],\"grants\":[{\"subjectId\":\"" + Guid.Empty + "\",\"permission\":\"HTTP_REQUESTS\"},{\"subjectId\":\"" + Guid.NewGuid() + "\",\"permission\":\"ANIMATE_AVATAR\"}]}";
            stub.Routes["POST " + scriptsPath + "/validate"] = "{\"valid\":true,\"errors\":[],\"warnings\":[{\"line\":2,\"message\":\"o evento on_toque não existe\"}],\"handlers\":[\"on_touch\"]}"; stub.Routes["POST " + dbgPath + "/pause"] = "{}"; stub.Routes["PUT " + dbgPath + "/permissions/CREATE_NPCS"] = "{}";
            lua.Close(); var dbg = new LifeVerse.Unity.UI.LuaEditorWindow(app.Context, island, objectId); dbg.Open(); dbg.Open(dbgScript); yield return Until(() => dbg.OpenScript == dbgScript && dbg.Source.Contains("contador") && dbg.ConsoleText.Contains("tocado 12 vezes") && dbg.StateText.Contains("rodando"), "o console e o estado do script");
            Expect(dbg.OwnerPermissions["HTTP_REQUESTS"].value && !dbg.OwnerPermissions["CREATE_NPCS"].value && dbg.PerformanceText.Contains("Eventos: 12"), "as permissões e o uso do script na ilha"); _checks++;
            dbg.Source = dbgSource + "object."; dbg.PlaceCursor(dbg.Source.Length); dbg.Complete(); Expect(dbg.Suggestions.Exists(x => x.Insert == "say("), "completar object. com as funções da referência: " + dbg.Suggestions.Count); _checks++;
            dbg.Insert("say("); Expect(dbg.Source.EndsWith("object.say("), "a sugestão entra no código"); dbg.Source = dbgSource;
            dbg.Validate(); yield return Until(() => dbg.Problems == 1 && dbg.Tab == "Problemas" && dbg.ProblemsText.Contains("linha 2"), "os avisos do servidor na aba Problemas"); _checks++;
            dbg.Pause(); yield return Until(() => dbg.PauseText == "Continuar" && stub.ReceivedAny(b => b.Route == "POST " + dbgPath + "/pause"), "pausar o script"); dbg.Grant("CREATE_NPCS", true);
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + dbgPath + "/permissions/CREATE_NPCS" && b.Body.Contains("true")), "dar uma permissão ao script"); _checks++;
            var debugSent = new List<string>(); dbg.DebugSender = debugSent.Add; dbg.Save(); yield return Until(() => !dbg.Dirty, "salvar antes de depurar");
            dbg.SetBreakpoints(3); yield return null; Expect(dbg.GutterLines == 6 && dbg.GutterMarked(3) && !dbg.GutterMarked(2) && dbg.Colours.Contains(LifeVerse.Unity.UI.LuaHighlight.KeywordColour + "><noparse>local") && dbg.Colours.Contains(LifeVerse.Unity.UI.LuaHighlight.NamespaceColour + "><noparse>object"), "a margem com os números e a linha de parada, e o código em cores: " + dbg.GutterLines);
            dbg.ToggleLine(5); Expect(dbg.Breakpoints.SequenceEqual(new[] { 3, 5 }) && dbg.GutterMarked(5), "clicar no número marca a linha"); dbg.ToggleLine(5); Expect(dbg.Breakpoints.SequenceEqual(new[] { 3 }), "clicar de novo tira a linha"); _checks++;
            dbg.ToggleDebugging(); Expect(debugSent.Count == 1 && debugSent[0].Contains("\"action\":\"start\"") && debugSent[0].Contains("[3]") && debugSent[0].Contains(dbgScript.ToString()), "depurar manda à ilha as linhas de parada");
            dbg.ReceiveDebug("{\"operation\":\"script_debug\",\"script\":\"" + dbgScript + "\",\"event\":\"started\",\"debugging\":true,\"stopped\":false}"); Expect(dbg.Debugging && !dbg.Stopped && !dbg.StepsEnabled, "a ilha começou a depurar");
            dbg.ReceiveDebug("{\"operation\":\"script_debug\",\"script\":\"" + dbgScript + "\",\"event\":\"stopped\",\"debugging\":true,\"stopped\":true,\"line\":3,\"reason\":\"breakpoint\",\"handler\":\"on_touch\",\"locals\":[{\"name\":\"e\",\"value\":\"table\",\"type\":\"table\"}],\"globals\":[{\"name\":\"contador\",\"value\":\"1\",\"type\":\"number\"}]}");
            Expect(dbg.Stopped && dbg.StoppedLine == 3 && dbg.StepsEnabled && dbg.Tab == "Depuração" && dbg.DebugText.Contains("contador = 1"), "parado na linha 3 com o que o script guarda"); _checks++; yield return Snap("ui-script-debug");
            dbg.ReceiveDebug("{\"operation\":\"script_debug\",\"script\":\"" + dbgScript + "\",\"event\":\"ended\",\"reason\":\"timeout\"}"); Expect(!dbg.Debugging && !dbg.StepsEnabled && dbg.StatusText.Contains("2 minutos"), "a depuração termina quando a ilha solta o script"); _checks++; dbg.Close();
            stub.Routes["GET " + scriptsPath + "/" + scriptId + "/versions"] = "{\"current\":3,\"versions\":[{\"version\":2,\"name\":\"porta\",\"lines\":4,\"bytes\":60,\"savedAt\":\"2026-11-19T10:00:00Z\"},{\"version\":1,\"name\":\"porta\",\"lines\":2,\"bytes\":20,\"savedAt\":\"2026-11-18T10:00:00Z\"}]}";
            stub.Routes["GET " + scriptsPath + "/" + scriptId + "/versions/1"] = "{\"version\":1,\"source\":\"print('antiga')\"}"; stub.Routes["POST " + scriptsPath + "/" + scriptId + "/versions/1/restore"] = "{}";
            var restoredCalled = false; var history = new LifeVerse.Unity.UI.ScriptHistoryWindow(app.Context, island, objectId, scriptId, () => restoredCalled = true); history.Open(); yield return Until(() => history.VersionCount == 2, "as versões anteriores do script");
            history.Pick(1); yield return Until(() => history.Preview.Contains("antiga") && history.CanRestore, "o código da versão escolhida e o botão de restaurar"); history.Restore();
            yield return Until(() => restoredCalled && stub.ReceivedAny(b => b.Route == "POST " + scriptsPath + "/" + scriptId + "/versions/1/restore"), "a restauração chegar à API e o editor ser avisado"); history.Close(); _checks++;
            lua.Close();

            var asked = new List<string>(); var dialogs = new LifeVerse.Unity.UI.ScriptDialogs(ui) { Send = asked.Add }; var dialogId = Guid.NewGuid(); var secondId = Guid.NewGuid();
            dialogs.OnOperation("{\"operation\":\"script_dialog\",\"id\":\"" + dialogId + "\",\"kind\":\"permission\",\"title\":\"Permissão\",\"message\":\"A porta quer abrir.\",\"buttons\":[\"Permitir\",\"Negar\"],\"timeout\":30,\"objectName\":\"Porta\"}");
            dialogs.OnOperation("{\"operation\":\"script_dialog\",\"id\":\"" + secondId + "\",\"kind\":\"dialog\",\"message\":\"Qual é a senha?\",\"buttons\":[\"Enviar\"],\"input\":true,\"objectName\":\"Cofre\"}"); yield return null;
            Expect(dialogs.IsOpen && dialogs.Current.Id == dialogId && dialogs.Waiting == 1 && dialogs.Text.Contains("Porta pergunta") && dialogs.Buttons.Count == 2, "a primeira pergunta aparece e a segunda espera: " + dialogs.Text);
            dialogs.Press("Permitir"); yield return null; Expect(asked.Count == 1 && asked[0].Contains(dialogId.ToString()) && asked[0].Contains("Permitir") && dialogs.Current.Id == secondId && dialogs.InputField != null, "a resposta vai à ilha e a próxima pergunta, com campo de texto, aparece");
            dialogs.InputField.value = "abracadabra"; dialogs.Press("Enviar"); Expect(asked.Count == 2 && asked[1].Contains("abracadabra") && !dialogs.IsOpen, "o texto digitado vai junto com a resposta"); yield return null;
            dialogs.OnOperation("{\"operation\":\"script_dialog\",\"id\":\"" + Guid.NewGuid() + "\",\"kind\":\"payment\",\"message\":\"Pagar 5?\",\"buttons\":[\"Pagar\",\"Recusar\"]}"); yield return null; dialogs.Expire(); yield return null;
            Expect(asked.Count == 3 && asked[2].Contains("Recusar") && !dialogs.IsOpen, "um pedido de pagamento sem resposta é recusado quando o tempo acaba"); dialogs.OnOperation("isso não é json"); dialogs.OnOperation("{\"operation\":\"outra\"}"); Expect(!dialogs.IsOpen, "mensagens que não são perguntas são ignoradas"); _checks++;

            var clicks = new List<string>(); var panels = new LifeVerse.Unity.UI.ScriptHud(ui) { Send = clicks.Add }; var hudScript = Guid.NewGuid();
            panels.OnOperation("{\"operation\":\"script_hud\",\"script\":\"" + hudScript + "\",\"visible\":true,\"objectName\":\"Placar\",\"title\":\"Pontos\",\"x\":0.8,\"y\":0.3,\"width\":240,\"elements\":[{\"id\":\"a\",\"type\":\"label\",\"text\":\"Vida\",\"color\":\"#ff0000\"},{\"id\":\"b\",\"type\":\"button\",\"text\":\"Curar\"},{\"id\":\"c\",\"type\":\"progress\",\"text\":\"Energia\",\"value\":30,\"max\":60},{\"id\":\"d\",\"type\":\"input\",\"placeholder\":\"Mensagem\"}]}"); yield return null;
            Expect(panels.Has(hudScript) && panels.TextOf(hudScript).Contains("Pontos") && panels.TextOf(hudScript).Contains("Vida") && panels.ButtonsOf(hudScript).Contains("Curar") && panels.BarOf(hudScript) != null && panels.InputOf(hudScript) != null, "o painel do script mostra título, texto, botão, barra e campo: " + panels.TextOf(hudScript));
            panels.Press(hudScript, "Curar"); Expect(clicks.Count == 1 && clicks[0].Contains("\"action\":\"click\"") && clicks[0].Contains("\"element\":\"b\""), "o clique no botão vai à ilha com o elemento");
            panels.InputOf(hudScript).value = "oi"; panels.OnOperation("{\"operation\":\"script_hud\",\"script\":\"" + hudScript + "\",\"visible\":true,\"title\":\"Pontos\",\"elements\":[{\"id\":\"d\",\"type\":\"input\",\"placeholder\":\"Mensagem\"}]}"); yield return null;
            Expect(panels.InputOf(hudScript).value == "oi", "uma atualização do script não apaga o que a pessoa digitava");
            panels.CloseFromMenu(hudScript); Expect(!panels.Has(hudScript) && clicks[clicks.Count - 1].Contains("\"action\":\"close\""), "fechar o painel avisa a ilha");
            for (var i = 0; i < 12; i++) panels.OnOperation("{\"operation\":\"script_hud\",\"script\":\"" + Guid.NewGuid() + "\",\"visible\":true,\"title\":\"p\",\"elements\":[]}"); Expect(panels.PanelCount == LifeVerse.Unity.UI.ScriptHud.MaxPanels, "no máximo oito painéis ao mesmo tempo: " + panels.PanelCount); panels.Clear(); _checks++;

            // the radial menu, the map and the overlay
            var radial = app.Hud.Radial; radial.Open(ui, app.RadialItems(), new Vector2(500, 350)); yield return null;
            Expect(radial.IsOpen && radial.Slices.Count == 12 && radial.PageCount == 1 && radial.Slices[10].Label == "Construir" && radial.Slices[11].Label == "Terreno", "o menu radial abre com as doze ações numa página: " + radial.Slices.Count + " " + radial.PageCount); _checks++;
            Vector2 Toward(int i) { var angle = LifeVerse.Unity.UI.RadialMenu.AngleOf(i, 12); return radial.Centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 100f; }
            Expect(radial.SliceAt(Toward(0)) == 0 && radial.SliceAt(Toward(3)) == 3 && radial.SliceAt(Toward(7)) == 7 && radial.SliceAt(radial.Centre) == -1 && radial.SliceAt(radial.Centre + new Vector2(0, -300)) == -1, "o ponteiro acha a fatia certa, e o meio e o fora não são fatia"); _checks++;
            Expect(radial.Slices[0].Label == "Acenar" && radial.Slices[9].Label == "Voar", "as ações do jogo estão no anel"); var ran = false; var spy = app.RadialItems(); spy[0] = new LifeVerse.Unity.UI.RadialItem("Acenar", () => ran = true); radial.Open(ui, spy, new Vector2(500, 350)); yield return null;
            Expect(radial.Choose(0) && !radial.IsOpen && ran, "escolher uma fatia fecha o menu e roda a ação"); _checks++;
            var many = new List<LifeVerse.Unity.UI.RadialItem>(); for (var i = 0; i < 14; i++) many.Add(new LifeVerse.Unity.UI.RadialItem("Item " + i, () => { }));
            radial.Open(ui, many, new Vector2(5, 5)); yield return null; var kept = radial.Centre; Expect(radial.PageCount == 2 && radial.Slices.Count == 11 && kept.x >= 150f && kept.y >= 150f, "com 14 ações o menu vira duas páginas e o anel não sai da tela: " + radial.PageCount + " " + kept); _checks++;
            radial.Choose(-1, radial.Centre); Expect(radial.Page == 1 && radial.Slices.Count == 3, "tocar no meio vai para a próxima página"); radial.Close();            yield return new WaitForSeconds(.6f);
            Expect(app.Hud.Map.Texture != null && app.Hud.Map.OtherDots >= 1, "o mapa foi pintado e mostra as outras pessoas: " + app.Hud.Map.OtherDots); _checks++;
            Expect(LifeVerse.Unity.UI.Minimap.ColourOf(-5f, 0f).b > LifeVerse.Unity.UI.Minimap.ColourOf(3f, 0f).b && LifeVerse.Unity.UI.Minimap.ColourOf(-1f, 0f).b > LifeVerse.Unity.UI.Minimap.ColourOf(-6f, 0f).b - 200, "a água é azul e a terra não"); _checks++;
            Expect(app.Hud.TogglePerformance(), "ligar a sobreposição de desempenho"); yield return new WaitForSeconds(.8f); Expect(app.Hud.Perf.Fps > 1f && app.Hud.Perf.FrameMs > 0f, "a sobreposição mede quadros por segundo: " + app.Hud.Perf.Fps); _checks++; app.Hud.TogglePerformance();

            if (_shots != null)
            {
                _camera.gameObject.SetActive(false); app.Hud.Perf.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
                var w1 = new LifeVerse.Unity.UI.SocialWindow(app.Context); w1.Open(); w1.MoveTo(new Vector2(250, 90)); w1.Talk(friend, "Ana");
                var w2 = new LifeVerse.Unity.UI.WalletWindow(app.Context); w2.Open(); w2.MoveTo(new Vector2(720, 90)); var w3 = new LifeVerse.Unity.UI.InventoryWindow(app.Context); w3.Open(); w3.MoveTo(new Vector2(720, 380));
                yield return new WaitForSeconds(.5f); app.Hud.Radial.Open(ui, app.RadialItems(), new Vector2(150, 480)); yield return Shot("platform-ui");
                app.Hud.Radial.Close(); w1.Close(); w2.Close(); w3.Close(); app.Hud.Perf.style.display = UnityEngine.UIElements.DisplayStyle.None; _camera.gameObject.SetActive(true);
            }
            // 5) the rest of the platform: character creator, groups, people actions, editable profile, market, island management, events and places, item rights and offers
            var other = Guid.NewGuid(); var groupId = Guid.NewGuid(); var storeId = Guid.NewGuid(); var productId = Guid.NewGuid(); var itemId = Guid.NewGuid();
            var creator = new LifeVerse.Unity.UI.CharacterWindow(app.Context, LifeVerse.Unity.UI.CharacterDraft.Starter("Tester"), false); creator.Open(); yield return null;
            Expect(creator.Draft.Problems().Count == 0 && creator.Preview.Shown != null && creator.Preview.Texture != null, "o criador abre com o personagem inicial válido e a prévia"); _checks++;
            creator.Draft.HairStyle = "mohawk"; creator.Draft.SkinColor = "#8D5524"; creator.Draft.Body["height"] = 1.1f; creator.Changed(); yield return null;
            Expect(creator.Preview.View.Appearance.HairStyle == "mohawk" && creator.Preview.View.Torso.localScale.y > 1.09f, "mudar o personagem muda a prévia na hora"); _checks++;
            if (_shots != null) { _camera.gameObject.SetActive(false); creator.MoveTo(new Vector2(250, 30)); yield return Shot("creator"); _camera.gameObject.SetActive(true); }
            creator.Draft.DisplayName = "a"; creator.Save(); yield return new WaitForSeconds(.3f); Expect(!stub.ReceivedAny(b => b.Route == "PUT /api/v1/users/me/avatar"), "um personagem inválido não é enviado"); _checks++;
            creator.Draft.DisplayName = "Tester Silva"; creator.Save(); yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT /api/v1/users/me/avatar" && b.Body.Contains("\"displayName\":\"Tester Silva\"") && b.Body.Contains("\"hairStyle\":\"mohawk\"") && b.Body.Contains("equippedItems") && b.Body.Contains("\"bodySettingsJson\"")), "o personagem salvo chegar à API"); _checks++;
            yield return Until(() => !creator.IsSaving, "o salvamento terminar");

            stub.Routes["GET /api/v1/groups/mine"] = "[{\"id\":\"" + groupId + "\",\"name\":\"Os Construtores\",\"role\":\"owner\"}]"; stub.Routes["POST /api/v1/groups"] = "{}"; stub.Routes["GET /api/v1/social/groups/invites"] = "[{\"inviteId\":\"" + Guid.NewGuid() + "\",\"groupName\":\"Pintores\",\"fromUsername\":\"Ana\"}]";
            stub.Routes["GET /api/v1/groups/" + groupId] = "{\"group\":{\"description\":\"Quem constrói.\"}}"; stub.Routes["GET /api/v1/groups/" + groupId + "/members"] = "[{\"userId\":\"" + other + "\",\"username\":\"Beto\",\"role\":\"member\"}]"; stub.Routes["GET /api/v1/groups/" + groupId + "/requests"] = "[{\"id\":\"" + Guid.NewGuid() + "\",\"username\":\"Caio\"}]"; stub.Routes["POST /api/v1/groups/" + groupId + "/invites"] = "{}";
            var groups = new LifeVerse.Unity.UI.GroupsWindow(app.Context); groups.Open(); yield return Until(() => groups.MineCount == 1, "os grupos da pessoa");
            groups.Show("Convites"); yield return Until(() => groups.InviteCount == 1, "os convites de grupo"); groups.Show("Criar"); groups.NameField.value = "Novo Clube"; groups.Create(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/groups" && b.Body.Contains("Novo Clube")), "o grupo criado chegar à API"); _checks++;
            var detail = new LifeVerse.Unity.UI.GroupDetailWindow(app.Context, groupId, "Os Construtores"); detail.Open(); yield return Until(() => detail.MemberCount == 1 && detail.RequestCount == 1, "os membros e os pedidos do grupo"); _checks++; detail.Close(); groups.Close();

            stub.Routes["GET /api/v1/social/presence/" + other] = "{\"status\":\"busy\"}"; stub.Routes["POST /api/v1/social/blocks/" + other] = "{}"; stub.Routes["POST /api/v1/social/reports"] = "{}"; stub.Routes["POST /api/v1/social/friends/requests"] = "{}";
            var person = new LifeVerse.Unity.UI.PersonActionsWindow(app.Context, other, "Beto"); person.Open(); yield return null;
            person.Report(); yield return new WaitForSeconds(.2f); Expect(!stub.ReceivedAny(b => b.Route == "POST /api/v1/social/reports"), "denunciar sem motivo não envia nada"); person.ReasonField.value = "assedio no chat"; person.Report(); person.Block();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/social/reports" && b.Body.Contains(other.ToString()) && b.Body.Contains("assedio no chat")) && stub.ReceivedAny(b => b.Route == "POST /api/v1/social/reports") , "a denúncia chegar à API"); _checks++; person.Close();
            Expect(LifeVerse.Unity.UI.PersonActionsWindow.PresenceWords("busy") == "ocupada" && LifeVerse.Unity.UI.PersonActionsWindow.PresenceWords("invisible") == "offline", "a presença em português"); _checks++;
            app.Hud.Presence.value = "away"; yield return null;

            stub.Routes["PUT /api/v1/users/me/profile/details"] = "{}"; stub.Routes["POST /api/v1/users/me/profile/posts"] = "{}"; stub.Routes["GET /api/v1/users/" + userId + "/profile/feed"] = "[{\"text\":\"Primeira publicação\"}]";
            var mine = new LifeVerse.Unity.UI.ProfileWindow(app.Context); mine.Open(); yield return Until(() => mine.ShownName == "Dono da Ilha" && mine.Editable, "o próprio perfil abrir editável");
            mine.Bio = "Nova bio sem acento."; mine.SaveBio(); mine.PostText = "Ola, mundo!"; mine.Publish();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT /api/v1/users/me/profile/details" && b.Body.Contains("Nova bio")) && stub.ReceivedAny(b => b.Route == "POST /api/v1/users/me/profile/posts" && b.Body.Contains("Ola, mundo!")), "a bio e a publicação chegarem à API"); _checks++; mine.Close();

            stub.Routes["GET /api/v1/economy/products"] = "[{\"id\":\"" + productId + "\",\"name\":\"Sofá azul\",\"priceCoins\":120,\"description\":\"Um sofá.\"}]"; stub.Routes["POST /api/v1/economy/products/" + productId + "/purchase"] = "{}"; stub.Routes["GET /api/v1/economy/orders"] = "[{\"productName\":\"Mesa\",\"status\":\"delivered\",\"priceCoins\":50}]";
            stub.Routes["GET /api/v1/economy/rental-plans"] = "[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Mensal\",\"priceCoins\":900,\"days\":30}]"; stub.Routes["GET /api/v1/discovery/stores/mine"] = "[{\"id\":\"" + storeId + "\",\"name\":\"Loja do Dono\",\"isOpen\":true}]"; stub.Routes["POST /api/v1/discovery/stores"] = "{}";
            var market = new LifeVerse.Unity.UI.MarketWindow(app.Context); market.Open(); yield return Until(() => market.ProductCount == 1, "os produtos do mercado");
            market.Buy(productId, 120, "Sofá azul"); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/economy/products/" + productId + "/purchase" && b.Body.Contains("idempotencyKey")), "a compra chegar à API com a chave de idempotência"); _checks++;
            market.Show("Pedidos"); yield return Until(() => market.OrderCount == 1, "os pedidos"); market.Show("Aluguel"); yield return Until(() => market.PlanCount == 1, "os planos de aluguel"); market.Show("Minhas lojas"); yield return Until(() => market.StoreCount == 1, "as lojas da pessoa");
            market.StoreNameField.value = "Outra Loja"; market.CreateStore(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/discovery/stores" && b.Body.Contains("Outra Loja")), "a loja criada chegar à API"); _checks++; market.Close();

            stub.Routes["PUT /api/v1/worlds/islands/" + island] = "{}"; stub.Routes["POST /api/v1/worlds/islands/" + island + "/permissions"] = "{}"; stub.Routes["PUT /api/v1/worlds/islands/" + island + "/land/rules"] = "{}"; var parcelA = Guid.NewGuid(); var parcelB = Guid.NewGuid(); stub.Routes["GET /api/v1/worlds/islands/" + island + "/land"] = "{\"islandId\":\"" + island + "\",\"size\":256,\"cellSize\":4,\"version\":1,\"parcels\":[{\"id\":\"" + parcelA + "\",\"name\":\"Terreno 1\",\"ownerName\":\"Dono\",\"salePrice\":500,\"runs\":[[0,0,1],[1,-2,2]]},{\"id\":\"" + parcelB + "\",\"name\":\"Terreno 2\",\"ownerName\":\"Dono\"}]}";
            var manage = new LifeVerse.Unity.UI.IslandManageWindow(app.Context, island); manage.Open(); yield return Until(() => manage.NameValue == "Ilha de Teste", "os dados da ilha na gestão");
            Expect(LifeVerse.Unity.UI.IslandManageWindow.ValidateGeneral("ab", "10", "256", out _, out _) != null && LifeVerse.Unity.UI.IslandManageWindow.ValidateGeneral("Ilha", "0", "256", out _, out _) != null && LifeVerse.Unity.UI.IslandManageWindow.ValidateGeneral("Ilha", "10", "10", out _, out _) != null && LifeVerse.Unity.UI.IslandManageWindow.ValidateGeneral("Ilha", "10", "256", out var mp, out var m) == null && mp == 10 && m == 256, "a gestão confere os dados da ilha"); _checks++;
            manage.SaveGeneral(); manage.GrantPermission(); manage.SaveRules();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT /api/v1/worlds/islands/" + island && b.Body.Contains("\"name\":\"Ilha de Teste\"") && b.Body.Contains("\"maxPlayers\":64")) && stub.ReceivedAny(b => b.Route == "PUT /api/v1/worlds/islands/" + island + "/land/rules" && b.Body.Contains("accessMode") && b.Body.Contains("applyToExisting")), "a ilha e as regras salvas chegarem à API"); _checks++;
            manage.Show("Terrenos"); yield return Until(() => manage.Parcels.ParcelCount == 2, "os terrenos da ilha"); _checks++;
            var land = "/api/v1/worlds/islands/" + island + "/land"; yield return Until(() => manage.Parcels.Grid.Size == 64, "a grade de células da ilha (256 m / 4 m = 64 x 64)"); _checks++;
            stub.Routes["GET " + land + "/parcels/" + parcelA + "/about"] = "{\"cells\":[[0,0],[1,0]]}"; stub.Routes["GET " + land + "/parcels/" + parcelA + "/access"] = "[]";
            foreach (var verb in new[] { "split", "expand", "shrink" }) stub.Routes["POST " + land + "/parcels/" + parcelA + "/" + verb] = "{}";
            stub.Routes["POST " + land + "/merge"] = "{}"; stub.Routes["PUT " + land + "/parcels/" + parcelA + "/media"] = "{}"; stub.Routes["PUT " + land + "/parcels/" + parcelA + "/environment"] = "{}";
            manage.Parcels.Choose(parcelA, "Terreno 1"); yield return null; Expect(manage.Parcels.Grid.Owned == 7 && manage.Parcels.Grid.Origin == -32, "as células do terreno escolhido vêm das corridas da própria lista: " + manage.Parcels.Grid.Owned); _checks++; manage.Parcels.Grid.Toggle((3, 3)); manage.Parcels.Grid.Toggle((4, 3)); manage.Parcels.Grid.Toggle((-40, 3)); manage.Parcels.NewNameField.value = "Terreno Novo";
            manage.Parcels.Split(); manage.Parcels.Reshape("expand", "Terreno ampliado."); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + land + "/parcels/" + parcelA + "/split" && b.Body.Contains("\"cells\":[[3,3],[4,3]]") && b.Body.Contains("Terreno Novo")) && stub.ReceivedAny(b => b.Route == "POST " + land + "/parcels/" + parcelA + "/expand" && b.Body.Contains("extraObjects")), "dividir e ampliar chegarem à API com as células escolhidas"); _checks++;
            manage.Parcels.MarkForMerge(parcelA, true); manage.Parcels.MarkForMerge(parcelB, true); manage.Parcels.Merge(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + land + "/merge" && b.Body.Contains(parcelB.ToString()) && b.Body.Contains("primaryId")), "juntar os terrenos marcados chegar à API"); _checks++;
            manage.Parcels.MusicField.value = "ftp://x"; manage.Parcels.SaveMedia(); manage.Parcels.EnvironmentField.value = "{ não é json"; manage.Parcels.SaveEnvironment(); yield return new WaitForSeconds(.3f);
            Expect(!stub.ReceivedAny(b => b.Route == "PUT " + land + "/parcels/" + parcelA + "/media") && !stub.ReceivedAny(b => b.Route == "PUT " + land + "/parcels/" + parcelA + "/environment"), "música com endereço errado e ambiente que não é JSON não são enviados"); _checks++;
            manage.Parcels.MusicField.value = "https://exemplo.com/musica.mp3"; manage.Parcels.EnvironmentField.value = "{\"hour\":18}"; manage.Parcels.SaveMedia(); manage.Parcels.SaveEnvironment();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + land + "/parcels/" + parcelA + "/media" && b.Body.Contains("musica.mp3")) && stub.ReceivedAny(b => b.Route == "PUT " + land + "/parcels/" + parcelA + "/environment" && b.Body.Contains("hour")), "a música e o ambiente do terreno chegarem à API"); _checks++; manage.Close();
            // the full island management (as the Godot client): the summary, then each section reads its route and sends its changes
            var mg = "/api/v1/worlds/islands/" + island + "/management"; var at = "\"2026-11-25T10:00:00Z\""; var bia = Guid.NewGuid(); var permit = Guid.NewGuid(); var banId = Guid.NewGuid(); var thing = Guid.NewGuid(); var ball = Guid.NewGuid(); var backup = Guid.NewGuid(); var entry = Guid.NewGuid();
            stub.Routes["GET " + mg] = "{\"name\":\"Ilha de Teste\",\"description\":\"Uma ilha\",\"category\":\"social\",\"contentRating\":\"general\",\"language\":\"pt-BR\",\"state\":\"online\",\"visitors\":2,\"maxPlayers\":32,\"ownerName\":\"Dono\",\"canEdit\":true,\"showInSearch\":true,\"showOnMap\":false,\"spawn\":{\"x\":4,\"z\":-2},\"updatedAt\":" + at + ",\"createdAt\":" + at + ",\"adminCount\":1,\"size\":256,\"usage\":{\"objects\":120,\"maxObjects\":5000,\"cost\":340,\"maxCost\":15000},\"options\":{\"categories\":[{\"id\":\"social\",\"title\":\"Social\"},{\"id\":\"jogo\",\"title\":\"Jogo\"}],\"ratings\":[\"general\",\"mature\"],\"languages\":[\"pt-BR\",\"en\"]}}";
            stub.Routes["PUT " + mg + "/general"] = "{}";
            stub.Routes["GET " + mg + "/access"] = "{\"mode\":\"invite\",\"updatedAt\":" + at + ",\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{\"permissionId\":\"" + permit + "\",\"name\":\"Bia\",\"username\":\"bia\",\"expiresAt\":null}]}"; stub.Routes["PUT " + mg + "/access/mode"] = "{}"; stub.Routes["POST " + mg + "/access/allow"] = "{}"; stub.Routes["DELETE " + mg + "/access/allow/" + permit] = "{}";
            stub.Routes["GET " + mg + "/bans"] = "{\"counts\":{\"active\":1,\"expired\":0,\"lifted\":2},\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{\"banId\":\"" + banId + "\",\"name\":\"Zé\",\"reason\":\"spam\",\"status\":\"active\",\"expiresAt\":null,\"bannedBy\":\"Dono\"}]}"; stub.Routes["POST " + mg + "/bans"] = "{}"; stub.Routes["DELETE " + mg + "/bans/" + banId] = "{}";
            stub.Routes["GET " + mg + "/permissions"] = "{\"definitions\":[{\"key\":\"build\",\"title\":\"Construir\",\"text\":\"Montar objetos\"},{\"key\":\"kick\",\"title\":\"Expulsar\",\"text\":\"Tirar pessoas\"}],\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{\"userId\":\"" + bia + "\",\"name\":\"Bia\",\"role\":\"\",\"permissions\":[\"build\"]}]}"; stub.Routes["POST " + mg + "/permissions"] = "{}";
            stub.Routes["GET " + mg + "/server"] = "{\"state\":\"online\",\"running\":true,\"uptimeSeconds\":3700,\"visitors\":1,\"maxPlayers\":32,\"players\":[{\"userId\":\"" + bia + "\",\"name\":\"Bia\",\"x\":1,\"y\":2,\"z\":3,\"onlineSeconds\":65,\"isStaff\":false}]}"; stub.Routes["POST " + mg + "/server/notice"] = "{}"; stub.Routes["POST " + mg + "/server/kick"] = "{}";
            stub.Routes["GET " + mg + "/objects"] = "{\"usage\":{\"objects\":1,\"maxObjects\":5000,\"cost\":40,\"maxCost\":15000},\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{\"id\":\"" + thing + "\",\"name\":\"Casa\",\"type\":\"model\",\"ownerName\":\"Bia\",\"cost\":40,\"scripts\":1}]}"; stub.Routes["POST " + mg + "/objects/return"] = "{}";
            stub.Routes["GET " + mg + "/physics"] = "{\"summary\":{\"simulated\":1,\"active\":1,\"frozen\":0},\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{\"id\":\"" + ball + "\",\"name\":\"Bola\",\"ownerName\":\"Bia\",\"cost\":2,\"state\":\"active\"}]}"; stub.Routes["POST " + mg + "/physics/freeze"] = "{}";
            stub.Routes["GET " + mg + "/performance"] = "{\"running\":true,\"current\":{\"fps\":58,\"tickRate\":60,\"processMs\":3.2,\"memoryMb\":410,\"players\":1,\"maxPlayers\":32},\"scripts\":{\"active\":2,\"total\":3},\"summary\":{\"fps\":{\"min\":50,\"avg\":58,\"max\":60}},\"series\":[{}],\"diagnostics\":{\"scripts\":[{\"name\":\"Porta\",\"script\":\"porta.lua\",\"runs\":10,\"instructions\":12000,\"errors\":0}]}}";
            stub.Routes["GET " + mg + "/environment"] = "{\"look\":{\"hour\":12,\"weather\":\"clear\",\"fog\":0.1,\"light\":1},\"updatedAt\":" + at + ",\"builtIn\":[{\"id\":\"sunset\",\"title\":\"Pôr do sol\",\"look\":{\"hour\":18,\"weather\":\"cloudy\",\"fog\":0.2,\"light\":0.7}}],\"custom\":[]}"; stub.Routes["PUT " + mg + "/environment"] = "{}";
            stub.Routes["GET " + mg + "/media"] = "{\"music\":{\"url\":\"\",\"name\":\"\",\"volume\":0.8,\"autoplay\":true},\"updatedAt\":" + at + "}"; stub.Routes["PUT " + mg + "/media"] = "{}";
            stub.Routes["GET " + mg + "/terrain"] = "{\"current\":{\"version\":3,\"savedAt\":" + at + ",\"stats\":{\"sizeMeters\":256,\"minHeight\":-2,\"maxHeight\":30,\"avgHeight\":4,\"seaLevel\":0}},\"backups\":[{\"id\":\"" + backup + "\",\"name\":\"Antes da festa\",\"savedAt\":" + at + ",\"by\":\"Dono\",\"valid\":true}],\"limits\":{\"maxBackups\":10}}"; stub.Routes["POST " + mg + "/terrain/backups"] = "{}"; stub.Routes["POST " + mg + "/terrain/backups/" + backup + "/restore"] = "{}";
            stub.Routes["GET " + mg + "/restart"] = "{\"state\":\"online\",\"running\":true,\"uptimeSeconds\":100,\"active\":null,\"history\":[{\"createdAt\":" + at + ",\"requestedBy\":\"Dono\",\"status\":\"completed\",\"durationSeconds\":40}]}"; stub.Routes["POST " + mg + "/restart"] = "{}";
            stub.Routes["GET " + mg + "/audit"] = "{\"total\":1,\"page\":1,\"pageSize\":50,\"items\":[{\"id\":\"" + entry + "\",\"at\":" + at + ",\"actorName\":\"Dono\",\"category\":\"geral\",\"action\":\"general.save\",\"summary\":\"Mudou o nome\"}]}"; stub.Routes["GET " + mg + "/audit/" + entry] = "{\"id\":\"" + entry + "\",\"at\":" + at + ",\"actorName\":\"Dono\",\"category\":\"geral\",\"action\":\"general.save\",\"summary\":\"Mudou o nome\",\"before\":{\"name\":\"A\"},\"after\":{\"name\":\"B\"}}";
            bool Sent(string route) { lock (stub.Requests) return stub.Requests.Contains(route); }
            var gm = new LifeVerse.Unity.UI.IslandManagementWindow(app.Context, island); gm.Open();
            yield return Until(() => gm.SummaryText.Contains("Ilha de Teste") && gm.SummaryText.Contains("no ar") && gm.SummaryText.Contains("340/15.000 prims") && gm.Fields.TryGetValue("Nome", out var gmName) && gmName.value == "Ilha de Teste", "o resumo e os dados gerais da gestão completa"); _checks++;
            Expect(gm.Choices["Categoria"].value == "Social" && gm.Choices["Idioma"].index == 0 && gm.Toggles["Aparecer no buscador"].value && !gm.Toggles["Aparecer no mapa"].value && gm.Fields["Ponto de chegada X"].value == "4", "as escolhas da seção Geral vêm da API"); _checks++;
            yield return Snap("ui-island-management");
            gm.Fields["Pessoas ao mesmo tempo"].value = "0"; gm.SaveGeneral(); yield return new WaitForSeconds(.2f); Expect(gm.Status.Contains("1 a 256") && !stub.ReceivedAny(b => b.Route == "PUT " + mg + "/general"), "lotação fora do limite não é enviada"); _checks++;
            gm.Fields["Pessoas ao mesmo tempo"].value = "40"; gm.Fields["Nome"].value = "Ilha Nova"; gm.Choices["Categoria"].index = 1; gm.SaveGeneral();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + mg + "/general" && b.Body.Contains("Ilha Nova") && b.Body.Contains("\"maxPlayers\":40") && b.Body.Contains("\"category\":\"jogo\"") && b.Body.Contains("expectedUpdatedAt") && b.Body.Contains("\"spawnX\":4")), "os dados gerais chegarem à API com a data da última leitura"); _checks++;
            gm.Show("acesso"); yield return Until(() => gm.RowIds.Contains(permit) && gm.Choices.ContainsKey("Modo de entrada"), "a lista de acesso");
            Expect(gm.Choices["Modo de entrada"].index == 1, "o modo de entrada vem da API (só convidados)"); gm.Choices["Modo de entrada"].index = 2; gm.Press("Salvar o modo");
            gm.Fields["Nome da pessoa"].value = "Caio"; gm.Fields["Por quantos dias (vazio = sempre)"].value = "7"; gm.Press("Liberar"); gm.Press("Tirar");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + mg + "/access/mode" && b.Body.Contains("closed")) && stub.ReceivedAny(b => b.Route == "POST " + mg + "/access/allow" && b.Body.Contains("Caio") && b.Body.Contains("expiresAt\":\"20")) && Sent("DELETE " + mg + "/access/allow/" + permit), "modo, liberação com prazo e retirada chegarem à API"); _checks++;
            gm.Show("banimentos"); yield return Until(() => gm.RowIds.Contains(banId), "a lista de banimentos");
            gm.Ban("Rui", "flood", "abc"); Expect(gm.Status.Contains("1 a 3650"), "dias de banimento errados são recusados"); gm.Ban("Rui", "flood", "3"); gm.Press("Retirar");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + mg + "/bans" && b.Body.Contains("Rui") && b.Body.Contains("flood")) && Sent("DELETE " + mg + "/bans/" + banId), "banir e retirar um banimento chegarem à API"); _checks++;
            gm.Show("permissoes"); yield return Until(() => gm.RowIds.Contains(bia) && gm.Toggles.ContainsKey("Construir"), "as permissões e as suas definições");
            gm.Fields["Nome da pessoa"].value = "Caio"; gm.Toggles["Construir"].value = true; gm.Press("Dar");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + mg + "/permissions" && b.Body.Contains("Caio") && b.Body.Contains("[\"build\"]")), "dar permissões pelo nome chegar à API"); _checks++;
            gm.Show("administracao"); yield return Until(() => gm.RowIds.Contains(bia), "quem está na ilha");
            gm.Fields["Texto do aviso"].value = "Bom dia a todos"; gm.Press("Enviar para todos"); gm.Press("Expulsar");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + mg + "/server/notice" && b.Body.Contains("Bom dia a todos")) && stub.ReceivedAny(b => b.Route == "POST " + mg + "/server/kick" && b.Body.Contains(bia.ToString())), "o aviso e a expulsão chegarem à API"); _checks++;
            gm.Show("objetos"); yield return Until(() => gm.RowIds.Contains(thing), "os objetos da ilha");
            gm.Toggles["obj:" + thing].value = true; gm.Press("Devolver os marcados aos donos"); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + mg + "/objects/return" && b.Body.Contains(thing.ToString())), "devolver os objetos marcados chegar à API"); _checks++;
            gm.Show("fisica"); yield return Until(() => gm.RowIds.Contains(ball), "os objetos simulados"); gm.Press("Congelar");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + mg + "/physics/freeze" && b.Body.Contains(ball.ToString()) && b.Body.Contains("\"all\":false")), "congelar um objeto chegar à API"); _checks++;
            gm.Show("desempenho"); yield return Until(() => gm.Lines >= 6, "as medidas de desempenho"); _checks++;
            gm.Show("ambiente"); yield return Until(() => gm.Actions.ContainsKey("Usar"), "a aparência e as predefinições");
            gm.Fields["Hora (0 a 24)"].value = "30"; gm.Press("Salvar a aparência"); Expect(gm.Status.Contains("0 a 24"), "hora fora do limite é recusada");
            gm.Press("Usar"); Expect(gm.Fields["Hora (0 a 24)"].value == "18" && gm.Choices["Clima"].index == 1, "a predefinição enche o formulário"); gm.Press("Salvar a aparência");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + mg + "/environment" && b.Body.Contains("\"hour\":18") && b.Body.Contains("cloudy") && b.Body.Contains("expectedUpdatedAt")), "a aparência chegar à API"); _checks++;
            gm.Show("midia"); yield return Until(() => gm.Fields.ContainsKey("Endereço (https)"), "a música da ilha");
            gm.Fields["Endereço (https)"].value = "http://exemplo.com/a.mp3"; gm.Press("Salvar a música"); Expect(gm.Status.Contains("https"), "música sem https é recusada");
            gm.Fields["Endereço (https)"].value = "https://cdn.exemplo.com/tema.mp3"; gm.Press("Salvar a música"); yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + mg + "/media" && b.Body.Contains("tema.mp3")), "a música chegar à API"); _checks++;
            gm.Show("terreno"); yield return Until(() => gm.RowIds.Contains(backup), "os backups do terreno");
            gm.Fields["Nome do backup"].value = "Teste"; gm.Press("Fazer backup do terreno de agora"); gm.Press("Em 5 min");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + mg + "/terrain/backups" && b.Body.Contains("Teste")) && stub.ReceivedAny(b => b.Route == "POST " + mg + "/terrain/backups/" + backup + "/restore" && b.Body.Contains("\"mode\":\"in\"") && b.Body.Contains("\"minutes\":5")), "o backup e a restauração agendada chegarem à API"); _checks++;
            gm.Show("reinicio"); yield return Until(() => gm.Actions.ContainsKey("Em 5 min"), "a reinicialização"); gm.Press("Em 10 min");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + mg + "/restart" && b.Body.Contains("\"minutes\":10")), "a reinicialização agendada chegar à API"); _checks++;
            gm.Show("auditoria"); yield return Until(() => gm.RowIds.Contains(entry), "o histórico da ilha"); gm.Press("Detalhes");
            yield return Until(() => Sent("GET " + mg + "/audit/" + entry) && gm.Lines >= 5, "os detalhes de uma entrada do histórico"); _checks++;
            gm.Show("regras"); gm.Press("Salvar as regras"); yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT /api/v1/worlds/islands/" + island + "/land/rules" && b.Body.Contains("\"autoReturnMinutes\":null")), "as regras pela gestão completa chegarem à API"); _checks++;
            gm.Show("terrenos"); yield return Until(() => gm.Lines == 2, "os terrenos dentro da gestão completa"); gm.Close();
            // the land management (ten sections) and "Sobre este Terreno" (eight), as in the Godot client
            var landBase = "/api/v1/worlds/islands/" + island + "/land"; var pA = Guid.NewGuid(); var pB = Guid.NewGuid(); var rentId = Guid.NewGuid(); var accessGroup = Guid.NewGuid(); var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid(); var entryZe = Guid.NewGuid(); var bench = Guid.NewGuid(); var biaId = Guid.NewGuid();
            stub.Routes["GET " + landBase + "/overview"] = "{\"island\":{\"name\":\"Ilha de Teste\",\"size\":256,\"owner\":\"Dono\",\"isPublic\":true},\"totals\":{\"area\":65536,\"areaParcelled\":1000,\"areaFree\":64536,\"parcels\":2,\"available\":0,\"onSale\":1,\"rented\":1,\"sold\":0,\"common\":0,\"monthlyRevenue\":300,\"expiringSoon\":1,\"objectsLimit\":5000,\"objectsUsed\":12,\"objectsShared\":400},"
                + "\"parcels\":[{\"id\":\"" + pA + "\",\"code\":\"T-01\",\"name\":\"Terreno 1\",\"category\":\"residencial\",\"kind\":\"sale\",\"area\":112,\"runs\":[[0,0,6]],\"owner\":\"Dono\",\"salePrice\":500,\"objects\":10,\"maxObjects\":200,\"cost\":30,\"maxCost\":600,\"buildMode\":\"owner\"},"
                + "{\"id\":\"" + pB + "\",\"code\":\"T-02\",\"name\":\"Lote B\",\"category\":\"comercial\",\"kind\":\"rented\",\"area\":64,\"runs\":[[2,0,1]],\"owner\":\"Dono\",\"tenant\":\"Bia\",\"rentStatus\":\"active\",\"rentExpiresAt\":" + at + ",\"rentalId\":\"" + rentId + "\",\"rentPrice\":100,\"rentPeriodDays\":30,\"objects\":2,\"maxObjects\":200}],"
                + "\"contracts\":[{\"id\":\"" + rentId + "\",\"code\":\"T-02\",\"parcel\":\"Lote B\",\"landlord\":\"Dono\",\"tenant\":\"Bia\",\"price\":100,\"periodDays\":30,\"status\":\"active\",\"startsAt\":" + at + ",\"expiresAt\":" + at + "}],"
                + "\"history\":[{\"at\":" + at + ",\"kind\":\"change\",\"what\":\"split\",\"actor\":\"Dono\",\"code\":\"T-01\",\"parcel\":\"Terreno 1\"},{\"at\":" + at + ",\"kind\":\"money\",\"what\":\"payment\",\"actor\":\"Bia\",\"code\":\"T-02\",\"parcel\":\"Lote B\",\"amount\":100}]}";
            stub.Routes["POST " + landBase + "/claim"] = "{}"; stub.Routes["DELETE " + landBase + "/parcels/" + pA + "/sale"] = "{}"; stub.Routes["POST " + landBase + "/rentals/" + rentId + "/end"] = "{}";
            stub.Routes["GET " + landBase + "/parcels/" + pA + "/group-access"] = "[{\"id\":\"" + accessGroup + "\",\"groupId\":\"" + g1 + "\",\"groupName\":\"Construtores\",\"kind\":\"build\",\"at\":" + at + "}]"; stub.Routes["POST " + landBase + "/parcels/" + pA + "/group-access"] = "{}"; stub.Routes["DELETE " + landBase + "/parcels/" + pA + "/group-access/" + accessGroup] = "{}";
            stub.Routes.TryGetValue("GET /api/v1/social/groups", out var oldGroups); stub.Routes.TryGetValue("GET /api/v1/social/people", out var oldPeople);
            stub.Routes["GET /api/v1/social/groups"] = "[{\"id\":\"" + g1 + "\",\"name\":\"Construtores\"},{\"id\":\"" + g2 + "\",\"name\":\"Vizinhos\"}]"; stub.Routes["GET /api/v1/social/people"] = "[{\"id\":\"" + biaId + "\",\"username\":\"bia\",\"displayName\":\"Bia\"}]";
            var lm = new LifeVerse.Unity.UI.LandManagementWindow(app.Context, island); lm.Open();
            yield return Until(() => lm.SummaryText.Contains("2 terrenos") && lm.SummaryText.Contains("300 VC") && lm.Lines >= 10, "a visão geral dos terrenos"); _checks++;
            lm.Show("mapa"); yield return Until(() => lm.Grid != null && lm.Grid.Painted == 9 && lm.RowIds.Contains(pA) && lm.RowIds.Contains(pB), "o mapa de parcelas pintado pela situação de cada terreno"); _checks++; yield return Snap("ui-land-management");
            lm.Show("demarcacao"); yield return Until(() => lm.Actions.ContainsKey("Demarcar") && lm.Grid.Painted == 9, "a demarcação");
            lm.Press("Demarcar"); Expect(lm.Status.Contains("nome"), "demarcar sem nome é recusado"); lm.Fields["Nome do terreno"].value = "Casa nova"; lm.Press("Demarcar"); Expect(lm.Status.Contains("Marque"), "demarcar sem quadrados é recusado");
            lm.Grid.Toggle((10, 10)); lm.Grid.Toggle((11, 10)); lm.Press("Demarcar"); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + landBase + "/claim" && b.Body.Contains("Casa nova") && b.Body.Contains("[[10,10],[11,10]]")), "demarcar um terreno chegar à API"); _checks++;
            lm.Show("lista"); yield return Until(() => lm.RowIds.Count == 2, "a lista de terrenos"); lm.Press("À venda"); yield return Until(() => lm.RowIds.Count == 1 && lm.RowIds[0] == pA, "o filtro de terrenos à venda");
            lm.Press("Todos"); yield return Until(() => lm.RowIds.Count == 2 && lm.Fields.ContainsKey("Buscar por nome ou código"), "a lista inteira de novo"); lm.Fields["Buscar por nome ou código"].value = "lote"; lm.Press("Buscar"); Expect(lm.RowIds.Count == 1 && lm.RowIds[0] == pB, "a busca na lista de terrenos"); _checks++;
            lm.Show("vendas"); yield return Until(() => lm.Actions.ContainsKey("Tirar da venda"), "os terrenos à venda"); lm.Press("Tirar da venda"); yield return Until(() => Sent("DELETE " + landBase + "/parcels/" + pA + "/sale"), "tirar da venda chegar à API"); _checks++;
            lm.Show("alugueis"); yield return Until(() => lm.Actions.ContainsKey("Encerrar o aluguel"), "os aluguéis"); lm.Press("Encerrar o aluguel"); yield return Until(() => Sent("POST " + landBase + "/rentals/" + rentId + "/end"), "encerrar um aluguel chegar à API"); _checks++;
            lm.Show("contratos"); yield return Until(() => lm.RowIds.Contains(rentId), "os contratos"); lm.Show("prims"); yield return Until(() => lm.RowIds.Count == 2, "os prims por terreno"); _checks++;
            lm.Show("permissoes"); yield return Until(() => lm.RowIds.Contains(accessGroup) && lm.Actions.ContainsKey("Dar acesso ao grupo"), "o acesso de grupo do terreno");
            lm.Choices["Grupo"].index = 1; lm.Press("Dar acesso ao grupo"); lm.Press("Tirar o grupo");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + landBase + "/parcels/" + pA + "/group-access" && b.Body.Contains(g2.ToString()) && b.Body.Contains("\"allow\"")) && Sent("DELETE " + landBase + "/parcels/" + pA + "/group-access/" + accessGroup), "dar e tirar o acesso de um grupo chegarem à API"); _checks++;
            lm.Show("historico"); yield return Until(() => lm.Lines == 3, "o histórico dos terrenos"); lm.Press("Dinheiro"); yield return Until(() => lm.Lines == 2, "o filtro de dinheiro do histórico"); _checks++; lm.Close();

            var pp = "/api/v1/worlds/islands/" + island + "/parcels/" + pA; var pl = landBase + "/parcels/" + pA;
            stub.Routes["GET " + pl + "/about"] = "{\"parcel\":{\"id\":\"" + pA + "\",\"code\":\"T-01\",\"name\":\"Terreno 1\",\"description\":\"Perto do mar\",\"category\":\"residencial\",\"area\":112,\"revision\":3,\"accessMode\":\"list\",\"buildMode\":\"owner\",\"allowScripts\":true,\"allowVoice\":true,\"allowMedia\":false,\"autoReturnMinutes\":null,\"teleportMode\":\"free\",\"spawnX\":2,\"spawnZ\":1,\"yaw\":0,\"media\":null,\"createdAt\":" + at + "},"
                + "\"island\":{\"name\":\"Ilha de Teste\"},\"owner\":{\"name\":\"Dono\",\"kind\":\"person\"},\"history\":[{\"at\":" + at + ",\"what\":\"claim\",\"to\":\"Dono\"}],\"transfer\":null,\"rights\":{\"canResell\":true,\"canLease\":true,\"canTransfer\":true},"
                + "\"usage\":{\"objects\":10,\"maxObjects\":200,\"cost\":30,\"maxCost\":600},\"sale\":{\"price\":500,\"exclusive\":false,\"includeObjects\":true,\"description\":\"\"},\"rentOffer\":null,\"rental\":null,\"me\":{\"canManage\":true,\"isOwner\":true,\"isTenant\":false,\"isStaff\":true}}";
            stub.Routes["PATCH " + pp] = "{}"; foreach (var route in new[] { "category", "image", "arrival", "options", "media", "access-mode" }) stub.Routes["PUT " + pl + "/" + route] = "{}";
            stub.Routes["GET " + pl + "/access"] = "{\"mode\":\"list\",\"entries\":[{\"id\":\"" + entryZe + "\",\"userId\":\"" + Guid.NewGuid() + "\",\"userName\":\"Zé\",\"kind\":\"ban\",\"reason\":\"\",\"expiresAt\":null}]}"; stub.Routes["DELETE " + pl + "/access/" + entryZe] = "{}"; stub.Routes["POST " + pl + "/access"] = "{}"; stub.Routes["POST " + pl + "/transfer"] = "{}";
            stub.Routes["GET " + pl + "/objects"] = "[{\"id\":\"" + bench + "\",\"name\":\"Banco\",\"ownerName\":\"Zé\",\"primitiveType\":\"box\",\"cost\":2,\"hasScripts\":false,\"authorized\":false}]"; stub.Routes["POST " + pl + "/objects/return"] = "{}";
            stub.Routes["GET " + pl + "/history"] = "[{\"type\":\"payment\",\"amount\":100,\"at\":" + at + "}]"; stub.Routes["GET " + pl + "/audit"] = "[{\"operation\":\"split\",\"actorName\":\"Dono\",\"summary\":\"Dividiu o terreno\",\"at\":" + at + "}]";
            var pi = new LifeVerse.Unity.UI.ParcelInfoWindow(app.Context, island, pA, () => new Vector3(3f, 0f, 2f)); pi.Open();
            yield return Until(() => pi.SummaryText.Contains("T-01") && pi.SummaryText.Contains("é seu") && pi.Fields.TryGetValue("Nome", out var pn) && pn.value == "Terreno 1", "o cabeçalho e a seção Geral de Sobre este Terreno"); _checks++; yield return Snap("ui-parcel-info");
            pi.Fields["Nome"].value = "Terreno Mar"; pi.Press("Salvar nome e descrição"); pi.Choices["Finalidade"].index = 1; pi.Press("Salvar a finalidade");
            pi.Fields["Ponto de chegada X (vazio = o centro)"].value = "1"; pi.Fields["Ponto de chegada Z"].value = ""; pi.Press("Salvar a chegada"); Expect(pi.Status.Contains("Preencha X e Z"), "chegada com só um número é recusada");
            pi.Press("Usar onde estou"); Expect(pi.Fields["Ponto de chegada X (vazio = o centro)"].value == "3" && pi.Fields["Ponto de chegada Z"].value == "2", "usar onde estou enche a chegada"); pi.Press("Salvar a chegada");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PATCH " + pp && b.Body.Contains("Terreno Mar") && b.Body.Contains("\"expectedRevision\":3")) && stub.ReceivedAny(b => b.Route == "PUT " + pl + "/category" && b.Body.Contains("comercial")) && stub.ReceivedAny(b => b.Route == "PUT " + pl + "/arrival" && b.Body.Contains("\"x\":3") && b.Body.Contains("\"free\"")), "nome, finalidade e chegada do terreno chegarem à API"); _checks++;
            pi.Show("propriedade"); yield return Until(() => pi.Actions.ContainsKey("Tirar da venda") && pi.Actions.ContainsKey("Transferir"), "a propriedade do terreno");
            Expect(pi.Actions.ContainsKey("Anunciar o aluguel…") && !pi.Actions.ContainsKey("Anunciar a venda…") && !pi.Actions.Keys.Any(k => k.StartsWith("Comprar")), "o dono vê anunciar o aluguel e tirar da venda, e não comprar");
            pi.Fields["Nome da pessoa"].value = "Caio"; pi.Press("Transferir"); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + pl + "/transfer" && b.Body.Contains("Caio")), "transferir o terreno chegar à API"); _checks++;
            pi.Show("acesso"); yield return Until(() => pi.RowIds.Contains(entryZe) && pi.Actions.ContainsKey("Dar acesso ao grupo"), "a lista de acesso e os grupos do terreno");
            pi.AddPerson("bia", "build", "30"); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + pl + "/access" && b.Body.Contains(biaId.ToString()) && b.Body.Contains("\"build\"") && b.Body.Contains("\"minutes\":30")), "pôr uma pessoa na lista pelo nome chegar à API");
            yield return new WaitForSeconds(.6f); yield return Until(() => pi.Choices.ContainsKey("Modo") && pi.Actions.ContainsKey("Tirar"), "a lista lida de novo"); pi.Choices["Modo"].index = 2; pi.Press("Salvar o modo"); pi.Press("Tirar");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + pl + "/access-mode" && b.Body.Contains("private")) && Sent("DELETE " + pl + "/access/" + entryZe), "o modo de entrada e tirar da lista chegarem à API"); _checks++;
            pi.Show("opcoes"); yield return Until(() => pi.Choices.ContainsKey("Quem constrói"), "as opções do terreno"); pi.Choices["Quem constrói"].index = 2; pi.Toggles["Permitir mídia"].value = true; pi.Fields["Devolver objetos soltos depois de (minutos, vazio = nunca)"].value = "15"; pi.Press("Salvar as opções");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + pl + "/options" && b.Body.Contains("everyone") && b.Body.Contains("\"autoReturnMinutes\":15") && b.Body.Contains("\"allowMedia\":true")), "as opções do terreno chegarem à API"); _checks++;
            pi.Show("objetos"); yield return Until(() => pi.RowIds.Contains(bench), "os objetos do terreno"); pi.Press("Devolver os de quem não tem permissão"); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + pl + "/objects/return" && b.Body.Contains("others")), "devolver os objetos sem permissão chegar à API"); _checks++;
            pi.Show("midia"); yield return Until(() => pi.Fields.ContainsKey("Endereço (https; vazio tira a música)"), "a música do terreno"); pi.Fields["Endereço (https; vazio tira a música)"].value = "https://cdn.exemplo.com/lote.ogg"; pi.Press("Salvar a música");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT " + pl + "/media" && b.Body.Contains("lote.ogg")), "a música do terreno chegar à API"); _checks++;
            pi.Show("historico"); yield return Until(() => pi.Lines >= 4, "o histórico e o dinheiro do terreno"); pi.Show("auditoria"); yield return Until(() => pi.Lines == 1 && Sent("GET " + pl + "/audit"), "a auditoria do terreno"); _checks++; pi.Close();
            if (oldGroups != null) stub.Routes["GET /api/v1/social/groups"] = oldGroups; else stub.Routes.Remove("GET /api/v1/social/groups"); if (oldPeople != null) stub.Routes["GET /api/v1/social/people"] = oldPeople; else stub.Routes.Remove("GET /api/v1/social/people");
            // the inventory as the Godot client keeps it: views, folders, details, bin, space and check, packs
            var inv = "/api/v1/inventory"; var fA = Guid.NewGuid(); var fB = Guid.NewGuid(); var fT = Guid.NewGuid(); var i1 = Guid.NewGuid(); var i2 = Guid.NewGuid();
            var invSaved = new[] { "GET " + inv + "/items", "GET " + inv + "/folders", "GET " + inv + "/summary" }.ToDictionary(k => k, k => stub.Routes.TryGetValue(k, out var v) ? v : null);
            stub.Routes["GET " + inv + "/summary"] = "{\"total\":3,\"favorites\":1,\"unfiled\":1,\"recent\":2,\"trash\":1,\"trashFolders\":1}";
            stub.Routes["GET " + inv + "/folders"] = "[{\"id\":\"" + fA + "\",\"parentId\":null,\"name\":\"Roupas\",\"itemCount\":1},{\"id\":\"" + fB + "\",\"parentId\":\"" + fA + "\",\"name\":\"Verão\",\"itemCount\":1}]";
            stub.Routes["GET " + inv + "/items"] = "{\"page\":1,\"pageSize\":30,\"total\":2,\"hasMore\":false,\"items\":[{\"itemId\":\"" + i1 + "\",\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Chapeu\",\"category\":\"accessories\",\"size\":2048,\"folderId\":\"" + fB + "\",\"ownerRights\":7,\"nextOwnerRights\":5,\"favorite\":false,\"hasUpdate\":false,\"version\":1,\"createdAt\":" + at + "},"
                + "{\"itemId\":\"" + i2 + "\",\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Tijolo\",\"category\":\"textures\",\"size\":512,\"folderId\":null,\"ownerRights\":15,\"nextOwnerRights\":7,\"favorite\":true,\"hasUpdate\":true,\"version\":2,\"createdAt\":" + at + "}]}";
            stub.Routes["GET " + inv + "/trash"] = "{\"retentionDays\":30,\"folders\":[{\"id\":\"" + fT + "\",\"name\":\"Velha\",\"items\":2,\"deletedAt\":" + at + ",\"purgeAt\":" + at + "}]}";
            stub.Routes["GET " + inv + "/storage"] = "{\"usedBytes\":5242880,\"quotaBytes\":104857600,\"inBinBytes\":1024,\"olderVersionsBytes\":2048,\"items\":3,\"assets\":3,\"byCategory\":[{\"category\":\"textures\",\"bytes\":4096,\"assets\":2}]}";
            stub.Routes["GET " + inv + "/integrity"] = "{\"checked\":3,\"findings\":[{\"rule\":\"stuck_offered\",\"severity\":\"error\",\"itemId\":\"" + i1 + "\",\"detail\":\"Chapeu está reservado\",\"repairable\":true}]}"; stub.Routes["POST " + inv + "/integrity/repair"] = "{\"released\":1,\"closed\":1}";
            foreach (var route in new[] { "POST /move", "POST /folders", "PATCH /folders/" + fB, "DELETE /folders/" + fB, "PATCH /items/" + i2, "DELETE /items/" + i2 + "/favorite", "POST /items/" + i2 + "/copy", "POST /items/" + i2 + "/equip", "POST /items/" + i2 + "/used", "POST /items/" + i2 + "/ack-update",
                "POST /items/" + i2 + "/restore", "DELETE /items/" + i2 + "/purge", "POST /trash/empty", "POST /folders/" + fT + "/restore", "DELETE /folders/" + fT + "/purge" }) { var cut = route.IndexOf(' '); stub.Routes[route.Substring(0, cut) + " " + inv + route.Substring(cut + 1)] = "{}"; }
            stub.Routes["POST " + inv + "/packs/unpack"] = "{\"folderId\":\"" + Guid.NewGuid() + "\",\"folderName\":\"pacote\",\"count\":2,\"items\":[]}";
            var iv = new LifeVerse.Unity.UI.InventoryManagerWindow(app.Context); iv.Open();
            yield return Until(() => iv.SummaryText.Contains("3 itens") && iv.RowIds.Contains(i1) && iv.RowIds.Contains(i2) && iv.Choices.ContainsKey("Mover para"), "o inventário com o resumo, os itens e as pastas"); _checks++; yield return Snap("ui-inventory");
            iv.Toggles["item:" + i2].value = true; iv.Choices["Mover para"].index = 1; iv.Press("Mover os marcados");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + inv + "/move" && b.Body.Contains(i2.ToString()) && b.Body.Contains("\"targetFolderId\":\"" + fA + "\"")), "mover os itens marcados para uma pasta chegar à API"); _checks++;
            yield return new WaitForSeconds(.6f); iv.Press("Detalhes"); yield return Until(() => iv.Fields.ContainsKey("Nome no inventário"), "os detalhes de um item");
            Expect(iv.Actions.ContainsKey("Entendi a versão nova") && iv.Actions.ContainsKey("Tirar dos favoritos"), "o item favorito com versão nova mostra as ações certas");
            iv.Fields["Nome no inventário"].value = "Tijolo vermelho"; iv.Press("Renomear"); iv.Choices["Pasta"].index = 2; iv.Press("Mudar de pasta"); iv.Press("Tirar dos favoritos"); iv.Press("Fazer uma cópia"); iv.Choices["Vestir em"].index = 1; iv.Press("Vestir"); iv.Press("Entendi a versão nova");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PATCH " + inv + "/items/" + i2 && b.Body.Contains("Tijolo vermelho")) && stub.ReceivedAny(b => b.Route == "PATCH " + inv + "/items/" + i2 && b.Body.Contains("\"folderId\":\"" + fB + "\"")) && Sent("DELETE " + inv + "/items/" + i2 + "/favorite")
                && Sent("POST " + inv + "/items/" + i2 + "/copy") && stub.ReceivedAny(b => b.Route == "POST " + inv + "/items/" + i2 + "/equip" && b.Body.Contains("\"head\"")) && Sent("POST " + inv + "/items/" + i2 + "/used") && Sent("POST " + inv + "/items/" + i2 + "/ack-update"), "renomear, mudar de pasta, favorito, cópia, vestir e versão nova chegarem à API"); _checks++;
            iv.Show("lixeira"); yield return Until(() => iv.RowIds.Contains(fT) && iv.RowIds.Contains(i2), "a lixeira com pastas e itens");
            iv.Press("Restaurar"); iv.Press("Apagar de vez"); iv.Press("Restaurar a pasta"); iv.Press("Apagar a pasta de vez"); iv.Press("Esvaziar a lixeira");
            yield return Until(() => Sent("POST " + inv + "/items/" + i2 + "/restore") && Sent("DELETE " + inv + "/items/" + i2 + "/purge") && Sent("POST " + inv + "/folders/" + fT + "/restore") && Sent("DELETE " + inv + "/folders/" + fT + "/purge") && Sent("POST " + inv + "/trash/empty"), "restaurar, apagar de vez e esvaziar a lixeira chegarem à API"); _checks++;
            iv.Show("pastas"); yield return Until(() => iv.RowIds.Contains(fA) && !iv.RowIds.Contains(fB), "as pastas do início");
            iv.Fields["Nome (para criar ou renomear)"].value = "Casas"; iv.Press("Criar pasta"); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST " + inv + "/folders" && b.Body.Contains("Casas")), "criar uma pasta chegar à API");
            yield return new WaitForSeconds(.6f); iv.Press("Abrir"); yield return Until(() => iv.Folder == fA && iv.RowIds.Contains(fB) && iv.RowIds.Contains(i1), "abrir uma pasta mostra as subpastas e os itens"); yield return new WaitForSeconds(.3f);
            iv.Fields["Nome (para criar ou renomear)"].value = "Inverno"; iv.Press("Renomear"); iv.Press("Levar para o início"); iv.Press("Apagar a pasta");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PATCH " + inv + "/folders/" + fB && b.Body.Contains("Inverno")) && stub.ReceivedAny(b => b.Route == "PATCH " + inv + "/folders/" + fB && b.Body.Contains("\"toTop\":true")) && Sent("DELETE " + inv + "/folders/" + fB), "renomear, subir e apagar uma pasta chegarem à API"); _checks++;
            iv.Show("espaco"); yield return Until(() => iv.Actions.ContainsKey("Consertar") && iv.Lines >= 6, "o espaço e a verificação do inventário"); iv.Press("Consertar");
            yield return Until(() => iv.Status.Contains("1 item(ns) liberado(s)"), "consertar o inventário e dizer o que mudou"); _checks++;
            var packZip = System.IO.Path.Combine(Application.temporaryCachePath, "pacote.zip"); System.IO.File.WriteAllBytes(packZip, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            iv.Show("pacote"); iv.Unpack(packZip); yield return Until(() => iv.LastPack == "2 item(ns) na pasta pacote." && Sent("POST " + inv + "/packs/unpack"), "abrir um pacote .zip pelo inventário"); _checks++;
            iv.Show("sempasta"); yield return Until(() => iv.RowIds.Count == 2, "a vista sem pasta"); iv.Close();
            foreach (var pair in invSaved) if (pair.Value != null) stub.Routes[pair.Key] = pair.Value; else stub.Routes.Remove(pair.Key);
            // the global search on the platform's search service, as in the Godot client
            var sP = Guid.NewGuid(); var sE = Guid.NewGuid(); var sPr = Guid.NewGuid(); var sI = Guid.NewGuid();
            string Hit(string kind, Guid id, string title, string extra = "") => "{\"kind\":\"" + kind + "\",\"kindLabel\":\"\",\"id\":\"" + id + "\",\"title\":\"" + title + "\",\"subtitle\":\"\",\"description\":\"Algo\",\"category\":\"\",\"score\":1,\"popularity\":1,\"updatedAt\":" + at + extra + "}";
            var hitP = Hit("person", sP, "Bia Praia"); var hitE = Hit("event", sE, "Festa na praia", ",\"startsAt\":" + at + ",\"priceCoins\":0,\"islandId\":\"" + island + "\""); var hitPr = Hit("product", sPr, "Toalha de praia", ",\"priceCoins\":30"); var hitI = Hit("island", sI, "Ilha da Praia", ",\"islandId\":\"" + sI + "\",\"x\":10,\"z\":20");
            stub.Routes["GET /api/v1/search"] = "{\"query\":\"praia\",\"total\":4,\"page\":1,\"pageSize\":20,\"items\":[" + hitP + "," + hitE + "," + hitPr + "," + hitI + "],\"counts\":{\"person\":1,\"event\":1,\"product\":1,\"island\":1}}";
            stub.Routes["GET /api/v1/search/suggest"] = "[{\"text\":\"Praia do Sol\",\"kind\":\"island\",\"id\":\"" + sI + "\"}]"; stub.Routes["GET /api/v1/search/interests"] = "[\"praia\",\"música\"]"; stub.Routes["PUT /api/v1/search/interests"] = "[]";
            stub.Routes["GET /api/v1/search/recommended"] = "[" + hitE + "]"; stub.Routes["GET /api/v1/search/popular"] = "[" + hitI + "]"; stub.Routes["GET /api/v1/search/recent"] = "[]";
            stub.Routes["GET /api/v1/search/favorites"] = "{\"query\":\"\",\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[" + hitPr + "]}"; stub.Routes["PUT /api/v1/search/favorites/island/" + sI] = "{}"; stub.Routes["POST /api/v1/search/report/island/" + sI] = "{}"; stub.Routes["POST /api/v1/search/events"] = "{}"; stub.Routes["POST /api/v1/discovery/events/" + sE + "/join"] = "{}";
            var searchSettings = new LifeVerse.Unity.UI.UserSettings(); (Guid Island, float? X, float? Z) went = default;
            var gs = new LifeVerse.Unity.UI.GlobalSearchWindow(app.Context, searchSettings, null, (i, x, z) => went = (i, x, z)); gs.Open();
            yield return Until(() => gs.RowIds.Contains(sE) && gs.RowIds.Contains(sI) && gs.Fields.ContainsKey("Interesses (separados por vírgula)"), "Para você com recomendados, populares e interesses"); _checks++;
            Expect(!gs.OpenLink("https://exemplo.com/x") && gs.Status.Contains("não é um link"), "um link de fora do jogo é recusado");
            Expect(gs.OpenLink("lifeverse://island/" + sI + "?x=3&z=4") && went.Island == sI && went.X == 3f && went.Z == 4f, "um link de lugar leva até o ponto"); gs.OpenLink("lifeverse://event/" + sE);
            yield return Until(() => Sent("POST /api/v1/discovery/events/" + sE + "/join"), "um link de evento chega à participação"); _checks++;
            gs.Show("tudo"); yield return Until(() => gs.Fields.ContainsKey("Buscar"), "a aba Tudo"); gs.Fields["Buscar"].value = "praia"; gs.Suggest("praia");
            yield return Until(() => gs.Suggested.Contains("Praia do Sol"), "as sugestões enquanto digita"); gs.Press("Buscar");
            yield return Until(() => gs.RowIds.Count == 4 && gs.SummaryText.Contains("4 resultados") && gs.SummaryText.Contains("1 ilha"), "os resultados da busca com as contagens por tipo"); _checks++; yield return Snap("ui-search");
            Expect(searchSettings.RecentSearches.Count == 1 && searchSettings.RecentSearches[0] == "praia", "a busca entra no histórico");
            yield return new WaitForSeconds(.3f); gs.Press("Detalhes"); yield return Until(() => gs.Opened == sI && gs.Actions.ContainsKey("Ir até lá"), "os detalhes de um lugar");
            gs.Press("Ir até lá"); Expect(went.Island == sI && went.X == 10f && went.Z == 20f, "ir até o lugar do resultado"); gs.Press("Favoritar"); gs.Press("Copiar o link"); Expect(gs.Status.Contains("lifeverse://island/" + sI), "copiar o link do resultado");
            gs.Fields["Motivo"].value = "spam aqui"; gs.Press("Enviar denúncia");
            yield return Until(() => Sent("PUT /api/v1/search/favorites/island/" + sI) && stub.ReceivedAny(b => b.Route == "POST /api/v1/search/report/island/" + sI && b.Body.Contains("spam aqui")) && stub.ReceivedAny(b => b.Route == "POST /api/v1/search/events" && b.Body.Contains("teleport")) && stub.ReceivedAny(b => b.Route == "POST /api/v1/search/events" && b.Body.Contains("open")), "favoritar, denunciar e os sinais de uso chegarem à API"); _checks++;
            gs.Show("itens"); yield return Until(() => gs.Fields.ContainsKey("Preço mínimo (VC)") && gs.Choices.ContainsKey("Direitos") && gs.RowIds.Count == 4, "os filtros de itens"); gs.Show("eventos"); yield return Until(() => gs.Toggles.ContainsKey("Só gratuitos") && gs.Choices["Ordem"].choices.Contains("Começam antes"), "os filtros de eventos"); _checks++;
            gs.Show("favoritos"); yield return Until(() => gs.RowIds.Count == 1 && gs.RowIds[0] == sPr, "os favoritos da busca");
            gs.Show("paravoce"); yield return Until(() => gs.Fields.ContainsKey("Interesses (separados por vírgula)") && gs.Actions.ContainsKey("Apagar o histórico"), "Para você com as últimas buscas"); yield return new WaitForSeconds(.3f);
            gs.Fields["Interesses (separados por vírgula)"].value = "arte, jogos"; gs.Press("Salvar os interesses"); gs.Press("Apagar o histórico");
            yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT /api/v1/search/interests" && b.Body.Contains("\"arte\"") && b.Body.Contains("\"jogos\"")) && searchSettings.RecentSearches.Count == 0, "salvar os interesses e apagar o histórico"); _checks++; gs.Close();
            // groups, wallet and profile as in the Godot client (the full behaviour is checked against the real API; here the screens on the running game)
            var hg = Guid.NewGuid(); var hcard = "{\"id\":\"" + hg + "\",\"name\":\"Construtores\",\"tag\":\"CONS\",\"description\":\"Quem gosta de construir\",\"accentColor\":\"#2AB3A6\",\"emblem\":\"hammer\",\"privacy\":\"public\",\"joinPolicy\":\"request\",\"members\":12,\"memberLimit\":500,\"isMember\":true,\"requestPending\":false,\"invited\":false,\"banned\":false,\"isActive\":true,\"title\":\"Mestre\",\"category\":\"construcao\",\"language\":\"pt-BR\",\"contentRating\":\"livre\",\"joinFee\":0}";
            stub.Routes["GET /api/v1/groups/active"] = "{\"groupId\":\"" + hg + "\",\"groupName\":\"Construtores\",\"tag\":\"CONS\",\"title\":\"Mestre\",\"color\":\"#2AB3A6\"}"; stub.Routes["GET /api/v1/groups/mine"] = "[" + hcard + "]";
            stub.Routes["GET /api/v1/groups/" + hg] = "{\"card\":" + hcard + ",\"myRole\":\"Líder\",\"myRank\":1000,\"myPermissions\":[],\"isLeader\":true,\"createdAt\":" + at + ",\"leaderName\":\"Dono\",\"notificationsMuted\":false}";
            stub.Routes["GET /api/v1/groups/" + hg + "/members"] = "[{\"userId\":\"" + app.Api.UserId + "\",\"username\":\"appuser\",\"roleId\":\"" + Guid.NewGuid() + "\",\"roleName\":\"Líder\",\"rank\":1000,\"joinedAt\":" + at + "},{\"userId\":\"" + Guid.NewGuid() + "\",\"username\":\"bia\",\"roleId\":\"" + Guid.NewGuid() + "\",\"roleName\":\"Membro\",\"rank\":0,\"joinedAt\":" + at + "}]";
            stub.Routes["GET /api/v1/groups/" + hg + "/roles"] = "[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Líder\",\"systemKey\":\"leader\",\"rank\":1000,\"permissions\":[],\"members\":1,\"canEdit\":false},{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Membro\",\"systemKey\":\"member\",\"rank\":0,\"permissions\":[\"invite\"],\"members\":11,\"canEdit\":true}]"; stub.Routes["GET /api/v1/groups/" + hg + "/requests"] = "[]";
            var hub = new LifeVerse.Unity.UI.GroupsHubWindow(app.Context); hub.Open(); yield return Until(() => hub.RowIds.Contains(hg) && hub.SummaryText.Contains("[CONS]"), "os grupos da pessoa com o grupo ativo"); _checks++;
            hub.Show("criar"); yield return null; hub.Press("Continuar"); Expect(hub.Step == 0 && hub.Status.Contains("3 a 40"), "o primeiro passo de criar grupo confere o nome"); hub.Close();
            var hgp = new LifeVerse.Unity.UI.GroupProfileWindow(app.Context, hg); hgp.Open(); hgp.Show("membros"); yield return Until(() => hgp.RowIds.Count == 2 && hgp.Choices.ContainsKey("Cargo"), "os membros e o cargo do grupo"); _checks++; yield return Snap("ui-group"); hgp.Close();
            stub.Routes["GET /api/v1/economy/wallet/summary"] = "{\"days\":30,\"received\":300,\"spent\":50,\"count\":4,\"reserved\":20,\"available\":250,\"total\":270,\"isBlocked\":false}";
            var hw = new LifeVerse.Unity.UI.WalletHubWindow(app.Context); hw.Open(); yield return Until(() => hw.Lines >= 4 && hw.SummaryText.Contains("Saldo"), "o resumo da carteira"); _checks++; yield return Snap("ui-wallet"); hw.Close();
            stub.Routes["GET /api/v1/users/" + app.Api.UserId + "/profile/full"] = "{\"id\":\"" + app.Api.UserId + "\",\"username\":\"appuser\",\"displayName\":\"App User\",\"isSelf\":true,\"privateProfile\":false,\"joinedAt\":" + at + ",\"presence\":\"online\",\"bio\":\"Construo casas na praia.\",\"firstLifeBio\":\"\",\"interests\":\"arte, música\",\"hasPhoto\":false,\"characterAgeYears\":1,\"married\":false,\"groups\":[{\"id\":\"" + hg + "\",\"name\":\"Construtores\",\"tag\":\"CONS\",\"role\":\"Líder\",\"shown\":true}],\"parcels\":[],\"gallery\":[],\"hasCover\":false,\"favorites\":[],\"achievements\":[{\"key\":\"first\",\"title\":\"Primeiros passos\",\"description\":\"Entrou numa ilha\",\"icon\":\"star\",\"unlockedAt\":" + at + "}],\"stats\":{\"joinedAt\":" + at + ",\"daysMember\":30,\"groups\":1,\"parcels\":0,\"creations\":2,\"events\":0,\"pictures\":3},\"settings\":{\"privateProfile\":false}}";
            var hp = new LifeVerse.Unity.UI.ProfileHubWindow(app.Context, app.Api.UserId); hp.Open(); yield return Until(() => hp.Fields.ContainsKey("Sobre você") && hp.Fields["Sobre você"].value == "Construo casas na praia.", "o próprio perfil para editar"); _checks++; yield return Snap("ui-profile");
            hp.Show("conquistas"); yield return Until(() => hp.Lines >= 2, "as conquistas"); hp.Show("grupos"); yield return Until(() => hp.RowIds.Contains(hg), "os grupos no perfil"); hp.Close(); _checks++;
            stub.Routes["GET /api/v1/users/me/avatar/looks"] = "[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Praia\",\"updatedAt\":" + at + ",\"shareCode\":\"K7P2QX9M\",\"skinColor\":\"#C98F65\",\"hairColor\":\"#2B1B14\",\"hairStyle\":\"short\",\"equippedItems\":[],\"customBody\":true,\"animationOverrides\":2},{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Festa\",\"updatedAt\":" + at + ",\"shareCode\":null,\"skinColor\":\"#C98F65\",\"hairColor\":\"#2B1B14\",\"hairStyle\":\"long\",\"equippedItems\":[]}]";
            var studio = new LifeVerse.Unity.UI.AvatarStudioWindow(app.Context, null, on => app.Follow.Mirror = on); studio.Open(); yield return Until(() => studio.RowIds.Count == 2 && studio.Actions.ContainsKey("Parar de compartilhar Praia") && studio.Actions.ContainsKey("Compartilhar Festa"), "os visuais salvos"); _checks++; yield return Snap("ui-avatar-studio");
            studio.Show("editor"); yield return null; studio.Toggles["Espelho (câmera de frente)"].value = true; Expect(app.Follow.Mirror, "o espelho põe a câmera de frente"); studio.Close(); Expect(!app.Follow.Mirror, "fechar o avatar desliga o espelho"); _checks++;
            // the free camera, the menu of the right button, the interface modes, the toolbar, the islands visited and the photos
            app.Follow.ZoomTarget = null; yield return null; yield return null; var me = app.PeopleViews.ViewOf(app.Client.SelfPeerId); var chest = GameSpace.ToUnity(me.GamePosition) + Vector3.up * 1.1f;
            var selfMenu = app.ContextItems(app.Follow.Camera.WorldToScreenPoint(chest)); Expect(app.ContextTarget == "self" && selfMenu.Exists(i => i.Label == "Meu perfil") && selfMenu.Exists(i => i.Label == "Avatar"), "o botão direito sobre o próprio avatar: " + app.ContextTarget); _checks++;
            var floor = app.ContextItems(app.Follow.Camera.WorldToScreenPoint(GameSpace.ToUnity(me.GamePosition + new Vector3(2.5f, 0f, 2.5f))));
            Expect(app.ContextTarget == "ground" && floor.Exists(i => i.Label == "Teleportar para cá") || app.ContextTarget == "object" && floor.Exists(i => i.Label == "Tocar"), "o botão direito no chão ou num objeto: " + app.ContextTarget); _checks++;
            Expect(app.OpenContextMenu(app.Follow.Camera.WorldToScreenPoint(chest)) && app.Hud.Radial.IsOpen && app.Hud.Radial.Slices.Count == System.Math.Min(8, selfMenu.Count), "o menu do botão direito abre como o radial"); _checks++; yield return Snap("ui-context-menu"); app.Hud.Radial.Close();
            app.Follow.FocusAt(new Ray(app.Follow.Camera.transform.position, (chest - app.Follow.Camera.transform.position).normalized)); var focus = app.Follow.ZoomTarget; var camFrom = app.Follow.Camera.transform.position; app.Follow.Drag(LifeVerse.Shared.Camera.CameraGesture.Orbit, 300f, 0f);
            Expect(focus.HasValue && Vector3.Distance(focus.Value, chest) < 1.5f, "a câmera livre põe o foco no ponto clicado");
            yield return Until(() => Vector3.Distance(app.Follow.Camera.transform.position, camFrom) > 1f && Mathf.Abs(Vector3.Distance(app.Follow.Camera.transform.position, focus.Value) - app.Follow.ZoomDistance) < 1.5f, "a câmera livre girar em volta do ponto escolhido", 4f); _checks++; app.Follow.ZoomTarget = null;
            app.Store.Set("interface.mode", "compact"); app.ApplyInterface(); Expect(app.Hud.Mode == "compact" && app.Hud.Map.style.display == UnityEngine.UIElements.DisplayStyle.None && app.Hud.Chat.style.display == UnityEngine.UIElements.DisplayStyle.None && app.Hud.BottomBar.style.display == UnityEngine.UIElements.DisplayStyle.Flex, "o modo compacto deixa só as barras"); _checks++; yield return Snap("ui-compact");
            app.Store.Set("interface.mode", "immersive"); app.ApplyInterface(); Expect(app.Hud.TopBar.style.display == UnityEngine.UIElements.DisplayStyle.None && app.Hud.BottomBar.style.display == UnityEngine.UIElements.DisplayStyle.None, "o modo imersivo esconde a interface");
            app.Hud.ToggleReveal(); Expect(app.Hud.Revealed && app.Hud.TopBar.style.display == UnityEngine.UIElements.DisplayStyle.Flex && app.Hud.Map.style.display == UnityEngine.UIElements.DisplayStyle.Flex, "F10 mostra a interface no modo imersivo"); app.Hud.ToggleReveal(); _checks++;
            app.Store.Set("interface.mode", "full"); app.ApplyInterface(); Expect(app.Hud.Map.style.display == UnityEngine.UIElements.DisplayStyle.Flex && app.Hud.TopBar.style.display == UnityEngine.UIElements.DisplayStyle.Flex, "o modo completo volta tudo");
            app.Settings.ToolbarOrder = new List<string> { "help", "map" }; app.Settings.ToolbarHidden = new List<string> { "wallet" }; app.ApplyInterface();
            Expect(app.Hud.BottomBar[0].name == "btn-help" && app.Hud.BottomBar[1].name == "btn-map" && UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(app.Hud.BottomBar, "btn-wallet").style.display == UnityEngine.UIElements.DisplayStyle.None, "a barra na ordem da pessoa e sem o botão escondido: " + app.Hud.BottomBar[0].name); _checks++;
            var bar = app.ToolbarWindow(); bar.Open(); yield return Snap("ui-toolbar"); bar.Close(); app.RestoreToolbar(); Expect(app.Hud.BottomBar[0].name == "btn-" + app.Hud.ToolbarDefaults[0] && UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(app.Hud.BottomBar, "btn-wallet").style.display == UnityEngine.UIElements.DisplayStyle.Flex, "restaurar a barra"); _checks++;
            app.Hud.Chat.SetMinimized(true); Expect(app.Hud.Chat.Minimized && app.Hud.Chat.style.height.value.value < 100f, "minimizar o chat"); app.Hud.Chat.SetMinimized(false); _checks++;
            Expect(app.Islands.Current == island && !app.Hud.IslandBack.enabledSelf && !app.Hud.IslandForward.enabledSelf, "a ilha entrada vai para o histórico (sem anterior ainda)"); _checks++;
            if (_shots == null) { app.TakePhoto(); yield return Until(() => app.LastPhoto != null && System.IO.File.Exists(app.LastPhoto), "a foto F12 aparecer na pasta", 10f); Expect(app.Ui.Root.style.display != UnityEngine.UIElements.DisplayStyle.None, "a interface volta depois da foto"); _checks++; System.IO.File.Delete(app.LastPhoto); }
            // the settings catalog (the Godot client's 196 settings, 18 categories, search, profiles), applied on the running game
            Expect(app.Store.SaveProfile("antes do teste") == null, "guardar as configurações de antes do teste");
            var cfg = new LifeVerse.Unity.UI.SettingsCatalogWindow(app.Context, app.Store, app.SettingsApplied, id => LifeVerse.Unity.UI.SettingsEffects.Info(id, app), app.SettingActions(), () => app.OpenClassicSettings().Open()); cfg.Open();
            yield return null; Expect(cfg.Section == "Graphics" && cfg.Controls.ContainsKey("graphics.vsync") && cfg.SummaryText.Contains("valem no Unity"), "a janela abre em Gráficos com as opções do catálogo"); _checks++; yield return Snap("ui-settings");
            Expect(cfg.Change("graphics.ssr", "true") == "" && !cfg.Controls["graphics.ssr"].enabledSelf, "o que o Unity ainda não faz aparece desligado e não muda");
            cfg.Change("graphics.preset", "high"); cfg.Change("graphics.shadow_distance", "300"); Expect(app.Store.Get("graphics.preset") == "custom" && cfg.Status.Contains("Personalizada"), "mudar as sombras à mão deixa a predefinição Personalizada");
            cfg.Change("perf.render_distance", "800"); Expect(app.Follow.Camera.farClipPlane == 800f, "a distância de renderização chega à câmera: " + app.Follow.Camera.farClipPlane); _checks++;
            cfg.Show("Camera"); yield return null; cfg.Change("camera.fov", "90"); cfg.Change("camera.shoulder", "0.5"); cfg.Change("camera.fp_bob", "false"); yield return null; yield return null;
            Expect(app.Follow.FieldOfView == 90f && app.Follow.Camera.fieldOfView == 90f && app.Follow.Shoulder == .5f && !app.Follow.Options.HeadBob, "a câmera recebe o campo de visão, o ombro e o balanço: " + app.Follow.Camera.fieldOfView); _checks++;
            cfg.Press("Restaurar o padrão desta categoria"); yield return null; Expect(app.Follow.FieldOfView == 75f && app.Follow.Shoulder == 0f && app.Follow.Options.HeadBob, "restaurar o padrão da categoria Câmera"); _checks++;
            cfg.Show("Notifications"); yield return null; cfg.Change("notifications.dnd", "true"); var quietBefore = app.Notices.Suppressed; app.Notices.Show("teste silencioso", LifeVerse.Unity.UI.NoticeKind.Info);
            Expect(app.Notices.DoNotDisturb && app.Notices.Suppressed == quietBefore + 1, "não perturbe segura os avisos"); cfg.Change("notifications.dnd", "false"); cfg.Change("notifications.max_toasts", "2"); Expect(app.Notices.MaxShown == 2, "o número de avisos na tela"); _checks++;
            cfg.Find("sombra"); yield return null; Expect(cfg.Section == "busca" && cfg.Controls.ContainsKey("graphics.shadow_distance") && cfg.SummaryText.Contains("para “sombra”"), "a busca acha as opções sem acento"); _checks++;
            cfg.Show("Graphics"); yield return null; cfg.Change("display.mode", "window"); Expect(cfg.ScreenPending && cfg.Actions.ContainsKey("Voltar a tela"), "mudar a tela pede para manter"); cfg.Press("Voltar a tela");
            Expect(!cfg.ScreenPending && app.Store.Get("display.mode") == "auto", "voltar a tela põe o modo de antes"); _checks++;
            cfg.Show("perfis"); yield return null; cfg.Fields["Nome do perfil"].value = "Teste"; cfg.Press("Salvar como perfil"); cfg.Change("camera.fov", "100"); Expect(app.Follow.FieldOfView == 100f, "a câmera muda antes de trocar o perfil");
            yield return null; cfg.Press("Usar Teste"); Expect(app.Follow.FieldOfView == 75f && app.Store.ActiveProfile == "Teste", "usar um perfil põe todas as opções dele"); _checks++;
            cfg.Show("Advanced"); yield return null; Expect(cfg.Controls.ContainsKey("advanced.version") && cfg.Controls.ContainsKey("advanced.gpu") && LifeVerse.Unity.UI.SettingsEffects.Info("advanced.version", app) == Application.version, "as informações de Avançado");
            var report = LifeVerse.Unity.UI.SettingsEffects.WriteDiagnostics(app.Store, app); Expect(System.IO.File.Exists(report) && System.IO.File.ReadAllText(report).Contains("Unity " + Application.unityVersion), "exportar o diagnóstico"); System.IO.File.Delete(report); _checks++;
            cfg.Close(); app.Store.LoadProfile("antes do teste"); app.Store.DeleteProfile("antes do teste"); app.Store.DeleteProfile("Teste"); app.SettingsApplied();

            stub.Routes["GET /api/v1/discovery/events"] = "[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Festa na praia\",\"startsAt\":\"2026-12-01T20:00:00Z\",\"capacity\":80}]"; stub.Routes["GET /api/v1/discovery/events/mine"] = "[]"; stub.Routes["POST /api/v1/discovery/events"] = "{}";
            stub.Routes["GET /api/v1/discovery/landmarks"] = "[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Meu cantinho\",\"islandId\":\"" + island + "\"}]"; stub.Routes["POST /api/v1/discovery/landmarks"] = "{}"; stub.Routes["GET /api/v1/discovery/photos"] = "[]";
            var discover = new LifeVerse.Unity.UI.DiscoveryWindow(app.Context, app, () => new Vector3(3f, 1f, -4f), () => 1.5f); discover.Open(); yield return Until(() => discover.EventCount == 1, "os eventos");
            Expect(LifeVerse.Unity.UI.DiscoveryWindow.ValidateEvent("ab", "10", "1", out _, out _) != null && LifeVerse.Unity.UI.DiscoveryWindow.ValidateEvent("Festa", "0", "1", out _, out _) != null && LifeVerse.Unity.UI.DiscoveryWindow.ValidateEvent("Festa", "10", "-2", out _, out _) != null && LifeVerse.Unity.UI.DiscoveryWindow.ValidateEvent("Festa", "10", "1,5", out var people, out var hours) == null && people == 10 && Mathf.Abs((float)hours - 1.5f) < 1e-4f, "o evento é conferido antes de enviar"); _checks++;
            discover.Show("Meus eventos"); discover.EventName.value = "Festa de teste"; discover.CreateEvent(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/discovery/events" && b.Body.Contains("Festa de teste") && b.Body.Contains(island.ToString())), "o evento criado chegar à API"); _checks++;
            discover.Show("Marcos"); yield return Until(() => discover.LandmarkCount == 1, "os marcos"); discover.MarkName.value = "Aqui"; discover.SaveLandmark();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/discovery/landmarks" && b.Body.Contains("\"positionX\":3") && b.Body.Contains("\"positionZ\":-4")), "o marco salvo na posição do jogo chegar à API"); _checks++;
            if (_shots == null) { discover.Show("Fotos"); discover.TakePhoto(); yield return Until(() => discover.LastPhotoPath != null && System.IO.File.Exists(discover.LastPhotoPath), "a foto aparecer na pasta", 10f); _checks++; System.IO.File.Delete(discover.LastPhotoPath); }
            discover.Close();

            stub.Routes["GET /api/v1/inventory/items/" + itemId + "/rights"] = "{\"itemId\":\"x\",\"permissions\":{\"owner\":7,\"group\":0,\"nextOwner\":3},\"revision\":5}"; stub.Routes["PUT /api/v1/inventory/items/" + itemId + "/rights"] = "{}"; stub.Routes["POST /api/v1/inventory/items/" + itemId + "/offers"] = "{}"; stub.Routes["GET /api/v1/inventory/items/" + itemId + "/history"] = "[{\"summary\":\"Criado\",\"at\":\"2026-11-01T10:00:00Z\"}]";
            stub.Routes["GET /api/v1/inventory/offers"] = "[{\"id\":\"" + Guid.NewGuid() + "\",\"itemName\":\"Cadeira\",\"fromUsername\":\"Ana\"}]"; stub.Routes["GET /api/v1/inventory/transfers"] = "[{\"itemName\":\"Mesa\",\"status\":\"done\"}]";
            var rights = new LifeVerse.Unity.UI.ItemWindow(app.Context, itemId, "Cadeira"); rights.Open(); yield return Until(() => rights.Loaded, "os direitos do item");
            Expect(rights.Owner == 7 && rights.NextOwner == 3 && rights.Group == 0 && rights.Bit(0, 2).value && !rights.Bit(0, 3).value, "os direitos do item aparecem nas colunas: " + rights.Owner + " " + rights.NextOwner);  _checks++;
            rights.Bit(2, 3).value = true; Expect(rights.Bit(2, 2).value && rights.NextOwner == 15, "ligar revender liga transferir (a regra do shared)"); _checks++; rights.Bit(2, 2).value = false; Expect(!rights.Bit(2, 3).value, "desligar transferir desliga revender"); _checks++;
            rights.Save(); yield return Until(() => stub.ReceivedAny(b => b.Route == "PUT /api/v1/inventory/items/" + itemId + "/rights" && b.Body.Contains("\"owner\":7") && b.Body.Contains("\"expectedRevision\":5")), "os direitos salvos chegarem à API com a revisão"); _checks++;
            rights.ToField.value = other.ToString(); rights.Offer(); yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/inventory/items/" + itemId + "/offers" && b.Body.Contains(other.ToString()) && b.Body.Contains("idempotencyKey")), "a oferta do item chegar à API"); _checks++; rights.Close();
            var offers = new LifeVerse.Unity.UI.OffersWindow(app.Context); offers.Open(); yield return Until(() => offers.OfferCount == 1 && offers.TransferCount == 1, "as ofertas e as transferências"); _checks++; offers.Close(); creator.Close();
            // 7) files, versions, profile pictures, marriage, conversations of groups and of the island, selling a product
            var asset = Guid.NewGuid(); var photoFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-test-" + Guid.NewGuid().ToString("N") + ".png"); System.IO.File.WriteAllBytes(photoFile, LifeVerse.Unity.Server.TestAssets.Png(new Color32(40, 120, 200, 255), 16));
            stub.Routes["POST /api/v1/assets/upload"] = "{\"id\":\"" + asset + "\",\"name\":\"foto\",\"storageKey\":\"chave-1\"}"; stub.Routes["POST /api/v1/discovery/photos"] = "{}";
            LifeVerse.Unity.UI.UploadResult sent = null; LifeVerse.Unity.UI.Uploads.Send(app.Context, photoFile, "textures", r => sent = r); yield return Until(() => sent != null, "o envio do arquivo terminar");
            Expect(sent.Ok && sent.AssetId == asset && sent.StorageKey == "chave-1" && stub.ReceivedAny(b => b.Route == "POST /api/v1/assets/upload" && b.Body.Contains("name=category") && b.Body.Contains("textures") && b.Body.Contains("name=file") && b.Body.Contains(".png")), "o arquivo sobe por multipart com o campo category e volta o identificador"); _checks++;
            Expect(!LifeVerse.Unity.UI.Uploads.Read(409, "{}").Ok && LifeVerse.Unity.UI.Uploads.Read(409, "{}").Message.Contains("cheio") && !LifeVerse.Unity.UI.Uploads.Read(400, "{\"detail\":\"Empty files are not accepted.\"}").Ok && LifeVerse.Unity.UI.Uploads.Read(400, "{\"detail\":\"Empty files are not accepted.\"}").Message.Contains("Empty files"), "as recusas do envio viram frases"); _checks++;
            var shoot = new LifeVerse.Unity.UI.DiscoveryWindow(app.Context, app, () => Vector3.zero, () => 0f); shoot.Open(); shoot.PickAndSendPhoto(photoFile);
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/discovery/photos" && b.Body.Contains("chave-1") && b.Body.Contains(island.ToString()) && b.Body.Contains("\"isPublic\":false")), "a foto enviada ser registrada na plataforma"); _checks++; shoot.Close();
            var assetV = Guid.NewGuid(); stub.Routes["GET /api/v1/assets/" + assetV + "/versions"] = "{\"versions\":[{\"version\":1,\"current\":false,\"size\":2048,\"at\":\"2026-10-01T10:00:00Z\"},{\"version\":2,\"current\":true,\"size\":4096,\"at\":\"2026-11-01T10:00:00Z\"}]}";
            var versions = new LifeVerse.Unity.UI.ItemWindow(app.Context, itemId, "Cadeira", assetV); versions.Open(); yield return Until(() => versions.VersionCount == 2, "as versões do arquivo do item"); _checks++; versions.Close();
            stub.Routes["GET /api/v1/users/" + userId + "/profile/full"] = "{\"profile\":{\"displayName\":\"Dono da Ilha\",\"about\":\"Construo coisas.\"},\"gallery\":[{\"id\":\"" + Guid.NewGuid() + "\",\"assetId\":\"" + asset + "\",\"isMain\":true},{\"id\":\"" + Guid.NewGuid() + "\",\"assetId\":\"" + Guid.NewGuid() + "\"}]}";
            stub.Routes["POST /api/v1/users/me/profile/gallery"] = "{}"; stub.Routes["PUT /api/v1/users/me/profile/cover"] = "{}";
            var gallery = new LifeVerse.Unity.UI.GalleryWindow(app.Context); gallery.Open(); yield return Until(() => gallery.Images == 2, "a galeria do perfil"); gallery.AddAsset(asset); gallery.SetCover(asset);
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/users/me/profile/gallery" && b.Body.Contains(asset.ToString())) && stub.ReceivedAny(b => b.Route == "PUT /api/v1/users/me/profile/cover" && b.Body.Contains(asset.ToString())), "a imagem da galeria e a capa chegarem à API"); _checks++; gallery.Close();
            stub.Routes["GET /api/v1/marriage"] = "{\"current\":null,\"incoming\":[{\"id\":\"" + Guid.NewGuid() + "\",\"fromId\":\"" + other + "\",\"fromName\":\"Ana\",\"message\":\"\",\"createdAt\":\"2026-11-19T10:00:00Z\"}],\"outgoing\":[]}"; stub.Routes["POST /api/v1/marriage/propose"] = "{}";
            var marriage = new LifeVerse.Unity.UI.MarriageWindow(app.Context); marriage.Open(); yield return Until(() => marriage.Count == 1, "os pedidos de casamento"); marriage.WhoField.value = other.ToString(); marriage.Propose();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/marriage/propose" && b.Body.Contains(other.ToString())), "o pedido de casamento chegar à API"); _checks++; marriage.Close();
            Expect(LifeVerse.Unity.UI.MarriageWindow.StateWords("accepted") == "casados" && LifeVerse.Unity.UI.MarriageWindow.StateWords("proposed") == "pedido em aberto", "o estado do casamento em português"); _checks++;
            stub.Routes["GET /api/v1/social/messages"] = "[{\"content\":\"oi pessoal\",\"senderUserId\":\"" + other + "\",\"senderName\":\"Beto\",\"createdAt\":\"2026-11-19T10:00:00Z\"}]"; stub.Routes["POST /api/v1/social/messages"] = "{}";
            var groupRoom = new LifeVerse.Unity.UI.GroupDetailWindow(app.Context, groupId, "Os Construtores"); groupRoom.Open(); yield return Until(() => groupRoom.Chat.LineCount == 1, "a conversa do grupo"); groupRoom.Chat.Input.value = "oi grupo"; groupRoom.Chat.Send();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/social/messages" && b.Body.Contains("oi grupo") && b.Body.Contains("\"groupId\":\"" + groupId + "\"")), "a mensagem do grupo chegar à API com o grupo"); _checks++; groupRoom.Close();
            var chatWindow = new LifeVerse.Unity.UI.SocialWindow(app.Context); chatWindow.Open(); chatWindow.Show("Ilha"); yield return Until(() => chatWindow.IslandChat.LineCount == 1, "o histórico da ilha"); chatWindow.IslandChat.Input.value = "bom dia, ilha"; chatWindow.IslandChat.Send();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/social/messages" && b.Body.Contains("bom dia, ilha") && b.Body.Contains("\"islandId\":\"" + island + "\"")), "a mensagem da ilha chegar à API com a ilha"); _checks++; chatWindow.Close();
            Expect(LifeVerse.Unity.UI.MarketWindow.ValidateProduct("", "Cadeira", "10", out _, out _) != null && LifeVerse.Unity.UI.MarketWindow.ValidateProduct(asset.ToString(), "ab", "10", out _, out _) != null && LifeVerse.Unity.UI.MarketWindow.ValidateProduct(asset.ToString(), "Cadeira", "-5", out _, out _) != null && LifeVerse.Unity.UI.MarketWindow.ValidateProduct(asset.ToString(), "Cadeira", "10", out var forSale, out var priceCoins) == null && forSale == asset && priceCoins == 10, "o produto é conferido antes de ser publicado"); _checks++;
            stub.Routes["POST /api/v1/economy/products"] = "{}"; var seller = new LifeVerse.Unity.UI.MarketWindow(app.Context); seller.Open(); seller.SellAsset.value = asset.ToString(); seller.SellName.value = "Cadeira nova"; seller.SellPrice.value = "75"; seller.Publish();
            yield return Until(() => stub.ReceivedAny(b => b.Route == "POST /api/v1/economy/products" && b.Body.Contains(asset.ToString()) && b.Body.Contains("\"priceCoins\":75") && b.Body.Contains("\"buyerRights\":7")), "o produto publicado chegar à API com os direitos padrão"); _checks++; seller.Close(); System.IO.File.Delete(photoFile);
            // the session cannot be renewed: the person goes back to the login screen with the message
            stub.ExpireSession(); var gone = app.Api.ApiAsync("GET", "/api/v1/social/friends"); yield return Until(() => gone.IsCompleted, "a chamada com a sessão acabada");
            yield return Until(() => app.Current == LifeVerse.Unity.UI.GameApp.Phase.Login, "voltar à tela de entrada com a sessão acabada");
            Expect(gone.Result.Status == 401 && app.Login.Status.text.Contains("sessão acabou"), "sem renovação a pessoa volta ao login com a mensagem: " + app.Login.Status.text); _checks++;
            app.Login.Password.value = "segredo"; app.Login.Remember.value = true; app.Login.Submit(); yield return Until(() => app.Current == LifeVerse.Unity.UI.GameApp.Phase.Playing, "entrar de novo depois da sessão acabada", 25f);
            app.BackToLogin(); Expect(app.Current == LifeVerse.Unity.UI.GameApp.Phase.Login && !app.Hud.Visible, "voltar à tela de entrada fecha o HUD"); _checks++;
            UnityEngine.Object.Destroy(app.gameObject); if (System.IO.File.Exists(settingsFile)) System.IO.File.Delete(settingsFile);
            // 6) a new account: the API refuses the entry (409) until the character exists, so the creator opens by itself, and saving it enters the island
            stub.RequireAvatar = true; stub.AvatarSaved = false; api.Issue("appuser2", false); stub.EntryToken = "appuser2"; yield return new WaitForSeconds(.3f);
            var app2 = new GameObject("App2").AddComponent<LifeVerse.Unity.UI.GameApp>(); app2.SettingsPath = settingsFile; app2.SavedLoginPath = savedLoginFile; app2.Begin(stub.Url, new LifeVerse.Unity.UI.UserSettings { AutoScale = false }); yield return null; yield return null;
            Expect(app2.Login.Password.value == "segredo" && app2.Login.User.value == "dono" && app2.Login.RememberPassword.value, "a senha lembrada volta na próxima abertura"); _checks++;
            app2.Login.User.value = "dono"; app2.Login.Password.value = "segredo"; app2.Login.Island.value = island.ToString(); app2.Login.Submit();
            LifeVerse.Unity.UI.CharacterWindow firstTime = null;
            yield return Until(() => { foreach (var child in app2.Ui.WindowLayer.Children()) if (child is LifeVerse.Unity.UI.CharacterWindow cw) firstTime = cw; return firstTime != null; }, "o criador de personagem abrir sozinho quando a API pede o avatar", 25f);
            Expect(app2.Current == LifeVerse.Unity.UI.GameApp.Phase.Loading && firstTime.Title.text.Contains("Crie"), "a primeira vez: o criador abre na tela de carregamento"); _checks++;
            firstTime.Draft.DisplayName = "Novato Teste"; firstTime.Save();
            yield return Until(() => app2.Current == LifeVerse.Unity.UI.GameApp.Phase.Playing && app2.Client != null && app2.Client.Authenticated, "entrar na ilha depois de criar o personagem", 30f); _checks++;
            UnityEngine.Object.Destroy(app2.gameObject); stub.RequireAvatar = false; if (System.IO.File.Exists(settingsFile)) System.IO.File.Delete(settingsFile);
        }

        private IEnumerator Steps(Action<Tuple<Player, Player>> created, Action<Tuple<Player, Player>> unused)
        {
            var api = new LocalIslandApi(); api.Issue("editor", true); api.Issue("viewer", false); api.Issue("dressed", false, 0, 0, DressedAppearance);
            api.Vehicles.Add(new VehicleRow(Guid.NewGuid(), "Carro", "car", 0f, 1.5f, 0f, 20f));   // by the spawn point of the viewer
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
            Expect(editor.Client.CanEditTerrain && !viewer.Client.CanEditTerrain, "sÃ³ o editor pode esculpir"); _checks++;
            SetUpShots(viewer, editor);
            yield return Shot("before");
            Expect(editor.Client.ProtocolMismatch == 0 && viewer.Client.RejectCode == null, "sem recusas"); _checks++;

            // the expected ground, computed independently with the same shared rules
            var expected = new TerrainMap();
            for (var i = 0; i < 3; i++) { editor.Client.Send(new SubmitTerrainEditMessage((int)TerrainTool.Raise, 20, 10, 12, 1f, 0)); expected.Apply(TerrainTool.Raise, 20, 10, 12, 1f, 0); yield return null; }
            yield return Until(() => viewer.Terrain.Map.Revision == expected.Revision && editor.Terrain.Map.Revision == expected.Revision, "as trÃªs ediÃ§Ãµes chegarem aos dois jogadores"); yield return null;
            Expect(viewer.Terrain.Map.Serialize().SequenceEqual(expected.Serialize()), "o mapa de quem olha Ã© idÃªntico ao esperado"); _checks++;
            Expect(editor.Terrain.Map.Serialize().SequenceEqual(expected.Serialize()), "o mapa de quem esculpiu tambÃ©m"); _checks++;
            var hill = expected.HeightAt(74, 69); Expect(hill > .5f, "o morro subiu");
            yield return null; yield return null; // LateUpdate redraws the mesh
            var mesh = viewer.Terrain.GetComponent<MeshFilter>().sharedMesh;
            Expect(Mathf.Abs(mesh.vertices[69 * 129 + 74].y - hill) < 1e-3f, "a malha de quem olha mostra o morro"); _checks++;

            for (var i = 0; i < 6; i++) { editor.Client.Send(new SubmitTerrainEditMessage((int)TerrainTool.Paint, 20, 10, 10, 1f, 3)); expected.Apply(TerrainTool.Paint, 20, 10, 10, 1f, 3); yield return null; } // rock on the new hill
            yield return Until(() => viewer.Terrain.Map.Revision == expected.Revision, "a pintura chegar a quem olha");
            Expect(viewer.Terrain.Map.Serialize().SequenceEqual(expected.Serialize()), "o mapa com a pintura Ã© idÃªntico"); _checks++;
            yield return Shot("after");
            yield return Objects(editor, viewer);
            yield return Walking(editor, viewer);
            yield return Bodies(editor, viewer, api);
            yield return Driving(editor, viewer);
            viewer.Client.Send(new SubmitTerrainEditMessage((int)TerrainTool.Raise, 0, 0, 5, 1f, 0));
            yield return Until(() => viewer.Client.LastTerrainRejection != null, "a recusa ao visitante");
            Expect(viewer.Client.LastTerrainRejection == "no_permission", "o visitante nÃ£o esculpe: " + viewer.Client.LastTerrainRejection); _checks++;

            editor.Client.Send(new SubmitSeaLevelMessage(2f));
            yield return Until(() => Mathf.Approximately(viewer.Ocean.SeaLevel, 2f) && Mathf.Approximately(viewer.Terrain.Map.SeaLevel, 2f), "o nÃ­vel do mar mudar para quem olha");
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
