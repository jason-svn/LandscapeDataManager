using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Connectivity per design scenario: which patches a scenario has, and its saved connected habitat.</summary>
public sealed class ConnectivityScenariosTests
{
    private static readonly IReadOnlyList<DesignOptionSetItem> Sets =
        [new("Test", [new DesignOptionItem("Option 1", true), new DesignOptionItem("Option 2", false)])];

    private static ConnectivityPatchItem Tree(string id, string? option, string role = BngPhaseRoles.Created) =>
        new(id, 1, "Planting", "Tree", "Oak", 12, new ConnectivityPoint(0, 0), 2, null, null, null, false, true, null, null,
            option is null ? new DesignOptionInfo(null, null, false) : new DesignOptionInfo("Test", option, option == "Option 1"), role);

    private static readonly DesignScenario SchemeB = new("Scheme B", new Dictionary<string, string> { ["Test"] = "Option 2" });

    [Fact]
    public void Offers_the_primary_options_the_saved_scenarios_and_the_existing_site()
    {
        var items = new[] { Tree("existing", null, BngPhaseRoles.Retained), Tree("a", "Option 1") };

        var offered = ConnectivityScenarios.Offered(items, Sets, [SchemeB]);

        Assert.Equal(3, offered.Count);
        Assert.Contains(offered, scenario => scenario.Name == "Scheme B");
        Assert.True(offered[^1].IsExistingSite);
    }

    [Fact]
    public void A_scenario_counts_its_options_and_what_stands_in_it()
    {
        var items = new[]
        {
            Tree("kept", null, BngPhaseRoles.Retained), Tree("felled", null, BngPhaseRoles.Lost),
            Tree("one", "Option 1"), Tree("two", "Option 2")
        };

        Assert.Equal(["kept", "two"], ConnectivityScenarios.InScenario(items, SchemeB, Sets).Select(item => item.UniqueId));
        Assert.Equal(["kept", "felled"], ConnectivityScenarios.InScenario(items, DesignScenarios.ExistingSite(Sets), Sets).Select(item => item.UniqueId));
    }

    [Fact]
    public void An_older_connector_s_built_design_offers_only_the_primary_options_and_keeps_every_item()
    {
        var items = new[] { Tree("a", "Option 1") with { DesignOption = null, PhaseRole = null } };

        Assert.Single(ConnectivityScenarios.Offered(items, Sets, [SchemeB]));
        Assert.Single(ConnectivityScenarios.InScenario(items, SchemeB, Sets));
    }

    [Fact]
    public void Connected_habitat_is_the_mean_equivalent_connected_area_over_the_habitat_area()
    {
        var patches = new[] { new ConnectivityPatch("a", 300, new PlanPoint(0, 0), 10), new ConnectivityPatch("b", 100, new PlanPoint(50, 0), 5) };
        var run = new ConnectivityRunResult([], [100d, 300d]);

        var result = ConnectivityScenarios.Summarize(SchemeB, ConnectivitySettings.Default with { DispersalDistancesMeters = [50, 250] }, patches, run, 25, DateTimeOffset.UnixEpoch);

        Assert.Equal((400d, 2, 200d, 50d), (result.HabitatAreaSquareMeters, result.Patches, result.EquivalentConnectedArea, result.ConnectedPercent));
    }

    [Fact]
    public void Saving_replaces_a_scenario_s_result_and_keeps_the_others()
    {
        ConnectivityScenarioResult Result(string scenario, double ec) => new(scenario, false, [250], 0.5, [ec], 1000, 10, "2026-10-22T00:00:00Z");
        var saved = ConnectivityScenarios.Save(null, [Result("Primary options", 100), Result("Scheme B", 200)]);

        var resaved = ConnectivityScenarios.Save(saved, [Result("Scheme B", 400)]);

        Assert.Equal([("Primary options", 100d), ("Scheme B", 400d)], resaved.Select(result => (result.Scenario, result.EquivalentConnectedArea)));
        Assert.Equal(40d, ConnectivityScenarios.For(resaved, SchemeB)!.ConnectedPercent);
    }

    [Fact]
    public void A_fully_connected_network_is_100_percent_connected()
    {
        // Two touching patches: p* = 1 everywhere, so EC = the habitat area.
        var patches = new[] { new ConnectivityPatch("a", 100, new PlanPoint(0, 0), 5), new ConnectivityPatch("b", 100, new PlanPoint(10, 0), 5) };
        var run = ConnectivityCalculator.Calculate(patches, new ConnectivityOptions([250]));

        var result = ConnectivityScenarios.Summarize(SchemeB, ConnectivitySettings.Default, patches, run, 25, DateTimeOffset.UnixEpoch);

        Assert.Equal(100d, result.ConnectedPercent, 6);
    }
}
