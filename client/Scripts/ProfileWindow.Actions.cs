using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The bottom bar of the profile. On somebody else's profile: IM / Chat, Teletransportar, friendship, Enviar presente, Bloquear and Mais. On mine: Editar perfil, Alterar aparência, Compartilhar perfil and
/// the privacy settings. Every button does a real thing through the same API calls the rest of the game uses; the server checks everything again.
/// </summary>
public partial class ProfileWindow
{
    /// <summary>The island the person is on now (a teleport invite needs it).</summary>
    public Func<Guid>? CurrentIsland { get; set; }

    private readonly List<Control> _dynamicActions = [];
    private HFlowContainer _actions = null!;
    private PopupMenu? _teleportMenu, _moreMenu;
    private string _relation = string.Empty;
    private int _relationFor;

    // ---- test access -------------------------------------------------------------------------------------------------------
    public IReadOnlyList<string> ActionsForTesting => [.. _dynamicActions.OfType<Button>().Where(b => b.Visible).Select(b => b.Text.Trim())];
    public bool ActionEnabledForTesting(string text) => _dynamicActions.OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { Disabled: false };
    public void PressActionForTesting(string text) => _dynamicActions.OfType<Button>().First(b => b.Text.Trim() == text).EmitSignal(BaseButton.SignalName.Pressed);
    public IReadOnlyList<string> MoreForTesting => [.. Enumerable.Range(0, _moreMenu?.ItemCount ?? 0).Select(i => _moreMenu!.GetItemText(i))];
    public IReadOnlyList<string> TeleportForTesting => [.. Enumerable.Range(0, _teleportMenu?.ItemCount ?? 0).Select(i => _teleportMenu!.GetItemText(i))];
    public void ChooseMoreForTesting(int index) => OnMore(_moreMenu!.GetItemId(index));
    public void ChooseTeleportForTesting(int index) => OnTeleport(_teleportMenu!.GetItemId(index));
    public string RelationForTesting => _relation;
    public int DialogsForTesting => _dialogs.Count(d => GodotObject.IsInstanceValid(d) && d.Visible);
    public void AnswerDialogForTesting(string text, bool confirm = true) { var dialog = _dialogs.Last(d => GodotObject.IsInstanceValid(d) && d.Visible); if (dialog.FindChild("Field", true, false) is LineEdit field) field.Text = text; ((Button)dialog.FindChild(confirm ? "Confirm" : "Cancel", true, false)!).EmitSignal(BaseButton.SignalName.Pressed); }
    public IReadOnlyList<string> GiftItemsForTesting => _giftList is null ? [] : [.. Enumerable.Range(0, _giftList.ItemCount).Select(i => _giftList.GetItemText(i))];
    public void PickGiftForTesting(int row) { _giftList!.Select(row); }
    private readonly List<FloatingWindow> _dialogs = [];

