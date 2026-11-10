using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// The five tabs of the Construir window (Geral, Objeto, Recursos, Textura, Conteúdo) laid out like the reference image: one narrow column of labelled rows,
/// numeric boxes instead of sliders, and the same controls and keys as before. A slider that carries a value is kept as the single source of truth and shown as a
/// number box (<see cref="SpinFor"/>), so every place that reads or writes the slider keeps working.
public partial class BuildEditor
{
    private Label _creatorValue = null!, _ownerValue = null!;
    private OptionButton _facePick = null!;
    private readonly Dictionary<HSlider, List<SpinBox>> _spins = [];

    // ---- small helpers --------------------------------------------------------------------------------------

    private static Label Heading(string text)
    {
        var label = Txt(text, 20, true, UiStyle.Text); label.AddThemeColorOverride("font_color", UiStyle.AccentText); return label;
    }

    private HBoxContainer FormRow(string label, Control control, int labelWidth = 190)
    {
        var row = Row(10); var name = Txt(label, 19, false, UiStyle.Text); name.CustomMinimumSize = new Vector2(labelWidth, 0); name.VerticalAlignment = VerticalAlignment.Center; row.AddChild(name);
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(control); return row;
    }

    /// A number box for a slider (two-way). <paramref name="invert"/> shows 100 minus the percentage, for transparency (the stored value is opacity).
    private SpinBox SpinFor(HSlider slider, Action? changed = null, bool percentInverted = false)
    {
        var step = percentInverted ? 1 : slider.Step; var min = percentInverted ? 0 : slider.MinValue; var max = percentInverted ? 100 : slider.MaxValue;
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(120, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Editable = slider.Editable, UpdateOnTextChanged = false, SelectAllOnFocus = true };
        spin.GetLineEdit().AddThemeFontSizeOverride("font_size", 19);
        double ToShown(double v) => percentInverted ? Math.Round((1 - v) * 100) : v; double ToStored(double v) => percentInverted ? 1 - v / 100.0 : v;
        spin.SetValueNoSignal(ToShown(slider.Value));
        spin.ValueChanged += v => { var stored = ToStored(v); if (Math.Abs(slider.Value - stored) > 1e-9) slider.Value = stored; changed?.Invoke(); };
        slider.ValueChanged += v => spin.SetValueNoSignal(ToShown(v));
        if (!_spins.TryGetValue(slider, out var list)) _spins[slider] = list = []; list.Add(spin);
        return spin;
    }

    private void SyncSpins(HSlider slider) { if (_spins.TryGetValue(slider, out var list)) foreach (var spin in list) spin.Editable = slider.Editable; }

    private HBoxContainer PairRow(string label, params (string Caption, Control Box)[] cells)
    {
        var row = Row(8); var name = Txt(label, 19); name.CustomMinimumSize = new Vector2(170, 0); name.VerticalAlignment = VerticalAlignment.Center; row.AddChild(name);
        foreach (var (caption, box) in cells)
        {
            if (caption.Length > 0) { var c = Txt(caption, 18, false, UiStyle.TextMuted); c.VerticalAlignment = VerticalAlignment.Center; row.AddChild(c); }
            box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(box);
        }
        return row;
    }

