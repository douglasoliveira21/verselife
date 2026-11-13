# Gerenciamento de Terrenos (janela da equipe da ilha)

Abre em **Mundo → Gerenciar terrenos…**. Só o dono e os administradores da ilha (o servidor recusa os outros com 403).

## O que existe

| Seção | O que mostra / faz |
|---|---|
| **Visão Geral** | Seis indicadores (área total, parcelada, disponível, parcelas vendidas, alugadas, receita mensal), o mapa dos terrenos e os detalhes do terreno escolhido |
| **Mapa de Parcelas** | O mesmo mapa com camadas (grade, rótulos, imagem da ilha), zoom (roda do mouse), mover (botão direito ou do meio), clique escolhe um terreno (Ctrl/Shift soma) |
| **Demarcação** | Marcar quadrados de 4 × 4 m (Demarcar/Reduzir, desfazer/refazer), ver área, dimensões, conflitos e cota de prims, **Criar parcela**, **Dividir**, **Unir** |
| **Lista de Terrenos** | Tabela com busca e filtros (disponíveis, vendidos, alugados, à venda, comuns, residenciais, comerciais) |
| **Vendas** | Anúncios ativos (criar, mudar preço, retirar) e histórico de vendas |
| **Aluguéis** | Ofertas (criar, retirar) e contratos em andamento (encerrar) |
| **Contratos** | Aluguéis em andamento e os encerrados nos últimos 90 dias |
| **Prims e Recursos** | Limite de objetos da ilha, quanto foi repartido, quanto está em uso e o uso de cada terreno |
| **Permissões** | Acesso e construção de cada terreno; “Configurar” abre a janela “Sobre o terreno” |
| **Histórico** | Mudanças e movimentos de dinheiro dos terrenos (os 200 mais recentes) |

Cores do mapa: verde disponível, laranja à venda, roxo alugado, cinza vendido, azul área comum.

## Como é calculado (tudo no servidor, `LandOverview`)

- **Disponível**: terreno do dono da ilha, sem aluguel e sem oferta de venda. **Vendido**: terreno de outra pessoa. **Alugado**: tem contrato ativo ou em tolerância. **À venda**: com preço. **Área comum**: finalidade “comum”.
- **Área livre** = área da ilha − soma das áreas dos terrenos.
- **Receita mensal**: aluguéis ativos cujo locador é o dono da ilha, convertidos para 30 dias.
- **Código** (L-01, L-02…): dado na primeira leitura, na ordem de criação, e nunca reaproveitado.
- **Finalidade** (residencial, comercial, mista, social, comum): nova coluna `category`; o dono do terreno e a equipe podem mudar.

## API

- `GET  /api/v1/worlds/islands/{id}/land/overview` — equipe da ilha.
- `PUT  /api/v1/worlds/islands/{id}/land/parcels/{parcelId}/category` — dono do terreno ou equipe.
- Venda, aluguel, dividir, unir e criar usam os endpoints que já existiam (`/land/...`).
- Migração: `Phase76ParcelCategoryAndCode` (`parcels.category`, `parcels.code`).

## Limites desta entrega

- **Renovar** aluguel é do locatário (o servidor só aceita dele); a tela do locador não tem esse botão.
- **Inspecionar** e **Configurar** abrem a janela “Sobre o terreno” que já existia; o redesenho dela, o Mercado Imobiliário, Minhas Propriedades e a venda de ilhas inteiras são as próximas fases.
- A imagem dos detalhes é a capa da ilha (terrenos ainda não têm imagem própria).
- Vender, alugar, dividir e unir valem para terrenos **do próprio usuário**; num terreno de outra pessoa a equipe só muda a finalidade.
- A API de produção precisa ser publicada (migração incluída) para a janela funcionar.

---

# Mercado Imobiliário

Abre em **Mundo → Mercado imobiliário…**. Mostra terrenos à venda ou para alugar nas **ilhas públicas**.

