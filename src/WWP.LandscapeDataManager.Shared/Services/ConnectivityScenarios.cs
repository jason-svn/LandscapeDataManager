using System.Globalization;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// One design scenario's habitat connectivity from the Connectivity tool, saved on the project
/// (<see cref="ProjectSettingsSnapshot.ConnectivityResults"/>) for the Dashboard: the equivalent
/// connected area (EC) per dispersal distance and the habitat area it's out of. It's the scheme as
/// drawn: new trees at <see cref="DesignYear"/>, existing ones today (growth-year copies don't count).
/// </summary>
public sealed record ConnectivityScenarioResult(
    string Scenario,
    bool IsExistingSite,
    IReadOnlyList<double> DispersalDistancesMeters,
    double ProbabilityAtDistance,
    IReadOnlyList<double> EquivalentConnectedAreaByDistance,
    double HabitatAreaSquareMeters,
    int Patches,
    string CalculatedUtc,
    int DesignYear = GrowthYearNames.DefaultDesignYear)
{
    /// <summary>EC averaged over the dispersal distances, in m².</summary>
    public double EquivalentConnectedArea => EquivalentConnectedAreaByDistance.Count == 0 ? 0 : EquivalentConnectedAreaByDistance.Average();

    /// <summary>
    /// EC as a share of the habitat area (0–100): 100 when every patch reaches every other for sure.
    /// Unlike the bands, which rank patches within one run, it compares across schemes and projects.
    /// </summary>
    public double ConnectedPercent => HabitatAreaSquareMeters > 0 ? Math.Min(100d, EquivalentConnectedArea / HabitatAreaSquareMeters * 100d) : 0;

    /// <summary>"50 m: 1,234 m² · 250 m: 5,678 m²".</summary>
    public string DescribeByDistance() => string.Join(" · ", DispersalDistancesMeters.Select((distance, index) =>
        string.Create(CultureInfo.InvariantCulture, $"{distance:0.##} m: {EquivalentConnectedAreaByDistance.ElementAtOrDefault(index):N0} m²")));
}

/// <summary>
/// Scores connectivity per design scenario: which patches a scenario has (its design options,
/// and what stands after development — what stood before, for the existing site), and the saved
/// per-scenario results the Dashboard compares.
/// </summary>
public static class ConnectivityScenarios
{
    /// <summary>
    /// The scenarios to offer: the primary options and every saved scenario, plus the existing site
    /// when anything stood in the first phase. Items without design-option info (from a connector
    /// that only reports the built design) offer the primary options alone.
    /// </summary>
    public static IReadOnlyList<DesignScenario> Offered(
        IReadOnlyList<ConnectivityPatchItem> items,
        IReadOnlyList<DesignOptionSetItem> sets,
        IReadOnlyList<DesignScenario>? saved)
    {
        if (!HasOptions(items))
        {
            return [DesignScenarios.Primary(sets)];
        }

        var offered = DesignScenarios.WithPrimary(saved, sets).ToList();
        if (items.Any(item => item.PhaseRole is BngPhaseRoles.Retained or BngPhaseRoles.Lost))
        {
            offered.Add(DesignScenarios.ExistingSite(sets));
        }

        return offered;
    }

    /// <summary>True when the connector reported every design option, with each item's option and phase.</summary>
    public static bool HasOptions(IReadOnlyList<ConnectivityPatchItem> items) =>
        items.Count > 0 && items.All(item => item.DesignOption is not null);

    /// <summary>The items in the scenario: its design options, and what stands in it (see <see cref="SitePhases.Counts"/>).</summary>
    public static IReadOnlyList<ConnectivityPatchItem> InScenario(
        IReadOnlyList<ConnectivityPatchItem> items,
        DesignScenario scenario,
        IReadOnlyList<DesignOptionSetItem> sets) =>
        HasOptions(items)
            ? DesignScenarios.Filter(items, item => item.DesignOption!, scenario, sets)
                .Where(item => SitePhases.Counts(item.PhaseRole, scenario))
                .ToList()
            : items;

    public static ConnectivityScenarioResult Summarize(
        DesignScenario scenario,
        ConnectivitySettings settings,
        IReadOnlyList<ConnectivityPatch> patches,
        ConnectivityRunResult run,
        int designYear,
        DateTimeOffset now) =>
        new(
            scenario.Name,
            scenario.IsExistingSite,
            settings.DispersalDistancesMeters,
            settings.ProbabilityAtDistance,
            run.EquivalentConnectedAreaByDistance,
            patches.Sum(patch => Math.Max(0d, patch.AreaSquareMeters)),
            patches.Count,
            now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            designYear);

    /// <summary>The saved results with these in: each replaces the saved result of the same scenario, others stay.</summary>
    public static IReadOnlyList<ConnectivityScenarioResult> Save(
        IReadOnlyList<ConnectivityScenarioResult>? saved,
        IReadOnlyList<ConnectivityScenarioResult> fresh)
    {
        var names = fresh.Select(result => result.Scenario).ToHashSet(StringComparer.Ordinal);
        return (saved ?? []).Where(result => !names.Contains(result.Scenario)).Concat(fresh).ToList();
    }

    /// <summary>The saved result for the scenario, by name; null when it hasn't been calculated.</summary>
    public static ConnectivityScenarioResult? For(IReadOnlyList<ConnectivityScenarioResult>? saved, DesignScenario scenario) =>
        saved?.LastOrDefault(result => string.Equals(result.Scenario, scenario.Name, StringComparison.Ordinal));
}
