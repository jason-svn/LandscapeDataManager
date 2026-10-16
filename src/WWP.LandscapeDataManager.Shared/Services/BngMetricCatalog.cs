using System.Globalization;
using System.Text.Json;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// A cell from the Statutory Biodiversity Metric's lookup tables — either a number (e.g. a
/// condition score of 2.5) or text (e.g. "30+" or "Not Possible ▲"). Kept distinct because the
/// metric's own formulas branch on the difference (see <see cref="BngHabitatCreationCalculator"/>).
/// </summary>
public readonly record struct BngCellValue(double? Number, string? Text)
{
    public static readonly BngCellValue Empty = new(null, null);

    public bool IsEmpty => Number is null && string.IsNullOrEmpty(Text);

    public static BngCellValue Of(double number) => new(number, null);

    public static BngCellValue Of(string text) => new(null, text);

    internal static BngCellValue FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => Of(element.GetDouble()),
        JsonValueKind.String => Of(element.GetString() ?? string.Empty),
        _ => Empty
    };

    /// <summary>Formats the way Excel's "General" number format displays the value.</summary>
    public override string ToString() => Number is { } number
        ? number.ToString("0.##########", CultureInfo.InvariantCulture)
        : Text ?? string.Empty;
}

public sealed record BngStrategicSignificance(string Description, string Category, double Multiplier);

public sealed record BngBroadHabitat(string Name, IReadOnlyList<string> ProposedHabitats);

/// <summary>One row of the metric's G-1 All Habitats sheet, joined with its G-3, G-4 and G-8 rows.</summary>
public sealed record BngHabitat(
    string Name,
    string Description,
    string? BroadHabitat,
    string Distinctiveness,
    double DistinctivenessScore,
    string? TradingRule,
    string? ConditionGroup,
    string CreationDifficulty,
    string EnhancementDifficulty,
    IReadOnlyDictionary<string, BngCellValue> ConditionScores,
    IReadOnlyDictionary<string, BngCellValue> TimeToTargetYears,
    string? BaselineBroadHabitat = null,
    string? Irreplaceable = null,
    IReadOnlyDictionary<string, BngCellValue>? EnhancementTimeToTargetYears = null)
{
    /// <summary>False for habitats the metric doesn't offer as post-intervention habitats (e.g. "Felled").</summary>
    public bool CanBeCreated => BroadHabitat is not null;

    /// <summary>False for habitats the A-1 baseline dropdown doesn't list (e.g. "Replacement for felled woodland").</summary>
    public bool CanBeBaseline => BaselineBroadHabitat is not null;
}

/// <summary>
/// The lookup tables of one release of the Statutory Biodiversity Metric, extracted from the
/// official .xlsm by <c>tools/BngMetricExtractor/Extract-BngMetric.ps1</c> and embedded in this
/// assembly. A new Defra release is a re-extraction (a new JSON resource), not a code change.
/// </summary>
public sealed class BngMetricCatalog
{
    private const string DefaultResourceSuffix = "BngMetric_2024-07-23.json";

    private static readonly Lazy<BngMetricCatalog> DefaultCatalog = new(() => LoadEmbedded(DefaultResourceSuffix));

    private readonly Dictionary<string, BngHabitat> _habitatsByKey;
    private readonly Dictionary<string, IReadOnlyList<string>> _conditionGroups;
    private readonly Dictionary<string, double> _temporalMultipliersByYears;
    private readonly IReadOnlySet<string> _enhancementHabitatNames;

