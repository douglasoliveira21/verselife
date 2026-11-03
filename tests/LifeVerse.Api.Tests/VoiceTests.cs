using LifeVerse.Shared.Voice;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class VoiceCodecTests
{
    private static short[] Tone(int frameIndex, float frequency = 440f, float amplitude = .5f)
    {
        var frame = new short[VoiceSettings.FrameSamples];
        for (var i = 0; i < frame.Length; i++)
        {
            var t = (frameIndex * VoiceSettings.FrameSamples + i) / (float)VoiceSettings.SampleRate;
            frame[i] = (short)(MathF.Sin(2f * MathF.PI * frequency * t) * amplitude * short.MaxValue);
        }
        return frame;
    }

    [Fact]
    public void AFrameBecomesAFewHundredBytesAndComesBackAsTheSameTone()
    {
        var encoder = new VoiceEncoder(); var decoder = new VoiceDecoder();
        short[]? last = null; var sizes = new List<int>();
        for (var i = 0; i < 25; i++)
        {
            var packet = encoder.Encode(Tone(i));
            Assert.NotEmpty(packet); sizes.Add(packet.Length);
            last = decoder.Decode(packet);
            Assert.NotNull(last);
        }
        Assert.All(sizes, size => Assert.InRange(size, 1, 120));
        // After the codec has warmed up the output carries the tone's energy, not silence.
        Assert.InRange(VoiceLevel.Of(last!), .15f, 1f);
    }

    [Fact]
    public void TheDecodedToneKeepsItsPitch()
    {
        var encoder = new VoiceEncoder(); var decoder = new VoiceDecoder();
        var output = new List<short>();
        for (var i = 0; i < 40; i++) output.AddRange(decoder.Decode(encoder.Encode(Tone(i, 500f)))!);
        var tail = output.Skip(output.Count - 3200).ToArray();
        var crossings = 0; for (var i = 1; i < tail.Length; i++) if (tail[i - 1] < 0 && tail[i] >= 0) crossings++;
        var hertz = crossings / (tail.Length / (float)VoiceSettings.SampleRate);
        Assert.InRange(hertz, 450f, 550f);
    }

    [Fact]
    public void SilenceStaysSmallAndQuiet()
    {
        var encoder = new VoiceEncoder(); var decoder = new VoiceDecoder();
        var packet = encoder.Encode(new short[VoiceSettings.FrameSamples]);
        Assert.InRange(packet.Length, 1, 40);
        Assert.Equal(0f, VoiceLevel.Of(decoder.Decode(packet)!), 2);
    }

    [Theory]
    [InlineData(0)] [InlineData(100)] [InlineData(319)] [InlineData(321)] [InlineData(960)]
    public void OnlyWholeFramesAreEncoded(int samples) => Assert.Empty(new VoiceEncoder().Encode(new short[samples]));

    [Fact]
    public void CorruptOrOversizedPacketsAreRefusedNotFatal()
    {
        var decoder = new VoiceDecoder();
        Assert.Null(decoder.Decode([]));
        Assert.Null(decoder.Decode(new byte[VoiceSettings.MaxPacketBytes + 1]));
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var garbage = new byte[random.Next(1, 60)]; random.NextBytes(garbage);
            var result = decoder.Decode(garbage);
            Assert.True(result is null || result.Length == VoiceSettings.FrameSamples);
        }
    }

    [Fact]
    public void ALostPacketCanBeConcealedWithoutAnException()
    {
        var encoder = new VoiceEncoder(); var decoder = new VoiceDecoder();
        for (var i = 0; i < 5; i++) decoder.Decode(encoder.Encode(Tone(i)));
        Assert.Equal(VoiceSettings.FrameSamples, decoder.Conceal().Length);
    }

    [Fact]
    public void EachSpeakerNeedsItsOwnDecoder_ButEncodersAndDecodersAreIndependentInstances()
    {
        var a = new VoiceEncoder(); var b = new VoiceEncoder();
        var one = a.Encode(Tone(0, 300f)); var two = b.Encode(Tone(0, 300f));
        Assert.Equal(one, two);
        Assert.NotNull(new VoiceDecoder().Decode(one)); Assert.NotNull(new VoiceDecoder().Decode(two));
    }
}

