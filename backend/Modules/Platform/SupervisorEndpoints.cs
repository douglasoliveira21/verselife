using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Platform;

/// <summary>
/// Internal endpoint (service key) for the island supervisor: the islands that players asked to enter but that have
/// no running server yet. The supervisor starts a server process for each and the server registers itself as usual.
/// </summary>
public static class SupervisorEndpoints
{
    public static IEndpointRouteBuilder MapSupervisorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/platform/supervisor/pending", PendingAsync).WithTags("Platform");
        return endpoints;
    }

    private static async Task<IResult> PendingAsync(HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var now = clock.GetUtcNow();
        var alive = now.AddSeconds(-45);
        var giveUp = now.AddMinutes(-WorldManager.StartTimeoutMinutes);
        var pending = await db.Islands.AsNoTracking()
            .Where(x => x.DeletedAt == null && x.RuntimeState == "starting" && x.UpdatedAt >= giveUp && !db.ServerInstances.Any(s => s.IslandId == x.Id && s.StoppedAt == null && s.LastHeartbeatAt > alive))
            .OrderBy(x => x.UpdatedAt)
            .Select(x => new { id = x.Id, name = x.Name, maxPlayers = x.MaxPlayers })
            .Take(50).ToListAsync(ct);
        return Results.Ok(new { islands = pending });
    }
}
