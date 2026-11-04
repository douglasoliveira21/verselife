using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class LandGridTests
{
    [Fact]
    public void Cells_AreFourMetresAndKeysRoundTripEvenWhenNegative()
    {
        Assert.Equal(0, LandGrid.CellOf(3.99)); Assert.Equal(1, LandGrid.CellOf(4)); Assert.Equal(-1, LandGrid.CellOf(-0.01)); Assert.Equal(-2, LandGrid.CellOf(-4.01));
        foreach (var (cx, cz) in new[] { (0, 0), (-5, 7), (31, -32), (int.MinValue / 2, int.MaxValue / 2) }) Assert.Equal((cx, cz), LandGrid.Unkey(LandGrid.Key(cx, cz)));
    }

    [Fact]
    public void RectangleBecomesTheCellsWhoseCentreIsInside()
    {
        var cells = LandGrid.CellsInRect(0, 0, 8, 8);
        Assert.Equal(4, cells.Count);
        Assert.Equal(64, LandGrid.AreaOf(cells.Count));
        Assert.Single(LandGrid.CellsInRect(0, 0, 5, 5)); // only the cell centred at (2, 2): the next centre, 6, is outside
        Assert.Empty(LandGrid.CellsInRect(0, 0, 1, 1));
        Assert.Empty(LandGrid.CellsInRect(5, 5, 5, 9));
    }

    [Fact]
    public void Parcel_MustBeOnePieceAndCornersDoNotConnect()
    {
        Assert.True(LandGrid.IsContiguous(new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(1, 0), LandGrid.Key(1, 1) }));
        Assert.False(LandGrid.IsContiguous(new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(1, 1) }), "touching only at a corner is not one piece");
        Assert.False(LandGrid.IsContiguous(new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(5, 5) }));
        Assert.False(LandGrid.IsContiguous(new HashSet<long>()));
        Assert.True(LandGrid.IsContiguous(new HashSet<long> { LandGrid.Key(3, 3) }));
    }

    [Fact]
    public void Islands_OnlyHoldWholeCells()
    {
        Assert.True(LandGrid.InsideIsland(-32, -32, 256)); Assert.True(LandGrid.InsideIsland(31, 31, 256));
        Assert.False(LandGrid.InsideIsland(32, 0, 256)); Assert.False(LandGrid.InsideIsland(-33, 0, 256));
        Assert.False(LandGrid.InsideIsland(31, 0, 250), "a cell that sticks out of the island is not inside it");
    }

    [Fact]
    public void Runs_AreCompactAndRebuildTheSameCells()
    {
        var cells = new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(1, 0), LandGrid.Key(2, 0), LandGrid.Key(5, 0), LandGrid.Key(0, 1), LandGrid.Key(-3, 4) };
        var runs = LandGrid.Runs(cells);
        Assert.Equal(4, runs.Length);
        Assert.Equal(cells, LandGrid.FromRuns(runs));
        Assert.Equal((-12d, 0d, 24d, 20d), LandGrid.Bounds(cells));
    }
}

public sealed class LandServiceTests : IDisposable
{
    private readonly DbContextOptions<LifeVerseDbContext> _options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly LifeVerseDbContext _db;
    private readonly Island _island;
    private readonly Guid _owner = Guid.NewGuid(), _buyer = Guid.NewGuid();

