using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Revit tree types the Airtable planting source has no row for, and the rows added for them.</summary>
public sealed class NewTreeTypesTests
{
    private static readonly string[] KeyFields = ["Types", "Planting type", "Species names", "species", "type"];

    private static ModelScanItem Tree(string type, int count = 1, string? code = null, string family = "WWP_Vegetation_Tree") =>
        new("Planting", type, type.GetHashCode(), count, 0, null, false, family, code);

    private static AirtableRecord Row(params (string Field, string Value)[] fields) =>
        new($"rec{fields.GetHashCode()}", fields.ToDictionary(field => field.Field, field => JsonSerializer.SerializeToElement(field.Value)));

    private static readonly AirtableRecord[] Sheet =
    [
        Row(("Types", "Acer_Campestre"), ("Planting type", "Tree"), ("Species Code", "ACCA")),
        Row(("Types", "Prunus_Avium"), ("Planting type", "Tree"), ("Species Code", "PRAV")),
        Row(("Types", "Generic"), ("Planting type", "Tree")),
        Row(("Types", "Meadow"), ("Planting type", "WWP_Vegetation_Meadow"))
    ];

    [Fact]
    public void A_tree_type_with_no_row_is_offered_once_with_all_its_trees()
    {
        var found = NewTreeTypes.Find([Tree("Sorbus_Torminalis", 3), Tree("Sorbus_Torminalis", 2, "SOTO")], Sheet, KeyFields, []);

        var type = Assert.Single(found);
        Assert.Equal(("WWP_Vegetation_Tree", "Sorbus_Torminalis", 5, "SOTO"), (type.FamilyName, type.TypeName, type.TreeCount, type.SpeciesCode));
        Assert.Null(type.Conflict);
    }

    [Fact]
    public void Types_the_sync_already_matches_are_not_offered()
    {
        var found = NewTreeTypes.Find(
            [Tree("acer campestre"), Tree("Wild_Cherry", code: "PRAV"), Tree("Generic"), new("Floors", "Meadow_New", 9, 1, 50, null, true, "Floor", null)],
            Sheet, KeyFields, []);

        Assert.Empty(found);
    }

    [Fact]
    public void A_type_paired_by_an_alias_is_not_offered()
    {
        var found = NewTreeTypes.Find([Tree("Field_Maple_Clone")], Sheet, KeyFields, [new TypeAlias("Missing_Row", "WWP_Vegetation_Tree", "Field_Maple_Clone")]);

        Assert.Empty(found);
    }

    [Fact]
    public void Two_families_with_a_type_of_the_same_name_are_conflicts()
    {
        var found = NewTreeTypes.Find([Tree("Tilia_Cordata"), Tree("Tilia_Cordata", family: "Other_Tree")], Sheet, KeyFields, []);

        Assert.Equal(2, found.Count);
        Assert.All(found, type => Assert.NotNull(type.Conflict));
    }

    [Fact]
    public void A_name_another_family_already_matches_by_species_code_is_a_conflict()
    {
        var found = NewTreeTypes.Find([Tree("Acer_Campestre_Elsrijk", code: "ACCA", family: "A"), Tree("Acer_Campestre_Elsrijk", family: "B")], Sheet, KeyFields, []);

        var type = Assert.Single(found);
        Assert.Equal("B", type.FamilyName);
        Assert.NotNull(type.Conflict);
    }

    [Fact]
    public void New_types_sharing_a_species_code_get_rows_matched_by_name()
    {
        var found = NewTreeTypes.Find([Tree("Sorbus_Torminalis", code: "SOTO"), Tree("Sorbus_Torminalis_Pendula", code: "soto"), Tree("Tilia_Cordata", code: "TICO")], Sheet, KeyFields, []);

        Assert.Equal(new string?[] { null, null, "TICO" }, found.Select(type => type.SpeciesCode));
        Assert.All(found, type => Assert.Null(type.Conflict));
    }

    [Fact]
    public void The_row_has_only_the_type_name_tree_and_species_code()
    {
        var fields = NewTreeTypes.Fields(new NewTreeType("WWP_Vegetation_Tree", "Sorbus_Torminalis", 5, "SOTO", null), Sheet);

        Assert.Equal(new Dictionary<string, object?> { ["Types"] = "Sorbus_Torminalis", ["Planting type"] = "Tree", ["Species Code"] = "SOTO" }, fields);
    }

    [Fact]
    public void Columns_the_source_does_not_have_are_left_out()
    {
        var fields = NewTreeTypes.Fields(new NewTreeType("WWP_Vegetation_Tree", "Sorbus_Torminalis", 5, "SOTO", null), [Row(("species", "Acer_Campestre"))]);

        Assert.Equal(new Dictionary<string, object?> { ["species"] = "Sorbus_Torminalis" }, fields);
        Assert.False(NewTreeTypes.Fields(new NewTreeType("F", "T", 1, null, null), Sheet).ContainsKey("Species Code"));
    }
}
