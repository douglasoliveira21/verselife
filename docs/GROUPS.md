# Grupos e Organizações 2.0

Plano em 5 fases. Este documento descreve o que existe hoje (**as 5 fases**).

| Fase | Conteúdo | Estado |
|---|---|---|
| 1 | Núcleo: criação, identidade, cargos e permissões, convites, pedidos, público/privado, moderação, histórico, transferência de liderança, dissolução, anti-abuso, janela de administração | feito |
| 2 | Comunicação: chat de grupo (respeitando silenciamento), anúncios, eventos exclusivos, notificações | feito |
| 3 | Propriedades: terrenos do grupo, construção compartilhada, administração coletiva, acesso por grupo | feito |
| 4 | Economia: tesouraria, contribuições voluntárias, lojas do grupo, marketplace, auditoria financeira | feito |
| 5 | Interface de administração completa e integração final | feito |

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

## Fase 5 – Interface de administração e integração

A janela **Grupos e organizações** (menu Comunicar) reúne tudo; o que o cargo da pessoa não permite aparece desligado, e o servidor confere de novo.

- **Meus grupos**: lista com contagem de mensagens novas, **convites recebidos** (aceitar/recusar) e, no grupo escolhido, as abas **Painel** (visão geral: cargo, membros, pedidos esperando, anúncios, próximo evento, terrenos, cofre, loja), **Chat**, **Anúncios**, **Eventos**, **Terrenos**, **Cofre**, **Loja**, **Membros**, **Cargos**, **Pedidos** (e banidos), **Histórico** (paginado, "Carregar mais antigos") e **Ajustes** (perfil, liderança, dissolução). As abas rolam na horizontal.
- **Procurar** (com **cancelar meu pedido**), **Criar grupo** e **Avisos** (anúncios e eventos guardados).
- **Identidade**: emblema (12 opções), cor e **imagem** (endereço https; vira miniatura de 96 px na visão do grupo, arquivos de até 2 MB, PNG/JPG/WebP; endereço ou arquivo ruim só deixa a imagem de fora).
- **Terreno pessoal aberto a um grupo**: na janela de terrenos, aba **Acesso → Abrir o terreno a um grupo** (escolhe um dos seus grupos, "pode entrar" ou "pode construir", e retira). Vale também para quem administra um terreno de grupo.
- **Mercado**: a lista de produtos mostra "· nome do grupo" nos produtos vendidos por grupos (`groupName` em `GET /economy/products`).
- **Histórico administrativo** traduz todas as ações das cinco fases.
- **Teste de ponta a ponta** (`GroupLifecycleTests`): um grupo nasce, ganha membros e cargos, conversa, anuncia, recebe terreno e evento, contribui, vende, é impedido de acabar com terreno e moedas, devolve tudo e é dissolvido, com o razão fechando em zero e o histórico guardado.

## Limitações conhecidas

- O chat do grupo guarda só texto; não há anexos, reações ou respostas.
- Anúncios e eventos de grupo não aparecem fora do grupo (busca pública, mapa).
- Terrenos do grupo ainda não são comprados, vendidos nem alugados com o cofre: a venda/aluguel de terreno de grupo segue bloqueada (`group_owned`).
- Vender o terreno ou outros itens do grupo e pagar membros por dividendos não existe; as retiradas são manuais, com motivo e limite.
- Os produtos de um grupo guardam o ativo de quem publicou; se essa pessoa apagar o ativo, o produto deixa de funcionar.
- A imagem do grupo é um endereço https; não há envio de arquivo pelo jogo.
- Testes contra PostgreSQL real continuam opcionais (sem credenciais aqui).

## Grupo ativo, título do cargo e propostas (reconstrução)

Migração `Phase85GroupsActiveAndProposals`.

