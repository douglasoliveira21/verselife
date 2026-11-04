using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Land;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Cutting and joining parcels, who may enter, what stands on the land and what the island servers are told.</summary>
public sealed class LandAdminTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly DbContextOptions<LifeVerseDbContext> _options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly LifeVerseDbContext _db;
    private readonly Island _island;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid(), _visitor = Guid.NewGuid(), _other = Guid.NewGuid(), _admin = Guid.NewGuid(), _instance = Guid.NewGuid();
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);

    public LandAdminTests()
    {
        _db = new LifeVerseDbContext(_options);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _island.OwnerUserId });
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HashSet<long> Rect(int cx0, int cz0, int cx1, int cz1) { var set = new HashSet<long>(); for (var x = cx0; x <= cx1; x++) for (var z = cz0; z <= cz1; z++) set.Add(LandGrid.Key(x, z)); return set; }

    private Guid Claim(string name, HashSet<long> cells, Guid? owner = null, int max = 100)
    {
        var result = LandService.ClaimAsync(_db, _island, owner ?? _owner, name, "", cells, max, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); return result.Parcel!.Id;
    }

    private IslandParcel Stored(Guid id) => _db.IslandParcels.AsNoTracking().Single(p => p.Id == id);
    private HashSet<long> CellsOf(Guid id) => _db.ParcelCells.AsNoTracking().Where(c => c.ParcelId == id).AsEnumerable().Select(c => LandGrid.Key(c.Cx, c.Cz)).ToHashSet();
    private BuildObject Put(Guid owner, float x, float z, string content = "[]") { var o = new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = owner, OwnerUserId = owner, Name = "Mesa", PrimitiveType = "cube", PositionX = x, PositionY = 1, PositionZ = z, Cost = 5, ContentJson = content, PhysicsJson = "{\"collision\":true}", CreatedAt = _clock.Now }; _db.BuildObjects.Add(o); _db.SaveChanges(); return o; }

    // ---- splitting -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Split_CutsANewParcelOutAndSharesTheBudgetByArea()
    {
        var whole = Claim("Grande", Rect(0, 0, 7, 3), max: 100);
        var inside = Put(_visitor, 2, 2); // in the cells that stay
        var result = await LandAdmin.SplitAsync(_db, whole, _owner, Rect(4, 0, 7, 3), "Metade", _clock, default);
        Assert.True(result.Ok, result.Error);
        var created = _db.IslandParcels.AsNoTracking().Single(p => p.Name == "Metade");
        Assert.Equal(_owner, created.OwnerUserId); Assert.Equal(16 * 16, created.Area); Assert.Equal(16 * 16, Stored(whole).Area);
        Assert.Equal(50, created.MaxObjects); Assert.Equal(50, Stored(whole).MaxObjects);
        Assert.Equal(Rect(4, 0, 7, 3), CellsOf(created.Id)); Assert.Equal(Rect(0, 0, 3, 3), CellsOf(whole));
        Assert.Equal(_visitor, _db.BuildObjects.AsNoTracking().Single(o => o.Id == inside.Id).OwnerUserId); // objects are untouched
        Assert.Equal(2, Stored(whole).Revision);
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == whole && c.Operation == "split");
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == created.Id && c.Operation == "split_from");
        // the grid still has one owner per cell
        Assert.Equal("overlap", (await LandService.ClaimAsync(_db, _island, _owner, "Outra", "", Rect(5, 1, 5, 1), 10, null, null, null, null, null, _clock, default)).Error);
    }

    [Fact]
    public async Task Split_BudgetNeverFallsBelowWhatIsAlreadyBuilt()
    {
        var whole = Claim("Grande", Rect(0, 0, 7, 3), max: 10);
        for (var i = 0; i < 6; i++) Put(_owner, 18 + i, 2); // 6 objects on the half that is cut out
        var result = await LandAdmin.SplitAsync(_db, whole, _owner, Rect(4, 0, 7, 3), "Metade", _clock, default);
        Assert.True(result.Ok);
        Assert.Equal(6, _db.IslandParcels.AsNoTracking().Single(p => p.Name == "Metade").MaxObjects); Assert.Equal(4, Stored(whole).MaxObjects);
        var tight = Claim("Apertado", Rect(20, 0, 23, 3), max: 4);
        for (var i = 0; i < 3; i++) { Put(_owner, 82, 2 + i); Put(_owner, 90, 2 + i); }
        Assert.Equal("limit_too_low", (await LandAdmin.SplitAsync(_db, tight, _owner, Rect(22, 0, 23, 3), "Corte", _clock, default)).Error);
        Assert.Equal(4, Stored(tight).MaxObjects);
    }

    [Fact]
    public async Task Split_RefusesWhatWouldBreakTheParcel()
    {
        var whole = Claim("Grande", Rect(0, 0, 7, 3));
        Assert.Equal("forbidden", (await LandAdmin.SplitAsync(_db, whole, _visitor, Rect(4, 0, 7, 3), "X", _clock, default)).Error);
        Assert.Equal("empty", (await LandAdmin.SplitAsync(_db, whole, _owner, [], "X", _clock, default)).Error);
        Assert.Equal("not_in_parcel", (await LandAdmin.SplitAsync(_db, whole, _owner, Rect(8, 0, 9, 3), "X", _clock, default)).Error);
        Assert.Equal("whole_parcel", (await LandAdmin.SplitAsync(_db, whole, _owner, Rect(0, 0, 7, 3), "X", _clock, default)).Error);
        Assert.Equal("not_contiguous", (await LandAdmin.SplitAsync(_db, whole, _owner, new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(7, 3) }, "X", _clock, default)).Error);
        Assert.Equal("not_contiguous", (await LandAdmin.SplitAsync(_db, whole, _owner, Rect(3, 0, 4, 3), "X", _clock, default)).Error); // the cut leaves two separate pieces behind
        Assert.Equal("bad_name", (await LandAdmin.SplitAsync(_db, whole, _owner, Rect(4, 0, 7, 3), "  ", _clock, default)).Error);
        Assert.Equal("name_taken", (await LandAdmin.SplitAsync(_db, whole, _owner, Rect(4, 0, 7, 3), "Grande", _clock, default)).Error);
        Assert.Equal(32, CellsOf(whole).Count);
        _db.ParcelRentals.Add(new ParcelRental { Id = Guid.NewGuid(), ParcelId = whole, LandlordUserId = _owner, TenantUserId = _visitor, Status = "active", StartsAt = _clock.Now, ExpiresAt = _clock.Now.AddDays(5), CreatedAt = _clock.Now }); _db.SaveChanges();
        Assert.Equal("rented", (await LandAdmin.SplitAsync(_db, whole, _owner, Rect(4, 0, 7, 3), "Metade", _clock, default)).Error);
    }

    [Fact]
    public async Task Split_CarriesTheAccessListAlongAndClearsOffers()
    {
        var whole = Claim("Grande", Rect(0, 0, 7, 3));
        await LandAdmin.AddAccessAsync(_db, whole, _owner, _visitor, "allow", null, "amigo", _clock, default);
        await LandAdmin.SetAccessModeAsync(_db, whole, _owner, "list", _clock, default);
        await LandCommerce.OfferForSaleAsync(_db, whole, _owner, new SaleOffer(500, null, "", true), _clock, default);
        await LandAdmin.SplitAsync(_db, whole, _owner, Rect(4, 0, 7, 3), "Metade", _clock, default);
        var created = _db.IslandParcels.AsNoTracking().Single(p => p.Name == "Metade");
        Assert.Equal("list", created.AccessMode); Assert.Contains(_db.ParcelAccess.AsNoTracking(), a => a.ParcelId == created.Id && a.UserId == _visitor && a.Kind == "allow");
        Assert.Null(Stored(whole).SalePrice);
    }

    // ---- joining ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Merge_JoinsNeighboursAddsTheBudgetAndKeepsEverythingElse()
    {
        var a = Claim("A", Rect(0, 0, 3, 3), max: 40); var b = Claim("B", Rect(4, 0, 7, 3), max: 60);
        var mine = Put(_visitor, 18, 2);
        var result = await LandAdmin.MergeAsync(_db, [a, b], _owner, null, _clock, default);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(Rect(0, 0, 7, 3), CellsOf(a)); Assert.Empty(CellsOf(b));
        Assert.Equal(100, Stored(a).MaxObjects); Assert.Equal(32 * 16, Stored(a).Area);
        Assert.NotNull(Stored(b).DeletedAt); Assert.Null(Stored(a).DeletedAt);
        Assert.Equal(_visitor, _db.BuildObjects.AsNoTracking().Single(o => o.Id == mine.Id).OwnerUserId);
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == a && c.Operation == "merge");
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == b && c.Operation == "merged_into");
        Claim("B", Rect(20, 20, 21, 21)); // the freed name can be used again
    }

    [Fact]
    public async Task Merge_RefusesWhatDoesNotTouchOrBelongsToOthers()
    {
        var a = Claim("A", Rect(0, 0, 3, 3)); var far = Claim("Longe", Rect(10, 10, 13, 13)); var corner = Claim("Quina", Rect(4, 4, 7, 7)); var theirs = Claim("Alheio", Rect(4, 0, 7, 3), _visitor);
        Assert.Equal("not_adjacent", (await LandAdmin.MergeAsync(_db, [a, far], _owner, null, _clock, default)).Error);
        Assert.Equal("not_adjacent", (await LandAdmin.MergeAsync(_db, [a, corner], _owner, null, _clock, default)).Error); // only a corner touches
        Assert.Equal("forbidden", (await LandAdmin.MergeAsync(_db, [a, theirs], _owner, null, _clock, default)).Error);
        Assert.Equal("bad_selection", (await LandAdmin.MergeAsync(_db, [a], _owner, null, _clock, default)).Error);
        Assert.Equal("not_found", (await LandAdmin.MergeAsync(_db, [a, Guid.NewGuid()], _owner, null, _clock, default)).Error);
        Assert.Equal(16, CellsOf(a).Count);
    }

    [Fact]
    public async Task Merge_AsksWhichSettingsWinWhenTheyDiffer()
    {
        var a = Claim("A", Rect(0, 0, 3, 3)); var b = Claim("B", Rect(4, 0, 7, 3));
        await LandAdmin.SetAccessModeAsync(_db, b, _owner, "private", _clock, default);
        Assert.Equal("primary_required", (await LandAdmin.MergeAsync(_db, [a, b], _owner, null, _clock, default)).Error);
        Assert.Equal("bad_primary", (await LandAdmin.MergeAsync(_db, [a, b], _owner, Guid.NewGuid(), _clock, default)).Error);
        Assert.True((await LandAdmin.MergeAsync(_db, [a, b], _owner, b, _clock, default)).Ok);
        Assert.Equal("private", Stored(b).AccessMode); Assert.NotNull(Stored(a).DeletedAt); Assert.Equal(32, CellsOf(b).Count);
    }

    [Fact]
    public async Task Merge_IsRefusedWhileARentalRuns()
    {
        var a = Claim("A", Rect(0, 0, 3, 3)); var b = Claim("B", Rect(4, 0, 7, 3));
        _db.ParcelRentals.Add(new ParcelRental { Id = Guid.NewGuid(), ParcelId = b, LandlordUserId = _owner, TenantUserId = _visitor, Status = "grace", StartsAt = _clock.Now, ExpiresAt = _clock.Now, CreatedAt = _clock.Now }); _db.SaveChanges();
        Assert.Equal("rented", (await LandAdmin.MergeAsync(_db, [a, b], _owner, null, _clock, default)).Error);
    }

    // ---- access ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Access_ModesListsAndTheirLimits()
    {
        var parcel = Claim("Casa", Rect(0, 0, 3, 3));
        Assert.Equal("forbidden", (await LandAdmin.SetAccessModeAsync(_db, parcel, _visitor, "private", _clock, default)).Error);
        Assert.Equal("bad_mode", (await LandAdmin.SetAccessModeAsync(_db, parcel, _owner, "secret", _clock, default)).Error);
        Assert.True((await LandAdmin.SetAccessModeAsync(_db, parcel, _owner, "list", _clock, default)).Ok); Assert.Equal("list", Stored(parcel).AccessMode);
        Assert.Equal("forbidden", (await LandAdmin.AddAccessAsync(_db, parcel, _visitor, _other, "ban", null, "", _clock, default)).Error);
        Assert.Equal("bad_kind", (await LandAdmin.AddAccessAsync(_db, parcel, _owner, _other, "mute", null, "", _clock, default)).Error);
        Assert.Equal("bad_target", (await LandAdmin.AddAccessAsync(_db, parcel, _owner, _owner, "ban", null, "", _clock, default)).Error);
        Assert.Equal("protected", (await LandAdmin.AddAccessAsync(_db, parcel, _owner, _admin, "ban", null, "", _clock, default)).Error);
        Assert.Equal("protected", (await LandAdmin.AddAccessAsync(_db, parcel, _owner, _island.OwnerUserId, "ban", null, "", _clock, default)).Error);
        Assert.Equal("bad_duration", (await LandAdmin.AddAccessAsync(_db, parcel, _owner, _other, "ban", 0, "", _clock, default)).Error);
        Assert.True((await LandAdmin.AddAccessAsync(_db, parcel, _owner, _visitor, "allow", null, "amigo", _clock, default)).Ok);
        Assert.True((await LandAdmin.AddAccessAsync(_db, parcel, _owner, _other, "ban", 30, "barulho", _clock, default)).Ok);
        var view = (await LandAdmin.AccessAsync(_db, parcel, _owner, _clock, default))!;
        Assert.Equal("list", view.Mode); Assert.Equal(2, view.Entries.Count);
        Assert.Null(await LandAdmin.AccessAsync(_db, parcel, _visitor, _clock, default));
        // a person is on one list at a time: banning an allowed person moves them
        await LandAdmin.AddAccessAsync(_db, parcel, _owner, _visitor, "ban", null, "", _clock, default);
        view = (await LandAdmin.AccessAsync(_db, parcel, _owner, _clock, default))!;
        Assert.Equal(2, view.Entries.Count); Assert.All(view.Entries, e => Assert.Equal("ban", e.Kind));
        // a ban with a time runs out by itself
        _clock.Now = _clock.Now.AddMinutes(31);
        Assert.Single((await LandAdmin.AccessAsync(_db, parcel, _owner, _clock, default))!.Entries);
        var entry = (await LandAdmin.AccessAsync(_db, parcel, _owner, _clock, default))!.Entries[0];
        Assert.Equal("forbidden", (await LandAdmin.RemoveAccessAsync(_db, parcel, _other, entry.Id, _clock, default)).Error);
        Assert.True((await LandAdmin.RemoveAccessAsync(_db, parcel, _owner, entry.Id, _clock, default)).Ok);
        Assert.Empty((await LandAdmin.AccessAsync(_db, parcel, _owner, _clock, default))!.Entries);
        Assert.Equal("not_found", (await LandAdmin.RemoveAccessAsync(_db, parcel, _owner, entry.Id, _clock, default)).Error);
    }

    [Fact]
    public async Task Eject_IsAOneMinuteBanAndIsRecorded()
    {
        var parcel = Claim("Casa", Rect(0, 0, 3, 3));
        Assert.True((await LandAdmin.EjectAsync(_db, parcel, _owner, _visitor, _clock, default)).Ok);
        var ban = _db.ParcelAccess.AsNoTracking().Single();
        Assert.Equal("ban", ban.Kind); Assert.Equal(_clock.Now.AddMinutes(1), ban.ExpiresAt);
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == parcel && c.Operation == "eject");
        Assert.Equal("forbidden", (await LandAdmin.EjectAsync(_db, parcel, _visitor, _other, _clock, default)).Error);
    }

    [Fact]
    public async Task TheViewMarksAClosedParcelAsRestrictedForThoseKeptOut()
    {
        var parcel = Claim("Casa", Rect(0, 0, 3, 3));
        Assert.Equal("owned", (await LandService.ViewAsync(_db, _island, _visitor, default)).Parcels.Single().State);
        await LandAdmin.SetAccessModeAsync(_db, parcel, _owner, "list", _clock, default);
        Assert.Equal("restricted", (await LandService.ViewAsync(_db, _island, _visitor, default)).Parcels.Single().State);
        await LandAdmin.AddAccessAsync(_db, parcel, _owner, _visitor, "allow", null, "", _clock, default);
        Assert.Equal("owned", (await LandService.ViewAsync(_db, _island, _visitor, default)).Parcels.Single().State);
        Assert.Equal("own", (await LandService.ViewAsync(_db, _island, _owner, default)).Parcels.Single().State);
        Assert.Equal("list", (await LandService.ViewAsync(_db, _island, _owner, default)).Parcels.Single().AccessMode);
    }

    // ---- what stands on the land -----------------------------------------------------------------------------

    [Fact]
    public async Task Objects_ListedForTheOwnerAndAdminsOnlyAndMarkedByWhoBelongsThere()
    {
        var parcel = Claim("Casa", Rect(0, 0, 3, 3));
        Put(_owner, 2, 2); Put(_visitor, 6, 6, "[{\"assetId\":\"x\",\"name\":\"s\",\"category\":\"scripts\"}]"); Put(_owner, 100, 100);
        Assert.Null(await LandAdmin.ObjectsAsync(_db, parcel, _visitor, _clock, default));
        var list = (await LandAdmin.ObjectsAsync(_db, parcel, _owner, _clock, default))!;
        Assert.Equal(2, list.Count);
        Assert.Single(list, o => o.HasScripts && o.OwnerUserId == _visitor && !o.Authorized); Assert.Single(list, o => o.Authorized && o.OwnerUserId == _owner);
        Assert.NotNull(await LandAdmin.ObjectsAsync(_db, parcel, _admin, _clock, default)); // island administrators too
        await LandAdmin.AddAccessAsync(_db, parcel, _owner, _visitor, "allow", null, "", _clock, default);
        Assert.All((await LandAdmin.ObjectsAsync(_db, parcel, _owner, _clock, default))!, o => Assert.True(o.Authorized));
    }

    [Fact]
    public async Task Return_SendsOthersObjectsToTheirInventoriesWithAReason()
    {
        var parcel = Claim("Casa", Rect(0, 0, 3, 3));
        var mine = Put(_owner, 2, 2); var stranger = Put(_visitor, 6, 6); var elsewhere = Put(_visitor, 100, 100); var allowed = Put(_other, 10, 10);
        await LandAdmin.AddAccessAsync(_db, parcel, _owner, _other, "allow", null, "", _clock, default);
        Assert.Equal("bad_reason", (await LandAdmin.ReturnObjectsAsync(_db, _storage, parcel, _owner, "others", null, "  ", _clock, default)).Error);
        Assert.Equal("forbidden", (await LandAdmin.ReturnObjectsAsync(_db, _storage, parcel, _other, "others", null, "x", _clock, default)).Error);
        Assert.Equal("bad_scope", (await LandAdmin.ReturnObjectsAsync(_db, _storage, parcel, _owner, "all", null, "x", _clock, default)).Error);
        var result = await LandAdmin.ReturnObjectsAsync(_db, _storage, parcel, _owner, "others", null, "sem permissão", _clock, default);
        Assert.True(result.Ok, result.Error);
        DateTimeOffset? Deleted(Guid id) => _db.BuildObjects.AsNoTracking().Single(o => o.Id == id).DeletedAt;
        Assert.NotNull(Deleted(stranger.Id)); Assert.Null(Deleted(mine.Id)); Assert.Null(Deleted(elsewhere.Id)); Assert.Null(Deleted(allowed.Id)); // only the unauthorized object, only on this parcel
        Assert.Contains(_db.InventoryItems.AsNoTracking(), i => i.OwnerUserId == _visitor && i.DisplayName.StartsWith("Devolvido"));
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == parcel && c.Operation == "return_objects");
        Assert.Equal("nothing_to_return", (await LandAdmin.ReturnObjectsAsync(_db, _storage, parcel, _owner, "others", null, "de novo", _clock, default)).Error);

        var again = await LandAdmin.ReturnObjectsAsync(_db, _storage, parcel, _owner, "ids", [mine.Id, elsewhere.Id], "limpeza", _clock, default); // the one far away is not on this parcel
        Assert.True(again.Ok); Assert.NotNull(Deleted(mine.Id)); Assert.Null(Deleted(elsewhere.Id));
        var audit = (await LandAdmin.AuditAsync(_db, parcel, _owner, default))!;
        Assert.Contains(audit, e => e.Operation == "return_objects"); Assert.Null(await LandAdmin.AuditAsync(_db, parcel, _visitor, default));
    }

    // ---- what the island servers are told --------------------------------------------------------------------

    [Fact]
    public async Task Sync_GivesOnlyTheClosedParcelsAndWhatHappenedToObjects()
    {
        var open = Claim("Aberta", Rect(0, 0, 3, 3)); var closed = Claim("Fechada", Rect(4, 0, 7, 3)); var banned = Claim("Com ban", Rect(8, 0, 11, 3));
        await LandAdmin.SetAccessModeAsync(_db, closed, _owner, "list", _clock, default);
        await LandAdmin.AddAccessAsync(_db, closed, _owner, _visitor, "allow", null, "", _clock, default);
        await LandAdmin.AddAccessAsync(_db, banned, _owner, _other, "ban", 10, "", _clock, default);
        var first = (await LandSync.BuildAsync(_db, _instance, 0, _clock, default))!;
        var json = JsonSerializer.SerializeToElement(first.Access, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(2, json.GetProperty("parcels").GetArrayLength()); Assert.Empty(first.Events);
        Assert.Null(await LandSync.BuildAsync(_db, Guid.NewGuid(), 0, _clock, default));

        // what the island server does with it
        var map = LandAccessMap.Parse(json); var nowMs = _clock.Now.ToUnixTimeMilliseconds();
        Assert.True(map.Denies(_other, 40, 8, nowMs)); // banned on the parcel with the ban
        Assert.False(map.Denies(_other, 10, 8, nowMs)); // the open parcel is not in the map
        Assert.True(map.Denies(_other, 20, 8, nowMs)); Assert.False(map.Denies(_visitor, 20, 8, nowMs)); Assert.False(map.Denies(_owner, 20, 8, nowMs)); Assert.False(map.Denies(_admin, 20, 8, nowMs));
        Assert.False(map.Denies(_other, 40, 8, nowMs + 11 * 60_000)); // the ban ran out

        // objects handed over or returned behind the island's back are announced
        var kept = Put(_owner, 2, 2); var sold = Put(_owner, 3, 3);
        await LandAdmin.ReturnObjectsAsync(_db, _storage, open, _owner, "ids", [kept.Id], "teste", _clock, default);
        _clock.Now = _clock.Now.AddSeconds(5);
        _db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = _island.Id, ObjectId = sold.Id, ActorUserId = _visitor, Operation = "land_transfer", PayloadJson = "{}", Revision = 2, CreatedAt = _clock.Now.AddSeconds(1) }); _db.SaveChanges();
        var next = (await LandSync.BuildAsync(_db, _instance, first.Cursor, _clock, default))!;
        // (the return above happened at the same instant as the first cursor, so look from the beginning of the day)
        next = (await LandSync.BuildAsync(_db, _instance, _clock.Now.AddHours(-1).UtcTicks, _clock, default))!;
        Assert.Contains(next.Events, e => e.GetProperty("operation").GetString() == "delete" && e.GetProperty("objectId").GetGuid() == kept.Id && e.GetProperty("returned").GetBoolean());
        Assert.Contains(next.Events, e => e.GetProperty("operation").GetString() == "update" && e.GetProperty("objectId").GetGuid() == sold.Id);
    }
}

