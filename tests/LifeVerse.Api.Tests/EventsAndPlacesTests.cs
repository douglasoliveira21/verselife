using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Events (announcing, detail, signing up, the calendar, calling off, who may do what) and places (what a viewer can do about an island), as the search window uses them.</summary>
public sealed class EventsAndPlacesTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner, _guest, _fan, _other;
    private readonly Island _island;

    public EventsAndPlacesTests()
    {
        (_owner, _guest, _fan, _other) = (User("dona"), User("convidada"), User("fa"), User("outra"));
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha Festa", Slug = "festa", IsPublic = true, ShowInSearch = true, Category = "social", Size = 256, MaxPlayers = 2, CreatedAt = _now, UpdatedAt = _now };
        _db.Islands.Add(_island); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = id, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id;
    }

    private EventCreate Make(string name = "Show na praia", int capacity = 0, long price = 0, int startsInHours = 3, string? category = "show", string? rating = null, string? image = null) =>
        new(name, "Música ao vivo.", _island.Id, _now.AddHours(startsInHours), _now.AddHours(startsInHours + 2), capacity, true, category, image, price, rating);

    private async Task<Guid> Announce(Guid who, EventCreate? request = null) { var outcome = await EventDirectory.CreateAsync(_db, who, request ?? Make(), _now, default); Assert.True(outcome.Ok, outcome.Error); return outcome.Id!.Value; }
    private async Task<JsonElement> Detail(Guid viewer, Guid id) => JsonSerializer.SerializeToElement(await EventDirectory.DetailAsync(_db, viewer, id, _now, default));
    private async Task<SearchPage> Find(SearchRequest request, Guid? who = null) { await SearchIndexer.RunAsync(_db, _now, default); return await GlobalSearch.SearchAsync(_db, who ?? _fan, request, _now, default); }

    // ---- announcing ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOwnerAnnouncesAndGuestsOnlyWhenTheOwnerAllowsIt()
    {
        await Announce(_owner);
        Assert.Equal("not_allowed", (await EventDirectory.CreateAsync(_db, _guest, Make("Festa da convidada"), _now, default)).Error);
        Assert.True((await EventDirectory.SetGuestEventsAsync(_db, _owner, _island.Id, true, _now, default)).Ok);
        Assert.Equal("not_found", (await EventDirectory.SetGuestEventsAsync(_db, _guest, _island.Id, false, _now, default)).Error); // only the owner decides
        await Announce(_guest, Make("Festa da convidada"));
        _island.AccessClosed = true; await _db.SaveChangesAsync();
        Assert.Equal("not_allowed", (await EventDirectory.CreateAsync(_db, _fan, Make("Em ilha fechada"), _now, default)).Error);
    }

    [Theory]
    [InlineData("ab", "show", 0, 0, 3, 2, null, "bad_name")]
    [InlineData("Festa", "nada", 0, 0, 3, 2, null, "bad_category")]
    [InlineData("Festa", "show", -1, 0, 3, 2, null, "bad_price")]
    [InlineData("Festa", "show", 0, 5000, 3, 2, null, "bad_capacity")]
    [InlineData("Festa", "show", 0, 0, -3, 2, null, "bad_start")]
    [InlineData("Festa", "show", 0, 0, 9000, 2, null, "bad_start")]
    [InlineData("Festa", "show", 0, 0, 3, 0, null, "bad_length")]
    [InlineData("Festa", "show", 0, 0, 3, 30, null, "bad_length")]
    [InlineData("Festa", "show", 0, 0, 3, 2, "http://x.com/a.png", "bad_image")]
    public async Task AnAnnouncementIsValidated(string name, string category, long price, int capacity, int startsInHours, int lengthHours, string? image, string error)
    {
        var request = new EventCreate(name, "d", _island.Id, _now.AddHours(startsInHours), _now.AddHours(startsInHours + lengthHours), capacity, true, category, image, price);
        Assert.Equal(error, (await EventDirectory.CreateAsync(_db, _owner, request, _now, default)).Error);
    }

    [Fact]
    public async Task TheArrivalPointMustBeInsideTheIslandAndTheNumberOfActiveEventsIsLimited()
    {
        Assert.Equal("bad_arrival", (await EventDirectory.CreateAsync(_db, _owner, Make() with { ArrivalX = 9999, ArrivalZ = 0 }, _now, default)).Error);
        Assert.Equal("bad_arrival", (await EventDirectory.CreateAsync(_db, _owner, Make() with { ArrivalX = 10 }, _now, default)).Error); // both or none
        for (var i = 0; i < EventRules.MaxActivePerOrganizer; i++) await Announce(_owner, Make($"Evento {i}"));
        Assert.Equal("too_many_events", (await EventDirectory.CreateAsync(_db, _owner, Make("Mais um"), _now, default)).Error);
    }

    // ---- the detail ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheDetailTellsWhoWhenWhereAndWhatTheViewerCanDo()
    {
        var id = await Announce(_owner, Make("Show na praia", capacity: 2, price: 50, rating: "moderado"));
        var d = await Detail(_fan, id);
        Assert.Equal("Show na praia", d.GetProperty("name").GetString()); Assert.Equal("scheduled", d.GetProperty("status").GetString()); Assert.Equal("dona", d.GetProperty("organizer").GetProperty("username").GetString());
        Assert.Equal(50, d.GetProperty("priceCoins").GetInt64()); Assert.Equal("moderado", d.GetProperty("contentRating").GetString()); Assert.Equal("Ilha Festa", d.GetProperty("island").GetProperty("name").GetString());
        Assert.True(d.GetProperty("canJoin").GetBoolean()); Assert.False(d.GetProperty("canManage").GetBoolean()); Assert.True((await Detail(_owner, id)).GetProperty("canManage").GetBoolean());
        Assert.Equal("show", d.GetProperty("category").GetString()); Assert.Equal("Show", d.GetProperty("categoryLabel").GetString());
    }

    [Fact]
    public async Task APrivateIslandAGroupEventOrABlockedOrganizerIsNotFound()
    {
        var id = await Announce(_owner); Assert.NotNull(await EventDirectory.DetailAsync(_db, _fan, id, _now, default));
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _fan, BlockedUserId = _owner, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Null(await EventDirectory.DetailAsync(_db, _fan, id, _now, default)); Assert.NotNull(await EventDirectory.DetailAsync(_db, _other, id, _now, default));
        _island.IsPublic = false; await _db.SaveChangesAsync(); Assert.Null(await EventDirectory.DetailAsync(_db, _other, id, _now, default)); Assert.NotNull(await EventDirectory.DetailAsync(_db, _owner, id, _now, default));
        _island.IsPublic = true; var group = Guid.NewGuid(); (await _db.DiscoveryEvents.SingleAsync()).GroupId = group; await _db.SaveChangesAsync();
        Assert.Null(await EventDirectory.DetailAsync(_db, _other, id, _now, default));
    }

    // ---- signing up, the calendar, calling off ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SigningUpIsOnceFullEventsRefuseAndLeavingFreesThePlace()
    {
        var id = await Announce(_owner, Make(capacity: 1));
        Assert.True((await EventDirectory.JoinAsync(_db, _fan, id, _now, default)).Ok); Assert.True((await EventDirectory.JoinAsync(_db, _fan, id, _now, default)).Ok);
        Assert.Equal(1, await EventDirectory.GoingAsync(_db, id, default)); Assert.True((await Detail(_fan, id)).GetProperty("joined").GetBoolean());
        Assert.Equal("event_full", (await EventDirectory.JoinAsync(_db, _other, id, _now, default)).Error); Assert.False((await Detail(_other, id)).GetProperty("canJoin").GetBoolean());
        await EventDirectory.LeaveAsync(_db, _fan, id, default); Assert.True((await EventDirectory.JoinAsync(_db, _other, id, _now, default)).Ok);
    }

    [Fact]
    public async Task TheCalendarSavesWithoutTakingAPlaceAndSurvivesLeaving()
    {
        var id = await Announce(_owner, Make(capacity: 1));
        Assert.True((await EventDirectory.CalendarAsync(_db, _fan, id, true, _now, default)).Ok);
        Assert.Equal(0, await EventDirectory.GoingAsync(_db, id, default)); Assert.True((await Detail(_fan, id)).GetProperty("inCalendar").GetBoolean()); Assert.False((await Detail(_fan, id)).GetProperty("joined").GetBoolean());
        Assert.Single(await EventDirectory.MineAsync(_db, _fan, _now, default));
        Assert.True((await EventDirectory.JoinAsync(_db, _fan, id, _now, default)).Ok); Assert.Equal(1, await EventDirectory.GoingAsync(_db, id, default));
        await EventDirectory.LeaveAsync(_db, _fan, id, default); Assert.True((await Detail(_fan, id)).GetProperty("inCalendar").GetBoolean()); Assert.Equal(0, await EventDirectory.GoingAsync(_db, id, default));
        await EventDirectory.CalendarAsync(_db, _fan, id, false, _now, default); Assert.Empty(await EventDirectory.MineAsync(_db, _fan, _now, default)); Assert.Equal(0, await _db.EventAttendees.CountAsync());
    }

    [Fact]
    public async Task CallingOffIsForTheOrganizerOrTheOwnerAndRemovesTheEventEverywhere()
    {
        await EventDirectory.SetGuestEventsAsync(_db, _owner, _island.Id, true, _now, default);
        var id = await Announce(_guest, Make("Festa da convidada")); await EventDirectory.JoinAsync(_db, _fan, id, _now, default);
        Assert.Equal(1, (await Find(new SearchRequest("convidada", ["event"]))).Total);
        Assert.Equal("forbidden", (await EventDirectory.CancelAsync(_db, _other, id, _now, default)).Error);
        Assert.True((await EventDirectory.CancelAsync(_db, _owner, id, _now, default)).Ok); // the owner of the island may too
        var d = await Detail(_fan, id); Assert.Equal("cancelled", d.GetProperty("status").GetString()); Assert.False(d.GetProperty("canJoin").GetBoolean());
        Assert.Equal(0, (await Find(new SearchRequest("convidada", ["event"]))).Total); Assert.Empty(await EventDirectory.MineAsync(_db, _fan, _now, default));
        Assert.Equal("cancelled", (await EventDirectory.JoinAsync(_db, _other, id, _now, default)).Error);
    }

    [Fact]
    public async Task AnEventThatAlreadyEndedCannotBeJoinedOrSaved()
    {
        var id = await Announce(_owner); var later = _now.AddDays(2);
        Assert.Equal("ended", (await EventDirectory.JoinAsync(_db, _fan, id, later, default)).Error); Assert.Equal("ended", (await EventDirectory.CalendarAsync(_db, _fan, id, true, later, default)).Error);
        Assert.Equal("ended", JsonSerializer.SerializeToElement(await EventDirectory.DetailAsync(_db, _fan, id, later, default)).GetProperty("status").GetString());
    }

    // ---- search: filters and what is indexed ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task EventsAreFoundWithCategoryPriceAndRatingAndFilteredByTimeFreeAndRating()
    {
        await Announce(_owner, Make("Show gratis", price: 0, startsInHours: 2, category: "show")); await Announce(_owner, Make("Aula paga", price: 80, startsInHours: 30, category: "aula", rating: "adulto"));
        var all = await Find(new SearchRequest("", ["event"])); Assert.Equal(2, all.Total);
        var show = all.Items.Single(i => i.Title == "Show gratis"); Assert.Equal("show", show.Category); Assert.Equal(0, show.PriceCoins); Assert.Equal(_now.AddHours(4), show.EndsAt);
        Assert.Equal(["Show gratis"], (await Find(new SearchRequest("", ["event"], Free: true))).Items.Select(i => i.Title)); Assert.Equal(["Aula paga"], (await Find(new SearchRequest("", ["event"], Free: false))).Items.Select(i => i.Title));
        Assert.Equal(["Show gratis"], (await Find(new SearchRequest("", ["event"], Ratings: ["livre", "moderado"]))).Items.Select(i => i.Title));
        Assert.Equal(["Show gratis"], (await Find(new SearchRequest("", ["event"], From: _now, To: _now.AddHours(12)))).Items.Select(i => i.Title)); // today
        Assert.Empty((await Find(new SearchRequest("", ["event"], From: _now.AddHours(2), To: _now.AddHours(3)))).Items.Select(i => i.Title).Except(["Show gratis"]));
        Assert.Equal(["Aula paga"], (await Find(new SearchRequest("", ["event"], Category: "aula"))).Items.Select(i => i.Title));
        Assert.Equal(["Show gratis", "Aula paga"], (await Find(new SearchRequest("", ["event"], Sort: "start"))).Items.Select(i => i.Title));
        Assert.Equal(["Aula paga", "Show gratis"], (await Find(new SearchRequest("", ["event"], Sort: "name"))).Items.Select(i => i.Title));
    }

    [Fact]
    public async Task OnlyEventsPassATimeFilterNotIslands()
    {
        await Announce(_owner);
        var page = await Find(new SearchRequest("", ["event", "island"], From: _now, To: _now.AddDays(1)));
        Assert.All(page.Items, i => Assert.Equal("event", i.Kind));
    }

    // ---- places -----------------------------------------------------------------------------------------------------------------------------

    private async Task<JsonElement> Place(Guid viewer) => JsonSerializer.SerializeToElement(await PlaceDirectory.InfoAsync(_db, viewer, _island.Id, _now, default));

    [Fact]
    public async Task APlaceTellsItsFactsAndWhetherTheViewerCanEnter()
    {
        var p = await Place(_fan);
        Assert.Equal("Ilha Festa", p.GetProperty("name").GetString()); Assert.Equal("dona", p.GetProperty("owner").GetProperty("username").GetString()); Assert.Equal("open", p.GetProperty("access").GetString()); Assert.True(p.GetProperty("canEnter").GetBoolean());
        Assert.False(p.GetProperty("online").GetBoolean()); Assert.Equal(JsonValueKind.Null, p.GetProperty("visitors").ValueKind);
        _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = _island.Id, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now, LastHeartbeatAt = _now, PlayerCount = 2 }); await _db.SaveChangesAsync();
        p = await Place(_fan); Assert.True(p.GetProperty("online").GetBoolean()); Assert.Equal(2, p.GetProperty("visitors").GetInt32()); Assert.Equal("full", p.GetProperty("access").GetString()); Assert.False(p.GetProperty("canEnter").GetBoolean());
    }

    [Fact]
    public async Task ABannedViewerAClosedIslandAndAHiddenOneAreTold()
    {
        _db.IslandBans.Add(new IslandBan { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _fan, BannedByUserId = _owner, Reason = "x", CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal("banned", (await Place(_fan)).GetProperty("access").GetString()); Assert.Equal("open", (await Place(_other)).GetProperty("access").GetString());
        _island.AccessClosed = true; await _db.SaveChangesAsync(); Assert.Equal("closed", (await Place(_other)).GetProperty("access").GetString()); Assert.Equal("open", (await Place(_owner)).GetProperty("access").GetString());
        _island.AccessClosed = false; _island.IsPublic = false; await _db.SaveChangesAsync();
        Assert.Null(await PlaceDirectory.InfoAsync(_db, _other, _island.Id, _now, default)); Assert.NotNull(await PlaceDirectory.InfoAsync(_db, _owner, _island.Id, _now, default));
        Assert.Null(await PlaceDirectory.InfoAsync(_db, _owner, Guid.NewGuid(), _now, default));
    }
}
