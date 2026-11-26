using LifeVerse.Unity.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The picture as the Godot client makes it: tonemapper, sun, the view from under the water and the grain of the ground.</summary>
    public sealed class LookTests
    {
        [Test]
        public void TheLastStepOfThePictureIsGodotsFilmicCurve()
        {
            Assert.AreEqual(0f, GodotTonemap.Filmic(0f), 1e-5f); Assert.AreEqual(1f, GodotTonemap.Filmic(GodotTonemap.White), 1e-5f); Assert.AreEqual(1f, GodotTonemap.Filmic(40f), 1e-5f);
            var before = 0f; for (var x = .05f; x < GodotTonemap.White; x += .05f) { var y = GodotTonemap.Filmic(x); Assert.Greater(y, before, "it only grows"); before = y; }
            var grey = new Color(.18f, .18f, .18f); var plain = GodotTonemap.Map(grey); var adjusted = GodotTonemap.Map(new Color(.6f, .2f, .1f), 1.06f, 1.1f);
            Assert.AreEqual(plain.r, plain.g, 1e-5f); Assert.Greater(adjusted.r - adjusted.g, GodotTonemap.Map(new Color(.6f, .2f, .1f)).r - GodotTonemap.Map(new Color(.6f, .2f, .1f)).g, "saturation pulls the colours apart");
            foreach (var x in new[] { 0f, .18f, 1f, 6f }) Assert.AreEqual(x, GodotTonemap.FromLogC(.244161f * Mathf.Log10(5.555556f * x + .047996f) + .386036f), 1e-3f, "the way back from the encoding HDRP uses: " + x);
        }

        [Test]
        public void UnderTheWaterHalfOfThePictureIsTheColourOfTheSea()
        {
            var white = GodotTonemap.Submerged(Color.white, 1f).gamma; Assert.AreEqual(.51f, white.r, .01f); Assert.AreEqual(.63f, white.g, .01f); Assert.AreEqual(.70f, white.b, .01f);
            Assert.Less(GodotTonemap.Submerged(Color.white, 0f).gamma.g, white.g, "darker at night");
            Assert.AreNotSame(GodotTonemap.Table(32, 1f, 1f), GodotTonemap.Table(32, 1f, 1f, 1f)); Assert.AreSame(GodotTonemap.Table(32, 1f, 1f, 1f), GodotTonemap.Table(32, 1f, 1f, .98f), "one table for each tenth of the day");
        }

        [Test]
        public void TheSunIsWhereTheGodotSunIsAndTheUnderwaterTableIsThereFromTheStart()
        {
            // the Godot sun at 14 h: turned 28 degrees, 60 degrees past noon, in game space; the scene flips z
            var godot = Quaternion.identity * Vector3.zero; var angle = 120f * Mathf.Deg2Rad; var pitched = new Vector3(0f, Mathf.Sin(-angle), -Mathf.Cos(-angle)); var yaw = -28f * Mathf.Deg2Rad;
            godot = new Vector3(pitched.x * Mathf.Cos(yaw) + pitched.z * Mathf.Sin(yaw), pitched.y, -pitched.x * Mathf.Sin(yaw) + pitched.z * Mathf.Cos(yaw));
            var scene = IslandEnvironment.SunDirection(14f); Assert.Less(Vector3.Distance(GameSpace.ToUnity(godot), scene), 1e-4f, godot + " " + scene);
            Assert.Less(Vector3.Distance(IslandEnvironment.SunDirection(12f), Vector3.down), 1e-4f);
            var host = new GameObject("look");
            try
            {
                var environment = host.AddComponent<IslandEnvironment>(); environment.Hour = 12f; environment.Apply(1f);
                var tables = 0; foreach (var volume in host.GetComponentsInChildren<UnityEngine.Rendering.Volume>()) if (volume.sharedProfile.TryGet<Tonemapping>(out var tonemapping)) { Assert.IsNotNull(tonemapping.lutTexture.value, volume.name); tables++; }
                Assert.AreEqual(2, tables, "the picture above the water and the picture under it");
                Assert.AreEqual((1f, 1f), IslandEnvironment.AdjustmentOf(0)); Assert.AreEqual((1.06f, 1.1f), IslandEnvironment.AdjustmentOf(2));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void TheGrainOfTheGroundLeavesTheAverageAlone()
        {
            Assert.AreEqual(128, TerrainView.GrainRed(1f), 1); Assert.Greater(TerrainView.GrainRed(1.2f), TerrainView.GrainRed(1.05f)); Assert.Less(TerrainView.GrainRed(.8f), 128);
            // what HDRP makes of the red: it squares the distance from one half
            var off = TerrainView.GrainRed(.8f) / 255f * 2f - 1f; Assert.AreEqual(.12f, off * off, .01f);
        }
    }
}
