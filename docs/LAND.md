# Terrenos do LifeVerse (sistema de parcelas)

Este documento acompanha as fases do sistema de terrenos pedido no "Prompt master". Aqui está o que **já existe**; o que ainda falta está no fim.

## Fase 1 — Estrutura territorial (entregue)

**O que já existia (Fase 23):** parcelas retangulares (`world.parcels`) com dono, limite de objetos, permissões em JSON, ambiente, ponto de chegada, revisão e histórico (`world.parcel_changes`), usadas pelas regras de construção (`BuildRules`) e pelos limites (`build-limits`). Nada disso foi recriado.

**O que a fase 1 acrescentou**

- **Malha territorial de 4 × 4 m.** A ilha é cortada em células; uma parcela é um conjunto de células que se tocam **pelos lados** (cantos não contam). Pode ser retangular ou irregular. Tabela nova `world.parcel_cells` (migration `Phase34LandCells`) com chave `(ilha, cx, cz)`: duas parcelas **não conseguem** ter a mesma célula nem se duas pessoas tentarem ao mesmo tempo (o banco recusa a segunda).
- **Servidor (`backend/Modules/Land`)**: `LandGrid` (conta de células, vizinhança, contorno), `LandService` (reivindicar células, liberar, listar, uso por parcela), `LandEndpoints`:
  - `GET /api/v1/worlds/islands/{id}/land` — as parcelas da ilha para quem pode entrar: dono, estado (`own`, `owned`, `public`, `restricted`), área, células em linhas compactas `[cz, cx0, cx1]`, objetos e custo usados, limites, versão.
  - `POST /api/v1/worlds/islands/{id}/land/claim` — cria uma parcela a partir de células (ou de um retângulo em metros). Por enquanto só o dono e os administradores da ilha. Recusas: `empty`, `too_big`, `outside_island`, `not_contiguous`, `bad_name`, `name_taken`, `overlap`.
  - O `POST /parcels` antigo agora também passa pela malha (e recusa sobreposição); excluir uma parcela libera as células.
- **Regras de construção seguem a malha.** `ParcelFacts` carrega as células: uma parcela em L **não** reivindica a caixa à sua volta, e o limite de objetos e de custo conta só o que está sobre as células dela (`LandService.UsageAsync`).
- **Parcelas antigas** (desenhadas como retângulo) ganham as células na primeira inicialização (`ParcelCellBackfill`), na ordem em que foram criadas; se duas se sobrepunham, a mais antiga fica com a área comum. Uma parcela que ficar sem nenhuma célula continua valendo pelo retângulo. As células usam o centro de cada quadrado, então a borda de uma parcela antiga pode andar até 2 m.
- **Cliente (`LandManager`)**: ferramenta **Selecionar terreno** (menu Mundo, ou botão direito no chão > Selecionar terreno): arraste sobre o chão e o contorno ciano, a grade e a área aparecem; os terrenos da vizinhança ficam contornados por estado (ciano seu, azul-claro de outra pessoa, cinza público, vermelho restrito) e a terra livre em verde. A janela **Sobre o terreno** (também em botão direito no chão > Sobre o terreno) mostra nome, descrição, área, proprietário, estado, região, localização e limites, e na aba **Objetos** quantos objetos e quanto custo já foram usados. Em terra livre, o campo **Criar terreno** manda o pedido ao servidor, que confere tudo de novo.
- A cor laranja marca terrenos à venda e para alugar (Fase 2).

**Testes:** `LandTests.cs` (cálculo da malha, contorno, um só pedaço, não sobrepor, nomes únicos, liberar, regras de construção com parcela em L, uso por parcela, listagem, preenchimento das antigas, paridade entre servidor e cliente) e `LandSmoke` (cliente).

## Fase 2 — Propriedade: compra, venda e aluguel (entregue)

**Dinheiro.** Usa a carteira e o livro-razão que já existiam (`economy.wallets`, `transactions`, `ledger entries`), sem taxa. Cada ação é uma só gravação: carteiras, livro-razão, parcela e objetos mudam juntos ou não mudam. A revisão da parcela e o saldo da carteira são _concurrency tokens_: duas pessoas comprando a mesma parcela (ou a mesma carteira sendo gasta duas vezes) não vencem as duas; a segunda recebe `conflict` e nada é cobrado. Todo pedido de dinheiro leva uma chave de idempotência: repetir o mesmo pedido não cobra de novo.

