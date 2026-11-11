# Novo editor de construção (namespace `Build`)

Reconstrução do editor Construir **do zero**, em `client/Scripts/Build/`, sem reaproveitar janela, controles, gizmos, estados nem lógica do editor clássico (`BuildEditor.*`, `BuildGizmo`). O editor clássico continua no jogo **só durante a transição** e será removido quando o novo atingir paridade (ver "Estado" no fim).

## O que foi preservado (infraestrutura estável)

Autenticação, rede (`NetworkManager`, comandos de construção do servidor da ilha), API de objetos, inventário, regras compartilhadas (`PrimMesh`, `PhysicsRules`, `LinksetRules`, `PrimitiveCatalog`), o renderizador de objetos do mundo (`BuildObjectView`, `MaterialBuilder`), o ScriptHost, a física simulada no servidor e os sons dos scripts. O editor novo só conversa com o servidor pelos comandos que já existem (`create`, `update`, `delete`, `structure`).

## Como ligar

Menu **Conteúdo → Alternar editor de construção (novo / clássico)**, ou a variável de ambiente `LIFEVERSE_BUILD_EDITOR=new` (ou `classic`). Exatamente um dos dois fica no mundo; a escolha é lembrada (`user://build_editor.cfg`). Os botões, o menu de contexto e a tecla B passam por `BuildHost`, que fala com o editor presente. O classic continua o padrão até a paridade.

## Módulos

| Módulo | Responsabilidade |
|---|---|
| `BuildEditorController` | Nó do mundo: dono dos módulos, entrada (teclas, mouse), ferramentas, criação, duplicar, excluir, desfazer. |
| `BuildEditorWindow` | Janela: barra de ferramentas, contadores, opções de encaixe, hierarquia, criação, estado. Só mostra estado e levanta eventos. |
| `BuildCreationPanel` | A tela "Criar objeto": grade de 12 formas + "Importar modelo 3D". |
| `BuildPrimitiveFactory` | Catálogo da grade, estado inicial de um objeto novo e a prévia translúcida. |
| `BuildNetworkSynchronizer` | Réplica das objetos da ilha (carga + operações do servidor), envio de comandos, recusas com motivo, versões (resposta antiga não desfaz a nova), limites e impacto reais do servidor. |
| `BuildSelectionManager` | Seleção por raio, múltipla (Shift), conjunto vinculado inteiro ou só uma peça (Ctrl), objeto ativo. |
| `BuildTransformManager` + `BuildGizmoMath` | Conta do arrasto: mover (eixo ou livre), girar (3 anéis + anel da câmera), esticar (por lado ou uniforme), eixos do mundo ou do objeto, encaixe de posição/giro/tamanho, limites (0,01 a 64). |
| `BuildGizmoController` | As alças 3D: vermelho X, verde Y, azul Z, tamanho constante na tela, acham-se por raio (sem corpos físicos). |
| `BuildFaceSelectionManager` | Face sob o cursor (Shift soma), destaque, nomes das faces. |
| `BuildLinkManager` | Vincular (o primeiro escolhido é a raiz) e desvincular, com o mesmo plano que o servidor valida; árvore do conjunto. |
| `BuildUndoRedoManager` | Histórico de comandos reversíveis (até 100) com checagem de versão. |
| `BuildPersistenceManager` | Contrato com o servidor: como um comando e um objeto são escritos; texto de cada recusa. |
| `BuildIcons` | Desenhos vetoriais das formas. |
| `BuildHost` | Escolha do editor (novo ou clássico) e atalhos para o resto do jogo. |

## Fluxo de criação (Fases 1 e 2)

1. **Construir** (botão ou B) abre a janela; **Criar** mostra a grade: Caixa, Esfera, Cilindro, Prisma, Toroide, Tubo, Anel, Cone, Pirâmide, Semiesfera, **Árvore**, **Grama** e **Importar modelo 3D**.
2. Escolher uma forma arma o posicionamento: uma cópia translúcida segue o cursor sobre o terreno e sobre outros objetos (um raio acha a superfície; a prévia e os corpos de seleção não contam como superfície).
3. O clique envia `create` ao servidor. **Nada existe antes da resposta**: a prévia nunca vira objeto. O servidor valida permissão, limite de objetos e de custo, e dá o identificador persistente; a recusa chega com o motivo em português.
4. O objeto criado é selecionado e a ferramenta **Mover** aparece sobre ele. Shift + clique coloca vários.

