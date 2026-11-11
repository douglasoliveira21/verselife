using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>The window where a picture is chosen for a map: the person's own textures (from the inventory), the patterns of the system, and "Importar imagem…" to bring a new one in.</summary>
public partial class BuildTexturePicker : Node
{
    public sealed record Entry(string? Asset, string? Builtin, string Name);

    private ConfirmationDialog _dialog = null!;
    private ItemList _list = null!;
    private TextureRect _preview = null!;
    private Label _name = null!, _status = null!;
    private Button _mine = null!, _system = null!;
    private FileDialog _files = null!;
    private HttpRequest? _http;
    private readonly List<Entry> _mineEntries = [], _shown = [];
    private int _source;
    private bool _allowBuiltin = true, _loaded;
    private Action<Entry>? _picked;

    public bool IsOpen => _dialog is not null && _dialog.Visible;
    public int CountForTesting => _shown.Count;
    public string StatusForTesting => _status.Text;
    public void SelectForTesting(int index) { _list.Select(index); OnSelected(index); }
    public void SourceForTesting(int source) => SelectSource(source);
    public void ConfirmForTesting() => Confirm();
    public void LoadForTesting(string json) => ReadItems(json);

    private TextureCache? Cache => GetParent()?.GetNodeOrNull<TextureCache>("TextureCache");

