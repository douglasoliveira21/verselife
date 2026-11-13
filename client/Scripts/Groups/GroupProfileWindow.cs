using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;
using LifeVerse.Client.Scripts.Notifications;
using LifeVerse.Shared.Groups;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>
/// The profile of a group: a side bar with ten tabs (Geral, Membros, Cargos, Permissões, Avisos, Chat, Terrenos, Economia, Propostas, Configurações) and the tab on the right. It shows what the server says and
/// offers only what the cargo of the person allows (the server checks everything again). The tabs live in the other files of this class.
/// </summary>
public sealed partial class GroupProfileWindow : Node
{
    public static readonly (string Id, string Text, string Icon)[] Tabs =
    [
        ("geral", "Geral", "info"), ("membros", "Membros", "people"), ("cargos", "Cargos", "star"), ("permissoes", "Permissões", "lock"), ("avisos", "Avisos", "bell"),
        ("chat", "Chat", "chat"), ("terrenos", "Terrenos", "map"), ("economia", "Economia", "coins"), ("propostas", "Propostas", "check"), ("configuracoes", "Configurações", "settings")
    ];

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] CategoryNames = ["Social", "Construção", "Comércio", "Jogos", "Educação", "Música", "Arte", "Eventos", "Outros"];

    private FloatingWindow _window = null!;
    private VBoxContainer _content = null!;
    private Label _note = null!, _title = null!;
    private readonly Dictionary<string, Button> _tabButtons = [];
    private GroupProfileApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private Guid _id;
    private JsonElement _view;
    private string _tab = "geral";
    private bool _loaded, _busy;
    private int _generation;
    private double _chatIn = 6;

    /// <summary>Goes to an island (the headquarters of the group).</summary>
    public Action<Guid>? Visit { get; set; }
    /// <summary>Opens the profile of a person.</summary>
    public Action<Guid>? OpenProfile { get; set; }
    /// <summary>Opens the older administration window of the group on one of its tabs (leadership and dissolving live there).</summary>
    public Action<Guid, int>? OpenAdmin { get; set; }
    /// <summary>Opens the land management of the groups.</summary>
    public Action<Guid>? ManageLand { get; set; }
    /// <summary>A short sentence for the main UI to show as a toast.</summary>
    public Action<string>? Said { get; set; }
    /// <summary>The group changed (left, edited): the list of groups should be read again.</summary>
    public Action? Changed { get; set; }

    public bool Visible => _window.Visible;
    public string TabForTesting => _tab;
    public Guid IdForTesting => _id;
    public Vector2 SizeForTesting => _window.Size;
    public string NoteForTesting => _note.Text;
    public string TitleForTesting => _title.Text;
    public string TextForTesting => string.Join("|", _content.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public bool HasButtonForTesting(string text) => _content.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => (b.Text.Trim() == text || b.TooltipText == text) && b.Visible);
    public bool ButtonEnabledForTesting(string text) => _content.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text || b.TooltipText == text) is { Disabled: false };
    public void PressForTesting(string text) { if (_content.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text || b.TooltipText == text) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }
    public Control ContentForTesting => _content;
    public async Task SelectRoleForTesting(string name) { var role = Items(_roles).FirstOrDefault(r => Str(r, "name") == name); if (role.ValueKind == JsonValueKind.Object) { _roleSelected = Guid.Parse(Str(role, "id")); await ShowTabAsync("cargos"); } }
    public IEnumerable<ConfirmationDialog> DialogsForTesting => _window.GetChildren().OfType<ConfirmationDialog>();
    public IReadOnlyList<string> TabTextsForTesting => [.. Tabs.Select(t => t.Text)];
    public bool TabEnabledForTesting(string id) => !_tabButtons[id].Disabled;
    public void SetTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new GroupProfileApi(new IslandManagementApi(transport)); }

    private GroupProfileApi Api => _api ??= new GroupProfileApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));

    public override void _Ready() => Build();

    public void Close() => _window.Visible = false;
    public async Task OpenAsync(Guid group, string tab = "geral")
    {
        if (_id != group) { _id = group; _loaded = false; _view = default; }
        _window.Visible = true; _window.MoveToFront(); _tab = Tabs.Any(t => t.Id == tab) ? tab : "geral"; await ReloadAsync();
    }

    // ---- reading what the server says about the group ----

    private static string Str(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static long Long(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
    private static int Int(JsonElement e, string n) => (int)Long(e, n);
    private static bool Flag(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static DateTimeOffset Date(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : default;
    private static JsonElement Prop(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) ? v : default;
    private static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];
    private JsonElement Card => Prop(_view, "card");
    private string GroupName => Str(Card, "name");
    private bool IsMember => Flag(Card, "isMember");
    private bool IsLeader => Flag(_view, "isLeader");
    private int MyRank => IsLeader ? GroupRules.LeaderRank : Int(_view, "myRank");
    private bool Can(string permission) => IsLeader || Items(Prop(_view, "myPermissions")).Any(p => p.GetString() == permission);
    private static string Moment(DateTimeOffset when) => when == default ? "—" : when.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt);
    private static string Day(DateTimeOffset when) => when == default ? "—" : when.ToLocalTime().ToString("dd/MM/yyyy", Pt);
    private static string Money(long value) => "L$ " + value.ToString("N0", Pt);
    private static string CategoryText(string key) { var i = Array.IndexOf(GroupRules.Categories, key); return i >= 0 && i < CategoryNames.Length ? CategoryNames[i] : key; }
    private static string JoinText(string policy) => policy switch { "open" => "Entrada livre", "invite" => "Só por convite", _ => "Entrada por aprovação" };
    private static string RatingText(string rating) => rating switch { "moderado" => "Moderado", "adulto" => "Adulto", _ => "Livre" };

    private async Task<bool> LoadViewAsync()
    {
        var reply = await Api.Get(_id);
        if (!reply.Ok) { _note.Text = reply.Status == 404 ? "Esse grupo não existe mais ou é privado." : reply.Status == 0 ? "Sem conexão com o servidor." : LandManagementApi.Words(reply); return false; }
        _view = reply.Body.Clone(); _loaded = true; _note.Text = ""; _title.Text = GroupName; return true;
    }

    public async Task ReloadAsync()
    {
        if (_busy) return; _busy = true; _note.Text = _loaded ? "" : "Carregando…";
        var ok = await LoadViewAsync(); _busy = false;
        if (!ok) { if (!_loaded) ClearContent(); return; }
        RefreshTabs(); await ShowTabAsync(_tab);
    }

    // ---- the shell ----

    private void Build()
    {
        _window = new FloatingWindow("Grupo", icon: "people", minimizable: true) { Visible = false }; _window.Place(.5f, .5f, -470, -340, 470, 340); _window.MakeResizable(); _window.CloseRequested += () => _window.Visible = false; AddChild(_window);
        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); _window.Content.AddChild(body);
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(176, 0) }; side.AddThemeConstantOverride("separation", 4); body.AddChild(side);
        _title = new Label { Text = "", ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(160, 0) }; _title.AddThemeFontSizeOverride("font_size", 15); _title.AddThemeFontOverride("font", UiStyle.Medium); side.AddChild(_title);
        foreach (var (id, text, icon) in Tabs)
        {
            var tab = id; var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 18), Alignment = HorizontalAlignment.Left, ToggleMode = true, Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), IconAlignment = HorizontalAlignment.Left };
            button.Pressed += () => { _tab = tab; RefreshTabs(); _ = ShowTabAsync(tab); }; side.AddChild(button); _tabButtons[id] = button;
        }
        side.AddChild(IslandUi.Spacer());
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 6); body.AddChild(right);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; _note.AddThemeColorOverride("font_color", UiStyle.Warning); right.AddChild(_note);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(480, 380) }; right.AddChild(scroll);
        _content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _content.AddThemeConstantOverride("separation", 10); scroll.AddChild(_content);
        RefreshTabs();
    }

    private void RefreshTabs()
    {
        foreach (var (id, button) in _tabButtons)
        {
            var on = id == _tab; button.ButtonPressed = on; var icon = Tabs.First(t => t.Id == id).Icon;
            button.Icon = UiStyle.Icon(icon, on ? UiStyle.Accent : UiStyle.Text, 18);
            var look = UiStyle.Box(on ? new Color(UiStyle.AccentFill, .7f) : new Color(0, 0, 0, 0), on ? UiStyle.Accent : new Color(0, 0, 0, 0), 10, on ? 1 : 0, 10, 4); if (on) { look.BorderWidthLeft = 3; }
            foreach (var state in new[] { "normal", "pressed", "hover", "focus", "hover_pressed" }) button.AddThemeStyleboxOverride(state, state == "hover" && !on ? UiStyle.Box(new Color(1, 1, 1, .06f), new Color(0, 0, 0, 0), 10, 0, 10, 4) : look);
            button.AddThemeColorOverride("font_color", on ? UiStyle.Accent : UiStyle.Text); button.AddThemeColorOverride("font_pressed_color", UiStyle.Accent);
            // a person who is not a member only reads the general tab
            button.Disabled = _loaded && !IsMember && id != "geral" && id != "configuracoes"; if (button.Disabled && id != "geral") button.TooltipText = "Só membros veem esta aba.";
            if (id == "configuracoes") button.Disabled = _loaded && !Can(GroupPermissions.EditProfile); if (id == "configuracoes" && button.Disabled) button.TooltipText = "Só quem pode editar o perfil do grupo.";
        }
    }

    private void ClearContent() { foreach (var child in _content.GetChildren()) child.QueueFree(); }

    private async Task ShowTabAsync(string tab)
    {
        var generation = ++_generation; _tab = tab; ClearContent();
        if (!IsMember && tab != "geral" && tab != "configuracoes") { _tab = "geral"; RefreshTabs(); tab = "geral"; }
        switch (tab)
        {
            case "geral": await ShowGeneralAsync(generation); break;
            case "membros": await ShowMembersAsync(generation); break;
            case "cargos": await ShowRolesAsync(generation); break;
            case "permissoes": await ShowMatrixAsync(generation); break;
            case "avisos": await ShowNoticesAsync(generation); break;
            case "chat": await ShowChatAsync(generation); break;
            case "terrenos": await ShowLandsAsync(generation); break;
            case "economia": await ShowEconomyAsync(generation); break;
            case "propostas": await ShowProposalsAsync(generation); break;
            case "configuracoes": ShowSettings(); break;
        }
    }

    private bool Stale(int generation) => generation != _generation;

    // ---- small builders shared by the tabs ----

    private Label Heading(string text) { var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeFontOverride("font", UiStyle.Medium); return label; }
    private Label Muted(string text, int size = 12) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label; }
    private Label Body(string text, int size = 13) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private Control TitleRow(string title, string? counter = null, params Control[] actions)
    {
        var row = IslandUi.Row(10); row.AddChild(Heading(title)); if (counter is not null) row.AddChild(Muted(counter)); row.AddChild(IslandUi.Spacer()); foreach (var a in actions) row.AddChild(a); return row;
    }
    private Button Act(string text, string? icon, Action action, IslandUi.Kind kind = IslandUi.Kind.Secondary, bool enabled = true, string tip = "") { var b = IslandUi.Action(text, icon, kind, 36, 16); b.Disabled = !enabled; b.TooltipText = tip; b.Pressed += action; return b; }
    private PanelContainer CardBox(Color? border = null) { var p = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; p.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(UiStyle.RowFill, .55f), border ?? UiStyle.BorderSoft, 12, 1, 14, 10)); return p; }
    private Control Empty(string text) { var label = Muted(text, 14); label.HorizontalAlignment = HorizontalAlignment.Center; return label; }

    private void Notify(string text) => Said?.Invoke(text);

    /// <summary>Sends a change and, when it worked, says so and reads the group again.</summary>
    private async Task<bool> Do(Task<IslandReply> call, string done, bool reload = true)
    {
        var reply = await call; if (!reply.Ok) { Notify(GroupWords(reply)); return false; }
        if (done.Length > 0) Notify(done); if (reload) { Changed?.Invoke(); await ReloadAsync(); } return true;
    }

    internal static string GroupWords(IslandReply reply)
    {
        var code = reply.Body.ValueKind == JsonValueKind.Object && reply.Body.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
        return code switch
        {
            "forbidden" => "Seu cargo não permite isso.", "outranked" => "Essa pessoa ou esse cargo está acima do seu.", "cannot_grant" => "Você não pode conceder uma permissão que não tem.", "muted" => "Você está silenciado neste grupo.",
            "bad_name" => "O nome não é válido (2 a 40 caracteres).", "name_taken" => "Já existe um grupo ou cargo com esse nome.", "bad_tag" => "A sigla precisa ter de 2 a 5 letras ou números.", "tag_taken" => "Essa sigla já é de outro grupo.",
            "bad_title" => "O título não é válido.", "bad_options" => "Informe de 2 a 8 opções diferentes.", "bad_deadline" => "O prazo precisa ficar entre 5 minutos e 30 dias.", "bad_quorum" => "O quórum vai de 0 a 100.", "bad_criterion" => "Critério inválido.",
            "too_many_proposals" => "Já há propostas demais abertas.", "not_open" => "Essa votação já terminou.", "not_eligible" => "O seu cargo não pode votar nessa proposta.", "already_voted" => "Você já votou nessa proposta.", "bad_option" => "Escolha uma das opções.",
            "fee_required" => "A taxa de entrada mudou: confirme o valor de novo.", "insufficient_balance" => "Saldo insuficiente na carteira.", "wallet_blocked" => "A carteira está bloqueada.", "already_member" => "Você já faz parte deste grupo.",
            "banned" => "Você foi banido deste grupo.", "full" or "group_full" => "O grupo está cheio.", "invite_only" => "Este grupo só entra por convite.", "bad_category" => "Categoria inválida.", "bad_language" => "Idioma inválido.", "bad_cover" => "A capa precisa ser um endereço https.",
            "bad_headquarters" => "Essa ilha não pode ser a sede.", "bad_fee" => "A taxa só vale para grupos de entrada livre, até L$ 1.000.000.", "bad_message" => "A mensagem é longa demais.", "bad_role" => "Esse cargo não existe ou não pode ser dado.",
            "bad_attachment" => "O anexo precisa ser um link https.", "bad_expiry" => "A validade precisa ser entre 1 minuto e 1 ano.", "rate_limited" => "Calma: faça isso de novo daqui a pouco.", "not_found" => "Não encontrei isso: pode ter sido apagado.",
            "too_many_roles" => "O grupo já tem cargos demais.", "bad_rank" => "O nível do cargo vai de 1 a 99.", "bad_permissions" => "Permissão inválida.", "system_role" => "Os cargos iniciais mantêm o nome e o nível.", "bad_message_" => "",
            _ => reply.Message.Length > 0 ? reply.Message : LandManagementApi.Words(reply)
        };
    }

    /// <summary>A confirmation dialog sized to its content (see <see cref="IslandUi.Fit"/>).</summary>
    private void Ask(string title, Control content, Func<Task<bool>> accept, string okText, Action<Button>? withOk = null)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = okText, CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() };
        dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); _window.AddChild(dialog); withOk?.Invoke(dialog.GetOkButton());
        dialog.Confirmed += async () => { await accept(); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree(); IslandUi.Fit(dialog, 520);
    }

    private Control Avatar(string name, int size = 32)
    {
        var circle = new PanelContainer { CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; circle.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.Accent, .22f), UiStyle.Accent, size / 2, 1, 0, 0));
        var initial = new Label { Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; initial.AddThemeFontSizeOverride("font_size", size / 2 + 1); initial.AddThemeColorOverride("font_color", UiStyle.Accent); circle.AddChild(initial); return circle;
    }

    public override void _Process(double delta)
    {
        if (_window is null || !_window.Visible || _tab != "chat" || !_loaded) return;
        _chatIn -= delta; if (_chatIn > 0) return; _chatIn = 6; _ = RefreshChatAsync();
    }
}
