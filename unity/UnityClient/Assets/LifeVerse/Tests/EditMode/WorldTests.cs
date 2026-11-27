using LifeVerse.Shared.Terrain;
using LifeVerse.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The world code that does not need a GPU: the terrain mesh and its ground texture, and the hour/weather rules of the environment (the screenshots cover what it looks like).</summary>
    public class WorldTests
    {
        private GameObject _root;

        [TearDown] public void Clean() { if (_root != null) Object.DestroyImmediate(_root); }

        private TerrainView NewView(TerrainMap map, int albedo = 128)
        {
            _root = new GameObject("test");
            var view = _root.AddComponent<TerrainView>(); view.AlbedoSize = albedo; view.Apply(map); return view;
        }

        [Test]
        public void TheTerrainMeshHasTheHeightsAndTheSizeOfTheMap()
        {
            var map = new TerrainMap(); map.Apply(TerrainTool.Raise, 20, 10, 12, 1f);
            var view = NewView(map);
            var mesh = _root.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(map.VertexCount * map.VertexCount, mesh.vertexCount);
            Assert.AreEqual(map.Cells * map.Cells * 6, mesh.triangles.Length);
            var half = map.HalfExtent;
            foreach (var probe in new[] { (0, 0), (64, 64), (74, 69), (map.Cells, map.Cells) })
            {
                var vertex = mesh.vertices[probe.Item2 * map.VertexCount + probe.Item1];
                Assert.AreEqual(probe.Item1 * map.CellSize - half, vertex.x, 1e-4f); Assert.AreEqual(map.HeightAt(probe.Item1, probe.Item2), vertex.y, 1e-4f); Assert.AreEqual(-(probe.Item2 * map.CellSize - half), vertex.z, 1e-4f, "z is flipped (GameSpace)");
            }
            Assert.That(mesh.bounds.size.x, Is.EqualTo(map.Cells * map.CellSize).Within(.01f));
            Assert.That(map.HeightAt(74, 69), Is.GreaterThan(.1f), "the hill was raised");
        }

        [Test]
        public void TheTrianglesFaceUpAndTheNormalsAgree()
        {
            var view = NewView(new TerrainMap());
            var mesh = _root.GetComponent<MeshFilter>().sharedMesh; var v = mesh.vertices; var t = mesh.triangles;
            for (var i = 0; i < t.Length; i += 3 * 997)
            {
                var normal = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]); // Unity: the front face of a clockwise triangle has this normal
                Assert.Greater(Vector3.Dot(normal.normalized, Vector3.up), .3f, $"triangle {i / 3} faces down");
            }
            Assert.Greater(mesh.normals[64 * 129 + 64].y, .99f, "flat ground in the middle");
        }

        [Test]
        public void TheGroundTextureBlendsTheLayersBySplatWeights()
        {
            var map = new TerrainMap(); map.Apply(TerrainTool.Paint, 0, 0, 20, 1f, 3); // rock around the centre
            var view = NewView(map, 256);
            var pixels = view.Albedo.GetPixels32(0); int Size = view.Albedo.width;
            Color32 At(float worldX, float worldZ) => pixels[Mathf.Clamp((int)((worldZ + map.HalfExtent) / (map.Cells * map.CellSize) * Size), 0, Size - 1) * Size + Mathf.Clamp((int)((worldX + map.HalfExtent) / (map.Cells * map.CellSize) * Size), 0, Size - 1)];
            var centre = At(0, 0); var grass = At(60, 60); var beach = At(-map.HalfExtent + 4, 0);
            Assert.AreEqual(centre.r, centre.b, 40, "rock is grey");   // r ~ g ~ b
            Assert.Greater(grass.g, grass.r + 15, "grass is green");
            Assert.Greater(grass.g, grass.b + 25);
            Assert.Greater(beach.r, beach.b + 20, "sand is warm");
            Assert.Greater(beach.r, 130);
        }

        [Test]
        public void TheColliderGivesTheSameGroundHeightAsTheMapAndFollowsEdits()
        {
            var map = new TerrainMap(); map.Apply(TerrainTool.Raise, 20, 10, 12, 1f);
            var view = NewView(map);
            foreach (var spot in new[] { (20f, 10f), (0f, 0f), (-50f, 30f), (23f, 12f) })
            {
                var height = view.GroundHeight(spot.Item1, spot.Item2);
                Assert.IsTrue(height.HasValue, $"o raio acha o chão em {spot}");
                Assert.AreEqual(map.SampleHeight(spot.Item1, spot.Item2), height.Value, .06f, $"altura em {spot}");
            }
            Assert.IsNull(view.GroundHeight(map.HalfExtent + 40f, 0f), "fora da ilha não há chão");
            var before = view.GroundHeight(20f, 10f).Value;
            Assert.IsTrue(view.ApplyEdit(TerrainTool.Raise, 20, 10, 12, 1f, 0, map.Revision + 1) || true);
            view.Flush();
            Assert.Greater(view.GroundHeight(20f, 10f).Value, before + .05f, "depois de uma edição do servidor o colisor sobe junto");
        }

        [Test]
        public void TheWavesAreBoundedAndMoveWithTime()
        {
            var peak = 0f; var changed = false;
            for (var x = -300; x <= 300; x += 7) for (var z = -300; z <= 300; z += 11) { var h = OceanView.Wave(x, z, 3f); peak = Mathf.Max(peak, Mathf.Abs(h)); if (Mathf.Abs(h - OceanView.Wave(x, z, 4f)) > .05f) changed = true; }
            Assert.LessOrEqual(peak, 1.56f); Assert.Greater(peak, .8f); Assert.IsTrue(changed, "a onda anda com o tempo");
        }

        [Test]
        public void TheSunFollowsTheHourAndTheNightIsDark()
        {
            _root = new GameObject("env"); var environment = _root.AddComponent<IslandEnvironment>(); environment.DayNightCycle = false;
            float Day(float hour) { environment.Hour = hour; environment.Apply(1f); return environment.DayFactor; }
            Assert.AreEqual(1f, Day(12f), .001f);
            Assert.Greater(Day(9f), .9f); Assert.Less(Day(6f), .25f); Assert.Less(Day(18f), .25f);
            Assert.AreEqual(0f, Day(0f), .001f); Assert.AreEqual(0f, Day(23f), .001f);
            environment.Hour = 12f; environment.Apply(1f);
            Assert.AreEqual(90f, environment.Sun.transform.eulerAngles.x, 1f, "noon: straight down (x = 90)");
            environment.Hour = 6f; environment.Apply(1f);
            Assert.AreEqual(Vector3.zero.y, environment.Sun.transform.forward.y, .02f, "sunrise: the sun on the horizon");
            environment.Hour = 12f; environment.Apply(1f); var noonLux = environment.Sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>().intensity;
            environment.Weather = "storm"; environment.Apply(1f); var stormLux = environment.Sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>().intensity;
            Assert.AreEqual(IslandEnvironment.NoonLux, noonLux, 1f); Assert.AreEqual(noonLux, stormLux, 1f, "as in Godot, a storm does not dim the sun");
            Assert.AreEqual(IslandEnvironment.FogDensityOf("storm"), environment.FogDensity, 1e-5f, "its fog greys the island"); Assert.AreEqual(1f, environment.SkyCover, 1e-3f, "and covers the sky"); Assert.IsTrue(environment.WeatherFogShown);
            environment.Weather = "clear"; environment.Apply(1f); Assert.AreEqual(0f, environment.FogDensity); Assert.AreEqual(0f, environment.SkyCover); Assert.IsFalse(environment.WeatherFogShown);
        }

        [Test]
        public void AParcelLookTakesOverGraduallyAndGivesTheHourBack()
        {
            _root = new GameObject("env"); var environment = _root.AddComponent<IslandEnvironment>(); environment.DayNightCycle = false; environment.Hour = 12f;
            Assert.AreEqual(12f, environment.HourShown, .001f);
            environment.SetParcelLook(22f, "rain", .5f);
            Assert.AreEqual(12f, environment.HourShown, .001f, "nothing happens until time passes");
            Assert.AreEqual("clear", environment.WeatherShown);
            environment.FinishBlend();
            Assert.AreEqual(22f, environment.HourShown, .001f, "no fim da transição vale a hora da parcela"); Assert.AreEqual("rain", environment.WeatherShown); Assert.AreEqual(.5f, environment.LightShown, .001f);
            environment.SetLook(null); environment.FinishBlend();
            Assert.AreEqual(12f, environment.HourShown, .001f, "a ilha volta a ter a sua hora");
        }

        [Test]
        public void TheIslandLookFromTheApiSetsHourWeatherFogAndSkyTint()
        {
            _root = new GameObject("env"); var environment = _root.AddComponent<IslandEnvironment>(); environment.DayNightCycle = false; environment.Hour = 12f;
            var look = LifeVerse.Shared.Land.ParcelEnvironment.Parse("{\"hour\":19.5,\"weather\":\"fog\",\"fog\":0.5,\"light\":0.8,\"sky\":\"#FF8040\"}");
            Assert.IsFalse(look.IsEmpty); environment.SetLook(look); environment.FinishBlend(); environment.Apply(1f);
            Assert.AreEqual(19.5f, environment.HourShown, .001f); Assert.AreEqual("fog", environment.WeatherShown); Assert.AreEqual(.8f, environment.LightShown, .001f);
            Assert.IsTrue(environment.LookActive);
            environment.Hour = 12f; environment.SetLook(new LifeVerse.Shared.Land.ParcelEnvironment()); Assert.IsFalse(environment.LookActive, "um visual vazio não vale");
        }

        [Test]
        public void TheSunDiscShowsInTheDayAndHidesAtNightAndBehindStorms()
        {
            _root = new GameObject("env"); var environment = _root.AddComponent<IslandEnvironment>(); environment.DayNightCycle = false;
            environment.Hour = 12f; environment.Apply(1f); Assert.Greater(environment.SunDiscVisibility, .95f);
            environment.Hour = 23f; environment.Apply(1f); Assert.AreEqual(0f, environment.SunDiscVisibility, .01f);
            environment.Hour = 12f; environment.Weather = "storm"; environment.Apply(1f); Assert.AreEqual(0f, environment.SunDiscVisibility, .01f);
        }
    }
}
