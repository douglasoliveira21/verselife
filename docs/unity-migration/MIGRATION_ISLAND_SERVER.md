# Servidor de ilha Unity (Fase 6)

Estado em 2026-11-18. Este documento descreve o que foi construído, o que foi **testado**, o que ainda **não existe** e como operar. O servidor Godot (`island-server/`) não foi alterado e continua sendo a fonte da verdade do comportamento.

## Arquitetura

```
unity/UnityIslandServer/
  Core/   LifeVerse.IslandServer.Core   net8.0 + netstandard2.1, sem engine
  Host/   LifeVerse.IslandServer.Host   processo .NET (console), sem Unity
  LoadTest/ LifeVerse.IslandServer.LoadTest   N clientes simulados por UDP contra um servidor em execução
unity/UnityClient/Assets/LifeVerse/Server/
          IslandServerBehaviour         componente do Unity Dedicated Server (só dá o relógio e o código de saída)
tests/LifeVerse.IslandServer.Tests      51 testes xUnit (UDP real + HTTP real)
```

Toda a regra está no `Core`. Os dois hospedeiros só dão o relógio e a saída:

| Hospedeiro | Quando usar | Dependências |
|---|---|---|
| `Host` (.NET) | Supervisor/Alpine, CI, desenvolvimento | só o runtime .NET; sem GPU, sem licença Unity, sem Vulkan |
| Unity Dedicated Server | quando o servidor precisar de física, navmesh ou assets da Unity (S4, objetos físicos, NPCs com cena) | Unity 6, módulo Linux, **Vulkan exigido pelo HDRP no build** |

Isto é uma decisão a revisar: hoje nada no servidor usa a Unity, e o `Host` é mais leve (dezenas de MB contra 161 MB) e não depende de licença. A Unity só passa a compensar quando a física dos objetos (spike S4) for portada para o PhysX da Unity. O `Core` foi escrito para que essa troca não mude nada no resto.

### Peças do `Core`

| Arquivo | Responsabilidade |
|---|---|
| `IslandServerConfig` | mesmas variáveis de ambiente do Godot, mais `LIFEVERSE_TICKET_PROTOCOL`, `LIFEVERSE_TICK_RATE` (60), `LIFEVERSE_LOOPBACK_ONLY` |
| `IslandApi` (`IIslandApi`, `HttpIslandApi`) | registro, validação do bilhete, snapshot do mundo (GET/PUT), heartbeat + comandos do admin, posições, confirmação de chegada, saída; cabeçalho `X-LifeVerse-Service-Key` |
| `IslandServer` | admissão, simulação autoritativa, snapshots por interesse, chat, voo, gestos, olhar, persistência, comandos, saúde, métricas, encerramento |
| `IslandLog` | uma linha JSON por evento (`{timestamp, event, properties}`), o formato que o Easypanel/Supervisor já leem |

Concorrência: tudo que mexe no estado roda em `Update()`, na thread do hospedeiro. As tarefas de rede (HTTP) só deixam recados numa fila (`_inbox`) que o `Update` esvazia. Um passo que lança exceção é registrado (`tick_failed`) e contado; o servidor segue.

## Paridade com o servidor Godot

