using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Groups;

namespace LifeVerse.Client.Scripts;

public sealed record GroupTreasuryData(bool CanView, bool CanTake, bool CanFreeze, long Balance, long TotalIn, long TotalOut, bool Frozen, long Mine);
public sealed record GroupLedgerLine(string Type, long Amount, long BalanceAfter, string Actor, string? Target, string Note, DateTimeOffset At);
public sealed record GroupProductData(Guid Id, string Name, long Price, string Status, string Publisher, int Sold);
public sealed record GroupStoreData(Guid Id, string IslandName, string ProductName, string DisplayName);

/// <summary>The money part of the groups window: the chest (give, take out, freeze, read the extract) and what the group sells (products in the marketplace, shops on the islands).</summary>
public sealed partial class GroupsWindow
{
    private GroupTreasuryData? _treasury;
    private readonly List<GroupLedgerLine> _ledger = [];
    private readonly List<GroupProductData> _products = [];
    private readonly List<GroupStoreData> _stores = [];
    private readonly List<(Guid AssetId, string Name)> _myAssets = [];
    private bool _paying;

    private Label _chestInfo = null!, _shopNote = null!;
    private ItemList _ledgerList = null!, _productList = null!, _storeList = null!;
    private LineEdit _giveAmount = null!, _giveNote = null!, _takeAmount = null!, _takeReason = null!, _prodName = null!, _prodPrice = null!, _newPrice = null!, _storeName = null!;
    private OptionButton _takeTo = null!, _prodAsset = null!, _storeIsland = null!, _storeProduct = null!;
    private CheckBox _freezeBox = null!;
    private Button _give = null!, _take = null!, _publish = null!, _reprice = null!, _unpublish = null!, _openStore = null!, _closeStore = null!;

    public GroupTreasuryData? Treasury => _treasury;
    public IReadOnlyList<GroupLedgerLine> Ledger => _ledger;
    public IReadOnlyList<GroupProductData> Products => _products;
    public IReadOnlyList<GroupStoreData> Stores => _stores;

    private static long Coins(string text) => long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static string Money(long coins) => coins.ToString("N0", Pt);

    // ---- the chest -------------------------------------------------------------------------------------------------------------------------------------

