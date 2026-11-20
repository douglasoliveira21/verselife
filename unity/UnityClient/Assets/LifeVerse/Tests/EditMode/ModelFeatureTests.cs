using System;
using System.Linq;
using System.Text.Json.Nodes;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class ModelFeatureTests
    {
        private static Mesh Sphere(int rings, int segments)
        {
            var vertices = new Vector3[(rings + 1) * (segments + 1)]; var uvs = new Vector2[vertices.Length]; var normals = new Vector3[vertices.Length]; var tris = new System.Collections.Generic.List<int>();
            for (var r = 0; r <= rings; r++) for (var s = 0; s <= segments; s++)
                { var a = Mathf.PI * r / rings; var b = 2f * Mathf.PI * s / segments; var n = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)); var i = r * (segments + 1) + s; vertices[i] = n; normals[i] = n; uvs[i] = new Vector2((float)s / segments, (float)r / rings); }
            for (var r = 0; r < rings; r++) for (var s = 0; s < segments; s++) { var i = r * (segments + 1) + s; tris.AddRange(new[] { i, i + 1, i + segments + 1, i + 1, i + segments + 2, i + segments + 1 }); }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uvs; mesh.triangles = tris.ToArray(); mesh.RecalculateBounds(); return mesh;
        }

        [Test]
        public void FrameStatsAverageTheSlowestOnePercent()
        {
            var frames = System.Linq.Enumerable.Repeat(10.0, 199).Concat(new[] { 50.0 }).ToList(); Assert.AreEqual(30.0, FrameStats.WorstOnePercent(frames), 1e-9);   // 200 frames: the slowest 2 average (50 + 10) / 2
            Assert.AreEqual(0, FrameStats.WorstOnePercent(new double[0])); Assert.AreEqual(10.0, FrameStats.WorstOnePercent(new[] { 10.0 })); Assert.AreEqual(0, FrameStats.WorstOnePercent(null));
        }

        [Test]
        public void SimplifierMakesALighterCopyThatStillFits()
        {
            var full = Sphere(80, 120); var before = MeshSimplifier.TriangleCount(full); Assert.Greater(before, MeshSimplifier.MinTriangles);
            var low = MeshSimplifier.Simplify(full, .25f); Assert.IsNotNull(low); var after = MeshSimplifier.TriangleCount(low);
            Assert.Less(after, before * .5f); Assert.Greater(after, 50); Assert.AreEqual(low.vertexCount, low.normals.Length); Assert.AreEqual(low.vertexCount, low.uv.Length);
            Assert.IsTrue(low.triangles.All(i => i >= 0 && i < low.vertexCount)); Assert.IsTrue(low.vertices.All(v => !float.IsNaN(v.x)));
            Assert.AreEqual(full.bounds.size.x, low.bounds.size.x, full.bounds.size.x * .1f); Assert.AreEqual(1, low.subMeshCount);
            var t = low.triangles; for (var i = 0; i < t.Length; i += 3) Assert.IsTrue(t[i] != t[i + 1] && t[i + 1] != t[i + 2] && t[i] != t[i + 2], "no collapsed triangle");
        }

        [Test]
        public void SimplifierLeavesSmallOrUnreadableMeshesAlone()
        {
            Assert.IsNull(MeshSimplifier.Simplify(Sphere(8, 12), .25f)); Assert.IsNull(MeshSimplifier.Simplify(null, .25f)); Assert.IsNull(MeshSimplifier.Simplify(Sphere(80, 120), 1f));
        }

        [Test]
        public void SimplifierKeepsEverySubMesh()
        {
            var mesh = Sphere(80, 120); var all = mesh.triangles; var half = all.Length / 6 * 3; mesh.subMeshCount = 2; mesh.SetTriangles(all.Take(half).ToArray(), 0); mesh.SetTriangles(all.Skip(half).ToArray(), 1);
            var low = MeshSimplifier.Simplify(mesh, .25f); Assert.IsNotNull(low); Assert.AreEqual(2, low.subMeshCount); Assert.Greater(low.GetIndexCount(0), 0u); Assert.Greater(low.GetIndexCount(1), 0u);
        }

        [Test]
        public void AnimationPositionRunsFromTheServerClock()
        {
            Assert.AreEqual(1.5, ModelAnimations.PositionAt(0, 100, 101.5, 1, true, true, 2), 1e-9); Assert.AreEqual(.5, ModelAnimations.PositionAt(0, 100, 102.5, 1, true, true, 2), 1e-9);
            Assert.AreEqual(2.0, ModelAnimations.PositionAt(0, 100, 150, 1, true, false, 2), 1e-9); Assert.AreEqual(.7, ModelAnimations.PositionAt(.7, 100, 150, 1, false, true, 2), 1e-9); Assert.AreEqual(1.0, ModelAnimations.PositionAt(0, 100, 100.5, 2, true, true, 2), 1e-9);
        }

        [Test]
        public void AnimationSettingsAreReadAndDoNotChangeTheShape()
        {
            var json = "{\"color\":\"#fff\",\"animation\":{\"clip\":\"Spin\",\"loop\":true,\"playing\":true}}";
            Assert.AreEqual("Spin", ModelAnimations.Settings(json)["clip"].GetValue<string>()); Assert.IsNull(ModelAnimations.Settings("{}")); Assert.IsNull(ModelAnimations.Settings("oops"));
            var plain = ModelAnimations.WithoutAnimation(json); Assert.IsFalse(plain.Contains("animation")); Assert.IsTrue(plain.Contains("color")); Assert.AreEqual("{}", ModelAnimations.WithoutAnimation("{}"));
            Assert.AreEqual("", ModelAnimations.Apply(null, new JsonObject(), 0));
        }

        [Test]
        public void SwayLeansBackFromTheDirectionOfTravel()
        {
            var forward = AvatarAttachments.SwayTarget(new Vector3(0, 0, 3), 1f); Assert.Less(forward.x, 0f); Assert.AreEqual(0f, forward.y); Assert.AreEqual(0f, AvatarAttachments.SwayTarget(new Vector3(0, 0, 3), 0f).magnitude);
            var right = AvatarAttachments.SwayTarget(new Vector3(3, 0, 0), 1f); Assert.Greater(right.z, 0f); Assert.LessOrEqual(AvatarAttachments.SwayTarget(new Vector3(0, 0, 500), 1f).x, 0f);
            Assert.GreaterOrEqual(AvatarAttachments.SwayTarget(new Vector3(0, 0, 500), 1f).x, -.5f); Assert.Greater(Mathf.Abs(AvatarAttachments.SwayTarget(new Vector3(0, 0, 3), 1f).x), Mathf.Abs(AvatarAttachments.SwayTarget(new Vector3(0, 0, 3), .3f).x));
        }

        [Test]
        public void SwayAndMoveFieldsAreChecked()
        {
            Assert.AreEqual(.5f, AttachmentsWindow.ParseSway("0,5")); Assert.AreEqual(1f, AttachmentsWindow.ParseSway("1")); Assert.IsNull(AttachmentsWindow.ParseSway("1.5")); Assert.IsNull(AttachmentsWindow.ParseSway("-1")); Assert.IsNull(AttachmentsWindow.ParseSway("x"));
            StringAssert.Contains("entre 0 e 1", AttachmentsWindow.Explain("{\"error\":\"bad_sway\"}")); Assert.IsTrue(AttachmentsWindow.PointNames(new[] { "humanoid" }).Contains("HUD no centro") || AttachmentsWindow.PointNames(new[] { "humanoid" }).Any(n => n.StartsWith("HUD")));
        }

        [Test]
        public void HudPointsAnchorToTheScreenCorners()
        {
            var screen = new Vector2(1280, 720); var tl = HudAttachments.Anchor("hud_top_left", screen, Vector2.zero); var br = HudAttachments.Anchor("hud_bottom_right", screen, Vector2.zero); var centre = HudAttachments.Anchor("hud_center", screen, Vector2.zero);
            Assert.AreEqual(new Vector2(HudAttachments.Margin, HudAttachments.Margin), tl); Assert.AreEqual(new Vector2(1280 - HudAttachments.Cell - HudAttachments.Margin, 720 - HudAttachments.Cell - HudAttachments.Margin), br); Assert.AreEqual(new Vector2(640 - HudAttachments.Cell / 2f, 360 - HudAttachments.Cell / 2f), centre);
            var moved = HudAttachments.Anchor("hud_top_left", screen, new Vector2(1f, 1f)); Assert.AreEqual(tl.x + HudAttachments.PixelsPerMetre, moved.x); Assert.AreEqual(tl.y - HudAttachments.PixelsPerMetre, moved.y);
        }
    }
}
