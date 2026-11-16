# Mapa do Mundo e mini-mapa

O mapa mostra só dados reais do servidor, na visão de quem está olhando. O cliente nunca decide quem pode entrar em algum lugar: ele pergunta, e o servidor responde.

## Atlas e coordenadas

- Cada ilha ocupa **uma vez** uma célula de uma grade quadrada (`Island.AtlasX/AtlasY`, índice único). As células são distribuídas em espiral a partir do centro, com a ilha de boas-vindas primeiro; célula = **768 m**, a ilha fica no centro dela.
- **Coordenadas globais** (atlas): metros, centro da célula. **Coordenadas locais**: metros a partir do centro da ilha, as mesmas do jogo. `MapCoordinates` converte de uma para a outra e lê/escreve "x, y" e "Nome/x/z".
- **Quadros** (tiles): PNG 256×256 gerado no servidor a partir do terreno salvo e das construções (`MapRenderer`, `Png`), guardado em `discovery.map_tiles` com versão/ETag e refeito no máximo uma vez por minuto. O cliente (`MapTileCache`) guarda na memória (LRU), em disco (por versão), lembra por 5 min as ilhas sem quadro e baixa 3 por vez. Só há quadro para ilhas com terreno salvo.
- Visibilidade (`MapService.Visible`): ilha pública e `ShowOnMap`, ou do próprio espectador, ou com permissão; nunca apagada nem de alguém bloqueado. O acesso vem como `open`, `full`, `closed`, `private` ou `banned`.

## API (`/api/v1/map`, limite "search" 240/min; escritas "announce" 20/min, com registro de auditoria)

| Rota | O que faz |
|---|---|
| `GET /atlas` | Ilhas do atlas (filtros: texto, categoria, classificação, células). Até 500 itens, com `truncated`. |
| `GET /islands/{id}/tile` | Quadro da ilha (PNG, ETag). |
| `GET /landmarks` | Painéis da coluna esquerda: pastas, marcos, favoritos, visita recente, ilhas de amigos e de grupos. |
| `GET/POST/PATCH/DELETE /landmarks…`, `POST/PATCH/DELETE /landmark-folders…` | Marcos (até 200) e pastas (até 20). Só nasce onde a pessoa pode entrar. |
| `GET /islands/{id}/parcels` | Terrenos da ilha com estado para o espectador (`mine`, `group`, `restricted`, `sale`, `rent`, `public`, `common`). Terreno restrito não revela nome, dono nem descrição. |
| `GET /people` | Amigos online que podem ser vistos: só aceitos, sem bloqueio, presença não privada, em ilha visível. Posição vem do relatório do servidor da ilha. |
| `POST /arrival` | Diz se aquele ponto pode ser alcançado e onde a pessoa chega (ilha aberta/cheia/fechada/privada/banida; terreno banido/restrito/sem teleporte; terreno com chegada forçada manda para o ponto dele). |

Migrações: `Phase97WorldMapAtlas` (colunas do atlas e `map_tiles`) e `Phase98MapPlaces` (marcos com descrição/pasta e `landmark_folders`). **Aplique antes de usar no servidor** (junto com as anteriores pendentes, `Phase92` em diante).

## Mapa do Mundo (cliente, `client/Scripts/Map`)

Abas **Mundo, Regiões, Terrenos, Pessoas, Grupos, Eventos**.

