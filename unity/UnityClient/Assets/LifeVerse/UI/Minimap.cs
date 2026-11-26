using System;
using System.Collections.Generic;
using LifeVerse.Shared.Terrain;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The map in the corner: the island seen from above (north up) painted from the terrain heights, deep and shallow water by the sea level, then sand, grass, rock and snow by height,
    /// with a white arrow for the person (turned the way they face) and a teal dot for each other person. It is repainted when the terrain or the sea level change.
    /// </summary>
    public sealed class Minimap : VisualElement
    {
        public const int Pixels = 128; public const float Size = 180f;
        private Texture2D _texture; private TerrainMap _map; private long _revision = -1; private float _sea = float.NaN;
        private readonly VisualElement _self; private readonly List<VisualElement> _others = new List<VisualElement>(); private readonly VisualElement _picture;
        public Texture2D Texture => _texture;
        public int OtherDots { get; private set; }

        public Minimap()
        {
            name = "minimap"; AddToClassList("lv-glass"); style.width = Size + 12; style.height = Size + 12; style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 5; pickingMode = PickingMode.Position;
            _picture = new VisualElement { pickingMode = PickingMode.Ignore }; _picture.style.width = Size; _picture.style.height = Size; _picture.style.borderTopLeftRadius = _picture.style.borderTopRightRadius = _picture.style.borderBottomLeftRadius = _picture.style.borderBottomRightRadius = 9; _picture.style.overflow = Overflow.Hidden; Add(_picture);
            _self = new VisualElement { pickingMode = PickingMode.Ignore }; _self.style.position = Position.Absolute; _self.style.width = 12; _self.style.height = 12; _self.style.backgroundColor = Color.white; _self.style.borderTopLeftRadius = _self.style.borderTopRightRadius = _self.style.borderBottomLeftRadius = _self.style.borderBottomRightRadius = 6; _self.style.borderTopWidth = _self.style.borderBottomWidth = _self.style.borderLeftWidth = _self.style.borderRightWidth = 2; _self.style.borderTopColor = _self.style.borderBottomColor = _self.style.borderLeftColor = _self.style.borderRightColor = new Color(.07f, .95f, .88f); _picture.Add(_self);
        }

        /// <summary>The colour of a place of the map: by how deep the water is, or how high the land.</summary>
        public static Color32 ColourOf(float height, float sea)
        {
            var depth = sea - height;
            if (depth > 0f) { var t = Mathf.Clamp01(depth / 6f); return (Color32)Color.Lerp(new Color(.25f, .62f, .75f), new Color(.04f, .16f, .34f), t); }
            var above = height - sea;
            if (above < .8f) return new Color32(222, 204, 150, 255);                                           // sand
            if (above < 10f) return (Color32)Color.Lerp(new Color(.36f, .62f, .3f), new Color(.25f, .45f, .22f), Mathf.Clamp01(above / 10f));   // grass
            if (above < 24f) return (Color32)Color.Lerp(new Color(.45f, .4f, .34f), new Color(.6f, .58f, .55f), Mathf.Clamp01((above - 10f) / 14f));   // rock
            return new Color32(240, 244, 248, 255);                                                              // snow
        }

        /// <summary>Repaints the picture from a map (only when it changed since the last time).</summary>
        public bool Paint(TerrainMap map)
        {
            if (map == null) return false;
            if (map == _map && map.Revision == _revision && Mathf.Approximately(map.SeaLevel, _sea)) return false;
            if (_texture == null) _texture = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var half = map.HalfExtent; var pixels = new Color32[Pixels * Pixels];
            for (var j = 0; j < Pixels; j++) for (var i = 0; i < Pixels; i++)
            {
                var x = -half + (i + .5f) / Pixels * half * 2f; var z = -half + (j + .5f) / Pixels * half * 2f;   // north (-z of the game) is the top row
                pixels[(Pixels - 1 - j) * Pixels + i] = ColourOf(map.SampleHeight(x, -z), map.SeaLevel);
            }
            _texture.SetPixels32(pixels); _texture.Apply(); _picture.style.backgroundImage = new StyleBackground(_texture); _map = map; _revision = map.Revision; _sea = map.SeaLevel; return true;
        }

        /// <summary>Where a place of the game is on the picture (pixels from its top-left corner), or null when it is off the map.</summary>
        public Vector2? PointOf(Vector3 game)
        {
            if (_map == null) return null; var half = _map.HalfExtent; var u = (game.x + half) / (half * 2f); var v = (game.z + half) / (half * 2f);
            if (u < 0f || u > 1f || v < 0f || v > 1f) return null; return new Vector2(u * Size, v * Size);
        }

        /// <summary>How many metres the mini-map shows from edge to edge (the whole island when it is smaller), the coordinates under it and the dots of the other avatars.</summary>
        public float ZoomMetres = 300f; public bool ShowCoords = true, ShowAvatars = true;
        /// <summary>"Mini-mapa gira com a câmera" (the way one looks is up; the map turns into a disc), "Nomes no mini-mapa" and "Construções no mini-mapa".</summary>
        public bool Rotate, ShowNames = true, ShowBuildings = true;
        /// <summary>Where the camera looks, in degrees (Unity yaw: 0 is north, 90 east).</summary>
        public float Heading;
        /// <summary>The outlines of the buildings, as squares of the ground in game metres (x, z, width, depth), set by the game.</summary>
        public readonly List<Rect> Buildings = new List<Rect>();
        public int BuildingsDrawn { get; private set; }
        public int NamesShown { get; private set; }
        private VisualElement _buildingLayer; private readonly List<Label> _names = new List<Label>();
        private Label _coords; private float _span = 1f; private Vector2 _origin;
        public string CoordsText => _coords?.text ?? "";
        public float SpanMetres => _span;

        /// <summary>The window of the island the map shows: a square of <see cref="ZoomMetres"/> around the person (kept inside the island), or the whole island.</summary>
        private void Frame(Vector3 self)
        {
            if (_map == null) return; var whole = _map.HalfExtent * 2f; _span = Mathf.Clamp(ZoomMetres, 20f, whole); var half = _span / 2f;
            var cx = Mathf.Clamp(self.x, -_map.HalfExtent + half, _map.HalfExtent - half); var cz = Mathf.Clamp(self.z, -_map.HalfExtent + half, _map.HalfExtent - half); _origin = new Vector2(cx - half, cz - half);
            var k = whole / _span; _picture.style.backgroundSize = new BackgroundSize(Size * k, Size * k);
            _picture.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, -(_origin.x + _map.HalfExtent) / whole * Size * k); _picture.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, -(_origin.y + _map.HalfExtent) / whole * Size * k);
        }

        private Vector2? InFrame(Vector3 game) { var u = (game.x - _origin.x) / _span; var v = (game.z - _origin.y) / _span; return u < 0f || u > 1f || v < 0f || v > 1f ? (Vector2?)null : new Vector2(u * Size, v * Size); }

        /// <summary>Turns the map to where the camera looks (every frame, so it turns smoothly).</summary>
        public void Turn(float heading)
        {
            Heading = heading; _picture.style.rotate = new Rotate(Rotate ? -heading : 0f);
            foreach (var tag in _names) tag.style.rotate = new Rotate(Rotate ? heading : 0f);
        }

        private void DrawBuildings(MeshGenerationContext context)
        {
            var drawn = 0; if (!ShowBuildings || _map == null) { BuildingsDrawn = 0; return; }
            var p = context.painter2D; p.fillColor = new Color(.92f, .9f, .86f, .55f); p.strokeColor = new Color(.15f, .13f, .12f, .8f); p.lineWidth = 1f;
            foreach (var b in Buildings)
            {
                var a = InFrame(new Vector3(b.xMin, 0f, b.yMin)); var c = InFrame(new Vector3(b.xMax, 0f, b.yMax)); if (a == null && c == null) continue;
                var u0 = ((b.xMin - _origin.x) / _span) * Size; var v0 = ((b.yMin - _origin.y) / _span) * Size; var w = Mathf.Max(2f, b.width / _span * Size); var h = Mathf.Max(2f, b.height / _span * Size);
                p.BeginPath(); p.MoveTo(new Vector2(u0, v0)); p.LineTo(new Vector2(u0 + w, v0)); p.LineTo(new Vector2(u0 + w, v0 + h)); p.LineTo(new Vector2(u0, v0 + h)); p.ClosePath(); p.Fill(); p.Stroke(); drawn++;
            }
            BuildingsDrawn = drawn;
        }

        public void Place(Vector3 self, IReadOnlyList<Vector3> others, IReadOnlyList<string> names = null)
        {
            if (_buildingLayer == null) { _buildingLayer = new VisualElement { pickingMode = PickingMode.Ignore }; _buildingLayer.style.position = Position.Absolute; _buildingLayer.style.left = 0; _buildingLayer.style.top = 0; _buildingLayer.style.width = Size; _buildingLayer.style.height = Size; _buildingLayer.generateVisualContent += DrawBuildings; _picture.Insert(0, _buildingLayer); }
            var radius = Rotate ? Size / 2f : 9f; _picture.style.borderTopLeftRadius = _picture.style.borderTopRightRadius = _picture.style.borderBottomLeftRadius = _picture.style.borderBottomRightRadius = radius;
            Turn(Heading); _buildingLayer.MarkDirtyRepaint();
            Frame(self);
            if (_coords == null) { _coords = new Label { pickingMode = PickingMode.Ignore }; _coords.AddToClassList("lv-small"); _coords.style.position = Position.Absolute; _coords.style.left = 8; _coords.style.bottom = 6; _coords.style.color = Color.white; _coords.style.unityTextOutlineColor = Color.black; _coords.style.unityTextOutlineWidth = .3f; Add(_coords); }
            _coords.style.display = ShowCoords ? DisplayStyle.Flex : DisplayStyle.None; _coords.text = Mathf.RoundToInt(self.x) + ", " + Mathf.RoundToInt(self.y) + ", " + Mathf.RoundToInt(self.z);
            if (!ShowAvatars) others = new List<Vector3>();
            if ((_map != null ? InFrame(self) : PointOf(self)) is { } p) { _self.style.display = DisplayStyle.Flex; _self.style.left = p.x - 6; _self.style.top = p.y - 6; } else _self.style.display = DisplayStyle.None;
            var shown = 0;
            foreach (var other in others)
            {
                if ((_map != null ? InFrame(other) : PointOf(other)) is not { } q) continue;
                if (shown >= _others.Count) { var dot = new VisualElement { pickingMode = PickingMode.Ignore }; dot.style.position = Position.Absolute; dot.style.width = 7; dot.style.height = 7; dot.style.backgroundColor = new Color(.07f, .95f, .88f); dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 4; _picture.Add(dot); _others.Add(dot); }
                var element = _others[shown++]; element.style.display = DisplayStyle.Flex; element.style.left = q.x - 3.5f; element.style.top = q.y - 3.5f;
            }
            for (var i = shown; i < _others.Count; i++) _others[i].style.display = DisplayStyle.None; OtherDots = shown;
            var named = 0;
            if (ShowNames && ShowAvatars && names != null)
                for (var i = 0; i < others.Count && i < names.Count; i++)
                {
                    if (string.IsNullOrEmpty(names[i]) || (_map != null ? InFrame(others[i]) : PointOf(others[i])) is not { } q) continue;
                    if (named >= _names.Count) { var label = new Label { pickingMode = PickingMode.Ignore }; label.AddToClassList("lv-small"); label.style.position = Position.Absolute; label.style.color = Color.white; label.style.fontSize = 10; label.style.unityTextOutlineColor = Color.black; label.style.unityTextOutlineWidth = .6f; _picture.Add(label); _names.Add(label); }
                    var tag = _names[named++]; tag.text = names[i]; tag.style.display = DisplayStyle.Flex; tag.style.left = q.x + 5; tag.style.top = q.y - 7; tag.style.rotate = new Rotate(Rotate ? Heading : 0f);
                }
            for (var i = named; i < _names.Count; i++) _names[i].style.display = DisplayStyle.None; NamesShown = named;
        }
    }

    /// <summary>The performance overlay (F3): frames per second, the frame time and the slowest frame of the last seconds, memory, and what the world is drawing (people, objects, how many are culled).</summary>
    public sealed class PerformanceOverlay : VisualElement
    {
        private readonly Label _text; private float _accumulated, _worst; private int _frames; private float _window;
        public float Fps { get; private set; }
        public float FrameMs { get; private set; }
        public float WorstMs { get; private set; }
        public Func<string> Extra;

        public PerformanceOverlay()
        {
            name = "performance"; AddToClassList("lv-glass"); pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.left = 18; style.top = 70; style.paddingTop = style.paddingBottom = 6;
            _text = UiKit.Text("", "lv-small"); _text.style.whiteSpace = WhiteSpace.Pre; Add(_text);
        }

        /// <summary>One frame of the game: the numbers are averaged over half a second and shown.</summary>
        public void Frame(float deltaSeconds)
        {
            _accumulated += deltaSeconds; _frames++; _worst = Mathf.Max(_worst, deltaSeconds); _window += deltaSeconds; if (_window < .5f) return;
            Fps = _frames / _accumulated; FrameMs = _accumulated / _frames * 1000f; WorstMs = _worst * 1000f; _accumulated = 0f; _frames = 0; _worst = 0f; _window = 0f;
            _text.text = $"{Fps:F0} quadros/s   {FrameMs:F1} ms   pior {WorstMs:F1} ms\nmemória {GC.GetTotalMemory(false) / (1024f * 1024f):F0} MB" + (Extra != null ? "\n" + Extra() : "");
        }
    }
}
