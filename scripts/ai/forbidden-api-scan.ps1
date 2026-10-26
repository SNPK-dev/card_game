$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
$domainDir = Join-Path $root "src/Game.Domain"

if (-not (Test-Path $domainDir)) {
  Write-Error "missing domain source directory: src/Game.Domain"
}

$patterns = @(
  "DateTime.Now",
  "DateTime.UtcNow",
  "DateTimeOffset.Now",
  "DateTimeOffset.UtcNow",
  "Guid.NewGuid",
  "Random.Shared",
  "new Random(",
  "System.IO.File",
  "System.IO.Directory",
  "File.",
  "Directory.",
  "HttpClient",
  "System.Net.Http"
)

$failed = $false

Get-ChildItem $domainDir -Recurse -Filter *.cs |
  Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
  ForEach-Object {
  $file = $_.FullName
  foreach ($pattern in $patterns) {
    $matches = Select-String -LiteralPath $file -SimpleMatch -Pattern $pattern
    foreach ($match in $matches) {
      $relative = Resolve-Path -Relative $file
      Write-Error "$relative`: forbidden pattern `"$pattern`": line $($match.LineNumber)"
      $failed = $true
    }
  }
}

if ($failed) {
  exit 1
}

Write-Host "Forbidden API scan passed."
