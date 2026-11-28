# Editor de construção — mapa de coordenadas do Second Life

Gerado por `unity/tools/sl-build-map/` a partir do visualizador oficial do Second Life, **tag `Second_Life_Release#e960123-26.4`** (commit `e960123230d521e8557a9582100bebcac36a7ca2`), a última versão marcada como estável no GitHub em 28/11/2026. Dados estruturados (com callbacks, dicas, limites, opções das listas e linha de origem): [build-editor-coordinate-map.json](build-editor-coordinate-map.json). Total: **489 componentes** em 7 janelas.

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

O pedido sugeriu 744 × 954 na posição (50, 70). A janela original tem proporção 295 : 658 (0,448); 744 × 954 teria 0,780 e quebraria a organização. Como o pedido dá prioridade à proporção original, a proposta é uma **escala única de 1.45**: **428 × 954** em (50, 70) na referência 1920 × 1080 — a altura sugerida, com a largura que a proporção permite.

- Cada linha de 19 px do original vira 28 px, o bastante para a fonte de 13 px do LifeVerse; textos em português mais longos que os ingleses precisam ser conferidos nos protótipos.
- `FloatingWindow` limita hoje a altura a `tela − 84 − 108` (888 px em 1080p). A janela de construção precisaria dispensar a margem de baixo (em 1920 × 1080 ela não cobre a barra central, que começa em x = 528, mas passa por cima do chat e do cartão da ilha; em 1280 × 720 e 1366 × 768 a barra chegaria sob a janela, e os protótipos a encerram acima dela), ou a escala cairia para 1,35.
- Em 1280 × 720 a escala automática mínima (0,85) deixa 847 unidades de altura: o topo (ferramentas e opções) ficaria fixo e o conteúdo das abas rolaria.

## Arquivos originais usados

159 arquivos, baixados com autorização do dono para uma pasta temporária (nada deles entra no repositório; o código não foi copiado). Para as posições: `floater_tools.xml`, `panel_tools_texture.xml`, `floater_build_options.xml`, `floater_texture_ctrl.xml`, `floater_color_picker.xml`, `floater_media_settings.xml` e `panel_media_settings_*.xml`, `floater_object_weights.xml`, `floater_bulk_perms.xml`, `widgets/*.xml`. Nesta versão **não existem** `panel_tools_general.xml`, `panel_tools_object.xml`, `panel_tools_features.xml` nem `panel_tools_contents.xml`: essas abas estão dentro de `floater_tools.xml`.

## Ferramentas de construção (`floater_tools.xml`)

Janela original: **295 × 658** (cabeçalho de 25 incluído; filhos deslocados 7 para baixo). No LifeVerse: **428 × 954**.


### Cabeçalho e ferramentas

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `button focus` | Cabeçalho e ferramentas | Botão | 10 | 11 | 35 | 25 | 14 | 16 | 51 | 36 | Modo Foco (câmera) |
| `button move` | Cabeçalho e ferramentas | Botão | 55 | 11 | 35 | 25 | 80 | 16 | 51 | 36 | Modo Mover (arrastar objetos) |
| `button edit` | Cabeçalho e ferramentas | Botão | 100 | 11 | 35 | 25 | 145 | 16 | 51 | 36 | Modo Editar |
| `button create` | Cabeçalho e ferramentas | Botão | 145 | 11 | 35 | 25 | 210 | 16 | 51 | 36 | Modo Criar |
| `button land` | Cabeçalho e ferramentas | Botão | 190 | 11 | 35 | 25 | 276 | 16 | 51 | 36 | Modo Terreno |
| `text status` | Cabeçalho e ferramentas | Rótulo | 8 | 39 | 285 | 20 | 12 | 57 | 413 | 29 | Rótulo «Drag to move, shift-drag to copy» |
| `Object Info Tabs` | Cabeçalho e ferramentas | Abas | 0 | 180 | 295 | 476 | 0 | 261 | 428 | 690 | As cinco abas de propriedades |

### Modo Foco

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `focus_radio_group` | Foco | Grupo de opções | 10 | 66 | 285 | 70 | 14 | 96 | 413 | 102 | Opções do modo Foco *(largura herdada do anterior)* |
| `focus_radio_group/radio zoom` | Foco | Opção | 10 | 72 | 285 | 10 | 14 | 104 | 413 | 14 | Câmera: aproximar/afastar *(altura não declarada; largura herdada do anterior)* |
| `focus_radio_group/radio orbit` | Foco | Opção | 10 | 88 | 285 | 10 | 14 | 128 | 413 | 14 | Câmera: orbitar (Ctrl) *(altura não declarada; largura herdada do anterior)* |
| `focus_radio_group/radio pan` | Foco | Opção | 10 | 104 | 285 | 10 | 14 | 151 | 413 | 14 | Câmera: panorâmica (Ctrl+Shift) *(altura não declarada; largura herdada do anterior)* |
| `slider zoom` | Foco | Controle deslizante | 110 | 64 | 134 | 14 | 160 | 93 | 194 | 20 | Nível de aproximação |

### Modo Mover

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `move_radio_group` | Mover | Grupo de opções | 10 | 66 | 134 | 70 | 14 | 96 | 194 | 102 | Opções do modo Mover *(largura herdada do anterior)* |
| `move_radio_group/radio move` | Mover | Opção | 10 | 72 | 134 | 10 | 14 | 104 | 194 | 14 | Arrastar no plano *(altura não declarada; largura herdada do anterior)* |
| `move_radio_group/radio lift` | Mover | Opção | 10 | 88 | 134 | 10 | 14 | 128 | 194 | 14 | Arrastar na vertical (Ctrl) *(altura não declarada; largura herdada do anterior)* |
| `move_radio_group/radio spin` | Mover | Opção | 10 | 104 | 134 | 10 | 14 | 151 | 194 | 14 | Girar arrastando (Ctrl+Shift) *(altura não declarada; largura herdada do anterior)* |

### Modo Editar

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `edit_radio_group` | Editar | Grupo de opções | 5 | 62 | 134 | 70 | 7 | 90 | 194 | 102 | Opções do modo Editar *(largura herdada do anterior)* |
| `edit_radio_group/radio position` | Editar | Opção | 5 | 66 | 134 | 10 | 7 | 96 | 194 | 14 | Manipulador de posição *(altura não declarada; largura herdada do anterior)* |
| `edit_radio_group/radio rotate` | Editar | Opção | 5 | 82 | 134 | 10 | 7 | 119 | 194 | 14 | Manipulador de rotação (Ctrl) *(altura não declarada; largura herdada do anterior)* |
| `edit_radio_group/radio stretch` | Editar | Opção | 5 | 98 | 134 | 10 | 7 | 142 | 194 | 14 | Manipulador de escala (Ctrl+Shift) *(altura não declarada; largura herdada do anterior)* |
| `edit_radio_group/radio select face` | Editar | Opção | 5 | 114 | 134 | 10 | 7 | 165 | 194 | 14 | Selecionar faces *(altura não declarada; largura herdada do anterior)* |
| `checkbox edit linked parts` | Editar | Caixa de marcar | 5 | 114 | 134 | 28 | 7 | 165 | 194 | 41 | Selecionar partes de um conjunto vinculado *(largura herdada do anterior)* |
| `link_btn` | Editar | Botão | 143 | 115 | 50 | 20 | 207 | 167 | 72 | 29 | Vincular a seleção |
| `unlink_btn` | Editar | Botão | 195 | 115 | 90 | 20 | 283 | 167 | 130 | 29 | Desvincular |
| `checkbox uniform` | Editar | Caixa de marcar | 143 | 54 | 134 | 19 | 207 | 78 | 194 | 28 | Esticar dos dois lados |
| `checkbox stretch textures` | Editar | Caixa de marcar | 143 | 71 | 134 | 19 | 207 | 103 | 194 | 28 | Esticar a textura junto |
| `checkbox snap to grid` | Editar | Caixa de marcar | 143 | 89 | 134 | 18 | 207 | 129 | 194 | 26 | Encaixar na grade |
| `combobox grid mode` | Editar | Lista suspensa | 195 | 90 | 60 | 20 | 283 | 130 | 87 | 29 | Referencial da grade: mundo, local ou outro objeto |
| `Options...` | Editar | Botão | 259 | 90 | 25 | 20 | 376 | 130 | 36 | 29 | Abre as opções da grade |

### Modo Criar

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `ToolCube` | Criar | Botão | 10 | 65 | 20 | 20 | 14 | 94 | 29 | 29 | Cubo |
| `ToolPrism` | Criar | Botão | 39 | 65 | 20 | 20 | 57 | 94 | 29 | 29 | Prisma |
| `ToolPyramid` | Criar | Botão | 68 | 65 | 20 | 20 | 99 | 94 | 29 | 29 | Pirâmide |
| `ToolTetrahedron` | Criar | Botão | 97 | 65 | 20 | 20 | 141 | 94 | 29 | 29 | Tetraedro |
| `ToolCylinder` | Criar | Botão | 126 | 65 | 20 | 20 | 183 | 94 | 29 | 29 | Cilindro |
| `ToolHemiCylinder` | Criar | Botão | 10 | 91 | 20 | 20 | 14 | 132 | 29 | 29 | Semicilindro |
| `ToolCone` | Criar | Botão | 39 | 91 | 20 | 20 | 57 | 132 | 29 | 29 | Cone |
| `ToolHemiCone` | Criar | Botão | 68 | 91 | 20 | 20 | 99 | 132 | 29 | 29 | Semicone |
| `ToolSphere` | Criar | Botão | 97 | 91 | 20 | 20 | 141 | 132 | 29 | 29 | Esfera |
| `ToolHemiSphere` | Criar | Botão | 126 | 91 | 20 | 20 | 183 | 132 | 29 | 29 | Hemisfério |
| `ToolTorus` | Criar | Botão | 10 | 116 | 20 | 20 | 14 | 168 | 29 | 29 | Toro |
| `ToolTube` | Criar | Botão | 39 | 116 | 20 | 20 | 57 | 168 | 29 | 29 | Tubo |
| `ToolRing` | Criar | Botão | 68 | 116 | 20 | 20 | 99 | 168 | 29 | 29 | Anel |
| `ToolTree` | Criar | Botão | 97 | 116 | 20 | 20 | 141 | 168 | 29 | 29 | Árvore |
| `ToolGrass` | Criar | Botão | 126 | 116 | 20 | 20 | 183 | 168 | 29 | 29 | Grama |
| `checkbox sticky` | Criar | Caixa de marcar | 155 | 62 | 128 | 19 | 225 | 90 | 186 | 28 | Manter a ferramenta de criar selecionada |
| `checkbox copy selection` | Criar | Caixa de marcar | 155 | 79 | 134 | 19 | 225 | 115 | 194 | 28 | Criar cópias do objeto selecionado |
| `checkbox copy centers` | Criar | Caixa de marcar | 173 | 99 | 134 | 19 | 251 | 144 | 194 | 28 | Centralizar a cópia |
| `checkbox copy rotates` | Criar | Caixa de marcar | 173 | 116 | 134 | 19 | 251 | 168 | 194 | 28 | Girar a cópia conforme a face |

