using Autodesk.Revit.DB;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Builds one Planting + one Planting Area (Floor) schedule per Design Option, covering the exact
/// same parameter set the Dashboard reports (<c>DashboardTreeItem</c>/<c>DashboardFloorItem</c>),
/// grouped by species/landscape type with a Count column — the Revit-native mirror of the
/// Dashboard's "By species" and "Planting areas by type" subtotal tables.
///
/// Revit has no native way to show or filter a schedule by Design Option (confirmed live: it's
/// absent from <see cref="ScheduleDefinition.GetSchedulableFields"/>, <see cref="View.DesignOption"/>
/// reads null and isn't a model-element property for views, and <see cref="ScheduleFilterType"/> has
/// no option-related member). The workaround: every run stamps each instance's resolved Design
/// Option label onto <see cref="DesignOptionLabelParameter"/>, then filters each per-option schedule
/// on that ordinary text field.
/// </summary>
internal static class KpiScheduleBuilderService
{
    private const string DesignOptionLabelParameter = "!_S_PLT_Schedule_DesignOptionLabel_Text";
    private const string PlantingGroupingParameter = "!_S_PLT_iTreeSpecies_Code_Text";
    private const string FloorGroupingParameter = "!_S_PLT_LDS_Type_Text";

    private sealed record FieldSpec(string ParameterName, bool Totals);

    public sealed record ScheduleOutcome(string ScheduleName, bool Created);

    public sealed record KpiScheduleResult(
        bool DesignOptionLabelBound,
        IReadOnlyList<ScheduleOutcome> Created,
        IReadOnlyList<string> AlreadyExisting,
        IReadOnlyList<string> SkippedEmpty,
        IReadOnlyList<string> MissingFields);

    private static readonly IReadOnlyList<FieldSpec> PlantingFields =
    [
        new(PlantingGroupingParameter, Totals: false),
        new("!_S_PLT_iTreeSpecies_CommonName_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_ScientificName_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_Type_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_NativeStatus_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_BloomMonths_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_EcologicalFunctions_Text", Totals: false),
        new("!_S_PLT_iTreeResult_Status_Text", Totals: false),
        new("!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CarbonSequesteredLifetimeTotal_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CORemovedAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CORemovedLifetimeTotal_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_NO2RemovedAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_NO2RemovedLifetimeTotal_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_O3RemovedAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_O3RemovedLifetimeTotal_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_PM25RemovedAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_PM25RemovedLifetimeTotal_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_SO2RemovedAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_SO2RemovedLifetimeTotal_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_RainfallInterceptedAnnual_Volume", Totals: true),
        new("!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume", Totals: true),
        new("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume", Totals: true),
        new("!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume", Totals: true)
    ];

    private static readonly IReadOnlyList<FieldSpec> FloorFields =
    [
        new(FloorGroupingParameter, Totals: false),
        new("!_S_PLT_LDS_SurfaceClass_Text", Totals: false),
        new("Area", Totals: true),
        new("!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume", Totals: true),
        new("!_S_PLT_LDS_PollutantsRemovedAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_LDS_OxygenProducedAnnual_Mass", Totals: true),
        new("!_S_PLT_LDS_TotalGWP_Mass", Totals: true),
        new("!_S_PLT_LDS_SurfaceTempReduction_Number", Totals: false),
        new("!_S_PLT_LDS_AirTempReduction_Number", Totals: false)
    ];

    public static KpiScheduleResult CreateSchedules(Document document)
    {
        var plantingInstances = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Planting)
            .WhereElementIsNotElementType()
            .ToList();
        var floorInstances = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType()
            .ToList();

        // The whole per-option mechanism hinges on this one parameter being bound. Check it against
        // the first available instance rather than assuming — if it's missing, every schedule this
        // method would create is filtered on a field with no data in it, i.e. silently empty. Bail
        // out up front with a clear signal instead.
        var probeElement = plantingInstances.Concat(floorInstances).FirstOrDefault();
        var labelBound = probeElement is not null && probeElement.LookupParameter(DesignOptionLabelParameter) is not null;
        if (!labelBound)
        {
            return new KpiScheduleResult(false, [], [], [], []);
        }

        var missingFields = new HashSet<string>();
        var plantingByLabel = StampAndGroup(document, plantingInstances);
        var floorByLabel = StampAndGroup(document, floorInstances);

        var created = new List<ScheduleOutcome>();
        var alreadyExisting = new List<string>();
        var skippedEmpty = new List<string>();

