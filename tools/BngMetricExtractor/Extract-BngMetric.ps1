<#
.SYNOPSIS
    Extracts the lookup tables the BNG calculator needs from the official Statutory Biodiversity
    Metric Calculation Tool (.xlsm) into a versioned JSON file shipped with the add-in.

.DESCRIPTION
    Re-run this whenever Defra publishes a new metric version; the calculator itself reads only
    the JSON, so a new metric is a data change, not a code change. Uses Excel COM (Excel must be
    installed). If the workbook is already open in Excel, that instance is reused and left open;
    otherwise it is opened read-only and closed afterwards.

    Tables extracted (sheet references are the 23.07.2024 release):
      - G-1 All Habitats        habitat name, description, distinctiveness, broad habitat lists
      - G-3 Multipliers         creation/enhancement difficulty, strategic significance, difficulty multipliers
      - G-4 Temporal multipliers standard time to target condition per habitat x condition, year multipliers
      - G-5 Enhancement Temporal time to target per habitat x (baseline condition - proposed condition)
      - G-8 Condition Look up   condition score per habitat x condition, condition groups
      Also the A-1 baseline and A-3 enhancement habitat dropdown lists, and G-1's irreplaceable flag.

.EXAMPLE
    .\Extract-BngMetric.ps1 -WorkbookPath "C:\...\The_Statutory_Biodiversity_Metric_Calculation_Tool_-_Macro_enabled_tool_23.07.2024.xlsm" -MetricVersion "2024-07-23"
#>
param(
    [Parameter(Mandatory = $true)] [string] $WorkbookPath,
    [Parameter(Mandatory = $true)] [string] $MetricVersion,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\..\src\WWP.LandscapeDataManager.Shared\Resources\BngMetric_$MetricVersion.json")
)

$ErrorActionPreference = 'Stop'

function Get-Cell($value) {
    # Keep numbers as numbers and everything else (e.g. "30+", "Not Possible ▲") as trimmed text.
    if ($null -eq $value) { return $null }
    if ($value -is [double]) { return $value }
    $text = "$value".Trim()
    if ($text -eq '') { return $null }
    return $text
}

function Get-NamedList($workbook, [string] $name) {
    $values = $workbook.Names.Item($name).RefersToRange.Value2
    $list = New-Object System.Collections.Generic.List[string]
    if ($values -is [array]) {
        foreach ($v in $values) { $c = Get-Cell $v; if ($null -ne $c) { $list.Add("$c") } }
    } else {
        $c = Get-Cell $values; if ($null -ne $c) { $list.Add("$c") }
    }
    return ,$list.ToArray()
}

$resolvedPath = (Resolve-Path $WorkbookPath).Path
$excel = $null
$openedHere = $false
try { $excel = [Runtime.InteropServices.Marshal]::GetActiveObject('Excel.Application') } catch { }
$workbook = $null
if ($excel) {
    foreach ($wb in $excel.Workbooks) {
        if ($wb.Name -eq [IO.Path]::GetFileName($resolvedPath)) { $workbook = $wb; break }
    }
}
if (-not $workbook) {
    if (-not $excel) { $excel = New-Object -ComObject Excel.Application; $excel.Visible = $false }
    $excel.AutomationSecurity = 3   # msoAutomationSecurityForceDisable: never run the workbook's macros
    $workbook = $excel.Workbooks.Open($resolvedPath, 0, $true)
    $openedHere = $true
}

