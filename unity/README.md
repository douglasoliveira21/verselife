# LifeVerse — projeto Unity 6 HDRP

Migração do cliente (e, depois, do servidor de ilha) de Godot 4.7 para Unity 6 HDRP. O projeto Godot (`client/`, `island-server/`) **não é alterado**. Plano e riscos: `docs/unity-migration/`.

| Pasta | Conteúdo |
|---|---|
| `UnityClient/` | Projeto Unity 6000.6.9f1, HDRP 17.6.0, Input System 1.20.1, glTFast 6.21.0 |
| `UnityPlugins/` | Compila `shared/` e `scripting/` para a Unity (netstandard2.1, C# 12) **sem alterar os originais** |
| `UnityWire/` | `LifeVerse.Wire`: protocolo de rede 12 (44 mensagens) e transporte LiteNetLib, `netstandard2.1` e `net8.0`. Especificação em `docs/unity-migration/WIRE_PROTOCOL.md`; testes em `tests/LifeVerse.Wire.Tests` |
| `UnityIslandServer/` | (Fase 6; D4 autorizada, falta o módulo Linux da Unity) |

## Comandos

Todos a partir da raiz do repositório. Unity em `C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe`.

```powershell
# 1. Recompilar os plugins shared/scripting e copiá-los para o projeto (precisa do SDK .NET 10)
powershell -ExecutionPolicy Bypass -File unity\UnityPlugins\build-plugins.ps1

# 2. Configurar HDRP, Player, Input System e a cena Bootstrap (idempotente)
Unity.exe -batchmode -nographics -quit -projectPath unity\UnityClient -executeMethod LifeVerse.Unity.EditorTools.ProjectSetup.ConfigureBatch

# 3. Testes do spike S1 (modo Edit)
Unity.exe -batchmode -nographics -projectPath unity\UnityClient -runTests -testPlatform EditMode -testResults results.xml

# 3b. Protocolo de rede (xUnit, UDP em loopback, inclui perda de pacotes)
dotnet test tests\LifeVerse.Wire.Tests\LifeVerse.Wire.Tests.csproj

# 4. Build do cliente Windows (Mono) e autoverificação dos plugins no player
Unity.exe -batchmode -nographics -quit -projectPath unity\UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildWindows
unity\UnityClient\Builds\Windows\LifeVerse.exe -batchmode -nographics -lifeverse-selfcheck -logFile -

# 5. Servidor dedicado Linux (precisa do módulo Linux Dedicated Server Build Support; o HDRP exige Vulkan no Linux)
Unity.exe -batchmode -nographics -projectPath unity\UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildLinuxServer
```

## Como o código existente é reaproveitado

`shared/` e `scripting/` têm C# 10–12, `IReadOnlySet`, `[GeneratedRegex]`, `PriorityQueue` e APIs do .NET 5–8 que o perfil .NET Standard 2.1 da Unity não tem. `UnityPlugins/build-plugins.ps1`:

1. copia os fontes para `UnityPlugins/generated/` (ignorado pelo git);
2. aplica substituições mecânicas documentadas no próprio script (11 regras: `IReadOnlySet<` → `ISet<`, `MathF.Tau`, `Convert.ToHexString`, `SHA256.HashData`, `Enum.IsDefined(x)`, `char.IsAsciiLetterOrDigit`, `Math.Log2`, `Read/WriteSingleLittleEndian`, `string.Create`, `Array.Clear(a)`, `TrimEntries`, e `[GeneratedRegex]` → `new Regex`);
3. compila com `Compat.cs` (polyfills internos e uma `PriorityQueue` idêntica à do .NET) e `Polyfills.cs`;
4. copia as DLLs (e dependências: Concentus, MoonSharp, System.Text.Json) para `UnityClient/Assets/LifeVerse/Plugins`.

Os `.dll` ficam fora do git: são reconstruídos pelo script. O `csc.rsp` do projeto liga `-langversion:12`; a assembly `LifeVerse.Unity.Core` traz as versões públicas dos polyfills para o código-fonte da Unity.

## Assemblies (asmdef)

Criadas junto com o primeiro código real de cada módulo, para não existir assembly vazia: `LifeVerse.Unity.Core`, `LifeVerse.Unity.Platform`, `LifeVerse.Unity.Editor`, `LifeVerse.Unity.Tests.EditMode`. As demais (`Networking`, `World`, `Terrain`, `Player`, `Camera`, `Avatar`, `Animation`, `Building`, `Inventory`, `Economy`, `Social`, `Chat`, `Scripting`, `Audio`, `UI`, `Assets`) nascem nas fases 5 a 12.

## Pacote para distribuir

`powershell -ExecutionPolicy Bypass -File scripts\build-unity-client.ps1 -Installer` compila o cliente, monta `artifacts\unity-<versão>\` com o zip, o instalador (Inno Setup, o mesmo `installer\LifeVerse.iss` do cliente Godot, com `/DEdition=-unity`), `SHA256SUMS.txt` e `release.json`. A versão é a do projeto Unity (`bundleVersion`). A configuração do aviso de atualização é a mesma da API (`ClientRelease:*`): o cliente lê `/api/v1/client/release?platform=windows`.