**Venda** (`LandCommerce`, endpoints sob `/land/parcels/{id}`):
- `POST …/sale` (só o proprietário): preço de 1 a 1.000.000.000, venda pública ou exclusiva para uma pessoa, descrição, incluir ou não os objetos do vendedor. `DELETE …/sale` retira.
- `POST …/buy`: o comprador manda o preço que viu (`expectedPrice`); se mudou, nada é cobrado (`price_changed`). Recusas: `not_for_sale`, `own_parcel`, `not_for_you`, `rented`, `insufficient_balance`, `wallet_blocked`, `conflict`. Ao comprar, o proprietário muda, a oferta some e, se a venda inclui objetos, os objetos do vendedor dentro das células da parcela passam ao comprador (objetos de visitantes continuam de quem os fez).
- Cada venda vira linha em `world.parcel_transactions`, no histórico de alterações da parcela e no livro-razão (débito = crédito).

**Aluguel**:
- `POST …/rental-offer` (só o proprietário): valor por período, período de 1 a 365 dias, mínimo e máximo de períodos, caução, dias de tolerância. `DELETE …/rental-offer` retira. Uma parcela está à venda **ou** para alugar, nunca os dois, e não pode ser vendida enquanto alugada.
- `POST …/rent`: paga os períodos ao proprietário e a caução vai para uma conta de custódia (`land_escrow`); cria o contrato (`world.parcel_rentals`). Durante o contrato **ativo** o locatário pode construir na parcela (`ParcelFacts.TenantUserId`), mas só mexe nos próprios objetos; o proprietário mantém os poderes dele.
- `POST /land/rentals/{id}/renew`: paga mais períodos a partir do vencimento (ou de agora, se já venceu), inclusive durante a tolerância.
- `POST /land/rentals/{id}/end`: o locatário pode sair quando quiser; o proprietário só depois do vencimento. A caução volta ao locatário.
- **Vencimento sem ninguém online** (`LandRentalWorker`, a cada minuto): contrato vencido com renovação automática ligada cobra mais um período (a mesma data nunca é cobrada duas vezes); sem renovação ou sem saldo vai para **tolerância** (`grace`, estado "pagamento pendente": sem construir); passada a tolerância o contrato **encerra**, a caução é devolvida e os objetos do locatário dentro da parcela voltam ao inventário dele como construção salva ("Devolvido: …"), nada é apagado de vez.
- Estados do contrato: `active`, `grace`, `ended`; a parcela aparece como `rent` (laranja) quando oferecida, e `rented_by_me` para o locatário.

**Cliente**: a janela "Sobre o terreno" ganhou a aba **Venda e aluguel**: o proprietário coloca à venda (preço, incluir objetos) ou oferece para aluguel (valor, período de 7, 14 ou 30 dias, mínimo e máximo, caução) e retira as ofertas; quem vê o terreno de outra pessoa à venda ou para alugar abre a janela de confirmação **Comprar terreno** (nome, área, preço, proprietário, limite de construção, objetos incluídos) ou **Alugar terreno** (períodos, total agora = aluguel + caução, renovação automática); o locatário vê situação, vencimento e próxima cobrança, renova ou encerra; o proprietário e o locatário veem o histórico. Depois de cada operação o mapa e o saldo do topo são atualizados. Terrenos à venda e para alugar aparecem em laranja.

**Limitações conhecidas desta fase**
- Quando a propriedade dos objetos muda (venda) ou eles são devolvidos (fim de aluguel), isso é gravado no banco; quem já está na ilha só vê a mudança ao recarregar os objetos (não há ainda aviso ao servidor de ilha).
- O restante da caução como garantia de danos não existe: a caução sempre volta inteira.
- Venda exclusiva para uma pessoa e venda para um grupo existem na API (`buyerUserId`) mas o cliente ainda não oferece o campo.
- Imagem da propriedade, condições de transferência e regras de devolução configuráveis ficam para depois; as regras de construção do locatário são as de "construir" (sem política própria de scripts ou física).
- Os testes de concorrência usam banco em memória, que não desfaz o que o perdedor já tinha alterado antes do conflito; no PostgreSQL a gravação inteira é desfeita. Falta repetir o cenário contra um PostgreSQL real.

