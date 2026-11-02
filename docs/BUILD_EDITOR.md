# Editor de construção 3D — Fase 05

O modo Construir é aberto pelo botão no painel World ou pela tecla `B`. A seleção usa raycast do cursor; o painel mostra a primitiva, snap de posição, cor, revisão e estado de permissão. O editor suporta cubo, esfera, cilindro, cone, plano, prisma, tubo, anel e pirâmide.

Cada objeto persistido tem UUID, ilha, proprietário, grupo, tipo, posição/rotação/escala, material JSON, física JSON, revisão e timestamps. A API limita escala a `0.01..64`, rejeita NaN/infinito, permite apenas primitivas catalogadas e aplica a cota `resource_limits.objects` da ilha. O island-server limita comandos de construção a 20 por segundo por conexão antes de encaminhá-los para o endpoint interno autenticado por chave de serviço.

Operações `create`, `update`, `delete`, `duplicate`, `group` e `ungroup` são registradas em `world.build_operations`. Atualizações exigem `expectedRevision`; conflitos retornam 409 e não sobrescrevem o estado oficial. O resultado aceito é replicado via RPC confiável aos clientes autenticados.

Materiais aceitam cor, transparência, emissão, rugosidade e metallic. A física inicial aceita colisão, trigger e camadas; objetos são reconstruídos com mesh e forma de colisão no cliente. A estratégia atual é estática por padrão, com limites de tamanho e culling por interesse do servidor herdado do multiplayer.

O histórico local envia undo/redo ao servidor, portanto uma reversão continua sujeita à autorização e revisão. O snapshot de ilha e o histórico de operações sobrevivem à reinicialização pelo mesmo pipeline de persistência da Fase 04.

Limitações conhecidas: seleção múltipla visual, gizmos tridimensionais avançados, texturas remotas/UV e simulação dinâmica completa ainda são extensões futuras; o contrato persistente já reserva essas propriedades em JSON e grupos por `group_id`.
