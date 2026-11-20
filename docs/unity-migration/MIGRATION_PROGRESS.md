# Migração para Unity 6 HDRP — Progresso

Última atualização: 2026-11-18 (Fase 6). Não há percentuais: o progresso é contado sobre o inventário de `MIGRATION_CHECKLIST.md` (72 itens, grupos A a H) e sobre os critérios de aceite.

## Resumo

| Medida | Valor |
|---|---|
| Itens no checklist | 72 |
| Itens migrados e validados na Unity | **0** |
| Itens com a base técnica provada (A3, A4: `shared` e `scripting` rodando na Unity) | 2, ainda em validação |
| Itens que não precisam de migração (backend) | 1 (A1) |
| Fases concluídas | 1 (auditoria) |
| Fase em andamento | **6 (servidores)**, parcial (ver `MIGRATION_ISLAND_SERVER.md`); a 3 (fundação) segue com o gate abaixo por revisar |
| Decisões | D1, D2, D3 (LiteNetLib), D4 (Unity Dedicated Server) e D7 tomadas e executadas; o módulo Linux está instalado e o build do servidor foi verificado |
| Protocolo de rede | `LifeVerse.Wire` (protocolo 12): 44 mensagens, 25 testes xUnit e 2 testes na Unity passam; rodou num player Windows (Mono). Ainda não há servidor de ilha nem cliente de jogo usando o protocolo |
| Projeto Unity | Criado em `unity/UnityClient` |
| Código escrito para a Unity | 7 arquivos C# (488 linhas) em `unity/UnityClient/Assets/LifeVerse` e `unity/UnityPlugins`, mais o script `build-plugins.ps1` (60 linhas) |
| Arquivos do projeto Godot alterados | 0 |

## Ambiente verificado (2026-11-18)

| Item | Estado |
|---|---|
| Unity Editor | **6000.6.9f1** em `C:\Program Files\Unity\Hub\Editor\6000.6.9f1`; Windows Standalone e WebGL; **sem** o módulo de servidor Linux |
| Licença | O modo batch funcionou até 2026-11-19, quando passou a falhar com "No valid Unity Editor license found" (código 198); depois que a licença foi reativada no Unity Hub, voltou a funcionar. O log continua mostrando `Access token is unavailable`: pode repetir, e a reativação é no Unity Hub |
| Pacotes (Fase 3) | HDRP 17.6.0, Input System 1.20.1, glTFast 6.21.0, Test Framework 1.8.0 (57 pacotes resolvidos) |
| SDK .NET | 10.0.404 |
| IL2CPP | Indisponível: o Visual Studio com C++ não está instalado. O player foi construído com Mono |
| GPU de desenvolvimento | NVIDIA RTX 2060 SUPER, 4 GB de VRAM |

## Gate da Fase 3 ("S1 passa: compila e roda shared")

| Critério | Resultado | Evidência |
|---|---|---|
| Projeto Unity 6 HDRP criado | Sim | `unity/UnityClient`; `GraphicsSettings` aponta para `LifeVerse-HDRP.asset`; espaço de cor linear; Direct3D12 e Direct3D11; Input System ativo |
| `shared` e `scripting` compilam para a Unity | Sim | `build-plugins.ps1` gera `LifeVerse.Shared.dll` e `LifeVerse.Scripting.dll` (netstandard2.1) |
| Rodam no editor | Sim | 10 testes de modo Edit, 10 passaram, 0 falharam |
| Rodam num player compilado | Sim | Build Windows Mono (143 MB, 0 erros, 0 avisos); `LifeVerse.exe -batchmode -nographics -lifeverse-selfcheck` imprime `event=plugin_self_check result=passed checks=5`, código de saída 0 |
| Rodam com IL2CPP | **Não verificado** | Falta o Visual Studio C++; risco R4 continua aberto |
| Renderização HDRP verificada visualmente | **Não verificado** | Os testes rodaram com `-nographics`; nenhuma cena foi renderizada |

## Fases

| Fase | Estado | Observação |
|---|---|---|
| 1 Auditoria | Concluído | `MIGRATION_AUDIT.md` (classificação por arquivo, não por classe) |
| 2 Arquitetura | Concluído | `MIGRATION_ARCHITECTURE.md`; decisões D1 a D7 tomadas e executadas |
| 3 Fundação Unity | Concluído (menos IL2CPP) | Projeto, HDRP, pacotes, DLLs de `shared`/`scripting`, build Windows reproduzível. IL2CPP não verificado (sem Visual Studio C++) |
| 4 Código compartilhado | Parcial | Codecs, terreno, grupos, Opus e NPC passam no editor e no player; falta a suíte completa de `shared` no Test Runner |
| 5 Multiplayer | Concluído (menos latência alta) | Protocolo 12 e transporte; 64 clientes contra o servidor; falta o teste com latência alta |
| 6 Servidores | Concluído no que se verifica aqui | Núcleo sem engine, Host .NET e Unity Dedicated Server (Linux rodou no WSL); terreno, veículos, construção, terras, voz, aparência, anexos, scripts e física portados; 51 testes xUnit. Faltam a imagem do supervisor (sem Docker) e o campo `engine`/`wireVersion` na API (migração de banco não autorizada) |
| 7 Mundo | Concluído (com lacunas) | Terreno, oceano, céu, clima, objetos, modelos, plantas, ambiente, colisão. Lacunas: LOD de malhas, WebP, mapeamento planar, pivô, animações de modelos |
| 8 Avatar, movimento e câmera | Concluído (com lacunas) | Ver "Fase 8". Faltam roupas do homem (arquivos ausentes), dedos e anexos personalizados |
| 9 Interface | Concluído | Ver "Fase 9". Exercício contra a API real: 15 de 15; falta renovar o token e entrar numa ilha real (precisam de Postgres e supervisor) |
| 10 Construção e terreno | Concluído (com lacunas) | Ver "Fase 10" |
| 11 Áudio e voz | Concluído | Ver "Fase 11" |
| 12 Sobras de 7 a 12 | Concluída, com lacunas | Teclas, mapas, importação de modelos, anexos. Ver "Fase 12" |
| 13 a 15 | Não iniciado | Otimização, validação (IL2CPP, regressão completa), distribuição |

## Módulos

| Módulo | Origem | Destino Unity | Implementação | Compilação | Testes | Pendências | Próxima ação |
|---|---|---|---|---|---|---|---|
| Backend | `backend/` | inalterado | Não muda | OK, 0 avisos | 2.558 passam, 1 falha anterior (`HighJumpTests`) | — | Nenhuma |
| `shared` | `shared/` | `LifeVerse.Shared.dll` | Implementado, em validação | OK (netstandard2.1, 0 erros) | Codecs, terreno, hash, grupos, Opus e NPC passam no editor e no player | Rodar a suíte completa na Unity; IL2CPP | Fase 4 |
| `scripting` | `scripting/` | `LifeVerse.Scripting.dll` | Implementado, em validação | OK | Compilador Lua e timer do `ScriptHost` passam no editor; compilador Lua passa no player | IL2CPP; sandbox e limites no player | Fase 4 |
| Pipeline de build | — | `build-plugins.ps1`, `ProjectSetup`, `BuildClient` | Implementado, validado | OK | Build Windows reproduzível | Build do servidor (D4) | — |
| Rede (protocolo e transporte) | `NetworkBridge` (44 RPCs) | `unity/UnityWire` (`LifeVerse.Wire`) | Implementado, em validação | OK (`netstandard2.1` e `net8.0`, 0 avisos; solução inteira 0 avisos) | 25 xUnit + 2 na Unity + autoverificação do player: passam | Servidor de ilha e cliente de jogo; 64 clientes; latência alta | Fase 6 |
| Servidor de ilha | `island-server/` | `unity/UnityIslandServer` (`Core`, `Host`, `LoadTest`) + `Assets/LifeVerse/Server` | Parcial: ver paridade em `MIGRATION_ISLAND_SERVER.md` | `Core` net8.0 e netstandard2.1 OK (0 avisos); Build Linux OK (162 MB) e executado no Ubuntu/WSL 2 | 51 xUnit; 64 clientes contra o player Unity (Windows/Mono); executado no Ubuntu (WSL 2): 64 clientes, SIGTERM; Alpine não suporta o player Unity (só o `backend/` é Alpine; as ilhas já são Debian) | Terreno por donos de parcela, scripts (`IslandScripting`), física (S4), voz, `engine`/`wireVersion` na API, Supervisor | Fase 6 (continuação) |
| Mundo 3D, Jogador, Avatar, Construção, UI, Social, Áudio | ver checklist | `LifeVerse.Unity.*` (World, Avatar, UI, Audio, Server) | Implementado | OK | 193 verificações do player, 64 testes Edit, 15 etapas na API real | Ver "Fase 12" e "Fases 13 a 15" | Fase 12 |
| Distribuição | `launcher/`, `installer/` | a adaptar | Não iniciado | — | — | — | Fase 15 |

## Registro de decisões e incompatibilidades

