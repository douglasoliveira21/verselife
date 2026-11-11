using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The island management window against a pretend server: it shows the real numbers of the island, keeps what is being edited, saves only when something changed, shows what the server refused, asks before
/// throwing changes away or overwriting somebody else's, lets a person without permission read but not change, and fits small screens.
/// </summary>
public partial class IslandManagementSmoke : Node3D
{
    private static readonly Guid Island = Guid.Parse("a7f3e9d2-4b5e-4c3a-9f82-6e1b23d9f0a1");

    private sealed class Server
    {
        public string Name = "Ilha Tropical", Description = "Ilha tropical com praias.", Category = "social", Rating = "livre", Language = "pt-BR", Cover = "";
        public int MaxPlayers = 60, Visitors = 12, Cost = 12450, MaxCost = 30000; public bool Public = true, InSearch = true, OnMap = true, CanEdit = true; public double X = 128, Z = 24;
        public DateTimeOffset Updated = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero); public string State = "online";
        public readonly List<(string Method, string Path, string? Body)> Calls = [];
        public bool Offline; public bool Stale; public bool Closed;
        public sealed record Entry(Guid Id, Guid User, string Name, DateTimeOffset? Expires);
        public readonly List<Entry> Allowed = [];
        public sealed record BanRow(Guid Id, string Name, string Reason, DateTimeOffset Created, DateTimeOffset? Expires, DateTimeOffset? Lifted);
        public readonly List<BanRow> Bans = [];
        public readonly Dictionary<string, List<string>> People = [];
        public readonly Dictionary<string, Guid> Ids = [];
        public bool IsOwner = true;
        public sealed record Obj(string Name, Guid Owner, string OwnerName, string Type, int Cost, int Scripts, string Physics, int Order) { public Guid Id { get; } = Guid.NewGuid(); public DateTimeOffset Created => new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(Order); }
        public readonly List<Obj> Objs = [];
        public int Returned;
        public sealed record TerrainBackup(Guid Id, string Name, long Version, bool Valid);
        public readonly List<TerrainBackup> TerrainBackups = [];
        public readonly List<string> TerrainRestoreSent = [];
        public long TerrainVersion = 42; public bool TerrainSaved = true, TerrainRestoring;
        public sealed record PhysObj(Guid Id, string Name, string Owner, int Cost, bool Frozen);
        public readonly List<PhysObj> Phys = [];
        public bool PhysReport = true; public int PhysKept;
        public string MediaUrl = "", MediaName = ""; public double MediaVolume = .6; public bool MediaAutoplay = true, MediaStale;
        public readonly List<string> MediaSent = [];
        public string EnvLook = "{}"; public bool EnvStale;
        public readonly Dictionary<string, string> EnvCustom = [];
        public readonly List<string> EnvSent = [];
        public sealed record Rs(Guid Id, string Status, int Remaining, string Reason, DateTimeOffset Created, long? Duration, string Result);
        public Rs? RestartActive;
        public readonly List<Rs> RestartHistory = [];
        public readonly List<string> RestartSent = [];
        public sealed record Player(Guid Id, string Name, bool Staff, long Seconds);
        public readonly List<Player> Online = [];
        public readonly List<(string Name, string Reason)> Kicked = [];
        public readonly List<(string Text, string? User)> Notices = [];
        public bool ServerOnline = true, KickFails;
        public sealed record AuditRow(string Actor, string Category, string Action, string Summary, string Resource, DateTimeOffset At, object? Before, object? After) { public Guid Id { get; } = Guid.NewGuid(); }
        public readonly List<AuditRow> Audits = [];
        public string Mode => Closed ? "closed" : Public ? "public" : "invite";

