# Janela "Informações e Gerenciamento da Ilha"

Abre em **Mundo → Gerenciar esta ilha…**. É a janela onde o proprietário (e quem tem a permissão `admin`) cuida da ilha onde está.

## O que já funciona

| Parte | Estado |
|---|---|
| Barra de título (logo, ajuda, minimizar, fechar), arrastar, redimensionar pelo canto | pronto |
| Menu lateral com as 13 seções, rolável, com seleção, hover e marca de alteração pendente | pronto |
| Cabeçalho da ilha (miniatura, nome, estado real, ID com copiar, visitantes, acesso, Ver no Buscador) | pronto |
| Rodapé (última atualização, Restaurar Padrão, Salvar Alterações) | pronto |
| **Geral**: identidade, imagem, categoria, classificação, idioma, proprietário, administradores, data, capacidade, visibilidade, uso de prims, acesso atual, ponto de chegada | pronto |
| **Acesso**: modo (pública, somente convidados, temporariamente fechada), lista de pessoas permitidas com expiração, pesquisa, paginação, ver perfil, alterar expiração e remover | pronto |
| **Banimentos**: resumo, banir com motivo e duração, lista com histórico, pesquisa, filtro por estado, paginação, ver perfil, alterar duração e desbanir | pronto |
| **Permissões**: o que se pode permitir, papéis, pessoas com permissões em interruptores, adicionar por papel, pesquisa, paginação, ver perfil e tirar tudo | pronto |
| **Objetos e Prims**: orçamento (prims totais, usadas, livres, objetos), lista com dono, tipo, scripts e física, filtros, ordenação, páginas, detalhes do objeto, consumo por proprietário e devolução | pronto |
| **Histórico e Auditoria**: eventos de gerenciamento e de construção com quem fez, quando e valores antes e depois; filtros por período, pessoa, tipo e texto; detalhes do evento | pronto |
| **Administração**: informações do servidor, jogadores conectados, expulsar, banir, aviso a todos ou a uma pessoa, fechar e reabrir a ilha | pronto (depende do servidor da ilha novo) |
| **Desempenho**: FPS, tick, tempo de quadro, memória, jogadores e scripts ativos; gráficos de 1, 6 e 24 horas; diagnóstico de scripts e objetos | pronto (depende do servidor da ilha novo) |
| **Reinicialização**: estado, reiniciar agora ou em 1, 5 ou 10 minutos ou em um horário, contagem regressiva, cancelar e histórico | pronto (depende do servidor da ilha novo) |
| **Ambiente**: horário, luz, clima, neblina e cor do céu da ilha toda, com predefinições do jogo e suas, aplicar, desfazer e voltar ao padrão | pronto |
| **Mídia**: música da ilha (endereço verificado na hora, nome, volume, tocar sozinha, testar), vídeo explicado como não disponível | pronto |
| Terreno, Física | **ainda não**: cada uma mostra "Ainda não disponível nesta versão" e o que vai reunir. Nenhuma tem dado inventado |

## Como a aba Geral decide as coisas

- **Tudo vem do servidor**: `GET /api/v1/worlds/islands/{id}/management`. Nome, descrição, contadores, uso de prims (custo usado ÷ orçamento da ilha), visitantes (do heartbeat do servidor da ilha, só vale se o último foi há menos de 60 s) e estado (online, iniciando, reiniciando, desligada).
- **Quem lê**: o proprietário, quem tem `admin`, e qualquer pessoa se a ilha for pública (só leitura). Ilha privada: 403.
- **Quem salva**: só o proprietário e quem tem `admin` (`PUT .../management/general`). O servidor valida tudo de novo (nome 1–48, descrição até 500, categoria/classificação/idioma da lista, capacidade 1–256, ponto de chegada dentro da ilha).
- **Concorrência**: o cliente manda o `updatedAt` que viu. Se outra pessoa salvou antes, o servidor responde 409 e nada é sobrescrito; a janela pergunta se quer recarregar.
- **Sucesso só depois de gravar**: a mensagem "Configurações da ilha atualizadas com sucesso." aparece depois da resposta do servidor. Erros mostram o motivo do servidor e marcam o campo.
- **Salvar** fica desligado sem alterações, sem permissão ou fora da aba Geral. Fechar com alteração pendente pergunta antes de descartar.
- **Restaurar Padrão** (aba Geral) volta categoria, classificação, idioma, capacidade (64) e visibilidade ao inicial, mediante confirmação, **sem salvar**. Nome, descrição e imagem não são tocados.

