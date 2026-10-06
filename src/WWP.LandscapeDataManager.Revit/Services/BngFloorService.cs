using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Floor Calculator's BNG tab: reports Floor instances with their stored Biodiversity Net Gain
/// inputs, and writes back the inputs plus the A-2 row results the tool calculated. Like
/// <see cref="FloorLdsCalculationService"/>, all calculation happens client-side against the
/// Statutory Biodiversity Metric tables — this service only reads area/inputs and writes the
/// finished values (see the <c>!_S_PLT_BNG*</c> parameters in <see cref="SharedParameterSetupService"/>).
/// </summary>
internal static class BngFloorService
{
    private const string ProposedHabitatParameter = "!_S_PLT_BNGInput_ProposedHabitat_Text";
    private const string StrategicSignificanceParameter = "!_S_PLT_BNGInput_StrategicSignificance_Text";
    private const string YearOffsetParameter = "!_S_PLT_BNGInput_CreationYearOffset_Number";
    private const string HabitatMappingParameter = "!_S_PLT_BNGInput_HabitatMapping_Text";
    // Shared with i-Tree's tree condition — see SharedParameterSetupService.
    private const string ConditionParameter = "!_S_PLT_iTreeInput_Condition_Text";

    private const string BroadHabitatParameter = "!_S_PLT_BNGResult_BroadHabitat_Text";
    private const string AreaHectaresParameter = "!_S_PLT_BNGResult_AreaHectares_Text";
    private const string DistinctivenessParameter = "!_S_PLT_BNGResult_Distinctiveness_Text";
    private const string DistinctivenessScoreParameter = "!_S_PLT_BNGResult_DistinctivenessScore_Text";
    private const string ConditionScoreParameter = "!_S_PLT_BNGResult_ConditionScore_Text";
    private const string StrategicCategoryParameter = "!_S_PLT_BNGResult_StrategicSignificanceCategory_Text";
    private const string StrategicMultiplierParameter = "!_S_PLT_BNGResult_StrategicSignificanceMultiplier_Text";
    private const string StandardTimeParameter = "!_S_PLT_BNGResult_StandardTimeToTarget_Text";
    private const string TimeStatusParameter = "!_S_PLT_BNGResult_TimeToTargetStatus_Text";
    private const string FinalTimeParameter = "!_S_PLT_BNGResult_FinalTimeToTarget_Text";
    private const string FinalTimeMultiplierParameter = "!_S_PLT_BNGResult_FinalTimeToTargetMultiplier_Text";
    private const string StandardDifficultyParameter = "!_S_PLT_BNGResult_StandardDifficulty_Text";
    private const string AppliedDifficultyParameter = "!_S_PLT_BNGResult_AppliedDifficulty_Text";
    private const string FinalDifficultyParameter = "!_S_PLT_BNGResult_FinalDifficulty_Text";
    private const string DifficultyMultiplierParameter = "!_S_PLT_BNGResult_DifficultyMultiplier_Text";
    private const string HabitatUnitsParameter = "!_S_PLT_BNGResult_HabitatUnits_Number";
    private const string StatusParameter = "!_S_PLT_BNGResult_Status_Text";
    private const string DetailsParameter = "!_S_PLT_BNGResult_Details_Text";
    private const string InputSignatureParameter = "!_S_PLT_BNGResult_InputSignature_Text";
    private const string LastUpdatedParameter = "!_S_PLT_BNGResult_LastUpdated_Text";
    private const string MetricVersionParameter = "!_S_PLT_BNGResult_MetricVersion_Text";
    internal const string BaselineHabitatParameter = "!_S_PLT_BNGInput_BaselineHabitat_Text";
    internal const string BaselineConditionParameter = "!_S_PLT_BNGInput_BaselineCondition_Text";
    internal const string IrreplaceableParameter = "!_S_PLT_BNGInput_Irreplaceable_Text";
    internal const string EnhancedParameter = "!_S_PLT_BNGInput_Enhanced_YesNo";
    internal const string UniqueParameter = "!_S_PLT_BNGInput_Unique_YesNo";
    private const string RoleParameter = "!_S_PLT_BNGResult_Role_Text";
    internal const string BaselineUnitsParameter = "!_S_PLT_BNGResult_BaselineUnits_Number";

    public static BngFloorsResult GetFloors(UIApplication application, BngFloorsRequest request)
    {
        var uiDocument = application.ActiveUIDocument
                         ?? throw new InvalidOperationException("Open a Revit project before loading floors.");
        var document = uiDocument.Document;

        IEnumerable<Element> floors;
        if (request.SelectionOnly)
        {
            var selectedIds = uiDocument.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                throw new InvalidOperationException("Select one or more Floor instances in Revit first, or load all floors.");
            }

            floors = selectedIds.Select(document.GetElement).Where(IsFloorInstance);
        }
        else
        {
            floors = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType();
        }

        var items = floors
            .Select(floor => ToItem(document, floor))
            .OrderBy(item => item.TypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.UniqueId, StringComparer.Ordinal)
            .ToList();

