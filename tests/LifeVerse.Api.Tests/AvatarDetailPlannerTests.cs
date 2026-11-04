using LifeVerse.Shared.Avatar;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Avatar 2.0, fifth part: how much of each avatar is drawn, by distance, visibility and a budget for the whole scene.</summary>
public sealed class AvatarDetailPlannerTests
{
    private static readonly DetailBudget Budget = new();
    private static DetailCandidate Near(int id, float distance, bool visible = true, int full = 40_000, int reduced = 30_000, AvatarDetail previous = AvatarDetail.Full) => new(id, distance, visible, full, reduced, false, previous);

    [Theory]
    [InlineData(5f, AvatarDetail.Full)] [InlineData(24f, AvatarDetail.Full)] [InlineData(40f, AvatarDetail.Reduced)] [InlineData(59f, AvatarDetail.Reduced)] [InlineData(90f, AvatarDetail.Basic)] [InlineData(119f, AvatarDetail.Basic)] [InlineData(150f, AvatarDetail.Hidden)]
    public void DistanceAsksForLessDetailTheFartherAvatarIs(float distance, AvatarDetail expected) => Assert.Equal(expected, AvatarDetailPlanner.ByDistance(distance, true, AvatarDetail.Basic.Next(distance), Budget));

    [Fact]
    public void NobodyFlickersAtTheEdgeOfARange()
    {
        // an avatar that is Full stays Full a bit past 25 m; one that is Reduced needs to be clearly inside 25 m to become Full
        Assert.Equal(AvatarDetail.Full, AvatarDetailPlanner.ByDistance(27f, true, AvatarDetail.Full, Budget));
        Assert.Equal(AvatarDetail.Reduced, AvatarDetailPlanner.ByDistance(27f, true, AvatarDetail.Reduced, Budget));
        Assert.Equal(AvatarDetail.Reduced, AvatarDetailPlanner.ByDistance(23f, true, AvatarDetail.Reduced, Budget));
        Assert.Equal(AvatarDetail.Full, AvatarDetailPlanner.ByDistance(20f, true, AvatarDetail.Reduced, Budget));
        // walking back and forth around the edge changes the detail at most once in each direction
        var tier = AvatarDetail.Full; var changes = 0;
        foreach (var distance in new[] { 22f, 24f, 26f, 27f, 26f, 25f, 24f, 26f, 28f, 26f, 24f, 22f, 21f })
        {
            var next = AvatarDetailPlanner.ByDistance(distance, true, tier, Budget); if (next != tier) changes++; tier = next;
        }
        Assert.True(changes <= 2, $"{changes} changes");
    }

    [Fact]
    public void SomebodyOffScreenIsNeverDrawnInFull()
    {
        Assert.Equal(AvatarDetail.Basic, AvatarDetailPlanner.ByDistance(10f, false, AvatarDetail.Full, Budget));
        Assert.Equal(AvatarDetail.Basic, AvatarDetailPlanner.ByDistance(50f, false, AvatarDetail.Reduced, Budget));
        Assert.Equal(AvatarDetail.Hidden, AvatarDetailPlanner.ByDistance(200f, false, AvatarDetail.Basic, Budget));
    }

    [Fact]
    public void ThePlayerIsAlwaysFull()
    {
        var plan = AvatarDetailPlanner.Plan([new DetailCandidate(1, 0f, true, 500_000, 400_000, true, AvatarDetail.Hidden), Near(2, 5f)], new DetailBudget { Triangles = 10_000, MaxFull = 0 });
        Assert.Equal(AvatarDetail.Full, plan[1]); Assert.True(plan[2] <= AvatarDetail.Basic);
    }

