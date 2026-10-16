using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class EmbodiedCarbonTests
{
    [Theory]
    [InlineData(10.16, 5.54, 15.7, 15.7)]  // granite: product + transport
    [InlineData(36.3, null, 38.4, 36.3)]   // only one part given: the parts win over the total
    [InlineData(null, null, 15.7, 15.7)]   // no parts: the sheet's total
    [InlineData(null, null, -27.0, 0)]     // tree: the sheet's "GWP" is sequestration, not embodied carbon
    [InlineData(null, null, null, 0)]
    public void Rate_is_product_plus_transport_and_never_negative(double? product, double? transport, double? total, double expected) =>
        Assert.Equal(expected, EmbodiedCarbon.Rate(product, transport, total), 6);

    [Fact]
    public void Payback_is_embodied_over_yearly_uptake()
    {
        Assert.Equal(7.85, EmbodiedCarbon.PaybackYears(15_700, 2_000)!.Value, 6);
        Assert.Equal(0, EmbodiedCarbon.PaybackYears(0, 2_000));
        Assert.Null(EmbodiedCarbon.PaybackYears(15_700, 0));
    }
}
