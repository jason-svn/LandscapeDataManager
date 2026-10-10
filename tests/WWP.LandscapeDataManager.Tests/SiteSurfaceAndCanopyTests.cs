using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Canopy Cover and Softscape (soft vs hard) Surface Ratio, end to end through BuildSiteKpiSummary.</summary>
public sealed class SiteSurfaceAndCanopyTests
{
    // ---- Hard vs soft classification of realistic landscape type names ----

    [Theory]
    [InlineData("Permeable Block Paving")]   // "paving" alone would say hard
    [InlineData("Porous Asphalt")]           // "asphalt" alone would say hard
    [InlineData("Grasscrete")]               // contains "concrete"
    [InlineData("Grass Reinforced Paving")]
    [InlineData("Resin Bound Gravel (permeable)")]
    [InlineData("Sedum Green Roof")]
    [InlineData("Extensive Green Roof")]
    [InlineData("Rain Garden")]
    [InlineData("Bioretention Planter")]
    [InlineData("Self-binding Hoggin")]
    [InlineData("Bark Mulch")]
    [InlineData("Amenity Grassland")]
    public void Soft_and_permeable_surfaces_count_as_pervious(string ldsType) =>
        Assert.Equal("Pervious", SurfaceClassCatalog.Infer(ldsType));

    [Theory]
    [InlineData("Artificial Grass")]         // contains "grass" but is a synthetic hard surface
    [InlineData("Synthetic Turf")]
    [InlineData("Wet-pour Rubber Play Surface")]
    [InlineData("Natural Stone Setts")]
    [InlineData("Tarmac Footpath")]
    [InlineData("Block Paving")]
    [InlineData("Porcelain Tile")]
    public void Hard_surfaces_count_as_impervious(string ldsType) =>
        Assert.Equal("Impervious", SurfaceClassCatalog.Infer(ldsType));

    // ---- The percentages ----

    [Fact]
    public void Softscape_ratio_is_pervious_area_over_site_area_and_excludes_hard_and_unclassified()
    {
        var floors = new[]
        {
            Floor("Lawn", 500),
            Floor("Sedum Green Roof", 100),
            Floor("Permeable Block Paving", 150),
            Floor("Granite Paving", 250),
            Floor("Water Feature", 50),         // unclassified: neither soft nor hard
        };

        var summary = DashboardAggregationService.BuildSiteKpiSummary([], floors, [], 2000, null);

        Assert.Equal(750, summary.PerviousAreaSquareMeters);
        Assert.Equal(37.5, summary.SoftscapeSurfaceRatioPercent);
    }

    [Fact]
    public void An_explicit_surface_class_tag_overrides_the_name()
    {
        var floors = new[] { Floor("Concrete Paving", 100, surfaceClass: "Pervious"), Floor("Lawn", 100, surfaceClass: "Impervious") };

        var summary = DashboardAggregationService.BuildSiteKpiSummary([], floors, [], 1000, null);

        Assert.Equal(100, summary.PerviousAreaSquareMeters);
    }

    [Fact]
    public void Canopy_cover_is_total_crown_area_over_site_area()
    {
        // Crown areas as Revit computes them: pi * (width / 2)^2 for 4 m and 6 m crowns.
        var trees = new[] { Tree(Math.PI * 4), Tree(Math.PI * 9) };

        var summary = DashboardAggregationService.BuildSiteKpiSummary(trees, [], [], 500, null);

        Assert.Equal(Math.PI * 13, summary.CanopyAreaSquareMeters, 6);
        Assert.Equal(Math.PI * 13 / 500 * 100, summary.CanopyCoverPercent!.Value, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    public void Without_a_site_area_both_percentages_are_unknown_not_zero(double? siteArea)
    {
        var summary = DashboardAggregationService.BuildSiteKpiSummary([Tree(30)], [Floor("Lawn", 100)], [], siteArea, null);

        Assert.Null(summary.CanopyCoverPercent);
        Assert.Null(summary.SoftscapeSurfaceRatioPercent);
        Assert.Equal(100, summary.PerviousAreaSquareMeters); // the areas are still reported
    }

    private static DashboardFloorItem Floor(string ldsType, double area, string? surfaceClass = null) =>
        new(Guid.NewGuid().ToString(), 1, "Floor", ldsType, ldsType, surfaceClass, "L0", new DesignOptionInfo(null, null, true),
            area, 0, 0, 0, 0, 0, 0, 0, 0);

    private static NormalizedTreeMetrics Tree(double canopyAreaSquareMeters) =>
        DashboardAggregationService.NormalizeTree(
            new DashboardTreeItem(Guid.NewGuid().ToString(), 1, "Tree", "Oak", "QURO", "Oak", null, null, null, null, null,
                canopyAreaSquareMeters, "L0", new DesignOptionInfo(null, null, true), "Calculated", "Metric", "USD", 1,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            "Metric", "USD", 1);
}