### Modo Terreno

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `land_radio_group` | Terreno | Grupo de opções | 4 | 61 | 114 | 105 | 6 | 88 | 165 | 152 | Ação do terreno |
| `land_radio_group/radio select land` | Terreno | Opção | 4 | 60 | 134 | 19 | 6 | 87 | 194 | 28 | Selecionar uma área *(relativo à borda oposta)* |
| `land_radio_group/radio flatten` | Terreno | Opção | 4 | 75 | 114 | 19 | 6 | 109 | 165 | 28 | Nivelar |
| `land_radio_group/radio raise` | Terreno | Opção | 4 | 90 | 114 | 19 | 6 | 130 | 165 | 28 | Elevar |
| `land_radio_group/radio lower` | Terreno | Opção | 4 | 105 | 114 | 19 | 6 | 152 | 165 | 28 | Rebaixar |
| `land_radio_group/radio smooth` | Terreno | Opção | 4 | 120 | 114 | 19 | 6 | 174 | 165 | 28 | Suavizar |
| `land_radio_group/radio noise` | Terreno | Opção | 4 | 135 | 114 | 19 | 6 | 196 | 165 | 28 | Rugosidade |
| `land_radio_group/radio revert` | Terreno | Opção | 4 | 150 | 114 | 19 | 6 | 218 | 165 | 28 | Restaurar |
| `Bulldozer:` | Terreno | Rótulo | 135 | 64 | 100 | 12 | 196 | 93 | 145 | 17 | Rótulo «Bulldozer:» |
| `Dozer Size:` | Terreno | Rótulo | 135 | 81 | 50 | 12 | 196 | 117 | 72 | 17 | Rótulo «Size» |
| `slider brush size` | Terreno | Controle deslizante | 185 | 78 | 80 | 19 | 268 | 113 | 116 | 28 | Tamanho do pincel |
| `Strength:` | Terreno | Rótulo | 135 | 102 | 50 | 12 | 196 | 148 | 72 | 17 | Rótulo «Strength» |
| `slider force` | Terreno | Controle deslizante | 185 | 99 | 80 | 19 | 268 | 144 | 116 | 28 | Força do pincel |
| `button apply to selection` | Terreno | Botão | 135 | 123 | 82 | 23 | 196 | 178 | 119 | 33 | Aplicar à área selecionada |
| `land info panel` | Terreno | Painel | 0 | 180 | 295 | 384 | 0 | 261 | 428 | 557 | Painel do terreno (substitui as abas no modo Terreno) |
| `land info panel/label_parcel_info` | Terreno | Rótulo | 20 | 204 | 240 | 19 | 29 | 296 | 348 | 28 | Rótulo «Parcel Information» |
| `land info panel/label_area_price` | Terreno | Rótulo | 30 | 228 | 150 | 19 | 44 | 331 | 218 | 28 | Rótulo «Price: L$[PRICE] for [AREA] m²» |
| `land info panel/label_area` | Terreno | Rótulo | 30 | 228 | 150 | 19 | 44 | 331 | 218 | 28 | Rótulo «Area: [AREA] m²» |
| `land info panel/button about land` | Terreno | Botão | 30 | 251 | 125 | 23 | 44 | 364 | 181 | 33 | Sobre o terreno |
| `land info panel/checkbox show owners` | Terreno | Caixa de marcar | 30 | 282 | 205 | 19 | 44 | 409 | 297 | 28 | Mostrar donos no chão |
| `land info panel/label_parcel_modify` | Terreno | Rótulo | 20 | 332 | 240 | 19 | 29 | 481 | 348 | 28 | Rótulo «Modify Parcel» |
| `land info panel/button subdivide land` | Terreno | Botão | 30 | 352 | 125 | 23 | 44 | 510 | 181 | 33 | Dividir terreno |
| `land info panel/button join land` | Terreno | Botão | 30 | 379 | 125 | 23 | 44 | 550 | 181 | 33 | Unir terrenos |
| `land info panel/label_parcel_trans` | Terreno | Rótulo | 20 | 436 | 240 | 19 | 29 | 632 | 348 | 28 | Rótulo «Land Transactions» |
| `land info panel/button buy land` | Terreno | Botão | 30 | 456 | 125 | 23 | 44 | 661 | 181 | 33 | Comprar terreno |
| `land info panel/button abandon land` | Terreno | Botão | 30 | 483 | 125 | 23 | 44 | 700 | 181 | 33 | Abandonar terreno |

### Informações da seleção

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `selection_empty` | Informações da seleção | Rótulo | 10 | 146 | 100 | 16 | 14 | 212 | 145 | 23 | Aviso: nada selecionado |
| `selection_faces` | Informações da seleção | Rótulo | 10 | 146 | 280 | 16 | 14 | 212 | 406 | 23 | Faces selecionadas *(oculto ao abrir)* |
| `selection_count` | Informações da seleção | Rótulo | 10 | 162 | 280 | 16 | 14 | 235 | 406 | 23 | Objetos selecionados e impacto no terreno *(oculto ao abrir)* |
| `cost_text_border` | Informações da seleção | Divisor | 6 | 142 | 282 | 0 | 9 | 206 | 409 | 0 | Separador acima das abas |

### Aba Geral

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `Object Info Tabs/General` | Geral | Painel | 1 | 205 | 293 | 450 | 1 | 297 | 425 | 652 | Aba Geral |
| `Object Info Tabs/General/Name:` | Geral | Rótulo | 11 | 210 | 90 | 10 | 16 | 304 | 130 | 14 | Rótulo «Name:» |
| `Object Info Tabs/General/Object Name` | Geral | Campo de texto | 101 | 210 | 170 | 19 | 146 | 304 | 246 | 28 | Nome do objeto |
| `Object Info Tabs/General/Description:` | Geral | Rótulo | 11 | 232 | 90 | 10 | 16 | 336 | 130 | 14 | Rótulo «Description:» |
| `Object Info Tabs/General/Object Description` | Geral | Campo de texto | 101 | 232 | 170 | 19 | 146 | 336 | 246 | 28 | Descrição do objeto |
| `Object Info Tabs/General/Creator:` | Geral | Rótulo | 11 | 258 | 90 | 29 | 16 | 374 | 130 | 42 | Rótulo «Creator:» |
| `Object Info Tabs/General/Creator Icon` | Geral | Ícone de pessoa | 102 | 258 | 18 | 18 | 148 | 374 | 26 | 26 | Foto do criador *(oculto ao abrir)* |
| `Object Info Tabs/General/Creator Name` | Geral | Rótulo | 121 | 257 | 170 | 29 | 175 | 373 | 246 | 42 | Criador (abre o perfil) |
| `Object Info Tabs/General/Owner:` | Geral | Rótulo | 11 | 289 | 90 | 29 | 16 | 419 | 130 | 42 | Rótulo «Owner:» |
| `Object Info Tabs/General/Owner Icon` | Geral | Ícone de pessoa | 102 | 289 | 18 | 18 | 148 | 419 | 26 | 26 | Foto do dono *(oculto ao abrir)* |
| `Object Info Tabs/General/Owner Group Icon` | Geral | Ícone de grupo | 102 | 289 | 18 | 18 | 148 | 419 | 26 | 26 | Insígnia do grupo dono *(oculto ao abrir)* |
| `Object Info Tabs/General/Owner Name` | Geral | Rótulo | 121 | 288 | 170 | 29 | 175 | 418 | 246 | 42 | Dono (abre o perfil) |
| `Object Info Tabs/General/Group:` | Geral | Rótulo | 11 | 320 | 75 | 18 | 16 | 464 | 109 | 26 | Rótulo «Group:» |
| `Object Info Tabs/General/Group Name Proxy` | Geral | Nome clicável | 109 | 320 | 142 | 18 | 158 | 464 | 206 | 26 | Grupo do objeto |
| `Object Info Tabs/General/button set group` | Geral | Botão | 264 | 315 | 23 | 23 | 383 | 457 | 33 | 33 | Escolher o grupo |
| `Object Info Tabs/General/checkbox share with group` | Geral | Caixa de marcar | 101 | 343 | 87 | 19 | 146 | 497 | 126 | 28 | Compartilhar com o grupo |
| `Object Info Tabs/General/button deed` | Geral | Botão | 207 | 339 | 80 | 23 | 300 | 492 | 116 | 33 | Transferir a propriedade ao grupo |
| `Object Info Tabs/General/label click action` | Geral | Rótulo | 11 | 372 | 82 | 16 | 16 | 539 | 119 | 23 | Rótulo «Click to:» |
| `Object Info Tabs/General/clickaction` | Geral | Lista suspensa | 103 | 365 | 130 | 23 | 149 | 529 | 188 | 33 | Ação ao clicar |
| `Object Info Tabs/General/checkbox for sale` | Geral | Caixa de marcar | 8 | 392 | 97 | 23 | 12 | 568 | 141 | 33 | À venda |
| `Object Info Tabs/General/Edit Cost` | Geral | Campo numérico | 105 | 395 | 85 | 20 | 152 | 573 | 123 | 29 | Preço |
| `Object Info Tabs/General/sale type` | Geral | Lista suspensa | 198 | 392 | 89 | 23 | 287 | 568 | 129 | 33 | O que se vende: cópia, conteúdo ou original |
| `Object Info Tabs/General/search_check` | Geral | Caixa de marcar | 8 | 420 | 110 | 15 | 12 | 609 | 160 | 22 | Exibir na busca |
| `Object Info Tabs/General/perms_build` | Geral | Painel | 1 | 439 | 290 | 121 | 1 | 637 | 420 | 175 | Quadro de permissões |
| `Object Info Tabs/General/perms_build/perm_modify` | Geral | Rótulo | 11 | 448 | 264 | 32 | 16 | 650 | 383 | 46 | O que você pode fazer com o objeto |
| `Object Info Tabs/General/perms_build/Anyone can:` | Geral | Rótulo | 11 | 484 | 250 | 10 | 16 | 702 | 362 | 14 | Rótulo «Anyone:» *(altura não declarada)* |
| `Object Info Tabs/General/perms_build/checkbox allow everyone move` | Geral | Caixa de marcar | 11 | 498 | 85 | 19 | 16 | 722 | 123 | 28 | Qualquer pessoa pode mover |
| `Object Info Tabs/General/perms_build/checkbox allow everyone copy` | Geral | Caixa de marcar | 96 | 498 | 90 | 19 | 139 | 722 | 130 | 28 | Qualquer pessoa pode copiar |
| `Object Info Tabs/General/perms_build/Next owner can:` | Geral | Rótulo | 11 | 521 | 250 | 19 | 16 | 755 | 362 | 28 | Rótulo «Next owner:» |
| `Object Info Tabs/General/perms_build/checkbox next owner can modify` | Geral | Caixa de marcar | 11 | 544 | 85 | 10 | 16 | 789 | 123 | 14 | Próximo dono pode modificar |
| `Object Info Tabs/General/perms_build/checkbox next owner can copy` | Geral | Caixa de marcar | 96 | 535 | 80 | 19 | 139 | 776 | 116 | 28 | Próximo dono pode copiar |
| `Object Info Tabs/General/perms_build/checkbox next owner can transfer` | Geral | Caixa de marcar | 176 | 535 | 100 | 19 | 255 | 776 | 145 | 28 | Próximo dono pode transferir |
| `Object Info Tabs/General/perms_build/B:` | Geral | Rótulo | 231 | 462 | 80 | 10 | 335 | 670 | 116 | 14 | Máscara base (permissões avançadas) |
| `Object Info Tabs/General/perms_build/O:` | Geral | Rótulo | 231 | 474 | 80 | 10 | 335 | 687 | 116 | 14 | Máscara do dono (avançadas) |
| `Object Info Tabs/General/perms_build/G:` | Geral | Rótulo | 231 | 486 | 80 | 10 | 335 | 705 | 116 | 14 | Máscara do grupo (avançadas) |
| `Object Info Tabs/General/perms_build/E:` | Geral | Rótulo | 231 | 498 | 80 | 10 | 335 | 722 | 116 | 14 | Máscara de todos (avançadas) |
| `Object Info Tabs/General/perms_build/N:` | Geral | Rótulo | 231 | 510 | 80 | 10 | 335 | 740 | 116 | 14 | Máscara do próximo dono (avançadas) |
| `Object Info Tabs/General/perms_build/F:` | Geral | Rótulo | 231 | 522 | 80 | 10 | 335 | 757 | 116 | 14 | Máscara efetiva (avançadas) |
| `Object Info Tabs/General/pathfinding_attrs_panel` | Geral | Painel | 1 | 560 | 290 | 25 | 1 | 812 | 420 | 36 | Atributos de navegação (pathfinding) |
| `Object Info Tabs/General/pathfinding_attrs_panel/pathfinding_attributes_label` | Geral | Rótulo | 11 | 564 | 150 | 10 | 16 | 818 | 218 | 14 | Rótulo «Pathfinding attributes:» *(altura não declarada)* |
| `Object Info Tabs/General/pathfinding_attrs_panel/pathfinding_attributes_value` | Geral | Rótulo | 161 | 564 | 130 | 10 | 233 | 818 | 188 | 14 | Valor do atributo de navegação *(altura não declarada)* |

