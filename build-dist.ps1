# Builds a self-contained, single-file Windows distribution into .\dist
# Usage:  pwsh ./build-dist.ps1   (or)   powershell -File build-dist.ps1
$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1

$root = $PSScriptRoot
$dist = Join-Path $root "dist"

Write-Host "Cleaning $dist ..." -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }

Write-Host "Publishing single-file win-x64 (self-contained) ..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/SenseSation.Web") `
    -c Release -r win-x64 -p:PublishSingleFile=true -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Trim artifacts that shouldn't ship.
Remove-Item -Force (Join-Path $dist "*.pdb") -ErrorAction SilentlyContinue
Remove-Item -Force (Join-Path $dist "appsettings.Development.json") -ErrorAction SilentlyContinue

$exe = Join-Path $dist "SenseSation.exe"
$mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "`nDone. $exe ($mb MB)" -ForegroundColor Green
Write-Host "Run it, then set your Henrik API key in dist\appsettings.json or via env HENRIK__APIKEY." -ForegroundColor Yellow
