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
}
