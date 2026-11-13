using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.LoginUi;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The login screen, in three independent layers: the background picture (<see cref="LoginBackground"/>), the login interface (logo and the card with the form, the registration and the recovery notice)
/// and the overlay (window controls, server status, news and version). Signing in goes through the existing <see cref="AuthManager"/>, so the session, the token and the entry into the world are the ones the game already has.
/// </summary>
public partial class LoginScreen : Control
{
    public LoginBackground Background { get; } = new();
    public LoginLightShow Lights { get; } = new();
    public LoginMusic Music { get; } = new();
    public LoginLogo Logo { get; } = new();
    public LoginPanel Panel { get; } = new();
    public ServerStatus Status { get; } = new();
    public NewsTicker News { get; } = new();
    public WindowControls Controls { get; } = new();
    public Label VersionLabel { get; private set; } = null!;
    public Button? DownloadButton { get; private set; }
    /// <summary>Tests only: replaces the change of scene after a successful login or registration (argument: a new account).</summary>
    public Action<bool>? EnterOverride { get; set; }

    private AuthManager _auth = null!;
    private UpdateChecker? _updates;
    private bool _registering;
    private bool UpdateRequired => _updates?.Status == LifeVerse.Shared.Updates.UpdateStatus.Required;

    public override void _Ready()
    {
        Theme = ViewerTheme.Create(); SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // layer 1: the picture
        AddChild(Background);
        Lights.Name = "LightShow"; AddChild(Lights); // light beams and drifting sparks over the picture, under the interface
        Music.Name = "Music"; AddChild(Music);
        // layer 2: the login interface
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Pass }; centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(centre);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; column.AddThemeConstantOverride("separation", 22); centre.AddChild(column);
        column.AddChild(Logo); column.AddChild(Panel);
        // layer 3: the overlay
        BuildOverlay();

