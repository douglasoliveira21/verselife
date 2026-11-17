using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The balance is pushed to who is connected, as the database has it, and a missing channel never breaks an operation.</summary>
public sealed class EconomyPushTests : IDisposable
{
    private sealed class FakeSocket : WebSocket
    {
        public List<string> Sent { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => null; public override string? CloseStatusDescription => null; public override string? SubProtocol => null; public override WebSocketState State => WebSocketState.Open;
        public override void Abort() { } public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask; public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask; public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool end, CancellationToken c) { Sent.Add(Encoding.UTF8.GetString(b)); return Task.CompletedTask; }
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly Guid _a = Guid.NewGuid(), _b = Guid.NewGuid();
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task EveryoneConnectedHearsTheirOwnBalance()
    {
        var hub = new NotificationHub(TimeProvider.System, NullLogger<NotificationHub>.Instance); var sa = new FakeSocket(); var offline = Guid.NewGuid();
        hub.TryAdd(_a, Guid.NewGuid(), sa);
        _db.Wallets.AddRange(new Wallet { Id = Guid.NewGuid(), UserId = _a, AvailableCoins = 950, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }, new Wallet { Id = Guid.NewGuid(), UserId = _b, AvailableCoins = 50, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await _db.SaveChangesAsync();
        var services = new ServiceCollection().AddSingleton(hub).BuildServiceProvider();
        await EconomyPush.WalletChangedAsync(services, _db, default, _a, _b, _a, offline);
        var message = Assert.Single(sa.Sent); using var doc = JsonDocument.Parse(message);
        Assert.Equal((EconomyPush.EventType, 950L), (doc.RootElement.GetProperty("type").GetString(), doc.RootElement.GetProperty("data").GetProperty("balance").GetInt64()));
    }

    [Fact]
    public async Task WithoutAChannelNothingHappensAndNothingBreaks()
    {
        await EconomyPush.WalletChangedAsync(null, _db, default, _a);
        await EconomyPush.WalletChangedAsync(new ServiceCollection().BuildServiceProvider(), _db, default, _a);
    }
}