    [Fact]
    public void TheClosestAreServedFirstAndTheFarthestLoseDetailWhenTheBudgetRunsOut()
    {
        var crowd = Enumerable.Range(0, 10).Select(i => Near(i, 3f + i * 2f)).ToList(); // ten people between 3 and 21 m, each costing 40k in full
        var plan = AvatarDetailPlanner.Plan(crowd, new DetailBudget { Triangles = 130_000 });
        Assert.Equal(AvatarDetail.Full, plan[0]); Assert.Equal(AvatarDetail.Full, plan[1]); Assert.Equal(AvatarDetail.Full, plan[2]); // 3 x 40k = 120k fit
        Assert.True(plan[9] < AvatarDetail.Full); // the farthest was cut down
        for (var i = 1; i < 10; i++) Assert.True(plan[i] <= plan[i - 1], "detail never grows with distance");
        var spent = crowd.Sum(c => plan[c.Id] switch { AvatarDetail.Full => c.FullCost, AvatarDetail.Reduced => c.ReducedCost, AvatarDetail.Basic => Budget.BasicCost, _ => 0 });
        Assert.True(spent <= 130_000, $"{spent} spent");
    }

    [Fact]
    public void ThereIsALimitOnFullAndReducedAvatarsEvenWhenTheyAreCheap()
    {
        var cheap = Enumerable.Range(0, 60).Select(i => new DetailCandidate(i, 5f + i * .1f, true, 3_000, 3_000)).ToList();
        var plan = AvatarDetailPlanner.Plan(cheap, new DetailBudget { Triangles = 10_000_000 });
        Assert.Equal(10, plan.Values.Count(v => v == AvatarDetail.Full)); Assert.Equal(24, plan.Values.Count(v => v == AvatarDetail.Reduced)); Assert.Equal(26, plan.Values.Count(v => v == AvatarDetail.Basic));
        Assert.All(Enumerable.Range(0, 10), i => Assert.Equal(AvatarDetail.Full, plan[i])); // the nearest ten are the Full ones
    }

    [Fact]
    public void AnAvatarOutsideEveryRangeCostsNothing()
    {
        var plan = AvatarDetailPlanner.Plan([Near(1, 500f), Near(2, 10f)], new DetailBudget { Triangles = 45_000 });
        Assert.Equal(AvatarDetail.Hidden, plan[1]); Assert.Equal(AvatarDetail.Full, plan[2]);
    }

    [Fact]
    public void ThePlanIsStableAndDoesNotDependOnTheOrderOfTheList()
    {
        var list = Enumerable.Range(0, 30).Select(i => Near(i, 4f + i * 3f, i % 4 != 0, 20_000 + i * 500, 12_000)).ToList();
        var first = AvatarDetailPlanner.Plan(list); var shuffled = AvatarDetailPlanner.Plan([.. list.OrderBy(c => (c.Id * 7919) % 31)]);
        Assert.Equal(first.OrderBy(p => p.Key), shuffled.OrderBy(p => p.Key));
        Assert.Equal(first.OrderBy(p => p.Key), AvatarDetailPlanner.Plan(list).OrderBy(p => p.Key));
    }

    [Fact]
    public void WithNobodyThereThePlanIsEmpty() => Assert.Empty(AvatarDetailPlanner.Plan([]));

    [Fact]
    public void ACustomAvatarCostsWhatItsPartsAdd()
    {
        Assert.Equal(52_000, AvatarDetailPlanner.RenderCost(40_000, [8_000, 4_000]));
        Assert.Equal(40_000, AvatarDetailPlanner.RenderCost(40_000, [-5, 0]));
        Assert.Equal(0, AvatarDetailPlanner.RenderCost(-3, []));
    }
}

internal static class DetailTestHelpers
{
    /// <summary>The detail an avatar would already have at this distance if it had been standing there a while (so the test measures the distance and not the stickiness).</summary>
    public static AvatarDetail Next(this AvatarDetail _, float distance) => distance <= 25 ? AvatarDetail.Full : distance <= 60 ? AvatarDetail.Reduced : distance <= 120 ? AvatarDetail.Basic : AvatarDetail.Hidden;
}
