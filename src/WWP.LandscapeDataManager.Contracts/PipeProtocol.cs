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
    public const string PublishPreferredUnitSystem = "publish-preferred-unit-system";
    public const string GetProjectSettingsJson = "get-project-settings-json";
    public const string PublishProjectSettingsJson = "publish-project-settings-json";
    public const string GetSelectedFloors = "get-selected-floors";
    public const string CalculateFloorsBatch = "calculate-floors-batch";
    public const string GetBngFloors = "get-bng-floors";
    public const string WriteBngFloors = "write-bng-floors";
    public const string GetDashboardReport = "get-dashboard-report";
    public const string GetConnectivityPatches = "get-connectivity-patches";
    public const string WriteConnectivityResults = "write-connectivity-results";
    public const string CreateConnectivityHeatmapView = "create-connectivity-heatmap-view";
    public const string GetGrowthProjectionStatus = "get-growth-projection-status";
    public const string RunGrowthProjection = "run-growth-projection";
    public const string GetViewDesignScenario = "get-view-design-scenario";
    public const string GetTreeTimelines = "get-tree-timelines";
    public const string SaveTreeTimelines = "save-tree-timelines";
    public const string GetOrganizerTrees = "get-organizer-trees";
    public const string SortTreesIntoWorksets = "sort-trees-into-worksets";
    public const string DeleteTrees = "delete-trees";
    public const string GetSceneGeometry = "get-scene-geometry";
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
public sealed record PreviewSharedParametersRequest;

/// <summary>Creates/validates only the parameters named in <see cref="IncludedParameterNames"/>, e.g. the subset the user left checked after reviewing the preview rows.</summary>
public sealed record EnsureSharedParametersRequest(IReadOnlyList<string> IncludedParameterNames);

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
/// <summary>
/// The project's location. <see cref="IsPublished"/>: true when it's the i-Tree location Location
/// Finder published (what every calculation uses), false when it's only Revit's Site Location, null
/// from a connector that predates the flag.
/// </summary>
public sealed record ProjectSiteLocationResult(string DocumentTitle, double Latitude, double Longitude, string? PlaceName, bool? IsPublished = null);

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
/// The project's Metric/Imperial preference for displaying i-Tree's Mass/Volume benefit results —
/// same role as <see cref="PublishPreferredCurrencyRequest"/>, just for units instead of currency.
/// Unlike currency, this never rewrites any stored value: Mass/Volume result parameters are always
/// written in raw SI (kg/m³) and displayed per Revit's own project unit settings, so flipping this
/// preference only changes Document Units — see <c>ProjectPreferencesService.PublishPreferredUnitSystem</c>.
/// </summary>
public sealed record PublishPreferredUnitSystemRequest(string UnitSystem);

public sealed record PublishPreferredUnitSystemResult(string DocumentTitle, string UnitSystem, string? Warning = null);

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
    double? AirTempReduction,
    string? SpeciesCode = null,
    double? PollenRate = null,
    double? IrrigationDemandFactor = null,
    string? Allergenic = null);

/// <summary>One distinct selected Floor instance, with whatever WWP landscape data sheet type is already assigned (if any).</summary>
public sealed record SelectedFloorItem(
    string UniqueId,
    string FamilyName,
    string TypeName,
    double AreaSquareMeters,
    string? CurrentLdsType,
    string? CurrentLdsMatchKey);

/// <summary><see cref="AllFloors"/> loads every Floor instance in the project instead of the current selection.</summary>
public sealed record SelectedFloorsRequest(bool AllFloors = false);

public sealed record SelectedFloorsResult(string DocumentTitle, IReadOnlyList<SelectedFloorItem> Items);

/// <summary>
/// The final values to write onto one Floor — already computed client-side (coefficient times
/// area, or as-is for the two intrinsic temperature fields), with any per-metric manual entry
/// from the tool's own UI already substituted in. Revit-side just writes these; there's no
/// separate override parameter to check, since "manual" only ever means "the tool sent a
/// different number than the coefficient table would have."
/// </summary>
/// <param name="CurrencyUsed">ISO code (USD, GBP, EUR, CAD, AUD, or NZD) <see cref="CostSavedAnnual"/> was converted into — the caller has already resolved the project's currency preference and applied its rate before sending, the same way i-Tree Calculator does for trees.</param>
/// <param name="ExchangeRateUsed">USD exchange rate applied to <see cref="CostSavedAnnual"/> (1.0 for USD) — recorded so a later currency switch can rescale this floor's cost-saved figure from its own stored rate.</param>
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
    double AirTempReduction,
    string CurrencyUsed = "USD",
    double ExchangeRateUsed = 1d,
    double PollenAnnual = 0);

