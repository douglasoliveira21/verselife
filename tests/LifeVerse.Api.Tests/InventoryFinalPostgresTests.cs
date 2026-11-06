using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.Api.Tests;

/// <summary>The whole inventory under everything at once on a real PostgreSQL, and coming back identical after a restart.</summary>
public sealed class InventoryFinalPostgresTests(ITestOutputHelper output)
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;
    private static DefaultHttpContext As(Guid user) => new() { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", user.ToString())], "test")) };

    private static async Task<(string Name, DbContextOptions<LifeVerseDbContext> Options)> CreateAsync()
    {
        var name = "lifeverse_f_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(Admin) { Database = name, Pooling = true, MaxPoolSize = 60 }.ConnectionString).Options;
        await using var db = new LifeVerseDbContext(options); await db.Database.MigrateAsync(); return (name, options);
    }

    private static async Task DropAsync(string name) { NpgsqlConnection.ClearAllPools(); await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync(); await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync(); }

    private static User NewUser(Guid id, string name, DateTimeOffset now) => new() { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@t.local", NormalizedEmail = name.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now };

    [PostgresFact]
    public async Task FoldersTheBinDeliveriesAndFavouritesAllAtOnceNeverBreakTheTreeDuplicateAnItemOrLeaveOneStuck()
    {
        var (name, options) = await CreateAsync();
        try
        {
            var now = DateTimeOffset.UtcNow; var users = new List<Guid>(); var assets = new List<Guid>(); var folders = new Dictionary<Guid, List<Guid>>();
            await using (var db = new LifeVerseDbContext(options))
            {
                for (var u = 0; u < 4; u++)
                {
                    var user = NewUser(Guid.NewGuid(), "u" + u, now); db.Users.Add(user); users.Add(user.Id);
                    for (var i = 0; i < 10; i++)
                    {
                        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = user.Id, CreatorUserId = user.Id, Name = $"Peça {u}-{i}", Category = "models", CreatedAt = now, UpdatedAt = now }; db.Assets.Add(asset); assets.Add(asset.Id);
                        db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k", MimeType = "model/gltf-binary", ByteSize = 1000 + i, CreatedAt = now });
                        db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = user.Id, AssetId = asset.Id, DisplayName = asset.Name, OwnerRights = i % 3 == 0 ? ItemRights.Modify | ItemRights.Transfer : ItemRights.All, NextOwnerRights = ItemRights.All, CreatedAt = now.AddSeconds(i), UpdatedAt = now });
                    }
                    folders[user.Id] = [];
                }
                await db.SaveChangesAsync();
                foreach (var user in users) for (var f = 0; f < 4; f++) folders[user].Add(((InventoryFolder)(await InventoryOrganizer.CreateFolderAsync(db, user, $"P{f}", f < 2 ? null : folders[user][f - 2], now, default)).Data!).Id);
            }
            var unexpected = new System.Collections.Concurrent.ConcurrentBag<string>(); var done = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(); var clock = TimeProvider.System;
            async Task Worker(int seed)
            {
                var rng = new Random(seed);
                for (var step = 0; step < 100; step++)
                {
                    await using var db = new LifeVerseDbContext(options); var me = users[rng.Next(users.Count)]; var op = rng.Next(11); var at = DateTimeOffset.UtcNow;
                    try
                    {
                        var mine = await db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == me && i.Status != "consumed" && i.Status != "purged").Select(i => i.Id).ToListAsync(); var pick = mine.Count > 0 ? mine[rng.Next(mine.Count)] : Guid.Empty; var folder = folders[me][rng.Next(4)];
                        switch (op)
                        {
                            case 0: await InventoryOrganizer.MoveAsync(db, me, [pick], null, rng.Next(3) == 0 ? null : folder, at, default); break;
                            case 1: await InventoryOrganizer.MoveAsync(db, me, null, [folders[me][rng.Next(4)]], rng.Next(3) == 0 ? null : folder, at, default); break;
                            case 2: await InventoryOrganizer.DeleteFolderAsync(db, me, folder, at, default); break;
                            case 3: await InventoryOrganizer.RestoreFolderAsync(db, me, folder, at, default); break;
                            case 4: await AssetEndpoints.DeleteItemAsync(pick, As(me), db, clock, default); break;
                            case 5: await AssetEndpoints.RestoreItemAsync(pick, As(me), db, default); break;
                            case 6: if (rng.Next(4) == 0) await InventoryOrganizer.PurgeItemAsync(db, me, pick, at, default); break;
                            case 7: await AssetEndpoints.UpdateItemAsync(pick, new InventoryItemUpdateRequest(null, null, rng.Next(2) == 0), As(me), db, clock, default); break;
                            case 8: { var to = users[rng.Next(users.Count)]; await ItemTransfers.OfferAsync(db, me, pick, to, "gift", "oi", false, "k" + Guid.NewGuid().ToString("N"), at, default); break; }
                            case 9:
                            {
                                var pending = await db.ItemOffers.AsNoTracking().Where(o => o.Status == "pending").OrderBy(o => o.CreatedAt).Take(5).ToListAsync();
                                if (pending.Count > 0) { var o = pending[rng.Next(pending.Count)]; var how = rng.Next(3); if (how == 0) await ItemTransfers.AcceptAsync(db, o.ToUserId, o.Id, at, default); else if (how == 1) await ItemTransfers.RejectAsync(db, o.ToUserId, o.Id, at, default); else await ItemTransfers.CancelAsync(db, o.FromUserId, o.Id, at, default); }
                                break;
                            }
                            case 10: await InventoryStorage.ReportAsync(db, me, InventoryStorage.DefaultQuotaBytes, default); break;
                        }
                        done.AddOrUpdate("op" + op, 1, (_, c) => c + 1);
                    }
                    catch (Exception exception) when (op is 4 or 5 or 7 && (exception is DbUpdateConcurrencyException || RaceFailures.IsRace(exception))) { done.AddOrUpdate("stale" + op, 1, (_, c) => c + 1); } // the endpoint filter turns this into a "try again" (409) for the person
                    catch (Exception exception) { unexpected.Add($"op {op}: {exception.GetType().Name}: {exception.Message}"); }
                }
            }
            await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => Worker(700 + i))));
            output.WriteLine("event=inventory_chaos " + string.Join(" ", done.OrderBy(d => d.Key).Select(d => $"{d.Key}={d.Value}"))); foreach (var line in unexpected.Distinct().Take(5)) output.WriteLine("unexpected: " + line);
            Assert.Empty(unexpected.Take(10));
            await using var check = new LifeVerseDbContext(options);
            foreach (var asset in assets) Assert.Equal(1, await check.InventoryItems.CountAsync(i => i.AssetId == asset)); // nothing was ever copied or lost on the way
            foreach (var user in users)
            {
                var live = await check.InventoryFolders.AsNoTracking().Where(f => f.OwnerUserId == user && f.DeletedAt == null).ToListAsync(); var parents = live.ToDictionary(f => f.Id, f => f.ParentId);
                foreach (var f in live) { Assert.InRange(FolderRules.DepthOf(parents, f.Id), 1, FolderRules.MaxDepth); Assert.True(f.ParentId is null || parents.ContainsKey(f.ParentId.Value), "a live folder inside one that is not live"); }
                var liveIds = parents.Keys.ToHashSet(); var items = await check.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == user && i.DeletedAt == null).ToListAsync();
                Assert.All(items, i => Assert.True(i.FolderId is null || liveIds.Contains(i.FolderId.Value), "a live item in a folder that is gone or not theirs"));
                var report = await InventoryStorage.ReportAsync(check, user, InventoryStorage.DefaultQuotaBytes, default); var held = await check.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == user && i.Status != "consumed" && i.Status != "purged").Select(i => i.AssetId).Distinct().ToListAsync();
                Assert.Equal(held.Count, report.Assets); Assert.True(report.UsedBytes >= held.Count * 1000L);
            }
            var integrity = await InventoryIntegrity.CheckAsync(check, null, default); Assert.True(integrity.Clean, "the duplication check found: " + string.Join("; ", integrity.Findings.Select(x => x.Rule + " " + x.Detail)));
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task TheInventoryComesBackIdenticalAfterARestart()
    {
        var (name, options) = await CreateAsync();
        try
        {
            var now = DateTimeOffset.UtcNow; var user = Guid.NewGuid(); var other = Guid.NewGuid();
            await using (var db = new LifeVerseDbContext(options))
            {
                db.Users.Add(NewUser(user, "eu", now)); db.Users.Add(NewUser(other, "outro", now)); var itemIds = new List<Guid>();
                for (var i = 0; i < 6; i++)
                {
                    var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = user, CreatorUserId = user, Name = $"Peça {i}", Category = "textures", CurrentVersion = i == 0 ? 2 : 1, CreatedAt = now, UpdatedAt = now }; db.Assets.Add(asset);
                    db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k", MimeType = "image/png", ByteSize = 100, CreatedAt = now });
                    if (i == 0) db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 2, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k2", MimeType = "image/png", ByteSize = 150, Notes = "mais claro", CreatedAt = now.AddMinutes(1) });
                    var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = user, AssetId = asset.Id, DisplayName = asset.Name, CreatedAt = now.AddSeconds(i), UpdatedAt = now }; db.InventoryItems.Add(item); itemIds.Add(item.Id);
                }
                await db.SaveChangesAsync();
                var casa = (InventoryFolder)(await InventoryOrganizer.CreateFolderAsync(db, user, "Casa", null, now, default)).Data!; var sala = (InventoryFolder)(await InventoryOrganizer.CreateFolderAsync(db, user, "Sala", casa.Id, now, default)).Data!; var velha = (InventoryFolder)(await InventoryOrganizer.CreateFolderAsync(db, user, "Velha", null, now, default)).Data!;
                Assert.True((await InventoryOrganizer.MoveAsync(db, user, [itemIds[0], itemIds[1]], null, sala.Id, now, default)).Ok); Assert.True((await InventoryOrganizer.MoveAsync(db, user, [itemIds[2]], null, velha.Id, now, default)).Ok);
                Assert.True((await InventoryOrganizer.DeleteFolderAsync(db, user, velha.Id, now, default)).Ok); // item 2 goes to the bin with its folder
                await AssetEndpoints.UpdateItemAsync(itemIds[3], new InventoryItemUpdateRequest(null, null, true), As(user), db, TimeProvider.System, default);
                var offer = await ItemTransfers.OfferAsync(db, user, itemIds[4], other, "gift", "oi", false, "chave-reinicio-1", now, default); Assert.True(offer.Ok); await InventoryOrganizer.TouchAsync(db, user, itemIds[5], now, default);
            }
            async Task<string> Snapshot()
            {
                await using var db = new LifeVerseDbContext(options);
                var folders = (await db.InventoryFolders.AsNoTracking().OrderBy(f => f.Name).ToListAsync()).Select(f => $"{f.Name}|{f.ParentId is not null}|{f.DeletedAt is not null}");
                var items = (await db.InventoryItems.AsNoTracking().OrderBy(i => i.DisplayName).ToListAsync()).Select(i => $"{i.DisplayName}|{i.OwnerUserId == user}|{i.FolderId is not null}|{i.DeletedAt is not null}|{i.DeletedWithFolderId is not null}|{i.Status}|{i.IsFavorite}|{i.LastUsedAt is not null}");
                var versions = (await db.AssetVersions.AsNoTracking().OrderBy(v => v.ByteSize).ThenBy(v => v.Version).ToListAsync()).Select(v => $"{v.Version}|{v.ByteSize}|{v.Notes}");
                var report = await InventoryStorage.ReportAsync(db, user, InventoryStorage.DefaultQuotaBytes, default); var offers = (await db.ItemOffers.AsNoTracking().ToListAsync()).Select(o => $"{o.Status}|{o.KeepCopy}");
                return string.Join("\n", folders.Concat(items).Concat(versions).Concat(offers).Append($"used={report.UsedBytes} bin={report.InBinBytes} older={report.OlderVersionsBytes}"));
            }
            var before = await Snapshot(); NpgsqlConnection.ClearAllPools(); var after = await Snapshot(); // the process "restarts": no connection, no cache, no tracked state
            Assert.Equal(before, after); Assert.Contains("Velha|False|True", after); Assert.Contains("pending|False", after); Assert.Contains("older=100", after); Assert.Contains("mais claro", after);
            await using var db2 = new LifeVerseDbContext(options);
            Assert.Single(await db2.InventoryItems.Where(i => i.DeletedWithFolderId != null).ToListAsync());
            var restored = await InventoryOrganizer.RestoreFolderAsync(db2, user, (await db2.InventoryFolders.SingleAsync(f => f.Name == "Velha")).Id, DateTimeOffset.UtcNow, default); Assert.True(restored.Ok);
            Assert.Empty(await db2.InventoryItems.Where(i => i.DeletedWithFolderId != null || (i.OwnerUserId == user && i.DeletedAt != null)).ToListAsync()); // and the bin works the same after the restart
        }
        finally { await DropAsync(name); }
    }
}
