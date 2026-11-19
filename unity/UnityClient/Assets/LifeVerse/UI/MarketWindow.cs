using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The marketplace: products for sale (a purchase is asked for twice, the second touch says the price, and carries an idempotency key so a repeated touch can never charge twice), the
    /// person's orders, the rental plans for an island of their own (rent with the same two touches) and their stores (create, open and close). The balance, the price checks and the
    /// delivery are the API's: it is the financial authority and refuses what is not allowed.
    /// </summary>
    public sealed class MarketWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Label _status; private readonly Dictionary<string, VisualElement> _tabs = new Dictionary<string, VisualElement>();
        private readonly ApiList _products, _orders, _plans, _stores; private readonly TextField _search, _storeName, _storeAbout;
        public string CurrentTab { get; private set; }
        public int ProductCount => _products.Count;
        public int PlanCount => _plans.Count;
        public int StoreCount => _stores.Count;
        public int OrderCount => _orders.Count;

        public MarketWindow(ApiContext context) : base(context.Ui, "market", "Mercado", 480f, 520f, new Vector2(400, 80))
        {
            _c = context; var bar = UiKit.Row(); _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            foreach (var name in new[] { "Produtos", "Pedidos", "Aluguel", "Minhas lojas", "Vender" }) { var captured = name; bar.Add(UiKit.Button(name, () => Show(captured))); var tab = new VisualElement(); tab.style.flexGrow = 1; tab.style.display = DisplayStyle.None; _tabs[name] = tab; }
            Body.Add(bar); Body.Add(_status);
            _search = UiKit.Field("Buscar produtos", false, 60); _search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) _products.Reload(); }, TrickleDown.TrickleDown);
            _products = new ApiList(context, () => "/api/v1/economy/products?page=1&pageSize=24" + (string.IsNullOrWhiteSpace(_search.value) ? "" : "&q=" + Uri.EscapeDataString(_search.value.Trim())), ProductRow, "Nenhum produto à venda.", false);
            _tabs["Produtos"].Add(_search); _tabs["Produtos"].Add(_products);
            _orders = new ApiList(context, () => "/api/v1/economy/orders", row => UiKit.Row(UiKit.Text(row.First("productName", "name", "description", "id"), "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("status"), "lv-muted"), UiKit.Text(row.Number("priceCoins", row.Number("amountCoins")).ToString("N0"), "lv-accent")), "Nenhum pedido.", false); _tabs["Pedidos"].Add(_orders);
            _plans = new ApiList(context, () => "/api/v1/economy/rental-plans", PlanRow, "Nenhum plano de aluguel.", false); _tabs["Aluguel"].Add(UiKit.Text("Alugue uma ilha para chamar de sua (o plano vale para a ilha em que você está).", "lv-muted", "lv-small")); _tabs["Aluguel"].Add(_plans);
            _stores = new ApiList(context, () => "/api/v1/discovery/stores/mine", StoreRow, "Você ainda não tem lojas.", false); _storeName = UiKit.Field("Nome da loja", false, 60); _storeAbout = UiKit.Field("Descrição", false, 200);
            _tabs["Minhas lojas"].Add(_stores); _tabs["Minhas lojas"].Add(_storeName); _tabs["Minhas lojas"].Add(_storeAbout); _tabs["Minhas lojas"].Add(UiKit.Button("Criar loja", CreateStore, "lv-btn-primary"));
            _sellAsset = UiKit.Field("Arquivo na plataforma (identificador)", false, 40); _sellCategory = new DropdownField("Tipo", new List<string> { "objects", "textures", "models", "sounds", "clothing", "accessories", "animations" }, 0); _sellName = UiKit.Field("Nome do produto", false, 80); _sellAbout = UiKit.Field("Descrição", false, 300); _sellPrice = UiKit.Field("Preço (VerseCoin)", false, 10);
            _sellResale = UiKit.Check("Quem comprar pode revender", false, _ => { }); _sellStatus = UiKit.Text("", "lv-small", "lv-muted"); _sellStatus.style.whiteSpace = WhiteSpace.Normal;
            foreach (var element in new VisualElement[] { UiKit.Text("Venda um arquivo seu: envie-o (ou use um que já está na plataforma) e dê um preço.", "lv-muted", "lv-small"), UiKit.Button("Enviar um arquivo…", PickFile), _sellAsset, _sellCategory, _sellName, _sellAbout, _sellPrice, _sellResale, _sellStatus, Controls.Confirm("Publicar à venda", () => "Confirmar a publicação", Publish, "lv-btn-primary") }) _tabs["Vender"].Add(element);
            foreach (var tab in _tabs.Values) Body.Add(tab); Show("Produtos");
        }

        private readonly TextField _sellAsset, _sellName, _sellAbout, _sellPrice; private readonly DropdownField _sellCategory; private readonly Toggle _sellResale; private readonly Label _sellStatus;
        public TextField SellAsset => _sellAsset; public TextField SellName => _sellName; public TextField SellPrice => _sellPrice;

        private void PickFile() => Uploads.PickAndUpload(_c, "Escolha o arquivo a vender", null, _sellCategory.value, result =>
        {
            if (!result.Ok) { _sellStatus.text = result.Message; _sellStatus.AddToClassList("lv-bad"); return; }
            _sellAsset.value = result.AssetId.ToString(); if (_sellName.value.Trim().Length == 0) _sellName.value = result.Name; _sellStatus.text = "Arquivo enviado. Falta o nome e o preço."; _sellStatus.RemoveFromClassList("lv-bad");
        });

        /// <summary>The checks before a product is published. Null when fine.</summary>
        public static string ValidateProduct(string asset, string name, string price, out Guid assetId, out long coins)
        {
            coins = 0; if (!Guid.TryParse((asset ?? "").Trim(), out assetId)) return "Envie um arquivo ou digite o identificador de um que já está na plataforma.";
            if ((name ?? "").Trim().Length < 3) return "O produto precisa de um nome (pelo menos 3 letras).";
            if (!long.TryParse((price ?? "").Trim(), out coins) || coins < 0 || coins > 10_000_000) return "O preço é um número inteiro de VerseCoin (0 a 10 milhões).";
            return null;
        }

        public void Publish()
        {
            var problem = ValidateProduct(_sellAsset.value, _sellName.value, _sellPrice.value, out var asset, out var coins); if (problem != null) { _sellStatus.text = problem; _sellStatus.AddToClassList("lv-bad"); return; }
            var rights = _sellResale.value ? LifeVerse.Shared.Items.ItemRights.All : LifeVerse.Shared.Items.ItemRights.Standard;
            Post("POST", "/api/v1/economy/products", JsonSerializer.Serialize(new { assetId = asset, name = _sellName.value.Trim(), description = _sellAbout.value, category = _sellCategory.value, priceCoins = coins, imageUrl = (string)null, permissionsJson = (string)null, buyerRights = rights, nextOwnerRights = rights, itemId = (Guid?)null, allowGift = true }), "Produto publicado.", () => { _sellName.value = ""; _sellPrice.value = ""; _sellStatus.text = ""; _products.Reload(); });
        }

        public void Show(string tab)
        {
            CurrentTab = tab; foreach (var pair in _tabs) pair.Value.style.display = pair.Key == tab ? DisplayStyle.Flex : DisplayStyle.None;
            if (tab == "Produtos") _products.Reload(); else if (tab == "Pedidos") _orders.Reload(); else if (tab == "Aluguel") _plans.Reload(); else _stores.Reload();
        }

        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); }
        private void Post(string method, string path, string json, string success, Action then = null) => this.Then(_c.Api.ApiAsync(method, path, json), r => { if (r.Status >= 200 && r.Status < 300) { if (success != null) _c.Notices.Show(success, NoticeKind.Good); Say("", false); then?.Invoke(); } else Say(UiTasks.Explain(r.Status), true); });

        private VisualElement ProductRow(Row row)
        {
            var id = row.Id(); var price = row.Number("priceCoins"); var name = row.First("name", "title");
            var info = new VisualElement(); info.style.flexGrow = 1; info.Add(UiKit.Text(name, "lv-bold")); var about = row.First("description"); if (about.Length > 0) { var text = UiKit.Text(about.Length > 90 ? about.Substring(0, 90) + "…" : about, "lv-muted", "lv-small"); text.style.whiteSpace = WhiteSpace.Normal; info.Add(text); }
            return UiKit.Row(info, UiKit.Text(price.ToString("N0") + " VC", "lv-accent"), Controls.Confirm("Comprar", () => "Confirmar: " + price.ToString("N0") + " VerseCoin", () => Buy(id, price, name), "lv-btn-primary"));
        }

        /// <summary>One purchase: the API charges, delivers the item to the inventory and answers; the idempotency key is new for each touch of confirmation.</summary>
        public void Buy(Guid product, long price, string name) => Post("POST", "/api/v1/economy/products/" + product + "/purchase", JsonSerializer.Serialize(new { idempotencyKey = Guid.NewGuid().ToString("N"), recipientUserId = (Guid?)null }), "Comprado: " + name + " (" + price.ToString("N0") + " VerseCoin).");

        private VisualElement PlanRow(Row row)
        {
            var id = row.Id(); var price = row.Number("priceCoins", row.Number("price")); var days = row.Number("days", row.Number("periodDays", 30));
            return UiKit.Row(UiKit.Text(row.First("name", "title", "id"), "lv-bold"), UiKit.Spacer(), UiKit.Text(price.ToString("N0") + " VC / " + days + " dias", "lv-accent"),
                Controls.Confirm("Alugar", () => "Confirmar: " + price.ToString("N0") + " VerseCoin", () => Post("POST", "/api/v1/economy/islands/" + _c.CurrentIsland() + "/rent", JsonSerializer.Serialize(new { planId = id, idempotencyKey = Guid.NewGuid().ToString("N") }), "Aluguel contratado.", () => _plans.Reload()), "lv-btn-primary"));
        }

        private VisualElement StoreRow(Row row)
        {
            var id = row.Id(); var open = row.Flag("isOpen");
            return UiKit.Row(UiKit.Text(row.First("name"), "lv-bold"), UiKit.Spacer(), UiKit.Text(open ? "aberta" : "fechada", open ? "lv-good" : "lv-muted"), UiKit.Button(open ? "Fechar" : "Abrir", () => Post("PUT", "/api/v1/discovery/stores/" + id, JsonSerializer.Serialize(new { name = (string)null, description = (string)null, category = (string)null, logoUrl = (string)null, bannerUrl = (string)null, isOpen = !open }), null, () => _stores.Reload())), Controls.Confirm("Apagar", () => "Confirmar", () => Post("DELETE", "/api/v1/discovery/stores/" + id, null, "Loja apagada.", () => _stores.Reload())));
        }

        public void CreateStore()
        {
            var name = _storeName.value.Trim(); if (name.Length < 3) { Say("O nome da loja precisa ter pelo menos 3 letras.", true); return; }
            Post("POST", "/api/v1/discovery/stores", JsonSerializer.Serialize(new { name, description = _storeAbout.value.Trim(), category = (string)null, logoUrl = (string)null, bannerUrl = (string)null, isOpen = true }), "Loja criada.", () => { _storeName.value = ""; _storeAbout.value = ""; _stores.Reload(); });
        }

        public TextField StoreNameField => _storeName;
    }
}
