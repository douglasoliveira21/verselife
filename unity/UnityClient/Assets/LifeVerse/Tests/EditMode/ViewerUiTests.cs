using System.IO;
using System.Linq;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The interface in the style of the Second Life viewer: the bars, the toolbar, the floating windows, the movement and camera controls and the textures they are drawn with.</summary>
    public sealed class ViewerUiTests
    {
        private UiRoot _ui;
        [SetUp] public void Make() => _ui = UiRoot.Create(new UserSettings());
        [TearDown] public void Clean() { if (_ui != null) Object.DestroyImmediate(_ui.gameObject); }

        [Test]
        public void EveryTextureTheThemeAsksForExistsAndKeepsItsPixels()
        {
            var theme = File.ReadAllText(Path.Combine(Application.dataPath, "LifeVerse/UI/Resources/LifeVerse/UI/Theme.uss"));
            var asked = System.Text.RegularExpressions.Regex.Matches(theme, "resource\\(\"(LifeVerse/UI/Skin/[a-z0-9-]+)\"\\)").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.Greater(asked.Count, 25);
            foreach (var path in asked)
            {
                var texture = Resources.Load<Texture2D>(path); Assert.IsNotNull(texture, path);
                Assert.AreEqual(1, texture.mipmapCount, "no mip maps: " + path); Assert.AreEqual(TextureFormat.RGBA32, texture.format, "not compressed: " + path);
            }
        }

        [Test]
        public void TheHudHasAMenuBarANavigationBarAndAToolbarOfIconsOverWords()
        {
            var hud = new Hud(_ui, new NotificationCenter(_ui));
            Assert.AreSame(hud.TopBar, hud.MenuBar.parent); Assert.AreSame(hud.TopBar, hud.NavigationBar.parent);
            var build = hud.Register("Construir", "build", () => null);
            Assert.IsTrue(build.ClassListContains("lv-tool")); Assert.AreEqual("Construir", build.text); Assert.AreEqual("hammer", UiKit.IconOf(build)); Assert.IsTrue(((UiKit.ButtonIcon)build.userData).Stacked);
            UiKit.IconOnly(build, true); Assert.IsTrue(build.ClassListContains("lv-tool-compact")); UiKit.IconOnly(build, false); Assert.IsFalse(build.ClassListContains("lv-tool-compact"));
            UiKit.Alert(build, true); Assert.IsTrue(((UiKit.ButtonIcon)build.userData).Alert); UiKit.Alert(build, false); Assert.IsFalse(((UiKit.ButtonIcon)build.userData).Alert);
            hud.SetIsland("Praia"); hud.SetPosition(new Vector3(1.4f, 2.6f, -3f)); Assert.AreEqual("Praia", hud.IslandText); Assert.AreEqual("1, 3, -3", hud.PositionText); hud.SetPosition(null); Assert.AreEqual("", hud.PositionText);
            string asked = null; hud.SearchAsked = text => asked = text; hud.Search.value = "  praia  "; hud.RunSearch(); Assert.AreEqual("praia", asked); Assert.AreEqual("", hud.Search.value);
            Assert.AreEqual("Disponível", Hud.PresenceWords("online")); Assert.AreEqual("Ocupado", Hud.PresenceWords("busy")); Assert.AreEqual("online", hud.Presence.value, "the value stays the platform's");
            Assert.IsFalse(Hud.DefaultHidden.Contains("chat"), "the chat button is on the bar, as in the viewer"); Assert.IsFalse(Hud.DefaultHidden.Contains("move"));
        }

        [Test]
        public void AWindowMinimizesToItsTitleResizesWithinLimitsAndTheOneOnTopIsMarked()
        {
            var one = new FloatingWindow(_ui, "um", "Um", 400f, 300f); var two = new FloatingWindow(_ui, "dois", "Dois"); one.Open(); two.Open();
            Assert.IsTrue(two.ClassListContains("lv-window-front") && !one.ClassListContains("lv-window-front"));
            one.Raise(); Assert.IsTrue(one.ClassListContains("lv-window-front") && !two.ClassListContains("lv-window-front"));
            one.Close(); Assert.IsTrue(two.ClassListContains("lv-window-front"), "the next one takes its place");
            one.Open(); one.SetMinimized(true); Assert.AreEqual(DisplayStyle.None, one.Body.style.display.value); Assert.AreEqual(StyleKeyword.Auto, one.style.height.keyword);
            one.SetMinimized(false); Assert.AreEqual(DisplayStyle.Flex, one.Body.style.display.value); Assert.AreEqual(300f, one.style.height.value.value);
            Assert.IsTrue(one.Resizable); Assert.IsFalse(two.Resizable, "a window as tall as its contents has no corner to drag");
            one.Resize(new Vector2(10f, 10f)); Assert.AreEqual(FloatingWindow.MinWidth, one.style.width.value.value); Assert.AreEqual(FloatingWindow.MinHeight, one.style.height.value.value);
            Assert.GreaterOrEqual(two.style.top.value.value, FloatingWindow.TopInset, "a new window opens under the bars");
        }

        [Test]
        public void FromInsideTheHeadTheBodyStaysAndTheFaceFollowsItsSliders()
        {
            var view = AvatarView.Create(1, "eu", Color.cyan); var host = view.gameObject;
            try
            {
                var wide = AvatarAppearance.Parse("{\"faceSettingsJson\":\"{\\\"mouthWidth\\\":1.2,\\\"noseSize\\\":0.8,\\\"headWidth\\\":1.1,\\\"browHeight\\\":1.1,\\\"jawWidth\\\":0.9}\"}");
                view.ApplyAppearance(wide);
                var head = view.Head; Assert.AreEqual(.36f, head.Find("Mouth").localScale.x, 1e-4f); Assert.AreEqual(.096f, head.Find("Nose").localScale.x, 1e-4f); Assert.AreEqual(.286f, head.localScale.x, 1e-4f);
                Assert.AreEqual(.297f, head.Find("Brow").localPosition.y, 1e-4f); Assert.AreEqual(.666f, head.Find("Jaw").localScale.x, 1e-4f);
                view.SetFirstPersonView(true); Assert.IsFalse(view.HeadShown); Assert.IsFalse(head.Find("Mouth").GetComponent<Renderer>().enabled);
                var torso = host.transform.Find("Body/Torso"); Assert.IsTrue(torso.GetComponent<Renderer>().enabled, "the body is still there");
                view.ApplyAppearance(wide); foreach (var part in head.GetComponentsInChildren<Renderer>()) Assert.IsFalse(part.enabled, "dressed again from inside, the head stays hidden: " + part.name);
                view.SetFirstPersonView(false); Assert.IsTrue(view.HeadShown && torso.GetComponent<Renderer>().enabled);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void TheChatKeepsATabForEachConversation()
        {
            var chat = new ChatPanel(_ui.Settings); var asked = new System.Collections.Generic.List<ChatTab>(); chat.Load = asked.Add;
            var ana = System.Guid.NewGuid(); var club = System.Guid.NewGuid();
            var tab = chat.Open(ana, false, "Ana"); Assert.AreSame(tab, chat.Active); Assert.AreEqual(1, asked.Count);
            chat.Fill(tab, new[] { ("Ana", "oi", false, (System.DateTimeOffset?)null), ("Você", "olá", true, null) }); CollectionAssert.AreEqual(new[] { "Ana: oi", "Você: olá" }, tab.Shown);
            var group = chat.Arrived(club, true, "Clube"); Assert.AreSame(tab, chat.Active, "what arrives does not take the screen"); Assert.AreEqual(DisplayStyle.None, group.Log.style.display.value, "nor is it drawn over the one on screen"); Assert.AreEqual(1, group.Unread); Assert.AreEqual("Clube (1)", group.Choose.text);
            chat.Select(group); Assert.AreEqual(0, group.Unread); Assert.AreEqual("Clube", group.Choose.text);
            string said = null; chat.SendConversation = (to, text, done) => { said = to.Name + "|" + text; done(false); };
            chat.Input.value = " vamos "; Assert.IsTrue(chat.Submit()); Assert.AreEqual("Clube|vamos", said); Assert.AreEqual(" vamos ", chat.Input.value, "what did not go stays to be sent again");
            chat.SendConversation = (to, text, done) => done(true); var loads = asked.Count; Assert.IsTrue(chat.Submit()); Assert.AreEqual("", chat.Input.value); Assert.AreEqual(loads + 1, asked.Count);
            chat.CloseTab(group); Assert.IsNull(chat.Active); Assert.AreEqual(1, chat.Tabs.Count);
            string nearby = null; chat.Send = text => { nearby = text; return true; }; chat.Input.value = "aqui perto"; chat.Submit(); Assert.AreEqual("aqui perto", nearby);
            for (var i = 0; i < 10; i++) chat.Open(System.Guid.NewGuid(), false, "p" + i); Assert.AreEqual(ChatPanel.MaxTabs, chat.Tabs.Count);
        }

        [Test]
        public void AResizedWindowOpensAgainAtItsSizeAndTheToolbarCanStandAtASide()
        {
            var window = new FloatingWindow(_ui, "tamanho", "T", 400f, 300f); window.Open(); window.Resize(new Vector2(620f, 410f)); window.Close();
            Assert.AreEqual(4, _ui.Settings.Windows["tamanho"].Length);
            var again = new FloatingWindow(_ui, "tamanho", "T", 400f, 300f); Assert.AreEqual(620f, again.style.width.value.value); Assert.AreEqual(410f, again.style.height.value.value);
            var plain = new FloatingWindow(_ui, "sem", "S", 300f, 200f); plain.Open(); plain.Close(); Assert.AreEqual(2, _ui.Settings.Windows["sem"].Length, "never resized: only the place is kept");
            var hud = new Hud(_ui, new NotificationCenter(_ui)); var build = hud.Register("Construir", "build", () => null);
            hud.SetToolbarEdge("left"); hud.ApplyLayout("full", false, null, null); Assert.IsTrue(hud.BottomBar.ClassListContains("lv-toolbar-side")); Assert.IsTrue(((UiKit.ButtonIcon)build.userData).Only); Assert.AreEqual(Hud.Edge + Hud.SideBarWidth, hud.Chat.style.left.value.value);
            hud.SetToolbarEdge("right"); Assert.AreEqual(Hud.Edge + Hud.SideBarWidth, hud.Map.style.right.value.value);
            hud.SetToolbarEdge("nada"); hud.ApplyLayout("full", false, null, null); Assert.AreEqual("bottom", hud.ToolbarEdge); Assert.IsFalse(((UiKit.ButtonIcon)build.userData).Only);
        }

        [Test]
        public void TheFourSettingsThatOnlyNeededTheClientAct()
        {
            foreach (var id in new[] { "network.download_limit", "metrics.cpu_gpu", "advanced.verify_files", "accessibility.captions" }) { var setting = SettingsCatalog.Find(id); Assert.IsTrue(setting.IsAvailable, id); }
            Assert.AreEqual(100, SettingsCatalog.Find("network.download_limit").Max);
            // the limit: a second of data per second, shared, and none without a limit
            double free = 0; Assert.AreEqual(0, LifeVerse.Unity.World.DownloadThrottle.Delay(5000, 0, 1, ref free)); Assert.AreEqual(.5, LifeVerse.Unity.World.DownloadThrottle.Delay(500, 1000, 1, ref free), 1e-9); Assert.AreEqual(1.0, LifeVerse.Unity.World.DownloadThrottle.Delay(500, 1000, 1, ref free), 1e-9); Assert.AreEqual(.1, LifeVerse.Unity.World.DownloadThrottle.Delay(100, 1000, 9, ref free), 1e-9);
            Assert.AreEqual("CPU 50 % do quadro   GPU 100 %", SettingsEffects.Busy(8, 40, 16)); Assert.AreEqual("CPU e GPU: sem medida ainda", SettingsEffects.Busy(1, 1, 0));
            var captions = new Captions(); captions.Say("Ana está falando", 0f); captions.Say("Ana está falando", 1f); captions.Say(Captions.Sound(false, 5f), 1f); CollectionAssert.AreEqual(new[] { "Ana está falando", "♪ Som de um objeto a 5 m" }, captions.Lines);
            captions.Say("b", 2f); captions.Say("c", 2f); Assert.AreEqual(Captions.MaxLines, captions.Lines.Count); captions.Tick(1f + Captions.Seconds); Assert.AreEqual(2, captions.Lines.Count); captions.Tick(100f); Assert.AreEqual(0, captions.Lines.Count);
        }

        [Test]
        public void TheGameFilesAreCheckedAgainstTheirListAndDamagedDownloadsAreRemoved()
        {
            var folder = Path.Combine(Path.GetTempPath(), "lv-files-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "dados")); File.WriteAllText(Path.Combine(folder, "jogo.exe"), "um"); File.WriteAllText(Path.Combine(folder, "dados", "a.bin"), "dois");
                Assert.IsNull(LifeVerse.Unity.World.GameFiles.Verify(folder), "no list yet"); Assert.AreEqual(2, LifeVerse.Unity.World.GameFiles.Write(folder));
                var good = LifeVerse.Unity.World.GameFiles.Verify(folder).Value; Assert.AreEqual(2, good.Checked); Assert.AreEqual(0, good.Bad.Count);
                File.WriteAllText(Path.Combine(folder, "dados", "a.bin"), "mudou"); File.Delete(Path.Combine(folder, "jogo.exe"));
                CollectionAssert.AreEquivalent(new[] { "dados/a.bin", "jogo.exe" }, LifeVerse.Unity.World.GameFiles.Verify(folder).Value.Bad);
                var cache = Path.Combine(folder, "cache"); Directory.CreateDirectory(cache); var bytes = System.Text.Encoding.UTF8.GetBytes("conteudo");
                string hash; using (var sha = System.Security.Cryptography.SHA256.Create()) hash = System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                File.WriteAllBytes(Path.Combine(cache, hash + ".bin"), bytes); File.WriteAllText(Path.Combine(cache, new string('0', 64) + ".bin"), "estragado");
                var found = LifeVerse.Unity.World.GameFiles.VerifyCache(cache); Assert.AreEqual((2, 1), found); Assert.IsTrue(File.Exists(Path.Combine(cache, hash + ".bin")));
                StringAssert.Contains("1 estragado(s) apagado(s)", LifeVerse.Unity.World.GameFiles.Words(null, found));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void TheMovementButtonsAddToTheKeyboard()
        {
            var keys = new PlayerInputSample { Forward = 1f, Right = -1f }; var pad = new PlayerInputSample { Forward = 1f, Run = true, Jump = true };
            var both = LocalPlayer.Merge(keys, pad); Assert.AreEqual(1f, both.Forward, "never past one"); Assert.AreEqual(-1f, both.Right); Assert.IsTrue(both.Run && both.Jump && !both.Descend);
            var window = new MoveWindow(_ui, () => null, () => null);
            window.Forward.Set(true); Assert.AreEqual(1f, window.Sample().Forward); Assert.IsFalse(window.Sample().Run); window.SetRunning(true); Assert.IsTrue(window.Sample().Run);
            window.Back.Set(true); Assert.AreEqual(0f, window.Sample().Forward, "both arrows cancel"); window.Forward.Set(false); window.Back.Set(false); Assert.IsFalse(window.Sample().Run, "running only while walking");
        }

        [Test]
        public void TheCameraButtonsTurnTiltAndComeCloserWithinLimits()
        {
            var host = new GameObject("camera");
            try
            {
                var camera = host.AddComponent<FollowCamera>(); camera.Yaw = 0f; camera.Pitch = 0f; camera.Distance = 6f;
                var window = new CameraWindow(_ui, () => camera, () => 40f);
                window.Right.Set(true); window.Move(1f); window.Right.Set(false); Assert.AreEqual(CameraWindow.TurnDegreesPerSecond, camera.Yaw, 1e-3f);
                window.Up.Set(true); window.Move(10f); window.Up.Set(false); Assert.AreEqual(80f, camera.Pitch, 1e-3f);
                window.Closer.Set(true); window.Move(10f); window.Closer.Set(false); Assert.AreEqual(FollowCamera.MinDistance, camera.Distance, 1e-3f);
                window.View(180f); Assert.AreEqual(220f, camera.Yaw, 1e-3f);
                window.Reset(); Assert.AreEqual(40f, camera.Yaw, 1e-3f); Assert.AreEqual(CameraWindow.DefaultDistance, camera.Distance, 1e-3f);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void AHintSaysWhatTheButtonDoesNotAlreadySay()
        {
            var same = new Button { text = "Mapa", tooltip = "Mapa" }; var other = new Button { text = "", tooltip = "Buscar" }; var child = new VisualElement(); other.Add(child);
            Assert.AreEqual("", Tooltips.Of(same, out _)); Assert.AreEqual("Buscar", Tooltips.Of(child, out var owner)); Assert.AreSame(other, owner);
            UiKit.Stacked(same, "globe"); UiKit.IconOnly(same, true); Assert.AreEqual("Mapa", Tooltips.Of(same, out _), "with only the icon showing, the words are the hint");
        }

        [Test]
        public void TheSectionsAndTheKindsOfItemsHaveTheirIcons()
        {
            Assert.AreEqual("globe", FloatingWindow.IconOf("world-map")); Assert.AreEqual("code", FloatingWindow.IconOf("lua-1234")); Assert.AreEqual("texture", FloatingWindow.IconOf("build-texture")); Assert.IsNull(FloatingWindow.IconOf("zzz"));
            foreach (var id in new[] { "avatar-studio", "character", "inventory-manager", "island-management", "profile-hub", "wallet-hub", "settings-catalog", "global-search", "notification-center", "real-estate", "teleport-invite-1" }) Assert.IsTrue(IconShapes.Has(FloatingWindow.IconOf(id)), id);
            foreach (var section in InventoryManagerWindow.Sections.Concat(IslandManagementWindow.Sections).Concat(GlobalSearchWindow.Sections).Concat(WalletHubWindow.Sections).Concat(ProfileHubWindow.Sections))
            { var icon = SectionWindow.IconOfSection(section.Id); Assert.AreNotEqual("caret-right", icon, section.Id); Assert.IsTrue(IconShapes.Has(icon), section.Id + " " + icon); }
            foreach (var category in SettingsCatalog.Categories) Assert.IsTrue(IconShapes.Has(SectionWindow.IconOfSection(category.Id)), category.Id);
            foreach (var kind in new[] { "objects", "textures", "sounds", "animations", "clothing", "accessories", "models", "scripts", "packages", "packs", "?" }) Assert.IsTrue(IconShapes.Has(InventoryManagerWindow.IconOfCategory(kind)), kind);
            foreach (var icon in Hud.Icons.Values) Assert.IsTrue(IconShapes.Has(icon), icon);
        }
    }
}
