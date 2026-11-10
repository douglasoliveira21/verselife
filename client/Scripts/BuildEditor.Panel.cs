using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// The build window (client/Assets/ui design): a toolbar with the tools and actions, and the tabs Geral, Objeto and Textura
/// with the values of the selected object. Nothing here talks to the server directly; every change goes through
/// CommitUpdates / CreateFrom, which send revisioned operations the island server validates.
public partial class BuildEditor
{
    private static readonly (string Id, string Label)[] Primitives =
    [
        ("cube", "Caixa"), ("sphere", "Esfera"), ("cylinder", "Cilindro"), ("prism", "Prisma"), ("torus", "Toro"), ("tube", "Tubo"),
        ("ring", "Anel"), ("pyramid", "Pirâmide"), ("cone", "Cone"), ("hemisphere", "Semiesfera"), ("plane", "Plano")
    ];

    private FloatingWindow _window = null!;
    private OptionButton _primitive = null!;
    private LineEdit _snap = null!, _name = null!;
    private TextEdit _description = null!;
    private readonly LineEdit[] _position = new LineEdit[3], _rotationFields = new LineEdit[3], _size = new LineEdit[3];
    private HSlider _hollow = null!, _taperX = null!, _taperY = null!, _twist = null!;
    private ColorPickerButton _colorPicker = null!, _emissionPicker = null!;
    private CheckBox _glow = null!, _ghost = null!;
    private OptionButton _physMode = null!, _collisionShape = null!; private LineEdit _physMass = null!, _physFriction = null!, _physBounce = null!; private OptionButton _physMaterial = null!; private CheckBox _physTemporary = null!;
    private HSlider _roughness = null!, _metallic = null!, _opacity = null!;
    private Label _status = null!, _selectionLabel = null!;
    private Button _toolSelect = null!, _toolMove = null!, _toolRotate = null!, _toolScale = null!, _toolFace = null!;
    private CheckBox _gridSnap = null!, _showGrid = null!, _showCollision = null!;
    private OptionButton _rotSnap = null!;
    private Label _faceLabel = null!;
    private bool _filling;