    private static VBoxContainer Column(int gap = 8) { var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", gap); return box; }

    private static CheckBox Check(string text, string tip = "")
    {
        var box = new CheckBox { Text = " " + text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 19); return box;
    }

    // ---- Geral ----------------------------------------------------------------------------------------------

    private Control BuildGeneral()
    {
        var box = Column(8);
        _name = Field("Nome do objeto", 160); _name.TextSubmitted += _ => ApplySelected(); box.AddChild(FormRow("Nome:", _name, 150));
        _description = new TextEdit { PlaceholderText = "Descrição (opcional)", WrapMode = TextEdit.LineWrappingMode.Boundary, CustomMinimumSize = new Vector2(0, 64), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _description.AddThemeFontSizeOverride("font_size", 19);
        _description.TextChanged += () => { if (_description.Text.Length > 4000) _description.Text = _description.Text[..4000]; };
        box.AddChild(FormRow("Descrição:", _description, 150));
        var apply = IconButton("check", "Aplicar nome e descrição", "Envia as alterações ao servidor", ApplySelected, height: 40); apply.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; apply.CustomMinimumSize = new Vector2(300, 40); box.AddChild(apply);
        _creatorValue = Txt("—", 19, true, UiStyle.AccentText); _ownerValue = Txt("—", 19, true, UiStyle.AccentText);
        box.AddChild(FormRow("Criador:", _creatorValue, 150)); box.AddChild(FormRow("Proprietário:", _ownerValue, 150));
        _infoLeft = Muted(string.Empty, 17); _infoRight = Muted(string.Empty, 16); _infoRight.AutowrapMode = TextServer.AutowrapMode.Arbitrary; box.AddChild(_infoRight);
        box.AddChild(Rule());
        BuildSharingSection(box);
        BuildPackageSection(box);
        BuildCollabSection(box);
        return box;
    }

    // ---- Objeto ---------------------------------------------------------------------------------------------

    private Control BuildObjectTab()
    {
        var page = Column(8);
        // the tools of the old tool column stay reachable: icons in one row, and the four mode buttons are kept (hidden) so the tool state still has somewhere to show
        _toolSelect = ListTool("pointer", "Selecionar", "Selecionar objetos (1)", () => SelectTool()); _toolMove = ListTool("move", "Mover", "Mover (2)", () => SetMode(GizmoMode.Move));
        _toolRotate = ListTool("rotate", "Rotacionar", "Girar (3)", () => SetMode(GizmoMode.Rotate)); _toolScale = ListTool("expand", "Redimensionar", "Redimensionar (4)", () => SetMode(GizmoMode.Scale));
        var actions = Row(6);
        foreach (var (icon, tip, action) in new (string, string, Action)[] { ("copy-plus", "Duplicar (Ctrl+D)", Duplicate), ("copy", "Copiar (Ctrl+C)", CopySelection), ("paste", "Colar (Ctrl+V)", Paste), ("link", "Agrupar (Ctrl+G)", GroupSelection), ("unlink", "Desagrupar (Ctrl+Shift+G)", Ungroup), ("trash", "Excluir (Delete)", DeleteSelection) })
        { var button = SquareIcon(icon, tip, action); button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; actions.AddChild(button); }
        page.AddChild(actions);

        page.AddChild(Heading("Posição, rotação e tamanho"));
        _snap = Field("0,5", 8); _snap.Text = Disp(.5); _snap.TooltipText = "Passo do movimento com as setas e da posição aplicada"; _snap.CustomMinimumSize = new Vector2(110, 40);
        page.AddChild(TripleRow("Posição", _position, () => GridStep(), _ => ApplySelected(), SquareIcon("copy", "Copia a posição do primeiro objeto (cole na aba Alinhar)", () => CopyTransform(0))));
        page.AddChild(TripleRow("Rotação", _rotationFields, RotationStep, _ => ApplySelected(), SquareIcon("reset", "Zera a rotação", () => { foreach (var field in _rotationFields) field.Text = Disp(0); ApplySelected(); })));
        _sizeLock = SquareIcon("link", "Mantém as proporções ao mudar um lado", () => { }, toggle: true);
        page.AddChild(TripleRow("Tamanho", _size, () => GridStep(), SizeCommitted, _sizeLock));
        var stepSlider = NewSlider(.05, 5, .05, .5); stepSlider.ValueChanged += value => { if (!_filling) _snap.Text = Disp(value); };
        _snap.TextChanged += _ => { var value = Mathf.Clamp(NumOr(_snap, .5f), .05f, 5f); if (!Mathf.IsEqualApprox((float)stepSlider.Value, value)) stepSlider.SetValueNoSignal(value); UpdateGrid(); };
        page.AddChild(FormRow("Passo de movimento:", _snap, 230));
        _showGrid = Check("Exibir grade"); _showGrid.Toggled += _ => UpdateGrid();
        _gridSnap = Check("Encaixar na grade", "A posição final cai em múltiplos do passo de movimento");
        _rotSnap = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, TooltipText = "Quanto gira a cada passo ao segurar Ctrl" }; foreach (var step in RotationSteps) _rotSnap.AddItem($"{step}°"); _rotSnap.Select(1);
        var gridRow = Row(14); gridRow.AddChild(_showGrid); gridRow.AddChild(_gridSnap); page.AddChild(gridRow);
        page.AddChild(FormRow("Passo de giro:", _rotSnap, 230));
        page.AddChild(Rule());

        // the shape
        page.AddChild(Heading("Primitiva"));
        _primitive = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, TooltipText = "O tipo do objeto selecionado; também é a forma criada pelo modo Criar" };
        foreach (var (id, label) in Primitives) { _primitive.AddItem(label); _primitive.SetItemMetadata(_primitive.ItemCount - 1, id); }
        _primitive.ItemSelected += _ => { if (!_filling) ChangePrimitiveType(); };
        page.AddChild(FormRow("Primitiva:", _primitive, 170));
        page.AddChild(Heading("Parâmetros da forma"));
        _hollow = NewSlider(0, .95, .01, 0); _taperX = NewSlider(-1, 1, .01, 0); _taperY = NewSlider(-1, 1, .01, 0); _twist = NewSlider(-180, 180, 1, 0);
        EnsureShapeSliders();
        var s = _shapeSliders;
        page.AddChild(PairRow("Corte do caminho", ("Início", SpinFor(s["pathBegin"])), ("Fim", SpinFor(s["pathEnd"]))));
        page.AddChild(PairRow("Oco (Hollow)", ("", SpinFor(_hollow))));
        page.AddChild(PairRow("Tamanho do furo", ("X", SpinFor(s["holeX"])), ("Y", SpinFor(s["holeY"]))));
        page.AddChild(PairRow("Torção (Twist)", ("Fim", SpinFor(_twist))));
        page.AddChild(PairRow("Afinar (Taper)", ("X", SpinFor(_taperX)), ("Y", SpinFor(_taperY))));
        page.AddChild(PairRow("Cisalhamento do topo", ("X", SpinFor(s["shearX"])), ("Y", SpinFor(s["shearZ"]))));
        page.AddChild(PairRow("Dimple", ("Início", SpinFor(s["pathBegin"])), ("Fim", SpinFor(s["pathEnd"]))));
        page.AddChild(PairRow("Corte do perfil", ("Início", SpinFor(s["cutStart"])), ("Fim", SpinFor(s["cutEnd"]))));
        page.AddChild(PairRow("Raio e espiral", ("Desloc.", SpinFor(s["radiusOffset"])), ("Inclin.", SpinFor(s["skew"]))));
        page.AddChild(PairRow("Revoluções", ("", SpinFor(s["revolutions"]))));
        page.AddChild(Txt("Cada campo muda a malha de verdade; os que a forma escolhida não usa ficam desabilitados.", 16, false, UiStyle.TextMuted));
        page.AddChild(Rule());

        var footer = Row(10);
        footer.AddChild(Txt("Selecionado:", 19)); _selectedCount = Txt("nenhum", 19, true, UiStyle.AccentText); footer.AddChild(_selectedCount);
        footer.AddChild(Filler()); _primsLabel = Txt("Prims: —", 19); footer.AddChild(_primsLabel);
        page.AddChild(footer);
        var buttons = Row(10);
        var restore = OrangeButton("undo", "Restaurar", "Volta os campos ao que o servidor confirmou", () => { FillFields(); _status.Text = "Valores restaurados."; }, 44); restore.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; buttons.AddChild(restore);
        var apply = IconButton("check", "Aplicar à seleção", "Envia posição, rotação, tamanho, forma e física ao servidor", ApplySelected, UiStyle.ButtonKind.Primary, 44); apply.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; buttons.AddChild(apply);
        page.AddChild(buttons);
        return page;
    }

