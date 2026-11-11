using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The TEXTURA tab, in two material models (PBR and Blinn-Phong) and the media of a surface. Each model shows the maps it really has: PBR has base colour, normal, emissive and a packed
/// occlusion-roughness-metallic map; Blinn-Phong has diffuse, normal, specular, transparency and glow. Every map has its own scale, offset and rotation (PBR also has "ALL"). What is
/// edited is the whole object or the faces chosen with the Face tool, and every change is stored with the object so everybody sees the same surface.
/// </summary>
public partial class BuildTextureTab : VBoxContainer
{
    private const double SendDelay = .4;
    private static readonly string[] PbrChannels = ["all", "texture", "normalMap", "ormMap", "emissiveMap"];
    private static readonly string[] PbrNames = ["ALL", "BASE COLOR", "NORMAL", "(O)RM", "EMISSIVE"];
    private static readonly string[] BlinnChannels = ["texture", "normalMap", "specularMap"];
    private static readonly string[] BlinnNames = ["DIFFUSE", "NORMAL", "SPECULAR"];

    private sealed class Slot { public string Key = ""; public TextureRect Thumb = null!; public Label Name = null!; public Button Choose = null!, Clear = null!; }

    private BuildMaterialManager _material = null!;
    private BuildTexturePicker _picker = null!;
    private BuildSelectionManager _selection = null!;
    private BuildMediaPanel? _media;
    private UiTabs _modes = null!;
    private Label _target = null!, _note = null!;
    private Control _pbr = null!, _blinn = null!, _mediaBox = null!;
    private readonly Dictionary<string, Slot> _slots = [];
    private ColorPickerButton _pbrColor = null!, _glowColor = null!, _blinnColor = null!, _specColor = null!, _blinnGlowColor = null!;
    private SpinBox _pbrOpacity = null!, _alphaCutoff = null!, _normalStrength = null!, _glowStrength = null!, _occlusion = null!, _roughness = null!, _metallic = null!;
    private SpinBox _blinnOpacity = null!, _blinnNormal = null!, _specStrength = null!, _gloss = null!, _blinnGlow = null!;
    private OptionButton _alphaMode = null!, _preset = null!;
    private CheckBox _fullBright = null!, _blinnGlowOn = null!;
    private UiTabs _pbrUvTabs = null!, _blinnUvTabs = null!;
    private readonly SpinBox[] _pbrUv = new SpinBox[5], _blinnUv = new SpinBox[5];
    private FileDialog _import = null!, _export = null!;
    private bool _filling;
    private double _timer;
    private readonly Dictionary<string, Action> _queue = [];

    public event Action<string>? Said;
    public event Action? FaceToolRequested, WholeObjectRequested;

    // ---- test access ---------------------------------------------------------------------------------------------------------

