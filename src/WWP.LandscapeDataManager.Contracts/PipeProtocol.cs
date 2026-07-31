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
    public const string PreviewSharedParameters = "preview-shared-parameters";
    public const string EnsureSharedParameters = "ensure-shared-parameters";
    public const string ScanPlantingInstances = "scan-planting-instances";
    public const string PreviewInstanceParameterWrites = "preview-instance-parameter-writes";
    public const string ApplyInstanceParameterWrites = "apply-instance-parameter-writes";
    public const string PairSelectedInstance = "pair-selected-instance";
    public const string UpdateSpeciesCatalogue = "update-species-catalogue";
    public const string ValidatePlantingInstances = "validate-planting-instances";
    public const string SelectElements = "select-elements";
    public const string ZoomToElements = "zoom-to-elements";
    public const string IsolateElements = "isolate-elements";
    public const string ResetIsolation = "reset-isolation";
    public const string ApplyStatusColourOverrides = "apply-status-colour-overrides";
    public const string ResetColourOverrides = "reset-colour-overrides";
    public const string AssignSpeciesToSelection = "assign-species-to-selection";
    public const string GetProjectSiteLocation = "get-project-site-location";
    public const string PublishProjectLocation = "publish-project-location";
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
    bool IsAreaBased = false,
    string FamilyName = "");

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

/// <summary>Read-only check of the shared parameter file against the current document — nothing is changed.</summary>
public sealed record PreviewSharedParametersRequest(string SharedParameterFilePath);

/// <summary>Creates/validates only the parameters named in <see cref="IncludedParameterNames"/>, e.g. the subset the user left checked after reviewing the preview rows.</summary>
public sealed record EnsureSharedParametersRequest(string SharedParameterFilePath, IReadOnlyList<string> IncludedParameterNames);

/// <summary>
/// One shared-parameter definition's binding outcome. Status is one of:
/// "Will create" (preview only — no binding exists yet and none was created), "Created" (binding
/// added), "Already valid" (binding matched expectations), "Conflict" (an existing
/// name/GUID/scope mismatch was found and left untouched), or "Error" (the definition could not
/// be read or bound).
/// </summary>
public sealed record SharedParameterSetupRow(
    string Name,
    string Guid,
    string Scope,
    string Category,
    string Status,
    string? Message,
    string Description);

public sealed record SharedParameterSetupResult(
    string DocumentTitle,
    IReadOnlyList<SharedParameterSetupRow> Rows);

/// <summary>
/// One Planting instance's stable identity for source-data matching. <see cref="UniqueId"/> is
/// Revit's own stable per-element identifier; <see cref="SourceRecordId"/> is whatever external
/// record ID was written to it by a previous sync (empty until the first successful match).
/// Neither is a display name, by design.
/// </summary>
public sealed record PlantingInstanceScanItem(
    string UniqueId,
    long ElementId,
    string FamilyName,
    string TypeName,
    long TypeId,
    string SourceRecordId);

public sealed record PlantingInstanceScanResult(
    string DocumentTitle,
    IReadOnlyList<PlantingInstanceScanItem> Items);

public sealed record InstanceParameterWriteItem(
    string UniqueId,
    string RevitParameter,
    string Conversion,
    string SourceValue,
    string? UnitMessage = null);

public sealed record InstanceParameterWriteBatch(IReadOnlyList<InstanceParameterWriteItem> Items);

public sealed record InstanceParameterWritePreviewRow(
    int ItemIndex,
    string UniqueId,
    string RevitParameter,
    string CurrentValue,
    string ProposedValue,
    string Status,
    string? Message,
    bool CanApply);

public sealed record InstanceParameterWriteResult(
    string DocumentTitle,
    IReadOnlyList<InstanceParameterWritePreviewRow> Rows,
    int ChangedParameterCount,
    int ChangedElementCount,
    bool Applied);

/// <summary>
/// Pairs the single currently-selected Revit element with an external source record by writing
/// its stable ID into <c>!_S_PLANTING_DataSync_SourceRecordId_Text</c> — the explicit,
/// user-driven alternative to ever guessing a first-sync match by display name.
/// </summary>
public sealed record PairSelectedInstanceRequest(string SourceRecordId);

public sealed record PairSelectedInstanceResult(bool Paired, string? UniqueId, string? Message);

/// <summary>A single i-Tree species catalogue entry, in the shape the Revit side writes onto Planting types.</summary>
public sealed record SpeciesCatalogueRecord(
    string SpeciesCode,
    string CommonName,
    string ScientificName,
    string SpeciesType,
    string? ReplaceBy);

public sealed record UpdateSpeciesCatalogueRequest(IReadOnlyList<SpeciesCatalogueRecord> Records);

/// <summary>Status is one of: "Added", "Changed", "Deprecated", "Unchanged".</summary>
public sealed record SpeciesCatalogueUpdateRow(
    string SpeciesCode,
    string TypeName,
    string Status,
    string? Message);

public sealed record UpdateSpeciesCatalogueResult(
    string DocumentTitle,
    IReadOnlyList<SpeciesCatalogueUpdateRow> Rows,
    int SkippedNoMatchingType,
    int TypesMissingSpeciesCodeParameter,
    int TypesWithEmptySpeciesCode,
    int TotalPlantingTypes);

/// <summary>Writes one chosen species record onto the ElementType of every currently-selected Revit element (deduped by type).</summary>
public sealed record AssignSpeciesRequest(SpeciesCatalogueRecord Record);

public sealed record AssignSpeciesResult(string DocumentTitle, IReadOnlyList<string> UpdatedTypeNames);

/// <summary>
/// Whatever location is currently set on the document's built-in Site Location (Manage tab →
/// Location). This reflects the Revit template default until someone has actually set it, so a
/// non-null result here is not proof the project was deliberately geolocated.
/// </summary>
public sealed record ProjectSiteLocationResult(string DocumentTitle, double Latitude, double Longitude, string? PlaceName);

public sealed record PublishProjectLocationRequest(double Latitude, double Longitude);

public sealed record PublishProjectLocationResult(string DocumentTitle, double Latitude, double Longitude);

/// <summary>
/// A single Planting instance's raw i-Tree inputs plus whatever tracking values were stored by
/// the last calculation — read-only, no status classification (that's pure client-side logic,
/// see Shared.Services.PlantingInstanceStatusEvaluator, so it stays unit-testable).
/// </summary>
public sealed record PlantingInstanceValidationItem(
    string UniqueId,
    long ElementId,
    string FamilyName,
    string TypeName,
    string? SpeciesCode,
    int? Years,
    string? Condition,
    int? CrownExposure,
    double? DbhInches,
    double? Latitude,
    double? Longitude,
    string StoredStatus,
    string StoredDetails,
    string StoredLastUpdatedUtc,
    string StoredEngineVersion,
    string StoredInputSignature);

public sealed record ValidatePlantingInstancesRequest(bool SelectedOnly = false);

public sealed record ValidatePlantingInstancesResult(
    string DocumentTitle,
    IReadOnlyList<PlantingInstanceValidationItem> Items);

public sealed record ElementSelectionRequest(IReadOnlyList<string> UniqueIds);

public sealed record StatusColourOverrideRequest(IReadOnlyDictionary<string, string> StatusByUniqueId);

public sealed record OperationResult(bool Success, string? Message = null);

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static JsonElement ToElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);
}
