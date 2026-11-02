using System.Text.RegularExpressions;

namespace LifeVerse.Api.Modules.Users;

public sealed record EquippedItemRequest(string Slot, string ItemKey, string TintColor);
public sealed record SaveAvatarRequest(
    string DisplayName,
    string BaseModel,
    string SkinColor,
    string HairColor,
    string HairStyle,
    float CameraSensitivity,
    bool InvertCameraY,
    IReadOnlyList<EquippedItemRequest> EquippedItems,
    string? EyeColor = null,
    string? BeardStyle = null,
    string? BodySettingsJson = null,
    string? FaceSettingsJson = null,
    string? OutfitsJson = null,
    string? AttachmentsJson = null);
public sealed record AvatarResponse(
    Guid Id,
    string DisplayName,
    string BaseModel,
    string SkinColor,
    string HairColor,
    string HairStyle,
    string EyeColor,
    string BeardStyle,
    string BodySettingsJson,
    string FaceSettingsJson,
    string OutfitsJson,
    string AttachmentsJson,
    float CameraSensitivity,
    bool InvertCameraY,
    IReadOnlyList<EquippedItemRequest> EquippedItems);

public static partial class AvatarCatalog
{
    public static readonly HashSet<string> BaseModels = ["human_a", "human_b"];
    public static readonly HashSet<string> HairStyles = ["short", "bob", "mohawk", "bald", "long", "ponytail"];
    public static readonly HashSet<string> BeardStyles = ["none", "stubble", "short", "full"];
    public static readonly IReadOnlyDictionary<string, HashSet<string>> Items = new Dictionary<string, HashSet<string>>
    {
        ["top"] = ["tee_basic", "jacket_basic"],
        ["bottom"] = ["pants_basic", "shorts_basic"],
        ["shoes"] = ["sneakers_basic", "boots_basic"],
        ["accessory"] = ["none", "glasses_round", "hat_basic"]
    };

    public static Dictionary<string, string[]> Validate(SaveAvatarRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 3 or > 64) errors["displayName"] = ["Display name must contain 3-64 characters."];
        if (!BaseModels.Contains(request.BaseModel ?? string.Empty)) errors["baseModel"] = ["Unknown base model."];
        if (!HairStyles.Contains(request.HairStyle ?? string.Empty)) errors["hairStyle"] = ["Unknown hair style."];
        if (!IsColor(request.SkinColor) || !IsColor(request.HairColor)) errors["colors"] = ["Skin and hair colors must use #RRGGBB or #RRGGBBAA."];
        if (request.EyeColor is not null && !IsColor(request.EyeColor)) errors["eyeColor"] = ["Eye color must use #RRGGBB or #RRGGBBAA."];
        if (request.BeardStyle is not null && !BeardStyles.Contains(request.BeardStyle)) errors["beardStyle"] = ["Unknown beard style."];
        ValidateJson(request.BodySettingsJson, "bodySettingsJson", errors, 4096);
        ValidateJson(request.FaceSettingsJson, "faceSettingsJson", errors, 8192);
        ValidateJson(request.OutfitsJson, "outfitsJson", errors, 32768);
        ValidateJson(request.AttachmentsJson, "attachmentsJson", errors, 32768);
        if (request.CameraSensitivity is < 0.001f or > 0.01f) errors["cameraSensitivity"] = ["Camera sensitivity must be between 0.001 and 0.01."];
        if (request.EquippedItems is null) errors["equippedItems"] = ["Equipment is required."];
        else
        {
            if (request.EquippedItems.Any(x => x is null))
            {
                errors["equippedItems"] = ["Equipment entries cannot be null."];
                return errors;
            }
            var duplicate = request.EquippedItems.GroupBy(x => x.Slot).FirstOrDefault(x => x.Count() > 1);
            if (duplicate is not null) errors["equippedItems"] = ["Each equipment slot may appear only once."];
            if (new[] { "top", "bottom", "shoes" }.Any(required => request.EquippedItems.All(x => x.Slot != required)))
                errors["equippedItems"] = ["Top, bottom, and shoes slots are required."];
            foreach (var item in request.EquippedItems)
            {
                if (!Items.TryGetValue(item.Slot ?? string.Empty, out var allowed) || !allowed.Contains(item.ItemKey ?? string.Empty) || !IsColor(item.TintColor))
                {
                    errors["equippedItems"] = ["Equipment contains an unknown slot, item, or invalid tint."];
                    break;
                }
            }
        }
        return errors;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex ColorPattern();
    private static bool IsColor(string? value) => value is not null && ColorPattern().IsMatch(value);
    private static void ValidateJson(string? value, string key, Dictionary<string, string[]> errors, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Length > max) { errors[key] = [$"{key} exceeds the maximum size."]; return; }
        try { using var _ = System.Text.Json.JsonDocument.Parse(value); } catch { errors[key] = [$"{key} must contain valid JSON."]; }
    }
}
