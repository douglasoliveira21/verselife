using System.Text.Json;

namespace LifeVerse.Shared.Networking;

/// Turns the API's live notification events into the short text shown to the player. Unknown or broken events
/// give null so the viewer simply ignores them.
public static class NotificationFormatter
{
    public static string? Describe(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return null;
            var data = root.TryGetProperty("data", out var value) ? value : default;
            return type.GetString() switch
            {
                "message" => $"Nova mensagem: {Clip(Text(data, "content"), 80)}",
                "friend_request" => "Você recebeu um pedido de amizade.",
                "friend_accepted" => "Seu pedido de amizade foi aceito.",
                "group_invite" => "Você foi convidado para um grupo.",
                _ => null
            };
        }
        catch (JsonException) { return null; }
    }

    private static string Text(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
