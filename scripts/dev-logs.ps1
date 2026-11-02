$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
docker compose --env-file (Join-Path $root '.env') -f (Join-Path $root 'infrastructure/compose.dev.yml') logs -f --tail 200
