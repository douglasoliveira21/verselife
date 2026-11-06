using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// The global inventory (client/Assets/ui/03): a folder tree on the left, the items as cards on the right, a storage bar and a
/// drop zone for placing models in the world. Drag an item onto a folder to file it, or onto the world to place a 3D model there.
public partial class InventoryPanel : Node3D
{
    private const long QuotaBytes = 100L * 1024 * 1024 * 1024;
    private static readonly Color FolderYellow = new("#F8B84A");
    private static readonly Color FolderGrey = new("#B9BEC2");

    private sealed record ItemInfo(Guid ItemId, Guid AssetId, string Name, string Category, string Mime, string Hash, long Size, Guid? FolderId, bool Favorite, bool Deleted, int Version, int OwnerRights = 15, string Status = "active", bool HasUpdate = false, bool IsCreator = false);
    private sealed record FolderInfo(Guid Id, string Name, Guid? ParentId = null, int Count = -1);
    private enum Pending { List, Action, Used, Integrity, Unpack }

    private const int PageSize = 50;
    private static readonly (string Label, string Types)[] TypeFilters = [("Todos os tipos", ""), ("Modelos 3D", "models"), ("Texturas", "textures"), ("Sons", "sounds"), ("Animações", "animations"), ("Roupas e acessórios", "clothing,accessories"), ("Objetos", "objects"), ("Pacotes", "packages")];
    private static readonly (string Label, string Sort, string Dir)[] SortChoices = [("Ordem padrão", "", ""), ("Nome (A–Z)", "name", "asc"), ("Nome (Z–A)", "name", "desc"), ("Mais novos", "date", "desc"), ("Mais antigos", "date", "asc"), ("Maiores", "size", "desc"), ("Menores", "size", "asc"), ("Tipo", "type", "asc"), ("Usados há pouco", "recent", "desc")];

    private OptionButton _typeBox = null!, _sortBox = null!;
    private CheckBox _subfolders = null!;
    private Button _loadMore = null!;
    private HttpRequest _trashHttp = null!;
    private readonly List<Guid> _order = [];
    private readonly List<FolderInfo> _binFolders = [];
    private bool _recent, _returned, _updated, _hasMore;
    private FileDialog _zipDialog = null!; private LostObjectsWindow? _lostWindow;
    private int _page = 1, _total;
    private Guid _selectedBinFolder;
    private AssetPreview _preview = null!;
    public AssetPreview PreviewForTesting => _preview;

    private FloatingWindow _window = null!;
    private Tree _tree = null!;
    private LineEdit _search = null!, _folderName = null!;
    private Label _status = null!, _storageText = null!;
    private ProgressBar _storageBar = null!;
    private GridContainer _grid = null!;
    private Button _gridMode = null!, _listMode = null!;
    private ConfirmationDialog _newFolderDialog = null!;
    private HttpRequest _http = null!, _folderHttp = null!;
    private FileDialog _fileDialog = null!;
    private ModelImportDialog _import = null!;
    private ThumbnailLoader _thumbnails = null!;
    private readonly Dictionary<Guid, ItemInfo> _items = [];
    private readonly List<FolderInfo> _folders = [];
    private readonly List<ItemCard> _cards = [];
    private readonly Queue<(Godot.HttpClient.Method Method, string Path, string? Body, Pending Kind, string[]? Headers, byte[]? Raw)> _queue = new();
    private Pending _inFlight;
    private Guid _selectedItem;
    private Guid _selectedFolder;
    private bool _trash;
    private bool _favorites;
    private bool _listLayout;
    private bool _foldersLoaded;
    private bool _itemsLoaded;
    private ItemInfo? _dragItem;

    /// Lets tests capture requests instead of sending them: (method, path, body).
    public Func<Godot.HttpClient.Method, string, string?, bool>? RequestSink { get; set; }

    public override void _Ready()
    {
        _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnResponse;
        _folderHttp = new HttpRequest(); AddChild(_folderHttp); _folderHttp.RequestCompleted += OnFolders;
        _trashHttp = new HttpRequest(); AddChild(_trashHttp); _trashHttp.RequestCompleted += OnTrashFolders;
        _preview = new AssetPreview { Name = "AssetPreview" }; AddChild(_preview);
        _thumbnails = new ThumbnailLoader { Name = "Thumbnails" }; AddChild(_thumbnails);
        BuildUi();
    }

    public ModelImportDialog ImportDialogForTesting => _import;
    public bool IsOpen => _window.Visible;
    /// Reloads the list when the window is open (another window changed the inventory).
    public void Refresh() { if (_window.Visible) LoadItems(); }
    /// Raised when the server accepted a change (upload, move, favourite…), so other windows can refresh what they list.
    public event Action? ItemsChanged;
    /// Sends an image to the inventory as a texture (used by the build window's Importar imagem).
    public void ImportTexture(string path) => Upload(path);
    public void Toggle() { _window.Visible = !_window.Visible; if (_window.Visible) { _window.MoveToFront(); LoadItems(); } }

    public override void _Process(double delta)
    {
        // A drag that ended without a control accepting it, over the 3D world, places the item there.
        if (_dragItem is null || GetViewport().GuiIsDragging()) return;
        var item = _dragItem; _dragItem = null;
        if (!GetViewport().GuiIsDragSuccessful() && GetViewport().GuiGetHoveredControl() is null) PlaceInWorld(item, PointUnder(GetViewport().GetMousePosition()));
    }

    // ---- layout ----------------------------------------------------------------------------------------------

