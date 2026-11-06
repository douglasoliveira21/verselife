using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

public sealed record IntegrityFinding(string Rule, string Severity, Guid? ItemId, Guid? OfferId, Guid? ObjectId, string Detail, bool Repairable);
public sealed record IntegrityReport(int Checked, List<IntegrityFinding> Findings)
{
    public bool Clean => Findings.Count == 0;
}

/// <summary>
/// Looks for things in the inventory that should never exist, the signs of a duplication or of an item stuck half-way: an item locked by an offer that does not
/// exist, two offers for the same item (either could be accepted, so it could be handed over twice), an owner that is not the last one in the item's history, a
/// copy promised by an accepted offer that is not there, and a thing that cannot be copied sitting in more than one object. It only reads (and says what it found);
/// <see cref="RepairAsync"/> fixes the two kinds that have one safe answer.
/// </summary>
public static class InventoryIntegrity
{
    public const int MaxFindings = 500;

    /// <summary>The findings for one person (their items, their offers, their objects), or for everybody when <paramref name="user"/> is null.</summary>
    public static async Task<IntegrityReport> CheckAsync(LifeVerseDbContext db, Guid? user, CancellationToken ct)
    {
        var findings = new List<IntegrityFinding>(); var inspected = 0;
        bool Full() => findings.Count >= MaxFindings;
        var pending = db.ItemOffers.AsNoTracking().Where(o => o.Status == "pending" && !o.KeepCopy);

        // 1. an item that is "offered" with no offer waiting for an answer
        var offeredItems = db.InventoryItems.AsNoTracking().Where(i => i.Status == ItemRightsService.Offered && i.DeletedAt == null && (user == null || i.OwnerUserId == user));
        foreach (var item in await offeredItems.Where(i => !pending.Any(o => o.ItemId == i.Id)).Take(MaxFindings).ToListAsync(ct))
            findings.Add(new IntegrityFinding("stuck_offered", "error", item.Id, null, null, $"\"{item.DisplayName}\" está reservado por uma oferta que não existe mais: não dá para usar nem oferecer de novo.", true));
        inspected += await offeredItems.CountAsync(ct);

        // 2. a pending hand-over whose item is not locked for it any more (or is not the giver's)
        var theirs = pending.Where(o => user == null || o.FromUserId == user || o.ToUserId == user);
        foreach (var offer in await theirs.Where(o => !db.InventoryItems.Any(i => i.Id == o.ItemId && i.Status == ItemRightsService.Offered && i.OwnerUserId == o.FromUserId && i.DeletedAt == null)).Take(MaxFindings).ToListAsync(ct))
            findings.Add(new IntegrityFinding("orphan_offer", "error", offer.ItemId, offer.Id, null, "Há uma oferta esperando resposta cujo item não está mais reservado para ela (mudou de dono, foi apagado ou liberado).", true));
        inspected += await theirs.CountAsync(ct);

        // 3. two hand-overs of the same item at once: either could be accepted
        var doubled = await pending.GroupBy(o => o.ItemId).Where(g => g.Count() > 1).Select(g => g.Key).Take(MaxFindings).ToListAsync(ct);
        foreach (var itemId in doubled)
        {
            var offers = await pending.Where(o => o.ItemId == itemId).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(ct);
            if (user is { } who && !offers.Any(o => o.FromUserId == who || o.ToUserId == who)) continue;
            foreach (var extra in offers.Skip(1)) findings.Add(new IntegrityFinding("double_offer", "error", itemId, extra.Id, null, "O mesmo item está em mais de uma oferta de entrega ao mesmo tempo; só a mais antiga vale.", true));
        }

        // 4. whoever holds the item is not the last one in its history of owners
        var held = db.InventoryItems.AsNoTracking().Where(i => i.DeletedAt == null && i.Status == ItemRightsService.Active && (user == null || i.OwnerUserId == user));
        var mismatched = await held.Where(i => db.ItemOwnerRecords.Any(r => r.ItemId == i.Id) && db.ItemOwnerRecords.Where(r => r.ItemId == i.Id).OrderByDescending(r => r.At).ThenByDescending(r => r.Id).Select(r => r.UserId).FirstOrDefault() != i.OwnerUserId).Take(MaxFindings).ToListAsync(ct);
        foreach (var item in mismatched) findings.Add(new IntegrityFinding("owner_history_mismatch", "error", item.Id, null, null, $"O dono de \"{item.DisplayName}\" não é o último do histórico de donos: alguém recebeu o item sem que a troca ficasse registrada.", false));
        inspected += await held.CountAsync(ct);

        // 5. an accepted offer that promised a copy which is not there
        var accepted = db.ItemOffers.AsNoTracking().Where(o => o.Status == "accepted" && o.KeepCopy && (user == null || o.FromUserId == user || o.ToUserId == user));
        foreach (var offer in await accepted.Where(o => o.ResultItemId == null || !db.InventoryItems.Any(i => i.Id == o.ResultItemId)).Take(MaxFindings).ToListAsync(ct))
            findings.Add(new IntegrityFinding("missing_copy", "error", offer.ItemId, offer.Id, null, "Uma oferta aceita prometia uma cópia que não existe.", false));
        inspected += await accepted.CountAsync(ct);

        // 6. something that cannot be copied, in more than one object of its holder
        var uses = new Dictionary<(Guid Owner, Guid Asset), List<Guid>>();
        foreach (var o in await db.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null && (user == null || o.OwnerUserId == user) && (o.PrimitiveType == "mesh" || o.ContentJson.Length > 2)).Select(o => new { o.Id, o.OwnerUserId, o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.ContentJson }).ToListAsync(ct))
            foreach (var single in ObjectRights.PartsOf(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.ContentJson).Singles) { if (!uses.TryGetValue((o.OwnerUserId, single), out var list)) uses[(o.OwnerUserId, single)] = list = []; list.Add(o.Id); }
        inspected += uses.Count;
        foreach (var ((owner, asset), objects) in uses.Where(u => u.Value.Count > 1))
        {
            if (Full()) break; if (await ObjectRights.CanBeInManyAsync(db, owner, asset, ct)) continue;
            foreach (var objectId in objects.Skip(1)) findings.Add(new IntegrityFinding("uncopyable_in_many_objects", "error", null, null, objectId, "Uma peça que não pode ser copiada está em mais de um objeto da mesma pessoa: é uma cópia que não devia existir.", false));
        }
        return new IntegrityReport(inspected, [.. findings.Take(MaxFindings)]);
    }

    /// <summary>Fixes what has one safe answer, for one person: gives back an item reserved for an offer that is gone, and closes an offer whose item is not reserved for it (or one of two for the same item, keeping the oldest). Everything else stays as it is, and is only reported.</summary>
    public static async Task<(int Released, int Closed)> RepairAsync(LifeVerseDbContext db, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        int released = 0, closed = 0;
        foreach (var item in await db.InventoryItems.Where(i => i.OwnerUserId == user && i.Status == ItemRightsService.Offered && i.DeletedAt == null && !db.ItemOffers.Any(o => o.ItemId == i.Id && o.Status == "pending" && !o.KeepCopy)).ToListAsync(ct))
        {
            item.Status = ItemRightsService.Active; item.Revision++; item.UpdatedAt = now; released++;
            ItemRightsService.Audit(db, user, "repair", "ok", item.Id, item.AssetId, user, null, "released: no offer was waiting for it", now);
        }
        var offers = await db.ItemOffers.Where(o => o.Status == "pending" && !o.KeepCopy && o.FromUserId == user).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(ct); var keep = new HashSet<Guid>();
        foreach (var offer in offers)
        {
            var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == offer.ItemId, ct);
            var locked = item is { DeletedAt: null } && item.OwnerUserId == offer.FromUserId && item.Status == ItemRightsService.Offered;
            if (locked && keep.Add(offer.ItemId)) continue; // the first good one for an item is the one that counts
            offer.Status = "cancelled"; offer.RespondedAt = now; offer.Revision++; closed++;
            ItemRightsService.Audit(db, user, "repair", "ok", offer.ItemId, offer.AssetId, user, offer.ToUserId, "offer closed: its item was not reserved for it, or another offer for the same item came first", now);
        }
        try { await db.SaveChangesAsync(ct); } catch (Exception exception) when (RaceFailures.IsRace(exception)) { db.ChangeTracker.Clear(); return (0, 0); } // somebody answered at the same moment: look again
        return (released, closed);
    }
}

/// <summary>Checks everybody's inventory now and then and says in the log when something is wrong, so a duplication is noticed even if nobody asks.</summary>
public sealed class InventoryIntegrityWorker(IServiceScopeFactory scopes, ILogger<InventoryIntegrityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                using var scope = scopes.CreateScope(); var report = await InventoryIntegrity.CheckAsync(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), null, stoppingToken);
                if (!report.Clean) logger.LogWarning("event=inventory_integrity findings={Count} rules={Rules}", report.Findings.Count, string.Join(",", report.Findings.Select(f => f.Rule).Distinct()));
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning(exception, "event=inventory_integrity_failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
