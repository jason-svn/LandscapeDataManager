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
$connectorOutput = Join-Path $PSScriptRoot "artifacts\Revit$RevitVersion"

# One entry per standalone tool executable. Each is published independently and deployed to its
# own App\<Name>\ subfolder so Revit's ribbon buttons can launch them as separate processes.
$toolApps = @(
    @{ Name = 'App'; Project = 'src\WWP.LandscapeDataManager.App\WWP.LandscapeDataManager.App.csproj'; ExeName = 'WWP.LandscapeDataManager.App.exe' }
    @{ Name = 'Parameters'; Project = 'src\WWP.LandscapeDataManager.App.Parameters\WWP.LandscapeDataManager.App.Parameters.csproj'; ExeName = 'WWP.LandscapeDataManager.Parameters.exe' }
    @{ Name = 'Importer'; Project = 'src\WWP.LandscapeDataManager.App.Importer\WWP.LandscapeDataManager.App.Importer.csproj'; ExeName = 'WWP.LandscapeDataManager.Importer.exe' }
    @{ Name = 'ITreeDownloader'; Project = 'src\WWP.LandscapeDataManager.App.ITreeDownloader\WWP.LandscapeDataManager.App.ITreeDownloader.csproj'; ExeName = 'WWP.LandscapeDataManager.ITreeDownloader.exe' }
)

& dotnet build $connectorProject -c $Configuration "-p:RevitVersion=$RevitVersion"
if ($LASTEXITCODE -ne 0) {
    throw 'The Revit connector build failed.'
}

foreach ($toolApp in $toolApps) {
    $toolProject = Join-Path $PSScriptRoot $toolApp.Project
    & dotnet publish $toolProject -c $Configuration -r win-x64 --self-contained false
    if ($LASTEXITCODE -ne 0) {
        throw "The '$($toolApp.Name)' tool publish failed."
    }

    $toolApp.OutputPath = Join-Path $PSScriptRoot "artifacts\$($toolApp.Name)\win-x64\publish"
    $publishedExecutable = Join-Path $toolApp.OutputPath $toolApp.ExeName
    if (-not (Test-Path -LiteralPath $publishedExecutable)) {
        throw "The '$($toolApp.Name)' executable was not produced at $publishedExecutable."
    }
}

if ($BuildOnly) {
    Write-Host "Build completed for Revit $RevitVersion."
    Write-Host "Connector: $connectorOutput"
    foreach ($toolApp in $toolApps) {
        Write-Host "$($toolApp.Name): $($toolApp.OutputPath)"
    }
    return
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$deploymentRoot = Join-Path $addinRoot 'WWP.LandscapeDataManager'
New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null

Get-ChildItem -LiteralPath $connectorOutput -File |
    Where-Object Extension -In '.dll', '.pdb', '.json' |
    Copy-Item -Destination $deploymentRoot -Force

foreach ($toolApp in $toolApps) {
    $deployedTool = Join-Path $deploymentRoot $toolApp.Name
    New-Item -ItemType Directory -Path $deployedTool -Force | Out-Null
    Copy-Item -Path (Join-Path $toolApp.OutputPath '*') -Destination $deployedTool -Recurse -Force
}

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
