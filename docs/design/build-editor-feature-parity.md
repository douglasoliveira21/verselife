# Editor de construção — matriz de fidelidade funcional (Fase 1)

Referência: visualizador oficial do Second Life, **tag `Second_Life_Release#e960123-26.4`** (commit `e960123230d521e8557a9582100bebcac36a7ca2`, LGPL-2.1). LifeVerse: `main`, commit `a2adc4e`. Posições de cada componente: [build-editor-coordinate-map.md](build-editor-coordinate-map.md).

Nada do código do jogo foi alterado nesta fase, e nada do código do visualizador foi copiado.

## Como ler

**Estado:** **Existente** (há e se comporta de modo equivalente, na arquitetura do LifeVerse) · **Parcial** (há algo, com diferença ou falta) · **Ausente** · **Bloqueado** (depende de algo que não existe e pede decisão) · **Não aplicável**. Nenhum item está como *Implementado* ou *Validado*: esta fase não implementou nada, e "Existente" **não** quer dizer equivalência comprovada lado a lado.

**Evidência:** `A` = o autoteste do mundo passa pela função (rodado em 28/11/2026 no jogo gerado, 374 verificações); `X: Nome` = há teste xUnit/EditMode com esse nome (os EditMode passaram em 28/11, 254/254; **os da API não foram rodados nesta fase**); `C` = só leitura do código; `—` = nada.

**Siglas dos arquivos originais:** FT `llfloatertools.cpp` + `floater_tools.xml` · PO `llpanelobject.cpp` · PP `llpanelpermissions.cpp` · PV `llpanelvolume.cpp` · PF `llpanelface.cpp` + `panel_tools_texture.xml` · PC `llpanelcontents.cpp`, `llpanelobjectinventory.cpp` · SM `llselectmgr.cpp` · TL `lltool*.cpp` · MN `llmanip*.cpp` · MV `menu_viewer.xml` · MO `menu_object.xml`.

> **Atualização (28/11/2026):** os estados e a coluna "Existe no LifeVerse" abaixo são o retrato da Fase 1. A Fase 3 trocou a interface (ver [a auditoria](../unity-migration/GODOT_PARITY_AUDIT.md)); a matriz será refeita na validação final. Única correção feita: a linha "Último dono", que estava errada.

## Resumo

118 funcionalidades comparadas: **29 existentes, 49 parciais, 32 ausentes, 3 bloqueadas, 5 não aplicáveis**.

## O que o LifeVerse já tem para reutilizar

| Parte | Onde | Observação |
|---|---|---|
| Janela de construção atual (12 abas) | `UI/BuildWindows.cs`, `BuildTabs.cs`, `BuildResourcesTab.cs`, `BuildAnimationTab.cs` | Toda em código (UI Toolkit); a nova interface troca a apresentação e mantém os comandos |
| Comandos, seleção, travas, desfazer, gizmo, imã, arrasto ao vivo | `UI/BuildEditor.cs` | Separado da janela: dá para reaproveitar quase inteiro. Gizmo desenhado em 2D por cima da cena |
| Pincéis de terreno | `UI/TerrainEditorTool.cs`, `shared/Terrain` | Terreno próprio (mapa de alturas 128 × 128, células de 2 m); o servidor de ilha manda |
| Geometria paramétrica | `shared/Building/PrimMesh.cs`, `RoundMesh.cs` | Compartilhada; faces com ids estáveis |
| Regras de física, recursos, vínculo, alinhar, encaixe, pacotes | `shared/Building/` | Algumas sem uso no cliente Unity (flexível) |
| Autoridade e persistência | `backend/Modules/Worlds/BuildOperations*.cs`, `BuildRules.cs`, `BuildProperties.cs` | Operações `create`, `update`, `delete`, `return`, `structure`, `protect`, `properties`, `buy`, `rez`; revisão por objeto; JSON com limites de tamanho |
| Dados do objeto | `WorldObjectState` | Nome, descrição, criador, dono, grupo, pai, pivô, direitos, material/geometria/física/conteúdo/permissões em JSON |
| Visual | `UI/Resources/LifeVerse/UI/Theme.uss` (tema Varanda, de dia e de noite), `UiKit`, `UiParts`, `Icon`, `FloatingWindow`, `SectionWindow`, `RadialMenu` | Fraunces nos títulos, Nunito Sans no resto, corpo 14 px (13 nas notas); variáveis `--lv-*`; botões com contorno de foco; "reduzir movimento"; escala automática de 0,85 para cima sobre 1080p |

## Decisões para o dono, antes ou durante os protótipos