public sealed class VoiceFramerTests
{
    [Theory]
    [InlineData(16000)] [InlineData(44100)] [InlineData(48000)]
    public void OneSecondOfAnyRateGivesAboutFiftyFrames(int rate)
    {
        var framer = new VoiceFramer(rate); var samples = new float[rate];
        var frames = framer.Push(samples).Count();
        Assert.InRange(frames, 48, 50);
    }

    [Fact]
    public void SamplesArrivingInSmallPiecesGiveTheSameFramesAsOneBigPush()
    {
        var samples = Enumerable.Range(0, 48000).Select(i => MathF.Sin(i * .05f) * .4f).ToArray();
        var whole = new VoiceFramer(48000).Push(samples).ToList();
        var pieces = new List<short[]>(); var framer = new VoiceFramer(48000);
        for (var i = 0; i < samples.Length; i += 441) pieces.AddRange(framer.Push(samples.AsSpan(i, Math.Min(441, samples.Length - i))));
        Assert.Equal(whole.Count, pieces.Count);
        for (var i = 0; i < whole.Count; i++) Assert.Equal(whole[i], pieces[i]);
    }

    [Fact]
    public void ALowToneSurvivesDownsamplingAndLoudnessIsPreserved()
    {
        var samples = Enumerable.Range(0, 48000).Select(i => MathF.Sin(2f * MathF.PI * 300f * i / 48000f) * .1f).ToArray();
        var frames = new VoiceFramer(48000).Push(samples).ToList();
        var level = VoiceLevel.Of(frames[10]);
        Assert.InRange(level, .2f, .4f);
    }

    [Fact]
    public void LoudInputIsClippedNotWrapped()
    {
        var frames = new VoiceFramer(16000).Push(Enumerable.Repeat(5f, 1600).ToArray()).ToList();
        Assert.All(frames[0], sample => Assert.Equal(short.MaxValue, sample));
        var negative = new VoiceFramer(16000).Push(Enumerable.Repeat(-5f, 1600).ToArray()).ToList();
        Assert.All(negative[0], sample => Assert.Equal(short.MinValue, sample));
    }

    [Fact]
    public void ResetDropsWhatWasWaiting()
    {
        var framer = new VoiceFramer(16000);
        Assert.Empty(framer.Push(new float[200]));
        framer.Reset();
        Assert.Empty(framer.Push(new float[200]));
        Assert.Single(framer.Push(new float[120]));
    }

    [Fact]
    public void TheEndToEndPathFromMicrophoneSamplesToDecodedAudioWorks()
    {
        var encoder = new VoiceEncoder(); var decoder = new VoiceDecoder(); var framer = new VoiceFramer(48000);
        var samples = Enumerable.Range(0, 48000).Select(i => MathF.Sin(2f * MathF.PI * 440f * i / 48000f) * .5f).ToArray();
        short[]? last = null;
        foreach (var frame in framer.Push(samples)) last = decoder.Decode(encoder.Encode(frame));
        Assert.True(VoiceLevel.Of(last!) > .15f);
    }
}

public sealed class VoiceRelayTests
{
    private static readonly VoicePeer[] Players = [new(1, 0, 0, 0), new(2, 10, 0, 0), new(3, 24.9f, 0, 0), new(4, 25.1f, 0, 0), new(5, 0, 0, -100), new(6, 0, 20, 0)];

