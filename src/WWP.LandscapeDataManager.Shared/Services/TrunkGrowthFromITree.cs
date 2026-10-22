using System.Globalization;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// One planting type's trunk growth rate from i-Tree: the forecast DBH growth for its species at the
/// condition and crown exposure most of its planted trees have (<see cref="TrunkGrowthFromITree.DefaultCondition"/>
/// and <see cref="TrunkGrowthFromITree.DefaultCrownExposure"/> when it has none with both set).
/// </summary>
public sealed record TypeTrunkGrowth(
    long TypeId,
    string FamilyName,
    string TypeName,
    string SpeciesCode,
    string Condition,
    int CrownExposure,
    int TreeCount,
    bool UsesDefaultInputs,
    double? RateCmPerYear = null,
    string? Error = null)
{
    public string InputsText => $"{Condition}, exposure {CrownExposure}{(UsesDefaultInputs ? " (default)" : string.Empty)}";
}

/// <summary>
/// The planting family grows DBH as <c>5.4 cm + (Years − AgeAtPlanting) × TrunkDiameterbyYear</c>, so
/// it needs a type's trunk growth rate in cm/yr. i-Tree grows the trunk by the same amount every year
/// for one species, condition and crown exposure (checked against its forecasts: Acer campestre,
/// Good, exposure 3 grows 0.44 cm in year 1 and in year 25), so the slope of its DBH forecast is the
/// family's rate exactly. One forecast request per species, condition and exposure.
/// </summary>
public static class TrunkGrowthFromITree
{
    public const string ParameterName = "!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number";

    /// <summary>The WWP sheet column the rate is pushed to and imported from.</summary>
    public const string SheetColumn = "Trunk diameter annual growth rate (cm/yr)";

    /// <summary>The family's planting DBH (a 16/18 cm girth tree), the forecast's starting size.</summary>
    public const double PlantingDbhCm = 5.4;

    public const string DefaultCondition = "Good";
    public const int DefaultCrownExposure = 3;

    /// <summary>Linear growth needs only a few years; kept short so each request is quick.</summary>
    public const int ForecastYears = 10;

    /// <summary>The DBH growth per year over a forecast: first to last year with a DBH. Null if it has fewer than two.</summary>
    public static double? RateCmPerYear(IReadOnlyList<TreeBenefitYear> years)
    {
        var sized = years.Where(year => year.DiameterCm > 0).OrderBy(year => year.Year).ToList();
        if (sized.Count < 2 || sized[^1].Year == sized[0].Year)
        {
            return null;
        }

        var rate = (sized[^1].DiameterCm - sized[0].DiameterCm) / (sized[^1].Year - sized[0].Year);
        return Math.Round(Math.Max(0, rate), 3);
    }

