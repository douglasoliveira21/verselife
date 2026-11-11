using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// The RECURSOS tab: the light the object gives (colour, intensity, range, falloff, shadows), the reflection it casts, the shape and numbers of its physics and, for an imported
/// model with a skeleton, its animation. Every control changes the real object; a control that cannot apply (a model without animations, a physical object with no solid shape) is
/// disabled with the reason beside it, never faked.
/// </summary>
public partial class BuildResourcesTab : VBoxContainer
{
    private const double SendDelay = .45;

    private BuildSelectionManager _selection = null!;
    private BuildResourceManager _resources = null!;
    private BuildNetworkSynchronizer _sync = null!;
    private CheckBox _lightOn = null!, _shadow = null!, _reflectionOn = null!, _animationOn = null!, _loop = null!;
    private ColorPickerButton _color = null!;
    private SpinBox _energy = null!, _range = null!, _falloff = null!, _reflectIntensity = null!, _reflectRange = null!, _speed = null!, _mass = null!, _friction = null!, _bounce = null!;
    private OptionButton _shape = null!, _surface = null!, _clip = null!;
    private Button _play = null!;
    private Label _animationNote = null!, _physicsNote = null!, _note = null!;
    private Control _lightRows = null!, _reflectionRows = null!, _animationRows = null!;
    private CheckBox _projOn = null!, _flexOn = null!;
    private OptionButton _projTexture = null!;
    private SpinBox _projFov = null!, _projFocus = null!, _projAmbiance = null!;
    private Button _projLibrary = null!;
    private Label _projNote = null!;
    private Control _projRows = null!, _flexRows = null!;
    private readonly Dictionary<string, SpinBox> _flexSpins = [];
    private Guid? _projAsset;
    private BuildTexturePicker? _picker;
    private bool _pendingProjector, _pendingFlex, _projDraft;
    private bool _filling;
    private double _timer;
    private bool _pendingLight, _pendingReflection, _pendingAnimation, _pendingNumbers;
    private string _clipsKey = string.Empty;

    public event Action<string>? Said;

    // ---- test access ---------------------------------------------------------------------------------------------------------

