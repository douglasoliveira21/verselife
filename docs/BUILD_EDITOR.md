# Editor de construção 3D

O modo Construir abre pelo botão **Construir** da barra inferior, por Conteúdo > Construir ou pela tecla `B`. O editor suporta cubo, esfera, cilindro, cone, plano, prisma, tubo, anel e pirâmide.

## Como usar

- **Abrir:** botão **Construir** da barra inferior, Conteúdo > Construir ou a tecla `B` (também `Ctrl+B`). Tudo em português.
- **Janela "Editor de Construção":** layout horizontal conforme os desenhos em `client/Assets/ui/construir/`. No alto, as abas **Geral**, **Objeto**, **Textura**, **Recursos**, **Conteúdo** e **Alinhar**; logo abaixo, uma barra com o que se usa o tempo todo (Face, Câmera de edição, Focar, Criar, Desfazer, Refazer, Tutorial, a seleção e o indicador "Salvando / Salvo"). Cada aba mostra uma prévia 3D viva do objeto selecionado (mesmo mundo à parte, gira com o mouse): **Objeto** tem a coluna Ferramentas (Selecionar, Mover, Rotacionar, Redimensionar, Duplicar, Copiar, Colar, Agrupar, Desagrupar, Excluir), Posição, Rotação e Tamanho com setas e botão de proporção, Passo de movimento (campo e controle), Grade e encaixe, Passo de giro (1°, 5°, 15°, 45°, 90°), Fantasma e Forma (oco, afunilar X/Y, torção) com Restaurar e Aplicar à seleção; **Textura** tem cor com seleção rápida, rugosidade, metalicidade, opacidade, brilho, Alvo (objeto ou face), biblioteca em grade com origem (minhas, favoritas, recentes, do sistema), prévia, UV e Aplicar textura; **Recursos** é a luz; **Conteúdo** é a lista do objeto (ícone, nome, tipo e remover); **Alinhar** são os alinhamentos, distribuir, ajustar ao solo e copiar e colar a transformação. Cor, rugosidade, metalicidade, opacidade e brilho são enviados ao servidor um instante depois de soltar o controle; o resto, ao apertar Enter, as setas ou o botão Aplicar. Valores fora dos limites são cortados (tamanho 0,01 a 64; oco 0 a 0,95; afunilamento -1 a 1; torção -180 a 180). Os números aparecem no formato brasileiro (1,500) e aceitam vírgula ou ponto. As abas Material, Cor, Iluminação, Física, Script e Permissões que aparecem nos desenhos não existem: o que elas mostram já está nas abas acima, ou ainda não existe no jogo.
- **Primitivas (11):** caixa, esfera, cilindro, prisma, toro, tubo, anel, pirâmide, cone, semiesfera e plano. **Criar** (botão do cubo): escolha a forma na aba Geral, aperte o botão e **clique onde quer colocá-la**; uma cópia translúcida da forma acompanha o mouse sobre o terreno e sobre outros objetos para mostrar o ponto antes do clique. Shift + clique coloca várias; Esc, outra ferramenta ou o botão de novo cancelam. Com a opção de grade ligada o ponto encaixa na grade.
- **Selecionar:** clique em um objeto; Shift soma ou tira da seleção. Objetos vinculados são selecionados juntos.
- **Alças 3D:** `1` Selecionar (sem alças), `2` Mover (setas X vermelho, Y verde, Z azul), `3` Girar (anéis), `4` Esticar (cubos nos eixos) e `5` Face. As alças seguem o mouse **sem saltos** (qualquer posição, ângulo e tamanho); segure **Ctrl** para encaixar nos passos (passo de movimento, passo de giro e 5 % de tamanho), ou ligue a grade para encaixar posições. Os valores guardados são arredondados a um milímetro (um centésimo de grau nas rotações). `Esc` cancela um arrasto.
- **Copiar / colar / duplicar** (`Ctrl+C`, `Ctrl+V`, `Ctrl+D`), **vincular / desvincular** (`Ctrl+G`, `Ctrl+Shift+G`), **excluir** (`Delete`), **desfazer** (`Ctrl+Z`) e **refazer** (`Ctrl+Y`). O histórico (60 passos) cobre mover, girar, escalar, material, vincular, criar, colar, duplicar e excluir.

