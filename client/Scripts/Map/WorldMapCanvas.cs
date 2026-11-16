using Godot;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>A parcel of an island as the server describes it to this player (bounds in metres from the island centre).</summary>
public sealed record ParcelShape(Guid Id, string Name, string State, string Owner, string Category, string Description, double Area, double MinX, double MinZ, double MaxX, double MaxZ, float SpawnX, float SpawnZ, bool Teleport, long? SalePrice, long? RentPrice);

/// <summary>A marker the map draws over an island: a landmark of the player or a friend. Position as a point of the atlas.</summary>
public sealed record MapPin(Guid Id, string Kind, string Name, Guid Island, double X, double Y);

/// <summary>
/// The drawn part of the world map: the ocean, the grid of the atlas, every island at its place (its picture from above, or a neutral square while there is none), the markers on them, the player and the
/// destination. It pans with the left button, zooms with the wheel around the cursor, selects with a click, and asks for a menu with the right button. Far away the islands become dots grouped by count; close up
/// they show names and markers. Everything it draws comes from the atlas: nothing is invented.
/// </summary>
public partial class WorldMapCanvas : Control
{
    public static readonly Color Ocean = new("#0E2230"), OceanLight = new("#12303F"), Online = new("#39D98A"), Error = new("#F16B73"), Card = new("#23323C");
    public const double MinZoom = 0.02, MaxZoom = 6;

    public MapHub Hub { get; set; } = null!;
    /// <summary>The islands to draw (the window filters them).</summary>
    public IReadOnlyList<AtlasIsland> Islands { get => _islands; set { _islands = value; _byId = []; foreach (var i in value) _byId[i.Id] = i; } }
    private IReadOnlyList<AtlasIsland> _islands = [];
    private Dictionary<Guid, AtlasIsland> _byId = [];
    private double _sinceRedraw;
    public HashSet<Guid> EventIslands { get; } = [];
    public HashSet<Guid> FeaturedIslands { get; } = [];
    public Guid Selected { get; private set; }
    public Guid Hovered { get; private set; }
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public double Zoom { get; private set; } = 0.35;
    public int CellMeters { get; set; } = 768;

    /// <summary>An island was chosen (or the choice was cleared: Guid.Empty).</summary>
    public event Action<Guid>? SelectionChanged;
    /// <summary>An island was double clicked.</summary>
    public event Action<Guid>? Activated;
    /// <summary>The right button: the island under it (or Guid.Empty), where, and the point of the atlas.</summary>
    public event Action<Guid, Vector2, double, double>? ContextRequested;
    /// <summary>The view moved or zoomed (to be remembered).</summary>
    public event Action? ViewChanged;

    /// <summary>Draw the parcels of the islands that are big enough on screen ("Mostrar Terrenos").</summary>
    public bool ShowParcels { get; set; }
    /// <summary>Parcels per island, filled by the window when <see cref="ParcelsNeeded"/> asks.</summary>
    public Dictionary<Guid, List<ParcelShape>> Parcels { get; } = [];
    public Guid SelectedParcel { get; private set; }
    /// <summary>Landmarks and friends drawn over the islands.</summary>
    public List<MapPin> Pins { get; } = [];
    /// <summary>The spot inside an island the player chose by clicking at a close zoom (an island, and metres from its centre).</summary>
    public (Guid Island, float X, float Z)? Point { get; private set; }
    /// <summary>On screen pixels an island needs before a click inside it chooses a spot, and before parcels are drawn.</summary>
    public const float PointPixels = 220, ParcelPixels = 160;

    /// <summary>A spot inside an island was chosen: the island and metres from its centre.</summary>
    public event Action<Guid, float, float>? PointPicked;
    /// <summary>A parcel was chosen (island, parcel; Guid.Empty parcel: none).</summary>
    public event Action<Guid, Guid>? ParcelPicked;
    /// <summary>An island is big enough on screen for its parcels and they are not known yet.</summary>
    public event Action<Guid>? ParcelsNeeded;
    private readonly HashSet<Guid> _parcelsAsked = [];

