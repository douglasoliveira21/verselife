using Godot;

namespace LifeVerse.Client.Scripts;

/// The five modes of the Construir window (Foco, Mover, Editar, Criar, Terreno). Each mode shows its own options panel; only Editar shows the
/// property tabs. The panels reuse the real controls of the tabs (two-way mirrors), so nothing here is decorative.
public enum BuildMode { Focus, Move, Edit, Create, Land }

public partial class BuildEditor
{
    private BuildMode _buildMode = BuildMode.Edit;
    private readonly Button[] _modeButtons = new Button[5];
    private readonly Dictionary<BuildMode, Control> _modePanels = [];
    private Control _stack = null!;
    private Action<bool> _showEditPages = _ => { };
    private Label _modeStatus = null!;
    private readonly HashSet<Control> _lockedByReadOnly = [];
    private bool _readOnly;

    public BuildMode CurrentBuildMode => _buildMode;
    public bool ReadOnlyForTesting => _readOnly;
    public void SetBuildModeForTesting(BuildMode mode) => SetBuildMode(mode);
    public string ModeStatusForTesting => _modeStatus.Text;
    public bool EditAreaVisibleForTesting => _stack.Visible && _tabs.Visible;

    private static readonly (BuildMode Mode, string Icon, string Text, string Tip)[] ModeDefinitions =
    [
        (BuildMode.Focus, "focus", "Foco", "Câmera: foca na seleção e orbita em volta dela"),
        (BuildMode.Move, "move", "Mover", "Move, gira e estica objetos com os gizmos"),
        (BuildMode.Edit, "edit", "Editar", "Edita identificação, forma, recursos, textura e conteúdo"),
        (BuildMode.Create, "cube", "Criar", "Escolhe uma primitiva e clica no mundo para criá-la"),
        (BuildMode.Land, "terrain", "Terreno", "Ferramentas de terreno")
    ];

    private Control BuildModeBar()
    {
        var row = Row(6);
        foreach (var (mode, icon, text, tip) in ModeDefinitions)
        {
            var captured = mode;
            var button = IconButton(icon, text, tip, () => SetBuildMode(captured), UiStyle.ButtonKind.Tool, 58);
            button.VerticalIconAlignment = VerticalAlignment.Top; button.IconAlignment = HorizontalAlignment.Center;
            button.AddThemeConstantOverride("icon_max_width", 26); button.AddThemeConstantOverride("h_separation", 1);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; button.ToggleMode = true; button.FocusMode = Control.FocusModeEnum.None;
            _modeButtons[(int)mode] = button; row.AddChild(button);
        }
        return row;
    }

