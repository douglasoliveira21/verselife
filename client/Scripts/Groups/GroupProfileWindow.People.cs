using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Shared.Groups;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>The tabs about people: Geral (identity, joining, leaving, headquarters), Membros, Cargos and Permissões.</summary>
public sealed partial class GroupProfileWindow
{
    private JsonElement _roles;
    private string _memberQuery = "", _memberRole = "";
    private int _memberPage;
    private readonly Dictionary<Guid, HashSet<string>> _draft = [];

    // ---- Geral ----

    private async Task ShowGeneralAsync(int generation)
    {
        var card = Card; var colour = new Color(Str(card, "accentColor") is { Length: > 0 } c ? c : "#12F2E0"); var tag = Str(card, "tag");
        var cover = new PanelContainer { CustomMinimumSize = new Vector2(0, 120), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cover.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(colour, .18f), new Color(colour, .5f), 12, 1, 0, 0));
        cover.AddChild(new TextureRect { Texture = UiStyle.Icon(EmblemIcon(Str(card, "emblem")), new Color(colour, .85f), 52), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(52, 52), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        _content.AddChild(cover);
        var head = IslandUi.Row(12); _content.AddChild(head);
        var tile = new PanelContainer { CustomMinimumSize = new Vector2(64, 64) }; tile.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(colour, .22f), colour, 14, 2, 0, 0)); tile.AddChild(new TextureRect { Texture = UiStyle.Icon(EmblemIcon(Str(card, "emblem")), colour, 34), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(34, 34), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }); head.AddChild(tile);
        var names = IslandUi.Column(2); names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; head.AddChild(names);
        var nameLabel = new Label { Text = tag.Length > 0 ? $"{GroupName}  [{tag}]" : GroupName }; nameLabel.AddThemeFontSizeOverride("font_size", 20); nameLabel.AddThemeFontOverride("font", UiStyle.Medium); names.AddChild(nameLabel);
        names.AddChild(Muted($"{Int(card, "members")} membro(s) · {CategoryText(Str(card, "category"))} · {(Str(card, "privacy") == "private" ? "Privado" : "Público")}"));
        _content.AddChild(Body(Str(card, "description") is { Length: > 0 } d ? d : "Este grupo ainda não tem descrição."));

        var info = CardBox(); var lines = IslandUi.Column(4); info.AddChild(lines); _content.AddChild(info);
        void Line(string key, string value) { var r = IslandUi.Row(8); var k = Muted(key); k.CustomMinimumSize = new Vector2(130, 0); r.AddChild(k); var v = Body(value); v.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; r.AddChild(v); lines.AddChild(r); }
        Line("Fundador", Str(_view, "leaderName")); Line("Criado em", Day(Date(_view, "createdAt"))); Line("Idioma", Str(card, "language") is { Length: > 0 } l ? l : "pt-BR"); Line("Classificação", RatingText(Str(card, "contentRating")));
        Line("Tipo de entrada", JoinText(Str(card, "joinPolicy"))); Line("Taxa de inscrição", Long(card, "joinFee") > 0 ? Money(Long(card, "joinFee")) : "Grátis");
        if (IsMember) Line("Seu cargo", Str(_view, "myRole") is { Length: > 0 } role ? role : "Membro");

        var actions = IslandUi.Row(8); _content.AddChild(actions);
        var policy = Str(card, "joinPolicy"); var pending = Flag(card, "requestPending");
        if (!IsMember)
        {
            if (Flag(card, "banned")) actions.AddChild(Act("Você foi banido", "lock", () => { }, enabled: false));
            else if (pending) { actions.AddChild(Act("Solicitação pendente", "calendar", () => { }, enabled: false)); actions.AddChild(Act("Cancelar pedido", "close", () => _ = Do(Api.CancelRequest(_id), "Pedido cancelado."))); }
            else if (policy == "open") actions.AddChild(Act("Participar", "plus", OpenJoin, IslandUi.Kind.Primary));
            else if (policy == "request") actions.AddChild(Act("Solicitar entrada", "plus", OpenJoin, IslandUi.Kind.Primary));
            else if (Flag(card, "invited")) actions.AddChild(Muted("Você foi convidado: aceite na central de notificações."));
            else actions.AddChild(Act("Só por convite", "lock", () => { }, enabled: false));
        }
        else
        {
            actions.AddChild(Act("Convidar", "mail", OpenInvite, IslandUi.Kind.Primary, Can(GroupPermissions.Invite), Can(GroupPermissions.Invite) ? "" : "Seu cargo não pode convidar."));
            actions.AddChild(Act("Abrir chat", "chat", () => _ = SwitchAsync("chat")));
            actions.AddChild(Act("Ver membros", "people", () => _ = SwitchAsync("membros")));
            actions.AddChild(Act("Sair do grupo", "close", OpenLeave, IslandUi.Kind.Danger));
        }

