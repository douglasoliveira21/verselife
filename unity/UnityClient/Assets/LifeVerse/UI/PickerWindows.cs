using System;
using System.Collections.Generic;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Escolha uma textura": the patterns of the system (drawn in code, free, everybody has them) with a small preview, and the images of the inventory. Used by the profile, the groups and the parcels (the build tool has
    /// its own picker, laid out as the viewer's).
    /// </summary>
    public sealed class TexturePickerWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Action<bool, string> _chosen; private readonly VisualElement _inventory;
        public int InventoryCount => _inventory.childCount;
        public TexturePickerWindow(ApiContext context, Action<bool, string> chosen, string title = "Escolha uma textura", bool systemPatterns = true) : base(context.Ui, "texture-picker", title, 420f, 480f, new Vector2(460, 110))
        {
            _c = context; _chosen = chosen; var scroll = new ScrollView(); scroll.style.flexGrow = 1; Body.Add(scroll);
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap;
            if (systemPatterns) { scroll.Add(UiKit.Text("Do sistema", "lv-bold", "lv-accent")); scroll.Add(grid); }
            foreach (var (id, label) in systemPatterns ? (IEnumerable<(string, string)>)BuiltInTextures.All : Array.Empty<(string, string)>())
            {
                var pick = id; var cell = new VisualElement(); cell.style.width = 88; cell.style.marginRight = 6; cell.style.marginBottom = 6; cell.style.alignItems = Align.Center;
                var preview = new Image { image = BuiltInTextures.Get(id), scaleMode = ScaleMode.ScaleToFit }; preview.style.width = 72; preview.style.height = 72; cell.Add(preview);
                var button = UiKit.Button(label, () => Choose(true, pick), "lv-btn-flat"); button.style.fontSize = 11; cell.Add(button); grid.Add(cell);
            }
            scroll.Add(UiKit.Text("Do inventário", "lv-bold", "lv-accent")); _inventory = new VisualElement(); scroll.Add(_inventory);
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?category=textures&page=1&pageSize=100", null), r =>
            {
                if (r.Status != 200) { _inventory.Add(UiKit.Text(UiTasks.Explain(r.Status, r.Body), "lv-bad")); return; }
                var rows = Json.Rows(r.Body); if (rows.Count == 0) _inventory.Add(UiKit.Text("Você não tem imagens no inventário.", "lv-muted", "lv-small"));
                foreach (var row in rows) { var asset = row.Id("assetId"); var label = row.First("name", "displayName"); _inventory.Add(UiKit.Button(label.Length > 0 ? label : asset.ToString().Substring(0, 8), () => Choose(false, asset.ToString()))); }
            });
        }

        public void Choose(bool builtin, string id) { _chosen?.Invoke(builtin, id); Close(); }
    }

    /// <summary>"Escolha um item": the items of the inventory (any kind), with a search; picking one gives its asset and its name (and, when asked, its category).</summary>
    public sealed class InventoryPickWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Action<Guid, string, string> _chosen; private readonly VisualElement _list; private readonly TextField _search;
        public int Count => _list.childCount;

        public InventoryPickWindow(ApiContext context, Action<Guid, string> chosen, string title = "Escolha um item") : this(context, (asset, name, _) => chosen?.Invoke(asset, name), title) { }

        public InventoryPickWindow(ApiContext context, Action<Guid, string, string> chosen, string title = "Escolha um item") : base(context.Ui, "inventory-pick", title, 420f, 460f, new Vector2(470, 120))
        {
            _c = context; _chosen = chosen; _search = UiKit.Field("Buscar", false, 60); Body.Add(_search); _search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) Load(); }, TrickleDown.TrickleDown);
            Body.Add(UiKit.Button("Buscar", Load)); var scroll = new ScrollView(); scroll.style.flexGrow = 1; _list = new VisualElement(); scroll.Add(_list); Body.Add(scroll); Load();
        }

        private void Load() => this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?page=1&pageSize=100&q=" + Uri.EscapeDataString(_search.value ?? "")), r =>
        {
            _list.Clear(); if (r.Status != 200) { _list.Add(UiKit.Text(UiTasks.Explain(r.Status, r.Body), "lv-bad")); return; }
            var rows = Json.Rows(r.Body); if (rows.Count == 0) _list.Add(UiKit.Text("Nada no inventário com esse nome.", "lv-muted", "lv-small"));
            foreach (var row in rows) { var asset = row.Id("assetId"); var label = row.First("name", "displayName"); var category = row.First("category", "content"); _list.Add(UiKit.Button((label.Length > 0 ? label : asset.ToString().Substring(0, 8)) + "  ·  " + category, () => { _chosen?.Invoke(asset, label, category); Close(); }, "lv-btn-flat")); }
        });
    }
}
