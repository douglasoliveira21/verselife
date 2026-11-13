using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.ParcelInfo;

/// <summary>The eight tabs of "Sobre este Terreno".</summary>
public partial class ParcelInfoWindow
{
    private LineEdit? _nameEdit;
    private TextEdit? _descEdit;
    private OptionButton? _categoryDrop, _teleportDrop;
    private Label? _arrivalText;
    private (double X, double Z, double Yaw)? _pendingArrival;
    private int _renderVersion;
    private readonly List<(Guid Id, CheckBox Box, bool Authorized)> _objectRows = [];
    private LineEdit? _returnReason;
    private CheckBox? _voiceBox, _mediaBox;

    private static readonly string[] BuildModes = ["owner", "list", "everyone", "nobody"];
    private static readonly string[] BuildModeTexts = ["Só o dono e o locatário", "O dono e a lista de construtores", "Todos", "Ninguém (só a equipe da ilha)"];
    private static readonly int?[] AutoReturns = [null, 5, 15, 30, 60];
    private static readonly string[] AccessModes = ["public", "list", "private"];
    private static readonly string[] TeleportModes = ["free", "forced", "disabled"];

    // ---- test hooks ----------------------------------------------------------------------------------------------------------------

    public string NameFieldForTesting { get => _nameEdit?.Text ?? ""; set { if (_nameEdit is not null) _nameEdit.Text = value; } }
    public string DescriptionFieldForTesting { get => _descEdit?.Text ?? ""; set { if (_descEdit is not null) _descEdit.Text = value; } }
    public void ChooseCategoryForTesting(string id) { if (_categoryDrop is not null) IslandUi.Select(_categoryDrop, id); }
    public void ChooseTeleportForTesting(string id) { if (_teleportDrop is not null) IslandUi.Select(_teleportDrop, id); }
    public Task<bool> SaveGeneralForTesting() => SaveGeneralAsync();
    public void UseMyPositionForTesting() => UseMyPosition();
    public Task<bool> SellForTesting(long price, bool objects) => Do(Api.OfferSale(IslandId, _parcel, price, objects, ""), "Terreno anunciado à venda.");
    public Task<bool> WithdrawSaleForTesting() => Do(Api.CancelSale(IslandId, _parcel), "Venda retirada.");
    public Task<bool> OfferRentForTesting(long price, int days) => Do(Api.OfferRent(IslandId, _parcel, price, days, 1, 6, 100, 3), "Terreno oferecido para aluguel.");
    public Task<bool> WithdrawRentForTesting() => Do(Api.CancelRent(IslandId, _parcel), "Oferta de aluguel retirada.");
    public Task<bool> BuyForTesting() => Obj(About, "sale") is { } s ? Do(Api.Buy(IslandId, _parcel, Long(s, "price")), "Terreno comprado.") : Task.FromResult(false);
    public Task<bool> RentForTesting(int periods) => Obj(About, "rentOffer") is { } o ? Do(Api.Rent(IslandId, _parcel, periods, Long(o, "price")), "Terreno alugado.") : Task.FromResult(false);
    public Task<bool> RenewForTesting(int periods) => Obj(About, "rental") is { } r && Guid.TryParse(Str(r, "id"), out var id) ? Do(Api.Renew(IslandId, id, periods), "Aluguel renovado.") : Task.FromResult(false);
    public Task<bool> LeaveForTesting() => Obj(About, "rental") is { } r && Guid.TryParse(Str(r, "id"), out var id) ? Do(Api.End(IslandId, id), "Aluguel encerrado.") : Task.FromResult(false);
    public Task<bool> SetImageForTesting(string image) => Do(Api.Image(IslandId, _parcel, image), image.Length == 0 ? "Imagem removida." : "Imagem atualizada.");
    public Task<bool> SetAccessModeForTesting(string mode) => Do(Api.AccessMode(IslandId, _parcel, mode), "Acesso atualizado.");
    public Task<bool> SetOptionsForTesting(string buildMode, bool scripts, int? minutes, bool? voice = null, bool? media = null) => Do(Api.Options(IslandId, _parcel, buildMode, scripts, minutes, voice, media), "Permissões atualizadas.");
    public bool VoiceFieldForTesting { get => _voiceBox?.ButtonPressed ?? true; set { if (_voiceBox is not null) _voiceBox.ButtonPressed = value; } }
    public bool MediaFieldForTesting { get => _mediaBox?.ButtonPressed ?? true; set { if (_mediaBox is not null) _mediaBox.ButtonPressed = value; } }
    public void PressSavePermissionsForTesting() => PressForTesting("Salvar Permissões");
    public Task<bool> SaveMediaForTesting(string url, double volume, bool autoplay, string name) => Do(Api.Media(IslandId, _parcel, url, volume, autoplay, name), url.Length == 0 ? "Música removida." : "Música salva.");
    public async Task<bool> AddAccessForTesting(string name, string kind, int? minutes) { var id = await FindAsync(name); return id is { } user && await Do(Api.AddAccess(IslandId, _parcel, user, kind, minutes), "Pessoa adicionada."); }
    public int ObjectRowsForTesting => _objectRows.Count;
    public void ReasonForTesting(string reason) { if (_returnReason is not null) _returnReason.Text = reason; }
    public void CheckObjectForTesting(int index, bool on) => _objectRows[index].Box.ButtonPressed = on;
    public Task<bool> ReturnSelectedForTesting() => ReturnAsync("ids");
    public Task<bool> ReturnOthersForTesting() => ReturnAsync("others");

    // ---- drawing -----------------------------------------------------------------------------------------------------------------

    private void Render()
    {
        _renderVersion++; _objectRows.Clear(); _voiceBox = null; _mediaBox = null; _nameEdit = null; _descEdit = null; _categoryDrop = null; _teleportDrop = null; _arrivalText = null; _returnReason = null;
        foreach (var child in _content.GetChildren()) child.QueueFree();
        switch (_tab)
        {
            case "geral": ShowGeneral(); break;
            case "propriedade": ShowProperty(); break;
            case "venda": ShowTrade(); break;
            case "acesso": _ = ShowAccessAsync(_renderVersion); break;
            case "permissoes": ShowPermissions(); break;
            case "objetos": _ = ShowObjectsAsync(_renderVersion); break;
            case "midia": ShowMedia(); break;
            case "contrato": ShowContract(); break;
        }
    }

    private static Control Line(string key, Control value, float keyWidth = 170)
    {
        var row = IslandUi.Row(10); var k = IslandUi.Text(key, 14, IslandUi.TextSecondary); k.CustomMinimumSize = new Vector2(keyWidth, 0); k.VerticalAlignment = VerticalAlignment.Top; row.AddChild(k); value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(value); return row;
    }

