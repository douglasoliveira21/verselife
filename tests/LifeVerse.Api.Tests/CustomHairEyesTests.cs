using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Avatar 2.0: a person's own hair and eyes, as rigid pieces on the head that replace the ones of the catalog.</summary>
public sealed class CustomHairEyesTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly AssetStorageService _storage;
    private readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _other = Guid.NewGuid();

    public CustomHairEyesTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        foreach (var user in new[] { _me, _other })
        {
            var avatar = new Avatar { Id = Guid.NewGuid(), UserId = user, DisplayName = "Pessoa", BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now };
            avatar.Appearance = new AvatarAppearance { Id = Guid.NewGuid(), AvatarId = avatar.Id, SkinColor = "#C98F65", HairColor = "#2B1B14", HairStyle = "short", CreatedAt = _now, UpdatedAt = _now };
            foreach (var (slot, key) in new[] { ("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") }) avatar.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = slot, ItemKey = key, TintColor = "#FFFFFF" });
            _db.Avatars.Add(avatar);
        }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static byte[] Hair(string? extras = null, float size = .3f) => WearableTemplate.Attachment(extras ?? WearableTemplate.Extras("hair", bone: "head", offset: [0, .12f, 0], tint: "hair"), size);
    private static byte[] Eyes(string? extras = null, float size = .05f) => WearableTemplate.Attachment(extras ?? WearableTemplate.Extras("eyes", bone: "head", offset: [0, .06f, .09f], tint: "eyes"), size);
    private static WearableReport Check(byte[] glb) => WearableValidator.Validate(glb);
    private static string[] Codes(WearableReport report) => [.. report.Errors.Select(e => e.Code).Distinct()];

    // ---- the rules -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void HairAndEyesAreSlotsOfTheAvatar()
    {
        Assert.Contains("hair", AvatarRules.Slots); Assert.Contains("eyes", AvatarRules.Slots);
        Assert.Contains(AvatarRules.ItemsFor("hair"), i => i.Key == "none"); Assert.Contains(AvatarRules.ItemsFor("eyes"), i => i.Key == "none");
        Assert.True(WearableValidator.IsFace("hair")); Assert.True(WearableValidator.IsFace("eyes")); Assert.False(WearableValidator.IsFace("hat"));
        Assert.DoesNotContain("hair", AvatarRules.RequiredSlots);
    }

    [Fact]
    public void APieceOfHairIsAcceptedAndAlwaysReplacesTheCatalogHair()
    {
        var report = Check(Hair()); Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.Equal(WearableKind.Attachment, report.Kind); Assert.Equal("hair", report.Slot); Assert.Equal("head", report.AttachBone); Assert.Equal("hair", report.Tint);
        Assert.True(report.HidesHair); // even though the file did not say so
        var noTint = Check(Hair(WearableTemplate.Extras("hair"))); Assert.True(noTint.Ok); Assert.Equal(string.Empty, noTint.Tint); Assert.Equal("head", noTint.AttachBone); // the head is the default bone
    }

    [Fact]
    public void ApairOfEyesIsAcceptedAndSmall()
    {
        var report = Check(Eyes()); Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.Equal("eyes", report.Slot); Assert.Equal("eyes", report.Tint); Assert.False(report.HidesHair);
        Assert.Contains("too_large", Codes(Check(Eyes(size: .5f)))); // eyes larger than 0.4 m are not eyes
        Assert.True(Check(Hair(size: .9f)).Ok); // hair may be big
        Assert.Contains("too_large", Codes(Check(Hair(size: 1.6f))));
    }

    [Theory]
    [InlineData("hair", "neck")] [InlineData("eyes", "chest")] [InlineData("hair", "hand_l")]
    public void HairAndEyesGoOnTheHeadOnly(string slot, string bone) => Assert.Contains("wrong_bone", Codes(Check(WearableTemplate.Attachment(WearableTemplate.Extras(slot, bone: bone), .2f))));

    [Theory]
    [InlineData("hat", "hair")] [InlineData("hair", "eyes")] [InlineData("eyes", "hair")] [InlineData("hair", "skin")] [InlineData("glasses", "eyes")]
    public void ATintMustBeThatOfItsOwnSlot(string slot, string tint) => Assert.Contains("bad_tint", Codes(Check(WearableTemplate.Attachment(WearableTemplate.Extras(slot, tint: tint), .1f))));

    [Fact]
    public void HairAndEyesAreNotRiggedClothing() => Assert.Contains("slot_kind", Codes(Check(WearableTemplate.Clothing("hair", WearableTemplate.ShirtBones, WearableTemplate.Extras("hair")))));

    [Fact]
    public void CatalogEyesAreHiddenOnlyByAnOwnPieceThatIsDrawn()
    {
        var piece = new WearableItem("eyes", AvatarRules.CustomKey(Guid.NewGuid()), "Meus olhos");
        (string, string)[] worn = [("top", "tee_basic"), ("eyes", piece.Key)];
        Assert.False(AvatarRules.EyesVisible(worn, (_, key) => key == piece.Key ? piece : null)); // own eyes replace the catalog ones
        Assert.True(AvatarRules.EyesVisible(worn)); // far away, where own pieces are not drawn, the catalog eyes stay
        Assert.True(AvatarRules.EyesVisible([("top", "tee_basic"), ("eyes", "none")]));
        var hair = new WearableItem("hair", AvatarRules.CustomKey(Guid.NewGuid()), "Meu cabelo", HidesHair: true);
        Assert.False(AvatarRules.HairVisible([("hair", hair.Key)], (_, key) => key == hair.Key ? hair : null)); Assert.True(AvatarRules.HairVisible([("hair", hair.Key)]));
    }

    // ---- the server ---------------------------------------------------------------------------------------------------------------------

    private async Task<Guid> Own(Guid user, byte[] bytes)
    {
        var result = await AvatarWearables.UploadAsync(_db, _storage, user, "item.glb", bytes, null, _now, default); Assert.True(result.Ok, result.Error);
        return JsonSerializer.SerializeToElement(result.Data, Web).GetProperty("item").GetProperty("id").GetGuid();
    }

    private async Task<Avatar> AvatarOf(Guid user) => await _db.Avatars.AsNoTracking().Include(a => a.Appearance).Include(a => a.EquippedItems).SingleAsync(a => a.UserId == user);
    private static SaveAvatarRequest Request(params (string Slot, string Key)[] items) => new("Pessoa", "human_a", "#C98F65", "#2B1B14", "short", .003f, false, [.. items.Select(i => new EquippedItemRequest(i.Slot, i.Key, "#FFFFFF"))], "#4A3326", "none", "{}", "{}", "[]", "[]");
    private static (string, string)[] Base => [("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic")];

    private async Task<List<string>> Errors(Guid user, SaveAvatarRequest request)
    {
        var avatar = await AvatarOf(user);
        var errors = AvatarCatalog.Validate(request, await AvatarWearables.ResolverAsync(_db, avatar.Id, default));
        return errors.Count > 0 ? [.. errors.SelectMany(e => e.Value)] : await AvatarWearables.CheckOutfitAsync(_db, avatar, request.EquippedItems, default);
    }

    [Fact]
    public async Task OwnHairAndEyesAreStoredAndListedWithTheirTint()
    {
        var hair = await Own(_me, Hair()); var eyes = await Own(_me, Eyes());
        var list = await AvatarWearables.ListAsync(_db, _me, default);
        Assert.Equal("hair", list.Single(w => w.Id == hair).Tint); Assert.Equal("eyes", list.Single(w => w.Id == eyes).Tint); Assert.True(list.Single(w => w.Id == hair).HidesHair); Assert.False(list.Single(w => w.Id == eyes).HidesHair);
        Assert.Equal("head", list.Single(w => w.Id == hair).AttachBone);
    }

    [Fact]
    public async Task OnlyYourOwnHairAndEyesInTheirOwnSlotCanBeWorn()
    {
        var hair = await Own(_me, Hair()); var eyes = await Own(_me, Eyes()); var theirs = await Own(_other, Hair(WearableTemplate.Extras("hair", offset: [0, .2f, 0])));
        Assert.Empty(await Errors(_me, Request([.. Base, ("hair", AvatarRules.CustomKey(hair)), ("eyes", AvatarRules.CustomKey(eyes))])));
        Assert.NotEmpty(await Errors(_me, Request([.. Base, ("hair", AvatarRules.CustomKey(theirs))]))); // somebody else's
        Assert.NotEmpty(await Errors(_me, Request([.. Base, ("eyes", AvatarRules.CustomKey(hair))]))); // hair in the eyes slot
        Assert.NotEmpty(await Errors(_me, Request([.. Base, ("hair", "tee_basic")]))); // a catalog item that is not of the slot
        Assert.Empty(await Errors(_me, Request([.. Base, ("hair", "none"), ("eyes", "none")])));
    }

    [Fact]
    public async Task HairAndEyesGoWithAHatAndCountInTheBudget()
    {
        var hair = await Own(_me, Hair()); var hat = await Own(_me, WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head", offset: [0, .3f, 0])));
        Assert.Empty(await Errors(_me, Request([.. Base, ("hair", AvatarRules.CustomKey(hair)), ("hat", AvatarRules.CustomKey(hat))]))); // a hat over own hair is fine
    }

    [Fact]
    public async Task WhatIsToldToOthersCarriesTheTintAndTheHairFlag()
    {
        var hair = await Own(_me, Hair()); var eyes = await Own(_me, Eyes());
        var request = Request([.. Base, ("hair", AvatarRules.CustomKey(hair)), ("eyes", AvatarRules.CustomKey(eyes))]);
        var avatar = await _db.Avatars.Include(a => a.Appearance).Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _me);
        UserEndpoints.ApplyAvatar(_db, avatar, request, _now); await _db.SaveChangesAsync();
        var worn = JsonSerializer.Deserialize<JsonElement>(await AvatarWearables.WornInfoAsync(_db, await AvatarOf(_me), default)).EnumerateArray().ToList();
        var h = worn.Single(w => w.GetProperty("slot").GetString() == "hair"); var e = worn.Single(w => w.GetProperty("slot").GetString() == "eyes");
        Assert.Equal("hair", h.GetProperty("tint").GetString()); Assert.True(h.GetProperty("hidesHair").GetBoolean()); Assert.Equal("head", h.GetProperty("attachBone").GetString());
        Assert.Equal("eyes", e.GetProperty("tint").GetString()); Assert.False(e.GetProperty("hidesHair").GetBoolean());
    }

    [Fact]
    public async Task ALookKeepsOwnHairAndEyes()
    {
        var hair = await Own(_me, Hair()); var eyes = await Own(_me, Eyes());
        var avatar = await _db.Avatars.Include(a => a.Appearance).Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _me);
        UserEndpoints.ApplyAvatar(_db, avatar, Request([.. Base, ("hair", AvatarRules.CustomKey(hair)), ("eyes", AvatarRules.CustomKey(eyes))]), _now); await _db.SaveChangesAsync();
        var saved = await AvatarLooks.SaveCurrentAsync(_db, _me, "Com meus olhos", _now, default); Assert.True(saved.Ok, saved.Error);
        UserEndpoints.ApplyAvatar(_db, await _db.Avatars.Include(a => a.Appearance).Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _me), Request(Base), _now); await _db.SaveChangesAsync();
        Assert.DoesNotContain((await AvatarOf(_me)).EquippedItems, i => i.Slot is "hair" or "eyes");
        var applied = await AvatarLooks.ApplyAsync(_db, _me, ((LookSummary)saved.Data!).Id, _now, default); Assert.True(applied.Ok, applied.Error);
        var items = (await AvatarOf(_me)).EquippedItems.ToDictionary(i => i.Slot, i => i.ItemKey);
        Assert.Equal(AvatarRules.CustomKey(hair), items["hair"]); Assert.Equal(AvatarRules.CustomKey(eyes), items["eyes"]);
    }

    [Fact]
    public async Task DeletingOwnHairTakesItOffTheAvatar()
    {
        var hair = await Own(_me, Hair());
        var avatar = await _db.Avatars.Include(a => a.Appearance).Include(a => a.EquippedItems).SingleAsync(a => a.UserId == _me);
        UserEndpoints.ApplyAvatar(_db, avatar, Request([.. Base, ("hair", AvatarRules.CustomKey(hair))]), _now); await _db.SaveChangesAsync();
        var deleted = await AvatarWearables.DeleteAsync(_db, _me, hair, _now, default); Assert.True(deleted.Ok, deleted.Error);
        Assert.DoesNotContain((await AvatarOf(_me)).EquippedItems, i => i.Slot == "hair");
    }
}
