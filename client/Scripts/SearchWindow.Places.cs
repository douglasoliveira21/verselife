using System.Text.Json;
using Godot;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The Places tab (and the island of an event): the chosen place shows its owner, category, rating, how many are there now and whether this person can enter (open, full, closed, private, banned), and the buttons:
/// teleport (only when entering is possible), show on the map, favorite, copy the location link, and, for the owner, whether guests may announce events there. What can be entered is decided by the server.
/// </summary>
public partial class SearchWindow
{
    /// <summary>Shows a spot of an island on the map window (the game supplies it; the map only draws the island the avatar is on).</summary>
    public Action<Guid, float, float>? ShowOnMap { get; set; }

    private VBoxContainer _placeBox = null!;
    private Button _mapButton = null!, _copyLink = null!, _guestEvents = null!;
    private int _placeSeq;
    private string _access = string.Empty;
    private bool _guestEventsAllowed;
    private Guid _placeIsland;

    private static bool IsPlaceKind(string kind) => kind is SearchKinds.Island or SearchKinds.Parcel or SearchKinds.Place;

    private void BuildPlaceActions(Control parent)
    {
        _placeBox = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _placeBox.AddThemeConstantOverride("separation", 6); parent.AddChild(_placeBox);
        _mapButton = Action("Mostrar no mapa", UiStyle.ButtonKind.Secondary, OnShowOnMap); _placeBox.AddChild(_mapButton);
        _copyLink = Action("Copiar localização", UiStyle.ButtonKind.Secondary, OnCopyLocation); _placeBox.AddChild(_copyLink);
        _guestEvents = Action("Eventos de convidados: bloqueados", UiStyle.ButtonKind.Secondary, OnGuestEvents); _guestEvents.Visible = false; _placeBox.AddChild(_guestEvents);
    }

    /// <summary>Reads the island of the chosen place (or event) for this viewer and draws the facts and the access state.</summary>
    private void LoadPlace(SearchHit hit)
    {
        if (hit.IslandId is not { } island) return;
        var seq = ++_placeSeq; _placeIsland = island; _access = string.Empty; var lines = _extra.Text;
        Send(Godot.HttpClient.Method.Get, SearchApi.IslandInfoPath(island), null, (status, body) =>
        {
            if (seq != _placeSeq || Selected?.Id != hit.Id) return;
            if (status == 404) { _access = "gone"; _placeNote = "Este lugar não está mais disponível."; ApplyAccess(hit); return; }
            if (status is < 200 or >= 300) { _placeNote = string.Empty; return; } // not known: the button stays as it was and the server decides when entering
            try
            {
                using var document = JsonDocument.Parse(body); var p = document.RootElement; _access = Str(p, "access");
                var facts = new List<string>();
                if (p.TryGetProperty("owner", out var owner) && Str(owner, "displayName") is { Length: > 0 } name) facts.Add("Proprietário(a): " + name + ".");
                var traits = new[] { Str(p, "category"), "classificação " + Str(p, "contentRating"), Str(p, "language") }.Where(t => t.Length > 0 && !t.EndsWith(' ')); facts.Add(string.Join(" · ", traits));
                var max = Int(p, "maxPlayers");
                facts.Add(Flag(p, "online") ? $"{(p.TryGetProperty("visitors", out var v) && v.TryGetInt32(out var visitors) ? visitors : 0)} {(p.TryGetProperty("visitors", out var v2) && v2.TryGetInt32(out var one) && one == 1 ? "visitante" : "visitantes")} agora" + (max > 0 ? $" (limite {max})" : "") + "." : "Ilha em repouso: ela liga quando alguém entra.");
                _placeNote = _access switch { "full" => "Ilha cheia no momento: tente de novo daqui a pouco.", "closed" => "Ilha temporariamente fechada.", "banned" => "Você foi banido desta ilha.", "private" => "Ilha privada: só convidados entram.", _ => string.Empty };
                _guestEventsAllowed = Flag(p, "allowsGuestEvents"); _guestEvents.Visible = Flag(p, "isOwner"); UpdateGuestEventsButton();
                _placeFacts = string.Join("\n", facts.Where(l => l.Length > 0));
            }
            catch (JsonException) { return; }
            ApplyAccess(hit);
        });
    }

    private string _placeNote = string.Empty, _placeFacts = string.Empty;

    /// <summary>The teleport button says what is possible, and the extra lines carry the facts of the island below what the chosen thing says about itself.</summary>
    private void ApplyAccess(SearchHit hit)
    {
        var shown = IsPlaceKind(hit.Kind) ? string.Join("\n", new[] { _placeFacts, _placeNote }.Where(t => t.Length > 0)) : _placeNote;
        _placeExtra.Text = shown; _placeExtra.Visible = shown.Length > 0;
        if (!hit.HasPlace || _access is "" || (hit.Kind == SearchKinds.Event && _eventStatus is "ended" or "cancelled" or "gone")) return;
        var blocked = _access is not "open";
        _goButton.Disabled = blocked;
        if (blocked) _goButton.Text = _access switch { "full" => "Ilha cheia", "closed" => "Ilha fechada", "banned" => "Acesso negado", "private" => "Ilha privada", _ => "Indisponível" };
    }

    private Label _placeExtra = null!;

    private void OnShowOnMap()
    {
        if (Selected is not { } hit || hit.IslandId is not { } island) return;
        if (ShowOnMap is null) { _note.Text = "O mapa não está disponível agora."; return; }
        ShowOnMap(island, hit.X ?? 0, hit.Z ?? 0);
    }

    private void OnCopyLocation()
    {
        if (Selected is not { } hit) return; var island = hit.IslandId ?? hit.Id;
        DisplayServer.ClipboardSet(SearchLinks.For(hit.Kind == SearchKinds.Parcel ? SearchKinds.Parcel : SearchKinds.Island, hit.Kind == SearchKinds.Parcel ? hit.Id : island, hit.X, hit.Z)); _note.Text = "Link da localização copiado.";
    }

    private void UpdateGuestEventsButton() => _guestEvents.Text = _guestEventsAllowed ? "Eventos de convidados: permitidos" : "Eventos de convidados: bloqueados";

    private void OnGuestEvents()
    {
        if (_placeIsland == Guid.Empty) return; var wanted = !_guestEventsAllowed;
        Send(Godot.HttpClient.Method.Put, SearchApi.EventPolicyPath(_placeIsland), JsonSerializer.Serialize(new { allowGuests = wanted }), (status, _) =>
        {
            if (status is < 200 or >= 300) { _note.Text = status == 404 ? "Só o dono da ilha decide isso." : Failure(status); return; }
            _guestEventsAllowed = wanted; UpdateGuestEventsButton(); _note.Text = wanted ? "Agora convidados podem anunciar eventos nesta ilha." : "Só você anuncia eventos nesta ilha.";
        });
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public string AccessForTesting => _access;
    public bool PlaceBoxVisibleForTesting => _placeBox.Visible;
    public string PlaceExtraForTesting => _placeExtra.Visible ? _placeExtra.Text : string.Empty;
    public string GuestEventsTextForTesting => _guestEvents.Visible ? _guestEvents.Text : string.Empty;
    public void PressMapForTesting() => OnShowOnMap();
    public void PressCopyLinkForTesting() => OnCopyLocation();
    public void PressGuestEventsForTesting() => OnGuestEvents();
}
