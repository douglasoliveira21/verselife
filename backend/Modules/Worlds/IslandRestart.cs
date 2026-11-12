using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Platform;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record RestartRequest(string Mode, int? Minutes, DateTimeOffset? At, string? Reason);

/// <summary>
/// Restarting an island. An administrator asks (now, in 1, 5 or 10 minutes, or at a time); the API warns the people on the island, tells the island server to save and stop, and starts the island
/// again through the World Manager. It is a state machine kept in <c>island_restarts</c> and moved by <see cref="Tick"/> every few seconds, so it survives the API restarting. Nothing here reaches
/// the machine: the only thing the API can do to a server is ask it, and the server answers by saving and leaving.
/// </summary>
public static class IslandRestartService
{
    public const int ReasonLimit = 200, MaxAheadHours = 24;
    public static readonly int[] AllowedMinutes = [1, 5, 10];
    public static readonly int[] WarnSeconds = [300, 60, 10];
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

    private static string Words(int seconds) => seconds >= 60 ? (seconds / 60 == 1 ? "1 minuto" : $"{seconds / 60} minutos") : $"{seconds} segundos";

    /// <summary>
    /// Moves every restart one step: sends the warnings that are due, tells the server to stop when its time comes, starts the island again once the old server is gone and closes the restart when
    /// the new one answers (or gives up after five minutes). <paramref name="ensure"/> asks the World Manager to start the island.
    /// </summary>
    public static async Task Tick(LifeVerseDbContext db, DateTimeOffset now, Func<Guid, Task> ensure, CancellationToken ct)
    {
        var open = await db.IslandRestarts.Where(r => r.Status == "scheduled" || r.Status == "running").ToListAsync(ct);
        foreach (var restart in open)
        {
            if (restart.Status == "scheduled") await StepScheduledAsync(db, restart, now, ct);
            else await StepRunningAsync(db, restart, now, ensure, ct);
        }
        if (open.Count > 0) await db.SaveChangesAsync(ct);
    }

    private static async Task StepScheduledAsync(LifeVerseDbContext db, IslandRestart restart, DateTimeOffset now, CancellationToken ct)
    {
        var remaining = (int)Math.Ceiling((restart.ScheduledFor - now).TotalSeconds);
        if (remaining > 0)
        {
            var warned = JsonSerializer.Deserialize<List<int>>(restart.WarnedJson) ?? [];
            var due = WarnSeconds.Where(s => remaining <= s && !warned.Contains(s)).ToList();
            if (due.Count == 0) return;
            warned.AddRange(due); restart.WarnedJson = JsonSerializer.Serialize(warned);
            if (await ServerChannel.RunningAsync(db, restart.IslandId, now, ct) is not null) // the smallest one that is due is the one worth saying
                db.IslandServerCommands.Add(new IslandServerCommand { Id = Guid.NewGuid(), IslandId = restart.IslandId, Kind = ServerCommandKinds.Notice, Text = $"A ilha vai reiniciar em {Words(due.Min())}. Seus dados serão salvos.", CreatedByUserId = restart.RequestedByUserId, CreatedAt = now, ExpiresAt = now + ServerChannel.CommandLifetime });
            return;
        }
        var running = await ServerChannel.RunningAsync(db, restart.IslandId, now, ct);
        if (running is null) { Finish(db, restart, "failed", "A ilha estava desligada na hora do reinício.", now); return; }
        if (restart.RestoreSnapshotId is { } snapshot && !await IslandTerrainManagement.ApplyRestoreAsync(db, restart.IslandId, snapshot, restart.RequestedByUserId, now, ct)) { Finish(db, restart, "failed", "O backup do terreno não pôde ser lido; nada foi mudado.", now); return; }
        db.IslandServerCommands.Add(new IslandServerCommand { Id = Guid.NewGuid(), IslandId = restart.IslandId, Kind = ServerCommandKinds.Shutdown, Text = restart.Reason, CreatedByUserId = restart.RequestedByUserId, CreatedAt = now, ExpiresAt = now + ServerChannel.CommandLifetime });
        restart.Status = "running"; restart.StartedAt = now;
    }

