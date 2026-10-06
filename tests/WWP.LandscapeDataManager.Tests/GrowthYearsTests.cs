using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class GrowthYearsTests
{
    [Theory]
    [InlineData("Trees - 15 Years", 15)]
    [InlineData("LIM_Growth_10yr", 10)]
    [InlineData("Growth Timeline : 5 Years", 5)]
    [InlineData("Year 25", 25)]
    [InlineData("Trees - 150 Years", null)] // not a growth-year stop
    [InlineData("Workset1", null)]
    [InlineData("Shared Levels and Grids", null)]
    [InlineData(null, null)]
    public void Reads_the_growth_year_from_a_workset_name(string? name, int? expected) =>
        Assert.Equal(expected, GrowthYearNames.FromName(name));

    [Theory]
    [InlineData(5, 10, 15)]
    [InlineData(null, 10, 10)]
    [InlineData(-3, 5, 5)]
    public void Actual_age_adds_the_age_at_planting(int? ageAtPlanting, int growthYear, int expected) =>
        Assert.Equal(expected, GrowthYearNames.ActualAge(ageAtPlanting, growthYear));

    [Fact]
    public void A_growth_year_shows_its_own_trees_and_trees_on_no_growth_year_workset()
    {
        var trees = new[] { Tree("t5", "Trees - 5 Years"), Tree("t10", "Trees - 10 Years"), Tree("existing", "Existing Trees"), Tree("solo", null) };

        Assert.Equal([5, 10], GrowthYears.InModel(trees));
        Assert.Equal(["t10", "existing", "solo"], trees.Where(tree => GrowthYears.CountsIn(tree, 10)).Select(tree => tree.UniqueId));
    }

    private static DashboardTreeItem Tree(string id, string? workset) =>
        new(id, 1, "F", "T", null, null, null, null, null, null, null, 0, null, new DesignOptionInfo(null, null, true), "Calculated", "Metric", "USD", 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, workset);
}
