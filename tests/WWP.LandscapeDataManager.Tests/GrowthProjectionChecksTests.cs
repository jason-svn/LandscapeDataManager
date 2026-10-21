using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class GrowthProjectionChecksTests
{
    private static PlantingInstanceValidationItem Tree(string type, int year, double? dbh) =>
        new($"{type}-{year}", 1, "WWP_Vegetation_Tree", type, "ACCA", 5 + year, "Good", 3, dbh, 51.5, -0.1,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, year);

    [Fact]
    public void Flags_a_type_whose_dbh_never_changes_across_growth_years()
    {
        var trees = new[]
        {
            Tree("Acer_Campestre", 0, 2.13), Tree("Acer_Campestre", 10, 2.13), Tree("Acer_Campestre", 25, 2.13), // rate 0
            Tree("Quercus_Robur", 0, 2.13), Tree("Quercus_Robur", 10, 6.07),                                   // grows
            Tree("Sorbus_Aria", 10, 3.0)                                                                       // one year only
        };

        Assert.Equal(["WWP_Vegetation_Tree : Acer_Campestre"], GrowthProjectionChecks.TypesWithUnchangingDbh(trees));
    }

    [Fact]
    public void Trees_without_a_dbh_or_growth_year_are_ignored()
    {
        var trees = new[] { Tree("Acer_Campestre", 0, null), Tree("Acer_Campestre", 10, null) };

        Assert.Empty(GrowthProjectionChecks.TypesWithUnchangingDbh(trees));
    }
}
