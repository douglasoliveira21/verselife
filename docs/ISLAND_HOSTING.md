# Hospedagem de ilhas (servidores de ilha)

Cada servidor de ilha (o servidor dedicado da Unity) hospeda **uma** ilha: registra-se na API e só então a API emite tickets de entrada para ela. Um único serviço, o `island-supervisor`, sobe as ilhas quando alguém tenta entrar e as desliga quando ficam vazias. O passo a passo de implantação está em `docs/unity-migration/DEPLOY_UNITY_ISLANDS.md`.

## Ilhas sob demanda (supervisor)

1. O jogador pede entrada. Se a ilha não tem servidor, a API a marca como `starting` e responde `503` com `Retry-After: 5` e `state: "starting"`. O cliente espera e tenta de novo (cerca de 60 s no total).
2. O supervisor consulta `GET /api/v1/platform/supervisor/pending?engine=unity` (chave de serviço) a cada poucos segundos e inicia um `LifeVerseServer.x86_64` por ilha pendente, numa porta UDP livre do intervalo.
3. A ilha se registra, o próximo pedido de entrada recebe o ticket e o jogador entra.
4. Depois de `SUPERVISOR_IDLE_MINUTES` (padrão 10) sem ninguém, a ilha salva o mundo e encerra sozinha; a porta volta a ficar livre.
5. Uma ilha que fica em `starting` por mais de 3 minutos sem servidor volta para `suspended`. Uma ilha que cai 3 vezes seguidas fica 5 minutos sem ser reiniciada.

Variáveis do supervisor: `LIFEVERSE_API_URL`, `LIFEVERSE_SERVICE_KEY` (mesma de `IslandServer__SharedKey`, mínimo 32 caracteres), `LIFEVERSE_HOSTNAME` (endereço público que o cliente usa), `SUPERVISOR_UNITY_SERVER_PATH`, `SUPERVISOR_PORT_START` (7800 na imagem), `SUPERVISOR_MAX_ISLANDS` (8), `SUPERVISOR_HEALTH_PORT_START`, `SUPERVISOR_IDLE_MINUTES`, `SUPERVISOR_POLL_SECONDS`.

No EasyPanel/Swarm: um serviço com a imagem de `island-supervisor/Dockerfile.unity`, com uma porta UDP publicada em modo host para cada ilha simultânea. Use `--update-order stop-first`: as portas em modo host impedem o contêiner novo de iniciar antes de o antigo parar. O painel pode voltar para `start-first` a cada deploy; o watchdog de `scripts/vps` corrige (`sudo bash install-island-watchdog.sh <nome do serviço>`; os scripts passaram apenas na checagem de sintaxe, não foram executados contra um Docker real). Ao reiniciar o supervisor, as ilhas dele são encerradas e voltam sob demanda.

## Diagnóstico do 503

1. O log do supervisor deve mostrar `supervisor started (unity)`, `started island ...`, `server_registered` e `server_started`; `server_start_failed` indica o motivo.
2. A ilha precisa ter o motor `unity` (`world.islands.engine`): o supervisor só recebe essas.
3. O cliente e o servidor precisam ter o mesmo protocolo; um diferente é recusado com mensagem de versão incompatível, não com 503.

## Integração contínua

`.github/workflows/ci.yml` roda a cada push/PR: build da solução, testes da API e build do estágio do supervisor da imagem. `.github/workflows/unity.yml` roda os testes do protocolo e do servidor de ilha (e os da Unity, se houver licença nos segredos do repositório).

## Armazenamento de arquivos (MinIO/S3)

Se `ObjectStorage:Endpoint`, `AccessKey` e `SecretKey` estiverem preenchidos, os uploads (modelos, texturas, sons) vão para o bucket `ObjectStorage:Bucket` (criado se não existir) usando assinatura AWS SigV4 escrita à mão, sem SDK. `ObjectStorage:Region` é `us-east-1` por padrão (serve para o MinIO). Sem esses valores, os arquivos ficam em `ObjectStorage:LocalRoot` no disco. As chaves já gravadas (`<dono>/<hash>`) são iguais nos dois modos; para migrar basta copiar a pasta para o bucket com o mesmo caminho. O cliente SigV4 é testado com o exemplo publicado pela AWS e com um servidor falso; não foi testado contra um MinIO real.

O `Endpoint` precisa de um nome que a própria API enxergue. Em painéis como o Easypanel o nome interno costuma ser `<projeto>_<serviço>`; para conferir, no console do app da API (não no seu computador): `getent hosts <nome>` (sem resposta = o nome não existe ali, e o log mostra `Name does not resolve`). O MinIO recusa um `Host` com `_` (`400 invalid hostname`, segundo relatos), por isso a API conecta pelo nome configurado e envia e assina o `Host` com `-` no lugar.

## Notificações em tempo real

`GET /api/v1/notifications/ws` (WebSocket, cabeçalho `Authorization: Bearer <token>`) entrega eventos JSON `{"type","data","at"}`: `message` (mensagem privada), `friend_request`, `friend_accepted`, `group_invite`. Até 4 conexões por usuário; eventos para quem está offline são descartados (o REST continua sendo a fonte). O cliente conecta sozinho depois do login, reconecta com espera crescente (2 s até 60 s) e mostra cada evento como aviso na tela. Com mais de uma instância da API seria preciso um barramento (Redis) entre elas; hoje o aviso só chega se o destinatário estiver ligado à mesma instância.
