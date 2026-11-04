using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Items;
using LifeVerse.Shared.Networking;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.Api.Tests;

/// <summary>
/// The advanced build editor end to end, on a real PostgreSQL where it matters: several people editing the same objects at once, constructions placed many times
/// at once, and the whole world coming back identical after a restart.
/// </summary>
public sealed class BuildEditorPostgresTests(ITestOutputHelper output)
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    private sealed class MemoryStore : IObjectStore
    {
        public readonly ConcurrentDictionary<string, byte[]> Files = new();
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private sealed class World : IAsyncDisposable
    {
        public string Name = ""; public DbContextOptions<LifeVerseDbContext> Options = null!; public Guid Owner = Guid.NewGuid(), Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Island, Instance = Guid.NewGuid();
        public MemoryStore Store = new(); public AssetStorageService Storage => new(Store, Microsoft.Extensions.Logging.Abstractions.NullLogger<AssetStorageService>.Instance);

        public static async Task<World> CreateAsync()
        {
            var world = new World { Name = "lifeverse_e_" + Guid.NewGuid().ToString("N")[..12] };
            await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{world.Name}\"", connection); await command.ExecuteNonQueryAsync(); }
            world.Options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(Admin) { Database = world.Name, Pooling = true, MaxPoolSize = 50 }.ConnectionString).Options;
            await using (var migrate = new LifeVerseDbContext(world.Options)) await migrate.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            await using var db = new LifeVerseDbContext(world.Options);
            User Make(Guid id, string n) => new() { Id = id, Username = n, NormalizedUsername = n.ToUpperInvariant(), Email = n + "@t.local", NormalizedEmail = n.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now };
            db.Users.AddRange(Make(world.Owner, "dono"), Make(world.Alice, "alice"), Make(world.Bob, "bob"));
            var island = new Island { Id = Guid.NewGuid(), OwnerUserId = world.Owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 }; world.Island = island.Id; db.Islands.Add(island);
            db.ServerInstances.Add(new ServerInstance { Id = world.Instance, IslandId = island.Id, Hostname = "h", Status = "healthy" });
            foreach (var user in new[] { world.Alice, world.Bob }) db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = user, Permission = "build", PrincipalType = "user", GrantedByUserId = world.Owner });
            await db.SaveChangesAsync(); return world;
        }

        public async Task<(int Status, JsonElement Body)> Send(LifeVerseDbContext db, Guid actor, string operation, BuildObjectRequest? o = null, Guid? id = null, int revision = 0, Guid[]? ids = null, Guid? package = null, float originX = 0, StructureEntry[]? links = null, bool? locked = null)
        {
            var result = await BuildOperations.ApplyAsync(Instance, new ServerBuildOperationRequest(actor, operation, id, o, revision, ids, package, originX, 0, 0, links, locked), db, Storage, TimeProvider.System, default);
            return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }

        public async Task<Guid> Create(Guid actor, BuildObjectRequest o) { await using var db = new LifeVerseDbContext(Options); var (s, b) = await Send(db, actor, "create", o); Assert.True(s == 200, s + " " + b); return b.GetProperty("objectId").GetGuid(); }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools(); await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
        }
    }

    private static BuildObjectRequest Box(float x, string name = "Peça", Guid? group = null, Guid? parent = null, int revision = 0, string physics = "{\"collision\":true}", string? description = null, float pivotY = 0) =>
        new("cube", x, 1, 0, 0, 0, 0, 1, 1, 1, "{}", physics, group, revision, "{}", name, description, parent, null, null, 0, pivotY, 0);

    [PostgresFact]
    public async Task EditorsHammeringTheSameObjectsNeverLoseAnUpdateAndEveryAcceptedChangeCountsOnce()
    {
        await using var world = await World.CreateAsync();
        var objects = new List<Guid>(); for (var i = 0; i < 6; i++) objects.Add(await world.Create(world.Alice, Box(i * 3, $"Peça {i}")));
        var initial = new Dictionary<Guid, int>(); await using (var read = new LifeVerseDbContext(world.Options)) foreach (var o in await read.BuildObjects.AsNoTracking().ToListAsync()) initial[o.Id] = o.Revision;
        var accepted = new ConcurrentDictionary<Guid, int>(); var conflicts = 0; var unexpected = new ConcurrentBag<string>();
        async Task Worker(Guid actor, int seed)
        {
            var rng = new Random(seed);
            for (var step = 0; step < 60; step++)
            {
                var id = objects[rng.Next(objects.Count)]; await using var db = new LifeVerseDbContext(world.Options);
                try
                {
                    var current = await db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);
                    var (status, body) = await world.Send(db, actor, "update", Box(current.PositionX, current.Name, revision: current.Revision, description: $"{actor:N}:{step}"), id, current.Revision);
                    if (status == 200) accepted.AddOrUpdate(id, 1, (_, n) => n + 1);
                    else if (status == 409 && body.GetProperty("code").GetString() == "revision_conflict") Interlocked.Increment(ref conflicts);
                    else unexpected.Add($"{status} {body}");
                }
                catch (Exception exception) { unexpected.Add(exception.GetType().Name + ": " + exception.Message); }
            }
        }
        await Task.WhenAll(new[] { world.Alice, world.Owner, world.Alice, world.Owner, world.Owner, world.Alice }.Select((actor, i) => Task.Run(() => Worker(actor, 100 + i))));
        Assert.Empty(unexpected);
        await using var check = new LifeVerseDbContext(world.Options);
        foreach (var id in objects)
        {
            var now = await check.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id); var n = accepted.GetValueOrDefault(id);
            Assert.Equal(initial[id] + n, now.Revision); // one revision per accepted change: nothing lost, nothing counted twice
            Assert.Equal(n, await check.BuildOperations.CountAsync(o => o.ObjectId == id && o.Operation == "update"));
        }
        output.WriteLine($"accepted {accepted.Values.Sum()}, refused as stale {conflicts}");
        Assert.True(accepted.Values.Sum() > 0);
    }

    [PostgresFact]
    public async Task ABuildersLinksetWithPivotsPhysicsAndProtectionComesBackIdenticalAfterARestartAndCanBeReturnedAndPlacedAgain()
    {
        await using var world = await World.CreateAsync();
        var set = Guid.NewGuid(); var root = await world.Create(world.Alice, Box(0, "Raiz", set, pivotY: -.5f, physics: "{\"collision\":true,\"mode\":\"physical\",\"mass\":8}"));
        var a = await world.Create(world.Alice, Box(2, "A", set, root, pivotY: .25f)); var b = await world.Create(world.Alice, Box(4, "B", set, a, physics: "{\"collision\":false}"));
        await using (var db = new LifeVerseDbContext(world.Options)) Assert.Equal(200, (await world.Send(db, world.Alice, "protect", ids: [b], locked: true)).Status);

        async Task<List<string>> Snapshot()
        {
            await using var db = new LifeVerseDbContext(world.Options);
            return (await db.BuildObjects.AsNoTracking().OrderBy(o => o.Name).ToListAsync()).Select(o => JsonSerializer.Serialize(BuildEndpoints.ToResponse(o))).ToList();
        }
        var before = await Snapshot(); await using (var db = new LifeVerseDbContext(world.Options)) { var operations = await db.BuildOperations.CountAsync(); Assert.True(operations >= 4); }
        NpgsqlConnection.ClearAllPools(); // the API process "restarts": no connection, no cache and no tracked state survives
        var after = await Snapshot(); Assert.Equal(before, after);
        await using (var db = new LifeVerseDbContext(world.Options))
        {
            var rows = await db.BuildObjects.AsNoTracking().ToListAsync(); Assert.Equal(3, rows.Count); Assert.Single(rows, o => o.ParentObjectId is null); Assert.Equal(-.5f, rows.Single(o => o.Name == "Raiz").PivotY);
            Assert.True(rows.Single(o => o.Name == "B").EditLocked); Assert.Contains("physical", rows.Single(o => o.Name == "Raiz").PhysicsJson); Assert.Contains("false", rows.Single(o => o.Name == "B").PhysicsJson);
            Assert.Equal(200, (await world.Send(db, world.Alice, "protect", ids: [b], locked: false)).Status);
        }

        // return the whole construction to the inventory, restart again, then place it twice: both copies are complete linksets
        await using (var db = new LifeVerseDbContext(world.Options)) Assert.Equal(200, (await world.Send(db, world.Alice, "return", ids: [root, a, b])).Status);
        NpgsqlConnection.ClearAllPools();
        await using (var db = new LifeVerseDbContext(world.Options))
        {
            Assert.Equal(0, await db.BuildObjects.CountAsync(o => o.DeletedAt == null));
            var item = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.OwnerUserId == world.Alice && i.DisplayName.StartsWith("Devolvido"));
            Assert.Equal(200, (await world.Send(db, world.Alice, "rez", package: item.Id, originX: 50)).Status);
        }
        await using var last = new LifeVerseDbContext(world.Options);
        var placed = await last.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).ToListAsync(); Assert.Equal(3, placed.Count);
        Assert.Single(placed.Select(o => o.GroupId).Distinct()); Assert.NotEqual(set, placed[0].GroupId); Assert.Single(placed, o => o.ParentObjectId is null);
        Assert.Equal(placed.Single(o => o.Name == "Raiz").Id, placed.Single(o => o.Name == "A").ParentObjectId); Assert.Equal(placed.Single(o => o.Name == "A").Id, placed.Single(o => o.Name == "B").ParentObjectId);
        Assert.Equal(.25f, placed.Single(o => o.Name == "A").PivotY); Assert.Equal(-.5f, placed.Single(o => o.Name == "Raiz").PivotY);
        Assert.All(placed, o => Assert.InRange(o.PositionX, 47.9f, 52.1f)); // placed where asked
        Assert.Null(LinksetRules.Validate(placed.Select(o => new LinkNode(o.Id, o.GroupId, o.ParentObjectId)).ToList()));
    }

    [PostgresFact]
    public async Task ManyPlacementsAtOnceMakeCompleteLinksetsAndAConstructionThatCannotBeCopiedIsPlacedExactlyOnce()
    {
        await using var world = await World.CreateAsync();
        var table = PackageDocument.Parse(BuildPackages.Serialize(BuildPresets.Find("table")!.Package), out _)!;
        Guid copyable, single;
        await using (var db = new LifeVerseDbContext(world.Options))
        {
            copyable = (await BuildPackageStore.SaveAsync(db, world.Storage, world.Alice, table, DateTimeOffset.UtcNow, default, new ItemPermissions(ItemRights.All, 0, ItemRights.All))).Id;
            single = (await BuildPackageStore.SaveAsync(db, world.Storage, world.Alice, table, DateTimeOffset.UtcNow, default, new ItemPermissions(ItemRights.Modify | ItemRights.Transfer, 0, ItemRights.Transfer))).Id;
            await db.SaveChangesAsync();
        }
        var statuses = new ConcurrentBag<int>(); var failures = new ConcurrentBag<string>();
        async Task Place(Guid item, float x)
        {
            try { await using var db = new LifeVerseDbContext(world.Options); var (status, body) = await world.Send(db, world.Alice, "rez", package: item, originX: x); statuses.Add(status); if (status is not (200 or 404 or 409)) failures.Add($"{status} {body}"); }
            catch (Exception exception) { failures.Add(exception.GetType().Name + ": " + exception.Message); }
        }
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => Place(copyable, 100 + i * 10))));
        Assert.Empty(failures);
        await using (var check = new LifeVerseDbContext(world.Options))
        {
            var rows = await check.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).ToListAsync(); var okCount = statuses.Count(s => s == 200);
            Assert.Equal(okCount * 5, rows.Count); var sets = rows.GroupBy(o => o.GroupId).ToList(); Assert.Equal(okCount, sets.Count);
            Assert.All(sets, s => { Assert.Equal(5, s.Count()); Assert.Null(LinksetRules.Validate(s.Select(o => new LinkNode(o.Id, o.GroupId, o.ParentObjectId)).ToList())); });
            Assert.True(okCount >= 1);
        }
        statuses.Clear();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() => Place(single, 300 + i * 10))));
        Assert.Empty(failures); Assert.Equal(1, statuses.Count(s => s == 200)); // the only one there is
        await using var after = await new LifeVerseDbContext(world.Options).Database.BeginTransactionAsync().ContinueWith(_ => new LifeVerseDbContext(world.Options));
        Assert.Equal(5, await after.BuildObjects.CountAsync(o => o.DeletedAt == null && o.PositionX > 290));
    }
}

