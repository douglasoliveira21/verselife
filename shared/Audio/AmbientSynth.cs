namespace LifeVerse.Shared.Audio;

/// <summary>Mix of the environment layers for one moment: each level is 0 (silent) to 1 (full).</summary>
public readonly record struct AmbientMix(float Ocean, float Wind, float Rain, float Master = 1f);

/// <summary>
/// Engine-independent generator of the ambient soundscape (surf, wind, rain) and footsteps, so the game ships sound
/// without audio assets. Output is mono samples in -1..1 that the client pushes to an audio stream.
/// </summary>
public sealed class AmbientSynth(int mixRate = 22050, int seed = 1)
{
    private readonly Random _random = new(seed);
    private double _time;
    private float _oceanLow, _oceanLow2, _windLow, _windLow2, _rainLow, _rainPrev, _stepLow, _stepEnvelope, _stepCutoff = .12f, _stepGain;
    private float _oceanGain, _windGain, _rainGain;

    public int MixRate { get; } = mixRate;

    /// <summary>Starts a footstep: <paramref name="intensity"/> is 0..1 and <paramref name="soft"/> gives a duller sound (sand, water).</summary>
    public void TriggerStep(float intensity, bool soft)
    {
        _stepEnvelope = 1f; _stepGain = Math.Clamp(intensity, 0f, 1f) * .55f;
        _stepCutoff = soft ? .05f : .16f;
    }

    public void Fill(Span<float> buffer, AmbientMix mix)
    {
        var dt = 1.0 / MixRate;
        var ocean = Math.Clamp(mix.Ocean, 0f, 1f); var wind = Math.Clamp(mix.Wind, 0f, 1f); var rain = Math.Clamp(mix.Rain, 0f, 1f);
        var master = Math.Clamp(mix.Master, 0f, 1f);
        for (var i = 0; i < buffer.Length; i++)
        {
            var white = (float)(_random.NextDouble() * 2 - 1);
            // Gains glide toward their targets so changing weather or walking to the shore never clicks.
            _oceanGain += (ocean - _oceanGain) * .0004f; _windGain += (wind - _windGain) * .0004f; _rainGain += (rain - _rainGain) * .0004f;

            // Surf: low-passed noise swelling and fading with two slow waves.
            _oceanLow += (white - _oceanLow) * .045f; _oceanLow2 += (_oceanLow - _oceanLow2) * .06f;
            var swell = .5 + .5 * Math.Sin(_time * 2 * Math.PI * .11); var swell2 = .5 + .5 * Math.Sin(_time * 2 * Math.PI * .067 + 1.3);
            var surf = _oceanLow2 * 5.5f * (float)(.25 + .75 * swell * swell2) * _oceanGain;

            // Wind: very low noise with a gusting level.
            _windLow += (white - _windLow) * .012f; _windLow2 += (_windLow - _windLow2) * .02f;
            var gust = .55 + .45 * Math.Sin(_time * 2 * Math.PI * .071) * Math.Sin(_time * 2 * Math.PI * .19 + .7);
            var breeze = _windLow2 * 11f * (float)gust * _windGain;

            // Rain: bright noise (high-passed, then softened).
            var bright = white - _rainPrev; _rainPrev = white;
            _rainLow += (bright - _rainLow) * .5f;
            var drops = _rainLow * .4f * _rainGain;

            // Footstep: short low-passed noise burst.
            _stepLow += (white - _stepLow) * _stepCutoff;
            var step = _stepLow * _stepEnvelope * _stepGain * 3f;
            _stepEnvelope *= .9972f;

            var mixed = (surf * .6f + breeze * .5f + drops * .5f) * master + step * master;
            buffer[i] = MathF.Tanh(mixed);
            _time += dt;
        }
    }
}
