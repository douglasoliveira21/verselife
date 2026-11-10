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

**Ainda não** (próximas fases): encaixe magnético, seleção por área, física dinâmica, presets, e levar a hierarquia para construções salvas no inventário (o formato salvo ainda guarda só o grupo; ao restaurar, a árvore volta achatada).

## Editor avançado — fase 2: pivôs, transformações precisas, espelhar e duplicação múltipla

Nova aba **Transformar** (as abas existentes continuam como estavam). A aritmética está em `shared/Building/TransformMath.cs` e é testada sem a engine (`TransformMathTests`); o editor só converte os tipos.

**Onde girar (o pivô).** As alças, os giros e os esticamentos por número usam o pivô escolhido: *centro da seleção* (padrão, como antes), *base da seleção* (o ponto mais baixo), *raiz do conjunto*, *primeiro selecionado*, *pivô do objeto*, *ponto escolhido* (coordenadas digitadas, com o botão "Usar o primeiro") ou *origem do mundo*. As alças aparecem no pivô e o texto da aba diz onde ele está. Esticar um objeto só, com pivô fora do centro, faz o objeto crescer **para longe do pivô**.

**Pivô do objeto.** Cada objeto pode ter um pivô próprio: um deslocamento em metros, nos eixos dele, a partir do centro (como a dobradiça de uma porta). Fica **salvo com o objeto** (`world.objects.pivot_x/y/z`, migração `Phase57ObjectPivot`; até 64 m, finito; vai e volta pela API e pela ilha). Mudar o pivô conta como mudar o objeto (exige o direito Modificar); mover um objeto que diz o mesmo pivô, não. "Pivô na base" e "No centro" são atalhos.

**Eixos.** *Do mundo* ou *locais (do primeiro selecionado)*, para as alças (mover, girar, esticar) e para os números.

**Com números.** *Mover* por x, y, z metros nos eixos escolhidos; *Girar* N graus em torno do eixo X, Y ou Z que passa pelo pivô (o centro de cada objeto dá a volta e ele gira junto); *Esticar* por fatores x, y, z em torno do pivô (distâncias e tamanhos). Esticar com fatores diferentes só vale para objetos **alinhados** aos eixos escolhidos (até 5°); senão o editor explica e não faz (daria um objeto "entortado"); fatores iguais nos três sempre valem. Tamanhos fora de 0,01 – 64 m são limitados, com aviso. Tudo é um passo só no desfazer.

**Espelhar.** Pelo plano que passa pelo pivô e é perpendicular ao eixo X, Y ou Z do mundo, como **cópias** (padrão) ou no lugar. O centro é refletido; para formas que ficam iguais ao virar de lado (caixa, cilindro, esfera…) a imagem no espelho é a mesma forma girada ao contrário, sem tamanho negativo; **formas torcidas torcem ao contrário** (o sinal da torção troca); **modelos importados não são espelhados** (esquerdo e direito não são iguais) e ficam de fora com aviso.

**Duplicação múltipla.** *Linha* (cada cópia anda o passo, gira em torno do pivô e cresce o fator por cópia), *grade* (quantidades e distâncias nos três eixos) e *círculo* (objetos igualmente espaçados em volta do pivô, virando ou não para o centro, em torno de qualquer eixo). Até 200 cópias e 300 objetos por vez; o texto da aba diz quantos objetos serão criados antes de criar. Cada cópia de um conjunto vinculado é um conjunto vinculado com a mesma árvore; a criação sai em ritmo abaixo do limite da ilha e tudo é **um passo só** no desfazer.

**Testes:** `TransformMathTests` (movimento em eixos do mundo e locais, giro em torno de pivô, esticar com e sem alinhamento, espelho — inclusive espelhar duas vezes volta ao mesmo —, pivô próprio, linha/grade/círculo e números absurdos), novos casos em `LinksetOperationTests` (pivô guardado, enviado, recusado fora de 64 m, exige Modificar) e a parte nova do `BuildSmoke` (modos de pivô, mover/girar/esticar por números com desfazer, pivô do objeto, espelho com cópias e no lugar, linha, grade, círculo, limite e cópia de conjuntos).

