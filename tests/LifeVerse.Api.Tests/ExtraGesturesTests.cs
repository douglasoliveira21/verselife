using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class ExtraGesturesTests
{
    [Fact]
    public void EveryGestureOfTheMenuHasANumberAndComesBackAsItself()
    {
        foreach (var state in new[] { AvatarMotionState.Applaud, AvatarMotionState.Point, AvatarMotionState.Greet, AvatarMotionState.Laugh, AvatarMotionState.SitFloor })
        {
            var id = ExtraGestures.IdOf(state);
            Assert.True(ExtraGestures.IsValid(id), state.ToString());
            Assert.Equal(state, ExtraGestures.States[id]);
        }
    }

    [Fact]
    public void WaveAndDanceAreNotHere_TheyTravelAsInputFlags() { Assert.Equal(-1, ExtraGestures.IdOf(AvatarMotionState.Wave)); Assert.Equal(-1, ExtraGestures.IdOf(AvatarMotionState.Dance)); }

    [Theory]
    [InlineData(-1)] [InlineData(5)] [InlineData(999)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void NumbersThatAreNotAGestureAreRefused(int id) => Assert.False(ExtraGestures.IsValid(id));

    [Fact]
    public void ASecondGestureTooSoonIsNotPassedOn()
    {
        var t = DateTimeOffset.UtcNow;
        Assert.True(ExtraGestures.MayRelay(null, t));
        Assert.False(ExtraGestures.MayRelay(t, t.AddMilliseconds(50)));
        Assert.True(ExtraGestures.MayRelay(t, t + ExtraGestures.MinInterval));
    }
}
