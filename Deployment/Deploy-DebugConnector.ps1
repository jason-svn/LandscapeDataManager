[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int] $RevitVersion,

    [Parameter(Mandatory)]
    [string] $ConnectorAssembly,

    [Parameter(Mandatory)]
    [string] $AppAssembly
)

$ErrorActionPreference = 'Stop'

$connectorAssemblyPath = [IO.Path]::GetFullPath($ConnectorAssembly)
if (-not (Test-Path -LiteralPath $connectorAssemblyPath)) {
    throw "The debug connector was not found at $connectorAssemblyPath."
}
$connectorOutputPath = Split-Path -Parent $connectorAssemblyPath

$appAssemblyPath = [IO.Path]::GetFullPath($AppAssembly)
if (-not (Test-Path -LiteralPath $appAssemblyPath)) {
    throw "The debug companion application was not found at $appAssemblyPath."
}
$appOutputPath = Split-Path -Parent $appAssemblyPath

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$deploymentRoot = Join-Path $addinRoot 'WWP.LandscapeDataManager'
$deployedApp = Join-Path $deploymentRoot 'App'
New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null
New-Item -ItemType Directory -Path $deployedApp -Force | Out-Null

Get-ChildItem -LiteralPath $connectorOutputPath -File |
    Where-Object Extension -In '.dll', '.pdb', '.json' |
    Copy-Item -Destination $deploymentRoot -Force

Get-ChildItem -LiteralPath $appOutputPath -Force |
    Where-Object {
        $_.Name -ne 'win-x64' -and
        $_.Name -notlike '*.exe.WebView2'
    } |
    Copy-Item -Destination $deployedApp -Recurse -Force

$deployedAssembly = Join-Path $deploymentRoot 'WWP.LandscapeDataManager.Revit.dll'
$templatePath = Join-Path $PSScriptRoot '..\Manifest\LIMLandscapeData.addin.template'
$manifestPath = Join-Path $addinRoot 'LIMLandscapeData.addin'
$escapedAssemblyPath = [Security.SecurityElement]::Escape($deployedAssembly)
$manifest = (Get-Content -LiteralPath $templatePath -Raw).Replace(
    '{{ASSEMBLY_PATH}}',
    $escapedAssemblyPath)
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))

Write-Host "Deployed the Debug connector for Revit $RevitVersion to $deploymentRoot"
Write-Host "Deployed the Debug companion application to $deployedApp"
