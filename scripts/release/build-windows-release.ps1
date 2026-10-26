param(
  [string]$Version = "latest",
  [string]$UnityEditor = "C:\Program Files\Unity\Hub\Editor\6000.3.2f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$releaseRoot = Join-Path $repo "artifacts\releases"
$release = Join-Path $releaseRoot $Version
$staging = Join-Path $releaseRoot (".staging-" + $Version)
if ([string]::IsNullOrWhiteSpace($Version) -or $Version -match '[\\/:*?"<>|]') { throw "Version contains invalid path characters." }
foreach ($target in @($release,$staging)) {
  $full = [IO.Path]::GetFullPath($target)
  if (-not $full.StartsWith([IO.Path]::GetFullPath($releaseRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe release target: $full" }
  if (Test-Path $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
$release = $staging
$client = Join-Path $release "client\windows"
$server = Join-Path $release "server"
$data = Join-Path $release "data"
$logs = Join-Path $release "logs"
$localTools = Join-Path $release "tools\local-models"
$gatewayTools = Join-Path $localTools "gateways"
$buildLogs = Join-Path $repo "artifacts\build-logs\$Version"
New-Item -ItemType Directory -Force -Path $client,$server,$data,$logs,$buildLogs,$gatewayTools | Out-Null
$unityBuildLog = Join-Path $buildLogs "unity-build.log"

dotnet publish (Join-Path $repo "src\Game.Api\Game.Api.csproj") -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $server
if ($LASTEXITCODE -ne 0) { throw "Server publish failed." }

$env:CARDGAME_UNITY_BUILD_OUTPUT_PATH = Join-Path $client "CardGame.exe"
$unityProject = Join-Path $repo "clients\unity\CardGame.Unity"
$unityConnectSettings = Join-Path $unityProject "ProjectSettings\UnityConnectSettings.asset"
$unityConnectSettingsBytes = [IO.File]::ReadAllBytes($unityConnectSettings)
try {
  $unity = Start-Process -FilePath $UnityEditor -ArgumentList @("-batchmode", "-quit", "-projectPath", $unityProject, "-logFile", $unityBuildLog, "-executeMethod", "CardGame.Unity.Client.Editor.Build.UnityBuildSmoke.BuildStandaloneReleaseCandidateBatch") -Wait -PassThru -WindowStyle Hidden
} finally {
  # Unity batch mode rewrites this editor-owned setting even when the player
  # output is identical. Preserve the checked-in project state so release
  # provenance is not falsely marked dirty.
  [IO.File]::WriteAllBytes($unityConnectSettings, $unityConnectSettingsBytes)
}
if ($unity.ExitCode -ne 0 -or -not (Test-Path (Join-Path $client "CardGame.exe"))) { throw "Unity Windows build failed. See $unityBuildLog" }

Copy-Item (Join-Path $repo "docs\user\quick-start.md") (Join-Path $release "QUICK-START.md") -Force
Copy-Item (Join-Path $repo "docs\user\quick-start.ru.md") (Join-Path $release "QUICK-START-RU.md") -Force
Copy-Item (Join-Path $repo "docs\user\manual-release-test.ru.md") (Join-Path $release "MANUAL-RELEASE-TEST-RU.md") -Force
Copy-Item (Join-Path $repo "docs\user\local-generation.md") (Join-Path $release "LOCAL-GENERATION.md") -Force
foreach ($tool in @("start-generation-gateways.ps1", "requirements.txt")) {
  Copy-Item (Join-Path $repo "scripts\local-models\$tool") (Join-Path $localTools $tool) -Force
}
foreach ($gateway in @("ollama_structured_gateway.py", "stable_diffusion_asset_gateway.py", "diffusers_a1111_api.py")) {
  Copy-Item (Join-Path $repo "scripts\local-models\gateways\$gateway") (Join-Path $gatewayTools $gateway) -Force
}
@'
# Optional: required only for OpenAI Connector mode.
OPENAI_API_KEY=
# Generation__OpenAIStructuredModel=gpt-5.6-terra
# Generation__OpenAIImageModel=gpt-image-2
'@ | Set-Content -Encoding ascii (Join-Path $release ".env.example")
@{
  Storage = @{ RootPath = "../data" }
  Api = @{ Cors = @{ Enabled = $false }; WriteRateLimit = @{ PermitLimit = 120; WindowSeconds = 60 } }
} | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 (Join-Path $server "appsettings.Production.json")

$launcher = @'
@echo off
setlocal
if not exist "%~dp0logs" mkdir "%~dp0logs"
set ASPNETCORE_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://127.0.0.1:5000
start "Chronicle Forge Server" /MIN /D "%~dp0server" cmd /c "Game.Api.exe --Storage:RootPath=""%~dp0data"" 1>>""%~dp0logs\server.log"" 2>>""%~dp0logs\server-error.log"""
for /L %%i in (1,1,30) do (
  powershell.exe -NoProfile -Command "try { $response = Invoke-RestMethod 'http://127.0.0.1:5000/health' -TimeoutSec 1; if ($response.status -eq 'ok') { exit 0 }; exit 1 } catch { exit 1 }" >nul 2>&1 && goto server_ready
  timeout /t 1 /nobreak >nul
)
echo Chronicle Forge Server failed to become healthy. See logs\server-error.log.
exit /b 1
:server_ready
start "Chronicle Forge" /D "%~dp0client\windows" CardGame.exe -logFile "%~dp0logs\client.log"
endlocal
'@
$launcher | Set-Content -Encoding ascii (Join-Path $release "PLAY.bat")
$commit = (git -C $repo rev-parse HEAD 2>$null)
git -C $repo diff --quiet HEAD --
if ($LASTEXITCODE -notin @(0, 1)) { throw "Could not verify release source state." }
$dirty = $LASTEXITCODE -ne 0
$dirty = $dirty -or [bool](git -C $repo ls-files --others --exclude-standard)
$manifest = [ordered]@{
  product = "Chronicle Forge"
  version = $Version
  platform = "windows-x64"
  builtAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
  gitCommit = $commit
  sourceDirty = $dirty
  unityVersion = "6000.3.2f1"
  serverSelfContained = $true
  launch = "PLAY.bat"
  clientExecutable = "client/windows/CardGame.exe"
  serverExecutable = "server/Game.Api.exe"
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $release "BUILD-MANIFEST.json")
Get-ChildItem $release -Recurse -File | Where-Object Name -ne "SHA256SUMS.txt" | Sort-Object FullName | ForEach-Object {
  $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  "$hash  $($_.FullName.Substring($release.Length + 1).Replace('\','/'))"
} | Set-Content -Encoding ascii (Join-Path $release "SHA256SUMS.txt")
$finalRelease = Join-Path $releaseRoot $Version
Move-Item -LiteralPath $release -Destination $finalRelease
$release = $finalRelease
$archive = Join-Path $releaseRoot "Chronicle-Forge-Windows-$Version.zip"
if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $release "*") -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Windows release ready: $release"
Write-Host "Windows release archive: $archive"
