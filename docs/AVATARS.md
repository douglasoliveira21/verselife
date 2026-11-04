# Avatar system

## Placeholder and licensing

The Phase 02 avatar is an original procedural placeholder assembled at runtime from Godot `BoxMesh` and `SphereMesh` primitives. No downloaded model, texture, motion-capture clip, or proprietary asset is included. It is clearly a replacement target for a production skinned GLTF.

The standard skeleton declares root, hips, spine, chest, neck, head, paired upper/lower arms and hands, and paired upper/lower legs and feet. The segmented visual hierarchy mirrors those regions. Because pieces are rigid, it avoids skin-weight deformation while preserving stable animation and replacement contracts.

## Customization catalog

- Bases: Classic (`human_a`), Athletic (`human_b`)
- Hair: short, bob, mohawk, bald
- Tops: basic tee, basic jacket
- Bottoms: basic pants, basic shorts
- Shoes: sneakers, boots
- Accessories: none, round glasses, basic hat
- Skin/hair colors and camera sensitivity/invert-Y preferences

Face, height, and proportion settings are reserved in the appearance schema but intentionally not exposed until a production skinned model supports them safely.

## Animation states

`Idle`, `Walk`, `Run`, `Jump`, `Fall`, `Landing`, `FlyIdle`, `FlyMove`, `Sit`, `StandUp`, `Swim`, `Dance`, and `Wave` are real AnimationLibrary clips connected through an AnimationTree state machine with cross-fades. Physics resolves a logical state through the shared state resolver; the visual rig consumes that result.

## Controls and interactions

Movement is camera-relative and uses `CharacterBody3D`, floor snapping, a 50-degree floor angle, collision, gravity, jump, run, flight, landing, water volumes, and surface speed metadata. `SpringArm3D` prevents the third-person camera crossing walls. First-person, cursor release, sensitivity, inversion, object raycast, contextual seating, stand-up, wave, and dance are implemented.

## Avatar 2.0 — fase 1: corpo, rosto, roupas por camada e looks (entregue)

O Avatar 2.0 será entregue em fases. Esta é a primeira; o corpo continua sendo o humanoide procedural (peças rígidas), então tudo aqui é feito sobre ele.

**Roadmap das próximas fases** (nada disto existe ainda):
2. Importar avatares GLB/glTF: validar e mapear esqueleto, ossos, skinning e proporções.
3. Roupas com rigging e deformação, detecção de incompatibilidade roupa × esqueleto, acessórios presos a ossos.
4. Importar animações compatíveis, Animation Override e biblioteca de animações (andar, correr, repouso, dança, sentar, gestos).
5. Itens do inventário global como roupas (propriedade e permissão validadas no servidor, economia), LOD, carregamento progressivo por distância e orçamento de renderização por avatar.

**Regras compartilhadas** (`shared/Avatar/AvatarRules.cs`, usadas pela API, pelo servidor de ilha e pelo cliente):
- **Corpo:** 7 controles com limites — altura, ombros, tronco, braços, pernas, cabeça, quadril. **Rosto:** 7 — olhos (tamanho e distância), nariz, boca, maxilar, sobrancelhas, largura da cabeça. A API **recusa** chave desconhecida, não numérico ou fora do limite; o cliente **lê com tolerância** (limita ao intervalo). Só o que difere do padrão é gravado.
- **Roupas por camada e slot:** `top`, `jacket` (camada de fora, exige um top por baixo), `bottom`, `shoes`, `hat`, `glasses`, `neck`, `ears`, `gloves`, `back` e o `accessory` antigo. Catálogo novo: vestido (cobre `bottom`), saia, casaco longo, colete, sandálias, boné, gorro (esconde o cabelo), óculos quadrados, colar, cachecol, argolas, luvas, mochila.
- **Conflitos:** slot duplicado, item desconhecido, slot obrigatório ausente, item num slot coberto por outro, "coberto" sem quem cubra, camada de fora sem top. A API devolve a razão; o editor aplica `Resolve` (a escolha mais recente vence: pôr calça tira o vestido, pôr vestido cobre a calça).

**Looks salvos** (migração `Phase39AvatarLooks`, tabela `identity.avatar_looks`): até 20 por avatar, nome de 1–40 caracteres único por avatar. Rotas em `/api/v1/users/me/avatar/looks`: listar, salvar o atual, aplicar, duplicar ("X (cópia)"), renomear, excluir, compartilhar (código de 8 caracteres) e parar de compartilhar, importar por código. O look guarda a aparência inteira (menos o nome e a câmera, que são da pessoa) e **é validado de novo ao aplicar e ao importar**, então um look antigo ou adulterado que fure as regras nunca é aplicado. Itens ainda são só os do catálogo (sem inventário, ver fase 5), então compartilhar não dá acesso a nada que se pague.

