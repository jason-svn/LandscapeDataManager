using System.Globalization;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>One i-Tree forecast per unique planting input, to maturity, shared by identical trees.</summary>
public sealed class TreeTimelinesTests
{
    private static PlantingInstanceValidationItem Item(
        string uniqueId = "t1", int? growthYear = null, bool inGroup = false, double? dbh = 2.13, int? matureAge = 30, int? ageAtPlanting = 5) =>
        new(uniqueId, 1, "Tree", "Acer_Campestre", "ACCA", 5, "Good", 3, dbh, 51.5, -0.12,
            "Calculated", string.Empty, string.Empty, string.Empty, string.Empty, growthYear, inGroup, ageAtPlanting, matureAge);

    [Theory]
    [InlineData(30, 5, 25)]   // matures 25 years after planting
    [InlineData(67, 5, 35)]   // capped at 35 years
    [InlineData(1, 5, 1)]     // a mature age below the planting age still forecasts one year
    [InlineData(null, 5, 25)] // no mature age: the family's 25-year cap
    [InlineData(0, null, 25)]
    public void The_horizon_is_maturity_capped_at_35_years(int? matureAge, int? ageAtPlanting, int expected) =>
        Assert.Equal(expected, TreeTimelines.Horizon(matureAge, ageAtPlanting));

    [Fact]
    public void Only_planted_trees_get_a_timeline()
    {
        Assert.True(TreeTimelines.IsPlanted(Item()));
        Assert.True(TreeTimelines.IsPlanted(Item(growthYear: 0)));   // the Baseline workset
        Assert.False(TreeTimelines.IsPlanted(Item(growthYear: 10))); // a growth-year copy
        Assert.False(TreeTimelines.IsPlanted(Item(inGroup: true)));
    }

    [Fact]
    public void Identical_inputs_share_a_key_and_different_ones_dont()
    {
        var first = TreeTimelines.PlantingInput(Item("a"))!;
        var second = TreeTimelines.PlantingInput(Item("b"))!;
        var bigger = TreeTimelines.PlantingInput(Item("c", dbh: 4))!;
        var longer = TreeTimelines.PlantingInput(Item("d", matureAge: 40))!;

        Assert.Equal(TreeTimelines.Key(first), TreeTimelines.Key(second));
        Assert.NotEqual(TreeTimelines.Key(first), TreeTimelines.Key(bigger));
        Assert.NotEqual(TreeTimelines.Key(first), TreeTimelines.Key(longer));
        Assert.Equal(25, first.Years);
        Assert.StartsWith("t1-", TreeTimelines.Key(first));
    }

    [Fact]
    public void A_tree_missing_an_input_gets_no_forecast() =>
        Assert.Null(TreeTimelines.PlantingInput(Item(dbh: null)));

    [Fact]
    public void Each_tree_stores_its_outlook_totals_and_a_lifetime_over_the_whole_forecast()
    {
        var timeline = new TreeBenefitTimeline("k", "ACCA", 5.4, 3, "2026-10-21T00:00:00Z",
        [
            new TreeBenefitYear(1, 1, 0.1, 0.2, 0.01, 0.02, 0.03, 0.04, 0.05, 2, 1, 0.5),
            new TreeBenefitYear(2, 2, 0.2, 0.4, 0.01, 0.02, 0.03, 0.04, 0.05, 4, 2, 1),
            new TreeBenefitYear(3, 3, 0.3, 0.6, 0.01, 0.02, 0.03, 0.04, 0.05, 6, 3, 1.5),
        ]);

        var items = TreeTimelines.BuildWriteItems("uid", timeline, usdExchangeRate: 0.8);
        double Value(string parameter) => double.Parse(Assert.Single(items, item => item.RevitParameter == parameter).SourceValue, CultureInfo.InvariantCulture);

        Assert.Equal("k", Assert.Single(items, item => item.RevitParameter == "!_S_PLT_Result_iTreeTimelineKey_Text").SourceValue);
        // 5 years: 1 + 2 + 3, then the mature year 3 twice more.
        Assert.Equal((1 + 2 + 3 + 3 + 3) * UnitConversions.CarbonToCo2MassRatio, Value("!_S_PLT_Result_CO2Equivalent5yr_Mass"), 9);
        Assert.Equal((3.5 + 7 + 10.5 + 10.5 + 10.5) * 0.8, Value("!_S_PLT_Result_CostSaved5yr_Currency"), 9);
        Assert.Equal(5 * 0.15, Value("!_S_PLT_Result_PollutantsRemoved5yr_Mass"), 9);
        // Lifetime = the forecast's own horizon (3 years).
        Assert.Equal(6, Value("!_S_PLT_iTreeResult_CarbonSequesteredLifetimeTotal_Mass"), 9);
        Assert.Equal(0.6, Value("!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume"), 9);
        Assert.Equal(1.2, Value("!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume"), 9);
        Assert.Equal(6 * UnitConversions.CarbonToCo2MassRatio * 32 / 44, Value("!_S_PLT_Result_OxygenProducedLifetimeTotal_Mass"), 9);
    }

    [Fact]
    public void The_forecast_is_read_year_by_year_from_the_response()
    {
        using var document = JsonDocument.Parse("""
        {"status":"ok","data":{"trees":{"1":{"benefits":{"annual":{"category":[
          {"carbon":{"sequestration":1.5,"sequestration-worth":0.3},"hydrology":{"runoff-avoided":0.2,"runoff-avoided-worth":0.05,"interception":0.9},
           "pollution-removed":{"co":0.001,"no2":0.002,"o3":0.003,"so2":0.004,"pm25":0.005,"co-worth":0.01,"no2-worth":0.02,"o3-worth":0.03,"pm25-worth":0.04,"so2-worth":0.05,"voc-worth":0.06}},
          {"carbon":{"sequestration":2.5,"sequestration-worth":0.5},"hydrology":{"runoff-avoided":0.4,"runoff-avoided-worth":0.1,"interception":1.8},"pollution-removed":{}}
        ]}}}}}}
        """);

        Assert.True(ITreeApiClient.TryGetResponseTree(document.RootElement, out var tree));
        var years = ITreeApiClient.ParseAnnualTimeline(tree);

        Assert.Equal(2, years.Count);
        Assert.Equal(new TreeBenefitYear(1, 1.5, 0.2, 0.9, 0.001, 0.002, 0.003, 0.004, 0.005, 0.3, 0.05, 0.21), years[0] with { AirPollutionUsd = Math.Round(years[0].AirPollutionUsd, 9) });
        Assert.Equal(2, years[1].Year);
        Assert.Equal(0, years[1].CoKg);
    }
}
