using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Land;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Voice and media per parcel: the owner switches them, voice is told to the island server through the land sync, and media in the objects of people who only build there is refused when it is off.</summary>
public sealed class LandVoiceMediaTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Island _island;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid(), _visitor = Guid.NewGuid(), _instance = Guid.NewGuid(), _parcel;
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
    private const string Media = "{\"media\":{\"type\":\"video\",\"url\":\"https://cdn.example.com/a.ogv\",\"playing\":true}}";

    public LandVoiceMediaTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        foreach (var user in new[] { _visitor, _owner }) _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = user, Permission = "enter", PrincipalType = "user", GrantedByUserId = _island.OwnerUserId });
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        _db.SaveChanges();
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x, z));
        var claimed = LandService.ClaimAsync(_db, _island, _owner, "Casa", "", cells, 50, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(claimed.Error); _parcel = claimed.Parcel!.Id;
    }

    public void Dispose() => _db.Dispose();

    private Task<CommerceResult> Options(Guid who, string mode = "everyone", bool? voice = null, bool? media = null) => LandAdmin.SetOptionsAsync(_db, _parcel, who, mode, false, null, _clock, default, voice, media);
    private IslandParcel Stored() => _db.IslandParcels.AsNoTracking().Single(p => p.Id == _parcel);
    private static BuildObjectRequest Cube(string material = "{}") => new("cube", 2, 1, 2, 0, 0, 0, 1, 1, 1, material, "{\"collision\":true}", null, 0, "{}", "Caixa", null, null, null, null);

    private async Task<(int Status, string? Code)> Apply(string operation, Guid actor, string material, Guid? id = null, int revision = 0)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, operation, id, Cube(material), revision, null), _db, _storage, _clock, default);
        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var code = JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) is { ValueKind: JsonValueKind.Object } body && body.TryGetProperty("code", out var c) ? c.GetString() : null;
        return (status, code);
    }

    [Fact]
    public async Task VoiceAndMediaAreOnByDefault_OnlyTheOwnerSwitchesThem_AndARequestWithoutThemLeavesThemAlone()
    {
        Assert.True(Stored().AllowVoice); Assert.True(Stored().AllowMedia);
        Assert.Equal("forbidden", (await Options(_visitor, voice: false)).Error);
        Assert.True((await Options(_owner, voice: false, media: false)).Ok); Assert.False(Stored().AllowVoice); Assert.False(Stored().AllowMedia);
        Assert.True((await Options(_owner, "owner")).Ok); Assert.False(Stored().AllowVoice); Assert.False(Stored().AllowMedia); // an old client that does not know them changes nothing
        Assert.True((await Options(_owner, voice: true)).Ok); Assert.True(Stored().AllowVoice); Assert.False(Stored().AllowMedia);
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.Operation == "build_options" && c.PayloadJson.Contains("\"media\":false"));
    }

    [Fact]
    public async Task TheLandViewTellsTheClientWhereVoiceIsOff()
    {
        Assert.True((await LandService.ViewAsync(_db, _island, _owner, default)).Parcels.Single().AllowVoice);
        await Options(_owner, voice: false);
        Assert.False((await LandService.ViewAsync(_db, _island, _visitor, default)).Parcels.Single().AllowVoice); // everybody who may look at the land sees it, so the voice bar can say so
        Assert.True((await LandService.ViewAsync(_db, _island, _visitor, default)).Parcels.Single().AllowMedia); await Options(_owner, media: false); Assert.False((await LandService.ViewAsync(_db, _island, _visitor, default)).Parcels.Single().AllowMedia); // the build editor reads it to warn before the person tries
    }

    [Fact]
    public async Task TheAboutViewCarriesBothSwitches()
    {
        await Options(_owner, voice: false);
        var about = JsonSerializer.SerializeToElement((await LandAbout.AboutAsync(_db, _island, _parcel, _owner, _clock.Now, default))!).GetProperty("parcel");
        Assert.False(about.GetProperty("allowVoice").GetBoolean()); Assert.True(about.GetProperty("allowMedia").GetBoolean());
    }

    [Fact]
    public async Task TheLandSyncTellsTheIslandServerWhereVoiceIsOff_AndItReadsThatBack()
    {
        var quiet = JsonSerializer.SerializeToElement(((await LandSync.BuildAsync(_db, _instance, 0, _clock, default))!).Rules!); Assert.Equal(0, quiet.GetProperty("parcels").GetArrayLength());
        await Options(_owner, voice: false);
        var answer = (await LandSync.BuildAsync(_db, _instance, 0, _clock, default))!; var rules = JsonSerializer.SerializeToElement(answer.Rules!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1, rules.GetProperty("parcels").GetArrayLength()); Assert.False(rules.GetProperty("parcels")[0].GetProperty("voice").GetBoolean());
        var map = LandRules.Parse(rules);
        Assert.True(map.HasZones); Assert.False(map.VoiceAllowedAt(2, 2)); Assert.False(map.VoiceAllowedAt(15.9, 15.9)); Assert.True(map.VoiceAllowedAt(16.1, 2)); Assert.True(map.VoiceAllowedAt(-3, -3));
        await Options(_owner, voice: true); Assert.False(LandRules.Parse(JsonSerializer.SerializeToElement(((await LandSync.BuildAsync(_db, _instance, 0, _clock, default))!).Rules!, new JsonSerializerOptions(JsonSerializerDefaults.Web))).HasZones);
    }

    [Fact]
    public void TheRulesParserSkipsDamagedEntriesAndEverythingIsAllowedWithoutRules()
    {
        Assert.True(LandRules.Empty.VoiceAllowedAt(1, 1)); Assert.False(LandRules.Empty.HasZones);
        using var broken = JsonDocument.Parse("{\"parcels\":[{\"voice\":false},{\"id\":\"x\",\"voice\":false,\"runs\":[[0,0,0]]},{\"id\":\"y\",\"voice\":true,\"runs\":[[5,5,5]]}]}");
        var map = LandRules.Parse(broken.RootElement); Assert.False(map.VoiceAllowedAt(2, 2)); Assert.True(map.VoiceAllowedAt(22, 22)); // only the well-formed entry that switches voice off counts
        using var none = JsonDocument.Parse("[1]"); Assert.False(LandRules.Parse(none.RootElement).HasZones);
    }

    [Fact]
    public void MediaIsRecognisedInTheMaterial()
    {
        Assert.True(BuildCost.HasMedia(Media)); Assert.False(BuildCost.HasMedia("{}")); Assert.False(BuildCost.HasMedia(null)); Assert.False(BuildCost.HasMedia("not json")); Assert.False(BuildCost.HasMedia("{\"media\":5}"));
    }

    [Fact]
    public async Task MediaInTheObjectsOfPeopleWhoOnlyBuildHereIsRefusedWhenTheParcelSaysNo_ButNeverForTheOwner()
    {
        await Options(_owner, "everyone");
        Assert.Equal(200, (await Apply("create", _visitor, Media)).Status); // allowed while the parcel allows media
        await Options(_owner, "everyone", media: false);
        var refused = await Apply("create", _visitor, Media); Assert.Equal((403, "media_not_allowed"), refused);
        Assert.Equal(200, (await Apply("create", _visitor, "{}")).Status); // an object without media is no problem
        Assert.Equal(200, (await Apply("create", _owner, Media)).Status); // the owner puts what the owner likes
    }

    [Fact]
    public async Task AddingMediaToAnObjectThatHadNoneIsRefusedToo_AndTurningMediaBackOnAllowsIt()
    {
        await Options(_owner, "everyone", media: false);
        Assert.Equal(200, (await Apply("create", _visitor, "{}")).Status);
        var item = await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.OwnerUserId == _visitor);
        var refused = await Apply("update", _visitor, Media, item.Id, item.Revision); Assert.Equal((403, "media_not_allowed"), refused);
        await Options(_owner, "everyone", media: true);
        Assert.Equal(200, (await Apply("update", _visitor, Media, item.Id, item.Revision)).Status);
    }
}
