# Migração para Unity 6 HDRP — Checklist de funcionalidades

Cada linha é uma funcionalidade do LifeVerse com a sua correspondência planejada na Unity. A classificação é **por área** (medida no nível de arquivo na auditoria). A classificação **por classe** é a Fase 2b e será registrada aqui antes de a área ser migrada.

Classes: **R** = reutilizável sem mudança (DLL de `shared`/`scripting`/`backend`); **A** = reutilizável com adaptação; **E** = depende da engine, reimplementar; **O** = obsoleta somente após substituição comprovada.
Estados permitidos: Não iniciado · Em análise · Em migração · Implementado, não testado · Em validação · Concluído · Bloqueado.
Critério de "Concluído": compila, testes relevantes passam, comportamento original preservado, integração funciona, sem regressão crítica conhecida.

## A. Plataforma e infraestrutura

| Id | Funcionalidade | Origem (Godot/repo) | Destino Unity | Classe | Estado |
|---|---|---|---|---|---|
| A1 | Backend ASP.NET + PostgreSQL | `backend/` | inalterado | R | Concluído (não muda) |
| A2 | Contratos HTTP e modelos de dados | `backend`, `client/Scripts/*Api.cs`, `*Models.cs` | clientes HTTP em `LifeVerse.Unity.Platform` | A (27 arquivos do cliente já sem Godot) | Não iniciado |
| A3 | Regras e codecs compartilhados | `shared/` (71 de 73 arquivos sem Godot) | DLL `LifeVerse.Shared` (D2) | R | Em validação: compila e passa em 10 testes no editor e em 5 verificações no player Mono; falta IL2CPP e a suíte completa |
| A4 | Runtime Lua | `scripting/` (MoonSharp) | DLL `LifeVerse.Scripting` | R | Em validação: compilador Lua e timer do `ScriptHost` passam no editor; falta IL2CPP e os limites no player |
| A5 | Supervisor de ilhas | `island-supervisor/` | inalterado ou adaptado ao novo servidor | R/A | Não iniciado |
| A6 | Testes xUnit (1.842) | `tests/` | continuam rodando contra `shared`/`backend`; executar `shared` também no Test Runner da Unity | R | Não iniciado |
| A7 | Autenticação, tokens, refresh rotativo | `AuthManager`, `SessionManager`, `SavedLogin` | `LifeVerse.Unity.Platform` | A | Não iniciado |
| A8 | Verificação de versão e atualização | `UpdateChecker`, `launcher/`, `installer/` | mesmo manifesto e launcher; instalador novo | A | Não iniciado |
| A9 | CI e build de release | `.github/workflows/release.yml`, `scripts/` | build Unity por linha de comando | E | Não iniciado |

## B. Rede e servidor de ilha

| Id | Funcionalidade | Origem | Destino | Classe | Estado |
|---|---|---|---|---|---|
| B1 | Transporte e framing | `NetworkManager`, `NetworkBridge` (44 RPCs) | LifeVerse Wire (D3): `unity/UnityWire`, `WIRE_PROTOCOL.md` | E | Em validação: 44 mensagens, 25 testes xUnit e 12 testes na Unity passam; falta ligar ao servidor e ao cliente de jogo (Fases 6 e 7) |
| B2 | Admissão por ticket assinado e versão | `ServerMain`, API `IslandEntry` | mesma API; ticket com `engine`/`wireVersion` | A | Não iniciado |
| B3 | Tick autoritativo, snapshots, interesse | `ServerMain`, `SnapshotCodec` | `UnityIslandServer` + codec existente | A | Não iniciado |
| B4 | Interpolação e reconciliação do cliente | `NetworkManager` | `LifeVerse.Unity.Networking` | E | Não iniciado |
| B5 | Reconexão e suspensão de avatar | `HandleConnectionLost`, `ServerMain` | idem | A | Não iniciado |
| B6 | Chat local, privado e de grupo | `SendChat`/`ReceiveChat`, `SocialPanel`, backend Social | idem | A | Não iniciado |
| B7 | Voz (Opus, relay, indicador) | `VoiceChat`, `VoicePlayback`, `ServerVoice`, `shared/Voice` | codec reaproveitado; captura/reprodução novas | A | Não iniciado |
| B8 | Teleporte e reservas | `TeleportCoordinator`, `ServerTeleport`, `shared/Land/LandTeleport` | idem | A | Não iniciado |
| B9 | Gestos, olhar, anexos ao vivo | `Gesture*`, `HeadLook*`, `AttachmentLive*` | idem | A | Não iniciado |
| B10 | Veículos | `VehicleClient/View`, `ServerVehicles`, `shared/Vehicles` | idem (física em `shared`) | A | Não iniciado |
| B11 | NPCs de scripts | `shared/Networking/NpcWorld`, `ScriptHost.Npc` | idem | R/A | Não iniciado |
| B12 | Física de objetos dinâmicos | `ServerPhysics` (RigidBody3D/StaticBody3D) | PhysX no servidor Unity (D4) | E | Não iniciado (spike S4) |
| B13 | Registro, heartbeat, health, logs JSON, métricas | `ServerMain`, `ServerConfiguration`, API Platform | idem | A | Não iniciado |
| B14 | Reinício de região e limites de recursos | World Manager (API), `ServerMain` | idem | A | Não iniciado |
| B15 | Sincronização de física no cliente | `PhysicsSync` | `LifeVerse.Unity.World` | E | Não iniciado |

