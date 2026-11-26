using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The wallet: the VerseCoin balance, the last transactions and a transfer to another person (asked to be confirmed before it is sent). The server is the financial authority: the balance, the limits and the block are its.</summary>
    public sealed class WalletWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Label _balance, _status; private readonly ScrollView _list; private readonly TextField _to, _amount, _message; private readonly Button _send;
        private (Guid To, long Amount, string Message)? _pending;
        public long Balance { get; private set; } = -1;
        public bool Blocked { get; private set; }

        public WalletWindow(ApiContext context) : base(context.Ui, "wallet", "Carteira", 420f, 520f, new Vector2(500, 100))
        {
            _c = context; _balance = UiKit.Text("…", "lv-title-text", "lv-accent"); _balance.style.fontSize = 28; Body.Add(_balance); _status = UiKit.Text("", "lv-small", "lv-muted"); Body.Add(_status);
            Body.Add(UiKit.Text("Últimas movimentações", "lv-bold")); _list = new ScrollView(); _list.style.flexGrow = 1; _list.style.minHeight = 150; Body.Add(_list);
            Body.Add(UiKit.Divider()); Body.Add(UiKit.Text("Enviar VerseCoin", "lv-bold"));
            _to = UiKit.Field("Identificador de quem recebe", false, 40); _amount = UiKit.Field("Quantidade", false, 12); _message = UiKit.Field("Mensagem (opcional)", false, 120); Body.Add(_to); Body.Add(_amount); Body.Add(_message);
            _send = UiKit.Button("Enviar", Send, "lv-btn-primary"); Body.Add(_send); Refresh();
        }

        public void Refresh()
        {
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/economy/wallet"), r =>
            {
                if (r.Status != 200) { _balance.text = "—"; _status.text = UiTasks.Explain(r.Status); return; }
                var wallet = Json.One(r.Body); Balance = wallet.Number("availableCoins"); Blocked = wallet.Flag("isBlocked"); _balance.text = Balance.ToString("N0") + " VerseCoin"; _status.text = Blocked ? "Sua carteira está bloqueada." : "";
            });
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/economy/wallet/transactions"), r =>
            {
                _list.Clear(); if (r.Status != 200) return;
                foreach (var row in Json.Rows(r.Body).Take(40))
                {
                    var amount = row.Number("amountCoins", row.Number("amount")); var line = UiKit.Row(UiKit.Text(row.First("description", "kind", "type", "reason"), "lv-muted"), UiKit.Spacer(), UiKit.Text((amount > 0 ? "+" : "") + amount.ToString("N0"), amount < 0 ? "lv-bad" : "lv-good")); line.AddToClassList("lv-row"); _list.Add(line);
                }
                if (_list.childCount == 0) _list.Add(UiKit.Text("Nenhuma movimentação ainda.", "lv-muted"));
            });
        }

        /// <summary>Fills who receives (from "Pagar…" on a person) and puts the keyboard on the amount.</summary>
        public void PayTo(Guid user, string name) { _to.value = user.ToString(); _status.text = "Pagar a " + name + ": digite a quantidade e confirme."; _status.RemoveFromClassList("lv-bad"); _amount.Focus(); }
        public string Recipient => _to.value;

        /// <summary>The check before a transfer is asked for. Null when it is fine.</summary>
        public static string Validate(string to, string amount, long balance, out Guid recipient, out long coins)
        {
            recipient = Guid.Empty; coins = 0;
            if (!Guid.TryParse((to ?? "").Trim(), out recipient)) return "Digite o identificador de quem recebe.";
            if (!long.TryParse((amount ?? "").Trim(), out coins) || coins <= 0) return "Digite uma quantidade inteira maior que zero.";
            if (balance >= 0 && coins > balance) return "Saldo insuficiente.";
            return null;
        }

        /// <summary>First press asks to confirm (it says what will be sent and to whom); the second sends. Changing a field cancels the confirmation.</summary>
        public void Send()
        {
            if (_pending == null)
            {
                var problem = Validate(_to.value, _amount.value, Balance, out var recipient, out var coins); if (problem != null) { _status.text = problem; _status.AddToClassList("lv-bad"); return; }
                _pending = (recipient, coins, _message.value.Trim()); _status.RemoveFromClassList("lv-bad"); _status.text = "Confirme: enviar " + coins.ToString("N0") + " VerseCoin para " + recipient.ToString().Substring(0, 8) + "… Não dá para desfazer."; _send.text = "Confirmar o envio"; return;
            }
            var (to, amount, message) = _pending.Value; _pending = null; _send.text = "Enviar";
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/economy/transfer", JsonSerializer.Serialize(new { recipientUserId = to, amountCoins = amount, idempotencyKey = Guid.NewGuid().ToString("N"), message = message.Length > 0 ? message : null })), r =>
            {
                if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show("Enviado: " + amount.ToString("N0") + " VerseCoin.", NoticeKind.Good); _amount.value = ""; _status.text = ""; Refresh(); } else { _status.text = UiTasks.Explain(r.Status); _status.AddToClassList("lv-bad"); }
            });
        }

        public bool AwaitingConfirmation => _pending != null;
        public TextField ToField => _to; public TextField AmountField => _amount;
    }

    /// <summary>The inventory: views (todos, favoritos, recentes, lixeira), a search field and the items with a favourite star. Placing, copying and rights come with the building tools.</summary>
    public sealed class InventoryWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly ScrollView _list; private readonly TextField _search; private readonly Label _status; private string _view = "all";
        private readonly Dictionary<string, Button> _views = new Dictionary<string, Button>();
        public int ItemCount => _list.contentContainer.childCount;
        public string View => _view;

        public InventoryWindow(ApiContext context) : base(context.Ui, "inventory", "Inventário", 440f, 500f, new Vector2(420, 110))
        {
            _c = context; var bar = UiKit.Row(); foreach (var pair in new[] { ("Tudo", "all"), ("Favoritos", "favorites"), ("Recentes", "recent"), ("Atualizados", "updated"), ("Devolvidos", "returned"), ("Lixeira", "trash") }) { var view = pair.Item2; var button = UiKit.Button(pair.Item1, () => Show(view)); _views[view] = button; bar.Add(button); }
            Body.Add(bar); _search = UiKit.Field("Buscar", false, 60); _search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) Load(); }, TrickleDown.TrickleDown); Body.Add(_search);
            _status = UiKit.Text("", "lv-small", "lv-muted"); Body.Add(_status); _list = new ScrollView(); _list.style.flexGrow = 1; Body.Add(_list); Show("all");
        }

        public void Show(string view) { _view = view; foreach (var pair in _views) pair.Value.EnableInClassList("lv-btn-selected", pair.Key == view); Load(); }

        public void Load()
        {
            var query = "?view=" + _view + "&page=1&pageSize=50" + (string.IsNullOrWhiteSpace(_search.value) ? "" : "&search=" + Uri.EscapeDataString(_search.value.Trim()));
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items" + query), r =>
            {
                _list.Clear(); if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status); return; }
                var rows = Json.Rows(r.Body); _status.text = rows.Count == 0 ? "Nada por aqui." : rows.Count + (rows.Count == 1 ? " item" : " itens");
                foreach (var row in rows)
                {
                    var id = row.Id("id", "itemId"); var name = row.First("name", "title", "assetName", "displayName"); if (name.Length == 0) name = "Item " + id.ToString().Substring(0, 8); var favorite = row.Flag("favorite") || row.Flag("isFavorite");
                    var star = UiKit.Button(favorite ? "★" : "☆", () => Toggle(id, favorite), "lv-btn-flat"); var rights = UiKit.Button("Direitos", () => new ItemWindow(_c, id, name, row.Id("assetId")).Open()); var line = UiKit.Row(UiKit.Text(name, "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("type", "category", "kind"), "lv-muted"), rights, star); line.AddToClassList("lv-row"); _list.Add(line);
                }
            });
        }

        private void Toggle(Guid id, bool favorite) => this.Then(_c.Api.ApiAsync(favorite ? "DELETE" : "POST", "/api/v1/inventory/items/" + id + "/favorite"), r => { if (r.Status >= 200 && r.Status < 300) Load(); else _status.text = UiTasks.Explain(r.Status); });
    }

    /// <summary>Search and discovery: public islands with a search, their category and an "Entrar" button, and a search over everything (people, islands, items).</summary>
    public sealed class SearchWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly TextField _query; private readonly ScrollView _list; private readonly Label _status; private string _tab = "Ilhas";
        public int ResultCount => _list.contentContainer.childCount;

        public SearchWindow(ApiContext context) : base(context.Ui, "search", "Buscar e descobrir", 460f, 500f, new Vector2(380, 100))
        {
            _c = context; var bar = UiKit.Row(UiKit.Button("Ilhas", () => { _tab = "Ilhas"; Search(); }), UiKit.Button("Tudo", () => { _tab = "Tudo"; Search(); })); Body.Add(bar);
            _query = UiKit.Field("Buscar", false, 120); _query.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Search(); }, TrickleDown.TrickleDown); Body.Add(_query); Body.Add(UiKit.Button("Buscar", Search, "lv-btn-primary"));
            _status = UiKit.Text("", "lv-small", "lv-muted"); Body.Add(_status); _list = new ScrollView(); _list.style.flexGrow = 1; Body.Add(_list); Search();
        }

        public void Search()
        {
            var q = _query.value.Trim(); var path = _tab == "Ilhas" ? "/api/v1/discovery/islands?page=1&pageSize=24" + (q.Length > 0 ? "&q=" + Uri.EscapeDataString(q) : "") : q.Length == 0 ? null : "/api/v1/discovery/search?page=1&pageSize=24&q=" + Uri.EscapeDataString(q);
            _list.Clear(); if (path == null) { _status.text = "Digite o que procurar."; return; }
            this.Then(_c.Api.ApiAsync("GET", path), r =>
            {
                _list.Clear(); if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status); return; }
                var rows = Json.Rows(r.Body); _status.text = rows.Count == 0 ? "Nada encontrado." : "";
                foreach (var row in rows)
                {
                    var id = row.Id("id", "islandId"); var name = row.First("name", "title", "username", "displayName"); var kind = row.First("type", "category");
                    var line = UiKit.Row(UiKit.Text(name, "lv-bold"), UiKit.Spacer(), UiKit.Text(kind, "lv-muted")); if (_tab == "Ilhas" || kind == "island") line.Add(UiKit.Button("Entrar", () => _c.EnterIsland(id), "lv-btn-primary")); line.AddToClassList("lv-row"); _list.Add(line);
                }
            });
        }

        public TextField QueryField => _query;
    }

    /// <summary>The island as the platform knows it: name, description, category, size, how many can be in it, whether it is running, and who has permissions.</summary>
    public sealed class IslandInfoWindow : FloatingWindow
    {
        public IslandInfoWindow(ApiContext context, Guid island) : base(context.Ui, "island-info", "Sobre a ilha", 380f, -1f, new Vector2(560, 140))
        {
            var name = UiKit.Text("…", "lv-title-text", "lv-accent"); var text = UiKit.Text("", "lv-muted"); text.style.whiteSpace = WhiteSpace.Normal; var facts = UiKit.Text("", "lv-small"); var people = new ScrollView(); people.style.maxHeight = 160;
            Body.Add(name); Body.Add(text); Body.Add(facts); Body.Add(UiKit.Divider()); Body.Add(UiKit.Text("Permissões", "lv-bold")); Body.Add(people);
            this.Then(context.Api.ApiAsync("GET", "/api/v1/worlds/islands/" + island), r =>
            {
                if (r.Status != 200) { name.text = "—"; text.text = UiTasks.Explain(r.Status); return; }
                var row = Json.One(r.Body); name.text = row.Text("name"); text.text = row.Text("description");
                facts.text = "Categoria: " + row.Text("category", "—") + "   ·   Tamanho: " + row.Text("size", "—") + "   ·   Até " + row.Text("maxPlayers", "—") + " pessoas   ·   Estado: " + row.Text("runtimeState", "—");
            });
            this.Then(context.Api.ApiAsync("GET", "/api/v1/worlds/islands/" + island + "/permissions"), r =>
            {
                if (r.Status != 200) { people.Add(UiKit.Text(r.Status == 403 ? "Só quem gerencia a ilha vê as permissões." : UiTasks.Explain(r.Status), "lv-muted")); return; }
                foreach (var row in Json.Rows(r.Body)) { var line = UiKit.Row(UiKit.Text(row.First("username", "displayName", "userId", "subject"), "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("role", "permission", "level"), "lv-muted")); line.AddToClassList("lv-row"); people.Add(line); }
                if (people.childCount == 0) people.Add(UiKit.Text("Só o dono.", "lv-muted"));
            });
        }
    }
}
