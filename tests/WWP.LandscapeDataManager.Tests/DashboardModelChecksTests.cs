using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>The Dashboard's model checks: what Health Check used to list.</summary>
public sealed class DashboardModelChecksTests
{
    private static readonly DesignOptionInfo Main = new(null, null, true);

    private static DashboardTreeItem Tree(string id, bool inGroup) =>
        DashboardAggregationTestTrees.Item(10) with
        {
            UniqueId = id,
            Inputs = new PlantingInstanceValidationItem(id, 1, "Tree", "Oak", "QURO", 10, "Good", 5, 2, null, null, "Calculated", "", "", "", "", InGroup: inGroup)
        };

    private static DashboardFloorItem Floor(string id, string? ldsType, bool? calculated, bool structural = false, string? role = BngPhaseRoles.Created) =>
        new(id, 1, "Floor", "Meadow", ldsType, null, "L0", Main, 100, 0, 0, 0, 0, 0, 0, 0, 0,
            IsStructural: structural, PhaseRole: role, HasLdsResult: calculated);

    private static DashboardReportResult Report(IReadOnlyList<DashboardTreeItem> trees, IReadOnlyList<DashboardFloorItem> floors, IReadOnlyList<string>? warnings = null) =>
        new("Doc", "Metric", "GBP", null, null, trees, floors, [], ModelWarnings: warnings);

    [Fact]
    public void A_clean_model_has_nothing_to_fix()
    {
        var checks = DashboardModelChecks.For(Report([Tree("t1", false)], [Floor("f1", "Meadow", true)]));

        Assert.False(checks.HasIssues);
        Assert.Empty(checks.UniqueIds);
    }

    [Fact]
    public void Lists_setup_warnings_grouped_trees_and_floors_not_calculated()
    {
        var checks = DashboardModelChecks.For(Report(
            [Tree("t1", true), Tree("t2", false)],
            [Floor("f1", null, false), Floor("f2", "Meadow", false), Floor("f3", "Meadow", true)],
            ["LIM's shared parameters aren't set up for Floors yet."]));

        Assert.Equal(4, checks.Lines.Count);
        Assert.StartsWith("LIM's shared parameters", checks.Lines[0]);
        Assert.StartsWith("1 tree(s) are in a group", checks.Lines[1]);
        Assert.StartsWith("1 floor(s) have no landscape type", checks.Lines[2]);
        Assert.StartsWith("1 floor(s) are matched", checks.Lines[3]);
        Assert.Equal(["t1", "f1", "f2"], checks.UniqueIds);
    }

    [Fact]
    public void Skips_slabs_excluded_floors_and_an_older_connector()
    {
        var checks = DashboardModelChecks.For(Report([],
        [
            Floor("slab", null, false, structural: true),
            Floor("excluded", null, false, role: BngPhaseRoles.Excluded),
            Floor("old", null, calculated: null)
        ]));

        Assert.False(checks.HasIssues);
    }
}
