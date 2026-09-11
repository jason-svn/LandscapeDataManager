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

/// <summary>
/// One design option's worth of the project — e.g. this WWP project's "Growth Timeline : 5/10/15/20/25
/// Years" options, each modeling the same planted layout at a different tree age. Kept separate rather
/// than summed: design options here are alternate snapshots in time of the same site, not independent
/// alternatives whose benefits add together, so a downstream viewer must let the user pick one instead
/// of silently blending 5-, 10-, 15-, 20- and 25-year figures into one meaningless total.
/// </summary>
internal sealed record DashboardJsonScenario(
    string Label,
    int? Years,
    bool IsPrimary,
    DashboardJsonTotals Totals,
    DashboardJsonSiteKpi SiteKpi,
    IReadOnlyList<DashboardJsonSpecies> Species,
    IReadOnlyList<DashboardJsonFloorType> FloorTypes);

/// <summary>The full payload written by "Export JSON" — a standalone snapshot meant to be loaded into another (e.g. browser-based) dashboard, independent of this app's own unit/currency toggles.</summary>
internal sealed record DashboardJsonExport(
    int SchemaVersion,
    DashboardJsonProject Project,
    IReadOnlyList<DashboardJsonScenario> Scenarios);

internal static class DashboardJsonExportService
{
    private const int SchemaVersion = 2;
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
        var treesByOption = report.Trees
            .GroupBy(tree => (Label: DashboardUnitLabels.FormatDesignOption(tree.DesignOption), tree.DesignOption.IsPrimary))
            .ToList();
        var floorsByOption = report.Floors
            .GroupBy(floor => (Label: DashboardUnitLabels.FormatDesignOption(floor.DesignOption), floor.DesignOption.IsPrimary))
            .ToList();

        var optionKeys = treesByOption.Select(g => g.Key)
            .Concat(floorsByOption.Select(g => g.Key))
            .Distinct()
            .OrderBy(key => DashboardUnitLabels.ExtractProjectionYears(key.Label) ?? int.MaxValue)
            .ThenBy(key => key.Label, StringComparer.Ordinal)
            .ToList();

        var scenarios = optionKeys.Select(key =>
        {
            var trees = treesByOption.FirstOrDefault(g => g.Key.Equals(key))?.ToList() ?? [];
            var floors = floorsByOption.FirstOrDefault(g => g.Key.Equals(key))?.ToList() ?? [];
            return BuildScenario(key.Label, key.IsPrimary, trees, floors, report, currency, usdToTargetRate);
        }).ToList();

        var project = new DashboardJsonProject(report.DocumentTitle, currency, location, DateTimeOffset.Now);
        return new DashboardJsonExport(SchemaVersion, project, scenarios);
    }

    private static DashboardJsonScenario BuildScenario(
        string label,
        bool isPrimary,
        IReadOnlyList<DashboardTreeItem> treeItems,
        IReadOnlyList<DashboardFloorItem> floors,
        DashboardReportResult report,
        string currency,
        double usdToTargetRate)
    {
        var normalizedTrees = treeItems
            .Select(tree => DashboardAggregationService.NormalizeTree(tree, "Metric", currency, usdToTargetRate))
            .ToList();

        var grandTotal = DashboardAggregationService.BuildGrandTotal(normalizedTrees, floors);
        var speciesSubtotals = DashboardAggregationService.AggregateTreesBySpecies(normalizedTrees);
        var floorSubtotals = DashboardAggregationService.AggregateFloorsByType(floors);
        var siteKpi = DashboardAggregationService.BuildSiteKpiSummary(
            normalizedTrees, floors, report.Lighting, report.SiteTotalAreaSquareMeters, report.HabitatConnectivityScore);

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

        return new DashboardJsonScenario(
            label, DashboardUnitLabels.ExtractProjectionYears(label), isPrimary, totals, siteKpiJson, species, floorTypes);
    }

    public static async Task ExportAsync(string path, DashboardJsonExport export)
    {
        var json = JsonSerializer.Serialize(export, Options);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
    }
}
