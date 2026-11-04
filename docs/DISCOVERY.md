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

**Ordem e limites.** A relevância é calculada em C# (igual em qualquer banco): título igual à busca > começa com > tem a frase > tem todas as palavras; mais o quanto cada palavra está no título; um pouco de popularidade (com teto) e de novidade. O nome exato vence qualquer popularidade. Todas as palavras precisam estar (como começo de palavra); a busca olha de 2 caracteres para cima, até 8 palavras de 32 letras e 100 caracteres. O banco devolve até 400 candidatos (ordenados por `ts_rank` e popularidade) e a página (até 50, padrão 20) sai deles. Tipos: `types=island,group`; nomes desconhecidos são ignorados. A consulta ao banco usa só letras e números, então nada digitado muda a consulta.

## Foi preciso um motor externo (Elasticsearch, Meilisearch…)?
**Não, por ora.** Medido num PostgreSQL 18 real com 50.000 linhas indexadas, buscar candidatos leva cerca de **11 ms**, e o índice GIN responde a consulta de começo de palavra. O que um motor externo daria a mais — tolerância a erro de digitação, sinônimos, ordenação por semelhança — não é necessário para nomes de ilhas, grupos e pessoas nesta escala, e custaria outro serviço para operar, manter em dia e proteger (privacidade em dois lugares). Reavaliar se passar de alguns milhões de linhas, ou se a tolerância a erros de digitação virar pedido real (aí o primeiro passo é a extensão `pg_trgm`, que ainda é PostgreSQL).

## Testes
`SearchTextTests` e `GlobalSearchTests` (47): normalização, termos, consulta sem injeção, relevância (exato > começa > palavra > só no texto; popularidade e novidade só desempatam), os oito tipos indexados com seu lugar, nada privado/apagado/de grupo/encerrado indexado, esconder na hora sem esperar o índice, bloqueios nos dois sentidos, moderação, atualização incremental (só o que mudou; reconstruir dá o mesmo), coisas que terminam ou somem, terrenos e pontos que seguem a ilha, nome novo da pessoa, acentos e começos de palavra, todas as palavras, consultas esquisitas, tipos, paginação estável e completa, autocomplete, populares e recentes, e velocidade (1.500 ilhas indexadas e buscadas em segundos no teste sem banco). `SearchPostgresTests` (2, só com `LIFEVERSE_TEST_PG`): o mesmo no PostgreSQL de verdade — coluna e índice existem, começos de palavra, sem acento, a consulta maliciosa não faz nada, o vetor acompanha o texto, e 50.000 linhas (candidatos em ~11 ms, índice GIN usável). Com essas rodadas, as migrações de todas as fases foram aplicadas do zero num PostgreSQL de verdade.

## Ainda não (próximas fases)
Filtros por categoria, ordenação por popularidade e por atualização, favoritos, histórico local, recomendações por interesses, janela de busca com miniaturas e prévia, teleporte pelo resultado, denúncia de resultado, métricas.
