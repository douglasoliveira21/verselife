using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The administration panel: only administrators get in, people can be found, blocked, unblocked and given a new password (sessions end, nothing secret is kept), and what they have can be seen.</summary>
[Collection("AdminEnvironment")]
public sealed class AdminPanelTests : IDisposable
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 17, 20, 0, 0, TimeSpan.Zero);
    private readonly Guid _admin = Guid.NewGuid(), _ana = Guid.NewGuid(), _bia = Guid.NewGuid();
    private readonly string? _oldIds = Environment.GetEnvironmentVariable("LIFEVERSE_ADMIN_USER_IDS"), _oldMfa = Environment.GetEnvironmentVariable("LIFEVERSE_ADMIN_MFA_TOKEN");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public AdminPanelTests()
    {
        Environment.SetEnvironmentVariable("LIFEVERSE_ADMIN_USER_IDS", _admin.ToString()); Environment.SetEnvironmentVariable("LIFEVERSE_ADMIN_MFA_TOKEN", "mfa-secreto-de-teste");
        foreach (var (id, name) in new[] { (_admin, "chefe"), (_ana, "ana"), (_bia, "bia") }) _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.l", NormalizedEmail = $"{name}@T.L".ToUpperInvariant(), PasswordHash = "x", CreatedAt = _now.AddDays(-10), UpdatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() { Environment.SetEnvironmentVariable("LIFEVERSE_ADMIN_USER_IDS", _oldIds); Environment.SetEnvironmentVariable("LIFEVERSE_ADMIN_MFA_TOKEN", _oldMfa); _db.Dispose(); }

    private HttpContext Ctx(Guid? user = null, string mfa = "mfa-secreto-de-teste") { var c = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, (user ?? _admin).ToString())], "test")) }; c.Request.Headers["X-Admin-MFA"] = mfa; return c; }
    private static int Status(IResult r) => r is ForbidHttpResult ? 403 : r is IStatusCodeHttpResult { StatusCode: { } code } ? code : r is IValueHttpResult ? 200 : 0;
    private static JsonElement Body(IResult r) => JsonSerializer.SerializeToElement((r as IValueHttpResult)!.Value, Web);
    private Session AddSession(Guid user, bool revoked = false) { var s = new Session { Id = Guid.NewGuid(), UserId = user, FamilyId = Guid.NewGuid(), RefreshTokenHash = Guid.NewGuid().ToString("N"), CreatedAt = _now.AddHours(-1), ExpiresAt = _now.AddDays(5), RevokedAt = revoked ? _now : null, IpAddress = "203.0.113.9", UserAgent = "teste" }; _db.Sessions.Add(s); _db.SaveChanges(); return s; }

    [Fact]
    public async Task OnlyAnAdministratorWithTheRightMfaGetsIn()
    {
        Assert.Equal(200, Status(await AdminPanel.OverviewAsync(Ctx(), _db, new FixedClock(_now), default)));
        Assert.Equal(403, Status(await AdminPanel.OverviewAsync(Ctx(_ana), _db, new FixedClock(_now), default)));                       // a player
        Assert.Equal(403, Status(await AdminPanel.OverviewAsync(Ctx(mfa: "errado"), _db, new FixedClock(_now), default)));               // an administrator with the wrong second factor
        Assert.Equal(403, Status(await AdminPanel.UsersAsync(null, null, null, null, null, Ctx(_ana), _db, default)));
    }

    [Fact]
    public async Task TheOverviewCountsPeopleSessionsAndTheDaysOfTheCharts()
    {
        _db.Users.Single(u => u.Id == _bia).Status = "suspended"; AddSession(_ana); AddSession(_ana, revoked: true); _db.SaveChanges();
        var o = Body(await AdminPanel.OverviewAsync(Ctx(), _db, new FixedClock(_now), default)).GetProperty("users");
        Assert.Equal((3, 2, 1, 1), (o.GetProperty("total").GetInt32(), o.GetProperty("active").GetInt32(), o.GetProperty("blocked").GetInt32(), o.GetProperty("activeSessions").GetInt32()));
        var charts = Body(await AdminPanel.OverviewAsync(Ctx(), _db, new FixedClock(_now), default)).GetProperty("charts");
        Assert.Equal(14, charts.GetProperty("signups").GetArrayLength()); Assert.Equal(2, charts.GetProperty("logins").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task PeopleAreFoundByNameOrEmailFilteredByStatusAndPaged()
    {
        _db.Users.Single(u => u.Id == _bia).Status = "suspended"; _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = _ana, AvailableCoins = 250, CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); AddSession(_ana);
        var all = JsonSerializer.SerializeToElement(await AdminPanelUsers(), Web); Assert.Equal(3, all.GetProperty("total").GetInt32());
        var found = JsonSerializer.SerializeToElement(await AdminPanelUsers(q: "AN"), Web); var ana = Assert.Single(found.GetProperty("items").EnumerateArray()); Assert.Equal(("ana", 250), (ana.GetProperty("username").GetString(), ana.GetProperty("balance").GetInt32())); Assert.NotEqual(JsonValueKind.Null, ana.GetProperty("lastLoginAt").ValueKind);
        Assert.Equal(1, JsonSerializer.SerializeToElement(await AdminPanelUsers(status: "suspended"), Web).GetProperty("total").GetInt32());
        Assert.Equal(1, JsonSerializer.SerializeToElement(await AdminPanelUsers(page: 3, size: 1), Web).GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("passwordHash", JsonSerializer.Serialize(all, Web), StringComparison.OrdinalIgnoreCase);
    }
    private Task<object> AdminPanelUsers(string? q = null, string? status = null, int? page = null, int? size = null) => AdminPanel.UsersDataAsync(_db, q, status, "name", page, size, default);

    [Fact]
    public async Task BlockingNeedsAReasonSignsThePersonOutAndCanBeUndone()
    {
        AddSession(_ana); AddSession(_ana);
        Assert.Equal(400, (await AdminPanel.SetStatusAsync(_db, _admin, _ana, "suspended", " ", _now, default)).Status);
        Assert.Equal(409, (await AdminPanel.SetStatusAsync(_db, _admin, _admin, "suspended", "teste", _now, default)).Status);       // nobody blocks themselves out
        Assert.Equal(404, (await AdminPanel.SetStatusAsync(_db, _admin, Guid.NewGuid(), "suspended", "teste", _now, default)).Status);
        Assert.Equal(200, (await AdminPanel.SetStatusAsync(_db, _admin, _ana, "suspended", "spam no chat", _now, default)).Status);
        Assert.Equal("suspended", _db.Users.Single(u => u.Id == _ana).Status); Assert.All(_db.Sessions.Where(s => s.UserId == _ana), s => Assert.NotNull(s.RevokedAt));
        Assert.Equal("user_block", _db.AdminAudits.Single().Action);
        Assert.Equal(200, (await AdminPanel.SetStatusAsync(_db, _admin, _ana, "active", "engano", _now, default)).Status); Assert.Equal("active", _db.Users.Single(u => u.Id == _ana).Status);
        await AdminPanel.SetStatusAsync(_db, _admin, _bia, "banned", "fraude", _now, default); Assert.Equal("banned", _db.Users.Single(u => u.Id == _bia).Status);
    }

    [Fact]
    public async Task ANewPasswordIsCheckedHashedEndsTheSessionsAndIsNeverLogged()
    {
        AddSession(_ana);
        Assert.Equal(400, (await AdminPanel.SetPasswordAsync(_db, _admin, _ana, "curta", "pedido da pessoa", _now, default)).Status);      // too weak
        Assert.Equal(400, (await AdminPanel.SetPasswordAsync(_db, _admin, _ana, "Senha12", " ", _now, default)).Status);                   // no reason
        var result = await AdminPanel.SetPasswordAsync(_db, _admin, _ana, "Senha12", "a pessoa esqueceu a senha", _now, default); Assert.Equal(200, result.Status);
        var user = _db.Users.Single(u => u.Id == _ana);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, "Senha12")); Assert.NotEqual("Senha12", user.PasswordHash);
        Assert.All(_db.Sessions.Where(s => s.UserId == _ana), s => Assert.NotNull(s.RevokedAt));
        var audit = Assert.Single(_db.AdminAudits); Assert.Equal("user_password_reset", audit.Action); Assert.DoesNotContain("Senha12", audit.MetadataJson); Assert.DoesNotContain(user.PasswordHash, audit.MetadataJson);
    }

    [Fact]
    public async Task TheInventoryTheFriendsAndTheGroupsOfAPersonAreListed()
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = _ana, CreatorUserId = _ana, Name = "Cadeira", Category = "objects", CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _ana, AssetId = asset.Id, DisplayName = "Cadeira azul", CreatedAt = _now });
        _db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserId = _ana, FriendUserId = _bia, Status = "accepted", CreatedAt = _now, UpdatedAt = _now }); _db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserId = _admin, FriendUserId = _ana, Status = "pending", CreatedAt = _now, UpdatedAt = _now });
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana, Name = "Construtores", CreatedAt = _now, UpdatedAt = _now }; _db.SocialGroups.Add(group); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = _ana, Role = "owner", JoinedAt = _now }); _db.SaveChanges();
        var inventory = Body(await AdminPanel.InventoryAsync(_ana, null, null, null, Ctx(), _db, default)); Assert.Equal(1, inventory.GetProperty("total").GetInt32()); Assert.Equal("Cadeira azul", inventory.GetProperty("items")[0].GetProperty("displayName").GetString());
        var friends = Body(await AdminPanel.FriendsAsync(_ana, Ctx(), _db, default)); Assert.Equal("bia", friends.GetProperty("friends")[0].GetProperty("username").GetString()); Assert.Equal("received", friends.GetProperty("pending")[0].GetProperty("direction").GetString());
        var groups = Body(await AdminPanel.UserGroupsAsync(_ana, Ctx(), _db, default)); Assert.Equal(("Construtores", "owner"), (groups[0].GetProperty("name").GetString(), groups[0].GetProperty("role").GetString()));
        var list = Body(await AdminPanel.GroupsAsync("constr", null, null, Ctx(), _db, default)); Assert.Equal(1, list.GetProperty("items")[0].GetProperty("members").GetInt32());
        Assert.Equal(403, Status(await AdminPanel.InventoryAsync(_ana, null, null, null, Ctx(_bia), _db, default)));
    }

    [Fact]
    public async Task TheLogsShowTheActionsOfTheAdministratorsTheLoginsAndTheMoney()
    {
        await AdminPanel.SetStatusAsync(_db, _admin, _ana, "suspended", "teste", _now, default); AddSession(_ana);
        _db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = "k", Type = "user_transfer", Status = "completed", Amount = 40, InitiatorUserId = _ana, CounterpartyUserId = _bia, CreatedAt = _now }); _db.SaveChanges();
        var admin = Body(await AdminPanel.LogsAsync(null, null, null, null, null, null, Ctx(), _db, new FixedClock(_now), default)); Assert.Equal(("user_block", "chefe"), (admin.GetProperty("items")[0].GetProperty("action").GetString(), admin.GetProperty("items")[0].GetProperty("admin").GetString()));
        var logins = Body(await AdminPanel.LogsAsync("logins", _ana, null, null, null, null, Ctx(), _db, new FixedClock(_now), default)); Assert.Equal("203.0.113.9", logins.GetProperty("items")[0].GetProperty("ipAddress").GetString());
        var money = Body(await AdminPanel.LogsAsync("economy", null, null, 7, null, null, Ctx(), _db, new FixedClock(_now), default)); Assert.Equal(("ana", "bia", 40), (money.GetProperty("items")[0].GetProperty("from").GetString(), money.GetProperty("items")[0].GetProperty("to").GetString(), money.GetProperty("items")[0].GetProperty("amount").GetInt32()));
    }
}

public sealed class AdminPanelPageTests
{
    private static string Html => AdminPanelPage.Html;

    [Fact]
    public void ThePageNeverPutsServerDataInAsHtml() { Assert.DoesNotContain("innerHTML", Html); Assert.DoesNotContain("outerHTML", Html); Assert.DoesNotContain("document.write", Html); Assert.DoesNotContain("eval(", Html); Assert.DoesNotContain("insertAdjacentHTML", Html); }

    [Fact]
    public void ThePageHoldsNoSecretAndSendsBothTokens() { Assert.Contains("X-Admin-MFA", Html); Assert.Contains("type=\"password\"", Html); Assert.DoesNotContain("localStorage", Html); Assert.DoesNotContain("sessionStorage", Html); }

    [Fact]
    public void EveryChangeAsksForAReason() { foreach (var path in new[] { "/block", "/unblock", "/password", "/revoke-sessions" }) Assert.Contains(path, Html); Assert.Contains("Motivo (obrigatório)", Html); }
}
