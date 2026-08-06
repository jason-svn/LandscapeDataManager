using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// The QC report's row actions: select/zoom/isolate reported elements, and apply/reset a fixed
/// status-to-colour override table in the active view. Every method resolves elements by
/// <see cref="Element.UniqueId"/> — the same stable identifier used everywhere else in this
/// add-in — never by name.
/// </summary>
internal static class RevitViewInteractionService
{
    private static readonly Dictionary<string, Color> StatusColours = new(StringComparer.Ordinal)
    {
        ["Ready"] = new Color(0, 102, 204),
        ["MissingInput"] = new Color(128, 128, 128),
        ["InvalidInput"] = new Color(230, 126, 34),
        ["Calculated"] = new Color(34, 139, 34),
        ["APIWarning"] = new Color(230, 180, 30),
        ["APIError"] = new Color(196, 38, 46),
        ["Stale"] = new Color(142, 68, 173),
        ["Failed"] = new Color(255, 0, 0),
        ["Success"] = new Color(34, 139, 34),
        ["NeedsAttention"] = new Color(196, 38, 46)
    };

    public static OperationResult Select(UIApplication application, ElementSelectionRequest request)
    {
        var uiDocument = GetUiDocument(application);
        var ids = ResolveIds(uiDocument.Document, request.UniqueIds);
        uiDocument.Selection.SetElementIds(ids);
        return new OperationResult(true, $"Selected {ids.Count:N0} element(s).");
    }

    public static OperationResult Zoom(UIApplication application, ElementSelectionRequest request)
    {
        var uiDocument = GetUiDocument(application);
        var ids = ResolveIds(uiDocument.Document, request.UniqueIds);
        uiDocument.Selection.SetElementIds(ids);
        uiDocument.ShowElements(ids);
        return new OperationResult(true, $"Zoomed to {ids.Count:N0} element(s).");
    }

    public static OperationResult Isolate(UIApplication application, ElementSelectionRequest request)
    {
        var uiDocument = GetUiDocument(application);
        var ids = ResolveIds(uiDocument.Document, request.UniqueIds);
        if (ids.Count == 0)
        {
            return new OperationResult(false, "No matching elements to isolate.");
        }

        using var transaction = new Transaction(uiDocument.Document, "LIM Isolate Reported Elements");
        transaction.Start();
        uiDocument.ActiveView.IsolateElementsTemporary(ids);
        transaction.Commit();
        return new OperationResult(true, $"Isolated {ids.Count:N0} element(s) in the active view.");
    }

    public static OperationResult ResetIsolation(UIApplication application)
    {
        var uiDocument = GetUiDocument(application);
        using var transaction = new Transaction(uiDocument.Document, "LIM Reset Temporary View Mode");
        transaction.Start();
        uiDocument.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        transaction.Commit();
        return new OperationResult(true, "Temporary isolate/hide was reset.");
    }

    public static OperationResult ApplyColourOverrides(UIApplication application, StatusColourOverrideRequest request)
    {
        var uiDocument = GetUiDocument(application);
        var document = uiDocument.Document;
        var view = uiDocument.ActiveView;
        var solidFillPatternId = new FilteredElementCollector(document)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(pattern => pattern.GetFillPattern().IsSolidFill)?.Id;

        var applied = 0;
        using var transaction = new Transaction(document, "LIM Apply Status Colour Overrides");
        transaction.Start();
        foreach (var (uniqueId, status) in request.StatusByUniqueId)
        {
            var element = document.GetElement(uniqueId);
            if (element is null || !StatusColours.TryGetValue(status, out var color))
            {
                continue;
            }

            var overrides = new OverrideGraphicSettings().SetProjectionLineColor(color);
            if (solidFillPatternId is not null)
            {
                overrides = overrides
                    .SetSurfaceForegroundPatternColor(color)
                    .SetSurfaceForegroundPatternId(solidFillPatternId);
            }

            view.SetElementOverrides(element.Id, overrides);
            applied++;
        }

        transaction.Commit();
        return new OperationResult(true, $"Applied colour overrides to {applied:N0} element(s).");
    }

    public static OperationResult ResetColourOverrides(UIApplication application, ElementSelectionRequest request)
    {
        var uiDocument = GetUiDocument(application);
        var document = uiDocument.Document;
        var view = uiDocument.ActiveView;
        var ids = ResolveIds(document, request.UniqueIds);

        using var transaction = new Transaction(document, "LIM Reset Status Colour Overrides");
        transaction.Start();
        foreach (var id in ids)
        {
            view.SetElementOverrides(id, new OverrideGraphicSettings());
        }

        transaction.Commit();
        return new OperationResult(true, $"Reset colour overrides on {ids.Count:N0} element(s).");
    }

    private static IList<ElementId> ResolveIds(Document document, IReadOnlyList<string> uniqueIds) =>
        uniqueIds
            .Select(document.GetElement)
            .Where(element => element is not null)
            .Select(element => element!.Id)
            .ToList();

    private static UIDocument GetUiDocument(UIApplication application) =>
        application.ActiveUIDocument
        ?? throw new InvalidOperationException("Open a Revit project before performing this action.");
}
