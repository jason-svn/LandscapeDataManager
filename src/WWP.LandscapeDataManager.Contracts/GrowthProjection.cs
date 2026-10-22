namespace WWP.LandscapeDataManager.Contracts;

/// <summary>Which original trees a Growth Projections run copies into the growth years.</summary>
public static class GrowthProjectionScopes
{
    public const string AllTrees = "All";
    public const string Selection = "Selection";
    public const string DesignOptions = "Options";
}

/// <summary>Key of the main model in <see cref="GrowthDesignOptionChoice"/> lists; a design option's key is its element id.</summary>
public static class GrowthOptionKeys
{
    public const string MainModel = "main";
}

/// <summary>One place original trees can live: the main model or one design option.</summary>
public sealed record GrowthDesignOptionChoice(string Key, string Label, int OriginalTreeCount, bool IsActive);

/// <summary>A growth-year workset already in the model and how many trees sit on it.</summary>
public sealed record GrowthYearWorkset(int Year, string WorksetName, int TreeCount, int LinkedCopyCount);

/// <summary>
/// What Growth Projections needs to show before a run. Original trees are Planting instances that
/// aren't copies made by the tool; <see cref="UnlinkedGrowthTrees"/> are trees on a growth year above
/// 0 (by workset or by projected year) that the tool didn't make (e.g. placed by hand), which it never
/// copies or moves back to Baseline.
/// </summary>
public sealed record GrowthProjectionStatus(
    string DocumentTitle,
    bool IsWorkshared,
    bool YearsParameterBound, // !_S_PLT_TreeGrowth_ProjectedYears_Number is on the trees
    bool AgeAtPlantingBound,
    string ActiveOptionLabel,
    int OriginalTreeCount,
    int SelectedOriginalCount,
    int UnlinkedGrowthTrees,
    IReadOnlyList<GrowthDesignOptionChoice> Options,
    IReadOnlyList<GrowthYearWorkset> Worksets,
    int GroupedTrees = 0);

/// <summary>
/// <see cref="Years"/> are the growth years to project (above 0 — year 0 is always the originals).
/// <see cref="OptionKeys"/> is only read for <see cref="GrowthProjectionScopes.DesignOptions"/>.
/// </summary>
public sealed record GrowthProjectionRequest(
    IReadOnlyList<int> Years,
    string Scope,
    IReadOnlyList<string> OptionKeys,
    bool PreviewOnly);

public sealed record GrowthProjectionResult(
    string DocumentTitle,
    bool PreviewOnly,
    int OriginalsInScope,
    int MovedToBaseline,
    int Created,
    int Updated,
    int Deleted,
    IReadOnlyList<string> WorksetsCreated,
    IReadOnlyList<string> Notes);

/// <summary>A tool-made copy: the original it projects and the growth year it stands for.</summary>
public sealed record GrowthCopyLink(string CopyUniqueId, string SourceUniqueId, int Year);

public sealed record GrowthCopyToCreate(string SourceUniqueId, int Year);

/// <summary>The work one run does: copies to add, linked copies to refresh, and copies to remove.</summary>
public sealed record GrowthProjectionPlan(
    IReadOnlyList<GrowthCopyToCreate> ToCreate,
    IReadOnlyList<GrowthCopyLink> ToUpdate,
    IReadOnlyList<GrowthCopyLink> ToDelete);

/// <summary>
/// Pure planning for Growth Projections, kept out of the Revit connector so it can be tested. Each
/// original in scope gets exactly one linked copy per growth year: a missing one is created, an
/// existing one is refreshed (moved/retyped to match its original), and extra copies of the same
/// original and year are removed. Copies whose original no longer exists anywhere are removed too.
/// Copies for years not in this run are left alone — dropping a year never deletes its trees.
/// </summary>
public static class GrowthProjectionPlanner
{
    public static GrowthProjectionPlan Plan(
        IReadOnlyCollection<string> originalsInScope,
        IReadOnlySet<string> allOriginals,
        IReadOnlyList<GrowthCopyLink> existingCopies,
        IReadOnlyList<int> years)
    {
        var projectedYears = years.Where(year => year > 0).Distinct().Order().ToList();
        var copiesByKey = existingCopies
            .GroupBy(copy => (copy.SourceUniqueId, copy.Year))
            .ToDictionary(group => group.Key, group => group.OrderBy(copy => copy.CopyUniqueId, StringComparer.Ordinal).ToList());

        var toCreate = new List<GrowthCopyToCreate>();
        var toUpdate = new List<GrowthCopyLink>();
        var toDelete = new List<GrowthCopyLink>();

        foreach (var source in originalsInScope.Distinct(StringComparer.Ordinal))
        {
            foreach (var year in projectedYears)
            {
                if (copiesByKey.TryGetValue((source, year), out var copies))
                {
                    toUpdate.Add(copies[0]);
                    toDelete.AddRange(copies.Skip(1));
                }
                else
                {
                    toCreate.Add(new GrowthCopyToCreate(source, year));
                }
            }
        }

        toDelete.AddRange(existingCopies.Where(copy => !allOriginals.Contains(copy.SourceUniqueId)));
        return new GrowthProjectionPlan(toCreate, toUpdate, toDelete.DistinctBy(copy => copy.CopyUniqueId).ToList());
    }
}
