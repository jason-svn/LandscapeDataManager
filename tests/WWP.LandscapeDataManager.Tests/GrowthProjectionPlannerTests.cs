using WWP.LandscapeDataManager.Contracts;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class GrowthProjectionPlannerTests
{
    private static readonly IReadOnlySet<string> Originals = new HashSet<string> { "a", "b", "c" };

    [Fact]
    public void Creates_one_copy_per_original_and_year()
    {
        var plan = GrowthProjectionPlanner.Plan(["a", "b"], Originals, [], [5, 10]);

        Assert.Equal(4, plan.ToCreate.Count);
        Assert.Contains(new GrowthCopyToCreate("b", 10), plan.ToCreate);
        Assert.Empty(plan.ToUpdate);
        Assert.Empty(plan.ToDelete);
    }

    [Fact]
    public void Refreshes_existing_copies_instead_of_duplicating()
    {
        GrowthCopyLink[] copies = [new("a5", "a", 5)];

        var plan = GrowthProjectionPlanner.Plan(["a"], Originals, copies, [5, 10]);

        Assert.Equal([new GrowthCopyToCreate("a", 10)], plan.ToCreate);
        Assert.Equal(copies, plan.ToUpdate);
    }

    [Fact]
    public void Removes_extra_copies_and_copies_of_deleted_originals_but_keeps_other_years()
    {
        GrowthCopyLink[] copies =
        [
            new("a5-1", "a", 5),
            new("a5-2", "a", 5),   // duplicate of the same original and year
            new("gone5", "gone", 5), // original deleted
            new("a20", "a", 20)    // a year not in this run
        ];

        var plan = GrowthProjectionPlanner.Plan(["a"], Originals, copies, [5]);

        Assert.Equal(["a5-1"], plan.ToUpdate.Select(copy => copy.CopyUniqueId));
        Assert.Equal(["a5-2", "gone5"], plan.ToDelete.Select(copy => copy.CopyUniqueId).Order());
        Assert.DoesNotContain(plan.ToDelete, copy => copy.CopyUniqueId == "a20");
    }

    [Fact]
    public void Year_zero_is_never_copied()
    {
        var plan = GrowthProjectionPlanner.Plan(["a"], Originals, [], [0, 7, 7]);

        Assert.Equal([new GrowthCopyToCreate("a", 7)], plan.ToCreate);
    }

    [Theory]
    [InlineData("LIM_Growth_0yr - Baseline", 0)]
    [InlineData("LIM_Growth_0yr - Project start", 0)]
    [InlineData("LIM_Growth_7yr", 7)]
    [InlineData("LIM_Growth_30yr", 30)]
    [InlineData("Trees - 150 Years", null)]
    public void Workset_names_carry_any_growth_year_up_to_the_maximum(string name, int? expected) =>
        Assert.Equal(expected, GrowthYearNames.FromName(name));

    [Theory]
    [InlineData(3, 10, null, 13)]  // no mature size known: grows normally up to 25 years after planting
    [InlineData(3, 40, null, 28)]  // ...then stops at 25 growth years
    [InlineData(null, 25, null, 25)]
    [InlineData(3, 10, 20, 13)]    // still younger than mature
    [InlineData(3, 25, 20, 20)]    // past maturity: capped at the mature age
    [InlineData(25, 5, 20, 20)]    // planted already mature
    public void Modelled_age_is_capped_at_mature_size(int? ageAtPlanting, int growthYear, int? matureAge, int expected) =>
        Assert.Equal(expected, GrowthYearNames.ModelledAge(ageAtPlanting, growthYear, matureAge));

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(25)]
    public void Generated_workset_names_read_back_as_their_year(int year) =>
        Assert.Equal(year, GrowthYearNames.FromName(GrowthYearNames.WorksetName(year)));
}
