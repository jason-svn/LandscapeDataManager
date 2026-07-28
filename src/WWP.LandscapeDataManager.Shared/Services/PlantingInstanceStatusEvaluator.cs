using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Classifies one Planting instance before any i-Tree API call is made — pure, unit-testable
/// logic kept separate from the Revit-side scan so "what counts as valid" never depends on a
/// live Revit session to verify.
/// </summary>
public static class PlantingInstanceStatusEvaluator
{
    private static readonly string[] ValidConditions = ["Excellent", "Good", "Fair", "Poor", "Critical", "Dying", "Dead"];

    public static PlantingInstanceStatus Evaluate(PlantingInstanceValidationItem item, string currentEngineVersion)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(item.SpeciesCode)) missing.Add("iTreeSpecies_Code");
        if (item.DbhInches is null) missing.Add("TreeTrunk_DBH");
        if (item.Years is null) missing.Add("TreeGrowth_Years");
        if (string.IsNullOrWhiteSpace(item.Condition)) missing.Add("iTreeInput_Condition");
        if (item.CrownExposure is null) missing.Add("iTreeInput_CrownExposure");
        if (item.Latitude is null || item.Longitude is null) missing.Add("iTreeLocation (Project Information)");

        if (missing.Count > 0)
        {
            return new PlantingInstanceStatus("MissingInput", $"Missing: {string.Join(", ", missing)}", string.Empty);
        }

        var invalid = new List<string>();
        if (item.CrownExposure is < 0 or > 5)
        {
            invalid.Add("iTreeInput_CrownExposure (must be 0-5)");
        }

        if (!ValidConditions.Contains(item.Condition, StringComparer.OrdinalIgnoreCase))
        {
            invalid.Add($"iTreeInput_Condition ('{item.Condition}' is not Excellent/Good/Fair/Poor/Critical/Dying/Dead)");
        }

        if (item.DbhInches is <= 0 or > 500)
        {
            invalid.Add("TreeTrunk_DBH (out of plausible range)");
        }

        if (item.Years is <= 0)
        {
            invalid.Add("TreeGrowth_Years (must be positive)");
        }

        if (invalid.Count > 0)
        {
            return new PlantingInstanceStatus("InvalidInput", $"Invalid: {string.Join(", ", invalid)}", string.Empty);
        }

        var currentSignature = InputSignatureCalculator.Compute(
            item.SpeciesCode!, item.Years!.Value, item.Condition!, item.CrownExposure!.Value,
            item.DbhInches!.Value, item.Latitude!.Value, item.Longitude!.Value, currentEngineVersion);

        if (string.IsNullOrEmpty(item.StoredStatus))
        {
            return new PlantingInstanceStatus("Ready", null, currentSignature);
        }

        if (string.Equals(item.StoredInputSignature, currentSignature, StringComparison.Ordinal))
        {
            // Nothing has changed since the last attempt — report exactly what that attempt found
            // instead of re-deriving it, so a stored APIWarning/APIError isn't silently forgotten.
            return new PlantingInstanceStatus(item.StoredStatus, item.StoredDetails, currentSignature);
        }

        return item.StoredStatus == "Calculated"
            ? new PlantingInstanceStatus("Stale", "Inputs changed since the last calculation.", currentSignature)
            : new PlantingInstanceStatus("Ready", null, currentSignature);
    }
}

/// <summary>Status is one of: Ready, MissingInput, InvalidInput, Calculated, APIWarning, APIError, Stale.</summary>
public sealed record PlantingInstanceStatus(string Status, string? Details, string InputSignature);
