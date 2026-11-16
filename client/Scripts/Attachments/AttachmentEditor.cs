using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Build;

namespace LifeVerse.Client.Scripts.Attachments;

public enum AttachmentTool { Move, Rotate, Scale, Select }

/// <summary>
/// The editor of where an object sits on the body. The camera goes close to the part of the body, the object gets the handles of the build editor (arrows, rings, boxes) in the space of its attachment
/// point, and a panel shows and takes the numbers. What is changed is shown at once and kept only on "Salvar", which asks the server; "Cancelar" (or Esc) puts everything back.
/// </summary>
public sealed partial class AttachmentEditor : Node
{
    public PlayerController? Player { get; set; }
    public Action<string>? Tell { get; set; }

    private static readonly (string Label, float Step)[] RotationSteps = [("Livre", 0), ("1°", 1), ("5°", 5), ("15°", 15), ("45°", 45)];
    private static readonly (string Label, float Step)[] PositionSteps = [("Livre", 0), ("1 cm", .01f), ("5 cm", .05f), ("10 cm", .1f)];

    private FloatingWindow _window = null!;
    private BuildGizmoController _gizmo = null!;
    private Label _title = null!, _where = null!, _status = null!;
    private readonly SpinBox[] _position = new SpinBox[3], _rotation = new SpinBox[3], _scale = new SpinBox[3];
    private SpinBox _sway = null!;
    private float _swayStart;
    /// <summary>The sway being set (0 to 1): a light movement of the object with the body, kept with the placement.</summary>
    public float Sway => (float)_sway.Value;
    private bool SwayChanged => Target is not null && Mathf.Abs(Sway - _swayStart) > .001f;
    public SpinBox SwayField => _sway;
    private readonly Dictionary<AttachmentTool, Button> _tools = [];
    private Button _undo = null!, _redo = null!, _save = null!;
    private CheckBox _proportions = null!, _solo = null!;
    private OptionButton _rotationStep = null!, _positionStep = null!;
    private bool _filling, _saving, _liveDirty, _sentLive;
    private ulong _lastLive;
    private const ulong LiveEveryMs = 100;
    /// <summary>Tests: where the live preview goes instead of the island.</summary>
    public Action<Guid, Placement>? LiveSink { get; set; }
    public int LiveSent { get; private set; }

    // a drag in progress
    private BuildGizmoHandle _handle = BuildGizmoHandle.None;
    private Placement _start;
    private Vector3 _center, _axis, _hit0;
    private Basis _frame0 = Basis.Identity;
    private float _t0, _angle0, _metres = 1;
    private bool _middleDrag;

    public AttachmentData? Target { get; private set; }
    public AttachmentEditModel? Model { get; private set; }
    public AttachmentTool Tool { get; private set; } = AttachmentTool.Move;
    public bool IsOpen => Target is not null;
    public bool Dragging => _handle != BuildGizmoHandle.None;
    public BuildGizmoController Gizmo => _gizmo;
    public string Status => _status.Text;
    public bool SoloOthers { get => _solo.ButtonPressed; set => _solo.ButtonPressed = value; }

    private AvatarManager? Manager => GetNodeOrNull<AvatarManager>("/root/AvatarManager");
    private AttachmentStore? Store => Manager?.Attachments;
    private AvatarRig? Rig => Player?.Rig;

    public override void _Ready() { Build(); }
    public override void _ExitTree() { if (IsOpen) End(revert: true); }

    // ---- opening and closing -------------------------------------------------------------------------------------------------------

    /// <summary>Opens the editor on a worn object. False (and a message) when it cannot be edited.</summary>
    public bool Open(Guid attachmentId)
    {
        if (Store?.Items.FirstOrDefault(a => a.Id == attachmentId && a.Equipped) is not { } data) { Tell?.Invoke("Este objeto não está no corpo."); return false; }
        if (!data.CanModify) { Tell?.Invoke("Você não tem o direito de modificar este item, então não pode editar onde ele fica."); return false; }
        if (Rig is not { } rig || rig.AttachmentHolder(data.Id) is null) { Tell?.Invoke("Este objeto ainda não apareceu no corpo."); return false; }
        if (IsOpen) End(revert: true);
        Target = data; Model = new AttachmentEditModel(new Placement(Vec(data.Position), Vec(data.Rotation), Vec(data.Scale)));
        Model.Changed += OnChanged; _saving = false; _status.Text = string.Empty;
        _title.Text = string.IsNullOrWhiteSpace(data.Name) ? "Objeto" : data.Name; _where.Text = $"Anexado em: {data.PointName}";
        _proportions.ButtonPressed = Model.KeepProportions;
        _swayStart = data.Sway; _filling = true; _sway.Value = data.Sway; _filling = false;
        Player?.EnterEditView(rig.AttachmentWorldPosition(data.Id) ?? Vector3.Zero, Mathf.Clamp(rig.AttachmentSize(data.Id) * 3.2f, .45f, 2.6f));
        rig.SoloAttachment(_solo.ButtonPressed ? data.Id : null);
        _window.Visible = true; _window.MoveToFront(); SetTool(AttachmentTool.Move); Fill(); return true;
    }

