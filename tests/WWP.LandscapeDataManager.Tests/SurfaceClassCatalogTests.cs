using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class SurfaceClassCatalogTests
{
    [Theory]
    [InlineData("Lawn")]
    [InlineData("Meadow")]
    [InlineData("Wildflower Meadow")]
    [InlineData("Shrub Bed")]
    [InlineData("Gravel Path")]
    public void Vegetated_and_soft_landscape_types_infer_pervious(string ldsType) =>
        Assert.Equal("Pervious", SurfaceClassCatalog.Infer(ldsType));

    [Theory]
    [InlineData("Granite - Blanco Cristal")]
    [InlineData("Concrete Paving")]
    [InlineData("Asphalt")]
    [InlineData("Timber Decking")]
    public void Hard_landscape_types_infer_impervious(string ldsType) =>
        Assert.Equal("Impervious", SurfaceClassCatalog.Infer(ldsType));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Some Unrecognized Type")]
    public void Unrecognized_or_missing_types_return_null(string? ldsType) =>
        Assert.Null(SurfaceClassCatalog.Infer(ldsType));
}