- **Grupo ativo**: a pessoa usa **um** grupo por vez (`group_members.is_active`). `POST /groups/{id}/activate` (só membro; o anterior deixa de valer), `DELETE /groups/active`, `GET /groups/active` (`ActiveGroupView`: grupo, sigla, título, cor). Sair, ser removido ou a dissolução do grupo encerram o uso sozinhos (a associação é a fonte da verdade). `GET /groups/mine` traz `isActive` e `title` em cada cartão.
- **Título do cargo** (`group_roles.title`, até 24 caracteres): `PUT /groups/{id}/roles/{roleId}/title` (permissão `manage_roles`, mesmas regras de hierarquia; só o líder muda o título do cargo de líder). Vazio = o nome do cargo. A lista de cargos traz `title`.
- **Propostas e votações**: nova permissão `manage_proposals` (líder e administrador; a migração acrescenta aos grupos existentes). `POST|GET /groups/{id}/proposals`, `GET /{pid}`, `POST /{pid}/vote {option}`, `POST /{pid}/close` (encerra e apura), `DELETE /{pid}` (cancela). Uma proposta tem 2 a 8 opções, prazo de 5 minutos a 30 dias, **quem pode votar** (cargo mínimo), **quórum** (% de quem pode votar que precisa votar) e **critério**: `majority` (mais da metade), `two_thirds`, `unanimous` ou `plurality` (a mais votada, sem empate). No máximo 10 abertas por grupo.
- **Regras do voto**: um voto por pessoa, sem trocar (índice único); só membros e só quem tem o cargo mínimo; depois do prazo o voto é recusado e a proposta se encerra. Fecha sozinha no prazo (`GroupProposalWorker`, a cada minuto), quando todos que podem votar votaram, ou por um gerente. Empate ou falta de quórum = `rejected`; ninguém votou = `rejected`. `EligibleAtClose` guarda quantos podiam votar.
- **Visibilidade**: enquanto aberta, os membros veem só **quantos** votaram e o próprio voto; quem tem `manage_proposals` vê também a divisão. Fechada, todos os membros veem a divisão e a opção vencedora.
- **Avisos**: nova proposta (`group_proposal`) e resultado (`group_proposal_result`) viram notificações de grupo (guardadas) e aviso ao vivo; aparecem na central de notificações (aba Grupos).
- Ainda não existe interface no cliente para estas funções, e o título/sigla **ainda não aparece acima do avatar no mundo 3D** (isso exige subir a versão do protocolo de rede e mudar o servidor da ilha: etapa 11 do plano em `GROUPS_REDESIGN.md`).

## Perfil do grupo, taxa de entrada, convites e avisos (reconstrução, migração `Phase86`)

- **Campos novos do grupo** (`social.groups`): `category` (social, construcao, comercio, jogos, educacao, musica, arte, eventos, outros), `language` (`pt-BR`, `en-US`…), `content_rating` (livre, moderado, adulto), `cover_url` (https, até 400), `headquarters_island_id` (uma ilha pública ou do próprio criador; `Guid.Empty` limpa) e `join_fee`. Criar e `PUT /groups/{id}` validam tudo (`bad_category`, `bad_language`, `bad_rating`, `bad_cover`, `bad_headquarters`, `bad_fee`). `GET /groups/mine`, `/search` e `/{id}` trazem os campos. `GET /groups/search?category=&language=&policy=&free=true` filtra.
- **Taxa de entrada**: só em grupo de entrada **livre** (`open`), de 1 a 1.000.000 moedas; deixar de ser livre zera a taxa. `POST /groups/{id}/join {confirmFee}`: sem o valor exato o servidor responde `fee_required` com `{fee}` (a pessoa vê o preço e confirma); saldo (`insufficient_balance`) e carteira bloqueada são conferidos; as moedas vão para o **cofre do grupo** (extrato `join_fee`) **na mesma gravação** que cria o membro, então não há cobrança sem entrada nem entrada sem cobrança. Quem sai e volta paga de novo; um clique duplo não cobra duas vezes (a segunda tentativa vira `already_member`).
- **Convite com mensagem e cargo**: `POST /groups/{id}/invites {userId|username, message, roleId}`. Mensagem até 200 caracteres (aparece na central de notificações); o cargo prometido não pode ser o de líder e, se não for o cargo comum, quem convida precisa de `assign_roles` e de nível acima do cargo (`outranked`). Ao aceitar, a pessoa entra já com esse cargo.
- **Avisos que expiram e com anexo**: `expiresAt` (de 1 minuto a 1 ano; depois disso os membros não veem mais; quem tem `announce` ainda vê) e `attachmentUrl` (link https até 400; não há envio de arquivo). Os itens da lista trazem os dois campos.
- **Notificações guardadas**: pedido decidido, remoção, banimento, mudança de cargo, nova liderança e dissolução agora também ficam guardados em `social.group_notifications` (além do aviso ao vivo); quem estava offline vê na central de notificações, aba Grupos.
