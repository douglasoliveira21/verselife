"""Writes docs/design/build-editor-coordinate-map.{md,json} from the positions computed by slmap.py.
Run: python -I build_map.py <map.json> <viewer-files.txt> <repo-root>"""
import io, json, os, sys

SCALE = 1.45                 # proposed: 295x658 (original window with its 25 px header) -> 428x954 at the 1920x1080 reference
ORIGIN = (50, 70)            # where the window's top-left corner goes on the 1920x1080 reference
COMMIT = 'e960123230d521e8557a9582100bebcac36a7ca2'
TAG = 'Second_Life_Release#e960123-26.4'

KIND = {'button': 'Botão', 'text': 'Rótulo', 'check_box': 'Caixa de marcar', 'spinner': 'Campo numérico', 'combo_box': 'Lista suspensa', 'radio_group': 'Grupo de opções', 'radio_item': 'Opção',
        'panel': 'Painel', 'tab_container': 'Abas', 'line_editor': 'Campo de texto', 'texture_picker': 'Seletor de textura', 'color_swatch': 'Amostra de cor', 'slider_bar': 'Controle deslizante',
        'slider': 'Controle deslizante', 'menu_button': 'Botão de menu', 'view_border': 'Divisor', 'avatar_icon': 'Ícone de pessoa', 'group_icon': 'Ícone de grupo', 'name_box': 'Nome clicável',
        'flyout_button': 'Botão com menu', 'filter_editor': 'Campo de filtro', 'panel_inventory_object': 'Árvore de conteúdo', 'web_browser': 'Visor de mídia', 'scroll_list': 'Lista',
        'icon': 'Ícone', 'loading_indicator': 'Indicador de carregamento', 'view': 'Área', 'asset_filtered_inv_panel': 'Árvore do inventário'}

TAB = {'General': 'Geral', 'Object': 'Objeto', 'Features': 'Recursos', 'Texture': 'Textura', 'Content': 'Conteúdo'}

