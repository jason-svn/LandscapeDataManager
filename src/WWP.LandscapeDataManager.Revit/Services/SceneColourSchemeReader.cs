using System.Globalization;
using Autodesk.Revit.DB;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// The model's views that colour trees or floors with filters, as the 3D view's "Colour by" schemes
/// (<see cref="SceneColourScheme"/>). Every view is read, not just those on sheets; views that colour
/// the same way count once, under the shortest name of those on sheets, if any. A view whose filters
/// colour no tree or floor is left out. Revit itself tests each tree and floor against a view's
/// filters (<see cref="ElementFilter.PassesFilter(Element)"/>), so the colours follow the view exactly
/// whatever its rules are; only the legend labels need to understand the rules.
/// </summary>
internal static class SceneColourSchemeReader
{
    private sealed record ColourFilter(ParameterFilterElement Filter, ElementFilter? Rules, string Colour, bool Trees, bool Floors);

    /// <summary>A filter that bounds one number: from <see cref="Min"/> to <see cref="Max"/>, Revit internal units.</summary>
    private sealed record Band(ElementId Parameter, double? Min, bool MinInclusive, double? Max, bool MaxInclusive);

    public static IReadOnlyList<SceneColourScheme> Read(Document document, IReadOnlyList<Element> trees, IReadOnlyList<Element> floors)
    {
        var plantingId = new ElementId(BuiltInCategory.OST_Planting);
        var floorsId = new ElementId(BuiltInCategory.OST_Floors);
        var onSheets = new FilteredElementCollector(document).OfClass(typeof(Viewport)).Cast<Viewport>().Select(viewport => viewport.ViewId.Value).ToHashSet();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var schemes = new List<SceneColourScheme>();
        // Views that colour the same way count once, under the name of one on a sheet if there is one,
        // the shortest ("O2" rather than "Habitat O2").
        foreach (var view in new FilteredElementCollector(document)
                     .OfClass(typeof(View))
                     .Cast<View>()
                     .Where(view => !view.IsTemplate && view.AreGraphicsOverridesAllowed())
                     .OrderBy(view => onSheets.Contains(view.Id.Value) ? 0 : 1)
                     .ThenBy(view => view.Name.Length)
                     .ThenBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var filters = ColourFilters(document, view, plantingId, floorsId);
            if (filters.Count > 0 && ToScheme(document, view.Name, filters, trees, floors) is { } scheme && seen.Add(Signature(scheme)))
            {
                schemes.Add(scheme);
            }
        }

        return schemes.OrderBy(scheme => scheme.ViewName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>What a scheme shows: its legend and which element takes which entry.</summary>
    private static string Signature(SceneColourScheme scheme) =>
        string.Join("|", scheme.Rules.Select(rule => $"{rule.Label}:{rule.Colour}:{rule.Trees}:{rule.Floors}")) + "#" +
        string.Join(";", scheme.Matches.OrderBy(match => match.Key, StringComparer.Ordinal).Select(match => $"{match.Key}={match.Value}"));

    /// <summary>The view's enabled, visible filters on trees or floors that set a colour, in the view's order.</summary>
    private static List<ColourFilter> ColourFilters(Document document, View view, ElementId plantingId, ElementId floorsId)
    {
        var filters = new List<ColourFilter>();
        ICollection<ElementId> ids;
        try
        {
            ids = view.GetOrderedFilters();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return filters;
        }

        foreach (var id in ids)
        {
            if (document.GetElement(id) is not ParameterFilterElement filter || !view.GetIsFilterEnabled(id) || !view.GetFilterVisibility(id))
            {
                continue;
            }

            var categories = filter.GetCategories();
            var forTrees = categories.Contains(plantingId);
            var forFloors = categories.Contains(floorsId);
            if ((forTrees || forFloors) && Colour(view.GetFilterOverrides(id)) is { } colour)
            {
                filters.Add(new ColourFilter(filter, filter.GetElementFilter(), colour, forTrees, forFloors));
            }
        }

        return filters;
    }

    /// <summary>Null when none of the view's filters colours a tree or floor.</summary>
    private static SceneColourScheme? ToScheme(Document document, string viewName, List<ColourFilter> filters, IReadOnlyList<Element> trees, IReadOnlyList<Element> floors)
    {
        // Revit applies a view's filters top first: an element takes the colour of the first one that passes it.
        var matches = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (elements, isTree) in new[] { (trees, true), (floors, false) })
        {
            foreach (var element in elements)
            {
                var index = filters.FindIndex(filter => (isTree ? filter.Trees : filter.Floors) && Passes(filter.Rules, element));
                if (index >= 0)
                {
                    matches[element.UniqueId] = index;
                }
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        // The number parameter every rule tests, if they all test the same one (e.g. bands of a score
        // plus "has no value" for the elements without one).
        var bands = filters.Select(filter => BandOf(filter.Rules)).ToList();
        var tested = filters.Select(filter => RulesOf(filter.Rules)?.Select(rule => rule.GetRuleParameter()).Distinct().ToList()).ToList();
        var parameterId = tested.All(ids => ids is { Count: 1 }) && tested.Select(ids => ids![0]).Distinct().Count() == 1 ? tested[0]![0] : null;
        var sample = parameterId is null
            ? null
            : trees.Concat(floors).Select(element => ParameterOf(element, parameterId)).FirstOrDefault(parameter => parameter is { StorageType: StorageType.Double or StorageType.Integer });
        if (sample is null)
        {
            parameterId = null;
        }

        var unit = sample is not null && UnitUtils.IsMeasurableSpec(sample.Definition.GetDataType())
            ? document.GetUnits().GetFormatOptions(sample.Definition.GetDataType()).GetUnitTypeId()
            : null;
        double Display(double value) => Math.Round(unit is null ? value : UnitUtils.ConvertFromInternalUnits(value, unit), 6);

        var rules = filters.Select((filter, index) =>
        {
            var band = bands[index];
            var min = band?.Min is { } low ? Display(low) : (double?)null;
            var max = band?.Max is { } high ? Display(high) : (double?)null;
            var label = band is not null ? BandLabel(min, band.MinInclusive, max, band.MaxInclusive) : TextLabel(filter.Rules) ?? filter.Filter.Name;
            return new SceneColourRule(label, filter.Filter.Name, filter.Colour, filter.Trees, filter.Floors, min, max);
        }).ToList();

        // Bands of one number read low to high in the legend (anything that isn't a band first); any
        // other scheme keeps the view's order.
        var order = Enumerable.Range(0, rules.Count).ToList();
        if (parameterId is not null)
        {
            order = order.OrderBy(index => rules[index].Min ?? double.NegativeInfinity).ThenBy(index => rules[index].Max ?? double.PositiveInfinity).ToList();
        }

        var position = new int[order.Count];
        for (var at = 0; at < order.Count; at++)
        {
            position[order[at]] = at;
        }

        Dictionary<string, double>? values = null;
        if (parameterId is not null)
        {
            values = new Dictionary<string, double>(StringComparer.Ordinal);
            var forTrees = filters.Any(filter => filter.Trees);
            var forFloors = filters.Any(filter => filter.Floors);
            foreach (var element in (forTrees ? trees : []).Concat(forFloors ? floors : []))
            {
                if (NumberOf(ParameterOf(element, parameterId)) is { } value)
                {
                    values[element.UniqueId] = Display(value);
                }
            }
        }

        return new SceneColourScheme(
            viewName,
            sample?.Definition.Name,
            sample is null ? null : (unit is null ? null : UnitLabel(document, sample)) ?? UnitFromNames(filters.Select(filter => filter.Filter.Name).ToList()),
            order.Select(index => rules[index]).ToList(),
            matches.ToDictionary(match => match.Key, match => position[match.Value], StringComparer.Ordinal),
            values);
    }

    private static bool Passes(ElementFilter? rules, Element element)
    {
        if (rules is null)
        {
            return true; // A filter without rules takes every element of its categories.
        }

        try
        {
            return rules.PassesFilter(element);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return false;
        }
    }

    /// <summary>The filter's colour: its surface pattern colour, else its line colour; null when it sets no colour.</summary>
    private static string? Colour(OverrideGraphicSettings overrides)
    {
        foreach (var colour in new[]
                 {
                     overrides.SurfaceForegroundPatternColor, overrides.SurfaceBackgroundPatternColor,
                     overrides.ProjectionLineColor, overrides.CutForegroundPatternColor
                 })
        {
            if (colour is { IsValid: true })
            {
                return $"#{colour.Red:x2}{colour.Green:x2}{colour.Blue:x2}";
            }
        }

        return null;
    }

    /// <summary>The filter's rules when they're all ANDed together; null for anything else (OR, inverted).</summary>
    private static List<FilterRule>? RulesOf(ElementFilter? filter)
    {
        switch (filter)
        {
            case ElementParameterFilter { Inverted: false } parameters:
                return parameters.GetRules().ToList();
            case LogicalAndFilter { Inverted: false } and:
                var rules = new List<FilterRule>();
                foreach (var inner in and.GetFilters())
                {
                    if (RulesOf(inner) is not { } innerRules)
                    {
                        return null;
                    }

                    rules.AddRange(innerRules);
                }

                return rules;
            default:
                return null;
        }
    }

    /// <summary>The band a filter keeps one number in (lower and/or upper bound), else null.</summary>
    private static Band? BandOf(ElementFilter? filter)
    {
        if (RulesOf(filter) is not { Count: > 0 } rules)
        {
            return null;
        }

        var parameter = rules[0].GetRuleParameter();
        double? min = null, max = null;
        bool minInclusive = false, maxInclusive = false;
        foreach (var rule in rules)
        {
            double value;
            switch (rule)
            {
                case FilterDoubleRule number when number.GetRuleParameter() == parameter:
                    value = number.RuleValue;
                    break;
                case FilterIntegerRule number when number.GetRuleParameter() == parameter:
                    value = number.RuleValue;
                    break;
                default:
                    return null;
            }

            switch (((FilterNumericValueRule)rule).GetEvaluator())
            {
                case FilterNumericGreater:
                    (min, minInclusive) = (value, false);
                    break;
                case FilterNumericGreaterOrEqual:
                    (min, minInclusive) = (value, true);
                    break;
                case FilterNumericLess:
                    (max, maxInclusive) = (value, false);
                    break;
                case FilterNumericLessOrEqual:
                    (max, maxInclusive) = (value, true);
                    break;
                case FilterNumericEquals:
                    (min, minInclusive, max, maxInclusive) = (value, true, value, true);
                    break;
                default:
                    return null;
            }
        }

        return new Band(parameter, min, minInclusive, max, maxInclusive);
    }

    /// <summary>"4–8", "≥ 24", "&lt; 4", "= 5".</summary>
    private static string BandLabel(double? min, bool minInclusive, double? max, bool maxInclusive) => (min, max) switch
    {
        ({ } low, { } high) when low == high => $"= {Format(low)}",
        ({ } low, { } high) => $"{Format(low)}–{Format(high)}",
        ({ } low, null) => $"{(minInclusive ? "≥" : ">")} {Format(low)}",
        (null, { } high) => $"{(maxInclusive ? "≤" : "<")} {Format(high)}",
        _ => "All"
    };

    /// <summary>The text a one-rule "equals" filter matches (e.g. a floor's Use), else null.</summary>
    private static string? TextLabel(ElementFilter? filter) =>
        RulesOf(filter) is [FilterStringRule rule] && rule.GetEvaluator() is FilterStringEquals && !string.IsNullOrWhiteSpace(rule.RuleString)
            ? rule.RuleString
            : null;

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The project's unit symbol for the parameter ("kg", "m³"), with "/yr" for an annual figure.</summary>
    private static string? UnitLabel(Document document, Parameter parameter)
    {
        var options = document.GetUnits().GetFormatOptions(parameter.Definition.GetDataType());
        var symbol = options.GetSymbolTypeId();
        if (symbol.Empty())
        {
            symbol = FormatOptions.GetValidSymbols(options.GetUnitTypeId()).FirstOrDefault(valid => !valid.Empty()) ?? symbol;
        }

        if (symbol.Empty())
        {
            return null;
        }

        var label = LabelUtils.GetLabelForSymbol(symbol);
        return parameter.Definition.Name.Contains("Annual", StringComparison.OrdinalIgnoreCase) ? $"{label}/yr" : label;
    }

    /// <summary>
    /// For a plain number parameter (no Revit unit), the unit the filters' names end with:
    /// "WWP_Floor CO2e 0-0.3 kg per m2 yr" and its siblings give "kg/m²/yr". Null when the names
    /// don't share a unit-like ending.
    /// </summary>
    private static string? UnitFromNames(IReadOnlyList<string> names)
    {
        if (names.Count < 2)
        {
            return null;
        }

        var suffix = names.Aggregate((common, name) =>
        {
            var length = 0;
            while (length < common.Length && length < name.Length && common[^(length + 1)] == name[^(length + 1)])
            {
                length++;
            }

            return common[^length..];
        });
        var start = suffix.IndexOf(' ');
        var words = (start < 0 ? string.Empty : suffix[(start + 1)..]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 4 || !char.IsLetter(words[0][0]))
        {
            return null;
        }

        static string Unit(string word) => word.Replace("m2", "m²", StringComparison.Ordinal).Replace("m3", "m³", StringComparison.Ordinal);
        return Unit(words[0]) + string.Concat(words.Skip(1).Where(word => !word.Equals("per", StringComparison.OrdinalIgnoreCase)).Select(word => "/" + Unit(word)));
    }

    /// <summary>
    /// The parameter a filter rule tests, on the element or else its type (as Revit's filters look):
    /// the instance's when it has a value — a parameter bound to both can be empty on the instance.
    /// </summary>
    private static Parameter? ParameterOf(Element element, ElementId parameterId)
    {
        var onInstance = Find(element, parameterId);
        if (onInstance is { HasValue: true })
        {
            return onInstance;
        }

        return (element.Document.GetElement(element.GetTypeId()) is ElementType type ? Find(type, parameterId) : null) ?? onInstance;
    }

    private static Parameter? Find(Element element, ElementId parameterId)
    {
        if (ParameterUtils.IsBuiltInParameter(parameterId))
        {
            return element.get_Parameter((BuiltInParameter)(int)parameterId.Value);
        }

        return element.Document.GetElement(parameterId) switch
        {
            SharedParameterElement shared => element.get_Parameter(shared.GuidValue),
            ParameterElement project => element.get_Parameter(project.GetDefinition()),
            _ => null
        };
    }

    private static double? NumberOf(Parameter? parameter) => parameter switch
    {
        { HasValue: true, StorageType: StorageType.Double } => parameter.AsDouble(),
        { HasValue: true, StorageType: StorageType.Integer } => parameter.AsInteger(),
        _ => null
    };
}
