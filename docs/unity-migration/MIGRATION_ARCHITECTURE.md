# Migração para Unity 6 HDRP — Arquitetura proposta (Fase 2)

Estado: **em execução**. D1, D2, D3 e D7 foram tomadas e executadas; D4 foi autorizada, mas a instalação do módulo Linux depende de uma permissão específica (seção 5). D5 e D6 seguem como recomendação. Base: `MIGRATION_AUDIT.md`.

## 1. Princípios

1. O backend (ASP.NET Core 10 + PostgreSQL), os contratos HTTP e os dados persistidos **não mudam**. A Unity é um novo cliente e um novo servidor de ilha da mesma plataforma.
2. **Fonte única de regras.** Lógica de negócio, codecs e física de terreno/veículos/NPCs ficam em `shared` e `scripting` (já livres de engine) e são consumidas pelo backend, pelos testes, pelo Godot (enquanto existir) e pela Unity.
3. O projeto Godot permanece intacto e funcional durante toda a migração; os dois clientes coexistem até a validação final.
4. Nada de conversão automática de nomes: cada classe com Godot é reimplementada com a API da Unity (ver `MIGRATION_CHECKLIST.md`).

## 2. Onde fica o projeto Unity (decisão D1)

Proposta: subpasta do monorepo, `unity/`, separada de `client/` e `island-server/`:

```text
lifeverse/
├── client/ island-server/ ...        (Godot — intocados)
├── shared/ scripting/ backend/ tests/ (reaproveitados)
└── unity/
    ├── UnityClient/                   Unity 6 HDRP, cliente Windows
    ├── UnityIslandServer/             Dedicated Server (ver D3)
    ├── Plugins~/                      DLLs compiladas de shared/scripting (ver seção 3)
    ├── MigrationTools/                conversores de materiais e assets
    └── Tests/                         testes de modo Edit/Play e de integração
```
Vantagem: um único repositório, CI e `shared` compartilhados. Alternativa: repositório separado.

## 3. Como o C# existente é reutilizado (decisão D2) — risco principal de reuso

Medição em `shared` (73 arquivos) e `scripting` (19): **166 usos de collection expressions** (`= []`), **67 construtores primários**, **13 membros `required`**, **37 usos de `System.Text.Json`**, 7 `GeneratedRegex`, 132 padrões `is {}`/`is []`, e namespaces com escopo de arquivo em todos. O compilador que a Unity usa por padrão aceita C# 9; esses recursos são C# 10 a 12.

**Verificado em 2026-11-18** (Unity 6000.6.9f1, projeto temporário criado fora do repositório, em pasta temporária da sessão): o editor compila C# **9.0** por padrão e rejeita file-scoped namespace (CS8773), construtores primários (CS8773) e `required` (CS8773, CS0518, CS0656). Com `Assets/csc.rsp` contendo `-langversion:12` **e** um arquivo de polyfill com `IsExternalInit`, `RequiredMemberAttribute` e `CompilerFeatureRequiredAttribute`, o mesmo código (construtor primário, `required`, `init`, collection expression, file-scoped namespace) **compilou sem erros** (`Tundra build success`, `Exiting batchmode successfully`). Portanto há **dois caminhos viáveis**: (a) DLL pré-compilada; (b) compilar o código-fonte dentro da Unity com `-langversion:12` e polyfills. Ainda **não** testado: IL2CPP, `System.Text.Json` e as bibliotecas MoonSharp/Concentus dentro da Unity.

**Resultado da Fase 3 (2026-11-18): o caminho (a) foi implementado e passou no spike S1 no editor.** `unity/UnityPlugins/build-plugins.ps1` compila uma cópia dos fontes para `netstandard2.1` e gera `LifeVerse.Shared.dll` (544 KB) e `LifeVerse.Scripting.dll` (354 KB), sem alterar os projetos originais. O netstandard2.1 não tem 13 APIs que o código usa; foram resolvidas com 11 substituições mecânicas no script mais uma camada `Compat.cs` (polyfills internos e uma `PriorityQueue` com o mesmo heap de 4 filhos do .NET): `IReadOnlySet<` → `ISet<`, `[GeneratedRegex]` → `new Regex`, `MathF.Tau`, `Convert.ToHexString`, `SHA256.HashData`, `Enum.IsDefined(x)`, `char.IsAsciiLetterOrDigit`, `Math.Log2`, `Read/WriteSingleLittleEndian`, `string.Create`, `Array.Clear(a)`, `StringSplitOptions.TrimEntries`, e as extensões `Stream.ReadExactly` e `Order()`. `scripting` precisou também do pacote `System.Text.Json`. Dez testes de modo Edit (codecs de snapshot e veículo, terreno binário e JSON, SHA-256, regras de grupo, Opus, A* de NPC, compilador Lua e timer do `ScriptHost`) **passam** na Unity 6000.6.9f1.

