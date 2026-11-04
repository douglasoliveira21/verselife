using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Saved looks now keep the custom body and the animation overrides too, and give them back as far as they still exist.</summary>
public sealed class AvatarLooksFullTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _friend = Guid.NewGuid();

    public AvatarLooksFullTests() { Seed(_me, "Marina Costa"); Seed(_friend, "Rafa Lima"); }
    public void Dispose() => _db.Dispose();

    private void Seed(Guid user, string name)
    {
        var avatar = new Avatar { Id = Guid.NewGuid(), UserId = user, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now };
        avatar.Appearance = new AvatarAppearance { Id = Guid.NewGuid(), AvatarId = avatar.Id, SkinColor = "#C98F65", HairColor = "#2B1B14", HairStyle = "short", CreatedAt = _now, UpdatedAt = _now };
        foreach (var (slot, key) in new[] { ("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") }) avatar.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = slot, ItemKey = key, TintColor = "#FFFFFF" });
        _db.Avatars.Add(avatar); _db.SaveChanges();
    }

    private Avatar Mine(Guid? user = null) { _db.ChangeTracker.Clear(); return _db.Avatars.Include(a => a.Appearance).Include(a => a.EquippedItems).Single(a => a.UserId == (user ?? _me)); }

    private Guid Body(Guid user, string name = "Robô")
    {
        var avatar = Mine(user); var model = new AvatarModel { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = name, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k/" + Guid.NewGuid().ToString("N"), CreatedAt = _now };
        _db.AvatarModels.Add(model); _db.SaveChanges(); return model.Id;
    }

    private Guid AnimationFile(Guid user, params string[] clips)
    {
        var avatar = Mine(user); var report = JsonSerializer.Serialize(new { clips = clips.Select(c => new { name = c, duration = 1.5, bones = 20, rootMotion = false, loopable = true }) });
        var file = new AvatarAnimationFile { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = "Dança", ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "a/" + Guid.NewGuid().ToString("N"), ReportJson = report, CreatedAt = _now };
        _db.AvatarAnimationFiles.Add(file); _db.SaveChanges(); return file.Id;
    }

    private void Set(Guid user, Guid? body, Dictionary<string, string>? overrides)
    {
        var avatar = Mine(user); avatar.CustomModelId = body; avatar.Appearance!.AnimationOverridesJson = AvatarAnimations.WriteOverrides(overrides ?? []); _db.SaveChanges();
    }

    private (Guid? Body, Dictionary<string, string> Overrides) State(Guid? user = null) { var a = Mine(user); return (a.CustomModelId, AvatarAnimations.ReadOverrides(a.Appearance!.AnimationOverridesJson)); }
    private async Task<Guid> Save(string name, Guid? user = null) { var r = await AvatarLooks.SaveCurrentAsync(_db, user ?? _me, name, _now, default); Assert.True(r.Ok, r.Error); return ((LookSummary)r.Data!).Id; }
    private async Task<AvatarResponse> Apply(Guid look, Guid? user = null) { var r = await AvatarLooks.ApplyAsync(_db, user ?? _me, look, _now, default); Assert.True(r.Ok, r.Error); return (AvatarResponse)r.Data!; }

    [Fact]
    public async Task ALookKeepsTheBodyAndTheAnimationsAndPutsThemBack()
    {
        var body = Body(_me); var file = AnimationFile(_me, "groove");
        var wanted = new Dictionary<string, string> { ["Walk"] = "builtin:happy", ["Dance"] = $"custom:{file}#groove" };
        Set(_me, body, wanted);
        var look = await Save("Completo");
        var summary = (await AvatarLooks.ListAsync(_db, _me, default)).Single(); Assert.True(summary.Full); Assert.True(summary.CustomBody); Assert.Equal(2, summary.AnimationOverrides);

        Set(_me, null, null); Assert.Null(State().Body); Assert.Empty(State().Overrides); // dressed in the plain body now
        var response = await Apply(look);
        var (restoredBody, restored) = State(); Assert.Equal(body, restoredBody); Assert.Equal(wanted, restored);
        Assert.Equal(body, response.CustomModelId); Assert.NotNull(response.CustomModelHash); Assert.Contains(file.ToString(), response.AnimationFilesJson); Assert.Empty(response.LookSkipped!);
    }

    [Fact]
    public async Task ALookOfThePlainBodyTakesTheCustomBodyOff()
    {
        var look = await Save("Simples");
        var body = Body(_me); Set(_me, body, new() { ["Idle"] = "builtin:relaxed" });
        var response = await Apply(look);
        Assert.Null(State().Body); Assert.Empty(State().Overrides); Assert.Null(response.CustomModelId);
    }

    [Fact]
    public async Task ABodyDeletedSinceIsLeftOutAndTheRestStillComesOn()
    {
        var body = Body(_me); Set(_me, body, new() { ["Walk"] = "builtin:tired" });
        var look = await Save("Com robô");
        _db.AvatarModels.Single(m => m.Id == body).DeletedAt = _now; await _db.SaveChangesAsync();
        Set(_me, null, null);
        var response = await Apply(look);
        Assert.Null(State().Body); Assert.Equal("builtin:tired", State().Overrides["Walk"]); Assert.Equal(["body"], response.LookSkipped);
    }

    [Fact]
    public async Task AnAnimationDeletedSinceIsDroppedAndReported()
    {
        var file = AnimationFile(_me, "groove"); Set(_me, null, new() { ["Dance"] = $"custom:{file}#groove", ["Walk"] = "builtin:happy" });
        var look = await Save("Dançarino");
        _db.AvatarAnimationFiles.Single(f => f.Id == file).DeletedAt = _now; await _db.SaveChangesAsync(); Set(_me, null, null);
        var response = await Apply(look);
        Assert.Equal(new Dictionary<string, string> { ["Walk"] = "builtin:happy" }, State().Overrides); Assert.Equal(["animation:Dance"], response.LookSkipped);
    }

    [Fact]
    public async Task ABodyOfSomeoneElseNeverComesOnThroughALook()
    {
        var theirs = Body(_friend); Set(_me, null, null);
        var avatar = Mine(); var snapshot = AvatarLooks.Snapshot(avatar) with { WithBodyAndAnimations = true, CustomModelId = theirs, AnimationOverridesJson = "{}" };
        var look = new AvatarLook { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = "Forjado", SnapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = _now, UpdatedAt = _now };
        _db.AvatarLooks.Add(look); await _db.SaveChangesAsync();
        var response = await Apply(look.Id);
        Assert.Null(State().Body); Assert.Null(response.CustomModelId); Assert.Contains("body", response.LookSkipped!);
    }

    [Fact]
    public async Task OldLooksLeaveTheBodyAndAnimationsAlone()
    {
        var avatar = Mine(); var old = AvatarLooks.Snapshot(avatar) with { WithBodyAndAnimations = false, CustomModelId = null, AnimationOverridesJson = null };
        // what the server wrote before: no field for body or animations at all
        var json = JsonSerializer.Serialize(old, new JsonSerializerOptions(JsonSerializerDefaults.Web)); using (var d = JsonDocument.Parse(json)) json = JsonSerializer.Serialize(d.RootElement.EnumerateObject().Where(p => p.Name is not ("withBodyAndAnimations" or "customModelId" or "animationOverridesJson")).ToDictionary(p => p.Name, p => p.Value));
        Assert.DoesNotContain("customModelId", json);
        _db.AvatarLooks.Add(new AvatarLook { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = "Antigo", SnapshotJson = json, CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        var body = Body(_me); Set(_me, body, new() { ["Walk"] = "builtin:happy" });
        var summary = (await AvatarLooks.ListAsync(_db, _me, default)).Single(); Assert.False(summary.Full);
        await Apply(summary.Id);
        Assert.Equal(body, State().Body); Assert.Equal("builtin:happy", State().Overrides["Walk"]); // untouched
    }

    [Fact]
    public async Task ASharedLookBringsNeitherBodyNorAnimationFiles()
    {
        var body = Body(_me); var file = AnimationFile(_me, "groove"); Set(_me, body, new() { ["Dance"] = $"custom:{file}#groove" });
        var look = await Save("Para dividir"); var share = await AvatarLooks.ShareAsync(_db, _me, look, true, _now, default); var code = ((LookSummary)share.Data!).ShareCode!;
        var friendsBody = Body(_friend, "Do Rafa"); Set(_friend, friendsBody, new() { ["Walk"] = "builtin:tired" });
        var imported = await AvatarLooks.ImportAsync(_db, _friend, code, null, _now, default); Assert.True(imported.Ok, imported.Error);
        var copy = (LookSummary)imported.Data!; Assert.False(copy.Full); Assert.False(copy.CustomBody); Assert.Equal(0, copy.AnimationOverrides);
        await Apply(copy.Id, _friend);
        Assert.Equal(friendsBody, State(_friend).Body); Assert.Equal("builtin:tired", State(_friend).Overrides["Walk"]); // the friend keeps their own
        Assert.DoesNotContain(file.ToString(), _db.AvatarLooks.AsNoTracking().Single(l => l.Id == copy.Id).SnapshotJson);
    }

    [Fact]
    public async Task ADuplicateKeepsEverything()
    {
        var body = Body(_me); Set(_me, body, new() { ["Walk"] = "builtin:happy" }); var look = await Save("Original");
        var copy = ((LookSummary)(await AvatarLooks.DuplicateAsync(_db, _me, look, _now, default)).Data!); Assert.True(copy.Full); Assert.True(copy.CustomBody); Assert.Equal(1, copy.AnimationOverrides);
        Set(_me, null, null); await Apply(copy.Id); Assert.Equal(body, State().Body);
    }
}
