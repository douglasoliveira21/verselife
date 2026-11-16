using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Map;
using LifeVerse.Shared.Platform;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Landmarks and their folders, the panels of the map (favorites, recent, friends, groups), the parcels drawn over an island, where friends are, and whether a chosen spot can be gone to.</summary>
public sealed class MapPlacesTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me, _owner, _friend, _stranger;
    private readonly Island _island;

    public MapPlacesTests()
    {
        (_me, _owner, _friend, _stranger) = (User("eu"), User("dona"), User("amiga"), User("estranho"));
        _island = Island("Ilha Central", _owner); Friends(_me, _friend);
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now, PresenceStatus = "online" });
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = id, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id;
    }

    private Island Island(string name, Guid? owner = null, bool isPublic = true)
    {
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = owner ?? _owner, Name = name, Slug = Guid.NewGuid().ToString("N")[..12], IsPublic = isPublic, ShowInSearch = true, ShowOnMap = true, MaxPlayers = 3, Size = 256, CreatedAt = _now, UpdatedAt = _now };
        _db.Islands.Add(island); _db.SaveChanges(); return island;
    }

    private void Friends(Guid a, Guid b) { _db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserId = a, FriendUserId = b, Status = "accepted", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); }
    private LandmarkRequest Mark(string name = "Minha casa", Guid? island = null, float x = 10, float z = -20, Guid? folder = null, bool pub = false) => new(name, "Descrição", island ?? _island.Id, x, 1, z, folder, pub);
    private async Task<Guid> Make(LandmarkRequest? r = null) { var o = await MapPlaces.CreateAsync(_db, _me, r ?? Mark(), _now, default); Assert.True(o.Ok, o.Error); return o.Id!.Value; }
    private async Task<JsonElement> Panels(Guid? viewer = null) => JsonSerializer.SerializeToElement(await MapPlaces.PanelsAsync(_db, viewer ?? _me, _now, default));

    // ---- landmarks -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ALandmarkIsMadeOnlyWhereThePersonMayEnterAndWithinTheIsland()
    {
        await Make();
        Assert.Equal("bad_name", (await MapPlaces.CreateAsync(_db, _me, Mark("  "), _now, default)).Error); Assert.Equal("bad_name", (await MapPlaces.CreateAsync(_db, _me, Mark(new string('a', 61)), _now, default)).Error);
        Assert.Equal("bad_description", (await MapPlaces.CreateAsync(_db, _me, Mark() with { Description = new string('d', 301) }, _now, default)).Error);
        Assert.Equal("bad_position", (await MapPlaces.CreateAsync(_db, _me, Mark(x: 500), _now, default)).Error); Assert.Equal("bad_position", (await MapPlaces.CreateAsync(_db, _me, Mark(z: float.NaN), _now, default)).Error);
        Assert.Equal("island_not_found", (await MapPlaces.CreateAsync(_db, _me, Mark(island: Island("Reservada", _owner, isPublic: false).Id), _now, default)).Error);
        Assert.Equal("island_not_found", (await MapPlaces.CreateAsync(_db, _me, Mark(island: Guid.NewGuid()), _now, default)).Error);
        _island.AccessClosed = true; await _db.SaveChangesAsync(); Assert.Equal("no_access", (await MapPlaces.CreateAsync(_db, _me, Mark("Fechada"), _now, default)).Error); // visible, but not enterable now
        _db.IslandBans.Add(new IslandBan { Id = Guid.NewGuid(), IslandId = Island("Banida", _owner).Id, UserId = _me, BannedByUserId = _owner, Reason = "x", CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal("no_access", (await MapPlaces.CreateAsync(_db, _me, Mark(island: (await _db.Islands.SingleAsync(i => i.Name == "Banida")).Id), _now, default)).Error);
    }

    [Fact]
    public async Task LandmarksAreLimitedAndGoInFoldersOwnedByThePerson()
    {
        var folder = (await MapPlaces.CreateFolderAsync(_db, _me, "Casas", _now, default)).Id!.Value; await Make(Mark(folder: folder));
        var others = (await MapPlaces.CreateFolderAsync(_db, _stranger, "Dele", _now, default)).Id!.Value; Assert.Equal("folder_not_found", (await MapPlaces.CreateAsync(_db, _me, Mark(folder: others), _now, default)).Error);
        for (var i = 0; i < MapPlaces.MaxLandmarks - 1; i++) _db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = _me, IslandId = _island.Id, Name = "m" + i, CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal("too_many_landmarks", (await MapPlaces.CreateAsync(_db, _me, Mark("A mais"), _now, default)).Error);
    }

    [Fact]
    public async Task OnlyTheOwnerChangesOrDeletesALandmarkAndItsPlaceNeverMoves()
    {
        var id = await Make(); var folder = (await MapPlaces.CreateFolderAsync(_db, _me, "Pasta", _now, default)).Id!.Value;
        Assert.Equal("not_found", (await MapPlaces.ChangeAsync(_db, _stranger, id, new LandmarkChange("Roubado", null, null, null, null), _now, default)).Error);
        Assert.Equal("bad_name", (await MapPlaces.ChangeAsync(_db, _me, id, new LandmarkChange("", null, null, null, null), _now, default)).Error);
        Assert.True((await MapPlaces.ChangeAsync(_db, _me, id, new LandmarkChange("Novo nome", "Nova descrição", folder, null, true), _now, default)).Ok);
        var l = await _db.Landmarks.SingleAsync(x => x.Id == id); Assert.Equal(("Novo nome", "Nova descrição", folder, true), (l.Name, l.Description, l.FolderId, l.IsPublic)); Assert.Equal((10f, -20f), (l.PositionX, l.PositionZ));
        Assert.True((await MapPlaces.ChangeAsync(_db, _me, id, new LandmarkChange(null, null, null, true, null), _now, default)).Ok); Assert.Null((await _db.Landmarks.SingleAsync(x => x.Id == id)).FolderId);
        Assert.Equal("not_found", (await MapPlaces.DeleteAsync(_db, _stranger, id, _now, default)).Error); Assert.True((await MapPlaces.DeleteAsync(_db, _me, id, _now, default)).Ok);
        Assert.Empty((await Panels()).GetProperty("landmarks").EnumerateArray()); Assert.Equal("not_found", (await MapPlaces.DeleteAsync(_db, _me, id, _now, default)).Error);
    }

    [Fact]
    public async Task FoldersHaveLimitsUniqueNamesAndLeaveTheirLandmarksLoose()
    {
        var a = (await MapPlaces.CreateFolderAsync(_db, _me, "Casas", _now, default)).Id!.Value; Assert.Equal("name_taken", (await MapPlaces.CreateFolderAsync(_db, _me, "casas", _now, default)).Error); Assert.Equal("bad_name", (await MapPlaces.CreateFolderAsync(_db, _me, " ", _now, default)).Error);
        Assert.True((await MapPlaces.CreateFolderAsync(_db, _stranger, "Casas", _now, default)).Ok); // another person may use it
        Assert.True((await MapPlaces.RenameFolderAsync(_db, _me, a, "CASAS", default)).Ok); // changing only the case of its own name is fine
        var b = (await MapPlaces.CreateFolderAsync(_db, _me, "Lojas", _now, default)).Id!.Value; Assert.Equal("name_taken", (await MapPlaces.RenameFolderAsync(_db, _me, b, "casas", default)).Error); Assert.True((await MapPlaces.RenameFolderAsync(_db, _me, b, "Comércio", default)).Ok);
        Assert.Equal("not_found", (await MapPlaces.RenameFolderAsync(_db, _stranger, b, "X", default)).Error);
        for (var i = 0; i < MapPlaces.MaxFolders - 2; i++) Assert.True((await MapPlaces.CreateFolderAsync(_db, _me, "p" + i, _now, default)).Ok); Assert.Equal("too_many_folders", (await MapPlaces.CreateFolderAsync(_db, _me, "demais", _now, default)).Error);
        var id = await Make(Mark(folder: a)); Assert.True((await MapPlaces.DeleteFolderAsync(_db, _me, a, _now, default)).Ok); Assert.Null((await _db.Landmarks.SingleAsync(x => x.Id == id)).FolderId); Assert.Equal("not_found", (await MapPlaces.DeleteFolderAsync(_db, _stranger, b, _now, default)).Error);
    }

    [Fact]
    public async Task ALandmarkForALinkIsSeenByOthersOnlyWhenPublicAndNotBlocked()
    {
        var id = await Make();
        Assert.NotNull(await MapPlaces.ViewAsync(_db, _me, id, _now, default)); Assert.Null(await MapPlaces.ViewAsync(_db, _stranger, id, _now, default)); // private
        await MapPlaces.ChangeAsync(_db, _me, id, new LandmarkChange(null, null, null, null, true), _now, default);
        var shared = JsonSerializer.SerializeToElement(await MapPlaces.ViewAsync(_db, _stranger, id, _now, default)); Assert.Equal("Minha casa", shared.GetProperty("name").GetString()); Assert.Equal("eu", shared.GetProperty("ownerName").GetString()); Assert.False(shared.GetProperty("isMine").GetBoolean());
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _stranger, BlockedUserId = _me, CreatedAt = _now }); await _db.SaveChangesAsync(); Assert.Null(await MapPlaces.ViewAsync(_db, _stranger, id, _now, default));
        var other = Island("Outra", _owner); (await _db.Landmarks.SingleAsync(x => x.Id == id)).IslandId = other.Id; other.IsPublic = false; await _db.SaveChangesAsync(); Assert.Null(await MapPlaces.ViewAsync(_db, _friend, id, _now, default)); // the island is not for them
    }

    // ---- the panels ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PanelsListFavoritesRecentFriendsAndGroups()
    {
        var mine = Island("Minha ilha", _me); var friendIsland = Island("Ilha da amiga", _friend); var strangerIsland = Island("Ilha do estranho", _stranger); var secret = Island("Secreta da amiga", _friend, isPublic: false);
        _db.IslandFavorites.Add(new IslandFavorite { Id = Guid.NewGuid(), UserId = _me, IslandId = _island.Id, CreatedAt = _now }); _db.IslandFavorites.Add(new IslandFavorite { Id = Guid.NewGuid(), UserId = _me, IslandId = secret.Id, CreatedAt = _now });
        _db.PlayerLocations.Add(new PlayerLocation { UserId = _me, IslandId = friendIsland.Id, X = 1, Z = 2, UpdatedAt = _now.AddMinutes(-1) });
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Clube", NameKey = "clube", Tag = "CLB", HeadquartersIslandId = _island.Id, CreatedAt = _now, UpdatedAt = _now }; _db.SocialGroups.Add(group); _db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = _me, Role = "member", JoinedAt = _now });
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = friendIsland.Id, OwnerUserId = _owner, GroupId = group.Id, Name = "Sede", BoundsJson = "{}" }); await _db.SaveChangesAsync();
        var p = await Panels();
        Assert.Equal(["Ilha Central"], p.GetProperty("favorites").EnumerateArray().Select(f => f.GetProperty("name").GetString())); // not the one that is not for them
        Assert.Equal(["Ilha da amiga"], p.GetProperty("recent").EnumerateArray().Select(f => f.GetProperty("name").GetString())); // newest first, without the hidden one
        Assert.Equal(["Ilha da amiga"], p.GetProperty("friends").EnumerateArray().Select(f => f.GetProperty("name").GetString())); Assert.Equal("amiga", p.GetProperty("friends")[0].GetProperty("ownerName").GetString()); // public islands of friends only
        var g = p.GetProperty("groups").EnumerateArray().Single(); Assert.Equal("Clube", g.GetProperty("name").GetString());
        Assert.Equal(["headquarters", "land"], g.GetProperty("places").EnumerateArray().Select(x => x.GetProperty("kind").GetString()).OrderBy(k => k)); _ = mine; _ = strangerIsland;
        Assert.Empty((await Panels(_stranger)).GetProperty("groups").EnumerateArray()); Assert.Empty((await Panels(_stranger)).GetProperty("friends").EnumerateArray());
    }

    // ---- the parcels ----------------------------------------------------------------------------------------------------------------------

    private IslandParcel Parcel(string name, string mode = "public", long? sale = null, long? rent = null, Guid? owner = null, Guid? group = null, string category = "residencial", string teleport = "free", double minX = 0, double minZ = 0, double maxX = 20, double maxZ = 20, double area = 400)
    {
        var p = new IslandParcel { Id = Guid.NewGuid(), IslandId = _island.Id, OwnerUserId = owner ?? _owner, GroupId = group, Name = name, BoundsJson = JsonSerializer.Serialize(new { minX, minZ, maxX, maxZ }), Area = area, AccessMode = mode, SalePrice = sale, RentPrice = rent, Category = category, TeleportMode = teleport, SpawnX = 3, SpawnZ = 4, CreatedAt = _now, UpdatedAt = _now };
        _db.IslandParcels.Add(p); _db.SaveChanges(); return p;
    }

    private async Task<Dictionary<string, JsonElement>> Parcels() => JsonSerializer.SerializeToElement(await MapOverlays.ParcelsAsync(_db, _me, _island.Id, _now, default)).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString() ?? i.GetProperty("name").GetString()!, i => i);

    [Fact]
    public async Task ParcelsShowAStateThatMeansSomethingToTheViewerAndHideWhatIsRestricted()
    {
        var mine = Parcel("Minha", owner: _me); mine.Code = "A"; var forSale = Parcel("Venda", sale: 500); forSale.Code = "B"; var forRent = Parcel("Aluguel", rent: 20); forRent.Code = "C"; var open = Parcel("Aberta"); open.Code = "D";
        var secret = Parcel("Segredo da dona", mode: "private"); secret.Code = "E"; secret.Description = "Casa particular"; var common = Parcel("Praça", category: "comum"); common.Code = "F";
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Clube", NameKey = "clube", CreatedAt = _now, UpdatedAt = _now }; _db.SocialGroups.Add(group); _db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = _me, Role = "member", JoinedAt = _now });
        var ofGroup = Parcel("Do grupo", group: group.Id); ofGroup.Code = "G"; await _db.SaveChangesAsync();
        var items = await Parcels();
        Assert.Equal(("mine", "sale", "rent", "public", "restricted", "common", "group"), (items["A"].GetProperty("state").GetString(), items["B"].GetProperty("state").GetString(), items["C"].GetProperty("state").GetString(), items["D"].GetProperty("state").GetString(), items["E"].GetProperty("state").GetString(), items["F"].GetProperty("state").GetString(), items["G"].GetProperty("state").GetString()));
        Assert.Equal("Terreno restrito", items["E"].GetProperty("name").GetString()); Assert.Equal(string.Empty, items["E"].GetProperty("description").GetString()); Assert.Equal(JsonValueKind.Null, items["E"].GetProperty("owner").ValueKind); Assert.False(items["E"].GetProperty("teleport").GetBoolean());
        Assert.Equal(500, items["B"].GetProperty("salePrice").GetInt64()); Assert.Equal("dona", items["B"].GetProperty("owner").GetString()); Assert.Equal(20, items["C"].GetProperty("rentPrice").GetInt64()); Assert.Equal(JsonValueKind.Null, items["D"].GetProperty("owner").ValueKind); Assert.Equal("Clube", items["G"].GetProperty("owner").GetString());
        Assert.Equal(20, items["A"].GetProperty("bounds").GetProperty("maxX").GetDouble()); Assert.Equal("eu", items["A"].GetProperty("owner").GetString());
        Assert.Null(await MapOverlays.ParcelsAsync(_db, _me, Guid.NewGuid(), _now, default)); _island.IsPublic = false; await _db.SaveChangesAsync(); Assert.Null(await MapOverlays.ParcelsAsync(_db, _me, _island.Id, _now, default)); // an island that is not for them draws nothing
    }

    // ---- friends ------------------------------------------------------------------------------------------------------------------------------

    private void Online(Island island, Guid user, int secondsAgo = 5, float x = 10, float z = 20)
    {
        var report = JsonSerializer.Serialize(new ServerReport([new PlayerReport(user, "x", 0, x, 0, z)]), ServerChannelJson.Options);
        _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = island.Id, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now, LastHeartbeatAt = _now.AddSeconds(-secondsAgo), ReportJson = report }); _db.SaveChanges();
    }

    [Fact]
    public async Task FriendsAreShownOnlyWhenOnlineSharingAndOnAKnownIsland()
    {
        Online(_island, _friend); Online(Island("Outra", _owner), _stranger);
        var list = await MapOverlays.FriendsAsync(_db, _me, _now, default); var f = Assert.Single(list); Assert.Equal(("amiga", "Ilha Central", 10d, 20d), (f.Name, f.IslandName, f.X, f.Z));
        var friend = await _db.Users.SingleAsync(u => u.Id == _friend); friend.PrivacyJson = "{\"presence\":\"private\"}"; await _db.SaveChangesAsync(); Assert.Empty(await MapOverlays.FriendsAsync(_db, _me, _now, default)); // they chose not to be seen
        friend.PrivacyJson = "{}"; friend.PresenceStatus = "offline"; await _db.SaveChangesAsync(); Assert.Empty(await MapOverlays.FriendsAsync(_db, _me, _now, default));
        friend.PresenceStatus = "online"; _island.IsPublic = false; await _db.SaveChangesAsync(); Assert.Empty(await MapOverlays.FriendsAsync(_db, _me, _now, default)); // on an island that is not for me
        _island.IsPublic = true; _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _me, BlockedUserId = _friend, CreatedAt = _now }); await _db.SaveChangesAsync(); Assert.Empty(await MapOverlays.FriendsAsync(_db, _me, _now, default));
        Assert.Empty(await MapOverlays.FriendsAsync(_db, _stranger, _now, default)); // a stranger sees nobody
    }

    [Fact]
    public async Task ARunningServerThatStoppedReportingShowsNobody()
    {
        Online(_island, _friend, secondsAgo: 120); Assert.Empty(await MapOverlays.FriendsAsync(_db, _me, _now, default));
    }

    // ---- going to a spot -----------------------------------------------------------------------------------------------------------------------

    private async Task<ArrivalAnswer> Go(float x, float z, Guid? who = null, Guid? island = null) => await MapArrival.ResolveAsync(_db, who ?? _me, new ArrivalRequest(island ?? _island.Id, x, z), _now, default);

    [Fact]
    public async Task AnOpenSpotIsAllowedAsAskedAndClampedToTheIsland()
    {
        var a = await Go(30, -40); Assert.Equal((true, 30f, -40f), (a.Allowed, a.X, a.Z)); Assert.Null(a.ParcelId);
        var far = await Go(9999, -9999); Assert.True(far.Allowed); Assert.Equal((128f, -128f), (far.X, far.Z));
        Assert.Equal("bad_position", (await Go(float.NaN, 0)).Reason); Assert.Equal("island_not_found", (await Go(0, 0, island: Guid.NewGuid())).Reason);
    }

    [Fact]
    public async Task TheIslandDecidesFirst()
    {
        _island.AccessClosed = true; await _db.SaveChangesAsync(); Assert.Equal("closed", (await Go(1, 1)).Reason); Assert.True((await Go(1, 1, _owner)).Allowed);
        _island.AccessClosed = false; _db.IslandBans.Add(new IslandBan { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _me, BannedByUserId = _owner, Reason = "x", CreatedAt = _now }); await _db.SaveChangesAsync(); Assert.Equal("banned", (await Go(1, 1)).Reason);
        _db.IslandBans.RemoveRange(_db.IslandBans); _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = _island.Id, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now, LastHeartbeatAt = _now, PlayerCount = 3 }); await _db.SaveChangesAsync();
        Assert.Equal("full", (await Go(1, 1)).Reason); Assert.True((await Go(1, 1, _owner)).Allowed);
    }

    [Fact]
    public async Task LandRulesApplyAtTheChosenSpot()
    {
        var open = Parcel("Aberta", minX: 0, minZ: 0, maxX: 20, maxZ: 20); var a = await Go(5, 5); Assert.True(a.Allowed); Assert.Equal(open.Id, a.ParcelId); Assert.Equal("Aberta", a.ParcelName);
        var secret = Parcel("Privada", mode: "private", minX: 30, minZ: 30, maxX: 50, maxZ: 50); var denied = await Go(40, 40); Assert.False(denied.Allowed); Assert.Equal("parcel_restricted", denied.Reason); Assert.Null(denied.ParcelName); // it does not even say what it is
        Assert.True((await Go(40, 40, _owner)).Allowed);
        _db.ParcelAccess.Add(new ParcelAccess { Id = Guid.NewGuid(), ParcelId = secret.Id, UserId = _me, Kind = "allow", GrantedByUserId = _owner, CreatedAt = _now }); await _db.SaveChangesAsync(); Assert.True((await Go(40, 40)).Allowed);
        _db.ParcelAccess.Add(new ParcelAccess { Id = Guid.NewGuid(), ParcelId = open.Id, UserId = _me, Kind = "ban", GrantedByUserId = _owner, CreatedAt = _now }); await _db.SaveChangesAsync(); Assert.Equal("parcel_banned", (await Go(5, 5)).Reason);
        var nope = Parcel("Sem teleporte", teleport: "disabled", minX: 60, minZ: 60, maxX: 70, maxZ: 70); Assert.Equal("parcel_no_teleport", (await Go(65, 65)).Reason); Assert.True((await Go(65, 65, _owner)).Allowed);
        var forced = Parcel("Com pouso", teleport: "forced", minX: 80, minZ: -60, maxX: 100, maxZ: -40); var f = await Go(90, -50); Assert.True(f.Allowed); Assert.True(f.Forced); Assert.Equal((3f, 4f), (f.X, f.Z)); _ = nope; _ = forced;
        var inner = Parcel("Interna", minX: 5, minZ: 5, maxX: 8, maxZ: 8, area: 9); Assert.Equal(inner.Id, (await Go(6, 6, _owner)).ParcelId); // the smallest parcel around the point wins
    }
}