## O que os campos fazem de verdade

- **Aparecer no buscador** (`show_in_search`): tirado do índice de busca e das buscas de ilhas. A ilha continua pública para quem sabe chegar nela.
- **Aparecer no mapa** (`show_on_map`): guardado e mostrado em "Entrada pelo mapa"; o mapa ainda não filtra por ele (ver limites).
- **Classificação** e **idioma**: guardados e mostrados; ainda não filtram nada.
- **Ponto de chegada**: o servidor da ilha usa X e Z; a altura (Y) é a do terreno naquele ponto. Por isso a tela mostra só X e Z. "Usar Minha Posição" lê o avatar.
- **Capacidade**: o limite que o servidor já aplica ao teleportar.
- **Imagem de divulgação**: escolhida entre as suas texturas e guardada como `asset:<id>`. A janela mostra; a tela de carregamento e o buscador ainda só entendem endereços web.

## Limites conhecidos desta entrega

- 2 das 13 seções não existem ainda (acima).
- **Definir no Mundo** (clicar no cenário para escolher o ponto) está desligado, com dica. Falta a ferramenta de seleção no mundo.
- **Gerenciar** administradores está desligado até existir a aba Permissões.
- Não há botão de enquadrar a imagem.
- O mapa e o buscador de ilhas ainda não filtram por classificação, idioma nem "Aparecer no mapa".
- A imagem `asset:<id>` não aparece na tela de carregamento nem na lista do buscador.
- O nome novo é avisado por um evento (`IslandRenamed`), mas a barra superior do jogo ainda não escuta.
- Migração nova: `Phase65IslandManagement` (4 colunas em `world.islands`). O servidor de produção precisa ser reimplantado e migrado.
- Visual: conferido por captura (1536×1024) contra o mockup. Cores, fontes e proporções seguem a tabela do design; o tema ainda não está em um `Theme` do Godot, e sim em `IslandUi` (uma classe com os tokens).

## Testes

- API: `IslandManagementTests` (leitura, permissões, validação, conflito, visibilidade).
- Cliente: `IslandManagementSmoke` com um servidor de mentira: números reais, rascunho, salvar, recusa, conflito, fechar com alteração, restaurar padrão, troca de seção, sem permissão, servidor fora do ar, telas de 1280×720 a 2560×1440. Para gerar uma captura: `LIFEVERSE_SHOT=caminho.png` com a janela do Godot aberta (não `--headless`).

## Aba Acesso

- **Regra única** (`IslandAccess.AllowsAsync`), usada no pedido de entrada (token) e no teleporte: o proprietário e quem tem `admin` entram sempre; se a ilha está **temporariamente fechada**, ninguém mais; senão, ilha **pública** é aberta a todos e ilha **somente convidados** só a quem está na lista e cuja permissão não expirou. A expiração é conferida na hora da entrada, não depende de uma varredura.
- **Modos**: `public` (IsPublic), `invite` (privada, só a lista) e `closed` (`access_closed`). Fechar guarda se a ilha era pública, então reabrir devolve o modo anterior. Quem já está na ilha não é expulso; vale para as próximas entradas.
- **Lista** (`GET/POST/PUT/DELETE .../management/access`): pessoas por nome (usuário ou nome do avatar), expiração opcional (sem, 1 h, 1 dia, 7 dias, 30 dias), 10 por página, pesquisa por nome. Cada adicionar/alterar/remover vale na hora; o **modo** é rascunho e vai com "Salvar Alterações" (junto com a aba Geral, que é salva antes), com a mesma proteção contra salvar por cima de mudança alheia.
- **Erros mostrados com o motivo do servidor**: pessoa não encontrada, já está na lista, proprietário (já entra sempre), expiração no passado.
- Mensagem nova no teleporte: "Essa ilha está temporariamente fechada."
- Migração nova: `Phase66IslandAccess` (`world.islands.access_closed`, `world.permissions.expires_at`).

