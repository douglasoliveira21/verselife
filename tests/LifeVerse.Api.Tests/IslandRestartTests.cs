using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Restarting an island: who may ask, the rules of the request, the warnings, the stop command, bringing the island back, cancelling, giving up, and the history.</summary>
public sealed class IslandRestartTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _admin, _stranger, _island, _oldServer = Guid.NewGuid();
    private readonly List<Guid> _ensured = [];

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now(); }

    public IslandRestartTests()
    {
        _clock = new Clock(() => _now);
        (_owner, _admin, _stranger) = (User("alice"), User("bruno"), User("edu"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now });
        _db.ServerInstances.Add(new ServerInstance { Id = _oldServer, IslandId = _island, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now.AddHours(-3), LastHeartbeatAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now });
        _db.SaveChanges(); return id;
    }

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };

    private static (int Status, JsonElement Body) Read(IResult result)
    {
        var status = result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, value is null ? default : JsonSerializer.SerializeToElement(value));
    }

    private async Task<(int Status, JsonElement Body)> Ask(Guid who, string mode, int? minutes = null, DateTimeOffset? at = null, string? reason = null) =>
        Read(await IslandRestartService.RequestAsync(_island, new RestartRequest(mode, minutes, at, reason), As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Get(Guid who) => Read(await IslandRestartService.GetAsync(_island, As(who), _db, _clock, default));
    private Task Tick() => IslandRestartService.Tick(_db, _now, id => { _ensured.Add(id); return Task.CompletedTask; }, default);
    private void Beat() { foreach (var s in _db.ServerInstances.Where(s => s.StoppedAt == null)) s.LastHeartbeatAt = _now; _db.SaveChanges(); }
    private async Task<List<ServerCommand>> Commands() => [.. (await _db.IslandServerCommands.Where(c => c.DeliveredAt == null).OrderBy(c => c.CreatedAt).ToListAsync()).Select(c => new ServerCommand(c.Id, c.Kind, c.TargetUserId, c.Text))];
    private async Task<IslandRestart> Only() => await _db.IslandRestarts.SingleAsync();

    [Fact]
    public async Task OnlyStaffAskAndSee()
    {
        Assert.Equal(403, (await Get(_stranger)).Status); Assert.Equal(403, (await Ask(_stranger, "now")).Status);
        Assert.Equal(200, (await Get(_admin)).Status); Assert.Equal(403, (await Ask(_admin, "in", 5)).Status); Assert.Equal(200, (await Ask(_owner, "in", 5)).Status); // staff sees, only the owner restarts
    }

    [Fact]
    public async Task TheRequestHasRules()
    {
        Assert.True((await Ask(_owner, "whenever")).Status is 400 or 422); Assert.True((await Ask(_owner, "in", 3)).Status is 400 or 422); Assert.True((await Ask(_owner, "in")).Status is 400 or 422);
        Assert.True((await Ask(_owner, "at", at: _now.AddSeconds(10))).Status is 400 or 422); Assert.True((await Ask(_owner, "at", at: _now.AddDays(2))).Status is 400 or 422); Assert.True((await Ask(_owner, "at")).Status is 400 or 422);
        Assert.True((await Ask(_owner, "now", reason: new string('x', 201))).Status is 400 or 422);
        Assert.Equal(0, await _db.IslandRestarts.CountAsync());
        Assert.Equal(200, (await Ask(_owner, "at", at: _now.AddHours(3), reason: "Atualização")).Status);
        Assert.Equal(409, (await Ask(_owner, "now")).Status); // one at a time
        Assert.Equal(1, await _db.IslandAudit.CountAsync(e => e.Action == "restart_requested"));
    }

    [Fact]
    public async Task ADeadIslandHasNothingToRestart()
    {
        (await _db.ServerInstances.SingleAsync()).StoppedAt = _now; await _db.SaveChangesAsync();
        Assert.Equal(409, (await Ask(_owner, "now")).Status);
        var info = (await Get(_owner)).Body; Assert.Equal("offline", info.GetProperty("state").GetString()); Assert.False(info.GetProperty("running").GetBoolean());
    }

    [Fact]
    public async Task EachWarningIsSentOnceAndTheSmallestDueIsTheOneSaid()
    {
        await Ask(_owner, "in", 10); // 600 s
        await Tick(); Assert.Empty(await Commands()); // nothing due yet
        _now = _now.AddSeconds(310); Beat(); await Tick(); // 290 s left
        var first = await Commands(); Assert.Single(first); Assert.Contains("5 minutos", first[0].Text);
        await Tick(); Assert.Single(await Commands()); // not repeated
        _now = _now.AddSeconds(235); Beat(); await Tick(); // 55 s left
        Assert.Contains("1 minuto", (await Commands()).Last().Text);
        _now = _now.AddSeconds(46); Beat(); await Tick(); // 9 s left
        Assert.Contains("10 segundos", (await Commands()).Last().Text);
        Assert.Equal(3, (await Commands()).Count);
    }

    [Fact]
    public async Task ARestartAskedForOneMinuteOnlySaysTheSmallestWarning()
    {
        await Ask(_owner, "in", 1); await Tick();
        var all = await Commands(); Assert.Single(all); Assert.Contains("1 minuto", all[0].Text);
    }

    [Fact]
    public async Task AtItsTimeTheServerIsToldToStop_ThenTheIslandIsStartedAgainAndTheRestartCompletes()
    {
        await Ask(_owner, "in", 1, reason: "Atualização");
        _now = _now.AddSeconds(61); Beat(); await Tick();
        Assert.Equal("running", (await Only()).Status);
        var shutdown = (await Commands()).Single(c => c.Kind == ServerCommandKinds.Shutdown); Assert.Equal("Atualização", shutdown.Text);
        _ensured.Clear(); await Tick(); Assert.Empty(_ensured); // the old server is still alive: wait for it to leave
        (await _db.ServerInstances.SingleAsync()).StoppedAt = _now; await _db.SaveChangesAsync(); _now = _now.AddSeconds(10);
        await Tick(); Assert.Equal([_island], _ensured); // it left: ask for a new one
        _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = _island, Hostname = "h2", Port = 2, Status = "healthy", StartedAt = _now, LastHeartbeatAt = _now }); await _db.SaveChangesAsync();
        _now = _now.AddSeconds(20); Beat(); await Tick();
        var done = await Only(); Assert.Equal("completed", done.Status); Assert.NotNull(done.CompletedAt);
        var history = (await Get(_owner)).Body; Assert.Equal(JsonValueKind.Null, history.GetProperty("active").ValueKind);
        var item = history.GetProperty("history")[0]; Assert.Equal("completed", item.GetProperty("status").GetString()); Assert.Equal("alice", item.GetProperty("requestedBy").GetString()); Assert.InRange(item.GetProperty("durationSeconds").GetInt64(), 25, 40);
        Assert.Equal(1, await _db.IslandAudit.CountAsync(e => e.Action == "restart_completed"));
    }

    [Fact]
    public async Task ARestartThatNeverHappensGivesUpWithAReason()
    {
        await Ask(_owner, "now"); await Tick(); Assert.Equal("running", (await Only()).Status);
        _now = _now.AddMinutes(6); Beat(); await Tick(); // the old server never left
        var failed = await Only(); Assert.Equal("failed", failed.Status); Assert.Contains("não obedeceu", failed.Result);
        Assert.Equal(1, await _db.IslandAudit.CountAsync(e => e.Action == "restart_failed"));
        Assert.Equal(200, (await Ask(_owner, "now")).Status); // and a new one can be asked
    }

    [Fact]
    public async Task IfTheIslandNeverComesBackItIsToldTooAndIfItWentOffBeforeItsTimeItFails()
    {
        await Ask(_owner, "now"); await Tick();
        (await _db.ServerInstances.SingleAsync()).StoppedAt = _now; await _db.SaveChangesAsync();
        _now = _now.AddMinutes(6); await Tick(); Assert.Contains("não voltou", (await Only()).Result);
        _db.IslandRestarts.RemoveRange(_db.IslandRestarts); await _db.SaveChangesAsync();
        _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = _island, Hostname = "h", Port = 3, Status = "healthy", StartedAt = _now, LastHeartbeatAt = _now }); await _db.SaveChangesAsync();
        await Ask(_owner, "in", 1); (await _db.ServerInstances.Where(s => s.StoppedAt == null).ToListAsync()).ForEach(s => s.StoppedAt = _now); await _db.SaveChangesAsync();
        _now = _now.AddMinutes(2); await Tick(); Assert.Contains("desligada", (await Only()).Result);
    }

    [Fact]
    public async Task CancellingIsPossibleOnlyBeforeItStarts()
    {
        var id = (await Ask(_owner, "in", 10)).Body.GetProperty("id").GetGuid();
        _now = _now.AddSeconds(310); Beat(); await Tick();
        Assert.Equal(204, Read(await IslandRestartService.CancelAsync(_island, id, As(_owner), _db, _clock, default)).Status);
        Assert.Equal("cancelled", (await Only()).Status);
        Assert.Contains("cancelada", (await Commands()).Last().Text); // people who were warned are told it is off
        _now = _now.AddMinutes(20); await Tick(); Assert.DoesNotContain(await Commands(), c => c.Kind == ServerCommandKinds.Shutdown);
        Assert.Equal(404, Read(await IslandRestartService.CancelAsync(_island, id, As(_owner), _db, _clock, default)).Status);
        Beat(); var again = (await Ask(_owner, "now")).Body.GetProperty("id").GetGuid(); await Tick();
        Assert.Equal(409, Read(await IslandRestartService.CancelAsync(_island, again, As(_owner), _db, _clock, default)).Status); // already running
        Assert.Equal(403, Read(await IslandRestartService.CancelAsync(_island, again, As(_stranger), _db, _clock, default)).Status);
        Assert.Equal(1, await _db.IslandAudit.CountAsync(e => e.Action == "restart_cancelled"));
    }

    [Fact]
    public async Task TheScreenSeesTheCountdownAndTheStateOfTheIsland()
    {
        await Ask(_owner, "in", 10, reason: "Manutenção");
        var info = (await Get(_owner)).Body; var active = info.GetProperty("active");
        Assert.Equal("online", info.GetProperty("state").GetString()); Assert.Equal("scheduled", active.GetProperty("status").GetString()); Assert.Equal(600, active.GetProperty("remainingSeconds").GetInt32()); Assert.Equal("Manutenção", active.GetProperty("reason").GetString());
        Assert.Equal(10800, info.GetProperty("uptimeSeconds").GetInt64());
        _now = _now.AddMinutes(11); Beat(); await Tick();
        Assert.Equal("restarting", (await Get(_owner)).Body.GetProperty("state").GetString());
    }
}
