using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Design option scenarios: one chosen option per design option set, the way Revit shows a model
/// with those options visible. Main-model elements (in no option) are in every scenario, an element
/// in a chosen option is in, and an element in any other option is out. A set the scenario doesn't
/// mention, or whose chosen option no longer exists, falls back to the set's primary option.
/// </summary>
public static class DesignScenarios
{
    public const string PrimaryScenarioName = "Primary options";

    /// <summary>Revit appends " &lt;primary&gt;" to the primary option's name; scenarios store the plain name.</summary>
    public static string CleanOptionName(string? name) =>
        (name ?? string.Empty).Replace("<primary>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

    /// <summary>In no design option: Revit shows main-model elements whichever options are visible.</summary>
    public static bool IsMainModel(DesignOptionInfo option) =>
        string.IsNullOrWhiteSpace(option.SetName) && string.IsNullOrWhiteSpace(option.OptionName);

    /// <summary>
    /// The sets to choose from. Revit's own list wins, since it includes options with nothing modelled
    /// in them yet; without one (an older connector, a cached snapshot) the sets are rebuilt from the
    /// options the elements sit in.
    /// </summary>
    public static IReadOnlyList<DesignOptionSetItem> ResolveSets(
        IReadOnlyList<DesignOptionSetItem>? fromRevit,
        IEnumerable<DesignOptionInfo> elementOptions)
    {
        if (fromRevit is { Count: > 0 })
        {
            return fromRevit;
        }

        return elementOptions
            .Where(option => !IsMainModel(option) && !string.IsNullOrWhiteSpace(option.SetName))
            .GroupBy(option => option.SetName!.Trim(), StringComparer.Ordinal)
            .Select(set => new DesignOptionSetItem(
                set.Key,
                set.GroupBy(option => CleanOptionName(option.OptionName), StringComparer.Ordinal)
                    .Select(group => new DesignOptionItem(group.Key, group.Any(option => option.IsPrimary)))
                    .OrderByDescending(option => option.IsPrimary)
                    .ThenBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()))
            .OrderBy(set => set.SetName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The scenario Revit shows by default: every set's primary option.</summary>
    public static DesignScenario Primary(IReadOnlyList<DesignOptionSetItem> sets) =>
        new(PrimaryScenarioName, sets.ToDictionary(set => set.SetName, PrimaryOptionOf, StringComparer.Ordinal));

    /// <summary>The set's primary option, else its first; empty for a set with no options at all.</summary>
    public static string PrimaryOptionOf(DesignOptionSetItem set) =>
        (set.Options.FirstOrDefault(option => option.IsPrimary) ?? set.Options.FirstOrDefault())?.Name ?? string.Empty;

    /// <summary>The option the scenario shows for <paramref name="set"/>: its choice while that option still exists, else the primary.</summary>
    public static string ChosenOption(DesignScenario scenario, DesignOptionSetItem set) =>
        scenario.Choices.TryGetValue(set.SetName, out var chosen) &&
        set.Options.Any(option => string.Equals(option.Name, chosen, StringComparison.Ordinal))
            ? chosen
            : PrimaryOptionOf(set);

    /// <summary>The same scenario with one valid choice for every set, and nothing for sets that no longer exist.</summary>
    public static DesignScenario Normalize(DesignScenario scenario, IReadOnlyList<DesignOptionSetItem> sets) =>
        scenario with { Choices = sets.ToDictionary(set => set.SetName, set => ChosenOption(scenario, set), StringComparer.Ordinal) };

    /// <summary>Choices the scenario names that don't exist any more — a set or option renamed or deleted since it was saved.</summary>
    public static IReadOnlyList<string> MissingChoices(DesignScenario scenario, IReadOnlyList<DesignOptionSetItem> sets) =>
        scenario.Choices
            .Where(choice => sets.FirstOrDefault(set => set.SetName == choice.Key) is not { } set ||
                             set.Options.All(option => option.Name != choice.Value))
            .Select(choice => $"{choice.Key}: {choice.Value}")
            .ToList();

    public static bool Includes(DesignScenario scenario, DesignOptionInfo option, IReadOnlyList<DesignOptionSetItem> sets)
    {
        if (IsMainModel(option))
        {
            return true;
        }

        var setName = option.SetName?.Trim() ?? string.Empty;
        var optionName = CleanOptionName(option.OptionName);
        if (sets.FirstOrDefault(set => set.SetName == setName) is { } knownSet)
        {
            return string.Equals(optionName, ChosenOption(scenario, knownSet), StringComparison.Ordinal);
        }

        // A set the list doesn't know: the scenario's own choice if it has one, else Revit's primary.
        return scenario.Choices.TryGetValue(setName, out var chosen)
            ? string.Equals(optionName, chosen, StringComparison.Ordinal)
            : option.IsPrimary;
    }

    /// <summary>
    /// The site before development: the primary options' elements that stood in the first phase
    /// (see <see cref="SitePhases"/>). Existing conditions belong in the main model, or identically
    /// in every option of a set, so the primary options give the same site as any other.
    /// </summary>
    public static DesignScenario ExistingSite(IReadOnlyList<DesignOptionSetItem> sets) =>
        Primary(sets) with { Name = SitePhases.ExistingSiteName, IsExistingSite = true };

    public static List<T> Filter<T>(IEnumerable<T> items, Func<T, DesignOptionInfo> optionOf, DesignScenario scenario, IReadOnlyList<DesignOptionSetItem> sets) =>
        items.Where(item => Includes(scenario, optionOf(item), sets)).ToList();

    /// <summary>"Variante 1: Baseline + Test: Option 1"; "Main model" when the model has no design options.</summary>
    public static string Describe(DesignScenario scenario, IReadOnlyList<DesignOptionSetItem> sets) =>
        scenario.IsExistingSite
            ? "the site before development (first phase)"
            : sets.Count == 0
                ? "Main model"
                : string.Join(" + ", sets.Select(set => $"{set.SetName}: {ChosenOption(scenario, set)}"));

    /// <summary>"Scheme A = Variante 1: Baseline + Test: Option 1".</summary>
    public static string Label(DesignScenario scenario, IReadOnlyList<DesignOptionSetItem> sets) =>
        sets.Count == 0 || scenario.IsExistingSite ? scenario.Name : $"{scenario.Name} = {Describe(scenario, sets)}";

    public static bool SameChoices(DesignScenario first, DesignScenario second, IReadOnlyList<DesignOptionSetItem> sets) =>
        sets.All(set => ChosenOption(first, set) == ChosenOption(second, set));

    /// <summary>
    /// The scenarios to offer: the primary one first (unless a saved scenario already picks every
    /// primary), then the saved ones in their saved order. Saved scenarios with a blank name are skipped.
    /// </summary>
    public static IReadOnlyList<DesignScenario> WithPrimary(IReadOnlyList<DesignScenario>? saved, IReadOnlyList<DesignOptionSetItem> sets)
    {
        var primary = Primary(sets);
        var named = (saved ?? [])
            .Where(scenario => !string.IsNullOrWhiteSpace(scenario.Name))
            .GroupBy(scenario => scenario.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return named.Any(scenario => SameChoices(scenario, primary, sets))
            ? named
            : [primary, .. named];
    }

    /// <summary>
    /// The scenario <paramref name="name"/> names among <paramref name="scenarios"/>, else the one
    /// named "Baseline" (the team's convention), else the first.
    /// </summary>
    public static DesignScenario? Find(IReadOnlyList<DesignScenario> scenarios, string? name) =>
        scenarios.FirstOrDefault(scenario => string.Equals(scenario.Name, name, StringComparison.CurrentCultureIgnoreCase))
        ?? scenarios.FirstOrDefault(scenario => !scenario.IsExistingSite && scenario.Name.Contains("baseline", StringComparison.OrdinalIgnoreCase))
        ?? scenarios.FirstOrDefault(scenario => !scenario.IsExistingSite)
        ?? scenarios.FirstOrDefault();

    /// <summary>Adds <paramref name="scenario"/>, or replaces the saved one with the same name.</summary>
    public static IReadOnlyList<DesignScenario> Upsert(IReadOnlyList<DesignScenario>? saved, DesignScenario scenario)
    {
        var list = (saved ?? []).ToList();
        var index = list.FindIndex(existing => string.Equals(existing.Name, scenario.Name, StringComparison.CurrentCultureIgnoreCase));
        if (index >= 0)
        {
            list[index] = scenario;
        }
        else
        {
            list.Add(scenario);
        }

        return list;
    }

    public static IReadOnlyList<DesignScenario> Remove(IReadOnlyList<DesignScenario>? saved, string name) =>
        (saved ?? []).Where(scenario => !string.Equals(scenario.Name, name, StringComparison.CurrentCultureIgnoreCase)).ToList();
}
