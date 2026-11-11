using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Platform;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record KickRequest(Guid UserId, string? Reason);
public sealed record NoticeRequest(string Text, Guid? UserId);

/// <summary>
/// The Administração tab: who is on the island right now (as its server reports them), removing a person, and telling people something. Removing and telling are commands: the API cannot reach a
/// player, only ask the island server, which picks them up with its next heartbeat (within about 15 seconds). Nothing about the machine (address, port) is ever sent to the owner.
/// </summary>
public static class IslandServerManagement
{
    public static IEndpointRouteBuilder MapIslandServerManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/server").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", GetAsync); group.MapPost("/kick", KickAsync); group.MapPost("/notice", NoticeAsync);
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<(Island? Island, Guid User, IResult? Refusal)> AdminOf(Guid islandId, HttpContext context, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (!Who(context, out var user)) return (null, default, Results.Unauthorized());
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return (null, user, Results.NotFound());
        var admin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);
        return admin ? (island, user, null) : (null, user, Results.Forbid());
    }

    private static async Task<HashSet<Guid>> StaffAsync(LifeVerseDbContext db, Island island, DateTimeOffset now, CancellationToken ct)
    {
        var admins = await db.IslandPermissions.AsNoTracking().Where(p => p.IslandId == island.Id && p.Permission == "admin" && p.UserId != null && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now)).Select(p => p.UserId!.Value).ToListAsync(ct);
        return [island.OwnerUserId, .. admins];
    }

    public static async Task<IResult> GetAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var instance = await ServerChannel.RunningAsync(db, islandId, now, ct);
        var report = instance is null ? null : ServerChannel.ReadReport(instance);
        var fresh = instance?.ReportedAt is { } reported && now - reported < ServerChannel.FreshFor;
        var staff = await StaffAsync(db, island, now, ct);
        var players = fresh && report is not null
            ? report.Players.OrderBy(p => p.ConnectedAtUnixMs).Select(p => new { userId = p.UserId, name = p.Name, onlineSeconds = Math.Max(0, (long)(now - DateTimeOffset.FromUnixTimeMilliseconds(p.ConnectedAtUnixMs)).TotalSeconds), x = p.X, y = p.Y, z = p.Z, isStaff = staff.Contains(p.UserId) }).ToList<object>()
            : [];
        var state = instance is null ? (island.RuntimeState is "starting" or "restarting" ? island.RuntimeState : "offline") : "online";
        return Results.Ok(new
        {
            state, running = instance is not null, instanceId = instance is null ? null : instance.Id.ToString("N")[..8], startedAt = instance?.StartedAt, uptimeSeconds = instance is null ? 0 : Math.Max(0, (long)(now - instance.StartedAt).TotalSeconds),
            lastHeartbeatAt = instance?.LastHeartbeatAt, reportedAt = instance?.ReportedAt, fresh, visitors = instance?.PlayerCount ?? 0, maxPlayers = island.MaxPlayers, closed = island.AccessClosed, players,
        });
    }

    private static async Task<IResult?> NeedRunning(Guid islandId, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct) =>
        await ServerChannel.RunningAsync(db, islandId, now, ct) is null ? Results.Conflict(new { error = "offline", message = "A ilha está desligada: não há servidor para receber o pedido." }) : null;

    public static async Task<IResult> KickAsync(Guid islandId, KickRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length > ServerChannel.MaxTextLength) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [$"O motivo pode ter no máximo {ServerChannel.MaxTextLength} caracteres."] });
        if (await NeedRunning(islandId, db, now, ct) is { } offline) return offline;
        if (request.UserId == me) return Results.Conflict(new { error = "self", message = "Você não pode expulsar a si mesmo." });
        if ((await StaffAsync(db, island, now, ct)).Contains(request.UserId)) return Results.Conflict(new { error = "staff", message = "O proprietário e os administradores não podem ser expulsos." });
        var instance = (await ServerChannel.RunningAsync(db, islandId, now, ct))!; var report = ServerChannel.ReadReport(instance);
        var player = report?.Players.FirstOrDefault(p => p.UserId == request.UserId);
        if (player is null) return Results.Conflict(new { error = "not_online", message = "Essa pessoa não está mais na ilha." });
        db.IslandServerCommands.Add(new IslandServerCommand { Id = Guid.NewGuid(), IslandId = islandId, Kind = ServerCommandKinds.Kick, TargetUserId = request.UserId, Text = reason, CreatedByUserId = me, CreatedAt = now, ExpiresAt = now + ServerChannel.CommandLifetime });
        IslandAudit.Add(db, islandId, me, "servidor", "player_kicked", $"Expulsou {player.Name} da ilha", player.Name, null, new { reason }, now);
        await db.SaveChangesAsync(ct);
        return Results.Accepted(value: new { queued = true, name = player.Name });
    }

    public static async Task<IResult> NoticeAsync(Guid islandId, NoticeRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var text = new string((request.Text ?? "").Trim().Where(c => !char.IsControl(c)).ToArray());
        if (text.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Escreva o aviso."] });
        if (text.Length > ServerChannel.MaxTextLength) return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = [$"O aviso pode ter no máximo {ServerChannel.MaxTextLength} caracteres."] });
        if (await NeedRunning(islandId, db, now, ct) is { } offline) return offline;
        string target = "todos na ilha";
        if (request.UserId is { } user)
        {
            var report = ServerChannel.ReadReport((await ServerChannel.RunningAsync(db, islandId, now, ct))!);
            var player = report?.Players.FirstOrDefault(p => p.UserId == user);
            if (player is null) return Results.Conflict(new { error = "not_online", message = "Essa pessoa não está mais na ilha." });
            target = player.Name;
        }
        db.IslandServerCommands.Add(new IslandServerCommand { Id = Guid.NewGuid(), IslandId = islandId, Kind = ServerCommandKinds.Notice, TargetUserId = request.UserId, Text = text, CreatedByUserId = me, CreatedAt = now, ExpiresAt = now + ServerChannel.CommandLifetime });
        IslandAudit.Add(db, islandId, me, "servidor", "notice_sent", request.UserId is null ? "Enviou um aviso a todos na ilha" : $"Enviou um aviso a {target}", target, null, new { text }, now);
        await db.SaveChangesAsync(ct);
        return Results.Accepted(value: new { queued = true });
    }
}
