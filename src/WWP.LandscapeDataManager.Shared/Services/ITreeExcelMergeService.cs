using System.Globalization;
using ClosedXML.Excel;

namespace WWP.LandscapeDataManager.App.Services;

internal sealed record ITreeExcelMergeOptions(
    string FilePath,
    string WorksheetName,
    int HeaderRow,
    bool AppendMissingSpecies,
    bool CreateBackup);

internal sealed record ITreeExcelMergeResult(
    string FilePath,
    string WorksheetName,
    int UpdatedRows,
    int AppendedRows,
    int PreservedUnmatchedRows,
    int AddedColumns,
    string? BackupPath);

internal sealed class ITreeExcelMergeService
{
    public Task<ITreeExcelMergeResult> MergeAsync(
        IReadOnlyList<ITreeExportRecord> records,
        ITreeExcelMergeOptions options,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Merge(records, options, cancellationToken), cancellationToken);

    private static ITreeExcelMergeResult Merge(
        IReadOnlyList<ITreeExportRecord> records,
        ITreeExcelMergeOptions options,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            throw new InvalidOperationException("There is no i-Tree data to write.");
        }

        var fullPath = ValidatePath(options.FilePath);
        var worksheetName = ValidateWorksheetName(options.WorksheetName);
        if (options.HeaderRow is < 1 or > 1_048_575)
        {
            throw new InvalidOperationException("The header row must be between 1 and 1,048,575.");
        }

        var destinationExists = File.Exists(fullPath);
        var exists = destinationExists && new FileInfo(fullPath).Length > 0;
        string? backupPath = null;
        if (exists && options.CreateBackup)
        {
            backupPath = CreateBackup(fullPath);
        }

        using var workbook = exists ? new XLWorkbook(fullPath) : new XLWorkbook();
        var worksheetExists = workbook.Worksheets.TryGetWorksheet(worksheetName, out var worksheet);
        worksheet ??= workbook.AddWorksheet(worksheetName);
        var sheetWasEmpty = worksheet.RangeUsed() is null;

        var headers = ReadHeaders(worksheet, options.HeaderRow);
        var speciesHeaderKey = NormalizeHeader("Species_Code");
        if (!headers.TryGetValue(speciesHeaderKey, out var speciesColumn))
        {
            if (!sheetWasEmpty)
            {
                throw new InvalidOperationException(
                    $"Worksheet '{worksheetName}' does not contain a Species_Code header on row {options.HeaderRow:N0}.");
            }

            speciesColumn = 1;
            worksheet.Cell(options.HeaderRow, speciesColumn).Value = "Species_Code";
            headers[speciesHeaderKey] = speciesColumn;
        }

        var exportHeaders = GetExportHeaders(records);
        var addedColumns = AddMissingHeaders(worksheet, options.HeaderRow, exportHeaders, headers);
        var existingRows = IndexExistingRows(worksheet, options.HeaderRow, speciesColumn);
        var exportCodes = records
            .Select(record => NormalizeSpeciesCode(record.SpeciesCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preservedUnmatchedRows = existingRows
            .Where(pair => !exportCodes.Contains(pair.Key))
            .Sum(pair => pair.Value.Count);
        var updatedRows = 0;
        var appendedRows = 0;
        var nextRow = Math.Max(options.HeaderRow + 1, (worksheet.LastRowUsed()?.RowNumber() ?? options.HeaderRow) + 1);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var code = NormalizeSpeciesCode(record.SpeciesCode);
            if (existingRows.TryGetValue(code, out var matchingRows))
            {
                foreach (var rowNumber in matchingRows)
                {
                    WriteRecord(worksheet, rowNumber, record, headers);
                    updatedRows++;
                }
            }
            else if (options.AppendMissingSpecies)
            {
                var appendedRow = nextRow++;
                WriteRecord(worksheet, appendedRow, record, headers);
                existingRows[code] = [appendedRow];
                appendedRows++;
            }
        }

        if (!worksheetExists || sheetWasEmpty)
        {
            FormatNewWorksheet(worksheet, options.HeaderRow, exportHeaders.Count);
        }

        SaveSafely(workbook, fullPath, destinationExists);
        return new ITreeExcelMergeResult(
            fullPath,
            worksheetName,
            updatedRows,
            appendedRows,
            preservedUnmatchedRows,
            addedColumns,
            backupPath);
    }

