using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.BuildEditor
{
    /// <summary>
    /// The handles drawn over the scene, as the game draws its gizmo today (2D lines over the picture, placed by projecting points of the world). Axis colours and meaning follow the
    /// viewer: X red, Y green, Z blue and Z is the height. These are drawings: dragging them does nothing yet.
    /// </summary>
    public sealed class Gizmos : VisualElement
    {
        private static readonly Color Red = new Color(.93f, .27f, .22f), Green = new Color(.22f, .78f, .32f), Blue = new Color(.22f, .52f, .98f);
        private static readonly Color Chosen = new Color(1f, .93f, .70f), Sea = new Color(.20f, .85f, .76f), Sun = new Color(.96f, .67f, .16f), Dark = new Color(.12f, .08f, .06f, .55f);
        // the axes as the person sees them, in game space: X east, Y north (game −z), Z up (game y)
        private static readonly Vector3 AxisX = Vector3.right, AxisY = Vector3.back, AxisZ = Vector3.up;

        private readonly Scenario _s; private readonly Projector _p;

        public Gizmos(Scenario scenario, Projector projector)
        {
            _s = scenario; _p = projector; pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0; generateVisualContent += Draw;
            Tags();
        }

        private Thing Subjected => _s.Selected == Subject.Ring ? Backdrop.Ring : _s.Selected == Subject.Table ? Backdrop.Table[4] : Backdrop.Crate;

        /// <summary>The words on the drawing: axis letters, the angle, the size, what each outline means.</summary>
        private void Tags()
        {
            void Tag(Vector3 at, string text, Color colour, float dx = -9f, float dy = -9f)
            {
                var point = _p.At(at); var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.style.position = Position.Absolute; label.style.left = point.x + dx; label.style.top = point.y + dy; label.style.fontSize = 13; label.style.color = Color.white;
                label.style.backgroundColor = colour; label.style.paddingLeft = label.style.paddingRight = 5; label.style.borderTopLeftRadius = label.style.borderTopRightRadius = label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 8; label.AddToClassList("lv-bold"); Add(label);
            }
            var t = Subjected;
            switch (_s.Marks)
            {
                case Marks.Move: case Marks.Linked: case Marks.Grid:
                    Tag(t.Centre + AxisX * 2.0f, "X", Red); Tag(t.Centre + AxisY * 2.0f, "Y", Green); Tag(t.Centre + AxisZ * 2.0f, "Z", Blue);
                    if (_s.Marks == Marks.Linked) { Tag(Backdrop.Table[0].Centre + Vector3.up * .5f, "raiz", new Color(.68f, .45f, .05f), -18f, -22f); Tag(t.Centre - Vector3.up * .75f, "parte escolhida", new Color(.08f, .42f, .39f), -52f, 4f); }
                    break;
                case Marks.Rotate: Tag(t.Centre + Vector3.up * 1.75f, "Z 28°", Blue, -24f, -22f); break;
                case Marks.Scale: Tag(t.Centre + Vector3.up * 1.35f, "1,500 × 1,500 × 1,500 m", new Color(.18f, .14f, .12f, .85f), -82f, -26f); break;
                case Marks.Face: Tag(t.Centre + Vector3.up * .78f, "face 0", new Color(.08f, .42f, .39f), -22f, -10f); break;
                case Marks.Ghost: Tag(new Vector3(-1.4f, Backdrop.Ground + 1.9f, -1.6f), "Caixa · clique para criar", new Color(.18f, .14f, .12f, .85f), -76f, -22f); break;
                case Marks.Terrain: Tag(new Vector3(1f, 2.4f, -2.5f), "Elevar · pincel 3 m", new Color(.18f, .14f, .12f, .85f), -60f, -10f); break;
                case Marks.Multi: Tag(Backdrop.Crate.Centre + Vector3.up * 1.7f, "3 objetos", new Color(.08f, .42f, .39f), -34f, -20f); break;
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D; p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
            void Path(IReadOnlyList<Vector3> points, Color colour, float width, bool closed = false)
            {
                for (var pass = 0; pass < 2; pass++)   // a dark halo under each line keeps it readable on any ground
                {
                    p.strokeColor = pass == 0 ? Dark : colour; p.lineWidth = pass == 0 ? width + 2.5f : width; p.BeginPath(); p.MoveTo(_p.At(points[0])); for (var i = 1; i < points.Count; i++) p.LineTo(_p.At(points[i])); if (closed) p.ClosePath(); p.Stroke();
                }
            }
            void Fill(IReadOnlyList<Vector3> points, Color colour) { p.fillColor = colour; p.BeginPath(); p.MoveTo(_p.At(points[0])); for (var i = 1; i < points.Count; i++) p.LineTo(_p.At(points[i])); p.ClosePath(); p.Fill(); }
            void Dot(Vector3 at, float radius, Color colour) { var c = _p.At(at); p.fillColor = Dark; p.BeginPath(); p.Arc(c, radius + 1.5f, 0f, 360f); p.Fill(); p.fillColor = colour; p.BeginPath(); p.Arc(c, radius, 0f, 360f); p.Fill(); }
            void Square(Vector3 at, float half, Color colour) { var c = _p.At(at); void One(float h, Color k) { p.fillColor = k; p.BeginPath(); p.MoveTo(c + new Vector2(-h, -h)); p.LineTo(c + new Vector2(h, -h)); p.LineTo(c + new Vector2(h, h)); p.LineTo(c + new Vector2(-h, h)); p.ClosePath(); p.Fill(); } One(half + 1.5f, Dark); One(half, colour); }
            void Outline(Thing t, Color colour, float width)
            {
                var c = t.Corners(); foreach (var (a, b) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) }) Path(new[] { c[a], c[b] }, colour, width);
            }
            void Arrow(Vector3 from, Vector3 axis, Color colour)
            {
                var tip = from + axis * 1.75f; Path(new[] { from, tip }, colour, 4f); var a = _p.At(from); var b = _p.At(tip); var d = (b - a).normalized; var n = new Vector2(-d.y, d.x);
                p.fillColor = colour; p.BeginPath(); p.MoveTo(b + d * 15f); p.LineTo(b + n * 7f); p.LineTo(b - n * 7f); p.ClosePath(); p.Fill();
            }
            void Handles(Vector3 centre)
            {
                // the planes between each pair of axes, then the arrows
                foreach (var (u, v, colour) in new[] { (AxisX, AxisY, Blue), (AxisX, AxisZ, Green), (AxisY, AxisZ, Red) })
                {
                    var quad = new[] { centre + u * .45f + v * .45f, centre + u * .85f + v * .45f, centre + u * .85f + v * .85f, centre + u * .45f + v * .85f }; Fill(quad, new Color(colour.r, colour.g, colour.b, .35f)); Path(quad, colour, 1.5f, true);
                }
                Arrow(centre, AxisX, Red); Arrow(centre, AxisY, Green); Arrow(centre, AxisZ, Blue); Dot(centre, 4f, Color.white);
            }
            Vector3[] Circle(Vector3 centre, Vector3 u, Vector3 v, float radius) => Enumerable.Range(0, 65).Select(i => centre + (u * Mathf.Cos(i / 64f * Mathf.PI * 2f) + v * Mathf.Sin(i / 64f * Mathf.PI * 2f)) * radius).ToArray();

            var t = Subjected;
            switch (_s.Marks)
            {
                case Marks.Focus:
                    var focus = Backdrop.Crate.Centre; Path(Circle(focus, AxisX, AxisY, 2.2f), new Color(1f, 1f, 1f, .75f), 1.6f); Dot(focus, 5f, Sun); var f = _p.At(focus);
                    foreach (var d in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right }) { p.strokeColor = Sun; p.lineWidth = 2.5f; p.BeginPath(); p.MoveTo(f + d * 12f); p.LineTo(f + d * 26f); p.Stroke(); }
                    break;
                case Marks.Selection: Outline(t, Chosen, 2f); break;
                case Marks.Move: Outline(t, Chosen, 1.6f); Handles(t.Centre); break;
                case Marks.Rotate:
                    Outline(t, Chosen, 1.2f); Path(Circle(t.Centre, AxisY, AxisZ, 1.55f), Red, 4f); Path(Circle(t.Centre, AxisX, AxisZ, 1.55f), Green, 4f); Path(Circle(t.Centre, AxisX, AxisY, 1.55f), Blue, 4f);
                    var toward = (_p.Eye - t.Centre).normalized; var side = Vector3.Cross(toward, Vector3.up).normalized; Path(Circle(t.Centre, side, Vector3.Cross(side, toward), 1.85f), new Color(1f, 1f, 1f, .8f), 1.6f);
                    Path(new[] { t.Centre, t.Centre + AxisX * 1.55f }, new Color(1f, 1f, 1f, .85f), 1.5f); Path(new[] { t.Centre, t.Centre + Quaternion.Euler(0f, 28f, 0f) * AxisX * 1.55f }, Sun, 2.5f);
                    break;
                case Marks.Scale:
                    Outline(t, Color.white, 1.4f); foreach (var corner in t.Corners()) Square(corner, 6f, Color.white);
                    var turn = Quaternion.Euler(0f, t.Yaw, 0f); foreach (var (axis, colour) in new[] { (AxisX, Red), (AxisY, Green), (AxisZ, Blue) }) foreach (var sign in new[] { -1f, 1f }) { var d = turn * axis; var at = t.Centre + d * (sign * Mathf.Abs(Vector3.Dot(axis, t.Size)) * .5f); Square(at, 7f, colour); }
                    break;
                case Marks.Face:
                    var c = t.Corners(); var top = new[] { c[2], c[3], c[7], c[6] }; var front = new[] { c[0], c[1], c[3], c[2] };
                    foreach (var face in _s.Number == 6 ? new[] { top, front } : new[] { top }) { Fill(face, new Color(Sea.r, Sea.g, Sea.b, .38f)); Path(face, Sea, 3f, true); Path(new[] { face[0], face[2] }, Sea, 1.2f); Path(new[] { face[1], face[3] }, Sea, 1.2f); }
                    break;
                case Marks.Ghost:
                    var ghost = new Thing { Centre = new Vector3(-1.4f, Backdrop.Ground + .5f, -1.6f), Size = Vector3.one }; var g = ghost.Corners(); Fill(new[] { g[2], g[3], g[7], g[6] }, new Color(Sea.r, Sea.g, Sea.b, .28f)); Fill(new[] { g[0], g[1], g[3], g[2] }, new Color(Sea.r, Sea.g, Sea.b, .2f)); Outline(ghost, Sea, 2f); Dot(ghost.Centre + Vector3.down * .5f, 5f, Sun);
                    break;
                case Marks.Terrain:
                    var centre = new Vector3(1f, .22f, -2.5f); Path(Circle(centre, AxisX, AxisY, 3f), Sun, 3f); Path(Circle(centre, AxisX, AxisY, 1.5f), new Color(Sun.r, Sun.g, Sun.b, .7f), 1.5f); Arrow(centre, AxisZ, Sun);
                    break;
                case Marks.Grid:
                    for (var i = -8; i <= 8; i++)
                    {
                        var major = i % 2 == 0; var colour = new Color(1f, 1f, 1f, major ? .7f : .35f); var y = Backdrop.Ground + .02f;
                        Path(new[] { new Vector3(t.Centre.x + i * .5f, y, t.Centre.z - 4f), new Vector3(t.Centre.x + i * .5f, y, t.Centre.z + 4f) }, colour, major ? 1.3f : .8f); Path(new[] { new Vector3(t.Centre.x - 4f, y, t.Centre.z + i * .5f), new Vector3(t.Centre.x + 4f, y, t.Centre.z + i * .5f) }, colour, major ? 1.3f : .8f);
                    }
                    Outline(t, Chosen, 1.6f); Handles(t.Centre); break;
                case Marks.Multi: foreach (var one in new[] { Backdrop.Crate, Backdrop.Ball, Backdrop.Column }) Outline(one, Chosen, 1.8f); Handles(Backdrop.Crate.Centre); break;
                case Marks.Linked:
                    Outline(Backdrop.Table[0], Sun, 2.2f); foreach (var part in Backdrop.Table.Skip(1).Take(3)) Outline(part, new Color(Sea.r, Sea.g, Sea.b, .7f), 1.2f); Outline(t, Sea, 3.2f); Handles(t.Centre);
                    break;
            }
        }
    }
}