1. **Escala da janela:** 428 × 954 (proporção original) no lugar de 744 × 954 — detalhes no [mapa](build-editor-coordinate-map.md).
2. **Eixos:** mostrar Z como altura, como no Second Life (só na tela; os dados não mudam).
3. **Vínculo:** selecionar e mover o conjunto inteiro, e o último selecionado como raiz (muda o comportamento atual).
4. **Teclas:** no modo construção, as do Second Life passam na frente dos gestos.
5. **Campos novos que pedem mudança de banco ou de protocolo** (só com autorização): "Exibir na busca", aplicar terreno a uma área; formas novas (tetraedro, semicilindro, semicone), formato do oco, gravidade e densidade pedem mudança nas regras da API e do servidor de ilha, sem migração, a conferir.
6. **O que o pipeline não faz como o original:** brilho/polimento clássico, espelhos e navegador de páginas nas faces: aceitar a diferença ou orçar.

## O que mais pesa (antes da tabela)

1. **Conjuntos vinculados não se movem juntos.** O LifeVerse guarda grupo e pai, com transformações absolutas: clicar numa peça seleciona só ela, e mover o "pai" não leva os filhos. No Second Life o clique seleciona o conjunto todo e "Editar partes vinculadas" desce a uma peça. É a maior diferença estrutural.
2. **A interface tem outra organização:** 12 abas em fila, controles deslizantes e campos de identificador, contra 5 modos + 5 abas com campos numéricos em duas colunas.
3. **Gizmos simples:** três linhas por modo; faltam planos, anéis, alças de canto, esticar de um lado só e o destaque ao passar o mouse.
4. **Faces:** uma de cada vez, sem destaque, só cor e textura; sem rotação de textura, mapeamento planar ou repetições por metro.
5. **Ação ao clicar** é salva e conferida no servidor, mas o clique do jogo abre um menu em vez de executá-la.
6. **Objetos flexíveis:** a regra existe em `shared`, o cliente Unity não os simula nem os edita.
7. **Convenção de eixos:** o jogo mostra Y como altura; no Second Life a altura é Z.

## Estrutura da janela e modos

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Cinco modos (Foco, Mover, Editar, Criar, Terreno) | FT, topo | Cinco botões; cada um mostra só as suas opções (`updatePopup`) | Não há modos: janela "Construir" e janela "Terreno" separadas | Janela única com os cinco modos e opções condicionais | Câmera, terreno, editor | Ausente | C |
| Linha de instrução da ferramenta | FT `status` | Texto muda com a ferramenta | Linha de avisos no rodapé | Instrução por ferramenta no alto | — | Parcial | C |
| Cinco abas de propriedades | FT `Object Info Tabs` | Geral, Objeto, Recursos, Textura, Conteúdo; as três últimas desabilitadas se a seleção não for toda de primitivas | 12 abas | Reorganizar nas cinco; guardar extras (ver fim) | — | Parcial | C |
| Janela arrastar, redimensionar, minimizar, guardar posição | `floater` (`save_rect`) | Posição lembrada; não redimensiona | `FloatingWindow` faz tudo isso e mantém na tela | Usar a existente; rever o limite de altura (ver mapa) | `FloatingWindow` | Existente | A; X: UiTests |
| Abrir construção | MV `Ctrl+B`, `Ctrl+1..5` | Abre e escolhe o modo | Tecla `B` e menu; `T` abre o terreno | Atalhos adicionais, sem tirar os do jogador | `KeyBindings` | Parcial | C |
| Ao fechar | FT `onClose` | Solta a seleção, sai da edição de partes, volta a câmera | Solta a seleção e as travas | Restaurar a câmera | Câmera | Parcial | C |

## Foco

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Opções Zoom / Órbita / Panorâmica | FT `focus_radio_group` | Escolhidas por botão ou por modificador (`Ctrl`, `Ctrl+Shift`) | Não há na janela | Opções que ativam os gestos da câmera | `FollowCamera` | Ausente | C |
| Controle deslizante de zoom | FT `slider zoom` | Fração de zoom da câmera | Não | Ligar à distância da câmera | `FollowCamera` | Ausente | C |
| Gestos Alt, Alt+Ctrl, Alt+Ctrl+Shift | TL `lltoolfocus` | Aproximar/orbitar, orbitar, deslizar em torno do ponto | `CameraControl.Classify` usa as mesmas três combinações; opção simplificada com Ctrl | Nada, salvo o conflito com Alt na construção (abaixo) | `shared/Camera` | Existente | C; X: UiTests |
| Foco num objeto ou ponto | TL; MV `H`, `Shift+H` | Centraliza a câmera na seleção | "Dar zoom", "Olhar para cá", duplo clique | Atalhos para a seleção | `FollowCamera` | Parcial | C |
| Câmera nunca move objeto | TL | Modo separado | Gestos não mexem em objetos | Garantir no modo Foco | — | Existente | C |

