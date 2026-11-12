using System.Text.Json;

namespace LifeVerse.Client.Scripts.RealEstate;

public sealed record PropNotice(Guid Id, string Kind, string Title, string Body, DateTimeOffset At, bool Read);

/// <summary>What <c>GET /properties/notices</c> says: the notices the real estate wrote for the person and how many are unread.</summary>
public sealed class NoticesData
{
    public int Unread { get; init; }
    public IReadOnlyList<PropNotice> Items { get; init; } = [];

    public static NoticesData? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        var list = new List<PropNotice>();
        foreach (var n in items.EnumerateArray())
        {
            if (!n.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var guid)) continue;
            string Text(string name) => n.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            list.Add(new PropNotice(guid, Text("kind"), Text("title"), Text("body"), n.TryGetProperty("at", out var at) && at.TryGetDateTimeOffset(out var when) ? when : default, n.TryGetProperty("read", out var read) && read.ValueKind == JsonValueKind.True));
        }
        return new NoticesData { Unread = root.TryGetProperty("unread", out var u) && u.TryGetInt32(out var count) ? count : 0, Items = list };
    }
}
