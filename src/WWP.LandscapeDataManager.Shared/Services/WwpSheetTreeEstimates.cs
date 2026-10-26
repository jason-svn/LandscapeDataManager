using System.Globalization;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>How a tree was matched to a WWP landscape data sheet row, strongest first.</summary>
public enum SheetRowMatch
{
    None,
    SpeciesCode,
    TypeName,
    FamilyName,
    Generic
}

/// <summary>What filling trees from the sheet did. <see cref="Message"/> is set when nothing ran.</summary>
public sealed record WwpSheetFillSummary(
    int Trees,
    int Estimated,
    int PollenWritten,
    IReadOnlyList<string> UnmatchedTypes,
    string? Message = null)
{
    public string Describe() => Message ?? (
        $"WWP sheet: {Estimated:N0} tree(s) i-Tree hasn't calculated estimated from the sheet, pollen for {PollenWritten:N0}" +
        (UnmatchedTypes.Count > 0 ? $"; no sheet row for {string.Join(", ", UnmatchedTypes.Take(4))}{(UnmatchedTypes.Count > 4 ? $" (+{UnmatchedTypes.Count - 4} more)" : string.Empty)}." : "."));
}

/// <summary>
/// The WWP landscape data sheet as a fallback for trees: a tree i-Tree hasn't calculated (no species
/// code, missing or invalid inputs, an API error) gets its yearly benefits from its sheet row's
/// per-tree figures — CO₂e, carbon, oxygen, runoff, pollutants and money saved — stored with status
/// <see cref="EstimatedStatus"/>. A tree with i-Tree results is never overwritten, and no input
/// signature is stored, so Tree Calculator still calculates an estimated tree with i-Tree once its
/// inputs are complete. Every matched tree also gets the sheet's pollen, which i-Tree doesn't model.
/// Used by Tree Calculator (after its i-Tree run) and Floor Calculator (with its floors).
/// </summary>
public static class WwpSheetTreeEstimates
{
    public const string EstimatedStatus = "Estimated";
    public const string EngineVersion = "WWP landscape data sheet";
    public const string PollenParameter = "!_S_PLT_Result_PollenAnnual_Mass";

    /// <summary>
    /// The tree's sheet row: by species code, then a sheet type named like the Revit type or family,
    /// then the sheet's Generic tree. Only Vegetation rows count — planting areas and surfaces are per m².
    /// </summary>
    public static (WwpLdsCoefficientRecord? Row, SheetRowMatch How) FindRow(
        string? speciesCode, string familyName, string typeName, IReadOnlyList<WwpLdsCoefficientRecord> rows)
    {
        var trees = rows.Where(IsTreeRow).ToList();
        if (!string.IsNullOrWhiteSpace(speciesCode) &&
            trees.FirstOrDefault(row => string.Equals(row.SpeciesCode?.Trim(), speciesCode.Trim(), StringComparison.OrdinalIgnoreCase)) is { } bySpecies)
        {
            return (bySpecies, SheetRowMatch.SpeciesCode);
        }

        var (named, how) = FindRowByName(familyName, typeName, trees);
        if (named is not null)
        {
            return (named, how);
        }

        return trees.FirstOrDefault(row => SameName(row.TypeName, "Generic")) is { } generic
            ? (generic, SheetRowMatch.Generic)
            : (null, SheetRowMatch.None);
    }

    /// <summary>
    /// The tree's sheet row by name alone — a sheet type named exactly like the Revit type, else like
    /// its family — with no species-code or Generic fallback. Tree Searcher uses it to find a type's
    /// species from the sheet's Species Code when it has none yet.
    /// </summary>
    public static (WwpLdsCoefficientRecord? Row, SheetRowMatch How) FindRowByName(
        string familyName, string typeName, IReadOnlyList<WwpLdsCoefficientRecord> rows)
    {
        var trees = rows.Where(IsTreeRow).ToList();
        if (trees.FirstOrDefault(row => SameName(row.TypeName, typeName)) is { } byType)
        {
            return (byType, SheetRowMatch.TypeName);
        }

        return trees.FirstOrDefault(row => SameName(row.TypeName, familyName)) is { } byFamily
            ? (byFamily, SheetRowMatch.FamilyName)
            : (null, SheetRowMatch.None);
    }