public sealed record FloorLdsAssignment(string FloorUniqueId, FloorLdsValues Values);

public sealed record CalculateFloorsBatchRequest(IReadOnlyList<FloorLdsAssignment> Assignments);

/// <param name="NotWritten">Parameters that couldn't be written to this floor (missing — not set up in this project — or read-only); empty when everything was written.</param>
public sealed record FloorCalculationRow(string UniqueId, string ResultSource, IReadOnlyList<string>? NotWritten = null);

public sealed record CalculateFloorsBatchResult(string DocumentTitle, IReadOnlyList<FloorCalculationRow> Rows);

/// <summary>Which floors Floor Calculator's BNG tab loads: the current Revit selection, or every Floor instance in the model.</summary>
public sealed record BngFloorsRequest(bool SelectionOnly = true);

/// <summary>
/// A floor's BNG role from its Revit phases, relative to the project's first phase (the baseline,
/// e.g. "Existing") and last phase (the proposal, e.g. "New Construction"). A retained floor
/// flagged <c>!_S_PLT_BNGInput_Enhanced_YesNo</c> becomes <see cref="Enhanced"/> client-side.
/// </summary>
public static class BngPhaseRoles
{
    /// <summary>Created in the first phase and never demolished — baseline habitat kept (A-1 retained).</summary>
    public const string Retained = "Retained";

    /// <summary>A retained floor whose habitat is enhanced (A-1 enhanced + A-3).</summary>
    public const string Enhanced = "Enhanced";

    /// <summary>Created in the first phase, demolished in a later one — baseline habitat lost (A-1).</summary>
    public const string Lost = "Lost";

    /// <summary>Created after the first phase and never demolished — new habitat (A-2).</summary>
    public const string Created = "Created";

    /// <summary>Demolished in the same phase it was created — never on site at baseline or after development.</summary>
    public const string Excluded = "Excluded";
}

/// <summary>
/// One Floor instance with its stored BNG inputs (<c>!_S_PLT_BNGInput_*</c> plus the shared
/// <c>!_S_PLT_iTreeInput_Condition_Text</c>), its type's remembered habitat, and the bookkeeping
/// of its last BNG write — enough for the client to recalculate and to tell a current result
/// from a stale one. <see cref="ParametersBound"/> is false when the BNG parameters haven't been
/// set up in this project yet.
/// </summary>
public sealed record BngFloorItem(
    string UniqueId,
    string FamilyName,
    string TypeName,
    long TypeId,
    double AreaSquareMeters,
    bool ParametersBound,
    string? ProposedHabitat,
    string? Condition,
    string? StrategicSignificance,
    int YearOffset,
    string? TypeHabitatMapping,
    string? StoredStatus,
    string? StoredInputSignature,
    string? StoredLastUpdated,
    string PhaseRole = BngPhaseRoles.Created,
    string? PhaseCreated = null,
    string? PhaseDemolished = null,
    string? BaselineHabitat = null,
    string? BaselineCondition = null,
    string? Irreplaceable = null,
    bool Enhanced = false,
    bool Unique = false,
    bool UniqueSupported = false,
    DesignOptionInfo? DesignOption = null);

public sealed record BngFloorsResult(string DocumentTitle, IReadOnlyList<BngFloorItem> Items, IReadOnlyList<DesignOptionSetItem>? DesignOptionSets = null);

