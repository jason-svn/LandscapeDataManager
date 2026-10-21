using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Each scenario's numbers come from its own options plus the main model.</summary>
public sealed class ScenarioPerformanceTests
{
    private static readonly IReadOnlyList<DesignOptionSetItem> Sets =
    [
        new("Variante 1", [new("Baseline", true), new("Option 1", false)]),
    ];

    private static DashboardTreeItem Tree(DesignOptionInfo option, double co2e, double maintenance = 0) =>
        DashboardAggregationTestTrees.Item(10) with { DesignOption = option, CO2EquivalentAnnual = co2e, CostSavedAnnual = 5, MaintenanceCostAnnual = maintenance };

    private static DashboardFloorItem Floor(DesignOptionInfo option, string ldsType, double area, double maintenance = 0) =>
        new(Guid.NewGuid().ToString(), 1, "Floor", ldsType, ldsType, null, "L0", option, area, 0, 0, 0, 2, 0, 0, 0, 0, MaintenanceCostAnnual: maintenance);

    [Fact]
    public void Each_scenario_counts_the_main_model_and_only_its_chosen_options()
    {
        var main = new DesignOptionInfo(null, null, true);
        var baseline = new DesignOptionInfo("Variante 1", "Baseline  <primary>", true);
        var option1 = new DesignOptionInfo("Variante 1", "Option 1", false);
        var inputs = new ScenarioPerformanceInputs(
            [Tree(main, 1, maintenance: 3), Tree(baseline, 10), Tree(option1, 100), Tree(option1, 100)],
            [Floor(main, "Lawn", 300), Floor(option1, "Granite Paving", 100, maintenance: 4)],
            Sets, null, "Metric", "GBP", 1, 1000, Years: 10, GrowthYear: null, GrowthYearStops: []);

        var primary = ScenarioPerformanceCalculator.Build(DesignScenarios.Primary(Sets), inputs);
        var option = ScenarioPerformanceCalculator.Build(new DesignScenario("Option 1", new Dictionary<string, string> { ["Variante 1"] = "Option 1" }), inputs);

        Assert.Equal(2, primary.TreeCount);
        Assert.Equal((1 + 10) * 10, primary.Co2EquivalentOutlook, 9);
        Assert.Equal(100, primary.GreenspaceRatioPercent);
        Assert.Equal(3, option.TreeCount);
        Assert.Equal((1 + 100 + 100) * 10, option.Co2EquivalentOutlook, 9);
        Assert.Equal(75, option.GreenspaceRatioPercent);

        // Value: £5 per tree per year plus £2 per floor per year; maintenance £3 tree + £4 floor per year.
        Assert.Equal((3 * 5 + 2 * 2) * 10, option.EcosystemValueOutlook, 9);
        Assert.Equal((3 + 4) * 10, option.MaintenanceCostOutlook, 9);
        Assert.Equal(option.EcosystemValueOutlook - option.MaintenanceCostOutlook, option.NetValueOutlook, 9);
    }

    [Theory]
    [InlineData(120d, 100d, 20d, 20d)]
    [InlineData(80d, 100d, -20d, -20d)]
    public void Differences_are_absolute_and_relative(double value, double reference, double absolute, double percent)
    {
        var (deltaAbsolute, deltaPercent) = ScenarioPerformanceCalculator.Difference(value, reference);

        Assert.Equal(absolute, deltaAbsolute!.Value, 9);
        Assert.Equal(percent, deltaPercent!.Value, 9);
    }

    [Fact]
    public void A_zero_reference_has_no_percentage()
    {
        Assert.Null(ScenarioPerformanceCalculator.Difference(5, 0).Percent);
        Assert.Null(ScenarioPerformanceCalculator.Difference(null, 5).Absolute);
    }
}
