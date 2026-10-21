using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

/// <summary>A run of related columns that share a swim lane. <see cref="Lane"/> names a LimLane colour, or null for none.</summary>
public sealed record OriginalDataGroup(string Label, string? Lane);

/// <summary>
/// One column of the original-data table: its CSV header, display width, how to read it off an
/// item, its lane, and the on-screen header (name, then unit or qualifier) when that differs.
/// </summary>
public sealed record OriginalDataColumn<T>(string Header, double Width, Func<T, string> Read, OriginalDataGroup? Group = null, string? Display = null, bool Numeric = false);

public sealed record OriginalDataCell(string Text, double Width, TextAlignment Alignment = TextAlignment.Left);

/// <summary>One swim lane across the header and rows: a group's name, total width and colours.</summary>
public sealed record OriginalDataLane(string Label, double Width, Brush Background, Brush Foreground)
{
    /// <summary>Inset 3px each side, so neighbouring lanes have a 6px gap.</summary>
    public double BandWidth => Math.Max(0, Width - 6);
}

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
    private static readonly OriginalDataGroup Species = new("Species", "Teal");
    private static readonly OriginalDataGroup Placement = new("Placement", "Grey");
    private static readonly OriginalDataGroup StoredBasis = new("Stored basis", "Violet");
    private static readonly OriginalDataGroup Carbon = new("Carbon", "Green");
    private static readonly OriginalDataGroup Air = new("Air quality", "Amber");
    private static readonly OriginalDataGroup Water = new("Water", "Blue");
    private static readonly OriginalDataGroup CostSaved = new("Cost saved", "Grey");
    private static readonly OriginalDataGroup Cooling = new("Cooling", "Teal");
    private static readonly OriginalDataGroup Bng = new("BNG", "Violet");
    private static readonly OriginalDataGroup BngBaseline = new("BNG baseline", "Amber");

    public static readonly IReadOnlyList<OriginalDataColumn<DashboardTreeItem>> TreeColumns =
    [
        new("Element ID", 90, tree => tree.ElementId.ToString(CultureInfo.InvariantCulture), Numeric: true),
        new("Family", 170, tree => tree.FamilyName),
        new("Type", 170, tree => tree.TypeName),
        new("Species code", 100, tree => Text(tree.SpeciesCode), Species, "Code"),
        new("Common name", 160, tree => Text(tree.CommonName), Species),
        new("Scientific name", 180, tree => Text(tree.ScientificName), Species),
        new("Species type", 110, tree => Text(tree.SpeciesType), Species, "Type"),
        new("Native status", 110, tree => Text(tree.NativeStatus), Species),
        new("Bloom months", 120, tree => Text(tree.BloomMonths), Species),
        new("Ecological functions", 180, tree => Text(tree.EcologicalFunctions), Species),
        new("Level", 120, tree => Text(tree.LevelName), Placement),
        new("Design option", 200, tree => DashboardUnitLabels.FormatDesignOption(tree.DesignOption), Placement),
        new("Workset", 170, tree => Text(tree.Workset), Placement),
        new("Growth year", 90, tree => GrowthYears.Of(tree)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, Placement, Numeric: true),
        new("Status", 110, tree => tree.Status, Placement),
        new("Canopy area (m²)", 110, tree => Number(tree.CanopyAreaSquareMeters), Placement, "Canopy area\nm²", true),
        new("Stored units", 100, tree => tree.StoredUnitSystem, StoredBasis, "Units"),
        new("Stored currency", 100, tree => tree.StoredCurrency, StoredBasis, "Currency"),
        new("Exchange rate used", 120, tree => Number(tree.StoredExchangeRateUsed), StoredBasis, "Exchange rate\nto USD", true),
        new("Carbon sequestered — annual", 130, tree => Number(tree.CarbonSequesteredAnnual), Carbon, "Sequestered\nannual", true),
        new("Carbon sequestered — lifetime", 130, tree => Number(tree.CarbonSequesteredLifetimeTotal), Carbon, "Sequestered\nlifetime", true),
        new("CO₂ equivalent — annual", 140, tree => Number(tree.CO2EquivalentAnnual), Carbon, "CO₂ equivalent\nannual", true),
        new("CO₂ equivalent — lifetime", 140, tree => Number(tree.CO2EquivalentLifetimeTotal), Carbon, "CO₂ equivalent\nlifetime", true),
        new("CO removed — annual", 120, tree => Number(tree.CORemovedAnnual), Air, "CO\nannual", true),
        new("CO removed — lifetime", 120, tree => Number(tree.CORemovedLifetimeTotal), Air, "CO\nlifetime", true),
        new("NO₂ removed — annual", 120, tree => Number(tree.NO2RemovedAnnual), Air, "NO₂\nannual", true),
        new("NO₂ removed — lifetime", 120, tree => Number(tree.NO2RemovedLifetimeTotal), Air, "NO₂\nlifetime", true),
        new("O₃ removed — annual", 120, tree => Number(tree.O3RemovedAnnual), Air, "O₃\nannual", true),
        new("O₃ removed — lifetime", 120, tree => Number(tree.O3RemovedLifetimeTotal), Air, "O₃\nlifetime", true),
        new("PM2.5 removed — annual", 120, tree => Number(tree.PM25RemovedAnnual), Air, "PM2.5\nannual", true),
        new("PM2.5 removed — lifetime", 120, tree => Number(tree.PM25RemovedLifetimeTotal), Air, "PM2.5\nlifetime", true),
        new("SO₂ removed — annual", 120, tree => Number(tree.SO2RemovedAnnual), Air, "SO₂\nannual", true),
        new("SO₂ removed — lifetime", 120, tree => Number(tree.SO2RemovedLifetimeTotal), Air, "SO₂\nlifetime", true),
        new("Rainfall intercepted — annual", 150, tree => Number(tree.RainfallInterceptedAnnual), Water, "Rain intercepted\nannual", true),
        new("Rainfall intercepted — lifetime", 150, tree => Number(tree.RainfallInterceptedLifetimeTotal), Water, "Rain intercepted\nlifetime", true),
        new("Runoff avoided — annual", 130, tree => Number(tree.RunoffAvoidedAnnual), Water, "Runoff avoided\nannual", true),
        new("Runoff avoided — lifetime", 130, tree => Number(tree.RunoffAvoidedLifetimeTotal), Water, "Runoff avoided\nlifetime", true),
        new("Total cost saved — annual", 120, tree => Number(tree.CostSavedAnnual), CostSaved, "Total\nannual", true),
        new("Total cost saved — lifetime", 120, tree => Number(tree.CostSavedLifetimeTotal), CostSaved, "Total\nlifetime", true),
        new("Carbon cost saved — annual", 120, tree => Number(tree.CarbonCostSavedAnnual), CostSaved, "Carbon\nannual", true),
        new("Carbon cost saved — lifetime", 120, tree => Number(tree.CarbonCostSavedLifetimeTotal), CostSaved, "Carbon\nlifetime", true),
        new("Stormwater cost saved — annual", 120, tree => Number(tree.StormWaterCostSavedAnnual), CostSaved, "Stormwater\nannual", true),
        new("Stormwater cost saved — lifetime", 120, tree => Number(tree.StormWaterCostSavedLifetimeTotal), CostSaved, "Stormwater\nlifetime", true),
        new("Air pollution cost saved — annual", 130, tree => Number(tree.AirPollutionCostSavedAnnual), CostSaved, "Air pollution\nannual", true),
        new("Air pollution cost saved — lifetime", 130, tree => Number(tree.AirPollutionCostSavedLifetimeTotal), CostSaved, "Air pollution\nlifetime", true),
        new("Unique ID", 300, tree => tree.UniqueId)
    ];

    public static readonly IReadOnlyList<OriginalDataColumn<DashboardFloorItem>> FloorColumns =
    [
        new("Element ID", 90, floor => floor.ElementId.ToString(CultureInfo.InvariantCulture), Numeric: true),
        new("Family", 150, floor => floor.FamilyName),
        new("Type", 200, floor => floor.TypeName),
        new("LDS type", 200, floor => Text(floor.LdsType)),
        new("Surface class", 110, floor => Text(floor.SurfaceClass)),
        new("Level", 120, floor => Text(floor.LevelName), Placement),
        new("Design option", 200, floor => DashboardUnitLabels.FormatDesignOption(floor.DesignOption), Placement),
        new("Area (m²)", 100, floor => Number(floor.AreaSquareMeters), Placement, "Area\nm²", true),
        new("Carbon sequestered — annual", 130, floor => Number(floor.CarbonSequesteredAnnual), Carbon, "Sequestered\nannual", true),
        new("Oxygen produced — annual", 130, floor => Number(floor.OxygenProducedAnnual), Carbon, "Oxygen\nannual", true),
        new("Total GWP", 110, floor => Number(floor.TotalGwp), Carbon, "Embodied\ntotal GWP", true),
        new("Runoff avoided — annual", 130, floor => Number(floor.RunoffAvoidedAnnual), Water, "Runoff avoided\nannual", true),
        new("Pollution removed — annual", 130, floor => Number(floor.PollutionMassRemovedAnnual), Air, "Pollution\nannual", true),
        new("Cost saved — annual", 120, floor => Number(floor.CostSavedAnnual), CostSaved, "Total\nannual", true),
        new("Surface temp. reduction", 130, floor => Number(floor.SurfaceTempReduction), Cooling, "Surface temp.\nreduction", true),
        new("Air temp. reduction", 120, floor => Number(floor.AirTempReduction), Cooling, "Air temp.\nreduction", true),
        new("BNG role", 100, floor => Text(floor.Bng?.PhaseRole), Bng, "Role"),
        new("BNG status", 110, floor => Text(floor.Bng?.StoredStatus), Bng, "Status"),
        new("BNG proposed habitat", 200, floor => Text(floor.Bng?.ProposedHabitat), Bng, "Habitat\nproposed"),
        new("BNG condition", 110, floor => Text(floor.Bng?.Condition), Bng, "Condition"),
        new("BNG strategic significance", 170, floor => Text(floor.Bng?.StrategicSignificance), Bng, "Strategic\nsignificance"),
        new("BNG broad habitat", 150, floor => Text(floor.Bng?.BroadHabitat), Bng, "Broad habitat"),
        new("BNG distinctiveness", 140, floor => Text(floor.Bng?.Distinctiveness), Bng, "Distinctiveness"),
        new("BNG habitat units", 120, floor => floor.Bng is null ? string.Empty : Number(floor.Bng.HabitatUnits), Bng, "Habitat units\nHU", true),
        new("BNG baseline habitat", 200, floor => Text(floor.Bng?.BaselineHabitat), BngBaseline, "Habitat"),
        new("BNG baseline condition", 150, floor => Text(floor.Bng?.BaselineCondition), BngBaseline, "Condition"),
        new("BNG baseline units", 110, floor => floor.Bng is null ? string.Empty : Number(floor.Bng.BaselineUnits), BngBaseline, "Units\nHU", true),
        new("BNG enhanced", 100, floor => floor.Bng is null ? string.Empty : floor.Bng.Enhanced ? "Yes" : "No", BngBaseline, "Enhanced"),
        new("Unique ID", 300, floor => floor.UniqueId)
    ];

    /// <summary>The CSV header row: each column's full, single-line name.</summary>
    public static IReadOnlyList<OriginalDataCell> Headers<T>(IReadOnlyList<OriginalDataColumn<T>> columns) =>
        columns.Select(column => new OriginalDataCell(column.Header, column.Width)).ToList();

    /// <summary>The on-screen header row: name on top, unit or qualifier underneath.</summary>
    public static IReadOnlyList<OriginalDataCell> DisplayHeaders<T>(IReadOnlyList<OriginalDataColumn<T>> columns) =>
        columns.Select(column => new OriginalDataCell(column.Display ?? column.Header, column.Width, Align(column))).ToList();

    /// <summary>Consecutive columns of one group merged into a lane; ungrouped runs get a transparent one.</summary>
    public static IReadOnlyList<OriginalDataLane> Lanes<T>(IReadOnlyList<OriginalDataColumn<T>> columns, Func<string, Brush> brush)
    {
        var lanes = new List<OriginalDataLane>();
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var run in columns.Aggregate(new List<(OriginalDataGroup? Group, double Width)>(), (runs, column) =>
                 {
                     if (runs.Count > 0 && ReferenceEquals(runs[^1].Group, column.Group))
                     {
                         runs[^1] = (column.Group, runs[^1].Width + column.Width);
                     }
                     else
                     {
                         runs.Add((column.Group, column.Width));
                     }

                     return runs;
                 }))
        {
            lanes.Add(run.Group is { Lane: { } lane } group
                ? new OriginalDataLane(group.Label.ToUpperInvariant(), run.Width, brush($"LimLane{lane}Brush"), brush($"LimLane{lane}TextBrush"))
                : new OriginalDataLane(string.Empty, run.Width, transparent, transparent));
        }

        return lanes;
    }

    public static List<OriginalDataRow> Rows<T>(IEnumerable<T> items, IReadOnlyList<OriginalDataColumn<T>> columns, Func<T, string> uniqueId) =>
        items.Select(item => new OriginalDataRow(
                uniqueId(item),
                columns.Select(column => new OriginalDataCell(column.Read(item), column.Width, Align(column))).ToList()))
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

    private static TextAlignment Align<T>(OriginalDataColumn<T> column) => column.Numeric ? TextAlignment.Right : TextAlignment.Left;

    // Ten significant digits rather than fixed decimals: per-tree pollutant removals can be far
    // below 0.000001 and must not round to 0. Invariant, so the CSV re-imports cleanly whatever
    // the viewer's regional decimal separator is.
    private static string Number(double value) => value.ToString("G10", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