    public int ModeForTesting => _modes.Selected;
    public string TargetForTesting => _target.Text;
    public string NoteForTesting => _note.Text;
    public bool PbrVisibleForTesting => _pbr.Visible;
    public bool BlinnVisibleForTesting => _blinn.Visible;
    public string SlotNameForTesting(string key) => _slots[key].Name.Text;
    public bool SlotClearVisibleForTesting(string key) => !_slots[key].Clear.Disabled;
    public IReadOnlyList<string> UvTabsForTesting(bool pbr) { var tabs = pbr ? _pbrUvTabs : _blinnUvTabs; return [.. Enumerable.Range(0, tabs.Count).Select(i => tabs.TabAt(i).Text.Trim())]; }
    public void SelectModeForTesting(int index) { _modes.Select(index); }
    public void PickForTesting(string key, string? asset, bool builtin = false) => OnPicked(key, asset, builtin);
    public void ClearSlotForTesting(string key) => _material.SetMap(key, null);
    public void SetUvTabForTesting(bool pbr, int index) { (pbr ? _pbrUvTabs : _blinnUvTabs).Select(index); }
    public void SetUvForTesting(bool pbr, double repeatX, double repeatY, double offsetX, double offsetY, double rotation) { var f = pbr ? _pbrUv : _blinnUv; f[0].Value = repeatX; f[1].Value = repeatY; f[2].Value = offsetX; f[3].Value = offsetY; f[4].Value = rotation; }
    public double UvForTesting(bool pbr, int field) => (pbr ? _pbrUv : _blinnUv)[field].Value;
    public void SetPbrForTesting(Color color, double opacity, string alphaMode, double normal, double roughness, double metallic, double occlusion) { _pbrColor.Color = color; _pbrOpacity.Value = opacity; _alphaMode.Select(alphaMode == "blend" ? 1 : alphaMode == "mask" ? 2 : 0); _normalStrength.Value = normal; _roughness.Value = roughness; _metallic.Value = metallic; _occlusion.Value = occlusion; Queue("pbr", PushPbr); }
    public void SetGlowForTesting(bool pbr, Color color, double strength) { if (pbr) { _glowColor.Color = color; _glowStrength.Value = strength; Queue("glow", PushPbrGlow); } else { _blinnGlowOn.ButtonPressed = strength > 0; _blinnGlowColor.Color = color; _blinnGlow.Value = strength; Queue("blinnGlow", PushBlinnGlow); } }
    public void SetBlinnForTesting(Color color, bool fullBright, double opacity, double normal, Color specular, double specStrength, double gloss) { _blinnColor.Color = color; _fullBright.ButtonPressed = fullBright; _blinnOpacity.Value = opacity; _blinnNormal.Value = normal; _specColor.Color = specular; _specStrength.Value = specStrength; _gloss.Value = gloss; Queue("blinn", PushBlinn); }
    public void PressClearForTesting() { _material.Clear(); }
    public void ApplyPresetForTesting(int index) { _preset.Select(index); ApplyPreset(); }
    public string ExportForTesting() => _material.Export() ?? string.Empty;
    public int ImportForTesting(string json) => _material.Import(json);
    public void FlushForTesting() => Flush();
    public BuildMediaPanel? MediaForTesting => _media;

    public void Initialize(BuildSelectionManager selection, BuildMaterialManager material, BuildTexturePicker picker, BuildMediaPanel media)
    {
        _selection = selection; _material = material; _picker = picker; _media = media;
        if (_mediaBox is null) return; // the tab is not built yet: it will take the panel when it is
        var old = _mediaBox; var index = old.GetIndex(); var visible = old.Visible; RemoveChild(old); old.QueueFree();
        _mediaBox = media; AddChild(media); MoveChild(media, index); media.Visible = visible;
    }

