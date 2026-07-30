using Autodesk.Revit.DB;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Creates the Planting Key Schedule if it doesn't already exist, or reconciles the managed
/// species columns (adding any that are missing, fixing their order, and re-asserting the
/// Scientific Name sort) on one that already does — so re-running this never produces a
/// duplicate schedule, and never forces the user to delete and re-create an existing schedule
/// just to pick up a column-order or new-field change. Shows one row per Planting type (not per
/// instance) by turning off "itemize every instance," since every field on it is a Type-level
/// species field.
/// </summary>
internal enum ScheduleEnsureStatus
{
    Created,
    Updated
}

internal static class PlantingKeyScheduleService
{
    private const string ScheduleName = "Planting Key Schedule";

    // Order matters: Scientific Name leads as the schedule's key/sort column, followed by
    // Common Name and Species Code, then the remaining species fields; Family and Type trails
    // as Revit-specific metadata rather than part of the species identity.
    private static readonly string[] DesiredFieldOrder =
    [
        "!_S_PLANTING_iTreeSpecies_ScientificName_Text",
        "!_S_PLANTING_iTreeSpecies_CommonName_Text",
        "!_S_PLANTING_iTreeSpecies_Code_Text",
        "!_S_PLANTING_iTreeSpecies_Type_Text",
        "!_S_PLANTING_iTreeSpecies_ReplaceBy_Text",
        "Family and Type"
    ];

    private const string ScientificNameFieldName = "!_S_PLANTING_iTreeSpecies_ScientificName_Text";

    public static ScheduleEnsureStatus EnsureSchedule(Document document)
    {
        var existingSchedule = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .FirstOrDefault(schedule => string.Equals(schedule.Name, ScheduleName, StringComparison.Ordinal));

        if (existingSchedule is not null)
        {
            ReconcileFields(document, existingSchedule.Definition);
            return ScheduleEnsureStatus.Updated;
        }

        var schedule = ViewSchedule.CreateSchedule(document, new ElementId(BuiltInCategory.OST_Planting));
        schedule.Name = ScheduleName;
        ReconcileFields(document, schedule.Definition);
        return ScheduleEnsureStatus.Created;
    }

    private static void ReconcileFields(Document document, ScheduleDefinition definition)
    {
        var schedulableFields = definition.GetSchedulableFields();

        var managedFieldIds = new List<ScheduleFieldId>();
        ScheduleField? scientificNameField = null;
        foreach (var name in DesiredFieldOrder)
        {
            var field = FindOrAddField(document, definition, schedulableFields, name);
            if (field is null)
            {
                continue;
            }

            managedFieldIds.Add(field.FieldId);
            if (name == ScientificNameFieldName)
            {
                scientificNameField = field;
            }
        }

        try
        {
            var currentOrder = definition.GetFieldOrder();
            var unmanagedTrailing = currentOrder.Where(id => !managedFieldIds.Contains(id)).ToList();
            definition.SetFieldOrder(managedFieldIds.Concat(unmanagedTrailing).ToList());
        }
        catch
        {
            // Best-effort: the schedule is still usable with its existing column order if this fails.
        }

        if (scientificNameField is not null)
        {
            try
            {
                var otherSortFields = definition.GetSortGroupFields()
                    .Where(sort => sort.FieldId != scientificNameField.FieldId)
                    .ToList();
                var newSortFields = new List<ScheduleSortGroupField> { new(scientificNameField.FieldId) };
                newSortFields.AddRange(otherSortFields);
                definition.SetSortGroupFields(newSortFields);
            }
            catch
            {
                // Best-effort: the schedule is still usable with its prior sort if this fails.
            }
        }

        try
        {
            definition.IsItemized = false;
        }
        catch
        {
            // Best-effort: the schedule is still usable per-instance if this setting can't apply.
        }
    }

    private static ScheduleField? FindOrAddField(
        Document document,
        ScheduleDefinition definition,
        IList<SchedulableField> schedulableFields,
        string name)
    {
        var match = schedulableFields.FirstOrDefault(candidate => TryGetName(candidate, document) == name);
        if (match is null)
        {
            return null;
        }

        for (var i = 0; i < definition.GetFieldCount(); i++)
        {
            var existingField = definition.GetField(i);
            if (existingField.ParameterId == match.ParameterId)
            {
                return existingField;
            }
        }

        return definition.AddField(match);
    }

    private static string? TryGetName(SchedulableField field, Document document)
    {
        try
        {
            return field.GetName(document);
        }
        catch
        {
            return null;
        }
    }
}
