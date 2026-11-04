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
                "group_invite" => Text(data, "groupName") is { Length: > 0 } invited ? $"Você foi convidado para o grupo {invited}." : "Você foi convidado para um grupo.",
                "group_join_request" => $"Novo pedido para entrar em {Group(data)}.",
                "group_request_decided" => data.ValueKind == JsonValueKind.Object && data.TryGetProperty("approved", out var approved) && approved.ValueKind == JsonValueKind.True ? $"Você entrou em {Group(data)}." : $"Seu pedido para entrar em {Group(data)} foi recusado.",
                "group_kicked" => $"Você foi removido de {Group(data)}.",
                "group_banned" => $"Você foi banido de {Group(data)}.",
                "group_role_changed" => $"Seu cargo em {Group(data)} agora é {Text(data, "role")}.",
                "group_leader_changed" => $"Você é o novo líder de {Group(data)}.",
                "group_dissolved" => $"{Group(data)} foi dissolvido.",
                "group_announcement" => $"{Group(data)} — anúncio: {Clip(Text(data, "title"), 60)}",
                "group_event" => $"{Group(data)} — novo evento: {Clip(Text(data, "name"), 60)}",
                "group_land" => $"{Group(data)} — novo terreno: {Clip(Text(data, "name"), 60)}",
                "group_land_released" => $"{Group(data)} devolveu a você o terreno {Clip(Text(data, "name"), 60)}.",
                "group_event_cancelled" => $"{Group(data)} — evento cancelado: {Clip(Text(data, "name"), 60)}",
                _ => null
            };
        }
        catch (JsonException) { return null; }
    }

    private static string Group(JsonElement data) => Text(data, "groupName") is { Length: > 0 } name ? name : "um grupo";

    private static string Text(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
