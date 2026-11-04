using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// How much a parcel may hold, by the island's rules. The island has a limit of objects and a budget of cost points; a parcel's share is
/// by area, so the parcels of an island can never add up to more than the island allows. An island can also set "parcelCostPerM2" in its
/// resource limits: then a parcel's cost budget is its area times that number (instead of a fixed worth per object slot).
/// </summary>
public static class LandBudget
{
    public static int IslandObjects(Island island) => BuildCost.Limit(island.ResourceLimitsJson, "objects", 5000);

    /// <summary>The share of the island's object limit that a parcel of this area gets (at least 10).</summary>
    public static int DefaultObjects(Island island, double area)
    {
        var islandArea = Math.Max(1d, (double)island.Size * island.Size);
        return (int)Math.Clamp(Math.Floor(IslandObjects(island) * area / islandArea), 10, 100000);
    }

    public static long? CostBudget(string? resourceLimitsJson, double area)
    {
        var perSquareMetre = BuildCost.Limit(resourceLimitsJson, "parcelCostPerM2", 0);
        return perSquareMetre > 0 ? (long)Math.Ceiling(area * perSquareMetre) : null;
    }
}
