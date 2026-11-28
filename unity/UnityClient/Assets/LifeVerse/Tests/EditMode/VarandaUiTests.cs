using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The Varanda interface: the sign of the place, the bar with the person in the middle, the sheet of menus, the speech bubbles and the day and night paper.</summary>
    public sealed class VarandaUiTests
    {
        private UiRoot _ui;
        [SetUp] public void Make() => _ui = UiRoot.Create(new UserSettings());
        [TearDown] public void Clean() { if (_ui != null) UnityEngine.Object.DestroyImmediate(_ui.gameObject); }

        private Hud Bar()
        {
            var hud = new Hud(_ui, new NotificationCenter(_ui));
            foreach (var id in new[] { "chat", "people", "inventory", "build", "move" }) hud.Register(id, id, () => null);
            hud.Register("Mais", Hud.FixedTool, () => { hud.ToggleSheet(); return null; });
            return hud;
        }

        [Test]
        public void ThePersonStandsInTheMiddleOfTheBarAndTheMenusButtonIsAlwaysLast()
        {
            var hud = Bar(); hud.ApplyLayout("full", false, new[] { Hud.FixedTool, "build" }, new[] { Hud.FixedTool, "move" });
            var names = hud.BottomBar.Children().Where(e => e.style.display != DisplayStyle.None).Select(e => e.name).ToList();
            CollectionAssert.AreEqual(new[] { "btn-build", "btn-chat", "me", "btn-people", "btn-inventory", "dock-divider", "btn-more" }, names);
            Assert.AreSame(hud.Presence, hud.BottomBar.Q<DropdownField>(), "the presence is under the face");
            hud.SetToolbarEdge("left"); Assert.AreEqual("me", hud.BottomBar[0].name, "on a side the face is at the top");
            hud.SetToolbarEdge("bottom"); Assert.AreNotEqual("me", hud.BottomBar[0].name);
            string dropped = null; hud.Reordered += (id, onto) => dropped = id + ">" + onto; hud.DropOnto("chat", Hud.FixedTool); Assert.IsNull(dropped, "the menus button does not move"); hud.DropOnto("chat", "build"); Assert.AreEqual("chat>build", dropped);
        }

        [Test]
        public void TheSheetShowsEveryMenuAndTheChoicesOfTheBar()
        {
            var hud = Bar(); var ran = "";
            hud.AddMenu("Eu", new (string, string, Action)[] { ("Meu perfil", "user", () => ran = "perfil"), (Hud.MenuLine, "", null), ("Sair da ilha", "power", null) });
            hud.AddMenu("Mundo", new (string, string, Action)[] { ("Tirar foto   F12", "photo", () => ran = "foto") });
            hud.ApplyLayout("full", false, null, new[] { "move" });
            string asked = null; hud.ToolShownAsked = (id, shown) => asked = id + " " + shown;
            Assert.IsNull(hud.OpenMenu); hud.ToggleSheet(); Assert.AreSame(hud.Sheet, hud.OpenMenu);
            CollectionAssert.AreEqual(new[] { "Eu", "Mundo" }, hud.MenuTitles);
            var photo = hud.Sheet.Q<Button>("item-Tirar foto"); Assert.IsNotNull(photo, "an entry with a key shows the words and the key"); CollectionAssert.AreEqual(new[] { "Tirar foto", "F12" }, photo.Query<Label>().ToList().Select(l => l.text));
            Assert.IsTrue(hud.Sheet.Q<Button>("chip-chat").ClassListContains("lv-btn-selected")); Assert.IsFalse(hud.Sheet.Q<Button>("chip-move").ClassListContains("lv-btn-selected")); Assert.IsNull(hud.Sheet.Q<Button>("chip-more"));
            Assert.IsNotNull(hud.Sheet.Q<Button>("sheet-edge-left")); Assert.IsNotNull(hud.Sheet.Q<Button>("sheet-mode-immersive"));
            hud.CloseSheet(); Assert.IsFalse(hud.Sheet.IsOpen); hud.OpenSheet(); hud.OpenSheet(); Assert.AreEqual(1, hud.Sheet.Query<Button>("menu-Eu").ToList().Count, "opening again draws it once");
            hud.CloseMenu(); Assert.IsNull(hud.OpenMenu); Assert.IsNull(asked); Assert.AreEqual("", ran);
        }

        [Test]
        public void TheSignAndTheTopShowWhoIsThereAndWhatWaits()
        {
            var hud = Bar();
            hud.SetPeople("Douglas", new[] { "Ana", "Beto", "Marina", "Caio", "Rui" }); Assert.AreEqual(5, hud.FacesShown, "four others and oneself");
            hud.SetPeople("Douglas", new string[0]); Assert.AreEqual(1, hud.FacesShown);
            Assert.AreEqual("", hud.BellText); hud.SetBell(3); Assert.AreEqual("3", hud.BellText); hud.SetBell(140); Assert.AreEqual("99+", hud.BellText); hud.SetBell(0); Assert.AreEqual("", hud.BellText);
            Assert.AreEqual(Faces.ColourOf("Ana"), Faces.ColourOf(" ana "), "a name keeps its colour"); Assert.AreEqual("A", Faces.InitialOf("ana")); Assert.AreEqual("?", Faces.InitialOf(" "));
        }

        [Test]
        public void WhatIsSaidComesInBubblesAndOnesOwnAtTheRight()
        {
            var chat = new ChatPanel(_ui.Settings) { IsMine = peer => peer == 1 };
            chat.Add(1, "eu", "olá <b>", DateTimeOffset.Now); chat.Add(2, "Ana", "oi", DateTimeOffset.Now); chat.AddSystem("aviso");
            var lines = chat.Query<VisualElement>(className: "lv-chat-line").ToList(); Assert.AreEqual(3, lines.Count);
            Assert.IsTrue(lines[0].ClassListContains("lv-chat-mine")); Assert.IsNull(lines[0].Q(className: "lv-face"), "no face on one's own");
            Assert.IsNotNull(lines[1].Q(className: "lv-face")); Assert.AreEqual("Ana", lines[1].Q<Label>(className: "lv-chat-by").text); Assert.IsNotNull(lines[1].Q<Label>(className: "lv-chat-when"));
            Assert.IsTrue(lines[2].ClassListContains("lv-chat-system")); Assert.IsNull(lines[2].Q<Label>(className: "lv-chat-when"));
            var said = lines[0].Q<Label>(className: "lv-chat-text"); Assert.AreEqual("olá <b>", said.text); Assert.IsFalse(said.enableRichText, "what people write is never read as formatting");
        }

        [Test]
        public void ThePaperTurnsDarkAtNightWithAMarginAtDusk()
        {
            Assert.IsFalse(_ui.Night); _ui.SetNight(true); Assert.IsTrue(_ui.Root.ClassListContains("lv-night")); _ui.SetNight(false); Assert.IsFalse(_ui.Root.ClassListContains("lv-night"));
            Assert.IsFalse(GameApp.NightFor(.4f, false), "by day it takes a darker sky to turn"); Assert.IsTrue(GameApp.NightFor(.3f, false)); Assert.IsTrue(GameApp.NightFor(.4f, true), "by night it stays until there is more light"); Assert.IsFalse(GameApp.NightFor(.6f, true));
            Assert.AreEqual("auto", SettingsCatalog.Find("interface.appearance").Default); Assert.IsTrue(SettingsCatalog.Find("interface.appearance").IsAvailable);
        }

        /// <summary>The words of both papers read on their surfaces (4.5 to 1), measured from the colours of the style sheet.</summary>
        [Test]
        public void BothPapersHaveEnoughContrast()
        {
            var theme = File.ReadAllText(Path.Combine(Application.dataPath, "LifeVerse/UI/Resources/LifeVerse/UI/Theme.uss"));
            foreach (var selector in new[] { ".lv-root, .unity-base-dropdown {", ".lv-root.lv-night, .lv-night .unity-base-dropdown {" })
            {
                var at = theme.IndexOf(selector, StringComparison.Ordinal); Assert.GreaterOrEqual(at, 0, selector); var block = theme.Substring(at, theme.IndexOf('}', at) - at);
                Color Of(string name) { var m = Regex.Match(block, "--lv-" + name + @": (#[0-9A-Fa-f]{6});"); Assert.IsTrue(m.Success, name); ColorUtility.TryParseHtmlString(m.Groups[1].Value, out var c); return c; }
                foreach (var (ink, surface) in new[] { ("ink", "paper"), ("ink", "sand"), ("ink", "raised"), ("soft", "paper"), ("soft", "sand"), ("faint", "paper"), ("faint", "raised"), ("on-fill", "sea"), ("on-fill", "clay"), ("clay-text", "paper"), ("sea-text", "paper"), ("sea-text", "sand"), ("good", "paper"), ("warn", "paper"), ("bad", "paper"), ("gold", "paper") })
                {
                    var ratio = Ratio(Of(ink), Of(surface)); Assert.GreaterOrEqual(ratio, 4.5f, $"{selector} {ink} on {surface}: {ratio:F2}");
                }
            }
        }

        private static float Ratio(Color a, Color b)
        {
            float L(Color c) { float F(float v) => v <= .03928f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f); return .2126f * F(c.r) + .7152f * F(c.g) + .0722f * F(c.b); }
            var x = L(a); var y = L(b); return (Mathf.Max(x, y) + .05f) / (Mathf.Min(x, y) + .05f);
        }
    }
}