### O que a aba Acesso **não** tem (e por quê)
- **Somente grupo** e **adicionar grupo**: as checagens de entrada de várias partes do servidor ainda olham só pessoas; sem fazer o servidor honrar grupos, a opção seria enfeite.
- **Regras de entrada por interruptor** (mapa, teleporte direto, convites, convidados temporários, retorno ao último local, restrição por classificação): não existe regra no servidor para cada uma. O card "Quem pode entrar agora" descreve o efeito real do modo no lugar delas.
- **Capacidade máxima** fica na aba Geral.
- Mudar o modo ou tirar alguém da lista não expulsa quem já está na ilha.

## Aba Banimentos

- **Aplicado no servidor**: o banimento entra na mesma regra de entrada do Acesso (`IslandAccess.AllowsAsync`). Quem está banido não consegue token de entrada nem teleporte, mesmo se estiver na lista de permitidos ou se a ilha for pública. O proprietário e os administradores nunca são afetados.
- **Quem pode**: proprietário, administradores e quem tem a permissão `ban`. Ninguém pode banir o proprietário, um administrador ou a si mesmo; a mesma pessoa não pode ter dois banimentos ativos.
- **Duração**: 1 hora, 1 dia, 7 dias, 30 dias ou permanente; muda depois pelo ícone de calendário. O banimento temporário termina sozinho, na hora da entrada, sem tarefa em segundo plano.
- **Histórico**: banimentos que terminaram ou foram desfeitos continuam na lista (estado Expirado ou Desbanido), com quem baniu, motivo e datas. O filtro e a pesquisa (por pessoa **ou motivo**) funcionam sobre tudo. Os quatro contadores contam só o que está valendo (ativos, temporários, permanentes) e os expirados.
- **Confirmação** antes de banir e antes de desbanir. Motivo é obrigatório (até 200 caracteres).
- Migração nova: `Phase67IslandBans` (`world.island_bans`).

### Limites do banimento
- **Não expulsa quem já está na ilha**: vale para a próxima entrada. Expulsar na hora exige que o servidor da ilha desconecte a pessoa, o que ainda não foi feito (a aba Administração vai ter isso).
- Não há banimento por grupo nem "ver histórico de uma pessoa" separado; o histórico é a própria lista com o filtro "Desbanidos".
- Os outros caminhos que só leem dados da ilha (manifesto de assets, terrenos) ainda não consultam o banimento; sem o token de entrada a pessoa não chega a se conectar.

### Correção feita junto
- No Acesso, adicionar de novo uma pessoa cuja entrada **expirou** (e não foi revogada) bateria no índice único do Postgres. Agora a entrada expirada é renovada em vez de criar uma segunda.

## Aba Permissões

- **Só o que o servidor confere**: construir e editar objetos (`build`, e o nome antigo `edit`, que vale o mesmo), editar scripts (`run_scripts`), modificar terreno (`edit_terrain`), banir (`ban`) e administrar (`admin`). `expel`, `invite` e `delete` existem no cadastro do servidor mas nada os consulta, então a aba não os oferece. Entrar é a aba Acesso.
- **Papéis** são conjuntos prontos, não algo guardado na ilha: Proprietário (tudo, fixo), Administrador (`admin`), Moderador (`ban`), Construtor (`build` e `run_scripts`) e Visitante (nada). A tabela de papéis conta as pessoas cujo conjunto é exatamente o do papel; quem tem outra combinação aparece como **Personalizado**.
- **Interruptores**: cada um manda ao servidor o conjunto completo que a pessoa deve ter; se o servidor recusa, o interruptor volta e o motivo aparece. Dar permissões por nome escolhe um papel (Moderador, Construtor ou Administrador, este só para o proprietário).
- **Limites que o servidor aplica**: ninguém muda as próprias permissões nem as do proprietário; só o proprietário dá ou tira `admin`, e só ele mexe nas permissões de um administrador. Assim ninguém sobe acima do que já é nem se tranca para fora.
- Não precisa de migração: usa a tabela de permissões que já existia.