**Árvore** e **Grama** são formas novas aceitas pelo servidor (`PrimitiveCatalog`, `BuildCatalog`, custo 160 e 48, teste de paridade): a árvore tem tronco e duas copas geradas (só o tronco bloqueia quem anda, no cliente e no servidor); a grama é um tufo que se atravessa. As cores vêm do material do objeto (tronco sempre marrom). Ainda **não** têm textura.

**Importar modelo 3D** abre o importador do inventário; colocar o modelo no mundo pelo inventário cria um objeto `mesh` pelo novo editor.

## Ferramentas (Fase 3)

Teclas: **1** Selecionar, **2** Criar, **3** Mover, **4** Girar, **5** Esticar, **6** Face, **Ctrl+D** duplicar, **Delete** excluir, **Ctrl+G** vincular, **Ctrl+Shift+G** desvincular, **Ctrl+Z** desfazer, **Ctrl+Y** (ou Ctrl+Shift+Z) refazer, **Esc** volta (cancela o arrasto, depois faces, depois seleção, depois fecha).

- **Mover**: setas por eixo; esfera central move no plano da câmera. **Girar**: um anel por eixo e o anel branco (em volta da câmera). **Esticar**: caixas por lado (sempre nos lados do objeto) e a esfera central (uniforme). Grupos giram em torno do centro do conjunto e mantêm as distâncias.
- **Eixos** Mundo ou Objeto; **Encaixar** liga o passo de posição (m), de giro (°) e de tamanho.
- Durante o arrasto o objeto só se move **neste computador** (prévia); ao soltar, vai um `update` por objeto com a versão de que partiu. Se o servidor recusar, o objeto volta ao último estado confirmado e o motivo aparece.
- **Face**: clique escolhe a face sob o cursor (formas procedurais); Shift soma. Aplicar material por face é da aba Textura (Fase 7). Modelos importados ainda não têm escolha de face.
- **Vincular/Soltar**: um comando `structure` só, tudo-ou-nada. Protegidos são recusados antes de enviar. A **hierarquia** do conjunto aparece como árvore e cada linha seleciona uma peça.
- **Desfazer/Refazer** são comandos novos ao servidor: se outra pessoa alterou o objeto desde então, o passo é pulado e a pessoa é avisada (nunca sobrescreve). Criação desfeita vira exclusão; exclusão desfeita cria de novo (a identidade nova é acompanhada pelo histórico).

## Números do servidor

"Objetos selecionados" e "Impacto na ilha" somam o `cost` que **o servidor** gravou em cada objeto (campo novo `Cost` na resposta de objetos). Ilha e terreno (usados, máximo, restante) vêm de `GET …/build-limits`. Nada é calculado no cliente.

## Testes

`BuildEditorV2Smoke` (janela, grade, prévia, criação confirmada pelo servidor, recusa, seleção, duplicar, excluir, impacto, limites, árvore/grama, importar, troca de editor), `BuildTransformSmoke` (mover, encaixe, cancelar, livre, girar, eixos locais, esticar, limite, recusa, desfazer, conflito) e `BuildLinkFaceSmoke` (vincular, soltar, hierarquia, mover conjunto, desfazer vínculo, protegido, faces). No servidor/API: `PlantObstacleTests`, `PrimitiveCatalogParityTests`, `BuildValidationTests`.


## Fase 4 — aba GERAL (identidade, ação ao clicar, venda, permissões)

A aba **GERAL** aparece para o objeto selecionado (fora da ferramenta Criar). Tudo muda o objeto de verdade, pelo servidor.

