# Builds the Unity island server for Linux and packs it for island-supervisor/Dockerfile.unity: artifacts/LifeVerseServer-linux.tar.gz and its SHA-256.
# Needs Unity 6000.6.9f1 with the "Linux Dedicated Server Build Support" module. Run from the repository root:
#   powershell -File scripts/package-unity-server.ps1            (builds, then packs)
#   powershell -File scripts/package-unity-server.ps1 -SkipBuild (packs what is in unity/UnityClient/Builds/LinuxServer)
param([switch]$SkipBuild, [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root "unity\UnityClient\Builds\LinuxServer"
if (-not $SkipBuild) {
    & powershell -File (Join-Path $root "unity\UnityPlugins\build-plugins.ps1")
    $log = Join-Path $env:TEMP "lifeverse-linux-server-build.log"
    $p = Start-Process -FilePath $Unity -ArgumentList @("-batchmode", "-nographics", "-projectPath", (Join-Path $root "unity\UnityClient"), "-executeMethod", "LifeVerse.Unity.EditorTools.BuildClient.BuildLinuxServer", "-quit", "-logFile", $log) -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "The Linux server build failed (exit $($p.ExitCode)); see $log" }
}
if (-not (Test-Path (Join-Path $build "LifeVerseServer.x86_64"))) { throw "No server build in $build" }
$out = Join-Path $root "artifacts"; New-Item -ItemType Directory -Force $out | Out-Null
# tar (built into Windows 10 and later) keeps the forward slashes Linux expects; the folder Unity says not to ship stays out, and so does StreamingAssets
# (the built-in body and the sounds, which only the game on a screen reads): the package goes to a public address, so it carries nothing the server does not use
$zip = Join-Path $out "LifeVerseServer-linux.tar.gz"; if (Test-Path $zip) { Remove-Item $zip }
& tar.exe -czf $zip --exclude "*BackUpThisFolder_ButDontShipItWithYourGame*" --exclude "*StreamingAssets*" -C $build .
if ($LASTEXITCODE -ne 0) { throw "tar failed ($LASTEXITCODE)" }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"file:   $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)"
"sha256: $hash"
