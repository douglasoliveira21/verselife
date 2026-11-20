using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The window that opens when the person clicks an object that is for sale: what it is (the name, whose it is), what is sold (the original, a copy or what is inside), the price in VerseCoin and the person's balance.
    /// "Comprar" asks the island, which charges the price that is on the object at that moment: if the owner changed it, nothing is charged and the window says so. The answer (bought, refused and why) comes back here.
    /// </summary>
    public sealed class PurchaseWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly BuildEditor _b; private readonly WorldObjectState _state; private readonly Label _owner, _what, _price, _balance, _status; private readonly Button _buy;
        public long Price { get; }
        public string SaleType { get; }
        public long Balance { get; private set; } = -1;
        public bool Done { get; private set; }
        public string StatusText => _status.text;
        public string OwnerName => _owner.text;
        public bool CanBuy => _buy.enabledSelf;

        public static string SaleLabel(string type) => type == "copy" ? "uma cópia do objeto" : type == "contents" ? "o conteúdo do objeto" : "o objeto original";

        public PurchaseWindow(ApiContext context, BuildEditor editor, WorldObjectState state) : base(context.Ui, "purchase", "Comprar objeto", 360f, -1f, new Vector2(420, 160))
        {
            _c = context; _b = editor; _state = state; var sale = BuildEditor.SaleOf(state); Price = sale.Price; SaleType = sale.Type;
            Body.Add(UiKit.Text(state.Name, "lv-title-text", "lv-accent")); _owner = UiKit.Text("de …", "lv-muted"); Body.Add(_owner); _what = UiKit.Text("Você leva " + SaleLabel(SaleType) + ".", "lv-body"); Body.Add(_what);
            _price = UiKit.Text("VC$ " + Price.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")), "lv-title-text"); _price.style.fontSize = 26; _price.style.marginTop = 6; Body.Add(_price); _balance = UiKit.Text("Seu saldo: …", "lv-small", "lv-muted"); Body.Add(_balance);
            _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; _status.style.marginTop = 6; Body.Add(_status);
            _buy = UiKit.Button("Comprar por VC$ " + Price.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")), Buy, "lv-btn-primary"); _buy.style.marginTop = 6; Body.Add(UiKit.Row(_buy, UiKit.Button("Cancelar", Close)));
            _b.Purchased += OnPurchased; _b.Said += OnSaid; Closed += () => { _b.Purchased -= OnPurchased; _b.Said -= OnSaid; };
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/users/" + state.OwnerUserId + "/profile/full", null), r => { var name = r.Status == 200 ? Json.One(r.Body).Child("profile").First("displayName", "username") : ""; _owner.text = "de " + (name.Length > 0 ? name : state.OwnerUserId.ToString().Substring(0, 8)); });
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/economy/wallet", null), r =>
            {
                if (r.Status != 200) { _balance.text = "Seu saldo: —"; return; } var wallet = Json.One(r.Body); Balance = wallet.Number("availableCoins"); _balance.text = "Seu saldo: VC$ " + Balance.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
                if (wallet.Flag("isBlocked")) { _status.text = "Sua carteira está bloqueada."; _status.AddToClassList("lv-bad"); _buy.SetEnabled(false); } else if (Balance < Price) { _status.text = "Saldo insuficiente para esta compra."; _status.AddToClassList("lv-bad"); }
            });
            if (state.OwnerUserId == _c.Api.UserId) { _status.text = "Este objeto já é seu."; _buy.SetEnabled(false); }
        }

        public void Buy()
        {
            if (Done || !_buy.enabledSelf) return; if (!_b.Buy(_state.Id, Price)) { _status.text = "Sem conexão com a ilha."; return; }
            _buy.SetEnabled(false); _status.text = "Comprando…"; _status.RemoveFromClassList("lv-bad");
        }

        private void OnPurchased(long price, string type, IReadOnlyList<string> delivered)
        {
            Done = true; _status.text = "Comprado por VC$ " + price.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + (delivered.Count > 0 ? ". Foi para o seu inventário: " + string.Join(", ", delivered) + "." : "."); _status.RemoveFromClassList("lv-bad"); _c.Notices.Show("Compra feita.", NoticeKind.Good);
        }

        private void OnSaid(string text) { if (Done || _buy.enabledSelf) return; _status.text = text; _status.AddToClassList("lv-bad"); _buy.SetEnabled(true); }
    }

    /// <summary>The window for an object that takes payments: a field for the amount (the price it asks is filled in, with the quick amounts the owner set), the balance, and "Pagar", which is a transfer to the owner the platform checks.</summary>
    public sealed class PaymentWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly WorldObjectState _state; private readonly TextField _amount; private readonly Label _status, _balance;
        public string StatusText => _status.text;
        public TextField Amount => _amount;
        public bool Paid { get; private set; }

        /// <summary>The amounts an object offers as buttons: its price first, then the quick amounts the owner set (up to four).</summary>
        public static List<long> OptionsOf(WorldObjectState state)
        {
            var list = new List<long>(); var price = BuildEditor.PayPriceOf(state); if (price > 0) list.Add(price);
            try { var p = System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(state.PermissionsJson) ? "{}" : state.PermissionsJson) as System.Text.Json.Nodes.JsonObject; if (p?["payQuick"] is System.Text.Json.Nodes.JsonArray quick) foreach (var q in quick) if (q is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<long>(out var n) && n > 0 && !list.Contains(n) && list.Count < 5) list.Add(n); }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException || exception is FormatException) { }
            return list;
        }

        /// <summary>Checks the amount: null when it can be sent, otherwise why not.</summary>
        public static string AmountProblem(string text, out long amount)
        {
            amount = 0; if (!long.TryParse((text ?? "").Trim(), out amount) || amount < 1) return "Digite um valor inteiro maior que zero."; if (amount > 10_000_000) return "O valor máximo é VC$ 10.000.000."; return null;
        }

        public PaymentWindow(ApiContext context, WorldObjectState state) : base(context.Ui, "payment", "Pagar", 340f, -1f, new Vector2(420, 160))
        {
            _c = context; _state = state; Body.Add(UiKit.Text(state.Name, "lv-title-text", "lv-accent")); Body.Add(UiKit.Text("O pagamento vai para o dono do objeto.", "lv-muted", "lv-small"));
            var options = OptionsOf(state); _amount = UiKit.Field("Valor (VerseCoin)", false, 10, options.Count > 0 ? options[0].ToString() : ""); Body.Add(_amount);
            if (options.Count > 0) { var row = UiKit.Row(); foreach (var o in options) { var value = o; row.Add(UiKit.Button("VC$ " + value.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")), () => _amount.value = value.ToString())); } Body.Add(row); }
            _balance = UiKit.Text("Seu saldo: …", "lv-small", "lv-muted"); Body.Add(_balance); _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            Body.Add(UiKit.Row(UiKit.Button("Pagar", Pay, "lv-btn-primary"), UiKit.Button("Cancelar", Close)));
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/economy/wallet", null), r => _balance.text = r.Status == 200 ? "Seu saldo: VC$ " + Json.One(r.Body).Number("availableCoins").ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) : "Seu saldo: —");
        }

        public void Pay()
        {
            var problem = AmountProblem(_amount.value, out var amount); if (problem != null) { Fail(problem); return; } if (_state.OwnerUserId == _c.Api.UserId) { Fail("Este objeto já é seu."); return; }
            _status.text = "Pagando…"; _status.RemoveFromClassList("lv-bad");
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/economy/transfer", JsonSerializer.Serialize(new { recipientUserId = _state.OwnerUserId, amountCoins = amount, idempotencyKey = Guid.NewGuid().ToString("N"), message = "Pagamento a " + _state.Name })), r =>
            {
                if (r.Status >= 200 && r.Status < 300) { Paid = true; _status.text = "Pagamento de VC$ " + amount.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + " feito."; _c.Notices.Show("Pagamento feito.", NoticeKind.Good); }
                else Fail(r.Status == 400 || r.Status == 409 || r.Status == 422 ? UiTasks.Explain(r.Status, r.Body) : UiTasks.Explain(r.Status));
            });
        }

        private void Fail(string text) { _status.text = text; _status.AddToClassList("lv-bad"); }
    }

    /// <summary>"Objetos apagados": what the person deleted from the world and can still get back for 30 days. Recovering puts the chosen ones in the inventory as one construction; nothing goes straight back into the world, so every rule about placing is checked when it is placed.</summary>
    public sealed class LostObjectsWindow : FloatingWindow
    {
        public sealed class Lost { public Guid Id; public string Name, Shape; public bool Linked; public DateTimeOffset DeletedAt, ExpiresAt; public string Text => Name + " · " + Shape + (Linked ? " · conjunto" : "") + "\napagado em " + DeletedAt.LocalDateTime.ToString("dd/MM HH:mm") + " · some para sempre em " + Math.Max(0, (int)Math.Ceiling((ExpiresAt - DateTimeOffset.UtcNow).TotalDays)) + " dia(s)"; }
        private readonly ApiContext _c; private readonly ScrollView _list; private readonly Label _note; private readonly Button _recover, _all; private readonly List<Lost> _lost = new List<Lost>(); private readonly HashSet<Guid> _chosen = new HashSet<Guid>(); private string _keep = "";
        public IReadOnlyList<Lost> Items => _lost;
        public string NoteText => _note.text;
        public int Chosen => _chosen.Count;

        public static List<Lost> Parse(string json)
        {
            var list = new List<Lost>();
            try
            {
                using var document = JsonDocument.Parse(json); if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array) return list;
                foreach (var e in objects.EnumerateArray()) { var row = new Row(e.Clone()); if (row.Id() == Guid.Empty) continue; list.Add(new Lost { Id = row.Id(), Name = row.Text("name"), Shape = row.Text("shape"), Linked = row.Has("groupId") && row.Text("groupId").Length > 0, DeletedAt = DateTimeOffset.TryParse(row.Text("deletedAt"), out var at) ? at : DateTimeOffset.MinValue, ExpiresAt = DateTimeOffset.TryParse(row.Text("expiresAt"), out var ex) ? ex : DateTimeOffset.MinValue }); }
            }
            catch (JsonException) { }
            return list;
        }

        public LostObjectsWindow(ApiContext context) : base(context.Ui, "lost-objects", "Objetos apagados", 440f, 420f, new Vector2(420, 120))
        {
            _c = context; var intro = UiKit.Text("Objetos que você apagou do mundo. Escolha os que quer de volta: eles vão para o seu inventário, em “Devolvidos e recuperados”, e daí você os coloca onde quiser.", "lv-muted", "lv-small"); intro.style.whiteSpace = WhiteSpace.Normal; intro.style.flexShrink = 0; Body.Add(intro);
            _list = new ScrollView(); _list.style.flexGrow = 1; _list.style.minHeight = 0; Body.Add(_list); _recover = UiKit.Button("Recuperar os escolhidos", () => Recover(true), "lv-btn-primary"); _all = UiKit.Button("Recuperar todos", () => Recover(false)); Body.Add(UiKit.Row(_recover, _all));
            _note = UiKit.Text("", "lv-small", "lv-muted"); _note.style.whiteSpace = WhiteSpace.Normal; _note.style.flexShrink = 0; Body.Add(_note); Reload();
        }

        public void Reload()
        {
            if (_keep.Length == 0) _note.text = "Carregando…";
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/lost", null), r =>
            {
                _lost.Clear(); _list.Clear(); _chosen.Clear();
                if (r.Status < 200 || r.Status >= 300) { _note.text = UiTasks.Explain(r.Status); Update(); return; }
                _lost.AddRange(Parse(r.Body)); foreach (var item in _lost) { var captured = item; var button = UiKit.Button(item.Text, () => Toggle(captured, null)); button.style.whiteSpace = WhiteSpace.Normal; button.style.unityTextAlign = TextAnchor.MiddleLeft; button.userData = captured; _list.Add(button); }
                _note.text = _keep.Length > 0 ? _keep : _lost.Count == 0 ? "Nenhum objeto apagado nos últimos 30 dias." : _lost.Count + " objeto(s) podem voltar."; _keep = ""; Update();
            });
        }

        public void Choose(int index) { if (index >= 0 && index < _lost.Count) Toggle(_lost[index], null); }

        private void Toggle(Lost item, bool? on)
        {
            var now = on ?? !_chosen.Contains(item.Id); if (now) _chosen.Add(item.Id); else _chosen.Remove(item.Id);
            foreach (var child in _list.Query<Button>().ToList()) if (child.userData == item) child.EnableInClassList("lv-btn-selected", now); Update();
        }

        private void Update() { _recover.SetEnabled(_chosen.Count > 0); _all.SetEnabled(_lost.Count > 0); }

        public void Recover(bool chosen)
        {
            var ids = (chosen ? _lost.Where(l => _chosen.Contains(l.Id)) : _lost).Select(l => l.Id).Take(200).ToArray(); if (ids.Length == 0) return;
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/inventory/lost/recover", JsonSerializer.Serialize(new { objectIds = ids })), r =>
            {
                if (r.Status >= 200 && r.Status < 300) { _keep = ids.Length + " objeto(s) no seu inventário, em “Devolvidos e recuperados”."; Reload(); return; }
                var code = Json.One(r.Body).First("error", "code"); _keep = code == "already_recovered" ? "Outra pessoa (ou outra janela sua) já recuperou esses objetos." : code == "not_found" ? "Algum desses objetos não pode mais ser recuperado (já voltou, venceu o prazo ou não é seu). A lista foi atualizada." : code == "inventory_full" ? "Seu inventário está cheio." : code == "invalid_recovery" ? "Escolha de 1 a 200 objetos." : UiTasks.Explain(r.Status, r.Body); Reload();
            });
        }
    }
}
