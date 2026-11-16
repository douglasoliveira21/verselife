using System.Text.Json;

namespace LifeVerse.Shared.Avatar;

/// <summary>
/// The rules the island server applies to the live preview of a placement that somebody is editing: the preview is only shown (never kept), only for an attachment that really is worn by that player, never faster than a few times a
/// second, and always within the limits the API enforces when the placement is saved. The server keeps the pure parts here so they can be tried without a running island.
/// </summary>
public static class AttachmentLive
{
    /// <summary>At least this long between two previews from the same player (the client sends a few per second while a handle is dragged).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(60);
    public const int Values = 9;

    /// <summary>The ids of the attachments in the "bodyAttachmentsJson" of an appearance (empty when there are none or the text is not what it should be).</summary>
    public static HashSet<Guid> WornIds(string? appearanceJson)
    {
        var ids = new HashSet<Guid>();
        try
        {
            if (string.IsNullOrEmpty(appearanceJson)) return ids;
            using var appearance = JsonDocument.Parse(appearanceJson);
            if (!appearance.RootElement.TryGetProperty("bodyAttachmentsJson", out var text) || text.ValueKind != JsonValueKind.String) return ids;
            using var list = JsonDocument.Parse(text.GetString() ?? "[]");
            if (list.RootElement.ValueKind != JsonValueKind.Array) return ids;
            foreach (var item in list.RootElement.EnumerateArray()) if (item.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid)) ids.Add(guid);
        }
        catch (JsonException) { }
        return ids;
    }

    /// <summary>Whether a preview from this player may be passed on now.</summary>
    public static bool MayRelay(DateTimeOffset? last, DateTimeOffset now) => last is null || now - last.Value >= MinInterval;

    /// <summary>Null when the nine numbers (position, rotation in degrees, scale) can be shown, otherwise what is wrong with them.</summary>
    public static string? Check(float[]? values, AttachmentPoints.Limits? limits = null)
    {
        if (values is null || values.Length != Values) return "bad_values";
        return AttachmentPoints.CheckTransform(limits ?? AttachmentPoints.DefaultLimits, values[..3], values[3..6], values[6..9]);
    }
}
