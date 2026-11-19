# LifeVerse Wire — protocolo 12

Especificação do protocolo de rede do cliente e do servidor de ilha em Unity (decisão D3). Código: `unity/UnityWire` (`LifeVerse.Wire`, alvos `netstandard2.1` e `net8.0`). Testes: `tests/LifeVerse.Wire.Tests` (25 testes xUnit) e, dentro da Unity, `WireTests` e a autoverificação do player.

**Não é compatível com o protocolo 11 do Godot** (`SceneMultiplayer`). As 44 mensagens são as mesmas dos 44 RPCs (mesmos nomes e a mesma ordem de argumentos); os payloads binários `Snapshot`, `VehicleSnapshots` e de voz continuam sendo os de `SnapshotCodec`, `VehicleCodec` e `VoiceCodec`, **byte a byte iguais**.

## Transporte
- **Biblioteca:** LiteNetLib 2.1.5 (MIT, C# puro). O ENet-CSharp foi descartado no spike S2: exige um binário nativo por plataforma e não há compilador C++ nesta máquina. O LiteNetLib traz canais, entrega confiável ordenada, não confiável sequenciada e fragmentação.
- **Chave de conexão:** `"LifeVerse.Wire"`. Muda só se o formato de quadro mudar. A versão do jogo (12) é conferida depois, em `Authenticate`, para o cliente poder mostrar "o servidor exige o protocolo N" (`ProtocolRejected`), como no protocolo 11.
- **Canais** (3, iguais aos do protocolo 11): 0 = confiável; 1 = entradas do cliente; 2 = estado do servidor.
- **Entrega:** `Reliable` = confiável ordenada. `Sequenced` = não confiável que descarta o que chega fora de ordem (o "não confiável ordenado" do protocolo 11). `Unreliable` = sem garantia (voz).

## Quadro
`[1 byte: id da mensagem][campos]`, até 1.200.000 bytes. Ids de `0x01` a `0x16` vão do cliente ao servidor; de `0x81` a `0x96`, do servidor ao cliente. Quem recebe só aceita a direção que lhe cabe.

## Campos (todos em little-endian)
| Tipo | Formato |
|---|---|
| `byte`, `bool` | 1 byte; `bool` só aceita 0 ou 1 |
| `int`, `uint` | 4 bytes |
| `long` | 8 bytes |
| `float` | 4 bytes (IEEE 754); **NaN e infinito são recusados** |
| comprimento | número variável de até 5 bytes (7 bits por byte; o 5º usa só 4 bits) |
| `string` | comprimento em bytes + UTF-8 estrito (inválido é recusado) |
| `byte[]` | comprimento + bytes |
| `float[]` | quantidade + floats |

Todo comprimento tem um limite por campo (tabela abaixo); passar do limite recusa o quadro. Bytes sobrando no fim também recusam o quadro.

## Limites
| Campo | Máximo | Origem |
|---|---:|---|
| Token | 4.096 B | — |
| Ids (objeto, parcela, avatar, anexo) | 64 B | — |
| Códigos de resposta | 64 B | — |
| Nome de exibição / de veículo / tipo de veículo | 128 / 128 / 32 B | — |
| Chat | 1.024 B | `MaxChatCharacters` (256) × 4 |
| JSON de diálogo / de operação de construção / de aparência | 16 KiB / 64 KiB / 128 KiB | — |
| Payload de snapshot (jogadores e veículos) | 32 KiB | `NetworkProtocol.MaxSnapshotBytes` |
| Importação de mapa de alturas | 300.000 B | `NetworkProtocol.MaxTerrainImportBytes` |
| Terreno completo / patch | 1 MiB / 256 KiB | — |
| Pacote de voz | 400 B | `VoiceSettings.MaxPacketBytes` |
| Valores de anexo ao vivo | 16 floats | — |

## Mensagens
Canal e entrega entre parênteses; "Rel" = confiável (canal 0).

### Cliente → servidor
| Id | Mensagem | Campos | Rota |
|---|---|---|---|
| 0x01 | Authenticate | string token, int protocolVersion | Rel |
| 0x02 | SendChat | string message | Rel |
| 0x03 | SetFlight | bool enabled | Rel |
| 0x04 | SubmitBuildOperation | string operationJson | Rel |
| 0x05 | SubmitTerrainEdit | int tool, float x, float z, float radius, float strength, int layer | Rel |
| 0x06 | SubmitTerrainImport | byte[] heights | Rel |
| 0x07 | SubmitSeaLevel | float level | Rel |
| 0x08 / 0x09 / 0x0A | UndoTerrain / RedoTerrain / RequestTerrain | — | Rel |
| 0x0B | RefreshAppearance | — | Rel |
| 0x0C | AttachmentLive | string id, float[] values | Rel |
| 0x0D | RequestTeleport | string parcelId, float x, float z | Rel |
| 0x0E | TouchObject | string objectId | Rel |
| 0x0F | ScriptDialogResponse | string json | Rel |
| 0x10 / 0x11 | EnterVehicle / ExitVehicle | int vehicleId / — | Rel |
| 0x12 | Gesture | int gestureId | Rel |
| 0x13 | SubmitInput | uint sequence, float x, float z, byte flags | canal 1, Sequenced |
| 0x14 | SubmitVehicleInput | float throttle, float steer, bool brake | canal 1, Sequenced |
| 0x15 | HeadLook | float pitch | canal 1, Sequenced |
| 0x16 | SubmitVoice | uint sequence, byte[] data | canal 1, Unreliable |

### Servidor → cliente
| Id | Mensagem | Campos | Rota |
|---|---|---|---|
| 0x81 | AuthResult | bool success, string code, uint serverTick | Rel |
| 0x82 | ProtocolRejected | int serverVersion | Rel |
| 0x83 | SpawnPlayer | int peerId, string avatarId, string displayName, string appearanceJson, float x, y, z, int state | Rel |
| 0x84 | DespawnPlayer | int peerId, bool suspended | Rel |
| 0x85 | AppearanceChanged | int peerId, string displayName, string appearanceJson | Rel |
| 0x86 | AttachmentLiveChanged | int peerId, string id, float[] values | Rel |
| 0x87 | ReceiveChat | int peerId, string displayName, string message, long unixMilliseconds | Rel |
| 0x88 / 0x89 | BuildOperationApplied / BuildOperationRejected | string operationJson / string code | Rel |
| 0x8A | TerrainFull | byte[] data, bool canEdit | Rel |
| 0x8B | TerrainPatched | byte[] patch, long revision | Rel |
| 0x8C | TerrainEdited | int tool, float x, z, radius, strength, int layer, long revision | Rel |
| 0x8D | TerrainEditRejected | string code | Rel |
| 0x8E | SeaLevelChanged | float level, long revision | Rel |
| 0x8F | TeleportResult | bool ok, string code, float x, float z, float yaw | Rel |
| 0x90 / 0x91 | VehicleSpawned / VehicleRejected | int vehicleId, string name, string type, float x, y, z, yaw / string code | Rel |
| 0x92 | GestureChanged | int peerId, int gestureId | Rel |
| 0x93 | Snapshot | byte[] payload (`SnapshotCodec`) | canal 2, Sequenced |
| 0x94 | VehicleSnapshots | byte[] payload (`VehicleCodec`) | canal 2, Sequenced |
| 0x95 | HeadLookChanged | int peerId, float pitch | canal 2, Sequenced |
| 0x96 | ReceiveVoice | int speakerPeer, uint sequence, byte[] data | canal 2, Unreliable |

**Diferenças de tipo em relação aos RPCs do Godot** (o valor sempre coube no tipo menor): `sequence` e `serverTick` são `uint` (eram `long`) e as flags de entrada são `byte` (`InputFlags` já é um byte).

## Admissão e regras do servidor
1. O cliente conecta (chave `LifeVerse.Wire`) e envia `Authenticate(ticket, 12)` como **primeira** mensagem.
2. A aplicação confere o ticket e a versão. Aceitando: `MarkAuthenticated(peerId)` e `AuthResult(true, …)`. Versão diferente: `ProtocolRejected(versão do servidor)` e desconexão.
3. O `WireServer` desconecta, por padrão: quem envia qualquer coisa que não seja `Authenticate` antes de autenticar (`not_authenticated`); quem não se autentica em 10 s (`auth_timeout`); e quem envia um quadro vazio, de tipo desconhecido, de direção errada, malformado ou com bytes sobrando.
4. O servidor recusa novas conexões acima do limite de jogadores.
5. Toda validação de jogo (permissões, limites, propriedade) continua no servidor, como hoje.

## O que os testes provam
- Ida e volta das 44 mensagens para os mesmos bytes; 22 por direção; rotas e contagens iguais às do protocolo 11.
- Todo truncamento, bytes sobrando, campos acima do limite, floats não finitos, UTF-8 inválido, booleano inválido e número variável longo demais são recusados **sem exceção**; 60.000 quadros aleatórios não lançam exceção.
- Rede real em UDP: admissão, recusa antes da autenticação, prazo de autenticação, quadro malformado, 500 mensagens confiáveis em ordem, entradas sequenciadas sem voltar atrás, 900 KB fragmentados chegando intactos, payload de snapshot do Godot atravessando a rede, broadcast, limite de jogadores e **12% de perda de datagramas com atraso de até 35 ms** (200 mensagens confiáveis chegam em ordem).
- Dentro da Unity: os mesmos fluxos em modo Edit e num player Windows compilado (Mono).

## Fora do escopo desta entrega
Este documento cobre o protocolo e o transporte. **Não** há ainda um servidor de ilha nem um cliente de jogo falando o protocolo: isso é a Fase 6 (servidor) e as Fases 7 a 12. Também não foram feitos testes de 64 clientes simultâneos nem de latência alta.
