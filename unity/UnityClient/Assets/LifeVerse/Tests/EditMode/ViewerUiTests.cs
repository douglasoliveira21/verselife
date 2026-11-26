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
