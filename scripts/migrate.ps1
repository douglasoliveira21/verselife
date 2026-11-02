$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet tool restore
dotnet ef database update --project (Join-Path $root 'backend/LifeVerse.Api.csproj') --startup-project (Join-Path $root 'backend/LifeVerse.Api.csproj')
