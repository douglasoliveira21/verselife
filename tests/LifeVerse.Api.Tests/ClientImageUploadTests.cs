using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The upload the game sends when a person imports an image (the same multipart it builds), run through the real upload endpoint: it must be accepted, land in the inventory as a texture, and a bad image must come back with a reason.</summary>
public sealed class ClientImageUploadTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
    private readonly Guid _user = Guid.NewGuid();

    public ClientImageUploadTests()
    {
        _db.Users.Add(new User { Id = _user, Username = "ana", NormalizedUsername = "ANA", Email = "ana@t.local", NormalizedEmail = "ANA@T.LOCAL", PasswordHash = "x", CreatedAt = DateTimeOffset.UtcNow }); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>The body the game builds in InventoryPanel.Upload (the file name kept to printable ASCII, the type always application/octet-stream).</summary>
    private static (byte[] Payload, string Boundary) ClientBody(string filename, byte[] bytes, string category)
    {
        var boundary = "----LifeVerse" + Guid.NewGuid().ToString("N");
        var header = new string(filename.Select(ch => ch is >= ' ' and <= '~' && ch is not '"' and not '\\' ? ch : '_').ToArray());
        var head = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=\"category\"\r\n\r\n{category}\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{header}\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
        return ([.. head, .. bytes, .. tail], boundary);
    }

    private async Task<(int Status, string Body)> Upload(string filename, byte[] bytes, string category = "textures")
    {
        var (payload, boundary) = ClientBody(filename, bytes, category);
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _user.ToString())], "test")) };
        context.Request.ContentType = $"multipart/form-data; boundary={boundary}"; context.Request.ContentLength = payload.Length; context.Request.Body = new MemoryStream(payload); context.Response.Body = new MemoryStream();
        var method = typeof(AssetEndpoints).GetMethod("UploadAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = await (Task<IResult>)method.Invoke(null, [context.Request, context, _db, _storage, TimeProvider.System, CancellationToken.None])!;
        var status = result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, value is null ? "" : System.Text.Json.JsonSerializer.Serialize(value));
    }

    [Theory]
    [InlineData("praia.png", "png")]
    [InlineData("Foto da Praia ção.png", "png")]
    [InlineData("foto.JPG", "jpg")]
    [InlineData("foto.jpeg", "jpg")]
    [InlineData("figura.webp", "webp")]
    public async Task AnImageImportedFromTheGameIsAcceptedAndBecomesATextureInTheInventory(string filename, string kind)
    {
        var bytes = kind switch { "png" => TestFiles.Png(64, 48), "jpg" => TestFiles.Jpeg(64, 48), _ => TestFiles.WebP("lossy", 64, 48) };
        var (status, body) = await Upload(filename, bytes);
        Assert.True(status is 200 or 201, $"status {status}: {body}");
        var item = await _db.InventoryItems.AsNoTracking().SingleAsync(); Assert.Equal(_user, item.OwnerUserId);
        var asset = await _db.Assets.AsNoTracking().SingleAsync(); Assert.Equal("textures", asset.Category);
        var version = await _db.AssetVersions.AsNoTracking().SingleAsync(); Assert.StartsWith("image/", version.MimeType);
    }

    [Fact]
    public async Task ABrokenImageComesBackWithAReasonTheGameCanShow()
    {
        var png = TestFiles.Png(16, 16); var cut = png[..(png.Length - 20)];
        var (status, body) = await Upload("quebrada.png", cut);
        Assert.Equal(400, status); Assert.Contains("\"file\"", body); Assert.Contains("PNG", body);
        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        var (fakeStatus, fakeBody) = await Upload("falsa.png", System.Text.Encoding.ASCII.GetBytes("isto nao e uma imagem"));
        Assert.Equal(400, fakeStatus); Assert.Contains("file", fakeBody);
    }
}
