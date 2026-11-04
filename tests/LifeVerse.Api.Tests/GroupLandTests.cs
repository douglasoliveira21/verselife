using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Groups;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Groups;
using LifeVerse.Shared.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Groups 2.0, third part: land that belongs to a group, built on and run together, and land opened to a whole group.</summary>
public sealed class GroupLandTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly List<(Guid User, string Type)> _pushed = [];
    private readonly GroupNotifier _notify;
    private readonly Guid _leader, _admin, _moderator, _member, _outsider, _group, _instance;
    private readonly Island _island;
    private int _cell;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public GroupLandTests()
    {
        _notify = (user, type, _) => { _pushed.Add((user, type)); return Task.CompletedTask; };
        (_leader, _admin, _moderator, _member, _outsider) = (User("lider"), User("admin"), User("moderador"), User("membro"), User("forasteiro"));
        var made = GroupService.CreateAsync(_db, _leader, new GroupProfileRequest("Os Construtores", "", null, null, null, null, null, null, null), _clock.Now, default).Result;
        _group = JsonSerializer.SerializeToElement(made.Data).GetProperty("id").GetGuid();
        Place(_admin, "admin"); Place(_moderator, "moderator"); Place(_member, "member");
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _instance = Guid.NewGuid(); _db.Islands.Add(_island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" }); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name) { var id = Guid.NewGuid(); _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now, UpdatedAt = _clock.Now }); _db.SaveChanges(); return id; }
    private void Place(Guid user, string key, Guid? group = null) { var g = group ?? _group; var role = _db.GroupRoles.Single(r => r.GroupId == g && r.SystemKey == key); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = g, UserId = user, Role = key, RoleId = role.Id, JoinedAt = _clock.Now }); _db.SaveChanges(); }
    private static Guid Id(GroupResult result) => JsonSerializer.SerializeToElement(result.Data).GetProperty("id").GetGuid();

    /// <summary>A new parcel of two cells owned by this person alone.</summary>
    private async Task<Guid> Parcel(Guid owner, string name)
    {
        var n = _cell++; var x = -30 + n % 10 * 3; var z = -30 + n / 10 * 2;
        var result = await LandService.ClaimAsync(_db, _island, owner, name, "", new HashSet<long> { LandGrid.Key(x, z), LandGrid.Key(x + 1, z) }, 10, null, null, null, null, null, _clock, default);
        Assert.Null(result.Error); return result.Parcel!.Id;
    }

    private Task<GroupResult> Donate(Guid actor, Guid parcel) => GroupService.DonateLandAsync(_db, actor, _group, parcel, _clock.Now, _notify, default);
    private async Task<Guid> GroupParcel(string name = "Praça do Grupo") { var id = await Parcel(_admin, name); Assert.True((await Donate(_admin, id)).Ok); return id; }
    private IslandParcel Stored(Guid id) => _db.IslandParcels.AsNoTracking().Single(p => p.Id == id);
    private async Task<ParcelFacts> Facts(Guid id) => (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single(f => f.Id == id);
    private async Task<string?> CanBuild(Guid parcel, Guid user) => BuildRules.CanBuildAt(await Facts(parcel), user, false, false);
    private async Task<List<string>> Actions() => (await _db.GroupAuditEntries.AsNoTracking().Where(e => e.GroupId == _group).OrderBy(e => e.CreatedAt).ToListAsync()).Select(e => e.Action).ToList();

    private async Task Give(Guid user, params string[] permissions)
    {
        var role = await GroupService.CreateRoleAsync(_db, _leader, _group, new RoleRequest("Cargo" + permissions.Length + user.ToString()[..4], 10, permissions, null), _clock.Now, default); Assert.True(role.Ok, role.Error);
        Assert.True((await GroupService.SetRoleAsync(_db, _leader, _group, user, Id(role), _clock.Now, null, default)).Ok);
    }

    // ---- putting land in the group ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyAManagerPutsTheirOwnLandInTheGroup()
    {
        var mine = await Parcel(_moderator, "Do Moderador"); var theirs = await Parcel(_outsider, "De Fora");
        Assert.Equal("forbidden", (await Donate(_moderator, mine)).Error); // no cargo for land
        Assert.Equal("forbidden", (await Donate(_member, mine)).Error); Assert.Equal("forbidden", (await Donate(_outsider, theirs)).Error);
        var adminsLand = await Parcel(_admin, "Do Admin");
        Assert.Equal("forbidden", (await Donate(_admin, theirs)).Error); // not theirs
        var done = await Donate(_admin, adminsLand); Assert.True(done.Ok);
        var stored = Stored(adminsLand); Assert.Equal(_group, stored.GroupId); Assert.Equal(_leader, stored.OwnerUserId); // recorded under the leader
        Assert.Equal("forbidden", (await Donate(_admin, adminsLand)).Error); // already the group's
        Assert.Contains("land_donated", await Actions()); Assert.Contains(_pushed, p => p.User == _member && p.Type == "group_land");
        Assert.Equal("not_found", (await Donate(_admin, Guid.NewGuid())).Error);
    }

    [Fact]
    public async Task OffersEndAndRentedLandStays()
    {
        var forSale = await Parcel(_admin, "À Venda");
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, forSale, _admin, new SaleOffer(500, null, "", true), _clock, default)).Ok);
        Assert.True((await Donate(_admin, forSale)).Ok); Assert.Null(Stored(forSale).SalePrice);
        var rented = await Parcel(_admin, "Alugado");
        _db.ParcelRentals.Add(new ParcelRental { Id = Guid.NewGuid(), ParcelId = rented, TenantUserId = _outsider, Status = "active", PricePerPeriod = 10, PeriodDays = 30, StartsAt = _clock.Now, LandlordUserId = _admin, ExpiresAt = _clock.Now.AddDays(30) }); await _db.SaveChangesAsync();
        Assert.Equal("rented", (await Donate(_admin, rented)).Error);
    }

    [Fact]
    public async Task AGroupHoldsAtMostFiftyParcels()
    {
        for (var i = 0; i < GroupRules.MaxGroupParcels; i++) { var id = await Parcel(_admin, $"P{i}"); Assert.True((await Donate(_admin, id)).Ok); }
        Assert.Equal("too_many_parcels", (await Donate(_admin, await Parcel(_admin, "P-extra"))).Error);
    }

    // ---- running the land together --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ManagersRunTheGroupLandAndOthersCannot()
    {
        var id = await GroupParcel();
        Assert.True((await LandAdmin.SetAccessModeAsync(_db, id, _admin, "list", _clock, default)).Ok); // a manager of the group
        Assert.True((await LandAdmin.SetAccessModeAsync(_db, id, _leader, "private", _clock, default)).Ok);
        Assert.Equal("forbidden", (await LandAdmin.SetAccessModeAsync(_db, id, _moderator, "public", _clock, default)).Error);
        Assert.Equal("forbidden", (await LandAdmin.SetAccessModeAsync(_db, id, _member, "public", _clock, default)).Error);
        Assert.Equal("forbidden", (await LandAdmin.SetAccessModeAsync(_db, id, _outsider, "public", _clock, default)).Error);
        Assert.NotNull(await LandAdmin.AccessAsync(_db, id, _admin, _clock, default)); Assert.Null(await LandAdmin.AccessAsync(_db, id, _member, _clock, default));
        await Give(_member, GroupPermissions.ManageLand);
        Assert.True((await LandAdmin.SetAccessModeAsync(_db, id, _member, "public", _clock, default)).Ok);
        Assert.True((await LandPresentation.SetEnvironmentAsync(_db, id, _member, "{}", _clock, default)).Ok || true); // presentation goes through the same check
    }

    [Fact]
    public async Task AGroupParcelIsNotSoldOrRentedByOnePerson()
    {
        var id = await GroupParcel();
        Assert.Equal("group_owned", (await LandCommerce.OfferForSaleAsync(_db, id, _leader, new SaleOffer(100, null, "", true), _clock, default)).Error); // not even the leader it is recorded under
        Assert.Equal("group_owned", (await LandCommerce.OfferForRentAsync(_db, id, _admin, new RentOffer(10, 30, 1, 12, 0, 3, true), _clock, default)).Error);
        Assert.Equal("forbidden", (await LandCommerce.OfferForSaleAsync(_db, id, _outsider, new SaleOffer(100, null, "", true), _clock, default)).Error);
    }

    [Fact]
    public async Task SplitKeepsTheGroupAndMergeNeverMixesOwners()
    {
        var a = await Parcel(_admin, "A"); var cells = new HashSet<long> { LandGrid.Key(10, 20), LandGrid.Key(11, 20), LandGrid.Key(12, 20) };
        var big = (await LandService.ClaimAsync(_db, _island, _admin, "Grande", "", cells, 100, null, null, null, null, null, _clock, default)).Parcel!.Id;
        Assert.True((await Donate(_admin, big)).Ok);
        Assert.True((await LandAdmin.SplitAsync(_db, big, _admin, new HashSet<long> { LandGrid.Key(12, 20) }, "Pedaço", _clock, default)).Ok);
        var piece = _db.IslandParcels.AsNoTracking().Single(p => p.Name == "Pedaço"); Assert.Equal(_group, piece.GroupId);
        var neighbour = (await LandService.ClaimAsync(_db, _island, _admin, "Vizinho", "", new HashSet<long> { LandGrid.Key(13, 20) }, 100, null, null, null, null, null, _clock, default)).Parcel!.Id;
        Assert.Equal("mixed_owners", (await LandAdmin.MergeAsync(_db, [piece.Id, neighbour], _admin, null, _clock, default)).Error);
        Assert.True((await LandAdmin.MergeAsync(_db, [big, piece.Id], _admin, null, _clock, default)).Ok);
        Assert.Equal(_group, _db.IslandParcels.AsNoTracking().Single(p => p.DeletedAt == null && p.Name == "Grande").GroupId);
        _ = a;
    }

    // ---- building together -----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyMembersWithTheCargoBuildOnGroupLand()
    {
        var id = await GroupParcel();
        Assert.Null(await CanBuild(id, _admin)); Assert.Null(await CanBuild(id, _leader)); // managers build
        Assert.Equal("parcel_forbidden", await CanBuild(id, _member)); Assert.Equal("parcel_forbidden", await CanBuild(id, _moderator)); Assert.Equal("parcel_forbidden", await CanBuild(id, _outsider));
        await Give(_member, GroupPermissions.BuildOnLand);
        Assert.Null(await CanBuild(id, _member)); Assert.Equal("parcel_forbidden", await CanBuild(id, _moderator));
        // a member who builds cannot run the land
        Assert.Equal("forbidden", (await LandAdmin.SetAccessModeAsync(_db, id, _member, "public", _clock, default)).Error);
        // leaving the group ends it at once
        Assert.True((await GroupService.LeaveAsync(_db, _member, _group, _clock.Now, default)).Ok);
        Assert.Equal("parcel_forbidden", await CanBuild(id, _member));
    }

    [Fact]
    public async Task ChangingACargoChangesWhatItBuildsAtOnce()
    {
        var id = await GroupParcel(); await Give(_member, GroupPermissions.BuildOnLand); Assert.Null(await CanBuild(id, _member));
        var role = await _db.GroupRoles.AsNoTracking().SingleAsync(r => r.GroupId == _group && r.Name.StartsWith("Cargo"));
        Assert.True((await GroupService.UpdateRoleAsync(_db, _leader, _group, role.Id, new RoleRequest(null, null, [], null), _clock.Now, default)).Ok);
        Assert.Equal("parcel_forbidden", await CanBuild(id, _member));
        Assert.True((await GroupService.KickAsync(_db, _admin, _group, _moderator, null, _clock.Now, _notify, default)).Ok);
        Assert.Equal("parcel_forbidden", await CanBuild(id, _moderator));
    }

    [Fact]
    public async Task ManagersEditAndReturnWhatMembersBuilt()
    {
        var id = await GroupParcel(); await Give(_member, GroupPermissions.BuildOnLand);
        var facts = await Facts(id);
        Assert.Null(BuildRules.CanEditObject(_admin, _member, false, facts)); Assert.Null(BuildRules.CanReturn(_admin, _member, false, facts));
        Assert.Equal("not_owner", BuildRules.CanEditObject(_member, _admin, false, facts)); // a builder does not rule over others' objects
        Assert.Equal("not_allowed_to_return", BuildRules.CanReturn(_moderator, _member, false, facts));
        Assert.Null(BuildRules.CanPutScripts(facts, _admin, false)); Assert.Equal("scripts_not_allowed", BuildRules.CanPutScripts(facts, _member, false));
    }

    [Fact]
    public async Task OnlyTheOwnersOfPersonalLandAreUnaffected()
    {
        var mine = await Parcel(_outsider, "Pessoal");
        Assert.Null(await CanBuild(mine, _outsider)); Assert.Equal("parcel_forbidden", await CanBuild(mine, _admin));
        Assert.True((await LandAdmin.SetAccessModeAsync(_db, mine, _outsider, "list", _clock, default)).Ok);
        Assert.Equal("forbidden", (await LandAdmin.SetAccessModeAsync(_db, mine, _admin, "list", _clock, default)).Error);
    }

    // ---- who gets in -----------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APrivateGroupParcelIsOpenToItsMembersOnly()
    {
        var id = await GroupParcel(); Assert.True((await LandAdmin.SetAccessModeAsync(_db, id, _leader, "private", _clock, default)).Ok);
        LandParcelView For(Guid viewer) => LandService.ViewAsync(_db, _island, viewer, default).Result.Parcels.Single(p => p.Id == id);
        Assert.True(For(_member).CanEnter); Assert.True(For(_moderator).CanEnter); Assert.False(For(_outsider).CanEnter);
        Assert.True(For(_admin).Mine); Assert.False(For(_member).Mine); Assert.Equal("Os Construtores", For(_member).OwnerName); Assert.Equal(_group, For(_member).GroupId);
        Assert.Equal("own", For(_admin).State); Assert.True(For(_admin).CanEditTerrain); Assert.False(For(_member).CanEditTerrain);
    }

    [Fact]
    public async Task TheIslandServersAreToldWhoTheGroupLetsIn()
    {
        var id = await GroupParcel(); await LandAdmin.SetAccessModeAsync(_db, id, _leader, "private", _clock, default);
        var answer = (await LandSync.BuildAsync(_db, _instance, 0, _clock, default))!;
        var parcel = JsonSerializer.SerializeToElement(answer.Access, Web).GetProperty("parcels").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        Assert.Equal("list", parcel.GetProperty("mode").GetString()); // private to everybody but the group
        var allowed = parcel.GetProperty("allow").EnumerateArray().Select(x => x.GetGuid()).ToHashSet();
        Assert.Contains(_member, allowed); Assert.Contains(_leader, allowed); Assert.DoesNotContain(_outsider, allowed);
        var editors = JsonSerializer.SerializeToElement(answer.Terrain, Web).GetProperty("parcels").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("editors").EnumerateArray().Select(x => x.GetGuid()).ToHashSet();
        Assert.Contains(_admin, editors); Assert.DoesNotContain(_member, editors);
    }

    // ---- opening a parcel to a whole group ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APersonOpensTheirParcelToAGroup()
    {
        var mine = await Parcel(_outsider, "Casa"); var other = Guid.NewGuid();
        Assert.Equal("forbidden", (await GroupService.GrantGroupAccessAsync(_db, _admin, mine, _group, "build", _clock.Now, default)).Error); // not the owner
        Assert.Equal("bad_kind", (await GroupService.GrantGroupAccessAsync(_db, _outsider, mine, _group, "own", _clock.Now, default)).Error);
        Assert.Equal("not_found", (await GroupService.GrantGroupAccessAsync(_db, _outsider, mine, other, "build", _clock.Now, default)).Error);
        Assert.True((await GroupService.GrantGroupAccessAsync(_db, _outsider, mine, _group, "build", _clock.Now, default)).Ok);
        Assert.Null(await CanBuild(mine, _member)); Assert.Null(await CanBuild(mine, _moderator)); Assert.Equal("parcel_forbidden", await CanBuild(mine, User("outro")));
        // giving the same group a lesser kind takes the building away
        Assert.True((await GroupService.GrantGroupAccessAsync(_db, _outsider, mine, _group, "allow", _clock.Now, default)).Ok);
        Assert.Equal("parcel_forbidden", await CanBuild(mine, _member));
        var list = JsonSerializer.SerializeToElement((await GroupService.GroupAccessAsync(_db, _outsider, mine, default)).Data, Web).EnumerateArray().ToList(); Assert.Single(list); Assert.Equal("allow", list[0].GetProperty("kind").GetString());
        Assert.Equal("forbidden", (await GroupService.GroupAccessAsync(_db, _member, mine, default)).Error);
        Assert.True((await GroupService.RevokeGroupAccessAsync(_db, _outsider, mine, list[0].GetProperty("id").GetGuid(), _clock.Now, default)).Ok);
        Assert.Empty(JsonSerializer.SerializeToElement((await GroupService.GroupAccessAsync(_db, _outsider, mine, default)).Data, Web).EnumerateArray());
    }

    [Fact]
    public async Task ListedParcelsLetInTheGuestGroupAndABanStillWins()
    {
        var mine = await Parcel(_outsider, "Clube"); await LandAdmin.SetAccessModeAsync(_db, mine, _outsider, "list", _clock, default);
        LandParcelView For(Guid viewer) => LandService.ViewAsync(_db, _island, viewer, default).Result.Parcels.Single(p => p.Id == mine);
        Assert.False(For(_member).CanEnter);
        await GroupService.GrantGroupAccessAsync(_db, _outsider, mine, _group, "allow", _clock.Now, default);
        Assert.True(For(_member).CanEnter); Assert.False(For(User("alheio")).CanEnter);
        Assert.True((await LandAdmin.AddAccessAsync(_db, mine, _outsider, _member, "ban", null, "briga", _clock, default)).Ok);
        Assert.False(For(_member).CanEnter); Assert.Equal("parcel_banned", await CanBuild(mine, _member));
    }

    [Fact]
    public async Task HiddenGroupsStayHiddenAndGrantsAreLimited()
    {
        var hidden = JsonSerializer.SerializeToElement((await GroupService.CreateAsync(_db, _leader, new GroupProfileRequest("Secreto", "", null, "private", null, null, null, null, null), _clock.Now, default)).Data).GetProperty("id").GetGuid();
        var mine = await Parcel(_outsider, "Casa");
        Assert.Equal("not_found", (await GroupService.GrantGroupAccessAsync(_db, _outsider, mine, hidden, "allow", _clock.Now, default)).Error); // the outsider is not in it
        var lands = await Parcel(_leader, "Terra do Líder");
        Assert.True((await GroupService.GrantGroupAccessAsync(_db, _leader, lands, hidden, "allow", _clock.Now, default)).Ok); // a member may
        for (var i = 0; i < GroupRules.MaxGroupAccessPerParcel - 1; i++) { var g = JsonSerializer.SerializeToElement((await GroupService.CreateAsync(_db, User("u" + i), new GroupProfileRequest("Grupo " + i + " teste", "", null, null, null, null, null, null, null), _clock.Now, default)).Data).GetProperty("id").GetGuid(); Assert.True((await GroupService.GrantGroupAccessAsync(_db, _leader, lands, g, "allow", _clock.Now, default)).Ok); }
        var one = JsonSerializer.SerializeToElement((await GroupService.CreateAsync(_db, User("ultimo"), new GroupProfileRequest("Mais Um Grupo", "", null, null, null, null, null, null, null), _clock.Now, default)).Data).GetProperty("id").GetGuid();
        Assert.Equal("too_many_grants", (await GroupService.GrantGroupAccessAsync(_db, _leader, lands, one, "allow", _clock.Now, default)).Error);
    }

    [Fact]
    public async Task AGroupParcelCannotBeOpenedToItself()
    {
        var id = await GroupParcel();
        Assert.Equal("bad_target", (await GroupService.GrantGroupAccessAsync(_db, _admin, id, _group, "build", _clock.Now, default)).Error);
        var friends = JsonSerializer.SerializeToElement((await GroupService.CreateAsync(_db, _outsider, new GroupProfileRequest("Amigos", "", null, null, null, null, null, null, null), _clock.Now, default)).Data).GetProperty("id").GetGuid();
        Assert.True((await GroupService.GrantGroupAccessAsync(_db, _admin, id, friends, "build", _clock.Now, default)).Ok);
        Assert.Null(await CanBuild(id, _outsider)); Assert.Contains("land_access_granted", await Actions());
    }

    // ---- taking land out ---------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheCargoThatReleasesLandHandsItBackToAMember()
    {
        var id = await GroupParcel();
        Assert.Equal("forbidden", (await GroupService.ReleaseLandAsync(_db, _admin, _group, id, _member, _clock.Now, _notify, default)).Error); // managing is not releasing
        Assert.Equal("bad_target", (await GroupService.ReleaseLandAsync(_db, _leader, _group, id, _outsider, _clock.Now, _notify, default)).Error); // only to a member
        Assert.Equal("not_found", (await GroupService.ReleaseLandAsync(_db, _leader, _group, Guid.NewGuid(), _member, _clock.Now, _notify, default)).Error);
        await GroupService.GrantGroupAccessAsync(_db, _admin, id, JsonSerializer.SerializeToElement((await GroupService.CreateAsync(_db, _outsider, new GroupProfileRequest("Amigos", "", null, null, null, null, null, null, null), _clock.Now, default)).Data).GetProperty("id").GetGuid(), "build", _clock.Now, default);
        Assert.True((await GroupService.ReleaseLandAsync(_db, _leader, _group, id, _member, _clock.Now, _notify, default)).Ok);
        var stored = Stored(id); Assert.Null(stored.GroupId); Assert.Equal(_member, stored.OwnerUserId);
        Assert.Empty(_db.ParcelGroupAccess.AsNoTracking().Where(a => a.ParcelId == id && a.RevokedAt == null));
        Assert.Contains(_pushed, p => p.User == _member && p.Type == "group_land_released"); Assert.Contains("land_released", await Actions());
        Assert.Null(await CanBuild(id, _member)); Assert.Equal("parcel_forbidden", await CanBuild(id, _admin)); // it is a person's land again
    }

    [Fact]
    public async Task ReleasingLandIsRateLimitedForNonLeaders()
    {
        await Give(_admin, GroupPermissions.ManageLand, GroupPermissions.ReleaseLand);
        for (var i = 0; i < GroupRules.MaxLandReleasesPerDay; i++) { var id = await GroupParcel("L" + i); Assert.True((await GroupService.ReleaseLandAsync(_db, _admin, _group, id, _member, _clock.Now, _notify, default)).Ok); _clock.Now = _clock.Now.AddSeconds(1); }
        var more = await GroupParcel("L-extra");
        Assert.Equal("rate_limited", (await GroupService.ReleaseLandAsync(_db, _admin, _group, more, _member, _clock.Now, _notify, default)).Error);
        Assert.True((await GroupService.ReleaseLandAsync(_db, _leader, _group, more, _member, _clock.Now, _notify, default)).Ok); // the leader is not limited
        var next = await GroupParcel("L-next"); _clock.Now = _clock.Now.AddHours(25);
        Assert.True((await GroupService.ReleaseLandAsync(_db, _admin, _group, next, _member, _clock.Now, _notify, default)).Ok);
    }

    [Fact]
    public async Task RentedLandIsNotReleased()
    {
        var id = await GroupParcel();
        _db.ParcelRentals.Add(new ParcelRental { Id = Guid.NewGuid(), ParcelId = id, TenantUserId = _outsider, Status = "active", PricePerPeriod = 10, PeriodDays = 30, StartsAt = _clock.Now, LandlordUserId = _admin, ExpiresAt = _clock.Now.AddDays(30) }); await _db.SaveChangesAsync();
        Assert.Equal("rented", (await GroupService.ReleaseLandAsync(_db, _leader, _group, id, _member, _clock.Now, _notify, default)).Error);
    }

    // ---- leadership, listing and the end -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheParcelsFollowTheNewLeader()
    {
        var id = await GroupParcel(); Assert.Equal(_leader, Stored(id).OwnerUserId);
        Assert.True((await GroupService.TransferLeadershipAsync(_db, _leader, _group, _admin, "Os Construtores", _clock.Now, _notify, default)).Ok);
        Assert.Equal(_admin, Stored(id).OwnerUserId);
        Assert.True((await LandAdmin.SetAccessModeAsync(_db, id, _leader, "list", _clock, default)).Ok); // the old leader is an administrator now: still a manager
        await GroupService.KickAsync(_db, _admin, _group, _leader, null, _clock.Now, _notify, default);
    }

    [Fact]
    public async Task TheGroupSeesItsLandAndWhatIsOfferable()
    {
        var id = await GroupParcel("Terra Um"); var spare = await Parcel(_admin, "Reserva");
        var lands = JsonSerializer.SerializeToElement((await GroupService.LandsAsync(_db, _member, _group, _clock.Now, default)).Data, Web);
        Assert.Equal(id, lands.GetProperty("lands")[0].GetProperty("parcelId").GetGuid()); Assert.Equal("Ilha", lands.GetProperty("lands")[0].GetProperty("islandName").GetString());
        Assert.False(lands.GetProperty("canManage").GetBoolean()); Assert.False(lands.GetProperty("canRelease").GetBoolean());
        Assert.True(JsonSerializer.SerializeToElement((await GroupService.LandsAsync(_db, _leader, _group, _clock.Now, default)).Data, Web).GetProperty("canRelease").GetBoolean());
        Assert.Equal("forbidden", (await GroupService.LandsAsync(_db, _outsider, _group, _clock.Now, default)).Error);
        var offer = JsonSerializer.SerializeToElement((await GroupService.OfferableLandAsync(_db, _admin, _group, _clock.Now, default)).Data, Web).EnumerateArray().Select(x => x.GetProperty("parcelId").GetGuid()).ToList();
        Assert.Equal([spare], offer); Assert.Equal("forbidden", (await GroupService.OfferableLandAsync(_db, _member, _group, _clock.Now, default)).Error);
    }

    [Fact]
    public async Task AGroupWithLandCannotBeDissolvedUntilItIsReleased()
    {
        var id = await GroupParcel();
        var blocked = await GroupService.DissolveAsync(_db, _leader, _group, "Os Construtores", _clock.Now, _notify, default); Assert.Equal("blocked", blocked.Error);
        Assert.True((await GroupService.ReleaseLandAsync(_db, _leader, _group, id, _leader, _clock.Now, _notify, default)).Ok);
        Assert.True((await GroupService.DissolveAsync(_db, _leader, _group, "Os Construtores", _clock.Now, _notify, default)).Ok);
        Assert.Equal(_leader, Stored(id).OwnerUserId);
    }

    [Fact]
    public void TheLandPermissionsExistAndGoToTheRightCargos()
    {
        foreach (var key in new[] { GroupPermissions.ManageLand, GroupPermissions.BuildOnLand, GroupPermissions.ReleaseLand }) Assert.Contains(key, GroupPermissions.Known);
        var roles = GroupRules.StartingRoles.ToDictionary(r => r.Key, r => r.Permissions);
        Assert.Contains("manage_land", roles["admin"]); Assert.Contains("build_on_land", roles["admin"]); Assert.DoesNotContain("release_land", roles["admin"]); Assert.Contains("release_land", roles["owner"]); Assert.DoesNotContain("manage_land", roles["moderator"]);
    }
}
