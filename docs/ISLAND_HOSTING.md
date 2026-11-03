# Hospedagem de ilhas (servidores de ilha)

Cada processo `island-server` hospeda **uma** ilha: lê `LIFEVERSE_ISLAND_ID`, registra-se na API e só então a API emite tickets de entrada para ela. Se uma ilha não tem servidor saudável (heartbeat nos últimos 45 s), a entrada devolve **503 "No healthy island server is available"**.

## Uma ilha por serviço

Para N ilhas rodando ao mesmo tempo, crie N serviços a partir da mesma imagem, cada um com valores próprios:

| Variável | Observação |
|---|---|
| `LIFEVERSE_ISLAND_ID` | UUID da ilha (veja `GET /api/v1/worlds/islands`) |
| `LIFEVERSE_GAME_PORT` | UDP único por serviço (7777, 7778, 7779…) |
| `LIFEVERSE_HEALTH_PORT` | TCP único por serviço (7070, 7071, 7072…) |
| `LIFEVERSE_HOSTNAME` | host público que o cliente usará para conectar |
| `LIFEVERSE_API_URL`, `LIFEVERSE_SERVICE_KEY` | iguais em todos |

Publique a porta UDP de cada serviço (modo host no Swarm) e libere-a no firewall. Aumentar réplicas do mesmo serviço não adiciona ilhas: todas herdam o mesmo `LIFEVERSE_ISLAND_ID`.

No Swarm, use `--update-order stop-first` (as portas em modo host impedem iniciar o novo container antes de parar o antigo).

## Diagnóstico do 503

1. `curl http://127.0.0.1:<porta de saúde>/health` no servidor deve responder `ready:true`.
2. `docker service logs <serviço>` deve mostrar `server_registered` e `server_started`; `server_start_failed` indica o motivo.
3. Confirme que o ID da ilha escolhida é o mesmo de `LIFEVERSE_ISLAND_ID` de algum serviço em execução.
4. O cliente e o servidor de ilhas precisam ter o mesmo protocolo (v2); um servidor antigo é recusado com mensagem de versão incompatível, não com 503.

## Deploy sem ficar em Pending (watchdog)

O painel recria a configuração do serviço a cada deploy e volta para `start-first`, o que deixa a tarefa nova em `Pending ... host-mode ports`. Para corrigir sozinho:

```bash
git clone https://github.com/douglasoliveira21/verselife.git && cd verselife/scripts/vps
sudo bash install-island-watchdog.sh verselife_island
```

Passe mais de um nome de serviço para vigiar várias ilhas (`sudo bash install-island-watchdog.sh verselife_island verselife_island2`). O temporizador roda a cada 30 s e só age quando a ordem não é `stop-first`; o pior caso depois de um deploy é cerca de 30 s esperando. Para conferir: `systemctl list-timers lifeverse-island-watchdog.timer` e `journalctl -u lifeverse-island-watchdog -n 20`. Os scripts passaram apenas na checagem de sintaxe; não foram executados contra um Docker real.

## Ilhas sob demanda (supervisor)

Em vez de um serviço por ilha, um único serviço `island-supervisor` sobe as ilhas só quando alguém tenta entrar e as desliga quando ficam vazias.

Como funciona:

1. O jogador pede entrada. Se a ilha não tem servidor, a API a marca como `starting` e responde `503` com `Retry-After: 5` e `state: "starting"`. O cliente mostra "Iniciando a ilha…" e tenta de novo a cada 4 s (até 18 vezes).
2. O supervisor consulta `GET /api/v1/platform/supervisor/pending` (chave de serviço) a cada poucos segundos e inicia um Godot headless por ilha pendente, numa porta UDP livre do intervalo (`7777` em diante, saúde em `7070` em diante).
3. A ilha se registra, o próximo pedido de entrada recebe o ticket e o jogador entra.
4. Depois de `SUPERVISOR_IDLE_MINUTES` (padrão 10) sem ninguém, a ilha salva o mundo e encerra sozinha; a porta volta a ficar livre.
5. Uma ilha que fica em `starting` por mais de 3 minutos sem servidor volta para `suspended`. Uma ilha que cai 3 vezes seguidas fica 5 minutos sem ser reiniciada.

Variáveis do supervisor: `LIFEVERSE_API_URL`, `LIFEVERSE_SERVICE_KEY` (mesma de `IslandServer__SharedKey`, mínimo 32 caracteres), `LIFEVERSE_HOSTNAME` (endereço público que o cliente usa), `SUPERVISOR_GODOT_PATH`, `SUPERVISOR_PROJECT_PATH`, `SUPERVISOR_PORT_START` (7777), `SUPERVISOR_MAX_ISLANDS` (8), `SUPERVISOR_HEALTH_PORT_START` (7070), `SUPERVISOR_IDLE_MINUTES`, `SUPERVISOR_POLL_SECONDS`.

No EasyPanel/Swarm: crie um serviço com a imagem de `island-supervisor/Dockerfile` e publique em modo host o intervalo **7777-7784/udp** (um por ilha simultânea). Como o supervisor é um único serviço, o problema de `start-first` do deploy por ilha deixa de existir para as ilhas; só o próprio supervisor precisa de `--update-order stop-first`. Ao reiniciar o supervisor as ilhas dele são encerradas e voltam sob demanda.

Verificado localmente: API + Postgres + supervisor + Godot real (503 `starting` → ilha registrada em ~2 s → entrada `200` → desligamento por ociosidade → porta liberada). As imagens Docker não foram construídas fora do CI.

## Integração contínua

`.github/workflows/ci.yml` roda a cada push/PR: build da solução e testes .NET, build das imagens Docker da ilha e do supervisor, e os testes de fumaça do Godot (`scripts/godot-smoke.sh`: terreno, construção, avatar no chão, inventário e scripts). O job do Godot é `continue-on-error` porque depende da imagem `godot-ci`; localmente: `bash scripts/godot-smoke.sh <caminho do godot>`.

## Armazenamento de arquivos (MinIO/S3)

Se `ObjectStorage:Endpoint`, `AccessKey` e `SecretKey` estiverem preenchidos, os uploads (modelos, texturas, sons) vão para o bucket `ObjectStorage:Bucket` (criado se não existir) usando assinatura AWS SigV4 escrita à mão, sem SDK. `ObjectStorage:Region` é `us-east-1` por padrão (serve para o MinIO). Sem esses valores, os arquivos ficam em `ObjectStorage:LocalRoot` no disco. As chaves já gravadas (`<dono>/<hash>`) são iguais nos dois modos; para migrar basta copiar a pasta para o bucket com o mesmo caminho. O cliente SigV4 é testado com o exemplo publicado pela AWS e com um servidor falso; não foi testado contra um MinIO real.

## Notificações em tempo real

`GET /api/v1/notifications/ws` (WebSocket, cabeçalho `Authorization: Bearer <token>`) entrega eventos JSON `{"type","data","at"}`: `message` (mensagem privada), `friend_request`, `friend_accepted`, `group_invite`. Até 4 conexões por usuário; eventos para quem está offline são descartados (o REST continua sendo a fonte). O cliente conecta sozinho depois do login, reconecta com espera crescente (2 s até 60 s) e mostra cada evento como aviso na tela. Com mais de uma instância da API seria preciso um barramento (Redis) entre elas; hoje o aviso só chega se o destinatário estiver ligado à mesma instância.
