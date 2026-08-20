using System.Text;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Matches Planting instances to source records by value equality between a chosen Revit
/// parameter (e.g. "Mark") and a chosen source column, instead of requiring every instance to be
/// paired one at a time via <see cref="InstanceMatchPlanBuilder"/>'s stable-ID pairing. Meant as a
/// bulk first-pass: callers still prefer an existing stable pairing when one is already there, and
/// only fall back to this for instances with none yet.
/// </summary>
public static class InstanceKeyMatchPlanBuilder
{
    public static IReadOnlyList<InstanceMatch> Build(
        IReadOnlyList<PlantingInstanceScanItem> revitInstances,
        IReadOnlyList<AirtableRecord> sourceRecords,
        string sourceField)
    {
        var index = BuildSourceIndex(sourceRecords, sourceField);
        var results = new List<InstanceMatch>(revitInstances.Count);

        foreach (var instance in revitInstances)
        {
            if (string.IsNullOrWhiteSpace(instance.KeyParameterValue))
            {
                results.Add(new InstanceMatch(
                    instance, null, "NotPaired",
                    "The chosen matching parameter is empty on this instance."));
                continue;
            }

            var normalizedKey = Normalize(instance.KeyParameterValue);
            if (!index.TryGetValue(normalizedKey, out var matches) || matches.Count == 0)
            {
                results.Add(new InstanceMatch(
                    instance, null, "Missing",
                    $"No source record has '{sourceField}' = '{instance.KeyParameterValue}'."));
                continue;
            }

            if (matches.Count > 1)
            {
                results.Add(new InstanceMatch(
                    instance, null, "Duplicate",
                    $"More than one source record has '{sourceField}' = '{instance.KeyParameterValue}'."));
                continue;
            }

            results.Add(new InstanceMatch(instance, matches[0], "Matched", null));
        }

        return results;
    }

    private static Dictionary<string, List<AirtableRecord>> BuildSourceIndex(
        IEnumerable<AirtableRecord> records,
        string sourceField)
    {
        var index = new Dictionary<string, List<AirtableRecord>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!record.Fields.TryGetValue(sourceField, out var value))
            {
                continue;
            }

            foreach (var key in ReadTextValues(value).Select(Normalize).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal))
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
        else if (value.ValueKind is JsonValueKind.Number)
        {
            yield return value.GetRawText();
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
}
