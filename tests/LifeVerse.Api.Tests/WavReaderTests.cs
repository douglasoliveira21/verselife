using System.Text;
using LifeVerse.Shared.Audio;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class WavReaderTests
{
    private static byte[] Wav(int rate, int channels, int bits, byte[] data, int format = 1, bool extraChunk = false)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.ASCII);
        w.Write("RIFF"u8); w.Write(0); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)format); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
        if (extraChunk) { w.Write("LIST"u8); w.Write(3); w.Write(new byte[] { 1, 2, 3 }); w.Write((byte)0); } // odd-sized chunks are padded
        w.Write("data"u8); w.Write(data.Length); w.Write(data); return stream.ToArray();
    }

    private static byte[] Pcm16(params short[] samples) { var bytes = new byte[samples.Length * 2]; for (var i = 0; i < samples.Length; i++) BitConverter.GetBytes(samples[i]).CopyTo(bytes, i * 2); return bytes; }

    [Fact]
    public void ASixteenBitMonoFileIsDecodedToItsRightLengthAndLevels()
    {
        var wav = WavReader.Read(Wav(8000, 1, 16, Pcm16(0, 16384, -16384, 32767, -32768)))!;
        Assert.Equal((8000, 1, 16, 5), (wav.SampleRate, wav.Channels, wav.BitsPerSample, wav.Frames)); Assert.Equal(5 / 8000.0, wav.Seconds, 6);
        Assert.Equal([0f, .5f, -.5f, 32767 / 32768f, -1f], wav.Samples);
    }

    [Fact]
    public void OtherDepthsAndAFloatFileAreDecodedToo()
    {
        Assert.Equal([-1f, 0f, 127 / 128f], WavReader.Read(Wav(8000, 1, 8, [0, 128, 255]))!.Samples);
        Assert.Equal(2, WavReader.Read(Wav(44100, 2, 24, [0, 0, 0x40, 0, 0, 0xC0, 0, 0, 0, 0, 0, 0]))!.Frames);
        var floats = new byte[8]; BitConverter.GetBytes(.25f).CopyTo(floats, 0); BitConverter.GetBytes(5f).CopyTo(floats, 4);
        Assert.Equal([.25f, 1f], WavReader.Read(Wav(8000, 1, 32, floats, format: 3))!.Samples); // beyond full scale is cut, not wrapped
        Assert.Equal(.5f, WavReader.Read(Wav(8000, 1, 32, BitConverter.GetBytes(1 << 30)))!.Samples[0]);
        Assert.Equal(.5f, WavReader.Read(Wav(8000, 1, 24, [0, 0, 0x40]))!.Samples[0]);
        Assert.NotNull(WavReader.Read(Wav(8000, 1, 16, Pcm16(1, 2, 3), extraChunk: true)));
    }

    [Fact]
    public void StereoIsKeptInterleavedAndPeaksOutlineTheWave()
    {
        var wav = WavReader.Read(Wav(8000, 2, 16, Pcm16(0, 0, 16384, -8192, 0, 0, -32768, 0)))!; Assert.Equal(4, wav.Frames); Assert.Equal(.5f, wav.Samples[2]); Assert.Equal(-.25f, wav.Samples[3]);
        Assert.Equal([.5f, 1f], WavReader.Peaks(wav, 2)); Assert.Equal([1f], WavReader.Peaks(wav, 1)); Assert.Equal(4, WavReader.Peaks(wav, 4).Length); Assert.Equal(new float[1], WavReader.Peaks(new WavData(8000, 1, 16, []), 1));
        Assert.Equal(300, WavReader.Peaks(wav, 300).Length); Assert.Single(WavReader.Peaks(wav, 0)); // more bins than frames, or none at all, are fine
    }

    [Fact]
    public void ThePlayableCopyIsSixteenBitAndMixesManyChannelsDownToTwo()
    {
        var mono = WavReader.ToPcm16(WavReader.Read(Wav(8000, 1, 16, Pcm16(100, -100)))!); Assert.False(mono.Stereo); Assert.Equal(Pcm16(100, -100), mono.Data);
        var six = WavReader.Read(Wav(8000, 6, 16, Pcm16(1, 2, 3, 4, 5, 6)))!; var down = WavReader.ToPcm16(six); Assert.True(down.Stereo); Assert.Equal(Pcm16(1, 2), down.Data);
    }

    [Theory]
    [InlineData("")] [InlineData("RIFF")] [InlineData("not a wave file at all, just some text that is long enough to pass the length check")]
    public void NonsenseIsNull(string text) => Assert.Null(WavReader.Read(Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void DamagedFilesAreNullAndNeverThrow()
    {
        var good = Wav(8000, 1, 16, Pcm16(1, 2, 3, 4));
        for (var cut = 0; cut < good.Length; cut++) { var short_ = good[..cut]; var ex = Record.Exception(() => WavReader.Read(short_)); Assert.Null(ex); } // every truncation
        Assert.Null(WavReader.Read(Wav(8000, 0, 16, Pcm16(1)))); Assert.Null(WavReader.Read(Wav(0, 1, 16, Pcm16(1)))); Assert.Null(WavReader.Read(Wav(8000, 99, 16, Pcm16(1)))); Assert.Null(WavReader.Read(Wav(8000, 1, 12, Pcm16(1)))); Assert.Null(WavReader.Read(Wav(8000, 1, 16, [], format: 1)));
        Assert.Null(WavReader.Read(Wav(8000, 1, 16, Pcm16(1), format: 85))); // a compressed format (MP3 in a WAV) is not decoded here
        var lying = Wav(8000, 1, 16, Pcm16(1, 2)); BitConverter.GetBytes(int.MaxValue).CopyTo(lying, lying.Length - 8); Assert.Equal(2, WavReader.Read(lying)!.Frames); // a data size that lies is cut to what is there
        var rng = new Random(3); for (var i = 0; i < 200; i++) { var noise = (byte[])good.Clone(); noise[rng.Next(noise.Length)] = (byte)rng.Next(256); noise[rng.Next(noise.Length)] = (byte)rng.Next(256); Assert.Null(Record.Exception(() => WavReader.Read(noise))); }
    }
}
