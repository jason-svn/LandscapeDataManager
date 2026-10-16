using System.Text.RegularExpressions;

namespace WWP.LandscapeDataManager.Contracts;

/// <summary>
/// The naming rule for growth years: a workset (or, in older models, a design option) whose name
/// carries a year count — e.g. "Trees - 15 Years", "LIM_Growth_10yr", "LIM_Growth_0yr - Baseline" —
/// holds the planting as it stands that many years after planting (0 = as planted). Growth
/// Projections makes whole years from 1 to <see cref="MaximumYear"/>; names are read up to
/// <see cref="MaximumNamedYear"/>. Shared by the Revit connector (tree age sync, Growth
/// Projections) and the tools.
/// </summary>
public static partial class GrowthYearNames
{
    /// <summary>The latest growth year Growth Projections makes — trees are at full size by then or soon after.</summary>
    public const int MaximumYear = 40;

    /// <summary>
    /// The largest year count a name is read as — above <see cref="MaximumYear"/> so growth-year
    /// worksets made when the limit was 100 (e.g. "LIM_Growth_50yr") still count as growth years.
    /// Otherwise their trees would read as on no growth-year workset and be added to every year.
    /// </summary>
    public const int MaximumNamedYear = 100;

    [GeneratedRegex(@"(?<!\d)(\d{1,3})(?!\d)\s*(?:years?|yrs?)(?![A-Za-z0-9])|\b(?:year|yr)\s*(\d{1,3})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitYearPattern();

    [GeneratedRegex(@"(?:_|-)(\d{1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex GrowthSuffixPattern();

    /// <summary>The workset name Growth Projections gives a growth year.</summary>
    public static string WorksetName(int year) => year == 0 ? "LIM_Growth_0yr - Baseline" : $"LIM_Growth_{year}yr";

    /// <summary>The growth year named in <paramref name="name"/>, else null.</summary>
    public static int? FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var match = ExplicitYearPattern().Match(name);
        var value = match.Groups.Cast<Group>().Skip(1).FirstOrDefault(group => group.Success)?.Value;
        if (value is not null && int.TryParse(value, out var years))
        {
            return years <= MaximumNamedYear ? years : null;
        }

        // Bare numeric suffixes occur in the newer LIM_Growth_10 convention, but accepting them on
        // every workset would mistake ordinary phase and planting-zone numbers for growth years.
        if (!name.Contains("growth", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        match = GrowthSuffixPattern().Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, out years) && years <= MaximumNamedYear ? years : null;
    }

    /// <summary>
    /// A tree's actual age in a growth year: the years it had already grown when planted (nursery
    /// stock, <c>!_S_PLT_TreeGrowth_AgeAtPlanting_Number</c> on its type) plus the years since
    /// planting. Negative or missing ages at planting count as 0.
    /// </summary>
    public static int ActualAge(int? ageAtPlanting, int growthYear) => Math.Max(0, ageAtPlanting ?? 0) + growthYear;

    /// <summary>Growth years after which a tree with no known mature size stops growing.</summary>
    public const int GrowthLimitWithoutMatureSize = 25;

    /// <summary>
    /// The age a tree is modelled at in a growth year: its <see cref="ActualAge"/>, but no older than
    /// the age at which its type reaches mature size (<paramref name="matureAge"/>). A type with no
    /// mature size (no LDS max height/width, or one its family never reaches) grows for at most
    /// <see cref="GrowthLimitWithoutMatureSize"/> years after planting — either way a tree keeps its
    /// final size instead of growing forever.
    /// </summary>
    public static int ModelledAge(int? ageAtPlanting, int growthYear, int? matureAge) =>
        matureAge is { } mature
            ? Math.Min(ActualAge(ageAtPlanting, growthYear), mature)
            : ActualAge(ageAtPlanting, Math.Min(growthYear, GrowthLimitWithoutMatureSize));
}

/// <summary>What the automatic tree-age update did before a Tree Calculator scan.</summary>
public sealed record TreeAgeSyncSummary(
    int OnGrowthWorksets,
    int Updated,
    int SkippedNotEditable,
    int MissingYearsParameter,
    int SkippedGrouped,
    int CappedAtMaturity = 0);