        foreach (var label in plantingByLabel.Keys.Concat(floorByLabel.Keys).Distinct().OrderBy(label => label))
        {
            // The label (e.g. "Variante 1 : Baseline (Primary)") is a fine parameter value/filter
            // target as-is, but Revit view names disallow several characters that commonly appear in
            // Design Option/Set names (":", "{", "}", etc.) — sanitize only the name Revit itself sees.
            var nameSafeLabel = SanitizeForViewName(label);
            ProcessOne(
                $"KPI - Planting Report - {nameSafeLabel}",
                BuiltInCategory.OST_Planting, label, plantingByLabel.GetValueOrDefault(label), PlantingFields, PlantingGroupingParameter);
            ProcessOne(
                $"KPI - Planting Area Report - {nameSafeLabel}",
                BuiltInCategory.OST_Floors, label, floorByLabel.GetValueOrDefault(label), FloorFields, FloorGroupingParameter);
        }

        return new KpiScheduleResult(true, created, alreadyExisting, skippedEmpty, missingFields.OrderBy(field => field).ToList());

        void ProcessOne(string scheduleName, BuiltInCategory category, string label, int count, IReadOnlyList<FieldSpec> fields, string groupingParameter)
        {
            if (count == 0)
            {
                skippedEmpty.Add(scheduleName);
                return;
            }

            if (new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Any(schedule => schedule.Name == scheduleName))
            {
                alreadyExisting.Add(scheduleName);
                return;
            }

            BuildSchedule(document, scheduleName, category, label, fields, groupingParameter, missingFields);
            created.Add(new ScheduleOutcome(scheduleName, Created: true));
        }
    }

    private static Dictionary<string, int> StampAndGroup(Document document, IReadOnlyList<Element> instances)
    {
        var counts = new Dictionary<string, int>();
        foreach (var element in instances)
        {
            var label = FormatDesignOptionLabel(DashboardReportService.GetDesignOptionInfo(document, element));
            element.LookupParameter(DesignOptionLabelParameter)?.Set(label);
            counts[label] = counts.GetValueOrDefault(label) + 1;
        }

        return counts;
    }

    private static void BuildSchedule(
        Document document,
        string scheduleName,
        BuiltInCategory category,
        string label,
        IReadOnlyList<FieldSpec> fields,
        string groupingParameter,
        HashSet<string> missingFields)
    {
        var schedule = ViewSchedule.CreateSchedule(document, new ElementId(category));
        schedule.Name = scheduleName;
        var definition = schedule.Definition;

        var schedulableByName = new Dictionary<string, SchedulableField>();
        foreach (var field in definition.GetSchedulableFields())
        {
            string? name;
            try
            {
                name = field.GetName(document);
            }
            catch (Exception)
            {
                continue;
            }

            schedulableByName.TryAdd(name, field);
        }

        ScheduleFieldId? groupingFieldId = null;
        foreach (var spec in fields)
        {
            if (!schedulableByName.TryGetValue(spec.ParameterName, out var schedulableField))
            {
                missingFields.Add(spec.ParameterName);
                continue;
            }

            var scheduleField = definition.AddField(schedulableField);
            if (spec.Totals)
            {
                scheduleField.DisplayType = ScheduleFieldDisplayType.Totals;
            }

            if (spec.ParameterName == groupingParameter)
            {
                groupingFieldId = scheduleField.FieldId;
            }
        }

        if (schedulableByName.TryGetValue("Count", out var countField))
        {
            definition.AddField(countField);
        }

        // The label field drives the per-option filter; left as a normal visible column so it also
        // confirms at a glance which option this particular schedule reflects.
        var labelField = definition.AddField(schedulableByName[DesignOptionLabelParameter]);
        definition.AddFilter(new ScheduleFilter(labelField.FieldId, ScheduleFilterType.Equal, label));

        if (groupingFieldId is not null)
        {
            definition.IsItemized = false;
            definition.AddSortGroupField(new ScheduleSortGroupField(groupingFieldId));
        }
    }

    /// <summary>Revit view names disallow <c>\ : { } [ ] | ; &lt; &gt; ? `</c> — replace each with "-".</summary>
    private static string SanitizeForViewName(string text)
    {
        var invalidCharacters = new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`' };
        var sanitized = text;
        foreach (var character in invalidCharacters)
        {
            sanitized = sanitized.Replace(character, '-');
        }

        return sanitized;
    }

    private static string FormatDesignOptionLabel(DesignOptionInfo option)
    {
        if (string.IsNullOrWhiteSpace(option.SetName) && string.IsNullOrWhiteSpace(option.OptionName))
        {
            return "Primary model";
        }

        var name = string.IsNullOrWhiteSpace(option.SetName)
            ? option.OptionName ?? "Unnamed option"
            : $"{option.SetName} : {option.OptionName ?? "Unnamed option"}";
        return option.IsPrimary ? $"{name} (Primary)" : name;
    }
}
