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
    [JsonPropertyName("eyeColor")] public string EyeColor { get; set; } = "#4A3326";
    [JsonPropertyName("beardStyle")] public string BeardStyle { get; set; } = "none";
    [JsonPropertyName("bodySettingsJson")] public string BodySettingsJson { get; set; } = "{}";
    [JsonPropertyName("faceSettingsJson")] public string FaceSettingsJson { get; set; } = "{}";
    [JsonPropertyName("outfitsJson")] public string OutfitsJson { get; set; } = "[]";
    [JsonPropertyName("attachmentsJson")] public string AttachmentsJson { get; set; } = "[]";
    [JsonPropertyName("cameraSensitivity")] public float CameraSensitivity { get; set; } = 0.003f;
    [JsonPropertyName("invertCameraY")] public bool InvertCameraY { get; set; }
    [JsonPropertyName("equippedItems")] public List<EquippedItemData> EquippedItems { get; set; } = [];
    /// The checked GLB the avatar wears as its body (null: the procedural body), its content hash, the factor that brings it to 1.8 m and its bone map.
    [JsonPropertyName("customModelId")] public Guid? CustomModelId { get; set; }
    [JsonPropertyName("customModelHash")] public string? CustomModelHash { get; set; }
    [JsonPropertyName("customModelScale")] public float CustomModelScale { get; set; } = 1f;
    [JsonPropertyName("customModelBonesJson")] public string CustomModelBonesJson { get; set; } = "{}";

    public string Item(string slot, string fallback) => EquippedItems.FirstOrDefault(x => x.Slot == slot)?.ItemKey ?? fallback;
    public string Tint(string slot, string fallback) => EquippedItems.FirstOrDefault(x => x.Slot == slot)?.TintColor ?? fallback;

    public AvatarAppearanceData Copy() => new()
    {
        Id = Id, DisplayName = DisplayName, BaseModel = BaseModel, SkinColor = SkinColor,
        HairColor = HairColor, HairStyle = HairStyle, EyeColor = EyeColor, BeardStyle = BeardStyle,
        BodySettingsJson = BodySettingsJson, FaceSettingsJson = FaceSettingsJson, OutfitsJson = OutfitsJson, AttachmentsJson = AttachmentsJson,
        CameraSensitivity = CameraSensitivity, InvertCameraY = InvertCameraY, CustomModelId = CustomModelId, CustomModelHash = CustomModelHash, CustomModelScale = CustomModelScale, CustomModelBonesJson = CustomModelBonesJson,
        EquippedItems = EquippedItems.Select(x => new EquippedItemData { Slot = x.Slot, ItemKey = x.ItemKey, TintColor = x.TintColor }).ToList()
    };
}

public sealed class EquippedItemData
{
    [JsonPropertyName("slot")] public string Slot { get; set; } = string.Empty;
    [JsonPropertyName("itemKey")] public string ItemKey { get; set; } = string.Empty;
    [JsonPropertyName("tintColor")] public string TintColor { get; set; } = "#FFFFFF";
}
