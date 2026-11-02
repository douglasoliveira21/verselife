# Release readiness

## Status: NÃO PRONTO PARA LANÇAMENTO PÚBLICO

### Bloqueadores

- Suíte automatizada não finalizou na auditoria atual.
- Testes de integração Docker/PostgreSQL/Redis/MinIO não executados nesta rodada.
- Cliente Godot, multiplayer ENet, LiveKit e fluxo de dois clientes não verificados manualmente.
- Carga de 20/50/100 jogadores e cenários de milhares de objetos não medidos.

### Critérios mínimos antes de beta fechado

1. Fazer a suíte xUnit concluir em CI com timeout e relatório de falhas.
2. Subir Compose limpo, aplicar todas as migrations e validar reinicialização sem perda.
3. Executar cliente em 1366×768 e 1920×1080, incluindo input, câmera, avatar e UI.
4. Testar dois clientes ENet, voz LiveKit, chat, teleporte, construção e reconexão.
5. Medir p50/p95 de frame time, tick, consultas, memória e tráfego nos cenários de carga.
6. Resolver falhas críticas/altas encontradas e atualizar este documento com evidências.
