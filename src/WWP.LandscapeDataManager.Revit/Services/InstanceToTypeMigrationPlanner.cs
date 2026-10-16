namespace WWP.LandscapeDataManager.Revit.Services;

internal sealed record BindingMigrationValue(string? Text = null, double? Number = null)
{
    public bool IsEmpty => Text is null && Number is null;

    public static BindingMigrationValue FromText(string? value) =>
        new(Text: string.IsNullOrEmpty(value) ? null : value);
}

internal sealed record BindingMigrationSample(
    long ElementId, long TypeId, string TypeName, BindingMigrationValue Value);

internal sealed record BindingMigrationTypeValue(
    long TypeId, string TypeName, BindingMigrationValue Value);

internal sealed record BindingMigrationConflict(
    long TypeId, string TypeName, IReadOnlyList<long> ElementIds, string Reason);

internal sealed record InstanceToTypeMigrationPlan(
    int InstanceCount,
    IReadOnlyList<BindingMigrationTypeValue> TypeValues,
    IReadOnlyList<BindingMigrationConflict> Conflicts)
{
    public bool CanMigrate => Conflicts.Count == 0;
}

/// <summary>
/// Plans the three known LDS scope migrations without choosing between differing instance values.
/// Empty and populated values are distinct: migrating must not fill an explicitly empty instance
/// from another instance of its type. Text is compared ordinally and numbers without rounding.
/// </summary>
internal static class InstanceToTypeMigrationPlanner
{
    public static bool Supports(string parameterName) => parameterName is
        "!_S_PLT_LDS_MatchKey_Text" or
        "!_S_PLT_LDS_AirTempReduction_Number" or
        "!_S_PLT_LDS_SurfaceTempReduction_Number";

    public static InstanceToTypeMigrationPlan Build(
        IEnumerable<BindingMigrationSample> samples,
        IEnumerable<BindingMigrationTypeValue>? existingTypeValues = null)
    {
        var rows = samples.ToList();
        var values = new List<BindingMigrationTypeValue>();
        var conflicts = new List<BindingMigrationConflict>();
        foreach (var group in rows.GroupBy(row => row.TypeId))
        {
            var first = group.First();
            string? reason = null;
            if (group.Key <= 0)
            {
                reason = "the element has no valid Revit type";
            }
            else if (group.Any(row =>
                         row.Value.Text is not null && row.Value.Number is not null ||
                         row.Value.Number is { } number && !double.IsFinite(number)))
            {
                reason = "an instance value cannot be preserved as text or a finite number";
            }
            else if (group.Select(row => row.Value).Distinct().Take(2).Count() > 1)
            {
                reason = "instances have different values (empty and populated values also differ)";
            }

            if (reason is not null)
            {
                conflicts.Add(new BindingMigrationConflict(
                    group.Key, first.TypeName, group.Select(row => row.ElementId).ToArray(), reason));
                continue;
            }

            values.Add(new BindingMigrationTypeValue(group.Key, first.TypeName, first.Value));
        }

        // Some loaded families may already carry a type-level shared value with the same GUID.
        // Preserve these too, including values on types that have no placed instances.
        foreach (var existing in existingTypeValues ?? [])
        {
            if (existing.Value.IsEmpty)
            {
                continue;
            }

            var planned = values.FirstOrDefault(value => value.TypeId == existing.TypeId);
            if (existing.TypeId <= 0 ||
                existing.Value.Text is not null && existing.Value.Number is not null ||
                existing.Value.Number is { } number && !double.IsFinite(number) ||
                planned is not null && planned.Value != existing.Value)
            {
                conflicts.Add(new BindingMigrationConflict(
                    existing.TypeId, existing.TypeName,
                    rows.Where(row => row.TypeId == existing.TypeId).Select(row => row.ElementId).ToArray(),
                    "an existing type value differs or cannot be preserved"));
            }
            else if (planned is null)
            {
                values.Add(existing);
            }
        }

        // A binding changes scope for the whole parameter, so a single conflicting type must
        // prevent migration of every type, rather than producing a partially usable write plan.
        return new InstanceToTypeMigrationPlan(rows.Count, conflicts.Count == 0 ? values : [], conflicts);
    }
}
