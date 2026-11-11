using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Media on a surface (a picture or a video from the web): only public https addresses of the right kind are accepted, faces included, and media costs budget.</summary>
public sealed class ObjectMediaTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly Guid _instance = Guid.NewGuid(); private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid();

    public ObjectMediaTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = island.Id, Hostname = "h", Status = "healthy" });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = _builder, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private async Task<(int Status, JsonElement Body)> Create(string material)
    {
        var request = new BuildObjectRequest("cube", 0, 1, 0, 0, 0, 0, 1, 1, 1, material, "{\"collision\":true}", null, 0, "{}", "Tela", null, null, null, null);
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(_builder, "create", null, request, 0, null, null, 0, 0, 0, null, null, null), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string Media(string type, string url, string extra = "") => $"{{\"media\":{{\"type\":\"{type}\",\"url\":\"{url}\"{extra}}}}}";

    [Theory]
    [InlineData("image", "https://exemplo.com/a.png", true)]
    [InlineData("image", "https://cdn.exemplo.com/pasta/foto.JPG?v=2", true)]
    [InlineData("image", "https://exemplo.com/a.webp", true)]
    [InlineData("video", "https://exemplo.com/filme.ogv", true)]
    [InlineData("image", "http://exemplo.com/a.png", false)]
    [InlineData("image", "ftp://exemplo.com/a.png", false)]
    [InlineData("image", "https://localhost/a.png", false)]
    [InlineData("image", "https://app.localhost/a.png", false)]
    [InlineData("image", "https://impressora.local/a.png", false)]
    [InlineData("image", "https://servico.internal/a.png", false)]
    [InlineData("image", "https://intranet/a.png", false)]
    [InlineData("image", "https://127.0.0.1/a.png", false)]
    [InlineData("image", "https://192.168.1.10/a.png", false)]
    [InlineData("image", "https://[::1]/a.png", false)]
    [InlineData("image", "https://usuario:senha@exemplo.com/a.png", false)]
    [InlineData("image", "https://exemplo.com/a.gif", false)]
    [InlineData("image", "https://exemplo.com/filme.ogv", false)]
    [InlineData("video", "https://exemplo.com/filme.mp4", false)]
    [InlineData("video", "https://exemplo.com/a.png", false)]
    [InlineData("web", "https://exemplo.com/a.png", false)]
    [InlineData("image", "", false)]
    public async Task OnlyPublicHttpsAddressesOfTheRightKindAreAccepted(string type, string url, bool accepted)
    {
        var (status, body) = await Create(Media(type, url));
        Assert.Equal(accepted ? 200 : 400, status);
        if (!accepted) Assert.Equal("invalid_object", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddressesAreCheckedOnEveryFaceAndTheFlagsMustBeTrueOrFalse()
    {
        Assert.Equal(200, (await Create("{\"faces\":[{\"index\":0,\"media\":{\"type\":\"image\",\"url\":\"https://exemplo.com/a.png\",\"glow\":true}}]}")).Status);
        Assert.Equal(400, (await Create("{\"faces\":[{\"index\":0,\"media\":{\"type\":\"image\",\"url\":\"https://127.0.0.1/a.png\"}}]}")).Status);
        Assert.Equal(400, (await Create(Media("image", "https://exemplo.com/a.png", ",\"playing\":\"sim\""))).Status);
        Assert.Equal(400, (await Create("{\"media\":\"https://exemplo.com/a.png\"}")).Status);
        Assert.Equal(400, (await Create(Media("image", "https://exemplo.com/" + new string('a', 600) + ".png"))).Status);
    }

    [Theory]
    [InlineData("{\"normalStrength\":2.5}", false)]
    [InlineData("{\"normalStrength\":1.5,\"specularStrength\":1.2,\"aoStrength\":0.5}", true)]
    [InlineData("{\"aoStrength\":3}", false)]
    [InlineData("{\"specularStrength\":-1}", false)]
    public async Task TheNewStrengthsStayInsideTheirRanges(string material, bool accepted) => Assert.Equal(accepted ? 200 : 400, (await Create(material)).Status);

    [Fact]
    public async Task MediaWeighsOnTheBudgetAndAVideoMoreThanAPicture()
    {
        async Task<int> CostOf(string material) { var (_, body) = await Create(material); return (await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == body.GetProperty("objectId").GetGuid())).Cost; }
        var plain = await CostOf("{}"); var picture = await CostOf(Media("image", "https://exemplo.com/a.png")); var video = await CostOf(Media("video", "https://exemplo.com/a.ogv"));
        Assert.True(picture >= plain + 5 && video >= plain + 12 && video > picture, $"{plain} / {picture} / {video}");
    }
}
