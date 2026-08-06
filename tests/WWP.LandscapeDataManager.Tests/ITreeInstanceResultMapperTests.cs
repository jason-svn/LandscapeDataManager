using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class ITreeInstanceResultMapperTests
{
    [Fact]
    public void With_no_outcome_only_the_tracking_parameters_are_written()
    {
        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "MissingInput", "Missing: TreeTrunk_DBH", "sig-1", null, "Metric");

        Assert.Equal(5, items.Count);
        Assert.Contains(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_Status_Text" && item.SourceValue == "MissingInput");
        Assert.DoesNotContain(items, item => item.RevitParameter.Contains("Carbon"));
    }

    [Fact]
    public void Converts_carbon_from_pounds_to_kilograms_for_a_metric_project()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Annual_CarbonSequestered_lb"] = 10d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric");

        var carbon = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_CO2SequesteredAnnual_Number");
        var value = double.Parse(carbon.SourceValue);
        Assert.True(value is > 4.5 and < 4.6, $"Expected ~4.54 kg, got {value}");
    }

    [Fact]
    public void Leaves_carbon_in_pounds_for_an_imperial_project()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Annual_CarbonSequestered_lb"] = 10d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Imperial");

        var carbon = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_CO2SequesteredAnnual_Number");
        Assert.Equal(10d, double.Parse(carbon.SourceValue), precision: 6);
    }

    [Fact]
    public void Converts_hydrology_gallons_to_cubic_metres_with_the_auto_revit_spec_conversion()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Annual_RunoffAvoided_gal"] = 100d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric");

        var runoff = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume");
        Assert.Equal("Auto (Revit spec)", runoff.Conversion);
        Assert.True(double.Parse(runoff.SourceValue) is > 0.37 and < 0.38);
    }

    [Fact]
    public void Writes_the_apis_monetary_benefit_as_cost_saved_with_no_unit_conversion()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Annual_Benefit_USD"] = 12.5d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric");

        var costSaved = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_CostSavedAnnual_Number");
        Assert.Equal(12.5d, double.Parse(costSaved.SourceValue), precision: 6);
    }

    [Fact]
    public void Writes_the_three_category_dollar_values_that_sum_into_cost_saved()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?>
            {
                ["Annual_CarbonBenefit_USD"] = 1d,
                ["Annual_StormWaterBenefit_USD"] = 2d,
                ["Annual_AirPollutionBenefit_USD"] = 3d
            }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric");

        Assert.Equal(1d, double.Parse(Assert.Single(items,
            item => item.RevitParameter == "!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Number").SourceValue), precision: 6);
        Assert.Equal(2d, double.Parse(Assert.Single(items,
            item => item.RevitParameter == "!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Number").SourceValue), precision: 6);
        Assert.Equal(3d, double.Parse(Assert.Single(items,
            item => item.RevitParameter == "!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Number").SourceValue), precision: 6);
    }

    [Fact]
    public void Converts_co2_equivalent_from_pounds_to_kilograms_for_a_metric_project()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Annual_CO2Equivalent_lb"] = 10d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric");

        var co2Equivalent = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_CO2EquivalentAnnual_Number");
        var value = double.Parse(co2Equivalent.SourceValue);
        Assert.True(value is > 4.5 and < 4.6, $"Expected ~4.54 kg, got {value}");
    }

    [Fact]
    public void Writes_lifetime_cumulative_results_alongside_the_annual_ones()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?>
            {
                ["CarbonSequestered_20yr_lb"] = 10d,
                ["RunoffAvoided_20yr_gal"] = 100d,
                ["Benefit_20yr_USD"] = 250d
            }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Imperial");

        var carbonLifetimeTotal = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_CO2SequesteredLifetimeTotal_Number");
        Assert.Equal(10d, double.Parse(carbonLifetimeTotal.SourceValue), precision: 6);

        var runoffLifetimeTotal = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume");
        Assert.True(double.Parse(runoffLifetimeTotal.SourceValue) is > 0.37 and < 0.38);

        var costSavedLifetimeTotal = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Number");
        Assert.Equal(250d, double.Parse(costSavedLifetimeTotal.SourceValue), precision: 6);
    }

    [Fact]
    public void Lifetime_total_reflects_whatever_the_api_actually_returned_not_a_fixed_20_years()
    {
        // The API sums are computed by ITreeApiClient over one entry per requested TreeGrowth_Years,
        // so a 25-year-old tree's "lifetime" fields hold a 25-year total under the same dictionary
        // keys — this mapper has no notion of "20" baked in, it just forwards whatever arrived.
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["CarbonSequestered_20yr_lb"] = 999d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Imperial");

        var carbonLifetimeTotal = Assert.Single(items, item => item.RevitParameter == "!_S_PLT_iTreeResult_CO2SequesteredLifetimeTotal_Number");
        Assert.Equal(999d, double.Parse(carbonLifetimeTotal.SourceValue), precision: 6);
    }

    [Fact]
    public void Defaults_to_usd_with_a_1to1_rate_when_no_currency_is_specified()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Annual_Benefit_USD"] = 12.5d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric");

        Assert.Equal("USD", Assert.Single(items,
            item => item.RevitParameter == "!_S_PLT_iTreeResult_CurrencyUsed_Text").SourceValue);
        Assert.Equal(1d, double.Parse(Assert.Single(items,
            item => item.RevitParameter == "!_S_PLT_iTreeResult_ExchangeRateUsed_Number").SourceValue), precision: 6);
    }

    [Fact]
    public void Converts_every_dollar_field_by_the_supplied_exchange_rate()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?>
            {
                ["Annual_Benefit_USD"] = 100d,
                ["Annual_CarbonBenefit_USD"] = 40d,
                ["Annual_StormWaterBenefit_USD"] = 50d,
                ["Annual_AirPollutionBenefit_USD"] = 10d,
                ["Benefit_20yr_USD"] = 2000d,
                ["CarbonBenefit_20yr_USD"] = 800d,
                ["StormWaterBenefit_20yr_USD"] = 1000d,
                ["AirPollutionBenefit_20yr_USD"] = 200d
            }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric", "GBP", 0.8d);

        double Value(string parameter) => double.Parse(Assert.Single(items, item => item.RevitParameter == parameter).SourceValue);

        Assert.Equal(80d, Value("!_S_PLT_iTreeResult_CostSavedAnnual_Number"), precision: 6);
        Assert.Equal(32d, Value("!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Number"), precision: 6);
        Assert.Equal(40d, Value("!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Number"), precision: 6);
        Assert.Equal(8d, Value("!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Number"), precision: 6);
        Assert.Equal(1600d, Value("!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Number"), precision: 6);
        Assert.Equal(640d, Value("!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Number"), precision: 6);
        Assert.Equal(800d, Value("!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Number"), precision: 6);
        Assert.Equal(160d, Value("!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Number"), precision: 6);

        Assert.Equal("GBP", Assert.Single(items,
            item => item.RevitParameter == "!_S_PLT_iTreeResult_CurrencyUsed_Text").SourceValue);
        Assert.Equal(0.8d, Value("!_S_PLT_iTreeResult_ExchangeRateUsed_Number"), precision: 6);
    }
}
