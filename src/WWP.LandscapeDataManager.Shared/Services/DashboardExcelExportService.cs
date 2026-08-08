using ClosedXML.Excel;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Everything the dashboard had on screen at export time — the workbook mirrors exactly this, not the unfiltered model, so the export matches what the user was looking at.</summary>
public sealed record DashboardExcelExportRequest(
    string FilePath,
    string DocumentTitle,
    string PreferredUnitSystem,
    string PreferredCurrency,
    string BenefitPeriodLabel,
    DashboardGrandTotal GrandTotal,
    IReadOnlyList<SpeciesSubtotal> SpeciesSubtotals,
    IReadOnlyList<FloorTypeSubtotal> FloorSubtotals,
    IReadOnlyList<NormalizedTreeMetrics> Trees,
    IReadOnlyList<DashboardFloorItem> Floors);

public sealed record DashboardExcelExportResult(string FilePath);

/// <summary>
/// Writes a fresh Dashboard workbook — unlike <see cref="ITreeExcelMergeService"/>, there is no
/// existing file to merge into, since this is a point-in-time report rather than a synced data
/// source. Reuses the same ClosedXML header/column formatting and atomic-save helpers.
/// </summary>
public static class DashboardExcelExportService
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#006B7C");

    public static Task<DashboardExcelExportResult> ExportAsync(
        DashboardExcelExportRequest request,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(request, cancellationToken), cancellationToken);

    private static DashboardExcelExportResult Export(DashboardExcelExportRequest request, CancellationToken cancellationToken)
    {
        var fullPath = ValidatePath(request.FilePath);

        using var workbook = new XLWorkbook();
        WriteSummarySheet(workbook, request);
        cancellationToken.ThrowIfCancellationRequested();
        WriteSpeciesSheet(workbook, request);
        cancellationToken.ThrowIfCancellationRequested();
        WriteFloorSheet(workbook, request);
        cancellationToken.ThrowIfCancellationRequested();
        WriteInstancesSheet(workbook, request);

        SaveSafely(workbook, fullPath);
        return new DashboardExcelExportResult(fullPath);
    }

    private static void WriteSummarySheet(XLWorkbook workbook, DashboardExcelExportRequest request)
    {
        var sheet = workbook.AddWorksheet("Summary");
        var total = request.GrandTotal;
        var massUnit = string.Equals(request.PreferredUnitSystem, "Metric", StringComparison.OrdinalIgnoreCase) ? "kg" : "lb / oz";

        var row = 1;
        sheet.Cell(row, 1).Value = request.DocumentTitle;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row += 1;
        sheet.Cell(row, 1).Value = $"Landscape benefits dashboard — {request.BenefitPeriodLabel} — {request.PreferredUnitSystem} / {request.PreferredCurrency}";
        row += 2;

        void WriteRow(string label, string value)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = value;
            row += 1;
        }

        WriteRow("Trees counted in totals", total.TreeCount.ToString("N0"));
        WriteRow("Trees excluded (not yet Calculated)", total.TreesExcludedFromTotals.ToString("N0"));
        WriteRow($"CO2 sequestered ({massUnit})", total.CO2SequesteredAnnual.ToString("N1"));
        WriteRow($"Total pollution mass removed ({massUnit})", total.TotalPollutionMassRemovedAnnual.ToString("N2"));
        WriteRow($"  PM2.5 removed ({massUnit})", total.PM25RemovedAnnual.ToString("N2"));
        WriteRow($"  NO2 removed ({massUnit})", total.NO2RemovedAnnual.ToString("N2"));
        WriteRow($"  O3 removed ({massUnit})", total.O3RemovedAnnual.ToString("N2"));
        WriteRow($"  SO2 removed ({massUnit})", total.SO2RemovedAnnual.ToString("N2"));
        WriteRow($"  CO removed ({massUnit})", total.CORemovedAnnual.ToString("N2"));
        WriteRow($"Tree monetary benefit ({request.PreferredCurrency})", total.TreeCostSavedAnnual.ToString("N2"));
        row += 1;
        WriteRow("Planting areas — count", total.FloorCount.ToString("N0"));
        WriteRow("Planting areas — area (m2)", total.FloorAreaSquareMeters.ToString("N1"));
        WriteRow("Planting areas — CO2 sequestered (kg)", total.FloorCO2SequesteredAnnual.ToString("N1"));
        WriteRow("Planting areas — runoff avoided (m3)", total.FloorRunoffAvoidedAnnual.ToString("N1"));
        WriteRow("Planting areas — pollution mitigated (kg)", total.FloorPollutionMassRemovedAnnual.ToString("N2"));
        WriteRow("Planting areas — total GWP", total.FloorTotalGwp.ToString("N1"));
        WriteRow("Planting areas — cost saved (as calculated)", total.FloorCostSavedAnnual.ToString("N2"));

        sheet.Column(1).Width = 42;
        sheet.Column(2).Width = 22;
    }

    private static void WriteSpeciesSheet(XLWorkbook workbook, DashboardExcelExportRequest request)
    {
        var sheet = workbook.AddWorksheet("By Species");
        string[] headers =
        [
            "Species code", "Common name", "Tree count",
            "CO2 sequestered", "PM2.5 removed", "NO2 removed", "O3 removed", "SO2 removed", "CO removed",
            "Cost saved"
        ];
        WriteHeaderRow(sheet, headers);

        var row = 2;
        foreach (var subtotal in request.SpeciesSubtotals)
        {
            var isAnnual = string.Equals(request.BenefitPeriodLabel, "Annual", StringComparison.OrdinalIgnoreCase);
            sheet.Cell(row, 1).Value = subtotal.SpeciesCode;
            sheet.Cell(row, 2).Value = subtotal.CommonName;
            sheet.Cell(row, 3).Value = subtotal.TreeCount;
            sheet.Cell(row, 4).Value = isAnnual ? subtotal.CO2SequesteredAnnual : subtotal.CO2SequesteredLifetimeTotal;
            sheet.Cell(row, 5).Value = isAnnual ? subtotal.PM25RemovedAnnual : subtotal.PM25RemovedLifetimeTotal;
            sheet.Cell(row, 6).Value = isAnnual ? subtotal.NO2RemovedAnnual : subtotal.NO2RemovedLifetimeTotal;
            sheet.Cell(row, 7).Value = isAnnual ? subtotal.O3RemovedAnnual : subtotal.O3RemovedLifetimeTotal;
            sheet.Cell(row, 8).Value = isAnnual ? subtotal.SO2RemovedAnnual : subtotal.SO2RemovedLifetimeTotal;
            sheet.Cell(row, 9).Value = isAnnual ? subtotal.CORemovedAnnual : subtotal.CORemovedLifetimeTotal;
            sheet.Cell(row, 10).Value = isAnnual ? subtotal.CostSavedAnnual : subtotal.CostSavedLifetimeTotal;
            row += 1;
        }

        FormatNewWorksheet(sheet, headers.Length, row - 1);
    }

    private static void WriteFloorSheet(XLWorkbook workbook, DashboardExcelExportRequest request)
    {
        var sheet = workbook.AddWorksheet("Planting Areas");
        string[] headers =
        [
            "Landscape data sheet type", "Planting area count", "Area (m2)", "CO2 sequestered (kg)",
            "Runoff avoided (m3)", "Pollution mitigated (kg)", "Total GWP", "Cost saved (as calculated)"
        ];
        WriteHeaderRow(sheet, headers);

        var row = 2;
        foreach (var subtotal in request.FloorSubtotals)
        {
            sheet.Cell(row, 1).Value = subtotal.LdsType;
            sheet.Cell(row, 2).Value = subtotal.FloorCount;
            sheet.Cell(row, 3).Value = subtotal.AreaSquareMeters;
            sheet.Cell(row, 4).Value = subtotal.CO2SequesteredAnnual;
            sheet.Cell(row, 5).Value = subtotal.RunoffAvoidedAnnual;
            sheet.Cell(row, 6).Value = subtotal.PollutionMassRemovedAnnual;
            sheet.Cell(row, 7).Value = subtotal.TotalGwp;
            sheet.Cell(row, 8).Value = subtotal.CostSavedAnnual;
            row += 1;
        }

        FormatNewWorksheet(sheet, headers.Length, row - 1);
    }

    private static void WriteInstancesSheet(XLWorkbook workbook, DashboardExcelExportRequest request)
    {
        var sheet = workbook.AddWorksheet("All Instances");
        string[] headers =
        [
            "Category", "Family : Type", "Species code", "Level", "Design option", "Status",
            "CO2 sequestered", "PM2.5 removed", "NO2 removed", "O3 removed", "SO2 removed", "CO removed",
            "Cost saved"
        ];
        WriteHeaderRow(sheet, headers);

        var isAnnual = string.Equals(request.BenefitPeriodLabel, "Annual", StringComparison.OrdinalIgnoreCase);
        var row = 2;
        foreach (var tree in request.Trees)
        {
            var item = tree.Source;
            sheet.Cell(row, 1).Value = "Tree";
            sheet.Cell(row, 2).Value = $"{item.FamilyName} : {item.TypeName}";
            sheet.Cell(row, 3).Value = item.SpeciesCode ?? string.Empty;
            sheet.Cell(row, 4).Value = item.LevelName ?? string.Empty;
            sheet.Cell(row, 5).Value = FormatDesignOption(item.DesignOption);
            sheet.Cell(row, 6).Value = item.Status;
            sheet.Cell(row, 7).Value = isAnnual ? tree.CO2SequesteredAnnual : tree.CO2SequesteredLifetimeTotal;
            sheet.Cell(row, 8).Value = isAnnual ? tree.PM25RemovedAnnual : tree.PM25RemovedLifetimeTotal;
            sheet.Cell(row, 9).Value = isAnnual ? tree.NO2RemovedAnnual : tree.NO2RemovedLifetimeTotal;
            sheet.Cell(row, 10).Value = isAnnual ? tree.O3RemovedAnnual : tree.O3RemovedLifetimeTotal;
            sheet.Cell(row, 11).Value = isAnnual ? tree.SO2RemovedAnnual : tree.SO2RemovedLifetimeTotal;
            sheet.Cell(row, 12).Value = isAnnual ? tree.CORemovedAnnual : tree.CORemovedLifetimeTotal;
            sheet.Cell(row, 13).Value = isAnnual ? tree.CostSavedAnnual : tree.CostSavedLifetimeTotal;
            row += 1;
        }

        foreach (var floor in request.Floors)
        {
            sheet.Cell(row, 1).Value = "Planting Area";
            sheet.Cell(row, 2).Value = $"{floor.FamilyName} : {floor.TypeName}";
            sheet.Cell(row, 3).Value = floor.LdsType ?? string.Empty;
            sheet.Cell(row, 4).Value = floor.LevelName ?? string.Empty;
            sheet.Cell(row, 5).Value = FormatDesignOption(floor.DesignOption);
            sheet.Cell(row, 6).Value = "n/a";
            sheet.Cell(row, 7).Value = floor.CO2SequesteredAnnual;
            sheet.Cell(row, 8).Value = "n/a";
            sheet.Cell(row, 9).Value = "n/a";
            sheet.Cell(row, 10).Value = "n/a";
            sheet.Cell(row, 11).Value = "n/a";
            sheet.Cell(row, 12).Value = "n/a";
            sheet.Cell(row, 13).Value = floor.CostSavedAnnual;
            row += 1;
        }

        FormatNewWorksheet(sheet, headers.Length, row - 1);
    }

    private static string FormatDesignOption(DesignOptionInfo designOption) =>
        designOption.IsPrimary
            ? "Primary"
            : $"{designOption.SetName} : {designOption.OptionName}";

    private static void WriteHeaderRow(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var column = 1; column <= headers.Count; column++)
        {
            sheet.Cell(1, column).Value = headers[column - 1];
        }
    }

    private static void FormatNewWorksheet(IXLWorksheet worksheet, int columnCount, int lastRow)
    {
        var header = worksheet.Range(1, 1, 1, Math.Max(1, columnCount));
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = HeaderFill;
        worksheet.SheetView.FreezeRows(1);
        if (lastRow >= 1)
        {
            worksheet.Range(1, 1, lastRow, Math.Max(1, columnCount)).SetAutoFilter();
        }

        for (var column = 1; column <= Math.Max(1, columnCount); column++)
        {
            worksheet.Column(column).Width = Math.Min(45, Math.Max(14, worksheet.Column(column).Width));
        }
    }

    private static string ValidatePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("Choose an Excel output path.");
        }

        var fullPath = Path.GetFullPath(filePath.Trim());
        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Choose an .xlsx workbook.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    private static void SaveSafely(XLWorkbook workbook, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(filePath)}.{Guid.NewGuid():N}.tmp{Path.GetExtension(filePath)}");
        try
        {
            workbook.SaveAs(temporaryPath);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
