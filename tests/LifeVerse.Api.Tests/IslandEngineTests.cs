using System.Net;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.IslandSupervisor;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Godot and Unity islands side by side: each client enters only the islands of its engine, starts on one of them and each supervisor asks only for its own.</summary>
public sealed class IslandEngineTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _db.Dispose();

    private User NewUser(string name) { var u = new User { Id = Guid.NewGuid(), Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x" }; _db.Users.Add(u); _db.SaveChanges(); return u; }

    private Island NewIsland(User owner, string name, string engine, int daysOld, bool welcome = false)
    {
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = name, Slug = name.ToLowerInvariant(), IsPublic = true, Engine = engine, IsWelcome = welcome, CreatedAt = _now.AddDays(-daysOld), UpdatedAt = _now };
        _db.Islands.Add(island); _db.SaveChanges(); return island;
    }

    [Fact]
    public void TheClientSaysItsEngineAndSilenceMeansGodot()
    {
        var context = new DefaultHttpContext(); Assert.Equal(IslandEngines.Godot, IslandEngines.OfClient(context));
        context.Request.Headers[IslandEngines.Header] = "Unity"; Assert.Equal(IslandEngines.Unity, IslandEngines.OfClient(context));
        context.Request.Headers[IslandEngines.Header] = "outro"; Assert.Equal(IslandEngines.Godot, IslandEngines.OfClient(context));
        Assert.True(IslandEngines.IsValid("unity")); Assert.False(IslandEngines.IsValid("Unity")); Assert.False(IslandEngines.IsValid(null));
        Assert.Contains("nova versão", IslandEngines.Mismatch(IslandEngines.Unity)); Assert.Contains("clássico", IslandEngines.Mismatch(IslandEngines.Godot));
        Assert.Equal(IslandEngines.Godot, new Island { Name = "x", Slug = "x" }.Engine);
    }

    [Fact]
    public async Task EachClientStartsOnAnIslandOfItsEngine()
    {
        var owner = NewUser("ana"); var plaza = NewIsland(owner, "Praca", IslandEngines.Godot, 10, welcome: true); var newer = NewIsland(owner, "Nova", IslandEngines.Unity, 2); var older = NewIsland(owner, "Antiga", IslandEngines.Unity, 5);
        var godot = await PlayerLocations.StartAsync(_db, owner.Id, _now, default); Assert.Equal(plaza.Id, godot.IslandId); Assert.Equal("welcome", godot.Reason);
        var unity = await PlayerLocations.StartAsync(_db, owner.Id, _now, default, IslandEngines.Unity); Assert.Equal(older.Id, unity.IslandId); Assert.Equal("engine", unity.Reason);
        _db.PlayerLocations.Add(new PlayerLocation { UserId = owner.Id, IslandId = newer.Id, X = 1, Y = 1, Z = 1, UpdatedAt = _now }); _db.SaveChanges();
        Assert.Equal(newer.Id, (await PlayerLocations.StartAsync(_db, owner.Id, _now, default, IslandEngines.Unity)).IslandId);
        Assert.Equal(plaza.Id, (await PlayerLocations.StartAsync(_db, owner.Id, _now, default)).IslandId);
    }

    [Fact]
    public async Task WithoutAnIslandOfItsEngineTheClientIsSentToTheWelcomeIsland()
    {
        var owner = NewUser("bia"); var plaza = NewIsland(owner, "Praca", IslandEngines.Godot, 10, welcome: true);
        var unity = await PlayerLocations.StartAsync(_db, owner.Id, _now, default, IslandEngines.Unity); Assert.Equal(plaza.Id, unity.IslandId); Assert.Equal("welcome", unity.Reason);
    }

    [Fact]
    public async Task TheIslandTheServerMakesRunsOnUnity()
    {
        var made = await WelcomeIsland.ResolveAsync(_db, _now, default);
        Assert.Equal(IslandEngines.Unity, made.Engine);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public string? Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"islands\":[{\"id\":\"" + Guid.Empty + "\",\"name\":\"Nova\"}]}") });
        }
    }

    [Fact]
    public async Task TheSupervisorAsksForTheUnityIslands()
    {
        var recorder = new Recorder(); using var http = new HttpClient(recorder);
        var islands = await new HttpPendingIslands(http, "http://api.local/").GetAsync(default);
        Assert.Equal("http://api.local/api/v1/platform/supervisor/pending?engine=unity", recorder.Url); Assert.Single(islands);
    }
}