- 2026-11-18: o prompt assume protocolo v7 e "preservar ENet". O repositório está no protocolo **11** e usa o `SceneMultiplayer` do Godot (ver `MIGRATION_AUDIT.md`, seção 3).
- 2026-11-18: não há `.tres` nem arquivos de shader; a conversão de recursos é conversão de código que cria recursos.
- 2026-11-18, **D1 tomada**: o projeto Unity fica em `unity/` no monorepo.
- 2026-11-18, **D3 tomada e executada**: protocolo "LifeVerse Wire" (versão 12) sobre LiteNetLib 2.1.5. ENet-CSharp descartado (binário nativo, sem compilador C++). Cliente e servidor Unity não falam com os Godot; a convivência passa pelo campo de versão/engine do ticket (a implementar na API).
- 2026-11-18, **D4 executada**: módulo "Linux Dedicated Server Build Support" instalado com o Unity CLI 1.0.1 (43 MB, SHA-256 conferido, assinatura da Unity Technologies SF válida, em `.tools/unity-cli/unity.exe`; o script oficial `install.ps1` **não** foi executado porque também edita o PATH e instala "skills" em clientes de agente de IA). Build do servidor dedicado Linux: 161 MB, 0 erros, 7 avisos. **Não executado** (sem WSL). Para repetir: `Unity.exe -batchmode -nographics -projectPath unity\UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildLinuxServer`.
- 2026-11-18: o HDRP recusou o build Linux com a lista padrão de APIs gráficas (inclui OpenGL). `ProjectSetup` agora fixa Vulkan para `StandaloneLinux64`.
- 2026-11-18: `LifeVerse.slnx` passou a incluir `unity/UnityWire` e `tests/LifeVerse.Wire.Tests` (a solução inteira compila com 0 avisos).
- 2026-11-18, **D2 tomada**: reuso por DLL pré-compilada, com `csc.rsp -langversion:12` e polyfills públicos no assembly `LifeVerse.Unity.Core` para o código-fonte da Unity.
- 2026-11-18, **D7 executada** (autorizada): HDRP 17.6.0 (embutido), Input System 1.20.1, glTFast 6.21.0 e Test Framework 1.8.0.
- 2026-11-18: o `shared` precisou de 13 APIs ausentes no netstandard2.1; a solução (11 substituições mecânicas no código gerado mais `Compat.cs`) deixa os fontes originais intactos. Se o `shared` passar a usar outra API do .NET 5–8, o `build-plugins.ps1` falha com o erro de compilação e a regra nova deve ser adicionada ali.
- 2026-11-18: o `build-plugins.ps1` copia, junto com as DLLs do LifeVerse, as dependências de pacotes (`System.Memory`, `System.Buffers`, `System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe`, `System.Threading.Tasks.Extensions`, `Microsoft.Bcl.AsyncInterfaces`, `System.Text.Json`, `System.Text.Encodings.Web`, Concentus, MoonSharp). Elas convivem com a Unity no editor e no player Mono; se aparecer conflito de assemblies em outro alvo, remover as que a Unity já fornece.
- 2026-11-18, **Fase 6 iniciada**: `unity/UnityIslandServer/Core` (sem engine) hospedado por `Host` (.NET) e por `Assets/LifeVerse/Server/IslandServerBehaviour` (Unity). A API continua emitindo bilhetes de protocolo 11: o servidor Unity os aceita por `LIFEVERSE_TICKET_PROTOCOL` (padrão 11) em vez de exigir uma mudança de API; escolher o motor por ilha continua dependendo de um campo `engine`/`wireVersion` (migração de banco, **não autorizada nem feita**).
- 2026-11-18: o Wire passou a numerar jogadores como `id do LiteNetLib + 1` (0 = sistema) e o servidor divide snapshots em pacotes de até 40 jogadores do mesmo tick; ambos documentados em `WIRE_PROTOCOL.md`.
- 2026-11-18: o commit `4e73996` omitiu três `.csproj` (`LifeVerse.Wire`, `LifeVerse.Shared.Unity`, `LifeVerse.Scripting.Unity`) por causa da regra `**/*.csproj` de `unity/.gitignore`; corrigido com exceções na regra, e os arquivos entram no commit da Fase 6.
- 2026-11-18: o servidor Godot não foi alterado.

## Fase 7 (mundo 3D): o que existe e como foi verificado

Código em `unity/UnityClient/Assets/LifeVerse/World` (assembly `LifeVerse.Unity.World`): `TerrainView` (malha a partir do `TerrainMap` e textura de chão), `OceanView`, `IslandEnvironment` (hora, sol, céu, névoa, clima, visual de parcela), `ProceduralTextures` e `WorldPreview`. Os materiais HDRP (`TerrainMaterial`, `OceanMaterial`) são assets criados por `WorldAssets` (editor; `-executeMethod LifeVerse.Unity.EditorTools.WorldAssets.BuildBatch`), porque o HDRP liga recursos por palavras-chave que só o editor define.

**Verificação:** `LifeVerse.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -lifeverse-world-preview <prefixo>` abre uma janela na GPU (RTX 2060 SUPER, Direct3D) e salva 7 fotos (`island`, `sunset`, `night`, `storm`, `under`, `beach`, `ground`). Quatro estão em `docs/unity-migration/img/` (`world-island.png`, `world-sunset.png`, `world-night.png`, `world-under.png`). Mais 5 testes de modo Edit (malha com as alturas do mapa, triângulos virados para cima, mistura das camadas por pesos, sol e noite, visual de parcela): 17 de 17 no total.

**Decisões e achados**
- **D5 (shader do terreno)**: sem Shader Graph, o HDRP não tem um shader de mistura de 8 camadas por vértice. As camadas são misturadas na CPU em **uma textura de 1024 px para a ilha** (25 cm por texel), com um mapa de detalhe repetido a cada metro para o grão fino, deformação do domínio e variação em manchas grandes para esconder a repetição de 5 m. Limite: a edição de terreno obriga a refazer a textura (hoje, tudo de uma vez); ainda aparece um padrão quadriculado fraco no gramado.
- **Céu**: o céu fisicamente baseado do HDRP **ignorou o sol** neste projeto (ficou em pleno dia à noite e no pôr do sol; a causa não foi isolada). O céu passou a ser um gradiente HDRP controlado pela hora (cores de dia e de noite como no cliente Godot, horizonte laranja com o sol baixo), sol e lua como luzes direcionais e câmera com exposição fixa. Parâmetros de volume do HDRP só valem se tiverem `Override`: valores atribuídos a cada quadro sem isso foram ignorados em silêncio.
- **Sol nunca com intensidade zero** e exposição fixa de dia: sem isso a noite saía branca.
- **Água**: material transparente do HDRP com duas camadas de normais deslizando; vista de baixo com o mesmo plano de dupla face e um volume de névoa azul. Fora do mapa da ilha não há chão: a água fica azul-escura (profundidade infinita), como no Godot.

**Não feito / não verificado**: chuva em partículas e raios; disco do sol e estrelas; ondas de vértice e espuma na costa (o Godot calcula profundidade por pixel); colisão e raycast do terreno; aplicar edições e patches do servidor à malha em tempo real; LOD; ler o ambiente e o terreno de uma ilha real (só a ilha de exemplo); desempenho (quadros por segundo não foram medidos); modo claro/escuro e qualidade gráfica (`GraphicsQuality`); IL2CPP; Linux com Vulkan renderizando; câmera e avatar (Fase 8), objetos do mundo (C6), efeitos (C7) e mapa (C9).

### Fase 7, segunda parte: o mundo acompanha o servidor

`WorldClient` (World) entra numa ilha por LifeVerse Wire e mantém o terreno e o mar iguais ao do servidor: o mapa inteiro ao entrar, as edições (repetidas localmente com as mesmas regras do `shared`), os patches de desfazer e o nível do mar; se a revisão local divergir, pede o mapa de novo (no máximo a cada 2 s, como o servidor permite). `TerrainView` redesenha a malha na hora e refaz a textura só na área tocada.

**Verificação** (`LifeVerse.exe -batchmode -nographics -lifeverse-world-selfcheck`, 6 execuções seguidas, 11 verificações cada, todas passaram): dentro de um único processo, um servidor de ilha de verdade (o mesmo núcleo do servidor dedicado, com a API em memória `LocalIslandApi`) numa porta UDP e três jogadores. Confere: só o editor esculpe e o visitante recebe `no_permission`; três edições de um jogador chegam ao outro e os mapas ficam **idênticos byte a byte** a um mapa calculado à parte; a malha de quem olha mostra o morro; a pintura chega igual; o mar sobe para quem olha; desfazer chega como patch aos dois; quem entra depois recebe o terreno e o mar como estão. Com uma janela (`-lifeverse-world-shots <prefixo>`) fotografa antes e depois: `docs/unity-migration/img/world-sync-after.png` mostra o morro de rocha criado por outro jogador. Uma falha intermitente da primeira versão do teste (conferir um jogador antes de o outro receber o patch) foi corrigida; era do teste, não do jogo.

Ainda sem: ler o terreno de uma ilha real da API (a ilha é a de exemplo ou a em memória), a aplicação de edições com máscara de parcela (chegam como patch e são aplicadas, mas não foram testadas), colisão local do terreno, e o resto do que a Fase 7 lista (chuva, ondas, objetos, efeitos, mapa).

### Fase 7, terceira parte: colisão, ondas, chuva, objetos e a porta REST

**Espaço de jogo (decisão registrada).** O servidor e o conteúdo vivem no espaço do Godot (destro); a Unity é canhota. O mundo é desenhado com o eixo z invertido (`GameSpace`: (x, y, z) do jogo vira (x, y, -z) na Unity; um quaternion (x, y, z, w) vira (-x, -y, z, w)). Assim um prédio, uma placa ou um modelo aparecem como no Godot, sem espelhamento, e as malhas mantêm a ordem dos triângulos. Tudo que o servidor manda passa por `GameSpace` a caminho da tela, e o que volta ao servidor (onde clicaram, onde andam) passa pela inversa. A física do servidor não muda (números do jogo). O avatar e a câmera (Fase 8) precisam respeitar isto.

