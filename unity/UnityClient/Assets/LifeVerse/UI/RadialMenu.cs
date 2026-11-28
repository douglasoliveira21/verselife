using System;
using System.Collections.Generic;
using LifeVerse.Shared.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>One action of the ring; <paramref name="Hint"/> is the explanation shown when the cursor rests on it (why it is unavailable, when it is).</summary>
    public sealed record RadialItem(string Label, Action Run, bool Enabled = true, string Hint = null);

    /// <summary>
    /// The radial menu: a ring of up to eight actions around the pointer (more are split in pages of eight with a "Próxima" in the middle), chosen by moving to a slice and clicking, by
    /// the number keys 1 to 8, or by Tab and the arrows. The geometry (which slice a point is in, where each label goes, keeping the ring on the screen, the pages) is the shared
    /// <see cref="RadialMath"/> of the Godot client. Escape or a click outside closes it.
    /// </summary>
    public sealed class RadialMenu : VisualElement
    {
        /// <summary>The size of the ring (1 = 300 px across at Full HD), how opaque its slices are and how much the slice under the cursor glows (Configurações › Interface).</summary>
        public static float Scale = 1f, Opacity = .88f, Glow = .6f;
        /// <summary>How quickly the ring opens (1 = 0.16 s; 0 opens at once, as with "Reduzir movimento") and whether resting on a slice explains it.</summary>
        public static float Speed = 1f; public static bool Tooltips = true, ReduceMotion;
        public static float OpenSeconds => ReduceMotion || Speed <= .01f ? 0f : .16f / Mathf.Clamp(Speed, .25f, 3f);
        /// <summary>The words of the tip of a slice: its hint, or why it cannot be used.</summary>
        public static string TipOf(RadialItem item) => item == null ? "" : !string.IsNullOrEmpty(item.Hint) ? item.Hint : item.Enabled ? item.Label : item.Label + ": indisponível agora.";
        private Label _tip; public string TipText => _tip != null && _tip.style.display == DisplayStyle.Flex ? _tip.text : "";
        public static float Inner => 46f * Scale;
        public static float Outer => 150f * Scale;
        private readonly List<List<RadialItem>> _pages = new List<List<RadialItem>>(); private int _page; private Vector2 _centre; private int _hover = -1;
        private readonly List<Label> _labels = new List<Label>();
        public bool IsOpen => parent != null;
        public int Page => _page;
        public int PageCount => _pages.Count;
        public int Hover => _hover;
        public IReadOnlyList<RadialItem> Slices => _pages.Count == 0 ? Array.Empty<RadialItem>() : _pages[_page];
        public Vector2 Centre => _centre;

        public RadialMenu()
        {
            name = "radial"; AddToClassList("lv-radial"); style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0; pickingMode = PickingMode.Position; focusable = true;
            generateVisualContent += Draw; RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerMoveEvent>(e => SetHover(SliceAt(e.localPosition)));
            RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) Choose(SliceAt(e.localPosition), e.localPosition); else Close(); e.StopPropagation(); });
            RegisterCallback<KeyDownEvent>(OnKey);
        }

        public static float AngleOf(int index, int count) => RadialMath.SliceCenter(index, count);

        /// <summary>The slice a point is over, or -1 (in the middle, outside the ring).</summary>
        public int SliceAt(Vector2 position)
        {
            var count = Slices.Count; if (count == 0) return -1; var offset = position - _centre;
            return RadialMath.SliceAt(new System.Numerics.Vector2(offset.x, offset.y), count, Inner, Outer);
        }

        public void Open(UiRoot ui, IReadOnlyList<RadialItem> items, Vector2 where)
        {
            _pages.Clear(); foreach (var page in RadialMath.Pages(items)) _pages.Add(page); _page = 0; _hover = -1; if (_tip != null) _tip.style.display = DisplayStyle.None;
            var screen = new Vector2(Mathf.Max(ui.Root.layout.width, 640f), Mathf.Max(ui.Root.layout.height, 480f)); var kept = RadialMath.KeepOnScreen(new System.Numerics.Vector2(where.x, where.y), Outer, new System.Numerics.Vector2(screen.x, screen.y)); _centre = new Vector2(kept.X, kept.Y);
            if (parent == null) ui.WindowLayer.Add(this); Rebuild(); MarkDirtyRepaint(); Focus();
            var seconds = OpenSeconds; style.transformOrigin = new TransformOrigin(_centre.x, _centre.y);
            if (seconds > 0f) { style.opacity = 0f; style.scale = new Scale(new Vector3(.85f, .85f, 1f)); experimental.animation.Start(0f, 1f, Mathf.RoundToInt(seconds * 1000f), (e, v) => { e.style.opacity = v; var k = .85f + .15f * v; e.style.scale = new Scale(new Vector3(k, k, 1f)); }); }
            else { style.opacity = 1f; style.scale = new Scale(Vector3.one); }
        }

        public void Close() { RemoveFromHierarchy(); }

        private void Rebuild()
        {
            foreach (var label in _labels) label.RemoveFromHierarchy(); _labels.Clear();
            var items = Slices;
            for (var i = 0; i < items.Count; i++)
            {
                var p = RadialMath.LabelPosition(i, items.Count, (Inner + Outer) / 2f); var label = new Label((i + 1) + "  " + items[i].Label); label.pickingMode = PickingMode.Ignore; label.style.position = Position.Absolute; label.style.width = 120; label.style.height = 24; label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.left = _centre.x + p.X - 60; label.style.top = _centre.y + p.Y - 12; label.style.fontSize = 14; label.AddToClassList("lv-radial-label"); label.AddToClassList("lv-bold"); label.EnableInClassList("lv-radial-off", !items[i].Enabled); Add(label); _labels.Add(label);
            }
            if (_pages.Count > 1) { var more = new Label("Próxima ▸ (" + (_page + 1) + "/" + _pages.Count + ")"); more.pickingMode = PickingMode.Ignore; more.style.position = Position.Absolute; more.style.left = _centre.x - 40; more.style.top = _centre.y - 10; more.style.fontSize = 11; more.style.width = 80; more.style.unityTextAlign = TextAnchor.MiddleCenter; more.AddToClassList("lv-radial-label"); Add(more); _labels.Add(more); }
        }

        private void SetHover(int index)
        {
            if (index == _hover) return; _hover = index; MarkDirtyRepaint();
            for (var i = 0; i < _labels.Count; i++) _labels[i].EnableInClassList("lv-radial-hot", i == index && i < Slices.Count && Slices[i].Enabled);
            if (_tip == null) { _tip = new Label { pickingMode = PickingMode.Ignore }; _tip.AddToClassList("lv-glass"); _tip.AddToClassList("lv-small"); _tip.style.position = Position.Absolute; _tip.style.maxWidth = 260; _tip.style.whiteSpace = WhiteSpace.Normal; _tip.style.unityTextAlign = TextAnchor.MiddleCenter; }
            if (_tip.parent != this) Add(_tip);
            var show = Tooltips && index >= 0 && index < Slices.Count; _tip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) { _tip.text = TipOf(Slices[index]); _tip.style.left = _centre.x - 130; _tip.style.width = 260; _tip.style.top = _centre.y + Outer + 8; }
        }

        /// <summary>Runs a slice (or, in the middle of a ring with pages, goes to the next page). Returns whether something was run.</summary>
        public bool Choose(int index, Vector2? at = null)
        {
            if (index < 0) { if (at is { } p && _pages.Count > 1 && (p - _centre).magnitude < Inner) { _page = (_page + 1) % _pages.Count; _hover = -1; Rebuild(); MarkDirtyRepaint(); return false; } if (at != null) Close(); return false; }
            var item = Slices[index]; if (!item.Enabled) return false; Close(); item.Run(); return true;
        }

        private void OnKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); return; }
            if (e.keyCode == KeyCode.Tab || e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.DownArrow) { SetHover(RadialMath.Step(_hover, 1, Slices.Count)); e.StopPropagation(); }
            else if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.UpArrow) { SetHover(RadialMath.Step(_hover, -1, Slices.Count)); e.StopPropagation(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Choose(_hover); e.StopPropagation(); }
            else if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha8) { Choose(RadialMath.SliceForKey(e.keyCode - KeyCode.Alpha0, Slices.Count)); e.StopPropagation(); }
        }

        private static readonly CustomStyleProperty<Color> FillProperty = new CustomStyleProperty<Color>("--lv-radial-fill"), HoverProperty = new CustomStyleProperty<Color>("--lv-radial-hover"), LineProperty = new CustomStyleProperty<Color>("--lv-radial-line");

        private void Draw(MeshGenerationContext context)
        {
            var count = Slices.Count; if (count == 0) return; var painter = context.painter2D;
            var paper = customStyle.TryGetValue(FillProperty, out var p) ? p : new Color(.97f, .95f, .89f); var chosen = customStyle.TryGetValue(HoverProperty, out var h) ? h : new Color(.08f, .42f, .39f); var wood = customStyle.TryGetValue(LineProperty, out var w) ? w : new Color(.61f, .42f, .27f); var sun = new Color(.89f, .6f, .18f);
            for (var i = 0; i < count; i++)
            {
                var centre = AngleOf(i, count); var half = Mathf.PI / count; var enabled = Slices[i].Enabled;
                var hot = i == _hover && enabled; painter.fillColor = hot ? chosen : new Color(paper.r, paper.g, paper.b, Opacity); painter.strokeColor = hot ? Color.Lerp(wood, sun, Glow) : wood; painter.lineWidth = hot ? 1.5f + 2f * Glow : 1.5f;
                painter.BeginPath(); painter.Arc(_centre, Outer, new Angle(centre - half + .01f, AngleUnit.Radian), new Angle(centre + half - .01f, AngleUnit.Radian)); painter.Arc(_centre, Inner, new Angle(centre + half - .01f, AngleUnit.Radian), new Angle(centre - half + .01f, AngleUnit.Radian), ArcDirection.CounterClockwise); painter.ClosePath(); painter.Fill(); painter.Stroke();
            }
        }
    }
}