**Editor** (tela do personagem): controles de corpo e rosto, um menu por slot extra, gerenciador de looks (lista, nome, Salvar/Aplicar/Duplicar/Excluir, Compartilhar com código copiado para a área de transferência, Importar). A prévia 3D já girava e dava zoom. Também foram adicionados cabelos "longo" e "rabo de cavalo" ao menu, que a API já aceitava.

**Sincronização e persistência:** a aparência continua indo ao servidor de ilha na admissão (os outros jogadores a veem ao entrar ou reconectar); mudar o visual dentro de uma ilha ainda exige reconectar. Tudo é gravado no perfil do avatar.

**Testes:** `AvatarLooksTests` (23: limites dos controles, leitura tolerante, conflitos de roupa, `Resolve`, salvar/aplicar/duplicar/renomear/excluir, limite de 20, isolamento entre pessoas, compartilhar/importar, look que quebra as regras) e `Avatar2Smoke` no cliente (corpo e rosto desenhados e com os pés no chão, roupas, conflitos no editor, gerenciador de looks).

**Limitações desta fase:** não há importação de GLB, rigging, animações customizadas, LOD nem inventário; o visual não muda para os outros sem reconexão; os controles do rosto são peças simples do boneco procedural; não verifiquei visualmente o editor com capturas de tela.

## Avatar 2.0 — fase 2: corpo personalizado em GLB (entregue)

**O que a pessoa faz:** no editor do personagem, seção "Corpo personalizado (GLB)": **Importar modelo…** (um único `.glb` com tudo dentro), **Salvar modelo de referência…** (um humanoide válido de 1,78 m em pose de T, com os ossos que o jogo espera), lista dos modelos (até 5), **Usar este corpo**, **Corpo padrão** e **Excluir**. Se o servidor recusa o arquivo, a janela mostra cada motivo em português (✖ erro, • aviso).

**O que o servidor confere** (`shared/Avatar/AvatarModelValidator.cs`, nada é carregado em engine): glTF 2.0 legível, sem links para outros arquivos nem extensões que o jogo não suporta (malha comprimida, por exemplo); **um** esqueleto usado pelas malhas; ossos reconhecidos por `BoneMapper` em qualquer convenção (Blender `UpperArm.L`, Mixamo `mixamorig:LeftArm`, VRM `J_Bip_L_UpperArm`, padrão do jogo) para os 16 ossos obrigatórios (peito é opcional: a coluna o substitui); **hierarquia** de humanoide (cabeça sob pescoço sob peito sob quadril, braço → antebraço → mão, perna → canela → pé); esqueleto **em pé no eixo Y**; **proporções** (pernas 25–65% da altura, braços 15–60%, ombros 5–50%); **skinning**: toda malha ligada ao esqueleto tem `JOINTS_0`/`WEIGHTS_0`, índices dentro do esqueleto e pesos somando 1; orçamentos (60 mil triângulos, 90 mil vértices, 128 ossos, 16 materiais, 8 texturas, 20 MB). Modelo em centímetros não é recusado: recebe um fator até 1,8 m. Avisos: animações do arquivo são ignoradas, malha solta, influências demais, assimetria.

**Armazenamento:** `identity.avatar_models` (migração `Phase40AvatarModels`): o GLB guardado pelo hash SHA-256, o relatório e o mapa de ossos; `Avatar.CustomModelId` guarda o corpo em uso. Só o dono escolhe, apaga e lê o relatório; o mesmo arquivo enviado duas vezes é um modelo só. Rotas: `GET/POST /api/v1/users/me/avatar/models`, `POST …/{id}/use`, `DELETE …/use`, `DELETE …/{id}`, `GET …/{id}/report`, e `GET /api/v1/avatars/models/{id}/file` (qualquer pessoa logada; é como todos veem o corpo dos outros).

**No jogo:** o avatar leva `customModelId`, `customModelHash`, `customModelScale` e o mapa de ossos na resposta do perfil e na admissão do servidor de ilha, então quem entra na ilha baixa o mesmo arquivo, **confere o hash** e usa um cache local. Enquanto baixa, ou se algo falha, aparece o corpo procedural. O modelo é importado pelo engine, virado para -Z, levado a 1,8 m e posto com os pés no chão. Ele **não traz animações**: o corpo procedural continua animado, escondido, e a cada quadro as rotações das suas juntas são copiadas para os ossos do modelo (quadril, coluna, braços e pernas), no espaço do próprio modelo; assim andar, correr, sentar, dançar e os gestos funcionam em qualquer esqueleto humanoide.

