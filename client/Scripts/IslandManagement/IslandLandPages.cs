using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>What both land tabs of the island window need: the overview of the island's land from the server (the same one the land management window uses), a way to ask for it and the way to open that window.</summary>
public abstract partial class IslandLandPageBase : ScrollContainer
{
    protected static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    protected VBoxContainer Root = null!;
    protected Label Status = null!;
    private LandManagementApi? _api;
    private Guid _island;
    protected LandOverviewData? Data;

    /// <summary>Opens the land management window on this island: (island, section, start a sale offer).</summary>
    public Action<Guid, string, bool>? OpenLandManagement { get; set; }
    public LandOverviewData? Loaded => Data;
    public string StatusForTesting => Status.Text;
    public string TextForTesting => string.Join("|", Root.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public bool HasButtonForTesting(string text) => Root.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text);
    public void PressForTesting(string text) { if (Root.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { Disabled: false } b) b.EmitSignal(BaseButton.SignalName.Pressed); }

    public void Bind(IslandManagementApi api) => _api = new LandManagementApi(api);
    public void SetIsland(Guid island) { if (island != _island) { _island = island; Data = null; } }
    protected Guid Island => _island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Disabled; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        Root = IslandUi.Column(14); Root.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(Root);
        Status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); Root.AddChild(Status);
    }

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        Status.Text = "Carregando…"; var reply = await _api.Overview(_island);
        if (!reply.Ok)
        {
            Data = null; Clear(); Status.Text = reply.Status switch { 404 => "O servidor ainda não tem os dados de terrenos. Eles chegam com a próxima publicação da API.", 403 => "Só o dono e os administradores da ilha veem os terrenos.", _ => LandManagementApi.Words(reply) }; return;
        }
        var data = LandOverviewData.Parse(reply.Body); if (data is null) { Status.Text = "O servidor mandou uma resposta que não entendi."; return; }
        Data = data; Status.Text = ""; Clear(); Render(data);
    }

    protected void Clear() { foreach (var child in Root.GetChildren().Skip(1)) child.QueueFree(); }
    protected abstract void Render(LandOverviewData data);

    protected static Control Kpi(string title, string value, string note = "", Color? tint = null)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 78) }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 14, 8));
        var column = IslandUi.Column(0); card.AddChild(column);
        var head = IslandUi.Row(6); head.AddChild(IslandUi.Text(title, 12, IslandUi.TextSecondary, true)); head.AddChild(IslandUi.Text(note, 11, IslandUi.TextMuted)); column.AddChild(head);
        column.AddChild(IslandUi.Text(value, 22, tint ?? IslandUi.TextPrimary, true)); return card;
    }
}

/// <summary>The Terrenos e Parcelas tab: how the land of the island is shared (area, parcels sold, rented and free), a small map of it and the way to the land management window.</summary>
public partial class IslandLandPage : IslandLandPageBase
{
    public IReadOnlyList<string> KpiTitlesForTesting => ["Área Total", "Área Parcelada", "Área Disponível", "Parcelas", "Vendidas", "Alugadas", "Disponíveis"];
    public int MapParcelsForTesting => Root.FindChildren("*", "Control", true, false).OfType<LandTerritoryMap>().FirstOrDefault()?.ParcelCountForTesting ?? 0;

