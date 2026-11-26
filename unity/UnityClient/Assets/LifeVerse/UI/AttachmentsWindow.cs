using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using LifeVerse.Shared.Avatar;
using LifeVerse.Unity.Avatar;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// What the avatar wears on the body: the list of attachments (put on / taken off, moved to another point, position, rotation and scale) and a form to wear a new object of the inventory at a point.
    /// Every change goes to the platform; the answer refreshes the list and the avatar the person sees.
    /// </summary>
    public sealed class AttachmentsWindow : FloatingWindow
    {
        public const string Route = "/api/v1/users/me/avatar/attachments";
        private readonly ApiContext _c; private readonly Action _changed; private readonly ScrollView _list = new ScrollView(); private readonly Label _status;
        private readonly DropdownField _item, _point; private readonly List<Guid> _itemIds = new List<Guid>(); private List<string> _profiles = new List<string> { AttachmentPoints.Humanoid };
        public List<AttachmentData> Attachments { get; private set; } = new List<AttachmentData>();
        public int Count => _list.contentContainer.childCount;
        public DropdownField ItemField => _item;
        public DropdownField PointField => _point;

        private readonly Func<AttachmentData, FloatingWindow> _edit; private readonly DropdownField _region;
        public DropdownField RegionField => _region;

        public AttachmentsWindow(ApiContext context, Action changed = null, Func<AttachmentData, FloatingWindow> edit = null) : base(context.Ui, "attachments", "Anexos do corpo", 520f, 560f, new Vector2(420, 90))
        {
            _c = context; _changed = changed; _edit = edit; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            _list.style.flexGrow = 1; _list.style.minHeight = 180; Body.Add(_list);
            Body.Add(UiKit.Text("Vestir um objeto", "lv-bold", "lv-accent"));
            _item = new DropdownField("Item do inventário", new List<string> { "(carregando…)" }, 0); _point = new DropdownField("Ponto do corpo", PointNames(_profiles), 0);
            _region = new DropdownField("Região do corpo", Regions.Select(r => r.Text).ToList(), 0); _region.RegisterValueChangedCallback(_ => { _point.choices = PointNames(_profiles, RegionOf(_region.index)); _point.index = 0; });
            Body.Add(_item); Body.Add(_region); Body.Add(_point);
            Body.Add(UiKit.Button("Vestir", Wear, "lv-btn-primary"));
            Reload(); LoadItems();
        }

        /// <summary>The regions of the body the points are grouped by ("" is every point).</summary>
        public static readonly (string Id, string Text)[] Regions = { ("", "Todas"), (AttachmentPoints.Head, "Cabeça"), (AttachmentPoints.Torso, "Tronco"), (AttachmentPoints.Arms, "Braços e mãos"), (AttachmentPoints.Legs, "Pernas e pés"), (AttachmentPoints.Special, "Especiais"), (AttachmentPoints.Hud, "Tela (HUD)") };
        public static string RegionOf(int index) => index >= 0 && index < Regions.Length ? Regions[index].Id : "";

        /// <summary>The names of the points of the body that the person's profiles allow, by region (head first), optionally only of one region.</summary>
        public static List<string> PointNames(IEnumerable<string> profiles, string region = null) => AttachmentPoints.All.Where(p => (p.Profile == AttachmentPoints.Humanoid || (profiles ?? Array.Empty<string>()).Contains(p.Profile, StringComparer.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(region) || p.Region == region)).OrderBy(p => Array.IndexOf(AttachmentPoints.Regions, p.Region)).ThenBy(p => p.Id).Select(p => p.Name).ToList();

        public static string KeyOfName(string name) => AttachmentPoints.All.FirstOrDefault(p => p.Name == name)?.Key;

        /// <summary>Three numbers typed as "x y z" (comma or dot decimals); null when they are not three numbers.</summary>
        public static float[] ParseTriple(string text)
        {
            var parts = (text ?? "").Replace(';', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (parts.Length != 3) return null; var result = new float[3];
            for (var i = 0; i < 3; i++) if (!float.TryParse(parts[i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]) || float.IsNaN(result[i]) || float.IsInfinity(result[i])) return null;
            return result;
        }

        public static string Triple(float[] v) => v == null || v.Length != 3 ? "0 0 0" : string.Join(" ", v.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)));

        public void Reload() => this.Then(_c.Api.ApiAsync("GET", Route), r =>
        {
            _list.Clear(); if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status, r.Body); return; }
            Attachments = AttachmentData.ParseList(r.Body, out var profiles); if (profiles.Count > 0) { _profiles = profiles; _point.choices = PointNames(_profiles); if (_point.index < 0) _point.index = 0; }
            foreach (var a in Attachments) _list.Add(RowOf(a)); if (Attachments.Count == 0) _list.Add(UiKit.Text("Você não está vestindo nenhum objeto.", "lv-muted")); _status.text = "";
        });

        private void LoadItems() => this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?view=all&page=1&pageSize=50"), r =>
        {
            _itemIds.Clear(); var names = new List<string>(); if (r.Status == 200) foreach (var row in Json.Rows(r.Body)) { var id = row.Id("id", "itemId"); if (id == Guid.Empty) continue; _itemIds.Add(id); names.Add(row.First("name", "displayName")); }
            if (names.Count == 0) names.Add("(inventário vazio)"); _item.choices = names; _item.index = 0;
        });

        private VisualElement RowOf(AttachmentData a)
        {
            var box = new VisualElement(); box.AddToClassList("lv-row"); box.style.flexDirection = FlexDirection.Column; box.style.marginBottom = 6;
            var text = UiKit.Text(a.Describe() + (a.Equipped ? "" : " · guardado"), "lv-small"); text.style.whiteSpace = WhiteSpace.Normal; box.Add(text);
            var position = UiKit.Field("Posição (m)", false, 40, Triple(a.Position)); var rotation = UiKit.Field("Rotação (°)", false, 40, Triple(a.Rotation)); var scale = UiKit.Field("Escala", false, 40, Triple(a.Scale)); var sway = UiKit.Field("Balanço (0 a 1)", false, 8, a.Sway.ToString("0.##", CultureInfo.InvariantCulture));
            var buttons = UiKit.Row(UiKit.Button(a.Equipped ? "Tirar" : "Vestir", () => (a.Equipped ? (Action)(() => Send("DELETE", Route + "/" + a.Id, null)) : () => Send("POST", Route, WearJson(a.InventoryItemId, a.Point, null)))()),
                UiKit.Button("Aplicar", () => Apply(a, position.value, rotation.value, scale.value, sway.value), "lv-btn-primary"), UiKit.Button("Voltar ao padrão", () => Send("POST", Route + "/" + a.Id + "/reset", null)));
            if (a.CanModify) { box.Add(position); box.Add(rotation); box.Add(scale); box.Add(sway); }
            box.Add(buttons);
            if (_edit != null && a.Equipped && a.CanModify && !a.IsHud) box.Add(UiKit.Button("Editar no corpo (setas, anéis e caixas)", () => _edit(a)?.Open()));
            var target = new DropdownField("Levar para", PointNames(_profiles), Math.Max(0, PointNames(_profiles).IndexOf(a.PointName))); var replace = new Toggle("Trocar o que já está nesse ponto");
            box.Add(target); box.Add(replace); box.Add(UiKit.Button("Mover", () => Move(a, target.value, replace.value))); return box;
        }

        private static string WearJson(Guid item, string point, float[] position) => JsonSerializer.Serialize(new { inventoryItemId = item, point, mode = (string)null, position });

        /// <summary>The sway typed as a number from 0 to 1 (comma or dot); null when it is not one.</summary>
        public static float? ParseSway(string text) => float.TryParse((text ?? "").Trim().Replace(',','.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0f && v <= 1f ? v : (float?)null;

        public void Apply(AttachmentData a, string position, string rotation, string scale, string sway = null)
        {
            var p = ParseTriple(position); var r = ParseTriple(rotation); var s = ParseTriple(scale); var w = sway == null ? a.Sway : ParseSway(sway);
            if (p == null || r == null || s == null) { Fail("Digite três números em cada campo (por exemplo: 0 0.1 0)."); return; }
            if (w == null) { Fail("O balanço precisa ficar entre 0 e 1."); return; }
            Send("PATCH", Route + "/" + a.Id, JsonSerializer.Serialize(new { position = p, rotation = r, scale = s, sway = w }));
        }

        /// <summary>Takes a worn object to another point of the body (or of the screen); "replace" takes off what is already there. The placement starts over from the new point.</summary>
        public void Move(AttachmentData a, string pointName, bool replace)
        {
            var key = KeyOfName(pointName); if (key == null) { Fail("Escolha o ponto."); return; }
            Send("POST", Route + "/" + a.Id + "/move", JsonSerializer.Serialize(new { point = key, mode = replace ? "replace" : "add" }));
        }

        public void Wear()
        {
            if (_itemIds.Count == 0 || _item.index < 0 || _item.index >= _itemIds.Count) { Fail("Escolha um item do inventário."); return; }
            var key = KeyOfName(_point.value); if (key == null) { Fail("Escolha o ponto do corpo."); return; }
            Send("POST", Route, WearJson(_itemIds[_item.index], key, null));
        }

        public void Detach(AttachmentData a) => Send("DELETE", Route + "/" + a.Id, null);

        public static string Explain(string body)
        {
            try { using var d = JsonDocument.Parse(body); var error = d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                return error switch { "already_attached" => "Esse objeto já está vestido.", "too_many_attachments" => "Você já veste o máximo de objetos.", "too_many_at_point" => "Esse ponto já está cheio.", "too_many_huds" => "Você já usa o máximo de objetos de tela.", "item_unavailable" => "O item não está mais com você.", "asset_unavailable" => "O arquivo do item não está mais disponível.", "not_attachable" => "Esse tipo de item não pode ser vestido.", "not_attached" => "O objeto não está vestido.", "bad_sway" => "O balanço precisa ficar entre 0 e 1.", "no_modify_right" => "Você não tem o direito de modificar esse objeto.", "point_unavailable" => "O seu corpo não tem esse ponto.", "unknown_point" => "Esse ponto não existe.", _ => null }; }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException) { return null; }
        }

        private void Send(string method, string path, string json) => this.Then(_c.Api.ApiAsync(method, path, json), r =>
        {
            if (r.Status >= 200 && r.Status < 300) { _status.RemoveFromClassList("lv-bad"); Reload(); _changed?.Invoke(); }
            else Fail(Explain(r.Body) ?? UiTasks.Explain(r.Status, r.Body));
        });

        private void Fail(string message) { _status.text = message; _status.AddToClassList("lv-bad"); }
    }
}