## C. Mundo 3D

| Id | Funcionalidade | Origem | Destino | Classe | Estado |
|---|---|---|---|---|---|
| C1 | Terreno (alturas, camadas, patches, edição) | `IslandTerrain`, `TerrainEditor`, `shared/Terrain` | malha própria + shader HDRP (D5) | A (dados) / E (render) | **Em migração**: malha e textura de chão (8 camadas misturadas na CPU) renderizadas e testadas; **aplica o mapa, as edições, os patches de desfazer e o nível do mar vindos do servidor** (rebake parcial da textura); **colisão local do terreno** (`MeshCollider`, raycast e altura do chão iguais às do mapa); falta o editor de terreno (UI) |
| C2 | Oceano e nível do mar | `OceanSurface`, `WaterVolume`, `shared` | água HDRP ou shader próprio; vista de baixo da superfície | E | **Em migração**: plano que segue a câmera, duas camadas de ondas, transparência, vista de baixo e névoa submersa renderizados; o nível do mar acompanha o servidor; **ondas na malha (CPU, LOD radial)**; **espuma da orla por pixel** (`ShoreFoam`: shader HLSL do HDRP que lê a profundidade da água de uma textura de alturas do terreno e pulsa como a arrebentação; segue edições do terreno e a maré); falta a cor da água por profundidade |
| C3 | Céu, hora, clima, neblina, luz | `IslandEnvironmentController`, `IslandLookService` | Sky/Fog/Exposure do HDRP + Volumes | E | **Em migração**: hora, sol, céu em gradiente, nuvens, névoa e clima (limpo, nublado, chuva, tempestade, neblina) e visual de parcela; **chuva em partículas**, céu cinza e névoa por clima; **disco do sol** (esfera emissiva a 1,8 km que some à noite e atrás de tempestade) e **leitura do ambiente salvo da ilha** (`IslandLookService`: pede `/environment` ao entrar e a cada 30 s; hora, clima, luz, névoa e tinta do céu entram em 3 s). Falta o halo em volta do disco |
| C4 | Música e mídia de terreno e ilha | `LandPresence`, `MediaDownload`, `LandManager` | idem | A | Não iniciado |
| C5 | Parcelas, vendas, aluguel, acesso, banimento | `LandManager` (+5), `LandManagement`, `RealEstate`, `shared/Land`, backend Land | UI nova; regras reaproveitadas | A | Não iniciado |
| C6 | Objetos do mundo e LOD | `BuildObjectView`, `ModelCache`, `ModelLod` | `LifeVerse.Unity.World` | E | **Em migração**: primitivas (a mesma geometria do `shared`: caixa, prisma, cilindro, tubo, cone, pirâmide, esfera, hemisfério, toro, anel, plano) com um material por face (cor, transparência, brilho, rugosidade, metal, dupla face); criar, atualizar, excluir, mover e poses de física vindos do servidor. **Texturas** (cor base com repetição e deslocamento, normal, ORM reempacotado para a máscara do HDRP, emissiva; PNG e JPEG, no máximo 2048), **modelos importados** (glTF/GLB pelo glTFast, normalizados para 1 m e centrados; um cubo no lugar até chegar), **árvores e grama** geradas (tronco e duas copas; 18 folhas) e **corte por distância** (alcance = maior escala × 120 m, entre 40 e 600 m, histerese de 10 %). Faltam: WebP (a Unity não lê), mapeamento planar, rotação e repetição separadas por mapa, sombreamento Blinn-Phong, animações dos modelos, pivô, colisão dos objetos, e simplificação de malha para o LOD (só o corte existe) |
| C7 | Efeitos de objeto (partículas, sons, mídia) | `ObjectEffects` (+2) | VFX/ParticleSystem e `AudioSource` | E | Não iniciado |
| C8 | Carregamento e streaming de recursos | `AssetLoadingService`, `LoadingScreen`, `shared/Loading` | idem | A | Não iniciado |
| C9 | Mapa e minimapa | `Map/`, `MiniMap`, `LandMapWindow` | UI nova | E | Não iniciado |