    private void BuildUi()
    {
        _window = new FloatingWindow("Inventário", icon: "bag", minimizable: true, expandable: true) { Name = "InventoryPanel", Visible = false };
        _window.Place(0, .5f, 18, -350, 798, 350);
        _window.ExpandToggled += expanded => _window.Place(expanded ? .5f : 0, .5f, expanded ? -560 : 18, expanded ? -450 : -370, expanded ? 560 : 798, expanded ? 450 : 370);
        var content = _window.Content;
        content.AddThemeConstantOverride("separation", 12);

        // search field and the two layout buttons, and the "+" menu with the less common actions
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 10); content.AddChild(top);
        var field = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 52) };
        var fieldNormal = UiStyle.Box(new Color(UiStyle.FieldFill, .55f), UiStyle.Border, 26, 1, 16, 0);
        var fieldFocus = UiStyle.Box(new Color(UiStyle.FieldFill, .55f), UiStyle.Accent, 26, 2, 16, 0);
        field.AddThemeStyleboxOverride("panel", fieldNormal);
        var inner = new HBoxContainer(); inner.AddThemeConstantOverride("separation", 12); field.AddChild(inner);
        inner.AddChild(UiStyle.IconRect("search", UiStyle.Text, 26));
        _search = new LineEdit { PlaceholderText = "Buscar no inventário...", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _search.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _search.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); _search.AddThemeFontSizeOverride("font_size", 20);
        _search.FocusEntered += () => field.AddThemeStyleboxOverride("panel", fieldFocus); _search.FocusExited += () => field.AddThemeStyleboxOverride("panel", fieldNormal);
        inner.AddChild(_search); top.AddChild(field);
        var group = new ButtonGroup();
        _gridMode = ModeButton("grid", "Cartões", group, true); _listMode = ModeButton("list", "Lista", group, false);
        top.AddChild(_gridMode); top.AddChild(_listMode);
        var plus = new MenuButton { Icon = UiStyle.Icon("plus", UiStyle.Text, 24), TooltipText = "Importar arquivo, nova pasta…", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(54, 52), Flat = false };
        StyleSquare(plus, false);
        var menu = plus.GetPopup();
        menu.AddItem("Importar modelo 3D…", 7); menu.AddItem("Importar arquivo…", 0); menu.AddItem("Nova pasta…", 1); menu.AddItem("Nova subpasta na pasta selecionada…", 11); menu.AddItem("Renomear pasta selecionada…", 12); menu.AddItem("Excluir pasta selecionada (vai para a lixeira)", 2); menu.AddSeparator();
        menu.AddItem("Pré-visualizar…", 16); menu.AddItem("Versões…", 21); menu.AddItem("Colocar no mundo", 3); menu.AddItem("Equipar", 4); menu.AddItem("Favoritar", 5); menu.AddItem("Excluir (vai para a lixeira)", 13); menu.AddItem("Restaurar da lixeira", 6); menu.AddItem("Excluir definitivamente", 14); menu.AddItem("Esvaziar a lixeira", 15); menu.AddSeparator(); menu.AddItem("Verificar duplicações e itens presos", 17); menu.AddItem("Corrigir itens presos por ofertas", 18); menu.AddSeparator(); menu.AddItem("Importar pacote (.zip)…", 19); menu.AddItem("Objetos apagados…", 20);
        menu.IdPressed += OnMenu;
        top.AddChild(plus);

        // folders on the left, items on the right
        var body = new HBoxContainer(); body.AddThemeConstantOverride("separation", 12); body.SizeFlagsVertical = Control.SizeFlags.ExpandFill; content.AddChild(body);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) }; left.AddThemeConstantOverride("separation", 10); body.AddChild(left);
        _tree = new Tree { HideRoot = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(250, 360), DropModeFlags = (int)Tree.DropModeFlagsEnum.OnItem, HideFolding = false };
        _tree.AddThemeFontSizeOverride("font_size", 19); _tree.AddThemeConstantOverride("v_separation", 6); _tree.AddThemeConstantOverride("item_margin", 18);
        var clear = new StyleBoxEmpty();
        _tree.AddThemeStyleboxOverride("panel", clear); _tree.AddThemeStyleboxOverride("focus", clear);
        _tree.AddThemeStyleboxOverride("selected", UiStyle.Box(new Color(UiStyle.AccentFillStrong, .85f), new Color(0, 0, 0, 0), 10, 0, 6, 4)); _tree.AddThemeStyleboxOverride("selected_focus", UiStyle.Box(new Color(UiStyle.AccentFillStrong, .85f), new Color(0, 0, 0, 0), 10, 0, 6, 4));
        _tree.AddThemeStyleboxOverride("cursor", clear); _tree.AddThemeStyleboxOverride("cursor_unfocused", clear);
        _tree.ItemSelected += OnSelected;
        _tree.SetDragForwarding(Callable.From<Vector2, Variant>(FolderDragData), Callable.From<Vector2, Variant, bool>(CanDrop), Callable.From<Vector2, Variant>(Drop));
        left.AddChild(_tree);
        _status = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) };
        _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); _status.AddThemeFontSizeOverride("font_size", 14);
        left.AddChild(_status);
        left.AddChild(new HSeparator());
        _storageText = new Label(); _storageText.AddThemeFontSizeOverride("font_size", 18); left.AddChild(_storageText);
        _storageBar = new ProgressBar { ShowPercentage = false, MaxValue = 100, CustomMinimumSize = new Vector2(0, 14) }; left.AddChild(_storageBar);

        body.AddChild(new VSeparator());
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 12); body.AddChild(right);
        var filters = new HBoxContainer(); filters.AddThemeConstantOverride("separation", 8); right.AddChild(filters);
        _typeBox = new OptionButton { FocusMode = Control.FocusModeEnum.None, TooltipText = "Mostrar só um tipo de item", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var (label, _) in TypeFilters) _typeBox.AddItem(label); _typeBox.AddThemeFontSizeOverride("font_size", 17); filters.AddChild(_typeBox);
        _sortBox = new OptionButton { FocusMode = Control.FocusModeEnum.None, TooltipText = "Ordenar a lista", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var (label, _, _) in SortChoices) _sortBox.AddItem(label); _sortBox.AddThemeFontSizeOverride("font_size", 17); filters.AddChild(_sortBox);
        _subfolders = new CheckBox { Text = "Com subpastas", TooltipText = "Ao escolher uma pasta, incluir o que está nas pastas dentro dela", FocusMode = Control.FocusModeEnum.None, ButtonPressed = true }; _subfolders.AddThemeFontSizeOverride("font_size", 16); filters.AddChild(_subfolders);
        _typeBox.ItemSelected += _ => LoadItems(); _sortBox.ItemSelected += _ => LoadItems(); _subfolders.Toggled += _ => { if (_selectedFolder != Guid.Empty) LoadItems(); };
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(500, 0) };
        _grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _grid.AddThemeConstantOverride("h_separation", 12); _grid.AddThemeConstantOverride("v_separation", 12);
        var gridColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; gridColumn.AddThemeConstantOverride("separation", 10); gridColumn.AddChild(_grid);
        _loadMore = new Button { Text = "Carregar mais", Visible = false, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44) }; _loadMore.Pressed += () => LoadItems(_page + 1); gridColumn.AddChild(_loadMore);
        scroll.AddChild(gridColumn); right.AddChild(scroll);
        right.AddChild(new DropZone(this));

        AddChild(_window);
        _newFolderDialog = new ConfirmationDialog { Title = "Nova pasta", OkButtonText = "Criar", CancelButtonText = "Cancelar", Exclusive = true };
        _folderName = new LineEdit { PlaceholderText = "Nome da pasta", CustomMinimumSize = new Vector2(280, 0) };
        _newFolderDialog.AddChild(_folderName); _newFolderDialog.Confirmed += CreateFolder; _folderName.TextSubmitted += _ => { _newFolderDialog.Hide(); CreateFolder(); };
        AddChild(_newFolderDialog);
        _fileDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.glb,*.gltf,*.png,*.jpg,*.jpeg,*.webp,*.ogg,*.wav"] };
        AddChild(_fileDialog);
        _zipDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.zip"], Title = "Importar pacote de arquivos" }; AddChild(_zipDialog); _zipDialog.FileSelected += UploadZip;
        _fileDialog.FileSelected += Upload;
        _import = new ModelImportDialog { Name = "ModelImport" }; AddChild(_import); _import.AttachTo(this); _import.Confirmed += Upload;
        var debounce = new Godot.Timer { OneShot = true, WaitTime = .3 }; AddChild(debounce); debounce.Timeout += () => LoadItems(); _search.TextChanged += _ => debounce.Start(); // wait for a pause in typing before asking the server
        _gridMode.Pressed += () => { _listLayout = false; RebuildCards(); }; _listMode.Pressed += () => { _listLayout = true; RebuildCards(); };
        UpdateStorage();
    }

    private static Button ModeButton(string icon, string tooltip, ButtonGroup group, bool pressed)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 26), ToggleMode = true, ButtonGroup = group, ButtonPressed = pressed, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(62, 52), ExpandIcon = false };
        StyleSquare(button, true);
        return button;
    }

    private static void StyleSquare(Button button, bool selectable)
    {
        const float mx = 10, my = 6;
        button.AddThemeStyleboxOverride("normal", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Normal, mx, my, 12));
        button.AddThemeStyleboxOverride("hover", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Hover, mx, my, 12));
        var kind = selectable ? UiStyle.ButtonKind.ToolSelected : UiStyle.ButtonKind.Tool;
        button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(kind, selectable ? UiStyle.ButtonState.Normal : UiStyle.ButtonState.Pressed, mx, my, 12));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.ButtonBox(kind, selectable ? UiStyle.ButtonState.Hover : UiStyle.ButtonState.Pressed, mx, my, 12));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    private void OnMenu(long id)
    {
        switch (id)
        {
            case 0: _fileDialog.PopupCentered(new Vector2I(800, 500)); break;
            case 7: _import.Open(); break;
            case 1: _folderMode = FolderMode.Top; OpenFolderDialog("Nova pasta", "Criar"); break;
            case 2: DeleteFolder(); break;
            case 3: Place(); break;
            case 4: Equip(); break;
            case 5: Favorite(); break;
            case 6: RestoreFromBin(); break;
            case 11: _folderMode = FolderMode.Sub; OpenFolderDialog("Nova subpasta", "Criar"); break;
            case 12: _folderMode = FolderMode.Rename; OpenFolderDialog("Renomear pasta", "Renomear"); break;
            case 13: DeleteItem(); break;
            case 14: PurgeSelected(); break;
            case 15: EmptyTrash(); break;
            case 16: PreviewSelected(); break;
            case 17: Send(Godot.HttpClient.Method.Get, ItemRightsApi.IntegrityPath, null, Pending.Integrity); _status.Text = "Conferindo duplicações e itens presos…"; break;
            case 19: _zipDialog.PopupCentered(new Vector2I(800, 500)); break;
            case 20: OpenLost(); break;
            case 21: OpenVersions(); break;
            case 18: Send(Godot.HttpClient.Method.Post, ItemRightsApi.RepairPath, null, Pending.Action); _status.Text = "Corrigindo o que dá para corrigir sozinho…"; break;
            case 8: OpenRights(false); break;
            case 9: OpenRights(true); break;
            case 10: MakeCopy(); break;
        }
    }

    // ---- data ------------------------------------------------------------------------------------------------

    /// The address of the list for what is chosen on the screen: the section, the folder, the search, the type and the order. The server does the filtering and the ordering, a page at a time.
    private string ItemsUrl(int page)
    {
        var view = _trash ? "trash" : _favorites ? "favorites" : _recent ? "recent" : _returned ? "returned" : _updated ? "updated" : "all";
        var url = $"/api/v1/inventory/items?view={view}&page={page}&pageSize={PageSize}";
        if (!string.IsNullOrWhiteSpace(_search?.Text)) url += $"&search={Uri.EscapeDataString(_search.Text.Trim())}";
        if (_typeBox is not null && _typeBox.Selected > 0) url += $"&types={Uri.EscapeDataString(TypeFilters[_typeBox.Selected].Types)}";
        if (_sortBox is not null && _sortBox.Selected > 0) { var (_, sort, dir) = SortChoices[_sortBox.Selected]; url += $"&sort={sort}&dir={dir}"; }
        if (view == "all" && _selectedFolder != Guid.Empty) url += $"&folderId={_selectedFolder}&includeSubfolders={(_subfolders?.ButtonPressed ?? true).ToString().ToLowerInvariant()}";
        return url;
    }

    private void LoadItems(int page = 1)
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (!session.IsAuthenticated && RequestSink is null) return;
        _page = page; if (page == 1) { _itemsLoaded = false; } _foldersLoaded = false;
        Send(Godot.HttpClient.Method.Get, ItemsUrl(page), null, Pending.List);
        if (RequestSink is null) { _folderHttp.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/inventory/folders", [$"Authorization: Bearer {session.AccessToken}"]); _trashHttp.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/inventory/trash", [$"Authorization: Bearer {session.AccessToken}"]); }
        else { RequestSink(Godot.HttpClient.Method.Get, "/api/v1/inventory/folders", null); RequestSink(Godot.HttpClient.Method.Get, "/api/v1/inventory/trash", null); }
    }

    private void OnTrashFolders(long result, long code, string[] headers, byte[] body)
    {
        if (code is < 200 or >= 300) return;
        ParseTrashFolders(Encoding.UTF8.GetString(body));
    }

    private void ParseTrashFolders(string json)
    {
        _binFolders.Clear();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("folders", out var list)) foreach (var entry in list.EnumerateArray()) if (Guid.TryParse(entry.GetProperty("id").GetString(), out var id)) _binFolders.Add(new FolderInfo(id, entry.GetProperty("name").GetString() ?? "Pasta", null, entry.TryGetProperty("items", out var n) && n.TryGetInt32(out var count) ? count : 0));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
        if (_foldersLoaded && _itemsLoaded) RebuildTree();
    }

    private void OnFolders(long result, long code, string[] headers, byte[] body)
    {
        if (code is < 200 or >= 300) { _foldersLoaded = true; Rebuild(); return; }
        ParseFolders(Encoding.UTF8.GetString(body));
    }

    private void ParseFolders(string json)
    {
        _folders.Clear();
        try { using var document = JsonDocument.Parse(json); foreach (var entry in document.RootElement.EnumerateArray()) if (Guid.TryParse(entry.GetProperty("id").GetString(), out var id)) _folders.Add(new FolderInfo(id, entry.GetProperty("name").GetString() ?? "Pasta", entry.TryGetProperty("parentId", out var p) && p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out var parentId) ? parentId : null, entry.TryGetProperty("itemCount", out var c) && c.TryGetInt32(out var count) ? count : -1)); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
        _foldersLoaded = true; Rebuild();
    }

    private void OnResponse(long result, long code, string[] headers, byte[] body)
    {
        var kind = _inFlight;
        if (kind == Pending.Used) { Pump(); return; } // noting that an item was used needs no refresh
        if (kind == Pending.Unpack) { _status.Text = UnpackText(code, body.Length == 0 ? "{}" : Encoding.UTF8.GetString(body)); if (code is >= 200 and < 300) { LoadItems(); ItemsChanged?.Invoke(); } Pump(); return; }
        if (kind == Pending.Integrity) { _status.Text = code is >= 200 and < 300 && ItemRightsApi.ParseIntegrity(Encoding.UTF8.GetString(body)) is { } report ? report.Summary : ErrorText(code, body); Pump(); return; }
        if (code is < 200 or >= 300) { _status.Text = ErrorText(code, body); if (kind == Pending.Action) LoadItems(); /* show what the server really has: a refused move must not look done */ Pump(); return; }
        try
        {
            using var document = JsonDocument.Parse(body.Length == 0 ? "{}" : Encoding.UTF8.GetString(body));
            if (kind == Pending.List && document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("items", out var items)) ParseItems(items, document.RootElement.TryGetProperty("page", out var pageNumber) && pageNumber.TryGetInt32(out var shownPage) ? shownPage : 1, document.RootElement.TryGetProperty("hasMore", out var more) && more.ValueKind == JsonValueKind.True, document.RootElement.TryGetProperty("total", out var total) && total.TryGetInt32(out var all) ? all : -1);
            else { _status.Text = "Operação concluída."; LoadItems(); ItemsChanged?.Invoke(); }
        }
        catch (JsonException) { _status.Text = body.Length == 0 ? "Operação concluída." : "Resposta de inventário inválida."; if (body.Length == 0) { LoadItems(); ItemsChanged?.Invoke(); } }
        Pump();
    }

    /// What the server said, in words: the reason it refused a change (a name in use, a folder inside itself, too many levels…).
    private static string ErrorText(long code, byte[] body)
    {
        var error = string.Empty;
        try { using var document = JsonDocument.Parse(Encoding.UTF8.GetString(body)); if (document.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) error = e.GetString() ?? string.Empty; }
        catch (JsonException) { }
        return error switch
        {
            "name_taken" => "Já existe uma pasta com esse nome aí.",
            "cycle" => "Uma pasta não pode ir para dentro dela mesma ou de uma pasta que está dentro dela.",
            "too_deep" => "Pastas demais umas dentro das outras (no máximo 8 níveis).",
            "folder_limit" => "Você chegou ao limite de 500 pastas.",
            "invalid_name" => "O nome da pasta é inválido (1 a 120 caracteres).",
            "not_found" => "Isso não existe mais, ou não é seu. A lista foi atualizada.",
            "invalid_move" => "Nada para mover (ou itens demais de uma vez).",
            "item_busy" => "Esse item está em uso agora (oferta ou venda).",
            _ => $"Inventário: HTTP {code}"
        };
    }

    private void ParseItems(JsonElement items, int page = 1, bool hasMore = false, int total = -1)
    {
        if (page <= 1) { _items.Clear(); _order.Clear(); }
        _hasMore = hasMore; _total = total;
        foreach (var entry in items.EnumerateArray())
        {
            string Text(string name, string fallback = "") => entry.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            if (!Guid.TryParse(Text("itemId"), out var itemId) || !Guid.TryParse(Text("assetId"), out var assetId)) continue;
            Guid? folder = entry.TryGetProperty("folderId", out var f) && f.ValueKind == JsonValueKind.String && Guid.TryParse(f.GetString(), out var folderId) ? folderId : null;
            _items[itemId] = new ItemInfo(itemId, assetId, Text("name", "Asset"), Text("category"), Text("mimeType"), Text("contentHash"),
                entry.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0, folder,
                entry.TryGetProperty("favorite", out var favorite) && favorite.ValueKind == JsonValueKind.True,
                entry.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True,
                entry.TryGetProperty("version", out var version) && version.TryGetInt32(out var v) ? v : 1,
                entry.TryGetProperty("ownerRights", out var rights) && rights.TryGetInt32(out var r) ? r : 15, Text("status", "active"), entry.TryGetProperty("hasUpdate", out var upd) && upd.ValueKind == JsonValueKind.True, entry.TryGetProperty("isCreator", out var cr) && cr.ValueKind == JsonValueKind.True);
            if (!_order.Contains(itemId)) _order.Add(itemId);
        }
        _itemsLoaded = true; _status.Text = _total >= 0 ? $"{_items.Count} de {_total} itens" : $"{_items.Count} itens carregados";
        Rebuild();
    }

    public void PopulateForTesting(string foldersJson, string itemsJson)
    {
        ParseFolders(foldersJson);
        using var document = JsonDocument.Parse(itemsJson);
        ParseItems(document.RootElement);
    }

    public void PopulatePageForTesting(string itemsJson, int page, bool hasMore, int total) { using var document = JsonDocument.Parse(itemsJson); _foldersLoaded = true; ParseItems(document.RootElement, page, hasMore, total); }
    public string UrlForTesting(int page = 1) => ItemsUrl(page);
    public void SearchForTesting(string text) => _search.Text = text;
    public void ChooseForTesting(int type, int sort) { _typeBox.Select(type); _sortBox.Select(sort); }
    public void ChooseSectionForTesting(string metadata) { var found = FindNode(_tree.GetRoot(), metadata); found?.Select(0); }
    public bool LoadMoreVisibleForTesting => _loadMore.Visible;
    public string StatusForTesting => _status.Text;
    public void DeleteFolderForTesting() => DeleteFolder();
    public void SelectItemForTesting(Guid id) => SelectItem(id);
    public void MenuForTesting(long id) => OnMenu(id);
    public void MoveFolderForTesting(Guid folder, Guid? parent) => MoveFolder(folder, parent);
    public void TrashFoldersForTesting(string json) => ParseTrashFolders(json);
    public string[] TreeLinesForTesting() { var lines = new List<string>(); void Walk(TreeItem item, int depth) { if (item.GetMetadata(0).VariantType == Variant.Type.String) lines.Add(new string(' ', depth) + item.GetText(0)); for (var child = item.GetFirstChild(); child is not null; child = child.GetNext()) Walk(child, depth + 1); } if (_tree.GetRoot() is { } root) Walk(root, -1); return [.. lines]; }
    private static TreeItem? FindNode(TreeItem? item, string metadata) { if (item is null) return null; if (item.GetMetadata(0).VariantType == Variant.Type.String && item.GetMetadata(0).AsString() == metadata) return item; for (var child = item.GetFirstChild(); child is not null; child = child.GetNext()) if (FindNode(child, metadata) is { } hit) return hit; return null; }
    public int ItemCountForTesting => _items.Count;
    public int CardCountForTesting => _cards.Count;
    public Tree TreeForTesting => _tree;

    // ---- folders and items -----------------------------------------------------------------------------------

    private void Rebuild()
    {
        if (!_itemsLoaded || !_foldersLoaded) return;
        RebuildTree();
        RebuildCards();
        UpdateStorage();
    }

    /// Meu Inventário (with the folders inside), Favoritos and Lixeira, as in the design.
    private void RebuildTree()
    {
        _tree.Clear();
        var root = _tree.CreateItem();
        var mine = _tree.CreateItem(root);
        mine.SetText(0, "Meu Inventário"); mine.SetMetadata(0, "all"); mine.SetIcon(0, UiStyle.Icon("folder-open", FolderYellow, 26)); mine.SetIconMaxWidth(0, 26);
        TreeItem? selected = null;
        var known = _folders.Select(f => f.Id).ToHashSet(); var byParent = _folders.ToLookup(f => f.ParentId is { } p && known.Contains(p) ? p : (Guid?)null);
        void AddFolders(TreeItem parent, Guid? parentId)
        {
            foreach (var folder in byParent[parentId].OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var count = folder.Count >= 0 && _hasMore ? folder.Count : _items.Values.Count(x => x.FolderId == folder.Id); // the server's count while only part of the list is here
                var node = _tree.CreateItem(parent);
                node.SetText(0, $"{folder.Name} ({count})"); node.SetMetadata(0, $"folder:{folder.Id}");
                node.SetIcon(0, UiStyle.Icon("folder-filled", count > 0 || byParent[folder.Id].Any() ? FolderYellow : FolderGrey, 26)); node.SetIconMaxWidth(0, 26);
                if (folder.Id == _selectedFolder) selected = node;
                AddFolders(node, folder.Id);
            }
        }
        AddFolders(mine, null);
        var recent = _tree.CreateItem(root); recent.SetText(0, "Recentes"); recent.SetMetadata(0, "recent"); recent.SetIcon(0, UiStyle.Icon("history", UiStyle.Text, 26)); recent.SetIconMaxWidth(0, 26);
        var returned = _tree.CreateItem(root); returned.SetText(0, "Devolvidos e recuperados"); returned.SetMetadata(0, "returned"); returned.SetIcon(0, UiStyle.Icon("download", UiStyle.Text, 26)); returned.SetIconMaxWidth(0, 26);
        var updatedNode = _tree.CreateItem(root); updatedNode.SetText(0, "Com atualização"); updatedNode.SetMetadata(0, "updated"); updatedNode.SetIcon(0, UiStyle.Icon("layers", UiStyle.Text, 26)); updatedNode.SetIconMaxWidth(0, 26);
        var lostNode = _tree.CreateItem(root); lostNode.SetText(0, "Objetos apagados"); lostNode.SetMetadata(0, "lost"); lostNode.SetIcon(0, UiStyle.Icon("undo", UiStyle.Text, 26)); lostNode.SetIconMaxWidth(0, 26);
        var fav = _tree.CreateItem(root); fav.SetText(0, "Favoritos"); fav.SetMetadata(0, "fav"); fav.SetIcon(0, UiStyle.Icon("star", UiStyle.Text, 26)); fav.SetIconMaxWidth(0, 26);
        var bin = _tree.CreateItem(root); bin.SetText(0, "Lixeira"); bin.SetMetadata(0, "trash"); bin.SetIcon(0, UiStyle.Icon("trash", UiStyle.Text, 26)); bin.SetIconMaxWidth(0, 26);
        TreeItem? selectedBin = null;
        foreach (var folder in _binFolders.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var node = _tree.CreateItem(bin); node.SetText(0, $"{folder.Name} ({folder.Count})"); node.SetMetadata(0, $"bin:{folder.Id}"); node.SetIcon(0, UiStyle.Icon("folder-filled", FolderGrey, 26)); node.SetIconMaxWidth(0, 26);
            if (folder.Id == _selectedBinFolder) selectedBin = node;
        }
        var current = selectedBin ?? (_trash ? bin : _favorites ? fav : _recent ? recent : _returned ? returned : _updated ? updatedNode : selected ?? mine);
        current.Select(0);
    }

    private IEnumerable<ItemInfo> ShownItems() => _order.Where(_items.ContainsKey).Select(id => _items[id]); // the server already filtered and ordered them

    private void RebuildCards()
    {
        foreach (var card in _cards) { _grid.RemoveChild(card); card.QueueFree(); }
        _cards.Clear();
        _grid.Columns = _listLayout ? 1 : 3; _loadMore.Visible = _hasMore;
        foreach (var info in ShownItems())
        {
            var card = new ItemCard(this, info, _listLayout) { Selected = info.ItemId == _selectedItem };
            _grid.AddChild(card); _cards.Add(card);
            _thumbnails.Get(info.AssetId, info.Hash, info.Mime, info.Category, info.Size, texture => { if (IsInstanceValid(card)) card.SetThumbnail(texture); });
        }
    }

    private void UpdateStorage()
    {
        var used = _items.Values.Where(x => !x.Deleted).Sum(x => x.Size);
        var percent = Math.Min(100d, used * 100d / QuotaBytes);
        _storageText.Text = $"{FormatSize(used)} de 100 GB ({percent.ToString("0", CultureInfo.InvariantCulture)}%)";
        _storageBar.Value = percent;
    }

    private static string FormatSize(long bytes)
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0", culture) + " GB";
        if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0", culture) + " MB";
        return (bytes / 1024d).ToString("0", culture) + " KB";
    }

    private void OnSelected()
    {
        var node = _tree.GetSelected();
        if (node is null || node.GetMetadata(0).VariantType != Variant.Type.String) return;
        var text = node.GetMetadata(0).AsString();
        var wasView = (_trash, _favorites, _recent, _returned, _updated, _selectedFolder);
        _trash = text is "trash" || text.StartsWith("bin:", StringComparison.Ordinal); _favorites = text == "fav"; _recent = text == "recent"; _returned = text == "returned"; _updated = text == "updated"; if (text == "lost") { OpenLost(); return; }
        _selectedFolder = Guid.Empty; _selectedBinFolder = Guid.Empty;
        if (text.StartsWith("folder:", StringComparison.Ordinal)) Guid.TryParse(text[7..], out _selectedFolder);
        if (text.StartsWith("bin:", StringComparison.Ordinal)) Guid.TryParse(text[4..], out _selectedBinFolder);
        if (wasView != (_trash, _favorites, _recent, _returned, _updated, _selectedFolder)) LoadItems(); // another section or folder: the server gives the list for it
    }

    private void SelectItem(Guid id)
    {
        _selectedItem = id;
        foreach (var card in _cards) card.Selected = card.Info.ItemId == id;
    }

    // ---- drag and drop ---------------------------------------------------------------------------------------

    private enum FolderMode { Top, Sub, Rename }
    private FolderMode _folderMode;

    private bool CanDrop(Vector2 position, Variant data) => data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().TryGetValue("type", out var type) && type.AsString() is "inventory_item" or "inventory_folder";

    /// What the tree hands out when a folder is dragged: only real folders (not the sections) can be picked up.
    private Variant FolderDragData(Vector2 position)
    {
        var node = _tree.GetItemAtPosition(position);
        if (node is null || node.GetMetadata(0).VariantType != Variant.Type.String || !node.GetMetadata(0).AsString().StartsWith("folder:", StringComparison.Ordinal)) return default;
        _tree.SetDragPreview(new Label { Text = node.GetText(0) });
        return new Godot.Collections.Dictionary { ["type"] = "inventory_folder", ["folderId"] = node.GetMetadata(0).AsString()[7..] };
    }

    private void Drop(Vector2 position, Variant data)
    {
        if (!CanDrop(position, data)) return;
        var node = _tree.GetItemAtPosition(position);
        if (node is null || node.GetMetadata(0).VariantType != Variant.Type.String) return;
        var text = node.GetMetadata(0).AsString(); Guid? target;
        if (text == "all") target = null; else if (text.StartsWith("folder:", StringComparison.Ordinal) && Guid.TryParse(text[7..], out var folderId)) target = folderId; else return;
        var dictionary = data.AsGodotDictionary();
        if (dictionary["type"].AsString() == "inventory_folder") { if (Guid.TryParse(dictionary["folderId"].AsString(), out var moving)) MoveFolder(moving, target); }
        else if (Guid.TryParse(dictionary["itemId"].AsString(), out var itemId)) MoveItem(itemId, target);
    }

    /// Whether the folder is the target itself or somewhere under it: a folder cannot go there. The server checks too; this only spares the round trip.
    private bool FolderIsUnder(Guid? folder, Guid ancestor)
    {
        for (var hops = 0; folder is { } at && hops < 16; hops++) { if (at == ancestor) return true; folder = _folders.FirstOrDefault(f => f.Id == at)?.ParentId; }
        return false;
    }

    public void MoveItem(Guid itemId, Guid? folderId)
    {
        if (!_items.TryGetValue(itemId, out var info) || info.FolderId == folderId) return;
        _items[itemId] = info with { FolderId = folderId };
        Rebuild();
        Send(Godot.HttpClient.Method.Post, "/api/v1/inventory/move", JsonSerializer.Serialize(new { itemIds = new[] { itemId }, folderIds = Array.Empty<Guid>(), targetFolderId = folderId }), Pending.Action); // all or nothing, and checked by the server
        _status.Text = folderId is null ? $"\"{info.Name}\" movido para fora das pastas." : $"\"{info.Name}\" movido para \"{_folders.FirstOrDefault(x => x.Id == folderId)?.Name ?? "pasta"}\".";
    }

    public void MoveFolder(Guid folderId, Guid? parentId)
    {
        var folder = _folders.FirstOrDefault(f => f.Id == folderId); if (folder is null || folder.ParentId == parentId) return;
        if (parentId is { } target && FolderIsUnder(target, folderId)) { _status.Text = "Uma pasta não pode ir para dentro dela mesma ou de uma pasta que está dentro dela."; return; }
        Send(Godot.HttpClient.Method.Post, "/api/v1/inventory/move", JsonSerializer.Serialize(new { itemIds = Array.Empty<Guid>(), folderIds = new[] { folderId }, targetFolderId = parentId }), Pending.Action);
        _status.Text = $"Movendo a pasta \"{folder.Name}\"…";
    }

    private void OpenFolderDialog(string title, string ok)
    {
        if (_folderMode != FolderMode.Top && _selectedFolder == Guid.Empty) { _status.Text = "Selecione uma pasta primeiro."; return; }
        _newFolderDialog.Title = title; _newFolderDialog.OkButtonText = ok;
        _folderName.Text = _folderMode == FolderMode.Rename ? _folders.FirstOrDefault(f => f.Id == _selectedFolder)?.Name ?? string.Empty : string.Empty;
        _newFolderDialog.PopupCentered(); _folderName.GrabFocus();
    }

    private void CreateFolder()
    {
        var name = _folderName.Text.Trim();
        if (name.Length is < 1 or > 120) { _status.Text = "Digite um nome de pasta (até 120 caracteres)."; return; }
        if (_folderMode == FolderMode.Rename) Send(Godot.HttpClient.Method.Patch, $"/api/v1/inventory/folders/{_selectedFolder}", JsonSerializer.Serialize(new { name }), Pending.Action);
        else Send(Godot.HttpClient.Method.Post, "/api/v1/inventory/folders", JsonSerializer.Serialize(new { name, parentId = _folderMode == FolderMode.Sub && _selectedFolder != Guid.Empty ? _selectedFolder : (Guid?)null }), Pending.Action);
        _folderName.Clear();
    }

    /// The folder goes to the bin with everything in it; it can come back from there for 30 days.
    private void DeleteFolder()
    {
        if (_selectedFolder == Guid.Empty) { _status.Text = "Selecione uma pasta para excluir; ela vai para a lixeira com tudo o que tem dentro."; return; }
        Send(Godot.HttpClient.Method.Delete, $"/api/v1/inventory/folders/{_selectedFolder}", null, Pending.Action);
        _status.Text = "Pasta enviada à lixeira com o que tem dentro. Dá para restaurar de lá."; _selectedFolder = Guid.Empty;
    }

    private void DeleteItem()
    {
        if (_selectedItem == Guid.Empty || !_items.ContainsKey(_selectedItem)) { _status.Text = "Escolha um item primeiro."; return; }
        Send(Godot.HttpClient.Method.Delete, $"/api/v1/inventory/items/{_selectedItem}", null, Pending.Action); _status.Text = "Item enviado à lixeira.";
    }

    /// In the bin: a folder selected there is restored with what it held; an item selected is restored by itself.
    private void RestoreFromBin()
    {
        if (_selectedBinFolder != Guid.Empty) { Send(Godot.HttpClient.Method.Post, $"/api/v1/inventory/folders/{_selectedBinFolder}/restore", null, Pending.Action); _status.Text = "Restaurando a pasta e o que tinha dentro…"; _selectedBinFolder = Guid.Empty; return; }
        Restore();
    }

    private void PurgeSelected()
    {
        if (!_trash) { _status.Text = "Só dá para excluir definitivamente o que está na lixeira."; return; }
        if (_selectedBinFolder != Guid.Empty) { Send(Godot.HttpClient.Method.Delete, $"/api/v1/inventory/folders/{_selectedBinFolder}/purge", null, Pending.Action); _selectedBinFolder = Guid.Empty; _status.Text = "Pasta excluída para sempre."; return; }
        if (_selectedItem == Guid.Empty || !_items.ContainsKey(_selectedItem)) { _status.Text = "Escolha um item da lixeira."; return; }
        Send(Godot.HttpClient.Method.Delete, $"/api/v1/inventory/items/{_selectedItem}/purge", null, Pending.Action); _status.Text = "Item excluído para sempre.";
    }

    private void EmptyTrash()
    {
        Send(Godot.HttpClient.Method.Post, "/api/v1/inventory/trash/empty", null, Pending.Action); _status.Text = "Esvaziando a lixeira…";
    }

    // ---- placing models --------------------------------------------------------------------------------------

    private static bool IsPackage(ItemInfo info) => info.Category == "packages";
    private static bool IsModel(ItemInfo info) => info.Mime.StartsWith("model/", StringComparison.Ordinal) || info.Category == "models";

    private void Place()
    {
        if (!_items.TryGetValue(_selectedItem, out var info)) { _status.Text = "Selecione um modelo 3D para colocar."; return; }
        PlaceInWorld(info, PointUnder(GetViewport().GetVisibleRect().Size / 2));
    }

    public void PlaceForTesting(Guid itemId, Vector3 position) { if (_items.TryGetValue(itemId, out var info)) PlaceInWorld(info, position); }

    private void PlaceInWorld(ItemInfo info, Vector3 position)
    {
        if (IsPackage(info))
        {
            if (GetParent()?.GetNodeOrNull<BuildEditor>("BuildEditor") is not { } builder) { _status.Text = "O editor de construção não está disponível."; return; }
            builder.PreviewPackage(info.ItemId, position - Vector3.Up * 1f);
            _status.Text = $"Colocando \"{info.Name}\"…";
            return;
        }
        if (!IsModel(info)) { _status.Text = "Só modelos 3D e construções podem ser colocados no mundo."; return; }
        if (GetParent()?.GetNodeOrNull<BuildEditor>("BuildEditor") is not { } editor) { _status.Text = "O editor de construção não está disponível."; return; }
        editor.CreateMeshFromAsset(info.AssetId, info.Name, position);
        _status.Text = $"Colocando \"{info.Name}\"…";
    }

    /// The point a screen position looks at: the ground or an object, or a spot a few metres ahead.
    private Vector3 PointUnder(Vector2 screen)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return new Vector3(0, .5f, 0);
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + direction * 150f, uint.MaxValue & ~BuildGizmo.PhysicsLayer));
        return hit.Count > 0 ? hit["position"].AsVector3() + Vector3.Up * 1f : origin + direction * 6f;
    }

    // ---- other actions ---------------------------------------------------------------------------------------

    private void Equip()
    {
        if (_selectedItem == Guid.Empty) return;
        Send(Godot.HttpClient.Method.Post, $"/api/v1/inventory/items/{_selectedItem}/equip", JsonSerializer.Serialize(new { slot = "accessory" }), Pending.Action);
    }

    private void Favorite() { if (_selectedItem != Guid.Empty) Send(Godot.HttpClient.Method.Post, $"/api/v1/inventory/items/{_selectedItem}/favorite", null, Pending.Action); }
    private void Restore() { if (_selectedItem != Guid.Empty) Send(Godot.HttpClient.Method.Post, $"/api/v1/inventory/items/{_selectedItem}/restore", null, Pending.Action); }

    private static string CategoryFor(string filename) => System.IO.Path.GetExtension(filename).ToLowerInvariant() switch { ".glb" or ".gltf" => "models", ".png" or ".jpg" or ".jpeg" or ".webp" => "textures", ".ogg" or ".wav" => "sounds", _ => "objects" };

    private void Upload(string path)
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        var filename = System.IO.Path.GetFileName(path);
        var category = CategoryFor(filename);
        var boundary = "----LifeVerse" + Guid.NewGuid().ToString("N");
        var head = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=category\r\n\r\n{category}\r\n--{boundary}\r\nContent-Disposition: form-data; name=file; filename=\"{filename}\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
        var payload = new byte[head.Length + bytes.Length + tail.Length];
        Buffer.BlockCopy(head, 0, payload, 0, head.Length); Buffer.BlockCopy(bytes, 0, payload, head.Length, bytes.Length); Buffer.BlockCopy(tail, 0, payload, head.Length + bytes.Length, tail.Length);
        _status.Text = $"Importando {filename} ({bytes.Length / 1024} KB)…";
        _queue.Enqueue((Godot.HttpClient.Method.Post, "/api/v1/assets/upload", null, Pending.Action, [$"Authorization: Bearer {session.AccessToken}", $"Content-Type: multipart/form-data; boundary={boundary}"], payload));
        Pump();
    }

    // ---- request queue ---------------------------------------------------------------------------------------

    private void Send(Godot.HttpClient.Method method, string path, string? body, Pending kind)
    {
        if (RequestSink is not null && RequestSink(method, path, body)) return;
        _queue.Enqueue((method, path, body, kind, null, null));
        Pump();
    }

    private void Pump()
    {
        if (_queue.Count == 0 || _http.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return;
        var (method, path, body, kind, headers, raw) = _queue.Dequeue();
        var session = GetNode<SessionManager>("/root/SessionManager");
        var url = $"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}{path}";
        _inFlight = kind;
        var error = raw is not null ? _http.RequestRaw(url, headers, method, raw) : _http.Request(url, headers ?? [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], method, body ?? string.Empty);
        if (error != Error.Ok) { _status.Text = $"Não foi possível enviar o pedido ({error})."; Pump(); }
    }

    // ---- cards -----------------------------------------------------------------------------------------------

    /// A dashed rounded outline, drawn over a control to mark a selection or a drop target.
    private static void DrawDashedFrame(CanvasItem item, Rect2 rect, Color color, float radius, float width = 2f)
    {
        const float dash = 10, gap = 7;
        var inset = rect.Grow(-width / 2f);
        void Edge(Vector2 from, Vector2 to) => item.DrawDashedLine(from, to, color, width, dash, true, true);
        Edge(inset.Position + new Vector2(radius, 0), inset.Position + new Vector2(inset.Size.X - radius, 0));
        Edge(inset.Position + new Vector2(radius, inset.Size.Y), inset.Position + new Vector2(inset.Size.X - radius, inset.Size.Y));
        Edge(inset.Position + new Vector2(0, radius), inset.Position + new Vector2(0, inset.Size.Y - radius));
        Edge(inset.Position + new Vector2(inset.Size.X, radius), inset.Position + new Vector2(inset.Size.X, inset.Size.Y - radius));
        foreach (var (center, start) in new[] { (inset.Position + new Vector2(radius, radius), Mathf.Pi), (inset.Position + new Vector2(inset.Size.X - radius, radius), Mathf.Pi * 1.5f), (inset.Position + new Vector2(inset.Size.X - radius, inset.Size.Y - radius), 0f), (inset.Position + new Vector2(radius, inset.Size.Y - radius), Mathf.Pi * .5f) })
            item.DrawArc(center, radius, start, start + Mathf.Pi / 2f, 8, color, width, true);
        _ = gap;
    }

    private sealed partial class ItemCard : PanelContainer
    {
        private readonly InventoryPanel _owner;
        private readonly TextureRect _thumbnail = new();
        private bool _selected;
        public ItemInfo Info { get; }
        public bool Selected { get => _selected; set { _selected = value; QueueRedraw(); } }

        public ItemCard(InventoryPanel owner, ItemInfo info, bool list)
        {
            _owner = owner; Info = info;
            CustomMinimumSize = list ? new Vector2(0, 64) : new Vector2(0, 178);
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            TooltipText = $"{info.Name}\n{info.Category} · {FormatSize(info.Size)} · v{info.Version}\nDireitos: {LifeVerse.Shared.Items.ItemRights.Describe(info.OwnerRights)}{(info.Status == "active" ? string.Empty : "\nEm oferta ou à venda: aguardando resposta")}{(info.HasUpdate ? "\n● Tem versão nova: abra “Versões…”" : string.Empty)}";
            AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.RowFill, .75f), new Color(UiStyle.Divider, .9f), 14, 1, 8, 8));
            _thumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize; _thumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered; _thumbnail.MouseFilter = MouseFilterEnum.Ignore;
            _thumbnail.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
            var name = new Label { Text = (info.HasUpdate ? "● " : string.Empty) + info.Name, MouseFilter = MouseFilterEnum.Ignore, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            name.AddThemeFontSizeOverride("font_size", list ? 19 : 18); name.AddThemeFontOverride("font", UiStyle.Medium);
            if (list)
            {
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); row.MouseFilter = MouseFilterEnum.Ignore;
                _thumbnail.CustomMinimumSize = new Vector2(48, 48); row.AddChild(_thumbnail);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill; name.VerticalAlignment = VerticalAlignment.Center; row.AddChild(name);
                var detail = new Label { Text = $"{info.Category} · {FormatSize(info.Size)} · {LifeVerse.Shared.Items.ItemRights.Letters(info.OwnerRights)}", MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
                detail.AddThemeColorOverride("font_color", UiStyle.TextMuted); detail.AddThemeFontSizeOverride("font_size", 16); row.AddChild(detail);
                if (Badge() is { } listBadge) row.AddChild(listBadge);
                AddChild(row);
            }
            else
            {
                var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; column.AddThemeConstantOverride("separation", 4);
                var holder = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
                _thumbnail.SetAnchorsPreset(LayoutPreset.FullRect); holder.AddChild(_thumbnail);
                if (Badge() is { } badge) { badge.SetAnchorsPreset(LayoutPreset.BottomRight); badge.GrowHorizontal = GrowDirection.Begin; badge.GrowVertical = GrowDirection.Begin; holder.AddChild(badge); }
                column.AddChild(holder);
                name.HorizontalAlignment = HorizontalAlignment.Center; column.AddChild(name);
                AddChild(column);
            }
            SetDragForwarding(Callable.From<Vector2, Variant>(DragData), Callable.From<Vector2, Variant, bool>((_, _) => false), Callable.From<Vector2, Variant>(_ => default));
            GuiInput += OnInput;
        }

        /// "3D", picture or speaker mark in the corner of the thumbnail.
        private Control? Badge()
        {
            Control content;
            if (IsPackage(Info)) content = UiStyle.IconRect("layers", UiStyle.Text, 22);
            else if (IsModel(Info)) { var label = new Label { Text = "3D", MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 17); content = label; }
            else if (Info.Category == "textures" || Info.Mime.StartsWith("image/", StringComparison.Ordinal)) content = UiStyle.IconRect("image", UiStyle.Text, 22);
            else if (Info.Category is "sounds" || Info.Mime.StartsWith("audio/", StringComparison.Ordinal)) content = UiStyle.IconRect("volume", UiStyle.Text, 22);
            else return null;
            content.MouseFilter = MouseFilterEnum.Ignore;
            var pill = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkEnd, SizeFlagsVertical = SizeFlags.ShrinkEnd };
            pill.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(0, 0, 0, .55f), UiStyle.Border, 10, 1, 8, 2));
            pill.AddChild(content);
            return pill;
        }

        public void SetThumbnail(Texture2D texture) => _thumbnail.Texture = texture;

        private void OnInput(InputEvent input)
        {
            if (input is not InputEventMouseButton { Pressed: true } click) return;
            _owner.SelectItem(Info.ItemId);
            if (click.ButtonIndex == MouseButton.Left && click.DoubleClick) { if (IsModel(Info) || IsPackage(Info)) _owner.Place(); else _owner.PreviewSelected(); }
            else if (click.ButtonIndex == MouseButton.Right) _owner.OpenItemMenu(this, GetGlobalMousePosition());
        }

        private Variant DragData(Vector2 position)
        {
            _owner.SelectItem(Info.ItemId);
            _owner._dragItem = Info;
            var preview = new HBoxContainer();
            preview.AddChild(new TextureRect { Texture = _thumbnail.Texture, CustomMinimumSize = new Vector2(56, 56), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            preview.AddChild(new Label { Text = Info.Name });
            SetDragPreview(preview);
            return new Godot.Collections.Dictionary { ["type"] = "inventory_item", ["itemId"] = Info.ItemId.ToString() };
        }

        public override void _Draw()
        {
            if (_selected) DrawDashedFrame(this, new Rect2(Vector2.Zero, Size), UiStyle.Accent, 14, 3);
        }
    }

    private void OpenItemMenu(Control anchor, Vector2 screen)
    {
        var menu = new PopupMenu();
        menu.AddItem("Pré-visualizar…", 16); menu.AddItem("Versões…", 21); menu.AddItem("Colocar no mundo", 3); menu.AddItem("Equipar", 4); menu.AddItem("Favoritar", 5);
        menu.AddSeparator(); if (_trash) { menu.AddItem("Restaurar da lixeira", 6); menu.AddItem("Excluir definitivamente", 14); } else menu.AddItem("Excluir (vai para a lixeira)", 13);
        menu.AddSeparator(); menu.AddItem("Direitos e propriedades…", 8); menu.AddItem("Oferecer a alguém…", 9); menu.AddItem("Fazer uma cópia", 10);
        menu.IdPressed += id => { OnMenu(id); menu.QueueFree(); };
        menu.PopupHide += () => menu.QueueFree();
        AddChild(menu);
        menu.Popup(new Rect2I((Vector2I)screen, Vector2I.Zero));
    }

    /// "Solte aqui para adicionar ao mundo": dropping a model here places it in front of the camera.
    private sealed partial class DropZone : PanelContainer
    {
        private readonly InventoryPanel _owner;
        private bool _hover;

        public DropZone(InventoryPanel owner)
        {
            _owner = owner;
            CustomMinimumSize = new Vector2(0, 76);
            AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.AccentFill, .35f), new Color(0, 0, 0, 0), 16, 0, 16, 10));
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 16);
            var icon = UiStyle.IconRect("cube", UiStyle.Accent, 34); icon.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(icon);
            var label = new Label { Text = "Solte aqui para adicionar ao mundo", VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 21); row.AddChild(label);
            AddChild(row);
            SetDragForwarding(Callable.From<Vector2, Variant>(_ => default), Callable.From<Vector2, Variant, bool>(CanDrop), Callable.From<Vector2, Variant>(Drop));
        }

        private bool CanDrop(Vector2 position, Variant data)
        {
            var accepts = _owner.CanDrop(position, data);
            if (accepts != _hover) { _hover = accepts; QueueRedraw(); }
            return accepts;
        }

        private void Drop(Vector2 position, Variant data)
        {
            _hover = false; QueueRedraw();
            if (!Guid.TryParse(data.AsGodotDictionary()["itemId"].AsString(), out var id) || !_owner._items.TryGetValue(id, out var info)) return;
            _owner.PlaceInWorld(info, _owner.PointUnder(_owner.GetViewport().GetVisibleRect().Size / 2));
        }

        public override void _Notification(int what)
        {
            if (what == NotificationDragEnd) { _hover = false; QueueRedraw(); }
        }

        public override void _Draw() => DrawDashedFrame(this, new Rect2(Vector2.Zero, Size), _hover ? Colors.White : UiStyle.Accent, 16, 3);
    }
}

