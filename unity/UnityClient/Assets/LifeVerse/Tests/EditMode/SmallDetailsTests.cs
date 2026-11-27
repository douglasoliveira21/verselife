using System;
using System.Collections.Generic;
using System.Text.Json;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The smaller details brought from the Godot client: graphs, terrain picture, search pictures, dragging, the content rights, the statement icons, animation variations and the detached camera.</summary>
    public sealed class SmallDetailsTests
    {
        [Test]
        public void GraphsPutTheHighestValueAtTheTop()
        {
            var points = Sparkline.Points(new List<float> { 10f, 30f, 20f }, 200f, 60f);
            Assert.AreEqual(3, points.Count); Assert.Less(points[1].y, points[0].y, "30 is higher on the graph than 10"); Assert.Less(points[0].x, points[2].x);
            Assert.AreEqual(0, Sparkline.Points(new List<float>(), 200f, 60f).Count);
        }

        [Test]
        public void TheTerrainPictureFollowsTheHeights()
        {
            using var doc = JsonDocument.Parse("{\"size\":2,\"seaLevel\":0,\"heights\":[-5,-5,5,30]}");
            var picture = IslandManagementWindow.TerrainPicture(doc.RootElement); Assert.IsNotNull(picture); Assert.AreEqual(2, picture.width);
            var top = picture.GetPixel(0, 1); var land = picture.GetPixel(0, 0); Assert.Greater(top.b, top.g, "the first row (north, at the top) is water"); Assert.Greater(land.g, land.b, "the second row starts with grass");
            using var bad = JsonDocument.Parse("{\"size\":3,\"heights\":[1,2]}"); Assert.IsNull(IslandManagementWindow.TerrainPicture(bad.RootElement));
            UnityEngine.Object.DestroyImmediate(picture);
        }

        [Test]
        public void SearchPicturesComeOnlyFromThisApi()
        {
            var asset = Guid.NewGuid(); var island = Guid.NewGuid();
            Assert.AreEqual("/api/v1/worlds/islands/" + island + "/assets/" + asset + "/file", GlobalSearchWindow.ThumbPath("asset:" + asset, island));
            Assert.AreEqual("/api/v1/assets/" + asset + "/download", GlobalSearchWindow.ThumbPath("asset:" + asset, Guid.Empty));
            Assert.AreEqual("/api/v1/users/x/profile/photo", GlobalSearchWindow.ThumbPath("/api/v1/users/x/profile/photo", Guid.Empty));
            var club = Guid.NewGuid();
            Assert.AreEqual("/api/v1/groups/" + club + "/image", GlobalSearchWindow.ThumbPath("asset://" + asset, Guid.Empty, club), "a group's picture is asked by the group");
            Assert.IsNull(GlobalSearchWindow.ThumbPath("asset://" + asset, Guid.Empty), "without the group there is nowhere to ask");
            Assert.IsNull(GlobalSearchWindow.ThumbPath("https://elsewhere.example/a.png", Guid.Empty), "other sites are never fetched");
            Assert.IsNull(GlobalSearchWindow.ThumbPath("/api/v1/../secret", Guid.Empty));
            Assert.AreEqual("calendar", GlobalSearchWindow.IconOfKind("event")); Assert.AreEqual("globe", GlobalSearchWindow.IconOfKind("island"));
        }

        [Test]
        public void DraggingPlacesAndFindsTheFolder()
        {
            CollectionAssert.AreEqual(new[] { "map", "voice", "build" }, ToolbarLayout.Place(new[] { "voice", "build", "map" }, "map", "voice"));
            CollectionAssert.AreEqual(new[] { "build", "map", "voice" }, ToolbarLayout.Place(new[] { "voice", "build", "map" }, "voice", "map"));
            CollectionAssert.AreEqual(new[] { "voice", "build" }, ToolbarLayout.Place(new[] { "voice", "build" }, "voice", "nope"));
            var folder = Guid.NewGuid(); var row = new VisualElement { userData = "drop-folder:" + folder }; var inside = new Label("x"); row.Add(inside);
            Assert.AreEqual(folder.ToString(), InventoryManagerWindow.FolderUnder(inside)); Assert.AreEqual("", InventoryManagerWindow.FolderUnder(new VisualElement { userData = "drop-folder:" })); Assert.IsNull(InventoryManagerWindow.FolderUnder(new Label()));
        }

        [Test]
        public void ContentRightsStatementAndDates()
        {
            StringAssert.Contains("pode ser modificado", BuildWindow.ContentRightsText(15, null)); StringAssert.Contains("só de leitura", BuildWindow.ContentRightsText(1, null));
            StringAssert.Contains("copiar", BuildWindow.ContentRightsText(15, 1));
            Assert.AreEqual("coins", GroupProfileWindow.LedgerIcon("contribution", 10)); Assert.AreEqual("minus", GroupProfileWindow.LedgerIcon("other", -5));
            var when = GroupProfileWindow.ParseWhen("25/12/2026 20:30"); Assert.IsNotNull(when); Assert.AreEqual(20, when.Value.Hour); Assert.AreEqual(12, when.Value.Month); Assert.IsNull(GroupProfileWindow.ParseWhen("amanhã"));
        }

        [Test]
        public void TheLibraryVariationsChangeThePose()
        {
            Assert.AreEqual("Walk", AvatarView.StateOf(NetworkMotionState.Walk, AvatarView.Gesture.None)); Assert.AreEqual("Greet", AvatarView.StateOf(NetworkMotionState.Idle, AvatarView.Gesture.Greet)); Assert.IsNull(AvatarView.StateOf(NetworkMotionState.Fly, AvatarView.Gesture.None));
            var walk = AvatarView.Evaluate(NetworkMotionState.Walk, AvatarView.Gesture.None, 1f, 40f, 3f);
            var tired = AvatarView.Vary(walk, "Walk@tired", 1f); Assert.Less(Mathf.Abs(tired.LegL), Mathf.Abs(walk.LegL)); Assert.Greater(tired.Pitch, walk.Pitch, "a tired walk leans forward");
            var spin = AvatarView.Vary(new PoseValues(), "Dance@spin", 3f); Assert.Greater(spin.Yaw, 0f); Assert.Greater(spin.ArmSideR, 100f, "arms out while spinning");
            var same = AvatarView.Vary(walk, "Walk@nope", 1f); Assert.AreEqual(walk.LegL, same.LegL);
        }

        [Test]
        public void TheDetachedCameraStaysInRangeAndDoubleClicksAreQuick()
        {
            var go = new GameObject("camera test"); var follow = go.AddComponent<FollowCamera>();
            try
            {
                follow.FreeOptions = follow.FreeOptions with { AllowDetached = true, DetachedRange = 10f, DetachedSpeed = 5f };
                follow.SetDetached(true); Assert.IsTrue(follow.Detached);
                Vector3 at = default; for (var i = 0; i < 100; i++) at = follow.MoveDetached(new Vector3(0f, 0f, 1f), true, .1f);
                Assert.LessOrEqual(Vector3.Distance(at, follow.DetachedAnchor), 10.01f, "never past the range");
                follow.SetDetached(false); Assert.IsFalse(follow.Detached);
                follow.FreeOptions = follow.FreeOptions with { AllowDetached = false }; follow.SetDetached(true); Assert.IsFalse(follow.Detached, "only when allowed");
                Assert.IsFalse(follow.IsDoubleClick(1f, new Vector2(10, 10))); Assert.IsTrue(follow.IsDoubleClick(1.2f, new Vector2(12, 11)));
                Assert.IsFalse(follow.IsDoubleClick(1.3f, new Vector2(12, 11)), "a third click starts over"); Assert.IsFalse(follow.IsDoubleClick(2f, new Vector2(12, 11)));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
