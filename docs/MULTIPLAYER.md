# Multiplayer — Fase 03

## Fluxo de admissão

1. O cliente autenticado chama `POST /api/v1/worlds/islands/{id}/entry-token`.
2. A API verifica usuário, sessão, avatar, acesso à ilha e heartbeat saudável; o ticket JWT dura 45 segundos e carrega usuário, avatar, ilha, sessão, `jti` e versão do protocolo.
3. O cliente abre ENet e envia apenas o ticket e `NetworkProtocol.Version` no handshake.
4. O island-server valida o ticket de volta na API por `X-LifeVerse-Service-Key`, rejeita versão incorreta, sessão expirada e reuso de `jti`, e só então cria o avatar.

O servidor nunca confia em posições, IDs ou nomes enviados pelo cliente. O cliente envia `MovementInput` sequenciado; o servidor simula em tick fixo, limita aceleração/velocidade e transmite snapshots quantizados.

## Canais

| Canal | Transporte | Uso |
|---|---|---|
| 0 | confiável | handshake, spawn/despawn, chat e mudanças discretas de estado (como ligar/desligar voo) |
| 1 | não confiável ordenado | inputs de movimento |
| 2 | não confiável ordenado | snapshots de estado |

Snapshots são limitados a 256 jogadores e 32 KiB, com interesse por proximidade. O cliente interpola avatares remotos e reconcilia o avatar local com o último input confirmado. Desconexões suspendem o avatar por dez segundos para permitir reconexão sem duplicação.

## Chat local

Mensagens são sanitizadas, limitadas a 256 caracteres, cinco por dez segundos por conexão e distribuídas somente num raio de 30 m. Termos adicionais podem ser configurados em `LIFEVERSE_CHAT_BLOCKED_TERMS`.

## Execução local

Defina `LIFEVERSE_ISLAND_ID` para uma ilha existente e mantenha o island-server com `LIFEVERSE_API_URL`, `LIFEVERSE_SERVICE_KEY`, `LIFEVERSE_GAME_PORT` e `LIFEVERSE_HEALTH_PORT`. O cliente aceita a URL da API via `LIFEVERSE_API_URL`; informe o UUID da ilha no painel Multiplayer da cena World.

O processo headless é iniciado com `--headless --path island-server`. Ele expõe saúde HTTP apenas em loopback por padrão e registra heartbeat no endpoint Platform.

## Limitações conhecidas

- O avatar inicial continua sendo o placeholder humanoide legalmente original da Fase 02; a rede transporta sua aparência persistida.
- A colisão autoritativa do servidor é uma simulação cinemática de ilha plana. A integração de geometria/PhysicsServer3D da ilha fica para a fase de mundo avançado.
- Docker não está instalado no ambiente auditado; por isso o Compose foi validado estaticamente, mas não executado nesta máquina.

- Protocolo **7**: `RequestTeleport(parcelId, x, z)` (cliente → servidor) e `TeleportResult(ok, code, x, z, yaw)` (servidor → cliente), usados pelo teleporte de terrenos. Publique API e servidor de ilhas juntos.
- Protocolo **8**: `RefreshAppearance()` (cliente → servidor) e `AppearanceChanged(peerId, displayName, appearanceJson)` (servidor → cliente). O servidor de ilha busca a aparência na API (`/api/v1/platform/avatars/{id}/appearance`, chave de serviço) e nunca usa o que o cliente diria. Publique API e servidor de ilhas juntos.
- Protocolo **9**: `SetFlight(enabled)` é confiável e idempotente. Os inputs contínuos continuam no canal não confiável ordenado, mas perder um pacote não pode mais deixar cliente e servidor discordando sobre o voo. Publique cliente, API e servidores de ilha juntos.