    public void ForgetParcels() { Parcels.Clear(); _parcelsAsked.Clear(); QueueRedraw(); }

    public void ClearPoint() { if (Point is null && SelectedParcel == Guid.Empty) return; Point = null; SelectedParcel = Guid.Empty; QueueRedraw(); }

    /// <summary>Chooses a spot inside an island (as a click would), and the parcel under it when the parcels are on.</summary>
    public void PickPoint(AtlasIsland island, float x, float z)
    {
        Point = (island.Id, x, z); var hit = ParcelAt(island.Id, x, z); SelectedParcel = hit?.Id ?? Guid.Empty; QueueRedraw(); PointPicked?.Invoke(island.Id, x, z); ParcelPicked?.Invoke(island.Id, SelectedParcel);
    }

    public ParcelShape? ParcelAt(Guid island, double x, double z) => Parcels.TryGetValue(island, out var list) ? list.Where(p => x >= p.MinX && x <= p.MaxX && z >= p.MinZ && z <= p.MaxZ).OrderBy(p => (p.MaxX - p.MinX) * (p.MaxZ - p.MinZ)).FirstOrDefault() : null;

    private readonly Dictionary<Guid, (string Version, Texture2D Texture)> _pictures = [];
    private readonly HashSet<(Guid, string)> _asked = [];
    private readonly Dictionary<string, Texture2D> _icons = [];
    private readonly List<(Vector2 At, float Radius, List<AtlasIsland> Members)> _clusters = [];
    private readonly List<(AtlasIsland Island, Rect2 Rect)> _drawn = [];
    private bool _dragging, _moved; private Vector2 _pressAt;
    private double _goalX, _goalY, _goalZoom; private bool _animating; private double _sinceTrim;

