[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts/portable",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$projectPath = Join-Path $repoRoot "src/Snapdex.App/Snapdex.App.csproj"
$outputDir = Join-Path $repoRoot (Join-Path $OutputRoot $RuntimeIdentifier)
$selfContainedValue = if ($SelfContained.IsPresent) { "true" } else { "false" }

Write-Host "Publishing portable snapdex build..."
Write-Host "  Project: $projectPath"
Write-Host "  Output : $outputDir"
Write-Host "  Config : $Configuration"
Write-Host "  RID    : $RuntimeIdentifier"
Write-Host "  Self-contained: $selfContainedValue"

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& dotnet publish $projectPath `
  -c $Configuration `
  -r $RuntimeIdentifier `
  --self-contained $selfContainedValue `
  -p:PublishSingleFile=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $outputDir

$exe = Join-Path $outputDir "Snapdex.App.exe"
if (-not (Test-Path $exe)) {
    throw "Publish finished but expected executable was not found: $exe"
}

Write-Host "Portable build ready: $exe"
Write-Host "You can zip this folder and distribute it as a portable release artifact."
