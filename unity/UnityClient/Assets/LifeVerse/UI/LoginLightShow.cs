using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The moving light over the picture of the login screen, as the Godot client's <c>LoginLightShow</c>: beams that sweep slowly like stage lights (cyan, violet, pink and gold) and soft dots of light
    /// drifting upwards. It is only decoration: it ignores the mouse, draws a handful of triangles and circles per frame and stands still when the person asked for less motion.
    /// </summary>
    public sealed class LoginLightShow : VisualElement
    {
        private readonly struct Beam
        {
            public readonly Vector2 Origin; public readonly float BaseAngle, Swing, Speed, Phase, Width, Length; public readonly Color Colour;
            public Beam(Vector2 origin, float baseAngle, float swing, float speed, float phase, float width, Color colour, float length) { Origin = origin; BaseAngle = baseAngle; Swing = swing; Speed = speed; Phase = phase; Width = width; Colour = colour; Length = length; }
        }
        private sealed class Mote { public Vector2 Position; public float Speed, Size, Phase; public Color Colour; }

        private readonly List<Beam> _beams = new List<Beam>(); private readonly List<Mote> _motes = new List<Mote>(); private readonly System.Random _random = new System.Random();
        private float _time; private float _last = -1f; private IVisualElementScheduledItem _tick;
        public int BeamCount => _beams.Count;
        public int MoteCount => _motes.Count;
        /// <summary>Still (no motion) when the person asked to reduce motion.</summary>
        public bool Still { get; set; }
        /// <summary>How far the first beam has turned (it changes with time; the checks use it to see the light moving).</summary>
        public float FirstBeamAngle => Angle(_beams[0]);
        public float Time => _time;

        public LoginLightShow()
        {
            name = "light-show"; pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0; style.overflow = Overflow.Hidden;
            var cyan = new Color(.18f, .85f, .82f); var violet = new Color(.545f, .36f, .965f); var pink = new Color(1f, .37f, .66f); var gold = new Color(1f, .77f, .42f);
            // origins are fractions of the window (below and above its edges), angles are measured from straight up (negative to the left)
            _beams.Add(new Beam(new Vector2(.08f, 1.08f), -.10f, .34f, .23f, 0f, .075f, cyan, 1.45f)); _beams.Add(new Beam(new Vector2(.30f, 1.08f), .05f, .30f, .17f, 1.7f, .06f, violet, 1.5f));
            _beams.Add(new Beam(new Vector2(.70f, 1.08f), -.05f, .30f, .20f, 3.1f, .06f, pink, 1.5f)); _beams.Add(new Beam(new Vector2(.93f, 1.08f), .12f, .34f, .26f, 4.6f, .075f, gold, 1.45f));
            _beams.Add(new Beam(new Vector2(.18f, -.08f), Mathf.PI + .12f, .28f, .15f, 2.4f, .05f, violet, 1.2f)); _beams.Add(new Beam(new Vector2(.84f, -.08f), Mathf.PI - .10f, .28f, .19f, 5.4f, .05f, cyan, 1.2f));
            for (var i = 0; i < 46; i++) _motes.Add(NewMote(true));
            generateVisualContent += Draw;
            RegisterCallback<AttachToPanelEvent>(_ => { _last = -1f; _tick ??= schedule.Execute(Step).Every(33); _tick.Resume(); });
            RegisterCallback<DetachFromPanelEvent>(_ => _tick?.Pause());
        }

        private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);

        private Mote NewMote(bool anywhere)
        {
            var palette = new[] { new Color(.18f, .85f, .82f), new Color(.72f, .61f, 1f), new Color(1f, .85f, .63f), Color.white };
            return new Mote { Position = new Vector2(R(0f, 1f), anywhere ? R(0f, 1f) : 1.05f), Speed = R(.012f, .04f), Size = R(1.4f, 4.2f), Phase = R(0f, 6.28f), Colour = palette[_random.Next(palette.Length)] };
        }

        private float Angle(Beam beam) => beam.BaseAngle + Mathf.Sin(_time * beam.Speed + beam.Phase) * beam.Swing;

        /// <summary>Moves the light by a time step (seconds).</summary>
        public void Advance(float step)
        {
            if (Still || step <= 0f) return; _time += step;
            for (var i = 0; i < _motes.Count; i++) { var m = _motes[i]; m.Position = new Vector2(m.Position.x + Mathf.Sin(_time * .5f + m.Phase) * .0006f, m.Position.y - m.Speed * step); if (m.Position.y < -.05f) _motes[i] = NewMote(false); }
            MarkDirtyRepaint();
        }

        private void Step() { var now = UnityEngine.Time.unscaledTime; var step = _last < 0f ? 0f : Mathf.Min(.1f, now - _last); _last = now; Advance(step); }

        private void Draw(MeshGenerationContext context)
        {
            var size = contentRect.size; if (size.x < 2f || size.y < 2f) return;
            foreach (var beam in _beams)
            {
                var origin = new Vector2(beam.Origin.x * size.x, beam.Origin.y * size.y); var angle = Angle(beam); var length = beam.Length * size.y; var pulse = .75f + .25f * Mathf.Sin(_time * .9f + beam.Phase * 1.3f);
                // three layers from wide and faint to narrow and bright give the beam a soft edge
                for (var layer = 0; layer < 3; layer++)
                {
                    var spread = beam.Width * (1.9f - layer * .6f); var strength = (.07f + layer * .05f) * pulse;
                    var left = origin + Up(angle - spread) * length; var right = origin + Up(angle + spread) * length;
                    Triangle(context, origin, left, right, new Color(beam.Colour.r, beam.Colour.g, beam.Colour.b, Mathf.Clamp01(strength * .75f)), new Color(beam.Colour.r, beam.Colour.g, beam.Colour.b, 0f));
                }
                Circle(context.painter2D, origin, size.y * .045f, new Color(beam.Colour.r, beam.Colour.g, beam.Colour.b, .10f * pulse));
            }
            foreach (var mote in _motes)
            {
                var twinkle = .35f + .65f * Mathf.Abs(Mathf.Sin(_time * 1.2f + mote.Phase)); var at = new Vector2(mote.Position.x * size.x, mote.Position.y * size.y);
                Circle(context.painter2D, at, mote.Size * 2.6f, new Color(mote.Colour.r, mote.Colour.g, mote.Colour.b, .05f * twinkle)); Circle(context.painter2D, at, mote.Size, new Color(mote.Colour.r, mote.Colour.g, mote.Colour.b, .55f * twinkle));
            }
        }

        /// <summary>Up turned by an angle (UI y grows downwards).</summary>
        private static Vector2 Up(float angle) => new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));

        private static void Triangle(MeshGenerationContext context, Vector2 a, Vector2 b, Vector2 c, Color near, Color far)
        {
            var mesh = context.Allocate(3, 3);
            mesh.SetNextVertex(new Vertex { position = new Vector3(a.x, a.y, Vertex.nearZ), tint = near });
            mesh.SetNextVertex(new Vertex { position = new Vector3(b.x, b.y, Vertex.nearZ), tint = far });
            mesh.SetNextVertex(new Vertex { position = new Vector3(c.x, c.y, Vertex.nearZ), tint = far });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
        }

        private static void Circle(Painter2D painter, Vector2 at, float radius, Color colour)
        {
            painter.fillColor = colour; painter.BeginPath(); painter.Arc(at, radius, 0f, 360f); painter.ClosePath(); painter.Fill();
        }
    }
}