**Colisão.** `TerrainView` tem um `MeshCollider` refeito com a malha; `GroundHeight(x, z)` e `Raycast` devolvem a altura do mapa dentro de 6 cm (teste: 4 pontos, fora da ilha não há chão, e o colisor sobe depois de uma edição do servidor).

**Oceano.** Malha única de 160 x 160 células com células que crescem com o cubo da distância (finas sob a câmera, 50 m na borda), vértices recalculados a cada quadro com as mesmas quatro senoides do Godot (0,22 m, esmaecendo de 140 a 300 m), duas camadas de normais deslizando. A espuma é uma faixa na textura do chão onde o terreno fica a menos de um metro do nível do mar (o Godot a calcula por pixel pela profundidade; aqui ela não se move). **Achado**: o terreno **não pode projetar sombra** (como no Godot, `CastShadow Off`): no HDRP ele sombreava todo o chão ao redor da câmera (um disco escuro com borda nítida, dentro do alcance das cascatas); isolado desligando sol, oceano, chuva e lua um a um, e as sombras do terreno.

**Chuva.** Partículas esticadas (HDRP/Unlit) seguindo a câmera, 4 200 por segundo na chuva e 7 000 na tempestade, céu cinza e névoa mais densa por clima; some com o tempo limpo.

**Objetos.** `WorldObjects` desenha o que o servidor manda (`objectState`, `objects`, `healed`, `delete`, poses de física) e a lista que a API dá ao entrar; ver C6 no checklist.

**Porta REST.** `IslandEntryClient` faz login (`/auth/login`), o bilhete de entrada (esperando o servidor da ilha iniciar quando a API responde 503 "starting"), o snapshot do terreno e a lista de objetos; `WorldJoin` junta tudo: mostra a ilha salva antes da conexão e depois entra no servidor com o bilhete.

**Verificação**: `-lifeverse-world-selfcheck` agora tem 29 verificações (4 execuções seguidas): as de antes mais os objetos (7 criados por um jogador chegam a outro, posição com z invertido, rotação, cor, transparência virar alfa, atualizar, excluir) e a porta REST contra uma API HTTP em memória (`StubPlatformApi`, as rotas do backend): senha errada 401, login, entrada sem token 401, uma resposta 503 e a repetição, o terreno salvo desenhado antes de conectar, os dois objetos carregados, e a entrada completa com o bilhete. 19 de 19 testes de modo Edit; o build Linux do servidor compila. Fotos: `world-objects.png`, `world-storm-rain.png`, `world-waves.png`.

**Não verificado**: a **API real** (nenhuma credencial usada: só a API de mentira com as rotas do backend; o login com segundo fator e a renovação do token de 15 minutos não existem no cliente), colisão dos objetos, desempenho (quadros por segundo não medidos), e o desenho do mar visto de baixo com as ondas.

### Fase 7, quarta parte: texturas, modelos, plantas, corte por distância, disco do sol, espuma por pixel e o ambiente da ilha

**Ambiente salvo da ilha.** `IslandLookService` pede `GET /api/v1/worlds/islands/{id}/environment` ao entrar (`WorldJoin` espera a primeira resposta, então a hora e o clima salvos já valem no primeiro quadro) e a cada 30 s; entrega o `look` ao `IslandEnvironment.SetLook`, que o mistura em 3 s (hora, clima, luz, névoa extra e tinta do céu a até 55 %). Se a API não responde, o último visual fica.

**Disco do sol.** Esfera emissiva (`SunMaterial`, HDRP/Unlit) a 1,8 km, com 1,8° de diâmetro, visível só com o sol acima do horizonte e some atrás de nuvens pesadas; mais um `Bloom` suave. Foto: `world-sun-disc.png`, `world-sunset.png`.

**Texturas.** `WorldObjects.AssetLoader` (ligado pelo `WorldJoin` a `IslandEntryClient.GetAssetAsync`: endpoint da ilha, depois os arquivos da pessoa) busca cada imagem uma vez (cache por id), decodifica PNG/JPEG, reduz acima de 2048 e a coloca no material. O HDRP só desenha um mapa se a palavra-chave estiver ligada, e isso só o editor faz; por isso há **quatro protótipos** (opaco e transparente, uma face e dupla face) já com mapas neutros (branco, normal plana, máscara de 1, brilho branco), e em tempo de execução só se troca a imagem. O ORM do glTF (oclusão R, rugosidade G, metal B) é reempacotado para a máscara do HDRP (metal R, oclusão G, suavidade A = 1 - rugosidade). **Limite**: no HDRP todos os mapas dividem a repetição do mapa base.

**Modelos importados.** glTF/GLB pelo glTFast (`GltfImport.LoadGltfBinary`), medidos longe do objeto, normalizados para 1 m no lado maior e centrados; a escala do objeto dá o tamanho real. Há um giro de 180° em y porque o glTFast espelha x e o espaço de jogo espelha z. Até chegar, e se falhar, um cubo translúcido. **Achado**: o glTFast cria os materiais em tempo de execução a partir de shader graphs que nenhuma cena cita; no player o modelo saía **magenta**. `GltfShaders` (pré-build) põe os três shaders na lista "Always Included Shaders"; depois disso o modelo desenha. Testado com um GLB feito à mão (`TestAssets.Glb`), não com modelos reais do Godot (as animações são ignoradas).

**Árvores e grama.** Geradas (nada a importar): árvore com tronco e duas copas cônicas, grama com 18 folhas dos dois lados; o tronco é sempre #7A5230 e as folhas têm verde próprio até a pessoa escolher uma cor.

**Corte por distância e LOD.** Cada objeto some além de `maior escala × 120 m` (entre 40 e 600 m) e volta dentro de 90 % do alcance, 4 verificações por segundo. A **simplificação de malha** do LOD do Godot não foi feita (a Unity não tem simplificador embutido); o corte é o que existe.

**Espuma por pixel.** `ShoreFoam` + `Shaders/ShoreFoam.shader`: folha plana no nível do mar com um shader HLSL (HDRP, sem iluminação, transparente) que, em cada pixel, lê a altura do terreno de uma textura (um texel por vértice, filtrada) e põe renda de espuma onde a água é rasa, com a borda indo e vindo como a arrebentação. A textura é refeita quando o mapa, a revisão ou o nível do mar mudam. **Achado**: o shader precisa incluir `core/ShaderLibrary/Common.hlsl` antes do `ShaderVariables.hlsl` do HDRP (senão `GLOBAL_CBUFFER_START` não existe; o build conta o erro, mas o player roda e o chão fica rosa/magenta). Não usa o buffer de profundidade da câmera: como o terreno é estático, a profundidade vem da textura. Foto: `world-sunset.png`.

**Verificação**: 21 de 21 testes de modo Edit; `-lifeverse-world-selfcheck` com 35 verificações (a hora 22 h e a chuva salvas vindas da API, a textura chegar do endpoint e estar na cor base, o GLB normalizado para 3 m × 1,5 m e centrado, a árvore e a grama, o corte do objeto a 1,5 km, a espuma preparada), no player de Windows. Fotos: `world-assets-models.png`.

**Não verificado**: modelos reais do Godot (só um cubo feito à mão), texturas WebP (não suportado), desempenho com muitos objetos, o halo do sol, a API real.

## Fase 8 (avatar, movimento e câmera): primeira parte

Novo assembly `LifeVerse.Unity.Avatar` (`Assets/LifeVerse/Avatar`); o `WorldClient` agora decodifica os snapshots e avisa por eventos (`PlayerSpawned`, `PlayerDespawned`, `SnapshotReceived`, `GestureChanged`, `AppearanceChanged`), sem referenciar o assembly de avatar.

**Movimento (`LocalPlayer`).** Lê o teclado (WASD ou setas, Shift corre, Espaço pula, C ou Ctrl desce, F voa, X senta), converte a direção para o espaço de jogo pela direção da câmera, envia `SubmitInputMessage` a 60 Hz e, no mesmo passo, move o próprio corpo com o `AuthoritativeMovement` do `shared`, o mesmo código do servidor. Cada snapshot traz a posição do servidor após a última entrada que ele processou (`LastProcessedInput`): compara-se com a posição que o jogador tinha **naquela mesma entrada** (histórico das últimas 240), e a diferença é fechada (30 % por snapshot quando passa de 8 cm, de uma vez acima de 3 m). Assim a latência não puxa o avatar de volta. **Achado**: voar não é uma flag de entrada (o servidor só honra `SetFlightMessage`, "confiável e idempotente"); a previsão liga o voo na hora e espera a confirmação do servidor por até 1,5 s. Os gestos (acenar, dançar) também não são flags de entrada: virão com a mensagem de gesto.

**Corpos (`AvatarView`, `PlayerViews`).** Um corpo procedural por pessoa (cor pelo nome), com o heading do servidor (`atan2(vx, -vz)` do jogo é o mesmo ângulo de um heading da Unity) e animação por estado (andar, correr, pular, cair, voar, nadar, sentar, acenar, dançar, digitando). Corpos remotos seguem o snapshot com suavização e extrapolação de até 0,2 s; um salto de mais de 5 m é imediato.

