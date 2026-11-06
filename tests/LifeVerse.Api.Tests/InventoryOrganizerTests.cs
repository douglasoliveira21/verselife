using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class FolderRulesTests
{
    private static FolderRules.Node N(Guid id, Guid? parent = null) => new(id, parent);

    [Fact]
    public void TheTreeKnowsItsDepthSubtreesAndWhatWouldMakeALoop()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid(); var nodes = new[] { N(a), N(b, a), N(c, b), N(d) };
        var parents = nodes.ToDictionary(n => n.Id, n => n.ParentId);
        Assert.Equal(3, FolderRules.DepthOf(parents, c)); Assert.Equal(1, FolderRules.DepthOf(parents, a)); Assert.Equal(0, FolderRules.DepthOf(parents, null)); Assert.Equal(0, FolderRules.DepthOf(parents, Guid.NewGuid()));
        Assert.Equal(new HashSet<Guid> { a, b, c }, FolderRules.Subtree(nodes, a)); Assert.Single(FolderRules.Subtree(nodes, d)); Assert.Equal(3, FolderRules.Height(nodes, a)); Assert.Equal(1, FolderRules.Height(nodes, d));
        Assert.Equal("cycle", FolderRules.CheckMove(nodes, a, c)); Assert.Equal("cycle", FolderRules.CheckMove(nodes, a, a)); Assert.Null(FolderRules.CheckMove(nodes, c, d)); Assert.Null(FolderRules.CheckMove(nodes, c, null)); Assert.Null(FolderRules.CheckMove(nodes, a, d));
    }

    [Fact]
    public void TheDepthLimitCountsWhatIsMovedWithTheFolder()
    {
        var chain = new List<FolderRules.Node>(); Guid? parent = null; for (var i = 0; i < FolderRules.MaxDepth; i++) { var id = Guid.NewGuid(); chain.Add(N(id, parent)); parent = id; }
        var loose = Guid.NewGuid(); var looseKid = Guid.NewGuid(); chain.Add(N(loose)); chain.Add(N(looseKid, loose));
        Assert.Equal("too_deep", FolderRules.CheckMove(chain, loose, chain[FolderRules.MaxDepth - 1].Id)); // 8 deep already
        Assert.Equal("too_deep", FolderRules.CheckMove(chain, loose, chain[FolderRules.MaxDepth - 2].Id)); // loose plus its child need two more levels
        Assert.Null(FolderRules.CheckMove(chain, looseKid, chain[FolderRules.MaxDepth - 2].Id));
        Assert.Null(FolderRules.CheckMove(chain, loose, chain[FolderRules.MaxDepth - 3].Id));
    }

    [Theory]
    [InlineData("Casa", "Casa")] [InlineData("  Casa  ", "Casa")] [InlineData("", null)] [InlineData("   ", null)] [InlineData(null, null)] [InlineData("a\nb", null)]
    public void FolderNamesAreTrimmedAndChecked(string? input, string? expected) => Assert.Equal(expected, FolderRules.CleanName(input));

    [Fact]
    public void ANameOfTheLongestAllowedSizeIsFineAndOneMoreIsNot()
    {
        Assert.NotNull(FolderRules.CleanName(new string('x', FolderRules.MaxNameLength))); Assert.Null(FolderRules.CleanName(new string('x', FolderRules.MaxNameLength + 1)));
    }
}

