using Godot;
using LifeVerse.Shared.Building;
using NQ = System.Numerics.Quaternion;
using NV = System.Numerics.Vector3;

namespace LifeVerse.Client.Scripts;

/// A rectangle drawn on the screen while choosing objects by area: solid for "inside" (dragged left to right), dashed-looking for "crossing" (right to left).
public sealed partial class MarqueeOverlay : Control
{
    public Rect2 Area { get; set; }
    public bool Crossing { get; set; }
    public MarqueeOverlay() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsPreset(LayoutPreset.FullRect); Visible = false; }
    public override void _Draw()
    {
        if (!Visible) return;
        var color = Crossing ? new Color(.98f, .72f, .29f) : new Color(.07f, .95f, .88f);
        DrawRect(Area, new Color(color, .14f)); DrawRect(Area, color, false, 2f);
    }
}

/// Magnetic snap, choosing objects by area (a rectangle on the screen or a box in the world) and the advanced alignment and distribution.
public partial class BuildEditor
{
    // ---- magnetic snap --------------------------------------------------------------------------------------------------------------

    private CheckBox _magnet = null!, _magVertex = null!, _magEdge = null!, _magFace = null!, _magContact = null!;
    private LineEdit _magRadius = null!;
    private MeshInstance3D? _magnetMarker;
    private List<SnapPoint> _magnetDragged = [], _magnetTargets = [];
    private List<Bounds> _magnetContactTargets = [];
    private Bounds _magnetSelectionBounds;
    private string _magnetHint = string.Empty;

    private SnapSettings MagnetSettings() => new(_magVertex.ButtonPressed, _magEdge.ButtonPressed, _magFace.ButtonPressed, _magContact.ButtonPressed, Mathf.Clamp(NumOr(_magRadius, .3f), .05f, 3f));

    /// The oriented box of an object (centre, turn and half sizes) as its visible mesh sees it.
    private static (NV Center, NQ Turn, NV Half) ObbOf(BuildObjectView view)
    {
        var mesh = view.GetNodeOrNull<MeshInstance3D>("PrimitiveMesh"); var aabb = mesh?.GetAabb() ?? new Aabb(-Vector3.One / 2, Vector3.One);
        var transform = mesh is not null && mesh.IsInsideTree() ? mesh.GlobalTransform : view.GlobalTransform;
        var center = transform * (aabb.Position + aabb.Size / 2); var basis = transform.Basis; var half = aabb.Size / 2 * basis.Scale; var q = basis.Orthonormalized().GetRotationQuaternion();
        return (N(center), new NQ(q.X, q.Y, q.Z, q.W), N(half.Abs()));
    }

    private static bool AxisAligned(BuildObjectView view)
    {
        var basis = view.GlobalTransform.Basis.Orthonormalized();
        foreach (var column in new[] { basis.X, basis.Y, basis.Z }) if (Mathf.Max(Mathf.Abs(column.X), Mathf.Max(Mathf.Abs(column.Y), Mathf.Abs(column.Z))) < .9994f) return false; // within 2 degrees of an axis
        return true;
    }

    /// Collects, when a drag starts, the points the selection can snap by and the points it can snap to (so the drag itself only does arithmetic).
    private void PrepareMagnet()
    {
        _magnetDragged = []; _magnetTargets = []; _magnetContactTargets = []; _magnetHint = string.Empty;
        if (_magnet is null || !_magnet.ButtonPressed || _selection.Count == 0) return;
        var settings = MagnetSettings(); var boxes = _selection.Select(WorldBounds).ToList();
        var union = new Bounds(boxes.Aggregate(boxes[0].Min, (m, b) => NV.Min(m, b.Min)), boxes.Aggregate(boxes[0].Max, (m, b) => NV.Max(m, b.Max))); _magnetSelectionBounds = union;
        if (_selection.Count <= 12) foreach (var view in _selection) { var (c, t, h) = ObbOf(view); _magnetDragged.AddRange(SnapMath.Features(c, t, h, settings)); }
        else _magnetDragged.AddRange(SnapMath.Features(union.Center, NQ.Identity, (union.Max - union.Min) / 2f, settings)); // a big selection snaps by the box around all of it
        var reach = settings.Radius + 30f; var around = G(union.Center);
        foreach (var view in _objects.Values.Where(v => !_selection.Contains(v) && v.Visible && v.Position.DistanceTo(around) < reach + v.Scale.Length()).OrderBy(v => v.Position.DistanceTo(around)).Take(120))
        {
            var (c, t, h) = ObbOf(view); _magnetTargets.AddRange(SnapMath.Features(c, t, h, settings));
            if (settings.Contact && AxisAligned(view)) _magnetContactTargets.Add(WorldBounds(view));
        }
    }

