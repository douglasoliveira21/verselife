using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LifeVerse.Shared.Items;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The inventory as the Godot client keeps it: views (Tudo, Pastas, Sem pasta, Favoritos, Recentes, Atualizados, Devolvidos), the bin, the space used with the integrity check, and opening a pack (a .zip of files
    /// that becomes a folder). Items can be searched and sorted, ticked and moved together into a folder or sent to the bin; each one opens its details: rename, move, copy, favourite, wear, take the news of an update,
    /// rights and history. Folders are created, renamed, moved up, sent to the bin, restored and deleted for good. Everything goes through <c>/api/v1/inventory</c>.
    /// </summary>
    public sealed class InventoryManagerWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections =
        {
            ("tudo", "Tudo"), ("arvore", "Árvore"), ("pastas", "Pastas"), ("sempasta", "Sem pasta"), ("favoritos", "Favoritos"), ("recentes", "Recentes"), ("atualizados", "Atualizados"), ("devolvidos", "Devolvidos"), ("lixeira", "Lixeira"),
            ("espaco", "Espaço e verificação"), ("pacote", "Abrir um pacote"),
        };
        public static readonly (string Id, string Text)[] Sorts = { ("", "Padrão (favoritos primeiro)"), ("name", "Nome"), ("date", "Mais novos"), ("recent", "Usados por último"), ("size", "Tamanho"), ("type", "Tipo") };
        public static readonly (string Id, string Text)[] Slots = { ("accessory", "Acessório"), ("head", "Cabeça"), ("hair", "Cabelo"), ("top", "Parte de cima"), ("bottom", "Parte de baixo"), ("shoes", "Calçados") };
        public const int PageSize = 30;
        private const string Inv = "/api/v1/inventory";
        private List<JsonElement> _folders = new List<JsonElement>();
        private Guid? _folder; private string _search = "", _sort = "";
        /// <summary>The folder open in the Pastas view (null is the top).</summary>
        public Guid? Folder => _folder;
        /// <summary>The report of the last pack opened (for the tests).</summary>
        public string LastPack { get; private set; } = "";

        private string _dir = ""; private readonly bool _subfolders;

        /// <summary>With the settings, the list starts in the order chosen in Configurações (Inventário e conteúdo) and a folder shows what is in its subfolders too when asked.</summary>
        public InventoryManagerWindow(ApiContext context, SettingsStore settings = null) : base(context, "inventory-manager", "Inventário", Sections, 820f, 560f, new Vector2(240, 70), 170f)
        {
            if (settings != null) { var (sort, dir) = SettingsEffects.InventorySort(settings.Int("inventory.default_sort")); _sort = sort; _dir = dir; _subfolders = settings.Bool("inventory.subfolders"); }
            Show("tudo");
        }

        protected override void Fill(string section)
        {
            Summary();
            switch (section)
            {
                case "tudo": Items("all"); break; case "sempasta": Items("unfiled"); break; case "favoritos": Items("favorites"); break; case "recentes": Items("recent"); break;
                case "atualizados": Items("updated"); break; case "arvore": Tree(); break; case "devolvidos": Items("returned"); break; case "pastas": Folders(); break; case "lixeira": Bin(); break; case "espaco": Space(); break; case "pacote": Pack(); break;
            }
        }

        private void Summary() => Get(Inv + "/summary", s => _summary.text = I(s, "total") + " itens  ·  " + I(s, "favorites") + " favoritos  ·  " + I(s, "unfiled") + " sem pasta  ·  " + I(s, "recent") + " recentes  ·  lixeira: " + I(s, "trash") + " itens e " + I(s, "trashFolders") + " pastas");

        // ---- words ----

        /// <summary>The address of a page of the list: a view (all, unfiled, favorites, recent, updated, returned, trash), a folder, a search and an order.</summary>
        public static string ListPath(string view, Guid? folder, string search, string sort, int page, int pageSize = PageSize, string dir = "", bool subfolders = false)
        {
            var api = view == "unfiled" ? "all" : view; var path = Inv + "/items?view=" + api + "&page=" + Math.Max(1, page) + "&pageSize=" + pageSize;
            if (view == "unfiled") path += "&unfiled=true"; if (folder is { } f) path += "&folderId=" + f;
            if (!string.IsNullOrWhiteSpace(search)) path += "&search=" + Uri.EscapeDataString(search.Trim()); if (!string.IsNullOrEmpty(sort)) path += "&sort=" + sort;
            if (!string.IsNullOrEmpty(dir)) path += "&dir=" + dir; if (subfolders && folder != null) path += "&includeSubfolders=true";
            return path;
        }

        /// <summary>A size in words: 512 B, 3,4 KB, 12,0 MB, 1,25 GB.</summary>
        public static string Size(long bytes)
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR"); if (bytes < 1024) return Math.Max(0, bytes) + " B"; if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0.0", pt) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576d).ToString("0.0", pt) + " MB"; return (bytes / 1073741824d).ToString("0.00", pt) + " GB";
        }

        /// <summary>What the holder may do with an item, in words ("copiar, modificar, transferir").</summary>
        public static string RightsWords(int rights)
        {
            var list = new List<string>(); if ((rights & ItemRights.Copy) != 0) list.Add("copiar"); if ((rights & ItemRights.Modify) != 0) list.Add("modificar"); if ((rights & ItemRights.Transfer) != 0) list.Add("transferir"); if ((rights & ItemRights.Resell) != 0) list.Add("revender");
            return list.Count == 0 ? "sem direitos" : string.Join(", ", list);
        }

        public static string CategoryWords(string category) => category switch
        {
            "objects" => "objeto", "textures" => "textura", "sounds" => "som", "animations" => "animação", "clothing" => "roupa", "accessories" => "acessório", "models" => "modelo", "scripts" => "script", "packages" => "construção", "packs" => "pacote", _ => category,
        };

        /// <summary>The picture of a kind of item.</summary>
        private static Icon Tinted(Icon icon, string kind) { icon.AddToClassList(kind); return icon; }
        public static string IconOfCategory(string category) => category switch
        {
            "objects" => "cube", "textures" => "texture", "sounds" => "volume", "animations" => "play", "clothing" => "avatar", "accessories" => "star", "models" => "cube", "scripts" => "code", "packages" => "home", "packs" => "folder", _ => "bag",
        };

        /// <summary>A refusal of the inventory in words.</summary>
        public static string Words(int status, string body)
        {
            string code = ""; try { using var d = JsonDocument.Parse(body ?? ""); if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) code = e.GetString() ?? ""; } catch (JsonException) { }
            return code switch
            {
                "name_taken" => "Já existe uma pasta com esse nome aqui.", "folder_limit" => "Você chegou ao limite de pastas.", "item_busy" => "O item está em uso (oferecido ou colocado no mundo).", "inventory_full" => "O inventário está cheio.",
                "storage_full" => "Seu espaço de arquivos acabou: esvazie a lixeira ou apague versões antigas.", "stale" => "Outra mudança chegou antes: a lista foi lida de novo.", "unchanged" => "Nada mudou.", "no_copy" or "not_copyable" => "Este item não pode ser copiado.",
                "invalid_zip" => "O arquivo não é um .zip válido.", "empty_zip" => "O .zip está vazio.", "too_many_files" => "O .zip tem arquivos demais.", "pack_too_large" => "O .zip é grande demais.", "bad_files" => "Alguns arquivos do .zip não são aceitos; nada foi criado.",
                "cycle" or "bad_parent" => "Uma pasta não pode ir para dentro dela mesma.", "bad_name" => "Dê um nome à pasta (até 60 letras).", "not_found" => "Não encontrado (talvez já tenha sido apagado).",
                _ => UiTasks.Explain(status, body),
            };
        }

        private void Change(string method, string path, object body, string done, Action then) => Send(method, path, body, done, then, Words);

        private void LoadFolders(Action then) => Get(Inv + "/folders", f => { _folders = Items(f); then(); });
        private string FolderName(Guid id) => S(_folders.FirstOrDefault(f => G(f, "id") == id), "name");
        private List<(string Id, string Text)> FolderOptions(bool withTop) { var list = _folders.OrderBy(f => PathOf(G(f, "id"))).Select(f => (G(f, "id").ToString(), PathOf(G(f, "id")))).ToList(); if (withTop) list.Insert(0, ("", "(sem pasta)")); return list; }

        /// <summary>The path of a folder: "Roupas / Verão".</summary>
        public string PathOf(Guid id)
        {
            var names = new List<string>(); var seen = new HashSet<Guid>(); Guid? at = id;
            while (at is { } current && seen.Add(current)) { var f = _folders.FirstOrDefault(x => G(x, "id") == current); if (f.ValueKind != JsonValueKind.Object) break; names.Insert(0, S(f, "name")); at = Guid.TryParse(S(f, "parentId"), out var p) ? p : (Guid?)null; }
            return string.Join(" / ", names);
        }

        // ---- the list of items ----

        private void Items(string view, int page = 1, Guid? folder = null) => LoadFolders(() => Get(ListPath(view, folder, _search, _sort, page, PageSize, _dir), root => DrawList(view, root, page, folder)));

        private void DrawList(string view, JsonElement root, int page, Guid? folder, bool clear = true)
        {
            if (clear) { Reset(); SearchBar(() => Items(view, 1, folder)); }
            var items = Arr(root, "items"); Line(I(root, "total") + (I(root, "total") == 1 ? " item" : " itens"), "lv-small", "lv-muted");
            foreach (var item in items) ItemRow(item, view, () => Items(view, page, folder));
            if (items.Count == 0) Line(view == "updated" ? "Nenhum item com versão nova." : "Nada por aqui.", "lv-small", "lv-muted");
            else Bulk(() => Items(view, page, folder));
            Pager(root, p => Items(view, p, folder));
        }

        private void SearchBar(Action reload)
        {
            var search = Field("Buscar (nome, descrição, tipo)", _search, 60); search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) { _search = search.value; reload(); } }, TrickleDown.TrickleDown);
            var sort = Choice("Ordem", Sorts.ToList(), _sort);
            Buttons(Btn("Buscar", () => { _search = search.value; _sort = IdOf(sort); _dir = ""; reload(); }), Btn("Limpar a busca", () => { _search = ""; _sort = ""; _dir = ""; reload(); }));
        }

        private void ItemRow(JsonElement item, string view, Action reload)
        {
            var id = G(item, "itemId"); var box = new Toggle(); Toggles["item:" + id] = box; var name = S(item, "name");
            var flags = (Flag(item, "favorite") ? "★ " : "") + (Flag(item, "hasUpdate") ? "versão nova · " : "") + (S(item, "origin") is "returned" or "recovered" ? "devolvido · " : "");
            var cells = new List<VisualElement> { box, Tinted(new Icon(IconOfCategory(S(item, "category")), 18) { name = "kind-icon" }, "lv-accent"), UiKit.Text(name, "lv-bold"), UiKit.Text(flags + CategoryWords(S(item, "category")) + " · " + Size(I(item, "size")) + (Has(item, "folderId") && _folders.Count > 0 ? " · " + PathOf(G(item, "folderId")) : ""), "lv-small", "lv-muted"), UiKit.Spacer() };
            if (view == "trash") { cells.Add(Btn("Restaurar", () => Change("POST", Inv + "/items/" + id + "/restore", "{}", name + " voltou da lixeira.", reload))); cells.Add(Sure("Apagar de vez", () => "Apagar " + name + " para sempre?", () => Change("DELETE", Inv + "/items/" + id + "/purge", null, name + " foi apagado.", reload))); }
            else cells.Add(Btn("Detalhes", () => Detail(item, reload)));
            var row = Row(id, cells.ToArray()); if (view == "trash") return;
            // the right button opens the quick actions under the row (wear, details, bin); a left drag carries the item onto a folder
            row.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { ToggleQuick(row, item, reload); e.StopPropagation(); } });
            Draggable(row, id, name, reload);
        }

        /// <summary>The quick actions of an item (the right button on its row): wear it, open its details, or send it to the bin.</summary>
        public VisualElement ToggleQuick(VisualElement row, JsonElement item, Action reload)
        {
            var existing = row.parent?.Q<VisualElement>("quick-" + G(item, "itemId")); if (existing != null) { existing.RemoveFromHierarchy(); return null; }
            var id = G(item, "itemId"); var name = S(item, "name");
            var quick = UiKit.Row(Btn("Vestir", () => Change("POST", Inv + "/items/" + id + "/equip", new { slot = "accessory" }, name + " vestido.", () => Change("POST", Inv + "/items/" + id + "/used", "{}", null, null))),
                Btn("Detalhes", () => Detail(item, reload)), Sure("Lixeira", () => "Mandar " + name + " para a lixeira?", () => Change("DELETE", Inv + "/items/" + id, null, name + " foi para a lixeira.", reload)));
            quick.name = "quick-" + id; quick.style.marginLeft = 28; quick.style.marginBottom = 4; var at = row.parent.IndexOf(row); row.parent.Insert(at + 1, quick); return quick;
        }

        // ---- dragging an item onto a folder ----
        private Label _ghost; private Guid _dragging; private Vector2 _dragFrom; private bool _dragMoved;

        /// <summary>The folder ("" for the top) of the row under a point of the panel, or null when it is not a folder.</summary>
        public static string FolderUnder(VisualElement picked)
        {
            for (var e = picked; e != null; e = e.parent) if (e.userData is string tag && tag.StartsWith("drop-folder:", StringComparison.Ordinal)) return tag.Substring("drop-folder:".Length);
            return null;
        }

        private void Draggable(VisualElement row, Guid id, string name, Action reload)
        {
            row.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0 || e.target is Button || e.target is Toggle || (e.target as VisualElement)?.parent is Toggle) return; _dragging = id; _dragFrom = e.position; _dragMoved = false; row.CapturePointer(e.pointerId); });
            row.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragging != id || !row.HasPointerCapture(e.pointerId)) return;
                if (!_dragMoved && ((Vector2)e.position - _dragFrom).sqrMagnitude < 36f) return; _dragMoved = true;
                if (_ghost == null) { _ghost = UiKit.Text("", "lv-glass", "lv-small"); _ghost.style.position = Position.Absolute; _ghost.pickingMode = PickingMode.Ignore; _ghost.style.paddingLeft = _ghost.style.paddingRight = 8; }
                if (_ghost.parent == null) _c.Ui.WindowLayer.Add(_ghost); _ghost.text = "📦 " + name + "  →  solte numa pasta"; _ghost.style.left = e.position.x + 12; _ghost.style.top = e.position.y + 8;
            });
            row.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragging != id) return; _dragging = Guid.Empty; if (row.HasPointerCapture(e.pointerId)) row.ReleasePointer(e.pointerId); _ghost?.RemoveFromHierarchy(); if (!_dragMoved) return;
                var target = FolderUnder(row.panel?.Pick(e.position)); if (target == null) { Say("Solte o item sobre uma pasta (ou sobre Início).", false); return; }
                DropOnFolder(id, target, reload);
            });
        }

        /// <summary>Moves one item into a folder ("" is the top).</summary>
        public void DropOnFolder(Guid item, string folder, Action reload)
        {
            Guid? to = Guid.TryParse(folder, out var t) ? t : (Guid?)null;
            Change("POST", Inv + "/move", new { itemIds = new[] { item }, folderIds = Array.Empty<Guid>(), targetFolderId = to }, "Item movido para " + (to is { } f ? PathOf(f) : "fora das pastas") + ".", reload);
        }

        private void Bulk(Action reload)
        {
            if (Section == "lixeira") return;
            Title("Os marcados"); var target = Choice("Mover para", FolderOptions(true), _folder?.ToString() ?? "");
            Buttons(Btn("Mover os marcados", () => MoveTicked(IdOf(target), reload), "lv-btn-primary"),
                Sure("Mandar os marcados para a lixeira", () => "Mandar os itens marcados para a lixeira?", () => { var ids = Ticked("item:"); if (ids.Length == 0) { Say("Marque os itens.", true); return; } foreach (var i in ids) Change("DELETE", Inv + "/items/" + i, null, null, null); Say(ids.Length + " item(ns) na lixeira.", false); schedule.Execute(reload).ExecuteLater(500); }));
        }

        private Guid[] Ticked(string prefix) => Toggles.Where(t => t.Key.StartsWith(prefix) && t.Value.value).Select(t => Guid.Parse(t.Key.Substring(prefix.Length))).ToArray();

        /// <summary>Moves the ticked items (and folders) into a folder ("" is the top, without a folder).</summary>
        public void MoveTicked(string target, Action reload)
        {
            var items = Ticked("item:"); var folders = Ticked("folder:"); if (items.Length == 0 && folders.Length == 0) { Say("Marque o que vai mudar de lugar.", true); return; }
            Guid? to = Guid.TryParse(target, out var t) ? t : (Guid?)null;
            Change("POST", Inv + "/move", new { itemIds = items, folderIds = folders, targetFolderId = to }, (items.Length + folders.Length) + " movido(s) para " + (to is { } f ? PathOf(f) : "fora das pastas") + ".", reload);
        }

        /// <summary>The preview opened last from an item (for the checks).</summary>
        public ItemPreviewWindow Preview { get; private set; }

        // ---- the details of an item ----

        private void Detail(JsonElement item, Action back)
        {
            Reset(); var id = G(item, "itemId"); var name = S(item, "name");
            Buttons(Btn("Voltar à lista", back)); Title(name);
            Line(CategoryWords(S(item, "category")) + " · versão " + I(item, "version") + " · " + Size(I(item, "size")) + " · " + S(item, "mimeType") + " · recebido em " + When(Date(item, "createdAt")) + (Has(item, "lastUsedAt") ? " · usado em " + When(Date(item, "lastUsedAt")) : ""), "lv-small", "lv-muted");
            if (S(item, "description").Length > 0) Line(S(item, "description"));
            Line("Você pode: " + RightsWords((int)I(item, "ownerRights")) + "  ·  quem receber de você poderá: " + RightsWords((int)I(item, "nextOwnerRights")) + (Flag(item, "isCreator") ? "  ·  você criou" : ""), "lv-small");
            if (Flag(item, "hasUpdate")) Buttons(Btn("Entendi a versão nova", () => Change("POST", Inv + "/items/" + id + "/ack-update", "{}", "Versão nova vista.", back)));
            Title("Nome"); var rename = Field("Nome no inventário", name, 120); Buttons(Btn("Renomear", () => { if (rename.value.Trim().Length == 0) { Say("Dê um nome ao item.", true); return; } Change("PATCH", Inv + "/items/" + id, new { displayName = rename.value.Trim() }, "Item renomeado.", back); }));
            Title("Pasta"); var folder = Choice("Pasta", FolderOptions(true), Has(item, "folderId") ? S(item, "folderId") : "");
            Buttons(Btn("Mudar de pasta", () => { var to = IdOf(folder); Change("PATCH", Inv + "/items/" + id, to.Length == 0 ? (object)new { clearFolder = true } : new { folderId = Guid.Parse(to) }, "Item mudou de pasta.", back); }));
            var kind = ItemPreviewWindow.KindOf(S(item, "category"), S(item, "mimeType"));
            if (kind != ItemPreviewKind.None) Buttons(Btn(kind == ItemPreviewKind.Package ? "Ver o conteúdo" : "Pré-visualizar", () => { Preview = new ItemPreviewWindow(_c, id, G(item, "assetId"), name, S(item, "category"), S(item, "mimeType"), I(item, "size")); Preview.Open(); }, "lv-btn-primary"));
            Title("Ações"); var slot = Choice("Vestir em", Slots.ToList(), "accessory");
            Buttons(Btn(Flag(item, "favorite") ? "Tirar dos favoritos" : "Pôr nos favoritos", () => Change(Flag(item, "favorite") ? "DELETE" : "POST", Inv + "/items/" + id + "/favorite", Flag(item, "favorite") ? null : "{}", Flag(item, "favorite") ? "Tirado dos favoritos." : "Nos favoritos.", back)),
                Btn("Fazer uma cópia", () => Change("POST", Inv + "/items/" + id + "/copy", "{}", "Cópia feita.", back)),
                Btn("Vestir", () => Change("POST", Inv + "/items/" + id + "/equip", new { slot = IdOf(slot) }, name + " vestido.", () => Change("POST", Inv + "/items/" + id + "/used", "{}", null, null))),
                Btn("Direitos e histórico…", () => new ItemWindow(_c, id, name, G(item, "assetId")).Open()),
                Sure("Mandar para a lixeira", () => "Mandar " + name + " para a lixeira?", () => Change("DELETE", Inv + "/items/" + id, null, name + " foi para a lixeira.", back)));
        }

        // ---- Árvore: the folders open in place, with what is inside under them ----

        private readonly HashSet<Guid> _expanded = new HashSet<Guid>();
        private readonly Dictionary<Guid, VisualElement> _nodes = new Dictionary<Guid, VisualElement>();
        public bool Expanded(Guid folder) => _expanded.Contains(folder);
        /// <summary>The rows shown under an open folder of the tree (for the checks).</summary>
        public int Under(Guid folder) => _nodes.TryGetValue(folder, out var node) ? node.childCount : 0;

        private void Tree() => LoadFolders(() =>
        {
            Reset(); _nodes.Clear(); Line("A seta abre uma pasta no lugar; o botão direito num item mostra as ações; arraste um item sobre uma pasta para movê-lo.", "lv-small", "lv-muted");
            var root = new VisualElement { name = "tree" }; _page.Add(root);
            var home = new Icon("home", 16); home.style.marginRight = 8; var top = UiKit.Row(home, UiKit.Text("Meu inventário", "lv-bold")); top.AddToClassList("lv-row"); top.userData = "drop-folder:"; root.Add(top);
            var body = new VisualElement(); body.style.marginLeft = 16; root.Add(body); TreeNode(body, null);
        });

        private void TreeNode(VisualElement into, Guid? folder)
        {
            foreach (var f in _folders.Where(x => (Guid.TryParse(S(x, "parentId"), out var p) ? p : (Guid?)null) == folder).OrderBy(x => S(x, "name")))
            {
                var id = G(f, "id"); var arrow = UiKit.Button("", () => ToggleNode(id), "lv-btn-flat"); arrow.name = "tree-" + id; arrow.AddToClassList("lv-nav-icon"); arrow.tooltip = "Abrir ou fechar a pasta"; var picture = new Icon(_expanded.Contains(id) ? "caret-down" : "caret-right", 12); arrow.Add(picture);
                var row = UiKit.Row(arrow, Tinted(new Icon(_expanded.Contains(id) ? "folder-open" : "folder", 17), "lv-sun"), UiKit.Text(S(f, "name"), "lv-bold"), UiKit.Text(I(f, "itemCount") + " itens", "lv-small", "lv-muted"));
                foreach (var cell in row.Children().Skip(1)) cell.style.marginRight = 8; row.AddToClassList("lv-row"); row.userData = "drop-folder:" + id; into.Add(row); RowIds.Add(id);
                var under = new VisualElement(); under.style.marginLeft = 22; into.Add(under); _nodes[id] = under;
                if (_expanded.Contains(id)) { TreeNode(under, id); TreeItems(under, id); }
            }
            if (folder == null) TreeItems(into, null);
        }

        private void TreeItems(VisualElement into, Guid? folder) => Get(ListPath(folder == null ? "unfiled" : "all", folder, "", _sort, 1, 100, _dir), root =>
        {
            foreach (var item in Arr(root, "items"))
            {
                var id = G(item, "itemId"); var name = S(item, "name"); var data = item;
                var row = UiKit.Row(Tinted(new Icon(IconOfCategory(S(item, "category")), 16), "lv-accent"), UiKit.Text(name), UiKit.Text(CategoryWords(S(item, "category")), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Detalhes", () => Detail(data, () => Show("arvore"))));
                foreach (var cell in row.Children()) cell.style.marginRight = 8; row.AddToClassList("lv-row"); into.Add(row); RowIds.Add(id);
                row.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { ToggleQuick(row, data, () => Show("arvore")); e.StopPropagation(); } });
                Draggable(row, id, name, () => Show("arvore"));
            }
            if (folder != null && into.childCount == 0) into.Add(UiKit.Text("Pasta vazia.", "lv-small", "lv-muted"));
        });

        /// <summary>Opens or closes a folder of the tree.</summary>
        public void ToggleNode(Guid folder) { if (!_expanded.Remove(folder)) _expanded.Add(folder); Show("arvore"); }

        // ---- Pastas ----

        private void Folders(Guid? open = null, int page = 1) => LoadFolders(() =>
        {
            _folder = open; Reset();
            var top = Btn("Início", () => Folders(null)); top.userData = "drop-folder:"; var crumbs = new List<VisualElement> { top };
            if (open is { } o) { var chain = new List<Guid>(); Guid? at = o; var seen = new HashSet<Guid>(); while (at is { } c && seen.Add(c)) { chain.Insert(0, c); var f = _folders.FirstOrDefault(x => G(x, "id") == c); at = Guid.TryParse(S(f, "parentId"), out var p) ? p : (Guid?)null; } foreach (var c in chain) { var id = c; crumbs.Add(UiKit.Text("/", "lv-muted")); crumbs.Add(UiKit.Button(FolderName(id), () => Folders(id), "lv-btn-flat")); } }
            Buttons(crumbs.ToArray());
            var children = _folders.Where(f => (Guid.TryParse(S(f, "parentId"), out var p) ? p : (Guid?)null) == open).OrderBy(f => S(f, "name")).ToList();
            Title("Pastas aqui (" + children.Count + ")"); var newName = Field("Nome (para criar ou renomear)", "", 60);
            Buttons(Btn("Criar pasta", () => { if (newName.value.Trim().Length == 0) { Say("Dê um nome à pasta.", true); return; } Change("POST", Inv + "/folders", new { name = newName.value.Trim(), parentId = open }, "Pasta criada.", () => Folders(open)); }, "lv-btn-primary"));
            foreach (var f in children)
            {
                var id = G(f, "id"); var name = S(f, "name"); var box = new Toggle(); Toggles["folder:" + id] = box;
                var cells = new List<VisualElement> { box, UiKit.Text("📁 " + name, "lv-bold"), UiKit.Text(I(f, "itemCount") + " itens", "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Abrir", () => Folders(id)),
                    Btn("Renomear", () => { if (newName.value.Trim().Length == 0) { Say("Escreva o nome novo no campo acima.", true); return; } Change("PATCH", Inv + "/folders/" + id, new { name = newName.value.Trim() }, "Pasta renomeada.", () => Folders(open)); }) };
                if (open != null) cells.Add(Btn("Levar para o início", () => Change("PATCH", Inv + "/folders/" + id, new { toTop = true }, "Pasta levada para o início.", () => Folders(open))));
                cells.Add(Sure("Apagar a pasta", () => "Mandar " + name + " e o que tem dentro para a lixeira?", () => Change("DELETE", Inv + "/folders/" + id, null, name + " foi para a lixeira.", () => Folders(open))));
                var folderRow = Row(id, cells.ToArray()); folderRow.userData = "drop-folder:" + id;
            }
            if (open is { } here)
            {
                Get(ListPath("all", here, _search, _sort, page, PageSize, _dir, _subfolders), root => { Title("Itens nesta pasta"); DrawList("all", root, page, here, false); });
            }
            else if (children.Count == 0) Line("Você ainda não tem pastas.", "lv-small", "lv-muted");
            else Bulk(() => Folders(open));
        });

        // ---- Lixeira ----

        private void Bin(int page = 1) => Get(Inv + "/trash", bin => Get(ListPath("trash", null, "", "", page), root =>
        {
            Reset(); var days = I(bin, "retentionDays"); Line("O que vai para a lixeira é apagado de vez depois de " + (days > 0 ? days : 30) + " dias.", "lv-small", "lv-muted");
            Buttons(Sure("Esvaziar a lixeira", () => "Apagar para sempre tudo o que está na lixeira?", () => Change("POST", Inv + "/trash/empty", "{}", "Lixeira esvaziada.", () => Bin())));
            var folders = Arr(bin, "folders"); Title("Pastas (" + folders.Count + ")");
            foreach (var f in folders)
            {
                var id = G(f, "id"); var name = S(f, "name");
                Row(id, UiKit.Text("📁 " + name, "lv-bold"), UiKit.Text(I(f, "items") + " itens · apagada em " + When(Date(f, "deletedAt")) + " · some em " + When(Date(f, "purgeAt")), "lv-small", "lv-muted"), UiKit.Spacer(),
                    Btn("Restaurar a pasta", () => Change("POST", Inv + "/folders/" + id + "/restore", "{}", name + " voltou da lixeira.", () => Bin())), Sure("Apagar a pasta de vez", () => "Apagar " + name + " para sempre?", () => Change("DELETE", Inv + "/folders/" + id + "/purge", null, name + " foi apagada.", () => Bin())));
            }
            if (folders.Count == 0) Line("Nenhuma pasta na lixeira.", "lv-small", "lv-muted");
            Title("Itens (" + I(root, "total") + ")"); foreach (var item in Arr(root, "items")) ItemRow(item, "trash", () => Bin(page));
            if (Arr(root, "items").Count == 0) Line("Nenhum item na lixeira.", "lv-small", "lv-muted"); Pager(root, p => Bin(p));
        }));

        // ---- Espaço e verificação ----

        private void Space() => Get(Inv + "/storage", s =>
        {
            var used = I(s, "usedBytes"); var quota = I(s, "quotaBytes"); var share = quota > 0 ? used * 100 / quota : 0;
            Title("Espaço"); Line(Size(used) + " de " + Size(quota) + " (" + share + "%)", share >= 90 ? "lv-bad" : "lv-bold");
            var bar = new VisualElement(); bar.AddToClassList("lv-meter"); var fill = new VisualElement(); fill.AddToClassList("lv-meter-fill"); fill.style.width = Length.Percent(Mathf.Clamp(share, 0, 100)); fill.EnableInClassList("lv-meter-high", share >= 90); bar.Add(fill); _page.Add(bar);
            Line(I(s, "items") + " itens · " + I(s, "assets") + " arquivos · " + Size(I(s, "inBinBytes")) + " na lixeira · " + Size(I(s, "olderVersionsBytes")) + " em versões antigas", "lv-small", "lv-muted");
            foreach (var c in Arr(s, "byCategory").OrderByDescending(c => I(c, "bytes"))) Line(CategoryWords(S(c, "category")) + ": " + Size(I(c, "bytes")) + " em " + I(c, "assets") + " arquivo(s)", "lv-small");
            Get(Inv + "/integrity", report =>
            {
                var findings = Arr(report, "findings"); Title("Verificação"); Line(I(report, "checked") + " itens verificados · " + (findings.Count == 0 ? "tudo certo" : findings.Count + " problema(s)"), findings.Count == 0 ? "lv-small" : "lv-bold");
                foreach (var f in findings) Line((S(f, "severity") == "error" ? "⚠ " : "• ") + S(f, "detail") + (Flag(f, "repairable") ? " (dá para consertar)" : ""), "lv-small");
                if (findings.Any(f => Flag(f, "repairable"))) Buttons(Btn("Consertar", Repair, "lv-btn-primary"));
            });
        });

        /// <summary>Repairs what the check found (items stuck by offers that no longer exist) and tells how many.</summary>
        public void Repair() => this.Then(_c.Api.ApiAsync("POST", Inv + "/integrity/repair", "{}"), r =>
        {
            if (r.Status != 200) { Say(Words(r.Status, r.Body), true); return; }
            var text = "Consertado."; try { using var d = JsonDocument.Parse(r.Body); text = "Consertado: " + I(d.RootElement, "released") + " item(ns) liberado(s), " + I(d.RootElement, "closed") + " oferta(s) encerrada(s)."; } catch (JsonException) { }
            Show("espaco"); Say(text, false); _c.Notices.Show(text, NoticeKind.Good);
        });

        // ---- Abrir um pacote ----

        private void Pack()
        {
            Line("Um pacote é um arquivo .zip com texturas, sons, modelos e outros arquivos: cada um vira um item, todos juntos numa pasta nova com o nome do .zip. Se algum arquivo não for aceito, nada é criado.", "lv-small", "lv-muted");
            Buttons(Btn("Escolher o .zip…", () => new FilePickerWindow(_c.Ui, "Escolher um pacote", new[] { ".zip" }, Unpack).Open(), "lv-btn-primary"));
            if (LastPack.Length > 0) Line(LastPack);
        }

        /// <summary>Sends a .zip to be unpacked into a new folder and tells what came of it.</summary>
        public void Unpack(string path)
        {
            byte[] bytes; try { bytes = File.ReadAllBytes(path); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { Say("Não consegui ler o arquivo: " + e.Message, true); return; }
            Say("Abrindo " + Path.GetFileName(path) + "…", false);
            this.Then(_c.Api.UploadAsync(Path.GetFileName(path), bytes, null, Inv + "/packs/unpack"), r =>
            {
                LastPack = PackWords(r.Status, r.Body); if (r.Status == 200) { _c.Notices.Show(LastPack, NoticeKind.Good); Say(LastPack, false); } else Say(LastPack, true);
            });
        }

        /// <summary>What a pack became, in words (or why nothing was created, with the files that failed).</summary>
        public static string PackWords(int status, string body)
        {
            try
            {
                using var d = JsonDocument.Parse(body ?? ""); var root = d.RootElement;
                if (status == 200) return I(root, "count") + " item(ns) na pasta " + S(root, "folderName") + ".";
                var words = Words(status, body); var problems = Arr(Obj(root, "report"), "problems");
                return problems.Count == 0 ? words : words + " " + string.Join("; ", problems.Take(5).Select(p => S(p, "file") + ": " + S(p, "error")));
            }
            catch (JsonException) { return Words(status, body); }
        }
    }
}
