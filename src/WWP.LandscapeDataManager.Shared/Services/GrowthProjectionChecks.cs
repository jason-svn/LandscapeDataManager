using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Sanity checks on growth-year trees before their i-Tree results are trusted.</summary>
public static class GrowthProjectionChecks
{
    /// <summary>
    /// Planting types whose trunk diameter (DBH) is the same in every growth year they appear in.
    /// i-Tree is sent DBH, so such a type gets identical results for Year 0, 10 and 25 — usually
    /// because its trunk growth rate (<c>!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number</c>) is blank
    /// or 0, or its family doesn't drive DBH from the tree's age. Types seen in one year only can't
    /// be judged and are left out.
    /// </summary>
    public static IReadOnlyList<string> TypesWithUnchangingDbh(IEnumerable<PlantingInstanceValidationItem> growthTrees) =>
        growthTrees
            .Where(tree => tree.GrowthYear is not null && tree.DbhInches is not null)
            .GroupBy(tree => $"{tree.FamilyName} : {tree.TypeName}", StringComparer.Ordinal)
            .Where(type => type.Select(tree => tree.GrowthYear).Distinct().Count() > 1 &&
                           type.Select(tree => Math.Round(tree.DbhInches!.Value, 4)).Distinct().Count() == 1)
            .Select(type => type.Key)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
