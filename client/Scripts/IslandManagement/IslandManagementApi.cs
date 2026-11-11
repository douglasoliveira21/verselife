using System.Text.Json;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>What the server said to one call: the body, a message the person can read, and which fields it refused.</summary>
public sealed record IslandReply(bool Ok, long Status, JsonElement Body, string Message, IReadOnlyDictionary<string, string> Fields)
{
    public bool Conflict => Status == 409;
}

/// <summary>The island as the management window shows it (everything comes from the server).</summary>
public sealed class IslandInfo
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string CoverImageUrl { get; init; } = "";
    public string Category { get; init; } = "social";
    public string ContentRating { get; init; } = "livre";
    public string Language { get; init; } = "pt-BR";
    public bool IsPublic { get; init; }
    public string AccessMode { get; init; } = "invite";
    public bool ShowInSearch { get; init; } = true;
    public bool ShowOnMap { get; init; } = true;
    public int MaxPlayers { get; init; }
    public int Size { get; init; } = 256;
    public double SpawnX { get; init; }
    public double SpawnZ { get; init; }
    public string OwnerName { get; init; } = "";
    public int AdminCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string State { get; init; } = "offline";
    public int Visitors { get; init; }
    public int Objects { get; init; }
    public int MaxObjects { get; init; }
    public long Cost { get; init; }
    public long MaxCost { get; init; }
    public bool CanEdit { get; init; }
    public bool IsOwner { get; init; }
    public int NameLimit { get; init; } = 48;
    public int DescriptionLimit { get; init; } = 500;
    public int MaxPlayersLimit { get; init; } = 256;
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<string> Ratings { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];

    public double UsagePercent => MaxCost <= 0 ? 0 : Math.Clamp(Cost * 100.0 / MaxCost, 0, 100);

    private static string Str(JsonElement e, string name, string fallback = "") => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static int Int(JsonElement e, string name, int fallback = 0) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;
    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
    private static double Dbl(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) ? n : 0;
    private static bool Flag(JsonElement e, string name, bool fallback = false) => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
    private static List<string> Strings(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Select(x => x.GetString() ?? "")] : [];
    private static DateTimeOffset Date(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetDateTimeOffset(out var d) ? d : default;

    public static IslandInfo? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var guid)) return null;
        var spawn = root.TryGetProperty("spawn", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        var usage = root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
        var options = root.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
        return new IslandInfo
        {
            Id = guid, Name = Str(root, "name"), Description = Str(root, "description"), CoverImageUrl = Str(root, "coverImageUrl"), Category = Str(root, "category", "social"), ContentRating = Str(root, "contentRating", "livre"),
            Language = Str(root, "language", "pt-BR"), IsPublic = Flag(root, "isPublic"), AccessMode = Str(root, "accessMode", "invite"), ShowInSearch = Flag(root, "showInSearch", true), ShowOnMap = Flag(root, "showOnMap", true), MaxPlayers = Int(root, "maxPlayers"), Size = Int(root, "size", 256),
            SpawnX = spawn.ValueKind == JsonValueKind.Object ? Dbl(spawn, "x") : 0, SpawnZ = spawn.ValueKind == JsonValueKind.Object ? Dbl(spawn, "z") : 0,
            OwnerName = Str(root, "ownerName"), AdminCount = Int(root, "adminCount"), CreatedAt = Date(root, "createdAt"), UpdatedAt = Date(root, "updatedAt"), State = Str(root, "state", "offline"), Visitors = Int(root, "visitors"),
            Objects = usage.ValueKind == JsonValueKind.Object ? Int(usage, "objects") : 0, MaxObjects = usage.ValueKind == JsonValueKind.Object ? Int(usage, "maxObjects") : 0, Cost = usage.ValueKind == JsonValueKind.Object ? Long(usage, "cost") : 0, MaxCost = usage.ValueKind == JsonValueKind.Object ? Long(usage, "maxCost") : 0,
            CanEdit = Flag(root, "canEdit"), IsOwner = Flag(root, "isOwner"),
            NameLimit = options.ValueKind == JsonValueKind.Object ? Int(options, "nameLimit", 48) : 48, DescriptionLimit = options.ValueKind == JsonValueKind.Object ? Int(options, "descriptionLimit", 500) : 500, MaxPlayersLimit = options.ValueKind == JsonValueKind.Object ? Int(options, "maxPlayersLimit", 256) : 256,
            Categories = options.ValueKind == JsonValueKind.Object ? Strings(options, "categories") : [], Ratings = options.ValueKind == JsonValueKind.Object ? Strings(options, "ratings") : [], Languages = options.ValueKind == JsonValueKind.Object ? Strings(options, "languages") : [],
        };
    }
}