### Limites
- **Papéis personalizados** (criar, editar, nomear) não existem: dependeriam de guardar papéis na ilha e de o servidor consultá-los.
- Não há permissão por grupo.
- Permissões **por terreno** (parcelas) continuam na janela de terrenos, não aqui.
- Mudar uma permissão vale na próxima ação da pessoa; não há aviso a ela.

## Aba Objetos e Prims

- **Números reais**: "prims" é o custo (`cost`) que cada objeto já tem para o orçamento da ilha (o mesmo que o construtor usa para recusar quando estoura). Total = orçamento da ilha, utilizadas = soma dos custos, disponíveis = a diferença; objetos = quantidade viva e o limite de objetos. A barra muda de cor a 75% e a 90%.
- **Lista**: nome, dono, tipo, prims, scripts ativos e física (estático, físico, sem colisão), ordenada por prims, mais recentes ou nome; pesquisa por objeto **ou dono**; filtros por dono, tipo e com/sem scripts; 10 por página. Clicar numa linha abre os detalhes ao lado (ID, dono, criador, dono anterior, tipo, prims, scripts, física, posição, rotação, dimensões, proteção, quando some se for temporário, criação e permissões).
- **Consumo por proprietário**: objetos, prims e scripts de cada pessoa; "Ver objetos" filtra a lista por ela.
- **Devolver** (um objeto, ou tudo de uma pessoa): usa a mesma operação do sistema de terrenos, que guarda os objetos de cada dono como um pacote no inventário dele e só então tira da ilha. **Nada é apagado.** Pede confirmação. Só o proprietário e os administradores veem e fazem; um administrador não devolve os objetos do proprietário.
- Sem migração: só lê as tabelas que já existiam.

### Limites
- **Não há excluir de vez**: só devolver. Apagar de vez continua sendo feito pelo editor de construção.
- **Localizar, inspecionar e editar** um objeto a partir daqui não existem (a janela não move a câmera nem abre o editor). A posição aparece nos detalhes em coordenadas.
- A devolução de tudo de uma pessoa roda em lotes de 200 e para no limite de 25 lotes; se sobrar algo, a tela avisa quantos restam e é só repetir.
- A lista e os números vêm do banco, que o servidor da ilha atualiza ao salvar; um objeto criado agora pode demorar a aparecer.
- Não mostra "Land Impact" separado de "prims": no jogo os dois são o mesmo custo.

## Aba Histórico e Auditoria

- **O que entra**: cada ação feita pelas abas deixa um registro (`world.island_audit`) **na mesma gravação** da mudança, com quem fez, o tipo, o recurso e os valores **antes e depois**: configurações gerais (só o que mudou), modo de acesso, entradas na lista de permitidos (adicionar, mudar expiração, remover), banimentos (banir, mudar duração, desbanir), permissões e objetos devolvidos. Salvar sem mudar nada não registra nada. Ações recusadas pelo servidor também não.
- **Construção**: o histórico de operações de objetos que a ilha já guardava (criar, mover, devolver, excluir, com quem) aparece junto, como tipo "Construção", com o payload da operação nos detalhes.
- **Consulta**: período (24 horas, 7 dias, 30 dias, tudo), quem fez, tipo e texto (no resumo ou no objeto), 10 por página, mais novo primeiro. Ao escolher um evento: ID, responsável, ilha, data, operação, recurso, resultado, valores anteriores e novos e o ID de correlação.
- **Só leitura**: não há como alterar ou apagar um registro pela API. Só o proprietário e os administradores leem.
- Migração nova: `Phase68IslandAudit`.

