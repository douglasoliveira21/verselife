using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record PrepareTeleportRequest(Guid DestinationIslandId, Guid? SourceIslandId, float? X, float? Z, string IdempotencyKey);

/// <summary>What a step of a trip answered: an HTTP status, a code the client can act on, and the transfer when there is one.</summary>
public sealed record TeleportOutcome(int Status, string Code, TeleportTransfer? Transfer = null, string? Detail = null);

/// <summary>
/// Travelling between islands as a controlled transfer: prepare (validate and reserve a place) → connect (a single-use token for that trip) → the destination
/// server confirms the arrival → the previous island is released. Every step can be repeated without harm, the reservation expires, a person has one trip at a
/// time (the database refuses a second one), and nothing takes the person off their current island before the arrival is confirmed.
/// </summary>
public static class TeleportService
{
    public const int ReservationSeconds = 90;
    public static readonly string[] ActiveStatuses = ["prepared", "connecting"];

    public static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 64 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Turns reservations that ran out of time into "expired", so they stop counting against the destination.</summary>
    public static async Task ExpireAsync(LifeVerseDbContext db, DateTimeOffset now, Guid? userId, CancellationToken ct)
    {
        var late = await db.TeleportTransfers.Where(t => ActiveStatuses.Contains(t.Status) && t.ExpiresAt <= now && (userId == null || t.UserId == userId)).ToListAsync(ct);
        if (late.Count == 0) return;
        foreach (var transfer in late) { transfer.Status = "expired"; transfer.FailureCode = "reservation_expired"; }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> CanEnterAsync(LifeVerseDbContext db, Island island, Guid userId, CancellationToken ct) =>
        island.IsPublic || island.OwnerUserId == userId ||
        await db.IslandPermissions.AsNoTracking().AnyAsync(p => p.IslandId == island.Id && p.UserId == userId && p.RevokedAt == null && (p.Permission == "enter" || p.Permission == "build" || p.Permission == "admin"), ct);

    /// <summary>How many places of an island are taken: people its server reports plus the reservations of other trips going there.</summary>
    public static async Task<int> TakenPlacesAsync(LifeVerseDbContext db, Guid islandId, Guid exceptUser, DateTimeOffset now, CancellationToken ct)
    {
        var present = await db.ServerInstances.AsNoTracking().Where(s => s.IslandId == islandId && s.StoppedAt == null && s.Status == "healthy" && s.LastHeartbeatAt > now.AddSeconds(-45))
            .OrderByDescending(s => s.LastHeartbeatAt).Select(s => s.PlayerCount).FirstOrDefaultAsync(ct);
        var reserved = await db.TeleportTransfers.AsNoTracking().CountAsync(t => t.DestinationIslandId == islandId && t.UserId != exceptUser && ActiveStatuses.Contains(t.Status) && t.ExpiresAt > now, ct);
        return present + reserved;
    }

    public static async Task<TeleportOutcome> PrepareAsync(LifeVerseDbContext db, Guid userId, Guid sessionId, PrepareTeleportRequest request, DateTimeOffset now, CancellationToken ct)
    {
        if (!ValidKey(request.IdempotencyKey)) return new(400, "invalid_key");
        // the same trip asked again gives the same answer
        var again = await db.TeleportTransfers.SingleOrDefaultAsync(t => t.UserId == userId && t.IdempotencyKey == request.IdempotencyKey, ct);
        if (again is not null) { if (ActiveStatuses.Contains(again.Status) && again.ExpiresAt <= now) { again.Status = "expired"; again.FailureCode = "reservation_expired"; await db.SaveChangesAsync(ct); } return new(200, again.Status, again); }

        await ExpireAsync(db, now, userId, ct);
        var active = await db.TeleportTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && ActiveStatuses.Contains(t.Status), ct);
        if (active is not null) return new(409, "transfer_in_progress", active);

        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == request.DestinationIslandId && i.DeletedAt == null, ct);
        if (island is null) return new(404, "island_not_found");
        if (request.SourceIslandId == island.Id) return new(409, "same_island");
        if (!await CanEnterAsync(db, island, userId, ct)) return new(403, island.IsPublic ? "access_denied" : "island_private");
        if (request.X is { } x && request.Z is { } z)
        {
            var half = island.Size / 2f;
            if (!float.IsFinite(x) || !float.IsFinite(z) || Math.Abs(x) > half || Math.Abs(z) > half) return new(400, "invalid_destination");
        }
        else if (request.X is not null || request.Z is not null) return new(400, "invalid_destination");
        if (island.MaxPlayers > 0 && await TakenPlacesAsync(db, island.Id, userId, now, ct) >= island.MaxPlayers) return new(409, "island_full");

