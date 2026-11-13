using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>
/// Moving light behind the interface, over the wallpaper: spotlight beams that sweep slowly like stage cannons (cyan, violet, pink and warm gold) and soft dots of light drifting upwards.
/// Everything is drawn additively and is only decoration: it ignores the mouse and costs a handful of polygons per frame.
/// </summary>
public partial class LoginLightShow : Control
{
    private sealed record Beam(Vector2 Origin, float BaseAngle, float Swing, float Speed, float Phase, float Width, Color Colour, float Length);
    private sealed class Mote { public Vector2 Position; public float Speed, Size, Phase; public Color Colour; }

    private readonly List<Beam> _beams = [];
    private readonly List<Mote> _motes = [];
    private readonly RandomNumberGenerator _random = new();
    private double _time;

    public int BeamCount => _beams.Count;
    public int MoteCount => _motes.Count;
    /// <summary>How far the first beam has turned: it changes with time (used by the test to see the light moving).</summary>
    public float FirstBeamAngle => Angle(_beams[0]);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore; ClipContents = true;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        _random.Randomize();
        // origins are fractions of the window (below and above its edges), angles are measured from straight up (negative to the left)
        var cyan = UiStyle.Accent; var violet = new Color("#8B5CF6"); var pink = new Color("#FF5FA8"); var gold = new Color("#FFC46B");
        _beams.AddRange([
            new Beam(new Vector2(.08f, 1.08f), -.10f, .34f, .23f, 0f, .075f, cyan, 1.45f),
            new Beam(new Vector2(.30f, 1.08f), .05f, .30f, .17f, 1.7f, .06f, violet, 1.5f),
            new Beam(new Vector2(.70f, 1.08f), -.05f, .30f, .20f, 3.1f, .06f, pink, 1.5f),
            new Beam(new Vector2(.93f, 1.08f), .12f, .34f, .26f, 4.6f, .075f, gold, 1.45f),
            new Beam(new Vector2(.18f, -.08f), 3.14159f + .12f, .28f, .15f, 2.4f, .05f, violet, 1.2f),
            new Beam(new Vector2(.84f, -.08f), 3.14159f - .10f, .28f, .19f, 5.4f, .05f, cyan, 1.2f)
        ]);
        for (var i = 0; i < 46; i++) _motes.Add(NewMote(true));
    }

    private Mote NewMote(bool anywhere)
    {
        var palette = new[] { UiStyle.Accent, new Color("#B79CFF"), new Color("#FFD9A0"), Colors.White };
        return new Mote { Position = new Vector2(_random.RandfRange(0, 1), anywhere ? _random.RandfRange(0, 1) : 1.05f), Speed = _random.RandfRange(.012f, .04f), Size = _random.RandfRange(1.4f, 4.2f), Phase = _random.RandfRange(0, 6.28f), Colour = palette[_random.RandiRange(0, palette.Length - 1)] };
    }

    private float Angle(Beam beam) => beam.BaseAngle + Mathf.Sin((float)_time * beam.Speed + beam.Phase) * beam.Swing;

    public override void _Process(double delta)
    {
        _time += delta; var step = (float)delta;
        for (var i = 0; i < _motes.Count; i++) { var mote = _motes[i]; mote.Position = new Vector2(mote.Position.X + Mathf.Sin((float)_time * .5f + mote.Phase) * .0006f, mote.Position.Y - mote.Speed * step); if (mote.Position.Y < -.05f) _motes[i] = NewMote(false); }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size; if (size.X < 2 || size.Y < 2) return;
        foreach (var beam in _beams)
        {
            var origin = new Vector2(beam.Origin.X * size.X, beam.Origin.Y * size.Y); var angle = Angle(beam); var length = beam.Length * size.Y;
            var pulse = .75f + .25f * Mathf.Sin((float)_time * .9f + beam.Phase * 1.3f);
            // three layers from wide and faint to narrow and bright give the beam a soft edge
            for (var layer = 0; layer < 3; layer++)
            {
                var spread = beam.Width * (1.9f - layer * .6f); var strength = (.07f + layer * .05f) * pulse;
                var left = origin + Vector2.Up.Rotated(angle - spread) * length; var right = origin + Vector2.Up.Rotated(angle + spread) * length;
                var near = new Color(beam.Colour, strength * 1.6f); var far = new Color(beam.Colour, 0f);
                DrawPolygon([origin, left, right], [near, far, far]);
            }
            // the bright spot where the beam leaves the cannon
            DrawCircle(origin, size.Y * .045f, new Color(beam.Colour, .10f * pulse));
        }
        foreach (var mote in _motes)
        {
            var twinkle = .35f + .65f * Mathf.Abs(Mathf.Sin((float)_time * 1.2f + mote.Phase)); var position = new Vector2(mote.Position.X * size.X, mote.Position.Y * size.Y);
            DrawCircle(position, mote.Size * 2.6f, new Color(mote.Colour, .05f * twinkle)); DrawCircle(position, mote.Size, new Color(mote.Colour, .55f * twinkle));
        }
    }
}