        // the headquarters
        var hq = Guid.TryParse(Str(card, "headquartersIslandId"), out var island) ? island : (Guid?)null;
        var hqCard = CardBox(); var hqBody = IslandUi.Column(6); hqCard.AddChild(hqBody); _content.AddChild(hqCard); hqBody.AddChild(Heading("Sede do grupo"));
        if (hq is null) hqBody.AddChild(Muted("O grupo ainda não definiu uma ilha de encontro."));
        else
        {
            var reply = await Api.Island(hq.Value); if (Stale(generation)) return;
            var row = IslandUi.Row(10); hqBody.AddChild(row); row.AddChild(UiStyle.IconRect("map", UiStyle.Accent, 28));
            row.AddChild(Body(reply.Ok ? Str(reply.Body, "name") : "Ilha"));
            row.AddChild(IslandUi.Spacer()); row.AddChild(Act("Teleportar", "pin", () => { Visit?.Invoke(hq.Value); }, IslandUi.Kind.Primary));
        }
        if (Can(GroupPermissions.EditProfile)) hqBody.AddChild(Act(hq is null ? "Definir sede" : "Alterar sede", "settings", () => _ = SwitchAsync("configuracoes")));
    }

    private static string EmblemIcon(string emblem) { var key = emblem == "coin" ? "coins" : emblem; return UiIconData.Icons.ContainsKey(key) ? key : "people"; }

    private async Task SwitchAsync(string tab) { _tab = tab; RefreshTabs(); await ShowTabAsync(tab); }

    private void OpenJoin()
    {
        var card = Card; var policy = Str(card, "joinPolicy"); var fee = Long(card, "joinFee");
        var content = IslandUi.Column(8);
        if (policy == "open") content.AddChild(Body(fee > 0 ? $"Para entrar em “{GroupName}” você paga {Money(fee)}, que vão para o cofre do grupo. O valor sai da sua carteira agora." : $"Entrar em “{GroupName}” agora?"));
        else content.AddChild(Body($"O grupo “{GroupName}” aprova quem entra. Escreva uma mensagem para quem decide."));
        var message = IslandUi.TextArea("Mensagem (opcional)", 70); message.Visible = policy != "open"; content.AddChild(message);
        Ask(policy == "open" ? "Participar do grupo" : "Solicitar entrada", content, async () => await Do(Api.Join(_id, message.Text.Trim(), policy == "open" ? fee : null), policy == "open" ? $"Você entrou em {GroupName}." : "Pedido enviado."), policy == "open" ? (fee > 0 ? $"Pagar {Money(fee)}" : "Participar") : "Enviar pedido");
    }

    private void OpenLeave()
    {
        var content = IslandUi.Column(8); content.AddChild(Body($"Sair de “{GroupName}”? Para voltar você precisará ser aceito de novo" + (Long(Card, "joinFee") > 0 && Str(Card, "joinPolicy") == "open" ? $" e pagar a taxa de {Money(Long(Card, "joinFee"))}." : ".")));
        if (IsLeader) content.AddChild(Body("Você é o líder: passe a liderança antes de sair (Configurações → Transferir liderança).", 12));
        Ask("Sair do grupo", content, async () => { var ok = await Do(Api.Leave(_id), $"Você saiu de {GroupName}.", reload: false); if (ok) { Changed?.Invoke(); _window.Visible = false; } return ok; }, "Sair");
    }

    private void OpenInvite()
    {
        var content = IslandUi.Column(8); var who = IslandUi.Input("Nome da pessoa", 40); content.AddChild(who);
        var role = IslandUi.Dropdown([("Cargo inicial: o comum do grupo", "")]); content.AddChild(role);
        foreach (var r in Items(_roles).Where(r => Str(r, "systemKey") != "owner" && Str(r, "systemKey") != "member" && (IsLeader || Can(GroupPermissions.AssignRoles) && Int(r, "rank") < MyRank))) role.AddItem(Str(r, "name")); // filled below with ids
        role.Clear(); role.AddItem("Cargo inicial: o comum do grupo"); role.SetItemMetadata(0, "");
        foreach (var r in Items(_roles).Where(r => Str(r, "systemKey") != "owner" && Str(r, "systemKey") != "member" && (IsLeader || Can(GroupPermissions.AssignRoles) && Int(r, "rank") < MyRank))) { role.AddItem(Str(r, "name")); role.SetItemMetadata(role.ItemCount - 1, Str(r, "id")); }
        var message = IslandUi.TextArea("Mensagem do convite (opcional, até 200 caracteres)", 70); content.AddChild(message);
        Ask("Convidar para o grupo", content, async () =>
        {
            if (who.Text.Trim().Length == 0) { Notify("Escreva o nome de quem você quer convidar."); return false; }
            return await Do(Api.Invite(_id, who.Text.Trim(), message.Text.Trim(), Guid.TryParse(IslandUi.IdOf(role), out var roleId) ? roleId : null), "Convite enviado.", reload: false);
        }, "Enviar convite");
    }

    // ---- Membros ----

    private async Task ShowMembersAsync(int generation)
    {
        var members = await Api.Members(_id); var roles = await Api.Roles(_id); if (Stale(generation)) return;
        if (!members.Ok) { _content.AddChild(Empty(GroupWords(members))); return; }
        if (roles.Ok) _roles = roles.Body.Clone();
        var all = Items(members.Body).ToList();
        _content.AddChild(TitleRow("Membros", $"{all.Count}", Act("Convidar membros", "mail", OpenInvite, IslandUi.Kind.Primary, Can(GroupPermissions.Invite))));
        if (Can(GroupPermissions.ApproveRequests)) await ShowRequestsAsync(generation);
        if (Stale(generation)) return;
        var filters = IslandUi.Row(8); _content.AddChild(filters);
        var search = new LineEdit { PlaceholderText = "Pesquisar membros...", Text = _memberQuery, ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32), RightIcon = UiStyle.Icon("search", UiStyle.TextMuted, 16) }; filters.AddChild(search);
        var roleFilter = IslandUi.Dropdown([("Todos os cargos", "")]); foreach (var r in Items(_roles)) { roleFilter.AddItem(Str(r, "name")); roleFilter.SetItemMetadata(roleFilter.ItemCount - 1, Str(r, "name")); } IslandUi.Select(roleFilter, _memberRole); filters.AddChild(roleFilter);
        var rows = IslandUi.Column(4); _content.AddChild(rows); var pager = IslandUi.Row(8); _content.AddChild(pager);
        void Draw()
        {
            foreach (var child in rows.GetChildren()) child.QueueFree(); foreach (var child in pager.GetChildren()) child.QueueFree();
            var shown = all.Where(m => (_memberQuery.Length == 0 || Str(m, "username").Contains(_memberQuery, StringComparison.OrdinalIgnoreCase)) && (_memberRole.Length == 0 || Str(m, "roleName") == _memberRole)).OrderByDescending(m => Int(m, "rank")).ThenBy(m => Str(m, "username")).ToList();
            var pages = Math.Max(1, (int)Math.Ceiling(shown.Count / 20.0)); _memberPage = Math.Clamp(_memberPage, 0, pages - 1);
            if (shown.Count == 0) rows.AddChild(Empty("Nenhum membro encontrado."));
            foreach (var m in shown.Skip(_memberPage * 20).Take(20)) rows.AddChild(MemberRow(m));
            pager.AddChild(Muted($"{shown.Count} resultado(s)")); pager.AddChild(IslandUi.Spacer());
            pager.AddChild(Act("‹", null, () => { _memberPage--; Draw(); }, enabled: _memberPage > 0)); pager.AddChild(Muted($"{_memberPage + 1} / {pages}")); pager.AddChild(Act("›", null, () => { _memberPage++; Draw(); }, enabled: _memberPage < pages - 1));
        }
        search.TextChanged += text => { _memberQuery = text.Trim(); _memberPage = 0; Draw(); };
        roleFilter.ItemSelected += _ => { _memberRole = IslandUi.IdOf(roleFilter); _memberPage = 0; Draw(); };
        Draw();
    }

    private Control MemberRow(JsonElement m)
    {
        var user = Guid.Parse(Str(m, "userId")); var name = Str(m, "username"); var rank = Int(m, "rank"); var role = Str(m, "roleName"); var muted = Date(m, "mutedUntil") > DateTimeOffset.UtcNow;
        var row = CardBox(); var line = IslandUi.Row(10); row.AddChild(line); line.AddChild(Avatar(name));
        var names = IslandUi.Column(0); names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; line.AddChild(names); names.AddChild(Body(name, 14)); names.AddChild(Muted($"Entrou em {Day(Date(m, "joinedAt"))}", 11));
        var roleLabel = Body(role); roleLabel.CustomMinimumSize = new Vector2(110, 0); roleLabel.AddThemeColorOverride("font_color", UiStyle.Accent); line.AddChild(roleLabel);
        var status = Muted(muted ? "Silenciado" : "—"); status.CustomMinimumSize = new Vector2(80, 0); line.AddChild(status);
        var menu = new MenuButton { Text = "⋯", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Ações", CustomMinimumSize = new Vector2(34, 30) }; var popup = menu.GetPopup(); var targets = new List<Action>();
        void Add(string label, Action act) { popup.AddItem(label); targets.Add(act); }
        var above = IsLeader ? rank < GroupRules.LeaderRank : MyRank > rank; var isMe = false; // nobody acts on the leader
        Add("Ver perfil", () => OpenProfile?.Invoke(user));
        if (above && !isMe && Can(GroupPermissions.AssignRoles)) Add("Atribuir cargo…", () => OpenAssign(user, name, rank));
        if (above && Can(GroupPermissions.Mute)) Add(muted ? "Reativar o chat" : "Silenciar 30 min", () => _ = Do(Api.Mute(_id, user, muted ? 0 : 30), muted ? $"{name} pode falar de novo." : $"{name} foi silenciado por 30 minutos."));
        if (above && Can(GroupPermissions.Kick)) Add("Expulsar", () => Confirm("Expulsar", $"Expulsar {name} de “{GroupName}”? A pessoa pode voltar.", () => Api.Kick(_id, user), $"{name} foi expulso."));
        if (above && Can(GroupPermissions.Ban)) Add("Banir", () => Confirm("Banir", $"Banir {name} de “{GroupName}” por 7 dias?", () => Api.Ban(_id, user, 7), $"{name} foi banido por 7 dias."));
        popup.IdPressed += id => { if (id >= 0 && id < targets.Count) targets[(int)id].Invoke(); }; line.AddChild(menu); return row;
    }

    /// <summary>Pending requests to join, with a box on each so several can be approved or refused at once.</summary>
    private async Task ShowRequestsAsync(int generation)
    {
        var reply = await Api.Requests(_id); if (Stale(generation) || !reply.Ok) return;
        var requests = Items(reply.Body).ToList(); if (requests.Count == 0) return;
        var card = CardBox(UiStyle.Accent); _content.AddChild(card); var column = IslandUi.Column(6); card.AddChild(column);
        column.AddChild(Heading($"Solicitações para entrar ({requests.Count})"));
        var boxes = new List<(CheckBox Box, Guid Id)>();
        foreach (var request in requests)
        {
            var line = IslandUi.Row(8); column.AddChild(line); var message = Str(request, "message");
            var box = new CheckBox { Text = Str(request, "username") + (message.Length > 0 ? $" — “{message}”" : ""), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, ClipText = true }; line.AddChild(box);
            line.AddChild(Muted(Moment(Date(request, "at")))); if (Guid.TryParse(Str(request, "id"), out var id)) boxes.Add((box, id));
        }
        var actions = IslandUi.Row(8); column.AddChild(actions);
        var all = new CheckBox { Text = "Selecionar todos", FocusMode = Control.FocusModeEnum.None }; all.Toggled += on => { foreach (var (box, _) in boxes) box.ButtonPressed = on; }; actions.AddChild(all); actions.AddChild(IslandUi.Spacer());
        async Task Decide(bool approve)
        {
            var chosen = boxes.Where(b => b.Box.ButtonPressed).Select(b => b.Id).ToList(); if (chosen.Count == 0) { Notify("Marque pelo menos uma solicitação."); return; }
            var failed = 0; foreach (var id in chosen) if (!(await Api.Decide(_id, id, approve)).Ok) failed++;
            Notify(failed == 0 ? (approve ? $"{chosen.Count} pessoa(s) aprovada(s)." : $"{chosen.Count} solicitação(ões) recusada(s).") : $"{chosen.Count - failed} de {chosen.Count} resolvidas; as outras falharam (grupo cheio ou pedido já resolvido).");
            Changed?.Invoke(); await ReloadAsync();
        }
        actions.AddChild(Act("Recusar selecionados", null, () => _ = Decide(false), IslandUi.Kind.Danger)); actions.AddChild(Act("Aprovar selecionados", "check", () => _ = Decide(true), IslandUi.Kind.Primary));
    }

    private void Confirm(string title, string text, Func<Task<IslandReply>> call, string done)
    {
        var content = IslandUi.Column(8); content.AddChild(Body(text));
        Ask(title, content, async () => await Do(call(), done), title);
    }

    private void OpenAssign(Guid user, string name, int targetRank)
    {
        var content = IslandUi.Column(8); content.AddChild(Body($"Escolha o cargo de {name}."));
        var role = IslandUi.Dropdown([]); content.AddChild(role);
        foreach (var r in Items(_roles).Where(r => Str(r, "systemKey") != "owner" && (IsLeader || Int(r, "rank") < MyRank))) { role.AddItem(Str(r, "name")); role.SetItemMetadata(role.ItemCount - 1, Str(r, "id")); }
        if (role.ItemCount == 0) { Notify("Você não pode dar nenhum cargo."); return; }
        Ask("Atribuir cargo", content, async () => Guid.TryParse(IslandUi.IdOf(role), out var id) && await Do(Api.SetRole(_id, user, id), $"{name} agora tem esse cargo."), "Atribuir");
    }

    // ---- Cargos ----

    private Guid? _roleSelected;

    private async Task ShowRolesAsync(int generation)
    {
        var roles = await Api.Roles(_id); if (Stale(generation)) return; if (!roles.Ok) { _content.AddChild(Empty(GroupWords(roles))); return; }
        _roles = roles.Body.Clone(); var list = Items(_roles).ToList(); if (_roleSelected is null || !list.Any(r => Str(r, "id") == _roleSelected.ToString())) _roleSelected = list.Count > 0 ? Guid.Parse(Str(list[0], "id")) : null;
        var canManage = Can(GroupPermissions.ManageRoles);
        var row = IslandUi.Row(12); _content.AddChild(row);
        var left = IslandUi.Column(6); left.CustomMinimumSize = new Vector2(190, 0); row.AddChild(left);
        left.AddChild(Heading("Cargos"));
        foreach (var r in list)
        {
            var id = Guid.Parse(Str(r, "id")); var on = id == _roleSelected;
            var item = CardBox(on ? UiStyle.Accent : null); item.MouseFilter = Control.MouseFilterEnum.Stop; item.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            var c = IslandUi.Column(0); item.AddChild(c); c.AddChild(Body(Str(r, "name"), 14)); c.AddChild(Muted($"{Int(r, "members")} membro(s) · nível {Int(r, "rank")}", 11));
            item.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _roleSelected = id; _draft.Remove(id); _ = ShowRolesAsync(_generation); } }; left.AddChild(item);
        }
        left.AddChild(Act("Novo cargo", "plus", OpenNewRole, IslandUi.Kind.Primary, canManage, canManage ? "" : "Seu cargo não gerencia cargos."));
        var right = IslandUi.Column(8); right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(right);
        if (list.FirstOrDefault(r => Str(r, "id") == _roleSelected.ToString()) is { ValueKind: JsonValueKind.Object } selected) BuildRoleEditor(right, selected, canManage); else right.AddChild(Empty("Escolha um cargo na lista."));
    }

    private void BuildRoleEditor(VBoxContainer panel, JsonElement role, bool canManage)
    {
        var id = Guid.Parse(Str(role, "id")); var system = Str(role, "systemKey"); var editable = Flag(role, "canEdit") && canManage; var owner = system == "owner";
        panel.AddChild(Heading(Str(role, "name")));
        var name = IslandUi.Input("Nome do cargo", 32); name.Text = Str(role, "name"); name.Editable = editable && system.Length == 0; panel.AddChild(Labeled("Nome", name));
        var title = IslandUi.Input("Título exibido acima do avatar", GroupRules.MaxRoleTitle); title.Text = Str(role, "title"); title.Editable = canManage && (!owner || IsLeader); panel.AddChild(Labeled("Título", title));
        var rank = IslandUi.Number(1, GroupRules.MaxCustomRank, 1, "", 120); rank.Value = Math.Clamp(Int(role, "rank"), 1, GroupRules.MaxCustomRank); rank.Editable = editable && system.Length == 0; panel.AddChild(Labeled("Nível (quem manda em quem)", rank));
        panel.AddChild(Muted($"{Int(role, "members")} membro(s) com este cargo."));
        panel.AddChild(Heading("Permissões"));
        var current = Items(Prop(role, "permissions")).Select(p => p.GetString() ?? "").ToHashSet(); var boxes = new Dictionary<string, CheckBox>();
        foreach (var (key, label, help) in GroupPermissions.All)
        {
            var allowed = editable && !owner && (IsLeader || Can(key)); var box = new CheckBox { Text = label, ButtonPressed = current.Contains(key) || owner, Disabled = !allowed, TooltipText = help, FocusMode = Control.FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 13); panel.AddChild(box); boxes[key] = box;
        }
        var buttons = IslandUi.Row(8); panel.AddChild(buttons);
        buttons.AddChild(Act("Salvar", "check", async () => await SaveRoleAsync(id, system, name.Text.Trim(), title.Text.Trim(), (int)rank.Value, boxes, editable), IslandUi.Kind.Primary, canManage && (editable || title.Editable)));
        buttons.AddChild(Act("Cancelar", "close", () => _ = ShowRolesAsync(_generation)));
        buttons.AddChild(Act("Duplicar cargo", "plus", () => OpenDuplicate(role), IslandUi.Kind.Secondary, canManage && !owner));
        buttons.AddChild(Act("Excluir", "close", () => Confirm("Excluir cargo", $"Excluir o cargo “{Str(role, "name")}”? Os membros voltam ao cargo comum.", () => Api.DeleteRole(_id, id), "Cargo excluído."), IslandUi.Kind.Danger, editable && system.Length == 0));
        if (owner) panel.AddChild(Muted("O cargo de líder nunca é editado: ele só muda de mãos na transferência de liderança.", 12));
    }

    private static Control Labeled(string label, Control field) { var column = IslandUi.Column(2); var l = IslandUi.Text(label, 12, UiStyle.TextMuted); column.AddChild(l); column.AddChild(field); return column; }

    private async Task SaveRoleAsync(Guid id, string system, string name, string title, int rank, Dictionary<string, CheckBox> boxes, bool editable)
    {
        if (editable)
        {
            var permissions = boxes.Where(b => b.Value.ButtonPressed).Select(b => b.Key).ToList(); object body = system.Length == 0 ? new { name, rank, permissions } : new { permissions };
            if (!await Do(Api.UpdateRole(_id, id, body), string.Empty, reload: false)) return;
        }
        if (!await Do(Api.RoleTitle(_id, id, title), "Cargo salvo.", reload: false)) return;
        await ShowRolesAsync(_generation);
    }

    private void OpenNewRole() => OpenRoleDialog("Novo cargo", "", 10, []);

    private void OpenDuplicate(JsonElement role) => OpenRoleDialog("Duplicar cargo", $"{Str(role, "name")} (cópia)", Math.Min(Int(role, "rank"), IsLeader ? GroupRules.MaxCustomRank : Math.Max(1, MyRank - 1)), [.. Items(Prop(role, "permissions")).Select(p => p.GetString() ?? "")]);

    private void OpenRoleDialog(string title, string name, int rank, IReadOnlyList<string> permissions)
    {
        var content = IslandUi.Column(8); var nameField = IslandUi.Input("Nome do cargo", 32); nameField.Text = name; content.AddChild(Labeled("Nome", nameField));
        var rankField = IslandUi.Number(1, IsLeader ? GroupRules.MaxCustomRank : Math.Max(1, MyRank - 1), 1, "", 120); rankField.Value = rank; content.AddChild(Labeled("Nível", rankField));
        content.AddChild(Muted(permissions.Count > 0 ? $"Começa com {permissions.Count} permissão(ões) do cargo copiado (só as que você pode dar)." : "Começa sem permissões: marque-as depois em Cargos ou Permissões."));
        Ask(title, content, async () =>
        {
            var give = permissions.Where(p => IsLeader || Can(p)).ToList(); var ok = await Do(Api.CreateRole(_id, nameField.Text.Trim(), (int)rankField.Value, give), "Cargo criado.", reload: false); if (ok) await ShowRolesAsync(_generation); return ok;
        }, "Criar");
    }

    // ---- Permissões ----

    private async Task ShowMatrixAsync(int generation)
    {
        var roles = await Api.Roles(_id); if (Stale(generation)) return; if (!roles.Ok) { _content.AddChild(Empty(GroupWords(roles))); return; }
        _roles = roles.Body.Clone(); var list = Items(_roles).OrderByDescending(r => Int(r, "rank")).ToList(); var canManage = Can(GroupPermissions.ManageRoles);
        _content.AddChild(TitleRow("Matriz de permissões"));
        _content.AddChild(Muted("Marque o que cada cargo pode fazer. As mudanças só valem ao salvar."));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 0) }; _content.AddChild(scroll);
        var grid = new GridContainer { Columns = list.Count + 1 }; grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 2); scroll.AddChild(grid);
        _draft.Clear(); var original = new Dictionary<Guid, HashSet<string>>(); var boxes = new Dictionary<(Guid, string), CheckBox>();
        grid.AddChild(Muted("Permissão")); foreach (var r in list) { var h = Body(Str(r, "name"), 12); h.HorizontalAlignment = HorizontalAlignment.Center; h.CustomMinimumSize = new Vector2(84, 0); h.AddThemeColorOverride("font_color", UiStyle.Accent); grid.AddChild(h); original[Guid.Parse(Str(r, "id"))] = Items(Prop(r, "permissions")).Select(p => p.GetString() ?? "").ToHashSet(); }
        foreach (var (key, label, help) in GroupPermissions.All)
        {
            var name = Body(label, 12); name.CustomMinimumSize = new Vector2(190, 0); name.TooltipText = help; grid.AddChild(name);
            foreach (var r in list)
            {
                var id = Guid.Parse(Str(r, "id")); var owner = Str(r, "systemKey") == "owner"; var editable = canManage && Flag(r, "canEdit") && !owner && (IsLeader || Can(key));
                var box = new CheckBox { ButtonPressed = original[id].Contains(key) || owner, Disabled = !editable, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, TooltipText = editable ? "" : owner ? "O líder tem tudo." : "Você não pode mudar isto." }; grid.AddChild(box); boxes[(id, key)] = box;
            }
        }
        var buttons = IslandUi.Row(8); _content.AddChild(buttons);
        buttons.AddChild(Act("Salvar alterações", "check", async () =>
        {
            var changed = 0; foreach (var r in list)
            {
                var id = Guid.Parse(Str(r, "id")); if (Str(r, "systemKey") == "owner") continue; var now = GroupPermissions.All.Where(p => boxes[(id, p.Key)].ButtonPressed).Select(p => p.Key).ToHashSet();
                if (now.SetEquals(original[id])) continue; changed++; if (!await Do(Api.UpdateRole(_id, id, new { permissions = now }), string.Empty, reload: false)) return;
            }
            Notify(changed == 0 ? "Nada mudou." : $"Permissões salvas em {changed} cargo(s)."); await ShowMatrixAsync(_generation);
        }, IslandUi.Kind.Primary, canManage));
        buttons.AddChild(Act("Descartar alterações", "close", () => _ = ShowMatrixAsync(_generation)));
    }
}
