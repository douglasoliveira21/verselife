using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    public enum IconSet { Hairline, Angular, Solid }

    /// <summary>
    /// An icon drawn from short path strings on a 24 grid, one family per direction. Parts are split by ';' and start with how they are painted:
    /// s stroke, f fill (ink), b fill (accent), g faint accent fill, k fill in the surface colour (a hole), x stroke in the surface colour.
    /// Commands: M L C Z, O cx cy r (circle), R x y w h r (rounded rectangle).
    /// </summary>
    public sealed class Glyph : VisualElement
    {
        private readonly IconSet _set; private readonly string _path;
        public Color Accent = Color.white, Surface = Color.black;
        public float Weight;

        public Glyph(IconSet set, string id, float size, Color? accent = null, Color? surface = null, float weight = 0f)
        {
            _set = set; name = "icon-" + id; _path = Of(set).TryGetValue(id, out var path) ? path : "s M4 4 L20 20";
            if (accent is { } a) Accent = a; if (surface is { } s) Surface = s; Weight = weight > 0f ? weight : set == IconSet.Hairline ? 1.35f : set == IconSet.Angular ? 1.6f : 2.3f;
            style.width = size; style.height = size; style.flexShrink = 0; pickingMode = PickingMode.Ignore; generateVisualContent += Draw;
        }

        public static Glyph Add(VisualElement parent, IconSet set, string id, float size, Color? accent = null, Color? surface = null, float weight = 0f)
        {
            var glyph = new Glyph(set, id, size, accent, surface, weight); parent.Add(glyph); return glyph;
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D; var k = layout.width / 24f; if (!(k > 0f)) return; var ink = resolvedStyle.color;
            foreach (var raw in _path.Split(';'))
            {
                var part = raw.Trim(); if (part.Length < 2) continue; var mode = part[0]; var t = part.Substring(1).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries); var i = 0;
                float N() => float.Parse(t[i++], CultureInfo.InvariantCulture);
                p.BeginPath();
                while (i < t.Length)
                {
                    var word = t[i++]; var command = word[0]; if (word.Length > 1) { t[--i] = word.Substring(1); }
                    switch (command)
                    {
                        case 'M': { var x = N(); var y = N(); p.MoveTo(new Vector2(x, y) * k); break; }
                        case 'L': { var x = N(); var y = N(); p.LineTo(new Vector2(x, y) * k); break; }
                        case 'C': { var a = new Vector2(N(), N()); var b = new Vector2(N(), N()); var c = new Vector2(N(), N()); p.BezierCurveTo(a * k, b * k, c * k); break; }
                        case 'Z': p.ClosePath(); break;
                        case 'O': { var c = new Vector2(N(), N()) * k; var r = N() * k; p.MoveTo(c + new Vector2(r, 0f)); p.Arc(c, r, new Angle(0f), new Angle(360f)); p.ClosePath(); break; }
                        case 'R':
                        {
                            var x = N() * k; var y = N() * k; var w = N() * k; var h = N() * k; var r = N() * k;
                            var wide = w - 2f * r > .05f; var tall = h - 2f * r > .05f;
                            p.MoveTo(new Vector2(x + r, y)); if (wide) p.LineTo(new Vector2(x + w - r, y)); p.QuadraticCurveTo(new Vector2(x + w, y), new Vector2(x + w, y + r));
                            if (tall) p.LineTo(new Vector2(x + w, y + h - r)); p.QuadraticCurveTo(new Vector2(x + w, y + h), new Vector2(x + w - r, y + h));
                            if (wide) p.LineTo(new Vector2(x + r, y + h)); p.QuadraticCurveTo(new Vector2(x, y + h), new Vector2(x, y + h - r));
                            if (tall) p.LineTo(new Vector2(x, y + r)); p.QuadraticCurveTo(new Vector2(x, y), new Vector2(x + r, y)); p.ClosePath(); break;
                        }
                    }
                }
                if (mode == 's' || mode == 'x')
                {
                    p.strokeColor = mode == 'x' ? Surface : ink; p.lineWidth = Mathf.Max(1f, Weight * k);
                    p.lineCap = _set == IconSet.Angular ? LineCap.Butt : LineCap.Round; p.lineJoin = _set == IconSet.Angular ? LineJoin.Miter : LineJoin.Round; p.Stroke();
                }
                else
                {
                    var faint = Accent; faint.a *= .3f;
                    p.fillColor = mode == 'f' ? ink : mode == 'b' ? Accent : mode == 'g' ? faint : Surface; p.Fill();
                }
            }
        }

        private static Dictionary<string, string> Of(IconSet set) => set == IconSet.Hairline ? Hair : set == IconSet.Angular ? Angled : Solids;

        private static readonly Dictionary<string, string> Hair = new Dictionary<string, string>
        {
            ["chat"] = "s M4 5 L20 5 L20 16 L11 16 L6 20 L6 16 L4 16 Z",
            ["voice"] = "s M9 6.5 C9 2.2 15 2.2 15 6.5 L15 11 C15 15.3 9 15.3 9 11 Z M6 11 C6 15 9 17.5 12 17.5 C15 17.5 18 15 18 11 M12 17.5 L12 21",
            ["move"] = "s O14 4.5 2 M13 9 L10.5 13.5 L13.5 16 L12.5 21 M10.5 13.5 L7 19 M13 9 L16.5 12 L20 12 M13 9 L8.5 11",
            ["camera"] = "s M3 8 L15 8 L15 17 L3 17 Z M15 11 L21 8 L21 17 L15 14",
            ["gestures"] = "s M8 13 L8 5 M11 11 L11 3.5 M14 11 L14 4.5 M17 13 L17 7 M8 13 L6 10.5 C4.5 9.5 3.5 11 4.5 12.5 L8 18 C9.5 20.5 11.5 21 13 21 C15.5 21 17 18.5 17 15 L17 13",
            ["photo"] = "s M4 4 L20 4 L20 20 L4 20 Z;s O9 9 1.6 M4 17 L10 12 L14 16 L17 13 L20 16",
            ["build"] = "s M12 3 L20 7.5 L20 16.5 L12 21 L4 16.5 L4 7.5 Z M4 7.5 L12 12 L20 7.5 M12 12 L12 21",
            ["people"] = "s O9 8 3 M3 20 C3 15.5 5.5 13.5 9 13.5 C12.5 13.5 15 15.5 15 20 M16.5 13.7 C19.2 13.9 21 15.8 21 19;s O16.5 8.5 2.2",
            ["map"] = "s M3 6 L9 4 L15 6 L21 4 L21 18 L15 20 L9 18 L3 20 Z M9 4 L9 18 M15 6 L15 20",
            ["minimap"] = "s O12 12 9;s M15.5 8.5 L13 13 L8.5 15.5 L11 11 Z",
            ["inventory"] = "s M5 8 L19 8 L18 21 L6 21 Z M9 11 L9 6.5 C9 4.5 10.3 3 12 3 C13.7 3 15 4.5 15 6.5 L15 11",
            ["search"] = "s O10.5 10.5 6.5 M15.4 15.4 L21 21",
            ["wallet"] = "s M4 7 C4 5.5 5 5 6 5 L18 5 L18 7 M4 7 L20 7 L20 19 L4 19 Z M15.5 13 L16.5 13",
            ["notifications"] = "s M6 17 L6 11 C6 7 8.5 4.5 12 4.5 C15.5 4.5 18 7 18 11 L18 17 M4 17 L20 17 M10 20 C10.8 21 13.2 21 14 20",
            ["social"] = "s O12 8 3.5;s M5 21 C5 16 8 13.5 12 13.5 C16 13.5 19 16 19 21",
            ["more"] = "s O5 12 1.1;s O12 12 1.1;s O19 12 1.1",
            ["settings"] = "s O12 12 3;s M12 3 L12 6 M12 18 L12 21 M3 12 L6 12 M18 12 L21 12 M5.6 5.6 L7.8 7.8 M16.2 16.2 L18.4 18.4 M5.6 18.4 L7.8 16.2 M16.2 7.8 L18.4 5.6",
            ["help"] = "s O12 12 9;s M9.5 9.5 C9.5 6.5 14.5 6.5 14.5 9.5 C14.5 11.8 12 11.6 12 14.2 M12 17 L12 17.4",
            ["back"] = "s M14.5 5 L7.5 12 L14.5 19", ["forward"] = "s M9.5 5 L16.5 12 L9.5 19",
            ["info"] = "s O12 12 9;s M12 11 L12 17 M12 7.3 L12 7.8",
            ["parcel"] = "s M4 9 L4 4 L9 4 M15 4 L20 4 L20 9 M20 15 L20 20 L15 20 M9 20 L4 20 L4 15",
            ["coin"] = "s O12 12 8.5;s M12 7.5 L15 12 L12 16.5 L9 12 Z",
            ["plus"] = "s M12 5 L12 19 M5 12 L19 12", ["send"] = "s M4 12 L20 4 L15 20 L11.5 13.5 Z M11.5 13.5 L20 4",
            ["close"] = "s M6 6 L18 18 M18 6 L6 18", ["minimize"] = "s M5 12 L19 12", ["zoomin"] = "s M12 6 L12 18 M6 12 L18 12", ["zoomout"] = "s M6 12 L18 12",
            ["chevron"] = "s M7 10 L12 15 L17 10", ["signal"] = "s M5 19 L5 16 M10 19 L10 13 M15 19 L15 9 M20 19 L20 5", ["menu"] = "s M4 7 L20 7 M4 12 L14 12 M4 17 L18 17", ["check"] = "s M5 12.5 L10 17.5 L19 7",
        };

        private static readonly Dictionary<string, string> Angled = new Dictionary<string, string>
        {
            ["chat"] = "s M3 5 L18 5 L21 8 L21 16 L11 16 L6 20 L6 16 L3 16 Z;b M7 9.5 L17 9.5 L16 11.5 L7 11.5 Z",
            ["voice"] = "s M10 3 L14 3 L15.5 4.5 L15.5 12 L14 13.5 L10 13.5 L8.5 12 L8.5 4.5 Z M5.5 10 L5.5 13 L9 16.5 L15 16.5 L18.5 13 L18.5 10 M12 16.5 L12 21 M8.5 21 L15.5 21;b M10.5 5 L13.5 5 L13.5 8.5 L10.5 8.5 Z",
            ["move"] = "s M5 11 L12 4 L19 11 M5 20 L12 13 L19 20;b M12 7.5 L14.5 10.5 L9.5 10.5 Z",
            ["camera"] = "s M3 8 L6 5.5 L15 5.5 L15 18.5 L6 18.5 L3 16 Z M15 10 L21 7 L21 17 L15 14;b M7 10 L11 10 L11 14 L7 14 Z",
            ["gestures"] = "s M7 14 L7 6 L9.7 6 L9.7 11.5 L9.7 3 L12.4 3 L12.4 11.5 L12.4 4.5 L15.1 4.5 L15.1 12 L15.1 7 L17.8 7 L17.8 16 L14 21 L9.5 21 L4 13.5 L4 11 L5.5 11 Z;b M10.5 15 L14.5 15 L13.5 17 L11.5 17 Z",
            ["photo"] = "s M3 7 L7 7 L9 4 L15 4 L17 7 L21 7 L21 19 L3 19 Z M9 13 L12 10 L15 13 L12 16 Z;b M17.5 9 L19.5 9 L19.5 11 L17.5 11 Z",
            ["build"] = "g M12 3 L20.5 7.8 L12 12.4 L3.5 7.8 Z;s M12 2.5 L20.5 7.5 L20.5 16.5 L12 21.5 L3.5 16.5 L3.5 7.5 Z M3.5 7.5 L12 12.4 L20.5 7.5 M12 12.4 L12 21.5",
            ["people"] = "s M6.5 5 L9 3.5 L11.5 5 L11.5 9 L9 10.5 L6.5 9 Z M3 20.5 L3 16 L6 13.5 L12 13.5 L15 16 L15 20.5 M14.5 5.5 L18.5 5.5 L18.5 9.5 L16 11 M17 13.5 L18.5 13.5 L21 16 L21 20;b M7.5 15.5 L10.5 15.5 L9 18 Z",
            ["map"] = "g M9 3.5 L15 6.5 L15 20.5 L9 17.5 Z;s M3 6 L9 3.5 L15 6.5 L21 4 L21 18 L15 20.5 L9 17.5 L3 20 Z",
            ["minimap"] = "s M8 3 L16 3 L21 8 L21 16 L16 21 L8 21 L3 16 L3 8 Z;b M12 6.5 L15.5 15 L12 13 L8.5 15 Z",
            ["inventory"] = "s M4 8 L7 5 L17 5 L20 8 L20 20 L4 20 Z M4 11 L20 11;b M9.5 9.5 L14.5 9.5 L14.5 14 L9.5 14 Z",
            ["search"] = "s M8 3.5 L13 3.5 L16.5 7 L16.5 12 L13 15.5 L8 15.5 L4.5 12 L4.5 7 Z M15 14 L21 20",
            ["wallet"] = "s M3 7 L6 4 L18 4 L18 7 M3 7 L21 7 L21 20 L3 20 Z;b M15 11.5 L21 11.5 L21 15.5 L15 15.5 L13.5 13.5 Z",
            ["notifications"] = "s M12 3 L17 6 L17 13 L20 17 L4 17 L7 13 L7 6 Z M9.5 20.5 L14.5 20.5;b M10 8 L14 8 L14 10.5 L10 10.5 Z",
            ["social"] = "s M9 4 L15 4 L16.5 6 L16.5 10 L12 12.5 L7.5 10 L7.5 6 Z M4 21 L4 18 L8 15 L16 15 L20 18 L20 21",
            ["more"] = "b M3.5 10.5 L6.5 10.5 L6.5 13.5 L3.5 13.5 Z M10.5 10.5 L13.5 10.5 L13.5 13.5 L10.5 13.5 Z M17.5 10.5 L20.5 10.5 L20.5 13.5 L17.5 13.5 Z",
            ["settings"] = "s M10 3 L14 3 L14.7 6 L17.5 7.2 L20 5.8 L22 9.2 L19.8 11 L19.8 13 L22 14.8 L20 18.2 L17.5 16.8 L14.7 18 L14 21 L10 21 L9.3 18 L6.5 16.8 L4 18.2 L2 14.8 L4.2 13 L4.2 11 L2 9.2 L4 5.8 L6.5 7.2 L9.3 6 Z;b M10 10 L14 10 L14 14 L10 14 Z",
            ["help"] = "s M8 3 L16 3 L21 8 L21 16 L16 21 L8 21 L3 16 L3 8 Z M9 9.5 L10.5 7.5 L13.5 7.5 L15 9.5 L12 12.5 L12 14.5;b M11 16 L13 16 L13 18 L11 18 Z",
            ["back"] = "s M15 4 L7 12 L15 20", ["forward"] = "s M9 4 L17 12 L9 20",
            ["info"] = "s M8 3 L16 3 L21 8 L21 16 L16 21 L8 21 L3 16 L3 8 Z M12 11 L12 17;b M11 6.5 L13 6.5 L13 8.8 L11 8.8 Z",
            ["parcel"] = "g M12 7 L17.5 10.2 L12 13.4 L6.5 10.2 Z;s M12 3 L21 8.5 L21 15.5 L12 21 L3 15.5 L3 8.5 Z",
            ["coin"] = "s M8 3.5 L16 3.5 L20.5 8 L20.5 16 L16 20.5 L8 20.5 L3.5 16 L3.5 8 Z;b M12 7.5 L16 12 L12 16.5 L8 12 Z",
            ["plus"] = "s M12 5 L12 19 M5 12 L19 12", ["send"] = "s M3 4 L21 12 L3 20 L7 12 Z M7 12 L13 12",
            ["close"] = "s M5 5 L19 19 M19 5 L5 19", ["minimize"] = "s M5 17 L19 17", ["zoomin"] = "s M12 6 L12 18 M6 12 L18 12", ["zoomout"] = "s M6 12 L18 12",
            ["chevron"] = "s M7 9 L12 15 L17 9", ["signal"] = "b M3 20 L6 20 L6 16 L3 16 Z M8 20 L11 20 L11 13 L8 13 Z M13 20 L16 20 L16 9 L13 9 Z M18 20 L21 20 L21 4 L18 4 Z",
            ["menu"] = "s M3 6 L18 6 L21 9 M3 12 L21 12 M3 18 L15 18", ["check"] = "s M4 12 L9.5 17.5 L20 6",
        };

        private static readonly Dictionary<string, string> Solids = new Dictionary<string, string>
        {
            ["chat"] = "f R2.5 3.5 19 14 6;f M7 15.5 L5.5 21.5 L13 16.5 Z;k O8 10.5 1.4;k O12 10.5 1.4;k O16 10.5 1.4",
            ["voice"] = "f R8.5 2.5 7 12.5 3.5;s M5.5 11 C5.5 15.5 8.5 17.8 12 17.8 C15.5 17.8 18.5 15.5 18.5 11 M12 18 L12 21 M8.5 21.2 L15.5 21.2",
            ["move"] = "f O14.5 4.5 2.6;s M13 9.5 L10.5 14 L13.5 16.5 L12.5 21 M10.5 14 L7 20 M12.5 9.5 L8 11.5 M13 9.5 L15.5 12.5 L19 12.5",
            ["camera"] = "f R2.5 7 13 11.5 3.5;f M15.5 11 L21.5 7.5 L21.5 18 L15.5 14.5 Z;b O7.2 11.4 2",
            ["gestures"] = "f R7 4 2.8 11 1.4;f R10.4 2.5 2.8 12 1.4;f R13.8 3.5 2.8 11 1.4;f R17.2 6 2.8 10 1.4;f M7 12 L20 12 L20 15 C20 19 17 21.5 14 21.5 C10.8 21.5 9.2 20 7.2 17 L3.5 12.4 C2.8 11 4.8 9.6 6 10.8 Z",
            ["photo"] = "f R2.5 5 19 15 4.5;k O12 12.5 4.6;b O12 12.5 2.8;k O18.3 8.5 1.1",
            ["build"] = "f R2.5 13 9 8.5 2.2;f R12.5 13 9 8.5 2.2;b R7.5 3 9 8.5 2.2",
            ["people"] = "f O8.5 8 3.7;f M2.2 20.5 C2.2 15.5 5 13 8.5 13 C12 13 14.8 15.5 14.8 20.5 Z;b O17 8.8 3;b M15.2 13.4 C19.2 12.8 22 15.2 22 19.8 L16.2 19.8 C16.2 17.2 15.6 15 15.2 13.4 Z",
            ["map"] = "f M2.5 6.5 L8.4 4 L8.4 17.8 L2.5 20.3 Z;b M9.4 4 L14.6 6.2 L14.6 20 L9.4 17.8 Z;f M15.6 6.2 L21.5 3.7 L21.5 17.5 L15.6 20 Z",
            ["minimap"] = "f O12 12 10;k M16.8 7.2 L13.8 13.8 L7.2 16.8 L10.2 10.2 Z;b O12 12 1.8",
            ["inventory"] = "s M8.5 10 L8.5 7 C8.5 4.5 10 3 12 3 C14 3 15.5 4.5 15.5 7 L15.5 10;f R3.5 8.5 17 13 4.5;b R8.5 13.5 7 4.2 2",
            ["search"] = "s O10.5 10.5 6.3 M15.4 15.4 L20.5 20.5",
            ["wallet"] = "s M5 6.5 L15.5 3.5 L16.5 6;f R2.5 6 19 14.5 4;b R13.5 10.5 8 6 2.6;k O16.8 13.5 1.2",
            ["notifications"] = "f M12 2.8 C8 2.8 5.8 6 5.8 10 L5.8 13.6 L3.6 17.5 L20.4 17.5 L18.2 13.6 L18.2 10 C18.2 6 16 2.8 12 2.8 Z;b M9 19 L15 19 C15 23 9 23 9 19 Z",
            ["social"] = "f O12 7.5 4.4;f M3.8 21.5 C3.8 16 7.5 13.5 12 13.5 C16.5 13.5 20.2 16 20.2 21.5 Z",
            ["more"] = "f O5 12 2.3;f O12 12 2.3;b O19 12 2.3",
            ["settings"] = "f M10 2.5 L14 2.5 L14.7 5.6 L17.4 6.8 L20.1 5.2 L22.2 9 L19.8 11 L19.8 13 L22.2 15 L20.1 18.8 L17.4 17.2 L14.7 18.4 L14 21.5 L10 21.5 L9.3 18.4 L6.6 17.2 L3.9 18.8 L1.8 15 L4.2 13 L4.2 11 L1.8 9 L3.9 5.2 L6.6 6.8 L9.3 5.6 Z;k O12 12 3.4",
            ["help"] = "f O12 12 10;x M9.2 9.5 C9.2 6 14.8 6 14.8 9.5 C14.8 11.8 12 11.8 12 14.2;k O12 17.6 1.4",
            ["back"] = "s M14.5 5 L7.5 12 L14.5 19", ["forward"] = "s M9.5 5 L16.5 12 L9.5 19",
            ["info"] = "f O12 12 10;x M12 11.3 L12 17;k O12 7.4 1.5",
            ["parcel"] = "f M12 2.5 L22 8.5 L12 14.5 L2 8.5 Z;b M2 12.5 L12 18.5 L22 12.5 L22 15.5 L12 21.5 L2 15.5 Z",
            ["coin"] = "b O12 12 10;k M12 6.5 L16 12 L12 17.5 L8 12 Z",
            ["plus"] = "s M12 5 L12 19 M5 12 L19 12", ["send"] = "f M2.5 11 L21.5 2.5 L15 21.5 L11 13.8 Z",
            ["close"] = "s M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5", ["minimize"] = "s M5.5 12 L18.5 12", ["zoomin"] = "s M12 6 L12 18 M6 12 L18 12", ["zoomout"] = "s M6 12 L18 12",
            ["chevron"] = "s M7 10 L12 15 L17 10", ["signal"] = "f R3 14.5 4.4 7 1.6;f R9.8 9.5 4.4 12 1.6;b R16.6 3.5 4.4 18 1.6", ["menu"] = "s M4.5 7 L19.5 7 M4.5 12 L19.5 12 M4.5 17 L13.5 17", ["check"] = "s M5 12.5 L10 17.5 L19 7.5",
        };
    }
}
