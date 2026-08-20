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
    double CO2EquivalentLifetimeTotal);

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
    double CO2EquivalentLifetimeTotal);

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
    double TotalGwp);

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
    double FloorCostSavedAnnual);

/// <summary>
/// The Site &amp; Biodiversity KPI tab's headline numbers. Unlike <see cref="DashboardGrandTotal"/>,
/// none of these depend on a tree's i-Tree calculation <c>Status</c> — canopy area, species identity,
/// native status, bloom months, and ecological function tags are all geometry/reference data available
/// the moment a Planting instance and its type are placed, whether or not the i-Tree API has run yet.
/// A percent field is null (rather than 0) whenever its denominator isn't available — e.g. Canopy Cover
/// and Softscape Surface Ratio need <c>!_S_PLT_Site_TotalArea_Area</c> entered on Project Information
/// first — so the UI can show "not entered yet" instead of a misleading 0%.
/// </summary>
public sealed record SiteKpiSummary(
    double? CanopyCoverPercent,
    double CanopyAreaSquareMeters,
    double? SoftscapeSurfaceRatioPercent,
    double PerviousAreaSquareMeters,
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
    double? LightingCompliancePercent);

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

        return new NormalizedTreeMetrics(
            item,
            string.Equals(item.Status, "Calculated", StringComparison.OrdinalIgnoreCase),
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
            PoundBasis(item.CO2EquivalentAnnual), PoundBasis(item.CO2EquivalentLifetimeTotal));
    }

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
                group.Sum(tree => tree.CO2EquivalentAnnual), group.Sum(tree => tree.CO2EquivalentLifetimeTotal)))
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
                group.Sum(floor => floor.TotalGwp)))
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
            SumTrees(tree => tree.PM25RemovedAnnual + tree.NO2RemovedAnnual + tree.O3RemovedAnnual + tree.SO2RemovedAnnual + tree.CORemovedAnnual),
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
            floors.Sum(floor => floor.TotalGwp),
            floors.Sum(floor => floor.CostSavedAnnual));
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
        var canopyAreaSquareMeters = trees.Sum(tree => tree.Source.CanopyAreaSquareMeters);
        var canopyCoverPercent = siteTotalAreaSquareMeters is > 0
            ? canopyAreaSquareMeters / siteTotalAreaSquareMeters.Value * 100d
            : (double?)null;

        var perviousAreaSquareMeters = floors
            .Where(floor => string.Equals(ResolveSurfaceClass(floor), "Pervious", StringComparison.OrdinalIgnoreCase))
            .Sum(floor => floor.AreaSquareMeters);
        var softscapeSurfaceRatioPercent = siteTotalAreaSquareMeters is > 0
            ? perviousAreaSquareMeters / siteTotalAreaSquareMeters.Value * 100d
            : (double?)null;

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

        return new SiteKpiSummary(
            canopyCoverPercent,
            canopyAreaSquareMeters,
            softscapeSurfaceRatioPercent,
            perviousAreaSquareMeters,
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
            lightingCompliancePercent);
    }

    /// <summary>The explicit <c>!_S_PLT_LDS_SurfaceClass_Text</c> tag always wins; otherwise inferred from the floor's LDS Type (see <see cref="SurfaceClassCatalog"/>).</summary>
    private static string? ResolveSurfaceClass(DashboardFloorItem floor) =>
        !string.IsNullOrWhiteSpace(floor.SurfaceClass) ? floor.SurfaceClass : SurfaceClassCatalog.Infer(floor.LdsType);

    private static IReadOnlyList<int> ParseMonthList(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token => int.TryParse(token, out var month) ? month : (int?)null)
                .Where(month => month is >= 1 and <= 12)
                .Select(month => month!.Value)
                .ToList();

    private static IReadOnlyList<string> ParseTagList(string? raw) =>
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
