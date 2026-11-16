using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Map;

namespace LifeVerse.Client.Tests;

/// <summary>The world map window against a pretend server: loading, the map (view, picking, markers, clusters), the card, teleport checks, favorites, the menu, categories, search, the regions tab, errors and the narrow layout.</summary>
public partial class WorldMapSmoke : Node
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), E = Guid.NewGuid(), F = Guid.NewGuid(), Luna = Guid.NewGuid();
    private readonly List<string> _calls = [];
    private readonly Dictionary<Guid, string> _access = new() { [A] = "open", [B] = "open", [C] = "full", [D] = "private", [E] = "open", [F] = "open" };
    private readonly List<(Guid Id, string Name, int X, int Y, string Category, string Rating, int? Visitors, int Favorites, bool Welcome, int DaysOld, bool Fav)> _islands =
    [
        (A, "Centro", 0, 0, "social", "livre", 28, 5, true, 400, false), (B, "Praia Azul", 1, 0, "residencial", "livre", 3, 0, false, 200, false), (C, "Loja Central", 0, 1, "comercial", "livre", 40, 2, false, 100, false),
        (D, "Reservada", -1, 0, "social", "livre", null, 0, false, 300, false), (E, "Bar Adulto", 1, 1, "social", "adulto", 2, 0, false, 90, false), (F, "Nova Ilha", -1, -1, "social", "moderado", 1, 0, false, 2, false),
    ];
    private readonly List<(Guid Id, string Name, int X, int Y)> _fillers = [];
    private bool _eventsOn = true, _down; private int _downStatus = 500; private Guid _joined; private readonly List<string> _said = [];

    private static byte[] Png(Color color) { var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgb8); image.Fill(color); return image.SavePngToBuffer(); }

    private string Atlas(string path)
    {
        var adultOnly = path.Contains("ratings=adulto"); var hideAdult = path.Contains("ratings=livre"); var category = path.Contains("category=residencial") ? "residencial" : path.Contains("category=comercial") ? "comercial" : null;
        var items = _islands.Where(i => (!adultOnly || i.Rating == "adulto") && (!hideAdult || i.Rating != "adulto") && (category is null || i.Category == category)).Select(i => (object)new
        {
            id = i.Id, name = i.Name, description = "Descrição de " + i.Name, category = i.Category, contentRating = i.Rating, language = "pt-BR", isPublic = i.Id != D, isWelcome = i.Welcome, cellX = i.X, cellY = i.Y, x = i.X * 768.0, y = i.Y * 768.0, size = 256, spawnX = 0f, spawnZ = 0f, maxPlayers = 60,
            online = i.Visitors is not null, visitors = i.Visitors, access = _access[i.Id], coverImageUrl = (string?)null, hasTile = true, tileVersion = "v1", favorite = i.Fav, favorites = i.Favorites, createdAt = DateTimeOffset.UtcNow.AddDays(-i.DaysOld), isOwner = false,
            owner = i.Id == D ? null : new { id = Luna, displayName = "Luna" }
        }).Concat(_fillers.Where(_ => category is null && !adultOnly).Select(f => (object)new
        {
            id = f.Id, name = f.Name, description = "Preenchimento", category = "social", contentRating = "livre", language = "pt-BR", isPublic = true, isWelcome = false, cellX = f.X, cellY = f.Y, x = f.X * 768.0, y = f.Y * 768.0, size = 256, spawnX = 0f, spawnZ = 0f, maxPlayers = 60,
            online = false, visitors = (int?)null, access = "open", coverImageUrl = (string?)null, hasTile = true, tileVersion = "v1", favorite = false, favorites = 0, createdAt = DateTimeOffset.UtcNow.AddDays(-50), isOwner = false, owner = new { id = Luna, displayName = "Luna" }
        })).ToList();
        return JsonSerializer.Serialize(new { items, total = items.Count, cellMeters = 768, tileSize = 256, truncated = false });
    }

    private (int, string) Server(string method, string path, string? body)
    {
        _calls.Add(method + " " + path);
        if (_down) return (_downStatus, "{}");
        if (method == "GET" && path.StartsWith("/api/v1/map/atlas")) return (200, Atlas(path));
        if (method == "GET" && path == "/api/v1/discovery/events") return (200, _eventsOn ? $"[{{\"id\":\"{Guid.NewGuid()}\",\"islandId\":\"{B}\"}}]" : "[]");
        if (method == "GET" && path.StartsWith("/api/v1/discovery/islands/") && path.EndsWith("/info"))
        {
            var id = Guid.Parse(path.Split('/')[^2]); if (!_access.TryGetValue(id, out var access)) return (404, "{}");
            return (200, JsonSerializer.Serialize(new { id, name = "Ilha", description = "Descrição longa da ilha.", category = "social", contentRating = "livre", language = "pt-BR", owner = new { id = Luna, username = "luna", displayName = "Luna" }, online = true, visitors = 3, maxPlayers = 60, size = 256, spawnX = 0, spawnZ = 0, access, canEnter = access == "open", visibility = "public" }));
        }
        if (path.StartsWith("/api/v1/discovery/islands/") && path.EndsWith("/favorite")) return (204, string.Empty);
        return (404, "{}");
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Settle() => await ToSignal(GetTree().CreateTimer(1.4), Godot.Timer.SignalName.Timeout);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private Vector2 Center(WorldMapCanvas canvas, Guid island) { var i = canvas.Islands.First(x => x.Id == island); return canvas.ToScreen(i.X, i.Y); }

    public override async void _Ready()
    {
        try
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://test_map.cfg")); DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://map_search_history.json"));
            var hub = new MapHub { Name = "Hub", JoinIsland = id => _joined = id, CurrentIsland = () => A, PlayerLocal = () => (10f, 20f, 0f), Say = text => _said.Add(text) }; hub.Settings.Path = "user://test_map.cfg"; AddChild(hub);
            hub.Api.Transport = Server; hub.Api.BytesTransport = path => (200, Png(path.Contains(A.ToString()) ? Colors.Green : Colors.Blue), "v1"); hub.Tiles.DiskFolder = string.Empty;
            var window = new WorldMapWindow { Name = "Map", Hub = hub }; AddChild(window); await Frames(2); window.WindowForTesting.Size = new Vector2(1100, 700); SearchWindow.KeepHistory = true;
            var canvas = window.Canvas;

            // opening: the map is read, adult islands stay out, and the view starts on the player
            window.Open(); await Frames(4);
            Require(canvas.Size.X > 400, "the map has a size: " + canvas.Size);
            Require(_calls.Any(c => c.StartsWith("GET /api/v1/map/atlas") && c.Contains("ratings=livre%2Cmoderado") && c.Contains("limit=500")) && _calls.Contains("GET /api/v1/discovery/events"), "the atlas and the events are asked for: " + string.Join("|", _calls));
            Require(window.Items.Count == 5 && window.Shown.Count == 5 && canvas.Islands.Count == 5 && !window.LoadingForTesting && window.StateTextForTesting == "", $"five islands on the map, the adult one not listed: {window.Items.Count}/{window.Shown.Count}/{canvas.Islands.Count} loading={window.LoadingForTesting} state={window.StateTextForTesting}");
            var you = hub.PlayerGlobal()!.Value; Require(you.X == 10 && you.Y == 20 && you.Island.Id == A, "the player is on the atlas: " + you);
            var shown = canvas.ToScreen(you.X, you.Y); Require(Mathf.Abs(shown.X - canvas.Size.X / 2) < 3 && Mathf.Abs(shown.Y - canvas.Size.Y / 2) < 3, "the view starts centred on the player: " + shown);
            Require(canvas.EventIslands.Contains(B), "the island with an event is marked");

            // the pictures of the islands come from the cache
            canvas.SetView(0, 0, .5); canvas.Layout(); await Frames(2); Require(canvas.HasPictureForTesting(A) && canvas.HasPictureForTesting(B), "the pictures of the islands on screen were asked for");

            // picking and the card
            var centreA = Center(canvas, A); canvas.ClickForTesting(centreA); await Frames(1);
            Require(window.CardShownForTesting && window.CardNameForTesting == "Centro" && window.CardLineForTesting == "Pública • 28/60" && window.CardCategoryForTesting == "Social / Livre" && window.CardStateForTesting == "● Online" && window.CardTextForTesting == "Descrição de Centro", $"the card of an island: {window.CardNameForTesting} | {window.CardLineForTesting} | {window.CardCategoryForTesting} | {window.CardStateForTesting}");
            var card = window.CardRectForTesting; Require(card.Position.X >= 0 && card.Position.Y >= 0 && card.End.X <= canvas.Size.X + 1 && card.End.Y <= canvas.Size.Y + 1, "the card stays inside the map: " + card);
            window.PressTeleportForTesting(); Require(_joined == Guid.Empty && _said.Last().Contains("já está nesta ilha"), "teleporting to the island you are on only says so");
            canvas.ClickForTesting(Center(canvas, B)); Require(window.CardNameForTesting == "Praia Azul" && window.TeleportEnabledForTesting, "another island, one that can be entered");
            var before = _calls.Count; window.PressTeleportForTesting(); Require(_calls.Skip(before).Any(c => c == $"GET /api/v1/discovery/islands/{B}/info") && _joined == B, "the access is checked with the server and then the game enters");
            _joined = Guid.Empty; _access[B] = "full"; before = _calls.Count; canvas.ClickForTesting(Center(canvas, B)); window.PressTeleportForTesting(); await Frames(1);
            Require(_joined == Guid.Empty && _said.Last().Contains("cheia") && _calls.Skip(before).Any(c => c.StartsWith("GET /api/v1/map/atlas")), "an island that filled up meanwhile is refused with the reason, and the map is read again"); _access[B] = "open"; window.Reload(); await Frames(1);
            canvas.ClickForTesting(Center(canvas, D)); Require(window.CardNameForTesting == "Reservada" && !window.TeleportEnabledForTesting && window.CardStateForTesting == "● Privada" && window.CardLineForTesting.StartsWith("Privada"), "a private island cannot be entered and says so");
            canvas.ClickForTesting(Center(canvas, C)); Require(!window.TeleportEnabledForTesting && window.CardStateForTesting.Contains("Cheia"), "a full island says so");
            canvas.ClickForTesting(new Vector2(canvas.Size.X - 30, 10)); Require(!window.CardShownForTesting && canvas.Selected == Guid.Empty, "a click on the sea closes the card");

            // info
            canvas.ClickForTesting(Center(canvas, B), doubleClick: true); await Frames(1);
            Require(window.InfoOpenForTesting && window.InfoTextForTesting.Contains("Proprietário(a): Luna") && window.InfoTextForTesting.Contains("Acesso: Online") && window.InfoTextForTesting.Contains("até 60 pessoas") && window.InfoTextForTesting.Contains("No mapa: 768, 0") && window.InfoTextForTesting.Contains("eventos marcados"), "a double click opens the info: " + window.InfoTextForTesting);

            // favorites
            canvas.ClickForTesting(Center(canvas, B)); Require(window.FavoriteTooltipForTesting == "Favoritar", "not a favorite yet");
            window.PressFavoriteForTesting(); Require(_calls.Contains($"POST /api/v1/discovery/islands/{B}/favorite") && window.FavoriteTooltipForTesting == "Remover dos favoritos" && hub.Atlas.Find(B)!.Favorite && hub.Atlas.Find(B)!.Favorites == 1, "favoriting is saved and shown everywhere");
            window.PressFavoriteForTesting(); Require(_calls.Contains($"DELETE /api/v1/discovery/islands/{B}/favorite") && window.FavoriteTooltipForTesting == "Favoritar" && !hub.Atlas.Find(B)!.Favorite, "and undone");

            // the menu
            var menu = window.MenuForTesting(C, C == Guid.Empty ? 0 : 0, 768);
            Require(menu.SequenceEqual(["Informações", "Teletransportar", "Teletransportar aqui", "Marcar destino", "Adicionar marco aqui", "Favoritar", "Copiar localização", "Centralizar aqui", "Esconder nomes"]) && !window.MenuEnabledForTesting("Teletransportar"), "the menu of an island (teleport off for a full one): " + string.Join("|", menu));
            window.ChooseMenuForTesting("Marcar destino"); Require(hub.Destination is { } d && d.Island == C && d.Name == "Loja Central" && window.MenuForTesting(C, 0, 768).Contains("Cancelar destino"), "a destination is set and can be cancelled");
            window.ChooseMenuForTesting("Cancelar destino"); Require(hub.Destination is null, "cancelled");
            var empty = window.MenuForTesting(Guid.Empty, 5000, 5000); Require(empty.SequenceEqual(["Centralizar aqui", "Copiar coordenadas", "Centralizar em mim", "Ajustar à região", "Esconder nomes"]), "the menu of the open sea: " + string.Join("|", empty));
            window.ChooseMenuForTesting("Esconder nomes"); Require(!hub.Settings.ShowNames, "names can be hidden"); window.MenuForTesting(Guid.Empty, 5000, 5000); window.ChooseMenuForTesting("Mostrar nomes"); Require(hub.Settings.ShowNames, "and shown");

            // pan, zoom, keys, fit
            canvas.SetView(0, 0, .5); var c0 = (canvas.CenterX, canvas.CenterY); canvas.DragForTesting(new Vector2(300, 300), new Vector2(200, 250)); Require(Mathf.IsEqualApprox((float)canvas.CenterX, (float)(c0.CenterX + 200)) && Mathf.IsEqualApprox((float)canvas.CenterY, (float)(c0.CenterY + 100)), "dragging moves the map");
            var cursor = new Vector2(400, 200); var under = canvas.ToGlobal(cursor); canvas.WheelForTesting(cursor, true); var after = canvas.ToGlobal(cursor); Require(canvas.Zoom > .5 && Math.Abs(after.X - under.X) < .01 && Math.Abs(after.Y - under.Y) < .01, "the wheel zooms around the cursor");
            for (var i = 0; i < 60; i++) canvas.WheelForTesting(cursor, false); Require(canvas.Zoom == WorldMapCanvas.MinZoom, "zoom stops at the far limit"); for (var i = 0; i < 80; i++) canvas.WheelForTesting(cursor, true); Require(canvas.Zoom == WorldMapCanvas.MaxZoom, "and at the near limit");
            canvas.SetView(0, 0, .5); canvas.KeyForTesting(Key.Equal); Require(Mathf.IsEqualApprox((float)canvas.Zoom, .625f), "+ zooms in"); canvas.KeyForTesting(Key.Minus); Require(Mathf.IsEqualApprox((float)canvas.Zoom, .5f), "− zooms out");
            canvas.KeyForTesting(Key.Left); Require(canvas.CenterX < 0, "the arrows move the map"); canvas.KeyForTesting(Key.Home); await Settle(); Require(Math.Abs(canvas.CenterX - 10) < 1 && Math.Abs(canvas.CenterY - 20) < 1, $"Home goes to the player: {canvas.CenterX},{canvas.CenterY} zoom {canvas.Zoom}");
            canvas.FitAll(); foreach (var island in canvas.Islands) { var r = canvas.RectOf(island); Require(new Rect2(Vector2.Zero, canvas.Size).Encloses(r), "fit shows every island: " + island.Name); }

            // far away the islands become groups
            canvas.SetView(0, 0, WorldMapCanvas.MinZoom); Require(canvas.IsClusteredForTesting && canvas.Clusters.Count >= 1 && canvas.Clusters.Sum(c => c.Members.Count) == 5, "far away they are grouped: " + canvas.Clusters.Count);
            var group = canvas.Clusters.OrderByDescending(c => c.Members.Count).First(); var zoomBefore = canvas.Zoom; canvas.ClickForTesting(group.At); await Settle(); Require(canvas.Zoom > zoomBefore * 1.5, "a click on a group zooms in on it");

            // categories are real filters
            canvas.FitAll(); before = _calls.Count;
            Require(window.CategoryLabelsForTesting.SequenceEqual(["Destaques", "Populares", "Novas Ilhas", "Residenciais", "Comerciais", "Eventos", "Adulto 18+"]), "the categories of the mockup");
            window.PressCategoryForTesting("residencial"); await Frames(1); Require(_calls.Skip(before).Any(c => c.Contains("category=residencial")) && window.Shown.Select(i => i.Name).SequenceEqual(["Praia Azul"]) && canvas.Islands.Count == 1, "Residenciais asks the server and shows one island");
            window.PressCategoryForTesting("comercial"); await Frames(1); Require(window.Shown.Select(i => i.Name).SequenceEqual(["Loja Central"]), "Comerciais");
            window.PressCategoryForTesting("comercial"); await Frames(1); Require(window.CategoryForTesting == "" && window.Shown.Count == 5, "the same category again shows everything");
            window.PressCategoryForTesting("eventos"); Require(window.Shown.Select(i => i.Name).SequenceEqual(["Praia Azul"]), "Eventos keeps the islands with an event");
            window.PressCategoryForTesting("destaques"); Require(window.Shown.Select(i => i.Name).First() == "Centro" && window.Shown.Count <= 8 && window.Shown.All(i => i.IsWelcome || (i.Visitors ?? 0) * 2 + i.Favorites > 0), "Destaques: the welcome island and those with people or likes");
            window.PressCategoryForTesting("populares"); Require(window.Shown.Select(i => i.Name).First() == "Loja Central" && window.Shown.Select(i => i.Name).SequenceEqual(window.Shown.OrderByDescending(i => (i.Visitors ?? 0) * 2 + i.Favorites).Select(i => i.Name)), "Populares: by people and likes");
            window.PressCategoryForTesting("novas"); Require(window.Shown.First().Name == "Nova Ilha" && window.Shown.Count == 5, "Novas Ilhas: the newest first");
            _eventsOn = false; window.Reload(); window.PressCategoryForTesting("eventos"); await Frames(1);
            Require(window.Shown.Count == 0 && window.StateTextForTesting == "Nenhuma ilha encontrada com este filtro.", "an empty result says so: " + window.StateTextForTesting); window.PressStateActionForTesting(); Require(window.CategoryForTesting == "" && window.Shown.Count == 5 && window.StateTextForTesting == "", "and offers to clear the filter"); _eventsOn = true; window.Reload();
            window.PressCategoryForTesting("adulto"); Require(window.AdultDialogOpenForTesting && window.CategoryForTesting == "" && window.Shown.Count == 5, "adult islands ask first"); window.ConfirmAdultForTesting(false); Require(window.CategoryForTesting == "" && !hub.Settings.ShowAdult, "cancelling changes nothing");
            window.PressCategoryForTesting("adulto"); before = _calls.Count; window.ConfirmAdultForTesting(true); await Frames(1);
            Require(hub.Settings.ShowAdult && window.CategoryForTesting == "adulto" && _calls.Skip(before).Any(c => c.Contains("ratings=adulto")) && window.Shown.Select(i => i.Name).SequenceEqual(["Bar Adulto"]), "after saying they are 18, adult islands are listed");
            window.PressCategoryForTesting("adulto"); await Frames(1); Require(window.Items.Count == 6 && _calls.Last(c => c.StartsWith("GET /api/v1/map/atlas")).Contains("limit=500") && !_calls.Last(c => c.StartsWith("GET /api/v1/map/atlas")).Contains("ratings="), "with it confirmed, everything is listed, adult included");
            hub.Settings.ShowAdult = false; window.Reload(); await Frames(1);

            // search
            window.SetQueryForTesting("praia"); Require(window.ResultTitlesForTesting.SequenceEqual(["Praia Azul"]), "search by name"); window.SetQueryForTesting("PRAIA"); Require(window.ResultCountForTesting == 1, "not case sensitive"); window.SetQueryForTesting("Pràia"); Require(window.ResultCountForTesting == 1, "nor accent sensitive");
            window.SetQueryForTesting("luna"); Require(window.ResultCountForTesting == 4 && window.ResultTitlesForTesting.All(t => t != "Reservada"), "search by public owner: " + string.Join(",", window.ResultTitlesForTesting)); window.SetQueryForTesting("comercial"); Require(window.ResultTitlesForTesting.SequenceEqual(["Loja Central"]), "by category");
            window.SetQueryForTesting("768, 0"); Require(window.ResultTitlesForTesting[0] == "Ir para 768, 0", "coordinates"); window.SubmitQueryForTesting(); await Settle(); Require(Math.Abs(canvas.CenterX - 768) < 2 && canvas.Selected == B && window.CardShownForTesting, "going to coordinates centres the map and chooses the island under them");
            window.SetQueryForTesting("Praia Azul/10/20"); Require(window.ResultTitlesForTesting[0] == "Praia Azul (10, 20)", "a place written Name/x/z"); window.SetQueryForTesting("naoexiste"); Require(window.ResultCountForTesting == 0 && window.ResultsShownForTesting.Count == 0, "nothing found");
            window.SetQueryForTesting("praia"); window.SubmitQueryForTesting(); await Settle(); Require(canvas.Selected == B && Math.Abs(canvas.CenterX - 768) < 2, "Enter takes the map to the first result"); window.SetQueryForTesting(""); Require(window.RecentForTesting.Contains("↺ praia"), "the searches made are remembered: " + string.Join("|", window.RecentForTesting));
            SearchWindow.KeepHistory = false; window.SetQueryForTesting("x"); window.SetQueryForTesting(""); Require(window.RecentForTesting.Count == 0, "or not, when the history is off"); SearchWindow.KeepHistory = true;
            for (var i = 0; i < 11; i++) _fillers.Add((Guid.NewGuid(), $"Ilha {i + 1:00}", 2 + i % 4, -3 - i / 4)); window.Reload(); await Frames(1);
            window.SetQueryForTesting("ilha"); Require(window.ResultCountForTesting >= 11 && window.PagerShownForTesting && window.ResultsShownForTesting.Count == 8, "many results come in pages of eight"); window.NextResultsForTesting(); Require(window.ResultsShownForTesting.Count == window.ResultCountForTesting - 8 || window.ResultsShownForTesting.Count == 8, "the next page");
            window.PressResultForTesting(0); Require(canvas.Selected != Guid.Empty, "choosing a result chooses the island"); _fillers.Clear(); window.SetQueryForTesting(""); window.Reload(); await Frames(1);

            // the regions tab
            window.SelectTabForTesting(1); Require(!canvas.Visible && window.RegionRowsForTesting == 5 && window.RegionNamesForTesting.SequenceEqual(["Centro", "Loja Central", "Nova Ilha", "Praia Azul", "Reservada"]), "the list of regions: " + string.Join(",", window.RegionNamesForTesting));
            window.SetRegionSortForTesting(1); Require(window.RegionNamesForTesting[0] == "Loja Central", "by visitors"); window.SetRegionSortForTesting(2); Require(window.RegionNamesForTesting.TakeLast(2).OrderBy(n => n).SequenceEqual(["Loja Central", "Reservada"]), "the ones that can be entered come first");
            window.SetRegionSortForTesting(0); Require(!window.RegionButtonEnabledForTesting(4, "Teletransportar") && window.RegionButtonEnabledForTesting(3, "Teletransportar"), "a private island cannot be teleported to from the list");
            window.PressRegionForTesting(3, "Teletransportar"); Require(_joined == B, "the list teleports too"); window.SetQueryForTesting("loja"); Require(window.RegionRowsForTesting == 1, "the search box filters the list"); window.SetQueryForTesting("");
            window.PressRegionForTesting(3, "Ver no mapa"); await Settle(); Require(window.TabForTesting == 0 && canvas.Visible && canvas.Selected == B && window.CardShownForTesting, "Ver no mapa goes to the island");

            // errors
            _down = true; _downStatus = 0; window.Reload(); await Frames(1); Require(window.StateTextForTesting == "Sem conexão com o servidor." && !window.LoadingForTesting, "no connection is said plainly");
            _downStatus = 500; window.Reload(); Require(window.StateTextForTesting.Contains("HTTP 500"), "a server error is said"); _downStatus = 429; window.Reload(); Require(window.StateTextForTesting.Contains("Muitas tentativas"), "too many requests too");
            _down = false; window.PressStateActionForTesting(); await Frames(1); Require(window.StateTextForTesting == "" && window.Items.Count == 5, "Tentar novamente reads the map again");

            // narrow window
            window.WindowForTesting.Size = new Vector2(620, 600); await Frames(4); Require(window.NarrowForTesting && !window.FiltersShownForTesting && window.MapShownForTesting, "a narrow window shows the map first");
            window.ToggleFiltersForTesting(); Require(window.FiltersShownForTesting && !window.MapShownForTesting, "the filters on their own"); window.ToggleFiltersForTesting(); window.WindowForTesting.Size = new Vector2(1100, 700); await Frames(4); Require(!window.NarrowForTesting && window.FiltersShownForTesting && window.MapShownForTesting, "a wide window shows both");

            // help, closing and remembering the view
            window.OpenHelpForTesting(); Require(window.HelpOpenForTesting, "the help opens"); canvas.SetView(1234, -567, .8); window.Close(); Require(!window.Visible, "closing hides it");
            var saved = new MapSettings { Path = "user://test_map.cfg" }; saved.Load(); Require(saved.HasView && Math.Abs(saved.CenterX - 1234) < .01 && Math.Abs(saved.CenterY + 567) < .01 && Math.Abs(saved.Zoom - .8) < .001, "the view is remembered: " + saved.CenterX);
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://test_map.cfg")); DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://map_search_history.json"));
            GD.Print("event=world_map_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception)
        {
            var where = exception.StackTrace?.Split((char)10).Where(l => l.Contains("WorldMapSmoke")).Take(2).Aggregate("", (s, l) => s + l.Trim() + " ; ");
            GD.PushError($"event=world_map_smoke result=failed error={exception.Message} at {where}"); GetTree().Quit(1);
        }
    }
}