public partial class InventoryPanel
{
    /// <summary>Opens the rights window for the selected item ("Direitos e propriedades…", or straight to the offer section).</summary>
    private void OpenRights(bool toOffer)
    {
        if (_selectedItem == Guid.Empty || !_items.TryGetValue(_selectedItem, out var info)) { _status.Text = "Escolha um item primeiro."; return; }
        if (GetParent()?.GetNodeOrNull<ItemRightsWindow>("ItemRightsWindow") is not { } window) { _status.Text = "A janela de direitos não está disponível aqui."; return; }
        window.Open(info.ItemId, info.Name, toOffer, Refresh);
    }

    /// <summary>"Fazer uma cópia": only for items that can be copied; the server decides and says why not.</summary>
    private void MakeCopy()
    {
        if (_selectedItem == Guid.Empty || !_items.TryGetValue(_selectedItem, out var info)) { _status.Text = "Escolha um item primeiro."; return; }
        if (!LifeVerse.Shared.Items.ItemRights.IsCopyable(info.OwnerRights)) { _status.Text = "Este item não pode ser copiado."; return; }
        Send(Godot.HttpClient.Method.Post, $"/api/v1/inventory/items/{_selectedItem}/copy", null, Pending.Action);
    }
}

public partial class InventoryPanel
{
    /// <summary>"Pré-visualizar…": opens the item in the preview window (a model turning, a texture to zoom, an animation playing, a sound to hear) and notes that it was used.</summary>
    private async void PreviewSelected()
    {
        if (_selectedItem == Guid.Empty || !_items.TryGetValue(_selectedItem, out var info)) { _status.Text = "Escolha um item primeiro."; return; }
        var kind = IsPackage(info) ? await _preview.OpenPackageAsync(info.ItemId, info.Name, info.Version) : await _preview.OpenAsync(info.AssetId, info.Name, info.Category, info.Mime, info.Hash, info.Size, info.Version);
        _status.Text = kind == PreviewKind.None ? "Esse tipo de item não tem pré-visualização." : $"Pré-visualizando \"{info.Name}\".";
        if (kind != PreviewKind.None && !info.Deleted) Send(Godot.HttpClient.Method.Post, $"/api/v1/inventory/items/{info.ItemId}/used", null, Pending.Used);
    }

