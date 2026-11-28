using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LifeVerse.Shared.Terrain;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>
    /// The picture behind the prototypes: a small seaside square rendered by the game's own world code (terrain, ocean, sky, building primitives and avatars) with no interface on it.
    /// The scene is composed for this comparison; it is not an island of the platform. Where each person's head falls on the picture is saved beside it, for the name plates.
    /// </summary>
    public static class WorldBackdrop
    {
        [Serializable] public sealed class Person { public string name; public string presence; public float x, y, distance; }
        [Serializable] public sealed class People { public List<Person> list = new List<Person>(); }

        public static readonly (string File, int Width, int Height, float Hour)[] Pictures =
        {
            ("fundo-dia-16x9", 2560, 1440, 15.2f), ("fundo-dia-21x9", 2560, 1080, 15.2f), ("fundo-entardecer-16x9", 2560, 1440, 17.9f), ("fundo-noite-16x9", 2560, 1440, 21.4f),
        };

        public static IEnumerator Render(string folder)
        {
            Directory.CreateDirectory(folder);
            var map = new TerrainMap();
            for (var i = 0; i < 26; i++) map.Apply(TerrainTool.Raise, 62, 22, TerrainMap.MaxBrushRadius, .8f);   // a soft hill to the east
            for (var i = 0; i < 14; i++) map.Apply(TerrainTool.Raise, -70, 40, 20, .7f);
            for (var z = -70; z <= 8; z += 3) map.Apply(TerrainTool.Paint, Mathf.Sin(z * .07f) * 2.5f, z, 2.6f, 1f, 1);   // the path to the square
            map.Apply(TerrainTool.Paint, 62, 22, 12, 1f, 7);

            var cameraObject = new GameObject("Backdrop camera") { tag = "MainCamera" }; var camera = cameraObject.AddComponent<Camera>(); camera.nearClipPlane = .2f; camera.farClipPlane = 6000f; camera.fieldOfView = 56f;
            var data = cameraObject.AddComponent<HDAdditionalCameraData>(); data.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var terrain = new GameObject("Terrain").AddComponent<TerrainView>(); terrain.Apply(map);
            var ocean = new GameObject("Ocean").AddComponent<OceanView>(); ocean.Camera = camera; ocean.SeaLevel = map.SeaLevel;
            var environment = new GameObject("Environment").AddComponent<IslandEnvironment>(); environment.Initialize(camera, map.SeaLevel); environment.DayNightCycle = false;
            var objects = new GameObject("Objects").AddComponent<WorldObjects>(); objects.Camera = camera;
            objects.Load(Scenery(map));

            AvatarView.PlatesDrawn = true;
            var cast = new (string Name, string Presence, float X, float Z, float Yaw, Color Shirt)[]
            {
                ("Você", "online", .6f, -62f, 4f, new Color(.80f, .34f, .26f)),
                ("Ana", "online", -3.4f, -52f, 148f, new Color(.13f, .50f, .48f)),
                ("Beto", "online", -1.6f, -51f, 214f, new Color(.93f, .66f, .22f)),
                ("Marina", "away", 5.8f, -47f, 232f, new Color(.40f, .33f, .62f)),
                ("Caio", "busy", -9.5f, -38f, 120f, new Color(.22f, .36f, .62f)),
            };
            var avatars = new List<(string Name, string Presence, Transform Body)>();
            var peer = 1;
            foreach (var person in cast)
            {
                var view = AvatarView.Create(peer++, person.Name, person.Shirt); view.External = true;
                view.transform.position = GameSpace.ToUnity(new Vector3(person.X, map.SampleHeight(person.X, person.Z), person.Z));
                var ahead = GameSpace.ToUnity(new Vector3(Mathf.Sin(person.Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(person.Yaw * Mathf.Deg2Rad)));
                view.transform.rotation = Quaternion.LookRotation(ahead, Vector3.up); avatars.Add((person.Name, person.Presence, view.transform));
            }

            var ground = map.SampleHeight(0f, -66f);
            var eye = new Vector3(-.9f, ground + 3.1f, -69.2f); var look = new Vector3(.2f, ground + 1.75f, -55f);
            foreach (var picture in Pictures)
            {
                var target = new RenderTexture(picture.Width, picture.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = picture.File }; camera.targetTexture = target;
                camera.transform.position = GameSpace.ToUnity(eye); camera.transform.LookAt(GameSpace.ToUnity(look));
                environment.Hour = picture.Hour; environment.Weather = "clear";
                for (var i = 0; i < 60; i++) { environment.Apply(.1f); yield return null; }
                CaptureRunner.Save(target, Path.Combine(folder, picture.File + ".png"));
                var people = new People();
                foreach (var (name, presence, body) in avatars)
                {
                    var at = camera.WorldToViewportPoint(body.position + Vector3.up * 2.02f);
                    people.list.Add(new Person { name = name, presence = presence, x = at.x, y = at.y, distance = at.z });
                }
                File.WriteAllText(Path.Combine(folder, picture.File + ".json"), JsonUtility.ToJson(people, true));
                Console.WriteLine("event=hud_backdrop file=" + picture.File);
                camera.targetTexture = null; UnityEngine.Object.Destroy(target);
            }
            foreach (var root in new[] { cameraObject, terrain.gameObject, ocean.gameObject, environment.gameObject, objects.gameObject }) UnityEngine.Object.Destroy(root);
            foreach (var (_, _, body) in avatars) UnityEngine.Object.Destroy(body.gameObject);
        }

        /// <summary>The square: a wooden deck, a kiosk, benches, palms and trees, and the lighthouse on the shore.</summary>
        private static string Scenery(TerrainMap map)
        {
            var list = new StringBuilder("["); var count = 0;
            string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            void Put(string type, string colour, float x, float lift, float z, float sx, float sy, float sz, float yaw = 0f, float rough = .75f)
            {
                if (count++ > 0) list.Append(',');
                var y = map.SampleHeight(x, z) + lift;
                list.Append("{\"id\":\"").Append(Guid.NewGuid()).Append("\",\"name\":\"p").Append(count).Append("\",\"primitiveType\":\"").Append(type)
                    .Append("\",\"materialJson\":\"{\\\"color\\\":\\\"").Append(colour).Append("\\\",\\\"roughness\\\":").Append(F(rough)).Append("}\",\"positionX\":").Append(F(x))
                    .Append(",\"positionY\":").Append(F(y)).Append(",\"positionZ\":").Append(F(z)).Append(",\"rotationY\":").Append(F(yaw * Mathf.Deg2Rad))
                    .Append(",\"scaleX\":").Append(F(sx)).Append(",\"scaleY\":").Append(F(sy)).Append(",\"scaleZ\":").Append(F(sz)).Append('}');
            }
            for (var i = -5; i <= 5; i++) Put("cube", i % 2 == 0 ? "#A9764C" : "#9A6A43", i * 1.5f, .08f, -42f, 1.42f, .16f, 14f);   // the deck
            // the kiosk
            foreach (var (dx, dz) in new[] { (-2.2f, -1.8f), (2.2f, -1.8f), (-2.2f, 1.8f), (2.2f, 1.8f) }) Put("cylinder", "#E8DFCF", 8.5f + dx, 1.5f, -34f + dz, .28f, 3f, .28f);
            Put("cone", "#B4553C", 8.5f, 3.9f, -34f, 6.8f, 1.9f, 6.8f); Put("cube", "#7C5638", 8.5f, .65f, -34f, 4.2f, 1.1f, 1.2f);
            // benches and planters
            Put("cube", "#7C5638", -7f, .5f, -45f, 3f, .18f, .8f, 28f); Put("cube", "#7C5638", -7.3f, .95f, -44.6f, 3f, .6f, .14f, 28f);
            Put("cube", "#D8CFC0", 4.6f, .4f, -38f, 1.4f, .8f, 1.4f); Put("sphere", "#4E7C3A", 4.6f, 1.25f, -38f, 1.5f, 1.1f, 1.5f);
            // the lighthouse
            Put("cylinder", "#F1ECE2", -30f, 7f, 14f, 6f, 14f, 6f); Put("cylinder", "#B8453A", -30f, 16f, 14f, 5.3f, 4f, 5.3f); Put("cylinder", "#F1ECE2", -30f, 20f, 14f, 4.6f, 4f, 4.6f);
            Put("cylinder", "#2E3A45", -30f, 22.6f, 14f, 5.4f, 1.2f, 5.4f); Put("cylinder", "#FFE7A6", -30f, 24.2f, 14f, 3.2f, 2f, 3.2f, 0f, .2f); Put("cone", "#B8453A", -30f, 26.4f, 14f, 5f, 2.4f, 5f);
            foreach (var (x, z, s) in new[] { (-14f, -34f, 4.6f), (-19f, -20f, 5.6f), (17f, -24f, 5.2f), (24f, -40f, 4.4f), (-24f, -48f, 4f), (14f, -4f, 6f), (-6f, -8f, 5f), (33f, -16f, 5.5f) }) Put("tree", "#4F8A3E", x, 0f, z, s, s, s, x * 11f);
            foreach (var (x, z) in new[] { (-5.5f, -58f), (7.5f, -56f), (11f, -61f), (-11f, -55f), (12f, -45f) }) Put("grass", "#6FA24A", x, 0f, z, 1.4f, 1.1f, 1.4f, z * 7f);
            return list.Append(']').ToString();
        }
    }
}
