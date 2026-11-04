using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// Selling, renting and buying land from the "Venda e aluguel" tab of the land window, and the confirmation windows for a purchase or a
/// rental. The client only asks: the server checks who owns what, the price the person saw, the balance and everything else, and does the
/// money and ownership changes in one step.
public partial class LandManager
{
    private LandParcelData? _tradeParcel;
    private Label _tradeNote = null!, _contract = null!, _history = null!, _buySummary = null!, _rentSummary = null!;
    private VBoxContainer _ownerSale = null!, _ownerRent = null!, _contractBox = null!, _buyBox = null!, _rentBox = null!;
    private LineEdit _salePrice = null!, _rentPrice = null!, _rentMin = null!, _rentMax = null!, _rentDeposit = null!;
    private CheckBox _saleObjects = null!;
    private OptionButton _rentDays = null!;
    private Button _cancelSale = null!, _cancelRent = null!, _endContract = null!;
    private FloatingWindow _buyWindow = null!, _rentWindow = null!;
    private Label _buyName = null!, _buyArea = null!, _buyPrice = null!, _buyOwner = null!, _buyLimit = null!, _buyObjects = null!;
    private Label _rentName = null!, _rentArea = null!, _rentTerms = null!, _rentTotal = null!;
    private SpinBox _rentPeriods = null!;
    private CheckBox _rentAuto = null!;
    private static readonly int[] PeriodChoices = [7, 14, 30];

    public static string Coins(long value) => $"L$ {value.ToString("N0", Pt)}";

    // ---- the tab ---------------------------------------------------------------------------------------------

