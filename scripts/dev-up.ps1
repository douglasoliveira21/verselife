$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) { throw 'Create .env from .env.example and replace all placeholder secrets first.' }
if ((Get-Content -LiteralPath $envFile -Raw) -match 'replace-with') { throw 'Replace every placeholder value in .env before starting the stack.' }
docker compose --env-file $envFile -f (Join-Path $root 'infrastructure/compose.dev.yml') up -d --build
