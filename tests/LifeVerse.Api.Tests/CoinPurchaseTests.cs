using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Buying VerseCoin: coins come only from a confirmed payment, once, for the right amount; refunds take them back; the signature is checked.</summary>
public sealed class CoinPurchaseTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly IPaymentProvider _provider = new TestPaymentProvider();
    private readonly DateTimeOffset _now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _user = Guid.NewGuid();
    private static readonly CoinPackage Pack = CoinPackages.Defaults.Single(p => p.Id == "intermediario");

    public void Dispose() => _db.Dispose();

    private async Task<CoinOrder> Order(string key = "order-key-0001") => (await CoinPurchases.CreateAsync(_db, _provider, _user, Pack, "pix", key, _now, default)).Order;
    private async Task<long> Balance() => (await EconomyEndpoints.EnsureWallet(_db, _user, default)).AvailableCoins;
    private Task<PaymentOutcome> Hook(CoinOrder o, string status, long? cents = null) => CoinPurchases.ApplyAsync(_db, _provider, new WebhookEvent(o.ProviderRef!, status, cents ?? o.PriceCents), _now.AddMinutes(1), default);

    [Fact]
    public async Task AnOrderStartsPendingAndCreditsNothing()
    {
        var order = await Order();
        Assert.Equal(("pending", 2750L, 2500L), (order.Status, order.Coins, order.PriceCents));
        Assert.Equal(0, await Balance());
    }

    [Fact]
    public async Task TheSameKeyGivesTheSameOrder()
    {
        var a = await Order(); var (b, existed) = await CoinPurchases.CreateAsync(_db, _provider, _user, Pack, "pix", "order-key-0001", _now, default);
        Assert.True(existed); Assert.Equal(a.Id, b.Id); Assert.Equal(1, await _db.CoinOrders.CountAsync());
    }

    [Fact]
    public async Task AConfirmedPaymentCreditsThePackWithItsBonusOnce()
    {
        var order = await Order();
        Assert.Equal(PaymentOutcome.Credited, await Hook(order, "paid"));
        Assert.Equal(2750, await Balance());
        Assert.Equal(PaymentOutcome.AlreadyApplied, await Hook(order, "paid")); Assert.Equal(PaymentOutcome.AlreadyApplied, await Hook(order, "paid"));
        Assert.Equal(2750, await Balance());
        Assert.Equal(1, await _db.LedgerTransactions.CountAsync(x => x.Type == "coin_purchase"));
    }

    [Fact]
    public async Task ThePersonIsToldWhenTheCoinsArriveEvenOffline()
    {
        var order = await Order(); await Hook(order, "paid"); await Hook(order, "paid");
        var notice = await _db.PropertyNotices.SingleAsync(n => n.UserId == _user);
        Assert.Equal(("coins_credited", "Compra concluída: VC$ 2.750"), (notice.Kind, notice.Title));
    }

    [Fact]
    public async Task APaymentOfAnotherAmountCreditsNothing()
    {
        var order = await Order();
        Assert.Equal(PaymentOutcome.AmountMismatch, await Hook(order, "paid", 100));
        Assert.Equal(0, await Balance()); Assert.StartsWith("amount_mismatch", (await _db.CoinOrders.SingleAsync()).Reason);
    }

    [Fact]
    public async Task AnUnknownReferenceIsNotAnOrder()
    {
        await Order();
        Assert.Equal(PaymentOutcome.UnknownOrder, await CoinPurchases.ApplyAsync(_db, _provider, new WebhookEvent("test-nope", "paid", 2500), _now, default));
        Assert.Equal(0, await Balance());
    }

    [Fact]
    public async Task ARefundTakesTheCoinsBack()
    {
        var order = await Order(); await Hook(order, "paid");
        Assert.Equal(PaymentOutcome.Refunded, await Hook(order, "refunded"));
        Assert.Equal(0, await Balance()); Assert.Equal("refunded", (await _db.CoinOrders.SingleAsync()).Status);
        Assert.Contains(await _db.PropertyNotices.Where(n => n.UserId == _user).Select(n => n.Kind).ToListAsync(), k => k == "coins_refunded");
        Assert.Equal(PaymentOutcome.AlreadyApplied, await Hook(order, "refunded"));
    }

    [Fact]
    public async Task ARefundAfterTheCoinsWereSpentHoldsTheWalletForReview()
    {
        var order = await Order(); await Hook(order, "paid");
        var wallet = await EconomyEndpoints.EnsureWallet(_db, _user, default); wallet.AvailableCoins = 1000; await _db.SaveChangesAsync();
        Assert.Equal(PaymentOutcome.HeldForReview, await Hook(order, "refunded"));
        wallet = await EconomyEndpoints.EnsureWallet(_db, _user, default);
        Assert.Equal((0L, true), (wallet.AvailableCoins, wallet.IsBlocked)); Assert.Equal("chargeback_review", (await _db.CoinOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task AFailedChargeClosesThePendingOrderAndAPaidOneIsUntouched()
    {
        var failed = await Order("order-key-0002"); Assert.Equal(PaymentOutcome.Failed, await Hook(failed, "failed"));
        var paid = await Order("order-key-0003"); await Hook(paid, "paid");
        Assert.Equal(PaymentOutcome.AlreadyApplied, await Hook(paid, "failed")); Assert.Equal(2750, await Balance());
    }

    [Fact]
    public async Task OldPendingOrdersExpireButALatePaymentStillCredits()
    {
        var order = await Order();
        Assert.Equal(1, await CoinPurchases.ExpireAsync(_db, _now.AddHours(2), default));
        Assert.Equal("expired", (await _db.CoinOrders.SingleAsync()).Status);
        Assert.Equal(PaymentOutcome.Credited, await Hook(order, "paid")); Assert.Equal(2750, await Balance());
    }

    [Fact]
    public void OnlyTheRightSignatureIsAccepted()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Economy:Payments:WebhookSecret"] = "segredo" }).Build();
        var body = "{\"providerRef\":\"x\",\"status\":\"paid\",\"amountCents\":1}";
        Assert.True(_provider.Verify(body, TestPaymentProvider.Sign(body, "segredo"), config));
        Assert.False(_provider.Verify(body, TestPaymentProvider.Sign(body, "outro"), config)); Assert.False(_provider.Verify(body + " ", TestPaymentProvider.Sign(body, "segredo"), config));
        Assert.False(_provider.Verify(body, null, config)); Assert.False(_provider.Verify(body, "x", new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void TheTestProviderIsNeverActiveInProduction()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Economy:Payments:Provider"] = "test" }).Build();
        Assert.NotNull(CoinPayments.Active(config, new FakeEnvironment("Development"))); Assert.Null(CoinPayments.Active(config, new FakeEnvironment("Production"))); Assert.Null(CoinPayments.Active(new ConfigurationBuilder().Build(), new FakeEnvironment("Development")));
    }

    private sealed class FakeEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name; public string ApplicationName { get; set; } = "t"; public string ContentRootPath { get; set; } = "."; public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