/// <summary>What stops people: the collision rules of the objects the editor makes, as the island server applies them.</summary>
public sealed class BuildCollisionTests
{
    private const float Dt = 1f / 60f;

    private static ObstacleField FieldOf(IEnumerable<PlacedObject> placed, Func<PackageObject, string> physics)
    {
        var field = new ObstacleField();
        foreach (var p in placed) field.Set(Guid.NewGuid(), ObstacleField.Blocks(physics(p.Source)) ? ObstacleField.For(p.Source.PrimitiveType, p.Position, new Vector3(p.Source.RotationX, p.Source.RotationY, p.Source.RotationZ), new Vector3(p.Source.ScaleX, p.Source.ScaleY, p.Source.ScaleZ)) : null);
        return field;
    }

    private static float WalkZ(ObstacleField? field, int steps = 600)
    {
        var state = new AuthoritativePlayerState { X = 0, Y = 0, Z = 0 };
        for (var i = 0; i < steps; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 1, InputFlags.Run), Dt, null, float.NegativeInfinity, float.PositiveInfinity, field);
        return state.Z;
    }

    [Fact]
    public void ThePlacedRoomStopsAnAvatarAtItsWallAndAGhostWallDoesNot()
    {
        var room = BuildPresets.Find("room")!.Package; var placed = BuildPackages.Place(room, Vector3.Zero);
        var solid = FieldOf(placed, p => p.PhysicsJson); var open = WalkZ(null);
        Assert.True(open > 3f); // nothing in the way: it walks on
        var stopped = WalkZ(solid); Assert.InRange(stopped, .5f, 1.9f - .1f - ObstacleField.Radius + .05f); // the wall is 20 cm thick, centred 1.9 m away
        var ghost = FieldOf(placed, p => p.Name == "Parede" && p.Z > 1 ? "{\"collision\":false}" : p.PhysicsJson); Assert.True(WalkZ(ghost) > 3f); // a ghost wall can be walked through
    }

    [Theory]
    [InlineData("{\"collision\":true}", true)] [InlineData("{}", true)] [InlineData("", true)] [InlineData("{\"collision\":false}", false)] [InlineData("{\"trigger\":true}", false)]
    [InlineData("{\"collision\":true,\"mode\":\"physical\",\"mass\":5}", true)] [InlineData("{\"collision\":true,\"mode\":\"static\"}", true)] [InlineData("not json", true)]
    public void EachObjectsOwnCollisionSettingDecidesWhetherItBlocks(string physics, bool blocks) => Assert.Equal(blocks, ObstacleField.Blocks(physics));

    [Fact]
    public void APivotIsOnlyAnEditingAidAndNeverMovesTheCollisionShape()
    {
        var one = ObstacleField.For("cube", new Vector3(5, 1, 0), Vector3.Zero, Vector3.One)!; var two = ObstacleField.For("cube", new Vector3(5, 1, 0), Vector3.Zero, Vector3.One)!;
        Assert.Equal(one, two); // the shape depends on position, turn and size only: the pivot is not part of it
        var table = BuildPackages.Place(BuildPresets.Find("table")!.Package, Vector3.Zero);
        Assert.All(table, p => Assert.NotNull(ObstacleField.For(p.Source.PrimitiveType, p.Position, Vector3.Zero, new Vector3(p.Source.ScaleX, p.Source.ScaleY, p.Source.ScaleZ))));
    }

    [Fact]
    public void ARotatedWallStillBlocksAlongItsTurnedFace()
    {
        var field = new ObstacleField(); field.Set(Guid.NewGuid(), ObstacleField.For("cube", new Vector3(0, 1.5f, 3), new Vector3(0, 90, 0), new Vector3(.2f, 3, 6)));
        Assert.InRange(WalkZ(field), 1f, 3f - .1f - ObstacleField.Radius + .05f); // turned a quarter, a 6 m long wall across the path
    }
}
