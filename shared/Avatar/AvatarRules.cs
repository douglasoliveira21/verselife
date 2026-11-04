using System.Globalization;
using System.Text.Json;

namespace LifeVerse.Shared.Avatar;

/// <summary>One thing an avatar can wear. A slot holds one item; an item can also cover other slots (a dress covers the legs) and can hide the hair.</summary>
public sealed record WearableItem(string Slot, string Key, string Label, string Layer = "base", string[]? Covers = null, bool HidesHair = false)
{
    public string[] CoveredSlots => Covers ?? [];
    /// <summary>"none" means the slot is empty; "covered" is what a slot holds while another item covers it.</summary>
    public bool IsEmpty => Key is "none" or "covered";
}

public sealed record EquipmentProblem(string Code, string Slot, string Detail = "");

/// <summary>
/// The rules of what an avatar looks like, shared by the API (which enforces them), the island servers and the client (which use them to
/// build the editor and to draw): the sliders of the body and the face with their limits, the wearable catalog with its slots and layers,
/// and what conflicts with what. Nothing here depends on the engine.
/// </summary>
public static class AvatarRules
{
    public sealed record Range(float Min, float Max, float Default = 1f);

    public static readonly IReadOnlyDictionary<string, Range> BodyRanges = new Dictionary<string, Range>
    {
        ["height"] = new(.85f, 1.15f), ["shoulder"] = new(.9f, 1.1f), ["torso"] = new(.9f, 1.1f), ["arms"] = new(.85f, 1.15f),
        ["legs"] = new(.85f, 1.15f), ["head"] = new(.85f, 1.15f), ["hips"] = new(.9f, 1.15f)
    };

    public static readonly IReadOnlyDictionary<string, Range> FaceRanges = new Dictionary<string, Range>
    {
        ["eyeScale"] = new(.8f, 1.2f), ["eyeSpacing"] = new(.85f, 1.15f), ["noseSize"] = new(.8f, 1.2f), ["mouthWidth"] = new(.8f, 1.2f),
        ["jawWidth"] = new(.9f, 1.1f), ["browHeight"] = new(.9f, 1.1f), ["headWidth"] = new(.9f, 1.1f)
    };

    public static readonly string[] BaseModels = ["human_a", "human_b"];
    public static readonly string[] HairStyles = ["short", "bob", "mohawk", "bald", "long", "ponytail"];
    public static readonly string[] BeardStyles = ["none", "stubble", "short", "full"];
    public static readonly string[] RequiredSlots = ["top", "bottom", "shoes"];

    /// <summary>The order the slots are drawn and listed in. "jacket" is the outer layer over "top".</summary>
    public static readonly string[] Slots = ["top", "jacket", "bottom", "shoes", "hat", "glasses", "neck", "ears", "gloves", "back", "accessory", "hair", "eyes"];

    public static readonly IReadOnlyList<WearableItem> Catalog =
    [
        new("top", "tee_basic", "Camiseta"), new("top", "jacket_basic", "Jaqueta"), new("top", "dress_basic", "Vestido", Covers: ["bottom"]),
        new("jacket", "none", "Sem casaco"), new("jacket", "coat_long", "Casaco longo", Layer: "outer"), new("jacket", "vest_basic", "Colete", Layer: "outer"),
        new("bottom", "pants_basic", "Calça"), new("bottom", "shorts_basic", "Bermuda"), new("bottom", "skirt_basic", "Saia"), new("bottom", "covered", "Coberto"),
        new("shoes", "sneakers_basic", "Tênis"), new("shoes", "boots_basic", "Botas"), new("shoes", "sandals_basic", "Sandálias"),
        new("hat", "none", "Sem chapéu"), new("hat", "cap_basic", "Boné"), new("hat", "hat_basic", "Chapéu"), new("hat", "beanie_basic", "Gorro", HidesHair: true),
        new("glasses", "none", "Sem óculos"), new("glasses", "glasses_round", "Óculos redondos"), new("glasses", "glasses_square", "Óculos quadrados"),
        new("neck", "none", "Sem colar"), new("neck", "necklace_chain", "Colar de corrente"), new("neck", "scarf_basic", "Cachecol"),
        new("ears", "none", "Sem brincos"), new("ears", "earrings_hoop", "Argolas"),
        new("gloves", "none", "Sem luvas"), new("gloves", "gloves_basic", "Luvas"),
        new("back", "none", "Sem mochila"), new("back", "backpack_basic", "Mochila"),
        new("hair", "none", "Cabelo do catálogo"), new("eyes", "none", "Olhos do catálogo"),
        new("accessory", "none", "Nenhum"), new("accessory", "glasses_round", "Óculos redondos"), new("accessory", "hat_basic", "Chapéu")
    ];

