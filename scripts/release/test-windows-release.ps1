param([string]$Version = "latest")
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$release = Join-Path $repo "artifacts\releases\$Version"
$serverLog = Join-Path $release "logs\server-smoke.log"
$serverError = Join-Path $release "logs\server-smoke-error.log"
$clientLog = Join-Path $release "logs\client-smoke.log"
$storageArgument = "--Storage:RootPath=" + (Join-Path $release "data")
$server = Start-Process -FilePath (Join-Path $release "server\Game.Api.exe") -ArgumentList @("--urls", "http://127.0.0.1:5000", $storageArgument) -WorkingDirectory (Join-Path $release "server") -WindowStyle Hidden -PassThru -RedirectStandardOutput $serverLog -RedirectStandardError $serverError
try {
  Start-Sleep -Seconds 3
  $health = Invoke-RestMethod "http://127.0.0.1:5000/health"
  $client = Start-Process -FilePath (Join-Path $release "client\windows\CardGame.exe") -ArgumentList @("-batchmode", "-logFile", $clientLog) -WorkingDirectory (Join-Path $release "client\windows") -WindowStyle Hidden -PassThru
  try {
    Start-Sleep -Seconds 8
    if ($client.HasExited) { throw "Packaged client exited unexpectedly with code $($client.ExitCode)." }
  } finally {
    if (-not $client.HasExited) { Stop-Process -Id $client.Id -Force }
  }
  $files = Get-ChildItem $release -Recurse -File
  $manualTest = Join-Path $release "MANUAL-RELEASE-TEST-RU.md"
  if (-not (Test-Path $manualTest)) { throw "Packaged manual release test guide is missing." }
  [pscustomobject]@{
    Health = $health.status
    ClientStarted = $true
    ExeExists = Test-Path (Join-Path $release "client\windows\CardGame.exe")
    FileCount = $files.Count
    Bytes = ($files | Measure-Object Length -Sum).Sum
    Checksums = (Get-Content (Join-Path $release "SHA256SUMS.txt")).Count
    ManualReleaseTestGuide = $true
  } | ConvertTo-Json
} finally {
  if (-not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}
