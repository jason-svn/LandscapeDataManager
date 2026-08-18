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

    public static string Normalize(string value) =>
        new([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>Candidate parameter names to try for a source header, in priority order, always ending with the header itself as a last-ditch exact/normalized match.</summary>
    public static IReadOnlyList<string> GetCandidates(string header) => header.Trim() switch
    {
        "Avoided runoff m3/yr (16/18 girth)" => ["!_S_PLT_LDS_AvoidedWaterRunoffAnnual_Number", header],
        "Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)" => ["!_S_PLT_LDS_CarbonDioxideSequestrationAnnual_Number", header],
        "Oxygen levels O2 kg/yr (16/18 girth)" => ["!_S_PLT_LDS_OxygenLevelsAnnual_Number", header],
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
        "WWP_Pollutants_Removed" => ["!_S_PLT_LDS_PollutantsRemovedAnnual_Mass", header],
        "Origin" => ["!_S_PLT_LDS_Origin_Text", header],
        "WWP_LDS_Category" => ["!_S_PLT_LDS_Category_Text", header],
        "WWP_LDS_SubCategory" => ["!_S_PLT_LDS_SubCategory_Text", header],
        "Product GWP" => ["!_S_PLT_LDS_ProductGWP_Number", header],
        "Transport GWP" => ["!_S_PLT_LDS_TransportGWP_Number", header],
        "Pollen" => ["!_S_PLT_LDS_PollenAnnual_Number", header],
        "Surface_Temperature_Reduction_Min" => ["!_S_PLT_LDS_SurfaceTempReduction_Number", header],
        "Air temperature reduction (1.5m height)" => ["!_S_PLT_LDS_AirTempReduction_Number", header],
        "Irrigation_Demand_Plant Factor" => ["!_S_PLT_LDS_IrrigationDemandFactor_Number", header],
        "Max_Height" => ["!_S_PLT_LDS_MaxHeight_Number", header],
        "Max_Width" => ["!_S_PLT_LDS_MaxWidth_Number", header],
        "Height year annual growth rate (m/yr)" => ["!_S_PLT_GrowthRatio_HeightbyYear_Number", header],
        "Width annual growth rate (m/yr)" => ["!_S_PLT_GrowthRatio_WidthbyYear_Number", header],
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
