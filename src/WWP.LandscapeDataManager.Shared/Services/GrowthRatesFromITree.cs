using System.Globalization;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// One planting type's growth rates from i-Tree: its species' forecast at the condition and crown
/// exposure most of its planted trees have (<see cref="GrowthRatesFromITree.DefaultCondition"/> and
/// <see cref="GrowthRatesFromITree.DefaultCrownExposure"/> when it has none with both set).
/// </summary>
public sealed record TypeGrowthRates(
    long TypeId,
    string FamilyName,
    string TypeName,
    string SpeciesCode,
    string Condition,
    int CrownExposure,
    int TreeCount,
    bool UsesDefaultInputs,
    double? TrunkCmPerYear = null,
    double? HeightMPerYear = null,
    double? WidthMPerYear = null,
    string? Error = null,
    string? Note = null,
    bool FromRevitType = false)
{
    public string InputsText => $"{Condition}, exposure {CrownExposure}{(UsesDefaultInputs ? " (default)" : string.Empty)}";

    public bool HasRates => TrunkCmPerYear is not null || HeightMPerYear is not null || WidthMPerYear is not null;
}

/// <summary>
/// One type's rates as i-Tree gave them, saved on the project (<see cref="ProjectSettingsSnapshot.GrowthRates"/>)
/// so Growth Projections shows them without asking i-Tree again. They hold while the type's inputs
/// (species, condition, crown exposure, design year and the project's location) stay the same.
/// </summary>
public sealed record SavedGrowthRates(
    string FamilyName,
    string TypeName,
    string SpeciesCode,
    string Condition,
    int CrownExposure,
    int DesignYear,
    double Latitude,
    double Longitude,
    double? TrunkCmPerYear,
    double? HeightMPerYear,
    double? WidthMPerYear,
    string SavedUtc);

/// <summary>One rate where it's kept (a Revit type parameter or a sheet column): what's there, and whether i-Tree's differs.</summary>
public sealed record StoredRate(string Rate, string Current, double Proposed, bool Differs)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Current) ||
                           double.TryParse(Current, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value == 0;
}

/// <summary>
/// Whether a type's i-Tree rates are already where they're kept: <see cref="UpToDate"/> (writing it is
/// skipped), <see cref="Stale"/> (a different rate is there), <see cref="Missing"/> (a rate is empty),
/// or <see cref="NoRow"/> (the sheet has no row for the type). <see cref="Changes"/> lists what would change.
/// </summary>
public sealed record RateStatus(string Status, string? Changes)
{
    public const string UpToDate = "Up to date";
    public const string Stale = "Stale";
    public const string Missing = "Missing";
    public const string NoRow = "No row";

    public bool IsUpToDate => Status == UpToDate;

    public bool NeedsWriting => Status is Stale or Missing;

    public static RateStatus Of(IReadOnlyCollection<StoredRate> rates)
    {
        var differing = rates.Where(rate => rate.Differs).ToList();
        if (differing.Count == 0)
        {
            return new RateStatus(UpToDate, null);
        }

        var changes = string.Join(", ", differing.Select(rate =>
            $"{rate.Rate} {(rate.IsEmpty ? "empty" : rate.Current)} → {GrowthRatesFromITree.Format(rate.Proposed)}"));
        return new RateStatus(differing.Any(rate => rate.IsEmpty) ? Missing : Stale, changes);
    }
}

/// <summary>
/// The planting family grows a tree from its type's rates: DBH as <c>5.4 cm + (Years − AgeAtPlanting)
/// × trunk rate</c> (cm/yr), and height and crown width as <c>Years × rate</c> (m/yr) until i-Tree has
/// given the tree its own size. i-Tree grows a trunk by the same amount every year for one species,
/// condition and crown exposure (Acer campestre, Good, exposure 3 grows 0.44 cm in year 1 and in
/// year 25), so the slope of its DBH forecast is the trunk rate exactly. Height and crown start from
/// a planting size and slow down, so no rate from zero fits every year: the height and width rates
/// are i-Tree's size at the design year divided by the design year, which draws a tree at the design
/// year at i-Tree's size even before it's calculated. The height rate also sets the family's Mature
/// Age (max height ÷ rate). One forecast request per species, condition and exposure.
/// </summary>
public static class GrowthRatesFromITree
{
    public const string TrunkParameter = "!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number";
    public const string HeightParameter = "!_S_PLT_GrowthRatio_HeightbyYear_Number";
    public const string WidthParameter = "!_S_PLT_GrowthRatio_WidthbyYear_Number";

    /// <summary>The WWP sheet columns the rates are pushed to and imported from.</summary>
    public const string TrunkSheetColumn = "Trunk diameter annual growth rate (cm/yr)";
    public const string HeightSheetColumn = "Height year annual growth rate (m/yr)";
    public const string WidthSheetColumn = "Width annual growth rate (m/yr)";

