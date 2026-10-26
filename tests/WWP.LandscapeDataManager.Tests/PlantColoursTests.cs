using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>A species' year in colour: the sheet's "Name #RRGGBB" colours, its seasons and fruit, and the phenology table built from them.</summary>
public sealed class PlantColoursTests
{
    private static readonly PlantAppearance FieldMaple = new(
        "Yellow-green #B6C65B", "8,9,10", "Tan samara #B7864F", "Samara",
        "Fresh green #8DBB4C", "Dark green #3F6B34", "Clear golden yellow #E2B93B", "Grey-brown bark #7D6E62");

    [Theory]
    [InlineData("Creamy white #F3EFD9", "Creamy white", "#F3EFD9")]
    [InlineData("#a1b2c3", "#A1B2C3", "#A1B2C3")]
    [InlineData("Pink (#E8A0B8)", "Pink", "#E8A0B8")]
    public void A_colour_reads_as_its_name_and_hex(string text, string name, string hex)
    {
        Assert.Equal(new PlantColour(name, hex), PlantColours.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pink")]
    [InlineData("#E8A0B")]
    public void Text_without_a_hex_is_no_colour(string? text)
    {
        Assert.Null(PlantColours.Parse(text));
    }

    [Fact]
    public void Months_fall_in_the_sheets_seasons()
    {
        Assert.Equal(new[] { 3, 3, 0, 0, 0, 1, 1, 1, 2, 2, 2, 3 }, Enumerable.Range(1, 12).Select(PlantColours.SeasonOf));
        Assert.Equal("#3F6B34", PlantColours.SeasonColour(FieldMaple, 7)!.Hex);
        Assert.Equal("#7D6E62", PlantColours.SeasonColour(FieldMaple, 1)!.Hex);
    }

    [Fact]
    public void None_is_no_kind_of_fruit()
    {
        Assert.Equal("Samara", PlantColours.FruitType(FieldMaple));
        Assert.Null(PlantColours.FruitType(FieldMaple with { FruitType = "None" }));
        Assert.Equal(new[] { 8, 9, 10 }, PlantColours.FruitMonths(FieldMaple));
    }

    private static NormalizedTreeMetrics Tree(string code, string? bloomMonths, PlantAppearance? appearance) =>
        DashboardAggregationService.NormalizeTree(
            DashboardAggregationTestTrees.Item(10) with { SpeciesCode = code, CommonName = code, BloomMonths = bloomMonths, Appearance = appearance },
            "Metric", "GBP", 1);

    private static SiteDetailTable Phenology(params NormalizedTreeMetrics[] trees) =>
        SiteKpiDetails.Build("Phenology", new SiteKpiDetailInputs(trees, [], [], [], null, [], "Scheme", BngMetricCatalog.Default))!;

    [Fact]
    public void Each_month_is_tinted_by_its_season_with_the_flower_and_fruit_marks()
    {
        var table = Phenology(Tree("ACCA", "4,5", FieldMaple));

        var looks = Assert.Single(table.Looks!);
        var april = looks[2 + 3]!;
        Assert.Equal("#8DBB4C", april.Background);
        Assert.Equal([new SiteDetailMark(SiteDetailMark.FlowerKind, "#B6C65B")], april.Marks);
        var september = looks[2 + 8]!;
        Assert.Equal("#E2B93B", september.Background);
        Assert.Equal([new SiteDetailMark("Samara", "#B7864F")], september.Marks);
        Assert.Equal("Samara", table.Rows[0][14]);
    }

    [Fact]
    public void A_species_without_colours_still_shows_its_bloom_months()
    {
        var table = Phenology(Tree("ULAM", "3,4", null));

        var looks = Assert.Single(table.Looks!);
        Assert.Null(looks[2 + 0]);
        Assert.Equal(SiteDetailMark.FlowerKind, Assert.Single(looks[2 + 2]!.Marks).Kind);
        Assert.Null(looks[2 + 2]!.Background);
    }

    [Fact]
    public void Phenological_resilience_counts_months_in_flower_or_in_fruit()
    {
        var summary = DashboardAggregationService.BuildSiteKpiSummary([Tree("ACCA", "4,5", FieldMaple)], [], [], null, null);

        Assert.Equal(5, summary.DistinctBloomMonthsCount);
    }
}
