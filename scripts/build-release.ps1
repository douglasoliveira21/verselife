$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content "$root\backend\LifeVerse.Api.csproj")).Project.PropertyGroup.Version
$out = Join-Path $root "artifacts\$version"
New-Item -ItemType Directory -Force -Path $out | Out-Null
foreach ($project in @('backend\LifeVerse.Api.csproj','client\LifeVerse.Client.csproj','island-server\LifeVerse.IslandServer.csproj','tests\LifeVerse.Api.Tests\LifeVerse.Api.Tests.csproj')) {
  dotnet build (Join-Path $root $project) --configuration Release --no-restore --nologo -m:1
  if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
& "$PSScriptRoot\generate-manifest.ps1" -Root "$root\client" -Output "$out\client-manifest.json"
Copy-Item "$root\client\project.godot" "$out\client-project.godot" -Force
@{ product = 'LifeVerse'; version = $version; commit = (git -C $root rev-parse HEAD); generatedAt = [DateTime]::UtcNow.ToString('O'); artifacts = @('client-manifest.json','client-project.godot') } | ConvertTo-Json | Set-Content "$out\release.json" -Encoding UTF8
Write-Host "Release artifacts: $out"
