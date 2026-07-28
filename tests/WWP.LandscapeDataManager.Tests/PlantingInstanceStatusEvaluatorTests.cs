using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class PlantingInstanceStatusEvaluatorTests
{
    private const string EngineVersion = "i-Tree API v3";

    private static PlantingInstanceValidationItem Complete(
        string? speciesCode = "QURO",
        int? years = 20,
        string? condition = "Good",
        int? crownExposure = 5,
        double? dbh = 12,
        double? latitude = 51.45,
        double? longitude = -2.58,
        string storedStatus = "",
        string storedDetails = "",
        string storedInputSignature = "") =>
        new("uid-1", 1, "Trees", "Oak", speciesCode, years, condition, crownExposure, dbh, latitude, longitude,
            storedStatus, storedDetails, "", "", storedInputSignature);

    [Fact]
    public void Reports_missing_input_matching_the_spec_example()
    {
        var item = Complete(dbh: null, crownExposure: null);

        var result = PlantingInstanceStatusEvaluator.Evaluate(item, EngineVersion);

        Assert.Equal("MissingInput", result.Status);
        Assert.Equal("Missing: TreeTrunk_DBH, iTreeInput_CrownExposure", result.Details);
    }

    [Fact]
    public void Reports_invalid_input_for_an_out_of_range_crown_exposure()
    {
        var item = Complete(crownExposure: 9);

        var result = PlantingInstanceStatusEvaluator.Evaluate(item, EngineVersion);

        Assert.Equal("InvalidInput", result.Status);
        Assert.Contains("CrownExposure", result.Details);
    }

    [Fact]
    public void Reports_invalid_input_for_an_unrecognized_condition()
    {
        var item = Complete(condition: "Sickly");

        var result = PlantingInstanceStatusEvaluator.Evaluate(item, EngineVersion);

        Assert.Equal("InvalidInput", result.Status);
    }

    [Fact]
    public void A_never_calculated_valid_instance_is_ready()
    {
        var item = Complete();

        var result = PlantingInstanceStatusEvaluator.Evaluate(item, EngineVersion);

        Assert.Equal("Ready", result.Status);
        Assert.NotEmpty(result.InputSignature);
    }

    [Fact]
    public void An_unchanged_previously_calculated_instance_reports_calculated()
    {
        var partial = Complete();
        var signature = PlantingInstanceStatusEvaluator.Evaluate(partial, EngineVersion).InputSignature;
        var item = Complete(storedStatus: "Calculated", storedInputSignature: signature);

        var result = PlantingInstanceStatusEvaluator.Evaluate(item, EngineVersion);

        Assert.Equal("Calculated", result.Status);
    }

    [Fact]
    public void A_changed_previously_calculated_instance_is_stale_not_ready()
    {
        var item = Complete(storedStatus: "Calculated", storedInputSignature: "stale-signature-from-before");

        var result = PlantingInstanceStatusEvaluator.Evaluate(item, EngineVersion);

        Assert.Equal("Stale", result.Status);
    }

    [Fact]
    public void A_changed_instance_that_was_never_successfully_calculated_is_ready_not_stale()
    {
        var item = Complete(storedStatus: "APIError", storedDetails: "timeout", storedInputSignature: "old-signature");

        var result = PlantingInstanceStatusEvaluator.Evaluate(item, EngineVersion);

        Assert.Equal("Ready", result.Status);
    }

    [Fact]
    public void An_unchanged_previously_errored_instance_still_reports_the_error_not_ready()
    {
        var partial = Complete();
        var signature = PlantingInstanceStatusEvaluator.Evaluate(partial, EngineVersion).InputSignature;
        var item = Complete(storedStatus: "APIError", storedDetails: "The i-Tree API timed out.", storedInputSignature: signature);

        var result = PlantingInstanceStatusEvaluator.Evaluate(item, EngineVersion);

        Assert.Equal("APIError", result.Status);
        Assert.Equal("The i-Tree API timed out.", result.Details);
    }
}
