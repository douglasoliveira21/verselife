using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>Some things on the island management window belong to the owner alone (the administrators may look but not change them). Somebody who rents the whole island has the owner's powers for as long as the contract runs.</summary>
public static class IslandOwnerOnly
{
    public const string Message = "Só o dono da ilha pode fazer isso.";

    public static IResult Refusal() => Results.Json(new { error = "owner_only", message = Message }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>The owner, or the person who rents the island now.</summary>
    public static async Task<bool> AllowsAsync(LifeVerseDbContext db, Island island, Guid user, DateTimeOffset? now = null)
    {
        if (island.OwnerUserId == user) return true;
        var at = now ?? DateTimeOffset.UtcNow;
        return await db.IslandLeases.AsNoTracking().AnyAsync(l => l.IslandId == island.Id && l.TenantUserId == user && l.Status == "active" && l.ExpiresAt > at);
    }
}
