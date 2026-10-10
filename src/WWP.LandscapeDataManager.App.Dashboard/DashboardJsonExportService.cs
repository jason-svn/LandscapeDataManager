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
    double FloorCostSavedAnnual);

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
    double AirPollutionCostSavedLifetime);

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
    double CostSavedAnnual);

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
    double? LightingCompliancePercent);

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
    string? DesignOption = null);

/// <summary>The full payload written by "Export JSON" — a standalone snapshot meant to be loaded into another (e.g. browser-based) dashboard, independent of this app's own unit/currency toggles.</summary>
internal sealed record DashboardJsonExport(
    int SchemaVersion,
    DashboardJsonProject Project,
    IReadOnlyList<DashboardJsonScenario> Scenarios);

internal static class DashboardJsonExportService
{
    private const int SchemaVersion = 4;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Builds the export DTO with one scenario per design option (see <see cref="DashboardJsonScenario"/>),
    /// each scoped to only that option's own trees/floors and always normalized onto Metric so the JSON
    /// is self-describing regardless of what unit system the dashboard is currently displaying in.
    /// A project with no design options in play collapses to a single "Primary model" scenario, so this
    /// still works for the common case that isn't using growth-year design options at all.
    /// </summary>
    public static DashboardJsonExport Build(
        DashboardReportResult report,
        string currency,
        double usdToTargetRate,
        DashboardJsonLocation location)
    {
        var project = new DashboardJsonProject(report.DocumentTitle, currency, location, DateTimeOffset.Now);

        // Growth years modelled as worksets, alternatives as design options: one scenario per
        // (design option, growth year) — that option's trees for the year plus trees on no growth-year
        // workset, its floors and lighting, and the main model throughout. Grouped option by option so
        // each option's years form its own timeline in the web dashboard.
        var worksetYears = GrowthYears.InModel(report.Trees);
        if (worksetYears.Count > 0)
        {
            var options = DashboardUnitLabels.SplitByDesignOption(report.Trees, tree => tree.DesignOption, report.Floors, floor => floor.DesignOption);
            var hasAlternatives = options.Count > 1 || options[0].Label != DashboardUnitLabels.MainModelLabel;
            var defaultOption = DashboardUnitLabels.DefaultDesignOption(options.Select(option => option.Label));
            var yearScenarios = options
                .SelectMany(option => worksetYears.Select((year, index) => BuildScenario(
                    hasAlternatives ? $"{option.Label} · {year} years" : $"{year} years",
                    option.Label == defaultOption && index == 0,
                    option.Trees.Where(tree => GrowthYears.CountsIn(tree, year)).ToList(),
                    option.Floors,
                    DashboardUnitLabels.LightingFor(report.Lighting, hasAlternatives ? option.Label : null),
                    report,
                    currency,
                    usdToTargetRate,
                    years: year,
                    designOption: hasAlternatives ? option.Label : null)))
                .ToList();
            return new DashboardJsonExport(SchemaVersion, project, yearScenarios);
        }

        // Main-model elements (in no design option) belong to every option's scenario, as in Revit.
        // isPrimary marks the scenario the web dashboard opens on: the "Baseline" option when there is one.
        var optionScenarios = DashboardUnitLabels
            .SplitByDesignOption(report.Trees, tree => tree.DesignOption, report.Floors, floor => floor.DesignOption);
        var defaultLabel = DashboardUnitLabels.DefaultDesignOption(optionScenarios.Select(scenario => scenario.Label));
        var scenarios = optionScenarios
            .Select(scenario => BuildScenario(
                scenario.Label,
                scenario.Label == defaultLabel,
                scenario.Trees,
                scenario.Floors,
                // A model without design options has one "Primary model" scenario holding everything.
                DashboardUnitLabels.LightingFor(report.Lighting, scenario.Label == DashboardUnitLabels.MainModelLabel ? null : scenario.Label),
                report,
                currency,
                usdToTargetRate))
            .ToList();

        return new DashboardJsonExport(SchemaVersion, project, scenarios);
    }

