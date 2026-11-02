# Known issues

- Suíte xUnit não concluiu nesta máquina durante a auditoria; investigar testes que dependem de banco/serviços e adicionar timeout explícito.
- Testes manuais do cliente Godot (mouse/câmera, foco, resoluções, animações, água, veículos) ainda não executados nesta auditoria.
- Docker Compose/PostgreSQL/Redis/MinIO e aplicação de migrations em ambiente limpo não foram verificados nesta rodada.
- Drag-and-drop visual de inventário, miniaturas reais, upload retomável e processamento isolado de assets ainda são parciais.
- Editor de heightmap/undo visual e permissões específicas por grupo de parcela ainda são parciais.
- LiveKit real, seleção de dispositivos, VAD, áudio espacial e reconexão não foram verificados.
- Física PhysX completa, replicação ENet de veículos e dispatcher de eventos de ilha ainda precisam de integração.
- Busca social e perfil ainda não agregam grupos/ilhas/criações públicas.
- Avaliações/favoritos de marketplace e aluguel de parcelas ainda não estão completos.
- Gerenciador universal de janelas, menus contextuais completos e testes de acessibilidade visual ainda são parciais.
