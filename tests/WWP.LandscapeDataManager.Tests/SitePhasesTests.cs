using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>A scheme counts what stands after development; the existing site what stood before it.</summary>
public sealed class SitePhasesTests
{
    private static readonly IReadOnlyList<DesignOptionSetItem> Sets =
    [
        new("Variante 1", [new("Option A", true), new("Option 1", false)]),
    ];

    private static readonly DesignOptionInfo Main = new(null, null, true);
    private static readonly DesignOptionInfo OptionA = new("Variante 1", "Option A  <primary>", true);

    private static DashboardTreeItem Tree(DesignOptionInfo option, double co2e, string? role) =>
        DashboardAggregationTestTrees.Item(10) with { DesignOption = option, CO2EquivalentAnnual = co2e, PhaseRole = role };

    private static DashboardFloorItem Floor(DesignOptionInfo option, double area, string? role) =>
        new(Guid.NewGuid().ToString(), 1, "Floor", "Meadow", "Meadow", null, "L0", option, area, 0, 0, 0, 2, 0, 0, 0, 0, PhaseRole: role);

    private static ScenarioPerformanceInputs Inputs() => new(
        [
            Tree(Main, 1, BngPhaseRoles.Retained),   // an existing tree that stays
            Tree(Main, 2, BngPhaseRoles.Lost),       // an existing tree felled for the scheme
            Tree(OptionA, 10, BngPhaseRoles.Created) // a tree the scheme plants
        ],
        [
            Floor(Main, 100, BngPhaseRoles.Retained),
            Floor(Main, 400, BngPhaseRoles.Lost),    // existing meadow under the scheme
            Floor(OptionA, 400, BngPhaseRoles.Created)
        ],
        Sets, null, "Metric", "GBP", 1, 1000, Years: 1, GrowthYear: null, GrowthYearStops: []);

    [Fact]
    public void A_scheme_leaves_out_what_it_demolishes()
    {
        var scheme = ScenarioPerformanceCalculator.Build(DesignScenarios.Primary(Sets), Inputs());

        Assert.Equal(2, scheme.TreeCount);
        Assert.Equal(1 + 10, scheme.Co2EquivalentOutlook, 9);
        Assert.Equal(2, scheme.PlantingAreaCount);
        // Value: £2 a year for each of the two floors that stand, plus the trees' £0.
        Assert.Equal(2 * 2, scheme.EcosystemValueOutlook, 9);
    }

    [Fact]
    public void The_existing_site_is_what_stood_before_development_and_has_no_BNG_change()
    {
        var existing = ScenarioPerformanceCalculator.Build(DesignScenarios.ExistingSite(Sets), Inputs());

        Assert.Equal(2, existing.TreeCount);
        Assert.Equal(1 + 2, existing.Co2EquivalentOutlook, 9);
        Assert.Equal(2, existing.PlantingAreaCount);
        Assert.Equal(0, existing.BngNetChangePercent ?? 0);
    }

    [Fact]
    public void Elements_without_a_phase_role_count_everywhere() =>
        Assert.True(SitePhases.StandsAfter(null) && SitePhases.StoodBefore(null));

    [Fact]
    public void The_default_scenario_is_never_the_existing_site()
    {
        IReadOnlyList<DesignScenario> offered = [DesignScenarios.Primary(Sets), DesignScenarios.ExistingSite(Sets)];
        Assert.False(DesignScenarios.Find(offered, null)!.IsExistingSite);
        Assert.True(DesignScenarios.Find(offered, SitePhases.ExistingSiteName)!.IsExistingSite);
    }

    [Fact]
    public void The_report_has_an_existing_site_when_anything_stood_in_the_first_phase()
    {
        var report = new DashboardReportResult("Doc", "Metric", "GBP", null, null, [Tree(OptionA, 1, BngPhaseRoles.Created)], [Floor(Main, 10, BngPhaseRoles.Retained)], []);
        Assert.True(SitePhases.HasExistingSite(report));
        Assert.False(SitePhases.HasExistingSite(report with { Floors = [] }));
    }
}