## Geometria procedural, faces, grade e alinhamento (Etapa 2)

- **Malha de verdade:** caixa, prisma, cilindro, tubo, cone e pirâmide são gerados por `shared/Building/PrimMesh.cs` (um contorno varrido de baixo para cima). Cada ajuste muda a malha: **oco** (furo vazado, com parede interna e topo/base em anel), **afunilamento X/Y** (o topo encolhe ou cresce), **torção** (até ±180°, em até 18 níveis), e **corte** (`cutStart`/`cutEnd`, de 0 a 1 do contorno, que retira uma cunha e cria as duas faces de corte). O tubo é sempre oco e o cone e a pirâmide terminam em ponta. Esfera, semiesfera, toro, anel e plano continuam com as malhas prontas do Godot (o anel e o toro aceitam oco).
- **Colisão exata:** a colisão desses objetos segue a malha (inclusive o furo), e todos os valores são cortados nos limites aceitos pelo servidor; o gerador nunca produz triângulos invertidos ou fora do limite (a malha mais pesada tem menos de 4000 triângulos).
- **Faces:** cada parede é uma superfície própria (topo 0, base 1, laterais 2 em diante, depois interior e cortes). Com a ferramenta **Face** (`5`) clique em uma face (ela fica realçada em amarelo) e use **Cor na face** na aba Textura; a cor é guardada em `faces` no material do objeto e pode ser limpa.
- **Grade e encaixe** (aba Objeto): grade 3D sob a seleção com o espaçamento do passo de movimento, **Ajustar à grade ao mover** (a posição final cai em múltiplos do passo, em coordenadas do mundo) e passo de giro configurável.
- **Alinhar** (aba própria): alinhar por esquerda, centro e direita (X), base, meio e topo (Y), frente, centro e trás (Z), **distribuir** com espaços iguais, **ajustar ao solo** e copiar/colar posição, rotação e tamanho. Cada ação é uma edição comum, com revisão e desfazer.
- **Vinculação e duplicação** continuam como na Etapa 1 (`Ctrl+G`, `Ctrl+D`).

## Texturas, materiais e luz (Etapa 3)

- **Aba Textura:** cor, rugosidade, metalicidade, opacidade e brilho (cor e intensidade da emissão), mais a **biblioteca de texturas** com quatro fontes: *Minhas texturas* (imagens PNG, JPG ou WebP do seu inventário), *Favoritas*, *Recentes* (as últimas 12 usadas, guardadas em `user://build_prefs.cfg`) e *Texturas do sistema* (xadrez, tijolos, tábuas, listras, ruído e ladrilhos, desenhadas pelo jogo, sem upload). **Importar imagem** envia o arquivo ao inventário (até 8 MB; o servidor valida o formato) e ele aparece em Minhas texturas.
- **Alvo:** o objeto inteiro ou, com uma face selecionada (ferramenta Face), só aquela face. **Aplicar textura**, **Como mapa normal** (relevo) e **Remover** valem para o alvo. Cada face pode ter textura, UV e cor próprias; o que ela não define vem do material do objeto.
- **UV:** repetição horizontal e vertical (0,01 a 100), deslocamento (-100 a 100), rotação (-360° a 360°) e espelhamento horizontal e vertical. Objetos com textura usam um shader próprio (o material padrão do Godot não gira nem espelha UV); objetos só com cor continuam no material padrão.
- **Aba Recursos:** **luz** com cor, intensidade (0 a 8) e alcance (0,5 a 30 m), sem sombras; cada jogador acende no máximo 16 luzes por vez, e as demais ficam apagadas até sobrar espaço.
- **No servidor:** `MaterialAssets` valida os números e os nomes das texturas do sistema, limita 8 imagens por objeto e 32 faces com estilo, e só aceita uma imagem nova se for PNG, JPEG ou WebP que quem edita possui (dono ou no inventário), como já é feito com modelos 3D. Quem pode entrar na ilha baixa as imagens que os objetos dela usam, para vê-las. As imagens são reduzidas a 2048 px no cliente.
- Tudo isso fica no `materialJson` do objeto (`texture`, `normalMap`, `builtin`, `uvRepeatX/Y`, `uvOffsetX/Y`, `uvRotation`, `flipU/V`, `emission`, `emissionStrength`, `light`, `faces[]`), então segue as mesmas revisões, o mesmo desfazer e a mesma persistência.