## Mover

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Arrastar o próprio objeto | TL `lltoolgrab` | Segurar o corpo e arrastar no plano | Só pelas setas do gizmo | Arrasto livre com prévia local | Travas, envio contínuo | Ausente | C |
| Vertical e giro por modificador | TL (`Ctrl`, `Ctrl+Shift`) | Levanta; gira | Não no arrasto (há Page Up/Down) | Modificadores no arrasto | — | Ausente | C |
| Mover sobre superfícies | TL | Segue o que está sob o mouse | Não | Raio contra terreno e objetos | Colisores dos objetos (hoje não há) | Ausente | C |
| Passo pelo teclado | — (setas movem o avatar) | — | Setas e Page Up/Down movem a seleção pela grade | Manter | — | Não aplicável (extra) | A |
| Permissão para mover o alheio | PP "Anyone: Move" | Quem não é dono move se permitido | `anyoneMove` conferido no servidor | — | API | Existente | X: BuildPropertiesTests |

## Editar: posição, rotação, escala

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Troca de modo por opção e por `Ctrl` / `Ctrl+Shift` | FT; TL `getOverrideTool` | Segurar a tecla troca o manipulador | Três botões | Opções + troca temporária por tecla | — | Parcial | A |
| Setas X, Y, Z | MN translate | Arrastar no eixo, com régua de encaixe | Linhas com bola, encaixe por passo | Aparência, destaque, régua e números | Desenho na cena | Parcial | A |
| Planos de movimento | MN translate | Quadrados movem em dois eixos | Não | Alças de plano | `GizmoMath` | Ausente | C |
| Anéis e giro livre | MN rotate | Três anéis, esfera livre, indicador de ângulo | Arrasto numa linha por eixo, passo de 15° | Anéis, giro livre, indicador | — | Parcial | A |
| Passo angular | `RotationStep` (1° padrão) | Encaixe pela régua | `RotateStep` 15° (Configurações) | Régua angular | — | Parcial | C |
| Alças de escala nas faces e cantos | MN scale | Faces esticam um eixo; cantos escalam tudo | Três linhas, um eixo, em torno do centro | Alças nas faces e cantos; uniforme | — | Parcial | A |
| Esticar dos dois lados | FT `checkbox uniform` (`ScaleUniform`, desligado) | Desligado: só o lado puxado cresce | Sempre pelo centro | Esticar de um lado (move o centro) | — | Ausente | C |
| Esticar a textura junto | FT (`ScaleStretchTextures`, ligado) | Desligado: repetições acompanham o tamanho | A textura sempre estica | Ajustar repetições ao escalar | Material por face | Ausente | C |
| Campos numéricos ao vivo | PO posição/tamanho/rotação | Atualizam durante o arrasto; aceitam contas | 9 campos de texto + "Aplicar"; atualizam só após o servidor; uma seleção por vez | Campos com incremento, confirmação e valores mistos | UI | Parcial | A |
| Limites | `floater_tools.xml` | Tamanho 0,01–64; X/Y −256–512, Z −32–4096 | Tamanho 0,01–64 (cliente e API); posição ±5000 no cliente | Limitar pela ilha | API | Existente | X: BuildValidationTests |
| Seleção por clique e várias | SM; TL select | Clique, `Shift` soma; retângulo de seleção; distância máx. 128 m | Clique, `Shift`/`Ctrl` somam; 300 m | Retângulo; opções "só meus"/"só móveis" | — | Parcial | A |
| Pivô | — (centro ou raiz) | Gira em torno da seleção | Campo de pivô salvo por objeto (extra); gizmo não o usa | Definir uso | — | Parcial | C |
| Imã de encaixe em cantos e faces | não há igual | — | `SnapMath` (extra) | Manter como opção | — | Não aplicável (extra) | A; X: SnapAndAreaTests |

## Selecionar face

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Escolher face clicando | TL `lltoolface` | Clique escolhe; `Shift` soma/tira | "Escolher a face clicando" (objeto já selecionado), uma face | Modo próprio, várias faces, várias peças | `PrimMesh` (faces estáveis) | Parcial | C; X: PrimMeshTests |
| Destaque das faces | `llface` (retícula) | Marca visível | Não (só mensagem) | Contorno/retícula | Malha por face | Ausente | C |
| Próxima/anterior face ou peça | MV `Ctrl+.` `Ctrl+,` | Navega; com `Shift` inclui | Não | Atalhos | — | Ausente | C |
| Valores mistos | PF | Campos marcam "diferentes" | Mostra o primeiro objeto | Estado misto em todos os controles | UI | Ausente | C |
| Faces de modelos importados | PF | Cada material é uma face | Não | Submalhas como faces | Importação glTF | Ausente | C |

