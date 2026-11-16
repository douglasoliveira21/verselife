using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Favorites, achievements and statistics of a profile: worked out from real records, never invented, and hidden according to what the person chose.</summary>
public sealed class ProfileSectionsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _ana, _bia, _island;

    public ProfileSectionsTests()
    {
        (_ana, _bia) = (User("ana", 30), User("bia", 30));
        _island = Guid.NewGuid(); _db.Islands.Add(new Island { Id = _island, OwnerUserId = _ana, Name = "Ilha Esmeralda", Slug = "esmeralda", IsPublic = true, ShowInSearch = true, CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name, int daysOld)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now.AddDays(-daysOld), UpdatedAt = _now.AddDays(-daysOld) });
        _db.SaveChanges(); return id;
    }

    private async Task<JsonElement> View(Guid viewer, Guid target) => JsonSerializer.SerializeToElement(await ProfileService.ViewAsync(_db, viewer, target, _now, default));
    private async Task Update(Guid user, ProfileUpdate update) => Assert.Null(await ProfileService.UpdateAsync(_db, user, update, _now, default));

    private async Task FavoriteIsland(Guid user, bool hidden = false)
    {
        _db.SearchEntries.Add(new SearchEntry { Id = Guid.NewGuid(), Kind = "island", RefId = _island, Title = "Ilha Esmeralda", SearchText = "ilha esmeralda", IslandId = _island, Hidden = hidden, SourceUpdatedAt = _now, IndexedAt = _now });
        _db.SearchFavorites.Add(new SearchFavorite { Id = Guid.NewGuid(), UserId = user, Kind = "island", RefId = _island, CreatedAt = _now }); await _db.SaveChangesAsync();
    }

    private static HashSet<string> Keys(JsonElement profile) => [.. profile.GetProperty("achievements").EnumerateArray().Select(a => a.GetProperty("key").GetString()!)];

    [Fact]
    public async Task FavoritesAreOptInAndOwnersAlwaysSeeTheirs()
    {
        await FavoriteIsland(_ana);
        Assert.Empty((await View(_bia, _ana)).GetProperty("favorites").EnumerateArray());
        Assert.Single((await View(_ana, _ana)).GetProperty("favorites").EnumerateArray());
        await Update(_ana, new ProfileUpdate(ShowFavorites: true));
        var seen = (await View(_bia, _ana)).GetProperty("favorites").EnumerateArray().ToList();
        Assert.Single(seen); Assert.Equal("Ilha Esmeralda", seen[0].GetProperty("title").GetString()); Assert.Equal("Ilha", seen[0].GetProperty("label").GetString()); Assert.Equal(_island, seen[0].GetProperty("islandId").GetGuid());
        await Update(_ana, new ProfileUpdate(ShowFavorites: false)); Assert.Empty((await View(_bia, _ana)).GetProperty("favorites").EnumerateArray());
    }

    [Fact]
    public async Task FavoritesOfHiddenThingsOrOfPrivateProfilesAreNotShown()
    {
        await FavoriteIsland(_ana, hidden: true); await Update(_ana, new ProfileUpdate(ShowFavorites: true));
        Assert.Empty((await View(_bia, _ana)).GetProperty("favorites").EnumerateArray());
    }

    [Fact]
    public async Task PeopleAreNeverListedAsFavorites()
    {
        _db.SearchFavorites.Add(new SearchFavorite { Id = Guid.NewGuid(), UserId = _ana, Kind = "person", RefId = _bia, CreatedAt = _now }); _db.SearchEntries.Add(new SearchEntry { Id = Guid.NewGuid(), Kind = "person", RefId = _bia, Title = "bia", SearchText = "bia", SourceUpdatedAt = _now, IndexedAt = _now }); await _db.SaveChangesAsync();
        await Update(_ana, new ProfileUpdate(ShowFavorites: true));
        Assert.Empty((await View(_ana, _ana)).GetProperty("favorites").EnumerateArray());
    }

    [Fact]
    public async Task ANewcomerHasNoAchievementsNothingIsInvented()
    {
        Assert.Empty((await View(_bia, _ana)).GetProperty("achievements").EnumerateArray());
    }

    [Fact]
    public async Task AchievementsComeFromRecordsWithTheirDates()
    {
        var old = User("velha", 400);
        Assert.Contains("veteran_1y", Keys(await View(_bia, old)));
        var date = (await View(_bia, old)).GetProperty("achievements").EnumerateArray().Single(a => a.GetProperty("key").GetString() == "veteran_1y").GetProperty("unlockedAt").GetDateTimeOffset();
        Assert.Equal(_now.AddDays(-400).AddYears(1), date);
        for (var i = 0; i < 3; i++) _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = _island, OwnerUserId = _ana, Name = $"T{i}", BoundsJson = "{}", CreatedAt = _now.AddDays(-10 + i), UpdatedAt = _now });
        _db.ProfileGallery.AddRange(Enumerable.Range(0, 3).Select(i => new ProfileGalleryImage { Id = Guid.NewGuid(), UserId = _ana, AssetId = Guid.NewGuid(), Position = i, CreatedAt = _now.AddDays(-i) })); await _db.SaveChangesAsync();
        var keys = Keys(await View(_bia, _ana)); Assert.Contains("first_parcel", keys); Assert.Contains("landowner", keys); Assert.Contains("photographer", keys);
    }

    [Fact]
    public async Task WhatThePersonHidesProducesNoAchievementForOthers()
    {
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = _island, OwnerUserId = _ana, Name = "Casa", BoundsJson = "{}", CreatedAt = _now.AddDays(-5), UpdatedAt = _now });
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana, Name = "Clube", NameKey = "clube", Privacy = "public", CreatedAt = _now, UpdatedAt = _now };
        _db.SocialGroups.Add(group); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = _ana, Role = "owner", JoinedAt = _now.AddDays(-3) }); await _db.SaveChangesAsync();
        var open = Keys(await View(_bia, _ana)); Assert.Contains("first_parcel", open); Assert.Contains("community", open); Assert.Contains("founder", open);
        await Update(_ana, new ProfileUpdate(HideParcels: true, HideGroups: true));
        Assert.Empty(Keys(await View(_bia, _ana))); Assert.Contains("first_parcel", Keys(await View(_ana, _ana)));
        await Update(_ana, new ProfileUpdate(HideAchievements: true, HideParcels: false)); Assert.Empty(Keys(await View(_bia, _ana))); Assert.NotEmpty(Keys(await View(_ana, _ana)));
    }

    [Fact]
    public async Task StatsCountRealRecordsAndFollowThePrivacyChoices()
    {
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = _island, OwnerUserId = _ana, Name = "Casa", BoundsJson = "{}", CreatedAt = _now, UpdatedAt = _now, ShowInProfile = true });
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = _island, OwnerUserId = _ana, Name = "Depósito", BoundsJson = "{}", CreatedAt = _now, UpdatedAt = _now });
        _db.EventAttendees.Add(new EventAttendee { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = _ana, JoinedAt = _now }); await _db.SaveChangesAsync();
        var others = (await View(_bia, _ana)).GetProperty("stats"); Assert.Equal(30, others.GetProperty("daysMember").GetInt32()); Assert.Equal(1, others.GetProperty("parcels").GetInt32()); Assert.Equal(1, others.GetProperty("events").GetInt32());
        Assert.Equal(2, (await View(_ana, _ana)).GetProperty("stats").GetProperty("parcels").GetInt32());
        await Update(_ana, new ProfileUpdate(HideParcels: true)); Assert.Equal(JsonValueKind.Null, (await View(_bia, _ana)).GetProperty("stats").GetProperty("parcels").ValueKind);
        await Update(_ana, new ProfileUpdate(HideStats: true)); Assert.Equal(JsonValueKind.Null, (await View(_bia, _ana)).GetProperty("stats").ValueKind); Assert.NotEqual(JsonValueKind.Null, (await View(_ana, _ana)).GetProperty("stats").ValueKind);
    }

    [Fact]
    public async Task TheBirthdayCanBeHiddenAndPrivateProfilesShowNothing()
    {
        Assert.NotEqual(JsonValueKind.Null, (await View(_bia, _ana)).GetProperty("birthday").ValueKind);
        await Update(_ana, new ProfileUpdate(HideAnniversary: true)); Assert.Equal(JsonValueKind.Null, (await View(_bia, _ana)).GetProperty("birthday").ValueKind); Assert.NotEqual(JsonValueKind.Null, (await View(_ana, _ana)).GetProperty("birthday").ValueKind);
        await Update(_ana, new ProfileUpdate(PrivateProfile: true, ShowFavorites: true)); var hidden = await View(_bia, _ana);
        Assert.Empty(hidden.GetProperty("achievements").EnumerateArray()); Assert.Empty(hidden.GetProperty("favorites").EnumerateArray()); Assert.Equal(JsonValueKind.Null, hidden.GetProperty("stats").ValueKind);
        var settings = (await View(_ana, _ana)).GetProperty("settings"); Assert.True(settings.GetProperty("hideAnniversary").GetBoolean()); Assert.True(settings.GetProperty("showFavorites").GetBoolean());
    }
}
