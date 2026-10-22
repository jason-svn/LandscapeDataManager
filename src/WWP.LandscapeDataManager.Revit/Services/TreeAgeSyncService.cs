using Autodesk.Revit.DB;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Gives each growth-year tree with no <c>!_S_PLT_TreeGrowth_ProjectedYears_Number</c> the growth
/// year of its workset (see <see cref="GrowthYearNames"/>). A projected year the tree already has is
/// kept: it decides the tree's workset, and one that differs is counted for Growth Projections' Sort
/// &amp; check trees, which moves the tree, rather than overwritten. The planting family computes the
/// modelled age (<c>!_S_PLT_TreeGrowth_Years_Number</c>, which drives DBH, crown and height) as
/// AgeAtPlanting + ProjectedYears. For a family that doesn't have that formula yet — Years is still a
/// plain editable value — Years is also set here (age at planting + year, capped at mature size), so
/// projections keep working until the family is updated. Runs before Tree Calculator reads its
/// inputs, so i-Tree sees the tree at its projected age. Trees on no growth-year workset are never touched.
/// </summary>
internal static class TreeAgeSyncService
{
    internal const string YearsParameter = "!_S_PLT_TreeGrowth_Years_Number";
    internal const string AgeAtPlantingParameter = "!_S_PLT_TreeGrowth_AgeAtPlanting_Number";
    internal const string ProjectedYearsParameter = "!_S_PLT_TreeGrowth_ProjectedYears_Number";

    /// <summary>The tree's own age at planting — on the instance, or its type in projects not yet migrated.</summary>
    internal static int? AgeAtPlanting(Element tree) =>
        ElementParameters.LookupOnInstanceOrType(tree, AgeAtPlantingParameter) is { HasValue: true, StorageType: StorageType.Integer } age
            ? age.AsInteger()
            : null;

    public static TreeAgeSyncSummary Apply(Document document, IReadOnlyList<Element> trees)
    {
        if (!document.IsWorkshared)
        {
            return new TreeAgeSyncSummary(0, 0, 0, 0, 0);
        }

        var worksets = document.GetWorksetTable();
        var matureAges = TreeMaturityService.GetMatureAges(
            document, trees.Where(tree => GrowthYearNames.FromName(worksets.GetWorkset(tree.WorksetId)?.Name) is not null));
        var candidates = new List<(Element Tree, List<(Parameter Parameter, int Value)> Writes)>();
        int onGrowthWorksets = 0, notEditable = 0, missingYears = 0, skippedGrouped = 0, capped = 0, notOnTheirWorkset = 0;
        foreach (var tree in trees)
        {
            if (GrowthYearNames.FromName(worksets.GetWorkset(tree.WorksetId)?.Name) is not { } worksetYear)
            {
                continue;
            }

            onGrowthWorksets++;
            var projected = tree.LookupParameter(ProjectedYearsParameter) is { StorageType: StorageType.Integer } p ? p : null;
            var growthYear = projected is { HasValue: true } ? projected.AsInteger() : worksetYear;
            if (growthYear != worksetYear)
            {
                notOnTheirWorkset++;
            }

            var years = tree.LookupParameter(YearsParameter) is { StorageType: StorageType.Integer } y ? y : null;
            // Nothing to drive the age: no ProjectedYears to write, and Years is missing or formula-driven.
            if (projected is null && years is not { IsReadOnly: false })
            {
                missingYears++;
                continue;
            }

            if (tree.GroupId != ElementId.InvalidElementId &&
                new[] { projected, years }.Any(parameter => parameter?.Definition is InternalDefinition { VariesAcrossGroups: false }))
            {
                skippedGrouped++;
                continue;
            }

            var writes = new List<(Parameter Parameter, int Value)>();
            if (projected is { HasValue: false })
            {
                if (projected.IsReadOnly)
                {
                    notEditable++;
                    continue;
                }

                writes.Add((projected, growthYear));
            }

            var ageAtPlanting = AgeAtPlanting(tree);
            var actualAge = GrowthYearNames.ModelledAge(
                ageAtPlanting, growthYear, matureAges.TryGetValue(tree.GetTypeId().Value, out var mature) ? mature : null);
            // A read-only Years is the family's own AgeAtPlanting + ProjectedYears formula: leave it to the family.
            if (years is { IsReadOnly: false })
            {
                if (actualAge < GrowthYearNames.ActualAge(ageAtPlanting, growthYear))
                {
                    capped++;
                }

                if (!(years.HasValue && years.AsInteger() == actualAge))
                {
                    writes.Add((years, actualAge));
                }
            }

            if (writes.Count > 0)
            {
                candidates.Add((tree, writes));
            }
        }

        if (candidates.Count == 0)
        {
            return new TreeAgeSyncSummary(onGrowthWorksets, 0, notEditable, missingYears, skippedGrouped, capped, notOnTheirWorkset);
        }

        if (document.IsReadOnly)
        {
            return new TreeAgeSyncSummary(
                onGrowthWorksets, 0, notEditable + candidates.Count, missingYears, skippedGrouped, NotOnTheirWorkset: notOnTheirWorkset);
        }

        var editableIds = document.IsDetached
            ? candidates.Select(candidate => candidate.Tree.Id).ToHashSet()
            : WorksharingUtils.CheckoutElements(document, candidates.Select(candidate => candidate.Tree.Id).ToList()).ToHashSet();
        var editable = candidates.Where(candidate => editableIds.Contains(candidate.Tree.Id)).ToList();
        notEditable += candidates.Count - editable.Count;
        if (editable.Count == 0)
        {
            return new TreeAgeSyncSummary(onGrowthWorksets, 0, notEditable, missingYears, skippedGrouped, NotOnTheirWorkset: notOnTheirWorkset);
        }

        using var transaction = new Transaction(document, "LIM Update tree ages from growth-year worksets");
        if (transaction.Start() != TransactionStatus.Started)
        {
            return new TreeAgeSyncSummary(
                onGrowthWorksets, 0, notEditable + editable.Count, missingYears, skippedGrouped, NotOnTheirWorkset: notOnTheirWorkset);
        }

        foreach (var (_, writes) in editable)
        {
            foreach (var (parameter, value) in writes)
            {
                parameter.Set(value);
            }
        }

        var commitStatus = transaction.Commit();
        return commitStatus == TransactionStatus.Committed
            ? new TreeAgeSyncSummary(onGrowthWorksets, editable.Count, notEditable, missingYears, skippedGrouped, capped, notOnTheirWorkset)
            : new TreeAgeSyncSummary(onGrowthWorksets, 0, notEditable + editable.Count, missingYears, skippedGrouped, NotOnTheirWorkset: notOnTheirWorkset);
    }
}
