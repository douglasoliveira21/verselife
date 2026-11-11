using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// The OBJETO tab: the four switches (Travar, Física, Temporário, Fantasma), the kind of primitive, position, size and rotation as numbers, the radius, the parametric shape
/// controls (only the ones the shape has), alignment of several objects and copy and paste of properties. Typing or stepping a number is previewed by the server's answer: the
/// values wait a moment (so a burst of clicks is one update) and are then sent as one update; the shape is rebuilt for everybody from what the server stored.
/// </summary>
public partial class BuildObjectTab : VBoxContainer
{
    private const double SendDelay = .45;

    private BuildSelectionManager _selection = null!;
    private BuildObjectProperties _props = null!;
    private BuildGeometryManager _geometry = null!;
    private BuildPhysicsManager _physics = null!;
    private BuildAlignManager _align = null!;
    private BuildNetworkSynchronizer _sync = null!;
    private Func<Vector3, float?> _groundAt = _ => null;

    private CheckBox _locked = null!, _physical = null!, _temporary = null!, _ghost = null!;
    private Label _lifetime = null!, _note = null!;
    private OptionButton _type = null!;
    private readonly SpinBox[] _position = new SpinBox[3], _size = new SpinBox[3], _rotation = new SpinBox[3];
    private SpinBox _radius = null!;
    private Control _radiusRow = null!, _transformBox = null!, _alignBox = null!;
    private VBoxContainer _shapeBox = null!;
    private ConfirmationDialog _confirm = null!;
    private readonly Dictionary<string, SpinBox> _shapeSpins = [];
    private string _shapeFor = string.Empty;
    private bool _filling;
    private string? _pendingType;
    private bool _pendingTransform, _pendingRadius;
    private readonly Dictionary<string, double> _pendingShape = [];
    private double _timer;

    public event Action<string>? Said;

    // ---- test access -----------------------------------------------------------------------------------------------------

