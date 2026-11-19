<#
  Compila shared/ e scripting/ para a Unity (netstandard2.1, C# 12) e copia os .dll para
  UnityClient/Assets/LifeVerse/Plugins. Os projetos originais NÃO são alterados.

  O netstandard2.1 não traz dois recursos que o código usa. Para não mexer nos fontes de shared/ e scripting/
  (que o backend, o servidor e os testes compilam), este script compila uma CÓPIA em unity/UnityPlugins/generated
  com duas substituições mecânicas:
    1. IReadOnlySet<T>  ->  ISet<T>               (HashSet<T> implementa ISet<T>; IReadOnlySet só existe no .NET 5+)
    2. [GeneratedRegex(args)] private static partial Regex Nome();
         ->  private static Regex Nome() => cache ??= new Regex(args);      (o gerador de regex é do .NET 7+)
  O resultado é o mesmo comportamento; as substituições são conferidas pelos testes no Unity Test Runner.
#>
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { 'C:\Program Files\dotnet\dotnet.exe' }
$plugins = Join-Path $repo 'unity\UnityClient\Assets\LifeVerse\Plugins'
$generated = Join-Path $here 'generated'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
if (Test-Path $generated) { Remove-Item $generated -Recurse -Force }

$regexPattern = '\[GeneratedRegex\((?<args>.*?)\)\]\s*\r?\n(?<indent>[ \t]*)(?<mods>(?:private|public|internal)\s+static)\s+partial\s+Regex\s+(?<name>\w+)\(\)\s*;'
function Convert-Source([string]$text) {
  # 1) tipos e APIs que não existem no netstandard2.1 (a camada LifeVerse.Compat está em Compat.cs)
  $text = $text -replace 'IReadOnlySet<', 'ISet<'
  $text = $text -replace 'MathF\.Tau', '(MathF.PI * 2f)'
  $text = $text -replace 'StringSplitOptions\.RemoveEmptyEntries \| StringSplitOptions\.TrimEntries\)', 'StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0)'
  $text = $text -replace 'Convert\.ToHexString\(', 'LifeVerse.Compat.Compat.ToHexString('
  $text = $text -replace 'SHA256\.HashData\(', 'LifeVerse.Compat.Compat.Sha256('
  $text = $text -replace 'Enum\.IsDefined\((?!typeof\()', 'LifeVerse.Compat.Compat.IsDefined('
  $text = $text -replace 'char\.IsAsciiLetterOrDigit', 'LifeVerse.Compat.Compat.IsAsciiLetterOrDigit'
  $text = $text -replace 'Math\.Log2\(', 'LifeVerse.Compat.Compat.Log2('
  $text = $text -replace 'BinaryPrimitives\.(Write|Read)SingleLittleEndian\(', 'LifeVerse.Compat.Compat.$1SingleLittleEndian('
  $text = $text -replace 'string\.Create\(CultureInfo\.InvariantCulture, (\$".*?")\)', 'FormattableString.Invariant($1)'
  $text = $text -replace 'Array\.Clear\(([\w\.]+)\)', 'Array.Clear($1, 0, $1.Length)'
  [regex]::Replace($text, $regexPattern, {
      param($m)
      $i = $m.Groups['indent'].Value; $n = $m.Groups['name'].Value
      "private static System.Text.RegularExpressions.Regex? _${n}Cache;`n$i$($m.Groups['mods'].Value) System.Text.RegularExpressions.Regex $n() => _${n}Cache ??= new System.Text.RegularExpressions.Regex($($m.Groups['args'].Value));"
    }, [System.Text.RegularExpressions.RegexOptions]::Singleline)
}

foreach ($name in 'shared', 'scripting') {
  $src = Join-Path $repo $name; $dst = Join-Path $generated $name
  Get-ChildItem $src -Recurse -Filter *.cs -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
    $rel = $_.FullName.Substring($src.Length).TrimStart('\'); $target = Join-Path $dst $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    $raw = [System.IO.File]::ReadAllText($_.FullName)
    [System.IO.File]::WriteAllText($target, (Convert-Source $raw), (New-Object System.Text.UTF8Encoding($true)))
  }
}

foreach ($project in 'LifeVerse.Shared.Unity.csproj', 'LifeVerse.Scripting.Unity.csproj') {
  & $dotnet build (Join-Path $here $project) -c $Configuration -nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar $project" }
}

# LifeVerse.Wire (protocolo 12 + transporte LiteNetLib): projeto próprio em ../UnityWire, alvo netstandard2.1
& $dotnet build (Join-Path $here '..\UnityWire\LifeVerse.Wire.csproj') -c $Configuration -f netstandard2.1 -p:CopyLocalLockFileAssemblies=true -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar LifeVerse.Wire" }

# LifeVerse.IslandServer.Core (servidor de ilha sem engine, Fase 6)
& $dotnet build (Join-Path $here '..\UnityIslandServer\Core\LifeVerse.IslandServer.Core.csproj') -c $Configuration -f netstandard2.1 -p:CopyLocalLockFileAssemblies=true -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar LifeVerse.IslandServer.Core' }

# copia as DLLs (incluindo as dependências de pacotes) e remove versões antigas
Get-ChildItem $plugins -Filter *.dll -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $plugins -Filter *.dll.meta -ErrorAction SilentlyContinue | Remove-Item -Force
foreach ($out in 'bin\LifeVerse.Shared.Unity', 'bin\LifeVerse.Scripting.Unity') {
  Get-ChildItem (Join-Path $here "$out\netstandard2.1") -Filter *.dll | ForEach-Object { Copy-Item $_.FullName $plugins -Force }
}
Get-ChildItem (Join-Path $here "..\UnityWire\bin\$Configuration\netstandard2.1") -Filter *.dll | ForEach-Object { Copy-Item $_.FullName $plugins -Force }
Get-ChildItem (Join-Path $here "..\UnityIslandServer\Core\bin\$Configuration\netstandard2.1") -Filter *.dll | ForEach-Object { Copy-Item $_.FullName $plugins -Force }
Write-Host "Plugins em $plugins :"; Get-ChildItem $plugins -Filter *.dll | ForEach-Object { '  {0}  {1:N0} bytes' -f $_.Name, $_.Length }
