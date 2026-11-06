using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Social;
using LifeVerse.Shared.Items;
using LifeVerse.Shared.Networking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Deliveries between players: the history of them, what the receiver is told and what they would get, and the check that looks for duplications.</summary>
public sealed class ItemDeliveryTests : IDisposable
{
    private sealed class Listener : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open; public List<string> Sent { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => null; public override string? CloseStatusDescription => null; public override WebSocketState State => _state; public override string? SubProtocol => null;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) { Sent.Add(Encoding.UTF8.GetString(buffer.AsSpan())); return Task.CompletedTask; }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) { _state = WebSocketState.Closed; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) { _state = WebSocketState.CloseSent; return Task.CompletedTask; }
        public override void Abort() => _state = WebSocketState.Aborted; public override void Dispose() { }
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly User _ana, _bia, _cris; private int _key; private int _tick;

    public ItemDeliveryTests() { _ana = NewUser("ana"); _bia = NewUser("bia"); _cris = NewUser("cris"); }
    public void Dispose() => _db.Dispose();

    private User NewUser(string username)
    {
        var u = new User { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), Email = $"{username}@t.local", NormalizedEmail = $"{username}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now };
        _db.Users.Add(u); _db.SaveChanges(); return u;
    }

    private DateTimeOffset Tick() => _now.AddMinutes(++_tick);

    private InventoryItem NewItem(User owner, string name = "Peça", int rights = ItemRights.All, int next = ItemRights.All)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner.Id, CreatorUserId = owner.Id, Name = name, Category = "mesh", CreatedAt = _now, UpdatedAt = _now };
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner.Id, AssetId = asset.Id, DisplayName = name, OwnerRights = rights, NextOwnerRights = next, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.InventoryItems.Add(item); _db.SaveChanges(); return item;
    }

    private async Task<ItemOffer> Offer(User from, InventoryItem item, User to, string kind = "gift", bool keep = false)
    {
        var result = await ItemTransfers.OfferAsync(_db, from.Id, item.Id, to.Id, kind, "olha isso", keep, "chave-" + (++_key).ToString("D6"), Tick(), default); Assert.True(result.Ok, result.Error); return result.Offer!;
    }


    // ---- the history of deliveries --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheHistoryListsEveryDeliveryWhateverHappenedToItAndOnlyToTheTwoPeopleInIt()
    {
        var a = NewItem(_ana, "Sofá"); var b = NewItem(_ana, "Mesa"); var c = NewItem(_bia, "Lustre"); var d = NewItem(_ana, "Tapete");
        var accepted = await Offer(_ana, a, _bia); Assert.True((await ItemTransfers.AcceptAsync(_db, _bia.Id, accepted.Id, Tick(), default)).Ok);
        var rejected = await Offer(_ana, b, _bia, "transfer"); Assert.True((await ItemTransfers.RejectAsync(_db, _bia.Id, rejected.Id, Tick(), default)).Ok);
        var back = await Offer(_bia, c, _ana); var cancelled = await Offer(_ana, d, _cris); Assert.True((await ItemTransfers.CancelAsync(_db, _ana.Id, cancelled.Id, Tick(), default)).Ok);

        var (total, lines) = await ItemTransferHistory.QueryAsync(_db, _ana.Id, new TransferQuery(), default); Assert.Equal(4, total);
        Assert.Equal(["Tapete", "Lustre", "Mesa", "Sofá"], lines.Select(l => l.ItemName)); // newest first
        Assert.Equal(["out", "in", "out", "out"], lines.Select(l => l.Direction)); Assert.Equal(["cancelled", "pending", "rejected", "accepted"], lines.Select(l => l.Status)); Assert.Equal(["cris", "bia", "bia", "bia"], lines.Select(l => l.OtherName));
        Assert.Equal("transfer", lines.Single(l => l.ItemName == "Mesa").Kind); Assert.NotNull(lines.Single(l => l.ItemName == "Sofá").AnsweredAt); Assert.Null(lines.Single(l => l.ItemName == "Lustre").AnsweredAt);
        var bias = await ItemTransferHistory.QueryAsync(_db, _bia.Id, new TransferQuery(), default); Assert.Equal(3, bias.Total); Assert.DoesNotContain(bias.Lines, l => l.ItemName == "Tapete"); // the one to Cris is not hers to see
        var criss = await ItemTransferHistory.QueryAsync(_db, _cris.Id, new TransferQuery(), default); Assert.Equal(1, criss.Total); Assert.Equal("in", criss.Lines[0].Direction);
        Assert.Equal(0, (await ItemTransferHistory.QueryAsync(_db, Guid.NewGuid(), new TransferQuery(), default)).Total); Assert.NotNull(back);
    }

    [Fact]
    public async Task TheHistoryCanBeFilteredByDirectionOutcomeWordAndPeriod()
    {
        var sofa = NewItem(_ana, "Sofá azul"); var mesa = NewItem(_ana, "Mesa"); var lustre = NewItem(_bia, "Lustre azul");
        var o1 = await Offer(_ana, sofa, _bia); var o2 = await Offer(_ana, mesa, _cris); var o3 = await Offer(_bia, lustre, _ana); await ItemTransfers.AcceptAsync(_db, _bia.Id, o1.Id, Tick(), default); await ItemTransfers.RejectAsync(_db, _cris.Id, o2.Id, Tick(), default);
        async Task<List<string>> Names(TransferQuery q, Guid? who = null) => (await ItemTransferHistory.QueryAsync(_db, who ?? _ana.Id, q, default)).Lines.Select(l => l.ItemName).OrderBy(n => n).ToList();
        Assert.Equal(["Lustre azul"], await Names(new TransferQuery(Direction: "in"))); Assert.Equal(["Mesa", "Sofá azul"], await Names(new TransferQuery(Direction: "out"))); Assert.Equal(3, (await Names(new TransferQuery(Direction: "nonsense"))).Count);
        Assert.Equal(["Sofá azul"], await Names(new TransferQuery(Status: "accepted"))); Assert.Equal(["Lustre azul", "Mesa"], await Names(new TransferQuery(Status: "pending,rejected"))); Assert.Equal(3, (await Names(new TransferQuery(Status: "bogus"))).Count); // an unknown outcome filters nothing
        Assert.Equal(["Lustre azul", "Sofá azul"], await Names(new TransferQuery(Search: "AZUL"))); Assert.Equal(["Mesa"], await Names(new TransferQuery(Search: "cris"))); Assert.Equal(["Lustre azul", "Sofá azul"], await Names(new TransferQuery(Search: "bia")));
        Assert.Empty(await Names(new TransferQuery(Search: "ana"))); // one's own name says nothing about who the delivery was with
        Assert.Empty(await Names(new TransferQuery(Search: "azul mesa"))); Assert.Equal(["Sofá azul"], await Names(new TransferQuery(From: o1.CreatedAt, To: o1.CreatedAt)));
        Assert.Equal(["Lustre azul", "Mesa"], await Names(new TransferQuery(From: o2.CreatedAt)));
    }

    [Fact]
    public async Task HistoryPagesAreStableAndTheDeletedStayInTheRecord()
    {
        var items = new List<InventoryItem>(); for (var i = 0; i < 12; i++) items.Add(NewItem(_ana, $"Peça {i:00}")); foreach (var item in items) await Offer(_ana, item, _bia, keep: true);
        var seen = new HashSet<Guid>(); var first = await ItemTransferHistory.QueryAsync(_db, _ana.Id, new TransferQuery(Page: 1, PageSize: 5), default); var second = await ItemTransferHistory.QueryAsync(_db, _ana.Id, new TransferQuery(Page: 2, PageSize: 5), default); var third = await ItemTransferHistory.QueryAsync(_db, _ana.Id, new TransferQuery(Page: 3, PageSize: 5), default);
        foreach (var line in first.Lines.Concat(second.Lines).Concat(third.Lines)) Assert.True(seen.Add(line.Id)); Assert.Equal((12, 5, 5, 2, 12), (first.Total, first.Lines.Count, second.Lines.Count, third.Lines.Count, seen.Count));
        Assert.Equal(12, (await ItemTransferHistory.QueryAsync(_db, _ana.Id, new TransferQuery(PageSize: 9999), default)).Lines.Count); Assert.Empty((await ItemTransferHistory.QueryAsync(_db, _ana.Id, new TransferQuery(Page: 9), default)).Lines);
        _db.InventoryItems.Remove(items[0]); _db.Users.Remove(_bia); await _db.SaveChangesAsync();
        var all = await ItemTransferHistory.QueryAsync(_db, _ana.Id, new TransferQuery(PageSize: 100), default); Assert.Equal(12, all.Total); Assert.Contains(all.Lines, l => l.ItemName == "item removido"); Assert.All(all.Lines, l => Assert.Equal("conta removida", l.OtherName));
    }

    // ---- what the receiver sees and is told -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheReceiverSeesWhatRightsTheyWouldGetBeforeAccepting()
    {
        var item = NewItem(_ana, "Estátua", rights: ItemRights.All, next: ItemRights.Copy | ItemRights.Modify); await Offer(_ana, item, _bia);
        var incoming = (await ItemTransfers.ListAsync(_db, _bia.Id, true, true, Tick(), default)).Single(); Assert.Equal(ItemRights.Copy | ItemRights.Modify, incoming.Arrives);
        var outgoing = (await ItemTransfers.ListAsync(_db, _ana.Id, false, true, Tick(), default)).Single(); Assert.Equal(incoming.Arrives, outgoing.Arrives); // the giver sees the same promise
        var accepted = await ItemTransfers.AcceptAsync(_db, _bia.Id, incoming.Id, Tick(), default); Assert.Equal(incoming.Arrives, accepted.Item!.OwnerRights); // and that is what arrives
    }

    [Fact]
    public async Task TheOtherPersonIsToldLiveWhenSomethingHappensToADelivery()
    {
        var hub = new NotificationHub(TimeProvider.System, NullLogger<NotificationHub>.Instance); var ana = new Listener(); var bia = new Listener(); hub.TryAdd(_ana.Id, Guid.NewGuid(), ana); hub.TryAdd(_bia.Id, Guid.NewGuid(), bia);
        var offer = await Offer(_ana, NewItem(_ana, "Sofá"), _bia, "transfer"); await ItemOfferNotifier.OfferedAsync(hub, _db, offer, default);
        Assert.Empty(ana.Sent); using (var json = JsonDocument.Parse(bia.Sent.Single())) { Assert.Equal("item_offer", json.RootElement.GetProperty("type").GetString()); var data = json.RootElement.GetProperty("data"); Assert.Equal(("ana", "Sofá", "transfer"), (data.GetProperty("fromName").GetString(), data.GetProperty("itemName").GetString(), data.GetProperty("kind").GetString())); }
        Assert.Equal("ana quer te entregar “Sofá”. Veja em Ofertas de itens.", NotificationFormatter.Describe(bia.Sent.Single()));
        var rejected = await ItemTransfers.RejectAsync(_db, _bia.Id, offer.Id, Tick(), default); await ItemOfferNotifier.AnsweredAsync(hub, _db, rejected.Offer!, "rejected", default);
        Assert.Equal("bia recusou “Sofá”.", NotificationFormatter.Describe(ana.Sent.Single())); Assert.Single(bia.Sent); // the answer goes to the giver only
        var second = await Offer(_ana, NewItem(_ana, "Mesa"), _bia, "gift"); await ItemTransfers.CancelAsync(_db, _ana.Id, second.Id, Tick(), default); var closed = await _db.ItemOffers.AsNoTracking().SingleAsync(o => o.Id == second.Id);
        await ItemOfferNotifier.AnsweredAsync(hub, _db, closed, "cancelled", default); Assert.Equal("ana cancelou a oferta de “Mesa”.", NotificationFormatter.Describe(bia.Sent.Last())); // a cancellation goes to the receiver
        await ItemOfferNotifier.OfferedAsync(new NotificationHub(TimeProvider.System, NullLogger<NotificationHub>.Instance), _db, offer, default); // nobody listening: harmless
    }

    [Theory]
    [InlineData("""{"type":"item_offer","data":{"fromName":"ana","itemName":"Sofá","kind":"gift"}}""", "ana quer te dar “Sofá”. Veja em Ofertas de itens.")]
    [InlineData("""{"type":"item_offer","data":{}}""", "Alguém quer te dar “”. Veja em Ofertas de itens.")]
    [InlineData("""{"type":"item_offer_answer","data":{"byName":"bia","itemName":"Mesa","answer":"accepted"}}""", "bia aceitou “Mesa”.")]
    [InlineData("""{"type":"item_offer_answer","data":{"byName":"bia","itemName":"Mesa","answer":"rejected"}}""", "bia recusou “Mesa”.")]
    public void TheNewEventsBecomeShortText(string json, string expected) => Assert.Equal(expected, NotificationFormatter.Describe(json));

    [Fact]
    public void AnUnknownAnswerIsIgnored() => Assert.Null(NotificationFormatter.Describe("""{"type":"item_offer_answer","data":{"answer":"?"}}"""));

    // ---- finding duplications ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryNormalWayOfHandingOverLeavesTheInventoryClean()
    {
        var a = NewItem(_ana, "A"); var b = NewItem(_ana, "B"); var c = NewItem(_ana, "C"); var d = NewItem(_ana, "D");
        var accepted = await Offer(_ana, a, _bia); await ItemTransfers.AcceptAsync(_db, _bia.Id, accepted.Id, Tick(), default);
        var kept = await Offer(_ana, b, _bia, keep: true); await ItemTransfers.AcceptAsync(_db, _bia.Id, kept.Id, Tick(), default);
        var rejected = await Offer(_ana, c, _cris); await ItemTransfers.RejectAsync(_db, _cris.Id, rejected.Id, Tick(), default); await Offer(_ana, d, _cris); // one still waiting
        var again = await Offer(_bia, a, _cris); await ItemTransfers.CancelAsync(_db, _bia.Id, again.Id, Tick(), default); await ItemTransfers.CopyAsync(_db, _ana.Id, c.Id, Tick(), default);
        foreach (var who in new Guid?[] { null, _ana.Id, _bia.Id, _cris.Id }) { var report = await InventoryIntegrity.CheckAsync(_db, who, default); Assert.True(report.Clean, string.Join("; ", report.Findings.Select(f => f.Rule))); Assert.True(report.Checked > 0 || who is null || who == _cris.Id); }
    }

    [Fact]
    public async Task AnItemLockedByAnOfferThatIsGoneIsFoundAndReleased()
    {
        var item = NewItem(_ana, "Presa"); item.Status = ItemRightsService.Offered; await _db.SaveChangesAsync();
        var finding = Assert.Single((await InventoryIntegrity.CheckAsync(_db, _ana.Id, default)).Findings); Assert.Equal(("stuck_offered", item.Id, true), (finding.Rule, finding.ItemId, finding.Repairable));
        Assert.True((await InventoryIntegrity.CheckAsync(_db, _bia.Id, default)).Clean); Assert.Single((await InventoryIntegrity.CheckAsync(_db, null, default)).Findings); // other people's problems are not yours
        Assert.Equal((0, 0), await InventoryIntegrity.RepairAsync(_db, _bia.Id, Tick(), default)); Assert.Equal(ItemRightsService.Offered, (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id)).Status);
        Assert.Equal((1, 0), await InventoryIntegrity.RepairAsync(_db, _ana.Id, Tick(), default)); Assert.Equal(ItemRightsService.Active, (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id)).Status);
        Assert.True((await InventoryIntegrity.CheckAsync(_db, _ana.Id, default)).Clean); Assert.Contains(_db.ItemAudit, a => a.Action == "repair" && a.ItemId == item.Id);
        Assert.Equal((0, 0), await InventoryIntegrity.RepairAsync(_db, _ana.Id, Tick(), default)); // nothing left to fix
    }

    [Fact]
    public async Task TwoOffersForTheSameItemAreFoundAndOnlyTheOldestIsKept()
    {
        var item = NewItem(_ana, "Disputada"); var first = await Offer(_ana, item, _bia);
        _db.ItemOffers.Add(new ItemOffer { Id = Guid.NewGuid(), ItemId = item.Id, AssetId = item.AssetId, FromUserId = _ana.Id, ToUserId = _cris.Id, Kind = "gift", Message = "", KeepCopy = false, Status = "pending", IdempotencyKey = "outra-chave-1", CreatedAt = Tick(), ExpiresAt = _now.AddDays(9) }); await _db.SaveChangesAsync();
        var report = await InventoryIntegrity.CheckAsync(_db, _ana.Id, default); var finding = Assert.Single(report.Findings); Assert.Equal("double_offer", finding.Rule); Assert.NotEqual(first.Id, finding.OfferId); Assert.Single((await InventoryIntegrity.CheckAsync(_db, _cris.Id, default)).Findings); Assert.True((await InventoryIntegrity.CheckAsync(_db, Guid.NewGuid(), default)).Clean);
        Assert.Equal((0, 1), await InventoryIntegrity.RepairAsync(_db, _ana.Id, Tick(), default));
        Assert.Equal("pending", (await _db.ItemOffers.AsNoTracking().SingleAsync(o => o.Id == first.Id)).Status); Assert.Equal("cancelled", (await _db.ItemOffers.AsNoTracking().SingleAsync(o => o.Id == finding.OfferId)).Status);
        Assert.True((await InventoryIntegrity.CheckAsync(_db, null, default)).Clean); Assert.True((await ItemTransfers.AcceptAsync(_db, _bia.Id, first.Id, Tick(), default)).Ok); // the one that stayed still works
    }

    [Fact]
    public async Task AnOfferWhoseItemWasTakenElsewhereIsFoundAndClosedWithoutTouchingTheItem()
    {
        var item = NewItem(_ana, "Perdida"); var offer = await Offer(_ana, item, _bia);
        var row = await _db.InventoryItems.SingleAsync(i => i.Id == item.Id); row.OwnerUserId = _cris.Id; row.Status = ItemRightsService.Active; await _db.SaveChangesAsync(); // it ended up with someone else some other way
        var findings = (await InventoryIntegrity.CheckAsync(_db, _bia.Id, default)).Findings; Assert.Contains(findings, f => f.Rule == "orphan_offer" && f.OfferId == offer.Id); Assert.Equal((0, 1), await InventoryIntegrity.RepairAsync(_db, _ana.Id, Tick(), default));
        var after = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id); Assert.Equal((_cris.Id, ItemRightsService.Active), (after.OwnerUserId, after.Status)); Assert.Equal("cancelled", (await _db.ItemOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).Status);
    }

    [Fact]
    public async Task AnOwnerMissingFromTheHistoryAndAPromisedCopyThatIsNotThereAreReportedButNotGuessedAt()
    {
        var item = NewItem(_ana, "Sem registro"); ItemRightsService.RecordOwner(_db, item, _ana.Id, null, "created", _now); ItemRightsService.RecordOwner(_db, item, _bia.Id, _ana.Id, "gift", _now.AddHours(1)); await _db.SaveChangesAsync(); // history says Bia holds it, but Ana does
        var report = await InventoryIntegrity.CheckAsync(_db, _ana.Id, default); Assert.Equal(["owner_history_mismatch"], report.Findings.Select(f => f.Rule)); Assert.All(report.Findings, f => Assert.False(f.Repairable));
        Assert.Equal((0, 0), await InventoryIntegrity.RepairAsync(_db, _ana.Id, Tick(), default)); Assert.Single((await InventoryIntegrity.CheckAsync(_db, _ana.Id, default)).Findings); // not repaired: that needs a person to decide
        var kept = await Offer(_ana, NewItem(_ana, "Promessa"), _bia, keep: true); await ItemTransfers.AcceptAsync(_db, _bia.Id, kept.Id, Tick(), default); Assert.DoesNotContain((await InventoryIntegrity.CheckAsync(_db, _bia.Id, default)).Findings, f => f.Rule == "missing_copy");
        var made = (await _db.ItemOffers.AsNoTracking().SingleAsync(o => o.Id == kept.Id)).ResultItemId; _db.InventoryItems.Remove(await _db.InventoryItems.SingleAsync(i => i.Id == made)); await _db.SaveChangesAsync();
        Assert.Contains((await InventoryIntegrity.CheckAsync(_db, _bia.Id, default)).Findings, f => f.Rule == "missing_copy" && f.OfferId == kept.Id);
    }

    [Fact]
    public async Task ASingleThingInTwoObjectsIsFoundButACopyableOneIsNot()
    {
        var maker = NewUser("criador"); var uncopyable = NewItem(maker, "Raro"); var copyable = NewItem(maker, "Comum");
        // Ana holds both, one that cannot be copied and one that can; each is wrongly or rightly in two objects
        var raroAna = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, AssetId = uncopyable.AssetId, DisplayName = "Raro", OwnerRights = ItemRights.Modify | ItemRights.Transfer, NextOwnerRights = ItemRights.All, CreatedAt = _now, UpdatedAt = _now };
        var comumAna = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, AssetId = copyable.AssetId, DisplayName = "Comum", OwnerRights = ItemRights.All, NextOwnerRights = ItemRights.All, CreatedAt = _now, UpdatedAt = _now };
        _db.InventoryItems.AddRange(raroAna, comumAna); var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 }; _db.Islands.Add(island);
        BuildObject Mesh(Guid asset, float x) => new() { Id = Guid.NewGuid(), IslandId = island.Id, CreatorUserId = _ana.Id, OwnerUserId = _ana.Id, Name = "Objeto", PrimitiveType = "mesh", PositionX = x, GeometryJson = $"{{\"assetId\":\"{asset}\"}}", CreatedAt = _now, UpdatedAt = _now };
        var rare1 = Mesh(uncopyable.AssetId, 1); var rare2 = Mesh(uncopyable.AssetId, 2); _db.BuildObjects.AddRange(rare1, Mesh(copyable.AssetId, 3), Mesh(copyable.AssetId, 4)); await _db.SaveChangesAsync();
        Assert.True((await InventoryIntegrity.CheckAsync(_db, _ana.Id, default)).Clean); // one object each of the single, two of the copyable: all allowed
        _db.BuildObjects.Add(rare2); await _db.SaveChangesAsync();
        var finding = Assert.Single((await InventoryIntegrity.CheckAsync(_db, _ana.Id, default)).Findings); Assert.Equal("uncopyable_in_many_objects", finding.Rule); Assert.Equal(rare2.Id, finding.ObjectId); Assert.False(finding.Repairable);
        Assert.True((await InventoryIntegrity.CheckAsync(_db, maker.Id, default)).Clean); // the maker may put what they made anywhere
    }
}
