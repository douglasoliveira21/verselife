# Administração e operações — Fase 11

O painel fica em `/admin` e consome somente endpoints administrativos. Cada chamada exige JWT, UUID presente em `LIFEVERSE_ADMIN_USER_IDS` e o segredo MFA injetado em `LIFEVERSE_ADMIN_MFA_TOKEN` no header `X-Admin-MFA`. Sem essa configuração, o acesso é negado.

O dashboard consulta usuários, sessões ativas, ilhas, instâncias, objetos, assets e denúncias. Suspensão, banimento, reativação, revogação de sessões, suspensão de ilhas e decisões de moderação geram registros em `platform.admin_audits`/`platform.moderation_cases`.

Cada resposta recebe `X-Correlation-ID`; logs da API devem preservá-lo. Health check existente cobre PostgreSQL. Métricas de CPU/memória/tráfego devem ser coletadas no host/container (Prometheus/OTel) e não são falsificadas pelo painel.

## Backup e recuperação

Produção deve executar `pg_dump --format=custom` com criptografia e retenção definida, exportar o bucket MinIO versionado e guardar `.env`/configuração em cofre de segredos. O backup deve ser validado por checksum e restaurado periodicamente em ambiente isolado antes de ser considerado recuperável. Nunca usar o painel para executar SQL arbitrário.


## Painel de administração completo (`/admin`)

Página única em `https://<api>/admin` (a página não guarda segredo: você digita o **token de acesso** da conta administradora, que vale 15 minutos, e o **token MFA** do servidor; os dois ficam só na memória). Acesso: id da conta em `LIFEVERSE_ADMIN_USER_IDS` + `LIFEVERSE_ADMIN_MFA_TOKEN` (header `X-Admin-MFA`). Tudo que muda algo pede **motivo** e deixa registro de auditoria; nenhuma senha ou hash aparece em resposta ou log.

- **Dashboard** (`GET /api/v1/admin/panel/overview`): usuários (total, ativos, bloqueados, online, sessões abertas, novos em 24 h/7/30 dias, logins), gráficos de 14 dias (cadastros, logins, transações), mundo (ilhas, servidores, objetos, assets, itens, produtos), social (grupos, amizades, denúncias), economia (VC$ nas carteiras, volume, pedidos pendentes), maiores saldos e últimas ações da administração.
- **Usuários** (`/panel/users`, busca por nome ou e-mail, filtro por situação, paginação) e **detalhe** (`/panel/users/{id}`): resumo, **bloquear / banir** (`/block`, desconecta a pessoa na hora) e **desbloquear** (`/unblock`), **trocar senha** (`/password`, mesma regra do cadastro, encerra todas as sessões), **encerrar sessões**, **dinheiro** (saldo, **enviar** e **retirar** VC$ pelo ajuste auditado, bloquear carteira, limite diário, movimentações), **inventário** (`/inventory`), **amigos** (`/friends`: amigos, pedidos, bloqueados), **grupos** (`/groups`), **sessões e logins** (IP e aparelho).
- **Grupos** (`/panel/groups`, `/panel/groups/{id}` com membros e cargos).
- **Logs** (`/panel/logs?kind=admin|logins|economy`, filtros por pessoa, texto e período).
- **Ilhas** (suspender e reativar), **Moderação** (decidir denúncias) e **Economia** (leva para `/admin/economia`).

Ninguém bloqueia a si mesmo. Enviar dinheiro cria a carteira se a pessoa ainda não tinha e manda um aviso a ela ("Crédito da administração").
