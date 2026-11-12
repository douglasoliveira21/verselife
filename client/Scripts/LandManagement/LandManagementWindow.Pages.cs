using Godot;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.LandManagement;

/// <summary>The sections that are tables: Lista de Terrenos, Vendas, Aluguéis, Contratos, Prims e Recursos, Permissões and Histórico.</summary>
public partial class LandManagementWindow
{
    private string _listFilter = "all";
    private string _listQuery = "";
    private VBoxContainer? _listHolder;
    private Label? _listCount;
    private readonly Dictionary<string, int> _rowCounts = [];

    public int RowsForTesting(string section) => _rowCounts.GetValueOrDefault(section);
    public void SetListFilterForTesting(string filter, string query = "") { _listFilter = filter; _listQuery = query; if (_data is not null) RefreshPage("lista"); }
    public async Task EndRentalForTesting(Guid rental) => await Do(_api!.EndRental(_island, rental), "Aluguel encerrado.");
    public async Task WithdrawSaleForTesting() { if (Selected is { } p) await Do(_api!.CancelSale(_island, p.Id), "Venda retirada."); }
    public async Task WithdrawRentOfferForTesting() { if (Selected is { } p) await Do(_api!.CancelRentOffer(_island, p.Id), "Oferta de aluguel retirada."); }

    private void RefreshPage(string id)
    {
        if (_data is null) return;
        if (id is "visao" or "mapa" or "demarcacao") { RefreshWorkspace(); return; }
        if (!_pageBodies.TryGetValue(id, out var body)) return;
        if (id == "lista") { BuildList(body); return; }
        foreach (var child in body.GetChildren()) child.QueueFree();
        switch (id)
        {
            case "vendas": BuildSales(body); break;
            case "alugueis": BuildRentals(body); break;
            case "contratos": BuildContracts(body); break;
            case "prims": BuildResources(body); break;
            case "permissoes": BuildPermissions(body); break;
            case "historico": BuildHistory(body); break;
        }
    }

    // ---- table pieces -------------------------------------------------------------------------------------------------------------

