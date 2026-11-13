using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.Notifications;

/// <summary>
/// The bell of the top bar, with the number of unread notifications on it, and the panel that opens below it (Todas, Grupos, Convites, Sistema). "Ver todas" opens the full center in a window.
/// The counter follows the server: it is asked for every minute, when the panel opens, and when a live event arrives.
/// </summary>
public partial class NotificationCenterUi : Node
{
    private const int PanelWidth = 360, MaxPanelHeight = 520, TopBarHeight = 38;

    private NotificationsApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private Button _bell = null!;
    private PanelContainer _badge = null!;
    private Label _badgeText = null!;
    private Control _overlay = null!, _catcher = null!;
    private PanelContainer _dropdown = null!;
    private CenterView _view = null!;
    private FloatingWindow? _window;
    private CenterView? _windowView;
    private double _pollIn = 4;
    private int _count;

    /// <summary>A sentence for the main UI to show as a toast (an invitation answered, an error).</summary>
    public Action<string>? Said { get; set; }
    /// <summary>The person clicked a notification that belongs to a place: open it (a group, the properties).</summary>
    public Action<CenterEntry>? Open { get; set; }
    /// <summary>Seconds between asking the server for the counters.</summary>
    public double PollEvery { get; set; } = 60;

    public int Count => _count;
    public bool PanelOpen => _dropdown.Visible;
    public bool WindowOpen => _window is not null && _window.Visible;
    public CenterView View => _view;
    public CenterView? WindowView => _windowView;
    public string BadgeTextForTesting => _badge.Visible ? _badgeText.Text : "";

    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new NotificationsApi(new IslandManagementApi(transport)); }

    private NotificationsApi Api => _api ??= new NotificationsApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));

    /// <summary>Puts the bell at the end of <paramref name="row"/> (the top bar) and the panel in <paramref name="overlay"/> (the full-screen layer above the windows).</summary>
    public void AttachTo(HBoxContainer row, Control overlay)
    {
        _overlay = overlay;
        _bell = new Button { Icon = UiStyle.Icon("bell", UiStyle.Text, 20), TooltipText = "Notificações", CustomMinimumSize = new Vector2(34, 30), FocusMode = Control.FocusModeEnum.None, Flat = true, ExpandIcon = false, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _bell.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _bell.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _bell.AddThemeStyleboxOverride("hover", UiStyle.Box(new Color(1, 1, 1, .08f), new Color(0, 0, 0, 0), 15, 0, 0, 0)); _bell.AddThemeStyleboxOverride("pressed", UiStyle.Box(new Color(UiStyle.AccentFill, .6f), new Color(0, 0, 0, 0), 15, 0, 0, 0));
        _bell.Pressed += TogglePanel;
        _badge = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(16, 16) };
        _badge.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Danger, new Color(UiStyle.PanelFillSolid, 1f), 9, 1, 4, 0));
        _badgeText = new Label { Text = "0", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore }; _badgeText.AddThemeFontSizeOverride("font_size", 10); _badgeText.AddThemeColorOverride("font_color", Colors.White);
        _badge.AddChild(_badgeText); _bell.AddChild(_badge); _badge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight); _badge.OffsetLeft = -14; _badge.OffsetTop = -1; _badge.OffsetRight = 4; _badge.OffsetBottom = 15; _badge.GrowHorizontal = Control.GrowDirection.Begin;
        row.AddChild(_bell);

        _catcher = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop }; _catcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _catcher.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true }) ClosePanel(); };
        overlay.AddChild(_catcher);
        BuildPanel(); overlay.AddChild(_dropdown);
    }

    private void BuildPanel()
    {
        _dropdown = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _dropdown.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.PanelFill, .97f), UiStyle.BorderBright, 14, 1, 14, 12, 14));
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 8); _dropdown.AddChild(column);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); column.AddChild(head);
        head.AddChild(UiStyle.IconRect("bell", UiStyle.Accent, 20));
        var title = new Label { Text = "Notificações", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; title.AddThemeFontSizeOverride("font_size", 16); title.AddThemeFontOverride("font", UiStyle.Medium); head.AddChild(title);
        var close = new Button { Icon = UiStyle.Icon("close", UiStyle.Text, 18), Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Fechar", CustomMinimumSize = new Vector2(30, 30) }; close.Pressed += ClosePanel; head.AddChild(close);
        _view = new CenterView(false) { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _view.Bind(Api); _view.Loaded += page => ShowCount(page.Unread); _view.Said = text => Said?.Invoke(text); _view.Open = entry => { ClosePanel(); Open?.Invoke(entry); };
        column.AddChild(_view);
        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", 8); column.AddChild(foot);
        var all = new Button { Text = "Marcar todas como lidas", Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; all.AddThemeFontSizeOverride("font_size", 12); all.AddThemeColorOverride("font_color", UiStyle.TextMuted); all.Pressed += () => _ = _view.MarkAllReadAsync(); foot.AddChild(all);
        var more = new Button { Text = "Ver todas", Flat = true, FocusMode = Control.FocusModeEnum.None }; more.AddThemeFontSizeOverride("font_size", 12); more.AddThemeColorOverride("font_color", UiStyle.Accent); more.Pressed += () => { ClosePanel(); OpenWindow(); }; foot.AddChild(more);
        var configure = new Button { Text = "Configurar", Flat = true, FocusMode = Control.FocusModeEnum.None }; configure.AddThemeFontSizeOverride("font_size", 12); configure.AddThemeColorOverride("font_color", UiStyle.Accent); configure.Pressed += () => { ClosePanel(); OpenSettings(); }; foot.AddChild(configure);
    }

    public ConfirmationDialog? SettingsForTesting { get; private set; }

    /// <summary>Which kinds of notification may pop up on the screen (the bell keeps all of them).</summary>
    public void OpenSettings()
    {
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 8);
        var hint = new Label { Text = "Escolha o que aparece como aviso na tela. Tudo continua no sino.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) }; hint.AddThemeFontSizeOverride("font_size", 12); hint.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(hint);
        foreach (var (id, text) in NotificationPrefs.Categories) { var category = id; var box = new CheckBox { Text = text, ButtonPressed = NotificationPrefs.ToastsOn(id), FocusMode = Control.FocusModeEnum.None, Name = id }; box.Toggled += on => NotificationPrefs.SetToasts(category, on); content.AddChild(box); }
        var dialog = new ConfirmationDialog { Title = "Notificações", OkButtonText = "Pronto", Exclusive = true, Theme = ViewerTheme.Create() }; dialog.GetCancelButton().Visible = false;
        dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); AddChild(dialog); SettingsForTesting = dialog; dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree(); IslandUi.Fit(dialog, 400);
    }

    private void Layout()
    {
        var view = _overlay.GetViewport().GetVisibleRect().Size; var top = TopBarHeight + 8; var height = Math.Min(MaxPanelHeight, Math.Max(220, (int)view.Y - top - 16));
        var width = Math.Min(PanelWidth, Math.Max(260, (int)view.X - 32));
        _dropdown.AnchorLeft = _dropdown.AnchorRight = 1; _dropdown.AnchorTop = _dropdown.AnchorBottom = 0;
        _dropdown.OffsetRight = -16; _dropdown.OffsetLeft = -16 - width; _dropdown.OffsetTop = top; _dropdown.OffsetBottom = top + height;
    }

    public void TogglePanel() { if (_dropdown.Visible) ClosePanel(); else OpenPanel(); }

    public void OpenPanel() { Layout(); _catcher.Visible = true; _dropdown.Visible = true; _dropdown.MoveToFront(); _ = _view.ReloadAsync(); }
    public void ClosePanel() { _dropdown.Visible = false; _catcher.Visible = false; }

    /// <summary>The full center: every notification with search, the unread filter and paging.</summary>
    public void OpenWindow()
    {
        if (_window is null)
        {
            _window = new FloatingWindow("Notificações", icon: "bell", minimizable: false) { Visible = false }; _window.Place(0.5f, 0.5f, -300, -270, 300, 270); _window.MakeResizable();
            _windowView = new CenterView(true) { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _windowView.Bind(Api); _windowView.Loaded += page => ShowCount(page.Unread); _windowView.Said = text => Said?.Invoke(text); _windowView.Open = entry => Open?.Invoke(entry);
            _window.Content.AddChild(_windowView); var all = new Button { Text = "Marcar todas como lidas", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34) }; all.Pressed += () => _ = _windowView.MarkAllReadAsync(); _window.Content.AddChild(all);
            _window.CloseRequested += () => _window.Visible = false; _overlay.AddChild(_window);
        }
        _window.Visible = true; _window.MoveToFront(); _ = _windowView!.ReloadAsync();
    }

    private void ShowCount(int count)
    {
        _count = Math.Max(0, count); _badge.Visible = _count > 0; _badgeText.Text = _count > 99 ? "99+" : _count.ToString();
        _bell.Icon = UiStyle.Icon("bell", _count > 0 ? UiStyle.Text : UiStyle.TextMuted, 20); _bell.TooltipText = _count > 0 ? $"Notificações ({(_count > 99 ? "99+" : _count.ToString())} não lidas)" : "Notificações";
    }

    /// <summary>Asks the server for the counters only (one small request).</summary>
    public async Task RefreshCountAsync()
    {
        var reply = await Api.List("todas", false, null, 0, 1);
        if (reply.Ok && CenterPage.Parse(reply.Body) is { } page) { ShowCount(page.Unread); if (_dropdown.Visible) _ = _view.ReloadAsync(); if (WindowOpen) _ = _windowView!.ReloadAsync(); }
    }

    /// <summary>A live event arrived: look again (the panel, if open, shows the new line).</summary>
    public void Poke() => _ = RefreshCountAsync();

    public override void _Process(double delta)
    {
        if (_bell is null) return;
        _pollIn -= delta; if (_pollIn > 0) return; _pollIn = PollEvery;
        if (_transport is null && GetNodeOrNull<SessionManager>("/root/SessionManager") is not { IsAuthenticated: true }) return; // nobody is signed in
        _ = RefreshCountAsync();
    }

    public override void _UnhandledInput(InputEvent input) { if (_dropdown is not null && _dropdown.Visible && input.IsActionPressed("ui_cancel")) { ClosePanel(); GetViewport().SetInputAsHandled(); } }
}
