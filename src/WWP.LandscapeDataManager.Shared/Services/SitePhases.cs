using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Which trees and planting areas a scenario's benefits count, from their phases (see
/// <see cref="BngPhaseRoles"/>): a scheme counts what stands after development (retained and created,
/// not demolished); the existing site counts what stood in the first phase (retained and lost). BNG
/// is the exception: it needs a scheme's lost floors for its baseline, so it takes them all.
/// Elements without a phase role (a report from an older connector) count in both.
/// </summary>
public static class SitePhases
{
    public const string ExistingSiteName = "Existing site";

    /// <summary>Stands after development: retained, enhanced or created — not demolished.</summary>
    public static bool StandsAfter(string? role) =>
        role is null or BngPhaseRoles.Retained or BngPhaseRoles.Enhanced or BngPhaseRoles.Created;

    /// <summary>Stood in the first phase: retained, enhanced or lost (demolished later).</summary>
    public static bool StoodBefore(string? role) =>
        role is null or BngPhaseRoles.Retained or BngPhaseRoles.Enhanced or BngPhaseRoles.Lost;

    public static bool Counts(string? role, DesignScenario scenario) =>
        scenario.IsExistingSite ? StoodBefore(role) : StandsAfter(role);

    public static string? RoleOf(DashboardFloorItem floor) => floor.PhaseRole ?? floor.Bng?.PhaseRole;

    /// <summary>True when anything in the report stood in the first phase, so there's an existing site to compare with.</summary>
    public static bool HasExistingSite(DashboardReportResult report) =>
        report.Trees.Any(tree => tree.PhaseRole is BngPhaseRoles.Retained or BngPhaseRoles.Lost) ||
        report.Floors.Any(floor => RoleOf(floor) is BngPhaseRoles.Retained or BngPhaseRoles.Lost);
}