| Comportamento | Godot | Unity `Core` | Teste |
|---|---|---|---|
| Ordem de subida: saúde → registro → mundo → porta → heartbeat | sim | sim | `StartRegistersLoadsTheWorldAndAnswersTheHealthCheck` |
| Admissão via `entry-tokens/validate`, bilhete de uso único, versão do bilhete | sim | sim | `AValidTicketAdmits…`, `UnknownTicketsAndReusedTicketsAreRefused`, `ATicketOfAnotherProtocol…` |
| Recusa por versão de protocolo (`ProtocolRejected`) | sim | sim (Wire 12) | `ADifferentWireVersionIsRejected…` |
| Avatar suspenso por 10 s e retomada no mesmo ponto | sim | sim | `LeavingDespawnsForOthers…` |
| Spawn: ponto de retomada ou ao redor do ponto de entrada (`SpawnPlanner`) | sim | sim | idem |
| Movimento autoritativo (`AuthoritativeMovement.Step`), entrada limitada, `LastProcessedInput` | sim | sim | `MovementIsAuthoritative…`, `ACheatingClient…` |
| Snapshots a 20 Hz com raio de interesse | sim | sim, **em pacotes de até 40 jogadores** | `PlayersFarApart…`, `ManyPlayersPlay…` |
| Chat: 5 por 10 s, raio de 30 m, termos bloqueados | sim | sim | `ChatReachesOnlyPlayersWithin30m…` |
| Voo idempotente, gestos extras e olhar com limite de taxa | sim | sim | `FlightIsAnIdempotentState…` |
| Terreno: pincel (limites e valores aplicados iguais para todos), patches, nível do mar, desfazer/refazer por traço, importação de mapa de alturas, limite de 20 edições/s, salvamento ao encerrar | sim | sim (só editores da ilha) | `OnlyIslandEditorsSculpt…`, `TerrainEditsAreRateLimited…` |
| Veículos: frota da API (sincronizada a cada 15 s), entrar/sair/dirigir, motorista preso ao assento, liberação ao desconectar, snapshots em pacotes de 32 | sim | sim (sem scripts de veículo) | `APlayerEntersDrives…`, `ADriverWhoDisconnects…`, `ManyVehiclesGoOut…` |
| Construção: validação do pedido, 20 operações/s, locks de edição (concessão de 20 s, anúncio a quem entra, liberação ao sair), poses ao vivo, repasse à API como o jogador, códigos de recusa, devolução ao inventário | sim | sim (sem os ganchos de script `OnObjectApplied`) | `ABuildOperationIsForwarded…`, `InvalidBuildOperations…`, `APIRefusals…`, `BuildOperationsAreRateLimited…`, `EditLocks…` |
| Terras (`land-sync` a cada 10 s): parcelas fechadas e banimentos (jogador é movido para o ponto permitido mais próximo), teletransporte (livre, forçado, desativado, com cooldown de 1 s), terreno por donos de parcela (máscara), eventos de terra repassados a todos | sim | sim (sem `LandInfo` e `LandRules` de voz/script) | `APlayerInsideLandThatCloses…`, `TeleportsFollowTheParcelRules`, `ParcelOwnersSculpt…` |
| Voz proximal: pacotes Opus repassados a quem está a 25 m, limite de tamanho (400 B) e de taxa (60/s), parcelas sem voz | sim | sim | `VoiceReachesOnlyListenersInRange…` |
| Aparência: o cliente só pede "atualizar"; o servidor consulta `avatars/{id}/appearance` e avisa todos (pausa de 3 s que adia em vez de descartar); prévia de anexos só para o que o jogador veste, com limites | sim | sim | `AppearanceIsAskedFromTheAPI…`, `LiveAttachmentPreviews…` |
| Scripts de objetos (Lua/MoonSharp): carga de objetos e scripts (`island-content`), toque, duplo clique, chat, entrar/sair de objetos, timers, mudanças de objeto pela via de construção, NPCs, gestos, veículos, região, estado persistido ao encerrar | sim | sim, **compilando o mesmo `IslandScripting` de `island-server/`** (fonte única, sem cópia) | `ObjectScriptsAnswerTouches…`, `AnIslandWhoseScriptContentCannotBeLoaded…` (só toque e chat com Lua real) |
| Física de objetos (corpos, fixos, terreno, empurrão, poses) | sim (Godot Physics) | sim, só no Unity Dedicated Server (PhysX) | `PhysicsGetsTheObjects…` (fake) + autoverificação de 15 itens no PhysX |
| Posições salvas a cada 20 s e ao sair; liberação de viajantes; confirmação de chegada | sim | sim | `ATravellerWhoseTrip…`, `ATravellerArriving…` |
| Heartbeat a cada 15 s, comandos `kick`, `notice`, `shutdown` | sim | sim | `AKickFromTheAdminPanel…`, `AShutdownCommand…` |
| Health check TCP/HTTP: 200 `healthy` / 503 `starting` | sim | sim, com métricas (jogadores, tick médio e p99, uptime) | `StartRegisters…` |
| Encerramento: salva mundo e jogadores, desconecta, se retira da API | sim | sim | `AShutdownCommand…` |
| Ilha vazia por `LIFEVERSE_IDLE_SHUTDOWN_MINUTES` | sim | **configuração e lógica prontas, sem teste de tempo** (só confere a leitura) | `AnIslandNobodyJoins…` (parcial) |
| Limite de jogadores | sim | sim | `TheServerNeverExceedsMaxPlayers` |
| Falha da API na admissão não derruba o servidor | sim | sim | `APIFailureRejects…` |
| HTTP real (rotas, cabeçalho, JSON camelCase) | — | sim, contra um `HttpListener` com as rotas da API | `HttpApiTests` |

