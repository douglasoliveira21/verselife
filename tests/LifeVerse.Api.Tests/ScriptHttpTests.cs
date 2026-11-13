using System.Net;
using System.Net.Sockets;
using System.Text;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Scripting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What stands between a script and the internet: which addresses are public, which requests are refused before anything is sent, the limits of the answer, redirects, a name that points inside (DNS rebinding), and the endpoint that only serves scripts the owner allowed.</summary>
public sealed class ScriptHttpTests : IDisposable
{
    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Seen.Add(request); return Task.FromResult(answer(request)); }
    }

    private static HttpResponseMessage Ok(string body, string type = "application/json") => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };
    private static HttpResponseMessage Redirect(string to, int code = 302) { var r = new HttpResponseMessage((HttpStatusCode)code); r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute); return r; }

    [Theory]
    [InlineData("8.8.8.8", true)] [InlineData("1.1.1.1", true)] [InlineData("93.184.216.34", true)] [InlineData("2606:4700:4700::1111", true)] [InlineData("172.15.0.1", true)] [InlineData("172.32.0.1", true)]
    [InlineData("127.0.0.1", false)] [InlineData("127.1.2.3", false)] [InlineData("10.1.2.3", false)] [InlineData("172.16.0.1", false)] [InlineData("172.31.255.255", false)] [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)] [InlineData("100.64.0.1", false)] [InlineData("0.0.0.0", false)] [InlineData("224.0.0.1", false)] [InlineData("255.255.255.255", false)] [InlineData("198.18.0.1", false)]
    [InlineData("::1", false)] [InlineData("::", false)] [InlineData("fe80::1", false)] [InlineData("fc00::1", false)] [InlineData("fd12:3456::1", false)] [InlineData("::ffff:10.0.0.1", false)] [InlineData("::ffff:127.0.0.1", false)] [InlineData("::ffff:169.254.169.254", false)] [InlineData("2001:db8::1", false)] [InlineData("ff02::1", false)]
    public void WhichAddressesArePublic(string address, bool expected) => Assert.Equal(expected, ScriptHttpClient.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://example.com/x", "only_https")] [InlineData("ftp://example.com/x", "only_https")] [InlineData("file:///etc/passwd", "only_https")] [InlineData("https://user:pass@example.com/", "no_credentials_in_url")]
    [InlineData("https://example.com:22/", "port_not_allowed")] [InlineData("https://example.com:8080/", "port_not_allowed")] [InlineData("https://localhost/", "private_address")] [InlineData("https://app.localhost/", "private_address")]
    [InlineData("https://10.0.0.5/", "private_address")] [InlineData("https://192.168.0.1/", "private_address")] [InlineData("https://[::1]/", "private_address")] [InlineData("https://169.254.169.254/latest/meta-data/", "private_address")]
    [InlineData("https://metadata/", "private_address")] [InlineData("https://db.internal/", "private_address")] [InlineData("https://printer.local/", "private_address")] [InlineData("https://127.1/", "private_address")]
    [InlineData("", "bad_url")] [InlineData("not a url", "bad_url")]
    public void RequestsRefusedBeforeAnythingIsSent(string url, string why) => Assert.Equal(why, ScriptHttpClient.Refuse(url, "GET", null, null));

    [Fact]
    public void MethodsHeadersAndBodiesAreChecked()
    {
        Assert.Null(ScriptHttpClient.Refuse("https://api.example.com/status", "GET", null, null)); Assert.Null(ScriptHttpClient.Refuse("https://api.example.com:8443/x", "post", new() { ["Content-Type"] = "application/json", ["Authorization"] = "Bearer x", ["X-Api-Key"] = "k" }, "{}"));
        Assert.Equal("bad_method", ScriptHttpClient.Refuse("https://api.example.com/", "DELETE", null, null)); Assert.Equal("bad_method", ScriptHttpClient.Refuse("https://api.example.com/", "CONNECT", null, null));
        Assert.Equal("body_too_big", ScriptHttpClient.Refuse("https://api.example.com/", "POST", null, new string('x', ScriptHttpClient.MaxRequestBytes + 1)));
        Assert.Equal("header_not_allowed", ScriptHttpClient.Refuse("https://api.example.com/", "GET", new() { ["Cookie"] = "a=b" }, null)); Assert.Equal("header_not_allowed", ScriptHttpClient.Refuse("https://api.example.com/", "GET", new() { ["Host"] = "internal" }, null));
        Assert.Equal("header_not_allowed", ScriptHttpClient.Refuse("https://api.example.com/", "GET", new() { ["X-Forwarded-For"] = "1", ["Proxy-Authorization"] = "x" }, null)); Assert.Equal("bad_header_value", ScriptHttpClient.Refuse("https://api.example.com/", "GET", new() { ["Accept"] = "a\r\nX-Evil: 1" }, null));
        Assert.Equal("content_type_not_allowed", ScriptHttpClient.Refuse("https://api.example.com/", "POST", new() { ["Content-Type"] = "application/octet-stream" }, "x")); Assert.Equal("too_many_headers", ScriptHttpClient.Refuse("https://api.example.com/", "GET", Enumerable.Range(0, 9).ToDictionary(i => $"X-H{i}", _ => "v"), null));
    }

    [Fact]
    public async Task AnAnswerIsReturnedSmall_WithItsStatusAndType()
    {
        var fake = new Fake(_ => Ok("{\"ok\":true}")); var client = new ScriptHttpClient(handler: fake);
        var result = await client.SendAsync(new ScriptHttpRequest("https://api.example.com/status?token=abc", "GET", new() { ["Authorization"] = "Bearer s3cret", ["X-Key"] = "k" }, null), default);
        Assert.Equal((200, "{\"ok\":true}", "application/json", false, null), (result.Status, result.Body, result.ContentType, result.Truncated, result.Error));
        var sent = Assert.Single(fake.Seen); Assert.Equal("LifeVerse-Script/1", sent.Headers.UserAgent.ToString()); Assert.Equal("Bearer s3cret", sent.Headers.Authorization!.ToString()); Assert.Equal("k", sent.Headers.GetValues("X-Key").Single()); Assert.Equal("https://api.example.com/status?token=abc", sent.RequestUri!.ToString());
        var big = await new ScriptHttpClient(handler: new Fake(_ => Ok(new string('a', 100_000), "text/plain"))).SendAsync(new ScriptHttpRequest("https://api.example.com/", "GET", null, null), default);
        Assert.True(big.Truncated); Assert.Equal(ScriptHttpClient.MaxResponseBytes, big.Body.Length);
        var binary = await new ScriptHttpClient(handler: new Fake(_ => Ok("MZ", "application/octet-stream"))).SendAsync(new ScriptHttpRequest("https://api.example.com/", "GET", null, null), default); Assert.Equal("content_type_not_allowed", binary.Error); Assert.Equal("", binary.Body);
        var refused = await client.SendAsync(new ScriptHttpRequest("http://api.example.com/", "GET", null, null), default); Assert.Equal("only_https", refused.Error); Assert.Single(fake.Seen);   // nothing was sent
    }

    [Fact]
    public async Task APostSendsItsBodyAndContentType()
    {
        var seen = ""; var fake = new Fake(request => { seen = request.Content!.ReadAsStringAsync().Result + "|" + request.Content.Headers.ContentType!.MediaType; return Ok("{}"); });
        await new ScriptHttpClient(handler: fake).SendAsync(new ScriptHttpRequest("https://api.example.com/x", "POST", new() { ["Content-Type"] = "text/plain; charset=utf-8" }, "olá"), default); Assert.Equal("olá|text/plain", seen);
    }

    [Fact]
    public async Task Redirects_AreFollowedByHandAndCheckedAtEveryStep()
    {
        var chain = new Fake(r => r.RequestUri!.AbsolutePath switch { "/a" => Redirect("/b"), "/b" => Redirect("https://other.example.com/c", 301), _ => Ok("fim") });
        var followed = await new ScriptHttpClient(handler: chain).SendAsync(new ScriptHttpRequest("https://api.example.com/a", "GET", null, null), default); Assert.Equal("fim", followed.Body); Assert.Equal(3, chain.Seen.Count);
        foreach (var target in new[] { "http://api.example.com/x", "https://169.254.169.254/latest/meta-data/", "https://localhost/admin", "https://10.0.0.1/", "https://[::1]:443/" })
        {
            var blocked = await new ScriptHttpClient(handler: new Fake(_ => Redirect(target))).SendAsync(new ScriptHttpRequest("https://api.example.com/", "GET", null, null), default);
            Assert.StartsWith("redirect_", blocked.Error);   // a public server cannot bounce a script to somewhere inside
        }
        var loop = await new ScriptHttpClient(handler: new Fake(_ => Redirect("https://api.example.com/again"))).SendAsync(new ScriptHttpRequest("https://api.example.com/", "GET", null, null), default); Assert.Equal("too_many_redirects", loop.Error);
        var post = new Fake(r => r.Method == HttpMethod.Post ? Redirect("https://api.example.com/done", 303) : Ok("get")); var after = await new ScriptHttpClient(handler: post).SendAsync(new ScriptHttpRequest("https://api.example.com/", "POST", null, "x"), default); Assert.Equal("get", after.Body); Assert.Equal(HttpMethod.Get, post.Seen.Last().Method);
    }

    [Fact]
    public async Task ANameThatPointsInside_OrChangesItsAnswer_NeverGetsConnected()
    {
        // the real connection path: a listener on this machine must never see a connection, whatever the name resolves to
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; var connected = false; _ = Task.Run(async () => { try { using var _ = await listener.AcceptTcpClientAsync(); connected = true; } catch (ObjectDisposedException) { } catch (SocketException) { } });
        foreach (var answers in new[] { new[] { IPAddress.Loopback }, [IPAddress.Parse("10.0.0.7")], [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("192.168.1.9")], [IPAddress.Parse("169.254.169.254")], [IPAddress.IPv6Loopback], [IPAddress.Parse("::ffff:127.0.0.1")], Array.Empty<IPAddress>() })
        {
            var client = new ScriptHttpClient(resolve: (_, _) => Task.FromResult(answers));
            var result = await client.SendAsync(new ScriptHttpRequest("https://innocent.example.com/", "GET", null, null), default);
            Assert.Contains(result.Error, new[] { "private_address", "connection_failed" }); Assert.NotEqual(200, result.Status);
        }
        await Task.Delay(200); Assert.False(connected, "the listener inside the machine received a connection"); listener.Stop();
        _ = port;
    }

    // ---- the endpoint ------------------------------------------------------------------------------------------------------

    private const string Key = "0123456789abcdef0123456789abcdef";
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IslandServer:SharedKey"] = Key }).Build();
    private readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner = Guid.NewGuid(), _object = Guid.NewGuid(), _script = Guid.NewGuid(), _foreignScript = Guid.NewGuid();
    private readonly ServerInstance _server;

    public ScriptHttpTests()
    {
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Praça", Slug = "praca", IsPublic = true, Size = 400 }; var other = new Island { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Outra", Slug = "outra", IsPublic = true, Size = 400 };
        _server = new ServerInstance { Id = Guid.NewGuid(), IslandId = island.Id, Hostname = "h", Port = 1, Status = "running", StartedAt = _now, LastHeartbeatAt = _now }; var foreign = Guid.NewGuid();
        _db.Islands.AddRange(island, other); _db.ServerInstances.Add(_server);
        _db.BuildObjects.AddRange(new BuildObject { Id = _object, IslandId = island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Painel", PrimitiveType = "cube" }, new BuildObject { Id = foreign, IslandId = other.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Alheio", PrimitiveType = "cube" });
        _db.ObjectScripts.AddRange(new ObjectScript { Id = _script, ObjectId = _object, OwnerUserId = _owner, Name = "painel", Source = "x", CreatedAt = _now, UpdatedAt = _now }, new ObjectScript { Id = _foreignScript, ObjectId = foreign, OwnerUserId = _owner, Name = "alheio", Source = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() { ScriptingEndpoints.Http = new ScriptHttpClient(); _db.Dispose(); }

    private static HttpContext Island(string key = Key) { var context = new DefaultHttpContext(); context.Request.Headers["X-LifeVerse-Service-Key"] = key; return context; }
    private static int Status(IResult result) => result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is IValueHttpResult ? 200 : 0;
    private Task<IResult> Call(Guid script, string url = "https://api.example.com/status?token=abc", string key = Key, TimeProvider? clock = null) => ScriptingEndpoints.ScriptHttpAsync(_server.Id, new ScriptHttpCall(script, url, "GET", null, null), Island(key), _config, _db, clock ?? new FixedClock(_now), default);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    [Fact]
    public async Task OnlyAScriptTheOwnerAllowed_OnItsOwnIsland_CanReachOut()
    {
        ScriptingEndpoints.Http = new ScriptHttpClient(handler: new Fake(_ => Ok("{\"temp\":21}")));
        Assert.Equal(401, Status(await Call(_script, key: "x"))); Assert.Equal(404, Status(await Call(Guid.NewGuid()))); Assert.Equal(404, Status(await Call(_foreignScript)));
        Assert.Equal(403, Status(await Call(_script)));                                                                              // not allowed yet
        _db.ScriptGrants.Add(new ScriptGrant { ScriptId = _script, SubjectId = Guid.Empty, Permission = "HTTP_REQUESTS", GrantedAt = _now }); _db.ScriptGrants.Add(new ScriptGrant { ScriptId = _script, SubjectId = Guid.NewGuid(), Permission = "HTTP_REQUESTS", GrantedAt = _now }); await _db.SaveChangesAsync();
        var done = await Call(_script); Assert.Equal(200, Status(done)); Assert.Contains("21", ((done as IValueHttpResult)!.Value as ScriptHttpResult)!.Body);
        var line = await _db.ScriptLogs.AsNoTracking().SingleAsync(); Assert.Equal("http", line.EventName); Assert.Contains("https://api.example.com/status → 200", line.Text); Assert.DoesNotContain("token", line.Text); Assert.DoesNotContain("abc", line.Text);   // the console has the address without its query: no secrets
        var private1 = await Call(_script, "https://10.0.0.1/admin"); Assert.Equal("private_address", ((private1 as IValueHttpResult)!.Value as ScriptHttpResult)!.Error);
        Assert.Contains("private_address", (await _db.ScriptLogs.AsNoTracking().OrderBy(l => l.CreatedAt).ToListAsync()).Last().Text);
    }

    [Fact]
    public async Task EachScriptIsLimitedToTenRequestsAMinute()
    {
        ScriptingEndpoints.Http = new ScriptHttpClient(handler: new Fake(_ => Ok("{}"))); _db.ScriptGrants.Add(new ScriptGrant { ScriptId = _script, SubjectId = Guid.Empty, Permission = "HTTP_REQUESTS", GrantedAt = _now }); await _db.SaveChangesAsync();
        var start = _now.AddHours(5); var codes = new List<int>();
        for (var i = 0; i < 12; i++) codes.Add(Status(await Call(_script, clock: new FixedClock(start.AddSeconds(i)))));
        Assert.Equal(10, codes.Count(c => c == 200)); Assert.Equal(2, codes.Count(c => c == 429));
        Assert.Equal(200, Status(await Call(_script, clock: new FixedClock(start.AddMinutes(2)))));                                // the window moves on
    }
}
