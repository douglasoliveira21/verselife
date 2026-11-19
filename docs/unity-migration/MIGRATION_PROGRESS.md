# Migração para Unity 6 HDRP — Progresso

Última atualização: 2026-11-18. Não há percentuais: o progresso é contado sobre o inventário de `MIGRATION_CHECKLIST.md` (72 itens, grupos A a H) e sobre os critérios de aceite.

## Resumo

| Medida | Valor |
|---|---|
| Itens no checklist | 72 |
| Itens migrados e validados na Unity | **0** |
| Itens com a base técnica provada (A3, A4: `shared` e `scripting` rodando na Unity) | 2, ainda em validação |
| Itens que não precisam de migração (backend) | 1 (A1) |
| Fases concluídas | 1 (auditoria) |
| Fase em andamento | **3 (fundação)**: entregue e verificada, falta a revisão do gate abaixo |
| Decisões | D1, D2, D3 (LiteNetLib), D4 (Unity Dedicated Server) e D7 tomadas e executadas; o módulo Linux está instalado e o build do servidor foi verificado |
| Protocolo de rede | `LifeVerse.Wire` (protocolo 12): 44 mensagens, 25 testes xUnit e 2 testes na Unity passam; rodou num player Windows (Mono). Ainda não há servidor de ilha nem cliente de jogo usando o protocolo |
| Projeto Unity | Criado em `unity/UnityClient` |
| Código escrito para a Unity | 7 arquivos C# (488 linhas) em `unity/UnityClient/Assets/LifeVerse` e `unity/UnityPlugins`, mais o script `build-plugins.ps1` (60 linhas) |
| Arquivos do projeto Godot alterados | 0 |

## Ambiente verificado (2026-11-18)

| Item | Estado |
|---|---|
| Unity Editor | **6000.6.9f1** em `C:\Program Files\Unity\Hub\Editor\6000.6.9f1`; Windows Standalone e WebGL; **sem** o módulo de servidor Linux |
| Licença | O log mostra `Access token is unavailable` no cliente de licença; o modo batch funciona, mas a ativação **não foi confirmada**: conferir no Unity Hub |
| Pacotes (Fase 3) | HDRP 17.6.0, Input System 1.20.1, glTFast 6.21.0, Test Framework 1.8.0 (57 pacotes resolvidos) |
| SDK .NET | 10.0.404 |
| IL2CPP | Indisponível: o Visual Studio com C++ não está instalado. O player foi construído com Mono |
| GPU de desenvolvimento | NVIDIA RTX 2060 SUPER, 4 GB de VRAM |

## Gate da Fase 3 ("S1 passa: compila e roda shared")

| Critério | Resultado | Evidência |
|---|---|---|
| Projeto Unity 6 HDRP criado | Sim | `unity/UnityClient`; `GraphicsSettings` aponta para `LifeVerse-HDRP.asset`; espaço de cor linear; Direct3D12 e Direct3D11; Input System ativo |
| `shared` e `scripting` compilam para a Unity | Sim | `build-plugins.ps1` gera `LifeVerse.Shared.dll` e `LifeVerse.Scripting.dll` (netstandard2.1) |
| Rodam no editor | Sim | 10 testes de modo Edit, 10 passaram, 0 falharam |
| Rodam num player compilado | Sim | Build Windows Mono (143 MB, 0 erros, 0 avisos); `LifeVerse.exe -batchmode -nographics -lifeverse-selfcheck` imprime `event=plugin_self_check result=passed checks=5`, código de saída 0 |
| Rodam com IL2CPP | **Não verificado** | Falta o Visual Studio C++; risco R4 continua aberto |
| Renderização HDRP verificada visualmente | **Não verificado** | Os testes rodaram com `-nographics`; nenhuma cena foi renderizada |

## Fases

| Fase | Estado | Observação |
|---|---|---|
| 1 Auditoria | Concluído (revisão pendente) | `MIGRATION_AUDIT.md`. Limite: classificação por arquivo, não por classe |
| 2 Arquitetura | Em análise | `MIGRATION_ARCHITECTURE.md`. Faltam a Fase 2b e as decisões D3 e D4 |
| 3 Fundação Unity | Em validação | Gate cumprido, exceto IL2CPP e render visual (acima). D1, D2 e D7 foram executadas |
| 4 Código compartilhado | Não iniciado | Rodar o conjunto completo de testes de `shared` dentro da Unity; planilha dos 117 smokes |
| 5 Multiplayer | Em migração | Protocolo e transporte feitos e testados (`WIRE_PROTOCOL.md`). Faltam: interpolação e reconciliação no cliente, interesse e tick no servidor, 64 clientes simultâneos e latência alta; dependem das Fases 6 e 7 |
| 6 Servidores | Não iniciado | Depende de D4 |
| 7 a 15 | Não iniciado | — |

## Módulos