## Grade e encaixe

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Ligar encaixe | FT; MV `G` | Padrão ligado | Opção nas Configurações | Caixa na janela | — | Parcial | A |
| Referencial Mundo / Local / Referência | FT `combobox grid mode`; MV `Shift+G` | Três referenciais; anexos têm o seu | "Eixos" mundo/local, só para mover | Os três, em todos os manipuladores | — | Parcial | C |
| Passo, extensão, subdivisões, opacidade | `floater_build_options.xml` | 0,01–5 m; extensão 1–50; subunidades | Só o passo (0–10 m) | Janela de grade | — | Parcial | A |
| Grade desenhada e réguas | MN | Linhas e marcas no mundo | Não | Desenho na cena | Pipeline HDRP | Ausente | C |
| Encaixar o objeto na grade | MV `Shift+X` | Alinha X/Y | Não | Comando | — | Ausente | C |

## Criar

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Quinze formas em três linhas | FT `ToolCube`…`ToolGrass` | Cubo, prisma, pirâmide, tetraedro, cilindro, semicilindro, cone, semicone, esfera, hemisfério, toro, tubo, anel, árvore, grama | 12 botões com nome | Botões com ícone na ordem; **faltam tetraedro, semicilindro e semicone** | `PrimMesh`, lista de formas da API | Parcial | A; X: PrimMeshTests |
| Clicar no mundo para criar | TL `lltoolplacer` | Nasce onde se clica, apoiado na superfície | Nasce 4 m à frente da câmera | Criação por clique com prévia | Raio contra superfícies | Ausente | A |
| Manter a ferramenta | FT `sticky` | Senão volta a Editar | Sempre mantém | Opção | — | Parcial | C |
| Copiar a seleção ao criar; centralizar; girar | FT | Clique cria cópia alinhada | "Copiar" ao lado (`Ctrl+D`) | Modo de cópia por clique | `TransformMath` | Parcial | A |
| Prévia fantasma e cancelar | (extra pedido) | Não há fantasma no original | Não | Prévia | — | Ausente | — |
| Permissão e limites ao criar | servidor | Terreno, cota | Servidor confere terreno, cota e custo | — | API | Existente | X: BuildOperationsTests, LandBuildingTests |
| Importar modelo; construções do inventário | (menus) | Envio de malha; rez | Importar GLB/glTF/DAE; colocar construção | Manter, acessível no modo Criar | — | Existente (extra) | A; X: BuildPackageTests |

## Terreno

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Selecionar terreno | FT; TL `lltoolselectland` | Arrastar marca um retângulo; mostra área e preço | Terrenos em janelas próprias | Seleção no mundo ligada ao terreno | `LandManagementWindow` | Ausente | C |
| Nivelar, elevar, rebaixar, suavizar, rugosidade, restaurar | FT `land_radio_group` | Seis pincéis | Os seis, mais pintar, nivelar numa altura e erodir | Ordem e nomes do original; extras à parte | Servidor de ilha | Existente | A; X: TerrainBrushTests, TerrainEditTests |
| Tamanho e força | FT sliders (força logarítmica) | Pincel redondo | Tamanho até 24 m, força %, maciez, redondo/quadrado | Mesma posição | — | Existente | A |
| Aplicar à seleção | FT `button apply to selection` | Aplica a área marcada de uma vez | Não | Operação por área | Mensagem nova no servidor de ilha | Bloqueado (protocolo) | — |
| Sobre, dividir, unir, comprar, abandonar; mostrar donos | `land info panel` | Habilitados por posse e grupo | Há em "Sobre este terreno" e "Gerenciar terrenos" (dividir, expandir, unir, reivindicar) | Reunir os botões no modo Terreno; donos no chão | API de terrenos | Parcial | A |
| Limites de altura e propriedade; sincronização; desfazer | servidor | — | −8 a 48 m; só no próprio terreno; desfazer no servidor | — | — | Existente | A; X: TerrainEditTests |

## Informações da seleção

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Nada selecionado / contagem | FT `selection_*` | Objetos e **impacto no terreno** | Nome e forma, ou "N objetos" | Contagem de objetos e primitivas | Linksets | Parcial | A |
| Faces selecionadas | FT `selection_faces` | Números das faces | Mensagem ao escolher | Mostrar na janela | — | Parcial | C |
| Capacidade restante | FT `status_remaining_capacity` | Do terreno | Aba Limites: objetos e custo da ilha e do terreno (dados reais) | Mostrar no alto; custo = "impacto" do LifeVerse | `BuildCost` | Existente | A; X: BuildRulesTests |
| Pesos de render, física, servidor, download | `floater_object_weights.xml` | Janela "mais informações" | Só custo | Triângulos são calculáveis; **peso de física e de servidor não existem** — não mostrar | Métricas novas | Parcial | C |

