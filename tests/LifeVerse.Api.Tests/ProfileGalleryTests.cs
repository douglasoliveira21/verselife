using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The gallery and the cover of a profile: only images of the owner, in formats the game draws, with a limit, an order, a main picture that is the profile photo, and the privacy of the profile.</summary>
public sealed class ProfileGalleryTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _ana, _bia;

    public ProfileGalleryTests() { (_ana, _bia) = (User("ana"), User("bia")); }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now.AddDays(-30), UpdatedAt = _now.AddDays(-30) });
        _db.SaveChanges(); return id;
    }

    private async Task<Guid> Image(Guid owner, string mime = "image/png", long size = 10)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = "foto", Category = "textures", CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "t/foto", MimeType = mime, ByteSize = size });
        await _db.SaveChangesAsync(); return asset.Id;
    }

    private async Task<JsonElement> View(Guid viewer, Guid target) => JsonSerializer.SerializeToElement(await ProfileService.ViewAsync(_db, viewer, target, _now, default));
    private Task<ProfileFailure?> Add(Guid user, Guid asset) => ProfileGallery.AddAsync(_db, user, asset, _now, default);

    [Fact]
    public async Task TheFirstImageBecomesTheMainOneAndTheProfilePhoto()
    {
        var first = await Image(_ana); var second = await Image(_ana);
        Assert.Null(await Add(_ana, first)); Assert.Null(await Add(_ana, second));
        var gallery = (await View(_bia, _ana)).GetProperty("gallery").EnumerateArray().ToList();
        Assert.Equal(2, gallery.Count); Assert.True(gallery[0].GetProperty("main").GetBoolean()); Assert.False(gallery[1].GetProperty("main").GetBoolean());
        Assert.Equal(first, (await _db.Users.SingleAsync(x => x.Id == _ana)).ProfilePhotoAssetId); Assert.True((await View(_bia, _ana)).GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task OnlyOwnImagesInADrawnFormatAndSizeAreAccepted()
    {
        Assert.Equal("photo_not_owned", (await Add(_ana, await Image(_bia)))!.Code);
        Assert.Equal("photo_not_owned", (await Add(_ana, Guid.NewGuid()))!.Code);
        Assert.Equal("photo_not_image", (await Add(_ana, await Image(_ana, "application/zip")))!.Code);
        Assert.Equal("photo_too_big", (await Add(_ana, await Image(_ana, size: ProfileGallery.MaxBytes + 1)))!.Code);
        Assert.Empty(await _db.ProfileGallery.ToListAsync());
    }

    [Fact]
    public async Task AnImageCannotBeAddedTwiceAndTheGalleryHasALimit()
    {
        var one = await Image(_ana); Assert.Null(await Add(_ana, one)); Assert.Equal("already_in_gallery", (await Add(_ana, one))!.Code);
        for (var i = 1; i < ProfileGallery.MaxImages; i++) Assert.Null(await Add(_ana, await Image(_ana)));
        Assert.Equal("gallery_full", (await Add(_ana, await Image(_ana)))!.Code);
    }

    [Fact]
    public async Task TheMainPictureCanChangeAndRemovingTheMainOnePromotesTheNext()
    {
        var a = await Image(_ana); var b = await Image(_ana); await Add(_ana, a); await Add(_ana, b);
        var ids = (await ProfileGallery.ListAsync(_db, _ana, default)).Select(x => x.Id).ToList();
        Assert.Null(await ProfileGallery.SetMainAsync(_db, _ana, ids[1], _now, default)); Assert.Equal(b, (await _db.Users.SingleAsync(x => x.Id == _ana)).ProfilePhotoAssetId);
        Assert.Null(await ProfileGallery.RemoveAsync(_db, _ana, ids[1], _now, default)); Assert.Equal(a, (await _db.Users.SingleAsync(x => x.Id == _ana)).ProfilePhotoAssetId);
        Assert.Null(await ProfileGallery.RemoveAsync(_db, _ana, ids[0], _now, default)); Assert.Null((await _db.Users.SingleAsync(x => x.Id == _ana)).ProfilePhotoAssetId); Assert.False((await View(_ana, _ana)).GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task NobodyChangesAnotherPersonsGallery()
    {
        await Add(_ana, await Image(_ana)); var id = (await ProfileGallery.ListAsync(_db, _ana, default))[0].Id;
        Assert.Equal("not_found", (await ProfileGallery.RemoveAsync(_db, _bia, id, _now, default))!.Code);
        Assert.Equal("not_found", (await ProfileGallery.SetMainAsync(_db, _bia, id, _now, default))!.Code);
        Assert.Single(await ProfileGallery.ListAsync(_db, _ana, default));
    }

    [Fact]
    public async Task TheOrderNeedsEveryImageOnce()
    {
        await Add(_ana, await Image(_ana)); await Add(_ana, await Image(_ana)); await Add(_ana, await Image(_ana));
        var ids = (await ProfileGallery.ListAsync(_db, _ana, default)).Select(x => x.Id).ToList();
        Assert.Equal("bad_order", (await ProfileGallery.ReorderAsync(_db, _ana, [ids[0], ids[1]], default))!.Code);
        Assert.Equal("bad_order", (await ProfileGallery.ReorderAsync(_db, _ana, [ids[0], ids[0], ids[1]], default))!.Code);
        Assert.Equal("bad_order", (await ProfileGallery.ReorderAsync(_db, _ana, [ids[0], ids[1], Guid.NewGuid()], default))!.Code);
        Assert.Null(await ProfileGallery.ReorderAsync(_db, _ana, [ids[2], ids[0], ids[1]], default));
        Assert.Equal([ids[2], ids[0], ids[1]], (await ProfileGallery.ListAsync(_db, _ana, default)).Select(x => x.Id).ToList());
    }

    [Fact]
    public async Task PrivateProfilesHideGalleryAndCoverFromOthers()
    {
        var image = await Image(_ana); await Add(_ana, image); Assert.Null(await ProfileGallery.SetCoverAsync(_db, _ana, image, _now, default));
        var seen = await View(_bia, _ana); Assert.Single(seen.GetProperty("gallery").EnumerateArray()); Assert.True(seen.GetProperty("hasCover").GetBoolean());
        Assert.Null(await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(PrivateProfile: true), _now, default));
        var hidden = await View(_bia, _ana); Assert.Empty(hidden.GetProperty("gallery").EnumerateArray()); Assert.False(hidden.GetProperty("hasCover").GetBoolean());
        Assert.Single((await View(_ana, _ana)).GetProperty("gallery").EnumerateArray());
    }

    [Fact]
    public async Task TheCoverFollowsTheSameRulesAndCanBeCleared()
    {
        Assert.Equal("photo_not_owned", (await ProfileGallery.SetCoverAsync(_db, _ana, await Image(_bia), _now, default))!.Code);
        Assert.Equal("photo_not_image", (await ProfileGallery.SetCoverAsync(_db, _ana, await Image(_ana, "text/plain"), _now, default))!.Code);
        Assert.Null(await ProfileGallery.SetCoverAsync(_db, _ana, await Image(_ana), _now, default)); Assert.True((await View(_ana, _ana)).GetProperty("hasCover").GetBoolean());
        Assert.Null(await ProfileGallery.SetCoverAsync(_db, _ana, null, _now, default)); Assert.False((await View(_ana, _ana)).GetProperty("hasCover").GetBoolean());
    }
}
