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
    public Task<ProfileReply> MarriageMine() => Send("GET", "/api/v1/marriage");
    public Task<ProfileReply> Propose(Guid user, string message) => Send("POST", "/api/v1/marriage/propose", new { userId = user, message });
    public Task<ProfileReply> Answer(Guid id, string answer) => Send("POST", $"/api/v1/marriage/{id}/{answer}", new { });
    public Task<ProfileReply> Divorce() => Send("POST", "/api/v1/marriage/divorce", new { });
}