Proposta original: **compilar `shared` e `scripting` fora da Unity** (multi-target `net8.0` + `netstandard2.1`, `LangVersion 12`, com polyfills de `IsExternalInit`, `RequiredMemberAttribute` e `CompilerFeatureRequiredAttribute`) e importar os `.dll` como plugins pré-compilados. Assim o código-fonte continua único e não é reescrito. Pontos a validar em spike (S1):
- `System.Text.Json` não vem com a Unity: levar a DLL (build `netstandard2.0`) e suas dependências, e testar com IL2CPP e *stripping*.
- `GeneratedRegex` é um source generator: no `netstandard2.1` cai em `Regex` em tempo de execução ou precisa de ajuste.
- MoonSharp 2.0.0 sob IL2CPP (usa reflexão em interop CLR; o projeto usa só *callbacks*, o que reduz o risco).
- Concentus (Opus) sob IL2CPP.

Assemblies na Unity: `LifeVerse.Shared.dll`, `LifeVerse.Scripting.dll` (plugins) e, como código-fonte Unity, `LifeVerse.Unity.Core`, `.Networking`, `.World`, `.Avatar`, `.Building`, `.UI`, `.Audio`, `.Platform` (asmdefs). Regra: nenhum assembly de `shared` referencia `UnityEngine`.

## 4. Rede (decisão D3 — a mais crítica)

### 4.1 O que existe
`ENetMultiplayerPeer` + RPCs do Godot (44 métodos, 3 canais, tabela na seção 6.1 da auditoria). A parte **específica do Godot** é o formato de pacote do RPC; a parte **do LifeVerse** são os argumentos de cada RPC e os payloads binários `SnapshotCodec`, `VehicleCodec`, `VoiceCodec` (em `shared`).

### 4.2 Alternativas

| | A. Emular o `SceneMultiplayer` do Godot na Unity | B1. Novo protocolo sobre ENet-CSharp | B2. Novo protocolo sobre LiteNetLib | B3. Novo protocolo sobre Unity Transport |
|---|---|---|---|---|
| Compatível com clientes/servidores Godot atuais | Em tese sim | Não | Não | Não |
| Esforço | Alto e frágil: replicar cache de caminhos de nó, ids de RPC por hash e checagens do `SceneMultiplayer` | Médio | Médio | Médio |
| Semântica de canais (confiável, não confiável ordenado, não confiável) | Já existe | Igual ao ENet atual (mesma biblioteca de base) | Equivalente | Equivalente |
| Dependência | Código nosso, acoplado a detalhes internos do Godot | Biblioteca nativa por plataforma (Windows/Linux) | C# puro, netstandard2.1 | Pacote oficial da Unity |
| Servidor sem Unity (ver D4) | Possível | Possível | Possível | Não |

**Decisão D3 (2026-11-18): B2, LiteNetLib 2.1.5.** Novo protocolo "LifeVerse Wire" (versão 12), reaproveitando **todos** os codecs e a semântica dos 44 RPCs. O spike S2 escolheu o LiteNetLib porque o ENet-CSharp exige binário nativo por plataforma e não há compilador C++ nesta máquina. A especificação está em `WIRE_PROTOCOL.md` e o código em `unity/UnityWire`.

Recomendação original: B (novo protocolo "LifeVerse Wire", versão de protocolo 12), reaproveitando **todos** os codecs e a semântica dos 44 RPCs; escolher entre B1 e B2 por spike (S2) medindo paridade de canais, MTU, fragmentação, perda de pacote e comportamento em Linux. Emular o Godot (A) não é recomendado: replica detalhes internos que mudam entre versões da engine e entrega pouco, porque os dois lados seriam novos.

**Consequência a aceitar (D3):** cliente Unity e servidor Unity **não falam com** cliente/servidor Godot. Plano de convivência: a API passa a emitir no ticket de entrada o campo `engine`/`wireVersion`, e cada instância de ilha é registrada com o seu tipo; um cliente só entra em ilhas do mesmo tipo. Os dois mundos usam o mesmo banco e os mesmos dados.

Formato "LifeVerse Wire" (a especificar na Fase 5): `[u8 tipo][varint tamanho][payload]`, um tipo por RPC; argumentos na mesma ordem e com os mesmos tipos da tabela da auditoria; strings UTF-8 com prefixo de tamanho; `byte[]` com prefixo de tamanho. Os payloads de `Snapshot`, `VehicleSnapshots` e voz permanecem **byte a byte iguais** aos atuais. Testes: ida e volta de cada mensagem, comparação de payloads com os do Godot, mensagens malformadas.