## D. Jogador, avatar e câmera

| Id | Funcionalidade | Origem | Destino | Classe | Estado |
|---|---|---|---|---|---|
| D1 | Movimento (andar, correr, pular, voar, nadar, sentar) | `PlayerController`, `shared/Networking/AuthoritativeMovement`, `MovementStateResolver` | seguidor cinemático + `shared` | A | **Em migração**: `LocalPlayer` prevê com o mesmo `AuthoritativeMovement` a 60 Hz, envia a entrada, reconcilia pela última entrada confirmada; andar, correr, pular, voar (`SetFlightMessage`) testados contra o servidor real. Faltam nadar visto de dentro, sentar em assentos, veículos e obstáculos de objetos na previsão |
| D2 | Câmeras (terceira, primeira, livre, construção, foto) | `PlayerController`, `BuildCamera`, `Camera/`, `shared/Camera` | Cinemachine ou câmera própria | E | **Em migração**: câmera de terceira pessoa (`FollowCamera`: órbita com o botão direito, roda, não entra no terreno). **Primeira pessoa** (V; bob, sway e olhos estáveis do `shared`). Faltam colisão dos olhos, livre, construção e foto |
| D3 | Entrada e atalhos | `project.godot` [input] (19 ações) | Input System | E | Não iniciado |
| D4 | Avatar procedural e corpos GLB | `AvatarRig` (+11), `BuiltInBodies`, `shared/Avatar` | `LifeVerse.Unity.Avatar` | E | **Em migração**: corpo procedural simples (`AvatarView`: tronco, cabeça com nariz, braços e pernas articulados) para todos os jogadores, interpolado pelos snapshots. Faltam o corpo do `HumanoidTemplate`, corpos GLB, aparência (`AppearanceJson`) |
| D5 | Criação e personalização de personagem | `CharacterCreator` (+5), `AppearanceWindow` | UI nova | E | Não iniciado |
| D6 | Roupas, acessórios, anexos em ossos | `AvatarRig.Wearables/Attachments`, `Attachments/`, `shared/Avatar/AttachmentPoints` | idem | E | Não iniciado |
| D7 | Animações padrão, gestos, digitação, passos | `DefaultAvatarAnimations`, `AvatarRig.*`, `tools/blender` | Animator/Playables | E | Não iniciado |
| D8 | Nível de detalhe por distância | `AvatarRig.Detail`, `shared/Avatar/AvatarDetailPlanner` | idem | A | Não iniciado |
| D9 | Menu radial e interação contextual | `RadialMenu`, `ContextInteraction`, `shared/Gameplay/RadialMath` | UI nova | E | Não iniciado |
| D10 | Mãos em primeira pessoa e olhar | `AvatarRig.FirstPerson/Look/Fingers` | idem | E | Não iniciado |

## E. Construção

| Id | Funcionalidade | Origem | Destino | Classe | Estado |
|---|---|---|---|---|---|
| E1 | Criar, selecionar, mover, girar, escalar, copiar, vincular, desfazer | `Build/` (37), `BuildObjectView` | `LifeVerse.Unity.Building` | E (matemática em `shared/Building` é R) | Não iniciado |
| E2 | Gizmos em tempo de execução | `Build/` + `shared/Building/GizmoMath` | desenho próprio em runtime | E | Não iniciado |
| E3 | Materiais, texturas por face, transparência, brilho, mídia em superfície | `MaterialBuilder` (shader), `BuildObjectView.Media` | HDRP/Lit por submalha | E | Não iniciado |
| E4 | Física e ações de clique de objeto | `shared/Building/PhysicsRules`, `ObjectInteraction` | idem | A | Não iniciado |
| E5 | Pacotes de construção, presets, limites e permissões | `shared/Building`, `Build/BuildPackageManager`, `BuildPermissionManager` | idem | A | Não iniciado |
| E6 | Importação de modelos (GLB, glTF, DAE) e validação | `ModelImportDialog`, `ModelInspector`, `GltfUploadPackager`, `ColladaConverter`, `shared/Avatar/*Validator`, `shared/Building/ModelLimits` | conversor reaproveitado (`ColladaConverter` não usa Godot); leitor de GLB novo (D7) | A/E | Não iniciado |

