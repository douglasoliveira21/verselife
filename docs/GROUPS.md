# Grupos e Organizações 2.0

Plano em 5 fases. Este documento descreve o que existe hoje (**Fases 1 a 4**) e o que falta.

| Fase | Conteúdo | Estado |
|---|---|---|
| 1 | Núcleo: criação, identidade, cargos e permissões, convites, pedidos, público/privado, moderação, histórico, transferência de liderança, dissolução, anti-abuso, janela de administração | feito |
| 2 | Comunicação: chat de grupo (respeitando silenciamento), anúncios, eventos exclusivos, notificações | feito |
| 3 | Propriedades: terrenos do grupo, construção compartilhada, administração coletiva, acesso por grupo | feito |
| 4 | Economia: tesouraria, contribuições voluntárias, lojas do grupo, marketplace, auditoria financeira | feito |
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

## Fase 2 – Comunicação

- **Chat do grupo** (`/chat`): só membros leem e escrevem; quem está silenciado não escreve (`muted`); até 1000 caracteres, 20 mensagens por 30 s por pessoa, `clientMessageId` evita duplicar. Cada pessoa apaga as próprias mensagens; quem tem `moderate_chat` apaga as de cargo menor (fica no histórico como `message_deleted`). As mensagens chegam ao vivo pelo WebSocket de notificações (`group_message`). As rotas antigas `/social/messages` com `groupId` agora também exigem ser membro (leitura) e não estar silenciado (envio).
- **Anúncios** (`/announcements`, permissão `announce`): título até 80, texto até 2000, até 3 fixados, 10 por hora por grupo. Todos os membros recebem aviso.
- **Eventos exclusivos** (`/events`, permissão `manage_events`): são `discovery.events` com `group_id`, nunca públicos; só membros veem, entram e saem; capacidade até 5000, duração até 7 dias, até 20 futuros por grupo, a ilha precisa ser uma onde quem cria pode entrar. Sair, ser expulso/banido ou o grupo acabar tira a pessoa dos eventos; cancelar avisa quem tinha vaga. A rota pública de entrar em evento recusa eventos de grupo para quem não é membro.
- **Notificações**: anúncios e eventos viram `social.group_notifications` (guardadas até ler; lidas há mais de 30 dias somem) além do aviso ao vivo. `GET /notifications`, `POST /notifications/read`, `PUT /{id}/notification-settings` (silenciar os avisos de um grupo). Eventos ao vivo novos: `group_message`, `group_announcement`, `group_event`, `group_event_cancelled`; todos os eventos de grupo agora aparecem como aviso na tela.
- **Permissões novas**: `announce`, `manage_events`, `moderate_chat` (Administrador tem as três, Moderador tem `moderate_chat`; a migração `Phase45GroupsCommunication` acrescenta aos grupos que já existem).
- **Cliente**: no grupo, abas Chat, Anúncios e Eventos; no topo, aba Avisos. Mensagens de outros grupos contam como "nova(s)" na lista.

## Fase 3 – Propriedades

- **Terreno do grupo**: um terreno (`IslandParcel`) com `GroupId`. Fica registrado em nome do **líder** (`OwnerUserId`), para que toda regra que olha "o dono" continue valendo; ao passar a liderança, os terrenos passam junto (`group_steward` no histórico do terreno). Quem manda de verdade é o cargo, conferido na hora: o líder antigo que sai do cargo não conserva poder.
- **Colocar e tirar**: `POST /groups/{id}/lands {parcelId}` (permissão `manage_land`; o terreno tem de ser seu, sem aluguel; encerra ofertas de venda/aluguel; até 50 por grupo). `POST /groups/{id}/lands/{parcelId}/release {userId}` (permissão `release_land`, só o líder por padrão): devolve a um membro; não líder faz no máximo 3 por dia; nunca com aluguel ativo; os acessos de outros grupos acabam. `GET /groups/{id}/lands` e `/lands/available`.
- **Administração coletiva**: quem tem `manage_land` faz tudo que o dono faria (modo de acesso, banir/permitir, opções, dividir e juntar, objetos, retorno de objetos, ambiente, música, histórico). A verificação é central (`LandAuthority.CanManageAsync`). Dividir mantém o grupo; juntar nunca mistura terreno de grupo com pessoal (`mixed_owners`). Terreno de grupo não é vendido nem alugado por uma pessoa só (`group_owned`); a tesouraria da Fase 4 cuidará disso.
- **Construção compartilhada**: `build_on_land` deixa construir no terreno do grupo (mesmo no modo "só o dono"); gerentes (`manage_land`) editam e devolvem objetos de qualquer um, colocam scripts, e editam o terreno (terraformação). Tudo é calculado na hora a partir dos membros e cargos atuais: sair, ser expulso ou perder o cargo tira o direito na mesma hora. Objetos continuam sendo de quem os criou.
- **Acesso por grupo**: qualquer dono (ou gerente) abre um terreno a todos os membros de outro grupo: `allow` (entra, no modo por lista) ou `build` (também constrói). Rotas `/worlds/islands/{islandId}/land/parcels/{parcelId}/group-access` (GET, POST, DELETE). Grupo privado só é visível a quem é membro; até 20 por terreno; banimento do terreno vence. Um terreno privado de grupo continua aberto aos membros do grupo; os servidores de ilha recebem a lista (`land-sync`).
- **Dissolução**: continua bloqueada enquanto o grupo tem terrenos (`owns_land`): devolva-os antes.
- **Permissões novas**: `manage_land`, `build_on_land`, `release_land` (Administrador tem as duas primeiras; o líder todas; a migração `Phase46GroupsLand` ajusta grupos existentes e cria `world.parcel_group_access`).
- **Cliente**: aba **Terrenos** no grupo (lista, colocar um terreno seu, devolver a um membro, abrir o terreno a outro grupo). O histórico do grupo ganhou as frases novas.

