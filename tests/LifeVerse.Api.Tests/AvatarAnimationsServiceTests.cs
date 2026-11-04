using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Animation files and overrides on the server, and the text that tells everybody what an avatar looks like and how it moves.</summary>
public sealed class AvatarAnimationsServiceTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryStore _store = new();
    private readonly AssetStorageService _storage;
    private readonly DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _other = Guid.NewGuid();
    private static readonly (string, int, float)[] Walk = [("upper_leg_l", 0, .6f), ("upper_leg_r", 0, -.6f), ("lower_leg_l", 0, .4f), ("lower_leg_r", 0, .4f), ("upper_arm_l", 0, -.4f), ("upper_arm_r", 0, .4f), ("spine", 1, .1f), ("head", 1, -.1f), ("chest", 1, .1f)];

    public AvatarAnimationsServiceTests()
    {
        _storage = new AssetStorageService(_store, NullLogger<AssetStorageService>.Instance);
        foreach (var user in new[] { _me, _other })
        {
            var avatar = new Avatar { Id = Guid.NewGuid(), UserId = user, DisplayName = "Pessoa", BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now };
            _db.Avatars.Add(avatar); _db.AvatarAppearances.Add(new AvatarAppearance { Id = Guid.NewGuid(), AvatarId = avatar.Id, SkinColor = "#C98F65", HairColor = "#2B1B14", HairStyle = "short", CreatedAt = _now, UpdatedAt = _now });
            foreach (var (slot, key) in new[] { ("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") }) _db.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = slot, ItemKey = key, TintColor = "#FFFFFF", EquippedAt = _now });
        }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static byte[] File(string name = "Passeio", float seconds = 1f, params string[] more) => HumanoidTemplate.Build(new TemplateOptions { Animations = [new AnimationSpec(name, seconds, Walk), .. more.Select(m => new AnimationSpec(m, seconds, Walk))] });
    private Task<ModelResult> Upload(Guid user, byte[] bytes, string file = "anim.glb", string? name = null) => AvatarAnimations.UploadAsync(_db, _storage, user, file, bytes, name, _now, default);
    private async Task<Guid> Own(Guid user, byte[] bytes) { var result = await Upload(user, bytes); Assert.True(result.Ok, result.Error); return JsonSerializer.SerializeToElement(result.Data, Web).GetProperty("file").GetProperty("id").GetGuid(); }
    private Task<ModelResult> Set(Guid user, params (string, string)[] map) => AvatarAnimations.SetOverridesAsync(_db, user, map.ToDictionary(m => m.Item1, m => m.Item2), _now, default);
    private async Task<Avatar> AvatarOf(Guid user) => await _db.Avatars.AsNoTracking().Include(a => a.Appearance).Include(a => a.EquippedItems).SingleAsync(a => a.UserId == user);

    // ---- files -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AGoodFileIsStoredWithItsClips()
    {
        var bytes = File("Andar", 1f, "Correr"); var id = await Own(_me, bytes);
        var list = await AvatarAnimations.ListAsync(_db, _me, default); var file = Assert.Single(list);
        Assert.Equal(id, file.Id); Assert.Equal(["Andar", "Correr"], file.Clips.Select(c => c.Name)); Assert.All(file.Clips, c => Assert.Equal(9, c.Bones)); Assert.Equal(64, file.Hash.Length);
        Assert.Equal(bytes, _store.Files[(await _db.AvatarAnimationFiles.SingleAsync()).StorageKey]);
        Assert.Empty(await AvatarAnimations.ListAsync(_db, _other, default));
    }

    [Fact]
    public async Task ARefusedFileIsNotStoredAndComesBackWithItsReport()
    {
        var result = await Upload(_me, HumanoidTemplate.Build()); // no animations
        Assert.False(result.Ok); Assert.Equal("invalid_animation", result.Error);
        Assert.Contains(JsonSerializer.SerializeToElement(result.Data, Web).GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "no_animations");
        Assert.Empty(_store.Files); Assert.Empty(_db.AvatarAnimationFiles);
        Assert.Equal("bad_file", (await Upload(_me, File(), "x.fbx")).Error); Assert.Equal("not_found", (await Upload(Guid.NewGuid(), File())).Error);
    }

    [Fact]
    public async Task TheSameFileTwiceIsOne_AndThereIsALimit()
    {
        await Own(_me, File()); await Own(_me, File()); Assert.Single(_db.AvatarAnimationFiles);
        for (var i = 1; i < AvatarAnimations.MaxFiles; i++) Assert.True((await Upload(_me, File($"A{i}"))).Ok);
        Assert.Equal("too_many_files", (await Upload(_me, File("Demais"))).Error);
        Assert.True((await Upload(_other, File())).Ok);
    }

    // ---- overrides ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheLibraryAndOwnAnimationsCanReplaceStates()
    {
        var id = await Own(_me, File("Andar", 1f, "Correr"));
        var result = await Set(_me, ("Walk", "builtin:happy"), ("Run", $"custom:{id}#Correr"), ("Idle", "default"), ("Dance", ""));
        Assert.True(result.Ok, result.Error);
        var stored = AvatarAnimations.ReadOverrides((await AvatarOf(_me)).Appearance!.AnimationOverridesJson);
        Assert.Equal(new Dictionary<string, string> { ["Walk"] = "builtin:happy", ["Run"] = $"custom:{id}#Correr" }, stored); // the defaults need no entry
        Assert.True((await Set(_me)).Ok); Assert.Equal("{}", (await AvatarOf(_me)).Appearance!.AnimationOverridesJson); // an empty map goes back to all defaults
    }

    [Fact]
    public async Task OnlyRealStatesAndRealSourcesAreAccepted()
    {
        var mine = await Own(_me, File("Andar")); var theirs = await Own(_other, File("Andar", 2f));
        Assert.Equal("bad_state", (await Set(_me, ("Moonwalk", "builtin:happy"))).Error);
        Assert.Equal("bad_source", (await Set(_me, ("Walk", "builtin:nope"))).Error); // not in the library
        Assert.Equal("bad_source", (await Set(_me, ("Run", "builtin:happy"))).Error); // an alternative of another state
        Assert.Equal("bad_source", (await Set(_me, ("Walk", $"custom:{theirs}#Andar"))).Error); // somebody else's file
        Assert.Equal("bad_source", (await Set(_me, ("Walk", $"custom:{mine}#Voar"))).Error); // an animation that is not in the file
        Assert.Equal("bad_source", (await Set(_me, ("Walk", $"custom:{Guid.NewGuid()}#Andar"))).Error);
        Assert.Equal("bad_source", (await Set(_me, ("Walk", "custom:xx#Andar"))).Error);
        Assert.Equal("bad_source", (await Set(_me, ("Walk", "http://evil.example/walk.glb"))).Error);
        Assert.Equal("{}", (await AvatarOf(_me)).Appearance!.AnimationOverridesJson); // nothing was half-saved
        Assert.Equal("too_many", (await AvatarAnimations.SetOverridesAsync(_db, _me, Enumerable.Range(0, 30).ToDictionary(i => $"S{i}", _ => "default"), _now, default)).Error);
        Assert.Equal("not_found", (await Set(Guid.NewGuid(), ("Walk", "builtin:happy"))).Error);
    }

    [Fact]
    public async Task DeletingAFileGivesItsStatesTheirDefaultsBack()
    {
        var id = await Own(_me, File("Andar", 1f, "Correr")); var keep = await Own(_me, File("Dancar"));
        await Set(_me, ("Walk", $"custom:{id}#Andar"), ("Run", $"custom:{id}#Correr"), ("Dance", $"custom:{keep}#Dancar"), ("Idle", "builtin:relaxed"));
        Assert.Equal("not_found", (await AvatarAnimations.DeleteAsync(_db, _other, id, _now, default)).Error);
        Assert.True((await AvatarAnimations.DeleteAsync(_db, _me, id, _now, default)).Ok);
        Assert.Equal(["Dance", "Idle"], AvatarAnimations.ReadOverrides((await AvatarOf(_me)).Appearance!.AnimationOverridesJson).Keys.Order());
        Assert.Equal(keep, Assert.Single(await AvatarAnimations.ListAsync(_db, _me, default)).Id);
        Assert.Equal("bad_source", (await Set(_me, ("Walk", $"custom:{id}#Andar"))).Error); // a deleted file cannot be chosen
        Assert.True((await Upload(_me, File("Andar", 1f, "Correr"))).Ok); // and can be uploaded again
    }

    // ---- what is told to everybody -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EverybodyIsToldWhichAnimationsToPlayAndWhatToDownload()
    {
        var id = await Own(_me, File("Andar"));
        await Set(_me, ("Walk", $"custom:{id}#Andar"), ("Dance", "builtin:groove"));
        var avatar = await AvatarOf(_me);
        var filesJson = await AvatarAnimations.FilesInfoAsync(_db, avatar, default);
        var file = JsonDocument.Parse(filesJson).RootElement.EnumerateArray().Single(); Assert.Equal(id, file.GetProperty("id").GetGuid()); Assert.Equal(64, file.GetProperty("hash").GetString()!.Length);
        Assert.Equal("[]", await AvatarAnimations.FilesInfoAsync(_db, await AvatarOf(_other), default));

        var json = JsonDocument.Parse(await AvatarAppearanceJson.BuildAsync(_db, avatar, default)).RootElement;
        Assert.Equal("Pessoa", json.GetProperty("displayName").GetString()); Assert.Equal(avatar.Id, json.GetProperty("id").GetGuid());
        Assert.Equal("builtin:groove", JsonDocument.Parse(json.GetProperty("animationOverridesJson").GetString()!).RootElement.GetProperty("Dance").GetString());
        Assert.Equal(id, JsonDocument.Parse(json.GetProperty("animationFilesJson").GetString()!).RootElement[0].GetProperty("id").GetGuid());
        Assert.Equal(3, json.GetProperty("equippedItems").GetArrayLength());
        var response = UserEndpoints.ToResponse(avatar, null, "[]", filesJson);
        Assert.Contains("custom:", response.AnimationOverridesJson); Assert.Equal(filesJson, response.AnimationFilesJson);
    }

    [Fact]
    public async Task TheRefreshedAppearanceIsWhatIsStored_NotWhatTheClientSays()
    {
        // an avatar that changed while playing: the island server asks the database, so the text is exactly the stored state
        var before = await AvatarAppearanceJson.BuildAsync(_db, await AvatarOf(_me), default);
        var appearance = await _db.AvatarAppearances.SingleAsync(a => a.AvatarId == (_db.Avatars.Single(x => x.UserId == _me).Id)); appearance.HairStyle = "long"; await _db.SaveChangesAsync();
        var after = await AvatarAppearanceJson.BuildAsync(_db, await AvatarOf(_me), default);
        Assert.NotEqual(before, after); Assert.Equal("long", JsonDocument.Parse(after).RootElement.GetProperty("hairStyle").GetString());
        Assert.Equal(after, await AvatarAppearanceJson.BuildAsync(_db, await AvatarOf(_me), default)); // asking again changes nothing
    }

    [Fact]
    public void TheBuiltinAlternativesAreOnlyForStatesThatExist()
    {
        foreach (var (state, names) in AnimationStates.Builtin) { Assert.True(AnimationStates.All.ContainsKey(state)); Assert.Equal(names.Length, names.Distinct().Count()); Assert.All(names, n => Assert.Matches("^[a-z0-9_]+$", n)); }
    }
}