    private static Label Cell(string text, Color? color = null, bool bold = false, float ratio = 1f)
    {
        var label = IslandUi.Text(text, 14, color ?? IslandUi.TextPrimary, bold); label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; label.TooltipText = text; label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static Control Table(string[] headers, float[] ratios, IReadOnlyList<Control[]> rows, string empty, float actionsWidth = 0)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard));
        var column = IslandUi.Column(0); card.AddChild(column);
        var head = new PanelContainer { CustomMinimumSize = new Vector2(0, 40) }; var headBox = IslandUi.Box(IslandUi.CardHeader, null, IslandUi.RadiusCard, 0, 14, 0); headBox.CornerRadiusBottomLeft = 0; headBox.CornerRadiusBottomRight = 0; head.AddThemeStyleboxOverride("panel", headBox);
        var headRow = IslandUi.Row(10); head.AddChild(headRow);
        for (var i = 0; i < headers.Length; i++) { var l = IslandUi.Text(headers[i], 13, IslandUi.TextSecondary, true); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.SizeFlagsStretchRatio = ratios[i]; headRow.AddChild(l); }
        if (actionsWidth > 0) headRow.AddChild(new Control { CustomMinimumSize = new Vector2(actionsWidth, 0) });
        column.AddChild(head);
        if (rows.Count == 0) { var note = IslandUi.Text(empty, 15, IslandUi.TextMuted, false, true); column.AddChild(IslandUi.Padded(note, 16, 16, 16, 16)); return card; }
        foreach (var cells in rows)
        {
            var row = new PanelContainer { CustomMinimumSize = new Vector2(0, 44) }; row.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(0, 0, 0, 0), null, 0, 0, 14, 4));
            var line = IslandUi.Row(10); row.AddChild(line);
            for (var i = 0; i < cells.Length; i++) line.AddChild(cells[i]);
            column.AddChild(row); column.AddChild(IslandUi.Divider());
        }
        return card;
    }

    private static Control Actions(float width, params Button[] buttons)
    {
        var box = IslandUi.Row(6); box.CustomMinimumSize = new Vector2(width, 0); box.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; box.Alignment = BoxContainer.AlignmentMode.End;
        foreach (var b in buttons) box.AddChild(b); return box;
    }

    private static Button Small(string text, IslandUi.Kind kind, Action act, bool enabled = true, string tip = "") { var b = IslandUi.Action(text, null, kind, 32); b.Pressed += act; b.Disabled = !enabled; b.TooltipText = tip; return b; }

    private static Control Title(string text, string? note = null)
    {
        var column = IslandUi.Column(2); column.AddChild(IslandUi.Text(text, 20, IslandUi.TextPrimary, true)); if (note is not null) column.AddChild(IslandUi.Text(note, 13, IslandUi.TextMuted, false, true)); return column;
    }

    private static Color KindColor(string kind) => kind switch { "available" => LandTerritoryMap.Available, "sale" => LandTerritoryMap.OnSale, "rented" => LandTerritoryMap.Rented, "common" => LandTerritoryMap.Common, _ => IslandUi.TextSecondary };

    private void ShowOnMap(LandParcelRow parcel) { _map.Select(parcel.Id); Select("mapa"); _map.FocusParcel(parcel.Id); RefreshWorkspace(); }

    private static string Short(DateTimeOffset when) => when == default ? "—" : when.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt);
    private static string ShortDay(DateTimeOffset when) => when == default ? "—" : when.ToLocalTime().ToString("dd/MM/yyyy", Pt);

    // ---- Lista de Terrenos ----------------------------------------------------------------------------------------------------------

    private void BuildList(VBoxContainer body)
    {
        if (_listHolder is null || !IsInstanceValid(_listHolder))
        {
            foreach (var child in body.GetChildren()) child.QueueFree();
            body.AddChild(Title("Lista de Terrenos", "Todos os terrenos da ilha, com dono, finalidade, prims e estado."));
            var filters = IslandUi.Row(10); body.AddChild(filters);
            var search = IslandUi.Input("Buscar por código, nome ou dono", 60); search.CustomMinimumSize = new Vector2(280, 38); search.TextChanged += text => { _listQuery = text; RefreshPage("lista"); }; filters.AddChild(search);
            var drop = IslandUi.Dropdown([("Todos", "all"), ("Disponíveis", "available"), ("Vendidos", "sold"), ("Alugados", "rented"), ("À venda", "sale"), ("Áreas comuns", "common"), ("Residenciais", "residencial"), ("Comerciais", "comercial")]);
            drop.CustomMinimumSize = new Vector2(190, 38); drop.ItemSelected += _ => { _listFilter = IslandUi.IdOf(drop); RefreshPage("lista"); }; filters.AddChild(drop);
            _listCount = IslandUi.Text("", 14, IslandUi.TextSecondary); _listCount.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _listCount.HorizontalAlignment = HorizontalAlignment.Right; filters.AddChild(_listCount);
            _listHolder = IslandUi.Column(0); body.AddChild(_listHolder);
        }
        foreach (var child in _listHolder.GetChildren()) child.QueueFree();
        var query = _listQuery.Trim();
        var parcels = _data!.Parcels.Where(p => _listFilter switch { "all" => true, "residencial" or "comercial" => p.Category == _listFilter, var kind => p.Kind == kind })
            .Where(p => query.Length == 0 || p.Code.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Owner.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _listCount!.Text = $"{parcels.Count} de {_data.Parcels.Count} terreno(s)"; _rowCounts["lista"] = parcels.Count;
        var rows = parcels.Select(p => new Control[]
        {
            Cell(p.Code, IslandUi.AccentPrimary, true, .6f), Cell(p.Name, null, false, 1.5f), Cell(LandMath.Area(p.Area), null, false, .9f), Cell(p.Owner, null, false, 1.1f), Cell(p.CategoryText, null, false, 1f),
            Cell($"{LandMath.Number(p.Objects)} / {LandMath.Number(p.MaxObjects)}", null, false, 1f), Cell(p.KindText, KindColor(p.Kind), true, .9f),
            Actions(190, Small("Ver no mapa", IslandUi.Kind.Secondary, () => ShowOnMap(p)), Small("Inspecionar", IslandUi.Kind.Quiet, () => Inspect?.Invoke(p))),
        }).ToList();
        _listHolder.AddChild(Table(["Código", "Nome", "Área", "Proprietário", "Categoria", "Prims", "Estado"], [.6f, 1.5f, .9f, 1.1f, 1f, 1f, .9f], rows, "Nenhum terreno com esse filtro.", 196));
    }

    // ---- Vendas -----------------------------------------------------------------------------------------------------------------------

    private void BuildSales(VBoxContainer body)
    {
        var offers = _data!.Parcels.Where(p => p.SalePrice is not null).ToList(); _rowCounts["vendas"] = offers.Count;
        var head = IslandUi.Row(10); head.AddChild(Title("Vendas", "Terrenos à venda na ilha e o que já foi vendido.")); head.GetChild<Control>(0).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var create = IslandUi.Action("Criar Anúncio", "plus", IslandUi.Kind.Orange, 40); create.Pressed += () => { var mine = _data.Parcels.Where(p => IsMine(p) && p.SalePrice is null && p.RentPrice is null && p.Kind is not ("rented" or "common")).ToList(); if (mine.Count == 0) { Notify("Você não tem terreno livre para anunciar."); return; } AskWhich("Criar anúncio de venda", mine, p => OpenSell(p)); }; head.AddChild(create); body.AddChild(head);
        var cards = IslandUi.Row(10); body.AddChild(cards);
        foreach (var (title, value) in new[] { ("Anúncios ativos", offers.Count.ToString(Pt)), ("Terrenos vendidos", _data.Totals.Sold.ToString(Pt)) })
        { var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 14, 8)); var c = IslandUi.Column(0); c.AddChild(IslandUi.Text(title, 12, IslandUi.TextSecondary, true)); c.AddChild(IslandUi.Text(value, 22, IslandUi.TextPrimary, true)); card.AddChild(c); cards.AddChild(card); }
        var rows = offers.Select(p => new Control[]
        {
            Cell(p.Code, IslandUi.AccentPrimary, true, .6f), Cell(p.Name, null, false, 1.5f), Cell(LandMath.Money(p.SalePrice!.Value), LandTerritoryMap.OnSale, true, 1f), Cell(p.Owner, null, false, 1.1f), Cell(LandMath.Area(p.Area), null, false, .9f),
            Actions(300, Small("Editar Preço", IslandUi.Kind.Secondary, () => OpenSell(p), IsMine(p), IsMine(p) ? "" : "Só o dono do terreno muda o preço."), Small("Retirar da Venda", IslandUi.Kind.Danger, () => { _map.Select(p.Id); _ = Do(_api!.CancelSale(_island, p.Id), "Venda retirada."); }, IsMine(p)), Small("Ver", IslandUi.Kind.Quiet, () => ShowOnMap(p))),
        }).ToList();
        body.AddChild(IslandUi.Text("Anúncios ativos", 17, IslandUi.TextPrimary, true));
        body.AddChild(Table(["Código", "Terreno", "Preço", "Vendedor", "Área"], [.6f, 1.5f, 1f, 1.1f, .9f], rows, "Nenhum terreno à venda no momento.", 306));
        var sold = _data.History.Where(h => h.What == "sale").ToList();
        body.AddChild(IslandUi.Text("Histórico de vendas", 17, IslandUi.TextPrimary, true));
        body.AddChild(Table(["Data", "Terreno", "Comprador", "Valor"], [1.2f, 1.6f, 1.2f, 1f], sold.Select(h => new Control[] { Cell(Short(h.At), null, false, 1.2f), Cell($"{h.Code} – {h.Parcel}", null, false, 1.6f), Cell(h.Actor, null, false, 1.2f), Cell(LandMath.Money(h.Amount ?? 0), LandTerritoryMap.OnSale, true, 1f) }).ToList(), "Ainda não houve vendas."));
    }

    private void AskWhich(string title, List<LandParcelRow> options, Action<LandParcelRow> then)
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("Escolha o terreno:", 14, IslandUi.TextSecondary));
        var drop = IslandUi.Dropdown(options.Select(p => ($"{p.Code} – {p.Name} ({LandMath.Area(p.Area)})", p.Id.ToString()))); if (Selected is { } s && options.Any(o => o.Id == s.Id)) IslandUi.Select(drop, s.Id.ToString()); content.AddChild(drop);
        Ask(title, content, () => { var chosen = options.First(o => o.Id.ToString() == IslandUi.IdOf(drop)); then(chosen); return Task.FromResult(true); }, "Continuar");
    }

    // ---- Aluguéis ---------------------------------------------------------------------------------------------------------------------

    private void BuildRentals(VBoxContainer body)
    {
        var offers = _data!.Parcels.Where(p => p.RentPrice is not null && p.Kind != "rented").ToList(); var live = _data.Contracts.Where(c => c.Status is "active" or "grace").ToList(); _rowCounts["alugueis"] = offers.Count + live.Count;
        var head = IslandUi.Row(10); var title = Title("Aluguéis", "Terrenos oferecidos para alugar e os contratos em andamento."); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; head.AddChild(title);
        var create = IslandUi.Action("Criar Locação", "plus", IslandUi.Kind.Purple, 40); create.Pressed += () => { var mine = _data.Parcels.Where(p => IsMine(p) && p.SalePrice is null && p.RentPrice is null && p.Kind is not ("rented" or "common")).ToList(); if (mine.Count == 0) { Notify("Você não tem terreno livre para alugar."); return; } AskWhich("Criar locação", mine, p => OpenRent(p)); }; head.AddChild(create); body.AddChild(head);
        body.AddChild(IslandUi.Text("Ofertas para alugar", 17, IslandUi.TextPrimary, true));
        body.AddChild(Table(["Código", "Terreno", "Valor", "Periodicidade", "Área"], [.6f, 1.5f, 1.2f, 1f, .9f],
            offers.Select(p => new Control[] { Cell(p.Code, IslandUi.AccentPrimary, true, .6f), Cell(p.Name, null, false, 1.5f), Cell(LandMath.Money(p.RentPrice!.Value), LandTerritoryMap.Rented, true, 1.2f), Cell($"a cada {p.RentPeriodDays} dias", null, false, 1f), Cell(LandMath.Area(p.Area), null, false, .9f),
                Actions(230, Small("Retirar oferta", IslandUi.Kind.Danger, () => { _map.Select(p.Id); _ = Do(_api!.CancelRentOffer(_island, p.Id), "Oferta de aluguel retirada."); }, IsMine(p)), Small("Ver", IslandUi.Kind.Quiet, () => ShowOnMap(p))) }).ToList(), "Nenhum terreno oferecido para aluguel.", 236));
        body.AddChild(IslandUi.Text("Contratos em andamento", 17, IslandUi.TextPrimary, true));
        body.AddChild(Table(["Imóvel", "Locatário", "Valor", "Periodicidade", "Vencimento", "Situação"], [1.5f, 1.1f, 1f, 1f, 1.1f, 1f],
            live.Select(c => new Control[] { Cell($"{c.Code} – {c.Parcel}", null, false, 1.5f), Cell(c.Tenant, null, false, 1.1f), Cell(LandMath.Money(c.Price), LandTerritoryMap.Rented, true, 1f), Cell($"a cada {c.PeriodDays} dias", null, false, 1f), Cell(ShortDay(c.ExpiresAt), null, false, 1.1f),
                Cell(c.Status == "grace" ? "Em tolerância" : "Em dia", c.Status == "grace" ? IslandUi.Warning : IslandUi.Success, true, 1f),
                Actions(230, Small("Ver Contrato", IslandUi.Kind.Secondary, () => Select("contratos")), Small("Encerrar", IslandUi.Kind.Danger, () => ConfirmEnd(c), c.ExpiresAt <= DateTimeOffset.UtcNow || IsLandlord(c), "O proprietário só encerra depois do vencimento; o servidor confere.")) }).ToList(), "Nenhum contrato em andamento.", 236));
    }

    private bool IsLandlord(LandContractRow contract) => _data!.Parcels.Any(p => p.Id == contract.ParcelId && IsMine(p));

    private void ConfirmEnd(LandContractRow contract)
    {
        var content = IslandUi.Text($"Encerrar o aluguel de {contract.Code} – {contract.Parcel} com {contract.Tenant}? O proprietário só pode encerrar depois do vencimento; os objetos do locatário voltam para o inventário dele.", 14, IslandUi.TextSecondary, false, true);
        Ask("Encerrar aluguel", content, async () => await Do(_api!.EndRental(_island, contract.Id), "Aluguel encerrado."), "Encerrar");
    }

    // ---- Contratos ----------------------------------------------------------------------------------------------------------------------

    private void BuildContracts(VBoxContainer body)
    {
        var contracts = _data!.Contracts; _rowCounts["contratos"] = contracts.Count;
        body.AddChild(Title("Contratos", "Aluguéis em andamento e os encerrados nos últimos 90 dias."));
        static (string, Color) State(string status) => status switch { "active" => ("Ativo", IslandUi.Success), "grace" => ("Em tolerância", IslandUi.Warning), "ended" => ("Encerrado", IslandUi.TextMuted), _ => (status, IslandUi.TextSecondary) };
        body.AddChild(Table(["Contrato", "Imóvel", "Locador", "Locatário", "Valor", "Vencimento", "Estado"], [.8f, 1.5f, 1.1f, 1.1f, 1f, 1.1f, 1f],
            contracts.Select(c => { var (text, color) = State(c.Status); return new Control[] { Cell(c.Id.ToString("N")[..8].ToUpperInvariant(), IslandUi.AccentPrimary, true, .8f), Cell($"{c.Code} – {c.Parcel}", null, false, 1.5f), Cell(c.Landlord, null, false, 1.1f), Cell(c.Tenant, null, false, 1.1f), Cell($"{LandMath.Money(c.Price)} / {c.PeriodDays} d", null, false, 1f), Cell(c.EndedAt is { } ended ? $"Encerrado {ShortDay(ended)}" : ShortDay(c.ExpiresAt), null, false, 1.1f), Cell(text, color, true, 1f) }; }).ToList(), "Nenhum contrato ainda."));
    }

    // ---- Prims e Recursos -----------------------------------------------------------------------------------------------------------------

    private void BuildResources(VBoxContainer body)
    {
        var t = _data!.Totals; _rowCounts["prims"] = _data.Parcels.Count;
        body.AddChild(Title("Prims e Recursos", "O limite de objetos da ilha, repartido entre os terrenos pela área, e quanto cada um usa."));
        var cards = IslandUi.Row(10); body.AddChild(cards);
        foreach (var (title, value) in new[] { ("Prims totais da ilha", LandMath.Number(t.ObjectsLimit)), ("Distribuídas aos terrenos", LandMath.Number(t.ObjectsShared)), ("Utilizadas", LandMath.Number(t.ObjectsUsed)), ("Disponíveis", LandMath.Number(Math.Max(0, t.ObjectsLimit - t.ObjectsUsed))) })
        { var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 14, 8)); var c = IslandUi.Column(0); c.AddChild(IslandUi.Text(title, 12, IslandUi.TextSecondary, true)); c.AddChild(IslandUi.Text(value, 22, IslandUi.TextPrimary, true)); card.AddChild(c); cards.AddChild(card); }
        var rows = _data.Parcels.OrderByDescending(p => p.Objects).Select(p =>
        {
            var bar = IslandUi.Progress(p.UsePercent, 12); bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; bar.SizeFlagsStretchRatio = 1.4f;
            return new Control[] { Cell(p.Code, IslandUi.AccentPrimary, true, .6f), Cell(p.Name, null, false, 1.5f), Cell($"{LandMath.Number(p.Objects)} / {LandMath.Number(p.MaxObjects)}", null, false, 1f), bar, Cell(p.MaxCost > 0 ? $"{LandMath.Number(p.Cost)} / {LandMath.Number(p.MaxCost)}" : LandMath.Number(p.Cost), null, false, 1f), Cell(p.Scripts ? "Permitidos" : "Não", null, false, .9f) };
        }).ToList();
        body.AddChild(Table(["Código", "Terreno", "Prims", "Uso", "Custo", "Scripts"], [.6f, 1.5f, 1f, 1.4f, 1f, .9f], rows, "Nenhum terreno ainda."));
    }

    // ---- Permissões ----------------------------------------------------------------------------------------------------------------------

    private void BuildPermissions(VBoxContainer body)
    {
        _rowCounts["permissoes"] = _data!.Parcels.Count;
        body.AddChild(Title("Permissões", "Quem entra e quem constrói em cada terreno. As listas de pessoas, grupos e banimentos de cada terreno abrem em “Configurar”."));
        static string Access(string mode) => mode switch { "public" => "Pública", "list" => "Só quem está na lista", "private" => "Privada", _ => mode };
        static string Build(string mode) => mode switch { "owner" => "Só o dono e o locatário", "list" => "Dono e lista de construtores", "everyone" => "Todos", "nobody" => "Só a equipe da ilha", _ => mode };
        body.AddChild(Table(["Código", "Terreno", "Acesso", "Construção", "Scripts", "Construtores"], [.6f, 1.4f, 1.3f, 1.6f, .9f, .9f],
            _data.Parcels.Select(p => new Control[] { Cell(p.Code, IslandUi.AccentPrimary, true, .6f), Cell(p.Name, null, false, 1.4f), Cell(Access(p.AccessMode), null, false, 1.3f), Cell(Build(p.BuildMode), null, false, 1.6f), Cell(p.Scripts ? "Permitidos" : "Não", null, false, .9f), Cell(p.Builders.ToString(Pt), null, false, .9f),
                Actions(130, Small("Configurar", IslandUi.Kind.Secondary, () => Inspect?.Invoke(p))) }).ToList(), "Nenhum terreno ainda.", 136));
    }

    // ---- Histórico -------------------------------------------------------------------------------------------------------------------------

    private void BuildHistory(VBoxContainer body)
    {
        var history = _data!.History; _rowCounts["historico"] = history.Count;
        body.AddChild(Title("Histórico", "O que aconteceu nos terrenos: criação, divisão, união, vendas, aluguéis, transferências e mudanças de regra (os 200 mais recentes)."));
        body.AddChild(Table(["Data", "Terreno", "Evento", "Pessoa", "Valor"], [1.2f, 1.6f, 1.5f, 1.1f, 1f],
            history.Select(h => new Control[] { Cell(Short(h.At), null, false, 1.2f), Cell(h.Code.Length > 0 ? $"{h.Code} – {h.Parcel}" : h.Parcel, null, false, 1.6f), Cell(LandMath.WhatText(h.What), null, true, 1.5f), Cell(h.Actor, null, false, 1.1f), Cell(h.Amount is { } a and > 0 ? LandMath.Money(a) : "", LandTerritoryMap.OnSale, true, 1f) }).ToList(), "Nada aconteceu ainda."));
    }
}