D = {  # what each control does; names are the original ones
    # ferramentas e modos
    'button focus': 'Modo Foco (câmera)', 'button move': 'Modo Mover (arrastar objetos)', 'button edit': 'Modo Editar', 'button create': 'Modo Criar', 'button land': 'Modo Terreno',
    'status': 'Instrução da ferramenta ativa',
    'focus_radio_group': 'Opções do modo Foco', 'radio zoom': 'Câmera: aproximar/afastar', 'radio orbit': 'Câmera: orbitar (Ctrl)', 'radio pan': 'Câmera: panorâmica (Ctrl+Shift)', 'slider zoom': 'Nível de aproximação',
    'move_radio_group': 'Opções do modo Mover', 'radio move': 'Arrastar no plano', 'radio lift': 'Arrastar na vertical (Ctrl)', 'radio spin': 'Girar arrastando (Ctrl+Shift)',
    'edit_radio_group': 'Opções do modo Editar', 'radio position': 'Manipulador de posição', 'radio rotate': 'Manipulador de rotação (Ctrl)', 'radio stretch': 'Manipulador de escala (Ctrl+Shift)', 'radio select face': 'Selecionar faces',
    'checkbox edit linked parts': 'Selecionar partes de um conjunto vinculado', 'link_btn': 'Vincular a seleção', 'unlink_btn': 'Desvincular', 'checkbox uniform': 'Esticar dos dois lados',
    'checkbox stretch textures': 'Esticar a textura junto', 'checkbox snap to grid': 'Encaixar na grade', 'combobox grid mode': 'Referencial da grade: mundo, local ou outro objeto', 'Options...': 'Abre as opções da grade',
    'ToolCube': 'Cubo', 'ToolPrism': 'Prisma', 'ToolPyramid': 'Pirâmide', 'ToolTetrahedron': 'Tetraedro', 'ToolCylinder': 'Cilindro', 'ToolHemiCylinder': 'Semicilindro', 'ToolCone': 'Cone', 'ToolHemiCone': 'Semicone', 'ToolSphere': 'Esfera', 'ToolHemiSphere': 'Hemisfério', 'ToolTorus': 'Toro', 'ToolTube': 'Tubo', 'ToolRing': 'Anel', 'ToolTree': 'Árvore', 'ToolGrass': 'Grama',
    'checkbox sticky': 'Manter a ferramenta de criar selecionada', 'checkbox copy selection': 'Criar cópias do objeto selecionado', 'checkbox copy centers': 'Centralizar a cópia', 'checkbox copy rotates': 'Girar a cópia conforme a face',
    'land_radio_group': 'Ação do terreno', 'radio select land': 'Selecionar uma área', 'radio flatten': 'Nivelar', 'radio raise': 'Elevar', 'radio lower': 'Rebaixar', 'radio smooth': 'Suavizar', 'radio noise': 'Rugosidade', 'radio revert': 'Restaurar',
    'slider brush size': 'Tamanho do pincel', 'slider force': 'Força do pincel', 'button apply to selection': 'Aplicar à área selecionada',
    'selection_empty': 'Aviso: nada selecionado', 'selection_faces': 'Faces selecionadas', 'selection_count': 'Objetos selecionados e impacto no terreno', 'cost_text_border': 'Separador acima das abas',
    'Object Info Tabs': 'As cinco abas de propriedades', 'General': 'Aba Geral', 'Object': 'Aba Objeto', 'Features': 'Aba Recursos', 'Texture': 'Aba Textura', 'Contents': 'Aba Conteúdo',
    'land info panel': 'Painel do terreno (substitui as abas no modo Terreno)', 'button about land': 'Sobre o terreno', 'checkbox show owners': 'Mostrar donos no chão', 'button subdivide land': 'Dividir terreno', 'button join land': 'Unir terrenos', 'button buy land': 'Comprar terreno', 'button abandon land': 'Abandonar terreno',
    # geral
    'Object Name': 'Nome do objeto', 'Object Description': 'Descrição do objeto', 'Creator Icon': 'Foto do criador', 'Creator Name': 'Criador (abre o perfil)', 'Owner Icon': 'Foto do dono', 'Owner Group Icon': 'Insígnia do grupo dono', 'Owner Name': 'Dono (abre o perfil)',
    'Group Name Proxy': 'Grupo do objeto', 'button set group': 'Escolher o grupo', 'checkbox share with group': 'Compartilhar com o grupo', 'button deed': 'Transferir a propriedade ao grupo', 'clickaction': 'Ação ao clicar', 'checkbox for sale': 'À venda', 'Edit Cost': 'Preço', 'sale type': 'O que se vende: cópia, conteúdo ou original', 'search_check': 'Exibir na busca',
    'perms_build': 'Quadro de permissões', 'perm_modify': 'O que você pode fazer com o objeto', 'checkbox allow everyone move': 'Qualquer pessoa pode mover', 'checkbox allow everyone copy': 'Qualquer pessoa pode copiar', 'checkbox next owner can modify': 'Próximo dono pode modificar', 'checkbox next owner can copy': 'Próximo dono pode copiar', 'checkbox next owner can transfer': 'Próximo dono pode transferir',
    'B:': 'Máscara base (permissões avançadas)', 'O:': 'Máscara do dono (avançadas)', 'G:': 'Máscara do grupo (avançadas)', 'E:': 'Máscara de todos (avançadas)', 'N:': 'Máscara do próximo dono (avançadas)', 'F:': 'Máscara efetiva (avançadas)',
    'pathfinding_attrs_panel': 'Atributos de navegação (pathfinding)', 'pathfinding_attributes_value': 'Valor do atributo de navegação',
    # objeto
    'checkbox locked': 'Bloqueado', 'Physical Checkbox Ctrl': 'Físico', 'Temporary Checkbox Ctrl': 'Temporário', 'Phantom Checkbox Ctrl': 'Fantasma (sem colisão)', 'object_horizontal': 'Separador horizontal', 'object_vertical': 'Separador entre as colunas',
    'clipboard_pos_btn': 'Copiar/colar posição', 'Pos X': 'Posição X', 'Pos Y': 'Posição Y', 'Pos Z': 'Posição Z (altura no Second Life)', 'clipboard_size_btn': 'Copiar/colar tamanho', 'Scale X': 'Tamanho X', 'Scale Y': 'Tamanho Y', 'Scale Z': 'Tamanho Z', 'clipboard_rot_btn': 'Copiar/colar rotação', 'Rot X': 'Rotação X', 'Rot Y': 'Rotação Y', 'Rot Z': 'Rotação Z',
    'comboBaseType': 'Tipo de forma', 'clipboard_obj_params_btn': 'Copiar/colar parâmetros da forma', 'cut begin': 'Corte de trajetória: início', 'cut end': 'Corte de trajetória: fim', 'Scale 1': 'Oco (%)', 'Skew': 'Inclinação (skew)', 'hole': 'Formato do oco', 'Twist Begin': 'Torção: início', 'Twist End': 'Torção: fim',
    'Taper Scale X': 'Afunilamento X ou tamanho do buraco X', 'Taper Scale Y': 'Afunilamento Y ou tamanho do buraco Y', 'Shear X': 'Cisalhamento do topo X', 'Shear Y': 'Cisalhamento do topo Y', 'Path Limit Begin': 'Corte de perfil / côncavo / fatia: início', 'Path Limit End': 'Corte de perfil / côncavo / fatia: fim', 'Taper X': 'Afunilamento X (toro/tubo/anel)', 'Taper Y': 'Afunilamento Y (toro/tubo/anel)', 'Radius Offset': 'Deslocamento do raio', 'Revolutions': 'Revoluções',
    'sculpt texture control': 'Textura de escultura', 'sculpt mirror control': 'Espelhar a escultura', 'sculpt invert control': 'Virar a escultura do avesso', 'sculpt type control': 'Tipo de costura da escultura',
    # recursos
    'clipboard_features_params_btn': 'Copiar/colar recursos', 'select_single': 'Aviso: selecione uma só primitiva', 'edit_object': 'Título: recursos do objeto', 'Animated Mesh Checkbox Ctrl': 'Malha animada', 'Flexible1D Checkbox Ctrl': 'Objeto flexível', 'FlexNumSections': 'Flexível: suavidade', 'FlexGravity': 'Flexível: gravidade', 'FlexFriction': 'Flexível: resistência ao ar', 'FlexWind': 'Flexível: vento', 'FlexTension': 'Flexível: tensão', 'FlexForceX': 'Flexível: força X', 'FlexForceY': 'Flexível: força Y', 'FlexForceZ': 'Flexível: força Z',
    'Light Checkbox Ctrl': 'O objeto emite luz', 'colorswatch': 'Cor', 'light texture control': 'Imagem projetada pela luz', 'clipboard_light_params_btn': 'Copiar/colar luz', 'Light Intensity': 'Luz: intensidade', 'Light FOV': 'Projetor: campo de visão', 'Light Radius': 'Luz: alcance', 'Light Focus': 'Projetor: foco', 'Light Falloff': 'Luz: atenuação', 'Light Ambiance': 'Projetor: ambiência',
    'Reflection Probe': 'Sonda de reflexo', 'Probe Volume Type': 'Sonda: formato (esfera/caixa)', 'Probe Dynamic': 'Sonda dinâmica', 'Probe Update Type': 'Sonda: atualização (estática, dinâmica, espelho)', 'Probe Ambiance': 'Sonda: ambiência', 'Probe Near Clip': 'Sonda: corte próximo',
    'Physics Shape Type Combo Ctrl': 'Forma física (preenchida pelo código: nenhuma, primitiva, convexa)', 'material': 'Material físico', 'Physics Gravity': 'Multiplicador de gravidade', 'Physics Friction': 'Atrito', 'Physics Density': 'Densidade', 'Physics Restitution': 'Restituição (quique)',
    # textura
    'clipboard_color_params_btn': 'Copiar/colar cor', 'ColorTrans': 'Transparência (%)', 'glow': 'Brilho (glow)', 'checkbox fullbright': 'Luz total', 'checkbox_hide_water': 'Esconder a água sobre a face',
    'combobox matmedia': 'Modo: material clássico, PBR ou mídia', 'radio_material_type': 'Mapa em edição (clássico)', 'Texture (diffuse)': 'Mapa difuso', 'Bumpiness (normal)': 'Mapa de relevo', 'Shininess (specular)': 'Mapa especular', 'radio_pbr_type': 'Canal em edição (PBR)', 'Complete material': 'PBR: material completo', 'Base color': 'PBR: cor base', 'Metallic/roughness': 'PBR: metal e aspereza', 'Emissive': 'PBR: emissivo', 'Normal': 'PBR: normal',
    'clipboard_texture_params_btn': 'Copiar/colar textura', 'checkbox_sync_settings': 'Travar repetições entre os mapas', 'pbr_control': 'Material PBR', 'pbr_from_inventory': 'Escolher material no inventário', 'material_permissions_loading_label': 'Aviso: carregando conteúdo', 'edit_selected_pbr': 'Editar o material selecionado', 'save_selected_pbr': 'Salvar o material no inventário',
    'texture control': 'Textura difusa', 'combobox alphamode': 'Modo de transparência', 'maskcutoff': 'Limite da máscara', 'bumpytexture control': 'Textura de relevo', 'combobox bumpiness': 'Padrão de relevo', 'shinytexture control': 'Textura especular', 'combobox shininess': 'Brilho especular', 'glossiness': 'Polimento', 'environment': 'Reflexo do ambiente', 'shinycolorswatch': 'Cor especular',
    'media_info': 'Endereço da mídia', 'add_media': 'Escolher a mídia', 'delete_media': 'Remover a mídia', 'button align': 'Alinhar a mídia', 'combobox texgen': 'Mapeamento: padrão ou planar', 'TexScaleU': 'Repetição horizontal (difuso)', 'TexScaleV': 'Repetição vertical (difuso)', 'rptctrl': 'Repetições por metro', 'TexRot': 'Rotação (difuso)', 'TexOffsetU': 'Deslocamento horizontal (difuso)', 'TexOffsetV': 'Deslocamento vertical (difuso)',
    'bumpyScaleU': 'Repetição horizontal (relevo)', 'bumpyScaleV': 'Repetição vertical (relevo)', 'bumpyRot': 'Rotação (relevo)', 'bumpyOffsetU': 'Deslocamento horizontal (relevo)', 'bumpyOffsetV': 'Deslocamento vertical (relevo)', 'shinyScaleU': 'Repetição horizontal (especular)', 'shinyScaleV': 'Repetição vertical (especular)', 'shinyRot': 'Rotação (especular)', 'shinyOffsetU': 'Deslocamento horizontal (especular)', 'shinyOffsetV': 'Deslocamento vertical (especular)',
    'checkbox planar align': 'Alinhar faces planares', 'button align textures': 'Alinhar texturas', 'title_media': 'Visor usado para ler o título da mídia',
    'gltfTextureScaleU': 'PBR: repetição U', 'gltfTextureScaleV': 'PBR: repetição V', 'gltfRptctrl': 'PBR: repetições por metro', 'gltfTextureRotation': 'PBR: rotação', 'gltfTextureOffsetU': 'PBR: deslocamento U', 'gltfTextureOffsetV': 'PBR: deslocamento V',
    # conteúdo
    'button new script': 'Criar script no objeto', 'button new notecard': 'Criar nota no objeto', 'button permissions': 'Permissões do conteúdo em lote', 'button publish': 'Abrir em editor externo', 'contents_filter': 'Filtrar o conteúdo', 'contents_inventory': 'Inventário do objeto (arrastar e soltar, abrir, renomear, excluir)',
    # grade
    'GridResolution': 'Passo da grade (m)', 'GridDrawSize': 'Extensão da grade (m)', 'GridSubUnit': 'Encaixar em subdivisões', 'GridOpacity': 'Opacidade da grade', 'grid_opacity_label': 'Rótulo da opacidade',
    # seletor de textura
    'preview_widget': 'Prévia da textura', 'mode_selection': 'Origem: inventário, local ou assado', 'Default': 'Textura padrão', 'Blank': 'Textura em branco', 'None': 'Sem textura', 'Pipette': 'Conta-gotas', 'inventory search editor': 'Buscar no inventário', 'inventory panel': 'Texturas do inventário', 'l_add_btn': 'Adicionar arquivo local', 'l_rem_btn': 'Remover arquivo local', 'l_upl_btn': 'Enviar arquivo local', 'l_name_list': 'Arquivos locais', 'l_bake_use_texture_combo_box': 'Textura assada do avatar', 'Select': 'Confirmar', 'Cancel': 'Cancelar', 'apply_immediate_check': 'Aplicar na hora',
    # seletor de cor
    'rspin': 'Vermelho', 'gspin': 'Verde', 'bspin': 'Azul', 'hspin': 'Matiz', 'sspin': 'Saturação', 'lspin': 'Luminosidade', 'hex_color': 'Código hexadecimal', 'apply_immediate': 'Aplicar na hora', 'color_pipette': 'Conta-gotas', 'copy_color_combobox': 'Copiar a cor em um formato', 'select_btn': 'Confirmar', 'cancel_btn': 'Cancelar',
    # mídia
    'OK': 'Aplicar e fechar', 'Apply': 'Aplicar', 'tab_container': 'Abas da mídia', 'panel_media_settings_general.xml': 'Aba geral da mídia', 'panel_media_settings_security.xml': 'Aba de segurança', 'panel_media_settings_permissions.xml': 'Aba de permissões', 'home_url': 'Endereço inicial', 'preview_media': 'Prévia', 'current_url_reset_btn': 'Voltar ao endereço inicial', 'auto_loop': 'Repetir', 'first_click_interact': 'Primeiro clique já interage', 'auto_zoom': 'Aproximar ao clicar', 'auto_play': 'Tocar sozinho', 'auto_scale': 'Escalar para a face', 'width_pixels': 'Largura (px)', 'height_pixels': 'Altura (px)',
    'whitelist_enable': 'Restringir a uma lista de endereços', 'whitelist': 'Endereços permitidos', 'whitelist_add': 'Adicionar endereço', 'whitelist_del': 'Remover endereço', 'controls': 'Barra de controles', 'perms_owner_interact': 'Dono pode interagir', 'perms_owner_control': 'Dono vê os controles', 'perms_group_name': 'Nome do grupo', 'perms_group_interact': 'Grupo pode interagir', 'perms_group_control': 'Grupo vê os controles', 'perms_anyone_interact': 'Todos podem interagir', 'perms_anyone_control': 'Todos veem os controles',
    # permissões em lote
    'check_animation': 'Incluir animações', 'check_bodypart': 'Incluir partes do corpo', 'check_clothing': 'Incluir roupas', 'check_gesture': 'Incluir gestos', 'check_notecard': 'Incluir notas', 'check_object': 'Incluir objetos', 'check_script': 'Incluir scripts', 'check_sound': 'Incluir sons', 'check_texture': 'Incluir texturas', 'check_settings': 'Incluir ajustes de ambiente', 'check_materials': 'Incluir materiais', 'check_all': 'Marcar todos', 'check_none': 'Desmarcar todos', 'share_with_group': 'Compartilhar com o grupo', 'everyone_copy': 'Todos podem copiar', 'next_owner_modify': 'Próximo dono modifica', 'next_owner_copy': 'Próximo dono copia', 'next_owner_transfer': 'Próximo dono transfere', 'queue output': 'Resultado por item', 'ok': 'Aplicar e fechar', 'apply': 'Aplicar', 'close': 'Fechar',
}