### Aba Objeto

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `Object Info Tabs/Object` | Objeto | Painel | 1 | 205 | 293 | 450 | 1 | 297 | 425 | 652 | Aba Objeto |
| `Object Info Tabs/Object/checkbox locked` | Objeto | Caixa de marcar | 11 | 210 | 123 | 19 | 16 | 304 | 178 | 28 | Bloqueado |
| `Object Info Tabs/Object/Physical Checkbox Ctrl` | Objeto | Caixa de marcar | 11 | 229 | 123 | 19 | 16 | 332 | 178 | 28 | Físico |
| `Object Info Tabs/Object/Temporary Checkbox Ctrl` | Objeto | Caixa de marcar | 11 | 248 | 123 | 19 | 16 | 360 | 178 | 28 | Temporário |
| `Object Info Tabs/Object/Phantom Checkbox Ctrl` | Objeto | Caixa de marcar | 11 | 267 | 123 | 19 | 16 | 387 | 178 | 28 | Fantasma (sem colisão) |
| `Object Info Tabs/Object/object_horizontal` | Objeto | Divisor | 11 | 306 | 95 | 0 | 16 | 444 | 138 | 0 | Separador horizontal |
| `Object Info Tabs/Object/clipboard_pos_btn` | Objeto | Botão de menu | 11 | 319 | 19 | 11 | 16 | 463 | 28 | 16 | Copiar/colar posição |
| `Object Info Tabs/Object/label position` | Objeto | Rótulo | 38 | 319 | 121 | 10 | 55 | 463 | 175 | 14 | Rótulo «Position (m)» |
| `Object Info Tabs/Object/Pos X` | Objeto | Campo numérico | 11 | 337 | 87 | 19 | 16 | 489 | 126 | 28 | Posição X |
| `Object Info Tabs/Object/Pos Y` | Objeto | Campo numérico | 11 | 359 | 87 | 19 | 16 | 521 | 126 | 28 | Posição Y |
| `Object Info Tabs/Object/Pos Z` | Objeto | Campo numérico | 11 | 381 | 87 | 19 | 16 | 552 | 126 | 28 | Posição Z (altura no Second Life) |
| `Object Info Tabs/Object/clipboard_size_btn` | Objeto | Botão de menu | 11 | 413 | 19 | 11 | 16 | 599 | 28 | 16 | Copiar/colar tamanho |
| `Object Info Tabs/Object/label size` | Objeto | Rótulo | 38 | 413 | 121 | 10 | 55 | 599 | 175 | 14 | Rótulo «Size (m)» |
| `Object Info Tabs/Object/Scale X` | Objeto | Campo numérico | 11 | 431 | 87 | 19 | 16 | 625 | 126 | 28 | Tamanho X |
| `Object Info Tabs/Object/Scale Y` | Objeto | Campo numérico | 11 | 453 | 87 | 19 | 16 | 657 | 126 | 28 | Tamanho Y |
| `Object Info Tabs/Object/Scale Z` | Objeto | Campo numérico | 11 | 475 | 87 | 19 | 16 | 689 | 126 | 28 | Tamanho Z |
| `Object Info Tabs/Object/clipboard_rot_btn` | Objeto | Botão de menu | 11 | 507 | 19 | 11 | 16 | 735 | 28 | 16 | Copiar/colar rotação |
| `Object Info Tabs/Object/label rotation` | Objeto | Rótulo | 38 | 507 | 121 | 10 | 55 | 735 | 175 | 14 | Rótulo «Rotation (°)» |
| `Object Info Tabs/Object/Rot X` | Objeto | Campo numérico | 11 | 525 | 87 | 19 | 16 | 761 | 126 | 28 | Rotação X |
| `Object Info Tabs/Object/Rot Y` | Objeto | Campo numérico | 11 | 547 | 87 | 19 | 16 | 793 | 126 | 28 | Rotação Y |
| `Object Info Tabs/Object/Rot Z` | Objeto | Campo numérico | 11 | 569 | 87 | 19 | 16 | 825 | 126 | 28 | Rotação Z |
| `Object Info Tabs/Object/object_vertical` | Objeto | Divisor | 118 | 211 | 0 | 500 | 171 | 306 | 0 | 725 | Separador entre as colunas |
| `Object Info Tabs/Object/comboBaseType` | Objeto | Lista suspensa | 126 | 211 | 125 | 19 | 183 | 306 | 181 | 28 | Tipo de forma |
| `Object Info Tabs/Object/clipboard_obj_params_btn` | Objeto | Botão de menu | 259 | 213 | 22 | 15 | 376 | 309 | 32 | 22 | Copiar/colar parâmetros da forma |
| `Object Info Tabs/Object/text cut` | Objeto | Rótulo | 126 | 233 | 150 | 10 | 183 | 338 | 218 | 14 | Rótulo «Path Cut (begin/end)» |
| `Object Info Tabs/Object/cut begin` | Objeto | Campo numérico | 126 | 247 | 68 | 16 | 183 | 358 | 99 | 23 | Corte de trajetória: início |
| `Object Info Tabs/Object/cut end` | Objeto | Campo numérico | 204 | 247 | 68 | 16 | 296 | 358 | 99 | 23 | Corte de trajetória: fim |
| `Object Info Tabs/Object/text hollow` | Objeto | Rótulo | 126 | 270 | 68 | 10 | 183 | 392 | 99 | 14 | Rótulo «Hollow» |
| `Object Info Tabs/Object/text skew` | Objeto | Rótulo | 204 | 270 | 63 | 10 | 296 | 392 | 91 | 14 | Rótulo «Skew» |
| `Object Info Tabs/Object/Scale 1` | Objeto | Campo numérico | 126 | 284 | 68 | 19 | 183 | 412 | 99 | 28 | Oco (%) |
| `Object Info Tabs/Object/Skew` | Objeto | Campo numérico | 204 | 284 | 68 | 19 | 296 | 412 | 99 | 28 | Inclinação (skew) |
| `Object Info Tabs/Object/Hollow Shape` | Objeto | Rótulo | 126 | 310 | 150 | 15 | 183 | 450 | 218 | 22 | Rótulo «Hollow Shape» |
| `Object Info Tabs/Object/hole` | Objeto | Lista suspensa | 126 | 323 | 150 | 23 | 183 | 468 | 218 | 33 | Formato do oco |
| `Object Info Tabs/Object/text twist` | Objeto | Rótulo | 126 | 353 | 150 | 10 | 183 | 512 | 218 | 14 | Rótulo «Twist (begin/end)» |
| `Object Info Tabs/Object/Twist Begin` | Objeto | Campo numérico | 126 | 367 | 68 | 19 | 183 | 532 | 99 | 28 | Torção: início |
| `Object Info Tabs/Object/Twist End` | Objeto | Campo numérico | 204 | 367 | 68 | 19 | 296 | 532 | 99 | 28 | Torção: fim |
| `Object Info Tabs/Object/scale_taper` | Objeto | Rótulo | 126 | 393 | 150 | 10 | 183 | 570 | 218 | 14 | Rótulo «Taper» |
| `Object Info Tabs/Object/scale_hole` | Objeto | Rótulo | 126 | 393 | 150 | 10 | 183 | 570 | 218 | 14 | Rótulo «Hole Size» *(oculto ao abrir)* |
| `Object Info Tabs/Object/Taper Scale X` | Objeto | Campo numérico | 126 | 407 | 68 | 19 | 183 | 590 | 99 | 28 | Afunilamento X ou tamanho do buraco X |
| `Object Info Tabs/Object/Taper Scale Y` | Objeto | Campo numérico | 204 | 407 | 68 | 19 | 296 | 590 | 99 | 28 | Afunilamento Y ou tamanho do buraco Y |
| `Object Info Tabs/Object/text topshear` | Objeto | Rótulo | 126 | 431 | 141 | 10 | 183 | 625 | 204 | 14 | Rótulo «Top Shear» |
| `Object Info Tabs/Object/Shear X` | Objeto | Campo numérico | 126 | 445 | 68 | 19 | 183 | 645 | 99 | 28 | Cisalhamento do topo X |
| `Object Info Tabs/Object/Shear Y` | Objeto | Campo numérico | 204 | 445 | 68 | 19 | 296 | 645 | 99 | 28 | Cisalhamento do topo Y |
| `Object Info Tabs/Object/advanced_cut` | Objeto | Rótulo | 126 | 471 | 150 | 10 | 183 | 683 | 218 | 14 | Rótulo «Profile Cut (begin/end)» *(oculto ao abrir)* |
| `Object Info Tabs/Object/advanced_dimple` | Objeto | Rótulo | 126 | 471 | 150 | 10 | 183 | 683 | 218 | 14 | Rótulo «Dimple (begin/end)» *(oculto ao abrir)* |
| `Object Info Tabs/Object/advanced_slice` | Objeto | Rótulo | 126 | 471 | 150 | 10 | 183 | 683 | 218 | 14 | Rótulo «Slice (begin/end)» |
| `Object Info Tabs/Object/Path Limit Begin` | Objeto | Campo numérico | 126 | 484 | 68 | 19 | 183 | 702 | 99 | 28 | Corte de perfil / côncavo / fatia: início |
| `Object Info Tabs/Object/Path Limit End` | Objeto | Campo numérico | 204 | 484 | 68 | 19 | 296 | 702 | 99 | 28 | Corte de perfil / côncavo / fatia: fim |
| `Object Info Tabs/Object/text taper2` | Objeto | Rótulo | 126 | 510 | 150 | 10 | 183 | 740 | 218 | 14 | Rótulo «Taper» *(oculto ao abrir)* |
| `Object Info Tabs/Object/Taper X` | Objeto | Campo numérico | 126 | 523 | 68 | 19 | 183 | 758 | 99 | 28 | Afunilamento X (toro/tubo/anel) *(oculto ao abrir)* |
| `Object Info Tabs/Object/Taper Y` | Objeto | Campo numérico | 204 | 523 | 68 | 19 | 296 | 758 | 99 | 28 | Afunilamento Y (toro/tubo/anel) *(oculto ao abrir)* |
| `Object Info Tabs/Object/text radius delta` | Objeto | Rótulo | 126 | 549 | 78 | 10 | 183 | 796 | 113 | 14 | Rótulo «Radius» *(oculto ao abrir)* |
| `Object Info Tabs/Object/text revolutions` | Objeto | Rótulo | 204 | 549 | 68 | 10 | 296 | 796 | 99 | 14 | Rótulo «Revolutions» *(oculto ao abrir)* |
| `Object Info Tabs/Object/Radius Offset` | Objeto | Campo numérico | 126 | 563 | 68 | 19 | 183 | 816 | 99 | 28 | Deslocamento do raio *(oculto ao abrir)* |
| `Object Info Tabs/Object/Revolutions` | Objeto | Campo numérico | 204 | 563 | 68 | 19 | 296 | 816 | 99 | 28 | Revoluções *(oculto ao abrir)* |
| `Object Info Tabs/Object/sculpt texture control` | Objeto | Seletor de textura | 126 | 275 | 145 | 141 | 183 | 399 | 210 | 204 | Textura de escultura *(oculto ao abrir)* |
| `Object Info Tabs/Object/sculpt mirror control` | Objeto | Caixa de marcar | 126 | 424 | 130 | 19 | 183 | 615 | 188 | 28 | Espelhar a escultura *(oculto ao abrir)* |
| `Object Info Tabs/Object/sculpt invert control` | Objeto | Caixa de marcar | 126 | 447 | 121 | 19 | 183 | 648 | 175 | 28 | Virar a escultura do avesso *(oculto ao abrir)* |
| `Object Info Tabs/Object/label sculpt type` | Objeto | Rótulo | 126 | 476 | 130 | 10 | 183 | 690 | 188 | 14 | Rótulo «Stitching type» *(oculto ao abrir)* |
| `Object Info Tabs/Object/sculpt type control` | Objeto | Lista suspensa | 126 | 490 | 150 | 19 | 183 | 710 | 218 | 28 | Tipo de costura da escultura *(oculto ao abrir)* |

