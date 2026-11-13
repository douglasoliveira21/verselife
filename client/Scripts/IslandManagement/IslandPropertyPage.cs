using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>The calls of the Propriedade tab: the real estate of the whole island.</summary>
public sealed class IslandRealEstateApi(IslandManagementApi inner)
{
    private static string Base(Guid island) => $"/api/v1/worlds/islands/{island}/real-estate";
    public Task<IslandReply> Get(Guid island) => inner.Send("GET", Base(island));
    public Task<IslandReply> OfferSale(Guid island, long price, bool includeObjects, string description) => inner.Send("POST", $"{Base(island)}/sale", new { price, includeObjects, description });
    public Task<IslandReply> CancelSale(Guid island) => inner.Send("DELETE", $"{Base(island)}/sale");
    public Task<IslandReply> OfferRent(Guid island, long price, int days, int min, int max, long deposit, int grace, bool autoRenewAllowed = false) => inner.Send("POST", $"{Base(island)}/rental-offer", new { pricePerPeriod = price, periodDays = days, minPeriods = min, maxPeriods = Math.Max(min, max), depositCoins = deposit, graceDays = grace, autoRenewAllowed });
    public Task<IslandReply> CancelRent(Guid island) => inner.Send("DELETE", $"{Base(island)}/rental-offer");
    public Task<IslandReply> SetRules(Guid island, string access, string build, bool scripts, bool voice, bool media, int? autoReturnMinutes, bool applyToExisting) =>
        inner.Send("PUT", $"/api/v1/worlds/islands/{island}/land/rules", new { accessMode = access, buildMode = build, allowScripts = scripts, allowVoice = voice, allowMedia = media, autoReturnMinutes, applyToExisting });
    public Task<IslandReply> Hosting(Guid island) => inner.Send("GET", $"{Base(island)}/hosting");
    public Task<IslandReply> SubscribeHosting(Guid island, Guid plan) => inner.Send("POST", $"{Base(island)}/hosting/subscribe", new { planId = plan, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> RenewHosting(Guid island) => inner.Send("POST", $"{Base(island)}/hosting/renew", new { planId = Guid.Empty, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> CancelTransfer(Guid island) => inner.Send("DELETE", $"{Base(island)}/transfer");
    public Task<IslandReply> AcceptTransfer(Guid island) => inner.Send("POST", $"{Base(island)}/transfer/accept", new { });
    public Task<IslandReply> DeclineTransfer(Guid island) => inner.Send("POST", $"{Base(island)}/transfer/decline", new { });
    public Task<IslandReply> Transfer(Guid island, string name) => inner.Send("POST", $"{Base(island)}/transfer", new { name });
    public Task<IslandReply> Renew(Guid island, Guid lease, int periods) => inner.Send("POST", $"{Base(island)}/leases/{lease}/renew", new { periods, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> End(Guid island, Guid lease) => inner.Send("POST", $"{Base(island)}/leases/{lease}/end", new { });
}

/// <summary>
/// The Propriedade tab: who owns the island, what kind of region it is, whether it is for sale, for rent or rented, and the buttons that put it on the market, take it back, hand it over, or (for a renter) renew or leave.
/// The server decides who may do what; the owner sees the buttons, the others only read.
/// </summary>
public partial class IslandPropertyPage : ScrollContainer
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private VBoxContainer _root = null!;
    private Label _status = null!;
    private IslandRealEstateApi? _api;
    private Guid _island;
    private JsonElement _view;
    private bool _loaded;

    public Action<string>? Notify { get; set; }
    public event Action? Changed;

    public bool IsOwner => _loaded && _view.ValueKind == JsonValueKind.Object && Flag(_view, "isOwner");
    public bool IsTenant => _loaded && _view.ValueKind == JsonValueKind.Object && Flag(_view, "isTenant");
    public string StatusForTesting => _status.Text;
    public string TextForTesting => string.Join("|", _root.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public bool HasButtonForTesting(string text) => _root.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text);
    public bool ButtonEnabledForTesting(string text) => _root.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { Disabled: false };

    public void Bind(IslandManagementApi api) => _api = new IslandRealEstateApi(api);
    public void SetIsland(Guid island) { if (island != _island) { _island = island; _loaded = false; } }

    private static bool Flag(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static long Long(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
    private static int Int(JsonElement e, string n) => (int)Long(e, n);
    private static DateTimeOffset Date(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : default;
    private static JsonElement? Obj(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Disabled; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        _root = IslandUi.Column(14); _root.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(_root);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); _root.AddChild(_status);
    }

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        _status.Text = "Carregando…"; var reply = await _api.Get(_island);
        if (!reply.Ok) { _status.Text = reply.Status == 404 ? "O servidor ainda não tem a propriedade da ilha. Ela chega com a próxima publicação da API." : LandManagementApi.Words(reply); return; }
        _view = reply.Body.Clone(); _loaded = true; _status.Text = ""; Render();
    }

    private void Clear() { foreach (var child in _root.GetChildren().Skip(1)) child.QueueFree(); }

    private static Control Line(string key, string value, Color? color = null)
    {
        var row = IslandUi.Row(10); var k = IslandUi.Text(key, 14, IslandUi.TextSecondary); k.CustomMinimumSize = new Vector2(190, 0); row.AddChild(k);
        var v = IslandUi.Text(value, 14, color ?? IslandUi.TextPrimary, true, true); v.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(v); return row;
    }

    private void Render()
    {
        Clear(); var v = _view; var owner = IsOwner; var tenant = IsTenant;
        var lease = Obj(v, "lease"); var sale = Obj(v, "sale"); var offer = Obj(v, "rentOffer");
        var (card, body) = IslandUi.Card("Propriedade", "shield"); card.SizeFlagsVertical = SizeFlags.ShrinkBegin; _root.AddChild(card);
        body.AddChild(Line("Proprietário atual", Str(v, "owner"))); body.AddChild(Line("ID da ilha", Str(v, "islandId")));
        body.AddChild(Line("Classe da região", Str(v, "region") == "homestead" ? "Homestead" : "Full Region")); body.AddChild(Line("Tamanho", $"{Int(v, "size")} m × {Int(v, "size")} m")); body.AddChild(Line("Terrenos", Int(v, "parcels").ToString(Pt)));
        body.AddChild(Line("Criada em", Date(v, "createdAt").ToLocalTime().ToString("dd/MM/yyyy", Pt)));
        if (v.TryGetProperty("hosting", out var hosting) && hosting.ValueKind == System.Text.Json.JsonValueKind.Object)
            body.AddChild(Line("Hospedagem", Str(hosting, "status") == "active" ? $"{Str(hosting, "plan")}, até {Date(hosting, "expiresAt").ToLocalTime():dd/MM/yyyy} · {Int(hosting, "maxPlayers")} pessoas · {LandMath.Number(Int(hosting, "maxObjects"))} objetos" : Str(hosting, "status") == "ended" ? $"{Str(hosting, "plan")} encerrado: contrate um plano em Gerenciar Hospedagem" : $"{Str(hosting, "plan")} vencido: renove até {Date(hosting, "recoveryUntil").ToLocalTime():dd/MM/yyyy} em Gerenciar Hospedagem", Str(hosting, "status") == "active" ? IslandUi.TextPrimary : IslandUi.Warning));
        var situation = lease is { } l ? $"Alugada para {Str(l, "tenant")} até {Date(l, "expiresAt").ToLocalTime():dd/MM/yyyy}" + (Str(l, "status") == "grace" ? " (em tolerância)" : "")
            : sale is { } s ? $"À venda por {LandMath.Money(Long(s, "price"))}" + (Flag(s, "exclusive") ? " (exclusiva para uma pessoa)" : "")
            : offer is { } o ? $"Para alugar por {LandMath.Money(Long(o, "price"))} a cada {Int(o, "periodDays")} dias" : "Sem anúncio";
        body.AddChild(Line("Situação", situation, lease is not null ? LandTerritoryMap.Rented : sale is not null ? IslandUi.AccentSecondary : offer is not null ? LandTerritoryMap.Rented : IslandUi.TextPrimary));
        if (lease is { } li) { body.AddChild(Line("Valor do aluguel", $"{LandMath.Money(Long(li, "price"))} a cada {Int(li, "periodDays")} dias")); if (Long(li, "deposit") > 0) body.AddChild(Line("Caução", LandMath.Money(Long(li, "deposit")))); body.AddChild(Line("Renovação", Flag(li, "autoRenew") ? "Automática: um período é cobrado no vencimento" : "Manual")); }

        if (v.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Object) body.AddChild(Line("Regras da ilha", RulesText(rules)));
        var buttons = IslandUi.Row(10); buttons.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(buttons);
        JsonElement? pending = v.TryGetProperty("transfer", out var transferView) && transferView.ValueKind == JsonValueKind.Object ? transferView.Clone() : null;
        if (Flag(v, "isWelcome")) body.AddChild(IslandUi.Text("Esta é a ilha de boas-vindas do jogo: ela não pode ser vendida, alugada nem transferida.", 13, IslandUi.TextMuted, false, true));
        else if (pending is { } wait)
        {
            body.AddChild(Line("Transferência", Flag(wait, "forMe") ? $"{Str(v, "owner")} quer te dar esta ilha, até {Date(wait, "expiresAt").ToLocalTime():dd/MM/yyyy HH:mm}" : $"Oferecida de presente a {Str(wait, "to")}, até {Date(wait, "expiresAt").ToLocalTime():dd/MM/yyyy HH:mm}", IslandUi.AccentSecondary));
            if (Flag(wait, "forMe"))
            {
                var accept = IslandUi.Action("Aceitar Ilha", "link", IslandUi.Kind.Primary, 42); accept.Pressed += () => Ask("Aceitar ilha", IslandUi.Text($"A ilha {Str(v, "name")}, os terrenos e os objetos de {Str(v, "owner")} passam a ser seus agora.", 14, IslandUi.TextSecondary, false, true), async () => await Do(_api!.AcceptTransfer(_island), "A ilha agora é sua."), "Aceitar"); buttons.AddChild(accept);
                var decline = IslandUi.Action("Recusar", null, IslandUi.Kind.Danger, 42); decline.Pressed += () => _ = Do(_api!.DeclineTransfer(_island), "Você recusou a ilha."); buttons.AddChild(decline);
            }
            else if (owner) { var cancel = IslandUi.Action("Cancelar Transferência", null, IslandUi.Kind.Danger, 42); cancel.Pressed += () => _ = Do(_api!.CancelTransfer(_island), "Transferência cancelada."); buttons.AddChild(cancel); body.AddChild(IslandUi.Text("Enquanto a pessoa não responde, a ilha continua sua e não pode ir ao mercado.", 13, IslandUi.TextMuted, false, true)); }
        }
        else if (owner && lease is null)
        {
            if (sale is null && offer is null) { var sell = IslandUi.Action("Colocar Ilha à Venda", "coins", IslandUi.Kind.Orange, 42); sell.Pressed += OpenSale; buttons.AddChild(sell); var rent = IslandUi.Action("Alugar Ilha", "calendar", IslandUi.Kind.Purple, 42); rent.Pressed += OpenRent; buttons.AddChild(rent); }
            else { var withdraw = IslandUi.Action(sale is not null ? "Retirar da Venda" : "Retirar Oferta de Aluguel", null, IslandUi.Kind.Danger, 42); withdraw.Pressed += () => _ = Do(sale is not null ? _api!.CancelSale(_island) : _api!.CancelRent(_island), "Anúncio retirado."); buttons.AddChild(withdraw); }
            var give = IslandUi.Action("Transferir Propriedade", "link", IslandUi.Kind.Secondary, 42); give.Pressed += OpenTransfer; give.Disabled = sale is not null; give.TooltipText = sale is not null ? "Retire a ilha da venda antes." : "Oferece a ilha, com os terrenos e objetos, a outra pessoa, de graça. Ela precisa aceitar."; buttons.AddChild(give);
        }
        else if (owner && lease is { } own)
        {
            var end = IslandUi.Action("Encerrar Aluguel", null, IslandUi.Kind.Danger, 42); end.Pressed += () => ConfirmEnd(Str(own, "id"), false); end.Disabled = Date(own, "expiresAt") > DateTimeOffset.UtcNow; end.TooltipText = "O proprietário só encerra depois do vencimento."; buttons.AddChild(end);
            body.AddChild(IslandUi.Text("Com a ilha alugada, quem aluga cuida dela; a venda e a transferência esperam o fim do contrato.", 13, IslandUi.TextMuted, false, true));
        }
        else if (tenant && lease is { } mine)
        {
            var renew = IslandUi.Action("Renovar", "calendar", IslandUi.Kind.Purple, 42); renew.Pressed += () => OpenRenew(mine); buttons.AddChild(renew);
            var leave = IslandUi.Action("Sair do Aluguel", null, IslandUi.Kind.Danger, 42); leave.Pressed += () => ConfirmEnd(Str(mine, "id"), true); buttons.AddChild(leave);
        }
        else body.AddChild(IslandUi.Text("Só o proprietário (ou quem aluga a ilha) muda isto. Você pode ver as informações.", 13, IslandUi.TextMuted, false, true));
        if (Flag(v, "isOwner") || Flag(v, "canEditRules"))
        { var host = IslandUi.Action("Gerenciar Hospedagem", "settings", IslandUi.Kind.Secondary, 42); host.TooltipText = "O plano da plataforma que mantém a ilha no ar: ver, contratar e renovar."; host.Pressed += () => _ = OpenHostingAsync(); buttons.AddChild(host); }
        if (Flag(v, "canEditRules") && v.TryGetProperty("rules", out var current) && current.ValueKind == JsonValueKind.Object)
        { var editRules = IslandUi.Action("Editar Regras", "settings", IslandUi.Kind.Secondary, 42); editRules.TooltipText = "Como os terrenos novos da ilha começam: entrada, construção, scripts, voz e mídia."; editRules.Pressed += () => OpenRules(current.Clone()); buttons.AddChild(editRules); }

        if (v.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Array && history.GetArrayLength() > 0)
        {
            var (hCard, hBody) = IslandUi.Card("Histórico de propriedade", "history"); hCard.SizeFlagsVertical = SizeFlags.ShrinkBegin; _root.AddChild(hCard);
            foreach (var h in history.EnumerateArray())
            {
                var amount = Long(h, "amount"); var who = Str(h, "to") is { Length: > 0 } to ? $"{(Str(h, "from") is { Length: > 0 } f ? f + " → " : "")}{to}" : Str(h, "from");
                hBody.AddChild(Line(Date(h, "at").ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt), $"{TypeText(Str(h, "type"))}{(who.Length > 0 ? " · " + who : "")}{(amount > 0 ? " · " + LandMath.Money(amount) : "")}"));
            }
        }
    }

    private static string TypeText(string type) => type switch { "sale" => "Venda", "transfer" => "Transferência", "sale_listed" => "Colocada à venda", "sale_cancelled" => "Venda retirada", "rent_listed" => "Oferecida para aluguel", "rent_cancelled" => "Oferta de aluguel retirada", "rent_start" => "Aluguel iniciado", "rent_payment" => "Aluguel renovado", "rent_end" => "Aluguel encerrado", _ => type };

    // ---- actions ---------------------------------------------------------------------------------------------------------------------------

    private async Task<bool> Do(Task<IslandReply> call, string done)
    {
        var reply = await call;
        if (!reply.Ok) { Notify?.Invoke(LandManagementApi.Words(reply)); return false; }
        Notify?.Invoke(done); await LoadAsync(); Changed?.Invoke(); return true;
    }

    private void Ask(string title, Control content, Func<Task<bool>> accept, string okText, Action<Button>? withOk = null)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = okText, CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() };
        dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); AddChild(dialog); withOk?.Invoke(dialog.GetOkButton());
        dialog.Confirmed += async () => { await accept(); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree();
        IslandUi.Fit(dialog, 500);
    }

    public Task<bool> SellForTesting(long price, bool objects) => Do(_api!.OfferSale(_island, price, objects, ""), "Ilha anunciada à venda.");
    public Task<bool> RentOutForTesting(long price, int days, long deposit) => Do(_api!.OfferRent(_island, price, days, 1, 6, deposit, 3), "Ilha oferecida para aluguel.");
    public Task<bool> WithdrawForTesting() => Do(Obj(_view, "sale") is not null ? _api!.CancelSale(_island) : _api!.CancelRent(_island), "Anúncio retirado.");
    public Task<bool> TransferForTesting(string name) => Do(_api!.Transfer(_island, name), "A ilha foi transferida.");
    public Task<bool> CancelTransferForTesting() => Do(_api!.CancelTransfer(_island), "Transferência cancelada.");
    public Task<bool> AcceptTransferForTesting() => Do(_api!.AcceptTransfer(_island), "A ilha agora é sua.");
    public Task<bool> DeclineTransferForTesting() => Do(_api!.DeclineTransfer(_island), "Você recusou a ilha.");
    public Task<bool> RenewForTesting(int periods) => Obj(_view, "lease") is { } l ? Do(_api!.Renew(_island, Guid.Parse(Str(l, "id")), periods), "Aluguel renovado.") : Task.FromResult(false);
    public Task<bool> LeaveForTesting() => Obj(_view, "lease") is { } l ? Do(_api!.End(_island, Guid.Parse(Str(l, "id"))), "Aluguel encerrado.") : Task.FromResult(false);

    private void OpenSale()
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("A ilha inteira vai à venda, com os terrenos e os objetos que ainda são seus. Quem comprar passa a ser o dono dela. Contratos de aluguel em andamento continuam.", 14, IslandUi.TextSecondary, false, true));
        var price = IslandUi.Number(1, 1_000_000_000, 1, " L$"); price.Value = 100000; content.AddChild(price);
        var objects = new CheckBox { Text = "Incluir os meus objetos na venda (senão voltam para o meu inventário)", ButtonPressed = true, FocusMode = FocusModeEnum.None }; content.AddChild(objects);
        var description = IslandUi.Input("Descrição do anúncio (opcional)", 200); content.AddChild(description);
        Ask("Colocar ilha à venda", content, async () => await Do(_api!.OfferSale(_island, (long)price.Value, objects.ButtonPressed, description.Text.Trim()), "Ilha anunciada à venda."), "Anunciar");
    }

    private void OpenRent()
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("Quem alugar cuida da ilha durante o contrato, mas ela continua sua.", 14, IslandUi.TextSecondary, false, true));
        var price = IslandUi.Number(1, 1_000_000_000, 1, " L$ por período"); price.Value = 5000; content.AddChild(price);
        var period = IslandUi.Dropdown([("7 dias", "7"), ("14 dias", "14"), ("30 dias", "30")]); IslandUi.Select(period, "30"); content.AddChild(period);
        var min = IslandUi.Number(1, 52, 1, " período(s) no mínimo"); min.Value = 1; content.AddChild(min);
        var max = IslandUi.Number(1, 52, 1, " período(s) no máximo"); max.Value = 6; content.AddChild(max);
        var deposit = IslandUi.Number(0, 1_000_000_000, 1, " L$ de caução"); content.AddChild(deposit);
        var grace = IslandUi.Number(0, 30, 1, " dia(s) de tolerância"); grace.Value = 3; content.AddChild(grace);
        var autoRenew = new CheckBox { Text = "Deixar quem aluga renovar sozinho a cada vencimento", FocusMode = FocusModeEnum.None, TooltipText = "Quem aluga escolhe; se escolher, um período a mais é cobrado da carteira dele no vencimento." }; content.AddChild(autoRenew);
        Ask("Alugar ilha", content, async () => await Do(_api!.OfferRent(_island, (long)price.Value, int.Parse(IslandUi.IdOf(period)), (int)min.Value, (int)max.Value, (long)deposit.Value, (int)grace.Value, autoRenew.ButtonPressed), "Ilha oferecida para aluguel."), "Anunciar");
    }

    // ---- hosting --------------------------------------------------------------------------------------------------------------------

    internal static string PlanText(JsonElement p) => $"{LandMath.Money(Long(p, "price"))} a cada {Int(p, "days")} dias · até {Int(p, "maxPlayers")} pessoas · {LandMath.Number(Int(p, "maxObjects"))} objetos · {Int(p, "maxSize")} m";

    /// <summary>The window of the plan that keeps the island running: what it has, what the owner can buy, and renewing. Only the owner pays; the island's staff can look.</summary>
    private async Task OpenHostingAsync()
    {
        var reply = await _api!.Hosting(_island); if (!reply.Ok) { Notify?.Invoke(LandManagementApi.Words(reply)); return; }
        var h = reply.Body.Clone(); var content = IslandUi.Column(8); content.CustomMinimumSize = new Vector2(580, 0);
        var status = Str(h, "status"); var owner = Flag(h, "isOwner");
        if (h.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object)
        {
            var plan = current.GetProperty("plan");
            content.AddChild(Line("Plano", Str(plan, "name"))); content.AddChild(Line("Inclui", PlanText(plan)));
            content.AddChild(Line("Situação", status == "active" ? $"Em vigor até {Date(current, "expiresAt").ToLocalTime():dd/MM/yyyy HH:mm}" : status == "recovery" ? $"Venceu em {Date(current, "expiresAt").ToLocalTime():dd/MM/yyyy}: renove até {Date(current, "recoveryUntil").ToLocalTime():dd/MM/yyyy HH:mm}" : $"Encerrado em {Date(current, "recoveryUntil").ToLocalTime():dd/MM/yyyy}: escolha um plano", status == "active" ? IslandUi.Success : IslandUi.Warning));
        }
        else content.AddChild(Line("Plano", "Esta ilha ainda não tem plano de hospedagem.", IslandUi.Warning));
        if (owner && h.TryGetProperty("balance", out var balance) && balance.ValueKind == JsonValueKind.Number) content.AddChild(Line("Seu saldo", LandMath.Money(balance.GetInt64())));
        if (!owner) content.AddChild(IslandUi.Text("Só o proprietário da ilha paga a hospedagem. Você pode ver como ela está.", 13, IslandUi.TextMuted, false, true));

        var dialog = new AcceptDialog { Title = "Hospedagem da ilha", OkButtonText = "Fechar", Unresizable = true, Theme = ViewerTheme.Create() };
        if (Flag(h, "canRenew"))
        {
            var renew = IslandUi.Action("Renovar Plano", "calendar", IslandUi.Kind.Purple, 40); renew.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            renew.TooltipText = "Paga mais um período do plano que a ilha tem."; renew.Pressed += () => { dialog.Hide(); dialog.QueueFree(); _ = Do(_api!.RenewHosting(_island), "Hospedagem renovada."); }; content.AddChild(renew);
        }
        var plans = h.TryGetProperty("plans", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
        if (Flag(h, "canSubscribe"))
        {
            content.AddChild(IslandUi.Text(plans.Count == 0 ? "Não há planos à venda agora." : "Planos disponíveis", 15, IslandUi.TextPrimary, true));
            foreach (var plan in plans)
            {
                var id = Guid.Parse(Str(plan, "id")); var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, 8, 1, 10, 6));
                var row = IslandUi.Row(10); card.AddChild(row); var info = IslandUi.Column(2); info.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(info);
                info.AddChild(IslandUi.Text(Str(plan, "name"), 15, IslandUi.TextPrimary, true)); info.AddChild(IslandUi.Text(PlanText(plan), 13, IslandUi.TextSecondary, false, true));
                var buy = IslandUi.Action("Contratar", "coins", IslandUi.Kind.Orange, 38); buy.Pressed += () => { dialog.Hide(); dialog.QueueFree(); _ = Do(_api!.SubscribeHosting(_island, id), "Hospedagem contratada."); }; row.AddChild(buy); content.AddChild(card);
            }
        }
        dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); AddChild(dialog); dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree(); IslandUi.Fit(dialog, 620);
    }

    public Task OpenHostingForTesting() => OpenHostingAsync();
    public void OpenRulesForTesting() { if (_view.ValueKind == JsonValueKind.Object && _view.TryGetProperty("rules", out var current) && current.ValueKind == JsonValueKind.Object) OpenRules(current.Clone()); }
    public void OpenTransferForTesting() => OpenTransfer();
    public System.Collections.Generic.IEnumerable<ConfirmationDialog> DialogsForTesting => GetChildren().OfType<ConfirmationDialog>();
    public Task<bool> SubscribeHostingForTesting(Guid plan) => Do(_api!.SubscribeHosting(_island, plan), "Hospedagem contratada.");
    public Task<bool> RenewHostingForTesting() => Do(_api!.RenewHosting(_island), "Hospedagem renovada.");

    private static readonly (string Text, string Id)[] AccessChoices = [("Livre: qualquer pessoa entra", "public"), ("Lista: só quem está na lista", "list"), ("Privado: só o dono", "private")];
    private static readonly (string Text, string Id)[] BuildChoices = [("Só o dono", "owner"), ("Dono e lista", "list"), ("Todos", "everyone"), ("Ninguém", "nobody")];

    /// <summary>The rules in one line, as the Propriedade tab shows them.</summary>
    internal static string RulesText(JsonElement r)
    {
        string Pick((string Text, string Id)[] choices, string id) => choices.FirstOrDefault(c => c.Id == id).Text?.Split(':')[0] ?? id;
        var back = r.TryGetProperty("autoReturnMinutes", out var m) && m.ValueKind == JsonValueKind.Number ? $"{m.GetInt32()} min" : "desligada";
        return $"Entrada: {Pick(AccessChoices, Str(r, "accessMode"))} · Construir: {Pick(BuildChoices, Str(r, "buildMode"))} · Scripts: {(Flag(r, "allowScripts") ? "sim" : "não")} · Voz: {(Flag(r, "allowVoice") ? "sim" : "não")} · Mídia: {(Flag(r, "allowMedia") ? "sim" : "não")} · Devolução de objetos: {back}";
    }

    private void OpenRules(JsonElement rules)
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("É assim que os terrenos novos da ilha começam. Cada dono de terreno ainda pode mudar o seu em “Sobre este Terreno”.", 14, IslandUi.TextSecondary, false, true));
        content.AddChild(IslandUi.Text("Quem pode entrar nos terrenos novos", 13, IslandUi.TextSecondary));
        var access = IslandUi.Dropdown(AccessChoices); IslandUi.Select(access, Str(rules, "accessMode")); content.AddChild(access);
        content.AddChild(IslandUi.Text("Quem pode construir", 13, IslandUi.TextSecondary));
        var build = IslandUi.Dropdown(BuildChoices); IslandUi.Select(build, Str(rules, "buildMode")); content.AddChild(build);
        CheckBox Box(string text, bool on) { var b = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None }; content.AddChild(b); return b; }
        var scripts = Box("Permitir scripts", Flag(rules, "allowScripts")); var voice = Box("Permitir voz por proximidade", Flag(rules, "allowVoice")); var media = Box("Permitir mídia nos objetos de quem constrói", Flag(rules, "allowMedia"));
        var back = IslandUi.Number(0, 10080, 5, " min para devolver objetos de estranhos (0 = não devolver)"); back.Value = rules.TryGetProperty("autoReturnMinutes", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 0; content.AddChild(back);
        var existing = Box("Aplicar também aos terrenos que ainda são do dono da ilha e não estão alugados", false);
        content.AddChild(IslandUi.Text("Terrenos de outras pessoas e terrenos alugados nunca são mudados por aqui.", 13, IslandUi.TextMuted, false, true));
        Ask("Regras da ilha", content, async () => await Do(_api!.SetRules(_island, IslandUi.IdOf(access), IslandUi.IdOf(build), scripts.ButtonPressed, voice.ButtonPressed, media.ButtonPressed, back.Value > 0 ? (int)back.Value : null, existing.ButtonPressed), "Regras da ilha salvas."), "Salvar");
    }

    public Task<bool> SetRulesForTesting(string access, string build, bool scripts, bool voice, bool media, int? back, bool existing) => Do(_api!.SetRules(_island, access, build, scripts, voice, media, back, existing), "Regras da ilha salvas.");

    private void OpenTransfer()
    {
        var islandName = Str(_view, "name"); var parcels = Int(_view, "parcels");
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text($"A ilha {islandName}, com {parcels} terreno(s) e todos os objetos que são seus, é oferecida a outra pessoa, de graça. Nada muda até ela aceitar (ela tem 7 dias; você pode cancelar antes). Se aceitar, o plano de hospedagem vai junto e só ela pode devolver.", 14, IslandUi.Error, false, true));
        var name = IslandUi.Input("Nome da pessoa", 60); content.AddChild(name);
        var confirm = IslandUi.Input("Para confirmar, digite o nome da ilha", 120); content.AddChild(confirm);
        content.AddChild(IslandUi.Text("A pessoa recebe um aviso em Minhas Propriedades e escolhe se aceita.", 13, IslandUi.TextMuted, false, true));
        Ask("Transferir propriedade", content, async () => await Do(_api!.Transfer(_island, name.Text.Trim()), "Oferta de transferência enviada."), "Oferecer", ok => { ok.Disabled = true; confirm.TextChanged += typed => ok.Disabled = typed.Trim() != islandName; });
    }

    private void OpenRenew(JsonElement lease)
    {
        var content = IslandUi.Column(8); var price = Long(lease, "price"); var days = Int(lease, "periodDays");
        content.AddChild(IslandUi.Text("Renovar o aluguel da ilha por mais:", 14, IslandUi.TextPrimary, true));
        var periods = IslandUi.Number(1, 12, 1, $" período(s) de {days} dias"); periods.Value = 1; content.AddChild(periods);
        var total = IslandUi.Text("", 16, LandTerritoryMap.Rented, true); content.AddChild(total); void Update() => total.Text = $"Total agora: {LandMath.Money(price * (long)periods.Value)}"; periods.ValueChanged += x => Update(); Update();
        var id = Guid.Parse(Str(lease, "id")); Ask("Renovar aluguel", content, async () => await Do(_api!.Renew(_island, id, (int)periods.Value), "Aluguel renovado."), "Renovar");
    }

    private void ConfirmEnd(string lease, bool tenant)
    {
        var text = tenant ? "Sair do aluguel da ilha? Os seus objetos voltam para o seu inventário e a caução volta para você." : "Encerrar o aluguel? Só vale depois do vencimento; os objetos de quem alugava voltam para o inventário dele.";
        Ask(tenant ? "Sair do aluguel" : "Encerrar aluguel", IslandUi.Text(text, 14, IslandUi.TextSecondary, false, true), async () => await Do(_api!.End(_island, Guid.Parse(lease)), tenant ? "Você saiu do aluguel." : "Aluguel encerrado."), tenant ? "Sair" : "Encerrar");
    }
}
