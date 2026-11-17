# Economia e marketplace — Fase 10

LV Coins são inteiros (`long`) e permanecem uma moeda virtual fechada. Carteiras, transações, produtos, pedidos e aluguéis ficam persistidos nos schemas `economy` e `marketplace`.

Compras e transferências usam chave de idempotência, transação PostgreSQL atômica, validação do preço atual, saldo não negativo e entrega no inventário somente dentro da mesma unidade transacional. O cliente nunca informa saldo; ele apenas solicita uma operação.

Produtos só podem ser publicados pelo proprietário do asset. O pedido registra comprador, vendedor, preço, taxa de 5%, transação e item entregue. Produtos desativados não podem ser comprados.

Planos de aluguel controlam tamanho, objetos, capacidade e duração. O vencimento preserva o conteúdo durante sete dias de recuperação; suspensão/exportação podem ser ligados ao job administrativo.

O painel Godot exibe saldo e catálogo. Ajustes administrativos, reconciliação e relatórios de circulação devem ser operados por uma superfície administrativa protegida, não por Lua ou pelo cliente.

## VerseCoin (VC$) — carteira e compra de moeda

A moeda se chama **VerseCoin** (símbolo **VC$**; no código e no banco continua `coins`). O que já existia — carteiras, livro-razão de partidas dobradas, transferências, marketplace, aluguéis, terrenos, caixa de grupos e pagamentos por script — segue valendo; só os textos do jogo mudaram de "L$ / LV Coins" para VC$.

- **Janela Carteira** (`client/Scripts/WalletWindow.cs`): menu lateral (Comprar VerseCoin, Meu Saldo, Transferir VerseCoin, Histórico de Transações, Ajuda). Abre pelo botão **Comprar VC$** (página de compra), pelo saldo da barra superior (Meu Saldo) ou pelo menu da barra. O saldo é sempre o do servidor; o cliente só pede de novo depois de cada operação.
- **Pacotes** (`GET /api/v1/economy/packages`, `backend/Modules/Economy/CoinPackages.cs`): lista padrão (500 / 1.000 / 2.500 +10% / 5.000 +15% / 10.000 +20%) ou a de `Economy:CoinPackages` na configuração (preço em centavos, bônus em %, validada; se inválida volta ao padrão).
- **Pedido de compra** (`POST /api/v1/economy/coin-orders`): valida pacote, forma de pagamento e chave de idempotência. **Ainda não há provedor de pagamento ligado**: sem `Economy:Payments:Provider` o servidor responde 503 `payments_not_configured` e nada é cobrado nem creditado. Falta: gateways (Mercado Pago, Asaas, PagBank), webhook autenticado e idempotente, crédito pelo livro-razão a partir de uma conta técnica de emissão, estornos e conciliação.
- Ainda não existem: pedido de pagamento/aprovação de despesas de grupo além do que há, aluguel recorrente com autorização, painel administrativo "Economia VerseCoin", antifraude e limites por carteira.
