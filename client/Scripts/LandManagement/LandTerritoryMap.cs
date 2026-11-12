using Godot;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.LandManagement;

/// <summary>
/// The territory map: the picture of the island with every real parcel laid over it (drawn from the cells the server counted), coloured by what it is. Mouse wheel zooms, dragging with the right or middle button moves it,
/// a click picks a parcel (Ctrl or Shift adds more) and, in the marking tools, dragging a rectangle adds cells to or takes them off the cell selection.
/// </summary>
public partial class LandTerritoryMap : Control
{
    public enum Tool { Pick, Add, Remove }

    public static readonly Color Available = new("#20E36B"), OnSale = new("#F4A340"), Rented = new("#B47AE8"), Sold = new("#8A97A0"), Common = new("#398BE5");

    private LandOverviewData? _data;
    private Texture2D? _cover;
    private readonly Dictionary<long, int> _cellOwner = [];
    private readonly List<Vector2[]> _edges = [];
    private readonly List<Vector2> _centres = [];
    private float _zoom = 1f; private Vector2 _pan;
    private bool _panning, _pressed, _moved; private Vector2 _pressAt, _lastAt; private (int, int)? _ruleStart, _ruleNow; private int _hover = -1;

    public Tool CurrentTool { get; set; } = Tool.Pick;
    public bool ShowGrid { get; set; }
    public bool ShowLabels { get; set; } = true;
    public bool ShowCover { get; set; } = true;
    public Guid? Selected { get; private set; }
    public HashSet<Guid> Picked { get; } = [];
    public HashSet<long> SelectedCells { get; private set; } = [];

    /// <summary>A parcel was clicked (null: empty ground).</summary>
    public event Action<Guid?>? ParcelClicked;
    public event Action? CellSelectionChanged;
    public event Action<int, int>? CellClicked;

    public float ZoomForTesting => _zoom;
    public int ParcelCountForTesting => _data?.Parcels.Count ?? 0;
    public int HoverForTesting => _hover;

    public LandTerritoryMap() { ClipContents = true; FocusMode = FocusModeEnum.Click; MouseFilter = MouseFilterEnum.Stop; CustomMinimumSize = new Vector2(320, 260); }

    public void SetData(LandOverviewData data)
    {
        _data = data; _cellOwner.Clear(); _edges.Clear(); _centres.Clear();
        for (var i = 0; i < data.Parcels.Count; i++) foreach (var cell in data.Parcels[i].Cells) _cellOwner[cell] = i;
        for (var i = 0; i < data.Parcels.Count; i++)
        {
            var lines = new List<Vector2>(); double sx = 0, sz = 0; var count = 0;
            foreach (var cell in data.Parcels[i].Cells)
            {
                var (cx, cz) = LandMath.Unkey(cell); sx += cx + .5; sz += cz + .5; count++;
                if (!Same(cx, cz - 1, i)) { lines.Add(new Vector2(cx, cz)); lines.Add(new Vector2(cx + 1, cz)); }
                if (!Same(cx, cz + 1, i)) { lines.Add(new Vector2(cx, cz + 1)); lines.Add(new Vector2(cx + 1, cz + 1)); }
                if (!Same(cx - 1, cz, i)) { lines.Add(new Vector2(cx, cz)); lines.Add(new Vector2(cx, cz + 1)); }
                if (!Same(cx + 1, cz, i)) { lines.Add(new Vector2(cx + 1, cz)); lines.Add(new Vector2(cx + 1, cz + 1)); }
            }
            _edges.Add(lines.ToArray()); _centres.Add(count == 0 ? Vector2.Zero : new Vector2((float)(sx / count), (float)(sz / count)));
        }
        if (Selected is { } keep && data.Parcels.All(p => p.Id != keep)) Selected = null;
        Picked.RemoveWhere(id => data.Parcels.All(p => p.Id != id));
        QueueRedraw();
    }

