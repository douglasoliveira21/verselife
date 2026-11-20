using System;
using UnityEngine;

namespace LifeVerse.Unity.Audio
{
    /// <summary>The five groups every sound belongs to, each with its own volume (Configurações › Áudio), all under the general one: Ambiente (the sound of the world), Efeitos (footsteps and sounds of objects), Música (login music and music of parcels), Mídia (audio from the internet that objects and lands play) and Voz (other people's voices).</summary>
    public enum Bus { Ambient, Effects, Music, Media, Voice }

    public static class Buses
    {
        private static readonly float[] Levels = { 1f, 1f, 1f, 1f, 1f };
        public static float Master { get; private set; } = 1f;

        public static void SetAll(float master, float ambient, float effects, float music, float media, float voice)
        {
            Master = Mathf.Clamp01(master); Levels[(int)Bus.Ambient] = Mathf.Clamp01(ambient); Levels[(int)Bus.Effects] = Mathf.Clamp01(effects); Levels[(int)Bus.Music] = Mathf.Clamp01(music); Levels[(int)Bus.Media] = Mathf.Clamp01(media); Levels[(int)Bus.Voice] = Mathf.Clamp01(voice);
        }

        public static float Of(Bus bus) => Levels[(int)bus];

        /// <summary>What a sound of this group is multiplied by: the group's volume times the general one.</summary>
        public static float Gain(Bus bus) => Master * Levels[(int)bus];

        /// <summary>Loudness in decibels as a linear gain (0 dB = 1).</summary>
        public static float DbToLinear(float db) => Mathf.Pow(10f, db / 20f);
    }

    /// <summary>Keeps an AudioSource at its own base volume times the volume of its group, so the sliders of the settings window change what is playing at once.</summary>
    public sealed class BusSource : MonoBehaviour
    {
        public AudioSource Source { get; private set; }
        public Bus Bus { get; set; }
        /// <summary>The volume of this sound alone (0 to 1) before the group and the general volume.</summary>
        public float Base { get; set; } = 1f;
        /// <summary>A fade the sound is going through (0 silent, 1 whole), moved toward <see cref="FadeTarget"/> each frame.</summary>
        public float Fade { get; set; } = 1f;
        public float FadeTarget { get; set; } = 1f;
        public float FadeSeconds { get; set; } = .25f;

        public static BusSource Make(GameObject host, Bus bus, float baseVolume, bool spatial, float range = 30f, bool loop = false)
        {
            var source = host.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = loop; source.spatialBlend = spatial ? 1f : 0f;
            if (spatial) { source.rolloffMode = AudioRolloffMode.Logarithmic; source.minDistance = Mathf.Max(1f, range / 7f); source.maxDistance = range; source.dopplerLevel = 0f; }
            var bus_ = host.AddComponent<BusSource>(); bus_.Source = source; bus_.Bus = bus; bus_.Base = baseVolume; bus_.Apply(); return bus_;
        }

        public float Volume => Mathf.Clamp01(Base * Buses.Gain(Bus) * Fade);

        public void Apply() { if (Source != null) Source.volume = Volume; }
        private void Update()
        {
            if (!Mathf.Approximately(Fade, FadeTarget)) Fade = Mathf.MoveTowards(Fade, FadeTarget, Time.unscaledDeltaTime / Mathf.Max(.01f, FadeSeconds));
            Apply();
        }
    }
}