- **Abas:** Ilhas à Venda, Ilhas para Alugar, Homesteads, Terrenos, Comerciais, Residenciais e Todos. Cada aba acerta os filtros (tipo, negociação, região, categoria).
- **Filtros:** busca (nome, código ou ilha), negociação (venda/aluguel), categoria, área e preço mínimos e máximos, “Somente disponíveis” (desligado, lista também o que já está alugado, marcado “Alugado”) e “Com visitação” (só terrenos de acesso público e com teleporte).
- **Ordenar por:** mais recentes, menor preço, maior preço, maior área, mais prims. **Grade ou lista**, com **páginas** de 9.
- **Cada card:** imagem (capa da ilha quando existe), código e nome, tipo, ilha, selo (À Venda, Para Alugar, Alugado), área, prims e preço (aluguel mostra “/ mensal”, “/ semanal” ou “/ N dias”).
- **Visitar** leva até o terreno (entra na ilha e teleporta). **Comprar** e **Alugar** pedem confirmação, mandam o preço que a pessoa viu (se mudou, nada é cobrado) e usam o mesmo caminho de dinheiro do resto dos terrenos. Aluguel mostra o total e a caução.
- **Fica de fora da lista:** terreno seu, venda exclusiva para outra pessoa e qualquer coisa em ilha privada.

API: `GET /api/v1/discovery/market` devolve `{ total, page, pageSize, items }` (o `/discovery/properties` antigo continua igual). Código novo: `LandMarket` (servidor) e `RealEstateMarketWindow` (cliente). Testes: `LandMarketTests`, `RealEstateMarketSmoke`.

Limites: “Visitar” em terreno teleporta até ele (veja “Visitar até o terreno”). Ilhas aparecem aqui só se forem públicas.

---

# Minhas Propriedades

Abre em **Eu → Minhas propriedades…**. Mostra, com dados reais do servidor (`GET /api/v1/properties/mine`), tudo o que a pessoa tem no mercado imobiliário.

| Aba | O que mostra | Botões |
|---|---|---|
| **Minhas Ilhas** | As ilhas que a pessoa criou: estado, prims usados/limite, visitantes/capacidade, terrenos | Gerenciar (abre o gerenciamento da ilha), Visitar, Anunciar ou Retirar anúncio (venda ou aluguel da ilha inteira) |
| **Meus Terrenos** | Terrenos seus em todas as ilhas: estado, área, finalidade, prims, oferta, locatário | Visitar, Gerenciar (abre o gerenciamento de terrenos), Anunciar ou Retirar anúncio |
| **Meus Aluguéis** | Terrenos seus alugados a outras pessoas | Ver Contrato, Encerrar (só depois do vencimento; o servidor confere) |
| **Propriedades Alugadas** | Terrenos que a pessoa aluga, com o tempo que resta | Visitar, Renovar, Ver Contrato, Sair |
| **Meus Anúncios** | Vendas e aluguéis oferecidos agora | Editar, Retirar |
| **Pagamentos** | Compras, vendas, aluguéis pagos e recebidos, caução; filtro e totais | Recibo (com botão Copiar) |
| **Contratos** | Aluguéis em que a pessoa é locadora ou locatária; filtro ativos/encerrados | — |

**Resumo da Conta Imobiliária** (embaixo): *Imóveis Totais* (ilhas + terrenos seus + terrenos que aluga), *Em Uso* (ilhas online + terrenos seus que não estão alugados a outros + os que aluga), *Alugados* (terrenos seus com contrato ativo), *Receita Mensal* (aluguéis ativos em que a pessoa é locadora, convertidos para 30 dias) e *Próximos Vencimentos* (contratos ativos, dos dois lados, que vencem em 7 dias).

Só entram em Pagamentos os registros de dinheiro (compra e venda, início e pagamento de aluguel, devolução de caução). Não há taxas no jogo, e o recibo diz isso.

Código: `LandProperties` (servidor), `MyPropertiesWindow` e `PropertiesData` (cliente). Testes: `LandPropertiesTests`, `MyPropertiesSmoke`. Sem migração nova.

Limites: “Visitar” em terreno teleporta até ele; o plano de hospedagem aparece no cartão da ilha.

---

# Venda e aluguel de ilhas inteiras

Uma ilha inteira entra no mercado como o terreno, com regras próprias (`IslandCommerce`, rotas em `/api/v1/worlds/islands/{id}/real-estate`).

**Onde se faz:** aba **Propriedade** do gerenciamento da ilha (dono, ID, classe da região, tamanho, terrenos, situação, histórico e os botões) e o botão **Anunciar** de **Minhas Propriedades**. O mercado mostra as ilhas nas abas **Ilhas à Venda**, **Ilhas para Alugar** e **Homesteads**.

