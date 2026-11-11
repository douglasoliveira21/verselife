using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Api.Data;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// The animation of a rigged mesh ("Animação de Mesh" in the Recursos tab) lives in the material JSON: which clip, whether it plays, whether it loops and how fast. The moment it
/// started or changed is written by the server (<c>since</c>, in milliseconds), so every player works out the same point of the clip from the same clock instead of each starting
/// at the time the message happened to reach them.
/// </summary>
public static class ObjectAnimation
{
    private static JsonObject? Animation(string? materialJson)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson) is JsonObject root ? root["animation"] as JsonObject : null; }
        catch (JsonException) { return null; }
    }

    private static string Key(JsonObject? a) => a is null ? string.Empty : $"{a["clip"]}|{a["playing"]}|{a["loop"]}|{a["speed"]}";

    /// <summary>Stamps the start of the animation when it is new or when its clip, state, loop or speed changed; leaves it alone otherwise.</summary>
    public static void Stamp(BuildObject item, string? previousMaterial, DateTimeOffset now)
    {
        var current = Animation(item.MaterialJson); if (current is null) return;
        var before = Animation(previousMaterial); var changed = Key(before) != Key(current);
        if (!changed && current["since"] is not null) return;
        var root = JsonNode.Parse(item.MaterialJson)!.AsObject(); var animation = root["animation"]!.AsObject();
        animation["since"] = changed || before?["since"] is null ? now.ToUnixTimeMilliseconds() : before["since"]!.DeepClone();
        item.MaterialJson = root.ToJsonString();
    }
}