    public override void _Ready()
    {
        _dialog = new ConfirmationDialog { Title = "Escolher textura", OkButtonText = "Usar esta", CancelButtonText = "Cancelar", Exclusive = false, MinSize = new Vector2I(860, 520), Theme = ViewerTheme.Create() }; AddChild(_dialog); _dialog.Confirmed += Confirm;
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 8); _dialog.AddChild(body);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); body.AddChild(top);
        _mine = SourceButton("Minhas texturas", 0); _system = SourceButton("Texturas do sistema", 1); top.AddChild(_mine); top.AddChild(_system);
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; top.AddChild(spacer);
        var import = new Button { Text = " Importar imagem…", Icon = UiStyle.Icon("download", UiStyle.Text, 20), FocusMode = Control.FocusModeEnum.None, TooltipText = "Envia uma imagem PNG, JPG ou WebP (até 8 MB) ao seu inventário" }; UiStyle.StyleButton(import, UiStyle.ButtonKind.Secondary, 10, 5, 12); import.Pressed += () => _files.PopupCentered(new Vector2I(800, 500)); top.AddChild(import);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); row.SizeFlagsVertical = Control.SizeFlags.ExpandFill; body.AddChild(row);
        _list = new ItemList { MaxColumns = 5, SameColumnWidth = true, IconMode = ItemList.IconModeEnum.Top, FixedIconSize = new Vector2I(96, 76), FixedColumnWidth = 110, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single };
        _list.ItemSelected += i => OnSelected((int)i); _list.ItemActivated += _ => Confirm(); row.AddChild(_list);
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) }; row.AddChild(side);
        _preview = new TextureRect { CustomMinimumSize = new Vector2(240, 200), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true }; side.AddChild(_preview);
        _name = new Label { Text = "—" }; _name.AddThemeFontOverride("font", UiStyle.Bold); side.AddChild(_name);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); _status.AddThemeFontSizeOverride("font_size", 16); side.AddChild(_status);
        _files = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.png,*.jpg,*.jpeg,*.webp ; Imagens"], Title = "Importar textura" }; _files.FileSelected += ImportImage; AddChild(_files);
        _http = new HttpRequest { Timeout = 20 }; AddChild(_http); _http.RequestCompleted += OnCompleted;
    }

    private Button SourceButton(string text, int source)
    {
        var button = new Button { Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38) }; UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 12, 5, 12); button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Normal, 12, 5, 12));
        button.Pressed += () => SelectSource(source); return button;
    }

    /// <summary>Opens the window. <paramref name="allowBuiltin"/> says whether the system patterns can be used (only the base colour takes them).</summary>
    public void Open(bool allowBuiltin, Action<Entry> picked)
    {
        _picked = picked; _allowBuiltin = allowBuiltin; _system.Disabled = !allowBuiltin; if (!allowBuiltin && _source == 1) _source = 0;
        _dialog.GetOkButton().Disabled = true; _dialog.PopupCentered(new Vector2I(900, 560)); SelectSource(_source);
        if (!_loaded) Load();
    }

    private void SelectSource(int source)
    {
        _source = source; _mine.SetPressedNoSignal(source == 0); _system.SetPressedNoSignal(source == 1);
        _shown.Clear(); _list.Clear();
        if (source == 1) _shown.AddRange(BuiltInTextures.All.Select(x => new Entry(null, x.Id, x.Label))); else _shown.AddRange(_mineEntries);
        for (var i = 0; i < _shown.Count; i++)
        {
            var entry = _shown[i]; var slot = _list.AddItem(string.Empty, entry.Builtin is { } b ? BuiltInTextures.Get(b) : null, true); _list.SetItemTooltip(slot, entry.Name);
            if (entry.Asset is not null && Guid.TryParse(entry.Asset, out var asset)) Cache?.Request(asset, texture => { if (texture is not null && IsInstanceValid(_list) && slot < _list.ItemCount && slot < _shown.Count && _shown[slot] == entry) _list.SetItemIcon(slot, texture); });
        }
        _status.Text = _shown.Count == 0 ? (source == 0 ? (_loaded ? "Nenhuma textura ainda. Use Importar imagem." : "Carregando suas texturas…") : "") : $"{_shown.Count} textura(s).";
        _preview.Texture = null; _name.Text = "—"; _dialog.GetOkButton().Disabled = true;
    }

    private void OnSelected(int index)
    {
        if (index < 0 || index >= _shown.Count) return;
        var entry = _shown[index]; _name.Text = entry.Name; _dialog.GetOkButton().Disabled = false; _preview.Texture = null;
        if (entry.Builtin is { } b) _preview.Texture = BuiltInTextures.Get(b);
        else if (Guid.TryParse(entry.Asset, out var asset)) Cache?.Request(asset, texture => { if (texture is not null && IsInstanceValid(_preview) && _list.GetSelectedItems().Contains(index)) { _preview.Texture = texture; _status.Text = $"{texture.GetWidth()} × {texture.GetHeight()}"; } });
    }

    private void Confirm()
    {
        var picked = _list.GetSelectedItems(); if (picked.Length != 1 || picked[0] >= _shown.Count) return;
        var entry = _shown[picked[0]]; if (entry.Builtin is not null && !_allowBuiltin) return;
        _dialog.Hide(); _picked?.Invoke(entry);
    }

    // ---- the person's textures ---------------------------------------------------------------------------------------------

    private void Load()
    {
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is not { IsAuthenticated: true } || app is null || _http is null) { _status.Text = "Entre em uma ilha para ver suas texturas."; return; }
        if (_http.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return;
        _http.Request($"{app.ApiBaseUrl}/api/v1/inventory/items?category=textures&page=1&pageSize=100", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private void OnCompleted(long result, long code, string[] headers, byte[] body)
    {
        if (code is < 200 or >= 300) { _status.Text = $"Não foi possível carregar as texturas (HTTP {code})."; return; }
        try { ReadItems(body.GetStringFromUtf8()); } catch (JsonException) { _status.Text = "Resposta de texturas inválida."; }
    }

    /// <summary>Reads the inventory answer ({ items: [...] }) and keeps the entries that are images the renderer can show.</summary>
    private void ReadItems(string json)
    {
        _mineEntries.Clear(); _loaded = true;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("items", out var items))
            foreach (var item in items.EnumerateArray())
            {
                var mime = item.TryGetProperty("mimeType", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                if (!mime.StartsWith("image/", StringComparison.Ordinal) || mime == "image/svg+xml") continue;
                if (!item.TryGetProperty("assetId", out var asset) || !Guid.TryParse(asset.GetString(), out var assetId)) continue;
                _mineEntries.Add(new Entry(assetId.ToString(), null, item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "Textura" : "Textura"));
            }
        if (_source == 0) SelectSource(0);
    }

    private void ImportImage(string path)
    {
        var size = Godot.FileAccess.FileExists(path) ? (long)Godot.FileAccess.GetFileAsBytes(path).Length : 0;
        if (size == 0) { _status.Text = "Não foi possível ler o arquivo."; return; }
        if (size > 8L * 1024 * 1024) { _status.Text = "A imagem passa de 8 MB; reduza o tamanho antes de importar."; return; }
        var inventory = GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel");
        if (inventory is null) { _status.Text = "O inventário não está disponível."; return; }
        inventory.ItemsChanged += OnInventoryChanged; inventory.ImportTexture(path); _status.Text = "Enviando a imagem… ela aparece em Minhas texturas quando terminar.";
    }

    private void OnInventoryChanged() { if (GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel") is { } inventory) inventory.ItemsChanged -= OnInventoryChanged; Load(); }
}
