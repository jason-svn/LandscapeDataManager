using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Outlooks add up year 1 + … + year N from the best source the model has.</summary>
public sealed class TreeOutlookTests
{
    private static TreeBenefitTimeline Timeline(string key, params double[] carbonKgByYear) =>
        new(key, "QURO", 5.4, carbonKgByYear.Length, "2026-10-21T00:00:00Z",
            carbonKgByYear.Select((carbon, index) => new TreeBenefitYear(index + 1, carbon, carbon / 10, 0, 0, 0, 0, 0, 0, carbon * 2, 0, 0)).ToList());

    private static NormalizedTreeMetrics Tree(double carbonAnnual, string? timelineKey = null, string? workset = null, string status = "Calculated") =>
        DashboardAggregationService.NormalizeTree(
            DashboardAggregationTestTrees.Item(1, status) with { CarbonSequesteredAnnual = carbonAnnual, TimelineKey = timelineKey, Workset = workset },
            "Metric", "USD", 1);

    [Fact]
    public void A_timeline_is_summed_year_by_year_and_held_after_maturity()
    {
        var outlook = new TreeOutlook([Tree(99, "k")], [], [Timeline("k", 1, 2, 3)], "Metric", 1);

        Assert.Equal(TreeOutlookMethod.Timeline, outlook.Method);
        Assert.Equal(1 + 2, outlook.Accumulate(total => total.CarbonSequesteredAnnual, 2), 9);
        Assert.Equal(1 + 2 + 3 + 3 + 3, outlook.Accumulate(total => total.CarbonSequesteredAnnual, 5), 9); // mature at year 3
        Assert.Equal((1 + 2 + 3) * UnitConversions.CarbonToCo2MassRatio, outlook.Accumulate(total => total.CO2EquivalentAnnual, 3), 9);
        Assert.Equal((1 + 2 + 3) * UnitConversions.CarbonToCo2MassRatio * 32 / 44, outlook.Accumulate(total => total.OxygenProducedAnnual, 3), 9);
    }

    [Fact]
    public void Timeline_money_is_converted_at_todays_rate()
    {
        var outlook = new TreeOutlook([Tree(0, "k")], [], [Timeline("k", 1, 1)], "Metric", 0.8);

        Assert.Equal((2 + 2) * 0.8, outlook.Accumulate(total => total.CarbonCostSavedAnnual, 2), 9);
    }

    [Fact]
    public void With_timelines_growth_copies_are_left_out_and_untimed_trees_use_annual_times_years()
    {
        var trees = new[]
        {
            Tree(0, "k", "LIM_Growth_0yr - Baseline"),
            Tree(50, "k2", "LIM_Growth_10yr"),   // a growth copy: the timeline already grows its original
            Tree(4),                              // planted, never forecast
            Tree(7, "k", status: "MissingInput"), // no result: doesn't count
        };

        var outlook = new TreeOutlook(trees, [0, 10], [Timeline("k", 1, 1, 1)], "Metric", 1);

        Assert.Equal(TreeOutlookMethod.Timeline, outlook.Method);
        Assert.Equal(1, outlook.TreesWithTimeline);
        Assert.Equal(3 + 4 * 3, outlook.Accumulate(total => total.CarbonSequesteredAnnual, 3), 9);
    }

    [Fact]
    public void Without_timelines_growth_worksets_are_interpolated()
    {
        var trees = new[] { Tree(2, workset: "LIM_Growth_0yr - Baseline"), Tree(6, workset: "LIM_Growth_10yr") };

        var outlook = new TreeOutlook(trees, [0, 10], null, "Metric", 1);

        Assert.Equal(TreeOutlookMethod.GrowthWorksets, outlook.Method);
        Assert.Equal((2 + 6) / 2d * 10, outlook.Accumulate(total => total.CarbonSequesteredAnnual, 10), 9);
    }

    [Fact]
    public void Without_growth_it_is_annual_times_years()
    {
        var outlook = new TreeOutlook([Tree(3)], [], null, "Metric", 1);

        Assert.Equal(TreeOutlookMethod.AnnualTimesYears, outlook.Method);
        Assert.Equal(30, outlook.Accumulate(total => total.CarbonSequesteredAnnual, 10), 9);
    }

    [Fact]
    public void A_growth_years_benefits_come_from_each_planted_trees_forecast_for_that_year()
    {
        var trees = new[]
        {
            Tree(1, "k", "LIM_Growth_0yr - Baseline") with { CarbonSequesteredLifetimeTotal = 42 },
            Tree(1, "k2", "LIM_Growth_10yr"),   // a copy that didn't grow: left out
            Tree(4),                            // planted, never forecast: keeps its stored figure
        };
        var outlook = new TreeOutlook(trees, [0, 10], [Timeline("k", 1, 2, 3)], "Metric", 1);

        var year0 = outlook.InYear(0);
        var year2 = outlook.InYear(2);
        var year10 = outlook.InYear(10);

        Assert.Equal(2, year10.Count);
        Assert.Equal([1d, 4d], year0.Select(tree => tree.CarbonSequesteredAnnual));
        Assert.Equal([2d, 4d], year2.Select(tree => tree.CarbonSequesteredAnnual));
        Assert.Equal([3d, 4d], year10.Select(tree => tree.CarbonSequesteredAnnual)); // mature after year 3
        Assert.Equal(42, year10[0].CarbonSequesteredLifetimeTotal);                    // lifetime is the whole forecast
    }

    [Fact]
    public void A_timeline_shorter_than_the_outlook_repeats_its_last_year() =>
        Assert.Equal(1 + 4 + 4 + 4, TimelineSum.Over(Timeline("k", 1, 4), 4).CarbonKg, 9);
}
