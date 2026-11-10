using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The controlled transfer between islands: validation, reservation, idempotency, the single-use ticket, arrival and release of the source.</summary>
public sealed class TeleportTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner = Guid.NewGuid(), _user = Guid.NewGuid(), _session = Guid.NewGuid();
    private readonly Island _origin, _public, _private, _small;

    public TeleportTests()
    {
        Island Make(string name, bool open, int max = 20, int size = 400) => new() { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = name, Slug = name.ToLowerInvariant(), IsPublic = open, Size = size, MaxPlayers = max };
        _origin = Make("Origem", true); _public = Make("Tropical", true); _private = Make("Reservada", false); _small = Make("Lotada", true, max: 2);
        _db.Islands.AddRange(_origin, _public, _private, _small); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Task<TeleportOutcome> Prepare(Island to, string key, Guid? user = null, float? x = null, float? z = null, DateTimeOffset? at = null) =>
        TeleportService.PrepareAsync(_db, user ?? _user, _session, new PrepareTeleportRequest(to.Id, _origin.Id, x, z, key), at ?? _now, default);

    [Fact]
    public async Task APublicIslandReservesAPlaceAndAskingAgainGivesTheSameTrip()
    {
        var first = await Prepare(_public, "viagem-1"); Assert.Equal((201, "prepared"), (first.Status, first.Code)); Assert.Equal(_now.AddSeconds(TeleportService.ReservationSeconds), first.Transfer!.ExpiresAt);
        var again = await Prepare(_public, "viagem-1"); Assert.Equal(200, again.Status); Assert.Equal(first.Transfer.Id, again.Transfer!.Id); Assert.Equal(1, await _db.TeleportTransfers.CountAsync());
    }

    [Fact]
    public async Task APrivateIslandIsRefusedUnlessThePersonHasAccess()
    {
        Assert.Equal((403, "island_private"), ((await Prepare(_private, "k1")).Status, (await Prepare(_private, "k1b")).Code));
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _private.Id, UserId = _user, Permission = "enter", PrincipalType = "user", GrantedByUserId = _owner }); await _db.SaveChangesAsync();
        Assert.Equal(201, (await Prepare(_private, "k2")).Status);
        Assert.Equal(201, (await Prepare(_private, "k3", user: _owner)).Status); // the owner always gets in
    }

    [Fact]
    public async Task AnUnknownOrTheSameIslandAndBadCoordinatesAreRefused()
    {
        Assert.Equal((404, "island_not_found"), ((await TeleportService.PrepareAsync(_db, _user, _session, new(Guid.NewGuid(), null, null, null, "k1"), _now, default)).Status, "island_not_found"));
        Assert.Equal("same_island", (await Prepare(_origin, "k2")).Code);
        Assert.Equal("invalid_destination", (await Prepare(_public, "k3", x: 9999, z: 0)).Code);
        Assert.Equal("invalid_destination", (await Prepare(_public, "k4", x: 10)).Code); // both or none
        Assert.Equal("invalid_destination", (await Prepare(_public, "k5", x: float.NaN, z: 0)).Code);
        Assert.Equal("invalid_key", (await Prepare(_public, "tem espaço")).Code);
        var fine = await Prepare(_public, "k6", x: 120, z: -80); Assert.Equal(201, fine.Status); Assert.Equal((120f, -80f), (fine.Transfer!.TargetX!.Value, fine.Transfer.TargetZ!.Value));
    }

    [Fact]
    public async Task APersonHasOneTripAtATimeAndTheReservationExpires()
    {
        var first = await Prepare(_public, "a"); var second = await Prepare(_public, "b");
        Assert.Equal((409, "transfer_in_progress"), (second.Status, second.Code)); Assert.Equal(first.Transfer!.Id, second.Transfer!.Id);
        var later = await Prepare(_public, "c", at: _now.AddSeconds(TeleportService.ReservationSeconds + 1)); // the first one ran out of time
        Assert.Equal(201, later.Status); Assert.Equal("expired", (await _db.TeleportTransfers.AsNoTracking().SingleAsync(t => t.Id == first.Transfer.Id)).Status);
        var stale = await Prepare(_public, "a", at: _now.AddSeconds(500)); Assert.Equal("expired", stale.Code); // the old key never revives
    }

    [Fact]
    public async Task ReservationsCountAgainstCapacityAndFreeUpWhenCancelled()
    {
        var one = await Prepare(_small, "u1", user: Guid.NewGuid()); var two = await Prepare(_small, "u2", user: Guid.NewGuid());
        Assert.Equal((201, 201), (one.Status, two.Status));
        var third = await Prepare(_small, "u3"); Assert.Equal((409, "island_full"), (third.Status, third.Code));
        Assert.Equal(200, (await TeleportService.CancelAsync(_db, one.Transfer!.UserId, one.Transfer.Id, _now, default)).Status);
        Assert.Equal(201, (await Prepare(_small, "u4")).Status);
        // people already on the island count too
        _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = _public.Id, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now, LastHeartbeatAt = _now, PlayerCount = 20 }); await _db.SaveChangesAsync();
        Assert.Equal("island_full", (await Prepare(_public, "full", user: Guid.NewGuid())).Code);
    }

    [Fact]
    public async Task CancellingIsIdempotentAndAnArrivedTripCannotBeCancelled()
    {
        var trip = (await Prepare(_public, "c1")).Transfer!;
        Assert.Equal("cancelled", (await TeleportService.CancelAsync(_db, _user, trip.Id, _now, default)).Code); Assert.Equal("cancelled", (await TeleportService.CancelAsync(_db, _user, trip.Id, _now, default)).Code);
        Assert.Equal(404, (await TeleportService.CancelAsync(_db, Guid.NewGuid(), trip.Id, _now, default)).Status); // nobody else's trip
        var next = (await Prepare(_public, "c2")).Transfer!; next.Status = "connecting"; next.TokenId = Guid.NewGuid(); next.TokenUsedAt = _now; await _db.SaveChangesAsync();
        Assert.Equal("arrived", (await TeleportService.ArrivedAsync(_db, _public.Id, next.Id, _now, default)).Code);
        Assert.Equal((409, "already_arrived"), ((await TeleportService.CancelAsync(_db, _user, next.Id, _now, default)).Status, (await TeleportService.CancelAsync(_db, _user, next.Id, _now, default)).Code));
    }

    [Fact]
    public async Task ArrivalNeedsTheTicketToHaveBeenUsedAndTheRightIslandAndIsIdempotent()
    {
        var trip = (await Prepare(_public, "a1")).Transfer!;
        Assert.Equal("prepared", (await TeleportService.ArrivedAsync(_db, _public.Id, trip.Id, _now, default)).Code); // nobody connected yet
        trip.Status = "connecting"; trip.TokenId = Guid.NewGuid(); await _db.SaveChangesAsync();
        Assert.Equal("token_not_used", (await TeleportService.ArrivedAsync(_db, _public.Id, trip.Id, _now, default)).Code);
        trip.TokenUsedAt = _now; await _db.SaveChangesAsync();
        Assert.Equal(404, (await TeleportService.ArrivedAsync(_db, _small.Id, trip.Id, _now, default)).Status); // another island cannot confirm
        Assert.Equal(200, (await TeleportService.ArrivedAsync(_db, _public.Id, trip.Id, _now, default)).Status); Assert.Equal(200, (await TeleportService.ArrivedAsync(_db, _public.Id, trip.Id, _now.AddSeconds(5), default)).Status);
        Assert.Equal(_now, (await _db.TeleportTransfers.AsNoTracking().SingleAsync()).ArrivedAt);
    }

    [Fact]
    public async Task OnlyAnArrivedTripReleasesTheSourceAndOnlyOnce()
    {
        var trip = (await Prepare(_public, "r1")).Transfer!;
        Assert.Empty(await TeleportService.ReleaseForSourceAsync(_db, _origin.Id, _now, default)); // not arrived: the person stays on the source
        trip.Status = "connecting"; trip.TokenUsedAt = _now; trip.TokenId = Guid.NewGuid(); await _db.SaveChangesAsync(); await TeleportService.ArrivedAsync(_db, _public.Id, trip.Id, _now, default);
        Assert.Empty(await TeleportService.ReleaseForSourceAsync(_db, _public.Id, _now, default)); // not the source
        var released = await TeleportService.ReleaseForSourceAsync(_db, _origin.Id, _now, default);
        Assert.Equal([new ReleasedPlayer(_user, _now.ToUnixTimeMilliseconds())], released);
        Assert.Empty(await TeleportService.ReleaseForSourceAsync(_db, _origin.Id, _now, default));
    }

    [Fact]
    public void TheTicketOfATripCarriesItsTransferId()
    {
        var options = new IslandEntryOptions { Issuer = "i", Audience = "a", SigningKey = new string('k', 40) }; var service = new IslandEntryTokenService(options, TimeProvider.System);
        var user = new User { Id = _user, Username = "u", NormalizedUsername = "U", Email = "e", NormalizedEmail = "E", PasswordHash = "x" }; var avatar = new Avatar { Id = Guid.NewGuid(), UserId = _user, DisplayName = "A", BaseModel = "default" };
        var trip = Guid.NewGuid(); var jti = Guid.NewGuid();
        var (token, identity) = service.Create(user, avatar, _public, _session, trip, jti);
        var read = service.Validate(token); Assert.NotNull(read); Assert.Equal(trip, read!.TransferId); Assert.Equal(jti, read.TokenId); Assert.Equal(identity.ExpiresAt, read.ExpiresAt, TimeSpan.FromSeconds(1));
        Assert.Null(service.Validate(token + "x")); Assert.Null(service.Create(user, avatar, _public, _session).Identity.TransferId);
    }
}

