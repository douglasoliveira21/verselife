using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Groups;

public sealed record ContributeRequest(long Amount, string? Note, string? IdempotencyKey);
public sealed record WithdrawRequest(long Amount, Guid? UserId, string? Reason, string? IdempotencyKey);
public sealed record FrozenRequest(bool Frozen);
public sealed record GroupProductRequest(Guid? AssetId, string? Name, string? Description, string? Category, long? PriceCoins, string? ImageUrl, string? PermissionsJson);
public sealed record GroupStoreRequest(Guid? IslandId, Guid? ProductId, Guid? ObjectId, string? DisplayName);

/// <summary>
/// Groups 2.0, economy: the chest of the group (voluntary contributions in, sales of the group's products in, withdrawals to members out), with
/// every movement written in a ledger that is never edited, and the products and shops the group sells through the marketplace. Money never
/// moves without a line: the balance, the group ledger and the wallet-side ledger change together or not at all.
/// </summary>
public static partial class GroupService
{
    public sealed record TreasuryLine(Guid Id, string Type, long Amount, long BalanceAfter, string Actor, string? Target, string Note, DateTimeOffset At);
    public sealed record ProductItem(Guid Id, string Name, string Description, string Category, long PriceCoins, string Status, string Publisher, string? ImageUrl, int Sold, DateTimeOffset UpdatedAt);
    public sealed record StoreItem(Guid Id, Guid IslandId, string IslandName, Guid ProductId, string ProductName, string DisplayName);

    private static async Task<GroupTreasury> TreasuryOfAsync(LifeVerseDbContext db, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var treasury = await db.GroupTreasuries.SingleOrDefaultAsync(t => t.GroupId == groupId, ct); if (treasury is not null) return treasury;
        treasury = new GroupTreasury { GroupId = groupId, UpdatedAt = now }; db.GroupTreasuries.Add(treasury); return treasury;
    }

