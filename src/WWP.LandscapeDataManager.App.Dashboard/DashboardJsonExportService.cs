using System.Text;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

/// <summary>Best-effort — Revit's Site Location defaults to an unset placeholder on new projects, so latitude/longitude may be absent.</summary>
internal sealed record DashboardJsonLocation(double? Latitude, double? Longitude, string? PlaceName);

internal sealed record DashboardJsonProject(
    string Title,
    string Currency,
    DashboardJsonLocation Location,
    DateTimeOffset GeneratedAt);

internal sealed record DashboardJsonPollutants(double Pm25Kg, double No2Kg, double O3Kg, double So2Kg, double CoKg, double TotalKg);

internal sealed record DashboardJsonTotals(
    int TreeCount,
    int TreesExcludedFromTotals,
    double CarbonSequesteredAnnualKg,
    double CarbonSequesteredLifetimeKg,
    double Co2EquivalentAnnualKg,
    double Co2EquivalentLifetimeKg,
    double RunoffAvoidedAnnualM3,
    double RunoffAvoidedLifetimeM3,
    double RainfallInterceptedAnnualM3,
    double RainfallInterceptedLifetimeM3,
    DashboardJsonPollutants PollutantsRemovedAnnual,
    DashboardJsonPollutants PollutantsRemovedLifetime,
    double TreeCostSavedAnnual,
    double TreeCostSavedLifetime,
    double CarbonCostSavedAnnual,
    double CarbonCostSavedLifetime,
    double StormWaterCostSavedAnnual,
    double StormWaterCostSavedLifetime,
    double AirPollutionCostSavedAnnual,
    double AirPollutionCostSavedLifetime,
    int FloorCount,
    double FloorAreaSquareMeters,
    double FloorCarbonSequesteredAnnualKg,
    double FloorRunoffAvoidedAnnualM3,
    double FloorPollutionMassRemovedAnnualKg,
    double FloorOxygenProducedAnnualKg,
    double FloorTotalGwp,
    double FloorCostSavedAnnual,
    double TreeMaintenanceCostAnnual = 0,
    double FloorMaintenanceCostAnnual = 0,
    int StaleTreeCount = 0,
    int EstimatedTreeCount = 0);

internal sealed record DashboardJsonSpecies(
    string SpeciesCode,
    string CommonName,
    int TreeCount,
    double CarbonSequesteredAnnualKg,
    double CarbonSequesteredLifetimeKg,
    double PollutionMassRemovedAnnualKg,
    double PollutionMassRemovedLifetimeKg,
    double CarbonCostSavedAnnual,
    double CarbonCostSavedLifetime,
    double StormWaterCostSavedAnnual,
    double StormWaterCostSavedLifetime,
    double AirPollutionCostSavedAnnual,
    double AirPollutionCostSavedLifetime,
    double MaintenanceCostAnnual = 0);

/// <summary><see cref="CostSavedAnnual"/> is "as calculated" — Floor Calculator never records which currency it used (see <see cref="FloorTypeSubtotal"/>).</summary>
internal sealed record DashboardJsonFloorType(
    string LdsType,
    int FloorCount,
    double AreaSquareMeters,
    double CarbonSequesteredAnnualKg,
    double RunoffAvoidedAnnualM3,
    double PollutionMassRemovedAnnualKg,
    double OxygenProducedAnnualKg,
    double TotalGwp,
    double CostSavedAnnual,
    double MaintenanceCostAnnual = 0);

internal sealed record DashboardJsonSiteKpi(
    double? CanopyCoverPercent,
    double CanopyAreaSquareMeters,
    double? SoftscapeSurfaceRatioPercent,
    double PerviousAreaSquareMeters,
    int DistinctSpeciesCount,
    int NativeTreeCount,
    int AdaptiveTreeCount,
    double? NativeSpeciesRatioPercent,
    int DistinctBloomMonthsCount,
    int DistinctEcologicalFunctionsCount,
    double? HabitatConnectivityScore,
    int LightingFixtureCount,
    int DarkSkyCompliantFixtureCount,
    double? LightingCompliancePercent,
    double TreePollenAnnualKg = 0,
    double FloorPollenAnnualKg = 0,
    int AllergenicTreeCount = 0,
    int TreesWithIrrigationFactorCount = 0,
    double? MeanIrrigationDemandFactor = null,
    int LowWaterTreeCount = 0,
    int TreesWithNativeStatusCount = 0,
    double? ConnectedHabitatPercent = null,
    double? EquivalentConnectedAreaSquareMeters = null);

