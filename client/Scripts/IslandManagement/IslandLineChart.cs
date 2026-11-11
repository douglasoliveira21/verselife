using System.Globalization;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>A small line chart of one measure over time: a grid, the lowest and highest value on the left, the time on the bottom and the last value marked. It draws only the points it is given.</summary>
public partial class IslandLineChart : Control
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private IReadOnlyList<(DateTimeOffset At, double Value)> _points = [];
    private Color _color = IslandUi.AccentPrimary;
    private string _unit = "";
    private double? _floor;

    public int PointCount => _points.Count;
    public double LastValue => _points.Count == 0 ? 0 : _points[^1].Value;

    public IslandLineChart() { CustomMinimumSize = new Vector2(0, 120); SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; MouseFilter = MouseFilterEnum.Ignore; }

    /// <summary>Gives the chart its points. <paramref name="floor"/> keeps the bottom of the scale at a value (0 for things that cannot be negative).</summary>
    public void SetData(IReadOnlyList<(DateTimeOffset At, double Value)> points, string unit, Color color, double? floor = 0) { _points = points; _unit = unit; _color = color; _floor = floor; QueueRedraw(); }

    private static string Format(double value, string unit) => (Math.Abs(value) >= 100 ? value.ToString("0", Pt) : value.ToString("0.#", Pt)) + (unit.Length > 0 ? " " + unit : "");

    public override void _Draw()
    {
        var font = UiStyle.Medium; const int size = 11;
        var left = 46f; var bottom = 20f; var plot = new Rect2(left, 6, Math.Max(10, Size.X - left - 8), Math.Max(10, Size.Y - bottom - 6));
        for (var i = 0; i <= 3; i++) { var y = plot.Position.Y + plot.Size.Y * i / 3f; DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), new Color(IslandUi.BorderDefault, .45f), 1); }
        if (_points.Count == 0) { DrawString(font, new Vector2(plot.Position.X + 10, plot.Position.Y + plot.Size.Y / 2), "Sem dados neste período", HorizontalAlignment.Left, -1, 13, IslandUi.TextMuted); return; }
        var low = _floor ?? _points.Min(p => p.Value); var high = _points.Max(p => p.Value); if (high - low < 1e-6) high = low + 1;
        var span = high - low; high += span * .08;
        var first = _points[0].At; var last = _points[^1].At; var total = Math.Max(1, (last - first).TotalSeconds);
        Vector2 At(int i) => new(plot.Position.X + (_points.Count == 1 ? plot.Size.X : (float)((_points[i].At - first).TotalSeconds / total) * plot.Size.X), plot.End.Y - (float)((_points[i].Value - low) / (high - low)) * plot.Size.Y);
        var line = new Vector2[_points.Count]; for (var i = 0; i < _points.Count; i++) line[i] = At(i);
        if (line.Length > 1)
        {
            var area = new List<Vector2>(line) { new(line[^1].X, plot.End.Y), new(line[0].X, plot.End.Y) };
            DrawColoredPolygon([.. area], new Color(_color, .12f)); DrawPolyline(line, _color, 2f, true);
        }
        DrawCircle(line[^1], 3.5f, _color);
        DrawString(font, new Vector2(2, plot.Position.Y + 9), Format(high, ""), HorizontalAlignment.Left, left - 6, size, IslandUi.TextMuted);
        DrawString(font, new Vector2(2, plot.End.Y), Format(low, ""), HorizontalAlignment.Left, left - 6, size, IslandUi.TextMuted);
        DrawString(font, new Vector2(plot.Position.X, Size.Y - 4), first.ToLocalTime().ToString("HH:mm", Pt), HorizontalAlignment.Left, -1, size, IslandUi.TextMuted);
        DrawString(font, new Vector2(plot.End.X - 40, Size.Y - 4), last.ToLocalTime().ToString("HH:mm", Pt), HorizontalAlignment.Right, 40, size, IslandUi.TextMuted);
    }
}