    /// <summary>The family's planting DBH (a 16/18 cm girth tree), the forecast's starting size.</summary>
    public const double PlantingDbhCm = 5.4;

    public const string DefaultCondition = "Good";
    public const int DefaultCrownExposure = 3;

    /// <summary>The trunk rate needs only a few years; the forecast runs to the design year when that's later.</summary>
    public const int MinimumForecastYears = 10;

    /// <summary>The DBH growth per year over a forecast: first to last year with a DBH. Null if it has fewer than two.</summary>
    public static double? TrunkRate(IReadOnlyList<TreeBenefitYear> years)
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
    /// Height and crown-width rates (m/yr): i-Tree's size after <paramref name="designYear"/> years of
    /// growth (after the forecast's last year if it's shorter), divided by those years. Year 1 is
    /// planting, so the size after n years is Year n + 1. Null where the forecast has no size.
    /// </summary>
    public static (double? Height, double? Width) SizeRates(IReadOnlyList<TreeBenefitYear> years, int designYear)
    {
        var grown = years
            .Where(year => year.Year > 1 && year.Year <= designYear + 1)
            .OrderBy(year => year.Year)
            .LastOrDefault();
        if (grown is null)
        {
            return (null, null);
        }

        double? Rate(double sizeMetres) => sizeMetres > 0 ? Math.Round(sizeMetres / (grown.Year - 1), 2) : null;
        return (Rate(grown.HeightM), Rate(grown.CrownWidthM));
    }

