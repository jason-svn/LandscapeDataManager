using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    double MaintenanceCostAnnual = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DashboardJsonPhenology? Phenology = null);

/// <summary>A colour as the sheet names it, with its "#RRGGBB".</summary>
internal sealed record DashboardJsonColour(string Name, string Hex);

/// <summary>
/// A species' year at a glance (schema 11, see <see cref="PlantAppearance"/>): the months it's in
/// flower and in fruit, its flowers' and fruit's colours, its kind of fruit, and its colour in
/// <see cref="Seasons"/> — spring, summer, autumn, winter (null where the sheet has none).
/// </summary>
internal sealed record DashboardJsonPhenology(
    IReadOnlyList<int> FlowerMonths,
    IReadOnlyList<int> FruitMonths,
    DashboardJsonColour? Flower,
    DashboardJsonColour? Fruit,
    string? FruitType,
    IReadOnlyList<DashboardJsonColour?> Seasons);

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
/// Schema v8: with growth years, a scenario's tree lifetime figures (totals and species) are the
/// benefits from planting to its growth year, so they rise year by year; before v8 they were each
/// tree's whole forecast, the same at every growth year. Without growth years, nothing changes.
/// Schema v9 adds the 3D scene (<see cref="DashboardJsonExport.Scene"/>) and, on each entry, the
/// scene's trees (<see cref="SceneTrees"/>) and floors (<see cref="SceneFloors"/>) it's built from;
/// both are left out when Revit couldn't give the geometry. Without Growth Projections copies, a model
/// that can grow its trees (growth rates or i-Tree forecasts with sizes) now has growth years too:
/// every tree grown by the planting family's formulas, the canopy figures from the same sizes.
/// Schema v10 adds the scene's colour schemes (<see cref="DashboardJsonScene.ColourSchemes"/>): the
/// model's views that colour trees or floors with filters, each element's colour decided by Revit.
/// Schema v11 adds each species' year at a glance (<see cref="DashboardJsonSpecies.Phenology"/>):
/// its months in flower and in fruit, and its flower, fruit and seasonal colours from the WWP sheet.
/// The site's phenological resilience now counts fruit months too.
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
    bool IsExistingSite = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? SceneTrees = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? SceneFloors = null);

/// <summary>The full payload written by "Export JSON" — a standalone snapshot meant to be loaded into another (e.g. browser-based) dashboard, independent of this app's own unit/currency toggles.</summary>
internal sealed record DashboardJsonExport(
    int SchemaVersion,
    DashboardJsonProject Project,
    IReadOnlyList<DashboardJsonScenario> Scenarios,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DashboardJsonScene? Scene = null);