    private static Vector3 Vec(float[] v) => v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.Zero;

    /// <summary>Closes the editor. With <paramref name="revert"/> what was changed and not saved goes back to what the server holds.</summary>
    private void End(bool revert)
    {
        if (Target is null) return; var id0 = Target.Id;
        EndDrag(); if (revert && _sentLive) SendLive(Model!.Saved); _sentLive = false; _liveDirty = false; Model!.Changed -= OnChanged;
        Target = null; Model = null; _gizmo.HideGizmo(); _window.Visible = false;
        if (Player is { EditViewActive: true }) Player.ExitMirror();
        if (Rig is { } rig) { rig.SetAttachmentSway(id0, null); rig.SoloAttachment(null); if (revert && Store is { } store) rig.ApplyAttachments(store.Items); }
    }

    /// <summary>Cancel: everything goes back to what the server holds.</summary>
    public void Cancel() { if (_saving) return; End(revert: true); }

    /// <summary>Save: asks the server; the editor closes when it accepts and says why when it does not.</summary>
    public void Save()
    {
        if (Target is null || Model is null || _saving) return;
        if (!Model.Dirty && !SwayChanged) { End(revert: false); return; }
        if (Store is not { } store) return;
        _saving = true; _save.Disabled = true; _status.Text = "Salvando…"; var placement = Model.Now; var id = Target.Id;
        var sway = SwayChanged ? Sway : (float?)null;
        store.Transform(id, placement, (ok, error) =>
        {
            _saving = false; _save.Disabled = false;
            if (!IsOpen || Target?.Id != id) return;
            if (ok) { Model!.MarkSaved(); Rig?.SetAttachmentSway(id, null); End(revert: false); Tell?.Invoke("Posição do anexo salva."); }
            else _status.Text = error;
        }, sway);
    }

    // ---- tools and handles -------------------------------------------------------------------------------------------------------

    public void SetTool(AttachmentTool tool)
    {
        EndDrag(); Tool = tool;
        foreach (var (key, button) in _tools) { button.SetPressedNoSignal(key == tool); UiStyle.StyleButton(button, key == tool ? UiStyle.ButtonKind.Primary : UiStyle.ButtonKind.Secondary, 10, 6); }
        if (tool == AttachmentTool.Select) _gizmo.HideGizmo();
        _status.Text = tool == AttachmentTool.Select ? "Clique em outro objeto do corpo para editá-lo." : string.Empty;
    }

    /// <summary>The point's frame in the world and the world position of the object.</summary>
    private (Basis Frame, float Metres, Vector3 Center)? Frame() => Target is { } t && Rig is { } rig && rig.AttachmentFrame(t.Id) is { } f && rig.AttachmentWorldPosition(t.Id) is { } p ? (f.Frame.Orthonormalized(), f.Metres, p) : null;

    private Basis AxesFor(Basis frame) => Tool == AttachmentTool.Scale ? frame * BuildGizmoMath.BasisOf(Model!.Rotation.X, Model.Rotation.Y, Model.Rotation.Z) : frame;

    public override void _Process(double delta)
    {
        if (Target is null || Model is null) return;
        Rig?.PreviewAttachment(Target.Id, Model.Position, Model.Rotation, Model.Scale); // also after the body was redrawn
        if (_liveDirty && Time.GetTicksMsec() - _lastLive >= LiveEveryMs) SendLive(Model.Now); // a few times a second while it changes, never every frame, never when nothing changed
        if (Frame() is not { } f || Tool == AttachmentTool.Select) { _gizmo.HideGizmo(); return; }
        if (!Dragging) _gizmo.Show(Tool switch { AttachmentTool.Rotate => BuildGizmoMode.Rotate, AttachmentTool.Scale => BuildGizmoMode.Scale, _ => BuildGizmoMode.Move }, f.Center, AxesFor(f.Frame));
        else _gizmo.Show(_gizmo.Mode, _dragCenterShown, AxesFor(_frame0));
        if (!Dragging && GetViewport().GetCamera3D() is { } camera && GetViewport().GuiGetHoveredControl() is null)
        { var mouse = GetViewport().GetMousePosition(); _gizmo.UpdateHover(camera.ProjectRayOrigin(mouse), camera.ProjectRayNormal(mouse)); }
    }
    private Vector3 _dragCenterShown;

