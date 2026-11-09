using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Building;
using NQ = System.Numerics.Quaternion;
using NV = System.Numerics.Vector3;

namespace LifeVerse.Client.Scripts;

/// The "Transformar" tab: where the handles and the typed numbers turn around (the pivot), along which axes (the world's or the first selected object's), exact moves,
/// turns and stretches by typed values, mirroring through a plane, and multiple duplication (a line, a grid or a circle of copies). The arithmetic is in
/// <see cref="TransformMath"/> and <see cref="ArrayMath"/>; the editor converts and sends the same updates and creations as for any other change.
public partial class BuildEditor
{
    private static readonly string[] PivotModes = ["Centro da seleção", "Base da seleção", "Raiz do conjunto", "Primeiro selecionado", "Pivô do objeto", "Ponto escolhido", "Origem do mundo"];
    private const int PivotOfObject = 4, PivotPoint = 5;
    private const int MaxCopiedObjects = 300;

    private OptionButton _pivotMode = null!, _axesSpace = null!, _arrayKind = null!;
    private LineEdit[] _pivotAt = null!, _objectPivot = null!, _moveBy = null!, _scaleBy = null!, _arrayStep = null!, _arrayTurn = null!, _arrayGrow = null!, _arrayCount = null!;
    private LineEdit _turnBy = null!;
    private CheckBox _mirrorCopy = null!, _circleFace = null!;
    private Label _pivotInfo = null!, _arrayInfo = null!;
    private OptionButton _circleAxis = null!;

    // ---- the pivot and the axes ---------------------------------------------------------------------------------------------------

