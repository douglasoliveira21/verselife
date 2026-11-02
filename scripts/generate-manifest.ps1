param([Parameter(Mandatory=$true)][string]$Root, [Parameter(Mandatory=$true)][string]$Output)
$resolved = (Resolve-Path -LiteralPath $Root).Path
$files = Get-ChildItem -LiteralPath $resolved -File -Recurse | Where-Object { $_.FullName -notmatch '\\\.git\\|\\.godot\\|\\bin\\|\\obj\\' } | ForEach-Object {
  $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  [ordered]@{ path = $_.FullName.Substring($resolved.Length).TrimStart('\'); sha256 = $hash; bytes = $_.Length }
}
$manifest = [ordered]@{ product = 'LifeVerse'; generatedAt = [DateTime]::UtcNow.ToString('O'); algorithm = 'SHA-256'; files = @($files) }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Output -Encoding UTF8
