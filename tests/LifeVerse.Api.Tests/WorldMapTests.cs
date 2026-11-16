using System.IO.Compression;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Map;
using LifeVerse.Shared.Terrain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The base of the world map: the atlas (where each island stands), what each viewer may see and do, and the pictures from above.</summary>
public sealed class WorldMapTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner, _viewer, _other;

    public WorldMapTests() => (_owner, _viewer, _other) = (User("dona"), User("visitante"), User("outra"));

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = id, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id;
    }

    private Island Island(string name, bool isPublic = true, string category = "social", string rating = "livre", int minutes = 0, bool welcome = false, Guid? owner = null)
    {
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = owner ?? _owner, Name = name, Slug = Guid.NewGuid().ToString("N")[..12], IsPublic = isPublic, ShowInSearch = true, ShowOnMap = true, Category = category, ContentRating = rating, IsWelcome = welcome, MaxPlayers = 2, Size = 256, CreatedAt = _now.AddMinutes(minutes), UpdatedAt = _now };
        _db.Islands.Add(island); _db.SaveChanges(); return island;
    }

    private async Task<JsonElement> Atlas(Guid viewer, AtlasQuery? q = null) => JsonSerializer.SerializeToElement(await MapService.AtlasAsync(_db, viewer, q ?? new AtlasQuery(), _now, default));
    private static List<string> Names(JsonElement atlas) => [.. atlas.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!)];
    private static JsonElement Item(JsonElement atlas, string name) => atlas.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == name);

    // ---- the grid ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheSpiralStartsAtTheCentreNeverRepeatsAndNeverJumps()
    {
        Assert.Equal((0, 0), MapService.SpiralCell(0)); Assert.Equal((1, 0), MapService.SpiralCell(1)); Assert.Equal((1, 1), MapService.SpiralCell(2)); Assert.Equal((0, 1), MapService.SpiralCell(3));
        var cells = Enumerable.Range(0, 500).Select(MapService.SpiralCell).ToList(); Assert.Equal(500, cells.Distinct().Count());
        for (var i = 1; i < cells.Count; i++) Assert.Equal(1, Math.Abs(cells[i].X - cells[i - 1].X) + Math.Abs(cells[i].Y - cells[i - 1].Y));
    }

    [Fact]
    public async Task EveryIslandGetsItsOwnCellOnceAndTheWelcomeIslandTheCentre()
    {
        var a = Island("Antiga", minutes: 0); var b = Island("Recente", minutes: 10); var welcome = Island("Boas-vindas", minutes: 20, welcome: true);
        Assert.Equal(3, await MapService.EnsureAtlasAsync(_db, default)); Assert.Equal(0, await MapService.EnsureAtlasAsync(_db, default)); // nothing left to give the second time
        var cells = await _db.Islands.AsNoTracking().Select(i => new { i.Name, i.AtlasX, i.AtlasY }).ToListAsync();
        Assert.Equal(3, cells.Select(c => (c.AtlasX, c.AtlasY)).Distinct().Count()); var centre = cells.Single(c => c.Name == "Boas-vindas"); Assert.Equal((0, 0), (centre.AtlasX, centre.AtlasY));
        var before = cells.ToDictionary(c => c.Name, c => (c.AtlasX, c.AtlasY));
        Island("Nova", minutes: 30); await MapService.EnsureAtlasAsync(_db, default);
        var after = await _db.Islands.AsNoTracking().ToDictionaryAsync(i => i.Name, i => (i.AtlasX, i.AtlasY));
        Assert.All(before, kv => Assert.Equal(kv.Value, after[kv.Key])); // the ones already placed never move
        Assert.Equal(4, after.Values.Distinct().Count()); _ = a; _ = b;
    }

    [Fact]
    public async Task ACellOfADeletedIslandIsNotGivenAgain()
    {
        var gone = Island("Apagada"); await MapService.EnsureAtlasAsync(_db, default); var cell = (gone.AtlasX, gone.AtlasY);
        (await _db.Islands.SingleAsync(i => i.Id == gone.Id)).DeletedAt = _now; await _db.SaveChangesAsync();
        var fresh = Island("Depois", minutes: 5); await MapService.EnsureAtlasAsync(_db, default);
        var placed = await _db.Islands.AsNoTracking().SingleAsync(i => i.Id == fresh.Id); Assert.NotEqual(cell, (placed.AtlasX, placed.AtlasY));
    }

    // ---- who sees what ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheAtlasListsOnlyWhatTheViewerMayKnowExists()
    {
        Island("Publica"); Island("Reservada", isPublic: false); var hidden = Island("Escondida"); hidden.ShowOnMap = false; var deleted = Island("Apagada"); deleted.DeletedAt = _now; await _db.SaveChangesAsync();
        Assert.Equal(["Publica"], Names(await Atlas(_viewer)));
        Assert.Equal(["Escondida", "Publica", "Reservada"], Names(await Atlas(_owner)).OrderBy(n => n)); // the owner sees their own, public or not
        var secret = await _db.Islands.SingleAsync(i => i.Name == "Reservada");
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = secret.Id, UserId = _viewer, PrincipalType = "user", Permission = "enter", CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Contains("Reservada", Names(await Atlas(_viewer))); Assert.DoesNotContain("Reservada", Names(await Atlas(_other))); // invited people see it
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _other, BlockedUserId = _owner, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.DoesNotContain("Publica", Names(await Atlas(_other))); // nor the islands of somebody who is blocked with you
    }

    [Fact]
    public async Task FiltersLimitAndBoundsApply()
    {
        Island("Praia Azul", category: "social"); Island("Loja Central", category: "comercial"); Island("Bar Adulto", category: "social", rating: "adulto");
        var all = await Atlas(_viewer); Assert.Equal(3, all.GetProperty("total").GetInt32()); Assert.False(all.GetProperty("truncated").GetBoolean());
        Assert.Equal(["Loja Central"], Names(await Atlas(_viewer, new AtlasQuery(Category: "COMERCIAL")))); Assert.Equal(["Praia Azul"], Names(await Atlas(_viewer, new AtlasQuery(Q: "praia"))));
        Assert.Equal(["Bar Adulto"], Names(await Atlas(_viewer, new AtlasQuery(Ratings: ["adulto"])))); Assert.Equal(2, (await Atlas(_viewer, new AtlasQuery(Ratings: ["livre", "moderado"]))).GetProperty("total").GetInt32());
        var one = await Atlas(_viewer, new AtlasQuery(Limit: 1)); Assert.Single(one.GetProperty("items").EnumerateArray()); Assert.True(one.GetProperty("truncated").GetBoolean()); Assert.Equal(3, one.GetProperty("total").GetInt32());
        var centre = all.GetProperty("items").EnumerateArray().First(i => i.GetProperty("cellX").GetInt32() == 0 && i.GetProperty("cellY").GetInt32() == 0).GetProperty("name").GetString();
        Assert.Equal(new List<string> { centre! }, Names(await Atlas(_viewer, new AtlasQuery(MinCellX: 0, MaxCellX: 0, MinCellY: 0, MaxCellY: 0))));
        Assert.Empty(Names(await Atlas(_viewer, new AtlasQuery(MinCellX: 50, MaxCellX: 60))));
    }

    [Fact]
    public async Task EachIslandCarriesItsPlaceInMetersAndTheOwnerOnlyWhenPublic()
    {
        var pub = Island("Publica"); var secret = Island("Reservada", isPublic: false); await MapService.EnsureAtlasAsync(_db, default);
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = secret.Id, UserId = _viewer, PrincipalType = "user", Permission = "enter", CreatedAt = _now }); await _db.SaveChangesAsync();
        var atlas = await Atlas(_viewer); var p = Item(atlas, "Publica"); var placed = await _db.Islands.AsNoTracking().SingleAsync(i => i.Id == pub.Id);
        Assert.Equal(placed.AtlasX!.Value * (double)MapService.CellMeters, p.GetProperty("x").GetDouble()); Assert.Equal(placed.AtlasY!.Value * (double)MapService.CellMeters, p.GetProperty("y").GetDouble()); Assert.Equal("dona", p.GetProperty("owner").GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, Item(atlas, "Reservada").GetProperty("owner").ValueKind); // a private island does not tell who owns it
    }

    // ---- what this viewer can do --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AccessSaysOpenFullClosedPrivateOrBanned()
    {
        var island = Island("Ilha"); Assert.Equal("open", Item(await Atlas(_viewer), "Ilha").GetProperty("access").GetString());
        Assert.False(Item(await Atlas(_viewer), "Ilha").GetProperty("online").GetBoolean());
        _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = island.Id, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now, LastHeartbeatAt = _now, PlayerCount = 2 }); await _db.SaveChangesAsync();
        var full = Item(await Atlas(_viewer), "Ilha"); Assert.Equal("full", full.GetProperty("access").GetString()); Assert.Equal(2, full.GetProperty("visitors").GetInt32()); Assert.True(full.GetProperty("online").GetBoolean());
        Assert.Equal("open", Item(await Atlas(_owner), "Ilha").GetProperty("access").GetString()); // the owner always comes in
        _db.ServerInstances.RemoveRange(_db.ServerInstances); island.AccessClosed = true; await _db.SaveChangesAsync(); Assert.Equal("closed", Item(await Atlas(_viewer), "Ilha").GetProperty("access").GetString());
        island.AccessClosed = false; _db.IslandBans.Add(new IslandBan { Id = Guid.NewGuid(), IslandId = island.Id, UserId = _viewer, BannedByUserId = _owner, Reason = "x", CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal("banned", Item(await Atlas(_viewer), "Ilha").GetProperty("access").GetString()); Assert.Equal("open", Item(await Atlas(_other), "Ilha").GetProperty("access").GetString());
    }

    // ---- the picture ---------------------------------------------------------------------------------------------------------------------

    private static string Terrain(Action<TerrainMap>? shape = null)
    {
        var map = new TerrainMap(32, 2f); shape?.Invoke(map);
        return JsonSerializer.Serialize(new { data = Convert.ToBase64String(map.Serialize()) });
    }

    private void Save(Island island, long version, string json) { _db.IslandSnapshots.Add(new IslandSnapshot { Id = Guid.NewGuid(), IslandId = island.Id, Version = version, TerrainJson = json, EnvironmentJson = "{}", IntegrityHash = "h", CreatedAt = _now }); _db.SaveChanges(); }

    [Fact]
    public void ThePngWriterMakesAValidFile()
    {
        var rgb = new byte[4 * 3 * 3]; for (var i = 0; i < rgb.Length; i++) rgb[i] = (byte)(i * 7);
        var png = Png.Encode(4, 3, rgb);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        var at = 8; var types = new List<string>(); byte[] data = [];
        while (at < png.Length)
        {
            var length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at)); var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4); types.Add(type);
            if (type == "IHDR") { Assert.Equal(4u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8))); Assert.Equal(3u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 12))); }
            if (type == "IDAT") data = png[(at + 8)..(at + 8 + length)];
            at += 12 + length;
        }
        Assert.Equal(["IHDR", "IDAT", "IEND"], types);
        using var inflated = new MemoryStream(); using (var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress)) z.CopyTo(inflated);
        Assert.Equal(3 * (4 * 3 + 1), inflated.Length); Assert.Equal(0, inflated.ToArray()[0]); Assert.Equal(rgb[..12], inflated.ToArray()[1..13]);
        Assert.Throws<ArgumentException>(() => Png.Encode(2, 2, new byte[5]));
    }

    [Fact]
    public void TheRendererDrawsWaterLandAndBuildings()
    {
        var map = new TerrainMap(32, 2f);
        var (wr, wg, wb) = MapRenderer.Terrain(map, 1, 1); Assert.True(wb > wr && wb > wg, $"water is blue: {wr},{wg},{wb}");
        var (lr, lg, lb) = MapRenderer.Terrain(map, 16, 16); Assert.True(lg > lb, $"land is green or brown: {lr},{lg},{lb}");
        var plain = MapRenderer.Render(map, [], 64); var built = MapRenderer.Render(map, [new Footprint(20, 20, 10, 10, 0)], 64);
        var centre = ((int)((20 + 32) / 64f * 64) * 64 + (int)((20 + 32) / 64f * 64)) * 3;
        Assert.NotEqual(plain[centre], built[centre]); Assert.Equal(0xe8, built[centre]); Assert.Equal(64 * 64 * 3, plain.Length);
    }

    [Fact]
    public async Task ATileExistsOnlyWithSavedTerrainAndIsKeptUntilSomethingChanges()
    {
        var island = Island("Ilha"); Assert.Null(await MapService.TileAsync(_db, _viewer, island.Id, _now, default)); // nothing saved yet
        Save(island, 1, Terrain());
        var first = await MapService.TileAsync(_db, _viewer, island.Id, _now, default); Assert.NotNull(first); Assert.Equal(0x89, first!.Png[0]);
        var again = await MapService.TileAsync(_db, _viewer, island.Id, _now.AddMinutes(10), default); Assert.Equal(first.Etag, again!.Etag); Assert.Equal(1, await _db.MapTiles.CountAsync()); // not made twice
        Save(island, 2, Terrain(m => m.Apply(TerrainTool.Raise, 0, 0, 10, 1f)));
        var soon = await MapService.TileAsync(_db, _viewer, island.Id, _now.AddSeconds(30), default); Assert.Equal(first.Etag, soon!.Etag); // changed, but made a moment ago: the old picture does
        var later = await MapService.TileAsync(_db, _viewer, island.Id, _now.AddMinutes(2), default); Assert.NotEqual(first.Etag, later!.Etag);
    }

    [Fact]
    public async Task ABuildingChangesThePictureAndAPrivateIslandHasNoneForStrangers()
    {
        var island = Island("Ilha"); Save(island, 1, Terrain()); var first = await MapService.TileAsync(_db, _viewer, island.Id, _now, default);
        _db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Casa", PrimitiveType = "box", ScaleX = 8, ScaleY = 4, ScaleZ = 8, CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        var built = await MapService.TileAsync(_db, _viewer, island.Id, _now.AddMinutes(2), default); Assert.NotEqual(first!.Etag, built!.Etag);
        island.IsPublic = false; await _db.SaveChangesAsync(); Assert.Null(await MapService.TileAsync(_db, _other, island.Id, _now.AddMinutes(3), default)); Assert.NotNull(await MapService.TileAsync(_db, _owner, island.Id, _now.AddMinutes(3), default));
    }

