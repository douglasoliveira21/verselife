# Fase 19 — Inventário avançado

## Entregue

- Inventário paginado (até 100 itens por página), pesquisa por nome, filtro por categoria e resposta com metadados de versão, tamanho, criador, proprietário e datas.
- Favoritos persistentes com filtro e índice dedicado.
- Lixeira lógica e restauração de itens.
- Pastas existentes com listagem, criação, renomeação, movimentação e exclusão lógica.
- Cliente Godot com filtros Todos/Favoritos/Lixeira, seleção, importação, colocação no mundo, equipamento, favoritar e restaurar.
- Permissões continuam sendo avaliadas exclusivamente pela API para copiar, baixar, equipar e colocar conteúdo.
- Migration `20261102205825_Phase19AdvancedInventory` adiciona `inventory.items.is_favorite` sem perder dados existentes.

## Limitações conhecidas

- A interface atual ainda usa seleção por árvore; drag-and-drop visual, miniaturas remotas, paginação incremental e operações em lote permanecem pendentes.
- Outfits avançados dependem do editor de avatar e dos itens equipados já existentes; o inventário não altera permissões no cliente.

## Verificação

`backend`, `client` e `island-server` compilados com .NET SDK disponível, sem erros ou avisos. A migration deve ser aplicada pelo pipeline normal (`dotnet ef database update` ou inicialização configurada da API) antes de ativar os filtros de favoritos.
