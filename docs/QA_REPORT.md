# Auditoria técnica e regressão — LifeVerse

- **Data:** 2026-11-02
- **Versão identificada:** 0.6.0 (`backend/LifeVerse.Api.csproj`)
- **Fase auditada:** Fase 12 e regressão das Fases 01–12
- **Revisor:** auditor técnico independente (execução local)

## Testes executados

| Verificação | Resultado | Observação |
|---|---|---|
| Estrutura do monorepo | PASSOU | `client`, `island-server`, `backend`, `shared`, `infrastructure`, `tests`, `docs` presentes |
| Referências de cenas/autoloads | PASSOU | Scripts referenciados por `project.godot` e cenas existentes |
| Build API | PASSOU | Debug, 0 erros/avisos |
| Build cliente Godot C# | PASSOU | Debug, 0 erros/avisos |
| Build island-server | PASSOU | Debug, 0 erros/avisos |
| Build assembly de testes | PASSOU | Debug, 0 erros/avisos |
| Pipeline Release | PASSOU | `scripts/build-release.ps1` gerou `artifacts/0.6.0` e manifesto SHA-256 |
| Migrations | PASSOU | `dotnet ef migrations script --idempotent`; nenhum pending model change |
| Godot headless editor scan | PASSOU COM RESSALVA | Projeto escaneou e carregou classes; erros de diretório `user://` por permissão do ambiente |
| Test runner xUnit | NÃO VERIFICADO | `dotnet test` iniciou, mas não terminou no smoke window e foi interrompido; não inventar contagem |
| Docker Compose/PostgreSQL | NÃO VERIFICADO | Docker ausente; `pg_isready` sem resposta em localhost:5432 |
| Export/instalação/atualização Windows | NÃO VERIFICADO | Templates de exportação, Inno Setup e máquina Windows de instalação não disponíveis |
| Lua worker real | NÃO VERIFICADO | Executável Lua 5.4 não instalado; serviço falha fechado |
| LiveKit real | NÃO VERIFICADO | Servidor/credenciais oficiais não configurados |

## Problemas encontrados e correções

### ALTO — suspensão administrativa não bloqueava nova autenticação

`LoginAsync` e `RefreshAsync` não verificavam `User.Status`. Corrigido para aceitar somente `active`; refresh também exige usuário e sessão válidos.

### ALTO — access tokens continuavam válidos após revogação

JWTs stateless não consultavam a sessão revogada. Adicionado middleware pós-autenticação que valida `sid`, sessão, expiração e status do usuário em cada requisição.

### ALTO — migração criava usuários existentes com status vazio

`Phase11Administration` usava default `""`. Corrigido para `defaultValue: "active"`, evitando lockout de contas existentes.

### ALTO — credencial de desenvolvimento no appsettings base

Senha `development-only` e endpoint MinIO local estavam no arquivo base. Movidos para `appsettings.Development.json`; produção exige `ConnectionStrings:Postgres` fornecida pelo ambiente.

### BAIXO — artefatos `.uid` gerados durante auditoria

Novos `.uid` passaram a ser ignorados; arquivos rastreados foram restaurados sem alteração de conteúdo.

## Pendências e riscos

- Docker/PostgreSQL indisponíveis impedem confirmar persistência, restart, migrations aplicadas e smoke auth.
- O runner de testes não concluiu; a suíte está compilável, mas a execução precisa ser repetida em ambiente com IPC/test host funcional.
- O painel administrativo mede contagens de domínio; CPU, memória, GPU, VRAM, tráfego e alertas dependem de coleta host/container (Prometheus/OpenTelemetry).
- O launcher e instalador estão preparados, mas não houve instalação/upgrade real em Windows.
- Lua, LiveKit e transferência ENet oficial continuam contratos não verificados sem os serviços correspondentes.

## Classificação final

- **Críticos sem resolução:** nenhum reproduzido.
- **Altos sem resolução:** nenhum de código reproduzido; permanecem bloqueios de infraestrutura e de testes de distribuição.
- **Médios:** execução do runner, observabilidade de host e integrações externas não verificadas.
- **Baixos:** limitações visuais/documentais do overlay e do painel.

## Recomendação de avanço

**Não liberar lançamento público.** A base compilável está apta para uma rodada de QA em ambiente provisionado, mas a próxima fase de distribuição só deve ser liberada após executar Docker/PostgreSQL, testes xUnit completos, exportação/instalação/atualização Windows, testes de carga, Lua worker, LiveKit e reconexão ENet com serviços reais.