**Classe da região:** deduzida do tamanho: 256 m ou mais é *Full Region*; menor, *Homestead*. Não há campo novo para isso.

## Venda
- Só o dono põe à venda: preço de 1 a 1.000.000.000, venda pública ou exclusiva para uma pessoa, descrição e se os objetos dele ficam com a ilha. A ilha de boas-vindas do jogo nunca é vendida. Venda e aluguel se excluem; não vende alugada.
- **Compra** (uma só gravação: carteiras, livro-razão e titularidade): o comprador manda o preço que viu (se mudou, nada é cobrado); recusas: saldo insuficiente, carteira bloqueada, venda para outra pessoa, ilha já vendida, chave repetida (cobra uma vez só).
- **O que muda de dono:** a ilha, **todos os terrenos que eram do vendedor** (os de outras pessoas continuam delas), e os objetos do vendedor, se a venda os incluía; senão eles voltam para o inventário do vendedor (nada é apagado). Ofertas dos terrenos que mudaram de dono são retiradas; **aluguéis em andamento continuam** e o aluguel passa a ir para o novo dono do terreno.
- **Transferir Propriedade:** presente, para uma pessoa pelo nome, sem dinheiro. Não vale com a ilha à venda ou alugada.

## Aluguel
- O dono oferece (valor por período de 1 a 365 dias, mínimo e máximo de períodos, caução, dias de tolerância). Quem aluga paga os períodos ao dono e a caução vai para a custódia.
- **A titularidade nunca muda.** O locatário recebe a permissão de **administrador** até o fim do contrato e passa a poder mexer no que é “só do dono” (reinício, terreno, ambiente, mídia, física, dados gerais, modo de acesso). Dar ou tirar o cargo de administrador continua do dono.
- **Renovar** (só o locatário), **sair** (o locatário, a qualquer hora) e **encerrar** (o dono, só depois do vencimento). Ao terminar: a caução volta, o locatário perde as permissões e os objetos dele na ilha voltam para o inventário dele.
- **Vencimento sem ninguém online:** o trabalhador de aluguéis (já existente) leva o contrato à tolerância e depois o encerra. Não há renovação automática de ilhas.
- Enquanto alugada, a ilha não pode ser vendida nem transferida.

## Dados
Migração `Phase77IslandSalesAndLeases`: colunas de venda e aluguel em `world.islands` e as tabelas `world.island_leases` e `world.island_transactions`. Auditoria da ilha registra oferta, retirada, venda, transferência, aluguel e fim do aluguel. Testes: `IslandCommerceTests` (14), mercado e Minhas Propriedades com ilhas, `IslandPropertySmoke`.

---

# Sobre este Terreno

A janela de **um terreno**. Abre com **botão direito no chão → Sobre este Terreno** (num terreno de alguém), e pelos botões **Inspecionar** e **Configurar** do Gerenciamento de Terrenos. A janela antiga “Sobre o terreno” continua para o que ela faz com **seleção**: marcar área livre, criar, dividir e unir (menu Mundo → Selecionar terreno, ou botão direito em chão sem dono).

Cabeçalho: imagem, **código e nome**, ilha e área, e o estado (Disponível, À Venda, Para Alugar, Alugado, Área Comum). Oito abas:

| Aba | O que mostra | Quem muda |
|---|---|---|
| **Geral** | imagem de divulgação, nome, descrição, área, categoria, ilha, proprietário, prims totais/usadas/livres com barra, ponto de chegada (**Usar Minha Posição**) e modo de teleporte | o dono (a equipe da ilha só muda finalidade e imagem) |
| **Propriedade** | proprietário, tipo de titularidade (pessoa ou grupo), grupo, data de aquisição, direitos de revenda e locação, **histórico de transferência** | só leitura |
| **Venda e Aluguel** | dono: colocar à venda, editar preço, retirar, habilitar aluguel, retirar oferta. Visitante: **Comprar**, **Alugar**, **Ver Condições** | dono; visitantes compram e alugam |
| **Acesso** | modo (público, só a lista, privado) e lista de pessoas (permitir, banir, permitir construir, com duração) | o dono |
| **Permissões** | quem constrói, executar scripts, devolução automática de objetos de quem não pode construir | o dono |
| **Objetos** | prims em uso, lista de objetos, **devolver selecionados** ou **não autorizados** (com motivo) | o dono |
| **Mídia** | música do terreno: endereço, nome, volume, tocar sozinha | o dono |
| **Contrato** | aluguel em andamento (locador, locatário, valor, periodicidade, início, vencimento, situação, caução, renovação) com **Renovar**, **Sair** ou **Encerrar**; sem contrato, as condições da oferta | locatário e dono, pelas regras do servidor |

