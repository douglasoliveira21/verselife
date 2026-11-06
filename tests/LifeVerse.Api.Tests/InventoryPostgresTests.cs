using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.Api.Tests;

/// <summary>The inventory's folders, bin and list on a real PostgreSQL: the queries translate, and many requests at once never leave the tree broken.</summary>
public sealed class InventoryPostgresTests(ITestOutputHelper output)
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    private sealed class Scratch : IAsyncDisposable
    {
        public string Name = "lifeverse_i_" + Guid.NewGuid().ToString("N")[..12]; public DbContextOptions<LifeVerseDbContext> Options = null!;
        public static async Task<Scratch> CreateAsync()
        {
            var s = new Scratch();
            await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{s.Name}\"", connection); await command.ExecuteNonQueryAsync(); }
            s.Options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(Admin) { Database = s.Name, Pooling = true, MaxPoolSize = 50 }.ConnectionString).Options;
            await using var db = new LifeVerseDbContext(s.Options); await db.Database.MigrateAsync(); return s;
        }
        public async ValueTask DisposeAsync() { NpgsqlConnection.ClearAllPools(); await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync(); await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync(); }
    }

    private static async Task<Guid> AddUser(LifeVerseDbContext db, string name)
    {
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@t.local", NormalizedEmail = name.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); return id;
    }

    private static async Task<List<InventoryItem>> AddItems(LifeVerseDbContext db, Guid owner, int count, string prefix = "PeÃ§a")
    {
        var now = DateTimeOffset.UtcNow; var items = new List<InventoryItem>();
        for (var i = 0; i < count; i++)
        {
            var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = $"{prefix} {i}", Category = i % 3 == 0 ? "models" : i % 3 == 1 ? "textures" : "sounds", Description = i % 2 == 0 ? "de madeira" : "", CreatedAt = now.AddSeconds(i), UpdatedAt = now };
            db.Assets.Add(asset); db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k" + asset.Id, MimeType = "model/gltf-binary", ByteSize = 100 * (i + 1), CreatedAt = now });
            var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner, AssetId = asset.Id, DisplayName = asset.Name, CreatedAt = now.AddSeconds(i), UpdatedAt = now }; db.InventoryItems.Add(item); items.Add(item);
        }
        await db.SaveChangesAsync(); return items;
    }

    [PostgresFact]
    public async Task EveryFilterAndOrderOfTheListRunsOnPostgresAndGivesTheRightAnswer()
    {
        await using var scratch = await Scratch.CreateAsync(); Guid me, other; Guid folderId; Guid sub;
        await using (var db = new LifeVerseDbContext(scratch.Options))
        {
            me = await AddUser(db, "eu"); other = await AddUser(db, "outro"); var items = await AddItems(db, me, 30); await AddItems(db, other, 5, "Alheio");
            folderId = ((InventoryFolder)(await InventoryOrganizer.CreateFolderAsync(db, me, "Casa", null, DateTimeOffset.UtcNow, default)).Data!).Id;
            sub = ((InventoryFolder)(await InventoryOrganizer.CreateFolderAsync(db, me, "Sala", folderId, DateTimeOffset.UtcNow, default)).Data!).Id;
            Assert.True((await InventoryOrganizer.MoveAsync(db, me, items.Take(5).Select(i => i.Id).ToArray(), null, folderId, DateTimeOffset.UtcNow, default)).Ok);
            Assert.True((await InventoryOrganizer.MoveAsync(db, me, items.Skip(5).Take(5).Select(i => i.Id).ToArray(), null, sub, DateTimeOffset.UtcNow, default)).Ok);
            items[29].IsFavorite = true; items[28].DeletedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
        }
        async Task<JsonElement> Ask(InventoryQuery q)
        {
            await using var db = new LifeVerseDbContext(scratch.Options);
            var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, me.ToString())], "test")) };
            var result = await InventoryEndpoints.ListAsync(q, context, TimeProvider.System, db, default);
            return JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        Assert.Equal(29, (await Ask(new InventoryQuery())).GetProperty("total").GetInt32()); // 30 less the one in the bin; nothing of the other person's
        Assert.Equal(10, (await Ask(new InventoryQuery(FolderId: folderId, IncludeSubfolders: true))).GetProperty("total").GetInt32()); Assert.Equal(5, (await Ask(new InventoryQuery(FolderId: folderId))).GetProperty("total").GetInt32());
        Assert.Equal(19, (await Ask(new InventoryQuery(Unfiled: true))).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Ask(new InventoryQuery(View: "trash"))).GetProperty("total").GetInt32()); Assert.Equal(1, (await Ask(new InventoryQuery(View: "favorites"))).GetProperty("total").GetInt32()); Assert.Equal(29, (await Ask(new InventoryQuery(View: "recent"))).GetProperty("total").GetInt32());
        Assert.Equal(14, (await Ask(new InventoryQuery(Search: "MADEIRA"))).GetProperty("total").GetInt32()); // case-insensitive on the description, as the database does it
        Assert.Equal(1, (await Ask(new InventoryQuery(Search: "peÃ§a 12 madeira"))).GetProperty("total").GetInt32()); Assert.Equal(0, (await Ask(new InventoryQuery(Search: "peÃ§a 12 inexistente"))).GetProperty("total").GetInt32());
        Assert.Equal(10, (await Ask(new InventoryQuery(Types: "models"))).GetProperty("total").GetInt32()); Assert.Equal(19, (await Ask(new InventoryQuery(Types: "textures,sounds"))).GetProperty("total").GetInt32());
        Assert.Equal(29, (await Ask(new InventoryQuery(Rights: "copy,modify,transfer,resell"))).GetProperty("total").GetInt32()); Assert.Equal(10, (await Ask(new InventoryQuery(MinSize: 2000))).GetProperty("total").GetInt32());
        foreach (var sort in new[] { "name", "date", "updated", "size", "type", "recent", "" })
            foreach (var dir in new[] { "asc", "desc", "" })
            {
                var seen = new HashSet<string>(); for (var page = 1; page <= 3; page++) foreach (var e in (await Ask(new InventoryQuery(Sort: sort, Dir: dir, Page: page, PageSize: 10))).GetProperty("items").EnumerateArray()) Assert.True(seen.Add(e.GetProperty("itemId").GetString()!), $"repeated item sorting by '{sort}' {dir}");
                Assert.Equal(29, seen.Count);
            }
        var bySize = (await Ask(new InventoryQuery(Sort: "size", Dir: "desc"))).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("size").GetInt64()).ToList(); Assert.Equal(bySize.OrderByDescending(x => x), bySize);
        var byName = (await Ask(new InventoryQuery(Sort: "name"))).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList(); Assert.Equal(byName.OrderBy(x => x.ToLowerInvariant(), StringComparer.Ordinal), byName);
        await using var check = new LifeVerseDbContext(scratch.Options); Assert.Equal(sub, (await check.InventoryFolders.AsNoTracking().SingleAsync(f => f.Name == "Sala")).Id);
    }

    [PostgresFact]
    public async Task ManyRequestsAtOnceNeverMakeALoopTooDeepATreeOrAnItemInAnotherPersonsFolder()
    {
        await using var scratch = await Scratch.CreateAsync(); Guid me, other; var folders = new List<Guid>(); List<Guid> items; Guid theirFolder;
        await using (var db = new LifeVerseDbContext(scratch.Options))
        {
            me = await AddUser(db, "eu"); other = await AddUser(db, "outro"); items = (await AddItems(db, me, 40)).Select(i => i.Id).ToList();
            for (var i = 0; i < 10; i++) folders.Add(((InventoryFolder)(await InventoryOrganizer.CreateFolderAsync(db, me, $"P{i}", i < 4 ? null : folders[i - 4], DateTimeOffset.UtcNow, default)).Data!).Id);
            theirFolder = ((InventoryFolder)(await InventoryOrganizer.CreateFolderAsync(db, other, "Dele", null, DateTimeOffset.UtcNow, default)).Data!).Id;
        }
        var unexpected = new ConcurrentBag<string>(); var done = new ConcurrentDictionary<string, int>();
        async Task Worker(int seed)
        {
            var rng = new Random(seed);
            for (var step = 0; step < 120; step++)
            {
                await using var db = new LifeVerseDbContext(scratch.Options); var op = rng.Next(8); var now = DateTimeOffset.UtcNow; InventoryOutcome? outcome = null;
                try
                {
                    switch (op)
                    {
                        case 0: outcome = await InventoryOrganizer.MoveAsync(db, me, null, [folders[rng.Next(folders.Count)]], rng.Next(5) == 0 ? null : folders[rng.Next(folders.Count)], now, default); break;
                        case 1: outcome = await InventoryOrganizer.MoveAsync(db, me, [items[rng.Next(items.Count)], items[rng.Next(items.Count)]], null, rng.Next(4) == 0 ? null : folders[rng.Next(folders.Count)], now, default); break;
                        case 2: outcome = await InventoryOrganizer.UpdateFolderAsync(db, me, folders[rng.Next(folders.Count)], null, folders[rng.Next(folders.Count)], false, now, default); break;
                        case 3: outcome = await InventoryOrganizer.DeleteFolderAsync(db, me, folders[rng.Next(folders.Count)], now, default); break;
                        case 4: outcome = await InventoryOrganizer.RestoreFolderAsync(db, me, folders[rng.Next(folders.Count)], now, default); break;
                        case 5: outcome = await InventoryOrganizer.CreateFolderAsync(db, me, "N" + rng.Next(6), rng.Next(3) == 0 ? null : folders[rng.Next(folders.Count)], now, default); break;
                        case 6: outcome = await InventoryOrganizer.MoveAsync(db, me, [items[rng.Next(items.Count)]], null, theirFolder, now, default); if (outcome.Ok) unexpected.Add("moved into somebody else's folder"); break; // must always be refused
                        case 7: outcome = await InventoryOrganizer.EmptyTrashAsync(db, me, now, now.AddDays(-30), default) is var _ ? InventoryOutcome.Done() : null; break; // nothing is old enough
                    }
                    done.AddOrUpdate($"op{op}:{(outcome?.Ok == true ? "ok" : outcome?.Error ?? "?")}", 1, (_, c) => c + 1);
                }
                catch (Exception exception) { unexpected.Add($"op {op}: {exception.GetType().Name}: {exception.Message}"); }
            }
        }
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => Worker(300 + i))));
        Assert.Empty(unexpected);
        await using var verify = new LifeVerseDbContext(scratch.Options);
        var live = await verify.InventoryFolders.AsNoTracking().Where(f => f.DeletedAt == null).ToListAsync(); var parents = live.ToDictionary(f => f.Id, f => f.ParentId);
        foreach (var folder in live)
        {
            var depth = FolderRules.DepthOf(parents, folder.Id); Assert.InRange(depth, 1, FolderRules.MaxDepth); // a loop would stop the count short of the top
            var hops = 0; var at = (Guid?)folder.Id; while (at is { } step && parents.TryGetValue(step, out var up) && hops++ <= live.Count + 1) at = up; Assert.True(at is null || !parents.ContainsKey(at.Value), $"folder {folder.Name} is inside a loop");
            Assert.True(folder.ParentId is null || parents.ContainsKey(folder.ParentId.Value) || live.All(f => f.Id != folder.ParentId), "a live folder under a folder that is not live");
        }
        var liveIds = live.Select(f => f.Id).ToHashSet(); var aliveItems = await verify.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == me && i.DeletedAt == null).ToListAsync();
        Assert.All(aliveItems, i => Assert.True(i.FolderId is null || liveIds.Contains(i.FolderId.Value), "a live item in a folder that is gone")); Assert.Equal(40, await verify.InventoryItems.CountAsync(i => i.OwnerUserId == me));
        Assert.DoesNotContain(await verify.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == me).ToListAsync(), i => i.FolderId == theirFolder);
        output.WriteLine(string.Join(", ", done.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")));
    }
}