    public LandServiceTests()
    {
        _db = new LifeVerseDbContext(_options);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HashSet<long> Rect(int cx0, int cz0, int cx1, int cz1) { var set = new HashSet<long>(); for (var x = cx0; x <= cx1; x++) for (var z = cz0; z <= cz1; z++) set.Add(LandGrid.Key(x, z)); return set; }

    private Task<ClaimResult> Claim(string name, HashSet<long> cells, Guid? by = null, int max = 100) =>
        LandService.ClaimAsync(_db, _island, by ?? _owner, name, "descrição", cells, max, null, null, null, null, null, TimeProvider.System, default);

    [Fact]
    public async Task Claim_CreatesTheParcelWithItsCellsAreaAndBounds()
    {
        var result = await Claim("Praça", Rect(0, 0, 3, 1));
        Assert.Null(result.Error); var parcel = result.Parcel!;
        Assert.Equal(8 * 16, parcel.Area); Assert.Equal(_owner, parcel.OwnerUserId);
        using var bounds = JsonDocument.Parse(parcel.BoundsJson);
        Assert.Equal(16, bounds.RootElement.GetProperty("maxX").GetDouble()); Assert.Equal(8, bounds.RootElement.GetProperty("maxZ").GetDouble());
        Assert.Equal(8, await _db.ParcelCells.CountAsync(c => c.ParcelId == parcel.Id));
        Assert.Contains(await _db.ParcelChanges.ToListAsync(), c => c.ParcelId == parcel.Id && c.Operation == "claim");
    }

    [Fact]
    public async Task TwoParcelsCanNeverShareACell()
    {
        var first = await Claim("A", Rect(0, 0, 3, 3));
        var second = await Claim("B", Rect(3, 3, 6, 6)); // shares the cell (3, 3)
        Assert.Equal("overlap", second.Error); Assert.Equal(first.Parcel!.Id, second.ConflictParcelId);
        Assert.Equal(16, await _db.ParcelCells.CountAsync());
        Assert.Null((await Claim("C", Rect(4, 0, 6, 3))).Error); // next to it is fine
    }

    [Theory]
    [InlineData("", "bad_name")]
    [InlineData("   ", "bad_name")]
    public async Task Claim_NeedsAName(string name, string error) => Assert.Equal(error, (await Claim(name, Rect(0, 0, 1, 1))).Error);

    [Fact]
    public async Task Claim_RefusesShapesThatAreNotValid()
    {
        Assert.Equal("empty", (await Claim("Vazio", [])).Error);
        Assert.Equal("not_contiguous", (await Claim("Duas", new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(2, 0) })).Error);
        Assert.Equal("not_contiguous", (await Claim("Quina", new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(1, 1) })).Error);
        Assert.Equal("outside_island", (await Claim("Fora", Rect(31, 0, 32, 0))).Error);
        Assert.Equal("bad_limit", (await Claim("Limite", Rect(0, 0, 1, 1), max: 0)).Error);
        Assert.Equal(0, await _db.ParcelCells.CountAsync());
        Assert.Null((await Claim("L", new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(1, 0), LandGrid.Key(1, 1) })).Error); // an irregular shape is fine
    }

    [Fact]
    public async Task Claim_NamesAreUniquePerIsland()
    {
        Assert.Null((await Claim("Loja", Rect(0, 0, 1, 1))).Error);
        Assert.Equal("name_taken", (await Claim("Loja", Rect(5, 5, 6, 6))).Error);
    }

    [Fact]
    public async Task Release_FreesTheCellsForAnotherParcel()
    {
        var parcel = (await Claim("A", Rect(0, 0, 3, 3))).Parcel!;
        await LandService.ReleaseAsync(_db, parcel.Id, default);
        Assert.Equal(0, await _db.ParcelCells.CountAsync());
        Assert.Null((await Claim("B", Rect(0, 0, 3, 3))).Error);
    }

    [Fact]
    public async Task Facts_UseTheCellsSoAnIrregularParcelDoesNotClaimItsBoundingBox()
    {
        var shape = new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(1, 0), LandGrid.Key(0, 1) }; // an L
        var parcel = (await Claim("L", shape)).Parcel!;
        var facts = (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single(f => f.Id == parcel.Id);
        Assert.True(facts.Contains(1, 1)); Assert.True(facts.Contains(5, 1)); Assert.True(facts.Contains(1, 5));
        Assert.False(facts.Contains(5, 5), "the empty corner of the box is not part of the parcel");
        Assert.Same(facts, BuildRules.ParcelAt([facts], 1, 1));
        Assert.Null(BuildRules.ParcelAt([facts], 5, 5));
    }

    [Fact]
    public async Task Usage_CountsOnlyObjectsOnTheCellsOfTheParcel()
    {
        var parcel = (await Claim("L", new HashSet<long> { LandGrid.Key(0, 0), LandGrid.Key(1, 0), LandGrid.Key(0, 1) })).Parcel!;
        BuildObject Object(float x, float z, int cost) => new() { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "o", PrimitiveType = "cube", PositionX = x, PositionZ = z, Cost = cost };
        _db.BuildObjects.AddRange(Object(1, 1, 3), Object(5, 1, 4), Object(5, 5, 100), Object(50, 50, 1000)); await _db.SaveChangesAsync();
        var facts = (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single();
        Assert.Equal((2, 7), await LandService.UsageAsync(_db, _island.Id, facts, Guid.Empty, default));
    }

    [Fact]
    public async Task View_ShowsOwnersStatesAndTheCellsInRows()
    {
        var mine = (await Claim("Minha", Rect(0, 0, 3, 3))).Parcel!;
        var theirs = (await Claim("Dela", Rect(4, 0, 7, 3), _buyer)).Parcel!;
        var closed = (await Claim("Fechada", Rect(8, 0, 9, 3), _buyer)).Parcel!; closed.PermissionsJson = "{\"enter\":false}";
        var open = (await Claim("Aberta", Rect(10, 0, 11, 3), _buyer)).Parcel!; open.PermissionsJson = "{\"enter\":true,\"build\":true}"; await _db.SaveChangesAsync();
        var view = await LandService.ViewAsync(_db, _island, _owner, default);
        Assert.Equal(4, view.CellSize); Assert.Equal(256, view.Size); Assert.True(view.Version > 0);
        string State(string name) => view.Parcels.Single(p => p.Name == name).State;
        Assert.Equal("own", State("Minha")); Assert.Equal("owned", State("Dela")); Assert.Equal("restricted", State("Fechada")); Assert.Equal("public", State("Aberta"));
        var m = view.Parcels.Single(p => p.Id == mine.Id);
        Assert.Equal(16, m.Cells); Assert.Equal(256, m.Area); Assert.Equal(4, m.Runs.Length); Assert.True(m.Mine);
        Assert.Equal(m.MaxObjects * ParcelFacts.CostPerObjectSlot, m.MaxCost);
        Assert.False(view.Parcels.Single(p => p.Id == theirs.Id).Mine);
    }

    [Fact]
    public async Task Backfill_GivesLegacyRectanglesTheirCellsInOrderAndSkipsWhatIsTaken()
    {
        IslandParcel Legacy(string name, double min, double max, int minutesAgo) => new() { Id = Guid.NewGuid(), IslandId = _island.Id, OwnerUserId = _owner, Name = name, BoundsJson = JsonSerializer.Serialize(new { minX = min, minZ = min, maxX = max, maxZ = max }), CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo) };
        var older = Legacy("Antiga", 0, 16, 10); var newer = Legacy("Sobreposta", 8, 24, 5);
        _db.IslandParcels.AddRange(older, newer); await _db.SaveChangesAsync();
        Assert.Equal(2, await ParcelCellBackfill.RunAsync(_db));
        Assert.Equal(16, await _db.ParcelCells.CountAsync(c => c.ParcelId == older.Id));
        Assert.Equal(16 - 4, await _db.ParcelCells.CountAsync(c => c.ParcelId == newer.Id)); // the 2 x 2 cells already taken stay with the older parcel
        Assert.Equal(0, await ParcelCellBackfill.RunAsync(_db));
    }
}

