using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// One design scenario's headline performance, side by side with the others in the scenario
/// overview. Outlook figures are totals over <see cref="Years"/> (trees via <see cref="TreeOutlook"/>,
/// planting areas at their constant WWP-sheet rate × years); masses, volumes and money are in the
/// dashboard's display units and project currency.
/// </summary>
public sealed record ScenarioPerformance(
    DesignScenario Scenario,
    string Description,
    int Years,
    TreeOutlookMethod Method,
    int TreeCount,
    int CalculatedTreeCount,
    int SpeciesCount,
    int PlantingAreaCount,
    double CanopyAreaSquareMeters,
    double? CanopyCoverPercent,
    double? GreenspaceRatioPercent,
    double Co2EquivalentOutlook,
    double RunoffAvoidedOutlook,
    double PollutionRemovedOutlook,
    double OxygenProducedOutlook,
    double EcosystemValueOutlook,
    double MaintenanceCostOutlook,
    double EmbodiedCarbon,
    double? BngPostInterventionUnits,
    double? BngNetChangeUnits,
    double? BngNetChangePercent,
    ConnectivityScenarioResult? Connectivity = null)
{
    /// <summary>The Connectivity tool's EC as a share of the habitat area, when this scenario has been calculated.</summary>
    public double? ConnectedHabitatPercent => Connectivity?.ConnectedPercent;

    /// <summary>The Connectivity tool's equivalent connected area (m², mean over its dispersal distances).</summary>
    public double? EquivalentConnectedArea => Connectivity?.EquivalentConnectedArea;

    /// <summary>Ecosystem value less the cost of maintaining the planting over the same outlook.</summary>
    public double NetValueOutlook => EcosystemValueOutlook - MaintenanceCostOutlook;
}

/// <summary>Everything a scenario's numbers depend on besides the scenario itself.</summary>
public sealed record ScenarioPerformanceInputs(
    IReadOnlyList<DashboardTreeItem> Trees,
    IReadOnlyList<DashboardFloorItem> Floors,
    IReadOnlyList<DesignOptionSetItem> Sets,
    IReadOnlyList<TreeBenefitTimeline>? Timelines,
    string UnitSystem,
    string Currency,
    double UsdToTargetRate,
    double? SiteAreaSquareMeters,
    int Years,
    int? GrowthYear,
    IReadOnlyList<int> GrowthYearStops,
    IReadOnlyList<ConnectivityScenarioResult>? Connectivity = null);

public static class ScenarioPerformanceCalculator
{
    public static ScenarioPerformance Build(DesignScenario scenario, ScenarioPerformanceInputs inputs)
    {
        var metric = string.Equals(inputs.UnitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
        double Pounds(double kilograms) => metric ? kilograms : kilograms / UnitConversions.KilogramsPerPound;
        double Ounces(double kilograms) => metric ? kilograms : kilograms / UnitConversions.KilogramsPerOunce;
        double Water(double cubicMetres) => metric ? cubicMetres : cubicMetres / UnitConversions.CubicMetresPerGallon;

        // Benefits count what stands after development (the existing site: what stood before);
        // BNG takes every floor of the scheme, lost ones included, for its baseline.
        var treesAllYears = DesignScenarios.Filter(inputs.Trees, tree => tree.DesignOption, scenario, inputs.Sets)
            .Where(tree => SitePhases.Counts(tree.PhaseRole, scenario))
            .Select(tree => DashboardAggregationService.NormalizeTree(tree, inputs.UnitSystem, inputs.Currency, inputs.UsdToTargetRate))
            .ToList();
        var schemeFloors = DesignScenarios.Filter(inputs.Floors, floor => floor.DesignOption, scenario, inputs.Sets);
        var floors = schemeFloors.Where(floor => SitePhases.Counts(SitePhases.RoleOf(floor), scenario)).ToList();
        var treesThisYear = inputs.GrowthYear is { } growthYear
            ? treesAllYears.Where(tree => GrowthYears.CountsIn(tree.Source, growthYear)).ToList()
            : treesAllYears;

        var outlook = new TreeOutlook(treesAllYears, inputs.GrowthYearStops, inputs.Timelines, inputs.UnitSystem, inputs.UsdToTargetRate);
        var years = inputs.Years;
        double Trees(Func<DashboardGrandTotal, double> annual) => outlook.Accumulate(annual, years);

        var total = DashboardAggregationService.BuildGrandTotal(treesThisYear, floors);
        var site = DashboardAggregationService.BuildSiteKpiSummary(treesThisYear, floors, [], inputs.SiteAreaSquareMeters, null);
        var bng = DashboardAggregationService.BuildBngSummary(schemeFloors, BngMetricCatalog.Default);

        var co2Equivalent = Trees(grand => grand.CO2EquivalentAnnual) +
                            Pounds(total.FloorCarbonSequesteredAnnual * UnitConversions.CarbonToCo2MassRatio) * years;
        var runoff = Water(Trees(grand => grand.RunoffAvoidedAnnual)) + Water(total.FloorRunoffAvoidedAnnual) * years;
        var pollution = Trees(grand => grand.TotalPollutionMassRemovedAnnual) + Ounces(total.FloorPollutionMassRemovedAnnual) * years;
        var oxygen = Trees(grand => grand.OxygenProducedAnnual) + Pounds(total.FloorOxygenProducedAnnual) * years;
        var value = Trees(grand => grand.TreeCostSavedAnnual) + total.FloorCostSavedAnnual * years;
        var maintenance = total.MaintenanceCostAnnual * years;

        return new ScenarioPerformance(
            scenario,
            DesignScenarios.Describe(scenario, inputs.Sets),
            years,
            outlook.Method,
            treesThisYear.Count,
            total.TreeCount,
            site.DistinctSpeciesCount,
            total.FloorCount,
            site.CanopyAreaSquareMeters,
            site.CanopyCoverPercent,
            site.GreenspaceRatioPercent,
            co2Equivalent,
            runoff,
            pollution,
            oxygen,
            value,
            maintenance,
            Pounds(total.FloorTotalGwp),
            // The existing site is the baseline itself: no change.
            scenario.IsExistingSite ? (bng.BaselineUnits > 0 ? bng.BaselineUnits : null) : bng.CurrentFloorCount > 0 ? bng.PostInterventionUnits : null,
            scenario.IsExistingSite ? (bng.BaselineUnits > 0 ? 0 : null) : bng.BaselineUnits > 0 ? bng.NetChangeUnits : null,
            scenario.IsExistingSite ? (bng.BaselineUnits > 0 ? 0 : null) : bng.NetChangePercent,
            ConnectivityScenarios.For(inputs.Connectivity, scenario));
    }

    /// <summary>The difference from a reference value: absolute, and as a % of the reference (null when that's 0 or unknown).</summary>
    public static (double? Absolute, double? Percent) Difference(double? value, double? reference)
    {
        if (value is not { } current || reference is not { } baseline)
        {
            return (null, null);
        }

        var absolute = current - baseline;
        return (absolute, Math.Abs(baseline) > 1e-12 ? absolute / Math.Abs(baseline) * 100d : null);
    }
}