public sealed class LandAccessMapTests
{
    private static readonly Guid Owner = Guid.NewGuid(), Tenant = Guid.NewGuid(), Guest = Guid.NewGuid(), Stranger = Guid.NewGuid(), Admin = Guid.NewGuid(), Banned = Guid.NewGuid();

    private static LandAccessMap Map(string mode, long? banUntil = null)
    {
        var json = $$"""
        {"admins":["{{Admin}}"],"parcels":[{"id":"{{Guid.NewGuid()}}","mode":"{{mode}}","owner":"{{Owner}}","tenant":"{{Tenant}}","allow":["{{Guest}}"],"bans":[{"userId":"{{Banned}}","untilMs":{{(banUntil is null ? "null" : banUntil.ToString())}}}],"runs":[[0,0,3],[1,0,3]]}]}
        """;
        return LandAccessMap.Parse(JsonDocument.Parse(json).RootElement);
    }

    [Theory]
    [InlineData("public", false, false)]
    [InlineData("list", false, true)]
    [InlineData("private", true, true)]
    public void ModesDecideWhoIsKeptOut(string mode, bool guestDenied, bool strangerDenied)
    {
        var map = Map(mode);
        Assert.False(map.Denies(Owner, 2, 2, 0)); Assert.False(map.Denies(Tenant, 2, 2, 0)); Assert.False(map.Denies(Admin, 2, 2, 0));
        Assert.Equal(guestDenied, map.Denies(Guest, 2, 2, 0));
        Assert.Equal(strangerDenied, map.Denies(Stranger, 2, 2, 0));
    }

