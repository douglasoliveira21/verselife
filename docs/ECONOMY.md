# Economia e marketplace — Fase 10

LV Coins são inteiros (`long`) e permanecem uma moeda virtual fechada. Carteiras, transações, produtos, pedidos e aluguéis ficam persistidos nos schemas `economy` e `marketplace`.

Compras e transferências usam chave de idempotência, transação PostgreSQL atômica, validação do preço atual, saldo não negativo e entrega no inventário somente dentro da mesma unidade transacional. O cliente nunca informa saldo; ele apenas solicita uma operação.

Produtos só podem ser publicados pelo proprietário do asset. O pedido registra comprador, vendedor, preço, taxa de 5%, transação e item entregue. Produtos desativados não podem ser comprados.

Planos de aluguel controlam tamanho, objetos, capacidade e duração. O vencimento preserva o conteúdo durante sete dias de recuperação; suspensão/exportação podem ser ligados ao job administrativo.

O painel Godot exibe saldo e catálogo. Ajustes administrativos, reconciliação e relatórios de circulação devem ser operados por uma superfície administrativa protegida, não por Lua ou pelo cliente.
