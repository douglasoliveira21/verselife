using System;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class LiveTests
    {
        [Test]
        public void TheChannelAddressComesFromTheApi()
        {
            Assert.AreEqual("wss://api.example.com/api/v1/notifications/ws", LiveChannel.AddressOf("https://api.example.com"));
            Assert.AreEqual("ws://127.0.0.1:5099/api/v1/notifications/ws", LiveChannel.AddressOf("http://127.0.0.1:5099/"));
        }

        [Test]
        public void ThePauseBetweenAttemptsGrowsUpToAMinute()
        {
            Assert.AreEqual(2f, LiveChannel.NextPause(0f)); Assert.AreEqual(4f, LiveChannel.NextPause(2f)); Assert.AreEqual(60f, LiveChannel.NextPause(40f)); Assert.AreEqual(60f, LiveChannel.NextPause(60f));
        }

        [Test]
        public void ACenterPageIsReadWithItsCounters()
        {
            var id = Guid.NewGuid(); var group = Guid.NewGuid();
            var page = CenterPage.Parse("{\"unread\":5,\"unreadByCategory\":{\"grupos\":2,\"convites\":1,\"sistema\":2},\"hasMore\":true,\"skip\":20,\"items\":[{\"source\":\"invite\",\"id\":\"" + id + "\",\"category\":\"convites\",\"kind\":\"group_invite\",\"title\":\"Convite\",\"body\":\"x\",\"at\":\"2026-11-25T10:00:00Z\",\"read\":false,\"groupId\":\"" + group + "\",\"actions\":[\"accept\",\"decline\"]},{\"nada\":1}]}");
            Assert.AreEqual(1, page.Items.Count); Assert.AreEqual(5, page.UnreadOf("todas")); Assert.AreEqual(2, page.UnreadOf("grupos")); Assert.AreEqual(1, page.UnreadOf("convites")); Assert.IsTrue(page.HasMore); Assert.AreEqual(20, page.Skip);
            var entry = page.Items[0]; Assert.IsTrue(entry.CanAnswer); Assert.AreEqual(group, entry.GroupId); Assert.IsFalse(entry.Read); Assert.AreEqual(2026, entry.At.Year);
            Assert.IsNull(CenterPage.Parse("oops")); Assert.IsNull(CenterPage.Parse("{}"));
        }

        [Test]
        public void TheCenterQueriesAreBuilt()
        {
            Assert.AreEqual("/api/v1/notifications/center?category=grupos&unread=true&skip=20&take=20", CenterPage.ListPath("grupos", true, null, 20, 20));
            StringAssert.EndsWith("&q=festa%20hoje", CenterPage.ListPath("todas", false, " festa hoje ", 0, 20));
            StringAssert.Contains("\"all\":true", CenterPage.ReadBody(null, true, "todas")); var one = CenterPage.ReadBody(new[] { new CenterEntry { Source = "group", Id = Guid.Empty } }, false, "grupos"); StringAssert.Contains("\"source\":\"group\"", one); StringAssert.Contains("\"all\":false", one);
        }

        [Test]
        public void MomentsAreToldLikeInTheGodotClient()
        {
            var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
            Assert.AreEqual("agora", RelativeTime.Of(now.AddSeconds(-10), now)); Assert.AreEqual("há 1 minuto", RelativeTime.Of(now.AddMinutes(-1), now)); Assert.AreEqual("há 5 minutos", RelativeTime.Of(now.AddMinutes(-5), now));
            Assert.AreEqual("há 1 hora", RelativeTime.Of(now.AddHours(-1), now)); Assert.AreEqual("há 3 dias", RelativeTime.Of(now.AddDays(-3), now)); Assert.AreEqual("", RelativeTime.Of(default, now)); StringAssert.Contains("/2026", RelativeTime.Of(now.AddDays(-30), now));
        }

        [Test]
        public void EventsAreSortedIntoTheCategoriesOfTheNotices()
        {
            Assert.AreEqual("convites", LiveEvents.CategoryOfType("group_invite")); Assert.AreEqual("convites", LiveEvents.CategoryOfType("teleport_invite")); Assert.AreEqual("grupos", LiveEvents.CategoryOfType("group_message"));
            Assert.AreEqual("sistema", LiveEvents.CategoryOfType("friend_request")); Assert.AreEqual("sistema", LiveEvents.CategoryOfType("message")); Assert.AreEqual("sistema", LiveEvents.CategoryOfType(null));
        }

        [Test]
        public void TeleportRefusalsAreExplained()
        {
            StringAssert.Contains("mesma ilha", LiveEvents.InviteError(409, "{\"error\":\"not_on_island\"}")); StringAssert.Contains("espere", LiveEvents.InviteError(429, "{}")); StringAssert.Contains("expirou", LiveEvents.InviteError(409, "{\"error\":\"expired\"}")); Assert.IsNotEmpty(LiveEvents.InviteError(500, "oops"));
        }
    }
}