    private bool Same(int cx, int cz, int parcel) => _cellOwner.TryGetValue(LandMath.Key(cx, cz), out var other) && other == parcel;

    public void SetCover(Texture2D? texture) { _cover = texture; QueueRedraw(); }
    public void Select(Guid? id) { Selected = id; if (id is { } one) { Picked.Clear(); Picked.Add(one); } else Picked.Clear(); QueueRedraw(); }
    public void SetCells(HashSet<long> cells) { SelectedCells = cells; CellSelectionChanged?.Invoke(); QueueRedraw(); }
    public void ClearCells() => SetCells([]);

    // ---- view ------------------------------------------------------------------------------------------------------------

    private float Side => Math.Max(1f, Math.Min(Size.X, Size.Y) - 24f);
    private float Half => _data is null ? 32f : _data.Size / (2f * LandMath.CellSize);
    private float CellPixels => _data is null ? 1f : Side * _zoom / (2f * Half);
    private Vector2 Origin => Size / 2 + _pan - new Vector2(Side * _zoom / 2, Side * _zoom / 2);
    private Vector2 CellToScreen(float cx, float cz) => Origin + new Vector2(cx + Half, cz + Half) * CellPixels;
    private (int, int) CellAt(Vector2 screen) { var c = (screen - Origin) / CellPixels; return ((int)Mathf.Floor(c.X - Half), (int)Mathf.Floor(c.Y - Half)); }

    public void Fit() { _zoom = 1f; _pan = Vector2.Zero; QueueRedraw(); }
    public void ZoomBy(float factor, Vector2? around = null)
    {
        var before = _zoom; var next = Mathf.Clamp(_zoom * factor, .6f, 14f); if (Mathf.IsEqualApprox(before, next)) return;
        var anchor = around ?? Size / 2; var oldOrigin = Origin; _zoom = next;
        var wanted = anchor - (anchor - oldOrigin) * (next / before); _pan = wanted - Size / 2 + new Vector2(Side * _zoom / 2, Side * _zoom / 2); QueueRedraw();
    }

