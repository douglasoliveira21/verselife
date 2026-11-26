using System;
using System.IO;
using System.Linq;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class EditorAndLoginExtrasTests
    {
        [Test]
        public void LuaIsColoured()
        {
            var tokens = LuaHighlight.Tokens("local n = 10 -- conta\nobject.say(\"oi\" .. n)\n--[[ bloco ]] x = [[longo]]");
            Assert.AreEqual(LuaHighlight.Kind.Keyword, tokens[0].Kind); Assert.AreEqual("local", tokens[0].Text);
            Assert.IsTrue(tokens.Contains((LuaHighlight.Kind.Number, "10"))); Assert.IsTrue(tokens.Contains((LuaHighlight.Kind.Comment, "-- conta"))); Assert.IsTrue(tokens.Contains((LuaHighlight.Kind.Namespace, "object")));
            Assert.IsTrue(tokens.Contains((LuaHighlight.Kind.Call, "say"))); Assert.IsTrue(tokens.Contains((LuaHighlight.Kind.String, "\"oi\""))); Assert.IsTrue(tokens.Contains((LuaHighlight.Kind.Comment, "--[[ bloco ]]"))); Assert.IsTrue(tokens.Contains((LuaHighlight.Kind.String, "[[longo]]")));
            Assert.AreEqual("local n = 10 -- conta\nobject.say(\"oi\" .. n)\n--[[ bloco ]] x = [[longo]]", string.Concat(tokens.Select(t => t.Text)), "nothing is lost or added");
            var rich = LuaHighlight.Rich("if a < b then end"); StringAssert.Contains("<noparse>\u00A0<\u00A0</noparse>", rich); StringAssert.StartsWith("<color=" + LuaHighlight.KeywordColour + "><noparse>if</noparse></color>", rich);
            StringAssert.Contains("</no</noparse><noparse>parse>", LuaHighlight.Rich("x = \"</noparse>\""));
            Assert.AreEqual("", LuaHighlight.Rich("")); Assert.AreEqual(1, LuaHighlight.Tokens("\"sem fim").Count);
        }

        [Test]
        public void ThePasswordIsSealedByWindows()
        {
            if (!SavedLogin.Available) Assert.Ignore("only on Windows");
            var path = Path.Combine(Path.GetTempPath(), "lv-login-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.IsTrue(SavedLogin.Save("ana", "Senha123", path)); var bytes = File.ReadAllBytes(path); Assert.IsFalse(System.Text.Encoding.UTF8.GetString(bytes).Contains("Senha123"), "never in plain text");
                var back = SavedLogin.Load(path); Assert.AreEqual(("ana", "Senha123"), back.Value);
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 }); Assert.IsNull(SavedLogin.Load(path), "a damaged file is not used"); Assert.IsFalse(File.Exists(path), "and is deleted");
                Assert.IsFalse(SavedLogin.Save("", "x", path)); Assert.IsNull(SavedLogin.Load(path));
            }
            finally { SavedLogin.Clear(path); }
        }

        [Test]
        public void TheLightsMoveUnlessStill()
        {
            var lights = new LoginLightShow(); Assert.AreEqual(6, lights.BeamCount); Assert.AreEqual(46, lights.MoteCount);
            var before = lights.FirstBeamAngle; lights.Advance(.5f); Assert.AreNotEqual(before, lights.FirstBeamAngle);
            var still = new LoginLightShow { Still = true }; var at = still.FirstBeamAngle; still.Advance(1f); Assert.AreEqual(at, still.FirstBeamAngle);
        }
    }
}
