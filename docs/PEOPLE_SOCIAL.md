# Pessoas, amigos e grupos

Janela **Pessoas** (botão na barra inferior ou menu): três abas.

- **Buscar**: procura jogadores por parte do nome (mínimo 2 letras, até 20 resultados, nomes que começam com o texto vêm primeiro). Cada resultado mostra a relação: amigo, pedido enviado, quer ser seu amigo. Quem bloqueou você, quem você bloqueou, contas apagadas e suspensas não aparecem.
- **Amigos**: pedidos recebidos e enviados (aceitar, recusar ou cancelar) e a lista de amigos (remover).
- **Grupos**: convites (entrar ou recusar), seus grupos com número de membros e papel, sair do grupo e criar grupo. O dono não pode sair do próprio grupo.

Novos eventos chegam em tempo real pelo WebSocket de notificações (mensagem privada, pedido de amizade, amizade aceita, convite de grupo) e aparecem como aviso na tela. "Mensagens privadas" continua no menu.

## API (todas exigem login)

| Método e caminho | O que faz |
|---|---|
| `GET /api/v1/social/people?q=` | busca de jogadores com a relação de cada um |
| `GET /api/v1/social/summary` | contadores: pedidos de amizade, convites de grupo, mensagens não lidas |
| `GET /api/v1/social/friends/requests` | pedidos pendentes (`direction`: `in` ou `out`) |
| `POST /api/v1/social/friends/{userId}/decline` | recusa um pedido recebido ou cancela um enviado |
| `GET /api/v1/social/groups` | meus grupos |
| `GET /api/v1/social/groups/invites` | convites pendentes para mim |
| `POST /api/v1/social/groups/invites/{inviteId}/accept` ou `/decline` | responde a um convite |
| `POST /api/v1/social/groups/{groupId}/leave` | sai do grupo (409 para o dono) |

Os endpoints que já existiam (`/friends`, `/friends/requests` POST, `/friends/{id}/accept`, `/groups` POST, `/groups/{id}/invites`) continuam valendo. Perfil público: `GET /api/v1/users/{id}/profile`. Não há migração de banco: as regras novas usam as tabelas atuais.

## Testes

`PeopleQueriesTests` (16 casos, banco em memória) cobre busca, bloqueios, relações, pedidos, convites e saída de grupo; `client/Tests/PeopleSmoke` confere a janela com respostas simuladas (roda em `scripts/godot-smoke.sh`).

Limites conhecidos: não há busca de grupos públicos nem tela de perfil na janela (o endpoint de perfil existe); o aviso em tempo real só chega se o destinatário estiver na mesma instância da API.
