using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The rules of linksets (trees of objects under a root), on their own.</summary>
public sealed class LinksetRulesTests
{
    private static Guid G(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);
    private static LinkNode N(int id, int? group = null, int? parent = null) => new(G(id), group is null ? null : G(group.Value), parent is null ? null : G(parent.Value));

    [Fact]
    public void AConsistentTreeIsValidAndEveryKindOfBreakageIsNamed()
    {
        Assert.Null(LinksetRules.Validate([N(1), N(2, 9), N(3, 9, 2), N(4, 9, 3)])); Assert.Null(LinksetRules.Validate([]));
        Assert.Equal("broken_link", LinksetRules.Validate([N(2, 9), N(3, 9, 8)])); // the parent is not there
        Assert.Equal("broken_link", LinksetRules.Validate([N(2, 9), N(3, 7, 2), N(4, 7)])); // the parent is in another linkset
        Assert.Equal("invalid_link", LinksetRules.Validate([N(2, 9), N(3, null, 2)])); // a parent without a linkset
        Assert.Equal("invalid_link", LinksetRules.Validate([N(2, 9, 2)])); // its own parent
        Assert.Equal("link_cycle", LinksetRules.Validate([N(2, 9, 3), N(3, 9, 2)]));
        Assert.Equal("link_cycle", LinksetRules.Validate([N(1, 9), N(2, 9, 4), N(3, 9, 2), N(4, 9, 3)]));
        var deep = new List<LinkNode> { N(1, 9) }; for (var i = 2; i <= LinksetRules.MaxDepth + 2; i++) deep.Add(N(i, 9, i - 1)); Assert.Equal("link_too_deep", LinksetRules.Validate(deep));
        var wide = new List<LinkNode> { N(1, 9) }; for (var i = 2; i <= LinksetRules.MaxMembers + 1; i++) wide.Add(N(i, 9, 1)); Assert.Equal("linkset_too_large", LinksetRules.Validate(wide));
        Assert.Null(LinksetRules.Validate([.. wide.Take(LinksetRules.MaxMembers)]));
    }

    [Fact]
    public void TheRootIsTheMemberWithoutAParentAndAnyoneAgreesOnWhichWhenThereAreSeveral()
    {
        var nodes = new[] { N(5, 9, 3), N(3, 9), N(7, 9) }; Assert.Equal(G(3), LinksetRules.RootOf(nodes, G(9))); Assert.Null(LinksetRules.RootOf(nodes, G(8)));
        Assert.Equal(G(2), LinksetRules.RootOf([N(4, 9), N(2, 9), N(3, 9)], G(9))); // an old group without a root: the lowest id, for everybody
        Assert.Equal([G(3), G(4)], LinksetRules.Descendants([N(1, 9), N(3, 9, 1), N(4, 9, 3), N(5, 9)], G(1)).Order()); Assert.Empty(LinksetRules.Descendants([N(1, 9), N(5, 9)], G(5)));
    }

    [Fact]
    public void LinkingPutsEveryoneUnderTheChosenRootAndKeepsTreesAlreadyBuilt()
    {
        var world = new[] { N(1), N(2), N(3) }; var plan = LinksetRules.Link(world, [G(1), G(2), G(3)], G(2), G(9));
        Assert.Equal([new LinkNode(G(1), G(9), G(2)), new LinkNode(G(2), G(9), null), new LinkNode(G(3), G(9), G(2))], plan.OrderBy(p => p.Id));
        // two linksets joined: both old roots hang under the new one, what was under them stays there
        var two = new[] { N(1, 5), N(2, 5, 1), N(3, 6), N(4, 6, 3) }; var joined = LinksetRules.Link(two, [G(1), G(2), G(3), G(4)], G(1), G(9));
        var after = two.Select(n => joined.FirstOrDefault(j => j.Id == n.Id) ?? n).ToList(); Assert.Null(LinksetRules.Validate(after)); Assert.All(after, n => Assert.Equal(G(9), n.GroupId));
        Assert.Equal(G(1), after.Single(n => n.Id == G(3)).ParentId); Assert.Equal(G(3), after.Single(n => n.Id == G(4)).ParentId); Assert.Equal(G(1), after.Single(n => n.Id == G(2)).ParentId); Assert.Null(after.Single(n => n.Id == G(1)).ParentId);
        // choosing someone deep in a tree as the root never makes a loop
        var tree = new[] { N(1, 5), N(2, 5, 1), N(3, 5, 2) }; var deep = LinksetRules.Link(tree, [G(1), G(2), G(3)], G(3), G(5)); var result = tree.Select(n => deep.FirstOrDefault(j => j.Id == n.Id) ?? n).ToList();
        Assert.Null(LinksetRules.Validate(result)); Assert.Null(result.Single(n => n.Id == G(3)).ParentId); Assert.Empty(LinksetRules.Link(tree, [G(1), G(2)], G(9), G(5))); // the root must be one of the chosen
    }

