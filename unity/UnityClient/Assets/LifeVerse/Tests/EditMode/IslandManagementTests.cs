using System;
using System.Text.Json;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class IslandManagementTests
    {
        [Test]
        public void TheGeneralDataIsChecked()
        {
            Assert.IsNotNull(IslandManagementWindow.ValidateGeneral("ab", "10", "0", "0", out _, out _, out _));
            Assert.IsNotNull(IslandManagementWindow.ValidateGeneral("Ilha", "0", "0", "0", out _, out _, out _));
            Assert.IsNotNull(IslandManagementWindow.ValidateGeneral("Ilha", "10", "x", "0", out _, out _, out _));
            Assert.IsNull(IslandManagementWindow.ValidateGeneral("Ilha", "32", "12,5", "-4", out var max, out var x, out var z)); Assert.AreEqual(32, max); Assert.AreEqual(12.5f, x); Assert.AreEqual(-4f, z);
        }

        [Test]
        public void ExpiriesAreDays()
        {
            var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
            Assert.IsNull(IslandManagementWindow.ExpiryIn("", now, out var ok)); Assert.IsTrue(ok);
            Assert.AreEqual(now.AddDays(7), IslandManagementWindow.ExpiryIn(" 7 ", now, out ok)); Assert.IsTrue(ok);
            IslandManagementWindow.ExpiryIn("0", now, out ok); Assert.IsFalse(ok); IslandManagementWindow.ExpiryIn("dez", now, out ok); Assert.IsFalse(ok); IslandManagementWindow.ExpiryIn("4000", now, out ok); Assert.IsFalse(ok);
        }

        [Test]
        public void DurationsAreToldInWords()
        {
            Assert.AreEqual("45 s", IslandManagementWindow.Duration(45)); Assert.AreEqual("12 min", IslandManagementWindow.Duration(725)); Assert.AreEqual("3 h 05 min", IslandManagementWindow.Duration(3 * 3600 + 300)); Assert.AreEqual("2 d 4 h", IslandManagementWindow.Duration(2 * 86400 + 4 * 3600)); Assert.AreEqual("0 s", IslandManagementWindow.Duration(-3));
        }

        [Test]
        public void TheLookIsCheckedAndBuilt()
        {
            var json = IslandManagementWindow.LookJson("18.5", "rain", "0,3", "0.8", "#a0c0ff", out var problem); Assert.IsNull(problem);
            using (var d = JsonDocument.Parse(json)) { Assert.AreEqual(18.5, d.RootElement.GetProperty("hour").GetDouble()); Assert.AreEqual("rain", d.RootElement.GetProperty("weather").GetString()); Assert.AreEqual(0.3, d.RootElement.GetProperty("fog").GetDouble(), 1e-9); Assert.AreEqual("#A0C0FF", d.RootElement.GetProperty("sky").GetString()); }
            Assert.IsFalse(IslandManagementWindow.LookJson("12", "clear", "0", "1", "", out _).Contains("sky"));
            Assert.IsNull(IslandManagementWindow.LookJson("25", "clear", "0", "1", "", out problem)); Assert.IsNotNull(problem);
            Assert.IsNull(IslandManagementWindow.LookJson("12", "snow", "0", "1", "", out problem)); Assert.IsNull(IslandManagementWindow.LookJson("12", "clear", "2", "1", "", out _)); Assert.IsNull(IslandManagementWindow.LookJson("12", "clear", "0", "1", "azul", out _));
        }

        [Test]
        public void MusicAddressesAreChecked()
        {
            Assert.IsNull(IslandManagementWindow.MusicProblem("")); Assert.IsNull(IslandManagementWindow.MusicProblem("https://cdn.example.com/a/tema.mp3")); Assert.IsNull(IslandManagementWindow.MusicProblem("https://cdn.example.com/tema.OGG?v=2"));
            Assert.IsNotNull(IslandManagementWindow.MusicProblem("http://cdn.example.com/tema.mp3")); Assert.IsNotNull(IslandManagementWindow.MusicProblem("https://cdn.example.com/tema.wav")); Assert.IsNotNull(IslandManagementWindow.MusicProblem("https://x.com/" + new string('a', 520) + ".mp3"));
        }

        [Test]
        public void RestartBodiesCarryTheMode()
        {
            StringAssert.Contains("\"mode\":\"in\"", IslandManagementWindow.RestartBody("in", 5, null, " manutenção ")); StringAssert.Contains("\"minutes\":5", IslandManagementWindow.RestartBody("in", 5, null, ""));
            StringAssert.Contains("\"at\":\"2026-11-25T20:00:00", IslandManagementWindow.RestartBody("at", null, new DateTimeOffset(2026, 11, 25, 20, 0, 0, TimeSpan.Zero), ""));
        }

        [Test]
        public void OptionsAreReadAsWordsOrObjects()
        {
            using var d = JsonDocument.Parse("{\"a\":[\"pt-BR\",\"en\"],\"b\":[{\"id\":\"social\",\"title\":\"Social\"},{\"key\":\"jogo\"}]}");
            var a = IslandManagementWindow.OptionsOf(d.RootElement, "a"); Assert.AreEqual(2, a.Count); Assert.AreEqual("en", a[1].Id);
            var b = IslandManagementWindow.OptionsOf(d.RootElement, "b"); Assert.AreEqual("Social", b[0].Text); Assert.AreEqual("jogo", b[1].Id); Assert.AreEqual("jogo", b[1].Text);
            Assert.AreEqual(0, IslandManagementWindow.OptionsOf(d.RootElement, "c").Count);
            Assert.AreEqual("no ar", IslandManagementWindow.StateWords("online")); Assert.AreEqual("xyz", IslandManagementWindow.StateWords("xyz"));
            Assert.AreEqual(16, IslandManagementWindow.Sections.Length);
        }
    }
}