        if (request.SelectionOnly && items.Count == 0)
        {
            throw new InvalidOperationException("None of the selected elements are Floor instances.");
        }

        return new BngFloorsResult(document.Title, items);
    }

    public static WriteBngFloorsResult Write(UIApplication application, WriteBngFloorsRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before writing BNG results.");

        var written = new List<string>();
        var failures = new List<BngWriteFailure>();
        var lastUpdated = FormatLocalTimestamp();

        using var transaction = new Transaction(document, "LIM Write BNG Habitat Units");
        transaction.Start();
        try
        {
            foreach (var write in request.Floors)
            {
                if (document.GetElement(write.FloorUniqueId) is not { } floor || !IsFloorInstance(floor))
                {
                    failures.Add(new BngWriteFailure(write.FloorUniqueId, "The floor no longer exists in the model."));
                    continue;
                }

                if (floor.LookupParameter(ProposedHabitatParameter) is null || floor.LookupParameter(HabitatUnitsParameter) is null)
                {
                    failures.Add(new BngWriteFailure(write.FloorUniqueId, "The BNG parameters aren't set up in this project — run Parameters from the LIM ribbon first."));
                    continue;
                }

                var readOnly = WriteValues(floor, write.Values, lastUpdated);
                if (readOnly.Count > 0)
                {
                    failures.Add(new BngWriteFailure(write.FloorUniqueId, $"Could not write {string.Join(", ", readOnly)} (read-only — is the floor in a group, or its value driven by a formula?)."));
                    continue;
                }

                written.Add(floor.UniqueId);
            }

            foreach (var mapping in request.TypeMappings)
            {
                if (document.GetElement(new ElementId(mapping.TypeId)) is ElementType type &&
                    type.LookupParameter(HabitatMappingParameter) is { IsReadOnly: false } parameter)
                {
                    parameter.Set(mapping.Habitat);
                }
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        return new WriteBngFloorsResult(document.Title, written, failures, lastUpdated);
    }

    private static BngFloorItem ToItem(Document document, Element floor)
    {
        var elementType = document.GetElement(floor.GetTypeId()) as ElementType;
        var yearOffset = floor.LookupParameter(YearOffsetParameter) is { HasValue: true, StorageType: StorageType.Integer } offset
            ? offset.AsInteger()
            : 0;
        var (phaseRole, phaseCreated, phaseDemolished) = GetPhaseRole(document, floor);

        return new BngFloorItem(
            floor.UniqueId,
            elementType?.FamilyName ?? string.Empty,
            elementType?.Name ?? floor.Name,
            elementType?.Id.Value ?? -1,
            GetAreaSquareMeters(floor),
            floor.LookupParameter(ProposedHabitatParameter) is not null,
            GetText(floor, ProposedHabitatParameter),
            GetText(floor, ConditionParameter),
            GetText(floor, StrategicSignificanceParameter),
            yearOffset,
            elementType is null ? null : GetText(elementType, HabitatMappingParameter),
            GetText(floor, StatusParameter),
            GetText(floor, InputSignatureParameter),
            GetText(floor, LastUpdatedParameter),
            phaseRole,
            phaseCreated,
            phaseDemolished,
            GetText(floor, BaselineHabitatParameter),
            GetText(floor, BaselineConditionParameter),
            GetText(floor, IrreplaceableParameter),
            IsEnhanced(floor),
            IsYes(floor, UniqueParameter),
            floor.LookupParameter(UniqueParameter) is not null);
    }

    /// <summary>
    /// The floor's BNG role from its phases, relative to the project's first phase (the baseline,
    /// e.g. "Existing") and last phase (e.g. "New Construction"): created in the first phase and
    /// never demolished = Retained, demolished later = Lost; created later and never demolished =
    /// Created; demolished in the phase it was created = Excluded. A project with a single phase
    /// treats every floor as Created, as before phases were considered.
    /// </summary>
    internal static (string Role, string? Created, string? Demolished) GetPhaseRole(Document document, Element element)
    {
        var order = new Dictionary<long, int>();
        var index = 0;
        foreach (Phase phase in document.Phases)
        {
            order[phase.Id.Value] = index++;
        }

        var createdName = document.GetElement(element.CreatedPhaseId)?.Name;
        var demolishedName = element.DemolishedPhaseId == ElementId.InvalidElementId ? null : document.GetElement(element.DemolishedPhaseId)?.Name;
        if (order.Count <= 1 || !order.TryGetValue(element.CreatedPhaseId.Value, out var created))
        {
            return (BngPhaseRoles.Created, createdName, demolishedName);
        }

        int? demolished = order.TryGetValue(element.DemolishedPhaseId.Value, out var demolishedIndex) ? demolishedIndex : null;
        var role = (created, demolished) switch
        {
            (_, { } d) when d == created => BngPhaseRoles.Excluded,
            (0, null) => BngPhaseRoles.Retained,
            (0, _) => BngPhaseRoles.Lost,
            (_, null) => BngPhaseRoles.Created,
            _ => BngPhaseRoles.Excluded
        };
        return (role, createdName, demolishedName);
    }

    internal static bool IsEnhanced(Element floor) => IsYes(floor, EnhancedParameter);

    private static bool IsYes(Element floor, string name) =>
        floor.LookupParameter(name) is { HasValue: true, StorageType: StorageType.Integer } flag && flag.AsInteger() == 1;

    /// <summary>Writes every BNG parameter, returning the names of any that turned out to be read-only.</summary>
    private static List<string> WriteValues(Element floor, BngFloorValues values, string lastUpdated)
    {
        var readOnly = new List<string>();

        void Set(string name, string value)
        {
            if (floor.LookupParameter(name) is not { } parameter)
            {
                return;
            }

            if (parameter.IsReadOnly)
            {
                readOnly.Add(name);
                return;
            }

            parameter.Set(value);
        }

        Set(ProposedHabitatParameter, values.ProposedHabitat);
        Set(ConditionParameter, values.Condition);
        Set(StrategicSignificanceParameter, values.StrategicSignificance);
        if (floor.LookupParameter(YearOffsetParameter) is { } offset)
        {
            if (offset.IsReadOnly)
            {
                readOnly.Add(YearOffsetParameter);
            }
            else
            {
                offset.Set(values.YearOffset);
            }
        }

        Set(BroadHabitatParameter, values.BroadHabitat);
        Set(AreaHectaresParameter, values.AreaHectares);
        Set(DistinctivenessParameter, values.Distinctiveness);
        Set(DistinctivenessScoreParameter, values.DistinctivenessScore);
        Set(ConditionScoreParameter, values.ConditionScore);
        Set(StrategicCategoryParameter, values.StrategicSignificanceCategory);
        Set(StrategicMultiplierParameter, values.StrategicSignificanceMultiplier);
        Set(StandardTimeParameter, values.StandardTimeToTarget);
        Set(TimeStatusParameter, values.TimeToTargetStatus);
        Set(FinalTimeParameter, values.FinalTimeToTarget);
        Set(FinalTimeMultiplierParameter, values.FinalTimeToTargetMultiplier);
        Set(StandardDifficultyParameter, values.StandardDifficulty);
        Set(AppliedDifficultyParameter, values.AppliedDifficulty);
        Set(FinalDifficultyParameter, values.FinalDifficulty);
        Set(DifficultyMultiplierParameter, values.DifficultyMultiplier);
        if (floor.LookupParameter(HabitatUnitsParameter) is { } units)
        {
            if (units.IsReadOnly)
            {
                readOnly.Add(HabitatUnitsParameter);
            }
            else
            {
                units.Set(values.HabitatUnits);
            }
        }

        Set(StatusParameter, values.Status);
        Set(DetailsParameter, values.Details);
        Set(InputSignatureParameter, values.InputSignature);
        Set(LastUpdatedParameter, lastUpdated);
        Set(MetricVersionParameter, values.MetricVersion);

        Set(BaselineHabitatParameter, values.BaselineHabitat);
        Set(BaselineConditionParameter, values.BaselineCondition);
        Set(IrreplaceableParameter, values.Irreplaceable);
        Set(RoleParameter, values.Role);
        SetInteger(EnhancedParameter, values.Enhanced ? 1 : 0);
        SetInteger(UniqueParameter, values.Unique ? 1 : 0);
        SetDouble(BaselineUnitsParameter, values.BaselineUnits);
        return readOnly;

        void SetInteger(string name, int value)
        {
            if (floor.LookupParameter(name) is not { } parameter)
            {
                return;
            }

            if (parameter.IsReadOnly)
            {
                readOnly.Add(name);
            }
            else if (parameter.StorageType == StorageType.Integer && (!parameter.HasValue || parameter.AsInteger() != value))
            {
                parameter.Set(value);
            }
        }

        void SetDouble(string name, double value)
        {
            if (floor.LookupParameter(name) is not { } parameter)
            {
                return;
            }

            if (parameter.IsReadOnly)
            {
                readOnly.Add(name);
            }
            else
            {
                parameter.Set(value);
            }
        }
    }

    private static bool IsFloorInstance(Element? element) =>
        element is not null and not ElementType && element.Category?.BuiltInCategory == BuiltInCategory.OST_Floors;

    private static double GetAreaSquareMeters(Element floor)
    {
        var areaParameter = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        var internalArea = areaParameter is { HasValue: true } ? areaParameter.AsDouble() : 0.0;
        return UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);
    }

    private static string? GetText(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true, StorageType: StorageType.String } ? parameter.AsString() : null;
    }

    /// <summary>Same local wall-clock format as <see cref="FloorLdsCalculationService"/>'s LastCalculated stamp.</summary>
    private static string FormatLocalTimestamp()
    {
        var now = DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;
        var zoneName = zone.IsDaylightSavingTime(now) ? zone.DaylightName : zone.StandardName;
        return $"{now.ToString("yyyy-MM-dd, HH:mm:ss", CultureInfo.InvariantCulture)}, {zoneName}";
    }
}
