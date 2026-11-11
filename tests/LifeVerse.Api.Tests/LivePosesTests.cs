using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Showing a drag to the other players as it happens: only for objects the dragger holds, only real numbers, and never more than a handful of objects.</summary>
public sealed class LivePosesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly EditLocks _locks = new();
    private readonly Guid _me = Guid.NewGuid(), _other = Guid.NewGuid(), _a = Guid.NewGuid(), _b = Guid.NewGuid();

    private static LivePose Pose(Guid id, float x = 1, float scale = 1) => new(id, x, 0, 0, 0, 0, 0, scale, scale, scale);

    [Fact]
    public void OnlyTheHolderOfTheLeaseMayMoveObjectsLive()
    {
        _locks.TryAcquire([_a], _me, Now, out _);
        Assert.True(LivePoses.Accept([Pose(_a)], _locks, _me, Now));
        Assert.False(LivePoses.Accept([Pose(_a)], _locks, _other, Now), "somebody else cannot move it");
        Assert.False(LivePoses.Accept([Pose(_b)], _locks, _me, Now), "an object nobody holds cannot be moved live");
        Assert.False(LivePoses.Accept([Pose(_a)], _locks, _me, Now + EditLocks.Lease + TimeSpan.FromSeconds(1)), "an expired lease does not count");
    }

    [Fact]
    public void AllTheObjectsInAMessageMustBeHeld()
    {
        _locks.TryAcquire([_a], _me, Now, out _);
        Assert.False(LivePoses.Accept([Pose(_a), Pose(_b)], _locks, _me, Now));
        _locks.TryAcquire([_b], _me, Now, out _);
        Assert.True(LivePoses.Accept([Pose(_a), Pose(_b)], _locks, _me, Now));
        Assert.False(LivePoses.Accept([Pose(_a), Pose(_a)], _locks, _me, Now), "the same object twice");
    }

    [Theory]
    [InlineData(float.NaN, 1)]
    [InlineData(float.PositiveInfinity, 1)]
    [InlineData(200_000f, 1)]
    [InlineData(1f, 0f)]
    [InlineData(1f, 500f)]
    public void NumbersThatAreNotRealOrOutOfRangeAreRefused(float x, float scale)
    {
        _locks.TryAcquire([_a], _me, Now, out _);
        Assert.False(LivePoses.Accept([Pose(_a, x, scale)], _locks, _me, Now));
    }

    [Fact]
    public void AMessageCarriesAtMostSixtyFourObjectsAndNeverNone()
    {
        var ids = Enumerable.Range(0, LivePoses.MaxObjects + 1).Select(_ => Guid.NewGuid()).ToList();
        _locks.TryAcquire(ids, _me, Now, out _);
        Assert.True(LivePoses.Accept(ids.Take(LivePoses.MaxObjects).Select(i => Pose(i)).ToList(), _locks, _me, Now));
        Assert.False(LivePoses.Accept(ids.Select(i => Pose(i)).ToList(), _locks, _me, Now));
        Assert.False(LivePoses.Accept([], _locks, _me, Now));
        Assert.False(LivePoses.Accept(null, _locks, _me, Now));
    }
}
