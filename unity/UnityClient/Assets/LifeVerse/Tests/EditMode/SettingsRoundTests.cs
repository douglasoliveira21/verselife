using System;
using System.Linq;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The rules behind the last settings brought from the Godot client: avatar budget, automatic quality, double tap and hold to fly, map marks, radial tips, object distance.</summary>
    public sealed class SettingsRoundTests
    {
        [Test]
        public void EverySettingEitherActsOrSaysWhyNot()
        {
            var silent = SettingsCatalog.All.Where(d => d.Unavailable == SettingsCatalog.NotYet).Select(d => d.Id).ToList();
            foreach (var id in new[] { "avatars.max_full", "voice.spatial", "graphics.water_waves", "world.map_events", "controls.double_tap_run", "privacy.external_media", "build.space" })
                CollectionAssert.DoesNotContain(silent, id, id + " should act in Unity now");
            StringAssert.Contains("Windows", SettingsCatalog.Find("audio.output_device").Unavailable);
            StringAssert.Contains("barra de ferramentas", SettingsCatalog.Find("interface.side_bar").Unavailable);
        }

        [Test]
        public void TheAvatarBudgetFollowsTheGodotRule()
        {
            var normal = SettingsEffects.BudgetFor(10, "medium", true, 120, 24, 250);
            Assert.AreEqual(10, normal.MaxFull); Assert.AreEqual(25f, normal.FullRange); Assert.AreEqual(60f, normal.ReducedRange); Assert.AreEqual(120f, normal.BasicRange); Assert.AreEqual(250_000, normal.Triangles);
            var high = SettingsEffects.BudgetFor(10, "high", true); Assert.AreEqual(37.5f, high.FullRange, 1e-3f); Assert.AreEqual(90f, high.ReducedRange, 1e-3f);
            var plain = SettingsEffects.BudgetFor(99, "low", false, 10, 24, 5); Assert.AreEqual(40, plain.MaxFull); Assert.AreEqual(0, plain.MaxReduced, "no reduced avatars without simplifying"); Assert.AreEqual(50_000, plain.Triangles);
            Assert.Greater(plain.BasicRange, plain.ReducedRange, "the last range is always past the reduced one");
            Assert.AreEqual(1, SettingsEffects.AnimationDivisorOf("high")); Assert.AreEqual(2, SettingsEffects.AnimationDivisorOf("normal")); Assert.AreEqual(3, SettingsEffects.AnimationDivisorOf("low"));
        }

        [Test]
        public void AutomaticQualityLowersOneLevelAfterThreeBadWindowsAndWaitsAMinute()
        {
            SettingsEffects.AutoQuality.Reset(); SettingsEffects.AutoQuality.Enabled = true;
            try
            {
                string Run(float seconds, float fps, string preset) { string got = null; for (var t = 0f; t < seconds; t += .5f) got ??= SettingsEffects.AutoQuality.Tick(.5f, fps, 60f, preset); return got; }
                Assert.IsNull(Run(14f, 30f, "ultra"), "two windows are not enough");
                Assert.AreEqual("high", Run(2f, 30f, "ultra"), "the third bad window lowers it");
                Assert.IsNull(Run(30f, 30f, "high"), "then it waits a minute");
                Assert.IsNull(SettingsEffects.AutoQuality.Below("verylow")); Assert.IsNull(SettingsEffects.AutoQuality.Below("custom"));
                SettingsEffects.AutoQuality.Reset(); Assert.IsNull(Run(40f, 55f, "high"), "55 of 60 is not bad");
                SettingsEffects.AutoQuality.Enabled = false; SettingsEffects.AutoQuality.Reset(); Assert.IsNull(Run(40f, 5f, "high"), "off never lowers");
            }
            finally { SettingsEffects.AutoQuality.Enabled = false; SettingsEffects.AutoQuality.Reset(); }
        }

        [Test]
        public void DoubleTapRunsAndHoldingJumpFliesOnce()
        {
            var g = new MoveGestures(); MoveGestures.DoubleTapRunEnabled = MoveGestures.HoldToFlyEnabled = true;
            g.Tick(.02f, 1f, true, false, false, true); g.Tick(.02f, 1.1f, false, false, false, true); Assert.IsFalse(g.Running);
            g.Tick(.02f, 1.3f, true, false, false, true); Assert.IsTrue(g.Running, "second tap within 0.32 s runs");
            g.Tick(.02f, 1.5f, false, false, false, true); Assert.IsFalse(g.Running, "letting go stops");
            g.Tick(.02f, 3f, true, false, false, true); g.Tick(.02f, 3.1f, false, false, false, true); g.Tick(.02f, 3.6f, true, false, false, true); Assert.IsFalse(g.Running, "too slow is walking");
            var toggles = 0; for (var i = 0; i < 60; i++) if (g.Tick(.02f, 4f + i * .02f, false, true, false, true)) toggles++;
            Assert.AreEqual(1, toggles, "a hold of 1.2 s toggles flight once");
            Assert.IsFalse(g.Tick(.3f, 6f, false, true, false, false), "not while flying, swimming or seated");
            MoveGestures.DoubleTapRunEnabled = MoveGestures.HoldToFlyEnabled = false;
            try { g.Tick(.02f, 7f, true, false, false, true); g.Tick(.02f, 7.05f, false, false, false, true); g.Tick(.02f, 7.1f, true, false, false, true); Assert.IsFalse(g.Running); Assert.IsFalse(g.Tick(1f, 8f, false, true, false, true)); }
            finally { MoveGestures.DoubleTapRunEnabled = MoveGestures.HoldToFlyEnabled = true; }
        }

        [Test]
        public void MapMarksFollowTheLayers()
        {
            var island = new AtlasIsland { Id = Guid.NewGuid(), Favorite = true };
            Assert.AreEqual(4, MapLayers.Badges(island, true, true, true).Count);
            MapLayers.Events = false; MapLayers.Favorites = false;
            try { var marks = MapLayers.Badges(island, true, true, true); Assert.AreEqual(2, marks.Count); Assert.AreEqual("star", marks[0].Icon); Assert.AreEqual("people", marks[1].Icon); }
            finally { MapLayers.Events = MapLayers.Favorites = true; }
            var items = Enumerable.Range(0, 12).Select(i => new AtlasIsland { Id = Guid.NewGuid(), Visitors = i, Favorites = 0 }).ToList(); items[3].IsWelcome = true;
            var featured = MapLayers.FeaturedOf(items); Assert.AreEqual(8, featured.Count); Assert.AreEqual(items[3].Id, featured[0], "the welcome island first"); CollectionAssert.DoesNotContain(featured, items[0].Id, "nobody visits it");
        }

        [Test]
        public void RadialTipsAndObjectDistance()
        {
            StringAssert.Contains("indisponível", RadialMenu.TipOf(new RadialItem("Sentar", null, false)));
            Assert.AreEqual("Por quê", RadialMenu.TipOf(new RadialItem("Sentar", null, false, "Por quê")));
            RadialMenu.ReduceMotion = true; try { Assert.AreEqual(0f, RadialMenu.OpenSeconds); } finally { RadialMenu.ReduceMotion = false; }
            RadialMenu.Speed = 2f; try { Assert.AreEqual(.08f, RadialMenu.OpenSeconds, 1e-4f); } finally { RadialMenu.Speed = 1f; }
            WorldObjects.MaxDistance = 1000f; Assert.AreEqual(300f, WorldObjects.LimitRange(300f), "the most hides nothing beyond its own range");
            WorldObjects.MaxDistance = 80f; try { Assert.AreEqual(80f, WorldObjects.LimitRange(300f)); Assert.AreEqual(40f, WorldObjects.LimitRange(40f)); } finally { WorldObjects.MaxDistance = 1000f; }
            WorldObjects.ExternalMedia = false; try { Assert.IsTrue(WorldObjects.MediaBlocked("https://example.com/a.png")); Assert.IsFalse(WorldObjects.MediaBlocked("file:///c:/a.png")); } finally { WorldObjects.ExternalMedia = true; }
            Assert.IsFalse(WorldObjects.MediaBlocked("https://example.com/a.png"));
        }
    }
}