## F. Interface (reconstrução em UI Toolkit, D6)

| Id | Tela | Origem | Estado |
|---|---|---|---|
| F1 | Login, registro, seleção de ilha, carregamento | `LoginScreen`, `Login/` (13), `LoadingScreen`, `StartPlace` | Não iniciado |
| F2 | HUD, barra superior e inferior, notificações | `ViewerUI` (+3), `Interface/`, `Notifications/` | Não iniciado |
| F3 | Chat, mensagens privadas, amigos, pessoas | `SocialPanel`, `PeoplePanel`, `PersonActions` | Não iniciado |
| F4 | Grupos | `GroupsWindow` (+5), `Groups/` (9) | Não iniciado |
| F5 | Perfil | `ProfileWindow` (+5), `ProfileApi` | Não iniciado |
| F6 | Inventário, versões, direitos de item, ofertas, objetos perdidos | `InventoryPanel` (+1), `AssetVersionsWindow`, `ItemRightsWindow`, `ItemOffersWindow`, `LostObjectsWindow` | Não iniciado |
| F7 | Busca e descoberta | `SearchWindow` (+8), `DiscoveryPanel` | Não iniciado |
| F8 | Editor de construção (painéis) | `Build/` | Não iniciado |
| F9 | Configurações (gráficos, áudio, controles, rede, interface, acessibilidade) | `SettingsPanel`, `Settings/` (16), `GraphicsQuality`, `Accessibility` | Não iniciado |
| F10 | Economia: carteira, compra e transferência de VerseCoin, marketplace | `WalletWindow`, `MarketplacePanel`, `EconomyRates` | Não iniciado |
| F11 | Gestão de ilha e de terreno | `IslandManagement/` (19), `LandManagement/`, `RealEstate/`, `ParcelInfo/` | Não iniciado |
| F12 | Editor Lua, documentação, histórico, diálogos e HUD de script | `LuaScriptEditor`, `ScriptEditorWindow`, `ScriptDocsWindow`, `ScriptHistoryWindow`, `ScriptDialogWindow`, `ScriptHudLayer`, `HudEditor` | Não iniciado |
| F13 | Tema visual (vidro escuro, `#1B262E`, `#6D818D`, `#12F2E0`, `#F4A340`, Inter, cantos 14–16 px) | `ViewerTheme`, `UiStyle`, `UiDraw`, `FloatingWindow` | Não iniciado |
| F14 | Sobreposição de desempenho | `PerformanceOverlay` | Não iniciado |

## G. Inventário, economia e scripting

| Id | Funcionalidade | Origem | Destino | Classe | Estado |
|---|---|---|---|---|---|
| G1 | Inventário (pastas, itens, lixeira, recebidos, transferências, direitos) | backend Inventory, `shared/Items`, `InventoryPanel` | API inalterada; UI nova | R/E | Não iniciado |
| G2 | VerseCoin (saldo, compra, transferência, histórico, marketplace, aluguel) | backend Economy, `WalletWindow`, `MarketplacePanel` | API inalterada; UI nova. **O servidor continua sendo a autoridade financeira.** | R/E | Não iniciado |
| G3 | Scripts Lua em objetos (eventos, timers, HUD, mídia, NPC, vendas, terrenos) | `scripting`, `IslandScripting` (+12) | `IScriptWorld` implementado no servidor Unity | R/A | Não iniciado |
| G4 | Mídia (música, URL, vídeo em superfície) | `LandPresence`, `ObjectEffects.Media`, `BuildObjectView.Media`, `MediaDownload` | idem | A | Não iniciado |

## H. Qualidade

| Id | Item | Estado |
|---|---|---|
| H1 | Inventário e plano de migração (6 documentos) | Em migração (este conjunto de documentos) |
| H2 | Projeto Unity 6 HDRP criado e compilando | Concluído (renderização visual ainda não verificada) |
| H3 | Spikes S1 a S6 (ver `MIGRATION_RISKS.md`) | S1 em validação (passou no editor e no player Mono; falta IL2CPP); S2 a S6 não iniciados |
| H4 | Presets gráficos Baixo/Médio/Alto/Ultra | Não iniciado |
| H5 | Build Windows do cliente e Linux do servidor | Cliente Windows (Mono): construído e executado; servidor Linux: compila (161 MB, 0 erros), ainda não executado no Linux |
