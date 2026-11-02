# Fase 27 — Comércio avançado

## Entregue

- Lojas persistentes vinculadas a ilhas e produtos do marketplace, com nome de vitrine e objeto opcional.
- Listagem pública de lojas somente com produtos ativos; criação/exclusão restrita ao proprietário da ilha.
- Marketplace já suporta pesquisa, categoria, compra transacional e idempotência por chave.
- Compras entregam item ao inventário dentro da mesma transação do livro-razão, evitando saldo sem entrega.
- Carteira, extrato, transferências e limites existentes permanecem protegidos por transações e chaves idempotentes.
- Migration `Phase27AdvancedCommerce` criada para `marketplace.world_stores`.

## Limitações conhecidas

- Favoritos, avaliações moderadas, página pública do criador e planos de aluguel de parcelas ainda precisam de entidades/endpoints dedicados.
- Avisos de vencimento e recuperação de aluguel ainda dependem de um job de cobrança; o período de recuperação já é persistido no aluguel.
- UI visual de vitrine e mensagens de compra em processamento precisam ser conectadas no cliente Godot.