    private static async Task<Wallet> WalletOfAsync(LifeVerseDbContext db, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.UserId == user, ct); if (wallet is not null) return wallet;
        wallet = new Wallet { Id = Guid.NewGuid(), UserId = user, CreatedAt = now, UpdatedAt = now }; db.Wallets.Add(wallet); return wallet;
    }

    private static async Task<LedgerAccount> UserAccountAsync(LifeVerseDbContext db, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        var account = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.UserId == user && a.AccountType == "user", ct); if (account is not null) return account;
        account = new LedgerAccount { Id = Guid.NewGuid(), UserId = user, AccountType = "user", CreatedAt = now }; db.LedgerAccounts.Add(account); return account;
    }

    /// <summary>The account of the chest of a group in the platform ledger (made when first needed).</summary>
    public static async Task<LedgerAccount> TreasuryAccountAsync(LifeVerseDbContext db, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var account = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.GroupId == groupId && a.AccountType == "group_treasury", ct); if (account is not null) return account;
        account = new LedgerAccount { Id = Guid.NewGuid(), GroupId = groupId, AccountType = "group_treasury", CreatedAt = now }; db.LedgerAccounts.Add(account); return account;
    }

    private static string Key(Guid groupId, string key) => $"grp:{groupId:N}:{key}";
    private static bool KeyOk(string? key) => key is { Length: >= 8 and <= 64 };

    private static void Move(LifeVerseDbContext db, GroupTreasury treasury, Guid groupId, string type, long signed, Guid? actor, Guid? target, string note, Guid? reference, Guid? transaction, DateTimeOffset now)
    {
        treasury.Balance += signed; treasury.UpdatedAt = now; if (signed > 0) treasury.TotalIn += signed; else treasury.TotalOut -= signed;
        db.GroupLedger.Add(new GroupLedgerEntry { Id = Guid.NewGuid(), GroupId = groupId, Type = type, Amount = signed, BalanceAfter = treasury.Balance, ActorUserId = actor, TargetUserId = target, Note = note, RefId = reference, LedgerTransactionId = transaction, CreatedAt = now });
    }

    /// <summary>Called by the marketplace when something the group sells is bought: the net price goes into the chest.</summary>
    public static async Task CreditSaleAsync(LifeVerseDbContext db, Guid groupId, long net, Guid orderId, Guid buyer, string productName, Guid ledgerTransaction, DateTimeOffset now, CancellationToken ct)
    {
        var treasury = await TreasuryOfAsync(db, groupId, now, ct);
        Move(db, treasury, groupId, "sale", net, buyer, null, productName.Length > 200 ? productName[..200] : productName, orderId, ledgerTransaction, now);
    }

    private static async Task<LedgerTransaction> PostLedgerAsync(LifeVerseDbContext db, string key, string type, Guid initiator, long amount, Guid fromUser, Guid toUser, Guid groupId, bool intoTreasury, DateTimeOffset now, CancellationToken ct)
    {
        var tx = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = key, Type = type, Status = "completed", Amount = amount, InitiatorUserId = initiator, MetadataJson = JsonSerializer.Serialize(new { groupId }), CreatedAt = now, CompletedAt = now };
        db.LedgerTransactions.Add(tx);
        var group = await TreasuryAccountAsync(db, groupId, now, ct); var person = await UserAccountAsync(db, intoTreasury ? fromUser : toUser, now, ct);
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = tx.Id, AccountId = intoTreasury ? person.Id : group.Id, Debit = amount, Credit = 0, CreatedAt = now });
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = tx.Id, AccountId = intoTreasury ? group.Id : person.Id, Debit = 0, Credit = amount, CreatedAt = now });
        return tx;
    }

    // ---- looking ------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The chest as one member sees it: everybody sees what they gave; whoever may view the chest sees the balance, the totals and who gave most.</summary>
    public static async Task<GroupResult> TreasuryAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var actor = ctx!.Actor!; var treasury = await db.GroupTreasuries.AsNoTracking().SingleOrDefaultAsync(t => t.GroupId == groupId, ct);
        var mine = await db.GroupLedger.AsNoTracking().Where(e => e.GroupId == groupId && e.Type == "contribution" && e.ActorUserId == me).SumAsync(e => (long?)e.Amount, ct) ?? 0;
        var canView = GroupRules.Has(actor, GroupPermissions.ViewTreasury) || GroupRules.Has(actor, GroupPermissions.ManageTreasury);
        var canTake = GroupRules.Has(actor, GroupPermissions.ManageTreasury);
        if (!canView) return GroupResult.Done(new { canView = false, canTake, canFreeze = actor.IsLeader, frozen = treasury?.Frozen == true, myContributed = mine });
        var givers = await db.GroupLedger.AsNoTracking().Where(e => e.GroupId == groupId && e.Type == "contribution").GroupBy(e => e.ActorUserId).Select(g => new { User = g.Key, Total = g.Sum(e => e.Amount) }).OrderByDescending(g => g.Total).Take(5).ToListAsync(ct);
        var names = await NamesAsync(db, givers.Where(g => g.User != null).Select(g => g.User!.Value), ct);
        return GroupResult.Done(new
        {
            canView = true, canTake, canFreeze = actor.IsLeader, balance = treasury?.Balance ?? 0, totalIn = treasury?.TotalIn ?? 0, totalOut = treasury?.TotalOut ?? 0, frozen = treasury?.Frozen == true, myContributed = mine,
            top = givers.Select(g => new { name = g.User is { } u ? names.GetValueOrDefault(u, "?") : "?", total = g.Total }).ToList()
        });
    }

    public static async Task<GroupResult> LedgerAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset? before, int take, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        if (!GroupRules.Has(ctx!.Actor!, GroupPermissions.ViewTreasury) && !GroupRules.Has(ctx.Actor!, GroupPermissions.ManageTreasury)) return GroupResult.Fail("forbidden");
        var query = db.GroupLedger.AsNoTracking().Where(e => e.GroupId == groupId); if (before is { } b) query = query.Where(e => e.CreatedAt < b);
        var list = await query.OrderByDescending(e => e.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
        var names = await NamesAsync(db, list.SelectMany(e => new[] { e.ActorUserId, e.TargetUserId }).Where(u => u != null).Select(u => u!.Value), ct);
        return GroupResult.Done(list.Select(e => new TreasuryLine(e.Id, e.Type, e.Amount, e.BalanceAfter, e.ActorUserId is { } a ? names.GetValueOrDefault(a, "?") : "—", e.TargetUserId is { } t ? names.GetValueOrDefault(t, "?") : null, e.Note, e.CreatedAt)).ToList());
    }

    // ---- coins in and out ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>A member gives coins to the chest. Voluntary and final: the coins do not come back if the person leaves.</summary>
    public static async Task<GroupResult> ContributeAsync(LifeVerseDbContext db, Guid me, Guid groupId, long amount, string? note, string? key, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        if (!KeyOk(key)) return GroupResult.Fail("bad_key");
        if (amount < 1 || amount > GroupRules.MaxCoinsPerOperation) return GroupResult.Fail("bad_amount");
        var text = CleanText(note, false); if (text.Length > 120) return GroupResult.Fail("bad_note");
        var ledgerKey = Key(groupId, key!);
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == ledgerKey, ct)) return GroupResult.Done(new { alreadyDone = true });
        var since = now.AddHours(-1);
        if (await db.GroupLedger.CountAsync(e => e.GroupId == groupId && e.Type == "contribution" && e.ActorUserId == me && e.CreatedAt > since, ct) >= GroupRules.ContributionsPerHour) return GroupResult.Fail("rate_limited");
        var wallet = await WalletOfAsync(db, me, now, ct);
        if (wallet.IsBlocked) return GroupResult.Fail("wallet_blocked");
        if (wallet.AvailableCoins < amount) return GroupResult.Fail("insufficient_balance");
        var treasury = await TreasuryOfAsync(db, groupId, now, ct);
        wallet.AvailableCoins -= amount; wallet.UpdatedAt = now;
        var tx = await PostLedgerAsync(db, ledgerKey, "group_contribution", me, amount, me, me, groupId, intoTreasury: true, now, ct);
        Move(db, treasury, groupId, "contribution", amount, me, null, text, null, tx.Id, now);
        Log(db, groupId, me, "treasury_contribution", null, new { amount }, now);
        var saved = await SaveAsync(db, ct, new { balance = treasury.Balance, amount }); return saved;
    }

    /// <summary>Takes coins out of the chest and puts them in the wallet of a member. Needs the cargo, a reason and respects the limits: a few a day, and a share of the chest per day for anyone but the leader. A frozen chest only opens for the leader.</summary>
    public static async Task<GroupResult> WithdrawAsync(LifeVerseDbContext db, Guid me, Guid groupId, long amount, Guid? target, string? reason, string? key, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageTreasury, now, ct); if (error is not null) return error;
        if (!KeyOk(key)) return GroupResult.Fail("bad_key");
        if (amount < 1 || amount > GroupRules.MaxCoinsPerOperation) return GroupResult.Fail("bad_amount");
        var why = CleanText(reason, false); if (why.Length is < 3 or > 200) return GroupResult.Fail("bad_reason");
        var heir = target ?? me;
        if (!await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == heir, ct)) return GroupResult.Fail("bad_target"); // coins only go to members
        var ledgerKey = Key(groupId, key!);
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == ledgerKey, ct)) return GroupResult.Done(new { alreadyDone = true });
        var treasury = await TreasuryOfAsync(db, groupId, now, ct); var leader = ctx!.Actor!.IsLeader;
        if (treasury.Frozen && !leader) return GroupResult.Fail("frozen");
        if (treasury.Balance < amount) return GroupResult.Fail("insufficient_funds");
        if (!leader)
        {
            var since = now.AddDays(-1);
            var recent = await db.GroupLedger.AsNoTracking().Where(e => e.GroupId == groupId && e.Type == "withdrawal" && e.ActorUserId == me && e.CreatedAt > since).ToListAsync(ct);
            if (recent.Count >= GroupRules.MaxWithdrawalsPerDay) return GroupResult.Fail("rate_limited");
            var taken = -recent.Sum(e => e.Amount); var allowance = (treasury.Balance + taken) * GroupRules.WithdrawalDailyShareBps / 10_000;
            if (taken + amount > allowance) return GroupResult.Fail("over_limit", new { available = Math.Max(0, allowance - taken) });
        }
        var wallet = await WalletOfAsync(db, heir, now, ct); if (wallet.IsBlocked) return GroupResult.Fail("wallet_blocked");
        wallet.AvailableCoins += amount; wallet.UpdatedAt = now;
        var tx = await PostLedgerAsync(db, ledgerKey, "group_withdrawal", me, amount, me, heir, groupId, intoTreasury: false, now, ct);
        Move(db, treasury, groupId, "withdrawal", -amount, me, heir, why, null, tx.Id, now);
        Log(db, groupId, me, "treasury_withdrawal", heir, new { amount, reason = why }, now);
        var users = leader ? new List<Guid>() : await AddNotificationsAsync(db, groupId, [ctx.Group.OwnerUserId], me, "group_treasury", "Retirada do cofre", $"{amount} moedas: {why}", now, ct);
        var saved = await SaveAsync(db, ct, new { balance = treasury.Balance, amount }); if (!saved.Ok) return saved;
        foreach (var user in users) await TellAsync(notify, user, "group_treasury", new { groupId, groupName = ctx.Group.Name, amount, reason = why });
        return saved;
    }

    /// <summary>The leader freezes or opens the chest: frozen, nobody else can take coins out (contributions still come in).</summary>
    public static async Task<GroupResult> SetFrozenAsync(LifeVerseDbContext db, Guid me, Guid groupId, bool frozen, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        if (!ctx!.Actor!.IsLeader) return GroupResult.Fail("forbidden");
        var treasury = await TreasuryOfAsync(db, groupId, now, ct); if (treasury.Frozen == frozen) return GroupResult.Done();
        treasury.Frozen = frozen; treasury.FrozenByUserId = frozen ? me : null; treasury.FrozenAt = frozen ? now : null; treasury.UpdatedAt = now;
        Log(db, groupId, me, frozen ? "treasury_frozen" : "treasury_unfrozen", null, new { }, now);
        return await SaveAsync(db, ct);
    }

    // ---- products and shops -------------------------------------------------------------------------------------------------------------------------------------

    public static async Task<GroupResult> ProductsAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var list = await db.MarketplaceProducts.AsNoTracking().Where(p => p.GroupId == groupId).OrderByDescending(p => p.UpdatedAt).Take(200).ToListAsync(ct);
        var ids = list.Select(p => p.Id).ToList(); var sold = await db.MarketplaceOrders.AsNoTracking().Where(o => ids.Contains(o.ProductId) && o.Status == "completed").GroupBy(o => o.ProductId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var names = await NamesAsync(db, list.Select(p => p.SellerUserId), ct);
        return GroupResult.Done(list.Select(p => new ProductItem(p.Id, p.Name, p.Description, p.Category, p.PriceCoins, p.Status, names.GetValueOrDefault(p.SellerUserId, "?"), p.ImageUrl, sold.GetValueOrDefault(p.Id), p.UpdatedAt)).ToList());
    }

    private static string? ProductProblem(string? name, string? description, string? category, long? price, string? image, out string cleanName, out string cleanDescription, out string cleanCategory)
    {
        cleanName = CleanText(name, false); cleanDescription = CleanText(description, true); cleanCategory = CleanText(category, false);
        if (cleanName.Length is < 1 or > 160) return "bad_name";
        if (cleanDescription.Length > 4000) return "bad_description";
        if (cleanCategory.Length > 48) return "bad_category";
        if (price is null or < 0 or > GroupRules.MaxCoinsPerOperation) return "bad_price";
        if (!GroupRules.ValidImageUrl(image)) return "bad_image";
        return null;
    }

    /// <summary>Publishes a product in the marketplace on behalf of the group: the asset is the publisher's, the money goes to the chest.</summary>
    public static async Task<GroupResult> PublishProductAsync(LifeVerseDbContext db, Guid me, Guid groupId, GroupProductRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageStore, now, ct); if (error is not null) return error;
        if (ProductProblem(r.Name, r.Description, r.Category, r.PriceCoins, r.ImageUrl, out var name, out var description, out var category) is { } problem) return GroupResult.Fail(problem);
        if (r.AssetId is not { } asset || !await db.Assets.AnyAsync(a => a.Id == asset && a.OwnerUserId == me && a.DeletedAt == null, ct)) return GroupResult.Fail("bad_asset");
        if (await db.MarketplaceProducts.CountAsync(p => p.GroupId == groupId && p.Status == "active", ct) >= GroupRules.MaxGroupProducts) return GroupResult.Fail("too_many_products");
        var item = new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = me, GroupId = groupId, AssetId = asset, Name = name, Description = description, Category = category.Length == 0 ? "geral" : category, PriceCoins = r.PriceCoins!.Value, ImageUrl = string.IsNullOrEmpty(r.ImageUrl) ? null : r.ImageUrl, PermissionsJson = string.IsNullOrWhiteSpace(r.PermissionsJson) ? "{}" : r.PermissionsJson, Status = "active", Version = 1, CreatedAt = now, UpdatedAt = now };
        db.MarketplaceProducts.Add(item); Log(db, groupId, me, "product_published", null, new { productId = item.Id, name, price = item.PriceCoins }, now);
        return await SaveAsync(db, ct, new { id = item.Id });
    }

    public static async Task<GroupResult> UpdateProductAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid productId, GroupProductRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageStore, now, ct); if (error is not null) return error;
        var item = await db.MarketplaceProducts.SingleOrDefaultAsync(p => p.Id == productId && p.GroupId == groupId && p.Status == "active", ct); if (item is null) return GroupResult.Fail("not_found");
        if (ProductProblem(r.Name ?? item.Name, r.Description ?? item.Description, r.Category ?? item.Category, r.PriceCoins ?? item.PriceCoins, r.ImageUrl ?? item.ImageUrl, out var name, out var description, out var category) is { } problem) return GroupResult.Fail(problem);
        var before = item.PriceCoins;
        item.Name = name; item.Description = description; item.Category = category.Length == 0 ? "geral" : category; item.PriceCoins = r.PriceCoins ?? item.PriceCoins; if (r.ImageUrl is not null) item.ImageUrl = r.ImageUrl.Length == 0 ? null : r.ImageUrl;
        item.Version++; item.UpdatedAt = now; Log(db, groupId, me, "product_updated", null, new { productId, name, from = before, to = item.PriceCoins }, now);
        return await SaveAsync(db, ct);
    }

    public static async Task<GroupResult> RemoveProductAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid productId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageStore, now, ct); if (error is not null) return error;
        var item = await db.MarketplaceProducts.SingleOrDefaultAsync(p => p.Id == productId && p.GroupId == groupId && p.Status == "active", ct); if (item is null) return GroupResult.Fail("not_found");
        item.Status = "inactive"; item.UpdatedAt = now;
        foreach (var store in await db.WorldStores.Where(s => s.ProductId == productId && s.DeletedAt == null).ToListAsync(ct)) { store.DeletedAt = now; store.UpdatedAt = now; }
        Log(db, groupId, me, "product_removed", null, new { productId, name = item.Name }, now);
        return await SaveAsync(db, ct);
    }

    public static async Task<GroupResult> StoresAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var stores = await db.WorldStores.AsNoTracking().Where(s => s.GroupId == groupId && s.DeletedAt == null).OrderBy(s => s.DisplayName).Take(200).ToListAsync(ct);
        var islandIds = stores.Select(s => s.IslandId).Distinct().ToList(); var productIds = stores.Select(s => s.ProductId).Distinct().ToList();
        var islands = await db.Islands.AsNoTracking().Where(i => islandIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var products = await db.MarketplaceProducts.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return GroupResult.Done(stores.Select(s => new StoreItem(s.Id, s.IslandId, islands.GetValueOrDefault(s.IslandId, "?"), s.ProductId, products.GetValueOrDefault(s.ProductId, "?"), s.DisplayName)).ToList());
    }

    /// <summary>Sets up a shop of the group on an island: on land the group owns (or an island of the person), selling a product of the group.</summary>
    public static async Task<GroupResult> CreateStoreAsync(LifeVerseDbContext db, Guid me, Guid groupId, GroupStoreRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageStore, now, ct); if (error is not null) return error;
        var name = CleanText(r.DisplayName, false); if (name.Length is < 1 or > 160) return GroupResult.Fail("bad_name");
        if (r.ProductId is not { } productId || !await db.MarketplaceProducts.AnyAsync(p => p.Id == productId && p.GroupId == groupId && p.Status == "active", ct)) return GroupResult.Fail("bad_product");
        if (r.IslandId is not { } islandId || !await db.Islands.AnyAsync(i => i.Id == islandId && i.DeletedAt == null, ct)) return GroupResult.Fail("bad_island");
        var onGroupLand = await db.IslandParcels.AnyAsync(p => p.IslandId == islandId && p.GroupId == groupId && p.DeletedAt == null, ct);
        if (!onGroupLand && !await db.Islands.AnyAsync(i => i.Id == islandId && i.OwnerUserId == me, ct)) return GroupResult.Fail("bad_island");
        if (await db.WorldStores.CountAsync(s => s.GroupId == groupId && s.DeletedAt == null, ct) >= GroupRules.MaxGroupStores) return GroupResult.Fail("too_many_stores");
        var store = new WorldStore { Id = Guid.NewGuid(), IslandId = islandId, OwnerUserId = me, GroupId = groupId, ProductId = productId, ObjectId = r.ObjectId, DisplayName = name, CreatedAt = now, UpdatedAt = now };
        db.WorldStores.Add(store); Log(db, groupId, me, "store_created", null, new { storeId = store.Id, name, islandId }, now);
        return await SaveAsync(db, ct, new { id = store.Id });
    }

    public static async Task<GroupResult> RemoveStoreAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid storeId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageStore, now, ct); if (error is not null) return error;
        var store = await db.WorldStores.SingleOrDefaultAsync(s => s.Id == storeId && s.GroupId == groupId && s.DeletedAt == null, ct); if (store is null) return GroupResult.Fail("not_found");
        store.DeletedAt = now; store.UpdatedAt = now; Log(db, groupId, me, "store_removed", null, new { storeId, name = store.DisplayName }, now);
        return await SaveAsync(db, ct);
    }

    /// <summary>A group that ends stops selling.</summary>
    private static async Task EndGroupShopsAsync(LifeVerseDbContext db, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var product in await db.MarketplaceProducts.Where(p => p.GroupId == groupId && p.Status == "active").ToListAsync(ct)) { product.Status = "inactive"; product.UpdatedAt = now; }
        foreach (var store in await db.WorldStores.Where(s => s.GroupId == groupId && s.DeletedAt == null).ToListAsync(ct)) { store.DeletedAt = now; store.UpdatedAt = now; }
    }
}
