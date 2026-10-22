using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Each planting type's trunk growth rate from i-Tree's DBH forecast.</summary>
public sealed class TrunkGrowthFromITreeTests
{
    private static TreeBenefitYear Year(int year, double dbh) => new(year, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, dbh);

    private static ModelScanItem Type(long id, string name, string? species = "ACCA", string family = "WWP_Vegetation_Tree") =>
        new("Planting", name, id, 1, 0, null, FamilyName: family, SpeciesCode: species);

    private static PlantingInstanceValidationItem Tree(string type, string? condition = "Good", int? exposure = 3, int? growthYear = null) =>
        new("uid", 1, "WWP_Vegetation_Tree", type, "ACCA", 5, condition, exposure, 2.1, 51.5, -0.1,
            "Calculated", string.Empty, string.Empty, string.Empty, string.Empty, growthYear, IsGrowthCopy: growthYear is not null);

    [Fact]
    public void The_rate_is_the_forecast_slope_from_the_first_year_to_the_last()
    {
        // Acer campestre, Good, exposure 3: i-Tree's DBH grows 0.44 cm every year.
        var years = Enumerable.Range(1, 10).Select(year => Year(year, 5.4 + 0.44 * (year - 1))).ToList();
        Assert.Equal(0.44, TrunkGrowthFromITree.RateCmPerYear(years)!.Value, 3);
    }

    [Fact]
    public void A_forecast_without_trunk_sizes_has_no_rate() =>
        Assert.Null(TrunkGrowthFromITree.RateCmPerYear([Year(1, 0), Year(2, 0)]));

    [Fact]
    public void A_type_takes_the_condition_and_exposure_most_of_its_planted_trees_have()
    {
        var plan = TrunkGrowthFromITree.Plan(
            [Type(1, "Acer_Campestre")],
            [
                Tree("Acer_Campestre", "good", 5), Tree("Acer_Campestre", "Good", 5), Tree("Acer_Campestre", "Fair", 3),
                // Growth-year copies don't count: they repeat their original.
                Tree("Acer_Campestre", "Fair", 3, growthYear: 10), Tree("Acer_Campestre", "Fair", 3, growthYear: 20)
            ]);

        var type = Assert.Single(plan);
        Assert.Equal(("Good", 5, 3, false), (type.Condition, type.CrownExposure, type.TreeCount, type.UsesDefaultInputs));
    }

    [Fact]
    public void A_type_without_trees_uses_the_default_inputs_and_types_without_a_species_are_left_out()
    {
        var plan = TrunkGrowthFromITree.Plan([Type(1, "Acer_Campestre"), Type(2, "Unknown", species: null)], []);

        var type = Assert.Single(plan);
        Assert.Equal((TrunkGrowthFromITree.DefaultCondition, TrunkGrowthFromITree.DefaultCrownExposure, true),
            (type.Condition, type.CrownExposure, type.UsesDefaultInputs));
    }

    [Fact]
    public async Task Each_species_condition_and_exposure_is_forecast_once()
    {
        var plan = TrunkGrowthFromITree.Plan([Type(1, "Prunus_Avium", "PRAV"), Type(2, "Prunus_Avium_Plena", "PRAV")], []);
        var requests = 0;
        var rated = await TrunkGrowthFromITree.RunAsync(plan, 51.5, -0.1, (input, years, _) =>
        {
            requests++;
            Assert.Equal(5.4, input.DiameterInches * 2.54, 6);
            return Task.FromResult<IReadOnlyList<TreeBenefitYear>>(
                Enumerable.Range(1, years).Select(year => Year(year, 5.4 + 0.5 * (year - 1))).ToList());
        });

        Assert.Equal(1, requests);
        Assert.All(rated, type => Assert.Equal(0.5, type.RateCmPerYear!.Value, 3));
        Assert.All(TrunkGrowthFromITree.BuildTypeWrites(rated), write =>
            Assert.Equal((TrunkGrowthFromITree.ParameterName, "Type", "0.5"), (write.RevitParameter, write.Scope, write.SourceValue)));
    }

    [Fact]
    public async Task A_failed_forecast_is_reported_on_its_types_and_not_written()
    {
        var plan = TrunkGrowthFromITree.Plan([Type(1, "Acer_Campestre")], []);
        var rated = await TrunkGrowthFromITree.RunAsync(plan, 51.5, -0.1,
            (_, _, _) => throw new InvalidOperationException("species not found"));

        Assert.Equal("species not found", Assert.Single(rated).Error);
        Assert.Empty(TrunkGrowthFromITree.BuildTypeWrites(rated));
    }

    [Fact]
    public void A_sheet_row_matched_by_two_types_gets_the_rate_of_the_one_with_more_trees()
    {
        var record = new AirtableRecord("rec1", new Dictionary<string, JsonElement>());
        var few = new TypeTrunkGrowth(1, "F", "Prunus_Avium", "PRAV", "Good", 3, 2, false, 0.444);
        var many = new TypeTrunkGrowth(2, "F", "Prunus_Avium_Plena", "PRAV", "Good", 5, 40, false, 0.786);
        var matches = new[]
        {
            new TypeMatch(Type(1, "Prunus_Avium"), record, "Matched", null),
            new TypeMatch(Type(2, "Prunus_Avium_Plena"), record, "Matched", null),
            new TypeMatch(Type(3, "Unmatched"), null, "No match", null)
        };

        var update = Assert.Single(TrunkGrowthFromITree.SheetUpdates([few, many], matches));
        Assert.Equal(("rec1", 0.79, "Prunus_Avium_Plena"), update);
    }

    [Fact]
    public void The_importer_maps_the_sheet_column_onto_the_trunk_growth_rate() =>
        Assert.Contains(TrunkGrowthFromITree.ParameterName, DefaultParameterMappingCatalog.GetCandidates(TrunkGrowthFromITree.SheetColumn));
}
