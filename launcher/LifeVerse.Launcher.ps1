param([string]$ManifestUrl = $env:LIFEVERSE_MANIFEST_URL, [string]$InstallRoot = "$env:LOCALAPPDATA\LifeVerse")
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ManifestUrl) -or -not $ManifestUrl.StartsWith('https://')) { throw 'ManifestUrl must be an authenticated HTTPS endpoint.' }
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
$manifestPath = Join-Path $InstallRoot 'manifest.json'; $tmp = "$manifestPath.download"
Invoke-WebRequest -Uri $ManifestUrl -OutFile $tmp -UseBasicParsing
$manifest = Get-Content $tmp -Raw | ConvertFrom-Json
foreach ($file in $manifest.files) {
  $target = Join-Path $InstallRoot $file.path; $dir = Split-Path -Parent $target; New-Item -ItemType Directory -Force -Path $dir | Out-Null
  if (Test-Path $target) { $current = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant(); if ($current -eq $file.sha256) { continue } }
  $url = "$ManifestUrl/$($file.path.Replace('\','/'))"; $partial = "$target.partial"; Invoke-WebRequest -Uri $url -OutFile $partial -UseBasicParsing
  if ((Get-FileHash $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { Remove-Item -LiteralPath $partial -Force; throw "Integrity check failed: $($file.path)" }
  Move-Item -LiteralPath $partial -Destination $target -Force
}
Move-Item -LiteralPath $tmp -Destination $manifestPath -Force
$client = Join-Path $InstallRoot 'LifeVerse.exe'; if (Test-Path $client) { Start-Process -FilePath $client }
