using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using LifeVerse.Api.Modules.Groups;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Land;
using LifeVerse.Shared.Groups;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Groups 2.0 end to end: one group lives through everything the five parts add, and ends cleanly.</summary>
public sealed class GroupLifecycleTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly List<(Guid User, string Type)> _pushed = [];
    private readonly GroupNotifier _notify;
    private int _keys;

    public GroupLifecycleTests() => _notify = (user, type, _) => { _pushed.Add((user, type)); return Task.CompletedTask; };

    public void Dispose() => _db.Dispose();

    private Guid User(string name, long coins = 0) { var id = Guid.NewGuid(); _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now, UpdatedAt = _clock.Now }); _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = coins, CreatedAt = _clock.Now, UpdatedAt = _clock.Now }); _db.SaveChanges(); return id; }
    private static Guid Id(GroupResult r) => JsonSerializer.SerializeToElement(r.Data).GetProperty("id").GetGuid();
    private string Key() => $"ciclo-{++_keys:D6}";
    private static void Ok(GroupResult r) => Assert.True(r.Ok, r.Error);

    [Fact]
    public async Task AGroupLivesAndEndsCleanly()
    {
        var (leader, ana, bia, cris, outsider) = (User("lider", 1_000), User("ana", 5_000), User("bia", 5_000), User("cris", 5_000), User("estranho", 5_000));
        var now = _clock.Now;

        // born open, people come in by themselves and by invitation
        var group = Id(await GroupService.CreateAsync(_db, leader, new GroupProfileRequest("Clube do Fim", "Para tudo", null, "public", "open", "#112233", "star", "CDF", 50), now, default));
        Ok(await GroupService.JoinAsync(_db, ana, group, null, now, _notify, default)); Ok(await GroupService.JoinAsync(_db, bia, group, null, now, _notify, default)); Ok(await GroupService.JoinAsync(_db, cris, group, null, now, _notify, default));
        Assert.Contains((await GroupService.MineAsync(_db, ana, now, default)), g => g.Name == "Clube do Fim" && g.Emblem == "star" && g.Members == 4);

        // ana becomes an administrator; bia gets a cargo that builds and sells
        var roles = await _db.GroupRoles.AsNoTracking().Where(r => r.GroupId == group).ToDictionaryAsync(r => r.SystemKey, r => r.Id);
        Ok(await GroupService.SetRoleAsync(_db, leader, group, ana, roles["admin"], now, _notify, default));
        var seller = Id(await GroupService.CreateRoleAsync(_db, leader, group, new RoleRequest("Vendedora", 20, [GroupPermissions.ManageStore, GroupPermissions.BuildOnLand], null), now, default));
        Ok(await GroupService.SetRoleAsync(_db, leader, group, bia, seller, now, _notify, default));

        // talk
        Ok(await GroupService.SendChatAsync(_db, cris, group, "Bom dia, clube!", Key(), now, _notify, default));
        Ok(await GroupService.PostAnnouncementAsync(_db, ana, group, new AnnouncementRequest("Inauguração", "Sábado, na praça.", true), now, _notify, default));

        // land: ana puts her parcel in; bia may build there, cris may not
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = ana, Name = "Praça", Slug = "praca", IsPublic = true, Size = 256 }; _db.Islands.Add(island); await _db.SaveChangesAsync();
        var parcel = (await LandService.ClaimAsync(_db, island, ana, "Praça do Clube", "", new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(1, 0) }, 50, null, null, null, null, null, _clock, default)).Parcel!.Id;
        Ok(await GroupService.DonateLandAsync(_db, ana, group, parcel, now, _notify, default));
        var facts = (await LandService.LoadFactsAsync(_db, island.Id, default)).Single(f => f.Id == parcel);
        Assert.Null(BuildRules.CanBuildAt(facts, bia, false, false)); Assert.Equal("parcel_forbidden", BuildRules.CanBuildAt(facts, cris, false, false)); Assert.Equal("parcel_forbidden", BuildRules.CanBuildAt(facts, outsider, false, false));

        // an event on the group's land, only for members
        var ev = Id(await GroupService.CreateEventAsync(_db, ana, group, new GroupEventRequest("Festa", "", island.Id, now.AddHours(2), now.AddHours(5), 10), now, _notify, default));
        Ok(await GroupService.JoinEventAsync(_db, cris, group, ev, now, default)); Assert.Equal("forbidden", (await GroupService.JoinEventAsync(_db, outsider, group, ev, now, default)).Error);

        // money: cris gives, bia sells to the outsider, the chest fills
        Ok(await GroupService.ContributeAsync(_db, cris, group, 500, "boa sorte", Key(), now, _notify, default));
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = bia, CreatorUserId = bia, Name = "Cadeira", Category = "furniture", CreatedAt = now, UpdatedAt = now }; _db.Assets.Add(asset); await _db.SaveChangesAsync();
        var product = Id(await GroupService.PublishProductAsync(_db, bia, group, new GroupProductRequest(asset.Id, "Cadeira do Clube", "Boa", "moveis", 1_000, null, null), now, default));
        Ok(await GroupService.CreateStoreAsync(_db, bia, group, new GroupStoreRequest(island.Id, product, null, "Banca da Praça"), now, default));
        var buy = await EconomyEndpoints.PurchaseAsync(product, new PurchaseRequest(Key()), new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", outsider.ToString())], "t")) }, _db, _clock, default);
        Assert.IsType<Ok<MarketplaceOrder>>(buy);
        var chest = JsonSerializer.SerializeToElement((await GroupService.TreasuryAsync(_db, leader, group, now, default)).Data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1_450, chest.GetProperty("balance").GetInt64()); // 500 given + 950 sold
        Assert.Equal(5_000, _db.Wallets.AsNoTracking().Single(w => w.UserId == bia).AvailableCoins); // the seller earns nothing personally

        // the group cannot be dissolved yet: land and coins remain
        var blocked = await GroupService.DissolveAsync(_db, leader, group, "Clube do Fim", now, _notify, default);
        Assert.Equal("blocked", blocked.Error); var reasons = (List<string>)blocked.Data!; Assert.Contains("owns_land", reasons); Assert.Contains("treasury_not_empty", reasons);

        // the leader pays the members out and gives the land back to ana
        Ok(await GroupService.WithdrawAsync(_db, leader, group, 1_450, cris, "Rateio final do clube", Key(), now, _notify, default));
        Ok(await GroupService.ReleaseLandAsync(_db, leader, group, parcel, ana, now, _notify, default));
        Assert.Equal(5_950, _db.Wallets.AsNoTracking().Single(w => w.UserId == cris).AvailableCoins); // 5000 - 500 + 1450
        var restored = _db.IslandParcels.AsNoTracking().Single(p => p.Id == parcel); Assert.Null(restored.GroupId); Assert.Equal(ana, restored.OwnerUserId);

        // the end
        _pushed.Clear();
        Ok(await GroupService.DissolveAsync(_db, leader, group, "Clube do Fim", now, _notify, default));
        foreach (var member in new[] { ana, bia, cris }) Assert.Contains(_pushed, p => p.User == member && p.Type == "group_dissolved");
        Assert.Empty(await GroupService.MineAsync(_db, ana, now, default)); Assert.DoesNotContain(await GroupService.SearchAsync(_db, outsider, "Clube do Fim", 0, now, default), g => g.Name == "Clube do Fim");
        Assert.Equal("not_found", (await GroupService.ChatAsync(_db, ana, group, null, 10, now, default)).Error); Assert.Equal("not_found", (await GroupService.TreasuryAsync(_db, leader, group, now, default)).Error);
        Assert.Equal("inactive", (await _db.MarketplaceProducts.AsNoTracking().SingleAsync()).Status); Assert.NotNull((await _db.WorldStores.AsNoTracking().SingleAsync()).DeletedAt);
        Assert.Empty(_db.DiscoveryEvents); Assert.Empty(_db.GroupMembers.Where(m => m.GroupId == group));
        // the history is kept, and it tells the story
        var story = (await _db.GroupAuditEntries.AsNoTracking().Where(e => e.GroupId == group).OrderBy(e => e.CreatedAt).Select(e => e.Action).ToListAsync()).ToHashSet();
        foreach (var action in new[] { "group_created", "role_assigned", "announcement_posted", "land_donated", "event_created", "treasury_contribution", "product_published", "store_created", "treasury_withdrawal", "land_released", "group_dissolved" }) Assert.Contains(action, story);
        // and the money still adds up
        Assert.Equal(await _db.LedgerEntries.AsNoTracking().SumAsync(e => e.Debit), await _db.LedgerEntries.AsNoTracking().SumAsync(e => e.Credit));
        Assert.Equal(0, (await _db.GroupTreasuries.AsNoTracking().SingleAsync()).Balance);
    }

    [Fact]
    public async Task TheMarketplaceListingNamesTheGroupThatSells()
    {
        var (leader, outsider) = (User("lider"), User("alguem"));
        var group = Id(await GroupService.CreateAsync(_db, leader, new GroupProfileRequest("Os Vendedores", "", null, null, null, null, null, null, null), _clock.Now, default));
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = leader, CreatorUserId = leader, Name = "Mesa", Category = "furniture", CreatedAt = _clock.Now, UpdatedAt = _clock.Now }; _db.Assets.Add(asset); await _db.SaveChangesAsync();
        Ok(await GroupService.PublishProductAsync(_db, leader, group, new GroupProductRequest(asset.Id, "Mesa do Grupo", "", "moveis", 50, null, null), _clock.Now, default));
        _db.MarketplaceProducts.Add(new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = outsider, AssetId = asset.Id, Name = "Pessoal", Description = "d", Category = "x", PriceCoins = 5, Status = "active", Version = 1, CreatedAt = _clock.Now, UpdatedAt = _clock.Now }); await _db.SaveChangesAsync();
        var result = await EconomyEndpoints.ProductsAsync(null, null, 1, 20, _db, default);
        var json = JsonSerializer.SerializeToElement(result.GetType().GetProperty("Value")!.GetValue(result), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("Os Vendedores", items.Single(i => i.GetProperty("name").GetString() == "Mesa do Grupo").GetProperty("groupName").GetString());
        Assert.Equal(JsonValueKind.Null, items.Single(i => i.GetProperty("name").GetString() == "Pessoal").GetProperty("groupName").ValueKind);
    }
}
