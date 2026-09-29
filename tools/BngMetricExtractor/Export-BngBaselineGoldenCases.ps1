<#
.SYNOPSIS
    Generates the golden test cases that pin BngHabitatBaselineCalculator (sheet A-1) and
    BngHabitatEnhancementCalculator (sheet A-3) to the official metric.

.DESCRIPTION
    Same approach as Export-BngGoldenCases.ps1: a macro-disabled copy of the workbook in a hidden
    Excel instance. A-1 cases cover every baseline habitat x each of its conditions x
    retained/enhanced/lost (and both irreplaceable answers where G-1 allows either). A-3 cases fill
    A-1 with enhanced rows — so A-3's baseline references map row-for-row — and pair each baseline
    habitat and condition with the same habitat at each of its conditions plus one other habitat,
    rotating strategic significance and year offsets.

.EXAMPLE
    .\Export-BngBaselineGoldenCases.ps1 -WorkbookPath "C:\...\The_Statutory_Biodiversity_Metric_Calculation_Tool_-_Macro_enabled_tool_23.07.2024.xlsm" -MetricJsonPath ..\..\src\WWP.LandscapeDataManager.Shared\Resources\BngMetric_2024-07-23.json
#>
param(
    [Parameter(Mandatory = $true)] [string] $WorkbookPath,
    [Parameter(Mandatory = $true)] [string] $MetricJsonPath,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot "..\..\tests\WWP.LandscapeDataManager.Tests\TestData"),
    [int[]] $YearOffsets = @(0, 3, 12, 35, -6, -35),
    [double] $AreaHectares = 0.8765
)

$ErrorActionPreference = 'Stop'
$A1FirstRow = 11
$A3FirstRow = 12
$BatchSize = 240   # A-1 has 248 input rows (11..258)

$metric = Get-Content $MetricJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$groups = @{}
foreach ($p in $metric.conditionGroups.PSObject.Properties) { $groups[$p.Name] = @($p.Value) }
$strategic = @($metric.strategicSignificance | ForEach-Object { $_.description })
$baselineHabitats = @($metric.habitats | Where-Object { $_.baselineBroadHabitat })
$enhancementNames = @{}
foreach ($b in $metric.enhancementBroadHabitats) { foreach ($h in @($b.habitats)) { $enhancementNames["$h"] = $true } }
$enhancementHabitats = @($metric.habitats | Where-Object { $enhancementNames.ContainsKey($_.name) })

function Get-IrreplaceableOptions($h) { if ($h.irreplaceable -eq 'Yes/No') { @('No', 'Yes') } else { @("$($h.irreplaceable)") } }

# --- A-1 cases ---
$a1Cases = New-Object System.Collections.Generic.List[object]
$i = 0
foreach ($h in $baselineHabitats) {
    foreach ($condition in $groups[$h.conditionGroup]) {
        foreach ($irreplaceable in (Get-IrreplaceableOptions $h)) {
            foreach ($fate in 'Retained', 'Enhanced', 'Lost') {
                $a1Cases.Add([pscustomobject]@{
                    Broad = $h.baselineBroadHabitat; Habitat = $h.name; Irreplaceable = $irreplaceable
                    Condition = $condition; Strategic = $strategic[$i % $strategic.Count]; Fate = $fate
                })
                $i++
            }
        }
    }
}

