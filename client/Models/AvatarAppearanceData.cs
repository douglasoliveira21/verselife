using System.Text.Json.Serialization;

namespace LifeVerse.Client.Models;

public sealed class AvatarAppearanceData
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "Resident";
    [JsonPropertyName("baseModel")] public string BaseModel { get; set; } = "human_a";
    [JsonPropertyName("skinColor")] public string SkinColor { get; set; } = "#C98F65";
    [JsonPropertyName("hairColor")] public string HairColor { get; set; } = "#2B1B14";
    [JsonPropertyName("hairStyle")] public string HairStyle { get; set; } = "short";
    [JsonPropertyName("cameraSensitivity")] public float CameraSensitivity { get; set; } = 0.003f;
    [JsonPropertyName("invertCameraY")] public bool InvertCameraY { get; set; }
    [JsonPropertyName("equippedItems")] public List<EquippedItemData> EquippedItems { get; set; } = [];

    public string Item(string slot, string fallback) => EquippedItems.FirstOrDefault(x => x.Slot == slot)?.ItemKey ?? fallback;
    public string Tint(string slot, string fallback) => EquippedItems.FirstOrDefault(x => x.Slot == slot)?.TintColor ?? fallback;

    public AvatarAppearanceData Copy() => new()
    {
        Id = Id, DisplayName = DisplayName, BaseModel = BaseModel, SkinColor = SkinColor,
        HairColor = HairColor, HairStyle = HairStyle, CameraSensitivity = CameraSensitivity,
        InvertCameraY = InvertCameraY, EquippedItems = EquippedItems.Select(x => new EquippedItemData { Slot = x.Slot, ItemKey = x.ItemKey, TintColor = x.TintColor }).ToList()
    };
}

public sealed class EquippedItemData
{
    [JsonPropertyName("slot")] public string Slot { get; set; } = string.Empty;
    [JsonPropertyName("itemKey")] public string ItemKey { get; set; } = string.Empty;
    [JsonPropertyName("tintColor")] public string TintColor { get; set; } = "#FFFFFF";
}
