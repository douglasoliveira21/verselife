using System;
using System.Collections.Generic;
using System.Globalization;
using LifeVerse.Shared.Terrain;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The Land tool: the six brushes of the viewer with size and strength, the parcel panel, and beside it what only LifeVerse has (paint, level, erode, brush shape, undo, the sea).</summary>
    public sealed partial class BuildWindow
    {
        private static readonly (string Name, TerrainTool Tool)[] Brushes = { ("radio flatten", TerrainTool.Flatten), ("radio raise", TerrainTool.Raise), ("radio lower", TerrainTool.Lower), ("radio smooth", TerrainTool.Smooth), ("radio noise", TerrainTool.Noise), ("radio revert", TerrainTool.Restore) };
        private TerrainPane _terrain;
        /// <summary>The terrain tools inside the window (null when the window has none).</summary>
        public TerrainPane Terrain => _terrain;

        private void BuildLand()
        {
            var editor = _links.Terrain;
            _ui.Unavailable("radio select land", "marcar uma área no chão chega numa próxima fase; para demarcar use Gerenciar terrenos."); _ui.Unavailable("button apply to selection", "aplicar a uma área precisa de uma mensagem nova no servidor de ilha.");
            _ui.Unavailable("checkbox show owners", "desenhar os donos no chão chega numa próxima fase.");
            foreach (var (name, tool) in Brushes) OnOption(name, () => _terrain?.Select(tool));
            if (_ui.Slider("slider brush size") is { } size) { size.lowValue = 1f; size.highValue = TerrainMap.MaxBrushRadius; size.RegisterValueChangedCallback(e => { if (!_filling && editor != null) { editor.Radius = e.newValue; editor.Refresh(); } }); }
            if (_ui.Slider("slider force") is { } force) { force.lowValue = 1f; force.highValue = 100f; force.RegisterValueChangedCallback(e => { if (!_filling && editor != null) { editor.StrengthPercent = e.newValue; editor.Refresh(); } }); }
            OnPress("button about land", () => _links.AboutLand?.Invoke());
            foreach (var name in new[] { "button subdivide land", "button join land", "button buy land", "button abandon land" }) { OnPress(name, () => _links.ManageLand?.Invoke()); if (_ui.Button(name) is { } button) button.tooltip = "Abre Gerenciar terrenos"; }
            if (editor != null) _terrain = new TerrainPane(this, _c, editor, _land);
        }

        private void RefreshLand()
        {
            var editor = _links.Terrain; var land = Mode == BuildMode.Land;
            if (_ui.Words("label_area") is { } area) { area.text = "Dono, tamanho e regras:"; area.tooltip = area.text; }
            _ui.Visible("label_area_price", false);
            _ui.Enable("button about land", _links.AboutLand != null); foreach (var name in new[] { "button subdivide land", "button join land", "button buy land", "button abandon land" }) _ui.Enable(name, _links.ManageLand != null);
            if (!land) return;
            Options(new[] { "radio flatten", "radio raise", "radio lower", "radio smooth", "radio noise", "radio revert" }, name => editor != null && Array.Exists(Brushes, b => b.Name == name && b.Tool == editor.Tool));
            foreach (var (name, _) in Brushes) _ui.Enable(name, editor != null);
            _ui.Slider("slider brush size")?.SetValueWithoutNotify(editor?.Radius ?? 4f); _ui.Slider("slider force")?.SetValueWithoutNotify(editor?.StrengthPercent ?? 25f);
            _ui.Enable("slider brush size", editor != null); _ui.Enable("slider force", editor != null);
            _terrain?.Refresh();
        }
    }

    /// <summary>The LifeVerse extras of the Land tool, to the right of the parcel panel.</summary>
    public sealed class TerrainPane
    {
        private readonly BuildWindow _window; private readonly TerrainEditor _t; private readonly Dictionary<TerrainTool, Toggle> _tools = new Dictionary<TerrainTool, Toggle>(); private readonly List<Button> _layers = new List<Button>();
        private readonly Toggle _square; private readonly BuildSpinner _soft, _level, _sea; private readonly Button _useHeight; private readonly Label _layerName; private readonly VisualElement _levelBox, _paintBox, _islandBox;

        public string StatusText => _t.Status;
        public bool LevelVisible => _levelBox.style.display != DisplayStyle.None;
        public bool PaintVisible => _paintBox.style.display != DisplayStyle.None;
        public bool IslandVisible => _islandBox.style.display != DisplayStyle.None;
        public int LayerButtons => _layers.Count;

        internal TerrainPane(BuildWindow window, ApiContext context, TerrainEditor editor, VisualElement into)
        {
            _window = window; _t = editor;
            var column = new VisualElement { name = "terrain-extras", pickingMode = PickingMode.Ignore }; column.style.position = Position.Absolute; column.style.left = 252; column.style.top = 4; column.style.width = 164; into.Add(column);
            column.Add(Heading("Mais ferramentas"));
            foreach (var (tool, text) in new[] { (TerrainTool.Paint, "Pintar"), (TerrainTool.Level, "Nivelar na altura"), (TerrainTool.Erode, "Erodir") })
            { var captured = tool; var radio = new Toggle { text = text }; radio.AddToClassList("bt-radio"); radio.style.height = 22; radio.RegisterValueChangedCallback(e => { if (!e.newValue) { radio.SetValueWithoutNotify(true); return; } Select(captured); }); _tools[tool] = radio; column.Add(radio); }
            _square = new Toggle { text = "Pincel quadrado" }; _square.AddToClassList("bt-check"); _square.style.height = 22; _square.style.marginTop = 6; _square.RegisterValueChangedCallback(e => { _t.Square = e.newValue; _t.Refresh(); }); column.Add(_square);
            _soft = Spin(column, "Suavidade %", 0f, 100f, 5f, "0", v => { _t.SoftPercent = v; _t.Refresh(); });

            _levelBox = new VisualElement(); _level = Spin(_levelBox, "Altura (m)", TerrainMap.MinHeight + 1f, TerrainMap.MaxHeight, .5f, "0.0", v => { _t.Level = v; _t.Refresh(); });
            _useHeight = Small("Altura sob o cursor", () => _t.UseHoverHeight()); _levelBox.Add(_useHeight); column.Add(_levelBox);

            _paintBox = new VisualElement(); var swatches = new VisualElement(); swatches.style.flexDirection = FlexDirection.Row; swatches.style.flexWrap = Wrap.Wrap; swatches.style.marginTop = 4;
            for (var i = 0; i < TerrainView.Layers.Length; i++)
            {
                var index = i; var layer = TerrainView.Layers[i]; var swatch = new Button(() => SelectLayer(index)) { tooltip = layer.Name }; swatch.AddToClassList("lv-btn"); swatch.AddToClassList("bt-picture"); swatch.style.width = 36; swatch.style.height = 30; swatch.style.marginRight = 4; swatch.style.marginBottom = 4; swatch.style.minHeight = 0; swatch.style.backgroundColor = Color.Lerp(layer.Dark, layer.Light, .5f); _layers.Add(swatch); swatches.Add(swatch);
            }
            _paintBox.Add(swatches); _layerName = UiKit.Text(TerrainView.Layers[0].Name, "lv-small"); _paintBox.Add(_layerName); column.Add(_paintBox);

            var history = UiKit.Row(Small("Desfazer", () => _t.Undo()), Small("Refazer", () => _t.Redo())); history.style.marginTop = 8; column.Add(history);
            _islandBox = new VisualElement(); _islandBox.style.marginTop = 10; _islandBox.Add(Heading("Ilha (editores)"));
            _sea = Spin(_islandBox, "Mar (m)", TerrainMap.MinSeaLevel, TerrainMap.MaxSeaLevel, .1f, "0.0", _ => { }); _sea.Show(TerrainMap.DefaultSeaLevel);
            _islandBox.Add(Small("Aplicar o mar", () => { if (_sea.Value is { } level) _t.ApplySeaLevel(level); else _t.Say("Digite o nível do mar em metros."); }));
            _islandBox.Add(UiKit.Row(Small("Exportar", () => _t.Export()), Small("Importar…", () => new FilePickerWindow(context.Ui, "Escolha o mapa de alturas (RAW 16 bits)", new[] { ".r16", ".raw" }, path => _t.Import(path)).Open()))); column.Add(_islandBox);
        }

        private static Label Heading(string text) { var label = UiKit.Text(text, "lv-small"); label.AddToClassList("bt-title"); label.style.marginBottom = 4; return label; }
        private static Button Small(string text, Action run) { var button = UiKit.Button(text, run); button.AddToClassList("bt-btn"); button.style.height = 26; button.style.marginTop = 3; button.style.marginRight = 4; button.style.flexGrow = 1; return button; }
        private static BuildSpinner Spin(VisualElement into, string label, float min, float max, float step, string format, Action<float> run)
        {
            var spinner = new BuildSpinner(label, 84f, label) { Min = min, Max = max, Step = step, Format = format }; spinner.style.height = 26; spinner.style.marginTop = 4; spinner.Committed += run; into.Add(spinner); return spinner;
        }

        public void Select(TerrainTool tool) { _t.Tool = tool; _t.Refresh(); _window.Refresh(); }
        public void SelectLayer(int layer) { _t.Layer = Mathf.Clamp(layer, 0, TerrainView.Layers.Length - 1); _window.Refresh(); }
        /// <summary>Closes the whole build window.</summary>
        public void Close() => _window.Close();

        public void Refresh()
        {
            foreach (var pair in _tools) pair.Value.SetValueWithoutNotify(pair.Key == _t.Tool);
            _square.SetValueWithoutNotify(_t.Square); _soft.Show(_t.SoftPercent); _level.Show(_t.Level);
            _levelBox.style.display = _t.Tool == TerrainTool.Level ? DisplayStyle.Flex : DisplayStyle.None; _paintBox.style.display = _t.Tool == TerrainTool.Paint ? DisplayStyle.Flex : DisplayStyle.None; _islandBox.style.display = _t.IslandEditor ? DisplayStyle.Flex : DisplayStyle.None;
            _layerName.text = TerrainView.Layers[Mathf.Clamp(_t.Layer, 0, TerrainView.Layers.Length - 1)].Name;
            for (var i = 0; i < _layers.Count; i++) { var chosen = i == _t.Layer; _layers[i].style.borderTopWidth = _layers[i].style.borderBottomWidth = _layers[i].style.borderLeftWidth = _layers[i].style.borderRightWidth = chosen ? 4 : 1; _layers[i].style.borderTopColor = _layers[i].style.borderBottomColor = _layers[i].style.borderLeftColor = _layers[i].style.borderRightColor = chosen ? new Color(.78f, .29f, .16f) : new Color(0f, 0f, 0f, .35f); }
        }
    }
}