**Ainda não:** encaixe magnético, seleção por área, física dinâmica, presets, e levar hierarquia e pivôs para as construções salvas no inventário.

## Fase 3 — encaixe magnético, seleção por área, alinhamento avançado

**Encaixe magnético** (aba *Transformar*, cartão "Encaixe magnético"). Ao arrastar uma seta de mover, a seleção é puxada para os cantos, o meio das arestas e o centro das faces de objetos vizinhos que estão perto do caminho do arrasto, ou uma face dela é feita encostar na face de outro objeto (`SnapMath.AlongAxis` e `SnapMath.Contact`, em `shared/Building/SnapMath.cs`). Cada tipo de ponto pode ser ligado ou desligado e o alcance é configurável (0,05 a 3 m). Um marcador amarelo mostra onde encaixou. Com o encaixe ligado ele tem prioridade sobre a grade.

Limites: os pontos são os da caixa de contorno orientada de cada objeto, não vértices exatos da malha; o encaixe de "faces encostando" só vale para vizinhos alinhados aos eixos; ao arrastar mais de 12 objetos o encaixe usa a caixa que envolve a seleção toda.

**Seleção por área.** Arrastar um retângulo em espaço vazio escolhe o que ficou inteiro dentro (esquerda para a direita) ou também o que o retângulo toca (direita para a esquerda); Shift soma à seleção. Conjuntos vinculados entram inteiros, exceto com "Editar só a peça". O cartão "Seleção por área" escolhe por uma caixa de coordenadas do mundo (dentro ou tocando), com atalho para preencher a caixa com a seleção atual.

**Alinhar e distribuir: opções** (aba *Alinhar*). "Alinhar a" aceita a seleção toda (como antes), o primeiro, o último, o maior, o menor ou a raiz do conjunto: o objeto de referência fica parado e os outros vêm até ele. "Distribuir" aceita espaços iguais (como antes), centros igualmente afastados ou um espaço fixo em metros entre os objetos.

Testes: `SnapAndAreaTests` (aritmética pura) e blocos novos em `BuildSmoke` (encaixe, alinhar ao primeiro, espaço fixo, seleção por volume).

## Fase 4 — física, predefinidos, prévia e construções com hierarquia

**Física por objeto** (aba *Objeto*, cartão do fantasma). Além de "Fantasma" (sem colisão sólida), cada objeto tem um modo **Estático** (padrão) ou **Físico**, com massa (0,01 a 10 000), atrito e quique (0 a 1). Fica em `PhysicsJson` (`mode`, `mass`, `friction`, `bounce`) e é validado no servidor em todo comando de objeto e em toda construção salva (`ObjectPhysics`, espelhado no cliente por `PhysicsRules`; um teste compara os dois). Um objeto físico precisa de colisão sólida (não pode ser fantasma nem gatilho) e custa 2 pontos a mais no orçamento da ilha.

**Limite honesto:** o mundo **ainda não simula** corpos físicos. O modo "Físico" é guardado, validado, cobrado e levado nas construções salvas, mas o objeto fica onde foi posto. A simulação (servidor da ilha dono dos corpos e replicação a todos os jogadores) é um passo à parte.

**Construções com hierarquia e pivôs.** O formato salvo (`lifeverse.build`, versão 1) ganhou campos opcionais `parent` (índice do objeto de que a peça depende) e `pivotX/Y/Z`. Construções antigas continuam abrindo. Ao salvar, a árvore do conjunto é guardada; ao colocar, o conjunto volta como um linkset novo com a mesma raiz e os mesmos filhos e pivôs. Isso vale para a colocação feita pelo cliente (com desfazer de um passo só) e para a colocação feita pelo servidor (itens que não podem ser copiados). Ao corrigir isso, a colocação pelo servidor passou a recriar também os grupos, que antes se perdiam. Um pai que não está na construção, ou em outro grupo, é salvo como raiz; um arquivo com pai inexistente, em outro grupo ou com laço é recusado.

