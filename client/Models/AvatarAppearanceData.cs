using System.Text.Json.Serialization;

namespace LifeVerse.Client.Models;

public sealed class AvatarAppearanceData
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "Resident";
    /// <summary>Set by the server only: the group worn and the title of the cargo, shown under the name. The client never sends it back as a choice.</summary>
    [JsonPropertyName("groupLabel")] public string GroupLabel { get; set; } = "";
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
    /// Triangles of the custom body, so a scene can budget what it draws (0 when the body is the procedural one).
    [JsonPropertyName("customModelTriangles")] public int CustomModelTriangles { get; set; }
    [JsonPropertyName("customModelBonesJson")] public string CustomModelBonesJson { get; set; } = "{}";
    /// The custom items worn (what to download, the slot, the kind, the bone and placement of a rigid piece), as the server describes them.
    [JsonPropertyName("customWearablesJson")] public string CustomWearablesJson { get; set; } = "[]";
    /// Which animation plays for which state when it is not the default ("Walk": "builtin:happy" or "custom:{file}#{name}"), and the files the custom ones come from.
    [JsonPropertyName("animationOverridesJson")] public string AnimationOverridesJson { get; set; } = "{}";
    [JsonPropertyName("animationFilesJson")] public string AnimationFilesJson { get; set; } = "[]";

    public Dictionary<string, string> AnimationOverrides
    {
        get
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(AnimationOverridesJson) ?? []; }
            catch (System.Text.Json.JsonException) { return []; }
        }
    }

    public IReadOnlyList<AnimationFileInfo> AnimationFiles
    {
        get
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<List<AnimationFileInfo>>(AnimationFilesJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? []; }
            catch (System.Text.Json.JsonException) { return []; }
        }
    }

    public IReadOnlyList<WornWearable> CustomWearables
    {
        get
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<List<WornWearable>>(CustomWearablesJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? []; }
            catch (System.Text.Json.JsonException) { return []; }
        }
    }

    /// What the outfit rules need to know about the custom items this avatar wears.
    public LifeVerse.Shared.Avatar.WearableItem? CustomItem(string slot, string key) => CustomWearables.FirstOrDefault(w => w.Key == key && w.Slot == slot) is { } w ? new LifeVerse.Shared.Avatar.WearableItem(w.Slot, w.Key, w.Key, w.Layer, w.Covers is { Length: > 0 } ? w.Covers : null, w.HidesHair) : null;

    public string Item(string slot, string fallback) => EquippedItems.FirstOrDefault(x => x.Slot == slot)?.ItemKey ?? fallback;
    public string Tint(string slot, string fallback) => EquippedItems.FirstOrDefault(x => x.Slot == slot)?.TintColor ?? fallback;

    public AvatarAppearanceData Copy() => new()
    {
        Id = Id, DisplayName = DisplayName, BaseModel = BaseModel, SkinColor = SkinColor,
        HairColor = HairColor, HairStyle = HairStyle, EyeColor = EyeColor, BeardStyle = BeardStyle,
        BodySettingsJson = BodySettingsJson, FaceSettingsJson = FaceSettingsJson, OutfitsJson = OutfitsJson, AttachmentsJson = AttachmentsJson,
        CameraSensitivity = CameraSensitivity, InvertCameraY = InvertCameraY, CustomModelId = CustomModelId, CustomModelHash = CustomModelHash, CustomModelScale = CustomModelScale, CustomModelTriangles = CustomModelTriangles, CustomModelBonesJson = CustomModelBonesJson, CustomWearablesJson = CustomWearablesJson, AnimationOverridesJson = AnimationOverridesJson, AnimationFilesJson = AnimationFilesJson,
        EquippedItems = EquippedItems.Select(x => new EquippedItemData { Slot = x.Slot, ItemKey = x.ItemKey, TintColor = x.TintColor }).ToList()
    };
}

/// An animation file an avatar uses, as the server sends it (what to download and how to check it).
public sealed class AnimationFileInfo
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
}

/// One custom item worn by an avatar, as the server sends it.
public sealed class WornWearable
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
    [JsonPropertyName("slot")] public string Slot { get; set; } = string.Empty;
    [JsonPropertyName("layer")] public string Layer { get; set; } = "base";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "attachment";
    [JsonPropertyName("hidesHair")] public bool HidesHair { get; set; }
    [JsonPropertyName("triangles")] public int Triangles { get; set; }
    [JsonPropertyName("covers")] public string[] Covers { get; set; } = [];
    [JsonPropertyName("attachBone")] public string AttachBone { get; set; } = string.Empty;
    [JsonPropertyName("offset")] public float[] Offset { get; set; } = [0, 0, 0];
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; } = [0, 0, 0];
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    /// <summary>For hair and eyes of the person: "hair" or "eyes" when the piece takes the colour of the avatar's hair or eyes.</summary>
    [JsonPropertyName("tint")] public string Tint { get; set; } = string.Empty;
}

public sealed class EquippedItemData
{
    [JsonPropertyName("slot")] public string Slot { get; set; } = string.Empty;
    [JsonPropertyName("itemKey")] public string ItemKey { get; set; } = string.Empty;
    [JsonPropertyName("tintColor")] public string TintColor { get; set; } = "#FFFFFF";
}
