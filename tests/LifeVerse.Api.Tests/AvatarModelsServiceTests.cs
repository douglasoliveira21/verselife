using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Custom avatar bodies on the server: upload and check, storage by hash, choosing, deleting, and what is sent to others.</summary>
public sealed class AvatarModelsServiceTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryStore _store = new();
    private readonly AssetStorageService _storage;
    private readonly DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _other = Guid.NewGuid();

    public AvatarModelsServiceTests()
    {
        _storage = new AssetStorageService(_store, NullLogger<AssetStorageService>.Instance);
        foreach (var user in new[] { _me, _other }) { _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = user, DisplayName = "Pessoa", BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Task<ModelResult> Upload(Guid user, byte[] bytes, string file = "corpo.glb", string? name = null) => AvatarModels.UploadAsync(_db, _storage, user, file, bytes, name, _now, default);
    private static byte[] Glb(TemplateOptions? options = null) => HumanoidTemplate.Build(options);
    private static JsonElement Json(object? data) => JsonSerializer.SerializeToElement(data);

    [Fact]
    public async Task AGoodModelIsStoredWithItsReportAndBoneMap()
    {
        var bytes = Glb(new TemplateOptions { Naming = BoneNaming.Mixamo });
        var result = await Upload(_me, bytes, "Meu Corpo.glb"); Assert.True(result.Ok, result.Error);
        var model = await _db.AvatarModels.SingleAsync();
        Assert.Equal("Meu Corpo", model.Name); Assert.Equal(bytes.Length, model.SizeBytes); Assert.Equal(204, model.Triangles); Assert.Equal(17, model.Joints);
        Assert.Equal(64, model.ContentHash.Length); Assert.Equal(bytes, _store.Files[model.StorageKey]);
        using var map = JsonDocument.Parse(model.BoneMapJson); Assert.Equal("mixamorig:LeftForeArm", map.RootElement.GetProperty("lower_arm_l").GetString());
        using var report = JsonDocument.Parse(model.ReportJson); Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.InRange(model.ScaleToStandard, 1f, 1.03f);
        Assert.Equal(model.Id, (await AvatarModels.ListAsync(_db, _me, default)).Single().Id);
        Assert.Empty(await AvatarModels.ListAsync(_db, _other, default));
    }

    [Fact]
    public async Task ARefusedModelIsNotStoredAndComesBackWithItsReport()
    {
        var result = await Upload(_me, Glb(new TemplateOptions { OmitBone = "hand_l" }));
        Assert.False(result.Ok); Assert.Equal("invalid_model", result.Error);
        Assert.Contains(Json(result.Data).GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "missing_bones");
        Assert.Empty(_store.Files); Assert.Empty(_db.AvatarModels);
        Assert.Equal("bad_file", (await Upload(_me, Glb(), "corpo.gltf")).Error);
        Assert.Equal("bad_file", (await Upload(_me, Glb(), "corpo.exe")).Error);
        Assert.Equal("invalid_model", (await Upload(_me, "just text pretending"u8.ToArray())).Error);
        Assert.Equal("not_found", (await Upload(Guid.NewGuid(), Glb())).Error);
    }

    [Fact]
    public async Task TheSameFileTwiceIsOneModel_AndThereIsALimit()
    {
        var first = await Upload(_me, Glb()); var again = await Upload(_me, Glb());
        Assert.True(first.Ok && again.Ok); Assert.Single(_db.AvatarModels);
        for (var i = 1; i < AvatarModels.MaxModels; i++) Assert.True((await Upload(_me, Glb(new TemplateOptions { Scale = 1 + i / 10f }))).Ok);
        Assert.Equal("too_many_models", (await Upload(_me, Glb(new TemplateOptions { Scale = 3 }))).Error);
        Assert.True((await Upload(_other, Glb())).Ok); // another person has their own room
        Assert.Equal(AvatarModels.MaxModels + 1, _db.AvatarModels.Count());
    }

    [Fact]
    public async Task OnlyTheOwnerChoosesAModel_AndDeletingItGoesBackToTheDefaultBody()
    {
        await Upload(_me, Glb()); var id = (await _db.AvatarModels.SingleAsync()).Id;
        Assert.Equal("not_found", (await AvatarModels.UseAsync(_db, _other, id, _now, default)).Error); // somebody else's model
        Assert.Equal("not_found", (await AvatarModels.UseAsync(_db, _me, Guid.NewGuid(), _now, default)).Error);
        Assert.True((await AvatarModels.UseAsync(_db, _me, id, _now, default)).Ok);
        var avatar = await _db.Avatars.AsNoTracking().SingleAsync(a => a.UserId == _me); Assert.Equal(id, avatar.CustomModelId);
        Assert.True((await AvatarModels.ListAsync(_db, _me, default)).Single().Active);
        var response = UserEndpoints.ToResponse(avatar, await AvatarModels.ActiveAsync(_db, avatar, default));
        Assert.Equal(id, response.CustomModelId); Assert.Equal(64, response.CustomModelHash!.Length); Assert.Contains("hips", response.CustomModelBonesJson);
        Assert.Equal("not_found", (await AvatarModels.DeleteAsync(_db, _other, id, _now, default)).Error);
        Assert.True((await AvatarModels.DeleteAsync(_db, _me, id, _now, default)).Ok);
        Assert.Null((await _db.Avatars.AsNoTracking().SingleAsync(a => a.UserId == _me)).CustomModelId);
        Assert.Empty(await AvatarModels.ListAsync(_db, _me, default));
        Assert.Equal("not_found", (await AvatarModels.UseAsync(_db, _me, id, _now, default)).Error); // a deleted model cannot be chosen
        Assert.True((await Upload(_me, Glb())).Ok); // and the same file can be uploaded again
        Assert.Single(await AvatarModels.ListAsync(_db, _me, default));
    }

    [Fact]
    public async Task ClearingTheModelAndTheReport()
    {
        await Upload(_me, Glb()); var id = (await _db.AvatarModels.SingleAsync()).Id;
        await AvatarModels.UseAsync(_db, _me, id, _now, default);
        Assert.True((await AvatarModels.UseAsync(_db, _me, null, _now, default)).Ok);
        Assert.Null((await _db.Avatars.AsNoTracking().SingleAsync(a => a.UserId == _me)).CustomModelId);
        Assert.NotNull(await AvatarModels.ReportAsync(_db, _me, id, default));
        Assert.Null(await AvatarModels.ReportAsync(_db, _other, id, default)); // the report belongs to the owner
    }

    [Fact]
    public async Task WithoutAModelTheResponseIsTheOldOne()
    {
        var avatar = await _db.Avatars.AsNoTracking().SingleAsync(a => a.UserId == _me);
        var response = UserEndpoints.ToResponse(avatar, null);
        Assert.Null(response.CustomModelId); Assert.Equal("{}", response.CustomModelBonesJson); Assert.Equal(1f, response.CustomModelScale);
    }
}
