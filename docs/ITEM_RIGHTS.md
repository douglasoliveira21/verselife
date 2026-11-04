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

## Fase 3 — marketplace e economia

**Produto do criador.** Ao publicar (`POST /api/v1/economy/products`) o criador diz o que o **comprador** recebe (`buyerRights`) e o que o comprador poderá **repassar** (`nextOwnerRights`, que precisa caber dentro do primeiro). Sem dizer, vale o padrão Copiar·Modificar·Transferir; o texto antigo `permissionsJson` (`{"copy":true,…}`) ainda é lido. "Nenhum direito" (0) é uma escolha válida. O produto grava os direitos e a compra os entrega: o item do comprador nasce com `OwnerRights = buyerRights` e `NextOwnerRights = nextOwnerRights`, e o histórico e a auditoria registram a compra. Alterar os direitos de um produto (`PATCH`) vale **só para quem comprar depois** e fica na auditoria (`product_rights`). Produtos que já existiam ficam com todos os direitos.

**Gratuito e pago.** Preço 0 é um produto gratuito: nenhuma moeda se move e **cada pessoa o leva uma única vez** (senão seria um jeito de encher inventários). Produto pago pode ser comprado de novo. A mesma chave de idempotência dá o mesmo pedido; a chave de outra pessoa é recusada (`key_reused`).

**Presente.** `PurchaseRequest.RecipientUserId` compra para outra pessoa: o item vai direto para o inventário dela (histórico `gift`), se o produto permitir (`allowGift`, padrão sim), se ela existir, estiver ativa e não houver bloqueio em nenhum sentido, e não for o vendedor.

**Revenda.** `PublishProductRequest.ItemId` coloca à venda **um item que a pessoa tem**, que precisa ter **Transferir e Revender**; o preço é de quem vende e os direitos do comprador são os de "próximo dono" do item.
- Item **não copiável**: fica trancado (`listed`) — não pode ser usado, oferecido nem copiado —, e a compra **move o próprio item** para o comprador; o produto vira `sold`. Na hora da compra confere-se de novo que o item ainda é de quem vende e ainda está à venda; senão, `listing_unavailable` e nenhuma moeda se move.
- Item **copiável**: não é trancado; cada compra faz uma cópia e quem vende fica com o seu. Só uma listagem ativa por item.
- Tirar da venda (`deactivate`) devolve o item. Os direitos de uma revenda são os do item e não se editam (só o preço e o texto).
- Quem recebeu só revende se o direito Revender chegou até ele, e a cadeia respeita isso em cada passo.

**Mercado mostra os direitos:** a listagem traz `buyerRights`, `rights` (`CMT-`), `rightsText`, `nextRightsText`, `isResale` e `allowGift`.

**Pagamento e entrega andam juntos.** Primeiro se confere carteira e se o item pode ser entregue; só depois as moedas se movem, tudo na mesma transação. Se alguém chegou antes (a última unidade, as mesmas moedas, a mesma conta), o resultado é `409 stale` e **nada** foi cobrado nem entregue. Testado em PostgreSQL de verdade: 20 compradores simultâneos do único exemplar de uma revenda → um pedido, um dono, um pagamento.

**Correção da fase 1 achada aqui.** Os valores padrão do banco (15) só existem para linhas escritas antes dos direitos, mas o EF tratava o valor `0` como "não informado" e gravaria 15: um item com **nenhum direito** viraria **todos os direitos** no PostgreSQL. Corrigido (`HasSentinel(-1)` nos itens; sem padrão no modelo dos produtos, com o padrão só na migração) e coberto por um teste no PostgreSQL de verdade. Os testes em memória não pegam isso porque o banco em memória não aplica valores padrão.

**Migração** `Phase54MarketplaceRights`: `products` ganha `buyer_rights`, `buyer_next_rights`, `source_item_id`, `allow_gift`; `orders` ganha `recipient_user_id`.

**Testes:** `MarketplaceRightsTests` (17) e 2 novos em `ItemRightsPostgresTests`.

**Ainda aberto:** vendas de grupo usam os direitos padrão (a tela de grupo não escolhe ainda); lojas dentro do mundo vendem pelo mesmo caminho e já respeitam tudo isto.

## Fase 4 — construção e comandos multiplayer

Todo comando de construção dos jogadores passa pela API (a ilha só repassa), então estas regras valem para a interface, para o servidor de ilha e para qualquer cliente.

