using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Reinicialização tab: the state of the island, a form to restart it (now, in 1, 5 or 10 minutes, or at a time), the countdown of the one that is scheduled and the history. The API warns the
/// people on the island, tells the island server to save and stop and starts the island again; this tab only asks and shows. It is an administrative tool, so every request asks first.
/// </summary>
public partial class IslandRestartPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Text, string Mode, int? Minutes)[] Options = [("Imediatamente", "now", null), ("Em 1 minuto", "in", 1), ("Em 5 minutos", "in", 5), ("Em 10 minutos", "in", 10), ("Agendar horário", "at", null)];
    private const string TimeFormat = "dd/MM/yyyy HH:mm";

    private readonly List<Button> _optionButtons = [];
    private Label _state = null!, _uptime = null!, _note = null!, _status = null!, _countdown = null!, _activeText = null!, _empty = null!;
    private Control _stateDot = null!, _activeCard = null!, _atRow = null!;
    private LineEdit _at = null!, _reason = null!;
    private Button _request = null!, _cancel = null!;
    private VBoxContainer _rows = null!;
    private IslandManagementApi? _api;
    private Guid _island, _activeId;
    private int _option = 2;
    private bool _running, _hasActive, _scheduled;
    private double _remaining, _since;

    public event Action<string>? Notify;
    public Action<string, string, Action>? Ask { get; set; }

    public string StatusForTesting => _status.Text;
    public string StateForTesting => _state.Text;
    public string UptimeForTesting => _uptime.Text;
    public bool ActiveVisibleForTesting => _activeCard.Visible;
    public string CountdownForTesting => _countdown.Text;
    public string ActiveTextForTesting => _activeText.Text;
    public bool CancelVisibleForTesting => _cancel.Visible;
    public bool RequestEnabledForTesting => !_request.Disabled;
    public bool AtVisibleForTesting => _atRow.Visible;
    public int HistoryForTesting => _rows.GetChildCount() / 2;
    public IReadOnlyList<string> HistoryTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text))).Where(t => t.Length > 0)];
    public string AtForTesting { get => _at.Text; set => _at.Text = value; }
    public string ReasonForTesting { get => _reason.Text; set => _reason.Text = value; }
    public void SelectOptionForTesting(int index) => Choose(index);
    public Task RequestForTesting() => RequestAsync();
    public Task CancelForTesting() => CancelAsync();
    public void PressRequestForTesting() => _request.EmitSignal(BaseButton.SignalName.Pressed);

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        var top = IslandUi.Row(14); top.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.CustomMinimumSize = new Vector2(0, 280); column.AddChild(top);
        top.AddChild(BuildState()); top.AddChild(BuildForm());
        _activeCard = BuildActive(); _activeCard.Visible = false; column.AddChild(_activeCard);
        column.AddChild(BuildHistory());
        Choose(2);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _island == Guid.Empty) { _since = 0; return; }
        if (_scheduled && _remaining > 0) { _remaining = Math.Max(0, _remaining - delta); _countdown.Text = Clock((int)Math.Ceiling(_remaining)); }
        _since += delta; if (_since >= (_hasActive ? 5 : 15)) { _since = 0; _ = LoadAsync(); }
    }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildState()
    {
        var (card, body) = IslandUi.Card("Estado Atual da Ilha", "info"); card.SizeFlagsStretchRatio = 40;
        var head = IslandUi.Row(10); _stateDot = IslandUi.Dot(IslandUi.Disabled, 14); head.AddChild(_stateDot); _state = IslandUi.Text("—", 20, IslandUi.TextPrimary, true); head.AddChild(_state); body.AddChild(head);
        var row = IslandUi.Row(8); var caption = IslandUi.Text("Tempo no ar:", 14, IslandUi.TextSecondary); caption.CustomMinimumSize = new Vector2(120, 0); row.AddChild(caption); _uptime = IslandUi.Text("—", 14, IslandUi.TextPrimary, true); row.AddChild(_uptime); body.AddChild(row);
        _note = IslandUi.Text("", 13, IslandUi.TextMuted, false, true); body.AddChild(_note);
        return card;
    }

    private Control BuildForm()
    {
        var (card, body) = IslandUi.Card("Reiniciar Região", "power"); card.SizeFlagsStretchRatio = 60; body.AddThemeConstantOverride("separation", 9);
        var options = new HFlowContainer(); options.AddThemeConstantOverride("h_separation", 8); options.AddThemeConstantOverride("v_separation", 8); body.AddChild(options);
        for (var i = 0; i < Options.Length; i++) { var index = i; var button = IslandUi.Action(Options[i].Text, null, IslandUi.Kind.Secondary, 38); button.Pressed += () => Choose(index); _optionButtons.Add(button); options.AddChild(button); }
        _atRow = IslandUi.Row(10); _atRow.AddChild(IslandUi.Text("Horário (dia/mês/ano hora:minuto):", 14, IslandUi.TextSecondary)); _at = IslandUi.Input(TimeFormat); _at.CustomMinimumSize = new Vector2(190, 40); _at.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; _atRow.AddChild(_at); body.AddChild(_atRow);
        _reason = IslandUi.Input("Motivo (aparece na história; opcional)", 200); body.AddChild(_reason);
        var foot = IslandUi.Row(10); body.AddChild(foot);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); foot.AddChild(_status);
        _request = IslandUi.Action("Solicitar Reinicialização", "power", IslandUi.Kind.Orange, 42); _request.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _request.CustomMinimumSize = new Vector2(250, 42); _request.Pressed += OnRequestPressed; foot.AddChild(_request);
        body.AddChild(IslandUi.Text("Quem está na ilha recebe avisos (5 minutos, 1 minuto e 10 segundos antes), o servidor salva tudo e a ilha volta sozinha. As pessoas precisam entrar de novo.", 13, IslandUi.TextMuted, false, true));
        return card;
    }

    private Control BuildActive()
    {
        var (card, body) = IslandUi.Card("Reinicialização em Andamento", "alert");
        var row = IslandUi.Row(18); body.AddChild(row);
        _countdown = IslandUi.Text("--:--", 34, IslandUi.AccentSecondary, true); row.AddChild(_countdown);
        _activeText = IslandUi.Text("", 14, IslandUi.TextSecondary, false, true); _activeText.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(_activeText);
        _cancel = IslandUi.Action("Cancelar Reinicialização", "close", IslandUi.Kind.Danger, 42); _cancel.Pressed += () => Confirm("Cancelar a reinicialização agendada? As pessoas que já foram avisadas serão informadas.", "Cancelar reinicialização", () => _ = CancelAsync()); row.AddChild(_cancel);
        return card;
    }

    private Control BuildHistory()
    {
        var (card, body) = IslandUi.Card("Histórico", "history");
        var head = IslandUi.Row(8); head.AddChild(Head("Data", 2)); head.AddChild(Head("Responsável", 2)); head.AddChild(Head("Motivo", 3)); head.AddChild(Head("Duração", 1)); head.AddChild(Head("Resultado", 3));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); body.AddChild(_rows);
        _empty = IslandUi.Text("Nenhuma reinicialização ainda.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 56); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        return card;
    }

    private static Label Head(string text, int ratio) { var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; return label; }

    // ---- form -------------------------------------------------------------------------------------------------------------

    private void Choose(int index)
    {
        _option = index;
        for (var i = 0; i < _optionButtons.Count; i++)
        {
            var on = i == index; var box = IslandUi.Box(on ? IslandUi.AccentSelected : new Color(0, 0, 0, .1f), on ? IslandUi.AccentPrimary : new Color("#4FA9C5"), IslandUi.RadiusButton, 1, 16, 6);
            foreach (var name in new[] { "normal", "hover", "pressed", "focus" }) _optionButtons[i].AddThemeStyleboxOverride(name, box);
        }
        _atRow.Visible = Options[index].Mode == "at";
        if (_atRow.Visible && _at.Text.Length == 0) _at.Text = DateTime.Now.AddHours(1).ToString(TimeFormat, Pt);
    }

    private void Confirm(string text, string ok, Action action) { if (Ask is { } ask) ask(text, ok, action); else action(); }

    private void OnRequestPressed()
    {
        var option = Options[_option];
        if (option.Mode == "at" && !TryAt(out _)) { Say("Escreva o horário como 25/12/2026 03:30.", true); return; }
        var when = option.Mode switch { "now" => "agora", "in" => $"em {option.Minutes} minuto(s)", _ => $"em {_at.Text.Trim()}" };
        Confirm($"Reiniciar a ilha {when}? Todo mundo que está nela será desconectado e precisará entrar de novo.", "Reiniciar", () => _ = RequestAsync());
    }

    private bool TryAt(out DateTimeOffset at)
    {
        at = default;
        if (!DateTime.TryParseExact(_at.Text.Trim(), TimeFormat, Pt, DateTimeStyles.None, out var local)) return false;
        at = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)); return true;
    }

    public async Task RequestAsync()
    {
        if (_api is null) return;
        var option = Options[_option]; DateTimeOffset? at = null;
        if (option.Mode == "at") { if (!TryAt(out var parsed)) { Say("Escreva o horário como 25/12/2026 03:30.", true); return; } at = parsed; }
        _request.Disabled = true; var reply = await _api.RestartRequest(_island, option.Mode, option.Minutes, at, _reason.Text.Trim()); _request.Disabled = false;
        if (!reply.Ok) { Say(reply.Message, true); return; }
        _reason.Text = ""; Say(option.Mode == "now" ? "Reinicialização pedida: o servidor recebe o pedido em até 15 segundos." : "Reinicialização agendada.", false); Notify?.Invoke("Reinicialização solicitada."); await LoadAsync();
    }

    public async Task CancelAsync()
    {
        if (_api is null || _activeId == Guid.Empty) return;
        var reply = await _api.RestartCancel(_island, _activeId);
        if (!reply.Ok) { Say(reply.Message, true); await LoadAsync(); return; }
        Say("Reinicialização cancelada.", false); Notify?.Invoke("Reinicialização cancelada."); await LoadAsync();
    }

    // ---- data -------------------------------------------------------------------------------------------------------------

    private static string Clock(int seconds) => seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
    private static string Duration(long seconds) => seconds < 60 ? $"{seconds} s" : seconds < 3600 ? $"{seconds / 60} min" : seconds < 86400 ? $"{seconds / 3600} h {seconds % 3600 / 60:00} min" : $"{seconds / 86400} d {seconds % 86400 / 3600} h";
    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static long Long(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
    private static string Date(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : "";

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.RestartGet(_island);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement d)
    {
        var state = Str(d, "state"); _running = d.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.True;
        var color = state == "restarting" ? IslandUi.Warning : IslandUi.StateColor(state);
        _state.Text = state == "restarting" ? "Reiniciando" : IslandUi.StateText(state); ((ColorRect)_stateDot).Color = color; _state.AddThemeColorOverride("font_color", color);
        _uptime.Text = _running ? Duration(Long(d, "uptimeSeconds")) : "—";
        var active = d.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.Object ? a : default; _hasActive = active.ValueKind == JsonValueKind.Object;
        _note.Text = !_running && !_hasActive ? "A ilha está desligada: não há o que reiniciar. Ela liga quando alguém entra." : "";
        _request.Disabled = _hasActive || !_running; _request.TooltipText = _hasActive ? "Já existe uma reinicialização agendada ou em andamento." : !_running ? "A ilha está desligada." : "";
        _activeCard.Visible = _hasActive;
        if (_hasActive)
        {
            _activeId = Guid.Parse(Str(active, "id")); var status = Str(active, "status"); _scheduled = status == "scheduled"; _remaining = Long(active, "remainingSeconds");
            _countdown.Text = _scheduled ? Clock((int)_remaining) : "Em curso";
            var reason = Str(active, "reason");
            _activeText.Text = $"{(_scheduled ? $"Agendada para {Date(active, "scheduledFor")}" : "O servidor está salvando e a ilha vai voltar sozinha")} · pedida por {Str(active, "requestedBy")}{(reason.Length > 0 ? $" · motivo: {reason}" : "")}";
            _cancel.Visible = _scheduled;
        }
        else { _activeId = Guid.Empty; _scheduled = false; }
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        var history = d.TryGetProperty("history", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        foreach (var item in history) AddHistory(item);
        _empty.Visible = history.Count == 0;
    }

    private void AddHistory(JsonElement item)
    {
        var status = Str(item, "status"); var (text, color) = status switch { "completed" => ("Concluída", IslandUi.Success), "cancelled" => ("Cancelada", IslandUi.TextMuted), _ => ("Falhou", IslandUi.Error) };
        var row = IslandUi.Row(8); row.CustomMinimumSize = new Vector2(0, 44);
        void Cell(string value, int ratio, Color? ink = null, bool wrap = false) { var label = IslandUi.Text(value, 14, ink ?? IslandUi.TextSecondary, false, wrap); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (!wrap) label.ClipText = true; row.AddChild(label); }
        Cell(Date(item, "createdAt"), 2); Cell(Str(item, "requestedBy"), 2, IslandUi.TextPrimary); Cell(Str(item, "reason").Length > 0 ? Str(item, "reason") : "—", 3, null, true);
        Cell(item.TryGetProperty("durationSeconds", out var d) && d.ValueKind == JsonValueKind.Number ? Duration(d.GetInt64()) : "—", 1);
        Cell($"{text}{(Str(item, "result").Length > 0 && status == "failed" ? " · " + Str(item, "result") : "")}", 3, color, true);
        _rows.AddChild(row); _rows.AddChild(IslandUi.Divider());
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