    public bool LockedForTesting => _locked.ButtonPressed;
    public bool PhysicalForTesting => _physical.ButtonPressed;
    public bool TemporaryForTesting => _temporary.ButtonPressed;
    public bool GhostForTesting => _ghost.ButtonPressed;
    public string LifetimeForTesting => _lifetime.Text;
    public string NoteForTesting => _note.Text;
    public string TypeForTesting => _type.Selected >= 0 ? _type.GetItemMetadata(_type.Selected).AsString() : string.Empty;
    public bool TypeEditableForTesting => !_type.Disabled;
    public bool TransformEditableForTesting => _position[0].Editable;
    public bool RadiusVisibleForTesting => _radiusRow.Visible;
    public double RadiusForTesting { get => _radius.Value; set => _radius.Value = value; }
    public bool AlignVisibleForTesting => _alignBox.Visible;
    public IReadOnlyCollection<string> ShapeKeysForTesting => _shapeSpins.Keys;
    public IReadOnlyList<string> ShapeLabelsForTesting => [.. _shapeLabels];
    private readonly List<string> _shapeLabels = [];
    public double PositionForTesting(int axis) => _position[axis].Value;
    public void SetPositionForTesting(double x, double y, double z) { _position[0].Value = x; _position[1].Value = y; _position[2].Value = z; }
    public void SetSizeForTesting(double x, double y, double z) { _size[0].Value = x; _size[1].Value = y; _size[2].Value = z; }
    public void SetRotationForTesting(double x, double y, double z) { _rotation[0].Value = x; _rotation[1].Value = y; _rotation[2].Value = z; }
    public void SetShapeForTesting(string key, double value) { if (_shapeSpins.TryGetValue(key, out var spin)) spin.Value = value; }
    public double ShapeValueForTesting(string key) => _shapeSpins[key].Value;
    public void ToggleForTesting(string which, bool on) { var box = which switch { "locked" => _locked, "physical" => _physical, "temporary" => _temporary, _ => _ghost }; box.ButtonPressed = on; }
    public void ChooseTypeForTesting(string type) { for (var i = 0; i < _type.ItemCount; i++) if (_type.GetItemMetadata(i).AsString() == type) { _type.Select(i); OnTypeChosen(i); } }
    public void ConfirmTypeForTesting() => _confirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
    public bool ConfirmOpenForTesting => _confirm.Visible;
    public string ConfirmTextForTesting => _confirm.DialogText;
    public void FlushForTesting() => Flush();
    public void PressAlignForTesting(AlignAxis axis, AlignEdge edge) => _align.Align(axis, edge);
    public void PressCopyForTesting() => _align.CopyProperties();
    public void PressPasteForTesting() => _align.PasteProperties();
    public void PressResetShapeForTesting() { if (_geometry.Reset() > 0) Said?.Invoke("Forma restaurada."); }

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection, BuildObjectProperties props, BuildGeometryManager geometry, BuildPhysicsManager physics, BuildAlignManager align, Func<Vector3, float?> groundAt)
    { _sync = sync; _selection = selection; _props = props; _geometry = geometry; _physics = physics; _align = align; _groundAt = groundAt; }

    // ---- building the tab ------------------------------------------------------------------------------------------------

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(Heading("PROPRIEDADES"));
        var flags = new GridContainer { Columns = 2 }; flags.AddThemeConstantOverride("h_separation", 24); AddChild(flags);
        _locked = Check("Travar", "Protege contra edição acidental: ninguém move, muda, vincula ou exclui até destravar"); _physical = Check("Física", "O servidor da ilha o faz cair, rolar e ser empurrado (precisa de colisão sólida)");
        _temporary = Check("Temporário", "O servidor remove o objeto sozinho depois de 10 minutos"); _ghost = Check("Fantasma", "Sem colisão sólida: dá para atravessar, e o objeto continua visível");
        foreach (var box in new[] { _locked, _physical, _temporary, _ghost }) flags.AddChild(box);
        _locked.Toggled += on => OnFlag("locked", on); _physical.Toggled += on => OnFlag("physical", on); _temporary.Toggled += on => OnFlag("temporary", on); _ghost.Toggled += on => OnFlag("ghost", on);
        _lifetime = new Label { Visible = false }; _lifetime.AddThemeFontSizeOverride("font_size", 16); _lifetime.AddThemeColorOverride("font_color", UiStyle.Orange); AddChild(_lifetime);
        AddChild(new HSeparator());

        AddChild(Heading("TIPO DE OBJETO"));
        _type = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.None, TooltipText = "A forma do objeto; trocar mantém tamanho, material e o que ainda serve" };
        foreach (var entry in BuildPrimitiveFactory.Entries.Where(e => BuildGeometryManager.IsParametric(e.Type))) { _type.AddIconItem(BuildIcons.Primitive(entry.Type, 28), entry.Label); _type.SetItemMetadata(_type.ItemCount - 1, entry.Type); }
        _type.ItemSelected += i => OnTypeChosen((int)i); AddChild(_type);
        _confirm = new ConfirmationDialog { Title = "Trocar a forma", OkButtonText = "Trocar", CancelButtonText = "Cancelar" }; _confirm.Confirmed += ApplyType; AddChild(_confirm);

        _transformBox = new VBoxContainer(); ((VBoxContainer)_transformBox).AddThemeConstantOverride("separation", 6); AddChild(_transformBox);
        _transformBox.AddChild(Heading("POSIÇÃO")); _transformBox.AddChild(Triple(_position, -100000, 100000, .01, 2));
        _transformBox.AddChild(Heading("TAMANHO")); _transformBox.AddChild(Triple(_size, BuildTransformManager.MinScale, BuildTransformManager.MaxScale, .01, 2));
        _transformBox.AddChild(Heading("ROTAÇÃO (graus)")); _transformBox.AddChild(Triple(_rotation, -360, 360, .5, 1));
        foreach (var spin in _position.Concat(_size).Concat(_rotation)) spin.ValueChanged += _ => { if (!_filling) { _pendingTransform = true; _timer = SendDelay; } };
        _radius = new SpinBox { MinValue = .01, MaxValue = 32, Step = .01, CustomMinimumSize = new Vector2(120, 38), SelectAllOnFocus = true, TooltipText = "Metade da largura da forma redonda" }; _radius.GetLineEdit().AddThemeFontSizeOverride("font_size", 17);
        _radius.ValueChanged += _ => { if (!_filling) { _pendingRadius = true; _timer = SendDelay; } };
        _radiusRow = Row("TAMANHO DO RAIO", _radius); AddChild(_radiusRow);

        _shapeBox = new VBoxContainer(); _shapeBox.AddThemeConstantOverride("separation", 6); AddChild(_shapeBox);

        _alignBox = new VBoxContainer { Visible = false }; ((VBoxContainer)_alignBox).AddThemeConstantOverride("separation", 6); AddChild(_alignBox);
        _alignBox.AddChild(Heading("ALINHAR"));
        foreach (var (axis, name) in new[] { (AlignAxis.X, "X"), (AlignAxis.Y, "Y"), (AlignAxis.Z, "Z") })
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4); row.AddChild(Small($"Eixo {name}", 70));
            foreach (var (edge, label) in new[] { (AlignEdge.Min, "Início"), (AlignEdge.Center, "Centro"), (AlignEdge.Max, "Fim") }) { var captured = (axis, edge); row.AddChild(Button(label, $"Alinha pelo {label.ToLowerInvariant()} no eixo {name}", () => _align.Align(captured.axis, captured.edge))); }
            row.AddChild(Button("Distribuir", $"Espalha por igual no eixo {name} (3 ou mais)", () => _align.Distribute(axis))); _alignBox.AddChild(row);
        }
        AddChild(Button("Pousar no chão", "Baixa ou sobe a seleção até a base tocar o chão", () => _align.RestOnGround(_groundAt)));
        var props = new HBoxContainer(); props.AddThemeConstantOverride("separation", 8); AddChild(props);
        var copy = Button("Copiar propriedades", "Guarda o material, a forma e a física do objeto", () => _align.CopyProperties()); copy.SizeFlagsHorizontal = SizeFlags.ExpandFill; props.AddChild(copy);
        var paste = Button("Colar propriedades", "Dá o que foi copiado à seleção", () => _align.PasteProperties()); paste.SizeFlagsHorizontal = SizeFlags.ExpandFill; props.AddChild(paste);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill }; _note.AddThemeFontSizeOverride("font_size", 16); _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_note);
        Refresh();
    }

    private static Label Heading(string text) { var label = new Label { Text = text }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 18); label.AddThemeColorOverride("font_color", UiStyle.AccentText); return label; }
    private static Label Small(string text, int width) { var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0), VerticalAlignment = VerticalAlignment.Center }; label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label; }
    private static CheckBox Check(string text, string tip) { var box = new CheckBox { Text = " " + text, TooltipText = tip, FocusMode = FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 18); return box; }

    private static Button Button(string text, string tip, Action action)
    {
        var button = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 8, 4, 12); button.AddThemeFontSizeOverride("font_size", 16); button.Pressed += action; return button;
    }

    private static HBoxContainer Row(string caption, Control control)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); row.AddChild(Small(caption, 150)); control.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(control); return row;
    }

    private static HBoxContainer Triple(SpinBox[] boxes, double min, double max, double step, int decimals)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        for (var i = 0; i < 3; i++)
        {
            row.AddChild(Small("XYZ"[i].ToString(), 16));
            var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(90, 38), SelectAllOnFocus = true, UpdateOnTextChanged = false };
            spin.GetLineEdit().AddThemeFontSizeOverride("font_size", 17); boxes[i] = spin; row.AddChild(spin);
        }
        return row;
    }

    // ---- showing the selection -------------------------------------------------------------------------------------------

    public void Refresh()
    {
        if (_selection is null || _locked is null) return;
        if (_pendingTransform || _pendingRadius || _pendingShape.Count > 0) return; // do not overwrite what the person is typing
        _filling = true;
        var views = _selection.Views().ToList(); var single = views.Count == 1; var active = views.LastOrDefault()?.State; var mine = views.Count > 0 && views.All(v => v.State.OwnerUserId == _sync.MyUserId);
        var flags = _physics.Read();
        _locked.SetPressedNoSignal(flags?.Locked == true); _physical.SetPressedNoSignal(flags?.Physical == true); _temporary.SetPressedNoSignal(flags?.Temporary == true); _ghost.SetPressedNoSignal(flags?.Ghost == true);
        foreach (var box in new[] { _locked, _physical, _temporary, _ghost }) box.Disabled = views.Count == 0;
        var left = views.Where(v => v.State.ExpiresAt is not null).Select(v => v.State.ExpiresAt!.Value - DateTimeOffset.UtcNow).DefaultIfEmpty().Min();
        _lifetime.Visible = views.Any(v => v.State.ExpiresAt is not null);
        if (_lifetime.Visible) _lifetime.Text = left <= TimeSpan.Zero ? "Temporário: o servidor vai removê-lo agora." : $"Temporário: o servidor o remove em {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min.";

        var parametric = active is not null && BuildGeometryManager.IsParametric(active.PrimitiveType);
        _type.Disabled = !parametric || views.Any(v => !BuildGeometryManager.IsParametric(v.State.PrimitiveType)) || !mine && views.Count > 0 && false;
        if (parametric) for (var i = 0; i < _type.ItemCount; i++) if (_type.GetItemMetadata(i).AsString() == active!.PrimitiveType) _type.Select(i);
        _transformBox.Visible = views.Count > 0;
        foreach (var spin in _position.Concat(_size).Concat(_rotation)) spin.Editable = single;
        if (active is not null)
        {
            _position[0].SetValueNoSignal(active.PositionX); _position[1].SetValueNoSignal(active.PositionY); _position[2].SetValueNoSignal(active.PositionZ);
            _size[0].SetValueNoSignal(active.ScaleX); _size[1].SetValueNoSignal(active.ScaleY); _size[2].SetValueNoSignal(active.ScaleZ);
            _rotation[0].SetValueNoSignal(Wrap(active.RotationX)); _rotation[1].SetValueNoSignal(Wrap(active.RotationY)); _rotation[2].SetValueNoSignal(Wrap(active.RotationZ));
        }
        _radiusRow.Visible = active is not null && BuildGeometryManager.HasRadius(active.PrimitiveType) && views.All(v => BuildGeometryManager.HasRadius(v.State.PrimitiveType));
        if (_radiusRow.Visible) { _radius.SetValueNoSignal(BuildGeometryManager.RadiusOf(active!)); _radius.Editable = single; }
        _alignBox.Visible = views.Count >= 2;
        BuildShape(active?.PrimitiveType, views);
        _note.Text = views.Count == 0 ? "Selecione um objeto para ver e editar a forma, a posição e o tamanho." : !single ? $"{views.Count} objetos: números de posição, tamanho e rotação só com um objeto; a forma vale para todos." : string.Empty;
        _filling = false;
    }

    private static double Wrap(double degrees) { var d = degrees % 360; return d > 180 ? d - 360 : d <= -180 ? d + 360 : d; }

    /// <summary>The shape controls of the active object's primitive, grouped; rebuilt only when the kind of primitive changes.</summary>
    private void BuildShape(string? type, IReadOnlyList<BuildObjectView> views)
    {
        var key = type is not null && BuildGeometryManager.IsParametric(type) ? type : string.Empty;
        if (key != _shapeFor)
        {
            _shapeFor = key; _shapeSpins.Clear(); _shapeLabels.Clear(); foreach (var child in _shapeBox.GetChildren()) { _shapeBox.RemoveChild(child); child.QueueFree(); }
            if (key.Length > 0)
            {
                _shapeBox.AddChild(Heading("FORMA"));
                foreach (var group in BuildGeometryManager.For(key).GroupBy(p => p.Group))
                {
                    var caption = Small(group.Key, 0); caption.AddThemeColorOverride("font_color", UiStyle.Text); _shapeBox.AddChild(caption);
                    var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); _shapeBox.AddChild(row);
                    foreach (var param in group)
                    {
                        var captured = param; row.AddChild(Small(param.Label, 0));
                        var spin = new SpinBox { MinValue = param.Min * param.Display, MaxValue = param.Max * param.Display, Step = param.Step * param.Display, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(80, 36), SelectAllOnFocus = true, TooltipText = param.Tip, UpdateOnTextChanged = false };
                        spin.GetLineEdit().AddThemeFontSizeOverride("font_size", 16);
                        spin.ValueChanged += v => { if (!_filling) { _pendingShape[captured.Key] = v / captured.Display; _timer = SendDelay; } };
                        _shapeSpins[$"{param.Key}"] = spin; _shapeLabels.Add($"{param.Group}: {param.Label}"); row.AddChild(spin);
                    }
                }
                _shapeBox.AddChild(Button("Restaurar forma", "Volta todos os controles da forma ao padrão", () => { if (_geometry.Reset() > 0) Said?.Invoke("Forma restaurada."); }));
            }
        }
        if (key.Length == 0 || views.Count == 0) return;
        var state = views[^1].State;
        foreach (var param in BuildGeometryManager.For(key)) _shapeSpins[param.Key].SetValueNoSignal(BuildGeometryManager.StoredValue(state, param) * param.Display);
    }

    // ---- what the person changes -----------------------------------------------------------------------------------------

    private void OnFlag(string which, bool on)
    {
        if (_filling) return;
        if (which == "locked") { if (_physics.SetLocked(on)) Said?.Invoke(on ? "Protegido contra edição acidental." : "Destravado."); return; }
        var (sent, problem) = which switch { "physical" => _physics.SetPhysical(on), "temporary" => _physics.SetTemporary(on), _ => _physics.SetGhost(on) };
        if (problem is not null) { Said?.Invoke(problem); Refresh(); return; }
        if (sent > 0) Said?.Invoke(which switch { "physical" => on ? "Física ligada: a ilha vai simulá-lo." : "Física desligada.", "temporary" => on ? "Temporário: some sozinho em 10 minutos." : "Não é mais temporário.", _ => on ? "Fantasma: dá para atravessar." : "Sólido de novo." });
    }

    private void OnTypeChosen(int index)
    {
        if (_filling) return;
        var views = _selection.Views().ToList(); if (views.Count == 0) return;
        var newType = _type.GetItemMetadata(index).AsString(); _pendingType = newType;
        var lost = views.SelectMany(v => BuildGeometryManager.Discarded(v.State.GeometryJson, v.State.PrimitiveType, newType)).Distinct().ToList();
        if (lost.Count == 0) { ApplyType(); return; }
        _confirm.DialogText = $"Ao trocar para {BuildPrimitiveFactory.LabelOf(newType)}, estes ajustes não servem mais e serão descartados:\n\n• {string.Join("\n• ", lost)}\n\nTamanho, material e o resto continuam."; _confirm.PopupCentered();
    }

    private void ApplyType()
    {
        if (_pendingType is not { } type) return; _pendingType = null;
        var sent = _geometry.ChangeType(type); if (sent > 0) Said?.Invoke($"Forma trocada para {BuildPrimitiveFactory.LabelOf(type)}.");
    }

    public override void _Process(double delta)
    {
        if (_timer <= 0) return;
        _timer -= delta; if (_timer <= 0) Flush();
    }

    /// <summary>Sends what was typed since the last time as one update per object.</summary>
    private void Flush()
    {
        _timer = 0;
        var transform = _pendingTransform; var radius = _pendingRadius; var shape = new Dictionary<string, double>(_pendingShape);
        _pendingTransform = false; _pendingRadius = false; _pendingShape.Clear();
        if (transform && _selection.Views().ToList() is { Count: 1 } one)
        {
            var before = one[0].State; var after = BuildPersistenceManager.Clone(before);
            after.PositionX = (float)_position[0].Value; after.PositionY = (float)_position[1].Value; after.PositionZ = (float)_position[2].Value;
            after.ScaleX = (float)Math.Clamp(_size[0].Value, BuildTransformManager.MinScale, BuildTransformManager.MaxScale); after.ScaleY = (float)Math.Clamp(_size[1].Value, BuildTransformManager.MinScale, BuildTransformManager.MaxScale); after.ScaleZ = (float)Math.Clamp(_size[2].Value, BuildTransformManager.MinScale, BuildTransformManager.MaxScale);
            after.RotationX = (float)_rotation[0].Value; after.RotationY = (float)_rotation[1].Value; after.RotationZ = (float)_rotation[2].Value;
            if (_sync.Update(after)) { Said?.Invoke("Posição, tamanho e rotação enviados ao servidor."); Changed?.Invoke(before, after); }
        }
        if (radius && _geometry.SetRadius(_radius.Value) > 0) Said?.Invoke("Raio enviado ao servidor.");
        if (shape.Count > 0 && _geometry.Set(shape) > 0) Said?.Invoke("Forma enviada ao servidor: a malha é refeita para todos.");
        Refresh();
    }

    /// <summary>A single object was changed by typing numbers (before and after, for the history).</summary>
    public event Action<BuildObjectState, BuildObjectState>? Changed;
}
