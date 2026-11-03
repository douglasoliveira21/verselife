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
