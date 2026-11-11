using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Terreno tab: what the ground of the island looks like (a small drawing and its figures), named backups of it, and restoring one. The island server saves the terrain by itself about once a minute;
/// a backup keeps one of those saves under a name. Restoring is a restart (the server holds the terrain in memory), so it is scheduled like one, with warnings to the people on the island; objects, people
/// and the look of the island are not touched.
/// </summary>
public partial class IslandTerrainPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Text, string Mode, int? Minutes)[] When = [("Agora", "now", null), ("Em 1 minuto", "in", 1), ("Em 5 minutos", "in", 5), ("Em 10 minutos", "in", 10)];

    private TextureRect _picture = null!;
    private Label _version = null!, _saved = null!, _size = null!, _heights = null!, _sea = null!, _hash = null!, _restoring = null!, _status = null!, _empty = null!, _count = null!;
    private LineEdit _name = null!;
    private OptionButton _when = null!;
    private Button _create = null!;
    private VBoxContainer _rows = null!;
    private IslandManagementApi? _api;
    private Guid _island;
    private bool _running;
    private List<(Guid Id, string Name, bool Valid)> _backups = [];

    public event Action<string>? Notify;
    public Action<string, string, Action>? Ask { get; set; }

    public string StatusForTesting => _status.Text;
    public string InfoForTesting => $"{_version.Text}|{_size.Text}|{_heights.Text}|{_sea.Text}";
    public bool PictureShownForTesting => _picture.Texture is not null;
    public string RestoringForTesting => _restoring.Visible ? _restoring.Text : "";
    public int BackupsForTesting => _backups.Count;
    public bool CreateEnabledForTesting => !_create.Disabled;
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text))).Where(t => t.Length > 0)];
    public string NameForTesting { get => _name.Text; set => _name.Text = value; }
    public void SelectWhenForTesting(int index) => _when.Select(index);
    public Task CreateForTesting() => CreateAsync();
    public Task RestoreFirstForTesting() => _backups.Count == 0 ? Task.CompletedTask : RestoreAsync(_backups[0].Id, _backups[0].Name);
    public Task DeleteFirstForTesting() => _backups.Count == 0 ? Task.CompletedTask : DeleteAsync(_backups[0].Id, _backups[0].Name);

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        column.AddChild(BuildCurrent());
        _restoring = IslandUi.Text("", 14, IslandUi.AccentSecondary, true, true); _restoring.Visible = false; column.AddChild(_restoring);
        column.AddChild(BuildBackups());
        column.AddChild(IslandUi.Text("Ainda não existem importar e exportar mapa de alturas, trocar texturas nem recalcular colisões por aqui: o servidor da ilha não oferece isso para quem administra de fora. O desenho e as alturas são os do último salvamento automático (cerca de uma vez por minuto).", 13, IslandUi.TextMuted, false, true));
    }

    private Control BuildCurrent()
    {
        var (card, body) = IslandUi.Card("Terreno Atual", "mountain");
        var row = IslandUi.Row(18); body.AddChild(row);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(220, 220), SizeFlagsVertical = SizeFlags.ShrinkBegin, ClipContents = true }; frame.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, 9, 1));
        _picture = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore }; frame.AddChild(_picture); row.AddChild(frame);
        var info = IslandUi.Column(8); info.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(info);
        Label Line(string caption) { var r = IslandUi.Row(8); var c = IslandUi.Text(caption, 14, IslandUi.TextSecondary); c.CustomMinimumSize = new Vector2(150, 0); r.AddChild(c); var v = IslandUi.Text("—", 14, IslandUi.TextPrimary, true, true); r.AddChild(v); info.AddChild(r); return v; }
        _version = Line("Versão salva:"); _saved = Line("Salvo:"); _size = Line("Tamanho:"); _heights = Line("Altura (mín / média / máx):"); _sea = Line("Nível do mar:"); _hash = Line("Verificação:");
        return card;
    }

    private Control BuildBackups()
    {
        var (card, body) = IslandUi.Card("Backups do Terreno", "folder");
        var form = IslandUi.Row(10); body.AddChild(form);
        _name = IslandUi.Input("Nome do backup (ex.: Antes da festa)", 80); _name.TextSubmitted += _t => _ = CreateAsync(); form.AddChild(_name);
        _create = IslandUi.Action("Criar Backup", "plus", IslandUi.Kind.Primary, 40); _create.Pressed += () => _ = CreateAsync(); form.AddChild(_create);
        var when = IslandUi.Row(10); body.AddChild(when); when.AddChild(IslandUi.Text("Ao restaurar, reiniciar:", 14, IslandUi.TextSecondary));
        _when = IslandUi.Dropdown(When.Select((w, i) => (w.Text, i.ToString()))); _when.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; _when.CustomMinimumSize = new Vector2(170, 40); when.AddChild(_when);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; when.AddChild(_status);
        var head = IslandUi.Row(8); head.AddChild(Head("Nome", 3)); head.AddChild(Head("Versão", 1)); head.AddChild(Head("Salvo em", 2)); head.AddChild(Head("Criado por", 2)); head.AddChild(Head("Alturas", 2)); head.AddChild(Head("Ações", 3, true));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); body.AddChild(_rows);
        _empty = IslandUi.Text("Nenhum backup ainda. Crie um antes de uma grande mudança no relevo.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 56); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        _count = IslandUi.Text("", 14, IslandUi.TextSecondary); body.AddChild(_count);
        return card;
    }

    private static Label Head(string text, int ratio, bool end = false) { var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (end) label.HorizontalAlignment = HorizontalAlignment.Right; return label; }

    private void Confirm(string text, string ok, Action action) { if (Ask is { } ask) ask(text, ok, action); else action(); }

    // ---- data -------------------------------------------------------------------------------------------------------------

    private static double Num(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) ? n : 0;
    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string Ago(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? $"há {Duration(Math.Max(0, (long)(DateTimeOffset.UtcNow - d).TotalSeconds))} ({d.ToLocalTime():dd/MM HH:mm})" : "—";
    private static string Duration(long s) => s < 60 ? $"{s} s" : s < 3600 ? $"{s / 60} min" : s < 86400 ? $"{s / 3600} h" : $"{s / 86400} d";
    private static string Meters(double v) => v.ToString("0.#", Pt) + " m";

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.TerrainGet(_island);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement d)
    {
        _running = d.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.True;
        var current = d.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        if (current.ValueKind == JsonValueKind.Object)
        {
            var stats = current.TryGetProperty("stats", out var s) ? s : default;
            _version.Text = ((long)Num(current, "version")).ToString(Pt); _saved.Text = Ago(current, "savedAt"); _hash.Text = Str(current, "hash");
            if (stats.ValueKind == JsonValueKind.Object) { _size.Text = $"{Num(stats, "sizeMeters").ToString("0", Pt)} × {Num(stats, "sizeMeters").ToString("0", Pt)} m ({(int)Num(stats, "cells")} células de {Num(stats, "cellSize").ToString("0.#", Pt)} m)"; _heights.Text = $"{Meters(Num(stats, "minHeight"))} / {Meters(Num(stats, "avgHeight"))} / {Meters(Num(stats, "maxHeight"))}"; _sea.Text = Meters(Num(stats, "seaLevel")); }
            else { _size.Text = "—"; _heights.Text = "—"; _sea.Text = "—"; }
            DrawPicture(current.TryGetProperty("preview", out var preview) ? preview : default);
        }
        else { _version.Text = "—"; _saved.Text = "O servidor da ilha ainda não salvou o terreno"; _size.Text = "—"; _heights.Text = "—"; _sea.Text = "—"; _hash.Text = "—"; _picture.Texture = null; }
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        var backups = d.TryGetProperty("backups", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        _backups = [.. backups.Select(b => (Guid.Parse(Str(b, "id")), Str(b, "name"), b.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True))];
        foreach (var backup in backups) _rows.AddChild(BuildRow(backup));
        _empty.Visible = backups.Count == 0; var max = d.TryGetProperty("limits", out var l) ? (int)Num(l, "maxBackups") : 10; _count.Text = $"{backups.Count} de {max} backups"; _create.Disabled = backups.Count >= max;
        var restoring = d.TryGetProperty("restoring", out var rs) && rs.ValueKind == JsonValueKind.Object ? rs : default;
        _restoring.Visible = restoring.ValueKind == JsonValueKind.Object;
        if (_restoring.Visible) _restoring.Text = Str(restoring, "status") == "running" ? "Restauração em andamento: a ilha está reiniciando com o terreno do backup." : $"Restauração agendada para {(DateTimeOffset.TryParse(Str(restoring, "scheduledFor"), out var when) ? when.ToLocalTime().ToString("dd/MM HH:mm", Pt) : "")}. Acompanhe ou cancele na aba Reinicialização.";
    }

    /// <summary>Draws the small grid of heights: deep water blue, shallow water lighter, sand, grass, rock and snow by height.</summary>
    private void DrawPicture(JsonElement preview)
    {
        if (preview.ValueKind != JsonValueKind.Object || !preview.TryGetProperty("heights", out var heights) || heights.ValueKind != JsonValueKind.Array) { _picture.Texture = null; return; }
        var n = (int)Num(preview, "size"); var values = heights.EnumerateArray().Select(h => h.TryGetDouble(out var x) ? x : 0).ToArray(); var sea = Num(preview, "seaLevel");
        if (n < 2 || values.Length < n * n) { _picture.Texture = null; return; }
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var z = 0; z < n; z++) for (var x = 0; x < n; x++)
        {
            var h = values[z * n + x]; Color color;
            if (h < sea) color = new Color("#0B3C5D").Lerp(new Color("#3FA7D6"), (float)Math.Clamp((h - (sea - 8)) / 8, 0, 1));
            else if (h < sea + 1.2) color = new Color("#E3D39A");
            else if (h < 12) color = new Color("#3F8F4F").Lerp(new Color("#2E6B3A"), (float)Math.Clamp((h - sea) / 12, 0, 1));
            else if (h < 28) color = new Color("#8A7A66");
            else color = new Color("#F2F6F8");
            image.SetPixel(x, z, color);
        }
        _picture.Texture = ImageTexture.CreateFromImage(image);
    }

    private Control BuildRow(JsonElement item)
    {
        var id = Guid.Parse(Str(item, "id")); var name = Str(item, "name"); var valid = item.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        var stats = item.TryGetProperty("stats", out var s) ? s : default;
        var row = IslandUi.Row(8); row.CustomMinimumSize = new Vector2(0, 48);
        void Cell(string text, int ratio, Color? ink = null, bool bold = false) { var label = IslandUi.Text(text, 14, ink ?? IslandUi.TextSecondary, bold); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; label.ClipText = true; row.AddChild(label); }
        Cell(valid ? name : name + " (ilegível)", 3, valid ? IslandUi.TextPrimary : IslandUi.Error, true); Cell(((long)Num(item, "version")).ToString(Pt), 1);
        Cell(item.TryGetProperty("savedAt", out var at) && at.TryGetDateTimeOffset(out var when) ? when.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : "—", 2); Cell(Str(item, "by"), 2);
        Cell(stats.ValueKind == JsonValueKind.Object ? $"{Num(stats, "minHeight").ToString("0", Pt)} a {Num(stats, "maxHeight").ToString("0", Pt)} m" : "—", 2);
        var actions = IslandUi.Row(6); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.SizeFlagsStretchRatio = 3; actions.Alignment = BoxContainer.AlignmentMode.End; row.AddChild(actions);
        var restore = IslandUi.Action("Restaurar", "undo", IslandUi.Kind.Orange, 34); restore.Disabled = !valid; restore.TooltipText = valid ? "Põe o terreno deste backup de volta (reinicia a ilha)." : "Esse backup não pode ser lido.";
        restore.Pressed += () => Confirm(RestoreText(name), "Restaurar", () => _ = RestoreAsync(id, name)); actions.AddChild(restore);
        var delete = IslandUi.IconButton("trash", "Excluir o backup (o terreno atual não muda)", 34); delete.Pressed += () => Confirm($"Excluir o backup “{name}”? O terreno atual não muda.", "Excluir", () => _ = DeleteAsync(id, name)); actions.AddChild(delete);
        _rows.AddChild(row); _rows.AddChild(IslandUi.Divider());
        return row;
    }

    private string RestoreText(string name)
    {
        var option = When[Math.Max(0, _when.Selected)];
        return _running
            ? $"Restaurar o terreno do backup “{name}” {(option.Mode == "now" ? "agora" : $"em {option.Minutes} minuto(s)")}? A ilha reinicia, quem está nela é avisado e desconectado, e tudo o que foi esculpido depois do backup se perde. Objetos e pessoas não mudam."
            : $"Restaurar o terreno do backup “{name}”? Ninguém está na ilha agora, então vale na hora. Tudo o que foi esculpido depois do backup se perde. Objetos não mudam.";
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------

    public async Task CreateAsync()
    {
        if (_api is null) return;
        var name = _name.Text.Trim(); if (name.Length == 0) { Say("Dê um nome ao backup.", true); return; }
        _create.Disabled = true; var reply = await _api.TerrainBackup(_island, name);
        if (!reply.Ok) { Say(reply.Message, true); _create.Disabled = false; return; }
        _name.Text = ""; Show(reply.Body); Say($"Backup “{name}” criado a partir do último salvamento do terreno.", false); Notify?.Invoke("Backup do terreno criado.");
    }

    public async Task DeleteAsync(Guid id, string name)
    {
        if (_api is null) return;
        var reply = await _api.TerrainBackupDelete(_island, id);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Say($"Backup “{name}” excluído.", false); await LoadAsync();
    }

    public async Task RestoreAsync(Guid id, string name)
    {
        if (_api is null) return;
        var option = When[Math.Max(0, _when.Selected)]; var reply = await _api.TerrainRestore(_island, id, option.Mode, option.Minutes);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        var applied = reply.Body.TryGetProperty("applied", out var a) && a.ValueKind == JsonValueKind.True;
        Say(applied ? $"Terreno do backup “{name}” restaurado." : option.Mode == "now" ? "Restauração pedida: o servidor recebe o pedido em até 15 segundos e a ilha reinicia." : "Restauração agendada. Quem está na ilha será avisado.", false); Notify?.Invoke("Restauração do terreno solicitada."); await LoadAsync();
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
