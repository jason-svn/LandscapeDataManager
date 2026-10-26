using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// The sheet row Floor Calculator matched each floor type to, as type aliases the type sync
/// understands (<see cref="StableTypeMatcher"/>), so a floor type named differently from the sheet
/// (e.g. "WWP_Surfaces_Building", calculated as Asphalt) still gets that row's data — maintenance
/// cost, rates — in Planting Data Sync, the same row its benefits come from. Floor Calculator saves
/// the row's key on the type (<c>!_S_PLT_LDS_MatchKey_Text</c>: category|subcategory|type, see
/// <see cref="WwpLdsSheet.ToCoefficientRecord"/>).
/// </summary>
public static class FloorMatchAliases
{
    /// <summary>
    /// One alias per floor type whose saved match key is exactly one row of <paramref name="records"/>,
    /// naming that row by a value the type sync indexes it under and no other row shares (its
    /// "Planting type" code, or its "Types"). A type whose row isn't in the source gets none.
    /// </summary>
    public static IReadOnlyList<TypeAlias> Build(
        IReadOnlyList<SelectedFloorItem> floors,
        IReadOnlyList<AirtableRecord> records,
        IReadOnlyList<string> typeKeyFields)
    {
        var rowsByKey = records
            .GroupBy(record => WwpLdsSheet.ToCoefficientRecord(record).MatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var aliases = new List<TypeAlias>();
        foreach (var type in floors.Where(floor => !string.IsNullOrWhiteSpace(floor.CurrentLdsMatchKey)).GroupBy(floor => (floor.FamilyName, floor.TypeName)))
        {
            // Floor Calculator's pick applies to every floor of a type; the commonest key if they differ.
            var key = type.GroupBy(floor => floor.CurrentLdsMatchKey!.Trim(), StringComparer.OrdinalIgnoreCase).MaxBy(group => group.Count())!.Key;
            if (rowsByKey.TryGetValue(key, out var rows) && rows.Count == 1 && UniqueName(rows[0], records, typeKeyFields) is { } name)
            {
                aliases.Add(new TypeAlias(name, type.Key.FamilyName, type.Key.TypeName));
            }
        }

        return aliases;
    }

    /// <summary>The aliases saved on the project, then those from <paramref name="fromFloors"/> for types they don't already pin.</summary>
    public static IReadOnlyList<TypeAlias> Merge(IReadOnlyList<TypeAlias> saved, IReadOnlyList<TypeAlias> fromFloors) =>
        [.. saved, .. fromFloors.Where(alias => !saved.Any(pinned => pinned.FamilyName == alias.FamilyName && pinned.TypeName == alias.TypeName))];

    /// <summary>The row's value in the first key field that names it alone; null when each is shared with another row.</summary>
    private static string? UniqueName(AirtableRecord row, IReadOnlyList<AirtableRecord> records, IReadOnlyList<string> keyFields)
    {
        foreach (var field in keyFields)
        {
            if (Text(row, field) is not { } value)
            {
                continue;
            }

            var normalized = StableTypeMatcher.Normalize(value);
            if (!records.Any(other => !ReferenceEquals(other, row) &&
                                      keyFields.Any(otherField => Text(other, otherField) is { } text && StableTypeMatcher.Normalize(text) == normalized)))
            {
                return value;
            }
        }

        return null;
    }

    private static string? Text(AirtableRecord record, string field) =>
        record.Fields.TryGetValue(field, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}
