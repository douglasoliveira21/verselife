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
            body.AddChild(Line("Hospedagem", Str(hosting, "status") == "active" ? $"{Str(hosting, "plan")}, até {Date(hosting, "expiresAt").ToLocalTime():dd/MM/yyyy} · {Int(hosting, "maxPlayers")} pessoas · {LandMath.Number(Int(hosting, "maxObjects"))} objetos" : $"{Str(hosting, "plan")} vencido: renove até {Date(hosting, "recoveryUntil").ToLocalTime():dd/MM/yyyy}", Str(hosting, "status") == "active" ? IslandUi.TextPrimary : IslandUi.Warning));
        var situation = lease is { } l ? $"Alugada para {Str(l, "tenant")} até {Date(l, "expiresAt").ToLocalTime():dd/MM/yyyy}" + (Str(l, "status") == "grace" ? " (em tolerância)" : "")
            : sale is { } s ? $"À venda por {LandMath.Money(Long(s, "price"))}" + (Flag(s, "exclusive") ? " (exclusiva para uma pessoa)" : "")
            : offer is { } o ? $"Para alugar por {LandMath.Money(Long(o, "price"))} a cada {Int(o, "periodDays")} dias" : "Sem anúncio";
        body.AddChild(Line("Situação", situation, lease is not null ? LandTerritoryMap.Rented : sale is not null ? IslandUi.AccentSecondary : offer is not null ? LandTerritoryMap.Rented : IslandUi.TextPrimary));
        if (lease is { } li) { body.AddChild(Line("Valor do aluguel", $"{LandMath.Money(Long(li, "price"))} a cada {Int(li, "periodDays")} dias")); if (Long(li, "deposit") > 0) body.AddChild(Line("Caução", LandMath.Money(Long(li, "deposit")))); body.AddChild(Line("Renovação", Flag(li, "autoRenew") ? "Automática: um período é cobrado no vencimento" : "Manual")); }

        var buttons = IslandUi.Row(10); buttons.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(buttons);
        if (Flag(v, "isWelcome")) body.AddChild(IslandUi.Text("Esta é a ilha de boas-vindas do jogo: ela não pode ser vendida, alugada nem transferida.", 13, IslandUi.TextMuted, false, true));
        else if (owner && lease is null)
        {
            if (sale is null && offer is null) { var sell = IslandUi.Action("Colocar Ilha à Venda", "coins", IslandUi.Kind.Orange, 42); sell.Pressed += OpenSale; buttons.AddChild(sell); var rent = IslandUi.Action("Alugar Ilha", "calendar", IslandUi.Kind.Purple, 42); rent.Pressed += OpenRent; buttons.AddChild(rent); }
            else { var withdraw = IslandUi.Action(sale is not null ? "Retirar da Venda" : "Retirar Oferta de Aluguel", null, IslandUi.Kind.Danger, 42); withdraw.Pressed += () => _ = Do(sale is not null ? _api!.CancelSale(_island) : _api!.CancelRent(_island), "Anúncio retirado."); buttons.AddChild(withdraw); }
            var give = IslandUi.Action("Transferir Propriedade", "link", IslandUi.Kind.Secondary, 42); give.Pressed += OpenTransfer; give.Disabled = sale is not null; give.TooltipText = sale is not null ? "Retire a ilha da venda antes." : "Entrega a ilha, com os terrenos e objetos, a outra pessoa, de graça."; buttons.AddChild(give);
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

    private void Ask(string title, Control content, Func<Task<bool>> accept, string okText)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = okText, CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() };
        dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); AddChild(dialog);
        dialog.Confirmed += async () => { await accept(); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree();
        dialog.PopupCentered(new Vector2I(500, 0));
    }

    public Task<bool> SellForTesting(long price, bool objects) => Do(_api!.OfferSale(_island, price, objects, ""), "Ilha anunciada à venda.");
    public Task<bool> RentOutForTesting(long price, int days, long deposit) => Do(_api!.OfferRent(_island, price, days, 1, 6, deposit, 3), "Ilha oferecida para aluguel.");
    public Task<bool> WithdrawForTesting() => Do(Obj(_view, "sale") is not null ? _api!.CancelSale(_island) : _api!.CancelRent(_island), "Anúncio retirado.");
    public Task<bool> TransferForTesting(string name) => Do(_api!.Transfer(_island, name), "A ilha foi transferida.");
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

    private void OpenTransfer()
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("A ilha, com todos os terrenos e objetos seus, passa para outra pessoa, de graça. Isso não pode ser desfeito por você.", 14, IslandUi.Error, false, true));
        var name = IslandUi.Input("Nome da pessoa", 60); content.AddChild(name);
        Ask("Transferir propriedade", content, async () => await Do(_api!.Transfer(_island, name.Text.Trim()), "A ilha foi transferida."), "Transferir");
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
