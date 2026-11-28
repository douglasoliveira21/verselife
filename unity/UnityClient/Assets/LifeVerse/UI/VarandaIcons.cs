using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The icons of the Varanda interface: solid, rounded and two-tone, drawn from short path strings on a 24 grid (the bar, the place sign, the pills and the postcard; the windows keep
    /// the line icons of <see cref="Icon"/>). Parts are split by ';' and start with how they are painted: s stroke, f fill (ink), b fill (accent), k a hole (the surface colour),
    /// x stroke in the surface colour. Commands: M x y, L x y, C (three points), Z, O cx cy r (circle), R x y w h r (rounded rectangle).
    /// </summary>
    public static class VarandaIcons
    {
        public static bool Has(string id) => id != null && Paths.ContainsKey(id);
        public static IEnumerable<string> Names => Paths.Keys;

        private static readonly CustomStyleProperty<Color> InkProperty = new CustomStyleProperty<Color>("--lv-icon-ink"), AccentProperty = new CustomStyleProperty<Color>("--lv-icon-accent"), SurfaceProperty = new CustomStyleProperty<Color>("--lv-icon-surface");

        /// <summary>The ink, accent and surface colours the theme gives an element (the text colour and sensible ones when it gives none).</summary>
        public static (Color Ink, Color Accent, Color Surface) ColoursOf(VisualElement element)
        {
            var ink = element.customStyle.TryGetValue(InkProperty, out var i) ? i : element.resolvedStyle.color;
            var accent = element.customStyle.TryGetValue(AccentProperty, out var a) ? a : ink;
            var surface = element.customStyle.TryGetValue(SurfaceProperty, out var s) ? s : new Color(.97f, .95f, .89f);
            return (ink, accent, surface);
        }

        public static void Draw(MeshGenerationContext context, string id, Rect box, Color ink, Color accent, Color surface)
        {
            if (box.width < 1f || box.height < 1f || !Paths.TryGetValue(id ?? "", out var path)) return;
            var p = context.painter2D; var k = Mathf.Min(box.width, box.height) / 24f; var origin = new Vector2(box.x + (box.width - 24f * k) / 2f, box.y + (box.height - 24f * k) / 2f);
            foreach (var raw in path.Split(';'))
            {
                var part = raw.Trim(); if (part.Length < 2) continue; var mode = part[0]; var t = part.Substring(1).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries); var i = 0;
                float N() => float.Parse(t[i++], CultureInfo.InvariantCulture);
                Vector2 V(float x, float y) => origin + new Vector2(x, y) * k;
                Vector2 Next() { var x = N(); var y = N(); return V(x, y); }
                p.BeginPath();
                while (i < t.Length)
                {
                    var word = t[i++]; var command = word[0]; if (word.Length > 1) t[--i] = word.Substring(1);
                    switch (command)
                    {
                        case 'M': p.MoveTo(Next()); break;
                        case 'L': p.LineTo(Next()); break;
                        case 'C': { var a = Next(); var b = Next(); var c = Next(); p.BezierCurveTo(a, b, c); break; }
                        case 'Z': p.ClosePath(); break;
                        case 'O': { var c = Next(); var r = N() * k; p.MoveTo(c + new Vector2(r, 0f)); p.Arc(c, r, new Angle(0f), new Angle(360f)); p.ClosePath(); break; }
                        case 'R':
                        {
                            var x = N(); var y = N(); var w = N(); var h = N(); var r = N();
                            var wide = w - 2f * r > .05f; var tall = h - 2f * r > .05f;   // a segment of no length would spike the outline
                            p.MoveTo(V(x + r, y)); if (wide) p.LineTo(V(x + w - r, y)); p.QuadraticCurveTo(V(x + w, y), V(x + w, y + r));
                            if (tall) p.LineTo(V(x + w, y + h - r)); p.QuadraticCurveTo(V(x + w, y + h), V(x + w - r, y + h));
                            if (wide) p.LineTo(V(x + r, y + h)); p.QuadraticCurveTo(V(x, y + h), V(x, y + h - r));
                            if (tall) p.LineTo(V(x, y + r)); p.QuadraticCurveTo(V(x, y), V(x + r, y)); p.ClosePath(); break;
                        }
                    }
                }
                if (mode == 's' || mode == 'x') { p.strokeColor = mode == 'x' ? surface : ink; p.lineWidth = Mathf.Max(1f, 2.3f * k); p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round; p.Stroke(); }
                else { p.fillColor = mode == 'f' ? ink : mode == 'b' ? accent : surface; p.Fill(); }
            }
        }

        private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>
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
            ["send"] = "f M2.5 11 L21.5 2.5 L15 21.5 L11 13.8 Z",
            ["close"] = "s M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5", ["minimize"] = "s M5.5 12 L18.5 12", ["zoomin"] = "s M12 6 L12 18 M6 12 L18 12", ["zoomout"] = "s M6 12 L18 12",
        };
    }

    /// <summary>One Varanda icon as an element, in the colours the theme gives it.</summary>
    public sealed class VarandaIcon : VisualElement
    {
        private string _name;
        public string Name { get => _name; set { _name = value; MarkDirtyRepaint(); } }

        public VarandaIcon(string name, float size)
        {
            _name = name; pickingMode = PickingMode.Ignore; AddToClassList("lv-icon"); style.width = size; style.height = size; style.flexShrink = 0;
            generateVisualContent += context => { var (ink, accent, surface) = VarandaIcons.ColoursOf(this); VarandaIcons.Draw(context, _name, contentRect, ink, accent, surface); };
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }
    }
}
