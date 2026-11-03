using Concentus;
using Concentus.Enums;

namespace LifeVerse.Shared.Voice;

public static class VoiceSettings
{
    public const int SampleRate = 16000;
    /// 20 ms of mono audio.
    public const int FrameSamples = 320;
    public const int BitRate = 20000;
    public const int MaxPacketBytes = 400;
    public const float HearingRange = 25f;
    /// Opus at 20 ms frames sends 50 packets a second; a little head-room covers jitter, anything above is abuse.
    public const int MaxPacketsPerSecond = 60;
}

/// One speaker's voice: 16 kHz mono PCM in 20 ms frames, squeezed to a few hundred bytes with Opus (pure C#, no native code).
public sealed class VoiceEncoder
{
    private readonly IOpusEncoder _encoder = OpusCodecFactory.CreateEncoder(VoiceSettings.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);

    public VoiceEncoder()
    {
        _encoder.Bitrate = VoiceSettings.BitRate;
        _encoder.UseVBR = true;
        _encoder.Complexity = 5;
    }

    /// Encodes exactly one frame; returns an empty array when the frame has the wrong size.
    public byte[] Encode(ReadOnlySpan<short> frame)
    {
        if (frame.Length != VoiceSettings.FrameSamples) return [];
        var buffer = new byte[VoiceSettings.MaxPacketBytes];
        var length = _encoder.Encode(frame, VoiceSettings.FrameSamples, buffer, buffer.Length);
        return length <= 0 ? [] : buffer[..length];
    }
}

/// One listener's view of one speaker. Hold one per speaker: the decoder keeps state between packets.
public sealed class VoiceDecoder
{
    private readonly IOpusDecoder _decoder = OpusCodecFactory.CreateDecoder(VoiceSettings.SampleRate, 1);

    /// Decodes one packet into a 20 ms frame. Corrupt or oversized input gives null instead of throwing.
    public short[]? Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length is 0 or > VoiceSettings.MaxPacketBytes) return null;
        try
        {
            var frame = new short[VoiceSettings.FrameSamples];
            var samples = _decoder.Decode(packet, frame, VoiceSettings.FrameSamples, false);
            return samples == VoiceSettings.FrameSamples ? frame : null;
        }
        catch (Exception exception) when (exception is OpusException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    /// Fills a gap left by a lost packet with the codec's best guess, so a drop sounds like a blip rather than a click.
    public short[] Conceal()
    {
        var frame = new short[VoiceSettings.FrameSamples];
        try { _decoder.Decode(ReadOnlySpan<byte>.Empty, frame, VoiceSettings.FrameSamples, false); } catch (Exception exception) when (exception is OpusException or ArgumentException) { }
        return frame;
    }
}

/// Turns what the sound card gives (any mix rate, mono samples as floats) into 16 kHz frames, one at a time.
public sealed class VoiceFramer(int sourceRate)
{
    private readonly List<float> _pending = [];
    private double _position;

    public int SourceRate { get; } = sourceRate;

    /// Adds samples and returns every complete 20 ms frame now available, as 16-bit PCM.
    public IEnumerable<short[]> Push(ReadOnlySpan<float> samples)
    {
        var ratio = (double)SourceRate / VoiceSettings.SampleRate;
        foreach (var sample in samples) _pending.Add(sample);
        var output = new List<short>();
        // Box filter over the span each output sample covers: cheap and enough to keep speech clean when going down to 16 kHz.
        while (_position + ratio <= _pending.Count)
        {
            var start = (int)_position; var end = Math.Min((int)Math.Ceiling(_position + ratio), _pending.Count);
            var sum = 0f; for (var i = start; i < end; i++) sum += _pending[i];
            output.Add(ToPcm(sum / Math.Max(1, end - start)));
            _position += ratio;
        }
        var consumed = (int)_position;
        if (consumed > 0) { _pending.RemoveRange(0, consumed); _position -= consumed; }
        _overflow.AddRange(output);
        var frames = new List<short[]>();
        while (_overflow.Count >= VoiceSettings.FrameSamples) { frames.Add(_overflow.GetRange(0, VoiceSettings.FrameSamples).ToArray()); _overflow.RemoveRange(0, VoiceSettings.FrameSamples); }
        return frames;
    }

    public void Reset() { _pending.Clear(); _overflow.Clear(); _position = 0; }

    private readonly List<short> _overflow = [];

    private static short ToPcm(float value) => (short)Math.Clamp(MathF.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
}

/// Loudness of a frame from 0 to 1, used to keep silence off the network and to show who is talking.
public static class VoiceLevel
{
    public static float Of(ReadOnlySpan<short> frame)
    {
        if (frame.IsEmpty) return 0f;
        double sum = 0; foreach (var sample in frame) sum += (double)sample * sample;
        return (float)Math.Min(1.0, Math.Sqrt(sum / frame.Length) / 8000.0);
    }
}
