param(
  [switch]$InstallOllama,
  [switch]$PullModels,
  [string]$StructuredModel = "llama3.1:8b-instruct",
  [string]$ImageModel = "sdxl-local",
  [string]$AnimationModel = "animatediff-local",
  [string]$AudioModel = "audiocraft-local",
  [string]$StructuredEndpoint = "http://127.0.0.1:11434",
  [string]$ImageEndpoint = "http://127.0.0.1:7860",
  [string]$AudioEndpoint = "http://127.0.0.1:7861"
)

$ErrorActionPreference = "Stop"

function Test-Command {
  param([string]$Name)
  return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-Endpoint {
  param([string]$Name, [string]$Uri)

  try {
    $response = Invoke-WebRequest -Uri $Uri -Method Get -TimeoutSec 5 -UseBasicParsing
    Write-Host "$Name endpoint reachable: $Uri ($($response.StatusCode))"
  }
  catch {
    Write-Host "$Name endpoint is not reachable yet: $Uri"
  }
}

Write-Host "Preparing local-only generation stack."
Write-Host "No external provider keys are required or written by this script."

if (-not (Test-Command "ollama")) {
  if ($InstallOllama) {
    if (-not (Test-Command "winget")) {
      throw "Ollama is not installed and winget is unavailable. Install Ollama manually from https://ollama.com/download, then rerun."
    }

    winget install --id Ollama.Ollama -e
  }
  else {
    Write-Host "Ollama is not installed. Rerun with -InstallOllama or install it manually."
  }
}
else {
  Write-Host "Ollama detected."
}

if ($PullModels) {
  if (-not (Test-Command "ollama")) {
    throw "Cannot pull $StructuredModel because the ollama command is not available."
  }

  ollama pull $StructuredModel
}

$env:CCG_LOCAL_STRUCTURED_ENDPOINT = "$StructuredEndpoint/v1/structured-card"
$env:CCG_LOCAL_STRUCTURED_MODEL = $StructuredModel
$env:CCG_LOCAL_IMAGE_ENDPOINT = "$ImageEndpoint/v1/assets"
$env:CCG_LOCAL_IMAGE_MODEL = $ImageModel
$env:CCG_LOCAL_ANIMATION_ENDPOINT = "$ImageEndpoint/v1/assets"
$env:CCG_LOCAL_ANIMATION_MODEL = $AnimationModel
$env:CCG_LOCAL_AUDIO_ENDPOINT = "$AudioEndpoint/v1/assets"
$env:CCG_LOCAL_AUDIO_MODEL = $AudioModel

Test-Endpoint "structured" "$StructuredEndpoint/api/tags"
Test-Endpoint "image" "$ImageEndpoint/health"
Test-Endpoint "audio" "$AudioEndpoint/health"

Write-Host ""
Write-Host "Configured local environment for this PowerShell process:"
Write-Host "CCG_LOCAL_STRUCTURED_ENDPOINT=$env:CCG_LOCAL_STRUCTURED_ENDPOINT"
Write-Host "CCG_LOCAL_STRUCTURED_MODEL=$env:CCG_LOCAL_STRUCTURED_MODEL"
Write-Host "CCG_LOCAL_IMAGE_ENDPOINT=$env:CCG_LOCAL_IMAGE_ENDPOINT"
Write-Host "CCG_LOCAL_IMAGE_MODEL=$env:CCG_LOCAL_IMAGE_MODEL"
Write-Host "CCG_LOCAL_ANIMATION_ENDPOINT=$env:CCG_LOCAL_ANIMATION_ENDPOINT"
Write-Host "CCG_LOCAL_ANIMATION_MODEL=$env:CCG_LOCAL_ANIMATION_MODEL"
Write-Host "CCG_LOCAL_AUDIO_ENDPOINT=$env:CCG_LOCAL_AUDIO_ENDPOINT"
Write-Host "CCG_LOCAL_AUDIO_MODEL=$env:CCG_LOCAL_AUDIO_MODEL"
Write-Host ""
Write-Host "Provider gateway contract:"
Write-Host "- structured gateway: POST /v1/structured-card returns output + rawStructuredOutputRef"
Write-Host "- image/animation/audio gateway: POST /v1/assets returns assetRef, format, contentHash, moderationStatus, rawAssetRef"