**Servidor:** `GET /land/parcels/{id}/about` junta tudo isto (qualquer um que possa entrar na ilha lê; o que é só do dono vem com bandeiras `canManage`, `isTenant`, `isStaff`), `PUT /land/parcels/{id}/image` põe ou tira a imagem (dono do terreno ou equipe; precisa ser **imagem do inventário da própria pessoa**). A imagem do terreno é entregue a quem pode entrar na ilha, como a capa. Migração `Phase78ParcelImage` (`parcels.image_url`). Código: `LandAbout` e `ParcelInfoWindow`. Testes: `LandAboutTests` e `ParcelInfoSmoke`.

**Limites:** “Criar objetos” e “Permitir visitantes” do prompt não existem como regras separadas no jogo (voz e mídia ganharam regra própria, veja “Voz e mídia por terreno”); ficaram em **Permissões** as regras que existem (quem constrói, scripts, devolução automática) e o acesso na aba **Acesso**. O mercado e Minhas Propriedades mostram essa imagem nos cards (veja a seção seguinte).

---

# Abas do gerenciamento da ilha: Terrenos e Parcelas, Vendas e Aluguéis

No menu da janela **Informações e Gerenciamento da Ilha**, depois de Histórico e Auditoria: **Terrenos e Parcelas**, **Vendas e Aluguéis** e **Propriedade**. As duas primeiras leem o mesmo `GET /land/overview` do Gerenciamento de Terrenos (só o dono e os administradores).

- **Terrenos e Parcelas:** área total, parcelada e disponível (com porcentagem), quantidade de parcelas, vendidas, alugadas e disponíveis, um **mapa territorial** pequeno com as parcelas de verdade (sem clique), como a ilha está dividida (à venda agora, áreas comuns, prims da ilha, distribuídos e em uso) e o botão **Abrir Gerenciamento de Terrenos**.
- **Vendas e Aluguéis:** anúncios ativos, parcelas à venda e alugadas, contratos ativos, receita mensal, próximos vencimentos (7 dias) e a tabela dos **contratos em andamento** (imóvel, locador, locatário, valor, vencimento, estado). Botões: **Gerenciar Vendas** e **Gerenciar Aluguéis** abrem o Gerenciamento de Terrenos nessas seções; **Criar Anúncio** abre Vendas e já pergunta qual terreno anunciar.

Sem migração nem rota nova. Testes: `IslandLandPagesSmoke`.

---

# Imagem do terreno no mercado e em Minhas Propriedades

A imagem de divulgação que o dono põe na aba **Geral** de “Sobre este Terreno” agora aparece nos **cards do Mercado Imobiliário** e nos cards de **Meus Terrenos** (em Minhas Propriedades). Sem imagem, o card usa a **capa da ilha**, e sem ela, o desenho de mapa de antes. O servidor manda `image` nos itens do mercado e `imageUrl` nos terrenos de `properties/mine`; a imagem é entregue a quem pode entrar na ilha (a rota que serve a capa serve também as imagens dos terrenos). Sem migração nova. Testes: `LandMarketTests`, `RealEstateMarketSmoke`, `MyPropertiesSmoke`.

---

# Voz e mídia por terreno

Duas regras novas na aba **Permissões** de “Sobre este Terreno” (só o dono muda; os outros leem). Ambas **ligadas por padrão**: os terrenos que já existem não mudam.

