using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class PeopleQueriesTests
{
    private static LifeVerseDbContext NewDb() => new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static User AddUser(LifeVerseDbContext db, string name, string status = "active", bool deleted = false)
    {
        var user = new User { Id = Guid.NewGuid(), Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@x.com", NormalizedEmail = (name + "@x.com").ToUpperInvariant(), PasswordHash = "x", Status = status, DeletedAt = deleted ? DateTimeOffset.UtcNow : null };
        db.Users.Add(user); db.SaveChanges(); return user;
    }

    private static void Link(LifeVerseDbContext db, Guid from, Guid to, string status)
    {
        db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserId = from, FriendUserId = to, Status = status, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
    }

    private static SocialGroup AddGroup(LifeVerseDbContext db, User owner, string name = "Clube")
    {
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = name, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.SocialGroups.Add(group);
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = owner.Id, Role = "owner", JoinedAt = DateTimeOffset.UtcNow });
        db.SaveChanges(); return group;
    }

    private static GroupInvite Invite(LifeVerseDbContext db, SocialGroup group, User from, User to)
    {
        var invite = new GroupInvite { Id = Guid.NewGuid(), GroupId = group.Id, InvitedUserId = to.Id, InvitedByUserId = from.Id, Status = "pending", CreatedAt = DateTimeOffset.UtcNow };
        db.GroupInvites.Add(invite); db.SaveChanges(); return invite;
    }

    [Fact]
    public async Task SearchFindsByPartOfTheNameIgnoringCaseAndPutsPrefixMatchesFirst()
    {
        using var db = NewDb(); var me = AddUser(db, "eu");
        AddUser(db, "MariaSilva"); AddUser(db, "JoaoMaria"); AddUser(db, "Pedro");
        var found = await PeopleQueries.SearchAsync(db, me.Id, "maria", default);
        Assert.Equal(["MariaSilva", "JoaoMaria"], found.Select(p => p.Username).ToArray());
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("a")] [InlineData("  ")]
    public async Task SearchNeedsAtLeastTwoCharacters(string? query)
    {
        using var db = NewDb(); var me = AddUser(db, "eu"); AddUser(db, "ana");
        Assert.Empty(await PeopleQueries.SearchAsync(db, me.Id, query, default));
    }

    [Fact]
    public async Task SearchHidesMeBlockedPeopleDeletedAndInactiveAccounts()
    {
        using var db = NewDb(); var me = AddUser(db, "luna_me");
        var blockedByMe = AddUser(db, "luna_a"); var blockedMe = AddUser(db, "luna_b");
        AddUser(db, "luna_c", deleted: true); AddUser(db, "luna_d", status: "suspended"); var visible = AddUser(db, "luna_e");
        db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = me.Id, BlockedUserId = blockedByMe.Id, CreatedAt = DateTimeOffset.UtcNow });
        db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = blockedMe.Id, BlockedUserId = me.Id, CreatedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        var found = await PeopleQueries.SearchAsync(db, me.Id, "luna", default);
        Assert.Equal(visible.Id, Assert.Single(found).Id);
    }

    [Fact]
    public async Task SearchTellsHowEachPersonIsRelatedToMe()
    {
        using var db = NewDb(); var me = AddUser(db, "me_user");
        var friend = AddUser(db, "zz_friend"); var sent = AddUser(db, "zz_sent"); var received = AddUser(db, "zz_received"); var stranger = AddUser(db, "zz_stranger");
        Link(db, me.Id, friend.Id, "accepted"); Link(db, friend.Id, me.Id, "accepted");
        Link(db, me.Id, sent.Id, "pending"); Link(db, received.Id, me.Id, "pending");
        var relation = (await PeopleQueries.SearchAsync(db, me.Id, "zz_", default)).ToDictionary(p => p.Username, p => p.Relation);
        Assert.Equal("friend", relation["zz_friend"]); Assert.Equal("pending_out", relation["zz_sent"]);
        Assert.Equal("pending_in", relation["zz_received"]); Assert.Equal("none", relation["zz_stranger"]);
    }

    [Fact]
    public async Task SearchTreatsWildcardCharactersAsPlainText()
    {
        using var db = NewDb(); var me = AddUser(db, "eu"); AddUser(db, "ana"); AddUser(db, "bia");
        Assert.Empty(await PeopleQueries.SearchAsync(db, me.Id, "%%", default));
        Assert.Empty(await PeopleQueries.SearchAsync(db, me.Id, new string('a', 65), default));
    }

    [Fact]
    public async Task FriendRequestsShowDirectionAndOnlyPendingOnes()
    {
        using var db = NewDb(); var me = AddUser(db, "eu"); var a = AddUser(db, "ana"); var b = AddUser(db, "bia"); var c = AddUser(db, "caio");
        Link(db, a.Id, me.Id, "pending"); Link(db, me.Id, b.Id, "pending"); Link(db, me.Id, c.Id, "accepted");
        var requests = await PeopleQueries.FriendRequestsAsync(db, me.Id, default);
        Assert.Equal(2, requests.Count);
        Assert.Equal("in", requests.Single(r => r.Username == "ana").Direction);
        Assert.Equal("out", requests.Single(r => r.Username == "bia").Direction);
    }

    [Fact]
    public async Task DecliningRemovesTheRequestInEitherDirectionAndOnlyWhilePending()
    {
        using var db = NewDb(); var me = AddUser(db, "eu"); var a = AddUser(db, "ana"); var b = AddUser(db, "bia");
        Link(db, a.Id, me.Id, "pending"); Link(db, me.Id, b.Id, "accepted");
        Assert.True(await PeopleQueries.DeclineFriendAsync(db, me.Id, a.Id, default));
        Assert.False(await PeopleQueries.DeclineFriendAsync(db, me.Id, a.Id, default));
        Assert.False(await PeopleQueries.DeclineFriendAsync(db, me.Id, b.Id, default));
        Assert.Equal(1, await db.Friendships.CountAsync());
    }

    [Fact]
    public async Task MyGroupsListsRoleAndMemberCount()
    {
        using var db = NewDb(); var me = AddUser(db, "eu"); var other = AddUser(db, "ana");
        var mine = AddGroup(db, me, "Meu clube"); var theirs = AddGroup(db, other, "Outro");
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = mine.Id, UserId = other.Id, Role = "member", JoinedAt = DateTimeOffset.UtcNow }); db.SaveChanges();
        var groups = await PeopleQueries.MyGroupsAsync(db, me.Id, default);
        var item = Assert.Single(groups);
        Assert.Equal(("Meu clube", "owner", 2), (item.Name, item.Role, item.Members));
        Assert.DoesNotContain(groups, g => g.Id == theirs.Id);
    }

    [Fact]
    public async Task AcceptingAnInviteMakesMeAMemberOnceAndClosesIt()
    {
        using var db = NewDb(); var owner = AddUser(db, "dono"); var me = AddUser(db, "eu"); var group = AddGroup(db, owner);
        var invite = Invite(db, group, owner, me);
        Assert.True(await PeopleQueries.AnswerInviteAsync(db, me.Id, invite.Id, true, Clock, default));
        Assert.Equal(1, await db.GroupMembers.CountAsync(m => m.GroupId == group.Id && m.UserId == me.Id && m.Role == "member"));
        Assert.Null(await PeopleQueries.AnswerInviteAsync(db, me.Id, invite.Id, true, Clock, default));
        Assert.Empty(await PeopleQueries.GroupInvitesAsync(db, me.Id, default));
    }

    [Fact]
    public async Task DecliningAnInviteDoesNotJoin_AndOthersCannotAnswerMine()
    {
        using var db = NewDb(); var owner = AddUser(db, "dono"); var me = AddUser(db, "eu"); var thief = AddUser(db, "intruso"); var group = AddGroup(db, owner);
        var invite = Invite(db, group, owner, me);
        Assert.Null(await PeopleQueries.AnswerInviteAsync(db, thief.Id, invite.Id, true, Clock, default));
        Assert.True(await PeopleQueries.AnswerInviteAsync(db, me.Id, invite.Id, false, Clock, default));
        Assert.False(await db.GroupMembers.AnyAsync(m => m.UserId == me.Id));
    }

    [Fact]
    public async Task InvitesToADeletedGroupAreHiddenAndDoNotJoin()
    {
        using var db = NewDb(); var owner = AddUser(db, "dono"); var me = AddUser(db, "eu"); var group = AddGroup(db, owner);
        var invite = Invite(db, group, owner, me);
        group.DeletedAt = DateTimeOffset.UtcNow; db.SaveChanges();
        Assert.Empty(await PeopleQueries.GroupInvitesAsync(db, me.Id, default));
        await PeopleQueries.AnswerInviteAsync(db, me.Id, invite.Id, true, Clock, default);
        Assert.False(await db.GroupMembers.AnyAsync(m => m.UserId == me.Id));
    }

    [Fact]
    public async Task LeavingAGroup_MembersCanButTheOwnerCannot()
    {
        using var db = NewDb(); var owner = AddUser(db, "dono"); var me = AddUser(db, "eu"); var group = AddGroup(db, owner);
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = me.Id, Role = "member", JoinedAt = DateTimeOffset.UtcNow }); db.SaveChanges();
        Assert.Equal(PeopleQueries.LeaveResult.OwnerCannotLeave, await PeopleQueries.LeaveGroupAsync(db, owner.Id, group.Id, default));
        Assert.Equal(PeopleQueries.LeaveResult.Left, await PeopleQueries.LeaveGroupAsync(db, me.Id, group.Id, default));
        Assert.Equal(PeopleQueries.LeaveResult.NotMember, await PeopleQueries.LeaveGroupAsync(db, me.Id, group.Id, default));
    }

    [Fact]
    public async Task SummaryCountsWhatNeedsMyAttention()
    {
        using var db = NewDb(); var owner = AddUser(db, "dono"); var me = AddUser(db, "eu"); var a = AddUser(db, "ana"); var group = AddGroup(db, owner);
        Link(db, a.Id, me.Id, "pending"); Link(db, me.Id, owner.Id, "pending"); Invite(db, group, owner, me);
        db.SocialMessages.Add(new SocialMessage { Id = Guid.NewGuid(), Channel = "private", SenderUserId = a.Id, RecipientUserId = me.Id, Content = "oi", CreatedAt = DateTimeOffset.UtcNow });
        db.SocialMessages.Add(new SocialMessage { Id = Guid.NewGuid(), Channel = "private", SenderUserId = a.Id, RecipientUserId = me.Id, Content = "lida", CreatedAt = DateTimeOffset.UtcNow, ReadAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        Assert.Equal(new SocialSummary(1, 1, 1), await PeopleQueries.SummaryAsync(db, me.Id, default));
    }
}
