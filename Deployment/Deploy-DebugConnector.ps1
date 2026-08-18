[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int] $RevitVersion,

    [Parameter(Mandatory)]
    [string] $ConnectorAssembly,

    # Semicolon-separated "ToolFolderName=PathToAssembly.dll" pairs, one per standalone tool exe
    # (e.g. "Parameters=...\artifacts\Parameters\WWP.LandscapeDataManager.Parameters.dll;Importer=...").
    # A single delimited string (not [string[]]) because this script is invoked via
    # "powershell.exe -File", which does not reliably bind repeated/array command-line
    # arguments the way an in-process PowerShell function call does.
    # Each tool is deployed to its own App\<ToolFolderName>\ subfolder.
    [string] $ToolAssembly = ''
)

$toolAssemblyPairs = $ToolAssembly -split ';' | Where-Object { $_ }

$ErrorActionPreference = 'Stop'

$connectorAssemblyPath = [IO.Path]::GetFullPath($ConnectorAssembly)
if (-not (Test-Path -LiteralPath $connectorAssemblyPath)) {
    throw "The debug connector was not found at $connectorAssemblyPath."
}
$connectorOutputPath = Split-Path -Parent $connectorAssemblyPath

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$deploymentRoot = Join-Path $addinRoot 'WWP.LandscapeDataManager'
New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null

Get-ChildItem -LiteralPath $connectorOutputPath -File |
    Where-Object Extension -In '.dll', '.pdb', '.json' |
    Copy-Item -Destination $deploymentRoot -Force

foreach ($pair in $toolAssemblyPairs) {
    $separatorIndex = $pair.IndexOf('=')
    if ($separatorIndex -lt 1) {
        throw "Invalid -ToolAssembly value '$pair'. Expected 'FolderName=PathToAssembly.dll'."
    }

    $toolName = $pair.Substring(0, $separatorIndex)
    $toolAssemblyPath = [IO.Path]::GetFullPath($pair.Substring($separatorIndex + 1))
    if (-not (Test-Path -LiteralPath $toolAssemblyPath)) {
        throw "The '$toolName' tool was not found at $toolAssemblyPath."
    }

    $toolOutputPath = Split-Path -Parent $toolAssemblyPath
    $deployedTool = Join-Path $deploymentRoot $toolName
    New-Item -ItemType Directory -Path $deployedTool -Force | Out-Null
    Get-ChildItem -LiteralPath $toolOutputPath -Force |
        Where-Object {
            $_.Name -ne 'win-x64' -and
            $_.Name -notlike '*.exe.WebView2'
        } |
        Copy-Item -Destination $deployedTool -Recurse -Force
    Write-Host "Deployed the '$toolName' tool to $deployedTool"
}

$deployedAssembly = Join-Path $deploymentRoot 'WWP.LandscapeDataManager.Revit.dll'
$templatePath = Join-Path $PSScriptRoot '..\Manifest\LIMLandscapeData.addin.template'
$manifestPath = Join-Path $addinRoot 'LIMLandscapeData.addin'
$escapedAssemblyPath = [Security.SecurityElement]::Escape($deployedAssembly)
$manifest = (Get-Content -LiteralPath $templatePath -Raw).Replace(
    '{{ASSEMBLY_PATH}}',
    $escapedAssemblyPath)
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))

Write-Host "Deployed the Debug connector for Revit $RevitVersion to $deploymentRoot"
