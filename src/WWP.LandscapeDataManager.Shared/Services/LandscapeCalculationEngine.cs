using System.Globalization;
using System.Text;
using System.Text.Json;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

public static class LandscapeCalculationEngine
{
    private static readonly string[] TreeKeyFields =
    [
        "Types",
        "Species names",
        "species",
        "type"
    ];

    private static readonly string[] SurfaceKeyFields =
    [
        "Planting type",
        "Types",
        "type"
    ];

    private static readonly string[] TreeCarbonFields =
    [
        "Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)",
        "WWP_Total_GWP"
    ];

    private static readonly string[] SurfaceCarbonFields =
    [
        "WWP_Total_GWP",
        "Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)"
    ];

    private static readonly string[] OxygenFields =
    [
        "Oxygen levels O2 kg/yr (16/18 girth)",
        "WWP_Oxygen_Levels"
    ];

    private static readonly string[] RunoffFields =
    [
        "Avoided runoff m3/yr (16/18 girth)",
        "WWP_Avoided_Water_Runoff"
    ];

    private static readonly string[] PollutantFields = ["WWP_Pollutants_Removed"];
    private static readonly string[] MaintenanceFields = ["Maintenance Costs", "WWP_Maintenance_Cost"];
    private static readonly string[] SavingsFields = ["COST_SAVED", "WWP_Cost_Saved"];

    public static CalculationReport Calculate(
        IReadOnlyList<AirtableRecord> sourceRecords,
        ModelScanResult scan)
    {
        var treeIndex = BuildIndex(sourceRecords, TreeKeyFields);
        var surfaceIndex = BuildIndex(sourceRecords, SurfaceKeyFields);
        var rows = new List<CalculationRow>();
        var totals = new CalculationTotals();
        var calculated = 0;
        var missing = 0;
        var ambiguous = 0;
        var invalid = 0;

        foreach (var item in scan.Items)
        {
            var sourceIndex = item.IsAreaBased ? surfaceIndex : treeIndex;
            sourceIndex.TryGetValue(Normalize(item.TypeName), out var matches);

            if (matches is null || matches.Count == 0)
            {
                missing++;
                rows.Add(CreateEmptyRow("Missing source", item));
                continue;
            }

            if (matches.Count > 1)
            {
                ambiguous++;
                rows.Add(CreateEmptyRow("Duplicate source", item));
                continue;
            }

            var source = matches[0];
            var carbon = ReadNumber(source, item.IsAreaBased ? SurfaceCarbonFields : TreeCarbonFields);
            var oxygen = ReadNumber(source, OxygenFields);
            var runoff = ReadNumber(source, RunoffFields);
            var pollutants = ReadNumber(source, PollutantFields);
            var maintenance = ReadNumber(source, MaintenanceFields);
            var savings = ReadNumber(source, SavingsFields);
            var values = new[] { carbon, oxygen, runoff, pollutants, maintenance, savings };

            if (values.Any(value => value.Error is not null))
            {
                invalid++;
                var badFields = string.Join(", ", values
                    .Where(value => value.Error is not null)
                    .Select(value => value.Error)
                    .Distinct());
                rows.Add(CreateEmptyRow($"Invalid: {badFields}", item));
                continue;
            }

            var multiplier = item.IsAreaBased
                ? item.AreaSquareMetres
                : item.ElementCount;
            var result = new CalculationTotals(
                carbon.Value * multiplier,
                oxygen.Value * multiplier,
                runoff.Value * multiplier,
                pollutants.Value * multiplier,
                maintenance.Value * multiplier,
                savings.Value * multiplier);

            totals += result;
            calculated++;
            rows.Add(new CalculationRow(
                "Calculated",
                item.TypeName,
                FormatBasis(item),
                result.Carbon,
                result.Oxygen,
                result.Runoff,
                result.Pollutants,
                result.MaintenanceCost,
                result.CostSavings));
        }

        return new CalculationReport(
            rows,
            totals,
            calculated,
            missing,
            ambiguous,
            invalid);
    }

    private static Dictionary<string, List<AirtableRecord>> BuildIndex(
        IEnumerable<AirtableRecord> records,
        IReadOnlyList<string> keyFields)
    {
        var index = new Dictionary<string, List<AirtableRecord>>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            var keys = keyFields
                .Where(record.Fields.ContainsKey)
                .SelectMany(field => ReadTextValues(record.Fields[field]))
                .Select(Normalize)
                .Where(key => key.Length > 0)
                .Distinct(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                if (!index.TryGetValue(key, out var matches))
                {
                    matches = [];
                    index[key] = matches;
                }

                if (!matches.Contains(record))
                {
                    matches.Add(record);
                }
            }
        }

        return index;
    }

    private static IEnumerable<string> ReadTextValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var text in ReadTextValues(item))
                {
                    yield return text;
                }
            }
        }
    }

    private static NumericValue ReadNumber(
        AirtableRecord record,
        IReadOnlyList<string> fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (!record.Fields.TryGetValue(fieldName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return new NumericValue(number, null);
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                return new NumericValue(0, fieldName);
            }

            var text = value.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(text) || text is "-" or "—")
            {
                continue;
            }

            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands |
                                        NumberStyles.AllowCurrencySymbol;
            if (double.TryParse(text, styles, CultureInfo.InvariantCulture, out number) ||
                double.TryParse(text, styles, CultureInfo.CurrentCulture, out number))
            {
                return new NumericValue(number, null);
            }

            return new NumericValue(0, fieldName);
        }

        return new NumericValue(0, null);
    }

    private static CalculationRow CreateEmptyRow(string status, ModelScanItem item) =>
        new(status, item.TypeName, FormatBasis(item), 0, 0, 0, 0, 0, 0);

    private static string FormatBasis(ModelScanItem item) => item.IsAreaBased
        ? $"{item.AreaSquareMetres:N2} m²"
        : $"{item.ElementCount:N0} ea";

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private sealed record NumericValue(double Value, string? Error);
}

public sealed record CalculationReport(
    IReadOnlyList<CalculationRow> Rows,
    CalculationTotals Totals,
    int CalculatedCount,
    int MissingCount,
    int AmbiguousCount,
    int InvalidCount);

public sealed record CalculationTotals(
    double Carbon = 0,
    double Oxygen = 0,
    double Runoff = 0,
    double Pollutants = 0,
    double MaintenanceCost = 0,
    double CostSavings = 0)
{
    public double NetSavings => CostSavings - MaintenanceCost;

    public static CalculationTotals operator +(CalculationTotals left, CalculationTotals right) =>
        new(
            left.Carbon + right.Carbon,
            left.Oxygen + right.Oxygen,
            left.Runoff + right.Runoff,
            left.Pollutants + right.Pollutants,
            left.MaintenanceCost + right.MaintenanceCost,
            left.CostSavings + right.CostSavings);
}
