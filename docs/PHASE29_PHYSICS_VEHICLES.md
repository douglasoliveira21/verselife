# Fase 29 — Física e veículos

## Entregue

- Entidades persistentes de veículos e assentos de motorista/passageiro.
- Criação de veículo terrestre de referência com massa, velocidade máxima e estado do motor.
- Entrada exclusiva em assentos, saída segura e tratamento de veículo lotado.
- Comandos de direção autoritativos no backend com aceleração limitada, frenagem, direção, revisão e limites de velocidade.
- Acesso de criação restrito a proprietário/permissão de construção; condução exige assento de motorista.
- Migration `Phase29PhysicsVehicles` criada para `world.vehicles` e `world.vehicle_seats`.

## Limitações conhecidas

- A simulação atual é um núcleo de estado/validação; colisão PhysX, suspensão, atrito, restituição e replicação ENet precisam ser conectados ao island-server.
- Scripts Lua não recebem acesso direto a saldo ou posição oficial; comandos futuros devem passar por este contrato autoritativo.
- Teste de dois clientes requer execução do servidor headless e do cliente Godot, não verificado automaticamente neste ambiente.