    private HBoxContainer TripleRow(string label, LineEdit[] fields, Func<float> step, Action<int> committed, Button trailing)
    {
        var row = Row(6); var name = Txt(label, 19); name.CustomMinimumSize = new Vector2(100, 0); name.VerticalAlignment = VerticalAlignment.Center; row.AddChild(name);
        for (var i = 0; i < 3; i++) row.AddChild(AxisField(fields, i, step, committed));
        row.AddChild(trailing); return row;
    }

    /// Changing the primitive of the selected objects to the one chosen in the list.
    private void ChangePrimitiveType()
    {
        if (_primitive.Selected < 0) return;
        var type = _primitive.GetItemMetadata(_primitive.Selected).AsString();
        if (_placeType is not null) { DisarmPlacement(null); }
        var afters = new List<BuildObjectState>();
        foreach (var view in _selection) { if (view.State.PrimitiveType.Equals(type, StringComparison.OrdinalIgnoreCase) || view.State.PrimitiveType == "mesh") continue; var after = FromView(view); after.PrimitiveType = type; afters.Add(after); }
        if (afters.Count == 0) return;
        CommitUpdates(afters); _status.Text = $"Forma trocada para {PrimitiveLabel(type)}.";
    }

    public void SetPrimitiveForTesting(string type) { for (var i = 0; i < _primitive.ItemCount; i++) if (_primitive.GetItemMetadata(i).AsString() == type) { _primitive.Select(i); ChangePrimitiveType(); } }
}