- **Nome** e **Descrição**: viram um `update` com a versão que se viu (Enter ou sair do campo). Nome vazio é recusado antes de enviar. Com vários objetos selecionados ficam como estão.
- **Criador**, **Dono** e **Último dono**: pelo **nome** (cache de `GET /users/{id}/profile`; você é "Você"). O criador nunca muda; o último dono (`LastOwnerUserId`, coluna nova) é gravado quando o objeto é vendido.
- **Grupo**: os seus grupos; vai em `properties.groupId`.
- **Ação ao clicar** (`ClickAction`, coluna nova, padrão `touch`): Tocar, Sentar, Comprar, Pagar, Abrir, Zoom, Ignorar, Nenhuma. "Comprar" só pode ser escolhida com o objeto à venda (o servidor recusa com `buy_needs_sale`); tirar o objeto da venda devolve o clique para Tocar.
- **Colocar objeto à venda**: modalidade (**original**, **cópia** ou **conteúdo**), preço inteiro de 0 a 10 000 000 moedas, botão APLICAR. O servidor exige o direito de copiar para vender cópia e o de transferir para vender o original.
- **Qualquer um**: permitir mover / copiar. **Próximo dono**: Modificar / Copiar / Transferir (só dá para marcar o que o dono tem). O servidor nunca deixa o próximo dono receber mais do que o dono tem.
- Um objeto que não é seu aparece com tudo desabilitado e a nota "não é seu".

### O que cada clique faz (para qualquer jogador, com qualquer editor)

| Ação | Comportamento |
|---|---|
| Tocar | O servidor entrega o toque ao script do objeto. |
| Sentar | O avatar senta em cima do objeto (o assento anda com ele). A ocupação só é conhecida neste computador: o servidor da ilha sabe que o avatar está sentado, não em quê. |
| Comprar | Janela com nome, vendedor, o que se recebe e o preço; "Comprar" manda `buy` à ilha, que cobra **o preço de agora** (se mudou, avisa e nada é cobrado). |
| Pagar | Janela com o valor (começa no preço do objeto) e paga ao dono pelo livro-razão (`/economy/transfer`). |
| Abrir | Lista o que o objeto guarda; o dono pede cópia ao inventário. Cópia por quem não é dono depende da aba Conteúdo (Fase 8). |
| Zoom | Uma câmera voa até o objeto e o orbita (botão direito gira, roda aproxima); Esc ou andar devolve a câmera. |
| Ignorar | O clique passa para o que está atrás. |
| Nenhuma | Nada. |

### Compra no servidor (`BuildOperations.Sales.cs`, operação `buy`)

Uma transação só: a chave de idempotência torna um clique repetido a mesma compra (nunca cobra duas vezes); confere preço, saldo, carteiras bloqueadas e bloqueios entre as pessoas; move as moedas no livro-razão com a taxa da plataforma (5 %); e entrega:
- **original**: o objeto e as peças do mesmo conjunto passam ao comprador, com os direitos do próximo dono; o dono anterior fica registrado; a venda acaba; os scripts passam a ser do novo dono;
- **cópia**: uma construção no inventário do comprador (o objeto continua com o vendedor e pode ser vendido de novo);
- **conteúdo**: uma cópia de cada item que o objeto guarda, no inventário do comprador.

A resposta (`bought`) é transmitida a todos na ilha, então o novo dono aparece para todo mundo. Migração `Phase62ObjectGeneral` (duas colunas, sem apagar nada).

### Testes
`ObjectSalesTests` (11: ações de clique, comprar só com venda, compra do original com moedas e livro-razão equilibrado, mesma chave, preço mudou/saldo/bloqueio, direitos, conjunto, scripts, cópia, conteúdo), `BuildGeneralSmoke` (a aba) e `BuildClickActionSmoke` (cada ação no mundo real).

### Limites desta fase
Sentar não valida ocupação entre jogadores; "Abrir" não dá cópias a quem não é dono; temporário e ações de mídia vêm nas fases seguintes.


## Fase 5 — aba OBJETO

