namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Infers Pervious/Impervious for the Softscape Surface Ratio KPI from a Floor's
/// <c>!_S_PLT_LDS_Type_Text</c> value (e.g. "Lawn", "Granite - Blanco Cristal") — the same text
/// already auto-populated by the Excel importer/Floor Calculator for every floor, so most projects
/// need no manual <c>!_S_PLT_LDS_SurfaceClass_Text</c> tagging at all. That tag still wins whenever a
/// project team sets it explicitly (see <see cref="DashboardAggregationService.BuildSiteKpiSummary"/>) —
/// this is only the fallback for floors that haven't been tagged.
/// </summary>
public static class SurfaceClassCatalog
{
    private static readonly string[] ImperviousKeywords =
    [
        "paving", "pavement", "granite", "concrete", "asphalt", "tarmac", "cobble", "stone",
        "tile", "hardscape", "decking", "resin", "brick", "slab"
    ];

    private static readonly string[] PerviousKeywords =
    [
        "lawn", "meadow", "grass", "turf", "soil", "mulch", "gravel", "planting", "shrub",
        "hedge", "wildflower", "permeable", "swale", "rain garden", "bioswale"
    ];

    /// <summary>Returns "Pervious", "Impervious", or null if <paramref name="ldsType"/> doesn't match any known keyword.</summary>
    public static string? Infer(string? ldsType)
    {
        if (string.IsNullOrWhiteSpace(ldsType))
        {
            return null;
        }

        var normalized = ldsType.ToLowerInvariant();
        if (ImperviousKeywords.Any(keyword => normalized.Contains(keyword)))
        {
            return "Impervious";
        }

        if (PerviousKeywords.Any(keyword => normalized.Contains(keyword)))
        {
            return "Pervious";
        }

        return null;
    }
}
