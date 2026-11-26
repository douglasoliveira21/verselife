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
    /// "Carteira", as in the Godot client: the summary (available, reserved, received and spent in a period), the statement with filters and the receipt of each movement, buying VerseCoin (the packages, the ways
    /// to pay, the order and how it stands), the fees of the platform shown before buying, and sending coins to someone (the classic form). Paying happens at the payment provider; this window never handles card or
    /// bank data, it only creates the order and shows its state.
    /// </summary>
    public sealed class WalletHubWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections = { ("resumo", "Resumo"), ("extrato", "Extrato"), ("comprar", "Comprar VerseCoin"), ("taxas", "Taxas"), ("enviar", "Enviar VerseCoin") };
        public static readonly (string Id, string Text)[] Kinds = { ("", "Tudo"), ("transfer", "Transferências"), ("market", "Mercado"), ("coins", "Compras de VC"), ("property", "Imóveis"), ("adjustment", "Ajustes") };
        public static readonly (string Id, string Text)[] Methods = { ("pix", "Pix"), ("card", "Cartão"), ("mercadopago", "Mercado Pago"), ("asaas", "Asaas"), ("pagbank", "PagBank") };
        private const string E = "/api/v1/economy";
        private int _days = 30; private string _kind = "", _direction = "";
        public long Balance { get; private set; } = -1;
        /// <summary>The last order created (for the tests).</summary>
        public Guid LastOrder { get; private set; }

        public WalletHubWindow(ApiContext context) : base(context, "wallet-hub", "Carteira", Sections, 720f, 540f, new Vector2(300, 70), 160f) { Show("resumo"); }

        protected override void Fill(string section)
        {
            Get(E + "/wallet", w => { Balance = I(w, "availableCoins"); _summary.text = "Saldo: " + Coins(Balance) + (Flag(w, "isBlocked") ? "  ·  carteira bloqueada" : ""); });
            switch (section) { case "resumo": Summary(); break; case "extrato": Statement(); break; case "comprar": Buy(); break; case "taxas": Fees(); break; case "enviar": SendCoins(); break; }
        }

        /// <summary>Money in reais from cents: "R$ 19,90".</summary>
        public static string Reais(long cents) => "R$ " + (cents / 100m).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
        /// <summary>A fee in basis points as a percentage: 500 → "5%", 250 → "2,5%".</summary>
        public static string Percent(long bps) => (bps / 100m).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + "%";
        public static string KindWords(string kind) => Kinds.FirstOrDefault(k => k.Id == kind).Text ?? kind;
        public static string StatusWords(string status) => status switch { "pending" => "esperando o pagamento", "paid" or "completed" => "pago", "expired" => "venceu", "cancelled" or "canceled" => "cancelado", "failed" => "falhou", "refunded" => "devolvido", _ => status };

        public static string Words(int status, string body)
        {
            string code = ""; try { using var d = JsonDocument.Parse(body ?? ""); if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) code = e.GetString() ?? ""; } catch (JsonException) { }
            return code switch
            {
                "payments_not_configured" => "A compra de VerseCoin ainda não está ligada neste servidor.", "package_not_found" => "Esse pacote não existe mais.", "invalid_order" => "Escolha o pacote e a forma de pagar.", "wallet_blocked" => "A carteira está bloqueada.",
                _ => UiTasks.Explain(status, body),
            };
        }

        // ---- Resumo ----

        private void Summary()
        {
            Buttons(Btn("7 dias", () => { _days = 7; Show("resumo"); }), Btn("30 dias", () => { _days = 30; Show("resumo"); }), Btn("90 dias", () => { _days = 90; Show("resumo"); }));
            Get(E + "/wallet/summary?days=" + _days, s =>
            {
                Title("Últimos " + I(s, "days") + " dias"); Line("Disponível " + Coins(I(s, "available")) + " · reservado " + Coins(I(s, "reserved")) + " · total " + Coins(I(s, "total")), "lv-bold");
                Line("Recebido " + Coins(I(s, "received")) + " · gasto " + Coins(I(s, "spent")) + " · " + I(s, "count") + " movimentação(ões)" + (Flag(s, "isBlocked") ? " · carteira bloqueada" : ""), "lv-small");
                Line("Reservado é o que está guardado por um negócio em andamento (um aluguel, uma oferta) e volta se ele não se concluir.", "lv-small", "lv-muted");
            });
        }

        // ---- Extrato ----

        private void Statement()
        {
            var kind = Choice("Tipo", Kinds.ToList(), _kind); var direction = Choice("Sentido", new List<(string, string)> { ("", "Entradas e saídas"), ("in", "Só entradas"), ("out", "Só saídas") }, _direction);
            Buttons(Btn("Filtrar", () => { _kind = IdOf(kind); _direction = IdOf(direction); Show("extrato"); }, "lv-btn-primary"));
            Get(E + "/wallet/transactions?days=" + _days + (_kind.Length > 0 ? "&kind=" + _kind : "") + (_direction.Length > 0 ? "&direction=" + _direction : ""), root =>
            {
                var items = Items(root);
                foreach (var t in items)
                {
                    var id = G(t, "id"); var incoming = S(t, "direction") == "in";
                    Row(id, UiKit.Text((incoming ? "+" : "−") + Coins(Math.Abs(I(t, "amount"))), incoming ? "lv-bold" : "lv-small"), UiKit.Text(KindWords(S(t, "kind")) + (S(t, "counterparty").Length > 0 ? " · " + S(t, "counterparty") : "") + (S(t, "message").Length > 0 ? " · " + S(t, "message") : "") + " · " + When(Date(t, "createdAt")), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Recibo", () => Receipt(id)));
                }
                if (items.Count == 0) Line("Nenhuma movimentação nesse filtro.", "lv-small", "lv-muted");
            });
        }

        private void Receipt(Guid id) => Get(E + "/wallet/transactions/" + id, t =>
        {
            Reset(); Buttons(Btn("Voltar ao extrato", () => Show("extrato"))); Title("Recibo " + S(t, "code"));
            Line((S(t, "direction") == "in" ? "Entrada de " : "Saída de ") + Coins(Math.Abs(I(t, "amount"))) + " · " + KindWords(S(t, "kind")) + " (" + S(t, "type") + ")", "lv-bold");
            Line("Situação: " + StatusWords(S(t, "status")) + " · feito em " + When(Date(t, "createdAt")) + (Has(t, "completedAt") ? " · concluído em " + When(Date(t, "completedAt")) : ""), "lv-small");
            if (S(t, "counterparty").Length > 0) Line("Com: " + S(t, "counterparty"), "lv-small"); if (I(t, "fee") > 0) Line("Taxa da plataforma: " + Coins(I(t, "fee")), "lv-small"); if (S(t, "message").Length > 0) Line("Mensagem: " + S(t, "message"), "lv-small");
            Buttons(Btn("Copiar o código", () => { GUIUtility.systemCopyBuffer = S(t, "code"); Say("Código copiado.", false); }));
        });

        // ---- Comprar VerseCoin ----

        private void Buy() => Get(E + "/packages", root =>
        {
            Line("O pagamento é feito no site do meio de pagamento escolhido; o jogo nunca pede dados de cartão ou de banco. As moedas chegam quando o pagamento é confirmado.", "lv-small", "lv-muted");
            if (!Flag(root, "paymentsEnabled")) Line("A compra de VerseCoin ainda não está ligada neste servidor.", "lv-bad");
            var methods = Arr(root, "methods").Select(m => m.GetString()).Where(m => m != null).ToList(); var method = Choice("Forma de pagar", Methods.Where(m => methods.Count == 0 || methods.Contains(m.Id)).ToList(), methods.FirstOrDefault() ?? "pix");
            foreach (var p in Arr(root, "packages"))
            {
                var id = S(p, "id"); var name = S(p, "name"); var total = I(p, "totalCoins"); var price = I(p, "priceCents");
                var cells = new List<VisualElement> { UiKit.Text(name, "lv-bold"), UiKit.Text(Coins(total) + (I(p, "bonusCoins") > 0 ? " (inclui " + Coins(I(p, "bonusCoins")) + " de bônus, +" + I(p, "bonusPercent") + "%)" : "") + " · " + Reais(price), "lv-small", "lv-muted"), UiKit.Spacer() };
                if (Flag(root, "paymentsEnabled")) cells.Add(Sure("Comprar " + name, () => "Pedir " + Coins(total) + " por " + Reais(price) + " (" + method.value + ")?", () => Order(id, IdOf(method)), "lv-btn-primary"));
                Row(Guid.Empty, cells.ToArray());
            }
            if (Arr(root, "packages").Count == 0) Line("Nenhum pacote à venda.", "lv-small", "lv-muted");
            if (LastOrder != Guid.Empty) Buttons(Btn("Ver como está o último pedido", () => OrderStatus(LastOrder)));
        });

        /// <summary>Creates an order for a package (the same key is never reused, so a double press never buys twice).</summary>
        public void Order(string package, string method)
        {
            this.Then(_c.Api.ApiAsync("POST", E + "/coin-orders", JsonSerializer.Serialize(new { packageId = package, method, idempotencyKey = Guid.NewGuid().ToString("N") }, Options)), r =>
            {
                if (r.Status < 200 || r.Status >= 300) { Say(Words(r.Status, r.Body), true); return; }
                try { using var d = JsonDocument.Parse(r.Body); LastOrder = G(d.RootElement, "id"); Say("Pedido feito: " + Coins(I(d.RootElement, "coins")) + " por " + Reais(I(d.RootElement, "priceCents")) + " · " + StatusWords(S(d.RootElement, "status")) + (Has(d.RootElement, "expiresAt") ? " · vale até " + When(Date(d.RootElement, "expiresAt")) : "") + ".", false); }
                catch (JsonException) { Say("Pedido feito.", false); }
            });
        }

        public void OrderStatus(Guid id) => Get(E + "/coin-orders/" + id, o => Say("Pedido de " + Coins(I(o, "coins")) + ": " + StatusWords(S(o, "status")) + (Has(o, "paidAt") ? " em " + When(Date(o, "paidAt")) : "") + ".", false));

        // ---- Taxas ----

        private void Fees() => Get(E + "/fees", f =>
        {
            Title("Taxas da plataforma"); Line("Mercado: " + Percent(I(f, "marketplaceBps")) + " do preço de cada venda.", "lv-bold"); Line("Venda de objetos no mundo: " + Percent(I(f, "objectSaleBps")) + " do preço.", "lv-bold");
            Line("Quem compra paga o preço anunciado; a taxa sai do valor que o vendedor recebe. Transferências entre pessoas não têm taxa.", "lv-small", "lv-muted");
        });

        // ---- Enviar VerseCoin ----

        private void SendCoins()
        {
            Line("Para mandar VerseCoin a alguém (com confirmação e a checagem do saldo), use o formulário de envio.", "lv-small", "lv-muted");
            Buttons(Btn("Abrir o envio de VerseCoin…", () => new WalletWindow(_c).Open(), "lv-btn-primary"));
        }
    }
}
