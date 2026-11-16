using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>
/// What sits over and beside the map: "Mostrar Terrenos" (parcels drawn over the islands that are big enough on screen, with a legend), the tabs Terrenos, Pessoas, Grupos and Eventos, the chosen spot of an island and
/// the teleport to it (the server decides if that spot can be reached and where the person arrives). Everything is read from the server for this player; nothing is invented.
/// </summary>
public partial class WorldMapWindow
{
    private sealed record EventEntry(Guid Id, string Name, Guid Island, DateTimeOffset Starts, DateTimeOffset Ends, float? X, float? Z);

    private ScrollContainer _parcelsPage = null!, _peoplePage = null!, _groupsPage = null!, _eventsPage = null!;
    private VBoxContainer _parcelRows = null!, _peopleRows = null!, _groupRows = null!, _eventRows = null!;
    private OptionButton _parcelFilter = null!;
    private Label _parcelTitle = null!, _peopleNote = null!;
    private CheckBox _parcelsToggle = null!, _friendsToggle = null!;
    private VBoxContainer _parcelLegend = null!;
    private Label _cardPoint = null!;
    private IReadOnlyList<MapFriend> _friends => Hub.Friends;
    private List<EventEntry> _events = [];
    private bool _peopleLoaded => Hub.FriendsLoaded; private bool _eventsLoaded; private string _peopleError => Hub.FriendsError;

    private static readonly string[] ParcelFilterKeys = ["", "mine", "sale", "rent", "public", "restricted", "group", "common"];

    private ScrollContainer Page(string name, out VBoxContainer column)
    {
        var page = new ScrollContainer { Visible = false, Name = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(320, 240) }; page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _stack.AddChild(page);
        column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; column.AddThemeConstantOverride("separation", 8); page.AddChild(column); return page;
    }

