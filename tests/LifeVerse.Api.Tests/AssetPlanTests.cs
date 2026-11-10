using System.Security.Claims;
using System.Text;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Shared.Loading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class AssetPlanTests
{
    private static AssetEntry A(double distance, long size, AssetKind kind = AssetKind.Texture, Guid? id = null) => new(id ?? Guid.NewGuid(), kind, "h", size, "image/png", distance);

    [Fact]
    public void TheNearestAssetsComeFirstAndWhatIsFarOrTooBigWaitsUntilAfterTheEntry()
    {
        var near = A(5, 1000); var mid = A(60, 2000); var far = A(300, 500); var huge = A(10, 30_000_000, AssetKind.Model);
        var plan = AssetPlanner.Plan([far, mid, huge, near]);
        Assert.Equal([near.AssetId, mid.AssetId], plan.Essential.Select(a => a.AssetId)); Assert.Equal(3000, plan.EssentialBytes);
        Assert.Equal([huge.AssetId, far.AssetId], plan.Deferred.Select(a => a.AssetId)); // the one over the budget goes with the far one, nearest first
    }

    [Fact]
    public void TheBudgetIsRespectedAndTheSameAssetIsNeverListedTwice()
    {
        var id = Guid.NewGuid(); var plan = AssetPlanner.Plan([A(40, 8_000_000, id: id), A(10, 8_000_000, id: id), A(20, 10_000_000), A(30, 10_000_000)], budgetBytes: 20_000_000);
        Assert.Equal(18_000_000, plan.EssentialBytes); Assert.Equal(3, plan.Essential.Count + plan.Deferred.Count); Assert.Equal(id, plan.Essential[0].AssetId); Assert.Equal(10, plan.Essential[0].Distance);
        Assert.Empty(AssetPlanner.Plan([]).Essential);
    }

    [Fact]
    public void AHashMatchesOnlyTheBytesItWasMadeFrom()
    {
        var bytes = Encoding.UTF8.GetBytes("pedra"); var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True(AssetPlanner.HashMatches(hash, bytes)); Assert.True(AssetPlanner.HashMatches(hash.ToUpperInvariant(), bytes)); Assert.False(AssetPlanner.HashMatches(hash, Encoding.UTF8.GetBytes("pedras")));
        Assert.False(AssetPlanner.HashMatches("", bytes)); Assert.False(AssetPlanner.HashMatches("zz", bytes));
    }
}

