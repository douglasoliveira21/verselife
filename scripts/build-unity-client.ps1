<#
  Builds the Unity client for Windows and packages it: a zip, an optional installer, the SHA-256 of every file and a small release.json.
    scripts\build-unity-client.ps1                 build + zip + checksums
    scripts\build-unity-client.ps1 -Installer      also the Inno Setup installer (.tools\inno\ISCC.exe or iscc on the PATH)
    scripts\build-unity-client.ps1 -SkipBuild      package what is already in unity\UnityClient\Builds\Windows
  The version is the one in the Unity project (Player settings, "bundleVersion"); the same number goes in the file names and in the installer.
  The Unity editor is looked for in $env:UNITY_EXE, then in the Unity Hub folder of the project's editor version.
#>
param([switch]$SkipBuild, [switch]$Installer)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'unity\UnityClient'
$settings = Get-Content (Join-Path $project 'ProjectSettings\ProjectSettings.asset') -Raw
if ($settings -notmatch '(?m)^  bundleVersion: (\S+)') { throw 'bundleVersion not found in ProjectSettings.asset' }
$version = $Matches[1]
$out = Join-Path $root "artifacts\unity-$version"
$build = Join-Path $project 'Builds\Windows'

if (-not $SkipBuild) {
  $editorVersion = ((Get-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') | Select-Object -First 1) -replace 'm_EditorVersion:\s*', '').Trim()
  $unity = if ($env:UNITY_EXE) { $env:UNITY_EXE } else { "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe" }
  if (-not (Test-Path $unity)) { throw "Unity $editorVersion not found at $unity (set UNITY_EXE)" }
  $log = Join-Path $env:TEMP 'lifeverse-unity-build.log'
  $process = Start-Process $unity -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$project,'-executeMethod','LifeVerse.Unity.EditorTools.BuildClient.BuildWindows','-logFile',$log -PassThru -Wait
  if ($process.ExitCode -ne 0) { throw "Unity build failed (exit $($process.ExitCode)); see $log" }
}
if (-not (Test-Path (Join-Path $build 'LifeVerse.exe'))) { throw "No build at $build" }

# only what the game needs: the folders Unity keeps for debugging are left out
$stage = Join-Path $out 'windows'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Get-ChildItem $build | Where-Object { $_.Name -notlike '*_BackUpThisFolder*' -and $_.Name -notlike '*_BurstDebugInformation_DoNotShip*' } | ForEach-Object { Copy-Item $_.FullName $stage -Recurse -Force }

$zip = Join-Path $out "LifeVerse-unity-windows-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$files = @(Get-Item $zip)

if ($Installer) {
  $iscc = if (Test-Path (Join-Path $root '.tools\inno\ISCC.exe')) { Join-Path $root '.tools\inno\ISCC.exe' } else { (Get-Command iscc -ErrorAction Stop).Source }
  & $iscc "/DAppVersion=$version" "/DEdition=-unity" "/DSourceDir=$stage" (Join-Path $root 'installer\LifeVerse.iss') | Out-Host
  if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
  $setup = Join-Path $root "build\installer\LifeVerse-Setup-$version-unity.exe"
  Copy-Item $setup $out -Force; $files += Get-Item (Join-Path $out (Split-Path $setup -Leaf))
}

$entries = $files | ForEach-Object { [ordered]@{ file = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }
$entries | ForEach-Object { "$($_.sha256)  $($_.file)" } | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ASCII
[ordered]@{ product = 'LifeVerse'; edition = 'unity'; version = $version; commit = (git -C $root rev-parse HEAD); generatedAt = [DateTime]::UtcNow.ToString('O'); files = @($entries) } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'release.json') -Encoding UTF8
Write-Host "Unity client $version packaged in $out"