### Aba Recursos

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `Object Info Tabs/Features` | Recursos | Painel | 1 | 205 | 293 | 450 | 1 | 297 | 425 | 652 | Aba Recursos |
| `Object Info Tabs/Features/clipboard_features_params_btn` | Recursos | Botão de menu | 259 | 213 | 22 | 15 | 376 | 309 | 32 | 22 | Copiar/colar recursos |
| `Object Info Tabs/Features/select_single` | Recursos | Rótulo | 11 | 210 | 252 | 20 | 16 | 304 | 365 | 29 | Aviso: selecione uma só primitiva |
| `Object Info Tabs/Features/edit_object` | Recursos | Rótulo | 11 | 210 | 252 | 10 | 16 | 304 | 365 | 14 | Título: recursos do objeto |
| `Object Info Tabs/Features/Animated Mesh Checkbox Ctrl` | Recursos | Caixa de marcar | 11 | 230 | 121 | 15 | 16 | 334 | 175 | 22 | Malha animada |
| `Object Info Tabs/Features/Flexible1D Checkbox Ctrl` | Recursos | Caixa de marcar | 11 | 260 | 121 | 10 | 16 | 377 | 175 | 14 | Objeto flexível |
| `Object Info Tabs/Features/FlexNumSections` | Recursos | Campo numérico | 11 | 280 | 128 | 19 | 16 | 406 | 186 | 28 | Flexível: suavidade |
| `Object Info Tabs/Features/FlexGravity` | Recursos | Campo numérico | 11 | 303 | 128 | 19 | 16 | 439 | 186 | 28 | Flexível: gravidade |
| `Object Info Tabs/Features/FlexFriction` | Recursos | Campo numérico | 11 | 326 | 128 | 19 | 16 | 473 | 186 | 28 | Flexível: resistência ao ar |
| `Object Info Tabs/Features/FlexWind` | Recursos | Campo numérico | 11 | 349 | 128 | 19 | 16 | 506 | 186 | 28 | Flexível: vento |
| `Object Info Tabs/Features/FlexTension` | Recursos | Campo numérico | 11 | 372 | 128 | 19 | 16 | 539 | 186 | 28 | Flexível: tensão |
| `Object Info Tabs/Features/FlexForceX` | Recursos | Campo numérico | 11 | 395 | 128 | 19 | 16 | 573 | 186 | 28 | Flexível: força X |
| `Object Info Tabs/Features/FlexForceY` | Recursos | Campo numérico | 11 | 418 | 128 | 19 | 16 | 606 | 186 | 28 | Flexível: força Y |
| `Object Info Tabs/Features/FlexForceZ` | Recursos | Campo numérico | 11 | 441 | 128 | 19 | 16 | 639 | 186 | 28 | Flexível: força Z |
| `Object Info Tabs/Features/object_horizontal` | Recursos | Divisor | 9 | 480 | 278 | 0 | 13 | 696 | 403 | 0 | Separador horizontal |
| `Object Info Tabs/Features/Light Checkbox Ctrl` | Recursos | Caixa de marcar | 11 | 488 | 60 | 16 | 16 | 708 | 87 | 23 | O objeto emite luz |
| `Object Info Tabs/Features/colorswatch` | Recursos | Amostra de cor | 81 | 487 | 40 | 50 | 117 | 706 | 58 | 72 | Cor |
| `Object Info Tabs/Features/light texture control` | Recursos | Seletor de textura | 138 | 487 | 32 | 48 | 200 | 706 | 46 | 70 | Imagem projetada pela luz |
| `Object Info Tabs/Features/clipboard_light_params_btn` | Recursos | Botão de menu | 259 | 487 | 22 | 15 | 376 | 706 | 32 | 22 | Copiar/colar luz |
| `Object Info Tabs/Features/Light Intensity` | Recursos | Campo numérico | 11 | 528 | 128 | 19 | 16 | 766 | 186 | 28 | Luz: intensidade |
| `Object Info Tabs/Features/Light FOV` | Recursos | Campo numérico | 145 | 531 | 120 | 16 | 210 | 770 | 174 | 23 | Projetor: campo de visão |
| `Object Info Tabs/Features/Light Radius` | Recursos | Campo numérico | 11 | 550 | 128 | 19 | 16 | 798 | 186 | 28 | Luz: alcance |
| `Object Info Tabs/Features/Light Focus` | Recursos | Campo numérico | 145 | 553 | 120 | 16 | 210 | 802 | 174 | 23 | Projetor: foco |
| `Object Info Tabs/Features/Light Falloff` | Recursos | Campo numérico | 11 | 572 | 128 | 19 | 16 | 829 | 186 | 28 | Luz: atenuação |
| `Object Info Tabs/Features/Light Ambiance` | Recursos | Campo numérico | 145 | 575 | 120 | 16 | 210 | 834 | 174 | 23 | Projetor: ambiência |
| `Object Info Tabs/Features/Reflection Probe` | Recursos | Caixa de marcar | 11 | 601 | 60 | 16 | 16 | 871 | 87 | 23 | Sonda de reflexo |
| `Object Info Tabs/Features/Probe Volume Type` | Recursos | Lista suspensa | 145 | 601 | 140 | 19 | 210 | 871 | 203 | 28 | Sonda: formato (esfera/caixa) |
| `Object Info Tabs/Features/Probe Dynamic` | Recursos | Caixa de marcar | 11 | 623 | 60 | 16 | 16 | 903 | 87 | 23 | Sonda dinâmica |
| `Object Info Tabs/Features/Probe Update Label` | Recursos | Rótulo | 11 | 621 | 100 | 10 | 16 | 900 | 145 | 14 | Rótulo «Probe Update» |
| `Object Info Tabs/Features/Probe Update Type` | Recursos | Lista suspensa | 145 | 621 | 140 | 19 | 210 | 900 | 203 | 28 | Sonda: atualização (estática, dinâmica, espelho) |
| `Object Info Tabs/Features/Probe Ambiance` | Recursos | Campo numérico | 11 | 641 | 120 | 16 | 16 | 929 | 174 | 23 | Sonda: ambiência |
| `Object Info Tabs/Features/Probe Near Clip` | Recursos | Campo numérico | 145 | 641 | 120 | 16 | 210 | 929 | 174 | 23 | Sonda: corte próximo |
| `Object Info Tabs/Features/label physicsshapetype` | Recursos | Rótulo | 145 | 243 | 121 | 10 | 210 | 352 | 175 | 14 | Rótulo «Physics Shape Type:» |
| `Object Info Tabs/Features/Physics Shape Type Combo Ctrl` | Recursos | Lista suspensa | 145 | 258 | 108 | 19 | 210 | 374 | 157 | 28 | Forma física (preenchida pelo código: nenhuma, primitiva, convexa) |
| `Object Info Tabs/Features/material` | Recursos | Lista suspensa | 145 | 282 | 150 | 19 | 210 | 409 | 218 | 28 | Material físico |
| `Object Info Tabs/Features/Physics Gravity` | Recursos | Campo numérico | 145 | 311 | 132 | 19 | 210 | 451 | 191 | 28 | Multiplicador de gravidade |
| `Object Info Tabs/Features/Physics Friction` | Recursos | Campo numérico | 145 | 334 | 132 | 19 | 210 | 484 | 191 | 28 | Atrito |
| `Object Info Tabs/Features/Physics Density` | Recursos | Campo numérico | 145 | 357 | 132 | 19 | 210 | 518 | 191 | 28 | Densidade |
| `Object Info Tabs/Features/Physics Restitution` | Recursos | Campo numérico | 145 | 384 | 132 | 19 | 210 | 557 | 191 | 28 | Restituição (quique) |