**Direitos do objeto.** Cada objeto do mundo tem `owner_rights` e `next_owner_rights` (colunas novas em `world.objects`; objetos que já existiam ficam com todos). **Herança dos componentes:** um objeto não pode mais do que aquilo de que é feito — o modelo 3D, as imagens do material e as coisas do conteúdo. Para cada parte que a pessoa não fez ela mesma, vale o direito do item que ela tem (o mais amplo, se tiver vários); o mais restrito vence. Ao criar, e ao acrescentar partes depois, os direitos só **diminuem**. O texto `permissionsJson` do objeto não mudou: continua sendo "o que os outros podem fazer" (legado, sem efeito); os direitos reais ficam nas colunas.

**Filhos e grupos** (`ObjectRights.EffectiveAsync`): os direitos de um objeto são cortados pelos de todos os objetos de que ele é parte (filho de algo que não pode ser modificado também não pode, até 16 níveis). Uma construção salva ou devolvida (`ForPackage`, `PackageRightsAsync`) tem só o que todos os seus objetos permitem.

**Modificar** (`update`): mover e girar nunca exigem Modificar; mudar o que o objeto **é** (formato, tamanho, material, física, conteúdo, nome, descrição, pai, grupo) exige, **para qualquer pessoa**, inclusive administrador da ilha ou dono do terreno (eles podem mover, devolver e apagar, mas não reconstruir o que não pode ser modificado). O JSON é comparado pelo sentido, então reenviar as mesmas configurações escritas de outro jeito não conta como mudança. Recusa: `403 no_modify`. **Scripts:** criar ou trocar o código de um script só em objeto que pode ser modificado.

**Coisa que não pode ser copiada fica em um objeto só** (modelo 3D e conteúdos; as imagens de um material podem aparecer em vários lugares): criar ou editar um segundo objeto com a mesma coisa dá `409 no_copy_in_use`; devolver o primeiro libera. Item nessa situação **não pode ser dado nem vendido** enquanto estiver em um objeto (`item_in_use`), senão sobraria uma cópia no mundo.

**Colocar do inventário no servidor** (`rez`, comando novo, também no servidor de ilha): o servidor lê a construção do inventário e cria **todos os objetos ou nenhum**, com as mesmas verificações de um `create` (onde a pessoa pode construir, scripts, limites, o que ela tem). Os objetos recebem os direitos do item. Item **sem Copiar** é consumido ao colocar (`status = consumed`; não volta pela lixeira) e reaparece, com os mesmos direitos, quando os objetos são devolvidos; o ciclo colocar-devolver-colocar sempre dá um só. Item copiável continua no inventário. Dois `rez` do mesmo item ao mesmo tempo: só um vale (testado no PostgreSQL de verdade, 20 pedidos). O posicionamento soma a origem às posições salvas (que já são relativas ao centro da construção).

**Devolver** (`return`, também usado quando uma locação acaba): a construção devolvida ao dono ganha os direitos do conjunto. **Ler a construção inteira** pelo endpoint do inventário exige Copiar (`403 use_rez` caso contrário) e **`/place`** (colocação antiga de ativo solto) recusa item não copiável (`409 use_rez`).

**Direitos de um objeto** (`GET/PUT /api/v1/worlds/islands/{islandId}/objects/{objectId}/rights`): o dono vê (o próprio conjunto e o que repassa) e pode **diminuir** os dois; subir é recusado (`cannot_raise_rights`, fica na auditoria); versão desatualizada dá `revision_conflict`.

**Migração** `Phase55ObjectRights`.

**Achado fora do escopo, ainda não corrigido:** `MeshAssets.IslandAssetAsync` (arquivos de modelos e imagens usados na ilha) procura o id com `ToLower().Contains(...)` em colunas `jsonb`, que o PostgreSQL não aceita (`lower(jsonb)`). Pode estar quebrando esse download em produção; o código novo desta fase evita o problema. Merece correção própria.

**Testes:** `ObjectRightsTests` (21) e 1 teste novo no PostgreSQL real; o teste de revenda concorrente da fase 3 ajudou a achar o problema do `jsonb`.

## Fase 5 — cliente

O jogo só **mostra e pede**; todas as regras continuam sendo do servidor, e cada recusa dele é explicada em palavras (`ItemRightsApi.Explain`).