/// <summary>The history of deliveries and the duplication check, on a real PostgreSQL.</summary>
public sealed class ItemDeliveryPostgresTests
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    [PostgresFact]
    public async Task TheHistoryQueriesAndTheIntegrityCheckRunOnPostgres()
    {
        var name = "lifeverse_d_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString).Options;
            await using (var migrate = new LifeVerseDbContext(options)) await migrate.Database.MigrateAsync();
            await using var db = new LifeVerseDbContext(options); var now = DateTimeOffset.UtcNow; var users = new List<User>();
            foreach (var n in new[] { "ana", "bia", "cris" }) { var u = new User { Id = Guid.NewGuid(), Username = n, NormalizedUsername = n.ToUpperInvariant(), Email = n + "@t.local", NormalizedEmail = n.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now }; db.Users.Add(u); users.Add(u); }
            await db.SaveChangesAsync(); var offers = new List<ItemOffer>();
            for (var i = 0; i < 9; i++)
            {
                var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = users[0].Id, CreatorUserId = users[0].Id, Name = $"Peça {i}", Category = "mesh", CreatedAt = now, UpdatedAt = now }; db.Assets.Add(asset);
                var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = users[0].Id, AssetId = asset.Id, DisplayName = i % 2 == 0 ? $"Sofá {i}" : $"Mesa {i}", CreatedAt = now, UpdatedAt = now }; db.InventoryItems.Add(item); await db.SaveChangesAsync();
                var result = await ItemTransfers.OfferAsync(db, users[0].Id, item.Id, users[1 + i % 2].Id, i % 3 == 0 ? "transfer" : "gift", "oi", false, $"chave-{i:D6}", now.AddMinutes(i), default); Assert.True(result.Ok, result.Error); offers.Add(result.Offer!);
            }
            Assert.True((await ItemTransfers.AcceptAsync(db, users[2].Id, offers[1].Id, now.AddHours(1), default)).Ok); Assert.True((await ItemTransfers.RejectAsync(db, users[1].Id, offers[2].Id, now.AddHours(1), default)).Ok);
            async Task<(int Total, List<TransferLine> Lines)> Ask(Guid user, TransferQuery q) => await ItemTransferHistory.QueryAsync(db, user, q, default);
            Assert.Equal(9, (await Ask(users[0].Id, new())).Total); Assert.Equal(5, (await Ask(users[1].Id, new())).Total); Assert.Equal(4, (await Ask(users[2].Id, new())).Total);
            Assert.Equal(5, (await Ask(users[0].Id, new(Search: "sofá"))).Total); Assert.Equal(4, (await Ask(users[0].Id, new(Search: "MESA"))).Total); Assert.Equal(5, (await Ask(users[0].Id, new(Search: "bia"))).Total); Assert.Equal(0, (await Ask(users[0].Id, new(Search: "ana"))).Total);
            Assert.Equal(1, (await Ask(users[0].Id, new(Status: "accepted"))).Total); Assert.Equal(1, (await Ask(users[0].Id, new(Status: "rejected"))).Total); Assert.Equal(7, (await Ask(users[0].Id, new(Status: "pending"))).Total);
            Assert.Equal(3, (await Ask(users[0].Id, new(From: now.AddMinutes(2), To: now.AddMinutes(4)))).Total); Assert.Equal(5, (await Ask(users[1].Id, new(Direction: "in"))).Total); Assert.Equal(0, (await Ask(users[1].Id, new(Direction: "out"))).Total);
            var seen = new HashSet<Guid>(); for (var page = 1; page <= 3; page++) foreach (var line in (await Ask(users[0].Id, new(Page: page, PageSize: 4))).Lines) Assert.True(seen.Add(line.Id)); Assert.Equal(9, seen.Count);
            var report = await InventoryIntegrity.CheckAsync(db, null, default); Assert.True(report.Clean, string.Join("; ", report.Findings.Select(f => f.Rule + " " + f.Detail)));
            var broken = await db.InventoryItems.FirstAsync(i => i.OwnerUserId == users[0].Id && i.Status == "offered"); broken.Status = "active"; await db.SaveChangesAsync(); // an offer whose item is no longer locked for it
            Assert.Contains((await InventoryIntegrity.CheckAsync(db, null, default)).Findings, f => f.Rule == "orphan_offer");
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync(); await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync(); }
    }
}

