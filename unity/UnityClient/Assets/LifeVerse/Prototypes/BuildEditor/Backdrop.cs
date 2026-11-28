using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LifeVerse.Shared.Terrain;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.Prototypes.BuildEditor
{
    /// <summary>A thing of the scene the selection can be: its box in game space (centre, size, turn around the height).</summary>
    public sealed class Thing
    {
        public Vector3 Centre, Size; public float Yaw;
        /// <summary>The eight corners (bit 0: x, bit 1: height, bit 2: depth).</summary>
        public Vector3[] Corners() { var q = Quaternion.Euler(0f, Yaw, 0f); return Enumerable.Range(0, 8).Select(i => Centre + q * Vector3.Scale(Size / 2f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))).ToArray(); }
    }

    /// <summary>Where a point of the scene falls on the picture, in panel units.</summary>
    public sealed class Projector
    {
        private readonly Matrix4x4 _matrix; private readonly Vector2 _panel; public readonly Vector3 Eye;
        public Projector(Camera camera, Vector2 panel) { _matrix = camera.projectionMatrix * camera.worldToCameraMatrix; _panel = panel; Eye = GameSpace.ToGame(camera.transform.position); }
        public Vector2 At(Vector3 game)
        {
            var u = GameSpace.ToUnity(game); var c = _matrix * new Vector4(u.x, u.y, u.z, 1f); var w = Mathf.Max(.001f, c.w);
            return new Vector2((c.x / w * .5f + .5f) * _panel.x, (.5f - c.y / w * .5f) * _panel.y);
        }
    }

    /// <summary>
    /// The picture behind the prototypes: a building spot on the shore, rendered by the game's own world code (terrain, ocean, sky and building primitives). It is composed for the
    /// prototype; it is not an island of the platform.
    /// </summary>
    public sealed class Backdrop
    {
        public const float Ground = .16f;
        public static readonly Thing Crate = new Thing { Centre = new Vector3(0f, Ground + .75f, 0f), Size = new Vector3(1.5f, 1.5f, 1.5f), Yaw = 28f };
        public static readonly Thing Ring = new Thing { Centre = new Vector3(-2.7f, Ground + .25f, -.8f), Size = new Vector3(1.8f, .5f, 1.8f) };
        public static readonly Thing Ball = new Thing { Centre = new Vector3(2.4f, Ground + .6f, 1.1f), Size = new Vector3(1.2f, 1.2f, 1.2f) };
        public static readonly Thing Column = new Thing { Centre = new Vector3(-2.2f, Ground + 1.2f, 2.2f), Size = new Vector3(.7f, 2.4f, .7f) };
        public static readonly Thing[] Table =
        {
            new Thing { Centre = new Vector3(-4.2f, Ground + 1.09f, 1.2f), Size = new Vector3(2.4f, .18f, 1.4f) },     // the top: the root of the set
            new Thing { Centre = new Vector3(-5.25f, Ground + .5f, .62f), Size = new Vector3(.18f, 1f, .18f) },
            new Thing { Centre = new Vector3(-3.15f, Ground + .5f, .62f), Size = new Vector3(.18f, 1f, .18f) },
            new Thing { Centre = new Vector3(-5.25f, Ground + .5f, 1.78f), Size = new Vector3(.18f, 1f, .18f) },
            new Thing { Centre = new Vector3(-3.15f, Ground + .5f, 1.78f), Size = new Vector3(.18f, 1f, .18f) },
        };

        private Camera _camera; private IslandEnvironment _environment;

        public IEnumerator Build()
        {
            var map = new TerrainMap();
            for (var i = 0; i < 22; i++) map.Apply(TerrainTool.Raise, -60, 30, TerrainMap.MaxBrushRadius, .8f);
            for (var z = -40; z <= -6; z += 3) map.Apply(TerrainTool.Paint, 0f, z, 2.4f, 1f, 1);
            var cameraObject = new GameObject("Backdrop camera") { tag = "MainCamera" }; _camera = cameraObject.AddComponent<Camera>(); _camera.nearClipPlane = .2f; _camera.farClipPlane = 6000f; _camera.fieldOfView = 46f;
            cameraObject.AddComponent<HDAdditionalCameraData>().antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var terrain = new GameObject("Terrain").AddComponent<TerrainView>(); terrain.Apply(map);
            var ocean = new GameObject("Ocean").AddComponent<OceanView>(); ocean.Camera = _camera; ocean.SeaLevel = map.SeaLevel;
            _environment = new GameObject("Environment").AddComponent<IslandEnvironment>(); _environment.Initialize(_camera, map.SeaLevel); _environment.DayNightCycle = false; _environment.Hour = 15.4f; _environment.Weather = "clear";
            var objects = new GameObject("Objects").AddComponent<WorldObjects>(); objects.Camera = _camera; objects.Load(Scenery());
            yield return null;
        }

        /// <summary>Renders the scene looking at a point, into a picture of the size asked for.</summary>
        public IEnumerator Shoot(Vector3 look, Vector3 from, int width, int height, float panelScale, Action<Texture2D, Projector> done)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); _camera.targetTexture = target;
            _camera.transform.position = GameSpace.ToUnity(look + from); _camera.transform.LookAt(GameSpace.ToUnity(look));
            for (var i = 0; i < 50; i++) { _environment.Apply(.1f); yield return null; }
            var projector = new Projector(_camera, new Vector2(width, height) / panelScale);
            var before = RenderTexture.active; RenderTexture.active = target; var picture = new Texture2D(width, height, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp }; picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply(); RenderTexture.active = before;
            _camera.targetTexture = null; UnityEngine.Object.Destroy(target); done(picture, projector);
        }

        private static string Scenery()
        {
            var list = new StringBuilder("["); var count = 0;
            string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            void Put(string type, string colour, string builtin, Vector3 at, Vector3 size, float yaw = 0f)
            {
                if (count++ > 0) list.Append(',');
                list.Append("{\"id\":\"").Append(Guid.NewGuid()).Append("\",\"name\":\"p").Append(count).Append("\",\"primitiveType\":\"").Append(type)
                    .Append("\",\"materialJson\":\"{\\\"color\\\":\\\"").Append(colour).Append("\\\",\\\"roughness\\\":0.75").Append(builtin == null ? "" : ",\\\"builtin\\\":\\\"" + builtin + "\\\"").Append("}\",\"positionX\":").Append(F(at.x))
                    .Append(",\"positionY\":").Append(F(at.y)).Append(",\"positionZ\":").Append(F(at.z)).Append(",\"rotationY\":").Append(F(yaw))
                    .Append(",\"scaleX\":").Append(F(size.x)).Append(",\"scaleY\":").Append(F(size.y)).Append(",\"scaleZ\":").Append(F(size.z)).Append('}');
            }
            void Thing(string type, string colour, string builtin, Thing t) => Put(type, colour, builtin, t.Centre, t.Size, t.Yaw);
            for (var i = -6; i <= 6; i++) Put("cube", i % 2 == 0 ? "#B98A5E" : "#A67A52", null, new Vector3(i * 1.25f - 1f, Ground / 2f, 1f), new Vector3(1.18f, Ground, 12f));   // the deck
            Thing("cube", "#E0B98A", "planks", Crate); Thing("torus", "#C0503A", null, Ring); Thing("sphere", "#2F8C80", null, Ball); Thing("cylinder", "#E8DFCF", null, Column);
            Thing("cube", "#8A5A38", "planks", Table[0]); foreach (var leg in Table.Skip(1)) Thing("cube", "#6E462C", null, leg);
            foreach (var (x, z, s) in new[] { (-13f, 12f, 4.6f), (10f, 14f, 5.4f), (-19f, 26f, 6f), (16f, 4f, 4.2f), (4f, 22f, 5f) }) Put("tree", "#4F8A3E", null, new Vector3(x, 0f, z), new Vector3(s, s, s), x * 11f);
            foreach (var (x, z) in new[] { (8.6f, -1f), (-11f, -2f), (9.4f, 7f), (-9.5f, 8f) }) Put("grass", "#6FA24A", null, new Vector3(x, 0f, z), new Vector3(1.4f, 1.1f, 1.4f), z * 17f);
            return list.Append(']').ToString();
        }
    }
}