    protected override void Render(LandOverviewData data)
    {
        var t = data.Totals; string Pct(double v) => t.Area <= 0 ? "" : $"({v * 100 / t.Area:0.#}%)".Replace('.', ',');
        var first = IslandUi.Row(10); Root.AddChild(first);
        first.AddChild(Kpi("Área Total", LandMath.Area(t.Area))); first.AddChild(Kpi("Área Parcelada", LandMath.Area(t.AreaParcelled), Pct(t.AreaParcelled))); first.AddChild(Kpi("Área Disponível", LandMath.Area(t.AreaFree), Pct(t.AreaFree), LandTerritoryMap.Available));
        var second = IslandUi.Row(10); Root.AddChild(second);
        second.AddChild(Kpi("Parcelas", t.Parcels.ToString(Pt))); second.AddChild(Kpi("Vendidas", t.Sold.ToString(Pt))); second.AddChild(Kpi("Alugadas", t.Rented.ToString(Pt), "", LandTerritoryMap.Rented)); second.AddChild(Kpi("Disponíveis", t.Available.ToString(Pt), "", LandTerritoryMap.Available));

        var row = IslandUi.Row(14); row.SizeFlagsHorizontal = SizeFlags.ExpandFill; Root.AddChild(row);
        var (mapCard, mapBody) = IslandUi.Card("Mapa Territorial", "map"); mapCard.SizeFlagsVertical = SizeFlags.ShrinkBegin; mapCard.CustomMinimumSize = new Vector2(380, 0); row.AddChild(mapCard);
        var map = new LandTerritoryMap { CustomMinimumSize = new Vector2(340, 300), ShowLabels = false, ShowCover = false, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill }; mapBody.AddChild(map); map.SetData(data);
        var legend = IslandUi.Row(12); mapBody.AddChild(legend);
        foreach (var (text, color) in new[] { ("Disponível", LandTerritoryMap.Available), ("À Venda", LandTerritoryMap.OnSale), ("Alugado", LandTerritoryMap.Rented), ("Vendido", LandTerritoryMap.Sold), ("Comum", LandTerritoryMap.Common) })
        { var item = IslandUi.Row(5); item.AddChild(IslandUi.Dot(color, 10)); item.AddChild(IslandUi.Text(text, 12, IslandUi.TextSecondary)); legend.AddChild(item); }

        var (card, body) = IslandUi.Card("Como a Ilha Está Dividida", "layers"); card.SizeFlagsVertical = SizeFlags.ShrinkBegin; card.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(card);
        Control Line(string key, string value) { var r = IslandUi.Row(10); var k = IslandUi.Text(key, 14, IslandUi.TextSecondary); k.CustomMinimumSize = new Vector2(190, 0); r.AddChild(k); r.AddChild(IslandUi.Text(value, 14, IslandUi.TextPrimary, true, true)); return r; }
        body.AddChild(Line("Terrenos na ilha:", t.Parcels.ToString(Pt))); body.AddChild(Line("À venda agora:", t.OnSale.ToString(Pt))); body.AddChild(Line("Áreas comuns:", t.Common.ToString(Pt)));
        body.AddChild(Line("Prims da ilha:", LandMath.Number(t.ObjectsLimit))); body.AddChild(Line("Distribuídas aos terrenos:", LandMath.Number(t.ObjectsShared))); body.AddChild(Line("Em uso:", LandMath.Number(t.ObjectsUsed)));
        if (t.Parcels == 0) body.AddChild(IslandUi.Text("Esta ilha ainda não tem terrenos demarcados. Use o gerenciamento de terrenos para marcar o primeiro.", 13, IslandUi.TextMuted, false, true));
        var open = IslandUi.Action("Abrir Gerenciamento de Terrenos", "map", IslandUi.Kind.Primary, 42); open.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; open.Pressed += () => OpenLandManagement?.Invoke(Island, "visao", false); body.AddChild(open);
    }
}

/// <summary>The Vendas e Aluguéis tab: the offers and contracts of the island at a glance, and the ways into the sales and rentals of the land management window.</summary>
public partial class IslandDealsPage : IslandLandPageBase
{
    public IReadOnlyList<string> KpiTitlesForTesting => ["Anúncios Ativos", "Parcelas à Venda", "Parcelas Alugadas", "Contratos Ativos", "Receita Mensal", "Próximos Vencimentos"];
    public int ContractRowsForTesting { get; private set; }

