using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Groups;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Groups 2.0, first part: creating and shaping a group, cargos and permissions, coming in, moderation and history, leadership and the end.</summary>
public sealed class GroupServiceTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly List<(Guid User, string Type)> _pushed = [];
    private readonly GroupNotifier _notify;
    private readonly Guid _leader, _admin, _moderator, _member, _stranger, _extra;
    private Guid _group;

    public GroupServiceTests()
    {
        _notify = (user, type, _) => { _pushed.Add((user, type)); return Task.CompletedTask; };
        (_leader, _admin, _moderator, _member, _stranger, _extra) = (User("lider"), User("admin"), User("moderador"), User("membro"), User("estranho"), User("extra"));
        _group = Made("Os Exploradores", tag: "EXP").Result;
        Place(_admin, "admin"); Place(_moderator, "moderator"); Place(_member, "member");
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name) { var id = Guid.NewGuid(); _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id; }

    private async Task<string> Image(Guid owner)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = "marca", Category = "textures", CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "tests/marca.png", MimeType = "image/png", ByteSize = 10, CreatedAt = _now });
        _db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner, AssetId = asset.Id, DisplayName = "marca", CreatedAt = _now, UpdatedAt = _now });
        await _db.SaveChangesAsync();
        return $"asset://{asset.Id}";
    }

    private async Task<Guid> Made(string name, string? tag = null, string privacy = "public", string? policy = null, Guid? by = null, int? limit = null)
    {
        var result = await GroupService.CreateAsync(_db, by ?? _leader, new GroupProfileRequest(name, "Um grupo para testes", null, privacy, policy, null, null, tag, limit), _now, default);
        Assert.True(result.Ok, result.Error); return Id(result);
    }

    private static Guid Id(GroupResult result) => JsonSerializer.SerializeToElement(result.Data).GetProperty("id").GetGuid();
    private static T Data<T>(GroupResult result) => (T)result.Data!;

    /// <summary>Puts somebody straight into a role (as if they had joined and been promoted).</summary>
    private void Place(Guid user, string key)
    {
        var role = _db.GroupRoles.Single(r => r.GroupId == _group && r.SystemKey == key);
        _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = _group, UserId = user, Role = key, RoleId = role.Id, JoinedAt = _now }); _db.SaveChanges();
    }

    private Task<GroupResult> Join(Guid user, Guid? group = null, string? message = null) => GroupService.JoinAsync(_db, user, group ?? _group, message, _now, _notify, default);
    private async Task<Guid> RoleId(string key) => (await _db.GroupRoles.AsNoTracking().SingleAsync(r => r.GroupId == _group && r.SystemKey == key)).Id;
    private async Task<List<string>> Actions() => (await _db.GroupAuditEntries.AsNoTracking().Where(e => e.GroupId == _group).OrderBy(e => e.CreatedAt).ToListAsync()).Select(e => e.Action).ToList();

    // ---- the rules themselves --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheHierarchyAndTheGrantingRules()
    {
        var leader = new RoleView(Guid.NewGuid(), "Líder", GroupRules.LeaderRank, new HashSet<string>(), IsLeader: true);
        var mod = new RoleView(Guid.NewGuid(), "Mod", 40, new HashSet<string> { "kick", "invite" });
        Assert.True(GroupRules.CanActOn(leader, 999)); Assert.True(GroupRules.CanActOn(mod, 39)); Assert.False(GroupRules.CanActOn(mod, 40)); Assert.False(GroupRules.CanActOn(mod, 80));
        Assert.True(GroupRules.CanGrant(mod, ["kick"])); Assert.False(GroupRules.CanGrant(mod, ["kick", "ban"])); Assert.True(GroupRules.CanGrant(leader, ["ban", "manage_roles"]));
        Assert.True(GroupRules.CanManageRank(mod, 39)); Assert.False(GroupRules.CanManageRank(mod, 40)); Assert.True(GroupRules.CanManageRank(leader, 99)); Assert.False(GroupRules.CanManageRank(leader, 100));
        Assert.True(GroupRules.Has(leader, "ban")); Assert.False(GroupRules.Has(mod, "ban"));
        Assert.Null(GroupRules.CleanPermissions(["kick", "fly"])); Assert.Equal(["ban", "kick"], GroupRules.CleanPermissions(["kick", "ban", "kick"])); Assert.Empty(GroupRules.CleanPermissions(null)!);
        Assert.Equal(GroupPermissions.All.Count, GroupPermissions.Known.Count);
        Assert.Equal(["owner", "admin", "moderator", "member"], GroupRules.StartingRoles.Select(r => r.Key));
        Assert.True(GroupRules.StartingRoles.Zip(GroupRules.StartingRoles.Skip(1)).All(p => p.First.Rank > p.Second.Rank));
    }

    [Theory]
    [InlineData("ab", false)] [InlineData("Abc", true)] [InlineData("   Os   Exploradores  ", true)] [InlineData("a\u0007bc", false)]
    public void Names(string name, bool ok) => Assert.Equal(ok, GroupRules.ValidName(name, out _));

    [Fact]
    public void TagsColoursAndImages()
    {
        Assert.True(GroupRules.ValidTag("AB")); Assert.True(GroupRules.ValidTag("ab12")); Assert.False(GroupRules.ValidTag("A")); Assert.False(GroupRules.ValidTag("ABCDEF")); Assert.False(GroupRules.ValidTag("A-B"));
        Assert.True(GroupRules.ValidColour("#12F2E0")); Assert.False(GroupRules.ValidColour("red")); Assert.False(GroupRules.ValidColour("#12F2E"));
        Assert.True(GroupRules.ValidImageUrl("https://cdn.example.com/a.png")); Assert.False(GroupRules.ValidImageUrl("http://cdn.example.com/a.png")); Assert.False(GroupRules.ValidImageUrl("https://u:p@cdn.example.com/a.png")); Assert.False(GroupRules.ValidImageUrl("javascript:alert(1)"));
    }

    // ---- creating --------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ACreatedGroupHasItsFourCargosAndALeader()
    {
        var group = await _db.SocialGroups.AsNoTracking().SingleAsync(); var roles = await _db.GroupRoles.AsNoTracking().Where(r => r.GroupId == group.Id).OrderByDescending(r => r.Rank).ToListAsync();
        Assert.Equal(["Líder", "Administrador", "Moderador", "Membro"], roles.Select(r => r.Name)); Assert.Equal("EXP", group.Tag); Assert.Equal("os exploradores", group.NameKey);
        var me = await _db.GroupMembers.AsNoTracking().SingleAsync(m => m.UserId == _leader); Assert.Equal(roles[0].Id, me.RoleId); Assert.Equal(_leader, group.OwnerUserId);
        Assert.Contains("group_created", await Actions());
        Assert.Empty(GroupRules.SplitPermissions(roles[3].Permissions)); Assert.Contains("kick", GroupRules.SplitPermissions(roles[2].Permissions)); Assert.DoesNotContain("ban", GroupRules.SplitPermissions(roles[2].Permissions)); Assert.Equal(GroupPermissions.Known.Count, GroupRules.SplitPermissions(roles[0].Permissions).Count);
    }

    [Fact]
    public async Task CreationIsValidated()
    {
        async Task<string?> Try(GroupProfileRequest r, Guid? by = null) => (await GroupService.CreateAsync(_db, by ?? _stranger, r, _now, default)).Error;
        Assert.Equal("bad_name", await Try(new("ab", "", null, null, null, null, null, null, null)));
        Assert.Equal("name_taken", await Try(new("os EXPLORADORES", "", null, null, null, null, null, null, null)));
        Assert.Equal("tag_taken", await Try(new("Outro Grupo", "", null, null, null, null, null, "exp", null)));
        Assert.Equal("bad_tag", await Try(new("Outro Grupo", "", null, null, null, null, null, "E", null)));
        Assert.Equal("bad_identity", await Try(new("Outro Grupo", "", "http://x.com/a.png", null, null, null, null, null, null)));
        Assert.Equal("bad_identity", await Try(new("Outro Grupo", "", null, null, null, "vermelho", null, null, null)));
        Assert.Equal("bad_identity", await Try(new("Outro Grupo", "", null, null, null, null, "robot", null, null)));
        Assert.Equal("bad_policy", await Try(new("Outro Grupo", "", null, "private", "open", null, null, null, null))); // a hidden group is entered by invitation
        Assert.Equal("bad_policy", await Try(new("Outro Grupo", "", null, "secret", null, null, null, null, null)));
        Assert.Equal("bad_description", await Try(new("Outro Grupo", new string('x', 601), null, null, null, null, null, null, null)));
        Assert.Null(await Try(new("Outro Grupo", "", await Image(_stranger), "private", null, "#112233", "star", "OG", 50)));
        var made = await _db.SocialGroups.AsNoTracking().SingleAsync(g => g.Name == "Outro Grupo"); Assert.Equal("invite", made.JoinPolicy); Assert.Equal("#112233", made.AccentColor); Assert.Equal(50, made.MemberLimit);
    }

    [Fact]
    public async Task ThereAreLimitsOnHowManyGroupsOnePersonMakes()
    {
        for (var i = 1; i < GroupRules.MaxGroupsCreated; i++) await Made($"Grupo numero {i}");
        Assert.Equal("too_many_groups_created", (await GroupService.CreateAsync(_db, _leader, new("Um a mais", "", null, null, null, null, null, null, null), _now, default)).Error);
    }

    // ---- the profile -----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyWhoMayEditTheProfileDoesIt()
    {
        Assert.Equal("forbidden", (await GroupService.UpdateProfileAsync(_db, _member, _group, new("Novo Nome", null, null, null, null, null, null, null, null), _now, default)).Error);
        Assert.Equal("forbidden", (await GroupService.UpdateProfileAsync(_db, _stranger, _group, new("Novo Nome", null, null, null, null, null, null, null, null), _now, default)).Error);
        Assert.True((await GroupService.UpdateProfileAsync(_db, _admin, _group, new("Novo Nome", "Descrição nova", await Image(_admin), null, "open", "#FF8800", "star", "NN", 100), _now, default)).Ok);
        var g = await _db.SocialGroups.AsNoTracking().SingleAsync(); Assert.Equal(("Novo Nome", "open", "#FF8800", "star", "NN", 100), (g.Name, g.JoinPolicy, g.AccentColor, g.Emblem, g.Tag, g.MemberLimit)); Assert.Equal("novo nome", g.NameKey);
        Assert.Contains("profile_updated", await Actions());
        Assert.Equal("name_taken", (await GroupService.UpdateProfileAsync(_db, _admin, _group, new((await Made("Rivais", by: _stranger)) is var other ? "Rivais" : "", null, null, null, null, null, null, null, null), _now, default)).Error);
        Assert.Equal("bad_limit", (await GroupService.UpdateProfileAsync(_db, _admin, _group, new(null, null, null, null, null, null, null, null, 3), _now, default)).Error); // there are 4 members
        Assert.Equal("bad_policy", (await GroupService.UpdateProfileAsync(_db, _admin, _group, new(null, null, null, "private", "open", null, null, null, null), _now, default)).Error);
        Assert.True((await GroupService.UpdateProfileAsync(_db, _admin, _group, new(null, null, null, "private", null, null, null, null, null), _now, default)).Ok);
        Assert.Equal("invite", (await _db.SocialGroups.AsNoTracking().SingleAsync(x => x.Id == _group)).JoinPolicy); // going private closes the door to open joining
    }

    // ---- looking ----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SearchFindsOnlyPublicGroups_AndSaysWhatTheSearcherIsToThem()
    {
        await Made("Segredo de Estado", privacy: "private", by: _stranger);
        var found = await GroupService.SearchAsync(_db, _stranger, null, 0, _now, default); Assert.Equal(["Os Exploradores"], found.Select(g => g.Name));
        Assert.Equal(["Os Exploradores"], (await GroupService.SearchAsync(_db, _stranger, "explo", 0, _now, default)).Select(g => g.Name));
        Assert.Equal(["Os Exploradores"], (await GroupService.SearchAsync(_db, _stranger, "exp", 0, _now, default)).Select(g => g.Name)); // by tag
        Assert.Empty(await GroupService.SearchAsync(_db, _stranger, "segredo", 0, _now, default));
        var card = found[0]; Assert.Equal(4, card.Members); Assert.False(card.IsMember); Assert.False(card.Banned);
        Assert.True((await GroupService.SearchAsync(_db, _member, null, 0, _now, default))[0].IsMember);
        Assert.Equal(2, (await GroupService.MineAsync(_db, _stranger, _now, default)).Count + 1); // the stranger's own private group is theirs
    }

    [Fact]
    public async Task APrivateGroupIsSeenByItsMembersAndByWhoWasInvited()
    {
        var secret = await Made("Segredo de Estado", privacy: "private", by: _stranger);
        Assert.Equal("not_found", (await GroupService.ViewAsync(_db, _member, secret, _now, default)).Error);
        Assert.Equal("not_found", (await Join(_member, secret)).Error);
        Assert.True((await GroupService.InviteAsync(_db, _stranger, secret, _member, _now, _notify, default)).Ok);
        Assert.True((await GroupService.ViewAsync(_db, _member, secret, _now, default)).Ok);
        var view = Data<GroupService.GroupView>(await GroupService.ViewAsync(_db, _stranger, secret, _now, default)); Assert.True(view.IsLeader); Assert.Equal(GroupPermissions.Known.Count, view.MyPermissions.Length);
        var asMember = Data<GroupService.GroupView>(await GroupService.ViewAsync(_db, _moderator, _group, _now, default)); Assert.Equal("Moderador", asMember.MyRole); Assert.Contains("kick", asMember.MyPermissions); Assert.DoesNotContain("ban", asMember.MyPermissions);
        Assert.Equal("not_found", (await GroupService.ViewAsync(_db, _member, Guid.NewGuid(), _now, default)).Error);
    }

    // ---- coming in -----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task JoiningFollowsThePolicyOfTheGroup()
    {
        Assert.Equal("already_member", (await Join(_member)).Error);
        // a group that approves: the person asks, the people who can approve are told
        var asked = await Join(_stranger, message: "Posso entrar?"); Assert.True(asked.Ok);
        Assert.DoesNotContain(_db.GroupMembers.AsNoTracking(), m => m.UserId == _stranger);
        Assert.Contains((_leader, "group_join_request"), _pushed); Assert.Contains((_moderator, "group_join_request"), _pushed); Assert.DoesNotContain((_member, "group_join_request"), _pushed);
        Assert.True((await Join(_stranger)).Ok); Assert.Equal(1, await _db.GroupJoinRequests.CountAsync()); // asking twice is one request
        Assert.Equal("bad_message", (await Join(_extra, message: new string('x', 301))).Error);
        // open and invitation-only
        var open = await Made("Porta Aberta", policy: "open", by: _extra); Assert.True((await Join(_stranger, open)).Ok); Assert.True(await _db.GroupMembers.AnyAsync(m => m.GroupId == open && m.UserId == _stranger));
        var closed = await Made("Só Convidados", policy: "invite", by: _extra); Assert.Equal("invite_only", (await Join(_stranger, closed)).Error);
    }

    [Fact]
    public async Task RequestsAreDecidedByWhoMay()
    {
        await Join(_stranger, message: "oi"); var request = (await _db.GroupJoinRequests.AsNoTracking().SingleAsync()).Id;
        Assert.Equal("forbidden", (await GroupService.RequestsAsync(_db, _member, _group, _now, default)).Error);
        var list = Data<List<GroupService.RequestItem>>(await GroupService.RequestsAsync(_db, _moderator, _group, _now, default)); Assert.Equal("estranho", Assert.Single(list).Username); Assert.Equal("oi", list[0].Message);
        Assert.Equal("forbidden", (await GroupService.DecideRequestAsync(_db, _member, _group, request, true, _now, _notify, default)).Error);
        Assert.True((await GroupService.DecideRequestAsync(_db, _moderator, _group, request, true, _now, _notify, default)).Ok);
        Assert.True(await _db.GroupMembers.AnyAsync(m => m.UserId == _stranger)); Assert.Contains((_stranger, "group_request_decided"), _pushed);
        Assert.Equal("not_found", (await GroupService.DecideRequestAsync(_db, _moderator, _group, request, true, _now, _notify, default)).Error); // already decided
        Assert.Contains("request_approved", await Actions());
    }

    [Fact]
    public async Task ADeniedRequestAndACancelledOne()
    {
        await Join(_stranger); var request = (await _db.GroupJoinRequests.AsNoTracking().SingleAsync()).Id;
        Assert.True((await GroupService.DecideRequestAsync(_db, _admin, _group, request, false, _now, _notify, default)).Ok);
        Assert.False(await _db.GroupMembers.AnyAsync(m => m.UserId == _stranger)); Assert.Contains("request_denied", await Actions());
        await Join(_extra); Assert.True((await GroupService.CancelRequestAsync(_db, _extra, _group, _now, default)).Ok); Assert.Equal("not_found", (await GroupService.CancelRequestAsync(_db, _extra, _group, _now, default)).Error);
    }

    [Fact]
    public async Task ApprovingSomebodyWhoWasBannedInTheMeantimeIsRefused()
    {
        await Join(_stranger); var request = (await _db.GroupJoinRequests.AsNoTracking().SingleAsync()).Id;
        _db.GroupBans.Add(new GroupBan { Id = Guid.NewGuid(), GroupId = _group, UserId = _stranger, BannedByUserId = _leader, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal("banned", (await GroupService.DecideRequestAsync(_db, _leader, _group, request, true, _now, _notify, default)).Error);
        Assert.False(await _db.GroupMembers.AnyAsync(m => m.UserId == _stranger));
    }

    [Fact]
    public async Task InvitesNeedPermissionAndAreChecked()
    {
        Assert.Equal("forbidden", (await GroupService.InviteAsync(_db, _member, _group, _stranger, _now, _notify, default)).Error);
        Assert.Equal("bad_target", (await GroupService.InviteAsync(_db, _moderator, _group, _moderator, _now, _notify, default)).Error);
        Assert.Equal("not_found", (await GroupService.InviteAsync(_db, _moderator, _group, Guid.NewGuid(), _now, _notify, default)).Error);
        Assert.Equal("already_member", (await GroupService.InviteAsync(_db, _moderator, _group, _member, _now, _notify, default)).Error);
        Assert.True((await GroupService.InviteAsync(_db, _moderator, _group, _stranger, _now, _notify, default)).Ok); Assert.Contains((_stranger, "group_invite"), _pushed);
        Assert.True((await GroupService.InviteAsync(_db, _admin, _group, _stranger, _now, _notify, default)).Ok); Assert.Equal(1, await _db.GroupInvites.CountAsync()); // the same person is invited once
        _db.GroupBans.Add(new GroupBan { Id = Guid.NewGuid(), GroupId = _group, UserId = _extra, BannedByUserId = _leader, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal("banned", (await GroupService.InviteAsync(_db, _moderator, _group, _extra, _now, _notify, default)).Error);
    }

    [Fact]
    public async Task AcceptingAnInviteGoesThroughTheSameChecks()
    {
        await GroupService.InviteAsync(_db, _moderator, _group, _stranger, _now, _notify, default); var invite = (await _db.GroupInvites.AsNoTracking().SingleAsync()).Id;
        Assert.Equal("not_found", (await GroupService.RespondToInviteAsync(_db, _extra, invite, true, _now, default)).Error); // not theirs
        Assert.True((await GroupService.RespondToInviteAsync(_db, _stranger, invite, true, _now, default)).Ok);
        var joined = await _db.GroupMembers.AsNoTracking().SingleAsync(m => m.UserId == _stranger); Assert.Equal("member", joined.Role); Assert.NotNull(joined.RoleId);
        Assert.Equal("not_found", (await GroupService.RespondToInviteAsync(_db, _stranger, invite, true, _now, default)).Error);
        // an invite from before a ban cannot be used after it
        await GroupService.InviteAsync(_db, _moderator, _group, _extra, _now, _notify, default); var second = (await _db.GroupInvites.AsNoTracking().SingleAsync(i => i.InvitedUserId == _extra)).Id;
        _db.GroupBans.Add(new GroupBan { Id = Guid.NewGuid(), GroupId = _group, UserId = _extra, BannedByUserId = _leader, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal("banned", (await GroupService.RespondToInviteAsync(_db, _extra, second, true, _now, default)).Error);
        Assert.False(await _db.GroupMembers.AnyAsync(m => m.UserId == _extra));
        var declined = await GroupService.InviteAsync(_db, _moderator, _group, User("outro"), _now, _notify, default); Assert.True(declined.Ok);
    }

    [Fact]
    public async Task AFullGroupAcceptsNobody()
    {
        var small = await Made("Pequeno", policy: "open", by: _extra, limit: 2); Assert.True((await Join(_stranger, small)).Ok);
        Assert.Equal("group_full", (await Join(User("terceiro"), small)).Error);
        await GroupService.InviteAsync(_db, _extra, small, _member, _now, _notify, default); var invite = (await _db.GroupInvites.AsNoTracking().SingleAsync(i => i.GroupId == small)).Id;
        Assert.Equal("group_full", (await GroupService.RespondToInviteAsync(_db, _member, invite, true, _now, default)).Error);
        Assert.Equal("pending", (await _db.GroupInvites.AsNoTracking().SingleAsync(i => i.Id == invite)).Status); // and the invite waits
    }

    [Fact]
    public async Task APersonBelongsToAtMostThirtyGroups()
    {
        for (var i = 0; i < GroupRules.MaxMemberships; i++) _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = Guid.NewGuid(), UserId = _stranger, Role = "member", JoinedAt = _now });
        await _db.SaveChangesAsync();
        await GroupService.UpdateProfileAsync(_db, _admin, _group, new(null, null, null, null, "open", null, null, null, null), _now, default);
        Assert.Equal("too_many_memberships", (await Join(_stranger)).Error);
    }

    // ---- moderation ---------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NobodyActsOnSomebodyWhoIsNotBelowThem()
    {
        Assert.Equal("outranked", (await GroupService.KickAsync(_db, _moderator, _group, _admin, null, _now, _notify, default)).Error);
        Assert.Equal("outranked", (await GroupService.KickAsync(_db, _moderator, _group, _leader, null, _now, _notify, default)).Error);
        Assert.Equal("outranked", (await GroupService.KickAsync(_db, _admin, _group, _leader, null, _now, _notify, default)).Error);
        Assert.Equal("bad_target", (await GroupService.KickAsync(_db, _moderator, _group, _moderator, null, _now, _notify, default)).Error);
        Assert.Equal("forbidden", (await GroupService.KickAsync(_db, _member, _group, _moderator, null, _now, _notify, default)).Error);
        Assert.Equal("forbidden", (await GroupService.KickAsync(_db, _stranger, _group, _member, null, _now, _notify, default)).Error);
        Assert.True((await GroupService.KickAsync(_db, _moderator, _group, _member, "spam", _now, _notify, default)).Ok);
        Assert.False(await _db.GroupMembers.AnyAsync(m => m.UserId == _member)); Assert.Contains((_member, "group_kicked"), _pushed); Assert.Contains("member_kicked", await Actions());
        Assert.True((await GroupService.KickAsync(_db, _admin, _group, _moderator, null, _now, _notify, default)).Ok);
        Assert.Equal("not_found", (await GroupService.KickAsync(_db, _leader, _group, _moderator, null, _now, _notify, default)).Error);
    }

    [Fact]
    public async Task BanningKeepsSomebodyOutUntilItEndsOrIsLifted()
    {
        Assert.Equal("forbidden", (await GroupService.BanAsync(_db, _moderator, _group, _member, null, null, _now, _notify, default)).Error); // moderators kick, they do not ban
        Assert.Equal("bad_duration", (await GroupService.BanAsync(_db, _admin, _group, _member, null, 0, _now, _notify, default)).Error);
        Assert.True((await GroupService.BanAsync(_db, _admin, _group, _member, "abuso", 1, _now, _notify, default)).Ok);
        Assert.False(await _db.GroupMembers.AnyAsync(m => m.UserId == _member)); Assert.Contains((_member, "group_banned"), _pushed);
        await GroupService.UpdateProfileAsync(_db, _admin, _group, new(null, null, null, null, "open", null, null, null, null), _now, default);
        Assert.Equal("banned", (await Join(_member)).Error);
        var list = Data<List<GroupService.BanItem>>(await GroupService.BansAsync(_db, _admin, _group, _now, default)); Assert.Equal("abuso", Assert.Single(list).Reason);
        Assert.Equal("forbidden", (await GroupService.BansAsync(_db, _moderator, _group, _now, default)).Error);
        _now = _now.AddDays(2); Assert.True((await Join(_member)).Ok); // the ban ran out
        await GroupService.BanAsync(_db, _admin, _group, _member, null, null, _now, _notify, default); Assert.Equal("banned", (await Join(_member)).Error);
        Assert.True((await GroupService.UnbanAsync(_db, _admin, _group, _member, _now, default)).Ok); Assert.True((await Join(_member)).Ok);
        Assert.Equal("not_found", (await GroupService.UnbanAsync(_db, _admin, _group, _member, _now, default)).Error);
    }

    [Fact]
    public async Task MutingHasALimitAndCanBeLifted()
    {
        Assert.Equal("bad_duration", (await GroupService.MuteAsync(_db, _moderator, _group, _member, 60 * 24 * 8, null, _now, default)).Error);
        Assert.True((await GroupService.MuteAsync(_db, _moderator, _group, _member, 30, "calma", _now, default)).Ok);
        Assert.Equal(_now.AddMinutes(30), (await _db.GroupMembers.AsNoTracking().SingleAsync(m => m.UserId == _member)).MutedUntil);
        var listed = Data<List<GroupService.MemberItem>>(await GroupService.MembersAsync(_db, _admin, _group, _now, default)); Assert.NotNull(listed.Single(m => m.Username == "membro").MutedUntil);
        Assert.True((await GroupService.MuteAsync(_db, _moderator, _group, _member, 0, null, _now, default)).Ok); Assert.Null((await _db.GroupMembers.AsNoTracking().SingleAsync(m => m.UserId == _member)).MutedUntil);
    }

    [Fact]
    public async Task ManyModerationActionsInAShortTimeAreStopped_ExceptForTheLeader()
    {
        var targets = Enumerable.Range(0, GroupRules.ModerationBurstLimit + 3).Select(i => User($"alvo{i}")).ToList(); foreach (var t in targets) Place(t, "member");
        var results = new List<string?>(); foreach (var t in targets) results.Add((await GroupService.MuteAsync(_db, _moderator, _group, t, 5, null, _now, default)).Error);
        Assert.Equal(GroupRules.ModerationBurstLimit, results.Count(r => r is null)); Assert.Equal(["rate_limited", "rate_limited", "rate_limited"], results.TakeLast(3));
        Assert.True((await GroupService.MuteAsync(_db, _leader, _group, targets[0], 5, null, _now, default)).Ok); // the leader is not held back
        _now = _now.AddMinutes(11); Assert.True((await GroupService.MuteAsync(_db, _moderator, _group, targets[^1], 5, null, _now, default)).Ok); // and the window passes
    }

    // ---- cargos ---------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AdministratorsMakeCargosBelowTheirOwn()
    {
        var made = await GroupService.CreateRoleAsync(_db, _admin, _group, new("Tesoureiro", 30, [GroupPermissions.Invite, GroupPermissions.Mute], "#E9C46A"), _now, default); Assert.True(made.Ok, made.Error);
        var role = await _db.GroupRoles.AsNoTracking().SingleAsync(r => r.Name == "Tesoureiro"); Assert.Equal(30, role.Rank); Assert.Equal("invite,mute", role.Permissions); Assert.Equal("#E9C46A", role.Color);
        Assert.Equal("outranked", (await GroupService.CreateRoleAsync(_db, _admin, _group, new("Co-líder", 80, [], null), _now, default)).Error); // not at their own rank
        Assert.Equal("bad_rank", (await GroupService.CreateRoleAsync(_db, _leader, _group, new("Acima", 100, [], null), _now, default)).Error); // nothing reaches the leader
        Assert.Equal("forbidden", (await GroupService.CreateRoleAsync(_db, _moderator, _group, new("Outro", 10, [], null), _now, default)).Error);
        Assert.Equal("name_taken", (await GroupService.CreateRoleAsync(_db, _admin, _group, new("tesoureiro", 5, [], null), _now, default)).Error);
        Assert.Equal("bad_permissions", (await GroupService.CreateRoleAsync(_db, _admin, _group, new("Voador", 5, ["fly"], null), _now, default)).Error);
        Assert.Equal("bad_name", (await GroupService.CreateRoleAsync(_db, _admin, _group, new("x", 5, [], null), _now, default)).Error);
        Assert.Contains("role_created", await Actions());
    }

    [Fact]
    public async Task NobodyClimbsByEditingCargos()
    {
        // the leader makes a cargo that can manage cargos and assign them, but cannot ban
        await GroupService.CreateRoleAsync(_db, _leader, _group, new("Gerente", 60, [GroupPermissions.ManageRoles, GroupPermissions.AssignRoles, GroupPermissions.Kick], null), _now, default);
        var manager = (await _db.GroupRoles.AsNoTracking().SingleAsync(r => r.Name == "Gerente")).Id; var boss = User("gerente"); Place(boss, "member");
        Assert.True((await GroupService.SetRoleAsync(_db, _leader, _group, boss, manager, _now, _notify, default)).Ok);
        // cannot give a permission the manager does not have, neither on a new cargo nor on an existing one
        Assert.Equal("cannot_grant", (await GroupService.CreateRoleAsync(_db, boss, _group, new("Banidor", 20, [GroupPermissions.Ban], null), _now, default)).Error);
        var mod = await RoleId("moderator");
        Assert.Equal("cannot_grant", (await GroupService.UpdateRoleAsync(_db, boss, _group, mod, new(null, null, [GroupPermissions.Kick, GroupPermissions.Ban], null), _now, default)).Error);
        // cannot raise a cargo to or above their own, nor touch their own or a higher one
        Assert.Equal("outranked", (await GroupService.UpdateRoleAsync(_db, boss, _group, manager, new(null, 70, null, null), _now, default)).Error);
        Assert.Equal("outranked", (await GroupService.UpdateRoleAsync(_db, boss, _group, await RoleId("admin"), new(null, null, [], null), _now, default)).Error);
        Assert.Equal("outranked", (await GroupService.UpdateRoleAsync(_db, boss, _group, await RoleId("owner"), new(null, null, [], null), _now, default)).Error);
        Assert.Equal("system_role", (await GroupService.DeleteRoleAsync(_db, boss, _group, await RoleId("admin"), _now, default)).Error); // the starting cargos are never deleted by anybody
        // cannot hand out a cargo at or above their own, nor to themselves, nor to somebody above them
        Assert.Equal("outranked", (await GroupService.SetRoleAsync(_db, boss, _group, _member, manager, _now, _notify, default)).Error);
        Assert.Equal("outranked", (await GroupService.SetRoleAsync(_db, boss, _group, _member, await RoleId("admin"), _now, _notify, default)).Error);
        Assert.Equal("bad_target", (await GroupService.SetRoleAsync(_db, boss, _group, boss, mod, _now, _notify, default)).Error);
        Assert.Equal("outranked", (await GroupService.SetRoleAsync(_db, boss, _group, _admin, mod, _now, _notify, default)).Error);
        Assert.Equal("outranked", (await GroupService.SetRoleAsync(_db, _admin, _group, _member, await RoleId("owner"), _now, _notify, default)).Error); // nobody becomes leader by a cargo
        // what they may do: give the moderator cargo (below them) to a member, and take a permission away from a cargo below them
        Assert.True((await GroupService.SetRoleAsync(_db, boss, _group, _member, mod, _now, _notify, default)).Ok);
        Assert.True((await GroupService.UpdateRoleAsync(_db, boss, _group, mod, new(null, null, [GroupPermissions.Kick], null), _now, default)).Ok);
        Assert.Equal("kick", (await _db.GroupRoles.AsNoTracking().SingleAsync(r => r.Id == mod)).Permissions);
    }

    [Fact]
    public async Task EditingAndDeletingCargos()
    {
        await GroupService.CreateRoleAsync(_db, _admin, _group, new("Tesoureiro", 30, [GroupPermissions.Invite], null), _now, default);
        var id = (await _db.GroupRoles.AsNoTracking().SingleAsync(r => r.Name == "Tesoureiro")).Id; Assert.True((await GroupService.SetRoleAsync(_db, _admin, _group, _member, id, _now, _notify, default)).Ok);
        Assert.Contains((_member, "group_role_changed"), _pushed);
        Assert.True((await GroupService.UpdateRoleAsync(_db, _admin, _group, id, new("Contador", 35, [GroupPermissions.Invite, GroupPermissions.Mute], "#00FF00"), _now, default)).Ok);
        var role = await _db.GroupRoles.AsNoTracking().SingleAsync(r => r.Id == id); Assert.Equal(("Contador", 35, "invite,mute", "#00FF00"), (role.Name, role.Rank, role.Permissions, role.Color));
        Assert.Equal("Contador", (await _db.GroupMembers.AsNoTracking().SingleAsync(m => m.UserId == _member)).Role); // members show the new name
        Assert.Equal("system_role", (await GroupService.UpdateRoleAsync(_db, _admin, _group, await RoleId("moderator"), new("Policial", null, null, null), _now, default)).Error);
        Assert.Equal("system_role", (await GroupService.UpdateRoleAsync(_db, _admin, _group, await RoleId("moderator"), new(null, 41, null, null), _now, default)).Error);
        Assert.Equal("system_role", (await GroupService.DeleteRoleAsync(_db, _admin, _group, await RoleId("member"), _now, default)).Error);
        Assert.True((await GroupService.DeleteRoleAsync(_db, _admin, _group, id, _now, default)).Ok);
        var after = await _db.GroupMembers.AsNoTracking().SingleAsync(m => m.UserId == _member); Assert.Equal(await RoleId("member"), after.RoleId); Assert.Equal("member", after.Role); // the people of a deleted cargo become plain members
        var listed = Data<List<GroupService.RoleItem>>(await GroupService.RolesAsync(_db, _admin, _group, _now, default)); Assert.Equal(["Líder", "Administrador", "Moderador", "Membro"], listed.Select(r => r.Name)); Assert.True(listed.Single(r => r.SystemKey == "moderator").CanEdit); Assert.False(listed.Single(r => r.SystemKey == "owner").CanEdit); Assert.False(listed.Single(r => r.SystemKey == "admin").CanEdit);
        Assert.Equal("forbidden", (await GroupService.RolesAsync(_db, _stranger, _group, _now, default)).Error);
    }

    [Fact]
    public async Task ThereIsALimitOnCargos()
    {
        for (var i = 0; i < GroupRules.MaxRoles - 4; i++) Assert.True((await GroupService.CreateRoleAsync(_db, _leader, _group, new($"Cargo {i:00}", 10, [], null), _now, default)).Ok);
        Assert.Equal("too_many_roles", (await GroupService.CreateRoleAsync(_db, _leader, _group, new("Mais um", 10, [], null), _now, default)).Error);
    }

    // ---- history ---------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryAdministrativeActionIsWrittenDown()
    {
        _now = _now.AddMinutes(1); await GroupService.InviteAsync(_db, _moderator, _group, _stranger, _now, _notify, default); _now = _now.AddMinutes(1);
        await GroupService.KickAsync(_db, _moderator, _group, _member, "x", _now, _notify, default); _now = _now.AddMinutes(1);
        await GroupService.CreateRoleAsync(_db, _admin, _group, new("Tesoureiro", 30, [], null), _now, default);
        Assert.Equal(["group_created", "invited", "member_kicked", "role_created"], await Actions());
        var audit = Data<List<GroupService.AuditItem>>(await GroupService.AuditAsync(_db, _admin, _group, null, 50, _now, default));
        Assert.Equal(["role_created", "member_kicked", "invited", "group_created"], audit.Select(a => a.Action)); Assert.Equal("admin", audit[0].Actor); Assert.Equal("membro", audit[1].Target); Assert.Equal("x", audit[1].Detail.GetProperty("reason").GetString());
        Assert.Equal(2, Data<List<GroupService.AuditItem>>(await GroupService.AuditAsync(_db, _admin, _group, audit[1].At, 50, _now, default)).Count); // paging by time
        Assert.Equal("forbidden", (await GroupService.AuditAsync(_db, _moderator, _group, null, 50, _now, default)).Error);
        Assert.Equal("forbidden", (await GroupService.AuditAsync(_db, _stranger, _group, null, 50, _now, default)).Error);
    }

    // ---- leaving, leadership and the end -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MembersLeaveButTheLeaderMustHandOverFirst()
    {
        Assert.True((await GroupService.LeaveAsync(_db, _member, _group, _now, default)).Ok); Assert.Contains("member_left", await Actions());
        Assert.Equal("not_found", (await GroupService.LeaveAsync(_db, _member, _group, _now, default)).Error);
        Assert.Equal("leader_cannot_leave", (await GroupService.LeaveAsync(_db, _leader, _group, _now, default)).Error);
    }

    [Fact]
    public async Task LeadershipPassesOnlyWithConfirmationToAMember()
    {
        Assert.Equal("forbidden", (await GroupService.TransferLeadershipAsync(_db, _admin, _group, _admin, "Os Exploradores", _now, _notify, default)).Error);
        Assert.Equal("confirmation_needed", (await GroupService.TransferLeadershipAsync(_db, _leader, _group, _admin, "os exploradores", _now, _notify, default)).Error); // the exact name
        Assert.Equal("bad_target", (await GroupService.TransferLeadershipAsync(_db, _leader, _group, _leader, "Os Exploradores", _now, _notify, default)).Error);
        Assert.Equal("not_found", (await GroupService.TransferLeadershipAsync(_db, _leader, _group, _stranger, "Os Exploradores", _now, _notify, default)).Error); // only a member
        Assert.True((await GroupService.TransferLeadershipAsync(_db, _leader, _group, _admin, "Os Exploradores", _now, _notify, default)).Ok);
        var members = await _db.GroupMembers.AsNoTracking().Where(m => m.GroupId == _group).ToDictionaryAsync(m => m.UserId, m => m.Role);
        Assert.Equal("owner", members[_admin]); Assert.Equal("admin", members[_leader]); Assert.Equal(_admin, (await _db.SocialGroups.AsNoTracking().SingleAsync()).OwnerUserId);
        Assert.Contains((_admin, "group_leader_changed"), _pushed); Assert.Contains("leadership_transferred", await Actions());
        Assert.Equal("outranked", (await GroupService.KickAsync(_db, _leader, _group, _admin, null, _now, _notify, default)).Error); // the old leader is just an administrator now
        Assert.Equal("forbidden", (await GroupService.DissolveAsync(_db, _leader, _group, "Os Exploradores", _now, _notify, default)).Error);
        Assert.True((await GroupService.LeaveAsync(_db, _leader, _group, _now, default)).Ok);
    }

    [Fact]
    public async Task DissolvingNeedsTheNameAndNoLand()
    {
        Assert.Equal("forbidden", (await GroupService.DissolveAsync(_db, _admin, _group, "Os Exploradores", _now, _notify, default)).Error);
        Assert.Equal("confirmation_needed", (await GroupService.DissolveAsync(_db, _leader, _group, "Os Exploradore", _now, _notify, default)).Error);
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = Guid.NewGuid(), OwnerUserId = _leader, GroupId = _group, Name = "Sede", BoundsJson = "{}", Area = 256, CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        var blocked = await GroupService.DissolveAsync(_db, _leader, _group, "Os Exploradores", _now, _notify, default);
        Assert.Equal("blocked", blocked.Error); Assert.Contains("owns_land", (List<string>)blocked.Data!); Assert.NotNull(await _db.SocialGroups.AsNoTracking().SingleAsync());
        (await _db.IslandParcels.SingleAsync()).DeletedAt = _now; await _db.SaveChangesAsync();
        await GroupService.InviteAsync(_db, _leader, _group, _stranger, _now, _notify, default); await Join(_extra);
        var permission = new IslandPermission { Id = Guid.NewGuid(), IslandId = Guid.NewGuid(), GroupId = _group, Permission = "build", PrincipalType = "group", GrantedByUserId = _leader }; _db.IslandPermissions.Add(permission); await _db.SaveChangesAsync();
        _pushed.Clear();
        Assert.True((await GroupService.DissolveAsync(_db, _leader, _group, "Os Exploradores", _now, _notify, default)).Ok);
        Assert.NotNull((await _db.SocialGroups.AsNoTracking().SingleAsync()).DeletedAt); Assert.Empty(_db.GroupMembers.AsNoTracking()); Assert.NotNull((await _db.IslandPermissions.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.All(_db.GroupInvites.AsNoTracking(), i => Assert.NotEqual("pending", i.Status)); Assert.All(_db.GroupJoinRequests.AsNoTracking(), r => Assert.NotEqual("pending", r.Status));
        Assert.Equal(3, _pushed.Count(p => p.Type == "group_dissolved")); Assert.DoesNotContain((_leader, "group_dissolved"), _pushed);
        Assert.Contains("group_dissolved", await Actions()); // the history stays
        Assert.Equal("not_found", (await GroupService.ViewAsync(_db, _leader, _group, _now, default)).Error);
        Assert.Empty(await GroupService.SearchAsync(_db, _stranger, null, 0, _now, default));
        await Made("Os Exploradores", by: _stranger); // the name is free again
    }

    // ---- groups from before cargos ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AGroupMadeBeforeCargosWorksAndGetsItsCargosOnTheFirstUse()
    {
        var old = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _leader, Name = "Antigo", NameKey = "antigo", Description = "", CreatedAt = _now, UpdatedAt = _now, Privacy = "private", JoinPolicy = "invite" };
        _db.SocialGroups.Add(old);
        _db.GroupMembers.AddRange(new GroupMember { Id = Guid.NewGuid(), GroupId = old.Id, UserId = _leader, Role = "owner", JoinedAt = _now }, new GroupMember { Id = Guid.NewGuid(), GroupId = old.Id, UserId = _admin, Role = "admin", JoinedAt = _now }, new GroupMember { Id = Guid.NewGuid(), GroupId = old.Id, UserId = _member, Role = "member", JoinedAt = _now });
        await _db.SaveChangesAsync();
        Assert.Empty(_db.GroupRoles.Where(r => r.GroupId == old.Id));
        Assert.True((await GroupService.InviteAsync(_db, _admin, old.Id, _stranger, _now, _notify, default)).Ok); // the administrator of the old group can invite
        Assert.Equal("forbidden", (await GroupService.InviteAsync(_db, _member, old.Id, _extra, _now, _notify, default)).Error);
        Assert.Equal(4, _db.GroupRoles.Count(r => r.GroupId == old.Id));
        Assert.Equal("outranked", (await GroupService.KickAsync(_db, _admin, old.Id, _leader, null, _now, _notify, default)).Error);
        Assert.True((await GroupService.KickAsync(_db, _leader, old.Id, _admin, null, _now, _notify, default)).Ok);
    }
}