    /// The per-mode option panels and the status line, placed right under the mode bar.
    private Control BuildModePanels()
    {
        var host = new VBoxContainer(); host.AddThemeConstantOverride("separation", 8);
        _modePanels[BuildMode.Focus] = ModePanel(host, panel =>
        {
            panel.AddChild(Txt("Controles de câmera (Foco):", 19, true));
            foreach (var (icon, text, keys, action) in new (string, string, string, Action?)[]
            {
                ("focus", "Focar na seleção", "Home", FocusSelection), ("rotate", "Orbitar", "botão direito + arrastar", null), ("expand", "Aproximar / Afastar", "roda do mouse", null),
                ("move", "Deslocar (pan)", "botão do meio + arrastar", null), ("camera", "Câmera de edição", "End", ToggleEditCamera)
            })
            {
                var row = Row(10);
                if (action is null) { row.AddChild(UiStyle.IconRect(icon, UiStyle.TextMuted, 24)); var name = Txt(text, 19); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(name); } // mouse gestures: information only, there is nothing to click
                else { var button = ModeAction(icon, text, keys, action); button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(button); }
                var shortcut = Txt(keys, 17, false, UiStyle.TextMuted); shortcut.HorizontalAlignment = HorizontalAlignment.Right; shortcut.CustomMinimumSize = new Vector2(230, 0); row.AddChild(shortcut); panel.AddChild(row);
            }
        });
        _modePanels[BuildMode.Move] = ModePanel(host, panel =>
        {
            panel.AddChild(Txt("Mover objetos selecionados.", 19));
            var group = new ButtonGroup();
            foreach (var (text, action) in new (string, Action)[] { ("Mover", () => SetMode(GizmoMode.Move)), ("Levantar / Abaixar (Ctrl)", () => { SetMode(GizmoMode.Move); _status.Text = "Levantar e abaixar: arraste a seta verde."; }), ("Rotacionar (Ctrl+Shift)", () => SetMode(GizmoMode.Rotate)) })
            {
                var radio = new CheckBox { Text = " " + text, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None }; radio.AddThemeFontSizeOverride("font_size", 19); var run = action; radio.Pressed += () => run(); panel.AddChild(radio);
            }
            panel.AddChild(Mirror(_showGrid, " Usar grade")); panel.AddChild(Mirror(_gridSnap, " Snap na grade"));
            var precision = new LineEdit { Text = _snap.Text, CustomMinimumSize = new Vector2(120, 38) }; precision.AddThemeFontSizeOverride("font_size", 19);
            precision.TextSubmitted += value => { _snap.Text = value; _snap.EmitSignal(LineEdit.SignalName.TextChanged, value); }; _snap.TextChanged += value => { if (precision.Text != value) precision.Text = value; };
            var precisionRow = Row(10); precisionRow.AddChild(Txt("Precisão da grade:", 19)); precisionRow.AddChild(Filler()); precisionRow.AddChild(precision); precisionRow.AddChild(Txt("m", 18, false, UiStyle.TextMuted)); panel.AddChild(precisionRow);
            var turn = new OptionButton { CustomMinimumSize = new Vector2(120, 38), FocusMode = Control.FocusModeEnum.None }; foreach (var step in RotationSteps) turn.AddItem($"{step}°"); turn.Select(_rotSnap.Selected);
            turn.ItemSelected += index => _rotSnap.Select((int)index); var turnRow = Row(10); turnRow.AddChild(Txt("Snap rotação:", 19)); turnRow.AddChild(Filler()); turnRow.AddChild(turn); panel.AddChild(turnRow);
        });
        _modePanels[BuildMode.Edit] = ModePanel(host, panel =>
        {
            panel.AddChild(Txt("Arrastar para mover, Shift + arrastar para copiar", 18, false, UiStyle.TextMuted));
            var columns = Row(10); panel.AddChild(columns); var left = Column(2); var right = Column(2); columns.AddChild(left); columns.AddChild(right);
            var group = new ButtonGroup();
            foreach (var (text, action) in new (string, Action)[]
            {
                ("Selecionar", () => { _partMode = false; SelectTool(); }), ("Esticar (Ctrl)", () => { _partMode = false; SetMode(GizmoMode.Scale); }), ("Girar (Ctrl+Shift)", () => { _partMode = false; SetMode(GizmoMode.Rotate); }),
                ("Selecionar face", () => { _partMode = false; FaceTool(); }), ("Editar partes vinculadas", () => { _partMode = true; if (_partCheck is not null) _partCheck.SetPressedNoSignal(true); SelectTool(); })
            })
            {
                var radio = new CheckBox { Text = " " + text, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None, ButtonPressed = text == "Selecionar" }; radio.AddThemeFontSizeOverride("font_size", 18); var run = action; radio.Pressed += () => run(); left.AddChild(radio);
            }
            right.AddChild(Mirror(_stretchBoth, " Alongar ambos os lados")); right.AddChild(Mirror(_stretchTextures, " Alongar texturas"));
            var local = Check("Usar eixos locais", "Os eixos do gizmo seguem o objeto"); local.Toggled += on => { _axesSpace.Select(on ? 1 : 0); RefreshGizmo(); }; right.AddChild(local);
            right.AddChild(Mirror(_showGrid, " Grade"));
            var options = TextButton("Opções…", "Alinhar, conjuntos vinculados, pivôs e duplicação múltipla", () => _showEditPages(!_advancedTabs.Visible)); options.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; right.AddChild(options);
        });
        _modePanels[BuildMode.Create] = ModePanel(host, panel =>
        {
            panel.AddChild(Txt("Selecionar uma primitiva para criar:", 19, true));
            panel.AddChild(Txt("Clique no terreno ou em um objeto. Shift + clique cria várias; Esc cancela.", 16, false, UiStyle.TextMuted));
            var grid = new GridContainer { Columns = 3 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); panel.AddChild(grid);
            for (var i = 0; i < _primitive.ItemCount; i++)
            {
                var index = i; var type = _primitive.GetItemMetadata(i).AsString();
                var button = ModeAction("cube", PrimitiveLabel(type), "Arma a criação de " + PrimitiveLabel(type), () => { DisarmPlacement(null); _primitive.Select(index); CreateObject(); });
                button.Icon = PrimitivePaletteIcon(type, UiStyle.Text, 34); button.VerticalIconAlignment = VerticalAlignment.Top; button.IconAlignment = HorizontalAlignment.Center; button.CustomMinimumSize = new Vector2(0, 86); button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; button.Alignment = HorizontalAlignment.Center;
                grid.AddChild(button);
            }
            panel.AddChild(Mirror(_gridSnap, " Encaixar na grade"));
        });
        _modePanels[BuildMode.Land] = ModePanel(host, panel =>
        {
            panel.AddChild(Txt("Ferramentas de terreno:", 19, true));
            panel.AddChild(Txt("Elevar, abaixar, suavizar, nivelar, reverter e ruído, com tamanho, força e suavidade do pincel, abrem na janela de terreno (histórico próprio).", 16, false, UiStyle.TextMuted));
            panel.AddChild(ModeAction("terrain", "Abrir ferramentas de terreno", "Eleva, abaixa, suaviza, nivela e pinta o terreno", OpenTerrainTools));
        });
        _modeStatus = new Label { Text = "Selecionado: 0 objetos / Prims: 0", VerticalAlignment = VerticalAlignment.Center };
        _modeStatus.AddThemeColorOverride("font_color", UiStyle.TextMuted); _modeStatus.AddThemeFontSizeOverride("font_size", 19);
        host.AddChild(_modeStatus);
        return host;
    }