## Fase 3 — Administração (entregue)

Tudo é do **proprietário** da parcela (administradores da ilha só leem a lista de objetos e o histórico); cada mudança é uma gravação só, com linha no histórico da parcela (`world.parcel_changes`).

**Dividir** (`POST /land/parcels/{id}/split`): corta células escolhidas e cria um terreno novo do mesmo dono. As duas partes precisam ficar **de uma peça só**; não vale cortar o terreno inteiro; não vale com aluguel em curso; o nome é único. Os objetos ficam onde estão e com quem os possui. O limite de objetos é repartido pela área, mas nunca abaixo do que cada parte já tem construído (se não couber, `limit_too_low` e nada muda). A lista de acesso e o modo de entrada vão para a parte nova; ofertas de venda e aluguel da parte antiga são retiradas.

**Unir** (`POST /land/merge`): de 2 a 20 parcelas do mesmo dono que se encostam **pelos lados** (canto não vale, `not_adjacent`), sem aluguel em curso. O cliente sempre pergunta de qual terreno valem as configurações (nome, descrição, permissões, ambiente, modo de acesso); a API exige a escolha quando elas diferem (`primary_required`). A área e o limite de objetos são somados, as listas de acesso se juntam, os outros terrenos saem (nome liberado) e os objetos continuam onde estão.

**Acesso** (aba **Acesso**): três modos — público, só a lista de permitidos, privado (só o dono e o locatário). Cada pessoa está em **uma** lista por vez: permitida ou banida, para sempre ou por 15 minutos, 1 hora, 24 horas ou 7 dias. O banimento vale mais que tudo, menos para o dono; o dono da ilha e os administradores não podem ser banidos e sempre entram. **Expulsar** é um banimento de 1 minuto. Até 200 pessoas por lista. Procura por nome usa a mesma busca de contas do menu radial.
- **Quem garante isso é o servidor da ilha, não o cliente.** A cada 10 s o servidor da ilha pede à API (`GET /platform/servers/{id}/land-sync`) os terrenos fechados ou com banimento (`LandAccessMap`, em `shared/Land`). A cada passo da simulação, qualquer jogador parado num lugar proibido é levado ao ponto permitido mais próximo, seja qual for o caminho (andar, voar, teleporte, reconexão, banimento recebido com a pessoa lá dentro). Não há laço de teleporte: o ponto escolhido é sempre permitido.
- Terrenos fechados para você aparecem em vermelho (restrito) no mapa.

**Objetos do terreno** (aba **Objetos**): lista de tudo que está sobre as células da parcela (nome, dono, custo, se tem script, se é de quem está autorizado: dono, locatário e lista de permitidos). Filtros: todos, meus, de terceiros, com scripts. **Devolver selecionados** ou **Devolver não autorizados** exigem um **motivo** (registrado no histórico); os objetos vão para o inventário de quem os fez como construção salva, nada é apagado de vez. A API avisa o servidor da ilha na rodada seguinte (até 10 s): os objetos devolvidos somem para todos, e objetos que mudaram de dono numa venda são atualizados, sem precisar recarregar.

**Histórico administrativo** (aba **Administração**): as últimas 100 operações da parcela (criada, dividida, unida, acesso, expulsões, devoluções, vendas, aluguéis).

**Testes:** `LandAdminTests` (dividir, orçamento, unir, configurações conflitantes, acesso, expiração dos banimentos, objetos, devolução, sincronização com o servidor da ilha), `LandAccessMapTests` (regras de quem é barrado e para onde é levado) e o `LandSmoke` do cliente.

**Limitações conhecidas desta fase**
- Acesso por **grupo** não existe (não há grupos de terreno ainda); só por lista de pessoas.
- A barreira visual opcional nos limites do terreno não foi feita (o jogador simplesmente é levado para fora).
- Veículos com motorista não passam pela verificação de acesso.
- Um banido é mantido fora fisicamente, mas as regras de **construção** ainda não olham a lista de banidos: num terreno com construção pública, o pedido de construir de um banido não é recusado por causa do banimento.
- A devolução e a troca de dono chegam aos jogadores em até 10 s (rodada de sincronização), não na hora.
- O cliente não oferece ainda a lista "Expulsar" a partir do menu radial de uma pessoa nem a localização do objeto no mundo.

