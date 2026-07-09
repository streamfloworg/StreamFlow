# Builds and Velopack-packs the unpackaged StreamFlow distribution for a GitHub Release.
# Does NOT publish anything itself -- it only produces the ./releases folder; uploading is a
# separate, explicit step (see the printed instructions at the end).
#
# Prereqs (one-time):
#   dotnet tool install -g vpk
#   (gh CLI, already used elsewhere in this repo's workflow, if you want to upload via gh release create)
#
# Usage:
#   ./scripts/release.ps1 -Version 1.2.3

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish\win-x64"
$releasesDir = Join-Path $root "releases"

Write-Host "Building native core (release)..."
Push-Location (Join-Path $root "native")
try { cargo build --release -p streamflow-core } finally { Pop-Location }

Write-Host "Publishing self-contained win-x64 build (v$Version)..."
dotnet publish (Join-Path $root "StreamFlow.App\StreamFlow.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o $publishDir

Write-Host "Packing with Velopack..."
vpk pack --packId StreamFlow --packVersion $Version --packDir $publishDir `
    --packTitle "StreamFlow"  --icon assets\icon.ico --splashImage  --mainExe StreamFlow.App.exe --outputDir $releasesDir

Write-Host ""
Write-Host "Packed to $releasesDir. To publish as a GitHub Release (not done automatically):"
Write-Host "  gh release create v$Version `"$releasesDir\*`" --title `"v$Version`""
