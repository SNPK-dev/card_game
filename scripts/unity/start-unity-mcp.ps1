$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$logRoot = Join-Path $repoRoot "artifacts\unity-mcp"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$uvx = (Get-Command uvx -ErrorAction Stop).Source
$endpoint = if ($env:CARDGAME_UNITY_MCP_ENDPOINT) { $env:CARDGAME_UNITY_MCP_ENDPOINT } else { "http://127.0.0.1:18088" }
$arguments = @(
  "--from", "mcpforunityserver==10.0.0",
  "mcp-for-unity",
  "--transport", "http",
  "--http-url", $endpoint,
  "--project-scoped-tools"
)
$process = Start-Process -FilePath $uvx -ArgumentList $arguments -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru `
  -RedirectStandardOutput (Join-Path $logRoot "server.out.log") `
  -RedirectStandardError (Join-Path $logRoot "server.err.log")
Write-Host "Unity MCP process launched for $endpoint (PID $($process.Id)). Check $endpoint/health before connecting."
