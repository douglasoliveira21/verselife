using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeVerse.Unity.Prototypes.BuildEditor
{
    public enum Marks { None, Focus, Selection, Move, Rotate, Scale, Face, Ghost, Terrain, Grid, Multi, Linked }

    public enum Subject { Nothing, Crate, Ring, Several, Table }

    /// <summary>One of the twenty states to show: the mode, the tab, what is selected, and the sample values on screen. The rules of what is visible follow the C++ of the viewer (see the parity matrix).</summary>
    public sealed class Scenario
    {
        public int Number;
        public string Id, Title;
        public string Mode = "Editar", Tool = "position", Tab = "Geral", Surface = "classic", Extra;
        public Subject Selected = Subject.Crate;
        public Marks Marks = Marks.Move;
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public readonly HashSet<string> Checked = new HashSet<string>(), Mixed = new HashSet<string>(), Off = new HashSet<string>(), Gone = new HashSet<string>();
        /// <summary>The components numbered in the side-by-side comparison, with why each differs (empty: placed as in the original).</summary>
        public readonly List<(string Name, string Note)> Compare = new List<(string, string)>();

        public bool HasSelection => Selected != Subject.Nothing;

        /// <summary>Things that need a change of database, server or protocol (proposal 5): drawn as unavailable, with the reason.</summary>
        public static readonly Dictionary<string, string> Unavailable = new Dictionary<string, string>
        {
            ["search_check"] = "pede campo novo no banco", ["button apply to selection"] = "pede mensagem nova no servidor de ilha",
            ["ToolTetrahedron"] = "forma nova na API e no servidor", ["ToolHemiCylinder"] = "forma nova na API e no servidor", ["ToolHemiCone"] = "forma nova na API e no servidor",
            ["hole"] = "formato do oco: gerador e validação da API", ["Physics Gravity"] = "física no servidor de ilha", ["Physics Density"] = "física no servidor de ilha",
            ["button new notecard"] = "tipo de arquivo novo", ["pbr_control"] = "material PBR como item pede API", ["edit_selected_pbr"] = "material PBR como item pede API", ["save_selected_pbr"] = "material PBR como item pede API",
            ["radio Shininess (specular)"] = "o desenho do jogo é só PBR", ["button publish"] = "não há editor externo",
        };

        private static readonly string[] BoxHidden = { "text skew", "Skew", "text taper2", "Taper X", "Taper Y", "text radius delta", "text revolutions", "Radius Offset", "Revolutions", "scale_hole", "advanced_cut", "advanced_dimple", "sculpt texture control", "sculpt mirror control", "sculpt invert control", "label sculpt type", "sculpt type control" };
        private static readonly string[] RingHidden = { "scale_taper", "advanced_dimple", "advanced_slice", "sculpt texture control", "sculpt mirror control", "sculpt invert control", "label sculpt type", "sculpt type control" };

        private static readonly string[] Classic = { "radio_material_type", "Texture (diffuse)", "Bumpiness (normal)", "Shininess (specular)", "clipboard_texture_params_btn", "checkbox_sync_settings", "texture control", "label alphamode", "combobox alphamode", "tex gen", "combobox texgen", "TexScaleU", "TexScaleV", "rptctrl", "TexRot", "TexOffsetU", "TexOffsetV", "checkbox planar align", "button align textures" };
        private static readonly string[] Pbr = { "radio_pbr_type", "Complete material", "Base color", "Metallic/roughness", "Emissive", "Normal", "clipboard_texture_params_btn", "pbr_control", "pbr_from_inventory", "edit_selected_pbr", "save_selected_pbr", "tex gen", "combobox texgen", "gltfTextureScaleU", "gltfTextureScaleV", "gltfRptctrl", "gltfTextureRotation", "gltfTextureOffsetU", "gltfTextureOffsetV" };
        private static readonly string[] Media = { "checkbox_sync_settings", "media_info", "add_media", "delete_media", "button align", "tex gen", "combobox texgen", "TexScaleU", "TexScaleV", "rptctrl", "TexRot", "TexOffsetU", "TexOffsetV", "button align textures" };
        private static readonly string[] TextureAlways = { "clipboard_color_params_btn", "color label", "colorswatch", "color trans", "ColorTrans", "glow label", "glow", "checkbox fullbright", "checkbox_hide_water", "object_horizontal", "label_matmedia", "combobox matmedia" };

        public bool Visible(Widget w)
        {
            if (w.Window != "tools") return w.VisibleAtLoad;
            if (Gone.Contains(w.Name)) return false;
            if (w.Mode != null) return w.Mode == Mode && w.VisibleAtLoad;
            if (w.Group == "Informações da seleção")
                return w.Name == "selection_empty" ? !HasSelection && Mode != "Terreno" : w.Name == "selection_faces" ? Tool == "face" && Mode == "Editar" : w.Name == "selection_count" ? HasSelection && Mode != "Terreno" : Mode != "Terreno";
            if (w.Name == "Object Info Tabs") return Mode != "Terreno";
            if (w.Tab == null) return w.VisibleAtLoad;
            if (Mode == "Terreno" || w.Tab != Tab) return false;
            switch (w.Tab)
            {
                case "Geral":
                    if (w.Name == "Creator Icon" || w.Name == "Owner Icon") return HasSelection;
                    // the six permission masks are a debugging view of the viewer (off by default) and pathfinding has nothing behind it here
                    return w.VisibleAtLoad && !w.Name.StartsWith("pathfinding", StringComparison.Ordinal) && !(w.Name.Length == 2 && w.Name[1] == ':');
                case "Objeto": return !(Selected == Subject.Ring ? RingHidden : BoxHidden).Contains(w.Name);
                case "Recursos": return w.Name != "select_single" && (w.VisibleAtLoad || w.Name == "edit_object") && w.Name != "Probe Update Label" && w.Name != "Probe Update Type";
                case "Textura": return TextureAlways.Contains(w.Name) || (Surface == "pbr" ? Pbr : Surface == "media" ? Media : Classic).Contains(w.Name);
                default: return w.VisibleAtLoad;
            }
        }

        public bool Disabled(Widget w)
        {
            if (Off.Contains(w.Name) || Unavailable.ContainsKey(w.Name)) return true;
            // as in the viewer: linking needs several objects (and not the parts mode), unlinking a linked set
            if (w.Name == "link_btn") return Selected != Subject.Several;
            if (w.Name == "unlink_btn") return Selected != Subject.Table;
            if (w.Window != "tools" || w.Tab == null) return false;
            if (!HasSelection) return true;                                            // as in the viewer: the tabs stay, greyed out
            if (w.Tab == "Recursos") return w.Name.StartsWith("Probe", StringComparison.Ordinal) && !Checked.Contains("Reflection Probe") || w.Name.StartsWith("Flex", StringComparison.Ordinal) && w.Type == "spinner" && !Checked.Contains("Flexible1D Checkbox Ctrl") || w.Name == "Animated Mesh Checkbox Ctrl";
            return false;
        }

        public string Value(Widget w) => Values.TryGetValue(w.Name, out var v) ? v : Defaults.TryGetValue(w.Name, out var d) ? d : "";

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            ["Object Name"] = "Caixote do deck", ["Object Description"] = "Madeira clara, dá para sentar", ["Creator Name"] = "Ana Ribeiro", ["Owner Name"] = "Você", ["Group Name Proxy"] = "Clube do Deck",
            ["clickaction"] = "Tocar (padrão)", ["Edit Cost"] = "120", ["sale type"] = "Cópia",
            ["combobox grid mode"] = "Mundo", ["selection_count"] = "1 objeto · custo 3 (ilha: 214 de 5.000)",
            ["Pos X"] = "12,500", ["Pos Y"] = "38,000", ["Pos Z"] = "1,250", ["Scale X"] = "1,500", ["Scale Y"] = "1,500", ["Scale Z"] = "1,500", ["Rot X"] = "0,00", ["Rot Y"] = "0,00", ["Rot Z"] = "28,00",
            ["comboBaseType"] = "Caixa", ["cut begin"] = "0,000", ["cut end"] = "1,000", ["Scale 1"] = "0,0", ["Skew"] = "0,00", ["hole"] = "Padrão", ["Twist Begin"] = "0", ["Twist End"] = "0",
            ["Taper Scale X"] = "0,00", ["Taper Scale Y"] = "0,00", ["Shear X"] = "0,00", ["Shear Y"] = "0,00", ["Path Limit Begin"] = "0,000", ["Path Limit End"] = "1,000", ["Taper X"] = "0,00", ["Taper Y"] = "0,00", ["Radius Offset"] = "0,00", ["Revolutions"] = "1,00",
            ["FlexNumSections"] = "2", ["FlexGravity"] = "0,3", ["FlexFriction"] = "2,0", ["FlexWind"] = "0,0", ["FlexTension"] = "1,0", ["FlexForceX"] = "0,00", ["FlexForceY"] = "0,00", ["FlexForceZ"] = "0,00",
            ["Light Intensity"] = "1,500", ["Light FOV"] = "0,500", ["Light Radius"] = "8,000", ["Light Focus"] = "0,000", ["Light Falloff"] = "1,000", ["Light Ambiance"] = "0,100",
            ["Probe Volume Type"] = "Esfera", ["Probe Ambiance"] = "0,000", ["Probe Near Clip"] = "0,000",
            ["Physics Shape Type Combo Ctrl"] = "Primitiva", ["material"] = "Madeira", ["Physics Gravity"] = "1,0", ["Physics Friction"] = "0,60", ["Physics Density"] = "1000", ["Physics Restitution"] = "0,40",
            ["ColorTrans"] = "0", ["glow"] = "0,00", ["combobox matmedia"] = "Clássica", ["combobox alphamode"] = "Mistura", ["combobox texgen"] = "Padrão",
            ["TexScaleU"] = "1,00000", ["TexScaleV"] = "1,00000", ["rptctrl"] = "0,7", ["TexRot"] = "0,00", ["TexOffsetU"] = "0,00000", ["TexOffsetV"] = "0,00000",
            ["gltfTextureScaleU"] = "1,00000", ["gltfTextureScaleV"] = "1,00000", ["gltfRptctrl"] = "0,7", ["gltfTextureRotation"] = "0,00", ["gltfTextureOffsetU"] = "0,00000", ["gltfTextureOffsetV"] = "0,00000",
            ["media_info"] = "https://exemplo.org/video.mp4",
            ["GridResolution"] = "0,50", ["GridDrawSize"] = "12,0",
            ["rspin"] = "180", ["gspin"] = "71", ["bspin"] = "47", ["hspin"] = "11", ["sspin"] = "59", ["lspin"] = "45", ["hex_color"] = "B4472F", ["copy_color_combobox"] = "Copiar como…",
            ["mode_selection"] = "Inventário",
            ["home_url"] = "https://exemplo.org/video.mp4", ["current_url"] = "https://exemplo.org/video.mp4", ["width_pixels"] = "1024", ["height_pixels"] = "576",
        };

        private static Scenario New(int number, string id, string title, Action<Scenario> set)
        {
            var s = new Scenario { Number = number, Id = id, Title = title };
            foreach (var name in new[] { "checkbox snap to grid", "checkbox stretch textures", "checkbox next owner can modify", "checkbox next owner can copy", "checkbox next owner can transfer", "Texture (diffuse)", "Complete material", "apply_immediate_check", "apply_immediate", "auto_scale", "auto_loop" }) s.Checked.Add(name);
            set(s);
            s.Checked.Add(s.Mode == "Foco" ? "radio zoom" : s.Mode == "Mover" ? "radio move" : s.Mode == "Terreno" ? "radio raise" : s.Tool == "rotate" ? "radio rotate" : s.Tool == "stretch" ? "radio stretch" : s.Tool == "face" ? "radio select face" : "radio position");
            if (!s.Values.ContainsKey("status")) s.Values["status"] = s.Mode == "Foco" ? "Arraste para mover a câmera" : s.Mode == "Mover" ? "Arraste; Ctrl ergue; Ctrl+Shift gira" : s.Mode == "Criar" ? "Clique no mundo para construir" : s.Mode == "Terreno" ? "Segure o botão para mudar o terreno" : s.Tool == "rotate" ? "Arraste um anel para girar" : s.Tool == "stretch" ? "Arraste uma alça para esticar" : s.Tool == "face" ? "Clique numa face; Shift soma" : "Arraste para mover; Shift copia";
            return s;
        }

        private static void Key(Scenario s, string name, string note = "") => s.Compare.Add((name, note));

        public static readonly Scenario[] All =
        {
            New(1, "foco", "Ferramenta Foco", s => { s.Mode = "Foco"; s.Selected = Subject.Nothing; s.Marks = Marks.Focus; Key(s, "button focus", "ícone do jogo no lugar do original"); Key(s, "radio zoom"); Key(s, "slider zoom"); Key(s, "selection_empty"); Key(s, "Object Info Tabs", "abas apagadas sem seleção, como no original"); }),
            New(2, "mover", "Ferramenta Mover", s => { s.Mode = "Mover"; s.Marks = Marks.Selection; Key(s, "button move"); Key(s, "radio move"); Key(s, "radio lift"); Key(s, "radio spin"); Key(s, "selection_count", "custo do LifeVerse no lugar de land impact"); }),
            New(3, "editar-posicao", "Editar: posição", s => { s.Tab = "Objeto"; Key(s, "button edit"); Key(s, "radio position"); Key(s, "checkbox snap to grid", "rótulo curto: \"Grade\""); Key(s, "combobox grid mode"); Key(s, "link_btn"); Key(s, "Pos X", "Z é a altura, como no original (proposta 2)"); }),
            New(4, "editar-rotacao", "Editar: rotação", s => { s.Tab = "Objeto"; s.Tool = "rotate"; s.Marks = Marks.Rotate; Key(s, "radio rotate"); Key(s, "Rot X"); Key(s, "Rot Z"); Key(s, "checkbox uniform"); }),
            New(5, "editar-escala", "Editar: escala", s => { s.Tab = "Objeto"; s.Tool = "stretch"; s.Marks = Marks.Scale; Key(s, "radio stretch"); Key(s, "checkbox uniform", "\"Dos dois lados\""); Key(s, "checkbox stretch textures"); Key(s, "Scale X"); }),
            New(6, "editar-face", "Editar: selecionar face", s => { s.Tab = "Textura"; s.Tool = "face"; s.Marks = Marks.Face; s.Values["selection_faces"] = "Faces selecionadas: 0, 3"; Key(s, "radio select face"); Key(s, "selection_faces"); Key(s, "texture control", "miniatura de textura do jogo"); Key(s, "TexScaleU"); }),
            New(7, "criar", "Ferramenta Criar", s => { s.Mode = "Criar"; s.Selected = Subject.Nothing; s.Marks = Marks.Ghost; s.Checked.Add("ToolCube"); s.Checked.Add("checkbox sticky"); s.Off.Add("checkbox copy centers"); s.Off.Add("checkbox copy rotates"); Key(s, "button create"); Key(s, "ToolCube", "ícones desenhados para o LifeVerse"); Key(s, "ToolTetrahedron", "indisponível: forma nova no servidor"); Key(s, "ToolTree"); Key(s, "checkbox sticky"); Key(s, "checkbox copy centers", "só habilita com \"Copiar seleção\""); }),
            New(8, "terreno", "Ferramenta Terreno", s => { s.Mode = "Terreno"; s.Selected = Subject.Nothing; s.Marks = Marks.Terrain; s.Gone.Add("label_area"); Key(s, "button land"); Key(s, "radio raise"); Key(s, "slider brush size"); Key(s, "button apply to selection", "indisponível: protocolo"); Key(s, "button about land"); Key(s, "button buy land"); }),
            New(9, "aba-geral", "Aba Geral", s => { s.Checked.Add("checkbox for sale"); Key(s, "Object Name"); Key(s, "Creator Name", "foto da pessoa no lugar do ícone"); Key(s, "button set group"); Key(s, "clickaction"); Key(s, "Edit Cost", "VC$ no lugar de L$"); Key(s, "search_check", "indisponível: banco"); Key(s, "checkbox next owner can modify"); }),
            New(10, "aba-objeto", "Aba Objeto (toro: campos que dependem da forma)", s => { s.Tab = "Objeto"; s.Selected = Subject.Ring; s.Values["comboBaseType"] = "Toro"; s.Values["Object Name"] = "Boia"; s.Values["Scale X"] = "1,800"; s.Values["Scale Y"] = "1,800"; s.Values["Scale Z"] = "0,500"; s.Values["Rot Z"] = "0,00"; s.Values["Pos X"] = "15,200"; s.Values["Pos Z"] = "0,750"; s.Values["Taper Scale X"] = "1,00"; s.Values["Taper Scale Y"] = "0,25"; Key(s, "checkbox locked"); Key(s, "comboBaseType"); Key(s, "scale_hole", "troca \"Afunilar\" no toro"); Key(s, "Skew"); Key(s, "Revolutions"); Key(s, "hole", "indisponível: gerador e API"); }),
            New(11, "aba-recursos", "Aba Recursos", s => { s.Tab = "Recursos"; s.Checked.Add("Light Checkbox Ctrl"); Key(s, "Flexible1D Checkbox Ctrl"); Key(s, "Light Checkbox Ctrl"); Key(s, "colorswatch"); Key(s, "Physics Shape Type Combo Ctrl"); Key(s, "material", "fica em Recursos, como no visualizador atual"); Key(s, "Physics Gravity", "indisponível: física no servidor"); Key(s, "Reflection Probe"); }),
            New(12, "textura-classica", "Aba Textura: material clássico", s => { s.Tab = "Textura"; Key(s, "colorswatch"); Key(s, "combobox matmedia"); Key(s, "Shininess (specular)", "indisponível: o jogo desenha só PBR"); Key(s, "texture control"); Key(s, "combobox alphamode"); Key(s, "rptctrl"); Key(s, "checkbox planar align"); }),
            New(13, "textura-pbr", "Aba Textura: material PBR", s => { s.Tab = "Textura"; s.Surface = "pbr"; s.Values["combobox matmedia"] = "PBR (metal)"; Key(s, "radio_pbr_type"); Key(s, "pbr_control", "indisponível: material como item"); Key(s, "pbr_from_inventory"); Key(s, "gltfTextureScaleU"); Key(s, "gltfTextureRotation"); }),
            New(14, "conteudo", "Aba Conteúdo", s => { s.Tab = "Conteúdo"; Key(s, "button new script"); Key(s, "button new notecard", "indisponível: tipo novo"); Key(s, "button permissions"); Key(s, "contents_filter"); Key(s, "contents_inventory"); }),
            New(15, "grade", "Opções da grade", s => { s.Tab = "Objeto"; s.Extra = "grid"; s.Marks = Marks.Grid; s.Checked.Add("GridSubUnit"); Key(s, "Options..."); Key(s, "GridResolution"); Key(s, "GridDrawSize"); Key(s, "GridSubUnit"); Key(s, "GridOpacity"); }),
            New(16, "midia", "Configurações de mídia", s => { s.Tab = "Textura"; s.Surface = "media"; s.Extra = "media"; s.Tool = "face"; s.Marks = Marks.Face; s.Values["combobox matmedia"] = "Mídia"; s.Checked.Add("auto_play"); s.Values["selection_faces"] = "Faces selecionadas: 0"; Key(s, "add_media"); Key(s, "home_url"); Key(s, "auto_loop"); Key(s, "auto_play"); Key(s, "auto_scale"); Key(s, "preview_media", "só imagem e vídeo: não há navegador de páginas"); }),
            New(17, "seletor-textura", "Seletor de textura", s => { s.Tab = "Textura"; s.Extra = "texture-picker"; Key(s, "texture control"); Key(s, "Default"); Key(s, "inventory search editor"); Key(s, "inventory panel", "inclui as texturas do sistema"); Key(s, "Select"); }),
            New(18, "seletor-cor", "Seletor de cor", s => { s.Tab = "Textura"; s.Extra = "color-picker"; Key(s, "colorswatch"); Key(s, "rspin"); Key(s, "hspin"); Key(s, "hex_color"); Key(s, "select_btn"); }),
            New(19, "selecao-multipla", "Seleção de vários objetos", s => { s.Selected = Subject.Several; s.Marks = Marks.Multi; s.Values["selection_count"] = "3 objetos · custo 7 (ilha: 214 de 5.000)"; foreach (var n in new[] { "Object Name", "Object Description" }) { s.Values[n] = ""; s.Off.Add(n); } s.Values["Creator Name"] = "(várias pessoas)"; s.Values["Edit Cost"] = "—"; s.Values["clickaction"] = "(diferentes)"; s.Mixed.Add("checkbox for sale"); s.Mixed.Add("checkbox next owner can copy"); s.Mixed.Add("checkbox share with group"); Key(s, "selection_count"); Key(s, "Object Name", "desligado com vários, como no original"); Key(s, "checkbox for sale", "valor misto: traço"); Key(s, "link_btn"); }),
            New(20, "vinculadas", "Editar partes de um conjunto vinculado", s => { s.Tab = "Objeto"; s.Selected = Subject.Table; s.Marks = Marks.Linked; s.Checked.Add("checkbox edit linked parts"); s.Values["selection_count"] = "1 parte de um conjunto de 5 · custo 9"; s.Values["Pos X"] = "17,150"; s.Values["Pos Y"] = "37,250"; s.Values["Pos Z"] = "0,500"; s.Values["Scale X"] = "0,180"; s.Values["Scale Y"] = "0,180"; s.Values["Scale Z"] = "1,000"; s.Values["Rot Z"] = "0,00"; Key(s, "checkbox edit linked parts"); Key(s, "unlink_btn"); Key(s, "link_btn"); Key(s, "Pos X", "posição da parte no mundo"); }),
        };
    }
}
