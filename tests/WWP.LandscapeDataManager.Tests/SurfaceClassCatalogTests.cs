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

    [Theory]
    [InlineData("Impervious", "Lawn", "WWP_Vegetation_Shrub", "Impervious")] // explicit tag wins
    [InlineData(null, "Granite paving", "WWP_Vegetation_Shrub", "Impervious")] // LDS type before the type name
    [InlineData(null, null, "WWP_Vegetation_Shrub", "Pervious")] // unmatched floor falls back to its type name
    [InlineData(null, null, "WWP_Vegetation_Meadow", "Pervious")]
    [InlineData(null, null, "Generic 300mm", null)]
    public void Resolve_falls_back_from_tag_to_lds_type_to_type_name(string? tag, string? ldsType, string? typeName, string? expected) =>
        Assert.Equal(expected, SurfaceClassCatalog.Resolve(tag, ldsType, typeName));
}
