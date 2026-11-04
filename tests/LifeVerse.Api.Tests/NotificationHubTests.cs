using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LifeVerse.Api.Modules.Social;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class NotificationHubTests
{
    private sealed class FakeSocket : WebSocket
    {
        private readonly System.Threading.Channels.Channel<WebSocketReceiveResult> _incoming = System.Threading.Channels.Channel.CreateUnbounded<WebSocketReceiveResult>();
        private WebSocketState _state = WebSocketState.Open;
        public List<string> Sent { get; } = [];
        public bool BreakOnSend { get; set; }
        public override WebSocketCloseStatus? CloseStatus { get; }
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public void ClientCloses() { _state = WebSocketState.CloseReceived; _incoming.Writer.TryWrite(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true)); }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            if (BreakOnSend) throw new WebSocketException("gone");
            Sent.Add(Encoding.UTF8.GetString(buffer.AsSpan())); return Task.CompletedTask;
        }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) => await _incoming.Reader.ReadAsync(ct);
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) { _state = WebSocketState.Closed; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) { _state = WebSocketState.CloseSent; return Task.CompletedTask; }
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() { }
    }

    private static NotificationHub Hub() => new(TimeProvider.System, NullLogger<NotificationHub>.Instance);

    [Fact]
    public async Task AnEventReachesEveryConnectionOfTheRightUserOnly()
    {
        var hub = Hub(); var alice = Guid.NewGuid(); var bob = Guid.NewGuid();
        var a1 = new FakeSocket(); var a2 = new FakeSocket(); var b = new FakeSocket();
        hub.TryAdd(alice, Guid.NewGuid(), a1); hub.TryAdd(alice, Guid.NewGuid(), a2); hub.TryAdd(bob, Guid.NewGuid(), b);
        await hub.NotifyAsync(alice, "message", new { content = "oi" });
        Assert.Single(a1.Sent); Assert.Single(a2.Sent); Assert.Empty(b.Sent);
        using var json = JsonDocument.Parse(a1.Sent[0]);
        Assert.Equal("message", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("oi", json.RootElement.GetProperty("data").GetProperty("content").GetString());
        Assert.True(json.RootElement.TryGetProperty("at", out _));
    }

    [Fact]
    public async Task NotifyingSomeoneOfflineIsHarmless() => await Hub().NotifyAsync(Guid.NewGuid(), "message", new { });

    [Fact]
    public async Task ADeadSocketIsDroppedWithoutBreakingTheOthers()
    {
        var hub = Hub(); var user = Guid.NewGuid();
        var dead = new FakeSocket { BreakOnSend = true }; var alive = new FakeSocket();
        hub.TryAdd(user, Guid.NewGuid(), dead); hub.TryAdd(user, Guid.NewGuid(), alive);
        await hub.NotifyAsync(user, "friend_request", new { from = 1 });
        Assert.Single(alive.Sent);
        Assert.Equal(1, hub.ConnectionCount(user));
    }

    [Fact]
    public void ConnectionsPerUserAreCapped()
    {
        var hub = Hub(); var user = Guid.NewGuid();
        for (var i = 0; i < NotificationHub.MaxConnectionsPerUser; i++) Assert.True(hub.TryAdd(user, Guid.NewGuid(), new FakeSocket()));
        Assert.False(hub.TryAdd(user, Guid.NewGuid(), new FakeSocket()));
    }

    [Fact]
    public async Task RunKeepsTheConnectionRegisteredUntilTheClientCloses()
    {
        var hub = Hub(); var user = Guid.NewGuid(); var socket = new FakeSocket();
        var run = hub.RunAsync(user, socket, default);
        await Task.Delay(50);
        Assert.Equal(1, hub.ConnectionCount(user));
        await hub.NotifyAsync(user, "group_invite", new { groupId = Guid.NewGuid() });
        Assert.Single(socket.Sent);
        socket.ClientCloses();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, hub.ConnectionCount(user));
    }

    [Fact]
    public async Task RunStopsWhenTheServerShutsDown()
    {
        var hub = Hub(); var user = Guid.NewGuid(); using var stop = new CancellationTokenSource();
        var run = hub.RunAsync(user, new FakeSocket(), stop.Token);
        await Task.Delay(50); stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, hub.ConnectionCount(user));
    }
}

public sealed class NotificationFormatterTests
{
    [Theory]
    [InlineData("""{"type":"friend_request","data":{"from":"x"}}""", "Você recebeu um pedido de amizade.")]
    [InlineData("""{"type":"friend_accepted","data":{}}""", "Seu pedido de amizade foi aceito.")]
    [InlineData("""{"type":"group_invite","data":{}}""", "Você foi convidado para um grupo.")]
    [InlineData("""{"type":"message","data":{"content":"oi"}}""", "Nova mensagem: oi")]
    [InlineData("""{"type":"group_announcement","data":{"groupName":"Clube","title":"Reunião"}}""", "Clube — anúncio: Reunião")]
    [InlineData("""{"type":"group_event","data":{"groupName":"Clube","name":"Caça"}}""", "Clube — novo evento: Caça")]
    [InlineData("""{"type":"group_request_decided","data":{"groupName":"Clube","approved":true}}""", "Você entrou em Clube.")]
    [InlineData("""{"type":"group_request_decided","data":{"groupName":"Clube","approved":false}}""", "Seu pedido para entrar em Clube foi recusado.")]
    [InlineData("""{"type":"group_kicked","data":{}}""", "Você foi removido de um grupo.")]
    public void KnownEventsBecomeShortText(string json, string expected) => Assert.Equal(expected, LifeVerse.Shared.Networking.NotificationFormatter.Describe(json));

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"type":7}""")]
    [InlineData("""{"type":"something_new","data":{}}""")]
    public void UnknownOrBrokenEventsAreIgnored(string json) => Assert.Null(LifeVerse.Shared.Networking.NotificationFormatter.Describe(json));

    [Fact]
    public void LongMessagesAreClipped()
    {
        var text = LifeVerse.Shared.Networking.NotificationFormatter.Describe("{\"type\":\"message\",\"data\":{\"content\":\"" + new string((char)97, 300) + "\"}}")!;
        Assert.True(text.Length < 100); Assert.EndsWith("…", text);
    }
}