## Inventário, importação e conteúdo (Etapa 4)

- **Salvar no inventário** (aba Geral): escreva um nome e use **Salvar cópia** (os objetos ficam no mundo) ou **Retirar** (os objetos saem do mundo; `Ctrl+Z` os traz de volta). A seleção inteira vira **um item do inventário** da categoria `packages`, com formas, tamanhos, materiais, texturas, conteúdo e vínculos. As posições são guardadas relativas ao meio da construção e à base do objeto mais baixo; cada conjunto vinculado continua sendo um conjunto. Só dá para guardar objetos seus, até 500 por construção.
- **Colocar uma construção:** no inventário, dê dois cliques no item, use **Colocar no mundo** ou arraste-o para o mundo ou para a área "Solte aqui". O cliente cria os objetos um a um (cerca de 11 por segundo, abaixo do limite de 20 por segundo da ilha), cada um validado de novo pelo servidor; os vínculos viram vínculos novos, a construção fica selecionada e **um único `Ctrl+Z` remove tudo**. Se o servidor recusar um objeto (limite da ilha ou permissão) a colocação para e avisa.
- **Importar modelo 3D** (botão "+" do inventário): escolha um GLB ou glTF e veja, antes de enviar, uma prévia girando, o tamanho original, triângulos, materiais, texturas, partes e animações. Modelos acima dos limites (300 mil triângulos, 64 materiais, 32 texturas, 5 mil partes, 100 MB) não podem ser confirmados e a janela diz o que ajustar. Você escolhe o **tamanho ao colocar** (metros do lado maior, 0,1 a 64; fica salvo e vale para os próximos modelos). O servidor confere os mesmos limites no upload lendo a parte JSON do arquivo, e rejeita arquivos danificados.
- **Aba Conteúdo:** lista o que o objeto carrega (scripts, texturas, sons, animações, modelos, outras construções). **Adicionar do inventário**, **Renomear** e **Remover** editam o `contentJson` do objeto, com revisão e desfazer como qualquer edição. O objeto guarda só referências (até 64, sem repetir o mesmo item); o servidor confere que quem adiciona tem o item.
- **API:** `POST /api/v1/inventory/build-packages` (valida cada objeto com as mesmas regras do editor, as texturas, os modelos e o conteúdo, e grava o item) e `GET /api/v1/inventory/build-packages/{itemId}` (só o dono lê). O formato é `lifeverse.build` versão 1; o cliente (`shared/Building/BuildPackage.cs`) e a API (`PackageDocument`) seguem as mesmas regras, e um teste confere que concordam.

## Construir em conjunto: terrenos, bloqueio, limites e devolução (Etapa 5)

**Quem pode o quê** (`BuildRules`, aplicado pela API em toda operação que a ilha envia; a interface só reflete isso):
- O dono da ilha e quem tem permissão `admin` fazem tudo na ilha.
- O dono de um terreno constrói nele e pode alterar ou devolver qualquer objeto que esteja dentro dele.
- Em um terreno, os outros só constroem se a permissão `build` do terreno estiver ligada (aí qualquer um constrói, mas só mexe no que é seu); permissão de construir na ilha **não** vale dentro de um terreno privado. Fora de terrenos vale a permissão `build`/`edit` da ilha.
- Alterar ou excluir um objeto exige ser o dono dele, administrador ou dono do terreno onde ele está; só o dono ou um administrador mudam o que os outros podem fazer com ele. Mover um objeto para dentro de outro terreno exige poder construir lá.

