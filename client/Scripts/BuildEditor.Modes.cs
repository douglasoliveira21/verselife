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
            panel.AddChild(Txt("Botão direito orbita, botão do meio desloca e a roda dá zoom. Home foca na seleção.", 18, false, UiStyle.TextMuted));
            var line = Row(8); panel.AddChild(line);
            line.AddChild(ModeAction("focus", "Focar na seleção", "Foca a câmera na seleção (Home)", FocusSelection));
            line.AddChild(ModeAction("camera", "Câmera de edição", "Liga ou desliga a câmera que gira em volta da seleção (End)", ToggleEditCamera));
        });
        _modePanels[BuildMode.Move] = ModePanel(host, panel =>
        {
            var line = Row(8); panel.AddChild(line);
            line.AddChild(ModeAction("move", "Mover", "Arrasta pelos eixos (2)", () => SetMode(GizmoMode.Move)));
            line.AddChild(ModeAction("rotate", "Girar", "Gira em torno do pivô (3)", () => SetMode(GizmoMode.Rotate)));
            line.AddChild(ModeAction("scale", "Esticar", "Estica o objeto (4)", () => SetMode(GizmoMode.Scale)));
            var options = Row(14); panel.AddChild(options);
            options.AddChild(Mirror(_gridSnap, " Encaixar na grade")); options.AddChild(Mirror(_showGrid, " Mostrar grade"));
        });
        _modePanels[BuildMode.Edit] = ModePanel(host, panel =>
        {
            var line = Row(6); panel.AddChild(line);
            line.AddChild(ModeAction("select", "Selecionar", "Só seleciona, sem mostrar gizmos (1)", SelectTool));
            line.AddChild(ModeAction("move", "Mover", "Gizmo de mover (2)", () => SetMode(GizmoMode.Move)));
            line.AddChild(ModeAction("rotate", "Girar", "Gizmo de girar (3)", () => SetMode(GizmoMode.Rotate)));
            line.AddChild(ModeAction("scale", "Esticar", "Gizmo de esticar (4)", () => SetMode(GizmoMode.Scale)));
            var options = Row(14); panel.AddChild(options);
            options.AddChild(Mirror(_partCheck, " Editar partes vinculadas"));
            var stretch = Row(14); panel.AddChild(stretch); stretch.AddChild(Mirror(_stretchBoth, " Esticar dos dois lados")); stretch.AddChild(Mirror(_stretchTextures, " Esticar texturas"));
            options.AddChild(Mirror(_gridSnap, " Encaixar na grade")); options.AddChild(Mirror(_showGrid, " Mostrar grade"));
        });
        _modePanels[BuildMode.Create] = ModePanel(host, panel =>
        {
            panel.AddChild(Txt("Escolha uma forma e clique no terreno ou em um objeto. Shift + clique cria várias; Esc cancela.", 18, false, UiStyle.TextMuted));
            var grid = new GridContainer { Columns = 4 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); panel.AddChild(grid);
            for (var i = 0; i < _primitive.ItemCount; i++)
            {
                var index = i; var type = _primitive.GetItemMetadata(i).AsString();
                var button = ModeAction("cube", PrimitiveLabel(type), "Arma a criação de " + PrimitiveLabel(type), () => { DisarmPlacement(null); _primitive.Select(index); CreateObject(); });
                button.Icon = PrimitivePaletteIcon(type, UiStyle.Text, 26);
                grid.AddChild(button);
            }
            panel.AddChild(Mirror(_gridSnap, " Encaixar na grade"));
        });
        _modePanels[BuildMode.Land] = ModePanel(host, panel =>
        {
            panel.AddChild(Txt("As ferramentas de terreno abrem na própria janela de terreno, com histórico próprio. Voltar aqui devolve o controle ao editor de objetos.", 18, false, UiStyle.TextMuted));
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
