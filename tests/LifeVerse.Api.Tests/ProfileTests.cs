using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The profile: what a person writes and shows, what the server hides from others, the picture, the groups shown, the type of account and marriage inside the game.</summary>
public sealed class ProfileTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _ana, _bia, _caio, _duda;

    public ProfileTests() { (_ana, _bia, _caio, _duda) = (User("ana"), User("bia"), User("caio"), User("duda")); }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now.AddDays(-30), UpdatedAt = _now, Status = "active" });
        _db.SaveChanges(); return id;
    }

    private async Task<JsonElement> View(Guid viewer, Guid target) => JsonSerializer.SerializeToElement(await ProfileService.ViewAsync(_db, viewer, target, _now, default));

    private async Task Update(Guid user, ProfileUpdate update) => Assert.Null(await ProfileService.UpdateAsync(_db, user, update, _now, default));

    private async Task<Guid> Image(Guid owner, string mime = "image/png", string category = "textures")
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = "foto", Category = category, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "t/foto", MimeType = mime, ByteSize = 10 });
        await _db.SaveChangesAsync(); return asset.Id;
    }

    private async Task<Guid> GroupWith(Guid member, string name, string privacy = "public")
    {
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = member, Name = name, NameKey = name.ToLowerInvariant(), Privacy = privacy, CreatedAt = _now, UpdatedAt = _now };
        _db.SocialGroups.Add(group); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = member, Role = "member", JoinedAt = _now });
        await _db.SaveChangesAsync(); return group.Id;
    }

    // ---- text, birthday, picture -----------------------------------------------------------------------------------------

    [Fact]
    public async Task WhatIWriteComesBackAndOthersSeeIt()
    {
        await Update(_ana, new ProfileUpdate(Bio: "Construtora de ilhas", FirstLifeBio: "Mora em Recife", Interests: "arquitetura, música"));
        var seen = await View(_bia, _ana);
        Assert.Equal("Construtora de ilhas", seen.GetProperty("bio").GetString()); Assert.Equal("Mora em Recife", seen.GetProperty("firstLifeBio").GetString()); Assert.Equal("arquitetura, música", seen.GetProperty("interests").GetString());
        Assert.False(seen.GetProperty("isSelf").GetBoolean()); Assert.True((await View(_ana, _ana)).GetProperty("isSelf").GetBoolean());
        Assert.Equal("standard", seen.GetProperty("accountType").GetString());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("settings").ValueKind); // only the owner gets the settings
        Assert.NotEqual(JsonValueKind.Null, (await View(_ana, _ana)).GetProperty("settings").ValueKind);
    }

    [Fact]
    public async Task TooLongTextsAreRefusedAndNothingChanges()
    {
        await Update(_ana, new ProfileUpdate(Bio: "ok"));
        var failure = await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(Bio: new string('a', ProfileService.MaxBio + 1)), _now, default);
        Assert.Equal("too_long", failure?.Code); Assert.Equal("ok", (await View(_ana, _ana)).GetProperty("bio").GetString());
        Assert.Equal("too_long", (await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(Interests: new string('a', ProfileService.MaxInterests + 1)), _now, default))?.Code);
    }


    [Fact]
    public async Task TheBirthdayIsTheDayTheCharacterWasCreatedAndNobodyTypesIt()
    {
        // no avatar yet: the day the account was made (30 days before "now")
        Assert.Equal(_now.AddDays(-30).ToString("dd/MM"), (await View(_bia, _ana)).GetProperty("birthday").GetString());
        // the character is what counts once it exists
        var created = new DateTimeOffset(2025, 3, 8, 15, 0, 0, TimeSpan.Zero);
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = _ana, DisplayName = "Ana Souza", BaseModel = "x", CreatedAt = created, UpdatedAt = created }); await _db.SaveChangesAsync();
        var seen = await View(_bia, _ana);
        Assert.Equal("08/03", seen.GetProperty("birthday").GetString()); Assert.Equal(created, seen.GetProperty("joinedAt").GetDateTimeOffset());
        Assert.Equal(1, seen.GetProperty("characterAgeYears").GetInt32()); // March 2025 to November 2026
        Assert.Equal("08/03", (await View(_ana, _ana)).GetProperty("birthday").GetString());
        // the profile update has no way to change it
        Assert.DoesNotContain(typeof(ProfileUpdate).GetProperties(), p => p.Name.Contains("Birth"));
        // a private profile keeps it from others too
        await Update(_ana, new ProfileUpdate(PrivateProfile: true));
        Assert.Equal(JsonValueKind.Null, (await View(_bia, _ana)).GetProperty("birthday").ValueKind);
        Assert.Equal("08/03", (await View(_ana, _ana)).GetProperty("birthday").GetString());
    }

    [Fact]
    public async Task OnlyAnImageOfMineCanBeThePhoto()
    {
        var mine = await Image(_ana); var theirs = await Image(_bia); var sound = await Image(_ana, "audio/ogg"); var model = await Image(_ana, "model/gltf-binary");
        Assert.Equal("photo_not_owned", (await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(PhotoAssetId: theirs), _now, default))?.Code);
        Assert.Equal("photo_not_image", (await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(PhotoAssetId: sound), _now, default))?.Code);
        Assert.Equal("photo_not_image", (await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(PhotoAssetId: model), _now, default))?.Code);
        Assert.Equal("photo_not_owned", (await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(PhotoAssetId: Guid.NewGuid()), _now, default))?.Code);
        Assert.False((await View(_bia, _ana)).GetProperty("hasPhoto").GetBoolean());
        await Update(_ana, new ProfileUpdate(PhotoAssetId: mine));
        Assert.True((await View(_bia, _ana)).GetProperty("hasPhoto").GetBoolean());
        Assert.Equal(mine, (await _db.Users.AsNoTracking().SingleAsync(x => x.Id == _ana)).ProfilePhotoAssetId);
        await Update(_ana, new ProfileUpdate(ClearPhoto: true));
        Assert.False((await View(_bia, _ana)).GetProperty("hasPhoto").GetBoolean());
    }

    // ---- privacy -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APrivateProfileShowsOthersOnlyTheName()
    {
        await Update(_ana, new ProfileUpdate(Bio: "segredo", Interests: "x", PrivateProfile: true));
        var other = await View(_bia, _ana);
        Assert.True(other.GetProperty("privateProfile").GetBoolean());
        Assert.Equal("", other.GetProperty("bio").GetString()); Assert.Equal(JsonValueKind.Null, other.GetProperty("birthday").ValueKind); Assert.False(other.GetProperty("hasPhoto").GetBoolean());
        Assert.Equal("ana", other.GetProperty("username").GetString());
        Assert.Equal("segredo", (await View(_ana, _ana)).GetProperty("bio").GetString());
        await Update(_ana, new ProfileUpdate(PrivateProfile: false));
        Assert.Equal("segredo", (await View(_bia, _ana)).GetProperty("bio").GetString());
    }

    [Fact]
    public async Task GroupsAreShownOnlyWhenThePersonAndTheGroupAllowIt()
    {
        var open = await GroupWith(_ana, "Os Abertos"); var secret = await GroupWith(_ana, "Os Secretos", "private"); var hiddenOne = await GroupWith(_ana, "Os Escondidos");
        Assert.True(await ProfileService.SetGroupShownAsync(_db, _ana, hiddenOne, false, default));
        Assert.False(await ProfileService.SetGroupShownAsync(_db, _bia, open, false, default), "only a member decides");
        var names = (await View(_bia, _ana)).GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Os Abertos"], names);
        // a private group is shown to its own members
        _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = secret, UserId = _bia, Role = "member", JoinedAt = _now }); await _db.SaveChangesAsync();
        Assert.Contains("Os Secretos", (await View(_bia, _ana)).GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
        // the person sees all of their own
        Assert.Equal(3, (await View(_ana, _ana)).GetProperty("groups").GetArrayLength());
        await Update(_ana, new ProfileUpdate(HideGroups: true));
        Assert.Equal(0, (await View(_bia, _ana)).GetProperty("groups").GetArrayLength());
    }

    [Fact]
    public async Task APersonIsFoundByUsernameOrDisplayName()
    {
        Assert.Equal(_ana, await ProfileService.FindByNameAsync(_db, "Ana", default));
        Assert.Null(await ProfileService.FindByNameAsync(_db, "ninguem", default));
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = _bia, DisplayName = "Bia Estrela", BaseModel = "x", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal(_bia, await ProfileService.FindByNameAsync(_db, "bia estrela", default));
    }

    // ---- type of account ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheAdministrationMakesADonorAndTheProfileShowsIt()
    {
        Assert.Equal("standard", (await View(_bia, _ana)).GetProperty("accountType").GetString());
        Assert.Null(await ProfileService.SetAccountTypeAsync(_db, _ana, "donor", _now, default));
        var seen = await View(_bia, _ana);
        Assert.Equal("donor", seen.GetProperty("accountType").GetString()); Assert.Equal(_now, seen.GetProperty("donorSince").GetDateTimeOffset());
        Assert.Equal("invalid_type", (await ProfileService.SetAccountTypeAsync(_db, _ana, "vip", _now, default))?.Code);
        Assert.Equal("not_found", (await ProfileService.SetAccountTypeAsync(_db, Guid.NewGuid(), "donor", _now, default))?.Code);
        Assert.Null(await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(Bio: "tentando ser vip"), _now, default));
        Assert.Equal("donor", (await View(_ana, _ana)).GetProperty("accountType").GetString()); // editing the profile does not touch it
        await ProfileService.SetAccountTypeAsync(_db, _ana, "standard", _now, default);
        Assert.Equal("standard", (await View(_bia, _ana)).GetProperty("accountType").GetString());
    }

    // ---- marriage --------------------------------------------------------------------------------------------------------------

    private async Task<Guid> Propose(Guid from, Guid to, string? message = null)
    {
        var (marriage, failure) = await ProfileService.ProposeAsync(_db, from, to, message, _now, default);
        Assert.Null(failure); return marriage!.Id;
    }

    [Fact]
    public async Task AMarriageNeedsAProposalAnAnswerAndBothSidesFree()
    {
        Assert.Equal("self", (await ProfileService.ProposeAsync(_db, _ana, _ana, null, _now, default)).Failure?.Code);
        Assert.Equal("not_found", (await ProfileService.ProposeAsync(_db, _ana, Guid.NewGuid(), null, _now, default)).Failure?.Code);
        var id = await Propose(_ana, _bia, "Aceita?");
        Assert.Equal("already_proposed", (await ProfileService.ProposeAsync(_db, _bia, _ana, null, _now, default)).Failure?.Code);
        Assert.False((await View(_caio, _ana)).GetProperty("married").GetBoolean(), "a proposal is not a marriage");
        Assert.Equal("not_yours", (await ProfileService.AnswerAsync(_db, _ana, id, "accept", _now, default))?.Code);
        Assert.Equal("not_found", (await ProfileService.AnswerAsync(_db, _caio, id, "accept", _now, default))?.Code);
        Assert.Null(await ProfileService.AnswerAsync(_db, _bia, id, "accept", _now, default));
        foreach (var (viewer, target, spouse) in new[] { (_caio, _ana, "bia"), (_caio, _bia, "ana") })
        {
            var seen = await View(viewer, target);
            Assert.True(seen.GetProperty("married").GetBoolean()); Assert.Equal(spouse, seen.GetProperty("marriage").GetProperty("spouseName").GetString());
        }
        Assert.Equal("not_open", (await ProfileService.AnswerAsync(_db, _bia, id, "accept", _now, default))?.Code);
    }

    [Fact]
    public async Task OnlyOneMarriageAtATimeAndOtherProposalsAreCancelled()
    {
        var toBia = await Propose(_ana, _bia); var caioToBia = await Propose(_caio, _bia); var anaToDuda = await Propose(_ana, _duda);
        Assert.Null(await ProfileService.AnswerAsync(_db, _bia, toBia, "accept", _now, default));
        Assert.Equal("cancelled", (await _db.Marriages.AsNoTracking().SingleAsync(x => x.Id == caioToBia)).Status);
        Assert.Equal("cancelled", (await _db.Marriages.AsNoTracking().SingleAsync(x => x.Id == anaToDuda)).Status);
        Assert.Equal("already_married", (await ProfileService.ProposeAsync(_db, _ana, _duda, null, _now, default)).Failure?.Code);
        Assert.Equal("target_married", (await ProfileService.ProposeAsync(_db, _duda, _bia, null, _now, default)).Failure?.Code);
    }

    [Fact]
    public async Task DecliningCancellingAndDivorcingEndThings()
    {
        var declined = await Propose(_ana, _bia); Assert.Null(await ProfileService.AnswerAsync(_db, _bia, declined, "decline", _now, default));
        var cancelled = await Propose(_ana, _bia); Assert.Equal("not_yours", (await ProfileService.AnswerAsync(_db, _bia, cancelled, "cancel", _now, default))?.Code);
        Assert.Null(await ProfileService.AnswerAsync(_db, _ana, cancelled, "cancel", _now, default));
        Assert.Equal("not_married", (await ProfileService.DivorceAsync(_db, _ana, _now, default))?.Code);
        var id = await Propose(_ana, _bia); await ProfileService.AnswerAsync(_db, _bia, id, "accept", _now, default);
        Assert.Null(await ProfileService.DivorceAsync(_db, _bia, _now, default)); // either one can end it
        Assert.False((await View(_caio, _ana)).GetProperty("married").GetBoolean());
        var marriage = await _db.Marriages.AsNoTracking().SingleAsync(x => x.Id == id); Assert.Equal(("ended", _bia), (marriage.Status, marriage.EndedByUserId));
        var again = await Propose(_ana, _caio); Assert.Null(await ProfileService.AnswerAsync(_db, _caio, again, "accept", _now, default)); // free to marry again
    }

    [Fact]
    public async Task ProposalsExpireAndTheListShowsWhatIsWaiting()
    {
        var id = await Propose(_ana, _bia, "Oi");
        var mine = JsonSerializer.SerializeToElement(await ProfileService.MineAsync(_db, _bia, _now, default));
        Assert.Equal(1, mine.GetProperty("incoming").GetArrayLength()); Assert.Equal("ana", mine.GetProperty("incoming")[0].GetProperty("fromName").GetString());
        Assert.Equal(1, JsonSerializer.SerializeToElement(await ProfileService.MineAsync(_db, _ana, _now, default)).GetProperty("outgoing").GetArrayLength());
        var later = _now.AddDays(ProfileService.ProposalDays + 1);
        Assert.Equal(0, JsonSerializer.SerializeToElement(await ProfileService.MineAsync(_db, _bia, later, default)).GetProperty("incoming").GetArrayLength());
        Assert.Equal("not_open", (await ProfileService.AnswerAsync(_db, _bia, id, "accept", later, default))?.Code);
        Assert.Equal("too_long", (await ProfileService.ProposeAsync(_db, _ana, _bia, new string('x', 301), later, default)).Failure?.Code);
    }

    [Fact]
    public async Task AtMostFiveProposalsAtOnce()
    {
        var others = Enumerable.Range(0, 6).Select(i => User("p" + i)).ToList();
        foreach (var other in others.Take(ProfileService.MaxOutgoing)) await Propose(_ana, other);
        Assert.Equal("too_many", (await ProfileService.ProposeAsync(_db, _ana, others[^1], null, _now, default)).Failure?.Code);
    }

    [Fact]
    public async Task MarriageCanBeHiddenFromTheProfile()
    {
        var id = await Propose(_ana, _bia); await ProfileService.AnswerAsync(_db, _bia, id, "accept", _now, default);
        await Update(_ana, new ProfileUpdate(HideMarriage: true));
        Assert.False((await View(_caio, _ana)).GetProperty("married").GetBoolean());
        Assert.True((await View(_ana, _ana)).GetProperty("married").GetBoolean());
        Assert.True((await View(_caio, _bia)).GetProperty("married").GetBoolean(), "each person decides for their own profile");
    }
}

/// <summary>The lines shown to the person when marriage news arrives.</summary>
public sealed class MarriageNotificationTextTests
{
    [Theory]
    [InlineData("marriage_proposal", "pedido de casamento")]
    [InlineData("marriage_accepted", "casados")]
    [InlineData("marriage_declined", "recusado")]
    [InlineData("marriage_cancelled", "cancelado")]
    public void EveryMarriageEventHasAReadableLine(string type, string expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { type, data = new { from = Guid.NewGuid() }, at = DateTimeOffset.UtcNow });
        Assert.Contains(expected, LifeVerse.Shared.Networking.NotificationFormatter.Describe(json));
    }
}
