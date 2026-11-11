using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// Temporary objects ("Temporário" in the Objeto tab) live for a fixed time and are then removed by the server, whether or not their owner is online. The clock starts when an
/// object becomes temporary, is not restarted by editing it, and is cancelled when the object stops being temporary. The island hears about the removal and shows it to everybody.
/// </summary>
public static class TemporaryObjects
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Starts, keeps or cancels the clock of an object after a change.</summary>
    public static void Refresh(BuildObject item, DateTimeOffset now)
    {
        if (ObjectPhysics.IsTemporary(item.PhysicsJson)) item.ExpiresAt ??= now + Lifetime; else item.ExpiresAt = null;
    }

    /// <summary>Removes every object whose time is up, one operation each (so the history shows who it belonged to). Returns what was removed.</summary>
    public static async Task<IReadOnlyList<BuildObject>> SweepAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.BuildObjects.Where(x => x.DeletedAt == null && x.ExpiresAt != null && x.ExpiresAt <= now).Take(500).ToListAsync(ct);
        if (due.Count == 0) return due;
        foreach (var item in due)
        {
            item.DeletedAt = now; item.Revision++; item.UpdatedAt = now;
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = item.IslandId, ObjectId = item.Id, ActorUserId = item.OwnerUserId, Operation = "expire", PayloadJson = "{}", Revision = item.Revision, CreatedAt = now });
        }
        // what hung under an expired object keeps hanging from nothing: it becomes a root of its own set
        var ids = due.Select(d => d.Id).ToHashSet();
        foreach (var child in await db.BuildObjects.Where(x => x.DeletedAt == null && x.ParentObjectId != null && ids.Contains(x.ParentObjectId!.Value)).ToListAsync(ct)) { child.ParentObjectId = null; child.Revision++; child.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
        return due;
    }
}

public sealed class TemporaryObjectWorker(IServiceScopeFactory scopes, ILogger<TemporaryObjectWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await using var scope = scopes.CreateAsyncScope(); var removed = await TemporaryObjects.SweepAsync(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), DateTimeOffset.UtcNow, stoppingToken); if (removed.Count > 0) logger.LogInformation("event=temporary_objects_removed count={Count}", removed.Count); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Temporary objects sweep failed"); }
        }
    }
}