**Testes:** `AvatarModelTests` (57: nomes de ossos, modelo limpo nas três convenções, cada defeito, arquivos quebrados, dano aleatório sem exceção), `AvatarModelsServiceTests` (6: guardar com relatório, recusar, duplicatas e limite, só o dono usa, apagar, resposta enviada aos outros), `AvatarCustomSmoke` (importa o GLB no engine nas três convenções, 1,8 m, pés no chão, frente certa, pernas balançam em oposição ao correr, hash diferente, download que falha, arquivo ilegível, resposta atrasada ignorada) e a seção de corpos no `Avatar2Smoke`.

**Limitações:** roupas, cabelos e acessórios do catálogo **não** aparecem no corpo personalizado (fase 3: roupas com rigging e acessórios presos a ossos); os controles de corpo e rosto não deformam o modelo (só o procedural); mãos e pés seguem a animação apenas pelos ossos pai (não há animação própria de dedos); a pose de repouso esperada é a de T/A com os eixos do glTF (Y para cima); looks salvos ainda não guardam qual corpo estava em uso; trocar de corpo dentro de uma ilha exige reconectar; os testes de renderização foram numéricos, sem captura de tela do avatar importado.

## Avatar 2.0 — fase 3: roupas com esqueleto e acessórios presos a ossos (entregue)

**Dois tipos de item** (`shared/Avatar/WearableValidator.cs`), ambos em um único `.glb`:
- **Roupa com esqueleto** (camiseta, calça, casaco, sapato, luva): malha ligada ao esqueleto humanoide padrão. Vale só em **corpo personalizado**.
- **Peça rígida** (chapéu, óculos, colar, brincos, mochila): sem esqueleto, presa a **um osso**. Vale no corpo padrão e nos personalizados.

O arquivo diz para que serve nos extras do asset: `{"lifeverse": {"slot": "top", "layer": "base", "covers": ["bottom"], "hidesHair": false, "bone": "head", "offset": [x,y,z], "rotation": [x,y,z], "scale": 1}}`. A camada é `base` ou `outer` (casaco); `covers` diz que slots um vestido cobre; `bone`, `offset` (metros, do osso padrão), `rotation` (graus) e `scale` valem para peça rígida. O editor salva um **exemplo de roupa** e um **exemplo de acessório** já com isso preenchido.

**Detecção de incompatibilidade roupa × esqueleto** (o servidor recusa e explica):
- peso em ossos que **não existem no esqueleto do jogo** (`incompatible_bones`, citando os nomes), em qualquer convenção de nome (Blender, Mixamo, VRM, padrão);
- peso **fora da região do slot** (`wrong_region`: uma calça feita como camiseta, uma camiseta usada como calça; um vestido que cobre as pernas pode ter peso nelas);
- esqueleto da roupa **montado de outro jeito** que o do jogo (`skeleton_mismatch` acima de 25 cm de desvio, aviso `loose_fit` acima de 8 cm; roupa em centímetros é reescalada com aviso);
- hierarquia de ossos que não é a de um humanoide; pesos que não somam 1; índice de osso inexistente; slot que não combina com o tipo (roupa com esqueleto para chapéu, peça rígida para camiseta); osso de peça rígida desconhecido; posição, escala ou tamanho absurdos (peça de mais de 1,5 m);
- orçamentos por item: 20 mil triângulos, 30 mil vértices, 4 materiais, 4 texturas, 10 MB. Um **conjunto** de itens personalizados soma no máximo 60 mil triângulos.

**Servidor** (migração `Phase41AvatarWearables`, tabela `identity.avatar_wearables`): enviar (`POST /api/v1/users/me/avatar/wearables`, multipart), listar, apagar (tira o item do avatar e descobre o que ele cobria), relatório, e download por qualquer pessoa logada (`GET /api/v1/avatars/wearables/{id}/file`). Um item se veste como `custom:{id}`. Ao salvar a aparência (e ao aplicar um look) o servidor só aceita item **seu**, **no slot para o qual foi feito**, roupa com esqueleto só com corpo personalizado, sem conflitos de camada e cobertura (as mesmas regras do catálogo, agora também para itens personalizados) e dentro do orçamento do conjunto. Um look com item de outra pessoa **não é aplicado**. A resposta do perfil e a admissão no servidor de ilha levam, para cada item vestido, o hash, o tipo, o osso e a posição, então quem vê o avatar baixa e confere o mesmo arquivo.

