# Permissões de itens (Copiar, Modificar, Transferir, Revender)

Quem decide é sempre o servidor (`ItemRightsService`). O jogo só mostra o que o servidor responde; nenhuma verificação vive só na interface.

## Fase 1 — o núcleo

**Os direitos** (`shared/Items/ItemRights.cs`) são quatro bits: Copiar 1, Modificar 2, Transferir 4, Revender 8. Revender sem Transferir não existe (some ao normalizar); bits desconhecidos são descartados. Um conjunto não pode dar mais do que tem (`Clamp`, `IsWithin`).

**Três conjuntos por item** (`ItemPermissions`): o do dono atual, o do grupo com quem o item é compartilhado, e o do **próximo dono** (quem compra ou recebe). O do grupo e o do próximo dono têm de caber dentro do conjunto do dono. Ao chegar ao próximo dono, ele passa a ter o conjunto "próximo dono", o compartilhamento com grupo some e o que ele repassa não passa disso (`ForNextOwner`).

**Herança** (`Intersect`, `ItemPermissions.Compose`, `ItemRightsService.ComposeAsync`): algo feito de partes (objeto com filhos, objetos agrupados, pacote) só tem o que **todas** as partes permitem. Uma parte não copiável torna o conjunto não copiável.

**Item não copiável:** só se move (de um dono para outro), nunca se duplica. A regra de mover sem duplicar é da fase 2; a fase 1 já guarda e decide `IsCopyable`.

**Regras por tipo de conteúdo** (`ContentRules`): o código-fonte de um **script** só pode ser lido/editado por quem tem Modificar; **modelos, texturas, animações, materiais e sons** só podem ser baixados como arquivo por quem tem Copiar; pôr qualquer um deles dentro de um objeto que pode mudar de mãos exige Transferir (o conteúdo vai junto). A categoria do ativo define o tipo (`FromCategory`). Essas regras são aplicadas nos endpoints de download, de scripts e de construção nas fases seguintes.

**No banco** (migração `Phase52ItemRights`): `inventory.items` ganhou `owner_rights`, `group_rights`, `next_owner_rights`, `group_id`, `copied_from_item_id`, `show_owner_history`, `status` (`active` ou `offered`) e `revision` (token de concorrência). Itens que já existiam ficam com **todos os direitos** (nada era restrito antes) e cada um ganha a primeira linha do histórico (`legacy`). Novas tabelas: `inventory.item_owners` (histórico de donos) e `inventory.item_audit` (trilha de auditoria). Nenhuma das duas tem chave estrangeira para o item, para sobreviver à remoção dele ou da conta.

**Serviço** (`backend/Modules/Inventory/ItemRightsService.cs`):
- `HeldAsync` / `CanAsync`: o dono tem o conjunto do dono; membro do grupo do item tem o do grupo (limitado ao do dono); qualquer outra pessoa nada. Item removido ou "oferecido" (esperando resposta) não pode ser usado por ninguém.
- `RequireAsync`: carrega o item e confere o direito. O que não é da pessoa **não existe** para ela (`not_found`); o que é dela mas sem o direito dá `not_allowed` e **fica na auditoria** como `denied`.
- `SetPermissionsAsync`: só o dono altera. Pode abrir mão de direitos (e o que repassa acompanha), mas só recupera direitos que não tem se também for quem **criou** o ativo e ainda o tiver. Grupo e próximo dono acima do dono são recusados. Uma mudança feita sobre uma versão velha (`expectedRevision`) é recusada; duas mudanças simultâneas: só uma vale (testado com 20 pedidos ao mesmo tempo num PostgreSQL real).
- `ShareWithGroupAsync`: só com grupo do qual o dono é membro; parar de compartilhar zera o conjunto do grupo.
- Histórico (`HistoryAsync`, `SetHistoryVisibilityAsync`): sempre gravado; o dono vê tudo; membro do grupo só vê se o dono permitiu, e então os nomes **intermediários** ficam ocultos ("proprietário anterior"): aparecem o criador, o dono atual e as linhas da própria pessoa.
- Auditoria (`Audit`): grava no mesmo salvamento da mudança que descreve (ou as duas coisas acontecem, ou nenhuma). Também registra o que foi recusado.

**API** (`/api/v1/inventory/items/{id}`): `GET rights`, `PUT rights` (`owner`, `group`, `nextOwner`, `expectedRevision`), `PUT group`, `PUT history-visibility`, `GET history`. Erros: 404 (não é seu), 409 (`stale`, `item_busy`), 400 (`cannot_raise_rights`, `exceeds_owner`, `not_in_group`).

