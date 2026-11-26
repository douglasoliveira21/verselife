using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Shared.Land;
using LifeVerse.Unity.Audio;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The pieces of the real estate notices, buying from the search, achievement icons, voice switched off by the land and the settings that follow the account.</summary>
    public sealed class PendingRoundTests
    {
        [Test]
        public void TheNoticesOfTheRealEstateAreRead()
        {
            Guid a = Guid.NewGuid(), b = Guid.NewGuid();
            var data = NoticesData.Parse("{\"unread\":1,\"items\":[{\"id\":\"" + a + "\",\"kind\":\"sold\",\"title\":\"Seu terreno foi vendido\",\"body\":\"Comprador: Bia.\",\"at\":\"2026-11-20T12:00:00Z\",\"read\":false},{\"id\":\"" + b + "\",\"title\":\"Antigo\",\"read\":true},{\"id\":\"x\"}]}");
            Assert.AreEqual(1, data.Unread); Assert.AreEqual(2, data.Items.Count); Assert.AreEqual("sold", data.Items[0].Kind); Assert.IsFalse(data.Items[0].Read); Assert.AreEqual(2026, data.Items[0].At.Year); Assert.IsTrue(data.Items[1].Read);
            Assert.AreEqual("Você tem 1 aviso novo: Seu terreno foi vendido", data.Strip()); data.Unread = 0; Assert.AreEqual("", data.Strip());
            Assert.IsNull(NoticesData.Parse("oops")); Assert.IsNull(NoticesData.Parse("{}"));
            Assert.AreEqual("{\"ids\":null}", NoticesData.ReadBody(new Guid[0])); Assert.AreEqual("{\"ids\":[\"" + a + "\",\"" + b + "\"]}", NoticesData.ReadBody(new[] { a, b }));
        }

        [Test]
        public void EachNoticeIsAnnouncedOnce()
        {
            PropertyNotice Notice(string title, bool read = false) => new PropertyNotice { Id = Guid.NewGuid(), Title = title, Read = read };
            var teller = new NoticeTeller(); var one = Notice("A"); var two = Notice("B"); var data = new NoticesData { Items = new List<PropertyNotice> { one, two, Notice("lido", true) } };
            Assert.AreEqual("Você tem 2 avisos novos sobre os seus imóveis.", teller.Tell(data)); Assert.IsNull(teller.Tell(data));
            data.Items.Insert(0, Notice("C")); Assert.AreEqual("C", teller.Tell(data));
            data.Items.Insert(0, Notice("D")); data.Items.Insert(0, Notice("E")); Assert.AreEqual("2 avisos novos sobre os seus imóveis. O mais recente: E", teller.Tell(data));
            Assert.IsNull(new NoticeTeller().Tell(new NoticesData()));
        }

        [Test]
        public void AStoreAndAShortWalletAreSaidInWords()
        {
            using var d = JsonDocument.Parse("{\"kind\":\"physical\",\"owner\":{\"displayName\":\"Bia\"},\"island\":{\"name\":\"Praia\"},\"categoryLabel\":\"Moda\",\"productCount\":2,\"featured\":[{\"name\":\"Chapéu\",\"priceCoins\":30},{\"name\":\"Fita\",\"priceCoins\":0}]}");
            var lines = GlobalSearchWindow.StoreLines(d.RootElement);
            CollectionAssert.AreEqual(new[] { "Proprietário(a): Bia.", "Loja física em Praia.", "Moda", "2 produtos publicados.", "Em destaque:", "• Chapéu — 30 VC", "• Fita — gratuito" }, lines);
            using var e = JsonDocument.Parse("{\"kind\":\"virtual\",\"island\":null,\"productCount\":0}");
            CollectionAssert.AreEqual(new[] { "Loja virtual: sem endereço no mundo.", "Nenhum produto à venda agora." }, GlobalSearchWindow.StoreLines(e.RootElement));
            Assert.AreEqual("Saldo insuficiente: você tem 10 VC e o item custa 30 VC.", GlobalSearchWindow.ShortOfCoins(10, 30));
            var owner = Guid.NewGuid(); StringAssert.EndsWith("&owner=" + owner, GlobalSearchWindow.SearchPath("", new[] { "product" }, 1, "", owner: owner)); Assert.IsFalse(GlobalSearchWindow.SearchPath("", null, 1, "").Contains("owner"));
        }

        [Test]
        public void AnAchievementShowsTheIconTheServerNames()
        {
            using var d = JsonDocument.Parse("[{\"icon\":\"people\"},{\"icon\":\"home\"},{\"icon\":\"nao_existe\"},{}]"); var list = d.RootElement.EnumerateArray().ToList();
            CollectionAssert.AreEqual(new[] { "people", "home", "star", "star" }, list.Select(ProfileHubWindow.AchievementIcon).ToArray());
            foreach (var name in new[] { "calendar", "star", "home", "people" }) Assert.IsTrue(IconShapes.Has(name), name);
        }

        [Test]
        public void TheParcelThatSwitchedVoiceOffClosesTheMicrophone()
        {
            var host = new GameObject("voice-test");
            try
            {
                var at = Vector3.zero; var land = host.AddComponent<LandMusic>(); land.Setup(null, () => Guid.Empty, () => at);
                var quiet = LandMath.CellOf(0); var loud = LandMath.CellOf(500);
                land.ReadLand("{\"version\":7,\"parcels\":[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Biblioteca\",\"allowVoice\":false,\"runs\":[[" + quiet + "," + quiet + "," + quiet + "]]},{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Palco\",\"allowVoice\":true,\"runs\":[[" + loud + "," + loud + "," + loud + "]]}]}");
                var run = LandMath.FromRuns(new[] { new[] { quiet, quiet, quiet } }).ToList(); CollectionAssert.Contains(run, LandMath.Key(quiet, quiet), "the run describes the cell under the person");
                Assert.AreEqual(7, land.Version); Assert.AreEqual("Biblioteca", land.VoiceOffIn);
                at = new Vector3(500, 0, 500); land.Evaluate(false); Assert.IsNull(land.VoiceOffIn);
                at = Vector3.zero; land.Evaluate(false); Assert.AreEqual("Biblioteca", land.VoiceOffIn);
                land.ReadLand("{\"version\":8,\"parcels\":[]}"); Assert.IsNull(land.VoiceOffIn); Assert.AreEqual(8, land.Version);
                Assert.AreEqual(3L, LandMusic.VersionOf("{\"version\":3}")); Assert.IsNull(LandMusic.VersionOf("{}")); Assert.IsNull(LandMusic.VersionOf("oops"));

                var voice = host.AddComponent<VoiceChat>(); voice.ManualInput = true; voice.TalkHeld = true; voice.BlockedIn = "Biblioteca";
                var tone = Enumerable.Range(0, 3200).Select(i => Mathf.Sin(i * .3f) * .5f).ToArray(); voice.SendSamples(tone, 16000);
                Assert.IsFalse(voice.Talking, "nothing leaves a parcel with voice off");
                voice.BlockedIn = null; voice.SendSamples(tone, 16000); Assert.IsTrue(voice.Talking);
                Assert.AreEqual("A voz está desligada em Biblioteca.", VoiceChat.BlockedWords("Biblioteca"));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void ReadingTheLandAgainDoesNotStartTheSameTuneOver()
        {
            var host = new GameObject("music-test");
            try
            {
                var land = host.AddComponent<LandMusic>(); land.Setup(null, () => Guid.Empty, () => Vector3.zero);
                var fetched = 0; land.Fetcher = _ => { fetched++; return new TaskCompletionSource<byte[]>().Task; };   // a download that is still going on
                string Land(long version, string url) => "{\"version\":" + version + ",\"parcels\":[{\"id\":\"" + Guid.Empty + "\",\"name\":\"Palco\",\"media\":\"{\\\"musicUrl\\\":\\\"" + url + "\\\"}\",\"runs\":[[0,0,0]]}]}";
                land.ReadLand(Land(1, "https://example.com/a.mp3")); Assert.AreEqual(1, fetched); Assert.AreEqual("https://example.com/a.mp3", land.WantedUrl);
                land.ReadLand(Land(2, "https://example.com/a.mp3")); Assert.AreEqual(1, fetched, "the same tune goes on");
                land.ReadLand(Land(3, "https://example.com/b.mp3")); Assert.AreEqual(2, fetched, "another tune starts"); Assert.AreEqual("https://example.com/b.mp3", land.WantedUrl);
                land.ReadLand("{\"version\":4,\"parcels\":[]}"); Assert.AreEqual("", land.WantedUrl);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void TheConnectionIsSummedUpInWords()
        {
            var network = new LifeVerse.Unity.World.NetworkQuality(); Assert.AreEqual("ping —", network.Describe()); Assert.AreEqual(0, network.Jitter);
            network.Add(40, 0); network.Add(60, 2); network.Add(50, 3);
            Assert.AreEqual(50, network.Latest); Assert.AreEqual(50, network.Average); Assert.AreEqual(15, network.Jitter); Assert.AreEqual("ping 50 ms (média 50)   jitter 15 ms   perda 3%", network.Describe());
            for (var i = 0; i < 40; i++) network.Add(10, 0); Assert.AreEqual(LifeVerse.Unity.World.NetworkQuality.Window, network.Count); Assert.AreEqual(10, network.Average); Assert.AreEqual(0, network.Jitter);
            network.Clear(); Assert.AreEqual(0, network.Count);
        }

        [Test]
        public void TheVolumeTheDetailAndTheCacheOfTheSettingsActHere()
        {
            foreach (var id in new[] { "audio.interface", "network.ping", "perf.object_lod", "perf.resource_cache" }) Assert.IsTrue(SettingsCatalog.Find(id).IsAvailable, id);
            var volume = UiSounds.Volume; var lod = LifeVerse.Unity.World.WorldObjects.LodAt; var limit = LifeVerse.Unity.World.AssetPreloader.LimitBytes;
            try
            {
                var store = new SettingsStore(new UserSettings()); SettingsEffects.Resources(store);
                Assert.AreEqual(1f, UiSounds.Volume); Assert.AreEqual(LifeVerse.Unity.World.WorldObjects.LodFraction, LifeVerse.Unity.World.WorldObjects.LodAt); Assert.AreEqual(2048L * 1024 * 1024, LifeVerse.Unity.World.AssetPreloader.LimitBytes);
                store.Set("audio.interface", "0.25"); store.Set("perf.object_lod", "low"); store.Set("perf.resource_cache", "256"); SettingsEffects.Resources(store);
                Assert.AreEqual(.25f, UiSounds.Volume, 1e-4f); Assert.AreEqual(.1f, LifeVerse.Unity.World.WorldObjects.LodAt, 1e-4f); Assert.AreEqual(256L * 1024 * 1024, LifeVerse.Unity.World.AssetPreloader.LimitBytes);
                Assert.Greater(LifeVerse.Unity.World.WorldObjects.LodFractionOf("high"), LifeVerse.Unity.World.WorldObjects.LodFractionOf("medium"));
            }
            finally { UiSounds.Volume = volume; LifeVerse.Unity.World.WorldObjects.LodAt = lod; LifeVerse.Unity.World.AssetPreloader.LimitBytes = limit; }
        }

        [Test]
        public void EverySettingThatActsHereAndTheAccountKeepsFollowsTheAccount()
        {
            Assert.Greater(AccountSync.SettingIds.Length, 40, string.Join(",", AccountSync.SettingIds));
            foreach (var id in new[] { "controls.invert_y", "controls.always_run", "chat.opacity", "chat.font_size", "accessibility.reduce_motion", "controls.mouse_sensitivity", "camera.fov", "interface.mode", "voice.hearing_distance", "inventory.default_sort", "build.move_step" }) CollectionAssert.Contains(AccountSync.SettingIds, id);
            foreach (var id in new[] { "graphics.preset", "audio.master", "voice.input_device", "interface.scale", "interface.side_bar" }) CollectionAssert.DoesNotContain(AccountSync.SettingIds, id);
            var settings = new UserSettings(); var sent = ""; var user = Guid.NewGuid();
            var sync = new AccountSync(settings, (m, p, b) => { sent = b ?? sent; return Task.FromResult((200, "{\"revision\":2,\"values\":{}}")); }, () => user);
            // every value the client holds is one the platform accepts
            var push = sync.PushAllAsync(1); Assert.IsTrue(push.IsCompleted); Assert.AreEqual(SyncResult.Ok, push.Result.Result);
            using (var d = JsonDocument.Parse(sent))
            {
                var values = d.RootElement.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
                foreach (var id in AccountSync.SettingIds) Assert.IsTrue(values.ContainsKey(id), id + " is sent");
                var check = LifeVerse.Shared.Settings.AccountPreferenceRules.Check(values); Assert.IsTrue(check.Ok, string.Join("; ", check.Errors.Select(e => e.Id + " " + e.Message)));
            }
            // and what the account holds comes back into this client's settings
            var account = AccountSync.Parse(200, "{\"revision\":9,\"values\":{\"camera.fov\":90,\"interface.mode\":\"compact\",\"controls.mouse_sensitivity\":2,\"voice.spatial\":false,\"camera.zoom_step\":0.5,\"graphics.preset\":\"low\",\"chat.opacity\":500}}");
            Assert.AreEqual(5, account.Values.Count, string.Join(",", account.Values.Keys)); Assert.AreEqual(5, sync.UseAccount(account));
            var store = new SettingsStore(settings);
            Assert.AreEqual(90, store.Int("camera.fov")); Assert.AreEqual("compact", store.Get("interface.mode")); Assert.AreEqual(.3f, settings.MouseSensitivity, 1e-4f); Assert.IsFalse(store.Bool("voice.spatial")); Assert.AreEqual(.5f, store.Float("camera.zoom_step"), 1e-4f);
        }
    }
}
