using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using LifeVerse.Api.Modules.Groups;
using LifeVerse.Shared.Groups;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Groups 2.0, fourth part: the chest of the group, contributions and withdrawals with their limits, and what the group sells.</summary>
public sealed class GroupEconomyTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly List<(Guid User, string Type)> _pushed = [];
    private readonly GroupNotifier _notify;
    private readonly Guid _leader, _admin, _member, _other, _outsider, _group;
    private int _keys;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public GroupEconomyTests()
    {
        _notify = (user, type, _) => { _pushed.Add((user, type)); return Task.CompletedTask; };
        (_leader, _admin, _member, _other, _outsider) = (User("lider"), User("admin"), User("membro"), User("outro"), User("forasteiro"));
        var made = GroupService.CreateAsync(_db, _leader, new GroupProfileRequest("Os Comerciantes", "", null, null, null, null, null, null, null), _clock.Now, default).Result;
        _group = JsonSerializer.SerializeToElement(made.Data).GetProperty("id").GetGuid();
        Place(_admin, "admin"); Place(_member, "member"); Place(_other, "member");
        foreach (var u in new[] { _leader, _admin, _member, _other, _outsider }) Fund(u, 10_000);
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name) { var id = Guid.NewGuid(); _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now, UpdatedAt = _clock.Now }); _db.SaveChanges(); return id; }
    private void Place(Guid user, string key) { var role = _db.GroupRoles.Single(r => r.GroupId == _group && r.SystemKey == key); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = _group, UserId = user, Role = key, RoleId = role.Id, JoinedAt = _clock.Now }); _db.SaveChanges(); }
    private void Fund(Guid user, long coins) { _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = user, AvailableCoins = coins, CreatedAt = _clock.Now, UpdatedAt = _clock.Now }); _db.SaveChanges(); }
    private long Coins(Guid user) => _db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;
    private long Chest() => _db.GroupTreasuries.AsNoTracking().SingleOrDefault(t => t.GroupId == _group)?.Balance ?? 0;
    private string Key() => $"chave-{++_keys:D6}";
    private static Guid Id(GroupResult result) => JsonSerializer.SerializeToElement(result.Data).GetProperty("id").GetGuid();
    private static JsonElement J(GroupResult result) => JsonSerializer.SerializeToElement(result.Data, Web);
    private Task<GroupResult> Give(Guid user, long amount, string? note = null, string? key = null) => GroupService.ContributeAsync(_db, user, _group, amount, note, key ?? Key(), _clock.Now, _notify, default);
    private Task<GroupResult> Take(Guid user, long amount, Guid? to = null, string reason = "reembolso", string? key = null) => GroupService.WithdrawAsync(_db, user, _group, amount, to, reason, key ?? Key(), _clock.Now, _notify, default);
    private async Task<List<string>> Actions() => (await _db.GroupAuditEntries.AsNoTracking().Where(e => e.GroupId == _group).OrderBy(e => e.CreatedAt).ToListAsync()).Select(e => e.Action).ToList();

    private async Task Grant(Guid user, params string[] permissions)
    {
        var role = await GroupService.CreateRoleAsync(_db, _leader, _group, new RoleRequest("Cargo" + permissions.Length + user.ToString()[..4], 10, permissions, null), _clock.Now, default); Assert.True(role.Ok, role.Error);
        Assert.True((await GroupService.SetRoleAsync(_db, _leader, _group, user, Id(role), _clock.Now, null, default)).Ok);
    }

    /// <summary>The platform ledger always balances and the chest always equals what the group ledger says.</summary>
    private async Task Balanced()
    {
        Assert.Equal(Chest(), await _db.GroupLedger.AsNoTracking().Where(e => e.GroupId == _group).SumAsync(e => (long?)e.Amount) ?? 0);
        Assert.Equal(await _db.LedgerEntries.AsNoTracking().SumAsync(e => e.Debit), await _db.LedgerEntries.AsNoTracking().SumAsync(e => e.Credit));
    }

    // ---- contributing ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MembersContributeAndTheMoneyMovesOnce()
    {
        var r = await Give(_member, 300, "para a festa"); Assert.True(r.Ok, r.Error);
        Assert.Equal(9_700, Coins(_member)); Assert.Equal(300, Chest());
        var line = await _db.GroupLedger.AsNoTracking().SingleAsync(); Assert.Equal("contribution", line.Type); Assert.Equal(300, line.BalanceAfter); Assert.Equal(_member, line.ActorUserId); Assert.Equal("para a festa", line.Note);
        Assert.Contains("treasury_contribution", await Actions()); await Balanced();
        Assert.Equal("forbidden", (await Give(_outsider, 10)).Error); Assert.Equal(10_000, Coins(_outsider));
    }

    [Fact]
    public async Task ContributionsAreCheckedAndNeverRepeated()
    {
        Assert.Equal("bad_amount", (await Give(_member, 0)).Error); Assert.Equal("bad_amount", (await Give(_member, -5)).Error); Assert.Equal("bad_amount", (await Give(_member, GroupRules.MaxCoinsPerOperation + 1)).Error);
        Assert.Equal("bad_key", (await GroupService.ContributeAsync(_db, _member, _group, 10, null, "curta", _clock.Now, _notify, default)).Error);
        Assert.Equal("bad_note", (await Give(_member, 10, new string('x', 121))).Error);
        Assert.Equal("insufficient_balance", (await Give(_member, 10_001)).Error); Assert.Equal(10_000, Coins(_member));
        var key = Key(); Assert.True((await Give(_member, 100, null, key)).Ok); var again = await Give(_member, 100, null, key);
        Assert.True(again.Ok); Assert.Equal(9_900, Coins(_member)); Assert.Equal(100, Chest()); // the same key is the same gift
        _db.Wallets.Single(w => w.UserId == _other).IsBlocked = true; await _db.SaveChangesAsync();
        Assert.Equal("wallet_blocked", (await Give(_other, 10)).Error);
    }

    [Fact]
    public async Task ContributionsAreRateLimited()
    {
        for (var i = 0; i < GroupRules.ContributionsPerHour; i++) Assert.True((await Give(_member, 1)).Ok);
        Assert.Equal("rate_limited", (await Give(_member, 1)).Error);
        _clock.Now = _clock.Now.AddMinutes(61); Assert.True((await Give(_member, 1)).Ok);
    }

    [Fact]
    public async Task OnlyWhoMayViewSeesTheChestButEveryoneSeesTheirOwnGifts()
    {
        await Give(_member, 300); await Give(_other, 100); await Give(_member, 50);
        var plain = J(await GroupService.TreasuryAsync(_db, _member, _group, _clock.Now, default));
        Assert.False(plain.GetProperty("canView").GetBoolean()); Assert.Equal(350, plain.GetProperty("myContributed").GetInt64()); Assert.False(plain.TryGetProperty("balance", out _));
        var admin = J(await GroupService.TreasuryAsync(_db, _admin, _group, _clock.Now, default)); // administrators view the chest
        Assert.True(admin.GetProperty("canView").GetBoolean()); Assert.Equal(450, admin.GetProperty("balance").GetInt64()); Assert.False(admin.GetProperty("canTake").GetBoolean());
        Assert.Equal("membro", admin.GetProperty("top")[0].GetProperty("name").GetString()); Assert.Equal(350, admin.GetProperty("top")[0].GetProperty("total").GetInt64());
        Assert.Equal("forbidden", (await GroupService.LedgerAsync(_db, _member, _group, null, 50, _clock.Now, default)).Error);
        var ledger = JsonSerializer.SerializeToElement((await GroupService.LedgerAsync(_db, _admin, _group, null, 50, _clock.Now, default)).Data, Web); Assert.Equal(3, ledger.GetArrayLength());
        Assert.Equal("forbidden", (await GroupService.TreasuryAsync(_db, _outsider, _group, _clock.Now, default)).Error);
    }

    // ---- withdrawing --------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheCargoWithdrawsAndOnlyToMembers()
    {
        await Give(_member, 1_000);
        Assert.Equal("forbidden", (await Take(_admin, 50)).Error); Assert.Equal("forbidden", (await Take(_member, 50)).Error);
        await Grant(_admin, GroupPermissions.ManageTreasury);
        Assert.Equal("bad_reason", (await Take(_admin, 50, reason: "x")).Error); Assert.Equal("bad_target", (await Take(_admin, 50, _outsider)).Error); Assert.Equal("bad_amount", (await Take(_admin, 0)).Error);
        Assert.Equal("insufficient_funds", (await Take(_leader, 5_000)).Error);
        Assert.True((await Take(_admin, 50, _other, "prêmio do concurso")).Ok);
        Assert.Equal(10_050, Coins(_other)); Assert.Equal(950, Chest()); Assert.Contains("treasury_withdrawal", await Actions()); await Balanced();
        Assert.Contains(_pushed, p => p.User == _leader && p.Type == "group_treasury"); // the leader is told
    }

    [Fact]
    public async Task AnyoneButTheLeaderIsLimitedPerDay()
    {
        await Give(_member, 1_000); await Grant(_admin, GroupPermissions.ManageTreasury);
        Assert.True((await Take(_admin, 200)).Ok); // 20% of the chest
        var over = await Take(_admin, 1); Assert.Equal("over_limit", over.Error);
        Assert.True((await Take(_leader, 500)).Ok); // the leader has no daily limit
        _clock.Now = _clock.Now.AddHours(25); Assert.True((await Take(_admin, 50)).Ok); // a new day
        await Balanced();
    }

    [Fact]
    public async Task WithdrawalsPerDayAreCounted()
    {
        await Give(_member, 9_000); await Grant(_admin, GroupPermissions.ManageTreasury);
        for (var i = 0; i < GroupRules.MaxWithdrawalsPerDay; i++) Assert.True((await Take(_admin, 10)).Ok);
        Assert.Equal("rate_limited", (await Take(_admin, 10)).Error);
        Assert.True((await Take(_leader, 10)).Ok);
    }

    [Fact]
    public async Task AFrozenChestOpensOnlyForTheLeader()
    {
        await Give(_member, 1_000); await Grant(_admin, GroupPermissions.ManageTreasury);
        Assert.Equal("forbidden", (await GroupService.SetFrozenAsync(_db, _admin, _group, true, _clock.Now, default)).Error);
        Assert.True((await GroupService.SetFrozenAsync(_db, _leader, _group, true, _clock.Now, default)).Ok);
        Assert.Equal("frozen", (await Take(_admin, 10)).Error); Assert.True((await Take(_leader, 10)).Ok); Assert.True((await Give(_other, 5)).Ok); // gifts still come in
        Assert.True(J(await GroupService.TreasuryAsync(_db, _member, _group, _clock.Now, default)).GetProperty("frozen").GetBoolean());
        Assert.True((await GroupService.SetFrozenAsync(_db, _leader, _group, false, _clock.Now, default)).Ok); Assert.True((await Take(_admin, 10)).Ok);
        Assert.Contains("treasury_frozen", await Actions()); Assert.Contains("treasury_unfrozen", await Actions());
    }

    [Fact]
    public async Task AWithdrawalKeyWorksOnlyOnce()
    {
        await Give(_member, 1_000); var key = Key();
        Assert.True((await Take(_leader, 100, null, "um motivo", key)).Ok); Assert.True((await Take(_leader, 100, null, "um motivo", key)).Ok);
        Assert.Equal(900, Chest()); Assert.Equal(10_100, Coins(_leader));
    }

    [Fact]
    public async Task AGroupWithCoinsCannotBeDissolved()
    {
        await Give(_member, 100);
        var blocked = await GroupService.DissolveAsync(_db, _leader, _group, "Os Comerciantes", _clock.Now, _notify, default); Assert.Equal("blocked", blocked.Error);
        Assert.Contains("treasury_not_empty", (List<string>)blocked.Data!);
        Assert.True((await Take(_leader, 100, _member)).Ok);
        Assert.True((await GroupService.DissolveAsync(_db, _leader, _group, "Os Comerciantes", _clock.Now, _notify, default)).Ok);
    }

    // ---- products and shops -------------------------------------------------------------------------------------------------------------------------------

    private Guid Asset(Guid owner) { var id = Guid.NewGuid(); _db.Assets.Add(new Asset { Id = id, OwnerUserId = owner, CreatorUserId = owner, Name = "Cadeira", Category = "furniture", CreatedAt = _clock.Now, UpdatedAt = _clock.Now }); _db.SaveChanges(); return id; }
    private Task<GroupResult> Publish(Guid user, Guid? asset = null, string name = "Cadeira Rara", long price = 100) => GroupService.PublishProductAsync(_db, user, _group, new GroupProductRequest(asset ?? Asset(user), name, "Uma cadeira", "moveis", price, null, null), _clock.Now, default);

    [Fact]
    public async Task OnlyTheStoreCargoPublishesAndTheAssetMustBeTheirs()
    {
        Assert.Equal("forbidden", (await Publish(_member)).Error); Assert.Equal("forbidden", (await Publish(_outsider)).Error);
        Assert.Equal("bad_asset", (await Publish(_admin, Asset(_member))).Error); Assert.Equal("bad_asset", (await Publish(_admin, Guid.NewGuid())).Error);
        Assert.Equal("bad_name", (await Publish(_admin, name: " ")).Error); Assert.Equal("bad_price", (await Publish(_admin, price: -1)).Error);
        var made = await Publish(_admin); Assert.True(made.Ok, made.Error);
        var product = await _db.MarketplaceProducts.AsNoTracking().SingleAsync(); Assert.Equal(_group, product.GroupId); Assert.Equal(_admin, product.SellerUserId); Assert.Equal("active", product.Status);
        Assert.Contains("product_published", await Actions());
        var list = JsonSerializer.SerializeToElement((await GroupService.ProductsAsync(_db, _member, _group, _clock.Now, default)).Data, Web); Assert.Equal("Cadeira Rara", list[0].GetProperty("name").GetString()); Assert.Equal("admin", list[0].GetProperty("publisher").GetString());
        Assert.Equal("forbidden", (await GroupService.ProductsAsync(_db, _outsider, _group, _clock.Now, default)).Error);
    }

    [Fact]
    public async Task ProductsAreUpdatedAndRemovedWithTheirShops()
    {
        var id = Id(await Publish(_admin));
        Assert.Equal("forbidden", (await GroupService.UpdateProductAsync(_db, _member, _group, id, new GroupProductRequest(null, null, null, null, 50, null, null), _clock.Now, default)).Error);
        Assert.True((await GroupService.UpdateProductAsync(_db, _admin, _group, id, new GroupProductRequest(null, "Cadeira Nova", null, null, 250, null, null), _clock.Now, default)).Ok);
        var stored = await _db.MarketplaceProducts.AsNoTracking().SingleAsync(); Assert.Equal(250, stored.PriceCoins); Assert.Equal("Cadeira Nova", stored.Name); Assert.Equal(2, stored.Version);
        Assert.Equal("bad_price", (await GroupService.UpdateProductAsync(_db, _admin, _group, id, new GroupProductRequest(null, null, null, null, -5, null, null), _clock.Now, default)).Error);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _admin, Name = "Feira", Slug = "feira", IsPublic = true }; _db.Islands.Add(island); await _db.SaveChangesAsync();
        Assert.True((await GroupService.CreateStoreAsync(_db, _admin, _group, new GroupStoreRequest(island.Id, id, null, "Banca"), _clock.Now, default)).Ok);
        Assert.True((await GroupService.RemoveProductAsync(_db, _admin, _group, id, _clock.Now, default)).Ok);
        Assert.Equal("inactive", (await _db.MarketplaceProducts.AsNoTracking().SingleAsync()).Status); Assert.NotNull((await _db.WorldStores.AsNoTracking().SingleAsync()).DeletedAt);
        Assert.Equal("not_found", (await GroupService.RemoveProductAsync(_db, _admin, _group, id, _clock.Now, default)).Error);
    }

    [Fact]
    public async Task ShopsStandOnGroupLandOrTheIslandOfTheOne()
    {
        var id = Id(await Publish(_admin)); var theirs = new Island { Id = Guid.NewGuid(), OwnerUserId = _outsider, Name = "Alheia", Slug = "alheia", IsPublic = true }; var mine = new Island { Id = Guid.NewGuid(), OwnerUserId = _admin, Name = "Minha", Slug = "minha", IsPublic = true };
        _db.Islands.AddRange(theirs, mine); await _db.SaveChangesAsync();
        Task<GroupResult> Store(Guid? island, Guid? product = null, string name = "Loja") => GroupService.CreateStoreAsync(_db, _admin, _group, new GroupStoreRequest(island, product ?? id, null, name), _clock.Now, default);
        Assert.Equal("bad_island", (await Store(theirs.Id)).Error); Assert.Equal("bad_island", (await Store(null)).Error); Assert.Equal("bad_island", (await Store(Guid.NewGuid())).Error);
        Assert.Equal("bad_product", (await Store(mine.Id, Guid.NewGuid())).Error); Assert.Equal("bad_name", (await Store(mine.Id, null, " ")).Error);
        Assert.Equal("forbidden", (await GroupService.CreateStoreAsync(_db, _member, _group, new GroupStoreRequest(mine.Id, id, null, "Loja"), _clock.Now, default)).Error);
        Assert.True((await Store(mine.Id)).Ok); // an island of the person
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = theirs.Id, OwnerUserId = _leader, GroupId = _group, Name = "Chão do Grupo", BoundsJson = "{}" }); await _db.SaveChangesAsync();
        var shop = await Store(theirs.Id, null, "Loja no Chão"); Assert.True(shop.Ok, shop.Error); // land the group owns
        var stores = JsonSerializer.SerializeToElement((await GroupService.StoresAsync(_db, _member, _group, _clock.Now, default)).Data, Web); Assert.Equal(2, stores.GetArrayLength());
        Assert.True((await GroupService.RemoveStoreAsync(_db, _admin, _group, Id(shop), _clock.Now, default)).Ok);
        Assert.Equal("forbidden", (await GroupService.RemoveStoreAsync(_db, _member, _group, Id(shop), _clock.Now, default)).Error);
    }

    // ---- selling through the marketplace -----------------------------------------------------------------------------------------------------------------

    private async Task<(int Status, MarketplaceOrder? Order)> Buy(Guid buyer, Guid product, string? key = null)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", buyer.ToString())], "test")) };
        var result = await EconomyEndpoints.PurchaseAsync(product, new PurchaseRequest(key ?? Key()), context, _db, _clock, default);
        return result is Ok<MarketplaceOrder> ok ? (200, ok.Value) : (result is IStatusCodeHttpResult { StatusCode: { } code } ? code : 0, null);
    }

    [Fact]
    public async Task WhatTheGroupSellsEarnsForTheChestNotForThePublisher()
    {
        var id = Id(await Publish(_admin, price: 1_000)); var publisherBefore = Coins(_admin);
        var (status, order) = await Buy(_outsider, id); Assert.Equal(200, status);
        Assert.Equal(9_000, Coins(_outsider)); Assert.Equal(publisherBefore, Coins(_admin)); // the publisher gets nothing personally
        Assert.Equal(950, Chest()); // 5% fee, as in any sale
        var line = await _db.GroupLedger.AsNoTracking().SingleAsync(); Assert.Equal("sale", line.Type); Assert.Equal(950, line.Amount); Assert.Equal(_outsider, line.ActorUserId); Assert.Equal("Cadeira Rara", line.Note);
        Assert.Equal(order!.Id, line.RefId);
        Assert.Single(_db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == _outsider));
        await Balanced();
        var sold = JsonSerializer.SerializeToElement((await GroupService.ProductsAsync(_db, _member, _group, _clock.Now, default)).Data, Web); Assert.Equal(1, sold[0].GetProperty("sold").GetInt32());
    }

    [Fact]
    public async Task APersonalProductStillPaysItsSeller()
    {
        var personal = new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = _other, AssetId = Asset(_other), Name = "Mesa", Description = "d", Category = "x", PriceCoins = 200, Status = "active", Version = 1, CreatedAt = _clock.Now, UpdatedAt = _clock.Now };
        _db.MarketplaceProducts.Add(personal); await _db.SaveChangesAsync();
        Assert.Equal(200, (await Buy(_outsider, personal.Id)).Status);
        Assert.Equal(10_190, Coins(_other)); Assert.Equal(0, Chest()); Assert.Empty(_db.GroupLedger);
    }

    [Fact]
    public async Task AGroupProductIsNotChangedAsAPersonalOne()
    {
        var id = Id(await Publish(_admin, price: 100));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", _admin.ToString())], "test")) };
        var update = await EconomyEndpoints.UpdateProductAsync(id, new PublishProductRequest(_db.MarketplaceProducts.Single().AssetId, "Preço Novo", "d", "c", 1, null, null), context, _db, _clock, default);
        Assert.IsType<NotFound>(update); Assert.Equal(100, (await _db.MarketplaceProducts.AsNoTracking().SingleAsync()).PriceCoins);
    }

    [Fact]
    public async Task EveryCoinInTheSystemStaysAccountedFor()
    {
        var id = Id(await Publish(_admin, price: 400));
        await Give(_member, 500); await Buy(_outsider, id); await Buy(_other, id); await Grant(_admin, GroupPermissions.ManageTreasury); await Take(_admin, 100); await Take(_leader, 300, _member);
        await Balanced();
        var wallets = await _db.Wallets.AsNoTracking().SumAsync(w => w.AvailableCoins); var fees = 2 * 20L; // 5% of two 400-coin sales
        Assert.Equal(5 * 10_000 - fees - Chest(), wallets);
    }

    [Fact]
    public void TheEconomyPermissionsExistAndGoToTheRightCargos()
    {
        foreach (var key in new[] { GroupPermissions.ViewTreasury, GroupPermissions.ManageTreasury, GroupPermissions.ManageStore }) Assert.Contains(key, GroupPermissions.Known);
        var roles = GroupRules.StartingRoles.ToDictionary(r => r.Key, r => r.Permissions);
        Assert.Contains("view_treasury", roles["admin"]); Assert.Contains("manage_store", roles["admin"]); Assert.DoesNotContain("manage_treasury", roles["admin"]); Assert.Contains("manage_treasury", roles["owner"]); Assert.DoesNotContain("view_treasury", roles["moderator"]);
    }
}
