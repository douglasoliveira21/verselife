using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

/// <summary>Several people building the same island and the same linksets: stale edits are refused instead of overwriting, and the history of a linkset tells who did what.</summary>
public sealed class CollaborationTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly Island _island; private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid(), _alice = Guid.NewGuid(), _bob = Guid.NewGuid(), _stranger = Guid.NewGuid();

    public CollaborationTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        foreach (var user in new[] { _alice, _bob }) _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = user, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Box(float x, string name = "Peça", Guid? group = null, Guid? parent = null, int revision = 0, float scale = 1) =>
        new("cube", x, 1, 0, 0, 0, 0, scale, scale, scale, "{}", "{\"collision\":true}", group, revision, "{}", name, null, parent, null, null);

    private async Task<(int Status, JsonElement Body)> Send(Guid actor, string operation, BuildObjectRequest? o = null, Guid? id = null, int revision = 0, StructureEntry[]? links = null)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, operation, id, o, revision, Links: links), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task<Guid> Create(Guid actor, BuildObjectRequest o) { var (s, b) = await Send(actor, "create", o); Assert.True(s == 200, s + " " + b); return b.GetProperty("objectId").GetGuid(); }
    private async Task<BuildObject> Obj(Guid id) => await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);

    private async Task<(IResult Result, JsonElement Body)> LinksetAudit(Guid user, Guid group)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };
        var result = await BuildOperations.LinksetAuditAsync(_island.Id, group, context, _db, default);
        return (result, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task AStaleEditIsRefusedAndTheOtherPersonsWorkIsKept()
    {
        var id = await Create(_alice, Box(0, "Parede")); var seen = (await Obj(id)).Revision; // both look at the same revision
        Assert.Equal(200, (await Send(_alice, "update", Box(5, "Parede da Alice", revision: seen), id, seen)).Status);
        var (status, body) = await Send(_owner, "update", Box(9, "Parede do dono", revision: seen), id, seen); // the island owner edits from the old picture
        Assert.Equal(409, status); Assert.Equal("revision_conflict", body.GetProperty("code").GetString()); Assert.Equal(seen + 1, body.GetProperty("currentRevision").GetInt32());
        var now = await Obj(id); Assert.Equal((5f, "Parede da Alice"), (now.PositionX, now.Name));
        Assert.Equal(200, (await Send(_owner, "update", Box(9, "Parede do dono", revision: now.Revision), id, now.Revision)).Status); Assert.Equal(9f, (await Obj(id)).PositionX); // after looking again, it goes through
    }

    [Fact]
    public async Task AStaleDeleteCannotRemoveAnObjectSomeoneJustChanged()
    {
        var id = await Create(_alice, Box(0)); var seen = (await Obj(id)).Revision;
        Assert.Equal(200, (await Send(_alice, "update", Box(3, revision: seen), id, seen)).Status);
        Assert.Equal(409, (await Send(_owner, "delete", id: id, revision: seen)).Status); Assert.Null((await Obj(id)).DeletedAt);
    }

    [Fact]
    public async Task ARestructureFromAStalePictureIsRefusedWholeAndChangesNothing()
    {
        var set = Guid.NewGuid(); var root = await Create(_alice, Box(0, "Raiz", set)); var a = await Create(_alice, Box(2, "A", set, root)); var b = await Create(_alice, Box(4, "B", set, root));
        var seenA = (await Obj(a)).Revision; var seenB = (await Obj(b)).Revision;
        Assert.Equal(200, (await Send(_owner, "update", Box(2.5f, "A", set, root, seenA), a, seenA)).Status); // somebody nudges A first
        var (status, body) = await Send(_alice, "structure", links: [new StructureEntry(a, set, b, seenA), new StructureEntry(b, set, root, seenB)]);
        Assert.Equal((409, "revision_conflict"), (status, body.GetProperty("code").GetString()));
        Assert.Equal(root, (await Obj(a)).ParentObjectId); Assert.Equal(root, (await Obj(b)).ParentObjectId); Assert.Equal(seenB, (await Obj(b)).Revision); // nothing moved, not even B
    }

    [Fact]
    public async Task DeletingTheRootWhileSomeoneEditsAChildMakesTheChildsStaleEditFailAndTheLinksetStaysValid()
    {
        var set = Guid.NewGuid(); var root = await Create(_alice, Box(0, "Raiz", set)); var child = await Create(_alice, Box(2, "Filho", set, root)); await Create(_alice, Box(4, "Outro", set, root));
        var seen = (await Obj(child)).Revision; var rootRevision = (await Obj(root)).Revision;
        Assert.Equal(200, (await Send(_owner, "delete", id: root, revision: rootRevision)).Status); // the owner deletes the root: the rest is re-hung
        var (status, _) = await Send(_alice, "update", Box(3, "Filho", set, root, seen), child, seen); Assert.Equal(409, status); // Alice's picture is stale
        var members = await _db.BuildObjects.AsNoTracking().Where(o => o.GroupId == set && o.DeletedAt == null).ToListAsync();
        Assert.Equal(2, members.Count); Assert.Single(members, m => m.ParentObjectId is null); Assert.All(members, m => Assert.True(m.ParentObjectId is null || members.Any(x => x.Id == m.ParentObjectId)));
    }

    [Fact]
    public async Task TwoPeopleBuildingSideBySideNeverStepOnEachOther()
    {
        var a = new List<Guid>(); var b = new List<Guid>();
        for (var i = 0; i < 8; i++) { a.Add(await Create(_alice, Box(i, $"A{i}"))); b.Add(await Create(_bob, Box(100 + i, $"B{i}"))); }
        for (var round = 0; round < 3; round++)
            for (var i = 0; i < 8; i++)
            {
                var ra = (await Obj(a[i])).Revision; Assert.Equal(200, (await Send(_alice, "update", Box(i + round + 1, $"A{i}", revision: ra), a[i], ra)).Status);
                var rb = (await Obj(b[i])).Revision; Assert.Equal(200, (await Send(_bob, "update", Box(100 + i + round + 1, $"B{i}", revision: rb), b[i], rb)).Status);
            }
        foreach (var id in a) Assert.Equal(_alice, (await Obj(id)).OwnerUserId);
        Assert.Equal(16, await _db.BuildObjects.CountAsync());
        var revision = (await Obj(a[0])).Revision; var (denied, body) = await Send(_bob, "update", Box(0, "invadida", revision: revision), a[0], revision);
        Assert.Equal(403, denied); Assert.Equal("not_owner", body.GetProperty("code").GetString()); // building together does not mean editing each other's things
    }

    [Fact]
    public async Task TheHistoryOfALinksetShowsEveryPartAndWhoTouchedIt()
    {
        var set = Guid.NewGuid(); var root = await Create(_alice, Box(0, "Raiz", set)); var a = await Create(_alice, Box(2, "A", set, root)); var b = await Create(_alice, Box(4, "B", set, root));
        var seen = (await Obj(a)).Revision; await Send(_owner, "update", Box(3, "A", set, root, seen), a, seen); // the owner moves a part
        var revisionB = (await Obj(b)).Revision; Assert.Equal(200, (await Send(_alice, "structure", links: [new StructureEntry(b, set, a, revisionB)])).Status);
        var (ok, body) = await LinksetAudit(_alice, set); Assert.IsNotType<ForbidHttpResult>(ok);
        Assert.Equal(3, body.GetProperty("members").GetArrayLength()); var log = body.GetProperty("log").EnumerateArray().ToList();
        Assert.Contains(log, e => e.GetProperty("operation").GetString() == "structure" && e.GetProperty("objectName").GetString() == "B" && e.GetProperty("actorUserId").GetGuid() == _alice);
        Assert.Contains(log, e => e.GetProperty("operation").GetString() == "update" && e.GetProperty("objectName").GetString() == "A" && e.GetProperty("actorUserId").GetGuid() == _owner);
        Assert.Equal(3, log.Count(e => e.GetProperty("operation").GetString() == "create"));
        var times = log.Select(e => e.GetProperty("at").GetDateTimeOffset()).ToList(); Assert.Equal(times.OrderByDescending(t => t), times); // newest first
        await Send(_alice, "delete", id: b, revision: (await Obj(b)).Revision);
        var (afterDelete, afterBody) = await LinksetAudit(_alice, set); Assert.IsNotType<ForbidHttpResult>(afterDelete);
        Assert.Contains(afterBody.GetProperty("members").EnumerateArray(), m => m.GetProperty("deleted").GetBoolean() && m.GetProperty("name").GetString() == "B"); // a deleted part is still in the story
        Assert.Contains(afterBody.GetProperty("log").EnumerateArray(), e => e.GetProperty("operation").GetString() == "delete");
    }

    [Fact]
    public async Task OnlyWhoCanEditEveryPartReadsTheHistoryOfALinkset()
    {
        var set = Guid.NewGuid(); var root = await Create(_alice, Box(0, "Raiz", set)); await Create(_bob, Box(2, "Do Bob", set, root));
        Assert.IsType<ForbidHttpResult>((await LinksetAudit(_alice, set)).Result); // one of the parts is Bob's
        Assert.IsType<ForbidHttpResult>((await LinksetAudit(_bob, set)).Result);
        Assert.IsNotType<ForbidHttpResult>((await LinksetAudit(_owner, set)).Result); // the island owner may edit everything
        Assert.IsType<ForbidHttpResult>((await LinksetAudit(_stranger, set)).Result);
        Assert.IsType<NotFound>((await LinksetAudit(_owner, Guid.NewGuid())).Result);
        Assert.IsType<UnauthorizedHttpResult>(await BuildOperations.LinksetAuditAsync(_island.Id, set, new DefaultHttpContext(), _db, default));
    }
}
