using System;
using System.Linq;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class AttachmentTests
    {
        private const string Sample = "{\"profiles\":[\"humanoid\"],\"attachments\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"inventoryItemId\":\"22222222-2222-2222-2222-222222222222\",\"assetId\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Chapéu\",\"point\":\"head_top\",\"pointName\":\"Topo da cabeça\",\"position\":[0,0.1,0],\"rotation\":[0,90,0],\"scale\":[1,1,1],\"equipped\":true,\"order\":0,\"revision\":4,\"canModify\":true}]}";

        [Test]
        public void ParsesTheListTheApiSends()
        {
            var list = AttachmentData.ParseList(Sample, out var profiles);
            Assert.AreEqual(1, list.Count); Assert.AreEqual("humanoid", profiles.Single()); var a = list[0];
            Assert.AreEqual("head_top", a.Point); Assert.IsTrue(a.Equipped); Assert.AreEqual(4, a.Revision); Assert.AreEqual(90f, a.Rotation[1], .001f); Assert.IsFalse(a.IsHud); StringAssert.Contains("Chapéu", a.Describe());
        }

        [Test]
        public void ParsesTheBareListOfAnAppearanceAndSurvivesGarbage()
        {
            var bare = "[" + Sample.Substring(Sample.IndexOf('{', 1), Sample.LastIndexOf('}') - Sample.IndexOf('{', 1) - 1).TrimEnd(']') + "]";
            Assert.AreEqual(0, AttachmentData.ParseArray("not json").Count); Assert.AreEqual(0, AttachmentData.ParseArray("").Count); Assert.AreEqual(0, AttachmentData.ParseArray("{}").Count);
            Assert.AreEqual(0, AttachmentData.ParseList("oops", out _).Count);
            Assert.IsNotNull(bare);
        }

        [Test]
        public void AppearanceCarriesWhatIsWorn()
        {
            var json = "{\"bodyAttachmentsJson\":\"[{\\\"id\\\":\\\"11111111-1111-1111-1111-111111111111\\\",\\\"assetId\\\":\\\"33333333-3333-3333-3333-333333333333\\\",\\\"point\\\":\\\"skull\\\",\\\"equipped\\\":true}]\"}";
            var appearance = AvatarAppearance.Parse(json); Assert.AreEqual(1, AttachmentData.ParseArray(appearance.AttachmentsJson).Count);
        }

        [Test]
        public void TriplesAreReadWithCommasAndRejectBadInput()
        {
            CollectionAssert.AreEqual(new[] { 0f, .5f, -1f }, AttachmentsWindow.ParseTriple("0 0,5 -1")); Assert.IsNull(AttachmentsWindow.ParseTriple("1 2")); Assert.IsNull(AttachmentsWindow.ParseTriple("a b c")); Assert.IsNull(AttachmentsWindow.ParseTriple("1 2 NaN"));
            Assert.AreEqual("0 0.1 0", AttachmentsWindow.Triple(new[] { 0f, .1f, 0f }));
        }

        [Test]
        public void PointListSkipsTheScreenAndTheOptionalProfiles()
        {
            var names = AttachmentsWindow.PointNames(new[] { "humanoid" }); Assert.IsTrue(names.Contains("Crânio")); Assert.IsNotNull(AttachmentsWindow.KeyOfName("Crânio")); Assert.IsNull(AttachmentsWindow.KeyOfName("não existe"));
            Assert.Greater(AttachmentsWindow.PointNames(new[] { "humanoid", "tail", "wings", "fingers" }).Count, names.Count);
        }

        [Test]
        public void ErrorsOfTheApiAreExplained()
        {
            StringAssert.Contains("máximo", AttachmentsWindow.Explain("{\"error\":\"too_many_attachments\"}")); Assert.IsNull(AttachmentsWindow.Explain("{\"error\":\"zzz\"}")); Assert.IsNull(AttachmentsWindow.Explain("x"));
        }
    }
}
