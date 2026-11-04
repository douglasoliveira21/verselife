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
}
