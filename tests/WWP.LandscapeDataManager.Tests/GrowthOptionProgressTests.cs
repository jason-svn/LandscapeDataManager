using WWP.LandscapeDataManager.Contracts;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Growth Projections' progress per design option: which still need copies or i-Tree, and whether to make them active first.</summary>
public sealed class GrowthOptionProgressTests
{
    private static int _next;

    private static OrganizerTree Tree(string option, int? linkedYear = null, int? projected = 25, bool isNew = true) =>
        new($"t{++_next}", _next, "WWP_Vegetation_Tree", "Acer_Campestre", 1, "LIM_Growth_25yr", 25, option, $"Set : {option}",
            projected, linkedYear, null, false, false, 0, 0, 0, isNew);

    [Fact]
    public void An_option_with_every_copy_made_and_calculated_is_done_and_others_say_what_to_do()
    {
        var trees = new List<OrganizerTree>
        {
            // Option 1 (active): two originals at 25, copies at 5 and 10.
            Tree("1"), Tree("1"), Tree("1", 5), Tree("1", 5), Tree("1", 10), Tree("1", 10),
            // Option 2: one original, no copies yet.
            Tree("2"),
            // Option 3: copies made, one waiting for i-Tree.
            Tree("3"), Tree("3", 5), Tree("3", 10)
        };
        var waiting = new HashSet<string> { trees[^1].UniqueId };

        var rows = GrowthOptionProgress.Build(trees, [5, 10, 25], 25, "1", waiting);

        Assert.Equal(
            [("Set : 1", 2, 4, 4, GrowthOptionProgressRow.Done), ("Set : 2", 1, 0, 2, GrowthOptionProgressRow.MakeActive), ("Set : 3", 1, 2, 2, GrowthOptionProgressRow.Run)],
            rows.Select(row => (row.OptionLabel, row.Originals, row.CopiesMade, row.CopiesNeeded, row.ToDo)));
        Assert.Equal([5, 10], rows[1].MissingYears);
    }

    [Fact]
    public void The_active_option_with_missing_copies_just_needs_a_run_and_an_original_needs_no_copy_for_its_own_year()
    {
        // An existing tree (year 0) needs copies at 5, 10 and 25; a new one (25) only at 5 and 10.
        var rows = GrowthOptionProgress.Build(
            [Tree("1", projected: null, isNew: false), Tree("1")], [5, 10, 25], 25, "1", new HashSet<string>());

        var row = Assert.Single(rows);
        Assert.Equal((5, GrowthOptionProgressRow.Run), (row.CopiesNeeded, row.ToDo));
    }
}