        public string Json() => JsonSerializer.Serialize(new
        {
            id = Island, name = Name, description = Description, coverImageUrl = Cover.Length == 0 ? null : Cover, category = Category, contentRating = Rating, language = Language, isPublic = Public, accessMode = Mode, showInSearch = InSearch, showOnMap = OnMap,
            maxPlayers = MaxPlayers, size = 256, spawn = new { x = X, z = Z }, ownerUserId = Guid.NewGuid(), ownerName = "AliceVerse", adminCount = 3, createdAt = "2025-03-15T12:00:00Z", updatedAt = Updated, state = State, visitors = Visitors,
            usage = new { objects = 320, maxObjects = 5000, cost = Cost, maxCost = MaxCost }, canEdit = CanEdit, isOwner = CanEdit,
            options = new { categories = new[] { "social", "nature" }, ratings = new[] { "livre", "moderado", "adulto" }, languages = new[] { "pt-BR", "en-US" }, nameLimit = 48, descriptionLimit = 500, maxPlayersLimit = 256 },
        });

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add((method, path, body));
            if (Offline) return Task.FromResult<(long, string)>((0, "{}"));
            if (method == "GET" && path == $"/api/v1/worlds/islands/{Island}/management") return Task.FromResult<(long, string)>((200, Json()));
            if (method == "PUT" && path == $"/api/v1/worlds/islands/{Island}/management/general")
            {
                var sent = JsonNode.Parse(body!)!.AsObject();
                if (!CanEdit) return Task.FromResult<(long, string)>((403, "{}"));
                if (Stale) return Task.FromResult<(long, string)>((409, "{\"error\":\"stale\",\"message\":\"Outra pessoa salvou a ilha antes de você.\"}"));
                if (string.IsNullOrWhiteSpace(sent["name"]!.GetValue<string>())) return Task.FromResult<(long, string)>((400, "{\"errors\":{\"name\":[\"O nome deve ter de 1 a 48 caracteres.\"]}}"));
                Name = sent["name"]!.GetValue<string>(); Description = sent["description"]!.GetValue<string>(); Category = sent["category"]!.GetValue<string>(); Rating = sent["contentRating"]!.GetValue<string>(); Language = sent["language"]!.GetValue<string>();
                MaxPlayers = sent["maxPlayers"]!.GetValue<int>(); InSearch = sent["showInSearch"]!.GetValue<bool>(); OnMap = sent["showOnMap"]!.GetValue<bool>(); X = sent["spawnX"]!.GetValue<double>(); Z = sent["spawnZ"]!.GetValue<double>();
                Cover = sent["coverImageUrl"]?.GetValue<string>() ?? ""; Updated = Updated.AddMinutes(5);
                return Task.FromResult<(long, string)>((200, Json()));
            }
            var basePath = $"/api/v1/worlds/islands/{Island}/management/access";
            if (path.StartsWith(basePath, StringComparison.Ordinal))
            {
                var rest = path[basePath.Length..]; var query = ""; var qi = rest.IndexOf('?'); if (qi >= 0) { query = rest[(qi + 1)..]; rest = rest[..qi]; }
                if (method == "GET" && rest == "")
                {
                    var q = Uri.UnescapeDataString(query.Split('&').FirstOrDefault(x => x.StartsWith("q=", StringComparison.Ordinal))?[2..] ?? "").ToLowerInvariant();
                    var page = int.Parse(query.Split('&').First(x => x.StartsWith("page=", StringComparison.Ordinal))[5..]);
                    var shown = Allowed.Where(a => q.Length == 0 || a.Name.ToLowerInvariant().Contains(q)).ToList();
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { mode = Mode, updatedAt = Updated, total = shown.Count, page, pageSize = 10, items = shown.Skip((page - 1) * 10).Take(10).Select(a => new { permissionId = a.Id, userId = a.User, name = a.Name, username = a.Name.ToLowerInvariant(), addedAt = "2026-11-10T10:00:00Z", expiresAt = a.Expires }) })));
                }
                if (method == "PUT" && rest == "/mode")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject();
                    if (Stale) return Task.FromResult<(long, string)>((409, "{\"error\":\"stale\",\"message\":\"Outra pessoa alterou a ilha.\"}"));
                    var mode = sent["mode"]!.GetValue<string>(); Closed = mode == "closed"; if (!Closed) Public = mode == "public"; Updated = Updated.AddMinutes(1);
                    return Task.FromResult<(long, string)>((200, "{}"));
                }
                if (method == "POST" && rest == "/allow")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject(); var who = sent["name"]!.GetValue<string>();
                    if (who == "ninguem") return Task.FromResult<(long, string)>((404, "{\"message\":\"Não encontrei ninguém com esse nome.\"}"));
                    if (Allowed.Any(a => a.Name == who)) return Task.FromResult<(long, string)>((409, "{\"message\":\"Essa pessoa já está na lista.\"}"));
                    DateTimeOffset? until = sent["expiresAt"] is { } e ? e.GetValue<DateTimeOffset>() : null;
                    Allowed.Add(new Entry(Guid.NewGuid(), Guid.NewGuid(), who, until)); return Task.FromResult<(long, string)>((200, "{}"));
                }
                if (method == "PUT" && rest.StartsWith("/allow/", StringComparison.Ordinal))
                {
                    var id = Guid.Parse(rest[7..]); var sent = JsonNode.Parse(body!)!.AsObject(); var i = Allowed.FindIndex(a => a.Id == id);
                    Allowed[i] = Allowed[i] with { Expires = sent["expiresAt"] is { } e ? e.GetValue<DateTimeOffset>() : null }; return Task.FromResult<(long, string)>((200, "{}"));
                }
                if (method == "DELETE" && rest.StartsWith("/allow/", StringComparison.Ordinal)) { Allowed.RemoveAll(a => a.Id == Guid.Parse(rest[7..])); return Task.FromResult<(long, string)>((204, "")); }
            }
            var bansPath = $"/api/v1/worlds/islands/{Island}/management/bans";
            if (path.StartsWith(bansPath, StringComparison.Ordinal))
            {
                var rest = path[bansPath.Length..]; var query = ""; var qi = rest.IndexOf('?'); if (qi >= 0) { query = rest[(qi + 1)..]; rest = rest[..qi]; }
                var now = DateTimeOffset.UtcNow;
                string StateOf(BanRow b) => b.Lifted is not null ? "lifted" : b.Expires is { } e && e <= now ? "expired" : "active";
                if (method == "GET" && rest == "")
                {
                    string Arg(string key) => Uri.UnescapeDataString(query.Split('&').FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "");
                    var q = Arg("q").ToLowerInvariant(); var status = Arg("status"); var page = int.Parse(Arg("page"));
                    var shown = Bans.Where(b => (status == "" || StateOf(b) == status) && (q.Length == 0 || b.Name.ToLowerInvariant().Contains(q) || b.Reason.ToLowerInvariant().Contains(q))).OrderByDescending(b => b.Created).ToList();
                    var counts = new { active = Bans.Count(b => StateOf(b) == "active"), temporary = Bans.Count(b => StateOf(b) == "active" && b.Expires is not null), permanent = Bans.Count(b => StateOf(b) == "active" && b.Expires is null), expired = Bans.Count(b => StateOf(b) == "expired") };
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { counts, total = shown.Count, page, pageSize = 10, items = shown.Skip((page - 1) * 10).Take(10).Select(b => new { banId = b.Id, userId = Guid.NewGuid(), name = b.Name, reason = b.Reason, bannedBy = "Alice", createdAt = b.Created, expiresAt = b.Expires, liftedAt = b.Lifted, status = StateOf(b) }) })));
                }
                if (method == "POST" && rest == "")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject(); var who = sent["name"]!.GetValue<string>();
                    if (who == "ninguem") return Task.FromResult<(long, string)>((404, "{\"message\":\"Não encontrei ninguém com esse nome.\"}"));
                    if (who == "alice") return Task.FromResult<(long, string)>((409, "{\"message\":\"O proprietário e os administradores não podem ser banidos.\"}"));
                    DateTimeOffset? until = sent["expiresAt"] is { } e ? e.GetValue<DateTimeOffset>() : null;
                    Bans.Add(new BanRow(Guid.NewGuid(), who, sent["reason"]!.GetValue<string>(), DateTimeOffset.UtcNow, until, null)); return Task.FromResult<(long, string)>((200, "{}"));
                }
                if (method == "PUT" && rest.StartsWith("/", StringComparison.Ordinal))
                {
                    var id = Guid.Parse(rest[1..]); var sent = JsonNode.Parse(body!)!.AsObject(); var i = Bans.FindIndex(b => b.Id == id);
                    Bans[i] = Bans[i] with { Expires = sent["expiresAt"] is { } e ? e.GetValue<DateTimeOffset>() : null }; return Task.FromResult<(long, string)>((200, "{}"));
                }
                if (method == "DELETE" && rest.StartsWith("/", StringComparison.Ordinal)) { var id = Guid.Parse(rest[1..]); var i = Bans.FindIndex(b => b.Id == id); Bans[i] = Bans[i] with { Lifted = DateTimeOffset.UtcNow }; return Task.FromResult<(long, string)>((204, "")); }
            }
            var permsPath = $"/api/v1/worlds/islands/{Island}/management/permissions";
            if (path.StartsWith(permsPath, StringComparison.Ordinal))
            {
                var rest = path[permsPath.Length..]; var query = ""; var qi = rest.IndexOf('?'); if (qi >= 0) { query = rest[(qi + 1)..]; rest = rest[..qi]; }
                string[] Defs = ["build", "run_scripts", "edit_terrain", "ban", "admin"];
                string RoleOf(List<string> set) => set.Count == 0 ? "visitor" : set.Contains("admin") ? "admin" : set.OrderBy(x => x).SequenceEqual(["ban"]) ? "moderator" : set.OrderBy(x => x).SequenceEqual(["build", "run_scripts"]) ? "builder" : "custom";
                if (method == "GET" && rest == "")
                {
                    string Arg(string key) => Uri.UnescapeDataString(query.Split('&').FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "");
                    var q = Arg("q").ToLowerInvariant(); var page = int.Parse(Arg("page"));
                    var shown = People.Where(p => q.Length == 0 || p.Key.ToLowerInvariant().Contains(q)).OrderBy(p => p.Key).ToList();
                    var roles = new[] { ("owner", "Proprietário", Defs, 1), ("admin", "Administrador", new[] { "admin" }, People.Count(p => RoleOf(p.Value) == "admin")), ("moderator", "Moderador", new[] { "ban" }, People.Count(p => RoleOf(p.Value) == "moderator")), ("builder", "Construtor", new[] { "build", "run_scripts" }, People.Count(p => RoleOf(p.Value) == "builder")), ("visitor", "Visitante", Array.Empty<string>(), 0) };
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new
                    {
                        definitions = Defs.Select(k => new { key = k, title = k == "build" ? "Construir" : k == "run_scripts" ? "Scripts" : k == "edit_terrain" ? "Terreno" : k == "ban" ? "Banir" : "Administrar", text = "texto " + k }),
                        roles = roles.Select(r => new { id = r.Item1, title = r.Item2, permissions = r.Item3, people = r.Item4 }), total = shown.Count, page, pageSize = 10, isOwner = IsOwner,
                        items = shown.Skip((page - 1) * 10).Take(10).Select(p => new { userId = Ids.TryGetValue(p.Key, out var id) ? id : Ids[p.Key] = Guid.NewGuid(), name = p.Key, username = p.Key.ToLowerInvariant(), permissions = p.Value, role = RoleOf(p.Value) }),
                    })));
                }
                if (method == "POST" && rest == "")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject(); var who = sent["name"]!.GetValue<string>(); var set = sent["permissions"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
                    if (who == "ninguem") return Task.FromResult<(long, string)>((404, "{\"message\":\"Não encontrei ninguém com esse nome.\"}"));
                    if (who == "alice") return Task.FromResult<(long, string)>((409, "{\"message\":\"As permissões do proprietário não mudam: ele pode tudo.\"}"));
                    if (set.Contains("admin") && !IsOwner) return Task.FromResult<(long, string)>((409, "{\"message\":\"Só o proprietário pode dar ou tirar a permissão de administrador.\"}"));
                    People[who] = set; Ids[who] = Guid.NewGuid(); return Task.FromResult<(long, string)>((200, "{}"));
                }
                if (method == "PUT" && rest.StartsWith("/", StringComparison.Ordinal))
                {
                    var id = Guid.Parse(rest[1..]); var who = Ids.First(x => x.Value == id).Key; var set = JsonNode.Parse(body!)!["permissions"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
                    if (set.Contains("admin") != People[who].Contains("admin") && !IsOwner) return Task.FromResult<(long, string)>((409, "{\"message\":\"Só o proprietário pode dar ou tirar a permissão de administrador.\"}"));
                    People[who] = set; return Task.FromResult<(long, string)>((200, "{}"));
                }
                if (method == "DELETE" && rest.StartsWith("/", StringComparison.Ordinal)) { var id = Guid.Parse(rest[1..]); var who = Ids.First(x => x.Value == id).Key; People.Remove(who); return Task.FromResult<(long, string)>((204, "")); }
            }
            var objPath = $"/api/v1/worlds/islands/{Island}/management/objects";
            if (path.StartsWith(objPath, StringComparison.Ordinal))
            {
                var rest = path[objPath.Length..]; var query = ""; var qi = rest.IndexOf('?'); if (qi >= 0) { query = rest[(qi + 1)..]; rest = rest[..qi]; }
                string Arg(string key) => Uri.UnescapeDataString(query.Split('&').FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "");
                if (method == "GET" && rest == "")
                {
                    var q = Arg("q").ToLowerInvariant(); var page = int.Parse(Arg("page")); var owner = Arg("owner"); var type = Arg("type"); var scripts = Arg("scripts"); var sort = Arg("sort");
                    var shown = Objs.Where(o => (q.Length == 0 || o.Name.ToLowerInvariant().Contains(q) || o.OwnerName.ToLowerInvariant().Contains(q)) && (owner == "" || o.Owner.ToString() == owner) && (type == "" || o.Type == type) && (scripts == "" || (scripts == "with") == (o.Scripts > 0))).ToList();
                    shown = sort == "name" ? [.. shown.OrderBy(o => o.Name)] : sort == "recent" ? [.. shown.OrderByDescending(o => o.Created)] : [.. shown.OrderByDescending(o => o.Cost)];
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new
                    {
                        usage = new { objects = Objs.Count, maxObjects = 5000, cost = Objs.Sum(o => (long)o.Cost), maxCost = 30000 }, types = Objs.Select(o => o.Type).Distinct().OrderBy(t => t), total = shown.Count, page, pageSize = 10,
                        items = shown.Skip((page - 1) * 10).Take(10).Select(o => new { id = o.Id, name = o.Name, ownerUserId = o.Owner, ownerName = o.OwnerName, type = o.Type, cost = o.Cost, scripts = o.Scripts, physics = o.Physics, x = 1.0, y = 2.0, z = 3.0, locked = false }),
                    })));
                }
                if (method == "GET" && rest == "/owners")
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { items = Objs.GroupBy(o => (o.Owner, o.OwnerName)).Select(g => new { userId = g.Key.Owner, name = g.Key.OwnerName, objects = g.Count(), cost = g.Sum(o => (long)o.Cost), scripts = g.Sum(o => o.Scripts) }).OrderByDescending(g => g.cost) })));
                if (method == "GET" && rest.StartsWith("/", StringComparison.Ordinal))
                {
                    var o = Objs.FirstOrDefault(x => x.Id == Guid.Parse(rest[1..])); if (o is null) return Task.FromResult<(long, string)>((404, "{}"));
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { id = o.Id, name = o.Name, description = "Descrição de " + o.Name, type = o.Type, ownerUserId = o.Owner, ownerName = o.OwnerName, creatorName = "Criadora", lastOwnerName = (string?)null, cost = o.Cost, scripts = o.Scripts, physics = o.Physics, position = new { x = 1.0, y = 2.5, z = 3.0 }, rotation = new { x = 0, y = 90, z = 0 }, scale = new { x = 1, y = 1, z = 1 }, locked = false, createdAt = o.Created, permissions = new { modify = true, copy = false } })));
                }
                if (method == "POST" && rest == "/return")
                {
                    var ids = JsonNode.Parse(body!)!["objectIds"]!.AsArray().Select(x => Guid.Parse(x!.GetValue<string>())).ToList(); var n = Objs.RemoveAll(o => ids.Contains(o.Id)); Returned += n;
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { operation = "return", returned = n })));
                }
                if (method == "POST" && rest == "/return-owner")
                {
                    var owner = Guid.Parse(JsonNode.Parse(body!)!["ownerUserId"]!.GetValue<string>()); var n = Objs.RemoveAll(o => o.Owner == owner); Returned += n;
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { returned = n, remaining = 0 })));
                }
            }
            var auditPath = $"/api/v1/worlds/islands/{Island}/management/audit";
            if (path.StartsWith(auditPath, StringComparison.Ordinal))
            {
                var rest = path[auditPath.Length..]; var query = ""; var qi = rest.IndexOf('?'); if (qi >= 0) { query = rest[(qi + 1)..]; rest = rest[..qi]; }
                string Arg(string key) => Uri.UnescapeDataString(query.Split('&').FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "");
                if (method == "GET" && rest == "")
                {
                    var hours = int.Parse(Arg("hours")); var category = Arg("category"); var user = Arg("user").ToLowerInvariant(); var q = Arg("q").ToLowerInvariant(); var page = int.Parse(Arg("page"));
                    var shown = Audits.Where(a => (hours == 0 || a.At > DateTimeOffset.UtcNow.AddHours(-hours)) && (category == "" || a.Category == category) && (user == "" || a.Actor.ToLowerInvariant().Contains(user)) && (q == "" || a.Summary.ToLowerInvariant().Contains(q))).OrderByDescending(a => a.At).ToList();
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { total = shown.Count, page, pageSize = 10, items = shown.Skip((page - 1) * 10).Take(10).Select(a => new { id = a.Id, at = a.At, actorUserId = Guid.NewGuid(), actorName = a.Actor, category = a.Category, action = a.Action, summary = a.Summary, resource = a.Resource, result = "success" }) })));
                }
                if (method == "GET" && rest.StartsWith("/", StringComparison.Ordinal))
                {
                    var a = Audits.FirstOrDefault(x => x.Id == Guid.Parse(rest[1..])); if (a is null) return Task.FromResult<(long, string)>((404, "{}"));
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { id = a.Id, at = a.At, actorName = a.Actor, island = Name, category = a.Category, action = a.Action, summary = a.Summary, resource = a.Resource, result = "success", before = a.Before, after = a.After, correlationId = Guid.Parse("99999999-9999-9999-9999-999999999999") })));
                }
            }
            var serverPath = $"/api/v1/worlds/islands/{Island}/management/server";
            if (path.StartsWith(serverPath, StringComparison.Ordinal))
            {
                var rest = path[serverPath.Length..];
                if (method == "GET" && rest == "")
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new
                    {
                        state = ServerOnline ? "online" : "offline", running = ServerOnline, instanceId = ServerOnline ? "ab12cd34" : null, startedAt = DateTimeOffset.UtcNow.AddHours(-2), uptimeSeconds = ServerOnline ? 7200 : 0, lastHeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-5), reportedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
                        fresh = ServerOnline, visitors = Online.Count, maxPlayers = 60, closed = Closed, players = ServerOnline ? Online.Select(p => new { userId = p.Id, name = p.Name, onlineSeconds = p.Seconds, x = 10.4, y = 2.0, z = -3.6, isStaff = p.Staff }) : [],
                    })));
                if (method == "POST" && rest == "/kick")
                {
                    if (KickFails) return Task.FromResult<(long, string)>((409, "{\"message\":\"Essa pessoa não está mais na ilha.\"}"));
                    var sent = JsonNode.Parse(body!)!.AsObject(); var id = Guid.Parse(sent["userId"]!.GetValue<string>()); var who = Online.FirstOrDefault(p => p.Id == id);
                    if (who is null) return Task.FromResult<(long, string)>((409, "{\"message\":\"Essa pessoa não está mais na ilha.\"}"));
                    Kicked.Add((who.Name, sent["reason"]?.GetValue<string>() ?? "")); Online.Remove(who); return Task.FromResult<(long, string)>((202, "{\"queued\":true}"));
                }
                if (method == "POST" && rest == "/notice")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject(); Notices.Add((sent["text"]!.GetValue<string>(), sent["userId"]?.GetValue<string>()));
                    return Task.FromResult<(long, string)>((202, "{\"queued\":true}"));
                }
            }
            var perfPath = $"/api/v1/worlds/islands/{Island}/management/performance";
            if (method == "GET" && path.StartsWith(perfPath, StringComparison.Ordinal))
            {
                var hours = int.Parse(path[(path.IndexOf("hours=", StringComparison.Ordinal) + 6)..]); var count = hours == 1 ? 60 : hours == 6 ? 90 : 120;
                var t0 = DateTimeOffset.UtcNow.AddHours(-hours);
                var series = Enumerable.Range(0, ServerOnline || count > 0 ? count : 0).Select(i => new { at = t0.AddMinutes(i * hours * 60.0 / count), fps = 40.0 + i % 21, processMs = 4.0 + i % 5, memoryMb = 100.0 + i, tickRate = 60.0, players = i % 6 });
                return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new
                {
                    window = hours, running = ServerOnline, fresh = ServerOnline,
                    current = ServerOnline ? new { fps = 58.4, processMs = 4.2, memoryMb = 212.5, tickRate = 60.0, players = 3, maxPlayers = 60, uptimeSeconds = 7200, reportedAt = DateTimeOffset.UtcNow } : null,
                    scripts = new { active = 7, total = 9 }, series,
                    summary = new { fps = new { min = 40.0, avg = 50.0, max = 60.0 }, processMs = new { min = 4.0, avg = 6.0, max = 8.0 }, memoryMb = new { min = 100.0, avg = 150.0, max = 220.0 }, players = new { min = 0.0, avg = 2.5, max = 5.0 } },
                    diagnostics = new
                    {
                        scripts = new[] { new { scriptId = Guid.NewGuid(), objectId = Guid.NewGuid(), name = "Porta", script = "abrir", runs = 40, instructions = 120000L, errors = 2, lastRunAt = DateTimeOffset.UtcNow, status = "active" }, new { scriptId = Guid.NewGuid(), objectId = Guid.NewGuid(), name = "Luz", script = "piscar", runs = 10, instructions = 500L, errors = 0, lastRunAt = DateTimeOffset.UtcNow, status = "active" } },
                        objects = new[] { new { objectId = Guid.NewGuid(), name = "Casa", ownerName = "AliceVerse", cost = 300, physical = false }, new { objectId = Guid.NewGuid(), name = "Bola", ownerName = "Duda", cost = 25, physical = true } },
                    },
                })));
            }
            var restartPath = $"/api/v1/worlds/islands/{Island}/management/restart";
            if (path.StartsWith(restartPath, StringComparison.Ordinal))
            {
                var rest = path[restartPath.Length..];
                object View(Rs r) => new { id = r.Id, status = r.Status, scheduledFor = DateTimeOffset.UtcNow.AddSeconds(r.Remaining), remainingSeconds = r.Remaining, requestedBy = "AliceVerse", reason = r.Reason, createdAt = r.Created, durationSeconds = r.Duration, result = r.Result };
                if (method == "GET" && rest == "")
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { state = !ServerOnline ? "offline" : RestartActive?.Status == "running" ? "restarting" : "online", running = ServerOnline, uptimeSeconds = ServerOnline ? 7200 : 0, active = RestartActive is null ? null : View(RestartActive), history = RestartHistory.AsEnumerable().Reverse().Select(View) })));
                if (method == "POST" && rest == "")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject(); RestartSent.Add(body!);
                    if (!ServerOnline) return Task.FromResult<(long, string)>((409, "{\"message\":\"A ilha está desligada: não há o que reiniciar. Ela liga quando alguém entra.\"}"));
                    if (RestartActive is not null) return Task.FromResult<(long, string)>((409, "{\"message\":\"Já existe uma reinicialização agendada ou em andamento. Cancele ou espere terminar.\"}"));
                    var mode = sent["mode"]!.GetValue<string>(); var minutes = sent["minutes"]?.GetValue<int>();
                    var remaining = mode == "now" ? 0 : mode == "in" ? minutes!.Value * 60 : (int)(sent["at"]!.GetValue<DateTimeOffset>() - DateTimeOffset.UtcNow).TotalSeconds;
                    RestartActive = new Rs(Guid.NewGuid(), mode == "now" ? "running" : "scheduled", remaining, sent["reason"]?.GetValue<string>() ?? "", DateTimeOffset.UtcNow, null, "");
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(View(RestartActive))));
                }
                if (method == "DELETE" && rest.StartsWith("/", StringComparison.Ordinal))
                {
                    if (RestartActive is null || RestartActive.Status == "running") return Task.FromResult<(long, string)>((409, "{\"message\":\"A reinicialização já começou e não dá mais para cancelar.\"}"));
                    RestartHistory.Add(RestartActive with { Status = "cancelled", Result = "Cancelada." }); RestartActive = null; return Task.FromResult<(long, string)>((204, ""));
                }
            }
            var envPath = $"/api/v1/worlds/islands/{Island}/management/environment";
            if (path.StartsWith(envPath, StringComparison.Ordinal))
            {
                var rest = path[envPath.Length..];
                string[] BuiltIn = ["day|Dia claro|{\"hour\":12,\"weather\":\"clear\",\"light\":1}", "sunset|Pôr do sol|{\"hour\":18.5,\"weather\":\"clear\",\"light\":0.85,\"sky\":\"#FF9A5A\"}", "night|Noite|{\"hour\":0,\"light\":0.4}", "fog|Neblina|{\"hour\":7,\"weather\":\"fog\",\"fog\":0.6}", "storm|Tempestade|{\"hour\":15,\"weather\":\"storm\"}", "default|Padrão do jogo|{}"];
                string Full() => JsonSerializer.Serialize(new
                {
                    look = JsonDocument.Parse(EnvLook).RootElement, updatedAt = Updated,
                    builtIn = BuiltIn.Select(p => p.Split('|', 3)).Select(p => new { id = p[0], title = p[1], look = JsonDocument.Parse(p[2]).RootElement }),
                    custom = EnvCustom.Select(c => new { name = c.Key, look = JsonDocument.Parse(c.Value).RootElement }),
                });
                if (method == "GET" && rest == "") return Task.FromResult<(long, string)>((200, Full()));
                if (method == "PUT" && rest == "")
                {
                    EnvSent.Add(body!); if (EnvStale) return Task.FromResult<(long, string)>((409, "{\"error\":\"stale\",\"message\":\"Outra pessoa alterou a ilha antes de você.\"}"));
                    EnvLook = JsonNode.Parse(body!)!["look"]!.ToJsonString(); Updated = Updated.AddMinutes(1); return Task.FromResult<(long, string)>((200, Full()));
                }
                if (method == "POST" && rest == "/presets")
                {
                    var sent = JsonNode.Parse(body!)!; EnvCustom[sent["name"]!.GetValue<string>()] = sent["look"]!.ToJsonString(); return Task.FromResult<(long, string)>((200, Full()));
                }
                if (method == "DELETE" && rest.StartsWith("/presets/", StringComparison.Ordinal)) { EnvCustom.Remove(Uri.UnescapeDataString(rest[9..])); return Task.FromResult<(long, string)>((204, "")); }
            }
            var mediaPath = $"/api/v1/worlds/islands/{Island}/management/media";
            if (path == mediaPath)
            {
                object Doc() => new { music = MediaUrl.Length == 0 ? null : new { url = MediaUrl, volume = MediaVolume, autoplay = MediaAutoplay, name = MediaName }, updatedAt = Updated, video = new { supported = false } };
                if (method == "GET") return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(Doc())));
                if (method == "PUT")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject(); MediaSent.Add(body!);
                    if (MediaStale) return Task.FromResult<(long, string)>((409, "{\"error\":\"stale\",\"message\":\"Outra pessoa alterou a ilha antes de você.\"}"));
                    MediaUrl = sent["musicUrl"]!.GetValue<string>(); MediaVolume = sent["volume"]!.GetValue<double>(); MediaAutoplay = sent["autoplay"]!.GetValue<bool>(); MediaName = sent["name"]!.GetValue<string>(); Updated = Updated.AddMinutes(1);
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(Doc())));
                }
            }
            var physPath = $"/api/v1/worlds/islands/{Island}/management/physics";
            if (path.StartsWith(physPath, StringComparison.Ordinal))
            {
                var rest = path[physPath.Length..]; var query = ""; var qi = rest.IndexOf('?'); if (qi >= 0) { query = rest[(qi + 1)..]; rest = rest[..qi]; }
                string Arg(string key) => Uri.UnescapeDataString(query.Split('&').FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "");
                if (method == "GET" && rest == "")
                {
                    var q = Arg("q").ToLowerInvariant(); var state = Arg("state"); var page = int.Parse(Arg("page"));
                    var shown = Phys.Where(o => (state == "" || (state == "frozen") == o.Frozen) && (q.Length == 0 || o.Name.ToLowerInvariant().Contains(q) || o.Owner.ToLowerInvariant().Contains(q))).OrderByDescending(o => o.Cost).ToList();
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new
                    {
                        fresh = PhysReport, server = PhysReport ? new { bodies = Phys.Count(o => !o.Frozen), fixedBodies = 40, maxBodies = 200, physicsMs = 1.7, tickRate = 60.0 } : null,
                        summary = new { simulated = Phys.Count, active = Phys.Count(o => !o.Frozen), frozen = Phys.Count(o => o.Frozen) }, total = shown.Count, page, pageSize = 10,
                        items = shown.Skip((page - 1) * 10).Take(10).Select(o => new { id = o.Id, name = o.Name, ownerUserId = Guid.NewGuid(), ownerName = o.Owner, cost = o.Cost, state = o.Frozen ? "frozen" : "active", x = 1.0, y = 2.0, z = 3.0, locked = false }),
                    })));
                }
                if (method == "POST" && (rest == "/freeze" || rest == "/release"))
                {
                    var sent = JsonNode.Parse(body!)!.AsObject(); var all = sent["all"]!.GetValue<bool>(); var ids = sent["objectIds"]!.AsArray().Select(x => Guid.Parse(x!.GetValue<string>())).ToList(); var freeze = rest == "/freeze"; var changed = 0;
                    for (var i = 0; i < Phys.Count; i++) if (Phys[i].Frozen != freeze && (all || ids.Contains(Phys[i].Id))) { Phys[i] = Phys[i] with { Frozen = freeze }; changed++; }
                    return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { changed, restored = changed - PhysKept, kept = freeze ? 0 : Math.Min(PhysKept, changed) })));
                }
            }
            var terrainPath = $"/api/v1/worlds/islands/{Island}/management/terrain";
            if (path.StartsWith(terrainPath, StringComparison.Ordinal))
            {
                var rest = path[terrainPath.Length..];
                string Doc() => JsonSerializer.Serialize(new
                {
                    island = new { size = 256, version = TerrainVersion }, running = ServerOnline,
                    current = TerrainSaved ? new
                    {
                        version = TerrainVersion, savedAt = DateTimeOffset.UtcNow.AddSeconds(-40), hash = "ABCDEF123456",
                        stats = new { cells = 128, cellSize = 2.0, sizeMeters = 256.0, seaLevel = -0.8, minHeight = -8.0, maxHeight = 14.5, avgHeight = 1.2, vertices = 16641 },
                        preview = new { size = 4, heights = new[] { -8.0, -3.0, -3.0, -8.0, -3.0, 2.0, 6.0, -3.0, -3.0, 6.0, 14.5, -3.0, -8.0, -3.0, -3.0, -8.0 }, seaLevel = -0.8 },
                    } : null,
                    backups = TerrainBackups.Select(b => new { id = b.Id, name = b.Name, version = b.Version, savedAt = DateTimeOffset.UtcNow.AddHours(-2), backupAt = DateTimeOffset.UtcNow, by = "AliceVerse", stats = new { minHeight = -8.0, maxHeight = 9.0 }, valid = b.Valid }),
                    limits = new { maxBackups = 10, nameLimit = 80 }, restoring = TerrainRestoring ? new { restartId = Guid.NewGuid(), status = "scheduled", scheduledFor = DateTimeOffset.UtcNow.AddMinutes(5), snapshotId = Guid.NewGuid() } : null,
                });
                if (method == "GET" && rest == "") return Task.FromResult<(long, string)>((200, Doc()));
                if (method == "POST" && rest == "/backups")
                {
                    var name = JsonNode.Parse(body!)!["name"]!.GetValue<string>();
                    if (!TerrainSaved) return Task.FromResult<(long, string)>((409, "{\"message\":\"O terreno ainda não foi salvo pelo servidor da ilha; tente de novo em um minuto.\"}"));
                    if (TerrainBackups.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return Task.FromResult<(long, string)>((409, "{\"message\":\"Já existe um backup com esse nome.\"}"));
                    TerrainBackups.Add(new TerrainBackup(Guid.NewGuid(), name, TerrainVersion - 1, true)); return Task.FromResult<(long, string)>((200, Doc()));
                }
                if (method == "DELETE" && rest.StartsWith("/backups/", StringComparison.Ordinal)) { TerrainBackups.RemoveAll(b => b.Id == Guid.Parse(rest[9..])); return Task.FromResult<(long, string)>((204, "")); }
                if (method == "POST" && rest.EndsWith("/restore", StringComparison.Ordinal))
                {
                    TerrainRestoreSent.Add(body!); if (TerrainRestoring) return Task.FromResult<(long, string)>((409, "{\"message\":\"Já existe uma reinicialização agendada ou em andamento. Cancele ou espere terminar.\"}"));
                    if (!ServerOnline) { TerrainVersion++; return Task.FromResult<(long, string)>((200, "{\"applied\":true,\"scheduled\":false}")); }
                    TerrainRestoring = true; return Task.FromResult<(long, string)>((200, "{\"applied\":false,\"scheduled\":true}"));
                }
            }
            return Task.FromResult<(long, string)>((404, "{}"));
        }
    }

    public override async void _Ready()
    {
        try
        {
            var server = new Server();
            var window = new IslandManagementWindow { Name = "IslandManagementWindow", IslandProvider = () => Island, CurrentPosition = () => (40.256, -12.5) };
            window.UseTransportForTesting(server.Handle); AddChild(window); await Frames(3);

            // --- the structure ---------------------------------------------------------------------------------------------
            Require(window.MenuForTesting.SequenceEqual(["Geral", "Acesso", "Banimentos", "Permissões", "Objetos e Prims", "Mídia", "Terreno", "Ambiente", "Física", "Desempenho", "Reinicialização", "Administração", "Histórico e Auditoria"]), "the thirteen sections in order");

            // --- real numbers ----------------------------------------------------------------------------------------------
            await window.OpenAsync(); await Frames(3);
            Require(window.IsOpen && window.NameForTesting == "Ilha Tropical", "the name comes from the server");
            Require(window.IdForTesting.Contains(Island.ToString()), "the id of the island is shown");
            Require(window.VisitorsForTesting == "12 / 60" && window.StateForTesting == "Online", "visitors and state are the server's");
            var general = window.General;
            Require(general.NameForTesting == "Ilha Tropical" && general.NameCountForTesting == "13 / 48", "the name and its counter");
            Require(general.DescriptionCountForTesting == $"{server.Description.Length} / 500", "the description counter is the real length");
            Require(general.UsageTextForTesting == "30.000|12.450|17.550|41,5%", $"the prim numbers are calculated (got {general.UsageTextForTesting})");
            Require(Math.Abs(general.UsageBarForTesting - 41.5) < .01, "the bar follows the percent");
            Require(general.OwnerForTesting == "AliceVerse" && general.AdminsForTesting == "3 administradores" && general.CreatedForTesting == "15/03/2025", "owner, administrators and creation date");
            Require(general.ModeForTesting == "Pública" && general.CategoryForTesting == "social", "access mode and category");
            Require(general.EditableForTesting, "the owner can edit");
            Require(!window.SaveEnabledForTesting, "with nothing changed there is nothing to save");

            // --- a change enables save; reverting it disables again ------------------------------------------------------------
            general.NameForTesting = "Ilha Nova"; await Frames(2);
            Require(window.Dirty && window.SaveEnabledForTesting, "a change enables Salvar");
            general.NameForTesting = "Ilha Tropical"; await Frames(2);
            Require(!window.Dirty && !window.SaveEnabledForTesting, "putting it back removes the pending change");

            // --- saving ----------------------------------------------------------------------------------------------------
            general.NameForTesting = "Ilha Boa"; general.CapacityForTesting = 40; general.InSearchForTesting = false; general.UseMyPositionForTesting(); await Frames(2);
            Require(Math.Abs(general.SpawnXForTesting - 40.26) < .01, "Usar Minha Posição fills the spot from the avatar");
            var renamed = ""; window.IslandRenamed += (_, name) => renamed = name;
            await window.SaveAsync(); await Frames(2);
            Require(server.Name == "Ilha Boa" && server.MaxPlayers == 40 && !server.InSearch && Math.Abs(server.X - 40.26) < .01, "the server received what was edited");
            Require(window.NameForTesting == "Ilha Boa" && window.VisitorsForTesting == "12 / 40" && !window.Dirty, "after saving the screen shows what the server kept");
            Require(window.ToastForTesting.Contains("sucesso") && renamed == "Ilha Boa", "success is announced after the server answered, and the rest of the game hears about the new name");
            var saves = server.Calls.Count(c => c.Method == "PUT");
            await window.SaveAsync(); Require(server.Calls.Count(c => c.Method == "PUT") == saves, "saving with nothing changed sends nothing");

            // --- refused values ----------------------------------------------------------------------------------------------
            general.NameForTesting = "   "; await Frames(2); await window.SaveAsync(); await Frames(2);
            Require(window.ToastForTesting.Contains("Não foi possível salvar") && window.ToastForTesting.Contains("1 a 48"), "the reason of the server is shown");
            Require(window.Dirty && server.Name == "Ilha Boa", "a refused save keeps the draft and changes nothing on the server");

            // --- somebody else saved first -----------------------------------------------------------------------------------
            general.NameForTesting = "Minha Versão"; server.Stale = true; await Frames(2); await window.SaveAsync(); await Frames(2);
            Require(window.ConfirmOpenForTesting && server.Name == "Ilha Boa", "a conflict asks before reloading and overwrites nothing");
            server.Stale = false; window.CancelConfirmForTesting(); Require(window.Dirty, "cancelling keeps my draft");

            // --- closing with a pending change asks ------------------------------------------------------------------------
            window.PressCloseForTesting(); await Frames(2);
            Require(window.IsOpen && window.ConfirmOpenForTesting, "closing with a pending change asks first");
            window.CancelConfirmForTesting(); Require(window.IsOpen && window.Dirty, "'cancel' keeps the window and the draft");
            window.PressCloseForTesting(); window.ConfirmForTesting(); await Frames(2);
            Require(!window.IsOpen, "confirming closes");
            await window.OpenAsync(); await Frames(3);
            Require(!window.Dirty && window.NameForTesting == "Ilha Boa" && window.General.NameForTesting == "Ilha Boa", "the discarded draft is gone");

            // --- restore defaults only touches what has a default ------------------------------------------------------------
            window.PressRestoreForTesting(); window.ConfirmForTesting(); await Frames(2);
            Require(window.General.CapacityForTesting == 60 || window.General.CapacityForTesting == 64, "capacity goes back to the default");
            Require(window.General.NameForTesting == "Ilha Boa" && window.General.InSearchForTesting, "the name stays and the visibility goes back on");
            Require(window.Dirty && window.SaveEnabledForTesting, "restored values still have to be saved");
            window.General.Revert(); await Frames(2);

            // --- Acesso ----------------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("acesso"); await Frames(3);
            var access = window.Access;
            Require(window.PageVisibleForTesting("acesso") && access.SavedMode == "public" && access.SummaryForTesting.StartsWith("Ilha pública"), "the Acesso tab shows the mode of the island");
            Require(access.TotalForTesting == 0 && access.RowsForTesting == 0, "an empty list is shown as empty");
            access.NameForTesting = "ninguem"; await access.AddAsync(); await Frames(2);
            Require(access.StatusForTesting.Contains("Não encontrei") && access.TotalForTesting == 0, "an unknown name is refused with the reason of the server");
            foreach (var who in new[] { "Carla", "Bruno" }) { access.NameForTesting = who; await access.AddAsync(); await Frames(2); }
            Require(access.TotalForTesting == 2 && access.RowsForTesting == 2, "people are added to the list");
            access.NameForTesting = "Carla"; await access.AddAsync(); await Frames(2);
            Require(access.StatusForTesting.Contains("já está") && access.TotalForTesting == 2, "the same person twice is refused");
            access.NameForTesting = "Duda"; access.SelectExpiryForTesting(2); await access.AddAsync(); await Frames(2);
            Require(access.RowTextsForTesting.Count(t => t.Contains("Não expira")) == 2 && access.RowTextsForTesting.Count(t => t.Contains("Duda") && !t.Contains("Não expira")) == 1, "an entry with an expiry shows its date and the others say they do not expire");
            access.SearchForTesting = "carl"; await access.LoadAsync(); await Frames(2);
            Require(access.RowsForTesting == 1, "the search narrows the list"); access.SearchForTesting = ""; await access.LoadAsync(); await Frames(2);
            for (var i = 0; i < 12; i++) { access.NameForTesting = $"Pessoa{i:00}"; access.SelectExpiryForTesting(0); await access.AddAsync(); await Frames(1); }
            await Frames(2);
            Require(access.TotalForTesting == 15 && access.RowsForTesting == 10 && access.PageTextForTesting == "Página 1 de 2", $"the list is paged ({access.TotalForTesting}, {access.RowsForTesting}, {access.PageTextForTesting})");
            await access.RemoveFirstForTesting(); await Frames(2);
            Require(access.TotalForTesting == 14, "a person can be removed");
            await access.ChangeFirstForTesting(TimeSpan.FromDays(7)); await Frames(2);
            Require(access.StatusForTesting.Contains("alterada"), "the expiry can be changed");
            Require(!window.Dirty, "nothing is pending before the mode is touched");
            access.PickModeForTesting("invite"); await Frames(2);
            Require(window.Dirty && window.SaveEnabledForTesting && access.SummaryForTesting.Contains("Somente convidados"), "picking a mode is a draft that enables Salvar");
            await window.SaveAsync(); await Frames(3);
            Require(server.Mode == "invite" && !window.Dirty && window.Info!.AccessMode == "invite" && window.ToastForTesting.Contains("sucesso"), "saving sends the mode and the screen shows what the server kept");
            access.PickModeForTesting("closed"); window.General.NameForTesting = "Combo"; await window.SaveAsync(); await Frames(3);
            Require(server.Mode == "closed" && server.Name == "Combo" && !window.Dirty, "the General tab and the mode are saved together");
            access.PickModeForTesting("public"); server.Stale = true; await window.SaveAsync(); await Frames(2);
            Require(window.ConfirmOpenForTesting && server.Mode == "closed", "a conflict on the mode asks before reloading and changes nothing");
            server.Stale = false; window.CancelConfirmForTesting(); access.Revert(); window.General.NameForTesting = "Ilha Boa"; access.PickModeForTesting("public"); await window.SaveAsync(); await Frames(3);
            Require(server.Mode == "public" && server.Name == "Ilha Boa" && !window.Dirty, "back to the original");
            window.SelectSectionForTesting("geral");

            // --- Banimentos ------------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("banimentos"); await Frames(3);
            var bans = window.Bans;
            Require(window.PageVisibleForTesting("banimentos") && bans.TotalForTesting == 0 && bans.CountersForTesting == "0|0|0|0", "the Banimentos tab starts empty with zero counters");
            bans.NameForTesting = ""; bans.PressBanForTesting(); await Frames(2);
            Require(bans.StatusForTesting.Contains("Digite o nome") && server.Bans.Count == 0, "a ban needs a name");
            bans.NameForTesting = "Carla"; bans.ReasonForTesting = ""; bans.PressBanForTesting(); await Frames(2);
            Require(bans.StatusForTesting.Contains("motivo") && server.Bans.Count == 0, "a ban needs a reason");
            bans.ReasonForTesting = "Spam"; bans.PressBanForTesting(); await Frames(2);
            Require(window.ConfirmOpenForTesting && server.Bans.Count == 0, "banning asks first and sends nothing yet");
            window.CancelConfirmForTesting(); Require(server.Bans.Count == 0, "cancelling bans nobody");
            bans.PressBanForTesting(); window.ConfirmForTesting(); await Frames(4);
            Require(server.Bans.Count == 1 && bans.TotalForTesting == 1 && bans.CountersForTesting == "1|1|0|0", $"after confirming the person is banned and the counters follow ({bans.CountersForTesting})");
            Require(bans.RowTextsForTesting[0].Contains("Carla") && bans.RowTextsForTesting[0].Contains("Spam") && bans.RowTextsForTesting[0].Contains("Ativo") && bans.RowTextsForTesting[0].Contains("Alice"), "the row shows who, why, by whom and the state");
            bans.NameForTesting = "alice"; bans.ReasonForTesting = "x"; await bans.BanAsync(); await Frames(2);
            Require(bans.StatusForTesting.Contains("não podem ser banidos") && server.Bans.Count == 1, "the refusal of the server is shown");
            bans.NameForTesting = "ninguem"; await bans.BanAsync(); await Frames(2); Require(bans.StatusForTesting.Contains("Não encontrei"), "an unknown name is refused");
            bans.SelectDurationForTesting(4); bans.NameForTesting = "Bruno"; bans.ReasonForTesting = "Fraude"; await bans.BanAsync(); await Frames(3);
            Require(bans.CountersForTesting == "2|1|1|0" && bans.RowTextsForTesting.Any(t => t.Contains("Permanente")), "a permanent ban is counted and shown as permanent");
            await bans.ChangeFirstForTesting(TimeSpan.FromDays(7)); await Frames(3);
            Require(bans.CountersForTesting == "2|2|0|0", "changing the duration updates the counters");
            bans.SearchForTesting = "fraude"; await bans.LoadAsync(); await Frames(2);
            Require(bans.RowsForTesting == 1, "the search looks in the reason too"); bans.SearchForTesting = ""; await bans.LoadAsync(); await Frames(2);
            await bans.LiftFirstForTesting(); await Frames(3);
            Require(bans.CountersForTesting == "1|1|0|0" && bans.TotalForTesting == 2, "lifting a ban keeps it in the history and takes it out of the active counters");
            bans.SelectFilterForTesting(3); await bans.LoadAsync(); await Frames(2);
            Require(bans.RowsForTesting == 1 && bans.RowTextsForTesting[0].Contains("Desbanido"), "the filter shows only the lifted ones");
            bans.SelectFilterForTesting(0);
            for (var i = 0; i < 12; i++) { bans.NameForTesting = $"Pessoa{i:00}"; bans.ReasonForTesting = "Teste"; await bans.BanAsync(); await Frames(1); }
            await bans.LoadAsync(); await Frames(2);
            Require(bans.TotalForTesting == 14 && bans.RowsForTesting == 10 && bans.PageTextForTesting == "Página 1 de 2", "the list is paged");
            window.SelectSectionForTesting("geral");

            // --- Permissões ------------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("permissoes"); await Frames(3);
            var perms = window.Permissions;
            Require(window.PageVisibleForTesting("permissoes") && perms.DefinitionsForTesting == 5 && perms.TotalForTesting == 0, "the Permissões tab shows the permissions the server offers and starts empty");
            Require(perms.RolesForTesting.Contains("Proprietário") && perms.RolesForTesting.Contains("Construtor"), "the roles are listed");
            perms.NameForTesting = ""; await perms.AddAsync(); Require(perms.StatusForTesting.Contains("Digite o nome"), "a person needs a name");
            perms.NameForTesting = "ninguem"; await perms.AddAsync(); await Frames(2); Require(perms.StatusForTesting.Contains("Não encontrei"), "an unknown name is refused with the reason");
            perms.NameForTesting = "alice"; await perms.AddAsync(); await Frames(2); Require(perms.StatusForTesting.Contains("proprietário") && server.People.Count == 0, "the owner cannot be given permissions");
            perms.SelectRoleForTesting(1); perms.NameForTesting = "Beto"; await perms.AddAsync(); await Frames(3);
            Require(server.People["Beto"].OrderBy(x => x).SequenceEqual(["build", "run_scripts"]) && perms.TotalForTesting == 1 && perms.RowTextsForTesting[0].Contains("Construtor"), "a person is added with the set of the chosen role");
            Require(perms.RolesForTesting.Contains("Construtor 1"), "the role counts one person");
            await perms.ToggleFirstForTesting("ban", true); await Frames(3);
            Require(server.People["Beto"].Contains("ban") && perms.RowTextsForTesting[0].Contains("Personalizado"), "a switch sends the whole set and the role becomes custom");
            await perms.ToggleFirstForTesting("ban", false); await perms.ToggleFirstForTesting("run_scripts", false); await Frames(3);
            Require(server.People["Beto"].SequenceEqual(["build"]) && perms.SetOfFirstForTesting.SequenceEqual(["build"]), "switching off takes just that one away");
            server.IsOwner = false; perms.SelectRoleForTesting(2); perms.NameForTesting = "Carla"; await perms.AddAsync(); await Frames(3);
            Require(perms.StatusForTesting.Contains("Só o proprietário") && !server.People.ContainsKey("Carla"), "only the owner can give administrator");
            await perms.ToggleFirstForTesting("admin", true); await Frames(3);
            Require(perms.StatusForTesting.Contains("Só o proprietário") && !server.People["Beto"].Contains("admin") && !perms.AdminSwitchEnabledForTesting, "the refused switch goes back and the administrator switch is off for a non-owner");
            server.IsOwner = true; await perms.LoadAsync(); await Frames(2);
            Require(perms.AdminSwitchEnabledForTesting, "the owner can use the administrator switch");
            perms.SearchForTesting = "zzz"; await perms.LoadAsync(); await Frames(2); Require(perms.RowsForTesting == 0, "the search narrows the list"); perms.SearchForTesting = ""; await perms.LoadAsync(); await Frames(2);
            for (var i = 0; i < 12; i++) { perms.NameForTesting = $"Pessoa{i:00}"; perms.SelectRoleForTesting(0); await perms.AddAsync(); await Frames(1); }
            await perms.LoadAsync(); await Frames(2);
            Require(perms.TotalForTesting == 13 && perms.RowsForTesting == 10 && perms.PageTextForTesting == "Página 1 de 2", "the list is paged");
            await perms.ClearFirstForTesting(); await Frames(3);
            Require(perms.TotalForTesting == 12, "all the permissions of a person can be taken away");
            window.SelectSectionForTesting("geral");

            // --- Objetos e Prims -------------------------------------------------------------------------------------------
            var carlaId = Guid.NewGuid(); var dudaId = Guid.NewGuid();
            server.Objs.AddRange([new("Árvore", carlaId, "Carla", "mesh", 40, 2, "static", 1), new("Banco", carlaId, "Carla", "cube", 10, 0, "static", 2), new("Bola", dudaId, "Duda", "sphere", 25, 0, "physical", 3), new("Casa", Guid.NewGuid(), "AliceVerse", "mesh", 300, 0, "static", 4)]);
            window.SelectSectionForTesting("objetos"); await Frames(4);
            var objects = window.Objects;
            Require(window.PageVisibleForTesting("objetos") && objects.TotalForTesting == 4, "the Objetos tab lists the objects");
            Require(objects.StatsForTesting == "30.000|375|29.625|4", $"the budget numbers are the server's ({objects.StatsForTesting})");
            Require(Math.Abs(objects.UsageBarForTesting - 1.25) < .01, "the bar follows the percent");
            Require(objects.RowTextsForTesting[0].Contains("Casa") && objects.RowTextsForTesting[0].Contains("AliceVerse") && objects.RowTextsForTesting[0].Contains("300"), "the biggest object comes first with its owner and prims");
            Require(objects.RowTextsForTesting.Any(t => t.Contains("Árvore") && t.Contains("Modelo")) && objects.RowTextsForTesting.Any(t => t.Contains("Bola") && t.Contains("Físico")), "type and physics are shown in words");
            Require(objects.OwnerOptionsForTesting.Contains("Carla") && objects.OwnerOptionsForTesting.Count == 4, "the owner filter lists the owners");
            objects.SearchForTesting = "carla"; await objects.LoadAsync(); await Frames(2); Require(objects.TotalForTesting == 2, "search finds by owner"); objects.SearchForTesting = "";
            objects.SelectScriptsForTesting(1); await objects.LoadAsync(); await Frames(2); Require(objects.TotalForTesting == 1, "the scripts filter works"); objects.SelectScriptsForTesting(0);
            objects.SelectTypeForTesting(1); await objects.LoadAsync(); await Frames(2); Require(objects.TotalForTesting == 1, "the type filter works"); objects.SelectTypeForTesting(0);
            objects.SelectSortForTesting(2); await objects.LoadAsync(); await Frames(2); Require(objects.RowTextsForTesting[0].Contains("Árvore"), "the order can be changed"); objects.SelectSortForTesting(0);
            await objects.LoadAsync(); await Frames(2);
            await objects.SelectFirstForTesting(); await Frames(3);
            Require(objects.DetailForTesting.Contains("Casa") && objects.DetailForTesting.Contains("AliceVerse") && objects.DetailForTesting.Contains("1, 2,5, 3") && objects.DetailForTesting.Contains("Descrição de Casa"), "the detail panel shows the object");
            await objects.ReturnSelectedForTesting(); await Frames(4);
            Require(server.Returned == 1 && objects.TotalForTesting == 3 && objects.StatsForTesting.StartsWith("30.000|75|"), "returning an object takes it off the list and gives its prims back");
            Require(objects.StatusForTesting.Contains("devolvido"), "the result is told");
            objects.ShowOwnersForTesting(true); await objects.LoadOwnersAsync(); await Frames(3);
            Require(objects.OwnersViewForTesting && objects.OwnerRowsForTesting >= 2 && objects.OwnerRowTextsForTesting.Any(t => t.Contains("Carla") && t.Contains("50")), "the consumption by owner adds objects and prims per person");
            await objects.ReturnFirstOwnerForTesting(); await Frames(4);
            Require(server.Objs.Count == 1 && server.Returned == 3, "returning everything of one owner leaves the others");
            objects.ShowOwnersForTesting(false);
            for (var i = 0; i < 12; i++) server.Objs.Add(new($"Extra{i}", dudaId, "Duda", "cube", 1, 0, "static", 10 + i));
            await objects.ReloadAllAsync(); await Frames(3);
            Require(objects.TotalForTesting == 13 && objects.RowsForTesting == 10 && objects.PageTextForTesting == "Página 1 de 2", "the list is paged");
            window.SelectSectionForTesting("geral");

            // --- Histórico e Auditoria -------------------------------------------------------------------------------------
            var seeded = DateTimeOffset.UtcNow;
            server.Audits.Add(new("AliceVerse", "geral", "settings_changed", "Alterou as configurações gerais (nome, capacidade)", "Ilha Tropical", seeded.AddMinutes(-5), new { name = "Ilha Velha", maxPlayers = 60 }, new { name = "Ilha Tropical", maxPlayers = 40 }));
            server.Audits.Add(new("Bruno", "banimento", "ban_added", "Baniu Carla", "Carla", seeded.AddMinutes(-30), null, new { reason = "Spam", expiresAt = (string?)null }));
            server.Audits.Add(new("AliceVerse", "acesso", "mode_changed", "Mudou o acesso de Pública para Somente convidados", "Ilha Tropical", seeded.AddHours(-2), new { mode = "public" }, new { mode = "invite" }));
            server.Audits.Add(new("Carla", "construcao", "create", "Criou o objeto Mesa", "Mesa", seeded.AddDays(-3), null, new { name = "Mesa" }));
            server.Audits.Add(new("AliceVerse", "permissao", "permissions_changed", "Mudou as permissões de Beto", "Beto", seeded.AddDays(-40), new { permissions = new[] { "build" } }, new { permissions = new[] { "build", "ban" } }));
            for (var i = 0; i < 9; i++) server.Audits.Add(new("Bruno", "objeto", "objects_returned", $"Devolveu o objeto Peça{i}", $"Peça{i}", seeded.AddDays(-50 - i), null, new { count = 1 }));
            window.SelectSectionForTesting("auditoria"); await Frames(4);
            var audit = window.Audit;
            Require(window.PageVisibleForTesting("auditoria") && audit.TotalForTesting == 14 && audit.RowsForTesting == 10 && audit.PageTextForTesting == "Página 1 de 2", "the history lists the events, newest first, in pages");
            Require(audit.RowTextsForTesting[0].Contains("AliceVerse") && audit.RowTextsForTesting[0].Contains("Alterou as configurações gerais") && audit.RowTextsForTesting[0].Contains("Sucesso"), "a row shows who, what and the result");
            audit.SelectPeriodForTesting(1); await audit.ApplyForTesting(); await Frames(2); Require(audit.TotalForTesting == 3, "the period filter works");
            audit.SelectPeriodForTesting(3); await audit.ApplyForTesting(); await Frames(2); Require(audit.TotalForTesting == 4, "30 days leaves out older events");
            audit.SelectPeriodForTesting(0); audit.SelectCategoryForTesting(3); await audit.ApplyForTesting(); await Frames(2); Require(audit.TotalForTesting == 1 && audit.RowTextsForTesting[0].Contains("Baniu Carla"), "the type filter works");
            audit.SelectCategoryForTesting(0); audit.UserForTesting = "carla"; await audit.ApplyForTesting(); await Frames(2); Require(audit.TotalForTesting == 1, "the person filter works");
            audit.UserForTesting = ""; audit.SearchForTesting = "mesa"; await audit.ApplyForTesting(); await Frames(2); Require(audit.TotalForTesting == 1, "the text search works");
            audit.SearchForTesting = "nada disso existe"; await audit.ApplyForTesting(); await Frames(2); Require(audit.TotalForTesting == 0 && audit.RowsForTesting == 0, "no match shows an empty list");
            audit.SearchForTesting = ""; await audit.ApplyForTesting(); await Frames(2);
            await audit.SelectFirstForTesting(); await Frames(3);
            var shownDetail = audit.DetailForTesting;
            Require(shownDetail.Contains("Ilha Tropical") && shownDetail.Contains("Sucesso") && shownDetail.Contains("99999999-9999-9999-9999-999999999999"), "the detail shows the event, the island, the result and the correlation id");
            Require(shownDetail.Contains("Valores anteriores") && shownDetail.Contains("Nome: Ilha Velha") && shownDetail.Contains("Capacidade: 60") && shownDetail.Contains("Valores novos") && shownDetail.Contains("Nome: Ilha Tropical") && shownDetail.Contains("Capacidade: 40"), "the detail shows the values before and after");
            audit.SelectPeriodForTesting(0); audit.SelectCategoryForTesting(0); await audit.ApplyForTesting(); await Frames(2);
            window.SelectSectionForTesting("geral");

            // --- Administração ---------------------------------------------------------------------------------------------
            server.Online.AddRange([new(Guid.NewGuid(), "Carla", false, 125), new(Guid.NewGuid(), "Duda", false, 4000), new(Guid.NewGuid(), "AliceVerse", true, 90000)]);
            window.SelectSectionForTesting("administracao"); await Frames(4);
            var admin = window.Administration;
            Require(window.PageVisibleForTesting("administracao") && admin.StateForTesting == "Online" && admin.PlayersForTesting == 3, "the Administração tab shows the server state and who is connected");
            Require(admin.InfoForTesting == "2 h 00 min|ab12cd34|3 / 60", $"uptime, server id and visitors come from the server ({admin.InfoForTesting})");
            Require(admin.RowTextsForTesting[0].Contains("Carla") && admin.RowTextsForTesting[0].Contains("2 min") && admin.RowTextsForTesting[2].Contains("Equipe") && admin.RowTextsForTesting[2].Contains("1 d 1 h"), "each person shows the time online and the staff are marked");
            Require(!admin.CloseForTestingDisabled() , "closing is possible while the island is open");
            admin.NoticeForTesting = ""; await admin.SendNoticeForTesting(); Require(admin.StatusForTesting.Contains("Escreva o aviso") && server.Notices.Count == 0, "an empty notice is not sent");
            admin.NoticeForTesting = "Reinício em 5 minutos"; await admin.SendNoticeForTesting(); await Frames(2);
            Require(server.Notices.Count == 1 && server.Notices[0].Text == "Reinício em 5 minutos" && server.Notices[0].User is null && admin.NoticeForTesting == "", "a notice for everybody is sent and the box is cleared");
            admin.NoticeForTesting = "Só para você"; await admin.NoticeToFirstForTesting(); await Frames(2);
            Require(server.Notices.Count == 2 && server.Notices[1].User is not null, "a notice for one person carries who");
            admin.ReasonForTesting = "Xingamentos"; server.KickFails = true; await admin.KickFirstForTesting(); await Frames(2);
            Require(admin.StatusForTesting.Contains("não está mais na ilha") && server.Kicked.Count == 0, "a refusal of the server is shown and nothing is removed");
            server.KickFails = false; await admin.KickFirstForTesting(); await Frames(3);
            Require(server.Kicked.Count == 1 && server.Kicked[0] == ("Carla", "Xingamentos") && admin.PlayersForTesting == 2, "removing sends the reason and the list updates");
            admin.ReasonForTesting = "Fraude"; admin.SelectBanLengthForTesting(3); await admin.BanFirstForTesting(); await Frames(3);
            Require(server.Bans.Any(b => b.Name == "Duda" && b.Reason == "Fraude" && b.Expires is null) && server.Kicked.Count == 2 && admin.PlayersForTesting == 1, "banning creates the ban and removes the person");
            Require(admin.StatusForTesting.Contains("banida"), "the result is told");
            await admin.CloseForTesting(true); await Frames(3);
            Require(server.Mode == "closed" && !admin.CloseEnabledForTesting && admin.ReopenEnabledForTesting && window.Info!.AccessMode == "closed", "closing the island uses the access mode and the screen follows");
            await admin.CloseForTesting(false); await Frames(3);
            Require(server.Mode == "public" && admin.CloseEnabledForTesting && !admin.ReopenEnabledForTesting, "reopening brings the mode back");
            window.General.NameForTesting = "Mudou"; await admin.CloseForTesting(true); await Frames(2);
            Require(admin.StatusForTesting.Contains("Salve ou descarte") && server.Mode == "public", "closing is refused while there are unsaved changes");
            window.General.Revert(); await Frames(2);
            server.ServerOnline = false; await admin.LoadAsync(); await Frames(3);
            Require(admin.StateForTesting == "Desligada" && admin.PlayersForTesting == 0, "an island without a server shows that and no people");
            server.ServerOnline = true; await admin.LoadAsync(); await Frames(2);
            window.SelectSectionForTesting("geral");

            // --- Desempenho ------------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("desempenho"); await Frames(4);
            var perf = window.Performance;
            Require(window.PageVisibleForTesting("desempenho") && perf.IndicatorsForTesting == "58,4|60|4,2 ms|212,5 MB|3 / 60|7", $"the indicators are what the server reported ({perf.IndicatorsForTesting})");
            Require(perf.ChartPointsForTesting.All(p => p == 60), "each chart gets the points of the last hour");
            Require(perf.ChartNotesForTesting[0] == "mín 40 · média 50 · máx 60" && perf.ChartNotesForTesting[1].EndsWith("MB"), "each chart says its lowest, average and highest value");
            perf.SelectPeriodForTesting(2); await perf.LoadAsync(); await Frames(3);
            Require(perf.ChartPointsForTesting.All(p => p == 120), "the period changes the points");
            Require(perf.RowsForTesting == 8 && perf.RowTextsForTesting[0].Contains("Porta") && perf.RowTextsForTesting[0].Contains("120.000 instruções") && perf.RowTextsForTesting[0].Contains("2 com erro"), "the diagnosis lists scripts first, with runs and errors");
            Require(perf.RowTextsForTesting.Any(t => t.Contains("Bola") && t.Contains("Física ativa")) && perf.RowTextsForTesting.Any(t => t.Contains("Casa") && t.Contains("300 prims")), "and the heaviest objects");
            perf.SelectFilterForTesting(1); await Frames(2); Require(perf.RowsForTesting == 4 && perf.RowTextsForTesting.Where(t => t.Length > 0).All(t => t.Contains("Script")), "the filter shows only scripts");
            perf.SelectFilterForTesting(2); await Frames(2); Require(perf.RowsForTesting == 4 && perf.RowTextsForTesting.Where(t => t.Length > 0).All(t => t.Contains("Objeto")), "or only objects"); perf.SelectFilterForTesting(0);
            server.ServerOnline = false; await perf.LoadAsync(); await Frames(3);
            Require(perf.IndicatorsForTesting.StartsWith("—|—|—|—|—|") && perf.StatusForTesting.Contains("desligado"), "with the server off the figures are blank and it says so");
            server.ServerOnline = true; perf.SelectPeriodForTesting(0); await perf.LoadAsync(); await Frames(2);
            window.SelectSectionForTesting("geral");

            // --- Reinicialização -------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("reinicializacao"); await Frames(4);
            var restart = window.Restart;
            Require(window.PageVisibleForTesting("reinicializacao") && restart.StateForTesting == "Online" && restart.UptimeForTesting == "2 h 00 min" && !restart.ActiveVisibleForTesting && restart.HistoryForTesting == 0 && restart.RequestEnabledForTesting, "the Reinicialização tab shows the state and nothing scheduled");
            restart.SelectOptionForTesting(0); restart.PressRequestForTesting(); await Frames(2);
            Require(window.ConfirmOpenForTesting && server.RestartSent.Count == 0, "asking to restart asks first and sends nothing yet");
            window.CancelConfirmForTesting(); Require(server.RestartSent.Count == 0, "cancelling the question restarts nothing");
            restart.PressRequestForTesting(); window.ConfirmForTesting(); await Frames(4);
            Require(server.RestartSent.Count == 1 && server.RestartSent[0].Contains("\"mode\":\"now\"") && restart.ActiveVisibleForTesting && restart.CountdownForTesting == "Em curso" && !restart.CancelVisibleForTesting && !restart.RequestEnabledForTesting && restart.StateForTesting == "Reiniciando", "an immediate restart is shown as running, cannot be cancelled and blocks another request");
            server.RestartHistory.Add(server.RestartActive! with { Status = "completed", Duration = 45, Result = "A ilha reiniciou e voltou ao ar." }); server.RestartActive = null; await restart.LoadAsync(); await Frames(3);
            Require(!restart.ActiveVisibleForTesting && restart.StateForTesting == "Online" && restart.HistoryForTesting == 1 && restart.HistoryTextsForTesting[0].Contains("Concluída") && restart.HistoryTextsForTesting[0].Contains("45 s") && restart.RequestEnabledForTesting, "when it finishes the history shows the result and the duration");
            restart.SelectOptionForTesting(2); restart.ReasonForTesting = "Atualização"; await restart.RequestForTesting(); await Frames(4);
            Require(server.RestartSent[1].Contains("\"minutes\":5") && server.RestartActive!.Reason == "Atualização" && restart.ActiveVisibleForTesting && restart.CountdownForTesting == "05:00" && restart.CancelVisibleForTesting, "a restart in 5 minutes shows a countdown and a cancel button");
            Require(restart.ActiveTextForTesting.Contains("Atualização") && restart.ActiveTextForTesting.Contains("AliceVerse"), "it says who asked and why");
            await restart.RequestForTesting(); await Frames(2);
            Require(restart.StatusForTesting.Contains("Já existe") && server.RestartSent.Count == 3, "a second request is refused with the reason of the server");
            await restart.CancelForTesting(); await Frames(3);
            Require(!restart.ActiveVisibleForTesting && restart.HistoryForTesting == 2 && restart.HistoryTextsForTesting[0].Contains("Cancelada"), "cancelling a scheduled restart puts it in the history");
            restart.SelectOptionForTesting(4); await Frames(1); Require(restart.AtVisibleForTesting && restart.AtForTesting.Length > 0, "scheduling a time shows the time field with a suggestion");
            restart.AtForTesting = "amanhã de manhã"; await restart.RequestForTesting(); Require(restart.StatusForTesting.Contains("25/12/2026 03:30") && server.RestartSent.Count == 3, "a time that is not understood is not sent");
            restart.AtForTesting = DateTime.Now.AddHours(2).ToString("dd/MM/yyyy HH:mm"); await restart.RequestForTesting(); await Frames(3);
            Require(server.RestartSent.Count == 4 && server.RestartSent[3].Contains("\"mode\":\"at\"") && restart.ActiveVisibleForTesting && restart.ActiveTextForTesting.Contains("Agendada para"), "a time is sent and the restart is scheduled");
            await restart.CancelForTesting(); await Frames(3);
            server.RestartHistory.Add(new Server.Rs(Guid.NewGuid(), "failed", 0, "", DateTimeOffset.UtcNow, null, "O servidor da ilha não obedeceu ao pedido (versão antiga?).")); await restart.LoadAsync(); await Frames(3);
            Require(restart.HistoryTextsForTesting[0].Contains("Falhou") && restart.HistoryTextsForTesting[0].Contains("não obedeceu"), "a failure shows why");
            server.ServerOnline = false; await restart.LoadAsync(); await Frames(3);
            Require(restart.StateForTesting == "Desligada" && !restart.RequestEnabledForTesting, "an island that is off cannot be restarted");
            server.ServerOnline = true; restart.SelectOptionForTesting(2); await restart.LoadAsync(); await Frames(2);
            window.SelectSectionForTesting("geral");

            // --- Ambiente --------------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("ambiente"); await Frames(4);
            var env = window.Environment;
            Require(window.PageVisibleForTesting("ambiente") && env.LoadedForTesting == "{}" && !env.IsDirty && !env.ApplyEnabledForTesting && env.EffectForTesting.Contains("sem nenhum ajuste"), "the Ambiente tab starts with no look and nothing to apply");
            Require(env.PresetNamesForTesting.Count == 6 && env.PresetNamesForTesting[0] == "Dia claro" && env.PresetNamesForTesting[5] == "Padrão do jogo", "the looks of the game are offered");
            env.SetHourForTesting(true, 18.5); await Frames(2);
            Require(env.IsDirty && env.ApplyEnabledForTesting && env.DraftJson == "{\"hour\":18.5}" && env.EffectForTesting.Contains("1 ajuste"), "turning a setting on makes a draft that can be applied");
            env.SetWeatherForTesting(true, 2); env.SetSkyForTesting(true, new Color("#FF9A5A")); await Frames(2);
            Require(env.DraftJson == "{\"hour\":18.5,\"weather\":\"rain\",\"sky\":\"#FF9A5A\"}", $"the draft has only the settings that are on ({env.DraftJson})");
            env.RevertForTesting(); await Frames(2); Require(!env.IsDirty && env.DraftJson == "{}", "undoing goes back to what the island has");
            env.SetHourForTesting(true, 20); env.SetFogForTesting(true, .4); env.SetLightForTesting(true, 1.2); await Frames(2);
            await env.ApplyForTesting(); await Frames(3);
            Require(server.EnvSent.Count == 1 && server.EnvLook == "{\"hour\":20,\"fog\":0.4,\"light\":1.2}" && !env.IsDirty && env.StatusForTesting.Contains("Ambiente aplicado") && env.StatusForTesting.Contains("30 segundos"), $"applying sends the look and says how long it takes to reach people ({server.EnvLook})");
            env.ChoosePresetForTesting("Pôr do sol"); env.LoadPresetForTesting(); await Frames(2);
            Require(env.IsDirty && env.DraftJson == "{\"hour\":18.5,\"weather\":\"clear\",\"light\":0.85,\"sky\":\"#FF9A5A\"}" && server.EnvLook.Contains("20"), "a saved look goes into the form without touching the island until it is applied");
            env.PresetNameForTesting = ""; await env.SavePresetForTesting(); Require(env.StatusForTesting.Contains("nome") , "a look needs a name");
            env.PresetNameForTesting = "Meu entardecer"; await env.SavePresetForTesting(); await Frames(2);
            Require(server.EnvCustom.ContainsKey("Meu entardecer") && env.PresetNamesForTesting.Contains("Meu entardecer") && env.IsDirty, "a look is saved by name and the form is kept");
            env.ChoosePresetForTesting("Meu entardecer  (minha)"); await env.DeletePresetForTesting(); await Frames(2);
            Require(!server.EnvCustom.ContainsKey("Meu entardecer") && env.PresetNamesForTesting.Count == 6, "a look of the owner can be deleted (not the game's)");
            server.EnvStale = true; await env.ApplyForTesting(); await Frames(2);
            Require(env.StatusForTesting.Contains("Outra pessoa") && server.EnvLook.Contains("20"), "a conflict is told and nothing is overwritten"); server.EnvStale = false;
            await env.ResetForTesting(); await Frames(3);
            Require(server.EnvLook == "{}" && !env.IsDirty && env.DraftJson == "{}" && env.StatusForTesting.Contains("padrão"), "going back to the game's sky clears the look");
            window.SelectSectionForTesting("geral");

            // --- Mídia -----------------------------------------------------------------------------------------------------
            var played = new List<string>(); var stopped = 0;
            window.Media.PreviewStart = (url, volume) => played.Add($"{url}|{volume:0.##}"); window.Media.PreviewStop = () => stopped++;
            window.SelectSectionForTesting("midia"); await Frames(4);
            var media = window.Media;
            Require(window.PageVisibleForTesting("midia") && media.LoadedUrlForTesting == "" && !media.SaveEnabledForTesting && !media.TestEnabledForTesting && !media.RemoveEnabledForTesting && media.CheckForTesting.Contains("silêncio"), "the Mídia tab starts with no music and nothing to save");
            Require(window.PagePlaceholderTextForTesting("midia").Contains("Ainda não disponível"), "the video says plainly that it is not available");
            media.UrlForTesting = "http://cdn.example.com/a.mp3"; await Frames(1);
            Require(media.CheckForTesting.Contains("https://") && !media.SaveEnabledForTesting && !media.TestEnabledForTesting, "an address that is not https is refused as it is typed, and cannot be saved or tested");
            media.UrlForTesting = "https://localhost/a.mp3"; Require(media.CheckForTesting.Contains("não é permitido"), "an address into a network is refused");
            media.UrlForTesting = "https://cdn.example.com/a.wav"; Require(media.CheckForTesting.Contains(".mp3 ou .ogg"), "the wrong kind of file is refused");
            media.UrlForTesting = "https://cdn.example.com/radio.ogg"; media.NameForTesting = "Rádio da Praia"; media.VolumeForTesting = 40; media.AutoplayForTesting = false; await Frames(1);
            Require(media.CheckForTesting.Contains("OGG") && media.SaveEnabledForTesting && media.TestEnabledForTesting, "a good address is accepted and can be saved and tested");
            media.TestForTesting(); await Frames(1);
            Require(played.Count == 1 && played[0] == "https://cdn.example.com/radio.ogg|0,4" && media.PreviewingForTesting, "testing plays the address for the person at the chosen volume");
            media.StopTestForTesting(); Require(stopped == 1 && !media.PreviewingForTesting, "the test can be stopped");
            media.TestForTesting(); window.SelectSectionForTesting("geral"); window.SelectSectionForTesting("midia"); await Frames(2);
            Require(stopped == 2, "leaving the tab ends a test that is playing");
            await media.SaveForTesting(); await Frames(3);
            Require(server.MediaSent.Count == 1 && server.MediaUrl == "https://cdn.example.com/radio.ogg" && Math.Abs(server.MediaVolume - .4) < .001 && !server.MediaAutoplay && server.MediaName == "Rádio da Praia" && !media.SaveEnabledForTesting && media.RemoveEnabledForTesting, "saving sends the address, volume, automatic play and name");
            Require(media.StatusForTesting.Contains("30 segundos"), "it says how long it takes to reach the people on the island");
            media.UrlForTesting = "https://cdn.example.com/outra.mp3"; server.MediaStale = true; await media.SaveForTesting(); await Frames(2);
            Require(media.StatusForTesting.Contains("Outra pessoa") && server.MediaUrl.EndsWith("radio.ogg"), "a conflict is told and nothing is overwritten"); server.MediaStale = false;
            await media.RemoveForTesting(); await Frames(3);
            Require(server.MediaUrl == "" && media.LoadedUrlForTesting == "" && !media.RemoveEnabledForTesting && media.StatusForTesting.Contains("removida"), "removing the music clears it");
            window.SelectSectionForTesting("geral");

            // --- Física ----------------------------------------------------------------------------------------------------
            server.Phys.AddRange([new(Guid.NewGuid(), "Bola", "Carla", 30, false), new(Guid.NewGuid(), "Caixa", "Duda", 20, false), new(Guid.NewGuid(), "Pêndulo", "Carla", 12, false), new(Guid.NewGuid(), "Pedra", "Duda", 8, true)]);
            window.SelectSectionForTesting("fisica"); await Frames(4);
            var physics = window.Physics;
            Require(window.PageVisibleForTesting("fisica") && physics.IndicatorsForTesting == "4|3 / 200|1|1,7 ms" && Math.Abs(physics.BodiesBarForTesting - 1.5) < .01 && physics.TotalForTesting == 4, $"the indicators are the server's ({physics.IndicatorsForTesting})");
            Require(physics.RowTextsForTesting[0].Contains("Bola") && physics.RowTextsForTesting[0].Contains("Ativo") && physics.RowTextsForTesting.Any(t => t.Contains("Pedra") && t.Contains("Congelado")), "each object shows its owner, its state and its weight");
            physics.SelectFilterForTesting(2); await physics.LoadAsync(); await Frames(2); Require(physics.TotalForTesting == 1, "the filter shows only the frozen"); physics.SelectFilterForTesting(1); await physics.LoadAsync(); await Frames(2); Require(physics.TotalForTesting == 3, "or only the active"); physics.SelectFilterForTesting(0);
            physics.SearchForTesting = "duda"; await physics.LoadAsync(); await Frames(2); Require(physics.TotalForTesting == 2, "search finds by owner"); physics.SearchForTesting = ""; await physics.LoadAsync(); await Frames(2);
            await physics.ToggleFirstForTesting(); await Frames(3);
            Require(server.Phys.Single(o => o.Name == "Bola").Frozen && physics.IndicatorsForTesting.StartsWith("4|2 / 200|2") && physics.StatusForTesting.Contains("congelado"), "freezing one object updates the counters and says so");
            await physics.ToggleFirstForTesting(); await Frames(3);
            Require(!server.Phys.Single(o => o.Name == "Bola").Frozen && physics.StatusForTesting.Contains("liberado"), "the same object is released again");
            await physics.FreezeAllForTesting(); await Frames(3);
            Require(server.Phys.All(o => o.Frozen) && !physics.FreezeAllEnabledForTesting && physics.ReleaseAllEnabledForTesting, "freezing all leaves nothing to freeze");
            server.PhysKept = 1; await physics.ReleaseAllForTesting(); await Frames(3);
            Require(server.Phys.All(o => !o.Frozen) && physics.StatusForTesting.Contains("a mudança dele foi mantida") && physics.ReleaseAllEnabledForTesting == false, "releasing says when an owner's own change was kept"); server.PhysKept = 0;
            for (var i = 0; i < 12; i++) server.Phys.Add(new(Guid.NewGuid(), $"Extra{i}", "Duda", 1, false)); await physics.LoadAsync(); await Frames(2);
            Require(physics.TotalForTesting == 16 && physics.RowsForTesting == 10 && physics.PageTextForTesting == "Página 1 de 2", "the list is paged");
            server.PhysReport = false; await physics.LoadAsync(); await Frames(2);
            Require(physics.IndicatorsForTesting.Split('|')[1] == "—" && physics.IndicatorsForTesting.Split('|')[3] == "—", "without a report from the island server its figures are blank");
            server.PhysReport = true; window.SelectSectionForTesting("geral");

            // --- Terreno ---------------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("terreno"); await Frames(4);
            var terrain = window.Terrain;
            Require(window.PageVisibleForTesting("terreno") && terrain.InfoForTesting.StartsWith("42|") && terrain.InfoForTesting.Contains("256 × 256 m") && terrain.InfoForTesting.Contains("-8 m / 1,2 m / 14,5 m") && terrain.InfoForTesting.EndsWith("-0,8 m"), $"the figures of the ground are the server's ({terrain.InfoForTesting})");
            Require(terrain.PictureShownForTesting && terrain.BackupsForTesting == 0 && terrain.RestoringForTesting == "", "the ground is drawn and there are no backups yet");
            terrain.NameForTesting = ""; await terrain.CreateForTesting(); Require(terrain.StatusForTesting.Contains("nome"), "a backup needs a name");
            server.TerrainSaved = false; terrain.NameForTesting = "Antes da festa"; await terrain.CreateForTesting(); await Frames(2);
            Require(terrain.StatusForTesting.Contains("ainda não foi salvo") && terrain.BackupsForTesting == 0, "without a save of the terrain a backup is refused with the reason"); server.TerrainSaved = true;
            await terrain.CreateForTesting(); await Frames(3);
            Require(terrain.BackupsForTesting == 1 && terrain.NameForTesting == "" && terrain.RowTextsForTesting[0].Contains("Antes da festa") && terrain.RowTextsForTesting[0].Contains("AliceVerse") && terrain.StatusForTesting.Contains("criado"), "a backup is created and listed with who made it");
            terrain.NameForTesting = "antes da FESTA"; await terrain.CreateForTesting(); await Frames(2); Require(terrain.StatusForTesting.Contains("Já existe") && terrain.BackupsForTesting == 1, "the same name twice is refused");
            terrain.SelectWhenForTesting(2); await terrain.RestoreFirstForTesting(); await Frames(3);
            Require(server.TerrainRestoreSent.Count == 1 && server.TerrainRestoreSent[0].Contains("\"mode\":\"in\"") && server.TerrainRestoreSent[0].Contains("\"minutes\":5") && terrain.RestoringForTesting.Contains("Restauração agendada") && terrain.RestoringForTesting.Contains("Reinicialização"), "restoring with the server up schedules a restart and says where to follow it");
            await terrain.RestoreFirstForTesting(); await Frames(2); Require(terrain.StatusForTesting.Contains("Já existe"), "a second restore is refused while one is scheduled");
            server.TerrainRestoring = false; server.ServerOnline = false; terrain.SelectWhenForTesting(0); await terrain.RestoreFirstForTesting(); await Frames(3);
            Require(server.TerrainVersion == 43 && terrain.StatusForTesting.Contains("restaurado"), "with nobody on the island the restore is applied at once"); server.ServerOnline = true;
            await terrain.DeleteFirstForTesting(); await Frames(3); Require(terrain.BackupsForTesting == 0 && terrain.StatusForTesting.Contains("excluído"), "a backup can be deleted");
            server.TerrainBackups.Add(new Server.TerrainBackup(Guid.NewGuid(), "Quebrado", 30, false)); await terrain.LoadAsync(); await Frames(2);
            Require(terrain.RowTextsForTesting[0].Contains("ilegível"), "a backup that cannot be read is marked");
            server.TerrainBackups.Clear(); await terrain.LoadAsync(); await Frames(2);
            window.SelectSectionForTesting("geral");

            // --- all thirteen sections are real pages ------------------------------------------------------------------------
            foreach (var section in LifeVerse.Client.Scripts.IslandManagement.IslandManagementWindow.Sections)
            {
                window.SelectSectionForTesting(section.Id); await Frames(2);
                Require(window.CurrentSection == section.Id && window.PageVisibleForTesting(section.Id), $"a click opens {section.Title}");
                Require(!window.PagePlaceholderTextForTesting(section.Id).Contains("Ainda não disponível") || section.Id == "midia", $"{section.Title} is built, not a placeholder"); // the only page that says it in a card is the video of Mídia
                foreach (var other in LifeVerse.Client.Scripts.IslandManagement.IslandManagementWindow.Sections.Where(s => s.Id != section.Id)) if (window.PageVisibleForTesting(other.Id)) throw new InvalidOperationException($"{other.Title} is still visible when {section.Title} is open");
            }
            Require(!window.SaveEnabledForTesting, "a section without anything to save cannot be saved");
            window.SelectSectionForTesting("geral"); Require(window.NameForTesting == "Ilha Boa", "the summary stays");

            // --- no permission -----------------------------------------------------------------------------------------------
            server.CanEdit = false; await window.LoadAsync(); await Frames(2);
            Require(!window.General.EditableForTesting && !window.SaveEnabledForTesting, "without permission the fields are read-only and there is no save");
            server.CanEdit = true; await window.LoadAsync(); await Frames(2);

            // --- server down -------------------------------------------------------------------------------------------------
            server.Offline = true; await window.LoadAsync(); await Frames(2);
            Require(window.FailureVisibleForTesting && window.FailureTextForTesting.Contains("servidor"), "no connection shows an error with a way to try again");
            server.Offline = false; await window.LoadAsync(); await Frames(2);
            Require(!window.FailureVisibleForTesting && window.NameForTesting == "Ilha Boa", "trying again recovers");

            // --- small screens -----------------------------------------------------------------------------------------------
            foreach (var view in new[] { new Vector2(1280, 720), new Vector2(1366, 768), new Vector2(1920, 1080), new Vector2(2560, 1440) })
            {
                window.SetViewportForTesting(view); await Frames(2);
                var size = window.PanelSizeForTesting;
                Require(size.X <= view.X - 31 && size.Y <= view.Y - 31, $"the window fits {view} (got {size}, min {window.PanelMinForTesting})");
            }
            window.SetViewportForTesting(new Vector2(1920, 1080)); await Frames(2);

            if (OS.GetEnvironment("LIFEVERSE_SHOT") is { Length: > 0 } shot)
            {
                GetWindow().Size = new Vector2I(1536, 1024); window.SetViewportForTesting(new Vector2(1536, 1024)); await Frames(6);
                server.TerrainBackups.Add(new Server.TerrainBackup(Guid.NewGuid(), "Antes da festa", 41, true)); server.TerrainBackups.Add(new Server.TerrainBackup(Guid.NewGuid(), "Ilha limpa", 30, true));
                server.MediaUrl = "https://cdn.example.com/radio/praia.mp3"; server.MediaName = "Rádio da Praia"; server.MediaVolume = .45;
                server.EnvLook = "{\"hour\":18.5,\"weather\":\"clear\",\"light\":0.85,\"sky\":\"#FF9A5A\"}"; server.EnvCustom["Meu entardecer"] = "{\"hour\":19}";
                server.RestartActive = new Server.Rs(Guid.NewGuid(), "scheduled", 272, "Atualização do servidor", DateTimeOffset.UtcNow, null, ""); server.RestartHistory.Add(new Server.Rs(Guid.NewGuid(), "completed", 0, "Manutenção", DateTimeOffset.UtcNow.AddDays(-2), 52, "ok"));
                server.Online.AddRange([new(Guid.NewGuid(), "Carla", false, 125), new(Guid.NewGuid(), "Duda", false, 4000)]); server.Name = "Ilha Tropical"; server.MaxPlayers = 60; server.InSearch = true; server.OnMap = true; server.Cover = ""; await window.LoadAsync(); await Frames(6);
                if (OS.GetEnvironment("LIFEVERSE_SECTION") is { Length: > 0 } section) { window.SelectSectionForTesting(section); await Frames(8); }
                GetViewport().GetTexture().GetImage().SavePng(shot);
            }

            GD.Print("event=island_management_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=island_management_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