- **Permitir voz neste terreno.** Desligada, **ninguém fala nem ouve** a voz de perto dentro do terreno. Quem manda isso valer é o **servidor da ilha**: ele recebe do `land-sync` a lista dos terrenos com voz desligada (`rules`) e, a cada pacote de voz, **descarta** o áudio de quem fala de dentro de um deles e **não entrega** áudio a quem escuta de dentro de um deles. O cliente não decide nada. Quem está fora continua se ouvindo normalmente.
- **Permitir mídia nos objetos de outras pessoas.** Desligada, quem só constrói no terreno (nem dono, nem locatário, nem administrador da ilha) **não pode pôr mídia** (imagem ou vídeo numa superfície) nos objetos: criar, colar ou editar um objeto para ter mídia volta `media_not_allowed` (403), como já acontece com scripts. O que **já estava lá continua**. O dono, o locatário e a equipe da ilha podem sempre.

Servidor: `PUT /land/parcels/{id}/options` aceita `allowVoice` e `allowMedia` (opcionais: uma requisição que não os manda deixa como estão); `GET …/about` os devolve. Migração `Phase79ParcelVoiceMedia` (`parcels.allow_voice`, `parcels.allow_media`, padrão ligado). Código: `LandRules` (compartilhado), `ServerVoice` (servidor da ilha), `BuildRules.CanPutMedia`. Testes: `LandVoiceMediaTests`, `ParcelInfoSmoke`.

**Publique API e servidor de ilhas juntos:** o servidor de ilhas antigo ignora `rules` e a voz continua liberada em todo lugar.

**Limites:** a regra de mídia vale para quem constrói e não retira mídia que já existia; a música do próprio terreno (aba Mídia) é do dono e não é afetada.

## Aviso de voz desligada na barra de voz

Ao entrar num terreno com a voz desligada, o botão **Falar** da barra de voz vira **Sem voz** e fica apagado, junto com o cadeado. A dica diz “A voz está desligada neste terreno (nome do terreno): quem está dentro não fala nem ouve.” O cliente também **para de capturar e enviar** áudio enquanto a pessoa está lá (o servidor da ilha descartaria de qualquer jeito); ao sair, tudo volta sozinho, e o microfone aberto (cadeado) continua como estava.

Como funciona: a lista de terrenos que o cliente já recebe ganhou `allowVoice`; `LandPresence` guarda se a voz vale onde o avatar está (`VoiceAllowed`, `VoiceOffIn`); a `VoiceBar` pergunta isso a cada quadro (`VoiceRule`) e `VoiceChat.BlockedHere` corta a captura. Testes: `VoiceUiSmoke`, `LandPresenceSmoke`, `LandVoiceMediaTests`. O aviso depende da API nova (sem ela o cliente assume que a voz vale).

## Aviso de mídia desligada no editor de construção

No painel **MÍDIA** da aba Textura do editor, aparece um aviso em laranja quando um objeto selecionado está num terreno **sem mídia para quem só constrói** e a pessoa não é a dona nem a locatária: “O terreno “Casa” não permite mídia nos objetos de quem não é o dono. O servidor vai recusar, a não ser que você seja o locatário ou da equipe da ilha.” O aviso **não trava** o botão Aplicar: o servidor é quem decide (a equipe da ilha pode, e o cliente não sabe quem é da equipe), mas a pessoa já sabe o porquê. Quando o servidor recusa mesmo assim, o editor explica: “Este terreno não permite mídia (imagem ou vídeo) em objetos de quem não é o dono.”

Como funciona: a lista de terrenos do cliente ganhou `allowMedia`; o painel pergunta o terreno de cada objeto selecionado (`BuildMediaPanel.ParcelAt`, ligado ao `LandManager`). Testes: `BuildTextureSmoke`, `LandPresenceSmoke`, `LandVoiceMediaTests`.

## Avisos e renovação automática (fase 80)

- **Avisos (`PropertyNotice`)**: o servidor grava um aviso quando um terreno é vendido ou alugado, quando uma renovação automática falha, quando o aluguel entra em tolerância ou termina (para quem aluga e para quem recebe) e, via `PropertyNotices.ScanAsync` (chamado pelo `LandRentalWorker`), quando falta pouco para o vencimento. `GET /api/v1/properties/notices` lista e `POST /api/v1/properties/notices/read` marca como lidos.
- **Cliente**: "Minhas Propriedades" consulta os avisos ao abrir e a cada 5 minutos; mostra uma faixa com "Ver Avisos" / "Marcar como Lidos" e avisa uma única vez (toast) de cada aviso novo.
- **Renovação automática de ilhas alugadas**: `Island.RentAutoRenewAllowed` (quem anuncia permite) e `IslandLease.AutoRenew` (quem aluga escolhe). No vencimento o servidor cobra um período; se faltar saldo, avisa e segue o fluxo de tolerância. Migração: `Phase80PropertyNoticesAndLeaseRenewal`.
- **Cota de objetos ao demarcar**: o diálogo "Criar parcela" aceita um limite de objetos (0 = proporcional à área); o servidor recusa com `budget_exceeded` se a ilha não tiver tanto livre.

