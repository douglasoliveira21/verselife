# Fase 23 — Terrenos e parcelas

## Entregue

- Entidade persistente `world.parcels` com UUID, proprietário, grupo, limites, área calculada, cota de objetos, permissões, ambiente, spawn e revisão.
- Histórico `world.parcel_changes` para alterações administrativas e operações de terreno.
- Endpoints autenticados para listar, criar, consultar, atualizar, excluir logicamente e auditar parcelas.
- Controle otimista de concorrência por `ExpectedRevision` para evitar sobrescrever alterações simultâneas.
- Validação de limites dentro do tamanho da ilha, área positiva, quotas e JSON de configuração.
- Operações de terreno autorizadas com payload limitado, versionamento e registro para undo/redo futuro.
- Migration `Phase23Parcels` criada; a API aplica migrations versionadas na inicialização.

## Limitações conhecidas

- O cliente ainda não possui pincel visual de heightmap com preview/gizmos; a API fornece o contrato seguro para elevar, abaixar, suavizar, nivelar, pintar e restaurar.
- Permissões específicas por usuário/grupo de parcela ainda precisam de uma tabela própria; nesta etapa o JSON de permissões é validado e o controle administrativo usa as permissões da ilha.
- A validação de sobreposição entre polígonos complexos não existe; os limites atuais são retângulos axis-aligned.
