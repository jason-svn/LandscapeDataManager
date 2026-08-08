using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>One tree's stored benefit values converted onto a single unit-system/currency basis — pure output of <see cref="DashboardAggregationService.NormalizeTree"/>, never itself read from Revit.</summary>
public sealed record NormalizedTreeMetrics(
    DashboardTreeItem Source,
    bool CountsTowardTotals,
    double CO2SequesteredAnnual,
    double CO2SequesteredLifetimeTotal,
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
    double RunoffAvoidedLifetimeTotal);

public sealed record SpeciesSubtotal(
    string SpeciesCode,
    string CommonName,
    int TreeCount,
    double CO2SequesteredAnnual,
    double CO2SequesteredLifetimeTotal,
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
    double CostSavedLifetimeTotal);

/// <summary><see cref="CostSavedAnnual"/> is reported as-calculated, not currency-normalized — Floor Calculator never records which currency (or exchange rate) it used, so there's nothing reliable to convert from.</summary>
public sealed record FloorTypeSubtotal(
    string LdsType,
    int FloorCount,
    double AreaSquareMeters,
    double CO2SequesteredAnnual,
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
    double CO2SequesteredAnnual,
    double CO2SequesteredLifetimeTotal,
    double TreeCostSavedAnnual,
    double TreeCostSavedLifetimeTotal,
    int FloorCount,
    double FloorAreaSquareMeters,
    double FloorCO2SequesteredAnnual,
    double FloorRunoffAvoidedAnnual,
    double FloorPollutionMassRemovedAnnual,
    double FloorTotalGwp,
    double FloorCostSavedAnnual);

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
    /// separate mass bases are still needed, not one: <c>CO2Sequestered</c> displays on a
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
            PoundBasis(item.CO2SequesteredAnnual), PoundBasis(item.CO2SequesteredLifetimeTotal),
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
            item.RunoffAvoidedAnnual, item.RunoffAvoidedLifetimeTotal);
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
                group.Sum(tree => tree.CO2SequesteredAnnual), group.Sum(tree => tree.CO2SequesteredLifetimeTotal),
                group.Sum(tree => tree.CORemovedAnnual), group.Sum(tree => tree.CORemovedLifetimeTotal),
                group.Sum(tree => tree.NO2RemovedAnnual), group.Sum(tree => tree.NO2RemovedLifetimeTotal),
                group.Sum(tree => tree.O3RemovedAnnual), group.Sum(tree => tree.O3RemovedLifetimeTotal),
                group.Sum(tree => tree.PM25RemovedAnnual), group.Sum(tree => tree.PM25RemovedLifetimeTotal),
                group.Sum(tree => tree.SO2RemovedAnnual), group.Sum(tree => tree.SO2RemovedLifetimeTotal),
                group.Sum(tree => tree.CostSavedAnnual), group.Sum(tree => tree.CostSavedLifetimeTotal)))
            .OrderByDescending(subtotal => subtotal.TreeCount)
            .ToList();

    public static IReadOnlyList<FloorTypeSubtotal> AggregateFloorsByType(IEnumerable<DashboardFloorItem> floors) =>
        floors
            .GroupBy(floor => floor.LdsType ?? "Unassigned")
            .Select(group => new FloorTypeSubtotal(
                group.Key,
                group.Count(),
                group.Sum(floor => floor.AreaSquareMeters),
                group.Sum(floor => floor.CO2SequesteredAnnual),
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
            SumTrees(tree => tree.CO2SequesteredAnnual), SumTrees(tree => tree.CO2SequesteredLifetimeTotal),
            SumTrees(tree => tree.CostSavedAnnual), SumTrees(tree => tree.CostSavedLifetimeTotal),
            floors.Count,
            floors.Sum(floor => floor.AreaSquareMeters),
            floors.Sum(floor => floor.CO2SequesteredAnnual),
            floors.Sum(floor => floor.RunoffAvoidedAnnual),
            floors.Sum(floor => floor.PollutionMassRemovedAnnual),
            floors.Sum(floor => floor.TotalGwp),
            floors.Sum(floor => floor.CostSavedAnnual));
    }

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
