using System.Collections.Generic;

namespace LifeVerse.Unity.UI
{
    /// <summary>The words of the build tool, by the original name of each component ("sheet:name" wins over "name"). Short on purpose: the rows are narrow.</summary>
    public static class BuildTexts
    {
        public static string Of(string sheet, string name) => Words.TryGetValue(sheet + ":" + name, out var own) ? own : Words.TryGetValue(name, out var text) ? text : "";

        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>
        {
            ["radio zoom"] = "Zoom", ["radio orbit"] = "Orbitar (Ctrl)", ["radio pan"] = "Panorâmica (Ctrl+Shift)",
            ["radio move"] = "Mover", ["radio lift"] = "Erguer (Ctrl)", ["radio spin"] = "Girar (Ctrl+Shift)",
            ["radio position"] = "Posição", ["radio rotate"] = "Rotação", ["radio stretch"] = "Escala", ["radio select face"] = "Selecionar face",
            ["checkbox edit linked parts"] = "Editar partes", ["link_btn"] = "Vincular", ["unlink_btn"] = "Desvincular", ["checkbox uniform"] = "Dos dois lados", ["checkbox stretch textures"] = "Esticar textura", ["checkbox snap to grid"] = "Grade",
            ["checkbox sticky"] = "Manter ferramenta", ["checkbox copy selection"] = "Copiar seleção", ["checkbox copy centers"] = "Centralizar", ["checkbox copy rotates"] = "Girar cópia",
            ["radio select land"] = "Selecionar", ["radio flatten"] = "Achatar", ["radio raise"] = "Elevar", ["radio lower"] = "Rebaixar", ["radio smooth"] = "Suavizar", ["radio noise"] = "Rugosidade", ["radio revert"] = "Restaurar",
            ["Bulldozer:"] = "Pincel:", ["Dozer Size:"] = "Tamanho", ["Strength:"] = "Força", ["button apply to selection"] = "Aplicar",
            ["selection_empty"] = "Nada selecionado.",
            ["Name:"] = "Nome:", ["Description:"] = "Descrição:", ["Creator:"] = "Criador:", ["Owner:"] = "Dono:", ["Group:"] = "Grupo:", ["checkbox share with group"] = "Compartilhar", ["button deed"] = "Dar ao grupo",
            ["label click action"] = "Ao clicar:", ["checkbox for sale"] = "À venda:", ["Edit Cost"] = "VC$", ["search_check"] = "Exibir na busca",
            ["Anyone can:"] = "Qualquer pessoa pode:", ["checkbox allow everyone move"] = "Mover", ["checkbox allow everyone copy"] = "Copiar", ["Next owner can:"] = "O próximo dono pode:",
            ["checkbox next owner can modify"] = "Modificar", ["checkbox next owner can copy"] = "Copiar", ["checkbox next owner can transfer"] = "Transferir",
            ["checkbox locked"] = "Bloqueado", ["Physical Checkbox Ctrl"] = "Físico", ["Temporary Checkbox Ctrl"] = "Temporário", ["Phantom Checkbox Ctrl"] = "Fantasma",
            ["label position"] = "Posição (m)", ["label size"] = "Tamanho (m)", ["label rotation"] = "Rotação (°)",
            ["Pos X"] = "X", ["Pos Y"] = "Y", ["Pos Z"] = "Z", ["Scale X"] = "X", ["Scale Y"] = "Y", ["Scale Z"] = "Z", ["Rot X"] = "X", ["Rot Y"] = "Y", ["Rot Z"] = "Z",
            ["text cut"] = "Corte da trajetória", ["cut begin"] = "I", ["cut end"] = "F", ["text hollow"] = "Oco (%)", ["text skew"] = "Inclinar", ["Hollow Shape"] = "Formato do oco", ["text twist"] = "Torção",
            ["Twist Begin"] = "I", ["Twist End"] = "F", ["scale_taper"] = "Afunilar", ["scale_hole"] = "Tamanho do buraco", ["Taper Scale X"] = "X", ["Taper Scale Y"] = "Y", ["text topshear"] = "Cisalhar o topo", ["Shear X"] = "X", ["Shear Y"] = "Y",
            ["advanced_cut"] = "Corte do perfil", ["advanced_dimple"] = "Côncavo", ["advanced_slice"] = "Fatia", ["Path Limit Begin"] = "I", ["Path Limit End"] = "F",
            ["text taper2"] = "Afunilar", ["Taper X"] = "X", ["Taper Y"] = "Y", ["text radius delta"] = "Raio", ["text revolutions"] = "Voltas",
            ["edit_object"] = "Recursos do objeto:", ["select_single"] = "Selecione um só objeto.", ["Animated Mesh Checkbox Ctrl"] = "Animar o modelo", ["Flexible1D Checkbox Ctrl"] = "Flexível",
            ["FlexNumSections"] = "Suavidade", ["FlexGravity"] = "Gravidade", ["FlexFriction"] = "Arrasto", ["FlexWind"] = "Vento", ["FlexTension"] = "Tensão", ["FlexForceX"] = "Força X", ["FlexForceY"] = "Força Y", ["FlexForceZ"] = "Força Z",
            ["Light Checkbox Ctrl"] = "Luz", ["Light Intensity"] = "Força", ["Light FOV"] = "Abertura", ["Light Radius"] = "Alcance", ["Light Focus"] = "Foco", ["Light Falloff"] = "Queda", ["Light Ambiance"] = "Ambiente",
            ["Reflection Probe"] = "Reflexo", ["Probe Dynamic"] = "Dinâmico", ["Probe Update Label"] = "Atualização", ["Probe Ambiance"] = "Força", ["Probe Near Clip"] = "Alcance",
            ["label physicsshapetype"] = "Forma de colisão:", ["Physics Gravity"] = "Gravidade", ["Physics Friction"] = "Atrito", ["Physics Density"] = "Densidade", ["Physics Restitution"] = "Quique",
            ["color label"] = "Cor", ["color trans"] = "Transparência %", ["glow label"] = "Brilho", ["checkbox fullbright"] = "Luz total", ["checkbox_hide_water"] = "Esconder água", ["label_matmedia"] = "Superfície",
            ["Texture (diffuse)"] = "Textura (difusa)", ["Bumpiness (normal)"] = "Relevo (normal)", ["Shininess (specular)"] = "Brilho (especular)",
            ["Complete material"] = "Material completo", ["Base color"] = "Cor base", ["Metallic/roughness"] = "Metal e aspereza", ["Emissive"] = "Emissivo", ["Normal"] = "Normal",
            ["checkbox_sync_settings"] = "Travar repetições", ["pbr_control"] = "Material", ["pbr_from_inventory"] = "Do inventário…", ["edit_selected_pbr"] = "Editar", ["save_selected_pbr"] = "Salvar no inventário",
            ["label alphamode"] = "Transparência", ["label maskcutoff"] = "Corte da máscara", ["label bumpiness"] = "Força do relevo",
            ["tex gen"] = "Mapeamento", ["TexScaleU"] = "Repetir na horizontal", ["TexScaleV"] = "Repetir na vertical", ["rptctrl"] = "Repetições por metro", ["TexRot"] = "Rotação (graus)", ["TexOffsetU"] = "Deslocar na horizontal", ["TexOffsetV"] = "Deslocar na vertical",
            ["bumpyScaleU"] = "Repetir na horizontal", ["bumpyScaleV"] = "Repetir na vertical", ["bumpyRot"] = "Rotação (graus)", ["bumpyOffsetU"] = "Deslocar na horizontal", ["bumpyOffsetV"] = "Deslocar na vertical",
            ["gltfTextureScaleU"] = "Repetir na horizontal", ["gltfTextureScaleV"] = "Repetir na vertical", ["gltfRptctrl"] = "Repetições por metro", ["gltfTextureRotation"] = "Rotação (graus)", ["gltfTextureOffsetU"] = "Deslocar na horizontal", ["gltfTextureOffsetV"] = "Deslocar na vertical",
            ["checkbox planar align"] = "Alinhar faces planas", ["button align textures"] = "Alinhar", ["add_media"] = "Escolher…", ["delete_media"] = "Remover", ["button align"] = "Alinhar",
            ["button new script"] = "Scripts…", ["button new notecard"] = "Nova nota", ["button permissions"] = "Permissões", ["button publish"] = "Editor externo",
            ["label_parcel_info"] = "Sobre o terreno", ["button about land"] = "Sobre o terreno", ["checkbox show owners"] = "Mostrar donos",
            ["label_parcel_modify"] = "Modificar o terreno", ["button subdivide land"] = "Dividir", ["button join land"] = "Unir", ["label_parcel_trans"] = "Transações", ["button buy land"] = "Comprar", ["button abandon land"] = "Abandonar",
            ["grid:GridResolution"] = "Passo (metros)", ["grid:GridDrawSize"] = "Extensão (metros)", ["grid:GridSubUnit"] = "Encaixar em subdivisões", ["grid:grid_opacity_label"] = "Opacidade:",
            ["texture-picker:Default"] = "Padrão", ["texture-picker:Blank"] = "Branca", ["texture-picker:None"] = "Nenhuma", ["texture-picker:inventory search editor"] = "Buscar nas texturas",
            ["texture-picker:Select"] = "Aplicar", ["texture-picker:Cancel"] = "Cancelar", ["texture-picker:apply_immediate_check"] = "Aplicar na hora",
            ["color-picker:r_val_text"] = "Vermelho:", ["color-picker:g_val_text"] = "Verde:", ["color-picker:b_val_text"] = "Azul:", ["color-picker:h_val_text"] = "Matiz:", ["color-picker:s_val_text"] = "Saturação:", ["color-picker:l_val_text"] = "Luz:",
            ["color-picker:hex_text"] = "Hex:", ["color-picker:hash_text"] = "#", ["color-picker:apply_immediate"] = "Aplicar na hora", ["color-picker:select_btn"] = "Aplicar", ["color-picker:cancel_btn"] = "Cancelar",
            ["color-picker:Current color:"] = "Cor atual:",
            ["media:OK"] = "OK", ["media:Cancel"] = "Cancelar", ["media:Apply"] = "Aplicar", ["home_label"] = "Endereço (https://…):", ["preview_label"] = "Prévia", ["current_url_label"] = "Endereço atual:", ["current_url_reset_btn"] = "Voltar",
            ["auto_loop"] = "Repetir", ["auto_zoom"] = "Aproximar ao clicar", ["auto_play"] = "Tocar sozinho", ["media_setting_note"] = "Cada pessoa pode desligar a mídia nas configurações.", ["auto_scale"] = "Ajustar à face do objeto",
            ["size_label"] = "Tamanho:", ["X_label"] = "×",
        };
    }
}
