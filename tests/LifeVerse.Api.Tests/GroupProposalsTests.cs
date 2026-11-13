using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Groups;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Groups: the group a person wears (with the title of their cargo) and the proposals the group votes on.</summary>
public sealed class GroupProposalsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly List<(Guid User, string Type)> _pushed = [];
    private readonly GroupNotifier _notify;
    private readonly Guid _leader, _admin, _moderator, _member, _other, _stranger, _group;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public GroupProposalsTests()
    {
        _notify = (user, type, _) => { _pushed.Add((user, type)); return Task.CompletedTask; };
        (_leader, _admin, _moderator, _member, _other, _stranger) = (User("lider"), User("admin"), User("moderador"), User("membro"), User("outro"), User("estranho"));
        var made = GroupService.CreateAsync(_db, _leader, new GroupProfileRequest("Construtores LV", "", null, null, null, null, null, null, null), _now, default).Result;
        _group = JsonSerializer.SerializeToElement(made.Data).GetProperty("id").GetGuid();
        Place(_admin, "admin"); Place(_moderator, "moderator"); Place(_member, "member"); Place(_other, "member");
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name) { var id = Guid.NewGuid(); _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id; }
    private void Place(Guid user, string key) { var role = _db.GroupRoles.Single(r => r.GroupId == _group && r.SystemKey == key); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = _group, UserId = user, Role = key, RoleId = role.Id, JoinedAt = _now }); _db.SaveChanges(); }
    private static JsonElement Json(GroupResult result) { Assert.True(result.Ok, result.Error); return JsonSerializer.SerializeToElement(result.Data, Web); }
    private static Guid Id(GroupResult result) => Json(result).GetProperty("id").GetGuid();

    private Task<GroupResult> Propose(Guid by, string title = "Pintar a praça", string[]? options = null, int days = 3, int? minRank = null, int? quorum = null, string? criterion = null) =>
        GroupService.CreateProposalAsync(_db, by, _group, new ProposalRequest(title, "Qual cor?", options ?? ["Azul", "Verde", "Amarelo"], _now.AddDays(days), minRank, quorum, criterion), _now, _notify, default);
    private Task<GroupResult> Vote(Guid who, Guid proposal, int option) => GroupService.VoteAsync(_db, who, _group, proposal, option, _now, _notify, default);
    private GroupProposal Stored(Guid id) => _db.GroupProposals.AsNoTracking().Single(p => p.Id == id);

    // ---- the group worn beside the name -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyAMemberWearsAGroup_OneAtATime_AndTheTitleIsTheCargoOrTheOneTheGroupWrote()
    {
        Assert.Null(await GroupService.ActiveOfAsync(_db, _member, default));
        Assert.Equal("forbidden", (await GroupService.ActivateAsync(_db, _stranger, _group, _now, default)).Error);
        Assert.Equal("not_found", (await GroupService.ActivateAsync(_db, _member, Guid.NewGuid(), _now, default)).Error);
        Assert.True((await GroupService.ActivateAsync(_db, _admin, _group, _now, default)).Ok);
        var worn = (await GroupService.ActiveOfAsync(_db, _admin, default))!; Assert.Equal(("Construtores LV", "Administrador"), (worn.GroupName, worn.Title));
        // a second group takes over
        var other = Id(await GroupService.CreateAsync(_db, _admin, new GroupProfileRequest("Os Exploradores", "", null, null, null, null, null, null, null), _now, default));
        Assert.True((await GroupService.ActivateAsync(_db, _admin, other, _now, default)).Ok);
        Assert.Equal("Os Exploradores", (await GroupService.ActiveOfAsync(_db, _admin, default))!.GroupName); Assert.Single(_db.GroupMembers.AsNoTracking().Where(m => m.UserId == _admin && m.IsActive).ToList());
        var cards = await GroupService.MineAsync(_db, _admin, _now, default); Assert.Equal(["Os Exploradores"], cards.Where(c => c.IsActive).Select(c => c.Name).ToArray()); Assert.Equal("Administrador", cards.Single(c => c.Name == "Construtores LV").Title);
        Assert.True((await GroupService.DeactivateAsync(_db, _admin, default)).Ok); Assert.Null(await GroupService.ActiveOfAsync(_db, _admin, default));
    }

    [Fact]
    public async Task TheAppearanceTheIslandShowsCarriesTheGroupWornAndTheCargoTitle_ChosenByTheServerOnly()
    {
        var avatar = new Avatar { Id = Guid.NewGuid(), UserId = _admin, DisplayName = "Admin", BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }; _db.Avatars.Add(avatar); await _db.SaveChangesAsync();
        string Label(string json) => JsonDocument.Parse(json).RootElement.GetProperty("groupLabel").GetString()!;
        Assert.Equal("", Label(await LifeVerse.Api.Modules.Users.AvatarAppearanceJson.BuildAsync(_db, avatar, default))); // wearing nothing: nothing under the name
        Assert.True((await GroupService.ActivateAsync(_db, _admin, _group, _now, default)).Ok);
        Assert.Equal("Construtores LV Administrador", Label(await LifeVerse.Api.Modules.Users.AvatarAppearanceJson.BuildAsync(_db, avatar, default))); // no tag: the name of the group
        _db.SocialGroups.Single(g => g.Id == _group).Tag = "CLV"; await _db.SaveChangesAsync();
        Assert.Equal("[CLV] Administrador", Label(await LifeVerse.Api.Modules.Users.AvatarAppearanceJson.BuildAsync(_db, avatar, default)));
        Assert.True((await GroupService.DeactivateAsync(_db, _admin, default)).Ok);
        Assert.Equal("", Label(await LifeVerse.Api.Modules.Users.AvatarAppearanceJson.BuildAsync(_db, avatar, default)));
    }

    [Fact]
    public async Task TheTitleOfACargoIsSetByWhoManagesRoles_AndStopsWhenThePersonLeaves()
    {
        var adminRole = _db.GroupRoles.AsNoTracking().Single(r => r.GroupId == _group && r.SystemKey == "admin"); var ownerRole = _db.GroupRoles.AsNoTracking().Single(r => r.GroupId == _group && r.SystemKey == "owner");
        Assert.True((await GroupService.ActivateAsync(_db, _admin, _group, _now, default)).Ok);
        Assert.Equal("forbidden", (await GroupService.SetRoleTitleAsync(_db, _member, _group, adminRole.Id, "Chefe", _now, default)).Error);
        Assert.Equal("bad_title", (await GroupService.SetRoleTitleAsync(_db, _leader, _group, adminRole.Id, new string('x', 25), _now, default)).Error);
        Assert.Equal("outranked", (await GroupService.SetRoleTitleAsync(_db, _admin, _group, ownerRole.Id, "Dono", _now, default)).Error); // an administrator does not retitle the leader
        Assert.True((await GroupService.SetRoleTitleAsync(_db, _leader, _group, adminRole.Id, "  Mestre de obras ", _now, default)).Ok);
        Assert.Equal("Mestre de obras", (await GroupService.ActiveOfAsync(_db, _admin, default))!.Title);
        Assert.Equal("Mestre de obras", (await GroupService.RolesAsync(_db, _leader, _group, _now, default)).Data is IEnumerable<GroupService.RoleItem> roles ? roles.Single(r => r.SystemKey == "admin").Title : "");
        Assert.Contains(_db.GroupAuditEntries.AsNoTracking().ToList(), e => e.Action == "role_title_changed");
        Assert.True((await GroupService.SetRoleTitleAsync(_db, _leader, _group, adminRole.Id, "", _now, default)).Ok); Assert.Equal("Administrador", (await GroupService.ActiveOfAsync(_db, _admin, default))!.Title); // empty: back to the name of the cargo
        Assert.True((await GroupService.LeaveAsync(_db, _admin, _group, _now, default)).Ok); Assert.Null(await GroupService.ActiveOfAsync(_db, _admin, default)); // leaving ends it
    }

    // ---- creating proposals ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyWhoHasThePermissionProposes_WithCheckedTextOptionsAndDeadline()
    {
        Assert.Equal("forbidden", (await Propose(_member)).Error); Assert.Equal("forbidden", (await Propose(_moderator)).Error); Assert.Equal("forbidden", (await Propose(_stranger)).Error);
        Assert.Equal("bad_title", (await Propose(_leader, "ab")).Error); Assert.Equal("bad_options", (await Propose(_leader, options: ["Só uma"])).Error); Assert.Equal("bad_options", (await Propose(_leader, options: ["Sim", "sim"])).Error);
        Assert.Equal("bad_options", (await Propose(_leader, options: [.. Enumerable.Range(0, 9).Select(i => $"O{i}")])).Error); Assert.Equal("bad_options", (await Propose(_leader, options: ["Sim", " "])).Error);
        Assert.Equal("bad_deadline", (await Propose(_leader, days: 31)).Error); Assert.Equal("bad_deadline", (await GroupService.CreateProposalAsync(_db, _leader, _group, new ProposalRequest("Prazo curto", "", ["A", "B"], _now.AddMinutes(1), null, null, null), _now, null, default)).Error);
        Assert.Equal("bad_quorum", (await Propose(_leader, quorum: 101)).Error); Assert.Equal("bad_criterion", (await Propose(_leader, criterion: "ditadura")).Error); Assert.Equal("bad_rank", (await Propose(_leader, minRank: 100)).Error);
        Assert.Empty(_db.GroupProposals);
        var made = await Propose(_admin); Assert.True(made.Ok, made.Error); Assert.Contains(_pushed, p => p.User == _member && p.Type == "group_proposal"); Assert.DoesNotContain(_pushed, p => p.User == _admin);
        Assert.Contains(_db.GroupNotifications.AsNoTracking().ToList(), n => n.UserId == _member && n.Type == "group_proposal");
        for (var i = 0; i < 9; i++) Assert.True((await Propose(_leader, $"Mais uma {i}")).Ok);
        Assert.Equal("too_many_proposals", (await Propose(_leader, "Demais")).Error);
    }

    // ---- voting ---------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OneVoteEach_OnlyMembersAndOnlyWhoIsEligible_AndTheSplitStaysHiddenWhileItRuns()
    {
        var id = Id(await Propose(_leader, minRank: 40)); // moderators and above
        Assert.Equal("forbidden", (await Vote(_stranger, id, 0)).Error); Assert.Equal("not_eligible", (await Vote(_member, id, 0)).Error);
        Assert.Equal("bad_option", (await Vote(_moderator, id, 3)).Error); Assert.Equal("bad_option", (await Vote(_moderator, id, -1)).Error); Assert.Equal("not_found", (await Vote(_moderator, Guid.NewGuid(), 0)).Error);
        Assert.True((await Vote(_moderator, id, 1)).Ok); Assert.Equal("already_voted", (await Vote(_moderator, id, 2)).Error); // no changing, no second vote
        var asMember = Json(await GroupService.ProposalAsync(_db, _member, _group, id, _now, default)); Assert.Equal((1, false, "open"), (asMember.GetProperty("votes").GetInt32(), asMember.GetProperty("canVote").GetBoolean(), asMember.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, asMember.GetProperty("counts").ValueKind); // members see only how many voted
        var asAdmin = Json(await GroupService.ProposalAsync(_db, _admin, _group, id, _now, default)); Assert.Equal(new[] { 0, 1, 0 }, asAdmin.GetProperty("counts").EnumerateArray().Select(c => c.GetInt32()).ToArray()); // managers see the split
        var mine = Json(await GroupService.ProposalAsync(_db, _moderator, _group, id, _now, default)); Assert.Equal((1, false), (mine.GetProperty("myVote").GetInt32(), mine.GetProperty("canVote").GetBoolean()));
        Assert.Equal(3, Json(await GroupService.ProposalAsync(_db, _admin, _group, id, _now, default)).GetProperty("eligible").GetInt32()); // admin, moderator and the leader
    }

    [Theory]
    [InlineData("majority", new[] { 0, 0, 1 }, "approved", 0)]      // 2 of 3 is more than half
    [InlineData("majority", new[] { 0, 1, 2 }, "rejected", -1)]     // 1 of 3 is not
    [InlineData("two_thirds", new[] { 0, 0, 1 }, "approved", 0)]    // exactly two thirds
    [InlineData("two_thirds", new[] { 0, 1, 0, 1, 2 }, "rejected", -1)]
    [InlineData("unanimous", new[] { 1, 1, 1 }, "approved", 1)]
    [InlineData("unanimous", new[] { 1, 1, 0 }, "rejected", -1)]
    [InlineData("plurality", new[] { 0, 1, 1, 2 }, "approved", 1)]  // the most voted wins even without a majority
    [InlineData("plurality", new[] { 0, 1 }, "rejected", -1)]       // a tie decides nothing
    public void TheCriterionDecidesWhoWins(string criterion, int[] votes, string status, int winner)
    {
        var (result, won, counts, total) = GroupService.Decide(["A", "B", "C"], votes, eligible: votes.Length, quorumPercent: 0, criterion);
        Assert.Equal((status, total), (result, votes.Length)); Assert.Equal(winner < 0 ? null : winner, won); Assert.Equal(3, counts.Length);
    }

    [Fact]
    public void WithoutQuorumOrVotesNothingIsDecided()
    {
        Assert.Equal("rejected", GroupService.Decide(["A", "B"], [0, 0], eligible: 10, quorumPercent: 50, "majority").Status); // 2 of 10 is under half
        Assert.Equal("approved", GroupService.Decide(["A", "B"], [0, 0, 0, 0, 0], eligible: 10, quorumPercent: 50, "majority").Status);
        Assert.Equal("rejected", GroupService.Decide(["A", "B"], [], eligible: 10, quorumPercent: 0, "majority").Status); // nobody voted
        Assert.Equal("approved", GroupService.Decide(["A", "B"], [1], eligible: 0, quorumPercent: 100, "majority").Status); // nobody could vote: no quorum to miss
    }

    // ---- closing --------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AProposalClosesAtTheDeadline_ByAManager_OrWhenEveryoneVoted_AndTheResultIsTold()
    {
        var byDeadline = Id(await Propose(_leader, days: 1, criterion: "plurality")); Assert.True((await Vote(_member, byDeadline, 2)).Ok); Assert.True((await Vote(_other, byDeadline, 2)).Ok); Assert.True((await Vote(_admin, byDeadline, 0)).Ok);
        _now = _now.AddDays(1).AddMinutes(1); _pushed.Clear();
        Assert.Equal(1, await GroupService.ProcessDueProposalsAsync(_db, _now, _notify, default)); var closed = Stored(byDeadline); Assert.Equal(("approved", 2), (closed.Status, closed.WinningOption)); Assert.Equal(5, closed.EligibleAtClose);
        Assert.Contains(_pushed, p => p.User == _member && p.Type == "group_proposal_result"); Assert.Contains(_db.GroupNotifications.AsNoTracking().ToList(), n => n.Type == "group_proposal_result" && n.Body.Contains("Amarelo"));
        Assert.Equal(0, await GroupService.ProcessDueProposalsAsync(_db, _now, _notify, default)); // once
        Assert.Equal("not_open", (await Vote(_moderator, byDeadline, 0)).Error);
        var afterwards = Json(await GroupService.ProposalAsync(_db, _member, _group, byDeadline, _now, default)); Assert.Equal(new[] { 1, 0, 2 }, afterwards.GetProperty("counts").EnumerateArray().Select(c => c.GetInt32()).ToArray()); // everybody sees the split once it is over

        var late = Id(await Propose(_leader, "Fora do prazo", days: 1)); _now = _now.AddDays(2);
        Assert.Equal("not_open", (await Vote(_member, late, 0)).Error); Assert.Equal("rejected", Stored(late).Status); // a vote after the deadline closes it (nobody voted)

        var manual = Id(await Propose(_leader, "Encerrar antes", days: 5)); Assert.True((await Vote(_member, manual, 1)).Ok);
        Assert.Equal("forbidden", (await GroupService.EndProposalAsync(_db, _member, _group, manual, false, _now, _notify, default)).Error);
        Assert.True((await GroupService.EndProposalAsync(_db, _admin, _group, manual, false, _now, _notify, default)).Ok); Assert.Equal(("approved", 1), (Stored(manual).Status, Stored(manual).WinningOption));
        Assert.Equal("not_open", (await GroupService.EndProposalAsync(_db, _admin, _group, manual, false, _now, _notify, default)).Error);

        var everyone = Id(await Propose(_leader, "Todos votam", days: 5, minRank: 80)); Assert.True((await Vote(_admin, everyone, 0)).Ok); Assert.Equal("open", Stored(everyone).Status); Assert.True((await Vote(_leader, everyone, 0)).Ok);
        Assert.Equal(("approved", 0), (Stored(everyone).Status, Stored(everyone).WinningOption)); // the last eligible vote closes it: nobody left to wait for
    }

    [Fact]
    public async Task ACancelledProposalDecidesNothing_AndTheListFiltersAndPages()
    {
        var a = Id(await Propose(_leader, "Primeira")); _now = _now.AddMinutes(1); var b = Id(await Propose(_leader, "Segunda")); _now = _now.AddMinutes(1); var c = Id(await Propose(_leader, "Terceira"));
        Assert.True((await GroupService.EndProposalAsync(_db, _leader, _group, a, true, _now, _notify, default)).Ok); Assert.Equal("cancelled", Stored(a).Status); Assert.Equal("not_open", (await Vote(_member, a, 0)).Error);
        var all = Json(await GroupService.ProposalsAsync(_db, _member, _group, null, 0, 20, _now, default)); Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "Terceira", "Segunda", "Primeira" }, all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray()); // open ones first, newest first
        Assert.Equal(2, Json(await GroupService.ProposalsAsync(_db, _member, _group, "open", 0, 20, _now, default)).GetProperty("total").GetInt32()); Assert.Equal(1, Json(await GroupService.ProposalsAsync(_db, _member, _group, "closed", 0, 20, _now, default)).GetProperty("total").GetInt32());
        Assert.Single(Json(await GroupService.ProposalsAsync(_db, _member, _group, null, 1, 1, _now, default)).GetProperty("items").EnumerateArray());
        Assert.Equal("forbidden", (await GroupService.ProposalsAsync(_db, _stranger, _group, null, 0, 20, _now, default)).Error); Assert.Equal("forbidden", (await GroupService.ProposalAsync(_db, _stranger, _group, c, _now, default)).Error);
        Assert.Contains(_db.GroupAuditEntries.AsNoTracking().ToList(), e => e.Action == "proposal_cancelled"); Assert.Contains(_db.GroupAuditEntries.AsNoTracking().ToList(), e => e.Action == "proposal_created");
    }

    [Fact]
    public void ThePermissionExistsForTheLeaderAndTheAdministratorOnly()
    {
        Assert.Contains(GroupPermissions.ManageProposals, GroupPermissions.Known); Assert.Equal(GroupPermissions.All.Count, GroupPermissions.Known.Count);
        var roles = GroupRules.StartingRoles.ToDictionary(r => r.Key, r => r.Permissions);
        Assert.Contains(GroupPermissions.ManageProposals, roles["owner"]); Assert.Contains(GroupPermissions.ManageProposals, roles["admin"]); Assert.DoesNotContain(GroupPermissions.ManageProposals, roles["moderator"]); Assert.Empty(roles["member"]);
    }
}
