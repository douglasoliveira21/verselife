using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Scripts;

/// In-world terrain tools (T): raise, lower, level, flatten, smooth, roughen, erode, restore and paint eight ground materials, with a round or
/// square brush of any size, strength and edge, a live preview of the result and undo and redo. Edits are only sent to the island server, which
/// checks permission (island editors sculpt anywhere; people who own, rent or build on a parcel sculpt inside it) and replicates them.
public partial class TerrainEditor : Node3D
{
    private const float DabInterval = .08f;
    private NetworkManager _network = null!;
    private IslandTerrain _terrain = null!;
    private FloatingWindow _window = null!;
    private Label _status = null!, _sizeValue = null!, _strengthValue = null!, _softValue = null!, _levelValue = null!, _scaleValue = null!, _seaValue = null!, _layerName = null!;
    private HSlider _size = null!, _strength = null!, _soft = null!, _level = null!, _scale = null!, _sea = null!;
    private Button _round = null!, _squareButton = null!;
    private Control _levelRow = null!, _paintBox = null!, _islandBox = null!;
    private readonly List<Button> _layerButtons = [];
    private FileDialog? _fileDialog;
    private MeshInstance3D _brushRound = null!, _brushSquare = null!, _preview = null!;
    private readonly Dictionary<TerrainTool, Button> _toolButtons = [];
    private TerrainTool _tool = TerrainTool.Raise;
    private int _layer;
    private bool _square;
    private bool _active, _dragging;
    private double _sinceDab, _sincePreview;
    private Vector3 _lastPreviewPoint = new(float.NaN, 0, 0);
    private bool _previewDirty = true;
    private Vector3? _hover;

    /// Tests: sends edits here instead of to the network; return true when it was "sent".
    public Func<TerrainTool, Vector3, float, float, int, bool>? SubmitOverride { get; set; }

    public bool Active => _active;
    public TerrainTool Tool => _tool;
    public int PreviewVertices { get; private set; }

