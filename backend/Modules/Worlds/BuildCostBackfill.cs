using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// Objects that existed before costs were introduced have a cost of 0. On startup they are priced once, in batches, so the island and
/// parcel budgets count what is already built. New objects always carry their cost, so after the first run there is nothing left to do.
/// </summary>
public static class BuildCostBackfill
{
    public static async Task<int> RunAsync(LifeVerseDbContext db, CancellationToken ct = default)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.BuildObjects.Where(x => x.Cost == 0).OrderBy(x => x.Id).Take(500).ToListAsync(ct);
            if (batch.Count == 0) return total;
            foreach (var item in batch) item.Cost = Math.Max(1, BuildCost.Of(item.PrimitiveType, item.GeometryJson, item.MaterialJson, item.PhysicsJson, item.ContentJson));
            await db.SaveChangesAsync(ct);
            total += batch.Count;
        }
    }
}