## Fase 4 – Economia

- **Cofre (tesouraria)**: `social.group_treasuries` (saldo com controle de concorrência, totais, congelado) e `social.group_ledger`, o extrato que nunca é editado (contribuição, venda, retirada, com saldo depois, quem fez, para quem, motivo). O saldo só mexe junto com uma linha do extrato, com a carteira e com o razão da plataforma (`LedgerTransaction`/`LedgerEntry`, conta `group_treasury`), que continuam fechando em zero.
- **Contribuições voluntárias** (`POST /groups/{id}/treasury/contribute`): qualquer membro; 1 a 1 bilhão de moedas; recado até 120; chave de idempotência (a mesma chave é o mesmo presente); até 20 por hora; carteira bloqueada ou sem saldo recusam. É definitivo: sair do grupo não devolve.
- **Retiradas** (`POST /treasury/withdraw`, permissão `manage_treasury`, só o líder por padrão): exige motivo (3 a 200), só para um **membro**, chave de idempotência. Quem não é líder: no máximo 10 por dia e no máximo 20% do cofre por dia; o líder é avisado de cada retirada. O líder pode **congelar** o cofre (`PUT /treasury/frozen`): congelado, só ele retira; contribuições continuam entrando.
- **Quem vê**: todos veem quanto deram; `view_treasury` (e `manage_treasury`) vê saldo, totais, maiores contribuintes e o extrato (`GET /treasury`, `/treasury/ledger`). Cada movimento também vai ao histórico administrativo (`treasury_contribution`, `treasury_withdrawal`, `treasury_frozen`…).
- **Produtos do grupo no mercado** (`/groups/{id}/products`, permissão `manage_store`): `marketplace.products.group_id`. O ativo é de quem publica (precisa ser dono), mas o dinheiro vai para o cofre: na compra (`EconomyEndpoints.PurchaseAsync`) o valor líquido (preço menos a taxa de 5%) vira uma linha `sale` do extrato e a conta do cofre no razão; o publicador não recebe nada pessoalmente. Preço e dados de um produto de grupo só mudam pela rota do grupo (a rota pessoal recusa); até 100 ativos por grupo. Quem publica ainda pode desativar o seu pelo mercado.
- **Lojas do grupo** (`/groups/{id}/stores`, `world_stores.group_id`): vendem um produto do grupo em terreno do grupo (ou em ilha de quem cria); até 50. Retirar o produto fecha as lojas dele.
- **Dissolução**: bloqueada com saldo no cofre (`treasury_not_empty`) além dos terrenos; ao dissolver, produtos saem do mercado e lojas fecham.
- **Permissões novas**: `view_treasury`, `manage_treasury`, `manage_store` (Administrador: ver o cofre e as lojas; líder: tudo; a migração `Phase47GroupsEconomy` ajusta grupos existentes).
- **Cliente**: abas **Cofre** (saldo, extrato, contribuir, retirar, congelar) e **Loja** (produtos, preço, lojas nas ilhas). As abas do grupo agora rolam na horizontal.

## Limitações conhecidas

- O chat do grupo guarda só texto; não há anexos, reações ou respostas.
- Anúncios e eventos de grupo não aparecem fora do grupo (busca pública, mapa).
- Abrir um terreno **pessoal** a um grupo só tem API por enquanto; a janela de terrenos de uma pessoa ganha isso na Fase 5. Pela janela do grupo, abre-se terrenos do próprio grupo.
- Terrenos do grupo ainda não são comprados, vendidos nem alugados com o cofre: a venda/aluguel de terreno de grupo segue bloqueada (`group_owned`).
- Vender o terreno ou outros itens do grupo e pagar membros por dividendos não existe; as retiradas são manuais, com motivo e limite.
- Os produtos de um grupo guardam o ativo de quem publicou; se essa pessoa apagar o ativo, o produto deixa de funcionar.
- Imagem do grupo: a API aceita URL https; ainda não há envio de arquivo na interface.
- Testes contra PostgreSQL real continuam opcionais (sem credenciais aqui).
