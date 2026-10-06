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
            return new TreeAgeSyncSummary(0, 0, 0, 0);
        }

        var worksets = document.GetWorksetTable();
        var changes = new List<(Parameter Years, int Age)>();
        int onGrowthWorksets = 0, notEditable = 0, missingYears = 0;
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

            var ageAtPlanting = (document.GetElement(tree.GetTypeId()) as ElementType)?.LookupParameter(AgeAtPlantingParameter) is
                { HasValue: true, StorageType: StorageType.Integer } age
                ? age.AsInteger()
                : (int?)null;
            var actualAge = GrowthYearNames.ActualAge(ageAtPlanting, growthYear);
            if (years.HasValue && years.AsInteger() == actualAge)
            {
                continue;
            }

            // A tree someone else has borrowed can't be edited from this session; leave it and say so.
            if (years.IsReadOnly || WorksharingUtils.GetCheckoutStatus(document, tree.Id) == CheckoutStatus.OwnedByOtherUser)
            {
                notEditable++;
                continue;
            }

            changes.Add((years, actualAge));
        }

        if (changes.Count > 0)
        {
            using var transaction = new Transaction(document, "LIM Update tree ages from growth-year worksets");
            transaction.Start();
            foreach (var (years, age) in changes)
            {
                years.Set(age);
            }

            transaction.Commit();
        }

        return new TreeAgeSyncSummary(onGrowthWorksets, changes.Count, notEditable, missingYears);
    }
}
