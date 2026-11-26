using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The "Recursos" tab of the build window, as in the Godot client: the light of the object (on, colour, energy, range, falloff, shadow; from the whole object or from one face, with its cone), a projector that
    /// throws a picture (a pattern of the system or an image of the inventory, with its opening, focus and the share of light outside the picture), and a reflection of the surroundings (intensity, range). All of it
    /// lives in the material JSON the server keeps, so everybody sees the same.
    /// </summary>
    public sealed partial class BuildWindow
    {
        private Toggle _lightOn, _lightShadow, _reflectOn; private VisualElement _lightColour; private Slider _lightEnergy, _lightRange, _lightFalloff, _lightCone, _projFov, _projFocus, _projAmbiance, _reflectIntensity, _reflectRange;
        private DropdownField _lightFace; private Label _projectorText;
        public Toggle LightOn => _lightOn; public Toggle ReflectOn => _reflectOn; public Slider LightEnergy => _lightEnergy; public DropdownField LightFace => _lightFace;

        public static readonly string[] LightColours = { "#FFE0A8", "#FFFFFF", "#FFB46B", "#9FD3FF", "#FF6B6B", "#7CFF9E", "#C59BFF" };

        private void BuildResources()
        {
            var tab = _tabs["Recursos"];
            tab.Add(UiKit.Text("Luz", "lv-bold"));
            _lightOn = UiKit.Check("O objeto ilumina em volta", false, v => { if (v) EditMaterial(m => { if (m["light"] is not JsonObject) m["light"] = new JsonObject { ["color"] = "#FFE0A8", ["energy"] = 1.5, ["range"] = 8.0, ["falloff"] = 1.0, ["shadow"] = false }; }); else EditMaterial(m => { m.Remove("light"); m.Remove("projector"); m.Remove("projectorBuiltin"); m.Remove("projectorTexture"); }); });
            _lightColour = Controls.ColourField("Cor da luz", "#FFE0A8", value => EditLight(l => l["color"] = value), LightColours);
            _lightEnergy = UiKit.Slide("Força", 0f, 8f, 1.5f, v => EditLight(l => l["energy"] = Math.Round(v, 2))); _lightRange = UiKit.Slide("Alcance (m)", .5f, 30f, 8f, v => EditLight(l => l["range"] = Math.Round(v, 1)));
            _lightFalloff = UiKit.Slide("Queda", .1f, 4f, 1f, v => EditLight(l => l["falloff"] = Math.Round(v, 2))); _lightShadow = UiKit.Check("Projeta sombra (só as primeiras luzes do mundo)", false, v => EditLight(l => l["shadow"] = v));
            _lightFace = new DropdownField("Sai de", new List<string> { "O objeto inteiro" }, 0); _lightFace.RegisterValueChangedCallback(_ => { if (_filling) return; var i = _lightFace.index; EditLight(l => { if (i <= 0 || i - 1 >= _faceIds.Length) l.Remove("face"); else l["face"] = _faceIds[i - 1]; }); });
            _lightCone = UiKit.Slide("Abertura do cone (graus)", 10f, 180f, 60f, v => EditLight(l => l["cone"] = Math.Round(v)));
            foreach (var e in new VisualElement[] { _lightOn, _lightColour, _lightEnergy, _lightRange, _lightFalloff, _lightShadow, _lightFace, _lightCone }) tab.Add(e);

            tab.Add(UiKit.Divider()); tab.Add(UiKit.Text("Projetor", "lv-bold")); _projectorText = UiKit.Text("Sem imagem: a luz é redonda.", "lv-small", "lv-muted"); _projectorText.style.whiteSpace = WhiteSpace.Normal; tab.Add(_projectorText);
            tab.Add(UiKit.Row(UiKit.Button("Escolher a imagem…", () => new TexturePickerWindow(_c, (builtin, id) => SetProjector(builtin, id), "Imagem do projetor").Open()), UiKit.Button("Tirar a imagem", () => EditMaterial(m => { m.Remove("projectorBuiltin"); m.Remove("projectorTexture"); }))));
            _projFov = UiKit.Slide("Abertura (graus)", 10f, 120f, 45f, v => EditProjector(p => p["fov"] = Math.Round(v))); _projFocus = UiKit.Slide("Foco (m)", 0f, 30f, 0f, v => EditProjector(p => p["focus"] = Math.Round(v, 1)));
            _projAmbiance = UiKit.Slide("Luz fora da imagem", 0f, 1f, .1f, v => EditProjector(p => p["ambiance"] = Math.Round(v, 2)), true); tab.Add(_projFov); tab.Add(_projFocus); tab.Add(_projAmbiance);

            tab.Add(UiKit.Divider()); tab.Add(UiKit.Text("Reflexo", "lv-bold"));
            _reflectOn = UiKit.Check("Reflete o que está em volta (superfícies brilhantes perto)", false, v => EditMaterial(m => { if (v) { if (m["reflection"] is not JsonObject) m["reflection"] = new JsonObject { ["intensity"] = .6, ["range"] = 8.0 }; } else m.Remove("reflection"); }));
            _reflectIntensity = UiKit.Slide("Intensidade", 0f, 1f, .6f, v => EditReflection(r => r["intensity"] = Math.Round(v, 2)), true); _reflectRange = UiKit.Slide("Alcance (m)", 2f, 30f, 8f, v => EditReflection(r => r["range"] = Math.Round(v, 1)));
            tab.Add(_reflectOn); tab.Add(_reflectIntensity); tab.Add(_reflectRange);
            tab.Add(UiKit.Text("Luzes e reflexos custam à placa de vídeo: só as primeiras do mundo ficam acesas (o limite está em Configurações › Gráficos).", "lv-small", "lv-muted"));
        }

        private void EditLight(Action<JsonObject> change) => EditMaterial(m => { var l = m["light"] as JsonObject; if (l == null) { l = new JsonObject { ["color"] = "#FFE0A8", ["energy"] = 1.5, ["range"] = 8.0 }; m["light"] = l; } change(l); });
        private void EditProjector(Action<JsonObject> change) => EditMaterial(m => { var p = m["projector"] as JsonObject; if (p == null) { p = new JsonObject(); m["projector"] = p; } change(p); });
        private void EditReflection(Action<JsonObject> change) => EditMaterial(m => { var r = m["reflection"] as JsonObject; if (r == null) { r = new JsonObject { ["intensity"] = .6, ["range"] = 8.0 }; m["reflection"] = r; } change(r); });

        /// <summary>Puts a picture in the projector (a pattern of the system or an image of the inventory); turns the light on when it was off.</summary>
        public void SetProjector(bool builtin, string id)
        {
            EditMaterial(m =>
            {
                m.Remove("projectorBuiltin"); m.Remove("projectorTexture"); m[builtin ? "projectorBuiltin" : "projectorTexture"] = id;
                if (m["light"] is not JsonObject) m["light"] = new JsonObject { ["color"] = "#FFFFFF", ["energy"] = 2.0, ["range"] = 10.0 };
            });
        }

        /// <summary>Puts a texture on the object (or on the chosen face, when one is chosen in Aparência): a pattern of the system or an image of the inventory; the other kind is taken off.</summary>
        public void SetTexture(bool builtin, string id) => EditMaterial(m => { m.Remove("texture"); m.Remove("builtin"); m[builtin ? "builtin" : "texture"] = id; });

        // ---- Conteúdo in folders, as the Godot client groups it ----
        public static readonly string[] ContentFolders = { "Scripts", "Objetos", "Sons", "Animações", "Imagens", "Texturas", "Materiais", "Notecards", "Outros" };
        public static string FolderOfCategory(string category) => category switch
        {
            "scripts" => "Scripts", "objects" or "models" or "packages" or "clothing" or "accessories" => "Objetos", "sounds" => "Sons", "animations" => "Animações", "images" or "photos" => "Imagens", "textures" => "Texturas", "materials" => "Materiais", "notecards" => "Notecards", _ => "Outros",
        };
        private VisualElement _contentTree;
        /// <summary>The folders of the content now shown (name and how many), for the tests.</summary>
        public readonly List<(string Folder, int Count)> ContentFoldersShown = new List<(string, int)>();

        private void DrawContentTree(List<(string Name, string Category)> entries)
        {
            if (_contentTree == null) return; _contentTree.Clear(); ContentFoldersShown.Clear();
            if (entries.Count == 0) { _contentTree.Add(UiKit.Text("O objeto não leva nada.", "lv-small", "lv-muted")); return; }
            foreach (var folder in ContentFolders)
            {
                var inside = entries.Where(e => FolderOfCategory(e.Category) == folder).ToList(); if (inside.Count == 0) continue; ContentFoldersShown.Add((folder, inside.Count));
                var fold = new Foldout { text = "📁 " + folder + " (" + inside.Count + ")", value = true }; foreach (var e in inside) fold.Add(UiKit.Text("• " + e.Name, "lv-small")); _contentTree.Add(fold);
            }
        }

        // ---- the names of the people of an object (creator, owner), looked up once ----
        private Label _people; private static readonly Dictionary<Guid, string> Names = new Dictionary<Guid, string>(); private static readonly HashSet<Guid> Asking = new HashSet<Guid>();
        public string PeopleText => _people?.text ?? "";

        /// <summary>The name of a person: "Você" for the one playing, known names at once, the others fetched from their public profile (and shown when they arrive).</summary>
        public string NameOf(Guid id, Action resolved)
        {
            if (id == Guid.Empty) return "—"; if (id == _c.Api.UserId) return "Você"; if (Names.TryGetValue(id, out var known)) return known;
            if (Asking.Add(id)) this.Then(_c.Api.ApiAsync("GET", "/api/v1/users/" + id + "/profile"), r =>
            {
                var name = id.ToString().Substring(0, 8);
                if (r.Status == 200) try { using var d = JsonDocument.Parse(r.Body); if (d.RootElement.TryGetProperty("displayName", out var shown) && shown.GetString() is { Length: > 0 } text) name = text; else if (d.RootElement.TryGetProperty("username", out var user) && user.GetString() is { Length: > 0 } u) name = u; } catch (JsonException) { }
                Names[id] = name; Asking.Remove(id); resolved?.Invoke();
            });
            return "…";
        }

        private void RefreshPeople(WorldObjectState first)
        {
            if (_people == null || first == null) return; var id = first.Id;
            void Again() { if (_b.Selection.Count == 1 && _b.Selection[0] == id) RefreshPeople(first); }
            _people.text = "Criador: " + NameOf(first.CreatorUserId, Again) + "   ·   Dono: " + NameOf(first.OwnerUserId, Again) + (first.GroupId is { } g && g != Guid.Empty ? "   ·   do grupo" : "");
        }

        private void RefreshResources(WorldObjectState first)
        {
            if (_lightOn == null || first == null) return;
            var light = WorldObjects.ReadLight(first.MaterialJson); var reflection = WorldObjects.ReadReflection(first.MaterialJson);
            _lightOn.SetValueWithoutNotify(light != null); foreach (var e in new VisualElement[] { _lightColour, _lightEnergy, _lightRange, _lightFalloff, _lightShadow, _lightFace, _lightCone, _projFov, _projFocus, _projAmbiance }) e.SetEnabled(light != null);
            var faces = new List<string> { "O objeto inteiro" }; faces.AddRange(_faceIds.Select(i => "Só a " + FaceName(i).ToLowerInvariant())); if (!faces.SequenceEqual(_lightFace.choices)) _lightFace.choices = faces;
            if (light != null)
            {
                _lightEnergy.SetValueWithoutNotify(light.Energy); _lightRange.SetValueWithoutNotify(light.Range); _lightFalloff.SetValueWithoutNotify(light.Falloff); _lightShadow.SetValueWithoutNotify(light.Shadow); _lightCone.SetValueWithoutNotify(light.Cone);
                var at = light.Face >= 0 ? Array.IndexOf(_faceIds, light.Face) + 1 : 0; _lightFace.SetValueWithoutNotify(_lightFace.choices.ElementAt(Mathf.Clamp(at, 0, _lightFace.choices.Count - 1)));
                _projFov.SetValueWithoutNotify(light.ProjectorFov); _projFocus.SetValueWithoutNotify(light.ProjectorFocus); _projAmbiance.SetValueWithoutNotify(light.Ambiance);
                _projectorText.text = light.ProjectorBuiltin != null ? "Imagem: " + BuiltInTextures.All.First(t => t.Id == light.ProjectorBuiltin).Label + " (do sistema)" : light.ProjectorTexture is { } asset ? "Imagem do inventário: " + asset.ToString().Substring(0, 8) : "Sem imagem: a luz é redonda.";
            }
            else _projectorText.text = "Ligue a luz para usar o projetor.";
            _reflectOn.SetValueWithoutNotify(reflection != null); _reflectIntensity.SetEnabled(reflection != null); _reflectRange.SetEnabled(reflection != null);
            if (reflection is { } r) { _reflectIntensity.SetValueWithoutNotify(r.Intensity); _reflectRange.SetValueWithoutNotify(r.Range); }
        }
    }

    /// <summary>
    /// "Escolha uma textura": the patterns of the system (drawn in code, free, everybody has them) with a small preview, and the images of the inventory. Used by the build window for the object's texture and for the
    /// picture of a projector.
    /// </summary>
    public sealed class TexturePickerWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Action<bool, string> _chosen; private readonly VisualElement _inventory;
        public int InventoryCount => _inventory.childCount;
        public TexturePickerWindow(ApiContext context, Action<bool, string> chosen, string title = "Escolha uma textura") : base(context.Ui, "texture-picker", title, 420f, 480f, new Vector2(460, 110))
        {
            _c = context; _chosen = chosen; var scroll = new ScrollView(); scroll.style.flexGrow = 1; Body.Add(scroll);
            scroll.Add(UiKit.Text("Do sistema", "lv-bold", "lv-accent")); var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; scroll.Add(grid);
            foreach (var (id, label) in BuiltInTextures.All)
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
}
