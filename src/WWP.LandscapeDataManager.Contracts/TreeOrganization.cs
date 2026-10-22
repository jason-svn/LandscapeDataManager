namespace WWP.LandscapeDataManager.Contracts;

/// <summary>
/// One Planting instance as Growth Projections' "Sort &amp; check trees" sees it. Positions are in
/// metres. <see cref="LinkedYear"/> is set on copies Growth Projections made (their year is the
/// link's); <see cref="IsSourceOfCopies"/> on trees those copies project.
/// </summary>
public sealed record OrganizerTree(
    string UniqueId,
    long ElementId,
    string FamilyName,
    string TypeName,
    long TypeId,
    string WorksetName,
    int? WorksetYear,
    string OptionKey,
    string OptionLabel,
    int? ProjectedYears,
    int? LinkedYear,
    string? LinkedSourceUniqueId,
    bool IsSourceOfCopies,
    bool InGroup,
    double X,
    double Y,
    double Z,
    bool IsNewConstruction = false)
{
    public string TypeLabel => $"{FamilyName} : {TypeName}";
}

public sealed record OrganizerTreesResult(
    string DocumentTitle,
    bool IsWorkshared,
    IReadOnlyList<OrganizerTree> Trees,
    IReadOnlyDictionary<int, string> YearWorksets);

/// <summary>A tree that isn't on the workset of its growth year, and the year it belongs to.</summary>
public sealed record TreeMove(OrganizerTree Tree, int Year);

/// <summary>A tree that can't be sorted, and why.</summary>
public sealed record TreeSortProblem(OrganizerTree Tree, string Reason);

/// <summary><see cref="YearFromPhase"/>: trees without a projected year of their own, placed by phase (existing at 0, new at the design year).</summary>
public sealed record TreeSortPlan(IReadOnlyList<TreeMove> Moves, IReadOnlyList<TreeSortProblem> Problems, int YearFromPhase);

public sealed record TreeSortItem(string UniqueId, int Year);

public sealed record TreeSortRequest(IReadOnlyList<TreeSortItem> Items);

public sealed record TreeSortResult(int Moved, int NotEditable, IReadOnlyList<string> WorksetsCreated, IReadOnlyList<string> Notes);

public sealed record DeleteTreesRequest(IReadOnlyList<string> UniqueIds);

/// <summary><see cref="CopiesDeleted"/>: Growth Projections copies of the deleted trees, removed with them.</summary>
public sealed record DeleteTreesResult(int Deleted, int CopiesDeleted, int NotEditable, IReadOnlyList<string> Notes);

/// <summary>Overlapping trees on one workset in one design option: one to keep, the rest to delete.</summary>
public sealed record TreeOverlapGroup(OrganizerTree Keep, IReadOnlyList<OrganizerTree> Extras, double SpreadMeters)
{
    public string WorksetName => Keep.WorksetName;
    public string OptionLabel => Keep.OptionLabel;
}

/// <summary>
/// Sorting by growth year: each tree belongs on the workset of its year (0 = the project start). A copy
/// Growth Projections made belongs to its link's year, since the tool manages it. Any other tree is an
/// original, on its <c>!_S_PLT_TreeGrowth_ProjectedYears_Number</c> when that's above 0, else by phase
/// (<see cref="GrowthYearNames.OwnYear"/>): existing trees as they stand today (0), new ones at the
/// design year. An original whose projected year doesn't say its year yet gets it written too.
/// Grouped trees can't change workset on their own.
/// </summary>
public static class TreeSortPlanner
{
    public static int TargetYear(OrganizerTree tree, int designYear = GrowthYearNames.DefaultDesignYear) =>
        tree.LinkedYear ?? GrowthYearNames.OwnYear(tree.ProjectedYears, tree.IsNewConstruction, designYear);

    public static TreeSortPlan Plan(IEnumerable<OrganizerTree> trees, int designYear = GrowthYearNames.DefaultDesignYear)
    {
        var moves = new List<TreeMove>();
        var problems = new List<TreeSortProblem>();
        var yearFromPhase = 0;
        foreach (var tree in trees)
        {
            var year = TargetYear(tree, designYear);
            if (tree.LinkedYear is null && tree.ProjectedYears is not > 0)
            {
                yearFromPhase++;
            }

            if (year == tree.WorksetYear && (tree.LinkedYear is not null || tree.ProjectedYears == year))
            {
                continue;
            }

            if (year is < 0 or > GrowthYearNames.MaximumYear)
            {
                problems.Add(new TreeSortProblem(tree, $"Projected year {year} is outside 0–{GrowthYearNames.MaximumYear}."));
            }
            else if (tree.InGroup)
            {
                problems.Add(new TreeSortProblem(tree, $"In a group, so it can't move to {GrowthYearNames.WorksetName(year)} on its own: ungroup it."));
            }
            else
            {
                moves.Add(new TreeMove(tree, year));
            }
        }

        return new TreeSortPlan(moves, problems, yearFromPhase);
    }
}

