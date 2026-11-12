# Gerenciamento de Terrenos (janela da equipe da ilha)

Abre em **Mundo → Gerenciar terrenos…**. Só o dono e os administradores da ilha (o servidor recusa os outros com 403).

## O que existe

| Seção | O que mostra / faz |
|---|---|
| **Visão Geral** | Seis indicadores (área total, parcelada, disponível, parcelas vendidas, alugadas, receita mensal), o mapa dos terrenos e os detalhes do terreno escolhido |
| **Mapa de Parcelas** | O mesmo mapa com camadas (grade, rótulos, imagem da ilha), zoom (roda do mouse), mover (botão direito ou do meio), clique escolhe um terreno (Ctrl/Shift soma) |
| **Demarcação** | Marcar quadrados de 4 × 4 m (Demarcar/Reduzir, desfazer/refazer), ver área, dimensões, conflitos e cota de prims, **Criar parcela**, **Dividir**, **Unir** |
| **Lista de Terrenos** | Tabela com busca e filtros (disponíveis, vendidos, alugados, à venda, comuns, residenciais, comerciais) |
| **Vendas** | Anúncios ativos (criar, mudar preço, retirar) e histórico de vendas |
| **Aluguéis** | Ofertas (criar, retirar) e contratos em andamento (encerrar) |
| **Contratos** | Aluguéis em andamento e os encerrados nos últimos 90 dias |
| **Prims e Recursos** | Limite de objetos da ilha, quanto foi repartido, quanto está em uso e o uso de cada terreno |
| **Permissões** | Acesso e construção de cada terreno; “Configurar” abre a janela “Sobre o terreno” |
| **Histórico** | Mudanças e movimentos de dinheiro dos terrenos (os 200 mais recentes) |

Cores do mapa: verde disponível, laranja à venda, roxo alugado, cinza vendido, azul área comum.

## Como é calculado (tudo no servidor, `LandOverview`)

- **Disponível**: terreno do dono da ilha, sem aluguel e sem oferta de venda. **Vendido**: terreno de outra pessoa. **Alugado**: tem contrato ativo ou em tolerância. **À venda**: com preço. **Área comum**: finalidade “comum”.
- **Área livre** = área da ilha − soma das áreas dos terrenos.
- **Receita mensal**: aluguéis ativos cujo locador é o dono da ilha, convertidos para 30 dias.
- **Código** (L-01, L-02…): dado na primeira leitura, na ordem de criação, e nunca reaproveitado.
- **Finalidade** (residencial, comercial, mista, social, comum): nova coluna `category`; o dono do terreno e a equipe podem mudar.

## API

- `GET  /api/v1/worlds/islands/{id}/land/overview` — equipe da ilha.
- `PUT  /api/v1/worlds/islands/{id}/land/parcels/{parcelId}/category` — dono do terreno ou equipe.
- Venda, aluguel, dividir, unir e criar usam os endpoints que já existiam (`/land/...`).
- Migração: `Phase76ParcelCategoryAndCode` (`parcels.category`, `parcels.code`).

## Limites desta entrega

- **Renovar** aluguel é do locatário (o servidor só aceita dele); a tela do locador não tem esse botão.
- **Inspecionar** e **Configurar** abrem a janela “Sobre o terreno” que já existia; o redesenho dela, o Mercado Imobiliário, Minhas Propriedades e a venda de ilhas inteiras são as próximas fases.
- A imagem dos detalhes é a capa da ilha (terrenos ainda não têm imagem própria).
- Vender, alugar, dividir e unir valem para terrenos **do próprio usuário**; num terreno de outra pessoa a equipe só muda a finalidade.
- A API de produção precisa ser publicada (migração incluída) para a janela funcionar.
