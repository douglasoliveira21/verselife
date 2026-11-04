using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Avatar 2.0, first part: body and face sliders, what can be worn together, and saved looks (save, put on, duplicate, share, import).</summary>
public sealed class AvatarLooksTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _friend = Guid.NewGuid();

    public AvatarLooksTests() { Seed(_me, "Marina Costa"); Seed(_friend, "Rafa Lima"); }
    public void Dispose() => _db.Dispose();

    private void Seed(Guid user, string name)
    {
        var avatar = new Avatar { Id = Guid.NewGuid(), UserId = user, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now };
        avatar.Appearance = new AvatarAppearance { Id = Guid.NewGuid(), AvatarId = avatar.Id, SkinColor = "#C98F65", HairColor = "#2B1B14", HairStyle = "short", CreatedAt = _now, UpdatedAt = _now };
        foreach (var (slot, key) in new[] { ("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") }) avatar.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = slot, ItemKey = key, TintColor = "#FFFFFF", EquippedAt = _now });
        _db.Avatars.Add(avatar); _db.SaveChanges();
    }

    private Avatar Mine(Guid? user = null) { _db.ChangeTracker.Clear(); return _db.Avatars.Include(a => a.Appearance).Include(a => a.EquippedItems).Single(a => a.UserId == (user ?? _me)); }

    private static SaveAvatarRequest Request(Avatar avatar, string hair = "short", string skin = "#C98F65", string body = "{}", string face = "{}", params (string Slot, string Key)[] items)
    {
        var list = items.Length > 0 ? items : [("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic")];
        return new SaveAvatarRequest(avatar.DisplayName, "human_a", skin, "#2B1B14", hair, .003f, false, [.. list.Select(i => new EquippedItemRequest(i.Slot, i.Key, "#FFFFFF"))], "#4A3326", "none", body, face, "[]", "[]");
    }

    private async Task Wear(SaveAvatarRequest request)
    {
        var avatar = Mine(); Assert.Empty(AvatarCatalog.Validate(request));
        UserEndpoints.ApplyAvatar(_db, avatar, request, _now); await _db.SaveChangesAsync();
    }

    // ---- sliders ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("{}", null)]
    [InlineData("{\"height\":1.1,\"legs\":0.9,\"head\":1}", null)]
    [InlineData("{\"height\":1.5}", "out_of_range:height")]
    [InlineData("{\"height\":\"tall\"}", "not_a_number:height")]
    [InlineData("{\"wings\":1}", "unknown_setting:wings")]
    [InlineData("[1]", "settings_not_object")]
    [InlineData("nope", "settings_invalid_json")]
    public void BodySettings_AreCheckedStrictly(string json, string? expected) => Assert.Equal(expected, AvatarRules.CheckSettings(json, AvatarRules.BodyRanges));

    [Fact]
    public void FaceSettings_AreCheckedStrictly()
    {
        Assert.Null(AvatarRules.CheckSettings("{\"eyeScale\":1.2,\"noseSize\":0.8,\"jawWidth\":1.1}", AvatarRules.FaceRanges));
        Assert.Equal("out_of_range:mouthWidth", AvatarRules.CheckSettings("{\"mouthWidth\":2}", AvatarRules.FaceRanges));
        Assert.Equal("unknown_setting:height", AvatarRules.CheckSettings("{\"height\":1}", AvatarRules.FaceRanges));
    }

    [Fact]
    public void ReadingIsLenientAndWritingIsClean()
    {
        var read = AvatarRules.ReadSettings("{\"height\":9,\"legs\":\"x\",\"wings\":3,\"torso\":0.95}", AvatarRules.BodyRanges);
        Assert.Equal(1.15f, read["height"]); Assert.Equal(1f, read["legs"]); Assert.Equal(.95f, read["torso"]); Assert.False(read.ContainsKey("wings"));
        Assert.Equal(AvatarRules.BodyRanges.Count, read.Count);
        Assert.Equal("{\"height\":1.15,\"torso\":0.95}", AvatarRules.WriteSettings(read, AvatarRules.BodyRanges)); // defaults are left out, names are sorted
        Assert.Equal("{}", AvatarRules.WriteSettings(AvatarRules.ReadSettings("garbage", AvatarRules.BodyRanges), AvatarRules.BodyRanges));
        Assert.Equal(AvatarRules.WriteSettings(read, AvatarRules.BodyRanges), AvatarRules.WriteSettings(AvatarRules.ReadSettings(AvatarRules.WriteSettings(read, AvatarRules.BodyRanges), AvatarRules.BodyRanges), AvatarRules.BodyRanges));
    }

    [Fact]
    public void TheApiRefusesBadSliders()
    {
        var avatar = Mine();
        Assert.Contains("bodySettingsJson", AvatarCatalog.Validate(Request(avatar, body: "{\"height\":3}")));
        Assert.Contains("faceSettingsJson", AvatarCatalog.Validate(Request(avatar, face: "{\"eyeScale\":0}")));
        Assert.Empty(AvatarCatalog.Validate(Request(avatar, body: "{\"height\":1.1,\"arms\":0.9}", face: "{\"eyeSpacing\":1.1}")));
        Assert.Empty(AvatarCatalog.Validate(Request(avatar, face: "{\"eyeScale\":1}"))); // what the older client always sent
    }

    // ---- what can be worn together --------------------------------------------------------------------------------------

    [Fact]
    public void EveryCatalogItemHasItsOwnPlaceAndTheDefaultOutfitIsValid()
    {
        Assert.Equal(AvatarRules.Catalog.Count, AvatarRules.Catalog.Select(i => (i.Slot, i.Key)).Distinct().Count());
        Assert.All(AvatarRules.Catalog, i => Assert.Contains(i.Slot, AvatarRules.Slots));
        Assert.Empty(AvatarRules.CheckEquipment([("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic")]));
    }

    [Fact]
    public void ADressCoversTheLegs()
    {
        var dress = new[] { ("top", "dress_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") };
        Assert.Contains(AvatarRules.CheckEquipment(dress), p => p.Code == "slot_covered" && p.Slot == "bottom");
        Assert.Empty(AvatarRules.CheckEquipment([("top", "dress_basic"), ("bottom", "covered"), ("shoes", "sneakers_basic")]));
        Assert.Contains(AvatarRules.CheckEquipment([("top", "tee_basic"), ("bottom", "covered"), ("shoes", "sneakers_basic")]), p => p.Code == "not_covered");
    }

    [Fact]
    public void AnOuterLayerNeedsSomethingUnderIt_AndNothingElseIsAllowed()
    {
        Assert.Empty(AvatarRules.CheckEquipment([("top", "tee_basic"), ("jacket", "coat_long"), ("bottom", "pants_basic"), ("shoes", "boots_basic")]));
        Assert.Contains(AvatarRules.CheckEquipment([("jacket", "coat_long"), ("top", "tee_basic"), ("top", "jacket_basic"), ("bottom", "pants_basic"), ("shoes", "boots_basic")]), p => p.Code == "duplicate_slot");
        Assert.Contains(AvatarRules.CheckEquipment([("top", "tee_basic"), ("bottom", "pants_basic")]), p => p is { Code: "missing_required", Slot: "shoes" });
        Assert.Contains(AvatarRules.CheckEquipment([("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "boots_basic"), ("hat", "glasses_round")]), p => p.Code == "unknown_item");
        Assert.Contains(AvatarRules.CheckEquipment([("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "boots_basic"), ("wings", "angel")]), p => p.Code == "unknown_item");
    }

    [Fact]
    public void ResolveGivesTheNewestChoiceTheWayAndFillsTheRest()
    {
        var worn = new List<(string, string)> { ("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") };
        var dress = AvatarRules.Resolve(worn.Select(w => w.Item1 == "top" ? ("top", "dress_basic") : w), changed: "top").ToDictionary(x => x.Slot, x => x.Key);
        Assert.Equal("covered", dress["bottom"]); Assert.Equal("dress_basic", dress["top"]);
        Assert.Empty(AvatarRules.CheckEquipment([.. dress.Select(x => (x.Key, x.Value))]));
        // choosing trousers while wearing a dress takes the dress off
        var back = AvatarRules.Resolve(dress.Select(x => x.Key == "bottom" ? ("bottom", "pants_basic") : (x.Key, x.Value)), changed: "bottom").ToDictionary(x => x.Slot, x => x.Key);
        Assert.Equal("pants_basic", back["bottom"]); Assert.Equal("tee_basic", back["top"]);
        // taking the dress off by choosing a shirt frees the legs
        var shirt = AvatarRules.Resolve(dress.Select(x => x.Key == "top" ? ("top", "jacket_basic") : (x.Key, x.Value)), changed: "top").ToDictionary(x => x.Slot, x => x.Key);
        Assert.Equal("pants_basic", shirt["bottom"]);
        // an unknown item is dropped and a missing required slot is filled; resolved sets are always valid
        var messy = AvatarRules.Resolve([("top", "ghost"), ("hat", "beanie_basic")]);
        Assert.Empty(AvatarRules.CheckEquipment([.. messy]));
        // an outer layer without a top cannot stay
        Assert.Empty(AvatarRules.CheckEquipment([.. AvatarRules.Resolve([("jacket", "coat_long"), ("top", "none")])]));
    }

    [Fact]
    public void ABeanieHidesTheHairAndOnlyABeanie()
    {
        Assert.False(AvatarRules.HairVisible([("hat", "beanie_basic")]));
        Assert.True(AvatarRules.HairVisible([("hat", "cap_basic"), ("glasses", "glasses_round")]));
    }

    [Fact]
    public void TheApiRefusesConflictingOutfits_AndAcceptsTheNewSlots()
    {
        var avatar = Mine();
        var conflict = Request(avatar, items: [("top", "dress_basic"), ("bottom", "shorts_basic"), ("shoes", "boots_basic")]);
        Assert.Contains("covered", AvatarCatalog.Validate(conflict)["equippedItems"][0]);
        var rich = Request(avatar, items: [("top", "tee_basic"), ("jacket", "coat_long"), ("bottom", "skirt_basic"), ("shoes", "sandals_basic"), ("hat", "beanie_basic"), ("glasses", "glasses_square"), ("neck", "necklace_chain"), ("ears", "earrings_hoop"), ("gloves", "gloves_basic"), ("back", "backpack_basic")]);
        Assert.Empty(AvatarCatalog.Validate(rich));
    }

    // ---- saved looks --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ALookIsSavedAndPutBackOn()
    {
        await Wear(Request(Mine(), "long", "#8D5524", "{\"height\":1.1}", "{\"noseSize\":1.1}", ("top", "jacket_basic"), ("bottom", "shorts_basic"), ("shoes", "boots_basic"), ("hat", "cap_basic")));
        var saved = await AvatarLooks.SaveCurrentAsync(_db, _me, "Verão", _now, default); Assert.True(saved.Ok, saved.Error);
        var id = ((LookSummary)saved.Data!).Id;
        await Wear(Request(Mine(), "bald", "#FFDBAC"));
        Assert.Equal("bald", Mine().Appearance!.HairStyle);

        var applied = await AvatarLooks.ApplyAsync(_db, _me, id, _now, default); Assert.True(applied.Ok, applied.Error);
        var avatar = Mine();
        Assert.Equal("long", avatar.Appearance!.HairStyle); Assert.Equal("#8D5524", avatar.Appearance.SkinColor); Assert.Equal("{\"height\":1.1}", avatar.Appearance.BodySettingsJson); Assert.Equal("{\"noseSize\":1.1}", avatar.Appearance.FaceSettingsJson);
        Assert.Equal(["bottom:shorts_basic", "hat:cap_basic", "shoes:boots_basic", "top:jacket_basic"], avatar.EquippedItems.OrderBy(i => i.Slot).Select(i => $"{i.Slot}:{i.ItemKey}").ToArray());
        Assert.Equal("Marina Costa", avatar.DisplayName); // the name and the camera are not part of a look
    }

    [Fact]
    public async Task LookNamesAreCleanUniqueAndLimited()
    {
        Assert.Equal("bad_name", (await AvatarLooks.SaveCurrentAsync(_db, _me, "  ", _now, default)).Error);
        Assert.Equal("bad_name", (await AvatarLooks.SaveCurrentAsync(_db, _me, new string('x', 41), _now, default)).Error);
        Assert.True((await AvatarLooks.SaveCurrentAsync(_db, _me, "Trabalho", _now, default)).Ok);
        Assert.Equal("name_taken", (await AvatarLooks.SaveCurrentAsync(_db, _me, "trabalho", _now, default)).Error);
        Assert.True((await AvatarLooks.SaveCurrentAsync(_db, _friend, "Trabalho", _now, default)).Ok); // names are per avatar
        for (var i = 0; i < AvatarLooks.MaxLooks - 1; i++) Assert.True((await AvatarLooks.SaveCurrentAsync(_db, _me, $"Look {i}", _now, default)).Ok);
        Assert.Equal("too_many_looks", (await AvatarLooks.SaveCurrentAsync(_db, _me, "Demais", _now, default)).Error);
        Assert.Equal(AvatarLooks.MaxLooks, (await AvatarLooks.ListAsync(_db, _me, default)).Count);
    }

    [Fact]
    public async Task DuplicatingRenamingAndDeleting()
    {
        var id = ((LookSummary)(await AvatarLooks.SaveCurrentAsync(_db, _me, "Festa", _now, default)).Data!).Id;
        var copy = (LookSummary)(await AvatarLooks.DuplicateAsync(_db, _me, id, _now, default)).Data!;
        Assert.Equal("Festa (cópia)", copy.Name);
        var again = (LookSummary)(await AvatarLooks.DuplicateAsync(_db, _me, id, _now, default)).Data!;
        Assert.Equal("Festa (cópia) (2)", again.Name);
        Assert.Equal("name_taken", (await AvatarLooks.RenameAsync(_db, _me, copy.Id, "festa", _now, default)).Error);
        Assert.True((await AvatarLooks.RenameAsync(_db, _me, copy.Id, "Casual", _now, default)).Ok);
        Assert.True((await AvatarLooks.DeleteAsync(_db, _me, copy.Id, default)).Ok);
        Assert.Equal(["Festa", "Festa (cópia) (2)"], (await AvatarLooks.ListAsync(_db, _me, default)).Select(l => l.Name).ToArray());
        Assert.Equal("not_found", (await AvatarLooks.DeleteAsync(_db, _me, copy.Id, default)).Error);
    }

    [Fact]
    public async Task NobodyTouchesSomeoneElsesLooks()
    {
        var id = ((LookSummary)(await AvatarLooks.SaveCurrentAsync(_db, _me, "Meu", _now, default)).Data!).Id;
        Assert.Equal("not_found", (await AvatarLooks.ApplyAsync(_db, _friend, id, _now, default)).Error);
        Assert.Equal("not_found", (await AvatarLooks.DuplicateAsync(_db, _friend, id, _now, default)).Error);
        Assert.Equal("not_found", (await AvatarLooks.RenameAsync(_db, _friend, id, "Roubado", _now, default)).Error);
        Assert.Equal("not_found", (await AvatarLooks.ShareAsync(_db, _friend, id, true, _now, default)).Error);
        Assert.Equal("not_found", (await AvatarLooks.DeleteAsync(_db, _friend, id, default)).Error);
        Assert.Empty(await AvatarLooks.ListAsync(_db, _friend, default));
    }

    [Fact]
    public async Task SharingGivesACodeAnotherPersonCanImport_UntilItIsTurnedOff()
    {
        await Wear(Request(Mine(), "ponytail", "#8D5524", items: [("top", "dress_basic"), ("bottom", "covered"), ("shoes", "sandals_basic")]));
        var id = ((LookSummary)(await AvatarLooks.SaveCurrentAsync(_db, _me, "Vestido", _now, default)).Data!).Id;
        var code = ((LookSummary)(await AvatarLooks.ShareAsync(_db, _me, id, true, _now, default)).Data!).ShareCode!;
        Assert.Equal(8, code.Length);
        Assert.Equal(code, ((LookSummary)(await AvatarLooks.ShareAsync(_db, _me, id, true, _now, default)).Data!).ShareCode); // asking again keeps the same code

        var imported = await AvatarLooks.ImportAsync(_db, _friend, code.ToLowerInvariant(), null, _now, default); Assert.True(imported.Ok, imported.Error);
        var copy = (LookSummary)imported.Data!; Assert.Equal("Vestido", copy.Name); Assert.Null(copy.ShareCode); // the copy is private
        Assert.True((await AvatarLooks.ApplyAsync(_db, _friend, copy.Id, _now, default)).Ok);
        var friend = Mine(_friend);
        Assert.Equal("ponytail", friend.Appearance!.HairStyle); Assert.Equal("Rafa Lima", friend.DisplayName);
        Assert.Contains(friend.EquippedItems, i => i.Slot == "top" && i.ItemKey == "dress_basic");

        Assert.Equal("Vestido (2)", ((LookSummary)(await AvatarLooks.ImportAsync(_db, _friend, code, null, _now, default)).Data!).Name); // importing twice does not collide
        Assert.True((await AvatarLooks.ShareAsync(_db, _me, id, false, _now, default)).Ok);
        Assert.Equal("not_found", (await AvatarLooks.ImportAsync(_db, _friend, code, null, _now, default)).Error);
        Assert.Equal("bad_code", (await AvatarLooks.ImportAsync(_db, _friend, "oops", null, _now, default)).Error);
        Assert.Equal("bad_code", (await AvatarLooks.ImportAsync(_db, _friend, "AAAAAAA0", null, _now, default)).Error); // 0 and O, 1 and I are not in the alphabet
    }

    [Fact]
    public async Task ALookThatBreaksTheRulesIsNeverApplied_EvenIfItWasStoredOrShared()
    {
        var id = ((LookSummary)(await AvatarLooks.SaveCurrentAsync(_db, _me, "Velho", _now, default)).Data!).Id;
        var look = await _db.AvatarLooks.SingleAsync(l => l.Id == id);
        look.SnapshotJson = look.SnapshotJson.Replace("tee_basic", "item_pago_que_nao_tenho"); look.ShareCode = "ABCDEFGH"; await _db.SaveChangesAsync();
        Assert.Equal("look_invalid", (await AvatarLooks.ApplyAsync(_db, _me, id, _now, default)).Error);
        Assert.Equal("look_invalid", (await AvatarLooks.ImportAsync(_db, _friend, "ABCDEFGH", null, _now, default)).Error);
        Assert.Equal("tee_basic", Mine().EquippedItems.Single(i => i.Slot == "top").ItemKey); // nothing changed
        look.SnapshotJson = "not json"; await _db.SaveChangesAsync();
        Assert.Equal("look_invalid", (await AvatarLooks.ApplyAsync(_db, _me, id, _now, default)).Error);
    }

    [Fact]
    public async Task SavingTheLookCopiesAllSettingsAndTheListShowsAPreview()
    {
        await Wear(Request(Mine(), "mohawk", "#8D5524", "{\"legs\":1.1}", "{\"eyeScale\":1.1}"));
        var summary = (LookSummary)(await AvatarLooks.SaveCurrentAsync(_db, _me, "Punk", _now, default)).Data!;
        Assert.Equal("mohawk", summary.HairStyle); Assert.Equal("#8D5524", summary.SkinColor); Assert.Equal(3, summary.EquippedItems.Count);
        var stored = JsonDocument.Parse((await _db.AvatarLooks.SingleAsync()).SnapshotJson).RootElement;
        Assert.Equal("{\"legs\":1.1}", stored.GetProperty("bodySettingsJson").GetString()); Assert.Equal("{\"eyeScale\":1.1}", stored.GetProperty("faceSettingsJson").GetString());
    }
}
