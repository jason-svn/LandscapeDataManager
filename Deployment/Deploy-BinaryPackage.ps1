[CmdletBinding()]
param(
    [ValidateRange(2025, 2099)]
    [int] $RevitVersion = 2025
)

$ErrorActionPreference = 'Stop'
$started = Get-Date

$connectorSource = Join-Path $PSScriptRoot "Connectors\Revit$RevitVersion"
$appSource = Join-Path $PSScriptRoot 'App'
$templatePath = Join-Path $PSScriptRoot 'LIMLandscapeData.addin.template'

if (-not (Test-Path -LiteralPath (Join-Path $connectorSource 'WWP.LandscapeDataManager.Revit.dll'))) {
    throw "This package does not contain a connector for Revit $RevitVersion."
}

# The package's App\ folder holds one subfolder per standalone tool executable (App, Parameters, ...).
$toolSources = @(Get-ChildItem -LiteralPath $appSource -Directory)
if ($toolSources.Count -eq 0) {
    throw 'The tool application payload is missing.'
}

# Revit keeps the connector DLL locked, so copying over it while Revit runs fails part-way.
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Revit is open. Save your work, close every Revit window, then run the installer again.'
}

# The copy is ~660 MB and takes a minute or two with no output of its own, so say what's
# happening at every step - a silent window looks frozen.
Write-Host ''
Write-Host "Installing LIM- Landscape Data for Revit $RevitVersion." -ForegroundColor Cyan
Write-Host 'This can take a couple of minutes. Please leave this window open until it says Done.'
Write-Host ''

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$deploymentRoot = Join-Path $addinRoot 'WWP.LandscapeDataManager'
New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null

Write-Host 'Step 1 of 4: Copying the Revit connector...'
Copy-Item -Path (Join-Path $connectorSource '*') -Destination $deploymentRoot -Recurse -Force

Write-Host "Step 2 of 4: Copying the $($toolSources.Count) LIM tools..."
$index = 0
foreach ($toolSource in $toolSources) {
    $index++
    Write-Host ("  [{0}/{1}] {2}" -f $index, $toolSources.Count, $toolSource.Name)
    Write-Progress -Activity "Installing LIM for Revit $RevitVersion" -Status "Copying $($toolSource.Name) ($index of $($toolSources.Count))" `
        -PercentComplete ([int](100 * ($index - 1) / $toolSources.Count))
    $deployedTool = Join-Path $deploymentRoot $toolSource.Name
    New-Item -ItemType Directory -Path $deployedTool -Force | Out-Null
    Copy-Item -Path (Join-Path $toolSource.FullName '*') -Destination $deployedTool -Recurse -Force
}

Write-Progress -Activity "Installing LIM for Revit $RevitVersion" -Completed

# Files extracted from a downloaded ZIP carry the "from the internet" mark, and Copy-Item keeps it;
# clear it so the tool executables launch without SmartScreen/"unknown publisher" prompts.
Write-Host 'Step 3 of 4: Clearing the "downloaded from the internet" mark...'
Get-ChildItem -LiteralPath $deploymentRoot -Recurse -File | Unblock-File

Write-Host 'Step 4 of 4: Registering the add-in with Revit...'
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

$fileVersion = (Get-Item -LiteralPath $connectorPath).VersionInfo.FileVersion
$version = if ($fileVersion) { ($fileVersion -split '\.')[0..2] -join '.' } else { $null }
$elapsed = [int]((Get-Date) - $started).TotalSeconds
Write-Host ''
Write-Host "Done - installed LIM- Landscape Data $(if ($version) { "v$version " })for Revit $RevitVersion in $elapsed seconds." -ForegroundColor Green
Write-Host 'Start Revit and open the LIM tab.'
