using System.Collections.Concurrent;
using System.Security.Claims;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.Api.Tests;

/// <summary>
/// Many people doing everything at once, at random, on a real PostgreSQL: giving, accepting, refusing, taking back, selling, buying, copying, placing and returning.
/// Whatever order things happen in, the rules that matter must still hold at the end: nothing exists twice, nothing is lost, no coin is made or destroyed, the
/// history says who holds what, and no request ends in an unexpected error.
/// </summary>
public sealed class ItemRightsChaosTests(ITestOutputHelper output)
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    private sealed class MemoryStore : LifeVerse.Api.Modules.Assets.IObjectStore
    {
        public readonly ConcurrentDictionary<string, byte[]> Files = new();
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private static async Task<(string Name, DbContextOptions<LifeVerseDbContext> Options)> CreateAsync()
    {
        var name = "lifeverse_c_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        var cs = new NpgsqlConnectionStringBuilder(Admin) { Database = name, Pooling = true, MaxPoolSize = 60 }.ConnectionString; var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options;
        await using var db = new LifeVerseDbContext(options); await db.Database.MigrateAsync(); return (name, options);
    }

    private static async Task DropAsync(string name)
    {
        NpgsqlConnection.ClearAllPools(); await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
    }

    private static DefaultHttpContext As(Guid user) => new() { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test")) };
    private static int Status(IResult r) => r is IStatusCodeHttpResult { StatusCode: { } code } ? code : r is ForbidHttpResult ? 403 : 0;

    [PostgresFact]
    public async Task RandomTradingByManyPeopleNeverDuplicatesLosesOrInventsAnything()
    {
        var (name, options) = await CreateAsync();
        try
        {
            var now = DateTimeOffset.UtcNow; const int people = 6, items = 8, startCoins = 10_000; var users = new List<Guid>(); var itemIds = new List<Guid>(); var assets = new List<Guid>(); var maker = Guid.NewGuid();
            await using (var db = new LifeVerseDbContext(options))
            {
                User Make(Guid id, string n) => new() { Id = id, Username = n, NormalizedUsername = n.ToUpperInvariant(), Email = n + "@t.local", NormalizedEmail = n.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now };
                db.Users.Add(Make(maker, "criador"));
                for (var i = 0; i < people; i++) { var u = Make(Guid.NewGuid(), "pessoa" + i); db.Users.Add(u); users.Add(u.Id); db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = u.Id, AvailableCoins = startCoins, CreatedAt = now, UpdatedAt = now }); }
                await db.SaveChangesAsync();
                for (var i = 0; i < items; i++)
                {
                    var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = maker, CreatorUserId = maker, Name = "Peça " + i, Category = "mesh", CreatedAt = now, UpdatedAt = now }; db.Assets.Add(asset); assets.Add(asset.Id);
                    var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = users[i % people], AssetId = asset.Id, DisplayName = "Peça " + i, OwnerRights = ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, NextOwnerRights = ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, CreatedAt = now, UpdatedAt = now };
                    db.InventoryItems.Add(item); itemIds.Add(item.Id);
                }
                await db.SaveChangesAsync();
            }

            var unexpected = new ConcurrentBag<string>(); var done = new ConcurrentDictionary<string, int>();
            async Task Worker(int seed)
            {
                var rng = new Random(seed);
                for (var step = 0; step < 150; step++)
                {
                    await using var db = new LifeVerseDbContext(options); var me = users[rng.Next(people)]; var op = rng.Next(9); var clock = TimeProvider.System; var at = DateTimeOffset.UtcNow;
                    try
                    {
                        var mine = await db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == me && i.DeletedAt == null).Select(i => i.Id).ToListAsync();
                        switch (op)
                        {
                            case 0 when mine.Count > 0: { var to = users[rng.Next(people)]; await ItemTransfers.OfferAsync(db, me, mine[rng.Next(mine.Count)], to, rng.Next(2) == 0 ? "gift" : "transfer", null, false, "chave-" + Guid.NewGuid().ToString("N"), at, default); break; }
                            case 1: { var o = await db.ItemOffers.AsNoTracking().Where(x => x.ToUserId == me && x.Status == "pending").Select(x => x.Id).ToListAsync(); if (o.Count > 0) await ItemTransfers.AcceptAsync(db, me, o[rng.Next(o.Count)], at, default); break; }
                            case 2: { var o = await db.ItemOffers.AsNoTracking().Where(x => x.ToUserId == me && x.Status == "pending").Select(x => x.Id).ToListAsync(); if (o.Count > 0) await ItemTransfers.RejectAsync(db, me, o[rng.Next(o.Count)], at, default); break; }
                            case 3: { var o = await db.ItemOffers.AsNoTracking().Where(x => x.FromUserId == me && x.Status == "pending").Select(x => x.Id).ToListAsync(); if (o.Count > 0) await ItemTransfers.CancelAsync(db, me, o[rng.Next(o.Count)], at, default); break; }
                            case 4 when mine.Count > 0: { var chosen = mine[rng.Next(mine.Count)]; var item = await db.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == chosen); var r = await EconomyEndpoints.PublishAsync(new PublishProductRequest(item.AssetId, "Revenda", "", "x", rng.Next(10, 500), null, null, null, null, item.Id, null), As(me), db, clock, default); Check(r, new[] { 201, 403, 404, 409 }, op); break; }
                            case 5: { var p = await db.MarketplaceProducts.AsNoTracking().Where(x => x.Status == "active").Select(x => x.Id).ToListAsync(); if (p.Count > 0) { var r = await EconomyEndpoints.PurchaseAsync(p[rng.Next(p.Count)], new PurchaseRequest("compra-" + Guid.NewGuid().ToString("N")), As(me), db, clock, default); Check(r, new[] { 200, 400, 403, 404, 409 }, op); } break; }
                            case 6: { var p = await db.MarketplaceProducts.AsNoTracking().Where(x => x.SellerUserId == me && x.Status == "active").Select(x => x.Id).ToListAsync(); if (p.Count > 0) Check(await EconomyEndpoints.DeactivateAsync(p[rng.Next(p.Count)], As(me), db, clock, default), new[] { 204, 404, 409 }, op); break; }
                            case 7 when mine.Count > 0: { var c = await ItemTransfers.CopyAsync(db, me, mine[rng.Next(mine.Count)], at, default); if (c.Ok) unexpected.Add("a copy of an item that cannot be copied was made"); break; }
                            case 8 when mine.Count > 0: { await ItemRightsService.SetPermissionsAsync(db, me, mine[rng.Next(mine.Count)], new ItemPermissions(ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, 0, ItemRights.Transfer), null, at, default); break; }
                        }
                        done.AddOrUpdate("op" + op, 1, (_, n) => n + 1);
                    }
                    catch (Exception exception) { unexpected.Add($"op {op}: {exception.GetType().Name}: {exception.Message} | {exception.InnerException?.GetType().Name}: {exception.InnerException?.Message} | {exception.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("LifeVerse.Api.Modules"))?.Trim()}"); }
                }
                void Check(IResult r, int[] allowed, int op) { if (!allowed.Contains(Status(r))) unexpected.Add($"op {op}: status {Status(r)}"); }
            }
            await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() => Worker(1000 + i))));
            output.WriteLine("event=item_chaos_trading " + string.Join(" ", done.OrderBy(d => d.Key).Select(d => $"{d.Key}={d.Value}")));

            await using var check = new LifeVerseDbContext(options);
            foreach (var line in unexpected.Distinct().Take(5)) output.WriteLine("unexpected: " + line);
            Assert.Empty(unexpected.Take(10)); // no request ended in an error nobody planned for
            foreach (var asset in assets) Assert.Equal(1, await check.InventoryItems.CountAsync(i => i.AssetId == asset && i.DeletedAt == null)); // nothing exists twice, nothing is lost
            var all = await check.InventoryItems.AsNoTracking().ToListAsync(); var offers = await check.ItemOffers.AsNoTracking().ToListAsync(); var products = await check.MarketplaceProducts.AsNoTracking().ToListAsync(); var records = await check.ItemOwnerRecords.AsNoTracking().ToListAsync();
            foreach (var item in all)
            {
                var offered = offers.Any(o => o.ItemId == item.Id && o.Status == "pending" && !o.KeepCopy); var listed = products.Any(p => p.SourceItemId == item.Id && p.Status == "active");
                Assert.Equal(offered, item.Status == "offered"); Assert.Equal(listed, item.Status == "listed"); Assert.True(item.Status is "active" or "offered" or "listed", item.Status); Assert.False(offered && listed);
                var moves = offers.Count(o => o.ItemId == item.Id && o.Status == "accepted") + await check.MarketplaceOrders.CountAsync(o => o.InventoryItemId == item.Id && o.Status == "completed");
                var mine = records.Where(r => r.ItemId == item.Id).ToList(); Assert.Equal(moves, mine.Count); // every change of owner has its line in the history
                if (mine.Count > 0) Assert.Equal(item.OwnerUserId, mine.OrderBy(r => r.At).Last().UserId);
                Assert.True(ItemRights.IsWithin(item.NextOwnerRights, item.OwnerRights)); Assert.False(ItemRights.IsCopyable(item.OwnerRights));
            }
            var wallets = await check.Wallets.AsNoTracking().ToListAsync(); Assert.All(wallets, w => Assert.True(w.AvailableCoins >= 0));
            var orders = await check.MarketplaceOrders.AsNoTracking().Where(o => o.Status == "completed").ToListAsync(); var fees = orders.Sum(o => o.PriceCoins * 500 / 10000);
            Assert.Equal(people * (long)startCoins, wallets.Sum(w => w.AvailableCoins) + fees); // no coin is made or destroyed: what left the wallets is the fees
            Assert.All(products.Where(p => p.Status == "sold"), p => Assert.Equal(1, orders.Count(o => o.ProductId == p.Id)));
            output.WriteLine($"event=item_chaos_trading_result owners_changed={records.Count} orders={orders.Count} offers={offers.Count} accepted={offers.Count(o => o.Status == "accepted")}");
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task RandomPlacingReturningAndPassingOnOfAnUncopyableConstructionKeepsTheSameNumberOfObjects()
    {
        var (name, options) = await CreateAsync();
        try
        {
            var now = DateTimeOffset.UtcNow; const int people = 4, size = 3; var users = new List<Guid>(); var store = new MemoryStore();
            var storage = new LifeVerse.Api.Modules.Assets.AssetStorageService(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<LifeVerse.Api.Modules.Assets.AssetStorageService>.Instance); var instance = Guid.NewGuid(); Guid package;
            await using (var db = new LifeVerseDbContext(options))
            {
                for (var i = 0; i < people; i++) { var u = new User { Id = Guid.NewGuid(), Username = "p" + i, NormalizedUsername = "P" + i, Email = $"p{i}@t.local", NormalizedEmail = $"P{i}@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now }; db.Users.Add(u); users.Add(u.Id); }
                await db.SaveChangesAsync();
                var island = new Island { Id = Guid.NewGuid(), OwnerUserId = users[0], Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 }; db.Islands.Add(island); db.ServerInstances.Add(new ServerInstance { Id = instance, IslandId = island.Id, Hostname = "h", Status = "healthy" });
                foreach (var u in users) db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = u, Permission = "build", PrincipalType = "user", GrantedByUserId = users[0] });
                await db.SaveChangesAsync();
                var sources = Enumerable.Range(0, size).Select(x => new PackageSourceDto("cube", "Caixa", "", x * 2f, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{}", "[]", null)).ToList();
                var item = await BuildPackageStore.SaveAsync(db, storage, users[0], PackageDocument.Create("Casa", sources), now, default, new ItemPermissions(ItemRights.Modify | ItemRights.Transfer, 0, ItemRights.Modify | ItemRights.Transfer)); await db.SaveChangesAsync(); package = item.Id;
            }

            var unexpected = new ConcurrentBag<string>(); var done = new ConcurrentDictionary<string, int>();
            async Task Worker(int seed)
            {
                var rng = new Random(seed);
                for (var step = 0; step < 120; step++)
                {
                    await using var db = new LifeVerseDbContext(options); var me = users[rng.Next(people)]; var op = rng.Next(5); var at = DateTimeOffset.UtcNow;
                    try
                    {
                        switch (op)
                        {
                            case 0: { var held = await db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == me && i.DeletedAt == null && i.Status == "active").Select(i => i.Id).ToListAsync(); if (held.Count > 0) Ok(await BuildOperations.ApplyAsync(instance, new ServerBuildOperationRequest(me, "rez", null, null, 0, null, held[rng.Next(held.Count)], rng.Next(0, 300), 0, rng.Next(0, 300)), db, storage, TimeProvider.System, default), "rez"); break; }
                            case 1: { var objs = await db.BuildObjects.AsNoTracking().Where(o => o.OwnerUserId == me && o.DeletedAt == null).Select(o => o.Id).ToListAsync(); if (objs.Count > 0) Ok(await BuildOperations.ApplyAsync(instance, new ServerBuildOperationRequest(me, "return", null, null, 0, objs.Take(rng.Next(1, objs.Count + 1)).ToArray()), db, storage, TimeProvider.System, default), "return"); break; }
                            case 2: { var held = await db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == me && i.DeletedAt == null).Select(i => i.Id).ToListAsync(); if (held.Count > 0) await ItemTransfers.OfferAsync(db, me, held[rng.Next(held.Count)], users[rng.Next(people)], "transfer", null, false, "chave-" + Guid.NewGuid().ToString("N"), at, default); break; }
                            case 3: { var o = await db.ItemOffers.AsNoTracking().Where(x => x.ToUserId == me && x.Status == "pending").Select(x => x.Id).ToListAsync(); if (o.Count > 0) await ItemTransfers.AcceptAsync(db, me, o[rng.Next(o.Count)], at, default); break; }
                            case 4: { var o = await db.ItemOffers.AsNoTracking().Where(x => x.FromUserId == me && x.Status == "pending").Select(x => x.Id).ToListAsync(); if (o.Count > 0) await ItemTransfers.CancelAsync(db, me, o[rng.Next(o.Count)], at, default); break; }
                        }
                        done.AddOrUpdate("op" + op, 1, (_, n) => n + 1);
                    }
                    catch (Exception exception) { unexpected.Add($"op {op}: {exception.GetType().Name}: {exception.Message} | {exception.InnerException?.GetType().Name}: {exception.InnerException?.Message} | {exception.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("LifeVerse.Api.Modules"))?.Trim()}"); }
                }
                void Ok(IResult r, string what) { var s = Status(r); if (s is not (200 or 400 or 403 or 404 or 409)) unexpected.Add($"{what}: status {s}"); }
            }
            await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => Worker(2000 + i))));
            output.WriteLine("event=item_chaos_building " + string.Join(" ", done.OrderBy(d => d.Key).Select(d => $"{d.Key}={d.Value}")));

            await using var check = new LifeVerseDbContext(options);
            foreach (var line in unexpected.Distinct().Take(5)) output.WriteLine("unexpected: " + line);
            Assert.Empty(unexpected.Take(10));
            // units: the objects standing in the world plus the objects inside every construction still in an inventory (not used up) are always exactly what was made
            var live = await check.BuildObjects.CountAsync(o => o.DeletedAt == null); var inInventories = 0;
            foreach (var item in await check.InventoryItems.AsNoTracking().Where(i => i.Status != "consumed").ToListAsync())
            {
                var (loaded, _) = await BuildPackageEndpoints.LoadAsync(check, storage, item, default); if (loaded is not null) inInventories += loaded.Objects.Length;
            }
            output.WriteLine($"event=item_chaos_building_result world={live} inventories={inInventories}");
            Assert.Equal(size, live + inInventories);
            Assert.All(await check.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).ToListAsync(), o => { Assert.False(ItemRights.IsCopyable(o.OwnerRights)); Assert.True(ItemRights.IsWithin(o.NextOwnerRights, o.OwnerRights)); });
            Assert.All(await check.InventoryItems.AsNoTracking().Where(i => i.Status != "consumed").ToListAsync(), i => Assert.False(ItemRights.IsCopyable(i.OwnerRights)));
        }
        finally { await DropAsync(name); }
    }
}
