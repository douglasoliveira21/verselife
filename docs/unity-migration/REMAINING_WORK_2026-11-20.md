# Verificação das pendências — 20/11/2026

> **Documento histórico** (marcado em 26/11/2026): as lacunas funcionais listadas aqui foram feitas depois. Para o que ainda está aberto, vale [FALTA_FAZER.md](FALTA_FAZER.md).

## Resultados executados nesta revisão

- API: 2.562 aprovados, 39 ignorados, zero falhas (2.601 no total).
- LifeVerse Wire: 25 aprovados, zero falhas.
- Núcleo do servidor Unity: 51 aprovados, zero falhas.
- `git diff --check`: aprovado.
- Evidência local da API: `tests/LifeVerse.Api.Tests/TestResults/remaining-verified.trx` (ignorado no Git).

## Correções

- Manual Lua regenerado pelo teste oficial a partir do catálogo.
- Teste de pulo do hemisfério começa na superfície real (7,5 m no cenário), evitando pedir pulo enquanto o avatar ainda cai. Nenhuma geometria de produção foi modificada.
- Classes que alteram o limite global de anexos usam a mesma coleção xUnit para impedir interferência entre testes.
- Teste do supervisor usa o host .NET em vez do testhost e aguarda a saída assíncrona por uma condição com prazo.
- Testes de busca e de orçamento Lua executam sem competir com outros testes de carga. Limites de produção não foram modificados.

## Trabalho ainda não concluído

- Os 39 testes PostgreSQL exigem `LIFEVERSE_TEST_PG` apontando para um banco de testes; não executar contra produção.
- Unity 6000.6.9f1 está instalado, mas as variações Windows IL2CPP não estão presentes no diretório do editor. Compilador C++ ainda não verificado.
- Testes Unity EditMode, player, voz real, instalador e comparação visual não foram reexecutados nesta revisão.
- Implantação do backend e servidor Unity, segredos CI, roteamento por engine/protocolo e conexão a uma ilha real ainda exigem execução e validação.
- Lacunas funcionais do resumo original (gizmo de anexos, anexos de imagem/construção, central de notificações, janela de voz e aparência no mundo) continuam pendentes de implementação/validação.
- Sem commit, push ou implantação nesta revisão. Não declarar a migração integralmente concluída com base apenas na suíte .NET.
