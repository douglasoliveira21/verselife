using Godot;
using LifeVerse.Shared.Loading;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The full-screen loading shown when entering the world and when travelling to another island. It only shows what a <see cref="LoadingManager"/> reports:
/// the title, the destination, one line per step (waiting, running, done, failed), the bar with the real percentage (or a moving segment while nothing can be
/// measured), a status line and a rotating tip. A failure shows what happened and only the actions that can really be taken. Built in code like the other
/// windows of the game, with the colours of the LifeVerse palette.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    private static readonly Color PanelFill = new("#1B262E"), TitleFill = new("#1E2A37"), Edge = new("#6D818D");
    private static readonly string[] Tips =
    [
        "Você pode personalizar seu avatar pelo inventário.", "Explore novas ilhas utilizando o mapa.", "Construa objetos em terrenos onde possui permissão.",
        "Adicione outros jogadores à sua lista de amigos.", "Utilize o chat local para conversar com pessoas próximas.", "Salve suas ilhas favoritas para acessá-las rapidamente."
    ];

    private readonly Dictionary<string, (TextureRect Icon, Label Name)> _rows = [];
    private LoadingManager? _manager;
    private Control _root = null!, _panel = null!;
    private Label _title = null!, _subtitle = null!, _islandName = null!, _islandInfo = null!, _status = null!, _percent = null!, _tip = null!, _errorTitle = null!, _errorDetail = null!, _welcome = null!;
    private VBoxContainer _stepList = null!, _errorBox = null!;
    private Button _retry = null!, _back = null!, _leave = null!, _cancel = null!;
    private TextureRect _preview = null!, _backdrop = null!;
    private Label _islandState = null!;
    private BarLine _bar = null!;
    private double _shown, _tipClock, _spin;
    private int _tipIndex;
    private Tween? _fade;

    public event Action? RetryRequested, BackRequested, LeaveRequested, CancelRequested;

    public bool IsOpen => _root is not null && _root.Visible;
    public double DisplayedProgress => _shown;
    public string StatusForTesting => _status.Text;
    public string TitleForTesting => _title.Text;
    public string ErrorForTesting => _errorBox.Visible ? _errorTitle.Text + "|" + _errorDetail.Text : string.Empty;
    public bool RetryVisibleForTesting => _retry.Visible;
    public bool BackVisibleForTesting => _back.Visible;
    public bool BarIndeterminateForTesting => _bar.Indeterminate;
    public string IslandNameForTesting => _islandName.Text;
    public int RowCountForTesting => _rows.Count;
    public string TipForTesting => _tip.Text;
    public float RootAlphaForTesting => _root.Modulate.A;

    public override void _Ready()
    {
        Layer = 100; Name = "LoadingScreen";
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false }; _root.SetAnchorsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        var background = new ColorRect { Color = new Color("#0D141A") }; background.SetAnchorsPreset(Control.LayoutPreset.FullRect); _root.AddChild(background);
        _backdrop = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore }; _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect); _root.AddChild(_backdrop);
        var dim = new ColorRect { Color = new Color("#0D141A", .62f), MouseFilter = Control.MouseFilterEnum.Ignore }; dim.SetAnchorsPreset(Control.LayoutPreset.FullRect); _root.AddChild(dim);
        var glow = new ColorRect { Color = new Color(UiStyle.Accent, .05f) }; glow.SetAnchorsPreset(Control.LayoutPreset.FullRect); glow.AnchorTop = .55f; _root.AddChild(glow);

        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; column.SetAnchorsPreset(Control.LayoutPreset.FullRect); column.AddThemeConstantOverride("separation", 18);
        column.OffsetLeft = 24; column.OffsetRight = -24; column.OffsetTop = 24; column.OffsetBottom = -24; _root.AddChild(column);

        var brand = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; brand.AddThemeConstantOverride("separation", 2); column.AddChild(brand);
        if (GD.Load<Texture2D>("res://Assets/brand/logo.png") is { } logo) brand.AddChild(new TextureRect { Texture = logo, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(0, 96) });
        brand.AddChild(Text("LifeVerse", 40, true, UiStyle.Text, HorizontalAlignment.Center));
        brand.AddChild(Text("MUNDO VIRTUAL 3D", 15, true, UiStyle.Accent, HorizontalAlignment.Center));

        var center = new CenterContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; column.AddChild(center);
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        _panel.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(PanelFill, .94f), Edge, 16, 1, 0, 0)); center.AddChild(_panel);
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 0); _panel.AddChild(stack);

        var bar = new PanelContainer(); bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = TitleFill, CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16, ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 12, ContentMarginBottom = 12 }); stack.AddChild(bar);
        var heading = new VBoxContainer(); heading.AddThemeConstantOverride("separation", 0); bar.AddChild(heading);
        _title = Text("Entrando no LifeVerse...", 22, true, UiStyle.Text); heading.AddChild(_title);
        _subtitle = Text(string.Empty, 15, false, UiStyle.TextMuted); heading.AddChild(_subtitle);

        var body = new MarginContainer(); foreach (var side in new[] { "left", "right", "top", "bottom" }) body.AddThemeConstantOverride("margin_" + side, 22); stack.AddChild(body);
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 16); body.AddChild(inner);

        var split = new HBoxContainer(); split.AddThemeConstantOverride("separation", 22); inner.AddChild(split);
        var island = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) }; island.AddThemeConstantOverride("separation", 6); split.AddChild(island);
        _preview = new TextureRect { CustomMinimumSize = new Vector2(230, 140), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true, Texture = GD.Load<Texture2D>("res://Assets/brand/logo.png") };
        var previewFrame = new PanelContainer(); previewFrame.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#121A20"), Edge, 12, 1, 4, 4)); previewFrame.AddChild(_preview); island.AddChild(previewFrame);
        _islandName = Text("Ilha", 19, true, UiStyle.Text); _islandName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; island.AddChild(_islandName);
        _islandInfo = Text(string.Empty, 15, false, UiStyle.TextMuted); _islandInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart; island.AddChild(_islandInfo);
        _islandState = Text(string.Empty, 15, true, UiStyle.Accent); _islandState.AutowrapMode = TextServer.AutowrapMode.WordSmart; island.AddChild(_islandState);

        _stepList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _stepList.AddThemeConstantOverride("separation", 7); split.AddChild(_stepList);

        var progress = new HBoxContainer(); progress.AddThemeConstantOverride("separation", 14); inner.AddChild(progress);
        _bar = new BarLine { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; progress.AddChild(_bar);
        _percent = Text("0%", 18, true, UiStyle.Accent, HorizontalAlignment.Right); _percent.CustomMinimumSize = new Vector2(58, 0); progress.AddChild(_percent);
        _status = Text(string.Empty, 16, false, UiStyle.TextMuted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; inner.AddChild(_status);
        _welcome = Text(string.Empty, 20, true, UiStyle.Accent, HorizontalAlignment.Center); _welcome.Visible = false; inner.AddChild(_welcome);
        _cancel = ActionButton("Cancelar", UiStyle.ButtonKind.Secondary, () => CancelRequested?.Invoke()); _cancel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; inner.AddChild(_cancel);

        _errorBox = new VBoxContainer { Visible = false }; _errorBox.AddThemeConstantOverride("separation", 8); inner.AddChild(_errorBox);
        _errorTitle = Text(string.Empty, 18, true, UiStyle.DangerBright); _errorTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart; _errorBox.AddChild(_errorTitle);
        _errorDetail = Text(string.Empty, 15, false, UiStyle.TextMuted); _errorDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart; _errorBox.AddChild(_errorDetail);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 10); _errorBox.AddChild(buttons);
        _retry = ActionButton("Tentar novamente", UiStyle.ButtonKind.Primary, () => RetryRequested?.Invoke()); _back = ActionButton("Voltar à ilha anterior", UiStyle.ButtonKind.Secondary, () => BackRequested?.Invoke()); _leave = ActionButton("Voltar ao menu principal", UiStyle.ButtonKind.Secondary, () => LeaveRequested?.Invoke());
        foreach (var button in new[] { _retry, _back, _leave }) buttons.AddChild(button);

        _tip = Text(string.Empty, 16, false, UiStyle.TextMuted, HorizontalAlignment.Center); _tip.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(_tip);
        GetViewport().SizeChanged += FitPanel; FitPanel();
        ShowTip(0);
    }

    public override void _ExitTree() { if (GetViewport() is { } viewport) viewport.SizeChanged -= FitPanel; }

    /// <summary>The panel follows the window: wide enough to read on a big screen, never wider than the window on a small one.</summary>
    private void FitPanel() { if (_panel is null) return; _panel.CustomMinimumSize = new Vector2(PanelWidthFor(GetViewport().GetVisibleRect().Size.X), 0); }

    public static float PanelWidthFor(float windowWidth) => Mathf.Clamp(windowWidth - 80, 360, 820);
    public float PanelWidthForTesting => _panel.CustomMinimumSize.X;
    public void FitPanelForTesting() => FitPanel();

    private static Label Text(string text, int size, bool bold, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontOverride("font", bold ? UiStyle.Bold : UiStyle.Regular); label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button ActionButton(string text, UiStyle.ButtonKind kind, Action action)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 42), Visible = false };
        UiStyle.StyleButton(button, kind, 16, 6, 14); button.AddThemeFontOverride("font", UiStyle.Medium); button.Pressed += action;
        return button;
    }

    // ---- driving the screen ---------------------------------------------------------------------------------

    /// <summary>Shows the screen for a loading that has just started (fading in over the world) and follows the manager from then on.</summary>
    public void Open(LoadingManager manager, string? category = null, Texture2D? preview = null)
    {
        Unbind(); _manager = manager;
        manager.LoadingStarted += OnStarted; manager.LoadingStepChanged += OnStepChanged; manager.LoadingProgressChanged += OnProgress; manager.LoadingFailed += OnFailed;
        _shown = 0; _errorBox.Visible = false; _welcome.Visible = false; _status.Visible = true; _cancel.Visible = false;
        _backdrop.Texture = null; _backdrop.Visible = false; _preview.Texture = GD.Load<Texture2D>("res://Assets/brand/logo.png");
        SetIsland(manager.Destination, category, preview);
        BuildRows(); Refresh();
        _root.Visible = true; Fade(1f, .35f);
    }

    private void Unbind()
    {
        if (_manager is null) return;
        _manager.LoadingStarted -= OnStarted; _manager.LoadingStepChanged -= OnStepChanged; _manager.LoadingProgressChanged -= OnProgress; _manager.LoadingFailed -= OnFailed;
        _manager = null;
    }

    private void OnStarted() { _errorBox.Visible = false; _status.Visible = true; BuildRows(); Refresh(); }
    private void OnStepChanged(LoadingStep step) { UpdateRow(step); Refresh(); }
    private void OnProgress(double value) => Refresh();
    private void OnFailed(LoadingStep step, string error) => Refresh();

    public void SetIsland(string name, string? category, Texture2D? preview = null)
    {
        _islandName.Text = string.IsNullOrWhiteSpace(name) ? "Ilha" : name;
        _islandInfo.Text = string.IsNullOrWhiteSpace(category) ? string.Empty : "Categoria: " + category;
        if (preview is not null) { _preview.Texture = preview; _backdrop.Texture = preview; _backdrop.Visible = true; }
        _subtitle.Text = _manager?.Mode == LoadingMode.Teleport && !string.IsNullOrWhiteSpace(name) ? "Destino: " + name : string.Empty;
    }

    private void BuildRows()
    {
        foreach (var child in _stepList.GetChildren()) child.QueueFree();
        _rows.Clear();
        if (_manager is null) return;
        foreach (var step in _manager.Steps)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
            var icon = new TextureRect { CustomMinimumSize = new Vector2(22, 22), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, PivotOffset = new Vector2(11, 11) };
            var label = Text(step.Name, 16, false, UiStyle.TextMuted);
            row.AddChild(icon); row.AddChild(label); _stepList.AddChild(row); _rows[step.Id] = (icon, label);
            UpdateRow(step);
        }
    }

    private void UpdateRow(LoadingStep step)
    {
        if (!_rows.TryGetValue(step.Id, out var row)) return;
        var (icon, color, bold) = step.State switch
        {
            StepState.Completed => ("check", UiStyle.SuccessBright, false),
            StepState.Running => ("rotate", UiStyle.Accent, true),
            StepState.Failed => ("alert", UiStyle.DangerBright, true),
            StepState.Cancelled => ("dots", UiStyle.TextDisabled, false),
            _ => ("dots", UiStyle.TextDisabled, false)
        };
        row.Icon.Texture = UiStyle.Icon(icon, color, 22); row.Icon.Rotation = 0;
        row.Name.Text = step.Name; row.Name.AddThemeColorOverride("font_color", step.State == StepState.Pending ? UiStyle.TextMuted : step.State == StepState.Completed ? UiStyle.Text : color);
        row.Name.AddThemeFontOverride("font", bold ? UiStyle.Bold : UiStyle.Regular);
    }

    private void Refresh()
    {
        if (_manager is null) return;
        if (_manager.Current is { } current && !_manager.Failed)
        {
            var measured = current.State == StepState.Running && current.Total is > 0 ? $"  ({current.Done}/{current.Total})" : string.Empty;
            _status.Text = current.Name.TrimEnd('.') + measured + (current.State == StepState.Running ? "…" : string.Empty);
        }
        _bar.Indeterminate = _manager.Indeterminate && !_manager.Finished;
        _islandState.Text = _manager.Failed ? "Estado: falha na conexão" : _manager.Current is { } now ? "Estado: " + now.Name.TrimEnd('.') : "Estado: pronto";
    }

    public override void _Process(double delta)
    {
        if (_root is null || !_root.Visible) return;
        _spin += delta * 360;
        foreach (var (id, row) in _rows) if (_manager?.Step(id)?.State == StepState.Running) row.Icon.Rotation = Mathf.DegToRad((float)(_spin % 360));
        if (_manager is not null)
        {
            var target = _manager.Progress;
            // the bar eases towards the real progress and never passes it
            _shown = Math.Min(target, _shown + (target - _shown) * (1 - Math.Exp(-9 * delta)));
            if (_manager.Succeeded && target - _shown < .002) _shown = 1;
            _bar.Value = (float)_shown; _percent.Text = $"{(int)Math.Floor(_shown * 100 + .0001)}%";
        }
        _bar.Phase += (float)delta;
        _tipClock += delta; if (_tipClock >= 7) { _tipClock = 0; ShowTip(_tipIndex + 1); }
    }

    private void ShowTip(int index) { _tipIndex = ((index % Tips.Length) + Tips.Length) % Tips.Length; _tip.Text = "Dica: " + Tips[_tipIndex]; }

    private void Fade(float alpha, double seconds, Action? done = null)
    {
        _fade?.Kill();
        if (!IsInsideTree()) { _root.Modulate = new Color(1, 1, 1, alpha); done?.Invoke(); return; }
        _fade = CreateTween(); _fade.TweenProperty(_root, "modulate:a", alpha, seconds).SetEase(Tween.EaseType.Out);
        if (done is not null) _fade.TweenCallback(Callable.From(done));
    }

    /// <summary>The world is ready: the bar is full, the welcome shows and the screen fades away. <paramref name="done"/> runs when the world is visible and the controls can be given back.</summary>
    public void Finish(string welcome, Action done)
    {
        _welcome.Text = welcome; _welcome.Visible = true; _status.Visible = false; _errorBox.Visible = false; _cancel.Visible = false;
        _shown = 1; _bar.Value = 1; _percent.Text = "100%"; _bar.Indeterminate = false;
        Fade(0f, .5, () => { _root.Visible = false; Unbind(); done(); });
    }

    /// <summary>A step failed: says what happened and offers only the actions that can really be taken now.</summary>
    public void ShowError(string title, string detail, bool canRetry, bool canGoBack, bool canLeave = true)
    {
        _errorTitle.Text = title; _errorDetail.Text = detail; _errorBox.Visible = true; _status.Visible = false;
        _retry.Visible = canRetry; _back.Visible = canGoBack; _leave.Visible = canLeave; _bar.Indeterminate = false; _cancel.Visible = false;
        _root.Visible = true; Fade(1f, .2);
    }

    public void SetTitle(string title) => _title.Text = title;

    /// <summary>Cancel is offered while nothing has left the current island yet (a trip still being prepared).</summary>
    public void SetCancelVisible(bool visible) => _cancel.Visible = visible;
    public bool CancelVisibleForTesting => _cancel.Visible;
    public void PressCancelForTesting() => CancelRequested?.Invoke();
    public void PressBackForTesting() => BackRequested?.Invoke();

    public void HideNow() { _fade?.Kill(); _root.Visible = false; _root.Modulate = Colors.White; Unbind(); }

    /// <summary>The moving or filling bar: a rounded track, a cyan fill, and a travelling segment while the work cannot be measured.</summary>
    private partial class BarLine : Control
    {
        public float Value { get; set; }
        public bool Indeterminate { get; set; }
        public float Phase { get; set; }

        public override void _Process(double delta) => QueueRedraw();

        public override void _Draw()
        {
            var radius = Size.Y / 2f;
            DrawStyleBox(new StyleBoxFlat { BgColor = new Color("#121A20"), BorderColor = new Color("#3A4A55"), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, CornerRadiusBottomLeft = (int)radius, CornerRadiusBottomRight = (int)radius, CornerRadiusTopLeft = (int)radius, CornerRadiusTopRight = (int)radius }, new Rect2(Vector2.Zero, Size));
            var fill = new StyleBoxFlat { BgColor = UiStyle.Accent, CornerRadiusBottomLeft = (int)radius, CornerRadiusBottomRight = (int)radius, CornerRadiusTopLeft = (int)radius, CornerRadiusTopRight = (int)radius };
            if (Indeterminate)
            {
                var width = Size.X * .28f; var travel = Size.X - width; var t = (Mathf.Sin(Phase * 2.2f) + 1f) / 2f;
                DrawStyleBox(fill, new Rect2(travel * t, 0, width, Size.Y));
            }
            else if (Value > 0) DrawStyleBox(fill, new Rect2(0, 0, Mathf.Max(Size.Y, Size.X * Mathf.Clamp(Value, 0f, 1f)), Size.Y));
        }
    }
}
