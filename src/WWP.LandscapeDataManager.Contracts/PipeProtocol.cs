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
    public const string GetSelectedPlantingTypes = "get-selected-planting-types";
    public const string GetAllPlantingTypes = "get-all-planting-types";
    public const string AssignSpeciesBatch = "assign-species-batch";
    public const string GetProjectSiteLocation = "get-project-site-location";
    public const string PublishProjectLocation = "publish-project-location";
    public const string PublishPreferredCurrency = "publish-preferred-currency";
    public const string GetProjectSettingsJson = "get-project-settings-json";
    public const string PublishProjectSettingsJson = "publish-project-settings-json";
    public const string GetSelectedFloors = "get-selected-floors";
    public const string CalculateFloorsBatch = "calculate-floors-batch";
    public const string RunHealthCheck = "run-health-check";
    public const string GetDashboardReport = "get-dashboard-report";
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
    string FamilyName = "",
    string? SpeciesCode = null);

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
    string? UnitSystemWarning = null,
    string PreferredCurrency = "USD",
    string PreferredCurrencySource = "Default",
    string? CurrencyWarning = null,
    double PreferredCurrencyFactor = 1d);

public sealed record ParameterMappingDefinition(
    string AirtableField,
    string RevitParameter,
    string Scope,
    string Conversion,
    bool Enabled = true);

/// <summary>
/// Bulk instance-matching by value equality: every Planting instance's <see cref="RevitParameter"/>
/// value is matched against every source record's <see cref="SourceField"/> value, instead of
/// requiring each instance to be paired to a source record one at a time.
/// </summary>
public sealed record InstanceMatchKeySettings(string RevitParameter, string SourceField);

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
/// Neither is a display name, by design. <see cref="KeyParameterValue"/> is this instance's value
/// for whichever Revit parameter <see cref="PlantingInstanceScanOptions.KeyParameterName"/> named,
/// if any — the bulk alternative to pairing instances one at a time.
/// </summary>
public sealed record PlantingInstanceScanItem(
    string UniqueId,
    long ElementId,
    string FamilyName,
    string TypeName,
    long TypeId,
    string SourceRecordId,
    string? KeyParameterValue = null);

/// <summary>
/// <see cref="KeyParameterName"/> is an arbitrary Revit parameter (e.g. "Mark") whose per-instance
/// value should be read back alongside the usual scan fields, so it can be matched against a
/// source column instead of requiring every instance to be paired one at a time.
/// </summary>
public sealed record PlantingInstanceScanOptions(string? KeyParameterName = null);

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
/// its stable ID into <c>!_S_PLT_DataSync_SourceRecordId_Text</c> — the explicit,
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

/// <summary>One distinct Planting ElementType among the current Revit selection, deduped so multiple selected instances of the same type appear once.</summary>
public sealed record SelectedPlantingTypeItem(string UniqueId, string FamilyName, string TypeName, string? CurrentSpeciesCode);

public sealed record SelectedPlantingTypesResult(string DocumentTitle, IReadOnlyList<SelectedPlantingTypeItem> Items);

/// <summary>One row's chosen species, targeted at a specific ElementType by its stable UniqueId.</summary>
public sealed record TypeSpeciesAssignment(string TypeUniqueId, SpeciesCatalogueRecord Record);

/// <summary>Writes each assignment's species record onto its target ElementType, in one transaction.</summary>
public sealed record AssignSpeciesBatchRequest(IReadOnlyList<TypeSpeciesAssignment> Assignments);

public sealed record AssignSpeciesBatchResult(string DocumentTitle, IReadOnlyList<string> UpdatedTypeNames);

/// <summary>
/// Whatever location is currently set on the document's built-in Site Location (Manage tab →
/// Location). This reflects the Revit template default until someone has actually set it, so a
/// non-null result here is not proof the project was deliberately geolocated.
/// </summary>
public sealed record ProjectSiteLocationResult(string DocumentTitle, double Latitude, double Longitude, string? PlaceName);

public sealed record PublishProjectLocationRequest(double Latitude, double Longitude);

public sealed record PublishProjectLocationResult(string DocumentTitle, double Latitude, double Longitude);

/// <summary>
/// The project's ISO 4217 currency code (see <c>RevitModelScanner.SupportedCurrencyCodes</c> for
/// the offered list) for reporting i-Tree monetary benefits — same role as
/// <see cref="ParameterCatalogResult.PreferredUnitSystem"/>, just for currency instead of
/// Metric/Imperial. <see cref="CurrencyFactor"/> is the USD-to-<see cref="CurrencyCode"/> rate the
/// caller has already resolved (1.0 for USD) — this call only writes it, it never looks up rates
/// itself, so every currency-setting call site (i-Tree Calculator's dropdown, Location Finder's
/// auto-match) stays in control of where that rate comes from.
/// </summary>
public sealed record PublishPreferredCurrencyRequest(string CurrencyCode, double CurrencyFactor = 1d);

