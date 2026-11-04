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

    private sealed record ItemInfo(Guid ItemId, Guid AssetId, string Name, string Category, string Mime, string Hash, long Size, Guid? FolderId, bool Favorite, bool Deleted, int Version, int OwnerRights = 15, string Status = "active");
    private sealed record FolderInfo(Guid Id, string Name);
    private enum Pending { List, Action }

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
        menu.AddItem("Importar modelo 3D…", 7); menu.AddItem("Importar arquivo…", 0); menu.AddItem("Nova pasta…", 1); menu.AddItem("Excluir pasta selecionada", 2); menu.AddSeparator(); menu.AddItem("Colocar no mundo", 3); menu.AddItem("Equipar", 4); menu.AddItem("Favoritar", 5); menu.AddItem("Restaurar da lixeira", 6);
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
        _tree.SetDragForwarding(Callable.From<Vector2, Variant>(_ => default), Callable.From<Vector2, Variant, bool>(CanDrop), Callable.From<Vector2, Variant>(Drop));
        left.AddChild(_tree);
        _status = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) };
        _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); _status.AddThemeFontSizeOverride("font_size", 14);
        left.AddChild(_status);
        left.AddChild(new HSeparator());
        _storageText = new Label(); _storageText.AddThemeFontSizeOverride("font_size", 18); left.AddChild(_storageText);
        _storageBar = new ProgressBar { ShowPercentage = false, MaxValue = 100, CustomMinimumSize = new Vector2(0, 14) }; left.AddChild(_storageBar);

        body.AddChild(new VSeparator());
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 12); body.AddChild(right);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(500, 0) };
        _grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _grid.AddThemeConstantOverride("h_separation", 12); _grid.AddThemeConstantOverride("v_separation", 12);
        scroll.AddChild(_grid); right.AddChild(scroll);
        right.AddChild(new DropZone(this));

        AddChild(_window);
        _newFolderDialog = new ConfirmationDialog { Title = "Nova pasta", OkButtonText = "Criar", CancelButtonText = "Cancelar", Exclusive = true };
        _folderName = new LineEdit { PlaceholderText = "Nome da pasta", CustomMinimumSize = new Vector2(280, 0) };
        _newFolderDialog.AddChild(_folderName); _newFolderDialog.Confirmed += CreateFolder; _folderName.TextSubmitted += _ => { _newFolderDialog.Hide(); CreateFolder(); };
        AddChild(_newFolderDialog);
        _fileDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.glb,*.gltf,*.png,*.jpg,*.jpeg,*.webp,*.ogg,*.wav"] };
        AddChild(_fileDialog);
        _fileDialog.FileSelected += Upload;
        _import = new ModelImportDialog { Name = "ModelImport" }; AddChild(_import); _import.AttachTo(this); _import.Confirmed += Upload;
        _search.TextChanged += _ => LoadItems();
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
            case 1: _folderName.Clear(); _newFolderDialog.PopupCentered(); _folderName.GrabFocus(); break;
            case 2: DeleteFolder(); break;
            case 3: Place(); break;
            case 4: Equip(); break;
            case 5: Favorite(); break;
            case 6: Restore(); break;
            case 8: OpenRights(false); break;
            case 9: OpenRights(true); break;
            case 10: MakeCopy(); break;
        }
    }

    // ---- data ------------------------------------------------------------------------------------------------

    private void LoadItems()
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (!session.IsAuthenticated) return;
        _itemsLoaded = false; _foldersLoaded = false;
        var url = $"/api/v1/inventory/items?search={Uri.EscapeDataString(_search?.Text ?? string.Empty)}&page=1&pageSize=100&includeDeleted={_trash.ToString().ToLowerInvariant()}&favoritesOnly={_favorites.ToString().ToLowerInvariant()}";
        Send(Godot.HttpClient.Method.Get, url, null, Pending.List);
        if (RequestSink is null) _folderHttp.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/inventory/folders", [$"Authorization: Bearer {session.AccessToken}"]);
        else RequestSink(Godot.HttpClient.Method.Get, "/api/v1/inventory/folders", null);
    }

    private void OnFolders(long result, long code, string[] headers, byte[] body)
    {
        if (code is < 200 or >= 300) { _foldersLoaded = true; Rebuild(); return; }
        ParseFolders(Encoding.UTF8.GetString(body));
    }

    private void ParseFolders(string json)
    {
        _folders.Clear();
        try { using var document = JsonDocument.Parse(json); foreach (var entry in document.RootElement.EnumerateArray()) if (Guid.TryParse(entry.GetProperty("id").GetString(), out var id)) _folders.Add(new FolderInfo(id, entry.GetProperty("name").GetString() ?? "Pasta")); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
        _foldersLoaded = true; Rebuild();
    }

    private void OnResponse(long result, long code, string[] headers, byte[] body)
    {
        var kind = _inFlight;
        if (code is < 200 or >= 300) { _status.Text = $"Inventário: HTTP {code}"; Pump(); return; }
        try
        {
            using var document = JsonDocument.Parse(body.Length == 0 ? "{}" : Encoding.UTF8.GetString(body));
            if (kind == Pending.List && document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("items", out var items)) ParseItems(items);
            else { _status.Text = "Operação concluída."; LoadItems(); ItemsChanged?.Invoke(); }
        }
        catch (JsonException) { _status.Text = body.Length == 0 ? "Operação concluída." : "Resposta de inventário inválida."; if (body.Length == 0) { LoadItems(); ItemsChanged?.Invoke(); } }
        Pump();
    }

    private void ParseItems(JsonElement items)
    {
        _items.Clear();
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
                entry.TryGetProperty("ownerRights", out var rights) && rights.TryGetInt32(out var r) ? r : 15, Text("status", "active"));
        }
        _itemsLoaded = true; _status.Text = $"{_items.Count} itens carregados";
        Rebuild();
    }

    public void PopulateForTesting(string foldersJson, string itemsJson)
    {
        ParseFolders(foldersJson);
        using var document = JsonDocument.Parse(itemsJson);
        ParseItems(document.RootElement);
    }

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
        foreach (var folder in _folders.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var count = _items.Values.Count(x => x.FolderId == folder.Id);
            var node = _tree.CreateItem(mine);
            node.SetText(0, $"{folder.Name} ({count})"); node.SetMetadata(0, $"folder:{folder.Id}");
            node.SetIcon(0, UiStyle.Icon("folder-filled", count > 0 ? FolderYellow : FolderGrey, 26)); node.SetIconMaxWidth(0, 26);
            if (folder.Id == _selectedFolder) selected = node;
        }
        var fav = _tree.CreateItem(root); fav.SetText(0, "Favoritos"); fav.SetMetadata(0, "fav"); fav.SetIcon(0, UiStyle.Icon("star", UiStyle.Text, 26)); fav.SetIconMaxWidth(0, 26);
        var bin = _tree.CreateItem(root); bin.SetText(0, "Lixeira"); bin.SetMetadata(0, "trash"); bin.SetIcon(0, UiStyle.Icon("trash", UiStyle.Text, 26)); bin.SetIconMaxWidth(0, 26);
        var current = _trash ? bin : _favorites ? fav : selected ?? mine;
        current.Select(0);
    }

    private IEnumerable<ItemInfo> ShownItems()
    {
        var items = _items.Values.AsEnumerable();
        if (_selectedFolder != Guid.Empty && !_trash && !_favorites) items = items.Where(x => x.FolderId == _selectedFolder);
        return items.OrderByDescending(x => x.Favorite).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    private void RebuildCards()
    {
        foreach (var card in _cards) { _grid.RemoveChild(card); card.QueueFree(); }
        _cards.Clear();
        _grid.Columns = _listLayout ? 1 : 3;
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
        var wasTrash = _trash; var wasFavorites = _favorites;
        _trash = text == "trash"; _favorites = text == "fav";
        _selectedFolder = Guid.Empty;
        if (text.StartsWith("folder:", StringComparison.Ordinal)) Guid.TryParse(text[7..], out _selectedFolder);
        if (_trash != wasTrash || _favorites != wasFavorites) { LoadItems(); return; }
        RebuildCards();
    }

    private void SelectItem(Guid id)
    {
        _selectedItem = id;
        foreach (var card in _cards) card.Selected = card.Info.ItemId == id;
    }

    // ---- drag and drop ---------------------------------------------------------------------------------------

    private bool CanDrop(Vector2 position, Variant data) => data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().TryGetValue("type", out var type) && type.AsString() == "inventory_item";

    private void Drop(Vector2 position, Variant data)
    {
        if (!CanDrop(position, data) || !Guid.TryParse(data.AsGodotDictionary()["itemId"].AsString(), out var itemId)) return;
        var node = _tree.GetItemAtPosition(position);
        if (node is null || node.GetMetadata(0).VariantType != Variant.Type.String) return;
        var text = node.GetMetadata(0).AsString();
        if (text == "all") MoveItem(itemId, null);
        else if (text.StartsWith("folder:", StringComparison.Ordinal) && Guid.TryParse(text[7..], out var folderId)) MoveItem(itemId, folderId);
    }

    public void MoveItem(Guid itemId, Guid? folderId)
    {
        if (!_items.TryGetValue(itemId, out var info) || info.FolderId == folderId) return;
        _items[itemId] = info with { FolderId = folderId };
        Rebuild();
        Send(Godot.HttpClient.Method.Patch, $"/api/v1/inventory/items/{itemId}", JsonSerializer.Serialize(new { displayName = (string?)null, folderId, isFavorite = (bool?)null }), Pending.Action);
        _status.Text = folderId is null ? $"\"{info.Name}\" movido para fora das pastas." : $"\"{info.Name}\" movido para \"{_folders.FirstOrDefault(x => x.Id == folderId)?.Name ?? "pasta"}\".";
    }

    private void CreateFolder()
    {
        var name = _folderName.Text.Trim();
        if (name.Length is < 1 or > 120) { _status.Text = "Digite um nome de pasta (até 120 caracteres)."; return; }
        Send(Godot.HttpClient.Method.Post, "/api/v1/inventory/folders", JsonSerializer.Serialize(new { name, parentId = (Guid?)null }), Pending.Action);
        _folderName.Clear();
    }

    private void DeleteFolder()
    {
        if (_selectedFolder == Guid.Empty) { _status.Text = "Selecione uma pasta para excluir; os itens dela voltam para fora das pastas."; return; }
        Send(Godot.HttpClient.Method.Delete, $"/api/v1/inventory/folders/{_selectedFolder}", null, Pending.Action);
        _selectedFolder = Guid.Empty;
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
            builder.RezPackage(info.ItemId, position - Vector3.Up * 1f);
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
            TooltipText = $"{info.Name}\n{info.Category} · {FormatSize(info.Size)} · v{info.Version}\nDireitos: {LifeVerse.Shared.Items.ItemRights.Describe(info.OwnerRights)}{(info.Status == "active" ? string.Empty : "\nEm oferta ou à venda: aguardando resposta")}";
            AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.RowFill, .75f), new Color(UiStyle.Divider, .9f), 14, 1, 8, 8));
            _thumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize; _thumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered; _thumbnail.MouseFilter = MouseFilterEnum.Ignore;
            _thumbnail.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
            var name = new Label { Text = info.Name, MouseFilter = MouseFilterEnum.Ignore, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            name.AddThemeFontSizeOverride("font_size", list ? 19 : 18); name.AddThemeFontOverride("font", UiStyle.Medium);
            if (list)
            {
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); row.MouseFilter = MouseFilterEnum.Ignore;
                _thumbnail.CustomMinimumSize = new Vector2(48, 48); row.AddChild(_thumbnail);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill; name.VerticalAlignment = VerticalAlignment.Center; row.AddChild(name);
                var detail = new Label { Text = $"{info.Category} · {FormatSize(info.Size)}", MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
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
            if (click.ButtonIndex == MouseButton.Left && click.DoubleClick && (IsModel(Info) || IsPackage(Info))) _owner.Place();
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
        menu.AddItem("Colocar no mundo", 3); menu.AddItem("Equipar", 4); menu.AddItem("Favoritar", 5); menu.AddItem("Restaurar da lixeira", 6);
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