    // ---- dragging (also what the tests call) ---------------------------------------------------------------------------------------

    /// <summary>The handle under a ray (none when nothing is there).</summary>
    public BuildGizmoHandle HandleAt(Vector3 origin, Vector3 direction) => Target is null || Tool == AttachmentTool.Select ? BuildGizmoHandle.None : _gizmo.HitTest(origin, direction);

    private static Vector3 Axis(Basis basis, int i) => (i == 0 ? basis.X : i == 1 ? basis.Y : basis.Z).Normalized();
    private int AxisIndex(BuildGizmoHandle handle) => handle switch { BuildGizmoHandle.X => 0, BuildGizmoHandle.Y => 1, BuildGizmoHandle.Z => 2, _ => -1 };

    public bool BeginDrag(BuildGizmoHandle handle, Vector3 origin, Vector3 direction)
    {
        if (Target is null || Model is null || handle == BuildGizmoHandle.None || Frame() is not { } f || GetViewport().GetCamera3D() is not { } camera) return false;
        _handle = handle; _start = Model.Now; _frame0 = f.Frame; _metres = f.Metres; _center = f.Center; _dragCenterShown = f.Center; _gizmo.SetActive(handle); Model.BeginGesture();
        var forward = -camera.GlobalBasis.Z; var i = AxisIndex(handle);
        switch (Tool)
        {
            case AttachmentTool.Move:
                if (i >= 0) { _axis = Axis(_frame0, i); _t0 = BuildGizmoMath.ClosestOnAxis(origin, direction, _center, _axis).T; }
                else _hit0 = BuildGizmoMath.RayPlane(origin, direction, _center, forward) ?? _center;
                break;
            case AttachmentTool.Rotate:
                _axis = i >= 0 ? Axis(_frame0, i) : forward;
                _angle0 = BuildGizmoMath.AngleAround(_center, _axis, BuildGizmoMath.RayPlane(origin, direction, _center, _axis) ?? _center + Vector3.Right);
                break;
            case AttachmentTool.Scale:
                if (i >= 0) { _axis = Axis(_frame0 * BuildGizmoMath.BasisOf(_start.Rotation.X, _start.Rotation.Y, _start.Rotation.Z), i); _t0 = BuildGizmoMath.ClosestOnAxis(origin, direction, _center, _axis).T; if (Mathf.Abs(_t0) < .05f) _t0 = _t0 < 0 ? -.05f : .05f; }
                else { _hit0 = BuildGizmoMath.RayPlane(origin, direction, _center, forward) ?? _center + Vector3.Right; }
                break;
        }
        return true;
    }