    public static WearableItem? Find(string slot, string key) => Catalog.FirstOrDefault(i => i.Slot == slot && i.Key == key);

    /// <summary>Items a person brought themselves are worn as "custom:" and the id of the item; what they are is looked up by whoever knows the person's items.</summary>
    public const string CustomPrefix = "custom:";
    public static bool IsCustom(string? key) => key is not null && key.StartsWith(CustomPrefix, StringComparison.Ordinal) && Guid.TryParse(key.AsSpan(CustomPrefix.Length), out _);
    public static string CustomKey(Guid id) => CustomPrefix + id.ToString();

    /// <summary>The catalog, plus the person's own items when a resolver that knows them is given.</summary>
    public static WearableItem? Look(string slot, string key, Func<string, string, WearableItem?>? custom) => IsCustom(key) ? custom?.Invoke(slot, key) : Find(slot, key);

    public static IEnumerable<WearableItem> ItemsFor(string slot) => Catalog.Where(i => i.Slot == slot);

    // ---- body and face sliders ------------------------------------------------------------------------------------------------------

    /// <summary>Null when the settings are acceptable: an object whose every name is a known slider and every value a number inside its limits.</summary>
    public static string? CheckSettings(string? json, IReadOnlyDictionary<string, Range> ranges)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "settings_not_object";
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!ranges.TryGetValue(property.Name, out var range)) return $"unknown_setting:{property.Name}";
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetSingle(out var value) || !float.IsFinite(value)) return $"not_a_number:{property.Name}";
                if (value < range.Min - .0001f || value > range.Max + .0001f) return $"out_of_range:{property.Name}";
            }
            return null;
        }
        catch (JsonException) { return "settings_invalid_json"; }
    }

    /// <summary>Reads the settings leniently (what the client draws): a missing, unknown or non-numeric entry is ignored, a number is clamped into its limits.</summary>
    public static Dictionary<string, float> ReadSettings(string? json, IReadOnlyDictionary<string, Range> ranges)
    {
        var result = ranges.ToDictionary(x => x.Key, x => x.Value.Default);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in document.RootElement.EnumerateObject())
                if (ranges.TryGetValue(property.Name, out var range) && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetSingle(out var value) && float.IsFinite(value))
                    result[property.Name] = Math.Clamp(value, range.Min, range.Max);
        }
        catch (JsonException) { }
        return result;
    }

    /// <summary>The settings as clean JSON with only the sliders that differ from their default (sorted, so equal looks are equal text).</summary>
    public static string WriteSettings(IReadOnlyDictionary<string, float> values, IReadOnlyDictionary<string, Range> ranges)
    {
        var parts = ranges.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => values.TryGetValue(k, out var v) && Math.Abs(Math.Clamp(v, ranges[k].Min, ranges[k].Max) - ranges[k].Default) > .0005f)
            .Select(k => $"\"{k}\":{Math.Round(Math.Clamp(values[k], ranges[k].Min, ranges[k].Max), 3).ToString("0.###", CultureInfo.InvariantCulture)}");
        return "{" + string.Join(",", parts) + "}";
    }

    // ---- what is worn --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Everything wrong with a set of worn items: an item that is not in the catalog (or in the wrong slot), a slot used twice, a required slot
    /// missing, an item sitting in a slot another item covers, and an outer layer worn without anything under it. Empty when fine.
    /// </summary>
    public static List<EquipmentProblem> CheckEquipment(IReadOnlyCollection<(string Slot, string Key)> worn, Func<string, string, WearableItem?>? custom = null)
    {
        var problems = new List<EquipmentProblem>();
        foreach (var group in worn.GroupBy(w => w.Slot)) if (group.Count() > 1) problems.Add(new("duplicate_slot", group.Key));
        var items = new Dictionary<string, WearableItem>();
        foreach (var (slot, key) in worn)
        {
            if (Look(slot ?? string.Empty, key ?? string.Empty, custom) is not { } item) { problems.Add(new("unknown_item", slot ?? string.Empty, key ?? string.Empty)); continue; }
            items[slot ?? string.Empty] = item;
        }
        foreach (var required in RequiredSlots) if (!items.ContainsKey(required)) problems.Add(new("missing_required", required));
        foreach (var item in items.Values.Where(i => !i.IsEmpty))
            foreach (var covered in item.CoveredSlots)
                if (items.TryGetValue(covered, out var other) && !other.IsEmpty) problems.Add(new("slot_covered", covered, item.Key));
        foreach (var (slot, item) in items)
        {
            if (item.Key == "covered" && !items.Values.Any(i => i.CoveredSlots.Contains(slot) && !i.IsEmpty)) problems.Add(new("not_covered", slot));
            if (item.Layer == "outer" && !(items.TryGetValue("top", out var top) && !top.IsEmpty)) problems.Add(new("nothing_under", slot, item.Key));
        }
        return problems;
    }

    /// <summary>
    /// Fixes what <see cref="CheckEquipment"/> would complain about in the way a person expects: wearing something that covers a slot empties
    /// that slot (and the legs of a dress read "covered"), a slot that is no longer covered gets a plain default back, a missing required slot is
    /// filled, and an unknown item becomes the slot's empty or default choice. The newest change (<paramref name="changed"/>) wins a conflict.
    /// </summary>
    public static List<(string Slot, string Key)> Resolve(IEnumerable<(string Slot, string Key)> worn, string? changed = null, Func<string, string, WearableItem?>? custom = null)
    {
        var map = new Dictionary<string, string>();
        foreach (var (slot, key) in worn) if (Look(slot, key, custom) is not null) map[slot] = key;
        foreach (var required in RequiredSlots) if (!map.ContainsKey(required)) map[required] = DefaultFor(required);
        // a wearable that covers other slots takes them over, except when the covered slot is the one just changed (then the cover is dropped)
        foreach (var slot in map.Keys.ToList())
        {
            var item = Look(slot, map[slot], custom);
            if (item is null || item.IsEmpty) continue;
            foreach (var covered in item.CoveredSlots)
            {
                if (map.TryGetValue(covered, out var other) && Look(covered, other, custom) is { IsEmpty: false } && changed == covered) { map[slot] = DefaultFor(slot, notCovering: item.CoveredSlots); break; }
                map[covered] = "covered";
            }
        }
        foreach (var slot in map.Keys.ToList())
            if (map[slot] == "covered" && !map.Where(p => Look(p.Key, p.Value, custom) is { } w && w.CoveredSlots.Contains(slot)).Any()) map[slot] = DefaultFor(slot);
        if (Look("top", map["top"], custom) is { IsEmpty: true } && map.TryGetValue("jacket", out var jacket) && Look("jacket", jacket, custom) is { Layer: "outer" }) map["jacket"] = "none";
        return [.. Slots.Where(map.ContainsKey).Select(s => (s, map[s]))];
    }

    /// <summary>The plain default of a slot: its empty item when it has one, otherwise the first one of the catalog.</summary>
    public static string DefaultFor(string slot, string[]? notCovering = null)
    {
        var items = ItemsFor(slot).Where(i => i.Key != "covered").ToList();
        return (items.FirstOrDefault(i => i.Key == "none") ?? items.FirstOrDefault(i => notCovering is null || !i.CoveredSlots.Any(notCovering.Contains)) ?? items[0]).Key;
    }

    /// <summary>Whether the hair is drawn: a hat that hides it (a beanie) leaves only the face.</summary>
    /// <summary>
    /// Whether the eyes of the standard face are drawn: a person's own eyes ("custom:" piece in the eyes slot) replace them. Pass no resolver when the
    /// custom pieces are not drawn (an avatar far away), so the catalog eyes stay.
    /// </summary>
    public static bool EyesVisible(IEnumerable<(string Slot, string Key)> worn, Func<string, string, WearableItem?>? custom = null) =>
        !worn.Any(w => w.Slot == "eyes" && IsCustom(w.Key) && Look(w.Slot, w.Key, custom) is not null);

    public static bool HairVisible(IEnumerable<(string Slot, string Key)> worn, Func<string, string, WearableItem?>? custom = null) => !worn.Any(w => Look(w.Slot, w.Key, custom) is { HidesHair: true });
}