### Ainda não feito (e por quê)

- **Retenção de snapshots**: apaga dados; precisa de decisão do dono do produto sobre prazos.
- **Pagamento em duas fases (reservar/liquidar)**: deliberadamente não feito; as operações usam chaves idempotentes no livro-razão.
- **Testes de concorrência em PostgreSQL real**: exigem credenciais; os testes de Postgres ficam ignorados sem elas.
- **"Criar objetos" / "Permitir visitantes"**: redundantes com as permissões de construção e o modo de acesso existentes.

Produção: publicar API, servidor de ilha e jogo juntos, com as migrações Phase65–Phase83.

## Expandir e reduzir parcela

- `POST /islands/{id}/land/parcels/{parcelId}/expand` (`cells`, `extraObjects`): junta quadrados livres que encostam na parcela; o limite de objetos sobe em `extraObjects` (nunca além do que a ilha tem livre). Erros: `overlap`, `not_contiguous`, `outside_island`, `too_big`, `budget_exceeded`, `bad_limit`, `rented`, `forbidden`.
- `POST .../parcels/{parcelId}/shrink` (`cells`): devolve quadrados à ilha. A parte que fica precisa ser de uma peça só, não pode ficar vazia e nenhum objeto pode estar nos quadrados devolvidos (`has_objects`). O limite de objetos não muda; o ponto de nascimento é movido se cair fora.
- Nos dois casos os anúncios de venda/aluguel da parcela são retirados e a mudança entra no histórico (`expand` / `shrink`). Parcelas alugadas não mudam de forma.
- Cliente: na aba Demarcação, marque os quadrados e use **Expandir** (livres, com a parcela escolhida) ou **Reduzir** (dentro da parcela).

## Hospedagem da ilha nas janelas

- O plano da plataforma que mantém a ilha no ar (`IslandRental` + `IslandRentalPlan`, comprado em `economy.rentals`) aparece como `hosting` (`plan`, `status` = `active` ou `recovery`, `expiresAt`, `recoveryUntil`, `maxPlayers`, `maxObjects`; `null` sem plano) em `GET /properties/mine` (cada ilha) e na visão da ilha de `real-estate` (só para o dono, que é quem paga).
- **Minhas Propriedades**: o cartão da ilha mostra "Hospedagem: plano, até data" ou "Hospedagem vencida: renove até data". **Propriedade** (gerenciamento da ilha): linha "Hospedagem" com plano, vencimento, pessoas e objetos.
- A compra e a renovação do plano agora também se fazem na aba Propriedade (veja “Gerenciar Hospedagem”).

## Atualização ao vivo das janelas

- `LiveTicker` (cliente) é o relógio: só anda com a janela aberta e nunca dispara com um diálogo (venda, aluguel, edição) aberto por cima, para não atrapalhar quem está digitando.
- **Gerenciamento de Terrenos**: a cada 15 s pergunta só `GET /land/version` (um número, que muda quando uma parcela é criada, removida, oferecida, alugada ou remodelada); se mudou, recarrega a visão geral. Se não mudou, não baixa mais nada.
- **Mercado Imobiliário** (30 s): repete a busca atual. **Sobre este Terreno** (20 s): relê o terreno. **Minhas Propriedades** (30 s): relê as propriedades e os avisos.
- A atualização é silenciosa: não mostra "Carregando…" e, se a conexão falhar, a janela fica como estava (sem apagar a lista).
- Limite conhecido: a versão da terra só acompanha parcelas; mudanças só na ilha (anúncio de venda da ilha, por exemplo) aparecem nas janelas que relêem tudo (Minhas Propriedades, Mercado), não no Gerenciamento de Terrenos.

## Visitar até o terreno

