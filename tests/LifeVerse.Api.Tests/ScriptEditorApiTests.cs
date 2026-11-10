using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Scripting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The script of an object as the editor in the Construir window uses it: create, read with its source, save (reactivates), see the runs, delete.</summary>
public sealed class ScriptEditorApiTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _owner = Guid.NewGuid(), _stranger = Guid.NewGuid(), _object = Guid.NewGuid();
    private readonly Island _island;
    private readonly LuaRuntimeOptions _options = new();

    public ScriptEditorApiTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island);
        _db.BuildObjects.Add(new BuildObject { Id = _object, IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Porta", PrimitiveType = "cube" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())])) };
    private static int Status(IResult result) => result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is IValueHttpResult ? 200 : 0;

    [Fact]
    public async Task ACreatedScriptCanBeReadWithItsSourceSavedAgainAndDeleted()
    {
        var created = await ScriptingEndpoints.CreateAsync(_island.Id, _object, new ObjectScriptRequest("Porta", "function on_touch(event)\n  object.say(\"oi\")\nend", "on_touch"), As(_owner), _db, _options, TimeProvider.System, default);
        Assert.Equal(201, Status(created)); var id = (await _db.ObjectScripts.AsNoTracking().SingleAsync()).Id;
        var one = await ScriptingEndpoints.GetOneAsync(_island.Id, _object, id, As(_owner), _db, default);
        var json = JsonSerializer.Serialize((one as IValueHttpResult)!.Value); Assert.Contains("on_touch", json); Assert.Contains("\"Version\":1", json);
        // stopped scripts come back to life when saved
        (await _db.ObjectScripts.SingleAsync()).Status = "stopped"; await _db.SaveChangesAsync();
        var saved = await ScriptingEndpoints.UpdateAsync(_island.Id, _object, id, new ObjectScriptRequest("Porta v2", "function on_touch(event) end", "on_touch"), As(_owner), _db, _options, TimeProvider.System, default);
        Assert.Equal(200, Status(saved)); var script = await _db.ObjectScripts.AsNoTracking().SingleAsync(); Assert.Equal(("Porta v2", 2, "active"), (script.Name, script.Version, script.Status));
        // the runs, newest first
        _db.ScriptExecutions.AddRange(new ScriptExecution { Id = Guid.NewGuid(), ScriptId = id, EventName = "on_touch", Status = "Completed", CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-5) }, new ScriptExecution { Id = Guid.NewGuid(), ScriptId = id, EventName = "on_touch", Status = "Error", Error = "attempt to index nil", CreatedAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();
        var runs = JsonSerializer.Serialize(((await ScriptingEndpoints.ExecutionsAsync(_island.Id, _object, id, As(_owner), _db, default)) as IValueHttpResult)!.Value);
        Assert.True(runs.IndexOf("attempt to index nil") < runs.IndexOf("Completed"), runs);
        Assert.Equal(204, Status(await ScriptingEndpoints.DeleteOneAsync(_island.Id, _object, id, As(_owner), _db, default)));
        Assert.Empty(await _db.ObjectScripts.ToListAsync()); Assert.Empty(await _db.ScriptExecutions.ToListAsync());
        Assert.Equal(404, Status(await ScriptingEndpoints.GetOneAsync(_island.Id, _object, id, As(_owner), _db, default)));
    }

    [Fact]
    public async Task OnlyWhoCanEditTheIslandReadsOrDeletesAScriptAndAnObjectThatCannotBeModifiedKeepsIt()
    {
        await ScriptingEndpoints.CreateAsync(_island.Id, _object, new ObjectScriptRequest("S", "function on_start(event) end"), As(_owner), _db, _options, TimeProvider.System, default);
        var id = (await _db.ObjectScripts.AsNoTracking().SingleAsync()).Id;
        Assert.NotEqual(200, Status(await ScriptingEndpoints.GetOneAsync(_island.Id, _object, id, As(_stranger), _db, default)));
        Assert.NotEqual(204, Status(await ScriptingEndpoints.DeleteOneAsync(_island.Id, _object, id, As(_stranger), _db, default)));
        var obj = await _db.BuildObjects.SingleAsync(); obj.OwnerRights = 5; await _db.SaveChangesAsync(); // no Modify right
        Assert.Equal(403, Status(await ScriptingEndpoints.DeleteOneAsync(_island.Id, _object, id, As(_owner), _db, default))); Assert.Single(await _db.ObjectScripts.ToListAsync());
    }
}
