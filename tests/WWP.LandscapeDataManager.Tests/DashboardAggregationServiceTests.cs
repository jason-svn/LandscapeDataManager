using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class DashboardAggregationServiceTests
{
    private static DashboardTreeItem CreateTree(
        string uniqueId = "uid-1",
        string speciesCode = "ACPL",
        string commonName = "Norway Maple",
        string status = "Calculated",
        string storedUnitSystem = "Metric",
        string storedCurrency = "USD",
        double storedExchangeRateUsed = 1d,
        double co2SequesteredAnnual = 0d,
        double pm25RemovedAnnual = 0d,
        double costSavedAnnual = 0d) =>
        new(
            uniqueId, 1, "Family", "Type", speciesCode, commonName, "Acer platanoides", "Tree",
            "Level 1", new DesignOptionInfo(null, null, true), status, storedUnitSystem, storedCurrency, storedExchangeRateUsed,
            co2SequesteredAnnual, co2SequesteredAnnual * 20,
            0d, 0d, 0d, 0d, 0d, 0d,
            pm25RemovedAnnual, pm25RemovedAnnual * 20,
            0d, 0d,
            costSavedAnnual, costSavedAnnual * 20,
            0d, 0d, 0d, 0d, 0d, 0d,
            0d, 0d, 0d, 0d);

    [Fact]
    public void Same_unit_system_and_currency_passes_values_through_unchanged()
    {
        var tree = CreateTree(co2SequesteredAnnual: 10d, pm25RemovedAnnual: 0.5d, costSavedAnnual: 25d);

        var normalized = DashboardAggregationService.NormalizeTree(tree, "Metric", "USD", 1d);

        Assert.Equal(10d, normalized.CO2SequesteredAnnual, precision: 6);
        Assert.Equal(0.5d, normalized.PM25RemovedAnnual, precision: 6);
        Assert.Equal(25d, normalized.CostSavedAnnual, precision: 6);
        Assert.True(normalized.CountsTowardTotals);
    }

    [Fact]
    public void Mass_stays_in_kilograms_for_a_metric_display_target()
    {
        // Mass values arrive from DashboardReportService already in canonical kilograms (Revit-native
        // Mass-spec parameters) — no stored-unit-system history to resolve, unlike currency.
        var tree = CreateTree(co2SequesteredAnnual: 10d);

        var normalized = DashboardAggregationService.NormalizeTree(tree, "Metric", "USD", 1d);

        Assert.Equal(10d, normalized.CO2SequesteredAnnual, precision: 6);
    }

    [Fact]
    public void Converts_pound_basis_mass_to_pounds_for_an_imperial_display_target()
    {
        // 10 kg displayed as Imperial should read ~22.05 lb.
        var tree = CreateTree(co2SequesteredAnnual: 10d);

        var normalized = DashboardAggregationService.NormalizeTree(tree, "Imperial", "USD", 1d);

        Assert.True(normalized.CO2SequesteredAnnual is > 22.0 and < 22.1,
            $"Expected ~22.05 lb, got {normalized.CO2SequesteredAnnual}");
    }

    [Fact]
    public void Converts_ounce_basis_mass_to_ounces_for_an_imperial_display_target()
    {
        // 1 kg displayed as Imperial should read ~35.27 oz.
        var tree = CreateTree(pm25RemovedAnnual: 1d);

        var normalized = DashboardAggregationService.NormalizeTree(tree, "Imperial", "USD", 1d);

        Assert.True(normalized.PM25RemovedAnnual is > 35.2 and < 35.3,
            $"Expected ~35.27 oz, got {normalized.PM25RemovedAnnual}");
    }

    [Fact]
    public void Converts_currency_via_the_stored_exchange_rate_and_a_fresh_target_rate()
    {
        // Stored as 80 GBP at a rate of 0.8 USD->GBP, i.e. 100 USD originally.
        // Converting to EUR at a fresh rate of 0.9 USD->EUR should give 90 EUR.
        var tree = CreateTree(storedCurrency: "GBP", storedExchangeRateUsed: 0.8d, costSavedAnnual: 80d);

        var normalized = DashboardAggregationService.NormalizeTree(tree, "Metric", "EUR", 0.9d);

        Assert.Equal(90d, normalized.CostSavedAnnual, precision: 6);
    }

    [Fact]
    public void Rows_not_yet_calculated_are_excluded_from_sums_but_not_dropped()
    {
        var calculated = CreateTree(uniqueId: "uid-1", status: "Calculated", costSavedAnnual: 10d);
        var stale = CreateTree(uniqueId: "uid-2", status: "Stale", costSavedAnnual: 999d);

        var normalizedCalculated = DashboardAggregationService.NormalizeTree(calculated, "Metric", "USD", 1d);
        var normalizedStale = DashboardAggregationService.NormalizeTree(stale, "Metric", "USD", 1d);

        Assert.True(normalizedCalculated.CountsTowardTotals);
        Assert.False(normalizedStale.CountsTowardTotals);

        var total = DashboardAggregationService.BuildGrandTotal(
            [normalizedCalculated, normalizedStale], []);

        Assert.Equal(1, total.TreeCount);
        Assert.Equal(1, total.TreesExcludedFromTotals);
        Assert.Equal(10d, total.TreeCostSavedAnnual, precision: 6);
    }

    [Fact]
    public void Aggregates_multiple_trees_of_the_same_species_into_one_subtotal_row()
    {
        var first = DashboardAggregationService.NormalizeTree(
            CreateTree(uniqueId: "uid-1", co2SequesteredAnnual: 10d, costSavedAnnual: 5d), "Metric", "USD", 1d);
        var second = DashboardAggregationService.NormalizeTree(
            CreateTree(uniqueId: "uid-2", co2SequesteredAnnual: 20d, costSavedAnnual: 7d), "Metric", "USD", 1d);

        var subtotals = DashboardAggregationService.AggregateTreesBySpecies([first, second]);

        var subtotal = Assert.Single(subtotals);
        Assert.Equal("ACPL", subtotal.SpeciesCode);
        Assert.Equal(2, subtotal.TreeCount);
        Assert.Equal(30d, subtotal.CO2SequesteredAnnual, precision: 6);
        Assert.Equal(12d, subtotal.CostSavedAnnual, precision: 6);
    }

    [Fact]
    public void Grand_total_keeps_tree_and_floor_benefits_in_separate_fields()
    {
        var tree = DashboardAggregationService.NormalizeTree(
            CreateTree(co2SequesteredAnnual: 10d, costSavedAnnual: 5d), "Metric", "USD", 1d);
        var floor = new DashboardFloorItem(
            "floor-1", 2, "FloorFamily", "FloorType", "WWP_Wetland", "Level 1",
            new DesignOptionInfo(null, null, true), 100d, 15d, 8d, 3d, 30d, 2d, 40d, 0.5d, 0.5d);

        var total = DashboardAggregationService.BuildGrandTotal([tree], [floor]);

        Assert.Equal(10d, total.CO2SequesteredAnnual, precision: 6);
        Assert.Equal(5d, total.TreeCostSavedAnnual, precision: 6);
        Assert.Equal(1, total.FloorCount);
        Assert.Equal(100d, total.FloorAreaSquareMeters, precision: 6);
        Assert.Equal(15d, total.FloorCO2SequesteredAnnual, precision: 6);
        Assert.Equal(8d, total.FloorRunoffAvoidedAnnual, precision: 6);
        Assert.Equal(3d, total.FloorPollutionMassRemovedAnnual, precision: 6);
        Assert.Equal(40d, total.FloorTotalGwp, precision: 6);
        Assert.Equal(30d, total.FloorCostSavedAnnual, precision: 6);
    }
}
