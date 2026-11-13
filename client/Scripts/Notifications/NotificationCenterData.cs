using System.Text.Json;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.Notifications;

/// <summary>One line of the notification center as the server sent it.</summary>
public sealed record CenterEntry(string Source, Guid Id, string Category, string Kind, string Title, string Body, DateTimeOffset At, bool Read, Guid? GroupId, string GroupName, IReadOnlyList<string> Actions)
{
    public bool IsInvite => Source == "invite";
    public bool CanAnswer => IsInvite && Actions.Contains("accept") && Actions.Contains("decline");
}

/// <summary>A page of the center with the counters that sit on the bell.</summary>
public sealed class CenterPage
{
    public int Unread { get; init; }
    public int Grupos { get; init; }
    public int Convites { get; init; }
    public int Sistema { get; init; }
    public bool HasMore { get; init; }
    public int Skip { get; init; }
    public IReadOnlyList<CenterEntry> Items { get; init; } = [];

    public int UnreadOf(string category) => category switch { "grupos" => Grupos, "convites" => Convites, "sistema" => Sistema, _ => Unread };

    public static CenterPage? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        string Text(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : 0;
        var list = new List<CenterEntry>();
        foreach (var i in items.EnumerateArray())
        {
            if (!i.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var guid)) continue;
            Guid? group = i.TryGetProperty("groupId", out var g) && Guid.TryParse(g.GetString(), out var gid) ? gid : null;
            var actions = i.TryGetProperty("actions", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
            list.Add(new CenterEntry(Text(i, "source"), guid, Text(i, "category"), Text(i, "kind"), Text(i, "title"), Text(i, "body"), i.TryGetProperty("at", out var at) && at.TryGetDateTimeOffset(out var when) ? when : default,
                i.TryGetProperty("read", out var read) && read.ValueKind == JsonValueKind.True, group, Text(i, "groupName"), actions));
        }
        var by = root.TryGetProperty("unreadByCategory", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        return new CenterPage { Unread = Int(root, "unread"), Grupos = by.ValueKind == JsonValueKind.Object ? Int(by, "grupos") : 0, Convites = by.ValueKind == JsonValueKind.Object ? Int(by, "convites") : 0, Sistema = by.ValueKind == JsonValueKind.Object ? Int(by, "sistema") : 0,
            HasMore = root.TryGetProperty("hasMore", out var more) && more.ValueKind == JsonValueKind.True, Skip = Int(root, "skip"), Items = list };
    }
}

/// <summary>The calls of the notification center.</summary>
public sealed class NotificationsApi(IslandManagementApi inner)
{
    public Task<IslandReply> List(string category, bool unreadOnly, string? search, int skip, int take) =>
        inner.Send("GET", $"/api/v1/notifications/center?category={Uri.EscapeDataString(category)}&unread={(unreadOnly ? "true" : "false")}&skip={skip}&take={take}" + (string.IsNullOrWhiteSpace(search) ? "" : $"&q={Uri.EscapeDataString(search.Trim())}"));

    public Task<IslandReply> MarkRead(IEnumerable<CenterEntry>? items, bool all, string category = "todas") =>
        inner.Send("POST", "/api/v1/notifications/center/read", new { items = items?.Select(i => new { source = i.Source, id = i.Id }), all, category });

    public Task<IslandReply> AnswerInvite(Guid invite, bool accept) => inner.Send("POST", $"/api/v1/social/groups/invites/{invite}/{(accept ? "accept" : "decline")}", new { });
}

/// <summary>How a moment is told in the lists: "agora", "há 2 minutos", "há 3 horas", "há 2 dias", then the date.</summary>
public static class RelativeTime
{
    public static string Of(DateTimeOffset when, DateTimeOffset now)
    {
        if (when == default) return "";
        var span = now - when; if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalSeconds < 45) return "agora";
        if (span.TotalMinutes < 60) { var m = Math.Max(1, (int)Math.Round(span.TotalMinutes)); return m == 1 ? "há 1 minuto" : $"há {m} minutos"; }
        if (span.TotalHours < 24) { var h = (int)span.TotalHours; return h == 1 ? "há 1 hora" : $"há {h} horas"; }
        if (span.TotalDays < 7) { var d = (int)span.TotalDays; return d == 1 ? "há 1 dia" : $"há {d} dias"; }
        return when.ToLocalTime().ToString("dd/MM/yyyy", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
    }
}