    public void UpdateDrag(Vector3 origin, Vector3 direction)
    {
        if (!Dragging || Model is null || GetViewport().GetCamera3D() is not { } camera) return;
        var forward = -camera.GlobalBasis.Z; var i = AxisIndex(_handle);
        var moveStep = PositionSteps[Mathf.Clamp(_positionStep.Selected, 0, PositionSteps.Length - 1)].Step; var turnStep = RotationSteps[Mathf.Clamp(_rotationStep.Selected, 0, RotationSteps.Length - 1)].Step;
        switch (Tool)
        {
            case AttachmentTool.Move:
                {
                    Vector3 local;
                    if (i >= 0) { var t = BuildGizmoMath.ClosestOnAxis(origin, direction, _center, _axis).T; var along = AttachmentEditModel.Snap((t - _t0) / _metres, moveStep); local = i == 0 ? new(along, 0, 0) : i == 1 ? new(0, along, 0) : new(0, 0, along); }
                    else
                    {
                        var hit = BuildGizmoMath.RayPlane(origin, direction, _center, forward) ?? _hit0; local = _frame0.Inverse() * (hit - _hit0) / _metres;
                        local = new Vector3(AttachmentEditModel.Snap(local.X, moveStep), AttachmentEditModel.Snap(local.Y, moveStep), AttachmentEditModel.Snap(local.Z, moveStep));
                    }
                    Model.SetPosition(_start.Position + local); break;
                }
            case AttachmentTool.Rotate:
                {
                    var hit = BuildGizmoMath.RayPlane(origin, direction, _center, _axis); if (hit is null) break;
                    var degrees = AttachmentEditModel.Snap(Mathf.RadToDeg(BuildGizmoMath.Delta(_angle0, BuildGizmoMath.AngleAround(_center, _axis, hit.Value))), turnStep);
                    var local = i >= 0 ? (i == 0 ? Vector3.Right : i == 1 ? Vector3.Up : Vector3.Back) : (_frame0.Inverse() * _axis).Normalized();
                    var basis = new Basis(local, Mathf.DegToRad(degrees)) * BuildGizmoMath.BasisOf(_start.Rotation.X, _start.Rotation.Y, _start.Rotation.Z);
                    Model.SetRotation(BuildGizmoMath.EulerDegrees(basis)); break;
                }
            case AttachmentTool.Scale:
                {
                    float ratio;
                    if (i >= 0) ratio = Mathf.Clamp(BuildGizmoMath.ClosestOnAxis(origin, direction, _center, _axis).T / _t0, .05f, 20f);
                    else { var hit = BuildGizmoMath.RayPlane(origin, direction, _center, forward) ?? _hit0; ratio = Mathf.Clamp(hit.DistanceTo(_center) / Mathf.Max(_hit0.DistanceTo(_center), .01f), .05f, 20f); }
                    var scale = _start.Scale; if (i < 0 || Model.KeepProportions) scale *= ratio; else scale[i] *= ratio;
                    Model.SetScale(scale); break;
                }
        }
    }

    public void EndDrag()
    {
        if (!Dragging) { _gizmo?.SetActive(BuildGizmoHandle.None); return; }
        _handle = BuildGizmoHandle.None; _gizmo.SetActive(BuildGizmoHandle.None); Model?.EndGesture(); _lastLive = 0;
    }

    // ---- the mouse -----------------------------------------------------------------------------------------------------------------

    public override void _Input(InputEvent input)
    {
        if (Target is null || Model is null || GetViewport().GetCamera3D() is not { } camera) return;
        if (input.IsActionPressed("ui_cancel") && !(GetViewport().GuiGetFocusOwner() is LineEdit)) { Cancel(); GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle) { _middleDrag = middle.Pressed; return; }
        if (input is InputEventMouseMotion motion)
        {
            if (_middleDrag) { Player?.PanEditView(motion.Relative); return; }
            if (Dragging) { UpdateDrag(camera.ProjectRayOrigin(motion.Position), camera.ProjectRayNormal(motion.Position)); GetViewport().SetInputAsHandled(); }
            return;
        }
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        if (!click.Pressed) { if (Dragging) { EndDrag(); GetViewport().SetInputAsHandled(); } return; }
        if (GetViewport().GuiGetHoveredControl() is not null) return; // a click on the panel is the panel's
        var origin = camera.ProjectRayOrigin(click.Position); var direction = camera.ProjectRayNormal(click.Position);
        if (Tool == AttachmentTool.Select) { PickOther(origin, direction); GetViewport().SetInputAsHandled(); return; }
        if (BeginDrag(HandleAt(origin, direction), origin, direction)) GetViewport().SetInputAsHandled();
    }

    private void PickOther(Vector3 origin, Vector3 direction)
    {
        if (Rig?.PickAttachment(origin, direction) is not { } id) { _status.Text = "Nenhum objeto aí."; return; }
        if (id == Target?.Id) { SetTool(AttachmentTool.Move); return; }
        SwitchTo(id);
    }

    /// <summary>Goes to another worn object. Refused while there is something unsaved here, so nothing is lost by accident.</summary>
    public bool SwitchTo(Guid id)
    {
        if (Model?.Dirty == true) { _status.Text = "Salve ou cancele as mudanças antes de trocar de objeto."; return false; }
        return Open(id);
    }

    // ---- the panel -----------------------------------------------------------------------------------------------------------------

    private void OnChanged() { Fill(); _liveDirty = true; }

    private void SendLive(Placement placement)
    {
        if (Target is null) return; LiveSent++; _sentLive = true; _liveDirty = false; _lastLive = Time.GetTicksMsec();
        if (LiveSink is not null) LiveSink(Target.Id, placement); else GetNodeOrNull<NetworkManager>("/root/NetworkManager")?.SendAttachmentLive(Target.Id, placement);
    }

