using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The pieces of an icon of the Godot client as drawing commands in its 24x24 box: the SVG elements of the Tabler set (path, circle, rect, line) turned into moves, lines and cubic curves
    /// (arcs become curves too), so the interface can draw them sharp at any size and in any colour, as <c>UiStyle.Icon</c> does in Godot by rasterising the SVG.
    /// </summary>
    public static class IconShapes
    {
        public enum Op { Move, Line, Cubic, Close }
        public readonly struct Step { public readonly Op Op; public readonly Vector2 A, B, C; public Step(Op op, Vector2 a = default, Vector2 b = default, Vector2 c = default) { Op = op; A = a; B = b; C = c; } }
        private static readonly Dictionary<string, List<List<Step>>> Cache = new Dictionary<string, List<List<Step>>>();
        private static readonly Regex Element = new Regex(@"<(path|circle|rect|line|ellipse|polyline|polygon)\b([^>]*)/?>", RegexOptions.Compiled);
        private static readonly Regex Attribute = new Regex(@"([a-zA-Z\-]+)\s*=\s*""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex Token = new Regex(@"[MmLlHhVvCcSsQqTtAaZz]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        public static bool Has(string name) => UiIconData.Icons.ContainsKey(name);

        /// <summary>The shapes of an icon (each one a list of steps), or those of "dots" when the name is unknown.</summary>
        public static List<List<Step>> Of(string name)
        {
            if (!UiIconData.Icons.TryGetValue(name ?? "", out var data)) data = UiIconData.Icons["dots"];
            lock (Cache) { if (Cache.TryGetValue(name ?? "", out var known)) return known; var parsed = Parse(data.Inner); Cache[name ?? ""] = parsed; return parsed; }
        }

        public static bool Filled(string name) => UiIconData.Icons.TryGetValue(name ?? "", out var data) && data.Filled;

        public static List<List<Step>> Parse(string svg)
        {
            var shapes = new List<List<Step>>();
            foreach (Match m in Element.Matches(svg ?? ""))
            {
                var a = new Dictionary<string, string>(); foreach (Match at in Attribute.Matches(m.Groups[2].Value)) a[at.Groups[1].Value] = at.Groups[2].Value;
                float F(string key, float fallback = 0f) => a.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
                switch (m.Groups[1].Value)
                {
                    case "path": if (a.TryGetValue("d", out var d)) shapes.AddRange(Path(d)); break;
                    case "circle": shapes.Add(Ellipse(F("cx"), F("cy"), F("r"), F("r"))); break;
                    case "ellipse": shapes.Add(Ellipse(F("cx"), F("cy"), F("rx"), F("ry"))); break;
                    case "line": shapes.Add(new List<Step> { new Step(Op.Move, new Vector2(F("x1"), F("y1"))), new Step(Op.Line, new Vector2(F("x2"), F("y2"))) }); break;
                    case "rect": shapes.Add(Rect(F("x"), F("y"), F("width"), F("height"), F("rx", F("ry")))); break;
                    case "polyline": case "polygon":
                        if (a.TryGetValue("points", out var pts))
                        {
                            var n = Numbers(pts); var shape = new List<Step>(); for (var i = 0; i + 1 < n.Count; i += 2) shape.Add(new Step(i == 0 ? Op.Move : Op.Line, new Vector2(n[i], n[i + 1])));
                            if (m.Groups[1].Value == "polygon") shape.Add(new Step(Op.Close)); shapes.Add(shape);
                        }
                        break;
                }
            }
            return shapes;
        }

        private static List<float> Numbers(string text) { var list = new List<float>(); foreach (Match t in Token.Matches(text)) if (float.TryParse(t.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) list.Add(f); return list; }

        /// <summary>An SVG path ("M9 5a3 3 0 0 1 3 -3h12…"): each "M" starts a new shape.</summary>
        public static List<List<Step>> Path(string d)
        {
            var shapes = new List<List<Step>>(); List<Step> shape = null; var tokens = new List<string>(); foreach (Match t in Token.Matches(d)) tokens.Add(t.Value);
            var at = Vector2.zero; var start = Vector2.zero; var lastControl = Vector2.zero; var lastQuad = Vector2.zero; char command = ' '; char previous = ' '; var i = 0;
            float N() => i < tokens.Count && float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? (i++ < 0 ? 0 : f) : 0f;
            bool NumberNext() => i < tokens.Count && !char.IsLetter(tokens[i][0]);
            void Add(Step s) { if (shape == null) { shape = new List<Step> { new Step(Op.Move, at) }; shapes.Add(shape); } shape.Add(s); }
            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0])) command = tokens[i++][0]; else if (command == ' ') { i++; continue; }
                var rel = char.IsLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                    {
                        var p = new Vector2(N(), N()); at = rel ? at + p : p; start = at; shape = new List<Step> { new Step(Op.Move, at) }; shapes.Add(shape);
                        command = rel ? 'l' : 'L'; break;   // more pairs after a move are lines
                    }
                    case 'L': { var p = new Vector2(N(), N()); at = rel ? at + p : p; Add(new Step(Op.Line, at)); break; }
                    case 'H': { var x = N(); at = new Vector2(rel ? at.x + x : x, at.y); Add(new Step(Op.Line, at)); break; }
                    case 'V': { var y = N(); at = new Vector2(at.x, rel ? at.y + y : y); Add(new Step(Op.Line, at)); break; }
                    case 'C':
                    {
                        var c1 = new Vector2(N(), N()); var c2 = new Vector2(N(), N()); var p = new Vector2(N(), N()); if (rel) { c1 += at; c2 += at; p += at; }
                        Add(new Step(Op.Cubic, c1, c2, p)); lastControl = c2; at = p; break;
                    }
                    case 'S':
                    {
                        var c2 = new Vector2(N(), N()); var p = new Vector2(N(), N()); if (rel) { c2 += at; p += at; }
                        var c1 = "CcSs".IndexOf(previous) >= 0 ? at * 2f - lastControl : at; Add(new Step(Op.Cubic, c1, c2, p)); lastControl = c2; at = p; break;
                    }
                    case 'Q':
                    {
                        var q = new Vector2(N(), N()); var p = new Vector2(N(), N()); if (rel) { q += at; p += at; }
                        Add(new Step(Op.Cubic, at + (q - at) * (2f / 3f), p + (q - p) * (2f / 3f), p)); lastQuad = q; at = p; break;
                    }
                    case 'T':
                    {
                        var p = new Vector2(N(), N()); if (rel) p += at; var q = "QqTt".IndexOf(previous) >= 0 ? at * 2f - lastQuad : at;
                        Add(new Step(Op.Cubic, at + (q - at) * (2f / 3f), p + (q - p) * (2f / 3f), p)); lastQuad = q; at = p; break;
                    }
                    case 'A':
                    {
                        var rx = N(); var ry = N(); var rotation = N(); var large = N() != 0f; var sweep = N() != 0f; var p = new Vector2(N(), N()); if (rel) p += at;
                        foreach (var s in Arc(at, p, rx, ry, rotation, large, sweep)) Add(s); at = p; break;
                    }
                    case 'Z': if (shape != null) shape.Add(new Step(Op.Close)); at = start; break;
                    default: i++; break;
                }
                previous = command;
                if (char.ToUpperInvariant(command) == 'Z' && NumberNext()) command = ' ';
            }
            return shapes;
        }

        /// <summary>An elliptical arc of SVG (endpoint form) as cubic curves, at most a quarter turn each.</summary>
        public static List<Step> Arc(Vector2 from, Vector2 to, float rx, float ry, float rotationDegrees, bool large, bool sweep)
        {
            var steps = new List<Step>(); if ((from - to).sqrMagnitude < 1e-10f) return steps;
            rx = Mathf.Abs(rx); ry = Mathf.Abs(ry); if (rx < 1e-6f || ry < 1e-6f) { steps.Add(new Step(Op.Line, to)); return steps; }
            var phi = rotationDegrees * Mathf.Deg2Rad; var cos = Mathf.Cos(phi); var sin = Mathf.Sin(phi);
            var dx = (from.x - to.x) / 2f; var dy = (from.y - to.y) / 2f; var x1 = cos * dx + sin * dy; var y1 = -sin * dx + cos * dy;
            var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry); if (lambda > 1f) { var k = Mathf.Sqrt(lambda); rx *= k; ry *= k; }
            var num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1; var den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            var coef = (large == sweep ? -1f : 1f) * Mathf.Sqrt(Mathf.Max(0f, num / den)); var cx1 = coef * rx * y1 / ry; var cy1 = -coef * ry * x1 / rx;
            var cx = cos * cx1 - sin * cy1 + (from.x + to.x) / 2f; var cy = sin * cx1 + cos * cy1 + (from.y + to.y) / 2f;
            float Angle(float ux, float uy, float vx, float vy) { var a = Mathf.Atan2(ux * vy - uy * vx, ux * vx + uy * vy); return a; }
            var theta = Angle(1f, 0f, (x1 - cx1) / rx, (y1 - cy1) / ry); var delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
            if (!sweep && delta > 0f) delta -= 2f * Mathf.PI; else if (sweep && delta < 0f) delta += 2f * Mathf.PI;
            var parts = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(delta) / (Mathf.PI / 2f) - 1e-4f)); var piece = delta / parts; var t = 4f / 3f * Mathf.Tan(piece / 4f);
            Vector2 Point(float angle) { var ex = rx * Mathf.Cos(angle); var ey = ry * Mathf.Sin(angle); return new Vector2(cos * ex - sin * ey + cx, sin * ex + cos * ey + cy); }
            Vector2 Tangent(float angle) { var ex = -rx * Mathf.Sin(angle); var ey = ry * Mathf.Cos(angle); return new Vector2(cos * ex - sin * ey, sin * ex + cos * ey); }
            for (var i = 0; i < parts; i++)
            {
                var a0 = theta + piece * i; var a1 = a0 + piece; var p0 = Point(a0); var p1 = i == parts - 1 ? to : Point(a1);
                steps.Add(new Step(Op.Cubic, p0 + Tangent(a0) * t, p1 - Tangent(a1) * t, p1));
            }
            return steps;
        }

        private static List<Step> Ellipse(float cx, float cy, float rx, float ry)
        {
            var s = new List<Step> { new Step(Op.Move, new Vector2(cx + rx, cy)) };
            s.AddRange(Arc(new Vector2(cx + rx, cy), new Vector2(cx - rx, cy), rx, ry, 0f, false, true)); s.AddRange(Arc(new Vector2(cx - rx, cy), new Vector2(cx + rx, cy), rx, ry, 0f, false, true)); s.Add(new Step(Op.Close)); return s;
        }

        private static List<Step> Rect(float x, float y, float w, float h, float r)
        {
            r = Mathf.Clamp(r, 0f, Mathf.Min(w, h) / 2f); var s = new List<Step> { new Step(Op.Move, new Vector2(x + r, y)), new Step(Op.Line, new Vector2(x + w - r, y)) };
            if (r > 0f) s.AddRange(Arc(new Vector2(x + w - r, y), new Vector2(x + w, y + r), r, r, 0f, false, true)); s.Add(new Step(Op.Line, new Vector2(x + w, y + h - r)));
            if (r > 0f) s.AddRange(Arc(new Vector2(x + w, y + h - r), new Vector2(x + w - r, y + h), r, r, 0f, false, true)); s.Add(new Step(Op.Line, new Vector2(x + r, y + h)));
            if (r > 0f) s.AddRange(Arc(new Vector2(x + r, y + h), new Vector2(x, y + h - r), r, r, 0f, false, true)); s.Add(new Step(Op.Line, new Vector2(x, y + r)));
            if (r > 0f) s.AddRange(Arc(new Vector2(x, y + r), new Vector2(x + r, y), r, r, 0f, false, true)); s.Add(new Step(Op.Close)); return s;
        }
    }

    /// <summary>
    /// An icon of the Godot client drawn by the interface: lines two units wide in a 24-unit box (or filled, for the filled ones), round ends, in <see cref="Colour"/> (the text colour when not set),
    /// at the size of the element. Ignores the mouse, so a button with an icon still takes the click.
    /// </summary>
    public sealed class Icon : VisualElement
    {
        private string _name; private Color? _colour;
        public string Name { get => _name; set { _name = value; MarkDirtyRepaint(); } }
        public Color? Colour { get => _colour; set { _colour = value; MarkDirtyRepaint(); } }

        public Icon(string name, float size = 20f, Color? colour = null)
        {
            _name = name; _colour = colour; pickingMode = PickingMode.Ignore; AddToClassList("lv-icon");
            style.width = size; style.height = size; style.flexShrink = 0; generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context) => DrawInto(context, _name, contentRect, _colour ?? resolvedStyle.color);

        /// <summary>Draws an icon in a box of an element being painted (an <see cref="Icon"/>, or a button that carries one).</summary>
        public static void DrawInto(MeshGenerationContext context, string name, Rect box, Color colour)
        {
            if (box.width < 1f || box.height < 1f) return; var _name = name;
            var scale = Mathf.Min(box.width, box.height) / 24f; var offset = new Vector2(box.x + (box.width - 24f * scale) / 2f, box.y + (box.height - 24f * scale) / 2f);
            var filled = IconShapes.Filled(_name); var painter = context.painter2D;
            painter.lineWidth = 2f * scale; painter.lineCap = LineCap.Round; painter.lineJoin = LineJoin.Round; painter.strokeColor = colour; painter.fillColor = colour;
            Vector2 P(Vector2 v) => offset + v * scale;
            foreach (var shape in IconShapes.Of(_name))
            {
                if (shape.Count == 0) continue; painter.BeginPath(); var moved = false; var only = true; var first = Vector2.zero;
                foreach (var s in shape)
                {
                    switch (s.Op)
                    {
                        case IconShapes.Op.Move: painter.MoveTo(P(s.A)); moved = true; first = s.A; break;
                        case IconShapes.Op.Line: painter.LineTo(P(s.A)); only &= (s.A - first).sqrMagnitude < .0004f; break;
                        case IconShapes.Op.Cubic: painter.BezierCurveTo(P(s.A), P(s.B), P(s.C)); only = false; break;
                        case IconShapes.Op.Close: painter.ClosePath(); break;
                    }
                }
                if (!moved) continue;
                if (only && !filled) { painter.BeginPath(); painter.Arc(P(first), scale, 0f, 360f); painter.Fill(); continue; }   // "l0 .01": a dot
                if (filled) painter.Fill(); else painter.Stroke();
            }
        }
    }
}
