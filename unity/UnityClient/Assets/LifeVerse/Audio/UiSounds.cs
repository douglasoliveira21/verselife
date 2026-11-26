using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeVerse.Unity.Audio
{
    public enum UiSound { Click, Toggle, Open, Close, Notice, Good, Warn, Bad }

    /// <summary>
    /// The small sounds of the interface: a click on a button, a toggle, a window opening and closing, and a chime for each kind of notice (plain, good, warning, bad). They are made on the fly (a short tone with a soft envelope, no files), play flat (not in 3D)
    /// under the Efeitos volume at half strength, and a sound is not repeated within 40 ms so a burst of events is one blip. The person can turn them off.
    /// </summary>
    public static class UiSounds
    {
        public static bool Enabled { get; set; } = true;
        private static float _volume = 1f;
        /// <summary>The volume of the sounds of the interface alone, 0 to 1 (on top of the Efeitos volume they play under).</summary>
        public static float Volume { get => _volume; set => _volume = Mathf.Clamp01(value); }
        public static int Played { get; private set; }
        public static UiSound? Last { get; private set; }
        private static readonly Dictionary<UiSound, AudioClip> Clips = new Dictionary<UiSound, AudioClip>(); private static readonly Dictionary<UiSound, float> LastAt = new Dictionary<UiSound, float>();
        private static AudioSource _source; private static BusSource _bus;

        /// <summary>(frequency in Hz, seconds) of each note of a sound.</summary>
        public static (float Hertz, float Seconds)[] NotesOf(UiSound sound)
        {
            switch (sound)
            {
                case UiSound.Click: return new[] { (1500f, .03f) };
                case UiSound.Toggle: return new[] { (900f, .03f), (1300f, .03f) };
                case UiSound.Open: return new[] { (620f, .04f), (930f, .06f) };
                case UiSound.Close: return new[] { (930f, .04f), (620f, .06f) };
                case UiSound.Notice: return new[] { (880f, .07f), (1180f, .09f) };
                case UiSound.Good: return new[] { (660f, .07f), (880f, .07f), (1320f, .1f) };
                case UiSound.Warn: return new[] { (700f, .09f), (700f, .09f) };
                default: return new[] { (330f, .12f), (250f, .16f) };
            }
        }

        /// <summary>The samples of a sound (mono): each note a sine with a quick rise and a smooth fall, so there is no click at the ends.</summary>
        public static float[] Render(UiSound sound, int rate = 22050)
        {
            var samples = new List<float>();
            foreach (var (hertz, seconds) in NotesOf(sound))
            {
                var n = Mathf.Max(8, (int)(seconds * rate)); for (var i = 0; i < n; i++) { var t = i / (float)n; var envelope = Mathf.Min(1f, t * 12f) * (1f - t) * (1f - t); samples.Add(Mathf.Sin(2f * Mathf.PI * hertz * i / rate) * .5f * envelope); }
            }
            return samples.ToArray();
        }

        private static AudioClip ClipOf(UiSound sound)
        {
            if (Clips.TryGetValue(sound, out var known) && known != null) return known;
            var data = Render(sound); var clip = AudioClip.Create("ui-" + sound, data.Length, 1, 22050, false); clip.SetData(data, 0); Clips[sound] = clip; return clip;
        }

        private static bool Ready()
        {
            if (_source != null) return true;
            var host = new GameObject("UiSounds"); UnityEngine.Object.DontDestroyOnLoad(host); _bus = BusSource.Make(host, Bus.Effects, .5f, false); _source = _bus.Source; return true;
        }

        /// <summary>Plays a sound of the interface (nothing when they are off, or the same one was played a moment ago).</summary>
        public static bool Play(UiSound sound)
        {
            if (!Enabled || !Application.isPlaying) return false;   // outside play mode (editor tests) there is no game to sound in
            var now = Time.realtimeSinceStartup; if (LastAt.TryGetValue(sound, out var before) && now - before < .04f) return false; LastAt[sound] = now;
            Ready(); _source.PlayOneShot(ClipOf(sound), Volume); Played++; Last = sound; return true;
        }

        public static void Click() => Play(UiSound.Click);
    }
}
