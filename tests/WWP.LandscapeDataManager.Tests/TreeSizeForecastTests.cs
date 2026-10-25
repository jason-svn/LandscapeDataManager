using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>The planting family's size formulas, checked against what the family draws in the sandbox.</summary>
public sealed class TreeSizeForecastTests
{
    /// <summary>The sandbox's values are read to 0.1 mm.</summary>
    private const double WithinHalfAMillimetre = 0.0005;

    private static TreeGrowthType Type(string name, double maxHeight, double maxWidth, double heightRate, double widthRate, double trunkRate, int matureAge) =>
        new("WWP_Vegetation_Tree", name, maxHeight, maxWidth, heightRate, widthRate, trunkRate, matureAge);

    private static readonly TreeGrowthType FieldMaple = Type("Acer_Campestre", 15, 7, 0.41, 0.24, 0.339, 37);
    private static readonly TreeGrowthType WildCherry = Type("Prunus_Avium", 20, 12, 0.34, 0.24, 0.564, 59);
    private static readonly TreeGrowthType Apple = Type("Malus_Domestica", 8, 3, 0.6, 0.5, 0, 14);

    private static TreeBenefitYear Year(int year, double heightM, double crownWidthM) =>
        new(year, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5.4 + year, heightM, Math.PI * crownWidthM * crownWidthM / 4);

    [Fact]
    public void Without_an_iTree_size_a_tree_grows_by_its_types_rates()
    {
        // The sandbox's Acer_Campestre drawn at 25 years: trunk 3.4167, crown 6.8333 × 6.0 m, DBH 138.8 mm.
        var size = TreeSizeForecast.At(FieldMaple, 0, 25, 0, 0);

        Assert.Equal(3.4167, size.TrunkHeightM, WithinHalfAMillimetre);
        Assert.Equal(6.8333, size.CrownHeightM, WithinHalfAMillimetre);
        Assert.Equal(3.0, size.CrownRadiusM, WithinHalfAMillimetre);
        Assert.Equal(0.1388 / 2, size.TrunkRadiusM, WithinHalfAMillimetre);
    }

    [Fact]
    public void An_iTree_size_is_drawn_as_it_is()
    {
        // The sandbox's Prunus_Avium with i-Tree's 8.4536 m height and 5.987 m crown: trunk 2.8179, crown 5.6357, DBH 195 mm.
        var size = TreeSizeForecast.At(WildCherry, 0, 25, 8.4536, 5.987);

        Assert.Equal(2.8179, size.TrunkHeightM, WithinHalfAMillimetre);
        Assert.Equal(5.6357, size.CrownHeightM, WithinHalfAMillimetre);
        Assert.Equal(5.987 / 2, size.CrownRadiusM, WithinHalfAMillimetre);
        Assert.Equal(0.195 / 2, size.TrunkRadiusM, WithinHalfAMillimetre);
    }

    [Fact]
    public void Growth_stops_at_mature_age_and_the_types_maximum_size()
    {
        // Mature at 14 years: 8.4 m of growth capped at 8 m, a 7 m crown at 3 m; no trunk rate, so the planting DBH.
        var size = TreeSizeForecast.At(Apple, 0, 25, 0, 0);

        Assert.Equal(8.0, size.HeightM, 9);
        Assert.Equal(1.5, size.CrownRadiusM, 9);
        Assert.Equal(TreeSizeForecast.PlantingDbhM / 2, size.TrunkRadiusM, 9);
    }

    [Theory]
    [InlineData(25, 0, 37, 25)]
    [InlineData(40, 0, 37, 37)]  // held at mature age
    [InlineData(10, 50, 37, 50)] // planted older than its mature age: stays at its planting age
    [InlineData(30, 2, 0, 27)]   // no mature age: 25 years after planting
    [InlineData(0, 0, 37, 1)]    // never below 1
    public void Growth_years_follow_the_family(int projectedYears, double ageAtPlanting, int matureAge, double expected) =>
        Assert.Equal(expected, TreeSizeForecast.GrowthYears(projectedYears, ageAtPlanting, matureAge), 9);

    [Fact]
    public void A_forecast_gives_each_year_its_iTree_size_and_holds_it_after_maturity()
    {
        var timeline = new TreeBenefitTimeline("k", "ACCA", 5.4, 2, "2026-10-24T00:00:00Z", [Year(1, 6, 2), Year(2, 9, 4)]);

        var profile = TreeSizeForecast.Profile(FieldMaple, 0, timeline, 5);

        Assert.Equal(6, profile.Count);
        Assert.Equal(6, profile[0].HeightM, 9);     // year 0, as planted: the forecast's first year
        Assert.Equal(1, profile[1].CrownRadiusM, 9);
        Assert.Equal(9, profile[2].HeightM, 9);
        Assert.Equal(2, profile[5].CrownRadiusM, 9); // past maturity: the last year
    }

    [Fact]
    public void A_forecast_without_a_crown_takes_its_height_and_the_types_crown_rate()
    {
        var timeline = new TreeBenefitTimeline("k", "ACCA", 5.4, 1, "2026-10-22T00:00:00Z", [Year(1, 6.3, 0)]);

        var size = TreeSizeForecast.At(FieldMaple, 0, 10, timeline);

        Assert.Equal(6.3, size.HeightM, 9);
        Assert.Equal(10 * 0.24 / 2, size.CrownRadiusM, 9);
    }

    [Fact]
    public void A_stored_forecast_without_sizes_is_requested_again()
    {
        Assert.True(TreeTimelines.HasSizes(new TreeBenefitTimeline("k", "ACCA", 5.4, 1, "2026-10-23T00:00:00Z", [Year(1, 6, 2)])));
        Assert.False(TreeTimelines.HasSizes(new TreeBenefitTimeline("k", "ACCA", 5.4, 1, "2026-10-22T00:00:00Z", [Year(1, 6.3, 0)])));
        Assert.False(TreeTimelines.HasSizes(new TreeBenefitTimeline("k", "ACCA", 5.4, 1, "2026-10-21T00:00:00Z", [Year(1, 0, 0)])));
    }
}