    protected override void Render(LandOverviewData data)
    {
        var t = data.Totals; var offers = data.Parcels.Count(p => p.SalePrice is not null || (p.RentPrice is not null && p.Kind != "rented")); var live = data.Contracts.Where(c => c.Status is "active" or "grace").ToList();
        var first = IslandUi.Row(10); Root.AddChild(first);
        first.AddChild(Kpi("Anúncios Ativos", offers.ToString(Pt))); first.AddChild(Kpi("Parcelas à Venda", t.OnSale.ToString(Pt), "", IslandUi.AccentSecondary)); first.AddChild(Kpi("Parcelas Alugadas", t.Rented.ToString(Pt), "", LandTerritoryMap.Rented));
        var second = IslandUi.Row(10); Root.AddChild(second);
        second.AddChild(Kpi("Contratos Ativos", live.Count.ToString(Pt))); second.AddChild(Kpi("Receita Mensal", LandMath.Money(t.MonthlyRevenue), "", LandTerritoryMap.Available)); second.AddChild(Kpi("Próximos Vencimentos", t.ExpiringSoon.ToString(Pt), t.ExpiringSoon > 0 ? "(em 7 dias)" : ""));

        var buttons = IslandUi.Row(10); Root.AddChild(buttons);
        var sales = IslandUi.Action("Gerenciar Vendas", "coins", IslandUi.Kind.Orange, 42); sales.Pressed += () => OpenLandManagement?.Invoke(Island, "vendas", false); buttons.AddChild(sales);
        var rentals = IslandUi.Action("Gerenciar Aluguéis", "calendar", IslandUi.Kind.Purple, 42); rentals.Pressed += () => OpenLandManagement?.Invoke(Island, "alugueis", false); buttons.AddChild(rentals);
        var create = IslandUi.Action("Criar Anúncio", "plus", IslandUi.Kind.Primary, 42); create.Pressed += () => OpenLandManagement?.Invoke(Island, "vendas", true); buttons.AddChild(create);

        var (card, body) = IslandUi.Card("Contratos", "doc"); card.SizeFlagsVertical = SizeFlags.ShrinkBegin; Root.AddChild(card);
        ContractRowsForTesting = live.Count;
        if (live.Count == 0) { body.AddChild(IslandUi.Text("Nenhum contrato de aluguel em andamento.", 14, IslandUi.TextMuted, false, true)); return; }
        var head = IslandUi.Row(8); foreach (var (text, ratio) in new[] { ("Imóvel", 1.5f), ("Locador", 1f), ("Locatário", 1f), ("Valor", 1.1f), ("Vencimento", 1f), ("Estado", .9f) }) { var l = IslandUi.Text(text, 13, IslandUi.TextSecondary, true); l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.SizeFlagsStretchRatio = ratio; head.AddChild(l); }
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        foreach (var c in live)
        {
            var row = IslandUi.Row(8);
            Label Cell(string text, float ratio, Color? color = null, bool bold = false) { var l = IslandUi.Text(text, 14, color ?? IslandUi.TextPrimary, bold); l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.SizeFlagsStretchRatio = ratio; l.ClipText = true; l.TooltipText = text; return l; }
            row.AddChild(Cell(c.Code.Length > 0 ? $"{c.Code} — {c.Parcel}" : c.Parcel, 1.5f, null, true)); row.AddChild(Cell(c.Landlord, 1f)); row.AddChild(Cell(c.Tenant, 1f)); row.AddChild(Cell($"{LandMath.Money(c.Price)} / {c.PeriodDays} d", 1.1f, LandTerritoryMap.Rented));
            row.AddChild(Cell(c.ExpiresAt.ToLocalTime().ToString("dd/MM/yyyy", Pt), 1f)); row.AddChild(Cell(c.Status == "grace" ? "Em tolerância" : "Ativo", .9f, c.Status == "grace" ? IslandUi.Warning : IslandUi.Success, true));
            body.AddChild(row); body.AddChild(IslandUi.Divider());
        }
    }
}
