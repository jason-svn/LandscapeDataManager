<#
.SYNOPSIS
    Generates the golden test cases that pin BngHabitatCreationCalculator to the official metric.

.DESCRIPTION
    Copies the metric workbook to a temp folder, opens the copy in a separate hidden Excel
    instance with macros disabled (the original is never touched), fills sheet A-2 with every
    creatable habitat x valid condition x a spread of year offsets, lets Excel calculate, and
    writes the inputs plus Excel's own results for columns H-Y to a TSV the unit tests replay.

.EXAMPLE
    .\Export-BngGoldenCases.ps1 -WorkbookPath "C:\...\The_Statutory_Biodiversity_Metric_Calculation_Tool_-_Macro_enabled_tool_23.07.2024.xlsm" -MetricJsonPath ..\..\src\WWP.LandscapeDataManager.Shared\Resources\BngMetric_2024-07-23.json
#>
param(
    [Parameter(Mandatory = $true)] [string] $WorkbookPath,
    [Parameter(Mandatory = $true)] [string] $MetricJsonPath,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\..\tests\WWP.LandscapeDataManager.Tests\TestData\BngA2GoldenCases.tsv"),
    [int[]] $YearOffsets = @(0, 3, 12, 35, -6, -35),
    [double] $AreaHectares = 1.2345
)

$ErrorActionPreference = 'Stop'
$FirstRow = 11
$BatchSize = 200

$metric = Get-Content $MetricJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$groups = @{}
foreach ($p in $metric.conditionGroups.PSObject.Properties) { $groups[$p.Name] = @($p.Value) }
$strategic = @($metric.strategicSignificance | ForEach-Object { $_.description })

