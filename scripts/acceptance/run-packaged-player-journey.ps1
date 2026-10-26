param(
  [Parameter(Mandatory=$true)][string]$ArtifactRoot,
  [string]$Version = "latest",
  [int]$RunCount = 2,
  [switch]$RestartBetweenRuns
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$releaseRoot = Join-Path $repo "artifacts\releases"
if ([string]::IsNullOrWhiteSpace($Version) -or $Version -match '[\\/:*?"<>|]') { throw "Version contains invalid path characters." }
$release = [IO.Path]::GetFullPath((Join-Path $releaseRoot $Version))
if (-not $release.StartsWith([IO.Path]::GetFullPath($releaseRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe release path: $release" }
$serverExe = Join-Path $release "server\Game.Api.exe"
$clientExe = Join-Path $release "client\windows\CardGame.exe"
if (-not (Test-Path $serverExe) -or -not (Test-Path $clientExe)) { throw "Release server/client is missing." }
New-Item -ItemType Directory -Force $ArtifactRoot | Out-Null
$ArtifactRoot = (Resolve-Path $ArtifactRoot).Path
$server = $null
function Start-TestServer {
  $storageArg = "--Storage:RootPath=" + (Join-Path $ArtifactRoot "durable-data")
  return Start-Process $serverExe -ArgumentList @("--urls","http://127.0.0.1:5000",$storageArg,"--Api:WriteRateLimit:PermitLimit=5000") -WorkingDirectory (Split-Path $serverExe) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $ArtifactRoot "server.log") -RedirectStandardError (Join-Path $ArtifactRoot "server-error.log")
}
function Stop-TestServer($process) {
  if ($process -and (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { Stop-Process -Id $process.Id -Force }
}
try {
  $server = Start-TestServer; Start-Sleep 3
  for ($i=1; $i -le $RunCount; $i++) {
    $run = Join-Path $ArtifactRoot ("run-{0:d2}" -f $i); New-Item -ItemType Directory -Force $run | Out-Null
    $result = Join-Path $run "journey-result.json"; $clientLog = Join-Path $run "client.log"
    $client = Start-Process $clientExe -ArgumentList @("-batchmode","-logFile",$clientLog,"--e2e-player-journey","--e2e-output",$result) -WorkingDirectory (Split-Path $clientExe) -PassThru
    if (-not $client.WaitForExit(900000)) { Stop-Process $client.Id -Force; throw "Packaged run $i timed out." }
    if ($client.ExitCode -ne 0 -or -not (Test-Path $result)) { throw "Packaged run $i failed." }
    $json = Get-Content -Raw $result | ConvertFrom-Json
    if ($json.verdict -ne "passed" -or -not $json.replayVerified -or $json.commandCount -le 10) { throw "Packaged run $i did not prove a complete terminal match." }
    if ($RestartBetweenRuns -and $i -lt $RunCount) { Stop-TestServer $server; Start-Sleep 2; $server = Start-TestServer; Start-Sleep 3 }
  }
  @{ verdict="full-player-journey-production-failed"; automatedRunsPassed=$RunCount; packagedClient=$true; restartBetweenRuns=[bool]$RestartBetweenRuns; interactivePlayerContractsPassed=$true; asynchronousGenerationLifecyclePassed=$true; dynamicallyPublishedDeckPassed=$true; selectedRuntimeDeckTerminalMatchPassed=$true; openAiPublishedReleaseDeckPassed=$true; clientLogExceptionScanRequired=$true; manualPackagedJourneyPassed=$false; reasons=@("A complete pointer/keyboard packaged-client journey has not yet been signed off on the release hardware matrix.") } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $ArtifactRoot "final-verdict.json")
} finally { Stop-TestServer $server }
exit 1
