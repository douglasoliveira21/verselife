using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>What the Texture tab is showing: the classic maps, the metal-and-roughness maps, or the media on the surface.</summary>
    public enum SurfaceView { Classic, Pbr, Media }

    /// <summary>
    /// The Texture tab: colour, transparency, glow; the classic maps, the metal-and-roughness maps or the media of a face; and how the picture repeats. With the "select face" tool and a face
    /// chosen, colour and picture apply to that face only. Everything is the material JSON the server keeps.
    /// </summary>
    public sealed partial class BuildWindow
    {
        private static readonly string[] ClassicOnly = { "radio_material_type", "Texture (diffuse)", "Bumpiness (normal)", "Shininess (specular)", "texture control", "label alphamode", "combobox alphamode", "bumpytexture control", "label bumpiness",
            "TexScaleU", "TexScaleV", "rptctrl", "TexRot", "TexOffsetU", "TexOffsetV", "checkbox planar align", "button align textures", "checkbox_sync_settings" };
        private static readonly string[] PbrOnly = { "radio_pbr_type", "Complete material", "Base color", "Metallic/roughness", "Emissive", "Normal", "pbr_control", "pbr_from_inventory", "edit_selected_pbr", "save_selected_pbr",
            "gltfTextureScaleU", "gltfTextureScaleV", "gltfRptctrl", "gltfTextureRotation", "gltfTextureOffsetU", "gltfTextureOffsetV" };
        private static readonly string[] MediaOnly = { "media_info", "add_media", "delete_media", "button align" };
        // components of the sheet that the island has nothing for in any view
        private static readonly string[] NeverShown = { "label maskcutoff", "maskcutoff", "combobox bumpiness", "shinytexture control", "label shininess", "combobox shininess", "label glossiness", "glossiness", "label environment", "environment", "label shinycolor", "shinycolorswatch",
            "bumpyScaleU", "bumpyScaleV", "bumpyRot", "bumpyOffsetU", "bumpyOffsetV", "shinyScaleU", "shinyScaleV", "shinyRot", "shinyOffsetU", "shinyOffsetV", "material_permissions_loading_label" };

        private Toggle _double; private Button _faceColour, _emissionColour; private BuildSpinner _bumpStrength, _roughness, _metal, _occlusion, _emissionStrength;
        private string _channel = "Texture (diffuse)", _pbr = "Complete material";
        public SurfaceView Surface { get; private set; } = SurfaceView.Classic;
        public Toggle DoubleSided => _double;
        public BuildSpinner NormalStrength => _bumpStrength;

        /// <summary>The name the person reads for a face id of a primitive.</summary>
        public static string FaceName(int id) => id == 0 ? "Topo (0)" : id == 1 ? "Base (1)" : "Lado " + (id - 1) + " (" + id + ")";

        /// <summary>The same rules the island applies to media: https, a public host name, no user name or port tricks.</summary>
        public static string MediaProblem(string type, string url)
        {
            if (type != "image" && type != "video") return "Escolha imagem ou vídeo.";
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return "O endereço precisa começar com https://.";
            if (!string.IsNullOrEmpty(uri.UserInfo)) return "O endereço não pode ter usuário e senha.";
            var host = uri.Host.ToLowerInvariant(); if (host == "localhost" || !host.Contains(".") || host.EndsWith(".local") || host.EndsWith(".internal") || System.Net.IPAddress.TryParse(host, out _)) return "Use um endereço de site público (não um número de rede nem um nome local).";
            return null;
        }

        /// <summary>The face the next colour or picture goes to, or -1 for the whole object.</summary>
        private int ChosenFace => Mode == BuildMode.Edit && _b.FacePick && _b.PickedFace >= 0 ? _b.PickedFace : -1;

        /// <summary>Changes the entry of the chosen face (the first face when none is chosen) inside the material; the rest of the object is untouched.</summary>
        public void EditFace(Action<JsonObject> change) => EditMaterial(m =>
        {
            var faces = m["faces"] as JsonArray ?? new JsonArray(); m["faces"] = faces; var index = Mathf.Max(0, _b.PickedFace);
            var entry = faces.OfType<JsonObject>().FirstOrDefault(x => x["index"]?.GetValue<int>() == index); if (entry == null) { entry = new JsonObject { ["index"] = index }; faces.Add(entry); } change(entry);
        });

        /// <summary>Takes off everything that is only of the chosen face.</summary>
        public void ClearFace() => EditMaterial(m => { if (m["faces"] is JsonArray faces) { var index = Mathf.Max(0, _b.PickedFace); var entry = faces.OfType<JsonObject>().FirstOrDefault(x => x["index"]?.GetValue<int>() == index); if (entry != null) faces.Remove(entry); if (faces.Count == 0) m.Remove("faces"); } });

        private void EditSurface(Action<JsonObject> change) { if (ChosenFace >= 0) EditFace(change); else EditMaterial(change); }

        /// <summary>Puts a texture on the object, or on the chosen face: a pattern of the system or an image of the inventory; the other kind is taken off.</summary>
        public void SetTexture(bool builtin, string id) => EditSurface(m => { m.Remove("texture"); m.Remove("builtin"); m.Remove("noTexture"); m[builtin ? "builtin" : "texture"] = id; });
        private void RemoveTexture() => EditSurface(m => { m.Remove("texture"); m.Remove("builtin"); if (ChosenFace >= 0) m["noTexture"] = true; });

        /// <summary>Puts a picture of the inventory as one of the other maps ("normalMap", "ormMap", "emissiveMap"), or takes it off (empty id).</summary>
        public void SetMap(string key, Guid asset) => EditMaterial(m => { if (asset == Guid.Empty) m.Remove(key); else m[key] = asset.ToString(); });

        private void PickMap(string key, string title) => PickTexture(title, Current(null, key), (builtin, id) => { if (!builtin && Guid.TryParse(id, out var asset)) SetMap(key, asset); }, () => SetMap(key, Guid.Empty), false);

        private void PickTexture(string title, (bool Builtin, string Id)? current, Action<bool, string> chosen, Action cleared, bool system = true) => Child("build-texture", () => new BuildTextureWindow(_c, title, current, chosen, cleared, system), true);

        /// <summary>The picture the object (or the chosen face) holds now under these two names of its material.</summary>
        private (bool Builtin, string Id)? Current(string builtinKey, string textureKey)
        {
            var material = Parse(First()?.MaterialJson); var face = ChosenFace >= 0 && material["faces"] is JsonArray faces ? faces.OfType<JsonObject>().FirstOrDefault(x => x["index"]?.GetValue<int>() == ChosenFace) : null;
            foreach (var source in new[] { face, material })
            {
                if (builtinKey != null && Text(source, builtinKey).Length > 0) return (true, Text(source, builtinKey));
                if (Text(source, textureKey).Length > 0) return (false, Text(source, textureKey));
            }
            return null;
        }

        private void PickColour(string current, string fallback, Action<string> chosen) => Child("build-colour", () => new BuildColourWindow(_c.Ui, string.IsNullOrEmpty(current) ? fallback : current, chosen), true);

        private BuildSpinner Spin(string name, string label, Rect area, float min, float max, float step, string format, Action<float> run)
        {
            var spinner = new BuildSpinner(label, 130f, label) { Min = min, Max = max, Step = step, Format = format }; _ui.Put(name, spinner, area, _pages["Textura"]);
            spinner.Committed += v => { if (!_filling) run(v); }; spinner.Refused += why => Say(why, true); return spinner;
        }

        private void BuildTexture()
        {
            var page = _pages["Textura"]; Rect At(string slot) { var a = _ui.AreaOf(slot); return new Rect(a.x, a.y - ScrollTop, a.width, a.height); }
            _faceColour = Swatch("Textura", "build-colour", "Cor", () => PickColour(Text(Parse(First()?.MaterialJson), "color"), "#FFFFFF", hex => EditSurface(m => m["color"] = hex)));
            OnNumber("ColorTrans", 0f, 100f, 5f, "0", v => EditSurface(m => m["transparency"] = Math.Round(1.0 - v / 100.0, 2)));
            OnNumber("glow", 0f, 8f, .1f, "0.00", v => EditSurface(m => { if (v <= 0f) { m.Remove("emission"); m.Remove("emissionStrength"); } else { if (!m.ContainsKey("emission")) m["emission"] = Text(m, "color").Length > 0 ? Text(m, "color") : "#FFFFFF"; m["emissionStrength"] = Math.Round(v, 2); } }));
            OnCheck("checkbox fullbright", v => EditSurface(m => m["fullBright"] = v));
            _ui.Unavailable("checkbox_hide_water", "o mar do jogo não pode ser escondido por face.");
            var bright = At("checkbox fullbright");
            _double = new Toggle { text = "Dois lados" }; _double.AddToClassList("bt-check"); _ui.Put("build-double", _double, new Rect(bright.x + 112f, bright.y, 110f, bright.height), page); _double.RegisterValueChangedCallback(e => { if (!_filling) EditMaterial(m => m["doubleSided"] = e.newValue); });

            Choices("combobox matmedia", new[] { "Clássica", "PBR (metal)", "Mídia" }); OnPick("combobox matmedia", i => { Surface = (SurfaceView)i; Refresh(); });
            foreach (var name in new[] { "Texture (diffuse)", "Bumpiness (normal)" }) { var channel = name; OnOption(channel, () => _channel = channel); }
            _ui.Unavailable("Shininess (specular)", "o desenho do jogo é só metal e aspereza.");
            foreach (var name in new[] { "Complete material", "Base color", "Metallic/roughness", "Emissive", "Normal" }) { var channel = name; OnOption(channel, () => _pbr = channel); }

            OnPress("texture control", () => PickTexture("Textura", Current("builtin", "texture"), (builtin, id) => SetTexture(builtin, id), RemoveTexture));
            OnPress("bumpytexture control", () => PickMap("normalMap", "Mapa de relevo"));
            Choices("combobox alphamode", new[] { "Mistura", "Nenhuma" }); OnPick("combobox alphamode", i => EditSurface(m => { if (i == 1) m["alphaMode"] = "none"; else m.Remove("alphaMode"); }));
            var strength = At("combobox alphamode");
            _bumpStrength = Spin("build-bump-strength", "", new Rect(strength.x, strength.y, 120f, 27f), 0f, 2f, .1f, "0.00", v => EditMaterial(m => m["normalStrength"] = Math.Round(v, 2)));

            // how the pictures repeat: one setting for all the maps
            foreach (var (u, v, du, dv) in new[] { ("TexScaleU", "TexScaleV", "TexOffsetU", "TexOffsetV"), ("gltfTextureScaleU", "gltfTextureScaleV", "gltfTextureOffsetU", "gltfTextureOffsetV") })
            {
                OnNumber(u, .1f, 16f, .1f, "0.00000", x => EditMaterial(m => m["uvRepeatX"] = Math.Round(x, 3))); OnNumber(v, .1f, 16f, .1f, "0.00000", x => EditMaterial(m => m["uvRepeatY"] = Math.Round(x, 3)));
                OnNumber(du, -1f, 1f, .05f, "0.00000", x => EditMaterial(m => m["uvOffsetX"] = Math.Round(x, 3))); OnNumber(dv, -1f, 1f, .05f, "0.00000", x => EditMaterial(m => m["uvOffsetY"] = Math.Round(x, 3)));
            }
            foreach (var name in new[] { "rptctrl", "gltfRptctrl", "TexRot", "gltfTextureRotation", "checkbox planar align", "button align textures", "button align", "combobox texgen", "checkbox_sync_settings" }) _ui.Unavailable(name, "rotação, mapeamento planar e alinhamento de textura chegam numa próxima fase.");
            _ui.Unavailable("clipboard_color_params_btn", "copiar e colar propriedades chega com o editor 3D."); _ui.Unavailable("clipboard_texture_params_btn", "copiar e colar propriedades chega com o editor 3D.");

            // metal and roughness: one picker per channel, with the numbers of the channel beside it
            foreach (var name in new[] { "pbr_control", "edit_selected_pbr", "save_selected_pbr" }) if (name != "pbr_control") _ui.Unavailable(name, "materiais PBR como itens do inventário pedem mudança na API.");
            OnPress("pbr_control", PickChannel); OnPress("pbr_from_inventory", PickChannel);
            var first = At("pbr_from_inventory"); var second = At("edit_selected_pbr"); var third = At("save_selected_pbr");
            _roughness = Spin("build-roughness", "Aspereza", new Rect(second.x, first.y, 260f, 27f), 0f, 1f, .05f, "0.00", v => EditMaterial(m => m["roughness"] = Math.Round(v, 2)));
            _metal = Spin("build-metal", "Metal", new Rect(second.x, second.y, 260f, 27f), 0f, 1f, .05f, "0.00", v => EditMaterial(m => m["metallic"] = Math.Round(v, 2)));
            _occlusion = Spin("build-occlusion", "Oclusão", new Rect(second.x, third.y, 260f, 27f), 0f, 1f, .05f, "0.00", v => EditMaterial(m => m["aoStrength"] = Math.Round(v, 2)));
            _emissionStrength = Spin("build-emission-strength", "Força do brilho", new Rect(second.x, third.y, 260f, 27f), 0f, 8f, .1f, "0.00", v => EditMaterial(m => { if (!m.ContainsKey("emission")) m["emission"] = "#FFFFFF"; m["emissionStrength"] = Math.Round(v, 2); }));
            _emissionColour = new Button(() => PickColour(Text(Parse(First()?.MaterialJson), "emission"), "#FFFFFF", hex => EditMaterial(m => { m["emission"] = hex; if (!m.ContainsKey("emissionStrength")) m["emissionStrength"] = 1.0; }))) { tooltip = "Cor do brilho" };
            _emissionColour.AddToClassList("lv-btn"); _emissionColour.AddToClassList("bt-picture"); _ui.Put("build-emission-colour", _emissionColour, new Rect(second.x, second.y, 70f, 29f), page);

            // media of the surface
            OnPress("add_media", OpenMedia); OnPress("delete_media", () => EditMaterial(m => m.Remove("media")));
        }

        private void PickChannel()
        {
            switch (_pbr)
            {
                case "Metallic/roughness": PickMap("ormMap", "Oclusão, aspereza e metal (ORM)"); break;
                case "Emissive": PickMap("emissiveMap", "Mapa de brilho"); break;
                case "Normal": PickMap("normalMap", "Mapa de relevo"); break;
                default: PickTexture("Cor base", Current("builtin", "texture"), (builtin, id) => SetTexture(builtin, id), RemoveTexture); break;
            }
        }

        private void OpenMedia()
        {
            if (_b.Selection.Count == 0) { Say("Selecione um objeto.", true); return; }
            Child("build-media", () => new BuildMediaWindow(_c.Ui, First()?.MaterialJson, media => EditMaterial(m => m["media"] = media)), true);
        }

        private void RefreshTexture(List<WorldObjectState> all, WorldObjectState first)
        {
            var has = first != null; var classic = Surface == SurfaceView.Classic; var pbr = Surface == SurfaceView.Pbr; var media = Surface == SurfaceView.Media;
            foreach (var name in ClassicOnly) _ui.Visible(name, classic);
            foreach (var name in PbrOnly) _ui.Visible(name, pbr);
            foreach (var name in MediaOnly) _ui.Visible(name, media);
            foreach (var name in NeverShown) _ui.Visible(name, false);
            _ui.Visible("tex gen", true); _ui.Visible("combobox texgen", true); Choice("combobox texgen", new[] { "Padrão" }, 0, false);
            if (media) foreach (var name in new[] { "checkbox_sync_settings", "TexScaleU", "TexScaleV", "rptctrl", "TexRot", "TexOffsetU", "TexOffsetV" }) _ui.Visible(name, true);
            Choice("combobox matmedia", new[] { "Clássica", "PBR (metal)", "Mídia" }, (int)Surface, has);
            Options(new[] { "Texture (diffuse)", "Bumpiness (normal)", "Shininess (specular)" }, n => n == _channel);
            Options(new[] { "Complete material", "Base color", "Metallic/roughness", "Emissive", "Normal" }, n => n == _pbr);
            foreach (var name in new[] { "Texture (diffuse)", "Bumpiness (normal)", "Complete material", "Base color", "Metallic/roughness", "Emissive", "Normal" }) _ui.Enable(name, has);

            var face = ChosenFace; var material = Parse(first?.MaterialJson);
            var over = face >= 0 && material["faces"] is JsonArray faces ? faces.OfType<JsonObject>().FirstOrDefault(x => x["index"]?.GetValue<int>() == face) : null;
            JsonNode Read(string key) => over != null && over[key] != null ? over[key] : material[key];
            float Num(string key, float fallback) => Read(key) is JsonValue v && v.TryGetValue<double>(out var n) && !double.IsNaN(n) ? (float)n : fallback;
            string Word(string key) => Read(key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

            _faceColour.SetEnabled(has); _faceColour.style.backgroundColor = ColourOf(Word("color"), Color.white); _faceColour.tooltip = face >= 0 ? "Cor da face " + FaceName(face) : "Cor";
            Number("ColorTrans", has ? Mathf.Round((1f - Num("transparency", 1f)) * 100f) : (float?)null, has);
            Number("glow", has ? (Word("emission").Length > 0 ? Num("emissionStrength", 1f) : 0f) : (float?)null, has);
            Check("checkbox fullbright", all, s => Flag(Parse(s.MaterialJson), "fullBright", false), has);
            _double.SetEnabled(has); var sides = all.Select(s => Flag(Parse(s.MaterialJson), "doubleSided", false)).Distinct().ToList(); _double.showMixedValue = sides.Count > 1; if (sides.Count <= 1) _double.SetValueWithoutNotify(sides.Count == 1 && sides[0]);

            var diffuse = _channel == "Texture (diffuse)";
            _ui.Visible("texture control", classic && diffuse); _ui.Visible("label alphamode", classic); _ui.Visible("combobox alphamode", classic && diffuse); _ui.Visible("bumpytexture control", classic && !diffuse); _ui.Visible("label bumpiness", false);
            if (_ui.Words("label alphamode") is { } label) label.text = diffuse ? BuildTexts.Of("tools", "label alphamode") : BuildTexts.Of("tools", "label bumpiness");
            _bumpStrength.style.display = classic && !diffuse || pbr && _pbr == "Normal" ? DisplayStyle.Flex : DisplayStyle.None; _bumpStrength.SetEnabled(has);
            var bumpArea = pbr ? new Vector2(_ui.AreaOf("edit_selected_pbr").x, _ui.AreaOf("edit_selected_pbr").y - ScrollTop) : new Vector2(_ui.AreaOf("combobox alphamode").x, _ui.AreaOf("combobox alphamode").y - ScrollTop); _bumpStrength.style.left = bumpArea.x; _bumpStrength.style.top = bumpArea.y;
            if (has) _bumpStrength.Show(Num("normalStrength", 1f)); else _bumpStrength.Blank();
            Choice("combobox alphamode", new[] { "Mistura", "Nenhuma" }, Word("alphaMode") == "none" ? 1 : 0, has);
            var builtin = Word("builtin"); var picture = Word("texture");
            _ui.Enable("texture control", has); Picture(_ui.Button("texture control"), builtin.Length > 0 ? builtin : null, picture.Length > 0);
            _ui.Enable("bumpytexture control", has); Picture(_ui.Button("bumpytexture control"), null, Word("normalMap").Length > 0);

            foreach (var (u, v, du, dv) in new[] { ("TexScaleU", "TexScaleV", "TexOffsetU", "TexOffsetV"), ("gltfTextureScaleU", "gltfTextureScaleV", "gltfTextureOffsetU", "gltfTextureOffsetV") })
            {
                Number(u, Same(all, s => Number(Parse(s.MaterialJson), "uvRepeatX", 1f)), has); Number(v, Same(all, s => Number(Parse(s.MaterialJson), "uvRepeatY", 1f)), has);
                Number(du, Same(all, s => Number(Parse(s.MaterialJson), "uvOffsetX", 0f)), has); Number(dv, Same(all, s => Number(Parse(s.MaterialJson), "uvOffsetY", 0f)), has);
            }
            foreach (var name in new[] { "rptctrl", "gltfRptctrl", "TexRot", "gltfTextureRotation" }) _ui.Spinner(name)?.Blank();

            // the channel of the metal-and-roughness view
            var metal = pbr && _pbr == "Metallic/roughness"; var emissive = pbr && _pbr == "Emissive"; var normal = pbr && _pbr == "Normal";
            _ui.Visible("pbr_from_inventory", pbr && !metal); _ui.Visible("edit_selected_pbr", pbr && !metal && !emissive && !normal); _ui.Visible("save_selected_pbr", pbr && !metal && !emissive && !normal);
            _roughness.style.display = _metal.style.display = _occlusion.style.display = metal ? DisplayStyle.Flex : DisplayStyle.None;
            _emissionColour.style.display = _emissionStrength.style.display = emissive ? DisplayStyle.Flex : DisplayStyle.None;
            if (pbr)
            {
                _ui.Enable("pbr_control", has); _ui.Enable("pbr_from_inventory", has);
                var map = metal ? Word("ormMap") : emissive ? Word("emissiveMap") : normal ? Word("normalMap") : picture;
                Picture(_ui.Button("pbr_control"), !metal && !emissive && !normal && builtin.Length > 0 ? builtin : null, map.Length > 0);
                if (_ui.Button("pbr_control") is { } tile) tile.tooltip = metal ? "Imagem de oclusão, aspereza e metal" : emissive ? "Imagem do brilho" : normal ? "Imagem do relevo" : "Cor base";
                foreach (var (spinner, key, fallback) in new[] { (_roughness, "roughness", .7f), (_metal, "metallic", 0f), (_occlusion, "aoStrength", 1f), (_emissionStrength, "emissionStrength", 1f) }) { spinner.SetEnabled(has); if (has) spinner.Show(Num(key, fallback)); else spinner.Blank(); }
                _emissionColour.SetEnabled(has); _emissionColour.style.backgroundColor = ColourOf(Word("emission"), Color.black);
            }
            if (_ui.Words("media_info") is { } info) { var current = material["media"] as JsonObject; info.text = current == null ? "Sem mídia nesta superfície" : Text(current, "url"); info.tooltip = info.text; }
            _ui.Enable("add_media", has); _ui.Enable("delete_media", has && material["media"] != null);
            if (_ui.Button("add_media") is { } add) add.text = material["media"] == null ? "Escolher…" : "Mudar…";
        }
    }
}