public sealed record PublishPreferredCurrencyResult(string DocumentTitle, string CurrencyCode, double CurrencyFactor);

/// <summary>
/// Raw JSON stored on <c>!_S_PLT_Settings_Json_Text</c> (a multiline text Project Information
/// parameter) — opaque at this layer by design, so the Revit side never needs to know the shape of
/// <c>ProjectSettingsSnapshot</c> (defined in the Shared project, which this Contracts project
/// cannot reference without a circular dependency). <see cref="SettingsJson"/> is null/empty when
/// nothing has been saved to this project yet.
/// </summary>
public sealed record GetProjectSettingsJsonResult(string DocumentTitle, string? SettingsJson);

public sealed record PublishProjectSettingsJsonRequest(string SettingsJson);

public sealed record PublishProjectSettingsJsonResult(string DocumentTitle);

/// <summary>
/// One row of the WWP landscape data sheet (Airtable-sourced, cached locally) — the phase 2
/// coefficient table Floor Calculator matches against. Metric fields are nullable since the
/// sheet doesn't populate every metric for every row. <see cref="MatchKey"/> is the stable
/// composite key (Category|SubCategory|TypeName) written onto a Floor once assigned, so a later
/// recalculation can re-find the exact row without re-matching by name. <see cref="PlantingTypeCode"/>
/// is the sheet's "Planting type" column — a Revit-facing code (e.g. "WWP_Wetland",
/// "WWP_Vegetation_Hedge") that Floor families are typically named after directly, so it's matched
/// first before falling back to Category/SubCategory/Types.
/// </summary>
public sealed record WwpLdsCoefficientRecord(
    string MatchKey,
    string Origin,
    string PlantingTypeCode,
    string Category,
    string SubCategory,
    string TypeName,
    double? CostSavedAnnual,
    double? OxygenProducedAnnual,
    double? TotalGwp,
    double? Co2SequesteredAnnual,
    double? RunoffAvoidedAnnual,
    double? PollutionMassRemovedAnnual,
    double? SurfaceTempReduction,
    double? AirTempReduction);

/// <summary>One distinct selected Floor instance, with whatever WWP landscape data sheet type is already assigned (if any).</summary>
public sealed record SelectedFloorItem(
    string UniqueId,
    string FamilyName,
    string TypeName,
    double AreaSquareMeters,
    string? CurrentLdsType,
    string? CurrentLdsMatchKey);

public sealed record SelectedFloorsResult(string DocumentTitle, IReadOnlyList<SelectedFloorItem> Items);

/// <summary>
/// The final values to write onto one Floor — already computed client-side (coefficient times
/// area, or as-is for the two intrinsic temperature fields), with any per-metric manual entry
/// from the tool's own UI already substituted in. Revit-side just writes these; there's no
/// separate override parameter to check, since "manual" only ever means "the tool sent a
/// different number than the coefficient table would have."
/// </summary>
public sealed record FloorLdsValues(
    string LdsType,
    string MatchKey,
    string ResultSource,
    double Co2SequesteredAnnual,
    double RunoffAvoidedAnnual,
    double PollutionMassRemovedAnnual,
    double CostSavedAnnual,
    double OxygenProducedAnnual,
    double TotalGwp,
    double SurfaceTempReduction,
    double AirTempReduction);

public sealed record FloorLdsAssignment(string FloorUniqueId, FloorLdsValues Values);

public sealed record CalculateFloorsBatchRequest(IReadOnlyList<FloorLdsAssignment> Assignments);

public sealed record FloorCalculationRow(string UniqueId, string ResultSource);

public sealed record CalculateFloorsBatchResult(string DocumentTitle, IReadOnlyList<FloorCalculationRow> Rows);

/// <summary>
/// One Planting or Floor instance reduced to a single Success/NeedsAttention verdict for the
/// Health Check tool — Status is one of "Success" or "NeedsAttention", matching the colour keys
/// the Revit-side view-override service already knows how to apply.
/// </summary>
public sealed record HealthCheckItem(
    string UniqueId,
    string Category,
    string FamilyName,
    string TypeName,
    string Status,
    string Reason);

/// <summary>
/// Floor items plus project-wide setup gaps (e.g. a shared parameter never bound at all) that
/// would otherwise make every single Floor look identically broken for the same root cause.
/// Planting items are not included here — the client evaluates those itself via
/// <see cref="ValidatePlantingInstancesResult"/> and the shared PlantingInstanceStatusEvaluator,
/// the same way i-Tree Calculator and Refresh &amp; Audit already do, so Health Check never
/// duplicates that business logic.
/// </summary>
public sealed record HealthCheckResult(
    string DocumentTitle,
    IReadOnlyList<HealthCheckItem> FloorItems,
    IReadOnlyList<string> ProjectWarnings);

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