internal static class DashboardJsonExportService
{
    private const int SchemaVersion = 11;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>With a 3D scene the file is mostly coordinates: indenting them would double its size.</summary>
    private static readonly JsonSerializerOptions CompactOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// Builds the export DTO with one entry per design scenario (one option per design option set,
    /// see <see cref="DesignScenarios"/>): the main model plus that scenario's chosen options, never
    /// the options it doesn't choose. With growth years modelled as worksets, each scenario becomes
    /// one entry per growth year — that year's trees plus trees on no growth-year workset — grouped
    /// scenario by scenario so each forms its own timeline in the web dashboard. Always normalized onto
    /// Metric so the JSON is self-describing whatever the dashboard is displaying. A model without
    /// design options has the single "Primary options" scenario.
    /// With <paramref name="geometry"/> (Revit's trees and floors) the export carries the 3D scene, and
    /// without Growth Projections copies a model that can grow its trees gets growth years 0–25 (see
    /// <see cref="DashboardJsonSceneBuilder.CanForecast"/>): every tree grown by the planting family's
    /// formulas, its canopy figures from the same sizes the 3D view draws.
    /// </summary>
    public static DashboardJsonExport Build(
        DashboardReportResult report,
        string currency,
        double usdToTargetRate,
        DashboardJsonLocation location,
        IReadOnlyList<DesignOptionSetItem> sets,
        IReadOnlyList<DesignScenario> scenarios,
        int designYear = GrowthYearNames.DefaultDesignYear,
        IReadOnlyList<ConnectivityScenarioResult>? connectivity = null,
        SceneGeometryResult? geometry = null)
    {
        var project = new DashboardJsonProject(report.DocumentTitle, currency, location, DateTimeOffset.Now);
        var hasAlternatives = scenarios.Count > 1;
        var defaultScenario = DesignScenarios.Find(scenarios, null);

        var worksetYears = GrowthYears.InModel(report.Trees);
        var forecast = geometry is not null && DashboardJsonSceneBuilder.CanForecast(geometry, report);
        var growthYears = forecast ? DashboardJsonSceneBuilder.ForecastYears(designYear) : worksetYears;
        var scene = geometry is null
            ? null
            : new DashboardJsonSceneBuilder(
                geometry,
                report,
                forecast ? SceneGrowth.Forecast : worksetYears.Count > 0 ? SceneGrowth.Worksets : SceneGrowth.Drawn,
                growthYears.Count > 0 ? growthYears.Max() : 0);

        // The file opens on the default scenario at the design year (new trees are drawn at it), or the year nearest it.
        var defaultYear = growthYears.OrderBy(year => Math.Abs(year - designYear)).ThenByDescending(year => year).FirstOrDefault();
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
            var sceneFloors = scene?.FloorIndices(floors);
            if (growthYears.Count == 0)
            {
                return new[] { BuildScenario(label, scenario == defaultScenario, trees, trees, floors, bngFloors, lighting, report,
                    designOption: hasAlternatives ? scenario.Name : null, choices: scenario.Choices, isExistingSite: scenario.IsExistingSite,
                    connectivity: ConnectivityScenarios.For(connectivity, scenario),
                    sceneTrees: scene?.TreeIndices(trees), sceneFloors: sceneFloors) };
            }

            // As in the Dashboard: with i-Tree forecasts in the model, a growth year's benefits are each
            // planted tree's forecast for that year, and that year's copies only drive canopy and the
            // other geometry figures (a copy whose trunk doesn't grow reports the planting-size benefit).
            var outlook = new TreeOutlook(trees, worksetYears, report.TreeTimelines, "Metric", usdToTargetRate);
            var fromForecast = outlook.Method == TreeOutlookMethod.Timeline;
            var speciesOutlooks = new Dictionary<string, TreeOutlook>(StringComparer.Ordinal);
            TreeOutlook OutlookOf(string? species) => species is null
                ? outlook
                : speciesOutlooks.TryGetValue(species, out var cached)
                    ? cached
                    : speciesOutlooks[species] = new TreeOutlook(
                        [.. trees.Where(tree => (tree.Source.SpeciesCode ?? "Unassigned") == species)], worksetYears, report.TreeTimelines, "Metric", usdToTargetRate);
            return growthYears.Select(year =>
            {
                // Forecast growth: every tree, grown to the year; with copies: that year's trees as drawn.
                var geometryTrees = forecast
                    ? trees.Select(tree => scene!.Grown(tree, year)).ToList()
                    : trees.Where(tree => GrowthYears.CountsIn(tree.Source, year)).ToList();
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
                    connectivity: ConnectivityScenarios.For(connectivity, scenario),
                    sincePlanting: (species, metric) => OutlookOf(species).Accumulate(metric, year),
                    sceneTrees: scene?.TreeIndices(geometryTrees),
                    sceneFloors: sceneFloors);
            });
        }).ToList();

        return new DashboardJsonExport(SchemaVersion, project, entries, scene?.ToJson());
    }

    /// <param name="normalizedTrees">The trees whose benefits count (normalized onto Metric and the export currency).</param>
    /// <param name="geometryTrees">The trees as modelled in this growth year, for canopy and the other site figures.</param>
    /// <param name="sincePlanting">
    /// With growth years, an annual metric added up from planting to this growth year (the outlook's
    /// running total), for all trees (species null) or one species code: the lifetime figures, so they
    /// rise year by year. Without, the lifetime figures are each tree's whole forecast.
    /// </param>
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
        ConnectivityScenarioResult? connectivity = null,
        Func<string?, Func<DashboardGrandTotal, double>, double>? sincePlanting = null,
        IReadOnlyList<int>? sceneTrees = null,
        IReadOnlyList<int>? sceneFloors = null)
    {
        var grandTotal = DashboardAggregationService.BuildGrandTotal(normalizedTrees, floors);
        var speciesSubtotals = DashboardAggregationService.AggregateTreesBySpecies(normalizedTrees);
        var floorSubtotals = DashboardAggregationService.AggregateFloorsByType(floors);
        var siteKpi = DashboardAggregationService.BuildSiteKpiSummary(
            geometryTrees, floors, lighting, report.SiteTotalAreaSquareMeters, report.HabitatConnectivityScore);

        // A lifetime figure: from planting to this growth year when there are growth years, else as stored.
        double Lifetime(string? species, Func<DashboardGrandTotal, double> annual, double stored) =>
            sincePlanting is null ? stored : sincePlanting(species, annual);
        double Total(Func<DashboardGrandTotal, double> annual, Func<DashboardGrandTotal, double> lifetime) =>
            Lifetime(null, annual, lifetime(grandTotal));

        var totals = new DashboardJsonTotals(
            grandTotal.TreeCount,
            grandTotal.TreesExcludedFromTotals,
            grandTotal.CarbonSequesteredAnnual, Total(t => t.CarbonSequesteredAnnual, t => t.CarbonSequesteredLifetimeTotal),
            grandTotal.CO2EquivalentAnnual, Total(t => t.CO2EquivalentAnnual, t => t.CO2EquivalentLifetimeTotal),
            grandTotal.RunoffAvoidedAnnual, Total(t => t.RunoffAvoidedAnnual, t => t.RunoffAvoidedLifetimeTotal),
            grandTotal.RainfallInterceptedAnnual, Total(t => t.RainfallInterceptedAnnual, t => t.RainfallInterceptedLifetimeTotal),
            new DashboardJsonPollutants(
                grandTotal.PM25RemovedAnnual, grandTotal.NO2RemovedAnnual, grandTotal.O3RemovedAnnual,
                grandTotal.SO2RemovedAnnual, grandTotal.CORemovedAnnual, grandTotal.TotalPollutionMassRemovedAnnual),
            new DashboardJsonPollutants(
                Total(t => t.PM25RemovedAnnual, t => t.PM25RemovedLifetimeTotal), Total(t => t.NO2RemovedAnnual, t => t.NO2RemovedLifetimeTotal),
                Total(t => t.O3RemovedAnnual, t => t.O3RemovedLifetimeTotal), Total(t => t.SO2RemovedAnnual, t => t.SO2RemovedLifetimeTotal),
                Total(t => t.CORemovedAnnual, t => t.CORemovedLifetimeTotal), Total(t => t.TotalPollutionMassRemovedAnnual, t => t.TotalPollutionMassRemovedLifetimeTotal)),
            grandTotal.TreeCostSavedAnnual, Total(t => t.TreeCostSavedAnnual, t => t.TreeCostSavedLifetimeTotal),
            grandTotal.CarbonCostSavedAnnual, Total(t => t.CarbonCostSavedAnnual, t => t.CarbonCostSavedLifetimeTotal),
            grandTotal.StormWaterCostSavedAnnual, Total(t => t.StormWaterCostSavedAnnual, t => t.StormWaterCostSavedLifetimeTotal),
            grandTotal.AirPollutionCostSavedAnnual, Total(t => t.AirPollutionCostSavedAnnual, t => t.AirPollutionCostSavedLifetimeTotal),
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
            s.CarbonSequesteredAnnual, Lifetime(s.SpeciesCode, t => t.CarbonSequesteredAnnual, s.CarbonSequesteredLifetimeTotal),
            s.PollutionRemovedAnnual,
            Lifetime(s.SpeciesCode, t => t.TotalPollutionMassRemovedAnnual,
                s.PM25RemovedLifetimeTotal + s.NO2RemovedLifetimeTotal + s.O3RemovedLifetimeTotal + s.SO2RemovedLifetimeTotal + s.CORemovedLifetimeTotal),
            s.CarbonCostSavedAnnual, Lifetime(s.SpeciesCode, t => t.CarbonCostSavedAnnual, s.CarbonCostSavedLifetimeTotal),
            s.StormWaterCostSavedAnnual, Lifetime(s.SpeciesCode, t => t.StormWaterCostSavedAnnual, s.StormWaterCostSavedLifetimeTotal),
            s.AirPollutionCostSavedAnnual, Lifetime(s.SpeciesCode, t => t.AirPollutionCostSavedAnnual, s.AirPollutionCostSavedLifetimeTotal),
            s.MaintenanceCostAnnual,
            Phenology(normalizedTrees.Where(tree => (tree.Source.SpeciesCode ?? "Unassigned") == s.SpeciesCode).Select(tree => tree.Source).ToList())))
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
            choices is { Count: > 0 } ? choices : null, benefitsFromForecast, isExistingSite, sceneTrees, sceneFloors);
    }

    /// <summary>A species' year at a glance from its trees (their type's sheet data); null when it has none of it.</summary>
    private static DashboardJsonPhenology? Phenology(IReadOnlyList<DashboardTreeItem> trees)
    {
        var appearance = trees.Select(tree => tree.Appearance).FirstOrDefault(found => found is not null);
        var flowers = PlantColours.Months(trees.Select(tree => tree.BloomMonths).FirstOrDefault(months => !string.IsNullOrWhiteSpace(months)));
        if (appearance is null && flowers.Count == 0)
        {
            return null;
        }

        static DashboardJsonColour? Colour(string? text) => PlantColours.Parse(text) is { } colour ? new DashboardJsonColour(colour.Name, colour.Hex) : null;
        return new DashboardJsonPhenology(
            [.. flowers.Distinct().Order()],
            [.. PlantColours.FruitMonths(appearance).Distinct().Order()],
            Colour(appearance?.FlowerColour),
            Colour(appearance?.FruitColour),
            PlantColours.FruitType(appearance),
            [Colour(appearance?.SpringColour), Colour(appearance?.SummerColour), Colour(appearance?.AutumnColour), Colour(appearance?.WinterColour)]);
    }

    public static async Task ExportAsync(string path, DashboardJsonExport export)
    {
        var json = JsonSerializer.Serialize(export, export.Scene is null ? Options : CompactOptions);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
    }
}
