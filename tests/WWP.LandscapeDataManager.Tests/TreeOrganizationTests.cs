using WWP.LandscapeDataManager.Contracts;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Growth Projections' Sort &amp; check trees: which workset each tree belongs on, and which trees overlap.</summary>
public sealed class TreeOrganizationTests
{
    private static OrganizerTree Tree(
        long id,
        int? worksetYear = 0,
        int? projected = null,
        int? linkedYear = null,
        string? linkedSource = null,
        bool isSource = false,
        bool inGroup = false,
        double x = 0,
        double y = 0,
        long typeId = 1,
        string option = GrowthOptionKeys.MainModel,
        bool isNew = false) =>
        new($"uid-{id}", id, "WWP_Vegetation_Tree", $"Type{typeId}", typeId,
            worksetYear is { } year ? GrowthYearNames.WorksetName(year) : "Planting", worksetYear,
            option, option == GrowthOptionKeys.MainModel ? "Main model" : option,
            projected, linkedYear, linkedSource, isSource, inGroup, x, y, 0, isNew);

    [Fact]
    public void A_tree_moves_to_the_workset_of_its_projected_year()
    {
        var plan = TreeSortPlanner.Plan([Tree(1, worksetYear: 0, projected: 10), Tree(2, worksetYear: 10, projected: 10)]);

        var move = Assert.Single(plan.Moves);
        Assert.Equal((1L, 10), (move.Tree.ElementId, move.Year));
    }

    [Fact]
    public void A_growth_projections_copy_belongs_to_its_link_year_whatever_its_parameter_says()
    {
        var plan = TreeSortPlanner.Plan([Tree(1, worksetYear: 5, projected: 5, linkedYear: 10, linkedSource: "uid-9")]);
        Assert.Equal(10, Assert.Single(plan.Moves).Year);
    }

    [Fact]
    public void Without_a_projected_year_the_phase_decides_existing_today_and_new_at_the_design_year()
    {
        var plan = TreeSortPlanner.Plan([
            Tree(1, worksetYear: null),                                // existing, on no growth workset
            Tree(2, worksetYear: 0, isNew: true),                      // new, still on year 0
            Tree(3, worksetYear: 0, projected: 0, isNew: true),        // new, copied from a year-0 tree
            Tree(4, worksetYear: 25, projected: 25, isNew: true),      // already where it belongs
            Tree(5, worksetYear: 25, isNew: true)                      // right workset, year not written yet
        ], designYear: 25);

        Assert.Equal([(1L, 0), (2L, 25), (3L, 25), (5L, 25)], plan.Moves.Select(move => (move.Tree.ElementId, move.Year)));
        Assert.Equal(4, plan.YearFromPhase);
    }

    [Fact]
    public void A_projected_year_set_by_hand_wins_over_the_design_year() =>
        Assert.Equal(10, Assert.Single(TreeSortPlanner.Plan([Tree(1, worksetYear: 0, projected: 10, isNew: true)], designYear: 25).Moves).Year);

    [Fact]
    public void Grouped_or_out_of_range_trees_are_reported_not_moved()
    {
        var plan = TreeSortPlanner.Plan([
            Tree(2, worksetYear: 0, projected: 10, inGroup: true),
            Tree(3, worksetYear: 0, projected: 150)
        ]);

        Assert.Empty(plan.Moves);
        Assert.Equal([2L, 3L], plan.Problems.Select(problem => problem.Tree.ElementId));
    }

    [Fact]
    public void Trees_in_the_same_place_on_the_same_workset_and_option_overlap_and_the_oldest_is_kept()
    {
        var groups = TreeOverlapFinder.Find([Tree(7, x: 0.05), Tree(3), Tree(5, x: 0.08, y: 0.02), Tree(9, x: 5)], 0.1, sameTypeOnly: true);

        var group = Assert.Single(groups);
        Assert.Equal(3L, group.Keep.ElementId);
        Assert.Equal([5L, 7L], group.Extras.Select(tree => tree.ElementId).Order());
    }

    [Fact]
    public void Trees_on_other_worksets_options_or_types_never_overlap()
    {
        var trees = new[]
        {
            Tree(1),
            Tree(2, worksetYear: 10, linkedYear: 10, linkedSource: "uid-1"), // a copy, on its own workset
            Tree(3, option: "123"),
            Tree(4, typeId: 2)
        };

        Assert.Empty(TreeOverlapFinder.Find(trees, 0.1, sameTypeOnly: true));
        Assert.Single(TreeOverlapFinder.Find(trees, 0.1, sameTypeOnly: false));
    }

    [Fact]
    public void The_original_that_copies_project_is_kept_and_a_copy_of_a_deleted_tree_is_not()
    {
        var groups = TreeOverlapFinder.Find([
            // Baseline: a hand-made duplicate (older) on top of the original the copies project.
            Tree(1), Tree(2, isSource: true),
            // Year 10: the original's copy, and a copy made from the duplicate.
            Tree(20, worksetYear: 10, linkedYear: 10, linkedSource: "uid-2"),
            Tree(10, worksetYear: 10, linkedYear: 10, linkedSource: "uid-1")
        ], 0.1, sameTypeOnly: true);

        Assert.Equal([(2L, 1L), (20L, 10L)], groups.Select(group => (group.Keep.ElementId, group.Extras.Single().ElementId)));
    }

    [Fact]
    public void A_hand_placed_tree_on_top_of_a_growth_projections_copy_is_the_one_to_delete()
    {
        var group = Assert.Single(TreeOverlapFinder.Find([
            Tree(1, worksetYear: 10, projected: 10),
            Tree(50, worksetYear: 10, linkedYear: 10, linkedSource: "uid-9")
        ], 0.1, sameTypeOnly: true));

        Assert.Equal((50L, 1L), (group.Keep.ElementId, group.Extras.Single().ElementId));
    }

    [Fact]
    public void A_chain_of_close_trees_is_one_group()
    {
        var group = Assert.Single(TreeOverlapFinder.Find([Tree(1), Tree(2, x: 0.09), Tree(3, x: 0.18)], 0.1, sameTypeOnly: true));
        Assert.Equal(2, group.Extras.Count);
        Assert.Equal(0.18, group.SpreadMeters, 6);
    }
}