    private LandManager? Land => GetParent()?.GetNodeOrNull<LandManager>("LandManager");

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        _terrain = GetParent().GetNode<IslandTerrain>("IslandTerrain");
        _network.TerrainPermissionChanged += OnPermissionChanged;
        _terrain.Changed += SyncSea;
        _network.TerrainEditRejected += OnRejected;
        BuildPanel();
        BuildBrush();
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.TerrainPermissionChanged -= OnPermissionChanged;
        if (_terrain is not null) _terrain.Changed -= SyncSea;
        _network.TerrainEditRejected -= OnRejected;
    }

    public void SetActive(bool active)
    {
        _active = active; _window.Visible = active; _dragging = false; HideBrush();
        if (active) { Land?.Load(); RefreshStatus(); }
    }

    // ---- who may edit and where ------------------------------------------------------------------------------

    /// Island editors sculpt anywhere; everyone else only inside the parcels they own, rent or build on.
    private bool CanSculpt => _network.CanEditTerrain || (Land?.HasTerrainRights ?? false);

    /// The test of which ground vertices may change; null for island editors, who need none.
    public Func<int, int, bool>? Mask => _network.CanEditTerrain || Land is not { } land ? null : land.TerrainRights.MaskFor(GetNode<SessionManager>("/root/SessionManager").UserId, _terrain.Map);

    // ---- input -------------------------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent input)
    {
        if (input.IsActionPressed("terrain_mode")) SetActive(!_active);
        if (!_active) return;
        if (input.IsActionPressed("ui_cancel")) SetActive(false);
        if (input is InputEventKey { Pressed: true, Echo: false, CtrlPressed: true } key)
        {
            if (key.PhysicalKeycode == Key.Z && !key.ShiftPressed) { Undo(); GetViewport().SetInputAsHandled(); }
            else if (key.PhysicalKeycode == Key.Y || (key.PhysicalKeycode == Key.Z && key.ShiftPressed)) { Redo(); GetViewport().SetInputAsHandled(); }
        }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
        {
            _dragging = mouse.Pressed && !mouse.DoubleClick;
            _sinceDab = DabInterval;
        }
    }

    public override void _Process(double delta)
    {
        if (!_active) return;
        var camera = GetViewport().GetCamera3D();
        var point = camera is null ? null : _terrain.Raycast(camera, GetViewport().GetMousePosition());
        var over = point is not null && GetViewport().GuiGetHoveredControl() is null;
        if (point is not Vector3 hit || !over) { HideBrush(); _hover = null; return; }
        _hover = hit;
        var radius = (float)_size.Value;
        _brushRound.Visible = !_square; _brushSquare.Visible = _square;
        var shown = _square ? _brushSquare : _brushRound;
        shown.GlobalPosition = hit + Vector3.Up * .15f; shown.Scale = new Vector3(radius, 1, radius);
        _sincePreview += delta;
        if (_sincePreview > .08 && (_previewDirty || _lastPreviewPoint.DistanceTo(hit) > .25f)) { _sincePreview = 0; UpdatePreview(hit); }
        if (!Input.IsMouseButtonPressed(MouseButton.Left)) _dragging = false;
        if (!_dragging) return;
        _sinceDab += delta;
        if (_sinceDab < DabInterval) return;
        _sinceDab = 0;
        SubmitAt(hit);
    }

    // ---- the brush ---------------------------------------------------------------------------------------------

    private BrushOptions Brush => new(_square, 1f - (float)_soft.Value / 100f, _tool == TerrainTool.Level ? (float)_level.Value : null);

    private float StrengthValue()
    {
        var percent = (float)_strength.Value / 100f;
        return _tool is TerrainTool.Raise or TerrainTool.Lower or TerrainTool.Noise ? percent * .4f : percent;
    }

    /// What one dab at this point would send: tool, radius, strength and the layer number packed with the brush options.
    public (TerrainTool Tool, float Radius, float Strength, int Packed) RequestAt() => (_tool, (float)_size.Value, StrengthValue(), BrushCodec.Pack(_layer, Brush));

    public bool SubmitAt(Vector3 point)
    {
        if (!_network.IsAuthenticated && SubmitOverride is null) { _status.Text = "Entre em uma ilha para editar o terreno."; return false; }
        if (!CanSculpt) { _status.Text = "Você não pode editar o terreno aqui: é preciso ser o dono, o locatário ou construtor de um terreno (ou editor da ilha)."; return false; }
        var (tool, radius, strength, packed) = RequestAt();
        if (SubmitOverride is not null) return SubmitOverride(tool, point, radius, strength, packed);
        _network.SubmitTerrainEdit(tool, point, radius, strength, packed);
        return true;
    }

    /// Shows what the tool would do at this point as a translucent surface, by trying it on the local ground and putting everything back.
    public void UpdatePreview(Vector3 point)
    {
        _lastPreviewPoint = point; _previewDirty = false;
        PreviewVertices = 0;
        var map = _terrain.Map;
        if (_tool == TerrainTool.Paint) { _preview.Mesh = null; return; }
        var radius = (float)_size.Value; var mask = Mask; var brush = Brush;
        var reach = radius + map.CellSize * 3f; // erosion can carry material a little past the brush
        var revision = map.Revision;
        var before = map.Capture(point.X, point.Z, reach, true, null);
        map.Apply(_tool, point.X, point.Z, radius, StrengthValue(), _layer, brush, mask);
        var vertices = new List<Vector3>(); var colors = new List<Color>(); var heights = new Dictionary<int, float>();
        foreach (var (index, _) in before.Entries) heights[index] = map.HeightAt(index % map.VertexCount, index / map.VertexCount);
        var half = map.Cells / 2f;
        Vector3 World(int x, int z) => new((x - half) * map.CellSize, heights.TryGetValue(z * map.VertexCount + x, out var h) ? h : map.HeightAt(x, z), (z - half) * map.CellSize);
        var cells = new HashSet<int>();
        foreach (var (index, _) in before.Entries)
        {
            int x = index % map.VertexCount, z = index / map.VertexCount;
            var distance = brush.Square ? MathF.Max(MathF.Abs(World(x, z).X - point.X), MathF.Abs(World(x, z).Z - point.Z)) : new Vector2(World(x, z).X - point.X, World(x, z).Z - point.Z).Length();
            if (distance > radius + map.CellSize) continue;
            foreach (var corner in new[] { (x, z), (x - 1, z), (x, z - 1), (x - 1, z - 1) }) if (corner.Item1 >= 0 && corner.Item2 >= 0 && corner.Item1 < map.Cells && corner.Item2 < map.Cells) cells.Add(corner.Item2 * map.Cells + corner.Item1);
        }
        foreach (var cell in cells)
        {
            int x = cell % map.Cells, z = cell / map.Cells;
            foreach (var (cx, cz) in new[] { (x, z), (x + 1, z), (x + 1, z + 1), (x, z), (x + 1, z + 1), (x, z + 1) })
            {
                var world = World(cx, cz); vertices.Add(world + Vector3.Up * .12f);
                var allowed = mask is null || mask(cx, cz);
                colors.Add(allowed ? new Color(.1f, .95f, .88f, .42f) : new Color(.95f, .25f, .25f, .30f));
            }
        }
        map.Restore(before); map.Revision = revision; // the real ground is back exactly as it was
        PreviewVertices = vertices.Count;
        if (vertices.Count == 0) { _preview.Mesh = null; return; }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _preview.Mesh = mesh;
    }

    private void HideBrush() { _brushRound.Visible = false; _brushSquare.Visible = false; _preview.Mesh = null; _lastPreviewPoint = new Vector3(float.NaN, 0, 0); }

    private void BuildBrush()
    {
        // unit-size outlines scaled to the brush radius; drawn on top so they stay visible on slopes
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, .85f, .2f, .9f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true };
        _brushRound = new MeshInstance3D { Name = "TerrainBrush", Mesh = new TorusMesh { InnerRadius = .97f, OuterRadius = 1f, Rings = 48, RingSegments = 4 }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_brushRound);
        var corners = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
        var strips = new List<Vector3>();
        for (var i = 0; i < 4; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % 4]; var side = (b - a).Normalized().Cross(Vector3.Up) * .03f;
            foreach (var p in new[] { a - side, b - side, b + side, a - side, b + side, a + side }) strips.Add(p);
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = strips.ToArray();
        var square = new ArrayMesh(); square.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _brushSquare = new MeshInstance3D { Name = "TerrainBrushSquare", Mesh = square, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_brushSquare);
        var previewMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _preview = new MeshInstance3D { Name = "TerrainPreview", MaterialOverride = previewMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true, ExtraCullMargin = 4000 };
        AddChild(_preview);
    }

    // ---- the window --------------------------------------------------------------------------------------------

    private static Label Heading(string text)
    {
        var label = new Label { Text = text }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 19);
        return label;
    }

    private static Label Small(string text = "") { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0) }; label.AddThemeColorOverride("font_color", UiStyle.TextMuted); label.AddThemeFontSizeOverride("font_size", 16); return label; }

    private static Button Toggle(string icon, string text, string tip)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 22), TooltipText = tip, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 46), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ExpandIcon = false };
        const float mx = 8, my = 6;
        button.AddThemeStyleboxOverride("normal", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Normal, mx, my, 12));
        button.AddThemeStyleboxOverride("hover", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Hover, mx, my, 12));
        button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Normal, mx, my, 12));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Hover, mx, my, 12));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeFontSizeOverride("font_size", 17); button.AddThemeConstantOverride("icon_max_width", 22);
        foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) button.AddThemeColorOverride(color, Colors.White);
        return button;
    }

    private static Button Action(string icon, string text, string tip, Action action)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 22), TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ExpandIcon = false };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 10, 6); button.AddThemeFontSizeOverride("font_size", 17); button.AddThemeConstantOverride("icon_max_width", 22);
        button.Pressed += action;
        return button;
    }

    private HSlider SliderRow(VBoxContainer box, string name, double min, double max, double value, double step, Func<double, string> format, out Label valueLabel)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); box.AddChild(row);
        var label = new Label { Text = name, CustomMinimumSize = new Vector2(120, 0) }; label.AddThemeFontSizeOverride("font_size", 17); row.AddChild(label);
        var slider = new HSlider { MinValue = min, MaxValue = max, Value = value, Step = step, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 28) }; row.AddChild(slider);
        var shown = new Label { Text = format(value), CustomMinimumSize = new Vector2(78, 0), HorizontalAlignment = HorizontalAlignment.Right }; shown.AddThemeFontSizeOverride("font_size", 17); row.AddChild(shown);
        slider.ValueChanged += v => { shown.Text = format(v); _previewDirty = true; };
        valueLabel = shown;
        return slider;
    }

    private void BuildPanel()
    {
        _window = new FloatingWindow("Editar terreno", icon: "map", minimizable: true) { Name = "TerrainTools", Visible = false };
        _window.Place(0, 0, 18, 56, 548, 56 + 760);
        _window.Scale = new Vector2(.8f, .8f);
        var box = _window.Content; box.AddThemeConstantOverride("separation", 8);

        box.AddChild(Heading("Ferramenta"));
        var grid = new GridContainer { Columns = 3 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); box.AddChild(grid);
        foreach (var (tool, icon, text, tip) in new (TerrainTool, string, string, string)[]
        {
            (TerrainTool.Raise, "caret-up", "Elevar", "Aumenta a altura do solo"), (TerrainTool.Lower, "caret-down", "Abaixar", "Reduz a altura do solo"), (TerrainTool.Level, "align-middle", "Nivelar", "Leva o solo à altura escolhida abaixo"),
            (TerrainTool.Flatten, "ground", "Achatar", "Deixa a área plana, na altura do ponto onde você clicou"), (TerrainTool.Smooth, "waves", "Suavizar", "Suaviza as transições entre alturas diferentes"), (TerrainTool.Noise, "dots", "Rugosidade", "Cria irregularidades naturais"),
            (TerrainTool.Erode, "triangle", "Erosão", "Desgasta encostas íngremes: a terra desliza para baixo"), (TerrainTool.Restore, "reset", "Restaurar", "Devolve o relevo e as texturas ao terreno original da ilha"), (TerrainTool.Paint, "texture", "Pintar", "Pinta o material escolhido abaixo")
        })
        {
            var button = Toggle(icon, text, tip); button.ButtonPressed = tool == _tool; var captured = tool;
            button.Pressed += () => SelectTool(captured);
            _toolButtons[tool] = button; grid.AddChild(button);
        }

        box.AddChild(Heading("Pincel"));
        var shape = new HBoxContainer(); shape.AddThemeConstantOverride("separation", 6); box.AddChild(shape);
        _round = Toggle("target", "Redondo", "Pincel circular"); _round.ButtonPressed = true; _round.Pressed += () => SelectShape(false); shape.AddChild(_round);
        _squareButton = Toggle("grid", "Quadrado", "Pincel quadrado, bom para pisos e fundações"); _squareButton.Pressed += () => SelectShape(true); shape.AddChild(_squareButton);
        _size = SliderRow(box, "Tamanho", 1, TerrainMap.MaxBrushRadius, 4, .5, v => $"{v:0.#} m", out _sizeValue);
        _strength = SliderRow(box, "Intensidade", 1, 100, 25, 1, v => $"{v:0}%", out _strengthValue);
        _soft = SliderRow(box, "Suavidade", 0, 100, 100, 1, v => $"{v:0}%", out _softValue);
        _levelRow = new VBoxContainer(); box.AddChild(_levelRow);
        _level = SliderRow((VBoxContainer)_levelRow, "Altura do nível", TerrainMap.MinHeight + 1, TerrainMap.MaxHeight, 2, .1, v => $"{v:0.0} m", out _levelValue);
        ((VBoxContainer)_levelRow).AddChild(Action("pin", "Usar a altura do ponto sob o cursor", "Copia a altura do solo onde o cursor está para o nível", UseHoverHeight));

        _paintBox = new VBoxContainer(); box.AddChild(_paintBox);
        _paintBox.AddChild(Heading("Material"));
        var swatches = new GridContainer { Columns = 4 }; swatches.AddThemeConstantOverride("h_separation", 6); swatches.AddThemeConstantOverride("v_separation", 6); _paintBox.AddChild(swatches);
        for (var i = 0; i < IslandTerrain.Layers.Length; i++)
        {
            var index = i; var layer = IslandTerrain.Layers[i];
            var button = new Button { ToggleMode = true, TooltipText = layer.Name, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 52), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var normal = UiStyle.Box(layer.Dark.Lerp(layer.Light, .5f), new Color(1, 1, 1, .35f), 10, 1, 0, 0); var chosen = UiStyle.Box(layer.Dark.Lerp(layer.Light, .5f), UiStyle.Accent, 10, 4, 0, 0);
            button.AddThemeStyleboxOverride("normal", normal); button.AddThemeStyleboxOverride("hover", chosen); button.AddThemeStyleboxOverride("pressed", chosen); button.AddThemeStyleboxOverride("hover_pressed", chosen); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            button.Pressed += () => SelectLayer(index);
            _layerButtons.Add(button); swatches.AddChild(button);
        }
        _layerName = new Label { Text = IslandTerrain.Layers[0].Name }; _layerName.AddThemeFontSizeOverride("font_size", 17); _paintBox.AddChild(_layerName);
        _layerButtons[0].SetPressedNoSignal(true);
        _scale = SliderRow((VBoxContainer)_paintBox, "Escala das texturas", .05, .6, .2, .01, v => $"{v:0.00}", out _scaleValue);
        _scale.ValueChanged += v => _terrain.SetTextureScale((float)v);
        ((VBoxContainer)_paintBox).AddChild(Small("A escala é só do seu computador: muda como as texturas aparecem para você."));

        var history = new HBoxContainer(); history.AddThemeConstantOverride("separation", 8); box.AddChild(history);
        history.AddChild(Action("undo", "Desfazer", "Desfaz a última pincelada (Ctrl+Z)", Undo)); history.AddChild(Action("redo", "Refazer", "Refaz o que foi desfeito (Ctrl+Y)", Redo));

        _islandBox = new VBoxContainer(); box.AddChild(_islandBox);
        _islandBox.AddChild(Heading("Ilha (editores da ilha)"));
        _sea = SliderRow((VBoxContainer)_islandBox, "Nível do mar", TerrainMap.MinSeaLevel, TerrainMap.MaxSeaLevel, _terrain.Map.SeaLevel, .1, v => $"{v:0.0} m", out _seaValue);
        var files = new HBoxContainer(); files.AddThemeConstantOverride("separation", 6); _islandBox.AddChild(files);
        files.AddChild(Action("check", "Aplicar mar", "Aplica o nível do mar", ApplySeaLevel)); files.AddChild(Action("download", "Exportar", "Salva as alturas em um arquivo RAW de 16 bits", ExportHeightmap)); files.AddChild(Action("folder", "Importar", "Substitui as alturas por um arquivo RAW de 16 bits", OpenImportDialog));

        _status = Small(); box.AddChild(_status);
        AddChild(_window);
        SelectTool(TerrainTool.Raise);
    }

    private void SelectTool(TerrainTool tool)
    {
        _tool = tool; _previewDirty = true;
        foreach (var (key, button) in _toolButtons) button.SetPressedNoSignal(key == tool);
        _levelRow.Visible = tool == TerrainTool.Level;
        _paintBox.Visible = tool == TerrainTool.Paint;
        RefreshStatus();
    }

    private void SelectShape(bool square) { _square = square; _round.SetPressedNoSignal(!square); _squareButton.SetPressedNoSignal(square); _previewDirty = true; }

    private void SelectLayer(int layer)
    {
        _layer = Mathf.Clamp(layer, 0, IslandTerrain.Layers.Length - 1);
        for (var i = 0; i < _layerButtons.Count; i++) _layerButtons[i].SetPressedNoSignal(i == _layer);
        _layerName.Text = IslandTerrain.Layers[_layer].Name;
    }

    private void UseHoverHeight()
    {
        if (_hover is not { } point) { _status.Text = "Passe o cursor sobre o terreno e aperte de novo."; return; }
        _level.Value = Mathf.Clamp(_terrain.Map.SampleHeight(point.X, point.Z), (float)_level.MinValue, (float)_level.MaxValue);
    }

    private bool CheckCanEdit()
    {
        if (!_network.IsAuthenticated) { _status.Text = "Entre em uma ilha para editar o terreno."; return false; }
        if (!CanSculpt) { _status.Text = "Você não pode editar o terreno aqui."; return false; }
        return true;
    }

    private bool CheckIslandEditor()
    {
        if (!CheckCanEdit()) return false;
        if (!_network.CanEditTerrain) { _status.Text = "Só os editores da ilha mudam o mar e importam mapas."; return false; }
        return true;
    }

    private void Undo() { if (CheckCanEdit()) _network.UndoTerrain(); }
    private void Redo() { if (CheckCanEdit()) _network.RedoTerrain(); }
    private void ApplySeaLevel() { if (CheckIslandEditor()) _network.SubmitSeaLevel((float)_sea.Value); }
    private void SyncSea() { if (_sea is not null && Mathf.Abs((float)_sea.Value - _terrain.Map.SeaLevel) > .001f) { _sea.SetValueNoSignal(_terrain.Map.SeaLevel); _seaValue.Text = $"{_terrain.Map.SeaLevel:0.0} m"; } _previewDirty = true; }

    private void ExportHeightmap()
    {
        var map = _terrain.Map;
        var directory = ProjectSettings.GlobalizePath("user://exports");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        var path = $"{directory}/heightmap-{map.VertexCount}x{map.VertexCount}-{DateTime.Now:yyyyMMdd-HHmmss}.r16";
        System.IO.File.WriteAllBytes(path, map.ExportHeights16());
        _status.Text = $"Mapa exportado (RAW 16 bits, {map.VertexCount}×{map.VertexCount}, {TerrainMap.MinHeight:0}…{TerrainMap.MaxHeight:0} m):\n{path}";
    }

    private void OpenImportDialog()
    {
        if (!CheckIslandEditor()) return;
        if (_fileDialog is null)
        {
            _fileDialog = new FileDialog { Title = "Importar mapa de alturas (RAW 16 bits)", FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Filters = ["*.r16,*.raw ; Mapa de alturas de 16 bits"] };
            _fileDialog.FileSelected += ImportHeightmap;
            AddChild(_fileDialog);
        }
        _fileDialog.CurrentDir = ProjectSettings.GlobalizePath("user://exports");
        _fileDialog.PopupCentered(new Vector2I(900, 600));
    }

    private void ImportHeightmap(string path)
    {
        if (!CheckIslandEditor()) return;
        byte[] bytes;
        try { bytes = System.IO.File.ReadAllBytes(path); }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { _status.Text = $"Não foi possível ler o arquivo: {exception.Message}"; return; }
        var count = _terrain.Map.VertexCount;
        if (bytes.Length != count * count * 2) { _status.Text = $"Arquivo inválido: esperado {count}×{count} amostras de 16 bits ({count * count * 2} bytes), mas tem {bytes.Length} bytes."; return; }
        _network.SubmitTerrainImport(bytes);
        _status.Text = "Mapa enviado ao servidor. As bordas (fundo do mar) não mudam.";
    }

    private void RefreshStatus()
    {
        _islandBox.Visible = _network.CanEditTerrain;
        _status.Text = !_network.IsAuthenticated ? "Entre em uma ilha para editar o terreno."
            : !CanSculpt ? "Você não pode editar o terreno aqui: é preciso ser o dono, o locatário ou construtor de um terreno (ou editor da ilha)."
            : !_network.CanEditTerrain ? "Você edita só o terreno dos seus terrenos. A prévia em vermelho mostra o que não pode mudar; a borda entre terrenos também fica como está."
            : _tool == TerrainTool.Paint ? "Segure o botão esquerdo para pintar o material escolhido."
            : _tool == TerrainTool.Level ? "Escolha a altura do nível e segure o botão esquerdo para levar o solo até ela."
            : _tool == TerrainTool.Restore ? "Segure o botão esquerdo para devolver o terreno original (relevo e texturas)."
            : "Segure o botão esquerdo sobre o terreno. A prévia em azul mostra o resultado.";
    }

    private void OnPermissionChanged(bool canEdit) { if (_active) RefreshStatus(); }
    private void OnRejected(string code) => _status.Text = code switch { "no_permission" => "Sem permissão para editar o terreno.", "rate_limit" => "Muitas edições seguidas; aguarde um instante.", "nothing_to_undo" => "Nada para desfazer.", "nothing_to_redo" => "Nada para refazer.", "invalid_heightmap" => "O servidor recusou o mapa de alturas.", "invalid_edit" => "O servidor recusou a pincelada.", _ => $"Edição recusada: {code}" };

    // ---- tests ---------------------------------------------------------------------------------------------------

    public void SelectToolForTesting(TerrainTool tool) => SelectTool(tool);
    public void SelectShapeForTesting(bool square) => SelectShape(square);
    public void SelectLayerForTesting(int layer) => SelectLayer(layer);
    public void SetBrushForTesting(float size, float strengthPercent, float softnessPercent, float level) { _size.Value = size; _strength.Value = strengthPercent; _soft.Value = softnessPercent; _level.Value = level; }
    public bool LevelRowVisibleForTesting => _levelRow.Visible;
    public bool PaintBoxVisibleForTesting => _paintBox.Visible;
    public bool IslandBoxVisibleForTesting => _islandBox.Visible;
    public string StatusForTesting => _status.Text;
    public int LayerButtonsForTesting => _layerButtons.Count;
    public bool WindowVisibleForTesting => _window.Visible;
    public Control WindowForTesting => _window;
    public void RefreshForTesting() => RefreshStatus();
    public bool RoundBrushVisibleForTesting => _brushRound.Visible;
}