### Limites
- Só registra o que passa a acontecer depois desta versão; o que foi feito antes pelas abas (ou por outros caminhos, como o editor de terrenos) não tem registro.
- **Resultado** é sempre "Sucesso": ações recusadas não são guardadas.
- Mudanças de terreno e de ambiente não aparecem (essas abas ainda não existem), e ações do editor de terrenos (venda, aluguel) têm o histórico delas na própria janela de terrenos.
- Os registros ficam para sempre; não há limpeza nem exportação ainda.

## Canal API ↔ servidor da ilha e aba Administração

A API não alcança os jogadores; só o servidor da ilha alcança. O canal usa o **batimento** que o servidor já mandava a cada 15 segundos:

- **O servidor conta** (corpo do batimento, `report`): quem está conectado (usuário, nome, desde quando, posição) e como ele está (FPS, tempo de processamento, memória, tempo no ar). A API guarda o último relatório (`server_instances.report_json`) cortado a 300 jogadores, 80 caracteres por nome e números finitos.
- **A API pede** (resposta do batimento): `{"commands":[...]}` com `kick` (tirar uma pessoa, com motivo) e `notice` (aviso a todos ou a uma pessoa). Cada comando é entregue **uma vez**, vale por **2 minutos** e só para a ilha dele (`world.island_commands`). Comandos de tipo desconhecido são ignorados, então um servidor antigo não quebra com pedidos novos. Resposta 204 continua significando "nada pedido".
- **No servidor da ilha**: expulsar manda o aviso "Você foi removido da ilha: motivo" e desconecta a pessoa 0,6 s depois; aviso usa o chat de sistema. O texto é filtrado de caracteres de controle.

### Administração
- **Servidor**: estado, tempo no ar, último sinal, identificador curto do servidor, visitantes. Endereço e porta **nunca** vão ao dono.
- **Jogadores**: a lista do último relatório (só vale se tem menos de 60 s), com tempo online e posição; a equipe aparece marcada e não pode ser expulsa nem banida. A lista se atualiza sozinha a cada 10 s.
- **Expulsar / Avisar**: vira um comando; chega ao jogador em até ~15 s. Recusas do servidor aparecem com o motivo (já saiu da ilha, ilha desligada, equipe, si mesmo, texto longo).
- **Banir**: cria o banimento (Banimentos) e expulsa; a pessoa não volta a entrar.
- **Fechar / Reabrir**: é o modo "temporariamente fechada" do Acesso (mesma regra, mesma auditoria); recusa se há alterações pendentes na tela.
- Tudo isso entra no **Histórico e Auditoria** (categoria Servidor).
- Migração nova: `Phase69ServerChannel` (`world.island_commands`, `platform.server_instances.report_json/reported_at`).

### Limites
- **O servidor da ilha precisa ser reimplantado** para relatar jogadores e obedecer comandos. Com o servidor antigo, a lista de jogadores fica vazia ("o servidor ainda não mandou a lista") e os pedidos expiram sem efeito.
- **Não testei o servidor da ilha em execução**: ele compila e o protocolo é coberto por testes na API e no cliente, mas a expulsão e o aviso de ponta a ponta (API → batimento → servidor → jogador) precisam ser conferidos numa ilha de verdade.
- Há até ~15 s entre pedir e acontecer.
- **Não há** salvar estado, criar ou restaurar backup, nem aviso com contagem regressiva: o servidor não expõe nada disso.
- Fechar a ilha não expulsa quem já está nela.

## Aba Desempenho

- **De onde vêm as medidas**: do relatório que o servidor da ilha manda no batimento (canal descrito acima): FPS, tempo de cada quadro (ms), memória (MB), ciclos de física por segundo (tick rate) e quem está conectado. Os indicadores mostram o último relatório, só se tiver menos de 60 s.
- **Histórico**: a API guarda **uma amostra por minuto** (`world.island_metrics`) e apaga as de mais de 24 horas. Os gráficos (FPS, memória, tempo de quadro, jogadores) cobrem a última hora, 6 horas ou 24 horas, com até 120 pontos (agrupados por média; jogadores pelo pico) e dizem o mínimo, a média e o máximo do período.
- **Scripts ativos** vem do banco (scripts ligados a objetos vivos da ilha).
- **Diagnóstico**: scripts que mais executaram no período (instruções, execuções, quantas deram erro) e os 10 objetos mais pesados (prims, se têm física). Dá para filtrar por scripts ou objetos. A tela atualiza sozinha a cada 15 s.
- Só o proprietário e os administradores veem. Migração nova: `Phase70IslandMetrics`.

