[CmdletBinding()]
param(
    [ValidateRange(2025, 2099)]
    [int] $RevitVersion = 2025
)

$ErrorActionPreference = 'Stop'

$connectorSource = Join-Path $PSScriptRoot "Connectors\Revit$RevitVersion"
$appSource = Join-Path $PSScriptRoot 'App'
$templatePath = Join-Path $PSScriptRoot 'WWP.LandscapeDataManager.addin.template'

if (-not (Test-Path -LiteralPath (Join-Path $connectorSource 'WWP.LandscapeDataManager.Revit.dll'))) {
    throw "This package does not contain a connector for Revit $RevitVersion."
}

if (-not (Test-Path -LiteralPath (Join-Path $appSource 'WWP.LandscapeDataManager.App.exe'))) {
    throw 'The WinUI 3 application payload is missing.'
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$deploymentRoot = Join-Path $addinRoot 'WWP.LandscapeDataManager'
$deployedApp = Join-Path $deploymentRoot 'App'
New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null
New-Item -ItemType Directory -Path $deployedApp -Force | Out-Null

Copy-Item -Path (Join-Path $connectorSource '*') -Destination $deploymentRoot -Recurse -Force
Copy-Item -Path (Join-Path $appSource '*') -Destination $deployedApp -Recurse -Force

$connectorPath = Join-Path $deploymentRoot 'WWP.LandscapeDataManager.Revit.dll'
$escapedAssemblyPath = [Security.SecurityElement]::Escape($connectorPath)
$manifest = (Get-Content -LiteralPath $templatePath -Raw).Replace('{{ASSEMBLY_PATH}}', $escapedAssemblyPath)
$manifestPath = Join-Path $addinRoot 'WWP.LandscapeDataManager.addin'
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))

Write-Host "Installed WWP Landscape Data Manager for Revit $RevitVersion."
Write-Host 'Restart Revit, then open EGIS > Landscape Data.'