    private static DashboardJsonScenario BuildScenario(
        string label,
        bool isPrimary,
        IReadOnlyList<DashboardTreeItem> treeItems,
        IReadOnlyList<DashboardFloorItem> floors,
        IReadOnlyList<DashboardLightingItem> lighting,
        DashboardReportResult report,
        string currency,
        double usdToTargetRate,
        int? years = null,
        string? designOption = null)
    {
        var normalizedTrees = treeItems
            .Select(tree => DashboardAggregationService.NormalizeTree(tree, "Metric", currency, usdToTargetRate))
            .ToList();

        var grandTotal = DashboardAggregationService.BuildGrandTotal(normalizedTrees, floors);
        var speciesSubtotals = DashboardAggregationService.AggregateTreesBySpecies(normalizedTrees);
        var floorSubtotals = DashboardAggregationService.AggregateFloorsByType(floors);
        var siteKpi = DashboardAggregationService.BuildSiteKpiSummary(
            normalizedTrees, floors, lighting, report.SiteTotalAreaSquareMeters, report.HabitatConnectivityScore);

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
            grandTotal.FloorCostSavedAnnual);

        var species = speciesSubtotals.Select(s => new DashboardJsonSpecies(
            s.SpeciesCode,
            s.CommonName,
            s.TreeCount,
            s.CarbonSequesteredAnnual, s.CarbonSequesteredLifetimeTotal,
            s.PM25RemovedAnnual + s.NO2RemovedAnnual + s.O3RemovedAnnual + s.SO2RemovedAnnual + s.CORemovedAnnual,
            s.PM25RemovedLifetimeTotal + s.NO2RemovedLifetimeTotal + s.O3RemovedLifetimeTotal + s.SO2RemovedLifetimeTotal + s.CORemovedLifetimeTotal,
            s.CarbonCostSavedAnnual, s.CarbonCostSavedLifetimeTotal,
            s.StormWaterCostSavedAnnual, s.StormWaterCostSavedLifetimeTotal,
            s.AirPollutionCostSavedAnnual, s.AirPollutionCostSavedLifetimeTotal))
            .ToList();

        var floorTypes = floorSubtotals.Select(f => new DashboardJsonFloorType(
            f.LdsType, f.FloorCount, f.AreaSquareMeters,
            f.CarbonSequesteredAnnual, f.RunoffAvoidedAnnual, f.PollutionMassRemovedAnnual,
            f.OxygenProducedAnnual, f.TotalGwp, f.CostSavedAnnual))
            .ToList();

        var siteKpiJson = new DashboardJsonSiteKpi(
            siteKpi.CanopyCoverPercent, siteKpi.CanopyAreaSquareMeters,
            siteKpi.SoftscapeSurfaceRatioPercent, siteKpi.PerviousAreaSquareMeters,
            siteKpi.DistinctSpeciesCount, siteKpi.NativeTreeCount, siteKpi.AdaptiveTreeCount,
            siteKpi.NativeSpeciesRatioPercent, siteKpi.DistinctBloomMonthsCount,
            siteKpi.DistinctEcologicalFunctionsCount, siteKpi.HabitatConnectivityScore,
            siteKpi.LightingFixtureCount, siteKpi.DarkSkyCompliantFixtureCount, siteKpi.LightingCompliancePercent);

        var bng = DashboardAggregationService.BuildBngSummary(floors, BngMetricCatalog.Default);
        static IReadOnlyList<DashboardJsonBngGroup> Groups(IReadOnlyList<BngGroupSubtotal> groups) =>
            groups.Select(g => new DashboardJsonBngGroup(g.Name, g.FloorCount, g.AreaHectares, g.HabitatUnits)).ToList();
        var bngJson = new DashboardJsonBng(
            bng.MetricVersion,
            bng.BaselineUnits, bng.PostInterventionUnits, bng.NetChangeUnits, bng.NetChangePercent,
            BngSummary.StatutoryNetGainPercent, bng.MeetsStatutoryNetGain, bng.IsComplete,
            bng.RetainedUnits, bng.EnhancedUnits, bng.CreatedUnits, bng.LostUnits,
            bng.FloorCount, bng.CurrentFloorCount,
            bng.RetainedFloorCount, bng.EnhancedFloorCount, bng.LostFloorCount, bng.CreatedFloorCount,
            bng.CheckDataFloorCount, bng.StaleFloorCount, bng.NotAssessedFloorCount, bng.ExcludedFloorCount,
            bng.TotalAreaHectares, bng.AssessedAreaHectares,
            Groups(bng.ByBroadHabitat), Groups(bng.ByDistinctiveness));

        return new DashboardJsonScenario(
            label, years ?? DashboardUnitLabels.ExtractProjectionYears(label), isPrimary, totals, siteKpiJson, bngJson, species, floorTypes, designOption);
    }

    public static async Task ExportAsync(string path, DashboardJsonExport export)
    {
        var json = JsonSerializer.Serialize(export, Options);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
    }
}
