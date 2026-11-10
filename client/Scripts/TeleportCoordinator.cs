using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Travelling to another island as a controlled transfer: ask the API to prepare the trip (it checks the destination, the access and the room, and reserves a place),
/// ask for the single-use ticket (starting the destination server when needed), and only then leave the current island and connect with that ticket. The trip is
/// complete when the destination server has confirmed the arrival, which this node checks with the API. Until the connection is switched the person can cancel and
/// stays where they are; if anything fails afterwards the trip is cancelled and the person can try again, go back to the island they left, or return to the menu.
/// </summary>
public partial class TeleportCoordinator : Node
{
    public const int MaxServerWaits = 15, ArrivalChecks = 24;
    public const double ServerWaitSeconds = 4, ArrivalCheckSeconds = .5;

    private NetworkManager _network = null!;
    private AppManager _app = null!;
    private SessionManager _session = null!;
    private WorldEntryCoordinator _entry = null!;
    private Guid _destination, _source, _transfer;
    private string _name = string.Empty, _key = string.Empty;
    private bool _switched, _staleRetried;
    private int _serverWaits, _arrivalChecks;
    private long _generation;

    public bool Busy { get; private set; }
    /// <summary>Test seam: replaces the HTTP calls. Receives method, path and body; answers with a status and a JSON text.</summary>
    public Func<string, string, string?, Task<(long Status, string Body)>>? Transport { get; set; }
    /// <summary>Test seam: replaces the connection to the destination (called with the ticket); the real one asks the network manager to connect.</summary>
    public Func<Guid, string, string, int, int, Error>? Connector { get; set; }
    public Guid TransferId => _transfer;

    public void Initialize(WorldEntryCoordinator entry)
    {
        _entry = entry; _network = GetNode<NetworkManager>("/root/NetworkManager"); _app = GetNode<AppManager>("/root/AppManager"); _session = GetNode<SessionManager>("/root/SessionManager");
        _network.ConnectionStateChanged += OnState;
        entry.Manager.LoadingCompleted += OnLoaded;
    }

    public override void _ExitTree() { if (_network is not null) _network.ConnectionStateChanged -= OnState; }

    // ---- starting -------------------------------------------------------------------------------------------

    /// <summary>Starts a trip. False when another one is going on or there is no session; asking for the island the person is already on does nothing.</summary>
    public bool Begin(Guid island, string? name = null)
    {
        if (Busy || island == Guid.Empty || !_session.IsAuthenticated) return false;
        Busy = true; _generation++; _network.TransferInProgress = true;
        _destination = island; _name = name ?? string.Empty; _source = _network.CurrentIslandId; _key = NewKey(); _transfer = Guid.Empty; _switched = false; _staleRetried = false; _serverWaits = 0;
        _entry.BeginTrip(island, _name, Retry, GoBack, Leave, CancelBeforeSwitch);
        _ = Prepare(_generation);
        return true;
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private async Task<(long Status, JsonElement? Json)> Call(string method, string path, string? body)
    {
        long status; string text;
        if (Transport is not null) (status, text) = await Transport(method, path, body);
        else (status, text) = await Http(method, path, body);
        try { using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text); return (status, document.RootElement.Clone()); }
        catch (JsonException) { return (status, null); }
    }

    private async Task<(long, string)> Http(string method, string path, string? body)
    {
        var request = new HttpRequest { Timeout = 15 }; AddChild(request);
        try
        {
            var headers = new[] { $"Authorization: Bearer {_session.AccessToken}", "Content-Type: application/json" };
            var error = request.Request(_app.ApiBaseUrl + path, headers, method == "GET" ? Godot.HttpClient.Method.Get : Godot.HttpClient.Method.Post, body ?? string.Empty);
            if (error != Error.Ok) return (0, "{}");
            var done = await ToSignal(request, HttpRequest.SignalName.RequestCompleted);
            var result = (long)done[0].AsInt64(); var code = (long)done[1].AsInt64(); var bytes = done[3].AsByteArray();
            return (result == (long)HttpRequest.Result.Success ? code : 0, Encoding.UTF8.GetString(bytes));
        }
        finally { request.QueueFree(); }
    }