**Câmera (`FollowCamera`).** Terceira pessoa em órbita da cabeça (botão direito + mouse, roda para a distância 1,2 a 14 m), puxada para perto quando o terreno está no caminho e nunca abaixo do chão.

**Verificação**: `-lifeverse-world-selfcheck` com 49 verificações (as de antes mais 14 de pessoas) contra o servidor real: o jogador anda 7,9 m em 2,5 s (3,2 m/s), servidor e previsão a 3 cm, 0 correções; corre a 6,4 m/s; pula 1,4 m e volta ao chão; o servidor o põe em voo e ele sobe, desce e pousa; o outro jogador vê o corpo, o estado, a direção e as pernas balançando. 26 de 26 testes de modo Edit (5 novos). Foto: `avatar-third-person.png`.

**Não feito nesta parte**: o corpo do `HumanoidTemplate` e as animações compartilhadas (`AnimationClips`), corpos GLB, roupas e anexos, aparência, primeira pessoa e olhar, gestos, sentar em assentos, nadar com água por volta, veículos, obstáculos dos objetos na previsão (o servidor corrige), passos, nomes sobre a cabeça, e a interface. Os controles não foram testados com um teclado de verdade (os testes usam a entrada por script; o ambiente não tem teclado).

### Fase 8, segunda parte: gestos, primeira pessoa e nomes

**Gestos.** Acenar (G, 2,5 s) e dançar (H, 5 s) viajam como flags de entrada que o servidor transforma em estado de movimento; **achado**: o servidor não segura o estado (a flag é limpa após um passo), então o cliente reenvia a flag a cada passo enquanto o gesto dura. Aplaudir (J), apontar (K), cumprimentar (L), rir (N) e sentar no chão vão uma vez pela `GestureMessage` e o servidor repassa aos outros, que tocam por 2,5 s (`ExtraGestures.Seconds`). Andar, pular, voar, nadar ou sentar encerra o gesto. `LocalPlayer.StartGesture(nome)` é o ponto de entrada para a interface futura.

**Primeira pessoa (V).** `FollowCamera.SetFirstPerson`: olhos a 1,62 m, mouse com o cursor travado, e as regras compartilhadas do Godot (`HeadBob`, `CameraSway`, `EyeStabilizer` de `shared/Camera`); o próprio corpo some. Falta a colisão dos olhos com objetos (`EyeCollision` não ligada), as mãos e o olhar. O C do Godot (primeira pessoa) está no Unity como descer; a tecla V alterna a câmera.

**Nomes.** `TextMesh` sobre a cabeça, de frente para a câmera, some além de 45 m e no próprio corpo. Funciona no HDRP; o texto fica um pouco apagado (sombreador de fonte do Unity).

**Verificação**: self-check com 56 verificações (7 novas: o aceno chega como estado e levanta o braço do outro, o aplauso chega pela mensagem, andar o interrompe, o nome, os olhos em primeira pessoa e o corpo escondido); 28 de 28 testes Edit. Fotos: `avatar-people-nametag.png`, `avatar-first-person.png`.

**Ainda não**: as animações compartilhadas (`AnimationClips`) e o corpo do `HumanoidTemplate` (o corpo atual não tem ossos padrão), corpos GLB, aparência/roupas/anexos, mãos e olhar, sentar em assentos, veículos, passos.

### Fase 8, terceira parte: aparência, corpo personalizado, animações do jogo, veículos e olhar

**Aparência (`AvatarAppearance`, `AvatarView.ApplyAppearance`).** O `AppearanceJson` do servidor (os mesmos campos do `AvatarAppearanceData` do Godot) é lido com as faixas do `shared` (`AvatarRules`: um número fora da faixa volta ao limite) e o corpo simples é vestido: pele, cabelo (curto, chanel, longo, rabo, moicano, careca), barba, olhos, tintas da camiseta, calça e sapatos, boné, chapéu, gorro, óculos, mochila, cachecol, saia/vestido, casaco, e os controles de altura, ombros, braços, pernas e cabeça. Chega no spawn e em `AppearanceChanged`. Foto: `avatar-dressed.png`.

**Corpo personalizado (`SkeletalBody`).** Um GLB com o esqueleto humanoide padrão é carregado pelo glTFast (o `PlayerViews` busca o asset da API pelo `customModelId`), levado a 1,8 m, posto no chão e usado no lugar do corpo simples (níveis Reduced e Full). **Os clipes do jogo** (os mesmos `.bin` do Godot, copiados para `Resources/Animations` como `.bytes`: idle, walk, run, jump, fall, voo, nado, digitação) são lidos pelo `SkeletonClip` do `shared` em outra thread; para cada osso padrão sai "quanto girou do repouso, no espaço do modelo", que é convertido para a Unity (o glTFast espelha x: o quaternion vira (x, -y, -z, w); o conteúdo gira 180° em y como nos objetos) e posto no osso. **Verificação forte**: um esqueleto de modelo (nomes Mixamo) com uma animação própria é tocado de duas formas, pelo carregador do glTFast e pelo código compartilhado, e os ossos (braço, perna, cabeça) ficam a menos de 1° um do outro. Troca de clipe com mistura de 0,2 s. Estados sem clipe (sentar, acenar, dançar, gestos) e o tempo antes de os clipes serem lidos usam a pose procedural do corpo simples sobre o esqueleto (os braços descem da pose em T). Foto: `avatar-custom-body.png`.

**Nível de detalhe.** `PlayerViews.PlanDetail` usa o `AvatarDetailPlanner` do `shared` duas vezes por segundo (distância, estar na frente da câmera, orçamento de triângulos e de corpos cheios): o jogador é sempre cheio, os de longe voltam ao corpo simples ou somem (e não são animados).

**Veículos (`VehicleViews`, `LocalPlayer`).** Carros da ilha seguem os snapshots do servidor (posição, rumo, inclinação, velocidade, motorista), com rodas que giram; E entra no mais próximo (até 4 m) ou sai; W/S aceleram, A/D viram, Espaço freia, enviados ao servidor, que simula o carro e prende o motorista (sentado). Foto: `vehicle-driving.png`.

**Olhar e digitação.** Em primeira pessoa o ângulo vertical vai ao servidor pelas regras compartilhadas (`HeadLook`: só se mudou 0,03 rad, no mínimo a cada 80 ms, repetido a cada segundo) e a cabeça dos outros o acompanha (nunca além de 0,9 rad; o servidor recusa o que passa de 1,6). `LocalPlayer.Typing` liga a flag que faz o avatar digitar.

**Verificação**: `-lifeverse-world-selfcheck` com 77 verificações (21 novas: aparência vinda do servidor, corpo personalizado, comparação com o carregador, clipes do jogo, andar com o clipe Walk, sentar sem clipe, detalhe, olhar, ângulo recusado, jogador saindo, carro, direção, freio, descer); 33 de 33 testes Edit (5 novos). Fotos: `avatar-custom-body.png`, `avatar-dressed.png`, `vehicle-driving.png`.

**O que da Fase 8 ainda falta** (e não foi inventado): peças de roupa e acessórios **personalizados** (GLB), anexos em ossos (`AttachmentPoints`) e as "prévias ao vivo", animações **próprias** da pessoa (arquivos `.glb` dela), dedos e mãos em primeira pessoa, olhos que olham, sentar em assentos de objetos (o servidor só senta onde a pessoa está), passos e o som de digitação (áudio é da Fase 11), o corpo "lifeverse_man" que acompanha o jogo (o arquivo não existe no Unity ainda) e nomes de grupo sob o nome. O corpo personalizado só foi testado com um modelo de caixas gerado pelo `HumanoidTemplate`, não com modelos Mixamo reais.

## Fase 9 (interface): primeira parte

Novo assembly `LifeVerse.Unity.UI` (`Assets/LifeVerse/UI`), em **UI Toolkit** (decisão D6): a estrutura em C# e o visual em `Resources/LifeVerse/UI/Theme.uss`, com as cores e raios do `UiStyle` do Godot (vidro `#1B262E`, contorno `#6D818D`, destaque `#12F2E0`, laranja `#F4A340`) e a fonte Inter (as mesmas três `.ttf`). **Achado**: o tema padrão do runtime do UI Toolkit é vazio, então campos de lista, alternadores, controles deslizantes e barras de rolagem só têm visual porque o `Theme.uss` os estiliza.

**O cliente jogável (`GameApp`).** Em uma execução normal (sem argumentos `-lifeverse-...`, fora do modo batch) o jogo abre na tela de entrada, e o fluxo inteiro existe: entrar ou criar a conta na API (`IslandEntryClient.LoginAsync/RegisterAsync`), tela de carregamento, o mundo (terreno, mar, céu, objetos, pessoas, veículos), câmera e avatar, HUD. Se a conexão cai, volta à tela de entrada com a mensagem. O endereço da API é `LIFEVERSE_API_URL` ou `https://api.nerdplus.com.br`.

**Telas.** `LoginView` (usuário, senha, ilha, lembrar usuário, modo de cadastro com e-mail; as mesmas regras de senha do servidor e mensagens em português; **só o nome do usuário é lembrado, nunca a senha**: o Godot guarda a senha, aqui não), `LoadingView` (a fase da entrada em português e uma barra), `Hud` (barra superior com ilha, pessoas e conexão; barra inferior com os botões), `ChatPanel` (as últimas 300 linhas, limpeza de caracteres de controle, 300 caracteres, Enter envia, Esc sai; enquanto o campo tem o foco o avatar digita para todos), `NotificationCenter` (até 4 avisos na tela, histórico de 50, repetida em 2 s conta uma vez), `FloatingWindow` (arrasta, vai para a frente, não sai da tela e lembra a posição), janelas Pessoas por perto e Ajuda.