    /// <summary>
    /// The species a sheet tree row stands for: its Species Code, else the i-Tree species whose
    /// scientific name is the row's name (the sheet names trees like "Fraxinus_Excelsior" but gives
    /// only some of them a code). <paramref name="codeByScientificName"/> is from <see cref="ScientificNameIndex"/>.
    /// </summary>
    public static string? SpeciesCodeFor(WwpLdsCoefficientRecord row, IReadOnlyDictionary<string, string> codeByScientificName) =>
        row.SpeciesCode ??
        (codeByScientificName.TryGetValue(DefaultParameterMappingCatalog.Normalize(row.TypeName), out var code) ? code : null);

    /// <summary>i-Tree species codes by scientific name, letters and digits only ("FRAXINUSEXCELSIOR").</summary>
    public static IReadOnlyDictionary<string, string> ScientificNameIndex(IEnumerable<SpeciesCatalogueRecord> catalogue)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in catalogue.Where(record => !string.IsNullOrWhiteSpace(record.ScientificName)))
        {
            index.TryAdd(DefaultParameterMappingCatalog.Normalize(record.ScientificName), record.SpeciesCode);
        }

        return index;
    }

    public static bool IsTreeRow(WwpLdsCoefficientRecord row) =>
        string.Equals(row.Category, "Vegetation", StringComparison.OrdinalIgnoreCase);

    private static bool SameName(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        DefaultParameterMappingCatalog.Normalize(left) == DefaultParameterMappingCatalog.Normalize(right);

    /// <summary>Only a tree without i-Tree results: one whose stored status is Calculated keeps them, even if stale.</summary>
    public static bool NeedsEstimate(PlantingInstanceValidationItem item) =>
        !string.Equals(item.StoredStatus, "Calculated", StringComparison.OrdinalIgnoreCase);

    /// <summary>Why i-Tree hasn't calculated this tree, in words for the stored details.</summary>
    public static string Reason(PlantingInstanceValidationItem item)
    {
        var status = PlantingInstanceStatusEvaluator.Evaluate(item, ITreeBatchCalculator.SignatureEngineVersion);
        return status.Status switch
        {
            "Ready" => "not calculated with i-Tree yet",
            EstimatedStatus => "not calculated with i-Tree yet",
            _ => string.IsNullOrWhiteSpace(status.Details) ? status.Status : status.Details!
        };
    }

    /// <summary>The estimate's writes: status and tracking, the sheet's per-tree figures, and zeros for i-Tree-only results.</summary>
    /// <param name="sheetCurrencies">Rates into the project currency for the sheet's money (see <see cref="WwpLdsSheet.CurrencyFactorsAsync"/>).</param>
    /// <param name="usdRate">USD→project-currency rate, stored like an i-Tree result's so the Dashboard can convert it.</param>
    public static IReadOnlyList<InstanceParameterWriteItem> BuildEstimateWrites(
        string uniqueId,
        WwpLdsCoefficientRecord row,
        SheetRowMatch how,
        string reason,
        string unitSystem,
        string currency,
        CurrencyFactors sheetCurrencies,
        double usdRate)
    {
        var co2Equivalent = row.Co2SequesteredAnnual ?? 0d;
        var items = new List<InstanceParameterWriteItem>
        {
            Text(uniqueId, "!_S_PLT_iTreeResult_Status_Text", EstimatedStatus),
            Text(uniqueId, "!_S_PLT_iTreeResult_Details_Text",
                $"Estimated from the WWP landscape data sheet ({Describe(row, how)}, per 16/18-girth tree): i-Tree couldn't calculate it — {reason}."),
            Text(uniqueId, "!_S_PLT_iTreeResult_LastUpdated_Text", LocalTimestamp()),
            // No signature: Tree Calculator sees the tree as Ready, and i-Tree replaces the estimate.
            Text(uniqueId, "!_S_PLT_iTreeResult_InputSignature_Text", string.Empty),
            Text(uniqueId, "!_S_PLT_iTreeResult_UnitSystem_Text", unitSystem),
            Text(uniqueId, "!_S_PLT_iTreeResult_EngineVersion_Text", EngineVersion),
            Text(uniqueId, "!_S_PLT_iTreeResult_CurrencyUsed_Text", currency),
            Number(uniqueId, "!_S_PLT_iTreeResult_ExchangeRateUsed_Number", usdRate),
            Mass(uniqueId, "!_S_PLT_iTreeResult_CO2EquivalentAnnual_Mass", co2Equivalent),
            Mass(uniqueId, "!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass", co2Equivalent / UnitConversions.CarbonToCo2MassRatio),
            Mass(uniqueId, "!_S_PLT_Result_OxygenProducedAnnual_Mass", row.OxygenProducedAnnual ?? co2Equivalent * UnitConversions.OxygenToCo2MassRatio),
            Volume(uniqueId, "!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume", row.RunoffAvoidedAnnual ?? 0d),
            Mass(uniqueId, "!_S_PLT_Result_PollutantsRemovedAnnual_Mass", row.PollutionMassRemovedAnnual ?? 0d),
            Number(uniqueId, "!_S_PLT_iTreeResult_CostSavedAnnual_Currency", WwpLdsSheet.CostSavedIn(sheetCurrencies, row) ?? 0d)
        };

        // What only i-Tree gives (each pollutant, rainfall, the money split, lifetime totals): zero,
        // so nothing from an earlier calculation lingers beside the estimate.
        foreach (var parameter in ITreeOnlyMassParameters)
        {
            items.Add(Mass(uniqueId, parameter, 0d));
        }

        foreach (var parameter in ITreeOnlyNumberParameters)
        {
            items.Add(Number(uniqueId, parameter, 0d));
        }

        foreach (var parameter in ITreeOnlyVolumeParameters)
        {
            items.Add(Volume(uniqueId, parameter, 0d));
        }

        return items;
    }

    /// <summary>The tree's pollen from its sheet row (kg a year), which i-Tree doesn't model.</summary>
    public static InstanceParameterWriteItem PollenWrite(string uniqueId, WwpLdsCoefficientRecord row) =>
        Mass(uniqueId, PollenParameter, row.PollenRate ?? 0d);

    /// <summary>Plans the fill for every scanned tree: pollen for each matched tree, an estimate for each one i-Tree hasn't calculated.</summary>
    public static (IReadOnlyList<InstanceParameterWriteItem> Writes, WwpSheetFillSummary Summary) Plan(
        IReadOnlyList<PlantingInstanceValidationItem> trees,
        IReadOnlyList<WwpLdsCoefficientRecord> rows,
        string unitSystem,
        string currency,
        CurrencyFactors sheetCurrencies,
        double usdRate)
    {
        var writes = new List<InstanceParameterWriteItem>();
        var unmatched = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var estimated = 0;
        var pollen = 0;
        foreach (var tree in trees)
        {
            var (row, how) = FindRow(tree.SpeciesCode, tree.FamilyName, tree.TypeName, rows);
            if (row is null)
            {
                unmatched.Add($"{tree.FamilyName} : {tree.TypeName}");
                continue;
            }

            writes.Add(PollenWrite(tree.UniqueId, row));
            pollen++;
            if (NeedsEstimate(tree))
            {
                writes.AddRange(BuildEstimateWrites(tree.UniqueId, row, how, Reason(tree), unitSystem, currency, sheetCurrencies, usdRate));
                estimated++;
            }
        }

        return (writes, new WwpSheetFillSummary(trees.Count, estimated, pollen, unmatched.ToList()));
    }

    /// <summary>Scans every tree, plans the fill and writes it to Revit.</summary>
    public static async Task<WwpSheetFillSummary> RunAsync(
        RevitPipeClient client,
        IReadOnlyList<WwpLdsCoefficientRecord> rows,
        string unitSystem,
        string currency,
        CurrencyFactors sheetCurrencies,
        double usdRate)
    {
        if (!rows.Any(IsTreeRow))
        {
            return new WwpSheetFillSummary(0, 0, 0, [], "WWP sheet: no tree rows cached — sync the landscape data sheet first.");
        }

        var scan = await client.SendAsync<ValidatePlantingInstancesResult>(PipeCommands.ValidatePlantingInstances, new ValidatePlantingInstancesRequest());
        var (writes, summary) = Plan(scan.Items, rows, unitSystem, currency, sheetCurrencies, usdRate);
        // One transaction per batch keeps a large model's write responsive.
        foreach (var batch in writes.Chunk(20000))
        {
            await client.SendAsync<InstanceParameterWriteResult>(PipeCommands.ApplyInstanceParameterWrites, new InstanceParameterWriteBatch(batch));
        }

        return summary;
    }

    private static string Describe(WwpLdsCoefficientRecord row, SheetRowMatch how) => how switch
    {
        SheetRowMatch.SpeciesCode => $"{row.TypeName}, matched by species code",
        SheetRowMatch.Generic => "its Generic tree row, as no row matched this tree",
        _ => $"{row.TypeName}, matched by name"
    };

    private static readonly string[] ITreeOnlyMassParameters =
    [
        "!_S_PLT_iTreeResult_CORemovedAnnual_Mass", "!_S_PLT_iTreeResult_NO2RemovedAnnual_Mass",
        "!_S_PLT_iTreeResult_O3RemovedAnnual_Mass", "!_S_PLT_iTreeResult_PM25RemovedAnnual_Mass",
        "!_S_PLT_iTreeResult_SO2RemovedAnnual_Mass",
        "!_S_PLT_iTreeResult_CarbonSequesteredLifetimeTotal_Mass", "!_S_PLT_iTreeResult_CO2EquivalentLifetimeTotal_Mass",
        "!_S_PLT_iTreeResult_CORemovedLifetimeTotal_Mass", "!_S_PLT_iTreeResult_NO2RemovedLifetimeTotal_Mass",
        "!_S_PLT_iTreeResult_O3RemovedLifetimeTotal_Mass", "!_S_PLT_iTreeResult_PM25RemovedLifetimeTotal_Mass",
        "!_S_PLT_iTreeResult_SO2RemovedLifetimeTotal_Mass", "!_S_PLT_Result_OxygenProducedLifetimeTotal_Mass"
    ];

    private static readonly string[] ITreeOnlyNumberParameters =
    [
        "!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Currency", "!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Currency",
        "!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Currency",
        "!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Currency", "!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Currency",
        "!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Currency", "!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Currency"
    ];

    private static readonly string[] ITreeOnlyVolumeParameters =
    [
        "!_S_PLT_iTreeResult_RainfallInterceptedAnnual_Volume", "!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume",
        "!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume"
    ];

    private static InstanceParameterWriteItem Text(string uniqueId, string parameter, string value) =>
        new(uniqueId, parameter, "Text", value);

    private static InstanceParameterWriteItem Number(string uniqueId, string parameter, double value) =>
        new(uniqueId, parameter, "Number (no conversion)", value.ToString("G17", CultureInfo.InvariantCulture));

    private static InstanceParameterWriteItem Mass(string uniqueId, string parameter, double kilograms) =>
        new(uniqueId, parameter, "Auto (Revit spec)", kilograms.ToString("G17", CultureInfo.InvariantCulture));

    private static InstanceParameterWriteItem Volume(string uniqueId, string parameter, double cubicMetres) =>
        new(uniqueId, parameter, "Auto (Revit spec)", cubicMetres.ToString("G17", CultureInfo.InvariantCulture));

    private static string LocalTimestamp()
    {
        var now = DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;
        return $"{now.ToString("yyyy-MM-dd, HH:mm:ss", CultureInfo.InvariantCulture)}, {(zone.IsDaylightSavingTime(now) ? zone.DaylightName : zone.StandardName)}";
    }
}
