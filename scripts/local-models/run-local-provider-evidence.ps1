param(
  [string]$StructuredEndpoint = $env:CCG_LOCAL_STRUCTURED_ENDPOINT,
  [string]$StructuredModel = $env:CCG_LOCAL_STRUCTURED_MODEL,
  [string]$ImageEndpoint = $env:CCG_LOCAL_IMAGE_ENDPOINT,
  [string]$ImageModel = $env:CCG_LOCAL_IMAGE_MODEL,
  [string]$AnimationEndpoint = $env:CCG_LOCAL_ANIMATION_ENDPOINT,
  [string]$AnimationModel = $env:CCG_LOCAL_ANIMATION_MODEL,
  [string]$AudioEndpoint = $env:CCG_LOCAL_AUDIO_ENDPOINT,
  [string]$AudioModel = $env:CCG_LOCAL_AUDIO_MODEL,
  [switch]$SkipAudio,
  [string]$OutputRoot = "artifacts/local-provider-evidence"
)

$ErrorActionPreference = "Stop"

function Require-Value {
  param([string]$Name, [string]$Value)

  if ([string]::IsNullOrWhiteSpace($Value)) {
    throw "$Name is required. Set it as an argument or environment variable."
  }
}

function Assert-LocalEndpoint {
  param([string]$Name, [string]$Value)

  $uri = [Uri]$Value
  $isLocal = $uri.IsLoopback -or $uri.Host.Equals("host.docker.internal", [StringComparison]::OrdinalIgnoreCase)
  if (-not $isLocal) {
    throw "$Name must point to a local endpoint. Refusing non-local URI: $Value"
  }
}

function Invoke-JsonPost {
  param([string]$Name, [string]$Uri, [object]$Body)

  $json = $Body | ConvertTo-Json -Depth 64
  try {
    return Invoke-RestMethod -Uri $Uri -Method Post -Body $json -ContentType "application/json" -TimeoutSec 300
  }
  catch {
    throw "$Name provider request failed at $Uri. $($_.Exception.Message)"
  }
}

function Write-JsonFile {
  param([string]$Path, [object]$Value)

  $Value | ConvertTo-Json -Depth 64 | Set-Content -Path $Path -Encoding UTF8
  return $Path
}

function Get-FileHashRef {
  param([string]$Path)

  $hash = Get-FileHash -Algorithm SHA256 -Path $Path
  return "sha256:$($hash.Hash.ToLowerInvariant())"
}

Require-Value "StructuredEndpoint" $StructuredEndpoint
Require-Value "StructuredModel" $StructuredModel
Require-Value "ImageEndpoint" $ImageEndpoint
Require-Value "ImageModel" $ImageModel
Require-Value "AnimationEndpoint" $AnimationEndpoint
Require-Value "AnimationModel" $AnimationModel
if (-not $SkipAudio) {
  Require-Value "AudioEndpoint" $AudioEndpoint
  Require-Value "AudioModel" $AudioModel
}