/// <summary>
/// Everything to write onto one Floor for the BNG tab — the inputs as edited in the tool, and
/// the A-2 row results already calculated client-side against the Statutory Biodiversity Metric
/// (Revit-side only writes). Result texts are exactly what the metric workbook displays.
/// </summary>
public sealed record BngFloorValues(
    string ProposedHabitat,
    string Condition,
    string StrategicSignificance,
    int YearOffset,
    string BroadHabitat,
    string AreaHectares,
    string Distinctiveness,
    string DistinctivenessScore,
    string ConditionScore,
    string StrategicSignificanceCategory,
    string StrategicSignificanceMultiplier,
    string StandardTimeToTarget,
    string TimeToTargetStatus,
    string FinalTimeToTarget,
    string FinalTimeToTargetMultiplier,
    string StandardDifficulty,
    string AppliedDifficulty,
    string FinalDifficulty,
    string DifficultyMultiplier,
    double HabitatUnits,
    string Status,
    string Details,
    string InputSignature,
    string MetricVersion,
    string Role = BngPhaseRoles.Created,
    string BaselineHabitat = "",
    string BaselineCondition = "",
    string Irreplaceable = "",
    bool Enhanced = false,
    double BaselineUnits = 0,
    bool Unique = false);

public sealed record BngFloorWrite(string FloorUniqueId, BngFloorValues Values);

/// <summary>A habitat to remember on a Floor type (<c>!_S_PLT_BNGInput_HabitatMapping_Text</c>) so new floors of that type pre-fill it.</summary>
public sealed record BngTypeMappingWrite(long TypeId, string Habitat);

public sealed record WriteBngFloorsRequest(IReadOnlyList<BngFloorWrite> Floors, IReadOnlyList<BngTypeMappingWrite> TypeMappings);

public sealed record BngWriteFailure(string UniqueId, string Error);

public sealed record WriteBngFloorsResult(
    string DocumentTitle,
    IReadOnlyList<string> WrittenUniqueIds,
    IReadOnlyList<BngWriteFailure> Failures,
    string LastUpdated);

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
    string StoredInputSignature,
    int? GrowthYear = null,
    bool InGroup = false,
    int? AgeAtPlanting = null,
    int? MatureAge = null,
    bool IsNewConstruction = false,
    bool IsGrowthCopy = false);

public sealed record ValidatePlantingInstancesRequest(bool SelectedOnly = false);

public sealed record ValidatePlantingInstancesResult(
    string DocumentTitle,
    IReadOnlyList<PlantingInstanceValidationItem> Items,
    TreeAgeSyncSummary? AgeSync = null);

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

/// <summary>One design option as Revit lists it, with or without anything modelled in it. <see cref="Name"/> has Revit's " &lt;primary&gt;" suffix removed.</summary>
public sealed record DesignOptionItem(string Name, bool IsPrimary);

/// <summary>One design option set and every option in it.</summary>
public sealed record DesignOptionSetItem(string SetName, IReadOnlyList<DesignOptionItem> Options);

/// <summary>
/// A scenario: one chosen option per design option set, e.g. "Variante 1" → "Baseline" and
/// "Test" → "Option 1". A set missing from <see cref="Choices"/> uses its primary option. Main-model
/// elements are in every scenario; an element in an option that isn't chosen is left out.
/// <see cref="IsExistingSite"/>: the site before development — what stood in the first phase (the
/// Dashboard's reference for every scheme) — rather than after it.
/// </summary>
public sealed record DesignScenario(string Name, IReadOnlyDictionary<string, string> Choices, bool IsExistingSite = false);

/// <summary>
/// The scenario the active view shows, worked out from which options have elements visible in it.
/// <see cref="Notes"/> says which sets fell back to their primary (nothing of theirs visible).
/// </summary>
public sealed record ViewDesignScenarioResult(
    string DocumentTitle,
    string ViewName,
    IReadOnlyDictionary<string, string> Choices,
    IReadOnlyList<string> Notes);

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
    double CO2EquivalentLifetimeTotal,
    string? Workset = null,
    double? CanopyCenterX = null,
    double? CanopyCenterY = null,
    double? CanopyRadiusMeters = null,
    double OxygenProducedAnnual = 0,
    double OxygenProducedLifetimeTotal = 0,
    double MaintenanceCostAnnual = 0,
    int? ConnectivityBand = null,
    string? TimelineKey = null,
    double PollutantsRemovedAnnual = 0,
    double PollenAnnual = 0,
    double? IrrigationDemandFactor = null,
    string? Allergenic = null,
    PlantingInstanceValidationItem? Inputs = null,
    string? PhaseRole = null,
    bool IsGrowthCopy = false);

