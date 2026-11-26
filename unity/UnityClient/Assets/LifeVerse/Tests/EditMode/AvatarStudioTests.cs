using System;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class AvatarStudioTests
    {
        [Test]
        public void CustomItemsAreRead()
        {
            var hat = Guid.NewGuid(); var list = AvatarWardrobe.ReadItems("[{\"id\":\"" + hat + "\",\"slot\":\"hat\",\"kind\":\"rigid\",\"attachBone\":\"Head\",\"offset\":[0,0.2,0.1],\"rotation\":[0,90,0],\"scale\":0.5},{\"id\":\"x\"},{\"slot\":\"top\"}]");
            Assert.AreEqual(1, list.Count); Assert.AreEqual(hat, list[0].Id); Assert.AreEqual(new Vector3(0, .2f, .1f), list[0].Offset); Assert.AreEqual(90f, list[0].Rotation.y); Assert.AreEqual(.5f, list[0].Scale);
            Assert.AreEqual(0, AvatarWardrobe.ReadItems("lixo").Count); Assert.AreEqual("head", AvatarWardrobe.DefaultBone("glasses")); Assert.AreEqual("chest", AvatarWardrobe.DefaultBone("back")); Assert.AreEqual("hips", AvatarWardrobe.DefaultBone("qualquer"));
        }

        [Test]
        public void TheAppearanceCarriesOverridesAndClothes()
        {
            var a = AvatarAppearance.Parse("{\"animationOverridesJson\":\"{\\\"Idle\\\":\\\"custom:" + Guid.Empty + "#Aceno\\\",\\\"Walk\\\":\\\"builtin:happy\\\"}\",\"customWearablesJson\":\"[1]\"}");
            Assert.AreEqual(2, a.AnimationOverrides.Count); StringAssert.EndsWith("#Aceno", a.AnimationOverrides["Idle"]); Assert.AreEqual("[1]", a.CustomWearablesJson);
            Assert.AreEqual(0, AvatarAppearance.Parse("{}").AnimationOverrides.Count);
        }

        [Test]
        public void TheStudioSpeaksPortuguese()
        {
            Assert.AreEqual("Parado", AvatarStudioWindow.StateWords("Idle")); Assert.AreEqual("parte de cima", AvatarStudioWindow.SlotWords("top"));
            StringAssert.StartsWith("Esse visual usa roupas", AvatarStudioWindow.Words(400, "{\"error\":\"look_invalid\"}"));
            Assert.AreEqual(" Faltam ossos.", AvatarStudioWindow.Problems("{\"error\":\"invalid_model\",\"report\":{\"issues\":[{\"code\":\"x\",\"message\":\"Faltam ossos.\",\"error\":true},{\"code\":\"y\",\"message\":\"Só um aviso.\",\"error\":false}]}}"));
            Assert.AreEqual("", AvatarStudioWindow.Problems("nada")); Assert.AreEqual(5, AvatarStudioWindow.Sections.Length);
        }
    }
}
