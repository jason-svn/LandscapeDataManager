using System.Globalization;
using System.IO;
using System.Text;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.Dashboard;

/// <summary>One column of the original-data table: header, display width, and how to read it off an item.</summary>
public sealed record OriginalDataColumn<T>(string Header, double Width, Func<T, string> Read);

public sealed record OriginalDataCell(string Text, double Width);

/// <summary>One element, one row — every cell already formatted, plus the Revit id for Select/Zoom.</summary>
public sealed class OriginalDataRow(string uniqueId, IReadOnlyList<OriginalDataCell> cells)
{
    public string UniqueId { get; } = uniqueId;
    public IReadOnlyList<OriginalDataCell> Cells { get; } = cells;

    public bool Matches(string search) =>
        search.Length == 0 || Cells.Any(cell => cell.Text.Contains(search, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The Dashboard's "Show original data" view: every Planting (or Floor) element and every value
/// the report read off it, exactly as stored in Revit — no unit conversion, currency conversion,
/// projection or aggregation, unlike every other Dashboard view. Each tree row carries its
/// stored unit system and currency so the raw numbers can be read correctly.
/// </summary>
internal static class OriginalDataTable
{
    public static readonly IReadOnlyList<OriginalDataColumn<DashboardTreeItem>> TreeColumns =
    [
        new("Element ID", 90, tree => tree.ElementId.ToString(CultureInfo.InvariantCulture)),
        new("Family", 170, tree => tree.FamilyName),
        new("Type", 170, tree => tree.TypeName),
        new("Species code", 100, tree => Text(tree.SpeciesCode)),
        new("Common name", 160, tree => Text(tree.CommonName)),
        new("Scientific name", 180, tree => Text(tree.ScientificName)),
        new("Species type", 110, tree => Text(tree.SpeciesType)),
        new("Level", 120, tree => Text(tree.LevelName)),
        new("Design option", 200, tree => DashboardUnitLabels.FormatDesignOption(tree.DesignOption)),
        new("Status", 110, tree => tree.Status),
        new("Native status", 110, tree => Text(tree.NativeStatus)),
        new("Bloom months", 120, tree => Text(tree.BloomMonths)),
        new("Ecological functions", 180, tree => Text(tree.EcologicalFunctions)),
        new("Canopy area (m²)", 120, tree => Number(tree.CanopyAreaSquareMeters)),
        new("Stored units", 100, tree => tree.StoredUnitSystem),
        new("Stored currency", 110, tree => tree.StoredCurrency),
        new("Exchange rate used", 120, tree => Number(tree.StoredExchangeRateUsed)),
        new("Carbon sequestered — annual", 150, tree => Number(tree.CarbonSequesteredAnnual)),
        new("Carbon sequestered — lifetime", 150, tree => Number(tree.CarbonSequesteredLifetimeTotal)),
        new("CO₂ equivalent — annual", 150, tree => Number(tree.CO2EquivalentAnnual)),
        new("CO₂ equivalent — lifetime", 150, tree => Number(tree.CO2EquivalentLifetimeTotal)),
        new("CO removed — annual", 140, tree => Number(tree.CORemovedAnnual)),
        new("CO removed — lifetime", 140, tree => Number(tree.CORemovedLifetimeTotal)),
        new("NO₂ removed — annual", 140, tree => Number(tree.NO2RemovedAnnual)),
        new("NO₂ removed — lifetime", 140, tree => Number(tree.NO2RemovedLifetimeTotal)),
        new("O₃ removed — annual", 140, tree => Number(tree.O3RemovedAnnual)),
        new("O₃ removed — lifetime", 140, tree => Number(tree.O3RemovedLifetimeTotal)),
        new("PM2.5 removed — annual", 150, tree => Number(tree.PM25RemovedAnnual)),
        new("PM2.5 removed — lifetime", 150, tree => Number(tree.PM25RemovedLifetimeTotal)),
        new("SO₂ removed — annual", 140, tree => Number(tree.SO2RemovedAnnual)),
        new("SO₂ removed — lifetime", 140, tree => Number(tree.SO2RemovedLifetimeTotal)),
        new("Rainfall intercepted — annual", 160, tree => Number(tree.RainfallInterceptedAnnual)),
        new("Rainfall intercepted — lifetime", 160, tree => Number(tree.RainfallInterceptedLifetimeTotal)),
        new("Runoff avoided — annual", 150, tree => Number(tree.RunoffAvoidedAnnual)),
        new("Runoff avoided — lifetime", 150, tree => Number(tree.RunoffAvoidedLifetimeTotal)),
        new("Total cost saved — annual", 150, tree => Number(tree.CostSavedAnnual)),
        new("Total cost saved — lifetime", 150, tree => Number(tree.CostSavedLifetimeTotal)),
        new("Carbon cost saved — annual", 160, tree => Number(tree.CarbonCostSavedAnnual)),
        new("Carbon cost saved — lifetime", 160, tree => Number(tree.CarbonCostSavedLifetimeTotal)),
        new("Stormwater cost saved — annual", 170, tree => Number(tree.StormWaterCostSavedAnnual)),
        new("Stormwater cost saved — lifetime", 170, tree => Number(tree.StormWaterCostSavedLifetimeTotal)),
        new("Air pollution cost saved — annual", 180, tree => Number(tree.AirPollutionCostSavedAnnual)),
        new("Air pollution cost saved — lifetime", 180, tree => Number(tree.AirPollutionCostSavedLifetimeTotal)),
        new("Unique ID", 300, tree => tree.UniqueId)
    ];

    public static readonly IReadOnlyList<OriginalDataColumn<DashboardFloorItem>> FloorColumns =
    [
        new("Element ID", 90, floor => floor.ElementId.ToString(CultureInfo.InvariantCulture)),
        new("Family", 150, floor => floor.FamilyName),
        new("Type", 200, floor => floor.TypeName),
        new("LDS type", 200, floor => Text(floor.LdsType)),
        new("Surface class", 110, floor => Text(floor.SurfaceClass)),
        new("Level", 120, floor => Text(floor.LevelName)),
        new("Design option", 200, floor => DashboardUnitLabels.FormatDesignOption(floor.DesignOption)),
        new("Area (m²)", 100, floor => Number(floor.AreaSquareMeters)),
        new("Carbon sequestered — annual", 150, floor => Number(floor.CarbonSequesteredAnnual)),
        new("Runoff avoided — annual", 150, floor => Number(floor.RunoffAvoidedAnnual)),
        new("Pollution removed — annual", 150, floor => Number(floor.PollutionMassRemovedAnnual)),
        new("Cost saved — annual", 130, floor => Number(floor.CostSavedAnnual)),
        new("Oxygen produced — annual", 150, floor => Number(floor.OxygenProducedAnnual)),
        new("Total GWP", 110, floor => Number(floor.TotalGwp)),
        new("Surface temp. reduction", 150, floor => Number(floor.SurfaceTempReduction)),
        new("Air temp. reduction", 130, floor => Number(floor.AirTempReduction)),
        new("BNG role", 100, floor => Text(floor.Bng?.PhaseRole)),
        new("BNG proposed habitat", 200, floor => Text(floor.Bng?.ProposedHabitat)),
        new("BNG condition", 110, floor => Text(floor.Bng?.Condition)),
        new("BNG strategic significance", 170, floor => Text(floor.Bng?.StrategicSignificance)),
        new("BNG broad habitat", 150, floor => Text(floor.Bng?.BroadHabitat)),
        new("BNG distinctiveness", 130, floor => Text(floor.Bng?.Distinctiveness)),
        new("BNG habitat units", 120, floor => floor.Bng is null ? string.Empty : Number(floor.Bng.HabitatUnits)),
        new("BNG baseline habitat", 200, floor => Text(floor.Bng?.BaselineHabitat)),
        new("BNG baseline condition", 150, floor => Text(floor.Bng?.BaselineCondition)),
        new("BNG baseline units", 120, floor => floor.Bng is null ? string.Empty : Number(floor.Bng.BaselineUnits)),
        new("BNG enhanced", 100, floor => floor.Bng is null ? string.Empty : floor.Bng.Enhanced ? "Yes" : "No"),
        new("BNG status", 110, floor => Text(floor.Bng?.StoredStatus)),
        new("Unique ID", 300, floor => floor.UniqueId)
    ];

    public static IReadOnlyList<OriginalDataCell> Headers<T>(IReadOnlyList<OriginalDataColumn<T>> columns) =>
        columns.Select(column => new OriginalDataCell(column.Header, column.Width)).ToList();

    public static List<OriginalDataRow> Rows<T>(IEnumerable<T> items, IReadOnlyList<OriginalDataColumn<T>> columns, Func<T, string> uniqueId) =>
        items.Select(item => new OriginalDataRow(
                uniqueId(item),
                columns.Select(column => new OriginalDataCell(column.Read(item), column.Width)).ToList()))
            .ToList();

    public static double TotalWidth<T>(IReadOnlyList<OriginalDataColumn<T>> columns) => columns.Sum(column => column.Width);

    public static async Task WriteCsvAsync(string path, IReadOnlyList<OriginalDataCell> headers, IEnumerable<OriginalDataRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(header => Escape(header.Text))));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Cells.Select(cell => Escape(cell.Text))));
        }

        // UTF-8 with BOM so Excel opens the subscripts (CO₂, m²) and non-ASCII names correctly.
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Text(string? value) => value ?? string.Empty;

    // Ten significant digits rather than fixed decimals: per-tree pollutant removals can be far
    // below 0.000001 and must not round to 0. Invariant, so the CSV re-imports cleanly whatever
    // the viewer's regional decimal separator is.
    private static string Number(double value) => value.ToString("G10", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