## O que ainda NÃO existe (honesto)

Estas mensagens do protocolo chegam ao servidor e são respondidas com uma recusa clara (`unsupported`) ou ignoradas; nada é fingido:

| Falta | Mensagens | Observação |
|---|---|---|





| Convivência de bilhetes na API | — | ver abaixo |

### Física de objetos (S4)

A física fica atrás de `IIslandPhysics` (`Core`): o `Core` mantém os corpos em dia com os objetos (a cada 0,5 s, quando a versão dos objetos muda), empurra os corpos com quem anda, avança a simulação e manda aos jogadores só as poses que mudaram (a cada 0,1 s, operação `physics`, 40 corpos por mensagem; quem entra recebe todas). A implementação é `UnityIslandPhysics` (PhysX da Unity, `Physics.simulationMode = Script`, passo fixo de 1/60 s): objetos "físicos" viram `Rigidbody`, sólidos fixos viram colisores, o terreno vira um `MeshCollider` refeito quando é esculpido; esferas, caixas e cilindros (malha convexa); massa, atrito e quique vêm de `PhysicsRules`. O host .NET **não tem física** (os objetos não caem lá): quem precisa de física usa o Unity Dedicated Server.

Verificado no PhysX real, dentro do player (`LifeVerse.exe -lifeverse-physics-selfcheck`, 15 verificações): a bola cai, repousa na altura certa, dorme e dá contato com o chão com a velocidade da queda; cai sobre uma caixa fixa; é chutada por quem anda; recebe impulso/velocidade de script; quica; repousa no terreno esculpido. Verificado de ponta a ponta: o servidor dentro do player carrega a bola da API, ela cai e dorme, e 64 clientes recebem 147 mensagens de física cada, sem desconexões. O `Core` é testado com uma física de mentira (sincronização, passo, empurrão, mudança de pose, quem chega depois).

**Diferenças em relação ao Godot, não medidas**: PhysX e Godot Physics resolvem contatos de forma diferente (empilhamento, quique, atrito combinado: o Unity usa a média, não a regra do Godot), então a mesma cena não se comporta idêntica; o cilindro é uma malha convexa de 24 lados; o impulso de script muda a velocidade na hora; o contato informa a velocidade relativa antes do impacto. Corpos acima de 200 são ignorados (como no Godot). Falta comparar cenas reais lado a lado, medir o custo com 200 corpos e confirmar o `Rigidbody` com `ContinuousDynamic` em objetos finos.

### Scripts: o que está e o que falta

O `IslandScripting*.cs` do Godot não depende de engine, então o `Core` o compila direto de `island-server/Scripts` (um `Compile Include` no `.csproj`; uma correção lá vale para os dois servidores). Só duas coisas foram escritas: `ServerConfiguration` (a parte que o código de scripts lê) e `HttpCompat` (sobrecargas do .NET 5+ que faltam no `netstandard2.1`). **Quando o servidor Godot for aposentado, essas fontes precisam ser movidas para o `Core`.** Verificado com Lua real: toque e chat. **Não verificado**: os demais eventos, HUD, mídia, efeitos, pagamentos, NPCs e gestos no servidor (o código é o mesmo do Godot, mas não há teste meu); Lua dentro do player Unity (só o compilador Lua foi autoverificado). **Física de objetos**: veja a seção abaixo.

## Convivência com a API (decisão pendente)

A API grava um único `ProtocolVersion` (11) nos bilhetes e o servidor Godot exige igualdade. O servidor Unity fala o Wire 12 com o cliente, mas aceita bilhetes de protocolo 11 (`LIFEVERSE_TICKET_PROTOCOL`, padrão `NetworkProtocol.Version`), então **funciona sem mudar a API**. O custo: nada impede que um cliente Godot receba o endereço de uma ilha Unity (ou o contrário); ele só vê `ProtocolRejected` (a ilha responde com a versão 12). Para escolher o motor por ilha é preciso um campo `engine`/`wireVersion` na ilha e no bilhete. Isso é uma mudança de API e de banco (migração): **não foi feita** e depende de autorização.