WINDOW_TITLE = {'tools': 'Ferramentas de construção', 'grid': 'Opções da grade', 'texture-picker': 'Seletor de textura', 'color-picker': 'Seletor de cor', 'media': 'Configurações de mídia', 'weights': 'Pesos do objeto (mais informações)', 'bulk-permissions': 'Permissões do conteúdo em lote'}

MODE_OF = [('Foco', ('focus_radio_group', 'slider zoom')), ('Mover', ('move_radio_group',)),
           ('Editar', ('edit_radio_group', 'checkbox edit linked parts', 'link_btn', 'unlink_btn', 'checkbox uniform', 'checkbox stretch', 'checkbox snap', 'combobox grid mode', 'Options...', 'text ruler')),
           ('Criar', ('Tool', 'checkbox sticky', 'checkbox copy')), ('Terreno', ('land_radio_group', 'land info panel', 'Bulldozer', 'Dozer', 'Strength', 'slider brush', 'slider force', 'button apply to')),
           ('Seleção', ('selection_', 'cost_text', 'link_num'))]


def group_of(win, r):
    if win != 'tools':
        return WINDOW_TITLE[win]
    if r['tab'] != '(janela)':
        return 'Aba ' + TAB.get(r['tab'], r['tab'])
    top = r['id'].split('/')[1]
    if r['name'] == 'Object Info Tabs':
        return 'Cabeçalho e ferramentas'
    for mode, prefixes in MODE_OF:
        if any(top.startswith(p) for p in prefixes):
            return 'Modo ' + mode if mode != 'Seleção' else 'Informações da seleção'
    return 'Cabeçalho e ferramentas'


