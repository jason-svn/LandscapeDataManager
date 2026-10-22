using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Connectivity tool: reports every tree and floor as a plan-geometry patch, and writes back the
/// 1–4 connectivity band the tool calculated. All of the network maths happens client-side
/// (<c>ConnectivityCalculator</c>) — this service only reads geometry and writes finished values.
/// By default only the built design counts (elements in a non-primary Design Option, or demolished,
/// are left out); asked for every design option, each element carries its option and phase role so
/// the tool can score any design scenario.
/// </summary>
internal static class ConnectivityService
{
    private const string CanopyWidthParameter = "!_S_PLT_TreeFoliage_Width";
    private const string LdsTypeParameter = "!_S_PLT_LDS_Type_Text";
    private const string SurfaceClassParameter = "!_S_PLT_LDS_SurfaceClass_Text";

    internal const string LockedParameter = "!_S_PLT_ConnectivityInput_Locked_YesNo";
    private const string BandParameter = "!_S_PLT_ConnectivityResult_Band_Number";
    private const string ClassParameter = "!_S_PLT_ConnectivityResult_Class_Text";
    private const string ScoreParameter = "!_S_PLT_ConnectivityResult_Score_Number";
    private const string DpcParameter = "!_S_PLT_ConnectivityResult_dPC_Number";
    private const string DetailsParameter = "!_S_PLT_ConnectivityResult_Details_Text";
    private const string LastUpdatedParameter = "!_S_PLT_ConnectivityResult_LastUpdated_Text";

    public static ConnectivityPatchesResult GetPatches(UIApplication application, ConnectivityPatchesRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before calculating connectivity.");

        var items = new List<ConnectivityPatchItem>();
        foreach (var element in Collect(document, BuiltInCategory.OST_Planting, request.AllDesignOptions))
        {
            if (ToTreeItem(document, element) is { } item)
            {
                items.Add(item);
            }
        }

        if (request.IncludeFloors)
        {
            foreach (var element in Collect(document, BuiltInCategory.OST_Floors, request.AllDesignOptions))
            {
                if (element is Floor floor && ToFloorItem(document, floor) is { } item)
                {
                    items.Add(item);
                }
            }
        }

        return new ConnectivityPatchesResult(document.Title, items, DesignOptionScenarioService.GetSets(document));
    }