**Limites por custo** (`BuildCost`): cada objeto custa pontos — geometria (1 ponto por 20 triângulos estimados; oco, torção e corte contam), física, texturas e luz, conteúdo — guardados em `world.objects.cost` (migration `Phase32BuildLimits`; objetos antigos são calculados uma vez na inicialização). A ilha tem limite de objetos (`objects`, 5.000 por padrão) e de custo (`cost`, 200.000 por padrão) nas suas configurações de recursos; cada terreno tem o limite de objetos (`MaxObjects`) e um orçamento de 20 pontos por vaga. O que estourar volta com um código (`cost_limit`, `object_quota`, `parcel_object_limit`, `parcel_cost_limit`) que a janela explica; a aba Geral mostra o uso da ilha e do terreno que você está olhando, e se pode construir ali.

**Bloqueio de edição:** ao selecionar, você pega uma reserva de 20 s sobre os objetos (renovada a cada 8 s enquanto a seleção continua; liberada ao soltar a seleção, sair do modo construção ou da ilha). Quem tenta mexer em um objeto reservado por outra pessoa vê "Em edição por …", o objeto mostra uma etiqueta e as alças não aparecem; a ilha recusa as alterações dele antes mesmo de falar com a API. Quem entra depois recebe a lista de reservas. Elas ficam na memória do servidor da ilha (duram segundos por desenho; reiniciar o servidor as limpa).

**Devolver ao dono:** `Devolver ao dono` (aba Geral) manda os objetos selecionados para o inventário de quem os fez, como construção salva (um item por dono), e os tira do mundo para todos. Vale para o dono do terreno, administradores e para o próprio dono; é tudo ou nada (se um objeto não puder ser devolvido, nenhum é) e não dá para desfazer pelo `Ctrl+Z` (o dono recebe de volta).

**Auditoria:** toda criação, alteração, exclusão e devolução grava uma linha em `world.build_operations` (quem, o quê, revisão, quando). O botão **Histórico** mostra as últimas 100 do objeto selecionado, para o dono, o dono do terreno e administradores (`GET /api/v1/worlds/islands/{id}/objects/{objectId}/audit`); `GET .../build-limits?x=&z=` informa o uso e se você pode construir no ponto.

**Correções encontradas nesta etapa** (existiam desde antes): o caminho que os jogadores usam (ilha → API) não conferia o dono em alterações e exclusões, não gravava nome, descrição nem conteúdo ao atualizar, aplicava uma validação mais fraca que a do editor, e o servidor da ilha rejeitava toro e semiesfera. Tudo passa agora por um único caminho (`BuildOperations`).

## Desempenho, câmera e acabamento (Etapa 6)

**Desempenho** (medido com `BuildLoadSmoke`, uma ilha de 2.000 objetos, no Godot sem janela; os tempos variam com o computador, a comparação é o que importa):
- **Objeto parado não é refeito.** Antes, toda confirmação do servidor reconstruía o objeto inteiro (malha, materiais, colisão, modelos baixados). Agora só muda a transformação quando forma, material, física e nome seguem iguais: mover os 2.000 objetos levou ~7 ms contra ~220 ms para refazê-los (cerca de 30 vezes menos).
- **Objetos iguais compartilham** a malha, a forma de colisão e o material (2.000 objetos com ~100 formas diferentes usaram 94 malhas e 3 materiais). Materiais com textura só são compartilhados quando o cache de texturas já existe.
- **Distância de desenho:** um objeto some além de 120 m por metro do seu tamanho (um metro some depois de 120 m, mínimo de 40 m e máximo de 600 m); a colisão continua. É corte por distância, não troca de malha por nível de detalhe.
- **Carga em fatias:** ao entrar na ilha, os objetos são criados 40 por quadro, sem travar o jogo. Colar ou colocar uma construção com mais de 10 objetos sai em ritmo de cerca de 11 por segundo, abaixo do limite da ilha.
- Construir 2.000 objetos levou ~260 ms; cada objeto tem ~6 nós.

**Câmera de edição** (botão ou `End`): gira em volta da seleção. Botão direito orbita, botão do meio (ou Shift + direito) desloca, a roda dá zoom proporcional (0,6 a 160 m), `Home` ou **Focar** aponta para a seleção com a distância certa para o tamanho dela. O avatar fica escondido enquanto ela está ligada e a câmera do jogador volta ao sair do modo construção.

