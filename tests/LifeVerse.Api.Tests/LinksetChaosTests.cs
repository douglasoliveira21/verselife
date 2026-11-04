using System.Collections.Concurrent;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Building;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.Api.Tests;

/// <summary>
/// Several editors changing the same linksets at the same time on a real PostgreSQL: linking, unlinking, changing roots, protecting, deleting and returning
/// objects, at random. Whatever the order, no linkset may end up broken (missing parent, cycle, two roots, a single member), and nothing may fail unexpectedly.
/// </summary>
public sealed class LinksetChaosTests(ITestOutputHelper output)
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    private sealed class MemoryStore : IObjectStore
    {
        public readonly ConcurrentDictionary<string, byte[]> Files = new();
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    [PostgresFact]
    public async Task EditorsRandomlyLinkingUnlinkingProtectingAndRemovingNeverBreakALinkset()
    {
        var name = "lifeverse_l_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            var cs = new NpgsqlConnectionStringBuilder(Admin) { Database = name, Pooling = true, MaxPoolSize = 40 }.ConnectionString; var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options;
            await using (var migrate = new LifeVerseDbContext(options)) await migrate.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow; var owner = Guid.NewGuid(); var instance = Guid.NewGuid(); var store = new MemoryStore();
            var storage = new AssetStorageService(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<AssetStorageService>.Instance); var ids = new List<Guid>();
            await using (var db = new LifeVerseDbContext(options))
            {
                db.Users.Add(new User { Id = owner, Username = "dono", NormalizedUsername = "DONO", Email = "d@t.local", NormalizedEmail = "D@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now });
                var island = new Island { Id = Guid.NewGuid(), OwnerUserId = owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 }; db.Islands.Add(island); db.ServerInstances.Add(new ServerInstance { Id = instance, IslandId = island.Id, Hostname = "h", Status = "healthy" });
                for (var i = 0; i < 14; i++) { var o = new BuildObject { Id = Guid.NewGuid(), IslandId = island.Id, CreatorUserId = owner, OwnerUserId = owner, Name = "peça " + i, PrimitiveType = "cube", PositionX = i * 3, CreatedAt = now, UpdatedAt = now }; db.BuildObjects.Add(o); ids.Add(o.Id); }
                await db.SaveChangesAsync();
            }

            var unexpected = new ConcurrentBag<string>(); var done = new ConcurrentDictionary<string, int>();
            ServerBuildOperationRequest Request(string operation, Guid? id = null, int revision = 0, Guid[]? ids = null, StructureEntry[]? links = null, bool? locked = null) => new(owner, operation, id, null, revision, ids, null, 0, 0, 0, links, locked);
            async Task<int> Apply(LifeVerseDbContext db, ServerBuildOperationRequest request) { var r = await BuildOperations.ApplyAsync(instance, request, db, storage, TimeProvider.System, default); return (r as IStatusCodeHttpResult)?.StatusCode ?? 200; }
            async Task Worker(int seed)
            {
                var rng = new Random(seed);
                for (var step = 0; step < 120; step++)
                {
                    await using var db = new LifeVerseDbContext(options); var op = rng.Next(8);
                    try
                    {
                        var live = await db.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).Select(o => new { o.Id, o.GroupId, o.ParentObjectId, o.Revision }).ToListAsync(); if (live.Count < 3) op = 6;
                        var world = live.Select(o => new LinkNode(o.Id, o.GroupId, o.ParentObjectId)).ToList(); var revision = live.ToDictionary(o => o.Id, o => o.Revision);
                        var pick = live.OrderBy(_ => rng.Next()).Take(rng.Next(2, 6)).Select(o => o.Id).ToList(); int status;
                        switch (op)
                        {
                            case 0: { var group = world.Where(n => n.GroupId is not null && pick.Contains(n.Id)).Select(n => n.GroupId).FirstOrDefault() ?? Guid.NewGuid(); var wholeSets = pick.Concat(world.Where(n => n.GroupId is not null && world.Where(m => pick.Contains(m.Id)).Any(m => m.GroupId == n.GroupId)).Select(n => n.Id)).Distinct().ToList(); var plan = LinksetRules.Link(world, wholeSets, pick[0], group); if (plan.Count == 0) break; status = await Apply(db, Request("structure", links: plan.Select(n => new StructureEntry(n.Id, n.GroupId, n.ParentId, revision[n.Id])).ToArray())); Check("link", status); break; }
                            case 1: { var plan = LinksetRules.Detach(world, pick); if (plan.Count == 0) break; status = await Apply(db, Request("structure", links: plan.Select(n => new StructureEntry(n.Id, n.GroupId, n.ParentId, revision[n.Id])).ToArray())); Check("unlink", status); break; }
                            case 2: { var member = world.Where(n => n.GroupId is not null && n.ParentId is not null).OrderBy(_ => rng.Next()).FirstOrDefault(); if (member is null) break; var plan = LinksetRules.SetRoot(world, member.Id); if (plan.Count == 0) break; status = await Apply(db, Request("structure", links: plan.Select(n => new StructureEntry(n.Id, n.GroupId, n.ParentId, revision[n.Id])).ToArray())); Check("root", status); break; }
                            case 3: { status = await Apply(db, Request("protect", ids: [pick[0]], locked: rng.Next(2) == 0)); Check("protect", status); break; }
                            case 4 when live.Count > 8: { status = await Apply(db, Request("delete", id: pick[0], revision: revision[pick[0]])); Check("delete", status); break; }
                            case 5 when live.Count > 8: { status = await Apply(db, Request("return", ids: [pick[0]])); Check("return", status); break; }
                            case 6 or 7: { status = await Apply(db, Request("create") with { Object = new BuildObjectRequest("cube", rng.Next(0, 90), 1, rng.Next(0, 90), 0, 0, 0, 1, 1, 1, "{}", "{}", null, 0, "{}", "nova", null, null, null, null) }); Check("create", status); break; }
                        }
                        done.AddOrUpdate("op" + op, 1, (_, n) => n + 1);
                        void Check(string what, int s) { if (s is not (200 or 400 or 403 or 404 or 409)) unexpected.Add($"{what}: status {s}"); }
                    }
                    catch (Exception exception) { unexpected.Add($"op {op}: {exception.GetType().Name}: {exception.Message}"); }
                }
            }
            await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => Worker(3000 + i))));
            output.WriteLine("event=linkset_chaos " + string.Join(" ", done.OrderBy(d => d.Key).Select(d => $"{d.Key}={d.Value}")));

            await using var check = new LifeVerseDbContext(options); foreach (var line in unexpected.Distinct().Take(5)) output.WriteLine("unexpected: " + line);
            Assert.Empty(unexpected.Take(10));
            var live2 = await check.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).ToListAsync(); var world2 = live2.Select(o => new LinkNode(o.Id, o.GroupId, o.ParentObjectId)).ToList();
            Assert.Null(LinksetRules.Validate(world2)); // no missing parent, no cycle, nobody hanging in another linkset
            foreach (var set in world2.Where(n => n.GroupId is not null).GroupBy(n => n.GroupId)) { Assert.True(set.Count() >= 2, "a linkset of one member must have been dissolved"); Assert.Equal(1, set.Count(n => n.ParentId is null)); } // and exactly one root
            output.WriteLine($"event=linkset_chaos_result live={live2.Count} linksets={world2.Where(n => n.GroupId is not null).Select(n => n.GroupId).Distinct().Count()}");
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync(); await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync(); }
    }

}