Assert-LocalEndpoint "StructuredEndpoint" $StructuredEndpoint
Assert-LocalEndpoint "ImageEndpoint" $ImageEndpoint
Assert-LocalEndpoint "AnimationEndpoint" $AnimationEndpoint
if (-not $SkipAudio) {
  Assert-LocalEndpoint "AudioEndpoint" $AudioEndpoint
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = Join-Path $OutputRoot $timestamp
New-Item -ItemType Directory -Force $outputDir | Out-Null

$structuredRequest = @{
  generationId = "local-provider-evidence-$timestamp"
  promptVersion = "provider-evidence-local-v1"
  modelId = $StructuredModel
  prompt = "Return a constrained JSON card proposal for a deterministic CCG test minion named Local Evidence Sentry."
  contextRefs = @("docs/generator/main.md", "docs/engine/asset_generation_spec.md")
  timeoutMs = 60000
}
$structuredResponse = Invoke-JsonPost "structured" $StructuredEndpoint $structuredRequest
$structuredPath = Write-JsonFile (Join-Path $outputDir "structured-response.json") $structuredResponse

$assetRequests = @(
  @{
    role = "image"
    endpoint = $ImageEndpoint
    model = $ImageModel
    body = @{
      generationId = "local-provider-evidence-$timestamp-image"
      cardId = "local_evidence_sentry"
      cardVersionId = "local_evidence_sentry_v1"
      assetKind = "STATIC_IMAGE"
      prompt = "Production card art for Local Evidence Sentry, clean readable fantasy card illustration."
      styleGuideRef = "style-guide-local-v1"
      safetyPolicyRef = "safety-policy-local-v1"
      modelId = $ImageModel
      timeoutMs = 120000
    }
  },
  @{
    role = "animation"
    endpoint = $AnimationEndpoint
    model = $AnimationModel
    body = @{
      generationId = "local-provider-evidence-$timestamp-animation"
      cardId = "local_evidence_sentry"
      cardVersionId = "local_evidence_sentry_v1"
      assetKind = "IDLE_ANIMATION"
      prompt = "Short idle card animation for Local Evidence Sentry, subtle glow and motion."
      styleGuideRef = "style-guide-local-v1"
      safetyPolicyRef = "safety-policy-local-v1"
      modelId = $AnimationModel
      timeoutMs = 120000
    }
  }
)

if (-not $SkipAudio) {
  $assetRequests += @{
    role = "audio"
    endpoint = $AudioEndpoint
    model = $AudioModel
    body = @{
      generationId = "local-provider-evidence-$timestamp-audio"
      cardId = "local_evidence_sentry"
      cardVersionId = "local_evidence_sentry_v1"
      assetKind = "AUDIO"
      prompt = "Short nonverbal magical chime for Local Evidence Sentry."
      styleGuideRef = "audio-style-local-v1"
      safetyPolicyRef = "safety-policy-local-v1"
      modelId = $AudioModel
      timeoutMs = 120000
    }
  }
}

$artifactEvidence = @(
  @{
    artifactKind = "STRUCTURED_OUTPUT"
    artifactRef = if ($structuredResponse.rawStructuredOutputRef) { $structuredResponse.rawStructuredOutputRef } else { $structuredPath }
    contentHash = Get-FileHashRef $structuredPath
  }
)

$providerEvidence = @(
  @{
    providerRole = "structured"
    providerName = "local-structured"
    modelId = $StructuredModel
    endpointHost = ([Uri]$StructuredEndpoint).Host
    apiKeyStatus = "MISSING"
    isConfiguredForLiveEndpoint = $false
    evidenceClassification = "LOCAL_CONFIGURED"
  }
)

foreach ($assetRequest in $assetRequests) {
  $response = Invoke-JsonPost $assetRequest.role $assetRequest.endpoint $assetRequest.body
  $path = Write-JsonFile (Join-Path $outputDir "$($assetRequest.role)-response.json") $response
  $artifactEvidence += @{
    artifactKind = $assetRequest.body.assetKind
    artifactRef = if ($response.assetRef) { $response.assetRef } else { $path }
    contentHash = if ($response.contentHash) { $response.contentHash } else { Get-FileHashRef $path }
  }
  $providerEvidence += @{
    providerRole = $assetRequest.role
    providerName = "local-$($assetRequest.role)"
    modelId = $assetRequest.model
    endpointHost = ([Uri]$assetRequest.endpoint).Host
    apiKeyStatus = "MISSING"
    isConfiguredForLiveEndpoint = $false
    evidenceClassification = "LOCAL_CONFIGURED"
  }
}

$report = @{
  reportId = "local-provider-evidence-$timestamp"
  createdAt = (Get-Date).ToUniversalTime().ToString("o")
  status = "Succeeded"
  hasLiveProviderEvidence = $false
  hasConfiguredProviderEvidence = $true
  providers = $providerEvidence
  artifacts = $artifactEvidence
  diagnostics = @()
  outputDirectory = $outputDir
}

$reportPath = Write-JsonFile (Join-Path $outputDir "provider-evidence-report.json") $report
Write-Host "Local provider evidence succeeded: $reportPath"
