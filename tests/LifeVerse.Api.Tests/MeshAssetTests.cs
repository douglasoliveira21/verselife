using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class MeshAssetTests
{
    private static LifeVerseDbContext NewDb() => new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (Guid Asset, Guid Owner) AddModel(LifeVerseDbContext db, string mime = "model/gltf-binary", bool deleted = false)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), CreatorUserId = Guid.NewGuid(), Name = "caixa", Category = "models", DeletedAt = deleted ? DateTimeOffset.UtcNow : null };
        db.Assets.Add(asset);
        db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = "h", StorageKey = "k", MimeType = mime });
        db.SaveChanges();
        return (asset.Id, asset.OwnerUserId);
    }

    private static string Geometry(Guid asset) => $"{{\"assetId\":\"{asset}\"}}";

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("not json")] [InlineData("[]")] [InlineData("{}")] [InlineData("{\"assetId\":5}")] [InlineData("{\"assetId\":\"nope\"}")]
    public void AssetIdOf_IgnoresAnythingThatIsNotAGuid(string? json) => Assert.Null(MeshAssets.AssetIdOf(json));

    [Fact]
    public void AssetIdOf_ReadsTheId()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, MeshAssets.AssetIdOf($"{{\"hollow\":0,\"assetId\":\"{id}\"}}"));
        Assert.Equal(id, MeshAssets.AssetIdOf($"{{\"assetId\":\"{id.ToString().ToUpperInvariant()}\"}}"));
    }

    [Fact]
    public async Task OrdinaryPrimitivesAreNeverChecked()
    {
        using var db = NewDb();
        Assert.Null(await MeshAssets.ValidateAsync(db, Guid.NewGuid(), "cube", "{}", null, default));
        Assert.Null(await MeshAssets.ValidateAsync(db, Guid.NewGuid(), "sphere", "{\"assetId\":\"junk\"}", null, default));
    }

    [Fact]
    public async Task AMeshNeedsARealModelTheUserHas()
    {
        using var db = NewDb();
        var user = Guid.NewGuid();
        Assert.Contains("assetId", await MeshAssets.ValidateAsync(db, user, "mesh", "{}", null, default));
        Assert.Contains("does not exist", await MeshAssets.ValidateAsync(db, user, "mesh", Geometry(Guid.NewGuid()), null, default));

        var (asset, owner) = AddModel(db);
        Assert.Contains("inventory", await MeshAssets.ValidateAsync(db, user, "MESH", Geometry(asset), null, default));
        Assert.Null(await MeshAssets.ValidateAsync(db, owner, "mesh", Geometry(asset), null, default)); // the uploader
        db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = user, AssetId = asset, DisplayName = "caixa" });
        db.SaveChanges();
        Assert.Null(await MeshAssets.ValidateAsync(db, user, "mesh", Geometry(asset), null, default)); // owns an inventory item of it
    }

    [Fact]
    public async Task EditsThatKeepTheSameModelAreAlwaysAllowed()
    {
        using var db = NewDb();
        var (asset, _) = AddModel(db);
        var stranger = Guid.NewGuid();
        Assert.Null(await MeshAssets.ValidateAsync(db, stranger, "mesh", Geometry(asset), asset, default));
        var (other, _) = AddModel(db);
        Assert.Contains("inventory", await MeshAssets.ValidateAsync(db, stranger, "mesh", Geometry(other), asset, default)); // swapping in someone else's model is not
    }

    [Theory]
    [InlineData("image/png")] [InlineData("audio/ogg")]
    public async Task OnlyModelsCanBeUsedAsMeshes(string mime)
    {
        using var db = NewDb();
        var (asset, owner) = AddModel(db, mime);
        Assert.Contains("not a 3D model", await MeshAssets.ValidateAsync(db, owner, "mesh", Geometry(asset), null, default));
    }

    [Fact]
    public async Task DeletedModelsAreRefused()
    {
        using var db = NewDb();
        var (asset, owner) = AddModel(db, deleted: true);
        Assert.Contains("does not exist", await MeshAssets.ValidateAsync(db, owner, "mesh", Geometry(asset), null, default));
    }
}
