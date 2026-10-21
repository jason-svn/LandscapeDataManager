using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Greenspace ratio = pervious ÷ (pervious + impervious); unclassified floors and building slabs stay out.</summary>
public sealed class GreenspaceRatioTests
{
    [Fact]
    public void Greenspace_ratio_ignores_unclassified_floors_and_building_slabs()
    {
        var floors = new[]
        {
            Floor("Lawn", "Lawn", 600),
            Floor("Granite Paving", "Granite Paving", 200),
            Floor(null, "Water Feature", 50),               // nothing says what it is
            Floor(null, "Structural Slab 250mm", 1000),     // a building floor
            Floor(null, "Generic 150mm", 80, structural: true),
        };

        var summary = DashboardAggregationService.BuildSiteKpiSummary([], floors, [], null, null);

        Assert.Equal(75, summary.GreenspaceRatioPercent!.Value, 9);
        Assert.Equal(600, summary.PerviousAreaSquareMeters);
        Assert.Equal(200, summary.ImperviousAreaSquareMeters);
        Assert.Equal(1, summary.UnclassifiedFloorCount);
        Assert.Equal(50, summary.UnclassifiedAreaSquareMeters);
        Assert.Equal(2, summary.BuildingSlabCount);
        Assert.Equal(1080, summary.BuildingSlabAreaSquareMeters);
    }

    [Theory]
    [InlineData(null, "Concrete Slab 200", false, true)]
    [InlineData(null, "Suspended floor", false, true)]
    [InlineData(null, "Paving Slab 600x600", false, false)]  // a landscape paving slab
    [InlineData("Granite", "Structural Slab", false, false)] // matched to an LDS type: landscape
    [InlineData(null, "Lawn", true, true)]                    // Revit says structural
    [InlineData("Asphalt - Asphalt", "WWP_Surfaces_Building", false, true)] // a building type matched to a stand-in LDS type
    public void Building_slabs_are_told_apart_from_landscape(string? ldsType, string typeName, bool structural, bool expected) =>
        Assert.Equal(expected, SurfaceClassCatalog.IsBuildingSlab(null, ldsType, structural, typeName));

    [Fact]
    public void A_building_tag_excludes_and_any_other_tag_includes()
    {
        Assert.True(SurfaceClassCatalog.IsBuildingSlab("Building", "Lawn", false, "Lawn"));
        Assert.False(SurfaceClassCatalog.IsBuildingSlab("Pervious", null, true, "Structural slab"));
    }

    [Fact]
    public void Without_classified_floors_the_ratio_is_unknown()
    {
        var summary = DashboardAggregationService.BuildSiteKpiSummary([], [Floor(null, "Water Feature", 50)], [], null, null);

        Assert.Null(summary.GreenspaceRatioPercent);
    }

    [Fact]
    public void Connectivity_bands_are_counted_with_the_unassessed_last()
    {
        var floors = new[] { Floor("Lawn", "Lawn", 10) with { ConnectivityBand = 4 }, Floor("Lawn", "Lawn", 10) };
        var trees = new[]
        {
            DashboardAggregationService.NormalizeTree(DashboardAggregationTestTrees.Item(1) with { ConnectivityBand = 1 }, "Metric", "USD", 1),
            DashboardAggregationService.NormalizeTree(DashboardAggregationTestTrees.Item(1) with { ConnectivityBand = 4 }, "Metric", "USD", 1),
        };

        var summary = DashboardAggregationService.BuildSiteKpiSummary(trees, floors, [], null, null);

        Assert.Equal([1, 0, 0, 2, 1], summary.ConnectivityBandCounts);
    }

    private static DashboardFloorItem Floor(string? ldsType, string typeName, double area, bool structural = false) =>
        new(Guid.NewGuid().ToString(), 1, "Floor", typeName, ldsType, null, "L0", new DesignOptionInfo(null, null, true),
            area, 0, 0, 0, 0, 0, 0, 0, 0, IsStructural: structural);
}