    private BngMetricCatalog(
        string metricName,
        string metricVersion,
        IReadOnlyList<string> conditions,
        IReadOnlyDictionary<string, double> distinctivenessScores,
        IReadOnlyList<BngStrategicSignificance> strategicSignificance,
        IReadOnlyDictionary<string, double> difficultyMultipliers,
        Dictionary<string, double> temporalMultipliersByYears,
        Dictionary<string, IReadOnlyList<string>> conditionGroups,
        IReadOnlyList<BngBroadHabitat> broadHabitats,
        IReadOnlyList<BngHabitat> habitats,
        IReadOnlySet<string> enhancementHabitatNames)
    {
        _enhancementHabitatNames = enhancementHabitatNames;
        MetricName = metricName;
        MetricVersion = metricVersion;
        Conditions = conditions;
        DistinctivenessScores = distinctivenessScores;
        StrategicSignificance = strategicSignificance;
        DifficultyMultipliers = difficultyMultipliers;
        _temporalMultipliersByYears = temporalMultipliersByYears;
        _conditionGroups = conditionGroups;
        BroadHabitats = broadHabitats;
        Habitats = habitats;

        // Both the short name ("Hazel scrub") and the full description ("Heathland and shrub -
        // Hazel scrub") resolve, so a value typed straight into Revit's Properties works either way.
        _habitatsByKey = new Dictionary<string, BngHabitat>(StringComparer.OrdinalIgnoreCase);
        foreach (var habitat in habitats)
        {
            _habitatsByKey.TryAdd(habitat.Description, habitat);
        }

        foreach (var habitat in habitats)
        {
            _habitatsByKey.TryAdd(habitat.Name, habitat);
        }
    }

    public static BngMetricCatalog Default => DefaultCatalog.Value;

    public string MetricName { get; }

    public string MetricVersion { get; }

    /// <summary>"The Statutory Biodiversity Metric 2024-07-23" — written to <c>BNGResult_MetricVersion_Text</c>.</summary>
    public string DisplayVersion => $"{MetricName} {MetricVersion}";

    /// <summary>All seven condition headers, in the metric's column order.</summary>
    public IReadOnlyList<string> Conditions { get; }

    public IReadOnlyDictionary<string, double> DistinctivenessScores { get; }

    public IReadOnlyList<BngStrategicSignificance> StrategicSignificance { get; }

    public IReadOnlyDictionary<string, double> DifficultyMultipliers { get; }

    public IReadOnlyList<BngBroadHabitat> BroadHabitats { get; }

    public IReadOnlyList<BngHabitat> Habitats { get; }

    /// <summary>Habitats offered in the A-2 "Proposed habitat" dropdown (every habitat with a broad habitat list).</summary>
    public IEnumerable<BngHabitat> CreatableHabitats => Habitats.Where(habitat => habitat.CanBeCreated);

    /// <summary>Habitats offered in the A-1 "Habitat Type" dropdown (every habitat with a baseline broad habitat list).</summary>
    public IEnumerable<BngHabitat> BaselineHabitats => Habitats.Where(habitat => habitat.CanBeBaseline);

    /// <summary>Habitats offered in the A-3 "Proposed habitat" dropdown under any broad habitat.</summary>
    public IEnumerable<BngHabitat> EnhancementHabitats => Habitats.Where(IsEnhancementHabitat);

    public bool IsEnhancementHabitat(BngHabitat habitat) => _enhancementHabitatNames.Contains(habitat.Name);

    public BngHabitat? FindHabitat(string? nameOrDescription) =>
        string.IsNullOrWhiteSpace(nameOrDescription)
            ? null
            : _habitatsByKey.GetValueOrDefault(nameOrDescription.Trim());

