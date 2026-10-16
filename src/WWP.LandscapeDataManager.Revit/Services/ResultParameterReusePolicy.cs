namespace WWP.LandscapeDataManager.Revit.Services;

internal sealed record ExistingResultParameter(Guid Guid, string DataType, bool? IsInstance);

internal sealed record ResultParameterReuseDecision(Guid? ExistingGuid, string? Conflict)
{
    public bool CanReuse => ExistingGuid.HasValue;
}

/// <summary>
/// Result readers/writers use these exact names. Older catalogs issued different shared IDs,
/// so an unambiguous, compatible project binding can be retained without replacing its values
/// or breaking schedules that reference its ID. This exception does not apply to input fields.
/// </summary>
internal static class ResultParameterReusePolicy
{
    private static readonly HashSet<string> SupportedNames = new(StringComparer.Ordinal)
    {
        "!_S_PLT_BNGResult_BroadHabitat_Text",
        "!_S_PLT_BNGResult_AreaHectares_Text",
        "!_S_PLT_BNGResult_Distinctiveness_Text",
        "!_S_PLT_BNGResult_DistinctivenessScore_Text",
        "!_S_PLT_BNGResult_ConditionScore_Text",
        "!_S_PLT_BNGResult_StrategicSignificanceCategory_Text",
        "!_S_PLT_BNGResult_StrategicSignificanceMultiplier_Text",
        "!_S_PLT_BNGResult_StandardTimeToTarget_Text",
        "!_S_PLT_BNGResult_TimeToTargetStatus_Text",
        "!_S_PLT_BNGResult_FinalTimeToTarget_Text",
        "!_S_PLT_BNGResult_FinalTimeToTargetMultiplier_Text",
        "!_S_PLT_BNGResult_StandardDifficulty_Text",
        "!_S_PLT_BNGResult_AppliedDifficulty_Text",
        "!_S_PLT_BNGResult_FinalDifficulty_Text",
        "!_S_PLT_BNGResult_DifficultyMultiplier_Text",
        "!_S_PLT_BNGResult_HabitatUnits_Number",
        "!_S_PLT_BNGResult_Status_Text",
        "!_S_PLT_BNGResult_Details_Text",
        "!_S_PLT_BNGResult_InputSignature_Text",
        "!_S_PLT_BNGResult_LastUpdated_Text",
        "!_S_PLT_BNGResult_MetricVersion_Text",
        "!_S_PLT_BNGResult_Role_Text",
        "!_S_PLT_BNGResult_BaselineUnits_Number",
        "!_S_PLT_ConnectivityResult_Band_Number",
        "!_S_PLT_ConnectivityResult_Class_Text",
        "!_S_PLT_ConnectivityResult_Score_Number",
        "!_S_PLT_ConnectivityResult_dPC_Number",
        "!_S_PLT_ConnectivityResult_Details_Text",
        "!_S_PLT_ConnectivityResult_LastUpdated_Text"
    };

    public static bool Supports(string name) => SupportedNames.Contains(name);

    public static ResultParameterReuseDecision Evaluate(
        string name,
        string expectedDataType,
        IReadOnlyList<ExistingResultParameter> namedDefinitions,
        int namedBindingCount)
    {
        if (!Supports(name))
        {
            return new(null, "This parameter requires the catalog's shared ID.");
        }
        if (namedDefinitions.Count != 1 || namedBindingCount != 1)
        {
            return new(null, "Expected exactly one shared definition and one project binding with this name. Resolve the missing or duplicate bindings first.");
        }

        var existing = namedDefinitions[0];
        if (existing.Guid == Guid.Empty || existing.IsInstance != true)
        {
            return new(null, "The existing shared parameter must have an Instance project binding. Its binding and values were left unchanged.");
        }
        if (string.IsNullOrEmpty(expectedDataType) ||
            !string.Equals(existing.DataType, expectedDataType, StringComparison.Ordinal))
        {
            return new(null, "The existing result parameter has a different data type. Its binding and values were left unchanged.");
        }

        return new(existing.Guid, null);
    }
}
