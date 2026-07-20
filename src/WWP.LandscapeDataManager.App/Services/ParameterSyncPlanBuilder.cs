using System.Text;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.Services;

internal static class ParameterSyncPlanBuilder
{
    private static readonly string[] TreeKeyFields =
    [
        "Types",
        "Species names",
        "species",
        "type"
    ];

    private static readonly string[] SurfaceKeyFields =
    [
        "Planting type",
        "Types",
        "type"
    ];

    public static ParameterSyncPlan Build(
        IReadOnlyList<AirtableRecord> records,
        ModelScanResult scan,
        IReadOnlyList<ParameterMappingDefinition> mappings,
        ModelScanOptions options)
    {
        var enabledMappings = mappings.Where(mapping => mapping.Enabled).ToList();
        if (enabledMappings.Count == 0)
        {
            throw new InvalidOperationException(
                "Save at least one enabled mapping on the Parameter Mapper tab before building a write preview.");
        }

        var treeIndex = BuildIndex(records, TreeKeyFields);
        var surfaceIndex = BuildIndex(records, SurfaceKeyFields);
        var writes = new List<ParameterWriteItem>();
        var issues = new List<ParameterSyncIssue>();

        foreach (var item in scan.Items.DistinctBy(item => item.TypeId))
        {
            var index = item.IsAreaBased ? surfaceIndex : treeIndex;
            index.TryGetValue(Normalize(item.TypeName), out var matches);
            if (matches is null || matches.Count == 0)
            {
                issues.Add(new ParameterSyncIssue(
                    item.TypeName,
                    "—",
                    "No source record matches this Revit type."));
                continue;
            }

            if (matches.Count > 1)
            {
                issues.Add(new ParameterSyncIssue(
                    item.TypeName,
                    "—",
                    "More than one source record matches this Revit type."));
                continue;
            }

            var record = matches[0];
            foreach (var mapping in enabledMappings)
            {
                if (!record.Fields.TryGetValue(mapping.AirtableField, out var sourceValue))
                {
                    issues.Add(new ParameterSyncIssue(
                        item.TypeName,
                        mapping.RevitParameter,
                        $"Source column '{mapping.AirtableField}' is missing from the matched record."));
                    continue;
                }

                writes.Add(new ParameterWriteItem(
                    item.TypeId,
                    item.TypeName,
                    mapping.AirtableField,
                    mapping.RevitParameter,
                    mapping.Scope,
                    mapping.Conversion,
                    ReadSourceValue(sourceValue)));
            }
        }

        return new ParameterSyncPlan(new ParameterWriteBatch(options, writes), issues);
    }

    private static Dictionary<string, List<AirtableRecord>> BuildIndex(
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

    private static string ReadSourceValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Array => string.Join("; ", ReadTextValues(value)),
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText()
    };

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

internal sealed record ParameterSyncPlan(
    ParameterWriteBatch Batch,
    IReadOnlyList<ParameterSyncIssue> Issues);

internal sealed record ParameterSyncIssue(
    string TypeName,
    string Parameter,
    string Message);