# Build the case list: every creatable habitat x each of its dropdown conditions x each offset,
# rotating strategic significance so all three options are covered.
$cases = New-Object System.Collections.Generic.List[object]
$i = 0
foreach ($h in $metric.habitats) {
    if (-not $h.broadHabitat) { continue }
    foreach ($condition in $groups[$h.conditionGroup]) {
        foreach ($offset in $YearOffsets) {
            $cases.Add([pscustomobject]@{
                Broad = $h.broadHabitat; Habitat = $h.name; Condition = $condition
                Strategic = $strategic[$i % $strategic.Count]; Offset = $offset
            })
            $i++
        }
    }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ("bng-golden-" + [guid]::NewGuid().ToString('N') + '.xlsm')
Copy-Item -LiteralPath $WorkbookPath -Destination $temp
# A copy of a OneDrive/SharePoint download keeps its Mark of the Web, which makes Excel open it
# in Protected View — where Workbooks.Open silently returns nothing.
Unblock-File -LiteralPath $temp

function Format-Value($v) {
    if ($null -eq $v) { return '' }
    if ($v -is [double]) { return $v.ToString('R', [Globalization.CultureInfo]::InvariantCulture) }
    if ($v -is [int]) { return "$v" }   # Excel error values (e.g. #VALUE!) come back as Int32 codes
    return ("$v".Trim() -replace "`t", ' ' -replace "`r?`n", ' ')
}

# Excel rejects COM calls (RPC_E_CALL_REJECTED) while it is busy, e.g. mid-recalculation; retry briefly.
function Invoke-Excel([scriptblock] $Action) {
    for ($attempt = 1; ; $attempt++) {
        try { return & $Action }
        catch [System.Runtime.InteropServices.COMException] {
            if ($_.Exception.HResult -ne -2147418111 -or $attempt -ge 100) { throw }
            Start-Sleep -Milliseconds 200
        }
    }
}

$excel = New-Object -ComObject Excel.Application
try {
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.AutomationSecurity = 3   # never run the workbook's macros
    $excel.AskToUpdateLinks = $false
    $workbook = $excel.Workbooks.Open($temp)
    if (-not $workbook) { throw "Excel did not open the workbook copy at $temp." }
    $sheet = $workbook.Worksheets.Item('A-2 On-Site Habitat Creation')
    # The sheet stays protected: only its unlocked input columns are written.
    Invoke-Excel { $excel.ScreenUpdating = $false; $excel.EnableEvents = $false; $excel.Calculation = -4135 } | Out-Null   # xlCalculationManual

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add((@('Habitat', 'Condition', 'StrategicSignificance', 'YearOffset', 'AreaHectares',
        'H_Distinctiveness', 'I_Score', 'K_ConditionScore', 'M_StrategicCategory', 'N_StrategicMultiplier',
        'O_StandardTime', 'R_TimeStatus', 'S_FinalTime', 'T_TimeMultiplier', 'U_StandardDifficulty',
        'V_AppliedDifficulty', 'W_FinalDifficulty', 'X_DifficultyMultiplier', 'Y_HabitatUnits') -join "`t"))

    for ($start = 0; $start -lt $cases.Count; $start += $BatchSize) {
        $batch = $cases.GetRange($start, [Math]::Min($BatchSize, $cases.Count - $start))
        $last = $FirstRow + $batch.Count - 1
        # Clear only the input columns — the rest of D:Q are the sheet's (locked) formulas.
        $end = $FirstRow + $BatchSize - 1
        foreach ($inputColumns in "D${FirstRow}:E$end", "G${FirstRow}:G$end", "J${FirstRow}:J$end", "L${FirstRow}:L$end", "P${FirstRow}:Q$end") {
            Invoke-Excel { $sheet.Range($inputColumns).ClearContents() } | Out-Null
        }
        $d = New-Object 'object[,]' $batch.Count, 2
        $j = New-Object 'object[,]' $batch.Count, 1
        $l = New-Object 'object[,]' $batch.Count, 1
        $pq = New-Object 'object[,]' $batch.Count, 2
        $g = New-Object 'object[,]' $batch.Count, 1
        for ($r = 0; $r -lt $batch.Count; $r++) {
            $c = $batch[$r]
            $d[$r, 0] = $c.Broad; $d[$r, 1] = $c.Habitat
            $g[$r, 0] = $AreaHectares
            $j[$r, 0] = $c.Condition
            $l[$r, 0] = $c.Strategic
            $pq[$r, 0] = if ($c.Offset -gt 30) { '30+' } elseif ($c.Offset -gt 0) { [double]$c.Offset } else { $null }
            $pq[$r, 1] = if ($c.Offset -lt -30) { '30+' } elseif ($c.Offset -lt 0) { [double](-$c.Offset) } else { $null }
        }
        Invoke-Excel { $sheet.Range("D${FirstRow}:E$last").Value2 = $d } | Out-Null
        Invoke-Excel { $sheet.Range("G${FirstRow}:G$last").Value2 = $g } | Out-Null
        Invoke-Excel { $sheet.Range("J${FirstRow}:J$last").Value2 = $j } | Out-Null
        Invoke-Excel { $sheet.Range("L${FirstRow}:L$last").Value2 = $l } | Out-Null
        Invoke-Excel { $sheet.Range("P${FirstRow}:Q$last").Value2 = $pq } | Out-Null
        Invoke-Excel { $excel.Calculate() } | Out-Null
        # Assigned inside the block: returning a 2-D array through the pipeline would flatten it.
        Invoke-Excel { $script:out = $sheet.Range("H${FirstRow}:Y$last").Value2 } | Out-Null
        for ($r = 0; $r -lt $batch.Count; $r++) {
            $c = $batch[$r]
            $row = $r + 1
            $cols = @($c.Habitat, $c.Condition, $c.Strategic, $c.Offset, $AreaHectares.ToString([Globalization.CultureInfo]::InvariantCulture))
            # H..Y = columns 1..18 of the read range; skip J (3), L (5), P (9), Q (10) which are inputs.
            foreach ($col in 1, 2, 4, 6, 7, 8, 11, 12, 13, 14, 15, 16, 17, 18) { $cols += Format-Value $out[$row, $col] }
            $lines.Add(($cols -join "`t"))
        }
        Write-Progress -Activity 'Calculating golden cases' -PercentComplete (100 * ($start + $batch.Count) / $cases.Count)
    }

    $workbook.Close($false)
    $outDir = Split-Path $OutputPath -Parent
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
    [IO.File]::WriteAllLines([IO.Path]::GetFullPath($OutputPath), $lines, (New-Object System.Text.UTF8Encoding $false))
    Write-Output "Wrote $($cases.Count) golden cases to $OutputPath"
}
finally {
    $excel.Quit()
    [Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
    Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
}
