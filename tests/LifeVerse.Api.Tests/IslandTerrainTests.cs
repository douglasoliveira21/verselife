using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Platform;
using LifeVerse.Shared.Terrain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Terreno tab: the figures of the ground, named backups, and restoring one without letting the island server write the old terrain back over it.</summary>
public sealed class IslandTerrainTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _admin, _stranger, _island, _server = Guid.NewGuid();
    private long _version = 10;

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now(); }

    public IslandTerrainTests()
    {
        _clock = new Clock(() => _now);
        (_owner, _admin, _stranger) = (User("alice"), User("bruno"), User("edu"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, Size = 16, CreatedAt = _now, UpdatedAt = _now, SnapshotVersion = _version });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now });
        _db.SaveChanges(); return id;
    }

    private static string Terrain(float height)
    {
        var map = new TerrainMap(8, 2f); map.Apply(TerrainTool.Raise, 0, 0, 6, height); return map.ToJson();
    }

    private async Task<IslandSnapshot> Save(string terrain, string environment = "{\"template\":\"tropical\"}")
    {
        var island = await _db.Islands.SingleAsync(); island.SnapshotVersion = ++_version;
        var snapshot = new IslandSnapshot { Id = Guid.NewGuid(), IslandId = _island, Version = _version, TerrainJson = terrain, EnvironmentJson = environment, IntegrityHash = IslandTerrainManagement.HashOf(_island, _version, terrain, environment), CreatedAt = _now };
        _db.IslandSnapshots.Add(snapshot); await _db.SaveChangesAsync(); return snapshot;
    }

    private async Task StartServer() { _db.ServerInstances.Add(new ServerInstance { Id = _server, IslandId = _island, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now.AddHours(-1), LastHeartbeatAt = _now }); await _db.SaveChangesAsync(); }

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };

    private static (int Status, JsonElement Body) Read(IResult result)
    {
        var status = result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, value is null ? default : JsonSerializer.SerializeToElement(value));
    }

    private async Task<(int Status, JsonElement Body)> Get(Guid who) => Read(await IslandTerrainManagement.GetAsync(_island, As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Backup(Guid who, string name) => Read(await IslandTerrainManagement.BackupAsync(_island, new BackupRequest(name), As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Restore(Guid who, Guid id, string mode = "now", int? minutes = null) => Read(await IslandTerrainManagement.RestoreAsync(_island, id, new RestoreRequest(mode, minutes), As(who), _db, _clock, default));

    [Fact]
    public async Task OnlyStaffSeeAndAct()
    {
        var save = await Save(Terrain(3));
        Assert.Equal(403, (await Get(_stranger)).Status); Assert.Equal(403, (await Backup(_stranger, "x")).Status); Assert.Equal(403, (await Restore(_stranger, save.Id)).Status);
        Assert.Equal(200, (await Get(_owner)).Status); Assert.Equal(200, (await Get(_admin)).Status);
    }

    [Fact]
    public async Task TheFiguresAreReadFromTheLatestSave()
    {
        Assert.Equal(JsonValueKind.Null, (await Get(_owner)).Body.GetProperty("current").ValueKind);
        await Save(Terrain(0)); await Save(Terrain(6));
        var current = (await Get(_owner)).Body.GetProperty("current"); var stats = current.GetProperty("stats");
        Assert.Equal(12, current.GetProperty("version").GetInt64()); Assert.Equal(12, current.GetProperty("hash").GetString()!.Length);
        Assert.Equal((8, 2.0, 16.0), (stats.GetProperty("cells").GetInt32(), stats.GetProperty("cellSize").GetDouble(), stats.GetProperty("sizeMeters").GetDouble()));
        Assert.True(stats.GetProperty("maxHeight").GetDouble() > stats.GetProperty("minHeight").GetDouble()); Assert.True(stats.GetProperty("minHeight").GetDouble() <= stats.GetProperty("avgHeight").GetDouble());
    }

    [Fact]
    public async Task ABackupKeepsTheLatestSaveUnderANameWithinLimits()
    {
        Assert.Equal(409, (await Backup(_owner, "Nada salvo")).Status);
        var save = await Save(Terrain(3));
        Assert.True((await Backup(_owner, "  ")).Status is 400 or 422); Assert.True((await Backup(_owner, new string('n', 81))).Status is 400 or 422);
        var body = (await Backup(_owner, "Antes da festa")).Body; var row = body.GetProperty("backups")[0];
        Assert.Equal("Antes da festa", row.GetProperty("name").GetString()); Assert.Equal("alice", row.GetProperty("by").GetString()); Assert.Equal(save.Version, row.GetProperty("version").GetInt64()); Assert.True(row.GetProperty("valid").GetBoolean());
        Assert.Equal(409, (await Backup(_owner, "antes da FESTA")).Status);
        for (var i = 1; i < IslandTerrainManagement.MaxBackups; i++) { await Save(Terrain(i)); Assert.Equal(200, (await Backup(_owner, "B" + i)).Status); }
        await Save(Terrain(9)); Assert.Equal(409, (await Backup(_owner, "Demais")).Status);
        Assert.Equal(1, await _db.IslandAudit.CountAsync(e => e.Action == "backup_created" && e.Summary.Contains("Antes da festa")));
    }

    [Fact]
    public async Task ABrokenSaveIsNeverKeptAsABackup()
    {
        await Save("{\"v\":1,\"data\":\"not terrain\"}");
        Assert.Equal(409, (await Backup(_owner, "Quebrado")).Status);
        await Save("not even json"); Assert.Equal(409, (await Backup(_owner, "Quebrado")).Status);
    }

    [Fact]
    public async Task DeletingABackupOnlyTakesAwayTheNameAndNotWhileItIsToBeRestored()
    {
        var save = await Save(Terrain(3)); await Backup(_owner, "A"); await StartServer();
        Assert.Equal(200, (await Restore(_owner, save.Id, "in", 5)).Status);
        Assert.Equal(409, Read(await IslandTerrainManagement.DeleteBackupAsync(_island, save.Id, As(_owner), _db, _clock, default)).Status);
        (await _db.IslandRestarts.SingleAsync()).Status = "cancelled"; await _db.SaveChangesAsync();
        Assert.Equal(204, Read(await IslandTerrainManagement.DeleteBackupAsync(_island, save.Id, As(_owner), _db, _clock, default)).Status);
        Assert.Null((await _db.IslandSnapshots.SingleAsync()).BackupName); Assert.Equal(404, Read(await IslandTerrainManagement.DeleteBackupAsync(_island, save.Id, As(_owner), _db, _clock, default)).Status);
        Assert.Equal(403, Read(await IslandTerrainManagement.DeleteBackupAsync(_island, save.Id, As(_stranger), _db, _clock, default)).Status);
    }

    [Fact]
    public async Task WithNoServerRunningARestoreIsAppliedAtOnceAsTheNewestSaveKeepingTheEnvironment()
    {
        var old = await Save(Terrain(2), "{\"template\":\"velho\"}"); await Backup(_owner, "Velho"); await Save(Terrain(9), "{\"template\":\"novo\"}");
        var (status, body) = await Restore(_owner, old.Id);
        Assert.Equal(200, status); Assert.True(body.GetProperty("applied").GetBoolean());
        var newest = await _db.IslandSnapshots.OrderByDescending(s => s.Version).FirstAsync();
        Assert.Equal(13, newest.Version); Assert.Equal(old.TerrainJson, newest.TerrainJson); Assert.Equal("{\"template\":\"novo\"}", newest.EnvironmentJson); // the terrain comes back, the rest does not
        Assert.Equal(IslandTerrainManagement.HashOf(_island, 13, newest.TerrainJson, newest.EnvironmentJson), newest.IntegrityHash);
        Assert.Equal(13, (await _db.Islands.AsNoTracking().SingleAsync()).SnapshotVersion);
        Assert.Contains(await _db.IslandAudit.Select(e => e.Action).ToListAsync(), a => a == "terrain_restored");
    }

    [Fact]
    public async Task WithAServerRunningTheRestoreWaitsForTheRestartAndTheOldServerCannotWriteOverIt()
    {
        var old = await Save(Terrain(2)); await Backup(_owner, "Velho"); await Save(Terrain(9)); await StartServer();
        Assert.True((await Restore(_owner, old.Id, "whenever")).Status is 400 or 422); Assert.True((await Restore(_owner, old.Id, "in", 3)).Status is 400 or 422);
        var versionBefore = (await _db.Islands.AsNoTracking().SingleAsync()).SnapshotVersion;
        var (status, body) = await Restore(_owner, old.Id, "in", 1); Assert.Equal(200, status); Assert.True(body.GetProperty("scheduled").GetBoolean());
        Assert.Equal(409, (await Restore(_owner, old.Id)).Status); // one at a time
        Assert.Equal(versionBefore, (await _db.Islands.AsNoTracking().SingleAsync()).SnapshotVersion); // nothing is written yet: the server still holds the ground
        var info = (await Get(_owner)).Body.GetProperty("restoring"); Assert.Equal("scheduled", info.GetProperty("status").GetString());
        _now = _now.AddSeconds(61); (await _db.ServerInstances.SingleAsync()).LastHeartbeatAt = _now; await _db.SaveChangesAsync();
        await IslandRestartService.Tick(_db, _now, _ => Task.CompletedTask, default);
        Assert.Equal("running", (await _db.IslandRestarts.SingleAsync()).Status);
        Assert.Contains(await _db.IslandServerCommands.Select(c => c.Kind).ToListAsync(), k => k == ServerCommandKinds.Shutdown);
        var newest = await _db.IslandSnapshots.OrderByDescending(s => s.Version).FirstAsync(); Assert.Equal(old.TerrainJson, newest.TerrainJson);
        Assert.True((await _db.Islands.AsNoTracking().SingleAsync()).SnapshotVersion > versionBefore); // a server that saves with the old version is now refused
        (await _db.ServerInstances.SingleAsync()).StoppedAt = _now; _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = _island, Hostname = "h2", Port = 2, Status = "healthy", StartedAt = _now.AddSeconds(20), LastHeartbeatAt = _now.AddSeconds(20) }); await _db.SaveChangesAsync();
        _now = _now.AddSeconds(30); foreach (var s in _db.ServerInstances.Where(s => s.StoppedAt == null)) s.LastHeartbeatAt = _now; await _db.SaveChangesAsync();
        await IslandRestartService.Tick(_db, _now, _ => Task.CompletedTask, default);
        var done = await _db.IslandRestarts.SingleAsync(); Assert.Equal("completed", done.Status); Assert.Contains("terreno restaurado", done.Result);
    }

    [Fact]
    public async Task AnUnreadableBackupCannotBeRestored()
    {
        var save = await Save(Terrain(2)); await Backup(_owner, "Bom"); save.TerrainJson = "{\"v\":1,\"data\":\"AAAA\"}"; await _db.SaveChangesAsync();
        Assert.Equal(409, (await Restore(_owner, save.Id)).Status);
        Assert.False((await Get(_owner)).Body.GetProperty("backups")[0].GetProperty("valid").GetBoolean());
        Assert.Equal(404, (await Restore(_owner, Guid.NewGuid())).Status);
    }
}
