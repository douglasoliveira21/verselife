using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Loading;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The hard cases of loading and travelling: a server that never answers, a slow one, a connection lost in the middle of a trip, a reconnection, going back,
/// the window changing size, and the old island leaving nothing behind (no avatar twice).
/// </summary>
public partial class ScenarioSmoke : Node
{
    private readonly List<string> _calls = [];
    private readonly Queue<(long, string)> _prepare = new(), _connect = new();
    private Guid _transfer = Guid.NewGuid();
    private int _connectorCalls;

    private string Ticket() => $"{{\"token\":\"t\",\"host\":\"h\",\"port\":7000,\"protocolVersion\":{LifeVerse.Shared.Networking.NetworkProtocol.Version}}}";

    public override async void _Ready()
    {
        try
        {
            var session = GetNode<Autoloads.SessionManager>("/root/SessionManager"); session.Set("smoke-token", "smoke-refresh", "smoke");
            var network = GetNode<Autoloads.NetworkManager>("/root/NetworkManager");
            var entry = new WorldEntryCoordinator { Name = "WorldEntryCoordinator" }; AddChild(entry);
            var trip = new TeleportCoordinator { Name = "TeleportCoordinator" }; AddChild(trip); trip.Initialize(entry); await Frames(2);
            var manager = entry.Manager; var screen = entry.Screen; var island = Guid.NewGuid(); var origin = Guid.NewGuid();
            trip.Transport = Fake; trip.Connector = (_, _, _, _, _) => { _connectorCalls++; return Error.Ok; };
            string Prepared() => $"{{\"id\":\"{_transfer}\",\"status\":\"prepared\"}}";

            // the island server does not answer when connecting
            entry.BeginForTesting(LoadingMode.Entry, "Tropical Island"); manager.Complete("locate"); entry.StateForTesting("connecting", "c"); entry.ConnectWaitForTesting(WorldEntryCoordinator.ConnectTimeoutSeconds - 5);
            Require(!manager.Failed, "still waiting before the time is up");
            entry.ConnectWaitForTesting(10); await Frames(2);
            Require(manager.Failed && screen.ErrorForTesting.Contains("não respondeu dentro do tempo esperado") && screen.RetryVisibleForTesting, $"a server that never answers times out: {screen.ErrorForTesting}");
            entry.LeaveForTesting();

            // entering again after the connection was lost: a new loading opens by itself
            entry.StateForTesting("offline", "perdida"); Require(!manager.Active, "nothing runs while there is no loading");
            entry.StateForTesting("requesting_ticket", "x"); await Frames(2);
            Require(manager.Active && screen.IsOpen && screen.TitleForTesting == "Entrando no LifeVerse...", "a reconnection shows the loading again");
            entry.LeaveForTesting();

            // a slow destination server: it takes ten asks, and the trip still gets there
            _transfer = Guid.NewGuid(); _prepare.Enqueue((201, Prepared())); for (var i = 0; i < 10; i++) _connect.Enqueue((503, "{\"code\":\"server_starting\"}")); _connect.Enqueue((200, Ticket()));
            network.SetCurrentIslandForTesting(origin, true);
            Require(trip.Begin(island, "Praia Azul"), "trip starts from an island");
            await Wait(() => _connectorCalls == 1);
            Require(_calls.Count(c => c.Contains("/connect")) == 11 && !manager.Failed, "a slow server is waited for without giving up");
            entry.StateForTesting("connecting", "c"); entry.StateForTesting("online", "ok");
            // the connection is lost in the middle of the trip: the trip is cancelled and the person is offered the way back
            _calls.Clear(); entry.StateForTesting("offline", "perdida"); trip.StateForTesting("offline", "perdida"); await Frames(3);
            Require(manager.Failed && screen.ErrorForTesting.Contains("perdida") && _calls.Any(c => c.Contains($"{_transfer}/cancel")), "a lost connection cancels the trip");
            Require(screen.BackVisibleForTesting && screen.RetryVisibleForTesting, "going back and trying again are offered");
            entry.LeaveForTesting(); await Frames(1); Require(!trip.Busy && !network.TransferInProgress, "the trip is over and reconnection is allowed again");

            // the destination server never starts
            _calls.Clear(); _transfer = Guid.NewGuid(); _prepare.Enqueue((201, Prepared())); for (var i = 0; i < 40; i++) _connect.Enqueue((503, "{\"code\":\"server_starting\"}"));
            network.SetCurrentIslandForTesting(origin, true); trip.Begin(island, "Praia Azul"); await Wait(() => manager.Failed);
            Require(screen.ErrorForTesting.Contains("não iniciou a tempo") && screen.RetryVisibleForTesting && screen.BackVisibleForTesting, $"a server that does not start fails with a way back: {screen.ErrorForTesting}");
            Require(_calls.Count(c => c.Contains("/connect")) == TeleportCoordinator.MaxServerWaits + 1, "it asked as many times as allowed, not forever");
            _connect.Clear();

            // going back before anything was switched: the person never left the island they were on
            screen.PressBackForTesting(); await Frames(2);
            Require(!trip.Busy && !screen.IsOpen && network.CurrentIslandId == origin && network.IsAuthenticated, "going back leaves the person exactly where they were");

            // the old island leaves nothing behind when the connection is switched
            var changes = new List<string>(); void Watch(string state, string detail) => changes.Add(state); network.ConnectionStateChanged += Watch;
            network.OnPlayerSpawned(2, Guid.NewGuid().ToString(), "Vizinho", "{}", new Vector3(1, 0, 1), 0); network.OnPlayerSpawned(3, Guid.NewGuid().ToString(), "Outro", "{}", new Vector3(2, 0, 2), 0);
            Require(network.RemoteAvatarCountForTesting == 2, "the neighbours of the old island are in the scene");
            network.LeaveCurrentIslandForTesting(); await Frames(2);
            Require(network.RemoteAvatarCountForTesting == 0 && !network.IsAuthenticated && changes.Contains("switching"), "switching islands removes the old avatars at once and tells the rest of the game");
            network.ConnectionStateChanged -= Watch; network.SetCurrentIslandForTesting(Guid.Empty, false);

            // the window changes size while loading
            Require(LoadingScreen.PanelWidthFor(1920) == 820 && LoadingScreen.PanelWidthFor(900) == 820 && LoadingScreen.PanelWidthFor(600) == 520 && LoadingScreen.PanelWidthFor(300) == 360, "the panel follows the width of the window within its limits");
            entry.BeginForTesting(LoadingMode.Entry, "Ilha"); await Frames(2);
            var wide = screen.PanelWidthForTesting; screen.FitPanelForTesting(); Require(screen.PanelWidthForTesting >= 360 && screen.PanelWidthForTesting <= 820 && wide >= 360, "refitting never breaks the limits");
            entry.LeaveForTesting();

            GD.Print("event=scenario_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=scenario_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private Task<(long, string)> Fake(string method, string path, string? body)
    {
        _calls.Add($"{method} {path}");
        if (path.EndsWith("/prepare")) return Task.FromResult(_prepare.Count > 0 ? _prepare.Dequeue() : (500L, "{}"));
        if (path.EndsWith("/connect")) return Task.FromResult(_connect.Count > 0 ? _connect.Dequeue() : (500L, "{}"));
        return Task.FromResult((200L, "{}"));
    }

    private async Task Wait(Func<bool> condition) { for (var i = 0; i < 900 && !condition(); i++) await Frames(1); if (!condition()) throw new InvalidOperationException("timed out waiting"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
