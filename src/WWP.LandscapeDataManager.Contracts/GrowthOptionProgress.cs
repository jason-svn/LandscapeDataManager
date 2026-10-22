namespace WWP.LandscapeDataManager.Contracts;

/// <summary>One growth year in one design option: the copies made of its originals, and how many it needs.</summary>
public sealed record GrowthYearCopies(int Year, int Made, int Needed);

/// <summary>
/// One design option's growth projection: its originals, the copies made of them for each growth
/// year, the trees still waiting for i-Tree, and what's left to do. Revit adds new elements only to
/// the active design option, so an option's missing copies can only be made while it's active.
/// </summary>
public sealed record GrowthOptionProgressRow(
    string OptionKey,
    string OptionLabel,
    bool IsActive,
    int Originals,
    IReadOnlyList<GrowthYearCopies> Years,
    int ToCalculate)
{
    public const string Done = "Done";
    public const string Run = "Run Create & calculate";
    public const string MakeActive = "Make it the active option, then run";

    public int CopiesMade => Years.Sum(year => Math.Min(year.Made, year.Needed));

    public int CopiesNeeded => Years.Sum(year => year.Needed);

    public IReadOnlyList<int> MissingYears => Years.Where(year => year.Made < year.Needed).Select(year => year.Year).ToList();

    public string ToDo => MissingYears.Count > 0
        ? IsActive ? Run : MakeActive
        : ToCalculate > 0 ? Run : Done;
}

/// <summary>Growth Projections' progress per design option, from the trees as Sort &amp; check reads them.</summary>
public static class GrowthOptionProgress
{
    /// <summary>
    /// One row per design option (and the main model) with trees. An original needs a copy for every
    /// listed year but its own (existing trees year 0, new trees the design year); grouped trees get
    /// none. <paramref name="waitingForITree"/>: unique ids of trees that still need calculating.
    /// </summary>
    public static IReadOnlyList<GrowthOptionProgressRow> Build(
        IReadOnlyList<OrganizerTree> trees,
        IReadOnlyList<int> years,
        int designYear,
        string activeOptionKey,
        IReadOnlySet<string> waitingForITree)
    {
        var growthYears = years.Where(year => year > 0).Distinct().Order().ToList();
        return trees
            .GroupBy(tree => (tree.OptionKey, tree.OptionLabel))
            .Select(option =>
            {
                var originals = option.Where(tree => tree.LinkedYear is null).ToList();
                var ownYears = originals
                    .Where(tree => !tree.InGroup)
                    .Select(tree => GrowthYearNames.OwnYear(tree.ProjectedYears, tree.IsNewConstruction, designYear))
                    .ToList();
                var copyYears = option.Where(tree => tree.LinkedYear is not null).Select(tree => tree.LinkedYear!.Value).ToList();
                return new GrowthOptionProgressRow(
                    option.Key.OptionKey,
                    option.Key.OptionLabel,
                    option.Key.OptionKey == activeOptionKey,
                    originals.Count,
                    growthYears
                        .Select(year => new GrowthYearCopies(year, copyYears.Count(copy => copy == year), ownYears.Count(own => own != year)))
                        .ToList(),
                    option.Count(tree => waitingForITree.Contains(tree.UniqueId)));
            })
            .OrderBy(row => row.OptionLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