    [Fact]
    public void ChangingTheRootTurnsTheChainAroundWithoutLosingAnyone()
    {
        var tree = new[] { N(1, 5), N(2, 5, 1), N(3, 5, 2), N(4, 5, 1) }; var plan = LinksetRules.SetRoot(tree, G(3)); var after = tree.Select(n => plan.FirstOrDefault(p => p.Id == n.Id) ?? n).ToList();
        Assert.Null(LinksetRules.Validate(after)); Assert.Equal(G(3), LinksetRules.RootOf(after, G(5))); Assert.Equal(G(2), after.Single(n => n.Id == G(1)).ParentId == G(2) ? G(2) : Guid.Empty); Assert.Equal(G(1), after.Single(n => n.Id == G(4)).ParentId); // the other branch stays where it was
        Assert.Empty(LinksetRules.SetRoot(tree, G(1))); Assert.Empty(LinksetRules.SetRoot(tree, G(99))); Assert.Empty(LinksetRules.SetRoot([N(1)], G(1)));
    }

    [Fact]
    public void WhenMembersLeaveTheRestIsReHungAndNothingIsLeftBroken()
    {
        var tree = new[] { N(1, 5), N(2, 5, 1), N(3, 5, 2), N(4, 5, 1), N(5, 5, 4) };
        // a member in the middle leaves: its children hang under its parent
        var middle = Apply(tree, LinksetRules.Heal(tree, [G(2)]), G(2)); Assert.Null(LinksetRules.Validate(middle)); Assert.Equal(G(1), middle.Single(n => n.Id == G(3)).ParentId);
        // a leaf leaves: nothing else changes
        Assert.Empty(LinksetRules.Heal(tree, [G(5)]));
        // the root leaves: the child with the lowest id becomes the root and the other hangs under it
        var rootGone = Apply(tree, LinksetRules.Heal(tree, [G(1)]), G(1)); Assert.Null(LinksetRules.Validate(rootGone)); Assert.Equal(G(2), LinksetRules.RootOf(rootGone, G(5))); Assert.Equal(G(2), rootGone.Single(n => n.Id == G(4)).ParentId);
        // everything but one leaves: the one left is no longer a linkset
        var alone = Apply(tree, LinksetRules.Heal(tree, [G(1), G(2), G(4), G(5)]), G(1), G(2), G(4), G(5)); Assert.Equal(new LinkNode(G(3), null, null), alone.Single());
        Assert.Empty(LinksetRules.Heal(tree, [G(1), G(2), G(3), G(4), G(5)])); // nobody left to change
        Assert.Empty(LinksetRules.Heal([N(1), N(2)], [G(1)])); // objects that were never linked are untouched
    }

    [Fact]
    public void UnlinkingKeepsTheObjectsAndHealsTheRest()
    {
        var tree = new[] { N(1, 5), N(2, 5, 1), N(3, 5, 2), N(4, 5, 1) }; var plan = LinksetRules.Detach(tree, [G(2)]); var after = tree.Select(n => plan.FirstOrDefault(p => p.Id == n.Id) ?? n).ToList();
        Assert.Equal(new LinkNode(G(2), null, null), after.Single(n => n.Id == G(2))); Assert.Equal(G(1), after.Single(n => n.Id == G(3)).ParentId); Assert.Null(LinksetRules.Validate(after));
        var all = LinksetRules.Detach(tree, [G(1), G(2), G(3), G(4)]); Assert.All(all, n => Assert.Equal(new LinkNode(n.Id, null, null), n)); Assert.Equal(4, all.Count);
    }

    private static List<LinkNode> Apply(IEnumerable<LinkNode> world, List<LinkNode> plan, params Guid[] leaving) => [.. world.Where(n => !leaving.Contains(n.Id)).Select(n => plan.FirstOrDefault(p => p.Id == n.Id) ?? n)];
}

