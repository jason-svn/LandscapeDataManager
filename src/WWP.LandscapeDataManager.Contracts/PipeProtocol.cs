using System.Text.Json;

namespace WWP.LandscapeDataManager.Contracts;

public static class PipeCommands
{
    public const string GetStatus = "get-status";
    public const string ScanModel = "scan-model";
    public const string GetParameterCatalog = "get-parameter-catalog";
    public const string PreviewParameterWrites = "preview-parameter-writes";
    public const string ApplyParameterWrites = "apply-parameter-writes";
    public const string GetITreeInputs = "get-itree-inputs";
    public const string EnsureSharedParameters = "ensure-shared-parameters";
}

public sealed record PipeRequest(string RequestId, string Command, JsonElement? Payload);

public sealed record PipeResponse(
    string RequestId,
    bool Success,
    JsonElement? Data = null,
    string? Error = null);

public sealed record RevitStatus(
    string RevitVersion,
    bool HasActiveDocument,
    string? DocumentTitle);

public sealed record ModelScanOptions(bool PrimaryDesignOptionsOnly = true);

public sealed record ModelScanItem(
    string Category,
    string TypeName,
    long TypeId,
    int ElementCount,
    double AreaSquareMetres,
    string? CalculationType,
    bool IsAreaBased = false);

public sealed record ModelScanResult(
    string DocumentTitle,
    IReadOnlyList<ModelScanItem> Items);

public sealed record RevitParameterDescriptor(
    string Name,
    string Scope,
    string StorageType,
    string DataTypeId,
    bool IsWritable,
    IReadOnlyList<string> Categories);

public sealed record ParameterCatalogResult(
    string DocumentTitle,
    IReadOnlyList<RevitParameterDescriptor> Parameters,
    string PreferredUnitSystem = "Metric",
    string PreferredUnitSystemSource = "Default",
    string? UnitSystemWarning = null);

public sealed record ParameterMappingDefinition(
    string AirtableField,
    string RevitParameter,
    string Scope,
    string Conversion,
    bool Enabled = true);

public sealed record ParameterWriteItem(
    long TypeId,
    string TypeName,
    string SourceField,
    string RevitParameter,
    string Scope,
    string Conversion,
    string SourceValue,
    string? UnitMessage = null);

public sealed record ParameterWriteBatch(
    ModelScanOptions Options,
    IReadOnlyList<ParameterWriteItem> Items);

public sealed record ParameterWritePreviewRow(
    int ItemIndex,
    string TypeName,
    string RevitParameter,
    string Scope,
    string CurrentValue,
    string ProposedValue,
    int TargetCount,
    string Status,
    string? Message,
    bool CanApply);

public sealed record ParameterWriteResult(
    string DocumentTitle,
    IReadOnlyList<ParameterWritePreviewRow> Rows,
    int ChangedParameterCount,
    int ChangedElementCount,
    bool Applied);

public sealed record ITreeInputOptions(bool SelectedOnly = false);

public sealed record ITreeRevitInput(
    string SpeciesCode,
    string CommonName,
    string ScientificName,
    string FamilyName,
    string TypeName,
    long TypeId,
    string Condition,
    double DiameterInches,
    double Latitude,
    double Longitude,
    int Years,
    int CrownExposure);

public sealed record ITreeInputScanResult(
    string DocumentTitle,
    IReadOnlyList<ITreeRevitInput> Items,
    int SkippedWithoutSpeciesCode);

public sealed record EnsureSharedParametersRequest(string SharedParameterFilePath);

/// <summary>
/// One shared-parameter definition's binding outcome. Status is one of:
/// "Created" (binding added), "Already valid" (binding matched expectations),
/// "Conflict" (an existing name/GUID/scope mismatch was found and left untouched), or
/// "Error" (the definition could not be read or bound).
/// </summary>
public sealed record SharedParameterSetupRow(
    string Name,
    string Guid,
    string Scope,
    string Category,
    string Status,
    string? Message);

public sealed record SharedParameterSetupResult(
    string DocumentTitle,
    IReadOnlyList<SharedParameterSetupRow> Rows);

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static JsonElement ToElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);
}
