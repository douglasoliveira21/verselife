using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class MarketplacePanel : Node3D
{
    private PanelContainer _panel = null!; private Tree _products = null!; private Label _wallet = null!; private Label _status = null!; private HttpRequest _http = null!; private Guid _selected;
    public override void _Ready() { _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnResponse; Build(); }
    public void Toggle() { _panel.Visible = !_panel.Visible; if (_panel.Visible) { LoadWallet(); LoadProducts(); } }
    private void Build() { _panel = new PanelContainer { Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.Center); _panel.OffsetLeft = -300; _panel.OffsetTop = -250; _panel.OffsetRight = 300; _panel.OffsetBottom = 250; var box = new VBoxContainer(); _panel.AddChild(box); box.AddChild(new Label { Text = "MARKETPLACE · LV COINS" }); _wallet = new Label { Text = "Carteira…" }; box.AddChild(_wallet); _products = new Tree { Columns = 4, HideRoot = true, CustomMinimumSize = new Vector2(550, 280) }; _products.ItemSelected += () => { var item = _products.GetSelected(); if (item is not null) { Guid.TryParse(item.GetMetadata(0).AsString(), out _selected); _status.Text = item.GetMetadata(1).AsString(); } }; box.AddChild(_products); var actions = new HBoxContainer(); var refresh = new Button { Text = "Atualizar" }; var buy = new Button { Text = "Comprar" }; actions.AddChild(refresh); actions.AddChild(buy); box.AddChild(actions); _status = new Label { Text = "Pronto" }; box.AddChild(_status); AddChild(_panel); refresh.Pressed += () => { LoadWallet(); LoadProducts(); }; buy.Pressed += Buy; }
    /// <summary>"CMT-" and "revenda": what the buyer gets, at a glance.</summary>
    public static string RightsColumn(JsonElement product) => (product.TryGetProperty("rights", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : "----") + (product.TryGetProperty("isResale", out var resale) && resale.ValueKind == JsonValueKind.True ? " · revenda" : string.Empty);

    /// <summary>"Você receberá: Copiar · Modificar. Poderá passar adiante: Transferir." — shown when a product is selected.</summary>
    public static string RightsSentence(JsonElement product)
    {
        string Text(string name) => product.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
        var gift = product.TryGetProperty("allowGift", out var g) && g.ValueKind == JsonValueKind.False ? " Não pode ser comprado de presente." : string.Empty;
        return $"Você receberá: {Text("rightsText")}. Poderá passar adiante: {Text("nextRightsText")}.{gift}";
    }

    private void LoadWallet() => Request("/api/v1/economy/wallet", Godot.HttpClient.Method.Get, null);
    private void LoadProducts() => Request("/api/v1/economy/products?page=1&pageSize=50", Godot.HttpClient.Method.Get, null);
    private void Buy() { if (_selected == Guid.Empty) return; var body = JsonSerializer.Serialize(new { idempotencyKey = Guid.NewGuid().ToString("N") }); Request($"/api/v1/economy/products/{_selected}/purchase", Godot.HttpClient.Method.Post, body); }
    private void Request(string path, Godot.HttpClient.Method method, string? body) { var session = GetNode<SessionManager>("/root/SessionManager"); if (!session.IsAuthenticated) return; _http.Request(GetNode<AppManager>("/root/AppManager").ApiBaseUrl + path, [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], method, body ?? string.Empty); }
    private void OnResponse(long result, long code, string[] headers, byte[] body) { if (code is < 200 or >= 300) { _status.Text = ItemRightsApi.Explain(ItemRightsApi.ErrorCode(Encoding.UTF8.GetString(body)), result == (long)HttpRequest.Result.Success ? (int)code : 0); return; } try { using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); if (doc.RootElement.TryGetProperty("availableCoins", out var coins)) _wallet.Text = $"Saldo: {coins.GetInt64():N0} LV Coins"; else if (doc.RootElement.TryGetProperty("items", out var items)) { _products.Clear(); foreach (var p in items.EnumerateArray()) { var row = _products.CreateItem(); row.SetText(0, p.GetProperty("name").GetString() ?? "Produto"); row.SetText(1, (p.GetProperty("category").GetString() ?? "") + (p.TryGetProperty("groupName", out var seller) && seller.ValueKind == JsonValueKind.String ? " · " + seller.GetString() : string.Empty)); row.SetText(2, $"{p.GetProperty("priceCoins").GetInt64():N0}"); row.SetMetadata(0, p.GetProperty("id").GetString() ?? string.Empty); row.SetText(3, RightsColumn(p)); row.SetMetadata(1, RightsSentence(p)); } _status.Text = $"{items.GetArrayLength()} produtos"; } else _status.Text = "Operação concluída."; } catch { _status.Text = "Resposta inválida."; } }
}
