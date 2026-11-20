using LifeVerse.Shared.Updates;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class UpdateTests
    {
        private const string Body = "{\"latest\":\"0.9.0\",\"minimum\":\"0.7.0\",\"downloadUrl\":\"https://example.com/get\",\"notes\":\"x\"}";

        [Test]
        public void TheAnswerOfThePlatformDecidesTheStatus()
        {
            var older = new UpdateCheck("0.6.0"); older.Apply(200, Body); Assert.AreEqual(UpdateStatus.Required, older.Status); Assert.IsTrue(older.Required); StringAssert.Contains("obrigatória", older.Describe());
            var behind = new UpdateCheck("0.8.0"); behind.Apply(200, Body); Assert.AreEqual(UpdateStatus.Available, behind.Status); Assert.IsFalse(behind.Required); StringAssert.Contains("0.9.0", behind.Describe());
            var current = new UpdateCheck("0.9.0"); current.Apply(200, Body); Assert.AreEqual(UpdateStatus.UpToDate, current.Status); Assert.AreEqual("", current.Describe());
        }

        [Test]
        public void NoAnswerOrABrokenOneChangesNothing()
        {
            foreach (var (code, body) in new[] { (500, "{}"), (404, ""), (200, "oops"), (200, "{}"), (200, "[]") }) { var c = new UpdateCheck("0.1.0"); c.Apply(code, body); Assert.AreEqual(UpdateStatus.Unknown, c.Status, code + " " + body); Assert.IsFalse(c.Required); Assert.AreEqual("", c.Describe()); }
            var unparsable = new UpdateCheck("beta"); unparsable.Apply(200, Body); Assert.AreEqual(UpdateStatus.Unknown, unparsable.Status);
        }

        [Test]
        public void OnlyPlainWebLinksAreOpened()
        {
            string opened = null; var good = new UpdateCheck("0.1.0"); good.Apply(200, Body); Assert.IsTrue(good.OpenDownload(url => opened = url)); Assert.AreEqual("https://example.com/get", opened);
            opened = null; var bad = new UpdateCheck("0.1.0"); bad.Apply(200, Body.Replace("https://example.com/get", "file:///c:/windows/system32/calc.exe")); Assert.IsFalse(bad.OpenDownload(url => opened = url)); Assert.IsNull(opened);
            Assert.IsFalse(new UpdateCheck("0.1.0").OpenDownload(url => opened = url));
        }

        [Test]
        public void TheLoginScreenShowsAndHidesTheNotice()
        {
            var login = new LoginView(new UserSettings(), "v"); Assert.AreEqual("", login.UpdateNotice);
            var asked = 0; login.ShowUpdate("Nova versão", false, true, () => asked++); Assert.AreEqual("Nova versão", login.UpdateNotice); Assert.IsTrue(login.UpdateDownloadShown);
            login.ShowUpdate("Obrigatória", true, false, null); Assert.IsFalse(login.UpdateDownloadShown); login.ShowUpdate("", false, false, null); Assert.AreEqual("Obrigatória", login.UpdateNotice, "an empty text only hides the banner");
            Assert.AreEqual(DisplayStyle.None, login.Q<VisualElement>("update").style.display.value);
        }
    }
}
