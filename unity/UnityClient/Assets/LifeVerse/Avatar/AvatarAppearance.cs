using System;
using System.Collections.Generic;
using System.Text.Json;
using LifeVerse.Shared.Avatar;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// What the server says a person looks like (the AppearanceJson of the spawn message, the same JSON the Godot client reads as AvatarAppearanceData): colours, hair and
    /// beard style, the body and face sliders (read with the shared <see cref="AvatarRules"/> ranges, so a bad number is brought back into range), what is worn in each slot
    /// with its tint, and the custom GLB body when there is one. A field that is missing or unreadable keeps its default.
    /// </summary>
    public sealed class AvatarAppearance
    {
        public string DisplayName = "Resident", BaseModel = "human_a", HairStyle = "short", BeardStyle = "none";
        public Color Skin = Hex("#C98F65", Color.white), Hair = Hex("#2B1B14", Color.black), Eyes = Hex("#4A3326", Color.black);
        public Dictionary<string, float> Body = AvatarRules.ReadSettings("{}", AvatarRules.BodyRanges), Face = AvatarRules.ReadSettings("{}", AvatarRules.FaceRanges);
        public Dictionary<string, (string Item, Color Tint)> Worn = new Dictionary<string, (string, Color)>();
        /// <summary>What is worn on the body, as the platform sent it (a JSON list).</summary>
        public string AttachmentsJson = "[]";
        public Guid? CustomModelId; public float CustomModelScale = 1f; public string CustomModelBonesJson = "{}";
        public int CustomModelTriangles;

        public static Color Hex(string text, Color fallback) => text != null && text.Length == 7 && text[0] == '#' && ColorUtility.TryParseHtmlString(text, out var color) ? color : fallback;

        public Color Tint(string slot, Color fallback) => Worn.TryGetValue(slot, out var worn) ? worn.Tint : fallback;
        public string Item(string slot, string fallback) => Worn.TryGetValue(slot, out var worn) ? worn.Item : fallback;
        /// <summary>The height factor of the body slider (1 is the normal height).</summary>
        public float Height => Body.TryGetValue("height", out var value) ? value : 1f;
        public float Shoulder => Body.TryGetValue("shoulder", out var value) ? value : 1f;

        public static AvatarAppearance Parse(string json)
        {
            var result = new AvatarAppearance();
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement; if (root.ValueKind != JsonValueKind.Object) return result;
                string Text(string name, string fallback) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
                result.DisplayName = Text("displayName", result.DisplayName); result.BaseModel = Text("baseModel", result.BaseModel); result.HairStyle = Text("hairStyle", result.HairStyle); result.BeardStyle = Text("beardStyle", result.BeardStyle);
                result.Skin = Hex(Text("skinColor", ""), result.Skin); result.Hair = Hex(Text("hairColor", ""), result.Hair); result.Eyes = Hex(Text("eyeColor", ""), result.Eyes);
                result.Body = AvatarRules.ReadSettings(Text("bodySettingsJson", "{}"), AvatarRules.BodyRanges); result.Face = AvatarRules.ReadSettings(Text("faceSettingsJson", "{}"), AvatarRules.FaceRanges);
                if (root.TryGetProperty("equippedItems", out var items) && items.ValueKind == JsonValueKind.Array)
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        var slot = item.TryGetProperty("slot", out var s) ? s.GetString() : null; var key = item.TryGetProperty("itemKey", out var k) ? k.GetString() : null;
                        if (string.IsNullOrEmpty(slot) || string.IsNullOrEmpty(key)) continue;
                        var tint = item.TryGetProperty("tintColor", out var t) ? Hex(t.GetString(), Color.white) : Color.white; result.Worn[slot] = (key, tint);
                    }
                if (root.TryGetProperty("customModelId", out var model) && model.ValueKind == JsonValueKind.String && Guid.TryParse(model.GetString(), out var id)) result.CustomModelId = id;
                if (root.TryGetProperty("customModelScale", out var scale) && scale.ValueKind == JsonValueKind.Number && scale.TryGetSingle(out var factor) && factor > 0f && float.IsFinite(factor)) result.CustomModelScale = factor;
                result.CustomModelBonesJson = Text("customModelBonesJson", "{}"); result.AttachmentsJson = Text("bodyAttachmentsJson", "[]");
                if (root.TryGetProperty("customModelTriangles", out var triangles) && triangles.TryGetInt32(out var count)) result.CustomModelTriangles = count;
            }
            catch (JsonException) { }
            return result;
        }
    }
}
