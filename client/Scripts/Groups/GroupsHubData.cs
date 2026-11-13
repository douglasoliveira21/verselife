using System.Text.Json;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>A group of the person as the "Meus Grupos" list shows it.</summary>
public sealed record HubGroup(Guid Id, string Name, string Tag, string Description, string AccentColor, string Emblem, string Privacy, string JoinPolicy, int Members, int MemberLimit, bool IsActive, string Title)
{
    public string PrivacyText => Privacy == "private" ? "Privado" : "Público";
    public string JoinText => JoinPolicy switch { "open" => "Entrada livre", "invite" => "Só por convite", _ => "Entrada por aprovação" };
    public string MembersText => Members == 1 ? "1 membro" : $"{Members} membros";
}

/// <summary>One announcement in "Avisos recentes".</summary>
public sealed record HubNotice(Guid Id, string Title, bool Pinned, string Author, DateTimeOffset At);

public static class GroupsHubParse
{
    private static string Text(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : 0;
    private static bool Flag(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;

    public static IReadOnlyList<HubGroup> Groups(JsonElement root)
    {
        var list = new List<HubGroup>(); if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (var g in root.EnumerateArray())
        {
            if (!g.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var guid)) continue;
            var colour = Text(g, "accentColor"); var emblem = Text(g, "emblem");
            list.Add(new HubGroup(guid, Text(g, "name"), Text(g, "tag"), Text(g, "description"), colour.Length > 0 ? colour : "#12F2E0", emblem.Length > 0 ? emblem : "people", Text(g, "privacy"), Text(g, "joinPolicy"), Int(g, "members"), Int(g, "memberLimit"), Flag(g, "isActive"), Text(g, "title")));
        }
        return list;
    }

    public static IReadOnlyList<HubNotice> Notices(JsonElement root)
    {
        var list = new List<HubNotice>(); if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (var n in root.EnumerateArray())
            if (n.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var guid)) list.Add(new HubNotice(guid, Text(n, "title"), Flag(n, "pinned"), Text(n, "author"), n.TryGetProperty("at", out var at) && at.TryGetDateTimeOffset(out var when) ? when : default));
        return list;
    }

    /// <summary>How many unread notifications each group has, from <c>GET /groups/notifications</c>.</summary>
    public static IReadOnlyDictionary<Guid, int> Unread(JsonElement root)
    {
        var counts = new Dictionary<Guid, int>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var n in items.EnumerateArray())
                if (!Flag(n, "read") && n.TryGetProperty("groupId", out var g) && Guid.TryParse(g.GetString(), out var guid)) counts[guid] = counts.GetValueOrDefault(guid) + 1;
        return counts;
    }
}

/// <summary>The calls of the "Meus Grupos" window.</summary>
public sealed class GroupsHubApi(IslandManagementApi inner)
{
    public Task<IslandReply> Mine() => inner.Send("GET", "/api/v1/groups/mine");
    public Task<IslandReply> Notices(Guid group) => inner.Send("GET", $"/api/v1/groups/{group}/announcements");
    public Task<IslandReply> Unread() => inner.Send("GET", "/api/v1/groups/notifications?unread=true&take=100");
    public Task<IslandReply> Activate(Guid group) => inner.Send("POST", $"/api/v1/groups/{group}/activate", new { });
    public Task<IslandReply> Deactivate() => inner.Send("DELETE", "/api/v1/groups/active");
}
