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
    @{ Name = 'Parameters'; Project = 'src\WWP.LandscapeDataManager.App.Parameters\WWP.LandscapeDataManager.App.Parameters.csproj'; ExeName = 'WWP.LandscapeDataManager.Parameters.exe' }
    @{ Name = 'Importer'; Project = 'src\WWP.LandscapeDataManager.App.Importer\WWP.LandscapeDataManager.App.Importer.csproj'; ExeName = 'WWP.LandscapeDataManager.Importer.exe' }
    @{ Name = 'ITreeDownloader'; Project = 'src\WWP.LandscapeDataManager.App.ITreeDownloader\WWP.LandscapeDataManager.App.ITreeDownloader.csproj'; ExeName = 'WWP.LandscapeDataManager.ITreeDownloader.exe' }
    @{ Name = 'ITreeCalculator'; Project = 'src\WWP.LandscapeDataManager.App.ITreeCalculator\WWP.LandscapeDataManager.App.ITreeCalculator.csproj'; ExeName = 'WWP.LandscapeDataManager.ITreeCalculator.exe' }
    @{ Name = 'SyncAudit'; Project = 'src\WWP.LandscapeDataManager.App.SyncAudit\WWP.LandscapeDataManager.App.SyncAudit.csproj'; ExeName = 'WWP.LandscapeDataManager.SyncAudit.exe' }
    @{ Name = 'TreeSearcher'; Project = 'src\WWP.LandscapeDataManager.App.TreeSearcher\WWP.LandscapeDataManager.App.TreeSearcher.csproj'; ExeName = 'WWP.LandscapeDataManager.TreeSearcher.exe' }
    @{ Name = 'LocationFinder'; Project = 'src\WWP.LandscapeDataManager.App.LocationFinder\WWP.LandscapeDataManager.App.LocationFinder.csproj'; ExeName = 'WWP.LandscapeDataManager.LocationFinder.exe' }
    @{ Name = 'FloorCalculator'; Project = 'src\WWP.LandscapeDataManager.App.FloorCalculator\WWP.LandscapeDataManager.App.FloorCalculator.csproj'; ExeName = 'WWP.LandscapeDataManager.FloorCalculator.exe' }
    @{ Name = 'HealthCheck'; Project = 'src\WWP.LandscapeDataManager.App.HealthCheck\WWP.LandscapeDataManager.App.HealthCheck.csproj'; ExeName = 'WWP.LandscapeDataManager.HealthCheck.exe' }
    @{ Name = 'Dashboard'; Project = 'src\WWP.LandscapeDataManager.App.Dashboard\WWP.LandscapeDataManager.App.Dashboard.csproj'; ExeName = 'WWP.LandscapeDataManager.Dashboard.exe' }
    @{ Name = 'Settings'; Project = 'src\WWP.LandscapeDataManager.App.Settings\WWP.LandscapeDataManager.App.Settings.csproj'; ExeName = 'WWP.LandscapeDataManager.Settings.exe' }
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

    # dotnet publish's file list for these unpackaged (WindowsPackageType=None) WinUI3 apps never
    # includes the app's own compiled XAML outputs — its per-page .xbf files and its resource
    # index .pri — into the RID-specific publish folder; only plain `dotnet build`, which just ran
    # as part of the publish above, writes fresh copies of those into the project's OutputPath
    # (one level up, shared across RIDs). Without copying them forward, the publish folder keeps
    # whatever stale .xbf/.pri it had from the last time this happened to work, so a newly
    # rebuilt .dll (new XAML layout, new event-connection indices) ends up deployed next to
    # mismatched compiled resources — controls silently missing or events wired to the wrong
    # element, with no build error to catch it.
    $buildOutputPath = Join-Path $PSScriptRoot "artifacts\$($toolApp.Name)"
    Get-ChildItem -LiteralPath $buildOutputPath -File -Filter '*.xbf' |
        Copy-Item -Destination $toolApp.OutputPath -Force
    $priName = [IO.Path]::GetFileNameWithoutExtension($toolApp.ExeName) + '.pri'
    $priPath = Join-Path $buildOutputPath $priName
    if (Test-Path -LiteralPath $priPath) {
        Copy-Item -LiteralPath $priPath -Destination $toolApp.OutputPath -Force
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