**Configurações (`SettingsWindow`, `UserSettings`, `GraphicsQuality`).** Cinco abas (Gráficos, Áudio, Controles, Interface, Acessibilidade), aplicadas na hora e salvas em `settings.json` (escrita por arquivo temporário; valores fora do limite voltam ao limite). Os **presets Baixo/Médio/Alto/Ultra** (item H4) mudam o alcance do detalhe dos modelos (`lodBias`), a nitidez das texturas, o alcance das sombras do sol (Baixo as desliga) e a chuva, mais vsync e limite de quadros. Controles chegam ao jogo (sensibilidade, inverter eixo, correr sempre), e movimento reduzido tira o balanço da câmera em primeira pessoa.

**Verificação**: 7 testes Edit novos (40 de 40) para configurações, escala, chat, formulário, carregamento e presets; `-lifeverse-ui-shots` fotografa e verifica o painel real (8 verificações); e o `-lifeverse-world-selfcheck` (86 verificações) agora entra pela tela do `GameApp` contra a ilha real do teste: senha errada fica na tela com a mensagem, entrar abre o HUD, o chat vai e volta entre dois jogadores, o preset Baixo desliga as sombras, os controles chegam ao avatar, voltar à tela fecha o HUD. Fotos: `ui-login.png`, `ui-register.png`, `ui-loading.png`, `ui-hud.png`, `ui-settings.png`, `ui-game-hud.png`.

**Ainda não** (a Fase 9 é grande): segundo fator no login, renovação do token de 15 minutos, escolha de ilha por lista (hoje o código da ilha), criação e personalização de personagem (D5), menu radial (D9), mensagens privadas, amigos, grupos, perfil, inventário, busca, economia, gestão de ilha, editor Lua, mini-mapa, sobreposição de desempenho, o volume de verdade (a configuração é salva e entregue, mas ainda não há áudio: Fase 11) e o idioma além do português. Os controles de teclado e mouse e o toque não foram testados com hardware real.

### Fase 9, segunda parte: sessão, ilhas, social, carteira, inventário, busca, perfil, scripts, menu radial, mapa e desempenho

**Segundo fator.** O login dos jogadores da API **não tem segundo fator** (`/api/v1/auth/login` recebe só `login` e `password`; o MFA do backend é o do painel de administração), então não há o que portar no cliente.

**Renovação do token.** `IslandEntryClient` guarda o `refreshToken` e o vencimento; toda chamada passa por `SendAsync`, que renova um minuto antes de vencer e, se a API responde 401, renova e refaz a chamada uma vez. Se a renovação é recusada, o evento `SessionExpired` leva a pessoa de volta ao login com "Sua sessão acabou". Testado: um token vencido é renovado sozinho e a chamada é refeita; uma sessão acabada volta ao login e dá para entrar de novo.

**Lista de ilhas.** A tela de entrada lista as ilhas públicas (`GET /api/v1/worlds/islands`, que não exige login), com categoria, e lembra a última; o campo de código continua para ilhas privadas.

**Janelas da plataforma** (todas por `ApiContext`/`FloatingWindow`, falando com as rotas reais do backend, cujas formas li no código do backend: ilhas, amigos, carteira, mensagens, scripts; as demais são lidas de forma tolerante):
- **Social** (`SocialWindow`): amigos (mensagem, remover, pedir por identificador), pedidos (aceitar/recusar), conversa privada (últimas 50, resposta), grupos (sair).
- **Carteira** (`WalletWindow`): saldo, movimentações e transferência de VerseCoin com **confirmação em dois toques** e chave de idempotência; recusa o que não faz sentido antes de perguntar à API. O servidor segue como autoridade financeira.
- **Inventário** (`InventoryWindow`): visões Tudo/Favoritos/Recentes/Lixeira, busca e estrela de favorito. **Busca** (`SearchWindow`): ilhas públicas com "Entrar" (troca de ilha pelo `GameApp.SwitchIsland`) e busca geral. **Perfil** (`ProfileWindow`) e **Ilha** (`IslandInfoWindow`, com as permissões).
- **Editor Lua** (`LuaEditorWindow`): lista os scripts do objeto, abre, edita, **verifica a sintaxe com o mesmo interpretador MoonSharp da ilha** (a mensagem traz a linha) e salva (novo ou atualização); a ilha ainda valida ao salvar. Enquanto a construção (Fase 10) não escolhe objetos com o mouse, o objeto é dado pelo identificador.
- **Menu radial** (`RadialMenu`, tecla Q): anel de até 12 ações desenhado com `Painter2D`, com a geometria compartilhada do `RadialMath` (fatia sob o ponteiro, posição dos rótulos, manter o anel na tela, páginas, teclas 1 a 8, Tab e setas). Ações: gestos, sentar no chão, carro, primeira pessoa, voar.
- **Mapa** (`Minimap`): a ilha vista de cima, pintada das alturas do terreno (água funda/rasa pelo nível do mar, areia, grama, rocha, neve), com a pessoa e as outras como pontos; repinta quando o terreno ou o mar mudam. **Desempenho** (`PerformanceOverlay`, F3): quadros por segundo, tempo do quadro, o pior quadro, memória, pessoas, objetos e quantos estão fora do alcance.

**Verificação**: `-lifeverse-world-selfcheck` com 106 verificações (20 novas, contra a API de mentira com as formas reais): lista de ilhas, renovação, amigos/conversa/envio, carteira com confirmação, inventário, busca, perfil, editor Lua (abrir, salvar, recusar erro), menu radial (fatias, páginas, anel na tela), mapa, sobreposição, sessão acabada e voltar a entrar; 46 de 46 testes Edit (6 novos). Foto: `ui-platform.png`.

**Ainda não**: criação e personalização de personagem (D5), grupos além de listar/sair (criar, convidar, papéis), perfil editável (galeria, feed, casamento), mensagens de grupo e de ilha, estado de presença, bloqueios, relatos, as janelas de versões/direitos/ofertas/objetos perdidos do inventário, colocar/copiar/equipar itens (vem com a construção), compra no mercado e lojas, aluguel de ilha, gestão de ilha e terras (permissões, banimentos, terrenos), eventos, fotos, marcos e teleporte pela descoberta. As formas de resposta de inventário, busca e perfil não foram confirmadas contra a API real (só li os contratos de ilhas, amigos, mensagens, carteira e scripts).

### Fase 9, terceira parte: personagem, grupos, perfil, presença, mercado, gestão, eventos e itens

Todas falam com as rotas reais do backend, cujos contratos (os `record` de pedido) li no código; as respostas são lidas de forma tolerante. Tudo que gasta dinheiro ou não se desfaz pede um segundo toque (`Controls.Confirm`), e o servidor segue sendo quem decide.

- **Criador de personagem** (`CharacterWindow`, `CharacterDraft`): nome, corpo, cores (pele, cabelo, olhos, com amostras), cabelo e barba, os 7 controles do corpo e os 7 do rosto, uma peça e uma cor por espaço (as do catálogo do jogo que servem àquele corpo), câmera; **prévia ao vivo**: o próprio avatar numa palco pequeno, filmado por uma câmera para uma textura, girando devagar. O rascunho é conferido pelas **mesmas regras da API** (`AvatarRules` do `shared`: nome de 3 a 64, cores `#RRGGBB`, faixas dos controles, camiseta/calça/sapatos obrigatórios, o que cobre o quê) com as mensagens em português; o que a API recusar volta com a primeira mensagem dela. Salvar é `PUT /api/v1/users/me/avatar`. **Primeira vez**: a API recusa a entrada com 409 até existir o avatar; o `GameApp` abre o criador sozinho sobre a tela de carregamento e, salvo o personagem, entra. Depois, "Mais > Meu personagem" o edita e a mudança aparece no avatar na hora. Foto: `ui-character-creator.png`.
- **Grupos** (`GroupsWindow`, `GroupDetailWindow`): meus, busca (entrar/pedir), criar, convites (aceitar/recusar); o grupo abre com descrição, membros (expulsar), pedidos (aprovar/negar) e convidar por identificador.
- **Pessoas e presença** (`PersonActionsWindow`, menu de presença na barra): na lista de pessoas por perto, "Ações" acha a pessoa na plataforma pelo nome (o mundo só conhece nomes) e oferece perfil, pedir amizade, escrever, **bloquear/desbloquear** e **denunciar** (motivo + segundo toque); a presença (online, ausente, ocupado, invisível) é enviada pelo menu e a dos outros aparece na janela.
- **Perfil editável** (`ProfileWindow`): o texto "sobre" e publicações (`PUT /me/profile/details`, `POST /me/profile/posts`) e o feed. Galeria, capa e casamento ficam para depois.
- **Mercado** (`MarketWindow`): produtos com compra (confirmação mostrando o preço; chave de idempotência nova a cada confirmação), pedidos, planos de aluguel de ilha (alugar com a mesma confirmação) e minhas lojas (criar, abrir/fechar, apagar). Publicar um produto precisa do envio de arquivo e vem com as ferramentas de item.
- **Gestão da ilha** (`IslandManageWindow`, `ParcelsPane`): dados (nome, descrição, categoria, pública, pessoas, tamanho; conferidos antes), permissões (dar/tirar), regras (quem entra e constrói, scripts, voz, mídia, devolução automática) e **terrenos** (lista, comprar com confirmação, pôr/tirar da venda, lista de permitidos e banidos, expulsar).
- **Eventos, marcos e fotos** (`DiscoveryWindow`): eventos públicos (participar/sair), os meus (criar, cancelar), marcos (salvar onde estou, ir, apagar) e fotos (a interface some por um quadro e a foto vai para a pasta `Photos` do jogo; a lista de fotos da plataforma também aparece). O **envio** da foto à plataforma precisa do envio de arquivo (`/assets/upload`) e não foi feito.
- **Itens** (`ItemWindow`, `OffersWindow`): direitos em três colunas (você, grupo, próximo dono) com copiar, modificar, transferir e revender, seguindo a regra do `shared` (revender liga transferir; tirar transferir tira revender) e a revisão esperada (um 409 recarrega); oferecer o item (com ficar com cópia); histórico; ofertas recebidas (aceitar/recusar) e transferências. O inventário ganhou as visões Atualizados e Devolvidos (objetos perdidos) e o botão Direitos.