**Testes:** `ItemRightsModelTests` e `ItemRightsServiceTests` (modelo, herança, quem tem o quê, recusas com marca na auditoria, mudança desatualizada, histórico) e `ItemRightsPostgresTests` (padrões do banco para itens "antigos"; 20 mudanças simultâneas, uma só vence).

## Fase 2 — cópia, presente e transferência

**Cópia** (`POST /api/v1/inventory/items/{id}/copy`, `ItemTransfers.CopyAsync`): exige o direito **Copiar** (antes bastava ser dono do ativo). Quem copia sendo o dono recebe uma cópia com os mesmos direitos; um membro do grupo recebe uma cópia com os direitos de "próximo dono". A cópia guarda de onde veio (`copied_from_item_id`), entra no histórico como `copy` e na auditoria. Item sem Copiar, de outra pessoa ou em oferta é recusado (e a tentativa fica na auditoria). Limite de 20.000 itens por pessoa.

**Oferta** (`POST /api/v1/inventory/items/{id}/offers`): presente (`gift`) ou transferência (`transfer`), com mensagem de até 200 letras. Só o dono, com o direito **Transferir**; o destinatário precisa existir, estar ativo e não haver bloqueio em nenhum sentido (nesse caso responde como se a pessoa não existisse). A mesma `idempotencyKey` devolve a mesma oferta; reusar a chave para outra coisa é `key_reused`. Máximo de 20 ofertas pendentes enviadas por pessoa e 50 recebidas. A oferta vale **7 dias**.

**O item fica trancado** (`status = offered`) enquanto espera a resposta: ninguém o usa, copia, renomeia, apaga, equipa, coloca no mundo nem oferece de novo (os endpoints antigos de inventário só enxergam itens `active`). Uma segunda oferta do mesmo item é barrada por um índice único e pelo token de versão. Com `keepCopy` (só para quem pode Copiar) o item **não** é trancado: quem dá fica com o seu e o destinatário receberá uma cópia.

**Aceitar** (`POST /offers/{id}/accept`): é uma única alteração do mesmo registro. O item muda de dono, passa a ter os direitos de "próximo dono" (se isso tirar Copiar, o novo dono não consegue copiar), perde o compartilhamento com grupo, a pasta e o favorito, volta a `active`, e o histórico e a auditoria são gravados no mesmo salvamento. Com `keepCopy`, nasce **exatamente uma** cópia. Antes de mover, confere que o item ainda é de quem ofereceu, não foi removido e (no caso da cópia) ainda pode ser copiado; senão a oferta é cancelada (`item_unavailable`). Aceitar de novo devolve o mesmo resultado sem repetir nada. Só o destinatário aceita ou recusa; só quem enviou cancela.

**Recusar e cancelar** liberam o item. **Expirar:** o `ItemOfferWorker` (a cada 5 minutos) e a listagem fecham ofertas vencidas e devolvem os itens; aceitar uma vencida dá `offer_expired`.

**Corridas e duplicação.** `inventory.items.revision` e `inventory.item_offers.revision` são tokens de concorrência: duas respostas ao mesmo tempo não se aplicam juntas (a perdedora recebe `stale` e nada muda). Testado num PostgreSQL de verdade com 20 pedidos simultâneos: 20 aceites/cancelamentos da mesma transferência → o item existe uma vez e está com um só dono; 20 aceites de oferta com cópia → uma cópia; 20 ofertas do mesmo item → uma ganha e 19 recebem `item_busy`; 18 aceites de 3 ofertas de cópia → três cópias, nem uma a mais.

**Endpoints:** `GET /api/v1/inventory/offers?box=incoming|outgoing&pending=true`, `POST …/offers/{id}/accept|reject|cancel`. Erros: 404 não é seu, 403 `not_allowed`/`keep_copy_needs_copy`, 409 `stale`/`item_busy`/`offer_closed`/`offer_expired`/`item_unavailable`/`inventory_full`, 429 limites.

**Testes:** `ItemTransferTests` (18) e 4 testes novos em `ItemRightsPostgresTests`.

**Ainda aberto de propósito:** colocar um item no mundo (`/place`) ainda não consome nem confere os direitos do item (fase 4); a revenda e o preço entram na fase 3.

## Ainda não (próximas fases)
3: marketplace e economia; 4: construção e comandos multiplayer (inclui colocar/pegar itens no mundo); 5: cliente; 6: testes finais de exploração.