## Fase 4 — Construção: orçamento, permissões e retorno automático (entregue)

**Custo de construção mais completo** (`BuildCost.Breakdown`). O custo de um objeto deixou de ser só triângulos: soma a geometria (1 ponto a cada 20 triângulos), a colisão sólida (1, ou 2 para malhas importadas), a **complexidade da colisão** (+1 para forma oca, torcida ou cortada, que precisa de malha própria), texturas, luz e faces coloridas, **materiais transparentes e brilhantes** (+1 cada) e o que o objeto carrega: **script 8**, **animação 3**, outros itens 1. O teto por objeto continua 400. Ao iniciar, a API reprecifica os objetos já existentes pelo modelo novo (só grava o que mudou; `BuildCostBackfill.RepriceAsync`).

**Orçamento do terreno pelas regras da ilha** (`LandBudget`):
- Os terrenos de uma ilha dividem o limite de objetos dela: ao criar, o limite padrão do terreno é a fatia da ilha proporcional à área (mínimo 10) e a soma dos limites dos terrenos **não pode passar** do limite da ilha (`budget_exceeded`). Dividir e unir mantêm a soma.
- A ilha pode definir `parcelCostPerM2` nos limites de recursos: o orçamento de custo de cada terreno passa a ser área × esse valor. Sem a regra vale o antigo (20 pontos por vaga de objeto).
- A janela do terreno mostra objetos e custo usados e disponíveis, e quanto é do dono e quanto é de terceiros.

**Permissões de construção por terreno** (aba **Acesso**, "Quem pode construir"; `PUT /land/parcels/{id}/options`):
- Modos: **só o dono (e o locatário)**, **o dono e a lista de construtores**, **todos**, **ninguém** (só administradores da ilha; bloqueia até o dono). A lista de construtores é a mesma lista de acesso, com o tipo "construtor"; uma pessoa pode estar ao mesmo tempo permitida/banida (entrada) e construtora. Quem está **banido não constrói**, nem onde todos podem.
- **Scripts:** o dono, o locatário e os administradores podem pôr scripts em objetos; os outros só se o terreno liberar ("Permitir scripts…"). A API recusa com `scripts_not_allowed` ao criar um objeto com script ou ao pôr o primeiro script em um objeto.
- As chaves antigas `build` e `run_scripts` de `permissions` acompanham o modo, então tudo que as lia continua igual.
- O editor de construção mostra no cartão "Terreno e limites" quem constrói ali, se há scripts liberados e o retorno automático, e explica a recusa ("banido", "scripts não permitidos").

**Retorno automático** (`LandAutoReturn`): nunca, ou depois de 5, 15, 30 ou 60 minutos (a API aceita de 1 a 10.080). A cada minuto o servidor devolve ao inventário dos donos os objetos que estão no terreno há mais que isso e são de quem **não** está autorizado (dono, locatário, construtores e lista de permitidos nunca são tocados; objetos com `"protected":true` nas permissões também não). Roda com o dono desconectado, registra "retorno automático" no histórico do terreno e os jogadores veem os objetos sumirem em até 10 s.

**Testes:** `BuildCostModelTests` (custos), `LandBuildingTests` (orçamento, os quatro modos, banidos, locatário, lista de construtores separada, opções, scripts, dividir e unir com as regras, retorno automático) e o `LandSmoke` do cliente.

**Limitações conhecidas desta fase**
- Permissões por **grupo** não existem (não há grupos de terreno); só por pessoa.
- Das permissões específicas do pedido, só **criar objetos**, **scripts**, **retorno de objetos de terceiros** e **objetos temporários** (retorno automático) existem de fato; **física**, **colocar veículos** e **compartilhar objetos** não têm sistema por trás ainda.
- A permissão de scripts vale na hora de colocar o script; a execução dos scripts já colocados não é filtrada pelo terreno.
- Não há ainda um botão para marcar um objeto como "protegido"; a regra existe e funciona se a marca estiver nas permissões do objeto.
- O reprecificar roda a cada início da API e percorre todos os objetos; em ilhas enormes pode valer guardar a versão do modelo de custo.

## Fase 5 — Terreno em 3D (entregue)