    public BngStrategicSignificance? FindStrategicSignificance(string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? null
            : StrategicSignificance.FirstOrDefault(option =>
                string.Equals(option.Description, description.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The conditions the metric's A-2 "Condition" dropdown offers for this habitat (its condition group).</summary>
    public IReadOnlyList<string> GetConditionOptions(BngHabitat habitat) =>
        habitat.ConditionGroup is not null && _conditionGroups.TryGetValue(habitat.ConditionGroup, out var options)
            ? options
            : [];

    /// <summary>The G-4 time-to-target multiplier for a number of years ("0".."31") or "30+"; null if the table has no such row.</summary>
    public double? GetTemporalMultiplier(string years) =>
        _temporalMultipliersByYears.TryGetValue(years, out var multiplier) ? multiplier : null;

    internal static BngMetricCatalog LoadEmbedded(string resourceSuffix)
    {
        var assembly = typeof(BngMetricCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded BNG metric resource '{resourceSuffix}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return Parse(document.RootElement);
    }

    internal static BngMetricCatalog Parse(JsonElement root)
    {
        var conditions = root.GetProperty("conditions").EnumerateArray().Select(item => item.GetString()!).ToList();

        var distinctivenessScores = root.GetProperty("distinctivenessScores").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetDouble(), StringComparer.OrdinalIgnoreCase);

        var strategicSignificance = root.GetProperty("strategicSignificance").EnumerateArray()
            .Select(item => new BngStrategicSignificance(
                item.GetProperty("description").GetString()!,
                item.GetProperty("category").GetString()!,
                item.GetProperty("multiplier").GetDouble()))
            .ToList();

        var difficultyMultipliers = root.GetProperty("difficultyMultipliers").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetDouble(), StringComparer.OrdinalIgnoreCase);

        var temporalMultipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in root.GetProperty("temporalMultipliers").EnumerateArray())
        {
            if (item.GetProperty("multiplier").ValueKind == JsonValueKind.Number)
            {
                temporalMultipliers[item.GetProperty("years").GetString()!] = item.GetProperty("multiplier").GetDouble();
            }
        }

        var conditionGroups = root.GetProperty("conditionGroups").EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => ReadStringList(property.Value),
                StringComparer.OrdinalIgnoreCase);

        var broadHabitats = root.GetProperty("broadHabitats").EnumerateArray()
            .Select(item => new BngBroadHabitat(
                item.GetProperty("name").GetString()!,
                ReadStringList(item.GetProperty("proposedHabitats"))))
            .ToList();

        var habitats = root.GetProperty("habitats").EnumerateArray()
            .Select(item => new BngHabitat(
                item.GetProperty("name").GetString()!,
                item.GetProperty("description").GetString()!,
                ReadOptionalString(item, "broadHabitat"),
                item.GetProperty("distinctiveness").GetString()!,
                item.GetProperty("distinctivenessScore").GetDouble(),
                ReadOptionalString(item, "tradingRule"),
                ReadOptionalString(item, "conditionGroup"),
                item.GetProperty("creationDifficulty").GetString()!,
                item.GetProperty("enhancementDifficulty").GetString()!,
                ReadCellMap(item.GetProperty("conditionScores")),
                ReadCellMap(item.GetProperty("timeToTargetYears")),
                ReadOptionalString(item, "baselineBroadHabitat"),
                ReadOptionalString(item, "irreplaceable"),
                item.TryGetProperty("enhancementTimeToTargetYears", out var enhancement) && enhancement.ValueKind == JsonValueKind.Object
                    ? ReadCellMap(enhancement)
                    : null))
            .ToList();

        var enhancementHabitatNames = root.TryGetProperty("enhancementBroadHabitats", out var enhancementBroad)
            ? enhancementBroad.EnumerateArray()
                .SelectMany(item => ReadStringList(item.GetProperty("habitats")))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new BngMetricCatalog(
            root.GetProperty("metricName").GetString()!,
            root.GetProperty("metricVersion").GetString()!,
            conditions,
            distinctivenessScores,
            strategicSignificance,
            difficultyMultipliers,
            temporalMultipliers,
            conditionGroups,
            broadHabitats,
            habitats,
            enhancementHabitatNames);
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().Select(item => item.GetString()!).ToList(),
        // PowerShell's ConvertTo-Json collapses a one-element array to a bare string.
        JsonValueKind.String => [element.GetString()!],
        _ => []
    };

    private static string? ReadOptionalString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyDictionary<string, BngCellValue> ReadCellMap(JsonElement element) =>
        element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => BngCellValue.FromJson(property.Value),
            StringComparer.OrdinalIgnoreCase);
}
