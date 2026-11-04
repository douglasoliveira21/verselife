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