**Acabamento:** o indicador "Salvando… / ✓ Salvo / ⚠ Sem resposta do servidor" acompanha as respostas do servidor (após 8 s sem resposta avisa); o tutorial de oito passos (criar, mover, esticar, girar, dar cor ou textura, duplicar, vincular, guardar) aparece na primeira vez, cada passo termina sozinho quando a pessoa realmente faz aquilo, pode ser pulado e repetido pelo botão **Tutorial**.

**Correção de concorrência (importante):** a revisão do objeto não era protegida contra duas operações simultâneas: duas edições do mesmo objeto podiam passar pela conferência e a última apagava a primeira. Agora a revisão é um _concurrency token_ do banco (migration `Phase33BuildConcurrency`, sem mudança de tabelas) e a edição que perde recebe `revision_conflict`; há um teste que injeta uma edição no meio de outra.

## Garantias do servidor

Cada alteração é enviada ao servidor da ilha com a revisão do objeto (`expectedRevision`); o servidor valida permissão, limites (escala `0.01..64`, cota de objetos, 20 operações por segundo por conexão) e responde com o estado oficial, que é replicado a todos. Se outra pessoa alterou o objeto antes, a edição é recusada (conflito de revisão) e o objeto volta ao estado oficial. Desfazer envia a alteração inversa com a revisão atual, então também está sujeito à autorização e aos conflitos. As operações ficam em `world.build_operations`.

Materiais aceitam cor, transparência, emissão, rugosidade e metallic; a física aceita colisão, trigger e camadas.

## Arquitetura

- `BuildEditor` (cliente): seleção, histórico, copiar/colar, agrupamento e envio das operações.
- `BuildGizmo` (cliente): alças 3D com camada de física própria; arrastar usa `GizmoMath`.
- `shared/Building/GizmoMath.cs`: geometria dos arrastos (ponto mais próximo do eixo, plano de rotação, ângulo, snap), testada sem a engine.

## Limitações

- Ainda não existem: geometria procedural para esfera, toro e semiesfera (cortes e torção), encaixe por vértice, aresta ou superfície, duplicar arrastando e em sequência, texturas gratuitas compartilhadas, flexibilidade, partículas e animação de textura, miniatura própria para construções salvas, compartilhar ou transferir construções entre jogadores (copiar, retirar e devolver com permissões por item), executar scripts guardados no conteúdo, atalhos personalizáveis, permissões de objeto por grupo social (hoje: dono, dono do terreno e administradores) e testes com dois jogadores reais.
- Nome e descrição passam a ser gravados: o servidor da ilha descartava esses campos (e permissões, conteúdo e pai) antes de enviar a operação à API.

- Não há seleção por retângulo, pivô personalizado nem alças em eixos locais para mover/girar (só para escalar um objeto). A coloração por face só vale para as formas procedurais.
- Desfazer pode ser recusado se outra pessoa mexeu no mesmo objeto depois.
- Simulação física dinâmica ainda não existe.
- Verificado por testes unitários da geometria e por um teste no Godot com um "servidor" em memória que impõe revisões; não testado com dois jogadores reais.

## Terrenos

O que cada terreno permite construir (quem constrói, scripts, orçamento, retorno automático de objetos de visitantes) está em [LAND.md](LAND.md); o cartão "Terreno e limites" da aba Geral mostra essas regras do ponto onde a câmera olha.

## Editor avançado — fase 1: linksets, hierarquia e proteção

**Linkset.** Objetos com o mesmo `groupId` formam um conjunto com **uma raiz** (o membro sem pai) e as demais peças **penduradas** nela ou em outras peças (`parentObjectId`): uma árvore, até 256 peças e 16 níveis. As regras são as mesmas no cliente e no servidor (`shared/Building/Linksets.cs`, `LinksetRules`): pai no mesmo conjunto, sem ciclo, sem profundidade além do limite, sem conjunto grande demais; "vincular", "desvincular", "tornar raiz" e a cura depois que peças saem são funções puras testadas.

