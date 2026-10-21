using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>A scenario picks one option per design option set; main-model elements are always in.</summary>
public sealed class DesignScenariosTests
{
    // The sandbox model's sets: Variante 1 (Baseline primary, Option 1, Option 2), Test (Option 1 primary, Option 2).
    private static readonly IReadOnlyList<DesignOptionSetItem> Sets =
    [
        new("Variante 1", [new("Baseline", true), new("Option 1", false), new("Option 2", false)]),
        new("Test", [new("Option 1", true), new("Option 2", false)])
    ];

    private static DesignOptionInfo Main => new(null, null, true);

    private static DesignOptionInfo In(string set, string option, bool primary = false) =>
        new(set, primary ? $"{option}  <primary>" : option, primary);

    [Fact]
    public void The_primary_scenario_shows_every_sets_primary_and_the_main_model()
    {
        var primary = DesignScenarios.Primary(Sets);

        Assert.True(DesignScenarios.Includes(primary, Main, Sets));
        Assert.True(DesignScenarios.Includes(primary, In("Variante 1", "Baseline", primary: true), Sets));
        Assert.True(DesignScenarios.Includes(primary, In("Test", "Option 1", primary: true), Sets));
        Assert.False(DesignScenarios.Includes(primary, In("Variante 1", "Option 1"), Sets));
        Assert.Equal("Variante 1: Baseline + Test: Option 1", DesignScenarios.Describe(primary, Sets));
    }

    [Fact]
    public void Picking_an_option_in_one_set_keeps_the_other_sets_primary()
    {
        // The old filter dropped Test's primary as soon as a Variante 1 option was picked.
        var scenario = new DesignScenario("Scheme B", new Dictionary<string, string> { ["Variante 1"] = "Option 2" });

        Assert.True(DesignScenarios.Includes(scenario, In("Variante 1", "Option 2"), Sets));
        Assert.False(DesignScenarios.Includes(scenario, In("Variante 1", "Baseline", primary: true), Sets));
        Assert.True(DesignScenarios.Includes(scenario, In("Test", "Option 1", primary: true), Sets));
        Assert.False(DesignScenarios.Includes(scenario, In("Test", "Option 2"), Sets));
        Assert.Equal("Scheme B = Variante 1: Option 2 + Test: Option 1", DesignScenarios.Label(scenario, Sets));
    }

    [Fact]
    public void A_deleted_option_falls_back_to_the_primary_and_is_reported()
    {
        var scenario = new DesignScenario("Old", new Dictionary<string, string> { ["Variante 1"] = "Option 9", ["Gone"] = "A" });

        Assert.Equal("Baseline", DesignScenarios.ChosenOption(scenario, Sets[0]));
        Assert.Equal(["Variante 1: Option 9", "Gone: A"], DesignScenarios.MissingChoices(scenario, Sets));
        Assert.Equal(2, DesignScenarios.Normalize(scenario, Sets).Choices.Count);
    }

    [Fact]
    public void Sets_are_rebuilt_from_elements_when_revit_sent_none()
    {
        var sets = DesignScenarios.ResolveSets(null, [Main, In("Test", "Option 1", primary: true), In("Test", "Option 2"), In("Test", "Option 2")]);

        var set = Assert.Single(sets);
        Assert.Equal("Test", set.SetName);
        Assert.Equal(["Option 1", "Option 2"], set.Options.Select(option => option.Name));
        Assert.True(set.Options[0].IsPrimary);
    }

    [Fact]
    public void Saved_scenarios_follow_the_primary_one_unless_one_already_picks_every_primary()
    {
        var saved = new List<DesignScenario> { new("Scheme B", new Dictionary<string, string> { ["Variante 1"] = "Option 2" }) };
        Assert.Equal([DesignScenarios.PrimaryScenarioName, "Scheme B"], DesignScenarios.WithPrimary(saved, Sets).Select(scenario => scenario.Name));

        saved.Add(new DesignScenario("Baseline scheme", new Dictionary<string, string> { ["Variante 1"] = "Baseline", ["Test"] = "Option 1" }));
        Assert.Equal(["Scheme B", "Baseline scheme"], DesignScenarios.WithPrimary(saved, Sets).Select(scenario => scenario.Name));
    }

    [Fact]
    public void Upsert_replaces_by_name_and_remove_deletes_it()
    {
        var saved = DesignScenarios.Upsert(null, new DesignScenario("A", new Dictionary<string, string>()));
        saved = DesignScenarios.Upsert(saved, new DesignScenario("a", new Dictionary<string, string> { ["Test"] = "Option 2" }));

        var only = Assert.Single(saved);
        Assert.Equal("Option 2", only.Choices["Test"]);
        Assert.Empty(DesignScenarios.Remove(saved, "A"));
    }

    [Fact]
    public void Find_prefers_the_named_then_a_baseline_then_the_first()
    {
        IReadOnlyList<DesignScenario> scenarios =
        [
            new("Scheme A", new Dictionary<string, string>()),
            new("Baseline", new Dictionary<string, string>()),
        ];

        Assert.Equal("Scheme A", DesignScenarios.Find(scenarios, "scheme a")!.Name);
        Assert.Equal("Baseline", DesignScenarios.Find(scenarios, null)!.Name);
    }

    [Fact]
    public void A_model_without_design_options_is_one_main_model_scenario()
    {
        var primary = DesignScenarios.Primary([]);

        Assert.True(DesignScenarios.Includes(primary, Main, []));
        Assert.Equal("Main model", DesignScenarios.Describe(primary, []));
    }
}
