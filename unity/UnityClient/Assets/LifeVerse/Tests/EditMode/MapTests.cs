using System;
using System.Linq;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class MapTests
    {
        private const string Atlas = "{\"items\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Praia\",\"description\":\"Sol\",\"category\":\"social\",\"contentRating\":\"general\",\"isPublic\":true,\"cellX\":0,\"cellY\":0,\"x\":0,\"y\":0,\"size\":512,\"spawnX\":10,\"spawnZ\":-20,\"maxPlayers\":40,\"online\":true,\"visitors\":3,\"access\":\"open\",\"favorite\":true,\"owner\":{\"id\":\"22222222-2222-2222-2222-222222222222\",\"displayName\":\"Ana\"}},{\"id\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Morro\",\"x\":768,\"y\":0,\"size\":512,\"access\":\"private\"},{\"nada\":1}],\"total\":2,\"cellMeters\":768,\"truncated\":false}";

        [Test]
        public void TheAtlasIsReadAndBrokenItemsAreSkipped()
        {
            var page = MapApi.ParseAtlas(Atlas); Assert.AreEqual(2, page.Items.Count); Assert.AreEqual(768, page.CellMeters); var beach = page.Items[0];
            Assert.AreEqual("Praia", beach.Name); Assert.IsTrue(beach.CanEnter); Assert.IsTrue(beach.Favorite); Assert.AreEqual(3, beach.Visitors); Assert.AreEqual("Ana", beach.OwnerName); Assert.AreEqual(-20f, beach.SpawnZ); Assert.AreEqual("Aberta", beach.AccessWords);
            Assert.IsFalse(page.Items[1].CanEnter); Assert.AreEqual("Privada", page.Items[1].AccessWords); Assert.IsNull(page.Items[1].Visitors);
            Assert.AreEqual(0, MapApi.ParseAtlas("oops").Items.Count); Assert.AreEqual(0, MapApi.ParseAtlas("[]").Items.Count); Assert.AreEqual(0, MapApi.ParseAtlas("{}").Items.Count);
        }

        [Test]
        public void TheAtlasQueryIsBuiltAndEscaped()
        {
            Assert.AreEqual("/api/v1/map/atlas?limit=300", MapApi.AtlasPath()); StringAssert.Contains("q=praia%20azul", MapApi.AtlasPath(" praia azul ")); StringAssert.Contains("limit=500", MapApi.AtlasPath(limit: 9999)); StringAssert.Contains("ratings=a%2Cb", MapApi.AtlasPath(ratings: new[] { "a", "b" }));
        }

        [Test]
        public void SpotsConvertBetweenTheIslandAndTheAtlas()
        {
            var island = MapApi.ParseAtlas(Atlas).Items[1]; Assert.IsTrue(MapCoordinates.Contains(island, 768 + 100, 50)); Assert.IsFalse(MapCoordinates.Contains(island, 0, 0));
            var (x, y) = MapCoordinates.ToGlobal(island, 10, -5); Assert.AreEqual(778, x); Assert.AreEqual(-5, y);
            var inside = MapCoordinates.ToLocal(island, 778, -5); Assert.IsTrue(inside.Inside); Assert.AreEqual(10, inside.LocalX, 1e-9); var outside = MapCoordinates.ToLocal(island, 5000, 0); Assert.IsFalse(outside.Inside); Assert.AreEqual(256, outside.LocalX, 1e-9);
            Assert.AreEqual("Morro", MapCoordinates.IslandAt(MapApi.ParseAtlas(Atlas).Items, 800, 0).Name); Assert.IsNull(MapCoordinates.IslandAt(MapApi.ParseAtlas(Atlas).Items, 5000, 5000));
            var toward = MapCoordinates.Toward(0, 0, 0, -100); Assert.AreEqual(100, toward.Meters, 1e-9); Assert.AreEqual(0, toward.Bearing, 1e-9); Assert.AreEqual(90, MapCoordinates.Toward(0, 0, 100, 0).Bearing, 1e-9);
        }

        [Test]
        public void TypedSpotsAcceptCommasAndRejectJunk()
        {
            Assert.IsTrue(MapCoordinates.TryParsePair("12 -30", out var a, out var b)); Assert.AreEqual(12, a); Assert.AreEqual(-30, b); Assert.IsTrue(MapCoordinates.TryParsePair("12.5; 3.5", out a, out b)); Assert.AreEqual(12.5, a); Assert.AreEqual(3.5, b);
            foreach (var bad in new[] { "", "12", "a b", "1 2 3", "NaN 1", new string('1', 50) }) Assert.IsFalse(MapCoordinates.TryParsePair(bad, out _, out _), bad);
        }

        [Test]
        public void TheCanvasConvertsAndZoomsAroundThePointer()
        {
            var canvas = new MapCanvas(); canvas.Centre = new Vector2(100, 200); canvas.Scale = .1f;
            var screen = canvas.ToScreen(100, 200); var back = canvas.ToAtlas(screen); Assert.AreEqual(100, back.X, 1e-3); Assert.AreEqual(200, back.Y, 1e-3);
            var probe = new Vector2(30, 40); var before = canvas.ToAtlas(probe); canvas.ZoomAt(probe, 2f); var after = canvas.ToAtlas(probe); Assert.AreEqual(.2f, canvas.Scale, 1e-6f); Assert.AreEqual(before.X, after.X, 1e-2); Assert.AreEqual(before.Y, after.Y, 1e-2);
            for (var i = 0; i < 40; i++) canvas.ZoomAt(probe, 2f); Assert.AreEqual(MapCanvas.MaxScale, canvas.Scale); for (var i = 0; i < 80; i++) canvas.ZoomAt(probe, .5f); Assert.AreEqual(MapCanvas.MinScale, canvas.Scale);
        }

        [Test]
        public void IslandsAreColoredByAccess()
        {
            var items = MapApi.ParseAtlas(Atlas).Items; Assert.AreNotEqual(MapCanvas.ColorOf(items[0]), MapCanvas.ColorOf(items[1]));
        }

        [Test]
        public void TheTripSaysWhyItWasRefused()
        {
            Assert.IsFalse(TeleportTrip.Explain(409, "island_private").Retry); Assert.IsTrue(TeleportTrip.Explain(409, "island_full").Retry); StringAssert.Contains("lotada", TeleportTrip.Explain(409, "island_full").Message);
            Assert.IsTrue(TeleportTrip.Explain(0, null).Retry); Assert.IsFalse(TeleportTrip.Explain(401, null).Retry); StringAssert.Contains("503", TeleportTrip.Explain(503, null).Message); Assert.IsTrue(TeleportTrip.Explain(409, "reservation_expired").Retry);
            Assert.AreEqual("server_starting", TeleportTrip.CodeOf("{\"code\":\"server_starting\"}")); Assert.IsNull(TeleportTrip.CodeOf("oops")); Assert.IsNull(TeleportTrip.CodeOf("[]"));
        }

        [Test]
        public void MarkProblemsAreExplained()
        {
            StringAssert.Contains("limite", WorldMapWindow.MarkProblem("{\"code\":\"too_many_landmarks\"}")); Assert.IsNull(WorldMapWindow.MarkProblem("{\"code\":\"zzz\"}")); Assert.IsNull(WorldMapWindow.MarkProblem("oops"));
        }
    }
}