        _auth = GetNode<AuthManager>("/root/AuthManager"); _auth.RequestFinished += OnRequestFinished;
        Panel.Form.Submitted += (login, password, remember) => { _remember = remember; _password = password; _login = login; Begin(false, () => _auth.Login(login, password)); };
        Panel.Register.Submitted += (user, email, password) => { _remember = false; _login = user; _password = password; Begin(true, () => _auth.Register(user, email, password)); };
        Status.Start(GetNode<AppManager>("/root/AppManager").ApiBaseUrl);
        _updates = GetNode<AppManager>("/root/AppManager").Updates; _updates.Checked += ShowUpdate; ShowUpdate();
        if (SavedLogin.Load() is { } saved) { Panel.Form.User.Text = saved.Login; Panel.Form.Password.Text = saved.Password; Panel.Form.Remember.ButtonPressed = true; Panel.Form.Enter.GrabFocus(); }
        else Panel.Form.User.Focus();
        Intro();
    }

    private string _login = "", _password = ""; private bool _remember;

    public override void _ExitTree()
    {
        if (IsInstanceValid(_auth)) _auth.RequestFinished -= OnRequestFinished;
        if (_updates is not null && IsInstanceValid(_updates)) _updates.Checked -= ShowUpdate;
    }

    private void BuildOverlay()
    {
        Controls.Music = Music;
        var top = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore }; top.SetAnchorsPreset(LayoutPreset.TopWide); top.AddThemeConstantOverride("margin_top", 24); top.AddThemeConstantOverride("margin_right", 28); AddChild(top);
        var right = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore }; top.AddChild(right); right.AddChild(Controls);

        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(0, 58) }; bar.SetAnchorsPreset(LayoutPreset.BottomWide); bar.GrowVertical = GrowDirection.Begin; AddChild(bar);
        bar.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(.05f, .08f, .1f, .8f), new Color(UiStyle.BorderSoft, .35f), 0, 1, 32, 0));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 20); bar.AddChild(row);
        Status.CustomMinimumSize = new Vector2(250, 0); row.AddChild(Status);
        var middle = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore }; row.AddChild(middle); middle.AddChild(News);
        VersionLabel = new Label { Text = $"Versão {AppManager.Version}  •  Build {Build()}", HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(250, 0) }; VersionLabel.AddThemeFontSizeOverride("font_size", 14); VersionLabel.AddThemeColorOverride("font_color", UiStyle.TextMuted); row.AddChild(VersionLabel);
    }

    /// <summary>The build number: the one stamped into the project by the release script, or "dev" when running from the editor.</summary>
    private static string Build()
    {
        var value = ProjectSettings.GetSetting("application/config/build_number", "").AsString();
        return value.Length > 0 ? value : OS.HasFeature("editor") ? "dev" : "release";
    }

    /// <summary>The logo fades in, then the card rises in. Nothing blocks the input while it plays.</summary>
    private void Intro()
    {
        Logo.Modulate = new Color(1, 1, 1, 0); Panel.Modulate = new Color(1, 1, 1, 0); Panel.Scale = new Vector2(.97f, .97f);
        Panel.Resized += () => Panel.PivotOffset = Panel.Size / 2; Panel.PivotOffset = Panel.Size / 2;
        var tween = CreateTween().SetParallel(); tween.TweenProperty(Logo, "modulate:a", 1f, .7f).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(Panel, "modulate:a", 1f, .55f).SetDelay(.25f).SetTrans(Tween.TransitionType.Sine); tween.TweenProperty(Panel, "scale", Vector2.One, .55f).SetDelay(.25f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.Finished += () => { Logo.Modulate = Colors.White; Panel.Modulate = Colors.White; Panel.Scale = Vector2.One; };
    }

    private void Begin(bool registering, Func<Error> send)
    {
        if (UpdateRequired) return;
        _registering = registering; Panel.SetBusy(true); Say(registering ? "Criando a conta…" : "Conectando…", false);
        var error = send();
        if (error != Error.Ok) OnRequestFinished(false, registering, error == Error.Busy ? "Aguarde: outro pedido ainda está em andamento." : $"Não foi possível iniciar o pedido: {error}");
    }

    private void Say(string text, bool error) { if (_registering) Panel.Register.Say(text, error); else Panel.Form.Say(text, error); }

    /// <summary>The server speaks English in its error titles; the player reads Portuguese.</summary>
    public static string Translate(string message) => message switch
    {
        "Invalid credentials." => "Usuário ou senha incorretos.",
        "Username or email is already registered." => "Esse nome de usuário ou e-mail já está em uso.",
        "Authentication failed." => "Não foi possível entrar. Tente de novo.",
        "The server returned an invalid response." => "O servidor respondeu algo que não consegui entender.",
        _ when message.Contains("password:", StringComparison.OrdinalIgnoreCase) => "A senha precisa ter de 12 a 128 caracteres, com maiúscula, minúscula e número.",
        _ => message
    };

    private void OnRequestFinished(bool success, bool newAccount, string message)
    {
        Panel.SetBusy(false); _registering = newAccount;
        if (!success) { Say(Translate(message), true); if (UpdateRequired) ShowUpdate(); return; }
        if (_remember && !newAccount) SavedLogin.Save(_login, _password); else if (!newAccount) SavedLogin.Clear();
        _password = string.Empty;
        Say(newAccount ? "Conta criada! Preparando o seu personagem…" : "Pronto! Entrando no mundo…", false);
        Panel.SetBusy(true); // stays blocked while the next screen loads
        Music.FadeOutAndFree();
        if (EnterOverride is not null) { EnterOverride(newAccount); return; }
        var ui = GetNode<UIManager>("/root/UIManager");
        if (newAccount) ui.ShowCharacterCreator(); else ui.ShowWorld();
    }

    /// <summary>A newer client is announced in the news strip and under the form; one below the minimum blocks the login until it is updated.</summary>
    private void ShowUpdate()
    {
        if (_updates is null) return;
        var items = new List<string>(); if (_updates.Status is LifeVerse.Shared.Updates.UpdateStatus.Available or LifeVerse.Shared.Updates.UpdateStatus.Required) items.Add(_updates.Describe());
        News.SetItems(items);
        var blocked = UpdateRequired; Panel.Form.Enter.Disabled = Panel.Register.Create.Disabled = blocked;
        if (items.Count == 0) return;
        Panel.Form.Say(_updates.Describe(), blocked);
        if (DownloadButton is not null || _updates.Release is not { HasSafeDownloadLink: true }) return;
        DownloadButton = new Button { Text = "Baixar atualização", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 46) }; UiStyle.StyleButton(DownloadButton, UiStyle.ButtonKind.Secondary, 12, 8);
        DownloadButton.Pressed += _updates.OpenDownload; Panel.Footer.AddChild(DownloadButton);
    }
}
