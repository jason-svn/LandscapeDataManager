using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Floor types the type sync matches to the sheet row Floor Calculator gave them.</summary>
public sealed class FloorMatchAliasesTests
{
    private static readonly string[] KeyFields = ["Types", "Planting type", "Species names", "species", "type"];

    private static AirtableRecord Row(string id, string category, string subCategory, string types, string plantingType) =>
        new(id, new Dictionary<string, JsonElement>
        {
            ["WWP_LDS_Category"] = JsonSerializer.SerializeToElement(category),
            ["WWP_LDS_SubCategory"] = JsonSerializer.SerializeToElement(subCategory),
            ["Types"] = JsonSerializer.SerializeToElement(types),
            ["Planting type"] = JsonSerializer.SerializeToElement(plantingType)
        });

    private static readonly AirtableRecord[] Sheet =
    [
        Row("recAsphalt", "Surfaces", "Asphalt", "Asphalt", "WWP_Surface_Asphalt"),
        Row("recMeadow", "Planting", "Meadow", "Meadow", "WWP_Vegetation_Meadow")
    ];

    private static SelectedFloorItem Floor(string type, string? matchKey) => new($"uid-{type}-{matchKey}", "Floor", type, 100, null, matchKey);

    private static ModelScanItem FloorType(string type) => new("Floors", type, type.GetHashCode(), 1, 100, null, true, "Floor");

    [Fact]
    public void A_floor_type_follows_the_row_floor_calculator_matched_it_to()
    {
        var aliases = FloorMatchAliases.Build([Floor("WWP_Surfaces_Building", "surfaces|asphalt|asphalt"), Floor("WWP_Surfaces_Building", null)], Sheet, KeyFields);

        var match = Assert.Single(StableTypeMatcher.Build([FloorType("WWP_Surfaces_Building")], Sheet, KeyFields, aliases));
        Assert.Equal("Matched", match.Status);
        Assert.Equal("recAsphalt", match.Record!.Id);
    }

    [Fact]
    public void No_alias_without_a_saved_match_or_for_a_row_the_source_does_not_have()
    {
        Assert.Empty(FloorMatchAliases.Build([Floor("WWP_Surfaces_Track", null), Floor("WWP_Surfaces_Gravel", "Surfaces|Gravel|Gravel")], Sheet, KeyFields));
    }

    [Fact]
    public void An_alias_saved_on_the_project_wins()
    {
        var saved = new TypeAlias("Meadow", "Floor", "WWP_Surfaces_Building");
        var merged = FloorMatchAliases.Merge([saved], FloorMatchAliases.Build([Floor("WWP_Surfaces_Building", "Surfaces|Asphalt|Asphalt")], Sheet, KeyFields));

        Assert.Equal(new[] { saved }, merged);
    }
}
