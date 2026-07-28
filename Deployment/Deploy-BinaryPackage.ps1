[CmdletBinding()]
param(
    [ValidateRange(2025, 2099)]
    [int] $RevitVersion = 2025
)

$ErrorActionPreference = 'Stop'

$connectorSource = Join-Path $PSScriptRoot "Connectors\Revit$RevitVersion"
$appSource = Join-Path $PSScriptRoot 'App'
$templatePath = Join-Path $PSScriptRoot 'LIMLandscapeData.addin.template'

if (-not (Test-Path -LiteralPath (Join-Path $connectorSource 'WWP.LandscapeDataManager.Revit.dll'))) {
    throw "This package does not contain a connector for Revit $RevitVersion."
}

# The package's App\ folder holds one subfolder per standalone tool executable (App, Parameters, ...).
$toolSources = Get-ChildItem -LiteralPath $appSource -Directory
if ($toolSources.Count -eq 0) {
    throw 'The tool application payload is missing.'
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$deploymentRoot = Join-Path $addinRoot 'WWP.LandscapeDataManager'
New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null

Copy-Item -Path (Join-Path $connectorSource '*') -Destination $deploymentRoot -Recurse -Force

foreach ($toolSource in $toolSources) {
    $deployedTool = Join-Path $deploymentRoot $toolSource.Name
    New-Item -ItemType Directory -Path $deployedTool -Force | Out-Null
    Copy-Item -Path (Join-Path $toolSource.FullName '*') -Destination $deployedTool -Recurse -Force
}

$connectorPath = Join-Path $deploymentRoot 'WWP.LandscapeDataManager.Revit.dll'
$escapedAssemblyPath = [Security.SecurityElement]::Escape($connectorPath)
$manifest = (Get-Content -LiteralPath $templatePath -Raw).Replace('{{ASSEMBLY_PATH}}', $escapedAssemblyPath)
$manifestPath = Join-Path $addinRoot 'LIMLandscapeData.addin'
foreach ($legacyName in 'WWPLandscapeDataManager.addin', 'WWP.LandscapeDataManager.addin') {
    $legacyManifestPath = Join-Path $addinRoot $legacyName
    if (Test-Path -LiteralPath $legacyManifestPath) {
        Remove-Item -LiteralPath $legacyManifestPath -Force
    }
}
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))

Write-Host "Installed LIM- LANDSCAPE DATA for Revit $RevitVersion."
Write-Host 'Restart Revit, then open EGIS > LIM- LANDSCAPE DATA.'