**No jogo:**
- Peças rígidas seguem o osso: no corpo padrão, a parte animada correspondente do boneco; no corpo personalizado, o osso real do modelo. A posição parte do osso padrão, então um chapéu com `offset [0, 0.15, 0]` fica no topo da cabeça nos dois corpos, vira com o avatar e acompanha a animação.
- Roupas com esqueleto: os ossos da roupa são casados com os do avatar pelo nome (convenções diferentes entre roupa e corpo funcionam) e a pele é refeita com a pose de descanso do próprio avatar, então a roupa dobra com o corpo.
- **Correção da fase 2:** corpos personalizados modelados em T ou A ficavam de braços abertos para sempre. Agora cada braço (e antebraço) é primeiro levado para baixo, e as animações do jogo (que partem de braços caídos) são aplicadas sobre isso; mãos de peça rígida e luvas acompanham.
- Editor: seção "Roupas e acessórios personalizados" (importar, exemplos, lista, Vestir, Tirar, Excluir). O item entra no menu do seu slot com "★" e segue as mesmas regras de conflito (vestido personalizado cobre a calça; escolher bermuda tira o vestido). Os motivos de recusa aparecem em português.

**Testes:** `WearableTests` (33), `AvatarWearablesServiceTests` (11), `AvatarWearablesSmoke` (peça na cabeça, que vira com o avatar e acompanha a corrida; roupa recusada no corpo padrão; roupa e peça na mão em três combinações de nomes corpo × roupa; a pele refeita com um bind por osso e nomes do avatar; a peça seguindo a mão com os braços balançando; hash, download e resposta atrasada), a seção de itens no `Avatar2Smoke` e a checagem de braços caídos no `AvatarCustomSmoke`.

**Limitações:** roupas e acessórios **do catálogo** continuam sem aparecer em corpo personalizado; a roupa não se estica para corpos de proporções muito diferentes (ela dobra com os ossos mas não é refeita, por isso o aviso de ajuste); malhas de roupa com morph targets (blendshapes) não são tratadas; sem inventário/loja (fase 5): os itens são do próprio avatar, e compartilhar um look não passa o item; hair cards e cabelo personalizado ainda são só os do catálogo; nada foi verificado visualmente em captura de tela, apenas por números nos smokes.

## Avatar 2.0 — fase 4: animações, Animation Override e atualização ao vivo (entregue)

**Animation Override.** Para cada estado do avatar (parado, andar, correr, pular, cair, aterrissar, voar parado, voar, sentar, sentar no chão, levantar, nadar, dançar, acenar, aplaudir, apontar, cumprimentar, rir) a pessoa escolhe o que toca no lugar do padrão, no editor (seção "Animações": uma lista com **Ver** em cada estado, **Salvar animações** e **Tudo padrão**). Duas origens:
- **Biblioteca do jogo** (`builtin:nome`, vale em qualquer corpo): parado relaxado/atento, andar alegre/cansado, correr em disparada, sentar relaxado, dançar balançado/em giro, acenar grande, reverência, gargalhada.
- **Arquivos próprios** (`custom:{arquivo}#{animação}`, vale em **corpo personalizado**): GLB com animações. Em corpo padrão toca o padrão e o editor diz por quê.

**Importar animações** (`shared/Avatar/AnimationClips.cs`): um `.glb` de até 5 MB com até 20 animações. O servidor confere: glTF legível, trilhas de rotação em ossos reconhecidos em qualquer convenção de nome (Blender, Mixamo, VRM, padrão), **pelo menos 8 ossos do humanoide** por animação, duração de 0,2 a 30 s, tempos crescentes, rotações unitárias, até 60 mil chaves, nomes diferentes. Avisos (não recusam): movimento de raiz (o jogo toca no lugar), animação que não termina onde começa (vai dar pulo ao repetir), trilhas de ossos que o jogo não usa (dedos), escala. O editor salva um **arquivo de exemplo**.

**Como toca em um corpo personalizado:** a animação é lida do próprio arquivo e amostrada a cada quadro; para cada osso padrão o jogo calcula quanto ele girou em relação ao descanso, no espaço do modelo, e aplica isso nos ossos do avatar, então as proporções e os eixos dos ossos do corpo não importam. O quadril sobe e desce como o arquivo manda. Estados de tocar uma vez (pular, aterrissar, levantar) seguram a última pose; os outros repetem. Peças presas a ossos (chapéus, luvas) acompanham.

**Servidor** (migração `Phase42AvatarAnimations`: tabela `identity.avatar_animation_files` e `AnimationOverridesJson` na aparência): enviar, listar, apagar (os estados que usavam o arquivo voltam ao padrão), relatório e download por hash para quem vê o avatar; `PUT /api/v1/users/me/avatar/animation-overrides` troca o mapa inteiro e só aceita estados reais, alternativas da biblioteca daquele estado e animações que existem em arquivos **do próprio avatar**; `GET …/animation-library` lista a biblioteca. O texto que descreve o avatar (usado na admissão e na atualização) agora sai de um único construtor, `AvatarAppearanceJson`.