    /// The amount along the dragged axis after the magnet has pulled the selection (null when nothing is close), and what it snapped to.
    private SnapResult? MagnetAdjust(Vector3 axisDirection, float amount)
    {
        if (_magnet is null || !_magnet.ButtonPressed || _magnetDragged.Count == 0) return null;
        var settings = MagnetSettings(); var direction = N(axisDirection.Normalized());
        var best = _magnetTargets.Count > 0 ? SnapMath.AlongAxis(_magnetDragged, _magnetTargets, direction, amount, settings.Radius) : null;
        if (settings.Contact && _magnetContactTargets.Count > 0)
        {
            var axis = Mathf.Abs(axisDirection.X) > .999f ? 0 : Mathf.Abs(axisDirection.Y) > .999f ? 1 : Mathf.Abs(axisDirection.Z) > .999f ? 2 : -1;
            if (axis >= 0 && SnapMath.Contact(_magnetSelectionBounds, _magnetContactTargets, axis, amount * (axisDirection[axis] < 0 ? -1 : 1), settings.Radius) is { } touch)
            {
                var signed = new SnapResult(touch.Amount * (axisDirection[axis] < 0 ? -1 : 1), touch.Kind, touch.Target);
                if (best is null || Mathf.Abs(signed.Amount - amount) < Mathf.Abs(best.Value.Amount - amount) - .001f) best = signed;
            }
        }
        return best;
    }

    private void ShowMagnet(SnapResult? snap)
    {
        if (snap is not { } s) { if (_magnetMarker is not null) _magnetMarker.Visible = false; return; }
        if (_magnetMarker is null) { _magnetMarker = new MeshInstance3D { Mesh = new SphereMesh { Radius = .1f, Height = .2f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, .85f, .2f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true } }; GetParent().AddChild(_magnetMarker); }
        _magnetMarker.GlobalPosition = G(s.Target); _magnetMarker.Visible = true;
        _magnetHint = s.Kind switch { SnapKind.Vertex => "Encaixe: canto", SnapKind.Edge => "Encaixe: meio da aresta", SnapKind.Face => "Encaixe: centro da face", _ => "Encaixe: faces encostando" };
        _status.Text = _magnetHint;
    }

    public SnapResult? MagnetForTesting(Vector3 axis, float amount) { PrepareMagnet(); return MagnetAdjust(axis, amount); }

    // ---- choosing by area -----------------------------------------------------------------------------------------------------------

    private CanvasLayer? _marqueeLayer;
    private MarqueeOverlay? _marquee;
    private Vector2? _marqueeStart;
    private bool _marqueeAdditive, _marqueeDrawn;
    private LineEdit[] _volumeMin = null!, _volumeMax = null!;

    private void BeginMarquee(Vector2 screen, bool additive) { _marqueeStart = screen; _marqueeAdditive = additive; _marqueeDrawn = false; }