[Fact]    public async Task TheAtlasCountsWhoLikedEachIslandAndTellsWhenItWasCreated()    {        var popular = Island("Popular", minutes: 5); Island("Calma", minutes: 6);        foreach (var who in new[] { _viewer, _other, _owner }) _db.IslandFavorites.Add(new IslandFavorite { Id = Guid.NewGuid(), UserId = who, IslandId = popular.Id, CreatedAt = _now }); await _db.SaveChangesAsync();        var atlas = await Atlas(_viewer); Assert.Equal(3, Item(atlas, "Popular").GetProperty("favorites").GetInt32()); Assert.Equal(0, Item(atlas, "Calma").GetProperty("favorites").GetInt32());        Assert.True(Item(atlas, "Popular").GetProperty("favorite").GetBoolean()); Assert.False(Item(atlas, "Calma").GetProperty("favorite").GetBoolean());        Assert.Equal(_now.AddMinutes(5), Item(atlas, "Popular").GetProperty("createdAt").GetDateTimeOffset());    }
    [Fact]
    public async Task TheAtlasTellsWhichIslandsHaveAPicture()
    {
        var a = Island("Com mapa"); Island("Sem mapa"); Save(a, 1, Terrain()); await MapService.TileAsync(_db, _viewer, a.Id, _now, default);
        var atlas = await Atlas(_viewer); Assert.True(Item(atlas, "Com mapa").GetProperty("hasTile").GetBoolean()); Assert.False(Item(atlas, "Sem mapa").GetProperty("hasTile").GetBoolean());
        Assert.False(string.IsNullOrEmpty(Item(atlas, "Com mapa").GetProperty("tileVersion").GetString()));
    }
}