- **Propriedades** (quatro interruptores, cada um é uma mudança real do objeto): **Travar** (`protect`: ninguém move, muda, vincula ou exclui até destravar), **Física** (a ilha o simula; precisa de colisão sólida: com Fantasma ligado o editor avisa antes de pedir), **Temporário** e **Fantasma** (sem colisão sólida). As regras são as do servidor (`PhysicsRules`).
- **Temporário de verdade** (servidor): ao virar temporário o objeto ganha uma hora de expirar (`ExpiresAt`, 10 minutos; editar não reinicia o relógio, desligar cancela). Um trabalhador (`TemporaryObjectWorker`, a cada 20 s) remove o que venceu, registra a operação `expire` e solta quem estava pendurado nele; a ilha percebe a remoção na atualização seguinte (até 15 s) e a **mostra a todos** como uma exclusão. A aba mostra "o servidor o remove em N min". Migração `Phase63TemporaryObjects`.
- **Tipo de objeto**: Caixa, Esfera, Cilindro, Prisma, Toroide, Tubo, Anel, Cone, Pirâmide, Semiesfera. Trocar **mantém tamanho, material, física e os ajustes que ainda servem**; se algum seria descartado, uma confirmação lista quais. Modelos importados, árvore e grama não trocam de tipo.
- **Posição, Tamanho e Rotação** em números (um objeto de cada vez; vários mostram a nota). Mudanças em rajada viram **um** update depois de uma pausa de 0,45 s, com a versão que se viu; o tamanho respeita o limite do servidor (0,01 a 64); entram no histórico.
- **Tamanho do raio**: só nas formas redondas (esfera, semiesfera, cilindro, cone, tubo, toroide, anel): metade da largura; a altura da esfera acompanha. Nas outras formas o controle não existe.
- **Forma paramétrica** (só os controles que a forma tem): corte do caminho (início/fim), oco (%), furo X/Y ou espessura/altura do perfil (toroide e anel), torção **inicial e final**, afunilamento X/Y, assimetria X/Z, corte do perfil, e variação do raio, voltas e inclinação nos anéis. Valores sempre dentro do alcance, com um começo antes do fim. O que o servidor guarda reconstrói a malha de verdade em todos os clientes (normais, UVs e agora **tangentes**, limites, colisão e custo); os parâmetros ficam guardados para editar depois. **Restaurar forma** limpa todos. Novo no formato: `twistBegin` (PrimMesh, RoundMesh, validação e custo).
- **Alinhar** (com 2 ou mais selecionados): início, centro ou fim em X, Y ou Z; **Distribuir** (3 ou mais); **Pousar no chão**. São updates comuns, no histórico.
- **Copiar / colar propriedades**: material, física e a forma (a forma só vai inteira para objetos do mesmo tipo; em outro tipo vai o que ainda serve).

Testes: `BuildObjectTabSmoke`, `TemporaryObjectTests` (relógio, varredura, filhos soltos), `GeometryParametersTests` (torção da base, todas as formas nos limites).
Limites: a distância de alinhamento usa a caixa do objeto girado (aproximação para formas curvas); a remoção de temporários chega aos jogadores em até ~15 s.


## Fase 6 — aba RECURSOS

- **Luz**: emitir luz, cor, intensidade (0 a 8), alcance (0,5 a 30 m), atenuação e **sombras**. A luz ilumina a cena de verdade (agora também em modelos importados). O mundo respeita os limites da placa de vídeo: 16 luzes acesas e **4 com sombra** ao mesmo tempo. Sombras custam 3 pontos a mais no orçamento. Desligar a luz leva junto o projetor, que depende dela.
- **Reflexo**: uma sonda de reflexo real do renderizador (`ReflectionProbe`), com intensidade (0 a 1) e alcance da área (2 a 30 m); até **4** sondas funcionando ao mesmo tempo; custa 6 pontos.
- **Forma física**: Nenhuma, **Prim** (a forma do objeto), **Casco convexo** e, só para modelos importados, **Malha exata** (só objeto parado). Um objeto com Física ligada **não aceita** Nenhuma nem Malha exata: a combinação é bloqueada e explicada, antes de pedir ao servidor. Massa, atrito, quique e material de superfície valem para objetos com Física ligada e vão sem perder o modo físico.
- **Animação de Mesh**: só para modelo importado com **esqueleto e animações** (o editor encontra o `Skeleton3D` e as animações do próprio modelo e mostra "esqueleto com N ossos e M animações"; para uma primitiva, um modelo carregando ou um modelo sem animações o controle fica desabilitado e diz por quê). Escolher a animação, **reproduzir/pausar**, **repetir (loop)** e **velocidade** (0,1 a 4). Todos veem o mesmo instante: o servidor grava o momento em que a animação começou ou mudou (`since`), e cada cliente calcula a posição no clipe com esse relógio (e o `offset` onde ela estava ao pausar); renomear o objeto não reinicia. Um clipe que o modelo não tem não toca nada.
- Servidor: valida `reflection`, `light.shadow` e `animation` (alcances, tipos, nome do clipe), cobra reflexo, sombra e animação, e carimba a animação (`ObjectAnimation`).
- Testes: `BuildResourcesSmoke` (luz, sombras limitadas, reflexo, forma e números da física, bloqueio da combinação, animação com um modelo com esqueleto montado em memória: reproduzir no mesmo instante, pausar, loop com velocidade, clipe desconhecido), `ObjectResourcesTests` (carimbo, alcances, orçamento).
- Ainda no editor clássico: o **projetor de luz** e o **caminho flexível (Flexi)**. Entram na aba antes de o clássico ser removido (Fase 8).


