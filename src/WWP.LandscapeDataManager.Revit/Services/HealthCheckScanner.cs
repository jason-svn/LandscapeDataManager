using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Floor half of the Diagnosis panel's Health Check tool (the Planting half is evaluated
/// client-side — see <see cref="HealthCheckResult"/>). Distinguishes a shared parameter that was
/// never bound to this project at all (run Shared Parameter Setup) from one that's bound but
/// simply hasn't been filled in yet (run Floor Calculator), so every red Floor points at the
/// actual next step instead of one generic "needs attention."
/// </summary>
internal static class HealthCheckScanner
{
    private const string PlantingSpeciesCodeParameter = "!_S_PLT_iTreeSpecies_Code_Text";
    private const string FloorTypeParameter = "!_S_PLT_LDS_Type_Text";
    private const string FloorResultSourceParameter = "!_S_PLT_LDS_ResultSource_Text";

    public static HealthCheckResult Scan(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before running the health check.");

        var plantingParametersBound = IsParameterBound(document, PlantingSpeciesCodeParameter);
        var floorParametersBound = IsParameterBound(document, FloorTypeParameter);

        var warnings = new List<string>();
        if (!plantingParametersBound)
        {
            warnings.Add("i-Tree shared parameters are not set up for Planting yet — run Shared Parameter Setup, then Load parameters and Apply.");
        }

        if (!floorParametersBound)
        {
            warnings.Add("WWP landscape data sheet shared parameters are not set up for Floors yet — run Shared Parameter Setup, then Load parameters and Apply.");
        }

        var floorItems = ScanFloors(document, floorParametersBound)
            .OrderBy(item => item.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HealthCheckResult(document.Title, floorItems, warnings);
    }

    private static IEnumerable<HealthCheckItem> ScanFloors(Document document, bool floorParametersBound)
    {
        var floors = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType()
            .ToElements();

        foreach (var floor in floors)
        {
            var (familyName, typeName) = GetFamilyAndTypeName(document, floor);

            if (!floorParametersBound)
            {
                yield return new HealthCheckItem(
                    floor.UniqueId, "Floor", familyName, typeName, "NeedsAttention",
                    "Shared parameters not set up (see warning above).");
                continue;
            }

            var resultSource = GetText(floor, FloorResultSourceParameter);
            if (resultSource.Length > 0)
            {
                yield return new HealthCheckItem(floor.UniqueId, "Floor", familyName, typeName, "Success", $"Calculated ({resultSource}).");
                continue;
            }

            var ldsType = GetText(floor, FloorTypeParameter);
            var reason = ldsType.Length == 0
                ? "No landscape type matched — open Floor Calculator and search/match a type."
                : "Landscape type matched but never calculated — open Floor Calculator and click Calculate.";
            yield return new HealthCheckItem(floor.UniqueId, "Floor", familyName, typeName, "NeedsAttention", reason);
        }
    }

    private static bool IsParameterBound(Document document, string parameterName)
    {
        var iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is InternalDefinition definition &&
                string.Equals(definition.Name, parameterName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static (string FamilyName, string TypeName) GetFamilyAndTypeName(Document document, Element element)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var familyName = elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty;
        var typeName = elementType?.Name ?? element.Name;
        return (familyName, typeName);
    }

    private static string GetText(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsString() ?? string.Empty : string.Empty;
    }
}
