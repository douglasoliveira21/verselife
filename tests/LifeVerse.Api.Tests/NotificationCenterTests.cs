using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The notification center: one list for group notices, invitations and what the real estate said, with counters, filters, paging and read marks.</summary>
public sealed class NotificationCenterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _me = Guid.NewGuid(), _other = Guid.NewGuid(), _boss = Guid.NewGuid();
    private readonly SocialGroup _builders;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web); // the way the API writes its answers (camelCase)

    public NotificationCenterTests()
    {
        _builders = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _boss, Name = "Construtores LV", NameKey = "construtores lv" };
        _db.SocialGroups.Add(_builders);
        foreach (var (id, name) in new[] { (_me, "marina"), (_other, "bruno"), (_boss, "chefe") })
            _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@t.local", NormalizedEmail = name.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = Now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void Group(Guid user, string title, int minutesAgo, bool read = false, string body = "") =>
        _db.GroupNotifications.Add(new GroupNotification { Id = Guid.NewGuid(), UserId = user, GroupId = _builders.Id, Type = "group_announcement", Title = title, Body = body, CreatedAt = Now.AddMinutes(-minutesAgo), ReadAt = read ? Now : null });
    private void Invite(Guid user, int minutesAgo, string status = "pending") => _db.GroupInvites.Add(new GroupInvite { Id = Guid.NewGuid(), GroupId = _builders.Id, InvitedUserId = user, InvitedByUserId = _boss, Status = status, CreatedAt = Now.AddMinutes(-minutesAgo) });
    private void Notice(Guid user, string title, int minutesAgo, bool read = false) =>
        _db.PropertyNotices.Add(new PropertyNotice { Id = Guid.NewGuid(), UserId = user, Kind = "sold", Title = title, DedupeKey = Guid.NewGuid().ToString(), CreatedAt = Now.AddMinutes(-minutesAgo), ReadAt = read ? Now : null });

    private async Task<JsonElement> List(Guid who, string? category = null, bool unread = false, string? q = null, int skip = 0, int take = 20)
    {
        _db.SaveChanges();
        var result = await NotificationCenter.ListAsync(_db, who, category, unread, q, skip, take, Now, default); Assert.True(result.Ok, result.Error);
        return JsonSerializer.SerializeToElement(result.Data, Web);
    }

    private static string[] Titles(JsonElement list) => list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray();

    [Fact]
    public async Task EverythingIsOneListNewestFirst_WithAnActionOnlyWhereThereIsAnAnswerToGive()
    {
        Group(_me, "Reunião sábado", 30); Invite(_me, 10); Notice(_me, "Sua ilha foi vendida", 20);
        var list = await List(_me);
        Assert.Equal(new[] { "Convite para o grupo", "Sua ilha foi vendida", "Reunião sábado" }, Titles(list));
        var invite = list.GetProperty("items")[0]; Assert.Equal(("invite", "convites", "Construtores LV"), (invite.GetProperty("source").GetString(), invite.GetProperty("category").GetString(), invite.GetProperty("groupName").GetString()));
        Assert.Equal(new[] { "accept", "decline" }, invite.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ToArray()); Assert.Contains("chefe", invite.GetProperty("body").GetString());
        Assert.Equal(0, list.GetProperty("items")[2].GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public async Task TheCountersAreTheUnreadOnesPerCategory_AndAnInviteCountsUntilItIsAnswered()
    {
        Group(_me, "A", 5); Group(_me, "B", 6, read: true); Invite(_me, 3); Invite(_me, 4, "accepted"); Notice(_me, "C", 7); Notice(_me, "D", 8, read: true); Notice(_me, "E", 9);
        var list = await List(_me); var counts = list.GetProperty("unreadByCategory");
        Assert.Equal((1, 1, 2, 4), (counts.GetProperty("grupos").GetInt32(), counts.GetProperty("convites").GetInt32(), counts.GetProperty("sistema").GetInt32(), list.GetProperty("unread").GetInt32()));
    }

    [Fact]
    public async Task FiltersByCategoryUnreadAndText()
    {
        Group(_me, "Reunião sábado", 5, body: "no Mirante"); Group(_me, "Festa", 6, read: true); Invite(_me, 3); Notice(_me, "Sua ilha foi vendida", 4); Notice(_me, "Aluguel venceu", 8, read: true);
        Assert.Equal(new[] { "Reunião sábado", "Festa" }, Titles(await List(_me, "grupos"))); Assert.Equal(new[] { "Convite para o grupo" }, Titles(await List(_me, "convites")));
        Assert.Equal(new[] { "Sua ilha foi vendida", "Aluguel venceu" }, Titles(await List(_me, "sistema")));
        Assert.Equal(new[] { "Convite para o grupo", "Sua ilha foi vendida", "Reunião sábado" }, Titles(await List(_me, unread: true)));
        Assert.Equal(new[] { "Reunião sábado" }, Titles(await List(_me, q: "mirante"))); Assert.Equal(new[] { "Convite para o grupo" }, Titles(await List(_me, q: "CHEFE")));
        Assert.Equal(6 - 1, (await List(_me, "todas")).GetProperty("items").GetArrayLength());
        _db.SaveChanges();
        Assert.Equal("bad_category", (await NotificationCenter.ListAsync(_db, _me, "outra", false, null, 0, 20, Now, default)).Error);
    }

    [Fact]
    public async Task APageIsCutAfterTheMerge_NotPerSource()
    {
        for (var i = 0; i < 6; i++) { Group(_me, $"G{i}", i * 3); Notice(_me, $"N{i}", i * 3 + 1); }
        var first = await List(_me, take: 5); var second = await List(_me, skip: 5, take: 5); var third = await List(_me, skip: 10, take: 5);
        Assert.Equal(new[] { "G0", "N0", "G1", "N1", "G2" }, Titles(first)); Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(new[] { "N2", "G3", "N3", "G4", "N4" }, Titles(second)); Assert.Equal(new[] { "G5", "N5" }, Titles(third)); Assert.False(third.GetProperty("hasMore").GetBoolean());
        Assert.Equal(50, (await List(_me, take: 5000)).GetProperty("take").GetInt32()); // a page never asks for more than the limit
    }

    [Fact]
    public async Task NobodyReadsAnotherPersonsNotifications()
    {
        Group(_other, "Segredo", 1); Invite(_other, 1); Notice(_other, "Privado", 1); Group(_me, "Meu", 1);
        Assert.Equal(new[] { "Meu" }, Titles(await List(_me))); Assert.Equal(1, (await List(_me)).GetProperty("unread").GetInt32());
        var marked = await NotificationCenter.MarkReadAsync(_db, _me, new CenterReadRequest(null, All: true), Now, default); Assert.True(marked.Ok);
        Assert.Equal(3, (await List(_other)).GetProperty("unread").GetInt32()); // what was marked was only mine
    }

    [Fact]
    public async Task MarkingAsReadWorksByName_ByCategory_AndForEverything_ButNeverAnswersAnInvite()
    {
        Group(_me, "A", 1); Group(_me, "B", 2); Notice(_me, "C", 3); Notice(_me, "D", 4); Invite(_me, 5); _db.SaveChanges();
        var first = _db.GroupNotifications.AsNoTracking().OrderByDescending(n => n.CreatedAt).First(n => n.UserId == _me);
        var one = await NotificationCenter.MarkReadAsync(_db, _me, new CenterReadRequest([new CenterRef("group", first.Id)]), Now, default);
        Assert.Equal(1, JsonSerializer.SerializeToElement(one.Data).GetProperty("marked").GetInt32());
        var system = await NotificationCenter.MarkReadAsync(_db, _me, new CenterReadRequest(null, All: true, Category: "sistema"), Now, default);
        Assert.Equal(2, JsonSerializer.SerializeToElement(system.Data).GetProperty("marked").GetInt32());
        var counts = (await List(_me)).GetProperty("unreadByCategory"); Assert.Equal((1, 1, 0), (counts.GetProperty("grupos").GetInt32(), counts.GetProperty("convites").GetInt32(), counts.GetProperty("sistema").GetInt32()));
        var all = await NotificationCenter.MarkReadAsync(_db, _me, new CenterReadRequest(null, All: true), Now, default); Assert.Equal(1, JsonSerializer.SerializeToElement(all.Data).GetProperty("marked").GetInt32());
        var after = await List(_me); Assert.Equal(1, after.GetProperty("unread").GetInt32()); // the invite is still waiting for an answer
        Assert.Equal("bad_category", (await NotificationCenter.MarkReadAsync(_db, _me, new CenterReadRequest(null, true, "x"), Now, default)).Error);
        Assert.Equal(0, JsonSerializer.SerializeToElement((await NotificationCenter.MarkReadAsync(_db, _me, new CenterReadRequest([new CenterRef("invite", Guid.NewGuid())]), Now, default)).Data).GetProperty("marked").GetInt32());
    }
}
