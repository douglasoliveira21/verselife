# Exploração e descoberta — Fase 09

O diretório global usa `/api/v1/discovery/islands` com pesquisa textual, categoria, ordenação (`popular`/`recent`), paginação e exposição somente de ilhas públicas ou próprias. Favoritos são persistidos por usuário.

O teletransporte cria uma reserva curta e chama o `WorldManager`; ilhas suspensas entram em `pending`, ilhas disponíveis em `reserved`, e a reserva expira em 30 segundos. Capacidade é verificada no servidor antes da transferência.

Landmarks, eventos com capacidade/inscrição e fotos com metadados JSON possuem endpoints autenticados e persistência no schema `discovery`. A captura de imagem no cliente pode salvar arquivo local; a publicação usa uma chave de armazenamento validada pela camada de assets.

O painel Mapa do cliente lista ilhas independentes e exibe um minimapa textual com posição, direção e POIs. A renderização cartográfica detalhada e o backend de presença em tempo real permanecem pontos de evolução visual.

# Busca global — fase 1: índice e busca textual

Uma busca única (`GET /api/v1/search`) sobre **oito tipos de coisa**: ilhas, terrenos, pessoas com perfil público, grupos, lojas, produtos, eventos e pontos de interesse. "Locais populares" e "locais recentes" são listas prontas dos lugares (`GET /search/popular`, `/search/recent`). O autocomplete é `GET /search/suggest?q=`. A rota antiga `/discovery/search` continua como estava.

## Como funciona

**Índice** (`discovery.search_index`, migração `Phase49GlobalSearch`): uma linha por coisa encontrável, com título, subtítulo, resumo, categoria, miniatura, ilha e ponto (para o teleporte), popularidade e o texto **normalizado** (minúsculas, sem acento nem pontuação). O PostgreSQL constrói sozinho o vetor de busca (`search_vector`, coluna gerada com `to_tsvector('simple', …)`) e um índice **GIN** acha palavras e começos de palavra ("prai" acha "praia"). Foi usado o recurso nativo do PostgreSQL, sem extensões (nada de `unaccent` nem `pg_trgm`, que pedem privilégio de administrador): quem tira os acentos é o próprio código, dos dois lados (`SearchText`).