**Inventário.** O menu do item ganhou "Direitos e propriedades…", "Oferecer a alguém…" e "Fazer uma cópia" (só se o item puder ser copiado). A dica de cada item mostra os direitos ("Direitos: Copiar · Modificar") e avisa quando está em oferta ou à venda. A lista do inventário (`GET /api/v1/inventory/items`) agora traz `ownerRights`, `groupRights`, `nextOwnerRights` e `status`, e não mostra itens usados ao colocar uma construção.

**Janela "Direitos do item"** (`ItemRightsWindow`): o que você pode fazer; uma grade Copiar / Modificar / Transferir / Revender para você, para o grupo e para o próximo dono (só o dono edita, o grupo só se o item estiver compartilhado; salvar manda a versão lida, então mudança em cima de leitura velha é recusada); opção de deixar quem tem o item ver o histórico de donos e a lista do histórico; **dar a alguém** (presente ou transferência, mensagem, "ficar com o meu e dar uma cópia" só para item copiável) — a pessoa é achada pelo **nome de usuário exato** pela busca global, nunca por nome parcial; **revender** (preço, 0 = grátis) só com Transferir e Revender; **fazer uma cópia**. Quem só recebe o item por um grupo vê os próprios direitos e nenhum botão de entregar.

**Janela "Ofertas de itens"** (menu Mundo): recebidas (aceitar / recusar) e enviadas (cancelar), com mensagem e prazo. O resultado de cada resposta continua na tela depois de a lista recarregar; oferta expirada é explicada e nada muda.

**Editor de construção.** O texto da seleção mostra os direitos do que está selecionado (só o que todos os objetos permitem) e avisa "só mover e devolver (sem direito de modificar)". Mensagens novas para `no_modify`, `no_copy_in_use`, `item_busy`, `use_rez` e `invalid_package`. **Colocar do inventário:** se o servidor responde `use_rez` ao abrir a construção (item que não pode ser copiado), o editor manda o comando `rez`; os objetos chegam todos de uma vez, ficam selecionados e o aviso diz que o item foi usado. Não há "desfazer" para isso (desfazer apagaria o que o item virou); "Devolver" manda de volta ao inventário. Os objetos agora trazem `ownerRights` e `nextOwnerRights` na resposta da API.

**Mercado.** A lista ganhou uma coluna de direitos (`CMT-`, e "revenda"), e ao escolher um produto aparece "Você receberá: … Poderá passar adiante: …" (e se não aceita presente). Erros de compra (saldo, já pegou o grátis, presente não permitido, venda indisponível) saem em palavras.

**Servidor.** `rez` soma a altura de base da construção ao colocar, como o cliente faz; mudar só a física "vazia" para a física padrão não conta como modificar.

**Testes:** `ItemRightsSmoke` (janelas contra um servidor de mentira: leitura, edição com versão, recusa de subir direitos, histórico, oferta com busca exata, revenda, cópia, quem não é dono, ofertas recebidas/enviadas, oferta expirada) e novas verificações no `BuildSmoke` (direitos da seleção, mensagens, comando e chegada do `rez`).

**Ainda aberto:** compartilhar um item com um grupo (a escolha do grupo ainda não tem tela; o servidor já aceita), comprar de presente pela tela do mercado (o servidor já aceita), editar os direitos de um objeto do mundo (o servidor já aceita), e escolher os direitos de um produto ao publicar.

## Fase 6 — exploração, concorrência e duplicação

Esta fase foi escrita para **tentar quebrar** as cinco anteriores. O que encontrou, e foi corrigido:

1. **Item em oferta ou à venda podia entrar num objeto.** A checagem de "você tem esse modelo/script?" só olhava se o item existia; um item reservado (`offered`/`listed`) contava. Aceita a oferta, o objeto ficava com a peça e o novo dono também. Agora só item `active` vale (`MeshAssets`, `MaterialAssets`, `ContentAssets`).
2. **Corrida entre a checagem e a mudança.** Oferecer/listar/copiar conferiam os direitos numa leitura e alteravam o item numa segunda leitura; entre as duas, o item podia ser **usado ao colocar uma construção** e a oferta o "ressuscitava", duplicando a construção (o teste de caos pegou em cerca de 1 rodada em 4). Agora a segunda leitura é comparada com a versão da primeira (`item_busy` se mudou) e copiar toca no original para que copiar e qualquer mudança dele se revezem.
3. **Erro 500 em compras simultâneas.** Duas compras criando a mesma conta contábil, ou o PostgreSQL desfazendo uma de duas transações que disputam as mesmas linhas, escapavam como exceção. Agora `RaceFailures.IsRace` reconhece conflito de versão, linha duplicada, deadlock e falha de serialização, olhando a cadeia inteira de exceções, e a resposta é `409 stale` ("tente de novo"), sem cobrar nada.
4. **Preço sem teto.** O preço × 5% podia estourar. Teto de 1 trilhão de moedas (`MaxPriceCoins`).
5. **Arquivos de modelo/imagem da ilha** (`MeshAssets.IslandAssetAsync`) procuravam o id com `lower()` em colunas `jsonb`, que o PostgreSQL não aceita: o download falhava. Agora usa o texto da coluna no PostgreSQL (com teste no banco real).