public sealed class LandParityTests
{
    [Fact]
    public void ClientAndServerAgreeOnCellsKeysAndRuns()
    {
        Assert.Equal(LandGrid.CellSize, LifeVerse.Shared.Land.LandMath.CellSize);
        foreach (var (cx, cz) in new[] { (0, 0), (-5, 7), (31, -32), (-1, -1) }) Assert.Equal(LandGrid.Key(cx, cz), LifeVerse.Shared.Land.LandMath.Key(cx, cz));
        foreach (var metres in new[] { 0d, 3.99, 4, -0.01, -4.01, 127.9 }) Assert.Equal(LandGrid.CellOf(metres), LifeVerse.Shared.Land.LandMath.CellOf(metres));
        var cells = LandGrid.CellsInRect(-20, -8, 12, 16);
        Assert.Equal(cells, LifeVerse.Shared.Land.LandMath.FromRuns(LandGrid.Runs(cells)));
        foreach (var size in new[] { 250, 256, 512 }) for (var cx = -70; cx < 70; cx += 7) Assert.Equal(LandGrid.InsideIsland(cx, 3, size), LifeVerse.Shared.Land.LandMath.InsideIsland(cx, 3, size));
    }
}

public sealed class LandMathTests
{
    private static HashSet<long> Cells(params (int, int)[] cells) => [.. cells.Select(c => LifeVerse.Shared.Land.LandMath.Key(c.Item1, c.Item2))];

    [Fact]
    public void Dragging_SelectsEveryCellTheRectangleTouchesInAnyDirection()
    {
        var a = LifeVerse.Shared.Land.LandMath.Between(1, 1, 9, 5); var b = LifeVerse.Shared.Land.LandMath.Between(9, 5, 1, 1);
        Assert.Equal(a, b); Assert.Equal(3 * 2, a.Count);
        Assert.Single(LifeVerse.Shared.Land.LandMath.Between(1, 1, 2, 2));
        Assert.Equal(2, LifeVerse.Shared.Land.LandMath.Between(-1, 1, 1, 1).Count); // crosses x = 0
    }

    [Fact]
    public void Boundary_OfARectangleIsFourJoinedEdges()
    {
        var edges = LifeVerse.Shared.Land.LandMath.Boundary(Cells((0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)));
        Assert.Equal(4, edges.Count);
        Assert.Contains(new LifeVerse.Shared.Land.LandEdge(0, 0, 3, 0), edges); Assert.Contains(new LifeVerse.Shared.Land.LandEdge(0, 2, 3, 2), edges);
        Assert.Contains(new LifeVerse.Shared.Land.LandEdge(0, 0, 0, 2), edges); Assert.Contains(new LifeVerse.Shared.Land.LandEdge(3, 0, 3, 2), edges);
    }

    [Fact]
    public void Boundary_OfAnLShapeHasSixEdgesAndAHoleHasItsOwn()
    {
        Assert.Equal(6, LifeVerse.Shared.Land.LandMath.Boundary(Cells((0, 0), (1, 0), (0, 1))).Count);
        var ring = Cells((0, 0), (1, 0), (2, 0), (0, 1), (2, 1), (0, 2), (1, 2), (2, 2));
        Assert.Equal(8, LifeVerse.Shared.Land.LandMath.Boundary(ring).Count); // outer square (4) and the hole (4)
        Assert.Empty(LifeVerse.Shared.Land.LandMath.Boundary(new HashSet<long>()));
    }

    [Fact]
    public void Extent_AndArea()
    {
        Assert.Equal((-1, 0, 2, 3), LifeVerse.Shared.Land.LandMath.Extent(Cells((-1, 0), (2, 3), (0, 1)))!.Value);
        Assert.Null(LifeVerse.Shared.Land.LandMath.Extent([]));
        Assert.Equal(48, LifeVerse.Shared.Land.LandMath.AreaOf(3));
    }
}
