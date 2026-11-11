using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Scripting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What the Conteúdo tab needs from the scripting API: Lua syntax errors with their line, a validation that saves nothing, and a reset that restarts without losing the source.</summary>
public sealed class ScriptContentApiTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _owner = Guid.NewGuid(), _stranger = Guid.NewGuid(), _object = Guid.NewGuid();
    private readonly Island _island;
    private readonly LuaRuntimeOptions _options = new();

    public ScriptContentApiTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island);
        _db.BuildObjects.Add(new BuildObject { Id = _object, IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Porta", PrimitiveType = "cube" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())])) };
    private static int Status(IResult result) => result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is IValueHttpResult ? 200 : 0;
    private static string Json(IResult result) => JsonSerializer.Serialize((result as IValueHttpResult)!.Value);

    [Fact]
    public void TheSyntaxCheckAcceptsLuaAndReportsTheLineOfAnError()
    {
        Assert.Null(LuaSyntax.Check("function on_touch(event)\n  object.say(\"oi\")\nend"));
        var error = LuaSyntax.Check("function on_touch(event)\n  local x = \nend end");
        Assert.NotNull(error); Assert.True(error!.Line >= 2, error.ToString()); Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Contains("linha", error.ToString());
        Assert.NotNull(LuaSyntax.Check("x = = 1"));
    }

    [Fact]
    public async Task ASourceWithASyntaxErrorIsNotSavedAndTheMessageNamesTheLine()
    {
        var bad = await ScriptingEndpoints.CreateAsync(_island.Id, _object, new ObjectScriptRequest("Quebrado", "function on_touch(event)\n  if then\nend", "on_touch"), As(_owner), _db, _options, TimeProvider.System, default);
        Assert.Equal(400, Status(bad)); Assert.Contains("linha", string.Join(" ", ((bad as ProblemHttpResult)!.ProblemDetails as HttpValidationProblemDetails)!.Errors["source"]));
        Assert.Empty(await _db.ObjectScripts.ToListAsync());
        await ScriptingEndpoints.CreateAsync(_island.Id, _object, new ObjectScriptRequest("Bom", "function on_touch(event) end", "on_touch"), As(_owner), _db, _options, TimeProvider.System, default);
        var id = (await _db.ObjectScripts.SingleAsync()).Id;
        var update = await ScriptingEndpoints.UpdateAsync(_island.Id, _object, id, new ObjectScriptRequest("Bom", "function on_touch(event) +++ end", "on_touch"), As(_owner), _db, _options, TimeProvider.System, default);
        Assert.Equal(400, Status(update));
        var kept = await _db.ObjectScripts.AsNoTracking().SingleAsync(); Assert.Equal("function on_touch(event) end", kept.Source); Assert.Equal(1, kept.Version);
    }

    [Fact]
    public async Task ValidateAnswersWithoutSavingAndOnlyForWhoCanEdit()
    {
        var good = Json(await ScriptingEndpoints.ValidateAsync(_island.Id, _object, new ScriptingEndpoints.ValidateScriptRequest("function on_start(event) end"), As(_owner), _db, _options, default));
        Assert.Contains("\"valid\":true", good);
        var bad = Json(await ScriptingEndpoints.ValidateAsync(_island.Id, _object, new ScriptingEndpoints.ValidateScriptRequest("a = = 1"), As(_owner), _db, _options, default));
        Assert.Contains("\"valid\":false", bad); Assert.Contains("\"line\":1", bad);
        var unsafeSource = Json(await ScriptingEndpoints.ValidateAsync(_island.Id, _object, new ScriptingEndpoints.ValidateScriptRequest("os.execute('x')"), As(_owner), _db, _options, default));
        Assert.Contains("\"valid\":false", unsafeSource);
        Assert.NotEqual(200, Status(await ScriptingEndpoints.ValidateAsync(_island.Id, _object, new ScriptingEndpoints.ValidateScriptRequest("x = 1"), As(_stranger), _db, _options, default)));
        Assert.Empty(await _db.ObjectScripts.ToListAsync());
    }

    [Fact]
    public async Task ResetRestartsTheScriptsClearsTheirStateAndNeverTouchesTheSource()
    {
        await ScriptingEndpoints.CreateAsync(_island.Id, _object, new ObjectScriptRequest("A", "function on_start(event) end"), As(_owner), _db, _options, TimeProvider.System, default);
        await ScriptingEndpoints.CreateAsync(_island.Id, _object, new ObjectScriptRequest("B", "function on_touch(event) end", "on_touch"), As(_owner), _db, _options, TimeProvider.System, default);
        var scripts = await _db.ObjectScripts.ToListAsync(); scripts[0].Status = "quarantined"; _db.ScriptStates.Add(new ScriptState { Id = Guid.NewGuid(), ScriptId = scripts[0].Id, Key = "n", ValueJson = "3", UpdatedAt = DateTimeOffset.UtcNow }); await _db.SaveChangesAsync();
        var sources = scripts.ToDictionary(x => x.Id, x => x.Source);

        Assert.NotEqual(200, Status(await ScriptingEndpoints.ResetAllAsync(_island.Id, _object, As(_stranger), _db, TimeProvider.System, default)));
        Assert.Equal(1, (await _db.ObjectScripts.AsNoTracking().FirstAsync()).Version);

        Assert.Contains("\"reset\":2", Json(await ScriptingEndpoints.ResetAllAsync(_island.Id, _object, As(_owner), _db, TimeProvider.System, default)));
        var after = await _db.ObjectScripts.AsNoTracking().ToListAsync();
        Assert.All(after, s => { Assert.Equal(2, s.Version); Assert.Equal("active", s.Status); Assert.Equal(sources[s.Id], s.Source); });
        Assert.Empty(await _db.ScriptStates.ToListAsync());
        Assert.Equal(2, await _db.ScriptExecutions.CountAsync(x => x.EventName == "reset"));

        var one = after[0].Id;
        Assert.Equal(200, Status(await ScriptingEndpoints.ResetOneAsync(_island.Id, _object, one, As(_owner), _db, TimeProvider.System, default)));
        Assert.Equal(3, (await _db.ObjectScripts.AsNoTracking().SingleAsync(x => x.Id == one)).Version);
        Assert.Equal(404, Status(await ScriptingEndpoints.ResetOneAsync(_island.Id, _object, Guid.NewGuid(), As(_owner), _db, TimeProvider.System, default)));
    }
}