## Aba Geral

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Nome e descrição | PP | 63/127 caracteres; salva ao sair do campo | Campos + botão; 160/4000 | Salvar ao confirmar o campo | — | Existente | A |
| Criador, dono (com foto e perfil) | PP | Clicáveis | Texto com os nomes | Foto e abrir perfil | Perfis | Parcial | A |
| Último dono | PP (junto ao dono de grupo) | Mostrado quando o dono é grupo | A API guarda e envia (`LastOwnerUserId`); o cliente não mostra | Mostrar na aba Geral | — | Parcial | C |
| Grupo: escolher, compartilhar, transferir | PP | Seletor; "Share"; "Deed" com confirmação | Campo de identificador; "Dar ao grupo"; `groupShare` só no servidor | Seletor de grupo, caixa de compartilhar | Grupos | Parcial | X: BuildPropertiesTests |
| Ação ao clicar (8) | PP `clickaction` | O clique executa; "Comprar" exige venda | 8 opções salvas e validadas; **o clique abre um menu** | Executar a ação no cliente | Mídia para "abrir/tocar mídia" | Parcial | C; X: BuildPropertiesTests |
| À venda: original, cópia, conteúdo; preço | PP | Habilitado conforme direitos | Há, com compra atômica e idempotente | Disposição e valores mistos | Economia | Existente | A; X: ObjectSalesTests, BuildPayPriceTests |
| Exibir na busca | PP `search_check` | Marca o objeto | Não | Campo e índice | API, banco | Bloqueado (migração) | — |
| Permissões: todos (mover, copiar), próximo dono | PP | Regras de dependência entre caixas | Há | Desabilitar conforme regras; valores mistos | — | Existente | X: ObjectRightsTests |
| Máscaras avançadas B/O/G/E/N/F | PP (`DebugPermissions`) | Texto de depuração | Não | Opcional | — | Ausente | — |
| Atributos de navegação (pathfinding) | PP | Do simulador | Não há navmesh | — | — | Não aplicável | — |

## Aba Objeto

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Bloqueado | PO | Só o dono; impede mover | "Proteger contra edição" (aba Geral) | Mover para cá | — | Existente | A |
| Físico, temporário, fantasma | PO | Fantasma = sem colisão; flexível força fantasma | "Físico", "Temporário", "Colide", "Gatilho" na aba Física | Fantasma = não colidir; regras do original | Servidor de ilha | Existente | X: IslandPhysicsTests, TemporaryObjectTests |
| Posição, tamanho, rotação em colunas; copiar/colar | PO | Menus de colar por grupo | Campos em lista | Duas colunas, setas, colar | Área de transferência | Parcial | A |
| Eixos Z para cima | PO | X leste, Y norte, Z altura | Y é altura | **Decisão:** mostrar na convenção do Second Life (X = x, Y = −z, Z = y) e converter a rotação junto | Aprovação | Ausente | — |
| Tipo de forma | PO `comboBaseType` | Troca entre 7 (+esculpida) | Não se troca a forma de um objeto | Trocar mantendo materiais | A API já aceita outra forma num `update` | Ausente | C |
| Corte de trajetória, corte de perfil | PO | Passos 0,025/0,02; mínimo 0,02 | "Corte" e "Caminho" | Nomes por forma; conferir qual é qual | `PrimMesh` | Parcial | X: PrimMeshTests |
| Oco e formato do oco | PO | Até 95% (70% com oco quadrado); 4 formatos | Oco; **sem formato** | Formato do oco na geometria | `PrimMesh`, validação da API | Parcial | X: PrimMeshTests |
| Torção início/fim | PO | ±180 (±360 em toro/tubo/anel/esfera) | Só o fim na tela (`twistBegin` existe no gerador); ±180 | Campo de início; faixa por forma | Idem | Parcial | C |
| Afunilamento / tamanho do buraco | PO | Muda de nome e faixa por forma | "Afunilar"; `holeX/Y` sem tela | Troca por forma | — | Parcial | C |
| Cisalhamento, skew, revoluções, deslocamento do raio, taper do toro | PO | Conforme a forma; revoluções 1–4 | Cisalhar, skew, revoluções 1–3; raio sem tela; taper de toro não separado | Completar e esconder o que não vale | `PrimMesh` | Parcial | C |
| Côncavo (dimple) / fatia (slice) | PO | Nome por forma | A API valida `slice*`; o gerador não os usa | Gerar | `PrimMesh` | Ausente | C |
| Esconder campos por forma | PO `getState` | Ver "Estados condicionais" | Todos sempre visíveis | Regras por forma | — | Ausente | C |
| Esculpidas (sculpt) | PO | Textura define a forma | Malhas importadas fazem o papel | Mostrar "Modelo 3D" | — | Não aplicável | — |
| Material físico (pedra … borracha) | PV `material` (aba Recursos) | 7 materiais | Os mesmos 7, com atrito e quique | **No visualizador atual fica em Recursos**, não em Objeto | — | Existente | X: PhysicsPresetsPackageTests |
| Geometria igual em cliente e servidor; materiais por face preservados | — | — | `PrimMesh` compartilhado; faces com ids estáveis | Testar ao trocar parâmetros | — | Existente | X: PrimMeshTests |