    private static VBoxContainer ModePanel(VBoxContainer host, Action<VBoxContainer> fill)
    {
        var panel = new VBoxContainer { Visible = false }; panel.AddThemeConstantOverride("separation", 8);
        fill(panel); host.AddChild(panel);
        return panel;
    }

    private Button ModeAction(string icon, string text, string tip, Action action)
    {
        var button = ListAction(icon, text, tip, action);
        button.CustomMinimumSize = new Vector2(0, 46); button.AddThemeFontSizeOverride("font_size", 19);
        return button;
    }

    /// A checkbox that shows and changes the same setting as one of the tabs: changing either one updates the other and the real setting.
    private static CheckBox Mirror(CheckBox source, string text)
    {
        var mirror = new CheckBox { Text = text, ButtonPressed = source.ButtonPressed, TooltipText = source.TooltipText, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        mirror.AddThemeFontSizeOverride("font_size", 19);
        mirror.Toggled += on => { if (source.ButtonPressed != on) source.ButtonPressed = on; };
        source.Toggled += on => { if (GodotObject.IsInstanceValid(mirror)) mirror.SetPressedNoSignal(on); };
        return mirror;
    }

    private void SetBuildMode(BuildMode mode)
    {
        if (mode != BuildMode.Create && _placeType is not null) DisarmPlacement(null);
        if (mode == BuildMode.Land) { _buildMode = mode; ShowModeUi(); OpenTerrainTools(); return; }
        if (mode != BuildMode.Focus) { var terrain = GetParent()?.GetNodeOrNull<TerrainEditor>("TerrainEditor"); terrain?.SetActive(false); }
        _buildMode = mode;
        switch (mode)
        {
            case BuildMode.Focus: CancelDrag(); FocusSelection(); break;
            case BuildMode.Move: _showEditPages(false); SetMode(_mode); break;
            case BuildMode.Edit: _showEditPages(false); if (_selectOnly) SelectTool(); break;
            case BuildMode.Create: CancelDrag(); break;
        }
        ShowModeUi();
    }

    private void ShowModeUi()
    {
        for (var i = 0; i < _modeButtons.Length; i++) _modeButtons[i].SetPressedNoSignal(i == (int)_buildMode);
        foreach (var (mode, panel) in _modePanels) panel.Visible = mode == _buildMode;
        var edit = _buildMode == BuildMode.Edit;
        _stack.Visible = edit; _tabs.Visible = edit; if (!edit) _advancedTabs.Visible = false;
        _applyBarRefresh();
        RefreshModeStatus();
    }

    private void RefreshModeStatus()
    {
        if (_modeStatus is null) return;
        var count = _selection.Count;
        _modeStatus.Text = $"Selecionado: {(count == 1 ? "1 objeto" : $"{count} objetos")} / Prims: {count}";
    }

    /// Objects the person may not change (locked by someone else, or marked edit-locked) can be looked at but every editing control is disabled.
    private void ApplyReadOnly()
    {
        var locked = _selection.Count > 0 && (SelectionLockedByOther(out _) || _selection.Any(v => v.State.EditLocked));
        if (locked == _readOnly) return;
        _readOnly = locked;
        if (locked) DisableEditing(_stack);
        else { foreach (var control in _lockedByReadOnly) if (GodotObject.IsInstanceValid(control)) SetControlEnabled(control, true); _lockedByReadOnly.Clear(); }
        if (locked) _status.Text = "Este objeto está bloqueado ou em edição por outra pessoa: você pode olhar, mas não alterar.";
    }

    private void DisableEditing(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Control control && IsEditable(control) && !IsDisabled(control)) { SetControlEnabled(control, false); _lockedByReadOnly.Add(control); }
            DisableEditing(child);
        }
    }

    private static bool IsEditable(Control control) => control is BaseButton or LineEdit or TextEdit or Slider or SpinBox;
    private static bool IsDisabled(Control control) => control switch { BaseButton b => b.Disabled, LineEdit l => !l.Editable, TextEdit t => !t.Editable, Slider r => !r.Editable, SpinBox sp => !sp.Editable, _ => false };
    private static void SetControlEnabled(Control control, bool enabled)
    {
        switch (control)
        {
            case BaseButton b: b.Disabled = !enabled; break;
            case LineEdit l: l.Editable = enabled; break;
            case TextEdit t: t.Editable = enabled; break;
            case Slider r: r.Editable = enabled; break;
            case SpinBox sp: sp.Editable = enabled; break;
        }
    }
}
