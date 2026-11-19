# Migração para Unity 6 HDRP — Plano de testes

Regra: uma funcionalidade só é **Concluída** quando compila, os testes abaixo passam, o comportamento original foi preservado, a integração funciona e não há regressão crítica conhecida. Teste que não pôde ser executado fica **Pendente**, com o motivo; nunca é marcado como passou.

## 1. Camadas

| Camada | O que cobre | Ferramenta | Estado hoje |
|---|---|---|---|
| T1 Regras compartilhadas | `shared`, `scripting`, backend | xUnit (existente, 1.842 testes) e Test Runner da Unity para os mesmos casos | xUnit passa; Unity pendente (sem projeto) |
| T2 Contrato de rede | Ida e volta das 44 mensagens do LifeVerse Wire; payloads de `Snapshot`/`VehicleSnapshots`/voz **byte a byte iguais** aos do Godot; mensagens malformadas; limites de tamanho | xUnit (`tests/LifeVerse.Wire.Tests`, 25 testes) + testes de modo Edit na Unity | **Passa** para o contrato e a rede em loopback com perda de pacotes; falta o payload de voz e de veículo contra amostras reais do Godot |
| T3 Integração com a API | Login, refresh, ticket de entrada, inventário, economia, terrenos, objetos | Testes de modo Play contra uma API local (`docker compose`/`scripts/dev-up.ps1`) | Pendente |
| T4 Multiplayer | 2 ou mais clientes, sincronia de posição e de objetos, reconexão, latência, perda de pacotes, reinício de servidor | Clientes automatizados; emulação de rede (atraso e perda) | Pendente |
| T5 Funcional do cliente | Cenários de cada item do checklist | Testes de modo Play, com roteiro manual quando a automação não alcançar | Pendente |
| T6 Gráfico | Equivalência visual e desempenho | Capturas com câmera fixa, comparação perceptual, métricas | Pendente |
| T7 Segurança | Autoridade do servidor, permissões, limites | Testes e fuzzing | Parcial: as regras existentes já têm xUnit |
| T8 Distribuição | Instalador, atualização por cima, launcher | Máquina limpa | Pendente |

## 2. Testes funcionais mínimos (itens da lista do prompt)

Login · entrada em ilha · spawn · movimento (andar, correr, pular, voar, nadar, sentar) · câmeras (terceira, primeira, livre, construção) · chat local, privado e de grupo · inventário (pastas, itens, lixeira, transferência) · construção (criar, mover, girar, escalar, copiar, vincular, desfazer/refazer, materiais, física, clique) · edição de objetos e permissões · teleporte · animações e gestos · anexos em ossos · grupos · economia (saldo, compra, transferência, marketplace, aluguel) · scripts Lua (eventos, timers, mídia, NPC, limites) · terrenos (edição, desfazer, importação de mapa de alturas) · persistência após reinício.

## 3. Multiplayer

| Cenário | Critério |
|---|---|
| 2 jogadores na mesma ilha | Posição, estado de movimento, gestos e chat visíveis nos dois |
| 64 clientes simulados | O servidor mantém o tick e respeita o raio de interesse |
| Latência de 150 ms e perda de 5% | Sem estado impossível; reconciliação do jogador local converge |
| Queda de um cliente | Avatar suspenso e removido no prazo; reconexão restabelece o estado |
| Reinício do servidor | Cliente reconecta; objetos e terreno persistidos intactos |
| Mensagens malformadas e fora de ordem | Ignoradas ou rejeitadas sem derrubar o servidor |
| Entrada com versão de protocolo diferente | Recusa com a mensagem de incompatibilidade |

## 4. Economia e integridade (VerseCoin)
Saldo e histórico só mudam pela API. Testes: idempotência (mesma chave não duplica), concorrência (duas operações no mesmo saldo), saldo negativo recusado, ajuste administrativo auditado. Os testes existentes do backend continuam sendo a referência; o cliente Unity apenas não pode exibir ou enviar saldos que não vieram da API. Nenhum saldo existente é recriado ou zerado.

## 5. Equivalência gráfica

Cenas de referência (as mesmas nos dois clientes): praia e oceano ao nascer do sol, terreno com camadas, interior com luzes pontuais, avatar com roupa e anexos, construção com materiais por face e mídia, noite com chuva.
1. Fixar câmera, hora, clima, resolução (1920×1080) e semente.
2. Capturar no Godot e na Unity; comparar por diferença perceptual e inspeção lado a lado; registrar as diferenças aceitas e as corrigidas.
3. Medir em cada preset: FPS médio e 1% pior, tempo de quadro (CPU e GPU), uso de RAM e VRAM, *draw calls*, tempo de carregamento da ilha.
4. Presets **Baixo, Médio, Alto e Ultra** só existem quando cada um tem recursos realmente suportados e medidos; ray tracing só em hardware compatível.

## 6. Equivalência dos 117 testes de fumaça do Godot
Cada `client/Tests/<Nome>Smoke` ganha um teste de modo Play equivalente `LifeVerse.Tests.<Nome>`. Uma planilha de correspondência (a criar na Fase 4) marca cada um como Portado, Substituído por teste de unidade ou Descartado com justificativa. Nenhum módulo muda para Concluído com os smokes do Godot sem equivalente.

## 7. Critério para registrar resultado
Todo resultado vai para `MIGRATION_PROGRESS.md` com: comando ou roteiro, versão do commit, data, resultado e, se falhou, a causa. Ambiente indisponível (sem Unity, sem GPU, sem servidor Linux) é registrado como **Pendente** com o motivo.
