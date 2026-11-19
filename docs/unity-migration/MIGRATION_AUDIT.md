# Migração para Unity 6 HDRP — Auditoria do projeto Godot (Fase 1)

Data da auditoria: 2026-11-18. Base: commit `aec459a` (branch `main`). Projeto original **não foi modificado**.

## 1. Método e limites

- Os números abaixo foram **medidos no repositório** com scripts de contagem (PowerShell/ripgrep). Excluem `bin/`, `obj/`, `.godot/`, `.tools/` e as migrações do EF.
- "Usa Godot" significa que o arquivo contém `using Godot;` ou o token `Godot.`. É uma classificação **por arquivo**, não por classe. A classificação por classe é a Fase 2b (ver `MIGRATION_CHECKLIST.md`).
- Esta auditoria é estática. Nada foi executado na Unity. Nenhum comportamento foi comparado ainda.
- Os 79 arquivos de `docs/` descrevem o produto; onde o código contradiz a documentação, vale o código.

## 2. Composição do repositório

| Componente | Projeto | Arquivos `.cs` | Linhas | Usam Godot | Livres de Godot |
|---|---|---:|---:|---:|---:|
| Cliente desktop | `client` (Godot.NET.Sdk 4.7.2, net8.0) | 412 | 66.507 | 385 | 27 |
| Servidor de ilha | `island-server` (Godot headless, net8.0) | 29 | 3.050 | 14 | 15 |
| Supervisor de ilhas | `island-supervisor` (net8.0) | 4 | 211 | 0 | 4 |
| Contratos e regras compartilhadas | `shared` (net8.0, pacote Concentus) | 73 | 7.261 | 2 | 71 |
| Runtime de scripts Lua | `scripting` (net8.0, pacote MoonSharp 2.0.0) | 19 | 3.055 | 0 | 19 |
| API | `backend` (ASP.NET Core, net10.0, EF Core/Npgsql) | 167 | 21.034 | 0 | 167 |
| Testes | `tests/LifeVerse.Api.Tests` (xUnit, net10.0) | 189 | 28.577 | 0 | 189 |

Outros: 92 migrações EF (PostgreSQL), 79 documentos em `docs/`, `launcher/LifeVerse.Launcher.ps1`, `installer/LifeVerse.iss` (Inno Setup), `scripts/` (build, manifesto, smoke, VPS), `tools/blender/` (5 scripts Python de geração de animações e roupas), `infrastructure/compose.{dev,prod}.yml`, workflow `.github/workflows/release.yml`.

### 2.1 O que isso significa
- **93% dos arquivos do cliente (385 de 412) dependem do Godot.** Só 27 são reaproveitáveis como estão, e a maioria são modelos de dados e clientes HTTP.
- **Backend, scripting, supervisor, `shared` (71/73) e a suíte de testes não dependem de engine.** Esse é o grosso do valor reaproveitável: regras de negócio, codecs de rede, física de terreno/veículos/NPCs, runtime Lua.
- O servidor de ilha mistura os dois: 15 arquivos são lógica pura, 14 usam Godot (`ServerMain`, `ServerPhysics`, `NetworkBridge`…).

## 3. Divergências entre o prompt de migração e o repositório real