    private Button ToolToggle(string text, string tip, Action action)
    {
        var button = new Button { Text = text, TooltipText = tip, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        const float mx = 8, my = 6;
        button.AddThemeStyleboxOverride("normal", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Normal, mx, my, 12));
        button.AddThemeStyleboxOverride("hover", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Hover, mx, my, 12));
        button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Normal, mx, my, 12));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Hover, mx, my, 12));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeFontOverride("font", UiStyle.Medium); button.AddThemeFontSizeOverride("font_size", 16);
        foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) button.AddThemeColorOverride(color, Colors.White);
        button.Pressed += action;
        return button;
    }

    private static Button TextButton(string text, string tip, Action action, UiStyle.ButtonKind kind = UiStyle.ButtonKind.Secondary)
    {
        var button = new Button { Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 42), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 16); button.Pressed += action;
        return button;
    }

    private void UpdateToolButtons()
    {
        if (_toolSelect is null) return;
        _toolSelect.SetPressedNoSignal(_selectOnly);
        _toolMove.SetPressedNoSignal(!_selectOnly && !_faceTool && _mode == GizmoMode.Move);
        _toolRotate.SetPressedNoSignal(!_selectOnly && !_faceTool && _mode == GizmoMode.Rotate);
        _toolScale.SetPressedNoSignal(!_selectOnly && !_faceTool && _mode == GizmoMode.Scale);
        _toolFace.SetPressedNoSignal(_faceTool);
    }

    private static Control Scroll(Control stack, Control content)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content); stack.AddChild(scroll);
        return scroll;
    }

    // ---- tabs ------------------------------------------------------------------------------------------------

    private static VBoxContainer Section() { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); return box; }

    private static Label FieldLabel(string text)
    {
        var label = new Label { Text = text }; label.AddThemeFontOverride("font", UiStyle.Medium); label.AddThemeFontSizeOverride("font_size", 16);
        return label;
    }

    private static LineEdit Field(string placeholder, int maxLength) => Fielded(new LineEdit { PlaceholderText = placeholder, MaxLength = maxLength, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

    private static LineEdit Fielded(LineEdit edit) { edit.AddThemeFontSizeOverride("font_size", 20); return edit; }

    // ---- reading and writing the fields ----------------------------------------------------------------------

    private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool TryNum(string text, out float value) => float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    private static float NumOr(LineEdit field, float fallback) => TryNum(field.Text, out var value) ? value : fallback;

    /// Shows the selected object's values in the window; with several objects only the count is shown.
    private void FillFields()
    {
        if (_selectionLabel is null) return;
        _filling = true; RefreshModeStatus(); ApplyReadOnly(); FillSharing(); FillResources(); if (_selection.Count != 1) FillShape([]);
        _selectedCount.Text = _selection.Count switch { 0 => "nenhum", 1 => "1 objeto", _ => $"{_selection.Count} objetos" };
        if (_selection.Count == 0) _primsLabel.Text = $"Prims: {_objects.Count:N0}".Replace(",", ".");
        _selectionLabel.Text = _selection.Count switch
        {
            0 => "Nada selecionado",
            1 => $"Selecionado: {_selection[0].State.Name} · revisão {_selection[0].State.Revision}{(_selection[0].State.GroupId is null ? "" : " · vinculado")}{RightsNote(_selection)}",
            _ => $"{_selection.Count} objetos selecionados{RightsNote(_selection)}"
        };
        if (_selection.Count == 1)
        {
            var view = _selection[0]; var s = view.State;
            _name.Text = s.Name; _description.Text = s.Description;
            for (var i = 0; i < 3; i++)
            {
                _position[i].Text = Disp(new[] { view.Position.X, view.Position.Y, view.Position.Z }[i]);
                _rotationFields[i].Text = Disp(new[] { s.RotationX, s.RotationY, s.RotationZ }[i]);
                _size[i].Text = Disp(new[] { view.Scale.X, view.Scale.Y, view.Scale.Z }[i]);
                _lastSize[i] = new[] { view.Scale.X, view.Scale.Y, view.Scale.Z }[i];
            }
            var geometry = ParseObject(s.GeometryJson); var material = ParseObject(s.MaterialJson); var physics = ParseObject(s.PhysicsJson);
            _hollow.Value = Read(geometry, "hollow", 0); _taperX.Value = Read(geometry, "taperX", 0); _taperY.Value = Read(geometry, "taperY", 0); _twist.Value = Read(geometry, "twist", 0);
            FillShape(geometry);
            if (material["color"]?.GetValue<string>() is { } color) _colorPicker.Color = Color.FromHtml(color);
            _roughness.Value = Read(material, "roughness", .7f); _metallic.Value = Read(material, "metallic", 0f); _opacity.Value = Read(material, "transparency", 1f);
            _glow.ButtonPressed = material["emission"] is not null;
            if (material["emission"]?.GetValue<string>() is { } emission) _emissionPicker.Color = Color.FromHtml(emission);
            _ghost.ButtonPressed = physics["collision"] is JsonValue flag && flag.TryGetValue<bool>(out var solid) && !solid; FillPhysics(s.PhysicsJson);
            var parts = s.GroupId is { } group ? _objects.Values.Count(o => o.State.GroupId == group) : 1;
            _infoLeft.Text = $"Criador: {Who(s.CreatorUserId)}\nProprietário: {Who(s.OwnerUserId)}";
            _creatorValue.Text = Who(s.CreatorUserId); _ownerValue.Text = Who(s.OwnerUserId);
            for (var i = 0; i < _primitive.ItemCount; i++) if (_primitive.GetItemMetadata(i).AsString() == s.PrimitiveType.ToLowerInvariant()) { _primitive.Select(i); break; }
            _infoRight.Text = $"Identificador: {s.Id}\nPrimitiva: {s.Name}{(parts > 1 ? $" (composta por {parts} primitivas)" : $" ({PrimitiveLabel(s.PrimitiveType)})")}";
        }
        else { _creatorValue.Text = "—"; _ownerValue.Text = "—"; _infoLeft.Text = _selection.Count == 0 ? "Selecione um objeto para ver criador, proprietário e identificador." : $"{_selection.Count} objetos: posição, rotação e tamanho próprios não são editados em conjunto; forma, material e física valem para todos."; _infoRight.Text = string.Empty; }
        FillTextureFields();
        FillContent();
        _filling = false;
        RefreshPreviews();
        RefreshGizmo();
    }

    private string Who(Guid id) => id == Guid.Empty ? "—" : id == _session.UserId ? "Você" : id.ToString()[..8];
    private string Short(Guid id) => id == Guid.Empty ? "—" : id == _session.UserId ? "você" : id.ToString()[..8];
    private static string PrimitiveLabel(string id) => Primitives.FirstOrDefault(x => x.Id == id).Label ?? id;

    private static JsonObject ParseObject(string json)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? []; }
        catch (JsonException) { return []; }
    }

    private static float Read(JsonObject node, string key, float fallback) => node[key] is JsonValue value && value.TryGetValue<double>(out var number) ? (float)number : fallback;

    private void ApplySelected()
    {
        if (_filling) return;
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto primeiro."; return; }
        var single = _selection.Count == 1;
        var afters = new List<BuildObjectState>();
        foreach (var view in _selection)
        {
            var after = FromView(view);
            if (single)
            {
                after.PositionX = NumOr(_position[0], after.PositionX); after.PositionY = NumOr(_position[1], after.PositionY); after.PositionZ = NumOr(_position[2], after.PositionZ);
                after.RotationX = NumOr(_rotationFields[0], after.RotationX); after.RotationY = NumOr(_rotationFields[1], after.RotationY); after.RotationZ = NumOr(_rotationFields[2], after.RotationZ);
                after.ScaleX = ClampScale(NumOr(_size[0], after.ScaleX)); after.ScaleY = ClampScale(NumOr(_size[1], after.ScaleY)); after.ScaleZ = ClampScale(NumOr(_size[2], after.ScaleZ));
                if (!string.IsNullOrWhiteSpace(_name.Text)) after.Name = _name.Text.Trim();
                after.Description = _description.Text.Trim();
            }
            after.GeometryJson = GeometryFromFields(after.GeometryJson, after.PrimitiveType);
            after.MaterialJson = MaterialFromFields(after.MaterialJson);
            after.PhysicsJson = PhysicsFromFields(after.PhysicsJson);
            afters.Add(after);
        }
        CommitUpdates(afters);
        _status.Text = "Alterações enviadas ao servidor.";
    }

    private string GeometryFromFields(string existing, string type)
    {
        var node = ParseObject(existing);
        node["hollow"] = Math.Round(Mathf.Clamp((float)_hollow.Value, 0f, .95f), 3); node["taperX"] = Math.Round(Mathf.Clamp((float)_taperX.Value, -1f, 1f), 3);
        node["taperY"] = Math.Round(Mathf.Clamp((float)_taperY.Value, -1f, 1f), 3); node["twist"] = Math.Round(Mathf.Clamp((float)_twist.Value, -180f, 180f), 2);
        WriteShape(node, type.ToLowerInvariant());
        return node.ToJsonString();
    }

    private string MaterialFromFields(string existing)
    {
        var node = ParseObject(existing);
        node["color"] = "#" + _colorPicker.Color.ToHtml(false);
        node["roughness"] = Math.Round(_roughness.Value, 2); node["metallic"] = Math.Round(_metallic.Value, 2);
        if (_opacity.Value < .999) node["transparency"] = Math.Round(_opacity.Value, 2); else node.Remove("transparency");
        if (_glow.ButtonPressed) { node["emission"] = "#" + _emissionPicker.Color.ToHtml(false); node["emissionStrength"] = Math.Round(_glowStrength.Value * 8, 2); } else { node.Remove("emission"); node.Remove("emissionStrength"); }
        return node.ToJsonString();
    }

    private string PhysicsFromFields(string existing)
    {
        var shape = new[] { "none", "box", "convex", "triangle", "sphere", "cylinder" }.ElementAtOrDefault(_collisionShape.Selected) ?? "box";
        var physical = _physMode.Selected == 1 && shape != "triangle";
        if (_physMode.Selected == 1 && shape == "triangle") { _physMode.Select(0); _status.Text = "Malha triangular só funciona como colisão estática."; }
        return PhysicsRules.Write(existing, new PhysicsSettings(!_ghost.ButtonPressed && shape != "none", Trigger: ParseObject(existing)["trigger"] is JsonValue t && t.TryGetValue<bool>(out var isTrigger) && isTrigger, physical,
            NumOr(_physMass, 1f), NumOr(_physFriction, .5f), NumOr(_physBounce, 0f), shape, _physMaterial.Selected > 0 ? _physMaterial.GetItemMetadata(_physMaterial.Selected).AsString() : "", _physTemporary.ButtonPressed));
    }

    private void FillPhysics(string physicsJson)
    {
        var p = PhysicsRules.Read(physicsJson);
        _physMode.Select(p.Physical ? 1 : 0); SyncPhysicsUi(); _collisionShape.Select(Array.IndexOf(new[] { "none", "box", "convex", "triangle", "sphere", "cylinder" }, p.CollisionShape) is var index && index >= 0 ? index : 1); _physMass.Text = Disp(p.Mass, 2); _physFriction.Text = Disp(p.Friction, 2); _physBounce.Text = Disp(p.Bounce, 2); _physMaterial.Select(Math.Max(0, Array.IndexOf(PhysicsRules.Materials.Keys.ToArray(), p.Material) + 1)); _physTemporary.SetPressedNoSignal(p.Temporary);
        if (_selection.Count == 1 && _selection[0].State.PrimitiveType.Equals("mesh", StringComparison.OrdinalIgnoreCase) && ParseObject(physicsJson)["shape"] is null) _collisionShape.Select(3); // a model with no shape chosen collides exactly
    }

    public void SetPhysicsForTesting(bool physical, float mass = 1f, float friction = .5f, float bounce = 0f) { _physMode.Select(physical ? 1 : 0); _physMass.Text = Disp(mass, 2); _physFriction.Text = Disp(friction, 2); _physBounce.Text = Disp(bounce, 2); ApplySelected(); }

    private void CopyProperties()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto para copiar as propriedades."; return; }
        var s = _selection[0].State;
        _propertyClipboard = JsonSerializer.Serialize(new { materialJson = s.MaterialJson, geometryJson = s.GeometryJson, physicsJson = s.PhysicsJson });
        _status.Text = "Propriedades copiadas.";
    }

    private void PasteProperties()
    {
        if (string.IsNullOrWhiteSpace(_propertyClipboard)) { _status.Text = "Nenhuma propriedade copiada."; return; }
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que receberão as propriedades."; return; }
        try
        {
            using var doc = JsonDocument.Parse(_propertyClipboard);
            var afters = new List<BuildObjectState>();
            foreach (var view in _selection)
            {
                var after = FromView(view);
                after.MaterialJson = doc.RootElement.GetProperty("materialJson").GetString() ?? after.MaterialJson;
                after.GeometryJson = doc.RootElement.GetProperty("geometryJson").GetString() ?? after.GeometryJson;
                after.PhysicsJson = doc.RootElement.GetProperty("physicsJson").GetString() ?? after.PhysicsJson;
                afters.Add(after);
            }
            CommitUpdates(afters);
            _status.Text = "Propriedades coladas.";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _status.Text = "Propriedades inválidas na área de transferência."; }
    }

    /// Test hook: fills the object tab as a person would and presses Aplicar.
    public void ApplyFieldsForTesting(Vector3 position, Vector3 size, string name, float hollow)
    {
        for (var i = 0; i < 3; i++) { _position[i].Text = Num(position[i]); _size[i].Text = Num(size[i]); }
        _name.Text = name; _hollow.Value = hollow;
        ApplySelected();
    }
    public void SelectToolForTesting() => SelectTool();
    public void ApplyRotationForTesting(Vector3 degrees) { for (var i = 0; i < 3; i++) _rotationFields[i].Text = Num(degrees[i]); ApplySelected(); }
    public string PrimitiveIdsForTesting => string.Join(",", Primitives.Select(x => x.Id));
    public Control WindowForTesting => _window;

    // ---- grid, faces and alignment -------------------------------------------------------------------------

    private float GridStep() => Mathf.Clamp(NumOr(_snap, .5f), 0f, 100f);

    private void UpdateGrid()
    {
        if (_grid is null || _showGrid is null) return;
        if (_selection.Count == 0 || !_active) { _grid.ShowAround(Vector3.Zero, 0, 0, false); return; }
        var boxes = _selection.Select(WorldBounds).ToList();
        var low = boxes.Min(x => x.Min.Y); var center = boxes.Aggregate(System.Numerics.Vector3.Zero, (sum, x) => sum + x.Center) / boxes.Count;
        _grid.ShowAround(new Vector3(center.X, 0, center.Z), low, GridStep(), _showGrid.ButtonPressed);
        _grid.Rotation = new Vector3(0, Space == AxisSpace.Local ? AxesEuler.Y : 0f, 0); // the grid follows the reference: world, the first object or the chosen reference
    }

    private void FaceTool()
    {
        CancelDrag(); _faceTool = true; _selectOnly = false; DisarmPlacement(null);
        UpdateToolButtons(); RefreshGizmo();
        _status.Text = "Clique em uma face para selecioná-la; depois use a aba Textura.";
    }

    private readonly SortedSet<int> _faceSet = [];
    public IReadOnlyCollection<int> SelectedFacesForTesting => _faceSet;

    private void SelectFace(BuildObjectView view, int face, bool additive = false)
    {
        var keep = additive && ReferenceEquals(_faceView, view) && face >= 0;
        var previous = keep ? _faceSet.ToArray() : [];
        if (!keep) ClearFace();
        if (!view.IsProcedural || face < 0) { _faceLabel.Text = "Esta forma não tem faces separadas; use caixa, prisma, cilindro, tubo, cone ou pirâmide."; return; }
        _faceView = view; foreach (var old in previous) _faceSet.Add(old);
        if (keep && _faceSet.Contains(face)) { _faceSet.Remove(face); face = _faceSet.Count > 0 ? _faceSet.Max : -1; } else _faceSet.Add(face); // shift + click on a chosen face takes it off
        _selectedFace = face; view.HighlightFaces(_faceSet);
        if (_faceSet.Count == 0) { ClearFace(); return; }
        ShowFaceLabel(); FillTextureFields(); _status.Text = "Face selecionada. Shift + clique soma mais faces; escolha a cor ou a textura na aba Textura.";
    }

    private void ShowFaceLabel() => _faceLabel.Text = _faceSet.Count == 1 ? $"Face selecionada: {_faceSet.First()}" : $"{_faceSet.Count} faces selecionadas: {string.Join(", ", _faceSet)}";

    /// Selects every face of the chosen object (or the first selected one) so a texture can go on all of them at once.
    private void SelectAllFaces()
    {
        var view = _faceView is not null && IsInstanceValid(_faceView) ? _faceView : _selection.Count == 1 ? _selection[0] : null;
        if (view is null || !view.IsProcedural) { _status.Text = "Selecione um objeto com faces separadas."; return; }
        ClearFace(); _faceView = view; foreach (var id in view.FaceIds) _faceSet.Add(id);
        _selectedFace = _faceSet.Max; view.HighlightFaces(_faceSet); ShowFaceLabel(); FillTextureFields();
    }

    public void SelectAllFacesForTesting() => SelectAllFaces();
    public void SelectFaceForTesting(Guid id, int face, bool additive) { if (_objects.TryGetValue(Resolve(id), out var view)) { if (!_selection.Contains(view)) SelectView(view, false); _faceTool = true; SelectFace(view, face, additive); } }

    private BuildObjectView? _faceView;

    private void ClearFace()
    {
        if (_faceView is not null && IsInstanceValid(_faceView)) _faceView.HighlightFace(-1);
        _faceView = null; _selectedFace = -1; _faceSet.Clear();
        if (_faceLabel is not null) { _faceLabel.Text = "Face (tecla 5): clique em uma face; Shift + clique soma mais."; FillTextureFields(); }
    }

    private void PaintFace(bool clear)
    {
        if (_faceSet.Count == 0 || _faceView is null || !IsInstanceValid(_faceView)) { _status.Text = "Selecione uma face com a ferramenta Face (5)."; return; }
        var after = FromView(_faceView);
        var material = ParseObject(after.MaterialJson);
        var faces = material["faces"] as JsonArray ?? [];
        var chosen = _faceSet.ToArray();
        for (var i = faces.Count - 1; i >= 0; i--) if (faces[i] is JsonObject entry && entry["index"]?.GetValue<int>() is { } index && chosen.Contains(index)) faces.RemoveAt(i);
        if (!clear) foreach (var face in chosen) faces.Add(new JsonObject { ["index"] = face, ["color"] = "#" + _colorPicker.Color.ToHtml(false) });
        if (faces.Count > 0) material["faces"] = faces; else material.Remove("faces");
        after.MaterialJson = material.ToJsonString();
        var kept = _selectedFace;
        CommitUpdates([after]);
        _selectedFace = kept;
        _status.Text = clear ? "Cor das faces removida." : chosen.Length == 1 ? "Face colorida." : $"{chosen.Length} faces coloridas.";
    }

    /// World-space box around an object's visible mesh.
    private static LifeVerse.Shared.Building.Bounds WorldBounds(BuildObjectView view)
    {
        var mesh = view.GetNodeOrNull<MeshInstance3D>("PrimitiveMesh");
        var local = mesh?.GetAabb() ?? new Aabb(-Vector3.One / 2, Vector3.One);
        var transform = mesh is not null && mesh.IsInsideTree() ? mesh.GlobalTransform : view.GlobalTransform;
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var corner = transform * local.GetEndpoint(i);
            min = min.Min(corner); max = max.Max(corner);
        }
        return new LifeVerse.Shared.Building.Bounds(new System.Numerics.Vector3(min.X, min.Y, min.Z), new System.Numerics.Vector3(max.X, max.Y, max.Z));
    }

    private void MoveSelection(float[] offsets, AlignAxis axis, string done)
    {
        if (offsets.All(x => MathF.Abs(x) < 1e-4f)) { _status.Text = "Nada a mover: a seleção já está assim."; return; }
        var afters = new List<BuildObjectState>();
        for (var i = 0; i < _selection.Count; i++)
        {
            var after = FromView(_selection[i]);
            switch (axis) { case AlignAxis.X: after.PositionX += offsets[i]; break; case AlignAxis.Y: after.PositionY += offsets[i]; break; default: after.PositionZ += offsets[i]; break; }
            afters.Add(after);
        }
        CommitUpdates(afters);
        _status.Text = done;
    }

    private void AlignSelection(AlignAxis axis, AlignEdge edge)
    {
        if (_selection.Count < 2) { _status.Text = "Selecione dois ou mais objetos para alinhar."; return; }
        var boxes = _selection.Select(WorldBounds).ToList(); var reference = AlignReferenceIndex(boxes, axis);
        MoveSelection(reference >= 0 ? AlignMath.AlignTo(boxes, reference, axis, edge) : AlignMath.Align(boxes, axis, edge), axis, reference >= 0 ? "Objetos alinhados ao escolhido." : "Objetos alinhados.");
    }

    private void DistributeSelection(AlignAxis axis)
    {
        if (_selection.Count < 3) { _status.Text = "Selecione três ou mais objetos para distribuir."; return; }
        var boxes = _selection.Select(WorldBounds).ToList(); var how = _distributeHow?.Selected ?? 0;
        var offsets = how == 1 ? AlignMath.DistributeCenters(boxes, axis) : how == 2 ? AlignMath.DistributeWithGap(boxes, axis, NumOr(_distributeGap, 0f)) : AlignMath.Distribute(boxes, axis);
        MoveSelection(offsets, axis, "Objetos distribuídos.");
    }

    private void RestSelectionOnGround()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto primeiro."; return; }
        var boxes = _selection.Select(WorldBounds).ToList();
        var center = boxes.Aggregate(System.Numerics.Vector3.Zero, (sum, x) => sum + x.Center) / boxes.Count;
        var low = boxes.Min(x => x.Min.Y);
        var from = new Vector3(center.X, low + .05f, center.Z);
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 500f, uint.MaxValue & ~BuildGizmo.PhysicsLayer);
        query.Exclude = new Godot.Collections.Array<Rid>(_selection.SelectMany(v => v.GetChildren().OfType<CollisionObject3D>()).Select(x => x.GetRid()));
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        var ground = hit.Count > 0 ? hit["position"].AsVector3().Y : 0f;
        MoveSelection(Enumerable.Repeat(AlignMath.RestOnGround(boxes, ground), _selection.Count).ToArray(), AlignAxis.Y, "Objetos apoiados no solo.");
    }

    private Vector3? _copiedPosition, _copiedRotation, _copiedSize;

    private void CopyTransform(int what)
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto para copiar."; return; }
        var view = _selection[0]; var s = view.State;
        switch (what)
        {
            case 0: _copiedPosition = view.Position; _status.Text = "Posição copiada."; break;
            case 1: _copiedRotation = new Vector3(s.RotationX, s.RotationY, s.RotationZ); _status.Text = "Rotação copiada."; break;
            default: _copiedSize = view.Scale; _status.Text = "Tamanho copiado."; break;
        }
    }

    private void PasteTransform()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que receberão os valores."; return; }
        if (_copiedPosition is null && _copiedRotation is null && _copiedSize is null) { _status.Text = "Nada foi copiado ainda."; return; }
        var afters = new List<BuildObjectState>();
        foreach (var view in _selection)
        {
            var after = FromView(view);
            if (_copiedPosition is { } p) { after.PositionX = p.X; after.PositionY = p.Y; after.PositionZ = p.Z; }
            if (_copiedRotation is { } r) { after.RotationX = r.X; after.RotationY = r.Y; after.RotationZ = r.Z; }
            if (_copiedSize is { } z) { after.ScaleX = ClampScale(z.X); after.ScaleY = ClampScale(z.Y); after.ScaleZ = ClampScale(z.Z); }
            afters.Add(after);
        }
        CommitUpdates(afters);
        _status.Text = "Valores colados.";
    }

    public void AlignForTesting(int axis, int edge) => AlignSelection((AlignAxis)axis, (AlignEdge)edge);
    public void DistributeForTesting(int axis) => DistributeSelection((AlignAxis)axis);
    public void RestOnGroundForTesting() => RestSelectionOnGround();
    public void SelectFaceForTesting(Guid id, int face) { if (_objects.TryGetValue(Resolve(id), out var view)) { SelectView(view, false); _faceTool = true; SelectFace(view, face); } }
    public void PaintFaceForTesting(bool clear) => PaintFace(clear);
    public bool GridVisibleForTesting => _grid.Visible;
    public void SetGridForTesting(bool show, bool snap) { _showGrid.ButtonPressed = show; _gridSnap.ButtonPressed = snap; UpdateGrid(); }
    public void SetCollisionPreviewForTesting(bool show) { _showCollision.ButtonPressed = show; SetCollisionPreview(show); }
    private void SetCollisionPreview(bool show) { foreach (var view in _objects.Values) view.SetCollisionPreview(show); }
    public void SetPaintColorForTesting(Color color) => _colorPicker.Color = color;
}

public partial class BuildEditor
{
    /// <summary>" · direitos CMT-" for the selection (only what every selected object allows), and a warning when the selection may not be modified.</summary>
    private string RightsNote(IReadOnlyList<BuildObjectView> selection)
    {
        if (selection.Count == 0) return string.Empty;
        var rights = LifeVerse.Shared.Items.ItemRights.Intersect(selection.Select(v => v.State.OwnerRights));
        return LinkNote(selection) + $" · direitos {LifeVerse.Shared.Items.ItemRights.Letters(rights)}{(LifeVerse.Shared.Items.ItemRights.Has(rights, LifeVerse.Shared.Items.Right.Modify) ? string.Empty : " · só mover e devolver (sem direito de modificar)")}";
    }

    public string RightsNoteForTesting => RightsNote(_selection);
}