    /// Follows the mouse while a rectangle is being drawn, and chooses what is in it when the button is let go.
    private void PumpMarquee()
    {
        if (_marqueeStart is not { } start) return;
        if (!_active || _dragging) { EndMarquee(); return; }
        var mouse = GetViewport().GetMousePosition();
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (!_marqueeDrawn && start.DistanceTo(mouse) < 6f) return; // a click, not a rectangle yet
            _marqueeDrawn = true; if (_marquee is null) { _marqueeLayer = new CanvasLayer { Layer = 40 }; _marquee = new MarqueeOverlay(); _marqueeLayer.AddChild(_marquee); AddChild(_marqueeLayer); }
            _marquee.Area = new Rect2(new Vector2(Mathf.Min(start.X, mouse.X), Mathf.Min(start.Y, mouse.Y)), (mouse - start).Abs()); _marquee.Crossing = mouse.X < start.X; _marquee.Visible = true; _marquee.QueueRedraw();
            return;
        }
        if (_marqueeDrawn) SelectInRectangle(start, mouse, mouse.X < start.X, _marqueeAdditive);
        EndMarquee();
    }

    private void EndMarquee() { _marqueeStart = null; _marqueeDrawn = false; if (_marquee is not null) _marquee.Visible = false; }

    /// Chooses the objects whose box on the screen is inside the rectangle (or, dragging right to left, at least crosses it), whole linksets included unless "Editar só a peça" is on.
    public int SelectInRectangle(Vector2 from, Vector2 to, bool crossing, bool additive)
    {
        var camera = GetViewport().GetCamera3D(); if (camera is null) return 0;
        var views = _objects.Values.Where(v => v.Visible).ToList();
        var rects = views.Select(view =>
        {
            var box = WorldBounds(view); var corners = new List<Vector2?>();
            for (var i = 0; i < 8; i++) { var corner = new Vector3((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z); corners.Add(camera.IsPositionBehind(corner) ? null : camera.UnprojectPosition(corner)); }
            return AreaSelect.RectangleOf(corners.Select(c => c is { } v ? (System.Numerics.Vector2?)new System.Numerics.Vector2(v.X, v.Y) : null).ToList());
        }).ToList();
        var chosen = AreaSelect.InRectangle(rects, ScreenRect.Between(new System.Numerics.Vector2(from.X, from.Y), new System.Numerics.Vector2(to.X, to.Y)), crossing).Select(i => views[i]).ToList();
        return SelectViews(chosen, additive, crossing ? "tocados pelo retângulo" : "dentro do retângulo");
    }

    /// Chooses the objects inside (or touching) a box of the world.
    public int SelectInVolume(Vector3 a, Vector3 b, bool touching)
    {
        var views = _objects.Values.ToList(); var volume = AreaSelect.VolumeBetween(N(a), N(b));
        var chosen = AreaSelect.InVolume(views.Select(WorldBounds).ToList(), volume, touching).Select(i => views[i]).ToList();
        return SelectViews(chosen, false, touching ? "tocando a área" : "dentro da área");
    }

    private int SelectViews(List<BuildObjectView> chosen, bool additive, string how)
    {
        var all = new List<BuildObjectView>();
        foreach (var view in chosen) foreach (var member in _partMode ? [view] : MembersOf(view)) if (!all.Contains(member)) all.Add(member);
        if (!additive) ClearSelectionVisuals();
        foreach (var view in all) if (!_selection.Contains(view)) { _selection.Add(view); view.SetSelected(true); }
        AfterSelectionChanged();
        _status.Text = all.Count == 0 ? "Nenhum objeto na área." : $"{all.Count} objeto(s) {how}.";
        return all.Count;
    }

    private void SelectInFields(bool touching)
    {
        var a = new Vector3(NumOr(_volumeMin[0], 0), NumOr(_volumeMin[1], 0), NumOr(_volumeMin[2], 0)); var b = new Vector3(NumOr(_volumeMax[0], 0), NumOr(_volumeMax[1], 0), NumOr(_volumeMax[2], 0));
        SelectInVolume(a, b, touching);
    }

    private void FillVolumeFromSelection()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione algo para usar a caixa dele como área."; return; }
        var boxes = _selection.Select(WorldBounds).ToList(); var min = boxes.Aggregate(boxes[0].Min, (m, x) => NV.Min(m, x.Min)); var max = boxes.Aggregate(boxes[0].Max, (m, x) => NV.Max(m, x.Max));
        for (var i = 0; i < 3; i++) { _volumeMin[i].Text = Disp(new[] { min.X, min.Y, min.Z }[i], 2); _volumeMax[i].Text = Disp(new[] { max.X, max.Y, max.Z }[i], 2); }
    }

    // ---- the cards ------------------------------------------------------------------------------------------------------------

    /// "Encaixe magnético" and "Seleção por área", at the end of the Transformar tab.
    private void BuildMagnetAndAreaCards(VBoxContainer page)
    {
        var magnet = Card("Encaixe magnético", "target", out var magnetBody, out _, 10); page.AddChild(magnet);
        magnetBody.AddChild(Txt("Ao arrastar uma seta, a seleção é puxada para os cantos, o meio das arestas e o centro das faces de outros objetos que estão perto do caminho, ou faz uma face encostar na de outro. O encaixe usa a caixa de contorno de cada objeto.", 18, false, UiStyle.TextMuted));
        _magnet = new CheckBox { Text = " Ligar o encaixe magnético" }; _magnet.AddThemeFontSizeOverride("font_size", 20); magnetBody.AddChild(_magnet);
        var kinds = Row(14); magnetBody.AddChild(kinds);
        _magVertex = new CheckBox { Text = " Cantos", ButtonPressed = true }; _magEdge = new CheckBox { Text = " Arestas", ButtonPressed = true }; _magFace = new CheckBox { Text = " Centro das faces", ButtonPressed = true }; _magContact = new CheckBox { Text = " Faces encostando", ButtonPressed = true };
        foreach (var box in new[] { _magVertex, _magEdge, _magFace, _magContact }) { box.AddThemeFontSizeOverride("font_size", 19); kinds.AddChild(box); }
        var radius = Row(10); magnetBody.AddChild(radius); radius.AddChild(Txt("Alcance (m)", 20, true)); _magRadius = Field("0,3", 6); _magRadius.Text = "0,3"; _magRadius.CustomMinimumSize = new Vector2(120, 44); radius.AddChild(_magRadius);

        var area = Card("Seleção por área", "layers", out var areaBody, out _, 10); page.AddChild(area);
        areaBody.AddChild(Txt("Arraste um retângulo no mundo, em um espaço vazio: da esquerda para a direita escolhe o que ficou inteiro dentro; da direita para a esquerda, também o que o retângulo toca (Shift soma). Ou escolha por uma caixa de coordenadas:", 18, false, UiStyle.TextMuted));
        var min = Row(10); areaBody.AddChild(min); min.AddChild(Txt("Canto A", 20, true)); _volumeMin = TripleOf(min, "x", "y", "z", "0");
        var max = Row(10); areaBody.AddChild(max); max.AddChild(Txt("Canto B", 20, true)); _volumeMax = TripleOf(max, "x", "y", "z", "0");
        var buttons = Row(10); areaBody.AddChild(buttons);
        buttons.AddChild(IconButton("target", "Usar a seleção", "Preenche os cantos com a caixa da seleção atual", FillVolumeFromSelection, UiStyle.ButtonKind.Secondary, 44));
        buttons.AddChild(IconButton("check", "Escolher dentro", "Escolhe o que está inteiro dentro da caixa", () => SelectInFields(false), UiStyle.ButtonKind.Primary, 44));
        buttons.AddChild(IconButton("check", "Escolher tocando", "Escolhe também o que só encosta na caixa", () => SelectInFields(true), UiStyle.ButtonKind.Secondary, 44));
    }

    // ---- advanced alignment ----------------------------------------------------------------------------------------------------

    private OptionButton _alignTo = null!, _distributeHow = null!; private LineEdit _distributeGap = null!;
    private static readonly string[] AlignReferences = ["selection", "first", "last", "largest", "smallest", "root"];

    /// The extra options of the Alinhar tab: align to the selection (as always) or to one object, and distribute with equal gaps, equal centres or a fixed gap.
    private PanelContainer BuildAdvancedAlignCard()
    {
        var card = Card("Alinhar e distribuir: opções", "layers", out var body, out _, 10);
        body.AddChild(Txt("“Alinhar a” vale para os botões de cima (esquerda, centro, direita…): o objeto escolhido fica onde está e os outros vêm até ele. “Distribuir” escolhe como o botão Distribuir espalha os objetos.", 18, false, UiStyle.TextMuted));
        var line = Row(12); body.AddChild(line);
        line.AddChild(Txt("Alinhar a", 20, true)); _alignTo = Pick("A seleção toda", "Primeiro selecionado", "Último selecionado", "O maior no eixo", "O menor no eixo", "Raiz do conjunto"); line.AddChild(_alignTo);
        var spread = Row(12); body.AddChild(spread);
        spread.AddChild(Txt("Distribuir", 20, true)); _distributeHow = Pick("Espaços iguais", "Centros igualmente afastados", "Espaço fixo entre eles (m)"); spread.AddChild(_distributeHow);
        _distributeGap = Field("0", 6); _distributeGap.Text = "0"; _distributeGap.CustomMinimumSize = new Vector2(110, 44); spread.AddChild(_distributeGap);
        return card;
    }

    /// The index in the selection of the object to align to, or -1 for "the whole selection".
    private int AlignReferenceIndex(IReadOnlyList<Bounds> boxes, AlignAxis axis)
    {
        var which = _alignTo is null ? "selection" : AlignReferences[Mathf.Clamp(_alignTo.Selected, 0, AlignReferences.Length - 1)];
        if (which == "root") { if (_selection[0].State.GroupId is not { } group || LinksetRules.RootOf(LinkWorld(), group) is not { } root) return -1; return _selection.ToList().FindIndex(v => v.State.Id == root); }
        return AlignMath.PickReference(boxes, axis, which);
    }

    public void SetAlignOptionsForTesting(int alignTo, int distribute, float gap) { _alignTo.Select(alignTo); _distributeHow.Select(distribute); _distributeGap.Text = Disp(gap); }
    public int SelectInRectangleForTesting(Vector2 from, Vector2 to, bool crossing, bool additive = false) => SelectInRectangle(from, to, crossing, additive);
    public int SelectInVolumeForTesting(Vector3 a, Vector3 b, bool touching) => SelectInVolume(a, b, touching);
    public void SetMagnetForTesting(bool on, float radius = .3f, bool contact = true) { _magnet.ButtonPressed = on; _magRadius.Text = Disp(radius); _magContact.ButtonPressed = contact; }
    public void BeginMarqueeForTesting(Vector2 screen) => BeginMarquee(screen, false);
}
