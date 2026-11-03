using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class EditLocksTests
{
    private static readonly Guid Ana = Guid.NewGuid(), Bia = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static Guid[] Ids(int n) => Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToArray();

    [Fact]
    public void ASecondPersonCannotTakeWhatIsHeld_AndTheOwnerCanRenew()
    {
        var locks = new EditLocks(); var box = Ids(1);
        Assert.True(locks.TryAcquire(box, Ana, T0, out _));
        Assert.False(locks.TryAcquire(box, Bia, T0.AddSeconds(5), out var holder)); Assert.Equal(Ana, holder);
        Assert.True(locks.TryAcquire(box, Ana, T0.AddSeconds(15), out _)); // renewing
        Assert.False(locks.TryAcquire(box, Bia, T0.AddSeconds(30), out _)); // still held: it was renewed at +15 s for 20 s
        Assert.Equal(Ana, locks.HolderOf(box[0], T0.AddSeconds(30)));
    }

    [Fact]
    public void LeasesRunOutSoACrashedPlayerNeverLocksForLong()
    {
        var locks = new EditLocks(); var box = Ids(1);
        locks.TryAcquire(box, Ana, T0, out _);
        Assert.Null(locks.HolderOf(box[0], T0 + EditLocks.Lease));
        Assert.True(locks.TryAcquire(box, Bia, T0 + EditLocks.Lease, out _));
        Assert.Equal(Bia, locks.HolderOf(box[0], T0 + EditLocks.Lease));
    }

    [Fact]
    public void AcquiringASetIsAllOrNothing()
    {
        var locks = new EditLocks(); var wall = Ids(3);
        locks.TryAcquire([wall[1]], Bia, T0, out _);
        Assert.False(locks.TryAcquire(wall, Ana, T0, out var holder)); Assert.Equal(Bia, holder);
        Assert.Null(locks.HolderOf(wall[0], T0)); Assert.Null(locks.HolderOf(wall[2], T0)); // none of them was taken
    }

    [Fact]
    public void ReleaseOnlyAffectsTheCallersOwnLeases()
    {
        var locks = new EditLocks(); var ids = Ids(2);
        locks.TryAcquire([ids[0]], Ana, T0, out _); locks.TryAcquire([ids[1]], Bia, T0, out _);
        Assert.Equal(new[] { ids[0] }, locks.Release(ids, Ana));
        Assert.Null(locks.HolderOf(ids[0], T0)); Assert.Equal(Bia, locks.HolderOf(ids[1], T0));
    }

    [Fact]
    public void LeavingTheIslandFreesEverything()
    {
        var locks = new EditLocks(); var mine = Ids(4); var yours = Ids(1);
        locks.TryAcquire(mine, Ana, T0, out _); locks.TryAcquire(yours, Bia, T0, out _);
        var freed = locks.ReleaseAll(Ana);
        Assert.Equal(mine.OrderBy(x => x), freed.OrderBy(x => x)); Assert.Equal(1, locks.Count);
    }

    [Fact]
    public void ExpireReportsWhatWasFreed()
    {
        var locks = new EditLocks(); var ids = Ids(2);
        locks.TryAcquire([ids[0]], Ana, T0, out _); locks.TryAcquire([ids[1]], Bia, T0.AddSeconds(10), out _);
        var gone = locks.Expire(T0 + EditLocks.Lease);
        Assert.Equal(new[] { (ids[0], Ana) }, gone); Assert.Equal(1, locks.Count);
    }

    [Fact]
    public void FirstHeldByOther_IgnoresTheCallersOwnLeases()
    {
        var locks = new EditLocks(); var ids = Ids(3);
        locks.TryAcquire([ids[0]], Ana, T0, out _); locks.TryAcquire([ids[1]], Bia, T0, out _);
        Assert.Equal((ids[1], Bia), locks.FirstHeldByOther(ids, Ana, T0));
        Assert.Null(locks.FirstHeldByOther([ids[0], ids[2]], Ana, T0));
        Assert.Equal((ids[0], Ana), locks.FirstHeldByOther(ids, Bia, T0));
    }

    [Fact]
    public void OnePlayerCannotLockTheWholeIsland()
    {
        var locks = new EditLocks();
        Assert.True(locks.TryAcquire(Ids(EditLocks.MaxPerUser), Ana, T0, out _));
        Assert.False(locks.TryAcquire(Ids(1), Ana, T0, out _));
        Assert.True(locks.TryAcquire(Ids(10), Bia, T0, out _));
    }

    [Fact]
    public void SnapshotListsOnlyLiveLeases()
    {
        var locks = new EditLocks(); var ids = Ids(2);
        locks.TryAcquire([ids[0]], Ana, T0, out _); locks.TryAcquire([ids[1]], Bia, T0.AddSeconds(15), out _);
        var live = locks.Snapshot(T0.AddSeconds(25));
        Assert.Single(live); Assert.Equal(ids[1], live[0].Object);
    }
}
