namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Infers Pervious/Impervious for the Softscape Surface Ratio KPI from a Floor's
/// <c>!_S_PLT_LDS_Type_Text</c> value (e.g. "Lawn", "Granite - Blanco Cristal") — the same text
/// already auto-populated by the Excel importer/Floor Calculator for every floor, so most projects
/// need no manual <c>!_S_PLT_LDS_SurfaceClass_Text</c> tagging at all. That tag still wins whenever a
/// project team sets it explicitly (see <see cref="DashboardAggregationService.BuildSiteKpiSummary"/>) —
/// this is only the fallback for floors that haven't been tagged.
/// </summary>
/// <remarks>
/// Rules run from most to least specific, because real names mix words: "Permeable Block Paving"
/// and "Porous Asphalt" contain hard-surface words but are permeable, while "Artificial Grass"
/// contains a soft one but is a synthetic surface. So permeable/green-infrastructure terms win
/// first, then synthetic surfaces, then plain hard words, then plain soft words.
/// </remarks>
public static class SurfaceClassCatalog
{
    private static readonly string[] PermeableOverrides =
    [
        "permeable", "porous", "pervious", "grasscrete", "grass reinforced", "reinforced grass",
        "grass paver", "grass block", "green roof", "living roof", "brown roof", "sedum", "hoggin",
        "rain garden", "bioretention", "bio-retention", "swale", "soakaway"
    ];

    private static readonly string[] SyntheticOverrides =
    [
        "artificial", "synthetic", "astroturf", "rubber", "wet-pour", "wetpour"
    ];

    private static readonly string[] ImperviousKeywords =
    [
        "paving", "pavement", "granite", "concrete", "asphalt", "tarmac", "cobble", "stone", "sett",
        "tile", "hardscape", "decking", "resin", "brick", "slab", "porcelain", "macadam"
    ];

    private static readonly string[] PerviousKeywords =
    [
        "lawn", "meadow", "grass", "turf", "soil", "mulch", "bark", "gravel", "planting", "planter",
        "shrub", "hedge", "wildflower", "woodland", "bed"
    ];

    /// <summary>Returns "Pervious", "Impervious", or null if <paramref name="ldsType"/> doesn't match any known keyword.</summary>
    public static string? Infer(string? ldsType)
    {
        if (string.IsNullOrWhiteSpace(ldsType))
        {
            return null;
        }

        var normalized = ldsType.ToLowerInvariant();
        bool Has(string[] keywords) => keywords.Any(normalized.Contains);

        if (Has(PermeableOverrides))
        {
            return "Pervious";
        }

        if (Has(SyntheticOverrides) || Has(ImperviousKeywords))
        {
            return "Impervious";
        }

        return Has(PerviousKeywords) ? "Pervious" : null;
    }
}
