# Builds a self-contained, single-file Windows distribution of the desktop app into .\dist
# Usage:  powershell -File build-dist.ps1
$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1

$root = $PSScriptRoot
$dist = Join-Path $root "dist"

Write-Host "Cleaning $dist ..." -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }

Write-Host "Publishing single-file win-x64 (self-contained) ..." -ForegroundColor Cyan
# InvariantGlobalization must stay false: RadiantConnect creates CultureInfo("en-us").
dotnet publish (Join-Path $root "src/SenseSation.Desktop") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:InvariantGlobalization=false -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Remove-Item -Force (Join-Path $dist "*.pdb") -ErrorAction SilentlyContinue

$exe = Join-Path $dist "SenseSation.exe"
$mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "`nDone. $exe ($mb MB)" -ForegroundColor Green
Write-Host "Run it. Set your HenrikDev key in Settings, or leave blank for local-client mode." -ForegroundColor Yellow