- "Visitar" em um terreno (Minhas Propriedades: terrenos e contratos de terreno; Mercado Imobiliário: anúncios de terreno) leva ao **terreno**, não só à ilha. Ilhas continuam indo para a ilha.
- Na mesma ilha, o avatar pede o teleporte ao terreno na hora (`LandManager.TeleportToParcel`). Em outra ilha, o cliente entra nela e guarda o terreno em `SearchTravel.SetParcel`; quando o avatar aparece lá, pede o teleporte (o mesmo caminho da busca global, que guarda um ponto em vez de um terreno).
- As regras do servidor da ilha continuam valendo: terreno que não aceita teleporte ou sem acesso responde com a mensagem de sempre ("Você não pode entrar nesse terreno."). Quem pede outra ilha no meio do caminho esquece o terreno guardado.
- **Sobre este Terreno** tem o botão **Visitar** no cabeçalho (vale para todas as abas). Terreno privado fica desligado para quem não é dono, locatário ou da administração; terreno que não aceita teleporte leva só até a ilha, com um aviso.

## Regras da ilha (Editar Regras)

- São as **regras de casa** da ilha: como um terreno novo começa. Campos: entrada (`accessMode`: livre, lista ou privado), quem constrói (`buildMode`: dono, lista, todos, ninguém), scripts, voz, mídia e devolução automática de objetos de estranhos (minutos, ou desligada). Ficam em `Island.RulesJson` (migração `Phase81IslandRules`); sem nada salvo valem as regras simples (livre, só o dono, sem scripts, com voz e mídia).
- `PUT /islands/{id}/land/rules` (dono e administradores da ilha). Valores inválidos voltam `bad_mode` / `bad_duration`; quem não é da administração recebe `forbidden`.
- **Terrenos novos** (demarcados com "Criar parcela") começam com essas regras, a menos que o pedido traga permissões próprias. **Terrenos que já existem** não mudam sozinhos: marcando "Aplicar também aos terrenos que ainda são do dono da ilha e não estão alugados", as regras são postas só nesses terrenos (cada um ganha uma linha `island_rules` no histórico e a versão da terra sobe). Terrenos de outras pessoas e alugados nunca são mudados por aqui.
- `GET real-estate` traz `rules` (qualquer pessoa lê) e `canEditRules` (só a administração). Na aba **Propriedade** a linha "Regras da ilha" resume tudo e o botão **Editar Regras** abre o diálogo.
- O dono de cada terreno continua mudando as regras do seu terreno em "Sobre este Terreno".

## Transferir propriedade da ilha

A transferência (presente: a ilha, os terrenos do dono e os objetos dele passam a outra pessoa, sem dinheiro) já existia; esta fase fechou o que faltava:

- **Hospedagem acompanha a ilha**: o plano da plataforma ativo (`IslandRental`) passa para a nova pessoa, tanto na transferência quanto na venda. Antes o dono antigo continuava como pagante e podia renovar um plano de uma ilha que já não era dele.
- **Avisos** (Minhas Propriedades): quem recebe vê "Você recebeu a ilha …" e quem entrega vê "Você transferiu a ilha …".
- **Confirmação mais segura**: o diálogo diz o nome da ilha, quantos terrenos e que a hospedagem vai junto, e só libera "Transferir" depois de digitar o nome da ilha.
- Continua valendo: só o dono transfere; não com a ilha à venda, alugada, nem a ilha de boas-vindas; a pessoa precisa existir; ficam registros no histórico da ilha e na auditoria.

## Aceitar a transferência da ilha

A transferência deixou de ser imediata: agora é uma **oferta que a outra pessoa precisa aceitar**.

- **Oferecer** (`POST real-estate/transfer {name}`): grava `Island.TransferToUserId` e `TransferExpiresAt` (7 dias; migração `Phase82IslandTransferOffer`). Nada muda de dono ainda. A pessoa recebe um aviso. Vale as mesmas regras de antes (só o dono, não à venda, não alugada, não a ilha de boas-vindas).
- **Enquanto a oferta espera**: a ilha continua do dono, mas não pode ser anunciada para venda nem aluguel (`transfer_pending`) nem ser oferecida a outra pessoa; o dono pode **cancelar** (`DELETE real-estate/transfer`).
- **Aceitar** (`POST .../transfer/accept`) só vale para a pessoa escolhida e antes do prazo (`transfer_expired` depois). É só então que a ilha, os terrenos do dono, os objetos e o plano de hospedagem mudam de dono, com registro no histórico e na auditoria e avisos para os dois. **Recusar** (`.../transfer/decline`) devolve a ilha ao estado normal e avisa o dono.
- Uma oferta vencida deixa de contar sozinha (o dono pode vender de novo); ninguém precisa limpar nada.
- **Cliente**: Minhas Propriedades ganhou a seção **Ilhas Oferecidas a Você** (Aceitar com confirmação, Recusar) e mostra no cartão da ilha para quem ofereceu "Oferecida a … de presente, espera a resposta". Na aba **Propriedade**, o dono vê o pedido pendente e **Cancelar Transferência**; quem recebeu, se puder abrir a ilha, também vê Aceitar Ilha / Recusar. `GET properties/mine` traz `incomingTransfers`.

