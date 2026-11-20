using System.Linq;
using LifeVerse.Unity.Audio;
using LifeVerse.Unity.Avatar;
using UnityEngine.InputSystem;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class AudioTests
    {
        [Test]
        public void TheVolumeOfASoundIsTheGroupsTimesTheGeneralOne()
        {
            Buses.SetAll(.5f, 1f, .8f, .6f, 1f, .4f);
            try
            {
                Assert.AreEqual(.4f, Buses.Gain(Bus.Effects), 1e-5f); Assert.AreEqual(.3f, Buses.Gain(Bus.Music), 1e-5f); Assert.AreEqual(.2f, Buses.Gain(Bus.Voice), 1e-5f);
                Buses.SetAll(9f, -3f, 1f, 1f, 1f, 1f); Assert.AreEqual(1f, Buses.Master); Assert.AreEqual(0f, Buses.Of(Bus.Ambient), "um valor fora dos limites é trazido para dentro");
                Assert.AreEqual(1f, Buses.DbToLinear(0f), 1e-5f); Assert.AreEqual(.5f, Buses.DbToLinear(-6.0206f), 1e-3f);
            }
            finally { Buses.SetAll(1f, 1f, 1f, 1f, 1f, 1f); }
        }

        [Test]
        public void TheSoundscapeFollowsThePlaceAndTheWeather()
        {
            Assert.Greater(AmbientLevels.Compute(0f, false, 0f, "clear").Ocean, .99f); Assert.Less(AmbientLevels.Compute(300f, false, 0f, "clear").Ocean, .01f); Assert.Greater(AmbientLevels.Compute(300f, true, 0f, "clear").Ocean, .99f, "na água o mar é inteiro");
            Assert.Greater(AmbientLevels.Compute(300f, false, 30f, "clear").Wind, AmbientLevels.Compute(300f, false, 0f, "clear").Wind, "o vento sobe com a altura"); Assert.Greater(AmbientLevels.Compute(300f, false, 0f, "storm").Wind, AmbientLevels.Compute(300f, false, 0f, "rain").Wind);
            Assert.AreEqual(1f, AmbientLevels.Compute(0f, false, 0f, "storm").Rain); Assert.AreEqual(0f, AmbientLevels.Compute(0f, false, 0f, "fog").Rain);
        }

        [Test]
        public void FilesAreKnownBySheerContentAndNotByName()
        {
            var wav = new byte[44]; System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0); System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
            var ogg = new byte[32]; System.Text.Encoding.ASCII.GetBytes("OggS").CopyTo(ogg, 0); var id3 = new byte[32]; System.Text.Encoding.ASCII.GetBytes("ID3").CopyTo(id3, 0); var frame = new byte[32]; frame[0] = 0xFF; frame[1] = 0xFB;
            Assert.AreEqual("wav", AudioDecode.Sniff(wav)); Assert.AreEqual("ogg", AudioDecode.Sniff(ogg)); Assert.AreEqual("mp3", AudioDecode.Sniff(id3)); Assert.AreEqual("mp3", AudioDecode.Sniff(frame));
            Assert.IsNull(AudioDecode.Sniff(new byte[64])); Assert.IsNull(AudioDecode.Sniff(null)); Assert.IsNull(AudioDecode.Sniff(new byte[4]), "curto demais para ser um som");
            Assert.IsNull(AudioDecode.Refuse("https://exemplo.com/a.mp3")); Assert.IsNotNull(AudioDecode.Refuse("http://exemplo.com/a.mp3")); Assert.IsNotNull(AudioDecode.Refuse("https://localhost/a.mp3")); Assert.IsNotNull(AudioDecode.Refuse("https://exemplo.com/a.exe"));
        }

        [Test]
        public void ParcelMusicRefusalsAreSentencesAndTheVoiceBuffersBeforeItPlays()
        {
            Assert.IsTrue(LandMusic.RefusalText("not_https").Contains("https")); Assert.IsTrue(LandMusic.RefusalText("bad_type").Contains("mp3")); Assert.IsNotEmpty(LandMusic.RefusalText("outro"));
            var host = new GameObject("voz"); try
            {
                var voice = host.AddComponent<VoiceOut>(); Assert.AreEqual(0, voice.Buffered);
                voice.Push(new short[LifeVerse.Shared.Voice.VoiceSettings.FrameSamples]); Assert.IsTrue(voice.Render(256).All(s => s == 0f), "com menos de três quadros ainda não toca");
                var loud = Enumerable.Repeat((short)8000, LifeVerse.Shared.Voice.VoiceSettings.FrameSamples).ToArray(); for (var i = 0; i < 4; i++) voice.Push(loud); var out_ = voice.Render(4096); Assert.IsTrue(out_.Any(s => s > .1f), "com o colchão cheio a voz sai");
                for (var i = 0; i < 60; i++) voice.Push(loud); Assert.LessOrEqual(voice.Buffered, (VoiceOut.MaxFrames + 1) * LifeVerse.Shared.Voice.VoiceSettings.FrameSamples, "quem fica muito atrás recomeça perto do presente");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void TheNewAudioSettingsStayWithinTheirLimits()
        {
            var s = new UserSettings { AmbientVolume = 9f, MediaVolume = float.NaN, VoiceSensitivity = 0f }.Clamped();
            Assert.AreEqual(1f, s.AmbientVolume); Assert.AreEqual(1f, s.MediaVolume, "um número inválido volta ao padrão"); Assert.AreEqual(.02f, s.VoiceSensitivity, 1e-5f); Assert.IsTrue(s.ParcelMusic);
        }
    
        [Test]
        public void TheMicrophonePickerFallsBackToTheSystemDefault()
        {
            MicDevices.Lister = () => new[] { "A", "B", "B", " " };
            try
            {
                MicDevices.Preferred = "B"; Assert.AreEqual("B", MicDevices.Pick()); CollectionAssert.AreEqual(new[] { "auto", "A", "B" }, MicDevices.Choices());
                MicDevices.Preferred = "C"; Assert.IsNull(MicDevices.Pick(), "um microfone que sumiu volta ao padrão"); MicDevices.Preferred = MicDevices.Auto; Assert.IsNull(MicDevices.Pick());
                Assert.AreEqual("Padrão do sistema", MicDevices.Label("auto")); Assert.AreEqual("B", MicDevices.Label("B"));
            }
            finally { MicDevices.Lister = null; MicDevices.Preferred = MicDevices.Auto; }
            Assert.AreEqual("auto", new UserSettings { InputDevice = "  " }.Clamped().InputDevice); Assert.AreEqual(120, new UserSettings { InputDevice = new string('x', 500) }.Clamped().InputDevice.Length);
        }

        [Test]
        public void TheSoundsOfTheInterfaceAreShortAndEndSoftly()
        {
            foreach (UiSound sound in System.Enum.GetValues(typeof(UiSound)))
            {
                var samples = UiSounds.Render(sound); Assert.Greater(samples.Length, 100, sound + " tem som"); Assert.Less(samples.Length, 22050 / 3, sound + " é curto");
                Assert.Less(Mathf.Abs(samples[samples.Length - 1]), .01f, sound + " termina sem estalo"); Assert.IsTrue(samples.All(s => Mathf.Abs(s) <= .5f + 1e-4f), sound + " não passa do volume");
            }
            Assert.Less(UiSounds.Render(UiSound.Click).Length, UiSounds.Render(UiSound.Good).Length, "o clique é mais curto que o aviso bom");
        }
    
        [Test]
        public void KeysSwapInsteadOfSharingAndNeverLoseAnAction()
        {
            KeyBindings.Reset();
            try
            {
                Assert.AreEqual(Key.W, KeyBindings.Of(GameAction.Forward)); Assert.IsTrue(KeyBindings.IsDefault);
                Assert.IsTrue(KeyBindings.Set(GameAction.Wave, Key.Y, out var swapped)); Assert.AreEqual(GameAction.Talk, swapped, "a tecla era de falar: as duas trocam"); Assert.AreEqual(Key.G, KeyBindings.Of(GameAction.Talk)); Assert.AreEqual(Key.Y, KeyBindings.Of(GameAction.Wave));
                Assert.IsFalse(KeyBindings.Set(GameAction.Wave, Key.Escape, out _), "Esc é do jogo"); Assert.IsFalse(KeyBindings.Set(GameAction.Wave, Key.None, out _)); Assert.AreEqual(Key.Y, KeyBindings.Of(GameAction.Wave));
                Assert.IsTrue(KeyBindings.Set(GameAction.Wave, Key.F9, out swapped)); Assert.IsNull(swapped); Assert.AreEqual(KeyBindings.All.Count(), KeyBindings.All.Select(KeyBindings.Of).Distinct().Count(), "nenhuma tecla faz duas coisas");
                var saved = KeyBindings.Save(); Assert.AreEqual(Key.F9.ToString(), saved["Wave"]); Assert.IsFalse(saved.ContainsKey("Jump"), "só o que mudou é salvo");
                KeyBindings.Reset(); Assert.AreEqual(Key.G, KeyBindings.Of(GameAction.Wave)); KeyBindings.Load(saved); Assert.AreEqual(Key.F9, KeyBindings.Of(GameAction.Wave)); Assert.AreEqual(Key.G, KeyBindings.Of(GameAction.Talk));
                foreach (var a in KeyBindings.All) Assert.IsNotEmpty(KeyBindings.Label(a), a + " tem nome");
                Assert.AreEqual("Espaço", KeyBindings.KeyName(Key.Space));
            }
            finally { KeyBindings.Reset(); }
        }

        [Test]
        public void ASavedFileThatClashesOrIsDamagedStillGivesEveryActionItsOwnKey()
        {
            try
            {
                KeyBindings.Load(new System.Collections.Generic.Dictionary<string, string> { ["Jump"] = "W", ["Nada"] = "Q", ["Fly"] = "Escape", ["Sit"] = "NaoExiste", ["Dance"] = "W" });
                Assert.AreEqual(Key.W, KeyBindings.Of(GameAction.Jump)); Assert.AreNotEqual(Key.W, KeyBindings.Of(GameAction.Forward), "quem ficou no padrão e chocou com uma escolha foi para uma tecla livre");
                Assert.AreEqual(Key.F, KeyBindings.Of(GameAction.Fly), "uma tecla que não pode ser usada é ignorada"); Assert.AreEqual(Key.X, KeyBindings.Of(GameAction.Sit)); Assert.AreEqual(Key.H, KeyBindings.Of(GameAction.Dance), "a mesma tecla pedida duas vezes vale para uma só");
                Assert.AreEqual(KeyBindings.All.Count(), KeyBindings.All.Select(KeyBindings.Of).Distinct().Count());
                KeyBindings.Load(null); Assert.IsTrue(KeyBindings.IsDefault);
            }
            finally { KeyBindings.Reset(); }
            var settings = new UserSettings { Keys = new System.Collections.Generic.Dictionary<string, string> { ["Jump"] = "", [new string('x', 99)] = "W" } }.Clamped(); Assert.AreEqual(0, settings.Keys.Count, "chaves vazias ou longas demais saem do arquivo");
        }
    }
}
