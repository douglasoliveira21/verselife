using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LifeVerse.Shared.Terrain;
using LifeVerse.Unity.World;
using LifeVerse.Wire;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The terrain tools (T): raise, lower, level, flatten, smooth, roughen, erode, restore and paint the eight ground materials, with a round or square brush of any size, strength and edge. Edits are only sent to the island, which
    /// checks who may sculpt (island editors anywhere) and replicates them; the ring over the ground shows where the brush is and the ground changes when the island says so. Undo and redo are the island's too, as is the sea level.
    /// </summary>
    public sealed class TerrainEditor : VisualElement
    {
        public const float DabInterval = .08f;
        private readonly ApiContext _c; private readonly Func<WorldClient> _client; private readonly Func<Camera> _camera; private readonly Func<TerrainView> _terrain;
        private Vector3? _hover; private float _sinceDab; private readonly List<Vector2> _ring = new List<Vector2>(); private string _lastRejection;

        public bool Active { get; private set; }
        public TerrainTool Tool { get; set; } = TerrainTool.Raise;
        public int Layer { get; set; }
        public bool Square { get; set; }
        public float Radius { get; set; } = 4f;
        public float StrengthPercent { get; set; } = 25f;
        public float SoftPercent { get; set; } = 100f;
        public float Level { get; set; } = 2f;
        public int Dabs { get; private set; }
        public string Status { get; private set; } = "";
        public event Action Changed;
        public Vector3? Hover => _hover;

        public TerrainEditor(ApiContext context, Func<WorldClient> client, Func<Camera> camera, Func<TerrainView> terrain)
        {
            _c = context; _client = client; _camera = camera; _terrain = terrain; name = "terrain-overlay"; pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0; generateVisualContent += Draw;
        }

        public void SetActive(bool active)
        {
            if (active == Active) return; Active = active; _hover = null;
            if (active) { if (parent == null) _c.Ui.HudLayer.Add(this); Refresh(); } else RemoveFromHierarchy();
            Changed?.Invoke();
        }

        public bool CanSculpt => _client() != null && _client().CanEditTerrain;
        public float StrengthValue() { var percent = StrengthPercent / 100f; return Tool == TerrainTool.Raise || Tool == TerrainTool.Lower || Tool == TerrainTool.Noise ? percent * .4f : percent; }
        public BrushOptions Brush => new BrushOptions(Square, 1f - SoftPercent / 100f, Tool == TerrainTool.Level ? Level : (float?)null);

        /// <summary>What one dab at a point sends: tool, radius, strength and the layer packed with the brush options.</summary>
        public SubmitTerrainEditMessage RequestAt(float x, float z) => new SubmitTerrainEditMessage((int)Tool, x, z, Radius, StrengthValue(), BrushCodec.Pack(Layer, Brush));

        public bool SubmitAt(float x, float z)
        {
            var client = _client(); if (client == null || !client.Connected) { Say("Entre numa ilha para editar o terreno."); return false; }
            if (!CanSculpt) { Say("Você não pode editar o terreno aqui: só os editores da ilha (a edição por terreno ainda depende da ilha liberar)."); return false; }
            if (client.Send(RequestAt(x, z))) { Dabs++; return true; } return false;
        }

        public bool Undo() { if (!CanSculpt) { Say("Você não pode editar o terreno aqui."); return false; } return _client().Send(new UndoTerrainMessage()); }
        public bool Redo() { if (!CanSculpt) { Say("Você não pode editar o terreno aqui."); return false; } return _client().Send(new RedoTerrainMessage()); }

        public bool ApplySeaLevel(float level)
        {
            if (!CanSculpt) { Say("Só os editores da ilha mudam o mar."); return false; }
            if (level < TerrainMap.MinSeaLevel || level > TerrainMap.MaxSeaLevel) { Say("O mar fica entre " + TerrainMap.MinSeaLevel.ToString("0.#", CultureInfo.InvariantCulture) + " e " + TerrainMap.MaxSeaLevel.ToString("0.#", CultureInfo.InvariantCulture) + " m."); return false; }
            return _client().Send(new SubmitSeaLevelMessage(level));
        }

        public string ExportPath { get; private set; }

        /// <summary>Saves the heights as a RAW 16-bit file (the one the island imports).</summary>
        public string Export()
        {
            var terrain = _terrain(); if (terrain == null) return null; var map = terrain.Map; var folder = Path.Combine(Application.persistentDataPath, "exports"); Directory.CreateDirectory(folder);
            ExportPath = Path.Combine(folder, "heightmap-" + map.VertexCount + "x" + map.VertexCount + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".r16"); File.WriteAllBytes(ExportPath, map.ExportHeights16());
            Say("Mapa exportado (RAW 16 bits, " + map.VertexCount + "×" + map.VertexCount + "): " + ExportPath); return ExportPath;
        }

        /// <summary>Replaces the heights with a RAW 16-bit file: it must have exactly the island's number of samples.</summary>
        public bool Import(string path)
        {
            if (!CanSculpt) { Say("Só os editores da ilha importam mapas."); return false; }
            byte[] bytes; try { bytes = File.ReadAllBytes(path); } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Say("Não foi possível ler o arquivo: " + exception.Message); return false; }
            var count = _terrain().Map.VertexCount; if (bytes.Length != count * count * 2) { Say("Arquivo inválido: esperado " + count + "×" + count + " amostras de 16 bits (" + count * count * 2 + " bytes), mas tem " + bytes.Length + "."); return false; }
            Say("Mapa enviado à ilha. As bordas (fundo do mar) não mudam."); return _client().Send(new SubmitTerrainImportMessage(bytes));
        }

        public void Say(string text) { Status = text; Changed?.Invoke(); }

        public void Refresh() { Say(_client() == null || !_client().Connected ? "Entre numa ilha para editar o terreno." : !CanSculpt ? "Você não pode editar o terreno aqui (só os editores da ilha)." : Tool == TerrainTool.Paint ? "Segure o botão esquerdo para pintar o material escolhido." : Tool == TerrainTool.Level ? "Escolha a altura do nível e segure o botão esquerdo para levar o solo até ela." : Tool == TerrainTool.Restore ? "Segure o botão esquerdo para devolver o terreno original." : "Segure o botão esquerdo sobre o terreno."); }

        public static string RejectionText(string code)
        {
            switch (code) { case "no_permission": return "Sem permissão para editar o terreno."; case "rate_limit": return "Muitas edições seguidas; aguarde um instante."; case "nothing_to_undo": return "Nada para desfazer."; case "nothing_to_redo": return "Nada para refazer."; case "invalid_heightmap": return "A ilha recusou o mapa de alturas."; case "invalid_edit": return "A ilha recusou a pincelada."; default: return "Edição recusada: " + code; }
        }

        /// <summary>Uses the ground height under the cursor as the level.</summary>
        public bool UseHoverHeight() { if (_hover == null) { Say("Passe o cursor sobre o terreno e aperte de novo."); return false; } Level = Mathf.Clamp(_terrain().Map.SampleHeight(_hover.Value.x, _hover.Value.z), TerrainMap.MinHeight + 1f, TerrainMap.MaxHeight); Changed?.Invoke(); return true; }

        // ---- every frame ----

        public void Tick(float dt)
        {
            if (!Active) return; var client = _client(); var camera = _camera(); var terrain = _terrain();
            if (client != null && client.LastTerrainRejection != null && client.LastTerrainRejection != _lastRejection) { _lastRejection = client.LastTerrainRejection; Say(RejectionText(_lastRejection)); }
            _hover = null; var mouse = Mouse.current; var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.ctrlKey.isPressed) { if (keyboard.zKey.wasPressedThisFrame) { if (keyboard.shiftKey.isPressed) Redo(); else Undo(); } if (keyboard.yKey.wasPressedThisFrame) Redo(); }
            if (mouse != null && camera != null && terrain != null)
            {
                var screen = mouse.position.ReadValue();
                if (!UiKit.IsOver(_c.Ui.Root, screen) && terrain.Raycast(camera.ScreenPointToRay(screen)) is Vector3 hit) _hover = GameSpace.ToGame(hit);
                _sinceDab += dt; if (_hover != null && mouse.leftButton.isPressed && _sinceDab >= DabInterval) { _sinceDab = 0f; SubmitAt(_hover.Value.x, _hover.Value.z); }
            }
            RebuildRing(); MarkDirtyRepaint();
        }

        private void RebuildRing()
        {
            _ring.Clear(); var camera = _camera(); var terrain = _terrain(); if (_hover == null || camera == null || terrain == null) return; var scale = Mathf.Max(.01f, _c.Ui.Panel.scale); var centre = _hover.Value; const int points = 72;
            for (var i = 0; i <= points; i++)
            {
                var t = i / (float)points; float x, z;
                if (Square) { var side = t * 4f; var s = side % 1f; var corner = (int)side % 4; var lx = corner == 0 ? -1f + 2f * s : corner == 1 ? 1f : corner == 2 ? 1f - 2f * s : -1f; var lz = corner == 0 ? -1f : corner == 1 ? -1f + 2f * s : corner == 2 ? 1f : 1f - 2f * s; x = centre.x + lx * Radius; z = centre.z + lz * Radius; }
                else { var angle = t * Mathf.PI * 2f; x = centre.x + Mathf.Cos(angle) * Radius; z = centre.z + Mathf.Sin(angle) * Radius; }
                var y = terrain.GroundHeight(x, z) ?? centre.y; var world = GameSpace.ToUnity(x, y + .15f, z); var screen = camera.WorldToScreenPoint(world); if (screen.z <= 0f) { _ring.Add(new Vector2(float.NaN, 0)); continue; }
                _ring.Add(new Vector2(screen.x / scale, (Screen.height - screen.y) / scale));
            }
        }

        private void Draw(MeshGenerationContext mgc)
        {
            if (!Active || _ring.Count < 2) return; var painter = mgc.painter2D; painter.lineWidth = 2.4f; painter.strokeColor = CanSculpt ? new Color(1f, .85f, .2f, .95f) : new Color(1f, .3f, .3f, .9f); painter.BeginPath(); var open = false;
            foreach (var p in _ring) { if (float.IsNaN(p.x)) { open = false; continue; } if (!open) { painter.MoveTo(p); open = true; } else painter.LineTo(p); }
            painter.Stroke();
        }
    }

    /// <summary>The window of the terrain tools: the tool grid, the brush, the ground materials, undo and redo, and the sea level, export and import for the island's editors.</summary>
    public sealed class TerrainWindow : FloatingWindow
    {
        private readonly TerrainEditor _t; private readonly Dictionary<TerrainTool, Button> _tools = new Dictionary<TerrainTool, Button>(); private readonly List<Button> _layers = new List<Button>(); private readonly Label _status, _layerName;
        private readonly VisualElement _levelBox, _paintBox, _islandBox; private readonly Slider _level; private readonly TextField _sea;
        public string StatusText => _status.text;
        public bool LevelVisible => _levelBox.style.display != DisplayStyle.None;
        public bool PaintVisible => _paintBox.style.display != DisplayStyle.None;
        public bool IslandVisible => _islandBox.style.display != DisplayStyle.None;
        public int LayerButtons => _layers.Count;

        public TerrainWindow(ApiContext context, TerrainEditor editor) : base(context.Ui, "terrain", "Editar terreno", 400f, 560f, new Vector2(20, 90))
        {
            _t = editor; Body.style.flexGrow = 1; Body.style.minHeight = 0; style.overflow = Overflow.Hidden; var scroll = new ScrollView(); scroll.style.flexGrow = 1; scroll.style.flexShrink = 1; scroll.style.minHeight = 0; Body.Add(scroll); var box = scroll.contentContainer;
            box.Add(UiKit.Text("Ferramenta", "lv-bold", "lv-accent")); var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; box.Add(grid);
            foreach (var pair in new[] { (TerrainTool.Raise, "Elevar"), (TerrainTool.Lower, "Abaixar"), (TerrainTool.Level, "Nivelar"), (TerrainTool.Flatten, "Achatar"), (TerrainTool.Smooth, "Suavizar"), (TerrainTool.Noise, "Rugosidade"), (TerrainTool.Erode, "Erosão"), (TerrainTool.Restore, "Restaurar"), (TerrainTool.Paint, "Pintar") })
            { var tool = pair.Item1; var button = UiKit.Button(pair.Item2, () => Select(tool)); button.style.width = 112; _tools[tool] = button; grid.Add(button); }
            box.Add(UiKit.Text("Pincel", "lv-bold", "lv-accent")); box.Add(UiKit.Row(UiKit.Button("Redondo", () => { _t.Square = false; _t.Refresh(); }), UiKit.Button("Quadrado", () => { _t.Square = true; _t.Refresh(); })));
            box.Add(UiKit.Slide("Tamanho (m)", 1f, TerrainMap.MaxBrushRadius, _t.Radius, v => _t.Radius = v)); box.Add(UiKit.Slide("Intensidade (%)", 1f, 100f, _t.StrengthPercent, v => _t.StrengthPercent = v)); box.Add(UiKit.Slide("Suavidade (%)", 0f, 100f, _t.SoftPercent, v => _t.SoftPercent = v));
            _levelBox = new VisualElement(); _level = UiKit.Slide("Altura do nível (m)", TerrainMap.MinHeight + 1f, TerrainMap.MaxHeight, _t.Level, v => _t.Level = v); _levelBox.Add(_level); _levelBox.Add(UiKit.Button("Usar a altura sob o cursor", () => { if (_t.UseHoverHeight()) _level.SetValueWithoutNotify(_t.Level); })); box.Add(_levelBox);
            _paintBox = new VisualElement(); _paintBox.Add(UiKit.Text("Material", "lv-bold", "lv-accent")); var swatches = new VisualElement(); swatches.style.flexDirection = FlexDirection.Row; swatches.style.flexWrap = Wrap.Wrap; _paintBox.Add(swatches);
            for (var i = 0; i < TerrainView.Layers.Length; i++) { var index = i; var layer = TerrainView.Layers[i]; var swatch = new Button(() => SelectLayer(index)) { text = "" }; swatch.style.width = 84; swatch.style.height = 44; swatch.style.backgroundColor = Color.Lerp(layer.Dark, layer.Light, .5f); swatch.tooltip = layer.Name; _layers.Add(swatch); swatches.Add(swatch); }
            _layerName = UiKit.Text(TerrainView.Layers[0].Name, "lv-body"); _paintBox.Add(_layerName); box.Add(_paintBox);
            box.Add(UiKit.Row(UiKit.Button("Desfazer", () => _t.Undo()), UiKit.Button("Refazer", () => _t.Redo())));
            _islandBox = new VisualElement(); _islandBox.Add(UiKit.Text("Ilha (editores da ilha)", "lv-bold", "lv-accent")); _sea = UiKit.Field("Nível do mar (m)", false, 6, "0"); _islandBox.Add(_sea);
            _islandBox.Add(UiKit.Row(UiKit.Button("Aplicar mar", () => { if (float.TryParse(_sea.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) _t.ApplySeaLevel(v); else _t.Say("Digite o nível do mar em metros."); }), UiKit.Button("Exportar", () => _t.Export()), UiKit.Button("Importar…", () => new FilePickerWindow(context.Ui, "Escolha o mapa de alturas (RAW 16 bits)", new[] { ".r16", ".raw" }, path => _t.Import(path)).Open()))); box.Add(_islandBox);
            _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            _t.Changed += Refresh; Closed += () => { _t.Changed -= Refresh; _t.SetActive(false); }; Refresh();
        }

        public void Select(TerrainTool tool) { _t.Tool = tool; _t.Refresh(); }
        public void SelectLayer(int layer) { _t.Layer = Mathf.Clamp(layer, 0, TerrainView.Layers.Length - 1); _layerName.text = TerrainView.Layers[_t.Layer].Name; Refresh(); }

        private void Refresh()
        {
            foreach (var pair in _tools) pair.Value.EnableInClassList("lv-btn-selected", pair.Key == _t.Tool); for (var i = 0; i < _layers.Count; i++) { var chosen = i == _t.Layer; _layers[i].style.borderTopWidth = _layers[i].style.borderBottomWidth = _layers[i].style.borderLeftWidth = _layers[i].style.borderRightWidth = chosen ? 4 : 1; _layers[i].style.borderTopColor = _layers[i].style.borderBottomColor = _layers[i].style.borderLeftColor = _layers[i].style.borderRightColor = chosen ? new Color(.07f, .95f, .88f) : new Color(1, 1, 1, .25f); }
            _levelBox.style.display = _t.Tool == TerrainTool.Level ? DisplayStyle.Flex : DisplayStyle.None; _paintBox.style.display = _t.Tool == TerrainTool.Paint ? DisplayStyle.Flex : DisplayStyle.None; _islandBox.style.display = _t.CanSculpt ? DisplayStyle.Flex : DisplayStyle.None; _status.text = _t.Status;
        }
    }
}