**Verificação**: `-lifeverse-world-selfcheck` com 131 verificações (25 novas contra a API de mentira com os contratos reais, entre elas: o personagem salvo chega com todos os campos, o inválido não é enviado, a primeira vez abre o criador e entra, as compras levam chave de idempotência, o marco sai na posição do jogo, os direitos voltam com a revisão); 51 de 51 testes Edit (5 novos). Foto: `ui-character-creator.png`.

**Ainda não (e não foi inventado)**: ver e atualizar **versões** de um item (a rota de versões não está no grupo de rotas que li; só o filtro "Atualizados" existe), publicar produto no mercado, galeria/capa/casamento do perfil, mensagens de grupo e de ilha, o envio das fotos, parcelas em detalhe (dividir, juntar, ampliar, mídia, ambiente, histórico, auditoria), o editor de terreno e as janelas de pacotes de construção. O corpo "lifeverse_man" aparece com o corpo simples. Nada disso foi exercitado contra a API real, só contra as formas dos contratos.

### Fase 9, quarta parte: envio de arquivos, versões, perfil, mensagens, terrenos e o corpo do jogo

**Envio de arquivos.** `IslandEntryClient.UploadAsync/FileCallAsync` mandam um arquivo por `multipart/form-data` (`POST /api/v1/assets/upload`, campos `file` e `category`: objects, textures, sounds, animations, clothing, accessories ou models; até 100 MB) pelo mesmo caminho que renova o token, refazendo o corpo se for preciso. O jogo não tem diálogo nativo de "abrir arquivo", então há um navegador de arquivos próprio (`FilePickerWindow`: pasta atual, lugares comuns como Fotos do jogo, Imagens, Documentos e Downloads, filtro por extensão, caminho digitável, recusa acima de 100 MB). `Uploads.Read` transforma as respostas (409 "espaço cheio", 400/422 com o `detail` da API) em frases.
- **Fotos**: "Enviar uma foto à plataforma" sobe a imagem como textura e a registra em `POST /discovery/photos` (privada até a pessoa torná-la pública).
- **Produtos**: a aba "Vender" do mercado envia o arquivo (ou usa um identificador que já existe), confere nome e preço e publica (`POST /economy/products`, direitos padrão do `shared`, ou todos se "pode revender").
- **Versões de item** (`ItemWindow`): `GET /api/v1/assets/{id}/versions` (a rota existe no grupo de assets do backend; antes eu a procurava no lugar errado), trocar para uma versão (`/activate`, com confirmação) e publicar uma nova a partir de um arquivo.
- **Perfil** (`GalleryWindow`, `MarriageWindow`): galeria (enviar imagem, principal, capa, remover), capa e casamento (pedidos, aceitar/recusar/retirar, pedir, divorciar com confirmação).
- **Mensagens**: `ConversationPane` lê e escreve `/api/v1/social/messages` para o grupo (`groupId`) e para a ilha (canal `local` com `islandId`); aparecem na janela do grupo e na aba "Ilha" do Social.
- **Terrenos** (`ParcelsPane`): grade de células da ilha (`CellGrid`, tamanho e `cellSize` lidos da API, até 24 x 24 quadrados) para **dividir**, **ampliar** e **encolher** (as células vão como pares [x, z], como o backend pede), **juntar** os terrenos marcados, **música** (URL http/https, volume, tocar sozinha), **ambiente** (JSON válido ou vazio), **auditoria** e **histórico**.

**O homem do LifeVerse (`lifeverse_man`).** O arquivo é o mesmo do Godot (`client/Assets/avatars/lifeverse_man.bin`, 53 MB, 18 ossos, 17 940 triângulos, 15 materiais). Não o copiei para dentro do repositório: um passo de pré-build (`BuiltInBodiesCopy`) o copia para `StreamingAssets/avatars` (ignorado pelo git) e o jogo o lê de lá **uma vez** (1,2 s no teste) e faz o corpo de cada pessoa a partir dessa única importação, com os materiais copiados por pessoa para o tom de pele (`Ga_Skin_*`) não vazar para os outros. Fica com 1,80 m, anima com os clipes do jogo, e entra na conta de triângulos do planejador de detalhe (longe, volta ao corpo simples). Ele nasce sem roupa, como no Godot ("começa como o modelo é"); as roupas dele (`client/Assets/wearables`) ainda não existem no repositório. Foto: `avatar-lifeverse-man.png`.

**Verificação**: `-lifeverse-world-selfcheck` com 149 verificações (18 novas: envio por multipart com os campos certos, foto registrada, versões, galeria e capa, casamento, conversa do grupo e da ilha, produto publicado, dividir/ampliar/juntar com as células escolhidas, música e ambiente inválidos não saem, o homem do LifeVerse carregado); 55 de 55 testes Edit (4 novos). **Ainda não**: roupas e acessórios do homem, publicar um novo item direto do inventário (sem o arquivo), as pendências que a quinta parte (abaixo) resolveu ou esclareceu: células do terreno e a API real.

### Fase 9, quinta parte: o cliente exercitado contra a API real

**Como se exercita a API real sem Postgres nem produção.** `tools/ApiLiveHost` sobe o backend de verdade (`WebApplicationFactory`, ambiente "Testing", banco **em memória**, `http://127.0.0.1:5099`) num processo local; nada toca a produção nem credenciais reais. O player roda `LifeVerse.exe -lifeverse-live-api http://127.0.0.1:5099` (`LiveApiCheck`): cria duas contas e usa **as mesmas janelas da pessoa** (login, personagem, ilhas, social, grupos, arquivos, economia, ofertas, perfil, eventos, gestão e terrenos, scripts) contra as respostas verdadeiras. Resultado: **14 etapas, 0 falhas**. Para repetir: `dotnet build tools\ApiLiveHost -c Release`, `dotnet run --project tools\ApiLiveHost -c Release --no-build -- 5099`, e rodar o player.

**O que o stub escondia (corrigido).** Caminho do inventário; formato do casamento (`current/incoming/outgoing`); parâmetro `page` obrigatório em produtos; nome do campo de favorito; origem das células (coordenadas centradas); direitos do item dentro de `permissions` com `revision` fora; versões em `{versions:[{version, at, size, current}]}`; e, nas recusas da API (400/409), a razão (`message`/`detail`/`error`) agora aparece na tela (`UiTasks.Explain(status, body)`), por exemplo na galeria. O stub do auto-teste passou a devolver esses mesmos formatos.

**Descobertas de comportamento (não são defeitos do cliente).**
- A plataforma guarda arquivos idênticos **uma vez**: subir os mesmos bytes que outra conta já subiu devolve `reused:true` com o asset **da outra pessoa**, que não dá para versionar nem pôr na galeria (404/400). O teste usa imagens diferentes a cada execução.
- Direitos do **grupo** só valem depois de o item ser compartilhado com um grupo (o servidor mantém zero antes); a janela edita direitos do grupo, mas o efeito exige o compartilhamento.
- Itens oferecidos e aceitos deixam de ser da pessoa: a galeria usa um arquivo próprio.

**Células do terreno.** A grade usa as células que a API devolve, mas também reconstrói as do terreno escolhido a partir das **faixas** (`[linha, primeira, última]`) das linhas da lista de terrenos, então não depende mais de `about` (grade de 64 x 64, centrada). Dividir, juntar e ampliar foram exercitados na API real.

**Inconclusivo (precisa de Postgres ou de um servidor de verdade).** A renovação do token de 15 min: a API aceita o pedido, mas o banco em memória não implementa `ExecuteUpdate`, então `/auth/refresh` dá 500 aqui; a lógica do cliente está provada só contra o stub. Entrada na ilha real (o `entry-token` exige o supervisor).

**O guarda-roupa do homem.** `client/Assets/wearables` não existe no repositório (nem no Godot, que também o carrega do servidor): não há arquivo para migrar. O homem segue sem roupa, como no Godot; quando houver as roupas, entram como anexos do esqueleto já existente.

**Verificação**: live 14/14; `-lifeverse-world-selfcheck` 150 verificações; 55 de 55 testes Edit.

### Fase 9, sexta parte: scripts de objetos, clique no mundo e assentos

