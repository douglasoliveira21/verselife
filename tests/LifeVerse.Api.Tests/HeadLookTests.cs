using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class HeadLookTests
{
    [Theory]
    [InlineData(0f)] [InlineData(-1.5f)] [InlineData(1.35f)] [InlineData(1.6f)]
    public void TheAnglesOfTheFirstPersonCameraAreAccepted(float pitch) => Assert.Null(HeadLook.Check(pitch));

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(1.7f)] [InlineData(-3f)] [InlineData(100f)]
    public void ThingsThatAreNotAnAngleAreRefused(float pitch) => Assert.NotNull(HeadLook.Check(pitch));

    [Fact]
    public void AHeadIsNeverDrawnBentFartherThanAHeadCan()
    {
        Assert.Equal(HeadLook.MaxPitch, HeadLook.Drawn(1.5f)); Assert.Equal(-HeadLook.MaxPitch, HeadLook.Drawn(-1.5f));
        Assert.Equal(.4f, HeadLook.Drawn(.4f)); Assert.Equal(0f, HeadLook.Drawn(float.NaN));
        Assert.InRange(HeadLook.MaxPitch, .6f, 1.1f);                                   // about 35 to 63 degrees
    }

    [Fact]
    public void AnglesAreNotPassedOnMoreOftenThanAFewTimesASecond()
    {
        var t = DateTimeOffset.UtcNow;
        Assert.True(HeadLook.MayRelay(null, t));
        Assert.False(HeadLook.MayRelay(t, t.AddMilliseconds(20)));
        Assert.True(HeadLook.MayRelay(t, t + HeadLook.MinInterval));
    }

    [Fact]
    public void TheClientSendsWhenTheAngleMovedAndRepeatsItSlowlyForWhoArrivesLater()
    {
        var late = TimeSpan.FromMilliseconds(100);
        Assert.False(HeadLook.ShouldSend(.2f, .21f, late));                              // hardly moved
        Assert.True(HeadLook.ShouldSend(.2f, .3f, late));                                // moved
        Assert.False(HeadLook.ShouldSend(.2f, .9f, TimeSpan.FromMilliseconds(30)));      // moved, but too soon
        Assert.True(HeadLook.ShouldSend(.2f, .2f, HeadLook.RefreshInterval));            // same angle, said again
    }

    [Fact]
    public void OnlyThosePlayersWhoCanSeeTheAvatarAreTold()
    {
        Assert.True(HeadLook.InRange(20f, 64f)); Assert.False(HeadLook.InRange(100f, 64f));
    }
}
