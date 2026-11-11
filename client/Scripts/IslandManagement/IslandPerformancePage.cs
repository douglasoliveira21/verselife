using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Desempenho tab: how the island server is doing now, how it did over the last hours (charts of what it reported) and which scripts and objects weigh the most. Only what the server measures is
/// shown; there is no CPU figure because the island server does not report one (the time of a frame says the same thing). Refreshes by itself while visible.
/// </summary>
public partial class IslandPerformancePage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Text, int Hours)[] Periods = [("Última hora", 1), ("Últimas 6 horas", 6), ("Últimas 24 horas", 24)];
    private static readonly (string Text, string Id)[] Filters = [("Scripts e objetos", ""), ("Só scripts", "script"), ("Só objetos", "object")];
    private const double RefreshSeconds = 15;

    private readonly Label[] _values = new Label[6];
    private readonly Label[] _notes = new Label[6];
    private readonly IslandLineChart[] _charts = [new(), new(), new(), new()];
    private readonly Label[] _chartNotes = new Label[4];
    private OptionButton _period = null!, _filter = null!;
    private Label _status = null!, _empty = null!;
    private VBoxContainer _rows = null!;
    private IslandManagementApi? _api;
    private Guid _island;
    private double _since;
    private JsonElement _last;

    public string StatusForTesting => _status.Text;
    public string IndicatorsForTesting => string.Join("|", _values.Select(v => v.Text));
    public IReadOnlyList<string> ChartNotesForTesting => [.. _chartNotes.Select(n => n.Text)];
    public IReadOnlyList<int> ChartPointsForTesting => [.. _charts.Select(c => c.PointCount)];
    public IReadOnlyList<double> ChartLastForTesting => [.. _charts.Select(c => c.LastValue)];
    public int RowsForTesting => _rows.GetChildCount();
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];
    public void SelectPeriodForTesting(int index) => _period.Select(index);
    public void SelectFilterForTesting(int index) { _filter.Select(index); if (_last.ValueKind == JsonValueKind.Object) ShowDiagnostics(_last); }

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        var bar = IslandUi.Row(10); column.AddChild(bar);
        _period = IslandUi.Dropdown(Periods.Select((p, i) => (p.Text, i.ToString()))); _period.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; _period.CustomMinimumSize = new Vector2(200, 40); _period.ItemSelected += _i => _ = LoadAsync(); bar.AddChild(_period);
        var refresh = IslandUi.IconButton("reset", "Atualizar agora", 40); refresh.Pressed += () => _ = LoadAsync(); bar.AddChild(refresh);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; bar.AddChild(_status);
        column.AddChild(BuildIndicators());
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 14); column.AddChild(grid);
        var specs = new (string Title, string Icon)[] { ("FPS do servidor", "chart"), ("Memória", "layers"), ("Tempo de quadro", "history"), ("Jogadores conectados", "people") };
        for (var i = 0; i < 4; i++)
        {
            var (card, body) = IslandUi.Card(specs[i].Title, specs[i].Icon); card.CustomMinimumSize = new Vector2(0, 220);
            _chartNotes[i] = IslandUi.Text("", 13, IslandUi.TextMuted); body.AddChild(_chartNotes[i]); body.AddChild(_charts[i]); grid.AddChild(card);
        }
        column.AddChild(BuildDiagnostics());
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _island == Guid.Empty) { _since = 0; return; }
        _since += delta; if (_since >= RefreshSeconds) { _since = 0; _ = LoadAsync(); }
    }

    private Control BuildIndicators()
    {
        var row = IslandUi.Row(12); row.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.CustomMinimumSize = new Vector2(0, 92);
        var specs = new (string Title, string Icon)[] { ("FPS do Servidor", "chart"), ("Tick Rate", "move"), ("Tempo de Quadro", "history"), ("Memória", "layers"), ("Jogadores", "people"), ("Scripts Ativos", "code") };
        for (var i = 0; i < 6; i++)
        {
            var (card, body) = IslandUi.Card(specs[i].Title, specs[i].Icon); body.AddThemeConstantOverride("separation", 0);
            _values[i] = IslandUi.Text("—", 22, IslandUi.TextPrimary, true); body.AddChild(_values[i]); _notes[i] = IslandUi.Text("", 12, IslandUi.TextMuted); body.AddChild(_notes[i]); row.AddChild(card);
        }
        return row;
    }

    private Control BuildDiagnostics()
    {
        var (card, body) = IslandUi.Card("Diagnóstico", "alert");
        var tools = IslandUi.Row(10); body.AddChild(tools);
        _filter = IslandUi.Dropdown(Filters); _filter.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; _filter.CustomMinimumSize = new Vector2(200, 40); _filter.ItemSelected += _i => { if (_last.ValueKind == JsonValueKind.Object) ShowDiagnostics(_last); }; tools.AddChild(_filter);
        tools.AddChild(IslandUi.Text("Scripts pelo que executaram no período; objetos pelo peso (prims).", 13, IslandUi.TextMuted, false, true));
        var head = IslandUi.Row(8); head.AddChild(Head("Recurso", 4)); head.AddChild(Head("Tipo", 1)); head.AddChild(Head("Consumo", 3)); head.AddChild(Head("Estado", 2));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); body.AddChild(_rows);
        _empty = IslandUi.Text("Nada para mostrar neste período.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 56); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        return card;
    }

    private static Label Head(string text, int ratio) { var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; return label; }

    // ---- data -------------------------------------------------------------------------------------------------------------

    private static double Num(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) ? n : 0;
    private static string One(double v) => v.ToString("0.#", Pt);

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.PerformanceGet(_island, Periods[Math.Max(0, _period.Selected)].Hours);
        if (!reply.Ok) { _status.Text = reply.Message; _status.AddThemeColorOverride("font_color", IslandUi.Error); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement d)
    {
        _last = d;
        var running = d.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.True; var fresh = d.TryGetProperty("fresh", out var f) && f.ValueKind == JsonValueKind.True;
        _status.Text = !running ? "O servidor da ilha está desligado: só aparece o histórico." : !fresh ? "O servidor ainda não mandou as medidas (elas chegam a cada 15 segundos)." : "Medidas do último sinal do servidor.";
        _status.AddThemeColorOverride("font_color", IslandUi.TextMuted);
        var current = d.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        var scripts = d.TryGetProperty("scripts", out var s) ? s : default;
        if (current.ValueKind == JsonValueKind.Object)
        {
            var fps = Num(current, "fps"); var tick = Num(current, "tickRate"); var ratio = tick > 0 ? fps / tick : 1;
            _values[0].Text = One(fps); _notes[0].Text = tick > 0 ? $"de {One(tick)} esperados" : ""; _values[0].AddThemeColorOverride("font_color", ratio < .5 ? IslandUi.Error : ratio < .9 ? IslandUi.AccentSecondary : IslandUi.Success);
            _values[1].Text = One(tick); _notes[1].Text = "ciclos de física por segundo";
            _values[2].Text = One(Num(current, "processMs")) + " ms"; _notes[2].Text = "tempo de cada quadro";
            _values[3].Text = One(Num(current, "memoryMb")) + " MB"; _notes[3].Text = "memória do servidor";
            _values[4].Text = $"{(int)Num(current, "players")} / {(int)Num(current, "maxPlayers")}"; _notes[4].Text = "presentes agora";
        }
        else foreach (var i in new[] { 0, 1, 2, 3, 4 }) { _values[i].Text = "—"; _notes[i].Text = ""; _values[i].AddThemeColorOverride("font_color", IslandUi.TextPrimary); }
        _values[5].Text = ((int)Num(scripts, "active")).ToString(Pt); _notes[5].Text = $"de {(int)Num(scripts, "total")} nesta ilha";

        var series = d.TryGetProperty("series", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        List<(DateTimeOffset, double)> Points(string key) => [.. series.Where(p => p.TryGetProperty("at", out var a) && a.TryGetDateTimeOffset(out _)).Select(p => (p.GetProperty("at").GetDateTimeOffset(), Num(p, key)))];
        _charts[0].SetData(Points("fps"), "", IslandUi.Success); _charts[1].SetData(Points("memoryMb"), "MB", IslandUi.AccentPrimary); _charts[2].SetData(Points("processMs"), "ms", IslandUi.AccentSecondary); _charts[3].SetData(Points("players"), "", new Color("#7FB3FF"));
        var summary = d.TryGetProperty("summary", out var sm) ? sm : default;
        string Note(string key, string unit) { var x = summary.ValueKind == JsonValueKind.Object && summary.TryGetProperty(key, out var v) ? v : default; return series.Count == 0 ? "Sem medidas neste período" : $"mín {One(Num(x, "min"))} · média {One(Num(x, "avg"))} · máx {One(Num(x, "max"))}{(unit.Length > 0 ? " " + unit : "")}"; }
        _chartNotes[0].Text = Note("fps", ""); _chartNotes[1].Text = Note("memoryMb", "MB"); _chartNotes[2].Text = Note("processMs", "ms"); _chartNotes[3].Text = Note("players", "");
        ShowDiagnostics(d);
    }

    private void ShowDiagnostics(JsonElement d)
    {
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        var diagnostics = d.TryGetProperty("diagnostics", out var dg) ? dg : default; var filter = Filters[Math.Max(0, _filter.Selected)].Id; var shown = 0;
        if (filter != "object" && diagnostics.ValueKind == JsonValueKind.Object && diagnostics.TryGetProperty("scripts", out var scripts))
            foreach (var item in scripts.EnumerateArray())
            {
                var errors = (int)Num(item, "errors"); var name = $"{Text(item, "name")} · {Text(item, "script")}";
                Add(name, "Script", $"{Num(item, "instructions").ToString("N0", Pt)} instruções · {(int)Num(item, "runs")} execuções", errors > 0 ? $"{errors} com erro" : "Normal", errors > 0 ? IslandUi.Error : IslandUi.Success); shown++;
            }
        if (filter != "script" && diagnostics.ValueKind == JsonValueKind.Object && diagnostics.TryGetProperty("objects", out var objects))
            foreach (var item in objects.EnumerateArray())
            {
                var physical = item.TryGetProperty("physical", out var p) && p.ValueKind == JsonValueKind.True;
                Add($"{Text(item, "name")} ({Text(item, "ownerName")})", "Objeto", $"{((int)Num(item, "cost")).ToString("N0", Pt)} prims", physical ? "Física ativa" : "Estático", physical ? IslandUi.AccentSecondary : IslandUi.TextMuted); shown++;
            }
        _empty.Visible = shown == 0;
    }

    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private void Add(string name, string type, string use, string state, Color color)
    {
        var row = IslandUi.Row(8); row.CustomMinimumSize = new Vector2(0, 40);
        void Cell(string text, int ratio, Color? ink = null, bool bold = false) { var label = IslandUi.Text(text, 14, ink ?? IslandUi.TextSecondary, bold); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; label.ClipText = true; row.AddChild(label); }
        Cell(name, 4, IslandUi.TextPrimary, true); Cell(type, 1); Cell(use, 3); Cell(state, 2, color, true);
        _rows.AddChild(row); _rows.AddChild(IslandUi.Divider());
    }
}
