using System.Text;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Matches Revit types to source records by Family + Type instead of display name alone. The
/// source's type-name column value is still the primary signal (that's what the spreadsheet
/// actually has), but when two Revit types of different Families normalize to the same name,
/// this reports the collision as ambiguous rather than guessing — resolvable by adding a
/// <see cref="TypeAlias"/> that pins the source's value to one specific Family + Type.
/// </summary>
public static class StableTypeMatcher
{
    public static IReadOnlyList<TypeMatch> Build(
        IReadOnlyList<ModelScanItem> revitTypes,
        IReadOnlyList<AirtableRecord> sourceRecords,
        IReadOnlyList<string> typeKeyFields,
        IReadOnlyList<TypeAlias> aliases)
    {
        var sourceIndex = BuildSourceIndex(sourceRecords, typeKeyFields);
        var typeNameCollisions = revitTypes
            .GroupBy(type => Normalize(type.TypeName))
            .Where(group => group.Select(type => type.FamilyName).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var aliasByFamilyType = aliases.ToDictionary(
            alias => (alias.FamilyName, alias.TypeName),
            StringPairComparer.Instance);

        var results = new List<TypeMatch>(revitTypes.Count);
        foreach (var revitType in revitTypes)
        {
            results.Add(MatchOne(revitType, sourceIndex, typeNameCollisions, aliasByFamilyType));
        }

        return results;
    }

    private static TypeMatch MatchOne(
        ModelScanItem revitType,
        IReadOnlyDictionary<string, List<AirtableRecord>> sourceIndex,
        IReadOnlySet<string> typeNameCollisions,
        IReadOnlyDictionary<(string, string), TypeAlias> aliasByFamilyType)
    {
        if (aliasByFamilyType.TryGetValue((revitType.FamilyName, revitType.TypeName), out var alias))
        {
            var aliasKey = Normalize(alias.SourceTypeName);
            if (!sourceIndex.TryGetValue(aliasKey, out var aliasMatches) || aliasMatches.Count == 0)
            {
                return new TypeMatch(revitType, null, "Missing",
                    $"The alias source value '{alias.SourceTypeName}' was not found in the source data.");
            }

            return aliasMatches.Count == 1
                ? new TypeMatch(revitType, aliasMatches[0], "Matched", null)
                : new TypeMatch(revitType, null, "Duplicate",
                    $"More than one source record matches the aliased value '{alias.SourceTypeName}'.");
        }

        var normalizedName = Normalize(revitType.TypeName);
        if (!sourceIndex.TryGetValue(normalizedName, out var matches) || matches.Count == 0)
        {
            return new TypeMatch(revitType, null, "Missing", "No source record matches this Revit type.");
        }

        if (matches.Count > 1)
        {
            return new TypeMatch(revitType, null, "Duplicate", "More than one source record matches this Revit type.");
        }

        if (typeNameCollisions.Contains(normalizedName))
        {
            return new TypeMatch(revitType, null, "Ambiguous",
                $"Another Revit family also has a type named '{revitType.TypeName}'. " +
                "Add a type alias to specify which Family this source value means.");
        }

        return new TypeMatch(revitType, matches[0], "Matched", null);
    }

    private static Dictionary<string, List<AirtableRecord>> BuildSourceIndex(
        IEnumerable<AirtableRecord> records,
        IReadOnlyList<string> keyFields)
    {
        var index = new Dictionary<string, List<AirtableRecord>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var keys = keyFields
                .Where(record.Fields.ContainsKey)
                .SelectMany(field => ReadTextValues(record.Fields[field]))
                .Select(Normalize)
                .Where(key => key.Length > 0)
                .Distinct(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (!index.TryGetValue(key, out var matches))
                {
                    matches = [];
                    index[key] = matches;
                }

                if (!matches.Contains(record))
                {
                    matches.Add(record);
                }
            }
        }

        return index;
    }

    private static IEnumerable<string> ReadTextValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var text in ReadTextValues(item))
                {
                    yield return text;
                }
            }
        }
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private sealed class StringPairComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringPairComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.Ordinal) &&
            string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(obj.Item1, obj.Item2);
    }
}

/// <summary>Status is one of: "Matched", "Missing", "Duplicate", "Ambiguous".</summary>
public sealed record TypeMatch(ModelScanItem RevitType, AirtableRecord? Record, string Status, string? Message);