    private void Fill()
    {
        if (Model is null) return;
        _filling = true;
        for (var i = 0; i < 3; i++) { _position[i].Value = Model.Position[i]; _rotation[i].Value = Model.Rotation[i]; _scale[i].Value = Model.Scale[i]; }
        _undo.Disabled = !Model.CanUndo; _redo.Disabled = !Model.CanRedo; _filling = false;
        _window.Title = Model.Dirty ? "Editar anexo ●" : "Editar anexo";
    }

    private SpinBox Spin(string prefix, double min, double max, double step, string suffix, Action<double> changed)
    {
        var spin = new SpinBox { Prefix = prefix, Suffix = suffix, MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32), CustomArrowStep = step * 10, UpdateOnTextChanged = true };
        spin.GetLineEdit().AddThemeFontSizeOverride("font_size", 14); spin.ValueChanged += value => { if (!_filling) changed(value); };
        return spin;
    }

    private HBoxContainer Row(string label, SpinBox[] spins, double min, double max, double step, string suffix, Action<int, double> changed)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4);
        var name = new Label { Text = label, CustomMinimumSize = new Vector2(62, 0) }; name.AddThemeFontSizeOverride("font_size", 14); name.AddThemeColorOverride("font_color", UiStyle.TextMuted); row.AddChild(name);
        for (var i = 0; i < 3; i++) { var axis = i; spins[i] = Spin("XYZ"[i] + " ", min, max, step, suffix, value => changed(axis, value)); row.AddChild(spins[i]); }
        return row;
    }

    private static Button Action(string text, Action run, string? tip = null)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tip ?? string.Empty, ClipText = true };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 10, 6); button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += run; return button;
    }

    private HBoxContainer Buttons(params Button[] buttons) { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); foreach (var b in buttons) row.AddChild(b); return row; }

    private void Build()
    {
        var limits = AttachmentEditModel.Epsilon > 0 ? LifeVerse.Shared.Avatar.AttachmentPoints.DefaultLimits : null!;
        _window = new FloatingWindow("Editar anexo", icon: "move") { Name = "AttachmentEditorWindow", Visible = false };
        _window.Place(1, .5f, -400, -320, -14, 320); _window.CloseRequested += Cancel; _window.MakeResizable();
        var c = _window.Content; c.AddThemeConstantOverride("separation", 8);
        _title = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; _title.AddThemeFontSizeOverride("font_size", 18); _title.AddThemeFontOverride("font", UiStyle.Bold); c.AddChild(_title);
        _where = new Label(); _where.AddThemeFontSizeOverride("font_size", 14); _where.AddThemeColorOverride("font_color", UiStyle.AccentText); c.AddChild(_where);
        var tools = new HBoxContainer(); tools.AddThemeConstantOverride("separation", 4); c.AddChild(tools);
        foreach (var (tool, label, tip) in new[] { (AttachmentTool.Move, "Mover", "Setas: desloca o objeto em relação ao ponto do corpo"), (AttachmentTool.Rotate, "Rotacionar", "Anéis: gira o objeto"), (AttachmentTool.Scale, "Escalar", "Caixas: muda o tamanho"), (AttachmentTool.Select, "Selecionar", "Clique em outro objeto do corpo para editá-lo") })
        { var button = new Button { Text = label, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tip }; button.AddThemeFontSizeOverride("font_size", 13); var t = tool; button.Pressed += () => SetTool(t); _tools[tool] = button; tools.AddChild(button); }
        var l = LifeVerse.Shared.Avatar.AttachmentPoints.DefaultLimits;
        c.AddChild(Row("Posição", _position, -l.MaxOffset, l.MaxOffset, .001, " m", (i, v) => { var p = Model!.Position; p[i] = (float)v; Model.SetPosition(p); }));
        c.AddChild(Row("Rotação", _rotation, -180, 180, .01, "°", (i, v) => { var r = Model!.Rotation; r[i] = (float)v; Model.SetRotation(r); }));
        c.AddChild(Row("Escala", _scale, l.MinScale, l.MaxScale, .001, "", (i, v) => Model!.SetScaleAxis(i, (float)v)));
        _proportions = new CheckBox { Text = "Manter proporções", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; _proportions.AddThemeFontSizeOverride("font_size", 14); _proportions.Toggled += on => { if (Model is not null) Model.KeepProportions = on; }; c.AddChild(_proportions);
        var swayRow = new HBoxContainer(); swayRow.AddThemeConstantOverride("separation", 6); c.AddChild(swayRow);
        var swayName = new Label { Text = "Balanço", CustomMinimumSize = new Vector2(62, 0), TooltipText = "Movimento leve do objeto com o corpo (caudas, penduricalhos). 0: parado" }; swayName.AddThemeFontSizeOverride("font_size", 14); swayName.AddThemeColorOverride("font_color", UiStyle.TextMuted); swayRow.AddChild(swayName);
        _sway = new SpinBox { MinValue = 0, MaxValue = 1, Step = .05, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32), TooltipText = "0 = parado, 1 = balança bastante" }; _sway.GetLineEdit().AddThemeFontSizeOverride("font_size", 14); swayRow.AddChild(_sway);
        _sway.ValueChanged += value => { if (!_filling && Target is not null) { Rig?.SetAttachmentSway(Target.Id, (float)value); _window.Title = "Editar anexo ●"; } };
        _solo = new CheckBox { Text = "Ocultar os outros anexos (só aqui)", FocusMode = Control.FocusModeEnum.None, TooltipText = "Só esconde na sua tela enquanto edita" }; _solo.AddThemeFontSizeOverride("font_size", 14); _solo.Toggled += on => Rig?.SoloAttachment(on && Target is not null ? Target.Id : null); c.AddChild(_solo);
        var steps = new HBoxContainer(); steps.AddThemeConstantOverride("separation", 6); c.AddChild(steps);
        _rotationStep = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Incremento da rotação", FocusMode = Control.FocusModeEnum.None }; foreach (var (label, _) in RotationSteps) _rotationStep.AddItem("Giro: " + label); steps.AddChild(_rotationStep);
        _positionStep = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Incremento do movimento", FocusMode = Control.FocusModeEnum.None }; foreach (var (label, _) in PositionSteps) _positionStep.AddItem("Mover: " + label); steps.AddChild(_positionStep);
        c.AddChild(Buttons(Action("Restaurar posição", () => Model?.ResetPosition()), Action("Restaurar rotação", () => Model?.ResetRotation()), Action("Restaurar escala", () => Model?.ResetScale())));
        c.AddChild(Buttons(Action("Centralizar no ponto", () => Model?.CenterOnPoint(), "Põe o objeto exatamente no ponto do corpo"), Action("Focar a câmera", FocusCamera, "Volta a câmera para o objeto")));
        c.AddChild(Buttons(Action("Copiar transformação", () => { Model?.Copy(); _status.Text = "Transformação copiada."; }), Action("Colar transformação", () => { if (Model is not null) _status.Text = Model.Paste() ? "Transformação colada." : "Não há nada copiado."; })));
        _undo = Action("Desfazer", () => Model?.Undo()); _redo = Action("Refazer", () => Model?.Redo()); c.AddChild(Buttons(_undo, _redo));
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) }; _status.AddThemeFontSizeOverride("font_size", 13); _status.AddThemeColorOverride("font_color", UiStyle.Orange); c.AddChild(_status);
        var cancel = Action("Cancelar", Cancel); _save = Action("Salvar", Save); UiStyle.StyleButton(_save, UiStyle.ButtonKind.Primary, 10, 6); c.AddChild(Buttons(cancel, _save));
        AddChild(_window);
        _gizmo = new BuildGizmoController { Name = "AttachmentGizmo", TopLevel = true }; AddChild(_gizmo);
    }

    private void FocusCamera() { if (Target is not null && Rig?.AttachmentWorldPosition(Target.Id) is { } p) Player?.FocusEditView(p, Mathf.Clamp(Rig.AttachmentSize(Target.Id) * 3.2f, .45f, 2.6f)); }

    // ---- tests ---------------------------------------------------------------------------------------------------------------------

    public SpinBox PositionField(int axis) => _position[axis];
    public SpinBox RotationField(int axis) => _rotation[axis];
    public SpinBox ScaleField(int axis) => _scale[axis];
    public void SetRotationStepForTesting(int index) => _rotationStep.Selected = index;
    public void SetPositionStepForTesting(int index) => _positionStep.Selected = index;
    public Button ToolButtonForTesting(AttachmentTool tool) => _tools[tool];
    public bool WindowVisible => _window.Visible;
}
