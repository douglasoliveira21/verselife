namespace LifeVerse.Shared.Avatar;

/// <summary>How much of an avatar is drawn and animated. Each step costs less than the one above it.</summary>
public enum AvatarDetail
{
    /// <summary>Not drawn at all and not animated (far away or out of the budget).</summary>
    Hidden = 0,
    /// <summary>The simple procedural body: no custom body, no custom items.</summary>
    Basic = 1,
    /// <summary>The custom body with the game's animations, but without custom items and without custom clips.</summary>
    Reduced = 2,
    /// <summary>Everything: custom body, custom items, custom animations.</summary>
    Full = 3
}

/// <summary>One avatar the planner has to decide about.</summary>
/// <param name="FullCost">Triangles when drawn in full (custom body and custom items).</param>
/// <param name="ReducedCost">Triangles when drawn reduced (custom body only).</param>
public sealed record DetailCandidate(int Id, float Distance, bool Visible, int FullCost, int ReducedCost, bool IsSelf = false, AvatarDetail Previous = AvatarDetail.Full);

/// <summary>The limits of what a scene may spend on the avatars in it.</summary>
public sealed record DetailBudget
{
    /// <summary>Triangles all the avatars together may cost.</summary>
    public int Triangles { get; init; } = 250_000;
    public int MaxFull { get; init; } = 10;
    public int MaxReduced { get; init; } = 24;
    /// <summary>Up to this distance (metres) an avatar can be Full, then Reduced, then Basic; beyond the last it is Hidden.</summary>
    public float FullRange { get; init; } = 25f;
    public float ReducedRange { get; init; } = 60f;
    public float BasicRange { get; init; } = 120f;
    /// <summary>How far past its range an avatar keeps the detail it has, as a fraction of the range, so it does not flicker at the edge.</summary>
    public float Hysteresis { get; init; } = .15f;
    /// <summary>What a Basic avatar costs (the procedural body).</summary>
    public int BasicCost { get; init; } = 2_500;
}

/// <summary>
/// Decides how much of each avatar in view is drawn: by distance (with some stickiness, so nobody flickers at the edge of a range), by whether it is
/// on screen, and by a budget of triangles and of full-detail avatars for the whole scene. Whoever is closest is served first; when the budget runs out
/// the farthest lose detail first. The avatar of the player is always Full. Pure maths: the same inputs always give the same plan.
/// </summary>
public static class AvatarDetailPlanner
{
    /// <summary>The detail the distance alone asks for, given what the avatar has now (it keeps its detail a little past the edge, and must be well inside a range to gain detail).</summary>
    public static AvatarDetail ByDistance(float distance, bool visible, AvatarDetail previous, DetailBudget budget)
    {
        float Edge(AvatarDetail tier, float range) => previous >= tier ? range * (1 + budget.Hysteresis) : range * (1 - budget.Hysteresis);
        var wanted = distance <= Edge(AvatarDetail.Full, budget.FullRange) ? AvatarDetail.Full
            : distance <= Edge(AvatarDetail.Reduced, budget.ReducedRange) ? AvatarDetail.Reduced
            : distance <= Edge(AvatarDetail.Basic, budget.BasicRange) ? AvatarDetail.Basic : AvatarDetail.Hidden;
        // somebody behind the camera is not worth the expensive parts: they are drawn simply, or not at all when they are far
        if (!visible && wanted > AvatarDetail.Basic) wanted = AvatarDetail.Basic;
        return wanted;
    }

    public static Dictionary<int, AvatarDetail> Plan(IReadOnlyCollection<DetailCandidate> candidates, DetailBudget? budget = null)
    {
        budget ??= new DetailBudget();
        var plan = new Dictionary<int, AvatarDetail>(); long spent = 0; var full = 0; var reduced = 0;
        int Cost(DetailCandidate c, AvatarDetail tier) => tier switch { AvatarDetail.Full => Math.Max(c.FullCost, budget.BasicCost), AvatarDetail.Reduced => Math.Max(c.ReducedCost, budget.BasicCost), AvatarDetail.Basic => budget.BasicCost, _ => 0 };
        // the player is always drawn in full and always counts against the budget
        foreach (var self in candidates.Where(c => c.IsSelf)) { plan[self.Id] = AvatarDetail.Full; spent += Cost(self, AvatarDetail.Full); full++; }
        foreach (var c in candidates.Where(c => !c.IsSelf).OrderBy(c => c.Distance).ThenBy(c => c.Id))
        {
            var tier = ByDistance(c.Distance, c.Visible, c.Previous, budget);
            // from what was asked, step down until it fits the counts and the budget
            while (tier > AvatarDetail.Hidden)
            {
                var fitsCount = tier != AvatarDetail.Full || full < budget.MaxFull;
                fitsCount &= tier != AvatarDetail.Reduced || reduced < budget.MaxReduced;
                if (fitsCount && spent + Cost(c, tier) <= budget.Triangles) break;
                tier--;
            }
            plan[c.Id] = tier; spent += Cost(c, tier);
            if (tier == AvatarDetail.Full) full++; else if (tier == AvatarDetail.Reduced) reduced++;
        }
        return plan;
    }

    /// <summary>The fraction of its triangles a custom body keeps when it is drawn at <see cref="AvatarDetail.Reduced"/> (its geometry is simplified).</summary>
    public const float ReducedRatio = .35f;
    /// <summary>A body with no more triangles than this is not simplified: it is cheap already.</summary>
    public const int ReducedFloor = 4_000;

    /// <summary>How many triangles a custom body of this size costs at <see cref="AvatarDetail.Reduced"/>, for the budget.</summary>
    public static int ReducedTriangles(int bodyTriangles) => bodyTriangles <= ReducedFloor ? Math.Max(0, bodyTriangles) : Math.Max(ReducedFloor, (int)(bodyTriangles * ReducedRatio));

    /// <summary>What an avatar costs to draw, for the budget: its triangles (a procedural body counts as <see cref="DetailBudget.BasicCost"/>).</summary>
    public static int RenderCost(int bodyTriangles, IEnumerable<int> itemTriangles) => Math.Max(0, bodyTriangles) + itemTriangles.Sum(Max0);

    private static int Max0(int value) => Math.Max(0, value);
}