/// <summary>
/// One Floor (planted/paved landscape area) instance's identity, placement, and stored LDS/i-Tree
/// results, per <c>FloorLdsCalculationService</c>. Floor cost is reported as-calculated (no currency
/// provenance is tracked for Floors today; see <c>WWP.LandscapeDataManager.App.FloorCalculator</c>,
/// which never calls <c>ExchangeRateService</c>). <see cref="Bng"/> is null when the BNG
/// parameters aren't bound in this project yet. <see cref="HasLdsResult"/>: Floor Calculator has
/// calculated the floor; null from a connector older than the field.
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
    double AirTempReduction,
    DashboardFloorBng? Bng = null,
    double MaintenanceCostAnnual = 0,
    bool IsStructural = false,
    int? ConnectivityBand = null,
    double PollenAnnual = 0,
    string? PhaseRole = null,
    bool? HasLdsResult = null);

/// <summary>
/// A Floor's stored BNG inputs and headline A-2 results, as written by Floor Calculator's BNG tab —
/// the inputs travel alongside so the Dashboard can recompute the input signature and tell a
/// current result from one whose area or inputs changed since (see <c>BngFloorStatusEvaluator</c>).
/// </summary>
public sealed record DashboardFloorBng(
    string? ProposedHabitat,
    string? Condition,
    string? StrategicSignificance,
    int YearOffset,
    string? BroadHabitat,
    string? Distinctiveness,
    double HabitatUnits,
    string? StoredStatus,
    string? StoredInputSignature,
    string? MetricVersion,
    string PhaseRole = BngPhaseRoles.Created,
    string? BaselineHabitat = null,
    string? BaselineCondition = null,
    string? Irreplaceable = null,
    bool Enhanced = false,
    double BaselineUnits = 0);

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

/// <summary>
/// One Revit property line (Site category) the Dashboard can use as the site boundary for Canopy
/// Cover and Softscape Surface Ratio. <see cref="IsClosed"/> is false for an open sketch, whose
/// area Revit doesn't compute.
/// </summary>
public sealed record DashboardPropertyLine(string UniqueId, string Name, double AreaSquareMeters, bool IsClosed);

public sealed record DashboardReportRequest(bool SelectedOnly = false);

/// <summary>
/// Everything the Dashboard reports on. <see cref="ModelWarnings"/>: project-wide setup problems
/// (parameters not bound, tree sizes stored as mm), null from an older connector.
/// </summary>
public sealed record DashboardReportResult(
    string DocumentTitle,
    string PreferredUnitSystem,
    string PreferredCurrency,
    double? SiteTotalAreaSquareMeters,
    double? HabitatConnectivityScore,
    IReadOnlyList<DashboardTreeItem> Trees,
    IReadOnlyList<DashboardFloorItem> Floors,
    IReadOnlyList<DashboardLightingItem> Lighting,
    IReadOnlyList<DashboardPropertyLine>? PropertyLines = null,
    IReadOnlyList<DesignOptionSetItem>? DesignOptionSets = null,
    ViewDesignScenarioResult? ViewScenario = null,
    IReadOnlyList<TreeBenefitTimeline>? TreeTimelines = null,
    IReadOnlyList<string>? ModelWarnings = null);

/// <summary>
/// One year of an i-Tree forecast for one tree, as i-Tree returned it in <c>benefits.annual[]</c>
/// (Year 1 is the first year after planting). Masses in kg, volumes in m³, money in USD; the tree's
/// size that year (DBH in cm, height in m, crown area in m²) as i-Tree grew it, 0 in timelines saved
/// before it was read.
/// </summary>
public sealed record TreeBenefitYear(
    int Year,
    double CarbonKg,
    double RunoffM3,
    double InterceptionM3,
    double CoKg,
    double No2Kg,
    double O3Kg,
    double So2Kg,
    double Pm25Kg,
    double CarbonUsd,
    double StormWaterUsd,
    double AirPollutionUsd,
    double DiameterCm = 0,
    double HeightM = 0,
    double CrownAreaM2 = 0)
{
    public double CrownWidthM => CrownWidth(CrownAreaM2);

    /// <summary>The width of a circle of i-Tree's crown area, in m; 0 without an area.</summary>
    public static double CrownWidth(double crownAreaM2) => crownAreaM2 > 0 ? 2 * System.Math.Sqrt(crownAreaM2 / System.Math.PI) : 0;
}