### Limites
- **Não há CPU**: o servidor da ilha não relata uso de CPU. O tempo de quadro mostra a mesma coisa (quanto o servidor leva por quadro) e o FPS fica verde, laranja ou vermelho comparado ao tick esperado.
- Exige o **servidor da ilha novo** (o que relata); com o antigo as medidas ficam em branco e só aparece o que já foi gravado.
- O consumo por script depende de as execuções serem gravadas (`ScriptExecution`); o que o servidor da ilha roda sem gravar não aparece.
- Não há alertas nem limites configuráveis ainda, e o histórico é de um dia.

## Aba Reinicialização

- **Como funciona**: o pedido vira uma linha em `world.island_restarts` e um serviço da API (`IslandRestartWorker`, a cada 5 s) move o reinício passo a passo, então ele sobrevive a um reinício da própria API:
  1. **Agendada**: aos 5 minutos, 1 minuto e 10 segundos antes, quem está na ilha recebe um aviso (só o menor que já venceu, e cada um uma vez).
  2. **Na hora**: o servidor da ilha recebe o comando `shutdown`, avisa quem está nela, **salva a ilha e as pessoas** (o mesmo encerramento limpo que ele já fazia ao ficar ocioso) e sai.
  3. **Voltando**: quando o servidor antigo sai, a API pede uma nova instância ao gerenciador de mundos e o supervisor sobe o processo. Quando o novo servidor responde, o reinício fica **Concluída**.
  4. Se em 5 minutos nada acontece, fica **Falhou** com o motivo ("não obedeceu ao pedido (versão antiga?)" ou "não voltou ao ar").
- **Pedido**: imediatamente, em 1, 5 ou 10 minutos, ou em um horário entre 1 minuto e 24 horas; motivo opcional (até 200 caracteres). Pede confirmação. Só um por vez; com a ilha desligada recusa ("não há o que reiniciar").
- **Cancelar** só antes de começar; quem já foi avisado recebe "a reinicialização foi cancelada".
- **Histórico**: data, responsável, motivo, duração e resultado (concluída, cancelada ou falhou). Pedido, cancelamento, conclusão e falha entram no Histórico e Auditoria.
- Só o proprietário e os administradores. Migração nova: `Phase71IslandRestarts`.

### Limites
- **O servidor da ilha novo é necessário** para obedecer ao `shutdown`; um antigo ignora o comando e o reinício falha após 5 minutos (sem derrubar ninguém).
- A ilha só volta se o **supervisor** estiver rodando; sem ele, falha com "não voltou ao ar".
- Quem estava na ilha é desconectado e precisa **entrar de novo** (o jogo ainda não reconecta sozinho).
- O comando chega com o próximo batimento: pode atrasar até uns 15 s depois do horário.
- Não há reinício forçado (matar o processo): a API só pede, o servidor decide sair.
- **Não testei** o ciclo completo (aviso, parada, volta) numa ilha de verdade; a máquina de estados é coberta por testes e o servidor da ilha só foi compilado.

## Aba Ambiente

