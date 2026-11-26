using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>A landmark of the player: a named spot of an island, optionally in a folder and shared with everybody.</summary>
    public sealed class MapMark { public Guid Id, Island; public string Name = "", Description = "", IslandName = ""; public float X, Z; public Guid? Folder; public bool IsPublic; }
    public sealed class MapFolder { public Guid Id; public string Name = ""; }

    /// <summary>A pin on the map: a landmark of the player, drawn where its island stands plus its spot.</summary>
    public sealed class MapPin { public Guid Id, Island; public string Name = ""; public double X, Y; }

    /// <summary>
    /// The atlas drawn from above: every island a square at its place (colored by whether this player can enter), dragged with the mouse and zoomed with the wheel. Clicking an island selects it, and clicking inside it chooses a spot of
    /// its ground. The conversions between the screen and the atlas are plain functions so they can be tested.
    /// </summary>
    public sealed class MapCanvas : VisualElement
    {
        public readonly List<AtlasIsland> Islands = new List<AtlasIsland>(); public readonly List<MapPin> Pins = new List<MapPin>();
        public Vector2 Centre;                 // the point of the atlas in the middle of the view (meters)
        public float Scale = .05f;             // pixels per meter
        public Guid Selected, Current; public (double X, double Y)? Spot; public (double X, double Y)? You;
        public event Action<AtlasIsland, double, double> Clicked;
        /// <summary>The picture of an island seen from above, when there is one (asked for here; the window fetches it and calls <see cref="Refresh"/>).</summary>
        public Func<AtlasIsland, Texture2D> TileOf; public Action<AtlasIsland> TileWanted;
        /// <summary>The parcels of each island, once asked for, and whether they are drawn (only on islands big enough on screen).</summary>
        public readonly Dictionary<Guid, List<MapParcel>> Parcels = new Dictionary<Guid, List<MapParcel>>(); public bool ShowParcels; public Action<AtlasIsland> ParcelsWanted;
        public const float ParcelMinPixels = 120f;
        public MapParcel ParcelAt(Guid island, double localX, double localZ) => Parcels.TryGetValue(island, out var list) ? list.FirstOrDefault(p => p.Contains(localX, localZ)) : null;
        /// <summary>The number of parcels drawn in the last frame (for the tests).</summary>
        public int ParcelsDrawn { get; private set; }
        private readonly Dictionary<Guid, VisualElement> _tiles = new Dictionary<Guid, VisualElement>();
        public const float MinScale = .004f, MaxScale = 2f;
        private Vector2 _dragFrom, _dragCentre; private bool _dragging, _moved; private readonly Dictionary<Guid, Label> _labels = new Dictionary<Guid, Label>();

        public MapCanvas()
        {
            name = "map-canvas"; style.flexGrow = 1; style.minHeight = 260; style.backgroundColor = new Color(.04f, .12f, .18f); generateVisualContent += Draw; pickingMode = PickingMode.Position;
            RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; _dragging = true; _moved = false; _dragFrom = (Vector2)e.localPosition; _dragCentre = Centre; this.CapturePointer(e.pointerId); });
            RegisterCallback<PointerMoveEvent>(e => { if (!_dragging) return; var d = (Vector2)e.localPosition - _dragFrom; if (d.sqrMagnitude > 16f) _moved = true; if (_moved) { Centre = _dragCentre - new Vector2(d.x, d.y) / Scale; Refresh(); } });
            RegisterCallback<PointerUpEvent>(e => { if (!_dragging) return; _dragging = false; this.ReleasePointer(e.pointerId); if (!_moved) Click((Vector2)e.localPosition); });
            RegisterCallback<WheelEvent>(e => { ZoomAt((Vector2)e.localMousePosition, e.delta.y > 0 ? .85f : 1f / .85f); e.StopPropagation(); });
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
        }

        /// <summary>The place on the screen of a point of the atlas.</summary>
        public Vector2 ToScreen(double x, double y) => new Vector2(Half(contentRect.width) + (float)(x - Centre.x) * Scale, Half(contentRect.height) + (float)(y - Centre.y) * Scale);
        private static float Half(float size) => float.IsNaN(size) ? 0f : size / 2f;
        /// <summary>The point of the atlas under a place of the screen.</summary>
        public (double X, double Y) ToAtlas(Vector2 screen) => (Centre.x + (screen.x - Half(contentRect.width)) / Scale, Centre.y + (screen.y - Half(contentRect.height)) / Scale);

        public void ZoomAt(Vector2 screen, float factor)
        {
            var before = ToAtlas(screen); Scale = Mathf.Clamp(Scale * factor, MinScale, MaxScale); var after = ToAtlas(screen); Centre += new Vector2((float)(before.X - after.X), (float)(before.Y - after.Y)); Refresh();
        }

        /// <summary>Frames every island (or the one given) in the view.</summary>
        public void Fit(AtlasIsland only = null)
        {
            var list = only != null ? new List<AtlasIsland> { only } : Islands; if (list.Count == 0 || contentRect.width < 2f) return;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var i in list) { var r = MapCoordinates.Rect(i); minX = Math.Min(minX, r.MinX); minY = Math.Min(minY, r.MinY); maxX = Math.Max(maxX, r.MaxX); maxY = Math.Max(maxY, r.MaxY); }
            Centre = new Vector2((float)((minX + maxX) / 2), (float)((minY + maxY) / 2)); var w = Math.Max(1.0, maxX - minX); var h = Math.Max(1.0, maxY - minY);
            Scale = Mathf.Clamp((float)Math.Min(contentRect.width * .8 / w, contentRect.height * .8 / h), MinScale, MaxScale); Refresh();
        }

        private void Click(Vector2 screen)
        {
            var (x, y) = ToAtlas(screen); var island = MapCoordinates.IslandAt(Islands, x, y);
            if (island == null) { Selected = Guid.Empty; Spot = null; Refresh(); Clicked?.Invoke(null, 0, 0); return; }
            var (lx, lz, _) = MapCoordinates.ToLocal(island, x, y); Selected = island.Id; Spot = (x, y); Refresh(); Clicked?.Invoke(island, lx, lz);
        }

        public static Color ColorOf(AtlasIsland island) => island.Access switch { "open" => new Color(.2f, .62f, .35f), "full" => new Color(.85f, .55f, .15f), "banned" => new Color(.7f, .2f, .2f), _ => new Color(.38f, .42f, .46f) };

        public void Refresh() { MarkDirtyRepaint(); PlaceLabels(); }

        private void PlaceLabels()
        {
            var seen = new HashSet<Guid>();
            foreach (var island in Islands)
            {
                var size = island.Size * Scale; if (size < 46f) continue;   // too small to name
                var top = ToScreen(MapCoordinates.Rect(island).MinX, MapCoordinates.Rect(island).MinY); if (top.x > contentRect.width || top.y > contentRect.height || top.x + size < 0 || top.y + size < 0) continue;
                if (island.HasTile) { var tile = TileOf?.Invoke(island); if (tile == null) TileWanted?.Invoke(island); else { if (!_tiles.TryGetValue(island.Id, out var image)) { image = new VisualElement { pickingMode = PickingMode.Ignore }; image.style.position = Position.Absolute; image.style.opacity = .85f; _tiles[island.Id] = image; Insert(0, image); } image.style.backgroundImage = new StyleBackground(Background.FromTexture2D(tile)); image.style.left = top.x; image.style.top = top.y; image.style.width = size; image.style.height = size; image.style.display = DisplayStyle.Flex; _shownTiles.Add(island.Id); } }
                if (!_labels.TryGetValue(island.Id, out var label)) { label = UiKit.Text(island.Name, "lv-small"); label.pickingMode = PickingMode.Ignore; label.style.position = Position.Absolute; label.style.color = Color.white; _labels[island.Id] = label; Add(label); }
                label.text = island.Name; label.style.left = top.x + 4; label.style.top = top.y + 2; label.style.display = DisplayStyle.Flex; seen.Add(island.Id);
            }
            foreach (var pair in _labels) if (!seen.Contains(pair.Key)) pair.Value.style.display = DisplayStyle.None;
            foreach (var pair in _tiles) if (!_shownTiles.Contains(pair.Key)) pair.Value.style.display = DisplayStyle.None; _shownTiles.Clear();
        }

        private readonly HashSet<Guid> _shownTiles = new HashSet<Guid>();
        /// <summary>The number of island pictures on view (for the tests).</summary>
        public int TilesShown => _tiles.Values.Count(t => t.style.display == DisplayStyle.Flex);

        /// <summary>The number of names on view (for the tests).</summary>
        public int LabelsShown => _labels.Values.Count(l => l.style.display == DisplayStyle.Flex);

        private void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            foreach (var island in Islands)
            {
                var r = MapCoordinates.Rect(island); var a = ToScreen(r.MinX, r.MinY); var b = ToScreen(r.MaxX, r.MaxY);
                if (b.x < 0 || b.y < 0 || a.x > contentRect.width || a.y > contentRect.height) continue;
                var w = Mathf.Max(3f, b.x - a.x); var h = Mathf.Max(3f, b.y - a.y);
                p.fillColor = ColorOf(island); p.BeginPath(); Rect(p, a.x, a.y, w, h); p.Fill();
                p.lineWidth = island.Id == Selected ? 3f : island.Id == Current ? 2.5f : 1f; p.strokeColor = island.Id == Selected ? Color.white : island.Id == Current ? new Color(.4f, .9f, 1f) : new Color(0, 0, 0, .5f); p.BeginPath(); Rect(p, a.x, a.y, w, h); p.Stroke();
            }
            var drawn = 0;
            if (ShowParcels)
                foreach (var island in Islands)
                {
                    if (island.Size * Scale < ParcelMinPixels) continue; var r = MapCoordinates.Rect(island); var a = ToScreen(r.MinX, r.MinY); var b = ToScreen(r.MaxX, r.MaxY); if (b.x < 0 || b.y < 0 || a.x > contentRect.width || a.y > contentRect.height) continue;
                    if (!Parcels.TryGetValue(island.Id, out var list)) { ParcelsWanted?.Invoke(island); continue; }
                    foreach (var parcel in list)
                    {
                        var p0 = ToScreen(island.X + parcel.MinX, island.Y + parcel.MinZ); var p1 = ToScreen(island.X + parcel.MaxX, island.Y + parcel.MaxZ); var colour = MapApi.ParcelColor(parcel.State);
                        p.fillColor = new Color(colour.r, colour.g, colour.b, .32f); p.BeginPath(); Rect(p, p0.x, p0.y, p1.x - p0.x, p1.y - p0.y); p.Fill();
                        p.strokeColor = colour; p.lineWidth = 1.5f; p.BeginPath(); Rect(p, p0.x, p0.y, p1.x - p0.x, p1.y - p0.y); p.Stroke(); drawn++;
                    }
                }
            ParcelsDrawn = drawn;
            foreach (var pin in Pins) { var s = ToScreen(pin.X, pin.Y); p.fillColor = new Color(1f, .8f, .2f); p.BeginPath(); Rect(p, s.x - 4, s.y - 4, 8, 8); p.Fill(); }
            if (Spot is { } spot) { var s = ToScreen(spot.X, spot.Y); p.strokeColor = Color.white; p.lineWidth = 2f; p.BeginPath(); p.MoveTo(new Vector2(s.x - 8, s.y)); p.LineTo(new Vector2(s.x + 8, s.y)); p.MoveTo(new Vector2(s.x, s.y - 8)); p.LineTo(new Vector2(s.x, s.y + 8)); p.Stroke(); }
            if (You is { } you) { var s = ToScreen(you.X, you.Y); p.fillColor = new Color(.3f, .9f, 1f); p.BeginPath(); p.Arc(s, 6f, 0f, 360f); p.Fill(); }
        }

        private static void Rect(Painter2D p, float x, float y, float w, float h) { p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); }
    }

    /// <summary>
    /// The world map: the atlas of islands with a search, the islands the player is tied to (landmarks, favorites, recent visits, friends), a card for the chosen island and the trip there. "Viajar" goes to the chosen spot of the island
    /// (or to where the island spawns people) as a controlled transfer; landmarks save a spot with a name; the star keeps an island among the favorites.
    /// </summary>
    public sealed class WorldMapWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Func<Vector3?> _playerPosition;
        public readonly MapCanvas Canvas = new MapCanvas(); private readonly TextField _search, _spotField, _markName; private readonly Label _status, _card; private readonly VisualElement _places, _results; private readonly Button _travel, _favorite, _mark;
        public AtlasPage Page { get; private set; } = AtlasPage.Empty;
        public AtlasIsland Chosen { get; private set; }
        public int ResultCount => _results.childCount;
        /// <summary>What the window last told the person (for the tests).</summary>
        public string Notice => _status.text;
        public List<MapMark> Marks { get; } = new List<MapMark>();
        public List<MapFolder> Folders { get; } = new List<MapFolder>();
        public List<(Guid Island, string Name)> Favorites { get; } = new List<(Guid, string)>();
        public List<(Guid Island, string Name)> Recent { get; } = new List<(Guid, string)>();
        public List<(Guid Island, string Name)> Friends { get; } = new List<(Guid, string)>();
        public int MaxLandmarks { get; private set; } = 200;
        public TextField SearchField => _search;
        public TextField SpotField => _spotField;
        public TextField MarkNameField => _markName;
        public Button TravelButton => _travel;

        public WorldMapWindow(ApiContext context, Func<Vector3?> playerPosition = null) : base(context.Ui, "world-map", "Mapa-múndi", 840f, 600f, new Vector2(180, 60))
        {
            _c = context; _playerPosition = playerPosition; Canvas.Current = context.CurrentIsland != null ? context.CurrentIsland() : Guid.Empty;
            _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            _search = UiKit.Field("Buscar ilha", false, 60); _search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) LoadAtlas(); }, TrickleDown.TrickleDown);
            var left = new VisualElement(); left.style.width = 250; left.style.marginRight = 8; left.Add(_search); left.Add(UiKit.Button("Buscar", LoadAtlas)); left.Add(_status);
            _results = new ScrollView(); _results.style.height = 150; left.Add(UiKit.Text("Ilhas", "lv-bold", "lv-accent")); left.Add(_results);
            _places = new ScrollView(); _places.style.flexGrow = 1; left.Add(_places);
            var centre = new VisualElement(); centre.style.flexGrow = 1; _parcelsToggle = new Toggle("Mostrar terrenos (nas ilhas grandes na tela)") { name = "show-parcels" }; _parcelsToggle.RegisterValueChangedCallback(e => SetParcels(e.newValue)); centre.Add(_parcelsToggle); centre.Add(Canvas);
            _parcelLine = UiKit.Text("", "lv-small"); _parcelLine.style.whiteSpace = WhiteSpace.Normal;
            _card = UiKit.Text("Escolha uma ilha no mapa.", "lv-small"); _card.style.whiteSpace = WhiteSpace.Normal; _card.style.minHeight = 54; centre.Add(_card);
            centre.Add(_parcelLine); _spotField = UiKit.Field("Ponto na ilha (x z, opcional)", false, 40); _spotField.RegisterValueChangedCallback(_ => ShowParcelAtSpot()); centre.Add(_spotField);
            _travel = UiKit.Button("Viajar", Travel, "lv-btn-primary"); _favorite = UiKit.Button("Favoritar", ToggleFavorite); _markName = UiKit.Field("Nome do marco", false, 60); _mark = UiKit.Button("Salvar marco aqui", AddMark);
            centre.Add(UiKit.Row(_travel, _favorite)); centre.Add(UiKit.Row(_markName, _mark));
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexGrow = 1; row.Add(left); row.Add(centre); Body.Add(row);
            Canvas.TileOf = island => _tileCache.TryGetValue(island.Id, out var tile) && tile.Version == island.TileVersion ? tile.Texture : null; Canvas.TileWanted = FetchTile;
            Canvas.ParcelsWanted = LoadParcels;
            Canvas.Clicked += OnClicked; SetChosen(null); LoadAtlas(); LoadPanels();
        }

        // ---- the pictures of the islands from above: kept in memory (the most recently asked ones), asked a few at a time, and an island with no picture is not asked again for a while ----
        private sealed class TileEntry { public string Version = ""; public Texture2D Texture; public long Used; }
        private readonly Dictionary<Guid, TileEntry> _tileCache = new Dictionary<Guid, TileEntry>(); private readonly HashSet<Guid> _tileLoading = new HashSet<Guid>(); private readonly Dictionary<Guid, float> _tileNone = new Dictionary<Guid, float>(); private long _tileTick;
        public const int MaxTiles = 96, MaxParallelTiles = 3; public const float NoTileSeconds = 300f;
        public int TileCount => _tileCache.Count;
        public int TileFetches { get; private set; }

        private void FetchTile(AtlasIsland island)
        {
            if (_tileLoading.Contains(island.Id) || _tileLoading.Count >= MaxParallelTiles || (_tileNone.TryGetValue(island.Id, out var until) && Time.realtimeSinceStartup < until)) return;
            _tileLoading.Add(island.Id); TileFetches++; var version = island.TileVersion;
            this.Then(_c.Api.GetBytesAsync(MapApi.TilePath(island.Id)), r =>
            {
                _tileLoading.Remove(island.Id); var texture = r.Status == 200 && r.Bytes.Length > 8 ? new Texture2D(2, 2) : null;
                if (texture == null || !texture.LoadImage(r.Bytes)) { if (texture != null) UnityEngine.Object.Destroy(texture); _tileNone[island.Id] = Time.realtimeSinceStartup + NoTileSeconds; return; }
                _tileCache[island.Id] = new TileEntry { Version = version, Texture = texture, Used = ++_tileTick };
                while (_tileCache.Count > MaxTiles) { var oldest = _tileCache.OrderBy(p => p.Value.Used).First(); UnityEngine.Object.Destroy(oldest.Value.Texture); _tileCache.Remove(oldest.Key); }
                Canvas.Refresh();
            });
        }

        // ---- the parcels drawn over the islands ----
        private Toggle _parcelsToggle; private Label _parcelLine; private readonly HashSet<Guid> _parcelsLoading = new HashSet<Guid>();
        public Toggle ParcelsToggle => _parcelsToggle;
        public string ParcelLine => _parcelLine.text;
        public int ParcelFetches { get; private set; }
        private readonly Dictionary<Guid, int> _parcelFetchesOf = new Dictionary<Guid, int>();
        /// <summary>How many times the parcels of one island were asked for (for the tests).</summary>
        public int ParcelFetchesOf(Guid island) => _parcelFetchesOf.TryGetValue(island, out var n) ? n : 0;

        public void SetParcels(bool on) { _parcelsToggle.SetValueWithoutNotify(on); Canvas.ShowParcels = on; Canvas.Refresh(); ShowParcelAtSpot(); }

        /// <summary>Asks for the parcels of an island once (the canvas asks when it wants to draw them).</summary>
        public void LoadParcels(AtlasIsland island)
        {
            if (island == null || _parcelsLoading.Contains(island.Id) || Canvas.Parcels.ContainsKey(island.Id)) return; _parcelsLoading.Add(island.Id); ParcelFetches++; _parcelFetchesOf[island.Id] = ParcelFetchesOf(island.Id) + 1;
            this.Then(_c.Api.ApiAsync("GET", MapApi.ParcelsPath(island.Id)), r => { _parcelsLoading.Remove(island.Id); Canvas.Parcels[island.Id] = r.Status == 200 ? MapApi.ParseParcels(r.Body) : new List<MapParcel>(); Canvas.Refresh(); ShowParcelAtSpot(); });
        }

        /// <summary>The parcel under the chosen spot, when parcels are shown: its name, what it is to the player and its price.</summary>
        private void ShowParcelAtSpot()
        {
            if (!Canvas.ShowParcels || Chosen == null || TypedSpot() is not { } spot) { _parcelLine.text = ""; return; }
            var parcel = Canvas.ParcelAt(Chosen.Id, spot.X, spot.Z); if (parcel == null) { _parcelLine.text = Canvas.Parcels.ContainsKey(Chosen.Id) ? "Fora de qualquer terreno." : ""; return; }
            _parcelLine.text = "Terreno: " + (parcel.Code.Length > 0 ? parcel.Code + " — " : "") + parcel.Name + " (" + parcel.StateWords.ToLowerInvariant() + ")" + (parcel.SalePrice is { } sale ? "  ·  " + LandText.Money(sale) : "") + (parcel.RentPrice is { } rent ? "  ·  " + LandText.Money(rent) + " / " + LandText.Period(parcel.RentPeriodDays ?? 30) : "") + (parcel.Owner.Length > 0 ? "  ·  de " + parcel.Owner : "") + (parcel.Teleport ? "" : "  ·  não aceita teletransporte");
        }

        public void LoadAtlas() => this.Then(_c.Api.ApiAsync("GET", MapApi.AtlasPath(_search.value)), r =>
        {
            if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status, r.Body); return; }
            Page = MapApi.ParseAtlas(r.Body); Canvas.Islands.Clear(); Canvas.Islands.AddRange(Page.Items); _status.text = Page.Items.Count == 0 ? "Nenhuma ilha encontrada." : Page.Items.Count + " ilha(s)" + (Page.Truncated ? " (há mais: refine a busca)" : "");
            _results.Clear(); foreach (var island in Page.Items.Take(60)) { var captured = island; _results.Add(UiKit.Button(island.Name + "  ·  " + island.AccessWords, () => Choose(captured, true), "lv-btn-flat")); }
            UpdateYou(); if (Canvas.Centre == Vector2.zero && Canvas.Scale <= .05f) { var current = Page.Items.FirstOrDefault(i => i.Id == Canvas.Current); if (current != null) Canvas.Fit(current); else Canvas.Fit(); } else Canvas.Refresh(); RebuildPins();
        });

        private void UpdateYou()
        {
            var here = Page.Items.FirstOrDefault(i => i.Id == Canvas.Current); var position = _playerPosition?.Invoke();
            Canvas.You = here == null ? null : position is { } p ? MapCoordinates.ToGlobal(here, p.x, p.z) : (here.X, here.Y);
        }

        /// <summary>The landmarks, favorites, recent visits and friends' islands of the player.</summary>
        public void LoadPanels() => this.Then(_c.Api.ApiAsync("GET", "/api/v1/map/landmarks"), r =>
        {
            if (r.Status != 200) return; Marks.Clear(); Folders.Clear(); Favorites.Clear(); Recent.Clear(); Friends.Clear();
            try
            {
                using var d = JsonDocument.Parse(r.Body); var root = d.RootElement; Guid Id(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : Guid.Empty;
                string S(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; float N(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : 0f;
                IEnumerable<JsonElement> List(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.Clone()).ToList() : new List<JsonElement>();
                foreach (var m in List("landmarks")) Marks.Add(new MapMark { Id = Id(m, "id"), Name = S(m, "name"), Description = S(m, "description"), Island = Id(m, "islandId"), IslandName = S(m, "islandName"), X = N(m, "x"), Z = N(m, "z"), Folder = Id(m, "folderId") is var folder && folder != Guid.Empty ? folder : (Guid?)null, IsPublic = m.TryGetProperty("isPublic", out var pub) && pub.ValueKind == JsonValueKind.True });
                foreach (var f in List("folders")) Folders.Add(new MapFolder { Id = Id(f, "id"), Name = S(f, "name") });
                foreach (var f in List("favorites")) Favorites.Add((Id(f, "islandId"), S(f, "name"))); foreach (var f in List("recent")) Recent.Add((Id(f, "islandId"), S(f, "name"))); foreach (var f in List("friends")) Friends.Add((Id(f, "islandId"), S(f, "name")));
                if (root.TryGetProperty("limits", out var limits) && limits.TryGetProperty("landmarks", out var lm) && lm.TryGetInt32(out var max) && max > 0) MaxLandmarks = max;
            }
            catch (JsonException) { return; }
            FillPlaces(); RebuildPins();
        });

        private void FillPlaces()
        {
            _places.Clear();
            void Group(string title, IEnumerable<(Guid Island, string Name)> rows, Func<Guid, float?> x = null)
            {
                var list = rows.ToList(); _places.Add(UiKit.Text(title + " (" + list.Count + ")", "lv-bold", "lv-accent")); if (list.Count == 0) _places.Add(UiKit.Text("Nada ainda.", "lv-muted", "lv-small"));
                foreach (var (island, name) in list) { var target = island; _places.Add(UiKit.Button(name, () => GoToIsland(target), "lv-btn-flat")); }
            }
            _places.Add(UiKit.Row(UiKit.Text("Meus marcos (" + Marks.Count + "/" + MaxLandmarks + ")", "lv-bold", "lv-accent"), UiKit.Spacer(), UiKit.Button("Gerenciar", () => new MapMarksWindow(_c, this).Open()))); if (Marks.Count == 0 && Folders.Count == 0) _places.Add(UiKit.Text("Escolha um ponto no mapa e salve um marco.", "lv-muted", "lv-small"));
            VisualElement MarkRow(MapMark m) { var mark = m; return UiKit.Row(UiKit.Button((m.IsPublic ? "[público] " : "") + m.Name, () => TravelTo(mark.Island, mark.X, mark.Z), "lv-btn-flat"), UiKit.Spacer(), Controls.Confirm("Apagar", () => "Confirmar", () => this.Then(_c.Api.ApiAsync("DELETE", "/api/v1/map/landmarks/" + mark.Id), r => { if (r.Status >= 200 && r.Status < 300) LoadPanels(); else _status.text = UiTasks.Explain(r.Status, r.Body); }), "lv-btn-flat")); }
            foreach (var folder in Folders) { var inside = Marks.Where(m => m.Folder == folder.Id).ToList(); _places.Add(UiKit.Text("Pasta: " + folder.Name + " (" + inside.Count + ")", "lv-small", "lv-bold")); foreach (var m in inside) _places.Add(MarkRow(m)); }
            foreach (var m in Marks.Where(m => m.Folder == null || Folders.All(f => f.Id != m.Folder))) _places.Add(MarkRow(m));            Group("Favoritos", Favorites); Group("Visita recente", Recent); Group("Ilhas de amigos", Friends);
        }

        private void RebuildPins()
        {
            Canvas.Pins.Clear();
            foreach (var m in Marks) if (Page.Items.FirstOrDefault(i => i.Id == m.Island) is { } island) { var (gx, gy) = MapCoordinates.ToGlobal(island, m.X, m.Z); Canvas.Pins.Add(new MapPin { Id = m.Id, Island = island.Id, Name = m.Name, X = gx, Y = gy }); }
            Canvas.Refresh();
        }

        private void OnClicked(AtlasIsland island, double localX, double localZ) { SetChosen(island); if (island != null) _spotField.value = Math.Round(localX) + " " + Math.Round(localZ); ShowParcelAtSpot(); }

        /// <summary>Picks an island from a list: it is framed on the map and its card opens.</summary>
        public void Choose(AtlasIsland island, bool frame) { Canvas.Selected = island.Id; Canvas.Spot = null; if (frame) Canvas.Fit(island); else Canvas.Refresh(); SetChosen(island); _spotField.value = ""; }

        private void GoToIsland(Guid id) { var island = Page.Items.FirstOrDefault(i => i.Id == id); if (island != null) Choose(island, true); else _c.EnterIsland?.Invoke(id); }

        private void SetChosen(AtlasIsland island)
        {
            Chosen = island; _travel.SetEnabled(island != null && island.CanEnter); _favorite.SetEnabled(island != null); _mark.SetEnabled(island != null && island.CanEnter); _favorite.text = island != null && island.Favorite ? "Tirar dos favoritos" : "Favoritar";
            _card.text = island == null ? "Escolha uma ilha no mapa." : island.Name + (island.Category.Length > 0 ? "  ·  " + island.Category : "") + "\n" + (island.Description.Length > 0 ? island.Description + "\n" : "") + island.AccessWords + (island.Online ? "  ·  no ar" : "") + (island.Visitors != null ? "  ·  " + island.Visitors + "/" + island.MaxPlayers + " pessoas" : "") + (island.OwnerName.Length > 0 ? "  ·  de " + island.OwnerName : "") + (island.Favorites > 0 ? "  ·  ★ " + island.Favorites : "");
        }

        /// <summary>The spot typed in the field (meters from the island's centre), or null when it is empty or not two numbers.</summary>
        public (float X, float Z)? TypedSpot() => MapCoordinates.TryParsePair(_spotField.value, out var x, out var z) ? ((float)x, (float)z) : ((float, float)?)null;

        public void Travel()
        {
            if (Chosen == null || !Chosen.CanEnter) return; var spot = TypedSpot();
            if (_spotField.value.Trim().Length > 0 && spot == null) { _status.text = "Escreva o ponto como dois números: x z."; return; }
            if (spot is { } s) { var (_, _, inside) = MapCoordinates.ToLocal(Chosen, s.X + Chosen.X, s.Z + Chosen.Y); if (!inside) { _status.text = "Esse ponto fica fora da ilha."; return; } var island = Chosen.Id; Arrival.Go(_c, island, s.X, s.Z, (text, bad) => { _status.text = text; _status.EnableInClassList("lv-bad", bad); if (!bad && !text.StartsWith("Verificando")) Close(); }); }
            else TravelTo(Chosen.Id, null, null);
        }

        private void TravelTo(Guid island, float? x, float? z) { if (_c.TravelTo != null) _c.TravelTo(island, x, z); else _c.EnterIsland?.Invoke(island); Close(); }

        public void ToggleFavorite()
        {
            if (Chosen == null) return; var island = Chosen; var path = "/api/v1/discovery/islands/" + island.Id + "/favorite";
            this.Then(_c.Api.ApiAsync(island.Favorite ? "DELETE" : "POST", path), r => { if (r.Status >= 200 && r.Status < 300) { island.Favorite = !island.Favorite; SetChosen(island); LoadPanels(); } else _status.text = UiTasks.Explain(r.Status, r.Body); });
        }

        public void AddMark()
        {
            if (Chosen == null || !Chosen.CanEnter) return; var name = _markName.value.Trim(); if (name.Length == 0) { _status.text = "Dê um nome ao marco."; return; } if (Marks.Count >= MaxLandmarks) { _status.text = "Você chegou ao limite de marcos. Apague algum."; return; }
            var spot = TypedSpot() ?? (Chosen.SpawnX, Chosen.SpawnZ); var body = JsonSerializer.Serialize(new { name, description = "", islandId = Chosen.Id, x = spot.Item1, y = 0f, z = spot.Item2, folderId = (Guid?)null, isPublic = false });
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/map/landmarks", body), r => { if (r.Status >= 200 && r.Status < 300) { _markName.value = ""; _status.text = "Marco salvo."; LoadPanels(); } else _status.text = MarkProblem(r.Body) ?? UiTasks.Explain(r.Status, r.Body); });
        }

        public static string MarkProblem(string body)
        {
            try { using var d = JsonDocument.Parse(body); var code = d.RootElement.TryGetProperty("code", out var c) ? c.GetString() : d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                return code switch { "island_not_found" => "Esta ilha não está mais disponível.", "folder_not_found" => "Essa pasta não existe mais.", "too_many_landmarks" => "Você chegou ao limite de marcos. Apague algum.", _ => null }; }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException) { return null; }
        }
    }
}