## Aba Recursos

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Flexível (suavidade, gravidade, arrasto, vento, tensão, força) | PV | Só em formas compatíveis; vira fantasma | `FlexSettings` em `shared`; sem tela nem simulação no Unity | Simulação e campos | Cliente Unity | Ausente | C; X: ObjectResourcesTests |
| Luz: cor, intensidade, raio, atenuação | PV | Raio 0–20, atenuação 0–2 | Cor, força, alcance 0,5–30, queda, sombra, face, cone | Faixas e disposição | HDRP | Existente | A; X: ObjectResourcesTests |
| Projetor: textura, FOV, foco, ambiência | PV | Textura vira projetor | Há | Disposição | — | Existente | A |
| Sonda de reflexo | PV | Esfera/caixa, ambiência, corte, estática/dinâmica/espelho | "Reflexo" (intensidade, alcance) | Formato e atualização; **espelho não** | HDRP | Parcial | C |
| Forma física | PV | Nenhuma, primitiva, convexa | Nenhuma, caixa, convexa, triângulos, esfera, cilindro | Nomes do original, mantendo extras | — | Existente | X: IslandPhysicsTests |
| Gravidade, atrito, densidade, restituição | PV | Faixas −1–28, 0–255, 1–22587, 0–1 | Massa, atrito 0–1, quique 0–1 | Gravidade e densidade | Física no servidor de ilha, validação | Parcial | X: IslandPhysicsTests |
| Malha animada | PV | Liga o esqueleto da malha | "Animação do modelo" (clipe, pausa, velocidade) | Colocar aqui | — | Existente | A |

## Aba Textura

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Cor com seletor RGB/HSL/hex, conta-gotas | `floater_color_picker.xml` | Janela própria, aplicar/cancelar | Hex + amostras | Seletor completo | — | Parcial | A |
| Transparência, brilho, luz total, esconder água | PF | Transparência 0–100% | Opacidade, cor e força do brilho, luz total, dois lados; sem "esconder água" | Disposição | Oceano | Parcial | A |
| Modo Clássico / PBR / Mídia | PF `matmedia` | Troca os controles | Tudo numa lista | Seletor e condições | — | Parcial | C |
| Difuso, relevo, especular | PF | Três canais, cada um com repetição, rotação, deslocamento | Difuso e relevo, uma repetição e deslocamento comuns; especular não desenhado (`glossiness` é ignorado) | Por canal; especular | Shader HDRP | Parcial | C |
| Brilho, polimento, ambiente, cor especular, padrões de relevo | PF | Lista de padrões | Não | Mapear para HDRP ou declarar diferença | Pipeline | Bloqueado (HDRP é PBR) | — |
| Transparência: nenhuma, mistura, máscara, emissiva | PF `alphamode` | Limite 0–255 | `alphaMode` lido; sem tela; sem limite de máscara | Controles e recorte | Material | Parcial | C |
| Material PBR (glTF) como item; substituições | PF; editor de material | Escolher, editar, salvar no inventário | Mapas soltos (cor, normal, ORM, emissivo), aspereza e metal | Asset de material | Inventário, API | Parcial | C |
| Mapeamento padrão/planar; repetições por metro; rotação; espelhar | PF | — | Repetição 0,1–16 e deslocamento | Rotação, planar, por metro, negativo | Malha e material | Ausente | C |
| Alinhar faces planares / entre faces; travar repetições | PF | — | Não | — | UV | Ausente | — |
| Copiar e colar propriedades | menus `menu_copy_paste_*` | Com verificação de faces | Não | Área de transferência própria | — | Ausente | — |
| Seletor de textura: busca, miniaturas, aplicar/cancelar, padrão, branco, nenhuma, conta-gotas | `floater_texture_ctrl.xml` | Prévia e "aplicar na hora" | Padrões do sistema com miniatura + lista do inventário; sem busca nem cancelar | Janela completa | Miniaturas | Parcial | A |
| Mídia na face: URL, tocar, repetir, escala, tamanho, controles, permissões, lista de endereços | `panel_media_settings_*` | Navegador embutido | Imagem ou vídeo por https, tocar/pausar | Demais ajustes; **navegador de páginas não existe** | Decisão de segurança | Parcial | X: ObjectMediaTests |