**Predefinidos** (aba *Hierarquia*): parede, piso, coluna, escada (6 degraus ligados), mesa (tampo + 4 pés ligados) e cômodo (piso + 4 paredes ligados), feitos de formas simples (`BuildPresets`).

**Prévia antes de colocar.** Escolher um predefinido, ou "Colocar" um item de construção no inventário, mostra uma cópia translúcida de **todas** as peças seguindo o mouse sobre o terreno, com contagem de peças e tamanho na barra de estado. Clique coloca ali (com todas as checagens do servidor); Esc cancela e nada é criado. Construções que não podem ser copiadas não podem ser lidas pelo cliente, então não têm prévia: o servidor as coloca diante do avatar.

Limites: a prévia não gira (a construção é colocada com a orientação com que foi salva); peças de modelo 3D aparecem como caixa na prévia.

Testes: `PhysicsPresetsPackageTests` (regras de física, hierarquia/pivôs no formato, predefinidos válidos, colocação pelo servidor, devolver e colocar de novo, física recusada em todo comando) e um bloco novo em `BuildSmoke`.

## Fase 5 — construção colaborativa, conflitos, desfazer consistente e histórico do conjunto

**O que já protegia e continua valendo.** O servidor confere a revisão de cada comando: uma edição, exclusão ou reorganização feita a partir de uma imagem velha do objeto é recusada por inteiro (`revision_conflict`, 409, com a revisão atual) e não muda nada, nem as outras peças do mesmo comando. A ilha reserva (trava) o que cada pessoa seleciona e recusa comandos sobre o que outra pessoa segura; selecionar uma peça seleciona o conjunto, então o conjunto inteiro fica reservado. Construir lado a lado não dá direito de editar as coisas dos outros (`not_owner`).

**Desfazer/refazer que respeita os outros.** O editor distingue as mudanças confirmadas como suas (resposta com a revisão que pediu mais um) das edições de outras pessoas. Desfazer ou refazer uma peça que outra pessoa alterou **depois** da sua última mudança não escreve por cima do trabalho dela: essa peça fica como está, a mensagem diz quantas ficaram e o passo sai do histórico se nenhuma peça pôde ser desfeita. Se você edita por cima da versão dela, a peça volta a ser sua e o desfazer funciona de novo.

**Desfazer que acompanha o servidor.** Se o servidor recusa o único passo que aguardava resposta (conflito de revisão, limite, permissão), esse passo sai do histórico, e um desfazer/refazer recusado volta para a pilha de onde saiu; assim o histórico descreve o que realmente existe no mundo. Quando há vários passos aguardando ao mesmo tempo, a recusa não diz a qual se refere, então o histórico não é mexido (nenhum passo é descartado por palpite).

**Histórico do conjunto.** Novo endpoint `GET /api/v1/worlds/islands/{ilha}/linksets/{grupo}/audit` e botão "Histórico do conjunto" (aba *Hierarquia*): todas as operações de todas as peças, inclusive as já apagadas ou devolvidas, da mais nova para a mais antiga, com a peça, quem fez e a revisão (criar, alterar, vincular, proteger, apagar, devolver, colocar). Só vê quem pode editar **todas** as peças vivas do conjunto (ou o dono/administrador da ilha), então nunca mostra mais do que o histórico de cada peça mostraria.

Limites: a trava de edição é por peça e dura enquanto a seleção dura (renovada a cada 8 s); uma recusa não identifica o comando, então a limpeza do histórico só acontece no caso sem ambiguidade; o teste de concorrência real (várias conexões ao mesmo tempo no PostgreSQL) fica para a fase 6.

Testes: `CollaborationTests` (edição velha recusada sem perder o trabalho do outro, exclusão velha, reorganização velha recusada inteira, raiz apagada enquanto filho é editado, duas pessoas construindo lado a lado, histórico do conjunto e quem pode lê-lo) e um bloco novo em `BuildSmoke`.

## Fase 6 — testes finais e fechamento

