using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// What the Dashboard flags about the model itself, over every design option rather than the
/// scenario shown: the connector's setup warnings (parameters not bound, tree sizes stored as mm),
/// trees in groups, and floors Floor Calculator hasn't calculated. Each line says how to fix it;
/// <see cref="UniqueIds"/> are the trees and floors behind them, to select in Revit.
/// </summary>
public sealed record DashboardModelChecks(IReadOnlyList<string> Lines, IReadOnlyList<string> UniqueIds)
{
    public bool HasIssues => Lines.Count > 0;

    public static DashboardModelChecks For(DashboardReportResult report)
    {
        var lines = new List<string>(report.ModelWarnings ?? []);
        var ids = new List<string>();

        // Every instance of a group shares one age and growth year, so they can't grow separately.
        var grouped = report.Trees.Where(tree => tree.Inputs?.InGroup == true).ToList();
        if (grouped.Count > 0)
        {
            lines.Add($"{grouped.Count:N0} tree(s) are in a group: a group shares one age and growth year, so Growth Projections can't age them separately. Ungroup them.");
            ids.AddRange(grouped.Select(tree => tree.UniqueId));
        }

        // Structural slabs aren't landscape, and an excluded floor (built and demolished in one
        // phase) was never on site. HasLdsResult is null from an older connector: nothing to say.
        var uncalculated = report.Floors
            .Where(floor => floor.HasLdsResult == false && !floor.IsStructural && SitePhases.RoleOf(floor) != BngPhaseRoles.Excluded)
            .ToList();
        var unmatched = uncalculated.Where(floor => string.IsNullOrWhiteSpace(floor.LdsType)).ToList();
        var matched = uncalculated.Count - unmatched.Count;
        if (unmatched.Count > 0)
        {
            lines.Add($"{unmatched.Count:N0} floor(s) have no landscape type, so they add nothing: in Floor Calculator, match them to the WWP sheet and calculate.");
        }

        if (matched > 0)
        {
            lines.Add($"{matched:N0} floor(s) are matched to a landscape type but not calculated yet: calculate them in Floor Calculator.");
        }

        ids.AddRange(uncalculated.Select(floor => floor.UniqueId));
        return new DashboardModelChecks(lines, ids);
    }
}