**Editor Lua.** Dois botões novos: **Histórico** (`ScriptHistoryWindow`: as versões guardadas, o código da escolhida, restaurar; `GET .../scripts/{id}/versions`, `/versions/{n}`, `POST .../restore`) e **Documentação** (`ScriptDocsWindow`: `GET /api/v1/scripting/reference`, busca, filtro por categoria, detalhe com permissão, limites e erros, e "Inserir exemplo" no fim do script).

**O que scripts pedem à pessoa.** O servidor da ilha já mandava `script_dialog` e `script_hud` como operação de construção; o `WorldClient` agora separa essas duas do resto (`ScriptOperation`) e responde por `SendScriptResponse` (a ilha confere que a pergunta foi feita a essa pessoa). `ScriptDialogs`: uma pergunta por vez com fila (até 10), campo de texto, contagem regressiva, "Negar"/"Recusar" se fechada ou esgotada em permissão/pagamento. `ScriptHud`: até 8 painéis (texto colorido, botões, barras, campos), a atualização do script não apaga o que a pessoa digita, o X avisa a ilha.

**Clique no mundo.** Clicar num objeto (cursor livre, fora da interface) abre um menu com **Tocar** (`TouchObjectMessage`, o `on_touch` dos scripts), **Sentar aqui** e **Scripts do objeto**. O objeto é achado por um raio contra a caixa do que está desenhado (`WorldObjects.Pick`; os objetos não têm colisor no cliente).

**Assento num objeto.** Como no Godot, a ilha só fica sabendo que a pessoa senta (a mesma entrada `Sit` do chão); este computador a desenha em cima do objeto (`LocalPlayer.SitOnObject`, até 6 m) e ela levanta com X ou qualquer tecla de andar.

**Verificação**: auto-teste 156 verificações; exercício na API real, 14 de 14 (com histórico, restauração e a documentação verdadeira); 55 de 55 testes Edit.

**Fica de fora, com o motivo.** Dedos: o código só mexe em ossos de dedo que o corpo tenha (o `lifeverse_man` tem 18 ossos, sem dedos), então não há o que verificar com os corpos que existem. Anexos e acessórios personalizados (editor e armazenamento do Godot: `AttachmentStore`, `AttachmentEditor`): é um editor inteiro, vai com as ferramentas de construção da Fase 10. "Publicar item novo direto do inventário" também depende dessas ferramentas (o pacote de construção). Passos: dependem do áudio (Fase 11). Roupas do homem, renovação do token e entrada na ilha real: sem mudança (arquivos ausentes, Postgres e supervisor).

## Fase 10 (construção e terreno)

**Modo de construção (B)** — `BuildEditor` (lógica e quadro por cima da cena) + `BuildWindow` (painel com onze abas). Clicar escolhe objetos (Shift/Ctrl soma; o raio vai contra a caixa do que está desenhado), e três setas coloridas desenhadas sobre a cena **movem, giram ou esticam** (botões Mover/Girar/Esticar; grade de 0,25 m, 15 graus; Alt solta). Setas, Page Up/Down, Delete, Ctrl+D (copiar), Ctrl+Z/Y. Tudo é um comando que a ilha confere e a API decide: o que o mundo mostra é o que veio de volta (a prévia do arrasto volta ao confirmado se for recusada). A seleção toma uma **reserva de edição** na ilha (renovada a cada 8 s; "em edição por Ana" aparece para os outros e a ilha recusa). Um **envio com ritmo** (12 comandos por segundo) evita que uma construção grande estoure o limite de 20 por segundo da ilha. Desfazer/refazer de criar, mudar e apagar.
- **Criar** (12 formas), **Objeto** (valores exatos conferidos antes de enviar; empurrões; pousar no chão), **Forma** (oco, afunilar, torção, cortes, caminho, cisalhar, voltas: as `PrimParams` do `shared`), **Aparência** (cor, opacidade, aspereza, metal, brilho, dois lados, luz total, textura do inventário, repetição, **uma face só**, mídia https), **Física** (as regras do `shared`: colisão, físico, gatilho, forma, superfície, massa, atrito, quique), **Dados** (ação do clique, venda, quem pode mover/copiar, direitos do próximo dono, grupo, devolver), **Conteúdo** (o que o objeto carrega, até 64 itens), **Alinhar** (alinhar por eixo e distribuir, com a conta do `shared`), **Geral** (nome, descrição, proteger, vincular/desvincular, copiar, apagar, guardar a seleção como **construção** no inventário e colocar uma de lá — a que não pode ser copiada vai por `rez` da ilha), **Auditoria**, **Limites** (uso da ilha e do terreno).
- **Clique fora do modo** abre o menu do objeto com Tocar, Sentar, Scripts e, conforme o objeto: **Comprar por VC$ n** (a ilha cobra o preço de agora e recusa se mudou), **Pagar** e **Abrir** (o conteúdo).

**Terreno (T)** — `TerrainEditor` + `TerrainWindow`: elevar, abaixar, nivelar (com a altura sob o cursor), achatar, suavizar, rugosidade, erosão, restaurar e pintar os oito materiais; pincel redondo ou quadrado, tamanho, intensidade, suavidade (as opções vão empacotadas no mesmo inteiro da mensagem, `BrushCodec`); anel do pincel desenhado sobre o chão (amarelo, vermelho sem permissão); desfazer/refazer, nível do mar, exportar e importar o mapa de 16 bits (arquivo escolhido no navegador do jogo). A ilha decide quem esculpe.

**Verificação.** Auto-teste **177 verificações** (criar, selecionar, valores exatos e recusados, mover, desfazer/refazer, arrastar as setas nos três modos, material, cor de uma face, copiar, bloqueio de outra pessoa, apagar e desfazer o apagar, forma, física, conteúdo, alinhar, mídia, venda; terreno: pincelada muda o chão para todos, desfazer, pintar, mar, exportar, arquivo de tamanho errado recusado); **exercício na API real 15 de 15** (novo passo: limites, objeto criado e lido pelo formato do editor, auditoria, construção guardada e lida de volta, aparece no inventário); 57 testes Edit; 51 testes xUnit do servidor de ilha. Fotos: `ui-build-create.png`, `ui-build-data.png`, `ui-terrain-paint.png`.

**Dois defeitos reais que isto achou e corrigiu.**
- O servidor da ilha usa só a **última entrada** de cada passo, e alternar "sentar" é um toque de um passo: com o cliente a 60 Hz e a ilha em menos, o toque se perdia ao acaso (sentar não pegava, levantar não pegava). A ilha agora **guarda o toque de sentar** que a entrada seguinte substituiu antes do seu quadro (`IslandServer.OnInput`), e o cliente usa o que a ilha confirmou (`LocalPlayer.ServerSeated`) em vez da previsão, que pisca entre o toque e a confirmação.
- O evento de operação chegava ao editor antes de o mundo ter o objeto (a seleção do objeto recém-criado falhava): agora o mundo aplica primeiro.

**O que o Godot tem e isto ainda não faz (nada disso estava no que foi pedido para verificar; fica registrado).** Imã dos cantos/arestas (`SnapMath` existe no `shared`, não está ligado ao arrasto); mostrar para os outros o arrasto ao vivo (`live`); escolher a **face clicando** no objeto (hoje é uma lista de faces); mapas de normal/ORM/emissivo e rotação da textura no painel (o mundo já os desenha); **tocar o vídeo/imagem da mídia** no objeto (o painel grava a mídia no material, mas o desenho dela fica para a Fase 11 com o áudio); prévia translúcida do resultado da pincelada do terreno; edição de terreno por quem só é dono/locatário de um terreno (depende da ilha liberar; hoje só editores da ilha); zoom da câmera no objeto; objetos perdidos e o tutorial de construção; diálogo próprio de compra/pagamento (hoje é o botão de dois toques).

### Fase 10, segunda parte: o que tinha ficado de fora

**Imã.** Ao mover, a seleção é puxada para os cantos, meios de aresta e centros de face dos vizinhos (e uma face encosta na do vizinho): a conta é a `SnapMath` do `shared`, o ponto onde ela encostou aparece como um anel amarelo, Alt solta. **Arrasto ao vivo.** Quem arrasta manda a pose a cada 0,1 s (`live`); os outros veem o objeto andar sem nada ser salvo, e um arrasto que acabou sem salvar volta ao que a ilha tem (a ilha escreve esses nomes com inicial maiúscula; o cliente lê dos dois jeitos). **Escolher a face clicando**: "Escolher a face clicando no objeto" na aba Aparência; o raio testa os triângulos da malha (`WorldObjects.PickFace`) e a lista de faces vem do objeto (uma caixa oca tem as seis e mais as de dentro). **Zoom no objeto** ("Dar zoom" no menu do clique; andar ou Esc volta à pessoa). **Tutorial de construção**: oito tarefas (`BuildTutorial` do `shared`) que terminam sozinhas quando a pessoa faz a coisa (criar, mover, tamanho, girar, cor, copiar, vincular, guardar); dá para pular e repetir.

**Terreno.** A **prévia** tenta a pincelada no chão local, lê as alturas em três anéis e põe tudo de volta exatamente como estava; os anéis ficam na altura nova (ciano; vermelho onde o terreno não é seu). **Quem é só dono, locatário ou construtor de um terreno** esculpe dentro dele: o cliente lê os terrenos da ilha (`canEditTerrain`), monta o mesmo `LandTerrainRights` do `shared`, só envia pinceladas cujo ponto central está dentro de um terreno seu, mostra a borda em vermelho, e a ilha confere de novo; mar, importar e exportar seguem só dos editores da ilha.

