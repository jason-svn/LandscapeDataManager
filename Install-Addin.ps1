[CmdletBinding()]
param(
    [ValidateRange(2025, 2099)]
    [int] $RevitVersion = 2025,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $BuildOnly
)

$ErrorActionPreference = 'Stop'

$revitDirectory = "C:\Program Files\Autodesk\Revit $RevitVersion"
if (-not (Test-Path -LiteralPath (Join-Path $revitDirectory 'RevitAPI.dll'))) {
    throw "Revit $RevitVersion is not installed at $revitDirectory."
}

$connectorProject = Join-Path $PSScriptRoot 'src\WWP.LandscapeDataManager.Revit\WWP.LandscapeDataManager.Revit.csproj'
$appProject = Join-Path $PSScriptRoot 'src\WWP.LandscapeDataManager.App\WWP.LandscapeDataManager.App.csproj'
$connectorOutput = Join-Path $PSScriptRoot "artifacts\Revit$RevitVersion"
$appOutput = Join-Path $PSScriptRoot 'artifacts\App\win-x64\publish'

& dotnet build $connectorProject -c $Configuration "-p:RevitVersion=$RevitVersion"
if ($LASTEXITCODE -ne 0) {
    throw 'The Revit connector build failed.'
}

& dotnet publish $appProject -c $Configuration -r win-x64 --self-contained false
if ($LASTEXITCODE -ne 0) {
    throw 'The WinUI 3 application publish failed.'
}

$publishedExecutable = Join-Path $appOutput 'WWP.LandscapeDataManager.App.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "The WinUI 3 executable was not produced at $publishedExecutable."
}

if ($BuildOnly) {
    Write-Host "Build completed for Revit $RevitVersion."
    Write-Host "Connector: $connectorOutput"
    Write-Host "WinUI app: $appOutput"
    return
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$deploymentRoot = Join-Path $addinRoot 'WWP.LandscapeDataManager'
$deployedApp = Join-Path $deploymentRoot 'App'
New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null
New-Item -ItemType Directory -Path $deployedApp -Force | Out-Null

Get-ChildItem -LiteralPath $connectorOutput -File |
    Where-Object Extension -In '.dll', '.pdb', '.json' |
    Copy-Item -Destination $deploymentRoot -Force
Copy-Item -Path (Join-Path $appOutput '*') -Destination $deployedApp -Recurse -Force

$connectorPath = Join-Path $deploymentRoot 'WWP.LandscapeDataManager.Revit.dll'
if (-not (Test-Path -LiteralPath $connectorPath)) {
    throw "The connector assembly was not produced at $connectorPath."
}

$templatePath = Join-Path $PSScriptRoot 'Manifest\LIMLandscapeData.addin.template'
$manifestPath = Join-Path $addinRoot 'LIMLandscapeData.addin'
foreach ($legacyName in 'WWPLandscapeDataManager.addin', 'WWP.LandscapeDataManager.addin') {
    $legacyManifestPath = Join-Path $addinRoot $legacyName
    if (Test-Path -LiteralPath $legacyManifestPath) {
        Remove-Item -LiteralPath $legacyManifestPath -Force
    }
}
$escapedAssemblyPath = [Security.SecurityElement]::Escape($connectorPath)
$manifest = (Get-Content -LiteralPath $templatePath -Raw).Replace('{{ASSEMBLY_PATH}}', $escapedAssemblyPath)
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))

Write-Host "Installed LIM- LANDSCAPE DATA for Revit $RevitVersion."
Write-Host "Restart Revit, then use EGIS > LIM- LANDSCAPE DATA."
