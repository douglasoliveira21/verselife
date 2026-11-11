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

/// <summary>The Desempenho tab: samples kept at most once a minute for a day, what the report says now, the scripts and objects that weigh the most, and who may look.</summary>
public sealed class IslandPerformanceTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _stranger, _island, _instance = Guid.NewGuid();

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now(); }

    public IslandPerformanceTests()
    {
        _clock = new Clock(() => _now);
        (_owner, _stranger) = (User("alice"), User("edu"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now });
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now.AddHours(-1), LastHeartbeatAt = _now });
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

    private Task Report(double fps, int players = 2, double memory = 100) =>
        ServerChannel.StoreReportAsync(_db, _instance, new ServerReport([.. Enumerable.Range(0, players).Select(i => new PlayerReport(Guid.NewGuid(), "p" + i, 0, 0, 0, 0))], fps, 4.5, memory, 3600, 60), _now, default);

    private async Task<(int Status, JsonElement Body)> Get(Guid who, int? hours = null) => Read(await IslandPerformanceManagement.GetAsync(_island, hours, As(who), _db, _clock, default));

    [Fact]
    public async Task OnlyStaffLook()
    {
        Assert.Equal(403, (await Get(_stranger)).Status); Assert.Equal(200, (await Get(_owner)).Status);
    }

    [Fact]
    public async Task TheCurrentFiguresAreWhatTheServerReported_AndAnOldReportIsNotShownAsCurrent()
    {
        await Report(58.5, players: 3, memory: 210);
        var current = (await Get(_owner)).Body.GetProperty("current");
        Assert.Equal((58.5, 4.5, 210.0, 60.0, 3, 10), (current.GetProperty("fps").GetDouble(), current.GetProperty("processMs").GetDouble(), current.GetProperty("memoryMb").GetDouble(), current.GetProperty("tickRate").GetDouble(), current.GetProperty("players").GetInt32(), current.GetProperty("maxPlayers").GetInt32()));
        _now = _now.AddSeconds(90); (await _db.ServerInstances.SingleAsync()).LastHeartbeatAt = _now; await _db.SaveChangesAsync();
        var stale = (await Get(_owner)).Body; Assert.False(stale.GetProperty("fresh").GetBoolean()); Assert.Equal(JsonValueKind.Null, stale.GetProperty("current").ValueKind);
        (await _db.ServerInstances.SingleAsync()).StoppedAt = _now; await _db.SaveChangesAsync();
        Assert.False((await Get(_owner)).Body.GetProperty("running").GetBoolean());
    }

    [Fact]
    public async Task AtMostOneSampleAMinuteIsKeptAndOldOnesAreDropped()
    {
        await Report(60); _now = _now.AddSeconds(15); await Report(50); _now = _now.AddSeconds(15); await Report(40);
        Assert.Equal(1, await _db.IslandMetrics.CountAsync()); // three heartbeats within a minute: one sample
        _now = _now.AddSeconds(60); await Report(30);
        Assert.Equal(2, await _db.IslandMetrics.CountAsync());
        _now = _now.AddHours(25); await Report(20);
        Assert.Equal(1, await _db.IslandMetrics.CountAsync()); // the ones older than a day are gone
    }

    [Fact]
    public async Task TheSeriesAndSummaryFollowTheChosenPeriod()
    {
        for (var i = 0; i < 90; i++) { await Report(30 + i % 31, players: i % 5); _now = _now.AddMinutes(1); (await _db.ServerInstances.SingleAsync()).LastHeartbeatAt = _now; await _db.SaveChangesAsync(); }
        var hour = (await Get(_owner, 1)).Body; var day = (await Get(_owner, 24)).Body;
        Assert.InRange(hour.GetProperty("series").GetArrayLength(), 55, IslandPerformanceManagement.MaxPoints); Assert.InRange(day.GetProperty("series").GetArrayLength(), 1, IslandPerformanceManagement.MaxPoints);
        Assert.Equal(1, hour.GetProperty("window").GetInt32()); Assert.Equal(24, day.GetProperty("window").GetInt32()); Assert.Equal(1, (await Get(_owner, 7)).Body.GetProperty("window").GetInt32()); // an unknown period falls back to one hour
        var fps = day.GetProperty("summary").GetProperty("fps"); Assert.Equal(30, fps.GetProperty("min").GetDouble()); Assert.Equal(60, fps.GetProperty("max").GetDouble());
        Assert.Equal(4, day.GetProperty("summary").GetProperty("players").GetProperty("max").GetDouble());
        var times = day.GetProperty("series").EnumerateArray().Select(p => p.GetProperty("at").GetDateTimeOffset()).ToList(); Assert.Equal(times.OrderBy(t => t), times);
        Assert.True(hour.GetProperty("summary").GetProperty("fps").GetProperty("min").GetDouble() >= 30);
    }

    [Fact]
    public async Task TheDiagnosisListsTheHeaviestScriptsAndObjects()
    {
        var cube = Guid.NewGuid(); var door = Guid.NewGuid(); var big = Guid.NewGuid();
        _db.BuildObjects.AddRange(
            new BuildObject { Id = cube, IslandId = _island, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Cubo", PrimitiveType = "cube", Cost = 5, CreatedAt = _now, UpdatedAt = _now },
            new BuildObject { Id = door, IslandId = _island, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Porta", PrimitiveType = "cube", Cost = 20, PhysicsJson = "{\"mode\":\"physical\"}", CreatedAt = _now, UpdatedAt = _now },
            new BuildObject { Id = big, IslandId = _island, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Casa", PrimitiveType = "mesh", Cost = 300, CreatedAt = _now, UpdatedAt = _now });
        var light = Guid.NewGuid(); var heavy = Guid.NewGuid(); var quiet = Guid.NewGuid();
        _db.ObjectScripts.AddRange(new ObjectScript { Id = light, ObjectId = cube, OwnerUserId = _owner, Name = "leve", Source = "", CreatedAt = _now, UpdatedAt = _now }, new ObjectScript { Id = heavy, ObjectId = door, OwnerUserId = _owner, Name = "pesado", Source = "", CreatedAt = _now, UpdatedAt = _now }, new ObjectScript { Id = quiet, ObjectId = big, OwnerUserId = _owner, Name = "parado", Source = "", Status = "disabled", CreatedAt = _now, UpdatedAt = _now });
        for (var i = 0; i < 3; i++) _db.ScriptExecutions.Add(new ScriptExecution { Id = Guid.NewGuid(), ScriptId = heavy, EventName = "touch", Status = "ok", InstructionCount = 5000, Error = i == 2 ? "timeout" : "", CreatedAt = _now.AddMinutes(-10) });
        _db.ScriptExecutions.Add(new ScriptExecution { Id = Guid.NewGuid(), ScriptId = light, EventName = "touch", Status = "ok", InstructionCount = 10, CreatedAt = _now.AddMinutes(-5) });
        _db.ScriptExecutions.Add(new ScriptExecution { Id = Guid.NewGuid(), ScriptId = heavy, EventName = "touch", Status = "ok", InstructionCount = 9999, CreatedAt = _now.AddHours(-30) }); // outside the window
        await _db.SaveChangesAsync();
        var body = (await Get(_owner, 1)).Body; var diagnostics = body.GetProperty("diagnostics");
        var scripts = diagnostics.GetProperty("scripts").EnumerateArray().ToList(); Assert.Equal("Porta", scripts[0].GetProperty("name").GetString());
        Assert.Equal((3, 15000L, 1), (scripts[0].GetProperty("runs").GetInt32(), scripts[0].GetProperty("instructions").GetInt64(), scripts[0].GetProperty("errors").GetInt32())); Assert.Equal(2, scripts.Count);
        Assert.Equal(2, body.GetProperty("scripts").GetProperty("active").GetInt32()); Assert.Equal(3, body.GetProperty("scripts").GetProperty("total").GetInt32());
        var objects = diagnostics.GetProperty("objects").EnumerateArray().ToList(); Assert.Equal(["Casa", "Porta", "Cubo"], objects.Select(o => o.GetProperty("name").GetString()!)); Assert.True(objects[1].GetProperty("physical").GetBoolean()); Assert.Equal("alice", objects[0].GetProperty("ownerName").GetString());
    }
}
