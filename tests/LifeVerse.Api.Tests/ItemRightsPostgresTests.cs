using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The item rights on a real PostgreSQL: the migration (including what it does to items that already exist) and the protection against two changes at once.</summary>
public sealed class ItemRightsPostgresTests
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    private static async Task<(string Name, string Connection)> CreateAsync()
    {
        var name = "lifeverse_r_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        var cs = new NpgsqlConnectionStringBuilder(Admin) { Database = name, Pooling = true, MaxPoolSize = 20 }.ConnectionString;
        await using var db = new LifeVerseDbContext(new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options);
        await db.Database.MigrateAsync();
        return (name, cs);
    }

    private static async Task DropAsync(string name)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecAsync(string cs, string sql) { await using var c = new NpgsqlConnection(cs); await c.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }
    private static async Task<long> ScalarAsync(string cs, string sql) { await using var c = new NpgsqlConnection(cs); await c.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, c); return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }

    [PostgresFact]
    public async Task AnItemInsertedTheOldWayGetsFullRightsFromTheDatabaseDefaults()
    {
        var (name2, cs2) = await CreateAsync();
        try
        {
            await using var db = new LifeVerseDbContext(new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs2).Options); var now = DateTimeOffset.UtcNow;
            var owner = new User { Id = Guid.NewGuid(), Username = "dono", NormalizedUsername = "DONO", Email = "d@t.local", NormalizedEmail = "D@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now }; db.Users.Add(owner);
            var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner.Id, CreatorUserId = owner.Id, Name = "Velho", Category = "mesh", CreatedAt = now, UpdatedAt = now }; db.Assets.Add(asset); await db.SaveChangesAsync();
            await ExecAsync(cs2, $"INSERT INTO inventory.items (id, owner_user_id, asset_id, display_name, is_favorite, created_at, updated_at) VALUES ('{Guid.NewGuid()}', '{owner.Id}', '{asset.Id}', 'sem direitos ainda', false, now(), now())");
            var old = await db.InventoryItems.AsNoTracking().SingleAsync(); Assert.Equal((15, 0, 15, "active", 0L), (old.OwnerRights, old.GroupRights, old.NextOwnerRights, old.Status, old.Revision)); // the database fills what old code did not know about
        }
        finally { await DropAsync(name2); }
    }

    [PostgresFact]
    public async Task TwoChangesToTheSameItemAtTheSameMomentCannotBothWin()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; Guid owner, item;
            await using (var db = new LifeVerseDbContext(options))
            {
                var u = new User { Id = Guid.NewGuid(), Username = "dono", NormalizedUsername = "DONO", Email = "d@t.local", NormalizedEmail = "D@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now }; db.Users.Add(u);
                var a = new Asset { Id = Guid.NewGuid(), OwnerUserId = u.Id, CreatorUserId = u.Id, Name = "Peça", Category = "mesh", CreatedAt = now, UpdatedAt = now }; db.Assets.Add(a);
                var i = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = u.Id, AssetId = a.Id, DisplayName = "Peça", CreatedAt = now, UpdatedAt = now }; db.InventoryItems.Add(i); await db.SaveChangesAsync(); owner = u.Id; item = i.Id;
            }
            // 20 requests, each with its own context, each lowering the rights in a different way at the same moment
            var results = new System.Collections.Concurrent.ConcurrentBag<(bool Ok, string? Error)>();
            await Parallel.ForEachAsync(Enumerable.Range(0, 20), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (n, ct) =>
            {
                await using var own = new LifeVerseDbContext(options); var mask = n % 2 == 0 ? 7 : 3;
                results.Add(await ItemRightsService.SetPermissionsAsync(own, owner, item, new ItemPermissions(mask, 0, mask), 0, now, ct)); // every one of them read revision 0
            });
            Assert.Single(results, r => r.Ok); Assert.Equal(19, results.Count(r => r.Error == "stale"));
            await using var check = new LifeVerseDbContext(options); var stored = await check.InventoryItems.AsNoTracking().SingleAsync();
            Assert.Equal(1L, stored.Revision); Assert.Equal(1, await check.ItemAudit.CountAsync(a => a.Outcome == "ok")); Assert.True(stored.OwnerRights is 7 or 3);
        }
        finally { await DropAsync(name); }
    }

    private static async Task<(Guid Giver, Guid Receiver, Guid Item, Guid Asset)> SeedAsync(DbContextOptions<LifeVerseDbContext> options, DateTimeOffset now, int rights = 15, int next = 15)
    {
        await using var db = new LifeVerseDbContext(options);
        User Make(string u) => new() { Id = Guid.NewGuid(), Username = u, NormalizedUsername = u.ToUpperInvariant(), Email = u + "@t.local", NormalizedEmail = u.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now };
        var giver = Make("dou" + Guid.NewGuid().ToString("N")[..6]); var receiver = Make("rec" + Guid.NewGuid().ToString("N")[..6]); db.Users.AddRange(giver, receiver);
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = giver.Id, CreatorUserId = giver.Id, Name = "Peça", Category = "mesh", CreatedAt = now, UpdatedAt = now }; db.Assets.Add(asset);
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = giver.Id, AssetId = asset.Id, DisplayName = "Peça", OwnerRights = rights, NextOwnerRights = next, CreatedAt = now, UpdatedAt = now }; db.InventoryItems.Add(item); await db.SaveChangesAsync();
        return (giver.Id, receiver.Id, item.Id, asset.Id);
    }

    [PostgresFact]
    public async Task TwentyAcceptsOfTheSameTransferMoveTheItemOnceAndNeverDuplicateIt()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; var (giver, receiver, item, asset) = await SeedAsync(options, now);
            await using (var db = new LifeVerseDbContext(options)) Assert.True((await ItemTransfers.OfferAsync(db, giver, item, receiver, "transfer", null, false, "chave-unica-1", now, default)).Ok);
            Guid offer; await using (var db = new LifeVerseDbContext(options)) offer = (await db.ItemOffers.SingleAsync()).Id;
            var results = new System.Collections.Concurrent.ConcurrentBag<OfferResult>();
            await Parallel.ForEachAsync(Enumerable.Range(0, 20), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (n, ct) =>
            {
                await using var own = new LifeVerseDbContext(options); results.Add(n % 4 == 0 ? await ItemTransfers.CancelAsync(own, giver, offer, now, ct) : await ItemTransfers.AcceptAsync(own, receiver, offer, now, ct));
            });
            await using var check = new LifeVerseDbContext(options); var held = await check.InventoryItems.AsNoTracking().Where(i => i.AssetId == asset && i.DeletedAt == null).ToListAsync(); var state = (await check.ItemOffers.AsNoTracking().SingleAsync()).Status;
            var single = Assert.Single(held); Assert.Equal("active", single.Status);
            Assert.True((state == "accepted" && single.OwnerUserId == receiver) || (state == "cancelled" && single.OwnerUserId == giver), $"offer {state}, owner {single.OwnerUserId}");
            Assert.Equal(state == "accepted" ? 1 : 0, await check.ItemOwnerRecords.CountAsync(r => r.ItemId == item && r.Reason == "transfer")); // one move, one line of history
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task TwentyAcceptsOfACopyOfferMakeExactlyOneCopy()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; var (giver, receiver, item, asset) = await SeedAsync(options, now);
            Guid offer; await using (var db = new LifeVerseDbContext(options)) { Assert.True((await ItemTransfers.OfferAsync(db, giver, item, receiver, "gift", null, true, "chave-unica-2", now, default)).Ok); offer = (await db.ItemOffers.SingleAsync()).Id; }
            await Parallel.ForEachAsync(Enumerable.Range(0, 20), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (n, ct) => { await using var own = new LifeVerseDbContext(options); await ItemTransfers.AcceptAsync(own, receiver, offer, now, ct); });
            await using var check = new LifeVerseDbContext(options); Assert.Equal(2, await check.InventoryItems.CountAsync(i => i.AssetId == asset)); Assert.Equal(1, await check.InventoryItems.CountAsync(i => i.AssetId == asset && i.OwnerUserId == receiver)); Assert.Equal(giver, (await check.InventoryItems.SingleAsync(i => i.Id == item)).OwnerUserId);
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task TwentyOffersOfTheSameItemAtOnceLockItForOneOfThem()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; var (giver, receiver, item, asset) = await SeedAsync(options, now); var results = new System.Collections.Concurrent.ConcurrentBag<OfferResult>();
            await Parallel.ForEachAsync(Enumerable.Range(0, 20), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (n, ct) => { await using var own = new LifeVerseDbContext(options); results.Add(await ItemTransfers.OfferAsync(own, giver, item, receiver, "gift", null, false, "chave-corrida-" + n, now, ct)); });
            await using var check = new LifeVerseDbContext(options); Assert.Equal(1, await check.ItemOffers.CountAsync(o => o.Status == "pending")); Assert.Equal("offered", (await check.InventoryItems.SingleAsync(i => i.Id == item)).Status);
            Assert.Equal(1, results.Count(r => r.Ok)); Assert.Equal(19, results.Count(r => r.Error == "item_busy"));
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task ACopyOfferWithTwoPeopleAcceptingNeverMakesMoreCopiesThanOffers()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; var (giver, receiver, item, asset) = await SeedAsync(options, now);
            await using (var db = new LifeVerseDbContext(options)) { for (var i = 0; i < 3; i++) Assert.True((await ItemTransfers.OfferAsync(db, giver, item, receiver, "gift", null, true, "chave-copias-" + i, now, default)).Ok); } // copyable items may have several offers
            List<Guid> offers; await using (var db = new LifeVerseDbContext(options)) offers = await db.ItemOffers.Select(o => o.Id).ToListAsync();
            await Parallel.ForEachAsync(offers.SelectMany(o => Enumerable.Repeat(o, 6)), new ParallelOptions { MaxDegreeOfParallelism = 18 }, async (offer, ct) => { await using var own = new LifeVerseDbContext(options); await ItemTransfers.AcceptAsync(own, receiver, offer, now, ct); });
            await using var check = new LifeVerseDbContext(options); Assert.Equal(3, await check.InventoryItems.CountAsync(i => i.OwnerUserId == receiver)); Assert.Equal(1, await check.InventoryItems.CountAsync(i => i.OwnerUserId == giver));
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task ARightsSetToNothingIsStoredAsNothingNotAsTheDatabaseDefault()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; var (giver, _, _, asset) = await SeedAsync(options, now, rights: 0, next: 0);
            await using var db = new LifeVerseDbContext(options); var stored = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.AssetId == asset);
            Assert.Equal((0, 0, 0), (stored.OwnerRights, stored.GroupRights, stored.NextOwnerRights)); // the column defaults (15) are only for rows written before the rights existed
            var product = new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = giver, AssetId = asset, Name = "Fechado", Description = "", Category = "x", Status = "active", BuyerRights = 0, BuyerNextRights = 0, AllowGift = false, CreatedAt = now, UpdatedAt = now };
            db.MarketplaceProducts.Add(product); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var back = await db.MarketplaceProducts.AsNoTracking().SingleAsync(p => p.Id == product.Id); Assert.Equal((0, 0, false), (back.BuyerRights, back.BuyerNextRights, back.AllowGift));
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task TwentyBuyersOfTheOnlyCopyOfAResaleLeaveExactlyOneOwnerAndOnePayment()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; var (seller, _, item, asset) = await SeedAsync(options, now, rights: ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, next: ItemRights.Modify | ItemRights.Transfer);
            var clock = new FixedTime(now); var buyers = new List<Guid>(); Guid product;
            await using (var db = new LifeVerseDbContext(options))
            {
                db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = seller, AvailableCoins = 0, CreatedAt = now, UpdatedAt = now });
                for (var i = 0; i < 20; i++)
                {
                    var u = new User { Id = Guid.NewGuid(), Username = "comp" + i, NormalizedUsername = "COMP" + i, Email = $"c{i}@t.local", NormalizedEmail = $"C{i}@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now }; db.Users.Add(u); buyers.Add(u.Id);
                    db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = u.Id, AvailableCoins = 1000, CreatedAt = now, UpdatedAt = now });
                }
                await db.SaveChangesAsync();
            }
            await using (var db = new LifeVerseDbContext(options)) { var r = await Modules_Economy_PublishAsync(db, seller, asset, item, clock); Assert.Equal(201, r.Status); product = r.Product!.Id; }
            var statuses = new System.Collections.Concurrent.ConcurrentBag<int>();
            await Parallel.ForEachAsync(buyers, new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (buyer, ct) =>
            {
                await using var own = new LifeVerseDbContext(options);
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", buyer.ToString())], "test")) };
                var result = await LifeVerse.Api.Modules.Economy.EconomyEndpoints.PurchaseAsync(product, new LifeVerse.Api.Modules.Economy.PurchaseRequest("compra-" + buyer.ToString("N")), context, own, clock, ct);
                statuses.Add(result is Microsoft.AspNetCore.Http.IStatusCodeHttpResult { StatusCode: { } code } ? code : 0);
            });
            await using var check = new LifeVerseDbContext(options);
            Assert.Equal(1, await check.MarketplaceOrders.CountAsync()); Assert.Equal(1, statuses.Count(s => s == 200));
            var moved = await check.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item); var order = await check.MarketplaceOrders.AsNoTracking().SingleAsync();
            Assert.Equal((order.BuyerUserId, "active", 1), (moved.OwnerUserId, moved.Status, await check.InventoryItems.CountAsync(i => i.AssetId == asset)));
            var wallets = await check.Wallets.AsNoTracking().ToListAsync(); Assert.Equal(1, wallets.Count(w => w.UserId != seller && w.AvailableCoins < 1000)); Assert.Equal(1000 - order.PriceCoins, wallets.Single(w => w.UserId == order.BuyerUserId).AvailableCoins);
            Assert.Equal(order.PriceCoins - order.PriceCoins * 500 / 10000, wallets.Single(w => w.UserId == seller).AvailableCoins); // paid once
        }
        finally { await DropAsync(name); }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static async Task<(int Status, MarketplaceProduct? Product)> Modules_Economy_PublishAsync(LifeVerseDbContext db, Guid seller, Guid asset, Guid item, TimeProvider clock)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", seller.ToString())], "test")) };
        var request = new LifeVerse.Api.Modules.Economy.PublishProductRequest(asset, "Revenda", "", "x", 600, null, null, null, null, item, null);
        var result = await LifeVerse.Api.Modules.Economy.EconomyEndpoints.PublishAsync(request, context, db, clock, default);
        return (result is Microsoft.AspNetCore.Http.IStatusCodeHttpResult { StatusCode: { } code } ? code : 0, (result as Microsoft.AspNetCore.Http.IValueHttpResult)?.Value as MarketplaceProduct);
    }

    private sealed class MemoryStore : LifeVerse.Api.Modules.Assets.IObjectStore
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Files = new();
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    [PostgresFact]
    public async Task TwentyPlacementsOfAnItemThatCannotBeCopiedPlaceItOnce()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; var store = new MemoryStore();
            var storage = new LifeVerse.Api.Modules.Assets.AssetStorageService(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<LifeVerse.Api.Modules.Assets.AssetStorageService>.Instance); var instance = Guid.NewGuid(); Guid owner, package;
            await using (var db = new LifeVerseDbContext(options))
            {
                var u = new User { Id = Guid.NewGuid(), Username = "dono", NormalizedUsername = "DONO", Email = "d@t.local", NormalizedEmail = "D@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now }; db.Users.Add(u); owner = u.Id;
                var island = new Island { Id = Guid.NewGuid(), OwnerUserId = u.Id, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 }; db.Islands.Add(island); db.ServerInstances.Add(new ServerInstance { Id = instance, IslandId = island.Id, Hostname = "h", Status = "healthy" }); await db.SaveChangesAsync();
                var sources = new[] { 0f, 2f, 4f }.Select(x => new LifeVerse.Api.Modules.Inventory.PackageSourceDto("cube", "Caixa", "", x, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{}", "[]", null)).ToList();
                var item = await LifeVerse.Api.Modules.Inventory.BuildPackageStore.SaveAsync(db, storage, u.Id, LifeVerse.Api.Modules.Inventory.PackageDocument.Create("Casa", sources), now, default, new ItemPermissions(ItemRights.Modify | ItemRights.Transfer, 0, ItemRights.Transfer)); await db.SaveChangesAsync(); package = item.Id;
            }
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();
            await Parallel.ForEachAsync(Enumerable.Range(0, 20), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (n, ct) =>
            {
                await using var own = new LifeVerseDbContext(options);
                var result = await LifeVerse.Api.Modules.Worlds.BuildOperations.ApplyAsync(instance, new LifeVerse.Api.Modules.Platform.ServerBuildOperationRequest(owner, "rez", null, null, 0, null, package, n * 20f, 0, 0), own, storage, new FixedTime(now), ct);
                results.Add((result as Microsoft.AspNetCore.Http.IStatusCodeHttpResult)?.StatusCode ?? 200);
            });
            await using var check = new LifeVerseDbContext(options);
            Assert.Equal(1, results.Count(s => s == 200)); Assert.Equal(3, await check.BuildObjects.CountAsync(o => o.DeletedAt == null)); // one placement: three objects, not sixty
            var used = await check.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == package); Assert.Equal(("consumed", true), (used.Status, used.DeletedAt is not null));
            Assert.All(await check.BuildObjects.AsNoTracking().ToListAsync(), o => Assert.Equal((6, 4), (o.OwnerRights, o.NextOwnerRights)));
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task TheIslandAssetFilesAreFoundOnPostgresByTheirIdInModelsAndMaterials()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow; var store = new MemoryStore();
            var storage = new LifeVerse.Api.Modules.Assets.AssetStorageService(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<LifeVerse.Api.Modules.Assets.AssetStorageService>.Instance);
            await using var db = new LifeVerseDbContext(options);
            var owner = new User { Id = Guid.NewGuid(), Username = "dono", NormalizedUsername = "DONO", Email = "d@t.local", NormalizedEmail = "D@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now }; db.Users.Add(owner);
            var island = new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 }; db.Islands.Add(island);
            Asset MakeAsset(string mime, byte[] bytes) { var a = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner.Id, CreatorUserId = owner.Id, Name = "A", Category = "x", CurrentVersion = 1, CreatedAt = now, UpdatedAt = now }; db.Assets.Add(a); db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = a.Id, Version = 1, ContentHash = a.Id.ToString("N"), StorageKey = "k" + a.Id, MimeType = mime, ByteSize = bytes.Length }); store.Files["k" + a.Id] = bytes; return a; }
            var model = MakeAsset("model/gltf-binary", [1, 2, 3]); var image = MakeAsset("image/png", [4, 5, 6]); var unused = MakeAsset("model/gltf-binary", [7]);
            await db.SaveChangesAsync();
            db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = island.Id, CreatorUserId = owner.Id, OwnerUserId = owner.Id, Name = "Peça", PrimitiveType = "mesh", GeometryJson = $"{{\"assetId\":\"{model.Id}\"}}", CreatedAt = now, UpdatedAt = now });
            db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = island.Id, CreatorUserId = owner.Id, OwnerUserId = owner.Id, Name = "Caixa", PrimitiveType = "cube", MaterialJson = $"{{\"map\":\"{image.Id}\"}}", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            var visitor = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", Guid.NewGuid().ToString())], "test")) };
            Assert.False((await LifeVerse.Api.Modules.Assets.MeshAssets.IslandAssetAsync(island.Id, model.Id, visitor, db, storage, default)) is IStatusCodeHttpResult { StatusCode: 404 }); // a model the island uses
            Assert.False((await LifeVerse.Api.Modules.Assets.MeshAssets.IslandAssetAsync(island.Id, image.Id, visitor, db, storage, default)) is IStatusCodeHttpResult { StatusCode: 404 }); // an image of a material
            Assert.True((await LifeVerse.Api.Modules.Assets.MeshAssets.IslandAssetAsync(island.Id, unused.Id, visitor, db, storage, default)) is IStatusCodeHttpResult { StatusCode: 404 }); // not used here: not served
        }
        finally { await DropAsync(name); }
    }
}
