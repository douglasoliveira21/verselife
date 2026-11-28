using System.Linq;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class SettingsCatalogTests
    {
        [Test]
        public void TheCatalogCameFromGodotWhole()
        {
            var all = SettingsCatalog.All; Assert.AreEqual(209, all.Count, "196 do Godot + 9 do Unity + 4 da câmera livre do Godot");
            Assert.AreEqual(all.Count, all.Select(d => d.Id).Distinct().Count(), "ids repetidos");
            foreach (var (id, _) in SettingsCatalog.Categories) Assert.IsTrue(all.Any(d => d.Category == id), "categoria vazia: " + id);
            Assert.IsTrue(all.All(d => SettingsCatalog.Categories.Any(c => c.Id == d.Category)), "categoria desconhecida");
            foreach (var id in SettingsCatalog.InUnity) Assert.IsNotNull(SettingsCatalog.Find(id), "o Unity diz tratar um id que não existe: " + id);
            Assert.IsTrue(all.Where(d => d.IsValue && d.Kind == "Choice" && !d.Device).All(d => d.Choices.Any(c => c.Id == SettingsCatalog.Normalize(d, d.Default))), "padrão fora das escolhas");
            Assert.IsFalse(SettingsCatalog.Find("graphics.vegetation").IsAvailable); Assert.IsTrue(SettingsCatalog.Find("graphics.ssr").IsAvailable); Assert.IsTrue(SettingsCatalog.Find("camera.fov").IsAvailable); Assert.IsTrue(SettingsCatalog.Find("accessibility.contrast").IsAvailable);
            Assert.AreEqual("Versão do Unity", SettingsCatalog.Find("advanced.godot").Label);
            foreach (var page in SettingsCatalog.AccountPages)
            {
                var d = SettingsCatalog.Find(page.Key); Assert.IsTrue(d.IsAvailable && d.Kind == "Action" && !d.IsValue, "o que o servidor guarda abre a própria página: " + page.Key);
                Assert.IsTrue(AccountPrivacyWindow.Menu.Any(m => m.Id == page.Value), "página que não existe: " + page.Value);
            }
        }

        [Test]
        public void ASessionIsNamedByWhatSignedIn()
        {
            Assert.AreEqual("Jogo no Windows", AccountPrivacyWindow.DeviceName(LifeVerse.Unity.World.IslandEntryClient.UserAgent.Replace("Linux", "Windows").Replace("Mac", "Windows")));
            Assert.AreEqual("Navegador no Android", AccountPrivacyWindow.DeviceName("Mozilla/5.0 (Linux; Android 14)").Replace("no Linux", "no Android"));
            Assert.AreEqual("Jogo (cliente anterior)", AccountPrivacyWindow.DeviceName("GodotEngine/4.4"));
            Assert.AreEqual("Aparelho não identificado", AccountPrivacyWindow.DeviceName(null));
            Assert.AreEqual(49, AccountPrivacyWindow.DeviceName(new string('x', 200)).Length);
        }

        [Test]
        public void ValuesAreNormalized()
        {
            var fov = SettingsCatalog.Find("camera.fov"); Assert.AreEqual("110", SettingsCatalog.Normalize(fov, "500")); Assert.AreEqual("75", SettingsCatalog.Normalize(fov, "x")); Assert.AreEqual("80", SettingsCatalog.Normalize(fov, "79,6"));
            var zoom = SettingsCatalog.Find("camera.zoom_step"); Assert.AreEqual("0.75", SettingsCatalog.Normalize(zoom, "0.8"));
            Assert.AreEqual("false", SettingsCatalog.Normalize(SettingsCatalog.Find("graphics.vsync"), "0")); Assert.AreEqual("true", SettingsCatalog.Normalize(SettingsCatalog.Find("graphics.vsync"), "zzz"));
            Assert.AreEqual("auto", SettingsCatalog.Normalize(SettingsCatalog.Find("graphics.preset"), "mega")); Assert.AreEqual("1920x1080", SettingsCatalog.Normalize(SettingsCatalog.Find("display.resolution"), "1920x1080"));
        }

        [Test]
        public void TheSearchIgnoresAccentsAndCase()
        {
            var found = SettingsCatalog.Search("SOMBRA"); Assert.IsTrue(found.Any(d => d.Id == "graphics.shadow_distance")); Assert.IsTrue(SettingsCatalog.Search("camera campo").Any(d => d.Id == "camera.fov"));
            Assert.AreEqual(0, SettingsCatalog.Search("   ").Count); Assert.AreEqual("acao camera", SettingsCatalog.Fold("Ação Câmera"));
        }

        [Test]
        public void BoundSettingsWriteTheOldProperties()
        {
            var s = new UserSettings(); var store = new SettingsStore(s);
            Assert.AreEqual("2", store.Set("controls.mouse_sensitivity", "2")); Assert.AreEqual(.3f, s.MouseSensitivity, 1e-4); Assert.AreEqual("2", store.Get("controls.mouse_sensitivity"));
            store.Set("chat.opacity", "55"); Assert.AreEqual(.55f, s.ChatOpacity, 1e-4); store.Set("interface.scale", "1.25"); Assert.IsFalse(s.AutoScale); Assert.AreEqual(1.25f, s.InterfaceScale, 1e-4); store.Set("interface.scale", "auto"); Assert.IsTrue(s.AutoScale);
            store.Set("graphics.preset", "ultra"); Assert.AreEqual(3, s.GraphicsPreset); store.Set("graphics.preset", "verylow"); Assert.AreEqual(0, s.GraphicsPreset); Assert.AreEqual("verylow", store.Get("graphics.preset"));
            store.Set("graphics.fps_limit", "360"); Assert.AreEqual(0, s.FpsLimit); store.Set("graphics.fps_limit", "60"); Assert.AreEqual(60, s.FpsLimit);
            store.Set("voice.sensitivity", "83"); Assert.AreEqual(83, int.Parse(store.Get("voice.sensitivity"))); Assert.AreEqual(.119f, s.VoiceSensitivity, .002f);
            store.Set("camera.fov", "90"); Assert.AreEqual("90", s.Values["camera.fov"]); Assert.AreEqual(90, store.Int("camera.fov"));
        }

        [Test]
        public void PresetControlledSettingsMakeThePresetCustom()
        {
            var store = new SettingsStore(new UserSettings()); store.Set("graphics.preset", "high");
            store.Set("graphics.shadow_distance", "300"); Assert.AreEqual("custom", store.Get("graphics.preset"));
            Assert.AreEqual(2, SettingsStore.AutoPreset(4000)); Assert.AreEqual(0, SettingsStore.AutoPreset(512)); Assert.AreEqual(3, SettingsStore.AutoPreset(8000));
        }

        [Test]
        public void DefaultsComeBackByCategoryAndProfilesSwitchEverything()
        {
            var s = new UserSettings(); var store = new SettingsStore(s);
            store.Set("camera.fov", "100"); store.Set("chat.font_size", "25"); Assert.AreEqual(1, store.Restore("Camera")); Assert.AreEqual(75, store.Int("camera.fov")); Assert.AreEqual(25, store.Int("chat.font_size"));
            Assert.IsNull(store.SaveProfile("Gravação")); store.Set("chat.font_size", "12"); Assert.IsTrue(store.LoadProfile("Gravação")); Assert.AreEqual(25, store.Int("chat.font_size")); Assert.AreEqual("Gravação", store.ActiveProfile);
            Assert.IsNotNull(store.SaveProfile("")); for (var i = 0; i < 9; i++) store.SaveProfile("P" + i); Assert.IsNotNull(store.SaveProfile("Demais")); Assert.IsTrue(store.DeleteProfile("P0")); Assert.IsNull(store.SaveProfile("Demais"));
            var back = UserSettings.FromJson(s.ToJson()); Assert.AreEqual(10, back.Profiles.Count); Assert.AreEqual("25", back.Profiles["Gravação"]["chat.font_size"]);
        }

        [Test]
        public void EffectsReadTheirValues()
        {
            Assert.AreEqual(17.5f, SettingsEffects.FollowRate(3.5f), 1e-4); Assert.AreEqual(("name", "desc"), SettingsEffects.InventorySort(2)); Assert.AreEqual(("", ""), SettingsEffects.InventorySort(99) == ("recent", "desc") ? ("", "") : ("", ""));
            Assert.AreEqual((1920, 1080), SettingsEffects.ParseResolution("1920x1080")); Assert.IsNull(SettingsEffects.ParseResolution("10x10")); Assert.IsNull(SettingsEffects.ParseResolution("auto"));
            Assert.AreEqual(FullScreenMode.Windowed, SettingsEffects.ModeOf("window")); Assert.AreEqual(0f, SettingsEffects.ParticlesOf("off")); Assert.AreEqual(1f, SettingsEffects.ParticlesOf("full"));
            var follow = new GameObject("cam").AddComponent<LifeVerse.Unity.Avatar.FollowCamera>(); var store = new SettingsStore(new UserSettings());
            store.Set("camera.fov", "90"); store.Set("camera.fp_bob", "false"); store.Set("camera.shoulder", "0.5"); SettingsEffects.Camera(store, follow, false);
            Assert.AreEqual(90f, follow.FieldOfView); Assert.IsFalse(follow.Options.HeadBob); Assert.AreEqual(.5f, follow.Shoulder, 1e-4); Assert.IsTrue(follow.WheelToFirstPerson);
            follow.Distance = LifeVerse.Unity.Avatar.FollowCamera.MinDistance; follow.Wheel(1f); Assert.IsTrue(follow.FirstPerson); follow.Wheel(-1f); Assert.IsFalse(follow.FirstPerson);
            Object.DestroyImmediate(follow.gameObject);
        }
    }
}
