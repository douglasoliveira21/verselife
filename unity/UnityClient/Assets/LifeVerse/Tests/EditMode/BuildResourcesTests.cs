using System;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class BuildResourcesTests
    {
        [Test]
        public void SystemTexturesAreTheGodotPatterns()
        {
            Assert.AreEqual(7, BuiltInTextures.All.Length); Assert.IsNull(BuiltInTextures.Get("nada")); Assert.IsFalse(BuiltInTextures.Has(null));
            Assert.AreEqual(.92f, BuiltInTextures.Pixel("checker", 0, 0).r, 1e-5); Assert.AreEqual(.25f, BuiltInTextures.Pixel("checker", 32, 0).r, 1e-5); Assert.AreEqual(.78f, BuiltInTextures.Pixel("bricks", 10, 1).r, 1e-5);
            Assert.AreEqual(BuiltInTextures.Hash(3, 4), BuiltInTextures.Hash(3, 4)); Assert.That(BuiltInTextures.Hash(3, 4), Is.InRange(0f, 1f));
            var t = BuiltInTextures.Get("plywood"); Assert.AreEqual(BuiltInTextures.Size, t.width); Assert.AreSame(t, BuiltInTextures.Get("plywood")); Assert.AreEqual(TextureWrapMode.Repeat, t.wrapMode);
        }

        [Test]
        public void TheLightOfAnObjectIsRead()
        {
            Assert.IsNull(WorldObjects.ReadLight("{}")); Assert.IsNull(WorldObjects.ReadLight("oops"));
            var l = WorldObjects.ReadLight("{\"light\":{\"color\":\"#FF0000\",\"energy\":20,\"range\":0.1,\"falloff\":2,\"shadow\":true,\"face\":3,\"cone\":500},\"projector\":{\"fov\":60,\"ambiance\":0.3},\"projectorBuiltin\":\"checker\"}");
            Assert.AreEqual(Color.red, l.Color); Assert.AreEqual(8f, l.Energy); Assert.AreEqual(.5f, l.Range); Assert.AreEqual(2f, l.Falloff); Assert.IsTrue(l.Shadow); Assert.AreEqual(3, l.Face); Assert.AreEqual(180f, l.Cone);
            Assert.AreEqual(60f, l.ProjectorFov); Assert.AreEqual(.3f, l.Ambiance, 1e-5); Assert.AreEqual("checker", l.ProjectorBuiltin); Assert.IsNull(l.ProjectorTexture);
            Assert.IsNull(WorldObjects.ReadLight("{\"light\":{},\"projectorBuiltin\":\"virus\"}").ProjectorBuiltin);
            Assert.AreEqual((.6f, 8f), WorldObjects.ReadReflection("{\"reflection\":{}}")); Assert.AreEqual((1f, 2f), WorldObjects.ReadReflection("{\"reflection\":{\"intensity\":5,\"range\":0}}")); Assert.IsNull(WorldObjects.ReadReflection("{}"));
        }

        [Test]
        public void ContentGoesInTheGodotFolders()
        {
            Assert.AreEqual("Sons", BuildWindow.FolderOfCategory("sounds")); Assert.AreEqual("Objetos", BuildWindow.FolderOfCategory("models")); Assert.AreEqual("Outros", BuildWindow.FolderOfCategory("xyz")); Assert.AreEqual(9, BuildWindow.ContentFolders.Length);
        }

        [Test]
        public void ScriptTemplatesComeFromTheApiOrTheDefault()
        {
            var list = LuaEditorWindow.TemplatesOf(200, "[{\"name\":\"porta\",\"eventName\":\"on_touch\",\"source\":\"function on_touch(e) end\"},{\"name\":\"vazio\"}]"); Assert.AreEqual(1, list.Count); Assert.AreEqual("porta", list[0].Name);
            Assert.AreEqual(LuaEditorWindow.DefaultTemplate, LuaEditorWindow.TemplatesOf(500, "")[0]); StringAssert.Contains("function on_touch", LuaEditorWindow.DefaultTemplate.Source);
        }

        [Test]
        public void FloatingTextFollowsTheScripts()
        {
            var host = new GameObject("texts").AddComponent<ObjectTexts>(); var id = Guid.NewGuid();
            host.OnOperation("{\"operation\":\"text\",\"objectId\":\"" + id + "\",\"text\":\"Olá\",\"color\":\"#00FF00\",\"x\":1,\"y\":2,\"z\":3}");
            Assert.AreEqual("Olá", host.TextOf(id)); Assert.AreEqual(Color.green, host.ColorOf(id)); Assert.AreEqual(GameSpace.ToUnity(1f, 2.4f, 3f), host.PlaceOf(id).Value);
            host.Show(id, new string('a', 300), "nada", Vector3.zero); Assert.AreEqual(ObjectTexts.MaxLength, host.TextOf(id).Length); Assert.AreEqual(Color.white, host.ColorOf(id));
            host.OnOperation("{\"operation\":\"text\",\"objectId\":\"" + id + "\",\"text\":\"\"}"); Assert.AreEqual(0, host.Count); host.OnOperation("{\"operation\":\"sound\"}"); host.OnOperation("lixo");
            for (var i = 0; i < ObjectTexts.MaxTexts + 5; i++) host.Show(Guid.NewGuid(), "x", "#FFFFFF", Vector3.zero); Assert.AreEqual(ObjectTexts.MaxTexts, host.Count);
            UnityEngine.Object.DestroyImmediate(host.gameObject);
        }
    }
}