**Testes de exploração** (`ItemRightsExploitTests`, e novos em `ObjectRightsTests`): direitos nunca crescem numa cadeia de donos (todas as formas de pedir mais, inclusive `int.MaxValue` e negativos); o criador que vendeu o item não recupera direitos; uma revenda não promete ao comprador mais do que o item repassa; só o criador vende cópias novas de um ativo; ofertas respondidas pela pessoa errada, reutilizadas, aceitas depois de canceladas ou canceladas depois de aceitas; duas contas ligadas não duplicam um item; ninguém lê, muda, oferece, copia, compartilha ou lista o item de outra pessoa; item apagado não se oferece; cada recusa deixa **uma** linha na auditoria e cada mudança de dono deixa histórico e auditoria no mesmo instante; item usado ao colocar não volta da lixeira; item reservado não responde aos endpoints antigos (renomear, apagar, vestir, colocar); preço absurdo; servir arquivo da ilha só a quem pode entrar e só do que ela usa.

**Testes de caos no PostgreSQL de verdade** (`ItemRightsChaosTests`): 16 pessoas fazendo 150 operações cada, ao acaso e ao mesmo tempo — oferecer, aceitar, recusar, cancelar, pôr à venda, comprar, tirar da venda, tentar copiar, baixar direitos. No fim: nenhum pedido terminou em erro inesperado; cada peça existe **uma** vez; o estado do item bate com as ofertas e vendas abertas; cada troca de dono tem sua linha de histórico e o último dono do histórico é o dono; nenhuma carteira negativa e **nenhuma moeda criada ou destruída** (o que saiu das carteiras são as taxas). Um segundo teste faz 10 pessoas colocarem, devolverem (inteiro ou em partes) e passarem adiante uma construção de 3 objetos sem direito de copiar: objetos no mundo + objetos nas construções do inventário somam sempre 3.

## Resumo para quem opera
- **Auditoria:** `inventory.item_audit` (tudo que foi feito e tudo que foi recusado, com quem, de quem para quem e o motivo) e `inventory.item_owners` (quem teve cada item). Não têm chave estrangeira para o item, então sobrevivem à remoção dele ou da conta. Exemplos: recusas de um usuário — `select * from inventory.item_audit where actor_user_id = '…' and outcome = 'denied' order by at desc`; caminho de um item — `select * from inventory.item_owners where item_id = '…' order by at`.
- **Ofertas** vencem em 7 dias (o `ItemOfferWorker` roda a cada 5 minutos e devolve os itens); no máximo 20 ofertas pendentes enviadas e 50 recebidas por pessoa.
- **Estados do item** (`inventory.items.status`): `active`, `offered` (esperando resposta), `listed` (à venda como revenda) e `consumed` (usado ao colocar uma construção; volta como item novo quando os objetos são devolvidos).
- **Migrações do recurso:** `Phase52ItemRights`, `Phase53ItemOffers`, `Phase54MarketplaceRights`, `Phase55ObjectRights`.

## Limites conhecidos
- Objetos e construções feitos **só de formas simples** (cubos, esferas…) não têm nada a proteger: qualquer um que veja o objeto vê suas formas e pode refazê-las. Os direitos protegem o que é referenciado por id (modelos, scripts, conteúdos) e o ciclo de vida do item/construção.
- Escolher o grupo de um item, comprar de presente pela tela do mercado, editar os direitos de um objeto do mundo e escolher os direitos de um produto ao publicar existem no servidor, mas ainda não têm tela.
- Texturas (imagens de material) podem aparecer em vários objetos; os objetos herdam os direitos delas, mas elas não são "gastas".
- Itens e objetos anteriores a este recurso têm todos os direitos.
