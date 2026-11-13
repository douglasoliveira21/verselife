using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Groups;
using LifeVerse.Api.Modules.Social;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Groups: category, language, rating, cover, headquarters and the price of joining; invitations with a message and a cargo; announcements that expire; and notifications that are kept.</summary>
public sealed class GroupProfileExtrasTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly Guid _leader, _admin, _moderator, _member, _newcomer, _island, _privateIsland;

    public GroupProfileExtrasTests()
    {
        (_leader, _admin, _moderator, _member, _newcomer) = (User("lider"), User("admin"), User("moderador"), User("membro"), User("novato"));
        _island = Guid.NewGuid(); _privateIsland = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _admin, Name = "Praça", Slug = "praca", IsPublic = true }); _db.Islands.Add(new Island { Id = _privateIsland, OwnerUserId = _admin, Name = "Casa", Slug = "casa", IsPublic = false }); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid(); _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = 1_000, CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id;
    }

    private static JsonElement Json(GroupResult r) { Assert.True(r.Ok, r.Error); return JsonSerializer.SerializeToElement(r.Data, Web); }
    private long Coins(Guid user) => _db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;
    private SocialGroup Stored(Guid id) => _db.SocialGroups.AsNoTracking().Single(g => g.Id == id);

    private async Task<Guid> Make(string name = "Construtores LV", string policy = "open", long? fee = null, Guid? by = null, string? category = null, string? language = null)
    {
        var made = await GroupService.CreateAsync(_db, by ?? _leader, new GroupProfileRequest(name, "", null, "public", policy, null, null, null, null, category, language, null, null, null, fee), _now, default); Assert.True(made.Ok, made.Error);
        return JsonSerializer.SerializeToElement(made.Data).GetProperty("id").GetGuid();
    }

    private void Place(Guid group, Guid user, string key) { var role = _db.GroupRoles.Single(r => r.GroupId == group && r.SystemKey == key); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group, UserId = user, Role = key, RoleId = role.Id, JoinedAt = _now }); _db.SaveChanges(); }

    // ---- the profile -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheNewFieldsAreCheckedAndSaved_WithSensibleDefaults()
    {
        var plain = await Make(); var g = Stored(plain); Assert.Equal(("social", "pt-BR", "livre", "", null, 0L), (g.Category, g.Language, g.ContentRating, g.CoverUrl, g.HeadquartersIslandId, g.JoinFee));
        async Task<string?> Try(Func<GroupProfileRequest, GroupProfileRequest> change) => (await GroupService.CreateAsync(_db, _leader, change(new GroupProfileRequest($"G{Guid.NewGuid():N}"[..10], "", null, "public", "open", null, null, null, null)), _now, default)).Error;
        Assert.Equal("bad_category", await Try(r => r with { Category = "ditadura" })); Assert.Equal("bad_language", await Try(r => r with { Language = "português do brasil!" })); Assert.Equal("bad_rating", await Try(r => r with { ContentRating = "x" }));
        Assert.Equal("bad_cover", await Try(r => r with { CoverUrl = "http://exemplo.com/a.png" })); Assert.Equal("bad_cover", await Try(r => r with { CoverUrl = "https://user:senha@exemplo.com/a.png" })); Assert.Equal("bad_cover", await Try(r => r with { CoverUrl = "https://exemplo.com/" + new string('a', 400) }));
        Assert.Equal("bad_headquarters", await Try(r => r with { HeadquartersIslandId = Guid.NewGuid() })); Assert.Equal("bad_headquarters", await Try(r => r with { HeadquartersIslandId = _privateIsland })); // somebody else's private island is no meeting place
        Assert.Equal("bad_fee", await Try(r => r with { JoinFee = -1 })); Assert.Equal("bad_fee", await Try(r => r with { JoinFee = GroupRules.MaxJoinFee + 1 })); Assert.Equal("bad_fee", await Try(r => r with { JoinPolicy = "request", JoinFee = 50 })); // only an open group asks a price
        var full = await GroupService.CreateAsync(_db, _admin, new GroupProfileRequest("Mestres de obra", "", null, "public", "open", null, null, null, null, "construcao", "en-US", "moderado", "https://exemplo.com/capa.png", _privateIsland, 250), _now, default);
        Assert.True(full.Ok, full.Error); var saved = Stored(JsonSerializer.SerializeToElement(full.Data).GetProperty("id").GetGuid()); Assert.Equal(("construcao", "en-US", "moderado", "https://exemplo.com/capa.png", _privateIsland, 250L), (saved.Category, saved.Language, saved.ContentRating, saved.CoverUrl, saved.HeadquartersIslandId, saved.JoinFee)); // the owner of the island may meet there
    }

    [Fact]
    public async Task EditingChangesThePriceAndTheHeadquarters_AndLeavingOpenEntryDropsThePrice()
    {
        var id = await Make(fee: 100);
        Assert.Equal("forbidden", (await GroupService.UpdateProfileAsync(_db, _member, id, new GroupProfileRequest(null, null, null, null, null, null, null, null, null, JoinFee: 5), _now, default)).Error);
        Assert.True((await GroupService.UpdateProfileAsync(_db, _leader, id, new GroupProfileRequest(null, null, null, null, null, null, null, null, null, Category: "musica", HeadquartersIslandId: _island, JoinFee: 300), _now, default)).Ok);
        var g = Stored(id); Assert.Equal(("musica", _island, 300L), (g.Category, g.HeadquartersIslandId, g.JoinFee));
        Assert.True((await GroupService.UpdateProfileAsync(_db, _leader, id, new GroupProfileRequest(null, null, null, null, null, null, null, null, null, HeadquartersIslandId: Guid.Empty), _now, default)).Ok); Assert.Null(Stored(id).HeadquartersIslandId); // the empty id clears it
        Assert.Equal("bad_category", (await GroupService.UpdateProfileAsync(_db, _leader, id, new GroupProfileRequest(null, null, null, null, null, null, null, null, null, Category: "x"), _now, default)).Error);
        Assert.True((await GroupService.UpdateProfileAsync(_db, _leader, id, new GroupProfileRequest(null, null, null, null, "request", null, null, null, null), _now, default)).Ok); Assert.Equal(0, Stored(id).JoinFee); // a group that no longer is open stops asking a price
        Assert.Contains(_db.GroupAuditEntries.AsNoTracking().ToList(), e => e.Action == "profile_updated" && e.DetailJson.Contains("join_fee"));
        var card = (await GroupService.MineAsync(_db, _leader, _now, default)).Single(); Assert.Equal(("musica", 0L), (card.Category, card.JoinFee));
    }

    [Fact]
    public async Task TheSearchFiltersByCategoryLanguagePolicyAndPrice()
    {
        await Make("Pedreiros", "open", 0, category: "construcao"); await Make("Carpinteiros", "request", category: "construcao", language: "en-US"); await Make("Pagos", "open", 50, category: "jogos"); await Make("Cantores", "invite", category: "musica");
        async Task<string[]> Find(string? category = null, string? language = null, string? policy = null, bool free = false) => (await GroupService.SearchAsync(_db, _newcomer, null, 0, _now, default, category, language, policy, free)).Select(c => c.Name).Order().ToArray();
        Assert.Equal(4, (await Find()).Length); Assert.Equal(["Carpinteiros", "Pedreiros"], await Find("construcao")); Assert.Equal(["Carpinteiros"], await Find(language: "en-US")); Assert.Equal(["Pagos", "Pedreiros"], await Find(policy: "open")); Assert.Equal(["Cantores", "Carpinteiros", "Pedreiros"], await Find(free: true)); Assert.Empty(await Find("construcao", "pt-BR", "request"));
    }

    // ---- the price of joining ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task JoiningAnOpenGroupWithAPriceNeedsTheExactPrice_AndMovesCoinsToTheChestWithTheMember()
    {
        var id = await Make(fee: 200);
        var asked = await GroupService.JoinAsync(_db, _newcomer, id, null, _now, null, default); Assert.Equal("fee_required", asked.Error); Assert.Equal(200, JsonSerializer.SerializeToElement(asked.Data, Web).GetProperty("fee").GetInt64());
        Assert.Equal("fee_required", (await GroupService.JoinAsync(_db, _newcomer, id, null, _now, null, default, confirmFee: 199)).Error); Assert.Equal(1_000, Coins(_newcomer)); Assert.False(await _db.GroupMembers.AnyAsync(m => m.UserId == _newcomer));
        _db.Wallets.Single(w => w.UserId == _newcomer).AvailableCoins = 100; await _db.SaveChangesAsync(); Assert.Equal("insufficient_balance", (await GroupService.JoinAsync(_db, _newcomer, id, null, _now, null, default, confirmFee: 200)).Error);
        _db.Wallets.Single(w => w.UserId == _newcomer).AvailableCoins = 1_000; _db.Wallets.Single(w => w.UserId == _newcomer).IsBlocked = true; await _db.SaveChangesAsync(); Assert.Equal("wallet_blocked", (await GroupService.JoinAsync(_db, _newcomer, id, null, _now, null, default, confirmFee: 200)).Error);
        _db.Wallets.Single(w => w.UserId == _newcomer).IsBlocked = false; await _db.SaveChangesAsync();
        Assert.True((await GroupService.JoinAsync(_db, _newcomer, id, null, _now, null, default, confirmFee: 200)).Ok);
        Assert.Equal(800, Coins(_newcomer)); Assert.True(await _db.GroupMembers.AnyAsync(m => m.GroupId == id && m.UserId == _newcomer));
        var chest = _db.GroupTreasuries.AsNoTracking().Single(t => t.GroupId == id); Assert.Equal(200, chest.Balance); Assert.Contains(_db.GroupLedger.AsNoTracking().ToList(), e => e.GroupId == id && e.Type == "join_fee" && e.Amount == 200 && e.ActorUserId == _newcomer);
        Assert.Equal("already_member", (await GroupService.JoinAsync(_db, _newcomer, id, null, _now, null, default, confirmFee: 200)).Error); Assert.Equal(800, Coins(_newcomer)); // a double click does not charge twice
        Assert.True((await GroupService.LeaveAsync(_db, _newcomer, id, _now, default)).Ok); Assert.True((await GroupService.JoinAsync(_db, _newcomer, id, null, _now, null, default, confirmFee: 200)).Ok); Assert.Equal(600, Coins(_newcomer)); // the door has a price every time
        var free = await Make("Grátis", fee: 0); Assert.True((await GroupService.JoinAsync(_db, _newcomer, free, null, _now, null, default)).Ok); Assert.Equal(600, Coins(_newcomer));
    }

    // ---- invitations ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnInvitationCarriesAMessageAndTheCargoTheInviterCanGive()
    {
        var id = await Make("Os Exploradores", "invite"); Place(id, _admin, "admin"); Place(id, _moderator, "moderator"); Place(id, _member, "member");
        var moderatorRole = _db.GroupRoles.Single(r => r.GroupId == id && r.SystemKey == "moderator"); var ownerRole = _db.GroupRoles.Single(r => r.GroupId == id && r.SystemKey == "owner"); var memberRole = _db.GroupRoles.Single(r => r.GroupId == id && r.SystemKey == "member");
        Assert.Equal("bad_message", (await GroupService.InviteAsync(_db, _admin, id, _newcomer, _now, null, default, new string('x', 201))).Error);
        Assert.Equal("bad_role", (await GroupService.InviteAsync(_db, _admin, id, _newcomer, _now, null, default, "oi", Guid.NewGuid())).Error); Assert.Equal("bad_role", (await GroupService.InviteAsync(_db, _admin, id, _newcomer, _now, null, default, "oi", ownerRole.Id)).Error);
        Assert.Equal("outranked", (await GroupService.InviteAsync(_db, _moderator, id, _newcomer, _now, null, default, "oi", moderatorRole.Id)).Error); // a moderator cannot hand out cargos
        Assert.True((await GroupService.InviteAsync(_db, _moderator, id, _newcomer, _now, null, default, "só o cargo comum", memberRole.Id)).Ok); Assert.Single(_db.GroupInvites.Where(i => i.Status == "pending")); _db.GroupInvites.RemoveRange(_db.GroupInvites); await _db.SaveChangesAsync();
        Assert.True((await GroupService.InviteAsync(_db, _admin, id, _newcomer, _now, null, default, "  Vem construir com a gente!  ", moderatorRole.Id)).Ok);
        var invite = _db.GroupInvites.AsNoTracking().Single(); Assert.Equal(("Vem construir com a gente!", moderatorRole.Id), (invite.Message, invite.RoleId));
        var center = await NotificationCenter.ListAsync(_db, _newcomer, "convites", false, null, 0, 20, _now, default); var body = JsonSerializer.SerializeToElement(center.Data, Web).GetProperty("items")[0].GetProperty("body").GetString(); Assert.Contains("Vem construir com a gente!", body); Assert.Contains("admin", body);
        Assert.True((await GroupService.RespondToInviteAsync(_db, _newcomer, invite.Id, true, _now, default)).Ok);
        var joined = _db.GroupMembers.AsNoTracking().Single(m => m.GroupId == id && m.UserId == _newcomer); Assert.Equal((moderatorRole.Id, "moderator"), (joined.RoleId, joined.Role)); // the cargo the invitation promised
    }

    // ---- announcements -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnAnnouncementCanExpireAndCarryAnHttpsLink_AndOnlyManagersStillSeeTheExpiredOne()
    {
        var id = await Make("Os Exploradores", "open"); Place(id, _member, "member");
        Task<GroupResult> Post(string title, DateTimeOffset? ends = null, string? link = null) => GroupService.PostAnnouncementAsync(_db, _leader, id, new AnnouncementRequest(title, "Texto", false, ends, link), _now, null, default);
        Assert.Equal("bad_attachment", (await Post("A", link: "http://exemplo.com/x")).Error); Assert.Equal("bad_attachment", (await Post("A", link: "ftp://exemplo.com/x")).Error); Assert.Equal("bad_expiry", (await Post("A", _now.AddSeconds(10))).Error); Assert.Equal("bad_expiry", (await Post("A", _now.AddDays(400))).Error);
        Assert.True((await Post("Permanente")).Ok); Assert.True((await Post("Passageiro", _now.AddDays(1), "https://exemplo.com/cartaz.png")).Ok);
        var both = Json(await GroupService.AnnouncementsAsync(_db, _member, id, _now, default)); Assert.Equal(2, both.GetArrayLength()); Assert.Equal("https://exemplo.com/cartaz.png", both.EnumerateArray().Single(a => a.GetProperty("title").GetString() == "Passageiro").GetProperty("attachmentUrl").GetString());
        _now = _now.AddDays(2);
        Assert.Equal(["Permanente"], Json(await GroupService.AnnouncementsAsync(_db, _member, id, _now, default)).EnumerateArray().Select(a => a.GetProperty("title").GetString()!).ToArray()); // the member no longer sees it
        Assert.Equal(2, Json(await GroupService.AnnouncementsAsync(_db, _leader, id, _now, default)).GetArrayLength()); // the manager does
    }

    // ---- notifications that are kept ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WhatHappensToAPersonIsKeptForWhenTheyAreBackOnline()
    {
        var id = await Make("Os Exploradores", "request"); Place(id, _moderator, "moderator"); Place(id, _member, "member"); Place(id, _newcomer, "member"); var adminRole = _db.GroupRoles.Single(r => r.GroupId == id && r.SystemKey == "admin");
        Assert.True((await GroupService.SetRoleAsync(_db, _leader, id, _member, adminRole.Id, _now, null, default)).Ok);
        Assert.True((await GroupService.KickAsync(_db, _leader, id, _newcomer, null, _now, null, default)).Ok);
        Assert.True((await GroupService.BanAsync(_db, _leader, id, _moderator, "spam", 3, _now, null, default)).Ok);
        var kept = _db.GroupNotifications.AsNoTracking().ToList();
        Assert.Contains(kept, n => n.UserId == _member && n.Type == "group_role_changed" && n.Title.Contains("Administrador")); Assert.Contains(kept, n => n.UserId == _newcomer && n.Type == "group_kicked"); Assert.Contains(kept, n => n.UserId == _moderator && n.Type == "group_banned" && n.Body.Contains("3 dia"));
        var center = JsonSerializer.SerializeToElement((await NotificationCenter.ListAsync(_db, _newcomer, "grupos", true, null, 0, 20, _now, default)).Data, Web); Assert.Contains("removido", center.GetProperty("items")[0].GetProperty("title").GetString()); // and it is in the notification center
    }
}
