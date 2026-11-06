using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>One version of an asset as the server lists it.</summary>
public sealed record VersionInfo(int Version, DateTimeOffset At, long Size, string Notes, bool Current)
{
    public string Text => $"{(Current ? "★ " : "   ")}Versão {Version} · {At.LocalDateTime:dd/MM/yyyy HH:mm} · {(Size >= 1 << 20 ? (Size / 1048576d).ToString("0.0") + " MB" : (Size / 1024d).ToString("0") + " KB")}{(Current ? " · em uso" : string.Empty)}{(Notes.Length > 0 ? "\n   " + Notes.Replace("\n", " ") : string.Empty)}";
}

/// <summary>
/// "Versões": the versions of an asset. Whoever holds an item of it reads what changed in each version; the creator can also publish a new version (a file and a note) and go
/// back to an earlier one. Whatever version is in use is what everyone gets, so publishing changes the objects already placed in the world, which is why the creator is asked to say what changed.
/// </summary>
public partial class AssetVersionsWindow : Node
{
    private FloatingWindow _window = null!;
    private ItemList _list = null!;
    private Label _title = null!, _note = null!;
    private LineEdit _notes = null!;
    private Button _publish = null!, _use = null!;
    private FileDialog _files = null!;
    private readonly List<VersionInfo> _versions = [];
    private Guid _assetId, _itemId; private bool _isOwner; private Action? _changed;

    /// <summary>Tests: answers the requests without a network: (method, path, body) -> (status, body).</summary>
    public Func<string, string, string?, (int Status, string Body)>? Transport { get; set; }
    public bool Visible => _window.Visible;
    public IReadOnlyList<VersionInfo> Versions => _versions;
    public bool IsOwnerForTesting => _isOwner;
    public string NoteForTesting => _note.Text;
    public string TitleForTesting => _title.Text;
    public bool PublishVisibleForTesting => _publish.Visible;
    public bool UseEnabledForTesting => _use.Visible && !_use.Disabled;

    public override void _Ready() => Build();

    public void Open(Guid assetId, Guid itemId, string name, Action? changed = null)
    {
        _assetId = assetId; _itemId = itemId; _changed = changed ?? _changed; _title.Text = name; _window.Visible = true; _window.MoveToFront(); Reload(true);
    }