O editor de terreno que já existia (elevar, abaixar, nivelar, suavizar, rugosidade, pintar, desfazer) foi ligado aos terrenos e ganhou o que faltava. Os detalhes técnicos do mapa estão em [TERRAIN.md](TERRAIN.md).

**Ferramentas** (janela **Editar terreno**, tecla `T` ou menu Mundo): **Elevar**, **Abaixar**, **Nivelar** (leva o solo à altura escolhida, com botão para copiar a altura do ponto sob o cursor), **Achatar** (na altura do ponto clicado), **Suavizar**, **Rugosidade**, **Erosão** (terra de encostas íngremes desliza para baixo; o material só muda de lugar), **Restaurar** (devolve relevo e texturas ao terreno original da ilha) e **Pintar**.
- **Pincel:** redondo ou quadrado, tamanho de 1 a 24 m, intensidade, **suavidade da borda** (100% é o pincel de sempre) e **prévia em tempo real**: antes de clicar, uma superfície translúcida mostra como o solo ficaria (azul onde pode mudar, vermelho onde não pode). A prévia testa a ferramenta no solo local e desfaz tudo, sem mexer no mapa nem na revisão.
- **Desfazer e refazer** como antes, por jogador, no servidor.

**Materiais:** a pintura agora tem **8 camadas** (Grama, Terra, Areia, Rocha, Cascalho, Neve, Lama, Grama seca), com mistura suave entre elas. Mapas antigos (4 camadas) carregam normalmente. A **escala das texturas** é um ajuste do seu computador. Texturas personalizadas importadas **não** existem.

**Quem pode esculpir onde (o servidor decide):**
- Editores da ilha (permissão `edit_terrain`) esculpem em qualquer lugar e são os únicos a mudar o nível do mar e importar mapas.
- **Dono, locatário e construtores de um terreno** esculpem dentro dele. Um vértice do solo só muda se **todos** os quadrados de terreno ao redor dele pertencem a terrenos que a pessoa pode esculpir: a pincelada nunca atravessa a divisa e nunca mexe na borda de um vizinho; terreno sem dono fica para os administradores. Quem está banido de um terreno, e terrenos fechados para construção, não dão direito.
- O desfazer de quem tem direito só guarda os vértices que a pessoa podia mudar.
- Edições comuns (ferramentas antigas, pincel redondo, sem restrição) continuam sendo repassadas como a pincelada inteira; as demais (máscara por terreno, quadrado, borda dura, nivelar, restaurar, erosão) são repassadas como os **vértices que de fato mudaram**, para que ninguém reproduza uma pincelada que o servidor não aplicou por inteiro.
- O protocolo não mudou de forma: a camada e as opções do pincel viajam juntas no mesmo número da mensagem de edição (um número simples de 0 a 15 continua sendo só a camada).

**Testes:** `TerrainBrushTests` (pincel quadrado, borda, nivelar, restaurar, erosão que conserva o material e é determinística, máscara, oito camadas e mapa antigo) e `LandTerrainRightsTests` (quem pode mudar qual vértice, divisas), o `TerrainEditorSmoke` do cliente (o que cada ferramenta envia, a prévia que não toca o mapa, quem pode esculpir).

**Limitações conhecidas desta fase**
- O relevo "de referência" para Restaurar é a ilha original (plana, com praia); não há ainda como um administrador definir outra referência.
- Não há restrição de altura máxima por região nem limite de inclinação por terreno.
- A colisão do solo e os objetos em cima são atualizados pelo mapa novo, mas objetos já no ar **não** descem sozinhos quando o chão é abaixado, nem vegetação é reposicionada (não existe vegetação).
- Quem esculpe numa divisa só consegue se os dois terrenos forem dele; não há aviso especial na tela além da prévia vermelha.
- Escala de textura por camada, importar texturas e a navegação (mapa de caminhos) não foram feitos.

## Fase 6 — Recursos avançados (entregue)

