using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Scripts;

/// In-world terrain tools (T): raise, lower, flatten, smooth, roughen and paint texture layers with a
/// round brush. Edits are only sent to the island server, which checks permission and replicates them.
public partial class TerrainEditor : Node3D
{
    private const float DabInterval = .08f;
    private NetworkManager _network = null!;
    private IslandTerrain _terrain = null!;
    private PanelContainer _panel = null!;
    private Label _status = null!;
    private HSlider _radius = null!;
    private HSlider _strength = null!;
    private Label _radiusLabel = null!;
    private Label _strengthLabel = null!;
    private OptionButton _layer = null!;
    private HSlider _sea = null!;
    private Label _seaLabel = null!;
    private FileDialog? _fileDialog;
    private MeshInstance3D _brush = null!;
    private readonly Dictionary<TerrainTool, Button> _toolButtons = [];
    private TerrainTool _tool = TerrainTool.Raise;
    private bool _active;
    private bool _dragging;
    private double _sinceDab;

    public bool Active => _active;

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
        _active = active; _panel.Visible = active; _dragging = false; _brush.Visible = false;
        if (active) RefreshStatus();
    }

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
        _brush.Visible = point is not null && GetViewport().GuiGetHoveredControl() is null;
        if (point is not Vector3 hit) return;
        _brush.GlobalPosition = hit + Vector3.Up * .15f;
        var radius = (float)_radius.Value;
        _brush.Scale = new Vector3(radius, 1, radius);
        if (!Input.IsMouseButtonPressed(MouseButton.Left)) _dragging = false;
        if (!_dragging) return;
        _sinceDab += delta;
        if (_sinceDab < DabInterval) return;
        _sinceDab = 0;
        if (!_network.IsAuthenticated) { _status.Text = "Entre em uma ilha para editar o terreno."; return; }
        if (!_network.CanEditTerrain) { _status.Text = "Você não tem permissão para editar o terreno desta ilha."; return; }
        var percent = (float)_strength.Value / 100f;
        var strength = _tool is TerrainTool.Raise or TerrainTool.Lower or TerrainTool.Noise ? percent * .4f : percent;
        _network.SubmitTerrainEdit(_tool, hit, radius, strength, _layer.Selected);
    }

    private void BuildPanel()
    {
        _panel = new PanelContainer { Name = "TerrainTools", Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _panel.OffsetLeft = 380; _panel.OffsetTop = 44; _panel.OffsetRight = 640; _panel.OffsetBottom = 45;
        var box = new VBoxContainer(); _panel.AddChild(box);
        box.AddChild(new Label { Text = "TERRENO · servidor autoritativo (T)" });
        foreach (var (tool, text) in new[] { (TerrainTool.Raise, "Elevar"), (TerrainTool.Lower, "Rebaixar"), (TerrainTool.Flatten, "Nivelar"), (TerrainTool.Smooth, "Suavizar"), (TerrainTool.Noise, "Rugosidade"), (TerrainTool.Paint, "Pintar textura") })
        {
            var button = new Button { Text = text, ToggleMode = true, ButtonPressed = tool == _tool };
            var captured = tool;
            button.Pressed += () => SelectTool(captured);
            _toolButtons[tool] = button; box.AddChild(button);
        }
        _layer = new OptionButton();
        foreach (var name in IslandTerrain.LayerNames) _layer.AddItem(name);
        box.AddChild(new Label { Text = "Textura (modo Pintar)" }); box.AddChild(_layer);
        _radiusLabel = new Label(); _radius = Slider(1, TerrainMap.MaxBrushRadius, 4, 0.5, _radiusLabel, v => $"Raio do pincel: {v:0.#} m");
        _strengthLabel = new Label(); _strength = Slider(1, 100, 25, 1, _strengthLabel, v => $"Intensidade: {v:0}%");
        box.AddChild(_radiusLabel); box.AddChild(_radius); box.AddChild(_strengthLabel); box.AddChild(_strength);
        var history = new HBoxContainer();
        var undo = new Button { Text = "Desfazer (Ctrl+Z)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; undo.Pressed += Undo; history.AddChild(undo);
        var redo = new Button { Text = "Refazer (Ctrl+Y)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; redo.Pressed += Redo; history.AddChild(redo);
        box.AddChild(history);
        _seaLabel = new Label();
        _sea = Slider(TerrainMap.MinSeaLevel, TerrainMap.MaxSeaLevel, _terrain.Map.SeaLevel, .1, _seaLabel, v => $"Nível do mar: {v:0.0} m");
        var applySea = new Button { Text = "Aplicar nível do mar" }; applySea.Pressed += ApplySeaLevel;
        box.AddChild(_seaLabel); box.AddChild(_sea); box.AddChild(applySea);
        var files = new HBoxContainer();
        var export = new Button { Text = "Exportar mapa", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Salva as alturas em um arquivo RAW de 16 bits" }; export.Pressed += ExportHeightmap; files.AddChild(export);
        var import = new Button { Text = "Importar mapa", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Substitui as alturas por um arquivo RAW de 16 bits" }; import.Pressed += OpenImportDialog; files.AddChild(import);
        box.AddChild(files);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) };
        box.AddChild(_status);
        AddChild(_panel);
    }

    private bool CheckCanEdit()
    {
        if (!_network.IsAuthenticated) { _status.Text = "Entre em uma ilha para editar o terreno."; return false; }
        if (!_network.CanEditTerrain) { _status.Text = "Você não tem permissão para editar o terreno desta ilha."; return false; }
        return true;
    }

    private void Undo() { if (CheckCanEdit()) _network.UndoTerrain(); }
    private void Redo() { if (CheckCanEdit()) _network.RedoTerrain(); }
    private void ApplySeaLevel() { if (CheckCanEdit()) _network.SubmitSeaLevel((float)_sea.Value); }
    private void SyncSea() { if (Mathf.Abs((float)_sea.Value - _terrain.Map.SeaLevel) > .001f) { _sea.SetValueNoSignal(_terrain.Map.SeaLevel); _seaLabel.Text = $"Nível do mar: {_terrain.Map.SeaLevel:0.0} m"; } }

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
        if (!CheckCanEdit()) return;
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
        if (!CheckCanEdit()) return;
        byte[] bytes;
        try { bytes = System.IO.File.ReadAllBytes(path); }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { _status.Text = $"Não foi possível ler o arquivo: {exception.Message}"; return; }
        var count = _terrain.Map.VertexCount;
        if (bytes.Length != count * count * 2) { _status.Text = $"Arquivo inválido: esperado {count}×{count} amostras de 16 bits ({count * count * 2} bytes), mas tem {bytes.Length} bytes."; return; }
        _network.SubmitTerrainImport(bytes);
        _status.Text = "Mapa enviado ao servidor. As bordas (fundo do mar) não mudam.";
    }

    private static HSlider Slider(double min, double max, double value, double step, Label label, Func<double, string> format)
    {
        var slider = new HSlider { MinValue = min, MaxValue = max, Value = value, Step = step, CustomMinimumSize = new Vector2(220, 0) };
        label.Text = format(value);
        slider.ValueChanged += v => label.Text = format(v);
        return slider;
    }

    private void BuildBrush()
    {
        // Unit-radius ring scaled to the brush radius; drawn on top so it stays visible on slopes.
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, .85f, .2f, .85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true };
        _brush = new MeshInstance3D { Name = "TerrainBrush", Mesh = new TorusMesh { InnerRadius = .97f, OuterRadius = 1f, Rings = 48, RingSegments = 4 }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_brush);
    }

    private void SelectTool(TerrainTool tool)
    {
        _tool = tool;
        foreach (var (key, button) in _toolButtons) button.ButtonPressed = key == tool;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        _status.Text = !_network.IsAuthenticated ? "Entre em uma ilha para editar o terreno."
            : !_network.CanEditTerrain ? "Você não tem permissão para editar o terreno desta ilha."
            : _tool == TerrainTool.Paint ? "Segure o botão esquerdo para pintar a textura escolhida."
            : "Segure o botão esquerdo sobre o terreno para aplicar a ferramenta.";
    }

    private void OnPermissionChanged(bool canEdit) { if (_active) RefreshStatus(); }
    private void OnRejected(string code) => _status.Text = code switch { "no_permission" => "Sem permissão para editar o terreno.", "rate_limit" => "Muitas edições seguidas; aguarde um instante.", "nothing_to_undo" => "Nada para desfazer.", "nothing_to_redo" => "Nada para refazer.", "invalid_heightmap" => "O servidor recusou o mapa de alturas.", _ => $"Edição recusada: {code}" };
}
