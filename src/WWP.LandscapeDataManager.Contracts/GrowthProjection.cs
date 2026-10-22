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
/// aren't copies made by the tool, each drawn at its own year (<see cref="GrowthYearNames.OwnYear"/>).
/// <see cref="UnlinkedGrowthTrees"/> is no longer counted (0): every tree that isn't a copy is an original.
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
/// <see cref="DesignYear"/>: the year new-construction trees are drawn at (see <see cref="GrowthYearNames.OwnYear"/>).
public sealed record GrowthProjectionRequest(
    IReadOnlyList<int> Years,
    string Scope,
    IReadOnlyList<string> OptionKeys,
    bool PreviewOnly,
    int DesignYear = GrowthYearNames.DefaultDesignYear);

/// <summary>
/// <see cref="Created"/> counts only copies Revit can make now: it adds new elements to the active
/// design option alone, so copies of trees in another option wait (<see cref="WaitingForOption"/>,
/// null from a connector that predates it) until that option is made active.
/// </summary>
public sealed record GrowthProjectionResult(
    string DocumentTitle,
    bool PreviewOnly,
    int OriginalsInScope,
    int MovedToBaseline,
    int Created,
    int Updated,
    int Deleted,
    IReadOnlyList<string> WorksetsCreated,
    IReadOnlyList<string> Notes,
    IReadOnlyList<GrowthOptionWaiting>? WaitingForOption = null,
    string? ActiveOption = null)
{
    /// <summary>
    /// Copies are waiting for another design option to be active. An older connector only says so in
    /// a note, after a run skipped them.
    /// </summary>
    public bool HasCopiesWaiting =>
        WaitingForOption is { Count: > 0 } ||
        Notes.Any(note => note.Contains("active design option", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Copies of trees in one design option that wait for it to be the active option.</summary>
public sealed record GrowthOptionWaiting(string OptionLabel, int Copies);

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
/// Copies for years not in this run are left alone — dropping a year never deletes its trees. An
/// original gets no copy for its own year (<c>ownYears</c>: a new tree drawn at the design year),
/// and a copy left over for that year is removed.
/// </summary>
public static class GrowthProjectionPlanner
{
    public static GrowthProjectionPlan Plan(
        IReadOnlyCollection<string> originalsInScope,
        IReadOnlySet<string> allOriginals,
        IReadOnlyList<GrowthCopyLink> existingCopies,
        IReadOnlyList<int> years,
        IReadOnlyDictionary<string, int>? ownYears = null)
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
            var ownYear = ownYears is not null && ownYears.TryGetValue(source, out var own) ? own : 0;
            if (copiesByKey.TryGetValue((source, ownYear), out var redundant))
            {
                toDelete.AddRange(redundant);
            }

            foreach (var year in projectedYears.Where(year => year != ownYear))
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