## 5. Servidor de ilha (decisão D4)

O que o servidor precisa de uma engine hoje: **física de objetos dinâmicos** (`RigidBody3D`/`StaticBody3D` em `ServerPhysics`), raios/obstáculos e carregamento de malhas colisoras. Todo o resto (movimento dos jogadores, terreno, veículos, NPCs, scripts, voz, admissão, interesse) é código `shared`/`scripting`.

- **D4-a. Unity Dedicated Server (Linux):** paridade de física com o cliente Unity (PhysX nos dois lados). Exige o módulo *Linux Dedicated Server Build Support* do editor, que **não está instalado** nesta máquina (só Windows e WebGL). Instalar módulo exige autorização.
- **D4-b. Serviço .NET sem engine + biblioteca de física:** processo leve e fácil de operar em contêiner. Perde a paridade de física com o cliente e exige escolher e integrar uma biblioteca.
**Decisão D4 (2026-11-18): D4-a, Unity Dedicated Server.** O Hub 4.0.1 não instala módulos por linha de comando e manda usar o **Unity CLI** independente. Com a sua permissão, o módulo **Linux Dedicated Server Build Support** (160 MB de download, 554 MB instalado) foi instalado em 2026-11-18 pelo CLI 1.0.1, baixado da CDN da Unity com o SHA-256 conferido e assinatura digital válida, guardado em `.tools/unity-cli/unity.exe` (fora do git), **sem** rodar o script oficial (ele também edita o PATH e instala "skills" em clientes de agente de IA, o que não foi autorizado). **Verificado:** `BuildClient.BuildLinuxServer` gera um servidor dedicado Linux (executável ELF, `UnityPlayer.so`, 161 MB, 0 erros, 7 avisos) com `LifeVerse.Shared/Scripting/Wire` e `LiteNetLib`. **Não verificado:** a execução do servidor no Linux (não há WSL nem máquina Linux aqui). O HDRP só aceita Vulkan no Linux; o `ProjectSetup` já configura isso. Recomendação provisória: **D4-a**, validada por spike (S4) que simula N objetos dinâmicos e compara com o comportamento atual. Em qualquer caso, a lógica fica em `shared`; a engine só fornece física e colisão.

Preservar: heartbeat e registro na API, `server_started`/logs JSON estruturados, health check em porta própria (`LIFEVERSE_HEALTH_PORT`), limites de recursos e o supervisor (`island-supervisor`, sem Godot, já reaproveitável).

## 6. Cliente