    private static NV N(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 G(NV v) => new(v.X, v.Y, v.Z);
    private static NQ QuatOf(Vector3 euler) { var q = Basis.FromEuler(euler).GetRotationQuaternion(); return new NQ(q.X, q.Y, q.Z, q.W); }
    private static Vector3 EulerOf(NQ q) => new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)).GetEuler();
    private static Pose PoseOf(BuildObjectView view) => new(N(view.Position), QuatOf(view.Rotation), N(view.Scale));

    private Guid _referenceObject;
    private bool ReferenceChosen => _axesSpace is not null && _axesSpace.Selected == 2 && _objects.TryGetValue(_referenceObject, out var reference) && IsInstanceValid(reference);
    private AxisSpace Space => _axesSpace is not null && _axesSpace.Selected is 1 or 2 ? AxisSpace.Local : AxisSpace.World;
    /// The turn the axes follow: the world (none), the first selected object, or the reference object chosen with "Usar como referência".
    private Vector3 AxesEuler => ReferenceChosen ? _objects[_referenceObject].Rotation : _selection.Count > 0 ? _selection[0].Rotation : Vector3.Zero;
    private NQ Frame => Space == AxisSpace.Local && (_selection.Count > 0 || ReferenceChosen) ? QuatOf(AxesEuler) : NQ.Identity;

    /// Where things turn and stretch around, according to the chosen mode.
    private Vector3 PivotWorld()
    {
        if (_selection.Count == 0) return Vector3.Zero;
        var first = _selection[0];
        switch (_pivotMode?.Selected ?? 0)
        {
            case 1: { var middle = _selection.Aggregate(Vector3.Zero, (sum, v) => sum + v.Position) / _selection.Count; return new Vector3(middle.X, _selection.Min(v => v.Position.Y - v.Scale.Y * .5f), middle.Z); }
            case 2: { if (first.State.GroupId is { } group && LinksetRules.RootOf(LinkWorld(), group) is { } root && _objects.TryGetValue(root, out var top)) return top.Position; return first.Position; }
            case 3: return first.Position;
            case PivotOfObject: return G(TransformMath.PivotOf(PoseOf(first), new NV(first.State.PivotX, first.State.PivotY, first.State.PivotZ)));
            case PivotPoint: return new Vector3(NumOr(_pivotAt[0], 0), NumOr(_pivotAt[1], 0), NumOr(_pivotAt[2], 0));
            case 6: return Vector3.Zero;
            default: return _selection.Aggregate(Vector3.Zero, (sum, v) => sum + v.Position) / _selection.Count;
        }
    }

    private void UpdatePivotInfo()
    {
        if (_pivotInfo is null) return;
        var p = PivotWorld(); _pivotInfo.Text = _selection.Count == 0 ? "Selecione algo para ver onde fica o pivô." : $"Pivô em x {Disp(p.X, 2)}, y {Disp(p.Y, 2)}, z {Disp(p.Z, 2)} · eixos {(Space == AxisSpace.Local ? "locais (do primeiro selecionado)" : "do mundo")}.";
    }

    // ---- applying a precise change ---------------------------------------------------------------------------------------------

    /// Changes every selected object with the same function of its pose and sends the results as one change (one undo step).
    private void ApplyPose(Func<Pose, Pose?> change, string done)
    {
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos primeiro."; return; }
        if (SelectionProtected()) return;
        var states = new List<BuildObjectState>(); var clamped = false;
        foreach (var view in _selection)
        {
            if (change(PoseOf(view)) is not { } pose) return; // the reason has been said
            var state = FromView(view); var position = G(pose.Position); var euler = EulerOf(pose.Rotation);
            state.PositionX = position.X; state.PositionY = position.Y; state.PositionZ = position.Z;
            state.RotationX = Mathf.RadToDeg(euler.X); state.RotationY = Mathf.RadToDeg(euler.Y); state.RotationZ = Mathf.RadToDeg(euler.Z);
            var wanted = pose.Scale; state.ScaleX = ClampScale(wanted.X); state.ScaleY = ClampScale(wanted.Y); state.ScaleZ = ClampScale(wanted.Z);
            clamped |= Mathf.Abs(state.ScaleX - wanted.X) > .002f || Mathf.Abs(state.ScaleY - wanted.Y) > .002f || Mathf.Abs(state.ScaleZ - wanted.Z) > .002f;
            states.Add(Tidy(state));
        }
        CommitUpdates(states); _status.Text = clamped ? done + " Alguns tamanhos foram limitados a 0,01 – 64 m." : done;
    }

    private void MoveSelectionBy()
    {
        var delta = new NV(NumOr(_moveBy[0], 0), NumOr(_moveBy[1], 0), NumOr(_moveBy[2], 0)); if (delta == NV.Zero) { _status.Text = "Escreva quanto mover em pelo menos um eixo."; return; }
        var space = Space; var frame = Frame; ApplyPose(pose => TransformMath.MoveBy(pose, delta, space, frame), $"Movido {Disp(delta.X, 2)}, {Disp(delta.Y, 2)}, {Disp(delta.Z, 2)} m ({(space == AxisSpace.Local ? "eixos locais" : "eixos do mundo")}).");
    }

    private void TurnSelection(int axis)
    {
        var degrees = NumOr(_turnBy, 0); if (Mathf.IsZeroApprox(degrees)) { _status.Text = "Escreva quantos graus girar."; return; }
        var pivot = N(PivotWorld()); var space = Space; var frame = Frame;
        ApplyPose(pose => TransformMath.RotateAbout(pose, pivot, axis, degrees, space, frame), $"Girado {Disp(degrees, 2)}° em torno do eixo {"XYZ"[axis]} do pivô.");
    }

    private void StretchSelection()
    {
        var factors = new NV(NumOr(_scaleBy[0], 1), NumOr(_scaleBy[1], 1), NumOr(_scaleBy[2], 1)); if (factors == NV.One) { _status.Text = "Escreva um fator diferente de 1 em algum eixo."; return; }
        var pivot = N(PivotWorld()); var space = Space; var frame = Frame; string? problem = null;
        ApplyPose(pose => { if (TransformMath.TryScaleAbout(pose, pivot, factors, space, frame, out var result, out problem)) return result; _status.Text = problem == "needs_alignment" ? "Esticar por eixo exige objetos alinhados aos eixos escolhidos (ou o mesmo fator nos três). Escolha eixos locais ou gire os objetos." : "Os fatores precisam ser números maiores que zero."; return null; },
            $"Esticado por {Disp(factors.X, 3)} × {Disp(factors.Y, 3)} × {Disp(factors.Z, 3)} em torno do pivô.");
    }

    // ---- mirror ------------------------------------------------------------------------------------------------------------------

    /// Mirrors the selection through the plane perpendicular to a world axis that goes through the pivot, as copies (the originals stay) or in place.
    private void MirrorSelection(int axis)
    {
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que quer espelhar."; return; }
        var skipped = _selection.Count(v => v.State.PrimitiveType == "mesh");
        var usable = _selection.Where(v => v.State.PrimitiveType != "mesh").ToList();
        if (usable.Count == 0) { _status.Text = "Modelos importados não podem ser espelhados: o lado esquerdo e o direito não são iguais."; return; }
        var pivot = N(PivotWorld()); Pose Mirrored(Pose pose) => TransformMath.Mirror(pose, pivot, axis);
        if (_mirrorCopy.ButtonPressed)
        {
            var sources = usable.Select(v => Clone(FromView(v))).ToList();
            MakeCopies(sources, [pose => Mirrored(pose)], NegateTwist, $"Espelhado em {"XYZ"[axis]}: {sources.Count} cópia(s){(skipped > 0 ? $"; {skipped} modelo(s) importado(s) ficaram de fora" : string.Empty)}.");
            return;
        }
        if (SelectionProtected()) return;
        var states = usable.Select(v => { var pose = Mirrored(PoseOf(v)); var state = FromView(v); var p = G(pose.Position); var e = EulerOf(pose.Rotation); state.PositionX = p.X; state.PositionY = p.Y; state.PositionZ = p.Z; state.RotationX = Mathf.RadToDeg(e.X); state.RotationY = Mathf.RadToDeg(e.Y); state.RotationZ = Mathf.RadToDeg(e.Z); return Tidy(NegateTwist(state)); }).ToList();
        CommitUpdates(states); _status.Text = $"Espelhado em {"XYZ"[axis]} no lugar{(skipped > 0 ? $"; {skipped} modelo(s) importado(s) ficaram de fora" : string.Empty)}.";
    }

    /// The mirror image of a twisted shape twists the other way round.
    private static BuildObjectState NegateTwist(BuildObjectState state)
    {
        try
        {
            if (JsonNode.Parse(string.IsNullOrWhiteSpace(state.GeometryJson) ? "{}" : state.GeometryJson) is JsonObject geometry && geometry["twist"] is JsonValue twist && twist.TryGetValue<double>(out var degrees) && degrees != 0) { geometry["twist"] = -degrees; state.GeometryJson = geometry.ToJsonString(); }
        }
        catch (JsonException) { }
        return state;
    }

    // ---- multiple duplication ---------------------------------------------------------------------------------------------------

    private ArraySpec ArrayFromFields()
    {
        int Count(int i, int fallback) => (int)Mathf.Clamp(NumOr(_arrayCount[i], fallback), -1000, 100000);
        NV Vec(LineEdit[] f, float d) => new(NumOr(f[0], d), NumOr(f[1], d), NumOr(f[2], d));
        var kind = _arrayKind.Selected switch { 1 => ArrayKind.Grid, 2 => ArrayKind.Circle, _ => ArrayKind.Line };
        return new ArraySpec(kind, Count(0, 1), Count(1, 1), Count(2, 1), Vec(_arrayStep, 0), Vec(_arrayTurn, 0), Vec(_arrayGrow, 1), _circleAxis.Selected, _circleFace.ButtonPressed);
    }

    private void UpdateArrayInfo()
    {
        if (_arrayInfo is null) return;
        var spec = ArrayFromFields(); var copies = spec.CopyCount; var objects = copies * Math.Max(1, _selection.Count);
        _arrayInfo.Text = copies < 0 ? "Números inválidos: uma linha precisa de 1 a 200 cópias, uma grade de 2 a 201 lugares e um círculo de 2 a 201 objetos." : objects > MaxCopiedObjects ? $"Seriam {objects} objetos: o limite é {MaxCopiedObjects} por vez." : $"Cria {copies} cópia(s) × {Math.Max(1, _selection.Count)} objeto(s) = {objects} objeto(s).";
    }

    private void DuplicateMany()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que quer duplicar."; return; }
        var spec = ArrayFromFields(); var transforms = ArrayMath.Copies(spec, N(PivotWorld()), Space, Frame);
        if (transforms.Count == 0) { _status.Text = "Os números da duplicação não são válidos."; return; }
        if (transforms.Count * _selection.Count > MaxCopiedObjects) { _status.Text = $"Seriam {transforms.Count * _selection.Count} objetos: o limite é {MaxCopiedObjects} por vez."; return; }
        var sources = _selection.Select(v => Clone(FromView(v))).ToList();
        MakeCopies(sources, transforms, null, $"{transforms.Count} cópia(s) de {sources.Count} objeto(s) criada(s) ({(spec.Kind == ArrayKind.Line ? "linha" : spec.Kind == ArrayKind.Grid ? "grade" : "círculo")}).");
    }

    /// Creates copies of the sources, one set for each transform: each copy of a linkset is a linkset of its own (with the same tree), and the whole is one undo step.
    private void MakeCopies(List<BuildObjectState> sources, IReadOnlyList<Func<Pose, Pose>> transforms, Func<BuildObjectState, BuildObjectState>? tweak, string done)
    {
        if (!_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de construir."; return; }
        if (transforms.Count * sources.Count > MaxCopiedObjects) { _status.Text = $"Seriam {transforms.Count * sources.Count} objetos: o limite é {MaxCopiedObjects} por vez."; return; }
        var total = transforms.Count * sources.Count; var created = new List<Guid>(); var entry = new HistoryEntry("create", []); var actions = new List<Action>();
        var copyOf = transforms.Select(_ => new Dictionary<Guid, Guid>()).ToList(); var groups = transforms.Select(_ => sources.Where(s => s.GroupId is not null).Select(s => s.GroupId!.Value).Distinct().ToDictionary(id => id, _ => Guid.NewGuid())).ToList();
        for (var t = 0; t < transforms.Count; t++)
            foreach (var source in sources)
            {
                var index = t; var template = source; var pose = transforms[t](new Pose(new NV(source.PositionX, source.PositionY, source.PositionZ), QuatOf(new Vector3(Mathf.DegToRad(source.RotationX), Mathf.DegToRad(source.RotationY), Mathf.DegToRad(source.RotationZ))), new NV(source.ScaleX, source.ScaleY, source.ScaleZ)));
                var state = Clone(source); var euler = EulerOf(pose.Rotation); state.RotationX = Mathf.RadToDeg(euler.X); state.RotationY = Mathf.RadToDeg(euler.Y); state.RotationZ = Mathf.RadToDeg(euler.Z);
                state.ScaleX = ClampScale(pose.Scale.X); state.ScaleY = ClampScale(pose.Scale.Y); state.ScaleZ = ClampScale(pose.Scale.Z); if (tweak is not null) state = tweak(state);
                var position = G(pose.Position); var group = source.GroupId is { } g ? groups[t][g] : (Guid?)null;
                actions.Add(() => CreateFrom(state, position, group, confirmed =>
                {
                    entry.Items.Add(new HistoryItem(confirmed.Id, null, Clone(confirmed))); created.Add(confirmed.Id); copyOf[index][template.Id] = confirmed.Id;
                    if (created.Count == total) { SelectIds(created); SendHierarchyOfCopies(sources, copyOf, groups); _status.Text = done; }
                }));
            }
        PushHistory(entry); RunPaced(actions); Teach(TutorialStep.Duplicate);
        if (actions.Count > BurstLimit) _status.Text = $"Criando {total} objetos aos poucos…";
    }

    private void SendHierarchyOfCopies(List<BuildObjectState> sources, List<Dictionary<Guid, Guid>> copyOf, List<Dictionary<Guid, Guid>> groups)
    {
        var links = new List<LinkNode>();
        for (var t = 0; t < copyOf.Count; t++) links.AddRange(HierarchyLinksForCopies(sources, copyOf[t], groups[t]));
        for (var from = 0; from < links.Count; from += 200) SendStructure(links.GetRange(from, Math.Min(200, links.Count - from)), string.Empty, history: false);
    }

    // ---- the pivot of one object ----------------------------------------------------------------------------------------------

    /// Sets the pivot of every selected object (an offset from its centre, in its own axes). It is saved with the object.
    private void ApplyObjectPivot(Vector3? offset)
    {
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos primeiro."; return; }
        if (SelectionProtected()) return;
        var states = _selection.Select(v => { var s = FromView(v); var o = offset ?? new Vector3(NumOr(_objectPivot[0], 0), NumOr(_objectPivot[1], 0), NumOr(_objectPivot[2], 0)); s.PivotX = Mathf.Clamp(o.X, -64, 64); s.PivotY = Mathf.Clamp(o.Y, -64, 64); s.PivotZ = Mathf.Clamp(o.Z, -64, 64); return s; }).ToList();
        CommitUpdates(states); _status.Text = "Pivô do objeto guardado. Escolha “Pivô do objeto” em Onde girar para usá-lo."; FillPivotFields();
    }

    private void FillPivotFields()
    {
        if (_objectPivot is null || _selection.Count == 0) return;
        var s = _selection[0].State; _objectPivot[0].Text = Disp(s.PivotX); _objectPivot[1].Text = Disp(s.PivotY); _objectPivot[2].Text = Disp(s.PivotZ);
    }

    // ---- the tab ------------------------------------------------------------------------------------------------------------------

    private static OptionButton Pick(params string[] items) { var option = new OptionButton { CustomMinimumSize = new Vector2(0, 44), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; option.AddThemeFontSizeOverride("font_size", 20); foreach (var item in items) option.AddItem(item); return option; }

    private Control BuildTransformTab()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12);

        var where = Card("Onde girar e por onde", "target", out var whereBody, out _, 10); page.AddChild(where);
        whereBody.AddChild(Txt("As alças e os números abaixo giram e esticam em torno do pivô, e movem pelos eixos escolhidos.", 18, false, UiStyle.TextMuted));
        var line = Row(12); whereBody.AddChild(line);
        line.AddChild(Txt("Onde girar", 20, true)); _pivotMode = Pick(PivotModes); _pivotMode.ItemSelected += _ => { RefreshGizmo(); UpdatePivotInfo(); }; line.AddChild(_pivotMode);
        line.AddChild(Txt("Eixos", 20, true)); _axesSpace = Pick("Do mundo", "Locais (do primeiro selecionado)", "De referência (objeto escolhido)"); _axesSpace.ItemSelected += _ => { RefreshGizmo(); UpdatePivotInfo(); }; line.AddChild(_axesSpace);
        line.AddChild(IconButton("pin", "Usar como referência", "O primeiro objeto selecionado passa a ser a referência dos eixos e da grade (escolha “De referência” em Eixos)", UseSelectionAsReference, UiStyle.ButtonKind.Tool, 44));
        var point = Row(10); whereBody.AddChild(point); point.AddChild(Txt("Ponto escolhido", 19)); _pivotAt = TripleOf(point, "x", "y", "z", "0"); foreach (var f in _pivotAt) f.TextChanged += _ => { if (_pivotMode.Selected == PivotPoint) { RefreshGizmo(); UpdatePivotInfo(); } };
        point.AddChild(IconButton("pin", "Usar o primeiro", "Põe aqui a posição do primeiro objeto selecionado", () => { if (_selection.Count == 0) return; var p = _selection[0].Position; _pivotAt[0].Text = Disp(p.X); _pivotAt[1].Text = Disp(p.Y); _pivotAt[2].Text = Disp(p.Z); _pivotMode.Select(PivotPoint); RefreshGizmo(); UpdatePivotInfo(); }, UiStyle.ButtonKind.Secondary, 44));
        _pivotInfo = Txt("Selecione algo para ver onde fica o pivô.", 18, false, UiStyle.TextMuted); whereBody.AddChild(_pivotInfo);
        AddStretchOptions(whereBody);

        var own = Card("Pivô do objeto", "pin", out var ownBody, out _, 10); page.AddChild(own);
        ownBody.AddChild(Txt("Cada objeto pode ter um pivô próprio: um deslocamento (em metros, nos eixos dele) a partir do centro. Fica salvo com o objeto, como a dobradiça de uma porta.", 18, false, UiStyle.TextMuted));
        var ownRow = Row(10); ownBody.AddChild(ownRow); _objectPivot = TripleOf(ownRow, "x", "y", "z", "0");
        ownRow.AddChild(IconButton("check", "Guardar pivô", "Guarda esses números como pivô dos objetos selecionados", () => ApplyObjectPivot(null), UiStyle.ButtonKind.Primary, 44));
        var ownQuick = Row(10); ownBody.AddChild(ownQuick);
        ownQuick.AddChild(IconButton("ground", "Pivô na base", "Põe o pivô no meio da face de baixo do primeiro objeto", () => { if (_selection.Count > 0) ApplyObjectPivot(new Vector3(0, -_selection[0].Scale.Y * .5f, 0)); }, UiStyle.ButtonKind.Secondary, 44));
        ownQuick.AddChild(IconButton("focus", "No centro", "Volta o pivô ao centro do objeto", () => ApplyObjectPivot(Vector3.Zero), UiStyle.ButtonKind.Secondary, 44));

        var exact = Card("Mover, girar e esticar com números", "move", out var exactBody, out _, 10); page.AddChild(exact);
        var move = Row(10); exactBody.AddChild(move); move.AddChild(Txt("Mover", 20, true)); _moveBy = TripleOf(move, "x", "y", "z", "0"); move.AddChild(IconButton("check", "Mover", "Move a seleção por esses metros, pelos eixos escolhidos", MoveSelectionBy, UiStyle.ButtonKind.Primary, 44));
        var turn = Row(10); exactBody.AddChild(turn); turn.AddChild(Txt("Girar", 20, true)); _turnBy = Field("graus", 8); _turnBy.Text = "90"; _turnBy.CustomMinimumSize = new Vector2(120, 44); turn.AddChild(_turnBy);
        for (var axis = 0; axis < 3; axis++) { var a = axis; turn.AddChild(IconButton("rotate", $"em {"XYZ"[axis]}", $"Gira em torno do eixo {"XYZ"[axis]} que passa pelo pivô", () => TurnSelection(a), UiStyle.ButtonKind.Secondary, 44)); }
        var stretch = Row(10); exactBody.AddChild(stretch); stretch.AddChild(Txt("Esticar", 20, true)); _scaleBy = TripleOf(stretch, "× x", "× y", "× z", "1"); stretch.AddChild(IconButton("check", "Esticar", "Multiplica o tamanho e a distância ao pivô por esses fatores", StretchSelection, UiStyle.ButtonKind.Primary, 44));

        var mirror = Card("Espelhar", "layers", out var mirrorBody, out _, 10); page.AddChild(mirror);
        mirrorBody.AddChild(Txt("Espelha pelo plano que passa pelo pivô e é perpendicular ao eixo escolhido. Formas torcidas torcem ao contrário; modelos importados não são espelhados.", 18, false, UiStyle.TextMuted));
        var mir = Row(10); mirrorBody.AddChild(mir); _mirrorCopy = new CheckBox { Text = " Manter o original (criar cópias)", ButtonPressed = true }; _mirrorCopy.AddThemeFontSizeOverride("font_size", 20); mir.AddChild(_mirrorCopy);
        for (var axis = 0; axis < 3; axis++) { var a = axis; mir.AddChild(IconButton("layers", $"Espelhar em {"XYZ"[axis]}", $"Espelha através do plano {"YZ XZ XY".Split(' ')[axis]} pelo pivô", () => MirrorSelection(a), UiStyle.ButtonKind.Secondary, 44)); }

        var many = Card("Duplicação múltipla", "copy-plus", out var manyBody, out _, 10); page.AddChild(many);
        manyBody.AddChild(Txt("Linha: cada cópia anda o passo, gira e cresce mais um pouco. Grade: quantidades e distâncias nos três eixos. Círculo: objetos igualmente espaçados em volta do pivô.", 18, false, UiStyle.TextMuted));
        var kind = Row(10); manyBody.AddChild(kind); kind.AddChild(Txt("Tipo", 20, true)); _arrayKind = Pick("Linha", "Grade", "Círculo"); _arrayKind.ItemSelected += _ => UpdateArrayInfo(); kind.AddChild(_arrayKind);
        _circleAxis = Pick("Eixo X", "Eixo Y", "Eixo Z"); _circleAxis.Select(1); kind.AddChild(_circleAxis); _circleFace = new CheckBox { Text = " Virar para o centro", ButtonPressed = true }; _circleFace.AddThemeFontSizeOverride("font_size", 18); kind.AddChild(_circleFace);
        var counts = Row(10); manyBody.AddChild(counts); counts.AddChild(Txt("Quantidade", 20, true)); _arrayCount = TripleOf(counts, "n / x", "y", "z", "3"); _arrayCount[1].Text = "1"; _arrayCount[2].Text = "1";
        var step = Row(10); manyBody.AddChild(step); step.AddChild(Txt("Passo (m)", 20, true)); _arrayStep = TripleOf(step, "x", "y", "z", "0"); _arrayStep[0].Text = "2";
        var turnStep = Row(10); manyBody.AddChild(turnStep); turnStep.AddChild(Txt("Giro por cópia (°)", 20, true)); _arrayTurn = TripleOf(turnStep, "x", "y", "z", "0");
        var grow = Row(10); manyBody.AddChild(grow); grow.AddChild(Txt("Tamanho por cópia (×)", 20, true)); _arrayGrow = TripleOf(grow, "x", "y", "z", "1");
        foreach (var f in _arrayCount.Concat(_arrayStep).Concat(_arrayTurn).Concat(_arrayGrow)) f.TextChanged += _ => UpdateArrayInfo();
        _arrayInfo = Txt(string.Empty, 18, false, UiStyle.TextMuted); manyBody.AddChild(_arrayInfo);
        manyBody.AddChild(IconButton("copy-plus", "Criar as cópias", "Cria as cópias com esses números (um passo só no desfazer)", DuplicateMany, UiStyle.ButtonKind.Primary, 48));
        UpdateArrayInfo();
        BuildMagnetAndAreaCards(page);
        return page;
    }

    private LineEdit[] TripleOf(Control row, string a, string b, string c, string initial)
    {
        var fields = new LineEdit[3];
        for (var i = 0; i < 3; i++) { fields[i] = Field(new[] { a, b, c }[i], 10); fields[i].Text = initial; fields[i].CustomMinimumSize = new Vector2(110, 44); fields[i].SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(fields[i]); }
        return fields;
    }

    // ---- tests ------------------------------------------------------------------------------------------------------------------

    public void SetPivotModeForTesting(int mode) { _pivotMode.Select(mode); RefreshGizmo(); UpdatePivotInfo(); }
    public void SetAxesForTesting(bool local) { _axesSpace.Select(local ? 1 : 0); RefreshGizmo(); }
    public void SetPivotPointForTesting(float x, float y, float z) { _pivotAt[0].Text = Disp(x); _pivotAt[1].Text = Disp(y); _pivotAt[2].Text = Disp(z); }
    public Vector3 PivotForTesting => PivotWorld();
    public void SetObjectPivotForTesting(float x, float y, float z) { _objectPivot[0].Text = Disp(x); _objectPivot[1].Text = Disp(y); _objectPivot[2].Text = Disp(z); ApplyObjectPivot(null); }
    public void MoveByForTesting(float x, float y, float z) { _moveBy[0].Text = Disp(x); _moveBy[1].Text = Disp(y); _moveBy[2].Text = Disp(z); MoveSelectionBy(); }
    public void TurnForTesting(int axis, float degrees) { _turnBy.Text = Disp(degrees); TurnSelection(axis); }
    public void StretchForTesting(float x, float y, float z) { _scaleBy[0].Text = Disp(x); _scaleBy[1].Text = Disp(y); _scaleBy[2].Text = Disp(z); StretchSelection(); }
    public void MirrorForTesting(int axis, bool copy) { _mirrorCopy.ButtonPressed = copy; MirrorSelection(axis); }
    public void ArrayForTesting(int kind, int nx, int ny, int nz, Vector3 step, Vector3 turn, Vector3 grow, int axis = 1, bool face = true)
    {
        _arrayKind.Select(kind); _arrayCount[0].Text = nx.ToString(); _arrayCount[1].Text = ny.ToString(); _arrayCount[2].Text = nz.ToString(); _circleAxis.Select(axis); _circleFace.ButtonPressed = face;
        for (var i = 0; i < 3; i++) { _arrayStep[i].Text = Disp(step[i]); _arrayTurn[i].Text = Disp(turn[i]); _arrayGrow[i].Text = Disp(grow[i]); }
        UpdateArrayInfo(); DuplicateMany();
    }
    public string ArrayInfoForTesting => _arrayInfo.Text;
    public string PivotInfoForTesting { get { UpdatePivotInfo(); return _pivotInfo.Text; } }
}

public partial class BuildEditor
{
    private void UseSelectionAsReference()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione o objeto que será a referência."; return; }
        _referenceObject = _selection[0].State.Id; _axesSpace.Select(2); RefreshGizmo(); UpdatePivotInfo();
        _status.Text = $"“{_selection[0].State.Name}” é a referência dos eixos e da grade.";
    }

    public void UseSelectionAsReferenceForTesting() => UseSelectionAsReference();
    public void SetAxesForTesting(int index) { _axesSpace.Select(index); RefreshGizmo(); }
    public Vector3 AxesEulerForTesting => AxesEuler;
    public Vector3 GridRotationForTesting => _grid.Rotation;
}
