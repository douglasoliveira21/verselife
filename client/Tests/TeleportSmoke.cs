using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Loading;

namespace LifeVerse.Client.Tests;

/// <summary>The trip between islands on the client: prepare, ticket, switching the connection, confirmation of the arrival, cancelling and recovering from failures.</summary>
public partial class TeleportSmoke : Node
{
    private readonly List<string> _calls = [];
    private readonly Queue<(long, string)> _connectAnswers = new();
    private readonly Queue<(long, string)> _prepareAnswers = new();
    private readonly Queue<string> _statusAnswers = new();
    private readonly List<string> _prepareBodies = [];
    private int _connectorCalls;
    private Guid _transfer = Guid.NewGuid();

    public override async void _Ready()
    {
        try
        {
            var session = GetNode<Autoloads.SessionManager>("/root/SessionManager"); session.Set("smoke-token", "smoke-refresh", "smoke");
            var network = GetNode<Autoloads.NetworkManager>("/root/NetworkManager");
            var entry = new WorldEntryCoordinator { Name = "WorldEntryCoordinator" }; AddChild(entry);
            var trip = new TeleportCoordinator { Name = "TeleportCoordinator" }; AddChild(trip); trip.Initialize(entry); await Frames(2);
            var manager = entry.Manager; var screen = entry.Screen; var island = Guid.NewGuid();
            trip.Transport = Fake; trip.Connector = (id, token, host, port, protocol) => { _connectorCalls++; return Error.Ok; };
            string Prepared() => $"{{\"id\":\"{_transfer}\",\"destinationIslandId\":\"{island}\",\"status\":\"prepared\"}}";

            // a trip that works: the destination server is still starting, then the ticket arrives, the connection is switched and the destination confirms the arrival
            _prepareAnswers.Enqueue((201, Prepared()));
            _connectAnswers.Enqueue((503, "{\"code\":\"server_starting\",\"state\":\"starting\"}")); _connectAnswers.Enqueue((200, $"{{\"transferId\":\"{_transfer}\",\"token\":\"t\",\"host\":\"127.0.0.1\",\"port\":7000,\"protocolVersion\":{LifeVerse.Shared.Networking.NetworkProtocol.Version}}}"));
            Require(trip.Begin(island, "Tropical Island") && trip.Busy && !trip.Begin(island, "Tropical Island"), "a second trip cannot start while one is going on");
            Require(screen.TitleForTesting == "Viajando para outra ilha..." && screen.CancelVisibleForTesting, "the loading opens with a cancel while nothing was left yet");
            await Wait(() => _connectorCalls == 1);
            Require(_calls[0].StartsWith("POST /api/v1/worlds/teleports/prepare") && _prepareBodies[0].Contains(island.ToString()) && _prepareBodies[0].Contains("idempotencyKey"), "the trip is prepared first, with a key");
            Require(_calls.Count(c => c.Contains("/connect")) == 2, "the destination server was waited for");
            Require(trip.SwitchedForTesting && !screen.CancelVisibleForTesting && manager.Step("server")!.State == StepState.Completed, "once the ticket is in hand the connection is switched and cancel is gone");
            entry.StateForTesting("connecting", "c"); entry.StateForTesting("authenticating", "a"); entry.StateForTesting("online", "ok"); trip.StateForTesting("online", "ok");
            entry.ProgressForTesting("snapshot", 1, 1); entry.ProgressForTesting("terrain", 1, 1); entry.ProgressForTesting("objects", 0, 0); entry.ProgressForTesting("assets", 0, 0); entry.ProgressForTesting("spawn", 1, 1);
            _statusAnswers.Enqueue("connecting");
            await Frames(3);
            Require(!manager.Succeeded && manager.Progress < 1, "everything is loaded but the destination has not confirmed the arrival: the trip is not over");
            _statusAnswers.Enqueue("arrived");
            await Wait(() => manager.Succeeded);
            Require(entry.ArrivalOkForTesting && !_calls.Any(c => c.Contains("/cancel")), "the arrival was confirmed by the API and nothing was cancelled");
            await Frames(2); Require(!trip.Busy, "the trip is over");
            await Frames(50);

            // cancelling while the server starts: the reservation is released and the connection never switched
            _calls.Clear(); _connectorCalls = 0; _transfer = Guid.NewGuid();
            _prepareAnswers.Enqueue((201, Prepared())); for (var i = 0; i < 50; i++) _connectAnswers.Enqueue((503, "{\"code\":\"server_starting\"}"));
            Require(trip.Begin(island, "Tropical Island"), "a new trip can start");
            await Wait(() => _calls.Count(c => c.Contains("/connect")) >= 2);
            Require(screen.CancelVisibleForTesting, "cancel is offered while preparing");
            screen.PressCancelForTesting(); await Frames(3);
            Require(!trip.Busy && !screen.IsOpen && _connectorCalls == 0 && _calls.Any(c => c.Contains($"{_transfer}/cancel")), "cancelled: reservation released, nothing connected, loading closed");
            _connectAnswers.Clear();

            // a private island: refused with the reason and no retry
            _calls.Clear(); _prepareAnswers.Enqueue((403, "{\"code\":\"island_private\"}"));
            trip.Begin(island, "Reservada"); await Wait(() => manager.Failed);
            Require(screen.ErrorForTesting.Contains("privada") && !screen.RetryVisibleForTesting, $"a private island has no retry: {screen.ErrorForTesting}");
            entry.LeaveForTesting(); await Frames(1); Require(!trip.Busy, "leaving frees the trip");

            // a full island can be retried later
            _prepareAnswers.Enqueue((409, "{\"code\":\"island_full\"}"));
            trip.Begin(island, "Lotada"); await Wait(() => manager.Failed);
            Require(screen.ErrorForTesting.Contains("lotada") && screen.RetryVisibleForTesting, "a full island offers a new attempt");
            entry.LeaveForTesting(); await Frames(1);

            // a trip left half done earlier is given up and the new one goes on
            _calls.Clear(); _prepareBodies.Clear(); var stale = Guid.NewGuid(); _transfer = Guid.NewGuid();
            _prepareAnswers.Enqueue((409, $"{{\"code\":\"transfer_in_progress\",\"transfer\":{{\"id\":\"{stale}\"}}}}")); _prepareAnswers.Enqueue((201, Prepared()));
            _connectAnswers.Enqueue((200, $"{{\"token\":\"t\",\"host\":\"h\",\"port\":7000,\"protocolVersion\":{LifeVerse.Shared.Networking.NetworkProtocol.Version}}}"));
            trip.Begin(island, "Tropical Island"); await Wait(() => _connectorCalls == 1);
            Require(_calls.Any(c => c.Contains($"{stale}/cancel")) && _prepareBodies.Count == 2 && _prepareBodies[0] != _prepareBodies[1], "the stale trip was cancelled and the new one used a new key");

            // a failure after the switch: the trip is cancelled at the API and the person can try again
            _calls.Clear(); _prepareBodies.Clear();
            entry.StateForTesting("connecting", "c"); entry.StateForTesting("failed", "Conexão recusada."); trip.StateForTesting("failed", "Conexão recusada."); await Frames(2);
            Require(manager.Failed && screen.RetryVisibleForTesting && _calls.Any(c => c.Contains($"{_transfer}/cancel")), "a failure after the switch cancels the trip and offers a new attempt");
            _transfer = Guid.NewGuid(); _prepareAnswers.Enqueue((201, Prepared()));
            _connectAnswers.Enqueue((200, $"{{\"token\":\"t2\",\"host\":\"h\",\"port\":7000,\"protocolVersion\":{LifeVerse.Shared.Networking.NetworkProtocol.Version}}}")); var before = _connectorCalls;
            entry.RetryForTesting(); await Wait(() => _connectorCalls == before + 1);
            Require(_prepareBodies.Count == 1 && manager.Active, "retry prepares a new trip");
            entry.LeaveForTesting(); await Frames(1);

            // a destination that refuses the arrival
            _calls.Clear(); _transfer = Guid.NewGuid(); _prepareAnswers.Enqueue((201, Prepared()));
            _connectAnswers.Enqueue((200, $"{{\"token\":\"t\",\"host\":\"h\",\"port\":7000,\"protocolVersion\":{LifeVerse.Shared.Networking.NetworkProtocol.Version}}}"));
            before = _connectorCalls; trip.Begin(island, "Tropical Island"); await Wait(() => _connectorCalls == before + 1);
            entry.StateForTesting("connecting", "c"); entry.StateForTesting("online", "ok"); _statusAnswers.Enqueue("cancelled"); trip.StateForTesting("online", "ok");
            await Wait(() => manager.Failed);
            Require(screen.ErrorForTesting.Contains("não confirmou"), $"a destination that does not confirm fails the trip: {screen.ErrorForTesting}");
            entry.LeaveForTesting(); await Frames(1);

            GD.Print("event=teleport_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=teleport_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private Task<(long, string)> Fake(string method, string path, string? body)
    {
        _calls.Add($"{method} {path}");
        if (path.EndsWith("/prepare")) { _prepareBodies.Add(body ?? ""); return Task.FromResult(_prepareAnswers.Count > 0 ? _prepareAnswers.Dequeue() : (500L, "{}")); }
        if (path.EndsWith("/connect")) return Task.FromResult(_connectAnswers.Count > 0 ? _connectAnswers.Dequeue() : (500L, "{}"));
        if (method == "GET") return Task.FromResult((200L, $"{{\"id\":\"{_transfer}\",\"status\":\"{(_statusAnswers.Count > 0 ? _statusAnswers.Dequeue() : "connecting")}\"}}"));
        return Task.FromResult((200L, "{}"));
    }

    private async Task Wait(Func<bool> condition) { for (var i = 0; i < 600 && !condition(); i++) await Frames(1); if (!condition()) throw new InvalidOperationException("timed out waiting for the trip"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