## Achados durante a construção

1. **Snapshots grandes quebravam o transporte.** O LiteNetLib não fragmenta pacotes não confiáveis (limite ~1020 bytes); com 44 jogadores no raio de interesse o `Send` lançava exceção (58 falhas de tick no primeiro teste de carga). O Godot/ENet fragmenta, então o problema não aparecia lá. Correção: o servidor divide o snapshot em pacotes do **mesmo tick** com até 40 jogadores (926 bytes), o próprio jogador sempre no primeiro. **O cliente precisa mesclar os pacotes de um mesmo tick** (registrado em `WIRE_PROTOCOL.md`). O mesmo vale para `VehicleSnapshots` (pacotes de 32 veículos).
2. **O id 0 do LiteNetLib colidia com "sistema".** O Wire usa `PeerId` 0 para avisos e scripts (`ReceiveChat` do servidor), como o Godot; o LiteNetLib numera pares a partir de 0. `WireServer` agora expõe `id + 1`.
3. **O health check fechava a conexão com o pedido HTTP por ler**, o que faz o TCP mandar um RST e o cliente (`Invoke-WebRequest`, visto no player Unity) perder a resposta; o servidor Godot tem o mesmo desenho. Agora o pedido é lido antes (e uma sonda que só abre a conexão recebe a resposta depois de 300 ms). Teste: `TheHealthCheckAnswersARealHttpClient…`.
4. **`HttpIslandApi` só estava coberto por uma API de mentira em memória**; `HttpApiTests` exercita o HTTP de verdade.
5. **O subtarget "Server" ficava gravado no projeto Unity** depois do build do servidor Linux e quebrava o build Windows ("Dedicated Server support for Win is not installed"). `BuildWindows` agora fixa `StandaloneBuildSubtarget.Player`.
6. **Os `.csproj` escritos à mão estavam ignorados pelo git** (a regra `**/*.csproj` de `unity/.gitignore` serve aos projetos gerados pela Unity). O commit `4e73996` ficou sem `LifeVerse.Wire.csproj`, `LifeVerse.Shared.Unity.csproj` e `LifeVerse.Scripting.Unity.csproj`: um clone limpo não compilaria. A regra ganhou exceções para as pastas `UnityWire`, `UnityPlugins` e `UnityIslandServer`; os três arquivos entram no próximo commit.
## Linux: o que o teste mostrou e o que continua aberto

- Funciona em Linux **glibc** (Ubuntu). **O player da Unity não roda em Alpine (musl)**, e a produção atual usa contêineres Alpine (o API e o servidor Godot): o servidor Unity precisará de uma imagem Debian/Ubuntu (ou camada de compatibilidade glibc, não testada). O `Host` .NET roda em Alpine (o .NET tem alvo `linux-musl`), **mas não foi executado em Linux**: o Ubuntu do WSL não tem o SDK .NET.
- Não testado: contêiner (Docker), Easypanel, o Supervisor, portas UDP expostas ao mundo, HTTPS contra a API real, `LIFEVERSE_*` pelo Easypanel, carga de longa duração, vazamento de memória.
- Cuidado ao medir: bots que andam perto de uma bola física a empurram o tempo todo (mensagens de física contínuas); com 1 cliente parado longe dela a bola dorme e só 1 mensagem é enviada.

## Como operar

Variáveis (iguais às do Godot): `LIFEVERSE_INSTANCE_ID`, `LIFEVERSE_ISLAND_ID`*, `LIFEVERSE_API_URL`*, `LIFEVERSE_SERVICE_KEY`* (≥ 32), `LIFEVERSE_HOSTNAME`, `LIFEVERSE_GAME_PORT` (7777, **UDP**), `LIFEVERSE_HEALTH_PORT` (7070, TCP), `LIFEVERSE_MAX_PLAYERS`, `LIFEVERSE_SNAPSHOT_RATE`, `LIFEVERSE_INTEREST_RADIUS`, `LIFEVERSE_IDLE_SHUTDOWN_MINUTES`, `LIFEVERSE_CHAT_BLOCKED_TERMS`. Novas: `LIFEVERSE_TICKET_PROTOCOL` (11), `LIFEVERSE_TICK_RATE` (60), `LIFEVERSE_LOOPBACK_ONLY` (`1` só aceita o próprio computador).

