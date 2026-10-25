using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>A tree's size as the planting family draws it, in metres.</summary>
public sealed record TreeSize(double TrunkHeightM, double CrownHeightM, double CrownRadiusM, double TrunkRadiusM)
{
    public double HeightM => TrunkHeightM + CrownHeightM;

    public double CrownAreaM2 => Math.PI * CrownRadiusM * CrownRadiusM;
}

/// <summary>
/// The planting family's size formulas (WWP_Vegetation_Tree), so a tree can be drawn at any growth
/// year without Growth Projections copies. With an i-Tree height or crown width the family takes it
/// as it is — trunk H/3, crown 2H/3, crown radius W/2, not capped at the maximum size. Without one it
/// grows the type's yearly rate over the tree's growth years and caps it at the type's maximum
/// height or width. The trunk grows from the 54 mm planting DBH by the type's trunk rate. Checked on
/// the sandbox's 930 trees: within 1 cm of what the family draws at each tree's own year.
/// </summary>
public static class TreeSizeForecast
{
    public const double PlantingDbhM = 0.054;

    /// <summary>The family ignores an i-Tree size below 1 mm (the parameter's "not calculated" 0).</summary>
    private const double NoSizeM = 0.001;

    /// <summary>
    /// <c>!_S_PLT_TreeGrowth_Years_Number</c>: the tree's age in the family, <paramref name="projectedYears"/>
    /// after planting — held at <c>Mature Age</c>, or 25 years after planting without one, and at least 1.
    /// </summary>
    public static double GrowthYears(int projectedYears, double ageAtPlanting, int matureAge)
    {
        var years = matureAge == 0
            ? ageAtPlanting + Math.Min(projectedYears, TreeTimelines.DefaultHorizonYears)
            : ageAtPlanting + projectedYears > matureAge
                ? Math.Max(ageAtPlanting, matureAge)
                : ageAtPlanting + projectedYears;
        return years < 1 ? 1 : years;
    }

    /// <summary>The size the family draws <paramref name="projectedYears"/> after planting, given the i-Tree size stored on the tree (0 without).</summary>
    public static TreeSize At(TreeGrowthType type, double ageAtPlanting, int projectedYears, double iTreeHeightM, double iTreeCrownWidthM)
    {
        var years = GrowthYears(projectedYears, ageAtPlanting, type.MatureAge);
        var height = iTreeHeightM > NoSizeM ? iTreeHeightM : Capped(years * type.HeightRateMPerYear, type.MaxHeightM);
        var width = iTreeCrownWidthM > NoSizeM ? iTreeCrownWidthM : Capped(years * type.WidthRateMPerYear, type.MaxWidthM);
        var dbh = PlantingDbhM + Math.Max(years - ageAtPlanting, 0) * type.TrunkRateCmPerYear / 100d;
        return new TreeSize(height / 3d, height * 2d / 3d, width / 2d, dbh / 2d);
    }

    /// <summary>
    /// The size <paramref name="projectedYears"/> after planting with i-Tree's forecast size for that
    /// year (see <see cref="TimelineSum.InYear"/>), or the type's growth rates without a forecast.
    /// </summary>
    public static TreeSize At(TreeGrowthType type, double ageAtPlanting, int projectedYears, TreeBenefitTimeline? timeline)
    {
        if (timeline is null || timeline.Years.Count == 0)
        {
            return At(type, ageAtPlanting, projectedYears, 0, 0);
        }

        var year = TimelineSum.InYear(timeline, projectedYears);
        return At(type, ageAtPlanting, projectedYears, year.HeightM, year.CrownWidthM);
    }

    /// <summary>Years 0 to <paramref name="lastYear"/>, one size each.</summary>
    public static IReadOnlyList<TreeSize> Profile(TreeGrowthType type, double ageAtPlanting, TreeBenefitTimeline? timeline, int lastYear) =>
        Enumerable.Range(0, lastYear + 1).Select(year => At(type, ageAtPlanting, year, timeline)).ToList();

    /// <summary>The family's cap: a maximum under 1 m means the type has none.</summary>
    private static double Capped(double grown, double maximum) => maximum < 1 || grown < maximum ? grown : maximum;
}
