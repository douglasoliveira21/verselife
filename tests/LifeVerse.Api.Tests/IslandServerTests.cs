using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The channel between the API and an island server, and the Administração tab built on it: the report of who is connected, commands handed over once, and the rules of removing and warning.</summary>
public sealed class IslandServerTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _admin, _carla, _duda, _stranger, _island, _instance = Guid.NewGuid();

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now(); }

    public IslandServerTests()
    {
        _clock = new Clock(() => _now);
        (_owner, _admin, _carla, _duda, _stranger) = (User("alice"), User("bruno"), User("carla"), User("duda"), User("edu"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now });
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island, Hostname = "secret-host", Port = 7777, Status = "healthy", StartedAt = _now.AddHours(-2), LastHeartbeatAt = _now, PlayerCount = 3 });
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

    private async Task Report(params (Guid User, string Name)[] players) =>
        await ServerChannel.StoreReportAsync(_db, _instance, new ServerReport([.. players.Select(p => new PlayerReport(p.User, p.Name, _now.AddMinutes(-10).ToUnixTimeMilliseconds(), 1, 2, 3))], 59.5, 3.2, 120, 7200), _now, default);

    private async Task<(int Status, JsonElement Body)> Get(Guid who) => Read(await IslandServerManagement.GetAsync(_island, As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Kick(Guid who, Guid target, string? reason = "Spam") => Read(await IslandServerManagement.KickAsync(_island, new KickRequest(target, reason), As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Notice(Guid who, string text, Guid? target = null) => Read(await IslandServerManagement.NoticeAsync(_island, new NoticeRequest(text, target), As(who), _db, _clock, default));

    // ---- the protocol -----------------------------------------------------------------------------------------------------

    [Fact]
    public void TheCommandsOfAnAnswerAreReadAndWhatIsUnknownOrBrokenIsIgnored()
    {
        var id = Guid.NewGuid(); var user = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new { commands = new object[] { new { id, kind = "kick", userId = user, text = "Spam" }, new { id = Guid.NewGuid(), kind = "reboot", userId = (Guid?)null, text = "" }, new { id = Guid.Empty, kind = "notice", userId = (Guid?)null, text = "x" }, new { id = Guid.NewGuid(), kind = "notice", userId = (Guid?)null, text = "Oi" } } });
        var commands = ServerChannelJson.ParseCommands(json);
        Assert.Equal(2, commands.Count); Assert.Equal((id, "kick", user, "Spam"), (commands[0].Id, commands[0].Kind, commands[0].UserId!.Value, commands[0].Text)); Assert.Equal("Oi", commands[1].Text);
        foreach (var bad in new[] { null, "", "   ", "{}", "[]", "not json", "{\"commands\":5}", "{\"commands\":[{\"id\":\"x\"}]}" }) Assert.Empty(ServerChannelJson.ParseCommands(bad));
    }

    // ---- the report ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheReportShowsWhoIsOnlineAndNothingAboutTheMachine()
    {
        await Report((_carla, "Carla"), (_duda, "Duda"), (_admin, "Bruno"));
        var (status, body) = await Get(_owner);
        Assert.Equal(200, status); Assert.Equal("online", body.GetProperty("state").GetString()); Assert.True(body.GetProperty("fresh").GetBoolean());
        var players = body.GetProperty("players").EnumerateArray().ToList(); Assert.Equal(3, players.Count);
        Assert.Equal(600, players.Single(p => p.GetProperty("name").GetString() == "Carla").GetProperty("onlineSeconds").GetInt64());
        Assert.True(players.Single(p => p.GetProperty("name").GetString() == "Bruno").GetProperty("isStaff").GetBoolean()); Assert.False(players.Single(p => p.GetProperty("name").GetString() == "Carla").GetProperty("isStaff").GetBoolean());
        Assert.Equal(7200, body.GetProperty("uptimeSeconds").GetInt64()); Assert.Equal(8, body.GetProperty("instanceId").GetString()!.Length);
        Assert.DoesNotContain("secret-host", body.GetRawText()); Assert.DoesNotContain("7777", body.GetRawText());
    }

    [Fact]
    public async Task AStaleReportIsNotShownAsCurrent_AndNoServerMeansOffline()
    {
        await Report((_carla, "Carla")); _now = _now.AddSeconds(90);
        (await _db.ServerInstances.SingleAsync()).LastHeartbeatAt = _now; await _db.SaveChangesAsync();
        var stale = (await Get(_owner)).Body; Assert.False(stale.GetProperty("fresh").GetBoolean()); Assert.Equal(0, stale.GetProperty("players").GetArrayLength());
        (await _db.ServerInstances.SingleAsync()).StoppedAt = _now; await _db.SaveChangesAsync();
        var off = (await Get(_owner)).Body; Assert.Equal("offline", off.GetProperty("state").GetString()); Assert.False(off.GetProperty("running").GetBoolean());
    }

    [Fact]
    public async Task OnlyStaffSeeAndAct()
    {
        await Report((_carla, "Carla"));
        Assert.Equal(403, (await Get(_stranger)).Status); Assert.Equal(403, (await Kick(_stranger, _carla)).Status); Assert.Equal(403, (await Notice(_carla, "oi")).Status);
        Assert.Equal(200, (await Get(_admin)).Status);
    }

    [Fact]
    public async Task AReportIsCutToASafeSize()
    {
        var many = Enumerable.Range(0, 400).Select(i => new PlayerReport(Guid.NewGuid(), new string('n', 200), 0, 0, 0, 0)).Append(new PlayerReport(Guid.Empty, "sem id", 0, 0, 0, 0)).ToArray();
        await ServerChannel.StoreReportAsync(_db, _instance, new ServerReport(many, double.NaN, -5, double.PositiveInfinity, -1), _now, default);
        var stored = ServerChannel.ReadReport(await _db.ServerInstances.SingleAsync())!;
        Assert.Equal(ServerChannel.MaxPlayers, stored.Players.Length); Assert.All(stored.Players, p => Assert.True(p.Name.Length <= 80));
        Assert.Equal((0, 0, 0, 0), (stored.Fps, stored.ProcessMs, stored.MemoryMb, (double)stored.UptimeSeconds));
        await ServerChannel.StoreReportAsync(_db, _instance, null, _now, default); // a heartbeat without a report changes nothing
    }

    // ---- commands -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ACommandIsHandedOverOnceAndOnlyToItsIslandAndOnlyWhileItIsFresh()
    {
        await Report((_carla, "Carla"));
        Assert.Equal(202, (await Kick(_owner, _carla, "Spam")).Status); Assert.Equal(202, (await Notice(_admin, "Reinício em 5 minutos")).Status);
        var other = Guid.NewGuid(); _db.IslandServerCommands.Add(new IslandServerCommand { Id = Guid.NewGuid(), IslandId = other, Kind = "notice", Text = "de outra ilha", CreatedByUserId = _owner, CreatedAt = _now, ExpiresAt = _now.AddMinutes(2) }); await _db.SaveChangesAsync();
        var taken = await ServerChannel.TakeCommandsAsync(_db, _island, _now, default);
        Assert.Equal(["kick", "notice"], taken.Select(c => c.Kind)); Assert.Equal(_carla, taken[0].UserId); Assert.Equal("Spam", taken[0].Text);
        Assert.Empty(await ServerChannel.TakeCommandsAsync(_db, _island, _now, default)); // not repeated
        Assert.Single(await ServerChannel.TakeCommandsAsync(_db, other, _now, default));
        await Notice(_owner, "velho"); _now = _now.AddMinutes(3);
        Assert.Empty(await ServerChannel.TakeCommandsAsync(_db, _island, _now, default)); // expired before the server asked
    }

    [Fact]
    public async Task RemovingHasRulesAndLeavesAnAuditEntry()
    {
        await Report((_carla, "Carla"), (_admin, "Bruno"), (_owner, "Alice"));
        Assert.Equal(409, (await Kick(_admin, _owner)).Status); Assert.Equal(409, (await Kick(_owner, _admin)).Status); Assert.Equal(409, (await Kick(_owner, _owner)).Status); // the owner, an administrator, oneself
        Assert.Equal(409, (await Kick(_owner, _duda)).Status); // not on the island
        Assert.True((await Kick(_owner, _carla, new string('x', 201))).Status is 400 or 422);
        Assert.Equal(0, await _db.IslandServerCommands.CountAsync()); Assert.Equal(0, await _db.IslandAudit.CountAsync());
        Assert.Equal(202, (await Kick(_admin, _carla, "Xingamentos")).Status);
        var entry = await _db.IslandAudit.SingleAsync(); Assert.Equal("servidor", entry.Category); Assert.Contains("Carla", entry.Summary); Assert.Contains("Xingamentos", entry.AfterJson);
        (await _db.ServerInstances.SingleAsync()).StoppedAt = _now; await _db.SaveChangesAsync();
        Assert.Equal(409, (await Kick(_owner, _carla)).Status); // no server to receive it
    }

    [Fact]
    public async Task NoticesGoToEveryoneOrToOnePerson()
    {
        await Report((_carla, "Carla"));
        Assert.Equal(202, (await Notice(_owner, "  Boa noite  ")).Status); Assert.Equal(202, (await Notice(_owner, "Só você", _carla)).Status);
        var commands = await _db.IslandServerCommands.OrderBy(c => c.CreatedAt).ToListAsync(); Assert.Null(commands[0].TargetUserId); Assert.Equal("Boa noite", commands[0].Text); Assert.Equal(_carla, commands[1].TargetUserId);
        Assert.Equal(409, (await Notice(_owner, "x", _duda)).Status);
        Assert.True((await Notice(_owner, "   ")).Status is 400 or 422); Assert.True((await Notice(_owner, new string('x', 201))).Status is 400 or 422);
        Assert.Equal(2, await _db.IslandServerCommands.CountAsync());
        Assert.Equal(2, await _db.IslandAudit.CountAsync(e => e.Action == "notice_sent"));
    }
}