/// <summary>
/// Finds trees that overlap on the same workset in the same design option: within
/// <c>toleranceMeters</c> of each other (3D), and of the same type unless <c>sameTypeOnly</c> is off.
/// Each group keeps one tree: one other copies project, else a Growth Projections copy whose original
/// is kept, else the oldest (lowest element id). Growth Projections copies sit exactly on their
/// originals but on another workset, so they never count.
/// </summary>
public static class TreeOverlapFinder
{
    public static IReadOnlyList<TreeOverlapGroup> Find(IReadOnlyList<OrganizerTree> trees, double toleranceMeters, bool sameTypeOnly)
    {
        var clusters = Cluster(trees, Math.Max(toleranceMeters, 0.001), sameTypeOnly).ToList();

        // Groups without copies first, so a copy whose original is going is never the one kept.
        var groups = clusters
            .Where(cluster => cluster.All(tree => tree.LinkedYear is null))
            .Select(cluster => Split(cluster, new HashSet<string>(StringComparer.Ordinal)))
            .ToList();
        var leaving = groups.SelectMany(group => group.Extras).Select(tree => tree.UniqueId).ToHashSet(StringComparer.Ordinal);
        groups.AddRange(clusters
            .Where(cluster => cluster.Any(tree => tree.LinkedYear is not null))
            .Select(cluster => Split(cluster, leaving)));

        return groups
            .OrderBy(group => group.WorksetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.OptionLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Keep.TypeLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Keep.ElementId)
            .ToList();
    }

    private static TreeOverlapGroup Split(IReadOnlyList<OrganizerTree> cluster, IReadOnlySet<string> leaving)
    {
        var ordered = cluster
            .OrderByDescending(tree => tree.IsSourceOfCopies)
            .ThenBy(tree => tree.LinkedSourceUniqueId is { } source && leaving.Contains(source))
            .ThenByDescending(tree => tree.LinkedYear is not null)
            .ThenBy(tree => tree.ElementId)
            .ToList();
        var spread = cluster.SelectMany(a => cluster.Select(b => Distance(a, b))).Max();
        return new TreeOverlapGroup(ordered[0], ordered.Skip(1).ToList(), spread);
    }

    /// <summary>Clusters of two or more trees linked by pairs within the tolerance (single linkage), per workset, option and type.</summary>
    private static IEnumerable<IReadOnlyList<OrganizerTree>> Cluster(IEnumerable<OrganizerTree> trees, double tolerance, bool sameTypeOnly)
    {
        foreach (var bucket in trees.GroupBy(tree => (tree.WorksetName, tree.OptionKey, Type: sameTypeOnly ? tree.TypeId : 0L)))
        {
            var items = bucket.ToList();
            if (items.Count < 2)
            {
                continue;
            }

            var parent = Enumerable.Range(0, items.Count).ToArray();
            int Root(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }

                return i;
            }

            // A grid of tolerance-sized cells: only neighbouring cells can hold a tree within reach.
            var cells = new Dictionary<(long, long, long), List<int>>();
            (long, long, long) CellOf(OrganizerTree tree) =>
                ((long)Math.Floor(tree.X / tolerance), (long)Math.Floor(tree.Y / tolerance), (long)Math.Floor(tree.Z / tolerance));
            for (var i = 0; i < items.Count; i++)
            {
                var (cx, cy, cz) = CellOf(items[i]);
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (!cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var neighbours))
                    {
                        continue;
                    }

                    foreach (var j in neighbours.Where(j => Distance(items[i], items[j]) <= tolerance))
                    {
                        parent[Root(i)] = Root(j);
                    }
                }

                if (!cells.TryGetValue((cx, cy, cz), out var cell))
                {
                    cells[(cx, cy, cz)] = cell = [];
                }

                cell.Add(i);
            }

            foreach (var cluster in Enumerable.Range(0, items.Count).GroupBy(Root).Where(group => group.Count() > 1))
            {
                yield return cluster.Select(i => items[i]).ToList();
            }
        }
    }

    private static double Distance(OrganizerTree a, OrganizerTree b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
}