| Módulo | Origem | Destino Unity | Implementação | Compilação | Testes | Pendências | Próxima ação |
|---|---|---|---|---|---|---|---|
| Backend | `backend/` | inalterado | Não muda | OK, 0 avisos | 2.558 passam, 1 falha anterior (`HighJumpTests`) | — | Nenhuma |
| `shared` | `shared/` | `LifeVerse.Shared.dll` | Implementado, em validação | OK (netstandard2.1, 0 erros) | Codecs, terreno, hash, grupos, Opus e NPC passam no editor e no player | Rodar a suíte completa na Unity; IL2CPP | Fase 4 |
| `scripting` | `scripting/` | `LifeVerse.Scripting.dll` | Implementado, em validação | OK | Compilador Lua e timer do `ScriptHost` passam no editor; compilador Lua passa no player | IL2CPP; sandbox e limites no player | Fase 4 |
| Pipeline de build | — | `build-plugins.ps1`, `ProjectSetup`, `BuildClient` | Implementado, validado | OK | Build Windows reproduzível | Build do servidor (D4) | — |
| Rede (protocolo e transporte) | `NetworkBridge` (44 RPCs) | `unity/UnityWire` (`LifeVerse.Wire`) | Implementado, em validação | OK (`netstandard2.1` e `net8.0`, 0 avisos; solução inteira 0 avisos) | 25 xUnit + 2 na Unity + autoverificação do player: passam | Servidor de ilha e cliente de jogo; 64 clientes; latência alta | Fase 6 |
| Servidor de ilha | `island-server/` | `UnityIslandServer` | Não iniciado (o build do servidor dedicado Linux já compila, sem lógica de jogo) | Build Linux OK (161 MB, 0 erros) | Não executado no Linux | Simulação de ilha, física de objetos (S4), execução em Linux | Fase 6 |
| Mundo 3D, Jogador, Avatar, Construção, UI, Social, Áudio, Distribuição | ver checklist | `LifeVerse.Unity.*` | Não iniciado | — | — | — | Fases 7 a 15 |

## Registro de decisões e incompatibilidades

- 2026-11-18: o prompt assume protocolo v7 e "preservar ENet". O repositório está no protocolo **11** e usa o `SceneMultiplayer` do Godot (ver `MIGRATION_AUDIT.md`, seção 3).
- 2026-11-18: não há `.tres` nem arquivos de shader; a conversão de recursos é conversão de código que cria recursos.
- 2026-11-18, **D1 tomada**: o projeto Unity fica em `unity/` no monorepo.
- 2026-11-18, **D3 tomada e executada**: protocolo "LifeVerse Wire" (versão 12) sobre LiteNetLib 2.1.5. ENet-CSharp descartado (binário nativo, sem compilador C++). Cliente e servidor Unity não falam com os Godot; a convivência passa pelo campo de versão/engine do ticket (a implementar na API).
- 2026-11-18, **D4 executada**: módulo "Linux Dedicated Server Build Support" instalado com o Unity CLI 1.0.1 (43 MB, SHA-256 conferido, assinatura da Unity Technologies SF válida, em `.tools/unity-cli/unity.exe`; o script oficial `install.ps1` **não** foi executado porque também edita o PATH e instala "skills" em clientes de agente de IA). Build do servidor dedicado Linux: 161 MB, 0 erros, 7 avisos. **Não executado** (sem WSL). Para repetir: `Unity.exe -batchmode -nographics -projectPath unity\UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildLinuxServer`.
- 2026-11-18: o HDRP recusou o build Linux com a lista padrão de APIs gráficas (inclui OpenGL). `ProjectSetup` agora fixa Vulkan para `StandaloneLinux64`.
- 2026-11-18: `LifeVerse.slnx` passou a incluir `unity/UnityWire` e `tests/LifeVerse.Wire.Tests` (a solução inteira compila com 0 avisos).
- 2026-11-18, **D2 tomada**: reuso por DLL pré-compilada, com `csc.rsp -langversion:12` e polyfills públicos no assembly `LifeVerse.Unity.Core` para o código-fonte da Unity.
- 2026-11-18, **D7 executada** (autorizada): HDRP 17.6.0 (embutido), Input System 1.20.1, glTFast 6.21.0 e Test Framework 1.8.0.
- 2026-11-18: o `shared` precisou de 13 APIs ausentes no netstandard2.1; a solução (11 substituições mecânicas no código gerado mais `Compat.cs`) deixa os fontes originais intactos. Se o `shared` passar a usar outra API do .NET 5–8, o `build-plugins.ps1` falha com o erro de compilação e a regra nova deve ser adicionada ali.
- 2026-11-18: o `build-plugins.ps1` copia, junto com as DLLs do LifeVerse, as dependências de pacotes (`System.Memory`, `System.Buffers`, `System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe`, `System.Threading.Tasks.Extensions`, `Microsoft.Bcl.AsyncInterfaces`, `System.Text.Json`, `System.Text.Encodings.Web`, Concentus, MoonSharp). Elas convivem com a Unity no editor e no player Mono; se aparecer conflito de assemblies em outro alvo, remover as que a Unity já fornece.
