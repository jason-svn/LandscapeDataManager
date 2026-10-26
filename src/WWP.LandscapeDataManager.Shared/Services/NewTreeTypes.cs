using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// A planting (tree) type placed in Revit that the project's planting source has no row for, to add
/// there as a row the team completes with what i-Tree doesn't give (native status, bloom months,
/// maintenance, growth rates, …) before the type sync brings it into Revit. <see cref="Conflict"/>
/// says why it can't be added as it is.
/// </summary>
public sealed record NewTreeType(string FamilyName, string TypeName, int TreeCount, string? SpeciesCode, string? Conflict);

/// <summary>Finds the Revit tree types the planting source is missing (<see cref="Find"/>) and builds the rows to add (<see cref="Fields"/>).</summary>
public static class NewTreeTypes
{
    /// <summary>What the WWP planting source's "Planting type" column holds for every tree.</summary>
    public const string TreePlantingType = "Tree";

    private const string PlantingTypeField = "Planting type";
    private const string SpeciesCodeField = "Species Code";

    /// <summary>The source columns that can hold a type's name, in the order the type sync's key fields try them ("Planting type" holds "Tree").</summary>
    private static readonly string[] NameFields = ["Types", "Species names", "species", "type"];

    /// <summary>The WWP sheet's fallback row for a tree it doesn't know: never a type to add.</summary>
    private const string GenericType = "Generic";

    /// <summary>
    /// The planting types of <paramref name="scan"/> (scan every design option) that no source record
    /// matches the way the type sync matches them (<see cref="StableTypeMatcher"/>: species code,
    /// alias, then type name). A type with an alias was paired on purpose and isn't offered. A type
    /// whose name another type shares (another family's, or one that differs only in spacing or
    /// case) would share its row, so it comes back as a conflict. New types that share a species code
    /// get none: one row per code would match them all, so their rows are matched by name instead.
    /// </summary>
    public static IReadOnlyList<NewTreeType> Find(
        IReadOnlyList<ModelScanItem> scan,
        IReadOnlyList<AirtableRecord> records,
        IReadOnlyList<string> typeKeyFields,
        IReadOnlyList<TypeAlias> aliases)
    {
        // The scan has a row per type and species code; a type is one row here, with all its trees.
        var types = scan
            .Where(item => !item.IsAreaBased)
            .GroupBy(item => (item.FamilyName, item.TypeName))
            .Select(group => group.First() with
            {
                ElementCount = group.Sum(item => item.ElementCount),
                SpeciesCode = group.Select(item => item.SpeciesCode).FirstOrDefault(code => !string.IsNullOrWhiteSpace(code))
            })
            .ToList();
        var aliased = aliases.Select(alias => (alias.FamilyName, alias.TypeName)).ToHashSet();
        var missing = StableTypeMatcher.Build(types, records, typeKeyFields, aliases)
            .Where(match => match.Status == "Missing" && !aliased.Contains((match.RevitType.FamilyName, match.RevitType.TypeName)))
            .Select(match => match.RevitType)
            .Where(type => StableTypeMatcher.Normalize(type.TypeName) is { Length: > 0 } name && name != StableTypeMatcher.Normalize(GenericType))
            .ToList();
        var sharedNames = types
            .GroupBy(type => StableTypeMatcher.Normalize(type.TypeName))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var sharedCodes = missing
            .Where(type => !string.IsNullOrWhiteSpace(type.SpeciesCode))
            .GroupBy(type => StableTypeMatcher.Normalize(type.SpeciesCode!))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        return missing
            .Select(type => new NewTreeType(
                type.FamilyName,
                type.TypeName,
                type.ElementCount,
                string.IsNullOrWhiteSpace(type.SpeciesCode) || sharedCodes.Contains(StableTypeMatcher.Normalize(type.SpeciesCode)) ? null : type.SpeciesCode.Trim(),
                sharedNames.Contains(StableTypeMatcher.Normalize(type.TypeName))
                    ? "Another type has this name (in another family, or spaced or cased differently), and one row would match both: rename one in Revit first."
                    : null))
            .OrderBy(type => type.TypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(type => type.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The row to add for <paramref name="type"/>: its exact Revit type name in the source's name
    /// column (what the type sync matches), "Tree" in "Planting type" when the source marks its trees
    /// that way, and its species code when the source has that column — nothing the sheet owns. A
    /// column is known from <paramref name="records"/>: Airtable leaves empty cells out of a record,
    /// so a table with no records yet gets only the name, in "Types".
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Fields(NewTreeType type, IReadOnlyList<AirtableRecord> records)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [NameFields.FirstOrDefault(field => records.Any(record => record.Fields.ContainsKey(field))) ?? NameFields[0]] = type.TypeName
        };
        if (records.Any(record => record.Fields.TryGetValue(PlantingTypeField, out var value) &&
                                  value.ValueKind == JsonValueKind.String && value.GetString() == TreePlantingType))
        {
            fields[PlantingTypeField] = TreePlantingType;
        }

        if (!string.IsNullOrWhiteSpace(type.SpeciesCode) && records.Any(record => record.Fields.ContainsKey(SpeciesCodeField)))
        {
            fields[SpeciesCodeField] = type.SpeciesCode.Trim();
        }

        return fields;
    }
}
