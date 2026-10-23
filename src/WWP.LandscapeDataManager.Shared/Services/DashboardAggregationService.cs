using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>One tree's stored benefit values converted onto a single unit-system/currency basis — pure output of <see cref="DashboardAggregationService.NormalizeTree"/>, never itself read from Revit.</summary>
public sealed record NormalizedTreeMetrics(
    DashboardTreeItem Source,
    bool CountsTowardTotals,
    double CarbonSequesteredAnnual,
    double CarbonSequesteredLifetimeTotal,
    double CORemovedAnnual,
    double CORemovedLifetimeTotal,
    double NO2RemovedAnnual,
    double NO2RemovedLifetimeTotal,
    double O3RemovedAnnual,
    double O3RemovedLifetimeTotal,
    double PM25RemovedAnnual,
    double PM25RemovedLifetimeTotal,
    double SO2RemovedAnnual,
    double SO2RemovedLifetimeTotal,
    double CostSavedAnnual,
    double CostSavedLifetimeTotal,
    double CarbonCostSavedAnnual,
    double CarbonCostSavedLifetimeTotal,
    double StormWaterCostSavedAnnual,
    double StormWaterCostSavedLifetimeTotal,
    double AirPollutionCostSavedAnnual,
    double AirPollutionCostSavedLifetimeTotal,
    double RainfallInterceptedAnnual,
    double RainfallInterceptedLifetimeTotal,
    double RunoffAvoidedAnnual,
    double RunoffAvoidedLifetimeTotal,
    double CO2EquivalentAnnual,
    double CO2EquivalentLifetimeTotal,
    double OxygenProducedAnnual = 0,
    double OxygenProducedLifetimeTotal = 0,
    double MaintenanceCostAnnual = 0,
    double SheetPollutantsAnnual = 0)
{
    /// <summary>Benefits estimated from the WWP landscape data sheet because i-Tree couldn't calculate this tree.</summary>
    public bool IsSheetEstimate => string.Equals(Source.Status, WwpSheetTreeEstimates.EstimatedStatus, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pollutants removed a year: i-Tree's five, or a sheet estimate's single total.</summary>
    public double PollutionRemovedAnnual => PM25RemovedAnnual + NO2RemovedAnnual + O3RemovedAnnual + SO2RemovedAnnual + CORemovedAnnual + SheetPollutantsAnnual;
}

public sealed record SpeciesSubtotal(
    string SpeciesCode,
    string CommonName,
    int TreeCount,
    double CarbonSequesteredAnnual,
    double CarbonSequesteredLifetimeTotal,
    double CORemovedAnnual,
    double CORemovedLifetimeTotal,
    double NO2RemovedAnnual,
    double NO2RemovedLifetimeTotal,
    double O3RemovedAnnual,
    double O3RemovedLifetimeTotal,
    double PM25RemovedAnnual,
    double PM25RemovedLifetimeTotal,
    double SO2RemovedAnnual,
    double SO2RemovedLifetimeTotal,
    double CostSavedAnnual,
    double CostSavedLifetimeTotal,
    double CarbonCostSavedAnnual,
    double CarbonCostSavedLifetimeTotal,
    double StormWaterCostSavedAnnual,
    double StormWaterCostSavedLifetimeTotal,
    double AirPollutionCostSavedAnnual,
    double AirPollutionCostSavedLifetimeTotal,
    double RainfallInterceptedAnnual,
    double RainfallInterceptedLifetimeTotal,
    double RunoffAvoidedAnnual,
    double RunoffAvoidedLifetimeTotal,
    double CO2EquivalentAnnual,
    double CO2EquivalentLifetimeTotal,
    double OxygenProducedAnnual = 0,
    double OxygenProducedLifetimeTotal = 0,
    double MaintenanceCostAnnual = 0,
    double SheetPollutantsAnnual = 0)
{
    public double PollutionRemovedAnnual => PM25RemovedAnnual + NO2RemovedAnnual + O3RemovedAnnual + SO2RemovedAnnual + CORemovedAnnual + SheetPollutantsAnnual;

    public double PollutionRemovedLifetimeTotal => PM25RemovedLifetimeTotal + NO2RemovedLifetimeTotal + O3RemovedLifetimeTotal + SO2RemovedLifetimeTotal + CORemovedLifetimeTotal;
}

/// <summary><see cref="CostSavedAnnual"/> is reported as-calculated, not currency-normalized — Floor Calculator never records which currency (or exchange rate) it used, so there's nothing reliable to convert from.</summary>
public sealed record FloorTypeSubtotal(
    string LdsType,
    int FloorCount,
    double AreaSquareMeters,
    double CarbonSequesteredAnnual,
    double RunoffAvoidedAnnual,
    double PollutionMassRemovedAnnual,
    double CostSavedAnnual,
    double OxygenProducedAnnual,
    double TotalGwp,
    double MaintenanceCostAnnual = 0);

/// <summary>
/// The dashboard's headline numbers. Tree and floor benefits are kept in separate fields throughout,
/// never summed into one figure — floor cost has no currency provenance, so blending it into the
/// currency-normalized tree totals would misrepresent both.
/// </summary>
public sealed record DashboardGrandTotal(
    int TreeCount,
    int TreesExcludedFromTotals,
    double PM25RemovedAnnual,
    double PM25RemovedLifetimeTotal,
    double NO2RemovedAnnual,
    double NO2RemovedLifetimeTotal,
    double O3RemovedAnnual,
    double O3RemovedLifetimeTotal,
    double SO2RemovedAnnual,
    double SO2RemovedLifetimeTotal,
    double CORemovedAnnual,
    double CORemovedLifetimeTotal,
    double TotalPollutionMassRemovedAnnual,
    double TotalPollutionMassRemovedLifetimeTotal,
    double CarbonSequesteredAnnual,
    double CarbonSequesteredLifetimeTotal,
    double CO2EquivalentAnnual,
    double CO2EquivalentLifetimeTotal,
    double TreeCostSavedAnnual,
    double TreeCostSavedLifetimeTotal,
    double CarbonCostSavedAnnual,
    double CarbonCostSavedLifetimeTotal,
    double StormWaterCostSavedAnnual,
    double StormWaterCostSavedLifetimeTotal,
    double AirPollutionCostSavedAnnual,
    double AirPollutionCostSavedLifetimeTotal,
    double RainfallInterceptedAnnual,
    double RainfallInterceptedLifetimeTotal,
    double RunoffAvoidedAnnual,
    double RunoffAvoidedLifetimeTotal,
    int FloorCount,
    double FloorAreaSquareMeters,
    double FloorCarbonSequesteredAnnual,
    double FloorRunoffAvoidedAnnual,
    double FloorPollutionMassRemovedAnnual,
    double FloorTotalGwp,
    double FloorCostSavedAnnual,
    double OxygenProducedAnnual = 0,
    double OxygenProducedLifetimeTotal = 0,
    double TreeMaintenanceCostAnnual = 0,
    double FloorOxygenProducedAnnual = 0,
    double FloorMaintenanceCostAnnual = 0)
{
    /// <summary>Trees' and planting areas' yearly upkeep, in the project currency.</summary>
    public double MaintenanceCostAnnual => TreeMaintenanceCostAnnual + FloorMaintenanceCostAnnual;
}

/// <summary>
/// The Site &amp; Biodiversity KPI tab's headline numbers. Unlike <see cref="DashboardGrandTotal"/>,
/// none of these depend on a tree's i-Tree calculation <c>Status</c> — canopy area, species identity,
/// native status, bloom months, and ecological function tags are all geometry/reference data available
/// the moment a Planting instance and its type are placed, whether or not the i-Tree API has run yet.
/// A percent field is null (rather than 0) whenever its denominator isn't available — e.g. Canopy Cover
/// and Softscape Surface Ratio need <c>!_S_PLT_Site_TotalArea_Area</c> entered on Project Information
/// first — so the UI can show "not entered yet" instead of a misleading 0%.
/// <see cref="CanopyAreaSquareMeters"/> is the union of the canopies (overlaps counted once, see
/// <see cref="CanopyUnion"/>); <see cref="CanopySummedAreaSquareMeters"/> adds them up as if none
/// overlapped. <see cref="GreenspaceRatioPercent"/> = pervious ÷ (pervious + impervious) floor area:
/// unclassified floors and building slabs are counted separately and left out of both.
/// <see cref="ConnectivityBandCounts"/> holds the trees and floors in bands 1–4, then those not assessed.
/// </summary>
public sealed record SiteKpiSummary(
    double? CanopyCoverPercent,
    double CanopyAreaSquareMeters,
    double? SoftscapeSurfaceRatioPercent,
    double PerviousAreaSquareMeters,
    double CanopySummedAreaSquareMeters,
    double? GreenspaceRatioPercent,
    double ImperviousAreaSquareMeters,
    int UnclassifiedFloorCount,
    double UnclassifiedAreaSquareMeters,
    int BuildingSlabCount,
    double BuildingSlabAreaSquareMeters,
    IReadOnlyList<int> ConnectivityBandCounts,
    int DistinctSpeciesCount,
    int NativeTreeCount,
    int AdaptiveTreeCount,
    int TreesWithNativeStatusCount,
    double? NativeSpeciesRatioPercent,
    int DistinctBloomMonthsCount,
    int DistinctEcologicalFunctionsCount,
    double MultiFunctionAreaSquareMeters,
    double? HabitatConnectivityScore,
    int LightingFixtureCount,
    int DarkSkyCompliantFixtureCount,
    double? LightingCompliancePercent,
    double TreePollenAnnual = 0,
    double FloorPollenAnnual = 0,
    int AllergenicTreeCount = 0,
    int TreesWithIrrigationFactorCount = 0,
    double? MeanIrrigationDemandFactor = null,
    int LowWaterTreeCount = 0);

public sealed record BngGroupSubtotal(string Name, int FloorCount, double AreaHectares, double HabitatUnits);

/// <summary>A floor's place in the BNG totals (see <see cref="DashboardAggregationService.ClassifyBngFloor"/>).</summary>
public static class BngFloorStates
{
    public const string Current = "Current";
    public const string CheckData = "Check data";
    public const string Stale = "Stale";
    public const string NotAssessed = "Not assessed";
    public const string Excluded = "Excluded";
}

/// <summary>
/// The Statutory Biodiversity Metric's on-site headline for the floors in scope, from the BNG
/// results Floor Calculator wrote. Baseline = A-1 total units of Existing-phase floors; post-
/// intervention = retained (A-1 U) + enhanced (A-3) + created (A-2) units — the Headline Results
/// sheet's own sums. Only current floors count (a written Calculated/CheckData result whose inputs,
/// area and phase role still match its stored signature), like <see cref="DashboardGrandTotal"/>
/// only totals Calculated trees; <see cref="IsComplete"/> is false while any baseline or new floor
/// is stale or unassessed, since the % change then isn't the whole site's.
/// </summary>
public sealed record BngSummary(
    int FloorCount,
    int CurrentFloorCount,
    int CheckDataFloorCount,
    int StaleFloorCount,
    int NotAssessedFloorCount,
    int ExcludedFloorCount,
    int RetainedFloorCount,
    int EnhancedFloorCount,
    int LostFloorCount,
    int CreatedFloorCount,
    double TotalAreaHectares,
    double AssessedAreaHectares,
    double BaselineUnits,
    double RetainedUnits,
    double EnhancedUnits,
    double CreatedUnits,
    double LostUnits,
    string? MetricVersion,
    IReadOnlyList<BngGroupSubtotal> ByBroadHabitat,
    IReadOnlyList<BngGroupSubtotal> ByDistinctiveness)
{
    public double PostInterventionUnits => RetainedUnits + EnhancedUnits + CreatedUnits;

    public double NetChangeUnits => PostInterventionUnits - BaselineUnits;

    /// <summary>Null without a baseline to compare against (no Existing-phase floors assessed).</summary>
    public double? NetChangePercent => BaselineUnits > 0 ? NetChangeUnits / BaselineUnits * 100d : null;

    /// <summary>The statutory 10% biodiversity net gain requirement, on-site habitat units only.</summary>
    public const double StatutoryNetGainPercent = 10d;

    public bool? MeetsStatutoryNetGain => NetChangePercent is { } percent ? percent >= StatutoryNetGainPercent : null;

    public bool IsComplete => StaleFloorCount == 0 && NotAssessedFloorCount == 0;
}

/// <summary>
/// Normalizes stored per-instance i-Tree results onto one unit-system/currency basis and sums across
/// instances — pure/testable, no Revit or IO dependency, mirroring <see cref="PlantingInstanceStatusEvaluator"/>
/// and <see cref="ITreeInstanceResultMapper"/>. This is the one place in the codebase that sums across
/// instances, so it is also the one place that has to account for results calculated under a different
/// unit system or currency than the project's current preference (see <see cref="NormalizeTree"/>).
/// </summary>
public static class DashboardAggregationService
{
    /// <summary>
    /// The status the Dashboard reports for a tree, from the inputs the report carries: a stored
    /// Calculated result whose i-Tree inputs have changed since (or gone missing) is Stale, as in
    /// Tree Calculator; a WWP-sheet estimate stays Estimated; anything else is what Tree Calculator
    /// would show now. A report without inputs (an older connector or cached snapshot) keeps its stored status.
    /// </summary>
    public static string CurrentStatus(DashboardTreeItem tree)
    {
        if (tree.Inputs is null || string.Equals(tree.Status, WwpSheetTreeEstimates.EstimatedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return tree.Status;
        }

        var current = PlantingInstanceStatusEvaluator.Evaluate(tree.Inputs, ITreeBatchCalculator.SignatureEngineVersion).Status;
        return string.Equals(tree.Status, "Calculated", StringComparison.OrdinalIgnoreCase) && current != "Calculated"
            ? "Stale"
            : current;
    }

    /// <summary>
    /// Converts one tree's stored values onto <paramref name="targetUnitSystem"/>/<paramref name="targetCurrency"/>.
    /// Mass fields arrive already in canonical kilograms — <c>DashboardReportService</c> reads them
    /// via <c>UnitUtils.ConvertFromInternalUnits</c> now that they're Revit-native Mass-spec parameters,
    /// so (unlike currency) there's no "what basis was this calculated under" history to resolve, just
    /// a display-basis conversion to whatever the dashboard's own unit toggle currently wants. Two
    /// separate mass bases are still needed, not one: <c>CarbonSequestered</c> displays on a
    /// pounds-per-kilogram basis while the five pollutant-removed fields display on an
    /// ounces-per-kilogram basis (see <c>ITreeInstanceResultMapper.FromPounds</c>/<c>FromOunces</c>) —
    /// collapsing them into one factor would silently misconvert one or the other.
    /// </summary>
    public static NormalizedTreeMetrics NormalizeTree(
        DashboardTreeItem item,
        string targetUnitSystem,
        string targetCurrency,
        double usdToTargetRate)
    {
        var isMetric = string.Equals(targetUnitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
        double PoundBasis(double kilograms) => isMetric ? kilograms : kilograms / UnitConversions.KilogramsPerPound;
        double OunceBasis(double kilograms) => isMetric ? kilograms : kilograms / UnitConversions.KilogramsPerOunce;
        var currencyFactor = ResolveCurrencyFactor(item.StoredCurrency, targetCurrency, item.StoredExchangeRateUsed, usdToTargetRate);
        double Money(double value) => value * currencyFactor;

        var isEstimate = string.Equals(item.Status, WwpSheetTreeEstimates.EstimatedStatus, StringComparison.OrdinalIgnoreCase);
        return new NormalizedTreeMetrics(
            item,
            // An i-Tree result — current, or Stale (its inputs changed since; still i-Tree's numbers,
            // flagged on the Dashboard until it's recalculated) — or a WWP-sheet estimate.
            isEstimate || string.Equals(item.Status, "Calculated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Status, "Stale", StringComparison.OrdinalIgnoreCase),
            PoundBasis(item.CarbonSequesteredAnnual), PoundBasis(item.CarbonSequesteredLifetimeTotal),
            OunceBasis(item.CORemovedAnnual), OunceBasis(item.CORemovedLifetimeTotal),
            OunceBasis(item.NO2RemovedAnnual), OunceBasis(item.NO2RemovedLifetimeTotal),
            OunceBasis(item.O3RemovedAnnual), OunceBasis(item.O3RemovedLifetimeTotal),
            OunceBasis(item.PM25RemovedAnnual), OunceBasis(item.PM25RemovedLifetimeTotal),
            OunceBasis(item.SO2RemovedAnnual), OunceBasis(item.SO2RemovedLifetimeTotal),
            Money(item.CostSavedAnnual), Money(item.CostSavedLifetimeTotal),
            Money(item.CarbonCostSavedAnnual), Money(item.CarbonCostSavedLifetimeTotal),
            Money(item.StormWaterCostSavedAnnual), Money(item.StormWaterCostSavedLifetimeTotal),
            Money(item.AirPollutionCostSavedAnnual), Money(item.AirPollutionCostSavedLifetimeTotal),
            // Volumes are always in cubic metres already (Revit's own unit engine handles Metric/
            // Imperial display, see FloorLdsCalculationService/DashboardReportService) — no manual
            // conversion needed here, unlike the mass and currency fields above.
            item.RainfallInterceptedAnnual, item.RainfallInterceptedLifetimeTotal,
            item.RunoffAvoidedAnnual, item.RunoffAvoidedLifetimeTotal,
            PoundBasis(item.CO2EquivalentAnnual), PoundBasis(item.CO2EquivalentLifetimeTotal),
            PoundBasis(OxygenOrDerived(item.OxygenProducedAnnual, item.CO2EquivalentAnnual)),
            PoundBasis(OxygenOrDerived(item.OxygenProducedLifetimeTotal, item.CO2EquivalentLifetimeTotal)),
            // Maintenance comes from the WWP sheet rate, already in the project currency.
            item.MaintenanceCostAnnual,
            // i-Tree gives each pollutant; a sheet estimate gives one total.
            isEstimate ? OunceBasis(item.PollutantsRemovedAnnual) : 0d);
    }

    /// <summary>
    /// Oxygen a tree releases: O₂ = CO₂e × 32/44 (each tonne of CO₂ fixed releases its 32/44 share as
    /// O₂). The stored value wins; a tree calculated before oxygen was stored gets it from its CO₂e.
    /// </summary>
    public static double OxygenOrDerived(double storedOxygen, double co2Equivalent) =>
        storedOxygen > 0 ? storedOxygen : co2Equivalent * UnitConversions.OxygenToCo2MassRatio;

    public static IReadOnlyList<SpeciesSubtotal> AggregateTreesBySpecies(IEnumerable<NormalizedTreeMetrics> trees) =>
        trees
            .Where(tree => tree.CountsTowardTotals)
            .GroupBy(tree => (
                Code: tree.Source.SpeciesCode ?? "Unassigned",
                Name: tree.Source.CommonName ?? tree.Source.SpeciesCode ?? "Unassigned"))
            .Select(group => new SpeciesSubtotal(
                group.Key.Code,
                group.Key.Name,
                group.Count(),
                group.Sum(tree => tree.CarbonSequesteredAnnual), group.Sum(tree => tree.CarbonSequesteredLifetimeTotal),
                group.Sum(tree => tree.CORemovedAnnual), group.Sum(tree => tree.CORemovedLifetimeTotal),
                group.Sum(tree => tree.NO2RemovedAnnual), group.Sum(tree => tree.NO2RemovedLifetimeTotal),
                group.Sum(tree => tree.O3RemovedAnnual), group.Sum(tree => tree.O3RemovedLifetimeTotal),
                group.Sum(tree => tree.PM25RemovedAnnual), group.Sum(tree => tree.PM25RemovedLifetimeTotal),
                group.Sum(tree => tree.SO2RemovedAnnual), group.Sum(tree => tree.SO2RemovedLifetimeTotal),
                group.Sum(tree => tree.CostSavedAnnual), group.Sum(tree => tree.CostSavedLifetimeTotal),
                group.Sum(tree => tree.CarbonCostSavedAnnual), group.Sum(tree => tree.CarbonCostSavedLifetimeTotal),
                group.Sum(tree => tree.StormWaterCostSavedAnnual), group.Sum(tree => tree.StormWaterCostSavedLifetimeTotal),
                group.Sum(tree => tree.AirPollutionCostSavedAnnual), group.Sum(tree => tree.AirPollutionCostSavedLifetimeTotal),
                group.Sum(tree => tree.RainfallInterceptedAnnual), group.Sum(tree => tree.RainfallInterceptedLifetimeTotal),
                group.Sum(tree => tree.RunoffAvoidedAnnual), group.Sum(tree => tree.RunoffAvoidedLifetimeTotal),
                group.Sum(tree => tree.CO2EquivalentAnnual), group.Sum(tree => tree.CO2EquivalentLifetimeTotal),
                group.Sum(tree => tree.OxygenProducedAnnual), group.Sum(tree => tree.OxygenProducedLifetimeTotal),
                group.Sum(tree => tree.MaintenanceCostAnnual),
                group.Sum(tree => tree.SheetPollutantsAnnual)))
            .OrderByDescending(subtotal => subtotal.TreeCount)
            .ToList();

    public static IReadOnlyList<FloorTypeSubtotal> AggregateFloorsByType(IEnumerable<DashboardFloorItem> floors) =>
        floors
            .GroupBy(floor => floor.LdsType ?? "Unassigned")
            .Select(group => new FloorTypeSubtotal(
                group.Key,
                group.Count(),
                group.Sum(floor => floor.AreaSquareMeters),
                group.Sum(floor => floor.CarbonSequesteredAnnual),
                group.Sum(floor => floor.RunoffAvoidedAnnual),
                group.Sum(floor => floor.PollutionMassRemovedAnnual),
                group.Sum(floor => floor.CostSavedAnnual),
                group.Sum(floor => floor.OxygenProducedAnnual),
                group.Sum(floor => Math.Max(0, floor.TotalGwp)),
                group.Sum(floor => floor.MaintenanceCostAnnual)))
            .OrderByDescending(subtotal => subtotal.FloorCount)
            .ToList();

    public static DashboardGrandTotal BuildGrandTotal(
        IReadOnlyList<NormalizedTreeMetrics> trees,
        IReadOnlyList<DashboardFloorItem> floors)
    {
        var counted = trees.Where(tree => tree.CountsTowardTotals).ToList();

        double SumTrees(Func<NormalizedTreeMetrics, double> selector) => counted.Sum(selector);

        return new DashboardGrandTotal(
            counted.Count,
            trees.Count - counted.Count,
            SumTrees(tree => tree.PM25RemovedAnnual), SumTrees(tree => tree.PM25RemovedLifetimeTotal),
            SumTrees(tree => tree.NO2RemovedAnnual), SumTrees(tree => tree.NO2RemovedLifetimeTotal),
            SumTrees(tree => tree.O3RemovedAnnual), SumTrees(tree => tree.O3RemovedLifetimeTotal),
            SumTrees(tree => tree.SO2RemovedAnnual), SumTrees(tree => tree.SO2RemovedLifetimeTotal),
            SumTrees(tree => tree.CORemovedAnnual), SumTrees(tree => tree.CORemovedLifetimeTotal),
            SumTrees(tree => tree.PollutionRemovedAnnual),
            SumTrees(tree => tree.PM25RemovedLifetimeTotal + tree.NO2RemovedLifetimeTotal + tree.O3RemovedLifetimeTotal + tree.SO2RemovedLifetimeTotal + tree.CORemovedLifetimeTotal),
            SumTrees(tree => tree.CarbonSequesteredAnnual), SumTrees(tree => tree.CarbonSequesteredLifetimeTotal),
            SumTrees(tree => tree.CO2EquivalentAnnual), SumTrees(tree => tree.CO2EquivalentLifetimeTotal),
            SumTrees(tree => tree.CostSavedAnnual), SumTrees(tree => tree.CostSavedLifetimeTotal),
            SumTrees(tree => tree.CarbonCostSavedAnnual), SumTrees(tree => tree.CarbonCostSavedLifetimeTotal),
            SumTrees(tree => tree.StormWaterCostSavedAnnual), SumTrees(tree => tree.StormWaterCostSavedLifetimeTotal),
            SumTrees(tree => tree.AirPollutionCostSavedAnnual), SumTrees(tree => tree.AirPollutionCostSavedLifetimeTotal),
            SumTrees(tree => tree.RainfallInterceptedAnnual), SumTrees(tree => tree.RainfallInterceptedLifetimeTotal),
            SumTrees(tree => tree.RunoffAvoidedAnnual), SumTrees(tree => tree.RunoffAvoidedLifetimeTotal),
            floors.Count,
            floors.Sum(floor => floor.AreaSquareMeters),
            floors.Sum(floor => floor.CarbonSequesteredAnnual),
            floors.Sum(floor => floor.RunoffAvoidedAnnual),
            floors.Sum(floor => floor.PollutionMassRemovedAnnual),
            // Embodied carbon only: a negative stored total is sequestration from older calculations.
            floors.Sum(floor => Math.Max(0, floor.TotalGwp)),
            floors.Sum(floor => floor.CostSavedAnnual),
            SumTrees(tree => tree.OxygenProducedAnnual), SumTrees(tree => tree.OxygenProducedLifetimeTotal),
            // Upkeep doesn't depend on i-Tree having run: every placed tree costs it.
            trees.Sum(tree => tree.MaintenanceCostAnnual),
            floors.Sum(floor => floor.OxygenProducedAnnual),
            floors.Sum(floor => floor.MaintenanceCostAnnual));
    }

    /// <summary>
    /// Builds the Site &amp; Biodiversity tab's numbers from the same filtered tree/floor lists every
    /// other tab uses, plus the two Project-Information-level site inputs and the (currently
    /// unfiltered — lighting fixtures aren't Design-Option/Level filtered yet) lighting fixture list.
    /// </summary>
    public static SiteKpiSummary BuildSiteKpiSummary(
        IReadOnlyList<NormalizedTreeMetrics> trees,
        IReadOnlyList<DashboardFloorItem> floors,
        IReadOnlyList<DashboardLightingItem> lighting,
        double? siteTotalAreaSquareMeters,
        double? habitatConnectivityScore)
    {
        var canopyAreaSquareMeters = CanopyAreaSquareMeters(trees.Select(tree => tree.Source));
        var canopySummedAreaSquareMeters = trees.Sum(tree => tree.Source.CanopyAreaSquareMeters);
        var canopyCoverPercent = siteTotalAreaSquareMeters is > 0
            ? canopyAreaSquareMeters / siteTotalAreaSquareMeters.Value * 100d
            : (double?)null;

        var surfaces = floors.Select(floor => (Floor: floor, Class: ClassifySurface(floor))).ToList();
        double AreaOf(string? surfaceClass) => surfaces.Where(surface => surface.Class == surfaceClass).Sum(surface => surface.Floor.AreaSquareMeters);
        var perviousAreaSquareMeters = AreaOf(SurfaceClassCatalog.Pervious);
        var imperviousAreaSquareMeters = AreaOf(SurfaceClassCatalog.Impervious);
        var softscapeSurfaceRatioPercent = siteTotalAreaSquareMeters is > 0
            ? perviousAreaSquareMeters / siteTotalAreaSquareMeters.Value * 100d
            : (double?)null;
        var greenspaceRatioPercent = perviousAreaSquareMeters + imperviousAreaSquareMeters > 0
            ? perviousAreaSquareMeters / (perviousAreaSquareMeters + imperviousAreaSquareMeters) * 100d
            : (double?)null;

        var connectivityBands = new int[5];
        foreach (var band in trees.Select(tree => tree.Source.ConnectivityBand).Concat(floors.Select(floor => floor.ConnectivityBand)))
        {
            connectivityBands[band is >= 1 and <= 4 ? band.Value - 1 : 4]++;
        }

        var distinctSpeciesCount = trees
            .Select(tree => tree.Source.SpeciesCode ?? tree.Source.CommonName ?? "Unassigned")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var nativeTreeCount = trees.Count(tree => string.Equals(tree.Source.NativeStatus, "Native", StringComparison.OrdinalIgnoreCase));
        var adaptiveTreeCount = trees.Count(tree => string.Equals(tree.Source.NativeStatus, "Adaptive", StringComparison.OrdinalIgnoreCase));
        var treesWithNativeStatusCount = trees.Count(tree => !string.IsNullOrWhiteSpace(tree.Source.NativeStatus));
        var nativeSpeciesRatioPercent = treesWithNativeStatusCount > 0
            ? (double)nativeTreeCount / treesWithNativeStatusCount * 100d
            : (double?)null;

        var distinctBloomMonths = new HashSet<int>();
        foreach (var tree in trees)
        {
            foreach (var month in ParseMonthList(tree.Source.BloomMonths))
            {
                distinctBloomMonths.Add(month);
            }
        }

        var distinctEcologicalFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tree in trees)
        {
            foreach (var tag in ParseTagList(tree.Source.EcologicalFunctions))
            {
                distinctEcologicalFunctions.Add(tag);
            }
        }
        var multiFunctionAreaSquareMeters = trees
            .Where(tree => ParseTagList(tree.Source.EcologicalFunctions).Count >= 2)
            .Sum(tree => tree.Source.CanopyAreaSquareMeters);

        var lightingFixtureCount = lighting.Count;
        var darkSkyCompliantFixtureCount = lighting.Count(fixture => string.Equals(fixture.DarkSkyCompliant, "Yes", StringComparison.OrdinalIgnoreCase));
        var lightingCompliancePercent = lightingFixtureCount > 0
            ? (double)darkSkyCompliantFixtureCount / lightingFixtureCount * 100d
            : (double?)null;

        // From the WWP sheet, which i-Tree doesn't model: pollen per tree and per planting area, the
        // species flagged allergenic, and each tree type's irrigation demand (plant factor 0–1).
        var irrigationFactors = trees
            .Select(tree => tree.Source.IrrigationDemandFactor)
            .Where(factor => factor is > 0)
            .Select(factor => factor!.Value)
            .ToList();
        var allergenicTreeCount = trees.Count(tree => IsAllergenic(tree.Source.Allergenic));

        return new SiteKpiSummary(
            canopyCoverPercent,
            canopyAreaSquareMeters,
            softscapeSurfaceRatioPercent,
            perviousAreaSquareMeters,
            canopySummedAreaSquareMeters,
            greenspaceRatioPercent,
            imperviousAreaSquareMeters,
            surfaces.Count(surface => surface.Class is null),
            AreaOf(null),
            surfaces.Count(surface => surface.Class == SurfaceClassCatalog.Building),
            AreaOf(SurfaceClassCatalog.Building),
            connectivityBands,
            distinctSpeciesCount,
            nativeTreeCount,
            adaptiveTreeCount,
            treesWithNativeStatusCount,
            nativeSpeciesRatioPercent,
            distinctBloomMonths.Count,
            distinctEcologicalFunctions.Count,
            multiFunctionAreaSquareMeters,
            habitatConnectivityScore,
            lightingFixtureCount,
            darkSkyCompliantFixtureCount,
            lightingCompliancePercent,
            trees.Sum(tree => tree.Source.PollenAnnual),
            floors.Sum(floor => floor.PollenAnnual),
            allergenicTreeCount,
            irrigationFactors.Count,
            irrigationFactors.Count > 0 ? irrigationFactors.Average() : null,
            irrigationFactors.Count(factor => factor <= LowWaterIrrigationFactor));
    }

    /// <summary>A plant factor at or below this is a low-water planting (WUCOLS "low": 0.1–0.3).</summary>
    public const double LowWaterIrrigationFactor = 0.3;

    /// <summary>The sheet's Allergenic cell: "Yes", or a positive score (it stores 0.2 for Quercus robur).</summary>
    public static bool IsAllergenic(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (string.Equals(value.Trim(), "Yes", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(value.Trim(), "True", StringComparison.OrdinalIgnoreCase) ||
         (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var score) && score > 0));

    /// <summary>
    /// Totals the BNG results stored on <paramref name="floors"/> into the metric's on-site headline
    /// (see <see cref="BngSummary"/>). A floor counts as current when Floor Calculator wrote a
    /// Calculated/CheckData result and the signature of its current inputs, area and phase role
    /// still matches the stored one; a mismatch is Stale (excluded, needs a recalculation), and a
    /// floor never written — or written as NeedsInfo — is Not assessed. Floors demolished in the
    /// phase they were created (Excluded) are counted separately and never affect the totals.
    /// </summary>
    public static BngSummary BuildBngSummary(IReadOnlyList<DashboardFloorItem> floors, BngMetricCatalog catalog)
    {
        var current = new List<(DashboardFloorItem Floor, DashboardFloorBng Bng, string Role)>();
        int checkData = 0, stale = 0, excluded = 0;
        foreach (var floor in floors)
        {
            var (state, role) = ClassifyBngFloor(floor, catalog);
            switch (state)
            {
                case BngFloorStates.Excluded:
                    excluded++;
                    continue;
                case BngFloorStates.NotAssessed:
                    continue;
                case BngFloorStates.Stale:
                    stale++;
                    continue;
                case BngFloorStates.CheckData:
                    checkData++;
                    break;
            }

            current.Add((floor, floor.Bng!, role));
        }

        static double Hectares(DashboardFloorItem floor) => floor.AreaSquareMeters / BngFloorStatusEvaluator.SquareMetresPerHectare;
        int CountRole(string role) => current.Count(pair => pair.Role == role);
        double SumPost(string role) => current.Where(pair => pair.Role == role).Sum(pair => pair.Bng.HabitatUnits);

        // Post-intervention habitat on site: lost floors deliver nothing, so they're left out of the breakdowns.
        var onSite = current.Where(pair => pair.Role != BngPhaseRoles.Lost).ToList();
        IReadOnlyList<BngGroupSubtotal> GroupBy(Func<DashboardFloorBng, string?> key) =>
            onSite
                .GroupBy(pair => string.IsNullOrWhiteSpace(key(pair.Bng)) ? "Unassigned" : key(pair.Bng)!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new BngGroupSubtotal(
                    group.Key,
                    group.Count(),
                    group.Sum(pair => Hectares(pair.Floor)),
                    group.Sum(pair => pair.Bng.HabitatUnits)))
                .OrderByDescending(subtotal => subtotal.HabitatUnits)
                .ToList();

        return new BngSummary(
            floors.Count,
            current.Count,
            checkData,
            stale,
            floors.Count - current.Count - stale - excluded,
            excluded,
            CountRole(BngPhaseRoles.Retained),
            CountRole(BngPhaseRoles.Enhanced),
            CountRole(BngPhaseRoles.Lost),
            CountRole(BngPhaseRoles.Created),
            floors.Where(floor => floor.Bng is not { PhaseRole: BngPhaseRoles.Excluded }).Sum(Hectares),
            current.Sum(pair => Hectares(pair.Floor)),
            current.Where(pair => pair.Role != BngPhaseRoles.Created).Sum(pair => pair.Bng.BaselineUnits),
            SumPost(BngPhaseRoles.Retained),
            SumPost(BngPhaseRoles.Enhanced),
            SumPost(BngPhaseRoles.Created),
            current.Where(pair => pair.Role == BngPhaseRoles.Lost).Sum(pair => pair.Bng.BaselineUnits),
            current.Select(pair => pair.Bng.MetricVersion).FirstOrDefault(version => !string.IsNullOrWhiteSpace(version)),
            GroupBy(bng => bng.BroadHabitat),
            GroupBy(bng => bng.Distinctiveness));
    }

    /// <summary>The same floor input Floor Calculator signed when it wrote the result, rebuilt from what's stored on the floor now.</summary>
    public static BngFloorInput ToFloorInput(DashboardFloorItem floor, DashboardFloorBng bng, BngMetricCatalog catalog) => new(
        bng.PhaseRole,
        bng.Enhanced,
        bng.BaselineHabitat,
        string.IsNullOrWhiteSpace(bng.BaselineCondition) ? null : bng.BaselineCondition.Trim(),
        bng.Irreplaceable,
        bng.ProposedHabitat,
        string.IsNullOrWhiteSpace(bng.Condition) ? null : bng.Condition.Trim(),
        catalog.FindStrategicSignificance(bng.StrategicSignificance)?.Description ?? bng.StrategicSignificance,
        bng.YearOffset,
        floor.AreaSquareMeters / BngFloorStatusEvaluator.SquareMetresPerHectare);

    /// <summary>The floor's explicit surface class tag, else one inferred from its LDS type; null when neither says.</summary>
    /// <summary>
    /// Where a floor stands in <see cref="BuildBngSummary"/> (see <see cref="BngFloorStates"/>), and its
    /// metric role: Excluded when demolished in the phase it was created; Not assessed without a written
    /// Calculated/CheckData result; Stale when its inputs, area or phase role changed since; otherwise
    /// Current or CheckData.
    /// </summary>
    public static (string State, string Role) ClassifyBngFloor(DashboardFloorItem floor, BngMetricCatalog catalog)
    {
        if (floor.Bng is { PhaseRole: BngPhaseRoles.Excluded })
        {
            return (BngFloorStates.Excluded, BngPhaseRoles.Excluded);
        }

        if (floor.Bng is not { } bng ||
            string.IsNullOrEmpty(bng.StoredInputSignature) ||
            !(string.Equals(bng.StoredStatus, nameof(BngCalculationOutcome.Calculated), StringComparison.Ordinal) ||
              string.Equals(bng.StoredStatus, nameof(BngCalculationOutcome.CheckData), StringComparison.Ordinal)))
        {
            return (BngFloorStates.NotAssessed, floor.Bng?.PhaseRole ?? SitePhases.RoleOf(floor) ?? BngPhaseRoles.Created);
        }

        var input = ToFloorInput(floor, bng, catalog);
        if (!string.Equals(BngFloorCalculator.ComputeSignature(input, catalog), bng.StoredInputSignature, StringComparison.Ordinal))
        {
            return (BngFloorStates.Stale, input.Role);
        }

        return (string.Equals(bng.StoredStatus, nameof(BngCalculationOutcome.CheckData), StringComparison.Ordinal) ? BngFloorStates.CheckData : BngFloorStates.Current, input.Role);
    }

    public static string? ResolveSurfaceClass(DashboardFloorItem floor) =>
        SurfaceClassCatalog.Resolve(floor.SurfaceClass, floor.LdsType, floor.TypeName, floor.FamilyName);

    /// <summary>
    /// <see cref="SurfaceClassCatalog.Pervious"/>, <see cref="SurfaceClassCatalog.Impervious"/>,
    /// <see cref="SurfaceClassCatalog.Building"/> for a building slab (left out of the greenspace
    /// ratio), or null when nothing says what the floor is.
    /// </summary>
    public static string? ClassifySurface(DashboardFloorItem floor)
    {
        if (SurfaceClassCatalog.IsBuildingSlab(floor.SurfaceClass, floor.LdsType, floor.IsStructural, floor.TypeName, floor.FamilyName))
        {
            return SurfaceClassCatalog.Building;
        }

        return ResolveSurfaceClass(floor) switch
        {
            { } value when value.Equals(SurfaceClassCatalog.Pervious, StringComparison.OrdinalIgnoreCase) => SurfaceClassCatalog.Pervious,
            { } value when value.Equals(SurfaceClassCatalog.Impervious, StringComparison.OrdinalIgnoreCase) => SurfaceClassCatalog.Impervious,
            _ => null
        };
    }

    /// <summary>
    /// The plan area the trees' canopies cover, overlaps counted once. A tree whose position the
    /// connector didn't send (an older snapshot) adds its own canopy area as it is.
    /// </summary>
    public static double CanopyAreaSquareMeters(IEnumerable<DashboardTreeItem> trees)
    {
        var circles = new List<CanopyCircle>();
        var unplaced = 0d;
        foreach (var tree in trees)
        {
            if (tree is { CanopyCenterX: { } x, CanopyCenterY: { } y, CanopyRadiusMeters: > 0 })
            {
                circles.Add(new CanopyCircle(x, y, tree.CanopyRadiusMeters.Value));
            }
            else
            {
                unplaced += tree.CanopyAreaSquareMeters;
            }
        }

        return CanopyUnion.AreaSquareMeters(circles) + unplaced;
    }

    internal static IReadOnlyList<int> ParseMonthList(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token => int.TryParse(token, out var month) ? month : (int?)null)
                .Where(month => month is >= 1 and <= 12)
                .Select(month => month!.Value)
                .ToList();

    internal static IReadOnlyList<string> ParseTagList(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static double ResolveCurrencyFactor(
        string storedCurrency, string targetCurrency, double storedExchangeRateUsed, double usdToTargetRate)
    {
        if (string.Equals(storedCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }

        // storedAmount = usd * storedExchangeRateUsed, so dividing recovers the USD basis before
        // applying today's fresh USD-to-target rate.
        var safeStoredRate = storedExchangeRateUsed > 0d ? storedExchangeRateUsed : 1d;
        return usdToTargetRate / safeStoredRate;
    }
}
