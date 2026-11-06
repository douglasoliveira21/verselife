using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>One deleted object that can still be brought back.</summary>
public sealed record LostInfo(Guid Id, string Name, string Shape, bool Linked, DateTimeOffset DeletedAt, DateTimeOffset ExpiresAt)
{
    public string Text => $"{Name} · {Shape}{(Linked ? " · conjunto" : string.Empty)}\napagado em {DeletedAt.LocalDateTime:dd/MM HH:mm} · some para sempre em {Math.Max(0, (int)Math.Ceiling((ExpiresAt - DateTimeOffset.UtcNow).TotalDays))} dia(s)";
}

/// <summary>
/// "Objetos apagados": the objects you deleted from the world and can still get back for 30 days. Recovering puts the chosen ones in the inventory as one construction
/// (in "Devolvidos e recuperados"), keeping the linksets; it never puts anything straight back in the world, so every rule about placing is checked when you place it.
/// </summary>
public partial class LostObjectsWindow : Node
{
    private FloatingWindow _window = null!;
    private ItemList _list = null!;
    private Label _note = null!;
    private Button _recover = null!, _all = null!;
    private readonly List<LostInfo> _lost = [];
    private Action? _changed; private string _keep = string.Empty;

    /// <summary>Tests: answers the requests without a network: (method, path, body) -> (status, body).</summary>
    public Func<string, string, string?, (int Status, string Body)>? Transport { get; set; }
    public bool Visible => _window.Visible;
    public IReadOnlyList<LostInfo> Lost => _lost;
    public string NoteForTesting => _note.Text;
    public bool RecoverEnabledForTesting => !_recover.Disabled;

    public override void _Ready() => Build();

    public void Open(Action? changed = null) { _changed = changed ?? _changed; _window.Visible = true; _window.MoveToFront(); Reload(); }
    public void Toggle(Action? changed = null) { if (_window.Visible) _window.Visible = false; else Open(changed); }

    private static Button Action(string text, UiStyle.ButtonKind kind, System.Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private void Build()
    {
        _window = new FloatingWindow("Objetos apagados", icon: "trash") { Name = "LostObjects", Visible = false };
        _window.Place(.5f, .5f, -280, -230, 280, 230);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        var intro = new Label { Text = "Objetos que você apagou do mundo. Escolha os que quer de volta: eles vão para o seu inventário, em “Devolvidos e recuperados”, e daí você os coloca onde quiser.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
        intro.AddThemeFontSizeOverride("font_size", 14); intro.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(intro);
        _list = new ItemList { SelectMode = ItemList.SelectModeEnum.Multi, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 220), MaxTextLines = 2 }; _list.AddThemeFontSizeOverride("font_size", 15); _list.MultiSelected += (_, _) => UpdateButtons(); _list.ItemSelected += _ => UpdateButtons(); content.AddChild(_list);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); content.AddChild(row);
        _recover = Action("Recuperar os escolhidos", UiStyle.ButtonKind.Primary, () => Recover(chosen: true)); _all = Action("Recuperar todos", UiStyle.ButtonKind.Secondary, () => Recover(chosen: false)); row.AddChild(_recover); row.AddChild(_all);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; _note.AddThemeFontSizeOverride("font_size", 14); _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(_note);
        AddChild(_window); UpdateButtons();
    }

    private void Send(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method.ToString().ToUpperInvariant(), path, body); done(status, text); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, string.Empty); return; }
        var http = new HttpRequest { Timeout = 20 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        if (http.Request($"{app.ApiBaseUrl}{path}", [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], method, body ?? string.Empty) != Error.Ok) { http.QueueFree(); done(0, string.Empty); }
    }

    public static IReadOnlyList<LostInfo> Parse(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json); if (d.RootElement.ValueKind != JsonValueKind.Object || !d.RootElement.TryGetProperty("objects", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
            return [.. list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && Guid.TryParse(S(e, "id"), out _)).Select(e => new LostInfo(Guid.Parse(S(e, "id")), S(e, "name"), S(e, "shape"), e.TryGetProperty("groupId", out var g) && g.ValueKind == JsonValueKind.String, DateTimeOffset.TryParse(S(e, "deletedAt"), out var at) ? at : DateTimeOffset.MinValue, DateTimeOffset.TryParse(S(e, "expiresAt"), out var ex) ? ex : DateTimeOffset.MinValue))];
        }
        catch (JsonException) { return []; }
    }

    public void Reload()
    {
        if (_keep.Length == 0) _note.Text = "Carregando…";
        Send(Godot.HttpClient.Method.Get, "/api/v1/inventory/lost", null, (status, body) =>
        {
            _lost.Clear(); _list.Clear();
            if (status is < 200 or >= 300) { _note.Text = status == 401 ? "Entre na sua conta para ver os objetos apagados." : $"Não foi possível ler a lista (HTTP {status})."; UpdateButtons(); return; }
            _lost.AddRange(Parse(body)); foreach (var item in _lost) _list.AddItem(item.Text);
            _note.Text = _keep.Length > 0 ? _keep : _lost.Count == 0 ? "Nenhum objeto apagado nos últimos 30 dias." : $"{_lost.Count} objeto(s) podem voltar."; _keep = string.Empty; UpdateButtons();
        });
    }

    private void UpdateButtons() { _recover.Disabled = _list.GetSelectedItems().Length == 0; _all.Disabled = _lost.Count == 0; }

    private void Recover(bool chosen)
    {
        var ids = (chosen ? _list.GetSelectedItems().Where(i => i < _lost.Count).Select(i => _lost[i].Id) : _lost.Select(l => l.Id)).Take(200).ToArray(); if (ids.Length == 0) return;
        Send(Godot.HttpClient.Method.Post, "/api/v1/inventory/lost/recover", JsonSerializer.Serialize(new { objectIds = ids }), (status, body) =>
        {
            if (status is >= 200 and < 300) { _changed?.Invoke(); _keep = $"{ids.Length} objeto(s) no seu inventário, em “Devolvidos e recuperados”."; Reload(); return; }
            var code = ItemRightsApi.ErrorCode(body);
            _note.Text = code switch { "already_recovered" => "Outra pessoa (ou outra janela sua) já recuperou esses objetos.", "not_found" => "Algum desses objetos não pode mais ser recuperado (já voltou, venceu o prazo ou não é seu). A lista foi atualizada.", "inventory_full" => "Seu inventário está cheio.", "invalid_recovery" => "Escolha de 1 a 200 objetos.", _ => $"Não foi possível recuperar (HTTP {status})." };
            _keep = _note.Text; Reload();
        });
    }


    public void SelectForTesting(params int[] rows) { _list.DeselectAll(); foreach (var row in rows) _list.Select(row, false); UpdateButtons(); }
    public void RecoverForTesting(bool chosen) => Recover(chosen);
}