/// <summary>Recovering deleted objects and unzipping packs, with many requests at once on a real PostgreSQL.</summary>
public sealed class InventoryPacksPostgresTests
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    private sealed class MemoryStore : LifeVerse.Api.Modules.Assets.IObjectStore
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Files = new();
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    [PostgresFact]
    public async Task TheSameDeletedObjectsRecoveredByManyRequestsAtOnceComeBackOnlyOnce()
    {
        var name = "lifeverse_r_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(Admin) { Database = name, Pooling = true, MaxPoolSize = 40 }.ConnectionString).Options;
            await using (var migrate = new LifeVerseDbContext(options)) await migrate.Database.MigrateAsync();
            var store = new MemoryStore(); var storage = new LifeVerse.Api.Modules.Assets.AssetStorageService(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<LifeVerse.Api.Modules.Assets.AssetStorageService>.Instance); var now = DateTimeOffset.UtcNow; var user = Guid.NewGuid(); var ids = new List<Guid>();
            await using (var db = new LifeVerseDbContext(options))
            {
                db.Users.Add(new User { Id = user, Username = "eu", NormalizedUsername = "EU", Email = "e@t.local", NormalizedEmail = "E@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now });
                var island = new Island { Id = Guid.NewGuid(), OwnerUserId = user, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 }; db.Islands.Add(island);
                for (var i = 0; i < 6; i++) { var o = new BuildObject { Id = Guid.NewGuid(), IslandId = island.Id, CreatorUserId = user, OwnerUserId = user, Name = $"Peça {i}", PrimitiveType = "cube", PositionX = i * 2, DeletedAt = now.AddMinutes(-5), Revision = 2, CreatedAt = now.AddHours(-1), UpdatedAt = now }; db.BuildObjects.Add(o); ids.Add(o.Id); db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = o.Id, ActorUserId = user, Operation = "delete", PayloadJson = "{}", Revision = 2, CreatedAt = now.AddMinutes(-5) }); }
                await db.SaveChangesAsync();
            }
            var outcomes = new System.Collections.Concurrent.ConcurrentBag<string>();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () => { await using var db = new LifeVerseDbContext(options); try { var r = await InventoryRecovery.RecoverAsync(db, storage, user, [.. ids], DateTimeOffset.UtcNow, default); outcomes.Add(r.Ok ? "ok" : r.Error ?? "?"); } catch (Exception exception) { outcomes.Add("EXCEPTION " + exception.GetType().Name + " " + exception.Message); } })));
            Assert.Equal(1, outcomes.Count(o => o == "ok")); Assert.All(outcomes.Where(o => o != "ok"), o => Assert.True(o is "already_recovered" or "not_found", o));
            await using var check = new LifeVerseDbContext(options); Assert.Equal(1, await check.InventoryItems.CountAsync(i => i.Origin == "recovered")); Assert.Equal(6, await check.BuildOperations.CountAsync(o => o.Operation == "recover")); Assert.Empty(await InventoryRecovery.ListAsync(check, user, DateTimeOffset.UtcNow, default));
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync(); await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync(); }
    }
}