## Fase 7 — aba TEXTURA

O alvo é o **objeto inteiro** ou as **faces escolhidas** com a ferramenta Face (o botão "Escolher faces" leva até ela). Mudar o objeto inteiro também tira o mesmo ajuste das faces, para o objeto mudar de verdade. Todo ajuste vai guardado no material do objeto, o servidor confere e todos desenham com o renderizador real.

- **PBR**: **Material** (materiais prontos para começar, **Aplicar**, **Remover** que deixa liso e branco, **Importar** e **Exportar** material em arquivo `.lvmat`: do arquivo entram só chaves conhecidas, dentro dos limites, e **nunca as imagens de outra pessoa**); **Base color** (textura do inventário ou padrão do sistema, cor, alfa em %, modo alfa opaco/mistura/recorte); **Normal** (textura e intensidade 0 a 2); **Emissive** (textura, cor, intensidade); **(O)RM** (textura empacotada de oclusão, rugosidade e metal; oclusão só com a textura, rugosidade e metal só sem ela).
- **Blinn-Phong** (uma camada de compatibilidade por shader, com as diferenças documentadas: a luz é a do shader, sem os reflexos do PBR): **Diffuse** (textura, cor, **Full Bright**), **Normal** (textura e intensidade), **Specular** (textura, cor, intensidade e brilho superficial), **Transparência** em % e **Glow**.
- **Mapeamento por mapa**: abas **ALL | BASE COLOR | NORMAL | (O)RM | EMISSIVE** (PBR) e **DIFFUSE | NORMAL | SPECULAR** (Blinn-Phong), cada uma com escala H/V, deslocamento H/V e rotação. **ALL** dá o mesmo mapeamento a todos os mapas; uma aba de mapa muda só aquele.
- **Seletor de texturas** novo: Minhas texturas (inventário), Texturas do sistema e **Importar imagem…**. O novo editor agora também cria o cache de texturas do mundo (antes só o editor clássico o criava, então texturas não apareciam sem ele).
- Renderizador: três parâmetros novos no shader (`normalStrength`, `specularStrength`, `aoStrength`) validados no servidor.

### Mídia
- **Imagem** (PNG, JPG, WebP) ou **vídeo** (Ogg Theora `.ogv`) de um endereço **https público**: o servidor recusa `http`, `localhost`, nomes `.local`/`.internal`, endereços IP, redes privadas, usuário e senha no endereço, endereços longos e arquivos de outro tipo, também nas faces. O cliente confere antes e repete as mesmas regras.
- Aplicar, remover, **reproduzir/pausar**, **repetir**, **glow** (a mídia vira o mapa emissivo) e escala/deslocamento U/V. Uma superfície com mídia é do próprio objeto (nunca compartilha material com outro).
- Consumo de recursos: cada pessoa pode **desligar a mídia no seu computador** (nada é baixado); **no máximo 2 vídeos tocam ao mesmo tempo**, só os que estão a até 40 m; imagens até 8 MB e vídeos até 25 MB; o arquivo baixado fica em disco; o som do vídeo fica mudo (o mundo ainda não sabe posicionar o som no espaço). Mídia pesa no orçamento da ilha (5 a imagem, 12 o vídeo).
- **Conteúdo web** não é oferecido: o cliente não tem um navegador embutido (a dependência técnica é o CEF ou um WebView do sistema); uma página falsa seria pior que nenhuma. A tela diz isso.
- Limites: o **vídeo** nunca foi tocado por um teste automático (não há arquivo `.ogv` nem codificador no projeto): foi testado o que o cerca (recusa de arquivos que não são Ogg, orçamento, desligar). A mídia vale em primitivas, ainda não em modelos importados, e vídeos não são sincronizados entre jogadores.

