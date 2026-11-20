using LifeVerse.Shared.Audio;
using UnityEngine;

namespace LifeVerse.Unity.Audio
{
    /// <summary>How loud each layer of the soundscape is for a moment of the game: the surf grows toward the shore (and is whole in the water), the wind with height and bad weather, the rain with the weather. The same rules as the Godot client.</summary>
    public static class AmbientLevels
    {
        /// <param name="edgeDistance">Metres from the person to the edge of the island (island half-size minus the larger of |x| and |z|).</param>
        public static AmbientMix Compute(float edgeDistance, bool swimming, float altitude, string weather)
        {
            var ocean = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edgeDistance / 140f)); if (swimming) ocean = 1f;
            float add;
            switch (weather) { case "cloudy": add = .035f; break; case "rain": add = .09f; break; case "storm": add = .28f; break; case "fog": add = .02f; break; default: add = 0f; break; }
            var wind = .055f + .07f * Mathf.Clamp01(altitude / 30f) + add;
            var rain = weather == "rain" ? .75f : weather == "storm" ? 1f : 0f;
            return new AmbientMix(ocean, wind, rain);
        }
    }

    /// <summary>The soundscape of the island, made on the fly (no files): surf, wind and rain, and the splash of swimming. The audio thread asks for samples; the game only says how loud each layer should be.</summary>
    public sealed class AmbientSound : MonoBehaviour
    {
        private AmbientSynth _synth; private readonly object _gate = new object(); private float _ocean, _wind, _rain, _tOcean, _tWind, _tRain; private BusSource _bus; private int _rate;
        public float Ocean => _ocean; public float Wind => _wind; public float Rain => _rain;
        public bool Playing => _bus != null && _bus.Source != null && _bus.Source.isPlaying;
        public int Splashes { get; private set; }
        public int Rendered { get; private set; }

        private void Awake()
        {
            _rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 22050; _synth = new AmbientSynth(_rate, 1);
            _bus = BusSource.Make(gameObject, Bus.Ambient, 1f, false, 30f, true); var clip = AudioClip.Create("ambient", _rate, 1, _rate, false); var ones = new float[_rate]; for (var i = 0; i < ones.Length; i++) ones[i] = 1f; clip.SetData(ones, 0);   // the filter replaces these samples; the source needs something to play
            _bus.Source.clip = clip; _bus.Source.Play();
        }

        public void SetTargets(AmbientMix mix) { _tOcean = mix.Ocean; _tWind = mix.Wind; _tRain = mix.Rain; }

        public void Splash() { lock (_gate) _synth.TriggerStep(.55f, true); Splashes++; }

        private void Update() { var blend = 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime); _ocean = Mathf.Lerp(_ocean, _tOcean, blend); _wind = Mathf.Lerp(_wind, _tWind, blend); _rain = Mathf.Lerp(_rain, _tRain, blend); }

        /// <summary>Makes <paramref name="frames"/> mono samples as the audio thread would (tests read them).</summary>
        public float[] Render(int frames)
        {
            var mono = new float[frames]; lock (_gate) _synth.Fill(mono, new AmbientMix(_ocean, _wind, _rain)); Rendered += frames; return mono;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_synth == null || channels <= 0) return; var frames = data.Length / channels; var mono = new float[frames]; lock (_gate) _synth.Fill(mono, new AmbientMix(_ocean, _wind, _rain));
            for (var i = 0; i < frames; i++) for (var c = 0; c < channels; c++) data[i * channels + c] = mono[i];
        }
    }
}
