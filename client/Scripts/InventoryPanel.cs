using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// The global inventory: folders, real previews, import, and drag and drop. Drag an item onto a folder to file it,
/// or out onto the world to place a 3D model there.
public partial class InventoryPanel : Node3D
{
    private sealed record ItemInfo(Guid ItemId, Guid AssetId, string Name, string Category, string Mime, string Hash, long Size, Guid? FolderId, bool Favorite, bool Deleted, int Version);
    private sealed record FolderInfo(Guid Id, string Name);
    private enum Pending { List, Action }

    private PanelContainer _panel = null!;
    private Tree _tree = null!;
    private LineEdit _search = null!, _folderName = null!;
    private OptionButton _category = null!;
    private Label _status = null!;
    private HttpRequest _http = null!, _folderHttp = null!;
    private FileDialog _fileDialog = null!;
    private ThumbnailLoader _thumbnails = null!;
    private readonly Dictionary<Guid, ItemInfo> _items = [];
    private readonly List<FolderInfo> _folders = [];
    private readonly Queue<(Godot.HttpClient.Method Method, string Path, string? Body, Pending Kind, string[]? Headers, byte[]? Raw)> _queue = new();
    private Pending _inFlight;
    private Guid _selectedItem;
    private Guid _selectedFolder;
    private bool _trash;
    private bool _favorites;
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