**Atualização ao vivo entre jogadores (protocolo 8).** Mudar roupa, corpo, animações ou o corpo personalizado enquanto está numa ilha: o cliente avisa `RefreshAppearance` (sem mandar nada), o **servidor da ilha pergunta à API** (`GET /api/v1/platform/avatars/{id}/appearance`, só com a chave de serviço) como aquele avatar é agora, guarda e envia `AppearanceChanged` a todos. Ninguém pode mostrar o que não foi conferido, um jogador que cai e volta recebe o visual atual, e há limite de uma atualização a cada 3 s por jogador. Todo pedido que muda o avatar na API (salvar, aplicar look, escolher corpo, vestir, animações) já avisa a ilha. **Publique a API e os servidores de ilha juntos.**

**Segurança:** a rota `land-sync` dos servidores de ilha (acesso, direitos de relevo, teleporte) estava aberta a quem soubesse o id da instância; agora exige a chave de serviço, como as outras rotas de plataforma.

**Testes:** `AnimationClipTests` (21: aceitar nas três convenções, várias animações, quanto cada osso girou, herança do quadril, bob, recusas, arquivos quebrados, dano aleatório sem exceção), `AvatarAnimationsServiceTests` (9: arquivos, limites, regras do mapa de overrides, apagar, texto enviado a todos, atualização), `AvatarAnimationsSmoke` (biblioteca com animação realmente diferente, fallback de nome desconhecido, animação própria com amplitude do arquivo, bob do quadril, pé que de fato anda, volta ao repouso, hash errado, download que falha, animação inexistente, corpo padrão, outro jogador vendo a mudança na hora, dado quebrado ignorado, reconexão com o visual novo), a seção de animações no `Avatar2Smoke` e `ProtocolConsistencyTests` com o protocolo 8.

**Limitações:** animações próprias só em corpo personalizado; a tradução da pose usa o descanso do corpo (animações feitas para outro descanso, como braços em A num corpo em T, ficam um pouco diferentes); o bob do quadril usa as unidades do arquivo; não há mistura (blend) entre uma animação própria e outra além do corte de estado; looks salvos ainda não guardam overrides; ainda não há tela dentro do mundo para mudar o visual (o editor é uma tela separada, e a atualização ao vivo vale para quando o cliente a pedir); sem captura de tela, só medições nos smokes.

## Avatar 2.0 — fase 5: inventário, economia e orçamento de renderização (entregue)

**Roupas e acessórios são itens do inventário.** Ao enviar um item (fase 3), o servidor passa a criar também um **asset** (categoria `clothing` ou `accessories`, com a versão e o hash do arquivo) e um **item de inventário** para quem o fez (migração `Phase43WearableAssets`: `AvatarWearable.AssetId`). Isso o liga a tudo que já existia: aparece no inventário, pode ser copiado, dado e **vendido no marketplace** (a publicação exige um asset seu, que agora existe; a compra entrega ao comprador um item de inventário do mesmo asset). Quem **tem o item no inventário pode vesti-lo**: o servidor recusa o que a pessoa não tem (outra pessoa, item apagado, outro slot). Itens enviados antes da fase 5 ganham o asset na primeira vez que aparecem. Enviar de novo o mesmo arquivo depois de apagar traz o mesmo asset de volta.

**Apagar** tira o item do inventário **de quem apagou** e do avatar dessa pessoa; quem comprou continua com o dele; quando ninguém mais o tem, o item deixa de existir.

**Consistência sem depender do marketplace:** se um item some do inventário de quem o veste (vendido, transferido, reembolsado, apagado por administrador), o avatar é acertado na próxima vez que alguém olha para ele (perfil, salvar, admissão numa ilha, atualização ao vivo): o slot volta a uma escolha simples e o que o item cobria é descoberto. Nada mais muda, e não fica mudando.

**Orçamento de renderização** (`shared/Avatar/AvatarDetailPlanner.cs`, matemática pura e determinística): para cada avatar de outro jogador decide quanto é desenhado:
- **Completo** (corpo e itens personalizados, animações próprias), **Reduzido** (corpo personalizado com as animações do jogo, atualizado em quadros alternados), **Simples** (o boneco padrão) e **Oculto** (não desenhado nem processado).
- Por distância (25, 60 e 120 m) com **histerese** (ninguém pisca na borda de uma faixa), por visibilidade (quem está fora da tela nunca é completo) e por um **orçamento de triângulos** da cena (250 mil) e de avatares completos (10) e reduzidos (24): os mais próximos são atendidos primeiro e, quando o orçamento acaba, os mais distantes perdem detalhe primeiro. O seu avatar é sempre completo. O custo vem do servidor (triângulos do corpo e de cada item, na descrição do avatar).