### Aba Textura

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `Object Info Tabs/Texture` | Textura | Painel | 1 | 205 | 293 | 450 | 1 | 297 | 425 | 652 | Aba Textura |
| `Object Info Tabs/Texture/clipboard_color_params_btn` | Textura | Botão de menu | 259 | 213 | 22 | 15 | 376 | 309 | 32 | 22 | Copiar/colar cor |
| `Object Info Tabs/Texture/color label` | Textura | Rótulo | 11 | 211 | 64 | 10 | 16 | 306 | 93 | 14 | Rótulo «Color» |
| `Object Info Tabs/Texture/colorswatch` | Textura | Amostra de cor | 11 | 225 | 54 | 22 | 16 | 326 | 78 | 32 | Cor |
| `Object Info Tabs/Texture/color trans` | Textura | Rótulo | 80 | 211 | 110 | 10 | 116 | 306 | 160 | 14 | Rótulo «Transparency %» |
| `Object Info Tabs/Texture/ColorTrans` | Textura | Campo numérico | 80 | 225 | 80 | 19 | 116 | 326 | 116 | 28 | Transparência (%) |
| `Object Info Tabs/Texture/glow label` | Textura | Rótulo | 175 | 211 | 80 | 10 | 254 | 306 | 116 | 14 | Rótulo «Glow» |
| `Object Info Tabs/Texture/glow` | Textura | Campo numérico | 175 | 225 | 77 | 19 | 254 | 326 | 112 | 28 | Brilho (glow) |
| `Object Info Tabs/Texture/checkbox fullbright` | Textura | Caixa de marcar | 8 | 248 | 81 | 19 | 12 | 360 | 117 | 28 | Luz total |
| `Object Info Tabs/Texture/checkbox_hide_water` | Textura | Caixa de marcar | 173 | 248 | 81 | 19 | 251 | 360 | 117 | 28 | Esconder a água sobre a face |
| `Object Info Tabs/Texture/object_horizontal` | Textura | Divisor | 9 | 281 | 278 | 0 | 13 | 407 | 403 | 0 | Separador horizontal |
| `Object Info Tabs/Texture/label_matmedia` | Textura | Rótulo | 13 | 293 | 90 | 10 | 19 | 425 | 130 | 14 | Rótulo «Material» |
| `Object Info Tabs/Texture/combobox matmedia` | Textura | Lista suspensa | 11 | 308 | 90 | 23 | 16 | 447 | 130 | 33 | Modo: material clássico, PBR ou mídia |
| `Object Info Tabs/Texture/radio_material_type` | Textura | Grupo de opções | 106 | 288 | 150 | 84 | 154 | 418 | 218 | 122 | Mapa em edição (clássico) *(oculto ao abrir)* |
| `Object Info Tabs/Texture/radio_material_type/Texture (diffuse)` | Textura | Opção | 106 | 288 | 150 | 16 | 154 | 418 | 218 | 23 | Mapa difuso *(largura herdada do anterior)* |
| `Object Info Tabs/Texture/radio_material_type/Bumpiness (normal)` | Textura | Opção | 106 | 305 | 150 | 16 | 154 | 442 | 218 | 23 | Mapa de relevo *(largura herdada do anterior)* |
| `Object Info Tabs/Texture/radio_material_type/Shininess (specular)` | Textura | Opção | 106 | 322 | 150 | 16 | 154 | 467 | 218 | 23 | Mapa especular *(largura herdada do anterior)* |
| `Object Info Tabs/Texture/radio_pbr_type` | Textura | Grupo de opções | 106 | 288 | 150 | 84 | 154 | 418 | 218 | 122 | Canal em edição (PBR) *(oculto ao abrir)* |
| `Object Info Tabs/Texture/radio_pbr_type/Complete material` | Textura | Opção | 106 | 288 | 150 | 16 | 154 | 418 | 218 | 23 | PBR: material completo *(largura herdada do anterior)* |
| `Object Info Tabs/Texture/radio_pbr_type/Base color` | Textura | Opção | 106 | 305 | 150 | 16 | 154 | 442 | 218 | 23 | PBR: cor base *(largura herdada do anterior)* |
| `Object Info Tabs/Texture/radio_pbr_type/Metallic/roughness` | Textura | Opção | 106 | 322 | 150 | 16 | 154 | 467 | 218 | 23 | PBR: metal e aspereza *(largura herdada do anterior)* |
| `Object Info Tabs/Texture/radio_pbr_type/Emissive` | Textura | Opção | 106 | 339 | 150 | 16 | 154 | 492 | 218 | 23 | PBR: emissivo *(largura herdada do anterior)* |
| `Object Info Tabs/Texture/radio_pbr_type/Normal` | Textura | Opção | 106 | 356 | 150 | 16 | 154 | 516 | 218 | 23 | PBR: normal *(largura herdada do anterior)* |
| `Object Info Tabs/Texture/clipboard_texture_params_btn` | Textura | Botão de menu | 259 | 288 | 22 | 15 | 376 | 418 | 32 | 22 | Copiar/colar textura |
| `Object Info Tabs/Texture/checkbox_sync_settings` | Textura | Caixa de marcar | 9 | 322 | 160 | 47 | 13 | 467 | 232 | 68 | Travar repetições entre os mapas |
| `Object Info Tabs/Texture/pbr_control` | Textura | Seletor de textura | 11 | 374 | 64 | 80 | 16 | 542 | 93 | 116 | Material PBR |
| `Object Info Tabs/Texture/pbr_from_inventory` | Textura | Botão | 85 | 374 | 140 | 23 | 123 | 542 | 203 | 33 | Escolher material no inventário |
| `Object Info Tabs/Texture/material_permissions_loading_label` | Textura | Rótulo | 85 | 401 | 160 | 10 | 123 | 581 | 232 | 14 | Aviso: carregando conteúdo *(oculto ao abrir)* |
| `Object Info Tabs/Texture/edit_selected_pbr` | Textura | Botão | 85 | 401 | 140 | 23 | 123 | 581 | 203 | 33 | Editar o material selecionado |
| `Object Info Tabs/Texture/save_selected_pbr` | Textura | Botão | 85 | 428 | 140 | 23 | 123 | 621 | 203 | 33 | Salvar o material no inventário |
| `Object Info Tabs/Texture/texture control` | Textura | Seletor de textura | 11 | 374 | 64 | 80 | 16 | 542 | 93 | 116 | Textura difusa |
| `Object Info Tabs/Texture/label alphamode` | Textura | Rótulo | 85 | 374 | 90 | 10 | 123 | 542 | 130 | 14 | Rótulo «Alpha mode» |
| `Object Info Tabs/Texture/combobox alphamode` | Textura | Lista suspensa | 85 | 388 | 120 | 23 | 123 | 563 | 174 | 33 | Modo de transparência |
| `Object Info Tabs/Texture/label maskcutoff` | Textura | Rótulo | 85 | 415 | 90 | 10 | 123 | 602 | 130 | 14 | Rótulo «Mask cutoff» |
| `Object Info Tabs/Texture/maskcutoff` | Textura | Campo numérico | 85 | 429 | 80 | 19 | 123 | 622 | 116 | 28 | Limite da máscara |
| `Object Info Tabs/Texture/bumpytexture control` | Textura | Seletor de textura | 11 | 374 | 64 | 80 | 16 | 542 | 93 | 116 | Textura de relevo |
| `Object Info Tabs/Texture/label bumpiness` | Textura | Rótulo | 85 | 374 | 90 | 10 | 123 | 542 | 130 | 14 | Rótulo «Bumpiness» |
| `Object Info Tabs/Texture/combobox bumpiness` | Textura | Lista suspensa | 85 | 388 | 90 | 23 | 123 | 563 | 130 | 33 | Padrão de relevo |
| `Object Info Tabs/Texture/shinytexture control` | Textura | Seletor de textura | 11 | 374 | 64 | 80 | 16 | 542 | 93 | 116 | Textura especular |
| `Object Info Tabs/Texture/label shininess` | Textura | Rótulo | 85 | 378 | 90 | 10 | 123 | 548 | 130 | 14 | Rótulo «Shininess» |
| `Object Info Tabs/Texture/combobox shininess` | Textura | Lista suspensa | 185 | 372 | 90 | 23 | 268 | 539 | 130 | 33 | Brilho especular |
| `Object Info Tabs/Texture/label glossiness` | Textura | Rótulo | 85 | 402 | 116 | 10 | 123 | 583 | 168 | 14 | Rótulo «Glossiness» |
| `Object Info Tabs/Texture/glossiness` | Textura | Campo numérico | 211 | 398 | 64 | 19 | 306 | 577 | 93 | 28 | Polimento |
| `Object Info Tabs/Texture/label environment` | Textura | Rótulo | 85 | 425 | 116 | 10 | 123 | 616 | 168 | 14 | Rótulo «Environment» |
| `Object Info Tabs/Texture/environment` | Textura | Campo numérico | 211 | 421 | 64 | 19 | 306 | 610 | 93 | 28 | Reflexo do ambiente |
| `Object Info Tabs/Texture/label shinycolor` | Textura | Rótulo | 85 | 448 | 116 | 10 | 123 | 650 | 168 | 14 | Rótulo «Color» |
| `Object Info Tabs/Texture/shinycolorswatch` | Textura | Amostra de cor | 211 | 444 | 64 | 45 | 306 | 644 | 93 | 65 | Cor especular |
| `Object Info Tabs/Texture/media_info` | Textura | Rótulo | 11 | 394 | 280 | 9 | 16 | 571 | 406 | 13 | Endereço da mídia |
| `Object Info Tabs/Texture/add_media` | Textura | Botão | 11 | 407 | 85 | 18 | 16 | 590 | 123 | 26 | Escolher a mídia |
| `Object Info Tabs/Texture/delete_media` | Textura | Botão | 101 | 407 | 85 | 18 | 146 | 590 | 123 | 26 | Remover a mídia |
| `Object Info Tabs/Texture/button align` | Textura | Botão | 191 | 407 | 85 | 18 | 277 | 590 | 123 | 26 | Alinhar a mídia |
| `Object Info Tabs/Texture/tex gen` | Textura | Rótulo | 11 | 471 | 140 | 10 | 16 | 683 | 203 | 14 | Rótulo «Mapping» |
| `Object Info Tabs/Texture/combobox texgen` | Textura | Lista suspensa | 151 | 468 | 125 | 23 | 219 | 679 | 181 | 33 | Mapeamento: padrão ou planar |
| `Object Info Tabs/Texture/TexScaleU` | Textura | Campo numérico | 11 | 496 | 265 | 19 | 16 | 719 | 384 | 28 | Repetição horizontal (difuso) |
| `Object Info Tabs/Texture/TexScaleV` | Textura | Campo numérico | 11 | 519 | 265 | 19 | 16 | 753 | 384 | 28 | Repetição vertical (difuso) |
| `Object Info Tabs/Texture/rptctrl` | Textura | Campo numérico | 11 | 542 | 265 | 19 | 16 | 786 | 384 | 28 | Repetições por metro |
| `Object Info Tabs/Texture/TexRot` | Textura | Campo numérico | 11 | 565 | 265 | 19 | 16 | 819 | 384 | 28 | Rotação (difuso) |
| `Object Info Tabs/Texture/TexOffsetU` | Textura | Campo numérico | 11 | 588 | 265 | 19 | 16 | 853 | 384 | 28 | Deslocamento horizontal (difuso) |
| `Object Info Tabs/Texture/TexOffsetV` | Textura | Campo numérico | 11 | 611 | 265 | 19 | 16 | 886 | 384 | 28 | Deslocamento vertical (difuso) |
| `Object Info Tabs/Texture/bumpyScaleU` | Textura | Campo numérico | 11 | 496 | 265 | 19 | 16 | 719 | 384 | 28 | Repetição horizontal (relevo) |
| `Object Info Tabs/Texture/bumpyScaleV` | Textura | Campo numérico | 11 | 519 | 265 | 19 | 16 | 753 | 384 | 28 | Repetição vertical (relevo) |
| `Object Info Tabs/Texture/bumpyRot` | Textura | Campo numérico | 11 | 565 | 265 | 19 | 16 | 819 | 384 | 28 | Rotação (relevo) |
| `Object Info Tabs/Texture/bumpyOffsetU` | Textura | Campo numérico | 11 | 588 | 265 | 19 | 16 | 853 | 384 | 28 | Deslocamento horizontal (relevo) |
| `Object Info Tabs/Texture/bumpyOffsetV` | Textura | Campo numérico | 11 | 611 | 265 | 19 | 16 | 886 | 384 | 28 | Deslocamento vertical (relevo) |
| `Object Info Tabs/Texture/shinyScaleU` | Textura | Campo numérico | 11 | 496 | 265 | 19 | 16 | 719 | 384 | 28 | Repetição horizontal (especular) |
| `Object Info Tabs/Texture/shinyScaleV` | Textura | Campo numérico | 11 | 519 | 265 | 19 | 16 | 753 | 384 | 28 | Repetição vertical (especular) |
| `Object Info Tabs/Texture/shinyRot` | Textura | Campo numérico | 11 | 565 | 265 | 19 | 16 | 819 | 384 | 28 | Rotação (especular) |
| `Object Info Tabs/Texture/shinyOffsetU` | Textura | Campo numérico | 11 | 588 | 265 | 19 | 16 | 853 | 384 | 28 | Deslocamento horizontal (especular) |
| `Object Info Tabs/Texture/shinyOffsetV` | Textura | Campo numérico | 11 | 611 | 265 | 19 | 16 | 886 | 384 | 28 | Deslocamento vertical (especular) |
| `Object Info Tabs/Texture/checkbox planar align` | Textura | Caixa de marcar | 8 | 631 | 260 | 16 | 12 | 915 | 377 | 23 | Alinhar faces planares |
| `Object Info Tabs/Texture/button align textures` | Textura | Botão | 10 | 436 | 66 | 20 | 14 | 632 | 96 | 29 | Alinhar texturas |
| `Object Info Tabs/Texture/title_media` | Textura | Visor de mídia | 1 | 452 | 4 | 4 | 1 | 655 | 6 | 6 | Visor usado para ler o título da mídia *(oculto ao abrir; desabilitado ao abrir)* |
| `Object Info Tabs/Texture/gltfTextureScaleU` | Textura | Campo numérico | 11 | 486 | 265 | 19 | 16 | 705 | 384 | 28 | PBR: repetição U |
| `Object Info Tabs/Texture/gltfTextureScaleV` | Textura | Campo numérico | 11 | 509 | 265 | 19 | 16 | 738 | 384 | 28 | PBR: repetição V |
| `Object Info Tabs/Texture/gltfRptctrl` | Textura | Campo numérico | 11 | 532 | 265 | 19 | 16 | 771 | 384 | 28 | PBR: repetições por metro |
| `Object Info Tabs/Texture/gltfTextureRotation` | Textura | Campo numérico | 11 | 555 | 265 | 19 | 16 | 805 | 384 | 28 | PBR: rotação |
| `Object Info Tabs/Texture/gltfTextureOffsetU` | Textura | Campo numérico | 11 | 578 | 265 | 19 | 16 | 838 | 384 | 28 | PBR: deslocamento U |
| `Object Info Tabs/Texture/gltfTextureOffsetV` | Textura | Campo numérico | 11 | 601 | 265 | 19 | 16 | 871 | 384 | 28 | PBR: deslocamento V |