# --- A-3 cases ---
$a3Cases = New-Object System.Collections.Generic.List[object]
$i = 0
foreach ($h in $baselineHabitats) {
    $irreplaceable = if ($h.irreplaceable -eq 'Yes') { 'Yes' } else { 'No' }
    foreach ($baseCondition in $groups[$h.conditionGroup]) {
        $targets = New-Object System.Collections.Generic.List[object]
        if ($enhancementNames.ContainsKey($h.name)) {
            foreach ($c in $groups[$h.conditionGroup]) { $targets.Add(@($h, $c)) }
        }
        $other = $enhancementHabitats[($i * 7 + 3) % $enhancementHabitats.Count]
        $otherConditions = $groups[$other.conditionGroup]
        $targets.Add(@($other, $otherConditions[$i % $otherConditions.Count]))
        foreach ($t in $targets) {
            $a3Cases.Add([pscustomobject]@{
                Broad = $h.baselineBroadHabitat; Habitat = $h.name; Irreplaceable = $irreplaceable; BaselineCondition = $baseCondition
                Proposed = $t[0].name; ProposedCondition = $t[1]
                Strategic = $strategic[$i % $strategic.Count]; Offset = $YearOffsets[$i % $YearOffsets.Count]
            })
            $i++
        }
    }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ("bng-golden-" + [guid]::NewGuid().ToString('N') + '.xlsm')
Copy-Item -LiteralPath $WorkbookPath -Destination $temp
Unblock-File -LiteralPath $temp

function Format-Value($v) {
    if ($null -eq $v) { return '' }
    if ($v -is [double]) { return $v.ToString('R', [Globalization.CultureInfo]::InvariantCulture) }
    if ($v -is [int]) { return "$v" }
    return ("$v".Trim() -replace "`t", ' ' -replace "`r?`n", ' ')
}

function Invoke-Excel([scriptblock] $Action) {
    for ($attempt = 1; ; $attempt++) {
        try { return & $Action }
        catch [System.Runtime.InteropServices.COMException] {
            if ($_.Exception.HResult -ne -2147418111 -or $attempt -ge 100) { throw }
            Start-Sleep -Milliseconds 200
        }
    }
}

function New-Column($count) { New-Object 'object[,]' $count, 1 }
function To-YearsCell([int] $years) { if ($years -gt 30) { '30+' } elseif ($years -gt 0) { [double]$years } else { $null } }

$excel = New-Object -ComObject Excel.Application
try {
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.AutomationSecurity = 3
    $excel.AskToUpdateLinks = $false
    $workbook = $excel.Workbooks.Open($temp)
    if (-not $workbook) { throw "Excel did not open the workbook copy at $temp." }
    $a1 = $workbook.Worksheets.Item('A-1 On-Site Habitat Baseline')
    $a3 = $workbook.Worksheets.Item('A-3 On-Site Habitat Enhancement')
    Invoke-Excel { $excel.ScreenUpdating = $false; $excel.EnableEvents = $false; $excel.Calculation = -4135 } | Out-Null

    $a1End = $A1FirstRow + $BatchSize - 1
    $a3End = $A3FirstRow + $BatchSize - 1
    function Clear-Inputs {
        foreach ($range in "E${A1FirstRow}:H$a1End", "K${A1FirstRow}:K$a1End", "M${A1FirstRow}:M$a1End", "S${A1FirstRow}:T$a1End", "Y${A1FirstRow}:Y$a1End") {
            Invoke-Excel { $a1.Range($range).ClearContents() } | Out-Null
        }
        foreach ($range in "R${A3FirstRow}:R$a3End", "Y${A3FirstRow}:Y$a3End", "AA${A3FirstRow}:AA$a3End", "AE${A3FirstRow}:AF$a3End") {
            Invoke-Excel { $a3.Range($range).ClearContents() } | Out-Null
        }
    }

    function Write-Baseline($batch, [scriptblock] $fateOf) {
        $n = $batch.Count
        $last = $A1FirstRow + $n - 1
        $efgh = New-Object 'object[,]' $n, 4
        $k = New-Column $n; $m = New-Column $n
        $st = New-Object 'object[,]' $n, 2
        for ($r = 0; $r -lt $n; $r++) {
            $c = $batch[$r]
            $efgh[$r, 0] = $c.Broad; $efgh[$r, 1] = $c.Habitat; $efgh[$r, 2] = $c.Irreplaceable; $efgh[$r, 3] = $AreaHectares
            $k[$r, 0] = if ($c.BaselineCondition) { $c.BaselineCondition } else { $c.Condition }
            $m[$r, 0] = $c.Strategic
            $fate = & $fateOf $c
            $st[$r, 0] = if ($fate -eq 'Retained') { $AreaHectares } else { $null }
            $st[$r, 1] = if ($fate -eq 'Enhanced') { $AreaHectares } else { $null }
        }
        Invoke-Excel { $a1.Range("E${A1FirstRow}:H$last").Value2 = $efgh } | Out-Null
        Invoke-Excel { $a1.Range("K${A1FirstRow}:K$last").Value2 = $k } | Out-Null
        Invoke-Excel { $a1.Range("M${A1FirstRow}:M$last").Value2 = $m } | Out-Null
        Invoke-Excel { $a1.Range("S${A1FirstRow}:T$last").Value2 = $st } | Out-Null
    }

    # --- A-1 ---
    $a1Lines = New-Object System.Collections.Generic.List[string]
    $a1Lines.Add((@('Habitat', 'Irreplaceable', 'Condition', 'StrategicSignificance', 'Fate', 'AreaHectares',
        'I_Distinctiveness', 'J_Score', 'L_ConditionScore', 'N_StrategicCategory', 'O_StrategicMultiplier', 'P_TradingRule',
        'Q_TotalUnits', 'U_UnitsRetained', 'V_UnitsEnhanced', 'W_AreaLost', 'X_UnitsLost') -join "`t"))
    for ($start = 0; $start -lt $a1Cases.Count; $start += $BatchSize) {
        $batch = $a1Cases.GetRange($start, [Math]::Min($BatchSize, $a1Cases.Count - $start))
        Clear-Inputs
        Write-Baseline $batch { param($c) $c.Fate }
        Invoke-Excel { $excel.Calculate() } | Out-Null
        $last = $A1FirstRow + $batch.Count - 1
        Invoke-Excel { $script:out = $a1.Range("I${A1FirstRow}:X$last").Value2 } | Out-Null
        for ($r = 0; $r -lt $batch.Count; $r++) {
            $c = $batch[$r]; $row = $r + 1
            $cols = @($c.Habitat, $c.Irreplaceable, $c.Condition, $c.Strategic, $c.Fate, $AreaHectares.ToString([Globalization.CultureInfo]::InvariantCulture))
            # I..X = 1..16: I, J, L, N, O, P, Q, U, V, W, X
            foreach ($col in 1, 2, 4, 6, 7, 8, 9, 13, 14, 15, 16) { $cols += Format-Value $out[$row, $col] }
            $a1Lines.Add(($cols -join "`t"))
        }
        Write-Progress -Activity 'A-1 golden cases' -PercentComplete (100 * ($start + $batch.Count) / $a1Cases.Count)
    }

    # --- A-3 ---
    $a3Lines = New-Object System.Collections.Generic.List[string]
    $a3Lines.Add((@('BaselineHabitat', 'Irreplaceable', 'BaselineCondition', 'StrategicSignificance', 'ProposedHabitat', 'ProposedCondition', 'YearOffset', 'AreaHectares',
        'T_DistinctivenessChange', 'U_ConditionChange', 'W_Distinctiveness', 'X_Score', 'Z_ConditionScore', 'AB_StrategicCategory', 'AC_StrategicMultiplier',
        'AD_StandardTime', 'AG_TimeStatus', 'AH_FinalTime', 'AI_TimeMultiplier', 'AJ_StandardDifficulty', 'AK_AppliedDifficulty',
        'AL_FinalDifficulty', 'AM_DifficultyMultiplier', 'AN_HabitatUnits') -join "`t"))
    for ($start = 0; $start -lt $a3Cases.Count; $start += $BatchSize) {
        $batch = $a3Cases.GetRange($start, [Math]::Min($BatchSize, $a3Cases.Count - $start))
        Clear-Inputs
        Write-Baseline $batch { param($c) 'Enhanced' }
        $n = $batch.Count
        $last = $A3FirstRow + $n - 1
        $rr = New-Column $n; $y = New-Column $n; $aa = New-Column $n
        $aeaf = New-Object 'object[,]' $n, 2
        for ($r = 0; $r -lt $n; $r++) {
            $c = $batch[$r]
            $rr[$r, 0] = $c.Proposed; $y[$r, 0] = $c.ProposedCondition; $aa[$r, 0] = $c.Strategic
            $aeaf[$r, 0] = To-YearsCell ([Math]::Max($c.Offset, 0))
            $aeaf[$r, 1] = To-YearsCell ([Math]::Max(-$c.Offset, 0))
        }
        Invoke-Excel { $a3.Range("R${A3FirstRow}:R$last").Value2 = $rr } | Out-Null
        Invoke-Excel { $a3.Range("Y${A3FirstRow}:Y$last").Value2 = $y } | Out-Null
        Invoke-Excel { $a3.Range("AA${A3FirstRow}:AA$last").Value2 = $aa } | Out-Null
        Invoke-Excel { $a3.Range("AE${A3FirstRow}:AF$last").Value2 = $aeaf } | Out-Null
        Invoke-Excel { $excel.CalculateFull() } | Out-Null
        Invoke-Excel { $script:out = $a3.Range("E${A3FirstRow}:AN$last").Value2 } | Out-Null
        for ($r = 0; $r -lt $n; $r++) {
            $c = $batch[$r]; $row = $r + 1
            # Column E (1) is the baseline reference A-3 resolved — it must be this row's A-1 line.
            if ((Format-Value $out[$row, 1]) -ne "$($r + 1)") { throw "A-3 row $($A3FirstRow + $r) resolved baseline ref '$(Format-Value $out[$row, 1])', expected $($r + 1)." }
            $cols = @($c.Habitat, $c.Irreplaceable, $c.BaselineCondition, $c.Strategic, $c.Proposed, $c.ProposedCondition, $c.Offset,
                $AreaHectares.ToString([Globalization.CultureInfo]::InvariantCulture))
            # E..AN = 1..36: T=16, U=17, W=19, X=20, Z=22, AB=24, AC=25, AD=26, AG=29, AH=30, AI=31, AJ=32, AK=33, AL=34, AM=35, AN=36
            foreach ($col in 16, 17, 19, 20, 22, 24, 25, 26, 29, 30, 31, 32, 33, 34, 35, 36) { $cols += Format-Value $out[$row, $col] }
            $a3Lines.Add(($cols -join "`t"))
        }
        Write-Progress -Activity 'A-3 golden cases' -PercentComplete (100 * ($start + $n) / $a3Cases.Count)
    }

    $workbook.Close($false)
    if (-not (Test-Path $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory | Out-Null }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllLines([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'BngA1GoldenCases.tsv')), $a1Lines, $utf8)
    [IO.File]::WriteAllLines([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'BngA3GoldenCases.tsv')), $a3Lines, $utf8)
    Write-Output "Wrote $($a1Cases.Count) A-1 and $($a3Cases.Count) A-3 golden cases to $OutputDirectory"
}
finally {
    $excel.Quit()
    [Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
    Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
}