    public void LoadTreasury()
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Get, $"/{g.Id}/treasury", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            try
            {
                using var d = JsonDocument.Parse(body); var r = d.RootElement; long N(string name) => r.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
                _treasury = new GroupTreasuryData(Flag(r, "canView"), Flag(r, "canTake"), Flag(r, "canFreeze"), N("balance"), N("totalIn"), N("totalOut"), Flag(r, "frozen"), N("myContributed"));
            }
            catch (JsonException) { return; }
            RefreshChest();
            if (_treasury!.CanView) LoadLedger();
        });
    }

    private void LoadLedger()
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Get, $"/{g.Id}/treasury/ledger?take=50", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            _ledger.Clear(); _ledger.AddRange(ReadList(body, e => new GroupLedgerLine(Str(e, "type"), e.TryGetProperty("amount", out var a) && a.TryGetInt64(out var am) ? am : 0, e.TryGetProperty("balanceAfter", out var b) && b.TryGetInt64(out var ba) ? ba : 0, Str(e, "actor"), Str(e, "target") is { Length: > 0 } t ? t : null, Str(e, "note"), Time(e, "at") ?? DateTimeOffset.UtcNow)));
            _ledgerList.Clear();
            foreach (var l in _ledger)
            {
                var what = l.Type switch { "contribution" => "contribuição", "sale" => "venda", _ => "retirada" };
                _ledgerList.AddItem($"{l.At.ToLocalTime():dd/MM HH:mm} · {what} {(l.Amount > 0 ? "+" : string.Empty)}{Money(l.Amount)} · {l.Actor}{(l.Target is not null ? $" → {l.Target}" : string.Empty)}{(l.Note.Length > 0 ? $" — {l.Note}" : string.Empty)}");
            }
        });
    }

    private void RefreshChest()
    {
        if (_treasury is not { } t) { _chestInfo.Text = string.Empty; return; }
        _chestInfo.Text = t.CanView
            ? $"Saldo do cofre: {Money(t.Balance)} moedas{(t.Frozen ? "  ·  CONGELADO" : string.Empty)}\nJá entrou {Money(t.TotalIn)} · já saiu {Money(t.TotalOut)} · você deu {Money(t.Mine)}"
            : $"Você já deu {Money(t.Mine)} moedas ao cofre do grupo.{(t.Frozen ? "  O cofre está congelado." : string.Empty)}";
        _freezeBox.SetPressedNoSignal(t.Frozen); UpdateEconomyButtons();
    }

    public void Contribute()
    {
        if (_current is not { } g || _paying) return;
        var amount = Coins(_giveAmount.Text); if (amount < 1) { Say(Explain(400, "{\"error\":\"bad_amount\"}")); return; }
        _paying = true;
        Call(Godot.HttpClient.Method.Post, $"/{g.Id}/treasury/contribute", new { amount, note = _giveNote.Text.Trim(), idempotencyKey = Guid.NewGuid().ToString("N") }, (status, body) =>
        {
            _paying = false;
            if (status is >= 200 and < 300) { Say($"Obrigado! {Money(amount)} moedas foram para o cofre."); _giveAmount.Text = _giveNote.Text = string.Empty; LoadTreasury(); } else Say(Explain(status, body));
        });
    }

    public void Withdraw()
    {
        if (_current is not { } g || _paying || _takeTo.ItemCount == 0 || !Guid.TryParse(_takeTo.GetItemMetadata(_takeTo.Selected).AsString(), out var to)) return;
        var amount = Coins(_takeAmount.Text); if (amount < 1) { Say(Explain(400, "{\"error\":\"bad_amount\"}")); return; }
        _paying = true;
        Call(Godot.HttpClient.Method.Post, $"/{g.Id}/treasury/withdraw", new { amount, userId = to, reason = _takeReason.Text.Trim(), idempotencyKey = Guid.NewGuid().ToString("N") }, (status, body) =>
        {
            _paying = false;
            if (status is >= 200 and < 300) { Say($"{Money(amount)} moedas saíram do cofre para {_takeTo.GetItemText(_takeTo.Selected)}."); _takeAmount.Text = _takeReason.Text = string.Empty; LoadTreasury(); }
            else if (ErrorOf(body) == "over_limit") Say($"Acima do limite de hoje. Ainda dá para tirar {Money(AvailableOf(body))} moedas."); else Say(Explain(status, body));
        });
    }

    private static long AvailableOf(string body)
    {
        try { using var d = JsonDocument.Parse(body); return d.RootElement.TryGetProperty("detail", out var detail) && detail.TryGetProperty("available", out var a) && a.TryGetInt64(out var n) ? n : 0; } catch (JsonException) { return 0; }
    }

    public void SetFrozen(bool frozen)
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Put, $"/{g.Id}/treasury/frozen", new { frozen }, (status, body) =>
        {
            if (status is >= 200 and < 300) { Say(frozen ? "Cofre congelado: só o líder tira moedas." : "Cofre liberado."); LoadTreasury(); } else { Say(Explain(status, body)); _freezeBox.SetPressedNoSignal(_treasury?.Frozen == true); }
        });
    }

    // ---- products and shops -----------------------------------------------------------------------------------------------------------------------------

    public void LoadShop()
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Get, $"/{g.Id}/products", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            _products.Clear(); _products.AddRange(ReadList(body, p => new GroupProductData(IdOf(p, "id"), Str(p, "name"), p.TryGetProperty("priceCoins", out var pr) && pr.TryGetInt64(out var price) ? price : 0, Str(p, "status"), Str(p, "publisher"), Num(p, "sold"))));
            _productList.Clear(); foreach (var p in _products) _productList.AddItem($"{p.Name} · {Money(p.Price)} moedas · {(p.Status == "active" ? "à venda" : "retirado")} · {p.Sold} vendido(s) · por {p.Publisher}");
            _storeProduct.Clear(); foreach (var p in _products.Where(x => x.Status == "active")) { _storeProduct.AddItem(p.Name); _storeProduct.SetItemMetadata(_storeProduct.ItemCount - 1, p.Id.ToString()); }
            _shopNote.Text = _products.Count == 0 ? "O grupo ainda não vende nada. O que for vendido rende para o cofre." : "O dinheiro das vendas (menos a taxa de 5%) vai para o cofre do grupo.";
            UpdateEconomyButtons();
        });
        Call(Godot.HttpClient.Method.Get, $"/{g.Id}/stores", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            _stores.Clear(); _stores.AddRange(ReadList(body, s => new GroupStoreData(IdOf(s, "id"), Str(s, "islandName"), Str(s, "productName"), Str(s, "displayName"))));
            _storeList.Clear(); foreach (var s in _stores) _storeList.AddItem($"{s.DisplayName} · {s.IslandName} · {s.ProductName}");
            UpdateEconomyButtons();
        });
        if (Has(GroupPermissions.ManageStore))
        {
            Call(Godot.HttpClient.Method.Get, "/api/v1/inventory/items?page=1&pageSize=100", null, (status, body) =>
            {
                if (status is < 200 or >= 300) return;
                try
                {
                    using var d = JsonDocument.Parse(body); _myAssets.Clear(); _prodAsset.Clear();
                    if (d.RootElement.TryGetProperty("items", out var items))
                        foreach (var entry in items.EnumerateArray())
                            if (entry.TryGetProperty("asset", out var asset) && IdOf(asset, "ownerUserId") == MyId) { _myAssets.Add((IdOf(asset, "id"), Str(asset, "name"))); _prodAsset.AddItem(Str(asset, "name")); }
                }
                catch (JsonException) { }
                UpdateEconomyButtons();
            });
            if (_islands.Count == 0)
                Call(Godot.HttpClient.Method.Get, "/api/v1/discovery/islands?pageSize=50", null, (status, body) =>
                {
                    if (status is < 200 or >= 300) return;
                    try { using var d = JsonDocument.Parse(body); _islands.Clear(); _evIsland.Clear(); if (d.RootElement.TryGetProperty("items", out var items)) foreach (var i in items.EnumerateArray()) { _islands.Add((IdOf(i, "id"), Str(i, "name"))); _evIsland.AddItem(Str(i, "name")); } }
                    catch (JsonException) { }
                    FillStoreIslands();
                });
            else FillStoreIslands();
        }
    }

    private void FillStoreIslands() { _storeIsland.Clear(); foreach (var i in _islands) _storeIsland.AddItem(i.Name); UpdateEconomyButtons(); }

    private GroupProductData? SelectedProduct() { var p = _productList.GetSelectedItems(); return p.Length == 0 || p[0] >= _products.Count ? null : _products[p[0]]; }
    private GroupStoreData? SelectedStore() { var p = _storeList.GetSelectedItems(); return p.Length == 0 || p[0] >= _stores.Count ? null : _stores[p[0]]; }

    public void PublishProduct()
    {
        if (_current is not { } g || _prodAsset.ItemCount == 0 || _prodAsset.Selected < 0 || _prodAsset.Selected >= _myAssets.Count) return;
        Do(Godot.HttpClient.Method.Post, $"/{g.Id}/products", new { assetId = _myAssets[_prodAsset.Selected].AssetId, name = _prodName.Text.Trim(), description = string.Empty, category = "geral", priceCoins = Coins(_prodPrice.Text) }, "Produto publicado no mercado em nome do grupo.", () => { _prodName.Text = _prodPrice.Text = string.Empty; LoadShop(); });
    }

    public void Reprice() { if (_current is { } g && SelectedProduct() is { } p) Do(Godot.HttpClient.Method.Put, $"/{g.Id}/products/{p.Id}", new { priceCoins = Coins(_newPrice.Text) }, $"Novo preço de “{p.Name}”: {Money(Coins(_newPrice.Text))}.", () => { _newPrice.Text = string.Empty; LoadShop(); }); }
    public void Unpublish() { if (_current is { } g && SelectedProduct() is { } p) Do(Godot.HttpClient.Method.Delete, $"/{g.Id}/products/{p.Id}", null, $"“{p.Name}” saiu do mercado e as lojas dele fecharam.", LoadShop); }

    public void OpenStore()
    {
        if (_current is not { } g || _storeIsland.ItemCount == 0 || _storeIsland.Selected < 0 || _storeIsland.Selected >= _islands.Count || _storeProduct.ItemCount == 0 || !Guid.TryParse(_storeProduct.GetItemMetadata(_storeProduct.Selected).AsString(), out var product)) return;
        Do(Godot.HttpClient.Method.Post, $"/{g.Id}/stores", new { islandId = _islands[_storeIsland.Selected].Id, productId = product, displayName = _storeName.Text.Trim() }, "Loja do grupo aberta.", () => { _storeName.Text = string.Empty; LoadShop(); });
    }

    public void CloseStore() { if (_current is { } g && SelectedStore() is { } s) Do(Godot.HttpClient.Method.Delete, $"/{g.Id}/stores/{s.Id}", null, $"A loja “{s.DisplayName}” fechou.", LoadShop); }

    private void UpdateEconomyButtons()
    {
        var t = _treasury; var manageStore = Has(GroupPermissions.ManageStore);
        _give.Disabled = _paying; _giveAmount.Editable = _giveNote.Editable = true;
        var take = t?.CanTake == true; _take.Disabled = !take || _paying || _takeTo.ItemCount == 0; _takeAmount.Editable = _takeReason.Editable = take; _takeTo.Disabled = !take;
        _freezeBox.Disabled = t?.CanFreeze != true;
        var product = SelectedProduct();
        _publish.Disabled = !(manageStore && _prodAsset.ItemCount > 0); _prodName.Editable = _prodPrice.Editable = manageStore; _prodAsset.Disabled = !manageStore;
        _reprice.Disabled = !(manageStore && product is { Status: "active" }); _newPrice.Editable = manageStore; _unpublish.Disabled = !(manageStore && product is { Status: "active" });
        _openStore.Disabled = !(manageStore && _storeIsland.ItemCount > 0 && _storeProduct.ItemCount > 0); _storeName.Editable = manageStore; _storeIsland.Disabled = _storeProduct.Disabled = !manageStore;
        _closeStore.Disabled = !(manageStore && SelectedStore() is not null);
    }

    private void RefreshTakeTargets() { if (_takeTo is null) return; _takeTo.Clear(); foreach (var m in _members) { _takeTo.AddItem(m.Username); _takeTo.SetItemMetadata(_takeTo.ItemCount - 1, m.UserId.ToString()); } }

    // ---- building the pages ---------------------------------------------------------------------------------------------------------------------------------

    private Control BuildChest(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        _chestInfo = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 40) }; _chestInfo.AddThemeFontSizeOverride("font_size", 16); page.AddChild(_chestInfo);
        _ledgerList = List(110); page.AddChild(_ledgerList);
        _giveAmount = Field("Quantas moedas", 10); _giveNote = Field("Recado (opcional)", 120); _give = Act("Contribuir", UiStyle.ButtonKind.Primary, Contribute); page.AddChild(Row(_giveAmount, _giveNote, _give));
        page.AddChild(Note("Contribuir é voluntário e definitivo: as moedas não voltam se você sair do grupo."));
        _takeAmount = Field("Moedas", 10); _takeTo = Choice(); _takeReason = Field("Motivo (obrigatório)", 200); _take = Act("Retirar do cofre", UiStyle.ButtonKind.Secondary, Withdraw); page.AddChild(Row(_takeAmount, _takeTo)); page.AddChild(Row(_takeReason, _take));
        _freezeBox = new CheckBox { Text = "Congelar o cofre (só o líder tira moedas)", FocusMode = Control.FocusModeEnum.None }; _freezeBox.AddThemeFontSizeOverride("font_size", 14); _freezeBox.Toggled += SetFrozen; page.AddChild(_freezeBox);
        return page;
    }

    private Control BuildShop(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6);
        _shopNote = Note("O grupo ainda não vende nada."); box.AddChild(_shopNote);
        box.AddChild(Heading("Produtos no mercado")); _productList = List(70); _productList.ItemSelected += _ => UpdateEconomyButtons(); box.AddChild(_productList);
        _newPrice = Field("Novo preço", 10); _reprice = Act("Mudar preço", UiStyle.ButtonKind.Secondary, Reprice); _unpublish = Act("Retirar do mercado", UiStyle.ButtonKind.Secondary, Unpublish); box.AddChild(Row(_newPrice, _reprice, _unpublish));
        _prodAsset = Choice(); _prodName = Field("Nome do produto", 160); _prodPrice = Field("Preço", 10); _publish = Act("Publicar produto", UiStyle.ButtonKind.Primary, PublishProduct); box.AddChild(Row(_prodAsset, _prodName)); box.AddChild(Row(_prodPrice, _publish));
        box.AddChild(Heading("Lojas nas ilhas")); _storeList = List(60); _storeList.ItemSelected += _ => UpdateEconomyButtons(); box.AddChild(_storeList);
        _storeIsland = Choice(); _storeProduct = Choice(); _storeName = Field("Nome da loja", 160); _openStore = Act("Abrir loja", UiStyle.ButtonKind.Primary, OpenStore); _closeStore = Act("Fechar loja", UiStyle.ButtonKind.Secondary, CloseStore);
        box.AddChild(Row(_storeIsland, _storeProduct)); box.AddChild(Row(_storeName, _openStore, _closeStore));
        box.AddChild(Note("A loja precisa ficar em terreno do grupo ou em uma ilha sua."));
        page.AddChild(Scroll(box));
        return page;
    }

    // ---- hooks for tests ------------------------------------------------------------------------------------------------------------------------------------------

    public void SetGiveForTesting(string amount, string note) { _giveAmount.Text = amount; _giveNote.Text = note; }
    public void SetTakeForTesting(string amount, string reason, string to) { _takeAmount.Text = amount; _takeReason.Text = reason; for (var i = 0; i < _takeTo.ItemCount; i++) if (_takeTo.GetItemText(i) == to) _takeTo.Select(i); }
    public void SetFreezeForTesting(bool on) => _freezeBox.ButtonPressed = on;
    public void SelectProductForTesting(int index) { _productList.Select(index); UpdateEconomyButtons(); }
    public void SelectStoreForTesting(int index) { _storeList.Select(index); UpdateEconomyButtons(); }
    public void SetProductFormForTesting(string name, string price, string newPrice) { _prodName.Text = name; _prodPrice.Text = price; _newPrice.Text = newPrice; }
    public void SetStoreFormForTesting(string name) => _storeName.Text = name;
    public bool EconomyButtonEnabledForTesting(string which) => which switch { "give" => !_give.Disabled, "take" => !_take.Disabled, "freeze" => !_freezeBox.Disabled, "publish" => !_publish.Disabled, "reprice" => !_reprice.Disabled, "unpublish" => !_unpublish.Disabled, "openStore" => !_openStore.Disabled, "closeStore" => !_closeStore.Disabled, _ => false };
    public string ChestInfoForTesting => _chestInfo.Text;
}