    [Fact]
    public void OnlyPlayersWithinHearingRangeReceiveTheVoiceAndNotTheSpeaker()
    {
        var hearers = VoiceRelay.Hearers(1, Players);
        Assert.Equal([2, 3, 6], hearers.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void DistanceIsMeasuredInThreeDimensions() => Assert.Contains(6, VoiceRelay.Hearers(1, Players));

    [Fact]
    public void AnUnknownSpeakerReachesNobody() => Assert.Empty(VoiceRelay.Hearers(99, Players));

    [Fact]
    public void BlockingWorksInBothDirections()
    {
        Assert.DoesNotContain(2, VoiceRelay.Hearers(1, Players, (a, b) => a == 1 && b == 2));
        Assert.DoesNotContain(2, VoiceRelay.Hearers(1, Players, (a, b) => a == 2 && b == 1));
        Assert.Contains(3, VoiceRelay.Hearers(1, Players, (a, b) => a == 1 && b == 2));
    }

    [Fact]
    public void NonFinitePositionsAreNeverInRange() => Assert.Empty(VoiceRelay.Hearers(1, [new(1, 0, 0, 0), new(2, float.NaN, 0, 0)]));

    [Theory]
    [InlineData(0)] [InlineData(-5)] [InlineData(VoiceSettings.MaxPacketBytes + 1)]
    public void EmptyAndOversizedPacketsAreRefused(int length) => Assert.False(new VoiceRelay().Allow(1, 0, length));

    [Fact]
    public void ANormalSpeakerSendingFiftyPacketsASecondIsNeverCut()
    {
        var relay = new VoiceRelay();
        for (var i = 0; i < 500; i++) Assert.True(relay.Allow(1, i * 20L, 60));
    }

    [Fact]
    public void FloodingIsStoppedAndTheSpeakerRecoversAfterASecond()
    {
        var relay = new VoiceRelay(); var accepted = 0;
        for (var i = 0; i < 200; i++) if (relay.Allow(1, 500, 60)) accepted++;
        Assert.Equal(VoiceSettings.MaxPacketsPerSecond, accepted);
        Assert.False(relay.Allow(1, 900, 60));
        Assert.True(relay.Allow(1, 1600, 60));
    }

    [Fact]
    public void OneFloodingSpeakerDoesNotAffectAnother()
    {
        var relay = new VoiceRelay();
        for (var i = 0; i < 200; i++) relay.Allow(1, 0, 60);
        Assert.True(relay.Allow(2, 0, 60));
    }

    [Fact]
    public void ForgettingAPeerClearsItsHistory()
    {
        var relay = new VoiceRelay();
        for (var i = 0; i < 100; i++) relay.Allow(1, 0, 60);
        relay.Forget(1);
        Assert.True(relay.Allow(1, 0, 60));
    }
}

public sealed class VoiceFramerStreamingTests
{
    [Theory]
    [InlineData(44100, 882)] [InlineData(48000, 960)] [InlineData(44100, 441)] [InlineData(22050, 441)] [InlineData(44100, 1024)]
    public void SteadyStreamingYieldsOneFramePerTwentyMilliseconds(int rate, int chunk)
    {
        var framer = new VoiceFramer(rate); var frames = 0; var total = 0;
        var seconds = 3;
        while (total + chunk <= rate * seconds) { frames += framer.Push(new float[chunk]).Count(); total += chunk; }
        var expected = total / (double)rate * 50;
        Assert.InRange(frames, expected - 2, expected + 1);
    }
}

public sealed class VoiceMeterTests
{
    [Fact]
    public void PeakIsTheLargestSampleAsAFractionOfFullScale()
    {
        Assert.Equal(0f, VoiceMeter.Peak([]));
        Assert.Equal(0f, VoiceMeter.Peak(new short[320]));
        Assert.Equal(.5f, VoiceMeter.Peak([0, 16383, -100]), 2);
        Assert.Equal(.5f, VoiceMeter.Peak([0, -16383, 100]), 2);
    }

    [Fact]
    public void TheMostNegativeSampleDoesNotOverflow() => Assert.Equal(1f, VoiceMeter.Peak([short.MinValue]));

    [Theory]
    [InlineData(.5f, false)] [InlineData(.91f, false)] [InlineData(.92f, true)] [InlineData(1f, true)]
    public void ClippingStartsNearTheTopOfTheRange(float peak, bool clipping) => Assert.Equal(clipping, VoiceMeter.IsClipping(peak));

    [Fact]
    public void ANormalSpeechLevelIsGreenAndAShoutIsRed()
    {
        var normal = Enumerable.Range(0, 320).Select(i => (short)(MathF.Sin(i * .2f) * 9000)).ToArray();
        var shout = Enumerable.Range(0, 320).Select(i => (short)Math.Clamp(MathF.Sin(i * .2f) * 60000, short.MinValue, short.MaxValue)).ToArray();
        Assert.False(VoiceMeter.IsClipping(VoiceMeter.Peak(normal)));
        Assert.True(VoiceMeter.IsClipping(VoiceMeter.Peak(shout)));
    }
}