    public void Toggle() { _panel.Visible = !_panel.Visible; if (_panel.Visible) LoadItems(); }

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
        _panel = new PanelContainer { Name = "InventoryPanel", Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft); _panel.OffsetLeft = 18; _panel.OffsetTop = -310; _panel.OffsetRight = 580; _panel.OffsetBottom = 311;
        var box = new VBoxContainer(); _panel.AddChild(box);
        box.AddChild(new Label { Text = "INVENTÁRIO · arraste itens para pastas ou para o mundo" });
        _search = new LineEdit { PlaceholderText = "Pesquisar conteúdo" }; box.AddChild(_search);
        _category = new OptionButton(); _category.AddItem("Todos");
        foreach (var value in new[] { "objects", "clothing", "accessories", "textures", "models", "sounds", "animations", "scripts", "huds", "landmarks", "packages" }) _category.AddItem(value);
        box.AddChild(_category);
        var filters = new HBoxContainer();
        var all = new Button { Text = "Todos" }; var fav = new Button { Text = "Favoritos" }; var trash = new Button { Text = "Lixeira" };
        filters.AddChild(all); filters.AddChild(fav); filters.AddChild(trash); box.AddChild(filters);
        var folders = new HBoxContainer();
        _folderName = new LineEdit { PlaceholderText = "Nome da nova pasta", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var newFolder = new Button { Text = "Nova pasta" }; var deleteFolder = new Button { Text = "Excluir pasta" };
        folders.AddChild(_folderName); folders.AddChild(newFolder); folders.AddChild(deleteFolder); box.AddChild(folders);

        _tree = new Tree { Columns = 4, CustomMinimumSize = new Vector2(540, 360), HideRoot = true, ColumnTitlesVisible = true, DropModeFlags = (int)Tree.DropModeFlagsEnum.OnItem };
        _tree.SetColumnTitle(0, "Nome"); _tree.SetColumnTitle(1, "Tipo"); _tree.SetColumnTitle(2, "Versão"); _tree.SetColumnTitle(3, "Estado");
        _tree.SetColumnExpand(0, true); _tree.SetColumnCustomMinimumWidth(0, 240);
        _tree.ItemSelected += OnSelected;
        _tree.SetDragForwarding(Callable.From<Vector2, Variant>(GetDragData), Callable.From<Vector2, Variant, bool>(CanDrop), Callable.From<Vector2, Variant>(Drop));
        box.AddChild(_tree);

        var actions = new HBoxContainer();
        foreach (var (text, handler) in new (string, Action)[] { ("Importar", () => _fileDialog.PopupCentered(new Vector2I(800, 500))), ("Colocar", Place), ("Equipar", Equip), ("Favoritar", Favorite), ("Restaurar", Restore) })
        { var button = new Button { Text = text }; button.Pressed += handler; actions.AddChild(button); }
        box.AddChild(actions);
        _status = new Label { Text = "Pronto", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) }; box.AddChild(_status);
        AddChild(_panel);
        _fileDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.glb,*.gltf,*.png,*.jpg,*.jpeg,*.webp,*.ogg,*.wav"] };
        AddChild(_fileDialog);
        _fileDialog.FileSelected += Upload;
        all.Pressed += () => { _trash = false; _favorites = false; LoadItems(); }; fav.Pressed += () => { _favorites = true; _trash = false; LoadItems(); }; trash.Pressed += () => { _trash = true; _favorites = false; LoadItems(); };
        newFolder.Pressed += CreateFolder; deleteFolder.Pressed += DeleteFolder;
        _search.TextChanged += _ => LoadItems(); _category.ItemSelected += _ => LoadItems();
    }

    // ---- data ------------------------------------------------------------------------------------------------

    private void LoadItems()
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (!session.IsAuthenticated) return;
        _itemsLoaded = false; _foldersLoaded = false;
        var url = $"/api/v1/inventory/items?search={Uri.EscapeDataString(_search?.Text ?? string.Empty)}&page=1&pageSize=100&includeDeleted={_trash.ToString().ToLowerInvariant()}&favoritesOnly={_favorites.ToString().ToLowerInvariant()}";
        if (_category?.Selected > 0) url += $"&category={Uri.EscapeDataString(_category.GetItemText(_category.Selected))}";
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
            else { _status.Text = "Operação concluída."; LoadItems(); }
        }
        catch (JsonException) { _status.Text = body.Length == 0 ? "Operação concluída." : "Resposta de inventário inválida."; if (body.Length == 0) LoadItems(); }
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
                entry.TryGetProperty("version", out var version) && version.TryGetInt32(out var v) ? v : 1);
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
    public Tree TreeForTesting => _tree;

    // ---- tree ------------------------------------------------------------------------------------------------

    private void Rebuild()
    {
        if (!_itemsLoaded || !_foldersLoaded) return;
        _tree.Clear();
        var root = _tree.CreateItem();
        var folderItems = new Dictionary<Guid, TreeItem>();
        var folderIcon = ThumbnailLoader.Placeholder("folders");
        foreach (var folder in _folders.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var count = _items.Values.Count(x => x.FolderId == folder.Id);
            var node = _tree.CreateItem(root);
            node.SetText(0, $"{folder.Name} ({count})"); node.SetText(1, "Pasta"); node.SetMetadata(0, $"folder:{folder.Id}");
            node.SetIcon(0, folderIcon); node.SetIconMaxWidth(0, 24); node.SetCustomColor(0, ViewerTheme.AccentLight);
            folderItems[folder.Id] = node;
        }
        foreach (var info in _items.Values.OrderByDescending(x => x.Favorite).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var parent = info.FolderId is { } id && folderItems.TryGetValue(id, out var folderNode) ? folderNode : root;
            var node = _tree.CreateItem(parent);
            node.SetText(0, info.Name); node.SetText(1, info.Category); node.SetText(2, info.Version.ToString());
            node.SetText(3, info.Deleted ? "Lixeira" : info.Favorite ? "Favorito" : "Disponível");
            node.SetMetadata(0, info.ItemId.ToString()); node.SetIconMaxWidth(0, 44);
            _thumbnails.Get(info.AssetId, info.Hash, info.Mime, info.Category, info.Size, texture => { if (IsInstanceValid(node)) node.SetIcon(0, texture); });
        }
    }

    private void OnSelected()
    {
        var node = _tree.GetSelected();
        if (node is null || node.GetMetadata(0).VariantType != Variant.Type.String) return;
        var text = node.GetMetadata(0).AsString();
        if (text.StartsWith("folder:", StringComparison.Ordinal)) { Guid.TryParse(text[7..], out _selectedFolder); _selectedItem = Guid.Empty; }
        else { Guid.TryParse(text, out _selectedItem); _selectedFolder = Guid.Empty; }
    }

    // ---- drag and drop ---------------------------------------------------------------------------------------

    private Variant GetDragData(Vector2 position)
    {
        var node = _tree.GetItemAtPosition(position);
        if (node is null || node.GetMetadata(0).VariantType != Variant.Type.String || !Guid.TryParse(node.GetMetadata(0).AsString(), out var id) || !_items.TryGetValue(id, out var info)) return default;
        _dragItem = info;
        var preview = new HBoxContainer();
        preview.AddChild(new TextureRect { Texture = node.GetIcon(0), CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        preview.AddChild(new Label { Text = info.Name });
        _tree.SetDragPreview(preview);
        return new Godot.Collections.Dictionary { ["type"] = "inventory_item", ["itemId"] = info.ItemId.ToString() };
    }

    private bool CanDrop(Vector2 position, Variant data) => data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().TryGetValue("type", out var type) && type.AsString() == "inventory_item";

    private void Drop(Vector2 position, Variant data)
    {
        if (!CanDrop(position, data) || !Guid.TryParse(data.AsGodotDictionary()["itemId"].AsString(), out var itemId)) return;
        Guid? target = null;
        var node = _tree.GetItemAtPosition(position);
        if (node is not null && node.GetMetadata(0).VariantType == Variant.Type.String)
        {
            var text = node.GetMetadata(0).AsString();
            if (text.StartsWith("folder:", StringComparison.Ordinal) && Guid.TryParse(text[7..], out var folderId)) target = folderId;
            else if (Guid.TryParse(text, out var neighbour) && _items.TryGetValue(neighbour, out var next)) target = next.FolderId; // dropped on an item: same folder as it
        }
        MoveItem(itemId, target);
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

    private static bool IsModel(ItemInfo info) => info.Mime.StartsWith("model/", StringComparison.Ordinal) || info.Category == "models";

    private void Place()
    {
        if (!_items.TryGetValue(_selectedItem, out var info)) { _status.Text = "Selecione um modelo 3D para colocar."; return; }
        PlaceInWorld(info, PointUnder(GetViewport().GetVisibleRect().Size / 2));
    }

    public void PlaceForTesting(Guid itemId, Vector3 position) { if (_items.TryGetValue(itemId, out var info)) PlaceInWorld(info, position); }

    private void PlaceInWorld(ItemInfo info, Vector3 position)
    {
        if (!IsModel(info)) { _status.Text = "Só modelos 3D (GLB/glTF) podem ser colocados no mundo."; return; }
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
        var category = _category.Selected > 0 ? _category.GetItemText(_category.Selected) : CategoryFor(filename);
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
}