public sealed class AssetManifestTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _owner = Guid.NewGuid(), _visitor = Guid.NewGuid();
    private readonly Island _island;

    public AssetManifestTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400, SpawnX = 0, SpawnZ = 0 };
        _db.Islands.Add(_island); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid AddAsset(string mime, string hash, long size)
    {
        var id = Guid.NewGuid(); _db.Assets.Add(new Asset { Id = id, OwnerUserId = _owner, CreatorUserId = _owner, Name = "a", Category = "x", CurrentVersion = 2 });
        _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = id, Version = 2, ContentHash = hash, StorageKey = "k", MimeType = mime, ByteSize = size });
        _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = id, Version = 1, ContentHash = "old", StorageKey = "k0", MimeType = mime, ByteSize = 1 });
        return id;
    }

    private void AddObject(string type, float x, float z, string geometry = "{}", string material = "{}") =>
        _db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "o", PrimitiveType = type, PositionX = x, PositionZ = z, GeometryJson = geometry, MaterialJson = material });

    private async Task<(int Status, AssetManifest? Body)> Ask(Guid user, float? x = null, float? z = null)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())])) };
        var result = await AssetManifestEndpoints.ManifestAsync(_island.Id, x, z, context, _db, default);
        if (result is Ok<AssetManifest> ok) return (200, ok.Value);
        return (result is ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 0, null);
    }

    [Fact]
    public async Task ListsTheModelsAndImagesObjectsUseWithHashSizeAndTheDistanceOfTheNearestOne()
    {
        var model = AddAsset("model/gltf-binary", "aaa", 5000); var texture = AddAsset("image/png", "bbb", 800); var shared = AddAsset("image/png", "ccc", 300); var ignored = AddAsset("audio/wav", "ddd", 10);
        AddObject("mesh", 30, 40, geometry: $"{{\"assetId\":\"{model}\"}}"); AddObject("cube", 100, 0, material: $"{{\"texture\":\"{texture}\"}}");
        AddObject("cube", 3, 4, material: $"{{\"texture\":\"{shared}\"}}"); AddObject("cube", 200, 0, material: $"{{\"texture\":\"{shared}\"}}"); AddObject("cube", 1, 1, material: $"{{\"texture\":\"{ignored}\"}}");
        await _db.SaveChangesAsync();
        var (status, manifest) = await Ask(_visitor);
        Assert.Equal(200, status); Assert.Equal([shared, model, texture], manifest!.Assets.Select(a => a.AssetId)); // nearest first; the sound is not an image, so it is not listed
        Assert.Equal(("ccc", 5.0), (manifest.Assets[0].Hash, manifest.Assets[0].Distance)); Assert.Equal("model", manifest.Assets[1].Kind); Assert.Equal(50, manifest.Assets[1].Distance); Assert.Equal(6100, manifest.TotalBytes);
        var elsewhere = (await Ask(_visitor, 100, 0)).Body!; Assert.Equal(texture, elsewhere.Assets[0].AssetId); Assert.Equal(0, elsewhere.Assets[0].Distance);
    }

    [Fact]
    public async Task APrivateIslandKeepsItsManifestFromStrangersAndAnEmptyOneIsEmpty()
    {
        Assert.Equal(0, (await Ask(_visitor)).Body!.Count);
        _island.IsPublic = false; await _db.SaveChangesAsync();
        Assert.Equal(403, (await Ask(_visitor)).Status); Assert.Equal(200, (await Ask(_owner)).Status);
        Assert.Equal(404, (await AssetManifestEndpoints.ManifestAsync(Guid.NewGuid(), null, null, new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", _owner.ToString())])) }, _db, default) as IStatusCodeHttpResult)?.StatusCode);
    }
}

/// <summary>The manifest on a real PostgreSQL, where the JSON columns are jsonb: the query that works in memory must work there too.</summary>
public sealed class AssetManifestPostgresTests
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    [PostgresFact]
    public async Task TheManifestQueryRunsOnJsonbColumnsAndFindsModelsAndTextures()
    {
        var name = "lifeverse_m_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new Npgsql.NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            var cs = new Npgsql.NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString; var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options;
            await using (var migrate = new LifeVerseDbContext(options)) await migrate.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow; var owner = Guid.NewGuid(); var island = Guid.NewGuid(); var model = Guid.NewGuid(); var texture = Guid.NewGuid();
            await using var db = new LifeVerseDbContext(options);
            db.Users.Add(new User { Id = owner, Username = "dono", NormalizedUsername = "DONO", Email = "d@t.local", NormalizedEmail = "D@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now });
            db.Islands.Add(new Island { Id = island, OwnerUserId = owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 });
            foreach (var (id, mime, hash) in new[] { (model, "model/gltf-binary", "aaa"), (texture, "image/png", "bbb") })
            {
                db.Assets.Add(new Asset { Id = id, OwnerUserId = owner, CreatorUserId = owner, Name = "a", Category = "x", CurrentVersion = 1 });
                db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = id, Version = 1, ContentHash = hash, StorageKey = "k" + hash, MimeType = mime, ByteSize = 100 });
            }
            db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = island, CreatorUserId = owner, OwnerUserId = owner, Name = "m", PrimitiveType = "mesh", PositionX = 30, PositionZ = 40, GeometryJson = $"{{\"assetId\":\"{model}\"}}" });
            db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = island, CreatorUserId = owner, OwnerUserId = owner, Name = "c", PrimitiveType = "cube", PositionX = 3, PositionZ = 4, MaterialJson = $"{{\"texture\":\"{texture}\"}}" });
            db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = island, CreatorUserId = owner, OwnerUserId = owner, Name = "plain", PrimitiveType = "cube", PositionX = 1, PositionZ = 1 });
            await db.SaveChangesAsync();
            var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())])) };
            var result = await AssetManifestEndpoints.ManifestAsync(island, null, null, context, db, default);
            var ok = Assert.IsType<Ok<AssetManifest>>(result);
            Assert.Equal([texture, model], ok.Value!.Assets.Select(a => a.AssetId)); Assert.Equal(2, ok.Value.Count);
        }
        finally
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
            await using var connection = new Npgsql.NpgsqlConnection(Admin); await connection.OpenAsync(); await using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await drop.ExecuteNonQueryAsync();
        }
    }
}
