using Autodesk.Revit.DB;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Builds one Planting + one Planting Area (Floor) schedule per Design Option, covering the exact
/// same parameter set the Dashboard reports (<c>DashboardTreeItem</c>/<c>DashboardFloorItem</c>),
/// grouped by species/landscape type with a Count column — the Revit-native mirror of the
/// Dashboard's "By species" and "Planting areas by type" subtotal tables. Running it again adds
/// any newer parameters to the schedules an earlier version made (see <see cref="AddMissingFields"/>).
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

    /// <summary>An existing KPI schedule that was brought up to date, and how many columns it gained.</summary>
    public sealed record ScheduleUpdate(string ScheduleName, int AddedColumns);

    public sealed record KpiScheduleResult(
        bool DesignOptionLabelBound,
        IReadOnlyList<ScheduleOutcome> Created,
        IReadOnlyList<ScheduleUpdate> Updated,
        IReadOnlyList<string> UpToDate,
        IReadOnlyList<string> SkippedEmpty,
        IReadOnlyList<string> MissingFields);

    /// <summary>The Connectivity Calculator's result for each tree and planting area: band 1–4, its name, the score and dPC.</summary>
    private static readonly IReadOnlyList<FieldSpec> ConnectivityFields =
    [
        new("!_S_PLT_ConnectivityResult_Band_Number", Totals: false),
        new("!_S_PLT_ConnectivityResult_Class_Text", Totals: false),
        new("!_S_PLT_ConnectivityResult_Score_Number", Totals: false),
        new("!_S_PLT_ConnectivityResult_dPC_Number", Totals: false)
    ];

    // Grouped as the Dashboard groups them: identity, carbon, air, water, value, maintenance,
    // forecast outlooks, connectivity. Re-running adds any of these an older KPI schedule lacks.
    private static readonly IReadOnlyList<FieldSpec> PlantingFields =
    [
        new(PlantingGroupingParameter, Totals: false),
        new("!_S_PLT_iTreeSpecies_CommonName_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_ScientificName_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_Type_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_NativeStatus_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_BloomMonths_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_EcologicalFunctions_Text", Totals: false),
        new("!_S_PLT_iTreeSpecies_Allergenic_Text", Totals: false),
        new("!_S_PLT_iTreeResult_Status_Text", Totals: false),
        new("!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CarbonSequesteredLifetimeTotal_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CO2EquivalentAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CO2EquivalentLifetimeTotal_Mass", Totals: true),
        new("!_S_PLT_Result_OxygenProducedAnnual_Mass", Totals: true),
        new("!_S_PLT_Result_OxygenProducedLifetimeTotal_Mass", Totals: true),
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
        new("!_S_PLT_iTreeResult_RainfallInterceptedAnnual_Volume", Totals: true),
        new("!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume", Totals: true),
        new("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume", Totals: true),
        new("!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume", Totals: true),
        new("!_S_PLT_iTreeResult_CostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Currency", Totals: true),
        new("!_S_PLT_Result_MaintenanceCostAnnual_Currency", Totals: true),
        // i-Tree forecast totals over the standard outlooks (see TreeTimelineStore).
        .. new[] { 5, 10, 20, 25 }.SelectMany(years => new FieldSpec[]
        {
            new($"!_S_PLT_Result_CO2Equivalent{years}yr_Mass", Totals: true),
            new($"!_S_PLT_Result_RunoffAvoided{years}yr_Volume", Totals: true),
            new($"!_S_PLT_Result_PollutantsRemoved{years}yr_Mass", Totals: true),
            new($"!_S_PLT_Result_CostSaved{years}yr_Currency", Totals: true),
        }),
        .. ConnectivityFields
    ];

    private static readonly IReadOnlyList<FieldSpec> FloorFields =
    [
        new(FloorGroupingParameter, Totals: false),
        new("!_S_PLT_LDS_SurfaceClass_Text", Totals: false),
        new("Area", Totals: true),
        new("!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_CO2EquivalentAnnual_Mass", Totals: true),
        new("!_S_PLT_Result_OxygenProducedAnnual_Mass", Totals: true),
        new("!_S_PLT_Result_EmbodiedGWP_Mass", Totals: true),
        new("!_S_PLT_Result_PollutantsRemovedAnnual_Mass", Totals: true),
        new("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume", Totals: true),
        new("!_S_PLT_iTreeResult_CostSavedAnnual_Currency", Totals: true),
        new("!_S_PLT_Result_MaintenanceCostAnnual_Currency", Totals: true),
        new("!_S_PLT_LDS_SurfaceTempReduction_Number", Totals: false),
        new("!_S_PLT_LDS_AirTempReduction_Number", Totals: false),
        // Biodiversity net gain, as the Floor Calculator's BNG tab writes it: the floor's role, its
        // baseline (A-1) and its proposed or enhanced habitat, with both unit columns totalled.
        new("!_S_PLT_BNGResult_Role_Text", Totals: false),
        new("!_S_PLT_BNGInput_BaselineHabitat_Text", Totals: false),
        new("!_S_PLT_BNGInput_BaselineCondition_Text", Totals: false),
        new("!_S_PLT_BNGResult_BaselineUnits_Number", Totals: true),
        new("!_S_PLT_BNGInput_ProposedHabitat_Text", Totals: false),
        new("!_S_PLT_BNGResult_Distinctiveness_Text", Totals: false),
        new("!_S_PLT_iTreeInput_Condition_Text", Totals: false),
        new("!_S_PLT_BNGInput_StrategicSignificance_Text", Totals: false),
        new("!_S_PLT_BNGResult_HabitatUnits_Number", Totals: true),
        new("!_S_PLT_BNGResult_Status_Text", Totals: false),
        .. ConnectivityFields
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
            return new KpiScheduleResult(false, [], [], [], [], []);
        }

        var missingFields = new HashSet<string>();
        var plantingByLabel = StampAndGroup(document, plantingInstances);
        // Growth Projections puts each growth year's copies on its own workset. A schedule that
        // totalled them together would count every tree once per year, so group by workset first.
        var groupPlantingByWorkset = document.IsWorkshared && plantingInstances.Any(element =>
            GrowthYearNames.FromName(document.GetWorksetTable().GetWorkset(element.WorksetId)?.Name) is not null);
        var floorByLabel = StampAndGroup(document, floorInstances);

        var created = new List<ScheduleOutcome>();
        var updated = new List<ScheduleUpdate>();
        var upToDate = new List<string>();
        var skippedEmpty = new List<string>();

        foreach (var label in plantingByLabel.Keys.Concat(floorByLabel.Keys).Distinct().OrderBy(label => label))
        {
            // The label (e.g. "Variante 1 : Baseline (Primary)") is a fine parameter value/filter
            // target as-is, but Revit view names disallow several characters that commonly appear in
            // Design Option/Set names (":", "{", "}", etc.) — sanitize only the name Revit itself sees.
            var nameSafeLabel = SanitizeForViewName(label);
            ProcessOne(
                $"KPI - Planting Report - {nameSafeLabel}",
                BuiltInCategory.OST_Planting, label, plantingByLabel.GetValueOrDefault(label), PlantingFields, PlantingGroupingParameter,
                groupPlantingByWorkset);
            ProcessOne(
                $"KPI - Planting Area Report - {nameSafeLabel}",
                BuiltInCategory.OST_Floors, label, floorByLabel.GetValueOrDefault(label), FloorFields, FloorGroupingParameter,
                groupByWorkset: false);
        }

        return new KpiScheduleResult(true, created, updated, upToDate, skippedEmpty, missingFields.OrderBy(field => field).ToList());

        void ProcessOne(string scheduleName, BuiltInCategory category, string label, int count, IReadOnlyList<FieldSpec> fields, string groupingParameter, bool groupByWorkset)
        {
            if (count == 0)
            {
                skippedEmpty.Add(scheduleName);
                return;
            }

            if (new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .FirstOrDefault(schedule => schedule.Name == scheduleName) is { } existing)
            {
                var added = AddMissingFields(document, existing, fields, missingFields);
                if (added > 0)
                {
                    updated.Add(new ScheduleUpdate(scheduleName, added));
                }
                else
                {
                    upToDate.Add(scheduleName);
                }

                return;
            }

            BuildSchedule(document, scheduleName, category, label, fields, groupingParameter, groupByWorkset, missingFields);
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
        bool groupByWorkset,
        HashSet<string> missingFields)
    {
        var schedule = ViewSchedule.CreateSchedule(document, new ElementId(category));
        schedule.Name = scheduleName;
        var definition = schedule.Definition;
        var schedulableByName = SchedulableFieldsByName(document, definition);

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

        if (groupByWorkset &&
            definition.GetSchedulableFields().FirstOrDefault(field =>
                field.ParameterId == new ElementId(BuiltInParameter.ELEM_PARTITION_PARAM)) is { } worksetSchedulable)
        {
            // One block per growth year, each with its own subtotal footer.
            var worksetField = definition.AddField(worksetSchedulable);
            definition.IsItemized = false;
            definition.AddSortGroupField(new ScheduleSortGroupField(worksetField.FieldId) { ShowHeader = true, ShowFooter = true });
        }

        if (groupingFieldId is not null)
        {
            definition.IsItemized = false;
            definition.AddSortGroupField(new ScheduleSortGroupField(groupingFieldId));
        }
    }

    /// <summary>
    /// Brings a KPI schedule made by an earlier version up to the current field list: each missing
    /// column is inserted after the field that precedes it in the list, with its total turned on.
    /// Columns already there (even hidden or renamed ones), the option filter, grouping, formatting
    /// and sheet placement are left as they are. Returns how many columns were added.
    /// </summary>
    private static int AddMissingFields(Document document, ViewSchedule schedule, IReadOnlyList<FieldSpec> fields, HashSet<string> missingFields)
    {
        var definition = schedule.Definition;
        var schedulableByName = SchedulableFieldsByName(document, definition);
        var added = 0;
        var insertAt = 0;
        foreach (var spec in fields)
        {
            if (!schedulableByName.TryGetValue(spec.ParameterName, out var schedulableField))
            {
                missingFields.Add(spec.ParameterName);
                continue;
            }

            var order = definition.GetFieldOrder();
            var index = -1;
            for (var position = 0; position < order.Count; position++)
            {
                var field = definition.GetField(order[position]);
                if (field.FieldType == schedulableField.FieldType && field.ParameterId == schedulableField.ParameterId)
                {
                    index = position;
                    break;
                }
            }

            if (index >= 0)
            {
                insertAt = index + 1;
                continue;
            }

            var inserted = definition.InsertField(schedulableField, insertAt++);
            if (spec.Totals)
            {
                inserted.DisplayType = ScheduleFieldDisplayType.Totals;
            }

            added++;
        }

        return added;
    }

    private static Dictionary<string, SchedulableField> SchedulableFieldsByName(Document document, ScheduleDefinition definition)
    {
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

        return schedulableByName;
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

        // Revit appends " <primary>" to the primary option's name; "(Primary)" says it once.
        var optionName = DesignOptionScenarioService.CleanOptionName(option.OptionName);
        if (optionName.Length == 0)
        {
            optionName = "Unnamed option";
        }

        var name = string.IsNullOrWhiteSpace(option.SetName) ? optionName : $"{option.SetName} : {optionName}";
        return option.IsPrimary ? $"{name} (Primary)" : name;
    }
}
