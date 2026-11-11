using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Recursos of an object on the server: reflection, light shadows and mesh animation are checked, priced, and the animation clock is the server's.</summary>
public sealed class ObjectResourcesTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly Guid _instance = Guid.NewGuid(); private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid();

    public ObjectResourcesTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = island.Id, Hostname = "h", Status = "healthy" });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = _builder, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Cube(string material) => new("cube", 0, 1, 0, 0, 0, 0, 1, 1, 1, material, "{\"collision\":true}", null, 0, "{}", "Modelo", null, null, null, null);

    private async Task<(int Status, JsonElement Body)> Send(string operation, BuildObjectRequest? o = null, Guid? id = null, int revision = 0)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(_builder, operation, id, o, revision, null, null, 0, 0, 0, null, null, null), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task<BuildObject> Obj(Guid id) => await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);
    private static long Since(BuildObject o) => JsonDocument.Parse(o.MaterialJson).RootElement.GetProperty("animation").GetProperty("since").GetInt64();
    private const string Playing = "{\"animation\":{\"clip\":\"Andar\",\"playing\":true,\"loop\":false,\"speed\":1}}";

    [Fact]
    public async Task TheServerStampsWhenAnAnimationStartedAndKeepsTheStampWhileItIsUnchanged()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var (status, body) = await Send("create", Cube(Playing)); Assert.Equal(200, status); var id = body.GetProperty("objectId").GetGuid();
        var first = Since(await Obj(id)); Assert.InRange(first, before, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await Task.Delay(40);
        var withSince = (await Obj(id)).MaterialJson; Assert.Equal(200, (await Send("update", Cube(withSince) with { Name = "Outro nome" }, id, 1)).Status);
        Assert.Equal(first, Since(await Obj(id))); // renaming does not restart the clip
        Assert.Equal(200, (await Send("update", Cube(Playing), id, 2)).Status); // the client forgot the stamp: it is kept, not made up again
        Assert.Equal(first, Since(await Obj(id)));
        await Task.Delay(40);
        Assert.Equal(200, (await Send("update", Cube("{\"animation\":{\"clip\":\"Andar\",\"playing\":false,\"loop\":false,\"speed\":1,\"offset\":0.4}}"), id, 3)).Status);
        Assert.True(Since(await Obj(id)) > first); // pausing is a change: the moment is new
        Assert.Equal(200, (await Send("update", Cube("{}"), id, 4)).Status); Assert.DoesNotContain("animation", (await Obj(id)).MaterialJson);
    }

    [Theory]
    [InlineData("{\"animation\":{\"clip\":\"Andar\",\"speed\":9}}", false)]
    [InlineData("{\"animation\":{\"clip\":\"\",\"speed\":1}}", false)]
    [InlineData("{\"animation\":{\"clip\":\"Andar\",\"playing\":\"sim\"}}", false)]
    [InlineData("{\"animation\":\"andar\"}", false)]
    [InlineData("{\"animation\":{\"clip\":\"Andar\",\"playing\":true,\"loop\":true,\"speed\":2}}", true)]
    [InlineData("{\"reflection\":{\"intensity\":0.5,\"range\":8}}", true)]
    [InlineData("{\"reflection\":{\"intensity\":2,\"range\":8}}", false)]
    [InlineData("{\"reflection\":{\"intensity\":0.5,\"range\":100}}", false)]
    [InlineData("{\"reflection\":3}", false)]
    [InlineData("{\"light\":{\"energy\":2,\"range\":8,\"shadow\":true}}", true)]
    [InlineData("{\"light\":{\"energy\":2,\"range\":8,\"shadow\":\"sim\"}}", false)]
    public async Task ResourcesOutsideTheirRangesAreRefused(string material, bool accepted)
    {
        var (status, body) = await Send("create", Cube(material));
        Assert.Equal(accepted ? 200 : 400, status);
        if (!accepted) Assert.Equal("invalid_object", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReflectionShadowsAndAnimationWeighOnTheBudget()
    {
        async Task<int> CostOf(string material) { var (_, body) = await Send("create", Cube(material)); return (await Obj(body.GetProperty("objectId").GetGuid())).Cost; }
        var plain = await CostOf("{}"); var lit = await CostOf("{\"light\":{\"energy\":2,\"range\":8}}"); var shadowed = await CostOf("{\"light\":{\"energy\":2,\"range\":8,\"shadow\":true}}");
        Assert.True(shadowed > lit && lit > plain, $"{plain} < {lit} < {shadowed}");
        Assert.True(await CostOf("{\"reflection\":{\"intensity\":0.5,\"range\":8}}") >= plain + 6);
        Assert.True(await CostOf(Playing) >= plain + 4);
    }
}