/// <summary>The calls of the management window. Every call goes through one transport (the real one is <see cref="ScriptApi.HttpTransport"/>), so a test can answer for the server.</summary>
public sealed class IslandManagementApi(Func<string, string, string?, Task<(long Status, string Body)>> transport)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<IslandReply> Send(string method, string path, object? body = null)
    {
        var (status, text) = await transport(method, path, body is null ? null : JsonSerializer.Serialize(body, Web));
        JsonElement root = default;
        try { if (!string.IsNullOrWhiteSpace(text)) root = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
        if (status is >= 200 and < 300) return new IslandReply(true, status, root, "", new Dictionary<string, string>());
        var fields = new Dictionary<string, string>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            foreach (var property in errors.EnumerateObject()) if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0) fields[property.Name] = property.Value[0].GetString() ?? "";
        var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
        if (message.Length == 0 && fields.Count > 0) message = string.Join(" ", fields.Values);
        if (message.Length == 0) message = status switch { 0 => "Não foi possível falar com o servidor.", 401 => "Sua sessão expirou. Entre de novo.", 403 => "Você não tem permissão para fazer isso nesta ilha.", 404 => "Ilha não encontrada.", 409 => "Outra pessoa alterou a ilha. Recarregue para ver as mudanças.", _ => "O servidor não conseguiu concluir a operação." };
        return new IslandReply(false, status, root, message, fields);
    }

    public Task<IslandReply> Get(Guid island) => Send("GET", $"/api/v1/worlds/islands/{island}/management");

    public Task<IslandReply> SaveGeneral(Guid island, IslandInfo draft, DateTimeOffset expectedUpdatedAt) => Send("PUT", $"/api/v1/worlds/islands/{island}/management/general", new
    {
        name = draft.Name, description = draft.Description, coverImageUrl = string.IsNullOrEmpty(draft.CoverImageUrl) ? null : draft.CoverImageUrl, category = draft.Category, contentRating = draft.ContentRating, language = draft.Language,
        maxPlayers = draft.MaxPlayers, showInSearch = draft.ShowInSearch, showOnMap = draft.ShowOnMap, spawnX = draft.SpawnX, spawnZ = draft.SpawnZ, expectedUpdatedAt,
    });

    // ---- access ----
    public Task<IslandReply> AccessGet(Guid island, string query, int page) => Send("GET", $"/api/v1/worlds/islands/{island}/management/access?page={page}&q={Uri.EscapeDataString(query)}");
    public Task<IslandReply> AccessMode(Guid island, string mode, DateTimeOffset expectedUpdatedAt) => Send("PUT", $"/api/v1/worlds/islands/{island}/management/access/mode", new { mode, expectedUpdatedAt });
    public Task<IslandReply> AccessAllow(Guid island, string name, DateTimeOffset? expiresAt) => Send("POST", $"/api/v1/worlds/islands/{island}/management/access/allow", new { name, expiresAt });
    public Task<IslandReply> AccessChange(Guid island, Guid permission, DateTimeOffset? expiresAt) => Send("PUT", $"/api/v1/worlds/islands/{island}/management/access/allow/{permission}", new { expiresAt });
    public Task<IslandReply> AccessRemove(Guid island, Guid permission) => Send("DELETE", $"/api/v1/worlds/islands/{island}/management/access/allow/{permission}");

    // ---- bans ----
    public Task<IslandReply> BansGet(Guid island, string query, string status, int page) => Send("GET", $"/api/v1/worlds/islands/{island}/management/bans?page={page}&status={Uri.EscapeDataString(status)}&q={Uri.EscapeDataString(query)}");
    public Task<IslandReply> BanAdd(Guid island, string name, string reason, DateTimeOffset? expiresAt) => Send("POST", $"/api/v1/worlds/islands/{island}/management/bans", new { name, reason, expiresAt });
    public Task<IslandReply> BanChange(Guid island, Guid ban, DateTimeOffset? expiresAt) => Send("PUT", $"/api/v1/worlds/islands/{island}/management/bans/{ban}", new { expiresAt });
    public Task<IslandReply> BanLift(Guid island, Guid ban) => Send("DELETE", $"/api/v1/worlds/islands/{island}/management/bans/{ban}");

    // ---- permissions ----
    public Task<IslandReply> PermsGet(Guid island, string query, int page) => Send("GET", $"/api/v1/worlds/islands/{island}/management/permissions?page={page}&q={Uri.EscapeDataString(query)}");
    public Task<IslandReply> PermsAdd(Guid island, string name, IEnumerable<string> permissions) => Send("POST", $"/api/v1/worlds/islands/{island}/management/permissions", new { name, permissions });
    public Task<IslandReply> PermsChange(Guid island, Guid user, IEnumerable<string> permissions) => Send("PUT", $"/api/v1/worlds/islands/{island}/management/permissions/{user}", new { permissions });
    public Task<IslandReply> PermsClear(Guid island, Guid user) => Send("DELETE", $"/api/v1/worlds/islands/{island}/management/permissions/{user}");

    // ---- objects ----
    public Task<IslandReply> ObjectsGet(Guid island, string query, Guid? owner, string type, string scripts, string sort, int page) =>
        Send("GET", $"/api/v1/worlds/islands/{island}/management/objects?page={page}&sort={sort}&scripts={scripts}&type={Uri.EscapeDataString(type)}&q={Uri.EscapeDataString(query)}{(owner is { } o ? $"&owner={o}" : "")}");
    public Task<IslandReply> ObjectsOwners(Guid island) => Send("GET", $"/api/v1/worlds/islands/{island}/management/objects/owners");
    public Task<IslandReply> ObjectDetail(Guid island, Guid id) => Send("GET", $"/api/v1/worlds/islands/{island}/management/objects/{id}");
    public Task<IslandReply> ObjectsReturn(Guid island, IEnumerable<Guid> ids) => Send("POST", $"/api/v1/worlds/islands/{island}/management/objects/return", new { objectIds = ids });
    public Task<IslandReply> ObjectsReturnOwner(Guid island, Guid owner) => Send("POST", $"/api/v1/worlds/islands/{island}/management/objects/return-owner", new { ownerUserId = owner });

    // ---- audit ----
    public Task<IslandReply> AuditGet(Guid island, int hours, string user, string category, string query, int page) =>
        Send("GET", $"/api/v1/worlds/islands/{island}/management/audit?page={page}&hours={hours}&category={Uri.EscapeDataString(category)}&user={Uri.EscapeDataString(user)}&q={Uri.EscapeDataString(query)}");
    public Task<IslandReply> AuditDetail(Guid island, Guid id) => Send("GET", $"/api/v1/worlds/islands/{island}/management/audit/{id}");

    // ---- server ----
    public Task<IslandReply> ServerGet(Guid island) => Send("GET", $"/api/v1/worlds/islands/{island}/management/server");
    public Task<IslandReply> ServerKick(Guid island, Guid user, string reason) => Send("POST", $"/api/v1/worlds/islands/{island}/management/server/kick", new { userId = user, reason });
    public Task<IslandReply> ServerNotice(Guid island, string text, Guid? user) => Send("POST", $"/api/v1/worlds/islands/{island}/management/server/notice", new { text, userId = user });
}