    public bool LightOnForTesting => _lightOn.ButtonPressed;
    public bool LightRowsVisibleForTesting => _lightRows.Visible;
    public bool ShadowForTesting => _shadow.ButtonPressed;
    public bool ReflectionOnForTesting => _reflectionOn.ButtonPressed;
    public bool AnimationEnabledForTesting => !_animationOn.Disabled;
    public string AnimationNoteForTesting => _animationNote.Text;
    public IReadOnlyList<string> ClipsForTesting => [.. Enumerable.Range(0, _clip.ItemCount).Select(_clip.GetItemText)];
    public bool PlayingForTesting => _play.Text.Contains("Pausar");
    public string ShapeForTesting => _shape.Selected >= 0 ? _shape.GetItemMetadata(_shape.Selected).AsString() : string.Empty;
    public IReadOnlyList<string> ShapeChoicesForTesting => [.. Enumerable.Range(0, _shape.ItemCount).Where(i => !_shape.IsItemDisabled(i)).Select(i => _shape.GetItemMetadata(i).AsString())];
    public bool NumbersEditableForTesting => _mass.Editable;
    public string PhysicsNoteForTesting => _physicsNote.Text;
    public void ToggleLightForTesting(bool on) => _lightOn.ButtonPressed = on;
    public void ToggleShadowForTesting(bool on) => _shadow.ButtonPressed = on;
    public void SetLightForTesting(double energy, double range, double falloff, Color color) { _energy.Value = energy; _range.Value = range; _falloff.Value = falloff; _color.Color = color; _pendingLight = true; _timer = SendDelay; }
    public void ToggleReflectionForTesting(bool on) => _reflectionOn.ButtonPressed = on;
    public void SetReflectionForTesting(double intensity, double range) { _reflectIntensity.Value = intensity; _reflectRange.Value = range; }
    public void ToggleAnimationForTesting(bool on) => _animationOn.ButtonPressed = on;
    public void ChooseClipForTesting(int index) { _clip.Select(index); OnClipChosen(index); }
    public void PressPlayForTesting() => _play.EmitSignal(BaseButton.SignalName.Pressed);
    public void ToggleLoopForTesting(bool on) => _loop.ButtonPressed = on;
    public void SetSpeedForTesting(double speed) => _speed.Value = speed;
    public void ChooseShapeForTesting(string id) { for (var i = 0; i < _shape.ItemCount; i++) if (_shape.GetItemMetadata(i).AsString() == id) { _shape.Select(i); OnShapeChosen(i); } }
    public void SetNumbersForTesting(double mass, double friction, double bounce) { _mass.Value = mass; _friction.Value = friction; _bounce.Value = bounce; }
    public void FlushForTesting() => Flush();
    public bool ProjectorOnForTesting => _projOn.ButtonPressed;
    public bool FlexOnForTesting => _flexOn.ButtonPressed;
    public bool ProjectorRowsVisibleForTesting => _projRows.Visible;
    public void ToggleProjectorForTesting(bool on) => _projOn.ButtonPressed = on;
    public void ChooseProjectorPatternForTesting(string id) { for (var i = 1; i < _projTexture.ItemCount; i++) if (_projTexture.GetItemMetadata(i).AsString() == id) { _projTexture.Select(i); _projTexture.EmitSignal(OptionButton.SignalName.ItemSelected, i); } }
    public void SetProjectorForTesting(double fov, double focus, double ambiance) { _projFov.Value = fov; _projFocus.Value = focus; _projAmbiance.Value = ambiance; }
    public void ToggleFlexForTesting(bool on) => _flexOn.ButtonPressed = on;
    public void SetFlexForTesting(string key, double value) => _flexSpins[key].Value = value;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection, BuildResourceManager resources, BuildTexturePicker? picker = null) { _sync = sync; _selection = selection; _resources = resources; _picker = picker; }

    // ---- building -------------------------------------------------------------------------------------------------------------

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(Heading("LUZ"));
        _lightOn = Check("Emitir luz", "O objeto passa a iluminar o que está perto (até 16 luzes acesas por vez no seu jogo)"); _lightOn.Toggled += on => { if (_filling) return; _lightRows.Visible = on; _pendingLight = true; Flush(); }; AddChild(_lightOn);
        var light = new VBoxContainer(); light.AddThemeConstantOverride("separation", 6); _lightRows = light; AddChild(light);
        _color = new ColorPickerButton { Color = new Color("#FFE0A8"), CustomMinimumSize = new Vector2(120, 38), EditAlpha = false }; _color.ColorChanged += _ => Pend(ref _pendingLight); light.AddChild(Row("COR", _color));
        _energy = Spin(0, 8, .1, 1.5, "Quanto ilumina (0 a 8)"); _range = Spin(.5, 30, .5, 8, "Até onde a luz chega, em metros"); _falloff = Spin(.1, 4, .1, 1, "Atenuação: quanto a luz some com a distância");
        light.AddChild(Row("INTENSIDADE", _energy)); light.AddChild(Row("ALCANCE (m)", _range)); light.AddChild(Row("ATENUAÇÃO", _falloff));
        foreach (var spin in new[] { _energy, _range, _falloff }) spin.ValueChanged += _ => Pend(ref _pendingLight);
        _shadow = Check("Sombras", "A luz projeta sombras (só as 4 luzes com sombra mais próximas funcionam ao mesmo tempo; custa mais)"); _shadow.Toggled += _ => Pend(ref _pendingLight); light.AddChild(_shadow);
        _projOn = Check("Projetor", "A luz projeta uma imagem, como um slide (precisa da luz acesa)"); _projOn.Toggled += on => { if (_filling) return; _projRows.Visible = on; _projDraft = on; if (on) { _projNote.Text = "Escolha uma imagem para o projetor começar."; if (_projAsset is not null || _projTexture.Selected > 0) { _pendingProjector = true; Flush(); } } else { _pendingProjector = true; Flush(); } }; light.AddChild(_projOn);
        var projector = new VBoxContainer(); projector.AddThemeConstantOverride("separation", 6); _projRows = projector; light.AddChild(projector);
        _projTexture = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None, TooltipText = "Padrão do sistema que a luz projeta" }; _projTexture.AddItem("(escolha uma imagem)");
        foreach (var (id, label) in BuiltInTextures.All) { _projTexture.AddItem(label); _projTexture.SetItemMetadata(_projTexture.ItemCount - 1, id); }
        _projTexture.ItemSelected += _ => { if (_filling) return; _projAsset = null; _pendingProjector = true; Flush(); }; projector.AddChild(Row("IMAGEM", _projTexture));
        _projLibrary = new Button { Text = "Usar uma imagem minha…", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), TooltipText = "Escolhe uma textura do seu inventário" }; UiStyle.StyleButton(_projLibrary, UiStyle.ButtonKind.Secondary, 10, 5, 12);
        _projLibrary.Pressed += () => _picker?.Open(false, entry => { if (Guid.TryParse(entry.Asset, out var asset)) { _projAsset = asset; _pendingProjector = true; Flush(); } }); projector.AddChild(_projLibrary);
        _projFov = Spin(10, 120, 1, 45, "Abertura do cone de luz, em graus"); _projFocus = Spin(0, 30, .5, 10, "Distância em que a imagem fica nítida, em metros"); _projAmbiance = Spin(0, 1, .05, .1, "Brilho de fundo em volta da imagem");
        projector.AddChild(Row("ABERTURA (°)", _projFov)); projector.AddChild(Row("FOCO (m)", _projFocus)); projector.AddChild(Row("AMBIENTE", _projAmbiance));
        foreach (var spin in new[] { _projFov, _projFocus, _projAmbiance }) spin.ValueChanged += _ => Pend(ref _pendingProjector);
        _projNote = Note(); projector.AddChild(_projNote);
        AddChild(new HSeparator());

        AddChild(Heading("REFLEXO"));
        _reflectionOn = Check("Reflexo", "O objeto reflete o que está em volta nas superfícies brilhantes perto dele (até 4 reflexos ao mesmo tempo)"); _reflectionOn.Toggled += on => { if (_filling) return; _reflectionRows.Visible = on; _pendingReflection = true; Flush(); }; AddChild(_reflectionOn);
        var reflection = new VBoxContainer(); reflection.AddThemeConstantOverride("separation", 6); _reflectionRows = reflection; AddChild(reflection);
        _reflectIntensity = Spin(0, 1, .05, .6, "Força do reflexo (0 a 1)"); _reflectRange = Spin(2, 30, .5, 8, "Tamanho da área que se reflete, em metros");
        reflection.AddChild(Row("INTENSIDADE", _reflectIntensity)); reflection.AddChild(Row("ALCANCE (m)", _reflectRange));
        foreach (var spin in new[] { _reflectIntensity, _reflectRange }) spin.ValueChanged += _ => Pend(ref _pendingReflection);
        AddChild(new HSeparator());

        AddChild(Heading("FORMA FÍSICA"));
        _shape = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None, TooltipText = "A geometria usada nas colisões" };
        foreach (var (id, label) in BuildResourceManager.ShapeKinds) { _shape.AddItem(label); _shape.SetItemMetadata(_shape.ItemCount - 1, id); }
        _shape.ItemSelected += i => OnShapeChosen((int)i); AddChild(Row("TIPO", _shape));
        _mass = Spin(.01, 10000, .01, 1, "Massa, em quilos"); _friction = Spin(0, 1, .05, .5, "Atrito (0 desliza, 1 agarra)"); _bounce = Spin(0, 1, .05, 0, "Quique (0 não quica, 1 quica tudo)");
        _surface = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None, TooltipText = "Preenche atrito e quique de uma vez" }; _surface.AddItem("(nenhum)"); _surface.SetItemMetadata(0, "");
        foreach (var (key, (label, _, _)) in PhysicsRules.Materials) { _surface.AddItem(label); _surface.SetItemMetadata(_surface.ItemCount - 1, key); }
        _surface.ItemSelected += i => { if (_filling) return; if (PhysicsRules.Materials.TryGetValue(_surface.GetItemMetadata((int)i).AsString(), out var m)) { _filling = true; _friction.Value = m.Friction; _bounce.Value = m.Bounce; _filling = false; } Pend(ref _pendingNumbers); };
        AddChild(Row("MASSA (kg)", _mass)); AddChild(Row("ATRITO", _friction)); AddChild(Row("QUIQUE", _bounce)); AddChild(Row("MATERIAL", _surface));
        foreach (var spin in new[] { _mass, _friction, _bounce }) spin.ValueChanged += _ => Pend(ref _pendingNumbers);
        _physicsNote = Note(); AddChild(_physicsNote);
        AddChild(new HSeparator());

        AddChild(Heading("FLEXIBILIDADE (FLEXI)"));
        _flexOn = Check("Flexibilidade (Flexi)", "O objeto balança como uma bandeira ou uma planta: a base fica parada e o topo se inclina"); _flexOn.Toggled += on => { if (_filling) return; _flexRows.Visible = on; _pendingFlex = true; Flush(); }; AddChild(_flexOn);
        var flexBox = new VBoxContainer(); flexBox.AddThemeConstantOverride("separation", 6); _flexRows = flexBox; AddChild(flexBox);
        foreach (var (key, label, min, max, step, value, tip) in new (string, string, double, double, double, double, string)[] { ("softness", "MACIEZ", 0, 3, .1, 1, "Quanto o objeto cede"), ("gravity", "GRAVIDADE", -10, 10, .1, 0, "Quanto ele pende para baixo (negativo sobe)"), ("drag", "ATRITO DO AR", 0, 10, .1, 2, "Quanto o balanço é freado"), ("wind", "VENTO", 0, 10, .1, 0, "Quanto o vento o empurra"), ("tension", "TENSÃO", 0, 10, .1, 1, "Quanto ele volta para a posição"), ("forceX", "FORÇA X", -10, 10, .1, 0, "Empurrão constante no eixo X"), ("forceY", "FORÇA Y", -10, 10, .1, 0, "Empurrão constante no eixo Y"), ("forceZ", "FORÇA Z", -10, 10, .1, 0, "Empurrão constante no eixo Z") })
        { var spin = Spin(min, max, step, value, tip); _flexSpins[key] = spin; spin.ValueChanged += _ => Pend(ref _pendingFlex); flexBox.AddChild(Row(label, spin)); }
        AddChild(new HSeparator());
        AddChild(Heading("ANIMAÇÃO DE MESH"));
        _animationOn = Check("Animação de Mesh", "Toca uma animação do próprio modelo; todos veem o mesmo instante dela"); _animationOn.Toggled += on => { if (_filling) return; _animationRows.Visible = on; if (on && _clip.ItemCount > 0 && _clip.Selected < 0) _clip.Select(0); _pendingAnimation = true; Flush(); }; AddChild(_animationOn);
        _animationNote = Note(); AddChild(_animationNote);
        var animation = new VBoxContainer(); animation.AddThemeConstantOverride("separation", 6); _animationRows = animation; AddChild(animation);
        _clip = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None }; _clip.ItemSelected += i => OnClipChosen((int)i); animation.AddChild(Row("ANIMAÇÃO", _clip));
        _play = new Button { Text = " Reproduzir", Icon = UiStyle.Icon("play", UiStyle.Text, 22), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) }; UiStyle.StyleButton(_play, UiStyle.ButtonKind.Secondary, 10, 6, 14); _play.AddThemeFontSizeOverride("font_size", 18); _play.Pressed += TogglePlay; animation.AddChild(_play);
        _loop = Check("Repetir (loop)", "Recomeça quando termina"); _loop.Toggled += _ => Pend(ref _pendingAnimation); animation.AddChild(_loop);
        _speed = Spin(.1, 4, .1, 1, "Velocidade: 1 é a normal"); _speed.ValueChanged += _ => Pend(ref _pendingAnimation); animation.AddChild(Row("VELOCIDADE", _speed));
        _note = Note(); AddChild(_note);
        Refresh();
    }

    private void Pend(ref bool flag) { if (_filling) return; flag = true; _timer = SendDelay; }

    private static Label Heading(string text) { var label = new Label { Text = text }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 18); label.AddThemeColorOverride("font_color", UiStyle.AccentText); return label; }
    private static CheckBox Check(string text, string tip) { var box = new CheckBox { Text = " " + text, TooltipText = tip, FocusMode = FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 18); return box; }
    private static Label Note() { var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label; }

    private static SpinBox Spin(double min, double max, double step, double value, string tip)
    {
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, TooltipText = tip, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 38), SelectAllOnFocus = true, UpdateOnTextChanged = false };
        spin.GetLineEdit().AddThemeFontSizeOverride("font_size", 17); return spin;
    }

    private static HBoxContainer Row(string caption, Control control)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(130, 0), VerticalAlignment = VerticalAlignment.Center }; label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        row.AddChild(label); control.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(control); return row;
    }

    // ---- showing the selection ----------------------------------------------------------------------------------------------

    public void Refresh()
    {
        if (_selection is null || _lightOn is null) return;
        if (_pendingLight || _pendingProjector || _pendingFlex || _pendingReflection || _pendingAnimation || _pendingNumbers) return; // do not overwrite what the person is typing
        _filling = true;
        var views = _selection.Views().ToList(); var active = views.LastOrDefault(); var any = active is not null;
        foreach (var control in new Control[] { _lightOn, _reflectionOn, _shape, _surface, _flexOn }) SetDisabled(control, !any);
        if (any)
        {
            var state = active!.State; var light = BuildResourceManager.ReadLight(state); var reflection = BuildResourceManager.ReadReflection(state); var physics = BuildResourceManager.ReadPhysics(state);
            _lightOn.SetPressedNoSignal(light.On); _lightRows.Visible = light.On; _color.Color = Color.FromHtml(light.Color); _energy.SetValueNoSignal(light.Energy); _range.SetValueNoSignal(light.Range); _falloff.SetValueNoSignal(light.Falloff); _shadow.SetPressedNoSignal(light.Shadow);
            _reflectionOn.SetPressedNoSignal(reflection.On); _reflectionRows.Visible = reflection.On; _reflectIntensity.SetValueNoSignal(reflection.Intensity); _reflectRange.SetValueNoSignal(reflection.Range);
            var mesh = views.All(v => v.State.PrimitiveType == "mesh");
            for (var i = 0; i < _shape.ItemCount; i++) { _shape.SetItemDisabled(i, _shape.GetItemMetadata(i).AsString() == "triangle" && !mesh); if (_shape.GetItemMetadata(i).AsString() == physics.Shape) _shape.Select(i); }
            var simulated = views.All(v => BuildResourceManager.ReadPhysics(v.State).Physical);
            foreach (var spin in new[] { _mass, _friction, _bounce }) spin.Editable = simulated; _surface.Disabled = !simulated;
            _mass.SetValueNoSignal(physics.Mass); _friction.SetValueNoSignal(physics.Friction); _bounce.SetValueNoSignal(physics.Bounce);
            _surface.Select(Math.Max(0, PhysicsRules.Materials.Keys.ToList().IndexOf(physics.Material) + 1));
            _physicsNote.Text = simulated ? "A ilha simula este objeto com esta massa, atrito e quique." : "Massa, atrito e quique só valem para objetos com Física ligada (aba OBJETO).";
            var proj = BuildResourceManager.ReadProjector(state); if (proj.On) _projDraft = false; _projOn.SetPressedNoSignal(proj.On || _projDraft); _projRows.Visible = light.On && (proj.On || _projDraft); _projAsset = proj.Asset; _projFov.SetValueNoSignal(proj.Fov); _projFocus.SetValueNoSignal(proj.Focus); _projAmbiance.SetValueNoSignal(proj.Ambiance);
            _projTexture.Select(proj.Asset is null && proj.Builtin is { } pb ? Math.Max(0, 1 + Array.FindIndex(BuiltInTextures.All, t => t.Id == pb)) : 0); _projNote.Text = proj.Asset is not null ? "Imagem do seu inventário em uso." : string.Empty;
            var flexNow = BuildResourceManager.ReadFlex(state); _flexOn.SetPressedNoSignal(flexNow is not null); _flexRows.Visible = flexNow is not null; var shownFlex = flexNow ?? new FlexSettings(1, 0, 2, 0, 1, 0, 0, 0);
            foreach (var (key, value) in new (string, float)[] { ("softness", shownFlex.Softness), ("gravity", shownFlex.Gravity), ("drag", shownFlex.Drag), ("wind", shownFlex.Wind), ("tension", shownFlex.Tension), ("forceX", shownFlex.ForceX), ("forceY", shownFlex.ForceY), ("forceZ", shownFlex.ForceZ) }) _flexSpins[key].SetValueNoSignal(value);
            RefreshAnimation(active, mesh);
        }
        else { _lightRows.Visible = _reflectionRows.Visible = _animationRows.Visible = _flexRows.Visible = false; _animationOn.Disabled = true; _animationNote.Text = string.Empty; _physicsNote.Text = string.Empty; _note.Text = "Selecione um objeto para configurar luz, reflexo, física e animação."; }
        if (any) _note.Text = views.Count > 1 ? $"{views.Count} objetos: o que você mudar vale para todos." : string.Empty;
        _filling = false;
    }

    private static void SetDisabled(Control control, bool disabled) { if (control is BaseButton b) b.Disabled = disabled; else if (control is OptionButton o) o.Disabled = disabled; }

    private void RefreshAnimation(BuildObjectView active, bool mesh)
    {
        var clips = active.AnimationClips; var bones = active.SkeletonBones; var model = BuildResourceManager.ReadAnimation(active.State);
        var key = string.Join('|', clips) + "#" + active.State.Id; if (key != _clipsKey) { _clipsKey = key; _clip.Clear(); foreach (var name in clips) _clip.AddItem(name); }
        var capable = mesh && clips.Count > 0;
        _animationOn.Disabled = !capable && !model.On; _animationOn.SetPressedNoSignal(model.On);
        _animationNote.Text = !mesh ? "Só modelos importados (malha) com esqueleto e animações podem ser animados."
            : !active.ModelLoaded ? "O modelo ainda está carregando…" : clips.Count == 0 ? (bones > 0 ? $"O modelo tem esqueleto ({bones} ossos) mas nenhuma animação." : "Este modelo não tem esqueleto nem animações.")
            : $"Esqueleto com {bones} ossos e {clips.Count} animação(ões).";
        _animationRows.Visible = model.On && capable;
        if (model.Clip.Length > 0) for (var i = 0; i < _clip.ItemCount; i++) if (_clip.GetItemText(i) == model.Clip) _clip.Select(i);
        _loop.SetPressedNoSignal(model.Loop); _speed.SetValueNoSignal(model.Speed); _play.Text = model.Playing ? " Pausar" : " Reproduzir"; _play.Icon = UiStyle.Icon(model.Playing ? "minus" : "play", UiStyle.Text, 22);
    }

    // ---- what the person changes ------------------------------------------------------------------------------------------

    private void OnShapeChosen(int index)
    {
        if (_filling) return;
        var (sent, problem) = _resources.SetPhysicsShape(_shape.GetItemMetadata(index).AsString());
        if (problem is not null) { Said?.Invoke(problem); Refresh(); return; }
        if (sent > 0) Said?.Invoke($"Forma física: {BuildResourceManager.ShapeKinds.First(k => k.Id == _shape.GetItemMetadata(index).AsString()).Label.ToLowerInvariant()}.");
    }

    private void OnClipChosen(int index) { if (_filling || index < 0) return; _pendingAnimation = true; Flush(); }

    private void TogglePlay()
    {
        var active = _selection.Views().LastOrDefault(); if (active is null || _clip.Selected < 0) return;
        var playing = _play.Text.Contains("Pausar");
        var position = active.AnimationDriver?.Position ?? 0; // pausing freezes where it is; playing goes on from there
        _play.Text = playing ? " Reproduzir" : " Pausar"; _animationOffset = position; _pendingAnimation = true; _togglePlay = true; Flush();
    }

    private double _animationOffset; private bool _togglePlay;

    public override void _Process(double delta)
    {
        if (_timer > 0) { _timer -= delta; if (_timer <= 0) Flush(); }
        else if (_selection is not null && _selection.Count > 0 && Engine.GetProcessFrames() % 30 == 0) { var active = _selection.Views().LastOrDefault(); if (active is not null && string.Join('|', active.AnimationClips) + "#" + active.State.Id != _clipsKey) Refresh(); } // the model finished loading
    }

    private float F(string key) => (float)_flexSpins[key].Value;

    private void Flush()
    {
        _timer = 0;
        var projector = _pendingProjector; var flex = _pendingFlex; _pendingProjector = _pendingFlex = false;
        var light = _pendingLight; var reflection = _pendingReflection; var animation = _pendingAnimation; var numbers = _pendingNumbers; _pendingLight = _pendingReflection = _pendingAnimation = _pendingNumbers = false;
        if (light && _resources.SetLight(new BuildResourceManager.LightModel(_lightOn.ButtonPressed, "#" + _color.Color.ToHtml(false), (float)_energy.Value, (float)_range.Value, (float)_falloff.Value, _shadow.ButtonPressed)) > 0) Said?.Invoke(_lightOn.ButtonPressed ? "Luz enviada ao servidor." : "Luz desligada.");
        if (projector && _resources.SetProjector(new BuildResourceManager.ProjectorModel(_projOn.ButtonPressed, _projAsset is null && _projTexture.Selected > 0 ? _projTexture.GetItemMetadata(_projTexture.Selected).AsString() : null, _projAsset, (float)_projFov.Value, (float)_projFocus.Value, (float)_projAmbiance.Value)) > 0) Said?.Invoke(_projOn.ButtonPressed ? "Projetor enviado ao servidor." : "Projetor desligado.");
        if (flex && _resources.SetFlex(_flexOn.ButtonPressed ? new FlexSettings(F("softness"), F("gravity"), F("drag"), F("wind"), F("tension"), F("forceX"), F("forceY"), F("forceZ")) : null) > 0) Said?.Invoke(_flexOn.ButtonPressed ? "Flexibilidade enviada ao servidor." : "Flexibilidade desligada.");
        if (reflection && _resources.SetReflection(new BuildResourceManager.ReflectionModel(_reflectionOn.ButtonPressed, (float)_reflectIntensity.Value, (float)_reflectRange.Value)) > 0) Said?.Invoke(_reflectionOn.ButtonPressed ? "Reflexo enviado ao servidor." : "Reflexo desligado.");
        if (animation)
        {
            var playing = _togglePlay ? _play.Text.Contains("Pausar") : BuildResourceManager.ReadAnimation(_selection.Views().LastOrDefault()?.State ?? new BuildObjectState()).Playing;
            var offset = _togglePlay ? _animationOffset : 0; _togglePlay = false;
            var clip = _clip.Selected >= 0 ? _clip.GetItemText(_clip.Selected) : string.Empty;
            if (_resources.SetAnimation(new BuildResourceManager.AnimationModel(_animationOn.ButtonPressed, clip, playing, _loop.ButtonPressed, _speed.Value, offset)) > 0) Said?.Invoke(_animationOn.ButtonPressed ? (playing ? "Animação tocando para todos." : "Animação pausada.") : "Animação desligada.");
        }
        if (numbers && _resources.SetPhysicsNumbers((float)_mass.Value, (float)_friction.Value, (float)_bounce.Value, _surface.GetItemMetadata(Math.Max(0, _surface.Selected)).AsString()) > 0) Said?.Invoke("Massa, atrito e quique enviados ao servidor.");
        Refresh();
    }
}
