using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.Notifications;
using LifeVerse.Shared.Groups;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>The tabs about what the group does: Avisos, Chat, Terrenos, Economia, Propostas and Configurações.</summary>
public sealed partial class GroupProfileWindow
{
    private VBoxContainer? _chatList; private LineEdit? _chatInput; private int _chatShown = -1;
    private string _lastChat = "";

    // ---- Avisos ----

    private async Task ShowNoticesAsync(int generation)
    {
        var reply = await Api.Announcements(_id); if (Stale(generation)) return; if (!reply.Ok) { _content.AddChild(Empty(GroupWords(reply))); return; }
        var list = Items(reply.Body).ToList(); var canPost = Can(GroupPermissions.Announce);
        _content.AddChild(TitleRow("Avisos", $"{list.Count}", Act("+ Novo aviso", "plus", OpenNewNotice, IslandUi.Kind.Primary, canPost, canPost ? "" : "Seu cargo não publica avisos.")));
        if (list.Count == 0) { _content.AddChild(Empty("Nenhum aviso publicado.")); return; }
        var now = CenterView.Now();
        foreach (var n in list)
        {
            var id = Guid.Parse(Str(n, "id")); var card = CardBox(Flag(n, "pinned") ? UiStyle.Orange : null); var col = IslandUi.Column(3); card.AddChild(col);
            var head = IslandUi.Row(8); col.AddChild(head); head.AddChild(UiStyle.IconRect(Flag(n, "pinned") ? "star" : "bell", UiStyle.Orange, 18)); var title = Body(Str(n, "title"), 15); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; head.AddChild(title);
            if (canPost) head.AddChild(Act("Apagar", "close", () => Confirm("Apagar aviso", $"Apagar “{Str(n, "title")}”?", () => Api.DeleteAnnouncement(_id, id), "Aviso apagado."), IslandUi.Kind.Danger));
            col.AddChild(Body(Str(n, "body"), 13));
            var meta = $"{Str(n, "author")} · {RelativeTime.Of(Date(n, "at"), now)}" + (Date(n, "expiresAt") is { } ends && ends != default ? $" · {(ends > now ? "expira" : "expirou")} em {Day(ends)}" : "");
            col.AddChild(Muted(meta, 11)); if (Str(n, "attachmentUrl") is { Length: > 0 } link) { var open = new LinkButton { Text = "Abrir anexo", Uri = link }; col.AddChild(open); }
            _content.AddChild(card);
        }
    }

    private void OpenNewNotice()
    {
        var content = IslandUi.Column(8); var title = IslandUi.Input("Título", GroupRules.MaxAnnouncementTitle); content.AddChild(Labeled("Título", title));
        var text = IslandUi.TextArea("Mensagem", 110); content.AddChild(Labeled("Mensagem", text));
        var expiry = IslandUi.Dropdown([("Sem validade", "0"), ("1 dia", "1"), ("7 dias", "7"), ("30 dias", "30")]); content.AddChild(Labeled("Validade", expiry));
        var link = IslandUi.Input("https://… (opcional)", 400); content.AddChild(Labeled("Anexo (link)", link));
        var pinned = new CheckBox { Text = "Fixar no topo", FocusMode = Control.FocusModeEnum.None }; content.AddChild(pinned);
        Ask("Novo aviso", content, async () =>
        {
            var days = int.Parse(IslandUi.IdOf(expiry)); DateTimeOffset? ends = days > 0 ? DateTimeOffset.UtcNow.AddDays(days) : null;
            return await Do(Api.PostAnnouncement(_id, title.Text.Trim(), text.Text.Trim(), ends, link.Text.Trim(), pinned.ButtonPressed), "Aviso publicado; os membros foram avisados.");
        }, "Publicar");
    }

    // ---- Chat ----

