using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.IslandServer.Core;
using LifeVerse.Shared.Terrain;
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

        private sealed class Player { public WorldClient Client; public TerrainView Terrain; public OceanView Ocean; }

        private IslandServerCore _server;
        private int _checks;
        private string _shots; private Camera _camera; private IslandEnvironment _environment;

        /// <summary>With -lifeverse-world-shots PREFIX (and a graphics device) the viewer is drawn too (sky, sea, camera) and photographed before and after the edits.</summary>
        private void SetUpShots(Player viewer, params Player[] hidden)
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-world-shots"); if (index < 0 || index + 1 >= args.Length) return;
            _shots = args[index + 1];
            foreach (var other in hidden) { other.Terrain.GetComponent<MeshRenderer>().enabled = false; other.Ocean.gameObject.SetActive(false); }
            var cameraObject = new GameObject("Camera") { tag = "MainCamera" }; _camera = cameraObject.AddComponent<Camera>(); _camera.nearClipPlane = .2f; _camera.farClipPlane = 6000f; _camera.fieldOfView = 62f;
            cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            cameraObject.transform.position = new Vector3(-95, 26, -150); cameraObject.transform.LookAt(new Vector3(0, 8, 0)); viewer.Ocean.Camera = _camera;
            _environment = new GameObject("Environment").AddComponent<IslandEnvironment>(); _environment.Initialize(_camera, -.8f); _environment.DayNightCycle = false; _environment.Hour = 14f;
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
            var client = root.AddComponent<WorldClient>(); client.Terrain = terrain; client.Ocean = ocean; client.Token = name;
            return new Player { Client = client, Terrain = terrain, Ocean = ocean };
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
            created(Tuple.Create(editor, viewer));
        }
    }
}