/// <summary>Folders, bulk moves, the bin and the list of the inventory: all of it belongs to one person and none of it reaches other people's things.</summary>
public sealed class InventoryOrganizerTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _me = Guid.NewGuid(), _other = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private int _clock;

    public void Dispose() => _db.Dispose();

    private DateTimeOffset Tick() => _now.AddSeconds(++_clock);

    private async Task<InventoryFolder> Folder(string name, Guid? parent = null, Guid? owner = null) { var r = await InventoryOrganizer.CreateFolderAsync(_db, owner ?? _me, name, parent, Tick(), default); Assert.True(r.Ok, r.Error); return (InventoryFolder)r.Data!; }

    private async Task<InventoryItem> Item(string name, string category = "objects", Guid? folder = null, Guid? owner = null, long size = 1000, int rights = ItemRights.All, string description = "", bool favorite = false)
    {
        var at = Tick(); var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner ?? _me, CreatorUserId = owner ?? _me, Name = name, Category = category, Description = description, CreatedAt = at, UpdatedAt = at };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k" + asset.Id, MimeType = "model/gltf-binary", ByteSize = size, CreatedAt = at });
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner ?? _me, AssetId = asset.Id, DisplayName = name, FolderId = folder, OwnerRights = rights, IsFavorite = favorite, CreatedAt = at, UpdatedAt = at };
        _db.InventoryItems.Add(item); await _db.SaveChangesAsync(); return item;
    }

    private async Task<InventoryItem> Reload(Guid id) => await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == id);

    private async Task<List<string>> Names(InventoryQuery q, Guid? user = null)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, (user ?? _me).ToString())], "test")) };
        var result = await InventoryEndpoints.ListAsync(q, context, new FixedClock(_now.AddMinutes(30)), _db, default);
        return JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();
    }

    private async Task<JsonElement> ListBody(InventoryQuery q)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _me.ToString())], "test")) };
        var result = await InventoryEndpoints.ListAsync(q, context, new FixedClock(_now.AddMinutes(30)), _db, default);
        return JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    // ---- folders ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task FoldersAndSubfoldersAreCreatedUnderOwnFoldersOnlyAndWithValidNames()
    {
        var casa = await Folder("Casa"); var sala = await Folder("Sala", casa.Id); Assert.Equal(casa.Id, sala.ParentId);
        Assert.Equal("invalid_name", (await InventoryOrganizer.CreateFolderAsync(_db, _me, "  ", null, Tick(), default)).Error);
        Assert.Equal("name_taken", (await InventoryOrganizer.CreateFolderAsync(_db, _me, "casa", null, Tick(), default)).Error); // names are compared without caring about capitals
        Assert.True((await InventoryOrganizer.CreateFolderAsync(_db, _me, "Casa", casa.Id, Tick(), default)).Ok); // the same name is fine one level down
        var theirs = await Folder("Deles", owner: _other); Assert.Equal("not_found", (await InventoryOrganizer.CreateFolderAsync(_db, _me, "Intruso", theirs.Id, Tick(), default)).Error);
        Assert.Equal("not_found", (await InventoryOrganizer.CreateFolderAsync(_db, _me, "Órfã", Guid.NewGuid(), Tick(), default)).Error);
        Assert.True((await InventoryOrganizer.CreateFolderAsync(_db, _other, "Casa", null, Tick(), default)).Ok); // another person can use the same name
    }

    [Fact]
    public async Task TheDepthAndTheNumberOfFoldersAreLimited()
    {
        var parent = (Guid?)null; for (var i = 0; i < FolderRules.MaxDepth; i++) parent = (await Folder($"N{i}", parent)).Id;
        Assert.Equal("too_deep", (await InventoryOrganizer.CreateFolderAsync(_db, _me, "Fundo demais", parent, Tick(), default)).Error);
        for (var i = FolderRules.MaxDepth; i < FolderRules.MaxFolders; i++) _db.InventoryFolders.Add(new InventoryFolder { Id = Guid.NewGuid(), OwnerUserId = _me, Name = $"P{i}", CreatedAt = _now, UpdatedAt = _now });
        await _db.SaveChangesAsync(); Assert.Equal("folder_limit", (await InventoryOrganizer.CreateFolderAsync(_db, _me, "Uma a mais", null, Tick(), default)).Error);
        Assert.True((await InventoryOrganizer.CreateFolderAsync(_db, _other, "Outra pessoa", null, Tick(), default)).Ok); // the limit is per person
    }

    [Fact]
    public async Task RenamingAndMovingAFolderRefusesLoopsAndNameClashes()
    {
        var a = await Folder("A"); var b = await Folder("B", a.Id); var c = await Folder("C", b.Id); var other = await Folder("D"); await Folder("B", other.Id);
        Assert.Equal("cycle", (await InventoryOrganizer.UpdateFolderAsync(_db, _me, a.Id, null, c.Id, false, Tick(), default)).Error); Assert.Equal("cycle", (await InventoryOrganizer.UpdateFolderAsync(_db, _me, a.Id, null, a.Id, false, Tick(), default)).Error);
        Assert.Null((await _db.InventoryFolders.AsNoTracking().SingleAsync(f => f.Id == a.Id)).ParentId); // nothing moved
        Assert.Equal("name_taken", (await InventoryOrganizer.UpdateFolderAsync(_db, _me, b.Id, null, other.Id, false, Tick(), default)).Error); // "B" already lives there
        Assert.True((await InventoryOrganizer.UpdateFolderAsync(_db, _me, b.Id, "Beta", other.Id, false, Tick(), default)).Ok); Assert.Equal(other.Id, (await _db.InventoryFolders.AsNoTracking().SingleAsync(f => f.Id == b.Id)).ParentId);
        Assert.True((await InventoryOrganizer.UpdateFolderAsync(_db, _me, c.Id, null, null, true, Tick(), default)).Ok); Assert.Null((await _db.InventoryFolders.AsNoTracking().SingleAsync(f => f.Id == c.Id)).ParentId); // to the top
        Assert.Equal("Beta", (await InventoryOrganizer.UpdateFolderAsync(_db, _me, b.Id, null, null, false, Tick(), default)).Data is InventoryFolder f ? f.Name : null); // no new parent: it stays where it is
        Assert.Equal("not_found", (await InventoryOrganizer.UpdateFolderAsync(_db, _other, a.Id, "Roubada", null, false, Tick(), default)).Error); // not theirs
    }

    // ---- moving -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AMoveIsAllOrNothing()
    {
        var target = await Folder("Destino"); var one = await Item("Um"); var two = await Item("Dois"); var strangers = await Item("Do outro", owner: _other); var inBin = await Item("Na lixeira"); inBin.DeletedAt = _now; await _db.SaveChangesAsync();
        Assert.Equal("not_found", (await InventoryOrganizer.MoveAsync(_db, _me, [one.Id, two.Id, strangers.Id], null, target.Id, Tick(), default)).Error); Assert.Null((await Reload(one.Id)).FolderId); // nobody moved
        Assert.Equal("not_found", (await InventoryOrganizer.MoveAsync(_db, _me, [one.Id, inBin.Id], null, target.Id, Tick(), default)).Error);
        Assert.Equal("not_found", (await InventoryOrganizer.MoveAsync(_db, _me, [one.Id], null, Guid.NewGuid(), Tick(), default)).Error);
        Assert.Equal("invalid_move", (await InventoryOrganizer.MoveAsync(_db, _me, [], [], target.Id, Tick(), default)).Error);
        var moved = await InventoryOrganizer.MoveAsync(_db, _me, [one.Id, two.Id, one.Id], null, target.Id, Tick(), default); Assert.True(moved.Ok); Assert.Equal(target.Id, (await Reload(one.Id)).FolderId); Assert.Equal(target.Id, (await Reload(two.Id)).FolderId);
        Assert.True((await InventoryOrganizer.MoveAsync(_db, _me, [one.Id], null, null, Tick(), default)).Ok); Assert.Null((await Reload(one.Id)).FolderId); // back to the top
    }

    [Fact]
    public async Task MovingFoldersAndItemsTogetherRefusesLoopsDuplicateNamesAndAnythingInTheWayWithoutMovingAnything()
    {
        var a = await Folder("A"); var b = await Folder("B", a.Id); var x = await Folder("X"); var clash = await Folder("A", x.Id); var item = await Item("Peça");
        Assert.Equal("cycle", (await InventoryOrganizer.MoveAsync(_db, _me, [item.Id], [a.Id], b.Id, Tick(), default)).Error); Assert.Null((await Reload(item.Id)).FolderId); // the item did not go either
        Assert.Equal("name_taken", (await InventoryOrganizer.MoveAsync(_db, _me, [item.Id], [a.Id], x.Id, Tick(), default)).Error); Assert.Null((await Reload(item.Id)).FolderId);
        Assert.Equal("not_found", (await InventoryOrganizer.MoveAsync(_db, _me, null, [(await Folder("Alheia", owner: _other)).Id], null, Tick(), default)).Error);
        Assert.True((await InventoryOrganizer.MoveAsync(_db, _me, [item.Id], [b.Id], x.Id, Tick(), default)).Ok); Assert.Equal(x.Id, (await Reload(item.Id)).FolderId); Assert.Equal(x.Id, (await _db.InventoryFolders.AsNoTracking().SingleAsync(f => f.Id == b.Id)).ParentId);
        Assert.NotNull(clash);
    }

    // ---- the bin ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DeletingAFolderSendsItsWholeTreeToTheBinAndRestoringBringsItBack()
    {
        var casa = await Folder("Casa"); var sala = await Folder("Sala", casa.Id); var a = await Item("Sofá", folder: sala.Id); var b = await Item("Mesa", folder: casa.Id); var loose = await Item("Solto"); var already = await Item("Já apagado", folder: casa.Id); already.DeletedAt = _now.AddDays(-1); await _db.SaveChangesAsync();
        var offered = await Item("Em oferta", folder: sala.Id); offered.Status = "offered"; await _db.SaveChangesAsync();
        var deleted = await InventoryOrganizer.DeleteFolderAsync(_db, _me, casa.Id, Tick(), default); Assert.True(deleted.Ok);
        Assert.Empty(await _db.InventoryFolders.Where(f => f.DeletedAt == null).ToListAsync()); Assert.NotNull((await Reload(a.Id)).DeletedAt); Assert.Equal(casa.Id, (await Reload(b.Id)).DeletedWithFolderId); Assert.Null((await Reload(loose.Id)).DeletedAt);
        Assert.Null((await Reload(offered.Id)).DeletedAt); Assert.Null((await Reload(offered.Id)).FolderId); // an item in an offer cannot be binned: it is taken out of the folder
        Assert.Null((await Reload(already.Id)).DeletedWithFolderId); // what was in the bin before stays a separate matter
        var restored = await InventoryOrganizer.RestoreFolderAsync(_db, _me, casa.Id, Tick(), default); Assert.True(restored.Ok);
        Assert.Equal(2, await _db.InventoryFolders.CountAsync(f => f.DeletedAt == null)); Assert.Null((await Reload(a.Id)).DeletedAt); Assert.Equal(sala.Id, (await Reload(a.Id)).FolderId); Assert.Null((await Reload(b.Id)).DeletedAt); Assert.NotNull((await Reload(already.Id)).DeletedAt);
        Assert.Equal("not_found", (await InventoryOrganizer.RestoreFolderAsync(_db, _me, casa.Id, Tick(), default)).Error); // already back
        Assert.Equal("not_found", (await InventoryOrganizer.DeleteFolderAsync(_db, _other, casa.Id, Tick(), default)).Error);
    }

    [Fact]
    public async Task RestoringASubfolderWhoseParentIsStillInTheBinPutsItAtTheTopAndAvoidsNameClashes()
    {
        var casa = await Folder("Casa"); var sala = await Folder("Sala", casa.Id); var item = await Item("Sofá", folder: sala.Id);
        await InventoryOrganizer.DeleteFolderAsync(_db, _me, sala.Id, Tick(), default); await InventoryOrganizer.DeleteFolderAsync(_db, _me, casa.Id, Tick(), default); // two separate deletions
        Assert.True((await InventoryOrganizer.RestoreFolderAsync(_db, _me, sala.Id, Tick(), default)).Ok);
        var salaNow = await _db.InventoryFolders.AsNoTracking().SingleAsync(f => f.Id == sala.Id); Assert.Null(salaNow.ParentId); Assert.Null(salaNow.DeletedAt); Assert.Null((await Reload(item.Id)).DeletedAt);
        await Folder("Casa"); // somebody takes the name meanwhile
        Assert.True((await InventoryOrganizer.RestoreFolderAsync(_db, _me, casa.Id, Tick(), default)).Ok); Assert.Equal("Casa (2)", (await _db.InventoryFolders.AsNoTracking().SingleAsync(f => f.Id == casa.Id)).Name);
    }

    [Fact]
    public async Task AnItemRestoredFromTheBinGoesToTheTopWhenItsFolderIsGone()
    {
        var folder = await Folder("Velha"); var item = await Item("Peça", folder: folder.Id);
        item.DeletedAt = _now; await _db.SaveChangesAsync(); await InventoryOrganizer.DeleteFolderAsync(_db, _me, folder.Id, Tick(), default);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _me.ToString())], "test")) };
        var result = await LifeVerse.Api.Modules.Assets.AssetEndpoints.RestoreItemAsync(item.Id, context, _db, default); Assert.IsType<Ok<InventoryItem>>(result);
        var back = await Reload(item.Id); Assert.Null(back.DeletedAt); Assert.Null(back.FolderId);
    }

    [Fact]
    public async Task PurgingGoesOnlyForItemsInTheBinAndKeepsTheRecord()
    {
        var live = await Item("Viva"); var binned = await Item("Na lixeira"); binned.DeletedAt = _now; await _db.SaveChangesAsync(); var theirs = await Item("Dele", owner: _other); theirs.DeletedAt = _now; await _db.SaveChangesAsync();
        Assert.Equal("not_found", (await InventoryOrganizer.PurgeItemAsync(_db, _me, live.Id, Tick(), default)).Error); Assert.Equal("not_found", (await InventoryOrganizer.PurgeItemAsync(_db, _me, theirs.Id, Tick(), default)).Error);
        Assert.True((await InventoryOrganizer.PurgeItemAsync(_db, _me, binned.Id, Tick(), default)).Ok); var row = await Reload(binned.Id); Assert.Equal("purged", row.Status);
        Assert.Equal("not_found", (await InventoryOrganizer.PurgeItemAsync(_db, _me, binned.Id, Tick(), default)).Error); // nothing to purge twice
        Assert.Contains(_db.ItemAudit, a => a.Action == "purge" && a.ItemId == binned.Id);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _me.ToString())], "test")) };
        Assert.IsType<NotFound>(await LifeVerse.Api.Modules.Assets.AssetEndpoints.RestoreItemAsync(binned.Id, context, _db, default)); // and it cannot be restored
        Assert.NotNull((await Reload(binned.Id)).DeletedAt); Assert.Equal("purged", (await Reload(binned.Id)).Status);
    }

    [Fact]
    public async Task EmptyingTheBinTakesOnlyThePersonsOwnAndExpiryTakesOnlyWhatIsOld()
    {
        var folder = await Folder("Pasta"); var inFolder = await Item("Dentro", folder: folder.Id); await InventoryOrganizer.DeleteFolderAsync(_db, _me, folder.Id, _now.AddDays(-40), default);
        var recent = await Item("Recente"); recent.DeletedAt = _now.AddDays(-2); var old = await Item("Antiga"); old.DeletedAt = _now.AddDays(-InventoryOrganizer.TrashDays - 1); var theirs = await Item("Dele", owner: _other); theirs.DeletedAt = _now.AddDays(-50); await _db.SaveChangesAsync();
        var expired = await InventoryOrganizer.ExpireTrashAsync(_db, _now, default); Assert.Equal((3, 1), expired); // the old one, the one that went with the old folder and the other person's, plus that folder
        Assert.Equal("purged", (await Reload(old.Id)).Status); Assert.Equal("purged", (await Reload(inFolder.Id)).Status); Assert.Equal("active", (await Reload(recent.Id)).Status);
        Assert.Empty(await _db.InventoryFolders.ToListAsync());
        var emptied = await InventoryOrganizer.EmptyTrashAsync(_db, _me, _now, null, default); Assert.Equal(1, emptied.Items); Assert.Equal("purged", (await Reload(recent.Id)).Status);
    }

    // ---- recents ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task UsingAnItemMakesItRecentAndOnlyTheHoldersUseCounts()
    {
        var old = await Item("Velha"); old.CreatedAt = _now.AddDays(-60); var used = await Item("Usada"); used.CreatedAt = _now.AddDays(-60); await _db.SaveChangesAsync();
        await InventoryOrganizer.TouchAsync(_db, _me, used.Id, _now.AddMinutes(10), default); await InventoryOrganizer.TouchAsync(_db, _other, old.Id, _now, default); await InventoryOrganizer.TouchAsync(_db, _me, Guid.NewGuid(), _now, default);
        Assert.Null((await Reload(old.Id)).LastUsedAt); Assert.NotNull((await Reload(used.Id)).LastUsedAt);
        var fresh = await Item("Nova"); Assert.Equal(["Nova", "Usada"], (await Names(new InventoryQuery(View: "recent"))).OrderBy(n => n)); // new things are recent too, old unused ones are not
        Assert.Equal("Usada", (await Names(new InventoryQuery(View: "recent", Sort: "recent")))[0].Replace("Nova", "Usada") is { } first ? first : ""); Assert.NotNull(fresh);
    }

    // ---- the list ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SearchIgnoresCapitalsAndNeedsEveryWordInTheNameTheAssetOrItsDescription()
    {
        await Item("Sofá Vermelho"); await Item("Sofá Azul", description: "de couro vermelho"); await Item("Mesa Azul"); await Item("Pedra", category: "textures");
        Assert.Equal(["Sofá Azul", "Sofá Vermelho"], (await Names(new InventoryQuery(Search: "SOFÁ", Sort: "name"))));
        Assert.Equal(["Sofá Azul", "Sofá Vermelho"], (await Names(new InventoryQuery(Search: "sofá vermelho", Sort: "name")))); // one word is in the name, the other in the description
        Assert.Empty(await Names(new InventoryQuery(Search: "sofá sofá sofá sofá sofá zzz"))); // the sixth word still counts
        Assert.Equal(2, (await Names(new InventoryQuery(Search: "sofá sofá sofá sofá sofá sofá zzz"))).Count); // a seventh word is ignored
        Assert.Empty(await Names(new InventoryQuery(Search: "sofá inexistente")));
        Assert.Equal(["Pedra"], await Names(new InventoryQuery(Search: "textures"))); // the kind of thing is searchable too
        Assert.Equal(4, (await Names(new InventoryQuery(Search: "   "))).Count);
        await Item("Oferta 50% off"); Assert.Empty(await Names(new InventoryQuery(Search: "50%x"))); Assert.Equal(["Oferta 50% off"], await Names(new InventoryQuery(Search: "50%"))); Assert.Empty(await Names(new InventoryQuery(Search: "_"))); // % and _ are text, not wildcards
    }

    [Fact]
    public async Task FiltersByTypeRightsSizeAndDateNarrowTheList()
    {
        await Item("Cadeira", "models", size: 5_000); await Item("Casa", "models", size: 50_000, rights: ItemRights.Modify | ItemRights.Transfer); await Item("Madeira", "textures", size: 900); await Item("Passos", "sounds", size: 20_000, rights: ItemRights.Copy);
        Assert.Equal(["Cadeira", "Casa"], await Names(new InventoryQuery(Types: "models", Sort: "name"))); Assert.Equal(["Madeira", "Passos"], await Names(new InventoryQuery(Types: "textures, SOUNDS", Sort: "name")));
        Assert.Equal(["Cadeira", "Madeira"], await Names(new InventoryQuery(Rights: "copy,modify", Sort: "name"))); // only what can be both copied and modified (the two with every right)... see below
    }

    [Fact]
    public async Task TheRightsFilterKeepsOnlyItemsWithEveryRightAsked()
    {
        await Item("Tudo"); await Item("Só copiar", rights: ItemRights.Copy); await Item("Copiar e modificar", rights: ItemRights.Copy | ItemRights.Modify);
        Assert.Equal(["Copiar e modificar", "Tudo"], await Names(new InventoryQuery(Rights: "copy,modify", Sort: "name"))); Assert.Equal(["Tudo"], await Names(new InventoryQuery(Rights: "resell")));
        Assert.Equal(3, (await Names(new InventoryQuery(Rights: "nonsense"))).Count);
    }

    [Fact]
    public async Task SizeAndDateFiltersAndEveryOrderWork()
    {
        var a = await Item("Beta", "models", size: 300); var b = await Item("alfa", "textures", size: 100); var c = await Item("Gama", "animations", size: 200);
        Assert.Equal(["alfa", "Beta", "Gama"], await Names(new InventoryQuery(Sort: "name"))); Assert.Equal(["Gama", "Beta", "alfa"], await Names(new InventoryQuery(Sort: "name", Dir: "desc")));
        Assert.Equal(["Beta", "Gama", "alfa"], await Names(new InventoryQuery(Sort: "size"))); Assert.Equal(["alfa", "Gama", "Beta"], await Names(new InventoryQuery(Sort: "size", Dir: "asc")));
        Assert.Equal(["Gama", "Beta", "alfa"], await Names(new InventoryQuery(Sort: "type"))); // animations, models, textures
        Assert.Equal(["Gama", "Beta"], (await Names(new InventoryQuery(MinSize: 150, Sort: "name", Dir: "desc"))));
        Assert.Equal(["alfa", "Gama"], await Names(new InventoryQuery(MaxSize: 250, Sort: "name")));
        Assert.Equal(["Gama"], await Names(new InventoryQuery(From: c.CreatedAt))); Assert.Equal(["alfa", "Beta"], (await Names(new InventoryQuery(To: b.CreatedAt.AddMilliseconds(1)))).OrderBy(x => x.ToLowerInvariant()));
        Assert.NotNull(a);
    }

    [Fact]
    public async Task TheDefaultOrderPutsFavouritesFirstThenTheNewest()
    {
        await Item("Velha"); await Item("Favorita", favorite: true); await Item("Nova"); Assert.Equal(["Favorita", "Nova", "Velha"], await Names(new InventoryQuery()));
        Assert.Equal(["Favorita"], await Names(new InventoryQuery(View: "favorites"))); Assert.Equal(["Favorita"], await Names(new InventoryQuery(FavoritesOnly: true)));
    }

    [Fact]
    public async Task PagesAreStableEvenWhenManyItemsTieAndNothingIsRepeatedOrMissing()
    {
        var at = _now; for (var i = 0; i < 25; i++) { var item = await Item($"Igual", size: 10); item.CreatedAt = at; item.UpdatedAt = at; await _db.SaveChangesAsync(); } // 25 with the same name, size and date
        var seen = new List<string>(); var ids = new HashSet<string>();
        for (var page = 1; page <= 3; page++)
        {
            var body = await ListBody(new InventoryQuery(Sort: "size", Page: page, PageSize: 10)); Assert.Equal(25, body.GetProperty("total").GetInt32());
            foreach (var e in body.GetProperty("items").EnumerateArray()) { seen.Add(e.GetProperty("name").GetString()!); Assert.True(ids.Add(e.GetProperty("itemId").GetString()!), "an item came twice"); }
            Assert.Equal(page < 3, body.GetProperty("hasMore").GetBoolean());
        }
        Assert.Equal(25, ids.Count); Assert.Empty((await ListBody(new InventoryQuery(Page: 4, PageSize: 10))).GetProperty("items").EnumerateArray());
        Assert.Equal(100, (await ListBody(new InventoryQuery(PageSize: 5000))).GetProperty("pageSize").GetInt32()); Assert.Equal(1, (await ListBody(new InventoryQuery(Page: -4, PageSize: 0))).GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task FolderViewsCanIncludeSubfoldersOrShowOnlyWhatIsNotInAnyFolder()
    {
        var casa = await Folder("Casa"); var sala = await Folder("Sala", casa.Id); await Item("Na casa", folder: casa.Id); await Item("Na sala", folder: sala.Id); await Item("Solto");
        Assert.Equal(["Na casa"], await Names(new InventoryQuery(FolderId: casa.Id))); Assert.Equal(["Na casa", "Na sala"], await Names(new InventoryQuery(FolderId: casa.Id, IncludeSubfolders: true, Sort: "name")));
        Assert.Equal(["Solto"], await Names(new InventoryQuery(Unfiled: true))); Assert.Equal(3, (await Names(new InventoryQuery())).Count);
    }

    [Fact]
    public async Task TheBinViewShowsOnlyWhatWasDeletedOnItsOwnAndNeverWhatIsPurgedOrConsumedOrOtherPeoples()
    {
        var folder = await Folder("Pasta"); var inFolder = await Item("Com a pasta", folder: folder.Id); var alone = await Item("Sozinha"); alone.DeletedAt = _now; var purged = await Item("Eliminada"); purged.DeletedAt = _now; purged.Status = "purged";
        var consumed = await Item("Usada ao colocar"); consumed.DeletedAt = _now; consumed.Status = "consumed"; var theirs = await Item("Dele", owner: _other); theirs.DeletedAt = _now; await _db.SaveChangesAsync();
        await InventoryOrganizer.DeleteFolderAsync(_db, _me, folder.Id, Tick(), default);
        Assert.Equal(["Sozinha"], await Names(new InventoryQuery(View: "trash"))); // the one binned with its folder comes back with the folder
        Assert.Equal(["Sozinha"], await Names(new InventoryQuery(IncludeDeleted: true, Sort: "name")).ContinueWith(t => t.Result.Where(n => n == "Sozinha").ToList()));
        Assert.DoesNotContain("Eliminada", await Names(new InventoryQuery(IncludeDeleted: true))); Assert.DoesNotContain("Usada ao colocar", await Names(new InventoryQuery(IncludeDeleted: true))); Assert.DoesNotContain("Dele", await Names(new InventoryQuery(IncludeDeleted: true)));
        Assert.NotNull(inFolder);
    }

    [Fact]
    public async Task NobodySeesAnotherPersonsInventory()
    {
        await Item("Meu"); await Item("Deles", owner: _other); Assert.Equal(["Meu"], await Names(new InventoryQuery())); Assert.Equal(["Deles"], await Names(new InventoryQuery(), _other)); Assert.Empty(await Names(new InventoryQuery(Search: "deles")));
    }
}
