using LifeVerse.Api.Modules.Users;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class AvatarValidationTests
{
    private static SaveAvatarRequest Valid() => new("Resident Name", "human_a", "#C98F65", "#2B1B14", "short", .003f, false,
        [new EquippedItemRequest("top", "tee_basic", "#3AAFC1"), new EquippedItemRequest("bottom", "pants_basic", "#263B56"), new EquippedItemRequest("shoes", "sneakers_basic", "#FFFFFF")]);

    [Fact]
    public void Valid_appearance_is_accepted() => Assert.Empty(AvatarCatalog.Validate(Valid()));

    [Fact]
    public void Unknown_catalog_item_is_rejected()
    {
        var value = Valid() with { EquippedItems = [new EquippedItemRequest("top", "paid_item_not_owned", "#FFFFFF")] };
        Assert.Contains("equippedItems", AvatarCatalog.Validate(value));
    }

    [Fact]
    public void Duplicate_slot_is_rejected()
    {
        var value = Valid() with { EquippedItems = [new EquippedItemRequest("top", "tee_basic", "#FFFFFF"), new EquippedItemRequest("top", "jacket_basic", "#FFFFFF")] };
        Assert.Contains("equippedItems", AvatarCatalog.Validate(value));
    }

    [Fact]
    public void TheManOfLifeVerseIsTheDefaultBaseModelAndIsAccepted()
    {
        Assert.Equal("lifeverse_man", LifeVerse.Shared.Avatar.AvatarRules.DefaultBaseModel);
        Assert.Contains(LifeVerse.Shared.Avatar.AvatarRules.DefaultMan, LifeVerse.Shared.Avatar.AvatarRules.BaseModels);
        var man = LifeVerse.Shared.Avatar.AvatarRules.DefaultMan;
        Assert.Empty(AvatarCatalog.Validate(Valid() with { BaseModel = man, EquippedItems = [.. LifeVerse.Shared.Avatar.AvatarRules.StarterOutfit(man).Select(x => new EquippedItemRequest(x.Slot, x.Key, x.Tint))] }));       // each body has its own wardrobe
        Assert.Contains("equippedItems", AvatarCatalog.Validate(Valid() with { BaseModel = man }));                  // the plain items belong to the older bodies
        Assert.Empty(AvatarCatalog.Validate(Valid() with { BaseModel = "human_b" }));                 // the older bodies stay
        Assert.Contains("baseModel", AvatarCatalog.Validate(Valid() with { BaseModel = "somebody_else" }));
        Assert.NotEqual(Guid.Empty, LifeVerse.Shared.Avatar.AvatarRules.DefaultManModelId);
    }
}
