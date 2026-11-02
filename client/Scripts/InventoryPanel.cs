using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class InventoryPanel : Node3D
{
    private PanelContainer _panel = null!;
    private Tree _tree = null!;
    private LineEdit _search = null!;
    private OptionButton _category = null!;
    private Label _status = null!;
    private HttpRequest _http = null!;
    private FileDialog _fileDialog = null!;
    private Guid _selectedItem;
    private bool _trash;
    private bool _favorites;

    public override void _Ready() { _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnResponse; BuildUi(); }
    public void Toggle() { _panel.Visible = !_panel.Visible; if (_panel.Visible) LoadItems(); }

    private void BuildUi()
    {
        _panel = new PanelContainer { Name = "InventoryPanel", Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft); _panel.OffsetLeft = 18; _panel.OffsetTop = -300; _panel.OffsetRight = 560; _panel.OffsetBottom = 300;
        var box = new VBoxContainer(); _panel.AddChild(box); box.AddChild(new Label { Text = "INVENTARIO GLOBAL" });
        _search = new LineEdit { PlaceholderText = "Pesquisar conteudo" }; box.AddChild(_search);
        _category = new OptionButton(); _category.AddItem("Todos"); foreach (var value in new[] { "objects", "clothing", "accessories", "textures", "models", "sounds", "animations", "scripts", "huds", "landmarks", "packages" }) _category.AddItem(value); box.AddChild(_category);
        var filters = new HBoxContainer(); var all = new Button { Text = "Todos" }; var fav = new Button { Text = "Favoritos" }; var trash = new Button { Text = "Lixeira" }; filters.AddChild(all); filters.AddChild(fav); filters.AddChild(trash); box.AddChild(filters);
        _tree = new Tree { Columns = 4, CustomMinimumSize = new Vector2(520, 360), HideRoot = true }; _tree.SetColumnTitle(0, "Nome"); _tree.SetColumnTitle(1, "Tipo"); _tree.SetColumnTitle(2, "Versao"); _tree.SetColumnTitle(3, "Estado"); _tree.ColumnTitlesVisible = true; _tree.ItemSelected += OnSelected; box.AddChild(_tree);
        var actions = new HBoxContainer(); var upload = new Button { Text = "Importar" }; var place = new Button { Text = "Colocar" }; var equip = new Button { Text = "Equipar" }; var favorite = new Button { Text = "Favoritar" }; var restore = new Button { Text = "Restaurar" }; foreach (var button in new[] { upload, place, equip, favorite, restore }) actions.AddChild(button); box.AddChild(actions);
        _status = new Label { Text = "Pronto" }; box.AddChild(_status); AddChild(_panel); _fileDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.glb,*.gltf,*.png,*.jpg,*.jpeg,*.webp,*.ogg,*.wav"] }; AddChild(_fileDialog);
        upload.Pressed += () => _fileDialog.PopupCentered(new Vector2I(800, 500)); place.Pressed += Place; equip.Pressed += Equip; favorite.Pressed += Favorite; restore.Pressed += Restore; _fileDialog.FileSelected += Upload; _search.TextChanged += _ => LoadItems(); _category.ItemSelected += _ => LoadItems(); all.Pressed += () => { _trash = false; _favorites = false; LoadItems(); }; fav.Pressed += () => { _trash = false; _favorites = true; LoadItems(); }; trash.Pressed += () => { _trash = true; _favorites = false; LoadItems(); };
    }

    private void LoadItems()
    {
        var session = GetNode<SessionManager>("/root/SessionManager"); if (!session.IsAuthenticated) return; var url = $"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/inventory/items?search={Uri.EscapeDataString(_search?.Text ?? string.Empty)}&page=1&pageSize=50&includeDeleted={_trash.ToString().ToLowerInvariant()}&favoritesOnly={_favorites.ToString().ToLowerInvariant()}"; if (_category?.Selected > 0) url += $"&category={Uri.EscapeDataString(_category.GetItemText(_category.Selected))}"; _http.Request(url, [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private void OnResponse(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode is < 200 or >= 300) { _status.Text = $"Inventario: HTTP {responseCode}"; return; }
        try { using var document = JsonDocument.Parse(Encoding.UTF8.GetString(body)); if (!document.RootElement.TryGetProperty("items", out var items)) { _status.Text = "Operacao concluida."; LoadItems(); return; } _tree.Clear(); foreach (var entry in items.EnumerateArray()) { var item = _tree.CreateItem(); item.SetText(0, entry.GetProperty("name").GetString() ?? "Asset"); item.SetText(1, entry.GetProperty("category").GetString() ?? ""); item.SetText(2, entry.GetProperty("version").GetInt32().ToString()); item.SetText(3, entry.GetProperty("deleted").GetBoolean() ? "Lixeira" : (entry.GetProperty("favorite").GetBoolean() ? "Favorito" : "Disponivel")); item.SetMetadata(0, entry.GetProperty("itemId").GetString() ?? string.Empty); } _status.Text = $"{items.GetArrayLength()} itens carregados"; } catch { _status.Text = "Resposta de inventario invalida."; }
    }

    private void OnSelected() { var item = _tree.GetSelected(); if (item?.GetMetadata(0).VariantType == Variant.Type.String) Guid.TryParse(item.GetMetadata(0).AsString(), out _selectedItem); }
    private void Upload(string path) { var session = GetNode<SessionManager>("/root/SessionManager"); var bytes = Godot.FileAccess.GetFileAsBytes(path); var boundary = "----LifeVerse" + Guid.NewGuid().ToString("N"); var category = _category.Selected > 0 ? _category.GetItemText(_category.Selected) : "objects"; var filename = System.IO.Path.GetFileName(path); var body = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=category\r\n\r\n{category}\r\n--{boundary}\r\nContent-Disposition: form-data; name=file; filename=\"{filename}\"\r\nContent-Type: application/octet-stream\r\n\r\n"); var end = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n"); var payload = new byte[body.Length + bytes.Length + end.Length]; Buffer.BlockCopy(body, 0, payload, 0, body.Length); Buffer.BlockCopy(bytes, 0, payload, body.Length, bytes.Length); Buffer.BlockCopy(end, 0, payload, body.Length + bytes.Length, end.Length); _http.RequestRaw($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/assets/upload", (System.ReadOnlySpan<string>)[ $"Authorization: Bearer {session.AccessToken}", $"Content-Type: multipart/form-data; boundary={boundary}" ], Godot.HttpClient.Method.Post, (System.ReadOnlySpan<byte>)payload); }
    private void Place() { if (_selectedItem == Guid.Empty) return; var island = GetNode<NetworkManager>("/root/NetworkManager").CurrentIslandId; var session = GetNode<SessionManager>("/root/SessionManager"); var body = JsonSerializer.Serialize(new { islandId = island, positionX = 0f, positionY = 1f, positionZ = 0f, rotationX = 0f, rotationY = 0f, rotationZ = 0f }); _http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/inventory/items/{_selectedItem}/place", [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, body); }
    private void Equip() { if (_selectedItem == Guid.Empty) return; var session = GetNode<SessionManager>("/root/SessionManager"); _http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/inventory/items/{_selectedItem}/equip", [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, "{\"slot\":\"accessory\"}"); }
    private void Favorite() { if (_selectedItem == Guid.Empty) return; var session = GetNode<SessionManager>("/root/SessionManager"); _http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/inventory/items/{_selectedItem}/favorite", [$"Authorization: Bearer {session.AccessToken}"], Godot.HttpClient.Method.Post); }
    private void Restore() { if (_selectedItem == Guid.Empty) return; var session = GetNode<SessionManager>("/root/SessionManager"); _http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/inventory/items/{_selectedItem}/restore", [$"Authorization: Bearer {session.AccessToken}"], Godot.HttpClient.Method.Post); }
}
