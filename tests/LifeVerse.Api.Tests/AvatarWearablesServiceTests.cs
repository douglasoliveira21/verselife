using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Custom clothes and accessories on the server: upload and check, wearing them only when they are yours and fit, and what is told to others.</summary>
public sealed class AvatarWearablesServiceTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly MemoryStore _store = new();
    private readonly AssetStorageService _storage;
    private readonly DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _other = Guid.NewGuid();

    public AvatarWearablesServiceTests()
    {
        _storage = new AssetStorageService(_store, NullLogger<AssetStorageService>.Instance);
        foreach (var user in new[] { _me, _other })
        {
            var avatar = new Avatar { Id = Guid.NewGuid(), UserId = user, DisplayName = "Pessoa", BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now };
            foreach (var (slot, key) in new[] { ("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") }) _db.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = slot, ItemKey = key, TintColor = "#FFFFFF", EquippedAt = _now });
            _db.Avatars.Add(avatar);
        }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Task<ModelResult> Upload(Guid user, byte[] bytes, string file = "item.glb", string? name = null) => AvatarWearables.UploadAsync(_db, _storage, user, file, bytes, name, _now, default);
    private static byte[] Hat(string slot = "hat", string? bone = null) => WearableTemplate.Attachment(WearableTemplate.Extras(slot, bone: bone, offset: [0, .1f, 0]));
    private static byte[] Shirt(string extras = "") => WearableTemplate.Clothing("top", WearableTemplate.ShirtBones, extras.Length == 0 ? null : extras);
    private async Task<Guid> Own(Guid user, byte[] bytes) { var result = await Upload(user, bytes); Assert.True(result.Ok, result.Error); return JsonSerializer.SerializeToElement(result.Data, Web).GetProperty("item").GetProperty("id").GetGuid(); }
    private async Task<Avatar> AvatarOf(Guid user) => await _db.Avatars.AsNoTracking().Include(a => a.Appearance).Include(a => a.EquippedItems).SingleAsync(a => a.UserId == user);

    private static SaveAvatarRequest Request(params (string Slot, string Key)[] items) => new("Pessoa", "human_a", "#C98F65", "#2B1B14", "short", .003f, false, [.. items.Select(i => new EquippedItemRequest(i.Slot, i.Key, "#FFFFFF"))], "#4A3326", "none", "{}", "{}", "[]", "[]");
    private static (string, string)[] Base => [("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic")];

    private async Task<List<string>> Errors(Guid user, SaveAvatarRequest request)
    {
        var avatar = await AvatarOf(user);
        var errors = AvatarCatalog.Validate(request, await AvatarWearables.ResolverAsync(_db, avatar.Id, default));
        if (errors.Count > 0) return [.. errors.SelectMany(e => e.Value)];
        return await AvatarWearables.CheckOutfitAsync(_db, avatar, request.EquippedItems, default);
    }

    // ---- storing --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AGoodItemIsStoredWithItsSlotKindAndReport()
    {
        var bytes = Hat(bone: "head"); var id = await Own(_me, bytes);
        var item = await _db.AvatarWearables.SingleAsync();
        Assert.Equal(id, item.Id); Assert.Equal("hat", item.Slot); Assert.Equal("attachment", item.Kind); Assert.Equal(12, item.Triangles); Assert.Equal(bytes, _store.Files[item.StorageKey]);
        var list = await AvatarWearables.ListAsync(_db, _me, default); Assert.Equal(AvatarRules.CustomKey(id), list.Single().Key);
        Assert.Empty(await AvatarWearables.ListAsync(_db, _other, default));
        Assert.Equal("clothing", JsonSerializer.SerializeToElement((await Upload(_me, Shirt())).Data, Web).GetProperty("item").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ARefusedItemIsNotStoredAndComesBackWithItsReport()
    {
        var result = await Upload(_me, HumanoidTemplate.Build()); // no extras
        Assert.False(result.Ok); Assert.Equal("invalid_wearable", result.Error);
        Assert.Contains(JsonSerializer.SerializeToElement(result.Data, Web).GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "no_metadata");
        Assert.Empty(_store.Files); Assert.Empty(_db.AvatarWearables);
        Assert.Equal("bad_file", (await Upload(_me, Hat(), "x.png")).Error); Assert.Equal("not_found", (await Upload(Guid.NewGuid(), Hat())).Error);
    }

    [Fact]
    public async Task TheSameFileTwiceIsOneItem_AndThereIsALimit()
    {
        await Own(_me, Hat()); await Own(_me, Hat()); Assert.Single(_db.AvatarWearables);
        for (var i = 1; i < AvatarWearables.MaxWearables; i++) Assert.True((await Upload(_me, WearableTemplate.Attachment(WearableTemplate.Extras("hat", offset: [0, (i + 20) / 100f, 0])))).Ok);
        var last = await Upload(_me, WearableTemplate.Attachment(WearableTemplate.Extras("hat", offset: [0, 1.5f, 0]))); Assert.True(last.Error == "too_many_items", $"count={_db.AvatarWearables.Count()} err={last.Error} ok={last.Ok}");
        Assert.True((await Upload(_other, Hat())).Ok);
    }

    // ---- wearing ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyYourOwnItemsInTheirOwnSlotCanBeWorn()
    {
        var mine = await Own(_me, Hat()); var theirs = await Own(_other, Hat(bone: "head"));
        Assert.Empty(await Errors(_me, Request([.. Base, ("hat", AvatarRules.CustomKey(mine))])));
        Assert.NotEmpty(await Errors(_me, Request([.. Base, ("hat", AvatarRules.CustomKey(theirs))]))); // somebody else's
        Assert.NotEmpty(await Errors(_me, Request([.. Base, ("glasses", AvatarRules.CustomKey(mine))]))); // a hat in the glasses slot
        Assert.NotEmpty(await Errors(_me, Request([.. Base, ("hat", AvatarRules.CustomKey(Guid.NewGuid()))]))); // does not exist
        Assert.NotEmpty(await Errors(_me, Request([.. Base, ("hat", "custom:not-a-guid")])));
    }

    [Fact]
    public async Task RiggedClothingNeedsACustomBody()
    {
        var shirt = await Own(_me, Shirt());
        var request = Request(("top", AvatarRules.CustomKey(shirt)), ("bottom", "pants_basic"), ("shoes", "sneakers_basic"));
        Assert.Contains(await Errors(_me, request), e => e.Contains("custom body"));
        (await _db.Avatars.SingleAsync(a => a.UserId == _me)).CustomModelId = Guid.NewGuid(); await _db.SaveChangesAsync();
        Assert.Empty(await Errors(_me, request));
    }

    [Fact]
    public async Task ACustomDressFollowsTheCoverRules()
    {
        (await _db.Avatars.SingleAsync(a => a.UserId == _me)).CustomModelId = Guid.NewGuid(); await _db.SaveChangesAsync();
        var dress = await Own(_me, WearableTemplate.Clothing("top", ["spine", "chest", "hips", "upper_leg_l", "upper_leg_r"], WearableTemplate.Extras("top", covers: ["bottom"])));
        Assert.Contains(await Errors(_me, Request(("top", AvatarRules.CustomKey(dress)), ("bottom", "pants_basic"), ("shoes", "sneakers_basic"))), e => e.Contains("covered"));
        Assert.Empty(await Errors(_me, Request(("top", AvatarRules.CustomKey(dress)), ("bottom", "covered"), ("shoes", "sneakers_basic"))));
    }

    [Fact]
    public async Task AnOutfitHasATriangleBudget()
    {
        (await _db.Avatars.SingleAsync(a => a.UserId == _me)).CustomModelId = Guid.NewGuid(); await _db.SaveChangesAsync();
        var top = await Own(_me, WearableTemplate.Clothing("top", WearableTemplate.ShirtBones, WearableTemplate.Extras("top"), new TemplateOptions { Copies = 250 }));
        var jacket = await Own(_me, WearableTemplate.Clothing("jacket", WearableTemplate.ShirtBones, WearableTemplate.Extras("jacket"), new TemplateOptions { Copies = 250 }));
        var bottom = await Own(_me, WearableTemplate.Clothing("bottom", WearableTemplate.TrouserBones, WearableTemplate.Extras("bottom"), new TemplateOptions { Copies = 250 }));
        var shoes = await Own(_me, WearableTemplate.Clothing("shoes", ["foot_l", "foot_r"], WearableTemplate.Extras("shoes"), new TemplateOptions { Copies = 750 }));
        Assert.Empty(await Errors(_me, Request(("top", AvatarRules.CustomKey(top)), ("bottom", AvatarRules.CustomKey(bottom)), ("shoes", "sneakers_basic")))); // 36k
        Assert.Contains(await Errors(_me, Request(("top", AvatarRules.CustomKey(top)), ("jacket", AvatarRules.CustomKey(jacket)), ("bottom", AvatarRules.CustomKey(bottom)), ("shoes", AvatarRules.CustomKey(shoes)))), e => e.Contains("triangles"));
    }

    // ---- deleting and telling others -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DeletingAnItemTakesItOffTheAvatar()
    {
        var hat = await Own(_me, Hat());
        var avatar = await _db.Avatars.Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _me);
        _db.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = "hat", ItemKey = AvatarRules.CustomKey(hat), TintColor = "#FFFFFF", EquippedAt = _now });
        var top = avatar.EquippedItems.Single(e => e.Slot == "top"); top.ItemKey = AvatarRules.CustomKey(hat); // pretend a required slot holds it too
        await _db.SaveChangesAsync();
        Assert.Equal("not_found", (await AvatarWearables.DeleteAsync(_db, _other, hat, _now, default)).Error);
        Assert.True((await AvatarWearables.DeleteAsync(_db, _me, hat, _now, default)).Ok);
        var after = await AvatarOf(_me);
        Assert.DoesNotContain(after.EquippedItems, e => e.ItemKey.StartsWith("custom:")); Assert.DoesNotContain(after.EquippedItems, e => e.Slot == "hat");
        Assert.Equal("tee_basic", after.EquippedItems.Single(e => e.Slot == "top").ItemKey); // a required slot goes back to a plain choice
        Assert.Empty(await AvatarWearables.ListAsync(_db, _me, default));
        Assert.Equal("not_found", (await AvatarWearables.DeleteAsync(_db, _me, hat, _now, default)).Error);
        Assert.NotEmpty(_store.Files); // the file stays (it is stored by hash and may be shared by a re-upload)
        Assert.True((await Upload(_me, Hat())).Ok);
    }

    [Fact]
    public async Task DeletingADressUncoversTheLegs()
    {
        var dress = await Own(_me, WearableTemplate.Clothing("top", ["spine", "chest", "hips"], WearableTemplate.Extras("top", covers: ["bottom"])));
        var avatar = await _db.Avatars.Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _me);
        avatar.EquippedItems.Single(e => e.Slot == "top").ItemKey = AvatarRules.CustomKey(dress); avatar.EquippedItems.Single(e => e.Slot == "bottom").ItemKey = "covered"; await _db.SaveChangesAsync();
        await AvatarWearables.DeleteAsync(_db, _me, dress, _now, default);
        var after = await AvatarOf(_me); Assert.Equal("pants_basic", after.EquippedItems.Single(e => e.Slot == "bottom").ItemKey); Assert.Empty(AvatarRules.CheckEquipment([.. after.EquippedItems.Select(e => (e.Slot, e.ItemKey))]));
    }

    [Fact]
    public async Task OthersAreToldWhatToDownloadAndHowToPlaceIt()
    {
        var hat = await Own(_me, Hat(bone: "head"));
        var avatar = await _db.Avatars.Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _me);
        _db.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = "hat", ItemKey = AvatarRules.CustomKey(hat), TintColor = "#FFFFFF", EquippedAt = _now }); await _db.SaveChangesAsync();
        var worn = JsonDocument.Parse(await AvatarWearables.WornInfoAsync(_db, await AvatarOf(_me), default)).RootElement.EnumerateArray().Single();
        Assert.Equal(hat, worn.GetProperty("id").GetGuid()); Assert.Equal(64, worn.GetProperty("hash").GetString()!.Length); Assert.Equal("attachment", worn.GetProperty("kind").GetString());
        Assert.Equal("head", worn.GetProperty("attachBone").GetString()); Assert.Equal(.1f, worn.GetProperty("offset")[1].GetSingle(), 3); Assert.Equal("hat", worn.GetProperty("slot").GetString());
        Assert.Equal("[]", await AvatarWearables.WornInfoAsync(_db, await AvatarOf(_other), default));
    }

    [Fact]
    public async Task ALookWithSomeoneElsesItemIsNotApplied()
    {
        var theirs = await Own(_other, Hat());
        var mine = await Own(_me, Hat(bone: "head"));
        var avatar = await _db.Avatars.Include(a => a.EquippedItems).Include(a => a.Appearance).SingleAsync(a => a.UserId == _me);
        _db.AvatarAppearances.Add(new AvatarAppearance { Id = Guid.NewGuid(), AvatarId = avatar.Id, SkinColor = "#C98F65", HairColor = "#2B1B14", HairStyle = "short", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync(); avatar = await _db.Avatars.Include(a => a.EquippedItems).Include(a => a.Appearance).SingleAsync(a => a.UserId == _me);
        _db.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = "hat", ItemKey = AvatarRules.CustomKey(mine), TintColor = "#FFFFFF", EquippedAt = _now }); await _db.SaveChangesAsync();
        var own = (LookSummary)(await AvatarLooks.SaveCurrentAsync(_db, _me, "Com chapéu", _now, default)).Data!;
        Assert.True((await AvatarLooks.ApplyAsync(_db, _me, own.Id, _now, default)).Ok);
        var look = await _db.AvatarLooks.SingleAsync(); look.SnapshotJson = look.SnapshotJson.Replace(AvatarRules.CustomKey(mine), AvatarRules.CustomKey(theirs)); await _db.SaveChangesAsync();
        Assert.Equal("look_invalid", (await AvatarLooks.ApplyAsync(_db, _me, own.Id, _now, default)).Error);
    }
}