    private static Button Action(string text, UiStyle.ButtonKind kind, System.Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private void Build()
    {
        _window = new FloatingWindow("Versões", icon: "layers") { Name = "AssetVersions", Visible = false };
        _window.Place(.5f, .5f, -280, -240, 280, 240);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        _title = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; _title.AddThemeFontSizeOverride("font_size", 20); _title.AddThemeFontOverride("font", UiStyle.Bold); content.AddChild(_title);
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 200), MaxTextLines = 3 }; _list.AddThemeFontSizeOverride("font_size", 15); _list.ItemSelected += _ => UpdateButtons(); content.AddChild(_list);
        _notes = new LineEdit { PlaceholderText = "O que mudou nesta versão (para quem tem o item)…", MaxLength = 400 }; content.AddChild(_notes);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); content.AddChild(row);
        _publish = Action("Publicar nova versão…", UiStyle.ButtonKind.Primary, () => _files.PopupCentered(new Vector2I(800, 500))); _use = Action("Usar a versão escolhida", UiStyle.ButtonKind.Secondary, UseChosen); row.AddChild(_publish); row.AddChild(_use);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; _note.AddThemeFontSizeOverride("font_size", 14); _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(_note);
        _files = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.glb,*.gltf,*.png,*.jpg,*.jpeg,*.webp,*.ogg,*.wav"], Title = "Escolher o arquivo da nova versão" }; AddChild(_files); _files.FileSelected += Publish;
        AddChild(_window); UpdateButtons();
    }

    private void Send(Godot.HttpClient.Method method, string path, string? body, byte[]? raw, string[]? headers, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method.ToString().ToUpperInvariant(), path, raw is null ? body : Encoding.Latin1.GetString(raw)); done(status, text); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, string.Empty); return; }
        var http = new HttpRequest { Timeout = 60 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        var header = headers ?? [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"];
        var error = raw is not null ? http.RequestRaw($"{app.ApiBaseUrl}{path}", header, method, raw) : http.Request($"{app.ApiBaseUrl}{path}", header, method, body ?? string.Empty);
        if (error != Error.Ok) { http.QueueFree(); done(0, string.Empty); }
    }

    public static (IReadOnlyList<VersionInfo> Versions, bool IsOwner, int Current) Parse(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json); var r = d.RootElement; if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("versions", out var list) || list.ValueKind != JsonValueKind.Array) return ([], false, 0);
            var versions = list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("version", out _)).Select(e => new VersionInfo(e.GetProperty("version").GetInt32(), e.TryGetProperty("at", out var at) && DateTimeOffset.TryParse(at.GetString(), out var when) ? when : DateTimeOffset.MinValue, e.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var size) ? size : 0, e.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? string.Empty : string.Empty, e.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.True)).ToList();
            return (versions, r.TryGetProperty("isOwner", out var o) && o.ValueKind == JsonValueKind.True, r.TryGetProperty("current", out var cur) && cur.TryGetInt32(out var number) ? number : 0);
        }
        catch (JsonException) { return ([], false, 0); }
    }

    private string _sticky = string.Empty;

    public void Reload(bool acknowledge = false)
    {
        if (_sticky.Length == 0) _note.Text = "Carregando…";
        Send(Godot.HttpClient.Method.Get, $"/api/v1/assets/{_assetId}/versions", null, null, null, (status, body) =>
        {
            _versions.Clear(); _list.Clear(); var message = _sticky; _sticky = string.Empty;
            if (status is < 200 or >= 300) { _note.Text = status == 404 ? "Não há histórico de versões para este item." : $"Não foi possível ler as versões (HTTP {status})."; UpdateButtons(); return; }
            var (versions, isOwner, current) = Parse(body); _versions.AddRange(versions); _isOwner = isOwner; foreach (var version in _versions) _list.AddItem(version.Text);
            _note.Text = message.Length > 0 ? message : _versions.Count <= 1 ? "Só existe a versão original." : isOwner ? $"A versão {current} está em uso por todos. Escolha uma para voltar a ela, ou publique uma nova." : $"A versão {current} é a que você tem. Leia o que mudou em cada uma.";
            UpdateButtons();
            if (acknowledge && !isOwner && _itemId != Guid.Empty) Send(Godot.HttpClient.Method.Post, $"/api/v1/inventory/items/{_itemId}/ack-update", null, null, null, (_, _) => _changed?.Invoke()); // looking at the versions is seeing the update
        });
    }

    private void UpdateButtons() { _publish.Visible = _isOwner; _notes.Visible = _isOwner; _use.Visible = _isOwner; _use.Disabled = _list.GetSelectedItems() is not { Length: > 0 } picked || picked[0] >= _versions.Count || _versions[picked[0]].Current; }

    private void UseChosen()
    {
        if (_list.GetSelectedItems() is not { Length: > 0 } picked || picked[0] >= _versions.Count) return; var version = _versions[picked[0]]; if (version.Current) return;
        Send(Godot.HttpClient.Method.Post, $"/api/v1/assets/{_assetId}/versions/{version.Version}/activate", null, null, null, (status, body) =>
        {
            if (status is >= 200 and < 300) { _sticky = $"Agora todos usam a versão {version.Version}."; _changed?.Invoke(); Reload(); return; }
            _note.Text = Explain(ItemRightsApi.ErrorCode(body), status);
        });
    }

    private void Publish(string path)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(path); var filename = System.IO.Path.GetFileName(path);
        if (bytes.Length == 0 || bytes.Length > 100 * 1024 * 1024) { _note.Text = "O arquivo precisa ter de 1 byte a 100 MB."; return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var boundary = "----LifeVerse" + Guid.NewGuid().ToString("N");
        var head = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=notes\r\n\r\n{_notes.Text.Trim()}\r\n--{boundary}\r\nContent-Disposition: form-data; name=file; filename=\"{filename}\"\r\nContent-Type: application/octet-stream\r\n\r\n"); var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
        var payload = new byte[head.Length + bytes.Length + tail.Length]; Buffer.BlockCopy(head, 0, payload, 0, head.Length); Buffer.BlockCopy(bytes, 0, payload, head.Length, bytes.Length); Buffer.BlockCopy(tail, 0, payload, head.Length + bytes.Length, tail.Length);
        PublishPayload(payload, boundary, session?.AccessToken ?? string.Empty);
    }

    private void PublishPayload(byte[] payload, string boundary, string token)
    {
        _note.Text = "Publicando a nova versão…";
        Send(Godot.HttpClient.Method.Post, $"/api/v1/assets/{_assetId}/versions", null, payload, [$"Authorization: Bearer {token}", $"Content-Type: multipart/form-data; boundary={boundary}"], (status, body) =>
        {
            if (status is >= 200 and < 300) { _sticky = "Nova versão publicada: quem tem o item foi avisado."; _notes.Text = string.Empty; _changed?.Invoke(); Reload(); return; }
            _note.Text = Explain(ItemRightsApi.ErrorCode(body), status);
        });
    }

    private static string Explain(string code, int status) => code switch
    {
        "wrong_kind" => "O arquivo precisa ser do mesmo tipo do original (modelo com modelo, imagem com imagem, som com som).", "invalid_file" => "O arquivo não passou na conferência (formato, tamanho ou conteúdo).", "unchanged" => "Esse arquivo é igual ao da versão em uso.",
        "version_limit" => "Chegou ao limite de 50 versões deste item.", "not_versionable" => "Este tipo de item não tem versões.", "stale" => "Outra versão foi publicada ao mesmo tempo; leia a lista de novo e tente outra vez.", "not_found" => "Só quem criou o item pode mudar as versões.",
        _ => $"Não foi possível concluir (HTTP {status})."
    };

    public void SelectForTesting(int row) { _list.Select(row); UpdateButtons(); }
    public void UseForTesting() => UseChosen();
    public void PublishForTesting(byte[] file, string name, string notes)
    {
        var boundary = "----test"; var head = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=notes\r\n\r\n{notes}\r\n--{boundary}\r\nContent-Disposition: form-data; name=file; filename=\"{name}\"\r\n\r\n"); var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
        PublishPayload([.. head, .. file, .. tail], boundary, string.Empty);
    }
}