- **O que é**: o "olhar" da ilha toda: horário do dia, intensidade da luz, clima (limpo, nublado, chuva, tempestade, neblina), neblina e cor do céu. Cada ajuste tem um interruptor: desligado, vale o do próprio jogo. É o mesmo modelo que cada terreno já tinha (`ParcelEnvironment`).
- **Onde aparece**: em todo lugar da ilha **fora de terrenos**. Dentro de um terreno com ambiente próprio, o do terreno vale por cima e a ilha preenche o que o terreno não define (`ParcelEnvironment.Over`); um terreno que não pode ter ambiente próprio, ou que não definiu nada, mostra o da ilha.
- **Como chega às pessoas**: o jogo (`IslandLookService`) pergunta ao servidor ao entrar na ilha e **a cada 30 segundos** (`GET .../environment`, para quem pode entrar), e a transição é a mesma suave dos terrenos. Se o servidor não responde, fica o último ambiente. Quem já está lá vê a mudança em até 30 s.
- **Nada muda até "Aplicar na Ilha"** (pede confirmação). "Desfazer alterações" volta ao que a ilha tem; "Voltar ao padrão do jogo" tira o ambiente de todos.
- **Predefinições**: seis do jogo (Dia claro, Pôr do sol, Noite, Neblina, Tempestade, Padrão do jogo) e até **8 suas**, com nome de até 40 caracteres; "Usar no formulário" só preenche, não aplica. Nomes de predefinições do jogo são reservados.
- **Validação no servidor**: horário 0–24, luz 0,2–1,6, neblina 0–1, clima da lista, cor `#RRGGBB`; qualquer outro ajuste é recusado. Mesma proteção contra salvar por cima de mudança alheia. Aplicar, salvar e excluir predefinição entram no Histórico e Auditoria (categoria Ambiente).
- O olhar fica em `islands.look_json`, **separado** de `environment_json`, porque este é reescrito pelo servidor da ilha quando salva o estado. Migração nova: `Phase72IslandLook`.

### Limites
- **Água** (nível, cor, reflexos, ondas), **nuvens**, **vento** e **ciclo do dia** da ilha não existem no jogo como ajuste da ilha, então não estão na aba. O horário aqui é fixo (o relógio do jogo fica parado nesse horário onde o ambiente vale).
- A atualização leva **até 30 s** (não é instantânea).
- O jogo novo (cliente) é necessário para mostrar; o cliente antigo ignora o ambiente da ilha e mostra o céu padrão.

## Aba Mídia

- **Música da ilha**: um arquivo de música que toca **onde nenhum terreno toca a sua** (terreno aberto, ou terreno que não define música). O terreno com música própria continua tocando a dele. Usa as mesmas regras de endereço da música de terreno, porque o arquivo é baixado no computador de cada jogador: **HTTPS**, sem usuário e senha no endereço, nada que aponte para dentro de uma rede (localhost, IPs privados, `.local`, `.internal`) e arquivo **.mp3 ou .ogg** de até 12 MB. O endereço é conferido enquanto se digita (jogo e servidor aplicam a mesma regra).
- **Campos**: endereço, nome da música ou estação (até 80 caracteres, só aparece aqui), volume padrão (0 a 100%) e "Tocar sozinha ao chegar na ilha". Se não toca sozinha, o jogador ouve o aviso "a música da ilha não toca sozinha".
- **Testar**: baixa e toca o arquivo **só para quem testa**, no volume escolhido; "Parar teste" ou sair da aba/fechar a janela encerra e o jogo volta ao que o lugar toca. Nada é salvo ao testar.
- **Salvar na Ilha** / **Remover música** (com confirmação): chega a quem está na ilha em até **30 s** (o mesmo ciclo do ambiente: `GET .../environment` devolve o olhar e a mídia). Cada jogador pode desligar a música nas próprias preferências. Mesma proteção contra salvar por cima de mudança alheia; entra no Histórico e Auditoria (categoria Mídia).
- **Vídeo**: o jogo não mostra vídeo, então o cartão diz "Ainda não disponível" e não há o que configurar. Nada é declarado como suportado sem estar implementado.
- Migração nova: `Phase73IslandMedia` (`islands.media_json`).

### Correção feita junto
- Nas abas Ambiente e Mídia, trocar de aba e voltar **recarregava do servidor e jogava fora o rascunho**. Agora o rascunho é mantido (e a versão em que ele se baseou continua valendo para detectar conflito).

### Limites
- Só **música por arquivo** (mp3/ogg), não rádio por transmissão contínua (stream): o jogo baixa o arquivo inteiro e repete.
- Não há playlist, agenda nem volume por região; só uma música para a ilha toda.
- Exige o jogo novo (cliente); o antigo não toca a música da ilha.