    public void FocusParcel(Guid id)
    {
        if (_data is null) return; var index = _data.Parcels.ToList().FindIndex(p => p.Id == id); if (index < 0) return;
        _zoom = Mathf.Clamp(Math.Max(_zoom, 2.4f), .6f, 14f); _pan = new Vector2(Side * _zoom / 2, Side * _zoom / 2) - new Vector2(_centres[index].X + Half, _centres[index].Y + Half) * CellPixels; QueueRedraw();
    }

    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }

    // ---- drawing ---------------------------------------------------------------------------------------------------------

    private static Color ColorOf(string kind) => kind switch { "available" => Available, "sale" => OnSale, "rented" => Rented, "sold" => Sold, "common" => Common, _ => Sold };

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("#06161E"));
        if (_data is null) return;
        var side = Side * _zoom;
        var island = new Rect2(Origin, new Vector2(side, side));
        DrawRect(island, new Color("#0A2A36"));
        if (ShowCover && _cover is not null) DrawTextureRect(_cover, island, false, new Color(1, 1, 1, .85f));
        DrawRect(island, new Color(IslandUi.BorderDefault, .9f), false, 1.5f);
        var scale = CellPixels;
        if (ShowGrid && scale >= 6) for (var i = 0; i <= _data.Size / LandMath.CellSize; i++) { var d = i * scale; DrawLine(Origin + new Vector2(d, 0), Origin + new Vector2(d, side), new Color(1, 1, 1, .07f)); DrawLine(Origin + new Vector2(0, d), Origin + new Vector2(side, d), new Color(1, 1, 1, .07f)); }
        var half = Half;
        Vector2 P(float cx, float cz) => Origin + new Vector2((cx + half) * scale, (cz + half) * scale);

        for (var i = 0; i < _data.Parcels.Count; i++)
        {
            var parcel = _data.Parcels[i]; var color = ColorOf(parcel.Kind); var picked = Picked.Contains(parcel.Id) || Selected == parcel.Id;
            foreach (var run in parcel.Runs) DrawRect(new Rect2(P(run[1], run[0]), new Vector2((run[2] - run[1] + 1) * scale, scale)), new Color(color, picked ? .62f : .38f));
            var edge = _edges[i]; var shifted = new Vector2[edge.Length]; for (var k = 0; k < edge.Length; k++) shifted[k] = P(edge[k].X, edge[k].Y);
            if (shifted.Length >= 2) DrawMultiline(shifted, picked ? IslandUi.AccentPrimary : new Color(color, .95f), picked ? 3f : 1.6f);
        }
        if (SelectedCells.Count > 0) foreach (var cell in SelectedCells) { var (cx, cz) = LandMath.Unkey(cell); DrawRect(new Rect2(P(cx, cz), new Vector2(scale, scale)), new Color(IslandUi.AccentPrimary, .5f)); }
        if (_ruleStart is { } a && _ruleNow is { } b)
        {
            var (x0, x1) = (Math.Min(a.Item1, b.Item1), Math.Max(a.Item1, b.Item1)); var (z0, z1) = (Math.Min(a.Item2, b.Item2), Math.Max(a.Item2, b.Item2));
            var rect = new Rect2(P(x0, z0), new Vector2((x1 - x0 + 1) * scale, (z1 - z0 + 1) * scale)); DrawRect(rect, new Color(CurrentTool == Tool.Remove ? IslandUi.Error : IslandUi.AccentPrimary, .22f)); DrawRect(rect, CurrentTool == Tool.Remove ? IslandUi.Error : IslandUi.AccentPrimary, false, 2f);
        }
        if (ShowLabels)
            for (var i = 0; i < _data.Parcels.Count; i++)
            {
                var parcel = _data.Parcels[i]; var centre = P(_centres[i].X, _centres[i].Y);
                var width = Math.Sqrt(parcel.CellCount) * scale; if (width < 46) continue;
                var font = UiStyle.Bold; var size = (int)Mathf.Clamp(width / 6f, 11f, 22f);
                var code = parcel.Code; var textSize = font.GetStringSize(code, HorizontalAlignment.Left, -1, size);
                DrawString(font, centre - new Vector2(textSize.X / 2, 0), code, HorizontalAlignment.Left, -1, size, IslandUi.TextPrimary);
                if (width >= 84) { var small = UiStyle.Medium; var line = $"{parcel.CategoryText}"; var sz = small.GetStringSize(line, HorizontalAlignment.Left, -1, size - 4); DrawString(small, centre + new Vector2(-sz.X / 2, size), line, HorizontalAlignment.Left, -1, size - 4, new Color(IslandUi.TextPrimary, .9f)); var area = LandMath.Area(parcel.Area); var sz2 = small.GetStringSize(area, HorizontalAlignment.Left, -1, size - 5); DrawString(small, centre + new Vector2(-sz2.X / 2, size * 2 - 2), area, HorizontalAlignment.Left, -1, size - 5, new Color(IslandUi.TextPrimary, .8f)); }
            }
    }

    // ---- mouse -----------------------------------------------------------------------------------------------------------

    private int ParcelAt(Vector2 screen) { var (cx, cz) = CellAt(screen); return _cellOwner.TryGetValue(LandMath.Key(cx, cz), out var index) ? index : -1; }

    public override void _GuiInput(InputEvent input)
    {
        if (_data is null) return;
        if (input is InputEventMouseButton wheel && wheel.Pressed && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) { ZoomBy(wheel.ButtonIndex == MouseButton.WheelUp ? 1.18f : 1 / 1.18f, wheel.Position); AcceptEvent(); return; }
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex is MouseButton.Right or MouseButton.Middle) { _panning = button.Pressed; _lastAt = button.Position; AcceptEvent(); return; }
            if (button.ButtonIndex != MouseButton.Left) return;
            if (button.Pressed)
            {
                GrabFocus(); _pressed = true; _moved = false; _pressAt = _lastAt = button.Position;
                if (CurrentTool != Tool.Pick) { _ruleStart = _ruleNow = CellAt(button.Position); QueueRedraw(); }
            }
            else if (_pressed)
            {
                _pressed = false;
                if (CurrentTool != Tool.Pick && _ruleStart is { } a && _ruleNow is { } b) { ApplyRectangle(a, b); _ruleStart = _ruleNow = null; if (!_moved) CellClicked?.Invoke(a.Item1, a.Item2); }
                else if (!_moved) Click(button.Position, button.CtrlPressed || button.ShiftPressed);
                QueueRedraw();
            }
            AcceptEvent(); return;
        }
        if (input is InputEventMouseMotion motion)
        {
            if (_panning || (_pressed && CurrentTool == Tool.Pick && (motion.Position - _pressAt).Length() > 5)) { _pan += motion.Position - _lastAt; _lastAt = motion.Position; _moved = true; QueueRedraw(); return; }
            if (_pressed && CurrentTool != Tool.Pick) { _ruleNow = CellAt(motion.Position); if ((motion.Position - _pressAt).Length() > 4) _moved = true; QueueRedraw(); return; }
            var hover = ParcelAt(motion.Position);
            if (hover != _hover) { _hover = hover; TooltipText = hover < 0 ? "" : Tip(_data.Parcels[hover]); QueueRedraw(); }
        }
    }

    private static string Tip(LandParcelRow p) => $"{p.Code} · {p.Name}\n{p.CategoryText} · {LandMath.Area(p.Area)}\n{p.KindText}" + (p.Kind is "sold" or "rented" ? $"\nDono: {p.Owner}" : "") + (p.SalePrice is { } s ? $"\nPreço: {LandMath.Money(s)}" : "") + (p.Kind == "rented" ? $"\nLocatário: {p.Tenant}" : "");

    private void Click(Vector2 at, bool add)
    {
        var index = ParcelAt(at);
        if (index < 0) { if (!add) { Selected = null; Picked.Clear(); } ParcelClicked?.Invoke(null); return; }
        var id = _data!.Parcels[index].Id;
        if (add) { if (!Picked.Remove(id)) Picked.Add(id); Selected = Picked.Contains(id) ? id : Picked.FirstOrDefault(); if (Picked.Count == 0) Selected = null; }
        else { Selected = id; Picked.Clear(); Picked.Add(id); }
        ParcelClicked?.Invoke(Selected);
    }

    private void ApplyRectangle((int, int) a, (int, int) b)
    {
        var (x0, x1) = (Math.Min(a.Item1, b.Item1), Math.Max(a.Item1, b.Item1)); var (z0, z1) = (Math.Min(a.Item2, b.Item2), Math.Max(a.Item2, b.Item2));
        var half = _data!.Size / (2 * LandMath.CellSize); var next = new HashSet<long>(SelectedCells);
        for (var cx = Math.Max(x0, -half); cx <= Math.Min(x1, half - 1); cx++)
            for (var cz = Math.Max(z0, -half); cz <= Math.Min(z1, half - 1); cz++)
            {
                var key = LandMath.Key(cx, cz); if (CurrentTool == Tool.Add) next.Add(key); else next.Remove(key);
            }
        SelectedCells = next; CellSelectionChanged?.Invoke();
    }

    /// <summary>For tests: what a click on this cell would do.</summary>
    public void ClickCellForTesting(int cx, int cz, bool add = false) => Click(CellToScreen(cx + .5f, cz + .5f), add);

    public void MarkRectangleForTesting((int, int) a, (int, int) b, Tool tool) { CurrentTool = tool; ApplyRectangle(a, b); QueueRedraw(); }
}
