using System.Text;
using System.Text.Json;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

public sealed class NameIndex
{
    private static readonly string[] CandidateFieldNames =
    [
        "Types",
        "Planting type",
        "Species names",
        "species",
        "type"
    ];

    private readonly HashSet<string> _names;

    private NameIndex(HashSet<string> names)
    {
        _names = names;
    }

    public static NameIndex Create(IEnumerable<AirtableRecord> records)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            foreach (var field in record.Fields)
            {
                if (!CandidateFieldNames.Contains(field.Key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddValues(names, field.Value);
            }
        }

        return new NameIndex(names);
    }

    public bool Contains(string value) => _names.Contains(Normalize(value));

    private static void AddValues(ISet<string> names, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                names.Add(Normalize(text));
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                AddValues(names, item);
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
}
