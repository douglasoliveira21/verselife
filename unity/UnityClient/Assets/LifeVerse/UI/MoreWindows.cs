using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>A window that is just a list read from the API (history, audit, versions…).</summary>
    public sealed class ApiListWindow : FloatingWindow
    {
        public ApiList List { get; }
        public ApiListWindow(ApiContext context, string id, string title, string path, Func<Row, VisualElement> make, string empty = "Nada por aqui.", float width = 460f) : base(context.Ui, id, title, width, 440f, new Vector2(460, 120))
        {
            List = new ApiList(context, () => path, make, empty); Body.Add(List);
        }
    }

    /// <summary>
    /// A conversation read from and written to the platform's messages (the same API for groups, the island's local channel and the global one): the last 50 lines oldest first, a field to
    /// write (Enter sends; empty lines are not sent) and a refresh button. <c>listPath</c> and <c>body</c> say which conversation it is.
    /// </summary>
    public sealed class ConversationPane : VisualElement
    {
        private readonly ApiContext _c; private readonly Func<string> _listPath; private readonly Func<string, string> _body; private readonly ScrollView _log = new ScrollView(); private readonly TextField _input; private readonly Label _status = UiKit.Text("", "lv-small", "lv-muted");
        public int LineCount => _log.contentContainer.childCount;
        public TextField Input => _input;

        public ConversationPane(ApiContext context, Func<string> listPath, Func<string, string> body)
        {
            _c = context; _listPath = listPath; _body = body; style.flexGrow = 1; _log.style.flexGrow = 1; _log.style.minHeight = 120; Add(_status); Add(_log);
            _input = UiKit.Field("", false, 600); _input.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Send(); }, TrickleDown.TrickleDown); Add(_input);
            Add(UiKit.Row(UiKit.Button("Enviar", () => Send(), "lv-btn-primary"), UiKit.Button("Atualizar", Reload))); Reload();
        }

        public void Reload()
        {
            var path = _listPath(); if (path == null) { _log.Clear(); return; }
            this.Then(_c.Api.ApiAsync("GET", path), r =>
            {
                _log.Clear(); if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status); _status.AddToClassList("lv-bad"); return; } _status.text = ""; _status.RemoveFromClassList("lv-bad");
                var mine = _c.Api.UserId;
                foreach (var row in Json.Rows(r.Body).OrderBy(x => x.Text("createdAt")))
                {
                    var fromMe = Guid.TryParse(row.Text("senderUserId"), out var sender) && sender == mine; var who = fromMe ? "Você" : row.First("senderName", "senderUsername", "username", "senderUserId");
                    var line = UiKit.Text((who.Length > 0 ? who + ": " : "") + row.Text("content"), fromMe ? "lv-accent" : "lv-muted"); line.style.whiteSpace = WhiteSpace.Normal; line.style.marginBottom = 3; _log.Add(line);
                }
                _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue / 2f)).StartingIn(30);
            });
        }

        public bool Send()
        {
            var text = ChatPanel.Clean(_input.value); if (text == null) return false;
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/social/messages", _body(text)), r => { if (r.Status >= 200 && r.Status < 300) { _input.value = ""; Reload(); } else { _status.text = r.Status == 429 ? "Devagar: muitas mensagens seguidas." : UiTasks.Explain(r.Status); _status.AddToClassList("lv-bad"); } });
            return true;
        }
    }

    /// <summary>The pictures of the profile: the gallery (add one from a file, make it the main one or the cover, remove it) and the cover. Files are sent to the platform first (as textures) and then attached to the profile.</summary>
    public sealed class GalleryWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Label _status;

        public GalleryWindow(ApiContext context) : base(context.Ui, "gallery", "Galeria do perfil", 440f, 480f, new Vector2(480, 110))
        {
            _c = context; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            Body.Add(UiKit.Button("Enviar uma imagem…", () => Uploads.PickAndUpload(context, "Escolha uma imagem", Uploads.ImageFilter, "textures", AddUploaded, FilePickerStart()), "lv-btn-primary"));
            Body.Add(UiKit.Button("Tirar a capa", () => Act("PUT", "/api/v1/users/me/profile/cover", JsonSerializer.Serialize(new { assetId = (Guid?)null }), "Capa removida.")));
            Body.Add(UiKit.Text("Imagens", "lv-bold", "lv-accent"));
            _gallery = new VisualElement(); Body.Add(_gallery); Reload();
        }

        private readonly VisualElement _gallery;
        private static string FilePickerStart() => System.IO.Directory.Exists(DiscoveryWindow.PhotoFolder) ? DiscoveryWindow.PhotoFolder : null;

        public void Reload() => this.Then(_c.Api.ApiAsync("GET", "/api/v1/users/" + _c.Api.UserId + "/profile/full"), r =>
        {
            _gallery.Clear(); _count = 0; if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status); return; }
            var root = Json.One(r.Body).Element; var profile = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("profile", out var p) ? p : root;
            foreach (var holder in new[] { profile, root }) if (holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty("gallery", out var gallery) && gallery.ValueKind == JsonValueKind.Array) { foreach (var item in gallery.EnumerateArray()) AddRow(new Row(item.Clone()), ++_count); break; }
            if (_count == 0) _gallery.Add(UiKit.Text("Nenhuma imagem na galeria.", "lv-muted"));
        });

        private int _count; public int Images => _count;

        private void AddRow(Row row, int number)
        {
            var id = row.Id("id", "imageId"); var asset = row.Id("assetId"); var main = row.Flag("isMain");
            var line = UiKit.Row(UiKit.Text("Imagem " + number + (main ? "  ★ principal" : ""), "lv-bold"), UiKit.Spacer(), UiKit.Button("Principal", () => Act("PUT", "/api/v1/users/me/profile/gallery/" + id + "/main", null, null)), UiKit.Button("Capa", () => Act("PUT", "/api/v1/users/me/profile/cover", JsonSerializer.Serialize(new { assetId = asset }), "Capa trocada.")), Controls.Confirm("Remover", () => "Confirmar", () => Act("DELETE", "/api/v1/users/me/profile/gallery/" + id, null, null)));
            line.AddToClassList("lv-row"); _gallery.Add(line);
        }

        private void AddUploaded(UploadResult result)
        {
            if (!result.Ok) { _status.text = result.Message; _status.AddToClassList("lv-bad"); return; }
            Act("POST", "/api/v1/users/me/profile/gallery", JsonSerializer.Serialize(new { assetId = result.AssetId }), "Imagem adicionada à galeria.");
        }

        public void SetCover(Guid asset) => Act("PUT", "/api/v1/users/me/profile/cover", JsonSerializer.Serialize(new { assetId = asset }), "Capa trocada.");
        public void Remove(Guid image) => Act("DELETE", "/api/v1/users/me/profile/gallery/" + image, null, null);
        public void AddAsset(Guid asset) => AddUploaded(new UploadResult { Ok = true, AssetId = asset });

        private void Act(string method, string path, string json, string success) => this.Then(_c.Api.ApiAsync(method, path, json), r => { if (r.Status >= 200 && r.Status < 300) { if (success != null) _c.Notices.Show(success, NoticeKind.Good); _status.text = ""; Reload(); } else { _status.text = UiTasks.Explain(r.Status, r.Body); _status.AddToClassList("lv-bad"); } });
    }

    /// <summary>
    /// Marriage, as the API tells it: <c>{ "current": null | {id, spouseId, spouseName, since}, "incoming": [{id, fromId, fromName, message, createdAt}], "outgoing": [{id, toId, toName, …}] }</c>.
    /// The marriage the person has (with the divorce button, two touches), the proposals received (accept or decline), the ones they made (withdraw) and a form to propose to someone.
    /// </summary>
    public sealed class MarriageWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly ScrollView _list = new ScrollView(); private readonly TextField _who, _message; private readonly Label _status;
        public int Count => _list.contentContainer.childCount;
        public bool Married { get; private set; }
        public int Incoming { get; private set; }
        public int Outgoing { get; private set; }

        public MarriageWindow(ApiContext context) : base(context.Ui, "marriage", "Casamento", 420f, 460f, new Vector2(500, 130))
        {
            _c = context; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status); _list.style.flexGrow = 1; _list.style.minHeight = 140; Body.Add(_list);
            _who = UiKit.Field("Pedir em casamento (identificador)", false, 40); _message = UiKit.Field("Mensagem", false, 200); Body.Add(_who); Body.Add(_message); Body.Add(UiKit.Button("Fazer o pedido", Propose, "lv-btn-primary"));
            Reload();
        }

        public static string StateWords(string state) => state switch { "proposed" or "pending" => "pedido em aberto", "accepted" or "married" or "active" => "casados", "declined" => "recusado", "cancelled" or "canceled" => "retirado", "divorced" or "ended" => "encerrado", _ => state };

        public void Reload() => this.Then(_c.Api.ApiAsync("GET", "/api/v1/marriage"), r =>
        {
            _list.Clear(); Married = false; Incoming = Outgoing = 0; if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status); return; }
            var root = Json.One(r.Body).Element; if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object)
            {
                var marriage = new Row(current); Married = true; var since = DiscoveryWindow.When(marriage.Text("since"));
                Add(UiKit.Row(UiKit.Text("Casado(a) com " + marriage.First("spouseName") + (since.Length > 0 ? "  ·  desde " + since : ""), "lv-bold"), UiKit.Spacer(), Controls.Confirm("Divorciar", () => "Confirmar o divórcio", () => Act("/api/v1/marriage/divorce", null, "Divórcio registrado."), "lv-btn-secondary")));
            }
            foreach (var row in Rows(root, "incoming"))
            {
                var id = row.Id(); Incoming++; var line = UiKit.Row(UiKit.Text(row.First("fromName") + " quer casar com você" + (row.Text("message").Length > 0 ? ": " + row.Text("message") : ""), "lv-bold"), UiKit.Spacer(),
                    UiKit.Button("Aceitar", () => Act("/api/v1/marriage/" + id + "/accept", null, "Vocês estão casados!"), "lv-btn-primary"), UiKit.Button("Recusar", () => Act("/api/v1/marriage/" + id + "/decline", null, null))); Add(line);
            }
            foreach (var row in Rows(root, "outgoing")) { var id = row.Id(); Outgoing++; Add(UiKit.Row(UiKit.Text("Pedido a " + row.First("toName") + ": aguardando", "lv-muted"), UiKit.Spacer(), UiKit.Button("Retirar", () => Act("/api/v1/marriage/" + id + "/cancel", null, null)))); }
            if (Count == 0) _list.Add(UiKit.Text("Sem propostas nem casamento.", "lv-muted"));
        });

        private static IEnumerable<Row> Rows(JsonElement root, string name) { if (root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array) foreach (var item in list.EnumerateArray()) yield return new Row(item.Clone()); }
        private void Add(VisualElement line) { line.AddToClassList("lv-row"); _list.Add(line); }

        private void Act(string path, string json, string success) => this.Then(_c.Api.ApiAsync("POST", path, json), r => { if (r.Status >= 200 && r.Status < 300) { if (success != null) _c.Notices.Show(success, NoticeKind.Good); _status.text = ""; Reload(); } else { _status.text = UiTasks.Explain(r.Status); _status.AddToClassList("lv-bad"); } });

        public void Propose()
        {
            if (!Guid.TryParse(_who.value.Trim(), out var user)) { _status.text = "Digite o identificador da pessoa."; _status.AddToClassList("lv-bad"); return; }
            Act("/api/v1/marriage/propose", JsonSerializer.Serialize(new { userId = user, message = string.IsNullOrWhiteSpace(_message.value) ? null : _message.value.Trim() }), "Pedido enviado.");
        }

        public TextField WhoField => _who;
    }
    /// <summary>
    /// The grid of the island's land, drawn as one picture: a square per cell of the API (4 m by 4 m, so a 256 m island is 64 x 64), touched or dragged over to choose. Cell numbers are the API's:
    /// the island is centred, so on a 256 m island they run from -32 to 31 on each axis. The chosen parcel's cells show in teal (the API gives them as runs [row, first, last]); what the
    /// person chose, in orange. <see cref="Cells"/> is what the API wants for a split or a reshape: [[x, z], ...].
    /// </summary>
    public sealed class CellGrid : VisualElement
    {
        private readonly HashSet<(int X, int Z)> _chosen = new HashSet<(int, int)>(), _owned = new HashSet<(int, int)>();
        private bool _painting, _paintValue;
        public int Size { get; private set; }
        public int Origin { get; private set; }
        public float CellPixels { get; private set; } = 6f;
        public int Chosen => _chosen.Count;
        public int Owned => _owned.Count;

        public CellGrid() { name = "cell-grid"; style.marginTop = 4; style.marginBottom = 4; pickingMode = PickingMode.Position; generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; var cell = CellAt(e.localPosition); if (cell == null) return; _painting = true; _paintValue = !_chosen.Contains(cell.Value); Set(cell.Value, _paintValue); this.CapturePointer(e.pointerId); });
            RegisterCallback<PointerMoveEvent>(e => { if (!_painting) return; var cell = CellAt(e.localPosition); if (cell != null) Set(cell.Value, _paintValue); });
            RegisterCallback<PointerUpEvent>(e => { if (!_painting) return; _painting = false; this.ReleasePointer(e.pointerId); }); }

        /// <summary>Builds the grid for an island of <paramref name="meters"/> with cells of <paramref name="cellMeters"/> (at most 256 x 256 cells).</summary>
        public void Build(int meters, int cellMeters)
        {
            _chosen.Clear(); _owned.Clear(); Size = cellMeters <= 0 ? 0 : Mathf.Clamp(meters / cellMeters, 1, 256); Origin = -Size / 2; CellPixels = Size == 0 ? 6f : Mathf.Clamp(384f / Size, 2f, 14f);
            style.width = Size * CellPixels; style.height = Size * CellPixels; MarkDirtyRepaint();
        }

        public bool Inside((int X, int Z) cell) => cell.X >= Origin && cell.X < Origin + Size && cell.Z >= Origin && cell.Z < Origin + Size;

        private (int X, int Z)? CellAt(Vector2 local)
        {
            if (Size == 0) return null; var cell = (X: Origin + Mathf.FloorToInt(local.x / CellPixels), Z: Origin + Mathf.FloorToInt(local.y / CellPixels)); return Inside(cell) ? cell : ((int, int)?)null;
        }

        private void Set((int X, int Z) cell, bool on) { if (!Inside(cell)) return; if (on) _chosen.Add(cell); else _chosen.Remove(cell); MarkDirtyRepaint(); }

        public void Toggle((int X, int Z) cell) { if (!Inside(cell)) return; if (!_chosen.Remove(cell)) _chosen.Add(cell); MarkDirtyRepaint(); }
        public void ClearChosen() { _chosen.Clear(); MarkDirtyRepaint(); }

        /// <summary>The cells of the chosen parcel from the runs the API gives: [row, first, last] (a row is the z of the cell, first and last are x, both included).</summary>
        public void SetOwnedRuns(int[][] runs)
        {
            _owned.Clear(); if (runs != null) foreach (var run in runs) if (run != null && run.Length == 3 && run[2] - run[1] < 4096) for (var x = run[1]; x <= run[2]; x++) _owned.Add((x, run[0])); MarkDirtyRepaint();
        }

        public static int[][] RunsOf(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array) return null;
            var list = new List<int[]>(); foreach (var run in runs.EnumerateArray()) if (run.ValueKind == JsonValueKind.Array && run.GetArrayLength() == 3) list.Add(new[] { run[0].GetInt32(), run[1].GetInt32(), run[2].GetInt32() }); return list.ToArray();
        }

        private void Draw(MeshGenerationContext context)
        {
            if (Size == 0) return; var painter = context.painter2D; var full = Size * CellPixels;
            painter.fillColor = new Color(.13f, .17f, .2f); painter.BeginPath(); painter.MoveTo(Vector2.zero); painter.LineTo(new Vector2(full, 0)); painter.LineTo(new Vector2(full, full)); painter.LineTo(new Vector2(0, full)); painter.ClosePath(); painter.Fill();
            void Fill(IEnumerable<(int X, int Z)> cells, Color color)
            {
                painter.fillColor = color;
                foreach (var c in cells) { var x = (c.X - Origin) * CellPixels; var y = (c.Z - Origin) * CellPixels; painter.BeginPath(); painter.MoveTo(new Vector2(x, y)); painter.LineTo(new Vector2(x + CellPixels, y)); painter.LineTo(new Vector2(x + CellPixels, y + CellPixels)); painter.LineTo(new Vector2(x, y + CellPixels)); painter.ClosePath(); painter.Fill(); }
            }
            Fill(_owned, new Color(.07f, .6f, .56f)); Fill(_chosen, new Color(.96f, .64f, .25f));
            painter.strokeColor = new Color(1f, 1f, 1f, .10f); painter.lineWidth = 1f;
            for (var i = 0; i <= Size; i += 8) { var p = i * CellPixels; painter.BeginPath(); painter.MoveTo(new Vector2(p, 0)); painter.LineTo(new Vector2(p, full)); painter.Stroke(); painter.BeginPath(); painter.MoveTo(new Vector2(0, p)); painter.LineTo(new Vector2(full, p)); painter.Stroke(); }
        }

        /// <summary>The chosen cells as the API takes them: [[x, z], ...].</summary>
        public int[][] Cells => _chosen.OrderBy(c => c.Z).ThenBy(c => c.X).Select(c => new[] { c.X, c.Z }).ToArray();
    }
}