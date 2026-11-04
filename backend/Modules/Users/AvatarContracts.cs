using System.Text.RegularExpressions;
using LifeVerse.Shared.Avatar;

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
    IReadOnlyList<EquippedItemRequest> EquippedItems,
    Guid? CustomModelId = null,
    string? CustomModelHash = null,
    float CustomModelScale = 1f,
    string CustomModelBonesJson = "{}",
    string CustomWearablesJson = "[]");

public static partial class AvatarCatalog
{
    public static readonly HashSet<string> BaseModels = [.. AvatarRules.BaseModels];
    public static readonly HashSet<string> HairStyles = [.. AvatarRules.HairStyles];
    public static readonly HashSet<string> BeardStyles = [.. AvatarRules.BeardStyles];
    public static readonly IReadOnlyDictionary<string, HashSet<string>> Items = AvatarRules.Catalog.GroupBy(i => i.Slot).ToDictionary(g => g.Key, g => g.Select(i => i.Key).ToHashSet());

    /// <summary>Checks an appearance against the catalog and the formats. The custom items ("custom:…") are accepted only when a resolver that knows the items of the person is given; their ownership, slot and the outfit as a whole are then checked by <see cref="AvatarWearables.CheckOutfitAsync"/>.</summary>
    public static Dictionary<string, string[]> Validate(SaveAvatarRequest request, Func<string, string, WearableItem?>? custom = null)
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
        if (!errors.ContainsKey("bodySettingsJson") && AvatarRules.CheckSettings(request.BodySettingsJson, AvatarRules.BodyRanges) is { } bodyProblem) errors["bodySettingsJson"] = [$"Body settings are not valid ({bodyProblem})."];
        if (!errors.ContainsKey("faceSettingsJson") && AvatarRules.CheckSettings(request.FaceSettingsJson, AvatarRules.FaceRanges) is { } faceProblem) errors["faceSettingsJson"] = [$"Face settings are not valid ({faceProblem})."];
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
            var problems = AvatarRules.CheckEquipment([.. request.EquippedItems.Select(x => (x.Slot ?? string.Empty, x.ItemKey ?? string.Empty))], custom);
            if (problems.Count > 0) errors["equippedItems"] = [.. problems.Select(Describe)];
            else if (request.EquippedItems.Any(item => !IsColor(item.TintColor))) errors["equippedItems"] = ["Equipment contains an invalid tint."];
        }
        return errors;
    }

    private static string Describe(EquipmentProblem problem) => problem.Code switch
    {
        "duplicate_slot" => "Each equipment slot may appear only once.",
        "missing_required" => "Top, bottom, and shoes slots are required.",
        "unknown_item" => "Equipment contains an unknown slot or item.",
        "slot_covered" => $"The {problem.Slot} slot is covered by {problem.Detail}; remove one of them.",
        "not_covered" => $"The {problem.Slot} slot is marked covered but nothing covers it.",
        "nothing_under" => $"{problem.Detail} is an outer layer and needs a top under it.",
        _ => "Equipment is not valid."
    };

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
