param(
  [string]$OutputRoot = "artifacts/final-acceptance",
  [switch]$SkipValidation
)

$ErrorActionPreference = "Continue"

function New-Gate {
  param(
    [string]$GateId,
    [string]$Status,
    [string[]]$Diagnostics = @(),
    [string[]]$EvidenceRefs = @()
  )

  [ordered]@{
    gateId = $GateId
    status = $Status
    diagnostics = @($Diagnostics | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    evidenceRefs = @($EvidenceRefs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  }
}

function Invoke-GateCommand {
  param(
    [string]$GateId,
    [string]$Command,
    [string]$EvidenceRef
  )

  $output = & powershell -NoProfile -ExecutionPolicy Bypass -Command $Command 2>&1
  $exitCode = $LASTEXITCODE
  $path = Join-Path $OutputDirectory ($EvidenceRef -replace '/', [IO.Path]::DirectorySeparatorChar)
  New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
  $output | Set-Content -Path $path -Encoding UTF8

  if ($exitCode -eq 0) {
    return New-Gate $GateId "Passed" @() @($EvidenceRef)
  }

  return New-Gate $GateId "Blocked" @("$GateId command failed with exit code $exitCode") @($EvidenceRef)
}

function Test-ProviderConfiguration {
  $required = @(
    "CCG_LOCAL_STRUCTURED_ENDPOINT",
    "CCG_LOCAL_STRUCTURED_MODEL",
    "CCG_LOCAL_IMAGE_ENDPOINT",
    "CCG_LOCAL_IMAGE_MODEL",
    "CCG_LOCAL_ANIMATION_ENDPOINT",
    "CCG_LOCAL_ANIMATION_MODEL",
    "CCG_LOCAL_AUDIO_ENDPOINT",
    "CCG_LOCAL_AUDIO_MODEL"
  )
  $missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
  if ($missing.Count -gt 0) {
    return New-Gate "L3_PROVIDER" "Blocked" @("Missing local provider configuration: $($missing -join ', ')")
  }

  $evidenceRef = "provider/local-provider-evidence.log"
  return Invoke-GateCommand `
    "L3_PROVIDER" `
    "powershell -NoProfile -ExecutionPolicy Bypass -File scripts\local-models\run-local-provider-evidence.ps1 -OutputRoot '$OutputDirectory\provider'" `
    $evidenceRef
}

function Test-ContainerStack {
  $configGate = Invoke-GateCommand "L2_DEPLOYMENT_CONFIG" "docker compose config --quiet" "deployment/docker-compose-config.log"
  if ($configGate.status -ne "Passed") {
    return $configGate
  }

  $evidenceRef = "deployment/docker-compose-up-health.log"
  $path = Join-Path $OutputDirectory ($evidenceRef -replace '/', [IO.Path]::DirectorySeparatorChar)
  New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null

  $lines = New-Object System.Collections.Generic.List[string]
  $up = & docker compose up -d --build 2>&1
  $upExit = $LASTEXITCODE
  $lines.AddRange([string[]]$up)
  if ($upExit -ne 0) {
    $lines | Set-Content -Path $path -Encoding UTF8
    return New-Gate "L2_DEPLOYMENT_RUNTIME" "Blocked" @("docker compose up failed with exit code $upExit") @($evidenceRef)
  }

  try {
    $healthy = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
      try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 2 -UseBasicParsing
        $lines.Add("health attempt ${attempt}: $($response.StatusCode)")
        if ($response.StatusCode -eq 200) {
          $healthy = $true
          break
        }
      }
      catch {
        $lines.Add("health attempt ${attempt}: $($_.Exception.Message)")
        Start-Sleep -Seconds 2
      }
    }

    $lines | Set-Content -Path $path -Encoding UTF8
    if ($healthy) {
      return New-Gate "L2_DEPLOYMENT_RUNTIME" "Passed" @() @($evidenceRef)
    }

    return New-Gate "L2_DEPLOYMENT_RUNTIME" "Blocked" @("container health endpoint did not become ready") @($evidenceRef)
  }
  finally {
    & docker compose down 2>&1 | Out-Null
  }
}

function Get-ProgressEvidence {
  $evidenceRef = "progress/progress.log"
  return Invoke-GateCommand "L0_PROGRESS" "bash scripts/ai/progress.sh" $evidenceRef
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$OutputDirectory = Join-Path $OutputRoot $timestamp
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

$gates = New-Object System.Collections.Generic.List[object]
$gates.Add((Get-ProgressEvidence))
$gates.Add((Test-ContainerStack))
$gates.Add((Test-ProviderConfiguration))

if (-not $SkipValidation) {
  $gates.Add((Invoke-GateCommand "L1_FORBIDDEN_SCAN" "bash scripts/ai/forbidden-api-scan.sh" "validation/forbidden-api-scan.log"))
  $gates.Add((Invoke-GateCommand "L1_RESTORE" "dotnet restore" "validation/dotnet-restore.log"))
  $gates.Add((Invoke-GateCommand "L1_BUILD" "dotnet build --configuration Release" "validation/dotnet-build-release.log"))
  $gates.Add((Invoke-GateCommand "L1_TEST" "dotnet test --configuration Release" "validation/dotnet-test-release.log"))
  $gates.Add((Invoke-GateCommand "L1_DOCTOR" "bash scripts/ai/doctor.sh" "validation/doctor.log"))
}

$blocked = @($gates | Where-Object { $_.status -ne "Passed" })
$report = [ordered]@{
  reportId = "final-acceptance-$timestamp"
  createdAt = (Get-Date).ToUniversalTime().ToString("o")
  status = if ($blocked.Count -eq 0) { "Accepted" } else { "Blocked" }
  acceptanceAlgorithmRef = "AcceptanceAlgorithm.md"
  commitSha = (& git rev-parse HEAD).Trim()
  branch = (& git rev-parse --abbrev-ref HEAD).Trim()
  gates = $gates
  blockers = @($blocked | ForEach-Object { "$($_.gateId): $($_.diagnostics -join '; ')" })
}

$reportPath = Join-Path $OutputDirectory "final-acceptance-report.json"
$report | ConvertTo-Json -Depth 64 | Set-Content -Path $reportPath -Encoding UTF8

Write-Host "Final acceptance status: $($report.status)"
Write-Host "Report: $reportPath"
if ($blocked.Count -gt 0) {
  Write-Host "Blockers:"
  foreach ($blocker in $report.blockers) {
    Write-Host "- $blocker"
  }
  exit 2
}