    public Task<PreviewKind> OpenPreviewForTesting(Guid itemId) { SelectItem(itemId); var info = _items[itemId]; return _preview.OpenAsync(info.AssetId, info.Name, info.Category, info.Mime, info.Hash, info.Size, info.Version); }
}

public partial class InventoryPanel
{
    /// <summary>A live event: a delivery was offered to this person or answered. The inventory is read again when open (an accepted gift appears, an answered one frees its item).</summary>
    public void OnLiveEvent(string json)
    {
        try { using var document = JsonDocument.Parse(json); if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("type", out var type) && type.GetString() is "item_offer_answer" or "item_offer" or "asset_updated") Refresh(); }
        catch (JsonException) { }
    }

    public string IntegrityTextForTesting(string json) => ItemRightsApi.ParseIntegrity(json)?.Summary ?? string.Empty;
}

public partial class InventoryPanel
{
    /// <summary>"Objetos apagados": the window for getting back what was deleted from the world.</summary>
    private void OpenLost()
    {
        _lostWindow ??= GetParent()?.GetNodeOrNull<LostObjectsWindow>("LostObjectsWindow");
        if (_lostWindow is null) { _lostWindow = new LostObjectsWindow { Name = "LostObjectsWindow" }; GetParent()?.AddChild(_lostWindow); }
        _lostWindow.Open(Refresh);
    }

