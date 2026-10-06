using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class SiteBoundaryResolverTests
{
    private static readonly DashboardPropertyLine North = new("pl-north", "North plot", 4000, true);
    private static readonly DashboardPropertyLine South = new("pl-south", "South plot", 1500, true);
    private static readonly DashboardPropertyLine Open = new("pl-open", "Sketch", 0, false);

    [Fact]
    public void Offers_project_information_each_property_line_and_their_sum()
    {
        var options = SiteBoundaryResolver.BuildOptions(2500, [North, South, Open]);

        Assert.Equal(
            [SiteBoundaryResolver.ProjectInformationKey, "pl-north", "pl-south", "pl-open", SiteBoundaryResolver.AllPropertyLinesKey],
            options.Select(option => option.Key));
        Assert.Equal(5500, options.Single(option => option.Key == SiteBoundaryResolver.AllPropertyLinesKey).AreaSquareMeters);
        Assert.Null(options.Single(option => option.Key == "pl-open").AreaSquareMeters);
    }

    [Fact]
    public void No_sum_option_for_a_single_property_line()
    {
        var options = SiteBoundaryResolver.BuildOptions(null, [North]);

        Assert.DoesNotContain(options, option => option.Key == SiteBoundaryResolver.AllPropertyLinesKey);
    }

    [Fact]
    public void A_saved_choice_wins_while_it_still_exists()
    {
        var options = SiteBoundaryResolver.BuildOptions(2500, [North, South]);

        Assert.Equal("pl-south", SiteBoundaryResolver.Resolve(options, "pl-south").Key);
        // A deleted property line falls back to the default.
        Assert.Equal(SiteBoundaryResolver.ProjectInformationKey, SiteBoundaryResolver.Resolve(options, "pl-deleted").Key);
    }

    [Fact]
    public void Defaults_to_the_entered_project_information_area()
    {
        var options = SiteBoundaryResolver.BuildOptions(2500, [North, South]);

        Assert.Equal(2500, SiteBoundaryResolver.Resolve(options, null).AreaSquareMeters);
    }

    [Fact]
    public void Without_a_project_information_area_defaults_to_the_only_closed_line()
    {
        var options = SiteBoundaryResolver.BuildOptions(null, [North, Open]);

        Assert.Equal("pl-north", SiteBoundaryResolver.Resolve(options, null).Key);
    }

    [Fact]
    public void Without_a_project_information_area_defaults_to_all_lines_when_there_are_several()
    {
        var options = SiteBoundaryResolver.BuildOptions(null, [North, South]);

        Assert.Equal(SiteBoundaryResolver.AllPropertyLinesKey, SiteBoundaryResolver.Resolve(options, null).Key);
    }

    [Fact]
    public void With_nothing_to_measure_resolves_to_project_information_without_an_area()
    {
        var resolved = SiteBoundaryResolver.Resolve(SiteBoundaryResolver.BuildOptions(null, null), null);

        Assert.Equal(SiteBoundaryResolver.ProjectInformationKey, resolved.Key);
        Assert.Null(resolved.AreaSquareMeters);
    }

    [Fact]
    public void Floors_typed_on_their_type_count_as_pervious()
    {
        var floors = new[]
        {
            Floor(ldsType: "Lawn", area: 300),
            Floor(ldsType: "Granite - Blanco Cristal", area: 200),
            Floor(ldsType: null, area: 100),
            Floor(ldsType: "Concrete", surfaceClass: "Pervious", area: 50) // explicit tag wins over the inferred class
        };

        var summary = DashboardAggregationService.BuildSiteKpiSummary([], floors, [], 1000, null);

        Assert.Equal(350, summary.PerviousAreaSquareMeters);
        Assert.Equal(35, summary.SoftscapeSurfaceRatioPercent);
    }

    private static DashboardFloorItem Floor(string? ldsType, double area, string? surfaceClass = null) =>
        new(Guid.NewGuid().ToString(), 1, "Floor", "Type", ldsType, surfaceClass, "Level 0", new DesignOptionInfo(null, null, true),
            area, 0, 0, 0, 0, 0, 0, 0, 0);
}
