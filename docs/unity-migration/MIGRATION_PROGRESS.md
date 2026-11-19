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
| 1 Auditoria | Concluído (revisão pendente) | `MIGRATION_AUDIT.md`. Limite: classificação por arquivo, não por classe |
| 2 Arquitetura | Em análise | `MIGRATION_ARCHITECTURE.md`. Faltam a Fase 2b e as decisões D3 e D4 |
| 3 Fundação Unity | Em validação | Gate cumprido, exceto IL2CPP e render visual (acima). D1, D2 e D7 foram executadas |
| 4 Código compartilhado | Não iniciado | Rodar o conjunto completo de testes de `shared` dentro da Unity; planilha dos 117 smokes |
| 5 Multiplayer | Em migração | Protocolo e transporte feitos e testados (`WIRE_PROTOCOL.md`). Faltam: interpolação e reconciliação no cliente, interesse e tick no servidor, 64 clientes simultâneos e latência alta; dependem das Fases 6 e 7 |
| 6 Servidores | **Em migração (parcial)** | Núcleo sem engine (`unity/UnityIslandServer/Core`) com admissão, simulação, snapshots por interesse, chat, voo, gestos, persistência, heartbeat/comandos do admin, saúde, métricas e encerramento; roda como processo .NET (`Host`) e como Unity Dedicated Server. 45 testes xUnit; 64 clientes UDP contra o servidor dentro do player Unity. Terreno editável (por editores da ilha) portado. Veículos, construção, terras (land-sync, acesso, teletransporte), terreno por donos de parcela voz, aparência e anexos portados. Também portados: terreno editável, veículos, construção com locks e terras (land-sync, acesso, teletransporte). **Faltam** scripts de objetos (`IslandScripting`), física de objetos (S4) e a execução no Linux. Ver `MIGRATION_ISLAND_SERVER.md` |
| 7 Mundo | **Iniciada (C1 a C3 em migração)** | `unity/UnityClient/Assets/LifeVerse/World`: terreno, oceano, céu e clima renderizados no HDRP e fotografados na GPU real; 17 testes de modo Edit. Ver a seção "Fase 7" abaixo |
| 8 Avatar, movimento e câmera | **Iniciada** | `unity/UnityClient/Assets/LifeVerse/Avatar`: movimento com previsão, corpos procedurais de todos os jogadores, câmera de terceira pessoa. Ver "Fase 8" |
| 9 a 15 | Não iniciado | — |

## Módulos

| Módulo | Origem | Destino Unity | Implementação | Compilação | Testes | Pendências | Próxima ação |
|---|---|---|---|---|---|---|---|
| Backend | `backend/` | inalterado | Não muda | OK, 0 avisos | 2.558 passam, 1 falha anterior (`HighJumpTests`) | — | Nenhuma |
| `shared` | `shared/` | `LifeVerse.Shared.dll` | Implementado, em validação | OK (netstandard2.1, 0 erros) | Codecs, terreno, hash, grupos, Opus e NPC passam no editor e no player | Rodar a suíte completa na Unity; IL2CPP | Fase 4 |
| `scripting` | `scripting/` | `LifeVerse.Scripting.dll` | Implementado, em validação | OK | Compilador Lua e timer do `ScriptHost` passam no editor; compilador Lua passa no player | IL2CPP; sandbox e limites no player | Fase 4 |
| Pipeline de build | — | `build-plugins.ps1`, `ProjectSetup`, `BuildClient` | Implementado, validado | OK | Build Windows reproduzível | Build do servidor (D4) | — |
| Rede (protocolo e transporte) | `NetworkBridge` (44 RPCs) | `unity/UnityWire` (`LifeVerse.Wire`) | Implementado, em validação | OK (`netstandard2.1` e `net8.0`, 0 avisos; solução inteira 0 avisos) | 25 xUnit + 2 na Unity + autoverificação do player: passam | Servidor de ilha e cliente de jogo; 64 clientes; latência alta | Fase 6 |
| Servidor de ilha | `island-server/` | `unity/UnityIslandServer` (`Core`, `Host`, `LoadTest`) + `Assets/LifeVerse/Server` | Parcial: ver paridade em `MIGRATION_ISLAND_SERVER.md` | `Core` net8.0 e netstandard2.1 OK (0 avisos); Build Linux OK (162 MB) e executado no Ubuntu/WSL 2 | 51 xUnit; 64 clientes contra o player Unity (Windows/Mono); executado no Ubuntu (WSL 2): 64 clientes, SIGTERM; Alpine não suporta o player Unity (só o `backend/` é Alpine; as ilhas já são Debian) | Terreno por donos de parcela, scripts (`IslandScripting`), física (S4), voz, `engine`/`wireVersion` na API, Supervisor | Fase 6 (continuação) |
| Mundo 3D, Jogador, Avatar, Construção, UI, Social, Áudio, Distribuição | ver checklist | `LifeVerse.Unity.*` | Não iniciado | — | — | — | Fases 7 a 15 |

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