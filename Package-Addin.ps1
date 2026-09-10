[CmdletBinding()]
param(
    [int[]] $RevitVersions = @(2025, 2026),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

foreach ($version in $RevitVersions) {
    if ($version -lt 2025) {
        throw "Revit $version is unsupported. The minimum version is Revit 2025."
    }
}

$connectorProject = Join-Path $PSScriptRoot 'src\WWP.LandscapeDataManager.Revit\WWP.LandscapeDataManager.Revit.csproj'
$packagesRoot = Join-Path $PSScriptRoot 'artifacts\Packages'
$stagingRoot = Join-Path $packagesRoot 'LIM-Landscape-Data-2025plus'
$zipPath = "$stagingRoot.zip"

# One entry per standalone tool executable; each publishes and stages into its own App\<Name>\ folder.
# Kept in sync with the Revit ribbon's command set (see OpenWorkflowCommands.cs) — every tool
# launchable from EGIS > LIM- LANDSCAPE DATA must have an entry here or the deployed add-in
# will be missing that tool's .exe.
$toolApps = @(
    @{ Name = 'Parameters'; Project = 'src\WWP.LandscapeDataManager.App.Parameters\WWP.LandscapeDataManager.App.Parameters.csproj' }
    @{ Name = 'Importer'; Project = 'src\WWP.LandscapeDataManager.App.Importer\WWP.LandscapeDataManager.App.Importer.csproj' }
    @{ Name = 'ITreeDownloader'; Project = 'src\WWP.LandscapeDataManager.App.ITreeDownloader\WWP.LandscapeDataManager.App.ITreeDownloader.csproj' }
    @{ Name = 'ITreeCalculator'; Project = 'src\WWP.LandscapeDataManager.App.ITreeCalculator\WWP.LandscapeDataManager.App.ITreeCalculator.csproj' }
    @{ Name = 'SyncAudit'; Project = 'src\WWP.LandscapeDataManager.App.SyncAudit\WWP.LandscapeDataManager.App.SyncAudit.csproj' }
    @{ Name = 'TreeSearcher'; Project = 'src\WWP.LandscapeDataManager.App.TreeSearcher\WWP.LandscapeDataManager.App.TreeSearcher.csproj' }
    @{ Name = 'LocationFinder'; Project = 'src\WWP.LandscapeDataManager.App.LocationFinder\WWP.LandscapeDataManager.App.LocationFinder.csproj' }
    @{ Name = 'FloorCalculator'; Project = 'src\WWP.LandscapeDataManager.App.FloorCalculator\WWP.LandscapeDataManager.App.FloorCalculator.csproj' }
    @{ Name = 'HealthCheck'; Project = 'src\WWP.LandscapeDataManager.App.HealthCheck\WWP.LandscapeDataManager.App.HealthCheck.csproj' }
    @{ Name = 'Dashboard'; Project = 'src\WWP.LandscapeDataManager.App.Dashboard\WWP.LandscapeDataManager.App.Dashboard.csproj' }
    @{ Name = 'Settings'; Project = 'src\WWP.LandscapeDataManager.App.Settings\WWP.LandscapeDataManager.App.Settings.csproj' }
)

foreach ($toolApp in $toolApps) {
    & dotnet publish (Join-Path $PSScriptRoot $toolApp.Project) -c $Configuration -r win-x64 --self-contained false
    if ($LASTEXITCODE -ne 0) {
        throw "The '$($toolApp.Name)' tool publish failed."
    }
}

foreach ($version in $RevitVersions) {
    & dotnet build $connectorProject -c $Configuration "-p:RevitVersion=$version"
    if ($LASTEXITCODE -ne 0) {
        throw "The Revit $version connector build failed."
    }
}

$resolvedPackagesRoot = [IO.Path]::GetFullPath($packagesRoot)
$resolvedStagingRoot = [IO.Path]::GetFullPath($stagingRoot)
if (-not $resolvedStagingRoot.StartsWith($resolvedPackagesRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The calculated staging directory is outside the package output directory.'
}

if (Test-Path -LiteralPath $resolvedStagingRoot) {
    Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $resolvedStagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $resolvedStagingRoot 'Connectors') -Force | Out-Null

foreach ($toolApp in $toolApps) {
    $publishedTool = Join-Path $PSScriptRoot "artifacts\$($toolApp.Name)\win-x64\publish"
    $stagedTool = Join-Path $resolvedStagingRoot "App\$($toolApp.Name)"
    New-Item -ItemType Directory -Path $stagedTool -Force | Out-Null
    Copy-Item -Path (Join-Path $publishedTool '*') -Destination $stagedTool -Recurse -Force
}

foreach ($version in $RevitVersions) {
    $source = Join-Path $PSScriptRoot "artifacts\Revit$version"
    $destination = Join-Path $resolvedStagingRoot "Connectors\Revit$version"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -File |
        Where-Object Extension -In '.dll', '.pdb', '.json' |
        Copy-Item -Destination $destination -Force
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Deployment\Deploy-BinaryPackage.ps1') -Destination $resolvedStagingRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Deployment\README.txt') -Destination $resolvedStagingRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Manifest\LIMLandscapeData.addin.template') -Destination $resolvedStagingRoot -Force

Compress-Archive -Path (Join-Path $resolvedStagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Created deployment package: $zipPath"