```powershell
# Servidor .NET (sem Unity)
dotnet run --project unity\UnityIslandServer\Host\LifeVerse.IslandServer.Host.csproj -c Release

# Testes
dotnet test tests\LifeVerse.IslandServer.Tests\LifeVerse.IslandServer.Tests.csproj

# Servidor Unity (recompilar os plugins antes)
powershell -ExecutionPolicy Bypass -File unity\UnityPlugins\build-plugins.ps1
Unity.exe -batchmode -nographics -projectPath unity\UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildLinuxServer
```

Códigos de saída: `0` parada limpa (SIGTERM, Ctrl+C, reinício pedido pelo admin, ilha vazia); `1` falha ao subir (configuração inválida ou API inalcançável; o motivo sai no log como `server_config_invalid` / `server_start_failed`).

Integração com o Supervisor: a mesma porta de saúde (`/` responde 200/503 em JSON, o campo `status` continua `healthy`/`starting`), as mesmas variáveis e o mesmo contrato HTTP com a API. A porta de jogo passa de ENet para **UDP do LiteNetLib**: o mapeamento de portas do Easypanel/firewall continua sendo UDP. O Supervisor precisa apenas trocar o comando de inicialização por ilha; **não foi alterado nem testado** (ver `supervisor/`).

## Evidência

| Verificação | Resultado |
|---|---|
| `dotnet test LifeVerse.IslandServer.Tests` | 51 de 51 (execuções repetidas, sem instabilidade); inclui 64 jogadores por UDP no loopback: 60,2 Hz, tick médio 0,53 ms, p99 3,9 ms, 0 falhas |
| `dotnet test LifeVerse.Wire.Tests` (depois da mudança do id) | 25 de 25 |
| `Core` compila para `netstandard2.1` (Unity) e `net8.0` | 0 avisos, 0 erros |
| Unity Dedicated Server **Linux** (`BuildLinuxServer`) | compila (162 MB, 0 erros) e **foi executado** no Ubuntu 26.04 (glibc 2.43) do WSL 2, a 2026-11-19, com a API de mentira em Python: sobe sem GPU nem Vulkan (`-batchmode -nographics`), registra, carrega mundo, objetos e a bola de física, e responde saúde 200 a 60 Hz (tick 0,44 ms, p99 3,0 ms); **64 clientes** do Windows pela rede do WSL (UDP): 64 admitidos, 38,9 snapshots/s por cliente, 0 desconexões; **SIGTERM** (o sinal do Supervisor): salva as posições dos jogadores, se retira da API (`DELETE`), registra `server_stopped` e o processo sai |
| **Unity player Windows (Mono) como servidor**, `-lifeverse-server`, API de mentira por HTTP (`HttpListener` em PowerShell), clientes em outro processo | **64 de 64 clientes** admitidos por UDP, 39,8 pacotes de snapshot/s por cliente (2 pacotes por snapshot a 20 Hz), 0 desconexões, 0 `tick_failed`; saúde 200 com 60 Hz, tick 0,9 ms (p99 3,3 ms); 64 validações e 65 gravações de posição na API; encerramento por comando `shutdown` do admin: salva, se retira (`DELETE`), código de saída 0 |
| `Host` com configuração inválida ou API inalcançável | código de saída 1 e log estruturado |

Reproduzir a carga contra um servidor em execução (`Host` ou Unity): `dotnet run --project unity\UnityIslandServer\LoadTest -- --port 17777 --clients 64 --seconds 20` (os bilhetes são `unity-<n>`; é preciso uma API que os aceite).

Não verificado: **150 ms de latência com perda** (o teste de perda do Wire cobre só o transporte), física/scripts/construção/veículos/voz, Supervisor, IL2CPP, múltiplas ilhas ao mesmo tempo, carga com os clientes em outra máquina. O teste de 64 clientes roda cliente e servidor no mesmo computador: os tempos de tick incluem a concorrência por CPU, e nada foi medido com um cliente Unity de verdade (ainda não existe).