    /// <summary>
    /// The types to rate: every planting type with a species code, with the condition and crown
    /// exposure most of its planted trees (not growth-year copies) share.
    /// </summary>
    public static IReadOnlyList<TypeTrunkGrowth> Plan(
        IEnumerable<ModelScanItem> modelTypes,
        IEnumerable<PlantingInstanceValidationItem> trees)
    {
        var treesByType = trees
            .Where(TreeTimelines.IsPlanted)
            .GroupBy(tree => (tree.FamilyName, tree.TypeName))
            .ToDictionary(group => group.Key, group => group.ToList());

        return modelTypes
            .Where(type => type.Category == "Planting" && !string.IsNullOrWhiteSpace(type.SpeciesCode))
            .DistinctBy(type => type.TypeId)
            .Select(type =>
            {
                var typeTrees = treesByType.GetValueOrDefault((type.FamilyName, type.TypeName)) ?? [];
                var usual = typeTrees
                    .Where(tree => !string.IsNullOrWhiteSpace(tree.Condition) && tree.CrownExposure is not null)
                    .GroupBy(tree => (Condition: Capitalize(tree.Condition!), CrownExposure: tree.CrownExposure!.Value))
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key.Condition, StringComparer.Ordinal)
                    .ThenBy(group => group.Key.CrownExposure)
                    .Select(group => group.Key)
                    .FirstOrDefault();
                var usesDefault = usual == default;
                return new TypeTrunkGrowth(
                    type.TypeId,
                    type.FamilyName,
                    type.TypeName,
                    type.SpeciesCode!.Trim().ToUpperInvariant(),
                    usesDefault ? DefaultCondition : usual.Condition,
                    usesDefault ? DefaultCrownExposure : usual.CrownExposure,
                    typeTrees.Count,
                    usesDefault);
            })
            .OrderBy(type => type.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(type => type.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Forecasts each distinct species, condition and exposure once at the project's location and
    /// fills in every type's rate, or why it has none.
    /// </summary>
    public static async Task<IReadOnlyList<TypeTrunkGrowth>> RunAsync(
        IReadOnlyList<TypeTrunkGrowth> plan,
        double latitude,
        double longitude,
        Func<ITreeRevitInput, int, CancellationToken, Task<IReadOnlyList<TreeBenefitYear>>> forecast,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = plan
            .Select(type => (type.SpeciesCode, type.Condition, type.CrownExposure))
            .Distinct()
            .ToList();
        var rates = new Dictionary<(string, string, int), (double? Rate, string? Error)>();
        foreach (var (species, condition, exposure) in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = new ITreeRevitInput(
                species, string.Empty, string.Empty, string.Empty, string.Empty, 0,
                condition, PlantingDbhCm / 2.54d, latitude, longitude, ForecastYears, exposure);
            try
            {
                var rate = RateCmPerYear(await forecast(input, ForecastYears, cancellationToken));
                rates[(species, condition, exposure)] = (rate, rate is null ? "i-Tree's forecast had no trunk sizes" : null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                rates[(species, condition, exposure)] = (null, exception.Message);
            }

            progress?.Report((rates.Count, inputs.Count));
        }

        return plan
            .Select(type => rates[(type.SpeciesCode, type.Condition, type.CrownExposure)] is var result
                ? type with { RateCmPerYear = result.Rate, Error = result.Error }
                : type)
            .ToList();
    }

    /// <summary>The type writes for every rated type, as the Importer would make them.</summary>
    public static IReadOnlyList<ParameterWriteItem> BuildTypeWrites(IEnumerable<TypeTrunkGrowth> rated) =>
        rated
            .Where(type => type.RateCmPerYear is not null)
            .Select(type => new ParameterWriteItem(
                type.TypeId,
                type.TypeName,
                "i-Tree forecast",
                ParameterName,
                "Type",
                "Number (no conversion)",
                Format(type.RateCmPerYear!.Value)))
            .ToList();

    /// <summary>
    /// The sheet rows to update: each matched row gets the rate of its type with the most trees (two
    /// types matching one row can differ when their trees' condition or exposure differ).
    /// </summary>
    public static IReadOnlyList<(string RecordId, double Rate, string TypeName)> SheetUpdates(
        IReadOnlyList<TypeTrunkGrowth> rated,
        IReadOnlyList<TypeMatch> matches)
    {
        var byTypeId = rated.Where(type => type.RateCmPerYear is not null).ToDictionary(type => type.TypeId);
        return matches
            .Where(match => match.Status == "Matched" && match.Record is not null && byTypeId.ContainsKey(match.RevitType.TypeId))
            .GroupBy(match => match.Record!.Id)
            .Select(group => group
                .Select(match => byTypeId[match.RevitType.TypeId])
                .OrderByDescending(type => type.TreeCount)
                .ThenBy(type => type.TypeName, StringComparer.OrdinalIgnoreCase)
                .Select(type => (group.Key, Math.Round(type.RateCmPerYear!.Value, 2), type.TypeName))
                .First())
            .ToList();
    }

    public static string Format(double rate) => rate.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Capitalize(string condition)
    {
        var trimmed = condition.Trim();
        return trimmed.Length == 0 ? trimmed : char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }
}