**Chegada e teleporte.** Cada terreno tem um **ponto de chegada** (x, z e para onde o avatar olha) e um **modo de teleporte**: *livre* (cada um escolhe o ponto), *forçado* (todo teleporte que cair no terreno vai para o ponto de chegada) ou *desativado* (ninguém teleporta para lá). Só o dono define; o ponto precisa estar nos quadrados do terreno e fora de objetos sólidos (`PUT .../land/parcels/{id}/arrival`). Sem ponto, vale o quadrado mais próximo do meio do terreno. O servidor da ilha decide tudo: o cliente pede `RequestTeleport(parcelId, x, z)` e recebe `TeleportResult`. O servidor aplica o modo, a lista de acesso (banido ou terreno fechado → `no_access`), os limites da ilha, limita a 1 pedido por segundo e recusa quem está num veículo; a altura vem do relevo e o avatar é tirado de dentro de paredes. As regras chegam ao servidor pelo `land-sync` (bloco `teleport`). **Protocolo de rede foi para a versão 7** (publique API e servidores de ilha juntos).

**Ambiente por terreno.** O dono pode mudar hora, clima (limpo, nublado, chuva, tempestade, neblina), névoa, intensidade da luz e cor do céu **só dentro do terreno** (`PUT .../environment`). O que não é marcado vem da ilha. O cliente mistura em 3 s ao entrar e ao sair e depois volta ao ambiente da ilha (e ao que a pessoa escolheu no menu Ambiente). Uma ilha pode proibir isso com `"parcelEnvironment": false` no ambiente dela. Os valores são normalizados no servidor e no cliente (as duas regras têm teste de paridade).

**Música por terreno.** O dono informa o endereço de um arquivo **.mp3 ou .ogg**, volume, nome e se toca sozinho (`PUT .../media`). Regras de endereço (API e cliente): só **https**, sem usuário na URL, sem localhost, nomes sem ponto, `.local`/`.internal`, nem IPs privados; o cliente ainda resolve o nome e recusa se apontar para rede interna, não segue redirecionamentos e limita o arquivo a 12 MB. Cada computador baixa e toca o arquivo; há opção **"Ouvir a música dos terrenos"** (guardada no computador) para desligar tudo. **Vídeo não existe** (a engine não tem como tocá-lo com segurança), e a música não passa pelo servidor.

**Mapa de terrenos.** Janela (menu Mundo → "Mapa de terrenos e busca…") com o mapa visto de cima, cada terreno na cor do que significa para você (seu, à venda/aluguel, público, restrito, de outros), contornos, sua posição, filtros (todos, à venda, para alugar, meus, públicos, restritos), dica com nome/dono/área/preço, e painel com **Teleportar**, **Ver terreno**, **Comprar** e **Alugar** (abrem a janela do terreno na aba de venda). Duplo clique teleporta; clicar no chão livre permite teleportar para o ponto.

**Busca de propriedades.** Aba "Buscar terrenos" da mesma janela: `GET /api/v1/discovery/properties?q&offer&minPrice&maxPrice&minArea&maxArea&sort&skip&take`. Só aparecem ofertas que a pessoa poderia aceitar: ilhas públicas, venda reservada a outra pessoa, terreno já alugado e terrenos da própria pessoa ficam de fora. Ao escolher um resultado em outra ilha o cliente entra nela; na mesma ilha, teleporta.

**Aba "Ambiente"** da janela do terreno reúne chegada, ambiente e música; quem não é dono vê os valores, sem editar.

**Banco:** migração `Phase38LandPresentation` (`ArrivalYaw`, `TeleportMode`, `MediaJson`; o ponto de chegada reutiliza `SpawnX/SpawnZ` e o ambiente reutiliza `EnvironmentJson`).

**Testes:** `LandPresentationTests` (30: endereços de música, paridade de ambiente, permissões, chegada dentro do terreno e fora de objetos, `land-sync` + decisão do servidor, busca, visão), `LandPresenceSmoke` no cliente (ambiente com mistura, música, aba Ambiente, teleporte, mapa, busca) e `ProtocolConsistencyTests` com o protocolo 7.

**Limitações conhecidas desta fase**
- A música é checada pelo endereço (e pelo IP resolvido antes do download), mas não há inspeção do conteúdo do arquivo; o dono do site pode trocá-lo depois.
- O teleporte é só dentro da ilha atual; entre ilhas o cliente entra na ilha e a pessoa usa o mapa de lá.
- O ponto de chegada vale para quem teleporta; quem entra na ilha continua nascendo no ponto de entrada da ilha.
- O ambiente do terreno não altera a água/oceano nem a chuva de quem está fora do terreno; vizinhos não veem o clima do terreno do outro.
- Ao trocar de dono o terreno mantém ambiente e música do dono anterior até o novo dono mudar.
- A busca ordena por preço da oferta que a pessoa pode aceitar; ainda não há filtro por região da ilha.

