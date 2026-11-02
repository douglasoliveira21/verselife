# Fase 32 — Performance

## Instrumentação entregue

- Overlay do cliente (`LIFEVERSE_SHOW_METRICS=1`) exibe FPS, frame time, RAM e quantidade de nós.
- Endpoint autenticado `GET /api/v1/platform/metrics` mede jogadores/sessões ativas, ilhas, objetos, scripts ativos, instâncias e tempo das consultas ao banco.
- A instrumentação é somente leitura e não altera a simulação.

## Resultado desta execução

Os projetos backend e cliente foram compilados sem erros. Não foi executado neste ambiente um cenário de carga com 20/50/100 jogadores ou 1.000/5.000 objetos; portanto não há números inventados de FPS, CPU, GPU, banda ou limites de capacidade.

## Procedimento de medição recomendado

1. Iniciar API, PostgreSQL, Redis e island-server.
2. Abrir o cliente com `LIFEVERSE_SHOW_METRICS=1`.
3. Registrar o endpoint de métricas a cada 5 segundos durante cenários de 20, 50 e 100 sessões.
4. Repetir com 1.000 e 5.000 objetos e scripts concorrentes.
5. Comparar p50/p95 de frame time, consultas, tick, memória e tráfego antes/depois.

Até essa coleta, os limites recomendados permanecem os já configurados por ilha e não devem ser tratados como capacidade prometida.
