using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Default Airtable-header → <c>!_S_PLT_*</c> parameter suggestions, layered underneath the
/// per-project saved mappings (<c>ParameterMappingDefinition</c>, which always wins when present).
/// This is the "it should just know" layer: a project that has never been mapped before still gets
/// every standard WWP landscape-data-sheet column pre-wired to its parameter, instead of showing up
/// entirely blank until the user manually maps each one.
///
/// Every header string below was cross-checked against a real WWP landscape data sheet export
/// (originally added when this lived in the now-retired companion app's column mapper).
/// </summary>
public static class DefaultParameterMappingCatalog
{
    /// <summary>
    /// Finds the best unused <paramref name="options"/> match for <paramref name="header"/>, trying
    /// the header's known default candidate(s) first, then the header text itself, both by exact
    /// name and by a loosely-normalized (letters/digits only, case-insensitive) comparison — so
    /// e.g. "WWP Cost Saved" still matches a parameter named "WWP_Cost_Saved". Ties prefer a
    /// Type-scoped parameter over an Instance-scoped one, matching how most mapped columns describe
    /// a planting type rather than one specific instance. Returns null if nothing unused matches.
    /// </summary>
    public static ParameterOption? FindMatch(string header, IReadOnlyList<ParameterOption> options, ISet<string> usedTargetKeys)
    {
        foreach (var candidate in GetCandidates(header))
        {
            var normalizedCandidate = Normalize(candidate);
            var match = options
                .Where(option => !usedTargetKeys.Contains(TargetKey(option)))
                .Where(option =>
                    string.Equals(option.Descriptor.Name, candidate, StringComparison.OrdinalIgnoreCase) ||
                    Normalize(option.Descriptor.Name) == normalizedCandidate)
                .OrderByDescending(option => string.Equals(option.Descriptor.Scope, "Type", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    public static string TargetKey(ParameterOption option) => $"{option.Descriptor.Scope}|{option.Descriptor.Name}";

    /// <summary>Names retired by a rename (new name, new GUID) — still bound in projects that haven't re-run Import Shared Parameter.</summary>
    private static readonly HashSet<string> RetiredParameterNames = new(StringComparer.Ordinal)
    {
        "!_S_PLT_LDS_CarbonDioxideSequestrationAnnual_Mass", "!_S_PLT_LDS_AvoidedWaterRunoffAnnual_Volume",
        "!_S_PLT_LDS_OxygenLevelsAnnual_Mass", "!_S_PLT_LDS_ProductGWP_Number", "!_S_PLT_LDS_TransportGWP_Number",
        "!_S_PLT_LDS_OxygenProducedAnnual_Mass", "!_S_PLT_LDS_PollutantsRemovedAnnual_Mass", "!_S_PLT_LDS_TotalGWP_Mass",
        "!_S_PLT_LDS_ResultSource_Text", "!_S_PLT_LDS_LastCalculated_Text"
    };

    /// <summary>
    /// What each retired import target became, so a mapping saved before the rename keeps working:
    /// the Importer reads it as the current parameter, with the user's own on/off and
    /// conversion choices. Retired names with no importable successor (calculated results) aren't here.
    /// </summary>
    private static readonly Dictionary<string, string> RenamedParameters = new(StringComparer.Ordinal)
    {
        ["!_S_PLT_LDS_CarbonDioxideSequestrationAnnual_Mass"] = "!_S_PLT_LDS_CO2SequestrationRate_Number",
        ["!_S_PLT_LDS_CarbonDioxideSequestrationAnnual_Number"] = "!_S_PLT_LDS_CO2SequestrationRate_Number",
        ["!_S_PLT_LDS_AvoidedWaterRunoffAnnual_Volume"] = "!_S_PLT_LDS_RunoffAvoidedRate_Number",
        ["!_S_PLT_LDS_AvoidedWaterRunoffAnnual_Number"] = "!_S_PLT_LDS_RunoffAvoidedRate_Number",
        ["!_S_PLT_LDS_OxygenLevelsAnnual_Mass"] = "!_S_PLT_LDS_OxygenProductionRate_Number",
        ["!_S_PLT_LDS_OxygenLevelsAnnual_Number"] = "!_S_PLT_LDS_OxygenProductionRate_Number",
        ["!_S_PLT_LDS_ProductGWP_Number"] = "!_S_PLT_LDS_ProductGWPRate_Number",
        ["!_S_PLT_LDS_TransportGWP_Number"] = "!_S_PLT_LDS_TransportGWPRate_Number",
        ["!_S_PLT_LDS_PollutantsRemovedAnnual_Mass"] = "!_S_PLT_LDS_PollutantsRemovedRate_Number",
        ["!_S_PLT_LDS_PollutantsRemovedAnnual_Number"] = "!_S_PLT_LDS_PollutantsRemovedRate_Number",
    };

    /// <summary>The current name for a saved mapping's parameter: its successor if it was renamed, else itself.</summary>
    public static string CurrentName(string parameterName) =>
        RenamedParameters.TryGetValue(parameterName, out var current) ? current : parameterName;

    /// <summary>
    /// Why a sheet column is deliberately left unmapped, shown in place of "Needs mapping" so it
    /// doesn't read as a gap; null for an ordinary column.
    /// </summary>
    public static string? SkipReason(string header) => CleanHeader(header) switch
    {
        "WWP_Total_GWP" => "Not imported — formula column (product + transport GWP)",
        _ => null
    };

    /// <summary>The LIM parameter a known sheet column belongs on, or null when the header has no default.</summary>
    public static string? DefaultParameter(string header) =>
        GetCandidates(header).FirstOrDefault(candidate => candidate.StartsWith("!_S_PLT_", StringComparison.Ordinal));

    /// <summary>
    /// Whether the Importer may write to this parameter. Never a calculated <c>!_S_PLT_Result_*</c>,
    /// <c>!_S_PLT_BNGResult_*</c>, or <c>!_S_PLT_ConnectivityResult_*</c> parameter
    /// (a calculator owns it — importing would overwrite its results with raw sheet values),
    /// and never a retired name, so a mapping saved before a rename can't keep feeding the old parameter.
    /// </summary>
    public static bool IsImportTarget(string parameterName) =>
        !parameterName.StartsWith("!_S_PLT_Result_", StringComparison.Ordinal) &&
        !parameterName.StartsWith("!_S_PLT_BNGResult_", StringComparison.Ordinal) &&
        !parameterName.StartsWith("!_S_PLT_ConnectivityResult_", StringComparison.Ordinal) &&
        !RetiredParameterNames.Contains(parameterName);

    /// <summary>
    /// A header as the sheet means it: trimmed, and without invisible characters — the live WWP
    /// sheet's first column, Origin, arrives with a byte-order mark (U+FEFF) from its CSV import.
    /// </summary>
    public static string CleanHeader(string header) =>
        new string([.. header.Where(character => character is not ('\uFEFF' or '\u200B' or '\u200C' or '\u200D' or '\u2060'))]).Trim();

    public static string Normalize(string value) =>
        new([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>Candidate parameter names to try for a source header, in priority order, always ending with the header itself as a last-ditch exact/normalized match.</summary>
    public static IReadOnlyList<string> GetCandidates(string header) => CleanHeader(header) switch
    {
        "Avoided runoff m3/yr (16/18 girth)" => ["!_S_PLT_LDS_RunoffAvoidedRate_Number", header],
        "Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)" => ["!_S_PLT_LDS_CO2SequestrationRate_Number", header],
        "Oxygen levels O2 kg/yr (16/18 girth)" => ["!_S_PLT_LDS_OxygenProductionRate_Number", header],
        // MaintenanceCost/PollutantsRemoved are Floor Calculator's own coefficient-times-area outputs
        // (see FloorLdsCalculationService) — but the raw WWP sheet columns are still useful direct
        // mapping targets for Types, matching the same "map raw WWP_* column onto its !_S_PLT_
        // parameter" intent as COST_SAVED below.
        "Maintenance Costs" => ["!_S_PLT_LDS_MaintenanceCostAnnual_Currency", header],
        "COST_SAVED" => ["!_S_PLT_iTreeResult_CostSavedAnnual_Currency", header],
        // The three cost-saved sub-categories below are i-Tree Calculator's own outputs
        // (Currency-typed) — the WWP sheet's per-category cost breakdown maps directly onto them.
        // There is no "COST SAVED per year - Oxygen" equivalent: i-Tree doesn't monetize oxygen
        // production as a benefit category (only Carbon/Stormwater/Air Quality are priced), so that
        // one column has no matching parameter at all — see the missing-parameters list.
        "COST SAVED per year - Water run-off (based on UK stormwater treatment costs and infrastructure)" => ["!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Currency", header],
        "COST SAVED per year - Carbon - calculated based on offsets saved (carbon price)" => ["!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Currency", header],
        "COST SAVED per year - Air pollutants based on health care costs, cleaning and ecosystem recovery costs" => ["!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Currency", header],
        // Unlike the three above, Oxygen has no i-Tree-computed sibling at all (i-Tree never
        // monetizes oxygen production) — this is always the WWP sheet's own reference value.
        "COST SAVED per year - Oxygen" => ["!_S_PLT_LDS_OxygenCostSavedAnnual_Currency", header],
        "f_int and Crg ( fraction of rainfall intercepted by the area(dimensionless, per planting type) (runoff coefficient" => ["!_S_PLT_LDS_RunoffCoefficient_Number", header],
        "Allergenic" => ["!_S_PLT_iTreeSpecies_Allergenic_Text", header],
        // A rate like the four above. Never !_S_PLT_Result_PollutantsRemovedAnnual_Mass, Floor
        // Calculator's area-scaled result, which the raw column must not overwrite.
        "WWP_Pollutants_Removed" => ["!_S_PLT_LDS_PollutantsRemovedRate_Number"],
        "Origin" => ["!_S_PLT_LDS_Origin_Text", header],
        "WWP_LDS_Category" => ["!_S_PLT_LDS_Category_Text", header],
        "WWP_LDS_SubCategory" => ["!_S_PLT_LDS_SubCategory_Text", header],
        "Product GWP" => ["!_S_PLT_LDS_ProductGWPRate_Number", header],
        // WWP_Total_GWP is a formula column (product + transport, or −sequestration for planting):
        // nothing to import. It must never land on !_S_PLT_Result_EmbodiedGWP_Mass, Floor Calculator's
        // area-scaled embodied-carbon result.
        "WWP_Total_GWP" => [],
        "Transport GWP" => ["!_S_PLT_LDS_TransportGWPRate_Number", header],
        "Pollen" => ["!_S_PLT_LDS_PollenAnnual_Number", header],
        "Surface_Temperature_Reduction_Min" => ["!_S_PLT_LDS_SurfaceTempReduction_Number", header],
        "Air temperature reduction (1.5m height)" => ["!_S_PLT_LDS_AirTempReduction_Number", header],
        "Irrigation_Demand_Plant Factor" => ["!_S_PLT_LDS_IrrigationDemandFactor_Number", header],
        "Max_Height" => ["!_S_PLT_LDS_MaxHeight_Length", header],
        "Max_Width" => ["!_S_PLT_LDS_MaxWidth_Length", header],
        // The growth rates: filled from i-Tree's forecast by Growth Projections (Growth rates from
        // i-Tree), or by hand.
        GrowthRatesFromITree.HeightSheetColumn => [GrowthRatesFromITree.HeightParameter, header],
        GrowthRatesFromITree.WidthSheetColumn => [GrowthRatesFromITree.WidthParameter, header],
        GrowthRatesFromITree.TrunkSheetColumn => [GrowthRatesFromITree.TrunkParameter, header],
        // Species identity columns — the most common headers on a fresh Airtable base, not present
        // in the original companion-app table because that tool matched them through a separate key
        // schedule step instead.
        "Species_Code" or "Species Code" or "Code" => ["!_S_PLT_iTreeSpecies_Code_Text", header],
        "Common_Name" or "Common Name" => ["!_S_PLT_iTreeSpecies_CommonName_Text", header],
        "Scientific_Name" or "Scientific Name" or "Latin Name" => ["!_S_PLT_iTreeSpecies_ScientificName_Text", header],
        "Species_Type" or "Species Type" or "Type" => ["!_S_PLT_iTreeSpecies_Type_Text", header],
        _ => [header]
    };
}