### Aba Conteúdo

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `Object Info Tabs/Contents` | Conteúdo | Painel | 1 | 205 | 293 | 450 | 1 | 297 | 425 | 652 | Aba Conteúdo |
| `Object Info Tabs/Contents/button new script` | Conteúdo | Botão com menu | 8 | 215 | 137 | 23 | 12 | 312 | 199 | 33 | Criar script no objeto |
| `Object Info Tabs/Contents/button new notecard` | Conteúdo | Botão | 152 | 215 | 137 | 23 | 220 | 312 | 199 | 33 | Criar nota no objeto |
| `Object Info Tabs/Contents/button permissions` | Conteúdo | Botão | 8 | 242 | 137 | 23 | 12 | 351 | 199 | 33 | Permissões do conteúdo em lote |
| `Object Info Tabs/Contents/button publish` | Conteúdo | Botão | 152 | 242 | 137 | 23 | 220 | 351 | 199 | 33 | Abrir em editor externo |
| `Object Info Tabs/Contents/contents_filter` | Conteúdo | Campo de filtro | 11 | 272 | 275 | 23 | 16 | 394 | 399 | 33 | Filtrar o conteúdo |
| `Object Info Tabs/Contents/contents_inventory` | Conteúdo | Árvore de conteúdo | 11 | 302 | 275 | 340 | 16 | 438 | 399 | 493 | Inventário do objeto (arrastar e soltar, abrir, renomear, excluir) |

## Opções da grade (`floater_build_options.xml`)

Janela original: **264 × 177** (cabeçalho de 25 incluído; filhos deslocados 7 para baixo). No LifeVerse: **383 × 257**.


### Opções da grade

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `GridResolution` | Opções da grade | Campo numérico | 10 | 37 | 200 | 23 | 14 | 54 | 290 | 33 | Passo da grade (m) |
| `GridDrawSize` | Opções da grade | Campo numérico | 10 | 60 | 200 | 23 | 14 | 87 | 290 | 33 | Extensão da grade (m) |
| `GridSubUnit` | Opções da grade | Caixa de marcar | 10 | 83 | 200 | 16 | 14 | 120 | 290 | 23 | Encaixar em subdivisões |
| `grid_opacity_label` | Opções da grade | Rótulo | 10 | 104 | 128 | 16 | 14 | 151 | 186 | 23 | Rótulo da opacidade |
| `GridOpacity` | Opções da grade | Controle deslizante | 10 | 120 | 210 | 18 | 14 | 174 | 304 | 26 | Opacidade da grade |

## Seletor de textura (`floater_texture_ctrl.xml`)

Janela original: **410 × 337** (cabeçalho de 25 incluído; filhos deslocados 7 para baixo). No LifeVerse: **594 × 489**.


### Seletor de textura

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `preview_widget` | Seletor de textura | Área | 4 | 27 | 165 | 165 | 6 | 39 | 239 | 239 | Prévia da textura |
| `Multiple` | Seletor de textura | Rótulo | 4 | 103 | 163 | 17 | 6 | 149 | 236 | 25 | Rótulo «Multiple textures» |
| `mode_selection` | Seletor de textura | Lista suspensa | 6 | 197 | 120 | 20 | 9 | 286 | 174 | 29 | Origem: inventário, local ou assado |
| `size_lbl` | Seletor de textura | Rótulo | 8 | 221 | 120 | 14 | 12 | 320 | 174 | 20 | Rótulo «[DIMENSIONS]» *(largura herdada do anterior)* |
| `over_limit_lbl` | Seletor de textura | Rótulo | 8 | 221 | 164 | 56 | 12 | 320 | 238 | 81 | Rótulo «Selected texture is [TEXDIM]. Inventory image must be square, no less than [MINTEXDIM].» *(oculto ao abrir)* |
| `Default` | Seletor de textura | Botão | 94 | 222 | 73 | 18 | 136 | 322 | 106 | 26 | Textura padrão *(desabilitado ao abrir)* |
| `Blank` | Seletor de textura | Botão | 94 | 245 | 73 | 20 | 136 | 355 | 106 | 29 | Textura em branco |
| `None` | Seletor de textura | Botão | 94 | 270 | 73 | 20 | 136 | 392 | 106 | 29 | Sem textura *(desabilitado ao abrir)* |
| `Pipette` | Seletor de textura | Botão | 18 | 247 | 28 | 28 | 26 | 358 | 41 | 41 | Conta-gotas |
| `preview_disabled` | Seletor de textura | Rótulo | 8 | 292 | 87 | 40 | 12 | 423 | 126 | 58 | Rótulo preenchido pelo código (preview_disabled) *(oculto ao abrir)* |
| `inventory search editor` | Seletor de textura | Campo de filtro | 175 | 27 | 231 | 23 | 254 | 39 | 335 | 33 | Buscar no inventário |
| `inventory panel` | Seletor de textura | Árvore do inventário | 175 | 54 | 231 | 242 | 254 | 78 | 335 | 351 | Texturas do inventário |
| `l_add_btn` | Seletor de textura | Botão | 94 | 222 | 73 | 18 | 136 | 322 | 106 | 26 | Adicionar arquivo local *(oculto ao abrir)* |
| `l_rem_btn` | Seletor de textura | Botão | 94 | 245 | 73 | 20 | 136 | 355 | 106 | 29 | Remover arquivo local *(oculto ao abrir; desabilitado ao abrir)* |
| `l_upl_btn` | Seletor de textura | Botão | 94 | 270 | 73 | 20 | 136 | 392 | 106 | 29 | Enviar arquivo local *(oculto ao abrir; desabilitado ao abrir)* |
| `l_name_list` | Seletor de textura | Lista | 170 | 29 | 235 | 260 | 246 | 42 | 341 | 377 | Arquivos locais *(oculto ao abrir)* |
| `l_bake_use_texture_combo_box` | Seletor de textura | Lista suspensa | 180 | 37 | 118 | 19 | 261 | 54 | 171 | 28 | Textura assada do avatar *(oculto ao abrir)* |
| `Select` | Seletor de textura | Botão | 176 | 307 | 100 | 20 | 255 | 445 | 145 | 29 | Confirmar *(relativo à borda oposta)* |
| `Cancel` | Seletor de textura | Botão | 296 | 307 | 100 | 20 | 429 | 445 | 145 | 29 | Cancelar |
| `apply_immediate_check` | Seletor de textura | Caixa de marcar | 6 | 307 | 150 | 20 | 9 | 445 | 218 | 29 | Aplicar na hora |

## Seletor de cor (`floater_color_picker.xml`)

Janela original: **440 × 467** (cabeçalho de 25 incluído; filhos deslocados 7 para baixo). No LifeVerse: **638 × 677**.


### Seletor de cor

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `r_val_text` | Seletor de cor | Rótulo | 10 | 32 | 413 | 20 | 14 | 46 | 599 | 29 | Rótulo «Red:» |
| `rspin` | Seletor de cor | Campo numérico | 73 | 32 | 50 | 20 | 106 | 46 | 72 | 29 | Vermelho |
| `g_val_text` | Seletor de cor | Rótulo | 10 | 56 | 413 | 20 | 14 | 81 | 599 | 29 | Rótulo «Green:» |
| `gspin` | Seletor de cor | Campo numérico | 73 | 56 | 50 | 20 | 106 | 81 | 72 | 29 | Verde |
| `b_val_text` | Seletor de cor | Rótulo | 10 | 80 | 413 | 20 | 14 | 116 | 599 | 29 | Rótulo «Blue:» |
| `bspin` | Seletor de cor | Campo numérico | 73 | 80 | 50 | 20 | 106 | 116 | 72 | 29 | Azul |
| `h_val_text` | Seletor de cor | Rótulo | 10 | 104 | 413 | 20 | 14 | 151 | 599 | 29 | Rótulo «Hue:» |
| `hspin` | Seletor de cor | Campo numérico | 73 | 104 | 50 | 20 | 106 | 151 | 72 | 29 | Matiz |
| `s_val_text` | Seletor de cor | Rótulo | 10 | 128 | 413 | 20 | 14 | 186 | 599 | 29 | Rótulo «Sat:» |
| `sspin` | Seletor de cor | Campo numérico | 73 | 128 | 50 | 20 | 106 | 186 | 72 | 29 | Saturação |
| `l_val_text` | Seletor de cor | Rótulo | 10 | 152 | 413 | 20 | 14 | 220 | 599 | 29 | Rótulo «Lum:» |
| `lspin` | Seletor de cor | Campo numérico | 73 | 152 | 50 | 20 | 106 | 220 | 72 | 29 | Luminosidade |
| `hex_text` | Seletor de cor | Rótulo | 10 | 178 | 413 | 20 | 14 | 258 | 599 | 29 | Rótulo «Hex:» |
| `hash_text` | Seletor de cor | Rótulo | 60 | 178 | 20 | 20 | 87 | 258 | 29 | 29 | Rótulo «#» |
| `hex_color` | Seletor de cor | Campo de texto | 73 | 176 | 50 | 20 | 106 | 255 | 72 | 29 | Código hexadecimal |
| `apply_immediate` | Seletor de cor | Caixa de marcar | 10 | 436 | 100 | 20 | 14 | 632 | 145 | 29 | Aplicar na hora |
| `color_pipette` | Seletor de cor | Botão | 110 | 428 | 28 | 28 | 160 | 621 | 41 | 41 | Conta-gotas |
| `copy_color_combobox` | Seletor de cor | Lista suspensa | 159 | 436 | 84 | 23 | 231 | 632 | 122 | 33 | Copiar a cor em um formato *(altura não declarada; relativo à borda oposta)* |
| `select_btn` | Seletor de cor | Botão | 260 | 436 | 80 | 23 | 377 | 632 | 116 | 33 | Confirmar *(relativo à borda oposta)* |
| `cancel_btn` | Seletor de cor | Botão | 345 | 436 | 80 | 23 | 500 | 632 | 116 | 33 | Cancelar *(relativo à borda oposta)* |
| `Current color:` | Seletor de cor | Rótulo | 10 | 201 | 110 | 16 | 14 | 291 | 160 | 23 | Rótulo «Current color:» |
| `(Drag below to save.)` | Seletor de cor | Rótulo | 10 | 279 | 130 | 16 | 14 | 405 | 188 | 23 | Rótulo «(Drag below to save)» |

## Configurações de mídia (`floater_media_settings.xml`)

Janela original: **365 × 542** (cabeçalho de 25 incluído; filhos deslocados 7 para baixo). No LifeVerse: **529 × 786**.


