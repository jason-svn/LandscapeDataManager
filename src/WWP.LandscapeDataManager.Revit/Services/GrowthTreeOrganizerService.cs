using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Growth Projections' "Sort &amp; check trees": reads every tree with its workset, design option,
/// projected year and position (<see cref="ReadTrees"/>); moves trees onto the workset of their growth
/// year (<see cref="Sort"/>, planned by <see cref="TreeSortPlanner"/>); and deletes the overlapping
/// trees the user chose to remove (<see cref="Delete"/>, found by <see cref="TreeOverlapFinder"/>).
/// </summary>
internal static class GrowthTreeOrganizerService
{
    public static OrganizerTreesResult ReadTrees(UIApplication application)
    {
        var document = GetDocument(application);
        var worksets = document.IsWorkshared ? document.GetWorksetTable() : null;
        var trees = PlantingTrees(document);
        var links = trees.Select(tree => (Tree: tree, Link: GrowthProjectionService.LinkOf(tree))).ToList();
        var sources = links.Where(entry => entry.Link is not null).Select(entry => entry.Link!.SourceUniqueId).ToHashSet(StringComparer.Ordinal);
        var optionLabels = new Dictionary<long, string>();

        var items = links.Select(entry =>
        {
            var tree = entry.Tree;
            var worksetName = worksets?.GetWorkset(tree.WorksetId)?.Name ?? string.Empty;
            var option = tree.DesignOption;
            var optionLabel = option is null
                ? "Main model"
                : optionLabels.TryGetValue(option.Id.Value, out var label)
                    ? label
                    : optionLabels[option.Id.Value] = GrowthProjectionService.OptionLabel(document, option);
            var (x, y, z) = PositionInMetres(tree);
            var type = document.GetElement(tree.GetTypeId()) as ElementType;
            return new OrganizerTree(
                tree.UniqueId,
                tree.Id.Value,
                type is FamilySymbol symbol ? symbol.FamilyName : type?.FamilyName ?? string.Empty,
                type?.Name ?? tree.Name,
                tree.GetTypeId().Value,
                worksetName,
                document.IsWorkshared ? GrowthYearNames.FromName(worksetName) : null,
                GrowthProjectionService.OptionKey(tree),
                optionLabel,
                GrowthProjectionService.ProjectedYearOf(tree),
                entry.Link?.Year,
                entry.Link?.SourceUniqueId,
                sources.Contains(tree.UniqueId),
                tree.GroupId != ElementId.InvalidElementId,
                x, y, z);
        }).ToList();

        var yearWorksets = GrowthProjectionService.FindYearWorksets(document)
            .ToDictionary(entry => entry.Key, entry => entry.Value.Name);
        return new OrganizerTreesResult(document.Title, document.IsWorkshared, items, yearWorksets);
    }