## Aba Conteúdo

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Árvore do inventário do objeto, com filtro | PC | Arrastar, abrir, renomear, copiar, excluir, propriedades | Lista por pastas; pôr e tirar; até 64 itens | Árvore com ações e arrastar | Inventário; renomear no contrato | Parcial | A; X: InventoryBuildTests |
| Novo script | PC (menu LSL/Lua) | Cria dentro do objeto | Editor de scripts Lua do objeto | Botão na posição | `LuaEditorWindow` | Existente | A; X: ScriptEditorApiTests |
| Nova nota | PC `new notecard` | Cria nota | Não | Tipo "nota" | Assets | Ausente | — |
| Permissões em lote | `floater_bulk_perms.xml` | Aplica por tipo | Texto explicando direitos | Janela em lote | API | Ausente | — |
| Editor externo | PC `publish` | Abre IDE | Não | — | — | Não aplicável | — |
| Editar, salvar, validar, executar, parar, reiniciar, erros, depurar; sandbox no servidor | scripts | LSL/Lua no servidor | Há, em Lua, com limites no servidor de ilha | Reiniciar todos; habilitar em lote | `scripting/` | Existente | A; X: ScriptHostTests, ScriptRuntimeApiTests |

## Vinculação

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Vincular / desvincular (`Ctrl+L`, `Ctrl+Shift+L`) | FT, SM | Exige 2+ e permissão de modificar; até 256 partes | Botões; servidor valida (256, 16 níveis) | Atalhos | — | Existente | A; X: LinksetTests, LinksetChaosTests |
| Raiz = **último** selecionado | SM `addNode` insere no começo | Último clicado vira raiz | **Primeiro** selecionado é o pai | Inverter a escolha | — | Parcial | C |
| Conjunto selecionado e movido como um | SM | Clique pega o conjunto; transformações relativas à raiz | Só a peça; transformações absolutas | Selecionar e transformar o conjunto (servidor pode continuar absoluto) | Editor; envio em lote atômico | Ausente | C |
| Editar partes vinculadas | FT checkbox | Seleciona filhos; destaca raiz e filhos | Não faz sentido hoje | Depois do item acima | — | Ausente | C |

## Desfazer, multiplayer e persistência

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Desfazer/refazer próprios (`Ctrl+Z`, `Ctrl+Y`) | MV | Por objeto, no servidor | 50 passos no cliente: criar, apagar, editar | Registrar vínculo e propriedades; agrupar; refazer apagado mantém identidade | API | Parcial | A |
| Venda, pagamento e transferência fora do desfazer | — | — | Não entram | Manter | — | Existente | C |
| Autoridade do servidor, versões, conflito, limite de envio | — | — | Revisões, travas "em edição por", 20 comandos/s | — | — | Existente | A; X: BuildOperationsTests, wire |
| Prévia local e interpolação do arrasto | — | — | `live` a 10 Hz sem salvar | Estender ao arrasto livre | — | Existente | A |
| Persistência e reconexão | — | — | PostgreSQL pela API | Persistir campos novos | Migrações (pedir aprovação) | Existente | X: IslandObjectsTests, ObjectStorageTests |

## Menu do objeto e atalhos

| Funcionalidade | Localização original | Comportamento original | Existe no LifeVerse | Implementação necessária | Dependências | Estado | Evidência |
|---|---|---|---|---|---|---|---|
| Tocar, sentar, zoom, editar, comprar, pagar, abrir | MO | Habilitados por permissão | Menu radial e menu do objeto | Mostrar "Editar" só a quem pode | Permissões no cliente | Parcial | A |
| Pegar, pegar cópia, devolver, excluir, duplicar, copiar/colar | MO | Conforme dono | "Guardar e retirar", "Devolver" e "Apagar" só dentro da janela | Levar ao menu | `BuildPackages` | Parcial | C |
| Foco na seleção, zoom (`H`, `Shift+H`) | MV | — | Não | Atalhos só no modo construção | Conflito: `H` = dançar | Ausente | C |

### Conflitos de teclas

`KeyBindings.Pressed` não olha os modificadores: **`Ctrl+L` também faria "cumprimentar"**.

| Second Life | Uso | No LifeVerse hoje | Proposta (a aprovar) |
|---|---|---|---|
| `G` | encaixar na grade | acenar (`G`) | no modo construção, `G` é da construção |
| `H`, `Shift+H` | focar / aproximar | dançar | idem |
| `Shift+X` | encaixar X/Y | sentar (`X`) | idem |
| `Ctrl+L`, `Ctrl+Shift+L` | vincular/desvincular | `L` cumprimentar | ignorar gestos com `Ctrl` |
| `Ctrl+D` | duplicar | já é duplicar | manter |
| `Ctrl+1…5` | modos | livre | adotar |
| `Ctrl+Shift+B` | grade | `B` abre construção | ignorar `B` com modificadores |
| `Alt` | câmera | também solta a grade no arrasto | câmera tem prioridade; soltar a grade fora das réguas, como no original |