        var transfer = new TeleportTransfer
        {
            Id = Guid.NewGuid(), UserId = userId, SessionId = sessionId, SourceIslandId = request.SourceIslandId, DestinationIslandId = island.Id, Status = "prepared", IdempotencyKey = request.IdempotencyKey,
            TargetX = request.X, TargetZ = request.Z, CreatedAt = now, ExpiresAt = now.AddSeconds(ReservationSeconds)
        };
        db.TeleportTransfers.Add(transfer);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // two requests raced: the database kept only one trip for the person
            db.Entry(transfer).State = EntityState.Detached;
            var winner = await db.TeleportTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && (t.IdempotencyKey == request.IdempotencyKey || ActiveStatuses.Contains(t.Status)), ct);
            if (winner is null) throw;
            return winner.IdempotencyKey == request.IdempotencyKey ? new(200, winner.Status, winner) : new(409, "transfer_in_progress", winner);
        }
        return new(201, "prepared", transfer);
    }

    /// <summary>Loads the person's own transfer; expired reservations are marked as such on the way.</summary>
    public static async Task<TeleportTransfer?> OwnAsync(LifeVerseDbContext db, Guid userId, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var transfer = await db.TeleportTransfers.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (transfer is not null && ActiveStatuses.Contains(transfer.Status) && transfer.ExpiresAt <= now) { transfer.Status = "expired"; transfer.FailureCode = "reservation_expired"; await db.SaveChangesAsync(ct); }
        return transfer;
    }

    /// <summary>Gives up on a trip that has not arrived. Cancelling twice is fine; one that arrived cannot be cancelled.</summary>
    public static async Task<TeleportOutcome> CancelAsync(LifeVerseDbContext db, Guid userId, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var transfer = await OwnAsync(db, userId, id, now, ct);
        if (transfer is null) return new(404, "transfer_not_found");
        if (transfer.Status == "arrived") return new(409, "already_arrived", transfer);
        if (ActiveStatuses.Contains(transfer.Status)) { transfer.Status = "cancelled"; transfer.CancelledAt = now; await db.SaveChangesAsync(ct); }
        return new(200, transfer.Status, transfer);
    }

    /// <summary>
    /// The ticket of a trip is accepted once, only for its own trip, user and destination, and only while the reservation and the token last. The update is
    /// atomic, so two servers (or one replayed token) cannot both use it.
    /// </summary>
    public static async Task<TeleportTransfer?> ConsumeTokenAsync(LifeVerseDbContext db, Guid transferId, Guid userId, Guid islandId, Guid tokenId, DateTimeOffset now, CancellationToken ct)
    {
        var transfer = await db.TeleportTransfers.AsNoTracking().SingleOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null || transfer.UserId != userId || transfer.DestinationIslandId != islandId || transfer.Status != "connecting" || transfer.TokenId != tokenId || transfer.TokenExpiresAt <= now || transfer.ExpiresAt <= now) return null;
        var used = await db.TeleportTransfers.Where(t => t.Id == transferId && t.TokenId == tokenId && t.TokenUsedAt == null && t.Status == "connecting").ExecuteUpdateAsync(s => s.SetProperty(t => t.TokenUsedAt, now), ct);
        return used == 1 ? transfer : null;
    }

    /// <summary>The destination server says the person is there (admitted, placed, snapshot sent). Saying it again is harmless; a trip that was cancelled meanwhile is refused so the server lets go.</summary>
    public static async Task<TeleportOutcome> ArrivedAsync(LifeVerseDbContext db, Guid serverIslandId, Guid transferId, DateTimeOffset now, CancellationToken ct)
    {
        var transfer = await db.TeleportTransfers.SingleOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null || transfer.DestinationIslandId != serverIslandId) return new(404, "transfer_not_found");
        if (transfer.Status == "arrived") return new(200, "arrived", transfer);
        if (transfer.Status != "connecting" || transfer.TokenUsedAt is null) return new(409, transfer.Status == "connecting" ? "token_not_used" : transfer.Status, transfer);
        transfer.Status = "arrived"; transfer.ArrivedAt = now; await db.SaveChangesAsync(ct);
        return new(200, "arrived", transfer);
    }

    /// <summary>People the source island must let go of: their trip arrived. Marked as told, so each is released once; the server only drops someone who joined before the arrival.</summary>
    public static async Task<List<ReleasedPlayer>> ReleaseForSourceAsync(LifeVerseDbContext db, Guid sourceIslandId, DateTimeOffset now, CancellationToken ct)
    {
        var pending = await db.TeleportTransfers.Where(t => t.SourceIslandId == sourceIslandId && t.Status == "arrived" && t.SourceReleasedAt == null).ToListAsync(ct);
        if (pending.Count == 0) return [];
        foreach (var transfer in pending) transfer.SourceReleasedAt = now;
        await db.SaveChangesAsync(ct);
        return [.. pending.Select(t => new ReleasedPlayer(t.UserId, t.ArrivedAt!.Value.ToUnixTimeMilliseconds()))];
    }
}

public sealed record ReleasedPlayer(Guid UserId, long ArrivedAtUnixMs);
