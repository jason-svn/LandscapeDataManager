using System.Text.RegularExpressions;

namespace WWP.LandscapeDataManager.Contracts;

/// <summary>
/// The naming rule for growth years: a workset (or, in older models, a design option) whose name
/// contains 5, 10, 15, 20 or 25 — e.g. "Trees - 15 Years", "LIM_Growth_10yr" — holds the planting
/// as it stands that many years after planting. Shared by the Revit connector (tree age sync) and
/// the tools (Dashboard slider, exports).
/// </summary>
public static partial class GrowthYearNames
{
    [GeneratedRegex(@"(?<!\d)(5|10|15|20|25)(?!\d)\s*(?:years?|yrs?)(?![A-Za-z0-9])|\b(?:year|yr)\s*(5|10|15|20|25)(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitYearPattern();

    [GeneratedRegex(@"(?:_|-)(5|10|15|20|25)$", RegexOptions.IgnoreCase)]
    private static partial Regex GrowthSuffixPattern();

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
            return years;
        }

        // Bare numeric suffixes occur in the newer LIM_Growth_10 convention, but accepting them on
        // every workset would mistake ordinary phase and planting-zone numbers for growth years.
        if (!name.Contains("growth", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        match = GrowthSuffixPattern().Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, out years) ? years : null;
    }

    /// <summary>
    /// A tree's actual age in a growth year: the years it had already grown when planted (nursery
    /// stock, <c>!_S_PLT_TreeGrowth_AgeAtPlanting_Number</c> on its type) plus the years since
    /// planting. Negative or missing ages at planting count as 0.
    /// </summary>
    public static int ActualAge(int? ageAtPlanting, int growthYear) => Math.Max(0, ageAtPlanting ?? 0) + growthYear;
}

/// <summary>What the automatic tree-age update did before a Tree Calculator scan.</summary>
public sealed record TreeAgeSyncSummary(
    int OnGrowthWorksets,
    int Updated,
    int SkippedNotEditable,
    int MissingYearsParameter,
    int SkippedGrouped);