| Premissa do prompt | Realidade medida |
|---|---|
| "Protocolo de rede v7" | `NetworkProtocol.Version = 11` (`shared/Networking/NetworkProtocol.cs`; `IslandEntry:ProtocolVersion = 11` na API). |
| "O sistema usa ENet; preservar o protocolo" | O transporte é o `ENetMultiplayerPeer` **da camada multiplayer de alto nível do Godot** (`SceneMultiplayer`): 44 métodos `[Rpc]` em 3 canais. O formato de pacote desse RPC (cache de caminho de nó, ids de método) é do Godot. Os payloads dos snapshots são binários próprios (`SnapshotCodec`, `VehicleCodec`, `VoiceCodec`) e são reaproveitáveis. Ver seção 6 e `MIGRATION_ARCHITECTURE.md`. |
| "Migrar cenas `.tscn`, recursos `.tres`, shaders" | Há **7 cenas de produção** (6 no cliente, 1 no servidor), **0 `.tres`** e **0 arquivos de shader**. Os 5 shaders estão **embutidos em strings C#**. Há 117 cenas de teste. |
| "Reconstruir a interface" (implica cenas de UI) | A UI é **construída em código**: `Control` aparece 1.812 vezes, `Button` 2.830, `Label` 1.679, `Theme` 285, `StyleBoxFlat` 34. Não há arquivos de cena de UI para converter. |
| "Scripts Lua" | O runtime é **MoonSharp** (Lua em C#) dentro do projeto `scripting`, usado em processo pelo servidor de ilha. É independente de engine. |
| "Servidor com física do mundo" | O servidor de ilha usa `RigidBody3D`/`StaticBody3D` do Godot em `ServerPhysics.cs` (186 linhas) para objetos dinâmicos. Terreno, veículos, NPCs e movimento de jogadores usam código em `shared` (`TerrainMap`, `VehiclePhysics`, `NpcWorld`, `AuthoritativeMovement`). |

## 4. Cliente — inventário

### 4.1 Autoloads (singletons) — `client/Autoloads`
`AppManager` (versão, URL da API; usa `OS.HasFeature("editor")` para escolher dev ou produção), `SessionManager`, `NetworkManager` (407 linhas, snapshots, avatares remotos, interpolação), `NetworkBridge` (44 RPCs), `AuthManager`, `UIManager`, `AvatarManager`, `ClientSettings`.

### 4.2 Cenas de produção
`Bootstrap`, `Login`, `World`, `Player`, `AvatarRig`, `CharacterCreator` (cliente) e `Server` (servidor). Tudo o mais é montado por código.

### 4.3 Subsistemas do cliente (`client/Scripts`, por arquivos)
- **Avatar:** `AvatarRig` (+ 11 parciais: animações, attachments, custom body, detalhe/LOD, dedos, primeira pessoa, passos, olhar, digitação, roupas), `CharacterCreator` (+ 5), `BuiltInBodies/Textures/Wearables`, `DefaultAvatarAnimations`, `NamePlate`, `VoiceIndicator`, subpasta `Attachments` (7).
- **Jogador e câmera:** `PlayerController`, `BuildCamera`, subpasta `Camera` (5), `ContextInteraction`, `InteractableSeat`, `RadialMenu`.
- **Mundo:** `IslandTerrain` (shader embutido), `OceanSurface` (shader embutido), `IslandEnvironmentController` (céu/clima, shader embutido), `WaterVolume`, `IslandLookService`, `LandManager` (+ 5) e `LandPresence`, `PhysicsSync`, `VehicleClient`/`VehicleView`, `AmbientAudio`.
- **Construção:** `BuildObjectView` (+ 4), `MaterialBuilder` (shader embutido), `ModelCache` (+ 1), `ModelLod`, `ModelInspector`, `ModelImportDialog`, `GltfUploadPackager`, `ColladaConverter`, `DaeUploadPackager`, `TerrainEditor`, subpasta `Build` (37 arquivos: gizmos, transformação, seleção, permissões, persistência, pacotes, materiais, mídia).
- **Social e economia:** `SocialPanel`, `PeoplePanel`, `GroupsWindow` (+ 5), subpasta `Groups` (9), `ProfileWindow` (+ 5), `WalletWindow`, `MarketplacePanel`, `ItemOffersWindow`, `ItemRightsWindow`, `RealEstate` (6), `LandManagement` (6), `IslandManagement` (19), `ParcelInfo` (2).
- **Inventário e assets:** `InventoryPanel` (+ 1), `AssetLoadingService`, `AssetPreview`, `AssetVersionsWindow`, `TextureCache`, `ThumbnailLoader`, `LostObjectsWindow`.
- **Busca, mapa, notificações:** `SearchWindow` (+ 8), `DiscoveryPanel`, `Map` (10), `MiniMap`, `LandMapWindow`, `Notifications` (4), `NotificationClient`.
- **Scripts de objetos:** `LuaScriptEditor`, `ScriptEditorWindow` (+ depuração), `ScriptDocsWindow`, `ScriptHistoryWindow`, `ScriptDialogWindow`, `ScriptHudLayer`, `HudEditor`, `ScriptApi` (+ 2).
- **Áudio, voz e mídia:** `VoiceChat`, `VoicePlayback`, `VoiceBar`, `ObjectEffects` (+ mídia, partículas), `MediaDownload`, `LandPresence` (música de terreno e ilha).
- **Infraestrutura de UI:** `ViewerUI` (+ 3), `ViewerTheme`, `UiStyle`, `UiTabs`, `UiDraw`, `UiIconData`, `FloatingWindow`, `Accessibility`, `LoadingScreen`, `LoginScreen`, subpasta `Login` (13), `SettingsPanel`, subpasta `Settings` (16), subpasta `Interface` (4), `GraphicsQuality`, `PerformanceOverlay`.
- **Fluxo de entrada:** `Bootstrap`, `StartPlace`, `WorldEntryCoordinator`, `TeleportCoordinator`, `SavedLogin`, `UpdateChecker`.

### 4.4 Uso de APIs do Godot no cliente (contagem de ocorrências)
`Viewport` 425, `HttpRequest` 154, `FileAccess` 100, `Tween` 90, `MeshInstance3D` 86, `Skeleton3D` 56, `ShaderMaterial/StandardMaterial3D` 56, `ConfigFile` 40, `AudioStreamPlayer` 34, `AnimationPlayer/AnimationTree` 34, `ArrayMesh/SurfaceTool` 26, `RayCast/IntersectRay` 24, `[Signal]` 22, `[Export]` 15, `GltfDocument` 9, `CanvasLayer` 13, `CharacterBody3D` 1 (o movimento do jogador é calculado em `shared`, não pela física da engine).

### 4.5 Entrada (`project.godot`)
19 ações: `move_forward/back/left/right` (W/S/A/D + setas), `strafe_left/right` (Q/Z), `jump` (Espaço), `run` (Shift), `fly_toggle` (F), `descend` (Ctrl), `first_person` (C), `camera_mode` (V), `camera_reset` (R), `cursor_toggle` (Alt), `interact` (E), `emote_wave` (G), `emote_dance` (H), `terrain_mode` (T), `build_mode` (B), `ui_cancel` (Esc).

### 4.6 Renderização atual
Forward+ com driver D3D12 no Windows, viewport 1920×1080 com esticamento `canvas_items`/`expand`, gravidade 9,8, áudio com entrada de microfone habilitada. Shaders **embutidos em C#**: `IslandTerrain` (terreno por camadas), `OceanSurface` (oceano com ondas, espuma, normal maps, fade de distância, tinta submersa), `IslandEnvironmentController` (céu/hora/clima), `MaterialBuilder` (materiais de construção, albedo/emissivo/mídia por face), `VoiceIndicator`.

### 4.7 Persistência local do cliente
`user://settings.cfg`, `user://land_media.cfg`, `user://media.cfg`, `user://media/` (cache de imagens/vídeo), `user://avatar_models/` (cache de GLB por hash), login salvo (`SavedLogin`).

## 5. Servidor de ilha — inventário (`island-server/Scripts`)
`ServerMain` (690 linhas: admissão, tick, snapshots, interesse, desconexão e reconexão), `ServerPhysics` (186), `ServerVehicles`, `ServerTeleport`, `ServerLocations`, `ServerAppearance`, `ServerAttachments`, `ServerChannel`, `ServerGesture`, `ServerHeadLook`, `ServerVoice`, `ServerConfiguration`, `NetworkBridge` (espelho do cliente), e `IslandScripting` (+ 12 parciais: HUD, mídia, NPC, terreno/parcelas, região, vehicles, efeitos, compras, gatilhos, movimento, depuração) que hospeda o `ScriptHost`.

Supervisor (`island-supervisor`, 4 arquivos, sem Godot): processo auxiliar que cuida das instâncias de ilha (ver `docs/ISLAND_HOSTING.md`; o código não foi lido em detalhe nesta fase). Extras de operação: `scripts/vps/island-watchdog.sh` e instalador.

## 6. Rede — inventário

- **Transporte:** `ENetMultiplayerPeer`; cliente `CreateClient(host, porta, 3 canais)`; servidor `CreateServer(porta, máx. jogadores, 3 canais)`. API de alto nível: `Multiplayer.MultiplayerPeer`, `[Rpc]`, `RpcId`.
- **Admissão:** a API emite um ticket de entrada assinado (`IslandEntry`, com `protocolVersion`, `tokenId`, aparência, `canEditTerrain`…; chave `ISLAND_ENTRY_SIGNING_KEY`); o cliente envia `Authenticate(token, protocolVersion)`; o servidor confere a versão, o ticket e o uso único (`_usedTickets`). O formato exato do token não foi lido em detalhe e deve ser documentado na Fase 2b.
- **Tick/snapshots:** tick autoritativo; `SnapshotCodec` (posições quantizadas, estado de movimento, última entrada processada); interesse por raio (`LIFEVERSE_INTEREST_RADIUS`); interpolação no cliente com `Lerp`; reconciliação do jogador local (`Reconcile`).
- **Movimento:** `AuthoritativeMovement` em `shared` (usado pelo servidor e pela predição do cliente).
- **Reconexão:** suspensão do avatar por dez segundos e reconexão com até 3 tentativas (`HandleConnectionLost`).
- **Voz:** pacotes Opus (Concentus) relayados pelo servidor (`VoiceRelay`, `ReceiveVoice`).

### 6.1 Os 44 RPCs
Canal 0 = confiável. Canal 1 = entradas do cliente (não confiável ordenado, exceto voz). Canal 2 = estado do servidor (não confiável ordenado, exceto voz).

**Cliente → servidor (22)**

| RPC | Modo | Canal | Argumentos |
|---|---|---:|---|
| Authenticate | Reliable | 0 | string token, int protocolVersion |
| SendChat | Reliable | 0 | string message |
| SetFlight | Reliable | 0 | bool enabled |
| SubmitBuildOperation | Reliable | 0 | string operationJson |
| SubmitTerrainEdit | Reliable | 0 | int tool, float x, float z, float radius, float strength, int layer |
| SubmitTerrainImport | Reliable | 0 | byte[] heights |
| SubmitSeaLevel | Reliable | 0 | float level |
| UndoTerrain / RedoTerrain / RequestTerrain | Reliable | 0 | — |
| RefreshAppearance | Reliable | 0 | — |
| AttachmentLive | Reliable | 0 | string id, float[] values |
| RequestTeleport | Reliable | 0 | string parcelId, float x, float z |
| TouchObject | Reliable | 0 | string objectId |
| ScriptDialogResponse | Reliable | 0 | string json |
| EnterVehicle / ExitVehicle | Reliable | 0 | int vehicleId / — |
| Gesture | Reliable | 0 | int gestureId |
| SubmitInput | UnreliableOrdered | 1 | long sequence, float x, float z, int flags |
| SubmitVehicleInput | UnreliableOrdered | 1 | float throttle, float steer, bool brake |
| HeadLook | UnreliableOrdered | 1 | float pitch |
| SubmitVoice | Unreliable | 1 | long sequence, byte[] data |

**Servidor → cliente (22)**

| RPC | Modo | Canal | Argumentos |
|---|---|---:|---|
| AuthResult | Reliable | 0 | bool success, string code, long serverTick |
| ProtocolRejected | Reliable | 0 | int serverVersion |
| SpawnPlayer | Reliable | 0 | int peerId, string avatarId, string displayName, string appearanceJson, float x, y, z, int state |
| DespawnPlayer | Reliable | 0 | int peerId, bool suspended |
| AppearanceChanged | Reliable | 0 | int peerId, string displayName, string appearanceJson |
| AttachmentLiveChanged | Reliable | 0 | int peerId, string id, float[] values |
| ReceiveChat | Reliable | 0 | int peerId, string displayName, string message, long unixMilliseconds |
| BuildOperationApplied / BuildOperationRejected | Reliable | 0 | string operationJson / string code |
| TerrainFull / TerrainPatched / TerrainEdited / TerrainEditRejected | Reliable | 0 | dados de terreno, revisão, código |
| SeaLevelChanged | Reliable | 0 | float level, long revision |
| TeleportResult | Reliable | 0 | bool ok, string code, float x, z, yaw |
| VehicleSpawned / VehicleRejected | Reliable | 0 | dados do veículo / código |
| GestureChanged | Reliable | 0 | int peerId, int gestureId |
| Snapshot | UnreliableOrdered | 2 | byte[] payload (`SnapshotCodec`) |
| VehicleSnapshots | UnreliableOrdered | 2 | byte[] payload (`VehicleCodec`) |
| HeadLookChanged | UnreliableOrdered | 2 | int peerId, float pitch |
| ReceiveVoice | Unreliable | 2 | int speakerPeer, long sequence, byte[] data |

## 7. Código compartilhado (`shared`) — inventário
`Audio` (síntese ambiente, leitor WAV), `Avatar` (clipes de animação, pontos de fixação, planejador de detalhe, validadores de modelo e roupa, template humanoide, leitor glTF), `Building` (17: matemática de alinhamento/gizmo/snap, pacotes, presets, catálogo de primitivas, malha de primitivas, travas de edição, linksets, limites de modelo, regras de física), `Camera`, `Gameplay` (estado de movimento, menu radial, raio), `Groups`, `Interaction`, `Items`, `Land` (acesso, matemática, regras, teleporte, direitos de terreno, ambiente de terreno, URL de mídia), `Loading`, `Networking` (10: movimento autoritativo, protocolo, codecs, NPCs, obstáculos, spawn, gestos, olhar), `Search`, `Settings`, `Terrain` (mapa, pincel, patches), `Updates`, `Vehicles` (frota, física), `Voice` (codec, relay).

## 8. Backend (`backend/Modules`)
`Worlds` (36 arquivos), `Land` (22), `Users` (13), `Assets` (11), `Economy` (11), `Inventory` (11), `Scripting` (10), `Platform` (8), `Discovery` (7), `Groups` (7), `Map` (6), `Identity` (5), `Social` (5). PostgreSQL com 92 migrações; a API aplica as migrações na subida. Painel `/admin` próprio. Integração do cliente com a API é por HTTP/JSON (`HttpRequest`, 154 usos) com JWT e refresh rotativo.

## 9. Scripting Lua (`scripting`)
`ScriptHost` (+ API, Runtime, Efeitos, HUD, Terrenos, Mídia, NPC, Animação de objeto, Veículos, Depuração), `LuaSandbox` (limites de instruções e tempo, `CoreModules.None`), `ScriptCompiler` (diagnósticos de editor), `ScriptCatalog` (catálogo da API), `LuaDebugger`, `ScriptWarmUp`. Eventos tratados por fila e relógio do servidor. Escopo de módulos bloqueados: `os`, `io`, `require`, `load*`, `coroutine`, metatabelas.

## 10. Testes
- **1.842** testes xUnit (`[Fact]`/`[Theory]`) em 189 arquivos, independentes de engine: cobrem backend, `shared`, `scripting`, protocolo, economia e regras de terreno. Estado atual: 2.558 casos passam e 1 falha antes desta migração (`HighJumpTests`, hemisfério, falha também no commit anterior).
- **117** cenas de smoke do cliente em Godot headless (`client/Tests/*.tscn`), executadas por `scripts/godot-smoke.sh` e `run-all-smokes.sh`.
- 3 testes no servidor de ilha (`island-server/Tests`).

## 11. Distribuição e ferramentas
Build do cliente: exportação Godot "Windows Desktop" (e "Linux") com templates mono, depois Inno Setup (`installer/LifeVerse.iss`). `launcher/LifeVerse.Launcher.ps1` baixa por HTTPS com hash e troca atômica. `scripts/generate-manifest.ps1` gera manifesto SHA-256. CI: `.github/workflows/release.yml`. `UpdateChecker` consulta a API. Ferramentas de conteúdo em Blender/Python em `tools/blender`.

## 12. Dependências de terceiros
MoonSharp 2.0.0 (Lua), Concentus 2.2.2 (Opus), EF Core 10.0.9 + Npgsql 10.0.0, JwtBearer 10.0.9, OpenApi, xUnit 2.9.3, Godot.NET.Sdk 4.7.2. A API de produção roda em contêiner Alpine, sem ICU: formatação com `CultureInfo("pt-BR")` falha no servidor (já corrigido uma vez).

## 13. Comportamentos implícitos e regras não óbvias encontrados
1. O endereço da API depende de `OS.HasFeature("editor")`: no editor usa `http://localhost:8080`; em build exportado usa produção. `LIFEVERSE_API_URL` sobrescreve.
2. Parte da animação dos avatares remotos é **deduzida no cliente** pelo movimento (nadar parado, voar parado, andar de costas), não vem da rede (`NetworkManager.RemoteMotion`, `shared/Gameplay/MovementStateResolver`).
3. Gestos além de acenar e dançar vão por RPC próprio (`Gesture`); acenar/dançar vão como flags do input (o byte de flags está cheio).
4. O chat de script tem limite de 5 mensagens por 10 s por script; um script é suspenso após 3 violações de instrução/tempo em 60 s (e seus timers são apagados).
5. Timers de script não persistem entre reinícios da ilha; são recriados pelo código de topo ou por `on_start`.
6. Mídia de terreno, ilha e scripts usa regras de endereço idênticas em três lugares (`shared/Land/LandMediaUrl`, `backend/Modules/Land/LandPresentation`, `scripting/MediaUrl`).
7. Constantes físicas compartilhadas definem natação e vadeio (`WadeDepth`, `SwimFloatDepth`); cliente e servidor precisam concordar.
8. A versão do protocolo é verificada em três pontos: ticket da API, `Authenticate` e resposta `ProtocolRejected`; um teste confere `appsettings.json` contra `NetworkProtocol.Version`.
9. O servidor de ilha mantém um relógio próprio (`Now`) usado por scripts, NPCs e veículos.
10. Texturas, vídeo e GLB baixados são guardados em cache local por hash e validados por magia de bytes antes de decodificar.
11. O avatar procedural e o corpo GLB personalizado usam caminhos de retargeting diferentes (`RetargetCustom`).
12. Formatos de import suportados hoje: GLB/glTF, DAE (convertido para GLB no cliente), PNG/JPG/WebP, WAV/OGG/MP3.
