using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>New trees are drawn grown, but their i-Tree forecast still starts at planting.</summary>
public sealed class TreeTimelinesDesignYearTests
{
    private static PlantingInstanceValidationItem Item(int years, int ageAtPlanting, double dbhInches, bool isNew) =>
        new("t1", 1, "Tree", "Acer_Campestre", "ACCA", years, "Good", 3, dbhInches, 51.5, -0.12,
            "Calculated", string.Empty, string.Empty, string.Empty, string.Empty, 25, false, ageAtPlanting, 40, isNew);

    [Fact]
    public void A_new_tree_drawn_at_the_design_year_forecasts_from_the_planting_size()
    {
        var input = TreeTimelines.PlantingInput(Item(years: 30, ageAtPlanting: 5, dbhInches: 8, isNew: true))!;
        Assert.Equal(TrunkGrowthFromITree.PlantingDbhCm, input.DiameterInches * 2.54, 6);
    }

    [Fact]
    public void An_existing_tree_forecasts_from_the_size_it_is_today() =>
        Assert.Equal(8, TreeTimelines.PlantingInput(Item(years: 30, ageAtPlanting: 5, dbhInches: 8, isNew: false))!.DiameterInches, 6);

    [Fact]
    public void A_new_tree_drawn_as_planted_keeps_its_own_size() =>
        Assert.Equal(2.5, TreeTimelines.PlantingInput(Item(years: 5, ageAtPlanting: 5, dbhInches: 2.5, isNew: true))!.DiameterInches, 6);

    [Fact]
    public void An_original_gets_no_copy_for_its_own_year_and_a_leftover_one_goes()
    {
        var plan = GrowthProjectionPlanner.Plan(
            ["new", "old"],
            new HashSet<string> { "new", "old" },
            [new GrowthCopyLink("c25", "new", 25), new GrowthCopyLink("c10", "new", 10)],
            [5, 10, 25],
            new Dictionary<string, int> { ["new"] = 25, ["old"] = 0 });

        Assert.Equal(
            new HashSet<string> { "new:5", "old:5", "old:10", "old:25" },
            plan.ToCreate.Select(copy => $"{copy.SourceUniqueId}:{copy.Year}").ToHashSet());
        Assert.Equal(["c10"], plan.ToUpdate.Select(copy => copy.CopyUniqueId));
        Assert.Equal(["c25"], plan.ToDelete.Select(copy => copy.CopyUniqueId));
    }

    [Theory]
    [InlineData(null, false, 0)]
    [InlineData(null, true, 25)]
    [InlineData(0, true, 25)]
    [InlineData(10, true, 10)]
    [InlineData(5, false, 5)]
    public void The_year_an_original_is_drawn_at(int? projected, bool isNew, int expected) =>
        Assert.Equal(expected, GrowthYearNames.OwnYear(projected, isNew, 25));
}
