param(
  [string]$Url = "http://127.0.0.1:5000",
  [string]$StorageRoot = "artifacts/unity-live-backend-storage"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$storagePath = Join-Path $repoRoot $StorageRoot
$logRoot = Join-Path $repoRoot "artifacts\unity-test-logs"
New-Item -ItemType Directory -Force -Path $storagePath, $logRoot | Out-Null

$environment = @{
  "Storage__RootPath" = $storagePath
  # A single validation run executes several complete deterministic matches plus
  # seed-repeatability checks from the same loopback partition.
  "Api__WriteRateLimit__PermitLimit" = "50000"
  "Api__WriteRateLimit__WindowSeconds" = "60"
}
foreach ($entry in $environment.GetEnumerator()) {
  [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
}

$process = Start-Process -FilePath "dotnet" -ArgumentList @(
  "run", "--project", "src/Game.Api", "--configuration", "Release", "--no-launch-profile", "--urls", $Url,
  "--Api:WriteRateLimit:PermitLimit", "50000", "--Api:WriteRateLimit:WindowSeconds", "60"
) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru `
  -RedirectStandardOutput (Join-Path $logRoot "backend.out.log") `
  -RedirectStandardError (Join-Path $logRoot "backend.err.log")

Write-Host "Unity test backend started at $Url (PID $($process.Id))."
Write-Host "Storage: $storagePath"