    /// <summary>
    /// Moves each tree onto its growth year's workset, creating missing ones (one undo step). A tree's
    /// projected year is set to match too (only Growth Projections copies can differ: their link's year
    /// decides), and so is its age where the family has no age formula yet.
    /// </summary>
    public static TreeSortResult Sort(UIApplication application, TreeSortRequest request)
    {
        var document = GetDocument(application);
        if (!document.IsWorkshared)
        {
            throw new InvalidOperationException("Growth years are kept on worksets, so the model must be workshared — enable it under Collaborate > Worksharing first.");
        }

        var byUniqueId = request.Items
            .Where(item => item.Year is >= 0 and <= GrowthYearNames.MaximumYear)
            .GroupBy(item => item.UniqueId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Year, StringComparer.Ordinal);
        var trees = byUniqueId.Keys
            .Select(document.GetElement)
            .Where(element => element is FamilyInstance && element.Category?.BuiltInCategory == BuiltInCategory.OST_Planting)
            .ToList();
        var notes = new List<string>();
        var grouped = trees.Count(tree => tree.GroupId != ElementId.InvalidElementId);
        if (grouped > 0)
        {
            notes.Add($"{grouped:N0} tree(s) in groups were skipped: ungroup them to sort them.");
            trees = trees.Where(tree => tree.GroupId == ElementId.InvalidElementId).ToList();
        }

        if (trees.Count == 0)
        {
            return new TreeSortResult(0, 0, [], notes);
        }

        // Measured before the transaction: it probes sample trees in a rolled-back transaction of its own.
        var matureAges = TreeMaturityService.GetMatureAges(document, trees);
        var yearWorksets = GrowthProjectionService.FindYearWorksets(document);
        var created = new List<string>();
        int moved = 0, notEditable;
        using var transaction = new Transaction(document, "LIM Sort trees into growth-year worksets");
        transaction.Start();
        try
        {
            var editable = WorksharingUtils.CheckoutElements(document, trees.Select(tree => tree.Id).ToList())
                .Select(id => id.Value)
                .ToHashSet();
            notEditable = trees.Count(tree => !editable.Contains(tree.Id.Value));
            if (notEditable > 0)
            {
                notes.Add($"{notEditable:N0} tree(s) are borrowed by someone else and were skipped: sync with central and sort again.");
            }

            foreach (var tree in trees.Where(tree => editable.Contains(tree.Id.Value)))
            {
                var year = byUniqueId[tree.UniqueId];
                if (!yearWorksets.TryGetValue(year, out var workset))
                {
                    var name = GrowthYearNames.WorksetName(year);
                    if (!WorksetTable.IsWorksetNameUnique(document, name))
                    {
                        throw new InvalidOperationException($"A workset named '{name}' exists but isn't a user workset — rename it and sort again.");
                    }

                    workset = yearWorksets[year] = Workset.Create(document, name);
                    created.Add(name);
                }

                GrowthProjectionService.SetWorkset(tree, workset.Id);
                if (GrowthProjectionService.ProjectedYearOf(tree) != year ||
                    tree.LookupParameter(TreeAgeSyncService.YearsParameter) is { IsReadOnly: false })
                {
                    GrowthProjectionService.SetAge(document, tree, year, matureAges);
                }

                moved++;
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        return new TreeSortResult(moved, notEditable, created, notes);
    }

    /// <summary>
    /// Deletes the given trees (Planting instances only) in one undo step, with the Growth Projections
    /// copies that project them: those would only be removed as orphans on the next run anyway.
    /// </summary>
    public static DeleteTreesResult Delete(UIApplication application, DeleteTreesRequest request)
    {
        var document = GetDocument(application);
        var notes = new List<string>();
        var requested = request.UniqueIds.ToHashSet(StringComparer.Ordinal);
        var trees = requested
            .Select(document.GetElement)
            .Where(element => element is FamilyInstance && element.Category?.BuiltInCategory == BuiltInCategory.OST_Planting)
            .ToList();
        var grouped = trees.Count(tree => tree.GroupId != ElementId.InvalidElementId);
        if (grouped > 0)
        {
            notes.Add($"{grouped:N0} tree(s) in groups were skipped: ungroup them to delete them.");
            trees = trees.Where(tree => tree.GroupId == ElementId.InvalidElementId).ToList();
        }

        var deleting = trees.Select(tree => tree.UniqueId).ToHashSet(StringComparer.Ordinal);
        var copies = PlantingTrees(document)
            .Where(tree => !deleting.Contains(tree.UniqueId) && tree.GroupId == ElementId.InvalidElementId &&
                           GrowthProjectionService.LinkOf(tree) is { } link && deleting.Contains(link.SourceUniqueId))
            .ToList();
        var all = trees.Concat(copies).ToList();
        if (all.Count == 0)
        {
            return new DeleteTreesResult(0, 0, 0, notes);
        }

        using var transaction = new Transaction(document, "LIM Delete overlapping trees");
        transaction.Start();
        try
        {
            var editable = document.IsWorkshared
                ? WorksharingUtils.CheckoutElements(document, all.Select(tree => tree.Id).ToList()).Select(id => id.Value).ToHashSet()
                : all.Select(tree => tree.Id.Value).ToHashSet();
            var notEditable = all.Count(tree => !editable.Contains(tree.Id.Value));
            if (notEditable > 0)
            {
                notes.Add($"{notEditable:N0} tree(s) are borrowed by someone else and were kept: sync with central and delete again.");
            }

            var deletedTrees = trees.Count(tree => editable.Contains(tree.Id.Value));
            var deletedCopies = copies.Count(tree => editable.Contains(tree.Id.Value));
            var ids = all.Where(tree => editable.Contains(tree.Id.Value)).Select(tree => tree.Id).ToList();
            if (ids.Count > 0)
            {
                // Growth Projections pins its copies; unpin them so they can go.
                foreach (var id in ids)
                {
                    GrowthProjectionService.SetPinned(document.GetElement(id), false);
                }

                document.Delete(ids);
            }

            transaction.Commit();
            return new DeleteTreesResult(deletedTrees, deletedCopies, notEditable, notes);
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }
    }

    private static List<Element> PlantingTrees(Document document) =>
        new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Planting)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .ToList();

    /// <summary>The tree's insertion point (else its bounding box centre) in metres.</summary>
    private static (double X, double Y, double Z) PositionInMetres(Element tree)
    {
        var point = tree.Location is LocationPoint location
            ? location.Point
            : tree.get_BoundingBox(null) is { } box ? (box.Min + box.Max) / 2 : XYZ.Zero;
        static double Metres(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
        return (Metres(point.X), Metres(point.Y), Metres(point.Z));
    }

    private static Document GetDocument(UIApplication application) =>
        application.ActiveUIDocument?.Document
        ?? throw new InvalidOperationException("Open a Revit project before sorting trees.");
}