    private static async Task StepRunningAsync(LifeVerseDbContext db, IslandRestart restart, DateTimeOffset now, Func<Guid, Task> ensure, CancellationToken ct)
    {
        var started = restart.StartedAt ?? restart.ScheduledFor;
        var alive = await db.ServerInstances.AsNoTracking().Where(x => x.IslandId == restart.IslandId && x.StoppedAt == null && x.Status == "healthy" && x.LastHeartbeatAt > now.AddSeconds(-45)).ToListAsync(ct);
        if (alive.Any(x => x.StartedAt > started)) { Finish(db, restart, "completed", restart.RestoreSnapshotId is null ? "A ilha reiniciou e voltou ao ar." : "A ilha reiniciou com o terreno restaurado.", now); return; }
        if (now - started > GiveUpAfter) { Finish(db, restart, "failed", alive.Count > 0 ? "O servidor da ilha não obedeceu ao pedido (versão antiga?)." : "A ilha não voltou ao ar em 5 minutos.", now); return; }
        if (alive.Count == 0) await ensure(restart.IslandId); // the old server is gone: ask for a new one (asking again is harmless)
    }

    private static void Finish(LifeVerseDbContext db, IslandRestart restart, string status, string result, DateTimeOffset now)
    {
        restart.Status = status; restart.Result = result; restart.CompletedAt = now;
        IslandAudit.Add(db, restart.IslandId, restart.RequestedByUserId, "servidor", status == "completed" ? "restart_completed" : "restart_failed", status == "completed" ? "A reinicialização terminou" : "A reinicialização falhou", "Reinicialização", new { scheduledFor = restart.ScheduledFor }, new { status, result }, now);
    }

    // ---- endpoints --------------------------------------------------------------------------------------------------------

