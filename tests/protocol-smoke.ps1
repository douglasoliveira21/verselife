$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'shared/LifeVerse.Shared.csproj') --no-restore -m:1
dotnet build (Join-Path $root 'client/LifeVerse.Client.csproj') --no-restore -m:1
dotnet build (Join-Path $root 'island-server/LifeVerse.IslandServer.csproj') --no-restore -m:1
Write-Host 'Protocol smoke: shared contract and both Godot assemblies compiled.'