## Estados condicionais do visualizador (extraídos do C++)

| Estado | O que muda no original | Origem |
|---|---|---|
| Nada selecionado | "Nothing selected"; abas visíveis e desabilitadas; contagem escondida | `LLFloaterTools::updatePopup` |
| Seleção não toda de primitivas | Abas Recursos, Textura e Conteúdo desabilitadas; se estava nelas, volta à primeira | `LLFloaterTools::refresh` |
| Modo Terreno | Esconde as abas e o separador; mostra o painel do terreno | `updatePopup` |
| Modo Foco / Mover | Só o grupo de opções do modo; o deslizante de zoom só habilita em "Zoom"; `Ctrl`/`Ctrl+Shift` marcam a opção | `updatePopup` |
| Modo Editar | Mostra vincular, desvincular, partes vinculadas, grade, esticar; "Vincular" e "Desvincular" habilitam por regra própria | `updatePopup`, `enableLinkObjects`, `enableUnlinkObjects` |
| Modo Editar, lista de referenciais | Mundo/Local/Referência; HUD: Tela/Local; anexo: Anexo/Local/Referência | `updatePopup` |
| Modo Criar | "Centralizar" e "Girar" cópia só habilitam com "Copiar seleção"; com ela marcada nenhum botão de forma fica aceso | `updatePopup` |
| Terreno com pincel × seleção | "Aplicar" só com área marcada e fora de "Selecionar" | `updatePopup` |
| Uma peça com ferramenta de face | Mostra os números das faces | `refresh` |
| Sem permissão de modificar | Geometria, recursos e nome desabilitados; mensagem sobre o que se pode fazer | `LLPanelObject::getState`, `LLPanelVolume::getState`, `LLPanelPermissions::refresh` |
| Pode mover mas não modificar | Posição e rotação habilitadas, tamanho não | `LLPanelObject::getState` |
| Várias peças | Nome e descrição desabilitados; geometria só com uma peça; Recursos mostra "selecione uma só primitiva" | idem |
| Bloqueado | "Bloqueado" só habilita para o próprio dono; objeto não se move | `getState` |
| Flexível | "Fantasma" desabilitado | `getState` |
| Cubo, cilindro, prisma | Corte, oco, torção ±180, afunilamento, cisalhamento, fatia | `getState`, escolha por forma |
| Esfera | Sem tamanho do topo nem cisalhamento; "côncavo"; torção ±360 | idem |
| Toro, tubo, anel | "Tamanho do buraco" (0,05–1 × 0,05–0,5), skew, taper, raio, revoluções, torção ±360 | idem |
| Esculpida / malha | Esconde corte, oco, torção etc.; mostra a textura de escultura; malha desabilita o tipo | idem |
| Oco quadrado em cilindro, prisma, toro, anel, esfera | Oco limitado a 70% | idem |
| Luz desligada | Parâmetros da luz desabilitados | `LLPanelVolume::getState` |
| Sonda de reflexo | Desabilitada em malha; "dinâmica" ou lista de atualização conforme espelhos | idem |
| Dono diferente / grupo dono | Preço desabilitado se donos diferentes; ícone de grupo no lugar do de pessoa | `LLPanelPermissions::refresh` |
| Venda impossível | Sem direito de transferir (ou copiar, ao vender cópia) a venda é corrigida/desabilitada; anexos não vendem | idem |
| Textura: Clássico / PBR / Mídia | Cada modo mostra seus grupos; canais difuso/relevo/especular trocam textura e campos | `LLPanelFace::updateVisibility`, `updateVisibilityGLTF` |
| Conteúdo | "Novo script" exige permissão e uma peça ou um conjunto; nota exige suporte da região | `LLPanelContents::getState` |
| Terreno selecionado | Comprar, abandonar, dividir e unir conforme dono, grupo e área | `LLPanelLandInfo::refresh` |

## Extras do LifeVerse a preservar (proposta de lugar)

| Extra | Lugar proposto |
|---|---|
| Alinhar e distribuir | botão nas opções do modo Editar |
| Limites e auditoria | janela "mais informações" (onde o original abre os pesos) |
| Importar modelo 3D, construções do inventário | opções do modo Criar |
| Guardar construção / pegar | menu do objeto |
| Objetos apagados | menu da janela |
| Tutorial | botão no cabeçalho |
| Imã de encaixe, texturas do sistema | grade e seletor de textura |
| Animação do modelo | aba Recursos (lugar da malha animada) |
