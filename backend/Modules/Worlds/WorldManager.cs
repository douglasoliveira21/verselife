using System.Collections.Concurrent;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record WorldInstanceDecision(Guid IslandId, string State, Guid? InstanceId, string? Host, int? Port);

public sealed class WorldManager(LifeVerseDbContext db, TimeProvider clock, ILogger<WorldManager> logger)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<WorldInstanceDecision?> EnsureAsync(Guid islandId, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(islandId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct);
            if (island is null) return null;
            var now = clock.GetUtcNow();
            var server = await db.ServerInstances.AsNoTracking().Where(x => x.IslandId == islandId && x.StoppedAt == null && x.LastHeartbeatAt > now.AddSeconds(-45)).OrderByDescending(x => x.LastHeartbeatAt).FirstOrDefaultAsync(ct);
            if (server is not null)
            {
                island.RuntimeState = "running"; island.UpdatedAt = now; await db.SaveChangesAsync(ct);
                return new WorldInstanceDecision(islandId, "running", server.Id, server.Hostname, server.Port);
            }
            if (island.RuntimeState != "starting")
            {
                island.RuntimeState = "starting"; island.UpdatedAt = now; await db.SaveChangesAsync(ct);
                logger.LogInformation("Island {IslandId} queued for instance startup", islandId);
            }
            return new WorldInstanceDecision(islandId, "starting", null, null, null);
        }
        finally { gate.Release(); }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddSeconds(-45);
        var islands = await db.Islands.Where(x => x.DeletedAt == null && x.RuntimeState == "running" && !db.ServerInstances.Any(s => s.IslandId == x.Id && s.StoppedAt == null && s.LastHeartbeatAt > cutoff)).ToListAsync(ct);
        foreach (var island in islands) { island.RuntimeState = "suspended"; island.UpdatedAt = clock.GetUtcNow(); }
        if (islands.Count > 0) await db.SaveChangesAsync(ct);
    }
}
