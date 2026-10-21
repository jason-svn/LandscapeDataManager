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
    public const string Pervious = "Pervious";
    public const string Impervious = "Impervious";

    /// <summary>A building's own floor slab: not landscape, so it's left out of the greenspace ratio.</summary>
    public const string Building = "Building";

    private static readonly string[] BuildingKeywords =
    [
        "structural", "building", "suspended", "screed", "podium", "ground bearing", "ground-bearing", "transfer slab"
    ];

    /// <summary>Words that make a "slab" a paving slab rather than a building slab.</summary>
    private static readonly string[] LandscapeSlabKeywords =
    [
        "paving", "paver", "flag", "stone", "granite", "porcelain", "tile", "step", "kerb", "permeable"
    ];

    /// <summary>
    /// True for a building floor slab, which isn't landscape at all. A floor tagged "Building" (or
    /// "Excluded") always is. A floor with an LDS type or another surface class tag never is — it was
    /// matched as landscape. Otherwise it is when Revit marks it structural, or its type or family name
    /// says so ("Structural slab", "Suspended slab", a "slab" that isn't a paving slab, …).
    /// </summary>
    public static bool IsBuildingSlab(string? surfaceClassTag, string? ldsType, bool isStructural, params string?[] names)
    {
        if (!string.IsNullOrWhiteSpace(surfaceClassTag))
        {
            var tag = surfaceClassTag.Trim();
            return tag.Equals(Building, StringComparison.OrdinalIgnoreCase) || tag.Equals("Excluded", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(ldsType))
        {
            return false;
        }

        return isStructural || names.Any(name =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var normalized = name.ToLowerInvariant();
            return BuildingKeywords.Any(normalized.Contains) ||
                   (normalized.Contains("slab") && !LandscapeSlabKeywords.Any(normalized.Contains));
        });
    }

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
        "shrub", "hedge", "wildflower", "woodland", "bed", "vegetation", "vegetated"
    ];

    /// <summary>
    /// A floor's surface class: its explicit <c>!_S_PLT_LDS_SurfaceClass_Text</c> tag, else inferred
    /// from its LDS Type, else from its floor type and family names (e.g. "WWP_Vegetation_Shrub") —
    /// so floors nobody has matched in Floor Calculator yet still count. Null when nothing matches.
    /// </summary>
    public static string? Resolve(string? surfaceClassTag, string? ldsType, params string?[] names)
    {
        if (!string.IsNullOrWhiteSpace(surfaceClassTag))
        {
            return surfaceClassTag.Trim();
        }

        return Infer(ldsType) ?? names.Select(Infer).FirstOrDefault(inferred => inferred is not null);
    }

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
