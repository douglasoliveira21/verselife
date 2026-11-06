namespace LifeVerse.Shared.Audio;

/// <summary>The sound of a WAV file, decoded: every sample from -1 to 1, channels interleaved.</summary>
public sealed record WavData(int SampleRate, int Channels, int BitsPerSample, float[] Samples)
{
    public int Frames => Channels == 0 ? 0 : Samples.Length / Channels;
    public double Seconds => SampleRate == 0 ? 0 : Frames / (double)SampleRate;
}

/// <summary>Reads PCM WAV files (8, 16, 24 and 32 bits, and 32-bit float) without trusting a single number in the header, so a damaged or hostile file only ever gives "null".</summary>
public static class WavReader
{
    public const int MaxChannels = 8, MaxSampleRate = 384_000;

    public static WavData? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 44 || !Tag(bytes, 0, "RIFF") || !Tag(bytes, 8, "WAVE")) return null;
        int format = 0, channels = 0, rate = 0, bits = 0; var haveFormat = false; var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var size = BitConverter.ToUInt32(bytes.Slice(offset + 4, 4)); var body = offset + 8;
            if (Tag(bytes, offset, "fmt "))
            {
                if (size < 16 || body + 16 > bytes.Length) return null;
                format = BitConverter.ToUInt16(bytes.Slice(body, 2)); channels = BitConverter.ToUInt16(bytes.Slice(body + 2, 2)); rate = (int)Math.Min(BitConverter.ToUInt32(bytes.Slice(body + 4, 4)), int.MaxValue); bits = BitConverter.ToUInt16(bytes.Slice(body + 14, 2));
                if (format == 0xFFFE && size >= 26 && body + 26 <= bytes.Length) format = BitConverter.ToUInt16(bytes.Slice(body + 24, 2)); // "extensible": the real format is inside
                haveFormat = true;
            }
            else if (Tag(bytes, offset, "data"))
            {
                if (!haveFormat || channels is < 1 or > MaxChannels || rate is < 1 or > MaxSampleRate) return null;
                var isFloat = format == 3 && bits == 32; if (!(format == 1 && bits is 8 or 16 or 24 or 32) && !isFloat) return null;
                var available = (long)Math.Min(size, (uint)(bytes.Length - body)); var bytesPerSample = bits / 8; var frameBytes = bytesPerSample * channels;
                var frames = (int)(available / frameBytes); if (frames == 0) return null;
                var samples = new float[frames * channels]; var at = body;
                for (var i = 0; i < samples.Length; i++, at += bytesPerSample)
                    samples[i] = bits switch
                    {
                        8 => (bytes[at] - 128) / 128f,
                        16 => BitConverter.ToInt16(bytes.Slice(at, 2)) / 32768f,
                        24 => ((bytes[at] | (bytes[at + 1] << 8) | ((sbyte)bytes[at + 2] << 16))) / 8388608f,
                        _ => isFloat ? Math.Clamp(BitConverter.ToSingle(bytes.Slice(at, 4)), -1f, 1f) : BitConverter.ToInt32(bytes.Slice(at, 4)) / 2147483648f
                    };
                return new WavData(rate, channels, bits, samples);
            }
            var next = (long)body + size + (size & 1); if (next > bytes.Length) break; offset = (int)next;
        }
        return null;
    }

    private static bool Tag(ReadOnlySpan<byte> bytes, int at, string tag) => at + 4 <= bytes.Length && bytes[at] == tag[0] && bytes[at + 1] == tag[1] && bytes[at + 2] == tag[2] && bytes[at + 3] == tag[3];

    /// <summary>The highest level of each of <paramref name="bins"/> equal slices of the sound (every channel counted), from 0 to 1: the outline of the wave.</summary>
    public static float[] Peaks(WavData wav, int bins)
    {
        bins = Math.Max(1, bins); var peaks = new float[bins]; if (wav.Frames == 0) return peaks;
        for (var frame = 0; frame < wav.Frames; frame++)
        {
            var bin = (int)((long)frame * bins / wav.Frames);
            for (var channel = 0; channel < wav.Channels; channel++) peaks[bin] = Math.Max(peaks[bin], Math.Abs(wav.Samples[frame * wav.Channels + channel]));
        }
        return peaks;
    }

    /// <summary>The sound as 16-bit signed little-endian samples (what the game's audio player takes), with more than two channels mixed down to two.</summary>
    public static (byte[] Data, bool Stereo) ToPcm16(WavData wav)
    {
        var stereo = wav.Channels >= 2; var outChannels = stereo ? 2 : 1; var data = new byte[wav.Frames * outChannels * 2];
        for (var frame = 0; frame < wav.Frames; frame++)
            for (var c = 0; c < outChannels; c++)
            {
                var value = (short)Math.Clamp((int)MathF.Round(wav.Samples[frame * wav.Channels + c] * 32767f), short.MinValue, short.MaxValue);
                data[(frame * outChannels + c) * 2] = (byte)(value & 0xFF); data[(frame * outChannels + c) * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }
        return (data, stereo);
    }
}