    private static string ValidatePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("Choose an Excel output path.");
        }

        var fullPath = Path.GetFullPath(filePath.Trim());
        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Choose an .xlsx or .xlsm workbook.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The selected output folder does not exist.");
        }

        return fullPath;
    }

    private static string ValidateWorksheetName(string worksheetName)
    {
        var value = string.IsNullOrWhiteSpace(worksheetName) ? "iTree Data" : worksheetName.Trim();
        if (value.Length > 31 || value.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0)
        {
            throw new InvalidOperationException("Enter a valid Excel worksheet name (31 characters or fewer).");
        }

        return value;
    }

    private static Dictionary<string, int> ReadHeaders(IXLWorksheet worksheet, int headerRow)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastColumn = worksheet.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var column = 1; column <= lastColumn; column++)
        {
            var text = worksheet.Cell(headerRow, column).GetFormattedString().Trim();
            var normalized = NormalizeHeader(text);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.TryAdd(normalized, column);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> GetExportHeaders(IReadOnlyList<ITreeExportRecord> records)
    {
        var headers = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            foreach (var header in record.Fields.Keys)
            {
                if (seen.Add(NormalizeHeader(header)))
                {
                    headers.Add(header);
                }
            }
        }

        headers.RemoveAll(header => NormalizeHeader(header) == NormalizeHeader("Species_Code"));
        headers.Insert(0, "Species_Code");
        return headers;
    }

    private static int AddMissingHeaders(
        IXLWorksheet worksheet,
        int headerRow,
        IReadOnlyList<string> exportHeaders,
        IDictionary<string, int> headers)
    {
        var added = 0;
        var nextColumn = Math.Max(1, (worksheet.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 0) + 1);
        var styleSourceColumn = nextColumn > 1 ? nextColumn - 1 : 0;

        foreach (var header in exportHeaders)
        {
            var normalized = NormalizeHeader(header);
            if (headers.ContainsKey(normalized))
            {
                continue;
            }

            var cell = worksheet.Cell(headerRow, nextColumn);
            cell.Value = header;
            if (styleSourceColumn > 0)
            {
                cell.Style = worksheet.Cell(headerRow, styleSourceColumn).Style;
            }
            worksheet.Column(nextColumn).Width = 18;
            headers[normalized] = nextColumn++;
            added++;
        }

        return added;
    }

    private static Dictionary<string, List<int>> IndexExistingRows(
        IXLWorksheet worksheet,
        int headerRow,
        int speciesColumn)
    {
        var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var code = NormalizeSpeciesCode(worksheet.Cell(row, speciesColumn).GetFormattedString());
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            if (!result.TryGetValue(code, out var rows))
            {
                rows = [];
                result[code] = rows;
            }
            rows.Add(row);
        }

        return result;
    }

    private static void WriteRecord(
        IXLWorksheet worksheet,
        int rowNumber,
        ITreeExportRecord record,
        IReadOnlyDictionary<string, int> headers)
    {
        foreach (var pair in record.Fields)
        {
            if (!headers.TryGetValue(NormalizeHeader(pair.Key), out var column))
            {
                continue;
            }

            SetCellValue(worksheet.Cell(rowNumber, column), pair.Value);
        }
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Clear(XLClearOptions.Contents);
                break;
            case bool boolean:
                cell.Value = boolean;
                break;
            case byte number:
                cell.Value = number;
                break;
            case short number:
                cell.Value = number;
                break;
            case int number:
                cell.Value = number;
                break;
            case long number:
                cell.Value = number;
                break;
            case float number:
                cell.Value = number;
                break;
            case double number:
                cell.Value = number;
                break;
            case decimal number:
                cell.Value = number;
                break;
            case DateTime dateTime:
                cell.Value = dateTime;
                break;
            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                break;
        }
    }

    private static void FormatNewWorksheet(IXLWorksheet worksheet, int headerRow, int exportColumnCount)
    {
        var header = worksheet.Range(headerRow, 1, headerRow, Math.Max(1, exportColumnCount));
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#006B7C");
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.RangeUsed()?.SetAutoFilter();
        for (var column = 1; column <= Math.Max(1, exportColumnCount); column++)
        {
            worksheet.Column(column).Width = Math.Min(45, Math.Max(14, worksheet.Column(column).Width));
        }
    }

    private static string CreateBackup(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var backupName = $"{Path.GetFileNameWithoutExtension(filePath)}.backup-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(filePath)}";
        var backupPath = Path.Combine(directory, backupName);
        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static void SaveSafely(XLWorkbook workbook, string filePath, bool replaceExisting)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(filePath)}.{Guid.NewGuid():N}.tmp{Path.GetExtension(filePath)}");
        try
        {
            workbook.SaveAs(temporaryPath);
            if (replaceExisting)
            {
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizeSpeciesCode(string value) => value.Trim().ToUpperInvariant();

    private static string NormalizeHeader(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
