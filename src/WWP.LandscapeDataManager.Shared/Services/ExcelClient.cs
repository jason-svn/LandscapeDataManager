using System.Text.Json;
using ClosedXML.Excel;

namespace WWP.LandscapeDataManager.App.Services;

internal sealed class ExcelClient
{
    public Task<IReadOnlyList<AirtableRecord>> GetRecordsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Select an Excel workbook.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The selected Excel workbook could not be found.", fullPath);
        }

        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Select an Excel 2007+ .xlsx or .xlsm workbook.");
        }

        return Task.Run<IReadOnlyList<AirtableRecord>>(
            () => ReadWorkbook(fullPath, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<AirtableRecord> ReadWorkbook(
        string filePath,
        CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets
            .FirstOrDefault(candidate => candidate.RangeUsed() is not null)
            ?? throw new InvalidDataException("The Excel workbook does not contain a non-empty worksheet.");
        var range = worksheet.RangeUsed()
                    ?? throw new InvalidDataException("The Excel worksheet is empty.");

        var firstRow = range.FirstRow();
        var headers = firstRow.Cells()
            .Select(cell => cell.GetFormattedString().Trim())
            .ToList();
        if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException(
                $"Worksheet '{worksheet.Name}' must use the first populated row for non-empty column headers.");
        }

        var duplicateHeader = headers
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateHeader is not null)
        {
            throw new InvalidDataException(
                $"Worksheet '{worksheet.Name}' contains the duplicate header '{duplicateHeader.Key}'.");
        }

        var records = new List<AirtableRecord>();
        foreach (var sourceRow in range.Rows().Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = sourceRow.Cells(1, headers.Count)
                .Select(cell => cell.GetFormattedString())
                .ToList();
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                fields[headers[columnIndex]] = JsonSerializer.SerializeToElement(values[columnIndex]);
            }

            records.Add(new AirtableRecord($"excel-{sourceRow.RowNumber()}", fields));
        }

        return records;
    }
}
