using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// The connectivity companion to the hand-made benefit views (Avoided_Water, Pollution, …): an
/// isometric 3D view in flat colours where every tree and planting area is coloured by its
/// connectivity score through view filters, ten steps from cool blue to red (see
/// <see cref="ConnectivityHeatmap"/>). Elements without a score are light grey and growth-year copies
/// are hidden: connectivity is worked out on the trees as drawn. Running it again updates the
/// filters and the view in place instead of adding new ones.
/// </summary>
internal static class ConnectivityHeatmapViewService
{
    private const string ScoreParameter = "!_S_PLT_ConnectivityResult_Score_Number";
    private static readonly Color NotAssessedColour = new(0xD9, 0xD9, 0xD9);
    private static readonly BuiltInCategory[] Categories = [BuiltInCategory.OST_Planting, BuiltInCategory.OST_Floors];

    public static ConnectivityHeatmapViewResult Ensure(UIApplication application)
    {
        var uiDocument = application.ActiveUIDocument
                         ?? throw new InvalidOperationException("Open a Revit project before making the connectivity view.");
        var document = uiDocument.Document;
        var scoreId = SharedParameterId(document, ScoreParameter)
                      ?? throw new InvalidOperationException("The connectivity parameters aren't set up in this project: run Import Shared Parameter from the LIM ribbon first.");
        var copiesId = SharedParameterId(document, GrowthProjectionService.GeneratedByParameter);
        var categoryIds = Categories.Select(category => new ElementId(category)).ToList();

        View3D view;
        var created = false;
        var filtersCreated = 0;
        using (var transaction = new Transaction(document, "LIM Connectivity Heatmap View"))
        {
            transaction.Start();

            // Highest priority first: the view applies the first matching filter in its list.
            var filters = new List<(ElementId Id, Color? Colour, bool Visible)>();
            if (copiesId is not null)
            {
                filters.Add((EnsureFilter(document, ConnectivityHeatmap.GrowthCopiesFilterName, [new ElementId(BuiltInCategory.OST_Planting)],
                    [ParameterFilterRuleFactory.CreateHasValueParameterRule(copiesId)], ref filtersCreated), null, false));
            }

            filters.Add((EnsureFilter(document, ConnectivityHeatmap.NotAssessedFilterName, categoryIds,
                [ParameterFilterRuleFactory.CreateHasNoValueParameterRule(scoreId)], ref filtersCreated), NotAssessedColour, true));
            foreach (var step in ConnectivityHeatmap.Steps)
            {
                var rules = new List<FilterRule> { ParameterFilterRuleFactory.CreateGreaterOrEqualRule(scoreId, step.From, 1e-6) };
                if (!step.IsTop)
                {
                    rules.Add(ParameterFilterRuleFactory.CreateLessRule(scoreId, step.To, 1e-6));
                }

                filters.Add((EnsureFilter(document, step.FilterName, categoryIds, rules, ref filtersCreated), new Color(step.Red, step.Green, step.Blue), true));
            }

            var existingView = FindView(document);
            created = existingView is null;
            view = existingView ?? CreateView(document);
            var solidFill = SolidFillPatternId(document);
            var inView = view.GetFilters().ToHashSet();
            foreach (var (id, colour, visible) in filters)
            {
                if (!inView.Contains(id))
                {
                    view.AddFilter(id);
                }

                var overrides = new OverrideGraphicSettings();
                if (colour is not null && solidFill is not null)
                {
                    overrides.SetSurfaceForegroundPatternId(solidFill);
                    overrides.SetSurfaceForegroundPatternColor(colour);
                    overrides.SetCutForegroundPatternId(solidFill);
                    overrides.SetCutForegroundPatternColor(colour);
                }

                view.SetFilterOverrides(id, overrides);
                view.SetFilterVisibility(id, visible);
            }

            transaction.Commit();
        }

        uiDocument.RequestViewChange(view);

        var (scored, notAssessed) = CountScores(document);
        return new ConnectivityHeatmapViewResult(document.Title, view.Name, created, filtersCreated, scored, notAssessed);
    }

    private static ElementId EnsureFilter(Document document, string name, IList<ElementId> categories, IList<FilterRule> rules, ref int created)
    {
        var filter = new ElementParameterFilter(rules);
        var existing = new FilteredElementCollector(document)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .FirstOrDefault(element => element.Name == name);
        if (existing is not null)
        {
            existing.SetCategories(categories);
            existing.SetElementFilter(filter);
            return existing.Id;
        }

        created++;
        return ParameterFilterElement.Create(document, name, categories, filter).Id;
    }

    private static View3D? FindView(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate && view.Name == ConnectivityHeatmap.ViewName);

    /// <summary>Set up like the benefit views: isometric, flat colours, fine detail, the finished site (last phase, nothing demolished).</summary>
    private static View3D CreateView(Document document)
    {
        var viewType = new FilteredElementCollector(document)
                           .OfClass(typeof(ViewFamilyType))
                           .Cast<ViewFamilyType>()
                           .FirstOrDefault(type => type.ViewFamily == ViewFamily.ThreeDimensional)
                       ?? throw new InvalidOperationException("This project has no 3D view type to make the connectivity view from.");
        var view = View3D.CreateIsometric(document, viewType.Id);
        view.Name = ConnectivityHeatmap.ViewName;
        view.DisplayStyle = DisplayStyle.FlatColors;
        view.DetailLevel = ViewDetailLevel.Fine;
        view.Scale = 2000;

        if (document.Phases.Size > 0 && view.get_Parameter(BuiltInParameter.VIEW_PHASE) is { IsReadOnly: false } phase)
        {
            phase.Set(document.Phases.get_Item(document.Phases.Size - 1).Id);
        }

        var showComplete = new FilteredElementCollector(document)
            .OfClass(typeof(PhaseFilter))
            .FirstOrDefault(filter => filter.Name == "Show Complete");
        if (showComplete is not null && view.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER) is { IsReadOnly: false } phaseFilter)
        {
            phaseFilter.Set(showComplete.Id);
        }

        return view;
    }

    private static ElementId? SharedParameterId(Document document, string name) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(SharedParameterElement))
            .FirstOrDefault(element => element.Name == name)?.Id;

    private static ElementId? SolidFillPatternId(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(pattern => pattern.GetFillPattern().IsSolidFill)?.Id;

    private static (int Scored, int NotAssessed) CountScores(Document document)
    {
        var scored = 0;
        var notAssessed = 0;
        foreach (var category in Categories)
        {
            foreach (var element in new FilteredElementCollector(document).OfCategory(category).WhereElementIsNotElementType())
            {
                if (GrowthProjectionService.LinkOf(element) is not null)
                {
                    continue;
                }

                if (element.LookupParameter(ScoreParameter) is { HasValue: true })
                {
                    scored++;
                }
                else
                {
                    notAssessed++;
                }
            }
        }

        return (scored, notAssessed);
    }
}