### Configurações de mídia

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `OK` | Configurações de mídia | Botão | 75 | 512 | 90 | 20 | 109 | 742 | 130 | 29 | Aplicar e fechar |
| `Cancel` | Configurações de mídia | Botão | 168 | 512 | 90 | 20 | 244 | 742 | 130 | 29 | Cancelar |
| `Apply` | Configurações de mídia | Botão | 261 | 512 | 90 | 20 | 378 | 742 | 130 | 29 | Aplicar |
| `tab_container` | Configurações de mídia | Abas | 0 | 22 | 365 | 485 | 0 | 32 | 529 | 703 | Abas da mídia |
| `tab_container/panel_media_settings_general.xml` | Configurações de mídia | Painel | 1 | 43 | 363 | 463 | 1 | 62 | 526 | 671 | Aba geral da mídia |
| `tab_container/panel_media_settings_general.xml/home_label` | Configurações de mídia | Rótulo | 11 | 53 | 363 | 15 | 16 | 77 | 526 | 22 | Rótulo «Home Page:» *(largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/home_fails_whitelist_label` | Configurações de mídia | Rótulo | 75 | 53 | 363 | 15 | 109 | 77 | 526 | 22 | Rótulo «(This page does not pass the specified whitelist)» *(oculto ao abrir; largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/home_url` | Configurações de mídia | Campo de texto | 11 | 72 | 340 | 20 | 16 | 104 | 493 | 29 | Endereço inicial *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/preview_media` | Configurações de mídia | Visor de mídia | 121 | 97 | 128 | 128 | 175 | 141 | 186 | 186 | Prévia *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/preview_label` | Configurações de mídia | Rótulo | 165 | 225 | 128 | 15 | 239 | 326 | 186 | 22 | Rótulo «Preview» *(largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/current_url_label` | Configurações de mídia | Rótulo | 11 | 245 | 128 | 15 | 16 | 355 | 186 | 22 | Rótulo «Current Page:» *(largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/current_url` | Configurações de mídia | Rótulo | 11 | 260 | 340 | 20 | 16 | 377 | 493 | 29 | Rótulo preenchido pelo código (current_url) *(desabilitado ao abrir; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/current_url_reset_btn` | Configurações de mídia | Botão | 244 | 280 | 110 | 20 | 354 | 406 | 160 | 29 | Voltar ao endereço inicial *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/auto_loop` | Configurações de mídia | Caixa de marcar | 11 | 309 | 150 | 16 | 16 | 448 | 218 | 23 | Repetir *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/first_click_interact` | Configurações de mídia | Caixa de marcar | 11 | 334 | 150 | 16 | 16 | 484 | 218 | 23 | Primeiro clique já interage *(oculto ao abrir; desabilitado ao abrir; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/auto_zoom` | Configurações de mídia | Caixa de marcar | 11 | 334 | 150 | 16 | 16 | 484 | 218 | 23 | Aproximar ao clicar *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/auto_play` | Configurações de mídia | Caixa de marcar | 11 | 359 | 150 | 16 | 16 | 521 | 218 | 23 | Tocar sozinho *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/media_setting_note` | Configurações de mídia | Rótulo | 31 | 374 | 340 | 15 | 45 | 542 | 493 | 22 | Rótulo «Note: Residents can override this setting» *(desabilitado ao abrir; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/auto_scale` | Configurações de mídia | Caixa de marcar | 11 | 398 | 150 | 16 | 16 | 577 | 218 | 23 | Escalar para a face *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/size_label` | Configurações de mídia | Rótulo | 31 | 419 | 150 | 15 | 45 | 608 | 218 | 22 | Rótulo «Size:» *(largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/width_pixels` | Configurações de mídia | Campo numérico | 99 | 418 | 50 | 16 | 144 | 606 | 72 | 23 | Largura (px) *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/X_label` | Configurações de mídia | Rótulo | 159 | 419 | 50 | 15 | 231 | 608 | 72 | 22 | Rótulo «X» *(largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_general.xml/height_pixels` | Configurações de mídia | Campo numérico | 179 | 418 | 50 | 16 | 260 | 606 | 72 | 23 | Altura (px) *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_security.xml` | Configurações de mídia | Painel | 1 | 43 | 363 | 463 | 1 | 62 | 526 | 671 | Aba de segurança |
| `tab_container/panel_media_settings_security.xml/whitelist_enable` | Configurações de mídia | Caixa de marcar | 11 | 67 | 250 | 16 | 16 | 97 | 362 | 23 | Restringir a uma lista de endereços *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_security.xml/whitelist` | Configurações de mídia | Lista | 31 | 87 | 315 | 200 | 45 | 126 | 457 | 290 | Endereços permitidos *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_security.xml/Parcel_Exp_Color` | Configurações de mídia | Ícone | 31 | 294 | 16 | 16 | 45 | 426 | 23 | 23 | Rótulo preenchido pelo código (Parcel_Exp_Color) *(relativo à borda oposta; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_security.xml/home_url_fails_some_items_in_whitelist` | Configurações de mídia | Rótulo | 31 | 295 | 16 | 15 | 45 | 428 | 23 | 22 | Rótulo «Entries that the home page fails against are marked:» *(largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_security.xml/whitelist_add` | Configurações de mídia | Botão | 31 | 326 | 70 | 20 | 45 | 473 | 102 | 29 | Adicionar endereço *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_security.xml/whitelist_del` | Configurações de mídia | Botão | 276 | 326 | 70 | 20 | 400 | 473 | 102 | 29 | Remover endereço *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_security.xml/home_url_fails_whitelist` | Configurações de mídia | Rótulo | 31 | 381 | 70 | 40 | 45 | 552 | 102 | 58 | Rótulo «Warning: the home page specified in the General tab fails to pass this whitelist. It has been disabled until a valid entry has been added.» *(largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml` | Configurações de mídia | Painel | 1 | 43 | 363 | 463 | 1 | 62 | 526 | 671 | Aba de permissões |
| `tab_container/panel_media_settings_permissions.xml/controls_label` | Configurações de mídia | Rótulo | 11 | 78 | 363 | 15 | 16 | 113 | 526 | 22 | Rótulo «Controls:» *(largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/controls` | Configurações de mídia | Lista suspensa | 101 | 75 | 120 | 18 | 146 | 109 | 174 | 26 | Barra de controles *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/owner_label` | Configurações de mídia | Rótulo | 11 | 128 | 120 | 15 | 16 | 186 | 174 | 22 | Rótulo «Owner» *(desabilitado ao abrir; largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/perms_owner_interact` | Configurações de mídia | Caixa de marcar | 31 | 149 | 250 | 16 | 45 | 216 | 362 | 23 | Dono pode interagir *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/perms_owner_control` | Configurações de mídia | Caixa de marcar | 31 | 171 | 250 | 16 | 45 | 248 | 362 | 23 | Dono vê os controles *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/group_label` | Configurações de mídia | Rótulo | 11 | 208 | 250 | 15 | 16 | 302 | 362 | 22 | Rótulo «Group:» *(desabilitado ao abrir; largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/perms_group_name` | Configurações de mídia | Nome clicável | 61 | 208 | 200 | 20 | 88 | 302 | 290 | 29 | Nome do grupo *(desabilitado ao abrir; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/perms_group_interact` | Configurações de mídia | Caixa de marcar | 31 | 234 | 250 | 16 | 45 | 339 | 362 | 23 | Grupo pode interagir *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/perms_group_control` | Configurações de mídia | Caixa de marcar | 31 | 256 | 250 | 16 | 45 | 371 | 362 | 23 | Grupo vê os controles *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/anyone_label` | Configurações de mídia | Rótulo | 11 | 293 | 250 | 15 | 16 | 425 | 362 | 22 | Rótulo «Anyone» *(desabilitado ao abrir; largura herdada do anterior; leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/perms_anyone_interact` | Configurações de mídia | Caixa de marcar | 31 | 314 | 250 | 16 | 45 | 455 | 362 | 23 | Todos podem interagir *(leiaute antigo (origem embaixo))* |
| `tab_container/panel_media_settings_permissions.xml/perms_anyone_control` | Configurações de mídia | Caixa de marcar | 31 | 336 | 250 | 16 | 45 | 487 | 362 | 23 | Todos veem os controles *(leiaute antigo (origem embaixo))* |

## Pesos do objeto (mais informações) (`floater_object_weights.xml`)

Janela original: **205 × 504** (cabeçalho de 25 incluído; filhos deslocados 25 para baixo). No LifeVerse: **297 × 731**.


### Pesos do objeto (mais informações)

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `selected_text` | Pesos do objeto (mais informações) | Rótulo | 10 | 35 | 180 | 16 | 14 | 51 | 261 | 23 | Rótulo preenchido pelo código (selected_text) |
| `objects` | Pesos do objeto (mais informações) | Rótulo | 10 | 54 | 45 | 16 | 14 | 78 | 65 | 23 | Rótulo preenchido pelo código (objects) |
| `objects_label` | Pesos do objeto (mais informações) | Rótulo | 65 | 54 | 130 | 16 | 94 | 78 | 188 | 23 | Rótulo preenchido pelo código (objects_label) |
| `prims` | Pesos do objeto (mais informações) | Rótulo | 10 | 73 | 45 | 16 | 14 | 106 | 65 | 23 | Rótulo preenchido pelo código (prims) |
| `prims_label` | Pesos do objeto (mais informações) | Rótulo | 65 | 73 | 130 | 16 | 94 | 106 | 188 | 23 | Rótulo preenchido pelo código (prims_label) |
| `selected_text_border` | Pesos do objeto (mais informações) | Divisor | 10 | 104 | 185 | 0 | 14 | 151 | 268 | 0 | Divisor |
| `weights_of_selected_text` | Pesos do objeto (mais informações) | Rótulo | 10 | 114 | 180 | 16 | 14 | 165 | 261 | 23 | Rótulo preenchido pelo código (weights_of_selected_text) |
| `download` | Pesos do objeto (mais informações) | Rótulo | 10 | 133 | 45 | 16 | 14 | 193 | 65 | 23 | Rótulo preenchido pelo código (download) |
| `download_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 133 | 16 | 16 | 49 | 193 | 23 | 23 | Indicador de espera de um valor |
| `download_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 133 | 130 | 16 | 87 | 193 | 188 | 23 | Rótulo preenchido pelo código (download_label) |
| `physics` | Pesos do objeto (mais informações) | Rótulo | 10 | 152 | 45 | 16 | 14 | 220 | 65 | 23 | Rótulo preenchido pelo código (physics) |
| `physics_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 152 | 16 | 16 | 49 | 220 | 23 | 23 | Indicador de espera de um valor |
| `physics_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 152 | 130 | 16 | 87 | 220 | 188 | 23 | Rótulo preenchido pelo código (physics_label) |
| `server` | Pesos do objeto (mais informações) | Rótulo | 10 | 171 | 45 | 16 | 14 | 248 | 65 | 23 | Rótulo preenchido pelo código (server) |
| `server_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 171 | 16 | 16 | 49 | 248 | 23 | 23 | Indicador de espera de um valor |
| `server_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 171 | 130 | 16 | 87 | 248 | 188 | 23 | Rótulo preenchido pelo código (server_label) |
| `display` | Pesos do objeto (mais informações) | Rótulo | 10 | 190 | 45 | 16 | 14 | 276 | 65 | 23 | Rótulo preenchido pelo código (display) |
| `display_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 190 | 16 | 16 | 49 | 276 | 23 | 23 | Indicador de espera de um valor |
| `display_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 190 | 130 | 16 | 87 | 276 | 188 | 23 | Rótulo preenchido pelo código (display_label) |
| `weights_text_border` | Pesos do objeto (mais informações) | Divisor | 10 | 221 | 185 | 0 | 14 | 320 | 268 | 0 | Divisor |
| `land_impacts_text` | Pesos do objeto (mais informações) | Rótulo | 10 | 231 | 180 | 16 | 14 | 335 | 261 | 23 | Rótulo preenchido pelo código (land_impacts_text) |
| `selected` | Pesos do objeto (mais informações) | Rótulo | 10 | 250 | 45 | 16 | 14 | 362 | 65 | 23 | Rótulo preenchido pelo código (selected) |
| `selected_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 250 | 16 | 16 | 49 | 362 | 23 | 23 | Indicador de espera de um valor |
| `selected_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 250 | 130 | 16 | 87 | 362 | 188 | 23 | Rótulo preenchido pelo código (selected_label) |
| `rezzed_on_land` | Pesos do objeto (mais informações) | Rótulo | 10 | 269 | 45 | 16 | 14 | 390 | 65 | 23 | Rótulo preenchido pelo código (rezzed_on_land) |
| `rezzed_on_land_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 269 | 16 | 16 | 49 | 390 | 23 | 23 | Indicador de espera de um valor |
| `rezzed_on_land_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 269 | 130 | 16 | 87 | 390 | 188 | 23 | Rótulo preenchido pelo código (rezzed_on_land_label) |
| `remaining_capacity` | Pesos do objeto (mais informações) | Rótulo | 10 | 288 | 45 | 16 | 14 | 418 | 65 | 23 | Rótulo preenchido pelo código (remaining_capacity) |
| `remaining_capacity_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 288 | 16 | 16 | 49 | 418 | 23 | 23 | Indicador de espera de um valor |
| `remaining_capacity_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 288 | 130 | 16 | 87 | 418 | 188 | 23 | Rótulo preenchido pelo código (remaining_capacity_label) |
| `total_capacity` | Pesos do objeto (mais informações) | Rótulo | 10 | 307 | 45 | 16 | 14 | 445 | 65 | 23 | Rótulo preenchido pelo código (total_capacity) |
| `total_capacity_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 307 | 16 | 16 | 49 | 445 | 23 | 23 | Indicador de espera de um valor |
| `total_capacity_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 307 | 130 | 16 | 87 | 445 | 188 | 23 | Rótulo preenchido pelo código (total_capacity_label) |
| `rendering_text_border` | Pesos do objeto (mais informações) | Divisor | 10 | 338 | 185 | 0 | 14 | 490 | 268 | 0 | Divisor |
| `rendering_info_text` | Pesos do objeto (mais informações) | Rótulo | 10 | 348 | 180 | 16 | 14 | 505 | 261 | 23 | Rótulo preenchido pelo código (rendering_info_text) |
| `lod_level` | Pesos do objeto (mais informações) | Rótulo | 10 | 367 | 45 | 16 | 14 | 532 | 65 | 23 | Rótulo preenchido pelo código (lod_level) |
| `lod_level_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 367 | 16 | 16 | 49 | 532 | 23 | 23 | Indicador de espera de um valor |
| `lod_level_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 367 | 130 | 16 | 87 | 532 | 188 | 23 | Rótulo preenchido pelo código (lod_level_label) |
| `triangles_shown` | Pesos do objeto (mais informações) | Rótulo | 10 | 386 | 45 | 16 | 14 | 560 | 65 | 23 | Rótulo preenchido pelo código (triangles_shown) |
| `triangles_shown_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 386 | 16 | 16 | 49 | 560 | 23 | 23 | Indicador de espera de um valor |
| `triangles_shown_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 386 | 130 | 16 | 87 | 560 | 188 | 23 | Rótulo preenchido pelo código (triangles_shown_label) |
| `pixel_area` | Pesos do objeto (mais informações) | Rótulo | 10 | 405 | 45 | 16 | 14 | 587 | 65 | 23 | Rótulo preenchido pelo código (pixel_area) |
| `pixel_area_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 405 | 16 | 16 | 49 | 587 | 23 | 23 | Indicador de espera de um valor |
| `pixel_area_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 405 | 130 | 16 | 87 | 587 | 188 | 23 | Rótulo preenchido pelo código (pixel_area_label) |
| `lods_text_border` | Pesos do objeto (mais informações) | Divisor | 10 | 436 | 185 | 0 | 14 | 632 | 268 | 0 | Divisor |
| `lod_info_text` | Pesos do objeto (mais informações) | Rótulo | 10 | 446 | 180 | 16 | 14 | 647 | 261 | 23 | Rótulo preenchido pelo código (lod_info_text) |
| `high_lod_tris` | Pesos do objeto (mais informações) | Rótulo | 10 | 465 | 45 | 16 | 14 | 674 | 65 | 23 | Rótulo preenchido pelo código (high_lod_tris) |
| `high_lod_tris_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 465 | 16 | 16 | 49 | 674 | 23 | 23 | Indicador de espera de um valor |
| `high_lod_tris_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 465 | 130 | 16 | 87 | 674 | 188 | 23 | Rótulo preenchido pelo código (high_lod_tris_label) |
| `medium_lod_tris` | Pesos do objeto (mais informações) | Rótulo | 10 | 484 | 45 | 16 | 14 | 702 | 65 | 23 | Rótulo preenchido pelo código (medium_lod_tris) |
| `medium_lod_tris_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 484 | 16 | 16 | 49 | 702 | 23 | 23 | Indicador de espera de um valor |
| `medium_lod_tris_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 484 | 130 | 16 | 87 | 702 | 188 | 23 | Rótulo preenchido pelo código (medium_lod_tris_label) |
| `low_lod_tris` | Pesos do objeto (mais informações) | Rótulo | 10 | 503 | 45 | 16 | 14 | 729 | 65 | 23 | Rótulo preenchido pelo código (low_lod_tris) |
| `low_lod_tris_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 503 | 16 | 16 | 49 | 729 | 23 | 23 | Indicador de espera de um valor |
| `low_lod_tris_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 503 | 130 | 16 | 87 | 729 | 188 | 23 | Rótulo preenchido pelo código (low_lod_tris_label) |
| `lowest_lod_tris` | Pesos do objeto (mais informações) | Rótulo | 10 | 522 | 45 | 16 | 14 | 757 | 65 | 23 | Rótulo preenchido pelo código (lowest_lod_tris) |
| `lowest_lod_tris_loading_indicator` | Pesos do objeto (mais informações) | Indicador de carregamento | 34 | 522 | 16 | 16 | 49 | 757 | 23 | 23 | Indicador de espera de um valor |
| `lowest_lod_tris_label` | Pesos do objeto (mais informações) | Rótulo | 60 | 522 | 130 | 16 | 87 | 757 | 188 | 23 | Rótulo preenchido pelo código (lowest_lod_tris_label) |

## Permissões do conteúdo em lote (`floater_bulk_perms.xml`)

Janela original: **410 × 317** (cabeçalho de 25 incluído; filhos deslocados 7 para baixo). No LifeVerse: **594 × 460**.


### Permissões do conteúdo em lote

| ID original | Aba | Componente | X orig. | Y orig. | L orig. | A orig. | X LV | Y LV | L LV | A LV | Funcionalidade |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|
| `check_animation` | Permissões do conteúdo em lote | Caixa de marcar | 10 | 37 | 16 | 16 | 14 | 54 | 23 | 23 | Incluir animações |
| `icon_animation` | Permissões do conteúdo em lote | Ícone | 31 | 37 | 16 | 16 | 45 | 54 | 23 | 23 | Rótulo preenchido pelo código (icon_animation) *(largura herdada do anterior)* |
| `check_bodypart` | Permissões do conteúdo em lote | Caixa de marcar | 10 | 55 | 16 | 16 | 14 | 80 | 23 | 23 | Incluir partes do corpo |
| `icon_bodypart` | Permissões do conteúdo em lote | Ícone | 28 | 55 | 16 | 16 | 41 | 80 | 23 | 23 | Rótulo preenchido pelo código (icon_bodypart) *(largura herdada do anterior)* |
| `check_clothing` | Permissões do conteúdo em lote | Caixa de marcar | 10 | 76 | 16 | 16 | 14 | 110 | 23 | 23 | Incluir roupas |
| `icon_clothing` | Permissões do conteúdo em lote | Ícone | 28 | 76 | 16 | 16 | 41 | 110 | 23 | 23 | Rótulo preenchido pelo código (icon_clothing) *(largura herdada do anterior)* |
| `check_gesture` | Permissões do conteúdo em lote | Caixa de marcar | 95 | 37 | 16 | 16 | 138 | 54 | 23 | 23 | Incluir gestos |
| `icon_gesture` | Permissões do conteúdo em lote | Ícone | 113 | 37 | 16 | 16 | 164 | 54 | 23 | 23 | Rótulo preenchido pelo código (icon_gesture) *(largura herdada do anterior)* |
| `check_notecard` | Permissões do conteúdo em lote | Caixa de marcar | 95 | 58 | 16 | 16 | 138 | 84 | 23 | 23 | Incluir notas |
| `icon_notecard` | Permissões do conteúdo em lote | Ícone | 115 | 58 | 16 | 16 | 167 | 84 | 23 | 23 | Rótulo preenchido pelo código (icon_notecard) *(largura herdada do anterior)* |
| `check_object` | Permissões do conteúdo em lote | Caixa de marcar | 95 | 79 | 16 | 16 | 138 | 115 | 23 | 23 | Incluir objetos |
| `icon_object` | Permissões do conteúdo em lote | Ícone | 113 | 79 | 16 | 16 | 164 | 115 | 23 | 23 | Rótulo preenchido pelo código (icon_object) *(largura herdada do anterior)* |
| `check_script` | Permissões do conteúdo em lote | Caixa de marcar | 180 | 37 | 16 | 16 | 261 | 54 | 23 | 23 | Incluir scripts |
| `icon_script` | Permissões do conteúdo em lote | Ícone | 200 | 37 | 16 | 16 | 290 | 54 | 23 | 23 | Rótulo preenchido pelo código (icon_script) *(largura herdada do anterior)* |
| `check_sound` | Permissões do conteúdo em lote | Caixa de marcar | 180 | 58 | 16 | 16 | 261 | 84 | 23 | 23 | Incluir sons |
| `icon_sound` | Permissões do conteúdo em lote | Ícone | 200 | 58 | 16 | 16 | 290 | 84 | 23 | 23 | Rótulo preenchido pelo código (icon_sound) *(largura herdada do anterior)* |
| `check_texture` | Permissões do conteúdo em lote | Caixa de marcar | 180 | 79 | 16 | 16 | 261 | 115 | 23 | 23 | Incluir texturas |
| `icon_texture` | Permissões do conteúdo em lote | Ícone | 198 | 79 | 16 | 16 | 287 | 115 | 23 | 23 | Rótulo preenchido pelo código (icon_texture) *(largura herdada do anterior)* |
| `check_settings` | Permissões do conteúdo em lote | Caixa de marcar | 245 | 37 | 16 | 16 | 355 | 54 | 23 | 23 | Incluir ajustes de ambiente |
| `icon_setting` | Permissões do conteúdo em lote | Ícone | 263 | 37 | 16 | 16 | 381 | 54 | 23 | 23 | Rótulo preenchido pelo código (icon_setting) *(largura herdada do anterior)* |
| `check_materials` | Permissões do conteúdo em lote | Caixa de marcar | 245 | 58 | 16 | 16 | 355 | 84 | 23 | 23 | Incluir materiais |
| `icon_materials` | Permissões do conteúdo em lote | Ícone | 263 | 58 | 16 | 16 | 381 | 84 | 23 | 23 | Rótulo preenchido pelo código (icon_materials) *(largura herdada do anterior)* |
| `check_all` | Permissões do conteúdo em lote | Botão | 290 | 33 | 115 | 23 | 420 | 48 | 167 | 33 | Marcar todos |
| `check_none` | Permissões do conteúdo em lote | Botão | 290 | 64 | 115 | 23 | 420 | 93 | 167 | 33 | Desmarcar todos |
| `newperms` | Permissões do conteúdo em lote | Rótulo | 10 | 97 | 250 | 16 | 14 | 141 | 362 | 23 | Rótulo «Adjust Content Permissions To» |
| `GroupLabel` | Permissões do conteúdo em lote | Rótulo | 10 | 117 | 92 | 28 | 14 | 170 | 133 | 41 | Rótulo «Group:» |
| `share_with_group` | Permissões do conteúdo em lote | Caixa de marcar | 10 | 145 | 92 | 16 | 14 | 210 | 133 | 23 | Compartilhar com o grupo |
| `AnyoneLabel` | Permissões do conteúdo em lote | Rótulo | 124 | 117 | 92 | 28 | 180 | 170 | 133 | 41 | Rótulo «Anyone:» |
| `everyone_copy` | Permissões do conteúdo em lote | Caixa de marcar | 124 | 145 | 92 | 16 | 180 | 210 | 133 | 23 | Todos podem copiar |
| `NextOwnerLabel` | Permissões do conteúdo em lote | Rótulo | 275 | 117 | 92 | 28 | 399 | 170 | 133 | 41 | Rótulo «Next owner:» |
| `next_owner_modify` | Permissões do conteúdo em lote | Caixa de marcar | 275 | 145 | 92 | 16 | 399 | 210 | 133 | 23 | Próximo dono modifica |
| `next_owner_copy` | Permissões do conteúdo em lote | Caixa de marcar | 275 | 164 | 92 | 16 | 399 | 238 | 133 | 23 | Próximo dono copia |
| `next_owner_transfer` | Permissões do conteúdo em lote | Caixa de marcar | 275 | 183 | 92 | 16 | 399 | 265 | 133 | 23 | Próximo dono transfere |
| `queue output` | Permissões do conteúdo em lote | Lista | 10 | 182 | 250 | 95 | 14 | 264 | 362 | 138 | Resultado por item *(desabilitado ao abrir)* |
| `ok` | Permissões do conteúdo em lote | Botão | 110 | 287 | 90 | 23 | 160 | 416 | 130 | 33 | Aplicar e fechar |
| `apply` | Permissões do conteúdo em lote | Botão | 205 | 287 | 90 | 23 | 297 | 416 | 130 | 33 | Aplicar |
| `close` | Permissões do conteúdo em lote | Botão | 300 | 287 | 100 | 23 | 435 | 416 | 145 | 33 | Fechar |