    [Fact]
    public void ABanBeatsEverythingExceptTheOwnerAndLastsUntilItsTime()
    {
        var map = Map("public", banUntil: 1000);
        Assert.True(map.Denies(Banned, 2, 2, 999)); Assert.False(map.Denies(Banned, 2, 2, 1000)); Assert.False(map.Denies(Banned, 2, 2, 5000));
        Assert.True(Map("public", banUntil: null).Denies(Banned, 2, 2, long.MaxValue / 2)); // for ever
        Assert.False(map.Denies(Banned, 100, 100, 0)); // only inside the parcel
        Assert.True(Map("list").Denies(Banned, 2, 2, 0));
    }

    [Fact]
    public void ForbiddenPeopleAreMovedToTheNearestAllowedSpot()
    {
        var map = Map("private");
        var spot = map.NearestAllowed(Stranger, 6, 2, 0)!.Value; // the parcel covers x 0..16, z 0..8
        Assert.False(map.Denies(Stranger, spot.X, spot.Z, 0));
        Assert.True(Math.Abs(spot.X - 6) < 12 && Math.Abs(spot.Z - 2) < 12, $"close to where they were: {spot}");
        Assert.False(LandAccessMap.Empty.Denies(Stranger, 0, 0, 0)); Assert.False(LandAccessMap.Empty.HasZones);
    }

    [Fact]
    public void DamagedEntriesAreSkipped()
    {
        var map = LandAccessMap.Parse(JsonDocument.Parse("""{"parcels":[{"id":"nope"},{"owner":1},{"id":"00000000-0000-0000-0000-000000000001","owner":"00000000-0000-0000-0000-000000000002","runs":[[0,0,0]]}]}""").RootElement);
        Assert.Equal(1, map.Zones);
        Assert.False(LandAccessMap.Parse(JsonDocument.Parse("[]").RootElement).HasZones);
    }
}
