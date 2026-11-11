using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Platform;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Platform;

/// <summary>
/// The channel between the API and an island server, carried by the heartbeat the server already sends: the server tells who is connected and how it is doing, and the answer brings what an
/// administrator asked it to do. Commands expire after two minutes so an old "remove this person" never fires long after it was asked.
/// </summary>
public static class ServerChannel
{
    public const int MaxPlayers = 300, MaxTextLength = 200;
    public static readonly TimeSpan CommandLifetime = TimeSpan.FromMinutes(2);
    /// <summary>A report older than this is not shown as current.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(60);

    /// <summary>Keeps the report of the heartbeat, cut to a safe size and shape.</summary>
    public static async Task StoreReportAsync(LifeVerseDbContext db, Guid instanceId, ServerReport? report, DateTimeOffset now, CancellationToken ct)
    {
        if (report is null) return;
        var clean = new ServerReport(
            [.. (report.Players ?? []).Where(p => p.UserId != Guid.Empty).Take(MaxPlayers).Select(p => p with { Name = (p.Name ?? "")[..Math.Min((p.Name ?? "").Length, 80)] })],
            Finite(report.Fps), Finite(report.ProcessMs), Finite(report.MemoryMb), Math.Max(0, report.UptimeSeconds));
        var json = JsonSerializer.Serialize(clean, ServerChannelJson.Options);
        var instance = await db.ServerInstances.SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct); if (instance is null) return;
        instance.ReportJson = json; instance.ReportedAt = now; await db.SaveChangesAsync(ct);
    }

    private static double Finite(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1e9) : 0;

    /// <summary>The commands waiting for this island, marked as handed over so the next heartbeat does not repeat them.</summary>
    public static async Task<List<ServerCommand>> TakeCommandsAsync(LifeVerseDbContext db, Guid islandId, DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.IslandServerCommands.Where(c => c.IslandId == islandId && c.DeliveredAt == null && c.ExpiresAt > now).OrderBy(c => c.CreatedAt).Take(50).ToListAsync(ct);
        if (due.Count == 0) return [];
        foreach (var command in due) command.DeliveredAt = now;
        await db.SaveChangesAsync(ct);
        return [.. due.Select(c => new ServerCommand(c.Id, c.Kind, c.TargetUserId, c.Text))];
    }

    public static ServerReport? ReadReport(ServerInstance instance)
    {
        try { return JsonSerializer.Deserialize<ServerReport>(instance.ReportJson, ServerChannelJson.Options); } catch (JsonException) { return null; }
    }

    /// <summary>The running instance of an island (alive in the last 45 seconds), or null.</summary>
    public static Task<ServerInstance?> RunningAsync(LifeVerseDbContext db, Guid islandId, DateTimeOffset now, CancellationToken ct) =>
        db.ServerInstances.AsNoTracking().Where(x => x.IslandId == islandId && x.StoppedAt == null && x.Status == "healthy" && x.LastHeartbeatAt > now.AddSeconds(-45)).OrderByDescending(x => x.LastHeartbeatAt).FirstOrDefaultAsync(ct);
}
