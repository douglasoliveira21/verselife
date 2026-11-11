using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Keeps the look of the island (hour, weather, fog, light, sky; set by the owner in the management window) up to date: it asks the server when the person enters an island and every half minute after,
/// and gives it to <see cref="LandPresence"/>, which shows it wherever a parcel does not set its own. If the server cannot be reached the last look stays.
/// </summary>
public partial class IslandLookService : Node
{
    public const double RefreshSeconds = 30;

    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private Guid _island;
    private double _since;
    private bool _busy;
    private ParcelEnvironment? _look;
    private IslandMusic? _music;

    public ParcelEnvironment? Look => _look;
    public IslandMusic? Music => _music;
    public Func<Guid>? IslandProvider { get; set; }
    public event Action? Changed;

    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) => _transport = transport;

    public override void _Ready() { _transport ??= ScriptApi.HttpTransport(this); Connect(); }

    private void Connect()
    {
        if (GetParent()?.GetNodeOrNull<LandManager>("LandManager")?.Presence is { } presence) { presence.BaseLook = () => _look; presence.BaseMedia = () => _music; }
    }

    private Guid Current => IslandProvider?.Invoke() ?? GetNodeOrNull<NetworkManager>("/root/NetworkManager")?.CurrentIslandId ?? Guid.Empty;

    public override void _Process(double delta)
    {
        var island = Current;
        if (island != _island) { _island = island; _look = null; _music = null; _since = RefreshSeconds; Connect(); Apply(); }
        if (island == Guid.Empty || _busy) return;
        _since += delta; if (_since >= RefreshSeconds) { _since = 0; _ = RefreshAsync(); }
    }

    /// <summary>Asks the server for the look now. A failure keeps what was there.</summary>
    public async Task RefreshAsync()
    {
        if (_transport is null || _island == Guid.Empty || _busy) return;
        _busy = true; var island = _island;
        try
        {
            var (status, body) = await _transport("GET", $"/api/v1/worlds/islands/{island}/environment", null);
            if (island != _island || status is < 200 or >= 300) return;
            using var document = JsonDocument.Parse(body);
            var look = document.RootElement.TryGetProperty("look", out var element) && element.ValueKind == JsonValueKind.Object ? ParcelEnvironment.Parse(element.GetRawText()) : new ParcelEnvironment();
            var next = look.IsEmpty ? null : look;
            IslandMusic? music = null;
            if (document.RootElement.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object && media.TryGetProperty("musicUrl", out var url) && url.ValueKind == JsonValueKind.String && url.GetString() is { Length: > 0 } address && LandMediaUrl.Check(address) is null)
                music = new IslandMusic(address, media.TryGetProperty("volume", out var v) && v.TryGetSingle(out var volume) ? Math.Clamp(volume, 0f, 1f) : 1f, !(media.TryGetProperty("autoplay", out var a) && a.ValueKind == JsonValueKind.False), media.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "");
            if (!Equals(next, _look) || !Equals(music, _music)) { _look = next; _music = music; Apply(); }
        }
        catch (JsonException) { }
        finally { _busy = false; }
    }

    /// <summary>For tests and for the first frame: sets the island of this service.</summary>
    public void SetIslandForTesting(Guid island) { _island = island; _look = null; _music = null; }

    private void Apply()
    {
        Connect();
        if (GetParent()?.GetNodeOrNull<LandManager>("LandManager")?.Presence is { } presence) presence.Evaluate(true);
        Changed?.Invoke();
    }
}
