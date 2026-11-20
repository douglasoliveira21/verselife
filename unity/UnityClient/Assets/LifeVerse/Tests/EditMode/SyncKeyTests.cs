using System;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class SyncKeyTests
    {
        [Test]
        public void KeysRoundTripThroughTheNamesOfThePlatform()
        {
            foreach (var key in new[] { Key.W, Key.Z, Key.Space, Key.LeftShift, Key.LeftCtrl, Key.UpArrow, Key.Digit5, Key.Digit0, Key.F5, Key.Tab }) { var name = AccountSync.PlatformName(key); Assert.IsNotNull(name, key.ToString()); Assert.AreEqual(key, AccountSync.KeyOf(name), name); }
            Assert.AreEqual("Shift", AccountSync.PlatformName(Key.LeftShift)); Assert.AreEqual("Up", AccountSync.PlatformName(Key.UpArrow)); Assert.AreEqual(Key.W, AccountSync.KeyOf("w"));
            Assert.IsNull(AccountSync.PlatformName(Key.F10)); Assert.IsNull(AccountSync.PlatformName(Key.Numpad4)); Assert.AreEqual(Key.None, AccountSync.KeyOf("Kp 4")); Assert.AreEqual(Key.None, AccountSync.KeyOf("")); Assert.AreEqual(Key.None, AccountSync.KeyOf("F10"));
        }

        [Test]
        public void KeyNamesFollowThePlatformRule()
        {
            Assert.IsTrue(AccountSync.ValidKeyName("W")); Assert.IsTrue(AccountSync.ValidKeyName("")); Assert.IsTrue(AccountSync.ValidKeyName("Kp 4")); Assert.IsFalse(AccountSync.ValidKeyName("Escape")); Assert.IsFalse(AccountSync.ValidKeyName("enter")); Assert.IsFalse(AccountSync.ValidKeyName("F10")); Assert.IsFalse(AccountSync.ValidKeyName(new string('A', 25))); Assert.IsFalse(AccountSync.ValidKeyName("a<b")); Assert.IsFalse(AccountSync.ValidKeyName("é"));
            using var d = JsonDocument.Parse("{\"a\":\"Space\",\"b\":5,\"c\":\"Escape\"}"); var root = d.RootElement;
            Assert.AreEqual("Space", AccountSync.Check("controls.jump.1", root.GetProperty("a"))); Assert.IsNull(AccountSync.Check("controls.jump.1", root.GetProperty("b"))); Assert.IsNull(AccountSync.Check("controls.jump.1", root.GetProperty("c")));
        }

        [Test]
        public void TheAccountsKeyBecomesTheKeyOfTheAction()
        {
            try
            {
                KeyBindings.Reset(); var settings = new UserSettings(); var sync = new AccountSync(settings, (m, p, b) => Task.FromResult((200, "{}")), () => Guid.NewGuid());
                Assert.AreEqual("Space", sync.Value(AccountSync.KeyId("jump"))); sync.Apply(AccountSync.KeyId("jump"), "K"); Assert.AreEqual(Key.K, KeyBindings.Of(GameAction.Jump)); Assert.AreEqual("K", sync.Value(AccountSync.KeyId("jump"))); Assert.IsTrue(settings.Keys.ContainsKey("Jump"), "kept in the settings file too");
                sync.Apply(AccountSync.KeyId("run"), "Kp 4"); Assert.AreEqual(KeyBindings.DefaultOf(GameAction.Run), KeyBindings.Of(GameAction.Run), "a key this game cannot name changes nothing"); sync.Apply(AccountSync.KeyId("run"), "");
                Assert.AreEqual(KeyBindings.DefaultOf(GameAction.Run), KeyBindings.Of(GameAction.Run)); CollectionAssert.Contains(AccountSync.SyncedIds, "controls.move_forward.1"); CollectionAssert.Contains(AccountSync.SyncedIds, "chat.opacity"); CollectionAssert.DoesNotContain(AccountSync.SyncedIds, "controls.jump.2");
            }
            finally { KeyBindings.Reset(); }
        }
    }
}