**Mídia desenhada.** Imagem: baixada (https, até 16 MB) e posta como a cor do objeto, numa cópia do material só daquele objeto; vídeo: `VideoPlayer` num `RenderTexture` que vira a cor do objeto (e o brilho, se pedido), tocando ou parado conforme `playing`; tirar a mídia tira o tocador. O som do vídeo espera o sistema de áudio (Fase 11). Para as pessoas só vale endereço https público (a mesma regra do painel e da ilha); só os testes aceitam arquivo local.

**Compra, pagamento e objetos apagados.** `PurchaseWindow` (o que leva, de quem é, preço, saldo, avisa saldo insuficiente ou carteira bloqueada; a recusa da ilha volta como frase na janela; a compra feita mostra o que foi para o inventário), `PaymentWindow` (o preço do objeto e os valores rápidos do dono como botões; confere o valor; transferência ao dono) e `LostObjectsWindow` ("Objetos apagados…" na aba Geral: lista, escolher, recuperar os escolhidos ou todos; vão para o inventário em "Devolvidos e recuperados").

**Defeito do servidor que isto ajudou a achar (continuação do da primeira parte).** A ilha usa só a última entrada de cada passo; além de "sentar", o **pulo** também podia se perder (um teste de pulo falhava ao acaso). Agora os dois são guardados quando a entrada seguinte os substitui antes do quadro. O cliente ainda repete o pedido de sentar/levantar se a ilha, tendo usado a entrada, não confirmou o que foi pedido (no máximo quatro vezes, com 1,2 s entre elas).

**Verificação.** Auto-teste **184 verificações** (3 rodadas seguidas sem falha); exercício na API real 15 de 15; 51 testes xUnit do servidor de ilha. Depois de reativar a licença, tudo foi rodado de novo: testes Edit 57 de 57, exercício na API real 15 de 15 (já com a lista de objetos apagados e a janela de compra contra a carteira real e o perfil verdadeiros), auto-teste 184.

## Fase 11 (áudio e voz)

Novo assembly `LifeVerse.Unity.Audio` (abaixo do Avatar e da interface, acima do mundo).

- **Cinco grupos de volume** como no Godot: Ambiente, Efeitos, Música, Mídia e Voz, todos sob o Geral. O volume de cada som é o do grupo vezes o Geral (`Buses`, `BusSource`); os cursores da janela de configuração agora são sete (Geral, Música, Efeitos, Voz, Ambiente, Mídia) e mexem no que já está tocando. Também novos e salvos: sensibilidade do microfone, "cancelar eco" e "tocar a música dos terrenos".
- **As três gravações do jogo** (passos em concreto, teclado, tema da tela de entrada: `client/Assets/audio`, 5 MB) são copiadas para `StreamingAssets/audio` antes do build (`SoundsCopy`, fora do git, como o corpo do homem) e lidas uma vez (`SoundLibrary`). O tema toca em círculo na tela de entrada e some em 1,5 s ao entrar no jogo.
- **Paisagem sonora** (`AmbientSound`): o mar, o vento e a chuva do `AmbientSynth` do `shared` (sem arquivos), com as mesmas regras do Godot (`AmbientLevels`): o mar sobe perto da praia e é inteiro na água, o vento com a altura e o temporal, a chuva com o tempo; o chapinhar de quem nada.
- **Passos e teclado** (`AvatarSounds`, em cada avatar, em 3D): andando ou correndo no chão toca a gravação dos passos em círculo (mais rápida correndo, cada avatar começa num ponto), digitando toca o teclado nas mãos; somem em fade quando para, e não há nem um nem outro na água.
- **O que os scripts tocam** (`WorldSounds`): `sound`/`sound_stop` (arquivo de som da ilha, wav/mp3/ogg, volume, em círculo ou não, no lugar do objeto) e `media` (mp3 ou ogg de endereço https, baixado aqui sob as mesmas regras da ilha: sem rede interna, sem redirecionamento, 12 MB, no máximo 8 de uma vez; a mesma jogada que muda de lugar ou volume não recomeça). Quando um som sem repetição acaba, o cliente avisa a ilha.
- **Música do terreno** (`LandMusic`): lê os terrenos da ilha, descobre qual está sob a pessoa, e toca o que o dono pôs (endereço https, volume, "tocar sozinha", nome); muda ao andar para outro terreno, chão aberto não tem música, a pessoa pode desligar tudo, e a recusa diz a razão (sem https, rede interna, não é mp3/ogg).
- **Voz por proximidade** (`VoiceChat`, `VoiceListener`, `VoiceOut`; o codec Opus é o do `shared`, o `Concentus.dll` já estava no projeto): segurar **G** fala, o botão **Voz** da barra deixa o microfone aberto; quadros de 20 ms a 16 kHz, 50 pacotes por segundo; microfone aberto só manda quando há fala (comporta com tempo de espera pela sensibilidade); "cancelar eco" não manda enquanto se ouve alguém; quem escuta decodifica um falante por vez, com colchão de 60 ms, pacote perdido preenchido pelo codec, e a voz sai da posição do falante em 3D com alcance de 25 m. O nome de quem fala fica verde. A janela de configuração tem **Testar microfone** (barra de nível; nada é gravado nem enviado) e o microfone só abre quando a pessoa pede ou fala pela primeira vez.
- **Vídeo no objeto** (fecha a pendência da Fase 10): o `VideoPlayer` agora toca o som por uma fonte 3D do objeto, no grupo Mídia.

**Verificação.** Auto-teste **190 verificações** (as três gravações lidas de verdade, volumes, paisagem sonora, passos/teclado, sons e mídia de script incluindo recusas, mp3 de verdade decodificado, música do terreno com todas as regras, voz de ponta a ponta entre dois clientes por cima do servidor de ilha); 62 de 62 testes Edit (5 novos); exercício na API real 15 de 15. **Não dá para verificar daqui** o que só um ouvido ou um microfone de verdade confirmam: se o som sai bonito nas caixas, a captura do microfone da pessoa (o teste alimenta amostras no lugar dele) e o som real do vídeo.
**Fechado depois (o que tinha ficado de fora).**
- **Música da ilha inteira**: a rota é `GET /api/v1/worlds/islands/{id}/environment`, que traz `media` (`musicUrl`, `volume`, `autoplay`, `name`) ao lado de `look`. O `IslandLookService` (que já lia o ambiente a cada 30 s) agora também lê isso (e trata endereço que as regras recusam como "sem música"); a `LandMusic` toca a do terreno e, onde nenhum terreno tem a sua, a da ilha ("música da ilha" se não tiver nome). Um terreno com música própria que não toca sozinha fica quieto (não cai para a da ilha).
- **Microfone à escolha** (`MicDevices`): a lista de microfones (sem repetidos nem vazios, o padrão do sistema na frente), a escolha salva, um que foi desconectado volta ao padrão, e trocar com o microfone aberto abre o novo. **Saída**: a Unity não deixa um programa escolher o dispositivo de saída; o jogo segue o padrão do sistema e a configuração diz isso (mudar no Windows vale na hora).
- **Sons da interface** (`UiSounds`): clique, interruptor, abrir e fechar janela, e um aviso para cada tipo (comum, bom, alerta, ruim); feitos na hora (um tom curto com subida e descida suaves, sem arquivo), planos, no volume dos Efeitos à metade, sem repetir o mesmo em 40 ms, com opção de desligar na configuração.

**Verificação agora.** Auto-teste **193 verificações**; testes Edit **64 de 64**; exercício na API real 15 de 15. Continua sem poder ser conferido daqui o que depende de ouvido e de microfone de verdade.

## Fase 12 (sobras das fases 7 a 12)

Verificado em 2026-11-20 (nada commitado): EditMode 72/72, auto-teste do mundo 198 checks, API real 16/16 passos, xUnit 51.

| Item | Onde | Estado |
|---|---|---|
| Teclas redefiníveis | `Avatar/KeyBindings.cs`, aba Controles da `SettingsWindow` | Feito: 23 ações, troca de tecla em conflito, salvo em `settings.json`, ignorado ao digitar |
| Mapas de textura no painel | `BuildTabs.cs` (campos de mapa, `ApplyMap`), edição otimista com revisão | Feito |
| Importar modelo (GLB, glTF, DAE) | pasta `Models/`, `UI/ModelImportWindow.cs` | Feito: prévia, tamanho, colisão, conversão de DAE, empacotamento para envio |
| Anexos ao corpo | `Avatar/AvatarAttachments.cs`, `UI/AttachmentsWindow.cs`, "Mais > Anexos do corpo" | Feito: lê `bodyAttachmentsJson`, prende o modelo ao osso/parte do corpo com o desvio do ponto, posição, rotação e escala (z invertido); janela para vestir, tirar, ajustar e voltar ao padrão |
| Pivô | | Não feito de propósito: o cliente Godot também só guarda o dado |
| Animações dentro do modelo | | Não iniciado |
| LOD de malha | | Adiado: exige um simplificador próprio |

Lacunas dos anexos: pontos de tela (HUD) não são desenhados no corpo (o cliente ainda não tem uma camada para eles); mover de ponto (`/move`) e o balanço (`sway`) não têm interface; o corpo simples usa a parte mais próxima do corpo quando o osso não existe.

Provas: `AttachmentTests` (6), passo "avatar: attachments" da API real (vestir, ler, ajustar, tirar), e no auto-teste um chapéu carregado de um GLB preso à cabeça, movido e retirado, mais a janela contra o stub.