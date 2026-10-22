using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Each planting type's trunk, height and crown-width growth rates from i-Tree's forecast.</summary>
public sealed class GrowthRatesFromITreeTests
{
    private static TreeBenefitYear Year(int year, double dbh, double height = 0, double crownArea = 0) =>
        new(year, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, dbh, height, crownArea);

    private static ModelScanItem Type(long id, string name, string? species = "ACCA", string family = "WWP_Vegetation_Tree") =>
        new("Planting", name, id, 1, 0, null, FamilyName: family, SpeciesCode: species);

    private static PlantingInstanceValidationItem Tree(string type, string? condition = "Good", int? exposure = 3, int? growthYear = null) =>
        new("uid", 1, "WWP_Vegetation_Tree", type, "ACCA", 5, condition, exposure, 2.1, 51.5, -0.1,
            "Calculated", string.Empty, string.Empty, string.Empty, string.Empty, growthYear, IsGrowthCopy: growthYear is not null);

    /// <summary>Quercus robur, Good, exposure 3 as i-Tree forecast it: 6.34 m and 9.0 m² at planting, 15.21 m and 50.8 m² after 25 years.</summary>
    private static IReadOnlyList<TreeBenefitYear> OakForecast() =>
        Enumerable.Range(1, 26)
            .Select(year => Year(year, 5.4 + 0.823 * (year - 1),
                height: 6.34 + (15.21 - 6.34) * (year - 1) / 25,
                crownArea: 9.0 + (50.8 - 9.0) * (year - 1) / 25))
            .ToList();

    [Fact]
    public void The_trunk_rate_is_the_forecast_slope_from_the_first_year_to_the_last()
    {
        // Acer campestre, Good, exposure 3: i-Tree's DBH grows 0.44 cm every year.
        var years = Enumerable.Range(1, 10).Select(year => Year(year, 5.4 + 0.44 * (year - 1))).ToList();
        Assert.Equal(0.44, GrowthRatesFromITree.TrunkRate(years)!.Value, 3);
    }

    [Fact]
    public void A_forecast_without_trunk_sizes_has_no_trunk_rate() =>
        Assert.Null(GrowthRatesFromITree.TrunkRate([Year(1, 0), Year(2, 0)]));

    [Fact]
    public void Height_and_width_rates_are_the_size_at_the_design_year_over_the_design_year()
    {
        // After 25 years (Year 26): 15.21 m tall, crown 50.8 m² = 8.04 m wide.
        var (height, width) = GrowthRatesFromITree.SizeRates(OakForecast(), 25);

        Assert.Equal(0.61, height!.Value, 2);
        Assert.Equal(0.32, width!.Value, 2);
    }

    [Fact]
    public void A_forecast_shorter_than_the_design_year_rates_its_last_year()
    {
        var (height, _) = GrowthRatesFromITree.SizeRates(OakForecast().Take(11).ToList(), 25);

        // After 10 years: 6.34 + 8.87 × 10 / 25 = 9.89 m.
        Assert.Equal(0.99, height!.Value, 2);
    }

    [Fact]
    public void A_forecast_without_sizes_has_no_height_or_width_rate() =>
        Assert.Equal((null, null), GrowthRatesFromITree.SizeRates([Year(1, 5.4), Year(2, 5.8)], 25));

