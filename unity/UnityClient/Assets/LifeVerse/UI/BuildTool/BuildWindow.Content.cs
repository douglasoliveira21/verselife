using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Shared.Items;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The Content tab: what the object carries, in folders, with a filter; putting an item of the inventory in it and taking one out; its scripts.</summary>
    public sealed partial class BuildWindow
    {
        public static readonly string[] ContentFolders = { "Scripts", "Objetos", "Sons", "Animações", "Imagens", "Texturas", "Materiais", "Notecards", "Outros" };
        public static string FolderOfCategory(string category) => category switch
        {
            "scripts" => "Scripts", "objects" or "models" or "packages" or "clothing" or "accessories" => "Objetos", "sounds" => "Sons", "animations" => "Animações", "images" or "photos" => "Imagens", "textures" => "Texturas", "materials" => "Materiais", "notecards" => "Notecards", _ => "Outros",
        };

        private ScrollView _tree; private Button _put, _take; private Guid _chosenContent; private string _contentText = "";
        /// <summary>The folders of the content now shown (name and how many).</summary>
        public readonly List<(string Folder, int Count)> ContentFoldersShown = new List<(string, int)>();
        public string ContentText => _contentText;
        public TextField ContentFilter => _ui.Text("contents_filter");

        private sealed class ContentEntry { public Guid Asset; public string Name, Category; }
        private static List<ContentEntry> ContentOf(WorldObjectState s)
        {
            var list = new List<ContentEntry>();
            try { foreach (var item in (JsonNode.Parse(string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson) as JsonArray ?? new JsonArray()).OfType<JsonObject>()) if (Guid.TryParse(item["assetId"]?.GetValue<string>(), out var asset)) list.Add(new ContentEntry { Asset = asset, Name = item["name"]?.GetValue<string>() ?? "Item", Category = item["category"]?.GetValue<string>() ?? "" }); }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException) { }
            return list;
        }

        private void BuildContent()
        {
            var page = _pages["Conteúdo"]; _tree = (ScrollView)_ui.Part("contents_inventory"); var area = _ui.AreaOf("contents_inventory"); area.y -= ScrollTop; _tree.style.height = area.height - 38f;
            _put = UiKit.Button("Pôr item…", PickContent); _put.AddToClassList("bt-btn"); _ui.Put("build-content-put", _put, new Rect(area.x, area.yMax - 31f, 130f, 29f), page);
            _take = UiKit.Button("Tirar", () => RemoveContent(_chosenContent)); _take.AddToClassList("bt-btn"); _take.tooltip = "Tira do objeto o item escolhido na lista"; _ui.Put("build-content-take", _take, new Rect(area.x + 138f, area.yMax - 31f, 90f, 29f), page);
            ContentFilter.RegisterValueChangedCallback(_ => Refresh());
            OnPress("button new script", () => { if (_b.Selection.Count == 1) new LuaEditorWindow(_c, _c.CurrentIsland(), _b.Selection[0]).Open(); else Say("Selecione um só objeto.", true); });
            if (_ui.Button("button new script") is { } scripts) scripts.tooltip = "Os scripts do objeto: criar, editar, executar";
            _ui.Unavailable("button new notecard", "as notas ainda não existem como arquivo do inventário.");
            OnPress("button permissions", ContentPermissions); _ui.Unavailable("button publish", "o LifeVerse não tem editor externo de scripts.");
        }

        /// <summary>What may be done with the content: it changes only when the object may be modified; an item inside keeps the rights it has in the inventory.</summary>
        public static string ContentRightsText(int objectRights, int? itemRights)
        {
            var text = (objectRights & ItemRights.Modify) != 0 ? "O objeto pode ser modificado: você muda o conteúdo dele." : "O objeto não pode ser modificado: o conteúdo é só de leitura.";
            if (itemRights is { } r) text += " Item: você pode " + InventoryManagerWindow.RightsWords(r) + ". Dentro do objeto ele mantém essas mesmas permissões.";
            return text;
        }

        private void ContentPermissions()
        {
            if (_b.Selection.Count != 1 || _b.StateOf(_b.Selection[0]) is not { } state) { Say("Selecione um só objeto.", true); return; }
            var asset = _chosenContent; if (asset == Guid.Empty) { Say(ContentRightsText(state.OwnerRights, null), false); return; }
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?page=1&pageSize=100&assetId=" + asset), r =>
            {
                int? rights = null; if (r.Status == 200) foreach (var row in Json.Rows(r.Body)) if (row.Id("assetId") == asset) { rights = (int)row.Number("ownerRights"); break; }
                Say(ContentRightsText(state.OwnerRights, rights) + (rights == null ? " (o item não está no seu inventário)" : ""), false);
            });
        }

        public void AddContentEntry(Guid asset, string name, string category)
        {
            _b.Edit(_b.Selection.ToList(), s =>
            {
                JsonArray array; try { array = JsonNode.Parse(string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson) as JsonArray ?? new JsonArray(); } catch (JsonException) { array = new JsonArray(); }
                if (array.Count >= 64 || array.OfType<JsonObject>().Any(x => x["assetId"]?.GetValue<string>() == asset.ToString())) return;
                array.Add(new JsonObject { ["assetId"] = asset.ToString(), ["name"] = name.Length > 120 ? name.Substring(0, 120) : name, ["category"] = category }); s.ContentJson = array.ToJsonString();
            }, "conteúdo");
        }

        public void RemoveContent(Guid asset)
        {
            if (asset == Guid.Empty) { Say("Escolha na lista o item que sai.", true); return; }
            _b.Edit(_b.Selection.ToList(), s => { var array = JsonNode.Parse(string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson) as JsonArray ?? new JsonArray(); var entry = array.OfType<JsonObject>().FirstOrDefault(x => x["assetId"]?.GetValue<string>() == asset.ToString()); if (entry != null) { array.Remove(entry); s.ContentJson = array.ToJsonString(); } }, "conteúdo");
            _chosenContent = Guid.Empty;
        }

        private void PickContent()
        {
            if (_b.Selection.Count != 1) { Say("Selecione um só objeto para mudar o conteúdo.", true); return; }
            Child("inventory-pick", () => new InventoryPickWindow(_c, (Guid asset, string name, string category) => AddContentEntry(asset, name, category.Length > 0 ? category : "objects"), "Pôr no objeto"), true);
        }

        private void RefreshContent(List<WorldObjectState> all, WorldObjectState first)
        {
            var one = all.Count == 1; var modify = one && (first.OwnerRights & ItemRights.Modify) != 0;
            _ui.Enable("button new script", one); _ui.Enable("button permissions", one); _put.SetEnabled(modify);
            var entries = one ? ContentOf(first) : new List<ContentEntry>(); if (!entries.Any(e => e.Asset == _chosenContent)) _chosenContent = Guid.Empty; _take.SetEnabled(modify && _chosenContent != Guid.Empty);
            _contentText = first == null ? "" : entries.Count == 0 ? "O objeto não leva nada." : string.Join("\n", entries.Select(e => "• " + e.Name + "  (" + e.Category + ")"));
            var wanted = SettingsCatalog.Fold(ContentFilter.value ?? "").Trim(); var shown = entries.Where(e => wanted.Length == 0 || SettingsCatalog.Fold(e.Name).Contains(wanted)).ToList();
            _tree.Clear(); ContentFoldersShown.Clear();
            if (shown.Count == 0) { var nothing = UiKit.Text(first == null ? "" : !one ? "Selecione um só objeto." : entries.Count == 0 ? "O objeto não leva nada." : "Nada com esse nome.", "lv-small", "lv-muted"); nothing.style.whiteSpace = WhiteSpace.Normal; _tree.Add(nothing); return; }
            foreach (var folder in ContentFolders)
            {
                var inside = shown.Where(e => FolderOfCategory(e.Category) == folder).ToList(); if (inside.Count == 0) continue; ContentFoldersShown.Add((folder, inside.Count));
                var head = UiKit.Text(folder + " (" + inside.Count + ")", "lv-small", "lv-bold"); head.style.marginTop = 4; _tree.Add(head);
                foreach (var entry in inside)
                {
                    var captured = entry; var row = new Button(() => { _chosenContent = captured.Asset; Refresh(); }) { text = "      " + entry.Name }; row.AddToClassList("lv-btn"); row.AddToClassList("bt-row"); row.EnableInClassList("bt-row-on", entry.Asset == _chosenContent); _tree.Add(row);
                }
            }
        }
    }
}
