# Editor de construção 3D

O modo Construir abre pelo botão **Construir** da barra inferior, por Conteúdo > Construir ou pela tecla `B`. O editor suporta cubo, esfera, cilindro, cone, plano, prisma, tubo, anel e pirâmide.

## Como usar

- **Abrir:** botão **Construir** da barra inferior, Conteúdo > Construir ou a tecla `B` (também `Ctrl+B`). Tudo em português.
- **Janela:** barra de ferramentas (Selecionar, Mover, Girar, Esticar; criar, copiar, colar, duplicar, excluir, desfazer, refazer; vincular e desvincular) e as abas **Geral** (nome, descrição, criador, proprietário, UUID, escolha da primitiva a criar), **Objeto** (posição, rotação e tamanho numéricos, passo de movimento, oco, afunilamento X/Y, torção, fantasma) e **Textura** (cor, rugosidade, metalicidade, opacidade, brilho; copiar e colar propriedades). Digite os valores e pressione Enter ou **Aplicar**; valores fora dos limites são cortados (tamanho 0,01 a 64; oco 0 a 0,95; afunilamento -1 a 1; torção -180 a 180).
- **Primitivas (11):** caixa, esfera, cilindro, prisma, toro, tubo, anel, pirâmide, cone, semiesfera e plano. **Criar** coloca a primitiva escolhida onde a câmera aponta.
- **Selecionar:** clique em um objeto; Shift soma ou tira da seleção. Objetos vinculados são selecionados juntos.
- **Alças 3D:** `1` Selecionar (sem alças), `2` Mover (setas X vermelho, Y verde, Z azul), `3` Girar (anéis), `4` Esticar (cubos nos eixos) e `5` Face. Mover usa o passo da aba Objeto e girar usa o passo de giro (5° por padrão). `Esc` cancela um arrasto.
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

## Garantias do servidor

Cada alteração é enviada ao servidor da ilha com a revisão do objeto (`expectedRevision`); o servidor valida permissão, limites (escala `0.01..64`, cota de objetos, 20 operações por segundo por conexão) e responde com o estado oficial, que é replicado a todos. Se outra pessoa alterou o objeto antes, a edição é recusada (conflito de revisão) e o objeto volta ao estado oficial. Desfazer envia a alteração inversa com a revisão atual, então também está sujeito à autorização e aos conflitos. As operações ficam em `world.build_operations`.

Materiais aceitam cor, transparência, emissão, rugosidade e metallic; a física aceita colisão, trigger e camadas.

## Arquitetura

- `BuildEditor` (cliente): seleção, histórico, copiar/colar, agrupamento e envio das operações.
- `BuildGizmo` (cliente): alças 3D com camada de física própria; arrastar usa `GizmoMath`.
- `shared/Building/GizmoMath.cs`: geometria dos arrastos (ponto mais próximo do eixo, plano de rotação, ângulo, snap), testada sem a engine.

## Limitações

- Ainda não existem (etapas seguintes do plano): geometria procedural para esfera, toro e semiesfera (cortes e torção), encaixe por vértice, aresta ou superfície, duplicar arrastando e em sequência, texturas gratuitas compartilhadas, flexibilidade, partículas e animação de textura, miniatura própria para construções salvas, compartilhar ou transferir construções entre jogadores (copiar, retirar e devolver com permissões), executar scripts guardados no conteúdo, atalhos personalizáveis, bloqueio de edição por objeto e limites de terreno por custo.
- Nome e descrição passam a ser gravados: o servidor da ilha descartava esses campos (e permissões, conteúdo e pai) antes de enviar a operação à API.

- Não há seleção por retângulo, pivô personalizado nem alças em eixos locais para mover/girar (só para escalar um objeto). A coloração por face só vale para as formas procedurais.
- Desfazer pode ser recusado se outra pessoa mexeu no mesmo objeto depois.
- Simulação física dinâmica ainda não existe.
- Verificado por testes unitários da geometria e por um teste no Godot com um "servidor" em memória que impõe revisões; não testado com dois jogadores reais.