    private void BuildOverlays()
    {
        // "Mostrar Terrenos" and its legend, top right of the map
        _parcelsToggle = new CheckBox { Text = "Mostrar Terrenos", Name = "ParcelsToggle", FocusMode = Control.FocusModeEnum.None, ButtonPressed = Hub.Settings.ShowParcels, TooltipText = "Desenha os terrenos nas ilhas que estão grandes na tela" }; UiStyle.StyleButton(_parcelsToggle, UiStyle.ButtonKind.Tool, 10, 6, 10); _parcelsToggle.AddThemeFontSizeOverride("font_size", 14);
        _parcelsToggle.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.Minsize); _parcelsToggle.OffsetLeft = -200; _parcelsToggle.OffsetRight = -8; _parcelsToggle.OffsetTop = 8; _parcelsToggle.OffsetBottom = 44; _parcelsToggle.Toggled += SetParcels; _stack.AddChild(_parcelsToggle);
        var legendPanel = new PanelContainer { Name = "ParcelLegend", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore }; legendPanel.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.PanelFillSolid, .9f), new Color(UiStyle.BorderSoft, .7f), 10, 1, 10, 8));
        legendPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.Minsize); legendPanel.OffsetLeft = -200; legendPanel.OffsetRight = -8; legendPanel.OffsetTop = 50; _stack.AddChild(legendPanel);
        _parcelLegend = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; _parcelLegend.AddThemeConstantOverride("separation", 2); legendPanel.AddChild(_parcelLegend);
        void Entry(string state, string icon, string text) { var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 6); var swatch = new ColorRect { Color = new Color(WorldMapCanvas.ParcelColor(state), .75f), CustomMinimumSize = new Vector2(14, 14), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; row.AddChild(swatch); var picture = UiStyle.IconRect(icon, WorldMapCanvas.ParcelColor(state), 14); picture.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(picture); var label = Lbl(text, 12, false, UiStyle.Text); label.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(label); _parcelLegend.AddChild(row); }
        Entry("mine", "home", "Seu terreno"); Entry("group", "people", "Do seu grupo"); Entry("public", "map", "Aberto"); Entry("sale", "bag", "À venda"); Entry("rent", "calendar", "Para alugar (tracejado)"); Entry("restricted", "lock", "Restrito (hachurado)"); Entry("common", "star", "Área comum");
        _canvas.ShowParcels = Hub.Settings.ShowParcels; legendPanel.Visible = Hub.Settings.ShowParcels;
        _canvas.ParcelsNeeded += id => LoadParcels(id, null); _canvas.PointPicked += (_, _, _) => UpdatePointLine(); _canvas.ParcelPicked += (_, _) => UpdatePointLine();

        _parcelsPage = Page("Parcels", out var parcels); var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); parcels.AddChild(head);
        _parcelTitle = Lbl("", 16, true); _parcelTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; head.AddChild(_parcelTitle);
        _parcelFilter = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(180, 36) }; foreach (var s in new[] { "Todos os terrenos", "Meus", "À venda", "Para alugar", "Abertos", "Restritos", "Do grupo", "Áreas comuns" }) _parcelFilter.AddItem(s); _parcelFilter.ItemSelected += _ => FillParcels(); head.AddChild(_parcelFilter);
        _parcelRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _parcelRows.AddThemeConstantOverride("separation", 6); parcels.AddChild(_parcelRows);

        _peoplePage = Page("People", out var people); var phead = new HBoxContainer(); phead.AddThemeConstantOverride("separation", 8); people.AddChild(phead);
        _friendsToggle = new CheckBox { Text = "Mostrar amigos no mapa", FocusMode = Control.FocusModeEnum.None, ButtonPressed = Hub.Settings.ShowFriends, TooltipText = "Pontos verdes no mapa onde seus amigos estão agora" }; _friendsToggle.AddThemeFontSizeOverride("font_size", 14); _friendsToggle.Toggled += on => { Hub.Settings.ShowFriends = on; Hub.Settings.Save(); Hub.Changed("world.map_friends", on); RebuildPins(); }; phead.AddChild(_friendsToggle);
        _peopleNote = Lbl("", 14, false, UiStyle.TextMuted, wrap: true); people.AddChild(_peopleNote); _peopleRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _peopleRows.AddThemeConstantOverride("separation", 6); people.AddChild(_peopleRows);

        _groupsPage = Page("Groups", out var groups); _groupRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _groupRows.AddThemeConstantOverride("separation", 6); groups.AddChild(_groupRows);
        _eventsPage = Page("Events", out var events); _eventRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _eventRows.AddThemeConstantOverride("separation", 6); events.AddChild(_eventRows);
    }


    /// <summary>The settings window changed a map preference: the buttons and the overlay follow.</summary>
    private void OnSettings()
    {
        _parcelsToggle.SetPressedNoSignal(Hub.Settings.ShowParcels); _canvas.ShowParcels = Hub.Settings.ShowParcels; _parcelLegend.GetParent<Control>().Visible = Hub.Settings.ShowParcels && _tabs.Selected == 0;
        _friendsToggle.SetPressedNoSignal(Hub.Settings.ShowFriends); RebuildPins(); _canvas.QueueRedraw();
    }
    private void SetParcels(bool on)
    {
        Hub.Settings.ShowParcels = on; Hub.Settings.Save(); Hub.Changed("world.map_parcels", on); _canvas.ShowParcels = on; _parcelLegend.GetParent<Control>().Visible = on && _tabs.Selected == 0; if (!on) { _canvas.ClearPoint(); UpdatePointLine(); } _canvas.QueueRedraw();
    }

    public void SetParcelsForTesting(bool on) { _parcelsToggle.ButtonPressed = on; SetParcels(on); }

    // ---- parcels -------------------------------------------------------------------------------------------------------------------------------------

    private static string StateWords(string state) => state switch { "mine" => "Seu terreno", "group" => "Do seu grupo", "restricted" => "Restrito", "sale" => "À venda", "rent" => "Para alugar", "common" => "Área comum", _ => "Aberto" };

    public void LoadParcels(Guid island, Action? then)
    {
        Hub.Api.Send(Godot.HttpClient.Method.Get, $"/api/v1/map/islands/{island}/parcels", null, (status, body) =>
        {
            var list = new List<ParcelShape>();
            if (status is >= 200 and < 300)
            {
                try
                {
                    using var d = JsonDocument.Parse(body);
                    foreach (var p in Elements(d.RootElement, "items"))
                    {
                        if (!Guid.TryParse(Str(p, "id"), out var id) || !p.TryGetProperty("bounds", out var b)) continue; var owner = Str(p, "owner");
                        list.Add(new ParcelShape(id, Str(p, "name"), Str(p, "state"), owner, Str(p, "category"), Str(p, "description"), Num(p, "area"), Num(b, "minX"), Num(b, "minZ"), Num(b, "maxX"), Num(b, "maxZ"), (float)Num(p, "spawnX"), (float)Num(p, "spawnZ"),
                            p.TryGetProperty("teleport", out var t) && t.ValueKind == JsonValueKind.True, p.TryGetProperty("salePrice", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : null, p.TryGetProperty("rentPrice", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt64() : null));
                    }
                }
                catch (JsonException) { list.Clear(); }
            }
            else if (status != 404) { Say(status == 0 ? "Sem conexão com o servidor." : $"Os terrenos não carregaram (HTTP {status})."); return; }
            _canvas.Parcels[island] = list; _canvas.QueueRedraw(); then?.Invoke(); if (_tabs.Selected == 2) FillParcels();
        });
    }

    private AtlasIsland? ParcelsIsland()
    {
        if (_canvas.Selected != Guid.Empty && _items.FirstOrDefault(i => i.Id == _canvas.Selected) is { } chosen) return chosen;
        var current = Hub.CurrentIsland?.Invoke() ?? Guid.Empty; return current == Guid.Empty ? null : _items.FirstOrDefault(i => i.Id == current);
    }

    private void FillParcels()
    {
        foreach (var child in _parcelRows.GetChildren()) { _parcelRows.RemoveChild(child); child.QueueFree(); }
        if (ParcelsIsland() is not { } island) { _parcelTitle.Text = "Terrenos"; _parcelRows.AddChild(Lbl("Escolha uma ilha na aba Mundo (ou entre em uma) para ver os terrenos dela.", 15, false, UiStyle.TextMuted, wrap: true)); return; }
        _parcelTitle.Text = "Terrenos de " + island.Name;
        if (!_canvas.Parcels.TryGetValue(island.Id, out var all)) { _parcelRows.AddChild(Lbl("Carregando os terrenos…", 15, false, UiStyle.AccentText)); LoadParcels(island.Id, null); return; }
        var filter = ParcelFilterKeys[Math.Clamp(_parcelFilter.Selected, 0, ParcelFilterKeys.Length - 1)]; var shown = all.Where(p => filter.Length == 0 || p.State == filter).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (shown.Count == 0) { _parcelRows.AddChild(Lbl(all.Count == 0 ? "Esta ilha não tem terrenos que você possa ver." : "Nenhum terreno com este filtro.", 15, false, UiStyle.TextMuted, wrap: true)); return; }
        foreach (var parcel in shown)
        {
            var p = parcel; var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(WorldMapCanvas.ParcelColor(p.State), .6f), 10, 1, 10, 8)); _parcelRows.AddChild(card);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; info.AddThemeConstantOverride("separation", 0); row.AddChild(info);
            info.AddChild(Lbl(p.Name, 16, true)); info.AddChild(Lbl("● " + StateWords(p.State) + (p.SalePrice is { } sale ? $" · {sale:N0} moedas".Replace(',', '.') : string.Empty) + (p.RentPrice is { } rent ? $" · {rent:N0} moedas".Replace(',', '.') + " por período" : string.Empty), 13, false, WorldMapCanvas.ParcelColor(p.State)));
            info.AddChild(Lbl(string.Join(" · ", new[] { p.Category, p.Area > 0 ? $"{p.Area:0} m²" : string.Empty, p.Owner.Length > 0 ? "de " + p.Owner : string.Empty }.Where(t => t.Length > 0)), 13, false, UiStyle.TextMuted));
            var see = Btn("Ver no mapa", UiStyle.ButtonKind.Tool, () => ShowParcel(island, p), "Mostra o terreno no mapa", 34); see.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(see);
            var go = Btn("Teletransportar", UiStyle.ButtonKind.Primary, () => TeleportToPoint(island, (float)((p.MinX + p.MaxX) / 2), (float)((p.MinZ + p.MaxZ) / 2)), p.Teleport ? "O servidor confere o acesso ao terreno" : "Este terreno não aceita teletransporte", 34); go.Disabled = !p.Teleport; go.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(go);
        }
    }

    private void ShowParcel(AtlasIsland island, ParcelShape parcel)
    {
        if (!_parcelsToggle.ButtonPressed) { _parcelsToggle.ButtonPressed = true; SetParcels(true); }
        _tabs.Select(0); FocusIsland(island); var cx = (float)((parcel.MinX + parcel.MaxX) / 2); var cz = (float)((parcel.MinZ + parcel.MaxZ) / 2); var (gx, gy) = MapCoordinates.ToGlobal(island, cx, cz);
        var zoom = Math.Clamp(Math.Min(_canvas.Size.X, _canvas.Size.Y) * .5 / Math.Max(parcel.MaxX - parcel.MinX, parcel.MaxZ - parcel.MinZ), 1.2, 4); _canvas.GlideTo(gx, gy, zoom); _canvas.PickPoint(island, cx, cz);
    }

    // ---- the chosen spot -------------------------------------------------------------------------------------------------------------------------------

    private void UpdatePointLine()
    {
        if (_cardPoint is null) return;
        if (_canvas.Point is { } point && _selectedIsland?.Id == point.Island)
        {
            var parcel = _canvas.ParcelAt(point.Island, point.X, point.Z); _cardPoint.Text = $"Ponto: x {point.X:0}, z {point.Z:0}" + (parcel is null ? string.Empty : $" · {parcel.Name} ({StateWords(parcel.State).ToLowerInvariant()})"); _cardPoint.Visible = true; _teleport.Text = "Teletransportar até aqui";
        }
        else { _cardPoint.Visible = false; _teleport.Text = "Teletransportar"; }
        _card.ResetSize(); PlaceCard();
    }

    private void OnTeleportPressed()
    {
        if (_selectedIsland is not { } island) return;
        if (_canvas.Point is { } point && point.Island == island.Id) TeleportToPoint(island, point.X, point.Z); else Teleport(island);
    }

    private static string ArrivalWords(string reason) => reason switch
    {
        "closed" => "A ilha está temporariamente fechada.", "full" => "A ilha está cheia no momento. Tente de novo daqui a pouco.", "private" => "Esta ilha é privada: só entra quem foi convidado.", "banned" => "Você não pode entrar nesta ilha.",
        "island_not_found" => "Esta ilha não está mais disponível.", "bad_position" => "Esse ponto fica fora da ilha.", "parcel_banned" => "Você foi impedido de entrar neste terreno.", "parcel_restricted" => "Este terreno é restrito: só entra quem foi autorizado.",
        "parcel_no_teleport" => "Este terreno não aceita teletransporte.", _ => "Não foi possível ir para esse ponto agora."
    };

    /// <summary>Asks the server whether this spot can be reached by this player and where they would arrive (a parcel may send them to its own arrival point), then goes through the game's own teleport.</summary>
    public void TeleportToPoint(AtlasIsland island, float x, float z)
    {
        Say($"Verificando o acesso a {island.Name}…");
        Hub.Api.Send(Godot.HttpClient.Method.Post, "/api/v1/map/arrival", JsonSerializer.Serialize(new { islandId = island.Id, x, z }), (status, body) =>
        {
            if (status == 404) { Say(ArrivalWords("island_not_found")); Reload(); return; }
            if (status is < 200 or >= 300) { Say(status == 0 ? "Sem conexão com o servidor." : status == 429 ? "Calma! Muitas tentativas. Tente de novo daqui a pouco." : $"Não foi possível conferir o acesso (HTTP {status})."); return; }
            bool allowed; string reason, parcel; float rx, rz, forced; try { using var d = JsonDocument.Parse(body); var a = d.RootElement; allowed = a.TryGetProperty("allowed", out var al) && al.ValueKind == JsonValueKind.True; reason = Str(a, "reason"); parcel = Str(a, "parcelName"); rx = (float)Num(a, "x"); rz = (float)Num(a, "z"); forced = a.TryGetProperty("forced", out var f) && f.ValueKind == JsonValueKind.True ? 1 : 0; }
            catch (JsonException) { Say("Resposta inesperada do servidor."); return; }
            if (!allowed) { Say(ArrivalWords(reason)); if (reason is "island_not_found") Reload(); return; }
            if (Hub.GoToPoint is null && Hub.JoinIsland is null) { Say("O teleporte não está disponível agora."); return; }
            Say(forced > 0 ? $"{(parcel.Length > 0 ? parcel : "Este terreno")} leva você ao ponto de chegada dele." : $"Indo para {island.Name} ({rx:0}, {rz:0})…"); HideCard();
            if (Hub.GoToPoint is not null) Hub.GoToPoint(island.Id, rx, rz); else Hub.JoinIsland!(island.Id);
        });
    }

    private void TeleportHere(AtlasIsland island, double gx, double gy) { var (lx, lz, inside) = MapCoordinates.ToLocal(island, gx, gy); if (!inside) { Teleport(island); return; } TeleportToPoint(island, (float)lx, (float)lz); }

    // ---- people --------------------------------------------------------------------------------------------------------------------------------------------

    public void LoadPeople() => Hub.RefreshFriends();

    private void OnFriends() { RebuildPins(); if (_tabs.Selected == 3) FillPeople(); }

    private void FillPeople()
    {
        foreach (var child in _peopleRows.GetChildren()) { _peopleRows.RemoveChild(child); child.QueueFree(); }
        _peopleNote.Text = _peopleError.Length > 0 ? _peopleError : !_peopleLoaded ? "Carregando…" : _friends.Count == 0 ? "Nenhum amigo online que você possa ver agora. Quem deixa a presença privada não aparece aqui." : $"{_friends.Count} {(_friends.Count == 1 ? "amigo online" : "amigos online")} em ilhas do mapa.";
        foreach (var friend in _friends)
        {
            var f = friend; var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .6f), 10, 1, 10, 8)); _peopleRows.AddChild(card);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row); var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; info.AddThemeConstantOverride("separation", 0); row.AddChild(info);
            info.AddChild(Lbl("● " + f.Name, 16, true, WorldMapCanvas.Online)); info.AddChild(Lbl($"em {f.IslandName} · {f.X:0}, {f.Z:0}", 13, false, UiStyle.TextMuted));
            var see = Btn("Ver no mapa", UiStyle.ButtonKind.Tool, () => GoToSpot(f.Island, (float)f.X, (float)f.Z), null, 34); see.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(see);
            var go = Btn("Teletransportar", UiStyle.ButtonKind.Primary, () => { if (_items.FirstOrDefault(i => i.Id == f.Island) is { } island) TeleportToPoint(island, (float)f.X, (float)f.Z); else Say("Esta ilha não está no mapa agora."); }, "O servidor confere o acesso", 34); go.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(go);
        }
    }

    // ---- groups ----------------------------------------------------------------------------------------------------------------------------------------------

    private void FillGroups()
    {
        foreach (var child in _groupRows.GetChildren()) { _groupRows.RemoveChild(child); child.QueueFree(); }
        if (_groups.Count == 0) { _groupRows.AddChild(Lbl(_panelsLoaded ? "Você não está em nenhum grupo que tenha sede ou terrenos em ilhas do mapa." : "Carregando…", 15, false, UiStyle.TextMuted, wrap: true)); return; }
        foreach (var group in _groups)
        {
            _groupRows.AddChild(Lbl(group.Name + (group.Tag.Length > 0 ? $"  [{group.Tag}]" : string.Empty), 17, true, UiStyle.AccentText));
            foreach (var place in group.Places)
            {
                var s = place; var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .6f), 10, 1, 10, 8)); _groupRows.AddChild(card);
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row); var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; info.AddThemeConstantOverride("separation", 0); row.AddChild(info);
                info.AddChild(Lbl(s.Name, 16, true)); info.AddChild(Lbl(s.Note, 13, false, UiStyle.TextMuted));
                var see = Btn("Ver no mapa", UiStyle.ButtonKind.Tool, () => GoToSpot(s.Island), null, 34); see.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(see);
                var island = _items.FirstOrDefault(i => i.Id == s.Island); var go = Btn("Teletransportar", UiStyle.ButtonKind.Primary, () => { if (_items.FirstOrDefault(i => i.Id == s.Island) is { } target) Teleport(target); else Say("Esta ilha não está no mapa agora."); }, island is null ? "Esta ilha não está no mapa agora" : island.CanEnter ? "Entra nesta ilha" : StateOf(island).Text, 34);
                go.Disabled = island is null || !island.CanEnter; go.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(go);
            }
        }
    }

    // ---- events -----------------------------------------------------------------------------------------------------------------------------------------------

    private void ReadEvents(string body)
    {
        var list = new List<EventEntry>();
        try
        {
            using var d = JsonDocument.Parse(body);
            foreach (var e in d.RootElement.EnumerateArray())
            {
                if (!Guid.TryParse(Str(e, "id"), out var id) || !Guid.TryParse(Str(e, "islandId"), out var island)) continue;
                list.Add(new EventEntry(id, Str(e, "name"), island, DateTimeOffset.TryParse(Str(e, "startsAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ? s : DateTimeOffset.MinValue, DateTimeOffset.TryParse(Str(e, "endsAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var en) ? en : DateTimeOffset.MinValue,
                    e.TryGetProperty("arrivalX", out var ax) && ax.ValueKind == JsonValueKind.Number ? (float)ax.GetDouble() : null, e.TryGetProperty("arrivalZ", out var az) && az.ValueKind == JsonValueKind.Number ? (float)az.GetDouble() : null));
            }
        }
        catch (JsonException) { list.Clear(); }
        _events = list; _eventsLoaded = true;
    }

    private void FillEvents()
    {
        foreach (var child in _eventRows.GetChildren()) { _eventRows.RemoveChild(child); child.QueueFree(); }
        if (_events.Count == 0) { _eventRows.AddChild(Lbl(_eventsLoaded ? "Nenhum evento público marcado agora." : "Carregando…", 15, false, UiStyle.TextMuted, wrap: true)); return; }
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _events)
        {
            var ev = entry; var island = _items.FirstOrDefault(i => i.Id == ev.Island); var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color("#7FA8FF", .6f), 10, 1, 10, 8)); _eventRows.AddChild(card);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row); var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; info.AddThemeConstantOverride("separation", 0); row.AddChild(info);
            var live = ev.Starts <= now && ev.Ends >= now; info.AddChild(Lbl(ev.Name, 16, true)); info.AddChild(Lbl((live ? "● Acontecendo agora" : "Começa " + ev.Starts.ToLocalTime().ToString("dd/MM 'às' HH:mm", CultureInfo.GetCultureInfo("pt-BR"))) + " · " + (island?.Name ?? "ilha fora do mapa"), 13, false, live ? WorldMapCanvas.Online : UiStyle.TextMuted));
            var see = Btn("Ver no mapa", UiStyle.ButtonKind.Tool, () => GoToSpot(ev.Island, ev.X, ev.Z), null, 34); see.Disabled = island is null; see.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(see);
            var more = Btn("Detalhes", UiStyle.ButtonKind.Tool, () => OpenLink?.Invoke(LifeVerse.Client.Scripts.SearchLinks.For("event", ev.Id)), "Abre o evento na Busca", 34); more.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(more);
            var go = Btn("Teletransportar", UiStyle.ButtonKind.Primary, () => { if (island is null) return; if (ev.X is { } x && ev.Z is { } z) TeleportToPoint(island, x, z); else Teleport(island); }, island is null ? "Esta ilha não está no mapa agora" : island.CanEnter ? "Vai ao ponto de chegada do evento" : StateOf(island).Text, 34); go.Disabled = island is null || !island.CanEnter; go.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(go);
        }
    }

    // ---- tests ------------------------------------------------------------------------------------------------------------------------------------------------

    public bool ParcelsShownForTesting => _canvas.ShowParcels;
    public bool ParcelLegendForTesting => _parcelLegend.GetParent<Control>().Visible;
    public string PointLineForTesting => _cardPoint.Visible ? _cardPoint.Text : string.Empty;
    public string TeleportTextForTesting => _teleport.Text;
    public IReadOnlyList<string> ParcelRowsForTesting => [.. _parcelRows.GetChildren().OfType<PanelContainer>().Select(c => c.FindChildren("*", "Label", true, false).OfType<Label>().First().Text)];
    public string ParcelTitleForTesting => _parcelTitle.Text;
    public void SetParcelFilterForTesting(int index) { _parcelFilter.Select(index); FillParcels(); }
    public void PressParcelForTesting(int row, string button) => _parcelRows.GetChildren().OfType<PanelContainer>().ElementAt(row).FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == button).EmitSignal(BaseButton.SignalName.Pressed);
    public bool ParcelButtonEnabledForTesting(int row, string button) => !_parcelRows.GetChildren().OfType<PanelContainer>().ElementAt(row).FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == button).Disabled;
    public IReadOnlyList<string> PeopleForTesting => [.. _peopleRows.GetChildren().OfType<PanelContainer>().Select(c => c.FindChildren("*", "Label", true, false).OfType<Label>().First().Text)];
    public string PeopleNoteForTesting => _peopleNote.Text;
    public void PressPersonForTesting(int row, string button) => _peopleRows.GetChildren().OfType<PanelContainer>().ElementAt(row).FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == button).EmitSignal(BaseButton.SignalName.Pressed);
    public IReadOnlyList<string> GroupRowsForTesting => [.. _groupRows.GetChildren().SelectMany(c => c is Label l ? [l.Text] : c.FindChildren("*", "Label", true, false).OfType<Label>().Take(1).Select(x => x.Text))];
    public void PressGroupPlaceForTesting(int place, string button) => _groupRows.GetChildren().OfType<PanelContainer>().ElementAt(place).FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == button).EmitSignal(BaseButton.SignalName.Pressed);
    public IReadOnlyList<string> EventRowsForTesting => [.. _eventRows.GetChildren().OfType<PanelContainer>().Select(c => c.FindChildren("*", "Label", true, false).OfType<Label>().First().Text)];
    public void PressEventForTesting(int row, string button) => _eventRows.GetChildren().OfType<PanelContainer>().ElementAt(row).FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == button).EmitSignal(BaseButton.SignalName.Pressed);
    public string EventNoteForTesting => _eventRows.GetChildren().OfType<Label>().FirstOrDefault()?.Text ?? string.Empty;
    public void TeleportToPointForTesting(Guid island, float x, float z) { if (_items.FirstOrDefault(i => i.Id == island) is { } known) TeleportToPoint(known, x, z); }
}
