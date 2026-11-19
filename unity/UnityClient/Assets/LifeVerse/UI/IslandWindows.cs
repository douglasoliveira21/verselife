using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The management of the island the person is in: the general data (name, description, category, public or not, how many people, how big; saved with the API's update), who has
    /// which permission (add and revoke), the rules of the island (who may enter and build, scripts, voice, media, what is returned automatically) and the parcels of land (see the
    /// next window). Only the owner and those with the right see most of it: a refusal of the API is told as it came.
    /// </summary>
    public sealed class IslandManageWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Guid _island; private readonly Label _status; private readonly Dictionary<string, VisualElement> _tabs = new Dictionary<string, VisualElement>();
        private readonly TextField _name, _about, _category, _max, _size, _permUser, _permName, _returnMinutes; private readonly Toggle _public, _scripts, _voice, _media, _existing; private readonly DropdownField _access, _build;
        private readonly ApiList _permissions;
        public string CurrentTab { get; private set; }
        public int PermissionCount => _permissions.Count;
        public string NameValue => _name.value;
        public ParcelsPane Parcels { get; }

        public IslandManageWindow(ApiContext context, Guid island) : base(context.Ui, "island-manage", "Gestão da ilha", 480f, 540f, new Vector2(340, 70))
        {
            _c = context; _island = island; var bar = UiKit.Row(); _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            foreach (var name in new[] { "Geral", "Permissões", "Regras", "Terrenos" }) { var captured = name; bar.Add(UiKit.Button(name, () => Show(captured))); var tab = new VisualElement(); tab.style.flexGrow = 1; tab.style.display = DisplayStyle.None; _tabs[name] = tab; }
            Body.Add(bar); Body.Add(_status);
            _name = UiKit.Field("Nome", false, 64); _about = UiKit.Field("Descrição", false, 400); _category = UiKit.Field("Categoria", false, 40); _max = UiKit.Field("Pessoas ao mesmo tempo", false, 4); _size = UiKit.Field("Tamanho (m)", false, 5); _public = UiKit.Check("Ilha pública (aparece na busca)", true, _ => { });
            foreach (var element in new VisualElement[] { _name, _about, _category, _max, _size, _public, UiKit.Button("Salvar", SaveGeneral, "lv-btn-primary") }) _tabs["Geral"].Add(element);
            _permissions = new ApiList(context, () => "/api/v1/worlds/islands/" + island + "/permissions", row => { var id = row.Id(); return UiKit.Row(UiKit.Text(row.First("userId", "groupId", "principalType"), "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("permission"), "lv-muted"), Controls.Confirm("Tirar", () => "Confirmar", () => Act("DELETE", "/api/v1/worlds/islands/" + island + "/permissions/" + id, null, "Permissão retirada.", () => _permissions.Reload()))); }, "Só o dono tem permissões.", false);
            _permUser = UiKit.Field("Pessoa (identificador)", false, 40); _permName = UiKit.Field("Permissão", false, 30, "build");
            _tabs["Permissões"].Add(_permissions); _tabs["Permissões"].Add(_permUser); _tabs["Permissões"].Add(_permName); _tabs["Permissões"].Add(UiKit.Button("Dar permissão", GrantPermission, "lv-btn-primary"));
            _access = new DropdownField("Quem entra", new List<string> { "public", "friends", "private" }, 0); _build = new DropdownField("Quem constrói", new List<string> { "owner", "group", "everyone" }, 0); _scripts = UiKit.Check("Permitir scripts", true, _ => { }); _voice = UiKit.Check("Permitir voz", true, _ => { }); _media = UiKit.Check("Permitir mídia", true, _ => { });
            _returnMinutes = UiKit.Field("Devolver objetos soltos depois de (minutos, vazio = nunca)", false, 5); _existing = UiKit.Check("Aplicar também aos terrenos que já existem", false, _ => { });
            foreach (var element in new VisualElement[] { _access, _build, _scripts, _voice, _media, _returnMinutes, _existing, UiKit.Button("Salvar as regras", SaveRules, "lv-btn-primary") }) _tabs["Regras"].Add(element);
            Parcels = new ParcelsPane(context, island); _tabs["Terrenos"].Add(Parcels); foreach (var tab in _tabs.Values) Body.Add(tab);
            this.Then(context.Api.ApiAsync("GET", "/api/v1/worlds/islands/" + island), r => { if (r.Status != 200) { Say(UiTasks.Explain(r.Status), true); return; } var row = Json.One(r.Body); _name.value = row.Text("name"); _about.value = row.Text("description"); _category.value = row.Text("category"); _max.value = row.Text("maxPlayers", "64"); _size.value = row.Text("size", "256"); _public.value = row.Flag("isPublic"); });
            Show("Geral");
        }

        public void Show(string tab) { CurrentTab = tab; foreach (var pair in _tabs) pair.Value.style.display = pair.Key == tab ? DisplayStyle.Flex : DisplayStyle.None; if (tab == "Permissões") _permissions.Reload(); }
        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); }
        private void Act(string method, string path, string json, string success, Action then = null) => this.Then(_c.Api.ApiAsync(method, path, json), r => { if (r.Status >= 200 && r.Status < 300) { if (success != null) _c.Notices.Show(success, NoticeKind.Good); Say("", false); then?.Invoke(); } else Say(UiTasks.Explain(r.Status), true); });

        /// <summary>The checks before the general data is sent. Null when fine.</summary>
        public static string ValidateGeneral(string name, string max, string size, out int maxPlayers, out int meters)
        {
            maxPlayers = 0; meters = 0;
            if ((name ?? "").Trim().Length < 3) return "O nome precisa ter pelo menos 3 letras.";
            if (!int.TryParse(max, out maxPlayers) || maxPlayers < 1 || maxPlayers > 256) return "Pessoas ao mesmo tempo: de 1 a 256.";
            if (!int.TryParse(size, out meters) || meters < 64 || meters > 4096) return "Tamanho: de 64 a 4096 metros.";
            return null;
        }

        public void SaveGeneral()
        {
            var problem = ValidateGeneral(_name.value, _max.value, _size.value, out var max, out var size); if (problem != null) { Say(problem, true); return; }
            Act("PUT", "/api/v1/worlds/islands/" + _island, JsonSerializer.Serialize(new { name = _name.value.Trim(), description = _about.value, coverImageUrl = (string)null, category = _category.value.Trim(), isPublic = _public.value, size, maxPlayers = max, spawnX = 0f, spawnY = 1f, spawnZ = 0f, environmentJson = (string)null, resourceLimitsJson = (string)null }), "Ilha salva.");
        }

        public void GrantPermission()
        {
            if (!Guid.TryParse(_permUser.value.Trim(), out var user)) { Say("Digite o identificador da pessoa.", true); return; }
            Act("POST", "/api/v1/worlds/islands/" + _island + "/permissions", JsonSerializer.Serialize(new { permission = _permName.value.Trim(), principalType = "user", userId = user, groupId = (Guid?)null }), "Permissão dada.", () => { _permUser.value = ""; _permissions.Reload(); });
        }

        public void SaveRules()
        {
            int? minutes = null; if (_returnMinutes.value.Trim().Length > 0) { if (!int.TryParse(_returnMinutes.value, out var m) || m < 1) { Say("Minutos: um número maior que zero (ou deixe vazio).", true); return; } minutes = m; }
            Act("PUT", "/api/v1/worlds/islands/" + _island + "/land/rules", JsonSerializer.Serialize(new { accessMode = _access.value, buildMode = _build.value, allowScripts = _scripts.value, allowVoice = _voice.value, allowMedia = _media.value, autoReturnMinutes = minutes, applyToExisting = _existing.value }), "Regras salvas.");
        }
    }

    /// <summary>
    /// The parcels of land of an island: the list with the owner and the sale price (buy with two touches, choose, tick to merge), and for the chosen one: sell or take back from sale,
    /// the people allowed or banned, ejecting someone, and the editing of its shape on a grid of the island's cells (split a piece off into a new parcel, expand, shrink), merging
    /// the ticked parcels into one, its music and its environment, and its audit and history. What a person may change is decided by the API from their rights.
    /// </summary>
    public sealed class ParcelsPane : VisualElement
    {
        private readonly ApiContext _c; private readonly Guid _island; private readonly ApiList _parcels, _access; private readonly TextField _price, _person, _minutes, _newName, _extra, _music, _musicName, _environment; private readonly DropdownField _kind;
        private readonly Slider _volume; private readonly Toggle _autoplay; private readonly Label _status; private readonly CellGrid _grid = new CellGrid(); private readonly HashSet<Guid> _merge = new HashSet<Guid>(); private Guid _selected; private int _meters, _cellMeters; private readonly Dictionary<Guid, int[][]> _runs = new Dictionary<Guid, int[][]>();
        public int ParcelCount => _parcels.Count;
        public int AccessCount => _access.Count;
        public CellGrid Grid => _grid;
        public Guid Selected => _selected;

        public ParcelsPane(ApiContext context, Guid island)
        {
            _c = context; _island = island; style.flexGrow = 1; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Add(_status);
            _parcels = new ApiList(context, () => Base, ParcelRow, "Esta ilha não tem terrenos.", true); _parcels.Loaded += ReadGridSize; _parcels.style.maxHeight = 150; Add(_parcels);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; scroll.style.minHeight = 150; Add(scroll);
            scroll.Add(UiKit.Text("Terreno escolhido", "lv-bold", "lv-accent")); _price = UiKit.Field("Preço de venda (VerseCoin)", false, 12);
            scroll.Add(_price); scroll.Add(UiKit.Button("Pôr à venda", Sell, "lv-btn-primary")); scroll.Add(UiKit.Button("Tirar da venda", () => Act("DELETE", Base + "/parcels/" + _selected + "/sale", null, "Venda cancelada.")));
            _access = new ApiList(context, () => _selected == Guid.Empty ? null : Base + "/parcels/" + _selected + "/access", row => { var id = row.Id(); return UiKit.Row(UiKit.Text(row.First("userName", "userId"), "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("kind"), "lv-muted"), UiKit.Button("Tirar", () => Act("DELETE", Base + "/parcels/" + _selected + "/access/" + id, null, null, () => _access.Reload()))); }, "Ninguém na lista deste terreno.", false); _access.style.maxHeight = 100; scroll.Add(_access);
            _person = UiKit.Field("Pessoa (identificador)", false, 40); _minutes = UiKit.Field("Minutos (vazio = sempre)", false, 6); _kind = new DropdownField("Tipo", new List<string> { "ban", "allow" }, 0); scroll.Add(_person); scroll.Add(_minutes); scroll.Add(_kind);
            scroll.Add(UiKit.Button("Colocar na lista", AddAccess)); scroll.Add(Controls.Confirm("Expulsar do terreno", () => "Confirmar a expulsão", Eject, "lv-btn-secondary"));

            scroll.Add(UiKit.Divider()); scroll.Add(UiKit.Text("Forma do terreno", "lv-bold", "lv-accent")); scroll.Add(UiKit.Text("Toque nas células (em laranja o que você escolheu; em verde o terreno escolhido).", "lv-muted", "lv-small")); scroll.Add(_grid);
            _newName = UiKit.Field("Nome do novo terreno (ao dividir)", false, 60); _extra = UiKit.Field("Objetos extras (ao ampliar ou encolher)", false, 6, "0"); scroll.Add(_newName); scroll.Add(_extra);
            scroll.Add(UiKit.Row(Controls.Confirm("Dividir", () => "Confirmar a divisão", Split), Controls.Confirm("Ampliar", () => "Confirmar", () => Reshape("expand", "Terreno ampliado.")), Controls.Confirm("Encolher", () => "Confirmar", () => Reshape("shrink", "Terreno encolhido."))));
            scroll.Add(UiKit.Text("Juntar: marque “Juntar” nos terrenos da lista que devem virar um só (o escolhido fica como principal).", "lv-muted", "lv-small")); scroll.Add(Controls.Confirm("Juntar os marcados", () => "Confirmar a união", Merge, "lv-btn-secondary"));

            scroll.Add(UiKit.Divider()); scroll.Add(UiKit.Text("Música", "lv-bold", "lv-accent")); _music = UiKit.Field("Endereço da música (URL)", false, 300); _musicName = UiKit.Field("Nome", false, 60); _volume = UiKit.Slide("Volume", 0f, 1f, .6f, _ => { }); _autoplay = UiKit.Check("Tocar sozinha", true, _ => { });
            scroll.Add(_music); scroll.Add(_musicName); scroll.Add(_volume); scroll.Add(_autoplay); scroll.Add(UiKit.Button("Salvar a música", SaveMedia));
            scroll.Add(UiKit.Text("Ambiente do terreno (JSON: hora, clima…; vazio volta ao da ilha)", "lv-bold", "lv-accent")); _environment = UiKit.Field("", false, 2000); _environment.multiline = true; _environment.style.minHeight = 50; scroll.Add(_environment); scroll.Add(UiKit.Button("Salvar o ambiente", SaveEnvironment));
            scroll.Add(UiKit.Divider()); scroll.Add(UiKit.Row(UiKit.Button("Auditoria", () => Journal("audit", "Auditoria do terreno")), UiKit.Button("Histórico", () => Journal("history", "Histórico do terreno"))));
        }

        private string Base => "/api/v1/worlds/islands/" + _island + "/land";

        private void ReadGridSize()
        {
            this.Then(_c.Api.ApiAsync("GET", Base), r =>
            {
                if (r.Status != 200) return; var root = Json.One(r.Body); _meters = (int)root.Number("size", 256); _cellMeters = (int)root.Number("cellSize", 4); _grid.Build(_meters, _cellMeters); if (_selected != Guid.Empty && _runs.TryGetValue(_selected, out var runs)) _grid.SetOwnedRuns(runs);
            });
        }

        private VisualElement ParcelRow(Row row)
        {
            var id = row.Id(); var price = row.Number("salePrice", row.Number("price")); var name = row.First("name"); _runs[id] = CellGrid.RunsOf(row.Element);
            var tick = UiKit.Check("Juntar", false, on => { if (on) _merge.Add(id); else _merge.Remove(id); });
            var line = UiKit.Row(UiKit.Text(name, "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("ownerName", "owner", "ownerUserId"), "lv-muted"), tick, UiKit.Button("Escolher", () => Choose(id, name)));
            if (price > 0) line.Add(Controls.Confirm("Comprar " + price.ToString("N0"), () => "Confirmar: " + price.ToString("N0") + " VerseCoin", () => Act("POST", Base + "/parcels/" + id + "/buy", JsonSerializer.Serialize(new { expectedPrice = price, idempotencyKey = Guid.NewGuid().ToString("N") }), "Terreno comprado.", () => _parcels.Reload()), "lv-btn-primary"));
            return line;
        }

        /// <summary>Ticks or unticks a parcel for the merge (what the "Juntar" boxes do).</summary>
        public void MarkForMerge(Guid id, bool on) { if (on) _merge.Add(id); else _merge.Remove(id); }

        public void Choose(Guid id, string name)
        {
            _selected = id; _access.Reload(); Say("Terreno escolhido: " + name, false); _grid.ClearChosen();
            if (_runs.TryGetValue(id, out var runs)) _grid.SetOwnedRuns(runs); else _grid.SetOwnedRuns(null);
        }

        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); }
        private void Act(string method, string path, string json, string success, Action then = null) => this.Then(_c.Api.ApiAsync(method, path, json), r => { if (r.Status >= 200 && r.Status < 300) { if (success != null) _c.Notices.Show(success, NoticeKind.Good); Say("", false); then?.Invoke(); } else Say(UiTasks.Explain(r.Status), true); });
        private bool NeedsParcel() { if (_selected != Guid.Empty) return false; Say("Escolha um terreno primeiro.", true); return true; }

        public void Sell()
        {
            if (NeedsParcel()) return;
            if (!long.TryParse(_price.value, out var price) || price <= 0) { Say("Digite um preço maior que zero.", true); return; }
            Act("POST", Base + "/parcels/" + _selected + "/sale", JsonSerializer.Serialize(new { price, buyerUserId = (Guid?)null, description = (string)null, includeObjects = true }), "Terreno à venda.", () => _parcels.Reload());
        }

        public void AddAccess()
        {
            if (_selected == Guid.Empty || !Guid.TryParse(_person.value.Trim(), out var user)) { Say("Escolha um terreno e digite o identificador da pessoa.", true); return; }
            int? minutes = int.TryParse(_minutes.value, out var m) && m > 0 ? m : (int?)null;
            Act("POST", Base + "/parcels/" + _selected + "/access", JsonSerializer.Serialize(new { userId = user, kind = _kind.value, minutes, reason = (string)null }), "Lista atualizada.", () => { _person.value = ""; _access.Reload(); });
        }

        public void Eject()
        {
            if (_selected == Guid.Empty || !Guid.TryParse(_person.value.Trim(), out var user)) { Say("Escolha um terreno e digite o identificador da pessoa.", true); return; }
            Act("POST", Base + "/parcels/" + _selected + "/eject", JsonSerializer.Serialize(new { userId = user }), "Pessoa expulsa do terreno.");
        }

        // ---- the shape ----

        public void Split()
        {
            if (NeedsParcel()) return; if (_grid.Chosen == 0) { Say("Escolha na grade as células que vão formar o novo terreno.", true); return; }
            var name = _newName.value.Trim(); if (name.Length < 2) { Say("Dê um nome ao novo terreno.", true); return; }
            Act("POST", Base + "/parcels/" + _selected + "/split", JsonSerializer.Serialize(new { cells = _grid.Cells, name }), "Terreno dividido.", () => { _grid.ClearChosen(); _parcels.Reload(); });
        }

        public void Reshape(string verb, string success)
        {
            if (NeedsParcel()) return; if (_grid.Chosen == 0) { Say("Escolha na grade as células.", true); return; }
            int.TryParse(_extra.value, out var extra);
            Act("POST", Base + "/parcels/" + _selected + "/" + verb, JsonSerializer.Serialize(new { cells = _grid.Cells, extraObjects = Math.Max(0, extra) }), success, () => { _grid.ClearChosen(); _parcels.Reload(); });
        }

        public void Merge()
        {
            if (_merge.Count < 2) { Say("Marque pelo menos dois terrenos para juntar.", true); return; }
            Act("POST", Base + "/merge", JsonSerializer.Serialize(new { parcelIds = _merge.ToArray(), primaryId = _selected == Guid.Empty ? (Guid?)null : _selected }), "Terrenos unidos.", () => { _merge.Clear(); _parcels.Reload(); });
        }

        // ---- presentation ----

        /// <summary>The checks before the music of a parcel is saved. Null when fine.</summary>
        public static string ValidateMusic(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? null : "O endereço da música precisa começar com http:// ou https://.";
        }

        public void SaveMedia()
        {
            if (NeedsParcel()) return; var problem = ValidateMusic(_music.value); if (problem != null) { Say(problem, true); return; }
            Act("PUT", Base + "/parcels/" + _selected + "/media", JsonSerializer.Serialize(new { musicUrl = string.IsNullOrWhiteSpace(_music.value) ? null : _music.value.Trim(), volume = _volume.value, autoplay = _autoplay.value, name = string.IsNullOrWhiteSpace(_musicName.value) ? null : _musicName.value.Trim() }), "Música salva.");
        }

        public void SaveEnvironment()
        {
            if (NeedsParcel()) return; var text = _environment.value.Trim();
            if (text.Length > 0) { try { using var _ = JsonDocument.Parse(text); } catch (JsonException) { Say("O ambiente precisa ser um JSON válido.", true); return; } }
            Act("PUT", Base + "/parcels/" + _selected + "/environment", JsonSerializer.Serialize(new { environment = text.Length == 0 ? null : text }), "Ambiente salvo.");
        }

        private void Journal(string what, string title)
        {
            if (NeedsParcel()) return;
            new ApiListWindow(_c, "parcel-" + what, title, Base + "/parcels/" + _selected + "/" + what, row => UiKit.Row(UiKit.Text(row.First("summary", "operation", "kind", "event"), "lv-muted"), UiKit.Spacer(), UiKit.Text(row.First("actorName", "userName") + "  " + DiscoveryWindow.When(row.Text("at", "createdAt")), "lv-dim", "lv-small")), "Sem registros.").Open();
        }

        public TextField NewNameField => _newName; public TextField MusicField => _music; public TextField EnvironmentField => _environment;
    }
}