**Testes no PostgreSQL real** (`BuildEditorPostgresTests`, rodam com `LIFEVERSE_TEST_PG` apontando para um servidor onde o usuário cria bancos):
- **Edição colaborativa sob concorrência:** seis editores (donos e administrador) atualizam as mesmas seis peças ao mesmo tempo, com revisão otimista. Ao final, a revisão de cada peça é a inicial mais exatamente o número de mudanças aceitas, o registro de auditoria tem uma linha por mudança aceita, e as edições velhas foram recusadas com `revision_conflict`; nenhum erro inesperado.
- **Restauração após reinício:** um conjunto com raiz, filhos, pivôs, física, uma peça fantasma e uma peça protegida é lido do banco, o pool de conexões é descartado (o processo "reinicia") e a leitura é idêntica campo a campo. Depois o conjunto é devolvido ao inventário, o processo "reinicia" de novo e o item é colocado: volta um linkset novo e válido, com a mesma árvore e os mesmos pivôs, no lugar pedido.
- **Colocações simultâneas:** oito colocações ao mesmo tempo da mesma construção copiável produzem conjuntos completos e válidos; seis colocações ao mesmo tempo de uma construção que não pode ser copiada produzem exatamente uma.

**Colisões** (`BuildCollisionTests`): o cômodo predefinido para o avatar na parede e uma parede fantasma deixa passar; cada configuração de física decide se o objeto bloqueia (sólido, fantasma, gatilho, físico, estático, JSON inválido); o pivô nunca desloca a forma de colisão; uma parede girada continua bloqueando pela face girada.

**Falha encontrada e corrigida pelos testes de carga:** duas primeiras compras simultâneas podiam criar duas vezes a conta da plataforma no livro-razão (sem usuário, então nenhum índice único a impede), e todas as compras seguintes falhavam com "Sequence contains more than one element". A leitura das contas do livro-razão agora usa sempre a mais antiga (`EnsureAccount`, tesouraria de grupo, conta de usuário e conta de terreno), então uma duplicata não quebra mais nada.

**Cobertura das 30 funções do pedido.** Linksets, raiz, edição por peça, proteção e hierarquia (fase 1); pivôs, mover/girar/esticar por números e eixos, espelhar e duplicação múltipla (fase 2); encaixe magnético, grade, seleção por área, alinhar e distribuir (fase 3); colisão por objeto, estático/físico, predefinidos, prévia e salvar/restaurar construções com árvore, pivôs e permissões (fase 4); construção colaborativa, resolução de conflitos, desfazer consistente com o servidor, auditoria e persistência (fase 5, mais os testes desta fase). O editor existente e suas abas foram preservados; as abas novas são *Hierarquia* e *Transformar*.

**Não feito (declarado):** simulação de corpos físicos no mundo (o modo "Físico" é guardado, validado e cobrado, mas o objeto não se move); encaixe magnético em vértices exatos da malha (usa a caixa de contorno); materiais e UV por face e limites/orçamento de terreno já existiam antes deste pedido e não foram alterados; a prévia da construção não gira.

## Correção posterior — mudanças na árvore de uma ilha fazem fila

O teste de carga de conjuntos (`LinksetChaosTests`) falhou uma vez em cada dez ou mais rodadas com "broken_link" quando a máquina estava ocupada: uma ligação a um pai que estava sendo apagado no mesmo instante deixava um conjunto com peça sem pai. Cada operação decidia a partir da árvore como ela estava, e duas operações ao mesmo tempo podiam, cada uma certa sozinha, deixar o conjunto errado juntas. Agora vincular, apagar, devolver, colocar uma construção e criar uma peça já ligada a um conjunto **fazem fila por ilha** no PostgreSQL (trava consultiva até salvar); mover, pintar e demais edições seguem livres. Em 12 rodadas seguidas do teste sob carga não houve mais falha.

## Janela Construir: modos (redesenho, fase 1)

A janela tem cinco modos na barra superior, como na referência: **Foco**, **Mover**, **Editar**, **Criar** e **Terreno**. Cada modo mostra o seu próprio painel de opções (`client/Scripts/BuildEditor.Modes.cs`); só o modo **Editar** mostra as abas de propriedades (Geral, Objeto, Recursos, Textura, Conteúdo).