internal sealed record DashboardJsonBngGroup(string Name, int FloorCount, double AreaHectares, double HabitatUnits);

/// <summary>
/// The metric's on-site habitat headline — see <see cref="BngSummary"/>. Baseline = Existing-phase
/// floors (A-1); post-intervention = retained + enhanced (A-3) + created (A-2) units.
/// <see cref="NetChangePercent"/> is null without a baseline; <see cref="IsComplete"/> is false
/// while floors are stale or unassessed (the figures then don't cover the whole site).
/// </summary>
internal sealed record DashboardJsonBng(
    string? MetricVersion,
    double BaselineUnits,
    double PostInterventionUnits,
    double NetChangeUnits,
    double? NetChangePercent,
    double StatutoryNetGainPercent,
    bool? MeetsStatutoryNetGain,
    bool IsComplete,
    double RetainedUnits,
    double EnhancedUnits,
    double CreatedUnits,
    double LostUnits,
    int FloorCount,
    int CurrentFloorCount,
    int RetainedFloorCount,
    int EnhancedFloorCount,
    int LostFloorCount,
    int CreatedFloorCount,
    int CheckDataFloorCount,
    int StaleFloorCount,
    int NotAssessedFloorCount,
    int ExcludedFloorCount,
    double TotalAreaHectares,
    double AssessedAreaHectares,
    IReadOnlyList<DashboardJsonBngGroup> ByBroadHabitat,
    IReadOnlyList<DashboardJsonBngGroup> ByDistinctiveness);

/// <summary>
/// One design option's worth of the project — e.g. this WWP project's "Growth Timeline : 5/10/15/20/25
/// Years" options, each modeling the same planted layout at a different tree age. Kept separate rather
/// than summed: design options here are alternate snapshots in time of the same site, not independent
/// alternatives whose benefits add together, so a downstream viewer must let the user pick one instead
/// of silently blending 5-, 10-, 15-, 20- and 25-year figures into one meaningless total.
/// </summary>
/// <remarks>
/// Schema v4 adds <see cref="DesignOption"/>: when growth years are modelled as worksets and the
/// model also has design options (real alternatives), each scenario is one option at one growth
/// year, and the web dashboard offers the option as a dropdown and the year as its slider. Null
/// when scenarios form a single timeline (v3 files never have it).
/// Schema v5: a scenario is a design scenario — one chosen option per design option set, named
/// in <see cref="DesignOption"/>, with the choices themselves in <see cref="Choices"/> (set → option).
/// Before v5, each design option was its own scenario and the other sets were dropped.
/// Schema v7: a scheme's benefits count only what stands after development (demolished trees
/// and planting areas are left out), and the "Existing site" entry (<see cref="IsExistingSite"/>)
/// is the site before it, for comparison; its BNG is the baseline (no change).
/// Schema v6 adds, without changing anything earlier: <see cref="BenefitsFromForecast"/>, stale
/// and WWP-sheet-estimate tree counts, pollen, allergen and irrigation site figures, and how many
/// trees have a native status (the native share's denominator).
/// </remarks>
internal sealed record DashboardJsonScenario(
    string Label,
    int? Years,
    bool IsPrimary,
    DashboardJsonTotals Totals,
    DashboardJsonSiteKpi SiteKpi,
    DashboardJsonBng Bng,
    IReadOnlyList<DashboardJsonSpecies> Species,
    IReadOnlyList<DashboardJsonFloorType> FloorTypes,
    string? DesignOption = null,
    IReadOnlyDictionary<string, string>? Choices = null,
    bool BenefitsFromForecast = false,
    bool IsExistingSite = false);

/// <summary>The full payload written by "Export JSON" — a standalone snapshot meant to be loaded into another (e.g. browser-based) dashboard, independent of this app's own unit/currency toggles.</summary>
internal sealed record DashboardJsonExport(
    int SchemaVersion,
    DashboardJsonProject Project,
    IReadOnlyList<DashboardJsonScenario> Scenarios);

