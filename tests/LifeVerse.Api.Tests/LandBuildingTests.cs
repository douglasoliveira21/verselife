using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class BuildCostModelTests
{
    private static int Cost(string content = "[]", string material = "{}", string geometry = "{}", string physics = "{\"collision\":true}") => BuildCost.Of("cube", geometry, material, physics, content);
    private static string Entry(string category) => $"{{\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"x\",\"category\":\"{category}\"}}";

    [Fact]
    public void ScriptsAndAnimationsWeighMoreThanOtherContent()
    {
        var plain = Cost();
        Assert.Equal(plain + 8, Cost($"[{Entry("scripts")}]"));
        Assert.Equal(plain + 3, Cost($"[{Entry("animations")}]"));
        Assert.Equal(plain + 1, Cost($"[{Entry("sounds")}]"));
        Assert.Equal(plain + 8 + 3 + 1, Cost($"[{Entry("scripts")},{Entry("animations")},{Entry("textures")}]"));
        Assert.True(BuildCost.HasScripts($"[{Entry("sounds")},{Entry("scripts")}]")); Assert.False(BuildCost.HasScripts($"[{Entry("sounds")}]")); Assert.False(BuildCost.HasScripts("not json")); Assert.False(BuildCost.HasScripts(null));
    }

    [Fact]
    public void ACollisionShapeThatIsNotABoxCostsMoreAndNonSolidCostsNothingForIt()
    {
        var box = Cost();
        var breakdown = BuildCost.Breakdown("cube", "{\"hollow\":0.5}", "{}", "{\"collision\":true}", "[]");
        Assert.Equal(1, breakdown.Collision);
        Assert.Equal(0, BuildCost.Breakdown("cube", "{\"hollow\":0.5}", "{}", "{\"collision\":false}", "[]").Collision);
        Assert.Equal(0, BuildCost.Breakdown("cube", "{}", "{}", "{\"collision\":true}", "[]").Collision);
        Assert.Equal(1, BuildCost.Breakdown("cube", "{\"cutEnd\":0.5}", "{}", "{\"collision\":true}", "[]").Collision);
    }

    [Fact]
    public void SeeThroughAndGlowingMaterialsCostAPointEach()
    {
        var plain = Cost();
        Assert.Equal(plain + 1, Cost(material: "{\"transparency\":0.5}"));
        Assert.Equal(plain + 1, Cost(material: "{\"emission\":\"#FFFFFF\"}"));
        Assert.Equal(plain + 2, Cost(material: "{\"transparency\":0.5,\"emission\":\"#FFFFFF\"}"));
        Assert.Equal(plain, Cost(material: "{\"transparency\":1}"));
    }

    [Fact]
    public void TheBreakdownAddsUpToTheCostAndStaysUnderTheCap()
    {
        var parts = BuildCost.Breakdown("sphere", "{}", "{\"light\":{\"energy\":1},\"transparency\":0.4}", "{\"collision\":true}", $"[{Entry("scripts")},{Entry("animations")}]");
        Assert.Equal(parts.Total, BuildCost.Of("sphere", "{}", "{\"light\":{\"energy\":1},\"transparency\":0.4}", "{\"collision\":true}", $"[{Entry("scripts")},{Entry("animations")}]"));
        Assert.True(parts.Scripts == 8 && parts.Animations == 3 && parts.Effects == 1);
        Assert.True(BuildCost.Of("mesh", "{\"hollow\":0.9,\"twist\":180}", "{\"faces\":[" + string.Join(",", Enumerable.Repeat("{\"index\":0}", 32)) + "]}", "{}", "[" + string.Join(",", Enumerable.Range(0, 64).Select(_ => Entry("scripts"))) + "]") <= BuildCost.MaxObjectCost);
    }

    [Fact]
    public async Task RepricingUpdatesOnlyTheObjectsWhoseCostChanges()
    {
        var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LifeVerseDbContext(options);
        var island = Guid.NewGuid(); var owner = Guid.NewGuid();
        BuildObject Make(string content, int cost) => new() { Id = Guid.NewGuid(), IslandId = island, CreatorUserId = owner, OwnerUserId = owner, Name = "o", PrimitiveType = "cube", ContentJson = content, Cost = cost };
        var scripted = Make($"[{Entry("scripts")}]", BuildCost.Of("cube", "{}", "{}", "{}", "[]") + 1); // priced when a script cost one point
        var current = Make("[]", BuildCost.Of("cube", "{}", "{}", "{}", "[]"));
        db.BuildObjects.AddRange(scripted, current); await db.SaveChangesAsync();
        Assert.Equal(1, await BuildCostBackfill.RepriceAsync(db));
        Assert.Equal(BuildCost.Of("cube", "{}", "{}", "{}", scripted.ContentJson), (await db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == scripted.Id)).Cost);
        Assert.Equal(0, await BuildCostBackfill.RepriceAsync(db));
    }
}