**Atualização incremental** (`SearchIndexer`, a cada 30 s em segundo plano e sob demanda): para cada fonte lê só o que mudou desde a última passada (uma marca-d'água por fonte, pelo "mudou em" da própria linha), grava só as linhas que de fato mudaram, e varre as linhas de coisas que deixaram de ser públicas, foram apagadas ou acabaram (evento que terminou). Rodar de novo não muda nada. Quando uma ilha abre ou fecha, o que há nela (terrenos, lojas, eventos, pontos) entra ou sai junto. A popularidade (favoritos e gente presente nas últimas 24 h nas ilhas; membros nos grupos; vendas nos produtos; inscritos nos eventos) é recalculada a cada passada. `POST /api/v1/platform/search/reindex` (chave de serviço) refaz tudo.

**O que entra no índice (e só isso):**
| Tipo | Entra quando |
|---|---|
| Ilha | pública e não apagada |
| Terreno | acesso "público", numa ilha pública |
| Pessoa | ativa, não apagada, não é a conta de sistema, perfil não privado e sem `"searchable":"no"` na privacidade |
| Grupo | privacidade pública e não apagado |
| Loja | aberta, numa ilha pública, com produto ativo |
| Produto | ativo (os de grupo mostram o grupo) |
| Evento | público, de ninguém em particular (não de grupo), ainda não terminou, em ilha pública |
| Ponto de interesse | público, não apagado, em ilha pública |

**Privacidade na hora de buscar** (o índice é uma cópia e pode estar um pouco atrasado, então nada confia só nele): cada resultado é conferido de novo na fonte (público, vivo, em ilha pública) e **não aparece** quem bloqueou você ou foi bloqueado por você (tudo o que é dessa pessoa some), nem você mesmo na lista de pessoas. Linhas escondidas pela moderação (`hidden`) não aparecem e uma reescrita do índice não as traz de volta. Um item que virou privado some **na mesma hora**, antes mesmo da próxima passada.

**Ordem e limites.** A relevância é calculada em C# (igual em qualquer banco): título igual à busca (85) > começa com (60) > tem a frase no meio (37) > tem todas as palavras; mais o quanto cada palavra está no título; um pouco de popularidade (com teto) e de novidade. O nome exato (e o nome que começa com a busca) vence qualquer popularidade. Todas as palavras precisam estar (como começo de palavra); a busca olha de 2 caracteres para cima, até 8 palavras de 32 letras e 100 caracteres. O banco devolve até 400 candidatos (ordenados por `ts_rank` e popularidade) e a página (até 50, padrão 20) sai deles. Tipos: `types=island,group`; nomes desconhecidos são ignorados. A consulta ao banco usa só letras e números, então nada digitado muda a consulta.

## Foi preciso um motor externo (Elasticsearch, Meilisearch…)?
**Não, por ora.** Medido num PostgreSQL 18 real com 50.000 linhas indexadas, buscar candidatos leva cerca de **11 ms**, e o índice GIN responde a consulta de começo de palavra. O que um motor externo daria a mais — tolerância a erro de digitação, sinônimos, ordenação por semelhança — não é necessário para nomes de ilhas, grupos e pessoas nesta escala, e custaria outro serviço para operar, manter em dia e proteger (privacidade em dois lugares). Reavaliar se passar de alguns milhões de linhas, ou se a tolerância a erros de digitação virar pedido real (aí o primeiro passo é a extensão `pg_trgm`, que ainda é PostgreSQL).

## Testes
`SearchTextTests` e `GlobalSearchTests` (47): normalização, termos, consulta sem injeção, relevância (exato > começa > palavra > só no texto; popularidade e novidade só desempatam), os oito tipos indexados com seu lugar, nada privado/apagado/de grupo/encerrado indexado, esconder na hora sem esperar o índice, bloqueios nos dois sentidos, moderação, atualização incremental (só o que mudou; reconstruir dá o mesmo), coisas que terminam ou somem, terrenos e pontos que seguem a ilha, nome novo da pessoa, acentos e começos de palavra, todas as palavras, consultas esquisitas, tipos, paginação estável e completa, autocomplete, populares e recentes, e velocidade (1.500 ilhas indexadas e buscadas em segundos no teste sem banco). `SearchPostgresTests` (2, só com `LIFEVERSE_TEST_PG`): o mesmo no PostgreSQL de verdade — coluna e índice existem, começos de palavra, sem acento, a consulta maliciosa não faz nada, o vetor acompanha o texto, e 50.000 linhas (candidatos em ~11 ms, índice GIN usável). Com essas rodadas, as migrações de todas as fases foram aplicadas do zero num PostgreSQL de verdade.

## Busca global — fase 2: filtros, ordem, favoritos, interesses e histórico

**Filtros e ordem** (`GET /api/v1/search`): além de `q`, `types`, `page` e `pageSize`, a busca aceita `category` (uma categoria, sem diferenciar maiúsculas), `islandId` (só o que está naquela ilha) e `sort`: `relevance` (padrão), `popularity` (ou `popular`) e `updated` (ou `recent`, o que mudou há menos tempo). Nome de ordem desconhecido volta para relevância. Os filtros entram na própria consulta ao banco (não só depois dos 400 candidatos), então um filtro estreito não perde resultados. A resposta traz `counts` (por tipo), `categories` (até 20 categorias com quantas há, para montar o filtro) e `sort`; os totais descrevem tudo o que foi achado, não só a página. A paginação mantém a ordem escolhida (desempate estável por título e id). Cada resultado vem com `favorite` (se já é favorito de quem busca).

**Favoritos** (`discovery.search_favorites`, por pessoa, qualquer tipo de resultado, até 500): `PUT /api/v1/search/favorites/{kind}/{id}` marca (só o que a pessoa pode ver agora; marcar de novo não muda nada; 404 se não existe ou não é visível, 409 se a lista está cheia), `DELETE` desmarca, `GET /api/v1/search/favorites?types=&page=&pageSize=` lista, o último marcado primeiro. O que deixou de ser público (ou foi escondido pela moderação, ou o dono bloqueou/foi bloqueado) **some da lista na hora**, mas continua marcado e volta se voltar a ser público.

**Interesses e recomendações:** `PUT /api/v1/search/interests` troca a lista (cada interesse é limpo: sem acento nem pontuação, até 3 palavras e 32 letras; repetidos saem; até 20); `GET` devolve. `GET /api/v1/search/recommended?take=` procura cada interesse no índice e junta: quanto mais interesses um resultado atende, mais alto ele vai; depois o quanto combina e a popularidade. **Pessoas nunca são recomendadas** (só coisas), o que já é favorito fica de fora, e passa pelas mesmas conferências de privacidade da busca (privado, apagado, bloqueios). Sem interesses, não há o que recomendar.

**Histórico local:** `SearchHistory` (em `shared/Search`) guarda o que a pessoa buscou **só no computador dela** (o servidor nunca recebe essa lista): mais novo primeiro, sem repetir a mesma busca (maiúsculas, acento e pontuação não contam), até 30, nada com menos de 2 letras, `Matching` oferece buscas passadas enquanto se digita, e um arquivo ilegível vira histórico vazio em vez de travar o jogo. A janela de busca (fase 3) usa isso com `user://search_history.json`.

**Testes (`SearchPhase2Tests`, 14):** filtro de categoria e contagem de categorias, filtro de ilha, as três ordens, paginação mantendo a ordem, favoritos marcados nos resultados e listados, só se favorita o que se vê, o que fecha sai da lista e volta, moderação, filtro por tipo e paginação de favoritos, limpeza de interesses, recomendações por quantidade de interesses, recomendações sem favoritos, privados e bloqueados, e o histórico (ordem, limite, digitação, arquivo salvo/danificado). No PostgreSQL de verdade os filtros e a ordem também são conferidos.

## Busca global — fase 3: janela, prévia, teleporte, denúncia e métricas

**Janela "Buscar no LifeVerse…"** (menu Mundo, `client/Scripts/SearchWindow.cs`): três abas — *Buscar*, *Favoritos* e *Para você*. Na aba Buscar: caixa de texto com sugestões enquanto se digita (as buscas anteriores que combinam vêm primeiro, marcadas com ↺, depois as do servidor), filtros de tipo, categoria (montada com as categorias que a busca achou) e ordem, lista com miniaturas, paginação e, com a caixa vazia, os lugares populares seguidos das novidades. Na aba Para você: campo de interesses (separados por vírgula) e as recomendações. A janela só fala com a API por `SearchApi` (caminhos e leitura das respostas, sem nós, em `SearchModels.cs`); respostas ilegíveis viram listas vazias, nunca erro.

**Miniaturas:** vêm do `thumbnailUrl` do resultado (ilhas e produtos têm imagem), buscadas **uma de cada vez** e guardadas em memória; só `http(s)` sem usuário/senha na URL, até 2 MB e 4096 px, formatos PNG/JPEG/WebP. Sem imagem, aparece o ícone do tipo.

**Prévia da localização:** ao escolher um resultado, o painel mostra imagem, nome, tipo, descrição e onde fica — "Nesta ilha · x 12, z -7" ou "Em outra ilha · …". Não há mini-mapa desenhado: a busca não devolve o tamanho da ilha, só o ponto; um mini-mapa fica como melhoria.

**Teleporte pelo resultado:** na mesma ilha vai ao terreno (pelo teleporte de terrenos, que respeita as regras do terreno) ou ao ponto; em outra ilha entra na ilha e, **ao chegar**, vai até o ponto (`SearchTravel` guarda o ponto durante a troca de ilha; se a pessoa for para outra ilha, ele é esquecido). Pessoas, grupos e produtos não têm lugar: o botão diz "Sem lugar para visitar". Duplo clique também vai.

**Denúncia de resultado** (`POST /api/v1/search/report/{kind}/{id}`, motivo de 3 a 500 letras): só se denuncia o que a pessoa pode ver e que não é dela; a mesma pessoa só denuncia o mesmo resultado uma vez; no máximo 20 denúncias por dia por pessoa. Quando **5 pessoas diferentes** denunciam o mesmo resultado, ele sai da busca até alguém da operação decidir (isso só tira da busca, não apaga nada). Operação (chave de serviço): `GET /api/v1/platform/search/reports` lista o que espera decisão (mais denunciados primeiro, com os motivos); `POST /api/v1/platform/search/reports/{kind}/{id}/resolve` com `{"action":"hide"}` mantém escondido ou `{"action":"dismiss"}` devolve à busca. Atenção: com poucos números a denúncia coordenada pode esconder algo injustamente; por isso a decisão final é humana e reversível.

**Métricas de pesquisa** (sem nomes): `SearchMetrics` conta por dia e por texto buscado (normalizado, só buscas de 2+ letras, no máximo 5.000 textos diferentes em memória): buscas, buscas sem resultado, resultados abertos, visitas e tempo total; grava no banco em lotes (a cada 30 s junto do indexador) em vez de escrever a cada busca. Só a primeira página conta como busca. O cliente avisa "abriu" e "foi até lá" em `POST /api/v1/search/events`. Operação: `GET /api/v1/platform/search/metrics?days=7` devolve totais, tempo médio, as buscas mais feitas e **as que não acharam nada** (o melhor sinal do que falta no jogo).

**Privacidade das métricas e do histórico:** as métricas não guardam quem buscou. O histórico de buscas de cada pessoa fica **só no computador dela** (`user://search_history.json`); o servidor nunca recebe essa lista.

**Testes:** `SearchPhase3Tests` (10: denúncia uma vez e com motivo, só o visível e não o próprio, 5 pessoas escondem e o índice não traz de volta, a operação mantém ou devolve, limite diário, contagem por dia/texto em lotes, relatório com mais buscados e sem resultado, enxurrada de textos), um teste no PostgreSQL real (favoritos, interesses, denúncia e métricas) e `SearchSmoke` no Godot (pretende ser o servidor: busca e caminho enviado, filtros, página, sugestões e histórico entre sessões, prévia, teleporte na mesma ilha / terreno / outra ilha com o ponto guardado, favoritar e desfavoritar, lista cheia, denúncia, abas, interesses, servidor fora do ar, respostas quebradas).

## Busca global — fase 4: testes finais e avaliação do motor externo

**Relevância (conjunto de buscas com resposta conhecida).** `SearchFinalTests` tem 15 buscas com o resultado esperado em primeiro lugar (nome igual, começo do nome, palavra no meio do nome, sem acento, maiúsculas, nome mais forte que descrição, mais novo desempata) contra um conjunto de ilhas com "iscas" muito populares. O teste mede a posição de cada resposta (MRR) e exige **1,000**: todas em primeiro. Ele achou um defeito real: uma ilha popular onde a palavra aparece no meio do nome ("Trilha da Floresta") passava na frente de uma cujo nome **começa** com a palavra ("Floresta Encantada"). Agora os degraus do título (igual 85, começa 60, palavra no meio 37) são maiores do que tudo o que popularidade (até 15) e novidade (até 5) conseguem somar, então **popularidade só desempata entre acertos do mesmo nível, nunca vira um acerto pior em melhor**. Todas as palavras precisam estar; meio de palavra não conta.

**Permissões (varredura de vazamento).** Um mundo com mais de 20 coisas proibidas, todas com a mesma palavra buscada e todas muito populares (para que o ranking não seja o que as esconde): ilha privada, apagada, de quem bloqueei, de quem me bloqueou, moderada; terreno privado e terreno em ilha fechada; grupo privado, apagado e de quem bloqueei; produto parado e de quem bloqueei; evento de grupo, já acabado e em ilha fechada; ponto privado e em ilha fechada; pessoas com perfil privado, fora da busca, do sistema, excluída, suspensa, bloqueada nos dois sentidos. O teste confere que **nada disso aparece** na busca (todas as páginas e as três ordens), nas sugestões, em populares, em recentes, nas recomendações, que **não dá para favoritar nem denunciar**, e que um favorito feito antes de fechar não é listado. Há ainda testes de que ninguém encontra a si mesmo e de bloqueio nos dois sentidos para pessoas.

**Textos estranhos.** Vazio, uma letra, `'; DROP TABLE users;--`, `%`, `_`, `\`, operadores de busca (`&`, `|`, `!`, `:*`), 5.000 letras, caractere nulo, emoji, quebras de linha e 200 palavras, mais página negativa e `pageSize` 9999: nenhum quebra e os limites valem.

**Desempenho e muitas pessoas ao mesmo tempo.**
- Sem banco (memória): 3.000 ilhas indexadas em ~0,4 s; uma passada sem novidades escreve zero linhas (~0,2 s); 20 buscas em ~0,13 s; 40 pessoas × 5 rodadas simultâneas, cada uma com seu contexto como no servidor, todas com a resposta certa (~0,3 s).
- PostgreSQL 18 de verdade, 8.000 coisas reais (5.000 ilhas, 3.000 grupos, parte privada) com a conferência completa de privacidade: indexar tudo ~5,5 s; passada sem novidades ~1,1 s; **30 pessoas simultâneas, 300 chamadas (busca + sugestão): mediana 29 ms, p95 165 ms, máximo 294 ms**; nenhum item privado em nenhuma página. Com 50.000 linhas só no índice, buscar candidatos leva ~10 ms e o índice GIN responde.

**Decisão sobre motor externo (Elasticsearch, Meilisearch…): continua não.** Os números acima sustentam a escala atual com folga e a busca fica no PostgreSQL, o que mantém **um só lugar** onde a privacidade é aplicada (um motor externo seria uma segunda cópia a proteger, manter em dia e remover quando algo vira privado). Reavaliar se: (1) o índice passar de alguns milhões de linhas ou o p95 passar de ~500 ms sob carga real; (2) "sem resultado" nas métricas mostrar muita busca com erro de digitação, caso em que o primeiro passo é a extensão `pg_trgm` do próprio PostgreSQL, ainda sem serviço novo; (3) surgir pedido de busca por significado (sinônimos, "praia" achando "litoral"), que aí sim justifica um motor próprio. O endpoint de métricas (`/api/v1/platform/search/metrics`) mostra os dois primeiros sinais.

## Limites conhecidos
- "Locais recentes" são lugares criados ou alterados há pouco; o jogo só guarda a última posição de cada pessoa, não um histórico de visitas.
- A prévia mostra o ponto em texto, não um mini-mapa.
- A denúncia coordenada de 5 contas pode esconder algo injustamente até a operação decidir; a decisão é humana e reversível.
- O teleporte para outra ilha depende do servidor da ilha aceitar a posição pedida (as regras do terreno valem); isso foi testado no cliente com um servidor de mentira, não com um servidor de ilha de verdade.
