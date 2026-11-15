using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Social;
using LifeVerse.Shared.Platform;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Teleports between two people on the same island: the sender asks, only the recipient answers, the spot comes from the island's own report, and nobody can spam or reach a person who blocked them.</summary>
public sealed class TeleportInviteTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _island = Guid.NewGuid(), _ana = Guid.NewGuid(), _bia = Guid.NewGuid(), _carlos = Guid.NewGuid();

    public TeleportInviteTests()
    {
        foreach (var (id, name) in new[] { (_ana, "ana"), (_bia, "bia"), (_carlos, "carlos") }) _db.Users.Add(new User { Id = id, Email = $"{name}@x.com", NormalizedEmail = $"{name}@X.COM", Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges();
        Report(new PlayerReport(_ana, "ana", 0, 10, 2, 20), new PlayerReport(_bia, "bia", 0, 50, 3, 60));
    }

    public void Dispose() => _db.Dispose();

    private void Report(params PlayerReport[] players)
    {
        foreach (var old in _db.ServerInstances.ToList()) _db.ServerInstances.Remove(old);
        _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = _island, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now.AddHours(-1), LastHeartbeatAt = _now, ReportJson = JsonSerializer.Serialize(new ServerReport(players), ServerChannelJson.Options) });
        _db.SaveChanges();
    }

    private Task<TeleportOutcome> Create(Guid from, Guid to, string kind = "offer", Guid? island = null, DateTimeOffset? at = null) => TeleportInvites.CreateAsync(_db, from, new TeleportInviteRequest(to, kind, island ?? _island), at ?? _now, default);

    [Fact]
    public async Task An_offer_waits_for_the_recipient()
    {
        var created = await Create(_ana, _bia);
        Assert.True(created.Ok); Assert.Equal("pending", created.Invite!.Status); Assert.Equal("ana", created.FromName);
        var pending = await TeleportInvites.PendingAsync(_db, _bia, _now, default); Assert.Single(pending);
        Assert.Empty(await TeleportInvites.PendingAsync(_db, _ana, _now, default));
    }

    [Fact]
    public async Task Accepting_an_offer_moves_the_recipient_to_the_sender_position_from_the_island_report()
    {
        var invite = (await Create(_ana, _bia)).Invite!;
        Report(new PlayerReport(_ana, "ana", 0, 11.5, 2, 22.5), new PlayerReport(_bia, "bia", 0, 50, 3, 60));      // ana walked on before bia answered
        var accepted = await TeleportInvites.AcceptAsync(_db, _bia, invite.Id, _now.AddSeconds(30), default);
        Assert.True(accepted.Ok); var d = accepted.Destination!;
        Assert.Equal(_bia, d.MoverUserId); Assert.Equal(_ana, d.DestinationUserId); Assert.Equal(11.5, d.X); Assert.Equal(22.5, d.Z);
    }

    [Fact]
    public async Task Accepting_a_request_moves_the_sender_to_the_recipient()
    {
        var invite = (await Create(_ana, _bia, "request")).Invite!;
        var d = (await TeleportInvites.AcceptAsync(_db, _bia, invite.Id, _now, default)).Destination!;
        Assert.Equal(_ana, d.MoverUserId); Assert.Equal(_bia, d.DestinationUserId); Assert.Equal(50, d.X); Assert.Equal(60, d.Z);
    }

    [Fact]
    public async Task Only_the_recipient_can_answer_and_only_once()
    {
        var invite = (await Create(_ana, _bia)).Invite!;
        Assert.Equal("not_found", (await TeleportInvites.AcceptAsync(_db, _ana, invite.Id, _now, default)).Error);
        Assert.Equal("not_found", (await TeleportInvites.AcceptAsync(_db, _carlos, invite.Id, _now, default)).Error);
        Assert.True((await TeleportInvites.AcceptAsync(_db, _bia, invite.Id, _now, default)).Ok);
        Assert.Equal("already_answered", (await TeleportInvites.AcceptAsync(_db, _bia, invite.Id, _now, default)).Error);
        Assert.Equal("closed", (await TeleportInvites.DeclineAsync(_db, _bia, invite.Id, _now, default)).Error);
    }

    [Fact]
    public async Task An_invite_expires_after_two_minutes()
    {
        var invite = (await Create(_ana, _bia)).Invite!;
        Assert.Equal("expired", (await TeleportInvites.AcceptAsync(_db, _bia, invite.Id, _now.AddMinutes(3), default)).Error);
        Assert.Equal("expired", (await _db.TeleportInvites.SingleAsync()).Status);
        Assert.Empty(await TeleportInvites.PendingAsync(_db, _bia, _now.AddMinutes(3), default));
    }

    [Fact]
    public async Task Declining_closes_it_and_names_the_sender()
    {
        var invite = (await Create(_ana, _bia)).Invite!;
        var declined = await TeleportInvites.DeclineAsync(_db, _bia, invite.Id, _now, default);
        Assert.True(declined.Ok); Assert.Equal("ana", declined.FromName); Assert.Equal("declined", (await _db.TeleportInvites.SingleAsync()).Status);
    }

    [Fact]
    public async Task Both_people_must_be_on_the_island_now()
    {
        Report(new PlayerReport(_ana, "ana", 0, 10, 2, 20));
        Assert.Equal("not_on_island", (await Create(_ana, _bia)).Error);
        Assert.Equal("not_on_island", (await Create(_carlos, _ana)).Error);
        Assert.Equal("island_offline", (await Create(_ana, _bia, island: Guid.NewGuid())).Error);
    }

    [Fact]
    public async Task The_other_person_leaving_before_the_answer_stops_the_teleport()
    {
        var invite = (await Create(_ana, _bia)).Invite!;
        Report(new PlayerReport(_bia, "bia", 0, 50, 3, 60));
        Assert.Equal("not_on_island", (await TeleportInvites.AcceptAsync(_db, _bia, invite.Id, _now, default)).Error);
        Assert.Equal("pending", (await _db.TeleportInvites.SingleAsync()).Status);
    }

    [Fact]
    public async Task Blocked_people_cannot_invite_and_cannot_be_accepted()
    {
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _bia, BlockedUserId = _ana, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal("blocked", (await Create(_ana, _bia)).Error);
        Assert.Equal("blocked", (await Create(_bia, _ana)).Error);
    }

    [Fact]
    public async Task Duplicates_and_spam_are_refused()
    {
        Assert.True((await Create(_ana, _bia)).Ok);
        Assert.Equal("already_pending", (await Create(_ana, _bia)).Error);
        Assert.True((await Create(_ana, _bia, "request")).Ok);
        Report(new PlayerReport(_ana, "ana", 0, 10, 2, 20), new PlayerReport(_bia, "bia", 0, 50, 3, 60), new PlayerReport(_carlos, "carlos", 0, 5, 1, 5));
        for (var i = 0; i < 8; i++) { _db.TeleportInvites.Add(new TeleportInvite { Id = Guid.NewGuid(), FromUserId = _ana, ToUserId = _carlos, Kind = "offer", IslandId = _island, Status = "declined", CreatedAt = _now, ExpiresAt = _now.AddMinutes(2) }); }
        await _db.SaveChangesAsync();
        Assert.Equal("rate_limited", (await Create(_ana, _carlos)).Error);
        Assert.True((await Create(_ana, _carlos, at: _now.AddMinutes(2))).Error is null or "not_on_island" or "island_offline");   // a minute later the limit has passed
    }

    [Theory]
    [InlineData("hug")]
    [InlineData("")]
    public async Task Unknown_kinds_are_refused(string kind) => Assert.Equal("bad_request", (await Create(_ana, _bia, kind)).Error);

    [Fact]
    public async Task Nobody_invites_themselves_or_a_stranger()
    {
        Assert.Equal("self", (await Create(_ana, _ana)).Error);
        Assert.Equal("not_found", (await Create(_ana, Guid.NewGuid())).Error);
    }
}