    [Fact]
    public void A_type_takes_the_condition_and_exposure_most_of_its_planted_trees_have()
    {
        var plan = GrowthRatesFromITree.Plan(
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
        var plan = GrowthRatesFromITree.Plan([Type(1, "Acer_Campestre"), Type(2, "Unknown", species: null)], []);

        var type = Assert.Single(plan);
        Assert.Equal((GrowthRatesFromITree.DefaultCondition, GrowthRatesFromITree.DefaultCrownExposure, true),
            (type.Condition, type.CrownExposure, type.UsesDefaultInputs));
    }

    [Fact]
    public async Task Each_species_condition_and_exposure_is_forecast_once_to_the_design_year()
    {
        var plan = GrowthRatesFromITree.Plan([Type(1, "Prunus_Avium", "PRAV"), Type(2, "Prunus_Avium_Plena", "PRAV")], []);
        var requests = new List<int>();
        var rated = await GrowthRatesFromITree.RunAsync(plan, 51.5, -0.1, 25, (input, years, _) =>
        {
            requests.Add(years);
            Assert.Equal(5.4, input.DiameterInches * 2.54, 6);
            return Task.FromResult(OakForecast());
        });

        Assert.Equal([26], requests);
        Assert.All(rated, type => Assert.Equal((0.823, 0.61, 0.32), (type.TrunkCmPerYear!.Value, type.HeightMPerYear!.Value, type.WidthMPerYear!.Value)));
        var writes = GrowthRatesFromITree.BuildTypeWrites(rated);
        Assert.Equal(6, writes.Count);
        Assert.Equal(
            [(GrowthRatesFromITree.TrunkParameter, "0.823"), (GrowthRatesFromITree.HeightParameter, "0.61"), (GrowthRatesFromITree.WidthParameter, "0.32")],
            writes.Where(write => write.TypeId == 1).Select(write => (write.RevitParameter, write.SourceValue)));
        Assert.All(writes, write => Assert.Equal("Type", write.Scope));
    }

    [Fact]
    public async Task A_failed_forecast_is_reported_on_its_types_and_not_written()
    {
        var plan = GrowthRatesFromITree.Plan([Type(1, "Acer_Campestre")], []);
        var rated = await GrowthRatesFromITree.RunAsync(plan, 51.5, -0.1, 25,
            (_, _, _) => throw new InvalidOperationException("species not found"));

        Assert.Equal("species not found", Assert.Single(rated).Error);
        Assert.Empty(GrowthRatesFromITree.BuildTypeWrites(rated));
    }

    [Fact]
    public void A_sheet_row_matched_by_two_types_gets_the_rates_of_the_one_with_more_trees()
    {
        var record = new AirtableRecord("rec1", new Dictionary<string, JsonElement>());
        var few = new TypeGrowthRates(1, "F", "Prunus_Avium", "PRAV", "Good", 3, 2, false, 0.444, 0.55, 0.3);
        var many = new TypeGrowthRates(2, "F", "Prunus_Avium_Plena", "PRAV", "Good", 5, 40, false, 0.786, 0.62, 0.41);
        var matches = new[]
        {
            new TypeMatch(Type(1, "Prunus_Avium"), record, "Matched", null),
            new TypeMatch(Type(2, "Prunus_Avium_Plena"), record, "Matched", null),
            new TypeMatch(Type(3, "Unmatched"), null, "No match", null)
        };

        var (recordId, fields, typeName) = Assert.Single(GrowthRatesFromITree.SheetUpdates([few, many], matches));
        Assert.Equal(("rec1", "Prunus_Avium_Plena"), (recordId, typeName));
        Assert.Equal(0.79, fields[GrowthRatesFromITree.TrunkSheetColumn]);
        Assert.Equal(0.62, fields[GrowthRatesFromITree.HeightSheetColumn]);
        Assert.Equal(0.41, fields[GrowthRatesFromITree.WidthSheetColumn]);
    }

    private static AirtableRecord Row(string id, double? trunk, double? height, double? width)
    {
        var fields = new Dictionary<string, JsonElement>();
        void Add(string column, double? value)
        {
            if (value is { } number)
            {
                fields[column] = JsonSerializer.SerializeToElement(number);
            }
        }

        Add(GrowthRatesFromITree.TrunkSheetColumn, trunk);
        Add(GrowthRatesFromITree.HeightSheetColumn, height);
        Add(GrowthRatesFromITree.WidthSheetColumn, width);
        return new AirtableRecord(id, fields);
    }

    private static readonly TypeGrowthRates Oak = new(1, "F", "Quercus_Robur", "QURO", "Good", 3, 175, false, 0.735, 0.58, 0.3);

    [Fact]
    public void A_sheet_row_that_already_has_the_rates_is_up_to_date_and_not_pushed()
    {
        var row = Row("rec1", 0.74, 0.58, 0.3);
        var match = new TypeMatch(Type(1, "Quercus_Robur"), row, "Matched", null);

        Assert.True(GrowthRatesFromITree.SheetStatus(Oak, match).IsUpToDate);
        Assert.Empty(GrowthRatesFromITree.SheetUpdates([Oak], [match]));
    }

    [Fact]
    public void A_sheet_row_with_other_rates_is_stale_and_one_with_an_empty_rate_is_missing()
    {
        var stale = GrowthRatesFromITree.SheetStatus(Oak, new TypeMatch(Type(1, "Quercus_Robur"), Row("rec1", 0.74, 0.45, 0.4), "Matched", null));
        var missing = GrowthRatesFromITree.SheetStatus(Oak, new TypeMatch(Type(1, "Quercus_Robur"), Row("rec1", null, 0.45, 0.4), "Matched", null));

        Assert.Equal((RateStatus.Stale, "height 0.45 → 0.58, width 0.4 → 0.3"), (stale.Status, stale.Changes));
        Assert.Equal(RateStatus.Missing, missing.Status);
        Assert.Equal(RateStatus.NoRow, GrowthRatesFromITree.SheetStatus(Oak, null).Status);
    }

    [Fact]
    public void A_type_whose_preview_finds_every_rate_unchanged_is_up_to_date()
    {
        var other = Oak with { TypeId = 2, TypeName = "Acer_Campestre" };
        var writes = GrowthRatesFromITree.BuildTypeWrites([Oak, other]);
        ParameterWritePreviewRow Preview(int index, string current, string status) =>
            new(index, writes[index].TypeName, writes[index].RevitParameter, "Type", current, writes[index].SourceValue, 1, status, null, status == "Ready");

        var statuses = GrowthRatesFromITree.TypeStatuses(writes,
        [
            Preview(0, "0.735", "No change"), Preview(1, "0.58", "No change"), Preview(2, "0.3", "No change"),
            Preview(3, "0.735", "No change"), Preview(4, "0.5", "Ready"), Preview(5, "", "Ready")
        ]);

        Assert.True(statuses[1].IsUpToDate);
        Assert.Equal((RateStatus.Missing, "height 0.5 → 0.58, width empty → 0.3"), (statuses[2].Status, statuses[2].Changes));
    }

    [Fact]
    public void Copies_wait_for_their_design_option_from_the_result_or_from_an_older_connector_s_note()
    {
        GrowthProjectionResult Result(IReadOnlyList<string> notes, IReadOnlyList<GrowthOptionWaiting>? waiting = null) =>
            new("Sandbox", false, 1010, 0, 0, 0, 0, [], notes, waiting);

        Assert.True(Result([], [new GrowthOptionWaiting("Test : Option 1 (primary)", 602)]).HasCopiesWaiting);
        Assert.True(Result(["2,020 copies for trees in 'Test : Option 2' were skipped: Revit puts new elements into the active design option."]).HasCopiesWaiting);
        Assert.False(Result([]).HasCopiesWaiting);
    }

    [Theory]
    [InlineData(GrowthRatesFromITree.TrunkSheetColumn, GrowthRatesFromITree.TrunkParameter)]
    [InlineData(GrowthRatesFromITree.HeightSheetColumn, GrowthRatesFromITree.HeightParameter)]
    [InlineData(GrowthRatesFromITree.WidthSheetColumn, GrowthRatesFromITree.WidthParameter)]
    public void The_importer_maps_each_sheet_column_onto_its_growth_rate(string column, string parameter) =>
        Assert.Contains(parameter, DefaultParameterMappingCatalog.GetCandidates(column));

    [Fact]
    public void A_calculated_tree_gets_i_tree_s_height_and_crown_width()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Tree_Height_m"] = 15.21, ["Crown_Area_m2"] = 50.8 }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems("uid", "Calculated", null, "sig", outcome, "Metric");

        var height = Assert.Single(items, item => item.RevitParameter == ITreeInstanceResultMapper.TreeHeightParameter);
        var width = Assert.Single(items, item => item.RevitParameter == ITreeInstanceResultMapper.CrownWidthParameter);
        Assert.Equal(("Auto (Revit spec)", 15.21), (height.Conversion, double.Parse(height.SourceValue, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(8.04, double.Parse(width.SourceValue, System.Globalization.CultureInfo.InvariantCulture), 2);
    }

    [Fact]
    public void A_tree_i_tree_could_not_calculate_goes_back_to_its_growth_rates()
    {
        var items = ITreeInstanceResultMapper.BuildWriteItems("uid", "APIError", "species not found", "sig", null, "Metric");

        Assert.All(
            items.Where(item => item.RevitParameter is ITreeInstanceResultMapper.TreeHeightParameter or ITreeInstanceResultMapper.CrownWidthParameter),
            item => Assert.Equal("0", item.SourceValue));
        Assert.Equal(2, items.Count(item => item.RevitParameter is ITreeInstanceResultMapper.TreeHeightParameter or ITreeInstanceResultMapper.CrownWidthParameter));
    }

    [Fact]
    public void The_forecast_reads_each_year_s_crown_area()
    {
        using var document = JsonDocument.Parse("""
            {"benefits":{"annual":{"category":[
              {"year":0,"diameter":5.4,"height":6.34,"crown-area":9.0},
              {"year":1,"diameter":6.22,"height":6.74,"crown-area":10.3}]}}}
            """);

        var years = ITreeApiClient.ParseAnnualTimeline(document.RootElement);

        Assert.Equal([9.0, 10.3], years.Select(year => year.CrownAreaM2));
        Assert.Equal(3.39, years[0].CrownWidthM, 2);
    }
}
