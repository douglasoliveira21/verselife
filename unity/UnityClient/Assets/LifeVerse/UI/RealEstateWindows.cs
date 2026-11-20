using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>Numbers and words of the real estate windows.</summary>
    public static class LandText
    {
        private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        public static string Money(long value) => "VC$ " + value.ToString("N0", Pt);
        public static string Area(double value) => value.ToString("N0", Pt) + " m²";
        public static string Period(int days) => days == 30 ? "mensal" : days == 7 ? "semanal" : days + " dias";
        public static string Category(string category) => category switch { "comercial" => "Comercial", "residencial" => "Residencial", "" => "", _ => char.ToUpperInvariant(category[0]) + category.Substring(1) };
    }

    /// <summary>One listing of the real estate market, as the server sent it.</summary>
    public sealed class MarketListing
    {
        public Guid ParcelId, IslandId; public string IslandName = "", Code = "", Name = "", Description = "", Category = "", Offer = "sale", State = "sale", OwnerName = "", Kind = "parcel", Region = "";
        public double Area; public int MaxObjects, RentPeriodDays, RentMinPeriods = 1, RentMaxPeriods = 1; public long Price, RentDeposit; public bool CanVisit, AutoRenewAllowed;
        public bool IsIsland => Kind == "island";
        public string Title => Code.Length > 0 ? Code + " — " + Name : Name;
        public string TypeText => IsIsland ? (Region == "homestead" ? "Ilha Homestead" : "Ilha Full Region") : "Terreno " + LandText.Category(Category);
        public string PriceText => Offer == "sale" ? LandText.Money(Price) : LandText.Money(Price) + " / " + LandText.Period(RentPeriodDays);
        public string Path => IsIsland ? "/api/v1/worlds/islands/" + IslandId + "/real-estate" : "/api/v1/worlds/islands/" + IslandId + "/land/parcels/" + ParcelId;
    }

    public sealed class MarketPage
    {
        public int Total, Page = 1, PageSize = 9; public List<MarketListing> Items = new List<MarketListing>();
        public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize)));

        public static MarketPage Parse(string json)
        {
            try
            {
                using var d = JsonDocument.Parse(json); var root = d.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
                string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; long L(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
                double Dbl(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : 0; bool F(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
                var page = new MarketPage { Total = (int)L(root, "total"), Page = Math.Max(1, (int)L(root, "page")), PageSize = Math.Max(1, (int)L(root, "pageSize")) };
                foreach (var e in items.EnumerateArray())
                {
                    if (!Guid.TryParse(S(e, "parcelId"), out var parcel) || !Guid.TryParse(S(e, "islandId"), out var island)) continue;
                    page.Items.Add(new MarketListing { ParcelId = parcel, IslandId = island, IslandName = S(e, "islandName"), Code = S(e, "code"), Name = S(e, "name"), Description = S(e, "description"), Category = S(e, "category"), Area = Dbl(e, "area"), MaxObjects = (int)L(e, "maxObjects"), Offer = S(e, "offer"), State = S(e, "state"), Price = L(e, "price"), RentPeriodDays = (int)L(e, "rentPeriodDays"), OwnerName = S(e, "ownerName"), CanVisit = F(e, "canVisit"), RentMinPeriods = Math.Max(1, (int)L(e, "rentMinPeriods")), RentMaxPeriods = Math.Max(1, (int)L(e, "rentMaxPeriods")), RentDeposit = L(e, "rentDeposit"), Kind = S(e, "kind") is { Length: > 0 } k ? k : "parcel", Region = S(e, "region"), AutoRenewAllowed = F(e, "autoRenewAllowed") });
                }
                return page;
            }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>What the tabs and filters of the market say, as the query the server understands.</summary>
    public sealed class MarketFilter
    {
        public string Query = "", Offer = "any", Category = "", Type = "", Region = "", Sort = "new"; public long? MinPrice, MaxPrice; public bool OnlyAvailable = true, WithVisit; public int Page = 1, PageSize = 9;

        public static readonly (string Id, string Text)[] Tabs = { ("ilhas_venda", "Ilhas à venda"), ("ilhas_aluguel", "Ilhas para alugar"), ("homesteads", "Homesteads"), ("terrenos", "Terrenos"), ("comerciais", "Comerciais"), ("residenciais", "Residenciais"), ("todos", "Todos") };

        /// <summary>The type, the offer, the region and the category each tab stands for.</summary>
        public void ApplyTab(string tab)
        {
            (Type, Offer, Region, Category) = tab switch { "ilhas_venda" => ("island", "sale", "", ""), "ilhas_aluguel" => ("island", "rent", "", ""), "homesteads" => ("island", "any", "homestead", ""), "comerciais" => ("parcel", "any", "", "comercial"), "residenciais" => ("parcel", "any", "", "residencial"), "todos" => ("", "any", "", ""), _ => ("parcel", "any", "", "") };
        }

        public string ToQuery()
        {
            var parts = new List<string> { "offer=" + Offer, "sort=" + Sort, "page=" + Page, "pageSize=" + PageSize, "onlyAvailable=" + (OnlyAvailable ? "true" : "false"), "withVisit=" + (WithVisit ? "true" : "false") };
            if (Query.Trim().Length > 0) parts.Add("q=" + Uri.EscapeDataString(Query.Trim())); if (Category.Length > 0) parts.Add("category=" + Category); if (Type.Length > 0) parts.Add("type=" + Type); if (Region.Length > 0) parts.Add("region=" + Region);
            if (MinPrice is { } a) parts.Add("minPrice=" + a); if (MaxPrice is { } b) parts.Add("maxPrice=" + b);
            return string.Join("&", parts);
        }

        public static long? Money(string text) => long.TryParse((text ?? "").Trim().Replace(".", ""), out var v) && v >= 0 ? v : (long?)null;
    }

    /// <summary>
    /// "Mercado imobiliário": the land that is for sale or for rent on the public islands. Tabs on top, a search, and the listings with pages and, on each, the buying, renting and visiting. The server decides what is listed and charges the money;
    /// this only asks and shows. A purchase asks to be confirmed (two touches) and carries the price the person saw, so the server refuses it if the price changed.
    /// </summary>
    public sealed class RealEstateMarketWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly MarketFilter _filter = new MarketFilter(); private readonly ScrollView _list = new ScrollView(); private readonly Label _status, _total; private readonly TextField _query, _maxPrice, _periods; private readonly VisualElement _tabs, _pages;
        public MarketPage Page { get; private set; } = new MarketPage();
        public string Tab { get; private set; } = "terrenos";
        public string LastQuery { get; private set; } = "";
        public int Count => _list.contentContainer.childCount;
        public string Notice => _status.text;
        public TextField QueryField => _query;
        public TextField PeriodsField => _periods;

        public RealEstateMarketWindow(ApiContext context) : base(context.Ui, "real-estate", "Mercado imobiliário", 720f, 560f, new Vector2(240, 70))
        {
            _c = context; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; _total = UiKit.Text("", "lv-bold");
            _tabs = new VisualElement(); _tabs.style.flexDirection = FlexDirection.Row; _tabs.style.flexWrap = Wrap.Wrap; foreach (var (id, text) in MarketFilter.Tabs) { var tab = id; _tabs.Add(UiKit.Button(text, () => SelectTab(tab), "lv-btn-flat")); } Body.Add(_tabs);
            _query = UiKit.Field("Buscar", false, 60); _maxPrice = UiKit.Field("Preço máximo", false, 12); _periods = UiKit.Field("Períodos do aluguel", false, 3, "1"); _query.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Search(1); }, TrickleDown.TrickleDown);
            _query.style.flexGrow = 1; _query.style.minWidth = 200; _maxPrice.style.width = 150; _periods.style.width = 200; Body.Add(UiKit.Row(_query, _maxPrice, UiKit.Button("Buscar", () => Search(1), "lv-btn-primary"))); Body.Add(_periods); Body.Add(_total); Body.Add(_status); _list.style.flexGrow = 1; _list.style.minHeight = 240; Body.Add(_list);
            _pages = new VisualElement(); _pages.style.flexDirection = FlexDirection.Row; Body.Add(_pages); SelectTab("terrenos");
        }

        public void SelectTab(string tab) { Tab = tab; _filter.ApplyTab(tab); Search(1); }

        public void Search(int page)
        {
            _filter.Query = _query.value; _filter.MaxPrice = MarketFilter.Money(_maxPrice.value); _filter.Page = Math.Max(1, page); LastQuery = _filter.ToQuery(); _status.text = "Buscando…";
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/discovery/market?" + LastQuery), r =>
            {
                _list.Clear(); _pages.Clear();
                if (r.Status != 200) { _status.text = r.Status == 404 ? "O servidor ainda não tem o mercado imobiliário." : UiTasks.Explain(r.Status, r.Body); return; }
                var data = MarketPage.Parse(r.Body); if (data == null) { _status.text = "Resposta inesperada do servidor."; return; }
                Page = data; _status.text = ""; _total.text = data.Total + " resultado(s)  ·  página " + data.Page + " de " + data.Pages; if (data.Items.Count == 0) _list.Add(UiKit.Text("Nada encontrado com esses filtros.", "lv-muted"));
                foreach (var item in data.Items) _list.Add(Card(item));
                for (var p = Math.Max(1, data.Page - 2); p <= Math.Min(data.Pages, data.Page + 2); p++) { var go = p; var b = UiKit.Button(p.ToString(), () => Search(go), p == data.Page ? "lv-btn-primary" : "lv-btn-flat"); _pages.Add(b); }
            });
        }

        private VisualElement Card(MarketListing item)
        {
            var box = new VisualElement(); box.AddToClassList("lv-row"); box.style.flexDirection = FlexDirection.Column; box.style.marginBottom = 6;
            box.Add(UiKit.Text(item.Title + "  ·  " + item.TypeText, "lv-bold")); var info = UiKit.Text(item.IslandName + (item.OwnerName.Length > 0 ? "  ·  de " + item.OwnerName : "") + "  ·  " + LandText.Area(item.Area) + "  ·  até " + item.MaxObjects + " objetos", "lv-small", "lv-muted"); info.style.whiteSpace = WhiteSpace.Normal; box.Add(info);
            if (item.Description.Length > 0) { var d = UiKit.Text(item.Description, "lv-small"); d.style.whiteSpace = WhiteSpace.Normal; box.Add(d); }
            var buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row; buttons.Add(UiKit.Text(item.PriceText + (item.State == "rented" ? "  ·  alugado" : ""), "lv-accent")); buttons.Add(UiKit.Spacer());
            if (item.State != "rented")
            {
                if (item.Offer == "sale") buttons.Add(Controls.Confirm("Comprar", () => "Confirmar a compra por " + LandText.Money(item.Price), () => Buy(item), "lv-btn-primary"));
                else buttons.Add(Controls.Confirm("Alugar", () => "Confirmar o aluguel (" + Periods(item) + " período(s))", () => Rent(item), "lv-btn-primary"));
            }
            if (item.CanVisit) buttons.Add(UiKit.Button("Visitar", () => { _c.EnterIsland?.Invoke(item.IslandId); Close(); }));
            box.Add(buttons); return box;
        }

        private int Periods(MarketListing item) => int.TryParse(_periods.value, out var n) ? Math.Max(item.RentMinPeriods, Math.Min(item.RentMaxPeriods, n)) : item.RentMinPeriods;

        public void Buy(MarketListing item) => Send(item.Path + "/buy", JsonSerializer.Serialize(new { expectedPrice = item.Price, idempotencyKey = Guid.NewGuid().ToString() }), "Compra feita.");
        public void Rent(MarketListing item) => Send(item.Path + "/rent", JsonSerializer.Serialize(new { periods = Periods(item), autoRenew = false, expectedPrice = item.Price, idempotencyKey = Guid.NewGuid().ToString() }), "Aluguel feito.");

        private void Send(string path, string json, string done) => this.Then(_c.Api.ApiAsync("POST", path, json), r =>
        {
            if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show(done, NoticeKind.Good); _status.text = done; Search(Page.Page); } else _status.text = Words(r.Status, r.Body);
        });

        /// <summary>The words for what the server answered to a purchase or a rental.</summary>
        public static string Words(int status, string body)
        {
            try { using var d = JsonDocument.Parse(body); var code = d.RootElement.TryGetProperty("code", out var c) ? c.GetString() : d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                var text = code switch { "price_changed" => "O preço mudou. Veja o novo valor e tente de novo.", "insufficient_funds" => "Saldo insuficiente.", "not_for_sale" => "Isso não está mais à venda.", "not_for_rent" => "Isso não está mais para alugar.", "already_rented" => "Já foi alugado.", "own_property" => "Isso já é seu.", "island_closed" => "Essa ilha está fechada.", _ => null }; if (text != null) return text; }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException) { }
            return UiTasks.Explain(status, body);
        }
    }

    /// <summary>
    /// Puts a parcel or an island up for sale or for rent: the price, and for a rental the period, how many periods may be taken, the deposit and whether the renter may ask for it to renew by itself. The platform checks everything again.
    /// </summary>
    public sealed class ListingOfferWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly bool _rent, _island; private readonly Guid _islandId, _parcel; private readonly Action _done; private readonly TextField _price, _days, _min, _max, _deposit; private readonly Toggle _objects, _renew; private readonly Label _status;
        public string Notice => _status.text;
        public TextField PriceField => _price;

        public ListingOfferWindow(ApiContext context, bool rent, bool island, Guid islandId, Guid parcel, string name, Action done) : base(context.Ui, "listing-offer", (rent ? "Alugar: " : "Vender: ") + name, 380f, -1f, new Vector2(340, 140))
        {
            _c = context; _rent = rent; _island = island; _islandId = islandId; _parcel = parcel; _done = done; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            _price = UiKit.Field(rent ? "Preço por período (VC$)" : "Preço (VC$)", false, 10); Body.Add(_price);
            _days = UiKit.Field("Dias de cada período (7 = semanal, 30 = mensal)", false, 3, "30"); _min = UiKit.Field("Mínimo de períodos", false, 3, "1"); _max = UiKit.Field("Máximo de períodos", false, 3, "6"); _deposit = UiKit.Field("Caução (VC$)", false, 10, "0");
            _renew = new Toggle("O inquilino pode pedir renovação automática"); _objects = new Toggle("Incluir os objetos na venda");
            if (rent) { Body.Add(_days); Body.Add(_min); Body.Add(_max); Body.Add(_deposit); Body.Add(_renew); } else Body.Add(_objects);
            Body.Add(_status); Body.Add(UiKit.Button(rent ? "Anunciar para alugar" : "Anunciar à venda", Submit, "lv-btn-primary"));
        }

        /// <summary>The body of the offer to sell (null when the price is not a positive whole number).</summary>
        public static string SaleBody(string price, bool includeObjects) => long.TryParse((price ?? "").Trim().Replace(".", ""), out var p) && p > 0 ? JsonSerializer.Serialize(new { price = p, includeObjects, description = "" }) : null;

        /// <summary>The body of the offer to rent (null when something is out of range: price above zero, 1 to 90 days, at least one period, a maximum not below the minimum, a deposit that is not negative).</summary>
        public static string RentBody(string price, string days, string min, string max, string deposit, bool autoRenew)
        {
            if (!long.TryParse((price ?? "").Trim().Replace(".", ""), out var p) || p <= 0 || !int.TryParse((days ?? "").Trim(), out var d) || d < 1 || d > 90 || !int.TryParse((min ?? "").Trim(), out var a) || a < 1 || !int.TryParse((max ?? "").Trim(), out var b) || b < a || b > 120 || !long.TryParse((deposit ?? "").Trim().Replace(".", ""), out var dep) || dep < 0) return null;
            return JsonSerializer.Serialize(new { pricePerPeriod = p, periodDays = d, minPeriods = a, maxPeriods = b, depositCoins = dep, graceDays = 3, autoRenewAllowed = autoRenew });
        }

        public string Route() => (_island ? "/api/v1/worlds/islands/" + _islandId + "/real-estate" : "/api/v1/worlds/islands/" + _islandId + "/land/parcels/" + _parcel) + (_rent ? (_island ? "/rental-offer" : "/rental-offer") : "/sale");

        public void Submit()
        {
            var body = _rent ? RentBody(_price.value, _days.value, _min.value, _max.value, _deposit.value, _renew.value) : SaleBody(_price.value, _objects.value);
            if (body == null) { _status.text = _rent ? "Confira os números: preço acima de zero, de 1 a 90 dias, mínimo de períodos 1 ou mais, máximo não menor que o mínimo." : "Digite o preço (um número inteiro acima de zero)."; _status.AddToClassList("lv-bad"); return; }
            this.Then(_c.Api.ApiAsync("POST", Route(), body), r => { if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show("Anúncio publicado.", NoticeKind.Good); _done?.Invoke(); Close(); } else { _status.text = RealEstateMarketWindow.Words(r.Status, r.Body); _status.AddToClassList("lv-bad"); } });
        }
    }

    /// <summary>What the person holds, as <c>GET /api/v1/properties/mine</c> says: the summary and the lists of islands, parcels, contracts, listings and payments.</summary>
    public sealed class PropertiesData
    {
        public sealed class Island { public Guid Id; public string Name = "", State = "", Tenant = ""; public int Parcels; public long? SalePrice, RentPrice; public bool IsPublic; }
        public sealed class Parcel { public Guid Id, IslandId; public string Island = "", Code = "", Name = "", Kind = "", Tenant = ""; public double Area; public long? SalePrice, RentPrice; public bool CanVisit; public string Title => Code.Length > 0 ? Code + " — " + Name : Name; }
        public sealed class Contract { public Guid Id, ParcelId, IslandId; public string Island = "", Code = "", Parcel = "", Landlord = "", Tenant = "", Role = "", Status = "", Target = "parcel"; public long Price; public int PeriodDays; public DateTimeOffset ExpiresAt; public bool Live => Status == "active" || Status == "grace"; public string Title => Code.Length > 0 ? Code + " — " + Parcel : Parcel; }
        public sealed class Listing { public string Kind = ""; public Guid ParcelId, IslandId; public string Island = "", Name = "", Code = "", Target = "parcel"; public long Price; public int PeriodDays; }
        public sealed class Payment { public DateTimeOffset At; public string Type = "", Parcel = "", Other = "", Island = ""; public bool Incoming; public long Amount; }
        public sealed class Incoming { public Guid IslandId, ParcelId; public string Name = "", From = ""; public DateTimeOffset ExpiresAt; public bool IsParcel => ParcelId != Guid.Empty; }

        public int Total, InUse, RentedOut, ExpiringSoon; public long MonthlyRevenue;
        public List<Island> Islands = new List<Island>(); public List<Parcel> Parcels = new List<Parcel>(); public List<Contract> Contracts = new List<Contract>(); public List<Listing> Listings = new List<Listing>(); public List<Payment> Payments = new List<Payment>(); public List<Incoming> Transfers = new List<Incoming>();

        public static string PaymentText(string type) => type switch { "sale" => "Compra e venda", "rent_start" => "Aluguel (início e caução)", "rent_payment" => "Pagamento de aluguel", "rent_end" => "Caução devolvida", _ => type.Replace('_', ' ') };
        public static string StatusText(string status) => status switch { "active" => "Ativo", "grace" => "Em tolerância", "ended" => "Encerrado", _ => status };

        public static PropertiesData Parse(string json)
        {
            try
            {
                using var d = JsonDocument.Parse(json); var root = d.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("summary", out var s)) return null;
                string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; int I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var x) ? x : 0;
                long L(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0; long? LN(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : (long?)null;
                double Dbl(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : 0; bool F(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
                Guid G(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : Guid.Empty; DateTimeOffset T(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var t) ? t : default;
                IEnumerable<JsonElement> All(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.Clone()).ToList() : new List<JsonElement>();
                var data = new PropertiesData { Total = I(s, "total"), InUse = I(s, "inUse"), RentedOut = I(s, "rentedOut"), MonthlyRevenue = L(s, "monthlyRevenue"), ExpiringSoon = I(s, "expiringSoon") };
                foreach (var i in All("islands")) data.Islands.Add(new Island { Id = G(i, "id"), Name = S(i, "name"), State = S(i, "state"), Tenant = S(i, "tenant"), Parcels = I(i, "parcels"), SalePrice = LN(i, "salePrice"), RentPrice = LN(i, "rentPrice"), IsPublic = F(i, "isPublic") });
                foreach (var p in All("parcels")) data.Parcels.Add(new Parcel { Id = G(p, "id"), IslandId = G(p, "islandId"), Island = S(p, "island"), Code = S(p, "code"), Name = S(p, "name"), Kind = S(p, "kind"), Tenant = S(p, "tenant"), Area = Dbl(p, "area"), SalePrice = LN(p, "salePrice"), RentPrice = LN(p, "rentPrice"), CanVisit = F(p, "canVisit") });
                foreach (var c in All("contracts")) data.Contracts.Add(new Contract { Id = G(c, "id"), ParcelId = G(c, "parcelId"), IslandId = G(c, "islandId"), Island = S(c, "island"), Code = S(c, "code"), Parcel = S(c, "parcel"), Landlord = S(c, "landlord"), Tenant = S(c, "tenant"), Role = S(c, "role"), Status = S(c, "status"), Target = S(c, "target") is { Length: > 0 } t ? t : "parcel", Price = L(c, "price"), PeriodDays = I(c, "periodDays"), ExpiresAt = T(c, "expiresAt") });
                foreach (var l in All("listings")) data.Listings.Add(new Listing { Kind = S(l, "kind"), ParcelId = G(l, "parcelId"), IslandId = G(l, "islandId"), Island = S(l, "island"), Name = S(l, "name"), Code = S(l, "code"), Target = S(l, "target") is { Length: > 0 } t ? t : "parcel", Price = L(l, "price"), PeriodDays = I(l, "periodDays") });
                foreach (var p in All("payments")) data.Payments.Add(new Payment { At = T(p, "at"), Type = S(p, "type"), Parcel = S(p, "parcel"), Other = S(p, "other"), Island = S(p, "island"), Incoming = S(p, "direction") == "in", Amount = L(p, "amount") });
                foreach (var t in All("incomingTransfers")) data.Transfers.Add(new Incoming { IslandId = G(t, "islandId"), ParcelId = G(t, "parcelId"), Name = S(t, "name"), From = S(t, "from"), ExpiresAt = T(t, "expiresAt") });
                return data;
            }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>
    /// "Minhas propriedades": the islands and parcels the person owns (with what is on sale or for rent), the contracts they have as landlord or tenant (renew or end them), the payments and the islands or parcels someone wants to give them
    /// (accept or decline). Everything is read from the server and every change goes through it.
    /// </summary>
    public sealed class MyPropertiesWindow : FloatingWindow
    {
        public static readonly (string Id, string Text)[] Tabs = { ("resumo", "Resumo"), ("ilhas", "Ilhas"), ("terrenos", "Terrenos"), ("alugueis", "Aluguéis"), ("anuncios", "Anúncios"), ("pagamentos", "Pagamentos") };
        private readonly ApiContext _c; private readonly ScrollView _list = new ScrollView(); private readonly Label _status;
        public PropertiesData Data { get; private set; }
        public string Tab { get; private set; } = "resumo";
        public int Count => _list.contentContainer.childCount;
        public string Notice => _status.text;

        public MyPropertiesWindow(ApiContext context) : base(context.Ui, "my-properties", "Minhas propriedades", 640f, 520f, new Vector2(260, 80))
        {
            _c = context; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            var tabs = new VisualElement(); tabs.style.flexDirection = FlexDirection.Row; tabs.style.flexWrap = Wrap.Wrap; foreach (var (id, text) in Tabs) { var tab = id; tabs.Add(UiKit.Button(text, () => Show(tab), "lv-btn-flat")); } Body.Add(tabs); Body.Add(_status); _list.style.flexGrow = 1; _list.style.minHeight = 260; Body.Add(_list); Reload();
        }

        public void Reload() => this.Then(_c.Api.ApiAsync("GET", "/api/v1/properties/mine"), r =>
        {
            if (r.Status != 200) { _status.text = r.Status == 404 ? "O servidor ainda não tem as propriedades." : UiTasks.Explain(r.Status, r.Body); return; }
            Data = PropertiesData.Parse(r.Body); if (Data == null) { _status.text = "Resposta inesperada do servidor."; return; } _status.text = ""; Show(Tab);
        });

        public void Show(string tab)
        {
            Tab = tab; _list.Clear(); if (Data == null) return; Row(null);
            switch (tab)
            {
                case "resumo":
                    Line("Total de propriedades: " + Data.Total + "  ·  em uso: " + Data.InUse + "  ·  alugadas a outros: " + Data.RentedOut); Line("Renda mensal dos aluguéis: " + LandText.Money(Data.MonthlyRevenue) + "  ·  vencendo em breve: " + Data.ExpiringSoon);
                    foreach (var t in Data.Transfers)
                    {
                        var transfer = t; var path = transfer.IsParcel ? "/api/v1/worlds/islands/" + transfer.IslandId + "/land/parcels/" + transfer.ParcelId + "/transfer/" : "/api/v1/worlds/islands/" + transfer.IslandId + "/real-estate/transfer/";
                        Row(UiKit.Text(transfer.From + " quer dar " + (transfer.IsParcel ? "o terreno " : "a ilha ") + transfer.Name, "lv-bold"), UiKit.Spacer(), UiKit.Button("Aceitar", () => Act("POST", path + "accept", "{}", "Recebido."), "lv-btn-primary"), UiKit.Button("Recusar", () => Act("POST", path + "decline", "{}", null)));
                    }
                    break;
                case "ilhas": if (Data.Islands.Count == 0) Line("Você não tem ilhas."); foreach (var i in Data.Islands) { var island = i; Row(UiKit.Button("Vender…", () => new ListingOfferWindow(_c, false, true, island.Id, Guid.Empty, island.Name, Reload).Open()), UiKit.Button("Alugar…", () => new ListingOfferWindow(_c, true, true, island.Id, Guid.Empty, island.Name, Reload).Open()), UiKit.Text(i.Name + "  ·  " + i.Parcels + " terreno(s)" + (i.SalePrice != null ? "  ·  à venda " + LandText.Money(i.SalePrice.Value) : "") + (i.RentPrice != null ? "  ·  para alugar " + LandText.Money(i.RentPrice.Value) : "") + (i.Tenant.Length > 0 ? "  ·  alugada a " + i.Tenant : ""), "lv-small"), UiKit.Spacer(), UiKit.Button("Ir", () => { _c.EnterIsland?.Invoke(island.Id); Close(); })); } break;
                case "terrenos": if (Data.Parcels.Count == 0) Line("Você não tem terrenos."); foreach (var p in Data.Parcels) { var parcel = p; var buttons = new List<VisualElement> { UiKit.Text(p.Title + "  ·  " + p.Island + "  ·  " + LandText.Area(p.Area) + (p.SalePrice != null ? "  ·  à venda " + LandText.Money(p.SalePrice.Value) : "") + (p.RentPrice != null ? "  ·  para alugar" : "") + (p.Tenant.Length > 0 ? "  ·  alugado a " + p.Tenant : ""), "lv-small"), UiKit.Spacer() }; if (p.SalePrice == null) buttons.Add(UiKit.Button("Vender…", () => new ListingOfferWindow(_c, false, false, parcel.IslandId, parcel.Id, parcel.Title, Reload).Open())); if (p.RentPrice == null && p.Tenant.Length == 0) buttons.Add(UiKit.Button("Alugar…", () => new ListingOfferWindow(_c, true, false, parcel.IslandId, parcel.Id, parcel.Title, Reload).Open()));
                if (p.SalePrice != null) buttons.Add(UiKit.Button("Tirar da venda", () => Act("DELETE", Land(parcel.IslandId) + "/parcels/" + parcel.Id + "/sale", null, "Anúncio retirado."))); if (p.RentPrice != null) buttons.Add(UiKit.Button("Tirar do aluguel", () => Act("DELETE", Land(parcel.IslandId) + "/parcels/" + parcel.Id + "/rental-offer", null, "Anúncio retirado."))); if (p.CanVisit) buttons.Add(UiKit.Button("Ir", () => { _c.EnterIsland?.Invoke(parcel.IslandId); Close(); })); Row(buttons.ToArray()); } break;
                case "alugueis":
                    if (Data.Contracts.Count == 0) Line("Nenhum contrato de aluguel.");
                    foreach (var contract in Data.Contracts)
                    {
                        var ct = contract; var path = ct.Target == "island" ? "/api/v1/worlds/islands/" + ct.IslandId + "/real-estate/leases/" + ct.Id : Land(ct.IslandId) + "/rentals/" + ct.Id; var buttons = new List<VisualElement> { UiKit.Text(ct.Title + "  ·  " + (ct.Role == "tenant" ? "inquilino de " + ct.Landlord : "locador para " + ct.Tenant) + "  ·  " + LandText.Money(ct.Price) + " / " + LandText.Period(ct.PeriodDays) + "  ·  " + PropertiesData.StatusText(ct.Status) + "  ·  até " + ct.ExpiresAt.ToString("dd/MM/yyyy"), "lv-small"), UiKit.Spacer() };
                        if (ct.Live) { if (ct.Role == "tenant") buttons.Add(Controls.Confirm("Renovar", () => "Confirmar", () => Act("POST", path + "/renew", JsonSerializer.Serialize(new { periods = 1, idempotencyKey = Guid.NewGuid().ToString() }), "Renovado."))); buttons.Add(Controls.Confirm("Encerrar", () => "Confirmar o fim", () => Act("POST", path + "/end", "{}", "Contrato encerrado."))); }
                        Row(buttons.ToArray());
                    }
                    break;
                case "anuncios": if (Data.Listings.Count == 0) Line("Você não tem anúncios."); foreach (var l in Data.Listings) Line((l.Kind == "sale" ? "À venda: " : "Para alugar: ") + (l.Code.Length > 0 ? l.Code + " — " : "") + l.Name + "  ·  " + l.Island + "  ·  " + LandText.Money(l.Price) + (l.Kind == "sale" ? "" : " / " + LandText.Period(l.PeriodDays))); break;
                case "pagamentos": if (Data.Payments.Count == 0) Line("Nenhum pagamento."); foreach (var p in Data.Payments) Line(p.At.ToString("dd/MM/yyyy") + "  ·  " + PropertiesData.PaymentText(p.Type) + "  ·  " + p.Parcel + "  ·  " + (p.Incoming ? "+" : "-") + LandText.Money(p.Amount) + (p.Other.Length > 0 ? "  ·  " + p.Other : "")); break;
            }
        }

        private static string Land(Guid island) => "/api/v1/worlds/islands/" + island + "/land";
        private void Line(string text) { var t = UiKit.Text(text, "lv-small"); t.style.whiteSpace = WhiteSpace.Normal; t.style.marginBottom = 4; _list.Add(t); }
        private void Row(params VisualElement[] cells) { if (cells == null || cells.Length == 0) return; var row = UiKit.Row(cells); row.AddToClassList("lv-row"); _list.Add(row); }

        private void Act(string method, string path, string json, string done) => this.Then(_c.Api.ApiAsync(method, path, json), r =>
        {
            if (r.Status >= 200 && r.Status < 300) { if (done != null) _c.Notices.Show(done, NoticeKind.Good); _status.text = ""; Reload(); } else _status.text = RealEstateMarketWindow.Words(r.Status, r.Body);
        });
    }
}