    private async Task ShowChatAsync(int generation)
    {
        _content.AddChild(TitleRow("Chat do grupo"));
        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 280), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; scroll.SetMeta("chat", true); _content.AddChild(scroll);
        _chatList = IslandUi.Column(6); _chatList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(_chatList); _lastChat = "";
        var row = IslandUi.Row(8); _content.AddChild(row);
        _chatInput = new LineEdit { PlaceholderText = "Digite uma mensagem...", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36), MaxLength = GroupRules.MaxChatLength }; _chatInput.TextSubmitted += typed => _ = SendChatAsync(); row.AddChild(_chatInput);
        var send = IslandUi.IconButton("chat", "Enviar", 36); send.Pressed += () => _ = SendChatAsync(); row.AddChild(send);
        await RefreshChatAsync(generation);
    }

    private async Task SendChatAsync()
    {
        if (_chatInput is null) return; var text = _chatInput.Text.Trim(); if (text.Length == 0) return;
        var reply = await Api.Send(_id, text); if (!reply.Ok) { Notify(GroupWords(reply)); return; }
        _chatInput.Text = ""; await RefreshChatAsync();
    }

    public async Task RefreshChatAsync(int? generation = null)
    {
        if (_chatList is null || !IsInstanceValid(_chatList)) return;
        var reply = await Api.Chat(_id, 50); if (generation is { } g && Stale(g) || _tab != "chat" || !IsInstanceValid(_chatList)) return;
        if (!reply.Ok) return; var key = reply.Body.GetRawText(); if (key == _lastChat) return; _lastChat = key;
        foreach (var child in _chatList.GetChildren()) child.QueueFree(); var now = CenterView.Now(); var items = Items(reply.Body).ToList();
        if (items.Count == 0) _chatList.AddChild(Empty("Ninguém falou ainda. Diga olá!"));
        foreach (var m in items)
        {
            var line = IslandUi.Row(8); line.AddChild(Avatar(Str(m, "username"), 28)); var col = IslandUi.Column(0); col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; line.AddChild(col);
            var head = Muted($"{Str(m, "username")} · {RelativeTime.Of(Date(m, "at"), now)}", 11); col.AddChild(head); col.AddChild(Body(Str(m, "content"), 13)); _chatList.AddChild(line);
        }
        if (_chatList.GetParent() is ScrollContainer sc) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); sc.ScrollVertical = (int)sc.GetVScrollBar().MaxValue; }
    }

    // ---- Terrenos ----

    private async Task ShowLandsAsync(int generation)
    {
        var reply = await Api.Lands(_id); if (Stale(generation)) return; if (!reply.Ok) { _content.AddChild(Empty(GroupWords(reply))); return; }
        var lands = Items(Prop(reply.Body, "lands")).ToList(); var manage = Flag(reply.Body, "canManage");
        _content.AddChild(TitleRow("Terrenos do grupo", $"{lands.Count}", Act("Gerenciar terrenos", "map", () => ManageLand?.Invoke(_id), IslandUi.Kind.Primary, manage, manage ? "" : "Seu cargo não administra terrenos.")));
        if (lands.Count == 0) { _content.AddChild(Empty("O grupo ainda não tem terrenos. Quem administra terrenos coloca um em Gerenciar terrenos.")); return; }
        foreach (var l in lands)
        {
            var card = CardBox(); var col = IslandUi.Column(2); card.AddChild(col); var row = IslandUi.Row(10); col.AddChild(row); row.AddChild(UiStyle.IconRect("map", UiStyle.Accent, 22));
            var name = Body(Str(l, "name"), 15); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(name);
            var status = Flag(l, "forSale") ? "À venda" : Str(l, "accessMode") switch { "private" => "Restrito", "list" => "Restrito", _ => "Aberto" }; var chip = Muted(status); chip.AddThemeColorOverride("font_color", Flag(l, "forSale") ? UiStyle.Orange : UiStyle.Success); row.AddChild(chip);
            col.AddChild(Muted($"{Str(l, "islandName")} · {Long(l, "area"):N0} m² · {Long(l, "maxObjects"):N0} objetos · construção: {Str(l, "buildMode")}", 11)); _content.AddChild(card);
        }
    }

    // ---- Economia ----

    private async Task ShowEconomyAsync(int generation)
    {
        var chest = await Api.Treasury(_id); if (Stale(generation)) return; if (!chest.Ok) { _content.AddChild(Empty(GroupWords(chest))); return; }
        var canView = Flag(chest.Body, "canView"); var canTake = Flag(chest.Body, "canTake");
        _content.AddChild(TitleRow("Economia do grupo"));
        var balance = CardBox(UiStyle.Accent); var col = IslandUi.Column(4); balance.AddChild(col); _content.AddChild(balance);
        col.AddChild(Muted("Saldo do grupo")); var value = new Label { Text = canView ? Money(Long(chest.Body, "balance")) : "—" }; value.AddThemeFontSizeOverride("font_size", 28); value.AddThemeFontOverride("font", UiStyle.Medium); value.AddThemeColorOverride("font_color", UiStyle.Accent); col.AddChild(value);
        col.AddChild(Muted(canView ? $"Entrou {Money(Long(chest.Body, "totalIn"))} · Saiu {Money(Long(chest.Body, "totalOut"))}" + (Flag(chest.Body, "frozen") ? " · cofre congelado" : "") : "Seu cargo não vê o saldo. Você já contribuiu " + Money(Long(chest.Body, "myContributed")) + "."));
        var buttons = IslandUi.Row(8); col.AddChild(buttons);
        buttons.AddChild(Act("Receber", "coins", OpenContribute, IslandUi.Kind.Primary, !Flag(chest.Body, "frozen"), Flag(chest.Body, "frozen") ? "O cofre está congelado." : "Contribuir para o cofre do grupo."));
        buttons.AddChild(Act("Pagar", "coins", OpenWithdraw, IslandUi.Kind.Orange, canTake && !Flag(chest.Body, "frozen"), canTake ? "" : "Seu cargo não retira do cofre."));
        if (!canView) return;
        var ledger = await Api.Ledger(_id, 30); if (Stale(generation)) return; _content.AddChild(Heading("Histórico"));
        if (!ledger.Ok || !Items(ledger.Body).Any()) { _content.AddChild(Empty("Nenhuma movimentação ainda.")); return; }
        foreach (var line in Items(ledger.Body))
        {
            var amount = Long(line, "amount"); var type = Str(line, "type"); var income = type is "contribution" or "sale" or "join_fee"; var card = CardBox(); var row = IslandUi.Row(10); card.AddChild(row);
            var text = IslandUi.Column(0); text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(text); text.AddChild(Body(TypeText(type) + (Str(line, "note") is { Length: > 0 } n ? $": {n}" : ""), 13)); text.AddChild(Muted($"{Str(line, "actor")} · {Moment(Date(line, "at"))}", 11));
            var money = Body((income ? "+ " : "− ") + Money(amount), 14); money.AddThemeColorOverride("font_color", income ? UiStyle.SuccessBright : UiStyle.Danger); row.AddChild(money); _content.AddChild(card);
        }
    }

    private static string TypeText(string type) => type switch { "contribution" => "Contribuição", "sale" => "Venda", "withdrawal" => "Retirada", "join_fee" => "Taxa de entrada", _ => type };

    private void OpenContribute()
    {
        var content = IslandUi.Column(8); content.AddChild(Body("Quanto você quer dar ao cofre do grupo? O valor sai da sua carteira e não volta se você sair do grupo."));
        var amount = IslandUi.Number(1, 1_000_000_000, 1, " L$", 200); amount.Value = 100; content.AddChild(Labeled("Valor", amount)); var note = IslandUi.Input("Recado (opcional)", 120); content.AddChild(Labeled("Recado", note));
        Ask("Contribuir para o cofre", content, async () => await Do(Api.Contribute(_id, (long)amount.Value, note.Text.Trim()), $"Você contribuiu com {Money((long)amount.Value)}.", reload: false) && await Refresh("economia"), "Contribuir");
    }

    private void OpenWithdraw()
    {
        var content = IslandUi.Column(8); content.AddChild(Body("As moedas vão para a carteira de um membro. Toda retirada fica no histórico e precisa de um motivo."));
        var amount = IslandUi.Number(1, 1_000_000_000, 1, " L$", 200); amount.Value = 100; content.AddChild(Labeled("Valor", amount)); var reason = IslandUi.Input("Motivo (3 a 200 caracteres)", 200); content.AddChild(Labeled("Motivo", reason));
        Ask("Retirar do cofre", content, async () => await Do(Api.Withdraw(_id, (long)amount.Value, null, reason.Text.Trim()), $"{Money((long)amount.Value)} passaram para a sua carteira.", reload: false) && await Refresh("economia"), "Retirar");
    }

    private async Task<bool> Refresh(string tab) { if (_tab == tab) await ShowTabAsync(tab); return true; }

    // ---- Propostas ----

    private async Task ShowProposalsAsync(int generation)
    {
        var reply = await Api.Proposals(_id); if (Stale(generation)) return; if (!reply.Ok) { _content.AddChild(Empty(GroupWords(reply))); return; }
        var list = Items(Prop(reply.Body, "items")).ToList(); var canCreate = Can(GroupPermissions.ManageProposals);
        _content.AddChild(TitleRow("Propostas", $"{list.Count}", Act("Criar proposta", "plus", OpenNewProposal, IslandUi.Kind.Primary, canCreate, canCreate ? "" : "Seu cargo não cria propostas.")));
        if (list.Count == 0) { _content.AddChild(Empty("Nenhuma proposta disponível.")); return; }
        foreach (var p in list)
        {
            var id = Guid.Parse(Str(p, "id")); var status = Str(p, "status"); var card = CardBox(status == "open" ? UiStyle.Accent : null); card.MouseFilter = Control.MouseFilterEnum.Stop; card.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            var col = IslandUi.Column(2); card.AddChild(col); var row = IslandUi.Row(8); col.AddChild(row); var title = Body(Str(p, "title"), 15); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(title);
            var chip = Body(StatusText(status), 12); chip.AddThemeColorOverride("font_color", status switch { "open" => UiStyle.Accent, "approved" => UiStyle.SuccessBright, "rejected" => UiStyle.Danger, _ => UiStyle.TextMuted }); row.AddChild(chip);
            col.AddChild(Muted($"{Str(p, "author")} · {Int(p, "votes")} voto(s) de {Int(p, "eligible")} · prazo {Moment(Date(p, "endsAt"))}" + (Flag(p, "canVote") ? " · você ainda não votou" : ""), 11));
            card.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _ = OpenProposalAsync(id); }; _content.AddChild(card);
        }
    }

    private static string StatusText(string status) => status switch { "open" => "Em votação", "approved" => "Aprovada", "rejected" => "Rejeitada", "cancelled" => "Cancelada", _ => "Encerrada" };
    private static string CriterionText(string c) => c switch { "two_thirds" => "dois terços", "unanimous" => "unanimidade", "plurality" => "a mais votada", _ => "maioria" };

    public async Task OpenProposalAsync(Guid proposal)
    {
        var reply = await Api.Proposal(_id, proposal); if (!reply.Ok) { Notify(GroupWords(reply)); return; }
        var p = reply.Body; var options = Items(Prop(p, "options")).Select(o => o.GetString() ?? "").ToList(); var counts = Items(Prop(p, "counts")).Select(c => c.GetInt32()).ToList(); var status = Str(p, "status");
        var content = IslandUi.Column(8); content.AddChild(Body(Str(p, "description").Length > 0 ? Str(p, "description") : "Sem descrição."));
        content.AddChild(Muted($"Por {Str(p, "author")} · {StatusText(status)} · critério: {CriterionText(Str(p, "criterion"))}" + (Int(p, "quorumPercent") > 0 ? $" · quórum {Int(p, "quorumPercent")}%" : "") + $" · prazo {Moment(Date(p, "endsAt"))}"));
        var winner = p.TryGetProperty("winningOption", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : -1; var vote = IslandUi.Dropdown(options.Select((o, i) => (o, i.ToString()))); var canVote = Flag(p, "canVote");
        for (var i = 0; i < options.Count; i++)
        {
            var line = IslandUi.Row(8); var name = Body(options[i] + (i == winner ? "  ✓ venceu" : ""), 14); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; if (i == winner) name.AddThemeColorOverride("font_color", UiStyle.SuccessBright); line.AddChild(name);
            if (counts.Count > i) { var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, counts.Sum()), Value = counts[i], ShowPercentage = false, CustomMinimumSize = new Vector2(120, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; line.AddChild(bar); line.AddChild(Muted(counts[i].ToString(), 12)); }
            if (Prop(p, "myVote").ValueKind == JsonValueKind.Number && Prop(p, "myVote").GetInt32() == i) line.AddChild(Muted("seu voto", 11)); content.AddChild(line);
        }
        if (counts.Count == 0) content.AddChild(Muted($"{Int(p, "votes")} voto(s) até agora. A divisão aparece quando a votação acabar."));
        if (canVote) content.AddChild(vote);
        if (status == "open" && Can(GroupPermissions.ManageProposals)) { var manage = IslandUi.Row(8); content.AddChild(manage); manage.AddChild(Act("Encerrar agora", "check", () => Confirm("Encerrar votação", "Contar os votos agora?", () => Api.CloseProposal(_id, proposal), "Votação encerrada."))); manage.AddChild(Act("Cancelar proposta", "close", () => Confirm("Cancelar proposta", "Cancelar sem decidir nada?", () => Api.CancelProposal(_id, proposal), "Proposta cancelada."), IslandUi.Kind.Danger)); }
        Ask(Str(p, "title"), content, async () => canVote && await Do(Api.Vote(_id, proposal, int.Parse(IslandUi.IdOf(vote))), "Voto registrado."), canVote ? "Votar" : "Fechar", ok => { if (!canVote) ok.Disabled = false; });
    }

    private void OpenNewProposal()
    {
        var content = IslandUi.Column(8); var title = IslandUi.Input("Título", GroupRules.MaxProposalTitle); content.AddChild(Labeled("Título", title)); var text = IslandUi.TextArea("Descrição", 70); content.AddChild(Labeled("Descrição", text));
        var options = IslandUi.TextArea("Uma opção por linha (2 a 8)", 80); content.AddChild(Labeled("Opções de votação", options));
        var days = IslandUi.Dropdown([("1 dia", "1"), ("3 dias", "3"), ("7 dias", "7"), ("14 dias", "14"), ("30 dias", "30")]); IslandUi.Select(days, "3"); content.AddChild(Labeled("Encerra em", days));
        var rank = IslandUi.Dropdown([("Todos os membros", "0")]); foreach (var r in Items(_roles).Where(r => Str(r, "systemKey") != "member" && Str(r, "systemKey") != "owner")) { rank.AddItem($"{Str(r, "name")} e acima"); rank.SetItemMetadata(rank.ItemCount - 1, Int(r, "rank").ToString()); } content.AddChild(Labeled("Quem vota", rank));
        var quorum = IslandUi.Number(0, 100, 5, " %", 120); content.AddChild(Labeled("Quórum (quantos precisam votar)", quorum));
        var criterion = IslandUi.Dropdown([("Maioria (mais da metade)", "majority"), ("Dois terços", "two_thirds"), ("Unanimidade", "unanimous"), ("A mais votada", "plurality")]); content.AddChild(Labeled("Critério de aprovação", criterion));
        Ask("Criar proposta", content, async () =>
        {
            var lines = options.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return await Do(Api.CreateProposal(_id, title.Text.Trim(), text.Text.Trim(), lines, DateTimeOffset.UtcNow.AddDays(int.Parse(IslandUi.IdOf(days))), int.Parse(IslandUi.IdOf(rank)), (int)quorum.Value, IslandUi.IdOf(criterion)), "Proposta publicada; os membros foram avisados.");
        }, "Publicar");
    }

    // ---- Configurações ----

    private void ShowSettings()
    {
        var card = Card; var canEdit = Can(GroupPermissions.EditProfile); _content.AddChild(TitleRow("Configurações"));
        var name = IslandUi.Input("Nome do grupo", 40); name.Text = Str(card, "name"); _content.AddChild(Labeled("Nome do grupo", name));
        var tag = IslandUi.Input("Sigla (2 a 5)", 5); tag.Text = Str(card, "tag"); _content.AddChild(Labeled("Sigla", tag));
        var description = IslandUi.TextArea("Descrição", 80); description.Text = Str(card, "description"); _content.AddChild(Labeled("Descrição", description));
        var category = IslandUi.Dropdown(GroupRules.Categories.Select((c, i) => (i < CategoryNames.Length ? CategoryNames[i] : c, c))); IslandUi.Select(category, Str(card, "category")); _content.AddChild(Labeled("Categoria", category));
        var language = IslandUi.Dropdown([("Português (Brasil)", "pt-BR"), ("English (US)", "en-US"), ("Español", "es-ES"), ("Français", "fr-FR"), ("Deutsch", "de-DE"), ("日本語", "ja-JP")]); IslandUi.Select(language, Str(card, "language")); _content.AddChild(Labeled("Idioma", language));
        var rating = IslandUi.Dropdown([("Livre", "livre"), ("Moderado", "moderado"), ("Adulto", "adulto")]); IslandUi.Select(rating, Str(card, "contentRating")); _content.AddChild(Labeled("Classificação de conteúdo", rating));
        var privacy = IslandUi.Dropdown([("Público (aparece na busca)", "public"), ("Privado (só por convite)", "private")]); IslandUi.Select(privacy, Str(card, "privacy")); _content.AddChild(Labeled("Visibilidade", privacy));
        var policy = IslandUi.Dropdown([("Entrada livre", "open"), ("Por aprovação", "request"), ("Só por convite", "invite")]); IslandUi.Select(policy, Str(card, "joinPolicy")); _content.AddChild(Labeled("Tipo de entrada", policy));
        var fee = IslandUi.Number(0, GroupRules.MaxJoinFee, 10, " L$", 200); fee.Value = Long(card, "joinFee"); _content.AddChild(Labeled("Taxa de inscrição (só entrada livre)", fee));
        var limit = IslandUi.Number(2, GroupRules.MaxMemberLimit, 10, "", 140); limit.Value = Math.Max(2, Int(card, "memberLimit")); _content.AddChild(Labeled("Limite de membros", limit));
        var cover = IslandUi.Input("https://… (capa do grupo)", 400); cover.Text = Str(card, "coverUrl"); _content.AddChild(Labeled("Imagem de capa (link https)", cover));
        foreach (var field in new Control[] { name, tag, description, category, language, rating, privacy, policy, fee, limit, cover }) SetEditable(field, canEdit);
        var save = Act("Salvar alterações", "check", async () =>
        {
            var open = IslandUi.IdOf(policy) == "open"; var body = new { name = name.Text.Trim(), tag = tag.Text.Trim(), description = description.Text.Trim(), category = IslandUi.IdOf(category), language = IslandUi.IdOf(language), contentRating = IslandUi.IdOf(rating), privacy = IslandUi.IdOf(privacy), joinPolicy = IslandUi.IdOf(policy), joinFee = open ? (long)fee.Value : 0L, memberLimit = (int)limit.Value, coverUrl = cover.Text.Trim() };
            await Do(Api.Update(_id, body), "Alterações salvas.");
        }, IslandUi.Kind.Primary, canEdit, canEdit ? "" : "Seu cargo não edita o perfil do grupo."); _content.AddChild(save);
        _content.AddChild(IslandUi.Divider());
        var risk = CardBox(UiStyle.Danger); var col = IslandUi.Column(6); risk.AddChild(col); col.AddChild(Heading("Área de risco")); col.AddChild(Muted("Passar a liderança e dissolver o grupo pedem que você digite o nome exato do grupo. Dissolver não é possível enquanto o grupo tem terrenos ou moedas no cofre."));
        var row = IslandUi.Row(8); col.AddChild(row); row.AddChild(Act("Transferir propriedade", "link", () => OpenAdmin?.Invoke(_id, 11), IslandUi.Kind.Secondary, IsLeader, IsLeader ? "" : "Só o líder.")); row.AddChild(Act("Dissolver grupo", "close", () => OpenAdmin?.Invoke(_id, 11), IslandUi.Kind.Danger, IsLeader, IsLeader ? "" : "Só o líder."));
        _content.AddChild(risk);
    }

    private static void SetEditable(Control field, bool on)
    {
        switch (field) { case LineEdit l: l.Editable = on; break; case TextEdit t: t.Editable = on; break; case OptionButton o: o.Disabled = !on; break; case SpinBox s: s.Editable = on; break; }
    }
}
