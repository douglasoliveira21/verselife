using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Gerenciamento de Terrenos", as in the Godot client: the island's staff look after its land in ten sections (Visão Geral, Mapa de Parcelas, Demarcação, Lista de Terrenos, Vendas, Aluguéis, Contratos,
    /// Prims e Recursos, Permissões, Histórico). Everything comes from <c>/land/overview</c> (owner and administrators only); each parcel opens in "Sobre este Terreno" for the rest.
    /// </summary>
    public sealed class LandManagementWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections =
        {
            ("visao", "Visão Geral"), ("mapa", "Mapa de Parcelas"), ("demarcacao", "Demarcação"), ("lista", "Lista de Terrenos"), ("vendas", "Vendas"), ("alugueis", "Aluguéis"), ("contratos", "Contratos"),
            ("prims", "Prims e Recursos"), ("permissoes", "Permissões"), ("historico", "Histórico"),
        };
        public static readonly (string Id, string Text)[] Categories = { ("residencial", "Residencial"), ("comercial", "Comercial"), ("mista", "Mista"), ("social", "Social"), ("comum", "Área comum") };
        public static readonly (string Id, string Text)[] Kinds = { ("", "Todos"), ("available", "Livres"), ("sale", "À venda"), ("rented", "Alugados"), ("sold", "Vendidos"), ("common", "Área comum") };
        private readonly Guid _island; private readonly Func<Vector3?> _where;
        /// <summary>The grid of the map and of the demarcation (for the tests: painted cells, chosen cells).</summary>
        public CellGrid Grid { get; private set; }

        private string Land => "/api/v1/worlds/islands/" + _island + "/land";

        public LandManagementWindow(ApiContext context, Guid island, Func<Vector3?> where = null) : base(context, "land-management", "Gerenciamento de Terrenos", Sections, 880f, 560f, new Vector2(180, 60))
        {
            _island = island; _where = where; Show("visao");
        }

        protected override void Fill(string section)
        {
            switch (section)
            {
                case "visao": Overview(); break; case "mapa": Map(); break; case "demarcacao": Claim(); break; case "lista": List(); break; case "vendas": Sales(); break;
                case "alugueis": Rentals(); break; case "contratos": Contracts(); break; case "prims": Prims(); break; case "permissoes": Groups(); break; case "historico": History(); break;
            }
        }

        // ---- words ----

        public static string KindWords(string kind) => kind switch { "available" => "livre", "sale" => "à venda", "rented" => "alugado", "sold" => "vendido", "common" => "área comum", _ => kind };
        public static string CategoryWords(string category) => Categories.FirstOrDefault(c => c.Id == category).Text ?? category;
        public static Color KindColor(string kind) => kind switch { "sale" => new Color(.96f, .64f, .25f), "rented" => new Color(.6f, .45f, .9f), "sold" => new Color(.3f, .55f, .95f), "common" => new Color(.35f, .75f, .4f), _ => new Color(.45f, .5f, .55f) };
        public static string ContractWords(string status) => status switch { "active" => "em vigor", "grace" => "em atraso (prazo de tolerância)", "ended" => "encerrado", "expired" => "vencido", "cancelled" => "cancelado", _ => status };
        public static string WhatWords(string what) => what switch
        {
            "claim" => "demarcou", "split" => "dividiu", "expand" => "ampliou", "shrink" => "reduziu", "merge" => "juntou", "category" => "mudou a finalidade", "image" => "mudou a imagem", "rename" => "renomeou", "sale" or "sold" => "venda",
            "sale_offer" => "pôs à venda", "sale_cancel" => "tirou da venda", "rent" => "aluguel", "rent_offer" => "pôs para alugar", "rent_cancel" => "tirou do aluguel", "renew" => "renovou o aluguel", "rental_end" or "end" => "encerrou o aluguel",
            "transfer" => "transferiu", "access_add" => "deu acesso", "access_remove" => "tirou acesso", "eject" => "expulsou", "options" => "mudou as opções", "media" => "mudou a música", "environment" => "mudou o ambiente", "arrival" => "mudou a chegada",
            "access_mode" => "mudou quem entra", "group_access_granted" => "deu acesso a um grupo", "group_access_revoked" => "tirou o acesso de um grupo", "deposit" => "depósito", "payment" => "pagamento", "refund" => "devolução",
            _ => what,
        };

        /// <summary>A refusal of the land routes in words (the API answers with a code).</summary>
        public static string Words(int status, string body)
        {
            string code = ""; try { using var d = JsonDocument.Parse(body ?? ""); if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) code = e.GetString() ?? ""; } catch (JsonException) { }
            return code switch
            {
                "overlap" => "Essa área já pertence a outro terreno.", "outside_island" => "A área passa dos limites da ilha.", "too_big" => "A área é grande demais para um terreno.", "empty" => "Marque algum quadrado no mapa.",
                "has_objects" => "Há objetos nessa área. Retire-os antes.", "budget_exceeded" => "A ilha não tem tantos objetos livres. Use um limite menor.", "bad_category" => "Finalidade inválida.", "forbidden" => "Você não tem permissão para isso nesta ilha.",
                "owner_only" => "Só o dono da ilha pode fazer isso.", "person_not_found" => "Não encontrei ninguém com esse nome.", "for_sale" => "Retire da venda antes.", "insufficient_balance" => "Saldo insuficiente na carteira.", "wallet_blocked" => "A carteira está bloqueada.",
                "transfer_pending" => "Há uma transferência esperando resposta.", "no_transfer" => "Não há transferência esperando você aqui.", "transfer_expired" => "O prazo dessa transferência acabou.", "common_area" => "Área comum não pode ser transferida.",
                "group_owned" => "Terreno de grupo não é transferido por uma pessoa só.", "bad_buyer" => "Escolha outra pessoa que não seja você.", "price_changed" => "O preço mudou: confira e tente de novo.", "not_for_sale" => "O terreno não está mais à venda.",
                "not_for_rent" => "O terreno não está mais para alugar.", "own_parcel" => "Este terreno já é seu.", "bad_periods" => "Número de períodos fora do que o dono aceita.", "bad_mode" => "Escolha um modo válido.", "bad_kind" => "Escolha um tipo válido.",
                "bad_point" => "O ponto de chegada precisa ficar dentro do terreno.", "too_many_grants" => "Grupos demais com acesso a este terreno.", "bad_target" => "O grupo dono do terreno já tem todo o acesso.", "not_https" => "O endereço precisa começar com https://.",
                "bad_type" => "O endereço precisa levar direto a um arquivo .mp3 ou .ogg.", "bad_host" => "Esse endereço não é permitido.", "bad_scope" => "Escolha o que devolver.", "bad_name" => "Dê um nome ao terreno.", "rented" => "O terreno está alugado.",
                _ => UiTasks.Explain(status, body),
            };
        }

        /// <summary>The cells of a claim as the API takes them, or a reason when it cannot be sent.</summary>
        public static string ClaimProblem(string name, int cells, string maxObjects, out int? max)
        {
            max = null; if ((name ?? "").Trim().Length < 2) return "Dê um nome ao terreno (pelo menos 2 letras)."; if (cells == 0) return "Marque no mapa os quadrados do terreno.";
            if (!string.IsNullOrWhiteSpace(maxObjects)) { if (!int.TryParse(maxObjects.Trim(), out var m) || m < 0) return "Limite de objetos: um número (ou vazio)."; max = m; }
            return null;
        }

        private void Summarize(JsonElement root)
        {
            var island = Obj(root, "island"); var t = Obj(root, "totals");
            _summary.text = S(island, "name") + "  ·  " + I(t, "parcels") + " terrenos  ·  " + D(t, "areaFree").ToString("N0", Pt) + " m² livres de " + D(t, "area").ToString("N0", Pt) + "  ·  aluguéis: " + Coins(I(t, "monthlyRevenue")) + " por mês";
        }

        private void WithOverview(Action<JsonElement> fill) => Get(Land + "/overview", root => { Summarize(root); fill(root); });

        private void About(Guid parcel) => new ParcelInfoWindow(_c, _island, parcel, _where).Open();

        // ---- Visão Geral ----

        private void Overview() => WithOverview(root =>
        {
            var t = Obj(root, "totals"); var island = Obj(root, "island");
            Title(S(island, "name")); Line("Dono: " + S(island, "owner") + "  ·  " + I(island, "size") + " m de lado  ·  " + (Flag(island, "isPublic") ? "pública" : "privada"), "lv-small", "lv-muted");
            Title("Área"); Line(D(t, "areaParcelled").ToString("N0", Pt) + " m² em terrenos  ·  " + D(t, "areaFree").ToString("N0", Pt) + " m² livres  ·  " + D(t, "area").ToString("N0", Pt) + " m² no total");
            Title("Terrenos"); Line(I(t, "parcels") + " no total  ·  " + I(t, "available") + " livres  ·  " + I(t, "onSale") + " à venda  ·  " + I(t, "rented") + " alugados  ·  " + I(t, "sold") + " vendidos  ·  " + I(t, "common") + " de área comum");
            Title("Dinheiro"); Line("Aluguéis: " + Coins(I(t, "monthlyRevenue")) + " por mês  ·  " + I(t, "expiringSoon") + " contrato(s) vencendo em 7 dias");
            Title("Objetos"); Line(I(t, "objectsUsed").ToString("N0", Pt) + " usados  ·  " + I(t, "objectsShared").ToString("N0", Pt) + " reservados aos terrenos  ·  limite da ilha " + I(t, "objectsLimit").ToString("N0", Pt));
            Buttons(Btn("Demarcar um terreno", () => Show("demarcacao"), "lv-btn-primary"), Btn("Ver o mapa", () => Show("mapa")), Btn("Ver a lista", () => Show("lista")));
        });

        // ---- Mapa de Parcelas ----

        private void Map() => Get(Land, land => WithOverview(root =>
        {
            Grid = new CellGrid(); Grid.Build((int)Math.Max(1, I(land, "size") > 0 ? I(land, "size") : I(Obj(root, "island"), "size")), (int)(I(land, "cellSize") > 0 ? I(land, "cellSize") : 4));
            var parcels = Arr(root, "parcels"); Grid.SetRegions(parcels.Select(p => (CellGrid.RunsOf(p), KindColor(S(p, "kind"))))); _page.Add(Grid);
            Buttons(Kinds.Skip(1).Select(k => (VisualElement)Legend(k.Text, KindColor(k.Id))).ToArray());
            foreach (var p in parcels) { var id = G(p, "id"); Row(id, UiKit.Text(S(p, "code") + " · " + S(p, "name"), "lv-bold"), UiKit.Text(KindWords(S(p, "kind")) + " · " + S(p, "owner"), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Sobre", () => About(id))); }
            if (parcels.Count == 0) Line("Ainda não há terrenos: use Demarcação.", "lv-small", "lv-muted");
        }));

        private static VisualElement Legend(string text, Color color)
        {
            var dot = new VisualElement(); dot.style.width = 10; dot.style.height = 10; dot.style.backgroundColor = color; dot.style.marginRight = 4; dot.style.marginLeft = 8;
            var row = UiKit.Row(dot, UiKit.Text(text, "lv-small")); row.style.alignItems = Align.Center; return row;
        }

        // ---- Demarcação ----

        private void Claim() => Get(Land, land => WithOverview(root =>
        {
            Line("Toque ou arraste no mapa para marcar os quadrados do novo terreno (cinza: já são de algum terreno).", "lv-small", "lv-muted");
            Grid = new CellGrid(); Grid.Build((int)(I(land, "size") > 0 ? I(land, "size") : 256), (int)(I(land, "cellSize") > 0 ? I(land, "cellSize") : 4)); Grid.SetRegions(Arr(root, "parcels").Select(p => (CellGrid.RunsOf(p), new Color(.4f, .42f, .45f)))); _page.Add(Grid);
            var name = Field("Nome do terreno", "", 60); var about = Field("Descrição", "", 400); var max = Field("Limite de objetos (vazio = o padrão)", "", 6);
            Buttons(Btn("Demarcar", () =>
            {
                var problem = ClaimProblem(name.value, Grid.Chosen, max.value, out var limit); if (problem != null) { Say(problem, true); return; }
                Send("POST", Land + "/claim", new { name = name.value.Trim(), description = about.value, cells = Grid.Cells, maxObjects = limit }, "Terreno demarcado.", () => Show("lista"), Words);
            }, "lv-btn-primary"), Btn("Limpar a marcação", () => Grid.ClearChosen()));
        }));

        // ---- Lista de Terrenos ----

        private void List(string kind = "") => WithOverview(root =>
        {
            Reset(); var bar = new List<VisualElement>(); foreach (var (id, text) in Kinds) { var k = id; bar.Add(Btn(text, () => List(k), k == kind ? "lv-btn-primary" : "lv-btn-flat")); } Buttons(bar.ToArray());
            var search = Field("Buscar por nome ou código", "", 40); var rows = new VisualElement(); _page.Add(rows);
            search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) Filter(); }, TrickleDown.TrickleDown);
            void Filter()
            {
                rows.Clear(); RowIds.Clear(); var q = search.value.Trim();
                foreach (var p in Arr(root, "parcels").Where(p => kind.Length == 0 || S(p, "kind") == kind).Where(p => q.Length == 0 || S(p, "name").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || S(p, "code").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var id = G(p, "id"); RowIds.Add(id);
                    var r = UiKit.Row(UiKit.Text(S(p, "code") + " · " + S(p, "name"), "lv-bold"), UiKit.Text(CategoryWords(S(p, "category")) + " · " + KindWords(S(p, "kind")) + " · " + S(p, "owner") + (S(p, "tenant").Length > 0 ? " (alugado a " + S(p, "tenant") + ")" : "") + " · " + D(p, "area").ToString("N0", Pt) + " m² · " + I(p, "objects") + "/" + I(p, "maxObjects") + " objetos", "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Sobre", () => About(id)));
                    r.AddToClassList("lv-row"); rows.Add(r);
                }
                if (rows.childCount == 0) rows.Add(UiKit.Text("Nenhum terreno aqui.", "lv-small", "lv-muted"));
            }
            Filter(); Actions["Buscar"] = Filter;
        });

        // ---- Vendas ----

        private void Sales() => WithOverview(root =>
        {
            var parcels = Arr(root, "parcels"); Title("À venda");
            foreach (var p in parcels.Where(p => Has(p, "salePrice")))
            {
                var id = G(p, "id"); Row(id, UiKit.Text(S(p, "code") + " · " + S(p, "name"), "lv-bold"), UiKit.Text(Coins(I(p, "salePrice")) + (Flag(p, "saleExclusive") ? " · só para uma pessoa" : "") + " · " + D(p, "area").ToString("N0", Pt) + " m²", "lv-small", "lv-muted"), UiKit.Spacer(),
                    Btn("Sobre", () => About(id)), Sure("Tirar da venda", () => "Confirmar", () => Send("DELETE", Land + "/parcels/" + id + "/sale", null, "Anúncio de venda retirado.", Sales, Words)));
            }
            if (!parcels.Any(p => Has(p, "salePrice"))) Line("Nenhum terreno à venda.", "lv-small", "lv-muted");
            Offer(parcels.Where(p => !Has(p, "salePrice") && S(p, "kind") != "rented" && S(p, "kind") != "common").ToList(), false);
        });

        private void Offer(List<JsonElement> candidates, bool rent)
        {
            Title(rent ? "Anunciar um aluguel" : "Anunciar uma venda"); if (candidates.Count == 0) { Line("Nenhum terreno disponível para anunciar.", "lv-small", "lv-muted"); return; }
            var pick = Choice("Terreno", candidates.Select(p => (S(p, "id"), S(p, "code") + " · " + S(p, "name"))).ToList(), S(candidates[0], "id"));
            Buttons(Btn(rent ? "Anunciar o aluguel…" : "Anunciar a venda…", () => { var id = Guid.Parse(IdOf(pick)); new ListingOfferWindow(_c, rent, false, _island, id, pick.value, () => Show(Section)).Open(); }, "lv-btn-primary"));
        }

        // ---- Aluguéis ----

        private void Rentals() => WithOverview(root =>
        {
            var parcels = Arr(root, "parcels"); Title("Alugados");
            foreach (var p in parcels.Where(p => S(p, "kind") == "rented"))
            {
                var id = G(p, "id"); var rental = G(p, "rentalId");
                Row(id, UiKit.Text(S(p, "code") + " · " + S(p, "name"), "lv-bold"), UiKit.Text(S(p, "tenant") + " · " + ContractWords(S(p, "rentStatus")) + " · até " + When(Date(p, "rentExpiresAt")), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Sobre", () => About(id)),
                    Sure("Encerrar o aluguel", () => "Encerrar o contrato de " + S(p, "tenant") + "?", () => Send("POST", Land + "/rentals/" + rental + "/end", "{}", "Aluguel encerrado.", Rentals, Words)));
            }
            if (!parcels.Any(p => S(p, "kind") == "rented")) Line("Nenhum terreno alugado.", "lv-small", "lv-muted");
            Title("Para alugar");
            foreach (var p in parcels.Where(p => Has(p, "rentPrice") && S(p, "kind") != "rented"))
            {
                var id = G(p, "id"); Row(id, UiKit.Text(S(p, "code") + " · " + S(p, "name"), "lv-bold"), UiKit.Text(Coins(I(p, "rentPrice")) + " a cada " + I(p, "rentPeriodDays") + " dias", "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Sobre", () => About(id)),
                    Sure("Tirar do aluguel", () => "Confirmar", () => Send("DELETE", Land + "/parcels/" + id + "/rental-offer", null, "Anúncio de aluguel retirado.", Rentals, Words)));
            }
            if (!parcels.Any(p => Has(p, "rentPrice") && S(p, "kind") != "rented")) Line("Nenhum anúncio de aluguel.", "lv-small", "lv-muted");
            Offer(parcels.Where(p => !Has(p, "rentPrice") && S(p, "kind") != "rented" && S(p, "kind") != "common" && !Has(p, "salePrice")).ToList(), true);
        });

        // ---- Contratos ----

        private void Contracts() => WithOverview(root =>
        {
            var contracts = Arr(root, "contracts"); Line(contracts.Count(c => S(c, "status") is "active" or "grace") + " em vigor  ·  " + contracts.Count + " nos últimos 90 dias", "lv-small", "lv-muted");
            foreach (var c in contracts)
            {
                var id = G(c, "id"); var live = S(c, "status") is "active" or "grace";
                var cells = new List<VisualElement> { UiKit.Text(S(c, "code") + " · " + S(c, "parcel"), "lv-bold"), UiKit.Text(S(c, "landlord") + " → " + S(c, "tenant") + " · " + Coins(I(c, "price")) + " / " + I(c, "periodDays") + " dias · " + ContractWords(S(c, "status")) + " · " + When(Date(c, "startsAt")) + " a " + When(Date(c, "expiresAt")) + (Flag(c, "autoRenew") ? " · renova sozinho" : "") + (I(c, "deposit") > 0 ? " · caução " + Coins(I(c, "deposit")) : ""), "lv-small", "lv-muted"), UiKit.Spacer() };
                if (live) cells.Add(Sure("Encerrar", () => "Encerrar este contrato?", () => Send("POST", Land + "/rentals/" + id + "/end", "{}", "Contrato encerrado.", Contracts, Words)));
                Row(id, cells.ToArray());
            }
            if (contracts.Count == 0) Line("Nenhum contrato ainda.", "lv-small", "lv-muted");
        });

        // ---- Prims e Recursos ----

        private void Prims() => WithOverview(root =>
        {
            var t = Obj(root, "totals"); Line(I(t, "objectsUsed").ToString("N0", Pt) + " objetos usados de " + I(t, "objectsLimit").ToString("N0", Pt) + " da ilha  ·  " + I(t, "objectsShared").ToString("N0", Pt) + " reservados aos terrenos", "lv-bold");
            foreach (var p in Arr(root, "parcels").OrderByDescending(p => I(p, "objects")))
            {
                var id = G(p, "id"); var share = I(p, "maxObjects") > 0 ? I(p, "objects") * 100 / I(p, "maxObjects") : 0;
                Row(id, UiKit.Text(S(p, "code") + " · " + S(p, "name"), "lv-bold"), UiKit.Text(I(p, "objects") + "/" + I(p, "maxObjects") + " objetos (" + share + "%) · " + I(p, "cost") + "/" + I(p, "maxCost") + " prims" + (Flag(p, "scripts") ? " · scripts" : "") + " · constrói: " + BuildWords(S(p, "buildMode")), "lv-small", share >= 90 ? "lv-bad" : "lv-muted"), UiKit.Spacer(), Btn("Sobre", () => About(id)));
            }
        });

        public static string BuildWords(string mode) => mode switch { "owner" => "só o dono", "list" => "a lista", "everyone" => "todos", "nobody" => "ninguém", _ => mode };

        // ---- Permissões (acesso de grupo) ----

        private void Groups(Guid? chosen = null) => WithOverview(root =>
        {
            var parcels = Arr(root, "parcels"); if (parcels.Count == 0) { Line("Ainda não há terrenos.", "lv-small", "lv-muted"); return; }
            Line("Abra um terreno a todos os membros de um grupo: \"entrar\" deixa entrar quando o terreno tem lista; \"construir\" deixa também construir. Pessoas, uma a uma, ficam em Sobre › Acesso.", "lv-small", "lv-muted");
            var current = chosen ?? G(parcels[0], "id");
            var pick = Choice("Terreno", parcels.Select(p => (S(p, "id"), S(p, "code") + " · " + S(p, "name"))).ToList(), current.ToString()); pick.RegisterValueChangedCallback(_ => Groups(Guid.Parse(IdOf(pick))));
            var area = new VisualElement(); _page.Add(area);
            GroupAccess(area, Land + "/parcels/" + current, () => Groups(current));
        });

        // ---- Histórico ----

        private void History(string kind = "") => WithOverview(root =>
        {
            Reset(); Buttons(Btn("Tudo", () => History("")), Btn("Mudanças", () => History("change")), Btn("Dinheiro", () => History("money")));
            var rows = Arr(root, "history").Where(h => kind.Length == 0 || S(h, "kind") == kind).ToList();
            foreach (var h in rows) Row(Guid.Empty, UiKit.Text(When(Date(h, "at")), "lv-small", "lv-muted"), UiKit.Text(S(h, "actor"), "lv-bold"), UiKit.Text(WhatWords(S(h, "what")) + " · " + S(h, "code") + " " + S(h, "parcel") + (Has(h, "amount") ? " · " + Coins(I(h, "amount")) : ""), "lv-small"));
            if (rows.Count == 0) Line("Nada ainda.", "lv-small", "lv-muted");
        });
    }
}