/// <summary>
/// i-Tree's year-by-year forecast for one unique planting input (species, planting DBH, condition,
/// crown exposure, location), up to <see cref="HorizonYears"/> — the tree's maturity, at most 35
/// years. Identical trees share one timeline through <see cref="Key"/>; after the horizon the tree's
/// benefit holds at its last (mature) year.
/// </summary>
public sealed record TreeBenefitTimeline(
    string Key,
    string SpeciesCode,
    double PlantingDbhCm,
    int HorizonYears,
    string CalculatedUtc,
    IReadOnlyList<TreeBenefitYear> Years);

public sealed record TreeTimelinesResult(string DocumentTitle, IReadOnlyList<TreeBenefitTimeline> Timelines);

/// <summary>Adds or replaces timelines by <see cref="TreeBenefitTimeline.Key"/>; the rest already stored are kept.</summary>
public sealed record SaveTreeTimelinesRequest(IReadOnlyList<TreeBenefitTimeline> Timelines);

public sealed record SaveTreeTimelinesResult(string DocumentTitle, int StoredCount);

/// <summary>A plan point in metres (Revit internal coordinates converted, not shared coordinates — only distances matter).</summary>
public sealed record ConnectivityPoint(double X, double Y);

/// <summary>
/// One Planting or Floor instance as a connectivity patch. A tree is its canopy circle
/// (<see cref="Center"/>/<see cref="RadiusMeters"/>, from <c>!_S_PLT_TreeFoliage_Width</c> or, failing
/// that, the family's bounding box — see <see cref="Note"/>); a floor is its top-face
/// <see cref="Outline"/>. <see cref="LdsType"/>/<see cref="SurfaceClass"/> let the tool decide which
/// floors are planting areas (see <c>ConnectivityPatchSelector</c>). <see cref="ParametersBound"/> is
/// false when the connectivity result parameters aren't set up in the project yet.
/// </summary>
public sealed record ConnectivityPatchItem(
    string UniqueId,
    long ElementId,
    string Category,
    string FamilyName,
    string TypeName,
    double AreaSquareMeters,
    ConnectivityPoint Center,
    double RadiusMeters,
    IReadOnlyList<ConnectivityPoint>? Outline,
    string? LdsType,
    string? SurfaceClass,
    bool Locked,
    bool ParametersBound,
    int? StoredBand,
    string? Note,
    DesignOptionInfo? DesignOption = null,
    string? PhaseRole = null);

/// <summary>
/// <see cref="AllDesignOptions"/>: every design option and phase, each item with its
/// <see cref="ConnectivityPatchItem.DesignOption"/> and <see cref="ConnectivityPatchItem.PhaseRole"/>,
/// so the tool can score any design scenario (the existing site too). Without it (and from a connector
/// that predates it) only the main model and primary options, not demolished: the built design.
/// </summary>
public sealed record ConnectivityPatchesRequest(bool IncludeFloors = true, bool AllDesignOptions = false);

/// <summary><see cref="DesignOptionSets"/>: every set and option in the model; null from a connector that predates it.</summary>
public sealed record ConnectivityPatchesResult(
    string DocumentTitle,
    IReadOnlyList<ConnectivityPatchItem> Items,
    IReadOnlyList<DesignOptionSetItem>? DesignOptionSets = null);

public sealed record ConnectivityResultValues(
    string UniqueId,
    int Band,
    string ClassName,
    double Score,
    double DpcPercent,
    string Details);

public sealed record WriteConnectivityResultsRequest(IReadOnlyList<ConnectivityResultValues> Items);

public sealed record ConnectivityWriteFailure(string UniqueId, string Reason);

/// <summary><see cref="SkippedLocked"/> are elements whose <c>!_S_PLT_ConnectivityInput_Locked_YesNo</c> is ticked — their hand-set score is left alone.</summary>
public sealed record WriteConnectivityResultsResult(
    string DocumentTitle,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> SkippedLocked,
    IReadOnlyList<ConnectivityWriteFailure> Failures,
    string LastUpdated);

/// <summary>
/// The Connectivity_Heatmap view and its filters, made or brought up to date. <see cref="Scored"/>
/// and <see cref="NotAssessed"/> count the trees and floors with and without a score (growth-year
/// copies, which the view hides, aren't counted).
/// </summary>
public sealed record ConnectivityHeatmapViewResult(
    string DocumentTitle,
    string ViewName,
    bool ViewCreated,
    int FiltersCreated,
    int Scored,
    int NotAssessed);

