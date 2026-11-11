using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Economy;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// Buying an object that is for sale ("Comprar objeto" / click action Comprar). The whole purchase is decided here, in one transaction: the price is the one the object has now
/// (a buyer who saw another price is told and nothing is charged), the coins move on the ledger (with the platform fee), and what the buyer gets depends on what is sold:
/// the original (the object and its linked parts change owner, the previous owner is remembered, the sale ends), a copy (a construction in the buyer's inventory) or the contents
/// (copies of what the object carries). The buyer always gets the rights the seller marked for the next owner, never more than the seller holds.
/// </summary>
public static partial class BuildOperations
{
    private static async Task<IResult> BuyAsync(ServerBuildOperationRequest request, Island island, Guid buyer, LifeVerseDbContext db, AssetStorageService storage, DateTimeOffset now, CancellationToken ct)
    {
        if (request.ObjectId is not { } objectId || request.Properties is not { ValueKind: JsonValueKind.Object } p) return Refuse(400, "invalid_operation");
        var key = p.TryGetProperty("idempotencyKey", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? string.Empty : string.Empty;
        long? expected = p.TryGetProperty("expectedPrice", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) ? n : null;
        if (key.Length is < 8 or > 128 || expected is null) return Refuse(400, "invalid_operation");

        // the same key twice is the same purchase: it is answered, never charged again
        var previous = await db.LedgerTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (previous is not null) return previous.InitiatorUserId == buyer ? Results.Ok(new { operation = "bought", objectId, buyerUserId = buyer, replay = true, objects = Array.Empty<object>() }) : Refuse(409, "key_reused");

        var item = await db.BuildObjects.SingleOrDefaultAsync(x => x.Id == objectId && x.IslandId == island.Id && x.DeletedAt == null, ct);
        if (item is null) return Refuse(404, "not_found");
        var (type, price) = BuildSharing.Sale(item.PermissionsJson);
        if (type == "none") return Refuse(409, "not_for_sale");
        if (item.OwnerUserId == buyer) return Refuse(409, "own_object");
        if (expected != price) return Results.Json(new { code = "price_changed", error = "price_changed", price, saleType = type }, statusCode: 409);

        var seller = item.OwnerUserId;
        var parts = item.GroupId is { } linkset ? await db.BuildObjects.Where(x => x.IslandId == island.Id && x.GroupId == linkset && x.OwnerUserId == seller && x.DeletedAt == null).ToListAsync(ct) : [item];
        if (!parts.Contains(item)) parts.Add(item);
        if (type == "copy" && parts.Any(o => !ItemRights.IsCopyable(o.OwnerRights))) return Refuse(409, "no_copy");
        if (type == "original" && parts.Any(o => !ItemRights.Has(o.OwnerRights, Right.Transfer))) return Refuse(409, "no_transfer");

        var buyerWallet = await EconomyEndpoints.EnsureWallet(db, buyer, ct); var sellerWallet = await EconomyEndpoints.EnsureWallet(db, seller, ct);
        if (buyerWallet.IsBlocked || sellerWallet.IsBlocked) return Refuse(403, "wallet_blocked");
        if (buyerWallet.AvailableCoins < price) return Refuse(409, "insufficient_balance");
        if (await db.UserBlocks.AnyAsync(b => (b.UserId == buyer && b.BlockedUserId == seller) || (b.UserId == seller && b.BlockedUserId == buyer), ct)) return Refuse(403, "blocked");

        var changed = new List<BuildObject>(); var delivered = new List<string>();
        var arrives = new ItemPermissions(item.OwnerRights, ItemRights.None, item.NextOwnerRights).ForNextOwner();
        switch (type)
        {
            case "original":
                foreach (var o in parts)
                {
                    var oneArrives = new ItemPermissions(o.OwnerRights, ItemRights.None, o.NextOwnerRights).ForNextOwner();
                    o.LastOwnerUserId = o.OwnerUserId; o.OwnerUserId = buyer; o.OwnerRights = oneArrives.Owner; o.NextOwnerRights = oneArrives.NextOwner;
                    o.PermissionsJson = BuildSharing.With(o.PermissionsJson, false, false, false, "none", null); // the sale is over, and so are the sharing choices of the seller
                    if (o.ClickAction == "buy") o.ClickAction = "touch";
                    o.Revision++; o.UpdatedAt = now; changed.Add(o);
                    db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = o.Id, ActorUserId = buyer, Operation = "transfer", PayloadJson = JsonSerializer.Serialize(new { from = seller, to = buyer, price }), Revision = o.Revision, CreatedAt = now });
                }
                foreach (var script in await db.ObjectScripts.Where(s => parts.Select(o => o.Id).Contains(s.ObjectId)).ToListAsync(ct)) { script.OwnerUserId = buyer; script.UpdatedAt = now; } // what runs in it now belongs to the new owner
                delivered.Add(item.Name);
                break;
            case "copy":
            {
                var sources = parts.Select(o => new PackageSourceDto(o.PrimitiveType, o.Name, o.Description, o.PositionX, o.PositionY, o.PositionZ, o.RotationX, o.RotationY, o.RotationZ, o.ScaleX, o.ScaleY, o.ScaleZ, o.MaterialJson, o.GeometryJson, o.PhysicsJson, o.ContentJson, o.GroupId, o.Id, o.ParentObjectId, o.PivotX, o.PivotY, o.PivotZ)).ToList();
                var package = PackageDocument.Create(item.Name, sources);
                var made = await BuildPackageStore.SaveAsync(db, storage, buyer, package, now, ct, arrives, "purchase");
                ItemRightsService.RecordOwner(db, made, buyer, seller, "purchase", now); delivered.Add(made.DisplayName);
                break;
            }
            default: // contents
            {
                foreach (var entry in ContentEntries(item.ContentJson))
                {
                    var made = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = buyer, AssetId = entry.Asset, DisplayName = entry.Name, OwnerRights = arrives.Owner, GroupRights = ItemRights.None, NextOwnerRights = arrives.NextOwner, CreatedAt = now, UpdatedAt = now, Origin = "purchase" };
                    db.InventoryItems.Add(made); ItemRightsService.RecordOwner(db, made, buyer, seller, "purchase", now); delivered.Add(entry.Name);
                }
                if (delivered.Count == 0) return Refuse(409, "nothing_to_sell");
                break;
            }
        }

        var fee = price * EconomyEndpoints.FeeBps / 10000;
        var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = key, Type = "object_purchase", Status = "completed", Amount = price, InitiatorUserId = buyer, MetadataJson = JsonSerializer.Serialize(new { objectId, saleType = type, seller }), CreatedAt = now, CompletedAt = now };
        db.LedgerTransactions.Add(transaction);
        buyerWallet.AvailableCoins -= price; buyerWallet.UpdatedAt = now; sellerWallet.AvailableCoins += price - fee; sellerWallet.UpdatedAt = now;
        var buyerAccount = await EconomyEndpoints.EnsureAccount(db, buyer, "user"); var sellerAccount = await EconomyEndpoints.EnsureAccount(db, seller, "user"); var treasury = await EconomyEndpoints.EnsureAccount(db, null, "treasury");
        db.LedgerEntries.AddRange(
            new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = buyerAccount.Id, Debit = price, Credit = 0, CreatedAt = now },
            new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = sellerAccount.Id, Debit = 0, Credit = price - fee, CreatedAt = now },
            new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = treasury.Id, Debit = 0, Credit = fee, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (RaceFailures.IsRace(exception)) { db.ChangeTracker.Clear(); return Refuse(409, "stale"); } // somebody bought it first, or the same coins were spent: nothing was charged
        return Results.Ok(new { operation = "bought", objectId, saleType = type, price, buyerUserId = buyer, sellerUserId = seller, delivered, objects = changed.Select(BuildEndpoints.ToResponse).ToArray() });
    }

    private static IEnumerable<(Guid Asset, string Name)> ContentEntries(string contentJson)
    {
        JsonNode? node; try { node = JsonNode.Parse(string.IsNullOrWhiteSpace(contentJson) ? "[]" : contentJson); } catch (JsonException) { yield break; }
        if (node is not JsonArray array) yield break;
        foreach (var entry in array.OfType<JsonObject>())
            if (entry["assetId"] is JsonValue a && a.TryGetValue<string>(out var text) && Guid.TryParse(text, out var asset) && entry["category"]?.GetValue<string>() != "scripts") yield return (asset, entry["name"]?.GetValue<string>() ?? "Item");
    }
}