    /// <summary>
    /// The types to rate: every planting type with a species code, with the condition and crown
    /// exposure most of its planted trees (not growth-year copies) share.
    /// </summary>
    public static IReadOnlyList<TypeGrowthRates> Plan(
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
                return new TypeGrowthRates(
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
    /// Forecasts each distinct species, condition and exposure once at the project's location, to the
    /// design year, and fills in every type's rates, or why it has none.
    /// </summary>
    public static async Task<IReadOnlyList<TypeGrowthRates>> RunAsync(
        IReadOnlyList<TypeGrowthRates> plan,
        double latitude,
        double longitude,
        int designYear,
        Func<ITreeRevitInput, int, CancellationToken, Task<IReadOnlyList<TreeBenefitYear>>> forecast,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var forecastYears = Math.Max(MinimumForecastYears, designYear + 1);
        var inputs = plan
            .Select(type => (type.SpeciesCode, type.Condition, type.CrownExposure))
            .Distinct()
            .ToList();
        var results = new Dictionary<(string, string, int), TypeGrowthRates>();
        foreach (var (species, condition, exposure) in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = new ITreeRevitInput(
                species, string.Empty, string.Empty, string.Empty, string.Empty, 0,
                condition, PlantingDbhCm / 2.54d, latitude, longitude, forecastYears, exposure);
            var rated = new TypeGrowthRates(0, string.Empty, string.Empty, species, condition, exposure, 0, false);
            try
            {
                var years = await forecast(input, forecastYears, cancellationToken);
                var (height, width) = SizeRates(years, designYear);
                rated = rated with { TrunkCmPerYear = TrunkRate(years), HeightMPerYear = height, WidthMPerYear = width };
                if (!rated.HasRates)
                {
                    rated = rated with { Error = "i-Tree's forecast had no tree sizes" };
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                rated = rated with { Error = exception.Message };
            }

            results[(species, condition, exposure)] = rated;
            progress?.Report((results.Count, inputs.Count));
        }

        return plan
            .Select(type => results[(type.SpeciesCode, type.Condition, type.CrownExposure)] is var result
                ? type with
                {
                    TrunkCmPerYear = result.TrunkCmPerYear,
                    HeightMPerYear = result.HeightMPerYear,
                    WidthMPerYear = result.WidthMPerYear,
                    Error = result.Error
                }
                : type)
            .ToList();
    }

    /// <summary>
    /// The project's saved rates with these freshly rated types in: each rated type's entry replaces
    /// its old one, other types' entries stay. Types i-Tree gave no rate are left as they were.
    /// </summary>
    public static IReadOnlyList<SavedGrowthRates> Save(
        IReadOnlyList<SavedGrowthRates>? saved,
        IEnumerable<TypeGrowthRates> rated,
        int designYear,
        double latitude,
        double longitude,
        DateTimeOffset now)
    {
        var fresh = rated
            .Where(type => type.HasRates)
            .Select(type => new SavedGrowthRates(
                type.FamilyName, type.TypeName, type.SpeciesCode, type.Condition, type.CrownExposure, designYear,
                Math.Round(latitude, 4), Math.Round(longitude, 4),
                type.TrunkCmPerYear, type.HeightMPerYear, type.WidthMPerYear,
                now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)))
            .ToList();
        var replaced = fresh.Select(entry => (entry.FamilyName, entry.TypeName)).ToHashSet();
        return (saved ?? [])
            .Where(entry => !replaced.Contains((entry.FamilyName, entry.TypeName)))
            .Concat(fresh)
            .OrderBy(entry => entry.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The planned types with the rates saved on the project, without asking i-Tree. A type whose
    /// inputs changed since (species, condition, exposure, design year, location), or that was never
    /// rated, gets no rates and a note saying so.
    /// </summary>
    public static IReadOnlyList<TypeGrowthRates> FromSaved(
        IReadOnlyList<TypeGrowthRates> plan,
        IReadOnlyList<SavedGrowthRates>? saved,
        int designYear,
        double latitude,
        double longitude)
    {
        var byType = (saved ?? []).ToDictionary(entry => (entry.FamilyName, entry.TypeName));
        return plan
            .Select(type =>
            {
                if (!byType.TryGetValue((type.FamilyName, type.TypeName), out var entry))
                {
                    return type with { Note = "Not asked i-Tree yet: Get rates" };
                }

                var same = string.Equals(entry.SpeciesCode, type.SpeciesCode, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(entry.Condition, type.Condition, StringComparison.OrdinalIgnoreCase) &&
                           entry.CrownExposure == type.CrownExposure &&
                           entry.DesignYear == designYear &&
                           Math.Abs(entry.Latitude - Math.Round(latitude, 4)) < 1e-6 &&
                           Math.Abs(entry.Longitude - Math.Round(longitude, 4)) < 1e-6;
                return same
                    ? type with { TrunkCmPerYear = entry.TrunkCmPerYear, HeightMPerYear = entry.HeightMPerYear, WidthMPerYear = entry.WidthMPerYear }
                    : type with { Note = "Inputs changed since i-Tree was asked: Get rates" };
            })
            .ToList();
    }

    /// <summary>
    /// A write of every growth rate parameter on each type, for a preview to read what the types
    /// have now (a preview's current value; it changes nothing).
    /// </summary>
    public static IReadOnlyList<ParameterWriteItem> TypeRateProbe(IEnumerable<TypeGrowthRates> types) =>
        types
            .SelectMany(type => new[] { TrunkParameter, HeightParameter, WidthParameter }.Select(parameter => new ParameterWriteItem(
                type.TypeId, type.TypeName, "Revit type", parameter, "Type", "Number (no conversion)", "0")))
            .ToList();

    /// <summary>
    /// Types the project has no i-Tree rates saved for take the rates their Revit type has now (from a
    /// preview of <see cref="TypeRateProbe"/>), marked as such: Get rates checks them against i-Tree.
    /// </summary>
    public static IReadOnlyList<TypeGrowthRates> FillFromTypes(
        IReadOnlyList<TypeGrowthRates> rated,
        IReadOnlyList<ParameterWriteItem> probe,
        IReadOnlyList<ParameterWritePreviewRow> preview)
    {
        var current = preview
            .Where(row => row.ItemIndex >= 0 && row.ItemIndex < probe.Count)
            .GroupBy(row => probe[row.ItemIndex].TypeId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(row => probe[row.ItemIndex].RevitParameter, row => ParseRate(row.CurrentValue)));
        return rated
            .Select(type =>
            {
                if (type.HasRates || type.Error is not null || !current.TryGetValue(type.TypeId, out var values))
                {
                    return type;
                }

                double? Value(string parameter) => values.GetValueOrDefault(parameter);
                var fromType = type with
                {
                    TrunkCmPerYear = Value(TrunkParameter),
                    HeightMPerYear = Value(HeightParameter),
                    WidthMPerYear = Value(WidthParameter)
                };
                return fromType.HasRates
                    ? fromType with { FromRevitType = true, Note = "From the Revit type: Get rates checks it against i-Tree" }
                    : type;
            })
            .ToList();
    }

    /// <summary>A rate as a preview shows it ("0.339"); null when it's empty or 0.</summary>
    private static double? ParseRate(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    /// <summary>The type writes for every rate i-Tree gave, as the Importer would make them (rates read from the types are already there).</summary>
    public static IReadOnlyList<ParameterWriteItem> BuildTypeWrites(IEnumerable<TypeGrowthRates> rated) =>
        rated
            .Where(type => !type.FromRevitType)
            .SelectMany(type => Rates(type).Select(rate => new ParameterWriteItem(
                type.TypeId,
                type.TypeName,
                "i-Tree forecast",
                rate.Parameter,
                "Type",
                "Number (no conversion)",
                Format(rate.Value))))
            .ToList();

    /// <summary>
    /// The sheet rows to update, with their rates by column: each matched row gets the rates of its
    /// type with the most trees (two types matching one row can differ when their trees' condition or
    /// exposure differ). A row that already has them is left out.
    /// </summary>
    public static IReadOnlyList<(string RecordId, IReadOnlyDictionary<string, object?> Fields, string TypeName)> SheetUpdates(
        IReadOnlyList<TypeGrowthRates> rated,
        IReadOnlyList<TypeMatch> matches)
    {
        var byTypeId = rated.Where(type => type.HasRates).ToDictionary(type => type.TypeId);
        return matches
            .Where(match => match.Status == "Matched" && match.Record is not null && byTypeId.ContainsKey(match.RevitType.TypeId))
            .GroupBy(match => match.Record!.Id)
            .Select(group => (
                Record: group.First().Record!,
                Type: group
                    .Select(match => byTypeId[match.RevitType.TypeId])
                    .OrderByDescending(type => type.TreeCount)
                    .ThenBy(type => type.TypeName, StringComparer.OrdinalIgnoreCase)
                    .First()))
            .Where(row => !RateStatus.Of(SheetRates(row.Type, row.Record)).IsUpToDate)
            .Select(row => (
                row.Record.Id,
                (IReadOnlyDictionary<string, object?>)Rates(row.Type).ToDictionary(rate => rate.SheetColumn, rate => (object?)Math.Round(rate.Value, 2)),
                row.Type.TypeName))
            .ToList();
    }

    /// <summary>The type's rates against its sheet row's columns, compared as pushed (to 2 decimals).</summary>
    public static IReadOnlyList<StoredRate> SheetRates(TypeGrowthRates type, AirtableRecord record) =>
        Rates(type)
            .Select(rate =>
            {
                var current = record.Fields.TryGetValue(rate.SheetColumn, out var value) && value.ValueKind == JsonValueKind.Number
                    ? value.GetDouble()
                    : (double?)null;
                var proposed = Math.Round(rate.Value, 2);
                return new StoredRate(
                    RateName(rate.Parameter),
                    current is { } number ? Format(number) : string.Empty,
                    proposed,
                    current is not { } stored || Math.Abs(stored - proposed) > 0.0001);
            })
            .ToList();

    /// <summary>Where the type's rates stand on its sheet row; <see cref="RateStatus.NoRow"/> without a matched row.</summary>
    public static RateStatus SheetStatus(TypeGrowthRates type, TypeMatch? match) =>
        match is { Status: "Matched", Record: { } record }
            ? RateStatus.Of(SheetRates(type, record))
            : new RateStatus(RateStatus.NoRow, null);

    /// <summary>
    /// Where each type's rates stand on its Revit type, from a preview of <see cref="BuildTypeWrites"/>:
    /// a write the preview finds unchanged is already there.
    /// </summary>
    public static IReadOnlyDictionary<long, RateStatus> TypeStatuses(
        IReadOnlyList<ParameterWriteItem> writes,
        IReadOnlyList<ParameterWritePreviewRow> preview) =>
        preview
            .Where(row => row.ItemIndex >= 0 && row.ItemIndex < writes.Count)
            .GroupBy(row => writes[row.ItemIndex].TypeId)
            .ToDictionary(
                group => group.Key,
                group => RateStatus.Of(group
                    .Select(row => new StoredRate(
                        RateName(writes[row.ItemIndex].RevitParameter),
                        row.CurrentValue,
                        double.Parse(writes[row.ItemIndex].SourceValue, CultureInfo.InvariantCulture),
                        row.Status != "No change"))
                    .ToList()));

    /// <summary>"trunk", "height" or "width" for a growth rate parameter.</summary>
    public static string RateName(string parameter) => parameter switch
    {
        TrunkParameter => "trunk",
        HeightParameter => "height",
        WidthParameter => "width",
        _ => parameter
    };

    public static string Format(double rate) => rate.ToString("0.###", CultureInfo.InvariantCulture);

    private static IEnumerable<(string Parameter, string SheetColumn, double Value)> Rates(TypeGrowthRates type)
    {
        if (type.TrunkCmPerYear is { } trunk)
        {
            yield return (TrunkParameter, TrunkSheetColumn, trunk);
        }

        if (type.HeightMPerYear is { } height)
        {
            yield return (HeightParameter, HeightSheetColumn, height);
        }

        if (type.WidthMPerYear is { } width)
        {
            yield return (WidthParameter, WidthSheetColumn, width);
        }
    }

    private static string Capitalize(string condition)
    {
        var trimmed = condition.Trim();
        return trimmed.Length == 0 ? trimmed : char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }
}
