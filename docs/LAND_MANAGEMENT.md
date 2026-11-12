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
- **Visitar** leva até a ilha do terreno. **Comprar** e **Alugar** pedem confirmação, mandam o preço que a pessoa viu (se mudou, nada é cobrado) e usam o mesmo caminho de dinheiro do resto dos terrenos. Aluguel mostra o total e a caução.
- **Fica de fora da lista:** terreno seu, venda exclusiva para outra pessoa e qualquer coisa em ilha privada.

API: `GET /api/v1/discovery/market` devolve `{ total, page, pageSize, items }` (o `/discovery/properties` antigo continua igual). Código novo: `LandMarket` (servidor) e `RealEstateMarketWindow` (cliente). Testes: `LandMarketTests`, `RealEstateMarketSmoke`.

Limites: “Visitar” entra na ilha, não teleporta até o terreno. Ilhas aparecem aqui só se forem públicas.

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

Limites: o plano de hospedagem da plataforma (`economy.rentals`) não é mostrado aqui; “Visitar” entra na ilha, não teleporta até o terreno.

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