    private static Control Line(string key, string value, Color? color = null, float keyWidth = 170) => Line(key, IslandUi.Text(value, 14, color ?? IslandUi.TextPrimary, true, true), keyWidth);

    /// <summary>A rule that is on unless the server says it is off (an older server does not send it).</summary>
    private static bool Allows(JsonElement parcel, string name) => !parcel.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.False;

    private static Control Note(string text) => IslandUi.Text(text, 13, IslandUi.TextMuted, false, true);

    private (PanelContainer Card, VBoxContainer Body) Card(string title, string icon) { var (card, body) = IslandUi.Card(title, icon); card.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin; return (card, body); }

    private bool Manage => Can("canManage");
    private bool Staff => Can("isStaff");
    private JsonElement P => About.GetProperty("parcel");

    // ---- Geral -------------------------------------------------------------------------------------------------------------------

    private void ShowGeneral()
    {
        var p = P; var island = About.GetProperty("island"); var usage = About.GetProperty("usage"); var canEditText = Manage; var canEditCategory = Manage || Staff;
        var row = IslandUi.Row(14); row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _content.AddChild(row);

        var (pictureCard, pictureBody) = Card("Imagem de Divulgação", "image"); pictureCard.CustomMinimumSize = new Vector2(330, 0); pictureCard.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; row.AddChild(pictureCard);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(0, 210), ClipContents = true }; frame.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, 9, 1));
        var picture = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepCentered, Texture = UiStyle.Icon("image", IslandUi.TextMuted, 44), MouseFilter = Control.MouseFilterEnum.Ignore }; frame.AddChild(picture); pictureBody.AddChild(frame);
        ShowPicture(picture, Str(p, "imageUrl"), "");
        if (canEditCategory)
        {
            var buttons = IslandUi.Row(8); pictureBody.AddChild(buttons);
            var pick = IslandUi.Action("Escolher Imagem", "image", IslandUi.Kind.Secondary, 38); pick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; pick.Pressed += () => PickImage(asset => _ = Do(Api.Image(IslandId, _parcel, $"asset:{asset:D}"), "Imagem atualizada.")); buttons.AddChild(pick);
            var remove = IslandUi.Action("Remover", "trash", IslandUi.Kind.Danger, 38); remove.Disabled = Str(p, "imageUrl").Length == 0; remove.Pressed += () => _ = Do(Api.Image(IslandId, _parcel, ""), "Imagem removida."); buttons.AddChild(remove);
        }
        else if (Str(p, "imageUrl").Length == 0) pictureBody.AddChild(Note("Este terreno ainda não tem imagem."));

        var (card, body) = Card("Informações do Terreno", "info"); card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(card);
        _nameEdit = IslandUi.Input("Nome do terreno", 120); _nameEdit.Text = Str(p, "name"); _nameEdit.Editable = canEditText; body.AddChild(Line("Nome do Terreno:", _nameEdit));
        _descEdit = IslandUi.TextArea("Descrição", 76); _descEdit.Text = Str(p, "description"); _descEdit.Editable = canEditText; body.AddChild(Line("Descrição:", _descEdit));
        body.AddChild(Line("Área:", $"{LandMath.Area(Dbl(p, "area"))} ({Int(p, "cells")} quadrados)"));
        _categoryDrop = IslandUi.Dropdown([("Residencial", "residencial"), ("Comercial", "comercial"), ("Mista", "mista"), ("Social", "social"), ("Área Comum", "comum")]); IslandUi.Select(_categoryDrop, Str(p, "category")); _categoryDrop.Disabled = !canEditCategory; body.AddChild(Line("Categoria:", _categoryDrop));
        body.AddChild(Line("Ilha:", Str(island, "name"))); body.AddChild(Line("Proprietário:", Str(About.GetProperty("owner"), "name")));
        var maxObjects = Int(usage, "maxObjects"); var used = Int(usage, "objects"); var percent = maxObjects <= 0 ? 0 : Math.Min(100.0, used * 100.0 / maxObjects);
        body.AddChild(Line("Prims Totais:", LandMath.Number(maxObjects))); body.AddChild(Line("Prims Utilizadas:", LandMath.Number(used))); body.AddChild(Line("Prims Livres:", LandMath.Number(Math.Max(0, maxObjects - used))));
        var bar = IslandUi.Progress(percent, 14); var barRow = IslandUi.Row(10); barRow.AddChild(bar); barRow.AddChild(IslandUi.Text($"{percent:0.#}%".Replace('.', ','), 14, IslandUi.TextPrimary, true)); body.AddChild(Line("", barRow));
        // arrival
        var arrival = IslandUi.Row(8); var hasPoint = p.TryGetProperty("spawnX", out var sx) && sx.ValueKind == JsonValueKind.Number;
        _arrivalText = IslandUi.Text(hasPoint ? $"x {Dbl(p, "spawnX"):0.#}, z {Dbl(p, "spawnZ"):0.#}, virado para {Dbl(p, "yaw"):0}°" : "No meio do terreno", 14, IslandUi.TextPrimary, true, true); _arrivalText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; arrival.AddChild(_arrivalText);
        if (Manage) { var here = IslandUi.Action("Usar Minha Posição", "pin", IslandUi.Kind.Secondary, 34); here.Pressed += UseMyPosition; arrival.AddChild(here); }
        body.AddChild(Line("Ponto de Chegada:", arrival));
        _teleportDrop = IslandUi.Dropdown([("Livre: cada um escolhe o ponto", "free"), ("Forçado: sempre no ponto de chegada", "forced"), ("Desativado: ninguém teleporta para cá", "disabled")]); IslandUi.Select(_teleportDrop, Str(p, "teleportMode")); _teleportDrop.Disabled = !Manage; body.AddChild(Line("Teleporte:", _teleportDrop));
        if (canEditText || canEditCategory)
        {
            var save = IslandUi.Action("Salvar Alterações", "ok", IslandUi.Kind.Primary, 42); save.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; save.Pressed += () => _ = SaveGeneralAsync(); body.AddChild(save);
            if (!canEditText) body.AddChild(Note("Este terreno é de outra pessoa: você só muda a finalidade e a imagem."));
        }
        else body.AddChild(Note("Só o dono do terreno muda estas informações."));
    }

    private void UseMyPosition()
    {
        if (Position?.Invoke() is not { } where) { Notify("Não sei onde você está agora."); return; }
        _pendingArrival = where; if (_arrivalText is not null) _arrivalText.Text = $"x {where.X:0.#}, z {where.Z:0.#}, virado para {where.Yaw:0}° (falta salvar)";
    }

    private async Task<bool> SaveGeneralAsync()
    {
        var p = P; var changed = false;
        if (Manage && _nameEdit is not null && _descEdit is not null && (_nameEdit.Text.Trim() != Str(p, "name") || _descEdit.Text.Trim() != Str(p, "description")))
        {
            if (_nameEdit.Text.Trim().Length == 0) { Notify("Dê um nome ao terreno."); return false; }
            var reply = await Api.Rename(IslandId, _parcel, _nameEdit.Text.Trim(), _descEdit.Text.Trim(), Int(p, "revision"));
            if (!reply.Ok) { Notify(reply.Status == 409 ? "O terreno mudou enquanto você editava. Atualizei; tente de novo." : LandManagementApi.Words(reply)); await LoadAsync(); return false; }
            changed = true; p = (await RefreshParcelAsync()) ?? p;
        }
        if ((Manage || Staff) && _categoryDrop is not null && IslandUi.IdOf(_categoryDrop) != Str(P, "category")) { var reply = await Api.Category(IslandId, _parcel, IslandUi.IdOf(_categoryDrop)); if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); return false; } changed = true; }
        if (Manage && _teleportDrop is not null && (_pendingArrival is not null || IslandUi.IdOf(_teleportDrop) != Str(P, "teleportMode")))
        {
            var x = _pendingArrival?.X ?? (P.TryGetProperty("spawnX", out var sx) && sx.ValueKind == JsonValueKind.Number ? sx.GetDouble() : (double?)null); var z = _pendingArrival?.Z ?? (P.TryGetProperty("spawnZ", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetDouble() : (double?)null);
            var reply = await Api.Arrival(IslandId, _parcel, x, z, _pendingArrival?.Yaw ?? Dbl(P, "yaw"), IslandUi.IdOf(_teleportDrop)); if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); return false; } changed = true; _pendingArrival = null;
        }
        Notify(changed ? "Alterações salvas." : "Nada mudou."); if (changed) await LoadAsync(); return true;
    }

    private async Task<JsonElement?> RefreshParcelAsync() { var reply = await Api.About(IslandId, _parcel); if (!reply.Ok) return null; _about = reply.Body.Clone(); return _about.GetProperty("parcel"); }

    // ---- Propriedade -------------------------------------------------------------------------------------------------------------

    private void ShowProperty()
    {
        var owner = About.GetProperty("owner"); var rights = About.GetProperty("rights"); var kind = Str(owner, "kind");
        var (card, body) = Card("Propriedade", "shield"); _content.AddChild(card);
        body.AddChild(Line("Proprietário:", Str(owner, "name"))); body.AddChild(Line("Tipo de Titularidade:", kind == "group" ? "Grupo" : "Pessoa"));
        body.AddChild(Line("Grupo:", kind == "group" ? Str(owner, "groupName") : "Nenhum")); body.AddChild(Line("Data de Aquisição:", Date(About, "acquiredAt").ToLocalTime().ToString("dd/MM/yyyy", Pt)));
        var hasRental = Obj(About, "rental") is not null;
        body.AddChild(Line("Direitos de Revenda:", Flag(rights, "canResell") ? "Sim: o dono pode vender" : kind == "group" ? "Não: terreno de grupo" : hasRental ? "Suspensos enquanto está alugado" : "Só o dono vende", Flag(rights, "canResell") ? IslandUi.Success : IslandUi.TextSecondary));
        body.AddChild(Line("Direitos de Locação:", Flag(rights, "canLease") ? "Sim: o dono pode alugar" : kind == "group" ? "Não: terreno de grupo" : hasRental ? "Em uso: já está alugado" : "Só o dono aluga", Flag(rights, "canLease") ? IslandUi.Success : IslandUi.TextSecondary));
        var (hCard, hBody) = Card("Histórico de Transferência", "history"); _content.AddChild(hCard);
        var history = About.GetProperty("history").EnumerateArray().ToList();
        if (history.Count == 0) hBody.AddChild(Note("Nenhuma transferência registrada."));
        foreach (var h in history)
        {
            var what = Str(h, "what") switch { "sale" => "Venda", "claim" => "Terreno criado", "island_transfer" => "Veio com a ilha", "split_from" => "Criado por divisão", "land_transfer" => "Transferência", var other => other };
            var from = Str(h, "from"); var to = Str(h, "to"); var amount = Long(h, "amount");
            hBody.AddChild(Line(Date(h, "at").ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt), $"{what}{(from.Length > 0 ? $" · de {from}" : "")} · para {to}{(amount > 0 ? $" · {LandMath.Money(amount)}" : "")}", null, 150));
        }
    }

    // ---- Venda e Aluguel --------------------------------------------------------------------------------------------------------

    /// <summary>Giving the parcel to another person: the offer waits for their answer. The owner offers and takes it back; the person offered to accepts or turns it down.</summary>
    private void ShowGiftCard(JsonElement rights)
    {
        var pending = Obj(About, "transfer"); var forMe = pending is { } p0 && Flag(p0, "forMe");
        if (pending is null && !(Manage && Flag(rights, "canTransfer"))) return;
        var (card, body) = Card("Presente", "link"); _content.AddChild(card);
        if (pending is { } wait)
        {
            body.AddChild(Line("Transferência:", forMe ? $"{Str(About.GetProperty("owner"), "name")} quer te dar este terreno, até {Date(wait, "expiresAt").ToLocalTime():dd/MM/yyyy HH:mm}" : $"Oferecido de presente a {Str(wait, "to")}, até {Date(wait, "expiresAt").ToLocalTime():dd/MM/yyyy HH:mm}", IslandUi.AccentSecondary));
            var row = IslandUi.Row(8);
            if (forMe)
            {
                var accept = IslandUi.Action("Aceitar Terreno", "link", IslandUi.Kind.Primary, 38); accept.Pressed += () => Ask("Aceitar terreno", IslandUi.Text("O terreno e os objetos do dono que estão nele passam a ser seus agora.", 14, IslandUi.TextSecondary, false, true), async () => await Do(Api.AcceptTransfer(IslandId, _parcel), "O terreno agora é seu."), "Aceitar"); row.AddChild(accept);
                var decline = IslandUi.Action("Recusar", null, IslandUi.Kind.Danger, 38); decline.Pressed += () => _ = Do(Api.DeclineTransfer(IslandId, _parcel), "Você recusou o terreno."); row.AddChild(decline);
            }
            else if (Manage && Flag(About.GetProperty("me"), "isOwner")) { var cancel = IslandUi.Action("Cancelar Transferência", null, IslandUi.Kind.Danger, 38); cancel.Pressed += () => _ = Do(Api.CancelTransfer(IslandId, _parcel), "Transferência cancelada."); row.AddChild(cancel); }
            body.AddChild(row); body.AddChild(Note("Enquanto a pessoa não responde, o terreno continua com o dono e não pode ser anunciado nem mudar de forma."));
            return;
        }
        var give = IslandUi.Action("Transferir Terreno", "link", IslandUi.Kind.Secondary, 40); give.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; give.Pressed += OpenGive; body.AddChild(give);
        body.AddChild(Note("É um presente: o terreno e os seus objetos nele passam a outra pessoa, se ela aceitar em até 7 dias."));
    }

    private void OpenGive()
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("O terreno, com os objetos seus que estão nele, é oferecido a outra pessoa, de graça. Nada muda até ela aceitar; você pode cancelar antes.", 14, IslandUi.TextSecondary, false, true));
        var name = IslandUi.Input("Nome da pessoa", 60); content.AddChild(name);
        Ask("Transferir terreno", content, async () => await Do(Api.Transfer(IslandId, _parcel, name.Text.Trim()), "Oferta de transferência enviada."), "Oferecer");
    }

    public void OpenSaleForTesting() => OpenSale(1000, true);
    public void OpenRentForTesting() => OpenRent();
    public void OpenGiveForTesting() => OpenGive();
    public System.Collections.Generic.IEnumerable<ConfirmationDialog> DialogsForTesting => _panel.GetChildren().OfType<ConfirmationDialog>();
    public Task<bool> GiveForTesting(string name) => Do(Api.Transfer(IslandId, _parcel, name), "Oferta de transferência enviada.");
    public Task<bool> CancelGiveForTesting() => Do(Api.CancelTransfer(IslandId, _parcel), "Transferência cancelada.");
    public Task<bool> AcceptGiveForTesting() => Do(Api.AcceptTransfer(IslandId, _parcel), "O terreno agora é seu.");
    public Task<bool> DeclineGiveForTesting() => Do(Api.DeclineTransfer(IslandId, _parcel), "Você recusou o terreno.");

    private void ShowTrade()
    {
        var sale = Obj(About, "sale"); var offer = Obj(About, "rentOffer"); var rental = Obj(About, "rental"); var rights = About.GetProperty("rights");
        if (Manage)
        {
            var (sCard, sBody) = Card("Venda", "coins"); _content.AddChild(sCard);
            if (rental is not null) sBody.AddChild(Note("O terreno está alugado: a venda espera o fim do contrato."));
            else if (sale is { } s)
            {
                sBody.AddChild(Line("À venda por:", LandMath.Money(Long(s, "price")), IslandUi.AccentSecondary)); sBody.AddChild(Line("Objetos incluídos:", Flag(s, "includeObjects") ? "Sim" : "Não")); if (Flag(s, "exclusive")) sBody.AddChild(Line("Comprador:", "Exclusivo para uma pessoa"));
                var row = IslandUi.Row(8); var edit = IslandUi.Action("Editar Preço", null, IslandUi.Kind.Secondary, 38); edit.Pressed += () => OpenSale(Long(s, "price"), Flag(s, "includeObjects")); row.AddChild(edit);
                var withdraw = IslandUi.Action("Retirar da Venda", null, IslandUi.Kind.Danger, 38); withdraw.Pressed += () => _ = Do(Api.CancelSale(IslandId, _parcel), "Venda retirada."); row.AddChild(withdraw); sBody.AddChild(row);
            }
            else
            {
                var sell = IslandUi.Action("Colocar à Venda", "coins", IslandUi.Kind.Orange, 40); sell.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; sell.Disabled = !Flag(rights, "canResell") || offer is not null; sell.Pressed += () => OpenSale(1000, true);
                sell.TooltipText = offer is not null ? "Retire a oferta de aluguel antes." : !Flag(rights, "canResell") ? "Este terreno não pode ser vendido agora." : ""; sBody.AddChild(sell);
            }
            var (rCard, rBody) = Card("Aluguel", "calendar"); _content.AddChild(rCard);
            if (rental is { } r) { rBody.AddChild(Line("Alugado para:", Str(r, "tenant"), LandTerritoryMap.Rented)); rBody.AddChild(Line("Vence em:", Date(r, "expiresAt").ToLocalTime().ToString("dd/MM/yyyy", Pt))); rBody.AddChild(Note("Os detalhes estão na aba Contrato.")); }
            else if (offer is { } o)
            {
                rBody.AddChild(Line("Valor:", $"{LandMath.Money(Long(o, "price"))} a cada {Int(o, "periodDays")} dias", LandTerritoryMap.Rented)); rBody.AddChild(Line("Períodos:", $"de {Int(o, "minPeriods")} a {Int(o, "maxPeriods")}")); rBody.AddChild(Line("Caução:", LandMath.Money(Long(o, "deposit"))));
                var withdraw = IslandUi.Action("Retirar Oferta", null, IslandUi.Kind.Danger, 38); withdraw.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; withdraw.Pressed += () => _ = Do(Api.CancelRent(IslandId, _parcel), "Oferta de aluguel retirada."); rBody.AddChild(withdraw);
            }
            else
            {
                var rent = IslandUi.Action("Habilitar Aluguel", "calendar", IslandUi.Kind.Purple, 40); rent.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; rent.Disabled = !Flag(rights, "canLease") || sale is not null; rent.Pressed += OpenRent;
                rent.TooltipText = sale is not null ? "Retire a venda antes." : !Flag(rights, "canLease") ? "Este terreno não pode ser alugado agora." : ""; rBody.AddChild(rent);
            }
            ShowGiftCard(rights);
            return;
        }
        ShowGiftCard(rights);
        var (card, body) = Card("Condições", "coins"); _content.AddChild(card);
        if (sale is { } buy)
        {
            body.AddChild(Line("À venda por:", LandMath.Money(Long(buy, "price")), IslandUi.AccentSecondary)); body.AddChild(Line("Vendedor:", Str(About.GetProperty("owner"), "name")));
            var row = IslandUi.Row(8); var go = IslandUi.Action("Comprar", "coins", IslandUi.Kind.Primary, 40); go.Pressed += () => OpenBuy(buy); row.AddChild(go); var terms = IslandUi.Action("Ver Condições", null, IslandUi.Kind.Secondary, 40); terms.Pressed += ShowTerms; row.AddChild(terms); body.AddChild(row);
        }
        if (offer is { } rentOffer)
        {
            body.AddChild(Line("Para alugar:", $"{LandMath.Money(Long(rentOffer, "price"))} a cada {Int(rentOffer, "periodDays")} dias", LandTerritoryMap.Rented));
            var row = IslandUi.Row(8); var go = IslandUi.Action("Alugar", "calendar", IslandUi.Kind.Purple, 40); go.Pressed += () => OpenRentAsTenant(rentOffer); row.AddChild(go); var terms = IslandUi.Action("Ver Condições", null, IslandUi.Kind.Secondary, 40); terms.Pressed += ShowTerms; row.AddChild(terms); body.AddChild(row);
        }
        if (sale is null && offer is null) body.AddChild(Note(rental is not null ? "Este terreno está alugado no momento." : "Este terreno não está à venda nem para alugar."));
        if (rental is { } mine && Can("isTenant")) body.AddChild(Note("Você aluga este terreno. Veja a aba Contrato para renovar ou sair."));
    }

    private void ShowTerms()
    {
        var sale = Obj(About, "sale"); var offer = Obj(About, "rentOffer"); var lines = new List<string>();
        if (sale is { } s) { lines.Add($"VENDA — {LandMath.Money(Long(s, "price"))}"); lines.Add(Flag(s, "includeObjects") ? "Os objetos do vendedor neste terreno vão junto." : "Os objetos do vendedor não vão junto: voltam para o inventário dele."); if (Str(s, "description").Length > 0) lines.Add(Str(s, "description")); }
        if (offer is { } o) { if (lines.Count > 0) lines.Add(""); lines.Add($"ALUGUEL — {LandMath.Money(Long(o, "price"))} a cada {Int(o, "periodDays")} dias"); lines.Add($"De {Int(o, "minPeriods")} a {Int(o, "maxPeriods")} períodos. Caução de {LandMath.Money(Long(o, "deposit"))}, devolvida no fim."); lines.Add($"Tolerância de {Int(o, "graceDays")} dia(s) depois do vencimento."); lines.Add("Quem aluga pode construir enquanto o contrato valer; os objetos dele voltam ao inventário dele no fim."); }
        var dialog = new AcceptDialog { Title = "Condições", OkButtonText = "Fechar", DialogText = string.Join("\n", lines), Theme = ViewerTheme.Create(), Unresizable = true }; dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree(); _panel.AddChild(dialog); IslandUi.Fit(dialog, 520);
    }

    private void OpenSale(long price, bool objects)
    {
        var content = IslandUi.Column(8); content.AddChild(Note("O terreno vai à venda para todos."));
        var value = IslandUi.Number(1, 1_000_000_000, 1, " L$"); value.Value = price; content.AddChild(value);
        var include = new CheckBox { Text = "Incluir os meus objetos na venda", ButtonPressed = objects, FocusMode = Control.FocusModeEnum.None }; content.AddChild(include);
        var description = IslandUi.Input("Descrição do anúncio (opcional)", 200); content.AddChild(description);
        Ask("Colocar à venda", content, async () => { if (Obj(About, "sale") is not null) await Api.CancelSale(IslandId, _parcel); return await Do(Api.OfferSale(IslandId, _parcel, (long)value.Value, include.ButtonPressed, description.Text.Trim()), "Terreno anunciado à venda."); }, "Anunciar");
    }

    private void OpenRent()
    {
        var content = IslandUi.Column(8); content.AddChild(Note("Quem alugar pode construir durante o contrato; o terreno continua seu."));
        var price = IslandUi.Number(1, 1_000_000_000, 1, " L$ por período"); price.Value = 500; content.AddChild(price);
        var period = IslandUi.Dropdown([("7 dias", "7"), ("14 dias", "14"), ("30 dias", "30")]); IslandUi.Select(period, "30"); content.AddChild(period);
        var min = IslandUi.Number(1, 52, 1, " período(s) no mínimo"); min.Value = 1; content.AddChild(min);
        var max = IslandUi.Number(1, 52, 1, " período(s) no máximo"); max.Value = 6; content.AddChild(max);
        var deposit = IslandUi.Number(0, 1_000_000_000, 1, " L$ de caução"); content.AddChild(deposit);
        var grace = IslandUi.Number(0, 30, 1, " dia(s) de tolerância"); grace.Value = 3; content.AddChild(grace);
        Ask("Habilitar aluguel", content, async () => await Do(Api.OfferRent(IslandId, _parcel, (long)price.Value, int.Parse(IslandUi.IdOf(period)), (int)min.Value, (int)max.Value, (long)deposit.Value, (int)grace.Value), "Terreno oferecido para aluguel."), "Anunciar");
    }

    private void OpenBuy(JsonElement sale)
    {
        var price = Long(sale, "price"); var content = IslandUi.Column(6);
        content.AddChild(IslandUi.Text(_title.Text, 17, IslandUi.TextPrimary, true)); content.AddChild(IslandUi.Text($"{_subtitle.Text} · vendedor: {Str(About.GetProperty("owner"), "name")}", 14, IslandUi.TextSecondary, false, true));
        content.AddChild(IslandUi.Text($"Preço: {LandMath.Money(price)}", 18, IslandUi.AccentSecondary, true)); content.AddChild(Note("O valor sai da sua carteira agora e o terreno passa a ser seu."));
        Ask("Comprar terreno", content, async () => await Do(Api.Buy(IslandId, _parcel, price), "Terreno comprado."), "Comprar");
    }

    private void OpenRentAsTenant(JsonElement offer)
    {
        var price = Long(offer, "price"); var min = Int(offer, "minPeriods"); var max = Math.Max(min, Int(offer, "maxPeriods")); var deposit = Long(offer, "deposit"); var content = IslandUi.Column(6);
        content.AddChild(IslandUi.Text(_title.Text, 17, IslandUi.TextPrimary, true));
        var periods = IslandUi.Number(min, max, 1, $" período(s) de {Int(offer, "periodDays")} dias"); periods.Value = min; content.AddChild(periods);
        var total = IslandUi.Text("", 16, LandTerritoryMap.Rented, true); content.AddChild(total); void Update() => total.Text = $"Total agora: {LandMath.Money(price * (long)periods.Value)}" + (deposit > 0 ? $" + caução {LandMath.Money(deposit)} (volta no fim)" : ""); periods.ValueChanged += v => Update(); Update();
        var auto = new CheckBox { Text = "Renovar sozinho a cada vencimento (cobra um período da minha carteira)", FocusMode = Control.FocusModeEnum.None, Visible = Flag(offer, "autoRenewAllowed") }; content.AddChild(auto);
        Ask("Alugar terreno", content, async () => await Do(Api.Rent(IslandId, _parcel, (int)periods.Value, price, auto.Visible && auto.ButtonPressed), "Terreno alugado."), "Alugar");
    }

    // ---- Acesso ---------------------------------------------------------------------------------------------------------------------

    private async Task<Guid?> FindAsync(string name)
    {
        var reply = await Api.FindPerson(name); if (!reply.Ok || reply.Body.ValueKind != JsonValueKind.Array) { Notify("Não foi possível procurar essa pessoa."); return null; }
        var matches = reply.Body.EnumerateArray().Where(x => string.Equals(Str(x, "displayName"), name, StringComparison.OrdinalIgnoreCase) || string.Equals(Str(x, "username"), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) { Notify($"Não encontrei “{name}” entre as pessoas cadastradas."); return null; }
        if (matches.Count > 1) { Notify($"Há mais de uma pessoa chamada “{name}”."); return null; }
        return Guid.TryParse(Str(matches[0], "id"), out var id) ? id : null;
    }

    private async Task ShowAccessAsync(int version)
    {
        if (!Manage) { _content.AddChild(Note("O acesso é do dono do terreno.")); var read = Card("Acesso", "lock"); _content.AddChild(read.Card); read.Body.AddChild(Line("Quem entra:", Str(P, "accessMode") switch { "list" => "Só quem está na lista", "private" => "Só o dono e o locatário", _ => "Todos" })); return; }
        var reply = await Api.Access(IslandId, _parcel); if (version != _renderVersion) return;
        if (!reply.Ok) { _content.AddChild(Note(LandManagementApi.Words(reply))); return; }
        var (card, body) = Card("Modo de Acesso", "lock"); _content.AddChild(card);
        var mode = IslandUi.Dropdown([("Público: todos entram", "public"), ("Só a lista: o dono, o locatário e quem está na lista", "list"), ("Privado: só o dono e o locatário", "private")]); IslandUi.Select(mode, Str(reply.Body, "mode")); mode.ItemSelected += index => _ = Do(Api.AccessMode(IslandId, _parcel, IslandUi.IdOf(mode)), "Acesso atualizado."); body.AddChild(mode);
        var (listCard, listBody) = Card("Lista de Pessoas", "people"); _content.AddChild(listCard);
        var entries = reply.Body.TryGetProperty("entries", out var e) && e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToList() : [];
        if (entries.Count == 0) listBody.AddChild(Note("Ninguém na lista."));
        foreach (var entry in entries)
        {
            var row = IslandUi.Row(8); var kind = Str(entry, "kind"); var until = Date(entry, "expiresAt") is { } at && at != default ? $" até {at.ToLocalTime():dd/MM HH:mm}" : "";
            var label = IslandUi.Text($"{Str(entry, "userName")} · {(kind == "ban" ? "banido" : kind == "build" ? "pode construir" : "permitido")}{until}", 14, kind == "ban" ? IslandUi.Error : IslandUi.TextPrimary); label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(label);
            var id = Guid.Parse(Str(entry, "id")); var remove = IslandUi.Action("Remover", null, IslandUi.Kind.Danger, 32); remove.Pressed += () => _ = Do(Api.RemoveAccess(IslandId, _parcel, id), "Removido da lista."); row.AddChild(remove); listBody.AddChild(row);
        }
        var (addCard, addBody) = Card("Adicionar à Lista", "plus"); _content.AddChild(addCard);
        var name = IslandUi.Input("Nome da pessoa", 60); addBody.AddChild(name);
        var kindDrop = IslandUi.Dropdown([("Permitir entrar", "allow"), ("Banir", "ban"), ("Permitir construir", "build")]); addBody.AddChild(kindDrop);
        var duration = IslandUi.Dropdown([("Para sempre", "0"), ("15 minutos", "15"), ("1 hora", "60"), ("24 horas", "1440"), ("7 dias", "10080")]); addBody.AddChild(duration);
        var add = IslandUi.Action("Adicionar", "plus", IslandUi.Kind.Primary, 40); add.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        add.Pressed += async () => { if (name.Text.Trim().Length == 0) { Notify("Digite o nome da pessoa."); return; } var found = await FindAsync(name.Text.Trim()); if (found is not { } user) return; var minutes = int.Parse(IslandUi.IdOf(duration)); await Do(Api.AddAccess(IslandId, _parcel, user, IslandUi.IdOf(kindDrop), minutes == 0 ? null : minutes), "Pessoa adicionada."); };
        addBody.AddChild(add);
    }

    // ---- Permissões -------------------------------------------------------------------------------------------------------------------

    private void ShowPermissions()
    {
        var p = P; var (card, body) = Card("Permissões do Terreno", "shield"); _content.AddChild(card); var builders = Int(About.GetProperty("usage"), "builders");
        var buildIndex = Math.Max(0, Array.IndexOf(BuildModes, Str(p, "buildMode"))); var minutes = p.TryGetProperty("autoReturnMinutes", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : (int?)null;
        if (!Manage)
        {
            body.AddChild(Line("Quem constrói:", BuildModeTexts[buildIndex])); body.AddChild(Line("Executar scripts:", Flag(p, "allowScripts") ? "Permitido" : "Não")); body.AddChild(Line("Devolução automática:", minutes is { } mm ? $"depois de {mm} minutos" : "Desligada")); body.AddChild(Line("Voz:", Allows(p, "allowVoice") ? "Permitida" : "Desligada neste terreno", Allows(p, "allowVoice") ? IslandUi.TextPrimary : IslandUi.Warning)); body.AddChild(Line("Mídia nos objetos de outros:", Allows(p, "allowMedia") ? "Permitida" : "Não permitida", Allows(p, "allowMedia") ? IslandUi.TextPrimary : IslandUi.Warning)); body.AddChild(Note("Só o dono do terreno muda as permissões.")); return;
        }
        var build = IslandUi.Dropdown(BuildModes.Select((id, i) => (BuildModeTexts[i], id))); build.Select(buildIndex); body.AddChild(Line("Quem Constrói:", build));
        var scripts = new CheckBox { Text = "Permitir executar scripts", ButtonPressed = Flag(p, "allowScripts"), FocusMode = Control.FocusModeEnum.None }; body.AddChild(Line("Scripts:", scripts));
        var auto = IslandUi.Dropdown(AutoReturns.Select((v, i) => (v is null ? "Desligada" : $"Depois de {v} minutos", i.ToString(Pt)))); auto.Select(Math.Max(0, Array.FindIndex(AutoReturns, v => v == minutes))); body.AddChild(Line("Devolver Objetos de Quem Não Pode Construir:", auto));
        _voiceBox = new CheckBox { Text = "Permitir voz neste terreno", ButtonPressed = Allows(p, "allowVoice"), FocusMode = Control.FocusModeEnum.None, TooltipText = "Desligada, ninguém fala nem ouve voz de perto dentro do terreno." }; body.AddChild(Line("Voz:", _voiceBox));
        _mediaBox = new CheckBox { Text = "Permitir mídia nos objetos de outras pessoas", ButtonPressed = Allows(p, "allowMedia"), FocusMode = Control.FocusModeEnum.None, TooltipText = "Desligada, quem só constrói aqui não põe imagem ou vídeo em superfícies. O que já estava lá continua." }; body.AddChild(Line("Mídia:", _mediaBox));
        body.AddChild(Note($"Construtores na lista: {builders}. Quem entra e quem constrói por pessoa se define na aba Acesso."));
        var save = IslandUi.Action("Salvar Permissões", "ok", IslandUi.Kind.Primary, 42); save.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        save.Pressed += () => _ = Do(Api.Options(IslandId, _parcel, IslandUi.IdOf(build), scripts.ButtonPressed, AutoReturns[Math.Clamp(auto.Selected, 0, AutoReturns.Length - 1)], _voiceBox!.ButtonPressed, _mediaBox!.ButtonPressed), "Permissões atualizadas."); body.AddChild(save);
    }

    // ---- Objetos ----------------------------------------------------------------------------------------------------------------------

    private async Task ShowObjectsAsync(int version)
    {
        var usage = About.GetProperty("usage"); var (head, headBody) = Card("Uso de Prims", "cube"); _content.AddChild(head);
        var maxObjects = Int(usage, "maxObjects"); var used = Int(usage, "objects"); headBody.AddChild(Line("Prims:", $"{LandMath.Number(used)} de {LandMath.Number(maxObjects)}")); headBody.AddChild(IslandUi.Progress(maxObjects <= 0 ? 0 : Math.Min(100.0, used * 100.0 / maxObjects), 14));
        headBody.AddChild(Line("De outras pessoas:", LandMath.Number(Int(usage, "others"))));
        if (!Manage && !Staff) { _content.AddChild(Note("A lista de objetos aparece para o dono do terreno.")); return; }
        var reply = await Api.Objects(IslandId, _parcel); if (version != _renderVersion) return;
        if (!reply.Ok) { _content.AddChild(Note(LandManagementApi.Words(reply))); return; }
        var (card, body) = Card("Objetos no Terreno", "list"); _content.AddChild(card);
        var items = reply.Body.ValueKind == JsonValueKind.Array ? reply.Body.EnumerateArray().ToList() : [];
        if (items.Count == 0) body.AddChild(Note("Nenhum objeto neste terreno."));
        var head2 = IslandUi.Row(8); foreach (var (text, ratio) in new[] { ("", .3f), ("Objeto", 1.4f), ("Dono", 1f), ("Prims", .6f), ("Scripts", .7f) }) { var l = IslandUi.Text(text, 13, IslandUi.TextSecondary, true); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.SizeFlagsStretchRatio = ratio; head2.AddChild(l); }
        if (items.Count > 0) body.AddChild(head2);
        foreach (var item in items)
        {
            var row = IslandUi.Row(8); var box = new CheckBox { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = .3f }; row.AddChild(box);
            var authorized = Flag(item, "authorized"); Label Cell(string t, float r, Color? c = null) { var l = IslandUi.Text(t, 14, c ?? IslandUi.TextPrimary); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.SizeFlagsStretchRatio = r; l.ClipText = true; return l; }
            row.AddChild(Cell(Str(item, "name"), 1.4f)); row.AddChild(Cell(Str(item, "ownerName"), 1f, authorized ? null : IslandUi.Warning)); row.AddChild(Cell(Int(item, "cost").ToString(Pt), .6f)); row.AddChild(Cell(Flag(item, "hasScripts") ? "Sim" : "Não", .7f)); body.AddChild(row);
            _objectRows.Add((Guid.Parse(Str(item, "id")), box, authorized));
        }
        if (items.Count == 0) return;
        _returnReason = IslandUi.Input("Motivo da devolução (obrigatório)", 200); body.AddChild(_returnReason);
        var buttons = IslandUi.Row(8); body.AddChild(buttons);
        var selected = IslandUi.Action("Devolver Selecionados", null, IslandUi.Kind.Secondary, 38); selected.Pressed += () => _ = ReturnAsync("ids"); buttons.AddChild(selected);
        var others = IslandUi.Action("Devolver Não Autorizados", null, IslandUi.Kind.Secondary, 38); others.Pressed += () => _ = ReturnAsync("others"); buttons.AddChild(others);
        body.AddChild(Note("Devolver manda o objeto de volta ao inventário de quem o fez, como construção salva. Nada é apagado de vez."));
    }

    private async Task<bool> ReturnAsync(string scope)
    {
        var reason = _returnReason?.Text.Trim() ?? ""; var ids = _objectRows.Where(r => r.Box.ButtonPressed).Select(r => r.Id).ToList();
        if (reason.Length == 0) { Notify("Conte o motivo da devolução."); return false; }
        if (scope == "ids" && ids.Count == 0) { Notify("Marque os objetos que quer devolver."); return false; }
        return await Do(Api.ReturnObjects(IslandId, _parcel, scope, scope == "ids" ? ids : null, reason), "Objetos devolvidos.");
    }

    // ---- Mídia -----------------------------------------------------------------------------------------------------------------------

    private void ShowMedia()
    {
        var media = Obj(P, "media"); var (card, body) = Card("Música do Terreno", "play"); _content.AddChild(card);
        if (!Manage)
        {
            if (media is { } m) { body.AddChild(Line("Música:", Str(m, "name").Length > 0 ? Str(m, "name") : Str(m, "musicUrl"))); body.AddChild(Line("Volume:", $"{Dbl(m, "volume") * 100:0}%")); body.AddChild(Line("Toca sozinha:", Flag(m, "autoplay") ? "Sim" : "Não")); }
            else body.AddChild(Note("Este terreno não toca música.")); body.AddChild(Note("Só o dono do terreno muda a mídia.")); return;
        }
        var url = IslandUi.Input("Endereço da música (https://…mp3 ou .ogg)", 512); url.Text = media is { } a ? Str(a, "musicUrl") : ""; body.AddChild(Line("Endereço:", url));
        var name = IslandUi.Input("Nome (opcional)", 80); name.Text = media is { } b ? Str(b, "name") : ""; body.AddChild(Line("Nome:", name));
        var volume = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = media is { } c ? Dbl(c, "volume") * 100 : 60, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; body.AddChild(Line("Volume:", volume));
        var autoplay = new CheckBox { Text = "Tocar sozinha para quem entra", ButtonPressed = media is not { } d || Flag(d, "autoplay") || !((JsonElement)d).TryGetProperty("autoplay", out _), FocusMode = Control.FocusModeEnum.None }; body.AddChild(Line("Reprodução:", autoplay));
        var row = IslandUi.Row(8); body.AddChild(row);
        var save = IslandUi.Action("Salvar Música", "ok", IslandUi.Kind.Primary, 40); save.Pressed += () => { if (url.Text.Trim().Length == 0) { Notify("Cole o endereço da música ou use Remover."); return; } _ = Do(Api.Media(IslandId, _parcel, url.Text.Trim(), volume.Value / 100, autoplay.ButtonPressed, name.Text.Trim()), "Música salva."); }; row.AddChild(save);
        var remove = IslandUi.Action("Remover Música", "trash", IslandUi.Kind.Danger, 40); remove.Disabled = media is null; remove.Pressed += () => _ = Do(Api.Media(IslandId, _parcel, "", .6, true, ""), "Música removida."); row.AddChild(remove);
        body.AddChild(Note("O endereço precisa ser HTTPS e terminar em .mp3 ou .ogg. A música toca só para quem está dentro do terreno."));
    }

    // ---- Contrato --------------------------------------------------------------------------------------------------------------------

    private void ShowContract()
    {
        var rental = Obj(About, "rental"); var sale = Obj(About, "sale"); var offer = Obj(About, "rentOffer");
        if (rental is { } r)
        {
            var (card, body) = Card("Contrato de Aluguel", "doc"); _content.AddChild(card); var status = Str(r, "status"); var expires = Date(r, "expiresAt");
            body.AddChild(Line("Locador:", Str(r, "landlord"))); body.AddChild(Line("Locatário:", Str(r, "tenant"))); body.AddChild(Line("Valor:", LandMath.Money(Long(r, "price")), LandTerritoryMap.Rented)); body.AddChild(Line("Periodicidade:", $"a cada {Int(r, "periodDays")} dias"));
            body.AddChild(Line("Início:", Date(r, "startsAt").ToLocalTime().ToString("dd/MM/yyyy", Pt))); body.AddChild(Line("Vencimento:", expires.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt)));
            body.AddChild(Line("Situação:", status == "grace" ? "Em tolerância (pague para voltar a construir)" : "Em dia", status == "grace" ? IslandUi.Warning : IslandUi.Success)); body.AddChild(Line("Caução:", LandMath.Money(Long(r, "deposit")))); body.AddChild(Line("Renovação:", Flag(r, "autoRenew") ? "Automática" : "Manual"));
            var row = IslandUi.Row(8); body.AddChild(row);
            if (Can("isTenant"))
            {
                var renew = IslandUi.Action("Renovar", "calendar", IslandUi.Kind.Purple, 40); renew.Pressed += () => OpenRenew(r); row.AddChild(renew);
                var leave = IslandUi.Action("Sair do Aluguel", null, IslandUi.Kind.Danger, 40); leave.Pressed += () => Ask("Sair do aluguel", IslandUi.Text("Os seus objetos neste terreno voltam para o seu inventário e a caução volta para você.", 14, IslandUi.TextSecondary, false, true), async () => await Do(Api.End(IslandId, Guid.Parse(Str(r, "id"))), "Você saiu do aluguel."), "Sair"); row.AddChild(leave);
            }
            else if (Manage)
            {
                var end = IslandUi.Action("Encerrar Contrato", null, IslandUi.Kind.Danger, 40); end.Disabled = expires > DateTimeOffset.UtcNow; end.TooltipText = "O dono só encerra depois do vencimento.";
                end.Pressed += () => Ask("Encerrar aluguel", IslandUi.Text("Os objetos do locatário voltam para o inventário dele e a caução volta para ele.", 14, IslandUi.TextSecondary, false, true), async () => await Do(Api.End(IslandId, Guid.Parse(Str(r, "id"))), "Aluguel encerrado."), "Encerrar"); row.AddChild(end);
            }
            return;
        }
        var (c, b) = Card(sale is not null || offer is not null ? "Condições da Oferta" : "Contrato", "doc"); _content.AddChild(c);
        if (sale is { } s) { b.AddChild(Line("Venda:", LandMath.Money(Long(s, "price")), IslandUi.AccentSecondary)); b.AddChild(Line("Objetos do vendedor:", Flag(s, "includeObjects") ? "Vão junto" : "Voltam para o vendedor")); if (Str(s, "description").Length > 0) b.AddChild(Line("Descrição:", Str(s, "description"))); }
        if (offer is { } o) { b.AddChild(Line("Aluguel:", $"{LandMath.Money(Long(o, "price"))} a cada {Int(o, "periodDays")} dias", LandTerritoryMap.Rented)); b.AddChild(Line("Períodos:", $"de {Int(o, "minPeriods")} a {Int(o, "maxPeriods")}")); b.AddChild(Line("Caução:", LandMath.Money(Long(o, "deposit")))); b.AddChild(Line("Tolerância:", $"{Int(o, "graceDays")} dia(s) depois do vencimento")); }
        if (sale is null && offer is null) b.AddChild(Note("Este terreno não tem contrato nem oferta no momento."));
    }

    private void OpenRenew(JsonElement rental)
    {
        var price = Long(rental, "price"); var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("Renovar o aluguel por mais:", 15, IslandUi.TextPrimary, true));
        var periods = IslandUi.Number(1, 12, 1, $" período(s) de {Int(rental, "periodDays")} dias"); periods.Value = 1; content.AddChild(periods);
        var total = IslandUi.Text("", 16, LandTerritoryMap.Rented, true); content.AddChild(total); void Update() => total.Text = $"Total agora: {LandMath.Money(price * (long)periods.Value)}"; periods.ValueChanged += v => Update(); Update();
        var id = Guid.Parse(Str(rental, "id")); Ask("Renovar aluguel", content, async () => await Do(Api.Renew(IslandId, id, (int)periods.Value), "Aluguel renovado."), "Renovar");
    }
}
