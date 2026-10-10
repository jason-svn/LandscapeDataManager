using Autodesk.Revit.DB;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Keeps each tree's modelled age (<c>!_S_PLT_TreeGrowth_Years_Number</c>, which drives the
/// planting family's growth formulas and so its DBH, crown and height) equal to its actual age:
/// the age at planting on its type plus the growth year of its workset (see
/// <see cref="GrowthYearNames"/>). Runs before Tree Calculator reads its inputs, so i-Tree sees
/// the tree at its real age. Trees on no growth-year workset are never touched.
/// </summary>
internal static class TreeAgeSyncService
{
    internal const string YearsParameter = "!_S_PLT_TreeGrowth_Years_Number";
    internal const string AgeAtPlantingParameter = "!_S_PLT_TreeGrowth_AgeAtPlanting_Number";

    public static TreeAgeSyncSummary Apply(Document document, IReadOnlyList<Element> trees)
    {
        if (!document.IsWorkshared)
        {
            return new TreeAgeSyncSummary(0, 0, 0, 0, 0);
        }

        var worksets = document.GetWorksetTable();
        var candidates = new List<(Element Tree, Parameter Years, int Age)>();
        int onGrowthWorksets = 0, notEditable = 0, missingYears = 0, skippedGrouped = 0;
        foreach (var tree in trees)
        {
            if (GrowthYearNames.FromName(worksets.GetWorkset(tree.WorksetId)?.Name) is not { } growthYear)
            {
                continue;
            }

            onGrowthWorksets++;
            if (tree.LookupParameter(YearsParameter) is not { StorageType: StorageType.Integer } years)
            {
                missingYears++;
                continue;
            }

            if (tree.GroupId != ElementId.InvalidElementId &&
                years.Definition is InternalDefinition { VariesAcrossGroups: false })
            {
                skippedGrouped++;
                continue;
            }

            var ageAtPlanting = (document.GetElement(tree.GetTypeId()) as ElementType)?.LookupParameter(AgeAtPlantingParameter) is
                { HasValue: true, StorageType: StorageType.Integer } age
                ? age.AsInteger()
                : (int?)null;
            var actualAge = GrowthYearNames.ActualAge(ageAtPlanting, growthYear);
            if (years.HasValue && years.AsInteger() == actualAge)
            {
                continue;
            }

            if (years.IsReadOnly)
            {
                notEditable++;
                continue;
            }

            candidates.Add((tree, years, actualAge));
        }

        if (candidates.Count == 0)
        {
            return new TreeAgeSyncSummary(onGrowthWorksets, 0, notEditable, missingYears, skippedGrouped);
        }

        if (document.IsReadOnly)
        {
            return new TreeAgeSyncSummary(
                onGrowthWorksets, 0, notEditable + candidates.Count, missingYears, skippedGrouped);
        }

        var editableIds = document.IsDetached
            ? candidates.Select(candidate => candidate.Tree.Id).ToHashSet()
            : WorksharingUtils.CheckoutElements(document, candidates.Select(candidate => candidate.Tree.Id).ToList()).ToHashSet();
        var editable = candidates.Where(candidate => editableIds.Contains(candidate.Tree.Id)).ToList();
        notEditable += candidates.Count - editable.Count;
        if (editable.Count == 0)
        {
            return new TreeAgeSyncSummary(onGrowthWorksets, 0, notEditable, missingYears, skippedGrouped);
        }

        using var transaction = new Transaction(document, "LIM Update tree ages from growth-year worksets");
        if (transaction.Start() != TransactionStatus.Started)
        {
            return new TreeAgeSyncSummary(
                onGrowthWorksets, 0, notEditable + editable.Count, missingYears, skippedGrouped);
        }

        foreach (var (_, years, age) in editable)
        {
            years.Set(age);
        }

        var commitStatus = transaction.Commit();
        return commitStatus == TransactionStatus.Committed
            ? new TreeAgeSyncSummary(onGrowthWorksets, editable.Count, notEditable, missingYears, skippedGrouped)
            : new TreeAgeSyncSummary(onGrowthWorksets, 0, notEditable + editable.Count, missingYears, skippedGrouped);
    }
}