**Carregamento progressivo:** nada caro é baixado nem montado antes de o nível pedir. A distância "simples" não baixa nada; "reduzido" baixa só o corpo; "completo" baixa itens e animações próprias. Voltar de "oculto" não baixa nada de novo; afastar-se de novo troca pelo boneco simples. O `NetworkManager` reavalia a cada 0,25 s (`UpdateDetail`) e o overlay de desempenho (`LIFEVERSE_SHOW_METRICS=1`) mostra quantos avatares há em cada nível.

**Limites por avatar no servidor:** corpo personalizado até 60 mil triângulos, itens personalizados somando até 60 mil (`OutfitTriangleBudget`), no máximo 5 corpos, 30 itens e 20 arquivos de animação.

**Testes:** `AvatarInventoryTests` (10: asset e inventário de quem faz, categorias, comprador veste e outros não, apagar não tira do comprador, item que sai do inventário é tirado do avatar e não fica mudando, reenviar traz o asset de volta, itens antigos), `AvatarDetailPlannerTests` (16: faixas, histerese, fora da tela, orçamento, limites, estabilidade e independência da ordem) e `AvatarDetailSmoke` (nada baixado de longe, corpo ao chegar perto, itens e animações de perto, ocultar e voltar sem baixar nada, o reduzido ainda corre, a cena inteira decide por distância e orçamento e ninguém pisca ao andar na borda). Os testes de loja usam o mesmo formato de item de inventário que a compra do marketplace cria; a compra em si não foi exercitada de ponta a ponta.

**Limitações:** não há simplificação das malhas importadas (LOD de geometria): o nível "simples" troca pelo boneco padrão em vez de uma versão reduzida do modelo; a tela do marketplace e a do inventário não foram alteradas nem verificadas para esses assets (eles usam as categorias `clothing` e `accessories` que o inventário já tinha); o orçamento é por cena e por distância, sem medir o tempo de quadro; looks salvos continuam sem guardar corpo, itens personalizados e animações; cabelo e olhos personalizados não existem (só os do catálogo e peças presas à cabeça).

## Avatar 2.0 — looks completos: corpo e animações (entregue)

Os looks salvos agora guardam também **qual corpo personalizado estava em uso** e **as animações escolhidas por estado** (junto com sliders, camadas e itens, que já eram guardados; itens personalizados vestidos continuam valendo como antes). Sem migração: o instantâneo é JSON e ganhou campos opcionais (`withBodyAndAnimations`, `customModelId`, `animationOverridesJson`).

- **Salvar**: tudo isso entra no look; a lista mostra "corpo próprio" e "N animações" (`full`, `customBody`, `animationOverrides` em `GET /avatar/looks`).
- **Aplicar**: o corpo e as animações voltam **na medida em que ainda existem e são da pessoa**. Um corpo apagado ou de outra pessoa não é aplicado (o avatar fica com o corpo padrão), e uma animação cujo arquivo foi apagado, ou que não é mais válida para o estado, é descartada; o resto do look entra normalmente e a resposta traz `lookSkipped` (`"body"`, `"animation:Dance"`), que a tela mostra como "Ficaram de fora…". Roupa com esqueleto continua exigindo corpo personalizado: se o corpo do look não está mais disponível e o look veste roupa assim, o look é recusado (`look_invalid`). Como todo pedido que muda o avatar, avisa a ilha e os outros jogadores.
- **Looks antigos** (salvos antes) não têm esses campos e continuam deixando o corpo e as animações como estão.
- **Compartilhar/importar**: o código **não leva** corpo nem arquivos de animação de quem compartilhou (são dele): a cópia importada se comporta como um look antigo e não mexe no corpo nem nas animações de quem a veste. Duplicar mantém tudo.
- **Testes**: `AvatarLooksFullTests` (9: salvar e restaurar, voltar ao corpo padrão, corpo apagado, animação apagada, corpo de outra pessoa forjado, look antigo, importação sem corpo, duplicar) e a extensão do `Avatar2Smoke`.

## Avatar 2.0 — cabelo e olhos próprios (entregue)

A pessoa pode trazer o **próprio cabelo** e os **próprios olhos**, como peças rígidas presas à cabeça que **trocam** os do catálogo. Não há tabela nem rota nova: são itens personalizados (`POST /api/v1/users/me/avatar/wearables`, mesmas regras de dono, inventário, hash e orçamento) em dois slots novos, `hair` e `eyes`, que entram em `AvatarRules.Slots` (com a entrada "do catálogo" `none`). Em corpo padrão e em corpo personalizado.

