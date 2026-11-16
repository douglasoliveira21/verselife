using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Items;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The Items and Stores tabs. Items: filters by price, creator and rights (copy, modify, transfer), the chosen item with its creator, seller, price, what the buyer gets, and a purchase that shows the price and the
/// rights, checks the balance, asks once, and is sent with one key so pressing twice cannot charge twice. Stores: virtual or in the world (with the place), owner, category, how many things they sell and a few to see,
/// "Ver produtos" (the catalog is the items of that owner), visiting the physical ones, and a form to open or change my own virtual store. What can be bought or seen is decided by the server.
/// </summary>
public partial class SearchWindow
{
    private static readonly (string Key, string Label)[] StoreCategories = [("moda", "Moda"), ("avatares", "Avatares"), ("construcao", "Construção"), ("decoracao", "Decoração"), ("animacoes", "Animações"), ("veiculos", "Veículos"), ("tecnologia", "Tecnologia"), ("servicos", "Serviços"), ("outros", "Outros")];
    private static string StoreCategoryLabel(string key) => StoreCategories.FirstOrDefault(c => c.Key == key).Label ?? key;

    // filters
    private LineEdit _minPrice = null!, _maxPrice = null!, _creator = null!;
    private CheckBox _rightCopy = null!, _rightModify = null!, _rightTransfer = null!;
    private OptionButton _storeKind = null!;
    private Button _newStore = null!, _clearOwner = null!;
    private HBoxContainer _ownerChip = null!;
    private Label _ownerChipText = null!;
    private Guid _ownerFilter = Guid.Empty;
    private string _ownerFilterName = string.Empty;
    private bool _keepOwnerOnTab;

    // item
    private VBoxContainer _itemBox = null!;
    private Button _buy = null!, _creatorButton = null!, _sellerButton = null!, _visitShop = null!, _shareItem = null!;
    private int _itemSeq;
    private Guid _itemId, _creatorId, _sellerId, _shopId;
    private string _sellerKind = string.Empty, _buyKey = string.Empty;
    private long _itemPrice;
    private bool _canBuy, _buying;
    private string _itemName = string.Empty, _itemRights = string.Empty;

    // store
    private VBoxContainer _storeBox = null!;
    private Button _viewProducts = null!, _storeOwner = null!, _editStore = null!, _shareStore = null!;
    private int _storeSeq;
    private Guid _storeId, _storeOwnerId;
    private JsonElement _storeData;

