# Grupos e Organizações 2.0

Plano em 5 fases. Este documento descreve o que existe hoje (**Fase 1 – núcleo**) e o que falta.

| Fase | Conteúdo | Estado |
|---|---|---|
| 1 | Núcleo: criação, identidade, cargos e permissões, convites, pedidos, público/privado, moderação, histórico, transferência de liderança, dissolução, anti-abuso, janela de administração | feito |
| 2 | Comunicação: chat de grupo (respeitando silenciamento), anúncios, eventos exclusivos, notificações | pendente |
| 3 | Propriedades: terrenos do grupo, construção compartilhada, administração coletiva, acesso por grupo | pendente |
| 4 | Economia: tesouraria, contribuições voluntárias, lojas do grupo, marketplace, auditoria financeira | pendente |
| 5 | Interface de administração completa e integração final | pendente |

## Regras compartilhadas (`shared/Groups/GroupRules.cs`)

Usadas pela API e pelo cliente (o cliente só habilita o que o servidor aceitaria; o servidor confere tudo de novo).

- O líder tem nível implícito 1000. Cargos têm nível de 0 a 99 (Membro 0, Moderador 40, Administrador 80).
- Só se age sobre quem tem nível **estritamente menor** que o seu.
- Só se cria/atribui cargos de nível abaixo do próprio (o líder pode até 99).
- Só se concede permissão que se possui (remover é livre).
- Freio de abuso: quem não é líder faz no máximo 30 ações de moderação (expulsar, banir, silenciar, mudar cargo) em 10 minutos, medido pelo histórico.
- Limites: 20 cargos, 100 convites pendentes, 10 pedidos pendentes por pessoa, 5 grupos criados, 30 participações, 500 membros (até 5000).
- Permissões: convidar, aprovar pedidos, expulsar, banir, silenciar, editar perfil, gerenciar cargos, atribuir cargos, ver histórico.

## Dados

Tabelas `social.groups` (+ nome único, sigla, privacidade, política de entrada, cor, emblema, limite), `social.group_members` (+ `role_id`, `muted_until`), `social.group_roles`, `social.group_bans`, `social.group_join_requests`, `social.group_audit`.
A migração `Phase44GroupsCore` cria os quatro cargos iniciais para grupos antigos e liga cada membro ao cargo correspondente; grupos antigos viram privados/por convite.

## API (`/api/v1/groups`)

`GET /permissions`, `/mine`, `/search`; `POST /`; `GET|PUT /{id}`; `POST|DELETE /{id}/join`; pedidos (`/requests`, `approve`, `deny`); `POST /{id}/invites`; membros (`/members`, `kick`, `ban`, `mute`, `PUT role`); `/bans`; `/roles` (CRUD); `/audit`; `POST /leave`, `/transfer`, `/dissolve`.
Transferir liderança e dissolver exigem digitar o nome exato do grupo. Dissolver é bloqueado enquanto o grupo tiver terrenos, revoga permissões de ilha do grupo, cancela convites/pedidos, avisa os membros e mantém o histórico.
Notificações: `group_invite`, `group_join_request`, `group_request_decided`, `group_kicked`, `group_banned`, `group_role_changed`, `group_leader_changed`, `group_dissolved`.

## Cliente

Menu Comunicar → **Grupos e organizações…** (`GroupsWindow`): abas Meus grupos / Procurar / Criar; no grupo: Membros, Cargos, Pedidos e banidos, Histórico, Ajustes (inclui liderança e dissolução).

## Limitações conhecidas da Fase 1

- O silenciamento é registrado e exibido, mas só tem efeito quando o chat do grupo existir (Fase 2).
- A dissolução só checa terrenos; tesouraria/lojas entram nas fases 3–4.
- Imagem do grupo: a API aceita URL https; ainda não há envio de arquivo na interface.
- Testes contra PostgreSQL real continuam opcionais (sem credenciais aqui).