    private Control BuildTradePage()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12); scroll.AddChild(page);

        _tradeNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(430, 0) }; _tradeNote.AddThemeColorOverride("font_color", UiStyle.TextMuted); _tradeNote.AddThemeFontSizeOverride("font_size", 17);
        page.AddChild(_tradeNote);

        // the owner: sell
        _ownerSale = Section(page, "Vender este terreno");
        var saleRow = new HBoxContainer(); saleRow.AddThemeConstantOverride("separation", 10); _ownerSale.AddChild(saleRow);
        saleRow.AddChild(Caption("Preço (L$)")); _salePrice = Field("1000"); saleRow.AddChild(_salePrice);
        _saleObjects = new CheckBox { Text = " Incluir os meus objetos", ButtonPressed = true, TooltipText = "Os objetos que são seus dentro do terreno passam para quem comprar" }; _saleObjects.AddThemeFontSizeOverride("font_size", 17); _ownerSale.AddChild(_saleObjects);
        var saleButtons = new HBoxContainer(); saleButtons.AddThemeConstantOverride("separation", 8); _ownerSale.AddChild(saleButtons);
        saleButtons.AddChild(Act("Colocar à venda", UiStyle.ButtonKind.Primary, OfferSale)); _cancelSale = Act("Retirar da venda", UiStyle.ButtonKind.Secondary, CancelSale); saleButtons.AddChild(_cancelSale);

        // the owner: rent out
        _ownerRent = Section(page, "Alugar este terreno para outras pessoas");
        var rentGrid = new GridContainer { Columns = 2 }; rentGrid.AddThemeConstantOverride("h_separation", 10); rentGrid.AddThemeConstantOverride("v_separation", 6); _ownerRent.AddChild(rentGrid);
        rentGrid.AddChild(Caption("Valor por período (L$)")); _rentPrice = Field("100"); rentGrid.AddChild(_rentPrice);
        rentGrid.AddChild(Caption("Período")); _rentDays = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var days in PeriodChoices) _rentDays.AddItem($"{days} dias"); _rentDays.Select(2); rentGrid.AddChild(_rentDays);
        rentGrid.AddChild(Caption("Mínimo de períodos")); _rentMin = Field("1"); rentGrid.AddChild(_rentMin);
        rentGrid.AddChild(Caption("Máximo de períodos")); _rentMax = Field("12"); rentGrid.AddChild(_rentMax);
        rentGrid.AddChild(Caption("Caução (L$)")); _rentDeposit = Field("0"); rentGrid.AddChild(_rentDeposit);
        var rentButtons = new HBoxContainer(); rentButtons.AddThemeConstantOverride("separation", 8); _ownerRent.AddChild(rentButtons);
        rentButtons.AddChild(Act("Oferecer para aluguel", UiStyle.ButtonKind.Primary, OfferRent)); _cancelRent = Act("Retirar oferta", UiStyle.ButtonKind.Secondary, CancelRent); rentButtons.AddChild(_cancelRent);

        // a running contract (the tenant or the landlord)
        _contractBox = Section(page, "Contrato de aluguel");
        _contract = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(430, 0) }; _contract.AddThemeFontSizeOverride("font_size", 17); _contractBox.AddChild(_contract);
        var contractButtons = new HBoxContainer(); contractButtons.AddThemeConstantOverride("separation", 8); _contractBox.AddChild(contractButtons);
        contractButtons.AddChild(Act("Renovar (1 período)", UiStyle.ButtonKind.Primary, RenewContract)); _endContract = Act("Encerrar contrato", UiStyle.ButtonKind.Secondary, EndContract); contractButtons.AddChild(_endContract);

        // someone else's parcel
        _buyBox = Section(page, "À venda");
        _buySummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(430, 0) }; _buySummary.AddThemeFontSizeOverride("font_size", 17); _buyBox.AddChild(_buySummary);
        _buyBox.AddChild(Act("Comprar terreno…", UiStyle.ButtonKind.Primary, OpenBuy));
        _rentBox = Section(page, "Para alugar");
        _rentSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(430, 0) }; _rentSummary.AddThemeFontSizeOverride("font_size", 17); _rentBox.AddChild(_rentSummary);
        _rentBox.AddChild(Act("Alugar terreno…", UiStyle.ButtonKind.Primary, OpenRent));

        var historyBox = Section(page, "Histórico");
        _history = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(430, 0), Text = "—" }; _history.AddThemeColorOverride("font_color", UiStyle.TextMuted); _history.AddThemeFontSizeOverride("font_size", 16); historyBox.AddChild(_history);

        BuildBuyWindow(); BuildRentWindow();
        return scroll;
    }

    private static VBoxContainer Section(VBoxContainer page, string title)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); page.AddChild(box);
        var heading = new Label { Text = title }; heading.AddThemeFontOverride("font", UiStyle.Bold); heading.AddThemeFontSizeOverride("font_size", 19); box.AddChild(heading);
        return box;
    }

    private static LineEdit Field(string placeholder) { var edit = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 12 }; edit.AddThemeFontSizeOverride("font_size", 18); return edit; }

    private static Button Act(string text, UiStyle.ButtonKind kind, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 42), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 17); button.Pressed += action;
        return button;
    }

    private static bool Live(LandParcelData parcel) => parcel.RentalId is not null && parcel.RentStatus is "active" or "grace";

    private void UpdateTrade(List<LandParcelData> parcels, int free)
    {
        if (_tradeNote is null) return;
        _tradeParcel = parcels.Count == 1 && free == 0 ? parcels[0] : null;
        foreach (var box in new Control[] { _ownerSale, _ownerRent, _contractBox, _buyBox, _rentBox }) box.Visible = false;
        var parcel = _tradeParcel;
        if (parcel is null) { _tradeNote.Text = _selection.Count == 0 ? "Selecione um terreno para ver a venda e o aluguel." : "Selecione um só terreno (inteiro) para vender, alugar ou comprar."; _tradeNote.Visible = true; return; }
        _tradeNote.Visible = false;
        if (parcel.Mine)
        {
            var rented = Live(parcel);
            _ownerSale.Visible = !rented; _ownerRent.Visible = !rented; _contractBox.Visible = rented;
            _cancelSale.Visible = parcel.SalePrice is not null; _cancelRent.Visible = parcel.RentPrice is not null;
            if (parcel.SalePrice is { } price) _salePrice.Text = price.ToString(Pt);
            _tradeNote.Text = parcel.SalePrice is not null ? $"À venda por {Coins(parcel.SalePrice.Value)}." : parcel.RentPrice is not null ? $"Oferecido para aluguel por {Coins(parcel.RentPrice.Value)} a cada {parcel.RentPeriodDays} dias." : rented ? string.Empty : "Este terreno é seu e não está à venda nem para alugar.";
            _tradeNote.Visible = _tradeNote.Text.Length > 0;
        }
        else if (parcel.TenantIsMe) _contractBox.Visible = true;
        else if (parcel.State == "sale") _buyBox.Visible = true;
        else if (parcel.State == "rent") _rentBox.Visible = true;
        else { _tradeNote.Text = "Este terreno não está à venda nem para alugar."; _tradeNote.Visible = true; }

        if (_contractBox.Visible) FillContract(parcel);
        if (_buyBox.Visible) _buySummary.Text = $"“{parcel.Name}” · {LandMathArea(parcel)} m² · {Coins(parcel.SalePrice ?? 0)}\nVendedor: {parcel.OwnerName}" + (string.IsNullOrWhiteSpace(parcel.SaleDescription) ? string.Empty : $"\n{parcel.SaleDescription}");
        if (_rentBox.Visible) _rentSummary.Text = $"“{parcel.Name}” · {LandMathArea(parcel)} m²\n{Coins(parcel.RentPrice ?? 0)} a cada {parcel.RentPeriodDays} dias · de {parcel.RentMinPeriods} a {parcel.RentMaxPeriods} períodos" + (parcel.RentDeposit > 0 ? $" · caução {Coins(parcel.RentDeposit)}" : string.Empty);
    }

    private static string LandMathArea(LandParcelData parcel) => parcel.Area.ToString("0.#", Pt);

    private void FillContract(LandParcelData parcel)
    {
        var status = parcel.RentStatus == "grace" ? "Pagamento pendente (em tolerância: sem construir até renovar)" : "Em dia";
        var who = parcel.TenantIsMe ? $"Alugado por você de {parcel.OwnerName}" : $"Alugado para {parcel.TenantName}";
        _contract.Text = $"{who}\nSituação: {status}\nVence em: {(parcel.RentExpiresAt is { } at ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : "—")}\nPróxima cobrança: {Coins(parcel.RentNextCharge)} por período" + (parcel.RentAutoRenew ? "\nRenovação automática ligada" : string.Empty);
        _endContract.Text = parcel.TenantIsMe ? "Encerrar contrato" : "Encerrar (só depois do vencimento)";
        foreach (var child in _contractBox.GetChildren().OfType<HBoxContainer>().SelectMany(row => row.GetChildren().OfType<Button>())) if (child.Text.StartsWith("Renovar", StringComparison.Ordinal)) child.Visible = parcel.TenantIsMe;
    }

    // ---- sending the requests ----------------------------------------------------------------------------------

    private static string CommerceMessage(int status, string error) => error switch
    {
        "insufficient_balance" => "Saldo insuficiente.",
        "price_changed" => "O preço mudou desde que você abriu: veja o novo valor e tente de novo.",
        "conflict" => "Outra pessoa foi mais rápida: o terreno não está mais disponível.",
        "not_for_sale" or "not_for_rent" => "Este terreno não está mais disponível.",
        "own_parcel" => "Este terreno já é seu.",
        "rented" => "O terreno está alugado no momento.",
        "not_for_you" => "Esta oferta é exclusiva para outra pessoa.",
        "wallet_blocked" => "A carteira está bloqueada.",
        "forbidden" => "Você não tem permissão para isso.",
        "bad_price" => "Preço inválido: use de 1 a 1.000.000.000.",
        "bad_terms" or "bad_periods" => "As condições estão fora do permitido.",
        "still_running" => "O contrato ainda está valendo.",
        "not_found" => "Não encontrei isso: atualize e tente de novo.",
        _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível concluir (HTTP {status})."
    };

    /// Sends a change, then tells the person, reloads the land and the balance.
    private void Mutate(Godot.HttpClient.Method method, string path, object? body, string done)
    {
        Call(method, path, body is null ? null : JsonSerializer.Serialize(body), (status, text) =>
        {
            if (status is >= 200 and < 300) { Say(done); Load(); WalletChanged?.Invoke(); LoadHistory(); }
            else Say(CommerceMessage(status, ErrorCode(text)));
        });
    }

    private static bool TryMoney(string text, out long value) => long.TryParse(text.Trim().Replace(".", string.Empty), System.Globalization.NumberStyles.Integer, Pt, out value) && value > 0;

    private void OfferSale()
    {
        if (_tradeParcel is not { Mine: true } parcel) return;
        if (!TryMoney(_salePrice.Text, out var price)) { Say("Digite o preço em L$ (um número maior que zero)."); return; }
        Mutate(Godot.HttpClient.Method.Post, $"/parcels/{parcel.Id}/sale", new { price, description = string.Empty, includeObjects = _saleObjects.ButtonPressed }, $"“{parcel.Name}” colocado à venda por {Coins(price)}.");
    }

    private void CancelSale() { if (_tradeParcel is { Mine: true } parcel) Mutate(Godot.HttpClient.Method.Delete, $"/parcels/{parcel.Id}/sale", null, "O terreno saiu da venda."); }

    private void OfferRent()
    {
        if (_tradeParcel is not { Mine: true } parcel) return;
        if (!TryMoney(_rentPrice.Text, out var price)) { Say("Digite o valor do aluguel em L$ (um número maior que zero)."); return; }
        if (!int.TryParse(_rentMin.Text.Trim(), out var min) || !int.TryParse(_rentMax.Text.Trim(), out var max) || min < 1 || max < min) { Say("Mínimo e máximo de períodos: números inteiros, com o máximo igual ou maior que o mínimo."); return; }
        if (!long.TryParse(string.IsNullOrWhiteSpace(_rentDeposit.Text) ? "0" : _rentDeposit.Text.Trim(), out var deposit) || deposit < 0) { Say("A caução deve ser zero ou mais."); return; }
        Mutate(Godot.HttpClient.Method.Post, $"/parcels/{parcel.Id}/rental-offer", new { pricePerPeriod = price, periodDays = PeriodChoices[Mathf.Clamp(_rentDays.Selected, 0, PeriodChoices.Length - 1)], minPeriods = min, maxPeriods = max, depositCoins = deposit, graceDays = 3, autoRenewAllowed = true }, $"“{parcel.Name}” oferecido para aluguel.");
    }

    private void CancelRent() { if (_tradeParcel is { Mine: true } parcel) Mutate(Godot.HttpClient.Method.Delete, $"/parcels/{parcel.Id}/rental-offer", null, "A oferta de aluguel foi retirada."); }

    private void RenewContract() { if (_tradeParcel is { RentalId: { } id, TenantIsMe: true }) Mutate(Godot.HttpClient.Method.Post, $"/rentals/{id}/renew", new { periods = 1, idempotencyKey = Guid.NewGuid().ToString() }, "Aluguel renovado por mais um período."); }

    private void EndContract() { if (_tradeParcel is { RentalId: { } id }) Mutate(Godot.HttpClient.Method.Post, $"/rentals/{id}/end", new { }, "Contrato encerrado."); }

    // ---- the confirmation windows ----------------------------------------------------------------------------

    private static (FloatingWindow Window, GridContainer Grid, VBoxContainer Body) ConfirmWindow(string title, string icon)
    {
        var window = new FloatingWindow(title, icon: icon) { Visible = false, Name = title.Replace(' ', '_') };
        window.Place(.5f, .5f, -250, -210, 250, 210);
        var body = window.Content; body.AddThemeConstantOverride("separation", 10);
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 8); body.AddChild(grid);
        return (window, grid, body);
    }

    private static Label ConfirmRow(GridContainer grid, string caption)
    {
        grid.AddChild(Caption(caption)); var value = Value(true); value.CustomMinimumSize = new Vector2(260, 0); grid.AddChild(value);
        return value;
    }

    private void BuildBuyWindow()
    {
        var (window, grid, body) = ConfirmWindow("Comprar terreno", "coin"); _buyWindow = window;
        _buyName = ConfirmRow(grid, "Terreno"); _buyArea = ConfirmRow(grid, "Área"); _buyPrice = ConfirmRow(grid, "Preço"); _buyOwner = ConfirmRow(grid, "Proprietário"); _buyLimit = ConfirmRow(grid, "Limite de construção"); _buyObjects = ConfirmRow(grid, "Objetos incluídos");
        var note = new Label { Text = "O valor é cobrado na hora e o terreno passa a ser seu.", AutowrapMode = TextServer.AutowrapMode.WordSmart }; note.AddThemeColorOverride("font_color", UiStyle.TextMuted); note.AddThemeFontSizeOverride("font_size", 16); body.AddChild(note);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); body.AddChild(row);
        row.AddChild(Act("Cancelar", UiStyle.ButtonKind.Secondary, () => _buyWindow.Visible = false)); row.AddChild(Act("Confirmar compra", UiStyle.ButtonKind.Primary, ConfirmBuy));
        AddChild(window);
    }

    private void OpenBuy()
    {
        if (_tradeParcel is not { SalePrice: { } price } parcel) return;
        _buyName.Text = parcel.Name; _buyArea.Text = $"{LandMathArea(parcel)} m²"; _buyPrice.Text = Coins(price); _buyOwner.Text = parcel.OwnerName;
        _buyLimit.Text = $"{parcel.MaxObjects:N0} objetos".Replace(",", "."); _buyObjects.Text = parcel.SaleIncludesObjects ? "Sim: os objetos do vendedor neste terreno" : "Não: o vendedor leva os objetos dele";
        _buyWindow.Visible = true; _buyWindow.MoveToFront();
    }

    public void ConfirmBuy()
    {
        if (_tradeParcel is not { SalePrice: { } price } parcel) return;
        _buyWindow.Visible = false;
        Mutate(Godot.HttpClient.Method.Post, $"/parcels/{parcel.Id}/buy", new { expectedPrice = price, idempotencyKey = Guid.NewGuid().ToString() }, $"Você comprou “{parcel.Name}”.");
    }

    private void BuildRentWindow()
    {
        var (window, grid, body) = ConfirmWindow("Alugar terreno", "coin"); _rentWindow = window;
        _rentName = ConfirmRow(grid, "Terreno"); _rentArea = ConfirmRow(grid, "Área"); _rentTerms = ConfirmRow(grid, "Condições");
        grid.AddChild(Caption("Períodos")); _rentPeriods = new SpinBox { MinValue = 1, MaxValue = 12, Value = 1, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; grid.AddChild(_rentPeriods);
        _rentPeriods.ValueChanged += _ => UpdateRentTotal();
        _rentTotal = ConfirmRow(grid, "Total agora");
        _rentAuto = new CheckBox { Text = " Renovar sozinho quando vencer", TooltipText = "Cobra mais um período na data do vencimento, se houver saldo" }; _rentAuto.AddThemeFontSizeOverride("font_size", 17); body.AddChild(_rentAuto);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); body.AddChild(row);
        row.AddChild(Act("Cancelar", UiStyle.ButtonKind.Secondary, () => _rentWindow.Visible = false)); row.AddChild(Act("Confirmar aluguel", UiStyle.ButtonKind.Primary, ConfirmRent));
        AddChild(window);
    }

    private void OpenRent()
    {
        if (_tradeParcel is not { RentPrice: { } price } parcel) return;
        _rentName.Text = parcel.Name; _rentArea.Text = $"{LandMathArea(parcel)} m²";
        _rentTerms.Text = $"{Coins(price)} a cada {parcel.RentPeriodDays} dias" + (parcel.RentDeposit > 0 ? $"; caução de {Coins(parcel.RentDeposit)} devolvida ao fim" : string.Empty);
        _rentPeriods.MinValue = parcel.RentMinPeriods; _rentPeriods.MaxValue = parcel.RentMaxPeriods; _rentPeriods.Value = parcel.RentMinPeriods; _rentAuto.ButtonPressed = false;
        UpdateRentTotal(); _rentWindow.Visible = true; _rentWindow.MoveToFront();
    }

    private void UpdateRentTotal() { if (_tradeParcel is { RentPrice: { } price } parcel) _rentTotal.Text = Coins(price * (long)_rentPeriods.Value + parcel.RentDeposit); }

    public void ConfirmRent()
    {
        if (_tradeParcel is not { RentPrice: { } price } parcel) return;
        _rentWindow.Visible = false;
        Mutate(Godot.HttpClient.Method.Post, $"/parcels/{parcel.Id}/rent", new { periods = (int)_rentPeriods.Value, autoRenew = _rentAuto.ButtonPressed, expectedPrice = price, idempotencyKey = Guid.NewGuid().ToString() }, $"Você alugou “{parcel.Name}”.");
    }

    // ---- history -----------------------------------------------------------------------------------------------

    private static string TypeName(string type) => type switch
    {
        "sale" => "Venda", "sale_listed" => "Colocado à venda", "sale_cancelled" => "Saiu da venda", "rent_listed" => "Oferecido para aluguel", "rent_cancelled" => "Oferta de aluguel retirada",
        "rent_start" => "Aluguel iniciado", "rent_payment" => "Pagamento de aluguel", "rent_end" => "Aluguel encerrado", _ => type
    };

    private void LoadHistory()
    {
        if (_history is null) return;
        if (_tradeParcel is not { } parcel || !(parcel.Mine || parcel.TenantIsMe)) { _history.Text = "O histórico aparece para o dono e para quem aluga."; return; }
        Call(Godot.HttpClient.Method.Get, $"/parcels/{parcel.Id}/history", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { _history.Text = "Não foi possível carregar o histórico."; return; }
            try
            {
                using var document = JsonDocument.Parse(body);
                var lines = document.RootElement.EnumerateArray().Take(12).Select(item =>
                {
                    var when = DateTimeOffset.TryParse(item.GetProperty("at").GetString(), out var at) ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : "—";
                    var amount = item.TryGetProperty("amount", out var a) && a.TryGetInt64(out var value) && value > 0 ? $" · {Coins(value)}" : string.Empty;
                    return $"{when} · {TypeName(item.GetProperty("type").GetString() ?? string.Empty)}{amount}";
                }).ToList();
                _history.Text = lines.Count == 0 ? "Nada ainda." : string.Join("\n", lines);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _history.Text = "Resposta inesperada do servidor."; }
        });
    }

    // ---- tests -------------------------------------------------------------------------------------------------

    public string TradeNoteForTesting => _tradeNote.Visible ? _tradeNote.Text : string.Empty;
    public bool OwnerSaleVisibleForTesting => _ownerSale.Visible;
    public bool OwnerRentVisibleForTesting => _ownerRent.Visible;
    public bool BuyBoxVisibleForTesting => _buyBox.Visible;
    public bool RentBoxVisibleForTesting => _rentBox.Visible;
    public bool ContractVisibleForTesting => _contractBox.Visible;
    public string ContractTextForTesting => _contract.Text;
    public string BuySummaryForTesting => _buySummary.Text;
    public string HistoryForTesting => _history.Text;
    public bool BuyWindowVisibleForTesting => _buyWindow.Visible;
    public bool RentWindowVisibleForTesting => _rentWindow.Visible;
    public string RentTotalForTesting => _rentTotal.Text;
    public void SetSaleForTesting(string price, bool includeObjects) { _salePrice.Text = price; _saleObjects.ButtonPressed = includeObjects; }
    public void SetRentOfferForTesting(string price, int daysIndex, string min, string max, string deposit) { _rentPrice.Text = price; _rentDays.Select(daysIndex); _rentMin.Text = min; _rentMax.Text = max; _rentDeposit.Text = deposit; }
    public void OfferSaleForTesting() => OfferSale();
    public void CancelSaleForTesting() => CancelSale();
    public void OfferRentForTesting() => OfferRent();
    public void CancelRentForTesting() => CancelRent();
    public void OpenBuyForTesting() => OpenBuy();
    public void OpenRentForTesting(int periods) { OpenRent(); _rentPeriods.Value = periods; }
    public void RenewForTesting() => RenewContract();
    public void EndForTesting() => EndContract();
    public void ShowTradeForTesting() => _infoTabs.Select(2);
}
