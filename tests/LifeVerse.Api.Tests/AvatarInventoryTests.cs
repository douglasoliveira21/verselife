using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Clothes and accessories as things of the inventory: made, kept, sold and bought like any asset, worn only by who holds them.</summary>
public sealed class AvatarInventoryTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
    private readonly DateTimeOffset _now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _maker = Guid.NewGuid(), _buyer = Guid.NewGuid(), _stranger = Guid.NewGuid();

    public AvatarInventoryTests()
    {
        foreach (var user in new[] { _maker, _buyer, _stranger })
        {
            var avatar = new Avatar { Id = Guid.NewGuid(), UserId = user, DisplayName = "Pessoa", BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now };
            _db.Avatars.Add(avatar);
            foreach (var (slot, key) in new[] { ("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") }) _db.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = slot, ItemKey = key, TintColor = "#FFFFFF", EquippedAt = _now });
        }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private async Task<(Guid Id, string Key)> Make(Guid user, byte[] bytes)
    {
        var result = await AvatarWearables.UploadAsync(_db, _storage, user, "item.glb", bytes, null, _now, default); Assert.True(result.Ok, result.Error);
        var item = JsonSerializer.SerializeToElement(result.Data, Web).GetProperty("item"); return (item.GetProperty("id").GetGuid(), item.GetProperty("key").GetString()!);
    }

    private static byte[] Hat() => WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head", offset: [0, .1f, 0]));
    private static SaveAvatarRequest Wear(string slot, string key) => new("Pessoa", "human_a", "#C98F65", "#2B1B14", "short", .003f, false, [new("top", "tee_basic", "#FFFFFF"), new("bottom", "pants_basic", "#FFFFFF"), new("shoes", "sneakers_basic", "#FFFFFF"), new(slot, key, "#FFFFFF")], "#4A3326", "none", "{}", "{}", "[]", "[]");

    private async Task<List<string>> Errors(Guid user, SaveAvatarRequest request)
    {
        var avatar = await _db.Avatars.AsNoTracking().SingleAsync(a => a.UserId == user);
        var errors = AvatarCatalog.Validate(request, await AvatarWearables.ResolverAsync(_db, avatar.Id, default));
        return errors.Count > 0 ? [.. errors.SelectMany(e => e.Value)] : await AvatarWearables.CheckOutfitAsync(_db, avatar, request.EquippedItems, default);
    }

    /// <summary>What the marketplace does when somebody buys a product: the buyer gets an inventory item of the same asset.</summary>
    private async Task Sell(Guid assetOwnerAsset, Guid to) { _db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = to, AssetId = assetOwnerAsset, DisplayName = "Comprado", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync(); }

    private async Task<Guid> AssetOf(Guid wearable) => (await _db.AvatarWearables.AsNoTracking().SingleAsync(w => w.Id == wearable)).AssetId!.Value;

    // ---- making ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AMadeItemIsAnAssetInTheInventoryOfItsMaker_SoItCanBeSold()
    {
        var (id, _) = await Make(_maker, Hat());
        var wearable = await _db.AvatarWearables.AsNoTracking().SingleAsync();
        var asset = await _db.Assets.AsNoTracking().SingleAsync(); var version = await _db.AssetVersions.AsNoTracking().SingleAsync(); var held = await _db.InventoryItems.AsNoTracking().SingleAsync();
        Assert.Equal(wearable.AssetId, asset.Id); Assert.Equal(_maker, asset.OwnerUserId); Assert.Equal("accessories", asset.Category); Assert.Equal("ready", asset.Status);
        Assert.Equal(wearable.ContentHash, version.ContentHash); Assert.Equal("model/gltf-binary", version.MimeType); Assert.Equal(wearable.SizeBytes, (int)version.ByteSize);
        Assert.Equal(_maker, held.OwnerUserId); Assert.Equal(asset.Id, held.AssetId); Assert.Null(held.DeletedAt);
        Assert.Equal(id, Assert.Single(await AvatarWearables.ListAsync(_db, _maker, default)).Id);
        // the marketplace checks exactly this before it lets a product be published
        Assert.True(await _db.Assets.AnyAsync(a => a.Id == asset.Id && a.OwnerUserId == _maker && a.DeletedAt == null));
    }

    [Fact]
    public async Task ClothingAndAccessoriesGetTheirOwnCategories()
    {
        await Make(_maker, WearableTemplate.Clothing("top", WearableTemplate.ShirtBones)); await Make(_maker, Hat());
        Assert.Equal(["accessories", "clothing"], (await _db.Assets.AsNoTracking().ToListAsync()).Select(a => a.Category).Order());
    }

    [Fact]
    public async Task TheSameFileTwiceIsStillOne()
    {
        await Make(_maker, Hat()); await Make(_maker, Hat());
        Assert.Single(_db.Assets); Assert.Single(_db.InventoryItems); Assert.Single(_db.AvatarWearables);
    }

    // ---- buying and selling --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WhoBuysTheItemCanWearIt_AndWhoDoesNotCannot()
    {
        var (id, key) = await Make(_maker, Hat());
        Assert.Contains(await Errors(_buyer, Wear("hat", key)), e => e.Contains("unknown"));
        await Sell(await AssetOf(id), _buyer);
        Assert.Empty(await Errors(_buyer, Wear("hat", key)));
        Assert.Equal(id, Assert.Single(await AvatarWearables.ListAsync(_db, _buyer, default)).Id);
        Assert.Contains(await Errors(_stranger, Wear("hat", key)), e => e.Contains("unknown"));
    }

    [Fact]
    public async Task TheMakerDeletingItDoesNotTakeItFromTheBuyer()
    {
        var (id, key) = await Make(_maker, Hat()); await Sell(await AssetOf(id), _buyer);
        Assert.True((await AvatarWearables.DeleteAsync(_db, _maker, id, _now, default)).Ok);
        Assert.Empty(await AvatarWearables.ListAsync(_db, _maker, default));
        Assert.Contains(await Errors(_maker, Wear("hat", key)), e => e.Contains("unknown"));
        Assert.Empty(await Errors(_buyer, Wear("hat", key))); Assert.Single(await AvatarWearables.ListAsync(_db, _buyer, default));
        Assert.Null((await _db.AvatarWearables.AsNoTracking().SingleAsync()).DeletedAt); // somebody still holds it
        Assert.True((await AvatarWearables.DeleteAsync(_db, _buyer, id, _now, default)).Ok);
        Assert.NotNull((await _db.AvatarWearables.AsNoTracking().SingleAsync()).DeletedAt); // nobody does now
        Assert.Equal("not_found", (await AvatarWearables.DeleteAsync(_db, _buyer, id, _now, default)).Error);
    }

    [Fact]
    public async Task AnItemGivenAwayOrSoldIsTakenOffTheAvatarThatWoreIt()
    {
        var (id, key) = await Make(_maker, WearableTemplate.Clothing("top", ["spine", "chest", "hips"], WearableTemplate.Extras("top", covers: ["bottom"])));
        var avatar = await _db.Avatars.AsNoTracking().SingleAsync(a => a.UserId == _maker);
        var top = await _db.EquippedItems.SingleAsync(e => e.AvatarId == avatar.Id && e.Slot == "top"); top.ItemKey = key;
        (await _db.EquippedItems.SingleAsync(e => e.AvatarId == avatar.Id && e.Slot == "bottom")).ItemKey = "covered"; await _db.SaveChangesAsync();
        Assert.False(await AvatarWearables.ReconcileAsync(_db, avatar.Id, default)); // still held: nothing changes
        // the item leaves the inventory without the avatar being asked (a transfer, an admin, a refund)
        foreach (var held in await _db.InventoryItems.Where(i => i.OwnerUserId == _maker).ToListAsync()) held.DeletedAt = _now; await _db.SaveChangesAsync();
        Assert.True(await AvatarWearables.ReconcileAsync(_db, avatar.Id, default));
        var worn = await _db.EquippedItems.AsNoTracking().Where(e => e.AvatarId == avatar.Id).ToDictionaryAsync(e => e.Slot, e => e.ItemKey);
        Assert.Equal("tee_basic", worn["top"]); Assert.Equal("pants_basic", worn["bottom"]); Assert.Empty(AvatarRules.CheckEquipment([.. worn.Select(w => (w.Key, w.Value))]));
        Assert.False(await AvatarWearables.ReconcileAsync(_db, avatar.Id, default)); // and it does not keep changing
    }

    [Fact]
    public async Task ASlotPieceThatIsNoLongerHeldIsRemoved()
    {
        var (_, key) = await Make(_maker, Hat());
        var avatar = await _db.Avatars.AsNoTracking().SingleAsync(a => a.UserId == _maker);
        _db.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = "hat", ItemKey = key, TintColor = "#FFFFFF", EquippedAt = _now }); await _db.SaveChangesAsync();
        (await _db.InventoryItems.SingleAsync()).DeletedAt = _now; await _db.SaveChangesAsync();
        await AvatarWearables.ReconcileForUserAsync(_db, _maker, default);
        Assert.DoesNotContain(_db.EquippedItems.AsNoTracking(), e => e.Slot == "hat");
    }

    [Fact]
    public async Task UploadingAgainAfterDeletingBringsTheSameAssetBack()
    {
        var (id, _) = await Make(_maker, Hat()); var assetBefore = await AssetOf(id);
        await AvatarWearables.DeleteAsync(_db, _maker, id, _now, default);
        var (again, _) = await Make(_maker, Hat());
        Assert.Equal(id, again); Assert.Equal(assetBefore, await AssetOf(again)); Assert.Single(_db.Assets);
        var held = await _db.InventoryItems.AsNoTracking().SingleAsync(); Assert.Null(held.DeletedAt); Assert.Single(await AvatarWearables.ListAsync(_db, _maker, default));
    }

    // ---- items made before the inventory was involved ---------------------------------------------------------------------------------

    [Fact]
    public async Task OldItemsGetTheirAssetTheFirstTimeTheyAreLooked_At()
    {
        var (id, key) = await Make(_maker, Hat());
        // turn it back into the way phase 3 stored it: no asset at all
        _db.InventoryItems.RemoveRange(_db.InventoryItems); _db.AssetVersions.RemoveRange(_db.AssetVersions); _db.Assets.RemoveRange(_db.Assets);
        (await _db.AvatarWearables.SingleAsync()).AssetId = null; await _db.SaveChangesAsync();
        Assert.Contains(await Errors(_maker, Wear("hat", key)), e => e.Contains("unknown")); // nothing in the inventory yet
        var listed = await AvatarWearables.ListAsync(_db, _maker, default);
        Assert.Equal(id, Assert.Single(listed).Id); Assert.Single(_db.Assets); Assert.Single(_db.InventoryItems);
        Assert.Empty(await Errors(_maker, Wear("hat", key)));
        await AvatarWearables.EnsureAssetsAsync(_db, _maker, default); Assert.Single(_db.Assets); // and only once
    }

    [Fact]
    public async Task WhatOthersAreToldIsNotLimitedToTheMaker()
    {
        var (id, key) = await Make(_maker, Hat()); await Sell(await AssetOf(id), _buyer);
        var avatar = await _db.Avatars.Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _buyer);
        _db.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = "hat", ItemKey = key, TintColor = "#FFFFFF", EquippedAt = _now }); await _db.SaveChangesAsync();
        avatar = await _db.Avatars.AsNoTracking().Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _buyer);
        var worn = JsonDocument.Parse(await AvatarWearables.WornInfoAsync(_db, avatar, default)).RootElement.EnumerateArray().Single();
        Assert.Equal(id, worn.GetProperty("id").GetGuid()); Assert.Equal(64, worn.GetProperty("hash").GetString()!.Length);
    }
}
