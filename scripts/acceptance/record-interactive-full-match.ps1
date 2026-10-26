param(
  [string]$ArtifactRoot = "",
  [string]$ReleaseVersion = "latest"
)

$ErrorActionPreference = "Stop"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CaptureWindowNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
"@
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$release = Join-Path $repo "artifacts\releases\$ReleaseVersion"
$serverExe = Join-Path $release "server\Game.Api.exe"
$clientExe = Join-Path $release "client\windows\CardGame.exe"
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$ffprobe = (Get-Command ffprobe -ErrorAction Stop).Source
if (-not (Test-Path $serverExe) -or -not (Test-Path $clientExe)) { throw "Release server/client is missing." }
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
  $ArtifactRoot = Join-Path $repo ("artifacts\video-evidence\interactive-full-match-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null
$ArtifactRoot = (Resolve-Path $ArtifactRoot).Path
$evidence = Join-Path $ArtifactRoot "interactive-match-evidence.json"
$clientLog = Join-Path $ArtifactRoot "client.log"
$serverLog = Join-Path $ArtifactRoot "server.log"
$serverError = Join-Path $ArtifactRoot "server-error.log"
$captureLog = Join-Path $ArtifactRoot "ffmpeg.log"
$video = Join-Path $ArtifactRoot "interactive-full-match.mp4"
$frameDirectory = Join-Path $ArtifactRoot "unity-frames"
$storage = Join-Path $ArtifactRoot "data"
New-Item -ItemType Directory -Force -Path $storage | Out-Null
New-Item -ItemType Directory -Force -Path $frameDirectory | Out-Null

$server = Start-Process -FilePath $serverExe `
  -ArgumentList @("--urls", "http://127.0.0.1:5000", "--Storage:RootPath=$storage", "--Api:WriteRateLimit:PermitLimit=5000") `
  -WorkingDirectory (Split-Path $serverExe) -WindowStyle Hidden -PassThru `
  -RedirectStandardOutput $serverLog -RedirectStandardError $serverError
$recorder = $null
$client = $null
try {
  for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try { if ((Invoke-RestMethod "http://127.0.0.1:5000/health" -TimeoutSec 1).status -eq "ok") { break } } catch { }
    Start-Sleep -Seconds 1
  }
  if ((Invoke-RestMethod "http://127.0.0.1:5000/health" -TimeoutSec 2).status -ne "ok") { throw "Server health check failed." }

  $client = Start-Process -FilePath $clientExe -ArgumentList @(
    "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720",
    "-logFile", $clientLog,
    "--record-full-match-evidence", "--record-seeded-match-evidence", "--record-evidence-output", $evidence, "--record-frame-dir", $frameDirectory) `
    -WorkingDirectory (Split-Path $clientExe) -PassThru
  if (-not $client.WaitForExit(900000)) { Stop-Process -Id $client.Id -Force; throw "Interactive evidence client timed out." }
  if ($client.ExitCode -ne 0) { throw "Interactive evidence client failed with exit code $($client.ExitCode)." }
} finally {
  if ($client -and -not $client.HasExited) { Stop-Process -Id $client.Id -Force }
  if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}

if (-not (Test-Path $evidence)) { throw "Interactive match evidence was not written." }
$result = Get-Content -Raw $evidence | ConvertFrom-Json
if (-not $result.Succeeded -or -not $result.ReplayVerified -or $result.MaxBoardEntityCount -le 0) {
  throw "Interactive match evidence failed: $($result.Error)"
}
$types = @($result.CommandTypes)
if (-not ($types | Where-Object { $_ -match 'PLAY_CARD$' }) -or -not ($types | Where-Object { $_ -match 'ATTACK$' })) {
  throw "Interactive match did not prove card play and combat."
}
if (@(Get-ChildItem $frameDirectory -Filter "frame-*.png").Count -lt 50) { throw "Unity did not capture enough internal match frames." }
& $ffmpeg -hide_banner -loglevel warning -y -framerate 5 -i (Join-Path $frameDirectory "frame-%06d.png") -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart $video
if ($LASTEXITCODE -ne 0) { throw "FFmpeg could not encode Unity's internal match frames." }
$duration = & $ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $video
if ($LASTEXITCODE -ne 0 -or -not $duration -or [double]$duration -lt 10) { throw "Recorded MP4 is missing, unreadable, or shorter than 10 seconds." }
[pscustomobject]@{
  Verdict = "passed"
  Video = $video
  DurationSeconds = [math]::Round([double]$duration, 2)
  MatchId = $result.MatchId
  CommandCount = $result.CommandCount
  MaxBoardEntityCount = $result.MaxBoardEntityCount
  ReplayVerified = $result.ReplayVerified
  FinalStateHash = $result.FinalStateHash
} | ConvertTo-Json