/// <summary>
/// One Planting instance for the 3D view: its trunk base (metres, Revit internal coordinates), its
/// size as the family draws it now, and what the family grows it from (see <c>TreeSizeForecast</c>):
/// age at planting, projected year and the i-Tree size stored on it (0 without one). A family
/// without LIM's size parameters gets its size from its bounding box.
/// </summary>
public sealed record SceneTreeItem(
    string UniqueId,
    string FamilyName,
    string TypeName,
    double X,
    double Y,
    double Z,
    double TrunkHeightM,
    double CrownHeightM,
    double CrownRadiusM,
    double TrunkRadiusM,
    double AgeAtPlanting,
    int? ProjectedYears,
    double ITreeHeightM,
    double ITreeCrownWidthM);

/// <summary>A planting type's growth: the family's yearly rates, its maximum size and <c>Mature Age</c> (0 when the family can't work one out).</summary>
public sealed record TreeGrowthType(
    string FamilyName,
    string TypeName,
    double MaxHeightM,
    double MaxWidthM,
    double HeightRateMPerYear,
    double WidthRateMPerYear,
    double TrunkRateCmPerYear,
    int MatureAge);

/// <summary>
/// A floor's top face triangulated, holes cut out: <see cref="Positions"/> holds x, y, z per vertex
/// (metres, Revit internal coordinates), <see cref="Indices"/> three vertex indices per triangle.
/// <see cref="HoleCount"/> is the face's inner loops.
/// </summary>
public sealed record SceneFloorItem(
    string UniqueId,
    IReadOnlyList<double> Positions,
    IReadOnlyList<int> Indices,
    int HoleCount);

/// <summary>Every tree and floor in the model, in every design option and phase: the JSON export picks each scenario's.</summary>
public sealed record SceneGeometryResult(
    string DocumentTitle,
    IReadOnlyList<SceneTreeItem> Trees,
    IReadOnlyList<TreeGrowthType> TreeTypes,
    IReadOnlyList<SceneFloorItem> Floors);

/// <summary>
/// The heatmap's steps over <c>!_S_PLT_ConnectivityResult_Score_Number</c> (0–1, the percentile rank by
/// dPC): ten equal steps, so each holds about a tenth of the scored elements, from cool blue (holds the
/// network together least) to red (most). Colours: Moreland's cool-warm map, without its grey middle.
/// </summary>
public static class ConnectivityHeatmap
{
    public const string ViewName = "Connectivity_Heatmap";
    public const string NotAssessedFilterName = "WWP_Connectivity not assessed";
    public const string GrowthCopiesFilterName = "WWP_Growth-year copies";

    public sealed record Step(double From, double To, byte Red, byte Green, byte Blue)
    {
        public string FilterName => FormattableString.Invariant($"WWP_Connectivity {From:0.0}-{To:0.0} score");

        /// <summary>The top step takes everything from <see cref="From"/> up, so a score of exactly 1 is in it.</summary>
        public bool IsTop => To >= 1;
    }

    public static readonly IReadOnlyList<Step> Steps =
    [
        new(0.0, 0.1, 0x3B, 0x4C, 0xC0),
        new(0.1, 0.2, 0x59, 0x77, 0xE3),
        new(0.2, 0.3, 0x7B, 0x9F, 0xF9),
        new(0.3, 0.4, 0x9E, 0xBE, 0xFF),
        new(0.4, 0.5, 0xC0, 0xD4, 0xF5),
        new(0.5, 0.6, 0xF2, 0xCB, 0xB7),
        new(0.6, 0.7, 0xF7, 0xAC, 0x8E),
        new(0.7, 0.8, 0xEE, 0x84, 0x68),
        new(0.8, 0.9, 0xD6, 0x52, 0x44),
        new(0.9, 1.0, 0xB4, 0x04, 0x26)
    ];

    /// <summary>The step a score falls in.</summary>
    public static Step StepOf(double score) =>
        Steps.FirstOrDefault(step => score >= step.From && (score < step.To || step.IsTop)) ?? (score < 0 ? Steps[0] : Steps[^1]);
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static JsonElement ToElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);
}