    private void BuildMarketFilters(Control row)
    {
        LineEdit Field(string placeholder, int width) { var f = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(width, 36), MaxLength = 12 }; f.AddThemeFontSizeOverride("font_size", 14); f.TextSubmitted += _ => Search(); row.AddChild(f); return f; }
        _minPrice = Field("Preço mín.", 100); _maxPrice = Field("Preço máx.", 100);
        _creator = new LineEdit { PlaceholderText = "Criador", CustomMinimumSize = new Vector2(130, 36), MaxLength = 40 }; _creator.AddThemeFontSizeOverride("font_size", 14); _creator.TextSubmitted += _ => Search(); row.AddChild(_creator);
        CheckBox Right(string text, string tip) { var c = new CheckBox { Text = " " + text, FocusMode = Control.FocusModeEnum.None, TooltipText = tip }; c.Toggled += _ => Search(); row.AddChild(c); return c; }
        _rightCopy = Right("Copiar", "Só itens que o comprador pode copiar"); _rightModify = Right("Modificar", "Só itens que o comprador pode modificar"); _rightTransfer = Right("Transferir", "Só itens que o comprador pode passar adiante");
        _storeKind = Choice("Virtuais e físicas", "Só virtuais", "Só físicas"); _storeKind.TooltipText = "Lojas sem endereço ou com lugar para visitar"; _storeKind.ItemSelected += _ => Search(); row.AddChild(_storeKind);
        _newStore = Action("Abrir minha loja", UiStyle.ButtonKind.Secondary, () => OpenStoreForm(false), 36); _newStore.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; _newStore.CustomMinimumSize = new Vector2(150, 36); row.AddChild(_newStore);
    }

    /// <summary>Under the filters: when only the products of one shop are shown, a line that says so and a way back to everything.</summary>
    private void BuildOwnerChip(Control parent)
    {
        _ownerChip = new HBoxContainer { Visible = false }; _ownerChip.AddThemeConstantOverride("separation", 8); parent.AddChild(_ownerChip);
        _ownerChipText = Text("", false, 14); _ownerChipText.WithColor(UiStyle.AccentText); _ownerChip.AddChild(_ownerChipText);
        _clearOwner = new Button { Text = "✕ Ver todos os itens", Flat = true, FocusMode = Control.FocusModeEnum.None }; _clearOwner.AddThemeColorOverride("font_color", UiStyle.Orange); _clearOwner.Pressed += () => { _ownerFilter = Guid.Empty; UpdateChrome(); Search(); }; _ownerChip.AddChild(_clearOwner);
    }

    /// <summary>The filters of the Items and Stores tabs as the search sends them.</summary>
    private string? MarketExtra()
    {
        var parts = new List<string>(); var tab = _tabs.Selected;
        if (tab == 5)
        {
            if (long.TryParse(_minPrice.Text.Trim(), out var min) && min >= 0) parts.Add("minPrice=" + min);
            if (long.TryParse(_maxPrice.Text.Trim(), out var max) && max >= 0) parts.Add("maxPrice=" + max);
            var rights = new List<string>(); if (_rightCopy.ButtonPressed) rights.Add("copy"); if (_rightModify.ButtonPressed) rights.Add("modify"); if (_rightTransfer.ButtonPressed) rights.Add("transfer");
            if (rights.Count > 0) parts.Add("rights=" + string.Join("%2C", rights));
            if (_creator.Text.Trim() is { Length: > 0 } creator) parts.Add("creator=" + Uri.EscapeDataString(creator));
            if (_ownerFilter != Guid.Empty) parts.Add("owner=" + _ownerFilter);
        }
        else if (tab == 6 && _storeKind.Selected > 0) parts.Add("located=" + (_storeKind.Selected == 2 ? "true" : "false"));
        return parts.Count == 0 ? null : string.Join("&", parts);
    }

    private void ResetMarketFilters()
    {
        _minPrice.Text = _maxPrice.Text = _creator.Text = string.Empty; _rightCopy.SetPressedNoSignal(false); _rightModify.SetPressedNoSignal(false); _rightTransfer.SetPressedNoSignal(false); _storeKind.Select(0);
        if (!_keepOwnerOnTab) _ownerFilter = Guid.Empty;
    }

    /// <summary>The grey line of an item or store card.</summary>
    private static string MarketMeta(SearchHit hit)
    {
        if (hit.Kind == SearchKinds.Product)
        {
            var parts = new List<string> { "Item" }; if (hit.Category.Length > 0) parts.Add(hit.Category); parts.Add(PriceText(hit.PriceCoins)); if (hit.Rights is { } mask) parts.Add("Permissões: " + ItemRights.Letters(mask));
            return string.Join(" · ", parts);
        }
        var owner = hit.Subtitle.StartsWith("Loja virtual · ", StringComparison.Ordinal) ? hit.Subtitle["Loja virtual · ".Length..] : string.Empty;
        var place = hit.Subtitle.StartsWith("Loja em ", StringComparison.Ordinal) ? hit.Subtitle["Loja em ".Length..] : string.Empty;
        var kind = hit.IslandId is null ? "Loja virtual" : "Loja física" + (place.Length > 0 ? " em " + place : string.Empty);
        return string.Join(" · ", new[] { kind, owner.Length > 0 ? "de " + owner : string.Empty, hit.Category.Length > 0 && hit.IslandId is null ? StoreCategoryLabel(hit.Category) : string.Empty }.Where(t => t.Length > 0));
    }

    // ---- actions of an item and of a store ---------------------------------------------------------------------------------------

    private void BuildMarketActions(Control parent)
    {
        _itemBox = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _itemBox.AddThemeConstantOverride("separation", 6); parent.AddChild(_itemBox);
        _buy = Action("Comprar", UiStyle.ButtonKind.Primary, OnBuy); _itemBox.AddChild(_buy);
        _creatorButton = Action("Ver criador", UiStyle.ButtonKind.Secondary, () => { if (_creatorId != Guid.Empty) OpenProfile?.Invoke(_creatorId); }); _itemBox.AddChild(_creatorButton);
        _sellerButton = Action("Ver vendedor", UiStyle.ButtonKind.Secondary, () => { if (_sellerKind == "group") OpenGroup?.Invoke(_sellerId); else if (_sellerId != Guid.Empty) OpenProfile?.Invoke(_sellerId); }); _itemBox.AddChild(_sellerButton);
        _visitShop = Action("Visitar loja", UiStyle.ButtonKind.Secondary, () => { if (_shopId != Guid.Empty) ShowStoreById(_shopId); }); _itemBox.AddChild(_visitShop);
        _shareItem = Action("Compartilhar", UiStyle.ButtonKind.Secondary, () => { if (Selected is { Kind: SearchKinds.Product } p) { DisplayServer.ClipboardSet(SearchLinks.For(SearchKinds.Product, p.Id)); _note.Text = "Link do item copiado."; } }); _itemBox.AddChild(_shareItem);

        _storeBox = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _storeBox.AddThemeConstantOverride("separation", 6); parent.AddChild(_storeBox);
        _viewProducts = Action("Ver produtos", UiStyle.ButtonKind.Primary, OnViewProducts); _storeBox.AddChild(_viewProducts);
        _storeOwner = Action("Ver proprietário", UiStyle.ButtonKind.Secondary, () => { if (_storeOwnerId != Guid.Empty) OpenProfile?.Invoke(_storeOwnerId); }); _storeBox.AddChild(_storeOwner);
        _editStore = Action("Editar minha loja", UiStyle.ButtonKind.Secondary, () => OpenStoreForm(true)); _editStore.Visible = false; _storeBox.AddChild(_editStore);
        _shareStore = Action("Compartilhar", UiStyle.ButtonKind.Secondary, () => { if (Selected is { Kind: SearchKinds.Store } s) { DisplayServer.ClipboardSet(SearchLinks.For(SearchKinds.Store, s.Id)); _note.Text = "Link da loja copiado."; } }); _storeBox.AddChild(_shareStore);
    }

    // ---- an item -----------------------------------------------------------------------------------------------------------------------

    private static string RightsWords(JsonElement flags)
    {
        var words = new List<string>(); if (Flag(flags, "copy")) words.Add("copiar"); if (Flag(flags, "modify")) words.Add("modificar"); if (Flag(flags, "transfer")) words.Add("transferir"); if (Flag(flags, "resell")) words.Add("revender");
        return words.Count == 0 ? "nada além de usar" : string.Join(", ", words);
    }

    private void LoadItem(SearchHit hit)
    {
        var seq = ++_itemSeq; _itemId = hit.Id; _creatorId = _sellerId = _shopId = Guid.Empty; _canBuy = false; _itemPrice = hit.PriceCoins ?? 0; _itemName = hit.Title; _extra.Text = "Carregando o item…"; UpdateItemButtons(string.Empty);
        Send(Godot.HttpClient.Method.Get, SearchApi.ItemPath(hit.Id), null, (status, body) =>
        {
            if (seq != _itemSeq || Selected?.Id != hit.Id) return;
            if (status is < 200 or >= 300) { _extra.Text = status == 404 ? "Este item não está mais à venda." : "Não foi possível carregar o item agora."; UpdateItemButtons(status == 404 ? "gone" : string.Empty); return; }
            try
            {
                using var document = JsonDocument.Parse(body); var p = document.RootElement;
                _itemPrice = Long(p, "priceCoins"); _canBuy = Flag(p, "canBuy"); _itemName = Str(p, "name");
                if (p.TryGetProperty("creator", out var creator) && Guid.TryParse(Str(creator, "id"), out var creatorId)) _creatorId = creatorId;
                if (p.TryGetProperty("seller", out var seller) && Guid.TryParse(Str(seller, "id"), out var sellerId)) { _sellerId = sellerId; _sellerKind = Str(seller, "kind"); }
                if (p.TryGetProperty("store", out var store) && store.ValueKind == JsonValueKind.Object && Guid.TryParse(Str(store, "id"), out var shopId)) _shopId = shopId;
                if (Str(p, "description") is { Length: > 0 } description) _pDescription.Text = description;
                p.TryGetProperty("rights", out var rights); p.TryGetProperty("nextRights", out var next); p.TryGetProperty("asset", out var asset); _itemRights = RightsWords(rights);
                var size = asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("byteSize", out var bytes) && bytes.TryGetInt64(out var n) ? $" · {Size(n)}" : string.Empty;
                var lines = new List<string>
                {
                    "Preço: " + (_itemPrice > 0 ? $"L$ {_itemPrice:N0}" : "gratuito"),
                    creator.ValueKind == JsonValueKind.Object ? "Criador(a): " + Str(creator, "displayName") : string.Empty,
                    seller.ValueKind == JsonValueKind.Object ? "Vendedor(a): " + Str(seller, "name") + (_sellerKind == "group" ? " (grupo)" : string.Empty) : string.Empty,
                    "Você poderá: " + _itemRights + ".", "E passar adiante: " + RightsWords(next) + ".",
                    Flag(p, "allowGift") ? "Pode ser comprado de presente." : "Não pode ser comprado de presente.",
                    Flag(p, "resale") ? "Revenda de um item usado." : string.Empty,
                    asset.ValueKind == JsonValueKind.Object ? "Tipo: " + Str(asset, "category") + size + "." : string.Empty,
                    Flag(p, "owned") ? "Você já tem este item." : Flag(p, "isOwn") ? "Este item é seu." : string.Empty
                };
                _extra.Text = string.Join("\n", lines.Where(l => l.Length > 0)); UpdateItemButtons(Flag(p, "isOwn") ? "own" : Flag(p, "owned") && _itemPrice == 0 ? "owned" : Flag(p, "available") ? "" : "gone");
            }
            catch (JsonException) { _extra.Text = "Resposta inesperada do servidor."; }
        });
    }

    private static string Size(long bytes) => bytes switch { < 1024 => $"{bytes} B", < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB", _ => $"{bytes / 1024.0 / 1024.0:0.#} MB" };

    private void UpdateItemButtons(string state)
    {
        (_buy.Text, _buy.Disabled) = _buying ? ("Comprando…", true) : state == "own" ? ("Este item é seu", true) : state == "owned" ? ("Você já pegou este item", true) : state == "gone" ? ("Item indisponível", true)
            : _canBuy ? (_itemPrice > 0 ? $"Comprar por L$ {_itemPrice:N0}" : "Pegar grátis", false) : ("Comprar", true);
        _creatorButton.Visible = _creatorId != Guid.Empty; _sellerButton.Visible = _sellerId != Guid.Empty && _sellerId != _creatorId; _visitShop.Visible = _shopId != Guid.Empty;
    }

    private static string BuyWords(int status, string code) => code switch
    {
        "insufficient_balance" => "Saldo insuficiente para esta compra.", "already_owned" => "Você já tem este item.", "cannot_buy_own_product" => "Você não pode comprar o seu próprio item.", "key_reused" => "Esta compra já foi enviada. Confira o seu inventário.",
        "gift_not_allowed" => "Este item não pode ser comprado de presente.", "recipient_not_found" => "A pessoa que receberia o item não foi encontrada.",
        _ => status == 404 ? "Este item não está mais à venda." : status == 403 ? "Sua carteira está bloqueada." : status == 0 ? "Sem conexão com o servidor. Confira o inventário antes de tentar de novo." : status == 429 ? "Calma: tente de novo daqui a pouco." : $"Não foi possível comprar agora ({(code.Length > 0 ? code : "HTTP " + status)})."
    };

    /// <summary>Asks the balance first (when it can), then shows the price and the rights and asks once. A key per attempt makes pressing again harmless.</summary>
    private void OnBuy()
    {
        if (!_canBuy || _buying || Selected is not { Kind: SearchKinds.Product } hit) return;
        void Proceed(long? balance)
        {
            if (balance is { } have && have < _itemPrice) { _note.Text = $"Saldo insuficiente: você tem L$ {have:N0} e o item custa L$ {_itemPrice:N0}."; return; }
            var price = _itemPrice > 0 ? $"L$ {_itemPrice:N0}, debitados agora da sua carteira" : "grátis"; var id = hit.Id; var name = _itemName;
            AskYes(_itemPrice > 0 ? "Confirmar compra" : "Pegar item", $"{name}\nValor: {price}.\nVocê poderá: {_itemRights}.\nA compra não pode ser desfeita por aqui.", _itemPrice > 0 ? "Comprar" : "Pegar", () => Purchase(id, name));
        }
        if (Actions is { } people) people.Balance(Proceed); else Proceed(null);
    }

    private void Purchase(Guid id, string name)
    {
        if (_buying) return; if (_buyKey.Length == 0) _buyKey = Guid.NewGuid().ToString("N"); _buying = true; UpdateItemButtons(string.Empty); _note.Text = "Comprando…";
        Send(Godot.HttpClient.Method.Post, $"/api/v1/economy/products/{id}/purchase", JsonSerializer.Serialize(new { idempotencyKey = _buyKey }), (status, body) =>
        {
            _buying = false;
            if (status is >= 200 and < 300)
            {
                _buyKey = string.Empty; _note.Text = $"Compra feita! “{name}” foi para o seu inventário."; Actions?.NotifyPaymentMade();
                if (Selected is { Kind: SearchKinds.Product } now && now.Id == id) LoadItem(now); return;
            }
            if (status != 0) _buyKey = string.Empty; // an answer: the next attempt is a new one; no answer: the same key is kept in case the first one went through
            _note.Text = BuyWords(status, ErrorCode(body)); if (Selected is { Kind: SearchKinds.Product } current && current.Id == id) LoadItem(current);
        });
    }

    // ---- a store -----------------------------------------------------------------------------------------------------------------------

    private void LoadStore(SearchHit hit)
    {
        var seq = ++_storeSeq; _storeId = hit.Id; _storeOwnerId = Guid.Empty; _extra.Text = "Carregando a loja…"; _editStore.Visible = false;
        Send(Godot.HttpClient.Method.Get, SearchApi.StorePath(hit.Id), null, (status, body) =>
        {
            if (seq != _storeSeq || Selected?.Id != hit.Id) return;
            if (status is < 200 or >= 300) { _extra.Text = status == 404 ? "Esta loja não está mais disponível." : "Não foi possível carregar a loja agora."; return; }
            try
            {
                using var document = JsonDocument.Parse(body); var s = document.RootElement.Clone(); _storeData = s;
                if (Guid.TryParse(Str(s, "ownerId"), out var ownerId)) _storeOwnerId = ownerId;
                var physical = Str(s, "kind") == "physical"; var count = Int(s, "productCount");
                if (Str(s, "description") is { Length: > 0 } description) _pDescription.Text = description;
                if (Str(s, "bannerUrl") is { Length: > 0 } banner && SearchApi.IsLoadableImageUrl(banner)) { _thumbQueue.Enqueue((banner, _pImage)); NextThumbnail(); }
                var lines = new List<string>();
                if (s.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.Object) lines.Add("Proprietário(a): " + Str(owner, "displayName") + ".");
                lines.Add(physical ? "Loja física" + (s.TryGetProperty("island", out var island) && island.ValueKind == JsonValueKind.Object ? " em " + Str(island, "name") : string.Empty) + "." : "Loja virtual: sem endereço no mundo.");
                lines.Add(string.Join(" · ", new[] { Str(s, "categoryLabel") }.Where(t => t.Length > 0)));
                lines.Add(count == 0 ? "Nenhum produto à venda agora." : $"{count} {(count == 1 ? "produto publicado" : "produtos publicados")}.");
                if (s.TryGetProperty("featured", out var featured) && featured.ValueKind == JsonValueKind.Array && featured.GetArrayLength() > 0) { lines.Add("Em destaque:"); foreach (var f in featured.EnumerateArray()) lines.Add("• " + Str(f, "name") + " — " + (Long(f, "priceCoins") > 0 ? $"L$ {Long(f, "priceCoins"):N0}" : "gratuito")); }
                _extra.Text = string.Join("\n", lines.Where(l => l.Length > 0)); _viewProducts.Disabled = count == 0; _editStore.Visible = Flag(s, "isOwner") && !physical; _storeOwner.Visible = _storeOwnerId != Guid.Empty;
            }
            catch (JsonException) { _extra.Text = "Resposta inesperada do servidor."; }
        });
    }

    /// <summary>The catalog of a store: the items that its owner sells, on the Items tab.</summary>
    private void OnViewProducts()
    {
        if (Selected is not { Kind: SearchKinds.Store } store || _storeOwnerId == Guid.Empty) return;
        _ownerFilter = _storeOwnerId; _ownerFilterName = store.Title; _keepOwnerOnTab = true; _query.Text = string.Empty;
        if (_tabs.Selected == 5) { _keepOwnerOnTab = false; UpdateChrome(); Search(); } else { _tabs.Select(5); _keepOwnerOnTab = false; }
    }

    /// <summary>Opens a store by its id on the Stores tab (from an item): the store is shown alone, chosen.</summary>
    private void ShowStoreById(Guid id)
    {
        Send(Godot.HttpClient.Method.Get, SearchApi.StorePath(id), null, (status, body) =>
        {
            if (status is < 200 or >= 300) { _note.Text = status == 404 ? "Esta loja não está mais disponível." : Failure(status); return; }
            try
            {
                using var document = JsonDocument.Parse(body); var s = document.RootElement; Guid? island = null; float? x = null, z = null;
                if (s.TryGetProperty("island", out var i) && i.ValueKind == JsonValueKind.Object && Guid.TryParse(Str(i, "id"), out var islandId)) { island = islandId; x = s.TryGetProperty("x", out var px) && px.ValueKind == JsonValueKind.Number ? px.GetSingle() : null; z = s.TryGetProperty("z", out var pz) && pz.ValueKind == JsonValueKind.Number ? pz.GetSingle() : null; }
                var hit = new SearchHit(SearchKinds.Store, "Loja", id, Str(s, "name"), island is null ? "Loja virtual" : "Loja em " + (i.ValueKind == JsonValueKind.Object ? Str(i, "name") : string.Empty), Str(s, "description"), Str(s, "category"), null, island, x, z, 0, false);
                _keepHit = hit; _tabs.Select(6);
            }
            catch (JsonException) { _note.Text = "Resposta inesperada do servidor."; }
        });
    }

    private SearchHit? _keepHit;

    /// <summary>After the Stores tab loaded, the store asked for (from an item) is put in front and chosen.</summary>
    private void ShowKeptHit()
    {
        if (_keepHit is not { } hit) return; _keepHit = null; _seq++; Done();
        Fill([hit], SearchPageData.Empty); Select(0); _count.Text = "1 loja";
    }

    // ---- opening and changing my own store ---------------------------------------------------------------------------------------------

    private FloatingWindow? _storeForm;
    private LineEdit _sName = null!, _sLogo = null!, _sBanner = null!;
    private TextEdit _sDescription = null!;
    private OptionButton _sCategory = null!;
    private CheckBox _sOpen = null!;
    private Label _sNote = null!;
    private Button _sDelete = null!;
    private Guid _editingStore;

    private void OpenStoreForm(bool editing)
    {
        if (_storeForm is not null && GodotObject.IsInstanceValid(_storeForm)) _storeForm.QueueFree();
        _editingStore = editing && Selected is { Kind: SearchKinds.Store } current ? current.Id : Guid.Empty;
        _storeForm = new FloatingWindow(_editingStore == Guid.Empty ? "Abrir minha loja" : "Editar minha loja", icon: "bag") { Name = "SearchStoreForm" }; _storeForm.Place(.5f, .5f, -300, -300, 300, 300);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _storeForm.Content.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 6); scroll.AddChild(box);
        void Caption(string text) => box.AddChild(Text(text, true, 13));
        Caption("Uma loja virtual reúne o que você vende no mercado. Ela aparece na busca enquanto estiver aberta e você tiver algo à venda.");
        Caption("Nome"); _sName = new LineEdit { MaxLength = 60, PlaceholderText = "Ex.: Moda da Luna", CustomMinimumSize = new Vector2(0, 38) }; box.AddChild(_sName);
        Caption("Descrição"); _sDescription = new TextEdit { CustomMinimumSize = new Vector2(0, 90), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Conte o que a sua loja vende…" }; box.AddChild(_sDescription);
        _sDescription.TextChanged += () => { if (_sDescription.Text.Length > 600) _sDescription.Text = _sDescription.Text[..600]; };
        Caption("Categoria"); _sCategory = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; foreach (var (_, label) in StoreCategories) _sCategory.AddItem(label); box.AddChild(_sCategory);
        Caption("Logotipo (endereço https, opcional)"); _sLogo = new LineEdit { MaxLength = 1024, PlaceholderText = "https://…", CustomMinimumSize = new Vector2(0, 38) }; box.AddChild(_sLogo);
        Caption("Banner (endereço https, opcional)"); _sBanner = new LineEdit { MaxLength = 1024, PlaceholderText = "https://…", CustomMinimumSize = new Vector2(0, 38) }; box.AddChild(_sBanner);
        _sOpen = new CheckBox { Text = " Loja aberta", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; box.AddChild(_sOpen);
        _sNote = Text("", false, 14); _sNote.AddThemeColorOverride("font_color", UiStyle.Orange); box.AddChild(_sNote);
        if (_editingStore != Guid.Empty && _storeData.ValueKind == JsonValueKind.Object)
        {
            _sName.Text = Str(_storeData, "name"); _sDescription.Text = Str(_storeData, "description"); _sLogo.Text = Str(_storeData, "logoUrl"); _sBanner.Text = Str(_storeData, "bannerUrl"); _sOpen.ButtonPressed = Flag(_storeData, "open");
            var key = Str(_storeData, "category"); var index = Array.FindIndex(StoreCategories, c => c.Key == key); if (index >= 0) _sCategory.Select(index);
        }
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); _storeForm.Content.AddChild(row);
        row.AddChild(Action(_editingStore == Guid.Empty ? "Abrir loja" : "Salvar", UiStyle.ButtonKind.Primary, SubmitStoreForm));
        _sDelete = Action("Excluir loja", UiStyle.ButtonKind.Secondary, DeleteStore); _sDelete.Visible = _editingStore != Guid.Empty; row.AddChild(_sDelete);
        row.AddChild(Action("Cancelar", UiStyle.ButtonKind.Secondary, () => _storeForm?.QueueFree()));
        AddChild(_storeForm); _storeForm.MoveToFront();
    }

    private static string StoreFormWords(string code) => code switch
    {
        "bad_name" => "O nome precisa ter de 3 a 60 letras.", "bad_description" => "A descrição passou de 600 letras.", "bad_category" => "Escolha uma categoria.", "bad_image" => "As imagens precisam ser endereços https.",
        "too_many_stores" => "Você já tem o máximo de 3 lojas.", "name_taken" => "Você já tem uma loja com esse nome.", "not_found" => "Esta loja não está mais disponível.", _ => "Não foi possível salvar agora."
    };

    private void SubmitStoreForm()
    {
        var body = JsonSerializer.Serialize(new { name = _sName.Text.Trim(), description = _sDescription.Text.Trim(), category = StoreCategories[_sCategory.Selected].Key, logoUrl = _sLogo.Text.Trim(), bannerUrl = _sBanner.Text.Trim(), isOpen = _sOpen.ButtonPressed });
        _sNote.Text = "Salvando…"; var editing = _editingStore != Guid.Empty;
        Send(editing ? Godot.HttpClient.Method.Put : Godot.HttpClient.Method.Post, editing ? SearchApi.StorePath(_editingStore) : "/api/v1/discovery/stores", body, (status, text) =>
        {
            if (status is < 200 or >= 300) { _sNote.Text = status == 0 ? "Sem conexão com o servidor." : StoreFormWords(ErrorCode(text)); return; }
            if (_storeForm is not null && GodotObject.IsInstanceValid(_storeForm)) _storeForm.QueueFree(); _storeForm = null;
            _flash = editing ? "Loja atualizada." : "Loja aberta. Ela aparece na busca assim que você tiver algo à venda."; _note.Text = _flash; if (_tabs.Selected == 6) Search(); else _tabs.Select(6);
        });
    }

    private void DeleteStore()
    {
        var id = _editingStore; if (id == Guid.Empty) return;
        AskYes("Excluir loja", "Excluir esta loja? Os seus produtos continuam à venda; só a loja some.", "Excluir", () => Send(Godot.HttpClient.Method.Delete, SearchApi.StorePath(id), null, (status, text) =>
        {
            if (status is < 200 or >= 300) { _sNote.Text = StoreFormWords(ErrorCode(text)); return; }
            if (_storeForm is not null && GodotObject.IsInstanceValid(_storeForm)) _storeForm.QueueFree(); _storeForm = null; _flash = "Loja excluída."; _note.Text = _flash; Search();
        }));
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public bool ItemBoxVisibleForTesting => _itemBox.Visible;
    public bool StoreBoxVisibleForTesting => _storeBox.Visible;
    public string BuyTextForTesting => _buy.Text;
    public bool BuyEnabledForTesting => !_buy.Disabled;
    public bool CreatorButtonVisibleForTesting => _creatorButton.Visible;
    public bool VisitShopVisibleForTesting => _visitShop.Visible;
    public bool ViewProductsEnabledForTesting => !_viewProducts.Disabled;
    public bool EditStoreVisibleForTesting => _editStore.Visible;
    public bool OwnerChipVisibleForTesting => _ownerChip.Visible;
    public string OwnerChipTextForTesting => _ownerChipText.Text;
    public string MarketMetaForTesting(int row) => _metaLabels[row].Text;
    public void SetPriceRangeForTesting(string min, string max) { _minPrice.Text = min; _maxPrice.Text = max; }
    public void SetCreatorForTesting(string text) => _creator.Text = text;
    public void SetRightsForTesting(bool copy, bool modify, bool transfer) { _rightCopy.SetPressedNoSignal(copy); _rightModify.SetPressedNoSignal(modify); _rightTransfer.SetPressedNoSignal(transfer); }
    public void SetStoreKindForTesting(int index) => _storeKind.Selected = index;
    public bool MarketFilterShowsForTesting(string which) => which switch { "price" => _minPrice.Visible, "creator" => _creator.Visible, "rights" => _rightCopy.Visible, "kind" => _storeKind.Visible, "new" => _newStore.Visible, _ => false };
    public void PressBuyForTesting() => OnBuy();
    public void PressCreatorForTesting() => _creatorButton.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressSellerForTesting() => _sellerButton.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressVisitShopForTesting() => _visitShop.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressViewProductsForTesting() => OnViewProducts();
    public void PressStoreOwnerForTesting() => _storeOwner.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressClearOwnerForTesting() => _clearOwner.EmitSignal(BaseButton.SignalName.Pressed);
    public void OpenStoreFormForTesting(bool editing) => OpenStoreForm(editing);
    public void FillStoreFormForTesting(string name, string description, int category, bool open) { _sName.Text = name; _sDescription.Text = description; _sCategory.Select(category); _sOpen.ButtonPressed = open; }
    public void SubmitStoreFormForTesting() => SubmitStoreForm();
    public void DeleteStoreForTesting() => DeleteStore();
    public string StoreFormNoteForTesting => _sNote.Text;
    public bool StoreFormOpenForTesting => _storeForm is not null && GodotObject.IsInstanceValid(_storeForm) && !_storeForm.IsQueuedForDeletion();
}
