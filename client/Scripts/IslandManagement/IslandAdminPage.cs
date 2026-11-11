using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Administração tab: who is on the island now (as its server reports them), removing and banning, a notice for everybody or for one person, closing and reopening the island, and how the
/// server is doing. The API cannot reach a player, so removing and warning are requests the island server picks up with its next heartbeat (up to about 15 seconds). The list refreshes by itself.
/// </summary>
public partial class IslandAdminPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Text, TimeSpan? Span)[] BanLengths = [("1 dia", TimeSpan.FromDays(1)), ("7 dias", TimeSpan.FromDays(7)), ("30 dias", TimeSpan.FromDays(30)), ("Permanente", null)];
    private const double RefreshSeconds = 10;

    private Label _state = null!, _uptime = null!, _heartbeat = null!, _instance = null!, _visitors = null!, _note = null!, _status = null!, _count = null!, _empty = null!, _closedNote = null!;
    private Control _stateDot = null!;
    private LineEdit _notice = null!, _reason = null!;
    private OptionButton _banLength = null!;
    private Button _close = null!, _reopen = null!, _send = null!;
    private VBoxContainer _rows = null!;
    private IslandManagementApi? _api;
    private Guid _island;
    private bool _closed, _running;
    private double _since;
    private List<(Guid Id, string Name, bool Staff)> _players = [];

    public event Action<string>? Notify;
    public event Action<Guid>? ViewProfile;
    public Action<string, string, Action>? Ask { get; set; }
    /// <summary>Closes (true) or reopens (false) the island; answers null when it worked, otherwise the reason it did not.</summary>
    public Func<bool, Task<string?>>? SetClosed { get; set; }

    public string StatusForTesting => _status.Text;
    public string StateForTesting => _state.Text;
    public string InfoForTesting => $"{_uptime.Text}|{_instance.Text}|{_visitors.Text}";
    public int PlayersForTesting => _players.Count;
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];
    public bool CloseEnabledForTesting => !_close.Disabled;
    public bool CloseForTestingDisabled() => _close.Disabled;
    public bool ReopenEnabledForTesting => !_reopen.Disabled;
    public string NoticeForTesting { get => _notice.Text; set => _notice.Text = value; }
    public string ReasonForTesting { get => _reason.Text; set => _reason.Text = value; }
    public void SelectBanLengthForTesting(int index) => _banLength.Select(index);
    public Task SendNoticeForTesting() => NoticeAsync(null, "todos");
    public Task NoticeToFirstForTesting() => _players.Count == 0 ? Task.CompletedTask : NoticeAsync(_players[0].Id, _players[0].Name);
    public Task KickFirstForTesting() => _players.Count == 0 ? Task.CompletedTask : KickAsync(_players[0].Id, _players[0].Name);
    public Task BanFirstForTesting() => _players.Count == 0 ? Task.CompletedTask : BanAsync(_players[0].Id, _players[0].Name);
    public Task CloseForTesting(bool close) => ToggleClosedAsync(close);

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        var top = IslandUi.Row(14); top.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.CustomMinimumSize = new Vector2(0, 250); column.AddChild(top);
        top.AddChild(BuildInfo()); top.AddChild(BuildOperations());
        column.AddChild(BuildPlayers());
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _island == Guid.Empty) { _since = 0; return; }
        _since += delta; if (_since >= RefreshSeconds) { _since = 0; _ = LoadAsync(); }
    }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildInfo()
    {
        var (card, body) = IslandUi.Card("Informações do Servidor", "info"); card.SizeFlagsStretchRatio = 42;
        var head = IslandUi.Row(10); _stateDot = IslandUi.Dot(IslandUi.Disabled, 12); head.AddChild(_stateDot); _state = IslandUi.Text("—", 18, IslandUi.TextPrimary, true); head.AddChild(_state); body.AddChild(head);
        void Line(string caption, out Label value) { var row = IslandUi.Row(8); var c = IslandUi.Text(caption, 14, IslandUi.TextSecondary); c.CustomMinimumSize = new Vector2(150, 0); row.AddChild(c); value = IslandUi.Text("—", 14, IslandUi.TextPrimary, true); row.AddChild(value); body.AddChild(row); }
        Line("Tempo no ar:", out _uptime); Line("Último sinal:", out _heartbeat); Line("Servidor:", out _instance); Line("Visitantes:", out _visitors);
        _note = IslandUi.Text("", 13, IslandUi.TextMuted, false, true); body.AddChild(_note);
        return card;
    }

    private Control BuildOperations()
    {
        var (card, body) = IslandUi.Card("Operações da Ilha", "settings"); card.SizeFlagsStretchRatio = 58;
        var buttons = IslandUi.Row(10); body.AddChild(buttons);
        _close = IslandUi.Action("Fechar Ilha", "lock", IslandUi.Kind.Danger, 40); _close.Pressed += () => Confirm("Fechar a ilha? Ninguém além do proprietário e dos administradores consegue entrar. Quem já está nela continua.", "Fechar", () => _ = ToggleClosedAsync(true)); buttons.AddChild(_close);
        _reopen = IslandUi.Action("Reabrir Ilha", "unlock", IslandUi.Kind.Secondary, 40); _reopen.Pressed += () => _ = ToggleClosedAsync(false); buttons.AddChild(_reopen);
        _closedNote = IslandUi.Text("", 13, IslandUi.TextMuted, false, true); body.AddChild(_closedNote);
        body.AddChild(IslandUi.Divider());
        body.AddChild(IslandUi.Text("Aviso", 14, IslandUi.TextSecondary, true));
        var notice = IslandUi.Row(10); body.AddChild(notice);
        _notice = IslandUi.Input("Texto do aviso (até 200 caracteres)", 200); _notice.TextSubmitted += _t => _ = NoticeAsync(null, "todos"); notice.AddChild(_notice);
        _send = IslandUi.Action("Enviar a Todos", "send", IslandUi.Kind.Primary, 40); _send.Pressed += () => _ = NoticeAsync(null, "todos"); notice.AddChild(_send);
        body.AddChild(IslandUi.Text("Para avisar uma só pessoa, escreva o texto aqui e use o botão de aviso na linha dela.", 13, IslandUi.TextMuted, false, true));
        return card;
    }

    private Control BuildPlayers()
    {
        var (card, body) = IslandUi.Card("Jogadores Conectados", "people"); card.SizeFlagsVertical = SizeFlags.ExpandFill;
        var tools = IslandUi.Row(10); body.AddChild(tools);
        _reason = IslandUi.Input("Motivo (para expulsar ou banir)", 200); tools.AddChild(_reason);
        _banLength = IslandUi.Dropdown(BanLengths.Select((b, i) => (b.Text, i.ToString()))); _banLength.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _banLength.CustomMinimumSize = new Vector2(170, 40); _banLength.Select(1); tools.AddChild(_banLength);
        var refresh = IslandUi.IconButton("reset", "Atualizar agora", 40); refresh.Pressed += () => _ = LoadAsync(); tools.AddChild(refresh);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); body.AddChild(_status);
        var head = IslandUi.Row(8); head.AddChild(Head("Pessoa", 3)); head.AddChild(Head("Tempo online", 2)); head.AddChild(Head("Posição", 3)); head.AddChild(Head("Ações", 3, true));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_rows);
        _empty = IslandUi.Text("Ninguém conectado agora.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 60); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        _count = IslandUi.Text("", 14, IslandUi.TextSecondary); body.AddChild(_count);
        return card;
    }

    private static Label Head(string text, int ratio, bool end = false) { var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (end) label.HorizontalAlignment = HorizontalAlignment.Right; return label; }

    // ---- data -------------------------------------------------------------------------------------------------------------

    /// <summary>What the island says about being closed, from the General data (the window tells this page).</summary>
    public void SetClosedState(bool closed) { _closed = closed; PaintOperations(); }

    private void PaintOperations()
    {
        _close.Disabled = _closed; _reopen.Disabled = !_closed;
        _closedNote.Text = _closed ? "A ilha está fechada: só o proprietário e os administradores entram." : "A ilha está aberta conforme o modo de acesso.";
        _send.Disabled = !_running;
    }

    private static string Duration(long seconds) => seconds < 60 ? $"{seconds} s" : seconds < 3600 ? $"{seconds / 60} min" : seconds < 86400 ? $"{seconds / 3600} h {seconds % 3600 / 60:00} min" : $"{seconds / 86400} d {seconds % 86400 / 3600} h";
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
    private static double Dbl(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) ? n : 0;
    private static string Ago(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? $"há {Duration(Math.Max(0, (long)(DateTimeOffset.UtcNow - d).TotalSeconds))}" : "—";

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.ServerGet(_island);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement d)
    {
        var state = Str(d, "state"); _running = d.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.True;
        var color = IslandUi.StateColor(state); _state.Text = IslandUi.StateText(state); ((ColorRect)_stateDot).Color = color; _state.AddThemeColorOverride("font_color", color);
        _uptime.Text = _running ? Duration(Long(d, "uptimeSeconds")) : "—"; _heartbeat.Text = _running ? Ago(d, "lastHeartbeatAt") : "—"; _instance.Text = _running ? Str(d, "instanceId") : "—";
        _visitors.Text = $"{Long(d, "visitors")} / {Long(d, "maxPlayers")}";
        _closed = d.TryGetProperty("closed", out var c) && c.ValueKind == JsonValueKind.True;
        var fresh = d.TryGetProperty("fresh", out var f) && f.ValueKind == JsonValueKind.True;
        _note.Text = !_running ? "O servidor da ilha está desligado. Ele liga quando alguém entra." : !fresh ? "O servidor ainda não mandou a lista de jogadores (ela chega a cada 15 segundos)." : $"Lista enviada pelo servidor {Ago(d, "reportedAt")}.";
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        var items = fresh && d.TryGetProperty("players", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        _players = [.. items.Select(p => (Guid.Parse(Str(p, "userId")), Str(p, "name"), p.TryGetProperty("isStaff", out var s) && s.ValueKind == JsonValueKind.True))];
        foreach (var item in items) _rows.AddChild(BuildRow(item));
        _empty.Visible = items.Count == 0; _count.Text = items.Count == 1 ? "1 pessoa conectada" : $"{items.Count} pessoas conectadas";
        PaintOperations();
    }

    private Control BuildRow(JsonElement item)
    {
        var id = Guid.Parse(Str(item, "userId")); var name = Str(item, "name"); var staff = item.TryGetProperty("isStaff", out var s) && s.ValueKind == JsonValueKind.True;
        var box = new PanelContainer { CustomMinimumSize = new Vector2(0, 50) }; box.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(0, 0, 0, 0), null, 0, 0, 0, 0));
        var column = IslandUi.Column(0); box.AddChild(column);
        var row = IslandUi.Row(8); row.CustomMinimumSize = new Vector2(0, 49); column.AddChild(row);
        var who = IslandUi.Row(9); who.SizeFlagsHorizontal = SizeFlags.ExpandFill; who.SizeFlagsStretchRatio = 3; who.AddChild(UiStyle.IconRect("avatar", IslandUi.TextSecondary, 22)); who.AddChild(IslandUi.Text(name, 15, IslandUi.TextPrimary, true));
        if (staff) who.AddChild(IslandUi.Pill("Equipe", IslandUi.AccentSecondary)); row.AddChild(who);
        var online = IslandUi.Text(Duration(Long(item, "onlineSeconds")), 14, IslandUi.TextSecondary); online.SizeFlagsHorizontal = SizeFlags.ExpandFill; online.SizeFlagsStretchRatio = 2; row.AddChild(online);
        var position = IslandUi.Text($"{Dbl(item, "x").ToString("0", Pt)}, {Dbl(item, "y").ToString("0", Pt)}, {Dbl(item, "z").ToString("0", Pt)}", 14, IslandUi.TextSecondary); position.SizeFlagsHorizontal = SizeFlags.ExpandFill; position.SizeFlagsStretchRatio = 3; row.AddChild(position);
        var actions = IslandUi.Row(6); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.SizeFlagsStretchRatio = 3; actions.Alignment = BoxContainer.AlignmentMode.End; row.AddChild(actions);
        var profile = IslandUi.IconButton("user", "Ver perfil", 34); profile.Pressed += () => ViewProfile?.Invoke(id); actions.AddChild(profile);
        var warn = IslandUi.IconButton("send", "Enviar o aviso só para esta pessoa", 34); warn.Pressed += () => _ = NoticeAsync(id, name); actions.AddChild(warn);
        var kick = IslandUi.Action("Expulsar", null, IslandUi.Kind.Orange, 34); kick.Disabled = staff; kick.TooltipText = staff ? "A equipe não pode ser expulsa." : "Tira a pessoa da ilha agora; ela pode voltar se o acesso permitir.";
        kick.Pressed += () => Confirm($"Expulsar {name} da ilha? Ela pode voltar se o acesso permitir.", "Expulsar", () => _ = KickAsync(id, name)); actions.AddChild(kick);
        var ban = IslandUi.Action("Banir", "ban", IslandUi.Kind.Danger, 34); ban.Disabled = staff;
        ban.Pressed += () => { if (_reason.Text.Trim().Length == 0) { Say("Escreva o motivo para banir.", true); return; } Confirm($"Banir {name} ({(BanLengths[Math.Max(0, _banLength.Selected)].Span is null ? "permanente" : "por " + BanLengths[Math.Max(0, _banLength.Selected)].Text)}) e tirar da ilha agora?", "Banir", () => _ = BanAsync(id, name)); };
        actions.AddChild(ban);
        column.AddChild(IslandUi.Divider());
        return box;
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------

    private void Confirm(string text, string ok, Action action) { if (Ask is { } ask) ask(text, ok, action); else action(); }

    public async Task NoticeAsync(Guid? user, string who)
    {
        if (_api is null) return;
        var text = _notice.Text.Trim(); if (text.Length == 0) { Say("Escreva o aviso.", true); return; }
        var reply = await _api.ServerNotice(_island, text, user);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Say(user is null ? "Aviso a caminho de todos (chega em até 15 segundos)." : $"Aviso a caminho de {who} (chega em até 15 segundos).", false); Notify?.Invoke("Aviso enviado."); _notice.Text = "";
    }

    public async Task KickAsync(Guid user, string name)
    {
        if (_api is null) return;
        var reply = await _api.ServerKick(_island, user, _reason.Text.Trim());
        if (!reply.Ok) { Say(reply.Message, true); await LoadAsync(); return; }
        Say($"{name} será removida em até 15 segundos.", false); Notify?.Invoke($"{name} foi removida da ilha."); _reason.Text = ""; await LoadAsync();
    }

    public async Task BanAsync(Guid user, string name)
    {
        if (_api is null) return;
        var span = BanLengths[Math.Max(0, _banLength.Selected)].Span;
        var ban = await _api.BanAdd(_island, name, _reason.Text.Trim(), span is { } s ? DateTimeOffset.UtcNow + s : null);
        if (!ban.Ok) { Say(ban.Message, true); return; }
        var kick = await _api.ServerKick(_island, user, _reason.Text.Trim());
        Say(kick.Ok ? $"{name} foi banida e será removida em até 15 segundos." : $"{name} foi banida, mas não deu para remover agora: {kick.Message}", !kick.Ok); Notify?.Invoke($"{name} foi banida."); _reason.Text = ""; await LoadAsync();
    }

    private async Task ToggleClosedAsync(bool close)
    {
        if (SetClosed is null) return;
        var problem = await SetClosed(close);
        if (problem is not null) { Say(problem, true); return; }
        _closed = close; PaintOperations(); Say(close ? "Ilha fechada." : "Ilha reaberta.", false); Notify?.Invoke(close ? "Ilha fechada." : "Ilha reaberta.");
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
