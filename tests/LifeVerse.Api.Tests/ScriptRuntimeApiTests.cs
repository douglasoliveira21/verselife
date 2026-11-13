using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Scripting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What the island reports about its scripts (state, counters, console, permissions) and what the owner sees of it, plus pausing a script and the list of the scripts that use the island the most.</summary>
public sealed class ScriptRuntimeApiTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _owner = Guid.NewGuid(), _stranger = Guid.NewGuid(), _object = Guid.NewGuid(), _script = Guid.NewGuid(), _otherIslandScript = Guid.NewGuid();
    private readonly Island _island, _other;
    private readonly ServerInstance _server;
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IslandServer:SharedKey"] = Key }).Build();
    private readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    public ScriptRuntimeApiTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Praça", Slug = "praca", IsPublic = true, Size = 400 }; _other = new Island { Id = Guid.NewGuid(), OwnerUserId = _stranger, Name = "Outra", Slug = "outra", IsPublic = true, Size = 400 };
        _server = new ServerInstance { Id = Guid.NewGuid(), IslandId = _island.Id, Hostname = "h", Port = 1, Status = "running", StartedAt = _now, LastHeartbeatAt = _now };
        var foreign = Guid.NewGuid();
        _db.Islands.AddRange(_island, _other); _db.ServerInstances.Add(_server);
        _db.BuildObjects.AddRange(new BuildObject { Id = _object, IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Porta", PrimitiveType = "cube" }, new BuildObject { Id = foreign, IslandId = _other.Id, CreatorUserId = _stranger, OwnerUserId = _stranger, Name = "Alheio", PrimitiveType = "cube" });
        _db.ObjectScripts.AddRange(new ObjectScript { Id = _script, ObjectId = _object, OwnerUserId = _owner, Name = "porta", Source = "x", CreatedAt = _now, UpdatedAt = _now }, new ObjectScript { Id = _otherIslandScript, ObjectId = foreign, OwnerUserId = _stranger, Name = "alheio", Source = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())])) };
    private static HttpContext Island(string key = Key) { var context = new DefaultHttpContext(); context.Request.Headers["X-LifeVerse-Service-Key"] = key; return context; }
    private static int Status(IResult result) => result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is ForbidHttpResult ? 403 : result is IValueHttpResult ? 200 : 0;
    private static JsonElement Body(IResult result) => JsonSerializer.SerializeToElement((result as IValueHttpResult)!.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static ScriptRuntimeItem Item(Guid script, string state = "Waiting", long events = 5, double cpu = 1.5, string? error = null) => new(script, state, state == "Waiting" ? "aguardando evento" : "suspenso por limite", events, 1, error is null ? 0 : 1, 0, 0, 7, 120, 4096, 2, cpu, 0.9, 1, 2, error, error is null ? 0 : 12, null);

    [Fact]
    public async Task TheIslandReportsState_Console_AndPermissions_AndTheOwnerReadsThem()
    {
        var report = new ScriptRuntimeReport([Item(_script, events: 9, error: "attempt to index nil")], [new ScriptLogItem(_script, "info", "touch", "tocado", null, 0.2, _now), new ScriptLogItem(_script, "error", "touch", "attempt to index nil", "porta:12 in touch", 0.4, _now.AddSeconds(1))], [new ScriptGrantItem(_script, Guid.NewGuid(), "CONTROL_ANIMATION")]);
        Assert.Equal(200, Status(await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, report, Island(), _config, _db, TimeProvider.System, default)));
        var runtime = await _db.ScriptRuntime.AsNoTracking().SingleAsync(); Assert.Equal((9L, "Waiting", 12), (runtime.Events, runtime.State, runtime.LastErrorLine));
        var seen = Body(await ScriptingEndpoints.RuntimeAsync(_island.Id, _object, _script, As(_owner), _db, default));
        Assert.Equal(9, seen.GetProperty("runtime").GetProperty("events").GetInt64()); Assert.Equal("active", seen.GetProperty("status").GetString());
        var logs = seen.GetProperty("logs").EnumerateArray().Select(l => l.GetProperty("text").GetString()).ToList(); Assert.Equal(["tocado", "attempt to index nil"], logs);           // oldest first, like a console
        Assert.Equal("CONTROL_ANIMATION", seen.GetProperty("grants")[0].GetProperty("permission").GetString());

        // a second report replaces the counters and the permissions (a revoked one disappears); the console only grows
        await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, new ScriptRuntimeReport([Item(_script, "Suspended", 20)], [new ScriptLogItem(_script, "warning", "limit", "suspended: TimeLimit", null, 0, _now.AddSeconds(2))], []), Island(), _config, _db, TimeProvider.System, default);
        Assert.Single(await _db.ScriptRuntime.ToListAsync()); Assert.Equal("Suspended", (await _db.ScriptRuntime.SingleAsync()).State); Assert.Empty(await _db.ScriptGrants.ToListAsync()); Assert.Equal(3, await _db.ScriptLogs.CountAsync());
    }

    [Fact]
    public async Task OnlyTheIslandServerWithTheKeyReports_AndOnlyAboutItsOwnScripts()
    {
        var report = new ScriptRuntimeReport([Item(_script)], [], []);
        Assert.Equal(401, Status(await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, report, Island("x"), _config, _db, TimeProvider.System, default)));
        Assert.Equal(404, Status(await ScriptingEndpoints.SaveRuntimeAsync(Guid.NewGuid(), report, Island(), _config, _db, TimeProvider.System, default)));
        await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, new ScriptRuntimeReport([Item(_script), Item(_otherIslandScript)], [new ScriptLogItem(_otherIslandScript, "info", "x", "forjado", null, 0, _now)], [new ScriptGrantItem(_otherIslandScript, Guid.NewGuid(), "SHOW_HUD")]), Island(), _config, _db, TimeProvider.System, default);
        Assert.Equal([_script], (await _db.ScriptRuntime.ToListAsync()).Select(r => r.ScriptId).ToList()); Assert.Empty(await _db.ScriptLogs.ToListAsync()); Assert.Empty(await _db.ScriptGrants.ToListAsync());   // another island's script is not this server's to touch
        Assert.Equal(400, Status(await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, new ScriptRuntimeReport(Enumerable.Range(0, 401).Select(_ => Item(_script)).ToArray(), [], []), Island(), _config, _db, TimeProvider.System, default)));
    }

    [Fact]
    public async Task TheConsoleKeepsOnlyTheLastTwoHundredLines_AndBadValuesAreClipped()
    {
        var lines = Enumerable.Range(0, 300).Select(i => new ScriptLogItem(_script, i % 2 == 0 ? "info" : "bogus", new string('e', 80), new string('t', 900), null, 0, _now.AddSeconds(i))).ToArray();
        await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, new ScriptRuntimeReport([Item(_script)], lines[..150], []), Island(), _config, _db, TimeProvider.System, default);
        await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, new ScriptRuntimeReport([Item(_script)], lines[150..], []), Island(), _config, _db, TimeProvider.System, default);
        var kept = await _db.ScriptLogs.AsNoTracking().OrderBy(l => l.CreatedAt).ToListAsync();
        Assert.Equal(200, kept.Count); Assert.Equal(_now.AddSeconds(100), kept[0].CreatedAt); Assert.All(kept, l => { Assert.Equal(500, l.Text.Length); Assert.Equal(40, l.EventName.Length); Assert.Contains(l.Level, new[] { "info", "warning", "error" }); });
    }

    [Fact]
    public async Task OnlyWhoCanEditTheIslandReadsTheRuntime_PausesAndResumes()
    {
        Assert.Equal(403, Status(await ScriptingEndpoints.RuntimeAsync(_island.Id, _object, _script, As(_stranger), _db, default)));
        Assert.Equal(404, Status(await ScriptingEndpoints.RuntimeAsync(_island.Id, _object, Guid.NewGuid(), As(_owner), _db, default)));
        Assert.Equal(403, Status(await ScriptingEndpoints.PauseAsync(_island.Id, _object, _script, true, As(_stranger), _db, TimeProvider.System, default)));
        Assert.Equal(200, Status(await ScriptingEndpoints.PauseAsync(_island.Id, _object, _script, true, As(_owner), _db, TimeProvider.System, default))); Assert.Equal("paused", (await _db.ObjectScripts.AsNoTracking().SingleAsync(s => s.Id == _script)).Status);
        Assert.Equal(200, Status(await ScriptingEndpoints.PauseAsync(_island.Id, _object, _script, false, As(_owner), _db, TimeProvider.System, default))); Assert.Equal("active", (await _db.ObjectScripts.AsNoTracking().SingleAsync(s => s.Id == _script)).Status);
        (await _db.ObjectScripts.SingleAsync(s => s.Id == _script)).Status = "stopped"; await _db.SaveChangesAsync();
        Assert.Equal(409, Status(await ScriptingEndpoints.PauseAsync(_island.Id, _object, _script, false, As(_owner), _db, TimeProvider.System, default)));      // a stopped script is started again by saving or restarting, not by "continue"
    }

    [Fact]
    public async Task TheUsagePanelListsTheHeaviestScriptsOfTheIslandFirst_ForWhoCanEditIt()
    {
        var second = Guid.NewGuid(); _db.ObjectScripts.Add(new ObjectScript { Id = second, ObjectId = _object, OwnerUserId = _owner, Name = "pesado", Source = "x", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, new ScriptRuntimeReport([Item(_script, cpu: 2), Item(second, "Suspended", 50, 90)], [], []), Island(), _config, _db, TimeProvider.System, default);
        var usage = Body(await ScriptingEndpoints.UsageAsync(_island.Id, As(_owner), _db, default));
        Assert.Equal(["pesado", "porta"], usage.GetProperty("scripts").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList()); Assert.Equal(1, usage.GetProperty("totals").GetProperty("suspended").GetInt32()); Assert.Equal(92, usage.GetProperty("totals").GetProperty("cpuMilliseconds").GetDouble());
        Assert.Equal(_owner, usage.GetProperty("totals").GetProperty("perOwner")[0].GetProperty("owner").GetGuid());
        Assert.Equal(403, Status(await ScriptingEndpoints.UsageAsync(_island.Id, As(_stranger), _db, default)));
    }

    [Fact]
    public async Task SavingAndValidating_UseTheSameCompiler_NewApiIsAccepted_AndMistakesComeBackWithTheirLine()
    {
        var options = new LuaRuntimeOptions();
        var source = "function on_touch(e)\n  audio.play('Campainha')\n  debug.log('tocado')\n  chat.say(region.getName())\n  timer.after(2, function() object.setAlpha(0.5) end)\nend";
        Assert.Equal(201, Status(await ScriptingEndpoints.CreateAsync(_island.Id, _object, new ObjectScriptRequest("Nova API", source, "on_touch"), As(_owner), _db, options, TimeProvider.System, default)));   // audio. and debug. used to be refused by the old text filter
        var id = (await _db.ObjectScripts.AsNoTracking().SingleAsync(s => s.Name == "Nova API")).Id;
        Assert.Equal(200, Status(await ScriptingEndpoints.UpdateAsync(_island.Id, _object, id, new ObjectScriptRequest("Nova API", source + "\nfunction on_start() chat.sya('x') end", "on_touch"), As(_owner), _db, options, TimeProvider.System, default)));   // a typo is a warning, not a refusal
        var refused = await ScriptingEndpoints.UpdateAsync(_island.Id, _object, id, new ObjectScriptRequest("Nova API", "function on_start()\n  os.execute('x')\nend", "on_start"), As(_owner), _db, options, TimeProvider.System, default);
        Assert.NotEqual(200, Status(refused)); Assert.Contains("linha 2", JsonSerializer.Serialize((refused as IValueHttpResult)!.Value));
        var checkedBody = Body(await ScriptingEndpoints.ValidateAsync(_island.Id, _object, new ScriptingEndpoints.ValidateScriptRequest("function on_touch_strat() end\nfunction on_start()\n  chat.sya('x')\nend"), As(_owner), _db, options, default));
        Assert.True(checkedBody.GetProperty("valid").GetBoolean()); Assert.Equal(2, checkedBody.GetProperty("warnings").GetArrayLength()); Assert.Equal(["on_touch_strat", "on_start"], checkedBody.GetProperty("handlers").EnumerateArray().Select(h => h.GetString()!).ToList());
        var blocked = Body(await ScriptingEndpoints.ValidateAsync(_island.Id, _object, new ScriptingEndpoints.ValidateScriptRequest("function on_start()\n  io.open('x')\nend"), As(_owner), _db, options, default));
        Assert.False(blocked.GetProperty("valid").GetBoolean()); Assert.Equal(2, blocked.GetProperty("errors")[0].GetProperty("line").GetInt32());
    }

    [Fact]
    public void TheReferenceListsWhatTheApiReallyHas()
    {
        var reference = Body(ScriptingEndpoints.Reference());
        Assert.True(reference.GetProperty("functions").GetArrayLength() > 60); Assert.Contains(reference.GetProperty("events").EnumerateArray(), e => e.GetProperty("name").GetString() == "on_dialog_response");
        Assert.Contains(reference.GetProperty("permissions").EnumerateArray(), p => p.GetProperty("name").GetString() == "CONTROL_ANIMATION" && p.GetProperty("askedOfAvatars").GetBoolean()); Assert.Equal(10, reference.GetProperty("states").GetArrayLength());
    }

    [Fact]
    public async Task TheOwnerGivesAndTakesBackPermissions_AndTheIslandReportNeverEatsThem()
    {
        Assert.Equal(200, Status(await ScriptingEndpoints.SetOwnerPermissionAsync(_island.Id, _object, _script, "control_physics", new OwnerPermissionRequest(true), As(_owner), _db, TimeProvider.System, default)));
        var grant = await _db.ScriptGrants.AsNoTracking().SingleAsync(); Assert.Equal((Guid.Empty, "CONTROL_PHYSICS"), (grant.SubjectId, grant.Permission));
        Assert.Equal(200, Status(await ScriptingEndpoints.SetOwnerPermissionAsync(_island.Id, _object, _script, "CONTROL_PHYSICS", new OwnerPermissionRequest(true), As(_owner), _db, TimeProvider.System, default))); Assert.Single(await _db.ScriptGrants.ToListAsync());   // asking twice is harmless
        // the island reports (its avatar grants only): the owner's grant stays, the avatar's is stored
        var avatar = Guid.NewGuid();
        await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, new ScriptRuntimeReport([Item(_script)], [], [new ScriptGrantItem(_script, avatar, "SHOW_HUD"), new ScriptGrantItem(_script, Guid.Empty, "HTTP_REQUESTS")]), Island(), _config, _db, TimeProvider.System, default);
        Assert.Equal(["CONTROL_PHYSICS", "SHOW_HUD"], (await _db.ScriptGrants.AsNoTracking().ToListAsync()).Select(g => g.Permission).Order().ToList());   // an owner grant sent by the island is ignored: only the editor makes them
        await ScriptingEndpoints.SaveRuntimeAsync(_server.Id, new ScriptRuntimeReport([Item(_script)], [], []), Island(), _config, _db, TimeProvider.System, default);
        Assert.Equal(["CONTROL_PHYSICS"], (await _db.ScriptGrants.AsNoTracking().ToListAsync()).Select(g => g.Permission).ToList());                              // the avatar took it back; the owner's stayed
        Assert.Equal(200, Status(await ScriptingEndpoints.SetOwnerPermissionAsync(_island.Id, _object, _script, "CONTROL_PHYSICS", new OwnerPermissionRequest(false), As(_owner), _db, TimeProvider.System, default))); Assert.Empty(await _db.ScriptGrants.ToListAsync());
        Assert.Equal(403, Status(await ScriptingEndpoints.SetOwnerPermissionAsync(_island.Id, _object, _script, "CONTROL_PHYSICS", new OwnerPermissionRequest(true), As(_stranger), _db, TimeProvider.System, default)));
        Assert.Equal(400, Status(await ScriptingEndpoints.SetOwnerPermissionAsync(_island.Id, _object, _script, "SHOW_HUD", new OwnerPermissionRequest(true), As(_owner), _db, TimeProvider.System, default)));      // asked of avatars, not given by the owner
        Assert.Equal(404, Status(await ScriptingEndpoints.SetOwnerPermissionAsync(_island.Id, _object, Guid.NewGuid(), "CONTROL_PHYSICS", new OwnerPermissionRequest(true), As(_owner), _db, TimeProvider.System, default)));
    }
}
