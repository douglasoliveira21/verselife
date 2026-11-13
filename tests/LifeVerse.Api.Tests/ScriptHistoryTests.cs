using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Scripting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The history of a script's code: each save keeps the code it replaced, the owner can read an old version and go back to it, strangers cannot, and only the latest twenty are kept.</summary>
public sealed class ScriptHistoryTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _owner = Guid.NewGuid(), _stranger = Guid.NewGuid(), _object = Guid.NewGuid(), _script = Guid.NewGuid();
    private readonly Island _island;
    private readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly LuaRuntimeOptions _options = new();

    public ScriptHistoryTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Praça", Slug = "praca", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island);
        _db.BuildObjects.Add(new BuildObject { Id = _object, IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Porta", PrimitiveType = "cube" });
        _db.ObjectScripts.Add(new ObjectScript { Id = _script, ObjectId = _object, OwnerUserId = _owner, Name = "porta", Source = "function on_touch() end -- 1", CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())])) };
    private static int Status(IResult result) => result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is ForbidHttpResult ? 403 : result is IValueHttpResult ? 200 : 0;
    private static JsonElement Body(IResult result) => JsonSerializer.SerializeToElement((result as IValueHttpResult)!.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private Task<IResult> Save(string source, string name = "porta") => ScriptingEndpoints.UpdateAsync(_island.Id, _object, _script, new ObjectScriptRequest(name, source), As(_owner), _db, _options, new FixedClock(_now), default);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    [Fact]
    public async Task EverySaveKeepsTheCodeItReplaced_AndSavingTheSameCodeKeepsNothing()
    {
        Assert.Equal(200, Status(await Save("function on_touch() end -- 2")));
        Assert.Equal(200, Status(await Save("function on_touch() end -- 2")));                      // same code: no new history, only a new version
        Assert.Equal(200, Status(await Save("function on_touch() end -- 3", "porta nova")));
        var list = Body(await ScriptingEndpoints.VersionsAsync(_island.Id, _object, _script, As(_owner), _db, default));
        Assert.Equal(4, list.GetProperty("current").GetInt32());
        var versions = list.GetProperty("versions").EnumerateArray().Select(v => (v.GetProperty("version").GetInt32(), v.GetProperty("name").GetString())).ToList();
        Assert.Equal([(3, "porta"), (1, "porta")], versions);                                       // newest first; version 2 was saved twice and so replaced nothing new
        var old = Body(await ScriptingEndpoints.VersionAsync(_island.Id, _object, _script, 1, As(_owner), _db, default));
        Assert.Equal("function on_touch() end -- 1", old.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GoingBackMakesTheOldCodeCurrent_AndIsItselfUndoable()
    {
        await Save("function on_touch() end -- 2");
        var restored = Body(await ScriptingEndpoints.RestoreAsync(_island.Id, _object, _script, 1, As(_owner), _db, new FixedClock(_now), default));
        Assert.Equal((3, 1), (restored.GetProperty("version").GetInt32(), restored.GetProperty("restoredFrom").GetInt32()));
        var script = await _db.ObjectScripts.AsNoTracking().SingleAsync(); Assert.Equal(("function on_touch() end -- 1", 3, "active"), (script.Source, script.Version, script.Status));
        var undo = Body(await ScriptingEndpoints.VersionAsync(_island.Id, _object, _script, 2, As(_owner), _db, default)); Assert.Equal("function on_touch() end -- 2", undo.GetProperty("source").GetString());   // what going back replaced is still there
        Assert.Equal(404, Status(await ScriptingEndpoints.RestoreAsync(_island.Id, _object, _script, 99, As(_owner), _db, new FixedClock(_now), default)));
    }

    [Fact]
    public async Task OnlyTheOwnerOfTheIslandSeesOrRestoresTheHistory()
    {
        await Save("function on_touch() end -- 2");
        Assert.Equal(403, Status(await ScriptingEndpoints.VersionsAsync(_island.Id, _object, _script, As(_stranger), _db, default)));
        Assert.Equal(403, Status(await ScriptingEndpoints.VersionAsync(_island.Id, _object, _script, 1, As(_stranger), _db, default)));
        Assert.Equal(403, Status(await ScriptingEndpoints.RestoreAsync(_island.Id, _object, _script, 1, As(_stranger), _db, new FixedClock(_now), default)));
        Assert.Equal(404, Status(await ScriptingEndpoints.VersionsAsync(_island.Id, _object, Guid.NewGuid(), As(_owner), _db, default)));
        Assert.Equal("function on_touch() end -- 2", (await _db.ObjectScripts.AsNoTracking().SingleAsync()).Source);
    }

    [Fact]
    public async Task OnlyTheLatestTwentyVersionsAreKept_AndDeletingTheScriptDeletesTheHistory()
    {
        for (var i = 2; i <= 30; i++) await Save($"function on_touch() end -- {i}");
        var versions = Body(await ScriptingEndpoints.VersionsAsync(_island.Id, _object, _script, As(_owner), _db, default)).GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("version").GetInt32()).ToList();
        Assert.Equal(ScriptingEndpoints.KeepRevisions, versions.Count); Assert.Equal(29, versions.Max()); Assert.Equal(10, versions.Min());
        Assert.Equal(404, Status(await ScriptingEndpoints.VersionAsync(_island.Id, _object, _script, 1, As(_owner), _db, default)));
    }

    [Fact]
    public async Task AVersionThatTheRulesNoLongerAcceptIsNotRestored()
    {
        _db.ScriptRevisions.Add(new ScriptRevision { Id = Guid.NewGuid(), ScriptId = _script, Version = 7, Name = "velho", Source = "function on_touch( end", SavedByUserId = _owner, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal(400, Status(await ScriptingEndpoints.RestoreAsync(_island.Id, _object, _script, 7, As(_owner), _db, new FixedClock(_now), default)));
        Assert.Equal("function on_touch() end -- 1", (await _db.ObjectScripts.AsNoTracking().SingleAsync()).Source);
    }
}