    // ---- building ----------------------------------------------------------------------------------------------------------------

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _target = new Label { Text = "Alvo: —" }; _target.AddThemeFontOverride("font", UiStyle.Bold); _target.AddThemeFontSizeOverride("font_size", 18); _target.AddThemeColorOverride("font_color", UiStyle.AccentText); AddChild(_target);
        var targets = new HBoxContainer(); targets.AddThemeConstantOverride("separation", 8); AddChild(targets);
        targets.AddChild(SmallButton("Escolher faces", "Muda para a ferramenta Face: clique nas faces (Shift soma mais)", () => FaceToolRequested?.Invoke())); targets.AddChild(SmallButton("Objeto inteiro", "Volta a editar o objeto todo", () => WholeObjectRequested?.Invoke()));
        _modes = new UiTabs { EqualWidth = true, Flat = true, FontSize = 17 }; _modes.AddTab("PBR"); _modes.AddTab("BLINN-PHONG"); _modes.AddTab("MÍDIA"); AddChild(_modes);
        _modes.TabChanged += OnMode;
        _pbr = BuildPbr(); _blinn = BuildBlinn(); _mediaBox = _media is null ? new VBoxContainer() : _media; AddChild(_pbr); AddChild(_blinn); AddChild(_mediaBox); _blinn.Visible = false; _mediaBox.Visible = false;
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _note.AddThemeFontSizeOverride("font_size", 16); _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_note);
        _import = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.lvmat ; Material do LifeVerse", "*.json ; JSON"], Title = "Importar material" }; _import.FileSelected += path => { var json = Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsString(path) : string.Empty; if (_material.Import(json) > 0) Said?.Invoke("Material importado."); }; AddChild(_import);
        _export = new FileDialog { FileMode = FileDialog.FileModeEnum.SaveFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.lvmat ; Material do LifeVerse"], Title = "Exportar material" }; _export.FileSelected += path => { if (_material.Export() is { } json) { using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write); file?.StoreString(json); Said?.Invoke("Material exportado."); } }; AddChild(_export);
        Refresh();
    }

    private static Button SmallButton(string text, string tip, Action action) { var b = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Secondary, 10, 4, 12); b.AddThemeFontSizeOverride("font_size", 16); b.Pressed += action; return b; }
    private static Label Heading(string text) { var label = new Label { Text = text }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 18); label.AddThemeColorOverride("font_color", UiStyle.AccentText); return label; }
    private static Label Small(string text, int width = 120) { var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0), VerticalAlignment = VerticalAlignment.Center }; label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label; }
    private static CheckBox Check(string text, string tip) { var box = new CheckBox { Text = " " + text, TooltipText = tip, FocusMode = FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 17); return box; }

    private static SpinBox Spin(double min, double max, double step, double value, string tip)
    {
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, TooltipText = tip, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(80, 36), SelectAllOnFocus = true, UpdateOnTextChanged = false };
        spin.GetLineEdit().AddThemeFontSizeOverride("font_size", 16); return spin;
    }

    private static HBoxContainer Row(string caption, params Control[] controls) { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); row.AddChild(Small(caption)); foreach (var c in controls) { c.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(c); } return row; }

    private ColorPickerButton Color_(Color color, string tip) { var c = new ColorPickerButton { Color = color, CustomMinimumSize = new Vector2(80, 36), TooltipText = tip, EditAlpha = false }; return c; }

    private Control SlotRow(string key, string caption, bool allowBuiltin)
    {
        var slot = new Slot { Key = key }; _slots[key] = slot;
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        var frame = new PanelContainer(); frame.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#121A20"), UiStyle.Border, 8, 1, 2, 2)); slot.Thumb = new TextureRect { CustomMinimumSize = new Vector2(48, 48), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true }; frame.AddChild(slot.Thumb); row.AddChild(frame);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddChild(col);
        col.AddChild(Small(caption, 0)); slot.Name = new Label { Text = "nenhuma", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; slot.Name.AddThemeFontSizeOverride("font_size", 16); col.AddChild(slot.Name);
        slot.Choose = SmallButton("Escolher…", "Escolhe uma imagem do inventário" + (allowBuiltin ? " ou um padrão do sistema" : ""), () => _picker.Open(allowBuiltin, entry => OnPicked(key, entry.Builtin ?? entry.Asset, entry.Builtin is not null)));
        slot.Clear = SmallButton("Tirar", "Tira a imagem deste mapa", () => _material.SetMap(key, null)); row.AddChild(slot.Choose); row.AddChild(slot.Clear);
        return row;
    }

    private void OnPicked(string key, string? value, bool builtin) { if (value is null) return; if (_material.SetMap(key, value, builtin) > 0) Said?.Invoke("Textura enviada ao servidor."); }

    private Control BuildPbr()
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8);
        box.AddChild(Heading("MATERIAL"));
        _preset = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36), FocusMode = FocusModeEnum.None, TooltipText = "Materiais prontos para começar" }; foreach (var (name, _) in BuildMaterialManager.Presets) _preset.AddItem(name);
        var materials = new HBoxContainer(); materials.AddThemeConstantOverride("separation", 6); materials.AddChild(_preset); materials.AddChild(SmallButton("Aplicar", "Dá o material escolhido ao alvo", ApplyPreset)); materials.AddChild(SmallButton("Remover", "Tira todas as texturas e a cor: o alvo fica liso e branco", () => { if (_material.Clear() > 0) Said?.Invoke("Material removido."); })); box.AddChild(materials);
        var files = new HBoxContainer(); files.AddThemeConstantOverride("separation", 6); files.AddChild(SmallButton("Importar material…", "Lê um material guardado em arquivo (as imagens ficam de fora)", () => _import.PopupCentered(new Vector2I(800, 500)))); files.AddChild(SmallButton("Exportar…", "Guarda o material do alvo em um arquivo", () => _export.PopupCentered(new Vector2I(800, 500)))); box.AddChild(files);

        box.AddChild(Heading("BASE COLOR")); box.AddChild(SlotRow("texture", "Textura", true));
        _pbrColor = Color_(Colors.White, "A cor da superfície (a textura é pintada por cima)"); _pbrOpacity = Spin(0, 100, 1, 0, "Transparência: 0 opaco, 100 invisível"); _pbrOpacity.Suffix = "%"; _alphaMode = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36), FocusMode = FocusModeEnum.None, TooltipText = "Como o canal alfa é usado" }; _alphaMode.AddItem("Opaco"); _alphaMode.AddItem("Mistura (transparente)"); _alphaMode.AddItem("Recorte"); _alphaCutoff = Spin(0, 1, .05, .5, "Recorte: de que valor de alfa para baixo some");
        box.AddChild(Row("COR", _pbrColor)); box.AddChild(Row("ALFA %", _pbrOpacity)); box.AddChild(Row("MODO ALFA", _alphaMode)); box.AddChild(Row("CORTE", _alphaCutoff));
        box.AddChild(Heading("NORMAL")); box.AddChild(SlotRow("normalMap", "Mapa normal", false)); _normalStrength = Spin(0, 2, .05, 1, "Intensidade do relevo (1 é o normal)"); box.AddChild(Row("INTENSIDADE", _normalStrength));
        box.AddChild(Heading("EMISSIVE")); box.AddChild(SlotRow("emissiveMap", "Mapa emissivo", false)); _glowColor = Color_(Colors.White, "Cor do brilho próprio"); _glowStrength = Spin(0, 16, .1, 1, "Intensidade do brilho"); box.AddChild(Row("COR", _glowColor)); box.AddChild(Row("INTENSIDADE", _glowStrength));
        box.AddChild(Heading("(O)RM")); box.AddChild(SlotRow("ormMap", "Textura ORM (oclusão, rugosidade e metal em uma imagem)", false)); _occlusion = Spin(0, 1, .05, 1, "Força da oclusão (só com a textura ORM)"); _roughness = Spin(0, 1, .05, .7, "Rugosidade (sem textura ORM)"); _metallic = Spin(0, 1, .05, 0, "Metalicidade (sem textura ORM)");
        box.AddChild(Row("OCLUSÃO", _occlusion)); box.AddChild(Row("RUGOSIDADE", _roughness)); box.AddChild(Row("METALICIDADE", _metallic));
        foreach (var spin in new[] { _pbrOpacity, _alphaCutoff, _normalStrength, _occlusion, _roughness, _metallic }) spin.ValueChanged += _ => Queue("pbr", PushPbr);
        _pbrColor.ColorChanged += _ => Queue("pbr", PushPbr); _alphaMode.ItemSelected += _ => Queue("pbr", PushPbr);
        _glowColor.ColorChanged += _ => Queue("glow", PushPbrGlow); _glowStrength.ValueChanged += _ => Queue("glow", PushPbrGlow);
        box.AddChild(Heading("MAPEAMENTO")); _pbrUvTabs = UvTabs(PbrNames, box, _pbrUv, true);
        return box;
    }

    private Control BuildBlinn()
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8);
        box.AddChild(Heading("DIFFUSE")); box.AddChild(SlotRow("texture", "Textura difusa", true));
        _blinnColor = Color_(Colors.White, "A cor da superfície"); _fullBright = Check("Full Bright", "A superfície brilha por igual, sem sombra nem luz do ambiente"); box.AddChild(Row("COR", _blinnColor)); box.AddChild(_fullBright);
        box.AddChild(Heading("NORMAL")); box.AddChild(SlotRow("normalMap", "Mapa normal", false)); _blinnNormal = Spin(0, 2, .05, 1, "Intensidade do relevo"); box.AddChild(Row("INTENSIDADE", _blinnNormal));
        box.AddChild(Heading("SPECULAR")); box.AddChild(SlotRow("specularMap", "Mapa especular", false)); _specColor = Color_(Colors.White, "Cor do brilho especular"); _specStrength = Spin(0, 2, .05, 1, "Intensidade do brilho"); _gloss = Spin(0, 1, .05, .4, "Brilho superficial: 0 fosco, 1 espelhado");
        box.AddChild(Row("COR", _specColor)); box.AddChild(Row("INTENSIDADE", _specStrength)); box.AddChild(Row("BRILHO SUP.", _gloss));
        box.AddChild(Heading("TRANSPARÊNCIA")); _blinnOpacity = Spin(0, 100, 1, 0, "0 opaco, 100 invisível"); _blinnOpacity.Suffix = "%"; box.AddChild(Row("TRANSPARÊNCIA", _blinnOpacity));
        box.AddChild(Heading("GLOW")); _blinnGlowOn = Check("Brilho próprio", "A superfície emite luz própria"); _blinnGlowColor = Color_(Colors.White, "Cor do brilho"); _blinnGlow = Spin(0, 16, .1, 1, "Intensidade do brilho"); box.AddChild(_blinnGlowOn); box.AddChild(Row("COR", _blinnGlowColor)); box.AddChild(Row("INTENSIDADE", _blinnGlow));
        foreach (var spin in new[] { _blinnNormal, _specStrength, _gloss, _blinnOpacity }) spin.ValueChanged += _ => Queue("blinn", PushBlinn);
        _blinnColor.ColorChanged += _ => Queue("blinn", PushBlinn); _specColor.ColorChanged += _ => Queue("blinn", PushBlinn); _fullBright.Toggled += _ => Queue("blinn", PushBlinn);
        _blinnGlowOn.Toggled += _ => Queue("blinnGlow", PushBlinnGlow); _blinnGlowColor.ColorChanged += _ => Queue("blinnGlow", PushBlinnGlow); _blinnGlow.ValueChanged += _ => Queue("blinnGlow", PushBlinnGlow);
        box.AddChild(Heading("MAPEAMENTO")); _blinnUvTabs = UvTabs(BlinnNames, box, _blinnUv, false);
        return box;
    }

    private UiTabs UvTabs(string[] names, VBoxContainer box, SpinBox[] fields, bool pbr)
    {
        var tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 14 }; foreach (var name in names) tabs.AddTab(name); box.AddChild(tabs);
        var scale = new HBoxContainer(); scale.AddThemeConstantOverride("separation", 6); scale.AddChild(Small("ESCALA", 70)); scale.AddChild(Small("H", 14)); fields[0] = Spin(.01, 100, .01, 1, "Repetições na horizontal"); scale.AddChild(fields[0]); scale.AddChild(Small("V", 14)); fields[1] = Spin(.01, 100, .01, 1, "Repetições na vertical"); scale.AddChild(fields[1]); box.AddChild(scale);
        var offset = new HBoxContainer(); offset.AddThemeConstantOverride("separation", 6); offset.AddChild(Small("DESLOC.", 70)); offset.AddChild(Small("H", 14)); fields[2] = Spin(-100, 100, .01, 0, "Deslocamento horizontal"); offset.AddChild(fields[2]); offset.AddChild(Small("V", 14)); fields[3] = Spin(-100, 100, .01, 0, "Deslocamento vertical"); offset.AddChild(fields[3]); box.AddChild(offset);
        var rotation = new HBoxContainer(); rotation.AddThemeConstantOverride("separation", 6); rotation.AddChild(Small("ROTAÇÃO", 70)); fields[4] = Spin(-360, 360, 1, 0, "Rotação, em graus"); rotation.AddChild(fields[4]); box.AddChild(rotation);
        foreach (var field in fields) field.ValueChanged += _ => Queue("uv" + (pbr ? "p" : "b"), () => PushUv(pbr));
        tabs.TabChanged += _ => FillUv(pbr);
        return tabs;
    }

    // ---- queued changes ------------------------------------------------------------------------------------------------------

    private void Queue(string key, Action push) { if (_filling) return; _queue[key] = push; _timer = SendDelay; }

    public override void _Process(double delta)
    {
        if (_timer <= 0) return;
        _timer -= delta; if (_timer <= 0) Flush();
    }

    private void Flush()
    {
        _timer = 0; var work = _queue.Values.ToList(); _queue.Clear();
        foreach (var push in work) push();
        Refresh();
    }

    private string OpacityMode() => _alphaMode.Selected switch { 1 => "blend", 2 => "mask", _ => "none" };
    private string Hex(ColorPickerButton c) => "#" + c.Color.ToHtml(false);

    private void PushPbr()
    {
        _material.Edit(layer =>
        {
            BuildMaterialManager.ColorTo(layer, Hex(_pbrColor)); BuildMaterialManager.OpacityTo(layer, 1 - _pbrOpacity.Value / 100, OpacityMode(), _alphaCutoff.Value);
            BuildMaterialManager.NumberTo(layer, "normalStrength", _normalStrength.Value, 0, 2); BuildMaterialManager.NumberTo(layer, "aoStrength", _occlusion.Value, 0, 1); BuildMaterialManager.NumberTo(layer, "roughness", _roughness.Value, 0, 1); BuildMaterialManager.NumberTo(layer, "metallic", _metallic.Value, 0, 1);
        }, "material");
    }

    private void PushPbrGlow() => _material.SetGlow(_glowStrength.Value > 0, Hex(_glowColor), _glowStrength.Value);

    private void PushBlinn()
    {
        _material.Edit(layer =>
        {
            BuildMaterialManager.ColorTo(layer, Hex(_blinnColor)); BuildMaterialManager.OpacityTo(layer, 1 - _blinnOpacity.Value / 100, "none"); BuildMaterialManager.FlagTo(layer, "fullBright", _fullBright.ButtonPressed);
            BuildMaterialManager.NumberTo(layer, "normalStrength", _blinnNormal.Value, 0, 2); BuildMaterialManager.NumberTo(layer, "specularStrength", _specStrength.Value, 0, 2); BuildMaterialManager.NumberTo(layer, "glossiness", _gloss.Value, 0, 1); layer["specularColor"] = Hex(_specColor);
        }, "material");
    }

    private void PushBlinnGlow() => _material.SetGlow(_blinnGlowOn.ButtonPressed, Hex(_blinnGlowColor), _blinnGlow.Value);

    private void PushUv(bool pbr)
    {
        var f = pbr ? _pbrUv : _blinnUv; var tabs = pbr ? _pbrUvTabs : _blinnUvTabs; var channel = (pbr ? PbrChannels : BlinnChannels)[Math.Clamp(tabs.Selected, 0, (pbr ? PbrChannels : BlinnChannels).Length - 1)];
        _material.SetUv(channel, f[0].Value, f[1].Value, f[2].Value, f[3].Value, f[4].Value);
    }

    private void ApplyPreset()
    {
        var (name, json) = BuildMaterialManager.Presets[Math.Max(0, _preset.Selected)];
        if (System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject preset && _material.ApplyMaterial(preset, "material: " + name) > 0) Said?.Invoke($"Material “{name}” enviado ao servidor.");
    }

    private void OnMode(int index)
    {
        if (_filling) return;
        if (index == 0) { if (_material.SetShading(true) > 0) Said?.Invoke("Modelo PBR."); } else if (index == 1) { if (_material.SetShading(false) > 0) Said?.Invoke("Modelo Blinn-Phong."); }
        ShowMode((int)index); Refresh();
    }

    private void ShowMode(int index) { _pbr.Visible = index == 0; _blinn.Visible = index == 1; _mediaBox.Visible = index == 2; }

    // ---- showing the surface ------------------------------------------------------------------------------------------------

    public void Refresh()
    {
        if (_material is null || _modes is null) return;
        if (_timer > 0) return; // the person is still changing things
        _filling = true;
        var any = _selection.Count > 0; _target.Text = "Alvo: " + _material.TargetName;
        if (!any) { _note.Text = "Selecione um objeto para dar textura a ele."; _filling = false; return; }
        var blinn = _material.IsBlinn; var mediaMode = _modes.Selected == 2;
        if (!mediaMode) { _modes.Select(blinn ? 1 : 0, notify: false); ShowMode(blinn ? 1 : 0); }
        foreach (var (key, slot) in _slots) FillSlot(slot, _material.MapOf(key));
        var color = _material.Text("color") is { } hex ? Color.FromHtml(hex) : Colors.White; var opacity = 100 - _material.Number("transparency", 1) * 100; var alpha = _material.Text("alphaMode");
        _pbrColor.Color = color; _blinnColor.Color = color; _pbrOpacity.SetValueNoSignal(Math.Round(opacity)); _blinnOpacity.SetValueNoSignal(Math.Round(opacity)); _alphaMode.Select(alpha == "blend" ? 1 : alpha == "mask" ? 2 : 0); _alphaCutoff.SetValueNoSignal(_material.Number("alphaCutoff", .5));
        _normalStrength.SetValueNoSignal(_material.Number("normalStrength", 1)); _blinnNormal.SetValueNoSignal(_material.Number("normalStrength", 1));
        _roughness.SetValueNoSignal(_material.Number("roughness", .7)); _metallic.SetValueNoSignal(_material.Number("metallic", 0)); _occlusion.SetValueNoSignal(_material.Number("aoStrength", 1)); _occlusion.Editable = _material.MapOf("ormMap").Asset is not null;
        _roughness.Editable = _metallic.Editable = _material.MapOf("ormMap").Asset is null;
        var glow = _material.Text("emission"); var glowColor = glow is not null ? Color.FromHtml(glow) : Colors.White;
        _glowColor.Color = glowColor; _glowStrength.SetValueNoSignal(glow is null ? 0 : _material.Number("emissionStrength", 1)); _blinnGlowColor.Color = glowColor; _blinnGlowOn.SetPressedNoSignal(glow is not null); _blinnGlow.SetValueNoSignal(_material.Number("emissionStrength", 1));
        _specColor.Color = _material.Text("specularColor") is { } sc ? Color.FromHtml(sc) : Colors.White; _specStrength.SetValueNoSignal(_material.Number("specularStrength", 1)); _gloss.SetValueNoSignal(_material.Number("glossiness", .4)); _fullBright.SetPressedNoSignal(_material.Flag("fullBright"));
        FillUv(true); FillUv(false);
        _note.Text = _selection.Count > 1 ? $"{_selection.Count} objetos: o que você mudar vale para todos." : "";
        _media?.Refresh();
        _filling = false;
    }

    private void FillSlot(Slot slot, (string? Asset, string? Builtin) map)
    {
        slot.Thumb.Texture = null;
        if (map.Builtin is { } b) { slot.Name.Text = BuiltInTextures.All.FirstOrDefault(x => x.Id == b).Label ?? b; slot.Thumb.Texture = BuiltInTextures.Get(b); }
        else if (map.Asset is { } a && Guid.TryParse(a, out var id)) { slot.Name.Text = "imagem " + a[..8]; (GetParent()?.GetNodeOrNull<TextureCache>("TextureCache") ?? GetTree().Root.FindChild("TextureCache", true, false) as TextureCache)?.Request(id, t => { if (IsInstanceValid(slot.Thumb)) slot.Thumb.Texture = t; }); }
        else slot.Name.Text = "nenhuma";
        slot.Clear.Disabled = map.Asset is null && map.Builtin is null;
    }

    private void FillUv(bool pbr)
    {
        if (_material is null) return; var was = _filling; _filling = true;
        var tabs = pbr ? _pbrUvTabs : _blinnUvTabs; if (tabs is null) { _filling = was; return; }
        var channels = pbr ? PbrChannels : BlinnChannels; var uv = _material.Uv(channels[Math.Clamp(tabs.Selected, 0, channels.Length - 1)]); var f = pbr ? _pbrUv : _blinnUv;
        f[0].SetValueNoSignal(uv.RepeatX); f[1].SetValueNoSignal(uv.RepeatY); f[2].SetValueNoSignal(uv.OffsetX); f[3].SetValueNoSignal(uv.OffsetY); f[4].SetValueNoSignal(uv.Rotation); _filling = was;
    }
}
