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

/// <summary>Temporary objects: the clock starts when an object becomes temporary, editing does not restart it, switching it off cancels it, and the server removes what is due.</summary>
public sealed class TemporaryObjectTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly Guid _instance = Guid.NewGuid(); private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid();

    public TemporaryObjectTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = island.Id, Hostname = "h", Status = "healthy" });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = _builder, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Cube(string physics, Guid? parent = null, Guid? group = null) => new("cube", 0, 1, 0, 0, 0, 0, 1, 1, 1, "{}", physics, group, 0, "{}", "Caixa", null, parent, null, null);

    private async Task<(int Status, JsonElement Body)> Send(string operation, BuildObjectRequest? o = null, Guid? id = null, int revision = 0)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(_builder, operation, id, o, revision, null, null, 0, 0, 0, null, null, null), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task<Guid> Make(string physics) { var (s, b) = await Send("create", Cube(physics)); Assert.Equal(200, s); return b.GetProperty("objectId").GetGuid(); }
    private async Task<BuildObject> Obj(Guid id) => await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);

    [Fact]
    public async Task TheClockStartsWhenAnObjectBecomesTemporaryAndEditingDoesNotRestartIt()
    {
        var plain = await Make("{\"collision\":true}"); Assert.Null((await Obj(plain)).ExpiresAt);
        var id = await Make("{\"collision\":true,\"temporary\":true}"); var started = (await Obj(id)).ExpiresAt;
        Assert.NotNull(started); Assert.InRange(started!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(9), TemporaryObjects.Lifetime + TimeSpan.FromSeconds(5));
        await Task.Delay(30);
        var (status, _) = await Send("update", Cube("{\"collision\":true,\"temporary\":true}") with { PositionX = 3 }, id, 1);
        Assert.Equal(200, status); Assert.Equal(started, (await Obj(id)).ExpiresAt);
        await Send("update", Cube("{\"collision\":true}"), id, 2); Assert.Null((await Obj(id)).ExpiresAt);
        await Send("update", Cube("{\"collision\":true,\"temporary\":true}"), id, 3); Assert.NotNull((await Obj(id)).ExpiresAt);
        Assert.NotNull((await Send("update", Cube("{\"collision\":true,\"temporary\":true}"), plain, 1)).Body.GetProperty("objectState").GetProperty("expiresAt").GetString());
    }

    [Fact]
    public async Task TheSweepRemovesOnlyWhatIsDueAndRecordsIt()
    {
        var fading = await Make("{\"collision\":true,\"temporary\":true}"); var lasting = await Make("{\"collision\":true}"); var later = await Make("{\"collision\":true,\"temporary\":true}");
        Assert.Empty(await TemporaryObjects.SweepAsync(_db, DateTimeOffset.UtcNow, default)); // nothing is due yet
        var due = await _db.BuildObjects.SingleAsync(o => o.Id == fading); due.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await _db.SaveChangesAsync();
        var removed = await TemporaryObjects.SweepAsync(_db, DateTimeOffset.UtcNow, default);
        Assert.Equal([fading], removed.Select(o => o.Id));
        Assert.NotNull((await Obj(fading)).DeletedAt); Assert.Null((await Obj(lasting)).DeletedAt); Assert.Null((await Obj(later)).DeletedAt);
        Assert.Equal(1, await _db.BuildOperations.CountAsync(o => o.Operation == "expire" && o.ObjectId == fading));
        Assert.Empty(await TemporaryObjects.SweepAsync(_db, DateTimeOffset.UtcNow, default)); // and it is not removed twice
    }

    [Fact]
    public async Task APartHangingUnderAnExpiredObjectBecomesARootInsteadOfDanglingFromNothing()
    {
        var group = Guid.NewGuid(); var (_, root) = await Send("create", Cube("{\"collision\":true,\"temporary\":true}", group: group)); var rootId = root.GetProperty("objectId").GetGuid();
        var (s, child) = await Send("create", Cube("{\"collision\":true}", parent: rootId, group: group)); Assert.Equal(200, s); var childId = child.GetProperty("objectId").GetGuid();
        var due = await _db.BuildObjects.SingleAsync(o => o.Id == rootId); due.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await _db.SaveChangesAsync();
        await TemporaryObjects.SweepAsync(_db, DateTimeOffset.UtcNow, default);
        var left = await Obj(childId); Assert.Null(left.DeletedAt); Assert.Null(left.ParentObjectId);
    }
}
