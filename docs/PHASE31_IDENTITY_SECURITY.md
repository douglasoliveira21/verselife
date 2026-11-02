# Fase 31 — Identidade social e segurança

## Entregue

- Perfil público autenticado por usuário, com nome, display name, biografia, foto, data de entrada e presença filtrada.
- Atualização de biografia, foto e política de privacidade pelo próprio usuário.
- Políticas de perfil/presença e mensagens privadas aplicadas no servidor.
- Mensagens privadas respeitam bloqueios e podem exigir amizade; usuários excluídos retornam `404`.
- Rate limit e auditoria de denúncias existentes permanecem ativos.
- Migration `Phase31IdentitySecurity` criada para dados de perfil e preferências.

## Limitações conhecidas

- Grupos públicos, ilhas públicas e criações publicadas ainda não são agregados na resposta do perfil.
- Mute local e controles visuais de privacidade precisam ser conectados ao cliente Godot; a restrição de mensagens é server-side.
- Fila administrativa e recursos de denúncias já existem em módulos de moderação, mas ainda precisam de uma UI web completa.
