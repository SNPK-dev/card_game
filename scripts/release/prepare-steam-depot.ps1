param(
  [Parameter(Mandatory=$true)][ValidatePattern('^[0-9]+$')][string]$AppId,
  [Parameter(Mandatory=$true)][ValidatePattern('^[0-9]+$')][string]$DepotId,
  [string]$Version = "latest",
  [string]$Description = "Chronicle Forge Windows release"
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$release = Join-Path $repo "artifacts\releases\$Version"
if (-not (Test-Path (Join-Path $release "client\windows\CardGame.exe"))) { throw "Build the Windows release first." }
$steamRoot = Join-Path $repo "artifacts\steam\$Version"
$content = Join-Path $steamRoot "content"
New-Item -ItemType Directory -Force -Path $content | Out-Null
Copy-Item -Path (Join-Path $release "*") -Destination $content -Recurse -Force
@"
"DepotBuildConfig"
{
  "DepotID" "$DepotId"
  "ContentRoot" "$($content.Replace('\','/'))"
  "FileMapping" { "LocalPath" "*" "DepotPath" "." "recursive" "1" }
  "FileExclusion" "logs/*"
}
"@ | Set-Content -Encoding ascii (Join-Path $steamRoot "depot_build_$DepotId.vdf")
@"
"appbuild"
{
  "appid" "$AppId"
  "desc" "$Description $Version"
  "buildoutput" "$((Join-Path $steamRoot 'output').Replace('\','/'))"
  "contentroot" "$($content.Replace('\','/'))"
  "setlive" ""
  "preview" "1"
  "depots" { "$DepotId" "$((Join-Path $steamRoot "depot_build_$DepotId.vdf").Replace('\','/'))" }
}
"@ | Set-Content -Encoding ascii (Join-Path $steamRoot "app_build_$AppId.vdf")
Write-Host "Steam depot preview prepared: $steamRoot"
Write-Host "Run steamcmd +run_app_build with the generated app VDF after reviewing it. Credentials are intentionally not stored."