- **Foco**: foca a câmera na seleção e liga/desliga a câmera de edição.
- **Mover**: Mover/Girar/Esticar, encaixar na grade e mostrar grade.
- **Editar**: Selecionar/Mover/Girar/Esticar, “Editar partes vinculadas”, grade; abas de propriedades.
- **Criar**: grade com todas as primitivas; clicar arma a criação (Shift + clique cria várias; Esc cancela). Sair do modo Criar desarma a colocação.
- **Terreno**: abre as ferramentas de terreno (janela própria, com histórico próprio).
- As caixas dos painéis são espelhos de duas vias das mesmas opções das abas: não há controle decorativo.
- Linha de estado: “Selecionado: N objeto(s) / Prims: N”.
- Objetos bloqueados por outra pessoa ou com edição travada ficam **somente leitura**: todos os campos das abas são desabilitados e voltam ao normal ao trocar a seleção.
- Teste: `BuildSmoke` cobre troca de modos, abas só no modo Editar, contagem de seleção e estado somente leitura.

Pendente nas próximas fases: abas internas redesenhadas (Geral, Objeto, Recursos, Textura, Conteúdo), terreno embutido na própria janela e arrastar para redimensionar (hoje: mover e alternar tamanho compacto/grande).

## Aba Geral: permissões, grupo e venda (redesenho, fase 2)

- Operação nova `properties` (só o proprietário ou um admin da ilha): muda direitos do proprietário e do próximo proprietário (Modificar, Copiar, Transferir), grupo, compartilhamento e venda. Fluxo: cliente → servidor da ilha → API (`BuildProperties.cs`, `BuildOperations.PropertiesAsync`).
- Regras do servidor: direitos só diminuem (`cannot_raise_rights`); o próximo proprietário nunca tem mais que o proprietário; grupo só se a pessoa for membro (`not_group_member`); vender “Cópia” exige o direito de copiar e vender “Original” exige transferir (`no_copy`/`no_transfer`); preço 0 a 10.000.000.
- Compartilhamento e venda ficam em `PermissionsJson` (`groupShare`, `anyoneMove`, `anyoneCopy`, `saleType`, `salePrice`); sem migração.
- **Aplicado de verdade**: “Qualquer um pode mover” deixa outras pessoas moverem e girarem (nunca mudar forma/tamanho); “Compartilhar com o grupo” deixa membros do grupo editarem.
- **Guardado, ainda sem efeito**: “Qualquer um pode copiar” e “À venda” (tipo e preço) são gravados e validados, mas a compra e a cópia por terceiros ainda não existem; ficam para a fase de conteúdo/ferramentas avançadas.
- Cliente: `BuildEditor.Sharing.cs`. Só os campos tocados são enviados; com várias seleções mostra o que todos permitem. Objetos de outra pessoa ficam só para leitura.
- Testes: `BuildPropertiesTests` (5) e `BuildSmoke`.

## Aba Objeto: geometria e física (redesenho, fase 3)

- **Geometria real** (`shared/Building/PrimMesh.cs`, `RoundMesh.cs`): caixa, prisma, cilindro, tubo, cone e pirâmide (perfil varrido por um caminho reto) e agora também **esfera, semiesfera, toro e anel** (perfil varrido por um caminho: polo a polo, ou um círculo/espiral). Todos usam malha de verdade, uma face por parede, com colisão pela mesma malha.
- Parâmetros (todos no `GeometryJson`, validados e limitados no servidor e no gerador): oco, afunilar X/Y, torção, **corte do perfil** (`cutStart/cutEnd`), **corte do caminho** (`pathBegin/pathEnd`: altura de uma caixa, concavidade da esfera, começo/fim do toro), **cisalhar topo** (`shearX/shearZ`), **tamanho do furo X/Y** (`holeX/holeY`; no toro e no anel é a espessura do perfil), **deslocar raio** (`radiusOffset`), **revoluções** (1 a 3), **inclinação** (`skew`, espiral).
- A janela tem o cartão “Forma avançada”: um controle por parâmetro; os que a forma escolhida não usa ficam desabilitados. Valores no padrão não são gravados, então objetos antigos ficam iguais.
- **Física**: Fantasma, Estático/Físico, forma de colisão, massa, atrito e quique já existiam; agora há **Material** (pedra, metal, vidro, madeira, carne, plástico, borracha — preenche atrito e quique) e **Temporário** (conta quase nada no orçamento da ilha).
- Custo: esfera/toro com forma própria custa o que a malha tem de triângulos.
- **Pendente**: o mundo ainda não simula corpos “Físicos” (já era assim), e “Temporário” ainda não apaga o objeto sozinho: só reduz o custo. Torção de toro e cisalhamento de esfera usam a mesma fórmula do perfil, sem uma prévia dedicada.
- Testes: `PrimMeshAdvancedTests`, `BuildPropertiesTests` e `BuildSmoke`.

