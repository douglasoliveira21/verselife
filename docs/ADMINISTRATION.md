# Administração e operações — Fase 11

O painel fica em `/admin` e consome somente endpoints administrativos. Cada chamada exige JWT, UUID presente em `LIFEVERSE_ADMIN_USER_IDS` e o segredo MFA injetado em `LIFEVERSE_ADMIN_MFA_TOKEN` no header `X-Admin-MFA`. Sem essa configuração, o acesso é negado.

O dashboard consulta usuários, sessões ativas, ilhas, instâncias, objetos, assets e denúncias. Suspensão, banimento, reativação, revogação de sessões, suspensão de ilhas e decisões de moderação geram registros em `platform.admin_audits`/`platform.moderation_cases`.

Cada resposta recebe `X-Correlation-ID`; logs da API devem preservá-lo. Health check existente cobre PostgreSQL. Métricas de CPU/memória/tráfego devem ser coletadas no host/container (Prometheus/OTel) e não são falsificadas pelo painel.

## Backup e recuperação

Produção deve executar `pg_dump --format=custom` com criptografia e retenção definida, exportar o bucket MinIO versionado e guardar `.env`/configuração em cofre de segredos. O backup deve ser validado por checksum e restaurado periodicamente em ambiente isolado antes de ser considerado recuperável. Nunca usar o painel para executar SQL arbitrário.