    public int VisibleCount { get { Layout(); return _drawn.Count; } }
    public IReadOnlyList<(Vector2 At, float Radius, List<AtlasIsland> Members)> Clusters { get { Layout(); return _clusters; } }
    public bool IsClusteredForTesting { get { Layout(); return _clusterMode; } }

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All; ClipContents = true; MouseFilter = MouseFilterEnum.Stop; CustomMinimumSize = new Vector2(320, 240); TextureFilter = TextureFilterEnum.Linear;
        TooltipText = string.Empty;
    }

    // ---- the view ---------------------------------------------------------------------------------------------------------------------

    public Vector2 ToScreen(double gx, double gy) => Size / 2f + new Vector2((float)((gx - CenterX) * Zoom), (float)((gy - CenterY) * Zoom));
    public (double X, double Y) ToGlobal(Vector2 screen) => (CenterX + (screen.X - Size.X / 2f) / Zoom, CenterY + (screen.Y - Size.Y / 2f) / Zoom);

    public Rect2 RectOf(AtlasIsland island) { var half = (float)(island.Size * Zoom / 2); var c = ToScreen(island.X, island.Y); return new Rect2(c - new Vector2(half, half), new Vector2(half * 2, half * 2)); }

    public void SetView(double centerX, double centerY, double zoom, bool announce = true)
    {
        (CenterX, CenterY, Zoom) = (centerX, centerY, Math.Clamp(zoom, MinZoom, MaxZoom)); _animating = false; QueueRedraw(); if (announce) ViewChanged?.Invoke();
    }

    /// <summary>Goes to a view smoothly (a quarter of a second).</summary>
    public void GlideTo(double centerX, double centerY, double zoom) { (_goalX, _goalY, _goalZoom) = (centerX, centerY, Math.Clamp(zoom, MinZoom, MaxZoom)); _animating = true; SetProcess(true); }

    public void Pan(Vector2 pixels) { CenterX -= pixels.X / Zoom; CenterY -= pixels.Y / Zoom; _animating = false; QueueRedraw(); ViewChanged?.Invoke(); }

    public void ZoomAt(Vector2 screen, double factor)
    {
        var before = ToGlobal(screen); Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom); CenterX = before.X - (screen.X - Size.X / 2f) / Zoom; CenterY = before.Y - (screen.Y - Size.Y / 2f) / Zoom; _animating = false; QueueRedraw(); ViewChanged?.Invoke();
    }

    public void ZoomCentered(double factor) => ZoomAt(Size / 2f, factor);

    /// <summary>Shows all the islands that are listed, with a margin.</summary>
    public void FitAll(bool glide = false)
    {
        if (Islands.Count == 0) { SetView(0, 0, Zoom, announce: false); return; }
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var i in Islands) { var r = MapCoordinates.Rect(i); minX = Math.Min(minX, r.MinX); minY = Math.Min(minY, r.MinY); maxX = Math.Max(maxX, r.MaxX); maxY = Math.Max(maxY, r.MaxY); }
        FitRect(minX, minY, maxX, maxY, glide);
    }

    public void FitRect(double minX, double minY, double maxX, double maxY, bool glide = false)
    {
        var width = Math.Max(maxX - minX, 64) * 1.25; var height = Math.Max(maxY - minY, 64) * 1.25; var zoom = Math.Clamp(Math.Min(Math.Max(Size.X, 100) / width, Math.Max(Size.Y, 100) / height), MinZoom, 1.6);
        var cx = (minX + maxX) / 2; var cy = (minY + maxY) / 2; if (glide) GlideTo(cx, cy, zoom); else SetView(cx, cy, zoom);
    }

    public void FitIsland(AtlasIsland island, bool glide = true) => FitRect(MapCoordinates.Rect(island).MinX, MapCoordinates.Rect(island).MinY, MapCoordinates.Rect(island).MaxX, MapCoordinates.Rect(island).MaxY, glide);

    public bool CenterOnPlayer(bool glide = true)
    {
        if (Hub.PlayerGlobal() is not { } player) return false;
        var zoom = Math.Max(Zoom, 0.9 * Math.Min(1, Size.X / 640)); if (glide) GlideTo(player.X, player.Y, zoom); else SetView(player.X, player.Y, zoom); return true;
    }

    public void Select(Guid island, bool announce = true) { if (Selected == island) return; Point = null; SelectedParcel = Guid.Empty; Selected = island; QueueRedraw(); if (announce) SelectionChanged?.Invoke(island); }

    // ---- what is under a point ---------------------------------------------------------------------------------------------------------

    /// <summary>The island under a screen point (the one drawn last wins).</summary>
    public AtlasIsland? Pick(Vector2 screen) { Layout(); for (var i = _drawn.Count - 1; i >= 0; i--) if (_drawn[i].Rect.HasPoint(screen)) return _drawn[i].Island; return null; }

    public List<AtlasIsland>? PickCluster(Vector2 screen) { Layout(); foreach (var (at, radius, members) in _clusters) if (at.DistanceTo(screen) <= radius + 4) return members; return null; }

    /// <summary>Zooms in on a group of islands (what a click on a cluster does).</summary>
    public void ZoomToGroup(IReadOnlyList<AtlasIsland> members)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var i in members) { var r = MapCoordinates.Rect(i); minX = Math.Min(minX, r.MinX); minY = Math.Min(minY, r.MinY); maxX = Math.Max(maxX, r.MaxX); maxY = Math.Max(maxY, r.MaxY); }
        FitRect(minX, minY, maxX, maxY, glide: true);
    }

    // ---- input ----------------------------------------------------------------------------------------------------------------------------

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed) { ZoomAt(button.Position, 1.18); AcceptEvent(); return; }
            if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed) { ZoomAt(button.Position, 1 / 1.18); AcceptEvent(); return; }
            if (button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed) { GrabFocus(); _dragging = true; _moved = false; _pressAt = button.Position; }
                else if (_dragging)
                {
                    _dragging = false;
                    if (!_moved)
                    {
                        if (PickCluster(button.Position) is { } group) { ZoomToGroup(group); return; }
                        var island = Pick(button.Position); Select(island?.Id ?? Guid.Empty);
                        if (island is not null && RectOf(island).Size.X >= PointPixels) { var (gx, gy) = ToGlobal(button.Position); var (lx, lz, inside) = MapCoordinates.ToLocal(island, gx, gy); if (inside) PickPoint(island, (float)lx, (float)lz); else ClearPoint(); } else ClearPoint();
                        if (island is not null && button.DoubleClick) Activated?.Invoke(island.Id);
                    }
                }
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Right && button.Pressed)
            {
                var island = Pick(button.Position); var (gx, gy) = ToGlobal(button.Position); ContextRequested?.Invoke(island?.Id ?? Guid.Empty, GetScreenTransform() * button.Position, gx, gy); AcceptEvent();
            }
        }
        else if (input is InputEventMouseMotion motion)
        {
            if (_dragging) { if (!_moved && motion.Position.DistanceTo(_pressAt) > 4) _moved = true; if (_moved) Pan(motion.Relative); }
            else { var hover = Pick(motion.Position)?.Id ?? Guid.Empty; if (hover != Hovered) { Hovered = hover; MouseDefaultCursorShape = hover == Guid.Empty ? CursorShape.Arrow : CursorShape.PointingHand; QueueRedraw(); } }
        }
        else if (input is InputEventKey { Pressed: true } key)
        {
            var handled = true;
            switch (key.Keycode)
            {
                case Key.Left: Pan(new Vector2(80, 0)); break; case Key.Right: Pan(new Vector2(-80, 0)); break; case Key.Up: Pan(new Vector2(0, 80)); break; case Key.Down: Pan(new Vector2(0, -80)); break;
                case Key.Equal or Key.Plus or Key.KpAdd: ZoomCentered(1.25); break; case Key.Minus or Key.KpSubtract: ZoomCentered(1 / 1.25); break;
                case Key.Home: CenterOnPlayer(); break; case Key.Escape: if (Selected != Guid.Empty) Select(Guid.Empty); else handled = false; break;
                case Key.Enter or Key.KpEnter: if (Selected != Guid.Empty) Activated?.Invoke(Selected); else handled = false; break;
                default: handled = false; break;
            }
            if (handled) AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        _sinceTrim += delta;
        if (_animating)
        {
            var t = Math.Min(1, delta / 0.12); CenterX += (_goalX - CenterX) * t; CenterY += (_goalY - CenterY) * t; Zoom += (_goalZoom - Zoom) * t;
            if (Math.Abs(_goalX - CenterX) * Zoom < .5 && Math.Abs(_goalY - CenterY) * Zoom < .5 && Math.Abs(_goalZoom - Zoom) < Zoom * .005) { (CenterX, CenterY, Zoom) = (_goalX, _goalY, _goalZoom); _animating = false; ViewChanged?.Invoke(); }
            QueueRedraw();
        }
        if (_sinceTrim > .4 && Hub is not null) { _sinceTrim = 0; Hub.Tiles.CancelExcept(new HashSet<Guid>(_drawn.Where(d => d.Island.HasTile).Select(d => d.Island.Id))); }
        _sinceRedraw += delta; if (_sinceRedraw >= 1 / 30.0 && IsVisibleInTree()) { _sinceRedraw = 0; QueueRedraw(); } // the pulse of the player's pin: thirty times a second is enough
    }

    // ---- drawing ----------------------------------------------------------------------------------------------------------------------------

    private Texture2D Icon(string name, Color color, int size)
    {
        var key = $"{name}|{color.ToHtml()}|{size}"; if (!_icons.TryGetValue(key, out var texture)) _icons[key] = texture = UiStyle.Icon(name, color, size); return texture;
    }

    private static string CategoryIcon(string category) => category.ToLowerInvariant() switch { "residencial" => "home", "comercial" => "bag", _ => string.Empty };

    private void Badge(Vector2 center, string icon, Color color, float size = 18)
    {
        DrawCircle(center, size / 2f + 2, new Color(UiStyle.PanelFillSolid, .92f)); DrawArc(center, size / 2f + 2, 0, Mathf.Tau, 20, new Color(color, .9f), 1.4f, true);
        var texture = Icon(icon, color, (int)size - 4); DrawTexture(texture, center - texture.GetSize() / 2f);
    }

    private bool _clusterMode;

    /// <summary>Works out what is on screen: which islands (or, when far away, which groups of them) and where, and asks for the pictures that are needed. Drawing and picking both read this.</summary>
    public void Layout()
    {
        _drawn.Clear(); _clusters.Clear();
        var screen = new Rect2(Vector2.Zero, Size).Grow(80); var shown = Islands.Where(i => screen.Intersects(RectOf(i))).ToList(); _clusterMode = Islands.Count > 0 && 256 * Zoom < 22;
        if (_clusterMode)
        {
            var buckets = new Dictionary<(int, int), List<AtlasIsland>>();
            foreach (var island in shown) { var at = ToScreen(island.X, island.Y); var key = ((int)Mathf.Floor(at.X / 46f), (int)Mathf.Floor(at.Y / 46f)); if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = []; list.Add(island); }
            foreach (var members in buckets.Values)
            {
                var at = Vector2.Zero; foreach (var m in members) at += ToScreen(m.X, m.Y); at /= members.Count; _clusters.Add((at, members.Count == 1 ? 7f : 11f + Mathf.Min(7f, members.Count * .6f), members));
            }
            return;
        }
        foreach (var island in shown.OrderBy(i => i.Id == Selected ? 1 : 0)) { var rect = RectOf(island); _drawn.Add((island, rect)); if (rect.Size.X >= 24 && island.HasTile) Request(island); if (ShowParcels && rect.Size.X >= ParcelPixels && _parcelsAsked.Add(island.Id)) ParcelsNeeded?.Invoke(island.Id); }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Ocean); DrawRect(new Rect2(Vector2.Zero, Size / new Vector2(1, 2)), new Color(OceanLight, .35f)); DrawGrid(); Layout(); var font = UiStyle.Regular;
        if (_clusterMode)
        {
            foreach (var (at, radius, members) in _clusters)
            {
                var color = members.Any(m => m.Id == Selected) ? UiStyle.Accent : members.All(m => !m.CanEnter) ? new Color(UiStyle.TextMuted, .9f) : members.Any(m => m.Online) ? Online : UiStyle.AccentText;
                DrawCircle(at, radius + 3, new Color(0, 0, 0, .35f)); DrawCircle(at, radius, color); DrawArc(at, radius, 0, Mathf.Tau, 20, new Color(1, 1, 1, .9f), 1.5f, true);
                if (members.Count > 1) DrawString(font, at + new Vector2(-30, 5), members.Count.ToString(), HorizontalAlignment.Center, 60, 13, new Color("#0E2230"));
            }
        }
        else foreach (var (island, rect) in _drawn) DrawIsland(island, rect, font);
        DrawPins(); DrawPoint(); DrawYou(); DrawDestination(); DrawCompass();
    }

    // ---- parcels, pins and the chosen spot -------------------------------------------------------------------------------------------------

    public static Color ParcelColor(string state) => state switch
    {
        "mine" => UiStyle.Accent, "group" => new Color("#7FA8FF"), "restricted" => Error, "sale" => UiStyle.Orange, "rent" => new Color("#FFD27A"), "common" => new Color("#A9BAC3"), _ => Online
    };

    /// <summary>What each state looks like beyond colour: restricted is hatched, for rent is dashed, for sale has a tag, common is dotted; so the map does not depend on colour alone.</summary>
    private void DrawParcels(AtlasIsland island, Rect2 rect)
    {
        if (!Parcels.TryGetValue(island.Id, out var list)) return; var centre = rect.GetCenter(); var scale = (float)Zoom;
        foreach (var p in list)
        {
            var r = new Rect2(centre + new Vector2((float)p.MinX, (float)p.MinZ) * scale, new Vector2((float)(p.MaxX - p.MinX), (float)(p.MaxZ - p.MinZ)) * scale); var color = ParcelColor(p.State); var chosen = p.Id == SelectedParcel;
            DrawRect(r, new Color(color, chosen ? .5f : .26f)); DrawRect(r, new Color(color, .95f), false, chosen ? 3f : 1.6f);
            if (p.State == "restricted") for (var d = 8f; d < r.Size.X + r.Size.Y; d += 9f) { var a = new Vector2(Mathf.Min(r.Position.X + d, r.End.X), Mathf.Max(r.Position.Y, r.Position.Y + d - r.Size.X)); var b = new Vector2(Mathf.Max(r.Position.X, r.Position.X + d - r.Size.Y), Mathf.Min(r.Position.Y + d, r.End.Y)); DrawLine(a, b, new Color(color, .55f), 1.2f); }
            if (p.State == "rent") for (var x = r.Position.X + 3; x < r.End.X - 3; x += 12) DrawLine(new Vector2(x, r.Position.Y + 3), new Vector2(Mathf.Min(x + 6, r.End.X - 3), r.Position.Y + 3), color, 2f);
            if (r.Size.X >= 34 && r.Size.Y >= 26) { var icon = Icon(p.State switch { "mine" => "home", "sale" => "bag", "rent" => "calendar", "restricted" => "lock", "group" => "people", "common" => "star", _ => "map" }, color, 18); DrawTexture(icon, r.GetCenter() - icon.GetSize() / 2f); }
            if (r.Size.X >= 90 && r.Size.Y >= 44 && Hub.Settings.ShowNames) DrawString(UiStyle.Regular, r.Position + new Vector2(0, r.Size.Y - 6), p.Name, HorizontalAlignment.Center, r.Size.X, 12, UiStyle.Text);
        }
    }

    private void DrawPins()
    {
        foreach (var pin in Pins)
        {
            if (!_byId.TryGetValue(pin.Island, out var island) || RectOf(island).Size.X < 90) continue; var at = ToScreen(pin.X, pin.Y); if (!new Rect2(Vector2.Zero, Size).Grow(20).HasPoint(at)) continue;
            if (pin.Kind == "friend") { DrawCircle(at, 8, new Color(UiStyle.PanelFillSolid, .95f)); DrawCircle(at, 6, Online); DrawString(UiStyle.Bold, at + new Vector2(-40, -12), pin.Name, HorizontalAlignment.Center, 80, 12, UiStyle.Text); }
            else { var icon = Icon("star", UiStyle.Orange, 22); DrawTexture(icon, at - new Vector2(11, 22)); DrawString(UiStyle.Regular, at + new Vector2(-50, 14), pin.Name, HorizontalAlignment.Center, 100, 12, UiStyle.Text); }
        }
    }

    private void DrawPoint()
    {
        if (Point is not { } point || Islands.FirstOrDefault(i => i.Id == point.Island) is not { } island) return; var (gx, gy) = MapCoordinates.ToGlobal(island, point.X, point.Z); var at = ToScreen(gx, gy);
        DrawLine(at - new Vector2(12, 0), at + new Vector2(12, 0), UiStyle.Accent, 2f); DrawLine(at - new Vector2(0, 12), at + new Vector2(0, 12), UiStyle.Accent, 2f); DrawArc(at, 8, 0, Mathf.Tau, 20, UiStyle.Accent, 2f, true);
    }

    private void Request(AtlasIsland island)
    {
        if (_pictures.TryGetValue(island.Id, out var have) && have.Version == island.TileVersion) return; if (!_asked.Add((island.Id, island.TileVersion))) return;
        Hub.Tiles.Get(island.Id, island.TileVersion, texture => { if (texture is not null) { _pictures[island.Id] = (island.TileVersion, texture); } QueueRedraw(); });
    }

    private void DrawGrid()
    {
        var step = CellMeters * Zoom; if (step < 18) return; var topLeft = ToGlobal(Vector2.Zero); var bottomRight = ToGlobal(Size);
        var half = CellMeters / 2.0; var first = Math.Floor((topLeft.X + half) / CellMeters) * CellMeters - half; var firstY = Math.Floor((topLeft.Y + half) / CellMeters) * CellMeters - half; var line = new Color(1, 1, 1, .055f);
        for (var gx = first; gx <= bottomRight.X; gx += CellMeters) { var x = ToScreen(gx, 0).X; DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), line, 1); }
        for (var gy = firstY; gy <= bottomRight.Y; gy += CellMeters) { var y = ToScreen(0, gy).Y; DrawLine(new Vector2(0, y), new Vector2(Size.X, y), line, 1); }
        if (step < 60) return; var font = UiStyle.Regular; var label = new Color(UiStyle.TextMuted, .55f);
        for (var gx = Math.Floor(topLeft.X / CellMeters) * CellMeters; gx <= bottomRight.X; gx += CellMeters) DrawString(font, new Vector2(ToScreen(gx, 0).X - 30, 16), $"{gx:0}", HorizontalAlignment.Center, 60, 11, label);
        for (var gy = Math.Floor(topLeft.Y / CellMeters) * CellMeters; gy <= bottomRight.Y; gy += CellMeters) DrawString(font, new Vector2(6, ToScreen(0, gy).Y + 4), $"{gy:0}", HorizontalAlignment.Left, 60, 11, label);
    }

    private void DrawIsland(AtlasIsland island, Rect2 rect, Font font)
    {
        var selected = island.Id == Selected; var hover = island.Id == Hovered;
        DrawRect(new Rect2(rect.Position + new Vector2(3, 4), rect.Size), new Color(0, 0, 0, .35f));
        if (_pictures.TryGetValue(island.Id, out var picture)) DrawTextureRect(picture.Texture, rect, false);
        else
        {
            DrawRect(rect, Card);
            if (rect.Size.X >= 34) { var icon = Icon(island.HasTile ? "map" : "grid", new Color(UiStyle.TextMuted, .55f), (int)Mathf.Clamp(rect.Size.X * .3f, 14, 40)); DrawTexture(icon, rect.GetCenter() - icon.GetSize() / 2f); }
        }
        if (!island.CanEnter) DrawRect(rect, new Color(0, 0, 0, island.Access is "private" or "banned" ? .42f : .28f));
        if (ShowParcels && rect.Size.X >= ParcelPixels) DrawParcels(island, rect);
        var border = selected ? UiStyle.Accent : hover ? UiStyle.AccentText : new Color(UiStyle.BorderSoft, .85f);
        if (selected) DrawRect(rect.Grow(4), new Color(UiStyle.Accent, .22f), false, 6f);
        DrawRect(rect, border, false, selected ? 3f : hover ? 2f : 1.4f);
        if (rect.Size.X >= 40)
        {
            var badges = new List<(string Icon, Color Color)>();
            if (CategoryIcon(island.Category) is { Length: > 0 } icon) badges.Add((icon, island.Category.Equals("residencial", StringComparison.OrdinalIgnoreCase) ? Online : Hub.Settings.ShowFeatured ? UiStyle.Orange : UiStyle.Orange));
            if (Hub.Settings.ShowEvents && EventIslands.Contains(island.Id)) badges.Add(("calendar", new Color("#7FA8FF")));
            if (Hub.Settings.ShowFeatured && FeaturedIslands.Contains(island.Id)) badges.Add(("star", UiStyle.Orange));
            if (Hub.Settings.ShowFavorites && island.Favorite) badges.Add(("star", UiStyle.Accent));
            if (!island.CanEnter) badges.Add((island.Access is "full" ? "people" : "lock", Error));
            var start = rect.Position + new Vector2(rect.Size.X / 2f - badges.Count * 11f + 11f, -12f); for (var i = 0; i < badges.Count; i++) Badge(start + new Vector2(i * 22f, 0), badges[i].Icon, badges[i].Color, 18);
        }
        if (Hub.Settings.ShowNames && rect.Size.X >= 90)
        {
            var width = Mathf.Min(rect.Size.X - 8, font.GetStringSize(island.Name, HorizontalAlignment.Left, -1, 13).X + 16); var chip = new Rect2(rect.Position + new Vector2((rect.Size.X - width) / 2f, rect.Size.Y - 24), new Vector2(width, 20));
            DrawRect(chip, new Color(UiStyle.PanelFillSolid, .82f)); DrawString(font, chip.Position + new Vector2(0, 14), island.Name, HorizontalAlignment.Center, width, 13, UiStyle.Text);
        }
        if (rect.Size.X >= 90 && island.Online) DrawCircle(rect.Position + new Vector2(rect.Size.X - 10, 10), 4.5f, Online);
    }

    private void DrawYou()
    {
        if (Hub?.PlayerGlobal() is not { } player) return; var at = ToScreen(player.X, player.Y); if (!new Rect2(Vector2.Zero, Size).Grow(40).HasPoint(at)) return;
        var pulse = (float)(Time.GetTicksMsec() % 1600) / 1600f; DrawArc(at, 8 + pulse * 16, 0, Mathf.Tau, 28, new Color(UiStyle.Accent, (1 - pulse) * .6f), 2f, true);
        DrawCircle(at, 6, UiStyle.Accent); DrawArc(at, 6, 0, Mathf.Tau, 18, Colors.White, 1.6f, true);
        var pin = Icon("pin", UiStyle.Accent, 30); DrawTexture(pin, at + new Vector2(-15, -40));
        DrawString(UiStyle.Bold, at + new Vector2(-30, -46), "Você", HorizontalAlignment.Center, 60, 13, UiStyle.Accent);
    }

    private void DrawDestination()
    {
        if (Hub?.Destination is not { } destination) return; var at = ToScreen(destination.GlobalX, destination.GlobalY); if (!new Rect2(Vector2.Zero, Size).Grow(40).HasPoint(at)) return;
        DrawCircle(at, 5, UiStyle.Orange); DrawArc(at, 9, 0, Mathf.Tau, 20, UiStyle.Orange, 2f, true); var pin = Icon("pin", UiStyle.Orange, 28); DrawTexture(pin, at + new Vector2(-14, -38));
    }

    private void DrawCompass()
    {
        foreach (var (letter, at) in new[] { ("N", new Vector2(Size.X / 2f, 16)), ("S", new Vector2(Size.X / 2f, Size.Y - 16)), ("O", new Vector2(16, Size.Y / 2f)), ("L", new Vector2(Size.X - 16, Size.Y / 2f)) })
        {
            DrawCircle(at, 13, new Color(UiStyle.PanelFillSolid, .94f)); DrawArc(at, 13, 0, Mathf.Tau, 24, new Color(UiStyle.BorderBright, .8f), 1.5f, true); DrawString(UiStyle.Bold, at + new Vector2(-10, 5), letter, HorizontalAlignment.Center, 20, 14, UiStyle.Text);
        }
    }

    // ---- tests ---------------------------------------------------------------------------------------------------------------------------

    public bool HasPictureForTesting(Guid island) => _pictures.ContainsKey(island);
    public int MarkerCountForTesting { get { Layout(); return _drawn.Count(d => d.Rect.Size.X >= 40); } }
    public void ClickForTesting(Vector2 at, bool doubleClick = false)
    {
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at }); _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, DoubleClick = doubleClick });
    }
    public void DragForTesting(Vector2 from, Vector2 to)
    {
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from }); _GuiInput(new InputEventMouseMotion { Position = to, Relative = to - from }); _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to });
    }
    public void WheelForTesting(Vector2 at, bool up) => _GuiInput(new InputEventMouseButton { ButtonIndex = up ? MouseButton.WheelUp : MouseButton.WheelDown, Pressed = true, Position = at });
    public void RightClickForTesting(Vector2 at) => _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
    public void KeyForTesting(Key key) => _GuiInput(new InputEventKey { Keycode = key, Pressed = true });
}