## Aba Recursos: luz, projetor e caminho flexível (redesenho, fase 4)

Tudo fica no `MaterialJson` do objeto, é validado no servidor (`MaterialAssets.ValidateResources`) e tem efeito de verdade no cliente (`BuildObjectView`).

- **Luz**: cor, intensidade, alcance e **atenuação** (`falloff`, 0,1 a 4: o expoente de queda da luz). A janela agora mostra os valores do objeto selecionado.
- **Projetor de luz**: imagem (uma textura embutida ou a escolhida na biblioteca da aba Textura), **campo de visão** (10 a 120°), **foco** (alcance da projeção) e **ambiência** (a parte da luz que cai fora da imagem). É um `SpotLight3D` com `LightProjector` apontando para a frente do objeto; a luz redonda vira só a ambiência. Só vale com a luz ligada, e a imagem é uma das partes do objeto (direitos e posse valem como numa textura).
- **Caminho flexível**: maciez, gravidade, arrasto, vento, tensão e força X/Y/Z (`flex`). A base fica parada e o topo se inclina e balança (mola amortecida, `FlexSimulation` em `shared/Building/ObjectResources.cs`); maciez 0 é rígido, tensão alta cede menos. Só a malha visível dobra: a colisão não muda.
- Custo: projetor +4 pontos, flexível +3.
- Testes: `ObjectResourcesTests` e `BuildSmoke`.
- **Pendente**: o flexível é uma inclinação do objeto inteiro, não uma dobra ao longo da altura; o vento é igual para todo mundo (mesmo tempo, fase própria por objeto).

## Aba Textura: várias faces e material avançado (redesenho, fase 5)

- **Várias faces**: com a ferramenta Face (5), Shift + clique soma (ou tira) faces do mesmo objeto; “Todas as faces” seleciona todas e “Sem faces” volta ao objeto inteiro. Cor, textura, mapa normal, UV e material avançado valem para todas as faces escolhidas de uma vez. Esfera, semiesfera, toro e anel também têm faces agora (fase 3).
- **Material avançado** (objeto inteiro ou faces, `MaterialBuilder`): **alinhamento** padrão ou planar (a textura em metros, projetada pelo eixo principal da face, sem esticar quando o objeto muda de tamanho), **brilho total** (sem luz nem sombra), **modo alfa** opaco/misturar/cortar com **corte alfa**, **dupla face**, além de rugosidade, metalicidade, opacidade (transparência) e brilho (emissão) já existentes, textura, cor, repetição, deslocamento, rotação e espelhamento.
- O servidor valida as chaves novas (`alphaMode`, `alphaCutoff`, `mapping`, `fullBright`, `doubleSided`) no objeto e em cada face.
- Testes: `ObjectResourcesTests` e `BuildSmoke`.
- **Pendente**: o mapa de metalicidade/rugosidade/emissivo ainda é um valor por alvo (não uma imagem); só o mapa normal aceita imagem.

## Conteúdo e ferramentas avançadas (redesenho, fase 6)

