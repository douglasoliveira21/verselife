# Exploração e descoberta — Fase 09

O diretório global usa `/api/v1/discovery/islands` com pesquisa textual, categoria, ordenação (`popular`/`recent`), paginação e exposição somente de ilhas públicas ou próprias. Favoritos são persistidos por usuário.

O teletransporte cria uma reserva curta e chama o `WorldManager`; ilhas suspensas entram em `pending`, ilhas disponíveis em `reserved`, e a reserva expira em 30 segundos. Capacidade é verificada no servidor antes da transferência.

Landmarks, eventos com capacidade/inscrição e fotos com metadados JSON possuem endpoints autenticados e persistência no schema `discovery`. A captura de imagem no cliente pode salvar arquivo local; a publicação usa uma chave de armazenamento validada pela camada de assets.

O painel Mapa do cliente lista ilhas independentes e exibe um minimapa textual com posição, direção e POIs. A renderização cartográfica detalhada e o backend de presença em tempo real permanecem pontos de evolução visual.