    public static IEndpointRouteBuilder MapIslandRestartManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/restart").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", GetAsync); group.MapPost("", RequestAsync); group.MapDelete("/{restartId:guid}", CancelAsync);
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

    private static async Task<object> View(LifeVerseDbContext db, IslandRestart r, DateTimeOffset now) => new
    {
        id = r.Id, status = r.Status, scheduledFor = r.ScheduledFor, remainingSeconds = r.Status == "scheduled" ? Math.Max(0, (int)Math.Ceiling((r.ScheduledFor - now).TotalSeconds)) : 0, requestedBy = await IslandAudit.NameAsync(db, r.RequestedByUserId),
        reason = r.Reason, createdAt = r.CreatedAt, startedAt = r.StartedAt, completedAt = r.CompletedAt, durationSeconds = r.StartedAt is { } s && r.CompletedAt is { } e ? (long)(e - s).TotalSeconds : (long?)null, result = r.Result,
    };

    public static async Task<IResult> GetAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var instance = await ServerChannel.RunningAsync(db, islandId, now, ct);
        var active = await db.IslandRestarts.AsNoTracking().Where(r => r.IslandId == islandId && (r.Status == "scheduled" || r.Status == "running")).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        var history = await db.IslandRestarts.AsNoTracking().Where(r => r.IslandId == islandId && r.Status != "scheduled" && r.Status != "running").OrderByDescending(r => r.CreatedAt).Take(20).ToListAsync(ct);
        var state = active?.Status == "running" ? "restarting" : instance is null ? (island.RuntimeState is "starting" or "restarting" ? island.RuntimeState : "offline") : "online";
        var historyView = new List<object>(); foreach (var item in history) historyView.Add(await View(db, item, now));
        return Results.Ok(new
        {
            state, running = instance is not null, startedAt = instance?.StartedAt, uptimeSeconds = instance is null ? 0 : Math.Max(0, (long)(now - instance.StartedAt).TotalSeconds),
            active = active is null ? null : await View(db, active, now), history = historyView, minutes = AllowedMinutes, maxAheadHours = MaxAheadHours,
        });
    }

    public static async Task<IResult> RequestAsync(Guid islandId, RestartRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!; if (!await IslandOwnerOnly.AllowsAsync(db, island, me, now)) return IslandOwnerOnly.Refusal();
        var reason = (request.Reason ?? "").Trim(); var errors = new Dictionary<string, string[]>();
        if (reason.Length > ReasonLimit) errors["reason"] = [$"O motivo pode ter no máximo {ReasonLimit} caracteres."];
        DateTimeOffset when = now;
        switch (request.Mode)
        {
            case "now": break;
            case "in" when request.Minutes is { } minutes && AllowedMinutes.Contains(minutes): when = now.AddMinutes(minutes); break;
            case "at" when request.At is { } at: if (at <= now.AddSeconds(30) || at > now.AddHours(MaxAheadHours)) errors["at"] = [$"Escolha um horário entre daqui a 1 minuto e {MaxAheadHours} horas."]; when = at; break;
            default: errors["mode"] = ["Escolha quando reiniciar."]; break;
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await db.IslandRestarts.AnyAsync(r => r.IslandId == islandId && (r.Status == "scheduled" || r.Status == "running"), ct)) return Results.Conflict(new { error = "active", message = "Já existe uma reinicialização agendada ou em andamento. Cancele ou espere terminar." });
        if (await ServerChannel.RunningAsync(db, islandId, now, ct) is null) return Results.Conflict(new { error = "offline", message = "A ilha está desligada: não há o que reiniciar. Ela liga quando alguém entra." });
        var restart = new IslandRestart { Id = Guid.NewGuid(), IslandId = islandId, RequestedByUserId = me, Reason = reason, ScheduledFor = when, Status = "scheduled", CreatedAt = now };
        db.IslandRestarts.Add(restart);
        IslandAudit.Add(db, islandId, me, "servidor", "restart_requested", request.Mode == "now" ? "Pediu a reinicialização imediata da ilha" : $"Agendou a reinicialização da ilha para {when.ToLocalTime():dd/MM/yyyy HH:mm}", "Reinicialização", null, new { scheduledFor = when, reason }, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await View(db, restart, now));
    }

    public static async Task<IResult> CancelAsync(Guid islandId, Guid restartId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!; if (!await IslandOwnerOnly.AllowsAsync(db, island, me, now)) return IslandOwnerOnly.Refusal();
        var restart = await db.IslandRestarts.SingleOrDefaultAsync(r => r.Id == restartId && r.IslandId == islandId, ct); if (restart is null) return Results.NotFound();
        if (restart.Status == "running") return Results.Conflict(new { error = "running", message = "A reinicialização já começou e não dá mais para cancelar." });
        if (restart.Status != "scheduled") return Results.NotFound();
        restart.Status = "cancelled"; restart.CompletedAt = now; restart.Result = "Cancelada.";
        IslandAudit.Add(db, islandId, me, "servidor", "restart_cancelled", "Cancelou a reinicialização agendada", "Reinicialização", new { scheduledFor = restart.ScheduledFor }, null, now);
        if (restart.WarnedJson != "[]" && await ServerChannel.RunningAsync(db, islandId, now, ct) is not null)
            db.IslandServerCommands.Add(new IslandServerCommand { Id = Guid.NewGuid(), IslandId = islandId, Kind = ServerCommandKinds.Notice, Text = "A reinicialização da ilha foi cancelada.", CreatedByUserId = me, CreatedAt = now, ExpiresAt = now + ServerChannel.CommandLifetime });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>Moves the restarts along every few seconds (see <see cref="IslandRestartService.Tick"/>).</summary>
public sealed class IslandRestartWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<IslandRestartWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var worlds = scope.ServiceProvider.GetRequiredService<WorldManager>();
                await IslandRestartService.Tick(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), clock.GetUtcNow(), async id => { await worlds.EnsureAsync(id, stoppingToken); }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Island restart tick failed"); }
        }
    }
}