- **Conteúdo**: cada item do objeto mostra ícone, nome, tipo e **permissões** (as letras C, M, T, R do item no seu inventário) e tem ações: **Abrir** (pré-visualiza), **Propriedades** (a janela de direitos do item), **Copiar** (faz uma cópia no inventário, só se o item permitir) e **Transferir** (vai direto à oferta). Além de **Adicionar do inventário**, **Renomear** e **Remover**. As ações são feitas no item do inventário que o objeto referencia, com as mesmas regras; se o item não está na lista carregada, o inventário abre para você achá-lo.
- **Esticar dos dois lados**: ligado, o objeto cresce do centro; desligado, a face oposta ao puxador fica parada.
- **Esticar texturas**: desligado, ao redimensionar um objeto com textura ela vira planar e mantém o tamanho em metros (desfazer restaura).
- **Eixos de referência**: além de “Do mundo” e “Locais”, há “De referência (objeto escolhido)”; “Usar como referência” fixa o primeiro objeto selecionado. Os eixos do gizmo e a **grade** seguem a referência.
- Já existiam e continuam: editar partes vinculadas, mostrar grade, encaixar na grade, vincular/desvincular, duplicar, excluir, desfazer/refazer.
- Correção: o card de Conteúdo tinha ganhado, por engano, os cartões de projetor e caminho flexível; foi desfeito.

## Critério final da janela Construir

Dentro da janela é possível criar uma primitiva (modo Criar), mudar a geometria (aba Objeto, “Forma avançada”), posicionar/redimensionar/girar (Mover/Editar e campos numéricos), aplicar textura e material por face (aba Textura), mudar nome, permissões e venda (aba Geral), luz, projetor e movimento flexível (aba Recursos), guardar e gerir conteúdo, vincular e continuar editando. Controles que a forma ou o direito do objeto não permitem ficam desabilitados; objetos bloqueados ou de outra pessoa ficam só para leitura.

**Ainda não é igual ao Second Life**: compra de objetos à venda e cópia por terceiros (“Qualquer um pode copiar”), remoção automática de objetos temporários, simulação de corpos físicos, terreno embutido na própria janela e redimensionar a janela por arraste.

## Janela Construir no layout da imagem de referência e materiais completos (refeito)

A janela foi refeita de acordo com a imagem de referência (janela estreita “Construir”, barra de modos com ícones, painel de opções do modo, linha “Selecionado: N objeto / Prims: N”, abas **Geral, Objeto, Recursos, Textura e Conteúdo**, campos numéricos em vez de sliders). Conferida por capturas reais (`client/Tests/BuildSmoke.cs` com `LIFEVERSE_CAPTURE_PATH`).

- **Modos**: Foco (controles de câmera), Mover (Mover / Levantar / Rotacionar, grade, precisão, snap de giro), Editar (Selecionar, Esticar, Girar, Selecionar face, Alinhar, Editar partes vinculadas; Alongar ambos os lados, Alongar texturas, Usar eixos locais, Grade; “Opções…” abre Alinhar/Conjuntos/Transformar), Criar (grade de primitivas) e Terreno (abre a janela de terreno; os controles do pincel ainda não estão dentro desta janela).
- **Geral**: nome, descrição, criador, proprietário, grupo, permissões (você pode / próximo proprietário), qualquer um pode mover/copiar, à venda.
- **Objeto**: posição, rotação, tamanho, grade, **Primitiva** (muda o tipo do objeto selecionado), parâmetros da forma (corte do caminho, oco, furo, torção, afinar, cisalhamento, dimple, corte do perfil, raio, revoluções).
- **Recursos**: estados (**Físico, Temporário, Fantasma**, Estático/Dinâmico), luz (cor, intensidade, alcance, atenuação), projetor, **flexibilidade (Flexi)** e **física** (forma de colisão, massa, atrito, restituição, material, visualizar colisão): tudo na mesma aba, como na referência.
- **Textura**: “Selecionar faces” (Todas as faces ou uma face da malha, com os nomes Frente/Esquerda/Trás/Direita/Topo/Base; Shift + clique no mundo com a ferramenta Face soma mais), miniatura da textura aplicada, **Escolher…** (biblioteca: texturas do seu inventário, favoritas, recentes, do sistema e importar imagem), **Limpar**, **Aplicar a todas**, cor, transparência %, repetição (U/V), offset, rotação, inverter, brilho/emissão e o material avançado.
- **Conteúdo**: inserir do inventário, lista com tipo e direitos, abrir, propriedades, transferir, remover, copiar para o inventário, renomear.

