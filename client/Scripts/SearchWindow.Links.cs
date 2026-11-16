using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Items;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The links of the game ("lifeverse://event/…", a person, a group, an item, a store or a place by its stable id), the preference to keep the history of searches, and the keyboard: Up and Down move through the
/// results, Enter opens the chosen one, Ctrl+Tab changes the tab. A link only ever opens one of these panels; what it points to is read from the server, which says whether this person may see it.
/// </summary>
public partial class SearchWindow
{
    /// <summary>The preference "Guardar histórico de buscas" (Preferências > Privacidade). Off: nothing is remembered or shown, and what was kept is forgotten.</summary>
    public static bool KeepHistory { get; set; } = true;

    public static void ForgetHistoryFile() { try { DirAccess.RemoveAbsolute(HistoryFile); } catch (Exception) { } }

    private IEnumerable<string> RememberedSearches => KeepHistory ? _history.Items : [];

    // ---- links ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Puts one found thing in front on its tab, chosen (what a link leads to).</summary>
    private void ShowHitOnTab(SearchHit hit, int tab)
    {
        _keepHit = hit;
        if (_tabs.Selected == tab) { ClearResults(); ShowKeptHit(); } else _tabs.Select(tab);
    }

    /// <summary>Opens what a link points to. People and groups open their own windows; the rest is shown here, on its tab, chosen. A link that points to nothing (gone, or not for this person) says so.</summary>
    public void OpenLink(string? link)
    {
        if (!SearchLinks.TryParse(link, out var kind, out var id, out var x, out var z)) { Open(); _note.Text = "Este link não é do LifeVerse."; return; }
        if (kind == "person") { OpenProfile?.Invoke(id); return; }
        if (kind == "group") { OpenGroup?.Invoke(id); return; }
        Open(); _note.Text = "Abrindo o link…";
        void Missing(int status) => _note.Text = status == 404 ? "Este link não leva a nada agora: o conteúdo não existe mais ou você não pode vê-lo." : Failure(status);
        switch (kind)
        {
            case "store": ShowStoreById(id); break;
            case "island":
                Send(Godot.HttpClient.Method.Get, SearchApi.IslandInfoPath(id), null, (status, body) =>
                {
                    if (status is < 200 or >= 300) { Missing(status); return; }
                    try
                    {
                        using var d = JsonDocument.Parse(body); var p = d.RootElement; var category = Str(p, "category");
                        ShowHitOnTab(new SearchHit(SearchKinds.Island, "Ilha", id, Str(p, "name"), "Ilha · " + category, Str(p, "description"), category, Str(p, "coverImageUrl") is { Length: > 0 } cover ? cover : null, id, x ?? Float(p, "spawnX"), z ?? Float(p, "spawnZ"), 0, false), 2);
                    }
                    catch (JsonException) { Missing(0); }
                });
                break;
            case "event":
                Send(Godot.HttpClient.Method.Get, SearchApi.EventPath(id), null, (status, body) =>
                {
                    if (status is < 200 or >= 300) { Missing(status); return; }
                    try
                    {
                        using var d = JsonDocument.Parse(body); var p = d.RootElement; p.TryGetProperty("island", out var island); Guid.TryParse(Str(island, "id"), out var islandId);
                        DateTimeOffset.TryParse(Str(p, "startsAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start); DateTimeOffset.TryParse(Str(p, "endsAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end);
                        ShowHitOnTab(new SearchHit(SearchKinds.Event, "Evento", id, Str(p, "name"), "Evento em " + Str(island, "name"), Str(p, "description"), Str(p, "category"), Str(p, "imageUrl") is { Length: > 0 } image ? image : null, islandId == Guid.Empty ? null : islandId, Float(p, "arrivalX"), Float(p, "arrivalZ"), 0, false, start, end, Long(p, "priceCoins"), Str(p, "contentRating")), 3);
                    }
                    catch (JsonException) { Missing(0); }
                });
                break;
            case "item":
                Send(Godot.HttpClient.Method.Get, SearchApi.ItemPath(id), null, (status, body) =>
                {
                    if (status is < 200 or >= 300) { Missing(status); return; }
                    try
                    {
                        using var d = JsonDocument.Parse(body); var p = d.RootElement; p.TryGetProperty("rights", out var r);
                        var mask = (Flag(r, "copy") ? ItemRights.Copy : 0) | (Flag(r, "modify") ? ItemRights.Modify : 0) | (Flag(r, "transfer") ? ItemRights.Transfer : 0) | (Flag(r, "resell") ? ItemRights.Resell : 0);
                        ShowHitOnTab(new SearchHit(SearchKinds.Product, "Produto", id, Str(p, "name"), string.Empty, Str(p, "description"), Str(p, "category"), Str(p, "imageUrl") is { Length: > 0 } image ? image : null, null, null, null, 0, false, null, null, Long(p, "priceCoins"), string.Empty, mask), 5);
                    }
                    catch (JsonException) { Missing(0); }
                });
                break;
        }
    }

    private static float? Float(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : null;

    // ---- the keyboard --------------------------------------------------------------------------------------------------------------

    /// <summary>Up and Down go through the results from the search box or a result; Ctrl+Tab (Shift: back) goes through the tabs.</summary>
    private bool HandleKeys(InputEventKey key)
    {
        if (key.CtrlPressed && key.Keycode == Key.Tab) { _tabs.Select((_tabs.Selected + (key.ShiftPressed ? _tabs.Count - 1 : 1)) % _tabs.Count); return true; }
        if (key.Keycode is Key.Down or Key.Up && _cards.Count > 0 && GetViewport().GuiGetFocusOwner() is { } owner && (owner == _query || _cards.Contains(owner)))
        {
            var next = Math.Clamp((_selectedIndex < 0 ? (key.Keycode == Key.Down ? -1 : _cards.Count) : _selectedIndex) + (key.Keycode == Key.Down ? 1 : -1), 0, _cards.Count - 1);
            Select(next); _resultScroll.EnsureControlVisible(_cards[next]); return true;
        }
        return false;
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public bool PressKeyForTesting(Key key, bool ctrl = false, bool shift = false) => HandleKeys(new InputEventKey { Keycode = key, Pressed = true, CtrlPressed = ctrl, ShiftPressed = shift });
    public void FocusQueryForTesting() => _query.GrabFocus();
    public int SelectedIndexForTesting => _selectedIndex;
    public IReadOnlyList<string> RecentForTesting => [.. RememberedSearches];
}
