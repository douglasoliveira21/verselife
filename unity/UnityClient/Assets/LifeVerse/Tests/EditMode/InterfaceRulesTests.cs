using System;
using System.Collections.Generic;
using LifeVerse.Shared.Camera;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class InterfaceRulesTests
    {
        private static readonly string[] Defaults = { "chat", "people", "social", "map", "help" };

        [Test]
        public void TheToolbarKeepsTheSavedOrderAndAddsTheRest()
        {
            CollectionAssert.AreEqual(new[] { "map", "chat", "people", "social", "help" }, ToolbarLayout.Order(Defaults, new[] { "map", "sumiu", "map" }));
            CollectionAssert.AreEqual(Defaults, ToolbarLayout.Order(Defaults, null));
            CollectionAssert.AreEqual(new[] { "people", "chat", "social", "map", "help" }, ToolbarLayout.Move(Defaults, "people", -1));
            CollectionAssert.AreEqual(Defaults, ToolbarLayout.Move(Defaults, "chat", -1));
            CollectionAssert.AreEqual(Defaults, ToolbarLayout.Move(Defaults, "nada", 1));
            Assert.IsTrue(ToolbarLayout.CanHide(Defaults, new HashSet<string> { "chat" }, "people"));
            Assert.IsFalse(ToolbarLayout.CanHide(Defaults, new HashSet<string> { "chat", "people", "social", "map" }, "help"));
            CollectionAssert.AreEqual(new[] { "a", "b" }, ToolbarLayout.Split(" a, b,,a "));
        }

        [Test]
        public void TheIslandHistoryGoesBackAndForward()
        {
            var h = new IslandHistory(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
            Assert.IsFalse(h.CanBack); h.Visit(a); h.Visit(b); h.Visit(c); h.Visit(c);
            Assert.AreEqual(3, h.Islands.Count); Assert.AreEqual(b, h.PeekBack); Assert.AreEqual(Guid.Empty, h.PeekForward);
            h.Visit(b); Assert.AreEqual(b, h.Current); Assert.AreEqual(c, h.PeekForward); Assert.AreEqual(a, h.PeekBack);
            h.Visit(c); Assert.AreEqual(c, h.Current); h.Visit(b); h.Visit(a); Assert.IsFalse(h.CanBack); Assert.IsTrue(h.CanForward);
            var d = Guid.NewGuid(); h.Visit(d); Assert.AreEqual(2, h.Islands.Count); Assert.IsFalse(h.CanForward); Assert.AreEqual(a, h.PeekBack);
            for (var i = 0; i < 30; i++) h.Visit(Guid.NewGuid()); Assert.AreEqual(IslandHistory.Max, h.Islands.Count);
            h.Visit(Guid.Empty); Assert.AreEqual(IslandHistory.Max, h.Islands.Count);
        }

        [Test]
        public void PhotosHaveADatedName()
        {
            var when = new DateTime(2026, 11, 26, 7, 30, 12);
            Assert.AreEqual("LifeVerse 2026-11-26 07-30-12.png", WorldPhotos.NameFor(when, "png"));
            Assert.AreEqual("LifeVerse 2026-11-26 07-30-12.jpg", WorldPhotos.NameFor(when, "jpg"));
            StringAssert.EndsWith("LifeVerse", WorldPhotos.Folder);
            var picture = new Texture2D(4, 4); try { Assert.AreEqual(0x89, WorldPhotos.Encode(picture, "png")[0]); Assert.AreEqual(0xFF, WorldPhotos.Encode(picture, "jpg")[0]); } finally { UnityEngine.Object.DestroyImmediate(picture); }
        }

        [Test]
        public void TheNearestAvatarOnTheRayIsPicked()
        {
            var ray = new Ray(new Vector3(0, 1, 0), Vector3.forward);
            var people = new List<(int, Vector3)> { (1, new Vector3(0, 0, 10)), (2, new Vector3(0, 0, 5)), (3, new Vector3(3, 0, 3)), (4, new Vector3(0, 0, -4)) };
            Assert.AreEqual(2, AvatarPick.Pick(ray, people)); Assert.AreEqual(-1, AvatarPick.Pick(new Ray(new Vector3(0, 1, 0), Vector3.left), people));
            Assert.AreEqual(-1, AvatarPick.Pick(ray, new List<(int, Vector3)> { (7, new Vector3(0, 0, 90)) }));
            var (along, gap) = AvatarPick.Closest(ray, new Vector3(1, 0, 4), new Vector3(1, 2, 4)); Assert.AreEqual(4f, along, .01f); Assert.AreEqual(1f, gap, .01f);
        }

        [Test]
        public void TheFreeCameraFocusesTurnsAndSlides()
        {
            var host = new GameObject("free camera"); var cam = new GameObject("cam").AddComponent<Camera>();
            try
            {
                var follow = host.AddComponent<FollowCamera>(); follow.Camera = cam; cam.transform.position = new Vector3(0, 2, -10);
                follow.PickPoint = r => new Vector3(0, 2, 0); follow.FocusAt(new Ray(cam.transform.position, Vector3.forward));
                Assert.AreEqual(new Vector3(0, 2, 0), follow.ZoomTarget.Value); Assert.AreEqual(10f, follow.ZoomDistance, .01f); Assert.AreEqual(0f, follow.Yaw, .01f); Assert.AreEqual(0f, follow.Pitch, .01f);
                follow.Drag(CameraGesture.OrbitDolly, 100f, 50f); Assert.AreEqual(100f * follow.Sensitivity, follow.Yaw, .01f); Assert.Less(follow.ZoomDistance, 10f);
                var pitch = follow.Pitch; follow.Drag(CameraGesture.Orbit, 0f, -40f); Assert.Greater(follow.Pitch, pitch);
                follow.Yaw = 0f; follow.Pitch = 0f; follow.Drag(CameraGesture.Pan, 100f, 0f); Assert.Less(follow.ZoomTarget.Value.x, 0f);
                follow.ZoomTarget = null; follow.Drag(CameraGesture.Orbit, 50f, 0f); Assert.AreEqual(0f, follow.Yaw, .01f);
                Assert.AreEqual(CameraGesture.None, CameraInputRules.Classify(false, false, false, follow.FreeOptions)); Assert.AreNotEqual(CameraGesture.None, CameraInputRules.Classify(false, true, false, follow.FreeOptions));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(cam.gameObject); }
        }
    }
}
