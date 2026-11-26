using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using LifeVerse.Shared.Avatar;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class SocialAttachmentsSettingsTests
    {
        [Test]
        public void RecentPeopleKeepTheNewestFirst()
        {
            var list = PeopleHubWindow.Remember(new[] { "Ana", "Bia" }, new[] { "Caio", "ana" });
            CollectionAssert.AreEqual(new[] { "Caio", "ana", "Bia" }, list);
            Assert.AreEqual(PeopleHubWindow.MaxRecent, PeopleHubWindow.Remember(Enumerable.Range(0, 40).Select(i => "p" + i), new string[0]).Count);
            Assert.AreEqual("12 m", PeopleHubWindow.Distance(12.4f)); Assert.AreEqual("1,5 km", PeopleHubWindow.Distance(1500f));
        }

        [Test]
        public void TheServerSaysWhereToArrive()
        {
            var a = Arrival.Read("{\"allowed\":true,\"reason\":null,\"x\":10.5,\"z\":-3,\"parcelId\":null,\"parcelName\":\"Praia\",\"forced\":true}");
            Assert.IsTrue(a.Allowed && a.Forced); Assert.AreEqual(10.5f, a.X); Assert.AreEqual("Praia", a.Parcel);
            Assert.IsFalse(Arrival.Read("lixo").Allowed); StringAssert.Contains("restrito", Arrival.Words("parcel_restricted")); StringAssert.Contains("Não foi possível", Arrival.Words("qualquer"));
        }

        [Test]
        public void SearchDetailsInWords()
        {
            Assert.AreEqual("Vocês são amigos.", GlobalSearchWindow.RelationWords("friend")); Assert.AreEqual("", GlobalSearchWindow.RelationWords("none"));
            Assert.AreEqual("Ilha cheia", GlobalSearchWindow.AccessButton("full")); StringAssert.Contains("privada", GlobalSearchWindow.AccessWords("private")); Assert.AreEqual("", GlobalSearchWindow.AccessWords("open"));
            using (var d = JsonDocument.Parse("{\"copy\":true,\"transfer\":true}")) Assert.AreEqual("copiar, transferir", GlobalSearchWindow.RightsWords(d.RootElement));
            using (var d = JsonDocument.Parse("{}")) Assert.AreEqual("nada além de usar", GlobalSearchWindow.RightsWords(d.RootElement));
            Assert.AreEqual("2,5 KB", GlobalSearchWindow.Size(2560)); Assert.AreEqual("Saldo insuficiente para esta compra.", GlobalSearchWindow.BuyWords(409, "{\"error\":\"insufficient_balance\"}"));
        }

        [Test]
        public void ThePlacementIsEditedBySteps()
        {
            var m = new AttachmentEditRules(new Placement(new float[] { 0, 0, 0 }, new float[] { 0, 0, 0 }, new float[] { 1, 1, 1 })) { PositionStep = .05f, RotationStep = 15f };
            m.Commit(m.Dragged(m.Now, AttachmentEditRules.Tool.Move, 1, .123f)); Assert.AreEqual(.1f, m.Now.P[1], 1e-4f); Assert.IsTrue(m.Dirty);
            m.Commit(m.Dragged(m.Now, AttachmentEditRules.Tool.Rotate, 2, 200f)); Assert.AreEqual(-165f, m.Now.R[2], 1e-3f);
            m.Commit(m.Dragged(m.Now, AttachmentEditRules.Tool.Scale, 0, 1f)); Assert.AreEqual(2f, m.Now.S[0], 1e-4f); Assert.AreEqual(2f, m.Now.S[2], 1e-4f, "keeps the proportions");
            m.KeepProportions = false; m.Commit(m.Dragged(m.Now, AttachmentEditRules.Tool.Scale, 1, 50f)); Assert.AreEqual(AttachmentEditRules.MaxScale, m.Now.S[1]); Assert.AreEqual(2f, m.Now.S[0], 1e-4f);
            m.Commit(m.Dragged(m.Now, AttachmentEditRules.Tool.Move, 0, 9f)); Assert.AreEqual(AttachmentEditRules.MaxOffset, m.Now.P[0]);
            m.Undo(); Assert.AreEqual(0f, m.Now.P[0]); m.Redo(); Assert.AreEqual(AttachmentEditRules.MaxOffset, m.Now.P[0]); m.Accept(); Assert.IsFalse(m.Dirty);
        }

        [Test]
        public void WornFilesAreRecognised()
        {
            Assert.AreEqual(AvatarAttachments.ContentKind.Image, AvatarAttachments.KindOf(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2 })); Assert.AreEqual(AvatarAttachments.ContentKind.Image, AvatarAttachments.KindOf(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
            Assert.AreEqual(AvatarAttachments.ContentKind.Construction, AvatarAttachments.KindOf(Encoding.UTF8.GetBytes(" {\"format\":\"lifeverse.build\",\"objects\":[]}")));
            Assert.AreEqual(AvatarAttachments.ContentKind.Model, AvatarAttachments.KindOf(Encoding.ASCII.GetBytes("glTF....")));
            var plate = AvatarAttachments.Plate(Guid.NewGuid(), 200, 100); Assert.AreEqual(1, plate.Objects.Length); Assert.AreEqual(1f, plate.Objects[0].ScaleX); Assert.AreEqual(.5f, plate.Objects[0].ScaleY); StringAssert.Contains("texture", plate.Objects[0].MaterialJson);
        }

        [Test]
        public void PointsAreGroupedByRegion()
        {
            var all = AttachmentsWindow.PointNames(new[] { "humanoid" }); var head = AttachmentsWindow.PointNames(new[] { "humanoid" }, AttachmentPoints.Head);
            Assert.Greater(all.Count, head.Count); Assert.IsTrue(head.Contains("Crânio")); Assert.IsTrue(head.All(n => AttachmentPoints.All.First(p => p.Name == n).Region == AttachmentPoints.Head));
            Assert.AreEqual("", AttachmentsWindow.RegionOf(0)); Assert.AreEqual(AttachmentPoints.Hud, AttachmentsWindow.RegionOf(AttachmentsWindow.Regions.Length - 1));
        }

        [Test]
        public void PictureSettingsMapToThisRenderer()
        {
            Assert.AreEqual(UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None, SettingsEffects.AntialiasingOf("0"));
            Assert.AreEqual(UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing, SettingsEffects.AntialiasingOf("4"));
            SettingsEffects.RenderScale.Target = 80f; SettingsEffects.RenderScale.Dynamic = false; SettingsEffects.RenderScale.Tick(.016f); Assert.AreEqual(80f, SettingsEffects.RenderScale.Current);
            SettingsEffects.RenderScale.Dynamic = true; for (var i = 0; i < 400; i++) SettingsEffects.RenderScale.Tick(.05f); Assert.AreEqual(50f, SettingsEffects.RenderScale.Current, "slow frames lower it to half");
            for (var i = 0; i < 400; i++) SettingsEffects.RenderScale.Tick(.008f); Assert.AreEqual(80f, SettingsEffects.RenderScale.Current, "quick frames bring it back, not past the target");
            SettingsEffects.RenderScale.Dynamic = false; SettingsEffects.RenderScale.Target = 100f; SettingsEffects.RenderScale.Tick(.016f);
            foreach (var id in new[] { "graphics.ssao", "graphics.ssr", "graphics.render_scale", "chat.visible_seconds", "world.minimap_zoom", "network.auto_reconnect" }) Assert.IsTrue(SettingsCatalog.All.First(d => d.Id == id).IsAvailable, id);
        }
    }
}
