using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// The Connectivity tool's per-project settings, kept in <see cref="ProjectSettingsSnapshot"/>.
/// <see cref="DispersalDistancesMeters"/> are the distances at which dispersal probability is
/// <see cref="ProbabilityAtDistance"/>; <see cref="IncludeUnclassifiedFloors"/> counts floors whose
/// surface class can't be told from their LDS Type as planting areas.
/// </summary>
public sealed record ConnectivitySettings(
    IReadOnlyList<double> DispersalDistancesMeters,
    double ProbabilityAtDistance = 0.5,
    bool IncludeFloors = true,
    bool IncludeUnclassifiedFloors = false)
{
    public static ConnectivitySettings Default { get; } = new([50, 250, 1000]);
}

/// <summary>
/// Decides which scanned elements are habitat patches: every tree, and every Floor that is a
/// planting area — Pervious by its <c>!_S_PLT_LDS_SurfaceClass_Text</c> tag, or failing that by
/// <see cref="SurfaceClassCatalog.Resolve"/> on its LDS Type or type name. Building slabs and paving are never patches.
/// </summary>
public static class ConnectivityPatchSelector
{
    /// <summary>Null when the item is a patch; otherwise why it's left out.</summary>
    public static string? ExclusionReason(ConnectivityPatchItem item, ConnectivitySettings settings)
    {
        if (item.AreaSquareMeters <= 0)
        {
            return "No area.";
        }

        if (item.Category != "Floor")
        {
            return null;
        }

        if (!settings.IncludeFloors)
        {
            return "Floors are switched off.";
        }

        var surfaceClass = SurfaceClassCatalog.Resolve(item.SurfaceClass, item.LdsType, item.TypeName, item.FamilyName);
        if (string.Equals(surfaceClass, "Pervious", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(surfaceClass, "Impervious", StringComparison.OrdinalIgnoreCase))
        {
            return "Impervious surface — not habitat.";
        }

        return settings.IncludeUnclassifiedFloors ? null : "Surface class unknown — set its LDS Type or SurfaceClass, or include unclassified floors.";
    }

    public static ConnectivityPatch ToPatch(ConnectivityPatchItem item) =>
        new(
            item.UniqueId,
            item.AreaSquareMeters,
            new PlanPoint(item.Center.X, item.Center.Y),
            item.RadiusMeters,
            item.Outline?.Select(point => new PlanPoint(point.X, point.Y)).ToList());
}