def describe(r):
    name, kind = r['name'], r['tag']
    if name in D:
        return D[name], True
    shown = (r.get('label') or r.get('text') or '').strip()
    if kind in ('text', 'view_border', 'loading_indicator', 'icon'):
        if kind == 'view_border':
            return 'Divisor', True
        if kind == 'loading_indicator':
            return 'Indicador de espera de um valor', True
        return ('Rótulo «%s»' % shown if shown else 'Rótulo preenchido pelo código (%s)' % name), True
    hint = (r.get('tool_tip') or shown or name).strip()
    return 'Original: «%s»' % ' '.join(hint.split())[:90], False


def main():
    map_path, files_path, root = sys.argv[1:4]
    windows = json.load(io.open(map_path, encoding='utf-8'))
    out_dir = os.path.join(root, 'docs', 'design')
    os.makedirs(out_dir, exist_ok=True)
    s = lambda v: None if v is None else int(round(v * SCALE))
    data = {'source': {'repository': 'https://github.com/secondlife/viewer', 'tag': TAG, 'commit': COMMIT, 'license': 'LGPL-2.1'},
            'method': 'positions computed from the XUI files with the rules of llui/llview.cpp, llfloater.cpp and lltabcontainer.cpp; top-left origin; header included',
            'lifeverse': {'reference': [1920, 1080], 'scale': SCALE, 'toolsWindowOrigin': list(ORIGIN), 'coordinates': 'relative to the top-left corner of each window, in reference pixels'},
            'windows': []}
    unnamed = []
    md = []
    total = 0
    for w in windows:
        W, H = w['width'], w['height']
        rows = []
        for r in w['widgets']:
            text, known = describe(r)
            if not known:
                unnamed.append((w['key'], r['name']))
            where = {'x': r['abs_x'], 'y': r['abs_y'], 'w': r['w'], 'h': r['h']}
            rows.append({'id': r['id'], 'name': r['name'], 'window': w['key'], 'group': group_of(w['key'], r), 'type': r['tag'], 'component': KIND.get(r['tag'], r['tag']),
                         'sourceFile': r['source'], 'sourceLine': r['line'], 'original': where, 'relativeToParent': {'x': r['x'], 'y': r['y']},
                         'normalized': {'x': round(r['abs_x'] / W, 4), 'y': round(r['abs_y'] / H, 4), 'w': round((r['w'] or 0) / W, 4), 'h': round((r['h'] or 0) / H, 4)},
                         'lifeverse': {k: s(v) for k, v in where.items()}, 'function': text, 'visibleAtLoad': r['visible'], 'enabledAtLoad': r['enabled'],
                         'originalLabel': r.get('label') or r.get('text'), 'tooltip': r.get('tool_tip'), 'follows': r.get('follows'), 'callbacks': r.get('callbacks', []),
                         'setting': r.get('control_name'), 'items': r.get('items', []), 'labelWidth': r.get('label_width'),
                         'limits': {k: r[k] for k in ('min_val', 'max_val', 'increment', 'decimal_digits', 'initial_value') if k in r} or None, 'notes': r['notes']})
        total += len(rows)
        data['windows'].append({'key': w['key'], 'title': WINDOW_TITLE[w['key']], 'file': w['file'], 'original': {'w': W, 'h': H, 'header': w['header_height'], 'childrenOffsetY': w['children_offset_y']},
                                'lifeverse': {'w': s(W), 'h': s(H)}, 'widgets': rows})
        md.append('\n## %s (`%s`)\n' % (WINDOW_TITLE[w['key']], w['file']))
        md.append('Janela original: **%d × %d** (cabeçalho de %d incluído; filhos deslocados %d para baixo). No LifeVerse: **%d × %d**.\n' % (W, H, w['header_height'], w['children_offset_y'], s(W), s(H)))
        groups = []
        for row in rows:
            if row['group'] not in groups:
                groups.append(row['group'])
        order = ['Cabeçalho e ferramentas', 'Modo Foco', 'Modo Mover', 'Modo Editar', 'Modo Criar', 'Modo Terreno', 'Informações da seleção', 'Aba Geral', 'Aba Objeto', 'Aba Recursos', 'Aba Textura', 'Aba Conteúdo']
        groups.sort(key=lambda g: order.index(g) if g in order else 99)
        for g in groups:
            md.append('\n### %s\n' % g)
            md.append('| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |')
            md.append('|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|')
            for row in (x for x in rows if x['group'] == g):
                o, l = row['original'], row['lifeverse']
                flags = ([] if row['visibleAtLoad'] else ['oculto ao abrir']) + ([] if row['enabledAtLoad'] else ['desabilitado ao abrir'])
                flags += [{'altura padrão': 'altura não declarada', 'largura herdada': 'largura herdada do anterior'}.get(n, n) for n in row['notes'] if n != 'posição dada pelo contêiner de abas']
                ident = row['id'].split('/', 1)[1].replace('|', '/')
                md.append('| `%s` | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |' % (
                    ident, g.replace('Aba ', '').replace('Modo ', ''), row['component'], o['x'], o['y'], o['w'], o['h'], l['x'], l['y'], l['w'], l['h'],
                    row['function'].replace('|', '/') + (' *(%s)*' % '; '.join(flags) if flags else '')))

    files = [line.split('\t')[0] for line in io.open(files_path, encoding='utf-8').read().split('\n') if line]
    intro = u'''# Editor de construção — mapa de coordenadas do Second Life

Gerado por `unity/tools/sl-build-map/` a partir do visualizador oficial do Second Life, **tag `%s`** (commit `%s`), a última versão marcada como estável no GitHub em 28/11/2026. Dados estruturados (com callbacks, dicas, limites, opções das listas e linha de origem): [build-editor-coordinate-map.json](build-editor-coordinate-map.json). Total: **%d componentes** em %d janelas.

## Como as posições foram calculadas

- Quase todos os XML destas janelas usam `layout="topleft"`, então a origem já é o canto superior esquerdo. A exceção são os **três painéis de mídia** (`panel_media_settings_*.xml`), no leiaute antigo (origem embaixo): neles `bottom_delta` negativo desce, e a posição foi convertida para o canto superior esquerdo.
- A maioria dos valores é **relativa ao componente anterior**. As regras são as de `llui/llview.cpp` (`applyXUILayout`): `top_pad` a partir da borda de baixo do anterior; `top_delta` e `left_delta` a partir do canto do anterior; `left_pad` a partir da borda direita; sem nada, 4 px abaixo do anterior; só com `left_pad`/`left_delta`, alinhado pela **borda de baixo**; valores negativos contam da borda oposta.
- Cabeçalho (`llfloater.cpp`): 25 px; as janelas antigas declaram 18 e são esticadas 7 px, o que desce todos os filhos. A janela de ferramentas, de 295 × 651 no XML, mede **295 × 658**.
- Abas (`lltabcontainer.cpp`): o painel começa `tab_height` abaixo do topo das abas, com 1 px de borda e sobreposição 1 (`settings.xml`).
- As coordenadas das tabelas são **relativas ao canto superior esquerdo de cada janela**; as normalizadas (`X/largura` etc.) estão no JSON.

## Limites desta extração (conferir nos protótipos)

- São as posições **de carregamento**. O código C++ muda visibilidade, habilitação e alguns rótulos conforme o estado (ver a [matriz de fidelidade](build-editor-feature-parity.md), seção "Estados condicionais"); quase não muda posições (uma exceção em `llpanelpermissions.cpp`, o ícone do dono).
- Componentes sem altura declarada aparecem com a marca *altura não declarada*: vale a do widget (botão e lista, 23) ou o mínimo do motor (10); o desenho real pode depender da fonte.
- `LLPANEL_BORDER_WIDTH` = 1 foi **assumido** (a constante fica em um arquivo que não foi baixado).
- Na janela "mais informações" (pesos) as últimas linhas passam da altura que o próprio XML declara (479).
- **Conferência visual parcial:** a aba Objeto foi comparada com a captura da [wiki oficial](https://wiki.secondlife.com/wiki/Building_Tools) — mesmas duas colunas e a mesma ordem —, mas ela é do visualizador **1.23**, antigo (nele o material ainda ficava nessa aba). **Falta uma captura em tamanho real do visualizador atual** para conferir pixel a pixel.

## Decisão de escala para o LifeVerse (proposta, a aprovar)

O pedido sugeriu 744 × 954 na posição (50, 70). A janela original tem proporção 295 : 658 (0,448); 744 × 954 teria 0,780 e quebraria a organização. Como o pedido dá prioridade à proporção original, a proposta é uma **escala única de %.2f**: **%d × %d** em (%d, %d) na referência 1920 × 1080 — a altura sugerida, com a largura que a proporção permite.

- Cada linha de 19 px do original vira 28 px, o bastante para a fonte de 13 px do LifeVerse; textos em português mais longos que os ingleses precisam ser conferidos nos protótipos.
- `FloatingWindow` limita hoje a altura a `tela − 84 − 108` (888 px em 1080p). A janela de construção precisaria dispensar a margem de baixo (em 1920 × 1080 ela não cobre a barra central, que começa em x = 528, mas passa por cima do chat e do cartão da ilha; em 1280 × 720 e 1366 × 768 a barra chegaria sob a janela, e os protótipos a encerram acima dela), ou a escala cairia para 1,35.
- Em 1280 × 720 a escala automática mínima (0,85) deixa 847 unidades de altura: o topo (ferramentas e opções) ficaria fixo e o conteúdo das abas rolaria.

## Arquivos originais usados

%d arquivos, baixados com autorização do dono para uma pasta temporária (nada deles entra no repositório; o código não foi copiado). Para as posições: `floater_tools.xml`, `panel_tools_texture.xml`, `floater_build_options.xml`, `floater_texture_ctrl.xml`, `floater_color_picker.xml`, `floater_media_settings.xml` e `panel_media_settings_*.xml`, `floater_object_weights.xml`, `floater_bulk_perms.xml`, `widgets/*.xml`. Nesta versão **não existem** `panel_tools_general.xml`, `panel_tools_object.xml`, `panel_tools_features.xml` nem `panel_tools_contents.xml`: essas abas estão dentro de `floater_tools.xml`.
''' % (TAG, COMMIT, total, len(windows), SCALE, round(295 * SCALE), round(658 * SCALE), ORIGIN[0], ORIGIN[1], len(files))
    if unnamed:
        intro += '\n%d componentes estão descritos só pelo texto original (marcados «Original:»).\n' % len(unnamed)
    io.open(os.path.join(out_dir, 'build-editor-coordinate-map.md'), 'w', encoding='utf-8', newline='\n').write(intro + '\n'.join(md) + '\n')
    data['sourceFiles'] = files
    io.open(os.path.join(out_dir, 'build-editor-coordinate-map.json'), 'w', encoding='utf-8', newline='\n').write(json.dumps(data, ensure_ascii=False, indent=1) + '\n')
    print('components', total, 'without a description', len(unnamed), unnamed[:20])


main()
