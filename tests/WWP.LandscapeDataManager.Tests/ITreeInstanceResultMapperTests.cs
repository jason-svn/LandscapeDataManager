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
        Assert.Contains(items, item => item.RevitParameter == "!_S_PLANTING_iTreeResult_Status_Text" && item.SourceValue == "MissingInput");
        Assert.DoesNotContain(items, item => item.RevitParameter.Contains("Carbon"));
    }

    [Fact]
    public void Converts_carbon_from_pounds_to_kilograms_for_a_metric_project()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Annual_CarbonSequestered_lb"] = 10d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric");

        var carbon = Assert.Single(items, item => item.RevitParameter == "!_S_PLANTING_iTreeCarbon_CO2SequesteredAnnual_Number");
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

        var carbon = Assert.Single(items, item => item.RevitParameter == "!_S_PLANTING_iTreeCarbon_CO2SequesteredAnnual_Number");
        Assert.Equal(10d, double.Parse(carbon.SourceValue), precision: 6);
    }

    [Fact]
    public void Converts_hydrology_gallons_to_cubic_metres_with_the_auto_revit_spec_conversion()
    {
        var outcome = new ITreeInstanceCalculationOutcome(
            new Dictionary<string, object?> { ["Annual_RunoffAvoided_gal"] = 100d }, null);

        var items = ITreeInstanceResultMapper.BuildWriteItems(
            "uid-1", "Calculated", null, "sig-1", outcome, "Metric");

        var runoff = Assert.Single(items, item => item.RevitParameter == "!_S_PLANTING_iTreeWater_RunoffAvoidedAnnual_Volume");
        Assert.Equal("Auto (Revit spec)", runoff.Conversion);
        Assert.True(double.Parse(runoff.SourceValue) is > 0.37 and < 0.38);
    }
}
