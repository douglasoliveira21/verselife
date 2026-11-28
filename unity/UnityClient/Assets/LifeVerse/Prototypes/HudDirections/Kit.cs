using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>The small pieces the three prototypes share: elements, fonts, drawings and the marks the checks read. Nothing here is used by the game.</summary>
    public static class Kit
    {
        public const string Root = "Assets/LifeVerse/Prototypes/HudDirections/";

        public static VisualElement El(VisualElement parent, string classes = null)
        {
            var e = new VisualElement(); Classes(e, classes); parent?.Add(e); return e;
        }

        public static Label Text(VisualElement parent, string text, string classes = null, FontDefinition? font = null)
        {
            var label = new Label(text) { enableRichText = true }; Classes(label, classes); if (font is { } f) label.style.unityFontDefinition = new StyleFontDefinition(f); parent?.Add(label); return label;
        }

        private static void Classes(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return; foreach (var c in classes.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
        }

        /// <summary>Says which functions of the game's HUD an element stands for (the capture checks that every one has a place).</summary>
        public static T Fn<T>(this T element, params string[] ids) where T : VisualElement { element.userData = "fn:" + string.Join("|", ids); return element; }

        /// <summary>A block that must not cover another block.</summary>
        public static T Region<T>(this T element, string name) where T : VisualElement { element.AddToClassList("region"); element.name = name; return element; }

        public static T At<T>(this T e, float? left = null, float? top = null, float? right = null, float? bottom = null) where T : VisualElement
        {
            e.style.position = Position.Absolute; if (left is { } l) e.style.left = l; if (top is { } t) e.style.top = t; if (right is { } r) e.style.right = r; if (bottom is { } b) e.style.bottom = b; return e;
        }

        public static Color Hex(string hex, float alpha = 1f) { ColorUtility.TryParseHtmlString(hex, out var c); c.a = alpha; return c; }

        // ---- fonts: the ones installed with Windows on this computer (their licence does not let the game ship them), never copied into the project ----
        private static readonly Dictionary<string, FontAsset> Fonts = new Dictionary<string, FontAsset>();
        /// <summary>The fonts that could not be loaded (the pictures would not show the direction's lettering).</summary>
        public static readonly HashSet<string> MissingFonts = new HashSet<string>();

        public static FontDefinition Font(string family, string style = "Regular")
        {
            var key = family + " " + style;
            if (!Fonts.TryGetValue(key, out var asset) || asset == null)
            {
                asset = MissingFonts.Contains(key) ? null : FontAsset.CreateFontAsset(family, style, 80);
                if (asset == null) { MissingFonts.Add(key); return FontDefinition.FromFont(Resources.Load<UnityEngine.Font>("LifeVerse/UI/Fonts/Inter-Regular")); }
                asset.hideFlags = HideFlags.HideAndDontSave; Fonts[key] = asset;
            }
            return FontDefinition.FromSDFFont(asset);
        }

        // ---- the only gradients: soft shade under text that sits straight on the world ----
        public static Texture2D Shade(Color colour, float from, float to)
        {
            var texture = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (var y = 0; y < 128; y++) { var t = y / 127f; var c = colour; c.a = Mathf.Lerp(from, to, t * t * (3f - 2f * t)); texture.SetPixel(0, y, c); }
            texture.Apply(); return texture;
        }

        public static VisualElement Fill(VisualElement parent, Texture2D texture)
        {
            var e = El(parent); e.pickingMode = PickingMode.Ignore; e.style.backgroundImage = new StyleBackground(texture); return e;
        }

        /// <summary>An element that draws itself.</summary>
        public static VisualElement Canvas(VisualElement parent, float width, float height, Action<Painter2D, Rect> draw, string classes = null)
        {
            var e = El(parent, classes); e.style.width = width; e.style.height = height; e.style.flexShrink = 0;
            e.generateVisualContent += context => draw(context.painter2D, new Rect(0, 0, e.layout.width, e.layout.height)); return e;
        }

        public static void Poly(Painter2D p, params Vector2[] points)
        {
            p.BeginPath(); p.MoveTo(points[0]); for (var i = 1; i < points.Length; i++) p.LineTo(points[i]); p.ClosePath();
        }

        public static void Disc(Painter2D p, Vector2 centre, float radius, Color colour)
        {
            p.fillColor = colour; p.BeginPath(); p.Arc(centre, radius, new Angle(0f), new Angle(360f)); p.Fill();
        }

        public static void Ring(Painter2D p, Vector2 centre, float radius, Color colour, float width, float from = 0f, float to = 360f)
        {
            p.strokeColor = colour; p.lineWidth = width; p.BeginPath(); p.Arc(centre, radius, new Angle(from), new Angle(to)); p.Stroke();
        }

        public static void Line(Painter2D p, Vector2 a, Vector2 b, Color colour, float width = 1f)
        {
            p.strokeColor = colour; p.lineWidth = width; p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        /// <summary>The island of the pictures as an outline, in a square of side 1 (north up).</summary>
        public static readonly Vector2[] Coast =
        {
            new Vector2(.22f, .20f), new Vector2(.46f, .14f), new Vector2(.70f, .22f), new Vector2(.84f, .40f), new Vector2(.80f, .62f), new Vector2(.64f, .80f), new Vector2(.42f, .86f), new Vector2(.24f, .74f), new Vector2(.16f, .52f),
        };

        public static void Island(Painter2D p, Rect area, Color land, Color edge, float edgeWidth)
        {
            var points = new Vector2[Coast.Length]; for (var i = 0; i < Coast.Length; i++) points[i] = new Vector2(area.x + Coast[i].x * area.width, area.y + Coast[i].y * area.height);
            Poly(p, points); p.fillColor = land; p.Fill(); if (edgeWidth > 0f) { Poly(p, points); p.strokeColor = edge; p.lineWidth = edgeWidth; p.lineJoin = LineJoin.Round; p.Stroke(); }
        }

        /// <summary>Where the others stand on the map, in the same square.</summary>
        public static readonly (string Name, Vector2 At)[] Dots = { ("Ana", new Vector2(.45f, .46f)), ("Beto", new Vector2(.53f, .45f)), ("Marina", new Vector2(.62f, .40f)), ("Caio", new Vector2(.34f, .33f)) };
        public static readonly Vector2 Me = new Vector2(.50f, .62f);

        /// <summary>Puts one element over each person's head, following how the picture is cropped on this screen.</summary>
        public static void Plates(VisualElement root, WorldBackdrop.People people, float pictureAspect, Func<WorldBackdrop.Person, VisualElement> make)
        {
            if (people == null) return;
            var layer = El(root, "plates"); layer.pickingMode = PickingMode.Ignore; layer.At(0, 0, 0, 0); layer.SendToBack();
            var placed = new List<(VisualElement Plate, WorldBackdrop.Person Who)>();
            foreach (var who in people.list) { var plate = make(who); if (plate == null) continue; plate.At(); plate.Fn("name-plates"); layer.Add(plate); placed.Add((plate, who)); }
            layer.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var w = layer.layout.width; var h = layer.layout.height; if (w < 2f || h < 2f) return;
                var sw = w / h > pictureAspect ? w : h * pictureAspect; var sh = sw / pictureAspect;
                foreach (var (plate, who) in placed) { plate.style.left = (w - sw) / 2f + who.x * sw; plate.style.top = (h - sh) / 2f + (1f - who.y) * sh; }
            });
        }
    }
}