    private Button BarButton(string text, string icon, string tip, Action action, UiStyle.ButtonKind kind = UiStyle.ButtonKind.Tool)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 18), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 42), TooltipText = tip };
        UiStyle.StyleButton(button, kind, 14, 6, 10); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private void AddDynamic(Control control) { _actions.AddChild(control); _actions.MoveChild(control, _dynamicActions.Count); _dynamicActions.Add(control); }

    /// <summary>Redraws the buttons for the profile on screen: mine or somebody else's.</summary>
    private void RebuildActions()
    {
        foreach (var old in _dynamicActions) { _actions.RemoveChild(old); old.QueueFree(); }
        _dynamicActions.Clear();
        if (_profile.ValueKind != JsonValueKind.Object) return;
        var name = Str(_profile, "displayName");
        if (IsSelf)
        {
            AddDynamic(BarButton("Editar perfil", "settings", "Vai para o texto “Sobre mim” para você editar", () => { _tabs.Select(0); _bio.GrabFocus(); }));
            if (Shortcuts.TryGetValue("appearance", out var appearance)) AddDynamic(BarButton("Alterar aparência", "avatar", "Abre o espelho para mudar o seu visual", () => appearance()));
            AddDynamic(BarButton("Compartilhar perfil", "link", "Copia um texto com o seu nome de usuário e o identificador do perfil", ShareProfile));
            AddDynamic(BarButton("Privacidade", "lock", "Mostra as opções do que os outros podem ver", () => { _tabs.Select(0); _scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, _selfSettings); }));
            return;
        }
        if (_viewing == Guid.Empty) return;
        var id = _viewing;
        AddDynamic(BarButton("IM / Chat", "chat", $"Escreve uma mensagem privada para {name}", () => AskText($"Mensagem para {name}", "Escreva a mensagem…", "Enviar", text => _ = Do(_api.Message(id, text), $"Mensagem enviada para {name}."), 1000), UiStyle.ButtonKind.ToolSelected));
        AddDynamic(BarButton("Teletransportar", "pin", "Pede para ir até a pessoa ou a convida para onde você está", ShowTeleportMenu));
        AddDynamic(FriendButton(id, name));
        AddDynamic(BarButton("Enviar Presente", "bag", $"Escolhe um item do seu inventário para dar a {name}", () => _ = OpenGiftAsync(id, name)));
        AddDynamic(BarButton("Bloquear", "ban", $"Bloqueia {name}: a pessoa não poderá mais falar com você", () => Confirm($"Bloquear {name}?", $"{name} não poderá mais falar com você nem enviar convites. Você pode desfazer isso na janela Pessoas.", "Bloquear", () => _ = Do(_api.Block(id), $"{name} foi bloqueado."), true)));
        AddDynamic(BarButton("Mais", "dots", "Copiar nome, identificador, compartilhar ou denunciar", ShowMore));
    }

    private Button FriendButton(Guid id, string name)
    {
        var (text, tip, enabled) = _relation switch
        {
            "friend" => ("Amigo ✓", $"Vocês são amigos. Clique para desfazer a amizade com {name}.", true),
            "pending_out" => ("Pedido enviado", "Aguardando a resposta da pessoa.", false),
            "pending_in" => ("Aceitar amizade", $"{name} quer ser seu amigo.", true),
            _ => ("Adicionar Amigo", $"Envia um pedido de amizade para {name}", true)
        };
        var button = BarButton(text, "user-add", tip, () =>
        {
            switch (_relation)
            {
                case "friend": Confirm($"Desfazer a amizade com {name}?", "Vocês deixam de ser amigos. Dá para enviar um novo pedido depois.", "Desfazer", () => _ = ChangeFriendshipAsync(_api.RemoveFriend(id), "none", $"Amizade com {name} desfeita.")); break;
                case "pending_in": _ = ChangeFriendshipAsync(_api.AcceptFriend(id), "friend", $"Agora você e {name} são amigos."); break;
                default: _ = ChangeFriendshipAsync(_api.AddFriend(id), "pending_out", $"Pedido de amizade enviado para {name}."); break;
            }
        });
        button.Disabled = !enabled; return button;
    }

    private async Task ChangeFriendshipAsync(Task<ProfileReply> call, string relationAfter, string ok)
    {
        var reply = await call;
        if (!reply.Ok) { _status.Text = reply.Status == 409 ? "Vocês já são amigos ou já existe um pedido." : Describe(reply); return; }
        _relation = relationAfter; _status.Text = ok; RebuildActions();
    }

    private async Task LoadRelationAsync(Guid id)
    {
        var token = ++_relationFor;
        var relation = await _api.RelationTo(Str(_profile, "username"), id);
        if (token != _relationFor || id != _viewing || IsSelf || relation == _relation) return;
        _relation = relation; RebuildActions();
    }


    /// <summary>Runs a call, shows the result in the bar, and says whether it worked.</summary>
    private async Task<bool> Do(Task<ProfileReply> call, string ok)
    {
        _status.Text = "Enviando…"; var reply = await call;
        _status.Text = reply.Ok ? ok : Describe(reply); return reply.Ok;
    }

    private static string Describe(ProfileReply reply)
    {
        var code = reply.Body.ValueKind == JsonValueKind.Object && reply.Body.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : string.Empty;
        return code switch
        {
            "not_on_island" => "A pessoa e você precisam estar na mesma ilha agora.",
            "island_offline" => "A ilha está desligada: não dá para teleportar.",
            "blocked" => "Não dá para fazer isso com quem bloqueou você ou foi bloqueado por você.",
            "already_pending" => "Já existe um convite desse tipo esperando resposta.",
            "self" or "to_self" => "Isso não pode ser feito com você mesmo.",
            "recipient_full" => "O inventário da pessoa está cheio.",
            "recipient_not_found" => "A pessoa não foi encontrada.",
            "item_busy" => "Esse item está ocupado; tente de novo em instantes.",
            "too_many_offers" => "Há ofertas demais esperando resposta.",
            _ => reply.Status == 429 ? "Muitas ações seguidas; espere um instante." : reply.Message
        };
    }

    // ---- teleport, more -------------------------------------------------------------------------------------------------------

    private void ShowTeleportMenu()
    {
        if (_teleportMenu is null) { _teleportMenu = new PopupMenu(); _teleportMenu.IdPressed += OnTeleport; AddChild(_teleportMenu); }
        _teleportMenu.Clear(); var name = Str(_profile, "displayName");
        _teleportMenu.AddItem($"Pedir para ir até {name}", 1); _teleportMenu.AddItem($"Convidar {name} para onde estou", 2);
        PopAt(_teleportMenu);
    }

    private void OnTeleport(long id)
    {
        var island = CurrentIsland?.Invoke() ?? Guid.Empty;
        if (island == Guid.Empty) { _status.Text = "Entre em uma ilha para usar o teleporte."; return; }
        var name = Str(_profile, "displayName"); var kind = id == 1 ? "request" : "offer";
        _ = Do(_api.Teleport(_viewing, kind, island), kind == "offer" ? $"Convite de teleporte enviado a {name}. Ele vale por 2 minutos." : $"Pedido de teleporte enviado a {name}. Ele vale por 2 minutos.");
    }

    private void ShowMore()
    {
        if (_moreMenu is null) { _moreMenu = new PopupMenu(); _moreMenu.IdPressed += OnMore; AddChild(_moreMenu); }
        _moreMenu.Clear();
        _moreMenu.AddItem("Copiar nome", 1); _moreMenu.AddItem("Copiar identificador", 2); _moreMenu.AddItem("Ver grupos", 3); _moreMenu.AddItem("Compartilhar perfil", 4); _moreMenu.AddSeparator(); _moreMenu.AddItem("Denunciar…", 5);
        PopAt(_moreMenu);
    }

    private void OnMore(long id)
    {
        var name = Str(_profile, "displayName");
        switch (id)
        {
            case 1: DisplayServer.ClipboardSet(name); _status.Text = "Nome copiado."; break;
            case 2: DisplayServer.ClipboardSet(_viewing.ToString()); _status.Text = "Identificador copiado."; break;
            case 3: _tabs.Select(TabGroups); break;
            case 4: ShareProfile(); break;
            case 5: AskText($"Denunciar {name}", "Conte o que aconteceu…", "Denunciar", reason => _ = Do(_api.Report(_viewing, reason), $"Denúncia sobre {name} enviada à moderação."), 500); break;
        }
    }

    private void ShareProfile()
    {
        DisplayServer.ClipboardSet($"{Str(_profile, "displayName")} (@{Str(_profile, "username")}) — perfil no LifeVerse · id {_viewing}"); _status.Text = "Texto do perfil copiado: cole onde quiser compartilhar.";
    }

    private void PopAt(PopupMenu menu)
    {
        var mouse = _window.GetGlobalMousePosition(); var screen = _window.GetViewportRect().Size;
        menu.Size = new Vector2I(260, 10); var at = new Vector2I((int)Math.Min(mouse.X, screen.X - 270), (int)Math.Max(0, mouse.Y - 10 - menu.ItemCount * 34));
        menu.Position = at; menu.Popup();
    }

    // ---- small dialogs --------------------------------------------------------------------------------------------------------

    private FloatingWindow MakeDialog(string title, float width = 420, float height = 220)
    {
        var dialog = new FloatingWindow(title, icon: "user") { Name = "ProfileDialog" + _dialogs.Count, Theme = ViewerTheme.Create(), EdgeResizable = false };
        dialog.Place(.5f, .5f, -width / 2, -height / 2, width / 2, height / 2); AddChild(dialog); dialog.MoveToFront(); _dialogs.Add(dialog);
        dialog.CloseRequested += () => { _dialogs.Remove(dialog); dialog.QueueFree(); };
        return dialog;
    }

    private static void CloseDialog(FloatingWindow dialog, List<FloatingWindow> list) { list.Remove(dialog); dialog.QueueFree(); }

    private void DialogButtons(FloatingWindow dialog, string confirmText, Action confirm, bool danger = false)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; row.AddThemeConstantOverride("separation", 8); dialog.Content.AddChild(row);
        var cancel = new Button { Name = "Cancel", Text = "Cancelar", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) }; UiStyle.StyleButton(cancel, UiStyle.ButtonKind.Tool, 14, 6, 10); cancel.Pressed += () => CloseDialog(dialog, _dialogs); row.AddChild(cancel);
        var ok = new Button { Name = "Confirm", Text = confirmText, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) }; UiStyle.StyleButton(ok, danger ? UiStyle.ButtonKind.Secondary : UiStyle.ButtonKind.Primary, 14, 6, 10); ok.Pressed += confirm; row.AddChild(ok);
    }

    /// <summary>Asks for a short text (a message, the reason of a report) and hands it over; empty text is not accepted.</summary>
    private void AskText(string title, string placeholder, string button, Action<string> submit, int maxLength = 500)
    {
        var dialog = MakeDialog(title, 440, 190);
        var field = new LineEdit { Name = "Field", PlaceholderText = placeholder, MaxLength = maxLength, CustomMinimumSize = new Vector2(0, 40) }; dialog.Content.AddChild(field);
        var note = Text("", 14, false, UiStyle.Orange); dialog.Content.AddChild(note);
        void Send() { var text = field.Text.Trim(); if (text.Length == 0) { note.Text = "Escreva alguma coisa antes de enviar."; return; } CloseDialog(dialog, _dialogs); submit(text); }
        field.TextSubmitted += _ => Send(); DialogButtons(dialog, button, Send); field.GrabFocus();
    }

    private void Confirm(string title, string message, string button, Action confirm, bool danger = false)
    {
        var dialog = MakeDialog(title, 440, 190); dialog.Content.AddChild(Text(message, 16));
        DialogButtons(dialog, button, () => { CloseDialog(dialog, _dialogs); confirm(); }, danger);
    }

    // ---- gift -----------------------------------------------------------------------------------------------------------------

    private ItemList? _giftList;
    private readonly List<(Guid Id, string Name)> _giftItems = [];

    /// <summary>Lists the items that can be handed over (the transfer right is set and nothing is waiting on them) and offers the chosen one: the person must accept it.</summary>
    private async Task OpenGiftAsync(Guid to, string name)
    {
        _status.Text = "Abrindo o inventário…"; var reply = await _api.MyItems();
        if (!reply.Ok) { _status.Text = Describe(reply); return; }
        _giftItems.Clear();
        if (reply.Body.ValueKind == JsonValueKind.Object && reply.Body.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray())
            {
                if (!Guid.TryParse(Str(item, "itemId"), out var itemId) || Str(item, "status") != "active" || Flag(item, "deleted")) continue;
                var rights = item.TryGetProperty("ownerRights", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0;
                if ((rights & LifeVerse.Shared.Items.ItemRights.Transfer) == 0) continue;
                _giftItems.Add((itemId, Str(item, "name")));
            }
        _status.Text = string.Empty;
        var dialog = MakeDialog($"Enviar presente para {name}", 460, 420);
        if (_giftItems.Count == 0) { dialog.Content.AddChild(Text("Você não tem nenhum item que possa ser dado: o direito de transferir precisa estar ativo.", 16, false, UiStyle.TextMuted)); DialogButtons(dialog, "Fechar", () => CloseDialog(dialog, _dialogs)); return; }
        dialog.Content.AddChild(Text("Escolha o item. A pessoa recebe uma oferta e precisa aceitar.", 15, false, UiStyle.TextMuted));
        _giftList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 200), SelectMode = ItemList.SelectModeEnum.Single }; foreach (var (_, itemName) in _giftItems) _giftList.AddItem(itemName);
        dialog.Content.AddChild(_giftList);
        var message = new LineEdit { Name = "Field", PlaceholderText = "Mensagem (opcional)", MaxLength = 200, CustomMinimumSize = new Vector2(0, 38) }; dialog.Content.AddChild(message);
        var note = Text("", 14, false, UiStyle.Orange); dialog.Content.AddChild(note);
        DialogButtons(dialog, "Enviar presente", () =>
        {
            var picked = _giftList.GetSelectedItems(); if (picked.Length == 0) { note.Text = "Escolha um item da lista."; return; }
            var (itemId, itemName) = _giftItems[picked[0]]; CloseDialog(dialog, _dialogs);
            _ = Do(_api.Gift(itemId, to, message.Text.Trim() is { Length: > 0 } text ? text : null, Guid.NewGuid().ToString("N")), $"“{itemName}” foi oferecido a {name}: falta ele aceitar.");
        });
    }
}