**O que o arquivo diz** (`extras.lifeverse`): `{"slot": "hair", "bone": "head", "offset": [0, 0.12, 0], "tint": "hair"}` ou `{"slot": "eyes", "tint": "eyes", ...}`.
- Só **peça rígida** (roupa com esqueleto para cabelo ou olhos dá `slot_kind`) e só no osso **cabeça** (`wrong_bone`).
- `tint` é opcional: `"hair"` num cabelo (ou `"eyes"` em olhos) faz a peça **tomar a cor de cabelo (ou de olhos) escolhida no editor**, multiplicando a cor dos materiais; sem `tint` o arquivo mantém as próprias cores. Qualquer outro valor, ou em outro slot, é `bad_tint`.
- Cabelo sempre **esconde o cabelo do catálogo** (`hidesHair` é forçado). Olhos próprios escondem os olhos padrão (`AvatarRules.EyesVisible`). Olhos têm no máximo 0,4 m; o cabelo, como toda peça, 1,5 m.
- Chapéu, óculos etc. continuam valendo por cima.

**No jogo.** A peça é baixada, conferida pelo hash e fixada ao osso da cabeça, como qualquer peça rígida; o cabelo e os olhos do catálogo só somem **onde a peça é desenhada** (avatares perto, nível de detalhe completo). De longe, onde itens próprios não são baixados, o avatar mostra o cabelo e os olhos do catálogo. A cor vem do cabelo/olhos do avatar, então quem vê o outro jogador vê a cor certa (o servidor manda `tint` com os itens vestidos).

**Editor.** Dois menus novos, "Cabelo seu (peça própria)" e "Olhos seus (peça própria)", e dois exemplos para salvar ("Exemplo de cabelo…", "Exemplo de olhos…") já com os extras preenchidos. Looks salvos guardam essas peças como qualquer item personalizado; apagar a peça tira do avatar.

**Testes:** `CustomHairEyesTests` (19: slots, cabelo e olhos aceitos, osso, tint, tamanho, não-roupa, `EyesVisible`, só itens seus no slot certo, chapéu por cima, o que os outros recebem, look, apagar), a seção nova do `AvatarWearablesSmoke` (peças substituem os do catálogo, tomam as cores, ficam na cabeça, voltam ao catálogo de longe) e a do `Avatar2Smoke` (menus, vestir, exemplos válidos).

**Limitações:** os controles de rosto (tamanho e distância dos olhos) não mexem na peça de olhos; se o arquivo do cabelo ou dos olhos falha ao baixar, o avatar fica sem cabelo/olhos até a peça chegar (o do catálogo não volta sozinho); a peça não acompanha expressões nem piscar; nada foi verificado em captura de tela, só por números.

## Avatar 2.0 — editor de aparência dentro do mundo: o espelho (entregue)

Antes, mudar o visual levava para outra tela (a do personagem), saindo da ilha. Agora há o **espelho**: no menu **Eu → "Espelho: mudar o visual aqui…"** abre-se uma janela ao lado do avatar, **sem sair da ilha**, e a câmera vai para a frente dele. A tela completa continua no menu ("Editar aparência (tela completa)…") e pelo botão **Editor completo…** da janela.

**Câmera.** `PlayerController.EnterMirror/SetMirrorView/TurnMirror/ExitMirror`: a câmera fica na frente do avatar, em **Rosto** (1,15 m, na altura da cabeça) ou **Corpo inteiro** (3,1 m), com **◀ Girar / Girar ▶** (45° por vez), botão direito para orbitar e roda para aproximar (0,7 a 5 m). Enquanto o espelho está aberto o avatar **fica parado** (andar, correr, pular e voar, inclusive por atalhos digitados na janela, são ignorados e o destino de caminhada é cancelado) e as teclas de trocar câmera não valem. Ao fechar, a câmera volta ao modo, distância e ângulo de antes.

**Janela** (`AppearanceWindow`): abas **Corpo** (corpo base, os 7 controles de corpo, cor da pele), **Rosto** (os 7 controles de rosto, cabelo, barba, cor do cabelo e dos olhos), **Roupas** (um menu por slot: o catálogo e os itens próprios com ★, inclusive cabelo e olhos próprios; as mesmas regras de conflito e cobertura da tela completa, via `AvatarRules.Resolve`) e **Looks** (lista, aplicar, salvar o visual atual como look).
- **Ao vivo e só local**: cada mudança aparece no avatar na hora (no máximo uma reconstrução por quadro, mesmo arrastando vários controles) e **só na sua tela** até salvar.
- **Salvar** envia o visual (`PUT /api/v1/users/me/avatar`, as mesmas regras do servidor) e, como em qualquer mudança, **avisa a ilha**: todos passam a ver. A resposta do servidor vira o visual salvo (ela traz o que os outros precisam para desenhar os itens). Erros do servidor aparecem em frase.
- **Desfazer** volta ao visual salvo; **Fechar** sem salvar também desfaz no avatar. Aplicar um look salva e mostra o que ele não conseguiu trazer (corpo ou animação que não existem mais).
- **Editor completo…** salva o que estiver pendente antes de abrir a tela completa.

