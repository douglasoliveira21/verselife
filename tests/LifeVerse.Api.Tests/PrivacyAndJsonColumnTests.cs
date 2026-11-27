using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using LifeVerse.Api.Modules.Groups;
using LifeVerse.Api.Modules.Identity;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Scripting;
using LifeVerse.Api.Modules.Social;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>
/// The jsonb columns as a real PostgreSQL treats them: it writes a document back with its own spacing ({"profile": "private"}), and it refuses text
/// operations on it without a cast. The privacy choices and every query that looks inside a document are proven here, against both databases.
/// </summary>
public sealed class PrivacyAndJsonColumnTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Clock Time = new(Now);
    private const string ServiceKey = "0123456789abcdef0123456789abcdef";

    private sealed class Scratch : IAsyncDisposable
    {
        private readonly string? _admin; private readonly string _name = "lifeverse_j_" + Guid.NewGuid().ToString("N")[..12];
        private Scratch(string? admin) => _admin = admin;

        public static Scratch InMemory() => new(null);

        public static async Task<Scratch> PostgresAsync()
        {
            var scratch = new Scratch(Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!);
            await using (var connection = new NpgsqlConnection(scratch._admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{scratch._name}\"", connection); await command.ExecuteNonQueryAsync(); }
            await using var db = scratch.Open(); await db.Database.MigrateAsync();
            return scratch;
        }

        public LifeVerseDbContext Open() => new(_admin is null
            ? new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(_name).Options
            : new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(_admin) { Database = _name }.ConnectionString).Options);

        public async ValueTask DisposeAsync()
        {
            if (_admin is null) return;
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_admin); await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
        }
    }

    private static Guid NewUser(LifeVerseDbContext db, string name, string privacy = "{}", long coins = 0)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", Bio = "Minha vida", PresenceStatus = "online", PrivacyJson = privacy, CreatedAt = Now, UpdatedAt = Now });
        db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = coins, CreatedAt = Now, UpdatedAt = Now });
        return id;
    }

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };

    private static (int Status, JsonElement Body) Read(IResult result)
    {
        var status = result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, value is null ? default : JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task PrivacyHolds(Scratch scratch, string hidden, string friendsOnly)
    {
        Guid viewer, friend, secret, choosy;
        await using (var db = scratch.Open())
        {
            viewer = NewUser(db, "vera"); friend = NewUser(db, "fabio"); secret = NewUser(db, "sigilosa", hidden); choosy = NewUser(db, "escolhe", friendsOnly);
            db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserId = friend, FriendUserId = choosy, Status = "accepted", CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        var hub = new NotificationHub(Time, NullLogger<NotificationHub>.Instance);
        async Task<int> Message(Guid from, Guid to) { await using var db = scratch.Open(); return Read(await SocialEndpoints.SendMessageAsync(new MessageRequest("oi", to, null, null), As(from), db, hub, Time, default)).Status; }

        await using (var db = scratch.Open())
        {
            var profile = Read(await UserEndpoints.PublicProfileAsync(secret, As(viewer), db, default)).Body;
            Assert.Equal(string.Empty, profile.GetProperty("bio").GetString()); Assert.Equal("offline", profile.GetProperty("presence").GetString());
            Assert.Equal("offline", Read(await SocialEndpoints.PresenceAsync(secret, As(viewer), db, default)).Body.GetProperty("status").GetString());
            var open = Read(await UserEndpoints.PublicProfileAsync(viewer, As(friend), db, default)).Body;
            Assert.Equal("Minha vida", open.GetProperty("bio").GetString()); Assert.Equal("online", open.GetProperty("presence").GetString());
            Assert.False(SearchIndexer.Discoverable(await db.Users.AsNoTracking().SingleAsync(u => u.Id == secret)));
            Assert.True(SearchIndexer.Discoverable(await db.Users.AsNoTracking().SingleAsync(u => u.Id == viewer)));
        }
        Assert.Equal(403, await Message(viewer, secret)); Assert.Equal(403, await Message(viewer, choosy)); Assert.Equal(200, await Message(friend, choosy)); Assert.Equal(200, await Message(secret, viewer));
    }

    [Fact]
    public async Task PrivacyIsHonouredHoweverTheDocumentIsSpaced()
    {
        await using var scratch = Scratch.InMemory(); // the spacing PostgreSQL writes back
        await PrivacyHolds(scratch, "{\"profile\": \"private\", \"presence\": \"private\", \"messages\": \"nobody\"}", "{\"messages\": \"friends\"}");
    }

    [Fact]
    public void ThePrivacyDocumentMustBeAJsonObjectAndAskingNotToBeFoundWorksWithAnySpacing()
    {
        Assert.True(ProfileService.IsPrivacyDocument("{}")); Assert.True(ProfileService.IsPrivacyDocument("{ \"presence\" : \"friends\" }"));
        foreach (var bad in new[] { "", "nada", "[1]", "\"x\"", "{\"a\":" }) Assert.False(ProfileService.IsPrivacyDocument(bad), bad);
        Assert.False(SearchIndexer.Discoverable(new User { Username = "a", NormalizedUsername = "A", Email = "a@t", NormalizedEmail = "A@T", PasswordHash = "x", PrivacyJson = "{\"searchable\": \"no\"}" }));
    }

    [PostgresFact]
    public async Task PrivacyIsHonouredAfterARoundTripThroughPostgres()
    {
        await using var scratch = await Scratch.PostgresAsync();
        await PrivacyHolds(scratch, "{\"profile\":\"private\",\"presence\":\"private\",\"messages\":\"nobody\"}", "{\"messages\":\"friends\"}");
    }

    private static async Task ChoicesAreEnforced(Scratch scratch)
    {
        Guid me, friend, stranger, group;
        await using (var db = scratch.Open())
        {
            me = NewUser(db, "eu", "{\"presence\":\"friends\",\"groups\":\"hidden\"}"); friend = NewUser(db, "amiga"); stranger = NewUser(db, "estranho", coins: 100000);
            db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserId = me, FriendUserId = friend, Status = "accepted", CreatedAt = Now });
            db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserId = friend, FriendUserId = me, Status = "accepted", CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        await using (var db = scratch.Open())
        {
            var made = await GroupService.CreateAsync(db, stranger, new GroupProfileRequest("Clube da Praia", "Um grupo para testes", null, "public", "invite", null, null, "PRAIA", 50), Now, default); Assert.True(made.Ok, made.Error);
            group = await db.SocialGroups.Select(g => g.Id).SingleAsync();
        }
        var hub = new NotificationHub(Time, NullLogger<NotificationHub>.Instance);
        async Task<PrivacySettings> Choose(PrivacyPatch patch) { await using var db = scratch.Open(); var answer = Read(await PrivacyChoices.PutAsync(patch, As(me), db, Time, default)); Assert.Equal(200, answer.Status); await using var fresh = scratch.Open(); return PrivacyChoices.Read((await fresh.Users.AsNoTracking().SingleAsync(u => u.Id == me)).PrivacyJson); }
        async Task<string> Seen(Guid by) { await using var db = scratch.Open(); var one = Read(await SocialEndpoints.PresenceAsync(me, As(by), db, default)).Body.GetProperty("status").GetString()!; Assert.Equal(one, Read(await UserEndpoints.PublicProfileAsync(me, As(by), db, default)).Body.GetProperty("presence").GetString()); var profile = JsonSerializer.SerializeToElement(await ProfileService.ViewAsync(db, by, me, Now, default)); Assert.Equal(one, profile.GetProperty("presence").GetString()); return one; }
        async Task<int> Message(Guid from) { await using var db = scratch.Open(); return Read(await SocialEndpoints.SendMessageAsync(new MessageRequest("oi", me, null, null), As(from), db, hub, Time, default)).Status; }
        async Task<string?> Invite() { await using var db = scratch.Open(); return (await GroupService.InviteAsync(db, stranger, group, me, Now, null, default)).Error; }
        async Task<int> Ask(Guid from) { await using var db = scratch.Open(); return Read(await SocialEndpoints.FriendRequestAsync(new FriendshipRequest(me), As(from), db, hub, Time, default)).Status; }

        // nothing chosen (the old default "presence": "friends" restricts nobody)
        await using (var db = scratch.Open()) Assert.Equal(new PrivacySettings("everyone", "everyone", "everyone", "everyone"), PrivacyChoices.Read((await db.Users.AsNoTracking().SingleAsync(u => u.Id == me)).PrivacyJson));
        Assert.Equal("online", await Seen(stranger));

        Assert.Equal(new PrivacySettings("friends", "friends", "friends", "nobody"), await Choose(new PrivacyPatch("friends", "friends", "friends", "nobody")));
        Assert.Equal("offline", await Seen(stranger)); Assert.Equal("online", await Seen(friend)); Assert.Equal("online", await Seen(me));
        Assert.Equal(403, await Message(stranger)); Assert.Equal(200, await Message(friend)); Assert.Equal("invites_closed", await Invite()); Assert.Equal(403, await Ask(stranger));

        await Choose(new PrivacyPatch(OnlineVisibility: "nobody", PrivateMessages: "nobody", GroupInvites: "nobody"));
        Assert.Equal("offline", await Seen(friend)); Assert.Equal("online", await Seen(me)); Assert.Equal(403, await Message(friend)); Assert.Equal("invites_closed", await Invite());

        var open = await Choose(new PrivacyPatch("everyone", "everyone", "everyone", "everyone"));
        Assert.Equal(new PrivacySettings("everyone", "everyone", "everyone", "everyone"), open);
        Assert.Equal("online", await Seen(stranger)); Assert.Equal(200, await Message(stranger)); Assert.Null(await Invite()); Assert.Equal(202, await Ask(stranger));
        await using (var db = scratch.Open()) Assert.Contains("groups", (await db.Users.AsNoTracking().SingleAsync(u => u.Id == me)).PrivacyJson); // what the choices do not name stays

        await using (var db = scratch.Open()) Assert.Equal(400, Read(await PrivacyChoices.PutAsync(new PrivacyPatch(FriendRequests: "friends"), As(me), db, Time, default)).Status);
    }

    [Fact]
    public async Task ThePrivacyChoicesAreEnforced()
    {
        await using var scratch = Scratch.InMemory();
        await ChoicesAreEnforced(scratch);
    }

    [PostgresFact]
    public async Task ThePrivacyChoicesAreEnforcedOnPostgres()
    {
        await using var scratch = await Scratch.PostgresAsync();
        await ChoicesAreEnforced(scratch);
    }

    private static async Task SessionsAndBlocksBelongToTheirOwner(Scratch scratch)
    {
        Guid me, other, phone = Guid.NewGuid(), pc = Guid.NewGuid(), old = Guid.NewGuid(), theirs = Guid.NewGuid(), current = Guid.NewGuid();
        await using (var db = scratch.Open())
        {
            me = NewUser(db, "dono"); other = NewUser(db, "vizinho");
            void Session(Guid id, Guid user, Guid family, string device, double hours, bool revoked = false, double lasts = 24) =>
                db.Sessions.Add(new Session { Id = id, UserId = user, FamilyId = family, RefreshTokenHash = id.ToString("N"), UserAgent = device, CreatedAt = Now.AddHours(hours), ExpiresAt = Now.AddHours(lasts), RevokedAt = revoked ? Now.AddHours(hours) : null });
            Session(Guid.NewGuid(), me, pc, "PC", -10, revoked: true); Session(current, me, pc, "PC", -1);
            Session(Guid.NewGuid(), me, phone, "Telefone", -3);
            Session(Guid.NewGuid(), me, old, "Antigo", -80, lasts: -1);
            Session(Guid.NewGuid(), other, theirs, "Deles", -2);
            db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = me, BlockedUserId = other, CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        HttpContext Mine() => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, me.ToString()), new Claim(JwtRegisteredClaimNames.Sid, current.ToString())], "test")) };
        async Task<List<JsonElement>> Open() { await using var db = scratch.Open(); return [.. Read(await AccountSessions.ListAsync(Mine(), db, Time, default)).Body.EnumerateArray()]; }

        var open = await Open(); // the expired one and the neighbour's are not there
        Assert.Equal([pc, phone], open.Select(s => s.GetProperty("id").GetGuid()));
        Assert.True(open[0].GetProperty("current").GetBoolean()); Assert.False(open[1].GetProperty("current").GetBoolean());
        Assert.Equal(Now.AddHours(-10), open[0].GetProperty("signedInAt").GetDateTimeOffset()); Assert.Equal(Now.AddHours(-1), open[0].GetProperty("lastSeenAt").GetDateTimeOffset());

        await using (var db = scratch.Open()) Assert.Equal(404, Read(await AccountSessions.EndOneAsync(theirs, Mine(), db, Time, default)).Status);
        await using (var db = scratch.Open()) Assert.Equal(204, Read(await AccountSessions.EndOneAsync(phone, Mine(), db, Time, default)).Status);
        Assert.Equal([pc], (await Open()).Select(s => s.GetProperty("id").GetGuid()));
        await using (var db = scratch.Open()) { db.Sessions.Add(new Session { Id = Guid.NewGuid(), UserId = me, FamilyId = Guid.NewGuid(), RefreshTokenHash = "outra", CreatedAt = Now, ExpiresAt = Now.AddHours(1) }); await db.SaveChangesAsync(); }
        await using (var db = scratch.Open()) Assert.Equal(2, Read(await AccountSessions.EndOthersAsync(Mine(), db, Time, default)).Body.GetProperty("ended").GetInt32()); // the new one and the expired one that was never closed
        Assert.Equal([pc], (await Open()).Select(s => s.GetProperty("id").GetGuid()));
        await using (var db = scratch.Open()) Assert.Null((await db.Sessions.AsNoTracking().SingleAsync(s => s.FamilyId == theirs)).RevokedAt);

        await using (var db = scratch.Open())
        {
            var blocked = Read(await SocialEndpoints.BlockedListAsync(Mine(), db, default)).Body.EnumerateArray().ToList();
            Assert.Equal(other, Assert.Single(blocked).GetProperty("userId").GetGuid()); Assert.Equal("vizinho", blocked[0].GetProperty("displayName").GetString());
            Assert.Empty(Read(await SocialEndpoints.BlockedListAsync(As(other), db, default)).Body.EnumerateArray()); // nobody learns who blocked them
        }
    }

    [Fact]
    public async Task SessionsAndTheBlockedListAreTheAccountsOwn()
    {
        await using var scratch = Scratch.InMemory();
        await SessionsAndBlocksBelongToTheirOwner(scratch);
    }

    [PostgresFact]
    public async Task SessionsAndTheBlockedListWorkOnPostgres()
    {
        await using var scratch = await Scratch.PostgresAsync();
        await SessionsAndBlocksBelongToTheirOwner(scratch);
    }

    [PostgresFact]
    public async Task AMomentSentWithAnOffsetIsAcceptedAndComparedByPostgres()
    {
        await using var scratch = await Scratch.PostgresAsync(); var brasilia = Now.ToOffset(TimeSpan.FromHours(-3)); Guid user;
        await using (var db = scratch.Open()) { user = NewUser(db, "fuso"); var added = db.Users.Local.Single(); added.CreatedAt = brasilia; await db.SaveChangesAsync(); }
        await using (var db = scratch.Open())
        {
            Assert.Equal(Now, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user)).CreatedAt);
            Assert.Equal(1, await db.Users.CountAsync(u => u.CreatedAt <= brasilia && u.CreatedAt >= brasilia.AddMinutes(-1)));
            Assert.Equal(1, await db.Users.Where(u => u.Id == user).ExecuteUpdateAsync(s => s.SetProperty(u => u.UpdatedAt, brasilia.AddHours(1))));
        }
    }

    [PostgresFact]
    public async Task EveryQueryThatLooksInsideADocumentRunsOnPostgres()
    {
        await using var scratch = await Scratch.PostgresAsync(); Guid owner, viewer, winner, island = Guid.NewGuid(), script = Guid.NewGuid(), ball = Guid.NewGuid(), instance = Guid.NewGuid();
        await using (var db = scratch.Open())
        {
            owner = NewUser(db, "dona", coins: 1000); viewer = NewUser(db, "vera"); winner = NewUser(db, "praiana"); NewUser(db, "praiano", "{\"profile\":\"private\"}");
            db.Islands.Add(new Island { Id = island, OwnerUserId = owner, Name = "Ilha", Slug = "ilha-json", IsPublic = true, MaxPlayers = 10, CreatedAt = Now, UpdatedAt = Now });
            db.ServerInstances.Add(new ServerInstance { Id = instance, IslandId = island, Hostname = "h", Port = 1, Status = "healthy", StartedAt = Now.AddHours(-1), LastHeartbeatAt = Now });
            db.BuildObjects.Add(new BuildObject { Id = ball, IslandId = island, CreatorUserId = owner, OwnerUserId = owner, Name = "Bola", PrimitiveType = "cube", Cost = 5, PhysicsJson = "{\"mode\":\"physical\"}", ContentJson = "{\"scripts\":[]}", CreatedAt = Now, UpdatedAt = Now });
            db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = island, CreatorUserId = owner, OwnerUserId = owner, Name = "Parede", PrimitiveType = "cube", Cost = 1, PhysicsJson = "{\"mode\":\"static\"}", CreatedAt = Now, UpdatedAt = Now });
            db.ObjectScripts.Add(new ObjectScript { Id = script, ObjectId = ball, OwnerUserId = owner, Name = "premio", Source = "x", CreatedAt = Now, UpdatedAt = Now });
            db.ScriptPayoutAuthorizations.Add(new ScriptPayoutAuthorization { Id = Guid.NewGuid(), ScriptId = script, OwnerUserId = owner, MaxPerPayment = 100, MaxPerDay = 150, ExpiresAt = Now.AddDays(1), CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        // the physics and performance tabs
        await using (var db = scratch.Open())
        {
            var physics = Read(await IslandPhysicsManagement.GetAsync(island, null, null, 1, As(owner), db, Time, default));
            Assert.Equal(200, physics.Status); Assert.Equal(1, physics.Body.GetProperty("total").GetInt32());
            var performance = Read(await IslandPerformanceManagement.GetAsync(island, 1, As(owner), db, Time, default));
            Assert.Equal(200, performance.Status); Assert.Contains("\"physical\":true", performance.Body.GetRawText());
            Assert.Equal(200, Read(await IslandPhysicsManagement.FreezeAsync(island, new FreezeRequest([], true), As(owner), db, Time, default)).Status);
        }
        await using (var db = scratch.Open()) Assert.NotNull((await db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == ball)).FrozenPhysicsJson);

        // the daily limit of a script is summed from the ledger of that script
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IslandServer:SharedKey"] = ServiceKey }).Build();
        async Task<int> Pay(string request) { await using var db = scratch.Open(); var http = new DefaultHttpContext(); http.Request.Headers["X-LifeVerse-Service-Key"] = ServiceKey; return Read(await ScriptingEndpoints.ScriptPayoutAsync(instance, new ScriptPayoutCall(script, winner, 100, request), http, config, db, Time, default)).Status; }
        Assert.Equal(200, await Pay("request-00000001")); Assert.Equal(409, await Pay("request-00000002"));

        // the inventory check and the search with people in it (a private one is not shown)
        await using (var db = scratch.Open()) Assert.NotNull(await InventoryIntegrity.CheckAsync(db, null, default));
        await using (var db = scratch.Open()) await SearchIndexer.RunAsync(db, Now.AddMinutes(1), default);
        await using (var db = scratch.Open())
        {
            var found = await GlobalSearch.SuggestAsync(db, viewer, "praia", 8, Now.AddMinutes(1), default);
            Assert.Contains(found, s => s.Text == "praiana"); Assert.DoesNotContain(found, s => s.Text == "praiano");
        }
    }
}