/// <summary>
/// One distinct Design Option combination an element can sit in. <see cref="SetName"/>/<see cref="OptionName"/>
/// are both null when the element isn't part of any Design Option set at all (the ordinary case for
/// most models) — <see cref="IsPrimary"/> is true for both that case and for the primary option of a
/// set, matching how <c>RevitModelScanner.IsInPrimaryDesignOption</c> already treats "no set" as primary.
/// </summary>
public sealed record DesignOptionInfo(string? SetName, string? OptionName, bool IsPrimary);

/// <summary>
/// One Planting instance's identity, placement, and every stored i-Tree benefit result (Annual and
/// LifetimeTotal pairs), read back as-is — no aggregation, no unit/currency normalization. That happens
/// client-side in <c>DashboardAggregationService</c> using <see cref="StoredUnitSystem"/>/
/// <see cref="StoredCurrency"/>/<see cref="StoredExchangeRateUsed"/> to know what basis these raw
/// numbers are actually in. <see cref="NativeStatus"/>/<see cref="BloomMonths"/>/
/// <see cref="EcologicalFunctions"/> are person-entered Site &amp; Biodiversity reference data (Planting
/// Type scope); <see cref="CanopyAreaSquareMeters"/> is derived from the Planting instance's own
/// <c>!_S_PLT_TreeFoliage_Width</c> (crown width), not looked up from a shared parameter of its own.
/// </summary>
public sealed record DashboardTreeItem(
    string UniqueId,
    long ElementId,
    string FamilyName,
    string TypeName,
    string? SpeciesCode,
    string? CommonName,
    string? ScientificName,
    string? SpeciesType,
    string? NativeStatus,
    string? BloomMonths,
    string? EcologicalFunctions,
    double CanopyAreaSquareMeters,
    string? LevelName,
    DesignOptionInfo DesignOption,
    string Status,
    string StoredUnitSystem,
    string StoredCurrency,
    double StoredExchangeRateUsed,
    double CarbonSequesteredAnnual,
    double CarbonSequesteredLifetimeTotal,
    double CORemovedAnnual,
    double CORemovedLifetimeTotal,
    double NO2RemovedAnnual,
    double NO2RemovedLifetimeTotal,
    double O3RemovedAnnual,
    double O3RemovedLifetimeTotal,
    double PM25RemovedAnnual,
    double PM25RemovedLifetimeTotal,
    double SO2RemovedAnnual,
    double SO2RemovedLifetimeTotal,
    double CostSavedAnnual,
    double CostSavedLifetimeTotal,
    double CarbonCostSavedAnnual,
    double CarbonCostSavedLifetimeTotal,
    double StormWaterCostSavedAnnual,
    double StormWaterCostSavedLifetimeTotal,
    double AirPollutionCostSavedAnnual,
    double AirPollutionCostSavedLifetimeTotal,
    double RainfallInterceptedAnnual,
    double RainfallInterceptedLifetimeTotal,
    double RunoffAvoidedAnnual,
    double RunoffAvoidedLifetimeTotal,
    double CO2EquivalentAnnual,
    double CO2EquivalentLifetimeTotal);

/// <summary>
/// One Floor (planted/paved landscape area) instance's identity, placement, and stored LDS/i-Tree
/// results, per <c>FloorLdsCalculationService</c>. Floor cost is reported as-calculated (no currency
/// provenance is tracked for Floors today; see <c>WWP.LandscapeDataManager.App.FloorCalculator</c>,
/// which never calls <c>ExchangeRateService</c>).
/// </summary>
public sealed record DashboardFloorItem(
    string UniqueId,
    long ElementId,
    string FamilyName,
    string TypeName,
    string? LdsType,
    string? SurfaceClass,
    string? LevelName,
    DesignOptionInfo DesignOption,
    double AreaSquareMeters,
    double CarbonSequesteredAnnual,
    double RunoffAvoidedAnnual,
    double PollutionMassRemovedAnnual,
    double CostSavedAnnual,
    double OxygenProducedAnnual,
    double TotalGwp,
    double SurfaceTempReduction,
    double AirTempReduction);

/// <summary>
/// One Lighting Fixture instance's identity, placement, and dark-sky compliance tag — the compliance
/// value itself is Type-scoped (<c>!_S_PLT_Lighting_DarkSkyCompliant_Text</c>) since fixture shielding
/// is a fixture-model property, not something that varies instance to instance.
/// </summary>
public sealed record DashboardLightingItem(
    string UniqueId,
    long ElementId,
    string FamilyName,
    string TypeName,
    string? LevelName,
    DesignOptionInfo DesignOption,
    string? DarkSkyCompliant);

public sealed record DashboardReportRequest(bool SelectedOnly = false);

public sealed record DashboardReportResult(
    string DocumentTitle,
    string PreferredUnitSystem,
    string PreferredCurrency,
    double? SiteTotalAreaSquareMeters,
    double? HabitatConnectivityScore,
    IReadOnlyList<DashboardTreeItem> Trees,
    IReadOnlyList<DashboardFloorItem> Floors,
    IReadOnlyList<DashboardLightingItem> Lighting);

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static JsonElement ToElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);
}
