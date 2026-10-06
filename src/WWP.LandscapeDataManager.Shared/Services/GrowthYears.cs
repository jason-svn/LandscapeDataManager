using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Growth years (the same planting modelled at 5/10/15/20/25 years) are put on worksets named with
/// the year, e.g. "Trees - 15 Years" or "LIM_Growth_10yr". Only trees are split this way: a tree on
/// no growth-year workset (existing trees, a non-workshared model) counts in every year, and so do
/// floors and lighting. Older models carried the year in a design option name instead, which the
/// same name rule still reads.
/// </summary>
public static class GrowthYears
{
    public static readonly IReadOnlyList<int> StandardYears = [5, 10, 15, 20, 25];

    /// <summary>The growth year named in <paramref name="name"/> (a workset or design option name), else null.</summary>
    public static int? FromName(string? name) => GrowthYearNames.FromName(name);

    /// <summary>The tree's growth year from its workset, or null when it isn't on a growth-year workset.</summary>
    public static int? Of(DashboardTreeItem tree) => FromName(tree.Workset);

    /// <summary>The growth years the model's tree worksets define, ascending; empty when growth years aren't modelled as worksets.</summary>
    public static IReadOnlyList<int> InModel(IEnumerable<DashboardTreeItem> trees) =>
        trees.Select(Of).OfType<int>().Distinct().Order().ToList();

    /// <summary>A tree counts in <paramref name="year"/> when it's on that year's workset or on no growth-year workset at all.</summary>
    public static bool CountsIn(DashboardTreeItem tree, int year) => Of(tree) is not { } treeYear || treeYear == year;
}