internal static class DashboardJsonExportService
{
    private const int SchemaVersion = 7;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Builds the export DTO with one entry per design scenario (one option per design option set,
    /// see <see cref="DesignScenarios"/>): the main model plus that scenario's chosen options, never
    /// the options it doesn't choose. With growth years modelled as worksets, each scenario becomes
    /// one entry per growth year — that year's trees plus trees on no growth-year workset — grouped
    /// scenario by scenario so each forms its own timeline in the web dashboard. Always normalized onto
    /// Metric so the JSON is self-describing whatever the dashboard is displaying. A model without
    /// design options has the single "Primary options" scenario.
    /// </summary>
    public static DashboardJsonExport Build(
        DashboardReportResult report,
        string currency,
        double usdToTargetRate,
        DashboardJsonLocation location,
        IReadOnlyList<DesignOptionSetItem> sets,
        IReadOnlyList<DesignScenario> scenarios,
        int designYear = GrowthYearNames.DefaultDesignYear,
        IReadOnlyList<ConnectivityScenarioResult>? connectivity = null)
    {
        var project = new DashboardJsonProject(report.DocumentTitle, currency, location, DateTimeOffset.Now);
        var hasAlternatives = scenarios.Count > 1;
        var defaultScenario = DesignScenarios.Find(scenarios, null);

        var worksetYears = GrowthYears.InModel(report.Trees);
        // The file opens on the default scenario at the design year (new trees are drawn at it), or the year nearest it.
        var defaultYear = worksetYears.OrderBy(year => Math.Abs(year - designYear)).ThenByDescending(year => year).FirstOrDefault();
        var entries = scenarios.SelectMany(scenario =>
        {
            // Benefits count what stands after development (the existing site: what stood before);
            // BNG takes every floor of the scheme, lost ones included, for its baseline.
            var trees = DesignScenarios.Filter(report.Trees, tree => tree.DesignOption, scenario, sets)
                .Where(tree => SitePhases.Counts(tree.PhaseRole, scenario))
                .Select(tree => DashboardAggregationService.NormalizeTree(tree, "Metric", currency, usdToTargetRate))
                .ToList();
            var bngFloors = DesignScenarios.Filter(report.Floors, floor => floor.DesignOption, scenario, sets);
            var floors = bngFloors.Where(floor => SitePhases.Counts(SitePhases.RoleOf(floor), scenario)).ToList();
            var lighting = DesignScenarios.Filter(report.Lighting, fixture => fixture.DesignOption, scenario, sets);
            var label = hasAlternatives ? DesignScenarios.Label(scenario, sets) : scenario.Name;
            if (worksetYears.Count == 0)
            {
                return new[] { BuildScenario(label, scenario == defaultScenario, trees, trees, floors, bngFloors, lighting, report,
                    designOption: hasAlternatives ? scenario.Name : null, choices: scenario.Choices, isExistingSite: scenario.IsExistingSite,
                    connectivity: ConnectivityScenarios.For(connectivity, scenario)) };
            }

            // As in the Dashboard: with i-Tree forecasts in the model, a growth year's benefits are each
            // planted tree's forecast for that year, and that year's copies only drive canopy and the
            // other geometry figures (a copy whose trunk doesn't grow reports the planting-size benefit).
            var outlook = new TreeOutlook(trees, worksetYears, report.TreeTimelines, "Metric", usdToTargetRate);
            var fromForecast = outlook.Method == TreeOutlookMethod.Timeline;
            return worksetYears.Select(year =>
            {
                var geometryTrees = trees.Where(tree => GrowthYears.CountsIn(tree.Source, year)).ToList();
                return BuildScenario(
                    hasAlternatives ? $"{scenario.Name} · {year} years" : $"{year} years",
                    scenario == defaultScenario && year == defaultYear,
                    fromForecast ? outlook.InYear(year) : geometryTrees,
                    geometryTrees,
                    floors,
                    bngFloors,
                    lighting,
                    report,
                    years: year,
                    designOption: hasAlternatives ? scenario.Name : null,
                    choices: scenario.Choices,
                    benefitsFromForecast: fromForecast,
                    isExistingSite: scenario.IsExistingSite,
                    connectivity: ConnectivityScenarios.For(connectivity, scenario));
            });
        }).ToList();

        return new DashboardJsonExport(SchemaVersion, project, entries);
    }

    /// <param name="normalizedTrees">The trees whose benefits count (normalized onto Metric and the export currency).</param>
    /// <param name="geometryTrees">The trees as modelled in this growth year, for canopy and the other site figures.</param>
    private static DashboardJsonScenario BuildScenario(
        string label,
        bool isPrimary,
        IReadOnlyList<NormalizedTreeMetrics> normalizedTrees,
        IReadOnlyList<NormalizedTreeMetrics> geometryTrees,
        IReadOnlyList<DashboardFloorItem> floors,
        IReadOnlyList<DashboardFloorItem> bngFloors,
        IReadOnlyList<DashboardLightingItem> lighting,
        DashboardReportResult report,
        int? years = null,
        string? designOption = null,
        IReadOnlyDictionary<string, string>? choices = null,
        bool benefitsFromForecast = false,
        bool isExistingSite = false,
        ConnectivityScenarioResult? connectivity = null)
    {
        var grandTotal = DashboardAggregationService.BuildGrandTotal(normalizedTrees, floors);
        var speciesSubtotals = DashboardAggregationService.AggregateTreesBySpecies(normalizedTrees);
        var floorSubtotals = DashboardAggregationService.AggregateFloorsByType(floors);
        var siteKpi = DashboardAggregationService.BuildSiteKpiSummary(
            geometryTrees, floors, lighting, report.SiteTotalAreaSquareMeters, report.HabitatConnectivityScore);

        var totals = new DashboardJsonTotals(
            grandTotal.TreeCount,
            grandTotal.TreesExcludedFromTotals,
            grandTotal.CarbonSequesteredAnnual, grandTotal.CarbonSequesteredLifetimeTotal,
            grandTotal.CO2EquivalentAnnual, grandTotal.CO2EquivalentLifetimeTotal,
            grandTotal.RunoffAvoidedAnnual, grandTotal.RunoffAvoidedLifetimeTotal,
            grandTotal.RainfallInterceptedAnnual, grandTotal.RainfallInterceptedLifetimeTotal,
            new DashboardJsonPollutants(
                grandTotal.PM25RemovedAnnual, grandTotal.NO2RemovedAnnual, grandTotal.O3RemovedAnnual,
                grandTotal.SO2RemovedAnnual, grandTotal.CORemovedAnnual, grandTotal.TotalPollutionMassRemovedAnnual),
            new DashboardJsonPollutants(
                grandTotal.PM25RemovedLifetimeTotal, grandTotal.NO2RemovedLifetimeTotal, grandTotal.O3RemovedLifetimeTotal,
                grandTotal.SO2RemovedLifetimeTotal, grandTotal.CORemovedLifetimeTotal, grandTotal.TotalPollutionMassRemovedLifetimeTotal),
            grandTotal.TreeCostSavedAnnual, grandTotal.TreeCostSavedLifetimeTotal,
            grandTotal.CarbonCostSavedAnnual, grandTotal.CarbonCostSavedLifetimeTotal,
            grandTotal.StormWaterCostSavedAnnual, grandTotal.StormWaterCostSavedLifetimeTotal,
            grandTotal.AirPollutionCostSavedAnnual, grandTotal.AirPollutionCostSavedLifetimeTotal,
            grandTotal.FloorCount,
            grandTotal.FloorAreaSquareMeters,
            grandTotal.FloorCarbonSequesteredAnnual,
            grandTotal.FloorRunoffAvoidedAnnual,
            grandTotal.FloorPollutionMassRemovedAnnual,
            floorSubtotals.Sum(f => f.OxygenProducedAnnual),
            grandTotal.FloorTotalGwp,
            grandTotal.FloorCostSavedAnnual,
            grandTotal.TreeMaintenanceCostAnnual,
            grandTotal.FloorMaintenanceCostAnnual,
            normalizedTrees.Count(tree => string.Equals(tree.Source.Status, "Stale", StringComparison.OrdinalIgnoreCase)),
            normalizedTrees.Count(tree => tree.IsSheetEstimate));

        var species = speciesSubtotals.Select(s => new DashboardJsonSpecies(
            s.SpeciesCode,
            s.CommonName,
            s.TreeCount,
            s.CarbonSequesteredAnnual, s.CarbonSequesteredLifetimeTotal,
            s.PollutionRemovedAnnual,
            s.PM25RemovedLifetimeTotal + s.NO2RemovedLifetimeTotal + s.O3RemovedLifetimeTotal + s.SO2RemovedLifetimeTotal + s.CORemovedLifetimeTotal,
            s.CarbonCostSavedAnnual, s.CarbonCostSavedLifetimeTotal,
            s.StormWaterCostSavedAnnual, s.StormWaterCostSavedLifetimeTotal,
            s.AirPollutionCostSavedAnnual, s.AirPollutionCostSavedLifetimeTotal,
            s.MaintenanceCostAnnual))
            .ToList();

        var floorTypes = floorSubtotals.Select(f => new DashboardJsonFloorType(
            f.LdsType, f.FloorCount, f.AreaSquareMeters,
            f.CarbonSequesteredAnnual, f.RunoffAvoidedAnnual, f.PollutionMassRemovedAnnual,
            f.OxygenProducedAnnual, f.TotalGwp, f.CostSavedAnnual, f.MaintenanceCostAnnual))
            .ToList();

        var siteKpiJson = new DashboardJsonSiteKpi(
            siteKpi.CanopyCoverPercent, siteKpi.CanopyAreaSquareMeters,
            siteKpi.SoftscapeSurfaceRatioPercent, siteKpi.PerviousAreaSquareMeters,
            siteKpi.DistinctSpeciesCount, siteKpi.NativeTreeCount, siteKpi.AdaptiveTreeCount,
            siteKpi.NativeSpeciesRatioPercent, siteKpi.DistinctBloomMonthsCount,
            siteKpi.DistinctEcologicalFunctionsCount, siteKpi.HabitatConnectivityScore,
            siteKpi.LightingFixtureCount, siteKpi.DarkSkyCompliantFixtureCount, siteKpi.LightingCompliancePercent,
            siteKpi.TreePollenAnnual, siteKpi.FloorPollenAnnual, siteKpi.AllergenicTreeCount,
            siteKpi.TreesWithIrrigationFactorCount, siteKpi.MeanIrrigationDemandFactor, siteKpi.LowWaterTreeCount,
            siteKpi.TreesWithNativeStatusCount,
            // The Connectivity tool's connected habitat for this scenario (the trees as drawn, any growth year).
            connectivity?.ConnectedPercent, connectivity?.EquivalentConnectedArea);

        var bng = DashboardAggregationService.BuildBngSummary(bngFloors, BngMetricCatalog.Default);
        static IReadOnlyList<DashboardJsonBngGroup> Groups(IReadOnlyList<BngGroupSubtotal> groups) =>
            groups.Select(g => new DashboardJsonBngGroup(g.Name, g.FloorCount, g.AreaHectares, g.HabitatUnits)).ToList();
        var bngJson = new DashboardJsonBng(
            bng.MetricVersion,
            // The existing site is the baseline itself: no change.
            bng.BaselineUnits,
            isExistingSite ? bng.BaselineUnits : bng.PostInterventionUnits,
            isExistingSite ? 0 : bng.NetChangeUnits,
            isExistingSite ? (bng.BaselineUnits > 0 ? 0 : null) : bng.NetChangePercent,
            BngSummary.StatutoryNetGainPercent, isExistingSite ? null : bng.MeetsStatutoryNetGain, bng.IsComplete,
            bng.RetainedUnits, bng.EnhancedUnits, bng.CreatedUnits, bng.LostUnits,
            bng.FloorCount, bng.CurrentFloorCount,
            bng.RetainedFloorCount, bng.EnhancedFloorCount, bng.LostFloorCount, bng.CreatedFloorCount,
            bng.CheckDataFloorCount, bng.StaleFloorCount, bng.NotAssessedFloorCount, bng.ExcludedFloorCount,
            bng.TotalAreaHectares, bng.AssessedAreaHectares,
            Groups(bng.ByBroadHabitat), Groups(bng.ByDistinctiveness));

        return new DashboardJsonScenario(
            label, years ?? DashboardUnitLabels.ExtractProjectionYears(label), isPrimary, totals, siteKpiJson, bngJson, species, floorTypes, designOption,
            choices is { Count: > 0 } ? choices : null, benefitsFromForecast, isExistingSite);
    }

    public static async Task ExportAsync(string path, DashboardJsonExport export)
    {
        var json = JsonSerializer.Serialize(export, Options);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
    }
}