| Área | Decisão proposta | Observação |
|---|---|---|
| Pipeline | Unity 6 + HDRP, Windows x64 | Editor local: 6000.6.9f1 (instalada). Fixar a versão no `ProjectVersion.txt`. |
| Entrada | Input System | Mapear as 19 ações da auditoria 4.5 com os mesmos atalhos. |
| Movimento | O cálculo continua em `shared` (`AuthoritativeMovement`); o jogador Unity é um seguidor cinemático com colisão consultada por `Physics`. Não usar `CharacterController` para decidir a posição. | `CharacterBody3D` aparece 1 vez no cliente atual. |
| Terreno | Malha própria alimentada por `TerrainMap` (mapa de alturas + camadas) e um shader HDRP equivalente ao atual (D5) | Evita converter dados persistidos para `TerrainData`. Unity Terrain só se o spike S5 mostrar ganho. |
| Água/oceano | Avaliar HDRP Water System contra um shader próprio que preserve ondas, espuma e a vista de baixo da superfície | Nível do mar e profundidade de vadeio vêm de `shared`; não mudam. |
| Materiais | Conversor `MaterialBuilder` → HDRP/Lit por submalha, com textura/emissivo/mídia por face | Comparação visual obrigatória (ver `MIGRATION_TEST_PLAN.md`). |
| Avatar | Corpo procedural e corpo GLB personalizado; esqueleto padrão de `HumanoidTemplate`; animação por Playables/Animator; retargeting equivalente ao `RetargetCustom` | **GLB em tempo de execução** exige uma biblioteca (glTFast é a candidata): é um pacote novo e precisa de autorização. |
| UI | **UI Toolkit** (USS para o tema "vidro escuro", C# para construir as janelas), com uGUI só em elementos em espaço de mundo (nameplates, indicador de voz) | A UI atual é toda em código (1.812 `Control`); portar para `VisualElement` por código é um mapeamento direto, sem arquivos de cena. Decisão D6. |
| Áudio | `AudioSource` 3D; decodificação de MP3/OGG/WAV em tempo de execução | MP3 em runtime não é nativo em todos os alvos: spike S6. |
| Voz | Captura com `Microphone`, Opus via Concentus (já em `shared`) | O codec é reaproveitado. |
| HTTP | `UnityWebRequest` ou `HttpClient` | O `MediaDownload` atual (redirecionamento seguro) deve ser reimplementado. |
| Persistência local | `Application.persistentDataPath` no lugar de `user://` | Mesmos arquivos e chaves. |

## 7. Scripting Lua
`scripting` (MoonSharp) é reaproveitado como DLL (D2). A camada que liga o runtime ao mundo é a interface `IScriptWorld`, que já abstrai o servidor; a implementação Unity dela substitui `IslandScripting` (14 arquivos parciais com Godot no servidor). Os limites (instruções, tempo, estado) e a API não mudam.

## 8. Fases e portões de saída

| Fase | Entrega | Portão para a seguinte |
|---|---|---|
| 1 Auditoria | `MIGRATION_AUDIT.md` | Revisado |
| 2 Arquitetura | este documento + decisões D1–D7 | Decisões irreversíveis autorizadas |
| 3 Fundação Unity | Projeto criado, HDRP configurado, asmdefs, build de plugins `shared`/`scripting`, spikes S1 | S1 passa (compila e roda `shared`) |
| 4 Código compartilhado | Testes de `shared` e `scripting` executados também por dentro da Unity (Test Runner) | Mesmos resultados do xUnit |
| 5 Rede | LifeVerse Wire, cliente e servidor de teste, spike S2 | Testes de ida e volta e 2 clientes sincronizando |
| 6 Servidor | Ilha dedicada, supervisor, saúde, logs | Ilha sobe e registra na API |
| 7 a 12 | Mundo, jogador, construção, UI, social/economia, scripting | Critérios do `MIGRATION_TEST_PLAN.md` por módulo |
| 13 a 15 | Otimização, validação, distribuição | Regressão completa |

## 9. Decisões pendentes

| Id | Decisão | Recomendação | Reversível? | Autorização necessária |
|---|---|---|---|---|
| D1 | Pasta do projeto Unity | `unity/` no monorepo | Sim | Não |
| D2 | Reuso de `shared`/`scripting` como DLL | Sim, com spike S1 | Sim | Não |
| D3 | Transporte: novo protocolo sobre ENet-CSharp ou LiteNetLib, sem compatibilidade com Godot | **Tomada: LiteNetLib (B2)**, protocolo 12, spike S2 | **Difícil**: define o protocolo 12 e a política de convivência | Autorizada em 2026-11-18 |
| D4 | Servidor: Unity Dedicated Server ou serviço .NET | Unity DS (módulo Linux) | Médio | Autorizada em 2026-11-18; módulo Linux instalado e build do servidor verificado (seção 5) |
| D5 | Terreno e água: malha própria + shader, ou Unity Terrain/Water | Malha própria; spike para a água | Sim | Não |
| D6 | UI Toolkit ou uGUI | UI Toolkit | Médio | Não |
| D7 | Pacotes Unity: HDRP, Input System, UI Toolkit (embutido), glTFast, Test Framework | Adicionar na Fase 3 | Sim | **Sim** (pela regra "não instalar pacotes sem autorização") |

## 10. Equivalências Godot → Unity (conceituais, com a frequência real no cliente)

| Godot (ocorrências) | Unity |
|---|---|
| `Control`/`Label`/`Button` (≈6.300) e `Theme`/`StyleBoxFlat` (≈320) | `VisualElement`/`Label`/`Button` e USS |
| `Node3D`/`MeshInstance3D` (≈350) | `GameObject`/`MeshFilter`+`MeshRenderer` |
| `Skeleton3D`/`AnimationPlayer`/`AnimationTree` (≈90) | `SkinnedMeshRenderer`/`Animator`/Playables |
| `HttpRequest` (154) | `UnityWebRequest` (corrotinas ou `Awaitable`) |
| `FileAccess`/`ConfigFile` (≈140) | `System.IO` + `persistentDataPath` |
| `Tween` (90) | corrotina/`Awaitable` ou biblioteca de tween |
| `ArrayMesh`/`SurfaceTool` (26) | `Mesh` + `MeshData` |
| `ShaderMaterial`/`StandardMaterial3D` (56) e 5 shaders embutidos | HDRP/Lit e Shader Graph/HLSL |
| `AudioStreamPlayer` (34) | `AudioSource` |
| `GltfDocument` (9) | glTFast (D7) |
| `RayCast`/`IntersectRay` (24) | `Physics.Raycast` |
| `[Rpc]` (44) | mensagens LifeVerse Wire |
| `[Signal]` (22) e `[Export]` (15) | eventos C# e campos serializados |
