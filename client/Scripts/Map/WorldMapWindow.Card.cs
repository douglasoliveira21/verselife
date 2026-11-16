using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>
/// The card of the chosen island over the map (picture, name, public or private, who is there, category and rating, how it is now, a short text, "Teletransportar" and "Info"), the Info window, the right-click menu,
/// and the actions: teleport (checked again with the server just before going), favorite, set a destination, copy the location. Whether an island can be entered is decided by the server.
/// </summary>
public partial class WorldMapWindow
{
    private PanelContainer _card = null!;
    private TextureRect _cardPicture = null!;
    private Label _cardName = null!, _cardLine = null!, _cardCategory = null!, _cardState = null!, _cardText = null!;
    private Button _teleport = null!, _info = null!, _favorite = null!;
    private AtlasIsland? _selectedIsland;
    private PopupMenu? _menu;
    private readonly Dictionary<int, Action> _menuActions = [];
    private FloatingWindow? _infoWindow;

    private void BuildCard()
    {
        _card = new PanelContainer { Visible = false, Name = "IslandCard", CustomMinimumSize = new Vector2(330, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        _card.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.PanelFillSolid, .96f), new Color(UiStyle.Accent, .55f), 14, 1, 12, 12, 8)); _stack.AddChild(_card);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); _card.AddChild(column);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 12); column.AddChild(top);
        _cardPicture = new TextureRect { CustomMinimumSize = new Vector2(96, 96), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin }; top.AddChild(_cardPicture);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 2); top.AddChild(text);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 4); text.AddChild(head);
        _cardName = Lbl("", 18, true); _cardName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; head.AddChild(_cardName);
        _favorite = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 30), ExpandIcon = false, TooltipText = "Favoritar" }; _favorite.Pressed += () => { if (_selectedIsland is { } i) ToggleFavorite(i); }; head.AddChild(_favorite);
        var close = new Button { Icon = UiStyle.Icon("close", UiStyle.TextMuted, 16), Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 30), ExpandIcon = false, TooltipText = "Fechar o cartão (Esc)" }; close.Pressed += () => { _canvas.Select(Guid.Empty, announce: false); HideCard(); }; head.AddChild(close);
        _cardLine = Lbl("", 14, false, UiStyle.TextMuted); text.AddChild(_cardLine); _cardCategory = Lbl("", 14, false, UiStyle.TextMuted); text.AddChild(_cardCategory); _cardState = Lbl("", 14, true, WorldMapCanvas.Online); text.AddChild(_cardState);
        _cardPoint = Lbl("", 14, false, UiStyle.AccentText, wrap: true); _cardPoint.Visible = false; column.AddChild(_cardPoint);
        _cardText = Lbl("", 14, false, UiStyle.Text, wrap: true); _cardText.CustomMinimumSize = new Vector2(0, 0); _cardText.MaxLinesVisible = 3; column.AddChild(_cardText);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8); column.AddChild(buttons);
        _teleport = Btn("Teletransportar", UiStyle.ButtonKind.Primary, OnTeleportPressed, "Entra nesta ilha (o servidor confere o acesso)", 42); _teleport.Icon = UiStyle.Icon("pin", UiStyle.Text, 20); _teleport.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; buttons.AddChild(_teleport);
        _info = Btn("Info", UiStyle.ButtonKind.Secondary, () => { if (_selectedIsland is { } i) ShowInfo(i); }, "Detalhes da ilha", 42); _info.Icon = UiStyle.Icon("info", UiStyle.Text, 20); _info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; buttons.AddChild(_info);
    }

    // ---- the card -----------------------------------------------------------------------------------------------------------------------------

    private void OnSelected(Guid id)
    {
        _selectedIsland = id == Guid.Empty ? null : _items.FirstOrDefault(i => i.Id == id);
        if (_selectedIsland is { } island) ShowCard(island); else HideCard();
    }

    private static (string Text, Color Color) StateOf(AtlasIsland island) => island.Access switch
    {
        "open" => island.Online ? ("Online", WorldMapCanvas.Online) : ("Em repouso: liga ao entrar", UiStyle.TextMuted),
        "full" => ("Cheia no momento", UiStyle.Orange), "closed" => ("Fechada temporariamente", WorldMapCanvas.Error), "private" => ("Privada", WorldMapCanvas.Error), "banned" => ("Acesso negado", WorldMapCanvas.Error), _ => ("Indisponível", WorldMapCanvas.Error)
    };

    private void ShowCard(AtlasIsland island)
    {
        _selectedIsland = island; _cardName.Text = island.Name; var capacity = island.Visitors is { } v ? $" • {v}/{island.MaxPlayers}" : $" • até {island.MaxPlayers}";
        _cardLine.Text = (island.IsPublic ? "Pública" : "Privada") + capacity; _cardCategory.Text = string.Join(" / ", new[] { island.Category.Length > 0 ? char.ToUpper(island.Category[0]) + island.Category[1..] : string.Empty, RatingWords(island.Rating) }.Where(t => t.Length > 0));
        var (state, color) = StateOf(island); _cardState.Text = "● " + state; _cardState.AddThemeColorOverride("font_color", color);
        _cardText.Text = island.Description.Length > 0 ? island.Description : "Sem descrição."; _cardText.Visible = true;
        _teleport.Disabled = !island.CanEnter; _teleport.TooltipText = island.CanEnter ? "Entra nesta ilha (o servidor confere o acesso)" : state;
        _favorite.Icon = UiStyle.Icon("star", island.Favorite ? UiStyle.Orange : UiStyle.TextMuted, 22); _favorite.TooltipText = island.Favorite ? "Remover dos favoritos" : "Favoritar";
        _cardPicture.Texture = UiStyle.Icon(island.HasTile ? "map" : "grid", new Color(UiStyle.TextMuted, .7f), 48);
        if (island.HasTile) Hub.Tiles.Get(island.Id, island.TileVersion, texture => { if (texture is not null && _selectedIsland?.Id == island.Id && GodotObject.IsInstanceValid(_cardPicture)) _cardPicture.Texture = texture; });
        _card.Visible = true; UpdatePointLine();
    }

    private static string RatingWords(string rating) => rating switch { "livre" => "Livre", "moderado" => "Moderado", "adulto" => "Adulto 18+", _ => string.Empty };

    private void HideCard() { _card.Visible = false; _selectedIsland = null; if (_canvas.Selected != Guid.Empty) _canvas.Select(Guid.Empty, announce: false); }

    /// <summary>Puts the card beside the island it talks about, inside the map (it follows the island when the map moves).</summary>
    private void PlaceCard()
    {
        if (_selectedIsland is not { } island || !_card.Visible || _canvas is null) return;
        var rect = _canvas.RectOf(island); var size = _card.GetCombinedMinimumSize(); var area = _stack.Size; if (area.X < 100) return;
        var x = rect.End.X + 14; if (x + size.X > area.X - 64) x = rect.Position.X - size.X - 14; x = Mathf.Clamp(x, 8, Mathf.Max(8, area.X - size.X - 64));
        var y = Mathf.Clamp(rect.GetCenter().Y - size.Y / 2f, 8, Mathf.Max(8, area.Y - size.Y - 36)); _card.Position = new Vector2(x, y); _card.Size = size;
    }

    /// <summary>Chooses an island, centres the map on it and shows its card.</summary>
    private void FocusIsland(AtlasIsland island)
    {
        _canvas.GlideTo(island.X, island.Y, Math.Max(_canvas.Zoom, .85)); _canvas.Select(island.Id, announce: false); _selectedIsland = island; ShowCard(island);
    }

    // ---- going --------------------------------------------------------------------------------------------------------------------------------

    private static string RefusalWords(string access) => access switch
    {
        "full" => "A ilha está cheia no momento. Tente de novo daqui a pouco.", "closed" => "A ilha está temporariamente fechada.", "private" => "Esta ilha é privada: só entra quem foi convidado.", "banned" => "Você não pode entrar nesta ilha.", "gone" => "Esta ilha não está mais disponível.", _ => "Não foi possível entrar nesta ilha agora."
    };

    /// <summary>Asks the server whether this player can come in right now, then enters through the game's own teleport. The card of an island that cannot be entered says why instead of trying.</summary>
    public void Teleport(AtlasIsland island)
    {
        if (Hub.CurrentIsland?.Invoke() == island.Id) { Say("Você já está nesta ilha."); return; }
        _teleport.Disabled = true; Say($"Verificando o acesso a {island.Name}…");
        Hub.Api.Send(Godot.HttpClient.Method.Get, $"/api/v1/discovery/islands/{island.Id}/info", null, (status, body) =>
        {
            if (_selectedIsland?.Id == island.Id) _teleport.Disabled = !island.CanEnter;
            if (status == 404) { Say(RefusalWords("gone")); Reload(); return; }
            if (status is < 200 or >= 300) { Say(status == 0 ? "Sem conexão com o servidor." : $"Não foi possível conferir o acesso (HTTP {status})."); return; }
            var access = "open"; try { using var d = JsonDocument.Parse(body); access = d.RootElement.TryGetProperty("access", out var a) ? a.GetString() ?? "open" : "open"; } catch (JsonException) { }
            if (access != "open") { Say(RefusalWords(access)); Reload(); return; }
            if (Hub.JoinIsland is null) { Say("O teleporte não está disponível agora."); return; }
            Say($"Entrando em {island.Name}…"); HideCard(); Hub.JoinIsland(island.Id);
        });
    }

    // ---- favorites, destination, copying --------------------------------------------------------------------------------------------------------

    public void ToggleFavorite(AtlasIsland island)
    {
        var wanted = !island.Favorite;
        Hub.Api.Send(wanted ? Godot.HttpClient.Method.Post : Godot.HttpClient.Method.Delete, $"/api/v1/discovery/islands/{island.Id}/favorite", wanted ? "{}" : null, (status, _) =>
        {
            if (status is < 200 or >= 300) { Say(status == 404 ? "Esta ilha não está mais disponível." : status == 0 ? "Sem conexão com o servidor." : $"Não foi possível atualizar os favoritos (HTTP {status})."); return; }
            var updated = island with { Favorite = wanted, Favorites = Math.Max(0, island.Favorites + (wanted ? 1 : -1)) }; Hub.Atlas.Replace(updated);
            Say(wanted ? $"{island.Name} foi para os seus favoritos." : $"{island.Name} saiu dos seus favoritos.");
        });
    }

    private void OnUpdated()
    {
        _items = [.. Hub.Atlas.Page.Items]; Refresh(false); if (_selectedIsland is { } chosen && _items.FirstOrDefault(i => i.Id == chosen.Id) is { } fresh) ShowCard(fresh);
    }

    private void MarkDestination(AtlasIsland island, double gx, double gy)
    {
        var (lx, lz, inside) = MapCoordinates.ToLocal(island, gx, gy); var local = inside ? (lx, lz) : (island.SpawnX, island.SpawnZ);
        Hub.SetDestination(new MapDestination(island.Id, island.Name, island.X + local.Item1, island.Y + local.Item2, (float)local.Item1, (float)local.Item2)); Say($"Destino marcado: {MapCoordinates.Format(island.Name, local.Item1, local.Item2)}.");
    }

    private void CopyLocation(AtlasIsland island, double gx, double gy)
    {
        var (lx, lz, inside) = MapCoordinates.ToLocal(island, gx, gy); var x = inside ? (float)lx : island.SpawnX; var z = inside ? (float)lz : island.SpawnZ;
        DisplayServer.ClipboardSet(LifeVerse.Client.Scripts.SearchLinks.For("island", island.Id, x, z)); Say($"Link copiado: {MapCoordinates.Format(island.Name, x, z)}.");
    }

    // ---- the menu ----------------------------------------------------------------------------------------------------------------------------------

    private void ShowMenu(Guid islandId, Vector2 screen, double gx, double gy)
    {
        _menu?.QueueFree(); _menu = new PopupMenu(); _menuActions.Clear(); var next = 0; var island = islandId == Guid.Empty ? null : _items.FirstOrDefault(i => i.Id == islandId);
        void Add(string text, Action action, bool disabled = false) { _menu.AddItem(text, next); _menu.SetItemDisabled(_menu.GetItemIndex(next), disabled); _menuActions[next++] = action; }
        if (island is not null)
        {
            _canvas.Select(island.Id, announce: false); _selectedIsland = island; ShowCard(island);
            Add("Informações", () => ShowInfo(island)); Add("Teletransportar", () => Teleport(island), !island.CanEnter); Add("Teletransportar aqui", () => TeleportHere(island, gx, gy), !island.CanEnter); Add("Marcar destino", () => MarkDestination(island, gx, gy));
            Add("Adicionar marco aqui", () => { var (lx, lz, inside) = MapCoordinates.ToLocal(island, gx, gy); _canvas.PickPoint(island, inside ? (float)lx : island.SpawnX, inside ? (float)lz : island.SpawnZ); AddMark(); });
            Add(island.Favorite ? "Remover dos favoritos" : "Favoritar", () => ToggleFavorite(island)); Add("Copiar localização", () => CopyLocation(island, gx, gy)); Add("Centralizar aqui", () => _canvas.GlideTo(gx, gy, _canvas.Zoom));
        }
        else
        {
            Add("Centralizar aqui", () => _canvas.GlideTo(gx, gy, _canvas.Zoom)); Add("Copiar coordenadas", () => { DisplayServer.ClipboardSet(string.Create(CultureInfo.InvariantCulture, $"{gx:0}, {gy:0}")); Say($"Coordenadas copiadas: {gx:0}, {gy:0}."); });
            Add("Centralizar em mim", () => { if (!_canvas.CenterOnPlayer()) Say("Você não está em uma ilha do mapa agora."); }); Add("Ajustar à região", () => _canvas.FitAll(glide: true));
        }
        if (Hub.Destination is not null) Add("Cancelar destino", () => { Hub.ClearDestination(); Say("Destino cancelado."); });
        Add(Hub.Settings.ShowNames ? "Esconder nomes" : "Mostrar nomes", () => { Hub.Settings.ShowNames = !Hub.Settings.ShowNames; Hub.Settings.Save(); Hub.Changed("world.map_names", Hub.Settings.ShowNames); _canvas.QueueRedraw(); });
        _menu.IdPressed += id => { if (_menuActions.TryGetValue((int)id, out var action)) action(); }; AddChild(_menu); _menu.Position = (Vector2I)screen; _menu.Popup();
    }

    // ---- info ------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The details of an island: owner (when public), rules, capacity, access and the places on the atlas and inside it, read from the server for this player.</summary>
    public void ShowInfo(AtlasIsland island)
    {
        if (_infoWindow is not null && GodotObject.IsInstanceValid(_infoWindow)) _infoWindow.QueueFree();
        _infoWindow = new FloatingWindow(island.Name, icon: "info") { Name = "WorldMapInfo" }; _infoWindow.Place(.5f, .5f, -250, -240, 250, 240); var window = _infoWindow;
        var body = new Label { Text = "Carregando…", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; body.AddThemeFontSizeOverride("font_size", 15); window.Content.AddChild(body);
        var buttons = new HFlowContainer(); buttons.AddThemeConstantOverride("h_separation", 8); window.Content.AddChild(buttons);
        buttons.AddChild(Btn("Copiar localização", UiStyle.ButtonKind.Secondary, () => CopyLocation(island, island.X + island.SpawnX, island.Y + island.SpawnZ), null, 36));
        buttons.AddChild(Btn("Ver na Busca", UiStyle.ButtonKind.Secondary, () => OpenLink?.Invoke(LifeVerse.Client.Scripts.SearchLinks.For("island", island.Id, island.SpawnX, island.SpawnZ)), "Abre a janela de busca nesta ilha", 36));
        buttons.AddChild(Btn("Fechar", UiStyle.ButtonKind.Secondary, () => window.QueueFree(), null, 36)); AddChild(window); window.MoveToFront();
        Hub.Api.Send(Godot.HttpClient.Method.Get, $"/api/v1/discovery/islands/{island.Id}/info", null, (status, text) =>
        {
            if (!GodotObject.IsInstanceValid(body)) return;
            if (status is < 200 or >= 300) { body.Text = status == 404 ? "Esta ilha não está mais disponível." : "Não foi possível carregar as informações agora."; return; }
            try
            {
                using var d = JsonDocument.Parse(text); var p = d.RootElement; var lines = new List<string>();
                if (Str(p, "description") is { Length: > 0 } description) { lines.Add(description); lines.Add(string.Empty); }
                if (p.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.Object) lines.Add("Proprietário(a): " + Str(owner, "displayName"));
                lines.Add($"Categoria: {Str(p, "category")} · classificação {RatingWords(Str(p, "contentRating"))} · {Str(p, "language")}");
                lines.Add($"Acesso: {StateOf(island with { Access = Str(p, "access") }).Text}" + (Str(p, "visibility") is { Length: > 0 } visibility ? $" ({(visibility == "public" ? "pública" : visibility == "invite" ? "somente convidados" : "fechada")})" : string.Empty));
                lines.Add($"Capacidade: {(p.TryGetProperty("visitors", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() + " agora, " : string.Empty)}até {Int(p, "maxPlayers")} pessoas");
                lines.Add($"Tamanho: {Int(p, "size")} m · chegada em x {Num(p, "spawnX"):0}, z {Num(p, "spawnZ"):0}");
                lines.Add($"No mapa: {island.X:0}, {island.Y:0} (célula {island.CellX}, {island.CellY})");
                if (_eventIslands.Contains(island.Id)) lines.Add("Há eventos marcados nesta ilha. Veja na Busca > Eventos.");
                body.Text = string.Join("\n", lines);
            }
            catch (JsonException) { body.Text = "Resposta inesperada do servidor."; }
        });
    }

    private static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    private static int Int(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
    private static double Num(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) ? n : 0;

    // ---- tests ----------------------------------------------------------------------------------------------------------------------------------------

    public bool CardShownForTesting => _card.Visible;
    public string CardNameForTesting => _cardName.Text;
    public string CardLineForTesting => _cardLine.Text;
    public string CardCategoryForTesting => _cardCategory.Text;
    public string CardStateForTesting => _cardState.Text;
    public string CardTextForTesting => _cardText.Text;
    public bool TeleportEnabledForTesting => !_teleport.Disabled;
    public string FavoriteTooltipForTesting => _favorite.TooltipText;
    public void PressTeleportForTesting() => _teleport.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressInfoForTesting() => _info.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressFavoriteForTesting() => _favorite.EmitSignal(BaseButton.SignalName.Pressed);
    public Rect2 CardRectForTesting => new(_card.Position, _card.Size);
    public bool InfoOpenForTesting => _infoWindow is not null && GodotObject.IsInstanceValid(_infoWindow) && !_infoWindow.IsQueuedForDeletion();
    public string InfoTextForTesting => string.Join((char)10, _infoWindow?.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text) ?? []);
    public IReadOnlyList<string> MenuForTesting(Guid island, double gx, double gy) { ShowMenu(island, new Vector2(100, 100), gx, gy); return [.. Enumerable.Range(0, _menu!.ItemCount).Select(_menu.GetItemText)]; }
    public void ChooseMenuForTesting(string text) { for (var i = 0; i < _menu!.ItemCount; i++) if (_menu.GetItemText(i) == text) { _menuActions[_menu.GetItemId(i)](); return; } throw new InvalidOperationException("no menu item " + text); }
    public bool MenuEnabledForTesting(string text) { for (var i = 0; i < _menu!.ItemCount; i++) if (_menu.GetItemText(i) == text) return !_menu.IsItemDisabled(i); return false; }
}