### Materiais: PBR completo e Blinn-Phong

- **Modelo** (`shading`): **Blinn-Phong** (mapa difuso/cor, normal, **especular** + cor do brilho, brilho/glossiness e ambiente; a luz é calculada no shader com um termo difuso e um destaque especular) ou **PBR** (cor base, normal, **metal-rugosidade-oclusão**, **emissivo**, números de rugosidade e metal). Sem a chave o objeto se comporta como antes.
- **Cinco mapas** (`texture`/`builtin`, `normalMap`, `specularMap`, `ormMap`, `emissiveMap`): “Mapa” escolhe qual recebe a imagem de “Escolher…”; cada mapa tem a própria repetição, deslocamento e rotação (`mapUv`) ou todos compartilham os do objeto (“Mesmo mapeamento em todos os mapas”).
- **Textura padrão**: todo objeto novo nasce em Blinn-Phong com a textura procedural **Compensado** (`plywood`), cor branca.
- **Retirar textura** (“Limpar”): tira todos os mapas e a cor do alvo (`noTexture` + branco); no objeto inteiro limpa também as faces. Uma face pode ficar limpa com o objeto texturizado, e uma face com textura própria aparece mesmo num objeto limpo.
- Servidor: valida `shading`, `specularColor`, `glossiness`, `environment`, `mapUv` (faixas por campo), `noTexture` e os novos mapas como imagens do inventário (contam como partes do objeto).
- Testes: `ObjectResourcesTests`, `ShaderSmoke` (compila e desenha cada combinação em renderizador real; em headless só valida a lógica) e `BuildSmoke`.

Ainda não: o terreno embutido na janela, o botão “Novo script…” e arrastar itens do inventário para dentro do objeto (Fase de scripts), e simulação de corpos físicos.

## Conteúdo e scripts dentro do objeto

- **Novo script…** (aba Conteúdo): cria um script Lua **dentro do objeto** (modelo “porta ao toque” ou o primeiro modelo do servidor) e abre o **editor**: caixa de código com números de linha e cores de Lua, modelos, **Salvar (Ctrl+S)** e um **console** com as últimas execuções (evento, resultado, instruções e o erro quando houver). O script roda no **servidor da ilha, neste objeto**: eventos `on_start`, `on_touch`, `on_enter`, `on_timer`, `on_chat`, `on_message`; funções `object.say`, `get_position/rotation/scale`, `set_position/rotation/scale`, `move`, `rotate`, `set_color`, `set_material`, `create_timer`, `cancel_timers`, `state_get/state_set`, `send_message`, `get_id`, `get_name` e `lv.time`. A ilha lê os scripts a cada 15 s, então um script salvo passa a valer em até 15 segundos. Salvar também reativa um script parado.
- A lista do Conteúdo mostra **juntos** os scripts do objeto e as outras coisas que ele carrega (objeto, som, textura, animação, modelo, anotação…). Abrir (ou clique duplo), Renomear e Remover (com confirmação) valem para scripts; Remover apaga o script do objeto.
- **Inserir do inventário…** agora oferece também scripts e anotações; e **dá para arrastar qualquer item do inventário para a lista** do Conteúdo.
- Servidor: `GET .../scripts/{id}` (com o código), `GET .../scripts/{id}/executions` (últimas 20) e `DELETE .../scripts/{id}` (só em objeto que pode ser modificado); testes em `ScriptEditorApiTests` e no `BuildSmoke`.
- **Limites**: `play_sound` ainda não toca (não há áudio de assets no motor de scripts), então sons do conteúdo não tocam por script; scripts do inventário inseridos como item ficam guardados como referência e **não rodam** (rodam os criados com “Novo script…”); a lista de funções é a do motor atual.