try {
    $g1 = $workbook.Worksheets.Item('G-1 All Habitats')
    $g3 = $workbook.Worksheets.Item('G-3 Multipliers')
    $g4 = $workbook.Worksheets.Item('G-4 Temporal multipliers')
    $g8 = $workbook.Worksheets.Item('G-8 Condition Look up')

    # --- Condition headers (identical in G-4 F3:M3 and G-8 B3:H3) ---
    $conditionHeaders = @()
    for ($c = 2; $c -le 8; $c++) { $conditionHeaders += "$($g8.Cells.Item(3, $c).Value2)".Trim() }
    for ($c = 7; $c -le 13; $c++) {
        $h = "$($g4.Cells.Item(3, $c).Value2)".Trim()
        if ($h -ne $conditionHeaders[$c - 7]) { throw "G-4 condition header '$h' does not match G-8 '$($conditionHeaders[$c - 7])'." }
    }

    # --- Lookups keyed by habitat description ---
    $difficulty = @{}
    $g3Values = $g3.Range('A3:E134').Value2
    for ($r = 1; $r -le $g3Values.GetLength(0); $r++) {
        $d = Get-Cell $g3Values[$r, 1]
        if ($d) { $difficulty["$d"] = @{ Creation = Get-Cell $g3Values[$r, 2]; Enhancement = Get-Cell $g3Values[$r, 4] } }
    }

    $temporal = @{}
    $g4Values = $g4.Range('F4:M135').Value2
    for ($r = 1; $r -le $g4Values.GetLength(0); $r++) {
        $d = Get-Cell $g4Values[$r, 1]
        if (-not $d) { continue }
        $row = [ordered]@{}
        for ($c = 0; $c -lt 7; $c++) { $row[$conditionHeaders[$c]] = Get-Cell $g4Values[$r, ($c + 2)] }
        $temporal["$d"] = $row
    }

    $conditionScores = @{}
    $conditionGroupOf = @{}
    $g8Values = $g8.Range('A4:I135').Value2
    for ($r = 1; $r -le $g8Values.GetLength(0); $r++) {
        $d = Get-Cell $g8Values[$r, 1]
        if (-not $d) { continue }
        $row = [ordered]@{}
        for ($c = 0; $c -lt 7; $c++) { $row[$conditionHeaders[$c]] = Get-Cell $g8Values[$r, ($c + 2)] }
        $conditionScores["$d"] = $row
        $conditionGroupOf["$d"] = Get-Cell $g8Values[$r, 9]
    }

    # --- Broad habitats and their proposed-habitat dropdown lists (A-2 column D / E) ---
    $broadMap = $g1.Range('AF27:AG41').Value2
    $broadHabitats = @()
    $broadOf = @{}
    for ($r = 1; $r -le $broadMap.GetLength(0); $r++) {
        $broad = Get-Cell $broadMap[$r, 1]
        $listName = Get-Cell $broadMap[$r, 2]
        if (-not $broad) { continue }
        $proposed = Get-NamedList $workbook "$listName"
        foreach ($p in $proposed) { $broadOf[$p] = "$broad" }
        $broadHabitats += [ordered]@{ name = "$broad"; proposedHabitats = $proposed }
    }

    # --- Baseline broad habitats (A-1 column E / F: INDEX(G-1 AG3:AG17, MATCH(broad, AF3:AF17))) ---
    $baselineMap = $g1.Range('AF3:AG17').Value2
    $baselineBroadHabitats = @()
    $baselineBroadOf = @{}
    for ($r = 1; $r -le $baselineMap.GetLength(0); $r++) {
        $broad = Get-Cell $baselineMap[$r, 1]
        $listName = Get-Cell $baselineMap[$r, 2]
        if (-not $broad) { continue }
        $listed = Get-NamedList $workbook "$listName"
        foreach ($p in $listed) { $baselineBroadOf[$p] = "$broad" }
        $baselineBroadHabitats += [ordered]@{ name = "$broad"; habitats = $listed }
    }

    # --- Enhancement broad habitats (A-3 column R: INDIRECT(INDEX(G-1 AG49:AG63, MATCH(broad, AF27:AF41)))) ---
    $enhanceNames = $g1.Range('AF27:AF41').Value2
    $enhanceLists = $g1.Range('AG49:AG63').Value2
    $enhancementBroadHabitats = @()
    for ($r = 1; $r -le $enhanceNames.GetLength(0); $r++) {
        $broad = Get-Cell $enhanceNames[$r, 1]
        $listName = Get-Cell $enhanceLists[$r, 1]
        if (-not $broad -or -not $listName) { continue }
        $enhancementBroadHabitats += [ordered]@{ name = "$broad"; habitats = (Get-NamedList $workbook "$listName") }
    }

    # --- G-5 Enhancement Temporal: years to target per habitat x "baseline condition - proposed condition" ---
    $g5 = $workbook.Worksheets.Item('G-5 Enhancement Temporal')
    $g5Values = $g5.Range('C4:X136').Value2
    $enhanceKeys = @()
    for ($c = 2; $c -le $g5Values.GetLength(1); $c++) { $enhanceKeys += "$(Get-Cell $g5Values[1, $c])" }
    $enhanceTemporal = @{}
    for ($r = 2; $r -le $g5Values.GetLength(0); $r++) {
        $d = Get-Cell $g5Values[$r, 1]
        if (-not $d) { continue }
        $row = [ordered]@{}
        for ($c = 2; $c -le $g5Values.GetLength(1); $c++) { $row[$enhanceKeys[$c - 2]] = Get-Cell $g5Values[$r, $c] }
        $enhanceTemporal["$d"] = $row
    }

    # --- Habitats (G-1) ---
    $habitats = @()
    $g1Values = $g1.Range('A3:T135').Value2
    $problems = @()
    for ($r = 1; $r -le $g1Values.GetLength(0); $r++) {
        $name = Get-Cell $g1Values[$r, 1]
        if (-not $name) { continue }
        $description = "$(Get-Cell $g1Values[$r, 2])"
        if (-not $difficulty.ContainsKey($description)) { $problems += "No G-3 difficulty row for '$description'." }
        if (-not $temporal.ContainsKey($description)) { $problems += "No G-4 temporal row for '$description'." }
        if (-not $conditionScores.ContainsKey($description)) { $problems += "No G-8 condition row for '$description'." }
        $habitats += [ordered]@{
            name                  = "$name"
            description           = $description
            broadHabitat          = $broadOf["$name"]
            baselineBroadHabitat  = $baselineBroadOf["$name"]
            irreplaceable         = Get-Cell $g1Values[$r, 20]
            distinctiveness       = Get-Cell $g1Values[$r, 11]
            distinctivenessScore  = Get-Cell $g1Values[$r, 12]
            tradingRule           = Get-Cell $g1Values[$r, 13]
            conditionGroup        = $conditionGroupOf[$description]
            creationDifficulty    = $difficulty[$description].Creation
            enhancementDifficulty = $difficulty[$description].Enhancement
            conditionScores       = $conditionScores[$description]
            timeToTargetYears     = $temporal[$description]
            enhancementTimeToTargetYears = $enhanceTemporal[$description]
        }
    }
    foreach ($p in $broadOf.Keys) {
        if (-not ($habitats | Where-Object { $_.name -eq $p })) { $problems += "Proposed habitat '$p' is not in G-1." }
    }
    if ($problems) { throw ("Metric tables are inconsistent:`n" + ($problems -join "`n")) }

    # --- Condition groups (A-2 column J dropdown = INDIRECT(condition group)) ---
    $conditionGroups = [ordered]@{}
    foreach ($group in ($conditionGroupOf.Values | Where-Object { $_ } | Sort-Object -Unique)) {
        $conditionGroups["$group"] = Get-NamedList $workbook "$group"
    }

    # --- Small tables ---
    $distinctivenessScores = [ordered]@{}
    $dv = $g1.Range('V3:W7').Value2
    for ($r = 1; $r -le 5; $r++) { $distinctivenessScores["$(Get-Cell $dv[$r, 1])"] = Get-Cell $dv[$r, 2] }

    $strategic = @()
    $ss = $g3.Range('L4:N6').Value2
    for ($r = 1; $r -le 3; $r++) {
        $strategic += [ordered]@{ description = Get-Cell $ss[$r, 1]; category = Get-Cell $ss[$r, 2]; multiplier = Get-Cell $ss[$r, 3] }
    }

    $difficultyMultipliers = [ordered]@{}
    $dm = $g3.Range('P3:Q6').Value2
    for ($r = 1; $r -le 4; $r++) { $difficultyMultipliers["$(Get-Cell $dm[$r, 1])"] = Get-Cell $dm[$r, 2] }

    $temporalMultipliers = @()
    $tm = $g4.Range('A4:C37').Value2
    for ($r = 1; $r -le $tm.GetLength(0); $r++) {
        $years = Get-Cell $tm[$r, 1]
        if ($null -eq $years) { continue }
        $temporalMultipliers += [ordered]@{ years = "$years"; multiplier = Get-Cell $tm[$r, 3] }
    }

    $result = [ordered]@{
        metricName            = 'The Statutory Biodiversity Metric'
        metricVersion         = $MetricVersion
        sourceFile            = [IO.Path]::GetFileName($resolvedPath)
        conditions            = $conditionHeaders
        distinctivenessScores = $distinctivenessScores
        strategicSignificance = $strategic
        difficultyMultipliers = $difficultyMultipliers
        temporalMultipliers   = $temporalMultipliers
        conditionGroups       = $conditionGroups
        broadHabitats         = $broadHabitats
        baselineBroadHabitats = $baselineBroadHabitats
        enhancementBroadHabitats = $enhancementBroadHabitats
        habitats              = $habitats
    }

    $outDir = Split-Path $OutputPath -Parent
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
    $json = $result | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, (New-Object System.Text.UTF8Encoding $false))
    Write-Output "Wrote $($habitats.Count) habitats, $($broadHabitats.Count) broad habitats, $($conditionGroups.Count) condition groups to $OutputPath"
}
finally {
    if ($openedHere) { $workbook.Close($false); if ($excel.Workbooks.Count -eq 0) { $excel.Quit() } }
}
