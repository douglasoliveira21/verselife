using System;
using System.Collections;
using System.IO;
using System.Linq;
using LifeVerse.Shared.Terrain;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Builds a small island (sculpted terrain, ocean, sky) and saves screenshots of it, to see the result of the world code on the real GPU:
    ///   LifeVerse.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -lifeverse-world-preview C:\out\shot
    /// writes shot-island.png, shot-sunset.png, shot-night.png, shot-storm.png, shot-under.png and exits. It needs a graphics device (no -nographics).
    /// </summary>
    public sealed class WorldPreview : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-world-preview");
            if (index < 0 || index + 1 >= args.Length) return;
            new GameObject("World Preview").AddComponent<WorldPreview>().Prefix = args[index + 1];
        }

        public string Prefix;

        /// <summary>The sculpted island the preview shows: hills, a rocky top, a dirt path, sand on the beach (the default map already has the sloping beach).</summary>
        public static TerrainMap SampleIsland()
        {
            var map = new TerrainMap();
            for (var i = 0; i < 90; i++) map.Apply(TerrainTool.Raise, 15, 10, TerrainMap.MaxBrushRadius, 1f);
            for (var i = 0; i < 45; i++) map.Apply(TerrainTool.Raise, -45, -30, 20, 1f);
            for (var i = 0; i < 30; i++) map.Apply(TerrainTool.Raise, 60, -50, 18, 1f);
            map.Apply(TerrainTool.Paint, 15, 10, 9, 1f, 3);   // rock on the top
            map.Apply(TerrainTool.Paint, -45, -30, 12, 1f, 7); // dry grass on the low hill
            for (var x = -40; x <= 15; x += 6) map.Apply(TerrainTool.Paint, x, x * .3f - 10, 3, 1f, 1); // a dirt path
            return map;
        }

        private IEnumerator Start()
        {
            Application.targetFrameRate = 30;
            var map = SampleIsland();
            var cameraObject = new GameObject("Camera") { tag = "MainCamera" }; var camera = cameraObject.AddComponent<Camera>(); camera.nearClipPlane = .2f; camera.farClipPlane = 6000f; camera.fieldOfView = 62f;
            cameraObject.AddComponent<HDAdditionalCameraData>();
            // in the editor the picture is drawn into a texture of the size of the comparison photos: it needs no Game view, whatever size that has
            RenderTexture target = null;
            if (Application.isEditor) { target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "preview" }; camera.targetTexture = target; }
            var terrain = new GameObject("Terrain").AddComponent<TerrainView>(); terrain.Apply(map);
            var ocean = new GameObject("Ocean").AddComponent<OceanView>(); ocean.Camera = camera; ocean.SeaLevel = map.SeaLevel;
            var environment = new GameObject("Environment").AddComponent<IslandEnvironment>(); environment.Initialize(camera, map.SeaLevel); environment.DayNightCycle = false;

            var views = new (string Name, Vector3 Position, Vector3 Target, float Hour, string Weather)[]
            {
                ("island", new Vector3(-95, 26, -150), new Vector3(0, 8, 0), 14f, "clear"),
                ("sunset", new Vector3(-95, 14, -150), new Vector3(0, 12, 0), 17.95f, "clear"),
                ("night", new Vector3(-95, 18, -150), new Vector3(0, 8, 0), 22f, "clear"),
                ("storm", new Vector3(-95, 22, -150), new Vector3(0, 8, 0), 13f, "storm"),
                ("under", new Vector3(30, -4.5f, -120), new Vector3(0, -2f, 0), 14f, "clear"),
                ("waves", new Vector3(-20, .9f, -170), new Vector3(10, 0f, -60), 15f, "clear"),
                ("rain", new Vector3(-60, 6f, -60), new Vector3(10, 8f, 10), 13f, "rain"),
                ("beach", new Vector3(0, 2.5f, -150), new Vector3(0, 1f, -90), 15f, "clear"),
                ("ground", new Vector3(-10, 1.7f, -40), new Vector3(15, 6f, 10), 11f, "clear"),
            };
            foreach (var view in views)
            {
                camera.transform.position = GameSpace.ToUnity(view.Position); camera.transform.LookAt(GameSpace.ToUnity(view.Target));
                environment.Hour = view.Hour; environment.Weather = view.Weather;
                for (var i = 0; i < 40; i++) { environment.Apply(.1f); yield return null; }
                var path = $"{Prefix}-{view.Name}.png"; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                if (File.Exists(path)) File.Delete(path);
                if (target != null) Save(target, path); else ScreenCapture.CaptureScreenshot(path);
                var waited = 0; while (!File.Exists(path) && waited++ < 120) yield return null;
                Console.WriteLine($"event=world_preview day={environment.DayFactor:F3} sunLux={environment.Sun.GetComponent<HDAdditionalLightData>().intensity:F2} sunPitch={environment.Sun.transform.eulerAngles.x:F0} view={view.Name} file={path} saved={File.Exists(path)}");
                for (var i = 0; i < 5; i++) yield return null;
            }
            Console.WriteLine("event=world_preview result=done"); CheckRun.Quit(0);
        }

        public const int Width = 1280, Height = 720;

        private static void Save(RenderTexture source, string path)
        {
            var before = RenderTexture.active; RenderTexture.active = source;
            var picture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); picture.Apply();
            RenderTexture.active = before; File.WriteAllBytes(path, picture.EncodeToPNG()); Destroy(picture);
        }
    }
}
