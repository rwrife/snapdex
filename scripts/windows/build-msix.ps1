[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$OutputRoot = "artifacts/msix",
    [string]$PackageProject = "packaging/Snapdex.Package/Snapdex.Package.wapproj"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$packageProjectPath = Join-Path $repoRoot $PackageProject
$outputDir = (Join-Path $repoRoot $OutputRoot)

if (-not (Test-Path $packageProjectPath)) {
    throw "Packaging project not found: $packageProjectPath"
}

function Get-MsBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/Installer/vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild/**/Bin/MSBuild.exe" | Select-Object -First 1
        if ($found) {
            return $found
        }
    }

    $msbuildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($msbuildCommand) {
        return $msbuildCommand.Source
    }

    throw "MSBuild not found. Install Visual Studio Build Tools (MSBuild + MSIX Packaging Tools)."
}

$msbuild = Get-MsBuildPath
Write-Host "Using MSBuild: $msbuild"

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& $msbuild $packageProjectPath `
  /restore `
  /t:Build `
  /p:Configuration=$Configuration `
  /p:Platform=$Platform `
  /p:GenerateAppxPackageOnBuild=true `
  /p:UapAppxPackageBuildMode=SideloadOnly `
  /p:AppxBundle=Never `
  /p:AppxPackageSigningEnabled=false `
  "/p:AppxPackageDir=$outputDir\\"

$msix = Get-ChildItem -Path $outputDir -Filter *.msix -File -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) {
    throw "Build completed but no .msix output was found under $outputDir"
}

Write-Host "MSIX package ready: $($msix.FullName)"
Write-Host "Install (Developer Mode): Add-AppxPackage -Path '$($msix.FullName)' -AllowUnsigned"