public sealed class LandBuildingTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly DbContextOptions<LifeVerseDbContext> _options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly LifeVerseDbContext _db;
    private readonly Island _island;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid(), _visitor = Guid.NewGuid(), _tenant = Guid.NewGuid(), _admin = Guid.NewGuid(), _instance = Guid.NewGuid();
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
    private readonly Guid _parcel;

    public LandBuildingTests()
    {
        _db = new LifeVerseDbContext(_options);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _island.OwnerUserId });
        foreach (var user in new[] { _builder, _visitor, _tenant, _owner }) _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = user, Permission = "enter", PrincipalType = "user", GrantedByUserId = _island.OwnerUserId });
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        _db.SaveChanges();
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x, z));
        var claimed = LandService.ClaimAsync(_db, _island, _owner, "Casa", "", cells, 50, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(claimed.Error); _parcel = claimed.Parcel!.Id;
    }

    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Cube(float x = 2, float z = 2) => new("cube", x, 1, z, 0, 0, 0, 1, 1, 1, "{}", "{\"collision\":true}", null, 0, "{}", "Caixa", null, null, null, null);

    private async Task<int> Create(Guid actor, float x = 2, float z = 2)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, "create", null, Cube(x, z), 0, null), _db, _storage, _clock, default);
        return (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
    }

    private async Task<string?> Code(Guid actor)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, "create", null, Cube(), 0, null), _db, _storage, _clock, default);
        return (JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) is { ValueKind: JsonValueKind.Object } body && body.TryGetProperty("code", out var code)) ? code.GetString() : null;
    }

    private Task<CommerceResult> Options(string mode, bool scripts = false, int? minutes = null) => LandAdmin.SetOptionsAsync(_db, _parcel, _owner, mode, scripts, minutes, _clock, default);
    private IslandParcel Stored() => _db.IslandParcels.AsNoTracking().Single(p => p.Id == _parcel);
    private BuildObject Put(Guid owner, float x, float z, DateTimeOffset created, string permissions = "{}") { var o = new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = owner, OwnerUserId = owner, Name = "Mesa", PrimitiveType = "cube", PositionX = x, PositionY = 1, PositionZ = z, Cost = 5, PermissionsJson = permissions, PhysicsJson = "{\"collision\":true}", CreatedAt = created }; _db.BuildObjects.Add(o); _db.SaveChanges(); return o; }

    // ---- the budget ------------------------------------------------------------------------------------------

    [Fact]
    public void ParcelsGetAShareOfTheIslandLimitByArea()
    {
        Assert.Equal(76, LandBudget.DefaultObjects(_island, 1000)); // 5000 * 1000 / 65536
        Assert.Equal(10, LandBudget.DefaultObjects(_island, 16));
        Assert.Equal(5000, LandBudget.DefaultObjects(_island, 256 * 256));
        _island.ResourceLimitsJson = "{\"objects\":20000}"; Assert.Equal(20000, LandBudget.DefaultObjects(_island, 256 * 256));
        Assert.Null(LandBudget.CostBudget("{}", 100)); Assert.Equal(300, LandBudget.CostBudget("{\"parcelCostPerM2\":3}", 100)); Assert.Equal(301, LandBudget.CostBudget("{\"parcelCostPerM2\":3}", 100.2));
    }

    [Fact]
    public async Task TheParcelsOfAnIslandCannotAddUpToMoreThanItsObjectLimit()
    {
        _island.ResourceLimitsJson = "{\"objects\":100}"; _db.SaveChanges();
        IReadOnlyCollection<long> Block(int cx) => new HashSet<long> { LandGrid.Key(cx, 10), LandGrid.Key(cx + 1, 10) };
        Assert.Equal("budget_exceeded", (await LandService.ClaimAsync(_db, _island, _owner, "Grande", "", Block(10), 60, null, null, null, null, null, _clock, default)).Error); // 50 already given + 60 > 100
        Assert.Null((await LandService.ClaimAsync(_db, _island, _owner, "Cabe", "", Block(10), 50, null, null, null, null, null, _clock, default)).Error);
        Assert.Equal("budget_exceeded", (await LandService.ClaimAsync(_db, _island, _owner, "Mais um", "", Block(20), 1, null, null, null, null, null, _clock, default)).Error);
    }

    [Fact]
    public async Task AnIslandRuleCanSetTheCostBudgetByArea()
    {
        var legacy = (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single();
        Assert.Equal(50 * ParcelFacts.CostPerObjectSlot, legacy.MaxCost);
        _island.ResourceLimitsJson = "{\"parcelCostPerM2\":2}"; _db.SaveChanges();
        var byArea = (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single();
        Assert.Equal(16 * 16 * 2, byArea.MaxCost); // 256 m2 at 2 points each
        Assert.Equal(byArea.MaxCost, (await LandService.ViewAsync(_db, _island, _owner, default)).Parcels.Single().MaxCost);
    }

    // ---- who may build ---------------------------------------------------------------------------------------

    [Fact]
    public async Task BuildModes_OwnerListEveryoneNobody()
    {
        Assert.Equal(200, await Create(_owner));
        Assert.Equal("parcel_forbidden", await Code(_visitor));
        Assert.Equal("owner", Stored().BuildMode);

        Assert.True((await Options("list")).Ok);
        Assert.Equal("parcel_forbidden", await Code(_builder)); // the list is empty
        Assert.True((await LandAdmin.AddAccessAsync(_db, _parcel, _owner, _builder, "build", null, "", _clock, default)).Ok);
        Assert.Equal(200, await Create(_builder)); Assert.Equal("parcel_forbidden", await Code(_visitor));
        Assert.Equal(200, await Create(_owner));

        Assert.True((await Options("everyone")).Ok);
        Assert.Equal(200, await Create(_visitor));
        Assert.True(JsonDocument.Parse(Stored().PermissionsJson).RootElement.GetProperty("build").GetBoolean(), "the old build key follows the mode");

        Assert.True((await Options("nobody")).Ok);
        Assert.Equal("parcel_forbidden", await Code(_owner)); Assert.Equal("parcel_forbidden", await Code(_builder)); Assert.Equal("parcel_forbidden", await Code(_visitor));
        Assert.Equal(200, await Create(_admin)); // only island administrators
        Assert.False(JsonDocument.Parse(Stored().PermissionsJson).RootElement.GetProperty("build").GetBoolean());
    }

    [Fact]
    public async Task ABannedPersonMayNotBuildEvenWhereEveryoneMay()
    {
        await Options("everyone");
        Assert.Equal(200, await Create(_visitor));
        await LandAdmin.AddAccessAsync(_db, _parcel, _owner, _visitor, "ban", null, "", _clock, default);
        Assert.Equal("parcel_banned", await Code(_visitor));
        _clock.Now = _clock.Now.AddMinutes(5);
        await LandAdmin.AddAccessAsync(_db, _parcel, _owner, _builder, "ban", 1, "", _clock, default);
        _clock.Now = _clock.Now.AddMinutes(2); // a short ban ends
        Assert.Equal(200, await Create(_builder));
    }

    [Fact]
    public async Task TheTenantBuildsEvenWhenTheModeIsOwnerAndNobodyBlocksHimToo()
    {
        _db.ParcelRentals.Add(new ParcelRental { Id = Guid.NewGuid(), ParcelId = _parcel, LandlordUserId = _owner, TenantUserId = _tenant, Status = "active", StartsAt = _clock.Now, ExpiresAt = _clock.Now.AddDays(5), CreatedAt = _clock.Now }); _db.SaveChanges();
        Assert.Equal(200, await Create(_tenant));
        await Options("nobody"); Assert.Equal("parcel_forbidden", await Code(_tenant));
    }

    [Fact]
    public async Task TheBuildersListIsSeparateFromTheEntryListAndCountsAsAuthorized()
    {
        await LandAdmin.AddAccessAsync(_db, _parcel, _owner, _builder, "allow", null, "", _clock, default);
        await LandAdmin.AddAccessAsync(_db, _parcel, _owner, _builder, "build", null, "", _clock, default);
        var entries = (await LandAdmin.AccessAsync(_db, _parcel, _owner, _clock, default))!.Entries;
        Assert.Equal(2, entries.Count); Assert.Contains(entries, e => e.Kind == "allow") ; Assert.Contains(entries, e => e.Kind == "build");
        await LandAdmin.AddAccessAsync(_db, _parcel, _owner, _builder, "ban", null, "", _clock, default); // banning replaces the entry permission, not the building one
        entries = (await LandAdmin.AccessAsync(_db, _parcel, _owner, _clock, default))!.Entries;
        Assert.Contains(entries, e => e.Kind == "ban") ; Assert.DoesNotContain(entries, e => e.Kind == "allow"); Assert.Contains(entries, e => e.Kind == "build");
        var onlyBuild = Put(_visitor, 2, 2, _clock.Now);
        await LandAdmin.AddAccessAsync(_db, _parcel, _owner, _visitor, "build", null, "", _clock, default);
        Assert.True((await LandAdmin.ObjectsAsync(_db, _parcel, _owner, _clock, default))!.Single(o => o.Id == onlyBuild.Id).Authorized);
    }

    [Fact]
    public async Task OptionsAreValidatedAndOnlyTheOwnerChangesThem()
    {
        Assert.Equal("forbidden", (await LandAdmin.SetOptionsAsync(_db, _parcel, _visitor, "everyone", false, null, _clock, default)).Error);
        Assert.Equal("bad_mode", (await Options("anyone")).Error);
        Assert.Equal("bad_duration", (await Options("owner", minutes: 0)).Error); Assert.Equal("bad_duration", (await Options("owner", minutes: 10_081)).Error);
        Assert.True((await Options("list", scripts: true, minutes: 15)).Ok);
        var parcel = Stored(); Assert.Equal("list", parcel.BuildMode); Assert.Equal(15, parcel.AutoReturnMinutes);
        var view = (await LandService.ViewAsync(_db, _island, _owner, default)).Parcels.Single();
        Assert.Equal("list", view.BuildMode); Assert.True(view.AllowScripts); Assert.Equal(15, view.AutoReturnMinutes);
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == _parcel && c.Operation == "build_options");
    }

    [Fact]
    public async Task ScriptsInVisitorsObjectsFollowTheParcelRule()
    {
        var facts = (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single();
        Assert.Null(BuildRules.CanPutScripts(facts, _owner, false)); Assert.Null(BuildRules.CanPutScripts(facts, _visitor, true)); Assert.Null(BuildRules.CanPutScripts(null, _visitor, false));
        Assert.Equal("scripts_not_allowed", BuildRules.CanPutScripts(facts, _visitor, false));
        await Options("everyone", scripts: true);
        facts = (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single();
        Assert.Null(BuildRules.CanPutScripts(facts, _visitor, false));
        Assert.Null(BuildRules.CanPutScripts(facts with { TenantUserId = _tenant, AllowScripts = false }, _tenant, false));
    }

    [Fact]
    public async Task SplittingAndJoiningKeepTheBuildingRules()
    {
        var cells = new HashSet<long>(); for (var x = 0; x < 8; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(20 + x, z));
        var big = (await LandService.ClaimAsync(_db, _island, _owner, "Grande", "", cells, 40, null, null, null, null, null, _clock, default)).Parcel!.Id;
        await LandAdmin.SetOptionsAsync(_db, big, _owner, "list", false, 30, _clock, default);
        var cut = new HashSet<long>(); for (var x = 4; x < 8; x++) for (var z = 0; z < 4; z++) cut.Add(LandGrid.Key(20 + x, z));
        await LandAdmin.SplitAsync(_db, big, _owner, cut, "Metade", _clock, default);
        var half = _db.IslandParcels.AsNoTracking().Single(p => p.Name == "Metade"); Assert.Equal("list", half.BuildMode); Assert.Equal(30, half.AutoReturnMinutes);
        Assert.True((await LandAdmin.MergeAsync(_db, [big, half.Id], _owner, null, _clock, default)).Ok); // same rules on both pieces: nothing to choose
        Assert.Equal("list", _db.IslandParcels.AsNoTracking().Single(p => p.Id == big).BuildMode);
    }

    // ---- automatic return ------------------------------------------------------------------------------------

    [Fact]
    public async Task AutoReturnSendsBackOnlyOldObjectsOfPeopleWhoMayNotBuild()
    {
        await Options("everyone", minutes: 15);
        var old = _clock.Now.AddMinutes(-30); var fresh = _clock.Now.AddMinutes(-5);
        var staleVisitor = Put(_visitor, 2, 2, old); var freshVisitor = Put(_visitor, 6, 6, fresh); var staleOwner = Put(_owner, 10, 10, old);
        var staleBuilder = Put(_builder, 12, 12, old); var staleProtected = Put(_visitor, 14, 2, old, "{\"protected\":true}"); var elsewhere = Put(_visitor, 100, 100, old);
        await LandAdmin.AddAccessAsync(_db, _parcel, _owner, _builder, "build", null, "", _clock, default);
        Assert.Equal(1, await LandAutoReturn.RunAsync(_db, _storage, _clock.Now, default));
        DateTimeOffset? Deleted(Guid id) => _db.BuildObjects.AsNoTracking().Single(o => o.Id == id).DeletedAt;
        Assert.NotNull(Deleted(staleVisitor.Id));
        Assert.Null(Deleted(freshVisitor.Id)); Assert.Null(Deleted(staleOwner.Id)); Assert.Null(Deleted(staleBuilder.Id)); Assert.Null(Deleted(staleProtected.Id)); Assert.Null(Deleted(elsewhere.Id));
        Assert.Contains(_db.InventoryItems.AsNoTracking(), i => i.OwnerUserId == _visitor && i.DisplayName.StartsWith("Devolvido"));
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == _parcel && c.Operation == "auto_return");
        Assert.Equal(0, await LandAutoReturn.RunAsync(_db, _storage, _clock.Now, default));
        _clock.Now = _clock.Now.AddMinutes(11); // the fresh one ages
        Assert.Equal(1, await LandAutoReturn.RunAsync(_db, _storage, _clock.Now, default));
        Assert.NotNull(Deleted(freshVisitor.Id));
    }

    [Fact]
    public async Task AutoReturnDoesNothingWhenItIsOffAndCoversTheCellsOfTheParcelOnly()
    {
        Put(_visitor, 2, 2, _clock.Now.AddDays(-9));
        Assert.Equal(0, await LandAutoReturn.RunAsync(_db, _storage, _clock.Now, default));
        await Options("owner", minutes: 5);
        Assert.Equal(1, await LandAutoReturn.RunAsync(_db, _storage, _clock.Now, default));
        Assert.Equal(0, await LandAutoReturn.RunAsync(_db, null, _clock.Now, default)); // no storage: nothing is touched
    }

    [Fact]
    public async Task TheViewShowsWhatThirdPartiesHaveBuilt()
    {
        Put(_owner, 2, 2, _clock.Now); Put(_visitor, 6, 6, _clock.Now); Put(_builder, 10, 10, _clock.Now);
        var view = (await LandService.ViewAsync(_db, _island, _owner, default)).Parcels.Single();
        Assert.Equal(3, view.Objects); Assert.Equal(2, view.OthersObjects); Assert.Equal(10, view.OthersCost);
    }
}
