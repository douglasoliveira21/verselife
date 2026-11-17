using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The history of a wallet: filters, totals, the reserved balance and the receipt.</summary>
public sealed class EconomyHistoryTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _you = Guid.NewGuid(), _stranger = Guid.NewGuid();
    public void Dispose() => _db.Dispose();

    private LedgerTransaction Add(string type, long amount, Guid? by, Guid? other, int daysAgo = 0, string status = "completed", string metadata = "{}")
    {
        var tx = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString("N"), Type = type, Status = status, Amount = amount, InitiatorUserId = by, CounterpartyUserId = other, MetadataJson = metadata, CreatedAt = _now.AddDays(-daysAgo), CompletedAt = _now.AddDays(-daysAgo) };
        _db.LedgerTransactions.Add(tx); _db.SaveChanges(); return tx;
    }

    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Theory]
    [InlineData("user_transfer", "transfer")] [InlineData("script_payout", "transfer")] [InlineData("marketplace_purchase", "market")] [InlineData("object_purchase", "market")]
    [InlineData("coin_purchase", "coins")] [InlineData("coin_refund", "coins")] [InlineData("admin_adjustment", "adjustment")]
    [InlineData("land_sale", "property")] [InlineData("island_renewal", "property")] [InlineData("land_autorenew", "property")] [InlineData("group_treasury", "other")]
    public void EveryTypeHasItsGroup(string type, string kind) => Assert.Equal(kind, EconomyHistory.Category(type));

    [Fact]
    public async Task TheListHasOnlyMyEntriesWithTheirDirectionAndFilters()
    {
        Add("user_transfer", 100, _me, _you); Add("user_transfer", 40, _you, _me); Add("coin_purchase", 2750, _me, null); Add("user_transfer", 7, _stranger, _you); Add("user_transfer", 9, _me, _you, daysAgo: 40);
        var all = await EconomyHistory.ListAsync(_db, _me, null, null, null, 100, _now, default); Assert.Equal(4, all.Count);
        Assert.Equal(3, (await EconomyHistory.ListAsync(_db, _me, 30, null, null, 100, _now, default)).Count);
        Assert.Equal(2, (await EconomyHistory.ListAsync(_db, _me, 30, "transfer", null, 100, _now, default)).Count);
        var received = await EconomyHistory.ListAsync(_db, _me, 30, "transfer", "in", 100, _now, default); Assert.Equal(40, J(received.Single()).GetProperty("amount").GetInt64());
        Assert.Single(await EconomyHistory.ListAsync(_db, _me, null, "coins", null, 100, _now, default));
        Assert.Equal(4, (await EconomyHistory.ListAsync(_db, _me, null, "invalid", "sideways", 100, _now, default)).Count);       // unknown filters are ignored
    }

    [Fact]
    public async Task TheSummaryAddsWhatCameInAndWhatWentOutAndTheReserved()
    {
        Add("user_transfer", 100, _me, _you); Add("user_transfer", 40, _you, _me); Add("coin_purchase", 2750, _me, null); Add("user_transfer", 500, _me, _you, status: "failed"); Add("user_transfer", 9, _me, _you, daysAgo: 40);
        _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = _me, AvailableCoins = 2000, CreatedAt = _now, UpdatedAt = _now });
        _db.PaymentHolds.AddRange(new PaymentHold { Id = Guid.NewGuid(), Key = "a", Purpose = "land", PayerUserId = _me, Amount = 300, TargetId = Guid.NewGuid(), Status = "held", CreatedAt = _now }, new PaymentHold { Id = Guid.NewGuid(), Key = "b", Purpose = "land", PayerUserId = _me, Amount = 999, TargetId = Guid.NewGuid(), Status = "settled", CreatedAt = _now });
        await _db.SaveChangesAsync();
        var s = J(await EconomyHistory.SummaryAsync(_db, _me, 30, _now, default));
        Assert.Equal((2790L, 100L, 3, 300L, 2000L, 2300L), (s.GetProperty("received").GetInt64(), s.GetProperty("spent").GetInt64(), s.GetProperty("count").GetInt32(), s.GetProperty("reserved").GetInt64(), s.GetProperty("available").GetInt64(), s.GetProperty("total").GetInt64()));
    }

    [Fact]
    public async Task OnlyWhoIsPartOfAnEntryGetsItsReceipt()
    {
        _db.Users.Add(new User { Id = _you, Username = "ana", NormalizedUsername = "ANA", Email = "a@t.l", NormalizedEmail = "A@T.L", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        var tx = Add("user_transfer", 100, _me, _you, metadata: "{\"message\":\"Aqui está o combinado\"}");
        var mine = J((await EconomyHistory.ReceiptAsync(_db, _me, tx.Id, default))!); Assert.Equal(("ana", "Aqui está o combinado", "out"), (mine.GetProperty("counterparty").GetString(), mine.GetProperty("message").GetString(), mine.GetProperty("direction").GetString()));
        Assert.Equal("in", J((await EconomyHistory.ReceiptAsync(_db, _you, tx.Id, default))!).GetProperty("direction").GetString());
        Assert.Null(await EconomyHistory.ReceiptAsync(_db, _stranger, tx.Id, default)); Assert.Null(await EconomyHistory.ReceiptAsync(_db, _me, Guid.NewGuid(), default));
        Assert.Equal(12, mine.GetProperty("code").GetString()!.Length);
    }
}
