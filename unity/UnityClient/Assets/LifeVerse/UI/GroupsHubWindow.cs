using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LifeVerse.Shared.Groups;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Grupos", as in the Godot client: the person's groups (with the active group, whose tag and title show beside the name), discovering groups with filters (category, language, how one enters, only free), the
    /// invites, the notices of the groups, and creating a group in four steps (Informações, Entrada, Configurações, Confirmação). Each group opens in <see cref="GroupProfileWindow"/>.
    /// </summary>
    public sealed class GroupsHubWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections = { ("meus", "Meus grupos"), ("descobrir", "Descobrir"), ("convites", "Convites"), ("avisos", "Avisos dos grupos"), ("criar", "Criar grupo") };
        public static readonly (string Id, string Text)[] Categories = { ("social", "Social"), ("construcao", "Construção"), ("comercio", "Comércio"), ("jogos", "Jogos"), ("educacao", "Educação"), ("musica", "Música"), ("arte", "Arte"), ("eventos", "Eventos"), ("outros", "Outros") };
        public static readonly (string Id, string Text)[] Languages = { ("pt-BR", "Português (Brasil)"), ("en", "English"), ("es", "Español") };
        public static readonly (string Id, string Text)[] Policies = { ("open", "Entrada livre"), ("request", "Entrada por aprovação"), ("invite", "Só por convite") };
        public static readonly (string Id, string Text)[] Ratings = { ("livre", "Livre"), ("moderado", "Moderado"), ("adulto", "Adulto") };
        public static readonly string[] Steps = { "Informações", "Entrada", "Configurações", "Confirmação" };
        private const string G = "/api/v1/groups";
        private string _q = "", _category = "", _language = "", _policy = ""; private bool _free;
        /// <summary>The group being created, step by step.</summary>
        public readonly Draft Creating = new Draft();
        public int Step { get; private set; }
        /// <summary>The id of the group just created (for the tests).</summary>
        public Guid Created { get; private set; }

        public sealed class Draft { public string Name = "", Tag = "", Description = "", Category = "social", Language = "pt-BR", Privacy = "public", Policy = "request", Rating = "livre", Cover = ""; public long Fee; public int Limit = GroupRules.DefaultMemberLimit; }

        public GroupsHubWindow(ApiContext context) : base(context, "groups-hub", "Grupos", Sections, 780f, 540f, new Vector2(260, 70), 170f) { Show("meus"); }

        protected override void Fill(string section)
        {
            Active();
            switch (section) { case "meus": Mine(); break; case "descobrir": Discover(); break; case "convites": Invites(); break; case "avisos": Notices(); break; case "criar": Create(); break; }
        }

        private void Active() => this.Then(_c.Api.ApiAsync("GET", G + "/active"), r =>
        {
            if (r.Status == 200 && r.Body.Trim().StartsWith("{")) try { using var d = JsonDocument.Parse(r.Body); _summary.text = "Grupo ativo: " + S(d.RootElement, "groupName") + (S(d.RootElement, "tag").Length > 0 ? " [" + S(d.RootElement, "tag") + "]" : "") + (S(d.RootElement, "title").Length > 0 ? " · " + S(d.RootElement, "title") : ""); return; } catch (JsonException) { }
            _summary.text = "Nenhum grupo ativo: escolha um em Meus grupos para mostrar a etiqueta dele ao lado do seu nome.";
        });

        // ---- words ----

        public static string PolicyWords(string policy) => Policies.FirstOrDefault(p => p.Id == policy).Text ?? policy;
        public static string CategoryWords(string category) => Categories.FirstOrDefault(c => c.Id == category).Text ?? category;

        /// <summary>A refusal of the group routes in words (the API answers with a code).</summary>
        public static string Words(int status, string body)
        {
            string code = ""; try { using var d = JsonDocument.Parse(body ?? ""); if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) code = e.GetString() ?? ""; } catch (JsonException) { }
            return code switch
            {
                "already_member" => "Você já está nesse grupo.", "banned" => "Você foi banido desse grupo.", "blocked" => "Não dá: há um bloqueio entre vocês.", "invite_only" => "Esse grupo só aceita por convite.", "name_taken" => "Já existe um grupo com esse nome.",
                "tag_taken" => "Essa etiqueta já é de outro grupo.", "bad_name" => "O nome precisa ter de 3 a 40 letras.", "bad_tag" => "A etiqueta tem de 2 a 5 letras ou números.", "bad_description" => "A descrição é longa demais.", "forbidden" => "Seu cargo não permite isso.",
                "outranked" => "Você não pode agir sobre alguém de cargo igual ou maior.", "leader_cannot_leave" => "O líder não pode sair: passe a liderança antes (Configurações).", "confirmation_needed" => "Digite o nome do grupo exatamente para confirmar.",
                "fee_required" => "Esse grupo cobra uma taxa de entrada: confirme o valor.", "insufficient_balance" or "insufficient_funds" => "Saldo insuficiente.", "wallet_blocked" => "A carteira está bloqueada.", "frozen" => "O baú do grupo está congelado.",
                "too_many_groups_created" => "Você já criou o máximo de grupos.", "too_many_memberships" => "Você já está no máximo de grupos.", "too_many_requests" => "Pedidos demais esperando resposta.", "too_many_roles" => "O grupo já tem o máximo de cargos.",
                "too_many_pinned" => "Já há avisos fixados demais.", "too_many_proposals" => "Já há propostas abertas demais.", "already_voted" => "Você já votou.", "not_eligible" => "Seu cargo não vota nessa proposta.", "not_open" => "A votação já terminou.",
                "muted" => "Você está silenciado no grupo.", "rate_limited" => "Devagar: muitas ações seguidas.", "system_role" => "Esse cargo é do sistema e não pode ser mudado assim.", "cannot_grant" => "Você não pode dar uma permissão que não tem.",
                "bad_options" => "Uma proposta precisa de 2 a 8 opções.", "bad_deadline" => "O prazo vai de 1 hora a 30 dias.", "bad_amount" => "Valor inválido.", "over_limit" => "Passa do limite do baú por hoje.", "too_many_parcels" => "O grupo já tem terrenos demais.",
                "rented" => "O terreno está alugado.", "not_found" => "Não encontrado (talvez já tenha sido apagado).",
                _ => UiTasks.Explain(status, body),
            };
        }

        /// <summary>The check of a step of "Criar grupo": null when it can go on.</summary>
        public static string StepProblem(int step, Draft d)
        {
            switch (step)
            {
                case 0:
                    if (!GroupRules.ValidName(d.Name, out _)) return "O nome precisa ter de 3 a 40 letras.";
                    if (!GroupRules.ValidTag(d.Tag.ToUpperInvariant())) return "A etiqueta tem de 2 a 5 letras ou números (ou fica vazia).";
                    if ((d.Description ?? "").Length > 1000) return "A descrição passa de 1000 letras."; return null;
                case 1:
                    if (d.Privacy == "private" && d.Policy != "invite") return "Grupo privado só entra por convite.";
                    if (d.Fee < 0 || d.Fee > GroupRules.MaxJoinFee || d.Policy != "open" && d.Fee > 0) return "A taxa de entrada só vale na entrada livre (até " + GroupRules.MaxJoinFee.ToString("N0") + " VC)."; return null;
                case 2:
                    if (d.Limit < 2 || d.Limit > GroupRules.MaxMemberLimit) return "O limite de membros vai de 2 a " + GroupRules.MaxMemberLimit + "."; if (!GroupRules.ValidImageUrl(d.Cover)) return "A capa precisa ser um endereço https."; return null;
                default: return null;
            }
        }

        // ---- cards ----

        private void Card(JsonElement card, bool mine)
        {
            var id = G(card, "id"); var name = S(card, "name");
            var line = (S(card, "tag").Length > 0 ? "[" + S(card, "tag") + "] " : "") + CategoryWords(S(card, "category")) + " · " + I(card, "members") + "/" + I(card, "memberLimit") + " membros · " + (S(card, "privacy") == "private" ? "privado" : "público") + " · " + PolicyWords(S(card, "joinPolicy")).ToLowerInvariant() + (I(card, "joinFee") > 0 ? " · taxa " + Coins(I(card, "joinFee")) : "");
            var cells = new List<VisualElement> { UiKit.Text((Flag(card, "isActive") ? "★ " : "") + name, "lv-bold"), UiKit.Text(line, "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Abrir " + name, () => new GroupProfileWindow(_c, id).Open()) };
            if (mine) cells.Add(Flag(card, "isActive") ? Btn("Deixar de usar", () => Send("DELETE", G + "/active", null, "Nenhum grupo ativo agora.", () => Show(Section), Words)) : Btn("Usar como ativo: " + name, () => Send("POST", G + "/" + id + "/activate", "{}", name + " é o seu grupo ativo.", () => Show(Section), Words)));
            else if (Flag(card, "isMember")) cells.Add(UiKit.Text("membro", "lv-small", "lv-accent"));
            else if (Flag(card, "requestPending")) cells.Add(Btn("Cancelar pedido", () => Send("DELETE", G + "/" + id + "/join", null, "Pedido cancelado.", () => Show(Section), Words)));
            else if (!Flag(card, "banned") && S(card, "joinPolicy") != "invite") cells.Add(Btn(S(card, "joinPolicy") == "open" ? "Entrar em " + name : "Pedir para entrar em " + name, () => Join(id, name, null), "lv-btn-primary"));
            Row(id, cells.ToArray());
        }

        /// <summary>Enters (or asks to enter) a group; when it charges a fee the API answers with it and the person confirms that exact value.</summary>
        public void Join(Guid id, string name, long? fee)
        {
            this.Then(_c.Api.ApiAsync("POST", G + "/" + id + "/join", JsonSerializer.Serialize(new { message = "", confirmFee = fee }, Options)), r =>
            {
                if (r.Status >= 200 && r.Status < 300) { var done = r.Body.Contains("\"requested\":true") ? "Pedido enviado a " + name + "." : "Você entrou em " + name + "."; _c.Notices.Show(done, NoticeKind.Good); Show(Section); Say(done, false); return; }
                if (r.Body.Contains("fee_required")) try
                {
                    using var d = JsonDocument.Parse(r.Body); var asked = I(d.RootElement, "fee"); if (asked == 0) asked = I(Obj(d.RootElement, "detail"), "fee");
                    Say(name + " cobra " + Coins(asked) + " para entrar.", false); Buttons(Sure("Pagar " + Coins(asked) + " e entrar", () => "Pagar " + Coins(asked) + " para entrar em " + name + "?", () => Join(id, name, asked), "lv-btn-primary")); return;
                } catch (JsonException) { }
                Say(Words(r.Status, r.Body), true);
            });
        }

        // ---- sections ----

        private void Mine() => Get(G + "/mine", root =>
        {
            var cards = Items(root); foreach (var c in cards) Card(c, true);
            if (cards.Count == 0) Line("Você ainda não está em nenhum grupo. Procure em Descobrir ou crie um.", "lv-small", "lv-muted");
        });

        private void Discover(int page = 0)
        {
            var q = Field("Buscar grupos", _q, 60); var category = Choice("Categoria", new[] { ("", "Todas") }.Concat(Categories).ToList(), _category); var language = Choice("Idioma", new[] { ("", "Todos") }.Concat(Languages).ToList(), _language);
            var policy = Choice("Entrada", new[] { ("", "Qualquer") }.Concat(Policies).ToList(), _policy); var free = Check("Só os sem taxa de entrada", _free);
            Buttons(Btn("Buscar", () => { _q = q.value.Trim(); _category = IdOf(category); _language = IdOf(language); _policy = IdOf(policy); _free = free.value; Show("descobrir"); }, "lv-btn-primary"));
            Get(G + "/search?q=" + Uri.EscapeDataString(_q) + "&page=" + page + "&category=" + Uri.EscapeDataString(_category) + "&language=" + Uri.EscapeDataString(_language) + "&policy=" + Uri.EscapeDataString(_policy) + "&free=" + (_free ? "true" : "false"), root =>
            {
                var cards = Items(root); Title(cards.Count + " grupo(s)"); foreach (var c in cards) Card(c, false); if (cards.Count == 0) Line("Nenhum grupo com esses filtros.", "lv-small", "lv-muted");
            });
        }

        private void Invites() => Get("/api/v1/social/groups/invites", root =>
        {
            var items = Items(root);
            foreach (var i in items)
            {
                var id = G(i, "inviteId") != Guid.Empty ? G(i, "inviteId") : G(i, "id"); var name = S(i, "groupName");
                Row(id, UiKit.Text(name, "lv-bold"), UiKit.Text("convite de " + (S(i, "fromUsername").Length > 0 ? S(i, "fromUsername") : S(i, "from")) + (S(i, "message").Length > 0 ? " · " + S(i, "message") : ""), "lv-small", "lv-muted"), UiKit.Spacer(),
                    Btn("Aceitar " + name, () => Send("POST", "/api/v1/social/groups/invites/" + id + "/accept", "{}", "Você entrou em " + name + ".", () => Show("convites"), Words), "lv-btn-primary"), Btn("Recusar " + name, () => Send("POST", "/api/v1/social/groups/invites/" + id + "/decline", "{}", "Convite recusado.", () => Show("convites"), Words)));
            }
            if (items.Count == 0) Line("Nenhum convite.", "lv-small", "lv-muted");
        });

        private void Notices() => Get(G + "/notifications?take=100", root =>
        {
            var items = Arr(root, "items"); _summary.text = I(root, "unread") + " aviso(s) não lido(s)";
            Buttons(Btn("Marcar tudo como lido", () => Send("POST", G + "/notifications/read", new { ids = (Guid[])null, groupId = (Guid?)null }, "Avisos lidos.", () => Show("avisos"), Words)));
            foreach (var n in items) Row(G(n, "id"), UiKit.Text((Flag(n, "read") ? "" : "● ") + S(n, "groupName"), "lv-bold"), UiKit.Text(S(n, "title") + (S(n, "body").Length > 0 ? " — " + S(n, "body") : "") + " · " + When(Date(n, "at")), "lv-small", "lv-muted"));
            if (items.Count == 0) Line("Nenhum aviso dos seus grupos.", "lv-small", "lv-muted");
        });

        // ---- Criar grupo ----

        private void Create()
        {
            var d = Creating; Buttons(Steps.Select((s, i) => (VisualElement)UiKit.Text((i == Step ? "● " : i < Step ? "✓ " : "○ ") + s, i == Step ? "lv-bold" : "lv-small")).ToArray());
            switch (Step)
            {
                case 0: { var name = Field("Nome do grupo", d.Name, 40); var tag = Field("Etiqueta (2 a 5 letras, opcional)", d.Tag, 5); var about = Field("Descrição", d.Description, 1000); Next(() => { d.Name = name.value.Trim(); d.Tag = tag.value.Trim().ToUpperInvariant(); d.Description = about.value; }); break; }
                case 1:
                {
                    var privacy = Choice("Quem vê o grupo", new List<(string, string)> { ("public", "Público: aparece na busca"), ("private", "Privado: só por convite") }, d.Privacy); var policy = Choice("Como se entra", Policies.ToList(), d.Policy); var fee = Field("Taxa de entrada (VC, só na entrada livre)", d.Fee.ToString(), 9);
                    Next(() => { d.Privacy = IdOf(privacy); d.Policy = d.Privacy == "private" ? "invite" : IdOf(policy); d.Fee = long.TryParse(fee.value.Trim(), out var f) ? f : -1; }); break;
                }
                case 2:
                {
                    var category = Choice("Categoria", Categories.ToList(), d.Category); var language = Choice("Idioma", Languages.ToList(), d.Language); var rating = Choice("Classificação", Ratings.ToList(), d.Rating);
                    var limit = Field("Limite de membros", d.Limit.ToString(), 5); var cover = Field("Capa (https, opcional)", d.Cover, 300);
                    Next(() => { d.Category = IdOf(category); d.Language = IdOf(language); d.Rating = IdOf(rating); d.Limit = int.TryParse(limit.value.Trim(), out var l) ? l : 0; d.Cover = cover.value.Trim(); }); break;
                }
                default:
                {
                    Title("Confira"); Line(d.Name + (d.Tag.Length > 0 ? " [" + d.Tag + "]" : "") + " · " + CategoryWords(d.Category) + " · " + Languages.First(l => l.Id == d.Language).Text); Line((d.Privacy == "private" ? "Privado" : "Público") + " · " + PolicyWords(d.Policy) + (d.Policy == "open" ? d.Fee > 0 ? " · taxa " + Coins(d.Fee) : " · grátis" : "") + " · até " + d.Limit + " membros");
                    if (d.Description.Length > 0) Line(d.Description, "lv-small", "lv-muted"); Line("Criar um grupo não custa nada.", "lv-small", "lv-muted");
                    Buttons(Btn("Voltar", () => { Step--; Show("criar"); }), Btn("Criar o grupo", CreateNow, "lv-btn-primary")); break;
                }
            }
        }

        private void Next(Action read)
        {
            var buttons = new List<VisualElement>(); if (Step > 0) buttons.Add(Btn("Voltar", () => { read(); Step--; Show("criar"); }));
            buttons.Add(Btn("Continuar", () => { read(); var problem = StepProblem(Step, Creating); if (problem != null) { Say(problem, true); return; } Step++; Show("criar"); }, "lv-btn-primary")); Buttons(buttons.ToArray());
        }

        public void CreateNow()
        {
            var d = Creating; for (var i = 0; i < 3; i++) if (StepProblem(i, d) is { } problem) { Step = i; Show("criar"); Say(problem, true); return; }
            this.Then(_c.Api.ApiAsync("POST", G, JsonSerializer.Serialize(new { name = d.Name.Trim(), description = d.Description.Trim(), tag = d.Tag.Length == 0 ? null : d.Tag, category = d.Category, language = d.Language, privacy = d.Privacy, joinPolicy = d.Policy, joinFee = d.Policy == "open" ? d.Fee : 0, memberLimit = d.Limit, contentRating = d.Rating, coverUrl = d.Cover.Length == 0 ? null : d.Cover }, Options)), r =>
            {
                if (r.Status < 200 || r.Status >= 300) { Say(Words(r.Status, r.Body), true); return; }
                try { using var doc = JsonDocument.Parse(r.Body); var root = doc.RootElement; Created = G(root, "id") != Guid.Empty ? G(root, "id") : G(Obj(root, "card"), "id"); } catch (JsonException) { }
                _c.Notices.Show("Grupo " + d.Name + " criado.", NoticeKind.Good); var name = d.Name; Step = 0; Creating.Name = Creating.Tag = Creating.Description = Creating.Cover = ""; Creating.Fee = 0;
                Show("meus"); Say("Grupo " + name + " criado.", false); if (Created != Guid.Empty) new GroupProfileWindow(_c, Created).Open();
            });
        }
    }
}