/// <summary>The parts that need a real PostgreSQL: the atomic single use of the ticket and the one-active-trip index under a race.</summary>
public sealed class TeleportPostgresTests
{
    private static string Admin => Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!;

    [PostgresFact]
    public async Task TheTicketOpensTheDestinationOnceEvenWhenUsedTwiceAtTheSameTimeAndRacingPreparesLeaveOneTrip()
    {
        var name = "lifeverse_t_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            var cs = new NpgsqlConnectionStringBuilder(Admin) { Database = name, Pooling = true, MaxPoolSize = 40 }.ConnectionString; var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options;
            await using (var migrate = new LifeVerseDbContext(options)) await migrate.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow; var owner = Guid.NewGuid(); var user = Guid.NewGuid(); var session = Guid.NewGuid(); var origin = Guid.NewGuid(); var destination = Guid.NewGuid();
            await using (var db = new LifeVerseDbContext(options))
            {
                db.Users.Add(new User { Id = owner, Username = "dono", NormalizedUsername = "DONO", Email = "d@t.local", NormalizedEmail = "D@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now });
                db.Users.Add(new User { Id = user, Username = "viajante", NormalizedUsername = "VIAJANTE", Email = "v@t.local", NormalizedEmail = "V@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now });
                foreach (var id in new[] { origin, destination }) db.Islands.Add(new Island { Id = id, OwnerUserId = owner, Name = "I" + id.ToString("N")[..6], Slug = "s" + id.ToString("N")[..8], IsPublic = true, Size = 400, MaxPlayers = 50 });
                await db.SaveChangesAsync();
            }
            // many requests with different keys at once: exactly one trip is kept
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
            {
                await using var db = new LifeVerseDbContext(options);
                return await TeleportService.PrepareAsync(db, user, session, new PrepareTeleportRequest(destination, origin, null, null, "race-" + i), DateTimeOffset.UtcNow, default);
            }));
            Assert.Equal(1, results.Count(r => r.Status == 201)); Assert.All(results.Where(r => r.Status != 201), r => Assert.Equal((409, "transfer_in_progress"), (r.Status, r.Code)));
            Guid transfer; Guid token = Guid.NewGuid();
            await using (var db = new LifeVerseDbContext(options)) { var t = await db.TeleportTransfers.SingleAsync(); transfer = t.Id; t.Status = "connecting"; t.TokenId = token; t.TokenExpiresAt = now.AddSeconds(45); await db.SaveChangesAsync(); }
            // the same ticket presented by several servers at once opens the door for one only
            var uses = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var db = new LifeVerseDbContext(options);
                return await TeleportService.ConsumeTokenAsync(db, transfer, user, destination, token, DateTimeOffset.UtcNow, default);
            }));
            Assert.Equal(1, uses.Count(u => u is not null));
            await using (var db = new LifeVerseDbContext(options))
            {
                Assert.Null(await TeleportService.ConsumeTokenAsync(db, transfer, Guid.NewGuid(), destination, token, DateTimeOffset.UtcNow, default)); // another person
                Assert.Null(await TeleportService.ConsumeTokenAsync(db, transfer, user, origin, token, DateTimeOffset.UtcNow, default)); // another island
                Assert.Null(await TeleportService.ConsumeTokenAsync(db, transfer, user, destination, Guid.NewGuid(), DateTimeOffset.UtcNow, default)); // another ticket
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync(); await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await drop.ExecuteNonQueryAsync();
        }
    }

    [PostgresFact]
    public async Task ANewTicketAfterARestartReplacesTheOldOneAndNothingOpensAfterACancellationOrTheEndOfTheReservation()
    {
        var name = "lifeverse_t_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(Admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            var cs = new NpgsqlConnectionStringBuilder(Admin) { Database = name, Pooling = true }.ConnectionString; var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options;
            await using (var migrate = new LifeVerseDbContext(options)) await migrate.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow; var user = Guid.NewGuid(); var destination = Guid.NewGuid();
            async Task<Guid> NewTrip(string status, DateTimeOffset expires, Guid? token)
            {
                await using var db = new LifeVerseDbContext(options); var id = Guid.NewGuid();
                db.TeleportTransfers.Add(new TeleportTransfer { Id = id, UserId = user, SessionId = Guid.NewGuid(), DestinationIslandId = destination, Status = status, IdempotencyKey = id.ToString("N"), CreatedAt = now, ExpiresAt = expires, TokenId = token, TokenExpiresAt = token is null ? null : now.AddSeconds(45) });
                await db.SaveChangesAsync(); return id;
            }
            async Task<bool> Opens(Guid trip, Guid token) { await using var db = new LifeVerseDbContext(options); return await TeleportService.ConsumeTokenAsync(db, trip, user, destination, token, DateTimeOffset.UtcNow, default) is not null; }
            async Task Close(Guid trip) { await using var db = new LifeVerseDbContext(options); var t = await db.TeleportTransfers.SingleAsync(x => x.Id == trip); t.Status = "cancelled"; await db.SaveChangesAsync(); }

            // the destination restarted while the person was connecting: a second ask gives a new ticket and the first one no longer works
            var first = Guid.NewGuid(); var second = Guid.NewGuid(); var trip = await NewTrip("connecting", now.AddSeconds(90), first);
            await using (var db = new LifeVerseDbContext(options)) { var t = await db.TeleportTransfers.SingleAsync(x => x.Id == trip); t.TokenId = second; await db.SaveChangesAsync(); }
            Assert.False(await Opens(trip, first)); Assert.True(await Opens(trip, second)); Assert.False(await Opens(trip, second)); // the new one opens once
            await Close(trip);

            var cancelled = await NewTrip("connecting", now.AddSeconds(90), Guid.NewGuid()); var cancelledToken = (await ReadToken(options, cancelled)); await Close(cancelled);
            Assert.False(await Opens(cancelled, cancelledToken)); // cancelled: the ticket is dead
            var late = Guid.NewGuid(); var lateTrip = await NewTrip("connecting", now.AddSeconds(-5), late);
            Assert.False(await Opens(lateTrip, late)); // the reservation ended
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(Admin); await connection.OpenAsync(); await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<Guid> ReadToken(DbContextOptions<LifeVerseDbContext> options, Guid trip) { await using var db = new LifeVerseDbContext(options); return (await db.TeleportTransfers.AsNoTracking().SingleAsync(x => x.Id == trip)).TokenId!.Value; }
}
