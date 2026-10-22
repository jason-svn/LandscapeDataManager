using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>The Dashboard flags a calculated tree whose inputs changed since as Stale, like Tree Calculator.</summary>
public sealed class DashboardCurrentStatusTests
{
    private static PlantingInstanceValidationItem Inputs(int years, double? dbh = 2.1, string storedStatus = "Calculated", int signedYears = 10) =>
        new("uid", 1, "Tree", "Oak", "QURO", years, "Good", 3, dbh, 51.5, -0.1, storedStatus, string.Empty, string.Empty, string.Empty,
            InputSignatureCalculator.Compute("QURO", signedYears, "Good", 3, 2.1, 51.5, -0.1, ITreeBatchCalculator.SignatureEngineVersion));

    private static DashboardTreeItem Tree(string status, PlantingInstanceValidationItem? inputs) =>
        DashboardAggregationTestTrees.Item(1, status) with { Inputs = inputs };

    [Fact]
    public void A_tree_whose_inputs_match_its_calculation_stays_calculated() =>
        Assert.Equal("Calculated", DashboardAggregationService.CurrentStatus(Tree("Calculated", Inputs(years: 10))));

    [Fact]
    public void A_tree_whose_inputs_changed_since_is_stale() =>
        Assert.Equal("Stale", DashboardAggregationService.CurrentStatus(Tree("Calculated", Inputs(years: 15))));

    [Fact]
    public void A_calculated_tree_that_has_lost_an_input_is_stale_too() =>
        Assert.Equal("Stale", DashboardAggregationService.CurrentStatus(Tree("Calculated", Inputs(years: 10, dbh: null))));

    [Fact]
    public void A_sheet_estimate_stays_estimated() =>
        Assert.Equal(WwpSheetTreeEstimates.EstimatedStatus,
            DashboardAggregationService.CurrentStatus(Tree(WwpSheetTreeEstimates.EstimatedStatus, Inputs(years: 10, storedStatus: WwpSheetTreeEstimates.EstimatedStatus))));

    [Fact]
    public void A_report_without_inputs_keeps_the_stored_status() =>
        Assert.Equal("Calculated", DashboardAggregationService.CurrentStatus(Tree("Calculated", null)));

    [Fact]
    public void Stale_trees_still_count_with_their_old_i_tree_results() =>
        Assert.True(DashboardAggregationService.NormalizeTree(Tree("Stale", null), "Metric", "USD", 1).CountsTowardTotals);
}