## Transferir terreno entre pessoas

Dar um terreno a outra pessoa segue o mesmo caminho da ilha: uma **oferta que a pessoa precisa aceitar** (migração `Phase83ParcelTransferOffer`: `IslandParcel.TransferToUserId` e `TransferExpiresAt`, 7 dias).

- `POST land/parcels/{id}/transfer {name}` oferece; `DELETE` cancela (dono); `POST .../transfer/accept` e `.../transfer/decline` são da pessoa escolhida. Erros: `transfer_pending`, `no_transfer`, `transfer_expired`, `for_sale`, `rented`, `common_area`, `group_owned`, `person_not_found`, `bad_buyer`.
- **Só o dono do terreno** oferece (nem o dono da ilha oferece terreno de outra pessoa). Não vale para área comum, terreno de grupo, à venda ou alugado.
- **Ao aceitar** o terreno muda de dono junto com os **objetos que o dono antigo deixou nele** (os de outras pessoas ficam delas; os de fora do terreno não mudam). As ofertas de venda e aluguel do terreno somem. Os dois recebem aviso; entra no histórico (`transfer_offered`, `transferred`…).
- **Enquanto a oferta espera**: o terreno não pode ser anunciado, dividido, unido, expandido nem reduzido (`transfer_pending`). Vencida, a oferta deixa de contar sozinha. Se a ilha inteira mudar de dono, as ofertas de terreno do dono antigo caem.
- O código de mover os objetos passou a ser o mesmo da compra (`MoveObjectsAsync`).
- **Cliente**: em **Sobre este Terreno**, aba de venda, o cartão **Presente** tem Transferir Terreno, a oferta pendente com Cancelar Transferência e, para quem recebeu, Aceitar Terreno / Recusar. **Minhas Propriedades** mostra "Ofertas de Presente" (ilhas e terrenos juntos) com Aceitar/Recusar e, no cartão do terreno, "Oferecido a … de presente".

## Gerenciar Hospedagem

- **Aba Propriedade → Gerenciar Hospedagem** (dono e administração): mostra o plano da ilha (nome, o que inclui, vencimento ou recuperação), o saldo do dono e, para o dono, **Renovar Plano** ou a lista de planos para **Contratar**. A administração só vê.
- `GET real-estate/hosting` (estado, planos, saldo), `POST .../hosting/subscribe {planId, idempotencyKey}`, `POST .../hosting/renew {idempotencyKey}` (`IslandHosting`). Só o **dono** paga (quem aluga a ilha não).
- **Regras do dinheiro**: a mesma chave não cobra duas vezes; um plano por vez (`already_hosted` → renove o que tem); saldo e carteira bloqueada são conferidos; a cobrança vai da carteira do dono para a conta `platform_revenue` numa transação balanceada do livro-razão; não se paga mais de um ano à frente (`too_far`).
- **Ciclo**: `active` (em vigor), `recovery` (a semana depois do vencimento, ainda dá para renovar) e `ended` (precisa escolher um plano de novo, o que também permite trocar de plano). Renovar soma o período depois do fim (ou a partir de hoje, se já venceu), então renovar antes não perde dias.
- Os endpoints antigos `economy/islands/{id}/rent` e `economy/rentals/{id}/renew` passaram a usar o mesmo código: antes não conferiam a chave repetida nem se já havia plano, e uma dupla clicada cobrava duas vezes.
- Não há tarefa que desligue a ilha quando o plano acaba: hoje o estado é informativo (e aparece em Minhas Propriedades).
