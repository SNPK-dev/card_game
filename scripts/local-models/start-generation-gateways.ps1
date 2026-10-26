param(
  [ValidateSet("Local", "OpenAI")]
  [string]$ProviderMode = "Local",
  [int]$StructuredPort = 8011,
  [int]$ImagePort = 8012,
  [int]$DiffusersPort = 7860,
  [string]$LocalStructuredModel = "qwen3.5:latest",
  [string]$LocalImageModel = "stabilityai/sd-turbo",
  [string]$Python = "python",
  [switch]$PullMissingModel,
  [string]$OpenAIStructuredModel = "gpt-5.6",
  [string]$OpenAIImageModel = "gpt-image-1.5"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if ($ProviderMode -eq "OpenAI") {
  $envFile = Join-Path $repoRoot ".env"
  if (Test-Path -LiteralPath $envFile) {
    foreach ($line in Get-Content -LiteralPath $envFile) {
      if ($line -match '^\s*OPENAI_API_KEY\s*=\s*(.+?)\s*$') {
        $env:OPENAI_API_KEY = $Matches[1].Trim('"', "'")
      }
    }
  }
  if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    throw "OPENAI_API_KEY is required for OpenAI provider mode."
  }
  $env:CCG_GENERATION_PROVIDER = "openai"
  $env:CCG_IMAGE_PROVIDER = "openai"
  $env:CCG_OPENAI_STRUCTURED_MODEL = $OpenAIStructuredModel
  $env:CCG_OPENAI_IMAGE_MODEL = $OpenAIImageModel
} else {
  $ollamaCommand = Get-Command ollama -ErrorAction SilentlyContinue
  $defaultOllama = Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"
  if ($null -eq $ollamaCommand -and (Test-Path -LiteralPath $defaultOllama)) {
    $ollamaCommand = Get-Command $defaultOllama
  }
  if ($null -eq $ollamaCommand) {
    throw "Ollama is required for Local mode. Install it first or run this script with -ProviderMode OpenAI."
  }
  $installedModels = @(& $ollamaCommand.Source list 2>$null | Select-Object -Skip 1 | ForEach-Object { ($_ -split '\s+')[0] })
  if ($LASTEXITCODE -ne 0) { throw "Ollama is not running. Start Ollama before the gateways." }
  if ($installedModels -notcontains $LocalStructuredModel) {
    if (-not $PullMissingModel) {
      throw "Local structured model '$LocalStructuredModel' is missing. Rerun with -PullMissingModel to download it."
    }
    & $ollamaCommand.Source pull $LocalStructuredModel
    if ($LASTEXITCODE -ne 0) { throw "Ollama failed to download '$LocalStructuredModel'." }
  }
  $env:CCG_GENERATION_PROVIDER = "local"
  $env:CCG_IMAGE_PROVIDER = "local"
  $env:CCG_LOCAL_STRUCTURED_MODEL = $LocalStructuredModel
  $env:CCG_DIFFUSERS_MODEL = $LocalImageModel
  $env:CCG_SD_WEBUI_URL = "http://127.0.0.1:$DiffusersPort"
}

$pythonExecutable = (Get-Command $Python -ErrorAction Stop).Source
if ($ProviderMode -eq "Local") {
  & $pythonExecutable -c "import torch, diffusers, transformers, accelerate, safetensors, PIL, fastapi, uvicorn"
  if ($LASTEXITCODE -ne 0) { throw "Local image dependencies are missing. Install requirements.txt with the selected Python interpreter." }
}
$structuredScript = Join-Path $PSScriptRoot "gateways\ollama_structured_gateway.py"
$imageScript = Join-Path $PSScriptRoot "gateways\stable_diffusion_asset_gateway.py"
$diffusersScript = Join-Path $PSScriptRoot "gateways\diffusers_a1111_api.py"
$logRoot = Join-Path $repoRoot "artifacts\provider-gateway-logs"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$diffusers = $null
if ($ProviderMode -eq "Local") {
  $diffusers = Start-Process -FilePath $pythonExecutable -ArgumentList @(('"' + $diffusersScript + '"'), $DiffusersPort) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logRoot "diffusers.out.log") -RedirectStandardError (Join-Path $logRoot "diffusers.err.log")
}
$structured = Start-Process -FilePath $pythonExecutable -ArgumentList @(('"' + $structuredScript + '"'), $StructuredPort) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logRoot "structured.out.log") -RedirectStandardError (Join-Path $logRoot "structured.err.log")
$image = Start-Process -FilePath $pythonExecutable -ArgumentList @(('"' + $imageScript + '"'), $ImagePort) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logRoot "image.out.log") -RedirectStandardError (Join-Path $logRoot "image.err.log")

Write-Host "Generation gateways started in $ProviderMode mode."
Write-Host "Structured endpoint: http://127.0.0.1:$StructuredPort/v1/structured-card (PID $($structured.Id))"
Write-Host "Image endpoint: http://127.0.0.1:$ImagePort/v1/assets (PID $($image.Id))"
if ($null -ne $diffusers) { Write-Host "Diffusers backend: http://127.0.0.1:$DiffusersPort (PID $($diffusers.Id)); first start downloads $LocalImageModel and can take several minutes." }
Write-Host "API credentials were not printed."