    public static WriteConnectivityResultsResult Write(UIApplication application, WriteConnectivityResultsRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before writing connectivity results.");

        var written = new List<string>();
        var skippedLocked = new List<string>();
        var failures = new List<ConnectivityWriteFailure>();
        var lastUpdated = FormatLocalTimestamp();

        using var transaction = new Transaction(document, "LIM Write Connectivity Scores");
        transaction.Start();
        try
        {
            foreach (var values in request.Items)
            {
                if (document.GetElement(values.UniqueId) is not { } element)
                {
                    failures.Add(new ConnectivityWriteFailure(values.UniqueId, "The element no longer exists in the model."));
                    continue;
                }

                if (element.LookupParameter(BandParameter) is null)
                {
                    failures.Add(new ConnectivityWriteFailure(values.UniqueId, "The connectivity parameters aren't set up in this project — run Import Shared Parameter from the LIM ribbon first."));
                    continue;
                }

                if (IsLocked(element))
                {
                    skippedLocked.Add(values.UniqueId);
                    continue;
                }

                var readOnly = new List<string>();
                SetNumber(element, BandParameter, values.Band, readOnly);
                SetText(element, ClassParameter, values.ClassName, readOnly);
                SetNumber(element, ScoreParameter, values.Score, readOnly);
                SetNumber(element, DpcParameter, values.DpcPercent, readOnly);
                SetText(element, DetailsParameter, values.Details, readOnly);
                SetText(element, LastUpdatedParameter, lastUpdated, readOnly);
                if (readOnly.Count > 0)
                {
                    failures.Add(new ConnectivityWriteFailure(values.UniqueId, $"Could not write {string.Join(", ", readOnly)} (read-only — is the element in a group, or its value driven by a formula?)."));
                    continue;
                }

                written.Add(values.UniqueId);
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        return new WriteConnectivityResultsResult(document.Title, written, skippedLocked, failures, lastUpdated);
    }

    /// <summary>The built design (main model and primary options, not demolished), or with <paramref name="allDesignOptions"/> everything: the tool then picks each scenario's elements itself.</summary>
    private static IEnumerable<Element> Collect(Document document, BuiltInCategory category, bool allDesignOptions) =>
        new FilteredElementCollector(document)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .Where(element => allDesignOptions || element.DesignOption is null || element.DesignOption.IsPrimary)
            .Where(element => allDesignOptions || element.DemolishedPhaseId == ElementId.InvalidElementId);

    private static ConnectivityPatchItem? ToTreeItem(Document document, Element element)
    {
        if (element.Location is not LocationPoint location)
        {
            return null;
        }

        string? note = null;
        var radiusFeet = ElementParameters.LookupOnInstanceOrType(element, CanopyWidthParameter) is { HasValue: true, StorageType: StorageType.Double } width
            ? width.AsDouble() / 2d
            : 0d;
        if (radiusFeet <= 0 && element.get_BoundingBox(null) is { } box)
        {
            radiusFeet = ((box.Max.X - box.Min.X) + (box.Max.Y - box.Min.Y)) / 4d;
            note = "Canopy width not set — used the family's plan size.";
        }

        var radius = ToMeters(radiusFeet);
        if (radius <= 0)
        {
            radius = 0.5;
            note = "Canopy width not set and the family has no plan size — assumed a 1 m canopy.";
        }

        var (familyName, typeName) = GetNames(document, element);
        return new ConnectivityPatchItem(
            element.UniqueId,
            element.Id.Value,
            "Planting",
            familyName,
            typeName,
            Math.PI * radius * radius,
            ToPoint(location.Point),
            radius,
            null,
            null,
            null,
            IsLocked(element),
            element.LookupParameter(BandParameter) is not null,
            GetStoredBand(element),
            note,
            DashboardReportService.GetDesignOptionInfo(document, element),
            BngFloorService.GetPhaseRole(document, element).Role);
    }

    private static ConnectivityPatchItem? ToFloorItem(Document document, Floor floor)
    {
        var outline = GetTopOutline(floor);
        if (outline.Count < 3)
        {
            return null;
        }

        var area = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED) is { HasValue: true } areaParameter
            ? UnitUtils.ConvertFromInternalUnits(areaParameter.AsDouble(), UnitTypeId.SquareMeters)
            : 0d;
        var center = new ConnectivityPoint(outline.Average(point => point.X), outline.Average(point => point.Y));
        var (familyName, typeName) = GetNames(document, floor);

        return new ConnectivityPatchItem(
            floor.UniqueId,
            floor.Id.Value,
            "Floor",
            familyName,
            typeName,
            area,
            center,
            0,
            outline,
            GetText(floor, LdsTypeParameter),
            GetText(floor, SurfaceClassParameter),
            IsLocked(floor),
            floor.LookupParameter(BandParameter) is not null,
            GetStoredBand(floor),
            null,
            DashboardReportService.GetDesignOptionInfo(document, floor),
            BngFloorService.GetPhaseRole(document, floor).Role);
    }

    /// <summary>The outer boundary of the floor's largest top face, flattened to plan (holes ignored).</summary>
    private static List<ConnectivityPoint> GetTopOutline(Floor floor)
    {
        var best = new List<ConnectivityPoint>();
        var bestArea = 0d;
        foreach (var reference in HostObjectUtils.GetTopFaces(floor))
        {
            if (floor.GetGeometryObjectFromReference(reference) is not Face face)
            {
                continue;
            }

            foreach (var loop in face.GetEdgesAsCurveLoops())
            {
                // Each curve's last tessellation point is the next curve's first — drop it.
                var points = loop
                    .SelectMany(curve =>
                    {
                        var tessellated = curve.Tessellate();
                        return tessellated.Take(tessellated.Count - 1);
                    })
                    .Select(ToPoint)
                    .ToList();
                var area = Math.Abs(SignedArea(points));
                if (area > bestArea)
                {
                    bestArea = area;
                    best = points;
                }
            }
        }

        return best;
    }

    private static double SignedArea(IReadOnlyList<ConnectivityPoint> points)
    {
        var sum = 0d;
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            sum += current.X * next.Y - next.X * current.Y;
        }

        return sum / 2d;
    }

    private static (string FamilyName, string TypeName) GetNames(Document document, Element element)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var familyName = elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty;
        return (familyName, elementType?.Name ?? element.Name);
    }

    private static ConnectivityPoint ToPoint(XYZ point) => new(ToMeters(point.X), ToMeters(point.Y));

    private static double ToMeters(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);

    private static string? GetText(Element element, string name)
    {
        var value = ElementParameters.LookupOnInstanceOrType(element, name)?.AsString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsLocked(Element element) =>
        element.LookupParameter(LockedParameter) is { HasValue: true, StorageType: StorageType.Integer } flag && flag.AsInteger() == 1;

    internal static int? GetStoredBand(Element element) =>
        element.LookupParameter(BandParameter) is { HasValue: true, StorageType: StorageType.Double } band
            ? (int)Math.Round(band.AsDouble())
            : null;

    private static void SetNumber(Element element, string name, double value, List<string> readOnly)
    {
        if (element.LookupParameter(name) is not { } parameter)
        {
            return;
        }

        if (parameter.IsReadOnly || !parameter.Set(value))
        {
            readOnly.Add(name);
        }
    }

    private static void SetText(Element element, string name, string value, List<string> readOnly)
    {
        if (element.LookupParameter(name) is not { } parameter)
        {
            return;
        }

        if (parameter.IsReadOnly || !parameter.Set(value))
        {
            readOnly.Add(name);
        }
    }

    private static string FormatLocalTimestamp()
    {
        var now = DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;
        var zoneName = zone.IsDaylightSavingTime(now) ? zone.DaylightName : zone.StandardName;
        return $"{now.ToString("yyyy-MM-dd, HH:mm:ss", CultureInfo.InvariantCulture)}, {zoneName}";
    }
}
