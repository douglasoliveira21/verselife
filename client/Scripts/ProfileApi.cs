using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeVerse.Client.Scripts;

public sealed record ProfileReply(bool Ok, long Status, JsonElement Body, string Message);

/// <summary>The profile and marriage calls of the server. Every call goes through one transport (the real one is <see cref="ScriptApi.HttpTransport"/>), so tests can answer for the server.</summary>
public sealed class ProfileApi(Func<string, string, string?, Task<(long Status, string Body)>> transport)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<ProfileReply> Send(string method, string path, object? body = null)
    {
        var (status, text) = await transport(method, path, body is null ? null : JsonSerializer.Serialize(body, Web));
        JsonElement root = default;
        try { if (!string.IsNullOrWhiteSpace(text)) root = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
        if (status is >= 200 and < 300) return new ProfileReply(true, status, root, string.Empty);
        var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? string.Empty : string.Empty;
        if (message.Length == 0) message = status switch { 0 => "Não foi possível falar com o servidor.", 401 => "Sessão expirada.", 403 => "Você não pode fazer isso.", 404 => "Não encontrado.", _ => $"O servidor recusou ({status})." };
        return new ProfileReply(false, status, root, message);
    }

    public Task<ProfileReply> ById(Guid id) => Send("GET", $"/api/v1/users/{id}/profile/full");
    public Task<ProfileReply> ByName(string name) => Send("GET", $"/api/v1/users/profile?name={Uri.EscapeDataString(name)}");
    public Task<ProfileReply> Update(JsonObject changes) => Send("PUT", "/api/v1/users/me/profile/details", JsonNode.Parse(changes.ToJsonString()));
    public Task<ProfileReply> GroupShown(Guid group, bool shown) => Send("PUT", $"/api/v1/users/me/groups/{group}/profile", new { shown });
    public Task<ProfileReply> ParcelShown(Guid parcel, bool shown) => Send("PUT", $"/api/v1/users/me/parcels/{parcel}/profile", new { shown });

    // ---- what one can do to another person (the profile's bottom bar) -------------------------------------------------------
    /// <summary>The relation to a person ("none", "friend", "pending_out", "pending_in"), read from the same search the Pessoas window uses; empty when it cannot be known.</summary>
    public async Task<string> RelationTo(string username, Guid id)
    {
        var reply = await Send("GET", "/api/v1/social/people?q=" + Uri.EscapeDataString(username));
        if (!reply.Ok || reply.Body.ValueKind != JsonValueKind.Array) return string.Empty;
        foreach (var person in reply.Body.EnumerateArray())
            if (person.TryGetProperty("id", out var pid) && Guid.TryParse(pid.GetString(), out var found) && found == id) return person.TryGetProperty("relation", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? string.Empty : string.Empty;
        return string.Empty;
    }
    public Task<ProfileReply> AddFriend(Guid id) => Send("POST", "/api/v1/social/friends/requests", new { userId = id });
    public Task<ProfileReply> AcceptFriend(Guid id) => Send("POST", $"/api/v1/social/friends/{id}/accept", new { });
    public Task<ProfileReply> RemoveFriend(Guid id) => Send("DELETE", $"/api/v1/social/friends/{id}");
    public Task<ProfileReply> Block(Guid id) => Send("POST", $"/api/v1/social/blocks/{id}", new { });
    public Task<ProfileReply> Message(Guid id, string text) => Send("POST", "/api/v1/social/messages", new { content = text, recipientUserId = id, groupId = (Guid?)null, islandId = (Guid?)null });
    public Task<ProfileReply> Report(Guid id, string reason) => Send("POST", "/api/v1/social/reports", new { reportedUserId = id, messageId = (Guid?)null, islandId = (Guid?)null, reason });
    /// <summary>kind "offer": the other person is taken to where I am; "request": I ask to be taken to where they are. Nobody moves until they accept.</summary>
    public Task<ProfileReply> Teleport(Guid id, string kind, Guid island) => Send("POST", "/api/v1/social/teleports", new { targetUserId = id, kind, islandId = island });
    public Task<ProfileReply> MyItems() => Send("GET", "/api/v1/inventory/items?pageSize=100");
    public Task<ProfileReply> Gift(Guid item, Guid to, string? message, string key) => Send("POST", $"/api/v1/inventory/items/{item}/offers", new { toUserId = to, kind = "gift", message, keepCopy = false, idempotencyKey = key });
    public Task<ProfileReply> GalleryAdd(Guid asset) => Send("POST", "/api/v1/users/me/profile/gallery", new { assetId = asset });
    public Task<ProfileReply> GalleryRemove(Guid image) => Send("DELETE", $"/api/v1/users/me/profile/gallery/{image}");
    public Task<ProfileReply> GalleryMain(Guid image) => Send("PUT", $"/api/v1/users/me/profile/gallery/{image}/main", new { });
    public Task<ProfileReply> GalleryOrder(IEnumerable<Guid> ids) => Send("PUT", "/api/v1/users/me/profile/gallery/order", new { ids });
    public Task<ProfileReply> CoverSet(Guid? asset) => Send("PUT", "/api/v1/users/me/profile/cover", new { assetId = asset });
    public Task<ProfileReply> Feed(Guid user, DateTimeOffset? before = null) => Send("GET", $"/api/v1/users/{user}/profile/feed" + (before is { } b ? "?before=" + Uri.EscapeDataString(b.ToString("o", System.Globalization.CultureInfo.InvariantCulture)) : ""));
    public Task<ProfileReply> Post(string text, Guid? galleryImage, string visibility) => Send("POST", "/api/v1/users/me/profile/posts", new { text, galleryImageId = galleryImage, visibility });
    public Task<ProfileReply> DeletePost(Guid post) => Send("DELETE", $"/api/v1/users/me/profile/posts/{post}");
    public Task<ProfileReply> MarriageMine() => Send("GET", "/api/v1/marriage");
    public Task<ProfileReply> Propose(Guid user, string message) => Send("POST", "/api/v1/marriage/propose", new { userId = user, message });
    public Task<ProfileReply> Answer(Guid id, string answer) => Send("POST", $"/api/v1/marriage/{id}/{answer}", new { });
    public Task<ProfileReply> Divorce() => Send("POST", "/api/v1/marriage/divorce", new { });
}