/// <summary>Linksets, protection and healing as the island API applies them.</summary>
public sealed class LinksetOperationTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly Island _island; private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid(), _other = Guid.NewGuid(), _admin = Guid.NewGuid();

    public LinksetOperationTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        foreach (var (user, permission) in new[] { (_builder, "build"), (_other, "build"), (_admin, "admin") }) _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = user, Permission = permission, PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Cube(float x = 0, Guid? group = null, Guid? parent = null, string name = "Caixa") => new("cube", x, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{\"collision\":true}", group, 0, "{}", name, null, parent, null, null);

    private async Task<(int Status, JsonElement Body)> Send(Guid actor, string operation, BuildObjectRequest? o = null, Guid? id = null, int revision = 0, Guid[]? ids = null, StructureEntry[]? links = null, bool? locked = null)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, operation, id, o, revision, ids, null, 0, 0, 0, links, locked), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string Code(JsonElement b) => b.GetProperty("code").GetString()!;
    private async Task<Guid> Make(Guid actor, float x = 0) { var (s, b) = await Send(actor, "create", Cube(x)); Assert.Equal(200, s); return b.GetProperty("objectId").GetGuid(); }
    private async Task<BuildObject> Obj(Guid id) => await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);
    private async Task<StructureEntry> Entry(Guid id, Guid? group, Guid? parent) => new(id, group, parent, (await Obj(id)).Revision);

    private async Task<(Guid Root, Guid A, Guid B, Guid Group)> Linkset(Guid actor)
    {
        var root = await Make(actor, 0); var a = await Make(actor, 3); var b = await Make(actor, 6); var group = Guid.NewGuid();
        var (status, body) = await Send(actor, "structure", links: [await Entry(root, group, null), await Entry(a, group, root), await Entry(b, group, a)]); Assert.Equal(200, status); Assert.Equal(3, body.GetProperty("objects").GetArrayLength());
        return (root, a, b, group);
    }

    // ---- linking -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ALinksetIsBuiltInOneStepWithARootAndATreeUnderIt()
    {
        var (root, a, b, group) = await Linkset(_builder);
        Assert.Equal((group, (Guid?)null), ((await Obj(root)).GroupId, (await Obj(root)).ParentObjectId)); Assert.Equal((group, root), ((await Obj(a)).GroupId, (await Obj(a)).ParentObjectId)); Assert.Equal((group, a), ((await Obj(b)).GroupId, (await Obj(b)).ParentObjectId));
        Assert.Equal(3, await _db.BuildOperations.CountAsync(o => o.Operation == "structure" && o.ActorUserId == _builder)); Assert.Equal(2, (await Obj(root)).Revision); // every object changed has its line in the audit and its new revision
        Assert.Equal(200, (await Send(_builder, "structure", links: [await Entry(root, group, null), await Entry(a, group, root)])).Status); Assert.Equal(2, (await Obj(root)).Revision); // asking for what already is changes nothing
    }

    [Fact]
    public async Task AMoveKeepsTheLinksetEvenIfTheUpdateSaysOtherwise()
    {
        var (root, a, _, group) = await Linkset(_builder); var rev = (await Obj(a)).Revision;
        var (status, _) = await Send(_builder, "update", Cube(10, Guid.NewGuid(), null) with { PositionX = 10 }, a, rev); Assert.Equal(200, status); var moved = await Obj(a);
        Assert.Equal((10f, group, (Guid?)root), (moved.PositionX, moved.GroupId, moved.ParentObjectId)); // groups and parents change only through "structure"
    }

    [Fact]
    public async Task ALinksetChangeIsRefusedUnlessEveryoneInvolvedMayTakePart()
    {
        var a = await Make(_builder, 0); var b = await Make(_other, 3); var group = Guid.NewGuid();
        var (s1, b1) = await Send(_builder, "structure", links: [await Entry(a, group, null), await Entry(b, group, a)]); Assert.Equal((403, "not_owner"), (s1, Code(b1))); // one of them is somebody else's
        Assert.Equal(200, (await Send(_admin, "structure", links: [await Entry(a, group, null), await Entry(b, group, a)])).Status); // an island administrator may
        var (s2, b2) = await Send(_builder, "structure", links: [new StructureEntry(a, null, null, 0)]); Assert.Equal((409, "revision_conflict"), (s2, Code(b2))); // an old view of the object
        Assert.Equal((404, "not_found"), ((await Send(_builder, "structure", links: [new StructureEntry(Guid.NewGuid(), null, null, 1)])).Status, "not_found"));
        Assert.Equal(400, (await Send(_builder, "structure", links: [])).Status); Assert.Equal(400, (await Send(_builder, "structure")).Status);
        var dup = await Entry(a, null, null); Assert.Equal(400, (await Send(_builder, "structure", links: [dup, dup])).Status);
    }

    [Fact]
    public async Task ABrokenTreeIsNeverAccepted()
    {
        var (root, a, b, group) = await Linkset(_builder); var lone = await Make(_builder, 20);
        var cycle = await Send(_builder, "structure", links: [await Entry(root, group, b)]); Assert.Equal((400, "link_cycle"), (cycle.Status, Code(cycle.Body))); // the root under its own descendant
        var other = Guid.NewGuid(); var wrongGroup = await Send(_builder, "structure", links: [await Entry(lone, other, a)]); Assert.Equal((400, "broken_link"), (wrongGroup.Status, Code(wrongGroup.Body))); // a parent from another linkset
        var noGroup = await Send(_builder, "structure", links: [await Entry(lone, null, a)]); Assert.Equal((400, "invalid_link"), (noGroup.Status, Code(noGroup.Body)));
        var orphan = await Send(_builder, "structure", links: [await Entry(a, null, null)]); Assert.Equal((400, "broken_link"), (orphan.Status, Code(orphan.Body))); // taking out the middle leaves its child hanging: the plan must include it
        var complete = LinksetRules.Detach([new LinkNode(root, group, null), new LinkNode(a, group, root), new LinkNode(b, group, a)], [a]); Assert.Equal(200, (await Send(_builder, "structure", links: [await Entry(a, null, null), .. await Task.WhenAll(complete.Where(c => c.Id != a).Select(c => Entry(c.Id, c.GroupId, c.ParentId)))])).Status);
        Assert.Equal((group, root), ((await Obj(b)).GroupId, (await Obj(b)).ParentObjectId)); Assert.Null((await Obj(a)).GroupId);
    }

    [Fact]
    public async Task ALinksetCannotGrowBeyondItsLimit()
    {
        var group = Guid.NewGuid(); var root = await Make(_builder); var r = await Obj(root); var tracked = await _db.BuildObjects.SingleAsync(o => o.Id == root); tracked.GroupId = group; await _db.SaveChangesAsync();
        for (var i = 0; i < LinksetRules.MaxMembers - 1; i++) _db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = _builder, OwnerUserId = _builder, Name = "f", PrimitiveType = "cube", GroupId = group, ParentObjectId = root, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync(); var extra = await Make(_builder, 50);
        var full = await Send(_builder, "structure", links: [await Entry(extra, group, root)]); Assert.Equal((400, "linkset_too_large"), (full.Status, Code(full.Body)));
        var create = await Send(_builder, "create", Cube(60, group, root)); Assert.Equal((400, "linkset_too_large"), (create.Status, Code(create.Body))); _ = r;
    }

    [Fact]
    public async Task AnObjectThatCannotBeModifiedCannotBeLinked()
    {
        var a = await Make(_builder, 0); var b = await Make(_builder, 3); var tracked = await _db.BuildObjects.SingleAsync(o => o.Id == b); tracked.OwnerRights = ItemRights.Copy | ItemRights.Transfer; await _db.SaveChangesAsync();
        var group = Guid.NewGuid(); var (status, body) = await Send(_builder, "structure", links: [await Entry(a, group, null), await Entry(b, group, a)]); Assert.Equal((403, "no_modify"), (status, Code(body)));
    }

    [Fact]
    public async Task ObjectsCreatedIntoALinksetFollowTheSameRules()
    {
        var (root, a, _, group) = await Linkset(_builder);
        Assert.Equal(200, (await Send(_builder, "create", Cube(30, group, a))).Status); var madeUnder = await _db.BuildObjects.AsNoTracking().FirstAsync(o => o.PositionX == 30); Assert.Equal((group, a), (madeUnder.GroupId, madeUnder.ParentObjectId));
        Assert.Equal((400, "invalid_link"), ((await Send(_builder, "create", Cube(31, null, root))).Status, "invalid_link")); Assert.Equal(400, (await Send(_builder, "create", Cube(32, group, Guid.NewGuid()))).Status);
        Assert.Equal(400, (await Send(_builder, "create", Cube(33, Guid.NewGuid(), root))).Status); // a parent from another linkset
        Assert.Equal(200, (await Send(_builder, "create", Cube(34, Guid.NewGuid()))).Status); // a group by itself is fine: the first of a new linkset
    }

    // ---- leaving a linkset ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DeletingTheRootPromotesAChildAndTellsEveryoneWhatChanged()
    {
        var (root, a, b, group) = await Linkset(_builder); var (status, body) = await Send(_builder, "delete", id: root, revision: (await Obj(root)).Revision); Assert.Equal(200, status);
        var healed = body.GetProperty("healed").EnumerateArray().ToList(); Assert.Single(healed); Assert.Equal(a, healed[0].GetProperty("id").GetGuid()); Assert.Equal(JsonValueKind.Null, healed[0].GetProperty("parentObjectId").ValueKind);
        var newRoot = await Obj(a); Assert.Equal((group, (Guid?)null), (newRoot.GroupId, newRoot.ParentObjectId)); Assert.Equal((group, a), ((await Obj(b)).GroupId, (await Obj(b)).ParentObjectId));
        Assert.Contains(_db.BuildOperations, o => o.ObjectId == a && o.Operation == "structure" && o.PayloadJson.Contains("member left"));
    }

    [Fact]
    public async Task DeletingTheOnlyOtherMemberDissolvesTheLinkset()
    {
        var root = await Make(_builder, 0); var child = await Make(_builder, 3); var group = Guid.NewGuid(); await Send(_builder, "structure", links: [await Entry(root, group, null), await Entry(child, group, root)]);
        var (status, body) = await Send(_builder, "delete", id: child, revision: (await Obj(child)).Revision); Assert.Equal(200, status); Assert.Equal(root, body.GetProperty("healed")[0].GetProperty("id").GetGuid());
        Assert.Null((await Obj(root)).GroupId); Assert.Null((await Obj(root)).ParentObjectId);
    }

    [Fact]
    public async Task ReturningTheMiddleOfALinksetReHangsItsChildren()
    {
        var (root, a, b, group) = await Linkset(_builder); var (status, body) = await Send(_builder, "return", ids: [a]); Assert.Equal(200, status);
        Assert.Equal(b, body.GetProperty("healed")[0].GetProperty("id").GetGuid()); Assert.Equal((group, root), ((await Obj(b)).GroupId, (await Obj(b)).ParentObjectId)); Assert.NotNull((await Obj(a)).DeletedAt);
    }

    // ---- protection -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AProtectedObjectCannotBeMovedChangedLinkedDeletedOrReturnedByItsOwner()
    {
        var id = await Make(_builder, 0); var other = await Make(_builder, 5); var (ps, pb) = await Send(_builder, "protect", ids: [id], locked: true); Assert.Equal(200, ps); Assert.True(pb.GetProperty("objects")[0].GetProperty("editLocked").GetBoolean());
        var rev = (await Obj(id)).Revision;
        Assert.Equal((403, "object_protected"), ((await Send(_builder, "update", Cube(9), id, rev)).Status, Code((await Send(_builder, "update", Cube(9), id, rev)).Body))); // not even a move
        Assert.Equal("object_protected", Code((await Send(_builder, "delete", id: id, revision: rev)).Body)); Assert.Equal("object_protected", Code((await Send(_builder, "return", ids: [id])).Body));
        var group = Guid.NewGuid(); Assert.Equal("object_protected", Code((await Send(_builder, "structure", links: [await Entry(id, group, null), await Entry(other, group, id)])).Body));
        var admin = await Send(_admin, "return", ids: [id]); Assert.Equal(200, admin.Status); // whoever manages the land may still clear it
    }

    [Fact]
    public async Task OnlyWhoMayEditAnObjectCanProtectOrUnprotectIt()
    {
        var id = await Make(_builder); var theirs = await Send(_other, "protect", ids: [id], locked: true); Assert.Equal((403, "not_owner"), (theirs.Status, Code(theirs.Body))); Assert.False((await Obj(id)).EditLocked);
        Assert.Equal(200, (await Send(_builder, "protect", ids: [id], locked: true)).Status); Assert.Equal("not_owner", Code((await Send(_other, "protect", ids: [id], locked: false)).Body)); Assert.True((await Obj(id)).EditLocked);
        var rev = (await Obj(id)).Revision; Assert.Equal(200, (await Send(_builder, "protect", ids: [id], locked: true)).Status); Assert.Equal(rev, (await Obj(id)).Revision); // again: nothing changes
        Assert.Equal(200, (await Send(_admin, "protect", ids: [id], locked: false)).Status); Assert.False((await Obj(id)).EditLocked); Assert.Equal(200, (await Send(_builder, "update", Cube(4), id, (await Obj(id)).Revision)).Status);
        Assert.Equal(400, (await Send(_builder, "protect", ids: [id])).Status); Assert.Equal(404, (await Send(_builder, "protect", ids: [Guid.NewGuid()], locked: true)).Status); Assert.Equal(400, (await Send(_builder, "protect", locked: true)).Status);
        Assert.Contains(_db.BuildOperations, o => o.Operation == "protect" && o.ObjectId == id); Assert.Contains(_db.BuildOperations, o => o.Operation == "unprotect" && o.ObjectId == id);
    }
}