**Como o servidor muda a estrutura.** Grupo e pai **só mudam** pela operação `structure` (uma lista de objetos com o grupo, o pai e a revisão vista); uma atualização comum **preserva** grupo e pai. A operação vale por inteiro ou não vale: cada objeto precisa ser editável por quem pede (dono, dono do terreno ou administrador), **não protegido**, **modificável** (direito Modificar) e estar na revisão que o editor viu; e o resultado, contando também os outros membros dos conjuntos tocados, precisa ser uma árvore válida (`link_cycle`, `broken_link`, `invalid_link`, `link_too_deep`, `linkset_too_large`). Cada objeto alterado ganha revisão nova e uma linha na auditoria (`structure`). Criar um objeto já dentro de um conjunto segue as mesmas regras.

**Quando peças saem** (excluídas ou devolvidas), o servidor reorganiza o resto na mesma transação (`LinksetRules.Heal`): quem estava pendurado nela passa para o ancestral mais próximo que ficou; se a raiz saiu, a peça de menor id vira a raiz e as outras penduram nela; um conjunto que ficou com uma peça só se desfaz. A resposta traz as peças que mudaram (`healed`) e a ilha as repassa a todos.

**Proteção contra edição acidental** (`protect`/`unprotect`, coluna `world.objects.edit_locked`): um objeto protegido não pode ser movido, alterado, vinculado, excluído nem devolvido pelo próprio dono (`403 object_protected`); quem gerencia o terreno ainda pode devolvê-lo. Proteger e desproteger valem para o dono, o dono do terreno e administradores, e ficam na auditoria (`protect`/`unprotect`). O editor não mostra alças em objeto protegido, nem apaga, nem manda mover.

**No editor** (aba nova **Hierarquia**; as abas existentes continuam como estavam): árvore dos conjuntos da ilha (raiz marcada, peças aninhadas, 🔒 nos protegidos), **Vincular** (`Ctrl+G`: a primeira peça selecionada é a raiz; árvores já montadas são mantidas e conjuntos unidos ficam sob a nova raiz), **Desvincular** (`Ctrl+Shift+G`), **Tornar raiz**, **Proteger/Desproteger**, e dois modos de seleção: **Editar só a peça** (ou `Ctrl` + clique) e **Incluir o que está pendurado nela**. Clicar em um nome da árvore seleciona aquela peça. O texto da seleção diz "raiz do conjunto" ou "peça de “X”" e "🔒 protegido". Vincular, desvincular, trocar a raiz e proteger entram no desfazer/refazer e voltam a estrutura **inteira** de uma vez. **Duplicar ou colar** um conjunto mantém o formato da árvore (as cópias nascem soltas num conjunto novo e, quando todas existem, cada cópia é pendurada na cópia do seu pai).

**Direitos.** A hierarquia agora conta para os direitos de objeto (fase 4 dos direitos de itens): uma peça pendurada em algo que não pode ser modificado também não pode.

**Ilha.** O servidor de ilha repassa `structure` e `protect`, guarda no cache todos os objetos que uma mensagem traz (`objects`/`healed`) e trava a edição das peças que outro editor tem reservadas.

**Testes:** `LinksetRulesTests` e `LinksetOperationTests` (regras puras e operações: estrutura em um passo, preservação em atualização, recusas por dono/revisão/proteção/direito, árvores quebradas, limite, cura ao excluir e devolver, proteção), `LinksetChaosTests` (PostgreSQL real: 10 editores, 120 operações cada — vincular, desvincular, trocar raiz, proteger, excluir, devolver, criar —; no fim nenhuma árvore quebrada, uma só raiz por conjunto, nenhum conjunto de uma peça, nenhum erro inesperado) e novas verificações no `BuildSmoke`.

**Ainda não** (próximas fases): pivôs, espelhar e duplicação múltipla, encaixe magnético, seleção por área, física dinâmica, presets, e levar a hierarquia para construções salvas no inventário (o formato salvo ainda guarda só o grupo; ao restaurar, a árvore volta achatada).
