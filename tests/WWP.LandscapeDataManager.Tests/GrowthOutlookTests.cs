using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class GrowthOutlookTests
{
    [Fact]
    public void Sums_the_years_between_growth_stops()
    {
        // Year 0 = 10/yr, year 5 = 20/yr, year 10 = 40/yr.
        var stops = new[] { (0, 10d), (5, 20d), (10, 40d) };

        Assert.Equal(75d, GrowthOutlook.Accumulate(stops, 5));         // (10 + 20) / 2 × 5
        Assert.Equal(75d + 150d, GrowthOutlook.Accumulate(stops, 10)); // + (20 + 40) / 2 × 5
    }

    [Fact]
    public void Holds_the_last_value_after_the_last_stop_and_interpolates_inside_a_span()
    {
        var stops = new[] { (0, 10d), (10, 30d) };

        Assert.Equal(200d + 30d * 5, GrowthOutlook.Accumulate(stops, 15)); // mature after year 10
        Assert.Equal((10d + 20d) / 2 * 5, GrowthOutlook.Accumulate(stops, 5)); // year 5 ≈ 20
    }

    [Fact]
    public void A_single_stop_is_annual_times_years()
    {
        Assert.Equal(120d, GrowthOutlook.Accumulate([(0, 12d)], 10));
        Assert.Equal(0d, GrowthOutlook.Accumulate([(0, 12d)], 0));
    }
}
