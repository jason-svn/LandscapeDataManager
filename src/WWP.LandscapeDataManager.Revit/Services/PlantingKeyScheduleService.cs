using Autodesk.Revit.DB;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Creates the Planting Key Schedule if it doesn't already exist — check-before-create, so
/// running this repeatedly never produces duplicate schedules. Shows one row per Planting type
/// (not per instance) by turning off "itemize every instance," since every field on it is a
/// Type-level species field.
/// </summary>
internal static class PlantingKeyScheduleService
{
    private const string ScheduleName = "Planting Key Schedule";

    private static readonly string[] SpeciesFieldNames =
    [
        "!_S_PLANTING_iTreeSpecies_Code_Text",
        "!_S_PLANTING_iTreeSpecies_CommonName_Text",
        "!_S_PLANTING_iTreeSpecies_ScientificName_Text",
        "!_S_PLANTING_iTreeSpecies_Type_Text",
        "!_S_PLANTING_iTreeSpecies_ReplaceBy_Text"
    ];

    /// <returns>True if a new schedule was created; false if one already existed.</returns>
    public static bool EnsureSchedule(Document document)
    {
        var existing = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Any(schedule => string.Equals(schedule.Name, ScheduleName, StringComparison.Ordinal));
        if (existing)
        {
            return false;
        }

        var schedule = ViewSchedule.CreateSchedule(document, new ElementId(BuiltInCategory.OST_Planting));
        schedule.Name = ScheduleName;

        var definition = schedule.Definition;
        var schedulableFields = definition.GetSchedulableFields();

        TryAddField(document, definition, schedulableFields, "Family and Type");
        foreach (var fieldName in SpeciesFieldNames)
        {
            TryAddField(document, definition, schedulableFields, fieldName);
        }

        try
        {
            definition.IsItemized = false;
        }
        catch
        {
            // Best-effort: the schedule is still usable per-instance if this setting can't apply.
        }

        return true;
    }

    private static void TryAddField(
        Document document,
        ScheduleDefinition definition,
        IList<SchedulableField> schedulableFields,
        string name)
    {
        var match = schedulableFields.FirstOrDefault(candidate => TryGetName(candidate, document) == name);
        if (match is not null)
        {
            definition.AddField(match);
        }
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