## Fase 7 — Multiplayer, segurança, persistência, otimização e concorrência (entregue)

**Sincronização.** O cliente pergunta a cada 10 s `GET /api/v1/worlds/islands/{id}/land/version` (um número só) e recarrega o território inteiro apenas quando ele mudou. Vendas, aluguéis, novos terrenos e mudanças de regra de outras pessoas aparecem sem abrir janela. Os servidores de ilha continuam puxando o `land-sync` a cada 10 s (acesso, direitos de relevo, teleporte, eventos de objetos). Sem mudança de protocolo.

**Segurança.** Um filtro no grupo `/land` recusa (404) qualquer rota cujo terreno ou aluguel não pertença à ilha do endereço, para ninguém alcançar terrenos de outra ilha pela rota de uma ilha qualquer. Continuam valendo: dono/administrador verificado em cada operação, chaves de idempotência, limite de 1 teleporte por segundo, validação de endereços de música e de ambiente, auditoria em `ParcelChanges`. O servidor da ilha esquece o limitador de teleporte de quem desconectou.

**Persistência.** O servidor de ilha não guarda regras de terreno: após reiniciar, reconstrói acesso, direitos de relevo e teleporte do banco (teste compara as respostas antes/depois de um "processo novo"); o relevo editado com direitos de terreno sobrevive a salvar/carregar.

**Otimização.** `ViewAsync` deixou de testar cada objeto contra cada terreno (um mapa célula → terreno resolve) e de buscar terrenos por varredura; `land-sync` lê as células uma vez só. Teste de carga: 289 terrenos e ~4.000 objetos, visão e sincronização em segundos.

**Concorrência.** Dois administradores no mesmo terreno: o segundo recebe `conflict` e pode tentar de novo sobre o estado novo, sem perder nem duplicar revisão; dividir enquanto o dono vende mantém células sem duplicata e áreas coerentes. Os testes contra **PostgreSQL real** (`LandPostgresTests`) rodam quando `LIFEVERSE_TEST_PG` aponta para um servidor onde o usuário cria bancos; cada teste cria um banco novo, aplica as migrações do zero e o apaga: dois compradores simultâneos (só um vence, dinheiro move uma vez, o perdedor não é cobrado), repetição após resposta perdida (cobra uma vez), seis administradores ao mesmo tempo (revisões únicas) e colunas da Fase 6. Sem a variável, esses 4 testes aparecem como ignorados.

**Cobertura dos testes obrigatórios do prompt:** dois compradores (`LandCommerceTests`, `LandPostgresTests`), relevo sem permissão e perto de divisas (`LandTerrainRightsTests`, `TerrainBrushTests`), dividir e unir terrenos, unir não adjacentes (`LandAdminTests`), aluguel expirado com objetos do locatário e transferência com objetos (`LandCommerceTests`), banido teleportando (`LandHardeningTests`, `LandPresentationTests`), limite de construção e devolução de objetos (`LandBuildingTests`, `LandAdminTests`), reinício do servidor (`LandHardeningTests`), dois administradores, desconexão durante compra (resposta perdida + nova tentativa com a mesma chave).

**Limitações conhecidas**
- Os testes em PostgreSQL real **não foram executados neste ambiente** (sem credenciais com permissão de criar bancos); o provedor em memória não prova rollback nem concorrência real.
- A rota de versão não detecta mudança de contagem de objetos nem de ocupação; essas somem na próxima abertura da janela.
- O filtro de ilha/terreno não tem teste automatizado de HTTP (não há servidor de teste no projeto).

## O que ainda falta (próximas fases)

- Limitações atuais: a lista de parcelas não é empurrada aos outros jogadores quando muda; o cliente desenha só a vizinhança (28 × 28 quadrados) ao redor da câmera; o `POST /land/claim` não cobra nem verifica saldo (criar terreno continua restrito a donos e administradores da ilha); os testes do servidor usam banco em memória (a restrição de chave única das células vale no PostgreSQL e foi simulada no teste).
