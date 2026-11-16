using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>The tabs Grupos, Terrenos, Favoritos, Conquistas and Estatísticas. They only draw what the server sent for this viewer: what the person hid is simply not there.</summary>
public partial class ProfileWindow
{
    private const int TabAbout = 0, TabFeed = 1, TabGroups = 2, TabParcels = 3, TabFavorites = 4, TabAchievements = 5, TabStats = 6, TabFirstLife = 7, TabMarriage = 8;
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Takes the person to an island (the game supplies it); used by the favorites.</summary>
    public Action<Guid>? VisitIsland { get; set; }

    private VBoxContainer _parcelRows = null!, _favoriteRows = null!, _achievementRows = null!;
    private GridContainer _statGrid = null!;
    private CheckBox _showFavorites = null!, _hideAnniversary = null!, _hideStats = null!, _hideAchievements = null!;

    // ---- test access -------------------------------------------------------------------------------------------------------
    public int ParcelRowsForTesting => _parcelRows.GetChildren().OfType<PanelContainer>().Count();
    public int FavoriteRowsForTesting => _favoriteRows.GetChildren().OfType<PanelContainer>().Count();
    public int AchievementRowsForTesting => _achievementRows.GetChildren().OfType<PanelContainer>().Count();
    public IReadOnlyList<string> StatsForTesting => [.. _statGrid.GetChildren().OfType<PanelContainer>().Select(c => string.Join(" ", c.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];
    public string EmptyTextForTesting(int tab) => string.Join(" ", _pages[tab].FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public void PressFavoriteForTesting(int index) => _favoriteRows.GetChildren().OfType<PanelContainer>().ElementAt(index).FindChildren("*", "Button", true, false).OfType<Button>().First().EmitSignal(BaseButton.SignalName.Pressed);
    public void ToggleParcelRowForTesting(int index, bool on) => _parcelRows.GetChildren().OfType<PanelContainer>().ElementAt(index).FindChildren("*", "CheckBox", true, false).OfType<CheckBox>().First().ButtonPressed = on;
    public void SetPrivacyForTesting(bool showFavorites, bool hideAnniversary, bool hideStats, bool hideAchievements) { _showFavorites.ButtonPressed = showFavorites; _hideAnniversary.ButtonPressed = hideAnniversary; _hideStats.ButtonPressed = hideStats; _hideAchievements.ButtonPressed = hideAchievements; }
    public int SelectedTabForTesting => _tabs.Selected;

    private static Control Page(string title, out VBoxContainer rows, string? hint = null)
    {
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 8);
        inner.AddChild(Text(Heading(title), 15, true, UiStyle.AccentText));
        if (hint is not null) inner.AddChild(Text(hint, 14, false, UiStyle.TextMuted));
        rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rows.AddThemeConstantOverride("separation", 8); inner.AddChild(rows);
        return Card(inner);
    }

    private static void Clear(Container box) { foreach (var child in box.GetChildren()) { box.RemoveChild(child); child.QueueFree(); } }

    private Control BuildParcelsTab() => Page("Terrenos", out _parcelRows);
    private Control BuildFavoritesTab() => Page("Favoritos", out _favoriteRows);
    private Control BuildAchievementsTab() => Page("Conquistas", out _achievementRows);

    private Control BuildStatsTab()
    {
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 8); inner.AddChild(Text(Heading("Estatísticas"), 15, true, UiStyle.AccentText));
        _statGrid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _statGrid.AddThemeConstantOverride("h_separation", 8); _statGrid.AddThemeConstantOverride("v_separation", 8); inner.AddChild(_statGrid);
        return Card(inner);
    }

    // ---- filling -----------------------------------------------------------------------------------------------------------

    private void FillSections()
    {
        FillParcelsTab(); FillFavorites(); FillAchievements(); FillStats();
        if (_statGrid is not null) _statGrid.Columns = _infoColumn.Size.X >= 620 ? 3 : 2;
    }

    private static Label Muted(string text) => Text(text, 15, false, UiStyle.TextMuted);

    private void FillParcelsTab()
    {
        Clear(_parcelRows); var self = IsSelf; var list = ParcelList().ToList();
        if (list.Count == 0) { _parcelRows.AddChild(Muted(Flag(_profile, "privateProfile") ? "Este perfil é privado." : self ? "Você ainda não tem terrenos." : "Nenhum terreno em destaque.")); return; }
        if (self) _parcelRows.AddChild(Muted($"Marque até {MaxParcelsShown} terrenos para aparecerem no seu perfil."));
        foreach (var parcel in list) _parcelRows.AddChild(ParcelCard(parcel, self));
    }

    /// <summary>One parcel: name, category, island, area, and "Visitar" (and, for my own land, the box that features it).</summary>
    private PanelContainer ParcelCard(JsonElement parcel, bool withToggle)
    {
        Guid.TryParse(Str(parcel, "id"), out var parcelId); Guid.TryParse(Str(parcel, "islandId"), out var islandId);
        var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .6f), 10, 1, 10, 8));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
        row.AddChild(UiStyle.IconRect(Str(parcel, "category") == "comercial" ? "bag" : "home", UiStyle.AccentText, 28));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 0); row.AddChild(text);
        var title = Text((Str(parcel, "code") is { Length: > 0 } code ? code + " · " : "") + Str(parcel, "name"), 16, true); title.AutowrapMode = TextServer.AutowrapMode.Off; title.ClipText = true; text.AddChild(title);
        var area = parcel.TryGetProperty("area", out var a) && a.ValueKind == JsonValueKind.Number ? $" · {a.GetDouble():0} m²" : "";
        text.AddChild(Text($"{CategoryLabel(Str(parcel, "category"))} · {Str(parcel, "islandName")}{area}", 13, false, UiStyle.TextMuted));
        if (withToggle)
        {
            var box = new CheckBox { Text = " destacar", ButtonPressed = Flag(parcel, "shown"), FocusMode = Control.FocusModeEnum.None, TooltipText = "Mostra este terreno no seu perfil" };
            var note = Text("", 13, false, UiStyle.Orange); text.AddChild(note);
            box.Toggled += on => { if (!_filling) _ = ToggleParcelAsync(parcelId, on, box, note); }; row.AddChild(box);
        }
        if (VisitParcel is not null && parcelId != Guid.Empty)
        {
            var visit = new Button { Text = "Visitar", FocusMode = Control.FocusModeEnum.None, TooltipText = "Vai até este terreno (a ilha decide se você pode entrar)", CustomMinimumSize = new Vector2(0, 34), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; UiStyle.StyleButton(visit, UiStyle.ButtonKind.Tool, 10, 4, 8);
            visit.Pressed += () => VisitParcel?.Invoke(islandId, parcelId); row.AddChild(visit);
        }
        return card;
    }

    private void FillFavorites()
    {
        Clear(_favoriteRows); var self = IsSelf;
        var list = _profile.ValueKind == JsonValueKind.Object && _profile.TryGetProperty("favorites", out var f) && f.ValueKind == JsonValueKind.Array ? f.EnumerateArray().ToList() : [];
        var shown = self && _profile.TryGetProperty("settings", out var s) && Flag(s, "showFavorites");
        if (self) _favoriteRows.AddChild(Muted(shown ? "Seus favoritos aparecem para outras pessoas." : "Seus favoritos só aparecem para você. Ative “Mostrar meus favoritos” em Privacidade para divulgá-los."));
        if (list.Count == 0) { _favoriteRows.AddChild(Muted(Flag(_profile, "privateProfile") ? "Este perfil é privado." : self ? "Você ainda não favoritou ilhas, lugares ou eventos. Use a estrela na Busca." : "Nenhum favorito à mostra.")); return; }
        foreach (var favorite in list)
        {
            Guid.TryParse(Str(favorite, "islandId"), out var island);
            var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .6f), 10, 1, 10, 8));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
            row.AddChild(UiStyle.IconRect(Str(favorite, "kind") == "event" ? "calendar" : Str(favorite, "kind") == "poi" ? "pin" : "globe", UiStyle.Orange, 26));
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 0); row.AddChild(text);
            var title = Text(Str(favorite, "title"), 16, true); title.AutowrapMode = TextServer.AutowrapMode.Off; title.ClipText = true; text.AddChild(title);
            text.AddChild(Text(Str(favorite, "label") + (Str(favorite, "subtitle") is { Length: > 0 } sub ? " · " + sub : ""), 13, false, UiStyle.TextMuted));
            if (VisitIsland is not null && island != Guid.Empty)
            {
                var go = new Button { Text = "Ir", FocusMode = Control.FocusModeEnum.None, TooltipText = "Vai até a ilha (ela decide se você pode entrar)", CustomMinimumSize = new Vector2(0, 34), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; UiStyle.StyleButton(go, UiStyle.ButtonKind.Tool, 12, 4, 8);
                go.Pressed += () => VisitIsland?.Invoke(island); row.AddChild(go);
            }
            _favoriteRows.AddChild(card);
        }
    }

    private void FillAchievements()
    {
        Clear(_achievementRows); var self = IsSelf;
        var list = _profile.ValueKind == JsonValueKind.Object && _profile.TryGetProperty("achievements", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToList() : [];
        if (list.Count == 0) { _achievementRows.AddChild(Muted(Flag(_profile, "privateProfile") ? "Este perfil é privado." : self ? "Nenhuma conquista ainda. Elas aparecem conforme você explora, constrói e participa." : "Nenhuma conquista à mostra.")); return; }
        foreach (var achievement in list)
        {
            var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.Orange, .55f), 10, 1, 10, 8));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
            row.AddChild(UiStyle.IconRect(UiIconData.Icons.ContainsKey(Str(achievement, "icon")) ? Str(achievement, "icon") : "star", UiStyle.Orange, 30));
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 0); row.AddChild(text);
            text.AddChild(Text(Str(achievement, "title"), 16, true)); text.AddChild(Text(Str(achievement, "description"), 13, false, UiStyle.TextMuted));
            if (DateTimeOffset.TryParse(Str(achievement, "unlockedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)) { var date = Text(when.ToString("dd/MM/yyyy", Pt), 13, false, UiStyle.Orange); date.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; date.AutowrapMode = TextServer.AutowrapMode.Off; row.AddChild(date); }
            _achievementRows.AddChild(card);
        }
    }

    private void FillStats()
    {
        Clear(_statGrid);
        if (_profile.ValueKind != JsonValueKind.Object || !_profile.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object)
        { _statGrid.AddChild(Muted(Flag(_profile, "privateProfile") ? "Este perfil é privado." : "As estatísticas deste residente estão ocultas.")); _statGrid.Columns = 1; return; }
        int? Count(string key) => stats.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        var days = Count("daysMember") ?? 0;
        var time = days >= 365 ? $"{days / 365} ano(s) e {days % 365 / 30} mês(es)" : days >= 30 ? $"{days / 30} mês(es)" : $"{days} dia(s)";
        StatTile("Tempo de participação", time, "calendar"); StatTile("Grupos", Count("groups")?.ToString() ?? "Oculto", "people"); StatTile("Terrenos", Count("parcels")?.ToString() ?? "Oculto", "home");
        StatTile("Criações à venda", (Count("creations") ?? 0).ToString(), "hammer"); StatTile("Eventos confirmados", (Count("events") ?? 0).ToString(), "bell"); StatTile("Imagens na galeria", (Count("pictures") ?? 0).ToString(), "camera");
    }

    private void StatTile(string caption, string value, string icon)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); row.AddChild(UiStyle.IconRect(icon, UiStyle.AccentText, 26));
        var text = new VBoxContainer(); text.AddThemeConstantOverride("separation", 0); row.AddChild(text);
        var cap = Text(caption, 12, false, UiStyle.TextMuted); cap.AutowrapMode = TextServer.AutowrapMode.Off; cap.ClipText = true; cap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; text.AddChild(cap); text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var v = Text(value, 20, true); v.AutowrapMode = TextServer.AutowrapMode.Off; v.ClipText = true; text.AddChild(v);
        var card = Card(row, 10); card.CustomMinimumSize = new Vector2(120, 0); _statGrid.AddChild(card);
    }
}
