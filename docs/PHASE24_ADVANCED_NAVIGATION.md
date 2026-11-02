# Fase 24 — Navegação avançada

## Entregue

- Busca global autenticada e paginada em `/api/v1/discovery/search`, reunindo ilhas públicas (ou próprias), eventos públicos e produtos ativos.
- Filtros por tipo, ordenação determinística e proteção contra exposição de ilhas privadas.
- Teleporte idempotente: uma solicitação pendente/reservada do mesmo usuário e destino é reutilizada, evitando avatares/reservas duplicados.
- Verificação de acesso, capacidade da ilha e preparação via World Manager continuam no servidor.
- Landmarks agora podem ser renomeados e ter visibilidade alterada, além de criar/listar/excluir.

## Limitações conhecidas

- O minimapa, marcadores visuais, barra de carregamento e câmera de fotografia ainda precisam de acabamento no cliente Godot.
- Usuários e grupos não foram incluídos na busca global porque as entidades atuais não possuem uma política de diretório público explícita; isso evita vazar dados privados.
- Histórico de teleporte ainda depende das reservas existentes e não possui uma tabela de destinos concluídos.
