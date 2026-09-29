using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class BngFloorStatusEvaluatorTests
{
    private static readonly BngMetricCatalog Catalog = BngMetricCatalog.Default;

    private static readonly BngHabitatCreationInput CompleteInput =
        new("Hazel scrub", "Moderate", "Formally identified in local strategy", 0, 0.5);

    [Theory]
    [InlineData("WWP_Mixed_Scrub", "Standard", "Mixed scrub")]
    [InlineData("Floor", "Modified grassland", "Modified grassland")]
    [InlineData("Floor", "Rain Garden - 300mm", "Rain garden")]
    [InlineData("WWP Lawn", "Generic", "Modified grassland")]
    [InlineData("Landscape", "Wildflower Meadow", "Other neutral grassland")]
    [InlineData("Hardscape", "Concrete Paving 200", "Developed land; sealed surface")]
    public void Matches_habitat_from_family_or_type_name(string familyName, string typeName, string expectedHabitat) =>
        Assert.Equal(expectedHabitat, BngHabitatMatcher.FindBestMatch(familyName, typeName, Catalog)?.Name);

    [Theory]
    [InlineData("Floor", "Generic 150mm")]
    [InlineData("", "")]
    public void Unrecognized_names_do_not_match(string familyName, string typeName) =>
        Assert.Null(BngHabitatMatcher.FindBestMatch(familyName, typeName, Catalog));

    [Fact]
    public void Signature_changes_with_area_and_inputs_but_not_with_habitat_spelling()
    {
        var signature = BngFloorStatusEvaluator.ComputeSignature(CompleteInput, Catalog);

        Assert.Equal(signature, BngFloorStatusEvaluator.ComputeSignature(CompleteInput with { Habitat = "Heathland and shrub - Hazel scrub" }, Catalog));
        Assert.NotEqual(signature, BngFloorStatusEvaluator.ComputeSignature(CompleteInput with { AreaHectares = 0.51 }, Catalog));
        Assert.NotEqual(signature, BngFloorStatusEvaluator.ComputeSignature(CompleteInput with { YearOffset = -2 }, Catalog));
    }

    [Fact]
    public void Status_reflects_whether_revit_holds_the_current_result()
    {
        var result = BngHabitatCreationCalculator.Calculate(CompleteInput, Catalog);
        var signature = BngFloorStatusEvaluator.ComputeSignature(CompleteInput, Catalog);

        Assert.Equal(BngFloorStatus.Ready, BngFloorStatusEvaluator.Evaluate(result, false, signature, null, false));
        Assert.Equal(BngFloorStatus.Calculated, BngFloorStatusEvaluator.Evaluate(result, false, signature, signature, false));
        Assert.Equal(BngFloorStatus.Stale, BngFloorStatusEvaluator.Evaluate(result, false, signature, "OLD", false));
        Assert.Equal(BngFloorStatus.AutoMatched, BngFloorStatusEvaluator.Evaluate(result, true, signature, signature, false));
        Assert.Equal(BngFloorStatus.Failed, BngFloorStatusEvaluator.Evaluate(result, false, signature, signature, true));
    }

    [Fact]
    public void Missing_inputs_and_metric_warnings_take_priority_over_write_state()
    {
        var needsInfo = BngHabitatCreationCalculator.Calculate(CompleteInput with { Condition = null }, Catalog);
        Assert.Equal(BngFloorStatus.NeedsInfo, BngFloorStatusEvaluator.Evaluate(needsInfo, false, "X", "X", false));

        // An unconfirmed guess stays under Auto-matched even while other inputs are still missing.
        Assert.Equal(BngFloorStatus.AutoMatched, BngFloorStatusEvaluator.Evaluate(needsInfo, true, "X", "X", false));

        // Delayed creation raises the metric's "Check details- Delay…" flag.
        var checkData = BngHabitatCreationCalculator.Calculate(CompleteInput with { YearOffset = -5 }, Catalog);
        Assert.Equal(BngCalculationOutcome.CheckData, checkData.Outcome);
        Assert.Equal(BngFloorStatus.CheckData, BngFloorStatusEvaluator.Evaluate(checkData, false, "X", "X", false));
    }
}