**O que continua só na tela completa:** corpos personalizados, animações, importar roupas e acessórios e o nome do avatar.

**Teste:** `MirrorSmoke` (carrega o mundo com o jogador de verdade: a câmera vai para a frente e perto, o avatar não anda com a tecla apertada, visão do corpo inteiro e de costas, mudanças aparecendo no avatar, vestido cobrindo a calça, item próprio indo para o visual com o que a prévia precisa, salvar, desfazer, fechar sem salvar volta ao salvo e a câmera volta, andar de novo, looks, salvar como look).

**Limitações:** sem captura de tela (a distância e a posição da câmera foram conferidas por números); a prévia reconstrói o avatar por inteiro a cada mudança, como a tela completa; o espelho não mostra um reflexo de verdade, é a câmera na frente do avatar; durante o espelho, quem está perto vê o avatar parado como em qualquer pausa e não o visual ainda não salvo.

## Avatar 2.0 — LOD de geometria: o corpo simplificado de longe (entregue)

Antes, o nível "Reduzido" desenhava o corpo personalizado com **todos** os triângulos (só deixava de baixar os itens e as animações próprias) e o "Básico" trocava por um boneco padrão. Agora o **Reduzido desenha uma versão simplificada do modelo**:

- **Quanto**: o corpo mantém `AvatarDetailPlanner.ReducedRatio` = **35%** dos triângulos, nunca abaixo de **4.000** (`ReducedFloor`); um corpo de até 4.000 triângulos não é mexido. `ReducedTriangles(corpo)` é a regra única (cliente e planejador). Exemplo: 30.000 → 10.500; 100.000 → 35.000.
- **Como** (`client/Scripts/ModelLod.cs`): o simplificador do próprio engine (o mesmo que o editor usa para gerar LODs na importação, meshoptimizer) roda em cada superfície do modelo **mantendo os vértices**: só o buffer de índices fica menor, então esqueleto, pesos, UVs, normais e materiais continuam como eram. Escolhe-se o LOD mais perto do alvo. O resultado fica em cache por corpo (hash), nome da malha e superfície, então uma multidão do mesmo corpo simplifica uma vez.
- **Onde**: só em `AvatarDetail.Reduced`. Em **Completo** o corpo mantém todos os triângulos (e ao chegar perto o corpo é remontado a partir do arquivo em cache). **Básico** continua o boneco padrão (nenhum arquivo é baixado de longe).
- **Orçamento**: o custo do avatar Reduzido no planejador passou a ser o do corpo **simplificado** (`AvatarRig.ReducedCost`); `BodyCost` é o corpo inteiro e `FullCost` é corpo + itens. Com o mesmo orçamento cabem mais avatares com o corpo próprio de média distância (o teste mostra 20 corpos de 60 mil triângulos cabendo no orçamento onde antes só 7 ou menos cabiam).
- **Segurança**: malha com morph targets (blendshapes) é mantida inteira; se uma superfície não puder ser simplificada, ela fica como estava (mais triângulos, nunca um corpo quebrado); se o engine não tiver simplificador (`ModelLod.Available`), nada muda. O rig informa `CustomBodyFileTriangles`, `CustomBodyTriangles` e `CustomBodyGeometry` ("full" ou "simplificado: 40.000 → 14.000").

**Testes:** `ReducedGeometryTests` (a regra do custo, nunca cresce, mais avatares cabem no orçamento) e `ModelLodSmoke` (uma esfera densa de ~9 mil triângulos fica com cerca de um terço, mantém a forma, normais, UVs e índices válidos, cache, fração menor dá menos triângulos; um modelo com várias malhas é simplificado por inteiro; abaixo do piso nada muda; blendshapes intactos; no avatar, Reduzido nunca tem mais triângulos que o arquivo e Completo tem todos).

**Limitações:** a simplificação considera a pose de repouso (não os pesos dos ossos), então articulações muito detalhadas podem ficar um pouco mais angulosas ao dobrar; nada foi visto em captura de tela; o resultado depende de o build do engine incluir o simplificador (o editor e os smokes incluem; num build exportado sem ele o corpo simplesmente segue com todos os triângulos).

**Limpeza:** o `LocomotionSolver.cs` (sem nenhum uso) foi removido.