Testes: `BuildTextureSmoke` (modelos, mapas, números, mapeamento por mapa, faces, Blinn-Phong, materiais em arquivo, mídia com imagem de verdade e o que acontece com arquivos inválidos), `ObjectMediaTests` (27 casos de endereço, faces, limites, custo).

## Fase 8 — aba CONTEÚDO, projetor e Flexi

**Aba CONTEÚDO** (`BuildContentTab`, `BuildContentManager`, `BuildPermissionManager`): o que o objeto carrega, numa árvore de pastas (Scripts, Objetos, Sons, Animações, Imagens, Texturas, Materiais, Notecards, Outros). A pasta padrão vem do tipo do item; **Mover para a pasta** grava a pasta na própria entrada (`folder`), então sobrevive a reinício.

- **NOVO SCRIPT** cria um script Lua no objeto (primeiro modelo) e abre o editor. O editor tem **Validar** (o servidor lê o código com o mesmo parser da ilha e devolve a linha do erro), Salvar (Ctrl+S), modelos e console.
- **Salvar/criar recusa erro de sintaxe**: `LuaSyntax.Check` (MoonSharp, só compila, nunca executa) faz a API responder "linha N: …" e o código salvo não muda.
- **RESETAR SCRIPTS** (`POST …/scripts/reset` ou `…/{id}/reset`): sobe a versão (a ilha encerra a execução antiga e inicia uma limpa em até 15 s), reativa o script, apaga o estado guardado e registra "reset" no log. O código-fonte nunca é tocado.
- **ATUALIZAR CONTEÚDO** lê os scripts de novo no servidor, sem duplicar linhas e sem descartar o que está aberto (e não salvo) no editor.
- **PERMISSÕES** mostra o que o objeto permite (só objeto modificável altera o conteúdo) e o que o item escolhido permite (copiar/modificar/transferir). Um item dentro do objeto é só uma referência (`assetId`): continua com os direitos que tem no inventário, então entrar num objeto nunca os amplia. Copiar exige o direito de copiar.
- Itens: **Inserir do inventário…**, arrastar do inventário para a lista, Abrir, Renomear, Copiar, Propriedades, Remover (com confirmação), até 64 itens; o servidor confere que quem insere possui o item.

**Aba RECURSOS** ganhou **Projetor** (imagem do sistema ou do inventário, abertura, foco, ambiente; precisa de luz acesa, e apagar a luz leva o projetor junto) e **Flexibilidade (Flexi)** (maciez, gravidade, atrito do ar, vento, tensão e forças X/Y/Z).

### Testes
- API: `ScriptContentApiTests` (sintaxe com linha, validar, resetar).
- Cliente: `BuildContentSmoke` (script novo/validar/salvar, pastas, mover, renomear, remover, resetar, atualizar sem perder alterações, objeto sem direito de modificar) e a seção de projetor/Flexi em `BuildResourcesSmoke`.

## Fase 9 — construir em grupo

- **Bloqueio de edição** (`BuildLockManager`): o servidor da ilha dá reservas de 20 s. O editor pede a reserva do que está selecionado, devolve o que foi solto e renova a cada 8 s; fechar o editor devolve tudo. As reservas dos outros jogadores são lembradas, o objeto ganha a etiqueta de quem edita, e **toda edição** (atualizar, vincular, propriedades, proteger, excluir) é recusada no cliente antes de ir (`LockGate` do sincronizador) com a mensagem "Fulano está editando…"; o servidor recusa também. Um objeto preso por outro não pode ser arrastado nem selecionado para reservar.
- **Arrasto ao vivo**: enquanto alguém arrasta, o cliente manda a pose a ~8 vezes por segundo (`live`). O servidor (`LivePoses.Accept`, em `shared`) só repassa se quem manda segura a reserva de **todos** os objetos, os números são reais e dentro da faixa e há no máximo 64 objetos. Nada é salvo: o salvamento continua sendo a atualização normal no fim do arrasto. Se o arrasto é solto sem salvar (a reserva some), o objeto volta ao lugar real.
- **Reconexão**: ao voltar, o replica carrega de novo, as reservas conhecidas dos outros são descartadas (nada é presumido) e as minhas são pedidas outra vez. Uma reserva que não é renovada expira sozinha.
- **Devolver** (aos donos, só dono do terreno/admin) e **Histórico** do objeto (quem fez o quê, revisão e hora) ficaram na janela, em `BuildAuditManager`. O limite da ilha/terreno já era mostrado.
- Conflitos de revisão e desfazer seguro continuam como na Fase 2 (um passo recusado sai do histórico; desfazer não sobrescreve a mudança de outra pessoa).

