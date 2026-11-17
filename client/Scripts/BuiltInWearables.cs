using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The wardrobe that comes with the game for the man of LifeVerse: every item is a file (Assets/wearables/&lt;key&gt;.bin, a glTF skinned to the skeleton of his body, whose extras say what it is), so nothing is downloaded and
/// everybody who sees him sees the same clothes. A character wears one by naming its key in a slot of its equipment, like any item of the catalog; the rig turns the key into a worn item and draws it with the same code
/// that draws the clothes a person brings.
/// </summary>
public static class BuiltInWearables
{
    public const string Prefix = "mh_";
    public static string PathOf(string key) => $"res://Assets/wearables/{key}.bin";
    public static bool IsKey(string? key) => key is not null && key.StartsWith(Prefix, StringComparison.Ordinal);

    private sealed record Entry(byte[] Bytes, string Hash, string Tint, string Layer);
    private static readonly Dictionary<string, Entry?> Known = [];

    private static Entry? Load(string key)
    {
        if (Known.TryGetValue(key, out var cached)) return cached;
        Entry? entry = null;
        if (IsKey(key) && Godot.FileAccess.FileExists(PathOf(key)) && Godot.FileAccess.GetFileAsBytes(PathOf(key)) is { Length: > 0 } bytes)
            entry = new Entry(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ExtrasText(bytes, "tint"), ExtrasText(bytes, "layer"));
        return Known[key] = entry;
    }

    /// <summary>One of the strings of the "lifeverse" extras of the asset of a glTF binary (empty when it has none).</summary>
    private static string ExtrasText(byte[] glb, string name)
    {
        try
        {
            if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67) return string.Empty;
            var length = (int)BitConverter.ToUInt32(glb, 12); using var document = JsonDocument.Parse(glb.AsMemory(20, length));
            return document.RootElement.TryGetProperty("asset", out var asset) && asset.TryGetProperty("extras", out var extras) && extras.TryGetProperty("lifeverse", out var meta) && meta.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException) { return string.Empty; }
    }

    public static bool Exists(string key) => Load(key) is not null;
    public static byte[]? Bytes(string key) => Load(key)?.Bytes;

    /// <summary>The item as the rig wears it (its id is the same for everybody), or null when the key has no file.</summary>
    public static WornWearable? Worn(string slot, string key)
    {
        if (Load(key) is not { } entry) return null;
        var catalog = AvatarRules.Find(slot, key);
        return new WornWearable
        {
            Id = new Guid(MD5.HashData(Encoding.UTF8.GetBytes("builtin:" + key))), Key = key, Hash = entry.Hash, Slot = slot, Kind = "clothing", Layer = entry.Layer.Length > 0 ? entry.Layer : catalog?.Layer ?? "base",
            HidesHair = catalog?.HidesHair ?? false, Covers = catalog?.CoveredSlots ?? [], Tint = entry.Tint
        };
    }
}
