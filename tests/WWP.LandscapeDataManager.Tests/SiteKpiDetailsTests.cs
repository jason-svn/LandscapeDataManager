using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>The breakdown behind each Site &amp; Biodiversity card.</summary>
public sealed class SiteKpiDetailsTests
{
    private static readonly DesignOptionInfo Main = new(null, null, true);

    private static NormalizedTreeMetrics Tree(string code, string? native, string? months, string? functions, double canopy = 20) =>
        DashboardAggregationService.NormalizeTree(
            DashboardAggregationTestTrees.Item(10) with
            {
                SpeciesCode = code, CommonName = code + " tree", NativeStatus = native, BloomMonths = months,
                EcologicalFunctions = functions, CanopyAreaSquareMeters = canopy, PollenAnnual = 1, IrrigationDemandFactor = 0.3, Allergenic = "0.2"
            },
            "Metric", "GBP", 1);

    private static DashboardFloorItem Floor(string ldsType, string? surfaceClass, double area) =>
        new(Guid.NewGuid().ToString(), 1, "Floor", ldsType, ldsType, surfaceClass, "L0", Main, area, 0, 0, 0, 0, 0, 0, 0, 0, PollenAnnual: area / 100);

    private static SiteKpiDetailInputs Inputs() => new(
        [Tree("QURO", "Native", "4,5,9,10", "InsectHost, BirdFood"), Tree("QURO", "Native", "4,5,9,10", "InsectHost, BirdFood"), Tree("LITU", "Non-native", "6,7", "Pollinator"), Tree("XX", null, null, null)],
        [Floor("Meadow", "Pervious", 300), Floor("Asphalt", "Impervious", 100), Floor("Mystery", null, 50)],
        [],
        [new DashboardLightingItem("l1", 1, "Bollard", "LED", "L0", Main, "Yes"), new DashboardLightingItem("l2", 2, "Column", "Flood", "L0", Main, null)],
        1000,
        [],
        "Scheme A",
        BngMetricCatalog.Default);

    [Fact]
    public void Every_card_has_a_table_whose_rows_fit_its_columns()
    {
        var inputs = Inputs();
        foreach (var kpi in SiteKpiDetails.Kpis)
        {
            var table = SiteKpiDetails.Build(kpi.Key, inputs);

            Assert.Equal(kpi.Key, table.Key);
            Assert.False(string.IsNullOrWhiteSpace(table.Explanation));
            Assert.All(table.Rows, row => Assert.Equal(table.Columns.Count, row.Count));
            if (table.Total is { } total)
            {
                Assert.Equal(table.Columns.Count, total.Count);
            }
        }
    }

    [Fact]
    public void Native_lists_native_species_first_and_shares_only_the_tagged_trees()
    {
        var table = SiteKpiDetails.Build("Native", Inputs());

        Assert.StartsWith("QURO", table.Rows[0][0]);
        Assert.Equal("Native", table.Rows[0][2]);
        Assert.Equal("67", table.Rows[0][3]);
        Assert.Equal("— not tagged", table.Rows[^1][2]);
        Assert.Equal("67", table.Total![3]);
    }

    [Fact]
    public void Phenology_marks_each_species_months_and_counts_species_per_month()
    {
        var table = SiteKpiDetails.Build("Phenology", Inputs());
        var oak = table.Rows.Single(row => row[0].StartsWith("QURO", StringComparison.Ordinal));

        Assert.Equal(14, table.Columns.Count);
        Assert.Equal("●", oak[2 + 3]);
        Assert.Equal(string.Empty, oak[2 + 0]);
        Assert.Equal("1", table.Total![2 + 3]);
        Assert.Equal("0", table.Total![2 + 0]);
    }

    [Fact]
    public void Surfaces_share_the_site_and_leave_unclassified_floors_out_of_the_ratio()
    {
        var table = SiteKpiDetails.Build("Greenspace", Inputs());
        var meadow = table.Rows.Single(row => row[0] == "Meadow");
        var mystery = table.Rows.Single(row => row[0] == "Mystery");

        Assert.Equal("30", meadow[4]);
        Assert.Equal("75", meadow[5]);
        Assert.Equal("left out", mystery[5]);
    }
}