- **Coluna esquerda:** busca (nome, região, dono, "x, y", "Nome/x/z", com páginas e histórico), categorias (Destaques, Populares, Novas Ilhas, Residenciais, Comerciais, Eventos, Adulto 18+) e **Marcos** (Meus Marcos com pastas, Favoritos, Visita Recente, Ilhas de Amigos, Grupos, Adicionar Marco, Gerenciar).
- **Mapa:** arrastar, roda, clique, duplo clique, botão direito, setas, `+`/`−`, `Home`, `Esc`. Longe, as ilhas viram pontos agrupados com a quantidade. Perto aparecem nomes, selos (categoria, evento, destaque, favorito, sem acesso), marcos e amigos. O cartão da ilha tem **Teletransportar** e **Info**.
- **Mostrar Terrenos:** desenha os terrenos das ilhas grandes na tela, com legenda. Além da cor cada estado tem desenho próprio (restrito hachurado, alugar tracejado, ícones), então o mapa não depende só de cor.
- **Ponto dentro da ilha:** em zoom alto, clicar escolhe um ponto (e o terreno sob ele). "Teletransportar até aqui" chama `POST /arrival` e só então usa o teleporte do jogo (`MapHub.GoToPoint`: na mesma ilha move; em outra entra e depois move).
- **Adulto 18+:** o servidor ainda não verifica idade; a lista só aparece depois de a pessoa declarar que tem 18 anos ou mais (`MapSettings.ShowAdult`). O servidor devolve apenas a classificação.
- **Marcos:** sempre privados no cliente. "Copiar link" copia o link da ilha com o ponto (`lifeverse://island/{id}?x=&z=`); o campo `isPublic` já existe no servidor mas não tem botão.

## Mini-mapa (`client/Scripts/MiniMap.cs`)

Redondo, com N/S/L/O, **norte fixo** ou **girando com a câmera**, avatares próximos (amigos destacados, nomes opcionais), construções, zoom de 40 a 1000 m (roda e botões) e menu (marcar/cancelar destino, abrir o Mapa do Mundo, orientação, nomes, avatares, construções, copiar coordenadas). O **destino** marcado em qualquer um dos mapas aparece nos dois: pino dentro do círculo, seta na borda com a distância fora dele (inclusive para outra ilha), e é limpo ao chegar a menos de 6 m. A janela é a janela comum do jogo (redimensionável).

## O que os dois mapas dividem (`MapHub`)

Um único nó guarda a conversa com o servidor (`MapApiClient`), o atlas, os quadros, as preferências (`user://map.cfg`), o destino e a lista de amigos (lida a cada 30 s enquanto algum mapa estiver na tela). O jogo só injeta o que só ele sabe: ilha atual, posição e direção do jogador, entrar numa ilha, ir a um ponto, avisos, abrir links.

## Preferências

Estão em **Configurações > Mundo > Geral**: mini-mapa (gira com a câmera, alcance, nomes, pessoas, construções) e Mapa do Mundo (terrenos, amigos, nomes, eventos, favoritos, destaques). O armazenamento das configurações é a fonte; `map.cfg` é um espelho (inclui também a última visão do mapa). Mudar no mapa grava na janela de configurações e vice-versa. Ao mudar pelo mapa, o jogo aplica também outras mudanças pendentes da janela de configurações.

## Desempenho

Quadros em fila com no máximo 3 downloads e cancelamento do que saiu da tela; só se desenham ilhas visíveis; longe vira agrupamento em vez de quadro; terrenos só são pedidos (uma vez) para ilhas grandes na tela; busca de ilha por id em tabela; o mapa grande redesenha no máximo 30 vezes por segundo.

## Testes

- Backend: `WorldMapTests` (atlas, espiral, quadros, visibilidade) e `MapPlacesTests` (marcos, pastas, painéis, terrenos, amigos, chegada).
- Godot (sem janela): `MapBaseSmoke`, `WorldMapSmoke`, `WorldMapPlacesSmoke`, `MiniMapSmoke`. Capturas: `WorldMapCapture` (com `LIFEVERSE_CAPTURE_PATH`), nas resoluções 1280×720, 1920×1080, 2560×1440 e 3840×2160 em `artifacts/mapa/`.

## Limites conhecidos

- Sem verificação de idade no servidor.
- Terrenos usam a caixa que envolve o terreno; formas por células não são desenhadas.
- Quadros só existem para ilhas com terreno salvo; as demais mostram um quadrado neutro.
- Marcos não podem ser compartilhados como marco público (só o link da ilha com o ponto).