### Testes
- API: `LivePosesTests` (só o dono da reserva, todos os objetos, números fora da faixa, limite de 64).
- Cliente: `BuildCollabSmoke` (reserva acompanha a seleção, renovação, reserva alheia bloqueia e nomeia quem edita, arrasto alheio ao vivo e desfeito, arrasto salvo permanece, meu arrasto só dos objetos que seguro, expiração, reconexão, fechar devolve, texto do histórico).
- Os outros smokes do editor passaram a ignorar os comandos de reserva ao contar as operações.

## Fase 10 — paridade, remoção do editor clássico e testes finais

**Portado do clássico para o editor novo**
- **Construções** (`BuildPackageManager`): *Guardar* (a seleção vira construção no inventário; só objetos seus), *Retirar* (guarda e apaga do mundo; Ctrl+Z traz de volta), prévia translúcida que segue o mouse e um clique coloca (Esc cancela), colocação objeto a objeto abaixo do limite da ilha (até 10 de uma vez, o resto ~11 por segundo), um único passo de desfazer, hierarquia (vínculos) refeita nas cópias e colocação pelo servidor quando a construção não pode ser copiada. O inventário passou a colocar construções pelo editor novo.
- **Predefinidos**: botões na tela Criar (mesma prévia e colocação).
- **Câmera de edição** (botão *Câmera*, Home foca): gira em volta da seleção; o avatar some enquanto ela está ligada; fechar o editor ou perder a conexão devolve a câmera normal.
- **Terreno**: botão que abre as ferramentas de terreno e tira o editor de construção do caminho.
- **Menu de contexto do objeto** (Mover, Girar, Esticar, Duplicar, Copiar, Excluir) abre o editor novo; *Copiar* agora duplica ao lado (o editor novo não tem área de transferência).

**Removido**: `BuildEditor.*` (todas as partes), `BuildGizmo` e `BuildGrid`, o nó `BuildEditor` do `World.tscn`, o alternador "novo/clássico" do menu e `BuildSmoke` (808 linhas, só do clássico). `BuildHost` agora só acha o editor único (`BuildEditorController`). O que o `BuildSmoke` cobria e continua coberto no editor novo está nos smokes `BuildEditorV2`, `BuildTransform`, `BuildLinkFace`, `BuildGeneral`, `BuildClickAction`, `BuildObjectTab`, `BuildResources`, `BuildTexture`, `BuildContent`, `BuildCollab` e `BuildPackage`. `BuildLoadSmoke`, `InventorySmoke` e `MovementSmoke` foram portados para o editor novo.

**Limitações conhecidas depois da remoção**
- Não há área de transferência (Ctrl+C / Ctrl+V de objetos) nem o tutorial de primeiro uso do clássico; duplicar (Ctrl+D) e Guardar/colocar construções cobrem o uso.
- Os "modos" do clássico (Foco, Mover, Editar, Criar, Terreno) viraram ferramentas (teclas 1–6) e o botão Terreno.
- `inventory_smoke` e `terrain_smoke` já falhavam na `main` antes deste trabalho (modelo sem corpo de colisão no cliente depois do commit das colisões no servidor; terreno).
- Vídeo em superfícies nunca foi tocado em teste automático; mídia web não é oferecida.

## Estado

Fases 1 a 10 feitas. O editor novo é o único. Para valer em produção é preciso reimplantar o backend (sintaxe Lua, reset, migrações das Fases 6.2/6.3) e o island-server (repasse `live`, compra, objetos temporários).
