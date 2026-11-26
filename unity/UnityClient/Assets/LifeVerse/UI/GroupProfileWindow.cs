using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using LifeVerse.Shared.Groups;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The profile of a group, as in the Godot client, in ten tabs: Geral (who leads, how one enters, join or leave), Membros (requests, invites, role, mute, kick, ban), Cargos (ranks and titles), Permissões (what each
    /// role may do), Avisos (pinned notices with a deadline), Chat, Terrenos (land given to the group, give one, give one back), Economia (the chest: give, take out, freeze, statement), Propostas (votes) and
    /// Configurações (the profile of the group, notifications, the bans, the history, passing the leadership and dissolving). What a member sees and may do follows their role (the API decides).
    /// </summary>
    public sealed class GroupProfileWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections =
        {
            ("geral", "Geral"), ("membros", "Membros"), ("cargos", "Cargos"), ("permissoes", "Permissões"), ("avisos", "Avisos"), ("chat", "Chat"), ("terrenos", "Terrenos"), ("economia", "Economia"), ("propostas", "Propostas"), ("configuracoes", "Configurações"),
        };
        public static readonly (string Id, string Text)[] Criteria = { ("majority", "Maioria simples"), ("absolute", "Maioria absoluta (mais da metade de quem pode votar)"), ("two_thirds", "Dois terços") };
        private readonly Guid _group; private JsonElement _view;
        public Guid Group => _group;
        private string P => "/api/v1/groups/" + _group;

        public GroupProfileWindow(ApiContext context, Guid group) : base(context, "group-" + group.ToString("N").Substring(0, 8), "Grupo", Sections, 800f, 560f, new Vector2(300, 60), 150f) { _group = group; Show("geral"); }

        protected override void Fill(string section) => Get(P, view =>
        {
            _view = view.Clone(); var card = Obj(view, "card");
            _summary.text = S(card, "name") + (S(card, "tag").Length > 0 ? " [" + S(card, "tag") + "]" : "") + "  ·  " + I(card, "members") + " membros  ·  " + (Flag(card, "isMember") ? "você é " + S(view, "myRole") : "você não é membro");
            switch (section)
            {
                case "geral": General(view, card); break; case "membros": Members(); break; case "cargos": Roles(false); break; case "permissoes": Roles(true); break; case "avisos": Announcements(); break;
                case "chat": Chat(); break; case "terrenos": Lands(); break; case "economia": Economy(); break; case "propostas": Proposals(); break; case "configuracoes": Settings(view, card); break;
            }
        });

        // ---- what the person may do ----
        public bool Can(string permission) => Flag(_view, "isLeader") || Arr(_view, "myPermissions").Any(p => p.ValueKind == JsonValueKind.String && p.GetString() == permission);
        private bool IsMember => Flag(Obj(_view, "card"), "isMember");
        private void Change(string method, string path, object body, string done, string reload) => Send(method, path, body, done, () => Show(reload), GroupsHubWindow.Words);

        // ---- Geral ----

        private void General(JsonElement view, JsonElement card)
        {
            Title(S(card, "name")); if (S(card, "description").Length > 0) Line(S(card, "description"));
            Line("Líder: " + S(view, "leaderName") + " · criado em " + When(Date(view, "createdAt")) + " · " + GroupsHubWindow.CategoryWords(S(card, "category")) + " · " + (S(card, "privacy") == "private" ? "privado" : "público") + " · " + GroupsHubWindow.PolicyWords(S(card, "joinPolicy")).ToLowerInvariant() + (I(card, "joinFee") > 0 ? " · taxa " + Coins(I(card, "joinFee")) : ""), "lv-small", "lv-muted");
            Line(I(card, "members") + " de " + I(card, "memberLimit") + " membros" + (S(card, "title").Length > 0 ? " · seu título: " + S(card, "title") : ""), "lv-small");
            if (IsMember)
            {
                Line("Seu cargo: " + S(view, "myRole") + (Date(view, "mutedUntil") is { } m && m > DateTimeOffset.UtcNow ? " · silenciado até " + When(m) : ""), "lv-small");
                Buttons(Flag(card, "isActive") ? Btn("Deixar de usar como ativo", () => Change("DELETE", "/api/v1/groups/active", null, "Nenhum grupo ativo agora.", "geral")) : Btn("Usar como grupo ativo", () => Change("POST", P + "/activate", "{}", S(card, "name") + " é o seu grupo ativo.", "geral"), "lv-btn-primary"),
                    Sure("Sair do grupo", () => "Sair de " + S(card, "name") + "?", () => Change("POST", P + "/leave", "{}", "Você saiu do grupo.", "geral")));
            }
            else if (Flag(card, "requestPending")) Buttons(Btn("Cancelar o pedido", () => Change("DELETE", P + "/join", null, "Pedido cancelado.", "geral")));
            else if (!Flag(card, "banned") && S(card, "joinPolicy") != "invite") Buttons(Btn(S(card, "joinPolicy") == "open" ? "Entrar" : "Pedir para entrar", () => Change("POST", P + "/join", new { message = "", confirmFee = I(card, "joinFee") > 0 ? I(card, "joinFee") : (long?)null }, "Pedido feito.", "geral"), "lv-btn-primary"));
        }

        // ---- Membros ----

        private void Members() => Get(P + "/members", members =>
        {
            var list = Items(members); Title(list.Count + " membro(s)");
            if (Can(GroupPermissions.Invite)) { var who = Field("Convidar pelo nome", "", 32); Buttons(Btn("Convidar", () => { if (who.value.Trim().Length == 0) { Say("Digite o nome da pessoa.", true); return; } Change("POST", P + "/invites", new { username = who.value.Trim(), message = "", roleId = (Guid?)null }, "Convite enviado a " + who.value.Trim() + ".", "membros"); }, "lv-btn-primary")); }
            var roles = new List<(string Id, string Text)>();
            foreach (var m in list)
            {
                var user = G(m, "userId"); var name = S(m, "username"); var cells = new List<VisualElement> { UiKit.Text(name, "lv-bold"), UiKit.Text(S(m, "roleName") + " · desde " + When(Date(m, "joinedAt")) + (Date(m, "mutedUntil") is { } mu && mu > DateTimeOffset.UtcNow ? " · silenciado" : ""), "lv-small", "lv-muted"), UiKit.Spacer() };
                if (user != _c.Api.UserId)
                {
                    if (Can(GroupPermissions.Mute)) cells.Add(Btn("Silenciar " + name, () => Change("POST", P + "/members/" + user + "/mute", new { minutes = 60, reason = "" }, name + " silenciado por 1 hora.", "membros")));
                    if (Can(GroupPermissions.Kick)) cells.Add(Sure("Expulsar " + name, () => "Expulsar " + name + "?", () => Change("POST", P + "/members/" + user + "/kick", new { reason = "" }, name + " saiu do grupo.", "membros")));
                    if (Can(GroupPermissions.Ban)) cells.Add(Sure("Banir " + name, () => "Banir " + name + "?", () => Change("POST", P + "/members/" + user + "/ban", new { reason = "", days = (int?)null }, name + " foi banido.", "membros")));
                }
                Row(user, cells.ToArray());
            }
            if (Can(GroupPermissions.AssignRoles)) Get(P + "/roles", rolesRoot =>
            {
                Title("Mudar o cargo de alguém"); var options = Items(rolesRoot).Where(r => S(r, "systemKey") != "leader").Select(r => (S(r, "id"), S(r, "name"))).ToList(); if (options.Count == 0 || list.Count == 0) return;
                var person = Choice("Pessoa", list.Select(m => (S(m, "userId"), S(m, "username"))).ToList(), S(list[0], "userId")); var role = Choice("Cargo", options, options[0].Item1);
                Buttons(Btn("Dar o cargo", () => Change("PUT", P + "/members/" + IdOf(person) + "/role", new { roleId = Guid.Parse(IdOf(role)) }, "Cargo mudado.", "membros")));
            });
            if (Can(GroupPermissions.ApproveRequests)) Get(P + "/requests", requests =>
            {
                var items = Items(requests); if (items.Count == 0) return; Title("Pedidos para entrar (" + items.Count + ")");
                foreach (var r in items) { var id = G(r, "id"); var name = S(r, "username"); Row(id, UiKit.Text(name, "lv-bold"), UiKit.Text(S(r, "message"), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Aprovar " + name, () => Change("POST", P + "/requests/" + id + "/approve", "{}", name + " entrou.", "membros"), "lv-btn-primary"), Btn("Recusar " + name, () => Change("POST", P + "/requests/" + id + "/deny", "{}", "Pedido recusado.", "membros"))); }
            });
        });

        // ---- Cargos and Permissões ----

        private void Roles(bool permissions) => Get(P + "/roles", root =>
        {
            var roles = Items(root).OrderByDescending(r => I(r, "rank")).ToList(); var manage = Can(GroupPermissions.ManageRoles);
            if (!permissions)
            {
                foreach (var r in roles)
                {
                    var id = G(r, "id"); var name = S(r, "name"); var cells = new List<VisualElement> { UiKit.Text(name, "lv-bold"), UiKit.Text("nível " + I(r, "rank") + " · " + I(r, "members") + " pessoa(s)" + (S(r, "title").Length > 0 ? " · título " + S(r, "title") : ""), "lv-small", "lv-muted"), UiKit.Spacer() };
                    if (manage && Flag(r, "canEdit") && S(r, "systemKey").Length == 0) cells.Add(Sure("Apagar o cargo " + name, () => "Apagar o cargo " + name + "?", () => Change("DELETE", P + "/roles/" + id, null, "Cargo apagado.", "cargos")));
                    Row(id, cells.ToArray());
                }
                if (!manage) return;
                Title("Novo cargo"); var newName = Field("Nome do cargo", "", 32); var rank = Field("Nível (1 a 99; maior manda mais)", "10", 2);
                Buttons(Btn("Criar o cargo", () => { if (!GroupRules.ValidRoleName(newName.value, out var clean)) { Say("O nome do cargo tem de 2 a 32 letras.", true); return; } if (!int.TryParse(rank.value, out var n) || n < 1 || n > GroupRules.MaxCustomRank) { Say("Nível de 1 a 99.", true); return; } Change("POST", P + "/roles", new { name = clean, rank = n, permissions = new string[0] }, "Cargo criado.", "cargos"); }, "lv-btn-primary"));
                Title("Título de um cargo"); var which = Choice("Cargo", roles.Select(r => (S(r, "id"), S(r, "name"))).ToList(), roles.Count > 0 ? S(roles[0], "id") : ""); var title = Field("Título (até 24 letras, aparece ao lado do nome)", "", GroupRules.MaxRoleTitle);
                Buttons(Btn("Salvar o título", () => Change("PUT", P + "/roles/" + IdOf(which) + "/title", new { title = title.value.Trim() }, "Título salvo.", "cargos")));
                return;
            }
            Line("O que cada cargo pode fazer. O líder pode tudo.", "lv-small", "lv-muted");
            foreach (var r in roles)
            {
                var id = G(r, "id"); var name = S(r, "name"); var has = Arr(r, "permissions").Select(p => p.GetString()).ToHashSet(); var editable = manage && Flag(r, "canEdit") && S(r, "systemKey") != "leader";
                Title(name + " (nível " + I(r, "rank") + ")");
                var boxes = new List<(string Key, Toggle Box)>(); var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; _page.Add(grid);
                foreach (var (key, label, help) in GroupPermissions.All) { var t = new Toggle(label) { value = has.Contains(key), tooltip = help }; t.style.width = 260; t.SetEnabled(editable); grid.Add(t); boxes.Add((key, t)); Toggles[name + ":" + key] = t; }
                if (editable) Buttons(Btn("Salvar as permissões de " + name, () => Change("PUT", P + "/roles/" + id, new { name, rank = I(r, "rank"), permissions = boxes.Where(b => b.Box.value).Select(b => b.Key).ToArray(), color = S(r, "color") }, "Permissões de " + name + " salvas.", "permissoes")));
            }
        });

        // ---- Avisos ----

        private void Announcements() => Get(P + "/announcements", root =>
        {
            if (Can(GroupPermissions.Announce))
            {
                var title = Field("Título do aviso", "", GroupRules.MaxAnnouncementTitle); var body = Field("Texto", "", GroupRules.MaxAnnouncementBody); var days = Field("Some depois de quantos dias (vazio = nunca)", "", 3); var pinned = Check("Fixar no alto", false);
                Buttons(Btn("Publicar o aviso", () =>
                {
                    if (title.value.Trim().Length == 0 || body.value.Trim().Length == 0) { Say("Escreva o título e o texto.", true); return; }
                    var until = IslandManagementWindow.ExpiryIn(days.value, DateTimeOffset.UtcNow, out var ok); if (!ok) { Say("Dias: um número de 1 a 3650, ou vazio.", true); return; }
                    Change("POST", P + "/announcements", new { title = title.value.Trim(), body = body.value.Trim(), pinned = pinned.value, expiresAt = until, attachmentUrl = "" }, "Aviso publicado.", "avisos");
                }, "lv-btn-primary"));
            }
            var items = Items(root);
            foreach (var a in items)
            {
                var id = G(a, "id"); var cells = new List<VisualElement> { UiKit.Text((Flag(a, "pinned") ? "📌 " : "") + S(a, "title"), "lv-bold"), UiKit.Text(S(a, "body") + " · " + S(a, "author") + " · " + When(Date(a, "at")) + (Date(a, "expiresAt") is { } e ? " · até " + When(e) : ""), "lv-small", "lv-muted"), UiKit.Spacer() };
                if (Can(GroupPermissions.Announce)) cells.Add(Sure("Apagar o aviso", () => "Apagar este aviso?", () => Change("DELETE", P + "/announcements/" + id, null, "Aviso apagado.", "avisos")));
                Row(id, cells.ToArray());
            }
            if (items.Count == 0) Line("Nenhum aviso.", "lv-small", "lv-muted");
        });

        // ---- Chat ----

        private void Chat() => Get(P + "/chat?take=50", root =>
        {
            foreach (var m in Items(root)) Row(G(m, "id"), UiKit.Text(S(m, "username") + ":", "lv-bold"), UiKit.Text(S(m, "content"), "lv-small"), UiKit.Spacer(), UiKit.Text(When(Date(m, "at")), "lv-small", "lv-muted"));
            if (Items(root).Count == 0) Line("Ninguém escreveu ainda.", "lv-small", "lv-muted");
            if (!IsMember) return; var text = Field("Mensagem", "", GroupRules.MaxChatLength);
            Buttons(Btn("Enviar", () => { if (text.value.Trim().Length == 0) return; Change("POST", P + "/chat", new { content = text.value.Trim(), clientMessageId = Guid.NewGuid().ToString("N") }, null, "chat"); }, "lv-btn-primary"));
        });

        // ---- Terrenos ----

        private void Lands() => Get(P + "/lands", root =>
        {
            var lands = Items(root); Title("Terrenos do grupo (" + lands.Count + ")");
            foreach (var l in lands)
            {
                var id = G(l, "parcelId"); var name = S(l, "name"); var cells = new List<VisualElement> { UiKit.Text(name, "lv-bold"), UiKit.Text(S(l, "islandName") + " · " + D(l, "area").ToString("N0", Pt) + " m² · até " + I(l, "maxObjects") + " objetos · constrói: " + LandManagementWindow.BuildWords(S(l, "buildMode")), "lv-small", "lv-muted"), UiKit.Spacer(),
                    Btn("Sobre " + name, () => new ParcelInfoWindow(_c, G(l, "islandId"), id).Open()) };
                if (Can(GroupPermissions.ReleaseLand)) cells.Add(Sure("Devolver " + name, () => "Devolver " + name + " para você?", () => Change("POST", P + "/lands/" + id + "/release", new { userId = (Guid?)null }, name + " deixou de ser do grupo.", "terrenos")));
                Row(id, cells.ToArray());
            }
            if (lands.Count == 0) Line("O grupo ainda não tem terrenos.", "lv-small", "lv-muted");
            if (IsMember) Get(P + "/lands/available", available =>
            {
                var mine = Items(available); if (mine.Count == 0) return; Title("Dar um terreno seu ao grupo");
                var pick = Choice("Terreno", mine.Select(l => (S(l, "parcelId"), S(l, "name") + " (" + S(l, "islandName") + ")")).ToList(), S(mine[0], "parcelId"));
                Buttons(Sure("Dar ao grupo", () => "O terreno passa a ser do grupo. Confirmar?", () => Change("POST", P + "/lands", new { parcelId = Guid.Parse(IdOf(pick)) }, "Terreno dado ao grupo.", "terrenos"), "lv-btn-primary"));
            });
        });

        // ---- Economia ----

        /// <summary>A number of VerseCoin typed in a field: whole, from 1 to the most one operation may move. Null when it is not.</summary>
        public static long? AmountOf(string text) => long.TryParse((text ?? "").Trim().Replace(".", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= GroupRules.MaxCoinsPerOperation ? n : (long?)null;

        private void Economy() => Get(P + "/treasury", t =>
        {
            Title("Baú do grupo");
            if (Flag(t, "canView")) Line("Saldo " + Coins(I(t, "balance")) + " · entrou " + Coins(I(t, "totalIn")) + " · saiu " + Coins(I(t, "totalOut")) + (Flag(t, "frozen") ? " · CONGELADO" : ""), "lv-bold");
            else Line("Seu cargo não vê o saldo do baú." + (Flag(t, "frozen") ? " O baú está congelado." : ""), "lv-small", "lv-muted");
            Line("Você já deu " + Coins(I(t, "myContributed")) + ".", "lv-small");
            foreach (var g in Arr(t, "top")) Line("• " + S(g, "name") + ": " + Coins(I(g, "total")), "lv-small", "lv-muted");
            if (IsMember) { var amount = Field("Quanto dar (VC)", "", 12); var note = Field("Recado", "", 120); Buttons(Sure("Dar ao baú", () => "Dar " + amount.value + " VC ao baú do grupo?", () => { if (AmountOf(amount.value) is not { } n) { Say("Digite um valor em VC.", true); return; } Change("POST", P + "/treasury/contribute", new { amount = n, note = note.value.Trim(), idempotencyKey = Guid.NewGuid().ToString() }, Coins(n) + " dados ao baú.", "economia"); }, "lv-btn-primary")); }
            if (Flag(t, "canTake")) { var take = Field("Quanto retirar (VC)", "", 12); var why = Field("Motivo", "", 120); Buttons(Sure("Retirar do baú", () => "Retirar " + take.value + " VC do baú?", () => { if (AmountOf(take.value) is not { } n) { Say("Digite um valor em VC.", true); return; } if (why.value.Trim().Length == 0) { Say("Diga o motivo.", true); return; } Change("POST", P + "/treasury/withdraw", new { amount = n, userId = (Guid?)null, reason = why.value.Trim(), idempotencyKey = Guid.NewGuid().ToString() }, Coins(n) + " retirados.", "economia"); })); }
            if (Flag(t, "canFreeze")) Buttons(Sure(Flag(t, "frozen") ? "Descongelar o baú" : "Congelar o baú", () => "Confirmar?", () => Change("PUT", P + "/treasury/frozen", new { frozen = !Flag(t, "frozen") }, Flag(t, "frozen") ? "Baú descongelado." : "Baú congelado.", "economia")));
            if (Flag(t, "canView")) Get(P + "/treasury/ledger?take=30", ledger =>
            {
                Title("Extrato"); foreach (var e in Items(ledger)) Row(G(e, "id"), UiKit.Text(When(Date(e, "at")), "lv-small", "lv-muted"), UiKit.Text((I(e, "amount") >= 0 ? "+" : "") + Coins(I(e, "amount")), "lv-bold"), UiKit.Text(S(e, "type") + " · " + S(e, "actor") + (S(e, "target").Length > 0 ? " → " + S(e, "target") : "") + (S(e, "note").Length > 0 ? " · " + S(e, "note") : "") + " · saldo " + Coins(I(e, "balanceAfter")), "lv-small"));
                if (Items(ledger).Count == 0) Line("Nada no extrato ainda.", "lv-small", "lv-muted");
            });
        });

        // ---- Propostas ----

        /// <summary>The checks of a new proposal: title, two to eight options, a deadline of 1 hour to 30 days. Null when fine.</summary>
        public static string ProposalProblem(string title, string[] options, int hours)
        {
            if ((title ?? "").Trim().Length == 0 || title.Length > GroupRules.MaxProposalTitle) return "O título vai de 1 a " + GroupRules.MaxProposalTitle + " letras.";
            var clean = (options ?? new string[0]).Select(o => o.Trim()).Where(o => o.Length > 0).ToArray(); if (clean.Length < GroupRules.MinProposalOptions || clean.Length > GroupRules.MaxProposalOptions) return "Uma proposta precisa de 2 a 8 opções.";
            if (clean.Any(o => o.Length > GroupRules.MaxOptionLength)) return "Cada opção tem até 80 letras."; if (clean.Distinct(StringComparer.OrdinalIgnoreCase).Count() != clean.Length) return "As opções não podem se repetir.";
            if (hours < 1 || hours > GroupRules.MaxProposalDays * 24) return "O prazo vai de 1 hora a 30 dias."; return null;
        }

        private void Proposals() => Get(P + "/proposals?take=50", root =>
        {
            foreach (var p in Items(root))
            {
                var id = G(p, "id"); var options = Arr(p, "options").Select(o => o.GetString()).ToList(); var counts = Arr(p, "counts").Select(c => c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0).ToList(); var open = S(p, "status") == "open";
                Title(S(p, "title")); Line((S(p, "description").Length > 0 ? S(p, "description") + " · " : "") + "por " + S(p, "author") + " · " + (open ? "aberta até " + When(Date(p, "endsAt")) : "encerrada") + " · " + I(p, "votes") + " de " + I(p, "eligible") + " votaram · " + (Criteria.FirstOrDefault(c => c.Id == S(p, "criterion")).Text ?? S(p, "criterion")), "lv-small", "lv-muted");
                var cells = new List<VisualElement>();
                for (var i = 0; i < options.Count; i++)
                {
                    var option = i; var label = options[i] + (counts.Count > i ? " (" + counts[i] + ")" : "") + (Has(p, "myVote") && I(p, "myVote") == i ? " ✓" : "") + (Has(p, "winningOption") && I(p, "winningOption") == i ? " 🏆" : "");
                    cells.Add(open && Flag(p, "canVote") && !Has(p, "myVote") ? Btn("Votar: " + options[i], () => Change("POST", P + "/proposals/" + id + "/vote", new { option }, "Voto dado.", "propostas")) : (VisualElement)UiKit.Text(label, "lv-small"));
                }
                if (open && Flag(p, "canManage")) { cells.Add(Sure("Encerrar a votação", () => "Encerrar agora?", () => Change("POST", P + "/proposals/" + id + "/close", "{}", "Votação encerrada.", "propostas"))); cells.Add(Sure("Cancelar a proposta", () => "Cancelar a proposta?", () => Change("DELETE", P + "/proposals/" + id, null, "Proposta cancelada.", "propostas"))); }
                Buttons(cells.ToArray()); RowIds.Add(id);
            }
            if (Items(root).Count == 0) Line("Nenhuma proposta.", "lv-small", "lv-muted");
            if (!Can(GroupPermissions.ManageProposals)) return;
            Title("Nova proposta"); var title = Field("Título", "", GroupRules.MaxProposalTitle); var about = Field("Descrição", "", GroupRules.MaxProposalBody); var optionsField = Field("Opções (uma por vírgula)", "Sim, Não", 600);
            var hoursField = Field("Prazo em horas", "72", 3); var criterion = Choice("Como se decide", Criteria.ToList(), "majority");
            Buttons(Btn("Abrir a votação", () =>
            {
                var options = optionsField.value.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).ToArray(); var hours = int.TryParse(hoursField.value, out var h) ? h : 0;
                var problem = ProposalProblem(title.value, options, hours); if (problem != null) { Say(problem, true); return; }
                Change("POST", P + "/proposals", new { title = title.value.Trim(), description = about.value.Trim(), options, endsAt = DateTimeOffset.UtcNow.AddHours(hours), minRank = 0, quorumPercent = 0, criterion = IdOf(criterion) }, "Votação aberta.", "propostas");
            }, "lv-btn-primary"));
        });

        // ---- Configurações ----

        private void Settings(JsonElement view, JsonElement card)
        {
            if (IsMember) { var muted = Check("Silenciar os avisos deste grupo", Flag(view, "notificationsMuted")); Buttons(Btn("Salvar os avisos", () => Change("PUT", P + "/notification-settings", new { muted = muted.value }, muted.value ? "Avisos do grupo silenciados." : "Avisos do grupo ligados.", "configuracoes"))); }
            if (Can(GroupPermissions.EditProfile))
            {
                Title("Perfil do grupo"); var name = Field("Nome", S(card, "name"), 40); var about = Field("Descrição", S(card, "description"), 1000); var tag = Field("Etiqueta", S(card, "tag"), 5);
                var policy = Choice("Como se entra", GroupsHubWindow.Policies.ToList(), S(card, "joinPolicy")); var category = Choice("Categoria", GroupsHubWindow.Categories.ToList(), S(card, "category")); var limit = Field("Limite de membros", I(card, "memberLimit").ToString(), 5); var color = Field("Cor (#RRGGBB)", S(card, "accentColor"), 7);
                Buttons(Btn("Salvar o perfil", () =>
                {
                    if (!GroupRules.ValidName(name.value, out var clean)) { Say("O nome precisa ter de 3 a 40 letras.", true); return; } if (!GroupRules.ValidTag(tag.value.Trim().ToUpperInvariant())) { Say("A etiqueta tem de 2 a 5 letras ou números.", true); return; } if (!GroupRules.ValidColour(color.value.Trim())) { Say("A cor é #RRGGBB.", true); return; }
                    if (!int.TryParse(limit.value, out var l) || l < 2 || l > GroupRules.MaxMemberLimit) { Say("Limite de 2 a " + GroupRules.MaxMemberLimit + ".", true); return; }
                    Change("PUT", P, new { name = clean, description = about.value, imageUrl = S(card, "imageUrl"), privacy = S(card, "privacy"), joinPolicy = IdOf(policy), accentColor = color.value.Trim(), emblem = S(card, "emblem"), tag = tag.value.Trim().ToUpperInvariant(), memberLimit = l, category = IdOf(category), language = S(card, "language"), contentRating = S(card, "contentRating"), coverUrl = S(card, "coverUrl"), joinFee = I(card, "joinFee") }, "Perfil do grupo salvo.", "configuracoes");
                }, "lv-btn-primary"));
            }
            if (Can(GroupPermissions.Ban)) Get(P + "/bans", bans =>
            {
                var list = Items(bans); if (list.Count == 0) return; Title("Banidos (" + list.Count + ")");
                foreach (var b in list) { var user = G(b, "userId"); var who = S(b, "username"); Row(user, UiKit.Text(who, "lv-bold"), UiKit.Text(S(b, "reason") + " · " + When(Date(b, "at")) + (Date(b, "expiresAt") is { } e ? " · até " + When(e) : ""), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Tirar o banimento de " + who, () => Change("DELETE", P + "/bans/" + user, null, "Banimento retirado.", "configuracoes"))); }
            });
            if (Can(GroupPermissions.ViewAudit)) Get(P + "/audit?take=30", audit =>
            {
                Title("Histórico do grupo"); foreach (var a in Items(audit)) Line(When(Date(a, "at")) + " · " + S(a, "actor") + " · " + S(a, "action") + (S(a, "target").Length > 0 ? " → " + S(a, "target") : ""), "lv-small");
            });
            if (!Flag(view, "isLeader")) return;
            Title("Liderança"); Line("Para passar a liderança ou desfazer o grupo, digite o nome do grupo exatamente.", "lv-small", "lv-muted");
            var confirm = Field("Nome do grupo (para confirmar)", "", 40); var heir = Field("Novo líder (nome da pessoa, para passar a liderança)", "", 32);
            Buttons(Sure("Passar a liderança", () => "Passar a liderança para " + heir.value.Trim() + "?", () => Get(P + "/members", members =>
            {
                var m = Items(members).FirstOrDefault(x => string.Equals(S(x, "username"), heir.value.Trim(), StringComparison.OrdinalIgnoreCase)); if (m.ValueKind != JsonValueKind.Object) { Say("Essa pessoa não é membro do grupo.", true); return; }
                Change("POST", P + "/transfer", new { userId = G(m, "userId"), confirm = confirm.value }, "Liderança passada.", "geral");
            })), Sure("Desfazer o grupo", () => "Desfazer o grupo para sempre?", () => Change("POST", P + "/dissolve", new { userId = (Guid?)null, confirm = confirm.value }, "Grupo desfeito.", "geral")));
        }
    }
}