    public LostObjectsWindow? LostWindowForTesting => _lostWindow;
    public void OpenLostForTesting() => OpenLost();

    /// <summary>Sends a zip of files; the server checks every file and creates all of them or none.</summary>
    private void UploadZip(string path)
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        var bytes = Godot.FileAccess.GetFileAsBytes(path); var filename = System.IO.Path.GetFileName(path);
        if (bytes.Length == 0 || bytes.Length > 100 * 1024 * 1024) { _status.Text = "O pacote precisa ter de 1 byte a 100 MB."; return; }
        var boundary = "----LifeVerse" + Guid.NewGuid().ToString("N");
        var head = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=file; filename=\"{filename}\"\r\nContent-Type: application/zip\r\n\r\n"); var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
        var payload = new byte[head.Length + bytes.Length + tail.Length]; Buffer.BlockCopy(head, 0, payload, 0, head.Length); Buffer.BlockCopy(bytes, 0, payload, head.Length, bytes.Length); Buffer.BlockCopy(tail, 0, payload, head.Length + bytes.Length, tail.Length);
        _status.Text = $"Descompactando {filename} ({bytes.Length / 1024} KB)…";
        _queue.Enqueue((Godot.HttpClient.Method.Post, "/api/v1/inventory/packs/unpack", null, Pending.Unpack, [$"Authorization: Bearer {session.AccessToken}", $"Content-Type: multipart/form-data; boundary={boundary}"], payload));
        Pump();
    }

    private static string ProblemText(string code) => code switch
    {
        "unsafe_path" => "caminho perigoso", "unsupported_type" => "tipo não aceito (só modelos .glb/.gltf, imagens .png/.jpg/.webp e sons .ogg/.wav)", "nested_zip" => "zip dentro de zip", "file_too_large" => "arquivo grande demais",
        "suspicious_compression" => "compactação suspeita", "unreadable" => "não deu para ler", _ => code
    };

    /// <summary>What to tell the person about the answer of an unzip: what was made, or why nothing was.</summary>
    public static string UnpackText(long status, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body); var root = document.RootElement;
            if (status is >= 200 and < 300) return $"{(root.TryGetProperty("count", out var c) ? c.GetInt32() : 0)} arquivo(s) na pasta “{(root.TryGetProperty("folderName", out var n) ? n.GetString() : "pacote")}”.";
            var code = root.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            if (code == "bad_files" && root.TryGetProperty("report", out var report) && report.TryGetProperty("problems", out var problems))
            {
                var list = problems.EnumerateArray().ToList();
                return $"Nada foi importado: {list.Count} arquivo(s) com problema.\n" + string.Join("\n", list.Take(4).Select(p => $"• {p.GetProperty("file").GetString()}: {ProblemText(p.GetProperty("error").GetString() ?? string.Empty)}")) + (list.Count > 4 ? $"\n… e mais {list.Count - 4}." : string.Empty);
            }
            return code switch
            {
                "invalid_zip" => "Esse arquivo não é um .zip válido (ou passa de 100 MB).", "empty_zip" => "O pacote não tem nenhum arquivo.", "too_many_files" => "O pacote tem arquivos demais (no máximo 100).", "pack_too_large" => "O pacote é grande demais depois de descompactado.",
                "inventory_full" => "Seu inventário não comporta tantos itens.", "folder_limit" => "Você chegou ao limite de pastas.", _ => $"Não foi possível descompactar (HTTP {status})."
            };
        }
        catch (JsonException) { return $"Não foi possível descompactar (HTTP {status})."; }
    }
}

public partial class InventoryPanel
{
    private AssetVersionsWindow? _versionsWindow;
    public AssetVersionsWindow? VersionsWindowForTesting => _versionsWindow;

    /// <summary>"Versões…": what changed in each version of the selected item's asset; the creator can publish a new one or go back.</summary>
    private void OpenVersions()
    {
        if (_selectedItem == Guid.Empty || !_items.TryGetValue(_selectedItem, out var info)) { _status.Text = "Escolha um item primeiro."; return; }
        _versionsWindow ??= GetParent()?.GetNodeOrNull<AssetVersionsWindow>("AssetVersionsWindow");
        if (_versionsWindow is null) { _versionsWindow = new AssetVersionsWindow { Name = "AssetVersionsWindow" }; GetParent()?.AddChild(_versionsWindow); }
        _versionsWindow.Open(info.AssetId, info.ItemId, info.Name, Refresh);
    }

    public void OpenVersionsForTesting(Guid itemId) { SelectItem(itemId); OpenVersions(); }
}
