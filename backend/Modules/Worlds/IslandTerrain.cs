using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Terrain;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record BackupRequest(string Name);
public sealed record RestoreRequest(string Mode, int? Minutes);

/// <summary>
/// The Terreno tab: what the ground of the island looks like now, named backups of it, and putting a backup back. The island server saves its terrain by itself about once a minute; a backup keeps one of
/// those saves under a name. Restoring needs the island server to stop (it holds the terrain in memory and would overwrite the change), so it is a restart: at the moment the server is told to stop, the
/// backup is written as the newest save, and the server that starts again loads it. Objects, people and the look of the island are not touched.
/// </summary>
public static class IslandTerrainManagement
{
    public const int MaxBackups = 10, NameLimit = 80;

    public static IEndpointRouteBuilder MapIslandTerrainManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/terrain").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", GetAsync); group.MapPost("/backups", BackupAsync); group.MapDelete("/backups/{snapshotId:guid}", DeleteBackupAsync); group.MapPost("/backups/{snapshotId:guid}/restore", RestoreAsync);
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<(Island? Island, Guid User, IResult? Refusal)> AdminOf(Guid islandId, HttpContext context, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (!Who(context, out var user)) return (null, default, Results.Unauthorized());
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return (null, user, Results.NotFound());
        var admin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);
        return admin ? (island, user, null) : (null, user, Results.Forbid());
    }

    /// <summary>The terrain of a save, or null when the save is not a terrain the game can read (so a broken save is never put back).</summary>
    public static TerrainMap? Read(string terrainJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(terrainJson);
            return document.RootElement.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } text && TerrainMap.TryDeserialize(Convert.FromBase64String(text), out var map) ? map : null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException) { return null; }
    }

    private static object? Stats(string terrainJson)
    {
        if (Read(terrainJson) is not { } map) return null;
        var heights = map.Heights; if (heights.Count == 0) return null;
        return new { cells = map.Cells, cellSize = map.CellSize, sizeMeters = map.Cells * map.CellSize, seaLevel = map.SeaLevel, minHeight = heights.Min(), maxHeight = heights.Max(), avgHeight = heights.Average(), vertices = heights.Count };
    }

    /// <summary>A small grid of heights (at most 24 by 24, one decimal) so the screen can draw the ground without receiving the whole map.</summary>
    private static object? Preview(string terrainJson)
    {
        if (Read(terrainJson) is not { } map) return null;
        var n = Math.Min(24, map.VertexCount); var heights = new List<double>(n * n);
        for (var z = 0; z < n; z++) for (var x = 0; x < n; x++) heights.Add(Math.Round(map.HeightAt(x * (map.VertexCount - 1) / Math.Max(1, n - 1), z * (map.VertexCount - 1) / Math.Max(1, n - 1)), 1));
        return new { size = n, heights, seaLevel = map.SeaLevel };
    }

    public static string HashOf(Guid islandId, long version, string terrain, string environment) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{islandId}:{version}:{terrain}:{environment}")));

    public static async Task<IResult> GetAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var latest = await db.IslandSnapshots.AsNoTracking().Where(s => s.IslandId == islandId).OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        var backups = await db.IslandSnapshots.AsNoTracking().Where(s => s.IslandId == islandId && s.BackupName != null).OrderByDescending(s => s.BackupAt).ToListAsync(ct);
        var rows = new List<object>(); foreach (var b in backups) rows.Add(new { id = b.Id, name = b.BackupName, version = b.Version, savedAt = b.CreatedAt, backupAt = b.BackupAt, by = b.BackupByUserId is { } u ? await IslandAudit.NameAsync(db, u, ct) : "", stats = Stats(b.TerrainJson), valid = Read(b.TerrainJson) is not null });
        var active = await db.IslandRestarts.AsNoTracking().Where(r => r.IslandId == islandId && r.RestoreSnapshotId != null && (r.Status == "scheduled" || r.Status == "running")).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        var running = await ServerChannel.RunningAsync(db, islandId, now, ct) is not null;
        return Results.Ok(new
        {
            island = new { size = island.Size, version = island.SnapshotVersion },
            current = latest is null ? null : new { version = latest.Version, savedAt = latest.CreatedAt, hash = latest.IntegrityHash[..Math.Min(12, latest.IntegrityHash.Length)], stats = Stats(latest.TerrainJson), preview = Preview(latest.TerrainJson) },
            backups = rows, limits = new { maxBackups = MaxBackups, nameLimit = NameLimit }, running,
            restoring = active is null ? null : new { restartId = active.Id, status = active.Status, scheduledFor = active.ScheduledFor, snapshotId = active.RestoreSnapshotId },
        });
    }

    public static async Task<IResult> BackupAsync(Guid islandId, BackupRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!; if (!await IslandOwnerOnly.AllowsAsync(db, island, me, now)) return IslandOwnerOnly.Refusal();
        var name = (request.Name ?? "").Trim();
        if (name.Length is 0 or > NameLimit) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"O nome deve ter de 1 a {NameLimit} caracteres."] });
        var latest = await db.IslandSnapshots.Where(s => s.IslandId == islandId).OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        if (latest is null) return Results.Conflict(new { error = "nothing_saved", message = "O terreno ainda não foi salvo pelo servidor da ilha; tente de novo em um minuto." });
        if (Read(latest.TerrainJson) is null) return Results.Conflict(new { error = "unreadable", message = "O último salvamento do terreno não pôde ser lido, então não vale como backup." });
        if (await db.IslandSnapshots.AnyAsync(s => s.IslandId == islandId && s.BackupName != null && s.BackupName.ToLower() == name.ToLower(), ct)) return Results.Conflict(new { error = "name_taken", message = "Já existe um backup com esse nome." });
        if (await db.IslandSnapshots.CountAsync(s => s.IslandId == islandId && s.BackupName != null, ct) >= MaxBackups) return Results.Conflict(new { error = "full", message = $"Você já tem {MaxBackups} backups do terreno. Exclua um para criar outro." });
        latest.BackupName = name; latest.BackupByUserId = me; latest.BackupAt = now;
        IslandAudit.Add(db, islandId, me, "terreno", "backup_created", $"Criou o backup do terreno {name}", name, null, new { version = latest.Version, savedAt = latest.CreatedAt }, now);
        await db.SaveChangesAsync(ct);
        return await GetAsync(islandId, context, db, clock, ct);
    }

    public static async Task<IResult> DeleteBackupAsync(Guid islandId, Guid snapshotId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!; if (!await IslandOwnerOnly.AllowsAsync(db, island, me, now)) return IslandOwnerOnly.Refusal();
        var backup = await db.IslandSnapshots.SingleOrDefaultAsync(s => s.Id == snapshotId && s.IslandId == islandId && s.BackupName != null, ct); if (backup is null) return Results.NotFound();
        if (await db.IslandRestarts.AnyAsync(r => r.IslandId == islandId && r.RestoreSnapshotId == snapshotId && (r.Status == "scheduled" || r.Status == "running"), ct)) return Results.Conflict(new { error = "in_use", message = "Esse backup está agendado para ser restaurado. Cancele a restauração antes de excluir." });
        var name = backup.BackupName!; backup.BackupName = null; backup.BackupByUserId = null; backup.BackupAt = null; // the save stays as part of the trail; it is only no longer kept under a name
        IslandAudit.Add(db, islandId, me, "terreno", "backup_deleted", $"Excluiu o backup do terreno {name}", name, new { version = backup.Version }, null, now);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static async Task<IResult> RestoreAsync(Guid islandId, Guid snapshotId, RestoreRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!; if (!await IslandOwnerOnly.AllowsAsync(db, island, me, now)) return IslandOwnerOnly.Refusal();
        var backup = await db.IslandSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == snapshotId && s.IslandId == islandId && s.BackupName != null, ct); if (backup is null) return Results.NotFound();
        if (Read(backup.TerrainJson) is null) return Results.Conflict(new { error = "unreadable", message = "Esse backup não pôde ser lido e não pode ser restaurado." });
        var minutes = request.Mode switch { "now" => 0, "in" when request.Minutes is { } m && IslandRestartService.AllowedMinutes.Contains(m) => m, _ => -1 };
        if (minutes < 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = ["Escolha quando restaurar."] });
        if (await db.IslandRestarts.AnyAsync(r => r.IslandId == islandId && (r.Status == "scheduled" || r.Status == "running"), ct)) return Results.Conflict(new { error = "active", message = "Já existe uma reinicialização agendada ou em andamento. Cancele ou espere terminar." });
        var running = await ServerChannel.RunningAsync(db, islandId, now, ct) is not null;
        if (!running)
        {
            // No server holds the terrain, so nothing can overwrite it: the backup becomes the newest save right now.
            await ApplyRestoreAsync(db, island.Id, snapshotId, me, now, ct); await db.SaveChangesAsync(ct);
            return Results.Ok(new { applied = true, scheduled = false });
        }
        db.IslandRestarts.Add(new IslandRestart { Id = Guid.NewGuid(), IslandId = islandId, RequestedByUserId = me, Reason = $"Restaurar o terreno: {backup.BackupName}", ScheduledFor = now.AddMinutes(minutes), Status = "scheduled", CreatedAt = now, RestoreSnapshotId = snapshotId });
        IslandAudit.Add(db, islandId, me, "terreno", "restore_requested", minutes == 0 ? $"Pediu a restauração imediata do terreno {backup.BackupName}" : $"Agendou a restauração do terreno {backup.BackupName} para daqui a {minutes} minutos", backup.BackupName!, null, new { version = backup.Version, minutes }, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { applied = false, scheduled = true, minutes });
    }

    /// <summary>Writes the terrain of a backup as the newest save of the island (keeping the look of the island that is saved now). Called when no server holds the terrain, or at the moment the server is told to stop.</summary>
    public static async Task<bool> ApplyRestoreAsync(LifeVerseDbContext db, Guid islandId, Guid snapshotId, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        var backup = await db.IslandSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == snapshotId && s.IslandId == islandId, ct); if (backup is null || Read(backup.TerrainJson) is null) return false;
        var island = await db.Islands.SingleAsync(i => i.Id == islandId, ct);
        var latest = await db.IslandSnapshots.AsNoTracking().Where(s => s.IslandId == islandId).OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        var version = Math.Max(island.SnapshotVersion, latest?.Version ?? 0) + 1; var environment = latest?.EnvironmentJson ?? island.EnvironmentJson;
        db.IslandSnapshots.Add(new IslandSnapshot { Id = Guid.NewGuid(), IslandId = islandId, Version = version, TerrainJson = backup.TerrainJson, EnvironmentJson = environment, IntegrityHash = HashOf(islandId, version, backup.TerrainJson, environment), CreatedAt = now });
        island.SnapshotVersion = version; // a server that still holds the old terrain is refused when it tries to save over this one
        IslandAudit.Add(db, islandId, actor, "terreno", "terrain_restored", $"Restaurou o terreno do backup {backup.BackupName}", backup.BackupName ?? "", null, new { fromVersion = backup.Version, newVersion = version }, now);
        return true;
    }
}