/// <summary>Versions of an asset published at the same moment, on a real PostgreSQL.</summary>
public sealed class AssetVersionsPostgresTests
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private sealed class MemoryStore : LifeVerse.Api.Modules.Assets.IObjectStore
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Files = new();
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    [PostgresFact]
    public async Task ManyVersionsPublishedAtOnceGetDistinctNumbersAndTheCurrentOneIsTheLatest()
    {
        var name = "lifeverse_v_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(Admin) { Database = name, Pooling = true, MaxPoolSize = 40 }.ConnectionString).Options;
            await using (var migrate = new LifeVerseDbContext(options)) await migrate.Database.MigrateAsync();
            var storage = new LifeVerse.Api.Modules.Assets.AssetStorageService(new MemoryStore(), Microsoft.Extensions.Logging.Abstractions.NullLogger<LifeVerse.Api.Modules.Assets.AssetStorageService>.Instance); var now = DateTimeOffset.UtcNow; var maker = Guid.NewGuid(); var assetId = Guid.NewGuid();
            await using (var db = new LifeVerseDbContext(options))
            {
                db.Users.Add(new User { Id = maker, Username = "criador", NormalizedUsername = "CRIADOR", Email = "c@t.local", NormalizedEmail = "C@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now });
                db.Assets.Add(new Asset { Id = assetId, OwnerUserId = maker, CreatorUserId = maker, Name = "Tinta", Category = "textures", CreatedAt = now, UpdatedAt = now });
                db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = assetId, Version = 1, ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Png)).ToLowerInvariant(), StorageKey = "k", MimeType = "image/png", ByteSize = Png.Length, CreatedAt = now }); await db.SaveChangesAsync();
            }
            var outcomes = new System.Collections.Concurrent.ConcurrentBag<string>();
            await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
            {
                await using var db = new LifeVerseDbContext(options);
                try { var r = await LifeVerse.Api.Modules.Assets.AssetVersioning.PublishAsync(db, storage, maker, assetId, "t.png", [.. Png, .. Enumerable.Repeat((byte)i, i + 1)], $"versão {i}", DateTimeOffset.UtcNow, default); outcomes.Add(r.Ok ? "ok" : r.Error ?? "?"); }
                catch (Exception exception) { outcomes.Add("EXCEPTION " + exception.GetType().Name + " " + exception.Message); }
            })));
            Assert.All(outcomes, o => Assert.Equal("ok", o)); // taking turns, every one of them gets its own number
            await using var check = new LifeVerseDbContext(options); var versions = await check.AssetVersions.AsNoTracking().Where(v => v.AssetId == assetId).Select(v => v.Version).OrderBy(v => v).ToListAsync(); Assert.Equal(Enumerable.Range(1, 11), versions);
            Assert.Equal(11, (await check.Assets.AsNoTracking().SingleAsync(a => a.Id == assetId)).CurrentVersion); // the current version is the latest published, never an older one that saved late
            var racers = new System.Collections.Concurrent.ConcurrentBag<string>();
            await Task.WhenAll(new[] { 3, 7, 5, 9, 2, 8 }.Select(v => Task.Run(async () => { await using var db = new LifeVerseDbContext(options); var r = await LifeVerse.Api.Modules.Assets.AssetVersioning.ActivateAsync(db, maker, assetId, v, DateTimeOffset.UtcNow, default); racers.Add(r.Ok ? "ok" : r.Error ?? "?"); })));
            Assert.All(racers, o => Assert.Equal("ok", o)); Assert.Contains((await check.Assets.AsNoTracking().SingleAsync(a => a.Id == assetId)).CurrentVersion, new[] { 3, 7, 5, 9, 2, 8 }); Assert.Equal(11, await check.AssetVersions.CountAsync(v => v.AssetId == assetId));
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync(); await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync(); }
    }
}
