using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The "properties" operation of the Geral tab: rights, group, sharing and sale, and what sharing lets other people do.</summary>
public sealed class BuildPropertiesTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid(), _other = Guid.NewGuid();

    public BuildPropertiesTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = island.Id, Hostname = "h", Status = "healthy" });
        foreach (var user in new[] { _builder, _other }) _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = user, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Cube(float x = 0, float scale = 1) => new("cube", x, 1, 0, 0, 0, 0, scale, scale, scale, "{}", "{\"collision\":true}", null, 0, "{}", "Caixa", null, null, null, null);

    private async Task<(int Status, JsonElement Body)> Send(Guid actor, string operation, BuildObjectRequest? o = null, Guid? id = null, int revision = 0, Guid[]? ids = null, string? properties = null)
    {
        JsonElement? props = properties is null ? null : JsonDocument.Parse(properties).RootElement.Clone();
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, operation, id, o, revision, ids, null, 0, 0, 0, null, null, props), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string Code(JsonElement b) => b.GetProperty("code").GetString()!;
    private async Task<Guid> Make(Guid actor) { var (s, b) = await Send(actor, "create", Cube()); Assert.Equal(200, s); return b.GetProperty("objectId").GetGuid(); }
    private async Task<BuildObject> Obj(Guid id) => await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);

    [Fact]
    public async Task TheOwnerCanLowerRightsButNeverRaiseThemAndTheNextOwnerNeverHasMore()
    {
        var id = await Make(_builder);
        var lowered = ItemRights.Copy | ItemRights.Modify;
        var (status, body) = await Send(_builder, "properties", ids: [id], properties: $"{{\"ownerRights\":{lowered},\"nextOwnerRights\":{ItemRights.All}}}");
        Assert.Equal(200, status); var made = await Obj(id);
        Assert.Equal(lowered, made.OwnerRights); Assert.Equal(lowered, made.NextOwnerRights); // the next owner is clamped to the owner
        Assert.Equal(2, made.Revision); Assert.Equal("properties", body.GetProperty("operation").GetString());
        var raised = await Send(_builder, "properties", ids: [id], properties: $"{{\"ownerRights\":{ItemRights.All}}}");
        Assert.Equal((403, "cannot_raise_rights"), (raised.Status, Code(raised.Body))); Assert.Equal(lowered, (await Obj(id)).OwnerRights);
        Assert.Equal(1, await _db.BuildOperations.CountAsync(o => o.Operation == "properties" && o.ObjectId == id));
    }

    [Fact]
    public async Task OnlyTheOwnerChangesPropertiesAndAnUnchangedRequestChangesNothing()
    {
        var id = await Make(_builder);
        var refused = await Send(_other, "properties", ids: [id], properties: "{\"anyoneMove\":true}");
        Assert.Equal((403, "not_owner"), (refused.Status, Code(refused.Body)));
        Assert.Equal(200, (await Send(_builder, "properties", ids: [id], properties: "{\"anyoneMove\":true}")).Status); var rev = (await Obj(id)).Revision;
        Assert.Equal(200, (await Send(_builder, "properties", ids: [id], properties: "{\"anyoneMove\":true}")).Status); Assert.Equal(rev, (await Obj(id)).Revision);
        Assert.Equal("invalid_operation", Code((await Send(_builder, "properties", ids: [id])).Body));
    }

    [Fact]
    public async Task AnyoneMayMoveWhenTheOwnerAllowsItButNeverChangeTheShape()
    {
        var id = await Make(_builder); var rev = (await Obj(id)).Revision;
        Assert.Equal("not_owner", Code((await Send(_other, "update", Cube(5), id, rev)).Body)); // before: not allowed
        await Send(_builder, "properties", ids: [id], properties: "{\"anyoneMove\":true}"); rev = (await Obj(id)).Revision;
        Assert.Equal(200, (await Send(_other, "update", Cube(5), id, rev)).Status); Assert.Equal(5, (await Obj(id)).PositionX);
        rev = (await Obj(id)).Revision;
        Assert.Equal("not_owner", Code((await Send(_other, "update", Cube(5, scale: 3), id, rev)).Body)); // resizing is changing what it is
        Assert.Equal(1, (await Obj(id)).ScaleX);
    }

    [Fact]
    public async Task MembersOfTheSharedGroupCanEditButOutsidersCannot()
    {
        var group = Guid.NewGuid(); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group, UserId = _builder, Role = "member" }); await _db.SaveChangesAsync();
        var id = await Make(_builder);
        var notMember = await Send(_builder, "properties", ids: [id], properties: $"{{\"groupId\":\"{Guid.NewGuid()}\"}}");
        Assert.Equal((403, "not_group_member"), (notMember.Status, Code(notMember.Body))); // only a group the owner belongs to
        Assert.Equal(200, (await Send(_builder, "properties", ids: [id], properties: $"{{\"groupId\":\"{group}\",\"groupShare\":true}}")).Status);
        var rev = (await Obj(id)).Revision; Assert.Equal(group, (await Obj(id)).GroupId);
        Assert.Equal("not_owner", Code((await Send(_other, "update", Cube(8, scale: 2), id, rev)).Body)); // _other is not in the group
        _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group, UserId = _other, Role = "member" }); await _db.SaveChangesAsync();
        Assert.Equal(200, (await Send(_other, "update", Cube(8, scale: 2), id, rev)).Status); Assert.Equal(2, (await Obj(id)).ScaleX);
        Assert.Equal(200, (await Send(_builder, "properties", ids: [id], properties: "{\"groupId\":null}")).Status); Assert.Null((await Obj(id)).GroupId);
    }

    [Fact]
    public async Task SaleNeedsTheRightToSellThatKindAndAValidPrice()
    {
        var id = await Make(_builder);
        var sale = await Send(_builder, "properties", ids: [id], properties: "{\"saleType\":\"copy\",\"salePrice\":250}");
        Assert.Equal(200, sale.Status); Assert.Equal(("copy", 250), BuildSharing.Sale((await Obj(id)).PermissionsJson));
        Assert.Equal("invalid_price", Code((await Send(_builder, "properties", ids: [id], properties: "{\"salePrice\":-1}")).Body));
        Assert.Equal("invalid_sale_type", Code((await Send(_builder, "properties", ids: [id], properties: "{\"saleType\":\"loja\"}")).Body));
        var blocked = await Send(_builder, "properties", ids: [id], properties: $"{{\"ownerRights\":{ItemRights.Modify | ItemRights.Transfer}}}"); Assert.Equal("no_copy", Code(blocked.Body)); // cannot take Copy away while selling copies
        await Send(_builder, "properties", ids: [id], properties: "{\"saleType\":\"none\"}");
        await Send(_builder, "properties", ids: [id], properties: $"{{\"ownerRights\":{ItemRights.Modify | ItemRights.Transfer}}}");
        Assert.Equal("no_copy", Code((await Send(_builder, "properties", ids: [id], properties: "{\"saleType\":\"copy\"}")).Body));
        await Send(_builder, "properties", ids: [id], properties: "{\"saleType\":\"none\"}"); Assert.Equal(("none", 0), BuildSharing.Sale((await Obj(id)).PermissionsJson));
    }

    [Fact]
    public void PhysicsMaterialAndTemporaryAreValidatedInBothRulesAndATemporaryObjectBarelyCosts()
    {
        foreach (var json in new[] { "{\"material\":\"rubber\",\"temporary\":true}", "{\"material\":\"stone\"}" }) { Assert.Null(ObjectPhysics.Validate(json)); Assert.Null(LifeVerse.Shared.Building.PhysicsRules.Validate(json)); }
        foreach (var json in new[] { "{\"material\":\"lava\"}", "{\"temporary\":\"sim\"}" }) { Assert.NotNull(ObjectPhysics.Validate(json)); Assert.NotNull(LifeVerse.Shared.Building.PhysicsRules.Validate(json)); }
        var written = LifeVerse.Shared.Building.PhysicsRules.Write("{}", new LifeVerse.Shared.Building.PhysicsSettings(Material: "glass", Temporary: true));
        Assert.Contains("glass", written); Assert.Contains("temporary", written); Assert.Equal("glass", LifeVerse.Shared.Building.PhysicsRules.Read(written).Material);
        Assert.Equal(1, BuildCost.Of("sphere", "{}", "{}", written, "[]")); Assert.True(BuildCost.Of("sphere", "{}", "{}", "{}", "[]") > 1);
        Assert.True(BuildCost.Triangles("torus", "{\"revolutions\":3}") > BuildCost.Triangles("torus", "{}"));
    }
}