    private static string? Code(JsonElement? json) => json is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;

    // ---- prepare and connect --------------------------------------------------------------------------------

    private async Task Prepare(long generation)
    {
        var body = JsonSerializer.Serialize(new { destinationIslandId = _destination, sourceIslandId = _source == Guid.Empty ? (Guid?)null : _source, idempotencyKey = _key });
        var (status, json) = await Call("POST", "/api/v1/worlds/teleports/prepare", body);
        if (generation != _generation || !Busy) return;
        if (status is 200 or 201 && json is { } ok && ok.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var transfer) && ok.TryGetProperty("status", out var state) && state.GetString() == "prepared")
        {
            _transfer = transfer; _entry.TripPrepared(); await Connect(generation); return;
        }
        if (status == 409 && Code(json) == "transfer_in_progress" && !_staleRetried && json is { } busy && busy.TryGetProperty("transfer", out var other) && other.ValueKind == JsonValueKind.Object && other.TryGetProperty("id", out var otherId) && Guid.TryParse(otherId.GetString(), out var stale))
        {
            // a trip left half done earlier (a closed game, a lost connection): give it up and start this one
            _staleRetried = true; await Call("POST", $"/api/v1/worlds/teleports/{stale}/cancel", null);
            if (generation != _generation || !Busy) return;
            _key = NewKey(); await Prepare(generation); return;
        }
        var (message, retry) = Explain(status, Code(json));
        FailTrip("locate", message, retry);
    }

    private async Task Connect(long generation)
    {
        var (status, json) = await Call("POST", $"/api/v1/worlds/teleports/{_transfer}/connect", null);
        if (generation != _generation || !Busy) return;
        if (status == 200 && json is { } ok && ok.TryGetProperty("token", out var token) && ok.TryGetProperty("host", out var host) && ok.TryGetProperty("port", out var port) && ok.TryGetProperty("protocolVersion", out var protocol))
        {
            _entry.TripTicketReady(); _switched = true;
            var error = Connector is not null ? Connector(_destination, token.GetString() ?? string.Empty, host.GetString() ?? string.Empty, port.GetInt32(), protocol.GetInt32())
                : _network.JoinWithTicket(_destination, token.GetString() ?? string.Empty, host.GetString() ?? string.Empty, port.GetInt32(), protocol.GetInt32());
            if (error != Error.Ok) FailTrip("connect", $"Não foi possível iniciar a conexão com a ilha de destino ({error}).", true);
            return;
        }
        if (status == 503 && Code(json) == "server_starting")
        {
            if (++_serverWaits > MaxServerWaits) { FailTrip("server", "A ilha de destino não iniciou a tempo. Tente de novo em instantes.", true); return; }
            await ToSignal(GetTree().CreateTimer(Transport is null ? ServerWaitSeconds : .01), SceneTreeTimer.SignalName.Timeout);
            if (generation != _generation || !Busy) return;
            await Connect(generation); return;
        }
        var (message, retry) = Explain(status, Code(json));
        FailTrip("server", message, retry);
    }

    private static (string Message, bool Retry) Explain(long status, string? code) => (code, status) switch
    {
        ("island_not_found", _) => ("Essa ilha não existe mais.", false),
        ("island_private", _) => ("Essa ilha é privada e você não tem acesso a ela.", false),
        ("access_denied", _) => ("Você não tem permissão para entrar nessa ilha.", false),
        ("island_full", _) => ("A ilha de destino está lotada no momento. Tente de novo em instantes.", true),
        ("invalid_destination", _) => ("O ponto de destino não fica dentro dessa ilha.", false),
        ("same_island", _) => ("Você já está nessa ilha.", false),
        ("avatar_required", _) => ("Crie o seu avatar antes de viajar.", false),
        ("token_used", _) or ("expired", _) or ("cancelled", _) or ("reservation_expired", _) => ("A reserva dessa viagem acabou. Tente de novo.", true),
        (_, 401) => ("Sua sessão expirou. Entre novamente.", false),
        (_, 0) => ("Não foi possível falar com o servidor. Verifique a conexão e tente de novo.", true),
        _ => ($"O servidor recusou a viagem ({status}).", true)
    };

    private void FailTrip(string step, string message, bool retry)
    {
        if (_transfer != Guid.Empty) _ = Call("POST", $"/api/v1/worlds/teleports/{_transfer}/cancel", null); // frees the reservation at the destination
        _entry.TripFailed(step, message, retry);
    }

    // ---- what happens on the connection, and the arrival ----------------------------------------------------

    private void OnState(string state, string detail)
    {
        if (!Busy) return;
        if (state == "online" && _switched) { _arrivalChecks = 0; _ = ConfirmArrival(_generation); return; }
        if (state is "failed" or "incompatible" or "offline" && _switched) { _ = Call("POST", $"/api/v1/worlds/teleports/{_transfer}/cancel", null); }
    }

    /// <summary>The destination server told the API the person is there; the trip only counts as done when the API says so.</summary>
    private async Task ConfirmArrival(long generation)
    {
        while (generation == _generation && Busy && _arrivalChecks++ < ArrivalChecks)
        {
            var (status, json) = await Call("GET", $"/api/v1/worlds/teleports/{_transfer}", null);
            if (generation != _generation || !Busy) return;
            var state = json is { } o && o.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (status == 200 && state == "arrived") { _entry.TripArrivalConfirmed(); return; }
            if (status == 200 && state is "cancelled" or "expired" or "failed") { _entry.TripFailed("ready", "A ilha de destino não confirmou a sua chegada.", true); return; }
            await ToSignal(GetTree().CreateTimer(Transport is null ? ArrivalCheckSeconds : .01), SceneTreeTimer.SignalName.Timeout);
        }
        if (generation == _generation && Busy) _entry.TripFailed("ready", "A ilha de destino não confirmou a sua chegada a tempo.", true);
    }

    private void OnLoaded() { if (!Busy) return; Busy = false; _network.TransferInProgress = false; _switched = false; }

    // ---- the actions on the loading screen ------------------------------------------------------------------

    private void Finish() { Busy = false; _generation++; _network.TransferInProgress = false; _switched = false; }

    /// <summary>Another attempt of the same trip with a new key (the old reservation is cancelled first).</summary>
    private void Retry()
    {
        if (!Busy) return;
        _generation++; var generation = _generation;
        if (_transfer != Guid.Empty) _ = Call("POST", $"/api/v1/worlds/teleports/{_transfer}/cancel", null);
        _key = NewKey(); _transfer = Guid.Empty; _switched = false; _staleRetried = false; _serverWaits = 0; _network.TransferInProgress = true;
        _ = Prepare(generation);
    }

    /// <summary>Cancelled before the connection was switched: the reservation is released and the person is still on the island they were on.</summary>
    private void CancelBeforeSwitch()
    {
        if (!Busy || _switched) return;
        var transfer = _transfer; Finish();
        if (transfer != Guid.Empty) _ = Call("POST", $"/api/v1/worlds/teleports/{transfer}/cancel", null);
        _entry.TripCancelledByUser();
    }

    /// <summary>Back to the island the trip started from: with the old connection still open nothing is needed; otherwise the person enters it again like on any entry.</summary>
    private void GoBack()
    {
        var transfer = _transfer; var source = _source; var switched = _switched; Finish();
        if (transfer != Guid.Empty) _ = Call("POST", $"/api/v1/worlds/teleports/{transfer}/cancel", null);
        if (!switched && _network.IsAuthenticated) { _network.RestorePreviousIsland(); return; }
        if (source != Guid.Empty) _network.JoinIsland(source);
    }

    private void Leave()
    {
        var transfer = _transfer; Finish();
        if (transfer != Guid.Empty) _ = Call("POST", $"/api/v1/worlds/teleports/{transfer}/cancel", null);
    }

    public void CancelForTesting() => CancelBeforeSwitch();
    public bool SwitchedForTesting => _switched;
    public void StateForTesting(string state, string detail) => OnState(state, detail);
}
