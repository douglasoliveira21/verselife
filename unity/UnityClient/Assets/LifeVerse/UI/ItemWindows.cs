using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using LifeVerse.Shared.Items;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The rights of an item of the inventory, in three columns (who has it: the owner now, the group it is shared with, and the next owner it is passed to) with four switches each:
    /// copy, modify, transfer and resell (resell only makes sense with transfer, and a set can only be passed on if the giver holds it: the shared <see cref="ItemRights"/> rules the
    /// switches follow). Also: give the item to someone (an offer, with an option to keep a copy), and the history of what happened to it.
    /// </summary>
    public sealed class ItemWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Guid _item; private readonly Label _status; private readonly Toggle[,] _bits = new Toggle[3, 4]; private readonly TextField _to, _note; private readonly Toggle _keep;
        private long? _revision; private readonly ApiList _history; private bool _loading;
        public int Owner => Read(0); public int Group => Read(1); public int NextOwner => Read(2);
        public bool Loaded { get; private set; }

        private static readonly (int Bit, string Label)[] Rights = { (ItemRights.Copy, "Copiar"), (ItemRights.Modify, "Modificar"), (ItemRights.Transfer, "Transferir"), (ItemRights.Resell, "Revender") };

        public ItemWindow(ApiContext context, Guid item, string name, Guid asset = default) : base(context.Ui, "item-" + item.ToString("N").Substring(0, 8), name.Length == 0 ? "Item" : name, 460f, 560f, new Vector2(470, 90))
        {
            _c = context; _item = item; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            var head = UiKit.Row(); foreach (var title in new[] { "Você", "Grupo", "Próximo dono" }) { var label = UiKit.Text(title, "lv-bold", "lv-accent"); label.style.flexGrow = 1; label.style.width = 130; head.Add(label); } Body.Add(head);
            var grid = UiKit.Row(); grid.style.alignItems = Align.FlexStart;
            for (var column = 0; column < 3; column++)
            {
                var box = new VisualElement(); box.style.flexGrow = 1; box.style.width = 130;
                for (var bit = 0; bit < 4; bit++) { var c = column; var b = bit; var toggle = UiKit.Check(Rights[bit].Label, false, v => OnToggle(c, b, v)); _bits[column, bit] = toggle; box.Add(toggle); }
                grid.Add(box);
            }
            Body.Add(grid); Body.Add(UiKit.Button("Salvar os direitos", Save, "lv-btn-primary")); Body.Add(UiKit.Divider());
            Body.Add(UiKit.Text("Dar este item", "lv-bold", "lv-accent")); _to = UiKit.Field("Para (identificador da pessoa)", false, 40); _note = UiKit.Field("Mensagem (opcional)", false, 200); _keep = UiKit.Check("Ficar com uma cópia (precisa do direito de copiar)", false, _ => { }); Body.Add(_to); Body.Add(_note); Body.Add(_keep);
            Body.Add(Controls.Confirm("Oferecer", () => "Confirmar a oferta", Offer, "lv-btn-secondary")); Body.Add(UiKit.Divider());
            Body.Add(UiKit.Text("Histórico", "lv-bold", "lv-accent")); _history = new ApiList(context, () => "/api/v1/inventory/items/" + item + "/history", row => UiKit.Row(UiKit.Text(row.First("summary", "event", "kind", "action"), "lv-muted", "lv-small"), UiKit.Spacer(), UiKit.Text(DiscoveryWindow.When(row.Text("at", "createdAt")), "lv-dim", "lv-small")), "Sem histórico.", false); _history.style.maxHeight = 120; Body.Add(_history);
            if (asset != Guid.Empty) AddVersions(asset);
            Load();
        }

        // ---- versions of the asset behind the item: the list, switching to one, and publishing a new one from a file ----
        private ApiList _versions; private Label _versionStatus;
        public int VersionCount => _versions?.Count ?? 0;

        private void AddVersions(Guid asset)
        {
            Body.Add(UiKit.Divider()); Body.Add(UiKit.Text("Versões", "lv-bold", "lv-accent")); _versionStatus = UiKit.Text("", "lv-small", "lv-muted"); _versionStatus.style.whiteSpace = WhiteSpace.Normal; Body.Add(_versionStatus);
            _versions = new ApiList(_c, () => "/api/v1/assets/" + asset + "/versions", row =>
            {
                var version = (int)row.Number("version"); var current = row.Flag("current") || row.Flag("isCurrent") || row.Flag("active") || row.Flag("isActive");
                var line = UiKit.Row(UiKit.Text("Versão " + version + (current ? "  ★ em uso" : ""), "lv-bold"), UiKit.Spacer(), UiKit.Text(DiscoveryWindow.When(row.First("at", "createdAt")) + "  " + Bytes(row.Number("size", row.Number("byteSize"))), "lv-muted", "lv-small"));
                if (!current) line.Add(Controls.Confirm("Usar esta", () => "Confirmar", () => VersionCall(HttpMethod.Post, "/api/v1/assets/" + asset + "/versions/" + version + "/activate")));
                return line;
            }, "Sem versões.");
            _versions.style.maxHeight = 130; Body.Add(_versions);
            Body.Add(UiKit.Button("Publicar uma nova versão…", () => new FilePickerWindow(_c.Ui, "Escolha o arquivo da nova versão", null, path =>
            {
                byte[] bytes; try { bytes = System.IO.File.ReadAllBytes(path); } catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException) { _versionStatus.text = exception.Message; return; }
                this.Then(_c.Api.FileCallAsync(HttpMethod.Post, "/api/v1/assets/" + asset + "/versions", System.IO.Path.GetFileName(path), bytes, null), r => { if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show("Nova versão publicada.", NoticeKind.Good); _versions.Reload(); } else { _versionStatus.text = UiTasks.Explain(r.Status); _versionStatus.AddToClassList("lv-bad"); } });
            }).Open(), "lv-btn-secondary"));
        }

        private static string Bytes(long n) => n <= 0 ? "" : n < 1024 ? n + " B" : n < 1024 * 1024 ? n / 1024 + " KB" : (n / 1024f / 1024f).ToString("F1") + " MB";
        private void VersionCall(HttpMethod method, string path) => this.Then(_c.Api.ApiAsync(method.Method, path), r => { if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show("Versão trocada.", NoticeKind.Good); _versions.Reload(); } else { _versionStatus.text = UiTasks.Explain(r.Status); _versionStatus.AddToClassList("lv-bad"); } });

        private int Read(int column) { var value = 0; for (var bit = 0; bit < 4; bit++) if (_bits[column, bit].value) value |= Rights[bit].Bit; return value; }

        /// <summary>Keeps the set coherent: resell needs transfer.</summary>
        private void OnToggle(int column, int bit, bool on)
        {
            if (_loading) return; _loading = true;
            if (Rights[bit].Bit == ItemRights.Resell && on) _bits[column, 2].value = true;
            if (Rights[bit].Bit == ItemRights.Transfer && !on) _bits[column, 3].value = false;
            _loading = false;
        }

        private void Set(int column, int value) { _loading = true; for (var bit = 0; bit < 4; bit++) _bits[column, bit].value = (value & Rights[bit].Bit) != 0; _loading = false; }

        public void Load()
        {
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items/" + _item + "/rights"), r =>
            {
                if (r.Status != 200) { Say(UiTasks.Explain(r.Status), true); return; }
                var all = Json.One(r.Body); var row = all.Has("permissions") ? all.Child("permissions") : all; Set(0, (int)row.Number("owner")); Set(1, (int)row.Number("group")); Set(2, (int)row.Number("nextOwner")); _revision = all.Has("revision") ? all.Number("revision") : row.Has("revision") ? row.Number("revision") : (long?)null; Loaded = true; Say("", false); _history.Reload();
            });
        }

        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); }

        public void Save() => this.Then(_c.Api.ApiAsync("PUT", "/api/v1/inventory/items/" + _item + "/rights", JsonSerializer.Serialize(new { owner = Owner, group = Group, nextOwner = NextOwner, expectedRevision = _revision })), r =>
        {
            if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show("Direitos salvos.", NoticeKind.Good); Load(); } else Say(r.Status == 409 ? "O item mudou enquanto você editava. Os direitos foram recarregados." : UiTasks.Explain(r.Status), true);
            if (r.Status == 409) Load();
        });

        public void Offer()
        {
            if (!Guid.TryParse(_to.value.Trim(), out var to)) { Say("Digite o identificador de quem recebe.", true); return; }
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/inventory/items/" + _item + "/offers", JsonSerializer.Serialize(new { toUserId = to, kind = (string)null, message = _note.value, keepCopy = _keep.value, idempotencyKey = Guid.NewGuid().ToString("N") })), r =>
            { if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show("Oferta enviada.", NoticeKind.Good); _to.value = ""; Say("", false); } else Say(UiTasks.Explain(r.Status), true); });
        }

        public Toggle Bit(int column, int bit) => _bits[column, bit];
        public TextField ToField => _to;
    }

    /// <summary>The offers of items: those received (accept, reject) and the transfers that already happened.</summary>
    public sealed class OffersWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly ApiList _offers, _transfers; private readonly Label _status;
        public int OfferCount => _offers.Count;
        public int TransferCount => _transfers.Count;

        public OffersWindow(ApiContext context) : base(context.Ui, "offers", "Ofertas de itens", 460f, 480f, new Vector2(430, 100))
        {
            _c = context; _status = UiKit.Text("", "lv-small", "lv-muted"); Body.Add(_status); Body.Add(UiKit.Text("Recebidas", "lv-bold", "lv-accent"));
            _offers = new ApiList(context, () => "/api/v1/inventory/offers", row => { var id = row.Id(); return UiKit.Row(UiKit.Text(row.First("itemName", "name") + " (de " + row.First("fromUsername", "fromUserId") + ")", "lv-bold"), UiKit.Spacer(), UiKit.Button("Aceitar", () => Act(id, "accept"), "lv-btn-primary"), UiKit.Button("Recusar", () => Act(id, "reject"))); }, "Nenhuma oferta.");
            _offers.style.maxHeight = 180; Body.Add(_offers); Body.Add(UiKit.Text("Transferências", "lv-bold", "lv-accent"));
            _transfers = new ApiList(context, () => "/api/v1/inventory/transfers", row => UiKit.Row(UiKit.Text(row.First("itemName", "name", "summary"), "lv-muted"), UiKit.Spacer(), UiKit.Text(row.First("status"), "lv-dim", "lv-small")), "Nenhuma transferência."); Body.Add(_transfers);
        }

        private void Act(Guid id, string verb) => this.Then(_c.Api.ApiAsync("POST", "/api/v1/inventory/offers/" + id + "/" + verb), r => { if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show(verb == "accept" ? "Item recebido." : "Oferta recusada.", NoticeKind.Good); _offers.Reload(); _transfers.Reload(); } else { _status.text = UiTasks.Explain(r.Status); _status.AddToClassList("lv-bad"); } });
    }
}
