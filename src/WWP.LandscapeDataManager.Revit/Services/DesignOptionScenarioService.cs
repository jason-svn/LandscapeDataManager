using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// The model's design option sets as Revit lists them — options with nothing modelled in them yet
/// included — and the scenario a view shows. Revit's API doesn't expose a view's Visibility/Graphics
/// design option settings, so the view's scenario is read from what it shows: a view collector only
/// returns elements visible in that view, so the option of each set that has elements visible is the
/// one the view displays. A set with nothing visible falls back to its primary option.
/// </summary>
internal static class DesignOptionScenarioService
{
    public static IReadOnlyList<DesignOptionSetItem> GetSets(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(DesignOption))
            .Cast<DesignOption>()
            .Select(option => (SetName: SetNameOf(document, option), Option: option))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.SetName))
            .GroupBy(pair => pair.SetName!, StringComparer.Ordinal)
            .Select(set => new DesignOptionSetItem(
                set.Key,
                set.Select(pair => new DesignOptionItem(CleanOptionName(pair.Option.Name), pair.Option.IsPrimary))
                    .OrderByDescending(option => option.IsPrimary)
                    .ThenBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()))
            .OrderBy(set => set.SetName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static ViewDesignScenarioResult GetViewScenario(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project first.");
        var view = document.ActiveView
                   ?? throw new InvalidOperationException("There's no active view.");
        return GetViewScenario(document, view);
    }

    public static ViewDesignScenarioResult GetViewScenario(Document document, View view)
    {
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        var notes = new List<string>();
        var options = new FilteredElementCollector(document).OfClass(typeof(DesignOption)).Cast<DesignOption>().ToList();
        foreach (var set in options.GroupBy(option => SetNameOf(document, option)).Where(set => !string.IsNullOrWhiteSpace(set.Key)))
        {
            var visible = set
                .Where(option => new FilteredElementCollector(document, view.Id)
                    .WherePasses(new ElementDesignOptionFilter(option.Id))
                    .WhereElementIsNotElementType()
                    .FirstElementId() != ElementId.InvalidElementId)
                .ToList();
            var primary = set.FirstOrDefault(option => option.IsPrimary) ?? set.First();
            var chosen = visible.Count switch
            {
                0 => primary,
                1 => visible[0],
                _ => visible.FirstOrDefault(option => option.IsPrimary) ?? visible[0]
            };

            if (visible.Count == 0)
            {
                notes.Add($"{set.Key}: nothing visible in this view, so its primary option ({CleanOptionName(primary.Name)}) is used.");
            }
            else if (visible.Count > 1)
            {
                notes.Add($"{set.Key}: this view shows more than one option; {CleanOptionName(chosen.Name)} is used.");
            }

            choices[set.Key!] = CleanOptionName(chosen.Name);
        }

        return new ViewDesignScenarioResult(document.Title, view.Name, choices, notes);
    }

    /// <summary>Revit appends " &lt;primary&gt;" to the primary option's name — the same rule as <c>DesignScenarios.CleanOptionName</c>.</summary>
    internal static string CleanOptionName(string? name) =>
        (name ?? string.Empty).Replace("<primary>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

    /// <summary>DesignOptionSet isn't a public API type, so the set's name is read through its element.</summary>
    private static string? SetNameOf(Document document, DesignOption option) =>
        option.get_Parameter(BuiltInParameter.OPTION_SET_ID) is { HasValue: true } setParameter
            ? document.GetElement(setParameter.AsElementId())?.Name?.Trim()
            : null;
}
